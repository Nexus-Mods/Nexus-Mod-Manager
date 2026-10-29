using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Bounded process boundary for a separately packaged reader; NMM remains on .NET Framework.</summary>
	public sealed class LarianPackageInspectorClient : ILoadOrderPackageInspector, ILoadOrderPackageInspectionBatch
	{
		private readonly string _executablePath;
		private readonly int _timeoutMilliseconds;
		private LarianPackageInspectionSession _batch;

		/// <summary>Shares one lazily started helper within a scan; independent callers retain the legacy one-shot protocol.</summary>
		IDisposable ILoadOrderPackageInspectionBatch.BeginBatch(IReadOnlyList<LoadOrderPackageFile> files)
		{
			if (_batch != null) throw new InvalidOperationException("An inspection batch is already active.");
			_batch = new LarianPackageInspectionSession(_executablePath, _timeoutMilliseconds, files);
			return new BatchScope(this);
		}

		/// <summary>Restores one-shot behavior after the provider disposes its scan lifetime.</summary>
		private sealed class BatchScope : IDisposable
		{
			private LarianPackageInspectorClient _owner;
			/// <summary>Captures the client whose scan is ending.</summary>
			internal BatchScope(LarianPackageInspectorClient owner) { _owner = owner; }
			/// <summary>Restores the client before releasing process resources.</summary>
			public void Dispose()
			{
				if (_owner == null) return;
				LarianPackageInspectionSession batch = _owner._batch; _owner._batch = null; _owner = null; batch.Dispose();
			}
		}

		/// <summary>Captures a trusted application-owned helper path, never an executable selected by game.json or a package.</summary>
		public LarianPackageInspectorClient(string executablePath, int timeoutMilliseconds = 60000)
		{
			if (String.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath))
				throw new ArgumentException("An absolute helper path is required.", nameof(executablePath));
			if (timeoutMilliseconds < 1 || timeoutMilliseconds > 300000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
			_executablePath = Path.GetFullPath(executablePath); _timeoutMilliseconds = timeoutMilliseconds;
		}
		public bool IsAvailable => File.Exists(_executablePath);

		/// <summary>Exchanges one JSON document through redirected pipes; cancellation and timeouts terminate this helper only.</summary>
		public LoadOrderPackageInspection Inspect(LoadOrderPackageFile file, IReadOnlyList<LoadOrderPackageFile> availableFiles, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_batch != null) return _batch.Inspect(file, availableFiles, cancellationToken);
			if (!IsAvailable)
				return new LoadOrderPackageInspection(LoadOrderInspectionStatus.Unavailable, LoadOrderPackageKind.Unknown, null, LoadOrderContentFeatures.None, null);
			string request = LarianPackageInspectionProtocol.CreateRequest(file, availableFiles);
			using (var process = new Process())
			{
				process.StartInfo = new ProcessStartInfo
				{
					FileName = _executablePath, WorkingDirectory = Path.GetDirectoryName(_executablePath),
					UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
					RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
					StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
				};
				bool started = false;
				try
				{
					started = process.Start();
					if (!started) throw new IOException("The package inspection helper could not be started.");
					var elapsed = Stopwatch.StartNew();
					Task<string> output = ReadBoundedAsync(process.StandardOutput, LarianPackageInspectionProtocol.MaximumResponseCharacters, false);
					Task<string> errors = ReadBoundedAsync(process.StandardError, 8192, true);
					Task input = Task.Run(() =>
					{
						// BaseStream avoids the machine's console encoding for non-ASCII paths.
						using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) writer.Write(request);
					});
					Task pipes = Task.WhenAll(input, output, errors);
					while (!process.WaitForExit(50))
					{
						cancellationToken.ThrowIfCancellationRequested();
						if (input.IsFaulted) input.GetAwaiter().GetResult();
						if (output.IsFaulted) output.GetAwaiter().GetResult();
						if (errors.IsFaulted) errors.GetAwaiter().GetResult();
						if (elapsed.ElapsedMilliseconds >= _timeoutMilliseconds) throw new IOException("The package inspection helper timed out.");
					}
					int remaining = Math.Max(0, _timeoutMilliseconds - (int)elapsed.ElapsedMilliseconds);
					try
					{
						if (!pipes.Wait(remaining, cancellationToken)) throw new IOException("The package inspection helper did not close its output pipes.");
					}
					catch (AggregateException) { pipes.GetAwaiter().GetResult(); throw; }
					cancellationToken.ThrowIfCancellationRequested();
					if (process.ExitCode != 0) throw new IOException("The package inspection helper failed: " + errors.Result);
					return LarianPackageInspectionProtocol.ReadResponse(output.Result, file.FileName);
				}
				catch (Exception error) when (error is Win32Exception || error is InvalidOperationException || error is DecoderFallbackException)
				{
					throw new IOException("The package inspection helper failed.", error);
				}
				finally
				{
					if (started)
					{
						try { if (!process.HasExited) process.Kill(); }
						catch (Exception error) when (error is Win32Exception || error is InvalidOperationException) { }
					}
				}
			}
		}

		/// <summary>Drains both pipes concurrently; stderr is truncated and oversized stdout fails the inspection.</summary>
		private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, bool truncate)
		{
			var result = new StringBuilder();
			char[] buffer = new char[4096];
			int count;
			while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
			{
				int remaining = maximum - result.Length;
				if (count > remaining && !truncate) throw new InvalidDataException("The package inspection helper returned oversized output.");
				if (remaining > 0) result.Append(buffer, 0, Math.Min(remaining, count));
			}
			return result.ToString();
		}
	}
}
