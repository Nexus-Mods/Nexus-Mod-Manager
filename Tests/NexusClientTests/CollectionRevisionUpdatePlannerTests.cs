using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.PluginManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionRevisionUpdatePlannerTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void Plan_UnchangedVerifiedMember_RemainsNoChange()
		{
			Fixture f = CreateFixture("unchanged", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "200", "recipe-a"));

			CollectionRevisionUpdateMemberPlan member = f.Plan().Members.Single();

			Assert.That(member.ChangeKind, Is.EqualTo(CollectionRevisionUpdateChangeKind.Unchanged));
			Assert.That(member.CurrentStateKind, Is.EqualTo(CollectionRevisionUpdateCurrentStateKind.MatchesOldBaseline));
			Assert.That(member.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.NoChange));
			Assert.That(member.RequiresExplicitReview, Is.False);
		}

		[Test]
		public void Plan_RecipeChange_FromVerifiedBaseline_FollowsCandidateAndRequiresRepreparation()
		{
			Fixture f = CreateFixture("recipe-change", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "200", "recipe-b"));

			CollectionRevisionUpdateMemberPlan member = f.Plan().Members.Single();

			Assert.That(member.ChangeKind, Is.EqualTo(CollectionRevisionUpdateChangeKind.RecipeChanged));
			Assert.That(member.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.FollowNewRevision));
			Assert.That(member.PreparationKind, Is.EqualTo(CollectionRevisionUpdatePreparationKind.ReprepareRequired));
		}

		[Test]
		public void Plan_C9Override_IsPreservedForExplicitReview()
		{
			Fixture f = CreateFixture("override", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b"));
			var requirement = new CollectionRequirementReference(f.Association, CollectionMemberKey.FromProvider("member-a"),
				CollectionRequirementAspect.InstallerRecipe, null);
			var baseline = CollectionRequirementState.Present("recipe-v1", "recipe-a");
			var chosen = CollectionRequirementState.Present("recipe-v1", "local-choice");
			var localOverride = new UserOverride(Guid.NewGuid(), requirement, baseline, chosen, "Keep my local choices");

			CollectionRevisionUpdatePlan plan = f.Plan(new[] { localOverride });
			CollectionRevisionUpdateMemberPlan member = plan.Members.Single();

			Assert.That(member.CurrentStateKind, Is.EqualTo(CollectionRevisionUpdateCurrentStateKind.IntentionalOverride));
			Assert.That(member.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.PreserveOverrideForReview));
			Assert.That(member.Overrides.Single(), Is.SameAs(localOverride));
			Assert.That(plan.HasActionRequired, Is.True);
		}

		[Test]
		public void Plan_UnacceptedDrift_NeverBecomesAutomaticUpdate()
		{
			Fixture f = CreateFixture("drift", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b"));
			var requirement = new CollectionRequirementReference(f.Association, CollectionMemberKey.FromProvider("member-a"),
				CollectionRequirementAspect.MemberEnabledState, null);
			var enabled = CollectionRequirementState.Present("bool-v1", "enabled");
			var disabled = CollectionRequirementState.Present("bool-v1", "disabled");
			var drift = new CollectionDriftObservation(Guid.NewGuid(), requirement, enabled, disabled, "Disabled outside Collections");

			CollectionRevisionUpdateMemberPlan member = f.Plan(null, new[] { drift }).Members.Single();

			Assert.That(member.CurrentStateKind, Is.EqualTo(CollectionRevisionUpdateCurrentStateKind.UnacceptedDrift));
			Assert.That(member.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.DriftRequiresReview));
		}

		[Test]
		public void Plan_PreparedNativeIdentityChange_ForSameProviderRecipe_RequiresRepreparation()
		{
			Fixture f = CreateFixture("prepared-change", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "200", "recipe-a"));
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
			var oldPrepared = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity>
			{
				{ key, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-environment-a") }
			};
			var newPrepared = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity>
			{
				{ key, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-environment-b") }
			};

			CollectionRevisionUpdatePlan plan = f.Plan(null, null, null, oldPrepared, newPrepared);

			Assert.That(plan.Members.Single().ChangeKind, Is.EqualTo(CollectionRevisionUpdateChangeKind.Unchanged));
			Assert.That(plan.Members.Single().PreparationKind, Is.EqualTo(CollectionRevisionUpdatePreparationKind.CandidateChanged));
			Assert.That(plan.PreparedNativeOutputChanged, Is.True);
			Assert.That(plan.RequiresNativeRepreparation, Is.False);
		}

		[Test]
		public void Plan_RemovedMemberWithStandaloneUse_RequiresReview()
		{
			Fixture f = CreateFixture("removed-standalone", CreateMember("member-a", "100", "200", "recipe-a"), null);
			var provenance = new NativeModProvenance(f.Binding.NativeMod, StandaloneModUse.ExplicitStandaloneUse);

			CollectionRevisionUpdateMemberPlan member = f.Plan(null, null, new[] { provenance }).Members.Single();

			Assert.That(member.ChangeKind, Is.EqualTo(CollectionRevisionUpdateChangeKind.Removed));
			Assert.That(member.StandaloneProtected, Is.True);
			Assert.That(member.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.ActionRequired));
		}

		[Test]
		public void Plan_UnscopedC9WinnerOverride_RemainsPlanLevelActionRequired()
		{
			Fixture f = CreateFixture("winner-override", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "200", "recipe-a"));
			var requirement = new CollectionRequirementReference(f.Association, null,
				CollectionRequirementAspect.FileWinner, "Data:textures\\shared.dds");
			var baseline = CollectionRequirementState.Present("winner-v1", "native-a");
			var chosen = CollectionRequirementState.Present("winner-v1", "native-local");
			var localOverride = new UserOverride(Guid.NewGuid(), requirement, baseline, chosen, "Keep winner");

			CollectionRevisionUpdatePlan plan = f.Plan(new[] { localOverride });

			Assert.That(plan.UnscopedOverrides.Single(), Is.SameAs(localOverride));
			Assert.That(plan.HasActionRequired, Is.True);
			Assert.That(plan.Members.Single().Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.NoChange));
		}

		[Test]
		public void Plan_ExactEffectPreviews_ClassifyAddedRemovedAndChangedEffects()
		{
			Fixture f = CreateFixture("effects", CreateMember("member-a", "100", "200", "recipe-a"),
				CreateMember("member-a", "100", "201", "recipe-b"));
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
			var oldPreview = new CollectionMemberEffectPreview(key, CollectionRecipeIdentity.FromFingerprint("recipe-a"),
				ModInstallMethod.Virtual, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\old.dds")) },
				new[] { new CollectionPlannedIniEffect(new CollectionNativeIniKey("game.ini", "General", "Setting"), "old") },
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var newPreview = new CollectionMemberEffectPreview(key, CollectionRecipeIdentity.FromFingerprint("recipe-b"),
				ModInstallMethod.Virtual, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\new.dds")) },
				new[] { new CollectionPlannedIniEffect(new CollectionNativeIniKey("game.ini", "General", "Setting"), "new") },
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);

			CollectionRevisionUpdatePlan plan = f.Plan(null, null, null, null, null,
				new Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> { { key, oldPreview } },
				new Dictionary<CollectionMemberKey, CollectionMemberEffectPreview> { { key, newPreview } });

			Assert.That(plan.Effects.Count(x => x.ChangeKind == CollectionRevisionUpdateEffectChangeKind.Removed), Is.EqualTo(1));
			Assert.That(plan.Effects.Count(x => x.ChangeKind == CollectionRevisionUpdateEffectChangeKind.Added), Is.EqualTo(1));
			Assert.That(plan.Effects.Count(x => x.ChangeKind == CollectionRevisionUpdateEffectChangeKind.Changed), Is.EqualTo(1));
		}

		private static Fixture CreateFixture(string suffix, NormalizedCollectionMember oldMember, NormalizedCollectionMember newMember)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-1-" + suffix);
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-" + suffix);
			CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-old", 1);
			CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-new", 2);
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
			CollectionMemberBinding binding = null;
			CollectionNativeModState native = null;
			if (oldMember != null)
			{
				native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), "C:\\Mods\\a.7z", "a.7z",
					"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
				binding = new CollectionMemberBinding(association, oldMember.IdentityResolution.Key, native.Identity,
					oldMember.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection);
			}
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				native == null ? new CollectionNativeModState[0] : new[] { native }, new CollectionNativeFileState[0],
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, new[] { association },
				binding == null ? new CollectionMemberBinding[0] : new[] { binding }, new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			ResolvedCollectionPlan oldPlan = CreatePlan(oldRevision, target, oldMember, state.Fingerprint, Sha256A);
			ResolvedCollectionPlan newPlan = CreatePlan(newRevision, target, newMember, state.Fingerprint, Sha256B);
			return new Fixture(association, binding, oldPlan, newPlan, state);
		}

		private static NormalizedCollectionMember CreateMember(string memberKey, string modId, string fileId, string recipe)
		{
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberKey)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), memberKey);
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, string sha256)
		{
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(sha256), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember[] members = member == null ? new NormalizedCollectionMember[0] : new[] { member };
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, members);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			ResolvedCollectionMemberPlan[] resolved = member == null ? new ResolvedCollectionMemberPlan[0] :
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) };
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, report, resolved);
		}

		private sealed class Fixture
		{
			public Fixture(CollectionTargetAssociation association, CollectionMemberBinding binding, ResolvedCollectionPlan oldPlan,
				ResolvedCollectionPlan newPlan, CollectionNativeStateIndex state)
			{
				Association = association;
				Binding = binding;
				OldPlan = oldPlan;
				NewPlan = newPlan;
				State = state;
			}

			public CollectionTargetAssociation Association { get; }
			public CollectionMemberBinding Binding { get; }
			public ResolvedCollectionPlan OldPlan { get; }
			public ResolvedCollectionPlan NewPlan { get; }
			public CollectionNativeStateIndex State { get; }

			public CollectionRevisionUpdatePlan Plan(IEnumerable<UserOverride> overrides = null,
				IEnumerable<CollectionDriftObservation> drift = null, IEnumerable<NativeModProvenance> provenance = null,
				IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> oldPrepared = null,
				IReadOnlyDictionary<CollectionMemberKey, PreparedCollectionNativeRecipeIdentity> newPrepared = null,
				IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> oldEffects = null,
				IReadOnlyDictionary<CollectionMemberKey, CollectionMemberEffectPreview> newEffects = null)
			{
				return new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State,
					overrides ?? new UserOverride[0], drift ?? new CollectionDriftObservation[0],
					provenance ?? new NativeModProvenance[0], oldPrepared, newPrepared, oldEffects, newEffects);
			}
		}
	}
}
