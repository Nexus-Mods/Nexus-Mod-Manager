namespace Nexus.Client.Mods.Formats.FOMod
{
	using System;

	/// <summary>
	/// Describes whether one persisted screenshot override belongs to the archive bytes currently present at its path.
	/// </summary>
	public enum FOModScreenshotOverrideReadState
	{
		None = 0,
		Current = 1,
		Stale = 2,
		ArchiveUnavailable = 3
	}

	/// <summary>
	/// Immutable logical export of one generated screenshot override from the shared FOMod metadata store.
	/// </summary>
	public sealed class FOModScreenshotOverrideRecord
	{
		private readonly byte[] _screenshotData;

		/// <summary>Creates one detached screenshot-override record.</summary>
		public FOModScreenshotOverrideRecord(long archiveLength, long archiveWriteTimeUtcTicks, string screenshotPath,
			byte[] screenshotData, long updatedUtcTicks)
		{
			if (archiveLength < 0)
				throw new ArgumentOutOfRangeException(nameof(archiveLength));
			if (String.IsNullOrWhiteSpace(screenshotPath))
				throw new ArgumentException("A screenshot override path is required.", nameof(screenshotPath));
			if (screenshotData == null || screenshotData.Length == 0)
				throw new ArgumentException("A screenshot override requires non-empty bytes.", nameof(screenshotData));
			ArchiveLength = archiveLength;
			ArchiveWriteTimeUtcTicks = archiveWriteTimeUtcTicks;
			ScreenshotPath = screenshotPath;
			_screenshotData = (byte[])screenshotData.Clone();
			UpdatedUtcTicks = updatedUtcTicks;
		}

		public long ArchiveLength { get; }
		public long ArchiveWriteTimeUtcTicks { get; }
		public string ScreenshotPath { get; }
		public byte[] ScreenshotData { get { return (byte[])_screenshotData.Clone(); } }
		public long UpdatedUtcTicks { get; }
	}

	/// <summary>
	/// Result of logically reading one archive's generated screenshot override.
	/// </summary>
	public sealed class FOModScreenshotOverrideReadResult
	{
		/// <summary>Creates one immutable screenshot-override read result.</summary>
		public FOModScreenshotOverrideReadResult(FOModScreenshotOverrideReadState state, FOModScreenshotOverrideRecord record)
		{
			if (!Enum.IsDefined(typeof(FOModScreenshotOverrideReadState), state))
				throw new ArgumentOutOfRangeException(nameof(state));
			if (state == FOModScreenshotOverrideReadState.None && record != null)
				throw new ArgumentException("A missing screenshot override cannot carry a persisted record.", nameof(record));
			if (state != FOModScreenshotOverrideReadState.None && record == null)
				throw new ArgumentNullException(nameof(record));
			State = state;
			Record = record;
		}

		public FOModScreenshotOverrideReadState State { get; }
		public FOModScreenshotOverrideRecord Record { get; }
	}

	/// <summary>
	/// Exposes selected non-rebuildable FOMod user metadata without exposing or snapshotting the shared SQLite database.
	/// </summary>
	/// <remarks>
	/// The reader is backed by the same live metadata-cache instance used by the FOMod format. Derived archive metadata remains
	/// deliberately outside this surface; callers can export only the generated screenshot override relevant to one archive.
	/// </remarks>
	public sealed class FOModUserMetadataReader
	{
		private readonly FOModArchiveMetadataCache _metadataCache;

		internal FOModUserMetadataReader(FOModArchiveMetadataCache metadataCache)
		{
			_metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
		}

		/// <summary>Gets whether the existing shared FOMod metadata store is usable.</summary>
		public bool IsUsable { get { return _metadataCache.IsUsable; } }

		/// <summary>Reads the logical screenshot override for one archive without mutating the shared store.</summary>
		public FOModScreenshotOverrideReadResult ReadScreenshotOverride(string archivePath)
		{
			return _metadataCache.ReadScreenshotOverride(archivePath);
		}
	}
}
