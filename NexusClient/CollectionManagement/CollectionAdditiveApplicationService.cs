using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.NexusMods.Collections;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Application-level composition root for the C6.15 additive Collections workflow.
	/// </summary>
	/// <remarks>
	/// WinForms consumes this service instead of constructing C4/C5/C6 persistence and native coordinators itself.
	/// It owns only Collections feature composition; native NMM services remain authoritative for installed state.
	/// </remarks>
	public sealed class CollectionAdditiveApplicationService
	{
		private readonly ServiceManager _services;
		private readonly IProfileManager _profileManager;
		private readonly GameStorageService _gameStorageService;
		private readonly INexusCollectionsProvider _provider;
		private readonly CollectionsStore _store;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionEffectiveSelectionBuilder _selectionBuilder;
		private readonly NexusCollectionBundleImporter _bundleImporter;
		private readonly CollectionAdditiveWorkflowCoordinator _workflow;

		/// <summary>
		/// Creates the production additive Collections application service for the active game mode.
		/// </summary>
		public CollectionAdditiveApplicationService(ServiceManager services, IProfileManager profileManager,
			GameStorageService gameStorageService, INexusCollectionsProvider provider)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_provider = provider ?? throw new ArgumentNullException(nameof(provider));
			if (_services.ModManager == null)
				throw new InvalidOperationException("The additive Collections workflow requires the active native ModManager.");
			if (_services.ModRepository == null)
				throw new InvalidOperationException("The additive Collections workflow requires the active Nexus mod repository.");

			GameStoragePathSet paths = GetTargetPaths();
			_store = new CollectionsStore(paths);
			_catalogStore = new CollectionsCatalogStore(_store);
			_revisionSourceStore = new CollectionsRevisionSourceStore(_store);
			_selectionBuilder = new CollectionEffectiveSelectionBuilder();
			_bundleImporter = new NexusCollectionBundleImporter();
			_workflow = BuildWorkflow(paths);
		}

		/// <summary>Gets the current active game's configured Game Storage path set.</summary>
		public GameStoragePathSet GetTargetPaths()
		{
			return _gameStorageService.FromGameMode(_services.ModManager.GameMode);
		}

		/// <summary>
		/// Imports and durably retains a local bundle or collection.json for one already resolved concrete preview revision.
		/// </summary>
		public NexusCollectionPreviewSnapshot ImportAndRetainLocalBundle(NexusCollectionPreviewSnapshot preview,
			string sourcePath, CancellationToken cancellationToken)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (!preview.HasConcreteRevision)
				throw new InvalidOperationException("A concrete Collection revision must be resolved before importing its bundle.");
			if (String.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A bundle path is required.", nameof(sourcePath));
			cancellationToken.ThrowIfCancellationRequested();

			NexusCollectionBundleImportResult bundleImport = _bundleImporter.ImportFile(sourcePath, preview.Revision);
			_catalogStore.SaveDefinitionAndRevision(preview.Definition, preview.Revision);
			CollectionRevisionSourceInputKind inputKind = ToInputKind(bundleImport.InputKind);
			_revisionSourceStore.RetainManifest(bundleImport.Manifest, inputKind, bundleImport.BundleContentHash,
				bundleImport.BundleByteLength, bundleImport.ManifestEntryName, bundleImport.Normalization.GetRawManifestBytes());
			using (FileStream bundle = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
				_revisionSourceStore.RetainBundle(preview.Revision.Identity, bundle, cancellationToken);
			return preview.WithBundleImport(bundleImport);
		}

		/// <summary>
		/// Downloads, imports and durably retains the provider-authorized bundle for one exact concrete Nexus revision.
		/// </summary>
		public async Task<NexusCollectionPreviewSnapshot> DownloadAndRetainBundleAsync(NexusCollectionPreviewSnapshot preview,
			CancellationToken cancellationToken)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (!preview.HasConcreteRevision || preview.RevisionLookup == null || preview.RevisionLookup.Revision == null)
				throw new InvalidOperationException("A concrete provider revision must be resolved before downloading its Collection bundle.");

			_catalogStore.SaveDefinitionAndRevision(preview.Definition, preview.Revision);
			using (var acquirer = new NexusCollectionBundleAcquirer(_provider, _store))
			{
				NexusCollectionBundleAcquisitionResult result = await acquirer.AcquireAsync(
					preview.RevisionLookup.Revision, preview.Revision, cancellationToken).ConfigureAwait(false);
				return preview.WithBundleImport(result.BundleImport);
			}
		}

		/// <summary>Builds one immutable effective optional-member selection over the exact retained preview manifest.</summary>
		public CollectionEffectiveSelection BuildEffectiveSelection(NexusCollectionPreviewSnapshot preview,
			IEnumerable<CollectionOptionalMemberSelection> optionalSelections)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (!preview.HasManifestPreview)
				throw new InvalidOperationException("The Collection manifest must be downloaded/imported before effective selection can be built.");
			return _selectionBuilder.Build(preview.CapabilityReport,
				optionalSelections ?? throw new ArgumentNullException(nameof(optionalSelections)));
		}

		/// <summary>Starts headless additive preparation for the active game target.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection selection,
			ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			return _workflow.PrepareAsync(selection, GetTargetPaths(), confirmOverwriteCallback, cancellationToken);
		}

		/// <summary>Resumes an input-paused preparation against freshly resolved active target paths.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> ResumePreparationAsync(CollectionMemberAcquisitionBatch batch,
			CancellationToken cancellationToken)
		{
			return _workflow.ResumePreparationAsync(batch, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Cancels a superseded additive preparation that has not crossed the native mutation boundary.</summary>
		public CollectionOperation CancelBeforeApply(CollectionOperationIdentity operationIdentity)
		{
			return _workflow.CancelBeforeApply(operationIdentity);
		}

		/// <summary>Loads and revalidates an exact persisted additive review.</summary>
		public CollectionAdditiveWorkflowReview GetReview(CollectionOperationIdentity operationIdentity)
		{
			return _workflow.GetReview(operationIdentity);
		}

		/// <summary>Applies the exact approved plan to the active game target.</summary>
		public Task<CollectionAdditiveWorkflowApplyResult> ApproveAndApplyAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, CancellationToken cancellationToken)
		{
			return _workflow.ApproveAndApplyAsync(operationIdentity, expectedPlan, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Reconciles incomplete additive operations for the active target before resume.</summary>
		public Task<IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult>> ReconcileIncompleteTargetAsync(CancellationToken cancellationToken)
		{
			return _workflow.ReconcileIncompleteTargetAsync(GetTargetPaths(), cancellationToken);
		}

		private CollectionAdditiveWorkflowCoordinator BuildWorkflow(GameStoragePathSet paths)
		{
			var operationStore = new CollectionsOperationStore(_store);
			var planStore = new CollectionsResolvedPlanStore(_store);
			var associationStore = new CollectionsAssociationStore(_store);
			var artifactStore = new CollectionsRetainedArtifactStore(_store);
			var referenceStore = new CollectionsRetainedArtifactReferenceStore(_store);
			var acquisitionStore = new CollectionsAcquisitionStore(_store);
			var recoveryManifestStore = new CollectionsNativeChildRecoveryManifestStore(artifactStore, referenceStore);
			var operationCoordinator = new CollectionOperationCoordinator(operationStore, planStore);
			var nativeStateReader = new CollectionNativeStateReader(_services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			var targetResolver = new CollectionTargetIdentityResolver(_gameStorageService);
			var planBuilder = new CollectionResolvedPlanBuilder(_catalogStore, _revisionSourceStore, operationCoordinator);
			var planPreparation = new CollectionAdditivePlanPreparationService(targetResolver, nativeStateReader, planBuilder);

			var requestCoordinator = new CollectionAcquisitionRequestCoordinator(
				new ModManagerCollectionAddModQueue(_services.ModManager), acquisitionStore);
			var archiveSource = new ModManagerCollectionManagedArchiveSource(_services.ModManager);
			var nexusRepository = _services.ModRepository as NexusModsApiRepository;
			if (nexusRepository == null)
				throw new InvalidOperationException("Nexus Collections require the active NexusModsApiRepository implementation.");
			var archiveVerifier = new NexusCollectionArchiveIdentityVerifier(nexusRepository);
			var archiveAdopter = new CollectionVerifiedArchiveAdopter(archiveSource, archiveVerifier,
				artifactStore, referenceStore, acquisitionStore);
			var premiumCoordinator = new CollectionPremiumAcquisitionCoordinator(requestCoordinator,
				new ModRepositoryCollectionPremiumAcquisitionAccountProvider(_services.ModRepository));
			var manualCoordinator = new CollectionManualAcquisitionCoordinator(requestCoordinator, archiveAdopter);
			var acquisitionRestart = new CollectionAcquisitionRestartCoordinator(acquisitionStore,
				new SettingsCollectionPersistedAddModStateSource(_services.ModManager.EnvironmentInfo, _services.ModManager.GameMode.ModeId),
				archiveAdopter, premiumCoordinator);
			var memberAcquisition = new CollectionMemberAcquisitionCoordinator(new CollectionMemberMatchEngine(), archiveAdopter,
				premiumCoordinator, manualCoordinator, acquisitionRestart, operationCoordinator);
			var planRevalidation = new CollectionAdditivePlanRevalidationService(targetResolver, nativeStateReader,
				operationCoordinator, planBuilder, memberAcquisition);

			var nativeRecipePreparer = new CollectionNativeRecipePreparer(_store);
			var dependencyPlanner = new CollectionDependencyPhasePlanner();
			var impactPlanner = new CollectionConflictImpactPlanner();
			var workflowRehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore,
				_revisionSourceStore, artifactStore, nativeStateReader, recoveryManifestStore);
			var runtimeReconstructor = new CollectionReviewedWorkflowRuntimeReconstructor(_store);
			var childPreparation = new CollectionNativeChildPreparationCoordinator(operationStore, planStore,
				artifactStore, referenceStore, recoveryManifestStore);
			var childExecution = new CollectionNativeChildExecutionCoordinator(_services, _gameStorageService,
				operationStore, planStore, associationStore, recoveryManifestStore);
			var childVerification = new CollectionNativeChildVerificationCoordinator(_services, _gameStorageService,
				operationStore, planStore, associationStore, recoveryManifestStore);
			var childRestart = new CollectionNativeChildRestartReconciliationCoordinator(_services, _gameStorageService,
				operationStore, planStore, associationStore, recoveryManifestStore);
			var associationCoordinator = new CollectionAssociationReconciliationCoordinator(operationStore, planStore,
				associationStore, recoveryManifestStore);
			var winnerCoordinator = new CollectionReviewedFileWinnerReconciliationCoordinator(_services, _profileManager,
				_gameStorageService, operationStore, planStore, associationStore, artifactStore, referenceStore);

			return new CollectionAdditiveWorkflowCoordinator(_services, _gameStorageService, operationStore, planStore,
				planPreparation, planRevalidation, memberAcquisition, nativeRecipePreparer, dependencyPlanner, impactPlanner,
				operationCoordinator, workflowRehydrator, runtimeReconstructor, nativeStateReader, childPreparation,
				childExecution, childVerification, childRestart, associationCoordinator, winnerCoordinator);
		}

		private static CollectionRevisionSourceInputKind ToInputKind(NexusCollectionBundleInputKind inputKind)
		{
			switch (inputKind)
			{
				case NexusCollectionBundleInputKind.RawManifest:
					return CollectionRevisionSourceInputKind.RawManifest;
				case NexusCollectionBundleInputKind.Archive:
					return CollectionRevisionSourceInputKind.Archive;
				default:
					throw new InvalidOperationException("The imported Collection bundle has no supported durable source kind.");
			}
		}
	}
}
