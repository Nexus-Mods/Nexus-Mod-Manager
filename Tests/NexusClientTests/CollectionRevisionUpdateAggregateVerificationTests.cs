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
	public class CollectionRevisionUpdateAggregateVerificationTests
	{
		[Test]
		public void Verify_UnchangedBoundMember_IsAcceptedWithoutCandidateChild()
		{
			Fixture f = CreateFixture("recipe-a", "recipe-a", true, new CollectionNativeFileState[0]);
			CollectionRevisionUpdatePlan update = f.Plan(new CollectionRevisionUpdateEffectPlan[0]);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.DoesNotThrow(() => new CollectionRevisionUpdateAggregateVerifier().Verify(update, preservation,
				f.Operation(update), f.State, new Dictionary<CollectionMemberKey, CollectionNativeModState>()));
		}

		[Test]
		public void Verify_ChangedMemberWithoutVerifiedCandidateNative_FailsClosed()
		{
			Fixture f = CreateFixture("recipe-a", "recipe-b", true, new CollectionNativeFileState[0]);
			CollectionRevisionUpdatePlan update = f.Plan(new CollectionRevisionUpdateEffectPlan[0]);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			Assert.Throws<InvalidOperationException>(() => new CollectionRevisionUpdateAggregateVerifier().Verify(update, preservation,
				f.Operation(update), f.State, new Dictionary<CollectionMemberKey, CollectionNativeModState>()));
		}

		[Test]
		public void Verify_RemovedFileEffectStillOwnedByUpdatedMember_FailsClosed()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-8");
			ModDeploymentTarget fileTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "obsolete\\file.txt");
			var owner = new CollectionNativeOwnerState("native-a", null, CollectionNativeOwnerKind.NativeMod, true, 0, String.Empty);
			var file = new CollectionNativeFileState(fileTarget, "C:\\Game\\Data\\obsolete\\file.txt", false, true, true,
				"native-a", new[] { owner }, new CollectionNativeOwnerState[0], new[] { owner });
			Fixture f = CreateFixture("recipe-a", "recipe-b", true, new[] { file }, target);
			CollectionRevisionUpdateEffectPlan removed = new CollectionRevisionUpdateEffectPlan(f.MemberKey,
				CollectionRevisionUpdateEffectKind.File, "file:" + (int)ModDeploymentRoot.Data + ":obsolete\\file.txt",
				CollectionRevisionUpdateEffectChangeKind.Removed);
			CollectionRevisionUpdatePlan update = f.Plan(new[] { removed });
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			var verified = new Dictionary<CollectionMemberKey, CollectionNativeModState> { { f.MemberKey, f.Native } };

			Assert.Throws<InvalidOperationException>(() => new CollectionRevisionUpdateAggregateVerifier().Verify(update, preservation,
				f.Operation(update), f.State, verified));
		}

		/// <summary>A removed independently used member must remain present at final verification.</summary>
		[Test]
		public void Verify_RemovedIndependentInstallation_MustRemainPresent()
		{
			Fixture f = CreateFixture("recipe-a", null, true, new CollectionNativeFileState[0]);
			CollectionRevisionUpdatePlan update = f.Plan(new CollectionRevisionUpdateEffectPlan[0],
				new[] { new NativeModProvenance(f.Native.Identity, StandaloneModUse.ExplicitStandaloneUse) });
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			var verified = new Dictionary<CollectionMemberKey, CollectionNativeModState>();
			Assert.DoesNotThrow(() => new CollectionRevisionUpdateAggregateVerifier().Verify(update, preservation,
				f.Operation(update), f.State, verified));

			CollectionNativeStateIndex missing = new CollectionNativeStateIndex(f.State.Target, new CollectionNativeRootState[0],
				new CollectionNativeModState[0], new CollectionNativeFileState[0], new CollectionNativeIniState[0],
				new CollectionNativeGameValueState[0], new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				new[] { f.Association }, update.Members.Select(x => x.Binding), new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			Assert.Throws<InvalidOperationException>(() => new CollectionRevisionUpdateAggregateVerifier().Verify(update, preservation,
				f.Operation(update), missing, verified));
		}

		private static Fixture CreateFixture(string oldRecipe, string newRecipe, bool includeBinding,
			IEnumerable<CollectionNativeFileState> files, CollectionTargetIdentity target = null)
		{
			target = target ?? CollectionTargetIdentity.FromFingerprint("target-c10-8");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-8");
			CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "old", 1);
			CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "new", 2);
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
			NormalizedCollectionMember oldMember = CreateMember(key, oldRecipe, "200");
			NormalizedCollectionMember newMember = newRecipe == null ? null : CreateMember(key, newRecipe, oldRecipe == newRecipe ? "200" : "201");
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
			var native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), "C:\\Mods\\a.7z", "a.7z",
				"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
			CollectionMemberBinding binding = includeBinding ? new CollectionMemberBinding(association, key, native.Identity,
				oldMember.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection) : null;
			var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new[] { native }, files,
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, new[] { association }, binding == null ? new CollectionMemberBinding[0] : new[] { binding },
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			ResolvedCollectionPlan oldPlan = CreatePlan(oldRevision, target, oldMember, state.Fingerprint);
			ResolvedCollectionPlan newPlan = CreatePlan(newRevision, target, newMember, state.Fingerprint);
			return new Fixture(association, key, native, state, oldPlan, newPlan);
		}

		private static NormalizedCollectionMember CreateMember(CollectionMemberKey key, string recipe, string fileId)
		{
			return new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(key),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), key.Value);
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint)
		{
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(
				"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"), 100, "schema-v1", "normalizer-v1");
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null,
				member == null ? new NormalizedCollectionMember[0] : new[] { member });
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, CollectionCapabilityReport.Create(manifest),
				member == null ? new ResolvedCollectionMemberPlan[0] :
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) });
		}

		private sealed class Fixture
		{
			public Fixture(CollectionTargetAssociation association, CollectionMemberKey memberKey, CollectionNativeModState native,
				CollectionNativeStateIndex state, ResolvedCollectionPlan oldPlan, ResolvedCollectionPlan newPlan)
			{
				Association = association; MemberKey = memberKey; Native = native; State = state; OldPlan = oldPlan; NewPlan = newPlan;
			}
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberKey MemberKey { get; }
			public CollectionNativeModState Native { get; }
			public CollectionNativeStateIndex State { get; }
			private ResolvedCollectionPlan OldPlan { get; }
			private ResolvedCollectionPlan NewPlan { get; }

			public CollectionRevisionUpdatePlan Plan(IEnumerable<CollectionRevisionUpdateEffectPlan> effects, IEnumerable<NativeModProvenance> provenance = null)
			{
				CollectionRevisionUpdatePlan basePlan = new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State,
					new UserOverride[0], new CollectionDriftObservation[0], provenance ?? new NativeModProvenance[0]);
				return new CollectionRevisionUpdatePlan(basePlan.Association, basePlan.OldPlan, basePlan.NewPlan, basePlan.ObservedStateFingerprint,
					basePlan.Members, effects, basePlan.UnscopedOverrides, basePlan.UnscopedDrift);
			}

			public CollectionOperation Operation(CollectionRevisionUpdatePlan update)
			{
				return new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
					update.NewPlan.Revision.Collection, update.NewPlan.Target, update.NewPlan.Revision, update.NewPlan.Identity, 1,
					CollectionOperationPhase.QualifiedRevisionOverridesVerified, CollectionOperationResultState.Pending,
					new CollectionNativeChildOperation[0]);
			}
		}
	}
}
