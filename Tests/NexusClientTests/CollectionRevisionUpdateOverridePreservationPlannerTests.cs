using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionRevisionUpdateOverridePreservationPlannerTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void Plan_AdditionalManagedContent_IsPreservedWithoutCandidateMutation()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "200", "recipe-a"));
			UserOverride value = Override(f.Association, null, CollectionRequirementAspect.AdditionalManagedContent,
				"native:standalone", CollectionRequirementState.Absent(), CollectionRequirementState.Present("content-v1", "kept"));
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.Plan(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.IsQualified, Is.True);
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.PreserveAdditionalManagedContent));
		}

		[Test]
		public void Plan_OmittedRetainedMember_SuppressesCandidateMutation()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "201", "recipe-b"));
			UserOverride value = Override(f.Association, CollectionMemberKey.FromProvider("member-a"), CollectionRequirementAspect.MemberParticipation,
				null, CollectionRequirementState.Present("participation-v1", "included"), CollectionRequirementState.Absent());
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.MembersWhoseCandidateMutationIsSuppressed.Single(), Is.EqualTo(CollectionMemberKey.FromProvider("member-a")));
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.PreserveExistingNativeState));
		}

		[Test]
		public void Plan_ArtifactOverride_RemainsActionRequiredWithoutTypedCandidateBaselineRebase()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "201", "recipe-b"));
			UserOverride value = Override(f.Association, CollectionMemberKey.FromProvider("member-a"), CollectionRequirementAspect.ArtifactSelection,
				null, CollectionRequirementState.Present("artifact-v1", "old"), CollectionRequirementState.Present("artifact-v1", "local"));
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.Plan(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.IsQualified, Is.False);
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.ActionRequired));
			Assert.Throws<InvalidOperationException>(() => new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update));
		}

		[Test]
		public void Plan_SubjectOverrideWithoutExactCandidateEffect_RemainsActionRequired()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "201", "recipe-b"));
			UserOverride value = Override(f.Association, null, CollectionRequirementAspect.FileWinner, "Data:textures\\shared.dds",
				CollectionRequirementState.Present("winner-v1", "old"), CollectionRequirementState.Present("winner-v1", "local"));
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.Plan(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.IsQualified, Is.False);
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.ActionRequired));
			Assert.Throws<InvalidOperationException>(() => new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update));
		}

		[Test]
		public void Plan_FileWinnerWithExactCandidateEffect_RemainsActionRequiredWithoutTypedStateAdapter()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "201", "recipe-b"));
			UserOverride value = Override(f.Association, null, CollectionRequirementAspect.FileWinner, "Data:textures\\shared.dds",
				CollectionRequirementState.Present("winner-v1", "old"), CollectionRequirementState.Present("winner-v1", "local"));
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value }, new[]
			{
				new CollectionRevisionUpdateEffectPlan(CollectionMemberKey.FromProvider("member-a"), CollectionRevisionUpdateEffectKind.File,
					"Data:textures\\shared.dds", CollectionRevisionUpdateEffectChangeKind.Changed)
			});

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.Plan(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.RequiresPostCandidateReapply, Is.False);
			Assert.That(result.IsQualified, Is.False);
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.ActionRequired));
			Assert.Throws<InvalidOperationException>(() => new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update));
		}

		[Test]
		public void Plan_CanonicalMemberEnabledOverride_RequiresTypedPostCandidateReplay()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "201", "recipe-b"));
			UserOverride value = Override(f.Association, CollectionMemberKey.FromProvider("member-a"), CollectionRequirementAspect.MemberEnabledState, null,
				CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Present("bool-v1", "disabled"));
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.RequiresPostCandidateReapply, Is.True);
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.ReapplyAfterCandidateExecution));
		}

		[Test]
		public void Plan_NonCanonicalMemberEnabledOverride_RemainsActionRequired()
		{
			Fixture f = CreateFixture(CreateMember("member-a", "100", "200", "recipe-a"), CreateMember("member-a", "100", "201", "recipe-b"));
			UserOverride value = Override(f.Association, CollectionMemberKey.FromProvider("member-a"), CollectionRequirementAspect.MemberEnabledState, null,
				CollectionRequirementState.Present("bool-v1", "enabled"), CollectionRequirementState.Present("custom-v1", "disabled"));
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });

			CollectionRevisionUpdateOverridePreservationPlan result = new CollectionRevisionUpdateOverridePreservationPlanner()
				.Plan(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.That(result.IsQualified, Is.False);
			Assert.That(result.Actions.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateOverridePreservationDisposition.ActionRequired));
		}

		private static UserOverride Override(CollectionTargetAssociation association, CollectionMemberKey memberKey,
			CollectionRequirementAspect aspect, string subject, CollectionRequirementState baseline, CollectionRequirementState chosen)
		{
			return new UserOverride(Guid.NewGuid(), new CollectionRequirementReference(association, memberKey, aspect, subject), baseline, chosen, "preserve");
		}

		private static Fixture CreateFixture(NormalizedCollectionMember oldMember, NormalizedCollectionMember newMember)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-5");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-5");
			CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-old", 1);
			CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-new", 2);
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
			var native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), "C:\\Mods\\a.7z", "a.7z",
				"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
			var binding = new CollectionMemberBinding(association, oldMember.IdentityResolution.Key, native.Identity,
				oldMember.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection);
			var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native },
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { association }, new[] { binding },
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			return new Fixture(association, CreatePlan(oldRevision, target, oldMember, state.Fingerprint, Sha256A),
				CreatePlan(newRevision, target, newMember, state.Fingerprint, Sha256B), state);
		}

		private static NormalizedCollectionMember CreateMember(string memberKey, string modId, string fileId, string recipe)
		{
			return new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberKey)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), memberKey);
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, string sha256)
		{
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sha256), 100, "schema-v1", "normalizer-v1");
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null,
				new[] { member });
			var resolved = new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) };
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, CollectionCapabilityReport.Create(manifest), resolved);
		}

		private sealed class Fixture
		{
			public Fixture(CollectionTargetAssociation association, ResolvedCollectionPlan oldPlan, ResolvedCollectionPlan newPlan,
				CollectionNativeStateIndex state)
			{
				Association = association; OldPlan = oldPlan; NewPlan = newPlan; State = state;
			}
			public CollectionTargetAssociation Association { get; }
			private ResolvedCollectionPlan OldPlan { get; }
			private ResolvedCollectionPlan NewPlan { get; }
			private CollectionNativeStateIndex State { get; }

			public CollectionRevisionUpdatePlan Plan(IEnumerable<UserOverride> overrides,
				IEnumerable<CollectionRevisionUpdateEffectPlan> explicitEffects = null)
			{
				CollectionRevisionUpdatePlan basePlan = new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State,
					overrides, new CollectionDriftObservation[0], new NativeModProvenance[0]);
				if (explicitEffects == null) return basePlan;
				return new CollectionRevisionUpdatePlan(basePlan.Association, basePlan.OldPlan, basePlan.NewPlan,
					basePlan.ObservedStateFingerprint, basePlan.Members, explicitEffects, basePlan.UnscopedOverrides, basePlan.UnscopedDrift);
			}
		}
	}
}
