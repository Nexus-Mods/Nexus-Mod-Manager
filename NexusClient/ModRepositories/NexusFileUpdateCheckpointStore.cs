namespace Nexus.Client.ModRepositories
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Text;
	using Nexus.Client.ModManagement;
	using Nexus.Client.OnlineServices.Infrastructure;
	using Nexus.Client.Util;

	/// <summary>
	/// Persists public Nexus file-resolution results so unchanged provider file timestamps can avoid repeated REST file-list work.
	/// </summary>
	internal sealed class NexusFileUpdateCheckpointStore
	{
		private const int CurrentVersion = 1;
		private const string DatabaseFileName = "NexusFileUpdateCheckpoints.json";
		private readonly object _sync = new object();
		private readonly string _path;
		private bool _loaded;
		private List<CheckpointEntry> _entries = new List<CheckpointEntry>();

		/// <summary>
		/// Creates a disabled store when no install-info directory is available.
		/// </summary>
		public NexusFileUpdateCheckpointStore(string installInfoDirectory)
		{
			if (!string.IsNullOrWhiteSpace(installInfoDirectory))
				_path = Path.Combine(installInfoDirectory, DatabaseFileName);
		}

		/// <summary>
		/// Gets whether persistent checkpoint storage is available for this repository instance.
		/// </summary>
		public bool IsEnabled => !string.IsNullOrWhiteSpace(_path);

		/// <summary>
		/// Tries to reuse one resolution only when the public provider file timestamp and archive identity both match.
		/// </summary>
		public bool TryGet(string gameDomain, int modId, DateTimeOffset latestFileUpdateUtc, string downloadId, string currentFilename, out RepositoryFileUpdateCheckpoint checkpoint)
		{
			checkpoint = null;
			if (!IsEnabled || modId <= 0 || latestFileUpdateUtc == default(DateTimeOffset))
				return false;

			lock (_sync)
			{
				EnsureLoaded();
				DateTimeOffset freshnessUtc = latestFileUpdateUtc.ToUniversalTime();
				CheckpointEntry entry = _entries.FirstOrDefault(candidate =>
					SameMod(candidate, gameDomain, modId)
					&& candidate.LatestFileUpdateUtc.ToUniversalTime() == freshnessUtc
					&& MatchesRequestIdentity(candidate, downloadId, currentFilename));

				if (entry == null)
					return false;

				checkpoint = FromEntry(entry);
				return true;
			}
		}

		/// <summary>
		/// Publishes a complete set of successfully applied checkpoint candidates atomically.
		/// </summary>
		public bool Commit(IEnumerable<RepositoryFileUpdateCheckpoint> checkpoints)
		{
			if (!IsEnabled || checkpoints == null)
				return false;

			List<RepositoryFileUpdateCheckpoint> candidates = checkpoints.Where(candidate => candidate != null).ToList();
			if (candidates.Count == 0)
				return false;

			lock (_sync)
			{
				EnsureLoaded();
				foreach (RepositoryFileUpdateCheckpoint candidate in candidates)
				{
					DateTimeOffset freshnessUtc = candidate.LatestFileUpdateUtc.ToUniversalTime();
					_entries.RemoveAll(entry =>
						SameMod(entry, candidate.GameDomain, candidate.ModId)
						&& entry.LatestFileUpdateUtc.ToUniversalTime() != freshnessUtc);

					_entries.RemoveAll(entry =>
						SameMod(entry, candidate.GameDomain, candidate.ModId)
						&& entry.LatestFileUpdateUtc.ToUniversalTime() == freshnessUtc
						&& SameSourceIdentity(entry, candidate.SourceDownloadId, candidate.SourceFilename));

					_entries.Add(ToEntry(candidate));
				}

				Save();
				return true;
			}
		}

		private void EnsureLoaded()
		{
			if (_loaded)
				return;

			_loaded = true;
			if (!File.Exists(_path))
				return;

			try
			{
				CheckpointDocument document = ApiJson.Deserialize<CheckpointDocument>(File.ReadAllText(_path));
				if (document == null || document.Version != CurrentVersion || document.Entries == null)
				{
					Trace.TraceWarning("Ignoring incompatible Nexus file-update checkpoint data.");
					return;
				}

				_entries = document.Entries.Where(IsValidEntry).ToList();
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Ignoring unreadable Nexus file-update checkpoint data.");
				TraceUtil.TraceException(ex);
				_entries = new List<CheckpointEntry>();
			}
		}

		private void Save()
		{
			string directory = Path.GetDirectoryName(_path);
			if (string.IsNullOrWhiteSpace(directory))
				return;

			Directory.CreateDirectory(directory);
			string temporaryPath = _path + ".tmp";
			try
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);

				var document = new CheckpointDocument
				{
					Version = CurrentVersion,
					Entries = _entries.ToList()
				};
				File.WriteAllText(temporaryPath, ApiJson.Serialize(document), new UTF8Encoding(false));

				if (File.Exists(_path))
				{
					try
					{
						File.Replace(temporaryPath, _path, null);
					}
					catch (PlatformNotSupportedException)
					{
						File.Delete(_path);
						File.Move(temporaryPath, _path);
					}
				}
				else
				{
					File.Move(temporaryPath, _path);
				}
			}
			finally
			{
				if (File.Exists(temporaryPath))
					File.Delete(temporaryPath);
			}
		}

		private static bool IsValidEntry(CheckpointEntry entry)
		{
			return entry != null
				&& !string.IsNullOrWhiteSpace(entry.GameDomain)
				&& entry.ModId > 0
				&& entry.LatestFileUpdateUtc != default(DateTimeOffset);
		}

		private static bool SameMod(CheckpointEntry entry, string gameDomain, int modId)
		{
			return entry.ModId == modId
				&& string.Equals(entry.GameDomain, gameDomain, StringComparison.OrdinalIgnoreCase);
		}

		private static bool MatchesRequestIdentity(CheckpointEntry entry, string downloadId, string currentFilename)
		{
			if (ModFileIdentity.IsUsableRepositoryId(downloadId))
			{
				return string.Equals(entry.SourceDownloadId, downloadId, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(entry.ResolvedDownloadId, downloadId, StringComparison.OrdinalIgnoreCase);
			}

			if (string.IsNullOrWhiteSpace(currentFilename))
				return false;

			return string.Equals(entry.SourceFilename, currentFilename, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(entry.ResolvedFilename, currentFilename, StringComparison.OrdinalIgnoreCase);
		}

		private static bool SameSourceIdentity(CheckpointEntry entry, string sourceDownloadId, string sourceFilename)
		{
			if (ModFileIdentity.IsUsableRepositoryId(sourceDownloadId))
				return string.Equals(entry.SourceDownloadId, sourceDownloadId, StringComparison.OrdinalIgnoreCase);

			return !string.IsNullOrWhiteSpace(sourceFilename)
				&& string.Equals(entry.SourceFilename, sourceFilename, StringComparison.OrdinalIgnoreCase);
		}

		private static CheckpointEntry ToEntry(RepositoryFileUpdateCheckpoint checkpoint)
		{
			return new CheckpointEntry
			{
				GameDomain = checkpoint.GameDomain,
				ModId = checkpoint.ModId,
				LatestFileUpdateUtc = checkpoint.LatestFileUpdateUtc.ToUniversalTime(),
				SourceDownloadId = checkpoint.SourceDownloadId,
				SourceFilename = checkpoint.SourceFilename,
				ResolvedDownloadId = checkpoint.ResolvedDownloadId,
				ResolvedFilename = checkpoint.ResolvedFilename,
				ResolvedName = checkpoint.ResolvedName,
				HumanReadableVersion = checkpoint.HumanReadableVersion,
				FileMetadataResolved = checkpoint.FileMetadataResolved
			};
		}

		private static RepositoryFileUpdateCheckpoint FromEntry(CheckpointEntry entry)
		{
			return new RepositoryFileUpdateCheckpoint(
				entry.GameDomain,
				entry.ModId,
				entry.LatestFileUpdateUtc,
				entry.SourceDownloadId,
				entry.SourceFilename,
				entry.ResolvedDownloadId,
				entry.ResolvedFilename,
				entry.ResolvedName,
				entry.HumanReadableVersion,
				entry.FileMetadataResolved);
		}

		private sealed class CheckpointDocument
		{
			public CheckpointDocument()
			{
			}

			public int Version { get; set; }

			public List<CheckpointEntry> Entries { get; set; }
		}

		private sealed class CheckpointEntry
		{
			public CheckpointEntry()
			{
			}

			public string GameDomain { get; set; }

			public int ModId { get; set; }

			public DateTimeOffset LatestFileUpdateUtc { get; set; }

			public string SourceDownloadId { get; set; }

			public string SourceFilename { get; set; }

			public string ResolvedDownloadId { get; set; }

			public string ResolvedFilename { get; set; }

			public string ResolvedName { get; set; }

			public string HumanReadableVersion { get; set; }

			public bool FileMetadataResolved { get; set; }
		}
	}
}
