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
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;
using Nexus.Client.Settings;
using Nexus.Client.Util.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.14c vertical coverage for reviewed plugin effects and additive file-winner behavior.
	/// </summary>
	[TestFixture]
	public class CollectionAdditiveWorkflowVerticalEffectsAndConflictsTests
	{
		[Test]
		public void Prepare_PluginProducingSimpleArchive_ReviewsImplicitActivationBeforeApproval()
		{
			using (Fixture fixture = Fixture.Create(Scenario.PluginArchive))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();

				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				PreparedCollectionNativeRecipe recipe = prepared.Runtime.PreparedRecipes.Single();
				Assert.That(recipe.EffectPreview.IsComplete, Is.True);
				Assert.That(recipe.EffectPreview.Files.Count, Is.EqualTo(1));
				Assert.That(recipe.EffectPreview.PluginEffects.Count, Is.EqualTo(1));
				CollectionPlannedPluginEffect plugin = recipe.EffectPreview.PluginEffects.Single();
				Assert.That(plugin.Kind, Is.EqualTo(CollectionPlannedPluginEffectKind.Activation));
				Assert.That(plugin.Active, Is.EqualTo(true));
				Assert.That(plugin.PluginPaths.Single(),
					Is.EqualTo(Path.GetFullPath(Path.Combine(fixture.PluginDirectory, "Example.esp"))));
				Assert.That(prepared.ImpactPlan.Status, Is.EqualTo(CollectionConflictImpactStatus.Ready));
				Assert.That(prepared.ImpactPlan.PluginImpacts.Count, Is.EqualTo(1));
				Assert.That(prepared.ImpactPlan.PluginImpacts.Single().Effect.Active, Is.EqualTo(true));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0),
					"Plugin activation must be part of the exact review before any native child is submitted.");

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);
				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(1));
				CollectionNativePluginState nativePlugin = fixture.NativeBoundary.LastTerminalState.Plugins.Values.Single();
				Assert.That(nativePlugin.FileName, Is.EqualTo(plugin.PluginPaths.Single()));
				Assert.That(nativePlugin.Active, Is.EqualTo(plugin.Active.Value),
					"The reviewed implicit activation must match the native terminal plugin state.");
			}
		}

		[Test]
		public void PrepareAndApply_TwoWritersWithPriority_ReconcilesReviewedWinnerAfterAllWritersExist()
		{
			using (Fixture fixture = Fixture.Create(Scenario.TwoWritersWithPriority))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();
				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				CollectionFileImpact impact = prepared.ImpactPlan.FileImpacts.Single(x => x.Writers.Count == 2);
				Assert.That(impact.PlannedWinner, Is.EqualTo(fixture.MemberA.Key));
				Assert.That(impact.Writers, Does.Contain(fixture.MemberA.Key));
				Assert.That(impact.Writers, Does.Contain(fixture.MemberB.Key));

				CollectionAdditiveWorkflowApplyResult applied = fixture.Apply(prepared);

				Assert.That(applied.Status, Is.EqualTo(CollectionAdditiveWorkflowApplyStatus.Committed));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(2));
				Assert.That(fixture.NativeBoundary.AppliedMembers.Last(), Is.EqualTo(fixture.MemberB.Key),
					"The deterministic child order intentionally leaves B as the transient last writer in this fixture.");
				Assert.That(fixture.WinnerBarrier.CallCount, Is.EqualTo(1));
				Assert.That(fixture.WinnerBarrier.NativeCallsAtBarrier, Is.EqualTo(2),
					"Reviewed winner reconciliation must run only after every required writer owner exists.");
				Assert.That(fixture.WinnerBarrier.PlannedWinner, Is.EqualTo(fixture.MemberA.Key));
				Assert.That(fixture.WinnerBarrier.LastAppliedMember, Is.EqualTo(fixture.MemberB.Key));
				Assert.That(fixture.WinnerBarrier.PlannedWinner, Is.Not.EqualTo(fixture.WinnerBarrier.LastAppliedMember),
					"The final file winner must come from the reviewed priority rule, never from child installation order.");
				Assert.That(applied.Finalization.Bindings.Count, Is.EqualTo(2));
			}
		}

		[Test]
		public void Prepare_UnrelatedExistingFileOwner_ReturnsActionRequiredWithoutNativeMutation()
		{
			using (Fixture fixture = Fixture.Create(Scenario.UnrelatedExistingOwner))
			{
				CollectionAdditiveWorkflowPreparationResult prepared = fixture.Prepare();

				Assert.That(prepared.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ActionRequired));
				Assert.That(prepared.Runtime, Is.Null);
				Assert.That(prepared.ImpactPlan.Status, Is.EqualTo(CollectionConflictImpactStatus.ActionRequired));
				Assert.That(prepared.ImpactPlan.Issues.Any(x =>
					x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired), Is.True);
				CollectionFileImpact impact = prepared.ImpactPlan.FileImpacts.Single();
				Assert.That(impact.CurrentOwnerKey, Is.EqualTo("native-unrelated"));
				Assert.That(fixture.NativeBoundary.CallCount, Is.EqualTo(0));
				Assert.That(fixture.WinnerBarrier.CallCount, Is.EqualTo(0));
			}
		}

		private enum Scenario
		{
			PluginArchive = 1,
			TwoWritersWithPriority = 2,
			UnrelatedExistingOwner = 3
		}

		private sealed class MemberSpec
		{
			public MemberSpec(string name, long modId, long fileId, string nativeKey, string archivePath, params string[] files)
			{
				Name = name;
				ModId = modId;
				FileId = fileId;
				NativeKey = nativeKey;
				ArchivePath = archivePath;
				Files = files;
			}

			public string Name { get; }
			public long ModId { get; }
			public long FileId { get; }
			public string NativeKey { get; }
			public string ArchivePath { get; }
			public string[] Files { get; }
			public CollectionMemberKey Key { get; set; }
		}

		private sealed class Fixture : IDisposable
		{
			private bool _disposed;

			private Fixture(string root, GameStoragePathSet paths, CollectionsStore store,
				NormalizedCollectionManifest manifest, CollectionAdditiveWorkflowCoordinator workflow,
				DeterministicNativeBoundary nativeBoundary, RecordingWinnerBarrier winnerBarrier,
				MemberSpec memberA, MemberSpec memberB, string pluginDirectory)
			{
				Root = root;
				Paths = paths;
				Store = store;
				Manifest = manifest;
				Workflow = workflow;
				NativeBoundary = nativeBoundary;
				WinnerBarrier = winnerBarrier;
				MemberA = memberA;
				MemberB = memberB;
				PluginDirectory = pluginDirectory;
			}

			public string Root { get; }
			public GameStoragePathSet Paths { get; }
			public CollectionsStore Store { get; }
			public NormalizedCollectionManifest Manifest { get; }
			public CollectionAdditiveWorkflowCoordinator Workflow { get; }
			public DeterministicNativeBoundary NativeBoundary { get; }
			public RecordingWinnerBarrier WinnerBarrier { get; }
			public MemberSpec MemberA { get; }
			public MemberSpec MemberB { get; }
			public string PluginDirectory { get; }

			public static Fixture Create(Scenario scenario)
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c6-15-14c-" + Guid.NewGuid().ToString("N"));
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

				CollectionIdentity collection = CollectionIdentity.FromNexus("c61514c-" + Guid.NewGuid().ToString("N"));
				int declaredMemberCount = scenario == Scenario.TwoWritersWithPriority ? 2 : 1;
				var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1),
					"Revision 1", null, declaredMemberCount);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C6.15.14c", null, null), revision);

				string archiveA = Path.Combine(root, "member-a.zip");
				File.WriteAllBytes(archiveA, Encoding.UTF8.GetBytes("C6.15.14c archive A"));
				MemberSpec memberA;
				MemberSpec memberB = null;
				bool usesPlugins = scenario == Scenario.PluginArchive;
				string modRules = "[]";
				if (scenario == Scenario.PluginArchive)
				{
					memberA = new MemberSpec("Plugin Member", 100, 200, "native-a", archiveA, "Example.esp");
				}
				else
				{
					memberA = new MemberSpec("Member A", 100, 200, "native-a", archiveA, @"textures\shared.dds");
					if (scenario == Scenario.TwoWritersWithPriority)
					{
						string archiveB = Path.Combine(root, "member-b.zip");
						File.WriteAllBytes(archiveB, Encoding.UTF8.GetBytes("C6.15.14c archive B"));
						memberB = new MemberSpec("Member B", 101, 201, "native-b", archiveB, @"textures\shared.dds");
						modRules = "[{\"source\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrimspecialedition\",\"modId\":\"101\",\"fileId\":\"201\"}},\"type\":\"before\",\"reference\":{\"repo\":{\"repository\":\"nexus\",\"gameId\":\"skyrimspecialedition\",\"modId\":\"100\",\"fileId\":\"200\"}}}]";
					}
				}

				var specs = new List<MemberSpec> { memberA };
				if (memberB != null) specs.Add(memberB);
				string manifestJson = BuildManifest(specs, modRules);
				byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
				NexusCollectionManifestNormalizationResult normalization = new NexusCollectionManifestNormalizer().Normalize(manifestBytes, revision);
				Assert.That(normalization.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.Supported));
				NormalizedCollectionManifest manifest = normalization.Manifest;
				for (int index = 0; index < specs.Count; index++)
					specs[index].Key = manifest.Members[index].IdentityResolution.Key;
				revisionSources.RetainManifest(manifest, CollectionRevisionSourceInputKind.RawManifest,
					manifest.Source.ContentHash, manifest.Source.ByteLength, "collection.json", manifestBytes);

				var managedMods = specs.ToDictionary(x => x, CreateManagedMod);
				var modKeys = new Dictionary<IMod, string>();
				var activeMods = new ThreadSafeObservableList<IMod>();
				var installState = new MutableInstallLogState();
				if (scenario == Scenario.UnrelatedExistingOwner)
				{
					string unrelatedArchive = Path.Combine(root, "unrelated.zip");
					File.WriteAllBytes(unrelatedArchive, Encoding.UTF8.GetBytes("unrelated existing archive"));
					MemberSpec unrelatedSpec = new MemberSpec("Unrelated", 999, 999, "native-unrelated", unrelatedArchive, @"textures\shared.dds");
					IMod unrelated = CreateManagedMod(unrelatedSpec);
					managedMods.Add(unrelatedSpec, unrelated);
					modKeys.Add(unrelated, unrelatedSpec.NativeKey);
					activeMods.Add(unrelated);
					installState.Seed(unrelatedSpec, ModInstallMethod.Direct,
						new[] { ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, @"textures\shared.dds") });
				}
				var readOnlyActiveMods = new ReadOnlyObservableList<IMod>(activeMods);

				IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "GetCommittedStateSnapshot": return installState.GetSnapshot();
						case "get_ActiveMods": return readOnlyActiveMods;
						case "GetModKey":
							if (args != null && args.Length > 0 && args[0] is IMod)
							{
								string key;
								return modKeys.TryGetValue((IMod)args[0], out key) ? key : null;
							}
							return null;
						default: return null;
					}
				});
				IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
					method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
				string pluginDirectory = Path.Combine(paths.GameInstallPath, "Data");
				Directory.CreateDirectory(pluginDirectory);
				IGameMode gameMode = CreateGameMode(paths, usesPlugins, pluginDirectory);
				MutablePluginState pluginState = usesPlugins ? new MutablePluginState(pluginDirectory) : null;
				IPluginManager pluginManager = pluginState == null ? null : CreatePluginManager(pluginState);
				var nativeStateReader = new CollectionNativeStateReader(installLog, virtualModActivator, pluginManager, gameMode, associations);
				var targetResolver = new CollectionTargetIdentityResolver(storageService);
				var planBuilder = new CollectionResolvedPlanBuilder(catalog, revisionSources, operationCoordinator);
				var planPreparation = new CollectionAdditivePlanPreparationService(targetResolver, nativeStateReader, planBuilder);

				var archiveSource = new DictionaryArchiveSource(specs.Select(x => new CollectionManagedArchiveCandidate(
					"nexus-mod-file", "skyrimspecialedition/" + x.ModId + "/" + x.FileId, x.ArchivePath)));
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

				ModManager manager = CreateModManagerShell(gameMode, installLog, managedMods.Values, ModInstallMethod.Direct);
				var services = new ServiceManager(installLog, null, null, null, manager, pluginManager, null, null);
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
				var specByKey = specs.ToDictionary(x => x.Key);
				var nativeBoundary = new DeterministicNativeBoundary(operationStore, recoveryManifests, nativeStateReader,
					installState, specByKey, pluginState);
				var winnerBarrier = new RecordingWinnerBarrier(nativeBoundary);

				var workflow = new CollectionAdditiveWorkflowCoordinator(services, storageService, operationStore, planStore,
					planPreparation, revalidation, memberAcquisition, new CollectionNativeRecipePreparer(store),
					new CollectionDependencyPhasePlanner(), new CollectionConflictImpactPlanner(), operationCoordinator,
					rehydrator, runtimeReconstructor, nativeStateReader, childPreparation, childExecution, childVerification,
					childRestart, associationCoordinator, winnerCoordinator, CollectionTargetMutationLeaseManager.Shared,
					new CollectionTargetOwnershipAuthorityValidator(storageService, services), nativeBoundary.ApplyAsync,
					winnerBarrier.ReconcileAsync);

				return new Fixture(root, paths, store, manifest, workflow, nativeBoundary, winnerBarrier,
					memberA, memberB, pluginDirectory);
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
			private readonly MutableInstallLogState _installState;
			private readonly IDictionary<CollectionMemberKey, MemberSpec> _specs;
			private readonly MutablePluginState _pluginState;
			private readonly List<CollectionMemberKey> _appliedMembers = new List<CollectionMemberKey>();

			public DeterministicNativeBoundary(CollectionsOperationStore operationStore,
				CollectionsNativeChildRecoveryManifestStore manifestStore, CollectionNativeStateReader nativeStateReader,
				MutableInstallLogState installState, IDictionary<CollectionMemberKey, MemberSpec> specs, MutablePluginState pluginState)
			{
				_operationStore = operationStore;
				_manifestStore = manifestStore;
				_nativeStateReader = nativeStateReader;
				_installState = installState;
				_specs = specs;
				_pluginState = pluginState;
			}

			public int CallCount { get; private set; }
			public IReadOnlyList<CollectionMemberKey> AppliedMembers { get { return _appliedMembers; } }
			public CollectionNativeStateIndex LastTerminalState { get; private set; }

			public Task<CollectionNativeChildVerificationResult> ApplyAsync(CollectionNativeChildPreparationResult prepared,
				ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan, CollectionMemberEffectPreview reviewedPreview,
				ModInstallationRecipeInput childRecipe, GameStoragePathSet targetPaths, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CallCount++;
				MemberSpec spec = _specs[prepared.Child.Member.MemberKey];
				_appliedMembers.Add(spec.Key);
				CollectionOperation operation = _operationStore.GetOperation(prepared.Operation.Identity);
				CollectionNativeChildOperation child = operation.NativeChildren.Single(x => x.Sequence == prepared.Child.Sequence);
				CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);

				var preFiles = reviewedPreview.Files.Select(x =>
					new CollectionNativeFileContentEvidence(x.Target, false, null, 0)).ToArray();
				CollectionContentHash expectedHash = ComputeHash(Encoding.UTF8.GetBytes("c6-15-14c-native-boundary"));
				var expectedFiles = reviewedPreview.Files.Select(x =>
					new CollectionNativeFileContentEvidence(x.Target, true, expectedHash, 25)).ToArray();
				var expectedReplay = childRecipe.NativeOperations.OfType<InstallModFileOperation>()
					.Select(x => new CollectionExpectedReplayOperation(ScriptedReplayOperationKind.ArchiveFile,
						x.SourcePath, x.DestinationPath, 0, null)).ToArray();
				var evidence = new CollectionNativeChildExecutionEvidence("skyrimspecialedition", spec.ModId, spec.FileId,
					Path.GetFileName(spec.ArchivePath), reviewedPreview, preFiles, expectedFiles,
					new CollectionReplayContentEvidence(false, null, 0, false, new CollectionReplayPayloadContentEvidence[0]),
					expectedReplay);
				_manifestStore.SaveManifest(manifest.WithExecutionEvidence(evidence));

				child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
					CollectionNativeChildCheckpoint.NativeSubmitted, null);
				operation = SaveChild(operation, child);

				_installState.Commit(spec, childRecipe.InstallContext.Method, reviewedPreview.Files.Select(x => x.Target));
				if (_pluginState != null)
					_pluginState.ApplyNativeRecipe(childRecipe);
				CollectionNativeStateIndex terminalState = _nativeStateReader.Capture(plan.Target);
				LastTerminalState = terminalState;
				CollectionNativeModState verifiedNativeMod = terminalState.Mods.Values.Single(x =>
					x.Identity.NativeModKey.Equals(spec.NativeKey, StringComparison.OrdinalIgnoreCase));

				manifest = _manifestStore.GetManifest(operation, child);
				_manifestStore.SaveManifest(manifest.WithTerminalStateFingerprint(terminalState.Fingerprint));
				var nativeResult = new ModOperationResult(child.NativeOperation, ModOperationReportedStatus.Succeeded,
					ModOperationDurability.VerifiedCommitted, "C6.15.14c deterministic native boundary committed.");
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
			private readonly DeterministicNativeBoundary _nativeBoundary;
			public RecordingWinnerBarrier(DeterministicNativeBoundary nativeBoundary) { _nativeBoundary = nativeBoundary; }
			public int CallCount { get; private set; }
			public int NativeCallsAtBarrier { get; private set; }
			public CollectionMemberKey PlannedWinner { get; private set; }
			public CollectionMemberKey LastAppliedMember { get; private set; }

			public Task ReconcileAsync(CollectionOperationIdentity operationIdentity, ResolvedCollectionPlan plan,
				CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan, GameStoragePathSet targetPaths,
				CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CallCount++;
				NativeCallsAtBarrier = _nativeBoundary.CallCount;
				CollectionFileImpact multiWriter = impactPlan.FileImpacts.SingleOrDefault(x => x.Writers.Count > 1);
				PlannedWinner = multiWriter == null ? null : multiWriter.PlannedWinner;
				LastAppliedMember = _nativeBoundary.AppliedMembers.LastOrDefault();
				return Task.CompletedTask;
			}
		}

		private sealed class MutableInstallLogState
		{
			private readonly Dictionary<string, NativeRecord> _mods = new Dictionary<string, NativeRecord>(StringComparer.OrdinalIgnoreCase);
			private readonly Dictionary<ModDeploymentTarget, List<string>> _owners = new Dictionary<ModDeploymentTarget, List<string>>();
			private long _deploymentSequence;

			public void Seed(MemberSpec spec, ModInstallMethod method, IEnumerable<ModDeploymentTarget> files)
			{
				Commit(spec, method, files);
			}

			public void Commit(MemberSpec spec, ModInstallMethod method, IEnumerable<ModDeploymentTarget> files)
			{
				_mods[spec.NativeKey] = new NativeRecord(spec, method);
				foreach (ModDeploymentTarget target in files)
				{
					List<string> owners;
					if (!_owners.TryGetValue(target, out owners))
					{
						owners = new List<string>();
						_owners.Add(target, owners);
					}
					owners.RemoveAll(x => x.Equals(spec.NativeKey, StringComparison.OrdinalIgnoreCase));
					owners.Add(spec.NativeKey);
				}
				_deploymentSequence++;
			}

			public InstallLogReadSnapshot GetSnapshot()
			{
				var mods = _mods.Values.Select(x => new InstallLogReadMod(x.Spec.NativeKey, x.Spec.ArchivePath,
					Path.GetFileName(x.Spec.ArchivePath), x.Spec.ModId.ToString(), x.Spec.FileId.ToString(),
					"1.0", "1.0", false, ModInstallRoot.Data, x.Method, false)).ToList();
				var files = _owners.Select(x => new InstallLogReadFile(x.Key.RelativePath, x.Key, x.Value.ToArray())).ToList();
				return new InstallLogReadSnapshot("original-values", _deploymentSequence, mods, files,
					new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
			}

			private sealed class NativeRecord
			{
				public NativeRecord(MemberSpec spec, ModInstallMethod method) { Spec = spec; Method = method; }
				public MemberSpec Spec { get; }
				public ModInstallMethod Method { get; }
			}
		}

		private sealed class DictionaryArchiveSource : ICollectionManagedArchiveSource
		{
			private readonly List<CollectionManagedArchiveCandidate> _candidates;
			public DictionaryArchiveSource(IEnumerable<CollectionManagedArchiveCandidate> candidates)
			{
				_candidates = candidates.ToList();
			}
			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				return _candidates.Where(x => StringComparer.Ordinal.Equals(x.Scheme, requestedArtifact.Scheme) &&
					StringComparer.Ordinal.Equals(x.StableId, requestedArtifact.StableId)).ToArray();
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
				throw new InvalidOperationException("C6.15.14c exact-archive fixtures must not enqueue an acquisition.");
			}
		}

		private static string BuildManifest(IList<MemberSpec> specs, string modRules)
		{
			string members = String.Join(",", specs.Select(x => String.Format(
				"{{\"name\":\"{0}\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{{\"type\":\"nexus\",\"modId\":{1},\"fileId\":{2},\"updatePolicy\":\"exact\"}}}}",
				x.Name, x.ModId, x.FileId)));
			return "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"C6.15.14c\",\"description\":\"Vertical effects/conflicts fixture\",\"domainName\":\"skyrimspecialedition\"}," +
				"\"mods\":[" + members + "],\"modRules\":" + modRules + "}";
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
			File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");
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

		private static IGameMode CreateGameMode(GameStoragePathSet paths, bool usesPlugins, string pluginDirectory)
		{
			return InterfaceStub<IGameMode>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ModeId": return paths.GameId;
					case "get_InstallationPath": return paths.GameInstallPath;
					case "get_PluginDirectory": return pluginDirectory;
					case "get_HasSecondaryInstallPath": return false;
					case "get_UsesPlugins": return usesPlugins;
					case "GetModFormatAdjustedPath": return args[1];
					case "CheckSecondaryInstall": return false;
					default: return null;
				}
			});
		}

		private sealed class MutablePluginState
		{
			private readonly string _pluginDirectory;
			private readonly Dictionary<string, bool> _active = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

			public MutablePluginState(string pluginDirectory)
			{
				_pluginDirectory = pluginDirectory;
			}

			public PluginSnapshot Snapshot
			{
				get
				{
					var entries = _active.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select((x, index) =>
						new PluginSnapshotEntry(new Plugin(x.Key, null, null), x.Value, index, null, String.Empty,
							new PluginValidationDiagnostic[0])).ToList();
					return new PluginSnapshot(entries, new PluginValidationDiagnostic[0]);
				}
			}

			public void ApplyNativeRecipe(ModInstallationRecipeInput recipe)
			{
				foreach (InstallModFileOperation operation in recipe.NativeOperations.OfType<InstallModFileOperation>())
				{
					string extension = Path.GetExtension(operation.DestinationPath);
					if (!extension.Equals(".esp", StringComparison.OrdinalIgnoreCase) &&
						!extension.Equals(".esm", StringComparison.OrdinalIgnoreCase) &&
						!extension.Equals(".esl", StringComparison.OrdinalIgnoreCase))
						continue;
					_active[Path.GetFullPath(Path.Combine(_pluginDirectory, operation.DestinationPath))] = true;
				}
			}
		}

		private static IPluginManager CreatePluginManager(MutablePluginState state)
		{
			return InterfaceStub<IPluginManager>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_CurrentSnapshot": return state.Snapshot;
					case "IsActivatiblePluginFile":
						string path = args == null || args.Length == 0 ? String.Empty : args[0] as string;
						string extension = Path.GetExtension(path ?? String.Empty);
						return extension.Equals(".esp", StringComparison.OrdinalIgnoreCase) ||
							extension.Equals(".esm", StringComparison.OrdinalIgnoreCase) ||
							extension.Equals(".esl", StringComparison.OrdinalIgnoreCase);
					default: return null;
				}
			});
		}

		private static IMod CreateManagedMod(MemberSpec spec)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Id": return spec.ModId.ToString();
					case "get_DownloadId": return spec.FileId.ToString();
					case "get_ModName": return spec.Name;
					case "get_Filename":
					case "get_ModArchivePath": return spec.ArchivePath;
					case "get_FileName": return Path.GetFileName(spec.ArchivePath);
					case "get_HumanReadableVersion": return "1.0";
					case "GetFileList": return new List<string>(spec.Files);
					default: return null;
				}
			});
		}

		private static ModManager CreateModManagerShell(IGameMode gameMode, IInstallLog installLog,
			IEnumerable<IMod> mods, ModInstallMethod preferredInstallMethod)
		{
			var registry = new ModRegistry(InterfaceStub<IModFormatRegistry>.Create((method, args) => null), gameMode);
			FieldInfo registeredModsField = typeof(ModRegistry).GetField("m_oclRegisteredMods", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.That(registeredModsField, Is.Not.Null);
			var registeredMods = (ThreadSafeObservableList<IMod>)registeredModsField.GetValue(registry);
			foreach (IMod mod in mods) registeredMods.Add(mod);

			var preferred = new PerGameModeSettings<string>();
			preferred["SkyrimSE"] = preferredInstallMethod.ToString();
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_PreferredInstallMethod": return preferred;
					case "get_SkipReadmeFiles": return false;
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
