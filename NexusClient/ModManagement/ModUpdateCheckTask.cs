using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement
{
	public class ModUpdateCheckTask : ThreadedBackgroundTask
	{
		private bool m_booCancel = false;
		private bool m_booOverrideCategorySetup = false;
		private bool? m_booMissingDownloadId = false;
		private List<IMod> m_lstModList = new List<IMod>();
		private int m_intRetries = 0;
		private bool OverrideLocalModNames = false;
		private Dictionary<string, string> dctNewDownloadID = new Dictionary<string, string>();

		#region Properties

		/// <summary>
		/// Gets the AutoUpdater.
		/// </summary>
		/// <value>The AutoUpdater.</value>
		protected AutoUpdater AutoUpdater { get; private set; }

		/// <summary>
		/// Gets the current mod repository.
		/// </summary>
		/// <value>The current mod repository.</value>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the current profile manager.
		/// </summary>
		/// <value>The current profile manager.</value>
		protected IProfileManager ProfileManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_AutoUpdater">The AutoUpdater.</param>
		/// <param name="p_ModRepository">The current mod repository.</param>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		public ModUpdateCheckTask(AutoUpdater p_AutoUpdater, IProfileManager p_prmProfileManager, IModRepository p_ModRepository, List<IMod> p_lstModList, bool p_booOverrideCategorySetup, bool? p_booMissingDownloadId, bool p_booOverrideLocalModNames)
		{
			AutoUpdater = p_AutoUpdater;
			ModRepository = p_ModRepository;
			ProfileManager = p_prmProfileManager;
			m_lstModList.AddRange(p_lstModList);
			m_booOverrideCategorySetup = p_booOverrideCategorySetup;
			m_booMissingDownloadId = p_booMissingDownloadId;
			OverrideLocalModNames = p_booOverrideLocalModNames;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			base.Cancel();
			m_booCancel = true;
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			int intModLimit = 75;
			if (m_booMissingDownloadId != false)
				intModLimit = 75;

			List<string> ModList = new List<string>();
			List<IMod> ModCheck = new List<IMod>();
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			OverallMessage = "Updating mods info: setup search..";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ItemProgress = 0;
			ItemProgressStepSize = 1;
			ItemProgressMaximum = 1;
			OverallProgressMaximum = 1;

			OverallProgressMaximum = m_lstModList.Count * 2;
			ItemProgressMaximum = (m_lstModList.Count > intModLimit) ? intModLimit : m_lstModList.Count;

			for (int i = 0; i < m_lstModList.Count; i++)
			{
				IMod modCurrent = m_lstModList[i];
				string modID = string.Empty;
				string modName = string.Empty;
				int isEndorsed = 0;
				ItemMessage = modCurrent.ModName;

				if (m_booCancel)
					break;

				modName = StripFileName(modCurrent.Filename, modCurrent.Id);

				if (!string.IsNullOrEmpty(modCurrent.Id))
				{
					modID = modCurrent.Id;
					isEndorsed = modCurrent.IsEndorsed == true ? 1 : (modCurrent.IsEndorsed == false ? -1 : 0);
				}
				else
				{
					try
					{
						IModInfo mifInfo = ModRepository.GetModInfoForFile(modCurrent.Filename);

						if (mifInfo != null)
						{
							modCurrent.Id = mifInfo.Id;
							modID = mifInfo.Id;
							AutoUpdater.AddNewVersionNumberForMod(modCurrent, mifInfo);
							modName = StripFileName(modCurrent.Filename, mifInfo.Id);
						}
					}
					catch (RepositoryUnavailableException)
					{
						//the repository is not available, so don't bother
					}
				}

				if ((m_booMissingDownloadId == null) || ((m_booMissingDownloadId == true) && (string.IsNullOrEmpty(modCurrent.DownloadId) || (modCurrent.DownloadId == "0") || (modCurrent.DownloadId == "-1"))))
				{
					ModList.Add(string.Format("{0}|{1}|{2}", modName, string.IsNullOrWhiteSpace(modID) ? "0" : modID, Path.GetFileName(modCurrent.Filename)));
					ModCheck.Add(modCurrent);
				}
				else if ((m_booMissingDownloadId == false) && !string.IsNullOrEmpty(modCurrent.DownloadId))
				{
					if (m_booOverrideCategorySetup)
						ModList.Add(string.Format("{0}", modCurrent.DownloadId));
					else
						ModList.Add(string.Format("{0}|{1}|{2}|{3}|{4}", string.IsNullOrWhiteSpace(modCurrent.DownloadId) ? "0" : modCurrent.DownloadId, string.IsNullOrWhiteSpace(modID) ? "0" : modID, Path.GetFileName(modName), string.IsNullOrWhiteSpace(modCurrent.HumanReadableVersion) ? "0" : modCurrent.HumanReadableVersion, isEndorsed));
					ModCheck.Add(modCurrent);
				}

				if (ItemProgress < ItemProgressMaximum)
					StepItemProgress();
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;

				// Prevents the repository request string from becoming too long.
				if (ModList.Count == intModLimit)
				{
					string strResult = CheckForModListUpdate(ModList, ModCheck);

					if (!string.IsNullOrEmpty(strResult))
					{
						ModList.Clear();
						return strResult;
					}

					ModList.Clear();
					OverallMessage = "Updating mods info: setup search..";
					ItemProgress = 0;
					ItemProgressMaximum = (m_lstModList.Count == intModLimit) ? 1 : (m_lstModList.Count - (i + 1));
				}
			}

			if (!m_booCancel && (ModList.Count > 0))
			{
				string strResult = CheckForModListUpdate(ModList, ModCheck);

				if (!string.IsNullOrEmpty(strResult))
				{
					m_lstModList.Clear();
					return strResult;
				}
			}

			m_lstModList.Clear();

			return dctNewDownloadID;
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
			if (m_booMissingDownloadId != false)
				OverallMessage = "Updating mods info: retrieving download ids..";
			else
				OverallMessage = "Updating mods info: getting online updates..";
			List<IModInfo> mifInfo = new List<IModInfo>();
			IMod[] ModCheckList = p_lstModCheck.ToArray();

			try
			{
				//get mod info
				for (int i = 0; i <= m_intRetries; i++)
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
						if (m_booCancel)
							break;
						if (OverallProgress < OverallProgressMaximum)
							StepOverallProgress();

						if (modUpdate == null)
							continue;

						ItemMessage = modUpdate.ModName;

						IMod modMod = null;
						if (m_booMissingDownloadId != false)
							modMod = m_lstModList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(modUpdate.FileName) && (StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename).ToString(), x.Id), StringComparison.OrdinalIgnoreCase) || StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename.Replace("_"," ")).ToString(), x.Id), StringComparison.OrdinalIgnoreCase))).FirstOrDefault();
						else
							modMod = m_lstModList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(modUpdate.FileName) && modUpdate.FileName.Equals(Path.GetFileName(x.Filename).ToString(), StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
						
						if (modMod == null)
						{
							if ((!string.IsNullOrEmpty(modUpdate.DownloadId)) && (modUpdate.DownloadId != "0") && (modUpdate.DownloadId != "-1"))
								modMod = m_lstModList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(x.DownloadId) && modUpdate.DownloadId.Equals(x.DownloadId.ToString(), StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

						}

						if(modMod == null)
						{
							if (m_booMissingDownloadId != false)
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
								if (m_booMissingDownloadId != false)
								{
									string Filename = Path.GetFileName(modMod.Filename);
									if (!string.Equals(StripFileName(Filename, modMod.Id), StripFileName(modUpdate.FileName, modUpdate.Id), StringComparison.InvariantCultureIgnoreCase))
										continue;
								}
							}
							else if (!string.IsNullOrWhiteSpace(modUpdate.DownloadId) && !modUpdate.DownloadId.Equals(modMod.DownloadId))
							{
								if (m_booMissingDownloadId != false)
								{
									string Filename = Path.GetFileName(modMod.Filename);
									if (string.Equals(Filename, modUpdate.FileName, StringComparison.InvariantCultureIgnoreCase))
									{
										if (!dctNewDownloadID.ContainsKey(Filename))
											dctNewDownloadID.Add(Filename, modUpdate.DownloadId);
									}
									else if (!string.IsNullOrEmpty(modMod.Id) && (modMod.Id != "0") && !string.IsNullOrEmpty(modUpdate.Id) && (modUpdate.Id != "0"))
									{
										if (string.Equals(modMod.Id, modUpdate.Id, StringComparison.InvariantCultureIgnoreCase))
										{
											if (!dctNewDownloadID.ContainsKey(Filename))
												dctNewDownloadID.Add(Filename, modUpdate.DownloadId);
										}
									}
								}
							}

							if (!string.IsNullOrEmpty(modMod.DownloadId) && string.IsNullOrWhiteSpace(modUpdate.DownloadId))
								modUpdate.DownloadId = modMod.DownloadId;

							if (m_booMissingDownloadId != false)
							{
								modUpdate.HumanReadableVersion = !string.IsNullOrEmpty(modMod.HumanReadableVersion) ? modMod.HumanReadableVersion : modUpdate.HumanReadableVersion;
								modUpdate.MachineVersion = modMod.MachineVersion != null ? modMod.MachineVersion : modUpdate.MachineVersion;
							}

							if ((modMod.CustomCategoryId != 0) && (modMod.CustomCategoryId != -1))
								modUpdate.CustomCategoryId = modMod.CustomCategoryId;
							
							modUpdate.UpdateWarningEnabled = modMod.UpdateWarningEnabled;
							AutoUpdater.AddNewVersionNumberForMod(modMod, modUpdate);

							if (!OverrideLocalModNames)
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
