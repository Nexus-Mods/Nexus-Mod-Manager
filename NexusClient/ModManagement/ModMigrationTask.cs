using System;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement
{
	public class ModMigrationTask : ThreadedBackgroundTask
	{
		private bool m_booMigrate = true;

		protected ConfirmActionMethod ConfirmActionMethod { get; private set; }
		protected MainFormVM ViewModel { get; private set; }
		protected ModManagerControl ModManagerControl { get; private set; }

		#region Properties


		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public ModMigrationTask(MainFormVM p_fvmViewModel, ModManagerControl p_mmgModManagerControl, bool p_booMigrate, ConfirmActionMethod p_camConfirm)
		{
			ViewModel = p_fvmViewModel;
			ModManagerControl = p_mmgModManagerControl;
			m_booMigrate = p_booMigrate;
			ConfirmActionMethod = p_camConfirm;
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
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			Update(ConfirmActionMethod);
		}

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update(ConfirmActionMethod p_camConfirm)
		{
			Start(p_camConfirm);
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			byte[] bteLoadOrder = null;
			byte[] bteModList = null;
			byte[] bteIniList = null;
			int intModCount = -1;

			ShowItemProgress = false;
			OverallMessage = "Initializing migration...";
			OverallProgress = 0;
			OverallProgressMaximum = 8;
			OverallProgressStepSize = 1;

			if (m_booMigrate)
			{
				OverallMessage = "Setup: Exporting Load Order (if available)";
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				if (ViewModel.GameMode.UsesPlugins)
					bteLoadOrder = ViewModel.PluginManagerVM.ExportLoadOrder();
				OverallMessage = "Setup: Exporting Mod List (this could take a lot)";
				Thread.Sleep(1);
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				bteModList = ViewModel.ModManager.InstallationLog.GetXMLModList();
				OverallMessage = "Setup: Exporting IniEdits List";
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				bteIniList = ViewModel.ModManager.InstallationLog.GetXMLIniList();
				intModCount = ViewModel.ModManager.ActiveMods.Count;
				OverallMessage = "Setup: Backing Up Current Profile";
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				AddNewProfile(bteModList, bteIniList, bteLoadOrder, intModCount, true);
				bteModList = null;
				bteIniList = null;
				bteLoadOrder = null;
			}

			OverallMessage = "Setup: Uninstalling Active Mods";
			if (OverallProgress < OverallProgressMaximum)
				StepOverallProgress();
			UninstallAllMods(true, true);

			OverallMessage = "Setup: Virtual Mod Setup";
			if (OverallProgress < OverallProgressMaximum)
				StepOverallProgress();
			ViewModel.ModManager.VirtualModActivator.Setup();
			if (m_booMigrate)
			{
				OverallMessage = "Migration: Migrating Profile";
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				ViewModel.ProfileManager.RestoreBackupProfile();
				OverallMessage = "Migration: Activating Profile";
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				return null;
			}

			OverallMessage = "Migration: Complete";
			return null;
		}

		private void AddNewProfile(byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, int p_intModCount, bool p_booBackup)
		{
			string[] strOptionalFiles = null;

			if (ViewModel.GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
				if ((ViewModel.PluginManager != null) && ((ViewModel.PluginManager.ActivePlugins != null) && (ViewModel.PluginManager.ActivePlugins.Count > 0)))
					strOptionalFiles = ViewModel.GameMode.GetOptionalFilesList(ViewModel.PluginManager.ActivePlugins.Select(x => x.Filename).ToArray());

			if (p_booBackup)
				ViewModel.ProfileManager.BackupProfile(p_bteModList, p_bteIniList, p_bteLoadOrder, ViewModel.GameMode.ModeId, p_intModCount, strOptionalFiles);
			else
				ViewModel.ProfileManager.AddProfile(p_bteModList, p_bteIniList, p_bteLoadOrder, ViewModel.GameMode.ModeId, p_intModCount, strOptionalFiles);
		}

		/// <summary>
		/// Uninstall all active mods.
		/// </summary>
		protected void UninstallAllMods(bool p_booForceUninstall, bool p_booSilent)
		{
			ModManagerControl.DeactivateAllMods(p_booForceUninstall, p_booSilent);
		}

		public void SetStatus(string p_strOverallMessage, Int32 p_intOverallProgress, TaskStatus p_tstStatus)
		{
			if (!p_strOverallMessage.Equals(OverallMessage))
				OverallMessage = p_strOverallMessage;
			if (!(p_intOverallProgress == OverallProgress))
				OverallProgress = p_intOverallProgress;
			if (!(p_tstStatus == Status))
			{
				Status = p_tstStatus;
			}
		}
	}
}
