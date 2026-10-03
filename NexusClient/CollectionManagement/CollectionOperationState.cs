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
		DetachTracking = 7,
		ReplaceCurrentManagedSetup = 8,
		RemoveCollectionMemberEffects = 9
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
		Completed = 14,

		/// <summary>Replacement-specific native phase: exact reviewed outgoing deactivations are being executed.</summary>
		RemovingOutgoingNativeChildren = 15,

		/// <summary>Replacement-specific safe barrier: every reviewed outgoing deactivation is authoritatively verified.</summary>
		OutgoingRemovalVerified = 16,

		/// <summary>C8.5 found a supported post-mutation delta which requires a new explicit immutable phase amendment.</summary>
		AwaitingReplacementPhaseAmendment = 17,

		/// <summary>C8.5 verified the post-removal barrier; incoming native work may now begin.</summary>
		ReadyForIncomingNativeChildren = 18,

		/// <summary>C8.5 persisted an explicit phase amendment; the amended observation must be revalidated before native work resumes.</summary>
		ReplacementBarrierRevalidationRequired = 19,

		/// <summary>C8.6 replacement-specific phase: reviewed incoming activate/reinstall children are being executed.</summary>
		InstallingIncomingNativeChildren = 20,

		/// <summary>C8.6 safe barrier: all reviewed incoming children and supported final native state were verified.</summary>
		IncomingNativeChildrenVerified = 21,

		/// <summary>
		/// C10 revision update is removing exact reviewed obsolete old-revision native effects.
		/// </summary>
		RemovingObsoleteRevisionEffects = 22,

		/// <summary>
		/// C10 revision update has verified every qualified obsolete-effect removal.
		/// </summary>
		ObsoleteRevisionEffectsVerified = 23,

		/// <summary>
		/// C10 revision update is executing exact reviewed candidate activate/reinstall children.
		/// </summary>
		InstallingCandidateRevisionChildren = 24,

		/// <summary>
		/// C10 revision update has verified every candidate native child at a durable safe boundary.
		/// </summary>
		CandidateRevisionChildrenVerified = 25,

		/// <summary>C10.7 is replaying the exact qualified C9 overrides after candidate native execution.</summary>
		ReapplyingQualifiedRevisionOverrides = 26,

		/// <summary>C10.7 has verified every replayable override against current authoritative/native activation state.</summary>
		QualifiedRevisionOverridesVerified = 27,

		/// <summary>C10.8 is verifying the complete supported candidate revision plus retained customization.</summary>
		VerifyingCandidateRevisionAggregate = 28,

		/// <summary>C10.8 durably sealed one fresh authoritative aggregate verification for later atomic publication.</summary>
		CandidateRevisionAggregateVerified = 29,

		/// <summary>C10.10c is executing exact qualified native repair children.</summary>
		RepairingQualifiedEffects = 30,

		/// <summary>C10.10c verified every qualified repair against fresh authoritative native state.</summary>
		QualifiedEffectsVerified = 31
	}

	/// <summary>Identifies which high-level Collection workflow is using the shared C6 native-child pipeline.</summary>
	internal enum CollectionNativeChildWorkflowMode
	{
		Additive = 0,
		Replacement = 1,
		RevisionUpdate = 2
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
