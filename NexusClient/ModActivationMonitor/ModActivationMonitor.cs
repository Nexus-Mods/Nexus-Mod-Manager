using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.ModManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;


namespace Nexus.Client.ModActivationMonitoring
{
	/// <summary>
	/// This monitors the status of activities.
	/// </summary>
	public class ModActivationMonitor : INotifyPropertyChanged
	{
		private ThreadSafeObservableList<IBackgroundTaskSet> m_oclTasks = new ThreadSafeObservableList<IBackgroundTaskSet>();
		//private IBackgroundTaskSet m_bstRunningTask = null;
		private string m_Status = null;
		private IBackgroundTaskSet m_btsRunningTask = null;
		
		/// <summary>
		/// Raised whenever a property of the class changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;


		#region Properties

		/// <summary>
		/// Gets the list of tasks being monitored.
		/// </summary>
		/// <value>The list of tasks being monitored.</value>
		public ReadOnlyObservableList<IBackgroundTaskSet> Tasks { get; private set; }

		/// <summary>
		/// Gets the list of tasks being executed.
		/// </summary>
		/// <value>The list of tasks being executed.</value>
		public ReadOnlyObservableList<IBackgroundTaskSet> ActiveTasks { get; private set; }

		public IBackgroundTaskSet RunningTask
		{
			get
			{
				return m_btsRunningTask;
			}
			set
			{
				m_btsRunningTask = value;
			}
		}

		public string Status
		{
			get
			{
				return m_Status;
			}
			set
			{
				bool booChanged = false;
				if (m_Status != value)
				{
					booChanged = true;
					m_Status = value;
				}
				if (booChanged)
					OnPropertyChanged("Status");
			}
		}

		public bool IsInstalling
		{
			get
			{
				return (m_btsRunningTask != null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModActivationMonitor()
		{
			Tasks = new ReadOnlyObservableList<IBackgroundTaskSet>(m_oclTasks);
			//m_oclTasks.CollectionChanged += new NotifyCollectionChangedEventHandler(oclTasks_CollectionChanged);
		}

		#endregion

		/// <summary>
		/// Adds a task to the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task to monitor.</param>
		public void AddActivity(IBackgroundTaskSet p_bstTask)
		{
			m_oclTasks.Add(p_bstTask);
		}
				

		#region Mods Removal

		#region Remove

		/// <summary>
		/// Removes a task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveTask(ModInstaller p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		/// <summary>
		/// Removes an uninstalling task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveTaskUn(ModUninstaller p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		/// <summary>
		/// Removes an upgrading task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveTaskUpg(ModUpgrader p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		#endregion

		#region RemoveQueued

		/// <summary>
		/// Removes a task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveQueuedTask(ModInstaller p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		/// <summary>
		/// Removes an uninstalling task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveQueuedTaskUn(ModUninstaller p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		/// <summary>
		/// Removes an upgrading task from the monitor.
		/// </summary>
		/// <remarks>
		/// Tasks can only be removed if they are not running.
		/// </remarks>
		/// <param name="p_tskTask">The task to remove.</param>
		public void RemoveQueuedTaskUpg(ModUpgrader p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		#endregion

		#region RemoveUseless

		/// <summary>
		/// Removes a useless task (the task is already in queue or running).
		/// </summary>
		public void RemoveUselessTask(ModInstaller p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		/// <summary>
		/// Removes a useless uninstalling task (the task is already in queue or running).
		/// </summary>
		public void RemoveUselessTaskUn(ModUninstaller p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		/// <summary>
		/// Removes a useless upgrading task (the task is already in queue or running).
		/// </summary>
		public void RemoveUselessTaskUpg(ModUpgrader p_tskTask)
		{
			m_oclTasks.Remove(p_tskTask);
		}

		#endregion

		#region CanRemove

		/// <summary>
		/// Determines if the given <see cref="BasicInstallTask"/> can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemove(ModInstaller p_tskTask)
		{
			return p_tskTask.IsCompleted;
		}

		/// <summary>
		/// Determines if the given <see cref="BasicInstallTask"/> can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveUn(ModUninstaller p_tskTask)
		{
			return p_tskTask.IsCompleted;
		}

		/// <summary>
		/// Determines if the given <see cref="BasicInstallTask"/> can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveUpg(ModUpgrader p_tskTask)
		{
			return p_tskTask.IsCompleted;
		}

		#endregion

		#region CanRemoveQueued

		/// <summary>
		/// Determines if the given <see cref="BasicInstallTask"/> queued can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveQueued(ModInstaller p_tskTask)
		{
			return p_tskTask.IsQueued;
		}

		/// <summary>
		/// Determines if the given uninstalling <see cref="BasicInstallTask"/> queued can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveQueuedUn(ModUninstaller p_tskTask)
		{
			return p_tskTask.IsQueued;
		}

		/// <summary>
		/// Determines if the given upgrading <see cref="BasicInstallTask"/> queued can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveQueuedUpg(ModUpgrader p_tskTask)
		{
			return p_tskTask.IsQueued;
		}

		#endregion

		#region CanRemoveselected

		/// <summary>
		/// Determines if the given <see cref="BasicInstallTask"/> selected can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveSelected(ModInstaller p_tskTask)
		{
			if (p_tskTask.IsQueued || p_tskTask.IsCompleted)
				return true;
			else
				return false;
		}
		
		/// <summary>
		/// Determines if the given uninstalling <see cref="BasicInstallTask"/> selected can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveSelectedUn(ModUninstaller p_tskTask)
		{
			if (p_tskTask.IsQueued || p_tskTask.IsCompleted)
				return true;
			else
				return false;
		}

		/// <summary>
		/// Determines if the given upgrading <see cref="BasicInstallTask"/> selected can be removed from
		/// the monitor.
		/// </summary>
		/// <param name="p_tskTask">The task for which it is to be determined
		/// if it can be removed from the monitor.</param>
		/// <returns><c>true</c> if the p_tskTask can be removed;
		/// <c>false</c> otherwise.</returns>
		public bool CanRemoveSelectedUpg(ModUpgrader p_tskTask)
		{
			if (p_tskTask.IsQueued || p_tskTask.IsCompleted)
				return true;
			else
				return false;
		}

		#endregion


		#endregion


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

		
	}
}
