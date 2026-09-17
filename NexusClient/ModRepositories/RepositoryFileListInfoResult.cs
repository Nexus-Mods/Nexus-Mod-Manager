namespace Nexus.Client.ModRepositories
{
	using System;
	using System.Collections.Generic;
	using Nexus.Client.Mods;

	/// <summary>
	/// Returns update-check metadata together with file-resolution checkpoints that may be published after local application succeeds.
	/// </summary>
	public sealed class RepositoryFileListInfoResult
	{
		/// <summary>
		/// Creates an aligned metadata/checkpoint result.
		/// </summary>
		public RepositoryFileListInfoResult(List<IModInfo> modInfo, List<RepositoryFileUpdateCheckpoint> checkpoints)
		{
			ModInfo = modInfo ?? throw new ArgumentNullException(nameof(modInfo));
			Checkpoints = checkpoints ?? throw new ArgumentNullException(nameof(checkpoints));

			if (ModInfo.Count != Checkpoints.Count)
				throw new ArgumentException("Metadata and checkpoint result counts must remain aligned.", nameof(checkpoints));
		}

		/// <summary>
		/// Gets the metadata rows in the same order as the request list.
		/// </summary>
		public List<IModInfo> ModInfo { get; }

		/// <summary>
		/// Gets the aligned checkpoint candidates; entries are null when no safe freshness checkpoint is available for that row.
		/// </summary>
		public List<RepositoryFileUpdateCheckpoint> Checkpoints { get; }
	}
}
