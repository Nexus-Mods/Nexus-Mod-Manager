using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Nexus.Client.ModManagement.InstallationLog;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	public class DeactivateMultipleModsTask : ThreadedBackgroundTask
	{
		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected VirtualModActivator VirtualModActivator { get; private set; }

		private IInstallLog m_iilInstallLog = null;
		private ModInstallerFactory m_mifModInstallerFactory = null;
		private ReadOnlyObservableList<IMod> m_rolModList = null;
		private string m_strLogPath = String.Empty;
		bool m_booCancel = false;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public DeactivateMultipleModsTask(ReadOnlyObservableList<IMod> p_rolModList, IInstallLog p_iilInstallLog, ModInstallerFactory p_mifModInstallerFactory, VirtualModActivator p_vmaVirtualModActivator, string p_strScriptedLogPath)
		{
			m_iilInstallLog = p_iilInstallLog;
			m_mifModInstallerFactory = p_mifModInstallerFactory;
			m_rolModList = p_rolModList;
			VirtualModActivator = p_vmaVirtualModActivator;
			m_strLogPath = p_strScriptedLogPath;
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
			OverallMessage = "Uninstalling all the active mods...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_rolModList.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)p_objArgs[0];

			foreach (IMod modMod in m_rolModList)
			{
				OverallMessage = "Uninstalling all the active mods: " + modMod.ModName;

				if (VirtualModActivator != null)
					VirtualModActivator.DisableMod(modMod);

				modMod.InstallDate = null;
				if (!m_iilInstallLog.ActiveMods.Contains(modMod))
					continue;
				ModUninstaller munUninstaller = m_mifModInstallerFactory.CreateUninstaller(modMod, m_rolModList);
				munUninstaller.Install();

				while (!munUninstaller.IsCompleted)
				{ }
				DeleteXMLInstalledFile(modMod);

				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();

				if (m_booCancel)
					break;
			}
			return null;
		}

		/// <summary>
		/// If the mod is scripted, deletes the XMLInstalledFiles file inside the InstallInfo\Scripted folder.
		/// </summary>
		private void DeleteXMLInstalledFile(IMod p_modMod)
		{
			string strInstallFilesPath = Path.Combine(m_strLogPath, "Scripted", Path.GetFileNameWithoutExtension(p_modMod.Filename)) + ".xml";
			if (File.Exists(strInstallFilesPath))
				FileUtil.ForceDelete(strInstallFilesPath);
		}
	}
}
