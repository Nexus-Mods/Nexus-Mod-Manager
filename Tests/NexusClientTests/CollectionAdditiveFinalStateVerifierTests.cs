using System;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>Aggregate authoritative-state verification coverage before additive Applied publication.</summary>
	[TestFixture]
	[Category("CollectionsGateA")]
	public class CollectionAdditiveFinalStateVerifierTests
	{
		private const string Sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Verify_MutatingMemberWithExpectedOwnerAndWinner_Succeeds()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionNativeStateIndex finalState = CreateFinalState(fixture, fixture.NativeMod.Identity.NativeModKey);
				Assert.DoesNotThrow(() => new CollectionAdditiveFinalStateVerifier().Verify(fixture.Plan, fixture.Matches,
					fixture.ImpactPlan, new[] { fixture.PreparedRecipe }, finalState));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Verify_FileWinnerChangedAfterChildVerification_BlocksAppliedPublication()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionNativeStateIndex finalState = CreateFinalState(fixture, "unexpected-owner");
				InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
					new CollectionAdditiveFinalStateVerifier().Verify(fixture.Plan, fixture.Matches,
						fixture.ImpactPlan, new[] { fixture.PreparedRecipe }, finalState));
				StringAssert.Contains("winner", error.Message.ToLowerInvariant());
			}
			finally { Directory.Delete(root, true); }
		}

		private static Fixture CreateFixture(string root)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-final-" + Guid.NewGuid().ToString("N"));
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus("collection-final"), "revision-1", 1);
			CollectionRecipeIdentity recipeIdentity = CollectionRecipeIdentity.FromFingerprint("recipe-final");
			var normalized = new NormalizedCollectionMember(0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-final")),
				CollectionMemberRequirement.Required, CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", "skyrimspecialedition/100/200", null), recipeIdentity, "Member");
			var source = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(Sha256), 10, "schema-v1", "normalizer-v1");
			var manifest = new NormalizedCollectionManifest(revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { normalized });
			CollectionNativeStateIndex initialState = CreateState(target, new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionTargetAssociation[0], new CollectionMemberBinding[0]);
			var resolvedMember = new ResolvedCollectionMemberPlan(normalized, CollectionResolvedArtifactChoice.Exact(normalized.Artifact));
			var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), initialState.Fingerprint, CollectionCapabilityReport.Create(manifest),
				new[] { resolvedMember });

			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, resolvedMember.MemberKey);
			var artifact = new CollectionsRetainedArtifact("artifact-final", CollectionContentHash.FromSha256(Sha256), 10);
			var reference = new CollectionsRetainedArtifactReferenceRecord("reference-final", artifact.ArtifactId,
				CollectionsRetainedArtifactOwnerKind.Download, request.RequestId.ToString("D"), "verified-final");
			var archive = new CollectionVerifiedArchive(request, artifact, reference, CollectionVerifiedArchiveSourceKind.RetainedContent,
				CollectionArchiveVerificationBasis.ExistingVerifiedReference);
			CollectionMemberMatchSet matches = new CollectionMemberMatchEngine().Match(plan, initialState, new[] { archive });
			Assert.AreEqual(CollectionMemberMatchDisposition.ArchiveOnlyReuse, matches.Members[0].Disposition);

			ModDeploymentTarget fileTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"planned\member.txt");
			var preview = new CollectionMemberEffectPreview(resolvedMember.MemberKey, recipeIdentity, ModInstallMethod.Virtual,
				ModInstallRoot.Data, new[] { new CollectionPlannedFileEffect(fileTarget) }, new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			CollectionDependencyPhasePlan dependency = new CollectionDependencyPhasePlanner().Plan(plan, matches);
			CollectionConflictImpactPlan impact = new CollectionConflictImpactPlanner().Plan(plan, matches, dependency, initialState, new[] { preview });
			Assert.IsTrue(impact.IsReady);

			var context = new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data);
			var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion, context,
				new ModInstallationRecipeExpectedContent(Sha256, 10),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
				new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, @"content\member.txt"),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, @"planned\member.txt") });
			var operation = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, context, recipeIdentity.Fingerprint));
			var input = new ModInstallationRecipeInput(operation, validation);
			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(input,
				new ModInstallationSimpleFileRecipe(new[] { new ModInstallationSimpleFileMapping(@"content\member.txt", @"planned\member.txt") }));
			var prepared = new PreparedCollectionNativeRecipe(resolvedMember,
				PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-final"), translated, preview, false,
				new[] { artifact.ArtifactId });
			var nativeMod = new CollectionNativeModState(new NativeModInstanceIdentity(target, "native-final"),
				Path.Combine(root, "member.7z"), "member.7z", "100", "200", "1.0", "1.0", false,
				ModInstallRoot.Data, ModInstallMethod.Virtual);
			return new Fixture(root, plan, matches, impact, prepared, nativeMod, fileTarget);
		}

		private static CollectionNativeStateIndex CreateFinalState(Fixture fixture, string effectiveOwner)
		{
			string physical = Path.Combine(fixture.Root, "member.txt");
			File.WriteAllText(physical, "content");
			var association = new CollectionTargetAssociation(Guid.NewGuid(), fixture.Plan.Revision, fixture.Plan.Target,
				CollectionAssociationState.Incomplete);
			var binding = new CollectionMemberBinding(association, fixture.Plan.SelectedMembers[0].MemberKey, fixture.NativeMod.Identity,
				fixture.Plan.SelectedMembers[0].RecipeIdentity, CollectionMemberBindingKind.InstalledForCollection);
			var owner = new CollectionNativeOwnerState(fixture.NativeMod.Identity.NativeModKey, null, CollectionNativeOwnerKind.NativeMod,
				true, 0, physical);
			var file = new CollectionNativeFileState(fixture.FileTarget, physical, false, false, true, effectiveOwner,
				new CollectionNativeOwnerState[0], new CollectionNativeOwnerState[0], new[] { owner });
			return CreateState(fixture.Plan.Target, new[] { fixture.NativeMod }, new[] { file }, new[] { association }, new[] { binding });
		}

		private static CollectionNativeStateIndex CreateState(CollectionTargetIdentity target, CollectionNativeModState[] mods,
			CollectionNativeFileState[] files, CollectionTargetAssociation[] associations, CollectionMemberBinding[] bindings)
		{
			return new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], mods, files,
				new CollectionNativeIniState[0], new CollectionNativeGameValueState[0], new CollectionNativePluginState[0],
				CollectionNativeStateCoverage.NotApplicable, associations, bindings, new UserOverride[0],
				CollectionNativeStateCoverage.Complete, new CollectionNativeStateIssue[0], 0);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-final-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			internal Fixture(string root, ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
				CollectionConflictImpactPlan impactPlan, PreparedCollectionNativeRecipe preparedRecipe,
				CollectionNativeModState nativeMod, ModDeploymentTarget fileTarget)
			{
				Root = root; Plan = plan; Matches = matches; ImpactPlan = impactPlan; PreparedRecipe = preparedRecipe;
				NativeMod = nativeMod; FileTarget = fileTarget;
			}
			internal string Root { get; }
			internal ResolvedCollectionPlan Plan { get; }
			internal CollectionMemberMatchSet Matches { get; }
			internal CollectionConflictImpactPlan ImpactPlan { get; }
			internal PreparedCollectionNativeRecipe PreparedRecipe { get; }
			internal CollectionNativeModState NativeMod { get; }
			internal ModDeploymentTarget FileTarget { get; }
		}
	}
}
