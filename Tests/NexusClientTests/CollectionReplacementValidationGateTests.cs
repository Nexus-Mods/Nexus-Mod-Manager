using System;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.PluginManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsGateA")]
	[Category("CollectionsReplacement")]
	public class CollectionReplacementValidationGateTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void PrepareMandatoryRecovery_NoOutgoingEffectsOrProfile_AllowsEmptyOperationRecoveryInputSet()
		{
			using (Scenario scenario = Scenario.Create("empty-recovery", false))
			{
				CollectionOperation operation = scenario.CreateApprovedOperation();

				operation = scenario.Coordinator.PrepareMandatoryRecovery(operation.Identity, scenario.Plan.Identity,
					scenario.Current, CollectionReplacementOptionalBackupResult.NotRequested(), new CollectionReplacementRecoveryInput[0]);

				Assert.That(operation.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
				CollectionReplacementRecoveryBoundary boundary = scenario.Coordinator.GetRecoveryBoundary(operation.Identity, scenario.Plan.Identity);
				Assert.That(boundary.Inputs, Is.Empty);
			}
		}

		[Test]
		public void PrepareMandatoryRecovery_ProfileProtectionStillRequiresMatchingRecoveryEvidence()
		{
			using (Scenario scenario = Scenario.Create("profile-coverage", true))
			{
				CollectionOperation operation = scenario.CreateApprovedOperation();

				InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
					scenario.Coordinator.PrepareMandatoryRecovery(operation.Identity, scenario.Plan.Identity,
						scenario.Current, CollectionReplacementOptionalBackupResult.NotRequested(), new CollectionReplacementRecoveryInput[0]));

				Assert.That(error.Message, Does.Contain("profile"));
				Assert.That(scenario.OperationStore.GetOperation(operation.Identity).Phase,
					Is.EqualTo(CollectionOperationPhase.CapturingOptionalLocalBackup));
			}
		}

		[Test]
		public void InspectInterrupted_ReadyToApplyWithMissingRecoveryBoundary_IsInvalidPersistedState()
		{
			using (Scenario scenario = Scenario.Create("missing-ready-boundary", false))
			{
				CollectionOperation operation = scenario.PrepareReadyToApply();
				scenario.ReferenceStore.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D"));

				CollectionReplacementStartupInspection inspection = scenario.Coordinator.InspectInterrupted(
					scenario.OperationStore.GetOperation(operation.Identity));

				Assert.That(inspection.Disposition, Is.EqualTo(CollectionReplacementStartupDisposition.InvalidPersistedState));
				Assert.That(inspection.Message, Does.Contain("recovery boundary"));
			}
		}

		[Test]
		public void InspectInterrupted_PostRemovalPhaseWithMissingRecoveryBoundary_IsInvalidPersistedState()
		{
			using (Scenario scenario = Scenario.Create("missing-post-removal-boundary", false))
			{
				CollectionOperation operation = scenario.PrepareReadyToApply();
				var postRemoval = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1),
					CollectionOperationPhase.OutgoingRemovalVerified, CollectionOperationResultState.Pending, operation.NativeChildren);
				scenario.OperationStore.SaveOperation(postRemoval);
				scenario.ReferenceStore.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D"));

				CollectionReplacementStartupInspection inspection = scenario.Coordinator.InspectInterrupted(
					scenario.OperationStore.GetOperation(operation.Identity));

				Assert.That(inspection.Disposition, Is.EqualTo(CollectionReplacementStartupDisposition.InvalidPersistedState));
				Assert.That(inspection.Message, Does.Contain("recovery boundary"));
			}
		}

		[Test]
		public void FinalizeReplacementAssociations_ExactReviewedSet_CommitsIncomingAssociationAndOperationTogether()
		{
			using (Scenario scenario = Scenario.Create("atomic-finalize", false))
			{
				CollectionOperation operation = scenario.PrepareReadyToApply();
				CollectionOperation committed = scenario.CreateCommittedOperation(operation);
				var incoming = new CollectionTargetAssociation(Guid.NewGuid(), scenario.Plan.Revision,
					scenario.Plan.Target, CollectionAssociationState.Applied);

				scenario.AssociationStore.FinalizeReplacementAssociations(scenario.Intent, incoming,
					new CollectionMemberBinding[0], committed);

				Assert.That(scenario.AssociationStore.GetAssociation(incoming.AssociationId), Is.Not.Null);
				CollectionOperation persisted = scenario.OperationStore.GetOperation(operation.Identity);
				Assert.That(persisted.IsSuccessful, Is.True);
				Assert.That(persisted.CheckpointSequence, Is.EqualTo(committed.CheckpointSequence));
			}
		}

		[Test]
		public void FinalizeReplacementAssociations_ChangedAssociationSet_RollsBackIncomingPublicationAndTerminalCheckpoint()
		{
			using (Scenario scenario = Scenario.Create("atomic-cas", false))
			{
				CollectionOperation operation = scenario.PrepareReadyToApply();
				CollectionOperation committed = scenario.CreateCommittedOperation(operation);
				CollectionRevisionIdentity unexpectedRevision = scenario.SeedAdditionalRevision("unexpected-revision", 2);
				var unexpected = new CollectionTargetAssociation(Guid.NewGuid(), unexpectedRevision,
					scenario.Plan.Target, CollectionAssociationState.Applied);
				scenario.AssociationStore.SaveAssociation(unexpected);
				var incoming = new CollectionTargetAssociation(Guid.NewGuid(), scenario.Plan.Revision,
					scenario.Plan.Target, CollectionAssociationState.Applied);

				Assert.Throws<InvalidOperationException>(() => scenario.AssociationStore.FinalizeReplacementAssociations(
					scenario.Intent, incoming, new CollectionMemberBinding[0], committed));

				Assert.That(scenario.AssociationStore.GetAssociation(unexpected.AssociationId), Is.Not.Null);
				Assert.That(scenario.AssociationStore.GetAssociation(incoming.AssociationId), Is.Null);
				CollectionOperation persisted = scenario.OperationStore.GetOperation(operation.Identity);
				Assert.That(persisted.IsSuccessful, Is.False);
				Assert.That(persisted.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
			}
		}

		private sealed class Scenario : IDisposable
		{
			private Scenario(string root, CollectionsStore store, ResolvedCollectionPlan plan,
				CollectionReplacementCurrentSetupSnapshot current, CollectionReplacementDiffPlan diff,
				CollectionReplacementReviewedIntent intent)
			{
				Root = root;
				Store = store;
				Plan = plan;
				Current = current;
				Diff = diff;
				Intent = intent;
				OperationStore = new CollectionsOperationStore(store);
				AssociationStore = new CollectionsAssociationStore(store);
				ReferenceStore = new CollectionsRetainedArtifactReferenceStore(store);
				Coordinator = new CollectionReplacementOperationCoordinator(OperationStore,
					new CollectionsResolvedPlanStore(store), new CollectionsRetainedArtifactStore(store), ReferenceStore);
			}

			public string Root { get; private set; }
			public CollectionsStore Store { get; private set; }
			public ResolvedCollectionPlan Plan { get; private set; }
			public CollectionReplacementCurrentSetupSnapshot Current { get; private set; }
			public CollectionReplacementDiffPlan Diff { get; private set; }
			public CollectionReplacementReviewedIntent Intent { get; private set; }
			public CollectionsOperationStore OperationStore { get; private set; }
			public CollectionsAssociationStore AssociationStore { get; private set; }
			public CollectionsRetainedArtifactReferenceStore ReferenceStore { get; private set; }
			public CollectionReplacementOperationCoordinator Coordinator { get; private set; }

			public static Scenario Create(string suffix, bool protectProfile)
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c8-10-" + suffix + "-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(root);
				var store = new CollectionsStore(root);
				store.CreateNew();

				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-10-" + suffix);
				CollectionNativeStateIndex state = CreateState(target);
				var current = new CollectionReplacementCurrentSetupSnapshot(state,
					new CollectionDriftObservation[0], new NativeModProvenance[0]);
				CollectionIdentity collection = CollectionIdentity.FromNexus("incoming-c8-10-" + suffix);
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c8-10", 1);
				new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
					new CollectionDefinition(collection, "C8.10 validation", null, null),
					new CollectionRevision(revision, "C8.10 revision", null, 0));
				var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256A),
					100, "schema-v1", "normalizer-v1");
				var manifest = new NormalizedCollectionManifest(revision, source,
					CollectionManifestMemberSetCompleteness.Complete, null, new NormalizedCollectionMember[0]);
				CollectionCapabilityReport capability = CollectionCapabilityReport.Create(manifest);
				var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
					CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection),
					state.Fingerprint, capability, new ResolvedCollectionMemberPlan[0]);
				CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(plan, current);
				CollectionReplacementEnvironmentProjection environment = new CollectionReplacementEnvironmentProjector().Project(diff);
				CollectionReplacementProfileProtectionSnapshot profile = protectProfile
					? CollectionReplacementProfileProtectionSnapshot.Preserve("profile-c8-10", "Profile C8.10", "profile-fingerprint-c8-10")
					: CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile();
				CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntent.Create(diff, environment,
					new CollectionReplacementNativeApproval[0], new CollectionReplacementAssociationApproval[0],
					new CollectionReplacementPreparedRecipeApproval[0], new CollectionReplacementFileWinnerApproval[0], profile);
				return new Scenario(root, store, plan, current, diff, intent);
			}

			public CollectionOperation CreateApprovedOperation()
			{
				CollectionOperation operation = Coordinator.CreateReviewedOperation(Diff, Intent);
				return Coordinator.Approve(operation.Identity, Plan.Identity, Current);
			}

			public CollectionOperation PrepareReadyToApply()
			{
				CollectionOperation operation = CreateApprovedOperation();
				return Coordinator.PrepareMandatoryRecovery(operation.Identity, Plan.Identity, Current,
					CollectionReplacementOptionalBackupResult.NotRequested(), new CollectionReplacementRecoveryInput[0]);
			}

			public CollectionOperation CreateCommittedOperation(CollectionOperation operation)
			{
				return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1),
					CollectionOperationPhase.Completed, CollectionOperationResultState.Committed, operation.NativeChildren);
			}

			public CollectionRevisionIdentity SeedAdditionalRevision(string revisionId, long number)
			{
				CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(Plan.Revision.Collection, revisionId, number);
				new CollectionsCatalogStore(Store).SaveRevision(new CollectionRevision(revision, revisionId, null, 0));
				return revision;
			}

			public void Dispose()
			{
				if (Directory.Exists(Root)) Directory.Delete(Root, true);
			}
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}
	}
}
