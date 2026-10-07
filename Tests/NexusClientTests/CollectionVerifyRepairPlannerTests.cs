using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.UI;
using Nexus.Client.ModManagement;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	public class CollectionVerifyRepairPlannerTests
	{
		private const string Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Plan_AppliedMemberWithoutRecordedDifference_IsHealthyAtCurrentCoverage()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Applied, includeBinding: true, includeNative: true);

			CollectionVerifyRepairPlan plan = f.Plan();

			Assert.That(plan.IsHealthyAtCurrentCoverage, Is.True);
			Assert.That(plan.ExactEffectVerificationAvailable, Is.False);
		}

		[Test]
		public void Plan_RecoveringAssociation_BlocksRepairAssessment()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Recovering, includeBinding: true, includeNative: true);

			CollectionVerifyRepairPlan plan = f.Plan();

			Assert.That(plan.HasActionRequired, Is.True);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.AssociationRecovering), Is.True);
		}

		[Test]
		public void Plan_MissingRequiredMember_IsExplicitlyRepairable()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: false);
			CollectionVerifyRepairPlan plan = f.Plan();
			CollectionVerifyRepairFinding finding = plan.Findings.Single(x => x.Kind == CollectionVerifyRepairFindingKind.MemberParticipationMismatch);

			Assert.That(finding.Disposition, Is.EqualTo(CollectionVerifyRepairDisposition.RestoreExpectedState));
			Assert.That(plan.HasRepairableDifferences, Is.True);
			Assert.That(plan.CanExecuteQualifiedRepair, Is.False, "C10.10a has not yet established exact effect verification coverage.");
		}

		[Test]
		public void Plan_IntentionalParticipationOmission_IsPreservedNotRepaired()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Modified, includeBinding: true, includeNative: false);
			CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, f.MemberKey,
				CollectionRequirementAspect.MemberParticipation, null);
			UserOverride localOverride = new UserOverride(Guid.NewGuid(), requirement,
				CollectionMemberRequirementStates.Included(), CollectionRequirementState.Absent(), "Keep omitted");

			CollectionVerifyRepairPlan plan = f.Plan(new[] { localOverride });

			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.MemberParticipationMismatch), Is.False);
			Assert.That(plan.Findings.Single(x => x.Kind == CollectionVerifyRepairFindingKind.PreservedExplicitOverride).Disposition,
				Is.EqualTo(CollectionVerifyRepairDisposition.PreserveLocalDecision));
		}

		[Test]
		public void Plan_PreviouslyMissingMemberNowPresent_TreatsParticipationDriftAsSatisfied()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, f.MemberKey,
				CollectionRequirementAspect.MemberParticipation, null);
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionMemberRequirementStates.Included(), CollectionRequirementState.Absent(), "Removed manually");

			CollectionVerifyRepairPlan plan = f.Plan(null, new[] { drift });

			Assert.That(plan.IsHealthyAtCurrentCoverage, Is.True);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.UncharacterizedModification), Is.False);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.Satisfied &&
				x.Requirement != null && x.Requirement.Equals(requirement) && x.ExpectedState.Equals(x.ObservedState)), Is.True);
		}

		[Test]
		public void Plan_PreviouslyDisabledMemberNowEnabled_TreatsEnabledDriftAsSatisfied()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, f.MemberKey,
				CollectionRequirementAspect.MemberEnabledState, null);
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionMemberRequirementStates.Enabled(true), CollectionMemberRequirementStates.Enabled(false), "Disabled manually");
			var current = new Dictionary<CollectionRequirementReference, CollectionRequirementState>
			{
				{ requirement, CollectionMemberRequirementStates.Enabled(true) }
			};

			CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, f.State, f.Bindings,
				new UserOverride[0], new[] { drift }, null, null, current);

			Assert.That(plan.IsHealthyAtCurrentCoverage, Is.True);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.Satisfied &&
				x.Requirement != null && x.Requirement.Equals(requirement) && x.ExpectedState.Equals(x.ObservedState)), Is.True);
		}

		[Test]
		public void Preparation_MissingBindingWithUniqueExactNexusArtifact_UsesProvisionalAdoptedBinding()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: false);
			NativeModInstanceIdentity replacementIdentity = new NativeModInstanceIdentity(f.Association.Target, "native-b");
			CollectionNativeModState replacement = new CollectionNativeModState(replacementIdentity, "C:\\Mods\\a.7z", "a.7z",
				"100", "200", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(f.Association.Target, f.State.Roots, new[] { replacement },
				f.State.Files.Values, f.State.IniEdits.Values, f.State.GameValues.Values, f.State.Plugins.Values, f.State.PluginCoverage,
				f.State.Associations.Values, f.Bindings, new UserOverride[0], f.State.AssociationCoverage, f.State.Issues, f.State.DeploymentCommitSequence);
			ResolvedCollectionMemberPlan member = new ResolvedCollectionMemberPlan(f.Manifest.Members[0],
				CollectionResolvedArtifactChoice.Exact(f.Manifest.Members[0].Artifact));

			CollectionMemberBinding effective;
			CollectionNativeModState native;
			string issue;
			bool resolved = CollectionVerifyRepairPreparationService.TryResolveExactReinstalledBinding(f.Association, member,
				f.Bindings[0], state, out effective, out native, out issue);

			Assert.That(resolved, Is.True, issue);
			Assert.That(native.Identity, Is.EqualTo(replacementIdentity));
			Assert.That(effective.NativeMod, Is.EqualTo(replacementIdentity));
			Assert.That(effective.BindingKind, Is.EqualTo(CollectionMemberBindingKind.AdoptedExisting));
			Assert.That(effective.VerifiedRecipe, Is.EqualTo(f.Bindings[0].VerifiedRecipe));
		}

		[Test]
		public void Preparation_MissingBindingWithAmbiguousExactNexusArtifacts_RefusesAutomaticRebind()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: false);
			CollectionNativeModState replacementA = new CollectionNativeModState(
				new NativeModInstanceIdentity(f.Association.Target, "native-b"), "C:\\Mods\\a.7z", "a.7z",
				"100", "200", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
			CollectionNativeModState replacementB = new CollectionNativeModState(
				new NativeModInstanceIdentity(f.Association.Target, "native-c"), "C:\\Mods\\b.7z", "b.7z",
				"100", "200", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual);
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(f.Association.Target, f.State.Roots, new[] { replacementA, replacementB },
				f.State.Files.Values, f.State.IniEdits.Values, f.State.GameValues.Values, f.State.Plugins.Values, f.State.PluginCoverage,
				f.State.Associations.Values, f.Bindings, new UserOverride[0], f.State.AssociationCoverage, f.State.Issues, f.State.DeploymentCommitSequence);
			ResolvedCollectionMemberPlan member = new ResolvedCollectionMemberPlan(f.Manifest.Members[0],
				CollectionResolvedArtifactChoice.Exact(f.Manifest.Members[0].Artifact));

			CollectionMemberBinding effective;
			CollectionNativeModState native;
			string issue;
			bool resolved = CollectionVerifyRepairPreparationService.TryResolveExactReinstalledBinding(f.Association, member,
				f.Bindings[0], state, out effective, out native, out issue);

			Assert.That(resolved, Is.False);
			StringAssert.Contains("multiple installed native mods", issue);
		}

		[Test]
		public void Plan_MemberEnabledDrift_IsRepairableButRequiresExplicitRepair()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, f.MemberKey,
				CollectionRequirementAspect.MemberEnabledState, null);
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionMemberRequirementStates.Enabled(true), CollectionMemberRequirementStates.Enabled(false), "Disabled manually");

			CollectionVerifyRepairFinding finding = f.Plan(null, new[] { drift }).Findings
				.Single(x => x.Kind == CollectionVerifyRepairFindingKind.DetectedDrift);

			Assert.That(finding.Disposition, Is.EqualTo(CollectionVerifyRepairDisposition.RestoreExpectedState));
		}

		[Test]
		public void Plan_OpaqueFileWinnerDrift_FailsClosedAsActionRequired()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Modified, includeBinding: true, includeNative: true);
			CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, null,
				CollectionRequirementAspect.FileWinner, "Data:textures\\shared.dds");
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionRequirementState.Present("winner-v1", "native-a"),
				CollectionRequirementState.Present("winner-v1", "native-b"), "Winner changed");

			CollectionVerifyRepairPlan plan = f.Plan(null, new[] { drift });

			Assert.That(plan.HasActionRequired, Is.True);
			Assert.That(plan.Findings.Single(x => x.Kind == CollectionVerifyRepairFindingKind.DetectedDrift).Disposition,
				Is.EqualTo(CollectionVerifyRepairDisposition.ActionRequired));
		}

		[TestCase(CollectionAssociationState.Modified)]
		[TestCase(CollectionAssociationState.Incomplete)]
		public void Plan_ChangedAssociationWithoutExactCoverage_NeverPretendsHealthy(CollectionAssociationState associationState)
		{
			Fixture f = CreateFixture(associationState, includeBinding: true, includeNative: true);

			CollectionVerifyRepairPlan plan = f.Plan();

			Assert.That(plan.HasActionRequired, Is.True);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.UncharacterizedModification), Is.True);
		}

		[Test]
		public void Plan_MissingRequiredBinding_IsRepairableFromRetainedManifest()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: false, includeNative: false);

			CollectionVerifyRepairPlan plan = f.Plan();

			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.MemberParticipationMismatch && x.IsRepairable), Is.True);
		}


		[Test]
		public void Preparation_SelectedMembersPreserveInstalledSourcePolicySubstitution()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Applied, includeBinding: true, includeNative: true);
			NormalizedCollectionMember normalized = f.Manifest.Members.Single();
			var selectedArtifact = new CollectionArtifactReference(NexusCollectionModFileArtifactIdentity.Scheme,
				NexusCollectionModFileArtifactIdentity.Format("skyrimspecialedition", 100, 300), null);
			var choice = CollectionResolvedArtifactChoice.SupportedSubstitution(normalized.Artifact, selectedArtifact,
				CollectionNexusSourcePolicyResolver.LatestSubstitutionRuleId);
			var choices = new Dictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> { { f.MemberKey, choice } };

			ResolvedCollectionMemberPlan member = CollectionVerifyRepairPreparationService.CreateSelectedMembers(f.Manifest, choices).Single();

			Assert.That(member.ArtifactChoice, Is.EqualTo(choice));
			Assert.That(member.ArtifactChoice.SelectedArtifact, Is.EqualTo(selectedArtifact));
		}

		[Test]
		public void Plan_ExactNoEffectCoverage_QualifiesCanonicalVirtualEnabledRepair()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, f.MemberKey,
				CollectionRequirementAspect.MemberEnabledState, null);
			CollectionDriftObservation drift = new CollectionDriftObservation(Guid.NewGuid(), requirement,
				CollectionMemberRequirementStates.Enabled(true), CollectionMemberRequirementStates.Enabled(false), "Disabled manually");
			CollectionMemberEffectPreview preview = new CollectionMemberEffectPreview(f.MemberKey, f.Manifest.Members[0].RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data, new CollectionPlannedFileEffect[0], new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);

			CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, f.State, f.Bindings,
				new UserOverride[0], new[] { drift }, new[] { preview });

			Assert.That(plan.ExactEffectVerificationAvailable, Is.True);
			Assert.That(plan.CanExecuteQualifiedRepair, Is.True);
		}

		[TestCase(CollectionAssociationState.Applied)]
		[TestCase(CollectionAssociationState.Incomplete)]
		[TestCase(CollectionAssociationState.Modified)]
		public void ExactEffectVerification_MatchingDestinationBytes_AreAccepted(CollectionAssociationState associationState)
		{
			Fixture f = CreateFixture(associationState, includeBinding: true, includeNative: true);
			string path = Path.GetTempFileName();
			try
			{
				byte[] bytes = new byte[] { 1, 2, 3, 4, 5 };
				File.WriteAllBytes(path, bytes);
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\verified.dds");
				CollectionNativeStateIndex state = WithManagedFile(f, target, path);
				CollectionMemberEffectPreview preview = ExactFilePreview(f, target, bytes);

				CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, state, f.Bindings,
					new UserOverride[0], new CollectionDriftObservation[0], new[] { preview });

				Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch ||
					x.Kind == CollectionVerifyRepairFindingKind.FileContentVerificationUnavailable), Is.False);
				Assert.That(plan.IsHealthyAtCurrentCoverage, Is.True);
				Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.UncharacterizedModification), Is.False);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestCase(CollectionAssociationState.Applied)]
		[TestCase(CollectionAssociationState.Incomplete)]
		[Category("CollectionsC12FailureInjection")]
		public void ExactEffectVerification_CorruptDestinationBytes_AreQualifiedForRepair(CollectionAssociationState associationState)
		{
			Fixture f = CreateFixture(associationState, includeBinding: true, includeNative: true);
			string path = Path.GetTempFileName();
			try
			{
				byte[] expected = new byte[] { 1, 2, 3, 4, 5 };
				File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\corrupt.dds");
				CollectionNativeStateIndex state = WithManagedFile(f, target, path);
				CollectionMemberEffectPreview preview = ExactFilePreview(f, target, expected);

				CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, state, f.Bindings,
					new UserOverride[0], new CollectionDriftObservation[0], new[] { preview });

				CollectionVerifyRepairFinding finding = plan.Findings.Single(x => x.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch);
				Assert.That(finding.Disposition, Is.EqualTo(CollectionVerifyRepairDisposition.RestoreExpectedState));
				Assert.That(plan.HasActionRequired, Is.False);
				Assert.That(plan.IsHealthyAtCurrentCoverage, Is.False);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[Test]
		public void ExactEffectVerification_FileOverride_PreservesLocalDecision()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Modified, includeBinding: true, includeNative: true);
			string path = Path.GetTempFileName();
			try
			{
				byte[] expected = new byte[] { 1, 2, 3, 4, 5 };
				File.WriteAllBytes(path, new byte[] { 7, 7, 7 });
				ModDeploymentTarget target = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\override.dds");
				CollectionNativeStateIndex state = WithManagedFile(f, target, path);
				CollectionMemberEffectPreview preview = ExactFilePreview(f, target, expected);
				CollectionRequirementReference requirement = new CollectionRequirementReference(f.Association, f.MemberKey,
					CollectionRequirementAspect.FileWinner, target.ToString());
				UserOverride local = new UserOverride(Guid.NewGuid(), requirement, CollectionRequirementState.Present("winner-v1", "old"),
					CollectionRequirementState.Present("winner-v1", "local"), "Preserve local file winner");

				CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, state, f.Bindings,
					new[] { local }, new CollectionDriftObservation[0], new[] { preview });

				Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch), Is.False);
				Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.PreservedExplicitOverride &&
					x.Requirement != null && x.Requirement.Equals(requirement)), Is.True);
			}
			finally
			{
				File.Delete(path);
			}
		}

		[TestCase(CollectionAssociationState.Applied)]
		[TestCase(CollectionAssociationState.Incomplete)]
		public void Plan_FileEffectWithoutDestinationHash_FailsClosedInsteadOfClaimingByteVerification(CollectionAssociationState associationState)
		{
			Fixture f = CreateFixture(associationState, includeBinding: true, includeNative: true);
			CollectionMemberEffectPreview preview = new CollectionMemberEffectPreview(f.MemberKey, f.Manifest.Members[0].RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data, new[] { new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\a.dds")) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);

			CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, f.State, f.Bindings,
				new UserOverride[0], new CollectionDriftObservation[0], new[] { preview });

			Assert.That(plan.HasActionRequired, Is.True);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch ||
				x.Kind == CollectionVerifyRepairFindingKind.FileContentVerificationUnavailable), Is.True);
		}

		[Test]
		public void ManifestPluginAfterRule_DriftIsQualifiedAsDirectPluginRepair()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Modified, includeBinding: true, includeNative: true);
			var rule = new CollectionPluginRelativeOrderRule("A.esp", "B.esp");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(f.Manifest.Revision, f.Manifest.Source,
				CollectionManifestMemberSetCompleteness.Complete, null, f.Manifest.Members, null, null, null, null, null, new[] { rule });
			CollectionNativePluginState a = Plugin("A.esp", 0);
			CollectionNativePluginState b = Plugin("B.esp", 1);
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(f.Association.Target, f.State.Roots, f.State.Mods.Values,
				f.State.Files.Values, f.State.IniEdits.Values, f.State.GameValues.Values, new[] { a, b }, CollectionNativeStateCoverage.Complete,
				f.State.Associations.Values, f.Bindings, new UserOverride[0], f.State.AssociationCoverage, f.State.Issues, f.State.DeploymentCommitSequence);
			CollectionMemberEffectPreview preview = new CollectionMemberEffectPreview(f.MemberKey, manifest.Members[0].RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data, new CollectionPlannedFileEffect[0], new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);

			CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, manifest, state, f.Bindings,
				new UserOverride[0], new CollectionDriftObservation[0], new[] { preview });

			CollectionVerifyRepairFinding finding = plan.Findings.Single(x => x.MemberKey == null &&
				x.Kind == CollectionVerifyRepairFindingKind.PluginEffectMismatch);
			Assert.That(finding.Disposition, Is.EqualTo(CollectionVerifyRepairDisposition.RestoreExpectedState));
			Assert.That(finding.Requirement.Aspect, Is.EqualTo(CollectionRequirementAspect.PluginState));
			Assert.That(finding.Requirement.SubjectKey, Is.EqualTo("after:b.esp|a.esp"));
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.UncharacterizedModification), Is.False);
		}

		[Test]
		public void ManifestPluginAfterRule_ExplicitOverrideIsPreserved()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Modified, includeBinding: true, includeNative: true);
			var rule = new CollectionPluginRelativeOrderRule("A.esp", "B.esp");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(f.Manifest.Revision, f.Manifest.Source,
				CollectionManifestMemberSetCompleteness.Complete, null, f.Manifest.Members, null, null, null, null, null, new[] { rule });
			CollectionNativeStateIndex state = new CollectionNativeStateIndex(f.Association.Target, f.State.Roots, f.State.Mods.Values,
				f.State.Files.Values, f.State.IniEdits.Values, f.State.GameValues.Values, new[] { Plugin("A.esp", 0), Plugin("B.esp", 1) },
				CollectionNativeStateCoverage.Complete, f.State.Associations.Values, f.Bindings, new UserOverride[0],
				f.State.AssociationCoverage, f.State.Issues, f.State.DeploymentCommitSequence);
			var requirement = new CollectionRequirementReference(f.Association, null, CollectionRequirementAspect.PluginState,
				"after:b.esp|a.esp");
			var local = new UserOverride(Guid.NewGuid(), requirement, CollectionRequirementState.Present("plugin-order-v1", "curator"),
				CollectionRequirementState.Present("plugin-order-v1", "local"), "Keep local order");

			CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, manifest, state, f.Bindings,
				new[] { local }, new CollectionDriftObservation[0]);

			Assert.That(plan.Findings.Any(x => x.Requirement != null && x.Requirement.Equals(requirement) &&
				x.Kind == CollectionVerifyRepairFindingKind.PreservedExplicitOverride), Is.True);
			Assert.That(plan.Findings.Any(x => x.Requirement != null && x.Requirement.Equals(requirement) && x.IsRepairable), Is.False);
		}

		/// <summary>Incomplete exact preparation cannot normalize a historical incomplete association.</summary>
		[Test]
		public void Plan_IncompletePreparation_KeepsIncompleteAssociationActionRequired()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			var preparation = new CollectionVerifyRepairPreparationResult(null, null, "Exact archive is unavailable.");
			CollectionVerifyRepairPlan plan = new CollectionVerifyRepairPlanner().Plan(f.Association, f.Manifest, f.State, f.Bindings,
				new UserOverride[0], new CollectionDriftObservation[0], null, preparation);

			Assert.That(plan.IsHealthyAtCurrentCoverage, Is.False);
			Assert.That(plan.Findings.Any(x => x.Kind == CollectionVerifyRepairFindingKind.ExactRecipePreparationUnavailable), Is.True);
		}

		/// <summary>An unchanged persisted incomplete state must not erase the verification review on document activation.</summary>
		[Test]
		public void ManagedRefresh_UnchangedIncompletePresentation_PreservesCurrentReview()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			Assert.That(CollectionsPreviewControl.HasSameManagedAssociationState(CreateManagedPresentation(f),
				CreateManagedPresentation(f)), Is.True);
			Assert.That(CollectionsPreviewControl.HasSameManagedAssociationState(CreateManagedPresentation(f),
				CreateManagedPresentation(f, association: f.Association.WithState(CollectionAssociationState.Applied))), Is.False);
		}

		/// <summary>A changed native binding invalidates the old review even when association state is still incomplete.</summary>
		[Test]
		public void ManagedRefresh_RecreatedNativeBinding_InvalidatesCurrentReview()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			CollectionMemberBinding old = f.Bindings.Single();
			var replacement = new CollectionMemberBinding(f.Association, old.MemberKey,
				new NativeModInstanceIdentity(f.Association.Target, "native-b"), old.VerifiedRecipe, old.BindingKind);
			Assert.That(CollectionsPreviewControl.HasSameManagedAssociationState(CreateManagedPresentation(f),
				CreateManagedPresentation(f, binding: replacement)), Is.False);
		}

		/// <summary>Drift content changes invalidate a review even when the observation identity is reused.</summary>
		[Test]
		public void ManagedRefresh_ChangedDriftWithSameIdentity_InvalidatesCurrentReview()
		{
			Fixture f = CreateFixture(CollectionAssociationState.Incomplete, includeBinding: true, includeNative: true);
			var requirement = new CollectionRequirementReference(f.Association, null, CollectionRequirementAspect.FileWinner, "Data:a.dds");
			var before = new CollectionDriftObservation(Guid.NewGuid(), requirement, CollectionRequirementState.Present("winner-v1", "a"),
				CollectionRequirementState.Present("winner-v1", "b"), "Changed winner");
			var after = new CollectionDriftObservation(before.ObservationId, requirement, before.ExpectedState,
				CollectionRequirementState.Present("winner-v1", "c"), before.Detail);
			Assert.That(CollectionsPreviewControl.HasSameManagedAssociationState(CreateManagedPresentation(f, drift: before),
				CreateManagedPresentation(f, drift: after)), Is.False);
		}

		/// <summary>Creates an installed presentation for refresh comparison without creating a WinForms control.</summary>
		private static CollectionManagementAssociationPresentation CreateManagedPresentation(Fixture fixture,
			CollectionMemberBinding binding = null, CollectionTargetAssociation association = null, CollectionDriftObservation drift = null)
		{
			CollectionTargetAssociation current = association ?? fixture.Association;
			CollectionMemberBinding memberBinding = binding ?? fixture.Bindings.Single();
			var member = new CollectionManagementMemberPresentation(memberBinding,
				new NativeModProvenance(memberBinding.NativeMod, StandaloneModUse.Unknown), 1,
				new UserOverride[0], new CollectionDriftObservation[0]);
			var customization = new CollectionAssociationCustomization(current, new UserOverride[0],
				drift == null ? new CollectionDriftObservation[0] : new[] { drift });
			return new CollectionManagementAssociationPresentation(new CollectionManagementAssociation(current, "Collection", "Revision 1"),
				null, null, null, new[] { member }, customization, "fallout4", null);
		}

		private static CollectionNativePluginState Plugin(string name, int priority)
		{
			return new CollectionNativePluginState(name, true, priority, null, String.Empty, PluginParseStatus.Parsed,
				PluginAddressClass.Full, PluginHeaderFlags.None, PluginSpecialFlags.None, false, 0,
				new string[0], new CollectionNativePluginDiagnostic[0]);
		}

		private static CollectionMemberEffectPreview ExactFilePreview(Fixture fixture, ModDeploymentTarget target, byte[] bytes)
		{
			return new CollectionMemberEffectPreview(fixture.MemberKey, fixture.Manifest.Members[0].RecipeIdentity,
				ModInstallMethod.Virtual, ModInstallRoot.Data,
				new[] { new CollectionPlannedFileEffect(target, Hash(bytes), bytes.LongLength) },
				new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
				new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
		}

		private static CollectionNativeStateIndex WithManagedFile(Fixture fixture, ModDeploymentTarget target, string physicalPath)
		{
			var owner = new CollectionNativeOwnerState("native-a", null, CollectionNativeOwnerKind.NativeMod, true, 0, null);
			var file = new CollectionNativeFileState(target, physicalPath, false, true, true, "native-a",
				new[] { owner }, new CollectionNativeOwnerState[0], new[] { owner });
			return new CollectionNativeStateIndex(fixture.Association.Target, fixture.State.Roots, fixture.State.Mods.Values,
				new[] { file }, fixture.State.IniEdits.Values, fixture.State.GameValues.Values, fixture.State.Plugins.Values,
				fixture.State.PluginCoverage, fixture.State.Associations.Values, fixture.Bindings, new UserOverride[0],
				fixture.State.AssociationCoverage, fixture.State.Issues, fixture.State.DeploymentCommitSequence);
		}

		private static CollectionContentHash Hash(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static Fixture CreateFixture(CollectionAssociationState state, bool includeBinding, bool includeNative)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c10-10");
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c10-10");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1);
			CollectionTargetAssociation association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, state);
			CollectionMemberKey key = CollectionMemberKey.FromProvider("member-a");
			NormalizedCollectionMember member = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(key), CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/200", null),
				CollectionRecipeIdentity.FromFingerprint("recipe-a"), "Member A");
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256), 100,
				"schema-v1", "normalizer-v1");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(revision, source,
				CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			NativeModInstanceIdentity nativeIdentity = new NativeModInstanceIdentity(target, "native-a");
			CollectionMemberBinding binding = includeBinding
				? new CollectionMemberBinding(association, key, nativeIdentity, member.RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection)
				: null;
			CollectionNativeModState native = includeNative
				? new CollectionNativeModState(nativeIdentity, "C:\\Mods\\a.7z", "a.7z", "100", "200", "1", "1", ModInstallRoot.Data, ModInstallMethod.Virtual)
				: null;
			CollectionNativeStateIndex nativeState = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				native == null ? new CollectionNativeModState[0] : new[] { native }, new CollectionNativeFileState[0],
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, new[] { association },
				binding == null ? new CollectionMemberBinding[0] : new[] { binding }, new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
			return new Fixture(association, key, manifest, nativeState,
				binding == null ? new CollectionMemberBinding[0] : new[] { binding });
		}

		private sealed class Fixture
		{
			public Fixture(CollectionTargetAssociation association, CollectionMemberKey memberKey, NormalizedCollectionManifest manifest,
				CollectionNativeStateIndex state, CollectionMemberBinding[] bindings)
			{
				Association = association; MemberKey = memberKey; Manifest = manifest; State = state; Bindings = bindings;
			}
			public CollectionTargetAssociation Association { get; }
			public CollectionMemberKey MemberKey { get; }
			public NormalizedCollectionManifest Manifest { get; }
			public CollectionNativeStateIndex State { get; }
			public CollectionMemberBinding[] Bindings { get; }
			public CollectionVerifyRepairPlan Plan(UserOverride[] overrides = null, CollectionDriftObservation[] drift = null)
			{
				return new CollectionVerifyRepairPlanner().Plan(Association, Manifest, State, Bindings,
					overrides ?? new UserOverride[0], drift ?? new CollectionDriftObservation[0]);
			}
		}
	}
}
