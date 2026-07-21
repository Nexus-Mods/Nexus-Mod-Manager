using System.ComponentModel;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ActivityMonitoring
{
	/// <summary>
	/// This monitors the status of activities.
	/// </summary>
	public class ActivityMonitor
	{
		private ThreadSafeObservableList<IBackgroundTask> m_oclTasks = new ThreadSafeObservableList<IBackgroundTask>();
		private ObservableSet<IBackgroundTask> m_setActiveTasks = new ObservableSet<IBackgroundTask>();

		#region Properties

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<IBackgroundTask> Tasks { get; private set; }

		/// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public ReadOnlyObservableList<IBackgroundTask> ActiveTasks { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ActivityMonitor()
		{
			Tasks = new ReadOnlyObservableList<IBackgroundTask>(m_oclTasks);
			ActiveTasks = new ReadOnlyObservableList<IBackgroundTask>(m_setActiveTasks);
		}

		#endregion

		/// <summary>
		/// Adds a task to the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task to monitor.</param>
		public void AddActivity(IBackgroundTask p_tskTask)
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
			if (e.PropertyName.Equals(ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.IsActive)))
			{
				IBackgroundTask tskTask = (IBackgroundTask)sender;
				if (tskTask.IsActive)
					m_setActiveTasks.Add(tskTask);
				else
					m_setActiveTasks.Remove(tskTask);
			}
		}

		#region Activity Removal

		/// <summary>
		/// Removes a task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveActivity(IBackgroundTask p_tskTask)
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
			return !p_tskTask.IsActive && !CanResume(p_tskTask);
		}

		#endregion

		#region Activity Pause/Resume

		/// <summary>
		/// Resumes a task.
		/// </summary>
		/// <param name="p_tskTask">The task to resume.</param>
		public void ResumeActivity(IBackgroundTask p_tskTask)
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
			return (p_tskTask.Status == TaskStatus.Paused) || (p_tskTask.Status == TaskStatus.Incomplete);
		}

		/// <summary>
		/// Pauses a task.
		/// </summary>
		/// <param name="p_tskTask">The task to pause.</param>
		public void PauseActivity(IBackgroundTask p_tskTask)
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
			return p_tskTask.SupportsPause && (p_tskTask.Status == TaskStatus.Running);
		}

		#endregion
	}
}
