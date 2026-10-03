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
	public class CollectionRevisionUpdatePublicationTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void Build_CanonicalEnabledOverride_RebasesToCandidateAndKeepsModifiedState()
		{
			DomainFixture f = DomainFixture.OneMember("recipe-a", "recipe-a", CollectionAssociationState.Modified);
			UserOverride userOverride = new UserOverride(Guid.NewGuid(),
				new CollectionRequirementReference(f.Association, f.MemberKey, CollectionRequirementAspect.MemberEnabledState, null),
				CollectionMemberRequirementStates.Enabled(true), CollectionMemberRequirementStates.Enabled(false), "keep disabled");
			CollectionRevisionUpdatePlan update = f.Plan(new[] { userOverride });
			CollectionRevisionUpdateReviewedIntent reviewed = CollectionRevisionUpdateReviewedIntent.Create(update);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(reviewed, update);

			CollectionRevisionUpdatePublicationPlan result = new CollectionRevisionUpdatePublicationBuilder().Build(update, preservation,
				new Dictionary<CollectionMemberKey, NativeModInstanceIdentity>());

			Assert.That(result.CandidateAssociation.AssociationId, Is.Not.EqualTo(f.Association.AssociationId));
			Assert.That(result.CandidateAssociation.Revision, Is.EqualTo(f.NewPlan.Revision));
			Assert.That(result.CandidateAssociation.State, Is.EqualTo(CollectionAssociationState.Modified));
			Assert.That(result.Bindings.Single().Association.AssociationId, Is.EqualTo(result.CandidateAssociation.AssociationId));
			UserOverride rebased = result.Overrides.Single();
			Assert.That(rebased.OverrideId, Is.EqualTo(userOverride.OverrideId));
			Assert.That(rebased.Requirement.AssociationId, Is.EqualTo(result.CandidateAssociation.AssociationId));
			Assert.That(rebased.Requirement.BaselineRevision, Is.EqualTo(f.NewPlan.Revision));
			Assert.That(rebased.BaselineState, Is.EqualTo(CollectionMemberRequirementStates.Enabled(true)));
			Assert.That(rebased.UserChosenState, Is.EqualTo(CollectionMemberRequirementStates.Enabled(false)));
		}

		[Test]
		public void Build_OmissionSatisfiedByCandidateRemoval_DropsObsoleteOverride()
		{
			DomainFixture f = DomainFixture.RemovedMember(CollectionAssociationState.Modified);
			UserOverride userOverride = new UserOverride(Guid.NewGuid(),
				new CollectionRequirementReference(f.Association, f.MemberKey, CollectionRequirementAspect.MemberParticipation, null),
				CollectionMemberRequirementStates.Included(), CollectionRequirementState.Absent(), "omit");
			CollectionRevisionUpdatePlan update = f.Plan(new[] { userOverride });
			CollectionRevisionUpdateReviewedIntent reviewed = CollectionRevisionUpdateReviewedIntent.Create(update);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(reviewed, update);

			CollectionRevisionUpdatePublicationPlan result = new CollectionRevisionUpdatePublicationBuilder().Build(update, preservation,
				new Dictionary<CollectionMemberKey, NativeModInstanceIdentity>());

			Assert.That(result.CandidateAssociation.State, Is.EqualTo(CollectionAssociationState.Applied));
			Assert.That(result.Bindings, Is.Empty);
			Assert.That(result.Overrides, Is.Empty);
		}

		[Test]
		public void FinalizeRevisionUpdateAssociation_ExactOldBaseline_CommitsCandidateAndJournalAtomically()
		{
			using (StoreFixture f = StoreFixture.Create("atomic", false))
			{
				CollectionRevisionUpdatePublicationPlan publication = f.BuildPublication();
				CollectionOperation committed = f.CommittedOperation();

				f.AssociationStore.FinalizeRevisionUpdateAssociation(f.Reviewed, f.Verification,
					publication.CandidateAssociation, publication.Bindings, f.ExpectedOldOverrides,
					publication.Overrides, committed);

				Assert.That(f.AssociationStore.GetAssociation(f.OldAssociation.AssociationId), Is.Null);
				CollectionTargetAssociation published = f.AssociationStore.GetAssociation(publication.CandidateAssociation.AssociationId);
				Assert.That(published, Is.Not.Null);
				Assert.That(published.Revision, Is.EqualTo(f.NewPlan.Revision));
				Assert.That(f.OperationStore.GetOperation(f.Operation.Identity).IsSuccessful, Is.True);
			}
		}

		[Test]
		public void FinalizeRevisionUpdateAssociation_ChangedOldOverride_RollsBackCandidateAndTerminalCheckpoint()
		{
			using (StoreFixture f = StoreFixture.Create("stale-override", true))
			{
				CollectionRevisionUpdatePublicationPlan publication = f.BuildPublication();
				UserOverride expected = f.ExpectedOldOverrides.Single();
				UserOverride changed = new UserOverride(expected.OverrideId, expected.Requirement, expected.BaselineState,
					CollectionRequirementState.Present("content-v1", "changed"), expected.Note);
				f.AssociationStore.SaveOverride(changed);

				Assert.Throws<InvalidOperationException>(() => f.AssociationStore.FinalizeRevisionUpdateAssociation(
					f.Reviewed, f.Verification, publication.CandidateAssociation, publication.Bindings,
					f.ExpectedOldOverrides, publication.Overrides, f.CommittedOperation()));

				Assert.That(f.AssociationStore.GetAssociation(f.OldAssociation.AssociationId), Is.Not.Null);
				Assert.That(f.AssociationStore.GetAssociation(publication.CandidateAssociation.AssociationId), Is.Null);
				CollectionOperation persisted = f.OperationStore.GetOperation(f.Operation.Identity);
				Assert.That(persisted.IsSuccessful, Is.False);
				Assert.That(persisted.Phase, Is.EqualTo(CollectionOperationPhase.CandidateRevisionAggregateVerified));
			}
		}

		private sealed class DomainFixture
		{
			private DomainFixture(CollectionTargetAssociation association, CollectionMemberKey memberKey,
				CollectionNativeModState native, CollectionNativeStateIndex state, ResolvedCollectionPlan oldPlan,
				ResolvedCollectionPlan newPlan)
			{
				Association = association; MemberKey = memberKey; Native = native; State = state; OldPlan = oldPlan; NewPlan = newPlan;
			}
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberKey MemberKey { get; }
			public CollectionNativeModState Native { get; }
			public CollectionNativeStateIndex State { get; }
			public ResolvedCollectionPlan OldPlan { get; }
			public ResolvedCollectionPlan NewPlan { get; }

			public static DomainFixture OneMember(string oldRecipe, string newRecipe, CollectionAssociationState stateKind)
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-9-domain");
				CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-9-domain");
				CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "old", 1);
				CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "new", 2);
				CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
				NormalizedCollectionMember oldMember = Member(key, oldRecipe, "200");
				NormalizedCollectionMember newMember = Member(key, newRecipe, oldRecipe == newRecipe ? "200" : "201");
				var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, stateKind);
				var native = Native(target);
				var binding = new CollectionMemberBinding(association, key, native.Identity, oldMember.RecipeIdentity,
					CollectionMemberBindingKind.InstalledForCollection);
				var index = Index(target, association, native, binding);
				return new DomainFixture(association, key, native, index,
					CollectionRevisionUpdatePublicationTests.Plan(oldRevision, target, index.Fingerprint, oldMember, Sha256A),
					CollectionRevisionUpdatePublicationTests.Plan(newRevision, target, index.Fingerprint, newMember, Sha256B));
			}

			public static DomainFixture RemovedMember(CollectionAssociationState stateKind)
			{
				DomainFixture baseFixture = OneMember("recipe-a", "recipe-a", stateKind);
				ResolvedCollectionPlan emptyNew = CollectionRevisionUpdatePublicationTests.Plan(baseFixture.NewPlan.Revision, baseFixture.NewPlan.Target,
					baseFixture.State.Fingerprint, null, Sha256B);
				return new DomainFixture(baseFixture.Association, baseFixture.MemberKey, baseFixture.Native,
					baseFixture.State, baseFixture.OldPlan, emptyNew);
			}

			public CollectionRevisionUpdatePlan Plan(IEnumerable<UserOverride> overrides)
			{
				return new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State,
					overrides, new CollectionDriftObservation[0], new[] { new NativeModProvenance(Native.Identity, StandaloneModUse.NoStandaloneUseVerified) });
			}
		}

		private sealed class StoreFixture : IDisposable
		{
			private StoreFixture(string root, CollectionsStore store, CollectionTargetAssociation oldAssociation,
				CollectionRevisionUpdatePlan update, CollectionRevisionUpdateReviewedIntent reviewed,
				CollectionRevisionUpdateOverridePreservationPlan preservation, CollectionOperation operation,
				CollectionRevisionUpdateAggregateVerificationRecord verification, List<UserOverride> expectedOldOverrides)
			{
				Root = root; Store = store; OldAssociation = oldAssociation; Update = update; Reviewed = reviewed;
				Preservation = preservation; Operation = operation; Verification = verification; ExpectedOldOverrides = expectedOldOverrides;
				AssociationStore = new CollectionsAssociationStore(store);
				OperationStore = new CollectionsOperationStore(store);
			}
			public string Root { get; }
			public CollectionsStore Store { get; }
			public CollectionsAssociationStore AssociationStore { get; }
			public CollectionsOperationStore OperationStore { get; }
			public CollectionTargetAssociation OldAssociation { get; }
			public CollectionRevisionUpdatePlan Update { get; }
			public ResolvedCollectionPlan NewPlan { get { return Update.NewPlan; } }
			public CollectionRevisionUpdateReviewedIntent Reviewed { get; }
			public CollectionRevisionUpdateOverridePreservationPlan Preservation { get; }
			public CollectionOperation Operation { get; }
			public CollectionRevisionUpdateAggregateVerificationRecord Verification { get; }
			public List<UserOverride> ExpectedOldOverrides { get; }

			public static StoreFixture Create(string suffix, bool withOverride)
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c10-9-" + suffix + "-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(root);
				var store = new CollectionsStore(root);
				store.CreateNew();
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-9-" + suffix);
				CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-9-" + suffix);
				CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "old", 1);
				CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "new", 2);
				var catalog = new CollectionsCatalogStore(store);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C10.9", null, null),
					new CollectionRevision(oldRevision, "old", null, 0));
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C10.9", null, null),
					new CollectionRevision(newRevision, "new", null, 0));
				CollectionAssociationState associationState = withOverride ? CollectionAssociationState.Modified : CollectionAssociationState.Applied;
				var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, associationState);
				var associationStore = new CollectionsAssociationStore(store);
				associationStore.SaveAssociation(association);
				var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new CollectionNativeModState[0],
					new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
					new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { association },
					new CollectionMemberBinding[0], new UserOverride[0], CollectionNativeStateCoverage.Complete,
					new CollectionNativeStateIssue[0], 0);
				ResolvedCollectionPlan oldPlan = Plan(oldRevision, target, state.Fingerprint, null, Sha256A);
				ResolvedCollectionPlan newPlan = Plan(newRevision, target, state.Fingerprint, null, Sha256B);
				var oldOverrides = new List<UserOverride>();
				if (withOverride)
				{
					var value = new UserOverride(Guid.NewGuid(), new CollectionRequirementReference(association, null,
						CollectionRequirementAspect.AdditionalManagedContent, "native:standalone"), CollectionRequirementState.Absent(),
						CollectionRequirementState.Present("content-v1", "kept"), "preserve");
					associationStore.SaveOverride(value);
					oldOverrides.Add(value);
				}
				CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(association, oldPlan, newPlan, state,
					oldOverrides, new CollectionDriftObservation[0], new NativeModProvenance[0]);
				CollectionRevisionUpdateReviewedIntent reviewed = CollectionRevisionUpdateReviewedIntent.Create(update);
				CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
					.RequireQualified(reviewed, update);
				var planStore = new CollectionsResolvedPlanStore(store);
				planStore.SavePlan(oldPlan, "test-plan/1", new byte[] { 1 });
				planStore.SavePlan(newPlan, "test-plan/1", new byte[] { 2 });
				var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
					collection, target, newRevision, newPlan.Identity, 7, CollectionOperationPhase.CandidateRevisionAggregateVerified,
					CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				var operationStore = new CollectionsOperationStore(store);
				operationStore.SaveOperation(operation);
				var verification = new CollectionRevisionUpdateAggregateVerificationRecord(operation.Identity.OperationId,
					newPlan.Identity, reviewed.ReviewFingerprint, state.Fingerprint);
				return new StoreFixture(root, store, association, update, reviewed, preservation, operation, verification, oldOverrides);
			}

			public CollectionRevisionUpdatePublicationPlan BuildPublication()
			{
				return new CollectionRevisionUpdatePublicationBuilder().Build(Update, Preservation,
					new Dictionary<CollectionMemberKey, NativeModInstanceIdentity>());
			}

			public CollectionOperation CommittedOperation()
			{
				return new CollectionOperation(Operation.Identity, Operation.Kind, Operation.Collection, Operation.Target,
					Operation.Revision, Operation.PlanIdentity, Operation.CheckpointSequence + 1, CollectionOperationPhase.Completed,
					CollectionOperationResultState.Committed, Operation.NativeChildren);
			}

			public void Dispose()
			{
				try { Directory.Delete(Root, true); } catch { }
			}
		}

		private static NormalizedCollectionMember Member(CollectionMemberKey key, string recipe, string fileId)
		{
			return new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(key),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), key.Value);
		}

		private static CollectionNativeModState Native(CollectionTargetIdentity target)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), "C:\\Mods\\a.7z", "a.7z",
				"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static CollectionNativeStateIndex Index(CollectionTargetIdentity target, CollectionTargetAssociation association,
			CollectionNativeModState native, CollectionMemberBinding binding)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native },
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { association }, new[] { binding },
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static ResolvedCollectionPlan Plan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			CollectionCurrentStateFingerprint fingerprint, NormalizedCollectionMember member, string sha256)
		{
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sha256), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember[] members = member == null ? new NormalizedCollectionMember[0] : new[] { member };
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, members);
			ResolvedCollectionMemberPlan[] resolved = member == null ? new ResolvedCollectionMemberPlan[0] :
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) };
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, CollectionCapabilityReport.Create(manifest), resolved);
		}
	}
}
