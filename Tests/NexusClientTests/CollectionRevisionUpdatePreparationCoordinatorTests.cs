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
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionRevisionUpdatePreparationCoordinatorTests
	{
		[Test]
		public void Begin_VerifiedCandidateArchive_PreparesChangedMemberWithoutLeavingReadyToApply()
		{
			using (Fixture f = Fixture.Create("ready", true))
			{
				f.ArchiveSource.Candidates = new[] { f.ExactCandidate };
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation operation = f.CreateAndApprove(plan);

				CollectionRevisionUpdatePreparationBatch batch = f.Preparation.Begin(operation.Identity, plan, f.State,
					CollectionArchiveOverwritePolicy.Prompt, null, CancellationToken.None);

				Assert.That(batch.IsReady, Is.True);
				Assert.That(batch.Members.Count, Is.EqualTo(1));
				Assert.That(batch.Members.Single().PreparedRecipe.PreparedNativeIdentity.Fingerprint, Is.EqualTo("prepared-c10-3"));
				Assert.That(f.RecipePreparation.CallCount, Is.EqualTo(1));
				Assert.That(f.Queue.CallCount, Is.EqualTo(0));
				Assert.That(f.OperationStore.GetOperation(operation.Identity).Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
			}
		}

		[Test]
		public void Begin_MissingPremiumArchive_PersistsAwaitingInputBeforeQueue_ThenProbeRevalidatesAndPrepares()
		{
			using (Fixture f = Fixture.Create("premium", true))
			{
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation operation = f.CreateAndApprove(plan);
				CollectionRevisionUpdatePreparationBatch waiting = f.Preparation.Begin(operation.Identity, plan, f.State,
					CollectionArchiveOverwritePolicy.OverwriteExistingArchives, null, CancellationToken.None);

				Assert.That(waiting.IsAwaitingInput, Is.True);
				Assert.That(waiting.Members.Single().Disposition, Is.EqualTo(CollectionMemberAcquisitionDisposition.PremiumQueued));
				Assert.That(f.Queue.CallCount, Is.EqualTo(1));
				Assert.That(f.Queue.PhaseObservedAtQueue, Is.EqualTo(CollectionOperationPhase.AwaitingInput));

				f.ArchiveSource.Candidates = new[] { f.ExactCandidate };
				CollectionRevisionUpdatePreparationBatch ready = f.Preparation.ProbeCompletedInput(waiting, plan, f.State, CancellationToken.None);

				Assert.That(ready.IsReady, Is.True);
				Assert.That(ready.Operation.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
				Assert.That(ready.Members.Single().VerifiedArchive, Is.Not.Null);
				Assert.That(ready.Members.Single().PreparedRecipe, Is.Not.Null);
				Assert.That(f.RecipePreparation.CallCount, Is.EqualTo(1));
			}
		}

		[Test]
		[Category("CollectionsC12FailureInjection")]
		public void ProbeCompletedInput_ChangedAssociationAfterPause_RefusesStaleApproval()
		{
			using (Fixture f = Fixture.Create("pause-stale-association", true))
			{
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation operation = f.CreateAndApprove(plan);
				CollectionRevisionUpdatePreparationBatch waiting = f.Preparation.Begin(operation.Identity, plan, f.State,
					CollectionArchiveOverwritePolicy.Prompt, null, CancellationToken.None);

				Assert.That(waiting.IsAwaitingInput, Is.True);
				f.AssociationStore.SaveAssociation(f.Association.WithState(CollectionAssociationState.Modified));
				f.ArchiveSource.Candidates = new[] { f.ExactCandidate };

				Assert.Throws<InvalidOperationException>(() => f.Preparation.ProbeCompletedInput(waiting, plan, f.State, CancellationToken.None));
				Assert.That(f.OperationStore.GetOperation(operation.Identity).Phase, Is.EqualTo(CollectionOperationPhase.AwaitingInput));
				Assert.That(f.RecipePreparation.CallCount, Is.EqualTo(0));
			}
		}

		[Test]
		public void Begin_PreparedNativeFingerprintChangedFromApprovedReview_FailsClosed()
		{
			using (Fixture f = Fixture.Create("prepared-stale", true))
			{
				f.ArchiveSource.Candidates = new[] { f.ExactCandidate };
				var oldPrepared = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity>
				{
					{ f.MemberKey, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-old") }
				};
				var newPrepared = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity>
				{
					{ f.MemberKey, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-approved") }
				};
				CollectionRevisionUpdatePlan plan = f.Plan(oldPrepared, newPrepared);
				CollectionOperation operation = f.CreateAndApprove(plan);
				f.RecipePreparation.PreparedFingerprint = "prepared-different";

				Assert.Throws<InvalidOperationException>(() => f.Preparation.Begin(operation.Identity, plan, f.State,
					CollectionArchiveOverwritePolicy.Prompt, null, CancellationToken.None));
				Assert.That(f.OperationStore.GetOperation(operation.Identity).Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
				Assert.That(f.OperationStore.GetOperation(operation.Identity).HasCrossedNativeBoundary, Is.False);
			}
		}

		[Test]
		public void Begin_UnchangedMemberWithoutEnvironmentRepreparation_DoesNotAcquireOrPrepare()
		{
			using (Fixture f = Fixture.Create("unchanged", false))
			{
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation operation = f.CreateAndApprove(plan);
				CollectionRevisionUpdatePreparationBatch batch = f.Preparation.Begin(operation.Identity, plan, f.State,
					CollectionArchiveOverwritePolicy.Prompt, null, CancellationToken.None);

				Assert.That(batch.IsReady, Is.True);
				Assert.That(batch.Members, Is.Empty);
				Assert.That(f.Queue.CallCount, Is.EqualTo(0));
				Assert.That(f.RecipePreparation.CallCount, Is.EqualTo(0));
			}
		}

		private sealed class Fixture : IDisposable
		{
			private Fixture(string root, CollectionsStore store, CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
				CollectionRevisionUpdateReviewCoordinator reviewCoordinator, CollectionRevisionUpdatePreparationCoordinator preparation,
				RecordingRecipePreparation recipePreparation, MutableArchiveSource archiveSource, RecordingQueue queue,
				CollectionTargetAssociation association, ResolvedCollectionPlan oldPlan, ResolvedCollectionPlan newPlan,
				CollectionNativeStateIndex state, CollectionMemberKey memberKey, CollectionManagedArchiveCandidate exactCandidate)
			{
				Root = root; Store = store; OperationStore = operationStore; AssociationStore = associationStore; ReviewCoordinator = reviewCoordinator;
				Preparation = preparation; RecipePreparation = recipePreparation; ArchiveSource = archiveSource; Queue = queue;
				Association = association; OldPlan = oldPlan; NewPlan = newPlan; State = state; MemberKey = memberKey; ExactCandidate = exactCandidate;
			}

			public string Root { get; }
			public CollectionsStore Store { get; }
			public CollectionsOperationStore OperationStore { get; }
			public CollectionsAssociationStore AssociationStore { get; }
			public CollectionRevisionUpdateReviewCoordinator ReviewCoordinator { get; }
			public CollectionRevisionUpdatePreparationCoordinator Preparation { get; }
			public RecordingRecipePreparation RecipePreparation { get; }
			public MutableArchiveSource ArchiveSource { get; }
			public RecordingQueue Queue { get; }
			public CollectionTargetAssociation Association { get; }
			public ResolvedCollectionPlan OldPlan { get; }
			public ResolvedCollectionPlan NewPlan { get; }
			public CollectionNativeStateIndex State { get; }
			public CollectionMemberKey MemberKey { get; }
			public CollectionManagedArchiveCandidate ExactCandidate { get; }

			public static Fixture Create(string suffix, bool changedRecipe)
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c10-3-" + suffix + "-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(root);
				var store = new CollectionsStore(root); store.CreateNew();
				var operationStore = new CollectionsOperationStore(store);
				var associationStore = new CollectionsAssociationStore(store);
				var planStore = new CollectionsResolvedPlanStore(store);
				var review = new CollectionRevisionUpdateReviewCoordinator(operationStore, planStore, associationStore);

				byte[] archiveBytes = Encoding.UTF8.GetBytes("c10.3 exact candidate archive");
				CollectionContentHash archiveHash = ComputeHash(archiveBytes);
				string archivePath = Path.Combine(root, "member.zip");
				File.WriteAllBytes(archivePath, archiveBytes);
				var archiveSource = new MutableArchiveSource();
				var artifactStore = new CollectionsRetainedArtifactStore(store);
				var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
				var acquisitionStore = new CollectionsAcquisitionStore(store);
				var adopter = new CollectionVerifiedArchiveAdopter(archiveSource, new RejectingVerifier(), artifactStore, referenceStore, acquisitionStore);
				var queue = new RecordingQueue(operationStore);
				var requestCoordinator = new CollectionAcquisitionRequestCoordinator(queue, acquisitionStore);
				var premium = new CollectionPremiumAcquisitionCoordinator(requestCoordinator, new FixedAccountProvider(true));
				var manual = new CollectionManualAcquisitionCoordinator(requestCoordinator, adopter);
				var restart = new CollectionAcquisitionRestartCoordinator(acquisitionStore, new EmptyPersistedAddModStateSource(), adopter, premium);
				var recipePreparation = new RecordingRecipePreparation();
				var preparation = new CollectionRevisionUpdatePreparationCoordinator(operationStore, review, adopter, premium, manual,
					restart, recipePreparation);

				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-sha256:" + new string('c', 64));
				CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-3-" + suffix);
				CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-old", 1);
				CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-new", 2);
				var catalog = new CollectionsCatalogStore(store);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C10.3", null, null), new CollectionRevision(oldRevision, "Old", null, 0));
				catalog.SaveRevision(new CollectionRevision(newRevision, "New", null, 0));

				CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
				NormalizedCollectionMember oldMember = CreateMember(key, archiveHash, "recipe-old");
				NormalizedCollectionMember newMember = CreateMember(key, archiveHash, changedRecipe ? "recipe-new" : "recipe-old");
				var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
				associationStore.SaveAssociation(association);
				var native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), archivePath, "member.zip",
					"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
				var binding = new CollectionMemberBinding(association, key, native.Identity, oldMember.RecipeIdentity,
					CollectionMemberBindingKind.InstalledForCollection);
				associationStore.SaveBinding(binding);
				CollectionNativeStateIndex state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native },
					new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
					new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { association }, new[] { binding },
					new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				ResolvedCollectionPlan oldPlan = CreatePlan(oldRevision, target, oldMember, state.Fingerprint, new string('a', 64));
				ResolvedCollectionPlan newPlan = CreatePlan(newRevision, target, newMember, state.Fingerprint, new string('b', 64));
				return new Fixture(root, store, operationStore, associationStore, review, preparation, recipePreparation, archiveSource, queue,
					association, oldPlan, newPlan, state, key,
					new CollectionManagedArchiveCandidate("nexus-mod-file", "skyrimspecialedition/100/200", archivePath));
			}

			public CollectionRevisionUpdatePlan Plan(
				IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> oldPrepared = null,
				IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> newPrepared = null)
			{
				return new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State,
					new UserOverride[0], new CollectionDriftObservation[0], new NativeModProvenance[0], oldPrepared, newPrepared);
			}

			public CollectionOperation CreateAndApprove(CollectionRevisionUpdatePlan plan)
			{
				CollectionOperation created = ReviewCoordinator.CreateReviewedOperation(plan);
				return ReviewCoordinator.Approve(created.Identity, NewPlan.Identity, plan);
			}

			public void Dispose()
			{
				if (Directory.Exists(Root)) Directory.Delete(Root, true);
			}
		}

		private sealed class RecordingRecipePreparation : ICollectionRevisionUpdateNativeRecipePreparationService
		{
			public int CallCount { get; private set; }
			public string PreparedFingerprint { get; set; } = "prepared-c10-3";

			public PreparedCollectionNativeRecipe Prepare(CollectionRevisionUpdatePlan updatePlan,
				CollectionRevisionUpdateMemberPlan updateMember, CollectionVerifiedArchive verifiedArchive,
				CollectionNativeStateIndex currentState, CancellationToken cancellationToken)
			{
				CallCount++;
				ResolvedCollectionMemberPlan member = updateMember.NewMember;
				var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
				var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
					ModInstallationSimpleFileRecipeAdapter.AdapterVersion, context,
					new ModInstallationRecipeExpectedContent(verifiedArchive.Artifact.ContentHash.Value, verifiedArchive.Artifact.ByteLength),
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
					new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "content\\member.txt"),
						new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "data\\member.txt") });
				var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
					new ModOperationFingerprint(updatePlan.NewPlan.Target.Fingerprint, context, member.RecipeIdentity.Fingerprint));
				ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(
					new ModInstallationRecipeInput(operation, validation),
					new ModInstallationSimpleFileRecipe(new[] { new ModInstallationSimpleFileMapping("content\\member.txt", "data\\member.txt") }));
				var preview = new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity,
					ModInstallMethod.Virtual, ModInstallRoot.Data, new CollectionPlannedFileEffect[0], new CollectionPlannedIniEffect[0],
					new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
				return new PreparedCollectionNativeRecipe(member, PreparedCollectionNativeRecipeIdentity.FromFingerprint(PreparedFingerprint),
					translated, preview, false, new[] { verifiedArchive.Artifact.ArtifactId });
			}
		}

		private static NormalizedCollectionMember CreateMember(CollectionMemberKey key, CollectionContentHash hash, string recipe)
		{
			return new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(key),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/200", hash),
				CollectionRecipeIdentity.FromFingerprint(recipe), "Member A");
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, string sourceHash)
		{
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sourceHash), 100, "schema-v1", "normalizer-v1");
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			var resolved = new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, report, new[] { resolved });
		}

		private static CollectionContentHash ComputeHash(byte[] value)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(value)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private sealed class MutableArchiveSource : ICollectionManagedArchiveSource
		{
			public IReadOnlyList<CollectionManagedArchiveCandidate> Candidates { get; set; } = new CollectionManagedArchiveCandidate[0];
			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact) { return Candidates; }
		}

		private sealed class RejectingVerifier : ICollectionArchiveIdentityVerifier
		{
			public bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive, CancellationToken cancellationToken)
			{
				throw new AssertionException("Expected SHA-256 should satisfy this fixture without provider identity verification.");
			}
		}

		private sealed class FixedAccountProvider : ICollectionPremiumAcquisitionAccountProvider
		{
			private readonly bool _premium;
			public FixedAccountProvider(bool premium) { _premium = premium; }
			public CollectionPremiumAcquisitionAccountState Capture()
			{
				return new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, _premium);
			}
		}

		private sealed class EmptyPersistedAddModStateSource : ICollectionPersistedAddModStateSource
		{
			public CollectionPersistedAddModState Find(Guid queueOperationId) { return null; }
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			private readonly CollectionsOperationStore _operations;
			public RecordingQueue(CollectionsOperationStore operations) { _operations = operations; }
			public int CallCount { get; private set; }
			public CollectionOperationPhase? PhaseObservedAtQueue { get; private set; }
			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				PhaseObservedAtQueue = _operations.GetIncompleteOperations().Single().Phase;
				return new TestBackgroundTask();
			}
		}

		private sealed class TestBackgroundTask : BackgroundTask { }
	}
}
