namespace Nexus.Client.Games
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;
	using Nexus.Client.BackgroundTasks;

	/// <summary>
	/// A whole-fixed-drive scan. It is invoked only by the explicit Game Discovery
	/// Scan command, not by launcher refresh, startup or catalog construction.
	/// Results are verified candidate roots; it never accepts a game, creates
	/// storage, or writes settings.
	/// </summary>
	public sealed class GameDiscoveryRecursiveScanner
	{
		public Task<GameDiscoveryScanResult> ScanAsync(IEnumerable<IGameModeDescriptor> supportedModes,
			IProgress<string> progress, CancellationToken cancellationToken)
		{
			if (supportedModes == null) throw new ArgumentNullException(nameof(supportedModes));
			if (cancellationToken.IsCancellationRequested)
				return Task.FromCanceled<GameDiscoveryScanResult>(cancellationToken);

			var matcher = new GameDiscoveryScanCandidateMatcher(supportedModes);
			if (matcher.SearchFileNames.Count == 0)
				return Task.FromResult(new GameDiscoveryScanResult(Array.Empty<GameDiscoveryInstallationCandidate>(), false));

			// A new scanner and its event handlers belong to exactly one scan. In
			// particular, neither this task nor its cancellation registration is
			// reused for another search after Stop.
			var searcher = new SafeGameDiscoveryFileSearcher();
			var completion = new TaskCompletionSource<GameDiscoveryScanResult>(
				TaskCreationOptions.RunContinuationsAsynchronously);
			var candidates = new List<GameDiscoveryInstallationCandidate>();
			EventHandler<Nexus.Client.Util.EventArgs<string>> fileFound = (sender, args) =>
				matcher.TryAddFile(args.Argument, candidates);
			PropertyChangedEventHandler changed = (sender, args) =>
			{
				if (args.PropertyName == nameof(FileSearcher.OverallMessage) && !cancellationToken.IsCancellationRequested)
					progress?.Report(searcher.OverallMessage);
			};
			CancellationTokenRegistration registration = default(CancellationTokenRegistration);
			EventHandler<TaskEndedEventArgs> ended = null;
			ended = (sender, args) =>
			{
				searcher.FileFound -= fileFound;
				searcher.PropertyChanged -= changed;
				searcher.TaskEnded -= ended;
				registration.Dispose();
				if (searcher.Failure != null)
					completion.TrySetException(searcher.Failure);
				else
					completion.TrySetResult(new GameDiscoveryScanResult(Array.AsReadOnly(candidates.ToArray()),
						args.Status == Nexus.Client.BackgroundTasks.TaskStatus.Cancelled || cancellationToken.IsCancellationRequested));
			};
			searcher.FileFound += fileFound;
			searcher.PropertyChanged += changed;
			searcher.TaskEnded += ended;
			registration = cancellationToken.Register(() => searcher.Cancel());
			try
			{
				searcher.FindInBackground(matcher.SearchFileNames.ToArray());
			}
			catch
			{
				searcher.FileFound -= fileFound;
				searcher.PropertyChanged -= changed;
				searcher.TaskEnded -= ended;
				registration.Dispose();
				throw;
			}
			return completion.Task;
		}

		// ThreadedBackgroundTask rethrows uncaught exceptions on its worker, which
		// could terminate NMM. For this opt-in scan, capture/report recoverable
		// filesystem failures through the awaited result instead.
		private sealed class SafeGameDiscoveryFileSearcher : FileSearcher
		{
			internal Exception Failure { get; private set; }
			protected override object DoWork(object[] args)
			{
				try { return base.DoWork(args); }
				catch (Exception error) when (!(error is OutOfMemoryException))
				{
					Failure = error;
					return null;
				}
			}
		}
	}

	public sealed class GameDiscoveryScanResult
	{
		internal GameDiscoveryScanResult(IReadOnlyList<GameDiscoveryInstallationCandidate> candidates, bool cancelled)
		{
			Candidates = candidates;
			Cancelled = cancelled;
		}
		public IReadOnlyList<GameDiscoveryInstallationCandidate> Candidates { get; }
		public bool Cancelled { get; }
	}

	/// <summary>Pure candidate matching reused by the real scanner and fixture tests.</summary>
	internal sealed class GameDiscoveryScanCandidateMatcher
	{
		private readonly Dictionary<string, List<ExecutableMatch>> _byFileName =
			new Dictionary<string, List<ExecutableMatch>>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		internal IReadOnlyList<string> SearchFileNames { get; }

		internal GameDiscoveryScanCandidateMatcher(IEnumerable<IGameModeDescriptor> descriptors)
		{
			foreach (IGameModeDescriptor descriptor in descriptors)
			{
				if (descriptor == null || String.IsNullOrWhiteSpace(descriptor.ModeId)) continue;
				foreach (string relativePath in descriptor.GameExecutables ?? Array.Empty<string>())
				{
					if (String.IsNullOrWhiteSpace(relativePath)) continue;
					string normalized = relativePath.Replace('\\', '/');
					string fileName = normalized.Substring(normalized.LastIndexOf('/') + 1);
					if (String.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
						continue;
					List<ExecutableMatch> matches;
					if (!_byFileName.TryGetValue(fileName, out matches))
						_byFileName[fileName] = matches = new List<ExecutableMatch>();
					matches.Add(new ExecutableMatch(descriptor, relativePath));
				}
			}
			SearchFileNames = _byFileName.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
		}

		internal void TryAddFile(string executablePath, ICollection<GameDiscoveryInstallationCandidate> candidates)
		{
			if (String.IsNullOrWhiteSpace(executablePath)) return;
			List<ExecutableMatch> matches;
			if (!_byFileName.TryGetValue(Path.GetFileName(executablePath), out matches)) return;
			foreach (ExecutableMatch match in matches)
			{
				try
				{
					string root;
					if (!GameInstallationCandidatePath.TryResolveGameRoot(executablePath, match.RelativePath, out root) ||
						!QuickGameDiscovery.IsVerifiedGameRoot(match.Descriptor, root)) continue;
					root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
					if (_seen.Add(match.Descriptor.ModeId + "\0" + root))
						candidates.Add(new GameDiscoveryInstallationCandidate(match.Descriptor.ModeId, root, "Drive scan"));
				}
				catch (Exception error) when (!(error is OutOfMemoryException))
				{
					Trace.TraceWarning("Game scan rejected a candidate for {0}: {1}", match.Descriptor.ModeId, error.Message);
				}
			}
		}

		private sealed class ExecutableMatch
		{
			internal ExecutableMatch(IGameModeDescriptor descriptor, string relativePath)
			{ Descriptor = descriptor; RelativePath = relativePath; }
			internal IGameModeDescriptor Descriptor { get; }
			internal string RelativePath { get; }
		}
	}

	/// <summary>Merge read-only detection evidence without overwriting saved paths.</summary>
	internal static class GameDiscoveryCandidateMerger
	{
		internal static IReadOnlyList<GameDiscoveryInstallationCandidate> Merge(
			IEnumerable<GameDiscoveryInstallationCandidate> existing,
			IEnumerable<GameDiscoveryInstallationCandidate> incoming)
		{
			var result = new List<GameDiscoveryInstallationCandidate>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (GameDiscoveryInstallationCandidate candidate in
				(existing ?? Enumerable.Empty<GameDiscoveryInstallationCandidate>()).Concat(
					incoming ?? Enumerable.Empty<GameDiscoveryInstallationCandidate>()))
			{
				if (candidate == null || String.IsNullOrWhiteSpace(candidate.GameRoot)) continue;
				string key = candidate.ModeId + "\0" + candidate.GameRoot.TrimEnd('\\', '/');
				if (seen.Add(key)) result.Add(candidate);
			}
			return result.AsReadOnly();
		}
	}
}
