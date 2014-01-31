using System.ComponentModel;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.DownloadMonitoring
{
	/// <summary>
	/// This monitors the status of activities.
	/// </summary>
	public class DownloadMonitor : INotifyPropertyChanged
	{
		private ThreadSafeObservableList<AddModTask> m_oclTasks = new ThreadSafeObservableList<AddModTask>();
		private ObservableSet<AddModTask> m_setActiveTasks = new ObservableSet<AddModTask>();
		private int m_TotalSpeed = 0;
		private int m_TotalProgress = 0;
		private int m_TotalMaximumProgress = 0;

		/// <summary>
		/// Raised whenever a property of the class changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;


		#region Properties

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<AddModTask> Tasks { get; private set; }

		/// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public ReadOnlyObservableList<AddModTask> ActiveTasks { get; private set; }

		/// <summary>
		/// Gets or sets the current download speed.
		/// </summary>
		/// <value>The download speed.</value>
		public int TotalSpeed
		{
			get
			{
				return m_TotalSpeed;
			}
			set
			{
				bool booChanged = false;
				if (m_TotalSpeed != value)
				{
					booChanged = true;
					m_TotalSpeed = value;
				}
				if (booChanged)
					OnPropertyChanged("TotalSpeed");
			}
		}

		/// <summary>
		/// Gets or sets the current download progress.
		/// </summary>
		/// <value>The download progress.</value>
		public int TotalProgress
		{
			get
			{
				return m_TotalProgress;
			}
			set
			{
				bool booChanged = false;
				if (m_TotalProgress != value)
				{
					booChanged = true;
					m_TotalProgress = value;
				}
				if (booChanged)
					OnPropertyChanged("TotalProgress");
			}
		}

		/// <summary>
		/// Gets or sets the current download maximum progress.
		/// </summary>
		/// <value>The download maximum progress.</value>
		public int TotalMaximumProgress
		{
			get
			{
				return m_TotalMaximumProgress;
			}
			set
			{
				bool booChanged = false;
				if (m_TotalMaximumProgress != value)
				{
					booChanged = true;
					m_TotalMaximumProgress = value;
				}
				if (booChanged)
					OnPropertyChanged("TotalMaximumProgress");
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DownloadMonitor()
		{
			Tasks = new ReadOnlyObservableList<AddModTask>(m_oclTasks);
			ActiveTasks = new ReadOnlyObservableList<AddModTask>(m_setActiveTasks);
		}

		#endregion

		/// <summary>
		/// Adds a task to the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task to monitor.</param>
		public void AddActivity(AddModTask p_tskTask)
		{
			p_tskTask.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(Task_PropertyChanged);
			m_oclTasks.Add(p_tskTask);
			if (p_tskTask.IsActive)
				m_setActiveTasks.Add(p_tskTask);
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of tasks.
		/// </summary>
		/// <remarks>
		/// This adds or removes tasks from the active task list as appropriate.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.IsActive)))
			{
				AddModTask tskTask = (AddModTask)sender;
				if (tskTask.IsActive)
					m_setActiveTasks.Add(tskTask);
				else
					m_setActiveTasks.Remove(tskTask);
			}

			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<AddModTask>(x => x.TaskSpeed)))
			{
				int speed = 0;
				int progress = 0;
				int maxprogress = 0;
				foreach (AddModTask Task in ActiveTasks)
				{
					speed += Task.TaskSpeed;
					progress += Task.ItemProgress;
					maxprogress += Task.ItemProgressMaximum;
				}

				TotalMaximumProgress = maxprogress;
				TotalProgress = progress;
				TotalSpeed = speed;
			}
		}

		#region Download Removal

		/// <summary>
		/// Removes a task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveDownload(AddModTask p_tskTask)
		{
			if (CanRemove(p_tskTask))
			{
				p_tskTask.PropertyChanged -= new System.ComponentModel.PropertyChangedEventHandler(Task_PropertyChanged);
				m_oclTasks.Remove(p_tskTask);
				m_setActiveTasks.Remove(p_tskTask);
			}
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemove(IBackgroundTask p_tskTask)
		{
			return !p_tskTask.IsActive && !CanResume(p_tskTask) && !(p_tskTask.InnerTaskStatus == TaskStatus.Retrying);
		}

		#endregion

		#region Download Pause/Resume

		/// <summary>
		/// Resumes a task.
		/// </summary>
		/// <param name="p_tskTask">The task to resume.</param>
		public void ResumeDownload(IBackgroundTask p_tskTask)
		{
			if (CanResume(p_tskTask))
				p_tskTask.Resume();
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be resumed.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be resumed.</param>
		/// <returns><c>true</c> if the p_tskTask can be resumed;
		/// <c>false</c> otherwise.</returns>
		public bool CanResume(IBackgroundTask p_tskTask)
		{
			return (p_tskTask.Status == TaskStatus.Paused) || (p_tskTask.Status == TaskStatus.Incomplete) || (p_tskTask.Status == TaskStatus.Queued);
		}

		/// <summary>
		/// Pauses a task.
		/// </summary>
		/// <param name="p_tskTask">The task to pause.</param>
		public void PauseDownload(IBackgroundTask p_tskTask)
		{
			if (CanPause(p_tskTask))
				p_tskTask.Pause();
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be paused.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be paused.</param>
		/// <returns><c>true</c> if the p_tskTask can be paused;
		/// <c>false</c> otherwise.</returns>
		public bool CanPause(IBackgroundTask p_tskTask)
		{
			return p_tskTask.SupportsPause && ((p_tskTask.Status == TaskStatus.Running) || (p_tskTask.Status == TaskStatus.Retrying) || (p_tskTask.InnerTaskStatus == TaskStatus.Retrying));
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

		#endregion
	}
}
