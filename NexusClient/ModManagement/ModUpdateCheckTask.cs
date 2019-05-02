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
        public ModUpdateCheckTask(AutoUpdater autoUpdater, IProfileManager profileManager, IModRepository modRepository, IEnumerable<IMod> modList, bool overrideCategorySetup, bool? missingDownloadId, bool overrideLocalModNames)
		{
			AutoUpdater = autoUpdater;
			ModRepository = modRepository;
			ProfileManager = profileManager;
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
			ItemProgressMaximum = (_modList.Count > modLimit) ? modLimit : _modList.Count;

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
					isEndorsed = modCurrent.IsEndorsed == true ? 1 : (modCurrent.IsEndorsed == false ? -1 : 0);
				}
				else
				{
					try
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
					catch (RepositoryUnavailableException)
					{
						//the repository is not available, so don't bother
					}
				}

				if ((_missingDownloadId == null) || ((_missingDownloadId == true) && (string.IsNullOrEmpty(modCurrent.DownloadId) || (modCurrent.DownloadId == "0") || (modCurrent.DownloadId == "-1"))))
				{
					modList.Add(string.Format("{0}|{1}|{2}", modName, string.IsNullOrWhiteSpace(modId) ? "0" : modId, Path.GetFileName(modCurrent.Filename)));
					modCheck.Add(modCurrent);
				}
				else if ((_missingDownloadId == false) && !string.IsNullOrEmpty(modCurrent.DownloadId))
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
					ItemProgressMaximum = (_modList.Count == modLimit) ? 1 : (_modList.Count - (i + 1));
				}
			}

			if (!_cancel && (modList.Count > 0))
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
					if ((p_strId.Length > 2) && (strModFilename.IndexOf(p_strId) > 0))
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
		/// <param name="p_lstModList">The mods for which to check for updates.</param>
		private string CheckForModListUpdate(List<string> p_lstModList, List<IMod> p_lstModCheck)
		{
			if (_missingDownloadId != false)
				OverallMessage = "Updating mods info: retrieving download ids..";
			else
				OverallMessage = "Updating mods info: getting online updates..";
			List<IModInfo> mifInfo = new List<IModInfo>();
			IMod[] ModCheckList = p_lstModCheck.ToArray();

			try
			{
				//get mod info
				for (int i = 0; i <= _retries; i++)
				{
					mifInfo = ModRepository.GetFileListInfo(p_lstModList);

					if (mifInfo != null)
						break;

					Thread.Sleep(1000);
				}

				if (mifInfo != null)
				{
					IModInfo[] mifModUpdates = mifInfo.ToArray();
					ItemProgress = 0;
					ItemProgressMaximum = mifInfo.Count;

					for (int i = 0; i < mifModUpdates.Count(); i++)
					{
						ModInfo modUpdate = (ModInfo)mifModUpdates[i];
						if (_cancel)
							break;
						if (OverallProgress < OverallProgressMaximum)
							StepOverallProgress();

						if (modUpdate == null)
							continue;

						ItemMessage = modUpdate.ModName;

						IMod modMod = null;
						if (_missingDownloadId != false)
							modMod = _modList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(modUpdate.FileName) && (StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename).ToString(), x.Id), StringComparison.OrdinalIgnoreCase) || StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename.Replace("_"," ")).ToString(), x.Id), StringComparison.OrdinalIgnoreCase))).FirstOrDefault();
						else
							modMod = _modList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(modUpdate.FileName) && modUpdate.FileName.Equals(Path.GetFileName(x.Filename).ToString(), StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
						
						if (modMod == null)
						{
							if ((!string.IsNullOrEmpty(modUpdate.DownloadId)) && (modUpdate.DownloadId != "0") && (modUpdate.DownloadId != "-1"))
								modMod = _modList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(x.DownloadId) && modUpdate.DownloadId.Equals(x.DownloadId.ToString(), StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

						}

						if(modMod == null)
						{
							if (_missingDownloadId != false)
							{
								if (ModCheckList.Count() == mifModUpdates.Count())
								{
									IMod modCheck = ModCheckList[i];
									if ((!string.IsNullOrEmpty(modUpdate.Id) && modUpdate.Id != "0") && (!string.IsNullOrEmpty(modCheck.Id) && modCheck.Id != "0"))
									{
										if (modUpdate.Id.Equals(modCheck.Id, StringComparison.OrdinalIgnoreCase))
											modMod = modCheck;
									}
								}
							}
						}

						if (modMod != null)
						{
							if (ItemProgress < ItemProgressMaximum)
								StepItemProgress();

							if (modUpdate.DownloadId == "-1")
							{
								if (_missingDownloadId != false)
								{
									string Filename = Path.GetFileName(modMod.Filename);
									if (!string.Equals(StripFileName(Filename, modMod.Id), StripFileName(modUpdate.FileName, modUpdate.Id), StringComparison.InvariantCultureIgnoreCase))
										continue;
								}
							}
							else if (!string.IsNullOrWhiteSpace(modUpdate.DownloadId) && !modUpdate.DownloadId.Equals(modMod.DownloadId))
							{
								if (_missingDownloadId != false)
								{
									string Filename = Path.GetFileName(modMod.Filename);
									if (string.Equals(Filename, modUpdate.FileName, StringComparison.InvariantCultureIgnoreCase))
									{
										if (!_newDownloadID.ContainsKey(Filename))
											_newDownloadID.Add(Filename, modUpdate.DownloadId);
									}
									else if (!string.IsNullOrEmpty(modMod.Id) && (modMod.Id != "0") && !string.IsNullOrEmpty(modUpdate.Id) && (modUpdate.Id != "0"))
									{
										if (string.Equals(modMod.Id, modUpdate.Id, StringComparison.InvariantCultureIgnoreCase))
										{
											if (!_newDownloadID.ContainsKey(Filename))
												_newDownloadID.Add(Filename, modUpdate.DownloadId);
										}
									}
								}
							}

							if (!string.IsNullOrEmpty(modMod.DownloadId) && string.IsNullOrWhiteSpace(modUpdate.DownloadId))
								modUpdate.DownloadId = modMod.DownloadId;

							if (_missingDownloadId != false)
							{
								modUpdate.HumanReadableVersion = !string.IsNullOrEmpty(modMod.HumanReadableVersion) ? modMod.HumanReadableVersion : modUpdate.HumanReadableVersion;
								modUpdate.MachineVersion = modMod.MachineVersion != null ? modMod.MachineVersion : modUpdate.MachineVersion;
							}

							if ((modMod.CustomCategoryId != 0) && (modMod.CustomCategoryId != -1))
								modUpdate.CustomCategoryId = modMod.CustomCategoryId;
							
							modUpdate.UpdateWarningEnabled = modMod.UpdateWarningEnabled;
							modUpdate.UpdateChecksEnabled = modMod.UpdateChecksEnabled;
							AutoUpdater.AddNewVersionNumberForMod(modMod, modUpdate);

							if (!_overrideLocalModNames)
								modUpdate.ModName = modMod.ModName;

							modMod.UpdateInfo(modUpdate, null);
							ItemProgress = 0;
						}
					}
				}
			}
			catch (RepositoryUnavailableException e)
			{
				return "The check failed for a server-side issue:" + Environment.NewLine + Environment.NewLine + e.Message;
			}

			return null;
		}
	}
}
