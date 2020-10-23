namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.Mods;
    using Nexus.Client.UI;

	public class ModUpdateCheckTask : ThreadedBackgroundTask
	{
		private readonly bool _overrideCategorySetup;
		private readonly bool? _missingDownloadId = false;
		private readonly List<IMod> _modList = new List<IMod>();
        private readonly bool _overrideLocalModNames;
        private readonly Dictionary<string, string> _newDownloadID = new Dictionary<string, string>();
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
			const int modLimit = 75;

			List<string> updatedMods = new List<string>();

			var modList = new List<string>();
			var modCheck = new List<IMod>();

            OverallMessage = "Updating mods info: setup search..";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ItemProgress = 0;
			ItemProgressStepSize = 1;
			ItemProgressMaximum = 1;
			OverallProgressMaximum = 1;

			OverallProgressMaximum = _modList.Count * 2;
			ItemProgressMaximum = _modList.Count > modLimit ? modLimit : _modList.Count;

			if (!string.IsNullOrEmpty(_period))
			{
				// get mod updates
				updatedMods = ModRepository.GetUpdated(_period);
			}

			for (var i = 0; i < _modList.Count; i++)
			{
                if (_cancel)
                {
                    break;
                }

                var modCurrent = _modList[i];
				var modId = string.Empty;
                var isEndorsed = 0;
				ItemMessage = modCurrent.ModName;

                var modName = StripFileName(modCurrent.Filename, modCurrent.Id);

				if (!string.IsNullOrEmpty(modCurrent.Id))
				{
					modId = modCurrent.Id;
					isEndorsed = modCurrent.IsEndorsed == true ? 1 : modCurrent.IsEndorsed == false ? -1 : 0;
				}
				else
				{
                    var modInfoForFile = ModRepository.GetModInfoForFile(modCurrent.Filename);

                    if (modInfoForFile != null)
                    {
                        modCurrent.Id = modInfoForFile.Id;
                        modId = modInfoForFile.Id;
                        AutoUpdater.AddNewVersionNumberForMod(modCurrent, modInfoForFile);
                        modName = StripFileName(modCurrent.Filename, modInfoForFile.Id);
                    }
                }

				if (_missingDownloadId == null || _missingDownloadId == true && (string.IsNullOrEmpty(modCurrent.DownloadId) || modCurrent.DownloadId == "0" || modCurrent.DownloadId == "-1"))
				{
					if (updatedMods.Count > 0 && !string.IsNullOrWhiteSpace(modId) && updatedMods.Contains(modId, StringComparer.OrdinalIgnoreCase))
					{
						modList.Add(string.Format("{0}|{1}|{2}", modName, modId, Path.GetFileName(modCurrent.Filename)));
						modCheck.Add(modCurrent);
					}
					else if (updatedMods.Count == 0 || string.IsNullOrWhiteSpace(modId))
					{
						modList.Add(string.Format("{0}|{1}|{2}", modName, string.IsNullOrWhiteSpace(modId) ? "0" : modId, Path.GetFileName(modCurrent.Filename)));
						modCheck.Add(modCurrent);
					}
				}
				else if (_missingDownloadId == false && !string.IsNullOrEmpty(modCurrent.DownloadId))
				{
					if (_overrideCategorySetup)
						modList.Add(string.Format("{0}", modCurrent.DownloadId));
					else
						modList.Add(string.Format("{0}|{1}|{2}|{3}|{4}", string.IsNullOrWhiteSpace(modCurrent.DownloadId) ? "0" : modCurrent.DownloadId, string.IsNullOrWhiteSpace(modId) ? "0" : modId, Path.GetFileName(modName), string.IsNullOrWhiteSpace(modCurrent.HumanReadableVersion) ? "0" : modCurrent.HumanReadableVersion, isEndorsed));
					modCheck.Add(modCurrent);
				}

				if (ItemProgress < ItemProgressMaximum)
					StepItemProgress();
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (_cancel)
					break;

				// Prevents the repository request string from becoming too long.
				if (modList.Count == modLimit)
				{
					string strResult = CheckForModListUpdate(modList, modCheck);

					if (!string.IsNullOrEmpty(strResult))
					{
						modList.Clear();
						return strResult;
					}

					modList.Clear();
					OverallMessage = "Updating mods info: setup search..";
					ItemProgress = 0;
					ItemProgressMaximum = _modList.Count == modLimit ? 1 : _modList.Count - (i + 1);
				}
			}

			if (!_cancel && modList.Count > 0)
			{
				string strResult = CheckForModListUpdate(modList, modCheck);

				if (!string.IsNullOrEmpty(strResult))
				{
					_modList.Clear();
					return strResult;
				}
			}

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
		private string CheckForModListUpdate(List<string> modList, List<IMod> modsToCheck)
		{
			OverallMessage = _missingDownloadId != false ? "Updating mods info: retrieving download ids.." : "Updating mods info: getting online updates..";
			var fileListInfo = new List<IModInfo>();
			var modCheckList = modsToCheck.ToArray();

			//get mod info
			for (var i = 0; i <= _retries; i++)
            {
                fileListInfo = ModRepository.GetFileListInfo(modList);

                if (fileListInfo != null)
                {
                    break;
                }

                Thread.Sleep(1000);
            }

            if (fileListInfo != null)
            {
                var modUpdates = fileListInfo.ToArray();
                ItemProgress = 0;
                ItemProgressMaximum = fileListInfo.Count;

                for (var i = 0; i < modUpdates.Count(); i++)
                {
                    var modUpdate = (ModInfo)modUpdates[i];
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

                    var mod = _missingDownloadId != false ? _modList.Where(x => x != null).FirstOrDefault(x => !string.IsNullOrEmpty(modUpdate.FileName) && (StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename).ToString(), x.Id), StringComparison.OrdinalIgnoreCase) || StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename.Replace("_", " ")).ToString(), x.Id), StringComparison.OrdinalIgnoreCase))) : _modList.Where(x => x != null).FirstOrDefault(x => !string.IsNullOrEmpty(modUpdate.FileName) && modUpdate.FileName.Equals(Path.GetFileName(x.Filename)?.ToString(), StringComparison.OrdinalIgnoreCase));

                    if (mod == null && !string.IsNullOrEmpty(modUpdate.DownloadId) && modUpdate.DownloadId != "0" && modUpdate.DownloadId != "-1")
                    {
                        mod = _modList.Where(x => x != null).FirstOrDefault(x => !string.IsNullOrEmpty(x.DownloadId) && modUpdate.DownloadId.Equals(x.DownloadId.ToString(), StringComparison.OrdinalIgnoreCase));
                    }

                    if (mod == null)
                    {
                        if (_missingDownloadId != false)
                        {
                            if (modCheckList.Count() == modUpdates.Count())
                            {
                                var modCheck = modCheckList[i];
                                if (!string.IsNullOrEmpty(modUpdate.Id) && modUpdate.Id != "0" && !string.IsNullOrEmpty(modCheck.Id) && modCheck.Id != "0")
                                {
                                    if (modUpdate.Id.Equals(modCheck.Id, StringComparison.OrdinalIgnoreCase))
                                    {
                                        mod = modCheck;
                                    }
                                }
                            }
                        }
                    }

                    if (mod != null)
                    {
                        if (ItemProgress < ItemProgressMaximum)
                        {
                            StepItemProgress();
                        }

                        if (modUpdate.DownloadId == "-1")
                        {
                            if (_missingDownloadId != false)
                            {
                                var filename = Path.GetFileName(mod.Filename);

                                if (!string.Equals(StripFileName(filename, mod.Id), StripFileName(modUpdate.FileName, modUpdate.Id), StringComparison.InvariantCultureIgnoreCase))
                                {
                                    continue;
                                }
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(modUpdate.DownloadId) && !modUpdate.DownloadId.Equals(mod.DownloadId))
                        {
                            if (_missingDownloadId != false)
                            {
                                var filename = Path.GetFileName(mod.Filename);

                                if (string.Equals(filename, modUpdate.FileName, StringComparison.InvariantCultureIgnoreCase))
                                {
                                    if (!_newDownloadID.ContainsKey(filename))
                                    {
                                        _newDownloadID.Add(filename, modUpdate.DownloadId);
                                    }
                                }
                                else if (!string.IsNullOrEmpty(mod.Id) && mod.Id != "0" && !string.IsNullOrEmpty(modUpdate.Id) && modUpdate.Id != "0")
                                {
                                    if (string.Equals(mod.Id, modUpdate.Id, StringComparison.InvariantCultureIgnoreCase) && !_newDownloadID.ContainsKey(filename))
                                    {
                                        _newDownloadID.Add(filename, modUpdate.DownloadId);
                                    }
                                }
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

                        if (mod.CustomCategoryId != 0 && mod.CustomCategoryId != -1)
                        {
                            modUpdate.CustomCategoryId = mod.CustomCategoryId;
                        }

                        modUpdate.UpdateWarningEnabled = mod.UpdateWarningEnabled;
                        modUpdate.UpdateChecksEnabled = mod.UpdateChecksEnabled;
                        AutoUpdater.AddNewVersionNumberForMod(mod, modUpdate);

                        if (!_overrideLocalModNames)
                        {
                            modUpdate.ModName = mod.ModName;
                        }

						if (!string.IsNullOrEmpty(mod.ModName))
							modUpdate.ModName = string.Empty;
                        mod.UpdateInfo(modUpdate, null);
                        ItemProgress = 0;
                    }
                }
            }

            return null;
		}
	}
}
