using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands.Generic;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.Settings;
using Nexus.Client.Util.Collections;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Nexus.Client.ModActivationMonitoring.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display Mod Activation monitoring.
	/// </summary>
	public class ModActivationMonitorVM : INotifyPropertyChanged
	{
		ModManager m_mmgModManager = null;

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
		public Command<BasicInstallTask> CancelTaskCommand { get; private set; }
		
		
		/// <summary>
		/// Gets the number of maximum allowed concurrent activations.
		/// </summary>
		/// <value>The number of maximum allowed concurrent activations.</value>
		public int MaxConcurrentActivation
		{
			get
			{
				return 1;
			}
		}

		#endregion

		/// <summary>
		/// Gets the Mod Activation manager to use to manage the monitored activities.
		/// </summary>
		/// <value>The Mod Activation manager to use to manage the monitored activities.</value>
		public ModActivationMonitor ModActivationMonitor { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<IBackgroundTaskSet> Tasks
		{
			get
			{
				return ModActivationMonitor.Tasks;
			}
		}

		/// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public ReadOnlyObservableList<IBackgroundTaskSet> ActiveTasks
		{
			get
			{
				return ModActivationMonitor.ActiveTasks;
			}
		}
		
		/// <summary>
		/// Gets the total task speed.
		/// </summary>
		/// <value>The total task speed.</value>
		public string Status
		{
			get
			{
				return ModActivationMonitor.Status;
			}
		}

		public IBackgroundTaskSet RunningTask
		{
			get
			{
				return ModActivationMonitor.RunningTask;
			}
			set
			{
				ModActivationMonitor.RunningTask = value;
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
		/// <param name="p_amnModActivationMonitor">The Activate Mods  manager to use to manage the monitored activities.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		public ModActivationMonitorVM(ModActivationMonitor p_amnModActivationMonitor, ISettings p_setSettings, ModManager p_mmgModManager)
		{
			ModActivationMonitor = p_amnModActivationMonitor;
			Settings = p_setSettings;
			m_mmgModManager = p_mmgModManager;
			ModActivationMonitor.PropertyChanged += new PropertyChangedEventHandler(ActiveTasks_PropertyChanged);
		}

		#endregion

		private void ActiveTasks_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (e.PropertyName == "Status")
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
		
		#region Remove Command

		#region Remove

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveTask(ModInstaller p_tskTask)
		{
			if (ModActivationMonitor.CanRemove(p_tskTask))
				ModActivationMonitor.RemoveTask(p_tskTask);
		}

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveTaskUn(ModUninstaller p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveUn(p_tskTask))
				ModActivationMonitor.RemoveTaskUn(p_tskTask);
		}

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveTaskUpg(ModUpgrader p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveUpg(p_tskTask))
				ModActivationMonitor.RemoveTaskUpg(p_tskTask);
		}

		#endregion

		#region RemoveQueued

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveQueuedTask(ModInstaller p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveQueued(p_tskTask))
				ModActivationMonitor.RemoveQueuedTask(p_tskTask);
		}

		/// <summary>
		/// Removes the uninstalling given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveQueuedTaskUn(ModUninstaller p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveQueuedUn(p_tskTask))
				ModActivationMonitor.RemoveQueuedTaskUn(p_tskTask);
		}

		/// <summary>
		/// Removes the upgrading given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveQueuedTaskUpg(ModUpgrader p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveQueuedUpg(p_tskTask))
				ModActivationMonitor.RemoveQueuedTaskUpg(p_tskTask);
		}

		#endregion

		#region RemoveSelected

		/// <summary>
		/// Removes the given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveSelectedTask(ModInstaller p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveSelected(p_tskTask))
			{
				if (p_tskTask.IsCompleted)
					ModActivationMonitor.RemoveTask(p_tskTask);
				else if (p_tskTask.IsQueued)
					ModActivationMonitor.RemoveQueuedTask(p_tskTask);
			}
		}

		/// <summary>
		/// Removes the uninstalling given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveSelectedTaskUn(ModUninstaller p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveSelectedUn(p_tskTask))
			{
				if (p_tskTask.IsCompleted)
					ModActivationMonitor.RemoveTaskUn(p_tskTask);
				else if (p_tskTask.IsQueued)
					ModActivationMonitor.RemoveQueuedTaskUn(p_tskTask);
			}
		}

		/// <summary>
		/// Removes the upgrading given task.
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveSelectedTaskUpg(ModUpgrader p_tskTask)
		{
			if (ModActivationMonitor.CanRemoveSelectedUpg(p_tskTask))
			{
				if (p_tskTask.IsCompleted)
					ModActivationMonitor.RemoveTaskUpg(p_tskTask);
				else if (p_tskTask.IsQueued)
					ModActivationMonitor.RemoveQueuedTaskUpg(p_tskTask);
			}
		}

		#endregion

		#region RemoveUseless

		/// <summary>
		/// Removes the given task (the task is already in queue or running).
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveUselessTask(ModInstaller p_tskTask)
		{
			ModActivationMonitor.RemoveUselessTask(p_tskTask);
		}

		/// <summary>
		/// Removes the given uninstalling task (the task is already in queue or running).
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveUselessTaskUn(ModUninstaller p_tskTask)
		{
			ModActivationMonitor.RemoveUselessTaskUn(p_tskTask);
		}

		/// <summary>
		/// Removes the given upgrading task (the task is already in queue or running).
		/// </summary>
		/// <param name="p_tskTask">BasicInstallTask task to remove.</param>
		public void RemoveUselessTaskUpg(ModUpgrader p_tskTask)
		{
			ModActivationMonitor.RemoveUselessTaskUpg(p_tskTask);
		}

		#endregion

		/// <summary>
		/// Removes all the completed/failed tasks.
		/// </summary>
		public void RemoveAllTasks(List<IBackgroundTaskSet> p_tskRunning)
		{
			if (p_tskRunning.Count > 0)
				foreach (IBackgroundTaskSet btRemovable in p_tskRunning)
				{
					if(btRemovable.GetType() == typeof(ModInstaller))
						RemoveTask((ModInstaller)btRemovable);
					else if(btRemovable.GetType() == typeof(ModUninstaller))
						RemoveTaskUn((ModUninstaller)btRemovable);
					else if(btRemovable.GetType() == typeof(ModUpgrader))
						RemoveTaskUpg((ModUpgrader)btRemovable);
				}		
		}

		/// <summary>
		/// Removes the selected task.
		/// </summary>
		public void RemoveSelectedTask(string p_strTask)
		{
			List<IBackgroundTaskSet> lstTasks = new List<IBackgroundTaskSet>();
			lock (Tasks)
			{
				foreach (IBackgroundTaskSet btTask in Tasks)
					lstTasks.Add(btTask);
			}

			if (lstTasks.Count > 0)
			{
				foreach (IBackgroundTaskSet btRemovable in lstTasks)
				{
					if (btRemovable.GetType() == typeof(ModInstaller))
					{
						if (((ModInstaller)btRemovable).ModName == p_strTask)
							RemoveSelectedTask((ModInstaller)btRemovable);
					}
					else if (btRemovable.GetType() == typeof(ModUninstaller))
					{
						if (((ModUninstaller)btRemovable).ModName == p_strTask)
							RemoveSelectedTaskUn((ModUninstaller)btRemovable);
					}
					else if (btRemovable.GetType() == typeof(ModUpgrader))
					{
						if (((ModUpgrader)btRemovable).ModName == p_strTask)
							RemoveSelectedTaskUpg((ModUpgrader)btRemovable);
					}
				}
			}
		}

		/// <summary>
		/// Check the task status.
		/// </summary>
		public bool CheckTaskStatus(string p_strTask)
		{
			bool booStatus = false;
			List<IBackgroundTaskSet> lstTasks = new List<IBackgroundTaskSet>();
			lock (Tasks)
			{
				foreach (IBackgroundTaskSet btTask in Tasks)
					lstTasks.Add(btTask);
			}

			if (lstTasks.Count > 0)
			{
				foreach (IBackgroundTaskSet btRemovable in lstTasks)
				{
					if (btRemovable.GetType() == typeof(ModInstaller))
					{
						if (((ModInstaller)btRemovable).ModName == p_strTask)
							if ((((ModInstaller)btRemovable).IsQueued) || (((ModInstaller)btRemovable).IsCompleted))
								booStatus = true;
					}
					else if (btRemovable.GetType() == typeof(ModUninstaller))
					{
						if (((ModUninstaller)btRemovable).ModName == p_strTask)
							if ((((ModUninstaller)btRemovable).IsQueued) || (((ModUninstaller)btRemovable).IsCompleted))
								booStatus = true;
					}
					else if (btRemovable.GetType() == typeof(ModUpgrader))
					{
						if (((ModUpgrader)btRemovable).ModName == p_strTask)
							if ((((ModUpgrader)btRemovable).IsQueued) || (((ModUpgrader)btRemovable).IsCompleted))
								booStatus = true;
					}
				}
			}
			return booStatus;
		}
		

		/// <summary>
		/// Removes all the queued tasks.
		/// </summary>
		public void RemoveQueuedTasks()
		{
			List<IBackgroundTaskSet> lstTasks = new List<IBackgroundTaskSet>();
			lock (Tasks)
			{
				foreach (IBackgroundTaskSet btTask in Tasks)
					lstTasks.Add(btTask);
			}

			if (lstTasks.Count > 0)
				foreach (IBackgroundTaskSet btRemovable in lstTasks)
				{
					if(btRemovable.GetType() == typeof(ModInstaller))
						RemoveQueuedTask((ModInstaller)btRemovable);
					else if (btRemovable.GetType() == typeof(ModUninstaller))
						RemoveQueuedTaskUn((ModUninstaller)btRemovable);
					else if (btRemovable.GetType() == typeof(ModUpgrader))
						RemoveQueuedTaskUpg((ModUpgrader)btRemovable);
				}
		
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
		
		
	}
}
