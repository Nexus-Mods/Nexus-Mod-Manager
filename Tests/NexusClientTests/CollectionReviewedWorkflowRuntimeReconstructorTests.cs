using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C6.15.12 reviewed-workflow executable runtime reconstruction coverage.</summary>
	[TestFixture]
	public class CollectionReviewedWorkflowRuntimeReconstructorTests
	{
		/// <summary>The opt-in survives saved review reconstruction and cannot change because a global preference changed after review.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void UnlistedPluginPreference_RoundTripsReviewedRuntime(bool enabled)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, enabled);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				byte[] bytes = CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot);
				CollectionReviewedWorkflowSnapshot restored = CollectionReviewedWorkflowSnapshotCodec.Deserialize(bytes);
				Assert.That(restored.EnableUnspecifiedPlugins, Is.EqualTo(enabled));
				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					restored, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				CollectionReviewedWorkflowRuntime runtime = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store).Reconstruct(rehydration);
				Assert.That(runtime.Plan.Policy.EnableUnspecifiedPlugins, Is.EqualTo(enabled));
				Assert.Throws<InvalidDataException>(() => CollectionReviewedWorkflowSnapshotCodec.Deserialize(bytes.Take(bytes.Length - 1).ToArray()));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Older reviewed workflows remain readable and never acquire the new activation opt-in.</summary>
		[TestCase(8, 3)]
		[TestCase(9, 1)]
		public void UnlistedPluginPreference_LegacyReviewsDefaultOff(int version, int omittedBytes)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root, true);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				byte[] bytes = CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot);
				bytes = bytes.Take(bytes.Length - omittedBytes).ToArray();
				Buffer.BlockCopy(BitConverter.GetBytes(version), 0, bytes, 0, sizeof(int));
				Assert.That(CollectionReviewedWorkflowSnapshotCodec.Deserialize(bytes).EnableUnspecifiedPlugins, Is.False);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Large reviewed Collections survive both the former 64 MB payload cap and 100,000-file reader cap.</summary>
		[Test]
		public void ReviewedSnapshot_RoundTripsLargeFilePlanBeyondFormerLimits()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionReviewedWorkflowSnapshot original = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				const int fileCount = 123426;
				string owner = new string('o', 600);
				var files = Enumerable.Range(0, fileCount).Select(index => new CollectionReviewedFileImpactSnapshot(
					ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\file-" + index + ".dds"),
					new[] { fixture.Member.MemberKey }, fixture.Member.MemberKey, owner, new Guid[0]));
				var large = new CollectionReviewedWorkflowSnapshot(original.Identity, original.Revision, original.Target,
					original.PolicyKind, original.ReplacementBackupChoice, original.ApprovedStateFingerprint, original.ManifestSource,
					original.Members, original.Dependencies, original.PriorityRules, original.Phases, original.Barriers, files,
					original.PluginImpacts, original.ConfigurationImpacts, original.AssociationImpacts, original.PreparedRecipes,
					original.AutomaticallyInstall, original.PreferIncomingCollection);

				byte[] payload = CollectionReviewedWorkflowSnapshotCodec.Serialize(large);
				Assert.Greater(payload.Length, 64 * 1024 * 1024);
				CollectionReviewedWorkflowSnapshot restored = CollectionReviewedWorkflowSnapshotCodec.Deserialize(payload);
				Assert.AreEqual(fileCount, restored.FileImpacts.Count);
				Assert.AreEqual(large.FileImpacts.OrderBy(x => x.Target.RelativePath, StringComparer.OrdinalIgnoreCase).Last().Target,
					restored.FileImpacts.Last().Target);
				Assert.AreEqual(owner, restored.FileImpacts.Last().CurrentOwnerKey);
				Assert.AreEqual(original.PreparedRecipes.Single().PreparedNativeFingerprint, restored.PreparedRecipes.Single().PreparedNativeFingerprint);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Approval reuses an unchanged prepared review but still rebuilds state-dependent runtime objects.</summary>
		[TestCase(true)]
		[TestCase(false)]
		public void Reconstruct_ReusesPreparedReviewAndInvalidatesChangedInstructions(bool seedPreparedReview)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				var reconstructor = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store);
				ResolvedCollectionPlan rememberedPlan = fixture.Plan;
				if (seedPreparedReview) reconstructor.RememberReview(snapshot, fixture.Plan, new[] { fixture.PreparedRecipe });
				else rememberedPlan = reconstructor.ReconstructPlan(snapshot);
				CollectionReviewedWorkflowSnapshot restored = CollectionReviewedWorkflowSnapshotCodec.Deserialize(CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					restored, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				CollectionReviewedWorkflowRuntime first = reconstructor.Reconstruct(rehydration);
				CollectionReviewedWorkflowRuntime second = reconstructor.Reconstruct(rehydration);
				Assert.AreSame(rememberedPlan, first.Plan);
				if (seedPreparedReview) Assert.AreSame(fixture.PreparedRecipe, first.GetPreparedRecipe(fixture.Member.MemberKey));
				Assert.AreSame(first.GetPreparedRecipe(fixture.Member.MemberKey), second.GetPreparedRecipe(fixture.Member.MemberKey));
				Assert.AreSame(first.Plan, second.Plan);
				Assert.AreNotSame(first, second);
				Assert.AreNotSame(first.Matches, second.Matches);
				Assert.AreNotSame(first.ImpactPlan, second.ImpactPlan);
				Assert.Throws<InvalidOperationException>(() => reconstructor.Reconstruct(new CollectionReviewedWorkflowRehydrationResult(
					CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired, restored, fixture.State, null, "changed")));

				var changedRecipe = new PreparedCollectionNativeRecipe(fixture.Member, PreparedCollectionNativeRecipeIdentity.FromFingerprint("changed-instructions"),
					fixture.PreparedRecipe.RecipeInput, fixture.PreparedRecipe.EffectPreview, fixture.PreparedRecipe.SkipReadmeFiles, fixture.PreparedRecipe.RetainedArtifactIds);
				CollectionReviewedWorkflowSnapshot changed = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { changedRecipe });
				CollectionReviewedWorkflowRuntime rebuilt = reconstructor.Reconstruct(new CollectionReviewedWorkflowRehydrationResult(
					CollectionReviewedWorkflowRehydrationStatus.Ready, changed, fixture.State, new[] { fixture.Member.MemberKey }, "ready"));
				Assert.AreNotSame(rememberedPlan, rebuilt.Plan);
				Assert.AreEqual(changedRecipe.PreparedNativeIdentity, rebuilt.GetPreparedRecipe(fixture.Member.MemberKey).PreparedNativeIdentity);
			}
			finally { Directory.Delete(root, true); }
		}

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

		/// <summary>Output hashes and lengths survive review persistence and executable recipe reconstruction.</summary>
		[Test]
		public void Reconstruct_RetainsReviewedFileContentIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				PreparedCollectionNativeRecipe original = fixture.PreparedRecipe;
				CollectionMemberEffectPreview oldPreview = original.EffectPreview;
				CollectionContentHash hash = CollectionContentHash.FromSha256(new string('a', 64));
				var preview = new CollectionMemberEffectPreview(oldPreview.MemberKey, oldPreview.RecipeIdentity,
					oldPreview.InstallMethod, oldPreview.InstallRoot,
					new[] { new CollectionPlannedFileEffect(oldPreview.Files.Single().Target, hash, 123) },
					oldPreview.IniEdits, oldPreview.GameValues, oldPreview.PluginEffects, oldPreview.Issues);
				var prepared = new PreparedCollectionNativeRecipe(fixture.Member, original.PreparedNativeIdentity,
					original.RecipeInput, preview, original.SkipReadmeFiles, original.RetainedArtifactIds);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { prepared });
				CollectionReviewedWorkflowSnapshot roundTrip = CollectionReviewedWorkflowSnapshotCodec.Deserialize(
					CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					roundTrip, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				CollectionPlannedFileEffect effect = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.Reconstruct(rehydration).GetPreparedRecipe(fixture.Member.MemberKey).EffectPreview.Files.Single();

				Assert.That(effect.HasExactContentIdentity, Is.True);
				Assert.That(effect.ExpectedContentHash, Is.EqualTo(hash));
				Assert.That(effect.ExpectedByteLength, Is.EqualTo(123));
				Assert.That(CollectionNativeChildExecutionCoordinator.CreateExpectedFileEvidence(effect).ContentHash, Is.EqualTo(hash));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void Reconstruct_RoundTripsRetainedGeneratedFileRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				byte[] generatedBytes = Encoding.UTF8.GetBytes("patched reviewed output");
				CollectionsRetainedArtifact generatedArtifact;
				using (var stream = new MemoryStream(generatedBytes, false))
					generatedArtifact = new CollectionsRetainedArtifactStore(fixture.Store).Publish(stream);
				InstallModFileOperation sourceOperation = fixture.PreparedRecipe.RecipeInput.NativeOperations.Single() as InstallModFileOperation;
				Assert.IsNotNull(sourceOperation);
				ModInstallationRecipeInput generatedInput = fixture.PreparedRecipe.RecipeInput.WithTransformedNativePlan(new ScriptedInstallOperation[]
				{
					new GenerateDataFileOperation(sourceOperation.DestinationPath, generatedBytes, sourceOperation.SourcePath)
				});
				var prepared = new PreparedCollectionNativeRecipe(fixture.Member,
					PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-generated-runtime"), generatedInput,
					fixture.PreparedRecipe.EffectPreview, false, fixture.PreparedRecipe.RetainedArtifactIds.Concat(new[] { generatedArtifact.ArtifactId }));

				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { prepared });
				CollectionReviewedWorkflowSnapshot roundTrip = CollectionReviewedWorkflowSnapshotCodec.Deserialize(
					CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
				Assert.AreEqual(0, roundTrip.PreparedRecipes.Single().SimpleFileMappings.Count);
				Assert.AreEqual(1, roundTrip.PreparedRecipes.Single().GeneratedFiles.Count);

				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					roundTrip, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				PreparedCollectionNativeRecipe reconstructed = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.Reconstruct(rehydration).GetPreparedRecipe(fixture.Member.MemberKey);
				GenerateDataFileOperation operation = reconstructed.RecipeInput.NativeOperations.Single() as GenerateDataFileOperation;
				Assert.IsNotNull(operation);
				Assert.AreEqual("textures\\source.dds", operation.PreparationSourcePath);
				Assert.AreEqual("textures\\installed.dds", operation.DestinationPath);
				CollectionAssert.AreEqual(generatedBytes, operation.Data);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconstruct_RoundTripsReviewedGameSpecificValueRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				byte[] first = Encoding.UTF8.GetBytes("<node id=\"ModuleShortDesc\" value=\"first\" />");
				byte[] second = Encoding.UTF8.GetBytes("<node id=\"ModuleShortDesc\" value=\"second\" />");
				const string firstKey = "bg3-modsettings-v1|Public|ffffffff-ffff-ffff-ffff-ffffffffffff";
				const string secondKey = "bg3-modsettings-v1|Public|00000000-0000-0000-0000-000000000001";
				var operations = fixture.PreparedRecipe.RecipeInput.NativeOperations.Concat(new ScriptedInstallOperation[]
				{
					new EditGameSpecificValueOperation(firstKey, first, true),
					new EditGameSpecificValueOperation(secondKey, second, true)
				});
				ModInstallationRecipeInput input = fixture.PreparedRecipe.RecipeInput.WithTransformedNativePlan(operations);
				CollectionMemberEffectPreview originalPreview = fixture.PreparedRecipe.EffectPreview;
				var preview = new CollectionMemberEffectPreview(fixture.Member.MemberKey, fixture.Member.RecipeIdentity,
					originalPreview.InstallMethod, originalPreview.InstallRoot, originalPreview.Files, originalPreview.IniEdits,
					new[] { new CollectionPlannedGameValueEffect(firstKey, first), new CollectionPlannedGameValueEffect(secondKey, second) },
					originalPreview.PluginEffects, originalPreview.Issues);
				var prepared = new PreparedCollectionNativeRecipe(fixture.Member,
					PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-game-value-runtime"), input, preview,
					false, fixture.PreparedRecipe.RetainedArtifactIds);

				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { prepared });
				CollectionReviewedWorkflowSnapshot roundTrip = CollectionReviewedWorkflowSnapshotCodec.Deserialize(
					CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
				Assert.AreEqual(2, roundTrip.PreparedRecipes.Single().GameSpecificValues.Count);
				Assert.That(roundTrip.PreparedRecipes.Single().GameSpecificValues.Select(x => x.Key), Is.EqualTo(new[] { firstKey, secondKey }));
				CollectionAssert.AreEqual(first, roundTrip.PreparedRecipes.Single().GameSpecificValues[0].Value);
				CollectionAssert.AreEqual(second, roundTrip.PreparedRecipes.Single().GameSpecificValues[1].Value);

				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					roundTrip, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				PreparedCollectionNativeRecipe reconstructed = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.Reconstruct(rehydration).GetPreparedRecipe(fixture.Member.MemberKey);
				List<EditGameSpecificValueOperation> reconstructedValues = reconstructed.RecipeInput.NativeOperations.OfType<EditGameSpecificValueOperation>().ToList();
				Assert.That(reconstructedValues.Select(x => x.Key), Is.EqualTo(new[] { firstKey, secondKey }));
				Assert.That(reconstructedValues.All(x => x.HasResolvedOverwriteDecision), Is.True);
				CollectionAssert.AreEqual(first, reconstructedValues[0].Value);
				CollectionAssert.AreEqual(second, reconstructedValues[1].Value);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconstruct_RoundTripsSourceReplicatedSimpleRecipe()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				var mappings = new[]
				{
					new ModInstallationSimpleFileMapping("textures\\source.dds", "textures\\first.dds"),
					new ModInstallationSimpleFileMapping("textures\\source.dds", "textures\\second.dds")
				};
				var recipe = new ModInstallationSimpleFileRecipe(mappings, true);
				ModInstallationRecipeValidation originalValidation = fixture.PreparedRecipe.Validation;
				var validation = new ModInstallationRecipeValidation(ModInstallationSimpleFileRecipeAdapter.AdapterId,
					ModInstallationSimpleFileRecipeAdapter.AdapterVersion, originalValidation.InstallContext,
					originalValidation.ExpectedContent,
					new[] { new ModInstallationRecipeCapability(ModInstallationSimpleFileRecipeAdapter.CapabilityId,
						ModInstallationSimpleFileRecipeAdapter.CapabilityVersion) },
					new[] {
						new ModInstallationRecipePath(ModInstallationRecipePathKind.ArchiveSource, "textures\\source.dds"),
						new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "textures\\first.dds"),
						new ModInstallationRecipePath(ModInstallationRecipePathKind.Destination, "textures\\second.dds")
					});
				var nativeIdentity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
					new ModOperationFingerprint(fixture.Target.Fingerprint, validation.InstallContext, fixture.Member.RecipeIdentity.Fingerprint));
				ModInstallationRecipeInput translated = new ModInstallationSimpleFileRecipeAdapter().Translate(
					new ModInstallationRecipeInput(nativeIdentity, validation), recipe);
				var preview = new CollectionMemberEffectPreview(fixture.Member.MemberKey, fixture.Member.RecipeIdentity,
					ModInstallMethod.Virtual, ModInstallRoot.Data, new[] {
						new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\first.dds")),
						new CollectionPlannedFileEffect(ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "textures\\second.dds"))
					}, new CollectionPlannedIniEffect[0], new CollectionPlannedGameValueEffect[0],
					new CollectionPlannedPluginEffect[0], new CollectionEffectPreviewIssue[0]);
				var prepared = new PreparedCollectionNativeRecipe(fixture.Member,
					PreparedCollectionNativeRecipeIdentity.FromFingerprint("prepared-replicated-runtime"), translated,
					preview, false, fixture.PreparedRecipe.RetainedArtifactIds);

				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, fixture.ImpactPlan, new[] { prepared });
				CollectionReviewedWorkflowSnapshot roundTrip = CollectionReviewedWorkflowSnapshotCodec.Deserialize(
					CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));
				Assert.AreEqual(2, roundTrip.PreparedRecipes.Single().SimpleFileMappings.Count);
				Assert.AreEqual(1, roundTrip.PreparedRecipes.Single().Validation.Paths.Count(x =>
					x.Kind == ModInstallationRecipePathKind.ArchiveSource));

				var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.Ready,
					roundTrip, fixture.State, new[] { fixture.Member.MemberKey }, "ready");
				PreparedCollectionNativeRecipe reconstructed = new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store)
					.Reconstruct(rehydration).GetPreparedRecipe(fixture.Member.MemberKey);
				InstallModFileOperation[] operations = reconstructed.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().ToArray();
				Assert.AreEqual(2, operations.Length);
				Assert.That(operations.Select(x => x.SourcePath), Is.All.EqualTo("textures\\source.dds"));
				CollectionAssert.AreEquivalent(new[] { "textures\\first.dds", "textures\\second.dds" },
					operations.Select(x => x.DestinationPath));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SnapshotCodec_RoundTripsPreservedExistingManagedFileWinner()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionFileImpact original = fixture.ImpactPlan.FileImpacts.Single();
				var impact = new CollectionConflictImpactPlan(fixture.Plan, fixture.State, new[]
				{
					new CollectionFileImpact(original.Target, original.Writers, original.PlannedWinner,
						"external-owner", true, original.AffectedAssociationIds)
				}, new CollectionPluginImpact[0], new CollectionConfigurationImpact[0],
					new CollectionAssociationImpact[0], new CollectionConflictImpactIssue[0]);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, impact, new[] { fixture.PreparedRecipe });

				CollectionReviewedWorkflowSnapshot roundTrip = CollectionReviewedWorkflowSnapshotCodec.Deserialize(
					CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot));

				Assert.IsTrue(roundTrip.FileImpacts.Single().PreserveCurrentOwner);
				Assert.AreEqual("external-owner", roundTrip.FileImpacts.Single().CurrentOwnerKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void SnapshotCodec_PreservesMandatoryVirtualOrphanReconciliationAcrossRestart()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				CollectionFileImpact original = fixture.ImpactPlan.FileImpacts.Single();
				var impact = new CollectionConflictImpactPlan(fixture.Plan, fixture.State, new[]
				{
					new CollectionFileImpact(original.Target, original.Writers, original.PlannedWinner,
						null, false, original.AffectedAssociationIds, true)
				}, new CollectionPluginImpact[0], new CollectionConfigurationImpact[0],
					new CollectionAssociationImpact[0], new CollectionConflictImpactIssue[0]);
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(fixture.Plan,
					fixture.DependencyPlan, impact, new[] { fixture.PreparedRecipe });
				byte[] payload = CollectionReviewedWorkflowSnapshotCodec.Serialize(snapshot);
				Assert.AreEqual(12, BitConverter.ToInt32(payload, 0));
				CollectionReviewedWorkflowSnapshot restored = CollectionReviewedWorkflowSnapshotCodec.Deserialize(payload);
				Assert.IsTrue(restored.FileImpacts.Single().ReconcileUnresolvedVirtualOwner);
				Assert.IsNull(restored.FileImpacts.Single().CurrentOwnerKey);
				Assert.AreEqual(original.PlannedWinner, restored.FileImpacts.Single().PlannedWinner);
			}
			finally { Directory.Delete(root, true); }
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
				StringAssert.Contains("executable file-operation persistence", result.Message.ToLowerInvariant());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Automatic recovery status reads metadata without auditing archives or authorizing an executable runtime.</summary>
		[Test]
		public void RecoveryStatus_DoesNotReadRetainedContentOrAuthorizeRuntime()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Fixture fixture = CreateFixture(root);
				var operationStore = new CollectionsOperationStore(fixture.Store);
				var planStore = new CollectionsResolvedPlanStore(fixture.Store);
				var coordinator = new CollectionOperationCoordinator(operationStore, planStore);
				CollectionOperation operation = coordinator.CreateApplyOperation(fixture.Revision.Identity.Collection, fixture.Target);
				operation = coordinator.BeginResolving(operation.Identity);
				operation = coordinator.BeginPreparing(operation.Identity, fixture.Revision.Identity);
				operation = coordinator.MarkReadyForReview(operation.Identity, fixture.Plan, fixture.DependencyPlan,
					fixture.ImpactPlan, new[] { fixture.PreparedRecipe });
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore,
					revision => sourceStore.GetSource(revision),
					(revision, source) => { throw new InvalidOperationException("Manifest content must not be loaded for status."); },
					artifact => { throw new InvalidOperationException("Archive content must not be loaded for status."); },
					target => fixture.State);
				CollectionReviewedWorkflowRehydrationResult status = rehydrator.RehydrateForRecoveryStatus(operation.Identity);
				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.Ready, status.Status);
				Assert.IsFalse(status.HasVerifiedRetainedInputs);
				Assert.Throws<InvalidOperationException>(() => new CollectionReviewedWorkflowRuntimeReconstructor(fixture.Store).Reconstruct(status));
				Assert.AreNotEqual(CollectionReviewedWorkflowRehydrationStatus.Ready, rehydrator.Rehydrate(operation.Identity).Status);

				ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
					new ModOperationFingerprint(fixture.Target.Fingerprint, fixture.PreparedRecipe.InstallContext, fixture.Member.RecipeIdentity.Fingerprint));
				var completed = new[] { new CollectionNativeChildOperation(2,
					new CollectionOperationMemberReference(fixture.Revision.Identity, fixture.Member.MemberKey),
					CollectionNativeChildAction.ActivateOrReinstall, native, CollectionNativeChildCheckpoint.Reconciled,
					new ModOperationResult(native, ModOperationReportedStatus.Succeeded, ModOperationDurability.VerifiedCommitted, "Committed")) };
				operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, CollectionOperationPhase.PausedAtSafeBoundary,
					CollectionOperationResultState.Pending, completed);
				operationStore.SaveOperation(operation);
				int checkpointReads = 0;
				var checkpointRehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore,
					revision => sourceStore.GetSource(revision),
					(revision, source) => { throw new InvalidOperationException("No manifest content for status."); },
					artifact => { throw new InvalidOperationException("No archive content for status."); }, target => fixture.State,
					(parent, child) => { throw new InvalidOperationException("No full recovery input audit for completed children."); },
					(parent, child) => { checkpointReads++; Assert.AreEqual(2, child.Sequence); return null; });
				Assert.AreEqual(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid,
					checkpointRehydrator.RehydrateForRecoveryStatus(operation.Identity).Status);
				Assert.AreEqual(1, checkpointReads, "Only the latest committed checkpoint establishes the continuation boundary.");

				var association = new CollectionTargetAssociation(Guid.NewGuid(), operation.Revision, operation.Target, CollectionAssociationState.Incomplete);
				operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, CollectionOperationPhase.Completed,
					CollectionOperationResultState.StoppedPartial, completed);
				operationStore.SaveOperation(operation);
				Assert.AreEqual(operation.Identity, operationStore.GetLatestStoppedPartialApply(association).Identity);
				operationStore.SaveOperation(new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, CollectionOperationPhase.Completed,
					CollectionOperationResultState.Committed, completed));
				Assert.IsNull(operationStore.GetLatestStoppedPartialApply(association), "A completed apply must not reopen an older partial review.");
			}
			finally { Directory.Delete(root, true); }
		}

		private static Fixture CreateFixture(string root, bool enableUnspecifiedPlugins = false)
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
				CollectionExecutionPolicy.InstallIntoCurrentSetup(false, false, enableUnspecifiedPlugins), state.Fingerprint, normalization.CapabilityReport, new[] { member });

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
