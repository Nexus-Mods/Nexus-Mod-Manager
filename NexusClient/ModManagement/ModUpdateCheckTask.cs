using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	public class ModUpdateCheckTask : ThreadedBackgroundTask
	{
		private bool m_booCancel = false;
		private bool m_booOverrideCategorySetup = false;
		private bool m_booMissingDownloadId = false;
		private List<IMod> m_lstModList = new List<IMod>();
		private int m_intRetries = 0;

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

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_AutoUpdater">The AutoUpdater.</param>
		/// <param name="p_ModRepository">The current mod repository.</param>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		public ModUpdateCheckTask(AutoUpdater p_AutoUpdater, IModRepository p_ModRepository, List<IMod> p_lstModList, bool p_booOverrideCategorySetup, bool p_booMissingDownloadId)
		{
			AutoUpdater = p_AutoUpdater;
			ModRepository = p_ModRepository;
			m_lstModList.AddRange(p_lstModList);
			m_booOverrideCategorySetup = p_booOverrideCategorySetup;
			m_booMissingDownloadId = p_booMissingDownloadId;
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
		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			int intModLimit = 100;
			if (m_booMissingDownloadId)
				intModLimit = 100;

			List<string> ModList = new List<string>();
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			OverallMessage = "Updating mods info: setup search...";
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
				string strLastVersion = string.Empty;
				ItemMessage = modCurrent.ModName;

				if (m_booCancel)
					break;



				if (!string.IsNullOrEmpty(modCurrent.Id))
				{
					modID = modCurrent.Id;
					isEndorsed = modCurrent.IsEndorsed == true ? 1 : (modCurrent.IsEndorsed == false ? -1 : 0);
					strLastVersion = modCurrent.LastKnownVersion;
					modName = StripFileName(modCurrent.Filename, modCurrent.Id);
				}
				else
				{
					try
					{
						IModInfo mifInfo = ModRepository.GetModInfoForFile(modCurrent.Filename);

						if (mifInfo != null)
						{
							modID = mifInfo.Id;
							modCurrent.Id = modID;
							strLastVersion = modCurrent.LastKnownVersion;
							AutoUpdater.AddNewVersionNumberForMod(modCurrent, mifInfo);
							modName = mifInfo.ModName;
						}
					}
					catch (RepositoryUnavailableException)
					{
						//the repository is not available, so don't bother
					}
				}

				if (!string.IsNullOrEmpty(modID))
				{
					if (m_booMissingDownloadId && string.IsNullOrEmpty(modCurrent.DownloadId))
						ModList.Add(string.Format("{0}|{1}", Path.GetFileName(modName), modID));
					else if (!string.IsNullOrEmpty(modCurrent.DownloadId))
					{
						if ((m_booOverrideCategorySetup) || (string.IsNullOrEmpty(strLastVersion)))
							ModList.Add(string.Format("{0}", modCurrent.DownloadId));
						else
							ModList.Add(string.Format("{0}|{1}|{2}", modCurrent.DownloadId, modCurrent.HumanReadableVersion, isEndorsed));
					}
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
					CheckForModListUpdate(ModList);
					ModList.Clear();
					OverallMessage = "Updating mods info: setup search...";
					ItemProgress = 0;
					ItemProgressMaximum = (m_lstModList.Count == intModLimit) ? 1 : (m_lstModList.Count - (i + 1));
				}
			}

			if (!m_booCancel && (ModList.Count > 0))
				CheckForModListUpdate(ModList);

			m_lstModList.Clear();

			return null;
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
		private void CheckForModListUpdate(List<string> p_lstModList)
		{
			OverallMessage = "Updating mods info: getting online updates...";
			List<IModInfo> mifInfo = new List<IModInfo>();

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
					ItemProgress = 0;
					ItemProgressMaximum = mifInfo.Count;
					OverallProgressMaximum = OverallProgress + mifInfo.Count;

					foreach (ModInfo modUpdate in mifInfo)
					{
						if (m_booCancel)
							break;
						if (OverallProgress < OverallProgressMaximum)
							StepOverallProgress();

						if (modUpdate == null)
							continue;

						ItemMessage = modUpdate.ModName;


						IMod modMod = null;
						if (m_booMissingDownloadId)
							modMod = m_lstModList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(modUpdate.FileName) && StripFileName(modUpdate.FileName, modUpdate.Id).Equals(StripFileName(Path.GetFileName(x.Filename).ToString(), x.Id), StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
						else
							modMod = m_lstModList.Where(x => x != null).Where(x => !string.IsNullOrEmpty(modUpdate.FileName) && modUpdate.FileName.Equals(Path.GetFileName(x.Filename).ToString(), StringComparison.OrdinalIgnoreCase)).FirstOrDefault();

						if (modMod != null)
						{
							if (ItemProgress < ItemProgressMaximum)
								StepItemProgress();

							if (m_booMissingDownloadId)
							{
								string Filename = Path.GetFileName(modMod.Filename);
								if (!String.Equals(StripFileName(Filename, modMod.Id), StripFileName(modUpdate.FileName, modUpdate.Id), StringComparison.InvariantCultureIgnoreCase))
									continue;
							}
							if (!String.IsNullOrEmpty(modMod.DownloadId) && String.IsNullOrWhiteSpace(modUpdate.DownloadId))
								modUpdate.DownloadId = modMod.DownloadId;


							if (m_booMissingDownloadId)
							{
								modUpdate.HumanReadableVersion = modMod.HumanReadableVersion;
								modUpdate.MachineVersion = modMod.MachineVersion;
							}

							modUpdate.CustomCategoryId = modMod.CustomCategoryId;
							modUpdate.UpdateWarningEnabled = modMod.UpdateWarningEnabled;
							AutoUpdater.AddNewVersionNumberForMod(modMod, modUpdate);
							modMod.UpdateInfo(modUpdate, null);
							ItemProgress = 0;
						}
					}
				}
			}
			catch (RepositoryUnavailableException e)
			{
				Trace.WriteLine(String.Format("ModUpdateCheck FAILED: {0}", e.Message));
			}
		}
	}
}
