using System;
using System.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.2 member matching/reuse coverage over the immutable C6.1 native-state index.
	/// </summary>
	[TestFixture]
	public class CollectionMemberMatchEngineTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Match_ExactAppliedBinding_ReusesInstalledInstance()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Applied, true);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.ExistingVerifiedBinding));
			Assert.That(result.MatchedNativeMod.Identity, Is.EqualTo(fixture.NativeMod.Identity));
		}

		[Test]
		public void Match_VerifiedGameRootMemberInstalledUnderData_RequiresRootCorrectingReinstall()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-root-mismatch");
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint("recipe-root");
			NormalizedCollectionMember member = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-root")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/200", null),
				recipe, "Root member", 0, CollectionMemberInstallRootBehavior.VortexDInputGameRoot);
			CollectionNativeModState nativeMod = CreateNativeMod(target, "native-root", "100", "200");
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Applied);
			var binding = new CollectionMemberBinding(association, member.IdentityResolution.Key, nativeMod.Identity, recipe,
				CollectionMemberBindingKind.InstalledForCollection);
			CollectionNativeStateIndex state = CreateState(target, new[] { nativeMod }, new[] { association }, new[] { binding },
				CollectionNativeStateCoverage.Complete);
			ResolvedCollectionPlan plan = CreatePlan(target, revision, member, state.Fingerprint);

			CollectionMemberMatchResult result = new CollectionMemberMatchEngine().Match(plan, state).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.RequiredInstallRootMismatch));
		}

		/// <summary>Installed Data bindings must supply exact archive evidence before package recognition can allow reuse.</summary>
		[Test]
		public void Match_InferredDestinationChecks_DoNotBlindlyReuseAppliedDataBinding()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Applied, true);
			var engine = new CollectionMemberMatchEngine((member, archive, cancellationToken) => true);
			CollectionMemberMatchResult pending = engine.Match(fixture.Plan, fixture.State).Members[0];
			Assert.That(pending.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.AcquisitionRequired));
			CollectionMemberMatchResult verified = engine.Match(fixture.Plan, fixture.State,
				new[] { CreateVerifiedArchive(fixture.Plan) }).Members[0];
			Assert.That(verified.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(verified.Reason, Is.EqualTo(CollectionMemberMatchReason.RequiredInstallRootMismatch));
		}

		/// <summary>Ordinary mods can reuse their installed binding after destination checks prove they do not need the game root.</summary>
		[Test]
		public void Match_ArchiveDoesNotRequireGameRoot_ReusesVerifiedDataBinding()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Applied, true);
			var engine = new CollectionMemberMatchEngine((member, archive, cancellationToken) => false);
			CollectionMemberMatchResult verified = engine.Match(fixture.Plan, fixture.State,
				new[] { CreateVerifiedArchive(fixture.Plan) }).Members[0];
			Assert.That(verified.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
		}

		/// <summary>Execution uses retained root decisions without reacquiring archives or recognizing packages again.</summary>
		[TestCase(false, CollectionMemberMatchDisposition.InstalledCompatible)]
		[TestCase(true, CollectionMemberMatchDisposition.ReinstallRequired)]
		public void Match_FrozenDestination_ReusesDataBindingOrKeepsApprovedFolderCorrection(bool gameRoot,
			CollectionMemberMatchDisposition expected)
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Applied, true);
			var engine = new CollectionMemberMatchEngine((member, archive, cancellationToken) => gameRoot, false);
			var replacement = new ResolvedCollectionPlan(fixture.Plan.Identity, fixture.Plan.Target,
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(), fixture.Plan.CurrentStateFingerprint,
				fixture.Plan.CapabilityReport, fixture.Plan.SelectedMembers);
			CollectionMemberMatchResult result = engine.MatchForReplacementExecution(replacement, fixture.State,
				new CollectionVerifiedArchive[0]).Members[0];
			Assert.AreEqual(expected, result.Disposition);
			Assert.AreEqual(fixture.NativeMod.Identity, result.MatchedNativeMod.Identity);
		}

		[Test]
		public void Match_CompatibleAppliedBindingFromAnotherAssociation_ReusesSharedInstance()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Applied, false,
				CreateOtherRevision("other-collection", "other-revision", 1));
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.CompatibleSharedVerifiedBinding));
		}

		[Test]
		public void Match_SharedNativeInstanceWithDifferentVerifiedRecipe_Blocks()
		{
			Fixture fixture = CreateFixture("other-recipe", "100", "200", CollectionAssociationState.Applied, false,
				CreateOtherRevision("other-collection", "other-revision", 1), "recipe-1");
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.ConflictingVerifiedRecipe));
		}

		[Test]
		public void Match_ExactInstalledArtifactWithoutVerifiedRecipe_RequiresReinstall()
		{
			Fixture fixture = CreateFixture(null, "100", "200", null, false);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.ExactArtifactRecipeUnverified));
		}

		[Test]
		public void Match_AlternateInstalledNexusFile_RequiresReinstall()
		{
			Fixture fixture = CreateFixture(null, "100", "201", null, false);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.AlternateArtifactInstalled));
		}

		[Test]
		public void Match_VerifiedArchiveWithoutInstalledCandidate_UsesArchiveOnlyReuse()
		{
			Fixture fixture = CreateFixture(null, null, null, null, false);
			CollectionVerifiedArchive archive = CreateVerifiedArchive(fixture.Plan);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State, new[] { archive }).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ArchiveOnlyReuse));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.VerifiedArchiveAvailable));
			Assert.That(result.VerifiedArchive, Is.SameAs(archive));
		}

		[Test]
		public void Match_NoInstalledOrVerifiedArchive_RequiresAcquisition()
		{
			Fixture fixture = CreateFixture(null, null, null, null, false);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.AcquisitionRequired));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.NoReusableInput));
		}

		/// <summary>An exact selected file wins over other files on the same page; duplicate exact registrations remain ambiguous.</summary>
		[TestCase("201", false)]
		[TestCase("200", true)]
		public void Match_MultipleInstalledCandidatesUseExactSelectedFile(string secondFileId, bool ambiguous)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-ambiguous");
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint("recipe-1");
			NormalizedCollectionMember member = CreateMember(recipe, "skyrimspecialedition/100/200");
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionNativeModState first = CreateNativeMod(target, "native-a", "100", "200");
			CollectionNativeModState second = CreateNativeMod(target, "native-b", "100", secondFileId);
			CollectionNativeStateIndex state = CreateState(target, new[] { first, second }, null, null,
				CollectionNativeStateCoverage.Complete);
			ResolvedCollectionPlan plan = CreatePlan(target, revision, member, state.Fingerprint);

			CollectionMemberMatchResult result = new CollectionMemberMatchEngine().Match(plan, state).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(ambiguous ? CollectionMemberMatchDisposition.Blocked : CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(result.Reason, Is.EqualTo(ambiguous ? CollectionMemberMatchReason.AmbiguousInstalledCandidates : CollectionMemberMatchReason.ExactArtifactRecipeUnverified));
			Assert.That(result.NativeCandidates.Count, Is.EqualTo(ambiguous ? 2 : 1));
			if (!ambiguous) Assert.That(result.NativeCandidates.Single().Identity, Is.EqualTo(first.Identity));
		}

		/// <summary>Interrupted Collections keep independently selected main/patch archives separate, including not-yet-installed siblings.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void Match_PartialCollectionDoesNotConfuseSelectedSiblingFiles(bool siblingInstalled)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-sibling-recovery");
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionRecipeIdentity mainRecipe = CollectionRecipeIdentity.FromFingerprint("recipe-main");
			CollectionRecipeIdentity patchRecipe = CollectionRecipeIdentity.FromFingerprint("recipe-patch");
			NormalizedCollectionMember main = CreateMember(mainRecipe, "skyrimspecialedition/100/200");
			var patch = new NormalizedCollectionMember(1,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-patch")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/201", null), patchRecipe, "Patch");
			var association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Incomplete);
			CollectionNativeModState mainMod = CreateNativeMod(target, "native-main", "100", "200");
			CollectionNativeModState patchMod = CreateNativeMod(target, "native-patch", "100", "201");
			var mainBinding = new CollectionMemberBinding(association, main.IdentityResolution.Key, mainMod.Identity, mainRecipe, CollectionMemberBindingKind.InstalledForCollection);
			var patchBinding = new CollectionMemberBinding(association, patch.IdentityResolution.Key, patchMod.Identity, patchRecipe, CollectionMemberBindingKind.InstalledForCollection);
			var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0],
				siblingInstalled ? new[] { mainMod, patchMod } : new[] { mainMod },
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new[] { association },
				siblingInstalled ? new[] { mainBinding, patchBinding } : new[] { mainBinding }, new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0, new[] { association.AssociationId });
			ResolvedCollectionPlan plan = CreatePlan(target, revision, main, state.Fingerprint, additionalMembers: new[] { patch });
			CollectionMemberMatchSet matched = new CollectionMemberMatchEngine().Match(plan, state);
			Assert.That(matched.HasBlockedMembers, Is.False);
			Assert.That(matched.Members.Single(x => x.Member.MemberKey.Equals(main.IdentityResolution.Key)).Disposition,
				Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
			CollectionMemberMatchResult patchMatch = matched.Members.Single(x => x.Member.MemberKey.Equals(patch.IdentityResolution.Key));
			Assert.That(patchMatch.Disposition, Is.EqualTo(siblingInstalled ? CollectionMemberMatchDisposition.InstalledCompatible : CollectionMemberMatchDisposition.AcquisitionRequired));
			Assert.That(patchMatch.NativeCandidates.Count, Is.EqualTo(siblingInstalled ? 1 : 0));
		}

		[Test]
		public void Match_ModifiedAssociation_DoesNotCountHistoricalBindingAsCompatible()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Modified, true);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.AssociationNotApplied));
		}

		[Test]
		public void Match_RecoveringAssociation_BlocksUntilRecoveryReconciliation()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "200", CollectionAssociationState.Recovering, true);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.AssociationRequiresRecovery));
		}

		/// <summary>Only the exact reconciled partial revision can reuse its completed bindings.</summary>
		[TestCase(CollectionAssociationState.Incomplete, true, true, true)]
		[TestCase(CollectionAssociationState.Incomplete, false, true, false)]
		[TestCase(CollectionAssociationState.Incomplete, true, false, false)]
		[TestCase(CollectionAssociationState.Recovering, true, true, false)]
		public void Match_PartialAssociationScopeIsRestricted(CollectionAssociationState associationState, bool scoped, bool sameRevision, bool reusable)
		{
			Fixture f = CreateFixture("recipe-1", "100", "200", associationState, sameRevision);
			CollectionTargetAssociation association = f.State.Associations.Values.Single();
			var state = new CollectionNativeStateIndex(f.State.Target, f.State.Roots, f.State.Mods.Values,
				f.State.Files.Values, f.State.IniEdits.Values, f.State.GameValues.Values, f.State.Plugins.Values,
				f.State.PluginCoverage, f.State.Associations.Values, f.State.BindingsByAssociation.Values.SelectMany(x => x)
					.Select(x => new CollectionMemberBinding(x.Association, x.MemberKey, x.NativeMod, x.VerifiedRecipe, CollectionMemberBindingKind.InstalledForCollection)),
				new UserOverride[0], f.State.AssociationCoverage, f.State.Issues, f.State.DeploymentCommitSequence,
				scoped ? new[] { association.AssociationId } : new Guid[0]);
			ResolvedCollectionPlan plan = CreatePlan(state.Target, f.Plan.Revision, f.NormalizedMember, state.Fingerprint);
			CollectionMemberMatchSet matched = new CollectionMemberMatchEngine().Match(plan, state);
			CollectionMemberMatchResult result = matched.Members[0];
			Assert.AreEqual(reusable ? CollectionMemberMatchDisposition.InstalledCompatible : CollectionMemberMatchDisposition.Blocked, result.Disposition);
			Assert.AreEqual(reusable, matched.HasReusableVerifiedBinding(state.BindingsByAssociation.Values.Single().Single(),
				f.NativeMod.Identity, plan.SelectedMembers.Single().RecipeIdentity));
			Assert.AreEqual(associationState, association.State);
		}

		[Test]
		public void Match_ReplacementPolicy_IsDeferredBeyondC6AdditiveMatcher()
		{
			Fixture fixture = CreateFixture(null, null, null, null, false);
			ResolvedCollectionPlan replacement = CreatePlan(fixture.Plan.Target, fixture.Plan.Revision,
				fixture.NormalizedMember, fixture.State.Fingerprint, null,
				CollectionExecutionPolicy.ReplaceCurrentManagedSetup(CollectionReplacementBackupChoice.ContinueWithoutLocalCollection));

			CollectionMemberMatchResult result = new CollectionMemberMatchEngine().Match(replacement, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.UnsupportedExecutionPolicy));
		}

		[Test]
		public void Match_MissingNativeInstanceReferencedByBinding_Blocks()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-missing-native");
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint("recipe-1");
			NormalizedCollectionMember member = CreateMember(recipe, "skyrimspecialedition/100/200");
			CollectionTargetAssociation association = new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Applied);
			CollectionMemberBinding binding = new CollectionMemberBinding(association, member.IdentityResolution.Key,
				new NativeModInstanceIdentity(target, "missing-native"), recipe, CollectionMemberBindingKind.InstalledForCollection);
			CollectionNativeStateIndex state = CreateState(target, new CollectionNativeModState[0],
				new[] { association }, new[] { binding }, CollectionNativeStateCoverage.Complete);
			ResolvedCollectionPlan plan = CreatePlan(target, revision, member, state.Fingerprint);

			CollectionMemberMatchResult result = new CollectionMemberMatchEngine().Match(plan, state).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.MissingBoundNativeMod));
		}

		[Test]
		public void Match_DirectBindingWhoseNativeArtifactContradictsPlan_Blocks()
		{
			Fixture fixture = CreateFixture("recipe-1", "100", "201", CollectionAssociationState.Applied, true);
			CollectionMemberMatchResult result = new CollectionMemberMatchEngine()
				.Match(fixture.Plan, fixture.State).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.BoundNativeArtifactMismatch));
		}

		[Test]
		public void Match_UnsupportedArtifactIdentity_BlocksWithoutGuessing()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-unsupported-artifact");
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint("recipe-unsupported");
			NormalizedCollectionMember member = CreateMemberWithArtifact(recipe,
				new CollectionArtifactReference("manual-file", "opaque-artifact", null));
			CollectionNativeStateIndex state = CreateState(target, new CollectionNativeModState[0], null, null,
				CollectionNativeStateCoverage.Complete);
			ResolvedCollectionPlan plan = CreatePlan(target, revision, member, state.Fingerprint);

			CollectionMemberMatchResult result = new CollectionMemberMatchEngine().Match(plan, state).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.UnsupportedArtifactIdentity));
		}

		[Test]
		public void Match_StaleStateFingerprint_BlocksEveryMember()
		{
			Fixture fixture = CreateFixture(null, null, null, null, false);
			ResolvedCollectionPlan stalePlan = CreatePlan(fixture.Plan.Target, fixture.Plan.Revision,
				fixture.NormalizedMember, new CollectionCurrentStateFingerprint("collection-native-state-v1", "stale"));

			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(stalePlan, fixture.State);
			CollectionMemberMatchResult result = matches.Members[0];

			Assert.That(matches.StateFingerprint, Is.EqualTo(stalePlan.CurrentStateFingerprint));
			Assert.That(matches.StateFingerprint, Is.Not.EqualTo(fixture.State.Fingerprint));
			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.CurrentStateChanged));
		}

		[Test]
		public void Match_CurrentStateFingerprint_BindsResultToApprovedPlanState()
		{
			Fixture fixture = CreateFixture(null, null, null, null, false);

			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(fixture.Plan, fixture.State);

			Assert.That(matches.StateFingerprint, Is.EqualTo(fixture.Plan.CurrentStateFingerprint));
			Assert.That(matches.StateFingerprint, Is.EqualTo(fixture.State.Fingerprint));
		}

		[Test]
		public void Match_UnavailableAssociationState_BlocksReusePlanning()
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-association-unavailable");
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint("recipe-1");
			NormalizedCollectionMember member = CreateMember(recipe, "skyrimspecialedition/100/200");
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionNativeStateIndex state = CreateState(target, new CollectionNativeModState[0], null, null,
				CollectionNativeStateCoverage.Unavailable);
			ResolvedCollectionPlan plan = CreatePlan(target, revision, member, state.Fingerprint);

			CollectionMemberMatchResult result = new CollectionMemberMatchEngine().Match(plan, state).Members[0];

			Assert.That(result.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.Blocked));
			Assert.That(result.Reason, Is.EqualTo(CollectionMemberMatchReason.AssociationStateUnavailable));
		}

		[Test]
		public void Match_VerifiedArchiveFromDifferentPlan_IsRejected()
		{
			Fixture first = CreateFixture(null, null, null, null, false);
			Fixture second = CreateFixture(null, null, null, null, false, null, null, Guid.NewGuid());
			CollectionVerifiedArchive wrongArchive = CreateVerifiedArchive(second.Plan);

			Assert.Throws<ArgumentException>(() => new CollectionMemberMatchEngine()
				.Match(first.Plan, first.State, new[] { wrongArchive }));
		}

		private static Fixture CreateFixture(string bindingRecipe, string nativeModId, string nativeFileId,
			CollectionAssociationState? associationState, bool associationUsesPlanRevision,
			CollectionRevisionIdentity associationRevision = null, string planRecipe = null, Guid? planId = null)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c6-2-" + Guid.NewGuid().ToString("N"));
			CollectionRevisionIdentity revision = CreateRevision();
			CollectionRecipeIdentity recipe = CollectionRecipeIdentity.FromFingerprint(planRecipe ?? "recipe-1");
			NormalizedCollectionMember member = CreateMember(recipe, "skyrimspecialedition/100/200");
			CollectionNativeModState nativeMod = nativeModId == null ? null : CreateNativeMod(target, "native-1", nativeModId, nativeFileId);
			CollectionTargetAssociation association = null;
			CollectionMemberBinding binding = null;
			if (bindingRecipe != null)
			{
				CollectionRevisionIdentity bindingRevision = associationUsesPlanRevision
					? revision
					: associationRevision ?? CreateOtherRevision("other-collection", "other-revision", 1);
				association = new CollectionTargetAssociation(Guid.NewGuid(), bindingRevision, target,
					associationState ?? CollectionAssociationState.Applied);
				binding = new CollectionMemberBinding(association,
					associationUsesPlanRevision ? member.IdentityResolution.Key : CollectionMemberKey.FromProvider("other-member"),
					nativeMod == null ? new NativeModInstanceIdentity(target, "native-1") : nativeMod.Identity,
					CollectionRecipeIdentity.FromFingerprint(bindingRecipe), CollectionMemberBindingKind.AdoptedExisting);
			}

			CollectionNativeStateIndex state = CreateState(target,
				nativeMod == null ? new CollectionNativeModState[0] : new[] { nativeMod },
				association == null ? null : new[] { association }, binding == null ? null : new[] { binding },
				CollectionNativeStateCoverage.Complete);
			ResolvedCollectionPlan plan = CreatePlan(target, revision, member, state.Fingerprint, planId);
			return new Fixture(plan, state, member, nativeMod);
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target,
			CollectionNativeModState[] mods, CollectionTargetAssociation[] associations,
			CollectionMemberBinding[] bindings, CollectionNativeStateCoverage associationCoverage)
		{
			return new CollectionNativeStateIndex(target,
				new CollectionNativeRootState[0], mods ?? new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable,
				associations ?? new CollectionTargetAssociation[0], bindings ?? new CollectionMemberBinding[0],
				new UserOverride[0], associationCoverage, new CollectionNativeStateIssue[0], 0);
		}

		private static ResolvedCollectionPlan CreatePlan(CollectionTargetIdentity target, CollectionRevisionIdentity revision,
			NormalizedCollectionMember member, CollectionCurrentStateFingerprint fingerprint, Guid? planId = null,
			CollectionExecutionPolicy policy = null, NormalizedCollectionMember[] additionalMembers = null)
		{
			NormalizedCollectionMember[] members = new[] { member }.Concat(additionalMembers ?? new NormalizedCollectionMember[0]).ToArray();
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(revision, source,
				CollectionManifestMemberSetCompleteness.Complete, null, members);
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			return new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(planId ?? Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"), 1),
				target, policy ?? CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint, report,
				members.Select(x => new ResolvedCollectionMemberPlan(x, CollectionResolvedArtifactChoice.Exact(x.Artifact))));
		}

		private static NormalizedCollectionMember CreateMember(CollectionRecipeIdentity recipe, string stableArtifactId)
		{
			return CreateMemberWithArtifact(recipe, new CollectionArtifactReference("nexus-mod-file", stableArtifactId, null));
		}

		private static NormalizedCollectionMember CreateMemberWithArtifact(CollectionRecipeIdentity recipe, CollectionArtifactReference artifact)
		{
			return new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-1")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected, artifact, recipe, "Member 1");
		}

		private static CollectionNativeModState CreateNativeMod(CollectionTargetIdentity target, string nativeKey,
			string nexusModId, string nexusFileId)
		{
			return new CollectionNativeModState(new NativeModInstanceIdentity(target, nativeKey),
				"C:\\Mods\\Example.7z", "Example.7z", nexusModId, nexusFileId, "1.0", "1.0.0.0",
				ModInstallRoot.Data, ModInstallMethod.Virtual);
		}

		private static CollectionRevisionIdentity CreateRevision()
		{
			return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("collection-1"), "revision-1", 1);
		}

		private static CollectionRevisionIdentity CreateOtherRevision(string collectionSlug, string revisionId, long revisionNumber)
		{
			return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus(collectionSlug), revisionId, revisionNumber);
		}

		private static CollectionVerifiedArchive CreateVerifiedArchive(ResolvedCollectionPlan plan)
		{
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, plan.SelectedMembers[0].MemberKey);
			var artifact = new CollectionsRetainedArtifact("artifact-c6-2", CollectionContentHash.FromSha256(Sha256A), 100);
			var reference = new CollectionsRetainedArtifactReferenceRecord("reference-c6-2", artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "verified-c6-2");
			return new CollectionVerifiedArchive(request, artifact, reference,
				CollectionVerifiedArchiveSourceKind.RetainedContent, CollectionArchiveVerificationBasis.ExistingVerifiedReference);
		}

		private sealed class Fixture
		{
			public Fixture(ResolvedCollectionPlan plan, CollectionNativeStateIndex state,
				NormalizedCollectionMember normalizedMember, CollectionNativeModState nativeMod)
			{
				Plan = plan;
				State = state;
				NormalizedMember = normalizedMember;
				NativeMod = nativeMod;
			}

			public ResolvedCollectionPlan Plan { get; }
			public CollectionNativeStateIndex State { get; }
			public NormalizedCollectionMember NormalizedMember { get; }
			public CollectionNativeModState NativeMod { get; }
		}
	}
}
