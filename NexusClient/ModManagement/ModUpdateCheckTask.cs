using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
		public ModUpdateCheckTask(AutoUpdater p_AutoUpdater, IModRepository p_ModRepository, List<IMod> p_lstModList)
		{
			AutoUpdater = p_AutoUpdater;
			ModRepository = p_ModRepository;
			m_lstModList.AddRange(p_lstModList);
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
			ItemProgressMaximum = (m_lstModList.Count > 250) ? 250 : m_lstModList.Count;

			for (int i = 0; i < m_lstModList.Count; i++)
			{
				string modID = String.Empty;
				Int32 isEndorsed = 0;
				string strLastVersion = String.Empty;
				ItemMessage = m_lstModList[i].ModName;

				if (m_booCancel)
					break;

				if (!String.IsNullOrEmpty(m_lstModList[i].Id))
				{
					modID = m_lstModList[i].Id;
					isEndorsed = m_lstModList[i].IsEndorsed ? 1 : 0;
					strLastVersion = m_lstModList[i].LastKnownVersion;

				}
				else
				{
					try
					{
						IModInfo mifInfo = ModRepository.GetModInfoForFile(m_lstModList[i].Filename);

						if (mifInfo != null)
						{
							modID = mifInfo.Id;
							m_lstModList[i].Id = modID;
							strLastVersion = m_lstModList[i].LastKnownVersion;
							AutoUpdater.AddNewVersionNumberForMod(m_lstModList[i], mifInfo);
						}
					}
					catch (RepositoryUnavailableException)
					{
						//the repository is not available, so don't bother
					}
				}

				if (!String.IsNullOrEmpty(modID))
				{
					if ((m_booOverrideCategorySetup) || (String.IsNullOrEmpty(strLastVersion)))
						ModList.Add(String.Format("{0}", modID));
					else
						ModList.Add(String.Format("{0}|{1}|{2}", modID, m_lstModList[i].HumanReadableVersion, isEndorsed));
				}

				if (ItemProgress < ItemProgressMaximum)
					StepItemProgress();
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;

				// Prevents the repository request string from becoming too long.
				if (ModList.Count == 250)
				{
					CheckForModListUpdate(ModList);
					ModList.Clear();
					OverallMessage = "Updating mods info: setup search...";
					ItemProgress = 0;
					ItemProgressMaximum = (m_lstModList.Count == 250) ? 1 : (m_lstModList.Count - (i + 1));
				}
			}

			if (!m_booCancel && (ModList.Count > 0))
				CheckForModListUpdate(ModList);

			m_lstModList.Clear();

			return null;
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
					mifInfo = ModRepository.GetModListInfo(p_lstModList);

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

						ItemMessage = modUpdate.ModName;

						foreach (IMod modMod in m_lstModList.Where(x => (String.IsNullOrEmpty(modUpdate.Id) ? "0" : modUpdate.Id) == x.Id))
						{
							if (ItemProgress < ItemProgressMaximum)
								StepItemProgress();
							modUpdate.CustomCategoryId = modMod.CustomCategoryId;
							modUpdate.UpdateWarningEnabled = modMod.UpdateWarningEnabled;
							AutoUpdater.AddNewVersionNumberForMod(modMod, modUpdate);
							modMod.UpdateInfo(modUpdate, false);
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
