using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using System.Diagnostics;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Tags mods with metadata retrieved from a mod repository.
	/// </summary>
	public class AutoTagger
	{
		#region Properties

		/// <summary>
		/// Gets the mod repository from which to get mod info.
		/// </summary>
		/// <value>The mod repository from which to get mod info.</value>
		protected IModRepository ModRepository { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object wiht the given values.
		/// </summary>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		public AutoTagger(IModRepository p_mrpModRepository)
		{
			ModRepository = p_mrpModRepository;
		}

		#endregion

		/// <summary>
		/// Gets a list of possible mod info tags which match the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to retrieve a list of possible tags.</param>
		/// <returns>A list of possible mod info tags which match the given mod.</returns>
		public IEnumerable<IModInfo> GetTagInfoCandidates(IMod p_modMod)
		{
			//get mod info
			List<IModInfo> lstMods = new List<IModInfo>();
			IModInfo mifInfo = null;
			try
			{
				if (!String.IsNullOrEmpty(p_modMod.Id))
					mifInfo = ModRepository.GetModInfo(p_modMod.Id);
				if (mifInfo == null)
					mifInfo = ModRepository.GetModInfoForFile(p_modMod.Filename);
				if (mifInfo == null)
				{
					//use heuristics to find info
					lstMods.AddRange(ModRepository.FindMods(p_modMod.ModName, true));
                    if (lstMods.Count == 0)
                        lstMods.AddRange(ModRepository.FindMods(Regex.Replace(p_modMod.ModName, "[^a-zA-Z0-9_. ]+", "", RegexOptions.Compiled), true));
                    if (lstMods.Count == 0)
                        lstMods.AddRange(ModRepository.FindMods(p_modMod.ModName, p_modMod.Author));
					if (lstMods.Count == 0)
						lstMods.AddRange(ModRepository.FindMods(p_modMod.ModName, false));
				}
				else
					lstMods.Add(mifInfo);

				//if we don't know the mod Id, then we have no way of getting
				// the file-specific info, so only look if we have one mod info
				// candidate.
				if (lstMods.Count == 1)
				{
					mifInfo = lstMods[0];
					lstMods.Clear();
					//get file specific info
					IModFileInfo mfiFileInfo = ModRepository.GetFileInfoForFile(p_modMod.Filename);
					if (mfiFileInfo == null)
					{
						foreach (IModFileInfo mfiModFileInfo in ModRepository.GetModFileInfo(mifInfo.Id))
							lstMods.Add(CombineInfo(mifInfo, mfiModFileInfo));
					}
					else
						lstMods.Add(CombineInfo(mifInfo, mfiFileInfo));
					if (lstMods.Count == 0)
						lstMods.Add(mifInfo);
				}
			}
			catch (RepositoryUnavailableException e)
			{
				TraceUtil.TraceException(e);
				//the repository is not available, so add a dummy value indicating such
				lstMods.Add(new ModInfo(null, String.Format("{0} is unavailable", ModRepository.Name), null, null, false, null, null, null, null, null, null));
			}
			return lstMods;
		}

		/// <summary>
		/// Combines the given mod info and mod file info into one mod info.
		/// </summary>
		/// <param name="p_mifInfo">The mod info to combine.</param>
		/// <param name="p_mfiFileInfo">The mod file info to combine.</param>
		/// <returns>A mid info representing the information from both given info objects.</returns>
		public static IModInfo CombineInfo(IModInfo p_mifInfo, IModFileInfo p_mfiFileInfo)
		{
			Int32 intLineTracker = 0;
			ModInfo mifUpdatedInfo = null;
			try
			{
				if (p_mifInfo == null)
				{
					intLineTracker = 1;
					if (p_mfiFileInfo == null)
						return null;
					intLineTracker = 2;
					mifUpdatedInfo = new ModInfo();
					intLineTracker = 3;
				}
				else
				{
					intLineTracker = 4;
					mifUpdatedInfo = new ModInfo(p_mifInfo);
					intLineTracker = 5;
				}
				intLineTracker = 6;
				if (p_mfiFileInfo != null)
				{
					intLineTracker = 7;
					if (!String.IsNullOrEmpty(p_mfiFileInfo.HumanReadableVersion))
					{
						intLineTracker = 8;
						mifUpdatedInfo.HumanReadableVersion = p_mfiFileInfo.HumanReadableVersion;
						intLineTracker = 9;
						mifUpdatedInfo.MachineVersion = null;
						intLineTracker = 10;
					}
					intLineTracker = 11;
					if (!String.IsNullOrEmpty(p_mfiFileInfo.Name))
					{
						intLineTracker = 12;
						mifUpdatedInfo.ModName = String.Format("{0} - {1}", mifUpdatedInfo.ModName, p_mfiFileInfo.Name);
						intLineTracker = 13;
					}
					intLineTracker = 14;
				}
				intLineTracker = 15;
			}
			catch (NullReferenceException)
			{
				Trace.TraceError("NullReferenceException in CombineInfo: LineTracker: {0}", intLineTracker);
				throw;
			}
			return mifUpdatedInfo;
		}

		/// <summary>
		/// Tags the mod with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod the tag.</param>
		/// <param name="p_mifModInfo">The values with which to tag the mod.</param>
		/// <param name="p_booOverwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		public void Tag(IMod p_modMod, IModInfo p_mifModInfo, bool p_booOverwriteAllValues)
		{
			p_modMod.UpdateInfo(p_mifModInfo, p_booOverwriteAllValues);
		}
	}
}
