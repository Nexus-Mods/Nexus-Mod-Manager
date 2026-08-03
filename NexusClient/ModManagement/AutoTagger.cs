using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
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

		/// <summary>
		/// Gets the Nexus game domain used by the backing repository.
		/// </summary>
		public string GameDomainName
		{
			get { return ModRepository == null ? String.Empty : ModRepository.GameDomainName; }
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_mrpModRepository">
		/// The mod repository from which to get mods and mod metadata.
		/// </param>
		public AutoTagger(IModRepository p_mrpModRepository)
		{
			ModRepository = p_mrpModRepository;
		}

		#endregion

		/// <summary>
		/// Gets a list of possible mod info tags which match the given mod.
		/// </summary>
		/// <param name="mod">
		/// The mod for which to retrieve a list of possible tags.
		/// </param>
		/// <returns>
		/// A list of possible mod info tags which match the given mod.
		/// </returns>
		public IEnumerable<IModInfo> GetTagInfoCandidates(IMod mod)
		{
			var mods = new List<IModInfo>();

			try
			{
				var modInfo = ModRepository.GetModInfoForFile(
				 mod.ModArchivePath);

				/*
				 * The archive could not be identified through MD5 or
				 * filename parsing. Use the Nexus mod ID already stored
				 * in the local mod metadata as the final fallback.
				 */
				if (modInfo == null &&
				 !string.IsNullOrEmpty(mod.Id))
				{
					modInfo = ModRepository.GetModInfo(mod.Id);

					if (modInfo != null)
					{
						Trace.TraceInformation(
						 "Get Mod Info: filename=\"{0}\", " +
						 "source=StoredModId, status=Match, " +
						 "modId={1}, fileId=unknown",
						 GetSafeFileName(mod.ModArchivePath),
						 modInfo.Id ?? "unknown");
					}
				}

				if (modInfo == null)
				{
					return mods;
				}

				/*
				 * GetModInfoForFile now returns file-specific data
				 * when either MD5 recognition or legacy filename
				 * recognition finds an exact Nexus file.
				 *
				 * A DownloadId therefore means that recognition is
				 * already complete. Do not hash the archive again.
				 */
				if (!string.IsNullOrEmpty(modInfo.DownloadId))
				{
					mods.Add(modInfo);
					return mods;
				}

				/*
				 * The mod was identified, but the exact Nexus file was
				 * not. Return all files belonging to that mod so the
				 * user can select the correct candidate.
				 */
				var modFileInfo =
				 ModRepository.GetModFileInfo(modInfo.Id);

				if (modFileInfo != null)
				{
					foreach (var fileInfo in modFileInfo)
					{
						var combinedInfo =
						 CombineInfo(modInfo, fileInfo);

						if (combinedInfo != null)
						{
							mods.Add(combinedInfo);
						}
					}
				}

				/*
				 * Keep the mod-only candidate when Nexus returned no
				 * files or the file-list request failed.
				 */
				if (mods.Count == 0)
				{
					mods.Add(modInfo);
				}
			}
			catch (Exception e)
			{
				TraceUtil.TraceException(e);

				/*
				 * Preserve the existing behavior of returning a visible
				 * error candidate to the tagging UI.
				 */
				mods.Add(
				 new ModInfo(
				  null,
				  $"{e.Message}",
				  null,
				  null,
				  null,
				  null,
				  false,
				  null,
				  null,
				  0,
				  -1,
				  null,
				  null,
				  null,
				  null,
				  true,
				  true));
			}

			return mods;
		}

		/// <summary>
		/// Combines the given mod info and mod file info into one mod info.
		/// </summary>
		/// <param name="p_mifInfo">
		/// The mod info to combine.
		/// </param>
		/// <param name="p_mfiFileInfo">
		/// The mod file info to combine.
		/// </param>
		/// <returns>
		/// A mod info representing the information from both objects.
		/// </returns>
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
					{
						return null;
					}

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

					if (!string.IsNullOrEmpty(
					 p_mfiFileInfo.Id))
					{
						mifUpdatedInfo.DownloadId =
						 p_mfiFileInfo.Id;
					}

					intLineTracker = 8;

					/*
					 * The previous implementation did not copy the
					 * Nexus filename into the combined result.
					 */
					if (!string.IsNullOrEmpty(
					 p_mfiFileInfo.Filename))
					{
						mifUpdatedInfo.FileName =
						 p_mfiFileInfo.Filename;
					}

					intLineTracker = 9;

					if (!string.IsNullOrEmpty(
					 p_mfiFileInfo.HumanReadableVersion))
					{
						mifUpdatedInfo.HumanReadableVersion =
						 p_mfiFileInfo.HumanReadableVersion;
						mifUpdatedInfo.LastKnownVersion =
						 p_mfiFileInfo.HumanReadableVersion;
					}
					else
					{
						mifUpdatedInfo.HumanReadableVersion = null;
						mifUpdatedInfo.LastKnownVersion = null;
					}

					intLineTracker = 10;
					mifUpdatedInfo.MachineVersion = null;
					intLineTracker = 11;

					intLineTracker = 12;

					if (!string.IsNullOrEmpty(
					 p_mfiFileInfo.Name))
					{
						mifUpdatedInfo.ModName =
						 string.Format(
						  "{0} - {1}",
						  mifUpdatedInfo.ModName,
						  p_mfiFileInfo.Name);

						intLineTracker = 13;
					}

					intLineTracker = 14;
				}

				intLineTracker = 15;
			}
			catch (NullReferenceException)
			{
				Trace.TraceError(
				 "NullReferenceException in CombineInfo: " +
				 "LineTracker: {0}",
				 intLineTracker);

				throw;
			}

			return mifUpdatedInfo;
		}

		/// <summary>
		/// Tags the mod with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod to tag.</param>
		/// <param name="p_mifModInfo">
		/// The values with which to tag the mod.
		/// </param>
		/// <param name="p_booOverwriteAllValues">
		/// Whether to overwrite the current info values,
		/// or just the empty ones.
		/// </param>
		public void Tag(IMod p_modMod, IModInfo p_mifModInfo, bool p_booOverwriteAllValues)
		{
			p_modMod.UpdateInfo(
			 p_mifModInfo,
			 p_booOverwriteAllValues);
		}

		private static string GetSafeFileName(string filePath)
		{
			try
			{
				return Path.GetFileName(filePath) ??
				 string.Empty;
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}
	}
}
