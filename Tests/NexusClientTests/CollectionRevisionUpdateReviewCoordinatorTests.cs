using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionRevisionUpdateReviewCoordinatorTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void ReviewedIntent_ExplicitC9Override_IsApprovableAndFrozenForPreservation()
		{
			using (Fixture f = Fixture.Create("override", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b")))
			{
				UserOverride localOverride = f.CreateOverride();
				CollectionRevisionUpdatePlan plan = f.Plan(new[] { localOverride });
				CollectionRevisionUpdateReviewedIntent intent = CollectionRevisionUpdateReviewedIntent.Create(plan);

				Assert.That(plan.HasActionRequired, Is.True);
				Assert.That(plan.HasBlockingActionRequired, Is.False);
				Assert.That(plan.HasReviewableOverrides, Is.True);
				Assert.That(intent.IsApprovable, Is.True);
				Assert.That(intent.PreservedOverrideIds, Is.EqualTo(new[] { localOverride.OverrideId }));
				Assert.That(intent.Members.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.PreserveOverrideForReview));
			}
		}

		[Test]
		public void ReviewedIntent_UnacceptedDrift_RemainsBlocking()
		{
			using (Fixture f = Fixture.Create("drift", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b")))
			{
				CollectionDriftObservation drift = f.CreateDrift();
				CollectionRevisionUpdatePlan plan = f.Plan(null, new[] { drift });
				CollectionRevisionUpdateReviewedIntent intent = CollectionRevisionUpdateReviewedIntent.Create(plan);

				Assert.That(plan.HasBlockingActionRequired, Is.True);
				Assert.That(intent.IsApprovable, Is.False);
				Assert.That(intent.UnacceptedDriftIds, Is.EqualTo(new[] { drift.ObservationId }));
			}
		}

		[Test]
		public void Coordinator_PersistsExactReview_AndDurablyApprovesAfterRestart()
		{
			using (Fixture f = Fixture.Create("persist", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b")))
			{
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation created = f.Coordinator.CreateReviewedOperation(plan);

				Assert.That(created.Kind, Is.EqualTo(CollectionOperationKind.UpdateRevision));
				Assert.That(created.Phase, Is.EqualTo(CollectionOperationPhase.ReadyForReview));
				Assert.That(created.Revision, Is.EqualTo(f.NewPlan.Revision));
				Assert.That(created.PlanIdentity, Is.EqualTo(f.NewPlan.Identity));

				CollectionRevisionUpdateReviewCoordinator restarted = f.CreateRestartedCoordinator();
				CollectionRevisionUpdateReviewedIntent loaded = restarted.LoadReviewedIntent(created.Identity);
				CollectionRevisionUpdateReviewedIntent expected = CollectionRevisionUpdateReviewedIntent.Create(plan);
				Assert.That(loaded.ReviewFingerprint, Is.EqualTo(expected.ReviewFingerprint));
				Assert.That(loaded.OldRevision, Is.EqualTo(f.OldPlan.Revision));
				Assert.That(loaded.CandidateRevision, Is.EqualTo(f.NewPlan.Revision));

				CollectionOperation approved = restarted.Approve(created.Identity, f.NewPlan.Identity, plan);
				Assert.That(approved.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
				Assert.That(approved.ResultState, Is.EqualTo(CollectionOperationResultState.Pending));
				Assert.That(new CollectionsOperationStore(new CollectionsStore(f.Root)).GetOperation(created.Identity).Phase,
					Is.EqualTo(CollectionOperationPhase.ReadyToApply));
			}
		}

		[Test]
		public void Coordinator_PersistsExactCandidateArtifactSubstitutionAcrossRestart()
		{
			NormalizedCollectionMember oldMember = CreateMember("member-a", "100", "200", "recipe-a");
			NormalizedCollectionMember newMember = CreateMember("member-a", "100", "201", "recipe-b");
			var selected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
				NexusCollectionModFileArtifactIdentity.Format("skyrimspecialedition", 100, 300), null);
			CollectionResolvedArtifactChoice substitution = CollectionResolvedArtifactChoice.SupportedSubstitution(newMember.Artifact, selected,
				CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);
			using (Fixture f = Fixture.Create("artifact-choice", oldMember, newMember, null, substitution))
			{
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation created = f.Coordinator.CreateReviewedOperation(plan);
				CollectionResolvedPlanRecord record = new CollectionsResolvedPlanStore(f.Store).GetPlan(f.NewPlan.Identity);

				Assert.That(record.PayloadFormat, Is.EqualTo(CollectionRevisionUpdateReviewedIntentCodec.PayloadFormat));
				CollectionRevisionUpdateReviewedIntent loaded = f.CreateRestartedCoordinator().LoadReviewedIntent(created.Identity);
				CollectionRevisionUpdateReviewEntry member = loaded.Members.Single();
				Assert.That(member.NewArtifactChoice, Is.EqualTo(substitution));
				Assert.That(member.NewArtifactChoice.SelectedArtifact, Is.EqualTo(selected));
				Assert.That(member.OldArtifactChoice.Kind, Is.EqualTo(CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact));
			}
		}

		[Test]
		public void Codec_LegacyPayloadRejectsInjectedArtifactChoices()
		{
			NormalizedCollectionMember oldMember = CreateMember("member-a", "100", "200", "recipe-a");
			NormalizedCollectionMember newMember = CreateMember("member-a", "100", "201", "recipe-b");
			var selected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
				NexusCollectionModFileArtifactIdentity.Format("skyrimspecialedition", 100, 300), null);
			CollectionResolvedArtifactChoice substitution = CollectionResolvedArtifactChoice.SupportedSubstitution(newMember.Artifact, selected,
				CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);
			using (Fixture f = Fixture.Create("legacy-choice-injection", oldMember, newMember, null, substitution))
			{
				byte[] payload = CollectionRevisionUpdateReviewedIntentCodec.Serialize(CollectionRevisionUpdateReviewedIntent.Create(f.Plan()));
				Assert.Throws<InvalidDataException>(() => CollectionRevisionUpdateReviewedIntentCodec.Deserialize(payload,
					CollectionRevisionUpdateReviewedIntentCodec.LegacyPayloadFormat));
			}
		}

		[Test]
		public void Coordinator_ApproveRejectsRecomputedPlanWithDifferentArtifactChoice()
		{
			NormalizedCollectionMember oldMember = CreateMember("member-a", "100", "200", "recipe-a");
			NormalizedCollectionMember newMember = CreateMember("member-a", "100", "201", "recipe-b");
			var reviewedSelected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
				NexusCollectionModFileArtifactIdentity.Format("skyrimspecialedition", 100, 300), null);
			CollectionResolvedArtifactChoice reviewedChoice = CollectionResolvedArtifactChoice.SupportedSubstitution(newMember.Artifact, reviewedSelected,
				CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);
			using (Fixture f = Fixture.Create("artifact-choice-stale", oldMember, newMember, null, reviewedChoice))
			{
				CollectionRevisionUpdatePlan reviewed = f.Plan();
				CollectionOperation created = f.Coordinator.CreateReviewedOperation(reviewed);

				var recomputedSelected = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
					NexusCollectionModFileArtifactIdentity.Format("skyrimspecialedition", 100, 301), null);
				CollectionResolvedArtifactChoice recomputedChoice = CollectionResolvedArtifactChoice.SupportedSubstitution(newMember.Artifact, recomputedSelected,
					CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);
				var recomputedNewPlan = new ResolvedCollectionPlan(f.NewPlan.Identity, f.NewPlan.Target, f.NewPlan.Policy,
					f.NewPlan.CurrentStateFingerprint, f.NewPlan.CapabilityReport, new[] { new ResolvedCollectionMemberPlan(newMember, recomputedChoice) });
				CollectionRevisionUpdatePlan recomputed = new CollectionRevisionUpdatePlanner().Plan(f.Association, f.OldPlan, recomputedNewPlan, f.State,
					new UserOverride[0], new CollectionDriftObservation[0], new NativeModProvenance[0]);

				Assert.Throws<InvalidOperationException>(() => f.Coordinator.Approve(created.Identity, f.NewPlan.Identity, recomputed));
			}
		}

		[Test]
		public void Coordinator_ApproveRejectsChangedPersistedAssociationState()
		{
			using (Fixture f = Fixture.Create("association-stale", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b")))
			{
				CollectionRevisionUpdatePlan plan = f.Plan();
				CollectionOperation created = f.Coordinator.CreateReviewedOperation(plan);
				f.AssociationStore.SaveAssociation(f.Association.WithState(CollectionAssociationState.Modified));

				Assert.Throws<InvalidOperationException>(() => f.Coordinator.Approve(created.Identity, f.NewPlan.Identity, plan));
				Assert.That(f.OperationStore.GetOperation(created.Identity).Phase, Is.EqualTo(CollectionOperationPhase.ReadyForReview));
			}
		}

		[Test]
		public void Coordinator_ApproveRejectsBlockingDrift()
		{
			using (Fixture f = Fixture.Create("blocking-drift", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b")))
			{
				CollectionRevisionUpdatePlan plan = f.Plan(null, new[] { f.CreateDrift() });
				CollectionOperation created = f.Coordinator.CreateReviewedOperation(plan);

				Assert.Throws<InvalidOperationException>(() => f.Coordinator.Approve(created.Identity, f.NewPlan.Identity, plan));
				Assert.That(f.OperationStore.GetOperation(created.Identity).Phase, Is.EqualTo(CollectionOperationPhase.ReadyForReview));
			}
		}

		[Test]
		public void Coordinator_ApproveRejectsRecomputedPlanWithDifferentPreservationInputs()
		{
			using (Fixture f = Fixture.Create("stale-review", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b")))
			{
				UserOverride localOverride = f.CreateOverride();
				CollectionRevisionUpdatePlan reviewed = f.Plan(new[] { localOverride });
				CollectionOperation created = f.Coordinator.CreateReviewedOperation(reviewed);
				CollectionRevisionUpdatePlan recomputedWithoutOverride = f.Plan();

				Assert.Throws<InvalidOperationException>(() => f.Coordinator.Approve(created.Identity, f.NewPlan.Identity, recomputedWithoutOverride));
			}
		}

		private static NormalizedCollectionMember CreateMember(string memberKey, string modId, string fileId, string recipe)
		{
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberKey)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), memberKey);
		}

		private sealed class Fixture : IDisposable
		{
			private Fixture(string root, CollectionTargetAssociation association, CollectionMemberBinding binding,
				ResolvedCollectionPlan oldPlan, ResolvedCollectionPlan newPlan, CollectionNativeStateIndex state,
				CollectionsStore store, CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
				CollectionRevisionUpdateReviewCoordinator coordinator)
			{
				Root = root; Association = association; Binding = binding; OldPlan = oldPlan; NewPlan = newPlan; State = state;
				Store = store; OperationStore = operationStore; AssociationStore = associationStore; Coordinator = coordinator;
			}

			public string Root { get; }
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberBinding Binding { get; }
			public ResolvedCollectionPlan OldPlan { get; }
			public ResolvedCollectionPlan NewPlan { get; }
			public CollectionNativeStateIndex State { get; }
			public CollectionsStore Store { get; }
			public CollectionsOperationStore OperationStore { get; }
			public CollectionsAssociationStore AssociationStore { get; }
			public CollectionRevisionUpdateReviewCoordinator Coordinator { get; }

			public static Fixture Create(string suffix, NormalizedCollectionMember oldMember, NormalizedCollectionMember newMember,
				CollectionResolvedArtifactChoice oldArtifactChoice = null, CollectionResolvedArtifactChoice newArtifactChoice = null)
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c10-2-" + suffix + "-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(root);
				var store = new CollectionsStore(root); store.CreateNew();
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-2-" + suffix);
				CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-2-" + suffix);
				CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-old", 1);
				CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-new", 2);
				var catalog = new CollectionsCatalogStore(store);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C10.2 " + suffix, null, null),
					new CollectionRevision(oldRevision, "Old", null, 0));
				catalog.SaveRevision(new CollectionRevision(newRevision, "New", null, 0));

				var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
				var associationStore = new CollectionsAssociationStore(store); associationStore.SaveAssociation(association);
				CollectionNativeModState native = null; CollectionMemberBinding binding = null;
				if (oldMember != null)
				{
					native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), "C:\\Mods\\a.7z", "a.7z",
						"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
					binding = new CollectionMemberBinding(association, oldMember.IdentityResolution.Key, native.Identity,
						oldMember.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection);
					associationStore.SaveBinding(binding);
				}
				CollectionNativeStateIndex state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
					native == null ? new CollectionNativeModState[0] : new[] { native }, new CollectionNativeFileState[0],
					new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
					CollectionNativeStateCoverage.NotApplicable, new[] { association }, binding == null ? new CollectionMemberBinding[0] : new[] { binding },
					new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
				ResolvedCollectionPlan oldPlan = CreatePlan(oldRevision, target, oldMember, state.Fingerprint, Sha256A, oldArtifactChoice);
				ResolvedCollectionPlan newPlan = CreatePlan(newRevision, target, newMember, state.Fingerprint, Sha256B, newArtifactChoice);
				var operationStore = new CollectionsOperationStore(store);
				var coordinator = new CollectionRevisionUpdateReviewCoordinator(operationStore, new CollectionsResolvedPlanStore(store), associationStore);
				return new Fixture(root, association, binding, oldPlan, newPlan, state, store, operationStore, associationStore, coordinator);
			}

			public CollectionRevisionUpdatePlan Plan(IEnumerable<UserOverride> overrides = null, IEnumerable<CollectionDriftObservation> drift = null)
			{
				return new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State,
					overrides ?? new UserOverride[0], drift ?? new CollectionDriftObservation[0], new NativeModProvenance[0]);
			}

			public UserOverride CreateOverride()
			{
				var requirement = new CollectionRequirementReference(Association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.InstallerRecipe, null);
				return new UserOverride(Guid.NewGuid(), requirement, CollectionRequirementState.Present("recipe-v1", "recipe-a"),
					CollectionRequirementState.Present("recipe-v1", "local-choice"), "Preserve local recipe choice");
			}

			public CollectionDriftObservation CreateDrift()
			{
				var requirement = new CollectionRequirementReference(Association, CollectionMemberKey.FromProvider("member-a"),
					CollectionRequirementAspect.MemberEnabledState, null);
				return new CollectionDriftObservation(Guid.NewGuid(), requirement, CollectionRequirementState.Present("bool-v1", "enabled"),
					CollectionRequirementState.Present("bool-v1", "disabled"), "Disabled outside Collections");
			}

			public CollectionRevisionUpdateReviewCoordinator CreateRestartedCoordinator()
			{
				var restartedStore = new CollectionsStore(Root);
				return new CollectionRevisionUpdateReviewCoordinator(new CollectionsOperationStore(restartedStore),
					new CollectionsResolvedPlanStore(restartedStore), new CollectionsAssociationStore(restartedStore));
			}

			public void Dispose()
			{
				if (Directory.Exists(Root)) Directory.Delete(Root, true);
			}
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, string sha256,
			CollectionResolvedArtifactChoice artifactChoice = null)
		{
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sha256),
				100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember[] members = member == null ? new NormalizedCollectionMember[0] : new[] { member };
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, members);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			ResolvedCollectionMemberPlan[] resolved = member == null ? new ResolvedCollectionMemberPlan[0] :
				new[] { new ResolvedCollectionMemberPlan(member, artifactChoice ?? CollectionResolvedArtifactChoice.Exact(member.Artifact)) };
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, report, resolved);
		}
	}
}
