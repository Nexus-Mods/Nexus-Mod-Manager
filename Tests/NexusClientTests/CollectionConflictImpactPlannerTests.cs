using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.4 conflict/impact planning coverage over exact C6.1-C6.3 inputs.
	/// </summary>
	[TestFixture]
	public class CollectionConflictImpactPlannerTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void EffectPreviewBuilder_ProjectsTranslatedC5FilePlanWithoutExecutingIt()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			var resolved = new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			var recipe = new ModInstallationSimpleFileRecipe(new[]
			{
				new ModInstallationSimpleFileMapping("source\\one.bin", "root\\one.bin")
			});
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.GameRoot);
			var operationIdentity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint("target-sha256:" + new string('a', 64), context, member.RecipeIdentity.Fingerprint));
			var validation = new ModInstallationRecipeValidation(
				ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion,
				context,
				new ModInstallationRecipeExpectedContent(new string('b', 64), 10),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
					ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
				new[]
				{
					new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "source\\one.bin"),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "root\\one.bin")
				});
			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(
				new ModInstallationRecipeInput(operationIdentity, validation), recipe);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) => null);
			IMod mod = InterfaceStub<IMod>.Create((method, args) => null);

			CollectionMemberEffectPreview preview = new CollectionMemberEffectPreviewBuilder().Build(resolved, translated, gameMode, mod);

			Assert.IsTrue(preview.IsComplete);
			Assert.AreEqual(1, preview.Files.Count);
			Assert.AreEqual(ModDeploymentRoot.GameRoot, preview.Files[0].Target.Root);
			Assert.AreEqual("root\\one.bin", preview.Files[0].Target.RelativePath);
		}

		[Test]
		public void Plan_CharacterizedFilePriorityChoosesUniqueWinnerWithoutUsingPhaseOrder()
		{
			NormalizedCollectionMember a = CreateMember(0, "a", 100, 200, 50);
			NormalizedCollectionMember b = CreateMember(1, "b", 101, 201, -10);
			CollectionFilePriorityRule rule = new CollectionFilePriorityRule(a.IdentityResolution.Key, b.IdentityResolution.Key);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			CollectionTargetIdentity collectionTarget = CreateTarget();
			CollectionNativeModState modA = CreateNativeMod(collectionTarget, "native-a", 100, 200);
			CollectionNativeModState modB = CreateNativeMod(collectionTarget, "native-b", 101, 201);
			CollectionNativeFileState file = CreateFile(target, modA.Identity.NativeModKey);
			Fixture fixture = CreateFixture(collectionTarget, new[] { a, b }, new[] { rule }, new[] { modA, modB },
				new[] { file }, null, null, null, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State,
				new[] { CreatePreview(a, target), CreatePreview(b, target) });

			Assert.AreEqual(CollectionConflictImpactStatus.Ready, result.Status);
			Assert.AreEqual("b", result.FileImpacts.Single().PlannedWinner.Value);
			Assert.AreEqual(new[] { "a", "b" }, result.FileImpacts.Single().Writers.Select(x => x.Value).ToArray());
		}

		[Test]
		public void Plan_InstalledCompatibleSelectedMemberStillParticipatesInTouchedFileWinner()
		{
			NormalizedCollectionMember compatible = CreateMember(0, "compatible", 100, 200, 0);
			NormalizedCollectionMember incoming = CreateMember(1, "incoming", 101, 201, 0);
			CollectionTargetIdentity targetIdentity = CreateTarget();
			CollectionNativeModState compatibleMod = CreateNativeMod(targetIdentity, "native-compatible", 100, 200);
			CollectionNativeModState incomingMod = CreateNativeMod(targetIdentity, "native-incoming", 101, 201);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			CollectionTargetAssociation association = new CollectionTargetAssociation(Guid.NewGuid(), CreateRevision(), targetIdentity,
				CollectionAssociationState.Applied);
			CollectionMemberBinding binding = new CollectionMemberBinding(association, compatible.IdentityResolution.Key,
				compatibleMod.Identity, compatible.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting);
			CollectionFilePriorityRule rule = new CollectionFilePriorityRule(incoming.IdentityResolution.Key, compatible.IdentityResolution.Key);
			Fixture fixture = CreateFixture(targetIdentity, new[] { compatible, incoming }, new[] { rule },
				new[] { compatibleMod, incomingMod }, new[] { CreateFile(target, compatibleMod.Identity.NativeModKey) },
				new[] { association }, new[] { binding }, null, CollectionNativeStateCoverage.NotApplicable);

			Assert.AreEqual(CollectionMemberMatchDisposition.InstalledCompatible,
				fixture.Matches.MembersByKey[compatible.IdentityResolution.Key].Disposition);
			Assert.AreEqual(CollectionMemberMatchDisposition.ReinstallRequired,
				fixture.Matches.MembersByKey[incoming.IdentityResolution.Key].Disposition);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { CreatePreview(incoming, target) });

			Assert.AreEqual(CollectionConflictImpactStatus.Ready, result.Status);
			CollectionFileImpact impact = result.FileImpacts.Single();
			CollectionAssert.AreEquivalent(new[] { compatible.IdentityResolution.Key, incoming.IdentityResolution.Key }, impact.Writers);
			Assert.AreEqual(compatible.IdentityResolution.Key, impact.PlannedWinner);
			Assert.AreEqual(compatibleMod.Identity.NativeModKey, impact.CurrentOwnerKey);
		}

		[Test]
		public void Plan_OverlappingMembersWithoutPriorityRequireExplicitWinnerDecision()
		{
			NormalizedCollectionMember a = CreateMember(0, "a", 100, 200, 0);
			NormalizedCollectionMember b = CreateMember(1, "b", 101, 201, 0);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "meshes\\shared.nif");
			CollectionTargetIdentity collectionTarget = CreateTarget();
			Fixture fixture = CreateFixture(collectionTarget, new[] { a, b }, null,
				new[] { CreateNativeMod(collectionTarget, "native-a", 100, 200), CreateNativeMod(collectionTarget, "native-b", 101, 201) },
				new CollectionNativeFileState[0], null, null, null, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State,
				new[] { CreatePreview(a, target), CreatePreview(b, target) });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			Assert.IsNull(result.FileImpacts.Single().PlannedWinner);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.FileWinnerDecisionRequired));
		}

		[Test]
		public void Plan_FilePriorityCycleBlocks()
		{
			NormalizedCollectionMember a = CreateMember(0, "a", 100, 200, 0);
			NormalizedCollectionMember b = CreateMember(1, "b", 101, 201, 0);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "meshes\\shared.nif");
			CollectionTargetIdentity collectionTarget = CreateTarget();
			Fixture fixture = CreateFixture(collectionTarget, new[] { a, b }, new[]
			{
				new CollectionFilePriorityRule(a.IdentityResolution.Key, b.IdentityResolution.Key),
				new CollectionFilePriorityRule(b.IdentityResolution.Key, a.IdentityResolution.Key)
			}, new[] { CreateNativeMod(collectionTarget, "native-a", 100, 200), CreateNativeMod(collectionTarget, "native-b", 101, 201) },
				new CollectionNativeFileState[0], null, null, null, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State,
				new[] { CreatePreview(a, target), CreatePreview(b, target) });

			Assert.AreEqual(CollectionConflictImpactStatus.Blocked, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.FilePriorityCycle));
		}

		[Test]
		public void Plan_UnrelatedExistingFileOwnerRequiresAdditiveReview()
		{
			NormalizedCollectionMember incoming = CreateMember(0, "incoming", 100, 200, 0);
			CollectionTargetIdentity targetIdentity = CreateTarget();
			CollectionNativeModState incomingMod = CreateNativeMod(targetIdentity, "native-incoming", 100, 200);
			CollectionNativeModState unrelated = CreateNativeMod(targetIdentity, "native-unrelated", 999, 999);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			Fixture fixture = CreateFixture(targetIdentity, new[] { incoming }, null, new[] { incomingMod, unrelated },
				new[] { CreateFile(target, unrelated.Identity.NativeModKey) }, null, null, null, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { CreatePreview(incoming, target) });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired));
			Assert.AreEqual(unrelated.Identity.NativeModKey, result.FileImpacts.Single().CurrentOwnerKey);
		}

		[Test]
		public void Plan_UnresolvedRecordedManagedFileOwnershipBlocksExactWinnerPlanning()
		{
			NormalizedCollectionMember incoming = CreateMember(0, "incoming", 100, 200, 0);
			CollectionTargetIdentity targetIdentity = CreateTarget();
			CollectionNativeModState incomingMod = CreateNativeMod(targetIdentity, "native-incoming", 100, 200);
			ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\shared.dds");
			CollectionNativeOwnerState unresolved = new CollectionNativeOwnerState(null, "legacy-owner-ref",
				CollectionNativeOwnerKind.Unresolved, null, null, null);
			CollectionNativeFileState file = new CollectionNativeFileState(target, "C:\\Game\\Data\\textures\\shared.dds",
				false, true, false, null, new[] { unresolved }, new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0]);
			Fixture fixture = CreateFixture(targetIdentity, new[] { incoming }, null, new[] { incomingMod },
				new[] { file }, null, null, null, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { CreatePreview(incoming, target) });

			Assert.AreEqual(CollectionConflictImpactStatus.Blocked, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileOwnershipUnresolved));
		}

		[Test]
		public void Plan_AbsolutePluginIndexRequiresExplicitAdditiveImpactReview()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			Fixture fixture = CreateFixture(target, new[] { member }, null,
				new[] { CreateNativeMod(target, "native", 100, 200) }, new CollectionNativeFileState[0],
				null, null, null, CollectionNativeStateCoverage.Complete);
			CollectionMemberEffectPreview preview = CreatePreview(member, null,
				new[] { CollectionPlannedPluginEffect.AbsoluteOrder("Example.esp", 3) });

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { preview });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.PluginOrderImpactRequiresReview));
		}

		[Test]
		public void Plan_AcquisitionRequiredMemberKeepsImpactPlanInPreparation()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			Fixture fixture = CreateFixture(target, new[] { member }, null, new CollectionNativeModState[0],
				new CollectionNativeFileState[0], null, null, null, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new CollectionMemberEffectPreview[0]);

			Assert.AreEqual(CollectionConflictImpactStatus.PreparationRequired, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.AcquisitionOrTranslationRequired));
		}

		[Test]
		public void Plan_IncompleteNativeEffectPreviewBlocksInsteadOfDroppingUnknownEffects()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			Fixture fixture = CreateFixture(target, new[] { member }, null,
				new[] { CreateNativeMod(target, "native", 100, 200) }, new CollectionNativeFileState[0],
				null, null, null, CollectionNativeStateCoverage.NotApplicable);
			CollectionMemberEffectPreview preview = new CollectionMemberEffectPreview(member.IdentityResolution.Key, member.RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data, new CollectionPlannedFileEffect[0], new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new[]
				{
					new CollectionEffectPreviewIssue(CollectionEffectPreviewIssueKind.UnboundedBasicInstall,
						"PerformBasicInstallOperation", "unbounded")
				});

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { preview });

			Assert.AreEqual(CollectionConflictImpactStatus.Blocked, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.EffectPreviewIncomplete));
		}

		[Test]
		public void Plan_PluginEffectRequiresCompletePluginState()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			Fixture fixture = CreateFixture(target, new[] { member }, null,
				new[] { CreateNativeMod(target, "native", 100, 200) }, new CollectionNativeFileState[0],
				null, null, null, CollectionNativeStateCoverage.Unavailable);
			CollectionMemberEffectPreview preview = CreatePreview(member, null,
				new[] { CollectionPlannedPluginEffect.Activation("Example.esp", true) });

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { preview });

			Assert.AreEqual(CollectionConflictImpactStatus.Blocked, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.PluginStateUnavailable));
		}

		[Test]
		public void Plan_ExistingPluginWithUnknownOwnerRequiresAdditiveReviewBeforeStateChange()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			var plugin = new CollectionNativePluginState("Example.esp", true, 1, 0, "00", PluginParseStatus.Parsed,
				PluginAddressClass.Full, PluginHeaderFlags.None, PluginSpecialFlags.None, false, 44, new string[0],
				new CollectionNativePluginDiagnostic[0]);
			Fixture fixture = CreateFixture(target, new[] { member }, null,
				new[] { CreateNativeMod(target, "native", 100, 200) }, new CollectionNativeFileState[0],
				null, null, null, CollectionNativeStateCoverage.Complete, null, null, new[] { plugin });
			CollectionMemberEffectPreview preview = CreatePreview(member, null,
				new[] { CollectionPlannedPluginEffect.Activation("Example.esp", false) });

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { preview });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingPluginStateDecisionRequired));
		}

		[Test]
		public void Plan_ExistingConfigurationOwnedByUnrelatedModRequiresAdditiveReview()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			CollectionNativeModState incoming = CreateNativeMod(target, "native-incoming", 100, 200);
			CollectionNativeModState unrelated = CreateNativeMod(target, "native-unrelated", 999, 999);
			CollectionNativeIniKey key = new CollectionNativeIniKey("game.ini", "Display", "Quality");
			var current = new CollectionNativeIniState(key,
				new[] { new CollectionNativeTextOwnerValue(unrelated.Identity.NativeModKey, "High") });
			Fixture fixture = CreateFixture(target, new[] { member }, null, new[] { incoming, unrelated },
				new CollectionNativeFileState[0], null, null, null, CollectionNativeStateCoverage.NotApplicable,
				new[] { current }, null, null);
			CollectionMemberEffectPreview preview = CreatePreview(member, null, null,
				new[] { new CollectionPlannedIniEffect(key, "Low") });

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { preview });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingConfigurationDecisionRequired));
		}

		[Test]
		public void Plan_ConflictingIniValuesRequireExplicitConfigurationDecision()
		{
			NormalizedCollectionMember a = CreateMember(0, "a", 100, 200, 0);
			NormalizedCollectionMember b = CreateMember(1, "b", 101, 201, 0);
			CollectionTargetIdentity target = CreateTarget();
			Fixture fixture = CreateFixture(target, new[] { a, b }, null,
				new[] { CreateNativeMod(target, "native-a", 100, 200), CreateNativeMod(target, "native-b", 101, 201) },
				new CollectionNativeFileState[0], null, null, null, CollectionNativeStateCoverage.NotApplicable);
			CollectionNativeIniKey key = new CollectionNativeIniKey("game.ini", "Display", "Quality");
			CollectionMemberEffectPreview first = CreatePreview(a, null, null, new[] { new CollectionPlannedIniEffect(key, "High") });
			CollectionMemberEffectPreview second = CreatePreview(b, null, null, new[] { new CollectionPlannedIniEffect(key, "Low") });

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State, new[] { first, second });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ConfigurationWinnerDecisionRequired));
		}

		[Test]
		public void Plan_SharedModifiedAssociationWithUserOverrideIsReportedAndNeverSilentlyRepaired()
		{
			NormalizedCollectionMember member = CreateMember(0, "member", 100, 200, 0);
			CollectionTargetIdentity target = CreateTarget();
			CollectionNativeModState nativeMod = CreateNativeMod(target, "native", 100, 200);
			CollectionTargetAssociation association = new CollectionTargetAssociation(Guid.NewGuid(), CreateOtherRevision(), target,
				CollectionAssociationState.Modified);
			CollectionMemberBinding binding = new CollectionMemberBinding(association, member.IdentityResolution.Key,
				nativeMod.Identity, member.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection);
			CollectionRequirementReference requirement = new CollectionRequirementReference(association, member.IdentityResolution.Key,
				CollectionRequirementAspect.InstallerRecipe, null);
			UserOverride userOverride = new UserOverride(Guid.NewGuid(), requirement,
				CollectionRequirementState.Present("recipe-v1", "baseline"), CollectionRequirementState.Present("recipe-v1", "local"), null);
			Fixture fixture = CreateFixture(target, new[] { member }, null, new[] { nativeMod }, new CollectionNativeFileState[0],
				new[] { association }, new[] { binding }, new[] { userOverride }, CollectionNativeStateCoverage.NotApplicable);

			CollectionConflictImpactPlan result = new CollectionConflictImpactPlanner().Plan(
				fixture.Plan, fixture.Matches, fixture.DependencyPlan, fixture.State,
				new[] { CreatePreview(member, ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "x.txt")) });

			Assert.AreEqual(CollectionConflictImpactStatus.ActionRequired, result.Status);
			CollectionAssociationImpact impact = result.AssociationImpacts.Single(x => x.Association.AssociationId == association.AssociationId);
			Assert.IsTrue((impact.Kind & CollectionAssociationImpactKind.SharedNativeInstance) != 0);
			Assert.IsTrue((impact.Kind & CollectionAssociationImpactKind.UserOverride) != 0);
			Assert.IsTrue(result.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingUserOverride));
		}

		private static CollectionMemberEffectPreview CreatePreview(NormalizedCollectionMember member, ModDeploymentTarget fileTarget,
			IEnumerable<CollectionPlannedPluginEffect> pluginEffects = null, IEnumerable<CollectionPlannedIniEffect> iniEffects = null)
		{
			return new CollectionMemberEffectPreview(member.IdentityResolution.Key, member.RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data,
				fileTarget == null ? new CollectionPlannedFileEffect[0] : new[] { new CollectionPlannedFileEffect(fileTarget) },
				iniEffects ?? new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
				pluginEffects ?? new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
		}

		private static Fixture CreateFixture(CollectionTargetIdentity target, NormalizedCollectionMember[] members,
			CollectionFilePriorityRule[] rules, CollectionNativeModState[] mods, CollectionNativeFileState[] files,
			CollectionTargetAssociation[] associations, CollectionMemberBinding[] bindings, UserOverride[] overrides,
			CollectionNativeStateCoverage pluginCoverage, CollectionNativeIniState[] iniStates = null,
			CollectionNativeGameValueState[] gameValues = null, CollectionNativePluginState[] plugins = null)
		{
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(target,
				new CollectionNativeRootState[0], mods ?? new CollectionNativeModState[0], files ?? new CollectionNativeFileState[0],
				iniStates ?? new CollectionNativeIniState[0], gameValues ?? new CollectionNativeGameValueState[0],
				plugins ?? new CollectionNativePluginState[0], pluginCoverage,
				associations ?? new CollectionTargetAssociation[0], bindings ?? new CollectionMemberBinding[0],
				overrides ?? new UserOverride[0], CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			CollectionRevisionIdentity revision = CreateRevision();
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(revision,
				new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256A), 10, "schema", "normalizer-v3"),
				CollectionManifestMemberSetCompleteness.Complete, null, members, null, rules);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, report.Status);
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, report,
				members.Select(x => new ResolvedCollectionMemberPlan(x, CollectionResolvedArtifactChoice.Exact(x.Artifact))).ToArray());
			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(plan, state);
			CollectionDependencyPhasePlan dependencyPlan = new CollectionDependencyPhasePlanner().Plan(plan, matches);
			return new Fixture(plan, state, matches, dependencyPlan);
		}

		private static CollectionNativeModState CreateNativeMod(CollectionTargetIdentity target, string nativeKey, long modId, long fileId)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, nativeKey), "archive", nativeKey + ".7z",
				modId.ToString(), fileId.ToString(), "1.0", "1.0", ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static CollectionNativeFileState CreateFile(ModDeploymentTarget target, string ownerKey)
		{
			CollectionNativeOwnerState owner = new CollectionNativeOwnerState(ownerKey, null, CollectionNativeOwnerKind.NativeMod, true, 0, null);
			return new CollectionNativeFileState(target, "C:\\Game\\Data\\" + target.RelativePath, false, true, true, ownerKey,
				new[] { owner }, new CollectionNativeOwnerState[0], new[] { owner });
		}

		private static NormalizedCollectionMember CreateMember(int ordinal, string key, long modId, long fileId, double phase)
		{
			return new NormalizedCollectionMember(ordinal,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(key)),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/" + modId + "/" + fileId, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-" + key), "Member " + key, phase);
		}

		private static CollectionTargetIdentity CreateTarget()
		{
			return CollectionTargetIdentity.FromFingerprint("target-c6-4-" + Guid.NewGuid().ToString("N"));
		}

		private static CollectionRevisionIdentity CreateRevision()
		{
			return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("collection-c6-4"), "revision-c6-4", 1);
		}

		private static CollectionRevisionIdentity CreateOtherRevision()
		{
			return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("other-collection"), "other-revision", 1);
		}

		private sealed class Fixture
		{
			public Fixture(ResolvedCollectionPlan plan, CollectionNativeStateIndex state, CollectionMemberMatchSet matches,
				CollectionDependencyPhasePlan dependencyPlan)
			{
				Plan = plan; State = state; Matches = matches; DependencyPlan = dependencyPlan;
			}
			public ResolvedCollectionPlan Plan { get; }
			public CollectionNativeStateIndex State { get; }
			public CollectionMemberMatchSet Matches { get; }
			public CollectionDependencyPhasePlan DependencyPlan { get; }
		}
	}
}
