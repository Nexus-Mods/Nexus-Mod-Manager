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
using Nexus.Client.CollectionManagement.UI;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.5 composition coverage for C6.2 matching, C4 acquisition and mandatory post-pause revalidation.
	/// </summary>
	[TestFixture]
	[Category("CollectionsC12Compatibility")]
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

				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(planBuild,
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);

				Assert.That(batch.IsAwaitingInput, Is.True);
				Assert.That(CollectionsPreviewControl.CanResumePreparation(batch), Is.True);
				Assert.That(batch.ArchiveOverwritePolicy, Is.EqualTo(CollectionArchiveOverwritePolicy.OverwriteExistingArchives));
				Assert.That(batch.Members.Single().QueueCorrelation.ArchiveOverwritePolicy,
					Is.EqualTo(CollectionArchiveOverwritePolicy.OverwriteExistingArchives));
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

		/// <summary>A blocked member must direct the UI to its issue instead of offering an invalid continuation.</summary>
		[Test]
		public void ResumePresentation_BlockedMemberRejectsBothPreparingAndAwaitingInput()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, false);
				CollectionAdditivePlanBuildResult preparing = fixture.BuildPlan(CreateState(fixture.Target, 0));
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(preparing, null, CancellationToken.None);
				CollectionMemberAcquisitionState blocked = new CollectionMemberAcquisitionState(waiting.Members.Single().Match,
					CollectionMemberAcquisitionDisposition.Blocked, null, null, null, null, null, null);
				CollectionMemberAcquisitionBatch blockedPreparing = new CollectionMemberAcquisitionBatch(preparing,
					waiting.MatchSet, new[] { blocked }, waiting.ArchiveOverwritePolicy);
				CollectionMemberAcquisitionBatch blockedWaiting = new CollectionMemberAcquisitionBatch(waiting.PlanBuild,
					waiting.MatchSet, new[] { blocked }, waiting.ArchiveOverwritePolicy);

				Assert.That(blockedPreparing.IsAwaitingInput, Is.False);
				Assert.That(blockedWaiting.IsAwaitingInput, Is.True);
				Assert.That(CollectionsPreviewControl.CanResumePreparation(blockedPreparing), Is.False);
				Assert.That(CollectionsPreviewControl.CanResumePreparation(blockedWaiting), Is.False);
				Assert.That(CollectionsPreviewControl.CanResumePreparation(null), Is.False);
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
				Assert.That(CollectionsPreviewControl.CanResumePreparation(ready), Is.True,
					"Verified input still needs the input-paused workflow to resume and revalidate.");
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
				Assert.That(CollectionsPreviewControl.CanResumePreparation(result.AcquisitionBatch), Is.False,
					"Preparing is not the durable AwaitingInput boundary and must not offer Check downloads and continue.");
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
				CollectionMemberAcquisitionBatch waiting = fixture.Acquisition.Begin(planBuild,
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);

				CollectionMemberAcquisitionBatch reconciled = fixture.Acquisition.ReconcileRestart(
					waiting.PlanBuild, CancellationToken.None);

				Assert.That(waiting.ArchiveOverwritePolicy, Is.EqualTo(CollectionArchiveOverwritePolicy.OverwriteExistingArchives));
				Assert.That(reconciled.ArchiveOverwritePolicy, Is.EqualTo(CollectionArchiveOverwritePolicy.Prompt),
					"Automatic archive replacement consent must not be inferred from a previous application session.");
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

		/// <summary>Automatic retries are bounded and never reverse a user's cancellation.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void ProbeCompletedInput_AutomaticFailureRetriesAreBoundedAndCancellationIsPreserved(bool cancelled)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true);
				CollectionAdditivePlanBuildResult plan = fixture.Builder.Build(fixture.EffectiveSelection, fixture.Target,
					CreateState(fixture.Target, 0), null, CollectionExecutionPolicy.InstallIntoCurrentSetup(true, true));
				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(plan,
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);
				Guid requestId = batch.Members.Single().Request.RequestId;
				for (int attempt = 0; attempt < (cancelled ? 1 : 3); attempt++)
				{
					fixture.Queue.LastTask.Finish(cancelled ? TaskStatus.Cancelled : TaskStatus.Error);
					batch = fixture.Acquisition.ProbeCompletedInput(batch, CancellationToken.None);
					Assert.That(batch.Members.Single().Request.RequestId, Is.EqualTo(requestId));
					Assert.That(batch.PlanBuild.Plan.Identity, Is.EqualTo(plan.Plan.Identity));
				}
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(cancelled ? 1 : 3));
				Assert.That(batch.Members.Single().Disposition, Is.EqualTo(CollectionMemberAcquisitionDisposition.RestartActionRequired));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>An explicit retry queues the same exact request without rebuilding its paused operation.</summary>
		[Test]
		public void RetryMember_FailedDownloadPreservesExactRequestAndPlan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true);
				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(fixture.BuildPlan(CreateState(fixture.Target, 0)),
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);
				Assert.Throws<InvalidOperationException>(() => fixture.Acquisition.RetryMember(batch,
					batch.Members.Single().Match.Member.MemberKey, null, CancellationToken.None));
				fixture.Queue.LastTask.Finish(TaskStatus.Error);
				batch = fixture.Acquisition.ProbeCompletedInput(batch, CancellationToken.None);
				CollectionMemberAcquisitionBatch retried = fixture.Acquisition.RetryMember(batch,
					batch.Members.Single().Match.Member.MemberKey, null, CancellationToken.None);
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(2));
				Assert.That(retried.PlanBuild, Is.SameAs(batch.PlanBuild));
				Assert.That(retried.Members.Single().Request, Is.SameAs(batch.Members.Single().Request));
				Assert.That(retried.Members.Single().Disposition, Is.EqualTo(CollectionMemberAcquisitionDisposition.PremiumQueued));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>A producer error does not prevent independent downloads or later retries from being queued.</summary>
		[Test]
		public void FailedAcquisition_ContinuesQueueingRemainingMembers()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true, true);
				fixture.Queue.FailOnCall = 2;
				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(fixture.BuildPlan(CreateState(fixture.Target, 0)),
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(3));
				Assert.That(batch.Members.Count(x => x.Disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired), Is.EqualTo(1));
				foreach (TestBackgroundTask task in fixture.Queue.Tasks) task.Finish(TaskStatus.Complete);
				batch = fixture.Acquisition.ProbeCompletedInput(batch, CancellationToken.None);
				fixture.Queue.FailOnCall = 4;
				batch = fixture.Acquisition.RetryFailedMembers(batch, null, CancellationToken.None);
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(6));
				Assert.That(batch.Members.Count(x => x.Disposition == CollectionMemberAcquisitionDisposition.PremiumQueued), Is.EqualTo(2));
				Assert.That(batch.Members.Count(x => x.Disposition == CollectionMemberAcquisitionDisposition.RestartActionRequired), Is.EqualTo(1));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>The main retry action targets failed requests, excluding active and already-verified work.</summary>
		[Test]
		public void RetryableFailedMembers_ExcludeRunningAndCompletedAcquisitions()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true);
				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(fixture.BuildPlan(CreateState(fixture.Target, 0)),
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);
				Assert.IsEmpty(CollectionsPreviewControl.GetRetryableFailedAcquisitionMembers(batch));
				fixture.Queue.LastTask.Finish(TaskStatus.Error);
				batch = fixture.Acquisition.ProbeCompletedInput(batch, CancellationToken.None);
				CollectionAssert.AreEqual(new[] { batch.Members.Single().Match.Member.MemberKey },
					CollectionsPreviewControl.GetRetryableFailedAcquisitionMembers(batch));

				fixture.ArchiveSource.Candidates = new[] { fixture.ExactCandidate };
				batch = fixture.Acquisition.ProbeCompletedInput(batch, CancellationToken.None);
				Assert.IsTrue(batch.IsReady);
				Assert.IsEmpty(CollectionsPreviewControl.GetRetryableFailedAcquisitionMembers(batch));
				Assert.AreEqual(1, fixture.Queue.CallCount, "A completed archive must not need another download.");
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>One blocked member does not prevent other local archives from being evaluated or falsely mark deferred acquisition as failed.</summary>
		[Test]
		public void Begin_BlockedMemberStillChecksIndependentLocalArchives()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true, true);
				fixture.ArchiveSource.Candidates = new[] { fixture.ExactCandidate };
				var first = new CollectionNativeModState(new NativeModInstanceIdentity(fixture.Target, "duplicate-a"),
					Path.Combine(root, "duplicate-a.zip"), "duplicate-a.zip", "1000", "2000", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
				var second = new CollectionNativeModState(new NativeModInstanceIdentity(fixture.Target, "duplicate-b"),
					Path.Combine(root, "duplicate-b.zip"), "duplicate-b.zip", "1000", "2000", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
				CollectionAdditivePlanBuildResult plan = fixture.BuildPlan(CreateState(fixture.Target, 0, new[] { first, second }));
				CollectionMemberAcquisitionBatch batch = fixture.Acquisition.Begin(plan, null, CancellationToken.None);
				Assert.That(batch.HasBlockedMembers, Is.True);
				Assert.That(batch.Members.Single(x => x.Match.Member.MemberKey.Equals(CollectionMemberKey.FromProvider("member-one"))).Disposition,
					Is.EqualTo(CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive));
				Assert.That(batch.Members.Single(x => x.Match.Member.MemberKey.Equals(CollectionMemberKey.FromProvider("member-blocked"))).Disposition,
					Is.EqualTo(CollectionMemberAcquisitionDisposition.Blocked));
				Assert.That(batch.Members.Single(x => x.Match.Member.MemberKey.Equals(CollectionMemberKey.FromProvider("member-waiting"))).Disposition,
					Is.EqualTo(CollectionMemberAcquisitionDisposition.DeferredByBlockedMember));
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(0), "Downloads remain deferred while a genuine match blocker exists.");
				Assert.That(fixture.OperationStore.GetOperation(plan.Operation.Identity).Phase, Is.EqualTo(CollectionOperationPhase.Preparing));
			}
			finally { Directory.Delete(root, true); }
		}

		private static Fixture CreateFixture(string root, bool premium, bool includeBlockedMember = false)
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
			var members = new List<NormalizedCollectionMember> { member };
			if (includeBlockedMember)
			{
				members.Add(new NormalizedCollectionMember(1,
					CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-blocked")),
					CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
					new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/1000/2000", archiveHash),
					CollectionRecipeIdentity.FromFingerprint("recipe-member-blocked"), "Blocked Member", 0));
				members.Add(new NormalizedCollectionMember(2,
					CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-waiting")),
					CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
					new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/3000/4000", CollectionContentHash.FromSha256(new string('f', 64))),
					CollectionRecipeIdentity.FromFingerprint("recipe-member-waiting"), "Waiting Member", 0));
			}
			var manifest = new NormalizedCollectionManifest(revision.Identity, source,
				CollectionManifestMemberSetCompleteness.Complete, null, members);
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

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, long deploymentSequence, CollectionNativeModState[] mods = null)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				mods ?? new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
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
			public int FailOnCall { get; set; }
			public List<TestBackgroundTask> Tasks { get; } = new List<TestBackgroundTask>();
			public TestBackgroundTask LastTask { get; private set; }
			public CollectionOperationPhase? PhaseObservedAtQueue { get; private set; }

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				if (CallCount == FailOnCall) throw new IOException("Simulated individual download failure.");
				CollectionOperation operation = _operationStore.GetIncompleteOperations().Single();
				PhaseObservedAtQueue = operation.Phase;
				LastTask = new TestBackgroundTask();
				Tasks.Add(LastTask);
				return LastTask;
			}
		}

		private sealed class TestBackgroundTask : BackgroundTask
		{
			/// <summary>Signals a terminal producer result to the acquisition consumer.</summary>
			public void Finish(TaskStatus status)
			{
				Status = status;
				OnTaskEnded((object)null);
			}
		}
	}
}
