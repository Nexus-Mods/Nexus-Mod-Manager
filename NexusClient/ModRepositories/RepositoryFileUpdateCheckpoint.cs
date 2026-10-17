namespace Nexus.Client.ModRepositories
{
	using System;

	/// <summary>
	/// Describes one successfully resolved Nexus file state that can be reused while the provider file-update timestamp is unchanged.
	/// </summary>
	public sealed class RepositoryFileUpdateCheckpoint
	{
		/// <summary>
		/// Creates one immutable file-resolution checkpoint.
		/// </summary>
		public RepositoryFileUpdateCheckpoint(string gameDomain, int modId, DateTimeOffset latestFileUpdateUtc,
			string sourceDownloadId, string sourceFilename, string resolvedDownloadId, string resolvedFilename,
			string resolvedName, string humanReadableVersion, bool fileMetadataResolved)
			: this(gameDomain, modId, latestFileUpdateUtc, sourceDownloadId, sourceFilename, resolvedDownloadId,
				resolvedFilename, resolvedName, humanReadableVersion, fileMetadataResolved, default(DateTimeOffset))
		{
		}

		/// <summary>
		/// Creates a file-resolution checkpoint with the time of its last direct verification.
		/// </summary>
		public RepositoryFileUpdateCheckpoint(
			string gameDomain,
			int modId,
			DateTimeOffset latestFileUpdateUtc,
			string sourceDownloadId,
			string sourceFilename,
			string resolvedDownloadId,
			string resolvedFilename,
			string resolvedName,
			string humanReadableVersion,
			bool fileMetadataResolved, DateTimeOffset verifiedUtc)
		{
			GameDomain = gameDomain ?? string.Empty;
			ModId = modId;
			LatestFileUpdateUtc = latestFileUpdateUtc.ToUniversalTime();
			SourceDownloadId = sourceDownloadId;
			SourceFilename = sourceFilename;
			ResolvedDownloadId = resolvedDownloadId;
			ResolvedFilename = resolvedFilename;
			ResolvedName = resolvedName;
			HumanReadableVersion = humanReadableVersion;
			FileMetadataResolved = fileMetadataResolved;
			VerifiedUtc = verifiedUtc.ToUniversalTime();
		}

		public string GameDomain { get; }

		public int ModId { get; }

		public DateTimeOffset LatestFileUpdateUtc { get; }

		public string SourceDownloadId { get; }

		public string SourceFilename { get; }

		public string ResolvedDownloadId { get; }

		public string ResolvedFilename { get; }

		public string ResolvedName { get; }

		public string HumanReadableVersion { get; }

		public bool FileMetadataResolved { get; }

		/// <summary>
		/// Gets the conservative time of the last direct file resolution; cache reuse never advances it.
		/// </summary>
		public DateTimeOffset VerifiedUtc { get; }
	}
}
