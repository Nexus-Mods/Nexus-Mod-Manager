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
		/// <param name="mod">The mod for which to retrieve a list of possible tags.</param>
		/// <returns>A list of possible mod info tags which match the given mod.</returns>
		public IEnumerable<IModInfo> GetTagInfoCandidates(IMod mod)
		{
			//get mod info
			var mods = new List<IModInfo>();
			IModInfo modInfo = null;

            try
			{
				if (!string.IsNullOrEmpty(mod.Id))
                {
                    modInfo = ModRepository.GetModInfo(mod.Id);
                }

                if (modInfo == null)
                {
                    modInfo = ModRepository.GetModInfoForFile(mod.Filename);
                }
				
				mods.Add(modInfo);

                // If we don't know the mod Id, then we have no way of getting the
                // file-specific info, so only look if we have one mod info candidate.
                if (mods.Count == 1)
				{
					modInfo = mods[0];
					mods.Clear();
					
					//get file specific info
                    var mfiFileInfo = ModRepository.GetModFileInfoForFile(mod.Filename);

                    if (mfiFileInfo == null)
					{
						foreach (var mfiModFileInfo in ModRepository.GetModFileInfo(modInfo.Id))
                        {
                            mods.Add(CombineInfo(modInfo, mfiModFileInfo));
                        }
                    }
					else
                    {
                        mods.Add(CombineInfo(modInfo, mfiFileInfo));
                    }

                    if (mods.Count == 0)
                    {
                        mods.Add(modInfo);
                    }
                }
			}
			catch (NullReferenceException e)
			{
				TraceUtil.TraceException(e);
				
				//couldn't find any match, so add a dummy value indicating such
				mods.Add(new ModInfo(null, $"{e.Message}", null, null, null, null, false, null, null, 0, -1, null, null, null, null, true, true));
			}

			return mods;
		}

		/// <summary>
		/// Combines the given mod info and mod file info into one mod info.
		/// </summary>
		/// <param name="p_mifInfo">The mod info to combine.</param>
		/// <param name="p_mfiFileInfo">The mod file info to combine.</param>
		/// <returns>A mid info representing the information from both given info objects.</returns>
		public static IModInfo CombineInfo(IModInfo p_mifInfo, IModFileInfo p_mfiFileInfo)
		{
			var intLineTracker = 0;
			ModInfo mifUpdatedInfo;

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
					if (!String.IsNullOrEmpty(p_mfiFileInfo.Id))
						mifUpdatedInfo.DownloadId = p_mfiFileInfo.Id;
					
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
