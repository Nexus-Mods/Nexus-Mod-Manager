using System;
using System.Diagnostics;
using System.Threading;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Reports throttled preparation activity only to the asynchronous operation that requested it.</summary>
	internal static class CollectionPreparationProgress
	{
		private static readonly AsyncLocal<ProgressScope> Current = new AsyncLocal<ProgressScope>();

		/// <summary>Captures progress for subsequent task work without sharing UI state across Collection operations.</summary>
		internal static IDisposable Begin(IProgress<string> progress)
		{
			var scope = new ProgressScope(progress, Current.Value);
			Current.Value = scope;
			return scope;
		}

		/// <summary>Names the current member or file; frequent file changes are throttled to avoid flooding the UI.</summary>
		internal static void Report(string status, bool force = false)
		{
			Current.Value?.Report(status, force);
		}

		/// <summary>Adds actual bytes read to the current stage at most twice per second.</summary>
		internal static void ReportBytes(long completed, long total)
		{
			Current.Value?.ReportBytes(completed, total);
		}

		/// <summary>Distinguishes an actual hashing pass from metadata-only archive reuse.</summary>
		internal static void ReportArchiveHashing(bool calculateMd5)
		{
			Current.Value?.ReportArchiveHashing(calculateMd5);
		}

		/// <summary>Names the archive being checked so byte progress cannot be mistaken for installation-plan size.</summary>
		internal static void ReportArchiveVerification(string path, bool reused)
		{
			Report(LanguageManager.Format(reused ? "Collections.Workflow.ReusingArchiveHash" : "Collections.Workflow.VerifyingArchive",
				reused ? "Reusing saved archive hash: {0}" : "Checking archive: {0}", System.IO.Path.GetFileName(path)), !reused);
		}

		/// <summary>Keeps progress attached to its operation when native extraction writes from a dedicated archive thread.</summary>
		internal static Action<long, long> CaptureByteReporter()
		{
			ProgressScope scope = Current.Value;
			return scope == null ? null : new Action<long, long>(scope.ReportBytes);
		}

		/// <summary>Keeps extraction status attached to the caller's Collection operation when native callbacks use another thread.</summary>
		internal static Action<string> CaptureStatusReporter()
		{
			ProgressScope scope = Current.Value;
			return scope == null ? null : new Action<string>(status => scope.Report(status, false));
		}

		/// <summary>Retains an operation's immutable progress destination and bounded reporting cadence.</summary>
		private sealed class ProgressScope : IDisposable
		{
			private readonly IProgress<string> _progress;
			private readonly ProgressScope _previous;
			private string _status;
			private string _hashAlgorithms;
			private long _lastReport;

			/// <summary>Captures the reporting destination and the caller's previous context.</summary>
			internal ProgressScope(IProgress<string> progress, ProgressScope previous)
			{
				_progress = progress;
				_previous = previous;
			}

			/// <summary>Updates the stage even when the UI update is deferred.</summary>
			internal void Report(string status, bool force)
			{
				_status = status;
				_hashAlgorithms = null;
				if (_progress != null && (force || IsDue()))
				{
					_lastReport = Stopwatch.GetTimestamp();
					_progress.Report(status);
				}
			}

			/// <summary>Shows which digests are being calculated while keeping the member's progress context.</summary>
			internal void ReportArchiveHashing(bool calculateMd5)
			{
				_hashAlgorithms = calculateMd5 ? "SHA-256 + MD5" : "SHA-256";
				if (_progress == null || String.IsNullOrWhiteSpace(_status)) return;
				_lastReport = Stopwatch.GetTimestamp();
				_progress.Report(LanguageManager.Format("Collections.Workflow.ArchiveHashAlgorithms", "{0} [{1}]", _status, _hashAlgorithms));
			}

			/// <summary>Formats measured byte progress without estimating download or decompression speed.</summary>
			internal void ReportBytes(long completed, long total)
			{
				if (_progress == null || String.IsNullOrWhiteSpace(_status) || !IsDue()) return;
				_lastReport = Stopwatch.GetTimestamp();
				string status = _hashAlgorithms == null ? _status : LanguageManager.Format(
					"Collections.Workflow.ArchiveHashAlgorithms", "{0} [{1}]", _status, _hashAlgorithms);
				_progress.Report(total > 0
					? LanguageManager.Format("Collections.Workflow.PreparationBytes", "{0} ({1:N0} / {2:N0} MB)",
						status, completed / (1024.0 * 1024.0), total / (1024.0 * 1024.0))
					: LanguageManager.Format("Collections.Workflow.PreparationBytesRead", "{0} ({1:N0} MB read)",
						status, completed / (1024.0 * 1024.0)));
			}

			/// <summary>Uses a monotonic clock so reporting cannot slow down per-buffer archive work.</summary>
			private bool IsDue()
			{
				return _lastReport == 0 || Stopwatch.GetTimestamp() - _lastReport >= Stopwatch.Frequency / 2;
			}

			/// <summary>Restores the caller's context; task execution retains its captured scope independently.</summary>
			public void Dispose()
			{
				Current.Value = _previous;
			}
		}
	}
}
