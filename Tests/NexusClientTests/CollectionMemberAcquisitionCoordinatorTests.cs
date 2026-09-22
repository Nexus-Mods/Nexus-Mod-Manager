using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.5 composition coverage for C6.2 matching, C4 acquisition and mandatory post-pause revalidation.
	/// </summary>
	[TestFixture]
	public class CollectionMemberAcquisitionCoordinatorTests
	{
		[Test]
		public void Begin_PremiumMissingArchive_PersistsAwaitingInputBeforeNativeQueue()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true);
				CollectionAdditivePlanBuildResult planBuild = fixture.BuildPlan(CreateState(fixture.Target, 0));

				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(planBuild, null, CancellationToken.None);

				Assert.That(batch.IsAwaitingInput, Is.True);
				Assert.That(batch.IsReady, Is.False);
				Assert.That(batch.Members.Single().Disposition, Is.EqualTo(CollectionMemberAcquisitionDisposition.PremiumQueued));
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(1));
				Assert.That(fixture.Queue.PhaseObservedAtQueue, Is.EqualTo(CollectionOperationPhase.AwaitingInput),
					"The durable C6.5 pause must exist before AddMod/Premium work is started.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ProbeCompletedInput_ManualArchiveBecomesVerifiedWithoutUsingStaleNativeStateAsApproval()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionAdditivePlanBuildResult planBuild = fixture.BuildPlan(CreateState(fixture.Target, 0));
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(planBuild, null, CancellationToken.None);
				Assert.That(waiting.Members.Single().Disposition, Is.EqualTo(CollectionMemberAcquisitionDisposition.ManualInputRequired));

				fixture.ArchiveSource.Candidates = new[] { fixture.ExactCandidate };
				CollectionMemberAcquisitionBatch ready = fixture.Acquisition.ProbeCompletedInput(waiting, CancellationToken.None);

				Assert.That(ready.IsReady, Is.True);
				Assert.That(ready.IsAwaitingInput, Is.True,
					"Archive readiness alone must not resume planning against the pre-pause native-state snapshot.");
				Assert.That(ready.Members.Single().Disposition, Is.EqualTo(CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive));
				Assert.That(ready.Members.Single().VerifiedArchive.Artifact.ContentHash, Is.EqualTo(fixture.ArchiveHash));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RevalidateAfterPause_ChangedNativeState_IncrementsPlanVersionAndRebindsVerifiedBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionNativeStateIndex initialState = CreateState(fixture.Target, 0);
				CollectionAdditivePlanBuildResult initialPlan = fixture.BuildPlan(initialState);
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(initialPlan, null, CancellationToken.None);
				fixture.ArchiveSource.Candidates = new[] { fixture.ExactCandidate };
				CollectionMemberAcquisitionBatch ready = fixture.Acquisition.ProbeCompletedInput(waiting, CancellationToken.None);
				CollectionVerifiedArchive previousArchive = ready.Members.Single().VerifiedArchive;

				CollectionNativeStateIndex refreshedState = CreateState(fixture.Target, 1);
				CollectionAdditivePlanRevalidationResult result = fixture.Revalidation.RevalidateAfterPause(
					ready, fixture.Target, refreshedState, CancellationToken.None);

				Assert.That(result.NativeStateChanged, Is.True);
				Assert.That(result.PlanBuild.Plan.Identity.PlanId, Is.EqualTo(initialPlan.Plan.Identity.PlanId));
				Assert.That(result.PlanBuild.Plan.Identity.Version, Is.EqualTo(initialPlan.Plan.Identity.Version + 1));
				Assert.That(result.PlanBuild.Plan.CurrentStateFingerprint, Is.EqualTo(refreshedState.Fingerprint));
				Assert.That(result.PlanBuild.Operation.Phase, Is.EqualTo(CollectionOperationPhase.Preparing));
				Assert.That(result.AcquisitionBatch.IsReady, Is.True);
				CollectionVerifiedArchive rebound = result.AcquisitionBatch.Members.Single().VerifiedArchive;
				Assert.That(rebound.Artifact.ArtifactId, Is.EqualTo(previousArchive.Artifact.ArtifactId));
				Assert.That(rebound.Request.PlanIdentity, Is.EqualTo(result.PlanBuild.Plan.Identity));
				Assert.That(rebound.Request.RequestId, Is.Not.EqualTo(previousArchive.Request.RequestId));

				CollectionOperation durable = fixture.OperationStore.GetOperation(result.PlanBuild.Operation.Identity);
				Assert.That(durable.Phase, Is.EqualTo(CollectionOperationPhase.Preparing));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RevalidateAfterPause_UnchangedNativeState_PreservesPlanIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionNativeStateIndex state = CreateState(fixture.Target, 0);
				CollectionAdditivePlanBuildResult initialPlan = fixture.BuildPlan(state);
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(initialPlan, null, CancellationToken.None);
				fixture.ArchiveSource.Candidates = new[] { fixture.ExactCandidate };
				CollectionMemberAcquisitionBatch ready = fixture.Acquisition.ProbeCompletedInput(waiting, CancellationToken.None);

				CollectionAdditivePlanRevalidationResult result = fixture.Revalidation.RevalidateAfterPause(
					ready, fixture.Target, CreateState(fixture.Target, 0), CancellationToken.None);

				Assert.That(result.NativeStateChanged, Is.False);
				Assert.That(result.PlanBuild.Plan.Identity, Is.EqualTo(initialPlan.Plan.Identity));
				Assert.That(result.AcquisitionBatch.IsReady, Is.True);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RevalidateAfterPause_ChangedCanonicalTarget_FailsOperationBeforeAnyNativeMutation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionAdditivePlanBuildResult initialPlan = fixture.BuildPlan(CreateState(fixture.Target, 0));
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(initialPlan, null, CancellationToken.None);
				fixture.ArchiveSource.Candidates = new[] { fixture.ExactCandidate };
				CollectionMemberAcquisitionBatch ready = fixture.Acquisition.ProbeCompletedInput(waiting, CancellationToken.None);
				CollectionTargetIdentity otherTarget = CanonicalTarget('b');

				Assert.Throws<InvalidOperationException>(() => fixture.Revalidation.RevalidateAfterPause(
					ready, otherTarget, CreateState(otherTarget, 0), CancellationToken.None));

				CollectionOperation durable = fixture.OperationStore.GetOperation(initialPlan.Operation.Identity);
				Assert.That(durable.Phase, Is.EqualTo(CollectionOperationPhase.Completed));
				Assert.That(durable.ResultState, Is.EqualTo(CollectionOperationResultState.FailedBeforeApply));
				Assert.That(durable.HasCrossedNativeBoundary, Is.False);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReconcileRestart_ManualPendingRequest_RemainsExplicitActionRequired()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionAdditivePlanBuildResult planBuild = fixture.BuildPlan(CreateState(fixture.Target, 0));
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(planBuild, null, CancellationToken.None);

				CollectionMemberAcquisitionBatch reconciled = fixture.Acquisition.ReconcileRestart(
					waiting.PlanBuild, CancellationToken.None);

				Assert.That(reconciled.IsAwaitingInput, Is.True);
				Assert.That(reconciled.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberAcquisitionDisposition.RestartActionRequired));
				Assert.That(reconciled.Members.Single().RestartResult.Disposition,
					Is.EqualTo(CollectionAcquisitionRestartDisposition.ManualInputRequired));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Fixture CreateFixture(string root, bool premium)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			var operationStore = new CollectionsOperationStore(store);
			var operationCoordinator = new CollectionOperationCoordinator(operationStore, new CollectionsResolvedPlanStore(store));
			var catalog = new CollectionsCatalogStore(store);
			var revisionSources = new CollectionsRevisionSourceStore(store);

			byte[] archiveBytes = Encoding.UTF8.GetBytes("c6.15.5 exact member archive");
			CollectionContentHash archiveHash = ComputeHash(archiveBytes);
			string archivePath = Path.Combine(root, "member.zip");
			File.WriteAllBytes(archivePath, archiveBytes);

			CollectionIdentity collection = CollectionIdentity.FromNexus("c6155-collection");
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1),
				"Revision 1", null, 1);
			catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C6.15.5", null, null), revision);

			byte[] manifestBytes = Encoding.UTF8.GetBytes("{\"revision\":\"c6155\"}");
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(ComputeHash(manifestBytes),
				manifestBytes.LongLength, "test.collection.schema/1", "test-normalizer/1");
			var member = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-one")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/200", archiveHash),
				CollectionRecipeIdentity.FromFingerprint("recipe-member-one"), "Member One", 0);
			var manifest = new NormalizedCollectionManifest(revision.Identity, source,
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			CollectionEffectiveSelection effectiveSelection = new CollectionEffectiveSelectionBuilder().Build(report,
				new CollectionOptionalMemberSelection[0]);
			revisionSources.RetainManifest(manifest, CollectionRevisionSourceInputKind.RawManifest,
				source.ContentHash, source.ByteLength, "collection.json", manifestBytes);

			var artifacts = new CollectionsRetainedArtifactStore(store);
			var references = new CollectionsRetainedArtifactReferenceStore(store);
			var acquisitions = new CollectionsAcquisitionStore(store);
			var archiveSource = new MutableArchiveSource();
			var adopter = new CollectionVerifiedArchiveAdopter(archiveSource, new RejectingVerifier(), artifacts, references, acquisitions);
			var queue = new RecordingQueue(operationStore);
			var requestCoordinator = new CollectionAcquisitionRequestCoordinator(queue, acquisitions);
			var premiumCoordinator = new CollectionPremiumAcquisitionCoordinator(requestCoordinator,
				new FixedAccountProvider(premium));
			var manualCoordinator = new CollectionManualAcquisitionCoordinator(requestCoordinator, adopter);
			var restartCoordinator = new CollectionAcquisitionRestartCoordinator(acquisitions,
				new EmptyPersistedAddModStateSource(), adopter, premiumCoordinator);
			var acquisition = new CollectionMemberAcquisitionCoordinator(new CollectionMemberMatchEngine(), adopter,
				premiumCoordinator, manualCoordinator, restartCoordinator, operationCoordinator);
			var builder = new CollectionResolvedPlanBuilder(catalog, revisionSources, operationCoordinator);
			var revalidation = new CollectionAdditivePlanRevalidationService(operationCoordinator, builder, acquisition);

			return new Fixture(store, operationStore, builder, acquisition, revalidation, archiveSource, queue,
				effectiveSelection, CanonicalTarget('a'), archiveHash,
				new CollectionManagedArchiveCandidate("nexus-mod-file", "skyrimspecialedition/100/200", archivePath));
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, long deploymentSequence)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
				new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new CollectionTargetAssociation[0], new CollectionMemberBinding[0], new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], deploymentSequence);
		}

		private static CollectionTargetIdentity CanonicalTarget(char value)
		{
			return CollectionTargetIdentity.FromFingerprint("target-sha256:" + new string(value, 64));
		}

		private static CollectionContentHash ComputeHash(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c6-15-5-acquisition-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsStore store, CollectionsOperationStore operationStore, CollectionResolvedPlanBuilder builder,
				CollectionMemberAcquisitionCoordinator acquisition, CollectionAdditivePlanRevalidationService revalidation,
				MutableArchiveSource archiveSource, RecordingQueue queue, CollectionEffectiveSelection effectiveSelection,
				CollectionTargetIdentity target, CollectionContentHash archiveHash, CollectionManagedArchiveCandidate exactCandidate)
			{
				Store = store;
				OperationStore = operationStore;
				Builder = builder;
				Acquisition = acquisition;
				Revalidation = revalidation;
				ArchiveSource = archiveSource;
				Queue = queue;
				EffectiveSelection = effectiveSelection;
				Target = target;
				ArchiveHash = archiveHash;
				ExactCandidate = exactCandidate;
			}

			public CollectionsStore Store { get; }
			public CollectionsOperationStore OperationStore { get; }
			public CollectionResolvedPlanBuilder Builder { get; }
			public CollectionMemberAcquisitionCoordinator Acquisition { get; }
			public CollectionAdditivePlanRevalidationService Revalidation { get; }
			public MutableArchiveSource ArchiveSource { get; }
			public RecordingQueue Queue { get; }
			public CollectionEffectiveSelection EffectiveSelection { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionContentHash ArchiveHash { get; }
			public CollectionManagedArchiveCandidate ExactCandidate { get; }

			public CollectionAdditivePlanBuildResult BuildPlan(CollectionNativeStateIndex state)
			{
				return Builder.Build(EffectiveSelection, Target, state);
			}
		}

		private sealed class MutableArchiveSource : ICollectionManagedArchiveSource
		{
			public IReadOnlyList<CollectionManagedArchiveCandidate> Candidates { get; set; } =
				new CollectionManagedArchiveCandidate[0];

			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				return Candidates;
			}
		}

		private sealed class RejectingVerifier : ICollectionArchiveIdentityVerifier
		{
			public bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive,
				CancellationToken cancellationToken)
			{
				throw new AssertionException("Expected-content-hash acquisition should not require provider identity verification.");
			}
		}

		private sealed class FixedAccountProvider : ICollectionPremiumAcquisitionAccountProvider
		{
			private readonly bool _premium;

			public FixedAccountProvider(bool premium)
			{
				_premium = premium;
			}

			public CollectionPremiumAcquisitionAccountState Capture()
			{
				return new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, _premium);
			}
		}

		private sealed class EmptyPersistedAddModStateSource : ICollectionPersistedAddModStateSource
		{
			public CollectionPersistedAddModState Find(Guid queueOperationId)
			{
				return null;
			}
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			private readonly CollectionsOperationStore _operationStore;

			public RecordingQueue(CollectionsOperationStore operationStore)
			{
				_operationStore = operationStore;
			}

			public int CallCount { get; private set; }
			public CollectionOperationPhase? PhaseObservedAtQueue { get; private set; }

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				CollectionOperation operation = _operationStore.GetIncompleteOperations().Single();
				PhaseObservedAtQueue = operation.Phase;
				return new TestBackgroundTask();
			}
		}

		private sealed class TestBackgroundTask : BackgroundTask
		{
		}
	}
}
