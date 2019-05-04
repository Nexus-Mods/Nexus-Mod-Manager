namespace Nexus.Client.DownloadMonitoring.UI
{
    using System;
    using System.ComponentModel;
    using System.Collections.Generic;
    using System.Linq;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.Commands.Generic;
    using Nexus.Client.ModManagement;
    using Nexus.Client.ModRepositories;
    using Nexus.Client.Settings;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Collections;
    
    /// <summary>
    /// This class encapsulates the data and the operations presented by UI
    /// elements that display Download monitoring.
    /// </summary>
    public class DownloadMonitorVM : INotifyPropertyChanged
	{
        readonly ModManager _modManager;
		private static readonly object Lock = new object();

		#region  events

		/// <summary>
		/// Raised when purging Downloads.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> PurgingDownloads = delegate { };

		#endregion

		#region Properties

		/// <summary>
		/// Raised whenever a property of the class changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmActionMethod ConfirmUpdaterAction = delegate { return true; };

		#region Commands

		/// <summary>
		/// Gets the command to cancel a task.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be cancel.
		/// </remarks>
		/// <value>The command to cancel a task.</value>
		public Command<AddModTask> CancelTaskCommand { get; private set; }

		/// <summary>
		/// Gets the command to remove a task from the list.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be removed.
		/// </remarks>
		/// <value>The command to remove a task from the list.</value>
		public Command<AddModTask> RemoveTaskCommand { get; private set; }

		/// <summary>
		/// Gets the command to pause a task.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be paused.
		/// </remarks>
		/// <value>The command to pause a task.</value>
		public Command<AddModTask> PauseTaskCommand { get; private set; }

		/// <summary>
		/// Gets the command to resume a task.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be resumed.
		/// </remarks>
		/// <value>The command to resume a task.</value>
		public Command<AddModTask> ResumeTaskCommand { get; private set; }

		/// <summary>
		/// Gets the number of maximum allowed concurrent downloads.
		/// </summary>
		/// <value>The number of maximum allowed concurrent downloads.</value>
		public int MaxConcurrentDownloads
		{
			get
			{
				if (ModRepository != null)
					return ModRepository.MaxConcurrentDownloads;
				else
					return 5;
			}
		}

		#endregion

		/// <summary>
		/// Gets the Download manager to use to manage the monitored activities.
		/// </summary>
		/// <value>The Download manager to use to manage the monitored activities.</value>
		protected DownloadMonitor DownloadMonitor { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<AddModTask> Tasks => DownloadMonitor.Tasks;

        /// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public ReadOnlyObservableList<AddModTask> ActiveTasks => DownloadMonitor.ActiveTasks;

        /// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public List<AddModTask> RunningTasks
		{
			get
			{
				ReadOnlyObservableList<AddModTask> rolTasks = DownloadMonitor.Tasks;
				lock (rolTasks)
					return rolTasks.Where(x => x.Status == TaskStatus.Running || x.Status == TaskStatus.Retrying).ToList();
			}
		}

		/// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public AddModTask QueuedTask
		{
			get
			{
				return DownloadMonitor.Tasks.Where(x => x.Status == TaskStatus.Queued).FirstOrDefault();
			}
		}

		/// <summary>
		/// Gets the total task speed.
		/// </summary>
		/// <value>The total task speed.</value>
		public int TotalSpeed => DownloadMonitor.TotalSpeed;

        /// <summary>
		/// Gets the total download progress.
		/// </summary>
		/// <value>The total download progress.</value>
		public long TotalProgress => DownloadMonitor.TotalProgress;

        /// <summary>
		/// Gets the total maximum download progress.
		/// </summary>
		/// <value>The total maximum download progress.</value>
		public long TotalMaxProgress => DownloadMonitor.TotalMaximumProgress;

        /// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_amnDownloadMonitor">The Download manager to use to manage the monitored activities.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		public DownloadMonitorVM(DownloadMonitor p_amnDownloadMonitor, ISettings p_setSettings, ModManager pModManager, IModRepository p_mrpModRepository)
		{
			DownloadMonitor = p_amnDownloadMonitor;
			Settings = p_setSettings;
			_modManager = pModManager;
			ModRepository = p_mrpModRepository;
			ModRepository.UserStatusUpdate += new System.EventHandler(ModRepository_UserStatusUpdate);
			DownloadMonitor.PropertyChanged += new PropertyChangedEventHandler(ActiveTasks_PropertyChanged);

			CancelTaskCommand = new Command<AddModTask>("Cancel", "Cancels the selected Download.", CancelTask);
			RemoveTaskCommand = new Command<AddModTask>("Remove", "Removes the selected Download.", RemoveTask);
			PauseTaskCommand = new Command<AddModTask>("Pause", "Pauses the selected Download.", PauseTask);
			ResumeTaskCommand = new Command<AddModTask>("Resume", "Resumes the selected Download.", ResumeTask);
		}

		#endregion

		private void ActiveTasks_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if ((e.PropertyName == "TotalSpeed") || (e.PropertyName == "TotalProgress") || (e.PropertyName == "TotalMaxProgress"))
			{
				OnPropertyChanged(e.PropertyName);
			}
		}

		/// <summary>
		/// Raises the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the project.
		/// </summary>
		/// <param name="name">The property name.</param>
		protected void OnPropertyChanged(string name)
		{
			PropertyChangedEventHandler handler = PropertyChanged;
			if (handler != null)
			{
				handler(this, new PropertyChangedEventArgs(name));
			}
		}


		#region Cancel Command

		/// <summary>
		/// Cancels the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to cancel.</param>
		public void CancelTask(IBackgroundTask p_tskTask)
		{
			lock (Lock)
				p_tskTask.Cancel();
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be cancelled.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be cancelled.</param>
		/// <returns><c>true</c> if the task can be cancelled;
		/// <c>false</c> otherwise.</returns>
		public bool CanCancelTask(IBackgroundTask p_tskTask)
		{
			return (p_tskTask.Status == TaskStatus.Paused) || (p_tskTask.Status == TaskStatus.Incomplete) || (p_tskTask.InnerTaskStatus == TaskStatus.Retrying) || (p_tskTask.Status == TaskStatus.Queued);
		}

		#endregion

		#region Remove Command

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">IBackgroundTask task to remove.</param>
		public void RemoveTask(AddModTask p_tskTask)
		{
			lock (Lock)
			{
				if (DownloadMonitor.CanRemove(p_tskTask))
					DownloadMonitor.RemoveDownload(p_tskTask);
			}
		}

		/// <summary>
		/// Removes all the completed/failed tasks.
		/// </summary>
		public void RemoveAllTasks()
		{
			List<IBackgroundTask> lstTasks = new List<IBackgroundTask>();

			lock (Tasks)
			{
				foreach (IBackgroundTask btTask in Tasks)
					lstTasks.Add(btTask);
			}

			if (lstTasks.Count > 0)
				foreach (IBackgroundTask btRemovable in lstTasks)
				{
					lock (Lock)
						RemoveTask((AddModTask)btRemovable);
				}
		}

		/// <summary>
		/// Removes all the paused/queued tasks.
		/// </summary>
		public void PurgeDownloads()
		{
			List<IBackgroundTask> lstTasks = new List<IBackgroundTask>();

			lock (Tasks)
			{
				foreach (IBackgroundTask btTask in Tasks)
					lstTasks.Add(btTask);
			}

			PurgingDownloads(this, new EventArgs<IBackgroundTask>(DownloadMonitor.PurgeDownloads(lstTasks, ConfirmUpdaterAction)));
		}
		
		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be removed.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed.</param>
		/// <returns><c>true</c> if the task can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveDownload(IBackgroundTask p_tskTask)
		{
			return DownloadMonitor.CanRemove(p_tskTask);
		}

		#endregion

		#region Pause Command

		/// <summary>
		/// Puases the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to pause.</param>
		public void PauseTask(IBackgroundTask p_tskTask)
		{
			lock (Lock)
			{
				if (DownloadMonitor.CanPause(p_tskTask))
					DownloadMonitor.PauseDownload(p_tskTask);
			}
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be paused.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be paused.</param>
		/// <returns><c>true</c> if the task can be paused;
		/// <c>false</c> otherwise.</returns>
		public bool CanPauseDownload(IBackgroundTask p_tskTask)
		{
			return DownloadMonitor.CanPause(p_tskTask);
		}

		#endregion

		#region Queue Command

		/// <summary>
		/// Puases the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to pause.</param>
		public void QueueTask(IBackgroundTask p_tskTask)
		{
			lock (Lock)
			{
				if (DownloadMonitor.CanQueue(p_tskTask))
					DownloadMonitor.QueueDownload(p_tskTask);
			}
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be paused.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be paused.</param>
		/// <returns><c>true</c> if the task can be paused;
		/// <c>false</c> otherwise.</returns>
		public bool CanQueueDownload(IBackgroundTask p_tskTask)
		{
			return DownloadMonitor.CanQueue(p_tskTask);
		}

		#endregion

		#region Resume Command

		/// <summary>
		/// Resumes the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to resume.</param>
		public void ResumeTask(IBackgroundTask p_tskTask)
		{
			bool booCanResume = false;

			lock (ModRepository)
			{
				if (ModRepository.IsOffline && (!ModRepository.SupportsUnauthenticatedDownload))
				{
					if (_modManager.Login())
						booCanResume = true;
				}
				else
					booCanResume = true;
			}

			lock (RunningTasks)
			{
				int MaxDownloads = 0;
				if (Settings.MaxConcurrentDownloads > MaxConcurrentDownloads)
					MaxDownloads = MaxConcurrentDownloads;
				else
					MaxDownloads = Settings.MaxConcurrentDownloads;


				if (RunningTasks.Count >= MaxDownloads)
				{
					if ((p_tskTask.SupportsQueue) && (p_tskTask.IsRemote))
						lock (Lock)
							p_tskTask.Queue();

					booCanResume = false;
				}
			}

			if (booCanResume)
				if (DownloadMonitor.CanResume(p_tskTask))
				{
					BackgroundWorker bgwWorker;
					bgwWorker = new BackgroundWorker();
					bgwWorker.DoWork += new DoWorkEventHandler(bgwWorker_DoWork);
					bgwWorker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bgwWorker_RunWorkerCompleted);
					bgwWorker.RunWorkerAsync(p_tskTask);
				}
		}

		/// <summary>
		/// Resumes all paused tasks.
		/// </summary>
		public void ResumeAllTasks(List<AddModTask> p_lstAddModTask)
		{
			int TaskCounter = 0;

			if (ModRepository.IsOffline)
				_modManager.Login();
			else
			{
				List<IBackgroundTask> lstTasks = new List<IBackgroundTask>();

				lock (Tasks)
				{
					foreach (IBackgroundTask btTask in Tasks)
						if ((btTask.Status == TaskStatus.Paused) || (btTask.Status == TaskStatus.Incomplete))
							if(p_lstAddModTask == null || p_lstAddModTask.Contains(btTask))
								lstTasks.Add(btTask);
				}

				if (lstTasks.Count > 0)
					foreach (IBackgroundTask btPaused in lstTasks)
					{
						if (TaskCounter < Settings.MaxConcurrentDownloads)
						{
							lock (Lock)
								ResumeTask(btPaused);
							TaskCounter++;
						}
						else
							QueueTask(btPaused);
					}
			}
		}

		/// <summary>
		/// Resumes all paused tasks.
		/// </summary>
		public void ResumeAllTasks()
		{
			ResumeAllTasks(null);
		}


		void bgwWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			DownloadMonitor.ResumeDownload((IBackgroundTask)e.Argument);
		}

		void bgwWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{

		}

		/// <summary>
		/// Handles the <see cref="m_mrpModRepository.UserStatusUpdate"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Updates the UI elements.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ModRepository_UserStatusUpdate(object sender, System.EventArgs e)
		{
			List<IBackgroundTask> lstTasks = new List<IBackgroundTask>();

			lock (ModRepository)
				if (ModRepository.IsOffline)
				{
					lock (Tasks)
					{
						foreach (IBackgroundTask btTask in Tasks)
							lstTasks.Add(btTask);
					}
					if (lstTasks.Count > 0)
						foreach (IBackgroundTask btActive in lstTasks)
							lock (Lock)
								PauseTask(btActive);
				}
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be resumed.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be resumed.</param>
		/// <returns><c>true</c> if the task can be resumed;
		/// <c>false</c> otherwise.</returns>
		public bool CanResumeDownload(IBackgroundTask p_tskTask)
		{
			return DownloadMonitor.CanResume(p_tskTask);
		}

		#endregion
	}
}
