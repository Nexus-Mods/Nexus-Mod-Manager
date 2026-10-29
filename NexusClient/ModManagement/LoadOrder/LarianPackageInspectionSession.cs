using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Optional scan lifetime; cached scans need not start a helper until the first cache miss.</summary>
	internal interface ILoadOrderPackageInspectionBatch
	{
		/// <summary>Opens a provider-owned lifetime that must be disposed on completion or cancellation.</summary>
		IDisposable BeginBatch(IReadOnlyList<LoadOrderPackageFile> files);
	}

	/// <summary>Owns one read-only helper process and one observed inventory for a bounded package scan.</summary>
	internal sealed class LarianPackageInspectionSession : IDisposable
	{
		private const int MaximumFrameBytes = 16 * 1024 * 1024;
		private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
		private readonly string _executablePath;
		private readonly int _timeoutMilliseconds;
		private readonly IReadOnlyList<LoadOrderPackageFile> _files;
		private Dictionary<string, LoadOrderPackageFile> _byName;
		private Process _process;
		private Task<string> _errors;
		private string _failure;
		private bool _disposed;
		private bool _protocolConfirmed;
		private int _consecutiveFailures;

		/// <summary>Captures trusted configuration without starting a process or reading package contents.</summary>
		internal LarianPackageInspectionSession(string executablePath, int timeoutMilliseconds, IReadOnlyList<LoadOrderPackageFile> files)
		{ _executablePath = executablePath; _timeoutMilliseconds = timeoutMilliseconds; _files = files; }

		/// <summary>Shares one healthy process; isolated process failures permit a bounded restart without trusting partial responses.</summary>
		internal LoadOrderPackageInspection Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> files, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			if (_disposed) throw new ObjectDisposedException(nameof(LarianPackageInspectionSession));
			if (_failure != null) throw new IOException(_failure);
			if (!ReferenceEquals(files, _files)) throw new ArgumentException("The inspection inventory changed during its session.");
			try
			{
				var elapsed = Stopwatch.StartNew();
				string request; bool initialize = _process == null;
				if (_process == null)
				{
					request = LarianPackageInspectionProtocol.CreateRequest(file, files);
					_byName = files.ToDictionary(item => item.FileName, StringComparer.OrdinalIgnoreCase);
					_process = new Process { StartInfo = new ProcessStartInfo
					{
						FileName = _executablePath, Arguments = "--session-v1", WorkingDirectory = Path.GetDirectoryName(_executablePath),
						UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
						RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
						StandardErrorEncoding = Utf8
					} };
					if (!_process.Start()) throw new IOException("The package inspection helper could not be started.");
					_errors = DrainErrorsAsync(_process.StandardError); ObserveFault(_errors);
				}
				else
				{
					if (!_byName.TryGetValue(file.FileName, out LoadOrderPackageFile expected) || expected.Path != file.Path ||
						expected.Length != file.Length || expected.LastWriteTimeUtcTicks != file.LastWriteTimeUtcTicks)
						throw new InvalidDataException("The input is absent from its session inventory.");
					request = file.FileName;
				}
				if (initialize)
				{
					if (AwaitReply(ReadFrameAsync(_process.StandardOutput.BaseStream), elapsed, token) != "NMM-PAK-SESSION-1") throw new InvalidDataException("Unsupported inspection session protocol.");
					_protocolConfirmed = true;
				}
				string response = AwaitReply(ExchangeAsync(_process.StandardInput.BaseStream, _process.StandardOutput.BaseStream, request), elapsed, token);
				LoadOrderPackageInspection inspection = LarianPackageInspectionProtocol.ReadResponse(response, file.FileName);
				_consecutiveFailures = 0; return inspection;
			}
			catch (OperationCanceledException) { Stop(); throw; }
			catch (Exception error) when (error is IOException || error is Win32Exception || error is InvalidOperationException || error is ArgumentException)
			{
				string details = (_protocolConfirmed ? "The package inspection helper stopped while inspecting this package. " :
					"The package inspection session could not start. Install the matching NMM package-inspection helper, then refresh. ") + error.Message;
				Stop();
				if (_errors != null && _errors.Status == TaskStatus.RanToCompletion && !String.IsNullOrWhiteSpace(_errors.Result)) details += " " + _errors.Result;
				if (!_protocolConfirmed || ++_consecutiveFailures >= 2) _failure = details;
				_process?.Dispose(); _process = null; _protocolConfirmed = false;
				throw new IOException(details, error);
			}
		}

		/// <summary>Applies one shared deadline to startup, handshake and response, and preserves cancellation through blocking pipe reads.</summary>
		private string AwaitReply(Task<string> task, Stopwatch elapsed, CancellationToken token)
		{
			ObserveFault(task);
			int remaining = (int)Math.Max(0, _timeoutMilliseconds - elapsed.ElapsedMilliseconds);
			try { if (!task.Wait(remaining, token)) throw new IOException("The package inspection helper timed out."); }
			catch (AggregateException) { task.GetAwaiter().GetResult(); throw; }
			token.ThrowIfCancellationRequested(); return task.GetAwaiter().GetResult();
		}

		/// <summary>Observes pipe failures that complete after timeout or cancellation kills their process.</summary>
		private static void ObserveFault(Task task) => task.ContinueWith(failed => { Exception ignored = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);

		/// <summary>Writes a length-prefixed UTF-8 request and reads exactly one bounded response.</summary>
		private static async Task<string> ExchangeAsync(Stream input, Stream output, string request)
		{
			byte[] bytes = Utf8.GetBytes(request);
			if (bytes.Length == 0 || bytes.Length > MaximumFrameBytes) throw new InvalidDataException("Inspection request exceeds its frame limit.");
			byte[] header = { (byte)bytes.Length, (byte)(bytes.Length >> 8), (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 24) };
			await input.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
			await input.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
			await input.FlushAsync().ConfigureAwait(false);
			return await ReadFrameAsync(output).ConfigureAwait(false);
		}

		/// <summary>Bounds a single helper frame before decoding it or passing JSON to the existing validator.</summary>
		private static async Task<string> ReadFrameAsync(Stream output)
		{
			byte[] header = new byte[4];
			await ReadExactAsync(output, header).ConfigureAwait(false);
			int length = header[0] | header[1] << 8 | header[2] << 16 | header[3] << 24;
			if (length < 1 || length > MaximumFrameBytes) throw new InvalidDataException("Invalid inspection response frame length.");
			byte[] bytes = new byte[length]; await ReadExactAsync(output, bytes).ConfigureAwait(false);
			return Utf8.GetString(bytes);
		}

		/// <summary>Detects truncated responses without waiting for EOF after a successful frame.</summary>
		private static async Task ReadExactAsync(Stream stream, byte[] bytes)
		{
			int offset = 0;
			while (offset < bytes.Length)
			{
				int count = await stream.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
				if (count == 0) throw new EndOfStreamException("Truncated inspection response.");
				offset += count;
			}
		}

		/// <summary>Drains stderr throughout the session while retaining only a bounded diagnostic prefix.</summary>
		private static async Task<string> DrainErrorsAsync(StreamReader reader)
		{
			var text = new StringBuilder(); char[] buffer = new char[2048]; int count;
			while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
				if (text.Length < 8192) text.Append(buffer, 0, Math.Min(count, 8192 - text.Length));
			return text.ToString();
		}

		/// <summary>Terminates only the process owned by this scan, including blocked pipes on timeout.</summary>
		private void Stop()
		{
			if (_process == null) return;
			try { if (!_process.HasExited) _process.Kill(); }
			catch (Exception error) when (error is Win32Exception || error is InvalidOperationException) { }
		}

		/// <summary>Releases the scan process promptly, even when enumeration exits early or cancellation interrupts a frame.</summary>
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true; Stop(); _process?.Dispose();
		}
	}
}
