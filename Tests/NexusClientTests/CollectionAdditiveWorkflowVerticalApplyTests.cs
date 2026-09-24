using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.Settings;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.14b/14d/14f/14g vertical apply coverage through the real additive coordinator, C6.6 durable preparation,
	/// C6.10 provenance reconciliation, reviewed-state invalidation, retained-input integrity and restart-safe approval boundaries.
	/// </summary>
	[TestFixture]
	public class CollectionAdditiveWorkflowVerticalApplyTests
	{
		[Test]
		public void ApproveAndApply_InstalledCompatibleNoOp_CommitsWithoutNativeChild()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Compatible, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(prepared.Runtime.Matches.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
				Assert.That(prepared.Runtime.PreparedRecipes, Is.Empty);

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);

				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(applied.Operation.IsSuccessful, Is.True);
				Assert.That(applied.Operation.NativeChildren, Is.Empty,
					"A verified installed-compatible member must remain a true zero-native-write Collection apply.");
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
				Assert.That(fixture.WinnerBarrier.CallCount, Is.EqualTo(1));
				Assert.That(applied.Finalization.Association.State, Is.EqualTo(CollectionAssociationState.Applied));
				Assert.That(applied.Finalization.Bindings.Single().BindingKind,
					Is.EqualTo(CollectionMemberBindingKind.AdoptedExisting));
			}
		}

		[Test]
		public void ApproveAndApply_CompatibleBindingSharedByAnotherCollection_PreservesExistingAssociation()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Compatible, ModInstallMethod.Virtual,
				seedCompatibleBindingForDifferentCollection: true))
			{
				CollectionTargetAssociation shared = fixture.Associations.GetAssociationsForTarget(fixture.Target)
					.Single(x => !x.Revision.Equals(fixture.Revision.Identity));
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				Assert.That(prepared.Runtime.Matches.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);

				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
				Assert.That(applied.Finalization.Association.AssociationId, Is.Not.EqualTo(shared.AssociationId));
				Assert.That(fixture.Associations.GetAssociation(shared.AssociationId).State, Is.EqualTo(CollectionAssociationState.Applied));
				Assert.That(fixture.Associations.GetBindings(shared.AssociationId).Single().NativeMod,
					Is.EqualTo(applied.Finalization.Bindings.Single().NativeMod));
			}
		}

		[TestCase(ModInstallMethod.Direct)]
		[TestCase(ModInstallMethod.Virtual)]
		public void PrepareAndApply_ExactSimpleArchive_UsesReviewedInstallContextAndCommits(ModInstallMethod installMethod)
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, installMethod))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				CollectionMemberMatchResult match = prepared.Runtime.Matches.Members.Single();
				PreparedCollectionNativeRecipe recipe = prepared.Runtime.PreparedRecipes.Single();

				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(match.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ArchiveOnlyReuse));
				Assert.That(recipe.InstallContext.Method, Is.EqualTo(installMethod));
				Assert.That(recipe.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.Data));
				Assert.That(recipe.RecipeInput.NativeOperations.Count, Is.EqualTo(1));
				Assert.That(recipe.RecipeInput.NativeOperations.All(x => x is InstallModFileOperation), Is.True,
					"Gate-A BasicInstall preparation must be expanded to explicit typed file operations before review.");
				Assert.That(recipe.RecipeInput.NativeOperations.Any(x => x is PerformBasicInstallOperation), Is.False);
				Assert.That(recipe.EffectPreview.IsComplete, Is.True);
				Assert.That(recipe.EffectPreview.Files.Count, Is.EqualTo(1));
				Assert.That(recipe.EffectPreview.InstallMethod, Is.EqualTo(installMethod));

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);

				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(1));
				Assert.That(fixture.NativeBoundary.LastRecipe.InstallContext.Method, Is.EqualTo(installMethod));
				Assert.That(fixture.NativeBoundary.LastPreparedManifest.PreviousNativeMod, Is.Null);
				Assert.That(applied.Operation.NativeChildren.Count, Is.EqualTo(1));
				Assert.That(applied.Operation.NativeChildren.Single().Checkpoint, Is.EqualTo(CollectionNativeChildCheckpoint.Reconciled));
				Assert.That(applied.Operation.NativeChildren.Single().NativeResult.Durability,
					Is.EqualTo(ModOperationDurability.VerifiedCommitted));
				Assert.That(applied.Finalization.Association.State, Is.EqualTo(CollectionAssociationState.Applied));
				Assert.That(applied.Finalization.Bindings.Single().BindingKind,
					Is.EqualTo(CollectionMemberBindingKind.InstalledForCollection));
				Assert.That(fixture.WinnerBarrier.CallCount, Is.EqualTo(1));
			}
		}

		[Test]
		public void PrepareAndApply_ExactInstalledArchiveWithoutVerifiedRecipe_ReinstallsUsingExistingContext()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.ExactArtifactUnverified, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				CollectionMemberMatchResult match = prepared.Runtime.Matches.Members.Single();
				PreparedCollectionNativeRecipe recipe = prepared.Runtime.PreparedRecipes.Single();

				Assert.That(match.Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
				Assert.That(match.Reason, Is.EqualTo(CollectionMemberMatchReason.ExactArtifactRecipeUnverified));
				Assert.That(recipe.InstallContext.Method, Is.EqualTo(ModInstallMethod.Direct),
					"Reviewed reinstall must preserve the native instance's existing method instead of taking the current global preference.");
				Assert.That(recipe.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.Data));

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);

				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(1));
				Assert.That(fixture.NativeBoundary.LastPreparedManifest.PreviousNativeMod, Is.Not.Null);
				Assert.That(fixture.NativeBoundary.LastPreparedManifest.PreviousNativeMod.InstallMethod,
					Is.EqualTo(ModInstallMethod.Direct));
				Assert.That(fixture.NativeBoundary.LastRecipe.InstallContext.Method, Is.EqualTo(ModInstallMethod.Direct));
				Assert.That(applied.Finalization.Bindings.Single().BindingKind,
					Is.EqualTo(CollectionMemberBindingKind.InstalledForCollection));
			}
		}

		[Test]
		public void GetReview_StateChangedAfterPreparation_FailsClosedBeforeNativeWrite()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));

				fixture.AdvanceDeploymentSequence();
				CollectionAdditiveWorkflowReview review = fixture.Workflow.GetReview(prepared.Operation.Identity);

				Assert.That(review.IsReady, Is.False);
				Assert.That(review.Rehydration.Status, Is.EqualTo(CollectionReviewedWorkflowRehydrationStatus.CurrentStateChanged));
				Assert.That(review.Operation.Phase, Is.EqualTo(CollectionOperationPhase.ReadyForReview));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0),
					"A stale exact review must be rejected before any native child can cross the mutation boundary.");
			}
		}

		[Test]
		public void ApproveAndApply_StateChangedAfterExplicitApproval_ReopensPreparationWithoutNativeWrite()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				CollectionOperation approved = fixture.MarkReadyToApply(prepared);
				Assert.That(approved.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));

				fixture.AdvanceDeploymentSequence();
				CollectionAdditiveWorkflowApplyResult result = fixture.Apply(prepared);

				Assert.That(result.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.RepreparationRequired));
				Assert.That(result.Operation.Phase, Is.EqualTo(CollectionOperationPhase.Preparing),
					"A stale pre-native approval must return to Preparing so the exact review can actually be rebuilt.");
				Assert.That(result.Operation.HasCrossedNativeBoundary, Is.False);
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
			}
		}

		[Test]
		public void Prepare_SpecialFileInstallGamePath_IsBlockedBeforeAnyNativeChild()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual, requiresSpecialFileInstallation: true))
			{
				CollectionAdditiveWorkflowPreparationResult result = fixture.Prepare();

				Assert.That(result.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.Blocked));
				StringAssert.Contains("SpecialFileInstall", result.Message);
				Assert.That(result.Operation.HasCrossedNativeBoundary, Is.False);
				Assert.That(result.Operation.NativeChildren, Is.Empty);
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0),
					"Uncharacterized special-game BasicInstall behavior must fail closed during review preparation.");
			}
		}

		[Test]
		public void PrepareAndApply_SkipReadmeSetting_IsCapturedInReviewedRecipeAndNativeApply()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Direct, skipReadmeFiles: true,
				archiveFiles: new[] { "README.txt", @"meshes\body.nif" }))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				PreparedCollectionNativeRecipe recipe = prepared.Runtime.PreparedRecipes.Single();

				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(recipe.SkipReadmeFiles, Is.True);
				Assert.That(recipe.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.SourcePath),
					Is.EquivalentTo(new[] { @"meshes\body.nif" }));
				CollectionAdditiveWorkflowReview rehydrated = fixture.Workflow.GetReview(prepared.Operation.Identity);
				Assert.That(rehydrated.IsReady, Is.True);
				Assert.That(rehydrated.Runtime.PreparedRecipes.Single().SkipReadmeFiles, Is.True,
					"The reviewed workflow must retain the exact readme-suppression input instead of consulting mutable settings later.");

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);
				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.LastRecipe.NativeOperations.OfType<InstallModFileOperation>().Select(x => x.SourcePath),
					Is.EquivalentTo(new[] { @"meshes\body.nif" }));
			}
		}

		[Test]
		public void PrepareAndApply_GameRootReinstall_StripsRecognizedWrapperIntoReviewedOperations()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.ExactArtifactUnverified, ModInstallMethod.Virtual,
				installedRoot: ModInstallRoot.GameRoot, archiveFiles: new[]
				{
					@"skse64_2_02_06_gog\skse64_loader.exe",
					@"skse64_2_02_06_gog\Data\Scripts\Test.pex"
				}))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				PreparedCollectionNativeRecipe recipe = prepared.Runtime.PreparedRecipes.Single();
				InstallModFileOperation[] operations = recipe.RecipeInput.NativeOperations.OfType<InstallModFileOperation>().ToArray();

				Assert.That(prepared.Runtime.Matches.Members.Single().Disposition, Is.EqualTo(CollectionMemberMatchDisposition.ReinstallRequired));
				Assert.That(recipe.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
				CollectionAssert.AreEqual(new[] { "skse64_loader.exe", @"Data\Scripts\Test.pex" },
					operations.Select(x => x.DestinationPath).ToArray());
				Assert.That(recipe.EffectPreview.Files.All(x => x.Target.Root == ModDeploymentRoot.GameRoot), Is.True);

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);
				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.LastRecipe.InstallContext.InstallRoot, Is.EqualTo(ModInstallRoot.GameRoot));
			}
		}

		[Test]
		public void Prepare_ModFileMergeGamePath_IsBlockedBeforeAnyNativeChild()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual, requiresModFileMerge: true))
			{
				CollectionAdditiveWorkflowPreparationResult result = fixture.Prepare();

				Assert.That(result.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.Blocked));
				StringAssert.Contains("ModFileMerge", result.Message);
				Assert.That(result.Operation.HasCrossedNativeBoundary, Is.False);
				Assert.That(result.Operation.NativeChildren, Is.Empty);
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
			}
		}

		[Test]
		public void ReconcileIncompleteTarget_RetainedManifestTampered_RequiresRepreparationWithoutNativeWrite()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				fixture.MarkReadyToApply(prepared);
				fixture.TamperRetainedManifest();

				IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> recovered = fixture.Workflow
					.ReconcileIncompleteTargetAsync(fixture.Paths, CancellationToken.None).GetAwaiter().GetResult();
				CollectionAdditiveWorkflowRecoveryResult operationRecovery = recovered.Single(x =>
					x.Operation.Identity.Equals(prepared.Operation.Identity));

				Assert.That(operationRecovery.Status, Is.EqualTo(CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired));
				Assert.That(operationRecovery.Rehydration.Status, Is.EqualTo(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid));
				Assert.That(operationRecovery.Operation.HasCrossedNativeBoundary, Is.False);
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
			}
		}

		[Test]
		public void ReconcileIncompleteTarget_RetainedArchiveTampered_RequiresRepreparationWithoutNativeWrite()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				fixture.MarkReadyToApply(prepared);
				fixture.TamperPreparedArchive(prepared);

				IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> recovered = fixture.Workflow
					.ReconcileIncompleteTargetAsync(fixture.Paths, CancellationToken.None).GetAwaiter().GetResult();
				CollectionAdditiveWorkflowRecoveryResult operationRecovery = recovered.Single(x =>
					x.Operation.Identity.Equals(prepared.Operation.Identity));

				Assert.That(operationRecovery.Status, Is.EqualTo(CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired));
				Assert.That(operationRecovery.Rehydration.Status, Is.EqualTo(CollectionReviewedWorkflowRehydrationStatus.RetainedInputInvalid));
				Assert.That(operationRecovery.Operation.HasCrossedNativeBoundary, Is.False);
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
			}
		}

		[Test]
		public void ReconcileIncompleteTarget_RestartAtReadyToApply_RehydratesThenResumesExactPlan()
		{
			using (Fixture fixture = Fixture.Create(InitialNativeState.Empty, ModInstallMethod.Virtual))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				CollectionOperation approved = fixture.MarkReadyToApply(prepared);
				Assert.That(approved.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));

				IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult> recovered = fixture.Workflow
					.ReconcileIncompleteTargetAsync(fixture.Paths, CancellationToken.None).GetAwaiter().GetResult();

				CollectionAdditiveWorkflowRecoveryResult operationRecovery = recovered.Single(x => x.Operation.Identity.Equals(prepared.Operation.Identity));
				Assert.That(operationRecovery.Status, Is.EqualTo(CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume));
				Assert.That(operationRecovery.Rehydration, Is.Not.Null);
				Assert.That(operationRecovery.Rehydration.CanResume, Is.True);
				Assert.That(operationRecovery.Operation.Phase, Is.EqualTo(CollectionOperationPhase.ReadyToApply));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);
				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(1));
				Assert.That(applied.Operation.IsSuccessful, Is.True);
			}
		}

		private enum InitialNativeState
		{
			Empty = 0,
			Compatible = 1,
			ExactArtifactUnverified = 2
		}

		private sealed class Fixture : IDisposable
		{
			private bool _disposed;

			private Fixture(string root, GameStoragePathSet paths, CollectionsStore store,
				CollectionRevision revision, NormalizedCollectionManifest manifest, NormalizedCollectionMember member,
				CollectionTargetIdentity target, CollectionsAssociationStore associations,
				CollectionAdditiveWorkflowCoordinator workflow, DeterministicNativeBoundary nativeBoundary,
				RecordingWinnerBarrier winnerBarrier, MutableInstallLogSnapshot installState)
			{
				Root = root;
				Paths = paths;
				Store = store;
				Revision = revision;
				Manifest = manifest;
				Member = member;
				Target = target;
				Associations = associations;
				Workflow = workflow;
				NativeBoundary = nativeBoundary;
				WinnerBarrier = winnerBarrier;
				InstallState = installState ?? throw new ArgumentNullException(nameof(installState));
			}

			public string Root { get; }
			public GameStoragePathSet Paths { get; }
			public CollectionsStore Store { get; }
			public CollectionRevision Revision { get; }
			public NormalizedCollectionManifest Manifest { get; }
			public NormalizedCollectionMember Member { get; }
			public CollectionTargetIdentity Target { get; }
			public CollectionsAssociationStore Associations { get; }
			public CollectionAdditiveWorkflowCoordinator Workflow { get; }
			public DeterministicNativeBoundary NativeBoundary { get; }
			public RecordingWinnerBarrier WinnerBarrier { get; }
			private MutableInstallLogSnapshot InstallState { get; }

			public static Fixture Create(InitialNativeState initialState, ModInstallMethod preferredInstallMethod,
				bool requiresSpecialFileInstallation = false, bool requiresModFileMerge = false, bool skipReadmeFiles = false,
				ModInstallRoot installedRoot = ModInstallRoot.Data, string[] archiveFiles = null,
				bool seedCompatibleBindingForDifferentCollection = false)
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c6-15-14b-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(root);
				var storageService = new GameStorageService(Path.Combine(root, "Registry"), new Version(9, 3));
				GameStoragePathSet paths = CreateStoragePaths(root);
				storageService.InitializeMetadataForStorage(paths);
				CollectionTargetIdentity target = new CollectionTargetIdentityResolver(storageService).Resolve(paths).Target;

				var store = new CollectionsStore(Path.Combine(root, "Collections"));
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var revisionSources = new CollectionsRevisionSourceStore(store);
				var operationStore = new CollectionsOperationStore(store);
				var planStore = new CollectionsResolvedPlanStore(store);
				var associations = new CollectionsAssociationStore(store);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var acquisitions = new CollectionsAcquisitionStore(store);
				var recoveryManifests = new CollectionsNativeChildRecoveryManifestStore(artifacts, references);
				var operationCoordinator = new CollectionOperationCoordinator(operationStore, planStore);

				CollectionIdentity collection = CollectionIdentity.FromNexus("c61514b-" + Guid.NewGuid().ToString("N"));
				var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1),
					"Revision 1", null, 1);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C6.15.14b", null, null), revision);

				byte[] archiveBytes = Encoding.UTF8.GetBytes("C6.15.14b deterministic exact archive bytes");
				string archivePath = Path.Combine(root, "member.zip");
				File.WriteAllBytes(archivePath, archiveBytes);
				archiveFiles = archiveFiles ?? new[] { @"meshes\body.nif" };
				string manifestJson = "{" +
					"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"C6.15.14b\",\"description\":\"Vertical apply fixture\",\"domainName\":\"skyrimspecialedition\"}," +
					"\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":100,\"fileId\":200,\"updatePolicy\":\"exact\"}}],\"modRules\":[]}";
				byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
				NexusCollectionManifestNormalizationResult normalization = new NexusCollectionManifestNormalizer().Normalize(manifestBytes, revision);
				Assert.That(normalization.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
				NormalizedCollectionManifest manifest = normalization.Manifest;
				NormalizedCollectionMember member = manifest.Members.Single();
				revisionSources.RetainManifest(manifest, CollectionRevisionSourceInputKind.RawManifest,
					manifest.Source.ContentHash, manifest.Source.ByteLength, "collection.json", manifestBytes);

				IMod managedMod = CreateManagedMod(archivePath, archiveFiles);
				var activeMods = new ThreadSafeObservableList<IMod>();
				var readOnlyActiveMods = new ReadOnlyObservableList<IMod>(activeMods);
				var installState = new MutableInstallLogSnapshot();
				if (initialState == InitialNativeState.Empty)
					installState.Set(CreateInstallSnapshot(null, archivePath, preferredInstallMethod, 0, new ModDeploymentTarget[0], installedRoot));
				else
				{
					activeMods.Add(managedMod);
					installState.Set(CreateInstallSnapshot("native-required", archivePath, ModInstallMethod.Direct, 0, new ModDeploymentTarget[0], installedRoot));
				}

				if (initialState == InitialNativeState.Compatible)
				{
					CollectionRevisionIdentity bindingRevision = revision.Identity;
					if (seedCompatibleBindingForDifferentCollection)
					{
						CollectionIdentity sharedCollection = CollectionIdentity.FromNexus("shared-" + Guid.NewGuid().ToString("N"));
						var sharedRevision = new CollectionRevision(
							CollectionRevisionIdentity.FromNexus(sharedCollection, "shared-revision", 1), "Shared Revision", null, 1);
						catalog.SaveDefinitionAndRevision(new CollectionDefinition(sharedCollection, "Shared C6.15.14g", null, null), sharedRevision);
						bindingRevision = sharedRevision.Identity;
					}
					var association = new CollectionTargetAssociation(Guid.NewGuid(), bindingRevision, target, CollectionAssociationState.Applied);
					associations.SaveAssociation(association);
					associations.SaveBinding(new CollectionMemberBinding(association, member.IdentityResolution.Key,
						new NativeModInstanceIdentity(target, "native-required"), member.RecipeIdentity,
						CollectionMemberBindingKind.AdoptedExisting));
				}

				IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "GetCommittedStateSnapshot": return installState.Get();
						case "get_ActiveMods": return readOnlyActiveMods;
						case "GetModKey": return args != null && args.Length > 0 && args[0] != null ? "native-required" : null;
						default: return null;
					}
				});
				IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
					method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
				IGameMode gameMode = CreateGameMode(paths, requiresSpecialFileInstallation, requiresModFileMerge);
				var nativeStateReader = new CollectionNativeStateReader(installLog, virtualModActivator, null, gameMode, associations);
				var targetResolver = new CollectionTargetIdentityResolver(storageService);
				var planBuilder = new CollectionResolvedPlanBuilder(catalog, revisionSources, operationCoordinator);
				var planPreparation = new CollectionAdditivePlanPreparationService(targetResolver, nativeStateReader, planBuilder);

				var archiveSource = new FixedArchiveSource(new CollectionManagedArchiveCandidate(
					"nexus-mod-file", "skyrimspecialedition/100/200", archivePath));
				var adopter = new CollectionVerifiedArchiveAdopter(archiveSource, new AcceptingVerifier(), artifacts, references, acquisitions);
				var queue = new FailingQueue();
				var requestCoordinator = new CollectionAcquisitionRequestCoordinator(queue, acquisitions);
				var premiumCoordinator = new CollectionPremiumAcquisitionCoordinator(requestCoordinator, new FixedAccountProvider(false));
				var manualCoordinator = new CollectionManualAcquisitionCoordinator(requestCoordinator, adopter);
				var acquisitionRestart = new CollectionAcquisitionRestartCoordinator(acquisitions,
					new EmptyPersistedAddModStateSource(), adopter, premiumCoordinator);
				var memberAcquisition = new CollectionMemberAcquisitionCoordinator(new CollectionMemberMatchEngine(), adopter,
					premiumCoordinator, manualCoordinator, acquisitionRestart, operationCoordinator);
				var revalidation = new CollectionAdditivePlanRevalidationService(targetResolver, nativeStateReader,
					operationCoordinator, planBuilder, memberAcquisition);

				ModManager manager = CreateModManagerShell(gameMode, installLog, managedMod, preferredInstallMethod, skipReadmeFiles);
				var services = new ServiceManager(installLog, null, null, null, manager, null, null, null);
				var rehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore, revisionSources,
					artifacts, nativeStateReader, recoveryManifests);
				var runtimeReconstructor = new CollectionReviewedWorkflowRuntimeReconstructor(store);
				var childPreparation = new CollectionNativeChildPreparationCoordinator(operationStore, planStore,
					artifacts, references, recoveryManifests);
				var childExecution = new CollectionNativeChildExecutionCoordinator(services, storageService,
					operationStore, planStore, associations, recoveryManifests);
				var childVerification = new CollectionNativeChildVerificationCoordinator(services, storageService,
					operationStore, planStore, associations, recoveryManifests);
				var childRestart = new CollectionNativeChildRestartReconciliationCoordinator(services, storageService,
					operationStore, planStore, associations, recoveryManifests);
				var associationCoordinator = new CollectionAssociationReconciliationCoordinator(operationStore, planStore,
					associations, recoveryManifests);
				var winnerCoordinator = new CollectionReviewedFileWinnerReconciliationCoordinator(operationStore, planStore,
					associations, artifacts, references,
					InterfaceStub<IModDeploymentManager>.Create((method, args) => null),
					InterfaceStub<IVirtualDeploymentService>.Create((method, args) => null),
					targetIdentity => nativeStateReader.Capture(targetIdentity), () => { });
				var nativeBoundary = new DeterministicNativeBoundary(operationStore, recoveryManifests, nativeStateReader,
					installState, archivePath);
				var winnerBarrier = new RecordingWinnerBarrier();

				var workflow = new CollectionAdditiveWorkflowCoordinator(services, storageService, operationStore, planStore,
					planPreparation, revalidation, memberAcquisition, new CollectionNativeRecipePreparer(store),
					new CollectionDependencyPhasePlanner(), new CollectionConflictImpactPlanner(), operationCoordinator,
					rehydrator, runtimeReconstructor, nativeStateReader, childPreparation, childExecution, childVerification,
					childRestart, associationCoordinator, winnerCoordinator, CollectionTargetMutationLeaseManager.Shared,
					new CollectionTargetOwnershipAuthorityValidator(storageService, new NoOpNativeStateReloader(),
						new CollectionTargetOwnershipAuthorityStore(Path.Combine(root, "MachineAuthority"))), nativeBoundary.ApplyAsync,
					winnerBarrier.ReconcileAsync);

				return new Fixture(root, paths, store, revision, manifest, member, target, associations, workflow,
					nativeBoundary, winnerBarrier, installState);
			}

			public CollectionAdditiveWorkflowPreparationResult Prepare()
			{
				CollectionCapabilityReport report = CollectionCapabilityReport.Create(Manifest);
				CollectionEffectiveSelection selection = new CollectionEffectiveSelectionBuilder().Build(report,
					new CollectionOptionalMemberSelection[0]);
				return Workflow.PrepareAsync(selection, Paths, null, CancellationToken.None).GetAwaiter().GetResult();
			}

			public CollectionAdditiveWorkflowApplyResult Apply(CollectionAdditiveWorkflowPreparationResult prepared)
			{
				return Workflow.ApproveAndApplyAsync(prepared.Operation.Identity, prepared.Runtime.Plan.Identity,
					Paths, CancellationToken.None).GetAwaiter().GetResult();
			}

			public CollectionOperation MarkReadyToApply(CollectionAdditiveWorkflowPreparationResult prepared)
			{
				var coordinator = new CollectionOperationCoordinator(new CollectionsOperationStore(Store),
					new CollectionsResolvedPlanStore(Store));
				return coordinator.MarkReadyToApply(prepared.Operation.Identity, prepared.Runtime.Plan.Identity);
			}

			public void AdvanceDeploymentSequence()
			{
				InstallLogReadSnapshot current = InstallState.Get();
				InstallState.Set(new InstallLogReadSnapshot(current.OriginalValuesKey, checked(current.DeploymentCommitSequence + 1),
					current.Mods, current.Files, current.IniEdits, current.GameValues, current.DeploymentTargets));
			}

			public void TamperRetainedManifest()
			{
				CollectionRevisionSourceRecord source = new CollectionsRevisionSourceStore(Store).GetSource(Revision.Identity);
				Assert.That(source, Is.Not.Null);
				TamperRetainedArtifact(source.RawManifestArtifactId);
			}

			public void TamperPreparedArchive(CollectionAdditiveWorkflowPreparationResult prepared)
			{
				CollectionRevisionSourceRecord source = new CollectionsRevisionSourceStore(Store).GetSource(Revision.Identity);
				string archiveArtifactId = prepared.Runtime.PreparedRecipes.Single().RetainedArtifactIds
					.Single(x => !StringComparer.Ordinal.Equals(x, source.RawManifestArtifactId));
				TamperRetainedArtifact(archiveArtifactId);
			}

			private void TamperRetainedArtifact(string artifactId)
			{
				var artifacts = new CollectionsRetainedArtifactStore(Store);
				CollectionsRetainedArtifact artifact = artifacts.GetArtifact(artifactId);
				Assert.That(artifact, Is.Not.Null);
				string hash = artifact.ContentHash.Value;
				string path = Path.Combine(Store.RetainedContentDirectory, "sha256", hash.Substring(0, 2), hash + ".blob");
				byte[] bytes = File.ReadAllBytes(path);
				Assert.That(bytes.Length, Is.GreaterThan(0));
				bytes[0] ^= 0x5a;
				File.WriteAllBytes(path, bytes);
				Assert.That(artifacts.VerifyArtifact(artifactId), Is.False);
			}

			public void Dispose()
			{
				if (_disposed) return;
				_disposed = true;
				if (Directory.Exists(Root)) Directory.Delete(Root, true);
			}
		}

		private sealed class DeterministicNativeBoundary
		{
			private readonly CollectionsOperationStore _operationStore;
			private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
			private readonly CollectionNativeStateReader _nativeStateReader;
			private readonly MutableInstallLogSnapshot _installState;
			private readonly string _archivePath;
			private long _deploymentSequence;

			public DeterministicNativeBoundary(CollectionsOperationStore operationStore,
				CollectionsNativeChildRecoveryManifestStore manifestStore, CollectionNativeStateReader nativeStateReader,
				MutableInstallLogSnapshot installState, string archivePath)
			{
				_operationStore = operationStore;
				_manifestStore = manifestStore;
				_nativeStateReader = nativeStateReader;
				_installState = installState;
				_archivePath = archivePath;
				_deploymentSequence = installState.Get().DeploymentCommitSequence;
			}

			public int CallCount { get; private set; }
			public ModInstallationRecipeInput LastRecipe { get; private set; }
			public CollectionNativeChildRecoveryManifest LastPreparedManifest { get; private set; }

			public Task<CollectionNativeChildVerificationResult> ApplyAsync(CollectionNativeChildPreparationResult prepared,
				ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan, CollectionMemberEffectPreview reviewedPreview,
				ModInstallationRecipeInput childRecipe, GameStoragePathSet targetPaths, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CallCount++;
				LastRecipe = childRecipe;
				CollectionOperation operation = _operationStore.GetOperation(prepared.Operation.Identity);
				CollectionNativeChildOperation child = operation.NativeChildren.Single(x => x.Sequence == prepared.Child.Sequence);
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
				LastPreparedManifest = manifest;

				var preFiles = reviewedPreview.Files.Select(x =>
					new CollectionNativeFileContentEvidence(x.Target, false, null, 0)).ToArray();
				CollectionContentHash expectedHash = ComputeHash(Encoding.UTF8.GetBytes("deterministic-native-boundary"));
				var expectedFiles = reviewedPreview.Files.Select(x =>
					new CollectionNativeFileContentEvidence(x.Target, true, expectedHash, 29)).ToArray();
				var expectedReplay = childRecipe.NativeOperations.OfType<InstallModFileOperation>()
					.Select(x => new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.ArchiveFile,
						x.SourcePath, x.DestinationPath, 0, null)).ToArray();
				var evidence = new CollectionNativeChildExecutionEvidence("skyrimspecialedition", 100, 200,
					Path.GetFileName(_archivePath), reviewedPreview, preFiles, expectedFiles,
					new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
					expectedReplay);
				_manifestStore.SaveManifest(manifest.WithExecutionEvidence(evidence));

				child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeSubmitted, null);
				operation = SaveChild(operation, child);

				string nativeKey = manifest.PreviousNativeMod == null ? "native-required" : manifest.PreviousNativeMod.Identity.NativeModKey;
				_deploymentSequence++;
				_installState.Set(CreateInstallSnapshot(nativeKey, _archivePath, childRecipe.InstallContext.Method,
					_deploymentSequence, reviewedPreview.Files.Select(x => x.Target), childRecipe.InstallContext.InstallRoot));
				CollectionNativeStateIndex terminalState = _nativeStateReader.Capture(plan.Target);
				CollectionNativeModState verifiedNativeMod = terminalState.Mods.Values.Single(x =>
					x.Identity.NativeModKey.Equals(nativeKey, StringComparison.OrdinalIgnoreCase));

				manifest = _manifestStore.GetManifest(operation, child);
				_manifestStore.SaveManifest(manifest.WithTerminalStateFingerprint(terminalState.Fingerprint));
				var nativeResult = new ModOperationResult(child.NativeOperation, ModOperationReportedStatus.Succeeded,
					ModOperationDurability.VerifiedCommitted, "C6.15.14 deterministic native boundary committed.");
				child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeTerminalObserved, nativeResult);
				operation = SaveChild(operation, child);

				return Task.FromResult(new CollectionNativeChildVerificationResult(operation, child, terminalState,
					verifiedNativeMod, ModOperationDurability.VerifiedCommitted));
			}

			private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
			{
				List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x => x.Sequence != child.Sequence).ToList();
				children.Add(child);
				var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
					operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
					operation.ResultState, children);
				_operationStore.SaveOperation(updated);
				return _operationStore.GetOperation(operation.Identity);
			}
		}

		private sealed class RecordingWinnerBarrier
		{
			public int CallCount { get; private set; }

			public Task ReconcileAsync(CollectionOperationIdentity operationIdentity, ResolvedCollectionPlan plan,
				CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan, GameStoragePathSet targetPaths,
				CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CallCount++;
				return Task.CompletedTask;
			}
		}

		private sealed class MutableInstallLogSnapshot
		{
			private InstallLogReadSnapshot _snapshot;
			public InstallLogReadSnapshot Get() { return _snapshot; }
			public void Set(InstallLogReadSnapshot snapshot) { _snapshot = snapshot; }
		}

		private sealed class FixedArchiveSource : ICollectionManagedArchiveSource
		{
			private readonly CollectionManagedArchiveCandidate _candidate;
			public FixedArchiveSource(CollectionManagedArchiveCandidate candidate) { _candidate = candidate; }
			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				return new[] { _candidate };
			}
		}

		private sealed class AcceptingVerifier : ICollectionArchiveIdentityVerifier
		{
			public bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive,
				CancellationToken cancellationToken)
			{
				return true;
			}
		}

		private sealed class FixedAccountProvider : ICollectionPremiumAcquisitionAccountProvider
		{
			private readonly bool _premium;
			public FixedAccountProvider(bool premium) { _premium = premium; }
			public CollectionPremiumAcquisitionAccountState Capture()
			{
				return new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, _premium);
			}
		}

		private sealed class EmptyPersistedAddModStateSource : ICollectionPersistedAddModStateSource
		{
			public CollectionPersistedAddModState Find(Guid queueOperationId) { return null; }
		}

		private sealed class FailingQueue : ICollectionAddModQueue
		{
			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				throw new InvalidOperationException("C6.15.14b exact-archive fixtures must not enqueue an acquisition.");
			}
		}

		private sealed class NoOpNativeStateReloader : ICollectionNativeStateReloader
		{
			public void Reload(string installLogPath)
			{
			}
		}

		private static GameStoragePathSet CreateStoragePaths(string root)
		{
			string storage = Path.Combine(root, "Storage");
			string installInfo = Path.Combine(storage, "InstallInfo");
			string mods = Path.Combine(storage, "Mods");
			string virtualInstall = Path.Combine(storage, "VirtualInstall");
			string game = Path.Combine(root, "Game");
			Directory.CreateDirectory(installInfo);
			Directory.CreateDirectory(mods);
			Directory.CreateDirectory(virtualInstall);
			Directory.CreateDirectory(game);
			File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog fileVersion=\"0.6.0.0\" />");
			return new GameStoragePathSet
			{
				GameId = "SkyrimSE",
				GameName = "Skyrim SE",
				GameInstallPath = game,
				InstallInfoPath = installInfo,
				ModsPath = mods,
				VirtualInstallPath = virtualInstall,
				LinkFolderRequired = false
			};
		}

		private static IGameMode CreateGameMode(GameStoragePathSet paths, bool requiresSpecialFileInstallation, bool requiresModFileMerge)
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ModeId": return paths.GameId;
					case "get_InstallationPath": return paths.GameInstallPath;
					case "get_PluginDirectory": return paths.GameInstallPath;
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return false;
					case "get_RequiresSpecialFileInstallation": return requiresSpecialFileInstallation;
					case "IsSpecialFile": return requiresSpecialFileInstallation;
					case "get_RequiresModFileMerge": return requiresModFileMerge;
					case "GetModFormatAdjustedPath": return args[1];
					default: return null;
				}
			});
		}

		private static IMod CreateManagedMod(string archivePath, IEnumerable<string> archiveFiles)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Id": return "100";
					case "get_DownloadId": return "200";
					case "get_ModName": return "Required";
					case "get_Filename":
					case "get_ModArchivePath": return archivePath;
					case "get_FileName": return Path.GetFileName(archivePath);
					case "get_HumanReadableVersion": return "1.0";
					case "GetFileList": return new List<string>(archiveFiles);
					default: return null;
				}
			});
		}

		private static ModManager CreateModManagerShell(IGameMode gameMode, IInstallLog installLog, IMod mod,
			ModInstallMethod preferredInstallMethod, bool skipReadmeFiles)
		{
			var registry = new ModRegistry(InterfaceStub<IModFormatRegistry>.Create((method, args) => null), gameMode);
			FieldInfo registeredModsField = typeof(ModRegistry).GetField("m_oclRegisteredMods", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(registeredModsField, Is.Not.Null);
			var registeredMods = (ThreadSafeObservableList<IMod>)registeredModsField.GetValue(registry);
			registeredMods.Add(mod);

			var preferred = new PerGameModeSettings<string>();
			preferred["SkyrimSE"] = preferredInstallMethod.ToString();
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_PreferredInstallMethod": return preferred;
					case "get_SkipReadmeFiles": return skipReadmeFiles;
					default: return null;
				}
			});
			IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
				method.Name == "get_Settings" ? settings : null);
			IModRepository repository = InterfaceStub<IModRepository>.Create((method, args) =>
				method.Name == "get_GameDomainName" ? "skyrimspecialedition" : null);

			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			SetField(manager, "<GameMode>k__BackingField", gameMode);
			SetField(manager, "<InstallationLog>k__BackingField", installLog);
			SetField(manager, "<ManagedModRegistry>k__BackingField", registry);
			SetField(manager, "<EnvironmentInfo>k__BackingField", environment);
			SetField(manager, "<ModRepository>k__BackingField", repository);
			return manager;
		}

		private static InstallLogReadSnapshot CreateInstallSnapshot(string nativeKey, string archivePath,
			ModInstallMethod installMethod, long deploymentSequence, IEnumerable<ModDeploymentTarget> files,
			ModInstallRoot installRoot = ModInstallRoot.Data)
		{
			var mods = new List<InstallLogReadMod>();
			var installedFiles = new List<InstallLogReadFile>();
			if (!String.IsNullOrEmpty(nativeKey))
			{
				mods.Add(new InstallLogReadMod(nativeKey, archivePath, Path.GetFileName(archivePath), "100", "200",
					"1.0", "1.0", false, installRoot, installMethod, false));
				foreach (ModDeploymentTarget target in files)
					installedFiles.Add(new InstallLogReadFile(target.RelativePath, target, new[] { nativeKey }));
			}
			return new InstallLogReadSnapshot("original-values", deploymentSequence, mods, installedFiles,
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
		}

		private static void SetField(object target, string fieldName, object value)
		{
			FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(field, Is.Not.Null, "Missing field: " + fieldName);
			field.SetValue(target, value);
		}

		private static CollectionContentHash ComputeHash(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes))
					.Replace("-", String.Empty).ToLowerInvariant());
		}
	}
}
