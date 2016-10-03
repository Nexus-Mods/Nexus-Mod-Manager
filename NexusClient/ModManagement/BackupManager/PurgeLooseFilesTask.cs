using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;
using SevenZip;

namespace Nexus.Client.ModManagement
{
	public class PurgeLooseFilesTask : ThreadedBackgroundTask
	{
		bool m_booAllowCancel = true;

		#region Fields
			
		private ConfirmActionMethod m_camConfirm = null;
		private BackupManager BackupManager = null;
		
		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		public PurgeLooseFilesTask(BackupManager p_BackupManager, ConfirmActionMethod p_camConfirm)
		{
			BackupManager = p_BackupManager;
			m_camConfirm = p_camConfirm;
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
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
			OverallMessage = "Purging Loose Files...";
			OverallProgress = 0;
			OverallProgressStepSize = 1;
			ShowItemProgress = true;
			ItemProgressStepSize = 1;

			BackupManager.Initialize();
			BackupManager.CheckLooseFiles(true);

			if (BackupManager.lstLooseFiles.Count == 0)
			{
				MessageBox.Show("Your game folder is already clean!", "Purge Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return null;
			}
			else
			{
				foreach (BackupInfo file in BackupManager.lstLooseFiles)
				{
					if (ItemProgressStepSize < BackupManager.lstLooseFiles.Count())
					{
						ItemMessage = file.VirtualModPath;
						StepItemProgress();
					}

					OverallMessage = string.Format("Purging files...{0}/{1}", ItemProgressStepSize++, BackupManager.lstLooseFiles.Count());
					StepOverallProgress();

					if (File.Exists(file.RealModPath))
						FileUtil.ForceDelete(file.RealModPath);
				}

				BackupManager.LooseFilesSize = 0;
				BackupManager.strLooseFilesSize = "0";
			}

			return BackupManager.lstLooseFiles;
		}
				
	}
}
