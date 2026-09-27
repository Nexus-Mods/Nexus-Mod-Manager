using System;

namespace Nexus.Client.Mods.Formats.FOMod
{
	/// <summary>Restores selected non-rebuildable FOMod user metadata without exposing or replacing the shared SQLite database.</summary>
	public sealed class FOModUserMetadataWriter
	{
		private readonly FOModArchiveMetadataCache _metadataCache;

		internal FOModUserMetadataWriter(FOModArchiveMetadataCache metadataCache)
		{
			_metadataCache = metadataCache ?? throw new ArgumentNullException(nameof(metadataCache));
		}

		/// <summary>Gets whether the existing shared FOMod metadata store is usable.</summary>
		public bool IsUsable { get { return _metadataCache.IsUsable; } }

		/// <summary>Durably restores one screenshot override against the archive fingerprint currently present at its path.</summary>
		public void RestoreScreenshotOverride(string archivePath, string screenshotPath, byte[] screenshotData, long updatedUtcTicks)
		{
			_metadataCache.RestoreScreenshotOverride(archivePath, screenshotPath, screenshotData, updatedUtcTicks);
		}

		/// <summary>Durably removes any screenshot override row for the specified archive path.</summary>
		public void RemoveScreenshotOverride(string archivePath)
		{
			_metadataCache.RemoveScreenshotOverride(archivePath);
		}
	}
}
