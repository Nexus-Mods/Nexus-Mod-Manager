using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.NexusMods.Collections;

namespace Nexus.Client.CollectionManagement
{
	public enum CollectionRevisionUpdateWorkflowStatus
	{
		Unknown = 0,
		ReadyForReview = 1,
		AwaitingInput = 2,
		ExplicitReviewRequired = 3,
		PausedAtSafeBoundary = 4,
		RecoveryRequired = 5,
		Committed = 6
	}

	public sealed class CollectionRevisionUpdateWorkflowReview
	{
		internal CollectionRevisionUpdateWorkflowReview(CollectionOperation operation, CollectionRevisionUpdatePlan updatePlan)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			UpdatePlan = updatePlan ?? throw new ArgumentNullException(nameof(updatePlan));
		}

		public CollectionOperation Operation { get; }
		public CollectionRevisionUpdatePlan UpdatePlan { get; }
	}

	public sealed class CollectionRevisionUpdateWorkflowResult
	{
		internal CollectionRevisionUpdateWorkflowResult(CollectionRevisionUpdateWorkflowStatus status,
			CollectionOperation operation, CollectionRevisionUpdatePreparationBatch preparation,
			CollectionRevisionUpdatePublicationResult publication, string message, CollectionRevisionUpdateCandidateExecutionPlanning executionPlanning = null)
		{
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateWorkflowStatus), status) || status == CollectionRevisionUpdateWorkflowStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Preparation = preparation;
			Publication = publication;
			Message = message ?? String.Empty;
			ExecutionPlanning = executionPlanning;
		}

		public CollectionRevisionUpdateWorkflowStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionRevisionUpdatePreparationBatch Preparation { get; }
		public CollectionRevisionUpdatePublicationResult Publication { get; }
		public string Message { get; }
		internal CollectionRevisionUpdateCandidateExecutionPlanning ExecutionPlanning { get; }
		public bool IsCommitted { get { return Status == CollectionRevisionUpdateWorkflowStatus.Committed; } }
	}

	/// <summary>
	/// Product-level C10 composition for comparing, approving, preparing, applying, resuming and publishing one concrete
	/// Collection revision update. Existing C10.1-C10.9 coordinators remain the owners of their native/persistence boundaries.
	/// </summary>
	public sealed class CollectionRevisionUpdateApplicationService
	{
		private const string PreparationRole = "revision-update-preparation-v1";
		private const string PreparationFormat = "nmm-ce.collections.revision-update-preparation/1";
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsStore _store;
		private readonly CollectionsCatalogStore _catalogStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsRevisionSourceStore _revisionSourceStore;
		private readonly CollectionsNexusLocatorStore _nexusLocatorStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionRevisionUpdatePlanner _planner;
		private readonly CollectionNexusSourcePolicyResolver _sourcePolicyResolver;
		private readonly CollectionRevisionUpdateReviewCoordinator _reviewCoordinator;
		private readonly CollectionRevisionUpdatePreparationCoordinator _preparationCoordinator;
		private readonly CollectionRevisionUpdateNativeRecipePreparationService _recipePreparation;
		private readonly CollectionRevisionUpdateObsoleteEffectCoordinator _obsoleteCoordinator;
		private readonly CollectionRevisionUpdateCandidateExecutionCoordinator _candidateCoordinator;
		private readonly CollectionRevisionUpdateOverrideReapplyCoordinator _overrideCoordinator;
		private readonly CollectionRevisionUpdateAggregateVerificationCoordinator _aggregateCoordinator;
		private readonly CollectionRevisionUpdatePublicationCoordinator _publicationCoordinator;
		private readonly CollectionAcquisitionRestartCoordinator _acquisitionRestart;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		private sealed class PreparationSnapshotDto
		{
			public string Format { get; set; }
			public string PlanId { get; set; }
			public int PlanVersion { get; set; }
			public List<PreparationMemberDto> Members { get; set; }
		}

		private sealed class PreparationMemberDto
		{
			public int MemberKind { get; set; }
			public string MemberValue { get; set; }
			public string PreparedNativeFingerprint { get; set; }
			public int? InstallMethod { get; set; }
			public int? InstallRoot { get; set; }
			public string GameRootArchiveBaseDirectory { get; set; }
			public byte[] InstallRootCorrection { get; set; }
		}

		public CollectionRevisionUpdateApplicationService(ServiceManager services, GameStorageService gameStorageService)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			if (_services.ModManager == null) throw new InvalidOperationException("Revision update requires the active native ModManager.");
			_store = new CollectionsStore(GetTargetPaths());
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			_catalogStore = new CollectionsCatalogStore(_store);
			_associationStore = new CollectionsAssociationStore(_store);
			_operationStore = new CollectionsOperationStore(_store);
			_planStore = new CollectionsResolvedPlanStore(_store);
			_revisionSourceStore = new CollectionsRevisionSourceStore(_store);
			_nexusLocatorStore = new CollectionsNexusLocatorStore(_store);
			_artifactStore = new CollectionsRetainedArtifactStore(_store);
			_referenceStore = new CollectionsRetainedArtifactReferenceStore(_store);
			_manifestStore = new CollectionsNativeChildRecoveryManifestStore(_artifactStore, _referenceStore);
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore);
			_planner = new CollectionRevisionUpdatePlanner();
			var nexusRepository = _services.ModRepository as NexusModsApiRepository;
			if (nexusRepository == null) throw new InvalidOperationException("Revision update requires the active NexusModsApiRepository implementation.");
			_sourcePolicyResolver = new CollectionNexusSourcePolicyResolver(_revisionSourceStore, nexusRepository);
			_reviewCoordinator = new CollectionRevisionUpdateReviewCoordinator(_operationStore, _planStore, _associationStore);
			// Share retained-source/artifact stores with recipe preparation so one revision continuation also shares their
			// process-local integrity caches instead of hashing/opening the same immutable inputs through parallel store instances.
			_recipePreparation = new CollectionRevisionUpdateNativeRecipePreparationService(_services, _artifactStore,
				new CollectionNativeRecipePreparer(_store, _revisionSourceStore, _artifactStore, _referenceStore));
			_preparationCoordinator = BuildPreparationCoordinator();
			_obsoleteCoordinator = new CollectionRevisionUpdateObsoleteEffectCoordinator(_services, _gameStorageService,
				_operationStore, _associationStore, _reviewCoordinator, CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services));
			_candidateCoordinator = new CollectionRevisionUpdateCandidateExecutionCoordinator(_services, _gameStorageService,
				_operationStore, _planStore, _associationStore, _artifactStore, _referenceStore, _manifestStore, _recipePreparation);
			_overrideCoordinator = new CollectionRevisionUpdateOverrideReapplyCoordinator(_services, _gameStorageService,
				_operationStore, _planStore, _associationStore, _manifestStore);
			_aggregateCoordinator = new CollectionRevisionUpdateAggregateVerificationCoordinator(_services, _gameStorageService,
				_operationStore, _planStore, _associationStore, _manifestStore, _artifactStore, _referenceStore);
			_publicationCoordinator = new CollectionRevisionUpdatePublicationCoordinator(_services, _gameStorageService,
				_operationStore, _planStore, _associationStore, _manifestStore, _artifactStore, _referenceStore);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services);
			_acquisitionRestart = BuildAcquisitionRestartCoordinator();
		}

		public async Task<CollectionRevisionUpdateWorkflowReview> PrepareReviewAsync(Guid associationId,
			CollectionRevisionIdentity candidateRevision, IEnumerable<CollectionOptionalMemberSelection> candidateOptionalSelection,
			CancellationToken cancellationToken)
		{
			if (associationId == Guid.Empty) throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (candidateRevision == null) throw new ArgumentNullException(nameof(candidateRevision));
			GameStoragePathSet paths = GetTargetPaths();
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				if (_operationStore.GetIncompleteOperations(authority.Target).Any())
					throw new InvalidOperationException("A revision update cannot begin while another Collection operation for this target is incomplete.");
				CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(authority.Target);
				CollectionTargetAssociation association = snapshot.Associations.SingleOrDefault(x => x.AssociationId == associationId);
				if (association == null) throw new InvalidOperationException("The installed Collection association is no longer present on the current target.");
				if (!association.Revision.Collection.Equals(candidateRevision.Collection) || association.Revision.Equals(candidateRevision))
					throw new ArgumentException("The candidate must be a distinct concrete revision of the installed Collection.", nameof(candidateRevision));

				CollectionNativeStateIndex state = _nativeStateReader.Capture(authority.Target);
				NexusCollectionBundleImportResult oldManifest = LoadRetainedManifest(association.Revision);
				NexusCollectionBundleImportResult candidateManifest = LoadRetainedManifest(candidateRevision);
				List<CollectionMemberBinding> bindings = snapshot.Bindings.Where(x => x.Association.AssociationId == associationId).ToList();
				List<UserOverride> overrides = snapshot.Overrides.Where(x => x.Requirement.AssociationId == associationId).ToList();
				List<CollectionDriftObservation> drift = snapshot.DriftObservations.Where(x => x.Requirement.AssociationId == associationId).ToList();

				CollectionEffectiveSelection oldSelection = BuildInstalledSelection(oldManifest.CapabilityReport, bindings, overrides, drift);
				CollectionNexusSourcePolicyResolution oldPolicyResolution = _sourcePolicyResolver.ResolveInstalled(oldSelection, bindings, state);
				oldSelection = RequireSupportedSelection(oldPolicyResolution.Selection);
				CollectionEffectiveSelection candidateSelection = BuildCandidateSelection(oldSelection, candidateManifest.CapabilityReport,
					candidateOptionalSelection);
				CollectionNexusSourcePolicyResolution candidatePolicyResolution = _sourcePolicyResolver.Resolve(candidateSelection);
				candidateSelection = RequireSupportedSelection(candidatePolicyResolution.Selection);
				ResolvedCollectionPlan oldPlan = BuildPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), authority.Target,
					state.Fingerprint, oldSelection, oldPolicyResolution.ArtifactChoices);
				ResolvedCollectionPlan newPlan = BuildPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), authority.Target,
					state.Fingerprint, candidateSelection, candidatePolicyResolution.ArtifactChoices);
				CollectionRevisionUpdatePlan update = _planner.Plan(association, oldPlan, newPlan, state, overrides, drift,
					snapshot.NativeModProvenance, null, null, null, null);
				CollectionOperation operation = _reviewCoordinator.CreateReviewedOperation(update);
				return new CollectionRevisionUpdateWorkflowReview(operation, update);
			}
		}

		/// <summary>Records explicit Collection-only use for every eligible member in a cancelled blocked comparison.</summary>
		/// <remarks>This changes provenance metadata only; a fresh revision review is required before native changes.</remarks>
		public Task ConfirmCollectionOnlyUseAsync(CollectionRevisionUpdatePlan reviewedPlan, CancellationToken cancellationToken)
		{
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			return ReviewStandaloneUseAsync(reviewedPlan, reviewedPlan.Members.Where(x => x.RequiresStandaloneUseConfirmation)
				.Select(x => x.MemberKey), new CollectionMemberKey[0], cancellationToken);
		}

		/// <summary>Records separate Collection-only and independent-use decisions for exact members of a cancelled comparison.</summary>
		/// <remarks>Unreviewed members retain their provenance. Installed mods are unchanged and a fresh update approval remains required.</remarks>
		public async Task ReviewStandaloneUseAsync(CollectionRevisionUpdatePlan reviewedPlan,
			IEnumerable<CollectionMemberKey> collectionOnlyMembers, IEnumerable<CollectionMemberKey> independentMembers, CancellationToken cancellationToken)
		{
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			List<CollectionMemberBinding> collectionOnlyBindings = GetReviewedOwnershipBindings(reviewedPlan, collectionOnlyMembers);
			List<CollectionMemberBinding> independentBindings = GetReviewedOwnershipBindings(reviewedPlan, independentMembers);
			if (collectionOnlyBindings.Count + independentBindings.Count == 0)
				throw new InvalidOperationException("The comparison has no standalone-ownership decisions to record.");
			GameStoragePathSet paths = GetTargetPaths();
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!reviewedPlan.Association.Target.Equals(authority.Target))
				throw new InvalidOperationException("The reviewed Collection belongs to another target.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				if (_operationStore.GetIncompleteOperations(authority.Target).Any())
					throw new InvalidOperationException("Complete pending Collection work before reviewing ownership.");
				CollectionNativeStateIndex state = _nativeStateReader.Capture(authority.Target);
				if (!state.Fingerprint.Equals(reviewedPlan.ObservedStateFingerprint))
					throw new InvalidOperationException("Native state changed after the comparison; review the revision again before recording ownership.");
				cancellationToken.ThrowIfCancellationRequested();
				_associationStore.SaveReviewedStandaloneUse(reviewedPlan.Association, collectionOnlyBindings, independentBindings);
			}
		}

		/// <summary>Resolves only the exact member decisions offered by the reviewed ownership comparison.</summary>
		private static List<CollectionMemberBinding> GetReviewedOwnershipBindings(CollectionRevisionUpdatePlan reviewedPlan,
			IEnumerable<CollectionMemberKey> memberKeys)
		{
			if (memberKeys == null) throw new ArgumentNullException(nameof(memberKeys));
			List<CollectionMemberKey> keys = memberKeys.ToList();
			if (keys.Any(x => x == null) || keys.Distinct().Count() != keys.Count)
				throw new ArgumentException("Ownership decisions cannot contain null or duplicate members.", nameof(memberKeys));
			Dictionary<CollectionMemberKey, CollectionMemberBinding> eligible = reviewedPlan.Members
				.Where(x => x.RequiresStandaloneUseConfirmation).ToDictionary(x => x.MemberKey, x => x.Binding);
			if (keys.Any(x => !eligible.ContainsKey(x)))
				throw new ArgumentException("An ownership decision is outside the exact reviewed member set.", nameof(memberKeys));
			return keys.Select(x => eligible[x]).ToList();
		}

		public CollectionOperation CancelBeforeApply(CollectionRevisionUpdateWorkflowReview review)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			return _reviewCoordinator.CancelBeforeApply(review.Operation.Identity, review.UpdatePlan.NewPlan.Identity);
		}

		/// <summary>Reads the latest revision-update journal snapshot without reconciling or submitting native work.</summary>
		internal CollectionOperation ReadCurrentOperation(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision)
				throw new InvalidOperationException("The revision-update operation is unavailable in the durable journal.");
			return operation;
		}

		/// <summary>Rehydrates an interrupted pre-mutation C10 review so the UI can explicitly approve or cancel it after restart.</summary>
		public CollectionRevisionUpdateWorkflowReview LoadPendingReview(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal ||
				operation.Phase != CollectionOperationPhase.ReadyForReview || operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("The revision-update operation is not an unapplied review that can be resumed explicitly.");

			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			CollectionRevisionUpdatePlan plan = RehydrateReviewedPlan(intent);
			intent.ValidateCurrentPlan(plan);
			return new CollectionRevisionUpdateWorkflowReview(operation, plan);
		}

		/// <summary>Finds the exact durable unfinished update for one installed association even before startup UI reconciliation finishes.</summary>
		internal CollectionRevisionUpdateWorkflowResult InspectInterruptedForAssociation(Guid associationId)
		{
			if (associationId == Guid.Empty) return null;
			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null) return null;
			foreach (CollectionOperation operation in _operationStore.GetIncompleteOperations(association.Target)
				.Where(x => x.Kind == CollectionOperationKind.UpdateRevision && x.Collection.Equals(association.Revision.Collection)))
			{
				try
				{
					CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.LoadReviewedIntent(operation.Identity);
					if (intent.AssociationId != associationId || !intent.OldRevision.Equals(association.Revision) ||
						!intent.CandidateRevision.Equals(operation.Revision) || !intent.Target.Equals(operation.Target)) continue;
					if (operation.RequiresRecovery || operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability)
						return new CollectionRevisionUpdateWorkflowResult(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired,
							operation, null, null, "The interrupted revision change must be reconciled before it can continue.");
					if (operation.Phase == CollectionOperationPhase.ReadyForReview)
						return new CollectionRevisionUpdateWorkflowResult(CollectionRevisionUpdateWorkflowStatus.ReadyForReview,
							operation, null, null, "The interrupted revision change is waiting for review.");
					if (operation.Phase == CollectionOperationPhase.AwaitingInput)
						return new CollectionRevisionUpdateWorkflowResult(CollectionRevisionUpdateWorkflowStatus.AwaitingInput,
							operation, null, null, "The interrupted revision change is waiting for required input.");
					return new CollectionRevisionUpdateWorkflowResult(CollectionRevisionUpdateWorkflowStatus.PausedAtSafeBoundary,
						operation, null, null, "The interrupted revision change is paused at a safe boundary and can be continued.");
				}
				catch (InvalidOperationException)
				{
				}
			}
			return null;
		}

		/// <summary>Returns other concrete Nexus revisions of the installed Collection whose exact manifest is still retained locally.</summary>
		internal IReadOnlyList<CollectionRevision> GetRetainedRevisionCandidates(Guid associationId)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty installed Collection association identifier is required.", nameof(associationId));
			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null)
				return new CollectionRevision[0];

			List<CollectionRevision> candidates = _catalogStore.GetRevisions(association.Revision.Collection)
				.Where(x => x != null && x.IsRemoteBaseline && !x.Identity.Equals(association.Revision) &&
					_revisionSourceStore.GetSource(x.Identity) != null)
				.OrderByDescending(x => x.Identity.NexusRevisionNumber ?? Int64.MinValue)
				.ThenByDescending(x => x.Identity.StableRevisionId, StringComparer.Ordinal)
				.ToList();
			return new ReadOnlyCollection<CollectionRevision>(candidates);
		}

		/// <summary>Loads one exact previously-retained Nexus revision as an incoming preview without provider/network access.</summary>
		internal NexusCollectionPreviewSnapshot LoadRetainedPreview(CollectionRevisionIdentity revisionIdentity)
		{
			if (revisionIdentity == null) throw new ArgumentNullException(nameof(revisionIdentity));
			CollectionDefinition definition = _catalogStore.GetDefinition(revisionIdentity.Collection);
			CollectionRevision revision = _catalogStore.GetRevision(revisionIdentity);
			if (definition == null || revision == null || !revision.IsRemoteBaseline)
				throw new InvalidOperationException("The selected retained Nexus Collection revision metadata is unavailable.");
			NexusCollectionNxmLink retainedLink = BuildRetainedNexusLink(revisionIdentity);
			return new NexusCollectionPreviewSnapshot(retainedLink, null, null, definition, revision,
				LoadRetainedManifest(revisionIdentity), null, null, null);
		}

		/// <summary>Projects installed optional participation onto an incoming preview without preparing or changing the setup.</summary>
		internal CollectionEffectiveSelection BuildCandidatePreviewSelection(Guid associationId, CollectionCapabilityReport candidateCapability)
		{
			if (candidateCapability == null) throw new ArgumentNullException(nameof(candidateCapability));
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths());
			CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(authority.Target);
			CollectionTargetAssociation association = snapshot.Associations.SingleOrDefault(x => x.AssociationId == associationId);
			if (association == null)
				throw new InvalidOperationException("The installed Collection association is no longer present on the current target.");
			NexusCollectionBundleImportResult installedManifest = LoadRetainedManifest(association.Revision);
			CollectionEffectiveSelection installedSelection = BuildInstalledSelection(installedManifest.CapabilityReport,
				snapshot.Bindings.Where(x => x.Association.AssociationId == associationId),
				snapshot.Overrides.Where(x => x.Requirement.AssociationId == associationId),
				snapshot.DriftObservations.Where(x => x.Requirement.AssociationId == associationId));
			return BuildCandidateSelection(installedSelection, candidateCapability, Enumerable.Empty<CollectionOptionalMemberSelection>());
		}

		/// <summary>Recreates a canonical public NXM locator from retained non-authoritative locator metadata.</summary>
		private NexusCollectionNxmLink BuildRetainedNexusLink(CollectionRevisionIdentity revisionIdentity)
		{
			if (revisionIdentity == null || !revisionIdentity.NexusRevisionNumber.HasValue ||
				revisionIdentity.NexusRevisionNumber.Value <= 0 || revisionIdentity.NexusRevisionNumber.Value > Int32.MaxValue) return null;
			CollectionNexusLocatorRecord locator = _nexusLocatorStore.Get(revisionIdentity.Collection);
			if (locator == null) return null;
			string value = String.Format(CultureInfo.InvariantCulture, "nxm://{0}/collections/{1}/revisions/{2}",
				locator.GameDomain, locator.CollectionSlug, revisionIdentity.NexusRevisionNumber.Value);
			NexusCollectionNxmLink link;
			return NexusCollectionNxmLinkParser.Classify(value, out link) == NexusNxmLinkDisposition.Collection ? link : null;
		}

		/// <summary>Loads the exact retained candidate preview for an unfinished update without network access or native work.</summary>
		internal NexusCollectionPreviewSnapshot LoadInterruptedPreview(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(GetTargetPaths());
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal ||
				!operation.Target.Equals(authority.Target))
				throw new InvalidOperationException("The unfinished revision change does not belong to the active setup.");
			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			if (!intent.CandidateRevision.Equals(operation.Revision) || !intent.Target.Equals(operation.Target))
				throw new InvalidOperationException("The unfinished revision change no longer matches its retained candidate review.");
			CollectionTargetAssociation association = _associationStore.GetAssociation(intent.AssociationId);
			if (!CollectionManagementApplicationService.MatchesPendingRevision(association, operation, intent.AssociationId, intent.OldRevision))
				throw new InvalidOperationException("The unfinished revision change no longer belongs to its reviewed installed association.");
			return LoadRetainedPreview(intent.CandidateRevision);
		}

		public async Task<CollectionRevisionUpdateWorkflowResult> ApproveAndApplyAsync(CollectionRevisionUpdateWorkflowReview review,
			CollectionArchiveOverwritePolicy archiveOverwritePolicy, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			CollectionRevisionUpdatePlan current = await RevalidatePreMutationPlanAsync(review.UpdatePlan, cancellationToken).ConfigureAwait(false);
			CollectionOperation approved = _reviewCoordinator.Approve(review.Operation.Identity, review.UpdatePlan.NewPlan.Identity, current);
			CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(current.NewPlan.Target, cancellationToken).ConfigureAwait(false);
			CollectionRevisionUpdatePreparationBatch preparation = _preparationCoordinator.Begin(approved.Identity, current, state,
				archiveOverwritePolicy, confirmOverwriteCallback, cancellationToken);
			if (!preparation.IsReady)
				return Result(CollectionRevisionUpdateWorkflowStatus.AwaitingInput, preparation.Operation, preparation, null,
					"Candidate archives are still being acquired or require manual input.");
			PersistPreparationSnapshot(preparation, cancellationToken);
			return await ContinuePreparedAsync(preparation, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>Verifies one user-selected archive for the exact pending candidate manual/browse request.</summary>
		public CollectionVerifiedArchive VerifyLocalFile(CollectionRevisionUpdatePreparationBatch preparation,
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			if (preparation == null) throw new ArgumentNullException(nameof(preparation));
			return _preparationCoordinator.VerifyLocalFile(preparation, pendingAction, localFilePath, cancellationToken);
		}

		public async Task<CollectionRevisionUpdateWorkflowResult> ProbePreparationAndContinueAsync(
			CollectionRevisionUpdatePreparationBatch previous, CollectionRevisionUpdateWorkflowReview review,
			CancellationToken cancellationToken)
		{
			if (previous == null) throw new ArgumentNullException(nameof(previous));
			if (review == null) throw new ArgumentNullException(nameof(review));
			CollectionRevisionUpdatePlan current = await RevalidatePreMutationPlanAsync(review.UpdatePlan, cancellationToken).ConfigureAwait(false);
			CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(current.NewPlan.Target, cancellationToken).ConfigureAwait(false);
			CollectionRevisionUpdatePreparationBatch preparation = _preparationCoordinator.ProbeCompletedInput(previous, current, state, cancellationToken);
			if (!preparation.IsReady)
				return Result(CollectionRevisionUpdateWorkflowStatus.AwaitingInput, preparation.Operation, preparation, null,
					"Candidate archives are still being acquired or require manual input.");
			PersistPreparationSnapshot(preparation, cancellationToken);
			return await ContinuePreparedAsync(preparation, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Rehydrates one interrupted UpdateRevision operation from its immutable review and retained candidate/old revision sources.
		/// Native work is never blindly replayed. Ambiguous obsolete-removal children remain RecoveryRequired; candidate children use C6.9 evidence reconciliation.
		/// </summary>
		public async Task<CollectionRevisionUpdateWorkflowResult> ResumeAsync(CollectionOperationIdentity operationIdentity,
			CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal)
				throw new InvalidOperationException("The revision-update operation is not available for resume.");
			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			CollectionRevisionUpdatePlan plan = RehydrateReviewedPlan(intent);

			if (operation.Phase == CollectionOperationPhase.ReadyForReview)
				return Result(CollectionRevisionUpdateWorkflowStatus.ReadyForReview, operation, null, null,
					"The revision update is still awaiting explicit approval.");

			if (operation.Phase == CollectionOperationPhase.AwaitingInput)
			{
				CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(plan.NewPlan.Target, cancellationToken).ConfigureAwait(false);
				CollectionRevisionUpdatePreparationBatch restarted = _preparationCoordinator.ReconcileRestart(operationIdentity, plan, state, cancellationToken);
				if (!restarted.IsReady)
					return Result(CollectionRevisionUpdateWorkflowStatus.AwaitingInput, restarted.Operation, restarted, null,
						"Revision-update acquisition requires user or provider input before resume.");
				PersistPreparationSnapshot(restarted, cancellationToken);
				return await ContinuePreparedAsync(restarted, cancellationToken).ConfigureAwait(false);
			}

			if (operation.Phase == CollectionOperationPhase.ReadyToApply)
			{
				CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(plan.NewPlan.Target, cancellationToken).ConfigureAwait(false);
				CollectionRevisionUpdatePreparationBatch restarted = _preparationCoordinator.Begin(operationIdentity, plan, state,
					CollectionArchiveOverwritePolicy.Prompt, null, cancellationToken);
				if (!restarted.IsReady)
					return Result(CollectionRevisionUpdateWorkflowStatus.AwaitingInput, restarted.Operation, restarted, null,
						"Revision-update acquisition requires input before native mutation can resume.");
				PersistPreparationSnapshot(restarted, cancellationToken);
				return await ContinuePreparedAsync(restarted, cancellationToken).ConfigureAwait(false);
			}

			CollectionRevisionUpdatePreparationBatch preparation = await RehydratePostMutationPreparationAsync(operation, plan, intent, cancellationToken).ConfigureAwait(false);
			if (preparation == null)
				return Result(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired,
					_operationStore.GetOperation(operationIdentity) ?? operation, null, null,
					"The pre-mutation C10.3 preparation cannot be proven after restart; native work was not replayed.");

			if (operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability ||
				operation.Phase == CollectionOperationPhase.RemovingObsoleteRevisionEffects ||
				operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary ||
				operation.Phase == CollectionOperationPhase.RecoveryRequired || operation.Phase == CollectionOperationPhase.Recovering)
			{
				CollectionRevisionUpdateWorkflowResult recovered = await ReconcileCrossedBoundaryAsync(preparation, cancellationToken).ConfigureAwait(false);
				if (recovered != null) return recovered;
			}
			return await ContinueFromCurrentPhaseAsync(preparation, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Continues from an already rehydrated/prepared in-memory batch. The durable operation and exact reviewed plan still
		/// control the transition; this only skips reconstructing the same candidate recipes again when the UI rechecks the
		/// unchanged safe boundary in the same application session.
		/// </summary>
		internal async Task<CollectionRevisionUpdateWorkflowResult> ResumePreparedAsync(
			CollectionRevisionUpdatePreparationBatch preparation, CancellationToken cancellationToken)
		{
			if (preparation == null) throw new ArgumentNullException(nameof(preparation));
			CollectionOperation operation = _operationStore.GetOperation(preparation.Operation.Identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal)
				throw new InvalidOperationException("The revision-update operation is not available for prepared continuation.");
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(preparation.CurrentPlan.NewPlan.Identity) ||
				operation.Revision == null || !operation.Revision.Equals(preparation.CurrentPlan.NewPlan.Revision) ||
				!operation.Target.Equals(preparation.CurrentPlan.NewPlan.Target))
				throw new InvalidOperationException("The retained in-memory preparation no longer belongs to the exact active revision-update operation.");

			switch (operation.Phase)
			{
				case CollectionOperationPhase.ObsoleteRevisionEffectsVerified:
				case CollectionOperationPhase.InstallingCandidateRevisionChildren:
				case CollectionOperationPhase.CandidateRevisionChildrenVerified:
				case CollectionOperationPhase.ReapplyingQualifiedRevisionOverrides:
				case CollectionOperationPhase.QualifiedRevisionOverridesVerified:
				case CollectionOperationPhase.VerifyingCandidateRevisionAggregate:
				case CollectionOperationPhase.CandidateRevisionAggregateVerified:
					return await ContinueFromCurrentPhaseAsync(preparation, cancellationToken).ConfigureAwait(false);
				default:
					return await ResumeAsync(operation.Identity, cancellationToken).ConfigureAwait(false);
			}
		}

		private async Task<CollectionRevisionUpdateWorkflowResult> ContinuePreparedAsync(CollectionRevisionUpdatePreparationBatch preparation,
			CancellationToken cancellationToken)
		{
			CollectionRevisionUpdateObsoleteEffectResult obsolete = await _obsoleteCoordinator.ExecuteAsync(preparation,
				GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			return await ContinueAfterObsoleteAsync(preparation, obsolete, cancellationToken).ConfigureAwait(false);
		}

		private async Task<CollectionRevisionUpdateWorkflowResult> ContinueAfterObsoleteAsync(CollectionRevisionUpdatePreparationBatch preparation,
			CollectionRevisionUpdateObsoleteEffectResult obsolete, CancellationToken cancellationToken)
		{
			CollectionRevisionUpdateCandidateExecutionResult candidate = await _candidateCoordinator.ExecuteAsync(preparation, obsolete,
				GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			CollectionRevisionUpdatePreparationBatch effectivePreparation = candidate.Preparation ?? preparation;
			if (candidate.Status == CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired)
				return Result(CollectionRevisionUpdateWorkflowStatus.ExplicitReviewRequired, candidate.Operation, effectivePreparation, null, candidate.Message, candidate.ExecutionPlanning);
			if (candidate.Status == CollectionRevisionUpdateCandidateExecutionStatus.NativeLaneBusy)
				return Result(CollectionRevisionUpdateWorkflowStatus.PausedAtSafeBoundary, candidate.Operation, effectivePreparation, null, candidate.Message, candidate.ExecutionPlanning);
			if (candidate.Status != CollectionRevisionUpdateCandidateExecutionStatus.Completed)
				return Result(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired, candidate.Operation, effectivePreparation, null, candidate.Message, candidate.ExecutionPlanning);
			return await ContinueAfterCandidateAsync(effectivePreparation, cancellationToken).ConfigureAwait(false);
		}

		private async Task<CollectionRevisionUpdateWorkflowResult> ContinueAfterCandidateAsync(CollectionRevisionUpdatePreparationBatch preparation,
			CancellationToken cancellationToken)
		{
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(preparation.ReviewedIntent, preparation.CurrentPlan);
			await _overrideCoordinator.ExecuteAsync(preparation.Operation.Identity, preparation.CurrentPlan, preservation,
				GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			await _aggregateCoordinator.VerifyAsync(preparation.Operation.Identity, preparation.CurrentPlan, preservation,
				GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			CollectionRevisionUpdatePublicationResult publication = await _publicationCoordinator.PublishAsync(preparation.Operation.Identity,
				preparation.CurrentPlan, preservation, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			return Result(CollectionRevisionUpdateWorkflowStatus.Committed, publication.Operation, preparation, publication,
				"The candidate revision was verified and atomically published as the installed Collection revision.");
		}

		private async Task<CollectionRevisionUpdateWorkflowResult> ContinueFromCurrentPhaseAsync(CollectionRevisionUpdatePreparationBatch preparation,
			CancellationToken cancellationToken)
		{
			CollectionOperation operation = _operationStore.GetOperation(preparation.Operation.Identity);
			if (operation == null) throw new InvalidOperationException("The revision-update operation disappeared during resume.");
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner()
				.RequireQualified(preparation.ReviewedIntent, preparation.CurrentPlan);

			if (operation.Phase == CollectionOperationPhase.ObsoleteRevisionEffectsVerified ||
				operation.Phase == CollectionOperationPhase.InstallingCandidateRevisionChildren)
			{
				CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(preparation.CurrentPlan.NewPlan.Target, cancellationToken).ConfigureAwait(false);
				var obsoletePlan = new CollectionRevisionUpdateObsoleteEffectPlan(preparation.ReviewedIntent, preparation.CurrentPlan, state,
					new CollectionRevisionUpdateObsoleteMemberAction[0]);
				var obsolete = new CollectionRevisionUpdateObsoleteEffectResult(operation, obsoletePlan, state,
					operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.Deactivate && x.HasVerifiedCommittedNativeState)
						.Select(x => preparation.CurrentPlan.Members.Single(m => m.MemberKey.Equals(x.Member.MemberKey)).Binding.NativeMod.NativeModKey));
				return await ContinueAfterObsoleteAsync(preparation, obsolete, cancellationToken).ConfigureAwait(false);
			}
			if (operation.Phase == CollectionOperationPhase.CandidateRevisionChildrenVerified ||
				operation.Phase == CollectionOperationPhase.ReapplyingQualifiedRevisionOverrides ||
				operation.Phase == CollectionOperationPhase.QualifiedRevisionOverridesVerified)
				return await ContinueAfterCandidateAsync(preparation, cancellationToken).ConfigureAwait(false);
			if (operation.Phase == CollectionOperationPhase.VerifyingCandidateRevisionAggregate ||
				operation.Phase == CollectionOperationPhase.CandidateRevisionAggregateVerified)
			{
				if (operation.Phase == CollectionOperationPhase.VerifyingCandidateRevisionAggregate)
					await _aggregateCoordinator.VerifyAsync(operation.Identity, preparation.CurrentPlan, preservation,
						GetTargetPaths(), cancellationToken).ConfigureAwait(false);
				CollectionRevisionUpdatePublicationResult publication = await _publicationCoordinator.PublishAsync(operation.Identity,
					preparation.CurrentPlan, preservation, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
				return Result(CollectionRevisionUpdateWorkflowStatus.Committed, publication.Operation, preparation, publication,
					"The interrupted revision update was verified and published.");
			}
			return Result(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired, operation, preparation, null,
				"The interrupted revision update is not at a safely resumable C10 boundary.");
		}

		private async Task<CollectionRevisionUpdateWorkflowResult> ReconcileCrossedBoundaryAsync(CollectionRevisionUpdatePreparationBatch preparation,
			CancellationToken cancellationToken)
		{
			CollectionOperation operation = _operationStore.GetOperation(preparation.Operation.Identity);
			if (operation == null) return null;
			CollectionNativeChildOperation ambiguous = operation.NativeChildren.Where(x => x.HasCrossedNativeBoundary && !x.IsReconciled)
				.OrderBy(x => x.Sequence).FirstOrDefault();
			if (ambiguous == null)
			{
				if (operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !operation.HasCrossedNativeBoundary)
					return Result(CollectionRevisionUpdateWorkflowStatus.PausedAtSafeBoundary, operation, preparation, null,
						"The operation paused before native submission and can be explicitly restarted from review.");
				return null;
			}
			if (ambiguous.Action == CollectionNativeChildAction.ActivateOrReinstall)
			{
				CollectionOperation reconciled = await _candidateCoordinator.ReconcileInterruptedCandidateChildAsync(operation.Identity,
					preparation.CurrentPlan.NewPlan, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
				if (reconciled.Phase == CollectionOperationPhase.RecoveryRequired || reconciled.ResultState == CollectionOperationResultState.RecoveryRequired)
					return Result(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired, reconciled, preparation, null,
						"The interrupted candidate child could not be proven committed from durable evidence.");
				return null;
			}

			CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(preparation.CurrentPlan.NewPlan.Target, cancellationToken).ConfigureAwait(false);
			CollectionRevisionUpdateMemberPlan member = preparation.CurrentPlan.Members.Single(x => x.MemberKey.Equals(ambiguous.Member.MemberKey));
			if (member.Binding != null && !state.Mods.ContainsKey(member.Binding.NativeMod))
			{
				var terminalResult = new Nexus.Client.ModManagement.Operations.ModOperationResult(ambiguous.NativeOperation,
					Nexus.Client.ModManagement.Operations.ModOperationReportedStatus.Succeeded,
					Nexus.Client.ModManagement.Operations.ModOperationDurability.VerifiedCommitted,
					"Restart reconciliation observed the exact reviewed obsolete native mod absent after authority recovery.");
				var reconciledChild = new CollectionNativeChildOperation(ambiguous.Sequence, ambiguous.Member, ambiguous.Action,
					ambiguous.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminalResult);
				operation = ReplaceChild(operation, reconciledChild);
				_operationStore.SaveOperation(operation);
				return Result(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired, operation, preparation, null,
					"The interrupted obsolete removal was proven committed, but C10.4 did not durably seal the complete obsolete-removal boundary; remaining removals are not replayed blindly.");
			}
			return Result(CollectionRevisionUpdateWorkflowStatus.RecoveryRequired, operation, preparation, null,
				"The interrupted obsolete removal is not provably committed; the application will not replay that deactivation blindly.");
		}

		private async Task<CollectionRevisionUpdatePlan> RevalidatePreMutationPlanAsync(CollectionRevisionUpdatePlan reviewed,
			CancellationToken cancellationToken)
		{
			CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(reviewed.NewPlan.Target, cancellationToken).ConfigureAwait(false);
			CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(reviewed.NewPlan.Target);
			CollectionTargetAssociation association = snapshot.Associations.SingleOrDefault(x => x.AssociationId == reviewed.Association.AssociationId);
			if (association == null) throw new InvalidOperationException("The installed Collection association disappeared before update approval.");
			return _planner.Plan(association, reviewed.OldPlan, reviewed.NewPlan, state,
				snapshot.Overrides.Where(x => x.Requirement.AssociationId == association.AssociationId),
				snapshot.DriftObservations.Where(x => x.Requirement.AssociationId == association.AssociationId),
				snapshot.NativeModProvenance, null, null, null, null);
		}

		private async Task<CollectionNativeStateIndex> CaptureAuthoritativeStateAsync(CollectionTargetIdentity target,
			CancellationToken cancellationToken)
		{
			GameStoragePathSet paths = GetTargetPaths();
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(target)) throw new InvalidOperationException("The canonical target changed during revision update.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				return _nativeStateReader.Capture(target);
			}
		}

		private CollectionRevisionUpdatePreparationCoordinator BuildPreparationCoordinator()
		{
			var acquisitionStore = new CollectionsAcquisitionStore(_store);
			var requestCoordinator = new CollectionAcquisitionRequestCoordinator(new ModManagerCollectionAddModQueue(_services.ModManager), acquisitionStore);
			var archiveSource = new ModManagerCollectionManagedArchiveSource(_services.ModManager);
			var repository = _services.ModRepository as NexusModsApiRepository;
			if (repository == null) throw new InvalidOperationException("Revision update requires the active NexusModsApiRepository implementation.");
			var adopter = new CollectionVerifiedArchiveAdopter(archiveSource, new NexusCollectionArchiveIdentityVerifier(repository),
				_artifactStore, _referenceStore, acquisitionStore);
			var premium = new CollectionPremiumAcquisitionCoordinator(requestCoordinator,
				new ModRepositoryCollectionPremiumAcquisitionAccountProvider(_services.ModRepository));
			var manual = new CollectionManualAcquisitionCoordinator(requestCoordinator, adopter,
				new NexusCollectionManualAcquisitionHintProvider(_catalogStore, _revisionSourceStore));
			var direct = new CollectionDirectAcquisitionCoordinator(
				new NexusCollectionDirectAcquisitionSourceProvider(_catalogStore, _revisionSourceStore), adopter, acquisitionStore);
			var restart = new CollectionAcquisitionRestartCoordinator(acquisitionStore,
				new SettingsCollectionPersistedAddModStateSource(_services.ModManager.EnvironmentInfo, _services.ModManager.GameMode.ModeId), adopter, premium);
			var bundled = new NexusCollectionBundledMemberAcquisitionCoordinator(new NexusCollectionBundledArtifactMaterializer(_store, _revisionSourceStore),
				adopter, requestCoordinator, _services.ModManager);
			return new CollectionRevisionUpdatePreparationCoordinator(_operationStore, _reviewCoordinator, adopter, premium, manual,
				restart, _recipePreparation, bundled, direct);
		}

		private CollectionAcquisitionRestartCoordinator BuildAcquisitionRestartCoordinator()
		{
			var acquisitionStore = new CollectionsAcquisitionStore(_store);
			var requestCoordinator = new CollectionAcquisitionRequestCoordinator(new ModManagerCollectionAddModQueue(_services.ModManager), acquisitionStore);
			var repository = _services.ModRepository as NexusModsApiRepository;
			if (repository == null) throw new InvalidOperationException("Revision update requires the active NexusModsApiRepository implementation.");
			var adopter = new CollectionVerifiedArchiveAdopter(new ModManagerCollectionManagedArchiveSource(_services.ModManager),
				new NexusCollectionArchiveIdentityVerifier(repository), _artifactStore, _referenceStore, acquisitionStore);
			var premium = new CollectionPremiumAcquisitionCoordinator(requestCoordinator,
				new ModRepositoryCollectionPremiumAcquisitionAccountProvider(_services.ModRepository));
			return new CollectionAcquisitionRestartCoordinator(acquisitionStore,
				new SettingsCollectionPersistedAddModStateSource(_services.ModManager.EnvironmentInfo, _services.ModManager.GameMode.ModeId), adopter, premium);
		}

		private NexusCollectionBundleImportResult LoadRetainedManifest(CollectionRevisionIdentity identity)
		{
			CollectionRevision revision = _catalogStore.GetRevision(identity);
			if (revision == null) throw new InvalidOperationException("The exact Collection revision metadata is missing from the durable catalog.");
			CollectionRevisionSourceRecord source = _revisionSourceStore.GetSource(identity);
			if (source == null) throw new InvalidOperationException("The exact retained Collection manifest is unavailable for revision " + identity + ".");
			byte[] raw = _revisionSourceStore.LoadManifest(identity, source.ManifestSource);
			NexusCollectionManifestNormalizationResult normalized = new NexusCollectionManifestNormalizer().Normalize(raw, revision);
			return new NexusCollectionBundleImportResult(ToBundleInputKind(source.InputKind), source.BundleContentHash,
				source.BundleByteLength, source.ManifestEntryName, normalized);
		}

		private static CollectionEffectiveSelection BuildInstalledSelection(CollectionCapabilityReport capability,
			IEnumerable<CollectionMemberBinding> bindings, IEnumerable<UserOverride> overrides, IEnumerable<CollectionDriftObservation> drift)
		{
			var bound = new HashSet<CollectionMemberKey>((bindings ?? Enumerable.Empty<CollectionMemberBinding>()).Select(x => x.MemberKey));
			var overrideList = (overrides ?? Enumerable.Empty<UserOverride>()).ToList();
			var driftList = (drift ?? Enumerable.Empty<CollectionDriftObservation>()).ToList();
			var decisions = new List<CollectionOptionalMemberSelection>();
			foreach (NormalizedCollectionMember member in capability.Manifest.Members.Where(x => x.Requirement == CollectionMemberRequirement.Optional && x.IdentityResolution.IsResolved))
			{
				CollectionMemberKey key = member.IdentityResolution.Key;
				bool selected = bound.Contains(key);
				UserOverride local = overrideList.SingleOrDefault(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) && x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation);
				CollectionDriftObservation observed = driftList.SingleOrDefault(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(key) && x.Requirement.Aspect == CollectionRequirementAspect.MemberParticipation);
				CollectionRequirementState expected = local == null ? (observed == null ? null : observed.ExpectedState) : local.UserChosenState;
				if (expected != null) selected = expected.Kind == CollectionRequirementStateKind.Present;
				decisions.Add(new CollectionOptionalMemberSelection(key, selected ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected));
			}
			return new CollectionEffectiveSelectionBuilder().Build(capability, decisions);
		}

		private static CollectionEffectiveSelection BuildCandidateSelection(CollectionEffectiveSelection installedSelection,
			CollectionCapabilityReport capability, IEnumerable<CollectionOptionalMemberSelection> explicitDecisions)
		{
			return new CollectionRevisionUpdateCandidateSelectionBuilder().Build(installedSelection, capability,
				explicitDecisions ?? Enumerable.Empty<CollectionOptionalMemberSelection>());
		}

		private static CollectionEffectiveSelection RequireSupportedSelection(CollectionEffectiveSelection selection)
		{
			if (selection.CapabilityReport.Status != CollectionCompatibilityStatus.Supported)
				throw new InvalidOperationException("The exact selected Collection revision is not fully supported by the current characterized recipe slice.");
			return selection;
		}

		private static ResolvedCollectionPlan BuildPlan(CollectionPlanIdentity identity, CollectionTargetIdentity target,
			CollectionCurrentStateFingerprint fingerprint, CollectionEffectiveSelection effective,
			IReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> artifactChoices = null)
		{
			var selected = new List<ResolvedCollectionMemberPlan>();
			var consumed = new HashSet<CollectionMemberKey>();
			foreach (NormalizedCollectionMember member in effective.Manifest.Members.Where(x => x.IsSelected))
			{
				CollectionResolvedArtifactChoice choice;
				if (artifactChoices != null && member.IdentityResolution.IsResolved && artifactChoices.TryGetValue(member.IdentityResolution.Key, out choice))
				{
					if (choice == null || !member.Artifact.Equals(choice.RequestedArtifact))
						throw new InvalidOperationException("A revision-update source-policy choice no longer matches its normalized requested artifact.");
					consumed.Add(member.IdentityResolution.Key);
				}
				else choice = CollectionResolvedArtifactChoice.Exact(member.Artifact);
				selected.Add(new ResolvedCollectionMemberPlan(member, choice));
			}
			if (artifactChoices != null && consumed.Count != artifactChoices.Count)
				throw new InvalidOperationException("A revision-update source-policy choice does not belong to the selected Collection closure.");
			return new ResolvedCollectionPlan(identity, target, CollectionExecutionPolicy.InstallIntoCurrentSetup(), fingerprint,
				effective.CapabilityReport, selected);
		}

		private CollectionRevisionUpdatePlan RehydrateReviewedPlan(CollectionRevisionUpdateReviewedIntent intent)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociation(intent.AssociationId);
			if (association == null || !association.Revision.Equals(intent.OldRevision) || !association.Target.Equals(intent.Target))
				throw new InvalidOperationException("The installed old revision association required by the interrupted C10 update is unavailable.");
			NexusCollectionBundleImportResult oldManifest = LoadRetainedManifest(intent.OldRevision);
			NexusCollectionBundleImportResult candidateManifest = LoadRetainedManifest(intent.CandidateRevision);
			var oldKeys = new HashSet<CollectionMemberKey>(intent.Members.Where(x => x.ChangeKind != CollectionRevisionUpdateChangeKind.Added).Select(x => x.MemberKey));
			var newKeys = new HashSet<CollectionMemberKey>(intent.Members.Where(x => x.ChangeKind != CollectionRevisionUpdateChangeKind.Removed).Select(x => x.MemberKey));
			var oldChoices = intent.Members.Where(x => x.OldArtifactChoice != null).ToDictionary(x => x.MemberKey, x => x.OldArtifactChoice);
			var newChoices = intent.Members.Where(x => x.NewArtifactChoice != null).ToDictionary(x => x.MemberKey, x => x.NewArtifactChoice);
			ResolvedCollectionPlan oldPlan = BuildPlanFromSelectedKeys(intent.OldPlanIdentity, intent.Target, intent.ObservedStateFingerprint, oldManifest.CapabilityReport, oldKeys, oldChoices);
			ResolvedCollectionPlan newPlan = BuildPlanFromSelectedKeys(intent.CandidatePlanIdentity, intent.Target, intent.ObservedStateFingerprint, candidateManifest.CapabilityReport, newKeys, newChoices);
			CollectionsAssociationTargetSnapshot snapshot = _associationStore.GetTargetSnapshot(intent.Target);
			Dictionary<CollectionMemberKey, CollectionMemberBinding> bindings = snapshot.Bindings.Where(x => x.Association.AssociationId == intent.AssociationId).ToDictionary(x => x.MemberKey);
			Dictionary<Guid, UserOverride> overrides = snapshot.Overrides.Where(x => x.Requirement.AssociationId == intent.AssociationId).ToDictionary(x => x.OverrideId);
			Dictionary<Guid, CollectionDriftObservation> drift = snapshot.DriftObservations.Where(x => x.Requirement.AssociationId == intent.AssociationId).ToDictionary(x => x.ObservationId);
			Dictionary<CollectionMemberKey, ResolvedCollectionMemberPlan> oldMembers = oldPlan.SelectedMembers.ToDictionary(x => x.MemberKey);
			Dictionary<CollectionMemberKey, ResolvedCollectionMemberPlan> newMembers = newPlan.SelectedMembers.ToDictionary(x => x.MemberKey);
			var members = new List<CollectionRevisionUpdateMemberPlan>();
			foreach (CollectionRevisionUpdateReviewEntry entry in intent.Members)
			{
				ResolvedCollectionMemberPlan oldMember; oldMembers.TryGetValue(entry.MemberKey, out oldMember);
				ResolvedCollectionMemberPlan newMember; newMembers.TryGetValue(entry.MemberKey, out newMember);
				CollectionMemberBinding binding; bindings.TryGetValue(entry.MemberKey, out binding);
				members.Add(new CollectionRevisionUpdateMemberPlan(entry.MemberKey, oldMember, newMember, binding, entry.ChangeKind,
					entry.CurrentStateKind, entry.Disposition, entry.PreparationKind, null, null,
					entry.PreservedOverrideIds.Select(x => Require(overrides, x, "override")),
					entry.UnacceptedDriftIds.Select(x => Require(drift, x, "drift observation")), entry.StandaloneProtected, entry.Detail));
			}
			var effects = intent.Effects.Select(x => new CollectionRevisionUpdateEffectPlan(x.MemberKey, x.Kind, x.ResourceKey, x.ChangeKind));
			var memberOverrideIds = new HashSet<Guid>(intent.Members.SelectMany(x => x.PreservedOverrideIds));
			var memberDriftIds = new HashSet<Guid>(intent.Members.SelectMany(x => x.UnacceptedDriftIds));
			var plan = new CollectionRevisionUpdatePlan(association, oldPlan, newPlan, intent.ObservedStateFingerprint, members, effects,
				intent.PreservedOverrideIds.Where(x => !memberOverrideIds.Contains(x)).Select(x => Require(overrides, x, "override")),
				intent.UnacceptedDriftIds.Where(x => !memberDriftIds.Contains(x)).Select(x => Require(drift, x, "drift observation")));
			ValidateReviewedMemberCorrelations(intent, plan);
			return plan;
		}

		private static void ValidateReviewedMemberCorrelations(CollectionRevisionUpdateReviewedIntent intent,
			CollectionRevisionUpdatePlan plan)
		{
			if (!intent.IncludesMemberCorrelations) return;
			IReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey> actual = plan.MemberCorrelations.NewByOld;
			if (actual.Count != intent.MemberCorrelations.Count)
				throw new InvalidOperationException("The retained revision can no longer reproduce the exact cross-key member correlation approved by the revision update.");
			foreach (CollectionRevisionUpdateMemberCorrelationReview reviewed in intent.MemberCorrelations)
			{
				CollectionMemberKey candidateMemberKey;
				if (!actual.TryGetValue(reviewed.OldMemberKey, out candidateMemberKey) || !candidateMemberKey.Equals(reviewed.CandidateMemberKey))
					throw new InvalidOperationException("The retained revision can no longer reproduce the exact cross-key member correlation approved by the revision update.");
			}
		}

		private static T Require<T>(IDictionary<Guid, T> values, Guid id, string label) where T : class
		{
			T value;
			if (!values.TryGetValue(id, out value) || value == null) throw new InvalidOperationException("The reviewed " + label + " changed or disappeared after C10 approval.");
			return value;
		}

		private static ResolvedCollectionPlan BuildPlanFromSelectedKeys(CollectionPlanIdentity identity, CollectionTargetIdentity target,
			CollectionCurrentStateFingerprint fingerprint, CollectionCapabilityReport capability, ISet<CollectionMemberKey> selectedKeys,
			IReadOnlyDictionary<CollectionMemberKey, CollectionResolvedArtifactChoice> artifactChoices)
		{
			var decisions = capability.Manifest.Members.Where(x => x.Requirement == CollectionMemberRequirement.Optional && x.IdentityResolution.IsResolved)
				.Select(x => new CollectionOptionalMemberSelection(x.IdentityResolution.Key,
					selectedKeys.Contains(x.IdentityResolution.Key) ? CollectionMemberSelection.Selected : CollectionMemberSelection.Unselected)).ToList();
			CollectionEffectiveSelection effective = new CollectionEffectiveSelectionBuilder().Build(capability, decisions);
			HashSet<CollectionMemberKey> actual = new HashSet<CollectionMemberKey>(effective.Manifest.Members.Where(x => x.IsSelected && x.IdentityResolution.IsResolved).Select(x => x.IdentityResolution.Key));
			if (!actual.SetEquals(selectedKeys)) throw new InvalidOperationException("The retained revision can no longer reproduce the exact member selection frozen by the C10 review.");
			if (artifactChoices != null && artifactChoices.Count != 0)
				effective = CollectionNexusSourcePolicyResolver.ReapplyReviewedChoices(effective, artifactChoices);
			effective = RequireSupportedSelection(effective);
			return BuildPlan(identity, target, fingerprint, effective, artifactChoices);
		}

		private void PersistPreparationSnapshot(CollectionRevisionUpdatePreparationBatch preparation, CancellationToken cancellationToken)
		{
			var dto = new PreparationSnapshotDto
			{
				Format = PreparationFormat,
				PlanId = preparation.CurrentPlan.NewPlan.Identity.PlanId.ToString("D"),
				PlanVersion = preparation.CurrentPlan.NewPlan.Identity.Version,
				Members = preparation.Members.Where(x => x.PreparedRecipe != null).OrderBy(x => x.UpdateMember.MemberKey.Kind).ThenBy(x => x.UpdateMember.MemberKey.Value, StringComparer.Ordinal)
					.Select(x => new PreparationMemberDto { MemberKind = (int)x.UpdateMember.MemberKey.Kind, MemberValue = x.UpdateMember.MemberKey.Value,
						PreparedNativeFingerprint = x.PreparedRecipe.PreparedNativeIdentity.Fingerprint,
						InstallMethod = (int)x.PreparedRecipe.InstallContext.Method, InstallRoot = (int)x.PreparedRecipe.InstallContext.InstallRoot,
						GameRootArchiveBaseDirectory = x.PreparedRecipe.GameRootArchiveBaseDirectory,
						InstallRootCorrection = CollectionInstallRootCorrection.Serialize(x.PreparedRecipe.EffectPreview.InstallRootCorrection) }).ToList()
			};
			byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(dto, Formatting.None));
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false)) artifact = _artifactStore.Publish(stream, cancellationToken);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
				preparation.Operation.Identity.OperationId.ToString("D"), PreparationRole);
		}

		private async Task<CollectionRevisionUpdatePreparationBatch> RehydratePostMutationPreparationAsync(CollectionOperation operation,
			CollectionRevisionUpdatePlan plan, CollectionRevisionUpdateReviewedIntent intent, CancellationToken cancellationToken)
		{
			PreparationSnapshotDto snapshot = LoadPreparationSnapshot(operation.Identity, plan.NewPlan.Identity, cancellationToken);
			if (snapshot == null) return null;
			CollectionNativeStateIndex state = await CaptureAuthoritativeStateAsync(plan.NewPlan.Target, cancellationToken).ConfigureAwait(false);
			var frozenMembers = snapshot.Members.ToDictionary(x => CreateMemberKey((CollectionMemberKeyKind)x.MemberKind, x.MemberValue));
			var committed = new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled && x.HasVerifiedCommittedNativeState)
				.Select(x => x.Member.MemberKey));
			var states = new List<CollectionRevisionUpdatePreparationMemberState>();
			foreach (CollectionRevisionUpdateMemberPlan member in plan.Members.Where(RequiresCandidatePreparation))
			{
				PreparationMemberDto frozen;
				if (!frozenMembers.TryGetValue(member.MemberKey, out frozen)) return null;
				CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(CollectionMemberAcquisitionCoordinator.CreateStableRequestId(plan.NewPlan.Identity, member.MemberKey), plan.NewPlan, member.MemberKey);
				CollectionAcquisitionRestartResult restart = _acquisitionRestart.Reconcile(request, cancellationToken);
				if (restart.Disposition != CollectionAcquisitionRestartDisposition.VerifiedInputReady || restart.VerifiedArchive == null) return null;
				PreparedCollectionNativeRecipe prepared = null;
				if (!committed.Contains(member.MemberKey))
				{
					ModInstallContext frozenContext = frozen.InstallMethod.HasValue && frozen.InstallRoot.HasValue
						? new ModInstallContext((ModInstallMethod)frozen.InstallMethod.Value, (ModInstallRoot)frozen.InstallRoot.Value)
						: null;
					if (frozen.InstallMethod.HasValue != frozen.InstallRoot.HasValue ||
						(frozen.GameRootArchiveBaseDirectory != null && (frozenContext == null || frozenContext.InstallRoot != ModInstallRoot.GameRoot)))
						throw new InvalidDataException("The retained Collection destination is incomplete or inconsistent.");
					prepared = _recipePreparation.PrepareAtExecutionBoundary(plan, member, restart.VerifiedArchive, state, cancellationToken,
						frozenContext, frozen.GameRootArchiveBaseDirectory, CollectionInstallRootCorrection.Deserialize(frozen.InstallRootCorrection));
					if (!StringComparer.Ordinal.Equals(prepared.PreparedNativeIdentity.Fingerprint, frozen.PreparedNativeFingerprint)) return null;
				}
				states.Add(new CollectionRevisionUpdatePreparationMemberState(member, CollectionMemberAcquisitionDisposition.ReadyVerifiedArchive,
					request, restart.VerifiedArchive, null, null, restart, restart.PremiumAvailability, prepared));
			}
			return new CollectionRevisionUpdatePreparationBatch(operation, intent, plan, states, CollectionArchiveOverwritePolicy.Prompt,
				state.Fingerprint);
		}

		private PreparationSnapshotDto LoadPreparationSnapshot(CollectionOperationIdentity identity, CollectionPlanIdentity planIdentity,
			CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(CollectionsRetainedArtifactOwnerKind.Operation,
				identity.OperationId.ToString("D"), PreparationRole);
			if (reference == null || !_artifactStore.VerifyArtifact(reference.ArtifactId, cancellationToken)) return null;
			using (Stream stream = _artifactStore.OpenRead(reference.ArtifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				PreparationSnapshotDto dto = JsonConvert.DeserializeObject<PreparationSnapshotDto>(reader.ReadToEnd());
				if (dto == null || !StringComparer.Ordinal.Equals(dto.Format, PreparationFormat) || dto.Members == null ||
					!StringComparer.OrdinalIgnoreCase.Equals(dto.PlanId, planIdentity.PlanId.ToString("D")) || dto.PlanVersion != planIdentity.Version)
					throw new InvalidDataException("The durable C10.3 preparation snapshot is missing, corrupt or belongs to another reviewed plan.");
				return dto;
			}
		}

		private static CollectionMemberKey CreateMemberKey(CollectionMemberKeyKind kind, string value)
		{
			switch (kind)
			{
				case CollectionMemberKeyKind.ProviderStable: return CollectionMemberKey.FromProvider(value);
				case CollectionMemberKeyKind.ValidatedMatch: return CollectionMemberKey.FromValidatedMatch(value);
				case CollectionMemberKeyKind.Local:
					Guid local;
					if (!Guid.TryParse(value, out local) || local == Guid.Empty) throw new InvalidDataException("The durable C10.3 preparation snapshot contains an invalid Local member identity.");
					return CollectionMemberKey.FromLocal(local);
				default: throw new InvalidDataException("The durable C10.3 preparation snapshot contains an unsupported member identity kind.");
			}
		}

		private static bool RequiresCandidatePreparation(CollectionRevisionUpdateMemberPlan member)
		{
			if (member == null || member.NewMember == null || member.ChangeKind == CollectionRevisionUpdateChangeKind.Removed) return false;
			if (member.ChangeKind != CollectionRevisionUpdateChangeKind.Unchanged) return true;
			return member.PreparationKind == CollectionRevisionUpdatePreparationKind.PreparedForCandidate ||
				member.PreparationKind == CollectionRevisionUpdatePreparationKind.CandidateChanged ||
				member.PreparationKind == CollectionRevisionUpdatePreparationKind.ReprepareRequired;
		}

		private static CollectionOperation ReplaceChild(CollectionOperation operation, CollectionNativeChildOperation replacement)
		{
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Select(x => x.Sequence == replacement.Sequence ? replacement : x).ToList();
			if (operation.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The revision-update checkpoint sequence cannot be incremented further.");
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState, children);
		}

		private static CollectionRevisionUpdateWorkflowResult Result(CollectionRevisionUpdateWorkflowStatus status,
			CollectionOperation operation, CollectionRevisionUpdatePreparationBatch preparation,
			CollectionRevisionUpdatePublicationResult publication, string message, CollectionRevisionUpdateCandidateExecutionPlanning executionPlanning = null)
		{
			return new CollectionRevisionUpdateWorkflowResult(status, operation, preparation, publication, message, executionPlanning);
		}

		private GameStoragePathSet GetTargetPaths()
		{
			return _gameStorageService.FromGameMode(_services.ModManager.GameMode);
		}

		private static NexusCollectionBundleInputKind ToBundleInputKind(CollectionRevisionSourceInputKind inputKind)
		{
			switch (inputKind)
			{
				case CollectionRevisionSourceInputKind.RawManifest: return NexusCollectionBundleInputKind.RawManifest;
				case CollectionRevisionSourceInputKind.Archive:
				case CollectionRevisionSourceInputKind.LocalWorkingCopy: return NexusCollectionBundleInputKind.Archive;
				default: throw new InvalidOperationException("The retained Collection revision has an unsupported source kind.");
			}
		}
	}
}
