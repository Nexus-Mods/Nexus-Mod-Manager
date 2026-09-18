using System;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Mods;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// The base class for all mod installers.
	/// </summary>
	public abstract class ModInstallerBase : IBackgroundTaskSet
	{
		private const string LegacyUnidentifiedMutationTarget = "nmm-process-legacy-native-mutation";

		/// <summary>
		/// We only want on installer running at a time, so as not to mess up
		/// the file system, of settings files. As such, all installers lock
		/// on this lock object.
		/// </summary>
		/// 
		protected static readonly object objInstallLock = new object();

		/// <summary>
		/// We only want on uninstaller running at a time, so as not to mess up
		/// the file system, of settings files. As such, all uninstaller lock
		/// on this lock object.
		/// </summary>
		/// 
		protected static readonly object objUninstallLock = new object();

		#region Events

		/// <summary>
		/// Raised when a task in the set has started.
		/// </summary>
		/// <remarks>
		/// The argument passed with the event args is the task that
		/// has been started.
		/// </remarks>
		public event EventHandler<EventArgs<IBackgroundTask>> TaskStarted = delegate { };

		/// <summary>
		/// Raised when a task set has completed.
		/// </summary>
		public event EventHandler<TaskSetCompletedEventArgs> TaskSetCompleted = delegate { };

		#endregion

		private EventWaitHandle m_ewhSetCompleted = new EventWaitHandle(false, EventResetMode.ManualReset);
		private ModOperationResult m_morOperationResult;
		private CollectionTargetMutationLease m_ctlParentMutationLease;

		#region Properties

		/// <summary>
		/// Gets the current error message.
		/// </summary>
		/// <value>The current error message.</value>
		public string PopupErrorMessage { get; protected set; }

		/// <summary>
		/// Gets the current error message type.
		/// </summary>
		/// <value>The current error message type.</value>
		public string PopupErrorMessageType { get; protected set; }

		/// <summary>
		/// Gets the current error message details.
		/// </summary>
		/// <value>The current error message details.</value>
		public string DetailsErrorMessage { get; protected set; }

		/// <summary>
		/// Gets whether the task set has completed.
		/// </summary>
		/// <value>Whether the task set has completed.</value>
		public bool IsCompleted { get; private set; }

		/// <summary>
		/// Gets whether the task set is queued.
		/// </summary>
		/// <value>Whether the task set is queued.</value>
		public bool IsQueued { get;  set; }

		/// <summary>
		/// Gets the immutable operation identity assigned before this native task is submitted.
		/// </summary>
		public ModOperationIdentity OperationIdentity { get; private set; }

		/// <summary>
		/// Gets the immutable terminal operation result when an identified native operation has completed.
		/// </summary>
		/// <remarks>
		/// Reported task status and verified durability are intentionally independent. A failed task may still have
		/// committed native state, while a nominally successful task may remain durability-unknown until reconciliation.
		/// </remarks>
		public ModOperationResult OperationResult => m_morOperationResult;

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModInstallerBase()
		{
		}

		/// <summary>
		/// Assigns the operation identity exactly once before submission.
		/// </summary>
		/// <param name="p_moiIdentity">The identity to attach to this native task.</param>
		protected internal void AssignOperationIdentity(ModOperationIdentity p_moiIdentity)
		{
			if (p_moiIdentity == null)
				throw new ArgumentNullException(nameof(p_moiIdentity));
			if (OperationIdentity != null)
				throw new InvalidOperationException("The native operation identity has already been assigned.");

			OperationIdentity = p_moiIdentity;
		}

		/// <summary>
		/// Assigns the parent Collection mutation reservation that this native child must inherit when it starts.
		/// </summary>
		/// <remarks>
		/// The parent handle remains owned by the caller. The native operation creates and disposes its own inherited handle.
		/// </remarks>
		protected internal void AssignParentMutationLease(CollectionTargetMutationLease p_ctlParentLease)
		{
			if (p_ctlParentLease == null)
				throw new ArgumentNullException(nameof(p_ctlParentLease));
			if (p_ctlParentLease.IsDisposed)
				throw new ObjectDisposedException(nameof(p_ctlParentLease));
			if (m_ctlParentMutationLease != null)
				throw new InvalidOperationException("The native operation already has a parent mutation lease.");

			m_ctlParentMutationLease = p_ctlParentLease;
		}

		/// <summary>
		/// Acquires the common in-process mutation reservation for this native operation.
		/// </summary>
		/// <remarks>
		/// Production tasks submitted through the C3 seam use their immutable target fingerprint. Legacy direct native calls
		/// without an identity still join the same process gate through a conservative fallback token.
		/// </remarks>
		protected CollectionTargetMutationLease AcquireMutationLease()
		{
			ModOperationIdentity identity = OperationIdentity;
			if (identity == null)
			{
				if (m_ctlParentMutationLease != null)
					throw new InvalidOperationException("A parent mutation lease cannot be inherited by an unidentified native operation.");

				return CollectionTargetMutationLeaseManager.Shared.AcquireNativeOperation(LegacyUnidentifiedMutationTarget);
			}

			if (m_ctlParentMutationLease != null)
			{
				return m_ctlParentMutationLease.Manager.InheritNativeOperation(
					m_ctlParentMutationLease, identity.Fingerprint.TargetFingerprint);
			}

			return CollectionTargetMutationLeaseManager.Shared.AcquireNativeOperation(identity.Fingerprint.TargetFingerprint);
		}

		#endregion

		#region Event Raising

		#region Task Started

		/// <summary>
		/// Raises the <see cref="TaskStarted"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the task that was started.</param>
		private void RaiseTaskStarted(EventArgs<IBackgroundTask> e)
		{
			TaskStarted(this, e);
		}

		/// <summary>
		/// The callback called by the begin invoke method used to call the event asynchronously upon completion
		/// of the event.
		/// </summary>
		/// <param name="p_asrResult">The asynchronous result for the call.</param>
		private void EndTaskStartedCallback(IAsyncResult p_asrResult)
		{
			Action<EventArgs<IBackgroundTask>> dlcEvent = (Action<EventArgs<IBackgroundTask>>)((AsyncResult)p_asrResult).AsyncDelegate;
			dlcEvent.EndInvoke(p_asrResult);
			p_asrResult.AsyncWaitHandle.Close();
		}

		/// <summary>
		/// Raises the <see cref="TaskStarted"/> event.
		/// </summary>
		/// <remarks>
		/// The event is raised asynchronously, so the installer can continue its work uninterrupted.
		/// This is to prevent deadlocks, primarily on the UI thread.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the task that was started.</param>
		protected virtual void OnTaskStarted(EventArgs<IBackgroundTask> e)
		{
			((Action<EventArgs<IBackgroundTask>>)RaiseTaskStarted).BeginInvoke(e, EndTaskStartedCallback, null);
		}

		/// <summary>
		/// Raises the <see cref="TaskStarted"/> event.
		/// </summary>
		/// <param name="p_bgtTask">The task that was started.</param>
		protected void OnTaskStarted(IBackgroundTask p_bgtTask)
		{
			OnTaskStarted(new EventArgs<IBackgroundTask>(p_bgtTask));
		}

		#endregion

		#region Task Set Completed

		/// <summary>
		/// Raises the <see cref="TaskSetCompleted"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the task that was started.</param>
		private void RaiseTaskSetCompleted(TaskSetCompletedEventArgs e)
		{
			TaskSetCompleted(this, e);
		}

		/// <summary>
		/// The callback called by the begin invoke method used to call the event asynchronously upon completion
		/// of the event.
		/// </summary>
		/// <param name="p_asrResult">The asynchronous result for the call.</param>
		private void EndTaskSetCompletedCallback(IAsyncResult p_asrResult)
		{
			Action<TaskSetCompletedEventArgs> dlcEvent = (Action<TaskSetCompletedEventArgs>)((AsyncResult)p_asrResult).AsyncDelegate;
			dlcEvent.EndInvoke(p_asrResult);
			p_asrResult.AsyncWaitHandle.Close();
		}

		/// <summary>
		/// Raises the <see cref="TaskSetCompleted"/> event.
		/// </summary>
		/// <remarks>
		/// The event is raised asynchronously, so the installer can continue its work uninterrupted.
		/// This is to prevent deadlocks, primarily on the UI thread.
		/// </remarks>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the task that was started.</param>
		protected virtual void OnTaskSetCompleted(TaskSetCompletedEventArgs e)
		{
			if (OperationIdentity != null && OperationResult == null)
			{
				SetOperationResult(e.Success ? ModOperationReportedStatus.Succeeded : ModOperationReportedStatus.Failed,
					ModOperationDurability.Unknown, e.Message);
			}

			IsCompleted = true;
			m_ewhSetCompleted.Set();
			((Action<TaskSetCompletedEventArgs>)RaiseTaskSetCompleted).BeginInvoke(e, EndTaskSetCompletedCallback, null);
		}

		/// <summary>
		/// Records the explicit native-operation result before publishing ordinary task-set completion.
		/// </summary>
		/// <param name="p_mrsReportedStatus">The terminal status reported by the workflow.</param>
		/// <param name="p_modDurability">The independently verified native durability.</param>
		/// <param name="p_booSuccess">Whether the legacy task-set completion should report success.</param>
		/// <param name="p_strMessage">The task-set completion message.</param>
		/// <param name="p_modMod">The mod the operation acted upon.</param>
		protected void OnTaskSetCompleted(ModOperationReportedStatus p_mrsReportedStatus, ModOperationDurability p_modDurability,
			bool p_booSuccess, string p_strMessage, IMod p_modMod)
		{
			SetOperationResult(p_mrsReportedStatus, p_modDurability, p_strMessage);
			OnTaskSetCompleted(new TaskSetCompletedEventArgs(p_booSuccess, p_strMessage, p_modMod));
		}

		/// <summary>
		/// Raises the <see cref="TaskSetCompleted"/> event using the legacy result contract.
		/// </summary>
		/// <param name="p_booSuccess">Whether or not the task set completed successfully.</param>
		/// <param name="p_strMessage">The message of the completed task set.</param>
		/// <param name="p_modMod">The mod the installer acted upon.</param>
		protected void OnTaskSetCompleted(bool p_booSuccess, string p_strMessage, IMod p_modMod)
		{
			OnTaskSetCompleted(p_booSuccess ? ModOperationReportedStatus.Succeeded : ModOperationReportedStatus.Failed,
				ModOperationDurability.Unknown, p_booSuccess, p_strMessage, p_modMod);
		}

		/// <summary>
		/// Publishes an immutable operation result exactly once when this task has an assigned operation identity.
		/// </summary>
		private void SetOperationResult(ModOperationReportedStatus p_mrsReportedStatus, ModOperationDurability p_modDurability, string p_strMessage)
		{
			ModOperationIdentity identity = OperationIdentity;
			if (identity == null || OperationResult != null)
				return;

			var result = new ModOperationResult(identity, p_mrsReportedStatus, p_modDurability, p_strMessage);
			Interlocked.CompareExchange(ref m_morOperationResult, result, null);
		}

		#endregion

		#endregion

		/// <summary>
		/// Blocks until the task set is completed.
		/// </summary>
		public void Wait()
		{
			m_ewhSetCompleted.WaitOne();
		}
	}
}
