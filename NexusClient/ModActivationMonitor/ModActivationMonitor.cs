using System;
using System.Collections.Generic;
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
		private readonly ThreadSafeObservableList<IBackgroundTaskSet> m_oclTasks = new ThreadSafeObservableList<IBackgroundTaskSet>();
		private readonly object m_objSubmissionLock = new object();
		private readonly List<IBackgroundTaskSet> m_lstPublishingTasks = new List<IBackgroundTaskSet>();
		private readonly List<IBackgroundTaskSet> m_lstQueuedTasks = new List<IBackgroundTaskSet>();
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
				return ((m_btsRunningTask != null) && (!m_btsRunningTask.IsCompleted) && (!m_btsRunningTask.IsQueued));
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
		/// Adds a native mod operation to the monitor and submits it for serialized execution.
		/// </summary>
		/// <param name="p_bstTask">The task to monitor and execute.</param>
		public void AddActivity(IBackgroundTaskSet p_bstTask)
		{
			Submit(p_bstTask);
		}

		/// <summary>
		/// Submits a native mod operation for exactly one in-process start through the shared activation queue.
		/// </summary>
		/// <param name="p_bstTask">The task set to submit.</param>
		/// <returns><c>true</c> when the task was accepted; <c>false</c> when the same task or a legacy filename duplicate is already pending.</returns>
		public bool Submit(IBackgroundTaskSet p_bstTask)
		{
			if (p_bstTask == null)
				throw new ArgumentNullException(nameof(p_bstTask));
			if (!IsSupportedOperationTask(p_bstTask))
				throw new ArgumentException("Only native mod install, upgrade and uninstall task sets can be submitted.", nameof(p_bstTask));
			ModInstallerBase nativeOperation = (ModInstallerBase)p_bstTask;
			if (nativeOperation.OperationIdentity == null)
				throw new InvalidOperationException("Native mod operations must have an operation identity before submission.");
			if (p_bstTask.IsCompleted)
				return false;

			lock (m_objSubmissionLock)
			{
				if (ContainsTaskReference(p_bstTask) || ContainsPublishingTaskReference(p_bstTask) || ShouldRejectLegacyDuplicate(p_bstTask))
					return false;

				p_bstTask.IsQueued = true;
				p_bstTask.TaskSetCompleted += SubmittedTask_TaskSetCompleted;
				m_lstPublishingTasks.Add(p_bstTask);
			}

			try
			{
				// CollectionChanged is synchronous. Publish outside the submission lock so a UI observer can marshal
				// safely, but publish before scheduling so observers subscribe before a fast task can complete.
				m_oclTasks.Add(p_bstTask);
			}
			catch
			{
				lock (m_objSubmissionLock)
				{
					m_lstPublishingTasks.Remove(p_bstTask);
					p_bstTask.IsQueued = false;
					p_bstTask.TaskSetCompleted -= SubmittedTask_TaskSetCompleted;
				}
				throw;
			}

			bool startNow = false;
			lock (m_objSubmissionLock)
			{
				m_lstPublishingTasks.Remove(p_bstTask);
				if (p_bstTask.IsCompleted)
				{
					p_bstTask.IsQueued = false;
					return true;
				}

				if ((m_btsRunningTask == null) || m_btsRunningTask.IsCompleted)
				{
					m_btsRunningTask = p_bstTask;
					p_bstTask.IsQueued = false;
					startNow = true;
				}
				else
				{
					m_lstQueuedTasks.Add(p_bstTask);
				}
			}

			if (startNow)
				StartTask(p_bstTask);
			return true;
		}

		/// <summary>
		/// Submits one native operation only when the serialized native lane is idle, invoking a durable acceptance callback before worker start.
		/// </summary>
		/// <remarks>
		/// This is the C6 Collection submission boundary. The accepted task reserves the running slot before publication so ordinary
		/// submissions cannot start ahead of it. The callback runs after observers can see the task but before <see cref="StartTask"/>;
		/// if the callback fails the task is removed without starting and the ordinary queue resumes.
		/// </remarks>
		/// <param name="p_bstTask">The fully constructed native operation.</param>
		/// <param name="p_actAcceptedBeforeStart">Durable callback that must complete before native worker start.</param>
		/// <returns><c>true</c> when the idle lane accepted and started the task; <c>false</c> when any native operation is already pending.</returns>
		public bool SubmitWhenIdle(IBackgroundTaskSet p_bstTask, Action p_actAcceptedBeforeStart)
		{
			if (p_bstTask == null)
				throw new ArgumentNullException(nameof(p_bstTask));
			if (p_actAcceptedBeforeStart == null)
				throw new ArgumentNullException(nameof(p_actAcceptedBeforeStart));
			if (!IsSupportedOperationTask(p_bstTask))
				throw new ArgumentException("Only native mod install, upgrade and uninstall task sets can be submitted.", nameof(p_bstTask));
			ModInstallerBase nativeOperation = (ModInstallerBase)p_bstTask;
			if (nativeOperation.OperationIdentity == null)
				throw new InvalidOperationException("Native mod operations must have an operation identity before submission.");
			if (p_bstTask.IsCompleted)
				return false;

			lock (m_objSubmissionLock)
			{
				if ((m_btsRunningTask != null && !m_btsRunningTask.IsCompleted) || m_lstQueuedTasks.Count != 0 ||
					m_lstPublishingTasks.Count != 0 || ContainsTaskReference(p_bstTask) || ShouldRejectLegacyDuplicate(p_bstTask))
					return false;

				p_bstTask.IsQueued = true;
				p_bstTask.TaskSetCompleted += SubmittedTask_TaskSetCompleted;
				m_lstPublishingTasks.Add(p_bstTask);
				m_btsRunningTask = p_bstTask;
			}

			try
			{
				m_oclTasks.Add(p_bstTask);
				p_actAcceptedBeforeStart();

				lock (m_objSubmissionLock)
				{
					m_lstPublishingTasks.Remove(p_bstTask);
					if (p_bstTask.IsCompleted)
					{
						p_bstTask.IsQueued = false;
						return true;
					}
					p_bstTask.IsQueued = false;
				}

				StartTask(p_bstTask);
				return true;
			}
			catch
			{
				// Keep the running-slot reservation while unpublishing outside the submission lock; CollectionChanged
				// may synchronously marshal to the UI and must not wait on a lock held by this thread.
				lock (m_objSubmissionLock)
				{
					m_lstPublishingTasks.Remove(p_bstTask);
					p_bstTask.IsQueued = false;
					p_bstTask.TaskSetCompleted -= SubmittedTask_TaskSetCompleted;
				}
				m_oclTasks.Remove(p_bstTask);

				IBackgroundTaskSet nextTask = null;
				lock (m_objSubmissionLock)
				{
					if (ReferenceEquals(m_btsRunningTask, p_bstTask))
						m_btsRunningTask = null;
					nextTask = DequeueNextTaskCore();
				}
				if (nextTask != null)
					StartTask(nextTask);
				throw;
			}
		}

		private bool ContainsTaskReference(IBackgroundTaskSet p_bstTask)
		{
			foreach (IBackgroundTaskSet task in m_oclTasks)
				if (ReferenceEquals(task, p_bstTask))
					return true;
			return false;
		}

		private bool ContainsPublishingTaskReference(IBackgroundTaskSet p_bstTask)
		{
			foreach (IBackgroundTaskSet task in m_lstPublishingTasks)
				if (ReferenceEquals(task, p_bstTask))
					return true;
			return false;
		}

		private bool ShouldRejectLegacyDuplicate(IBackgroundTaskSet p_bstTask)
		{
			string taskFileName = GetTaskModFileName(p_bstTask);
			if (String.IsNullOrEmpty(taskFileName))
				return false;

			if ((m_btsRunningTask != null) && !m_btsRunningTask.IsCompleted &&
				String.Equals(GetTaskModFileName(m_btsRunningTask), taskFileName, StringComparison.OrdinalIgnoreCase))
				return true;

			foreach (IBackgroundTaskSet publishingTask in m_lstPublishingTasks)
				if (String.Equals(GetTaskModFileName(publishingTask), taskFileName, StringComparison.OrdinalIgnoreCase))
					return true;

			foreach (IBackgroundTaskSet queuedTask in m_lstQueuedTasks)
				if (queuedTask.IsQueued && String.Equals(GetTaskModFileName(queuedTask), taskFileName, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		private static string GetTaskModFileName(IBackgroundTaskSet p_bstTask)
		{
			if (p_bstTask is ModInstaller installer)
				return installer.ModFileName;
			if (p_bstTask is ModUninstaller uninstaller)
				return uninstaller.ModFileName;
			return null;
		}

		private static bool IsSupportedOperationTask(IBackgroundTaskSet p_bstTask)
		{
			return (p_bstTask is ModInstaller) || (p_bstTask is ModUninstaller);
		}

		private static void StartTask(IBackgroundTaskSet p_bstTask)
		{
			if (p_bstTask is ModUninstaller uninstaller)
				uninstaller.Install();
			else if (p_bstTask is ModInstaller installer)
				installer.Install();
		}

		private void SubmittedTask_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			IBackgroundTaskSet completedTask = sender as IBackgroundTaskSet;
			IBackgroundTaskSet nextTask = null;
			lock (m_objSubmissionLock)
			{
				if (completedTask != null)
				{
					completedTask.IsQueued = false;
					completedTask.TaskSetCompleted -= SubmittedTask_TaskSetCompleted;
				}

				if (!ReferenceEquals(m_btsRunningTask, completedTask))
					return;

				m_btsRunningTask = null;
				nextTask = DequeueNextTaskCore();
			}

			if (nextTask != null)
				StartTask(nextTask);
		}

		/// <summary>Returns the next valid queued task while the submission lock is held.</summary>
		private IBackgroundTaskSet DequeueNextTaskCore()
		{
			while (m_lstQueuedTasks.Count > 0)
			{
				IBackgroundTaskSet candidate = m_lstQueuedTasks[0];
				m_lstQueuedTasks.RemoveAt(0);
				if (!ContainsTaskReference(candidate) || candidate.IsCompleted)
				{
					candidate.IsQueued = false;
					continue;
				}

				candidate.IsQueued = false;
				m_btsRunningTask = candidate;
				return candidate;
			}
			return null;
		}

		private void RemoveQueuedTaskCore(IBackgroundTaskSet p_bstTask)
		{
			if (p_bstTask == null)
				return;

			lock (m_objSubmissionLock)
			{
				int queuedIndex = -1;
				for (int i = 0; i < m_lstQueuedTasks.Count; i++)
				{
					if (ReferenceEquals(m_lstQueuedTasks[i], p_bstTask))
					{
						queuedIndex = i;
						break;
					}
				}

				if (queuedIndex < 0)
					return;

				m_lstQueuedTasks.RemoveAt(queuedIndex);
				p_bstTask.IsQueued = false;
				p_bstTask.TaskSetCompleted -= SubmittedTask_TaskSetCompleted;
				m_oclTasks.Remove(p_bstTask);
			}
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
			RemoveQueuedTaskCore(p_tskTask);
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
			RemoveQueuedTaskCore(p_tskTask);
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
			RemoveQueuedTaskCore(p_tskTask);
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
