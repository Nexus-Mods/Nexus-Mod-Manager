using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Settings;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ActivityMonitoring.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display activity monitoring.
	/// </summary>
	public class ActivityMonitorVM
	{
		#region Properties

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
		/// Gets the activity manager to use to manage the monitored activities.
		/// </summary>
		/// <value>The activity manager to use to manage the monitored activities.</value>
		protected ActivityMonitor ActivityMonitor { get; private set; }

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<IBackgroundTask> Tasks
		{
			get
			{
				return ActivityMonitor.Tasks;
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
				return ActivityMonitor.ActiveTasks;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_amnActivityMonitor">The activity manager to use to manage the monitored activities.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		public ActivityMonitorVM(ActivityMonitor p_amnActivityMonitor, ISettings p_setSettings)
		{
			ActivityMonitor = p_amnActivityMonitor;
			Settings = p_setSettings;

			CancelTaskCommand = new Command<IBackgroundTask>("Cancel", "Cancels the selected activity.", CancelTask);
			RemoveTaskCommand = new Command<IBackgroundTask>("Remove", "Removes the selected activity.", RemoveTask);
			PauseTaskCommand = new Command<IBackgroundTask>("Pause", "Pauses the selected activity.", PauseTask);
			ResumeTaskCommand = new Command<IBackgroundTask>("Resume", "Resumes the selected activity.", ResumeTask);
		}

		#endregion

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
			return (p_tskTask.Status == TaskStatus.Running) || (p_tskTask.Status == TaskStatus.Paused) || (p_tskTask.Status == TaskStatus.Incomplete);
		}

		#endregion

		#region Remove Command

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveTask(IBackgroundTask p_tskTask)
		{
			if (ActivityMonitor.CanRemove(p_tskTask))
				ActivityMonitor.RemoveActivity(p_tskTask);
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be removed.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed.</param>
		/// <returns><c>true</c> if the task can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveActivity(IBackgroundTask p_tskTask)
		{
			return ActivityMonitor.CanRemove(p_tskTask);
		}

		#endregion

		#region Pause Command

		/// <summary>
		/// Puases the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to pause.</param>
		public void PauseTask(IBackgroundTask p_tskTask)
		{
			if (ActivityMonitor.CanPause(p_tskTask))
				ActivityMonitor.PauseActivity(p_tskTask);
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be paused.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be paused.</param>
		/// <returns><c>true</c> if the task can be paused;
		/// <c>false</c> otherwise.</returns>
		public bool CanPauseActivity(IBackgroundTask p_tskTask)
		{
			return ActivityMonitor.CanPause(p_tskTask);
		}

		#endregion

		#region Resume Command

		/// <summary>
		/// Resumes the given task.
		/// </summary>
		/// <param name="p_tskTask">The task to resume.</param>
		public void ResumeTask(IBackgroundTask p_tskTask)
		{
			if (ActivityMonitor.CanResume(p_tskTask))
				ActivityMonitor.ResumeActivity(p_tskTask);
		}

		/// <summary>
		/// Determines if the given <see cref="IBackgroundTask"/> can be resumed.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be resumed.</param>
		/// <returns><c>true</c> if the task can be resumed;
		/// <c>false</c> otherwise.</returns>
		public bool CanResumeActivity(IBackgroundTask p_tskTask)
		{
			return ActivityMonitor.CanResume(p_tskTask);
		}

		#endregion
	}
}
