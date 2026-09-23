using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.15.12 reviewed-workflow executable runtime reconstruction coverage.</summary>
	[TestFixture]
	public class CollectionReviewedWorkflowRuntimeReconstructorTests
	{
		[Test]
		public void Reconstruct_RoundTripsExactSimpleRecipeAndReviewedPlanningObjects()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				CollectionReviewedWorkflowSnapshot roundTrip = CollectionReviewedWorkflowSnapshotCodec.Deserialize(
					CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
				Assert.AreEqual(1, roundTrip.PreparedRecipes.Single().SimpleFileMappings.Count);
				Assert.AreEqual("textures\\source.dds", roundTrip.PreparedRecipes.Single().SimpleFileMappings[0].SourcePath);
				Assert.AreEqual("textures\\installed.dds", roundTrip.PreparedRecipes.Single().SimpleFileMappings[0].DestinationPath);

				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					roundTrip, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				CollectionReviewedWorkflowRuntime runtime = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.Reconstruct(rehydration);

				Assert.AreEqual(fixture.Plan.Identity, runtime.Plan.Identity);
				Assert.AreEqual(fixture.Plan.CurrentStateFingerprint, runtime.Plan.CurrentStateFingerprint);
				Assert.AreEqual(CollectionMemberMatchDisposition.ArchiveOnlyReuse, runtime.Matches.Members.Single().Disposition);
				Assert.IsTrue(runtime.DependencyPlan.IsReady);
				Assert.IsTrue(runtime.ImpactPlan.IsReady);
				Assert.AreEqual(fixture.VerifiedArchive.Artifact.ArtifactId, runtime.GetVerifiedArchive(fixture.Member.MemberKey).Artifact.ArtifactId);
				PreparedCollectionNativeRecipe prepared = runtime.GetPreparedRecipe(fixture.Member.MemberKey);
				Assert.IsNotNull(prepared);
				Assert.AreEqual(fixture.PreparedRecipe.PreparedNativeIdentity, prepared.PreparedNativeIdentity);
				Assert.AreEqual(1, prepared.RecipeInput.NativeOperations.Count);
				InstallModFileOperation operation = prepared.RecipeInput.NativeOperations.Single() as InstallModFileOperation;
				Assert.IsNotNull(operation);
				Assert.AreEqual("textures\\source.dds", operation.SourcePath);
				Assert.AreEqual("textures\\installed.dds", operation.DestinationPath);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReconstructPlan_ValidatesRetainedSourceWithoutClaimingSafeBoundaryResume()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });

				ResolvedCollectionPlan reconstructed = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.ReconstructPlan(snapshot);

				Assert.AreEqual(fixture.Plan.Identity, reconstructed.Identity);
				Assert.AreEqual(fixture.Plan.Revision, reconstructed.Revision);
				Assert.AreEqual(fixture.Plan.Target, reconstructed.Target);
				Assert.AreEqual(fixture.Plan.CurrentStateFingerprint, reconstructed.CurrentStateFingerprint);
				Assert.AreEqual(fixture.Member.MemberKey, reconstructed.SelectedMembers.Single().MemberKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RehydrateThenReconstruct_RestoresExecutableReviewedRuntimeFromDurableStores()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				var operationStore = new CollectionsOperationStore(fixture.Store);
				var planStore = new CollectionsResolvedPlanStore(fixture.Store);
				var coordinator = new CollectionOperationCoordinator(operationStore, planStore);
				CollectionOperation operation = coordinator.CreateApplyOperation(fixture.Revision.Identity.Collection, fixture.Target);
				operation = coordinator.BeginResolving(operation.Identity);
				operation = coordinator.BeginPreparing(operation.Identity, fixture.Revision.Identity);
				operation = coordinator.MarkReadyForReview(operation.Identity, fixture.Plan, fixture.DependencyPlan,
					fixture.ImpactPlan, new[] { fixture.PreparedRecipe });

				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				var artifactStore = new CollectionsRetainedArtifactStore(fixture.Store);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore,
					revisionIdentity => sourceStore.GetSource(revisionIdentity),
					(revisionIdentity, expected) => sourceStore.LoadManifest(revisionIdentity, expected),
					artifactId =>
					{
						CollectionsRetainedArtifact artifact = artifactStore.GetArtifact(artifactId);
						if (artifact == null || !artifactStore.VerifyArtifact(artifactId))
							throw new InvalidDataException("Missing retained artifact.");
						return artifact;
					},
					targetIdentity => fixture.State);

				CollectionReviewedWorkflowRehydrationResult rehydration = rehydrator.Rehydrate(operation.Identity);
				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.Ready, rehydration.Status);
				CollectionReviewedWorkflowRuntime runtime = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.Reconstruct(rehydration);

				Assert.AreEqual(fixture.Plan.Identity, runtime.Plan.Identity);
				Assert.AreEqual(1, runtime.RemainingMembers.Count);
				Assert.AreEqual(fixture.Member.MemberKey, runtime.RemainingMembers[0]);
				Assert.IsNotNull(runtime.GetPreparedRecipe(fixture.Member.MemberKey));
				Assert.IsNotNull(runtime.GetVerifiedArchive(fixture.Member.MemberKey));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Rehydrate_MissingExecutableMappingDescriptorRequiresRepreparation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionReviewedWorkflowSnapshot valid = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				CollectionReviewedPreparedRecipeSnapshot recipe = valid.PreparedRecipes.Single();
				var incompleteRecipe = new CollectionReviewedPreparedRecipeSnapshot(recipe.MemberKey, recipe.SourceOrdinal,
					recipe.ProviderRecipeFingerprint, recipe.PreparedNativeFingerprint, recipe.SkipReadmeFiles,
					recipe.Validation, recipe.EffectPreview, recipe.RetainedArtifactIds);
				var incomplete = new CollectionReviewedWorkflowSnapshot(valid.Identity, valid.Revision, valid.Target,
					valid.PolicyKind, valid.ReplacementBackupChoice, valid.ApprovedStateFingerprint, valid.ManifestSource,
					valid.Members, valid.Dependencies, valid.PriorityRules, valid.Phases, valid.Barriers, valid.FileImpacts,
					valid.PluginImpacts, valid.ConfigurationImpacts, valid.AssociationImpacts, new[] { incompleteRecipe });

				var operationStore = new CollectionsOperationStore(fixture.Store);
				var planStore = new CollectionsResolvedPlanStore(fixture.Store);
				var coordinator = new CollectionOperationCoordinator(operationStore, planStore);
				CollectionOperation operation = coordinator.CreateApplyOperation(fixture.Revision.Identity.Collection, fixture.Target);
				operation = coordinator.BeginResolving(operation.Identity);
				operation = coordinator.BeginPreparing(operation.Identity, fixture.Revision.Identity);
				planStore.SavePlan(fixture.Plan, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat,
					CollectionReviewedWorkflowSnapshotCodec.Serialize(incomplete));
				operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, fixture.Plan.Identity, operation.CheckpointSequence + 1, CollectionOperationPhase.ReadyForReview,
					CollectionOperationResultState.Pending, operation.NativeChildren);
				operationStore.SaveOperation(operation);

				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				var artifactStore = new CollectionsRetainedArtifactStore(fixture.Store);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore,
					revisionIdentity => sourceStore.GetSource(revisionIdentity),
					(revisionIdentity, expected) => sourceStore.LoadManifest(revisionIdentity, expected),
					artifactId =>
					{
						CollectionsRetainedArtifact artifact = artifactStore.GetArtifact(artifactId);
						if (artifact == null || !artifactStore.VerifyArtifact(artifactId))
							throw new InvalidDataException("Missing retained artifact.");
						return artifact;
					},
					targetIdentity => fixture.State);
				CollectionReviewedWorkflowRehydrationResult result = rehydrator.Rehydrate(operation.Identity);

				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, result.Status);
				StringAssert.Contains("mapping", result.Message.ToLowerInvariant());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Fixture CreateFixture(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus("c61512-runtime");
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-runtime", 12),
				"Revision", null, 1);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(new CollectionDefinition(collection, "Runtime", null, null), revision);

			string json = "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Example\",\"description\":\"Example\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"updatePolicy\":\"exact\"}}]," +
				"\"modRules\":[]}";
			byte[] manifestBytes = Encoding.UTF8.GetBytes(json);
			NexusCollectionManifestNormalizationResult normalization = new NexusCollectionManifestNormalizer().Normalize(manifestBytes, revision);
			Assert.AreEqual(CollectionCompatibilityStatus.Supported, normalization.CapabilityReport.Status);
			CollectionRevisionSourceRecord source = new CollectionsRevisionSourceStore(store).RetainManifest(normalization.Manifest,
				CollectionRevisionSourceInputKind.RawManifest, normalization.Manifest.Source.ContentHash, manifestBytes.LongLength,
				"collection.json", manifestBytes);

			NormalizedCollectionMember normalized = normalization.Manifest.Members.Single();
			var member = new ResolvedCollectionMemberPlan(normalized, CollectionResolvedArtifactChoice.Exact(normalized.Artifact));
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-runtime-" + Guid.NewGuid().ToString("N"));
			var state = new CollectionNativeStateIndex(target, new CollectionNativeRootState[0], new CollectionNativeModState[0],
				new CollectionNativeFileState[0], new CollectionNativeIniState[0], new CollectionNativeGameValueState[0],
				new CollectionNativePluginState[0], CollectionNativeStateCoverage.NotApplicable, new CollectionTargetAssociation[0],
				new CollectionMemberBinding[0], new UserOverride[0], CollectionNativeStateCoverage.Complete,
				new CollectionNativeStateIssue[0], 0);
			var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), target,
				CollectionExecutionPolicy.InstallIntoCurrentSetup(), state.Fingerprint, normalization.CapabilityReport, new[] { member });

			byte[] archiveBytes = Encoding.UTF8.GetBytes("c6.15.12 runtime archive");
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(archiveBytes, false)) artifact = new CollectionsRetainedArtifactStore(store).Publish(stream);
			Guid requestId = CollectionMemberAcquisitionCoordinator.CreateStableRequestId(plan.Identity, member.MemberKey);
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(requestId, plan, member.MemberKey);
			CollectionsRetainedArtifactReferenceRecord reference = new CollectionsRetainedArtifactReferenceStore(store).AcquireReference(
				artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, requestId.ToString("D"),
				CollectionVerifiedArchiveAdopter.CreateReferenceRole(request.SelectedArtifact));
			var verifiedArchive = new CollectionVerifiedArchive(request, artifact, reference,
				CollectionVerifiedArchiveSourceKind.RetainedContent, CollectionArchiveVerificationBasis.ExistingVerifiedReference);

			var match = new CollectionMemberMatchResult(member, CollectionMemberMatchDisposition.ArchiveOnlyReuse,
				CollectionMemberMatchReason.VerifiedArchiveAvailable, new CollectionNativeModState[0], new CollectionMemberBinding[0], verifiedArchive);
			var matches = new CollectionMemberMatchSet(plan, state, new[] { match });
			var phase = new CollectionExecutionPhase(0, new[] { new CollectionPlannedPhaseMember(match) });
			var dependencyPlan = new CollectionDependencyPhasePlan(plan, matches, new[] { phase }, new CollectionPhaseBarrier[0],
				new CollectionDependencyPhaseIssue[0]);

			var targetFile = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\installed.dds");
			var preview = new CollectionMemberEffectPreview(member.MemberKey, member.RecipeIdentity, ModInstallMethod.Virtual,
				ModInstallRoot.Data, new[] { new CollectionPlannedFileEffect(targetFile) }, new CollectionPlannedIniEffect[0],
				new CollectionPlannedGameValueEffect[0], new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
			var impactPlan = new CollectionConflictImpactPlan(plan, state, new[] { new CollectionFileImpact(targetFile,
				new[] { member.MemberKey }, member.MemberKey, null, new Guid[0]) }, new CollectionPluginImpact[0],
				new CollectionConfigurationImpact[0], new CollectionAssociationImpact[0], new CollectionConflictImpactIssue[0]);

			var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
				ModInstallationSimpleFileRecipeAdapter.AdapterVersion, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data),
				new ModInstallationRecipeExpectedContent(artifact.ContentHash.Value, artifact.ByteLength),
				new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId, ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
				new[] { new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "textures\\source.dds"),
					new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "textures\\installed.dds") });
			var nativeIdentity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(target.Fingerprint, validation.InstallContext, member.RecipeIdentity.Fingerprint));
			ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(
				new ModInstallationRecipeInput(nativeIdentity, validation), new ModInstallationSimpleFileRecipe(new[] {
					new ModInstallationSimpleFileMapping("textures\\source.dds", "textures\\installed.dds") }));
			var prepared = new PreparedCollectionNativeRecipe(member, PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-runtime"),
				translated, preview, false, new[] { source.RawManifestArtifactId, artifact.ArtifactId });

			return new Fixture(store, revision, target, state, plan, member, dependencyPlan, impactPlan, prepared, verifiedArchive);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c61512-runtime-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private sealed class Fixture
		{
			public Fixture(CollectionsStore store, CollectionRevision revision, CollectionTargetIdentity target,
				CollectionNativeStateIndex state, ResolvedCollectionPlan plan, ResolvedCollectionMemberPlan member,
				CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
				PreparedCollectionNativeRecipe preparedRecipe, CollectionVerifiedArchive verifiedArchive)
			{
				Store = store; Revision = revision; Target = target; State = state; Plan = plan; Member = member;
				DependencyPlan = dependencyPlan; ImpactPlan = impactPlan; PreparedRecipe = preparedRecipe; VerifiedArchive = verifiedArchive;
			}
			public CollectionsStore Store { get; }
			public CollectionRevision Revision { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionNativeStateIndex State { get; }
			public ResolvedCollectionPlan Plan { get; }
			public ResolvedCollectionMemberPlan Member { get; }
			public CollectionDependencyPhasePlan DependencyPlan { get; }
			public CollectionConflictImpactPlan ImpactPlan { get; }
			public PreparedCollectionNativeRecipe PreparedRecipe { get; }
			public CollectionVerifiedArchive VerifiedArchive { get; }
		}
	}
}
