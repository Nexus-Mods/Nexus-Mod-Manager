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
	public class ActivateMultipleModsTask : ThreadedBackgroundTask
	{
		bool m_booAllowCancel = true;

		#region Fields

		private IInstallLog m_iilInstallLog = null;
		private ModInstallerFactory m_mifModInstallerFactory = null;
		private List<IMod> m_lstModList = null;
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate = null;
		private ConfirmActionMethod m_camConfirm = null;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ActivateMultipleModsTask(List<IMod> p_lstModList, IInstallLog p_iilInstallLog, ModInstallerFactory p_mifModInstallerFactory, ConfirmActionMethod p_camConfirm, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			m_iilInstallLog = p_iilInstallLog;
			m_mifModInstallerFactory = p_mifModInstallerFactory;
			m_lstModList = p_lstModList;
			m_camConfirm = p_camConfirm;
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate;
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
		/// Resumes the task.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown if the task is not paused.</exception>
		public override void Resume()
		{
			Update(m_camConfirm);
		}

		/// <summary>
		/// Cancels the update.
		/// </summary>
		public override void Cancel()
		{
			if (m_booAllowCancel)
				base.Cancel();
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			int modCounter = 0;
			if (m_lstModList != null && m_lstModList.Count > 0)
				modCounter = m_lstModList.Count;

			OverallMessage = string.Format("Installing selected mods ({0})...", modCounter);
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_lstModList.Count;
			ShowItemProgress = false;

			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			foreach (IMod modMod in m_lstModList)
			{
				OverallMessage = "Installing selected mods: " + modMod.ModName;

				if (m_iilInstallLog.ActiveMods.Contains(modMod))
					continue;
				
				ModInstaller minInstaller = m_mifModInstallerFactory.CreateInstaller(modMod, m_dlgOverwriteConfirmationDelegate, null);
				minInstaller.Install();

				while (!minInstaller.IsCompleted)
				{ }
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
			}

			return null;
		}
	}
}
