using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModAuthoring;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;


namespace Nexus.Client.ModManagement
{
	public class PurgeDownloadsTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected DownloadMonitoring.DownloadMonitor DownloadMonitor { get; private set; }
		
		protected List<IBackgroundTask> DownloadTasksList = null;
			
		/// <summary>
		/// Gets the delegate to call to confirm an action.
		/// </summary>
		/// <value>The delegate to call to confirm an action.</value>
		protected ConfirmActionMethod ConfirmActionMethod { get; private set; }


		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		/// <param name="p_lstMods">The mod list.</param>
		/// <param name="p_intNewValue">The new category id.</param>
		public PurgeDownloadsTask(List<IBackgroundTask> p_lstDownloadTasks, DownloadMonitoring.DownloadMonitor p_dmDownloadMonitor)
		{
			DownloadTasksList = p_lstDownloadTasks;
			DownloadMonitor = p_dmDownloadMonitor;
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
		public override void Resume()
		{
			Start(ConfirmActionMethod);
		}
				
		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="args">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			try
			{
				ConfirmActionMethod camConfirm = (ConfirmActionMethod)args[0];
				OverallMessage = "Starting purge downloads...";
				OverallProgress = 0;
				OverallProgressStepSize = 1;
				ShowItemProgress = false;
				OverallProgressMaximum = DownloadTasksList.Count();

				Status = TaskStatus.Running;
								
				int i = 1;
				
				if (DownloadTasksList.Count > 0)
				{
					foreach (AddModTask task in DownloadTasksList)
					{
						DownloadMonitor.PurgeDownload((AddModTask)task);
						OverallMessage = "Purging the downloads: " + i + "/" + DownloadTasksList.Count();
						StepOverallProgress();
						i++;
					}
				}
			}
			catch (Exception ex)
			{
				this.Status = TaskStatus.Error;
				this.ItemMessage = ("There was a problem: " + Environment.NewLine + ex.Message);
				return null;
			}

			return null;
		}
	}
}

