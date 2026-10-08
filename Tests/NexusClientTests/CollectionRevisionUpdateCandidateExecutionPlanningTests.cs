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
		public void CanReusePreparedRecipesAtBoundary_RequiresExactPreparationStateFingerprint()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				f.Association.Revision.Collection, f.State.Target, update.NewPlan.Revision, update.NewPlan.Identity, 0,
				CollectionOperationPhase.ReadyToApply, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			var batch = new CollectionRevisionUpdatePreparationBatch(operation, CollectionRevisionUpdateReviewedIntent.Create(update), update,
				new CollectionRevisionUpdatePreparationMemberState[0], CollectionArchiveOverwritePolicy.Prompt, f.State.Fingerprint);

			Assert.That(CollectionRevisionUpdateCandidateExecutionCoordinator.CanReusePreparedRecipesAtBoundary(batch, f.State), Is.True);

			ModDeploymentTarget changedTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\changed.dds");
			CollectionNativeStateIndex changed = CreateStateWithFileOwner(f, changedTarget, f.Native.Identity.NativeModKey);
			Assert.That(CollectionRevisionUpdateCandidateExecutionCoordinator.CanReusePreparedRecipesAtBoundary(batch, changed), Is.False);
		}

		/// <summary>Fresh preparation with another native attempt keeps consent when every approved effect is unchanged.</summary>
		[Test]
		public void PhaseBoundary_UnchangedOutputAcceptsFreshNativeAttempt()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateMemberPlan member = update.Members.Single();
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, member.NewMember, Sha256B);
			PreparedCollectionNativeRecipe template = CreatePrepared(update.NewPlan, member.NewMember, archive);
			PreparedCollectionNativeRecipe reviewed = CreateBoundaryPreparation(template, "unchanged");
			PreparedCollectionNativeRecipe current = CreateBoundaryPreparation(template, "unchanged");

			Assert.That(current.RecipeInput.OperationIdentity.OperationId, Is.Not.EqualTo(reviewed.RecipeInput.OperationIdentity.OperationId));
			Assert.DoesNotThrow(() => CollectionRevisionUpdateCandidateExecutionCoordinator.RequireUnchangedBoundaryPreparation(reviewed, current));
		}

		/// <summary>Phase advancement never broadens consent for changed native output, bytes or configuration/plugin effects.</summary>
		[TestCase("native-identity")]
		[TestCase("file-hash")]
		[TestCase("file-length")]
		[TestCase("ini-value")]
		[TestCase("plugin-state")]
		public void PhaseBoundary_ChangedOutputRequiresMemberReview(string change)
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateMemberPlan member = update.Members.Single();
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, member.NewMember, Sha256B);
			PreparedCollectionNativeRecipe template = CreatePrepared(update.NewPlan, member.NewMember, archive);
			PreparedCollectionNativeRecipe reviewed = CreateBoundaryPreparation(template, "unchanged");
			PreparedCollectionNativeRecipe current = CreateBoundaryPreparation(template, change);

			InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
				CollectionRevisionUpdateCandidateExecutionCoordinator.RequireUnchangedBoundaryPreparation(reviewed, current));
			StringAssert.Contains("Member A", error.Message);
			StringAssert.Contains("new review", error.Message);
		}

		/// <summary>Creates a fresh native attempt with independently varied exact phase-boundary evidence.</summary>
		private static PreparedCollectionNativeRecipe CreateBoundaryPreparation(PreparedCollectionNativeRecipe source, string change)
		{
			ModOperationIdentity operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				source.RecipeInput.OperationIdentity.Fingerprint);
			ModInstallationRecipeInput input = source.RecipeInput.ForOperationIdentity(operation);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\member.dds");
			var preview = new CollectionMemberEffectPreview(source.Member.MemberKey, source.ProviderRecipeIdentity,
				source.InstallContext.Method, source.InstallContext.InstallRoot,
				new[] { new CollectionPlannedFileEffect(target, CollectionContentHash.FromSha256(change == "file-hash" ? Sha256B : Sha256A),
					change == "file-length" ? 20 : 10) },
				new[] { new CollectionPlannedIniEffect(new CollectionNativeIniKey("game.ini", "section", "key"), change == "ini-value" ? "changed" : "reviewed") },
				new CollectionPlannedGameValueEffect[0],
				new[] { CollectionPlannedPluginEffect.Activation("member.esp", change != "plugin-state") }, new CollectionEffectPreviewIssue[0]);
			return new PreparedCollectionNativeRecipe(source.Member,
				change == "native-identity" ? PreparedCollectionNativeRecipeIdentity.FromFingerprint("changed-preparation") : source.PreparedNativeIdentity,
				input, preview, source.SkipReadmeFiles, source.RetainedArtifactIds);
		}

		[Test]
		public void RebindPreparationAtBoundary_CarriesExecutionPreparationForwardForSameSessionRetry()
		{
			Fixture f = CreateFixture(CreateMember("100", "200", "recipe-a", Sha256A), CreateMember("100", "201", "recipe-b", Sha256B), true);
			CollectionRevisionUpdatePlan update = f.Plan(new UserOverride[0]);
			CollectionRevisionUpdateMemberPlan member = update.Members.Single();
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, member.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, member.NewMember, archive);
			var state = new CollectionRevisionUpdatePreparationMemberState(member, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
				archive.Request, archive, null, null, null, null, prepared);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				f.Association.Revision.Collection, f.State.Target, update.NewPlan.Revision, update.NewPlan.Identity, 1,
				CollectionOperationPhase.ObsoleteRevisionEffectsVerified, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
			var oldBoundary = new CollectionCurrentStateFingerprint("native-state-v1", "old-boundary");
			var newBoundary = new CollectionCurrentStateFingerprint("native-state-v1", "new-boundary");
			var batch = new CollectionRevisionUpdatePreparationBatch(operation, CollectionRevisionUpdateReviewedIntent.Create(update), update,
				new[] { state }, CollectionArchiveOverwritePolicy.Prompt, oldBoundary);
			var effective = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> { { member.MemberKey, prepared } };

			CollectionRevisionUpdatePreparationBatch rebound = CollectionRevisionUpdateCandidateExecutionCoordinator.RebindPreparationAtBoundary(
				batch, operation, newBoundary, effective);

			Assert.That(rebound.PreparedAgainstStateFingerprint, Is.EqualTo(newBoundary));
			Assert.That(rebound.Members.Single().VerifiedArchive, Is.SameAs(archive));
			Assert.That(rebound.Members.Single().PreparedRecipe, Is.SameAs(prepared));
			Assert.That(rebound.Operation.Identity, Is.EqualTo(operation.Identity));
		}

		[Test]
		public void HasDurablyPassedObsoleteBoundary_InstallingCandidateWithVerifiedDeactivation_IsTrue()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-revision-resume");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-revision-resume");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-revision-resume", 6);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("old-member");
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "old-recipe"));
			var child = new CollectionNativeChildOperation(1, new CollectionOperationMemberReference(revision, memberKey),
				CollectionNativeChildAction.Deactivate, native, CollectionNativeChildCheckpoint.Reconciled,
				new ModOperationResult(native, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				collection, target, revision, plan, 10, CollectionOperationPhase.InstallingCandidateRevisionChildren,
				CollectionOperationResultState.Pending, new[] { child });

			Assert.That(CollectionRevisionUpdateCandidateExecutionCoordinator.HasDurablyPassedObsoleteBoundary(operation), Is.True);
		}

		[Test]
		public void HasDurablyPassedObsoleteBoundary_UnreconciledDeactivation_IsFalse()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-revision-resume-block");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-revision-resume-block");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-revision-resume-block", 6);
			CollectionMemberKey memberKey = CollectionMemberKey.FromProvider("old-member");
			CollectionPlanIdentity plan = CollectionPlanIdentity.From(Guid.NewGuid(), 1);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), "old-recipe"));
			var child = new CollectionNativeChildOperation(1, new CollectionOperationMemberReference(revision, memberKey),
				CollectionNativeChildAction.Deactivate, native, CollectionNativeChildCheckpoint.NativeTerminalObserved,
				new ModOperationResult(native, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, null));
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UpdateRevision,
				collection, target, revision, plan, 10, CollectionOperationPhase.InstallingCandidateRevisionChildren,
				CollectionOperationResultState.Pending, new[] { child });

			Assert.That(CollectionRevisionUpdateCandidateExecutionCoordinator.HasDurablyPassedObsoleteBoundary(operation), Is.False);
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
		public void Build_ChangedValidatedNexusFileKey_CorrelatesReviewedOldOwnerAndAvoidsFalseFileConflict()
		{
			NormalizedCollectionMember oldMember = CreateValidatedNexusMember("100", "200", "recipe-a", Sha256A);
			NormalizedCollectionMember newMember = CreateValidatedNexusMember("100", "201", "recipe-b", Sha256B);
			Fixture f = CreateFixture(oldMember, newMember, true);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			CollectionNativeStateIndex state = CreateStateWithFileOwner(f, target, f.Native.Identity.NativeModKey);
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(f.Association, f.OldPlan, f.NewPlan, state,
				new UserOverride[0], new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(f.Native.Identity, StandaloneModUse.NoStandaloneUseVerified) });
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan candidate = update.Members.Single(x => x.NewMember != null);
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, candidate.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, candidate.NewMember, archive, target);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(candidate,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, state, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);

			CollectionMemberMatchResult match = result.Matches.Members.Single();
			Assert.That(match.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(match.MatchedNativeMod.Identity, Is.EqualTo(f.Native.Identity));
			Assert.That(result.ImpactPlan.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired), Is.False);
			Assert.That(result.IsReady, Is.True);
		}

		/// <summary>A cross-key Nexus file change must treat the correlated standalone predecessor like a normal logical update.</summary>
		[Test]
		public void Build_ChangedValidatedNexusFileKey_ExplicitStandalonePredecessorIsLogicalReinstall()
		{
			NormalizedCollectionMember oldMember = CreateValidatedNexusMember("100", "200", "recipe-a", Sha256A);
			NormalizedCollectionMember newMember = CreateValidatedNexusMember("100", "201", "recipe-b", Sha256B);
			Fixture f = CreateFixture(oldMember, newMember, true);
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(f.Association, f.OldPlan, f.NewPlan, f.State,
				new UserOverride[0], new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(f.Native.Identity, StandaloneModUse.ExplicitStandaloneUse) });
			CollectionRevisionUpdateMemberPlan predecessor = update.Members.Single(x => x.OldMember != null && x.NewMember == null);
			Assert.That(predecessor.Disposition, Is.EqualTo(CollectionRevisionUpdateDisposition.PreserveStandalone));
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan candidate = update.Members.Single(x => x.NewMember != null);
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, candidate.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, candidate.NewMember, archive);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(candidate,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, f.State, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);

			CollectionMemberMatchResult match = result.Matches.Members.Single();
			Assert.That(match.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(match.MatchedNativeMod.Identity, Is.EqualTo(f.Native.Identity));
			Assert.That(result.IsReady, Is.True);
		}

		[Test]
		public void Build_ChangedValidatedNexusFileKey_OldNativeAlreadyRemovedByObsoletePhase_InstallsFromVerifiedArchive()
		{
			NormalizedCollectionMember oldMember = CreateValidatedNexusMember("100", "200", "recipe-a", Sha256A);
			NormalizedCollectionMember newMember = CreateValidatedNexusMember("100", "201", "recipe-b", Sha256B);
			Fixture f = CreateFixture(oldMember, newMember, true);
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(f.Association, f.OldPlan, f.NewPlan, f.State,
				new UserOverride[0], new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(f.Native.Identity, StandaloneModUse.NoStandaloneUseVerified) });
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan candidate = update.Members.Single(x => x.NewMember != null);
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, candidate.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, candidate.NewMember, archive);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(candidate,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);
			var postObsoleteState = new CollectionNativeStateIndex(f.State.Target, f.State.Roots, new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], f.State.PluginCoverage, f.State.Associations.Values,
				f.State.BindingsByAssociation.Values.SelectMany(x => x), new UserOverride[0], f.State.AssociationCoverage,
				f.State.Issues, f.State.DeploymentCommitSequence);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, postObsoleteState, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);

			Assert.That(result.Matches.Members.Single().Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ArchiveOnlyReuse));
			Assert.That(result.IsReady, Is.True);
		}

		[Test]
		public void Build_ChangedValidatedNexusFileKey_UnrelatedFileOwnerStillRequiresDecision()
		{
			NormalizedCollectionMember oldMember = CreateValidatedNexusMember("100", "200", "recipe-a", Sha256A);
			NormalizedCollectionMember newMember = CreateValidatedNexusMember("100", "201", "recipe-b", Sha256B);
			Fixture f = CreateFixture(oldMember, newMember, true);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			CollectionNativeStateIndex state = CreateStateWithFileOwner(f, target, "native-unrelated");
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(f.Association, f.OldPlan, f.NewPlan, state,
				new UserOverride[0], new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(f.Native.Identity, StandaloneModUse.NoStandaloneUseVerified) });
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan candidate = update.Members.Single(x => x.NewMember != null);
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, candidate.NewMember, Sha256B);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, candidate.NewMember, archive, target);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(candidate,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, state, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);

			Assert.That(result.ImpactPlan.Status, Is.EqualTo(CollectionConflictImpactStatus.ActionRequired));
			Assert.That(result.ImpactPlan.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired), Is.True);
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

		/// <summary>A candidate member cannot reinstall a shared native instance retained for an independently used removed member.</summary>
		[Test]
		public void Build_ChangedMemberSharingPreservedIndependentNative_FailsClosed()
		{
			NormalizedCollectionMember oldMember = CreateMember("100", "200", "recipe-a", Sha256A);
			var newMember = new NormalizedCollectionMember(0, CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-b")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected, oldMember.Artifact,
				CollectionRecipeIdentity.FromFingerprint("recipe-b"), "Member B");
			Fixture f = CreateFixture(oldMember, newMember, true);
			var oldSibling = new NormalizedCollectionMember(1, newMember.IdentityResolution,
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected, oldMember.Artifact, oldMember.RecipeIdentity, "Member B");
			var siblingBinding = new CollectionMemberBinding(f.Association, oldSibling.IdentityResolution.Key, f.Native.Identity,
				oldSibling.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting);
			var state = new CollectionNativeStateIndex(f.State.Target, new CollectionNativeRootState[0], new[] { f.Native },
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { f.Association },
				f.State.BindingsByAssociation[f.Association.AssociationId].Concat(new[] { siblingBinding }), new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			var manifest = new NormalizedCollectionManifest(f.OldPlan.Revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1"),
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { oldMember, oldSibling });
			var oldPlan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), state.Target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, CollectionCapabilityReport.Create(manifest),
				new[] { new ResolvedCollectionMemberPlan(oldMember, CollectionResolvedArtifactChoice.Exact(oldMember.Artifact)),
					new ResolvedCollectionMemberPlan(oldSibling, CollectionResolvedArtifactChoice.Exact(oldSibling.Artifact)) });
			ResolvedCollectionPlan newPlan = CollectionRevisionUpdateCandidateExecutionPlanner.RebindState(f.NewPlan, state.Fingerprint);
			CollectionRevisionUpdatePlan update = new CollectionRevisionUpdatePlanner().Plan(f.Association, oldPlan, newPlan, state,
				new UserOverride[0], new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(f.Native.Identity, StandaloneModUse.ExplicitStandaloneUse) });
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(CollectionRevisionUpdateReviewedIntent.Create(update), update);
			CollectionRevisionUpdateMemberPlan added = update.Members.Single(x => x.NewMember != null);
			CollectionVerifiedArchive archive = CreateVerifiedArchive(update.NewPlan, added.NewMember, Sha256A);
			PreparedCollectionNativeRecipe prepared = CreatePrepared(update.NewPlan, added.NewMember, archive);
			var preparation = new CollectionRevisionUpdatePreparationMemberState(added,
				CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive, archive.Request, archive, null, null, null, null, prepared);

			CollectionRevisionUpdateCandidateExecutionPlanning result = new CollectionRevisionUpdateCandidateExecutionPlanner().Build(
				update, state, preservation, new[] { preparation }, new[] { prepared }, new CollectionMemberKey[0], CancellationToken.None);
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
			ResolvedCollectionMemberPlan member, CollectionVerifiedArchive archive, ModDeploymentTarget plannedFile = null)
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
				ModInstallRoot.Data, plannedFile == null ? new CollectionPlannedFileEffect[0] : new[] { new CollectionPlannedFileEffect(plannedFile) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0],
				new CollectionEffectPreviewIssue[0]);
			return new PreparedCollectionNativeRecipe(member, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-c10-6"),
				translated, preview, false, new[] { archive.Artifact.ArtifactId });
		}

		private static NormalizedCollectionMember CreateValidatedNexusMember(string modId, string fileId, string recipe, string hash)
		{
			string stableId = "skyrimspecialedition/" + modId + "/" + fileId;
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromValidatedMatch("nexus-mod-file:" + stableId)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", stableId, CollectionContentHash.FromSha256(hash)),
				CollectionRecipeIdentity.FromFingerprint(recipe), "Member A");
		}

		private static CollectionNativeStateIndex CreateStateWithFileOwner(Fixture fixture, ModDeploymentTarget target, string ownerKey)
		{
			var mods = fixture.State.Mods.Values.ToList();
			if (!mods.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Identity.NativeModKey, ownerKey)))
				mods.Add(new CollectionNativeModState(new NativeModInstanceIdentity(fixture.State.Target, ownerKey),
					"C:\\Mods\\unrelated.7z", "unrelated.7z", "999", "999", "1.0", "1.0.0.0",
					ModInstallRoot.Data, ModInstallMethod.Virtual));
			var owner = new CollectionNativeOwnerState(ownerKey, null, CollectionNativeOwnerKind.NativeMod, true, 0, null);
			var file = new CollectionNativeFileState(target, "C:\\Game\\Data\\" + target.RelativePath, false, true, true, ownerKey,
				new[] { owner }, new CollectionNativeOwnerState[0], new[] { owner });
			return new CollectionNativeStateIndex(fixture.State.Target, fixture.State.Roots, mods, new[] { file },
				fixture.State.IniEdits.Values, fixture.State.GameValues.Values, fixture.State.Plugins.Values, fixture.State.PluginCoverage,
				fixture.State.Associations.Values, fixture.State.BindingsByAssociation.Values.SelectMany(x => x), new UserOverride[0],
				fixture.State.AssociationCoverage, fixture.State.Issues, fixture.State.DeploymentCommitSequence);
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
