using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.5 durable Collection coordinator state-machine coverage.
	/// </summary>
	[TestFixture]
	public class CollectionOperationCoordinatorTests
	{
		private const string Sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void AdditiveLifecycle_PersistsExactReviewPlanAndCommitsOnlyAfterVerifying()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				Assert.AreEqual(CollectionOperationPhase.Created, operation.Phase);
				Assert.AreEqual(1, operation.CheckpointSequence);

				operation = fixture.Coordinator.BeginResolving(operation.Identity);
				operation = fixture.Coordinator.BeginPreparing(operation.Identity, fixture.Revision);
				CollectionPlanIdentity planIdentity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans plans = CreateReadyPlans(fixture, planIdentity);
				operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, plans.Plan, plans.DependencyPlan, plans.ImpactPlan);

				Assert.AreEqual(CollectionOperationPhase.ReadyForReview, operation.Phase);
				Assert.AreEqual(planIdentity, operation.PlanIdentity);
				CollectionResolvedPlanRecord persisted = fixture.PlanStore.GetPlan(planIdentity);
				Assert.IsNotNull(persisted);
				Assert.AreEqual(CollectionReviewedWorkflowSnapshotCodec.PayloadFormat, persisted.PayloadFormat);
				Assert.Greater(persisted.Payload.Length, 0);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshotCodec.Deserialize(persisted.Payload);
				Assert.AreEqual(planIdentity, snapshot.Identity);
				Assert.AreEqual(fixture.Revision, snapshot.Revision);
				Assert.AreEqual(1, snapshot.Members.Count);

				operation = fixture.Coordinator.MarkReadyToApply(operation.Identity, planIdentity);
				operation = fixture.Coordinator.BeginApplying(operation.Identity, planIdentity);
				operation = fixture.Coordinator.BeginVerifying(operation.Identity);
				operation = fixture.Coordinator.CompleteCommitted(operation.Identity);

				Assert.AreEqual(CollectionOperationPhase.Completed, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.Committed, operation.ResultState);
				Assert.IsTrue(operation.IsSuccessful);
				Assert.AreEqual(operation.CheckpointSequence, fixture.OperationStore.GetOperation(operation.Identity).CheckpointSequence);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReviewedWorkflowV2_RehydratesOnlyAfterRetainedSourceAndCurrentStateValidation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = PrepareOperation(fixture);
				CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans plans = CreateReadyPlans(fixture, identity);
				operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, plans.Plan, plans.DependencyPlan, plans.ImpactPlan);

				var source = new CollectionRevisionSourceRecord(fixture.Revision, CollectionRevisionSourceInputKind.RawManifest,
					plans.Plan.ManifestSource.ContentHash, plans.Plan.ManifestSource.ByteLength, "collection.json", plans.Plan.ManifestSource,
					null, "artifact-manifest");
				var artifact = new CollectionsRetainedArtifact("artifact-manifest", plans.Plan.ManifestSource.ContentHash,
					plans.Plan.ManifestSource.ByteLength);
				bool manifestLoaded = false;
				bool stateCaptured = false;
				var rehydrator = new CollectionReviewedWorkflowRehydrator(fixture.OperationStore, fixture.PlanStore,
					revision => source,
					(revision, expected) => { manifestLoaded = true; Assert.AreEqual(plans.Plan.ManifestSource, expected); return new byte[10]; },
					artifactId => { Assert.AreEqual("artifact-manifest", artifactId); return artifact; },
					target => { stateCaptured = true; return plans.State; });

				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.Ready, result.Status);
				Assert.IsTrue(result.CanResume);
				Assert.IsTrue(manifestLoaded);
				Assert.IsTrue(stateCaptured);
				Assert.AreEqual(identity, result.Snapshot.Identity);
				Assert.AreEqual(1, result.RemainingMembers.Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReviewedWorkflowV2_ChangedNativeStateRequiresRepreparation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = PrepareOperation(fixture);
				CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans plans = CreateReadyPlans(fixture, identity);
				operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, plans.Plan, plans.DependencyPlan, plans.ImpactPlan);

				var source = new CollectionRevisionSourceRecord(fixture.Revision, CollectionRevisionSourceInputKind.RawManifest,
					plans.Plan.ManifestSource.ContentHash, plans.Plan.ManifestSource.ByteLength, "collection.json", plans.Plan.ManifestSource,
					null, "artifact-manifest");
				var artifact = new CollectionsRetainedArtifact("artifact-manifest", plans.Plan.ManifestSource.ContentHash,
					plans.Plan.ManifestSource.ByteLength);
				CollectionNativeStateIndex changedState = new CollectionNativeStateIndex(fixture.Target,
					new CollectionNativeRootState[0], new CollectionNativeModState[0], new CollectionNativeFileState[0],
					new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
					CollectionNativeStateCoverage.NotApplicable, new CollectionTargetAssociation[0], new CollectionMemberBinding[0],
					new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 99);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(fixture.OperationStore, fixture.PlanStore,
					revision => source, (revision, expected) => new byte[10], artifactId => artifact, target => changedState);

				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.CurrentStateChanged, result.Status);
				Assert.IsFalse(result.CanResume);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReviewedWorkflowV1_IsExplicitlyRepreparationRequired()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = PrepareOperation(fixture);
				CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans plans = CreateReadyPlans(fixture, identity);
				fixture.PlanStore.SavePlan(plans.Plan, CollectionReviewedWorkflowSnapshotCodec.LegacyPayloadFormat, new byte[] { 1 });
				operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, identity, operation.CheckpointSequence + 1, CollectionOperationPhase.ReadyForReview,
					CollectionOperationResultState.Pending, operation.NativeChildren);
				fixture.OperationStore.SaveOperation(operation);

				var rehydrator = new CollectionReviewedWorkflowRehydrator(fixture.OperationStore, fixture.PlanStore,
					revision => { throw new AssertionException("Legacy v1 must not load retained source."); },
					(revision, source) => { throw new AssertionException("Legacy v1 must not load manifest bytes."); },
					artifactId => { throw new AssertionException("Legacy v1 must not load retained artifacts."); },
					target => { throw new AssertionException("Legacy v1 must not capture native state."); });

				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, result.Status);
				StringAssert.Contains("v1", result.Message);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReviewedWorkflowV2_UnreconciledNativeChildRoutesThroughRecoveryBeforeRehydration()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { submitted });
				var rehydrator = new CollectionReviewedWorkflowRehydrator(fixture.OperationStore, fixture.PlanStore,
					revision => { throw new AssertionException("Ambiguous child must route through recovery before source load."); },
					(revision, source) => { throw new AssertionException("Ambiguous child must route through recovery before manifest load."); },
					artifactId => { throw new AssertionException("Ambiguous child must route through recovery before artifact load."); },
					target => { throw new AssertionException("Ambiguous child must route through recovery before state recapture."); });

				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired, result.Status);
				Assert.IsFalse(result.CanResume);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReviewedWorkflowV2_PreparedUnsubmittedChildRemainsSafePendingWork()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				var prepared = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
					submitted.NativeOperation, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { prepared });
				operation = fixture.Coordinator.PauseAtSafeBoundary(operation.Identity);

				var source = new CollectionRevisionSourceRecord(fixture.Revision, CollectionRevisionSourceInputKind.RawManifest,
					plans.Plan.ManifestSource.ContentHash, plans.Plan.ManifestSource.ByteLength, "collection.json", plans.Plan.ManifestSource,
					null, "artifact-manifest");
				var artifact = new CollectionsRetainedArtifact("artifact-manifest", plans.Plan.ManifestSource.ContentHash,
					plans.Plan.ManifestSource.ByteLength);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(fixture.OperationStore, fixture.PlanStore,
					revision => source, (revision, expected) => new byte[10], artifactId => artifact, target => plans.State);

				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.Ready, result.Status);
				Assert.IsTrue(result.CanResume);
				Assert.AreEqual(1, result.RemainingMembers.Count);
				Assert.AreEqual(prepared.Member.MemberKey, result.RemainingMembers.Single());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReviewedWorkflowV2_ReconciledCommittedChildResumesFromVerifiedSafeBoundary()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				var committedResult = new ModOperationResult(submitted.NativeOperation, ModOperationReportedStatus.Succeeded,
					ModOperationDurability.VerifiedCommitted, null);
				var reconciled = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
					submitted.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, committedResult);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { reconciled });
				var source = new CollectionRevisionSourceRecord(fixture.Revision, CollectionRevisionSourceInputKind.RawManifest,
					plans.Plan.ManifestSource.ContentHash, plans.Plan.ManifestSource.ByteLength, "collection.json", plans.Plan.ManifestSource,
					null, "artifact-manifest");
				var artifact = new CollectionsRetainedArtifact("artifact-manifest", plans.Plan.ManifestSource.ContentHash,
					plans.Plan.ManifestSource.ByteLength);
				CollectionNativeStateIndex safeState = CloneStateWithDeploymentSequence(plans.State, 1);
				CollectionNativeChildRecoveryManifest recovery = CreateSafeBoundaryManifest(operation, reconciled, plans.Plan, safeState.Fingerprint);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(fixture.OperationStore, fixture.PlanStore,
					revision => source, (revision, expected) => new byte[10], artifactId => artifact, target => safeState,
					(op, child) => recovery);

				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.Ready, result.Status);
				Assert.AreEqual(0, result.RemainingMembers.Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AwaitingInputDuringResolution_CanResumeResolutionWithoutInventingARevision()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				operation = fixture.Coordinator.BeginResolving(operation.Identity);
				operation = fixture.Coordinator.AwaitInput(operation.Identity);
				operation = fixture.Coordinator.BeginRevalidation(operation.Identity);
				operation = fixture.Coordinator.ResumeResolving(operation.Identity);

				Assert.AreEqual(CollectionOperationPhase.Resolving, operation.Phase);
				Assert.IsNull(operation.Revision);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AwaitingInput_RevalidatesAndResumesPreparationWithoutInventingAPlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				operation = fixture.Coordinator.BeginResolving(operation.Identity);
				operation = fixture.Coordinator.BeginPreparing(operation.Identity, fixture.Revision);
				operation = fixture.Coordinator.AwaitInput(operation.Identity);
				Assert.AreEqual(CollectionOperationPhase.AwaitingInput, operation.Phase);
				Assert.IsNull(operation.PlanIdentity);

				operation = fixture.Coordinator.BeginRevalidation(operation.Identity);
				operation = fixture.Coordinator.ResumePreparing(operation.Identity);

				Assert.AreEqual(CollectionOperationPhase.Preparing, operation.Phase);
				Assert.AreEqual(fixture.Revision, operation.Revision);
				Assert.IsNull(operation.PlanIdentity);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReopenPreparation_ProducesNextPlanVersionBeforeNativeBoundary()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = PrepareOperation(fixture);
				CollectionPlanIdentity firstIdentity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans first = CreateReadyPlans(fixture, firstIdentity);
				operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, first.Plan, first.DependencyPlan, first.ImpactPlan);
				operation = fixture.Coordinator.ReopenPreparation(operation.Identity);

				CollectionPlanIdentity secondIdentity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				Assert.AreEqual(firstIdentity.PlanId, secondIdentity.PlanId);
				Assert.AreEqual(firstIdentity.Version + 1, secondIdentity.Version);
				ReadyPlans second = CreateReadyPlans(fixture, secondIdentity);
				operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, second.Plan, second.DependencyPlan, second.ImpactPlan);

				Assert.AreEqual(secondIdentity, operation.PlanIdentity);
				Assert.IsNotNull(fixture.PlanStore.GetPlan(firstIdentity));
				Assert.IsNotNull(fixture.PlanStore.GetPlan(secondIdentity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PlanIdentity_CanOnlyBeCreatedWhilePreparing()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.CreateNextPlanIdentity(operation.Identity));

				operation = fixture.Coordinator.BeginResolving(operation.Identity);
				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.CreateNextPlanIdentity(operation.Identity));

				operation = fixture.Coordinator.BeginPreparing(operation.Identity, fixture.Revision);
				Assert.IsNotNull(fixture.Coordinator.CreateNextPlanIdentity(operation.Identity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReadyForReview_RejectsReplacementPolicyInC6()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = PrepareOperation(fixture);
				CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans additive = CreateReadyPlans(fixture, identity);
				var replacement = new ResolvedCollectionPlan(identity, additive.Plan.Target,
					CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection),
					additive.Plan.CurrentStateFingerprint, additive.Plan.CapabilityReport, additive.Plan.SelectedMembers);

				Assert.Throws<ArgumentException>(() => fixture.Coordinator.MarkReadyForReview(operation.Identity, replacement,
					additive.DependencyPlan, additive.ImpactPlan));
				Assert.AreEqual(CollectionOperationPhase.Preparing, fixture.OperationStore.GetOperation(operation.Identity).Phase);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReadyToApply_RejectsStaleConsentAndIllegalPhaseSkipping()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.BeginApplying(operation.Identity,
					CollectionPlanIdentity.From(Guid.NewGuid(), 1)));

				operation = fixture.Coordinator.BeginResolving(operation.Identity);
				operation = fixture.Coordinator.BeginPreparing(operation.Identity, fixture.Revision);
				CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
				ReadyPlans plans = CreateReadyPlans(fixture, identity);
				operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, plans.Plan, plans.DependencyPlan, plans.ImpactPlan);

				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.MarkReadyToApply(operation.Identity,
					CollectionPlanIdentity.From(Guid.NewGuid(), 1)));
				Assert.AreEqual(CollectionOperationPhase.ReadyForReview,
					fixture.OperationStore.GetOperation(operation.Identity).Phase);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void PreApplyCancellationAndFailureAreTerminalWithoutNativeChildren()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation cancelled = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				cancelled = fixture.Coordinator.BeginResolving(cancelled.Identity);
				cancelled = fixture.Coordinator.CompleteCancelledBeforeApply(cancelled.Identity);
				Assert.AreEqual(CollectionOperationResultState.CancelledBeforeApply, cancelled.ResultState);
				Assert.IsTrue(cancelled.IsTerminal);

				CollectionOperation failed = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
				failed = fixture.Coordinator.BeginResolving(failed.Identity);
				failed = fixture.Coordinator.BeginPreparing(failed.Identity, fixture.Revision);
				failed = fixture.Coordinator.CompleteFailedBeforeApply(failed.Identity);
				Assert.AreEqual(CollectionOperationResultState.FailedBeforeApply, failed.ResultState);
				Assert.IsFalse(failed.HasCrossedNativeBoundary);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Commit_RejectsPreparedButUnsubmittedChild()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				CollectionNativeChildOperation prepared = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member,
					submitted.Action, submitted.NativeOperation, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { prepared });
				operation = fixture.Coordinator.BeginVerifying(operation.Identity);

				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.CompleteCommitted(operation.Identity));
				Assert.AreEqual(CollectionOperationPhase.Verifying, fixture.OperationStore.GetOperation(operation.Identity).Phase);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void UnknownSubmittedChild_MustEnterRecoveryRequiredBeforeRecoveryCanResume()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation child = CreateSubmittedChild(fixture, plans.Plan, 1);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { child });

				operation = fixture.Coordinator.MarkRecoveryRequired(operation.Identity);
				Assert.AreEqual(CollectionOperationPhase.RecoveryRequired, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.RecoveryRequired, operation.ResultState);
				Assert.IsTrue(operation.RequiresRecovery);
				Assert.IsFalse(operation.IsTerminal);

				operation = fixture.Coordinator.BeginRecovery(operation.Identity);
				Assert.AreEqual(CollectionOperationPhase.Recovering, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.Pending, operation.ResultState);
				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.CompleteRolledBack(operation.Identity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void StoppedPartial_RequiresKnownReconciledNativeDurability()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { submitted });
				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.CompleteStoppedPartial(operation.Identity));

				ModOperationResult committed = new ModOperationResult(submitted.NativeOperation,
					ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null);
				CollectionNativeChildOperation reconciled = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member,
					submitted.Action, submitted.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, committed);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { reconciled });
				operation = fixture.Coordinator.CompleteStoppedPartial(operation.Identity);

				Assert.AreEqual(CollectionOperationPhase.Completed, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.StoppedPartial, operation.ResultState);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReconciledCommittedRecovery_CanResumeFinalVerification()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { submitted });
				operation = fixture.Coordinator.BeginRecovery(operation.Identity);

				ModOperationResult committed = new ModOperationResult(submitted.NativeOperation,
					ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null);
				CollectionNativeChildOperation reconciled = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member,
					submitted.Action, submitted.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, committed);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { reconciled });
				operation = fixture.Coordinator.ResumeVerifyingAfterRecovery(operation.Identity, plans.Plan.Identity);

				Assert.AreEqual(CollectionOperationPhase.Verifying, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.Pending, operation.ResultState);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReconciledCommittedRecovery_CanResumeRemainingNativeChildren()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { submitted });
				operation = fixture.Coordinator.BeginRecovery(operation.Identity);

				ModOperationResult committed = new ModOperationResult(submitted.NativeOperation,
					ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null);
				CollectionNativeChildOperation reconciled = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member,
					submitted.Action, submitted.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, committed);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { reconciled });
				operation = fixture.Coordinator.ResumeApplyingAfterRecovery(operation.Identity, plans.Plan.Identity);

				Assert.AreEqual(CollectionOperationPhase.ApplyingNativeChildren, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.Pending, operation.ResultState);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReconciledRecovery_CanCompleteRolledBackButCannotBeCancelledBeforeApply()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionOperation operation = MoveToApplying(fixture, out ReadyPlans plans);
				CollectionNativeChildOperation submitted = CreateSubmittedChild(fixture, plans.Plan, 1);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { submitted });
				Assert.Throws<InvalidOperationException>(() => fixture.Coordinator.CompleteCancelledBeforeApply(operation.Identity));
				operation = fixture.Coordinator.BeginRecovery(operation.Identity);

				ModOperationResult rolledBack = new ModOperationResult(submitted.NativeOperation,
					ModOperationReportedStatus.Failed, ModOperationDurability.VerifiedRolledBack, null);
				CollectionNativeChildOperation reconciled = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member,
					submitted.Action, submitted.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, rolledBack);
				operation = ReplaceChildren(fixture.OperationStore, operation, new[] { reconciled });
				operation = fixture.Coordinator.CompleteRolledBack(operation.Identity);

				Assert.AreEqual(CollectionOperationPhase.Completed, operation.Phase);
				Assert.AreEqual(CollectionOperationResultState.RolledBack, operation.ResultState);
				Assert.IsFalse(operation.HasUnknownNativeDurability);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionOperation PrepareOperation(Fixture fixture)
		{
			CollectionOperation operation = fixture.Coordinator.CreateApplyOperation(fixture.Revision.Collection, fixture.Target);
			operation = fixture.Coordinator.BeginResolving(operation.Identity);
			return fixture.Coordinator.BeginPreparing(operation.Identity, fixture.Revision);
		}

		private static CollectionOperation MoveToApplying(Fixture fixture, out ReadyPlans plans)
		{
			CollectionOperation operation = PrepareOperation(fixture);
			CollectionPlanIdentity identity = fixture.Coordinator.CreateNextPlanIdentity(operation.Identity);
			plans = CreateReadyPlans(fixture, identity);
			operation = fixture.Coordinator.MarkReadyForReview(operation.Identity, plans.Plan, plans.DependencyPlan, plans.ImpactPlan);
			operation = fixture.Coordinator.MarkReadyToApply(operation.Identity, identity);
			return fixture.Coordinator.BeginApplying(operation.Identity, identity);
		}

		private static CollectionOperation ReplaceChildren(CollectionsOperationStore store, CollectionOperation operation,
			IEnumerable<CollectionNativeChildOperation> children)
		{
			CollectionOperation updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection,
				operation.Target, operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1,
				operation.Phase, operation.ResultState, children);
			store.SaveOperation(updated);
			return updated;
		}

		private static CollectionNativeStateIndex CloneStateWithDeploymentSequence(CollectionNativeStateIndex state, long sequence)
		{
			return new CollectionNativeStateIndex(state.Target, state.Roots, state.Mods.Values, state.Files.Values, state.IniEdits.Values,
				state.GameValues.Values, state.Plugins.Values, state.PluginCoverage, state.Associations.Values,
				state.BindingsByAssociation.Values.SelectMany(x => x), state.OverridesByAssociation.Values.SelectMany(x => x),
				state.AssociationCoverage, state.Issues, sequence);
		}

		private static CollectionNativeChildRecoveryManifest CreateSafeBoundaryManifest(CollectionOperation operation,
			CollectionNativeChildOperation child, ResolvedCollectionPlan plan, CollectionCurrentStateFingerprint safeBoundary)
		{
			ResolvedCollectionMemberPlan member = plan.SelectedMembers.Single(x => x.MemberKey.Equals(child.Member.MemberKey));
			var preview = new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity, ModInstallMethod.Virtual,
				ModInstallRoot.Data, new CollectionPlannedFileEffect[0], new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var evidence = new CollectionNativeChildExecutionEvidence("game", 100, 200, "member.7z", preview,
				new CollectionNativeFileContentEvidence[0], new CollectionNativeFileContentEvidence[0],
				new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
				new CollectionExpectedReplayOperation[0]);
			return new CollectionNativeChildRecoveryManifest(operation.Identity, child.Sequence, plan.Identity, child.Member, child.Action,
				child.NativeOperation, plan.CurrentStateFingerprint,
				new CollectionRecoveryArtifact("artifact-child", CollectionContentHash.FromSha256(Sha256), 1), null, null,
				new CollectionScriptedReplayRecoverySnapshot(false, null, false, new CollectionReplayRecoveryPayload[0]), evidence, safeBoundary, safeBoundary);
		}

		private static CollectionNativeChildOperation CreateSubmittedChild(Fixture fixture, ResolvedCollectionPlan plan, int sequence)
		{
			ResolvedCollectionMemberPlan member = plan.SelectedMembers.Single();
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(fixture.Target.Fingerprint,
					new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data), member.RecipeIdentity.Fingerprint));
			return new CollectionNativeChildOperation(sequence,
				new CollectionOperationMemberReference(fixture.Revision, member.MemberKey),
				CollectionNativeChildAction.ActivateOrReinstall, native, CollectionNativeChildCheckpoint.NativeSubmitted, null);
		}

		private static ReadyPlans CreateReadyPlans(Fixture fixture, CollectionPlanIdentity identity)
		{
			NormalizedCollectionMember member = CreateMember();
			CollectionTargetAssociation association = new CollectionTargetAssociation(Guid.NewGuid(), fixture.Revision,
				fixture.Target, CollectionAssociationState.Applied);
			CollectionNativeModState nativeMod = new CollectionNativeModState(
				new NativeModInstanceIdentity(fixture.Target, "native-member"), "archive", "member.7z", "100", "200",
				"1.0", "1.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
			CollectionMemberBinding binding = new CollectionMemberBinding(association, member.IdentityResolution.Key,
				nativeMod.Identity, member.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting);
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(fixture.Target,
				new CollectionNativeRootState[0], new[] { nativeMod }, new CollectionNativeFileState[0],
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, new[] { association }, new[] { binding }, new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(fixture.Revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256), 10, NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion),
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(identity, fixture.Target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, report,
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) });
			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(plan, state);
			Assert.AreEqual(CollectionMemberMatchDisposition.InstalledCompatible, matches.Members.Single().Disposition);
			CollectionDependencyPhasePlan dependencyPlan = new CollectionDependencyPhasePlanner().Plan(plan, matches);
			CollectionConflictImpactPlan impactPlan = new CollectionConflictImpactPlanner().Plan(plan, matches, dependencyPlan,
				state, new CollectionMemberEffectPreview[0]);
			Assert.IsTrue(dependencyPlan.IsReady);
			Assert.IsTrue(impactPlan.IsReady);
			return new ReadyPlans(plan, dependencyPlan, impactPlan, state);
		}

		private static NormalizedCollectionMember CreateMember()
		{
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "game/100/200", null),
				CollectionRecipeIdentity.FromFingerprint("recipe-member"), "Member", 0);
		}

		private static Fixture CreateFixture(string root)
		{
			var featureStore = new CollectionsStore(root);
			featureStore.CreateNew();
			var catalog = new CollectionsCatalogStore(featureStore);
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c6-5");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c6-5", 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "Collection C6.5", null, null),
				new CollectionRevision(revision, "Revision", null, 1));
			var operationStore = new CollectionsOperationStore(featureStore);
			var planStore = new CollectionsResolvedPlanStore(featureStore);
			return new Fixture(revision, CollectionTargetIdentity.FromFingerprint("target-c6-5-" + Guid.NewGuid().ToString("N")),
				operationStore, planStore, new CollectionOperationCoordinator(operationStore, planStore));
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c6-5-coordinator-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
				CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
				CollectionOperationCoordinator coordinator)
			{
				Revision = revision;
				Target = target;
				OperationStore = operationStore;
				PlanStore = planStore;
				Coordinator = coordinator;
			}
			public CollectionRevisionIdentity Revision { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionsOperationStore OperationStore { get; }
			public CollectionsResolvedPlanStore PlanStore { get; }
			public CollectionOperationCoordinator Coordinator { get; }
		}

		private sealed class ReadyPlans
		{
			public ReadyPlans(ResolvedCollectionPlan plan, CollectionDependencyPhasePlan dependencyPlan,
				CollectionConflictImpactPlan impactPlan, CollectionNativeStateIndex state)
			{
				Plan = plan;
				DependencyPlan = dependencyPlan;
				ImpactPlan = impactPlan;
				State = state;
			}
			public ResolvedCollectionPlan Plan { get; }
			public CollectionDependencyPhasePlan DependencyPlan { get; }
			public CollectionConflictImpactPlan ImpactPlan { get; }
			public CollectionNativeStateIndex State { get; }
		}
	}
}
