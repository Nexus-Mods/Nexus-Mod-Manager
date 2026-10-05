using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionRevisionUpdateCandidateExecutionPlanningTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string Sha256B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

		[Test]
		public void Build_UnchangedBoundMember_RemainsInstalledCompatible()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "200", "recipe-a", Sha256A), true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, f.State, preservation, new CollectionRevisionUpdatePreparationMemberState[0],
				new PreparedCollectionNativeRecipe[0], new CollectionMemberKey[0], CancellationToken.None);

			Assert.That(result.Matches.Members.Single().Disposition, Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
			Assert.That(result.IsReady, Is.True);
		}

		[Test]
		public void Build_ArtifactOverride_RequiresExplicitReviewInsteadOfReinterpretingOpaqueState()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true);
			UserOverride value = new UserOverride(Guid.NewGuid(),
				new CollectionRequirementReference(f.Association, f.MemberKey, CollectionRequirementAspect.ArtifactSelection, null),
				CollectionRequirementState.Present("artifact-v1", "old"), CollectionRequirementState.Present("artifact-v1", "local"), "keep local");
			CollectionRevisionUpdatePlan update = f.Plan(new[] { value });
			var planner = new CollectionRevisionUpdateOverridePreservationPlanner();

			InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
				planner.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update));

			StringAssert.Contains("opaque", error.Message);
		}

		[Test]
		public void Build_UnchangedMemberWithoutOldBinding_FailsClosed()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "200", "recipe-a", Sha256A), false);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, f.State, preservation, new CollectionRevisionUpdatePreparationMemberState[0],
				new PreparedCollectionNativeRecipe[0], new CollectionMemberKey[0], CancellationToken.None);

			Assert.That(result.Matches.Members.Single().Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Matches.Members.Single().Reason, Is.EqualTo(CollectionMemberMatchReason.MissingBoundNativeMod));
			Assert.That(result.IsReady, Is.False);
		}

		[Test]
		public void Build_ChangedBoundMember_UsesNormalReinstallDisposition()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan updateMember = update.Members.Single();
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, updateMember.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, updateMember.NewMember, archive);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(updateMember,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, f.State, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);

			Assert.That(result.Matches.Members.Single().Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(result.Matches.Members.Single().MatchedNativeMod.Identity, Is.EqualTo(f.Native.Identity));
			Assert.That(result.IsReady, Is.True);
		}


		[Test]
		public void Build_ChangedBoundMemberSharedWithAnotherCollection_FailsClosed()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true, true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan updateMember = update.Members.Single();
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, updateMember.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, updateMember.NewMember, archive);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(updateMember,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, f.State, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);

			Assert.That(result.Matches.Members.Single().Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Matches.Members.Single().Reason, Is.EqualTo(CollectionMemberMatchReason.ConflictingVerifiedRecipe));
			Assert.That(result.IsReady, Is.False);
		}

		private static CollectionVerifiedArchive CreateVerifiedArchive(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, string hash)
		{
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, member.MemberKey);
			var artifact = new CollectionsRetainedArtifact("artifact-c10-6", CollectionContentHash.FromSha256(hash), 123);
			var reference = new CollectionsRetainedArtifactReferenceRecord("reference-c10-6", artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "archive");
			return new CollectionVerifiedArchive(request, artifact, reference, CollectionVerifiedArchiveSourceKind.RetainedContent,
				CollectionArchiveVerificationBasis.ExpectedContentHash);
		}

		private static PreparedCollectionNativeRecipe CreatePrepared(ResolvedCollectionPlan plan,
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive archive)
		{
			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion, context,
				new ModInstallationRecipeExpectedContent(archive.Artifact.ContentHash.Value, archive.Artifact.ByteLength),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
				new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "content\\member.txt"),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "data\\member.txt") });
			ModOperationIdentity operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(plan.Target.Fingerprint, context, member.RecipeIdentity.Fingerprint));
			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(
				new ModInstallationRecipeInput(operation, validation),
				new ModInstallationSimpleFileRecipe(new[] { new ModInstallationSimpleFileMapping("content\\member.txt", "data\\member.txt") }));
			var preview = new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity, ModInstallMethod.Virtual,
				ModInstallRoot.Data, new CollectionPlannedFileEffect[0], new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			return new PreparedCollectionNativeRecipe(member, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-c10-6"),
				translated, preview, false, new[] { archive.Artifact.ArtifactId });
		}

		private static NormalizedCollectionMember CreateMember(string modId, string fileId, string recipe, string hash)
		{
			return new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-a")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId,
					CollectionContentHash.FromSha256(hash)), CollectionRecipeIdentity.FromFingerprint(recipe), "Member A");
		}

		private static Fixture CreateFixture(NormalizedCollectionMember oldMember, NormalizedCollectionMember newMember, bool includeBinding, bool includeSharedBinding = false)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-6");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-6");
			CollectionRevisionIdentity oldRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-old", 1);
			CollectionRevisionIdentity newRevision = CollectionRevisionIdentity.FromNexus(collection, "revision-new", 2);
			var association = new CollectionTargetAssociation(Guid.NewGuid(), oldRevision, target, CollectionAssociationState.Applied);
			var native = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-a"), "C:\\Mods\\a.7z", "a.7z",
				"100", "200", "1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
			CollectionMemberBinding binding = includeBinding ? new CollectionMemberBinding(association, oldMember.IdentityResolution.Key,
				native.Identity, oldMember.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection) : null;
			var associations = new List<CollectionTargetAssociation> { association };
			var bindings = new List<CollectionMemberBinding>();
			if (binding != null) bindings.Add(binding);
			if (includeSharedBinding)
			{
				CollectionIdentity sharedCollection = CollectionIdentity.FromNexus("collection-c10-6-shared");
				CollectionRevisionIdentity sharedRevision = CollectionRevisionIdentity.FromNexus(sharedCollection, "revision-shared", 1);
				var sharedAssociation = new CollectionTargetAssociation(Guid.NewGuid(), sharedRevision, target, CollectionAssociationState.Applied);
				associations.Add(sharedAssociation);
				bindings.Add(new CollectionMemberBinding(sharedAssociation, CollectionMemberKey.FromProvider("shared-member"), native.Identity,
					oldMember.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting));
			}
			var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], includeBinding ? new[] { native } : new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, associations, bindings, new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			return new Fixture(association, native, CreatePlan(oldRevision, target, oldMember, state.Fingerprint, Sha256A),
				CreatePlan(newRevision, target, newMember, state.Fingerprint, Sha256B), state);
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionRevisionIdentity revision, CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, string sourceHash)
		{
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(sourceHash), 100, "schema-v1", "normalizer-v1");
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, CollectionCapabilityReport.Create(manifest),
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) });
		}

		private sealed class Fixture
		{
			internal Fixture(CollectionTargetAssociation association, CollectionNativeModState native, ResolvedCollectionPlan oldPlan,
				ResolvedCollectionPlan newPlan, CollectionNativeStateIndex state)
			{
				Association = association; Native = native; OldPlan = oldPlan; NewPlan = newPlan; State = state;
			}
			internal CollectionTargetAssociation Association { get; }
			internal CollectionNativeModState Native { get; }
			internal ResolvedCollectionPlan OldPlan { get; }
			internal ResolvedCollectionPlan NewPlan { get; }
			internal CollectionNativeStateIndex State { get; }
			internal CollectionMemberKey MemberKey { get { return NewPlan.SelectedMembers.Single().MemberKey; } }
			internal CollectionRevisionUpdatePlan Plan(IEnumerable<UserOverride> overrides)
			{
				return new CollectionRevisionUpdatePlanner().Plan(Association, OldPlan, NewPlan, State, overrides,
					new CollectionDriftObservation[0], new NativeModProvenance[0]);
			}
		}
	}
}
