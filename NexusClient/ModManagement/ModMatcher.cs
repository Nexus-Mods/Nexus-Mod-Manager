using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Mods;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Finds matches for a given mod in a given set of mods.
	/// </summary>
	public class ModMatcher
	{
		#region Properties

		/// <summary>
		/// Gets the set of candidate mods against which to match the given mod.
		/// </summary>
		/// <value>The set of candidate mods against which to match the given mod.</value>
		protected IEnumerable<IMod> Candidates { get; private set; }

		/// <summary>
		/// Gets whether to assume all of the given candidates exist.
		/// </summary>
		/// <value>Whether to assume all of the given candidates exist.</value>
		protected bool AssumeAllExist { get; private set; }

		#endregion

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_enmModCandidates">The set of candidate mods against which to match the given mod.</param>
		/// <param name="p_booAssumeAllExist">Whether to assume all of the given candidates exist.</param>
		public ModMatcher(IEnumerable<IMod> p_enmModCandidates, bool p_booAssumeAllExist)
		{
			Candidates = p_enmModCandidates;
			AssumeAllExist = p_booAssumeAllExist;
		}

		/// <summary>
		/// This finds any mod in the candidate list that is a different known version of the same repository file.
		/// </summary>
		/// <param name="p_modMod">The mod for which to find another version.</param>
		/// <param name="p_booExistingOnly">Whether the matcher should only match candidate mods that exist.</param>
		/// <returns>The active mod that is another known version of the same repository file,
		/// or <c>null</c> if no such mod was found.</returns>
		public IMod FindAlternateVersion(IMod p_modMod, bool p_booExistingOnly)
		{
			if (p_modMod == null || Candidates == null)
				return null;

			IEnumerable<IMod> matches = Candidates.Where(candidate =>
				candidate != null
				&& ModFileIdentity.IsSameRepositoryFile(candidate.Id, candidate.DownloadId, p_modMod.Id, p_modMod.DownloadId)
				&& ModFileIdentity.HasDifferentKnownVersion(candidate.HumanReadableVersion, p_modMod.HumanReadableVersion)
				&& !string.Equals(candidate.Filename, p_modMod.Filename, StringComparison.OrdinalIgnoreCase)
				&& (AssumeAllExist || !p_booExistingOnly || File.Exists(candidate.Filename)));

			IMod match = null;
			long largestFileSize = 0;
			foreach (IMod candidate in matches)
			{
				if (!File.Exists(candidate.Filename))
					continue;

				FileInfo fileInfo = new FileInfo(candidate.Filename);
				if (fileInfo.Length <= largestFileSize)
					continue;

				largestFileSize = fileInfo.Length;
				match = candidate;
			}

			return match ?? matches.FirstOrDefault();
		}
	}
}
