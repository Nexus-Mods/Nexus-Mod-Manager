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
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using ChinhDo.Transactions;

namespace Nexus.Client.ModManagement
{
	public class ReadMeSetupTask : ThreadedBackgroundTask
	{
		bool m_booCancel = false;

		#region Properties

		/// <summary>
		/// Gets the AutoUpdater.
		/// </summary>
		/// <value>The AutoUpdater.</value>
		protected ReadMeManager rmmReadMeManager { get; private set; }
		protected TxFileManager tfmFileManager = new TxFileManager();
		protected List<IMod> m_lstModList = new List<IMod>();

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public ReadMeSetupTask(ReadMeManager p_rmmReadMeManager, List<IMod> p_lstModList)
		{
			rmmReadMeManager = p_rmmReadMeManager;
			m_lstModList = p_lstModList;
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
			OverallMessage = "Scanning mod archives for readme files...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			OverallProgressMaximum = m_lstModList.Count;
			ShowItemProgress = false;
			
			ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];

			foreach (IMod modMod in m_lstModList)
			{
				rmmReadMeManager.VerifyReadMeFile(tfmFileManager, modMod.Filename);
				if (OverallProgress < OverallProgressMaximum)
					StepOverallProgress();
				if (m_booCancel)
					break;
			}

			rmmReadMeManager.SaveReadMeConfig();

			return null;
		}
	}
}
