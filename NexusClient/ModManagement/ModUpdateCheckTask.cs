namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using System.Threading;
	using System.Threading.Tasks;
	using Nexus.Client.BackgroundTasks;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods;
	using Nexus.Client.UI;
	using Nexus.Client.Util.Localization;

	public class ModUpdateCheckTask : ThreadedBackgroundTask
	{
		private readonly bool _overrideCategorySetup;
		private readonly bool? _missingDownloadId = false;
		private readonly List<IMod> _modList = new List<IMod>();
		private readonly bool _overrideLocalModNames;
		private readonly Dictionary<string, string> _newDownloadID = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private int _retries = 0;
		private bool _cancel;
		private string _period = string.Empty;

		#region Properties

		/// <summary>
		/// Gets the AutoUpdater.
		/// </summary>
		/// <value>The AutoUpdater.</value>
		protected AutoUpdater AutoUpdater { get; }

		/// <summary>
		/// Gets the current mod repository.
		/// </summary>
		/// <value>The current mod repository.</value>
		protected IModRepository ModRepository { get; }

		/// <summary>
		/// Gets the current profile manager.
		/// </summary>
		/// <value>The current profile manager.</value>
		protected IProfileManager ProfileManager { get; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="autoUpdater">The AutoUpdater.</param>
		/// <param name="modRepository">The current mod repository.</param>
		/// <param name="modList">The list of mods we need to update.</param>
		/// <param name="overrideCategorySetup">Whether to force a global update.</param>
		/// <inheritdoc />
		public ModUpdateCheckTask(AutoUpdater autoUpdater, IProfileManager profileManager, IModRepository modRepository, IEnumerable<IMod> modList, string period, bool overrideCategorySetup, bool? missingDownloadId, bool overrideLocalModNames)
		{
			AutoUpdater = autoUpdater;
			ModRepository = modRepository;
			ProfileManager = profileManager;
			_period = period;
			_modList.AddRange(modList);
			_overrideCategorySetup = overrideCategorySetup;
			_missingDownloadId = missingDownloadId;
			_overrideLocalModNames = overrideLocalModNames;
		}

		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="confirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod confirm)
		{
			Start(confirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		/// <inheritdoc />
		public override void Cancel()
		{
			base.Cancel();
			_cancel = true;
		}

		/// <summary>
		/// The method that is called to start the background task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always null.</returns>
		protected override object DoWork(object[] args)
		{
			List<RepositoryModUpdate> updatedMods = new List<RepositoryModUpdate>();
			int libraryCandidateCount = _modList.Count;

			var modList = new List<string>();
			var modCheck = new List<IMod>();

			OverallMessage = LanguageManager.Get("Tasks.ModUpdates.SetupSearch", "Updating mods info: setup search..");
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ItemProgress = 0;
			ItemProgressStepSize = 1;
			ItemProgressMaximum = 1;
			OverallProgressMaximum = 1;

			OverallProgressMaximum = _modList.Count * 2;
			ItemProgressMaximum = _modList.Count;

			if (_overrideCategorySetup && _missingDownloadId == false)
				return RefreshRepositoryCategories();

			if (!string.IsNullOrEmpty(_period))
			{
				// Get updated mods in the chosen period
				var updatedListStopwatch = Stopwatch.StartNew();
				updatedMods = ModRepository.GetUpdatedWithMetadata(_period);
				updatedListStopwatch.Stop();
				Trace.TraceInformation("NMM updated-mod list completed: elapsedMs={0}, returned={1}.",
					updatedListStopwatch.ElapsedMilliseconds, updatedMods == null ? -1 : updatedMods.Count);
			}

			var setupStopwatch = Stopwatch.StartNew();
			for (int i = 0; i < _modList.Count; i++)
			{
				if (_cancel)
				{
					break;
				}

				IMod modCurrent = _modList[i];
				string modId = string.Empty;
				int isEndorsed = 0;
				ItemMessage = modCurrent.ModName;

				string modName = StripFileName(modCurrent.Filename, modCurrent.Id);
                // Prefer the archive hash because Nexus file names are no longer stable identifiers.
                if (_missingDownloadId == null || _missingDownloadId == true)
                {
                    IModInfo modInfoForFile = ModRepository.GetModInfoForFile(modCurrent.ModArchivePath);

                    if (modInfoForFile != null)
                    {
                        modCurrent.Id = modInfoForFile.Id;
                        modId = modInfoForFile.Id;
                        AutoUpdater.AddNewVersionNumberForMod(modCurrent, modInfoForFile);
                        modName = StripFileName(modCurrent.Filename, modInfoForFile.Id);

                        if ((string.IsNullOrEmpty(modCurrent.DownloadId) || modCurrent.DownloadId == "0" || modCurrent.DownloadId == "-1") &&
                            !string.IsNullOrEmpty(modInfoForFile.DownloadId) && modInfoForFile.DownloadId != "0" && modInfoForFile.DownloadId != "-1")
                        {
                            var filename = Path.GetFileName(modCurrent.Filename);

                            if (!_newDownloadID.ContainsKey(filename))
                            {
                                _newDownloadID.Add(filename, modInfoForFile.DownloadId);
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(modId) && !string.IsNullOrEmpty(modCurrent.Id))
                {
                    modId = modCurrent.Id;
                    isEndorsed = modCurrent.IsEndorsed == true ? 1 : modCurrent.IsEndorsed == false ? -1 : 0;
                }

				// If we're looking for missing download Ids
				if (_missingDownloadId == null || _missingDownloadId == true && (string.IsNullOrEmpty(modCurrent.DownloadId) || modCurrent.DownloadId == "0" || modCurrent.DownloadId == "-1"))
				{
					if (updatedMods.Count > 0 && !string.IsNullOrWhiteSpace(modId) && updatedMods.Any(update => string.Equals(update.ModId, modId, StringComparison.OrdinalIgnoreCase)))
					{
						modList.Add(string.Format("{0}|{1}|{2}|{3}", modName, modId, modCurrent.DownloadId, Path.GetFileName(modCurrent.Filename)));
						modCheck.Add(modCurrent);
					}
					else if (updatedMods.Count == 0 || string.IsNullOrWhiteSpace(modId))
					{
						modList.Add(string.Format("{0}|{1}|{2}|{3}", modName, string.IsNullOrWhiteSpace(modId) ? "0" : modId, string.IsNullOrWhiteSpace(modCurrent.DownloadId) ? "0" : modCurrent.DownloadId, Path.GetFileName(modCurrent.Filename)));
						modCheck.Add(modCurrent);
					}
				}
				//  If we're performing a period-based update check or an update after a category reset
				else if (_missingDownloadId == false && !string.IsNullOrEmpty(modId))
				{
					// If we're performing a category reset
					if (_overrideCategorySetup && !string.IsNullOrEmpty(modCurrent.DownloadId))
					{
						// TODO: This used to work with download ids, it requires a new method in the API, currently it did nothing cause it passed downloadId to a modId search.
						modList.Add(string.Format("{0}|{1}|{2}|{3}", modName, string.IsNullOrWhiteSpace(modId) ? "0" : modId, modCurrent.DownloadId, Path.GetFileName(modCurrent.Filename)));
						modCheck.Add(modCurrent);
					}
					// If we're performing a period-based update check
					else if (updatedMods.Count > 0 && !string.IsNullOrWhiteSpace(modId) && updatedMods.Any(update => string.Equals(update.ModId, modId, StringComparison.OrdinalIgnoreCase)))
					{
						modList.Add(string.Format("{0}|{1}|{2}|{3}", modName, string.IsNullOrWhiteSpace(modId) ? "0" : modId, string.IsNullOrWhiteSpace(modCurrent.DownloadId) ? "0" : modCurrent.DownloadId, Path.GetFileName(modCurrent.Filename)));
						modCheck.Add(modCurrent);
					}
				}

				if (ItemProgress < ItemProgressMaximum)
					StepItemProgress();
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (_cancel)
					break;

				// Prevents the repository request string from becoming too long.
				// Since the system has been overhauled this is no longer required.
				//if (modList.Count == modLimit)
				//{
				//	string strResult = CheckForModListUpdate(modList, modCheck);

				//	if (!string.IsNullOrEmpty(strResult))
				//	{
				//		modList.Clear();
				//		return strResult;
				//	}

				//	modList.Clear();
				//	OverallMessage = LanguageManager.Get("Tasks.ModUpdates.SetupSearch", "Updating mods info: setup search..");
				//	ItemProgress = 0;
				//	ItemProgressMaximum = _modList.Count == modLimit ? 1 : _modList.Count - (i + 1);
				//}
			}

			setupStopwatch.Stop();
			Trace.TraceInformation("NMM mod-update setup completed: elapsedMs={0}, candidates={1}, selected={2}, cancelled={3}.",
				setupStopwatch.ElapsedMilliseconds, libraryCandidateCount, modList.Count, _cancel);

			if (!_cancel && modList.Count > 0)
			{
				string strResult = CheckForModListUpdate(modList, modCheck, updatedMods);

				if (!string.IsNullOrEmpty(strResult))
				{
					_modList.Clear();
					return strResult;
				}
			}

			_modList.Clear();

			return _newDownloadID;
		}

		/// <summary>
		/// Refreshes only repository category IDs after a category reset without entering file/update-chain resolution.
		/// </summary>
		private object RefreshRepositoryCategories()
		{
			var modsToRefresh = new List<IMod>();
			var normalizedModIds = new List<string>();
			var normalizedIdByMod = new Dictionary<IMod, string>();
			var setupStopwatch = Stopwatch.StartNew();

			foreach (IMod mod in _modList)
			{
				if (_cancel)
					break;

				ItemMessage = mod.ModName;
				int numericModId;
				if (mod.CategoryId == 0
					&& mod.CustomCategoryId < 0
					&& mod.UpdateChecksEnabled
					&& Int32.TryParse(mod.Id, out numericModId)
					&& numericModId > 0)
				{
					string normalizedModId = numericModId.ToString();
					modsToRefresh.Add(mod);
					normalizedModIds.Add(normalizedModId);
					normalizedIdByMod[mod] = normalizedModId;
				}

				if (ItemProgress < ItemProgressMaximum)
					StepItemProgress();
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			setupStopwatch.Stop();
			Trace.TraceInformation(
				"NMM category metadata setup completed: elapsedMs={0}, candidates={1}, selected={2}, uniqueMods={3}, cancelled={4}.",
				setupStopwatch.ElapsedMilliseconds,
				_modList.Count,
				modsToRefresh.Count,
				normalizedModIds.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
				_cancel);

			if (_cancel || modsToRefresh.Count == 0)
			{
				_modList.Clear();
				return _newDownloadID;
			}

			OverallMessage = LanguageManager.Get("Tasks.ModUpdates.GettingOnlineUpdates", "Updating mods info: getting online updates..");
			var fetchStopwatch = Stopwatch.StartNew();
			Dictionary<string, int> repositoryCategories = ModRepository.GetModCategoryIds(normalizedModIds);
			fetchStopwatch.Stop();
			Trace.TraceInformation(
				"NMM category metadata fetch completed: elapsedMs={0}, requested={1}, returned={2}.",
				fetchStopwatch.ElapsedMilliseconds,
				modsToRefresh.Count,
				repositoryCategories == null ? -1 : repositoryCategories.Count);

			if (repositoryCategories == null)
			{
				_modList.Clear();
				return _newDownloadID;
			}

			var applyStopwatch = Stopwatch.StartNew();
			int applied = 0;
			ItemProgress = 0;
			ItemProgressMaximum = modsToRefresh.Count;
			foreach (IMod mod in modsToRefresh)
			{
				if (_cancel)
					break;

				ItemMessage = mod.ModName;
				int repositoryCategoryId;
				string normalizedModId = normalizedIdByMod[mod];
				if (repositoryCategories.TryGetValue(normalizedModId, out repositoryCategoryId))
				{
					AutoUpdater.ApplyRepositoryCategory(mod, repositoryCategoryId);
					applied++;
				}

				if (ItemProgress < ItemProgressMaximum)
					StepItemProgress();
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			applyStopwatch.Stop();
			Trace.TraceInformation(
				"NMM category metadata apply completed: elapsedMs={0}, selected={1}, resolved={2}, applied={3}, cancelled={4}.",
				applyStopwatch.ElapsedMilliseconds,
				modsToRefresh.Count,
				repositoryCategories.Count,
				applied,
				_cancel);

			_modList.Clear();
			return _newDownloadID;
		}

		private string StripFileName(string p_strFileName, string p_strId)
		{
			string strModFilename = string.Empty;

			if (!string.IsNullOrWhiteSpace(p_strFileName))
			{
				strModFilename = Path.GetFileNameWithoutExtension(p_strFileName);

				if (!string.IsNullOrWhiteSpace(p_strId))
				{
					if (p_strId.Length > 2 && strModFilename.IndexOf(p_strId) > 0)
					{
						string strModIDPattern = "-" + p_strId + "-";
						string strVersionlessPattern = "-" + p_strId;

						if (strModFilename.IndexOf(strModIDPattern, 0) > 0)
							strModFilename = strModFilename.Substring(0, strModFilename.IndexOf(strModIDPattern, 0));
						else if (strModFilename.IndexOf(strVersionlessPattern, 0) > 0)
							strModFilename = strModFilename.Substring(0, strModFilename.IndexOf(strVersionlessPattern, 0));
					}
					else
					{
						if (strModFilename.IndexOf('-', 0) > 0)
							strModFilename = strModFilename.Substring(0, strModFilename.IndexOf('-', 0));
					}
				}
			}
			return strModFilename.Trim();
		}

		/// <summary>
		/// Checks for the updated information for the given mods.
		/// </summary>
		/// <param name="modList">The mods for which to check for updates.</param>
		/// <param name="modsToCheck">The local mods aligned with the repository request rows.</param>
		/// <param name="providerUpdates">The current provider freshness records, when this is a period-based update check.</param>
		private string CheckForModListUpdate(List<string> modList, List<IMod> modsToCheck, List<RepositoryModUpdate> providerUpdates)
		{
			OverallMessage = _missingDownloadId != false ? LanguageManager.Get("Tasks.ModUpdates.RetrievingDownloadIds", "Updating mods info: retrieving download ids..") : LanguageManager.Get("Tasks.ModUpdates.GettingOnlineUpdates", "Updating mods info: getting online updates..");
			List<IModInfo> fileListInfo = new List<IModInfo>();
			List<RepositoryFileUpdateCheckpoint> checkpointCandidates = new List<RepositoryFileUpdateCheckpoint>();
			IMod[] modCheckList = modsToCheck.ToArray();

			//get mod info
			var fetchStopwatch = Stopwatch.StartNew();
			for (int i = 0; i <= _retries; i++)
			{
				RepositoryFileListInfoResult fetchResult = ModRepository.GetFileListInfoWithFreshness(modList, providerUpdates);
				fileListInfo = fetchResult?.ModInfo;
				checkpointCandidates = fetchResult?.Checkpoints ?? new List<RepositoryFileUpdateCheckpoint>();

				if (fileListInfo != null)
				{
					break;
				}

				Task.Delay(2500);
			}
			fetchStopwatch.Stop();
			Trace.TraceInformation("NMM mod-update fetch completed: elapsedMs={0}, requested={1}, returned={2}.",
				fetchStopwatch.ElapsedMilliseconds, modList.Count, fileListInfo == null ? -1 : fileListInfo.Count);

			if (fileListInfo != null)
			{
				var applyStopwatch = Stopwatch.StartNew();
				int appliedCount = 0;
				IModInfo[] modUpdates = fileListInfo.ToArray();
				ItemProgress = 0;
				ItemProgressMaximum = fileListInfo.Count;

				for (int i = 0; i < modUpdates.Count(); i++)
				{
					ModInfo modUpdate = (ModInfo)modUpdates[i];
					if (_cancel)
					{
						break;
					}

					if (OverallProgress < OverallProgressMaximum)
					{
						StepOverallProgress();
					}

					if (modUpdate == null)
					{
						continue;
					}

					ItemMessage = modUpdate.ModName;

					IMod mod = i < modCheckList.Length ? modCheckList[i] : null;

					if (mod == null)
					{
						continue;
					}

					if (ModFileIdentity.IsUsableRepositoryId(mod.Id)
						&& !ModFileIdentity.IsUsableRepositoryId(modUpdate.Id))
					{
						continue;
					}

					if (ModFileIdentity.IsUsableRepositoryId(mod.Id)
						&& ModFileIdentity.IsUsableRepositoryId(modUpdate.Id)
						&& !mod.Id.Equals(modUpdate.Id, StringComparison.OrdinalIgnoreCase))
					{
						Trace.TraceWarning("Ignored mismatched mod update response. Expected mod ID {0}, received {1}.", mod.Id, modUpdate.Id);
						continue;
					}

					if (ItemProgress < ItemProgressMaximum)
					{
						StepItemProgress();
					}

					if (ModFileIdentity.IsUsableRepositoryId(modUpdate.DownloadId)
						&& !modUpdate.DownloadId.Equals(mod.DownloadId, StringComparison.OrdinalIgnoreCase))
					{
						string filename = Path.GetFileName(mod.Filename);
						if (!string.IsNullOrEmpty(filename) && !_newDownloadID.ContainsKey(filename))
						{
							_newDownloadID.Add(filename, modUpdate.DownloadId);
						}
					}

					if (!string.IsNullOrEmpty(mod.DownloadId) && string.IsNullOrWhiteSpace(modUpdate.DownloadId))
					{
						modUpdate.DownloadId = mod.DownloadId;
					}

					if (_missingDownloadId != false)
					{
						modUpdate.HumanReadableVersion = !string.IsNullOrEmpty(mod.HumanReadableVersion) ? mod.HumanReadableVersion : modUpdate.HumanReadableVersion;
						modUpdate.MachineVersion = mod.MachineVersion != null ? mod.MachineVersion : modUpdate.MachineVersion;
					}

					if (mod.CustomCategoryId >= 0)
					{
						modUpdate.CustomCategoryId = mod.CustomCategoryId;
					}

					modUpdate.UpdateWarningEnabled = mod.UpdateWarningEnabled;
					modUpdate.UpdateChecksEnabled = mod.UpdateChecksEnabled;
					if (mod.Website != null)
						modUpdate.Website = mod.Website;
					AutoUpdater.AddNewVersionNumberForMod(mod, modUpdate);

					if (!_overrideLocalModNames)
					{
						modUpdate.ModName = mod.ModName;
					}

					if (!string.IsNullOrEmpty(mod.ModName))
						modUpdate.ModName = string.Empty;
					mod.UpdateInfo(modUpdate, null);
					appliedCount++;
					ItemProgress = 0;
				}

				if (modUpdates.Count() < modCheckList.Count())
					Cancel();

				bool completeFreshnessRun = !_cancel
					&& modUpdates.Length == modCheckList.Length
					&& appliedCount == modCheckList.Length
					&& checkpointCandidates.Count == modUpdates.Length
					&& checkpointCandidates.Count > 0
					&& checkpointCandidates.All(candidate => candidate != null);
				bool checkpointsPersisted = completeFreshnessRun && ModRepository.CommitFileUpdateCheckpoints(checkpointCandidates);

				applyStopwatch.Stop();
				Trace.TraceInformation("NMM mod-update apply completed: elapsedMs={0}, requested={1}, returned={2}, applied={3}, cancelled={4}, checkpointCandidates={5}, checkpointsPersisted={6}.",
					applyStopwatch.ElapsedMilliseconds, modCheckList.Length, modUpdates.Length, appliedCount, _cancel,
					checkpointCandidates.Count(candidate => candidate != null), checkpointsPersisted);
			}

			return null;
		}
	}
}
