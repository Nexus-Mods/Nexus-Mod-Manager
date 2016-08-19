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
		/// This finds any mod in the candidate list that appears to be another version of the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to find another version.</param>
		/// <param name="p_booExistingOnly">Whether the matcher should only match candidate mods that exist.</param>
		/// <returns>The active mod that appears to be another version of the given mod,
		/// or <c>null</c> if no such mod was found.</returns>
		public IMod FindAlternateVersion(IMod p_modMod, bool p_booExistingOnly)
		{
			IEnumerable<IMod> lstMatches = from m in Candidates
										   where !String.IsNullOrEmpty(m.Id)
												&& m.Id.Equals(p_modMod.Id)
												&& m.DownloadId.Equals(p_modMod.DownloadId)
												&& !m.Filename.Equals(p_modMod.Filename, StringComparison.OrdinalIgnoreCase)
												&& (AssumeAllExist || !p_booExistingOnly || File.Exists(m.Filename))
										   select m;
			string strNewModName = p_modMod.ModName;
			if (String.IsNullOrEmpty(p_modMod.Id))
			{
				if (lstMatches.Count() == 0)
				{
					lstMatches = from m in Candidates
								 where m.ModName.Equals(strNewModName, StringComparison.InvariantCultureIgnoreCase)
									 && !m.Filename.Equals(p_modMod.Filename, StringComparison.OrdinalIgnoreCase)
									 && (AssumeAllExist || !p_booExistingOnly || File.Exists(m.Filename))
								 select m;
				}
				if (lstMatches.Count() == 0)
				{
					string strNewModNamePrefix = strNewModName.Split('-')[0].Trim();
					lstMatches = from m in Candidates
								 where m.ModName.Split('-')[0].Trim().Equals(strNewModNamePrefix, StringComparison.InvariantCultureIgnoreCase)
									 && !m.Filename.Equals(p_modMod.Filename, StringComparison.OrdinalIgnoreCase)
									 && (AssumeAllExist || !p_booExistingOnly || File.Exists(m.Filename))
								 select m;
				}
			}
			IMod modMatch = null;
			Int64 intFilesize = 0;
			foreach (IMod modCandidate in lstMatches)
			{
				if (File.Exists(modCandidate.Filename))
				{
					FileInfo fifInfo = new FileInfo(modCandidate.Filename);
					if (fifInfo.Length > intFilesize)
					{
						intFilesize = fifInfo.Length;
						modMatch = modCandidate;
					}
				}
			}
			if (modMatch == null)
				modMatch = lstMatches.FirstOrDefault();
			return modMatch;
		}
	}
}
