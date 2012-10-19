using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Settings;
using Nexus.Client.Util.Collections;
using System.ComponentModel;

namespace Nexus.Client.DownloadMonitoring.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display Download monitoring.
	/// </summary>
	public class DownloadMonitorVM : INotifyPropertyChanged
	{
		bool m_booOfflineMode = false;

		#region Properties

		/// <summary>
		/// Raised whenever a property of the class changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		#region Commands

		/// <summary>
		/// Gets the command to cancel a task.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be cancel.
		/// </remarks>
		/// <value>The command to cancel a task.</value>
		public Command<IBackgroundTask> CancelTaskCommand { get; private set; }

		/// <summary>
		/// Gets the command to remove a task from the list.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be removed.
		/// </remarks>
		/// <value>The command to remove a task from the list.</value>
		public Command<IBackgroundTask> RemoveTaskCommand { get; private set; }

		/// <summary>
		/// Gets the command to pause a task.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be paused.
		/// </remarks>
		/// <value>The command to pause a task.</value>
		public Command<IBackgroundTask> PauseTaskCommand { get; private set; }

		/// <summary>
		/// Gets the command to resume a task.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the task to be resumed.
		/// </remarks>
		/// <value>The command to resume a task.</value>
		public Command<IBackgroundTask> ResumeTaskCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the Download manager to use to manage the monitored activities.
		/// </summary>
		/// <value>The Download manager to use to manage the monitored activities.</value>
		protected DownloadMonitor DownloadMonitor { get; private set; }

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<IBackgroundTask> Tasks
		{
			get
			{
				return DownloadMonitor.Tasks;
			}
		}

		/// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public ReadOnlyObservableList<IBackgroundTask> ActiveTasks
		{
			get
			{
				return DownloadMonitor.ActiveTasks;
			}
		}

		/// <summary>
		/// Gets the total task speed.
		/// </summary>
		/// <value>The total task speed.</value>
		public int TotalSpeed
		{
			get
			{
				return DownloadMonitor.TotalSpeed;
			}
		}

		/// <summary>
		/// Gets the total download progress.
		/// </summary>
		/// <value>The total download progress.</value>
		public int TotalProgress
		{
			get
			{
				return DownloadMonitor.TotalProgress;
			}
		}

		/// <summary>
		/// Gets the total maximum download progress.
		/// </summary>
		/// <value>The total maximum download progress.</value>
		public int TotalMaxProgress
		{
			get
			{
				return DownloadMonitor.TotalMaximumProgress;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		/// <summary>
		/// Gets or sets whether the manager is in offline mode.
		/// </summary>
		/// <value>Whether the manager is in offline mode.</value>
		public bool OfflineMode
		{
			get
			{
				return m_booOfflineMode;
			}
			private set
			{
				m_booOfflineMode = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_amnDownloadMonitor">The Download manager to use to manage the monitored activities.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		public DownloadMonitorVM(DownloadMonitor p_amnDownloadMonitor, ISettings p_setSettings, bool p_booOfflineMode)
		{
			DownloadMonitor = p_amnDownloadMonitor;
			Settings = p_setSettings;
			m_booOfflineMode = p_booOfflineMode;
			DownloadMonitor.PropertyChanged += new PropertyChangedEventHandler(ActiveTasks_PropertyChanged);

			CancelTaskCommand = new Command<IBackgroundTask>("Cancel", "Cancels the selected Download.", CancelTask);
			RemoveTaskCommand = new Command<IBackgroundTask>("Remove", "Removes the selected Download.", RemoveTask);
			PauseTaskCommand = new Command<IBackgroundTask>("Pause", "Pauses the selected Download.", PauseTask);
			ResumeTaskCommand = new Command<IBackgroundTask>("Resume", "Resumes the selected Download.", ResumeTask);
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
			return (p_tskTask.Status == TaskStatus.Running) || (p_tskTask.Status == TaskStatus.Paused) || (p_tskTask.Status == TaskStatus.Incomplete) || (p_tskTask.InnerTaskStatus == TaskStatus.Retrying);
		}

		#endregion

		#region Remove Command

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveTask(IBackgroundTask p_tskTask)
		{
			if (DownloadMonitor.CanRemove(p_tskTask))
				DownloadMonitor.RemoveDownload(p_tskTask);
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
			if (DownloadMonitor.CanPause(p_tskTask))
				DownloadMonitor.PauseDownload(p_tskTask);
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

		#region Resume Command

		/// <summary>
		/// Resumes the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to resume.</param>
		public void ResumeTask(IBackgroundTask p_tskTask)
		{
			if (DownloadMonitor.CanResume(p_tskTask))
				DownloadMonitor.ResumeDownload(p_tskTask);
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
