using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.14a/14g vertical planning/preparation coverage through the real headless additive workflow coordinator.
	/// </summary>
	[TestFixture]
	public class CollectionAdditiveWorkflowVerticalPreparationTests
	{
		private const string SourceArchiveBase64 = "UEsDBBQAAAAIAI2QN11CiHJS4QAAAGIBAAAPAAAAY29sbGVjdGlvbi5qc29ujVC7TsQwEOz5itPWVnJBQOGWigahk6BBV6zsDbfCL/w4JYry79jJ0dPtzM7sjHYBdqMHuQCWfPERJDyXiLlO4ka9R1PZS84hyb6nCW0w1LG7omHd32wCHFpq7qdueOyGh69KaUoqcsjsXd18UMys0BySL1HRYeQpl0hN5y2ye90PpO85sk2BFKMhzZt7FWC9TiA/l7+gE/0UjqSr/0ox7RlDRX4LxFp6RJPoP+cF7J3aH/Icms7RVBJssS8a5HA8ChjZUAP3DZSgMdObN6zmqq9/URnW9bxZTsVQa3te734BUEsBAh4DFAAAAAgAjZA3XUKIclLhAAAAYgEAAA8AAAAAAAAAAQAAAKSBAAAAAGNvbGxlY3Rpb24uanNvblBLBQYAAAAAAQABAD0AAAAOAQAAAAA=";

		public enum RevisionSourceMode
		{
			DirectRetainedManifest = 0,
			LocalRawImport = 1,
			LocalArchiveImport = 2,
			RemoteArchiveAcquisition = 3
		}

		[TestCase(RevisionSourceMode.LocalRawImport)]
		[TestCase(RevisionSourceMode.LocalArchiveImport)]
		[TestCase(RevisionSourceMode.RemoteArchiveAcquisition)]
		public void Prepare_ImportedOrAcquiredRevisionSource_ComposesIntoDurableReadyReview(RevisionSourceMode sourceMode)
		{
			using (Fixture fixture = Fixture.Create(seedCompatibleNativeState: true, premium: false, includeOptional: false, sourceMode: sourceMode))
			{
				CollectionAdditiveWorkflowPreparationResult result = fixture.Workflow.PrepareAsync(
					fixture.BuildSelection(false), fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(result.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(result.Runtime.Plan.ManifestSource, Is.EqualTo(fixture.Manifest.Source));
				Assert.That(result.Runtime.Matches.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
				CollectionRevisionSourceRecord source = new CollectionsRevisionSourceStore(fixture.Store).GetSource(fixture.Revision.Identity);
				Assert.That(source, Is.Not.Null);
				Assert.That(source.RawManifestArtifactId, Is.Not.Null.And.Not.Empty);
				Assert.That(source.RawBundleArtifactId, Is.Not.Null.And.Not.Empty);
				Assert.That(source.InputKind, Is.EqualTo(sourceMode == RevisionSourceMode.LocalRawImport
					? CollectionRevisionSourceInputKind.RawManifest
					: CollectionRevisionSourceInputKind.Archive));
				Assert.That(fixture.Workflow.GetReview(result.Operation.Identity).IsReady, Is.True);
			}
		}
		[TestCase("latest")]
		[TestCase("prefer")]
		public void Prepare_UnresolvedSourcePolicy_RemainsActionRequiredAndCreatesNoWorkflowOperation(string updatePolicy)
		{
			using (Fixture fixture = Fixture.Create(seedCompatibleNativeState: true, premium: false, includeOptional: false,
				updatePolicy: updatePolicy))
			{
				CollectionEffectiveSelection selection = fixture.BuildSelection(false);
				Assert.That(selection.CapabilityReport.Status, Is.EqualTo(CollectionCompatibilityStatus.ActionRequired));
				Assert.Throws<InvalidOperationException>(() => fixture.Workflow.PrepareAsync(
					selection, fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult());
				Assert.That(new CollectionsOperationStore(fixture.Store).GetIncompleteOperations(), Is.Empty,
					"An unresolved latest/prefer source policy must not create a durable apply operation before an exact artifact is chosen.");
			}
		}

		[Test]
		public void Prepare_RequiredInstalledCompatibleMember_ReachesDurableReadyReviewWithoutRecipeOrAcquisition()
		{
			using (Fixture fixture = Fixture.Create(seedCompatibleNativeState: true, premium: false, includeOptional: false))
			{
				CollectionAdditiveWorkflowPreparationResult result = fixture.Workflow.PrepareAsync(
					fixture.BuildSelection(false), fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(result.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(result.Operation.Phase, Is.EqualTo(CollectionOperationPhase.ReadyForReview));
				Assert.That(result.Runtime, Is.Not.Null);
				Assert.That(result.Runtime.Matches.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
				Assert.That(result.Runtime.PreparedRecipes, Is.Empty);
				Assert.That(result.AcquisitionBatch.IsReady, Is.True);
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(0));

				CollectionAdditiveWorkflowReview review = fixture.Workflow.GetReview(result.Operation.Identity);
				Assert.That(review.IsReady, Is.True);
				Assert.That(review.Runtime.Plan.Identity, Is.EqualTo(result.Runtime.Plan.Identity));
				Assert.That(review.Runtime.RemainingMembers.Count, Is.EqualTo(1),
					"A reviewed no-op member remains in deterministic workflow order but must not require a prepared native recipe.");
				Assert.That(review.Operation.NativeChildren, Is.Empty,
					"Preparation of a fully compatible member must not create a native child intent.");
			}
		}

		[Test]
		public void Prepare_OptionalSelectionChange_ProducesNewExactReviewWithChangedSelectedClosure()
		{
			using (Fixture fixture = Fixture.Create(seedCompatibleNativeState: true, premium: false, includeOptional: true))
			{
				CollectionAdditiveWorkflowPreparationResult requiredOnly = fixture.Workflow.PrepareAsync(
					fixture.BuildSelection(false), fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult();
				Assert.That(requiredOnly.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(requiredOnly.Runtime.Plan.SelectedMembers.Count, Is.EqualTo(1));
				fixture.Workflow.CancelBeforeApply(requiredOnly.Operation.Identity);

				CollectionAdditiveWorkflowPreparationResult withOptional = fixture.Workflow.PrepareAsync(
					fixture.BuildSelection(true), fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(withOptional.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(withOptional.Runtime.Plan.SelectedMembers.Count, Is.EqualTo(2));
				Assert.That(withOptional.Runtime.Plan.Identity, Is.Not.EqualTo(requiredOnly.Runtime.Plan.Identity));
				Assert.That(withOptional.Runtime.Plan.SelectedMembers.Select(x => x.MemberKey),
					Does.Contain(fixture.OptionalMember.IdentityResolution.Key));
			}
		}

		[Test]
		public void ResumePreparation_ManualPauseRecapturesChangedNativeStateAndAvoidsRedundantInstall()
		{
			using (Fixture fixture = Fixture.Create(seedCompatibleNativeState: false, premium: false, includeOptional: false))
			{
				CollectionAdditiveWorkflowPreparationResult pending = fixture.Workflow.PrepareAsync(
					fixture.BuildSelection(false), fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(pending.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.AwaitingInput));
				Assert.That(pending.Operation.Phase, Is.EqualTo(CollectionOperationPhase.AwaitingInput));
				Assert.That(pending.AcquisitionBatch.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberAcquisitionDisposition.ManualInputRequired));
				CollectionPlanIdentity beforePausePlan = pending.AcquisitionBatch.PlanBuild.Plan.Identity;

				fixture.MakeExactArchiveAvailable();
				fixture.SeedCompatibleNativeState(deploymentSequence: 1);

				CollectionAdditiveWorkflowPreparationResult resumed = fixture.Workflow.ResumePreparationAsync(
					pending.AcquisitionBatch, fixture.Paths, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(resumed.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(resumed.Runtime.Plan.Identity.PlanId, Is.EqualTo(beforePausePlan.PlanId));
				Assert.That(resumed.Runtime.Plan.Identity.Version, Is.GreaterThan(beforePausePlan.Version));
				Assert.That(resumed.Runtime.Matches.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
				Assert.That(resumed.Runtime.PreparedRecipes, Is.Empty,
					"Post-pause revalidation must adopt newly compatible native reality instead of reinstalling reviewed work blindly.");
			}
		}

		[Test]
		public void Prepare_PremiumAcquisition_QueuesExactNexusIdentityThenResumesThroughStateRevalidation()
		{
			using (Fixture fixture = Fixture.Create(seedCompatibleNativeState: false, premium: true, includeOptional: false))
			{
				CollectionAdditiveWorkflowPreparationResult result = fixture.Workflow.PrepareAsync(
					fixture.BuildSelection(false), fixture.Paths, null, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(result.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.AwaitingInput));
				Assert.That(result.Operation.Phase, Is.EqualTo(CollectionOperationPhase.AwaitingInput));
				Assert.That(result.AcquisitionBatch.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberAcquisitionDisposition.PremiumQueued));
				Assert.That(fixture.Queue.CallCount, Is.EqualTo(1));
				Assert.That(fixture.Queue.LastUri, Is.Not.Null);
				Assert.That(fixture.Queue.LastUri.Scheme, Is.EqualTo("nxm"));
				Assert.That(fixture.Queue.LastUri.Host, Is.EqualTo("skyrimspecialedition").IgnoreCase);
				StringAssert.Contains("/mods/100/files/200", fixture.Queue.LastUri.AbsolutePath);
				Assert.That(new CollectionsResolvedPlanStore(fixture.Store)
					.GetPlan(result.AcquisitionBatch.PlanBuild.Plan.Identity), Is.Null,
					"An acquisition pause must not persist a reviewed-plan payload before recipe/effect review exists.");

				CollectionPlanIdentity queuedPlan = result.AcquisitionBatch.PlanBuild.Plan.Identity;
				fixture.MakeExactArchiveAvailable();
				fixture.SeedCompatibleNativeState(deploymentSequence: 1);
				CollectionAdditiveWorkflowPreparationResult resumed = fixture.Workflow.ResumePreparationAsync(
					result.AcquisitionBatch, fixture.Paths, CancellationToken.None).GetAwaiter().GetResult();

				Assert.That(resumed.Status, Is.EqualTo(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview));
				Assert.That(resumed.Runtime.Plan.Identity.PlanId, Is.EqualTo(queuedPlan.PlanId));
				Assert.That(resumed.Runtime.Plan.Identity.Version, Is.GreaterThan(queuedPlan.Version));
				Assert.That(resumed.Runtime.Matches.Members.Single().Disposition,
					Is.EqualTo(CollectionMemberMatchDisposition.InstalledCompatible));
				Assert.That(resumed.Runtime.PreparedRecipes, Is.Empty);
			}
		}

		private sealed class Fixture : IDisposable
		{
			private readonly MutableInstallLogSnapshot _installState;
			private readonly CollectionsAssociationStore _associations;
			private readonly CollectionTargetAssociation _compatibleAssociation;
			private readonly NativeModInstanceIdentity _requiredNativeIdentity;
			private readonly NativeModInstanceIdentity _optionalNativeIdentity;
			private readonly string _archivePath;
			private bool _disposed;

			private Fixture(string root, GameStorageService storageService, GameStoragePathSet paths,
				CollectionsStore store, CollectionRevision revision, NormalizedCollectionManifest manifest,
				CollectionCapabilityReport capabilityReport, NormalizedCollectionMember requiredMember, NormalizedCollectionMember optionalMember,
				CollectionTargetIdentity target, MutableInstallLogSnapshot installState,
				CollectionsAssociationStore associations, CollectionTargetAssociation compatibleAssociation,
				NativeModInstanceIdentity requiredNativeIdentity, NativeModInstanceIdentity optionalNativeIdentity,
				MutableArchiveSource archiveSource, RecordingQueue queue, CollectionAdditiveWorkflowCoordinator workflow,
				string archivePath)
			{
				Root = root;
				StorageService = storageService;
				Paths = paths;
				Store = store;
				Revision = revision;
				Manifest = manifest;
				CapabilityReport = capabilityReport ?? throw new ArgumentNullException(nameof(capabilityReport));
				RequiredMember = requiredMember;
				OptionalMember = optionalMember;
				Target = target;
				_installState = installState;
				_associations = associations;
				_compatibleAssociation = compatibleAssociation;
				_requiredNativeIdentity = requiredNativeIdentity;
				_optionalNativeIdentity = optionalNativeIdentity;
				ArchiveSource = archiveSource;
				Queue = queue;
				Workflow = workflow;
				_archivePath = archivePath;
			}

			public string Root { get; }
			public GameStorageService StorageService { get; }
			public GameStoragePathSet Paths { get; }
			public CollectionsStore Store { get; }
			public CollectionRevision Revision { get; }
			public NormalizedCollectionManifest Manifest { get; }
			public CollectionCapabilityReport CapabilityReport { get; }
			public NormalizedCollectionMember RequiredMember { get; }
			public NormalizedCollectionMember OptionalMember { get; }
			public CollectionTargetIdentity Target { get; }
			public MutableArchiveSource ArchiveSource { get; }
			public RecordingQueue Queue { get; }
			public CollectionAdditiveWorkflowCoordinator Workflow { get; }

			public static Fixture Create(bool seedCompatibleNativeState, bool premium, bool includeOptional,
				RevisionSourceMode sourceMode = RevisionSourceMode.DirectRetainedManifest, string updatePolicy = "exact")
			{
				string root = Path.Combine(Path.GetTempPath(), "nmm-c6-15-14a-" + Guid.NewGuid().ToString("N"));
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

				if (sourceMode != RevisionSourceMode.DirectRetainedManifest && (includeOptional || !StringComparer.OrdinalIgnoreCase.Equals(updatePolicy, "exact")))
					throw new InvalidOperationException("C6.15.14g imported/acquired source fixtures use the required-only exact-policy manifest.");
				CollectionIdentity collection = sourceMode == RevisionSourceMode.RemoteArchiveAcquisition
					? CollectionIdentity.FromNexus("2210")
					: CollectionIdentity.FromNexus("c61514a-" + Guid.NewGuid().ToString("N"));
				var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection,
					sourceMode == RevisionSourceMode.RemoteArchiveAcquisition ? "772530" : "revision-one", 1),
					"Revision 1", null, includeOptional ? 2 : 1);
				catalog.SaveDefinitionAndRevision(new CollectionDefinition(collection, "C6.15.14a", null, null), revision);

				byte[] archiveBytes = Encoding.UTF8.GetBytes("C6.15.14a exact archive bytes");
				string archivePath = Path.Combine(root, "member.zip");
				File.WriteAllBytes(archivePath, archiveBytes);

				string manifestJson = "{" +
					"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"C6.15.14a\",\"description\":\"Vertical preparation fixture\",\"domainName\":\"skyrimspecialedition\"}," +
					"\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":100,\"fileId\":200,\"updatePolicy\":\"" + updatePolicy + "\"}}" +
					(includeOptional ? ",{\"name\":\"Optional\",\"version\":\"1\",\"optional\":true,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":101,\"fileId\":201,\"updatePolicy\":\"exact\"}}" : String.Empty) +
					"],\"modRules\":[]}";
				byte[] manifestBytes = Encoding.UTF8.GetBytes(manifestJson);
				CollectionCapabilityReport retainedCapability;
				NormalizedCollectionManifest manifest = RetainRevisionSource(root, store, revisionSources, revision, manifestBytes,
					sourceMode, out retainedCapability);
				CollectionCompatibilityStatus expectedCapability = StringComparer.OrdinalIgnoreCase.Equals(updatePolicy, "exact")
					? CollectionCompatibilityStatus.Supported
					: CollectionCompatibilityStatus.ActionRequired;
				Assert.That(retainedCapability.Status, Is.EqualTo(expectedCapability));
				NormalizedCollectionMember required = manifest.Members.Single(x => x.Artifact.StableId == "skyrimspecialedition/100/200");
				NormalizedCollectionMember optional = includeOptional
					? manifest.Members.Single(x => x.Artifact.StableId == "skyrimspecialedition/101/201")
					: null;

				var compatibleAssociation = new CollectionTargetAssociation(Guid.NewGuid(), revision.Identity, target,
					CollectionAssociationState.Applied);
				var requiredNativeIdentity = new NativeModInstanceIdentity(target, "native-required");
				NativeModInstanceIdentity optionalNativeIdentity = optional == null ? null :
					new NativeModInstanceIdentity(target, "native-optional");
				var installState = new MutableInstallLogSnapshot();
				if (seedCompatibleNativeState)
				{
					installState.Set(CreateInstallSnapshot(includeOptional, 0));
					associations.SaveAssociation(compatibleAssociation);
					associations.SaveBinding(new CollectionMemberBinding(compatibleAssociation, required.IdentityResolution.Key,
						requiredNativeIdentity, required.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting));
					if (optional != null)
						associations.SaveBinding(new CollectionMemberBinding(compatibleAssociation, optional.IdentityResolution.Key,
							optionalNativeIdentity, optional.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting));
				}
				else
				{
					installState.Set(CreateInstallSnapshot(false, 0, includeRequired: false));
				}

				IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) =>
				{
					if (method.Name == "GetCommittedStateSnapshot") return installState.Get();
					return null;
				});
				IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
					method.Name == "GetReadSnapshot" ? new VirtualModReadSnapshot(new VirtualModReadLink[0]) : null);
				IGameMode gameMode = CreateGameMode(paths);
				var nativeStateReader = new CollectionNativeStateReader(installLog, virtualModActivator, null, gameMode, associations);
				var targetResolver = new CollectionTargetIdentityResolver(storageService);
				var planBuilder = new CollectionResolvedPlanBuilder(catalog, revisionSources, operationCoordinator);
				var planPreparation = new CollectionAdditivePlanPreparationService(targetResolver, nativeStateReader, planBuilder);

				var archiveSource = new MutableArchiveSource();
				var adopter = new CollectionVerifiedArchiveAdopter(archiveSource, new AcceptingVerifier(), artifacts, references, acquisitions);
				var queue = new RecordingQueue();
				var requestCoordinator = new CollectionAcquisitionRequestCoordinator(queue, acquisitions);
				var premiumCoordinator = new CollectionPremiumAcquisitionCoordinator(requestCoordinator,
					new FixedAccountProvider(premium));
				var manualCoordinator = new CollectionManualAcquisitionCoordinator(requestCoordinator, adopter);
				var acquisitionRestart = new CollectionAcquisitionRestartCoordinator(acquisitions,
					new EmptyPersistedAddModStateSource(), adopter, premiumCoordinator);
				var memberAcquisition = new CollectionMemberAcquisitionCoordinator(new CollectionMemberMatchEngine(), adopter,
					premiumCoordinator, manualCoordinator, acquisitionRestart, operationCoordinator);
				var revalidation = new CollectionAdditivePlanRevalidationService(targetResolver, nativeStateReader,
					operationCoordinator, planBuilder, memberAcquisition);

				ModManager manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
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

				var workflow = new CollectionAdditiveWorkflowCoordinator(services, storageService, operationStore, planStore,
					planPreparation, revalidation, memberAcquisition, new CollectionNativeRecipePreparer(store),
					new CollectionDependencyPhasePlanner(), new CollectionConflictImpactPlanner(), operationCoordinator,
					rehydrator, runtimeReconstructor, nativeStateReader, childPreparation, childExecution, childVerification,
					childRestart, associationCoordinator, winnerCoordinator,
					CollectionTargetMutationLeaseManager.Shared,
					new CollectionTargetOwnershipAuthorityValidator(storageService, services));

				return new Fixture(root, storageService, paths, store, revision, manifest, retainedCapability, required, optional, target,
					installState, associations, compatibleAssociation, requiredNativeIdentity, optionalNativeIdentity,
					archiveSource, queue, workflow, archivePath);
			}

			private static NormalizedCollectionManifest RetainRevisionSource(string root, CollectionsStore store,
				CollectionsRevisionSourceStore revisionSources, CollectionRevision revision, byte[] manifestBytes, RevisionSourceMode sourceMode,
				out CollectionCapabilityReport capabilityReport)
			{
				if (sourceMode == RevisionSourceMode.DirectRetainedManifest)
				{
					NexusCollectionManifestNormalizationResult normalization = new NexusCollectionManifestNormalizer().Normalize(manifestBytes, revision);
					revisionSources.RetainManifest(normalization.Manifest, CollectionRevisionSourceInputKind.RawManifest,
						normalization.Manifest.Source.ContentHash, normalization.Manifest.Source.ByteLength, "collection.json", manifestBytes);
					capabilityReport = normalization.CapabilityReport;
					return normalization.Manifest;
				}

				byte[] sourceBytes = sourceMode == RevisionSourceMode.LocalRawImport
					? manifestBytes
					: Convert.FromBase64String(SourceArchiveBase64);
				if (sourceMode == RevisionSourceMode.RemoteArchiveAcquisition)
				{
					var providerRevision = new NexusCollectionRevisionMetadata(2210, 772530, 1, 1, "/v2/collections/2210/revisions/772530/download_link");
					Uri contentUri = new Uri("https://collection-cdn.example.invalid/revision-1.zip?token=ephemeral");
					var provider = new FixedBundleProvider(providerRevision, contentUri);
					using (var acquirer = new NexusCollectionBundleAcquirer(provider, store,
						new FixedContentHandler(sourceBytes), NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
					{
						NexusCollectionBundleAcquisitionResult acquired = acquirer.AcquireAsync(providerRevision, revision).GetAwaiter().GetResult();
						Assert.That(provider.ResolveCallCount, Is.EqualTo(1));
						capabilityReport = acquired.BundleImport.Normalization.CapabilityReport;
						return acquired.BundleImport.Manifest;
					}
				}

				string sourcePath = Path.Combine(root, sourceMode == RevisionSourceMode.LocalRawImport ? "collection.json" : "collection.zip");
				File.WriteAllBytes(sourcePath, sourceBytes);
				NexusCollectionBundleImportResult imported = new NexusCollectionBundleImporter().ImportFile(sourcePath, revision);
				CollectionRevisionSourceInputKind inputKind = imported.InputKind == NexusCollectionBundleInputKind.RawManifest
					? CollectionRevisionSourceInputKind.RawManifest
					: CollectionRevisionSourceInputKind.Archive;
				revisionSources.RetainManifest(imported.Manifest, inputKind, imported.BundleContentHash, imported.BundleByteLength,
					imported.ManifestEntryName, imported.Normalization.GetRawManifestBytes());
				using (FileStream stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
					revisionSources.RetainBundle(revision.Identity, stream, CancellationToken.None);
				capabilityReport = imported.Normalization.CapabilityReport;
				return imported.Manifest;
			}

			public CollectionEffectiveSelection BuildSelection(bool selectOptional)
			{
				if (OptionalMember == null)
					return new CollectionEffectiveSelectionBuilder().Build(CapabilityReport, new CollectionOptionalMemberSelection[0]);
				return new CollectionEffectiveSelectionBuilder().Build(CapabilityReport, new[]
				{
					new CollectionOptionalMemberSelection(OptionalMember.IdentityResolution.Key,
						selectOptional ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected)
				});
			}

			public void MakeExactArchiveAvailable()
			{
				ArchiveSource.Candidates = new[]
				{
					new CollectionManagedArchiveCandidate("nexus-mod-file", "skyrimspecialedition/100/200", _archivePath)
				};
			}

			public void SeedCompatibleNativeState(long deploymentSequence)
			{
				_installState.Set(CreateInstallSnapshot(false, deploymentSequence));
				_associations.SaveAssociation(_compatibleAssociation);
				_associations.SaveBinding(new CollectionMemberBinding(_compatibleAssociation, RequiredMember.IdentityResolution.Key,
					_requiredNativeIdentity, RequiredMember.RecipeIdentity, CollectionMemberBindingKind.AdoptedExisting));
			}

			public void Dispose()
			{
				if (_disposed) return;
				_disposed = true;
				if (Directory.Exists(Root)) Directory.Delete(Root, true);
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

			private static IGameMode CreateGameMode(GameStoragePathSet paths)
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
						default: return null;
					}
				});
			}

			private static InstallLogReadSnapshot CreateInstallSnapshot(bool includeOptional, long deploymentSequence,
				bool includeRequired = true)
			{
				var mods = new List<InstallLogReadMod>();
				if (includeRequired)
					mods.Add(new InstallLogReadMod("native-required", "required.zip", "required.zip", "100", "200",
						"1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Virtual, false));
				if (includeOptional)
					mods.Add(new InstallLogReadMod("native-optional", "optional.zip", "optional.zip", "101", "201",
						"1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Virtual, false));
				return new InstallLogReadSnapshot("original-values", deploymentSequence, mods,
					new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
					new InstallLogReadDeploymentTarget[0]);
			}

		}

		private sealed class MutableInstallLogSnapshot
		{
			private InstallLogReadSnapshot _snapshot;
			public InstallLogReadSnapshot Get() { return _snapshot; }
			public void Set(InstallLogReadSnapshot snapshot) { _snapshot = snapshot; }
		}

		private sealed class MutableArchiveSource : ICollectionManagedArchiveSource
		{
			public IReadOnlyList<CollectionManagedArchiveCandidate> Candidates { get; set; } =
				new CollectionManagedArchiveCandidate[0];
			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				return Candidates;
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

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			private EventHandler<TaskEndedEventArgs> _taskEnded;
			private System.ComponentModel.PropertyChangedEventHandler _propertyChanged;
			public int CallCount { get; private set; }
			public Uri LastUri { get; private set; }

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				LastUri = sourceUri;
				return InterfaceStub<IBackgroundTask>.Create((method, args) =>
				{
					switch (method.Name)
					{
						case "get_Status":
						case "get_InnerTaskStatus": return Nexus.Client.BackgroundTasks.TaskStatus.Queued;
						case "add_TaskEnded": _taskEnded += (EventHandler<TaskEndedEventArgs>)args[0]; return null;
						case "remove_TaskEnded": _taskEnded -= (EventHandler<TaskEndedEventArgs>)args[0]; return null;
						case "add_PropertyChanged": _propertyChanged += (System.ComponentModel.PropertyChangedEventHandler)args[0]; return null;
						case "remove_PropertyChanged": _propertyChanged -= (System.ComponentModel.PropertyChangedEventHandler)args[0]; return null;
						case "get_OverallMessage": return "queued";
						case "get_ItemMessage": return String.Empty;
						default: return null;
					}
				});
			}
		}

		private sealed class FixedBundleProvider : INexusCollectionsProvider
		{
			private readonly NexusCollectionRevisionMetadata _revision;
			private readonly Uri _contentUri;
			public FixedBundleProvider(NexusCollectionRevisionMetadata revision, Uri contentUri)
			{
				_revision = revision;
				_contentUri = contentUri;
			}
			public int ResolveCallCount { get; private set; }
			public Task<NexusCollectionSummaryLookupResult> GetSummaryAsync(string collectionSlug, CancellationToken cancellationToken = default(CancellationToken))
			{
				throw new NotSupportedException();
			}
			public Task<NexusCollectionRevisionLookupResult> GetRevisionAsync(NexusCollectionRevisionRequest request, CancellationToken cancellationToken = default(CancellationToken))
			{
				throw new NotSupportedException();
			}
			public Task<NexusCollectionBundleResolutionResult> ResolveBundleAsync(NexusCollectionRevisionMetadata revision, CancellationToken cancellationToken = default(CancellationToken))
			{
				cancellationToken.ThrowIfCancellationRequested();
				ResolveCallCount++;
				return Task.FromResult(new NexusCollectionBundleResolutionResult(_revision, 1, new[]
				{
					new NexusCollectionBundleDownloadLocation(_contentUri, "fixture", "fixture")
				}));
			}
		}

		private sealed class FixedContentHandler : HttpMessageHandler
		{
			private readonly byte[] _content;
			public FixedContentHandler(byte[] content) { _content = content; }
			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested();
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new ByteArrayContent(_content)
				});
			}
		}

		private static CollectionContentHash ComputeHash(byte[] bytes)
		{
			using (SHA256 sha256 = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha256.ComputeHash(bytes))
					.Replace("-", String.Empty).ToLowerInvariant());
		}
	}
}
