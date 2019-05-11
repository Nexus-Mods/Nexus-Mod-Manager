using System;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	class DeleteMultipleModsTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Fields

		private VirtualModActivator m_ivaVirtualModActivator = null;
		private ReadOnlyObservableList<IMod> m_rolModList = null;
		private ModRegistry ManagedModRegistry = null;
		private ModManager ModManager = null;
		private ReadOnlyObservableList<IMod> ActiveMods = null;
		private ModInstallerFactory InstallerFactory = null;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public DeleteMultipleModsTask(ReadOnlyObservableList<IMod> p_rolModList, VirtualModActivator p_ivaVirtualModActivator, ModRegistry p_ManagedModRegistry, ModManager p_ModManager, ReadOnlyObservableList<IMod> p_rolActiveMods, ModInstallerFactory p_InstallerFactory)
		{
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_rolModList = p_rolModList;
			ManagedModRegistry = p_ManagedModRegistry;
			ModManager = p_ModManager;
			ActiveMods = p_rolActiveMods;
			InstallerFactory = p_InstallerFactory;
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
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			OverallMessage = "Deleting all the selected mods...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_rolModList.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			foreach (IMod modMod in m_rolModList)
			{
				OverallMessage = "Deleting: " + modMod.ModName;

				ModDeleter mddDeleter = InstallerFactory.CreateDelete(modMod, ActiveMods);
				mddDeleter.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(Deactivator_TaskSetCompleted);
				mddDeleter.Install();
				DeleteXMLInstalledFile(modMod);

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;
			}
			return null;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of the mod deletion
		/// mod deativator.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void Deactivator_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			if (e.Success)
				ManagedModRegistry.UnregisterMod((IMod)e.ReturnValue);
		}


		/// <summary>
		/// If the mod is scripted, deletes the XMLInstalledFiles file inside the InstallInfo\Scripted folder.
		/// </summary>
		private void DeleteXMLInstalledFile(IMod p_modMod)
		{
			string strInstallFilesPath = Path.Combine(Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(p_modMod.Filename)) + ".xml";
			if (File.Exists(strInstallFilesPath))
				FileUtil.ForceDelete(strInstallFilesPath);
		}
	}
}
