using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
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
		/// <summary>Reviews explicit, reversible post-install setup overlays for the active Collection target.</summary>
		public CollectionSetupPackageService SetupPackages { get; private set; }

		private readonly ServiceManager _services;
		private readonly IProfileManager _profileManager;
		private readonly GameStorageService _gameStorageService;
		private readonly INexusCollectionsProvider _provider;
		private readonly CollectionsStore _store;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsNexusLocatorStore _nexusLocatorStore;
		private readonly CollectionEffectiveSelectionBuilder _selectionBuilder;
		private readonly NexusCollectionBundleImporter _bundleImporter;
		private readonly CollectionConflictResolutionCoordinator _conflictResolutionCoordinator;
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
			_nexusLocatorStore = new CollectionsNexusLocatorStore(_store);
			_selectionBuilder = new CollectionEffectiveSelectionBuilder();
			_bundleImporter = new NexusCollectionBundleImporter();
			_conflictResolutionCoordinator = new CollectionConflictResolutionCoordinator(new CollectionsConflictResolutionStore(_store));
			SetupPackages = new CollectionSetupPackageService(_services, _gameStorageService, _store);
			_workflow = BuildWorkflow(paths);
		}

		/// <summary>Signals a library change after user-driven or authorized archive import.</summary>
		public event NotifyCollectionChangedEventHandler ManagedArchivesChanged
		{
			add { _services.ModManager.ManagedMods.CollectionChanged += value; }
			remove { _services.ModManager.ManagedMods.CollectionChanged -= value; }
		}

		/// <summary>Gets whether native work must finish before automatic continuation.</summary>
		public bool HasPendingNativeWork
		{
			get { return Nexus.Transactions.Transaction.Current != null ||
				(_services.ModActivationMonitor != null && _services.ModActivationMonitor.Tasks.Any(x => !x.IsCompleted)); }
		}

		/// <summary>Refreshes only a stale review, preserving already acquired content and source choices.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> RefreshInstallationAsync(CollectionMemberAcquisitionBatch batch,
			CancellationToken cancellationToken, string reviewMessage = null)
		{
			return _workflow.RefreshInstallationAsync(batch, GetTargetPaths(), cancellationToken, reviewMessage);
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
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			RetainNexusLocator(preview);

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

			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			RetainNexusLocator(preview);
			_catalogStore.SaveDefinitionAndRevision(preview.Definition, preview.Revision);
			using (var acquirer = new NexusCollectionBundleAcquirer(_provider, _store))
			{
				NexusCollectionBundleAcquisitionResult result = await acquirer.AcquireAsync(
					preview.RevisionLookup.Revision, preview.Revision, cancellationToken).ConfigureAwait(false);
				return preview.WithBundleImport(result.BundleImport);
			}
		}

		/// <summary>Retains the public Nexus slug/domain alongside the stable numeric Collection identity.</summary>
		private void RetainNexusLocator(NexusCollectionPreviewSnapshot preview)
		{
			if (preview == null || preview.Link == null || preview.Revision == null ||
				preview.Revision.Identity.Collection.Origin != CollectionOrigin.NexusMods) return;
			_nexusLocatorStore.Save(preview.Revision.Identity.Collection, preview.Link.GameDomain, preview.Link.CollectionSlug);
		}

		/// <summary>Reports preparation activity while refreshing a stale automatic review.</summary>
		internal Task<CollectionAdditiveWorkflowPreparationResult> RefreshInstallationAsync(CollectionMemberAcquisitionBatch batch,
			CancellationToken cancellationToken, string reviewMessage, IProgress<string> progress)
		{
			using (CollectionPreparationProgress.Begin(progress))
				return RefreshInstallationAsync(batch, cancellationToken, reviewMessage);
		}

		/// <summary>Builds one immutable effective optional-member selection over the exact retained preview manifest.</summary>
		public CollectionEffectiveSelection BuildEffectiveSelection(NexusCollectionPreviewSnapshot preview,
			IEnumerable<CollectionOptionalMemberSelection> optionalSelections)
		{
			return BuildEffectiveSelection(preview, optionalSelections, null);
		}

		/// <summary>Includes optional prerequisites only when both automatic installation and overwrite are authorized.</summary>
		internal CollectionEffectiveSelection BuildEffectiveSelection(NexusCollectionPreviewSnapshot preview,
			IEnumerable<CollectionOptionalMemberSelection> optionalSelections, CollectionExecutionPolicy policy)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (!preview.HasManifestPreview)
				throw new InvalidOperationException("The Collection manifest must be downloaded/imported before effective selection can be built.");
			return _selectionBuilder.Build(preview.CapabilityReport,
				optionalSelections ?? throw new ArgumentNullException(nameof(optionalSelections)),
				policy != null && policy.AutomaticallyInstall && policy.PreferIncomingCollection);
		}

		/// <summary>Starts headless additive preparation for the active game target.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection selection,
			ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			return PrepareAsync(selection, CollectionArchiveOverwritePolicy.Prompt, confirmOverwriteCallback, cancellationToken);
		}

		/// <summary>Starts additive preparation with one immutable archive-overwrite policy for the acquisition batch.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection selection,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken)
		{
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			return _workflow.PrepareAsync(selection, GetTargetPaths(), archiveOverwritePolicy, confirmOverwriteCallback, cancellationToken);
		}

		/// <summary>Begins a fresh additive preparation with one explicitly reviewed alternative archive.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection selection,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CollectionAlternateArchiveCandidate alternate, CancellationToken cancellationToken)
		{
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			return _workflow.PrepareAsync(selection, GetTargetPaths(), archiveOverwritePolicy, confirmOverwriteCallback, alternate, cancellationToken);
		}

		/// <summary>Starts an installation with the preferences captured when Install Collection was chosen.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareInstallationAsync(CollectionEffectiveSelection selection,
			CollectionExecutionPolicy policy, CollectionArchiveOverwritePolicy archiveOverwritePolicy,
			ConfirmOverwriteCallback confirmOverwriteCallback, CollectionAlternateArchiveCandidate alternate, CancellationToken cancellationToken)
		{
			if (policy == null) throw new ArgumentNullException(nameof(policy));
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			return _workflow.PrepareAsync(selection, GetTargetPaths(), archiveOverwritePolicy, confirmOverwriteCallback, alternate, cancellationToken, policy);
		}

		/// <summary>Captures UI progress before the ordinary preparation task copies its execution context.</summary>
		internal Task<CollectionAdditiveWorkflowPreparationResult> PrepareInstallationAsync(CollectionEffectiveSelection selection,
			CollectionExecutionPolicy policy, CollectionArchiveOverwritePolicy archiveOverwritePolicy,
			ConfirmOverwriteCallback confirmOverwriteCallback, CollectionAlternateArchiveCandidate alternate, CancellationToken cancellationToken,
			IProgress<string> progress)
		{
			using (CollectionPreparationProgress.Begin(progress))
				return PrepareInstallationAsync(selection, policy, archiveOverwritePolicy, confirmOverwriteCallback, alternate, cancellationToken);
		}

		public CollectionAlternateArchiveCandidate InspectAlternateLocalFile(CollectionMemberAcquisitionBatch batch,
			CollectionManualAcquisitionPendingAction pending, string localFilePath, CancellationToken cancellationToken)
		{
			return _workflow.InspectAlternateLocalFile(batch, pending, localFilePath, cancellationToken);
		}

		public void ReleaseAlternateLocalFile(CollectionAlternateArchiveCandidate candidate)
		{
			_workflow.ReleaseAlternateLocalFile(candidate);
		}

		/// <summary>Resumes an input-paused preparation against freshly resolved active target paths.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> ResumePreparationAsync(CollectionMemberAcquisitionBatch batch,
			CancellationToken cancellationToken)
		{
			return _workflow.ResumePreparationAsync(batch, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Retries one selected acquisition through the existing native download/import and overwrite paths.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> RetryMemberAcquisitionAsync(CollectionMemberAcquisitionBatch batch,
			CollectionMemberKey memberKey, ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken,
			IProgress<string> progress)
		{
			using (CollectionPreparationProgress.Begin(progress))
				return _workflow.RetryMemberAcquisitionAsync(batch, memberKey, GetTargetPaths(), confirmOverwriteCallback, cancellationToken);
		}

		/// <summary>Reports archive and file preparation while resuming completed downloads.</summary>
		internal Task<CollectionAdditiveWorkflowPreparationResult> ResumePreparationAsync(CollectionMemberAcquisitionBatch batch,
			CancellationToken cancellationToken, IProgress<string> progress)
		{
			using (CollectionPreparationProgress.Begin(progress))
				return ResumePreparationAsync(batch, cancellationToken);
		}

		/// <summary>
		/// Refreshes a saved conflict-only additive review without replaying archive extraction or
		/// Nexus resolution. Revalidates native ownership first, and never approves installation.
		/// </summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> ResumeFileWinnerReviewAsync(
			CollectionAdditiveWorkflowPreparationResult previous, CancellationToken cancellationToken)
		{
			return _workflow.ResumeFileWinnerReviewAsync(previous, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Verifies one user-selected archive for the exact pending manual/browse member request.</summary>
		public CollectionVerifiedArchive VerifyLocalFile(CollectionMemberAcquisitionBatch batch,
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			return _workflow.VerifyLocalFile(batch, pendingAction, localFilePath, cancellationToken);
		}

		/// <summary>Records one explicit C9 decision allowing the reviewed incoming member to win an exact existing-owner file conflict.</summary>
		public CollectionConflictResolutionDecision AuthorizeIncomingFileWinner(CollectionAdditiveWorkflowPreparationResult preparation,
			ModDeploymentTarget target, string note)
		{
			ResolvedCollectionPlan plan = RequireConflictDecisionPlan(preparation, target);
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			return _conflictResolutionCoordinator.AuthorizeIncomingFileWinner(plan, preparation.ImpactPlan, target, note);
		}

		/// <summary>Returns whether C9 can preserve the exact current owner as a selectable managed winner.</summary>
		public bool CanKeepExistingManagedFileWinner(CollectionAdditiveWorkflowPreparationResult preparation, ModDeploymentTarget target)
		{
			RequireConflictDecisionPlan(preparation, target);
			CollectionFileImpact impact = preparation.ImpactPlan.FileImpacts.SingleOrDefault(x => x.Target.Equals(target));
			return impact != null && impact.PlannedWinner != null && IsActiveNativeModOwner(impact.CurrentOwnerKey);
		}

		/// <summary>Records one explicit C9 decision preserving the current unrelated managed owner for an exact file conflict.</summary>
		public CollectionConflictResolutionDecision KeepExistingManagedFileWinner(CollectionAdditiveWorkflowPreparationResult preparation,
			ModDeploymentTarget target, string note)
		{
			ResolvedCollectionPlan plan = RequireConflictDecisionPlan(preparation, target);
			CollectionFileImpact impact = preparation.ImpactPlan.FileImpacts.SingleOrDefault(x => x.Target.Equals(target));
			if (impact == null || impact.PlannedWinner == null || !IsActiveNativeModOwner(impact.CurrentOwnerKey))
				throw new NotSupportedException("C9 can preserve only an active NMM mod owner for a reviewed file conflict. Original/unresolved fallback ownership remains ActionRequired.");
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			return _conflictResolutionCoordinator.KeepExistingManagedFileWinner(plan, preparation.ImpactPlan, target, note);
		}


		/// <summary>Stores all explicitly reviewed file-winner decisions in one operation, not a global preference.</summary>
		public IReadOnlyList<CollectionConflictResolutionDecision> SaveFileWinnerDecisions(
			CollectionAdditiveWorkflowPreparationResult preparation,
			IEnumerable<CollectionFileWinnerDecisionRequest> requests, string note)
		{
			if (requests == null) throw new ArgumentNullException(nameof(requests));
			var batch = requests.ToList();
			if (batch.Count == 0) return new CollectionConflictResolutionDecision[0];
			if (batch.Any(x => x == null))
				throw new ArgumentException("A null file-winner decision is not supported.", nameof(requests));
			ResolvedCollectionPlan plan = RequireConflictDecisionPlan(preparation, batch[0].Target);
			var impacts = preparation.ImpactPlan.FileImpacts.ToDictionary(x => x.Target);
			var log = _services.ModManager.InstallationLog;
			var activeOwners = new HashSet<string>(log.ActiveMods.Select(log.GetModKey)
				.Where(x => !String.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
			activeOwners.Remove(log.OriginalValuesKey);
			foreach (CollectionFileWinnerDecisionRequest request in batch)
			{
				if (request.Kind != CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner) continue;
				CollectionFileImpact impact;
				if (!impacts.TryGetValue(request.Target, out impact) || impact.PlannedWinner == null ||
					!activeOwners.Contains(impact.CurrentOwnerKey ?? String.Empty))
					throw new NotSupportedException("No to all cannot preserve an unmanaged/original file owner: " + request.Target);
			}
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			return _conflictResolutionCoordinator.SaveFileWinnerDecisions(plan, preparation.ImpactPlan, batch, note);
		}

		private bool IsActiveNativeModOwner(string ownerKey)
		{
			if (String.IsNullOrWhiteSpace(ownerKey)) return false;
			var installLog = _services.ModManager.InstallationLog;
			if (StringComparer.OrdinalIgnoreCase.Equals(ownerKey, installLog.OriginalValuesKey)) return false;
			foreach (var mod in installLog.ActiveMods)
				if (StringComparer.OrdinalIgnoreCase.Equals(ownerKey, installLog.GetModKey(mod))) return true;
			return false;
		}

		private static ResolvedCollectionPlan RequireConflictDecisionPlan(CollectionAdditiveWorkflowPreparationResult preparation,
			ModDeploymentTarget target)
		{
			if (preparation == null) throw new ArgumentNullException(nameof(preparation));
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (preparation.Status != CollectionAdditiveWorkflowPreparationStatus.ActionRequired || preparation.AcquisitionBatch == null ||
				preparation.ImpactPlan == null || preparation.AcquisitionBatch.PlanBuild == null || preparation.AcquisitionBatch.PlanBuild.Plan == null)
				throw new InvalidOperationException("Only an exact ActionRequired additive preparation can record a file-winner decision.");
			return preparation.AcquisitionBatch.PlanBuild.Plan;
		}

		/// <summary>Cancels a superseded additive preparation that has not crossed the native mutation boundary.</summary>
		public CollectionOperation CancelBeforeApply(CollectionOperationIdentity operationIdentity)
		{
			return _workflow.CancelBeforeApply(operationIdentity);
		}

		/// <summary>Loads and revalidates an exact persisted additive review.</summary>
		public CollectionAdditiveWorkflowReview GetReview(CollectionOperationIdentity operationIdentity)
		{
			using (CollectionsRetainedArtifactStore.BeginSavedArchiveHashReuse(Properties.Settings.Default.CollectionSkipExistingArchiveMd5 || Properties.Settings.Default.CollectionSkipContentChecks))
				return _workflow.GetReview(operationIdentity);
		}

		/// <summary>Applies the exact approved plan to the active game target.</summary>
		public Task<CollectionAdditiveWorkflowApplyResult> ApproveAndApplyAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, CancellationToken cancellationToken)
		{
			using (CollectionsRetainedArtifactStore.BeginSavedArchiveHashReuse(Properties.Settings.Default.CollectionSkipExistingArchiveMd5 || Properties.Settings.Default.CollectionSkipContentChecks))
				return _workflow.ApproveAndApplyAsync(operationIdentity, expectedPlan, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Reports the active member and installation stage without changing the public apply contract.</summary>
		internal async Task<CollectionAdditiveWorkflowApplyResult> ApproveAndApplyAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, CancellationToken cancellationToken, IProgress<string> progress)
		{
			using (CollectionPreparationProgress.Begin(progress))
				return await ApproveAndApplyAsync(operationIdentity, expectedPlan, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Reads the current durable journal for reports without reconstructing or verifying the reviewed plan.</summary>
		internal CollectionOperation ReadCurrentOperation(CollectionOperationIdentity operationIdentity)
		{
			return new CollectionsOperationStore(_store).GetOperation(operationIdentity);
		}

		/// <summary>Ends a reconciled installation for the selected revision without removing content or installing remaining members.</summary>
		public Task<IReadOnlyList<CollectionOperation>> StopReconciledInstallationForRemovalAsync(
			CollectionRevisionIdentity revision, CancellationToken cancellationToken)
		{
			return _workflow.StopReconciledInstallationForRemovalAsync(revision, GetTargetPaths(), cancellationToken);
		}

		/// <summary>Reconciles incomplete additive operations for the active target before resume.</summary>
		public Task<IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult>> ReconcileIncompleteTargetAsync(CancellationToken cancellationToken)
		{
			return ReconcileIncompleteTargetAsync(cancellationToken, false);
		}

		/// <summary>Allows an explicit recovery action to finish an unchanged, unregistered Witcher activation.</summary>
		internal Task<IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult>> ReconcileIncompleteTargetAsync(CancellationToken cancellationToken,
			bool repairInterruptedActivation)
		{
			if (CollectionsStoreBootstrap.OpenExistingIfPresent(_store) == null)
				return Task.FromResult((IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult>)new CollectionAdditiveWorkflowRecoveryResult[0]);
			return _workflow.ReconcileIncompleteTargetAsync(GetTargetPaths(), cancellationToken, repairInterruptedActivation);
		}

		/// <summary>Loads a stopped partial installation's saved selections on a worker for an explicit continuation review.</summary>
		internal Task<CollectionAdditiveWorkflowRecoveryResult> GetStoppedPartialInstallationAsync(CollectionTargetAssociation association,
			CancellationToken cancellationToken)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			CollectionTargetIdentity target = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths()).Target;
			if (!association.Target.Equals(target)) throw new InvalidOperationException("The stopped installation belongs to a different game/storage target.");
			return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				return _workflow.GetStoppedPartialInstallation(association);
			}, cancellationToken);
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
			var nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			var targetResolver = new CollectionTargetIdentityResolver(_gameStorageService);
			var planBuilder = new CollectionResolvedPlanBuilder(_catalogStore, _revisionSourceStore, operationCoordinator);

			var requestCoordinator = new CollectionAcquisitionRequestCoordinator(
				new ModManagerCollectionAddModQueue(_services.ModManager), acquisitionStore);
			var archiveSource = new ModManagerCollectionManagedArchiveSource(_services.ModManager);
			var nexusRepository = _services.ModRepository as NexusModsApiRepository;
			if (nexusRepository == null)
				throw new InvalidOperationException("Nexus Collections require the active NexusModsApiRepository implementation.");
			var sourcePolicyResolver = new CollectionNexusSourcePolicyResolver(_revisionSourceStore, nexusRepository);
			var planPreparation = new CollectionAdditivePlanPreparationService(targetResolver, nativeStateReader, planBuilder, sourcePolicyResolver);
			var archiveVerifier = new NexusCollectionArchiveIdentityVerifier(nexusRepository);
			var archiveAdopter = new CollectionVerifiedArchiveAdopter(archiveSource, archiveVerifier,
				artifactStore, referenceStore, acquisitionStore);
			var premiumCoordinator = new CollectionPremiumAcquisitionCoordinator(requestCoordinator,
				new ModRepositoryCollectionPremiumAcquisitionAccountProvider(_services.ModRepository));
			var manualCoordinator = new CollectionManualAcquisitionCoordinator(requestCoordinator, archiveAdopter,
				new NexusCollectionManualAcquisitionHintProvider(_catalogStore, _revisionSourceStore));
			var directCoordinator = new CollectionDirectAcquisitionCoordinator(
				new NexusCollectionDirectAcquisitionSourceProvider(_catalogStore, _revisionSourceStore), archiveAdopter, acquisitionStore);
			var acquisitionRestart = new CollectionAcquisitionRestartCoordinator(acquisitionStore,
				new SettingsCollectionPersistedAddModStateSource(_services.ModManager.EnvironmentInfo, _services.ModManager.GameMode.ModeId),
				archiveAdopter, premiumCoordinator);
			var bundledMaterializer = new NexusCollectionBundledArtifactMaterializer(_store, _revisionSourceStore);
			var bundledAcquisition = new NexusCollectionBundledMemberAcquisitionCoordinator(bundledMaterializer,
				archiveAdopter, requestCoordinator, _services.ModManager);
			var memberAcquisition = new CollectionMemberAcquisitionCoordinator(CollectionInstallDestinationResolver.CreateMatchEngine(_services.ModManager, artifactStore), archiveAdopter,
				premiumCoordinator, manualCoordinator, acquisitionRestart, operationCoordinator, bundledAcquisition, directCoordinator);
			var planRevalidation = new CollectionAdditivePlanRevalidationService(targetResolver, nativeStateReader,
				operationCoordinator, planBuilder, memberAcquisition);

			var nativeRecipePreparer = new CollectionNativeRecipePreparer(_store, _revisionSourceStore, artifactStore, referenceStore)
			{
				SelectInstallerOptions = _services.ModManager.SelectCollectionInstallerOptions
			};
			var dependencyPlanner = new CollectionDependencyPhasePlanner();
			var impactPlanner = new CollectionConflictImpactPlanner();
			var workflowRehydrator = new CollectionReviewedWorkflowRehydrator(operationStore, planStore,
				_revisionSourceStore, artifactStore, nativeStateReader, recoveryManifestStore);
			var runtimeReconstructor = new CollectionReviewedWorkflowRuntimeReconstructor(_store, _revisionSourceStore, artifactStore, referenceStore);
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
				childExecution, childVerification, childRestart, associationCoordinator, winnerCoordinator, _conflictResolutionCoordinator);
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
