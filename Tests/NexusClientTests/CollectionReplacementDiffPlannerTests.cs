using System;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.PluginManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionReplacementDiffPlannerTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Plan_OrdinaryManagedModWithUnknownStandaloneProvenance_RequiresExplicitReview()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-unknown");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionNativeStateIndex state = CreateState(target, new[] { outgoing });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, CreateMember("member-in", "100", "200", "recipe-in"), state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);

			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);
			CollectionReplacementNativeModImpact impact = diff.NativeMods.Single(x => x.NativeMod.Identity.Equals(outgoing.Identity));

			Assert.That(diff.IncomingMembers.Single().Disposition, Is.EqualTo(CollectionReplacementDiffDisposition.IncomingOnly));
			Assert.That(impact.Disposition, Is.EqualTo(CollectionReplacementDiffDisposition.OutgoingOnly));
			Assert.That(impact.StandaloneUse, Is.EqualTo(StandaloneModUse.Unknown));
			Assert.That(impact.RemovalDecision, Is.EqualTo(CollectionReplacementRemovalDecision.RequiresExplicitReview));
			Assert.That(diff.HasUnresolvedReviewDecisions, Is.True);
			Assert.That(diff.HasBlockers, Is.False);
			Assert.That(diff.PreservesUnknownOrUnmanagedContent, Is.True);
			Assert.That(diff.IsCompletePhysicalGameDirectoryInventory, Is.False);
		}

		[Test]
		public void Plan_OutgoingModWithVerifiedNoStandaloneUse_IsEligibleOnlyForLaterReviewedRemoval()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-removable");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionNativeStateIndex state = CreateState(target, new[] { outgoing });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, CreateMember("member-in", "100", "200", "recipe-in"), state.Fingerprint);
			var provenance = new NativeModProvenance(outgoing.Identity, StandaloneModUse.NoStandaloneUseVerified);
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new[] { provenance });

			CollectionReplacementNativeModImpact impact = new CollectionReplacementDiffPlanner().Plan(incoming, current)
				.NativeMods.Single();

			Assert.That(impact.Disposition, Is.EqualTo(CollectionReplacementDiffDisposition.OutgoingOnly));
			Assert.That(impact.RemovalDecision, Is.EqualTo(CollectionReplacementRemovalDecision.EligibleForReviewedRemoval));
			Assert.That(impact.RequiresExplicitReview, Is.False);
		}

		[Test]
		public void Plan_CompatibleSharedAssociation_ProtectsTheReusedNativeInstance()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-shared");
			NormalizedCollectionMember member = CreateMember("member-in", "100", "200", "recipe-shared");
			CollectionNativeModState native = CreateNativeMod(target, "native-shared", "100", "200");
			CollectionTargetAssociation association = CreateAssociation(target, "other-collection", "other-revision");
			CollectionMemberBinding binding = new CollectionMemberBinding(association,
				CollectionMemberKey.FromProvider("other-member"), native.Identity, member.RecipeIdentity,
				CollectionMemberBindingKind.AdoptedExisting);
			CollectionNativeStateIndex state = CreateState(target, new[] { native }, null,
				new[] { association }, new[] { binding });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, member, state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);

			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);

			Assert.That(diff.IncomingMembers.Single().Disposition, Is.EqualTo(CollectionReplacementDiffDisposition.RetainedReused));
			Assert.That(diff.Associations.Single().Disposition, Is.EqualTo(CollectionReplacementAssociationDisposition.SurvivesCompatible));
			Assert.That(diff.NativeMods.Single().Disposition, Is.EqualTo(CollectionReplacementDiffDisposition.SharedProtected));
			Assert.That(diff.NativeMods.Single().RemovalDecision, Is.EqualTo(CollectionReplacementRemovalDecision.Protected));
		}

		[Test]
		public void Plan_RemovingTopOwner_ReportsWinnerChangeToProtectedSurvivingOwner()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-winner");
			NormalizedCollectionMember member = CreateMember("member-in", "100", "200", "recipe-shared");
			CollectionNativeModState retained = CreateNativeMod(target, "native-retained", "100", "200");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionTargetAssociation association = CreateAssociation(target, "other-collection", "other-revision");
			CollectionMemberBinding binding = new CollectionMemberBinding(association,
				CollectionMemberKey.FromProvider("other-member"), retained.Identity, member.RecipeIdentity,
				CollectionMemberBindingKind.AdoptedExisting);
			CollectionNativeFileState file = CreatePromotedFile("textures\\shared.dds", "native-out",
				new CollectionNativeOwnerState("native-retained", null, CollectionNativeOwnerKind.NativeMod, null, null, null),
				new CollectionNativeOwnerState("native-out", null, CollectionNativeOwnerKind.NativeMod, null, null, null));
			CollectionNativeStateIndex state = CreateState(target, new[] { retained, outgoing }, new[] { file },
				new[] { association }, new[] { binding });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, member, state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(outgoing.Identity, StandaloneModUse.NoStandaloneUseVerified) });

			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);
			CollectionReplacementEffectImpact effect = diff.Effects.Single(x => x.Kind == CollectionReplacementEffectKind.File);

			Assert.That(diff.NativeMods.Single(x => x.NativeMod.Identity.Equals(retained.Identity)).Disposition,
				Is.EqualTo(CollectionReplacementDiffDisposition.SharedProtected));
			Assert.That(effect.Disposition, Is.EqualTo(CollectionReplacementEffectDisposition.WinnerChange));
			Assert.That(effect.CurrentOwnerKey, Is.EqualTo("native-out"));
			Assert.That(effect.ProjectedOwnerKey, Is.EqualTo("native-retained"));
		}

		[Test]
		public void Plan_RecordedOriginalFallback_IsReportedWithoutClaimingManagedOwnership()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-original");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionNativeFileState file = CreatePromotedFile("textures\\original.dds", "native-out",
				new CollectionNativeOwnerState("original-values", null, CollectionNativeOwnerKind.OriginalValue, null, null, null),
				new CollectionNativeOwnerState("native-out", null, CollectionNativeOwnerKind.NativeMod, null, null, null));
			CollectionNativeStateIndex state = CreateState(target, new[] { outgoing }, new[] { file }, null, null);
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, CreateMember("member-in", "100", "200", "recipe-in"), state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(outgoing.Identity, StandaloneModUse.NoStandaloneUseVerified) });

			CollectionReplacementEffectImpact effect = new CollectionReplacementDiffPlanner().Plan(incoming, current)
				.Effects.Single(x => x.Kind == CollectionReplacementEffectKind.File);

			Assert.That(effect.Disposition, Is.EqualTo(CollectionReplacementEffectDisposition.OutgoingOnly));
			Assert.That(effect.ProjectedOwnerKey, Is.EqualTo("original-values"));
		}

		[Test]
		public void Plan_StaleNativeFingerprint_BlocksDestructiveClassification()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-stale");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionNativeStateIndex state = CreateState(target, new[] { outgoing });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, CreateMember("member-in", "100", "200", "recipe-in"),
				new CollectionCurrentStateFingerprint("native-state-index-v3", "stale"));
			var current = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);

			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);

			Assert.That(diff.CurrentStateMatchesPlan, Is.False);
			Assert.That(diff.HasBlockers, Is.True);
			Assert.That(diff.NativeMods.Single().RemovalDecision, Is.EqualTo(CollectionReplacementRemovalDecision.Blocked));
		}

		[Test]
		public void CurrentSetupDecisionFingerprint_ChangesWhenDriftChanges()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-drift-fingerprint");
			CollectionTargetAssociation association = CreateAssociation(target, "drift-collection", "drift-revision");
			CollectionNativeStateIndex state = CreateState(target, new CollectionNativeModState[0], null,
				new[] { association }, new CollectionMemberBinding[0]);
			var requirement = new CollectionRequirementReference(association, null,
				CollectionRequirementAspect.PluginState, "Example.esp");
			var drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionRequirementState.Present("plugin-v1", "enabled"), CollectionRequirementState.Absent(), null);
			var clean = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);
			var changed = new CollectionReplacementCurrentSetupSnapshot(state,
				new[] { drift }, new NativeModProvenance[0]);

			Assert.That(clean.NativeState.Fingerprint, Is.EqualTo(changed.NativeState.Fingerprint));
			Assert.That(clean.DecisionFingerprint, Is.Not.EqualTo(changed.DecisionFingerprint));
		}

		[Test]
		public void CurrentSetupDecisionFingerprint_ChangesWhenStandaloneProvenanceChanges()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-1-decision-fingerprint");
			CollectionNativeModState mod = CreateNativeMod(target, "native-a", "100", "200");
			CollectionNativeStateIndex state = CreateState(target, new[] { mod });
			var unknown = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0], new NativeModProvenance[0]);
			var verified = new CollectionReplacementCurrentSetupSnapshot(state,
				new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(mod.Identity, StandaloneModUse.NoStandaloneUseVerified) });

			Assert.That(unknown.NativeState.Fingerprint, Is.EqualTo(verified.NativeState.Fingerprint));
			Assert.That(unknown.DecisionFingerprint, Is.Not.EqualTo(verified.DecisionFingerprint));
		}

		[Test]
		public void EnvironmentProjection_RecordedOriginalFallback_RemainsProvablyVisible()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-2-original");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionNativeFileState file = CreatePromotedFile("textures\\original.dds", "native-out",
				new CollectionNativeOwnerState("original-values", null, CollectionNativeOwnerKind.OriginalValue, null, null, null),
				new CollectionNativeOwnerState("native-out", null, CollectionNativeOwnerKind.NativeMod, null, null, null));
			CollectionNativeStateIndex state = CreateState(target, new[] { outgoing }, new[] { file });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, CreateMember("member-in", "100", "200", "recipe-in"), state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(outgoing.Identity, StandaloneModUse.NoStandaloneUseVerified) });

			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);
			CollectionReplacementEnvironmentProjection projection = new CollectionReplacementEnvironmentProjector().Project(diff);
			CollectionReplacementFileEnvironmentState projected = projection.Files.Single().Value;

			Assert.That(projected.Knowledge, Is.EqualTo(CollectionReplacementEnvironmentKnowledge.Known));
			Assert.That(projected.Visible, Is.True);
			Assert.That(projected.OwnerKey, Is.EqualTo("original-values"));
		}

		[Test]
		public void EnvironmentProjection_NoProvableFallback_BlocksSupportedConditionReadiness()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-2-unknown");
			CollectionNativeModState outgoing = CreateNativeMod(target, "native-out", "900", "901");
			CollectionNativeFileState file = CreatePromotedFile("textures\\unknown.dds", "native-out",
				new CollectionNativeOwnerState("native-out", null, CollectionNativeOwnerKind.NativeMod, null, null, null));
			CollectionNativeStateIndex state = CreateState(target, new[] { outgoing }, new[] { file });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, CreateMember("member-in", "100", "200", "recipe-in"), state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(outgoing.Identity, StandaloneModUse.NoStandaloneUseVerified) });

			CollectionReplacementEnvironmentProjection projection = new CollectionReplacementEnvironmentProjector().Project(
				new CollectionReplacementDiffPlanner().Plan(incoming, current));

			Assert.That(projection.Files.Single().Value.Knowledge, Is.EqualTo(CollectionReplacementEnvironmentKnowledge.Unprovable));
			Assert.That(projection.IsReadyForSupportedConditions, Is.False);
		}

		[Test]
		public void EnvironmentProjection_RetainedPlugin_ProvidesReplacementConditionBaseline()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-2-plugin");
			NormalizedCollectionMember member = CreateMember("member-in", "100", "200", "recipe-shared");
			CollectionNativeModState native = CreateNativeMod(target, "native-shared", "100", "200");
			CollectionNativeFileState file = CreatePromotedFile("Example.esp", "native-shared",
				new CollectionNativeOwnerState("native-shared", null, CollectionNativeOwnerKind.NativeMod, null, null, null));
			var plugin = new CollectionNativePluginState("Example.esp", true, 0, null, String.Empty,
				PluginParseStatus.Parsed, PluginAddressClass.Full, PluginHeaderFlags.None, PluginSpecialFlags.None, false, 0, new string[0], new CollectionNativePluginDiagnostic[0]);
			CollectionNativeStateIndex state = CreateState(target, new[] { native }, new[] { file }, null, null,
				new[] { plugin }, CollectionNativeStateCoverage.Complete);
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, member, state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0], new NativeModProvenance[0]);

			CollectionReplacementEnvironmentProjection projection = new CollectionReplacementEnvironmentProjector().Project(
				new CollectionReplacementDiffPlanner().Plan(incoming, current));
			bool registered;
			bool active;

			Assert.That(projection.TryGetPluginRegistered("Example.esp", out registered), Is.True);
			Assert.That(registered, Is.True);
			Assert.That(projection.TryGetPluginActive("Example.esp", out active), Is.True);
			Assert.That(active, Is.True);
		}

		[Test]
		public void ReviewedIntent_BindsDecisionFingerprintAndRoundTrips()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-3-review");
			NormalizedCollectionMember member = CreateMember("member-in", "100", "200", "recipe-shared");
			CollectionNativeModState native = CreateNativeMod(target, "native-shared", "100", "200");
			CollectionTargetAssociation association = CreateAssociation(target, "other-collection", "other-revision");
			CollectionMemberBinding binding = new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("other-member"),
				native.Identity, member.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting);
			CollectionNativeStateIndex state = CreateState(target, new[] { native }, null, new[] { association }, new[] { binding });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, member, state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0], new NativeModProvenance[0]);
			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);
			CollectionReplacementEnvironmentProjection environment = new CollectionReplacementEnvironmentProjector().Project(diff);

			CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntent.Create(diff, environment,
				new[] { new CollectionReplacementNativeApproval("native-shared", CollectionReplacementNativeDecision.Protected) },
				new[] { new CollectionReplacementAssociationApproval(association.AssociationId, CollectionReplacementAssociationDecision.Preserve) },
				new CollectionReplacementPreparedRecipeApproval[0], new CollectionReplacementFileWinnerApproval[0], CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile());
			CollectionReplacementReviewedIntent loaded = CollectionReplacementReviewedIntentCodec.Deserialize(CollectionReplacementReviewedIntentCodec.Serialize(intent));

			Assert.That(loaded.PlanIdentity, Is.EqualTo(intent.PlanIdentity));
			Assert.That(loaded.NativeStateFingerprint, Is.EqualTo(intent.NativeStateFingerprint));
			Assert.That(loaded.DecisionInputFingerprint, Is.EqualTo(intent.DecisionInputFingerprint));
			Assert.That(loaded.NativeApprovals.Single().Decision, Is.EqualTo(CollectionReplacementNativeDecision.Protected));
			Assert.That(loaded.AssociationApprovals.Single().Decision, Is.EqualTo(CollectionReplacementAssociationDecision.Preserve));
		}

		[Test]
		public void ReviewedIntent_FileWinnerApprovalRoundTrips()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-6-winner");
			NormalizedCollectionMember member = CreateMember("member-in", "100", "200", "recipe-shared");
			CollectionNativeStateIndex state = CreateState(target, new CollectionNativeModState[0]);
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, member, state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0], new NativeModProvenance[0]);
			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);
			CollectionReplacementEnvironmentProjection environment = new CollectionReplacementEnvironmentProjector().Project(diff);
			ModDeploymentTarget fileTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "meshes\\winner.bin");

			var recipeApproval = new CollectionReplacementPreparedRecipeApproval(member.IdentityResolution.Key, member.RecipeIdentity.Fingerprint,
				"prepared-native-test", "effect-test", ModInstallMethod.Virtual, ModInstallRoot.Data, "test-adapter", 1);
			CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntent.Create(diff, environment,
				new CollectionReplacementNativeApproval[0], new CollectionReplacementAssociationApproval[0],
				new[] { recipeApproval },
				new[] { new CollectionReplacementFileWinnerApproval(fileTarget, member.IdentityResolution.Key) },
				CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile());
			CollectionReplacementReviewedIntent loaded = CollectionReplacementReviewedIntentCodec.Deserialize(
				CollectionReplacementReviewedIntentCodec.Serialize(intent));

			Assert.That(loaded.FileWinnerApprovals.Count, Is.EqualTo(1));
			Assert.That(loaded.FileWinnerApprovals[0].Target, Is.EqualTo(fileTarget));
			Assert.That(loaded.FileWinnerApprovals[0].WinnerMemberKey, Is.EqualTo(member.IdentityResolution.Key));
		}

		[Test]
		public void ReviewedIntent_RejectsChangedStandaloneProvenanceAfterApproval()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c8-3-stale");
			NormalizedCollectionMember member = CreateMember("member-in", "100", "200", "recipe-shared");
			CollectionNativeModState native = CreateNativeMod(target, "native-shared", "100", "200");
			CollectionTargetAssociation association = CreateAssociation(target, "other-collection", "other-revision");
			CollectionMemberBinding binding = new CollectionMemberBinding(association, CollectionMemberKey.FromProvider("other-member"),
				native.Identity, member.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting);
			CollectionNativeStateIndex state = CreateState(target, new[] { native }, null, new[] { association }, new[] { binding });
			ResolvedCollectionPlan incoming = CreateReplacementPlan(target, member, state.Fingerprint);
			var current = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0], new NativeModProvenance[0]);
			CollectionReplacementDiffPlan diff = new CollectionReplacementDiffPlanner().Plan(incoming, current);
			CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntent.Create(diff,
				new CollectionReplacementEnvironmentProjector().Project(diff),
				new[] { new CollectionReplacementNativeApproval("native-shared", CollectionReplacementNativeDecision.Protected) },
				new[] { new CollectionReplacementAssociationApproval(association.AssociationId, CollectionReplacementAssociationDecision.Preserve) },
				new CollectionReplacementPreparedRecipeApproval[0], new CollectionReplacementFileWinnerApproval[0], CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile());
			var changed = new CollectionReplacementCurrentSetupSnapshot(state, new CollectionDriftObservation[0],
				new[] { new NativeModProvenance(native.Identity, StandaloneModUse.ExplicitStandaloneUse) });

			Assert.Throws<InvalidOperationException>(() => intent.ValidateCurrentSetup(changed));
		}

		private static CollectionTargetAssociation CreateAssociation(CollectionTargetIdentity target,
			string collectionSlug, string revisionId)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus(collectionSlug), revisionId, 1);
			return new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Applied);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target,
			CollectionNativeModState[] mods, CollectionNativeFileState[] files = null,
			CollectionTargetAssociation[] associations = null, CollectionMemberBinding[] bindings = null,
			CollectionNativePluginState[] plugins = null, CollectionNativeStateCoverage pluginCoverage = CollectionNativeStateCoverage.NotApplicable)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				mods ?? new CollectionNativeModState[0], files ?? new CollectionNativeFileState[0],
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				plugins ?? new CollectionNativePluginState[0], pluginCoverage,
				associations ?? new CollectionTargetAssociation[0], bindings ?? new CollectionMemberBinding[0],
				new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static CollectionNativeFileState CreatePromotedFile(string relativePath, string currentOwner,
			params CollectionNativeOwnerState[] owners)
		{
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, relativePath);
			return new CollectionNativeFileState(target, "C:\\Game\\Data\\" + relativePath, true, true, false,
				currentOwner, owners, owners, new CollectionNativeOwnerState[0]);
		}

		private static CollectionNativeModState CreateNativeMod(CollectionTargetIdentity target, string nativeKey,
			string nexusModId, string nexusFileId)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, nativeKey),
				"C:\\Mods\\" + nativeKey + ".7z", nativeKey + ".7z", nexusModId, nexusFileId,
				"1.0", "1.0.0.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static NormalizedCollectionMember CreateMember(string memberKey, string modId, string fileId, string recipe)
		{
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberKey)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint(recipe), memberKey);
		}

		private static ResolvedCollectionPlan CreateReplacementPlan(CollectionTargetIdentity target,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint)
		{
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(
				CollectionIdentity.FromNexus("incoming-collection"), "incoming-revision", 1);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(revision, source,
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			return new ResolvedCollectionPlan(CollectionPlanIdentity.From(
				Guid.Parse("bbbbbbbb-1111-2222-3333-cccccccccccc"), 1), target,
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection),
				fingerprint, report,
				new[] { new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact)) });
		}
	}
}
