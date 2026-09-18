namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the user-visible collection workflow represented by an operation journal entry.
	/// </summary>
	public enum CollectionOperationKind
	{
		Unknown = 0,
		ApplyResolvedPlan = 1,
		RestoreLocalCapture = 2,
		UpdateRevision = 3,
		VerifyRepair = 4,
		UninstallCollectionEffects = 5,
		CaptureLocalCollection = 6,
		DetachTracking = 7
	}

	/// <summary>
	/// Identifies the current durable lifecycle phase of a collection operation.
	/// </summary>
	public enum CollectionOperationPhase
	{
		Unknown = 0,
		Created = 1,
		Resolving = 2,
		Preparing = 3,
		AwaitingInput = 4,
		Revalidating = 5,
		ReadyForReview = 6,
		CapturingOptionalLocalBackup = 7,
		ReadyToApply = 8,
		ApplyingNativeChildren = 9,
		PausedAtSafeBoundary = 10,
		Verifying = 11,
		Recovering = 12,
		RecoveryRequired = 13,
		Completed = 14
	}

	/// <summary>
	/// Identifies the collection-level disposition independently from each native child's reported status/durability.
	/// </summary>
	public enum CollectionOperationResultState
	{
		/// <summary>
		/// The operation is still active and has no terminal/recovery-required result.
		/// </summary>
		Pending = 0,

		/// <summary>
		/// The selected collection result was fully verified and committed.
		/// </summary>
		Committed = 1,

		/// <summary>
		/// The operation was cancelled before any native child crossed the mutation boundary.
		/// </summary>
		CancelledBeforeApply = 2,

		/// <summary>
		/// Preparation failed before any native child crossed the mutation boundary.
		/// </summary>
		FailedBeforeApply = 3,

		/// <summary>
		/// Work stopped after the operation had already made durable/possibly durable progress.
		/// </summary>
		StoppedPartial = 4,

		/// <summary>
		/// Required compensation was completed and the operation verified the protected rollback result.
		/// </summary>
		RolledBack = 5,

		/// <summary>
		/// Durable reality cannot yet be safely finalized and explicit recovery/reconciliation is required.
		/// </summary>
		RecoveryRequired = 6
	}

	/// <summary>
	/// Identifies the native mod action intended for one collection child.
	/// </summary>
	public enum CollectionNativeChildAction
	{
		Unknown = 0,
		ActivateOrReinstall = 1,
		Deactivate = 2
	}

	/// <summary>
	/// Identifies how far a persisted native child has advanced through the collection safety boundary.
	/// </summary>
	public enum CollectionNativeChildCheckpoint
	{
		Unknown = 0,

		/// <summary>
		/// The child intent and immutable correlation have been durably recorded.
		/// </summary>
		IntentPersisted = 1,

		/// <summary>
		/// Required preimages/content leases have been verified or retained before native mutation.
		/// </summary>
		RecoveryInputsReady = 2,

		/// <summary>
		/// The native child was submitted across the mutation boundary.
		/// </summary>
		NativeSubmitted = 3,

		/// <summary>
		/// A terminal native report was observed, but collection state has not yet been reconciled/checkpointed.
		/// </summary>
		NativeTerminalObserved = 4,

		/// <summary>
		/// Native reality and the corresponding collection binding/association state have been reconciled.
		/// </summary>
		Reconciled = 5
	}
}
