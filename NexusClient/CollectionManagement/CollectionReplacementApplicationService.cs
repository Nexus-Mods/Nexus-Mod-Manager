using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	public enum CollectionReplacementWorkflowApplyStatus
	{
		Committed = 1,
		ExplicitReviewRequired = 2,
		RecoveryRequired = 3,
		PausedAtSafeBoundary = 4
	}

	public sealed class CollectionReplacementWorkflowReview
	{
		private readonly ReadOnlyCollection<PreparedCollectionNativeRecipe> _preparedRecipes;
		private readonly ReadOnlyCollection<CollectionVerifiedArchive> _verifiedArchives;

		internal CollectionReplacementWorkflowReview(CollectionOperation operation, ResolvedCollectionPlan plan,
			CollectionReplacementDiffPlan diff, CollectionReplacementEnvironmentProjection baseline,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
			IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes, IEnumerable<CollectionVerifiedArchive> verifiedArchives)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Plan = plan ?? throw new ArgumentNullException(nameof(plan));
			Diff = diff ?? throw new ArgumentNullException(nameof(diff));
			Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
			DependencyPlan = dependencyPlan ?? throw new ArgumentNullException(nameof(dependencyPlan));
			ImpactPlan = impactPlan ?? throw new ArgumentNullException(nameof(impactPlan));
			_preparedRecipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>((preparedRecipes ?? throw new ArgumentNullException(nameof(preparedRecipes))).ToList());
			_verifiedArchives = new ReadOnlyCollection<CollectionVerifiedArchive>((verifiedArchives ?? throw new ArgumentNullException(nameof(verifiedArchives))).ToList());
		}

		public CollectionOperation Operation { get; }
		public ResolvedCollectionPlan Plan { get; }
		public CollectionReplacementDiffPlan Diff { get; }
		public CollectionReplacementEnvironmentProjection Baseline { get; }
		public CollectionDependencyPhasePlan DependencyPlan { get; }
		public CollectionConflictImpactPlan ImpactPlan { get; }
		public ReadOnlyCollection<PreparedCollectionNativeRecipe> PreparedRecipes { get { return _preparedRecipes; } }
		public ReadOnlyCollection<CollectionVerifiedArchive> VerifiedArchives { get { return _verifiedArchives; } }
	}

	public sealed class CollectionReplacementWorkflowApplyResult
	{
		internal CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus status,
			CollectionOperation operation, CollectionReplacementFinalizationResult finalization,
			string pendingObservationArtifactId, string message)
		{
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Finalization = finalization;
			PendingObservationArtifactId = pendingObservationArtifactId;
			Message = message ?? String.Empty;
		}

		public CollectionReplacementWorkflowApplyStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionReplacementFinalizationResult Finalization { get; }
		public string PendingObservationArtifactId { get; }
		public string Message { get; }
		public bool IsCommitted { get { return Status == CollectionReplacementWorkflowApplyStatus.Committed; } }
	}

	/// <summary>
	/// Product-level C8 replacement composition used by the Collections UI. It deliberately consumes one already-ready
	/// additive Download/Prepare runtime so member archives are acquired through the existing C4 path, then rebuilds all
	/// planning/recipe evidence against the replacement policy and replacement condition environment before mutation.
	/// </summary>
	public sealed class CollectionReplacementApplicationService
	{
		private const string RecoveryPackageRole = "replacement-recovery-package-v1";
		private readonly ServiceManager _services;
		private readonly IProfileManager _profileManager;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsStore _store;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionReplacementOperationCoordinator _replacementCoordinator;
		private readonly CollectionReplacementOutgoingRemovalCoordinator _outgoingRemoval;
		private readonly CollectionReplacementPhaseBarrierCoordinator _phaseBarrier;
		private readonly CollectionReplacementIncomingInstallationCoordinator _incomingInstallation;
		private readonly CollectionReplacementAggregateVerificationCoordinator _aggregateVerification;
		private readonly CollectionReplacementRecoveryCoordinator _recovery;
		private readonly CollectionReplacementDiffPlanner _diffPlanner;
		private readonly CollectionReplacementEnvironmentProjector _environmentProjector;
		private readonly CollectionMemberMatchEngine _matchEngine;
		private readonly CollectionDependencyPhasePlanner _dependencyPlanner;
		private readonly CollectionConflictImpactPlanner _impactPlanner;
		private readonly CollectionNativeRecipePreparer _recipePreparer;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionLocalCaptureApplicationService _localCapture;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionReplacementApplicationService(ServiceManager services, IProfileManager profileManager,
			GameStorageService gameStorageService)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_profileManager = profileManager ?? throw new ArgumentNullException(nameof(profileManager));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			if (_services.ModManager == null) throw new InvalidOperationException("Replacement requires the active native ModManager.");
			GameStoragePathSet paths = GetTargetPaths();
			_store = new CollectionsStore(paths);
			CollectionsStoreBootstrap.OpenOrCreateForFeatureUse(_store);
			_operationStore = new CollectionsOperationStore(_store);
			_associationStore = new CollectionsAssociationStore(_store);
			_planStore = new CollectionsResolvedPlanStore(_store);
			_artifactStore = new CollectionsRetainedArtifactStore(_store);
			_referenceStore = new CollectionsRetainedArtifactReferenceStore(_store);
			var manifestStore = new CollectionsNativeChildRecoveryManifestStore(_artifactStore, _referenceStore);
			_replacementCoordinator = new CollectionReplacementOperationCoordinator(_operationStore, _planStore, _artifactStore, _referenceStore);
			_outgoingRemoval = new CollectionReplacementOutgoingRemovalCoordinator(_services, _gameStorageService, _operationStore,
				_associationStore, _replacementCoordinator, CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services));
			_phaseBarrier = new CollectionReplacementPhaseBarrierCoordinator(_services, _gameStorageService, _operationStore,
				_planStore, _associationStore, _artifactStore, _referenceStore);
			_incomingInstallation = new CollectionReplacementIncomingInstallationCoordinator(_services, _profileManager,
				_gameStorageService, _operationStore, _planStore, _associationStore, _artifactStore, _referenceStore, manifestStore);
			_aggregateVerification = new CollectionReplacementAggregateVerificationCoordinator(_services, _gameStorageService,
				_operationStore, _planStore, _associationStore, _artifactStore, _referenceStore, manifestStore);
			_recovery = new CollectionReplacementRecoveryCoordinator(_services, _gameStorageService, _operationStore,
				_planStore, _associationStore, _artifactStore, _referenceStore, manifestStore);
			_diffPlanner = new CollectionReplacementDiffPlanner();
			_environmentProjector = new CollectionReplacementEnvironmentProjector();
			_matchEngine = CollectionInstallDestinationResolver.CreateMatchEngine(_services.ModManager);
			_dependencyPlanner = new CollectionDependencyPhasePlanner();
			_impactPlanner = new CollectionConflictImpactPlanner();
			_recipePreparer = new CollectionNativeRecipePreparer(new CollectionsCatalogStore(_store),
				new CollectionsRevisionSourceStore(_store), _artifactStore, _referenceStore, new Nexus.Client.OnlineServices.NexusMods.Collections.NexusCollectionManifestNormalizer(),
				new BasicInstallPlanBuilder(), new ModInstallationSimpleFileRecipeAdapter(), new CollectionMemberEffectPreviewBuilder());
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore);
			_localCapture = new CollectionLocalCaptureApplicationService(_services, _gameStorageService);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services);
		}

		public GameStoragePathSet GetTargetPaths()
		{
			return _gameStorageService.FromGameMode(_services.ModManager.GameMode);
		}

		public async Task<CollectionReplacementWorkflowReview> PrepareReviewAsync(CollectionReviewedWorkflowRuntime additiveRuntime,
			CollectionReplacementBackupChoice backupChoice, CancellationToken cancellationToken)
		{
			if (additiveRuntime == null) throw new ArgumentNullException(nameof(additiveRuntime));
			if (backupChoice != CollectionReplacementBackupChoice.CreateLocalCollection &&
				backupChoice != CollectionReplacementBackupChoice.ContinueWithoutLocalCollection)
				throw new ArgumentOutOfRangeException(nameof(backupChoice));
			if (additiveRuntime.Plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new ArgumentException("Replacement product composition requires the exact ready additive Download/Prepare runtime.", nameof(additiveRuntime));

			GameStoragePathSet paths = GetTargetPaths();
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				if (_operationStore.GetIncompleteOperations(authority.Target).Any())
					throw new InvalidOperationException("Replacement cannot begin while another Collection operation for this target is incomplete.");

				CollectionReplacementCurrentSetupSnapshot current = _nativeStateReader.CaptureReplacementCurrentSetup(authority.Target);
				var plan = new ResolvedCollectionPlan(CollectionPlanIdentity.From(Guid.NewGuid(), 1), authority.Target,
					CollectionExecutionPolicy.ReplaceCurrentManagedSetup(backupChoice), current.NativeState.Fingerprint,
					additiveRuntime.Plan.CapabilityReport, additiveRuntime.Plan.SelectedMembers);
				CollectionReplacementDiffPlan diff = _diffPlanner.Plan(plan, current);
				CollectionReplacementEnvironmentProjection baseline = _environmentProjector.Project(diff);
				if (!diff.CurrentStateMatchesPlan || diff.HasBlockers || !baseline.IsReadyForSupportedConditions)
					throw new InvalidOperationException("The replacement difference is blocked, stale or cannot prove the supported condition environment.");

				List<CollectionVerifiedArchive> archives = additiveRuntime.VerifiedArchives.ToList();
				CollectionMemberMatchSet matches = _matchEngine.MatchForReplacementExecution(plan, current.NativeState, archives, cancellationToken);
				if (matches.HasBlockedMembers || matches.HasAcquisitionRequired)
					throw new InvalidOperationException("Download / Prepare does not currently provide every exact archive required for replacement.");
				CollectionDependencyPhasePlan dependency = _dependencyPlanner.Plan(plan, matches);
				if (!dependency.IsReady) throw new InvalidOperationException("The replacement dependency plan is not executable.");

				List<PreparedCollectionNativeRecipe> recipes = PrepareReplacementRecipes(plan, diff, baseline, matches,
					dependency, archives, additiveRuntime, cancellationToken);
				CollectionConflictImpactPlan impact = _impactPlanner.PlanForReplacementExecution(plan, matches, dependency,
					current.NativeState, recipes.Select(x => x.EffectPreview));
				if (!impact.IsReady) throw new InvalidOperationException("The replacement impact plan requires additional decisions before it can be reviewed.");

				CollectionReplacementProfileProtectionSnapshot profile = CaptureProfileProtection();
				CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntent.Create(diff, baseline,
					BuildNativeApprovals(diff), BuildAssociationApprovals(diff),
					recipes.Select(CollectionReplacementPreparedRecipeApproval.FromPreparedRecipe), BuildWinnerApprovals(impact), profile);
				CollectionOperation operation = _replacementCoordinator.CreateReviewedOperation(diff, intent);
				return new CollectionReplacementWorkflowReview(operation, plan, diff, baseline, dependency, impact, recipes, archives);
			}
		}

		public CollectionOperation CancelBeforeApply(CollectionReplacementWorkflowReview review)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			return _replacementCoordinator.CancelBeforeApply(review.Operation.Identity, review.Plan.Identity);
		}

		public async Task<CollectionReplacementWorkflowApplyResult> ApproveAndApplyAsync(CollectionReplacementWorkflowReview review,
			CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			CollectionReplacementCurrentSetupSnapshot current = _nativeStateReader.CaptureReplacementCurrentSetup(review.Plan.Target);
			CollectionOperation operation = _replacementCoordinator.Approve(review.Operation.Identity, review.Plan.Identity, current);

			CollectionReplacementOptionalBackupResult optionalBackup;
			IReadOnlyList<CollectionReplacementRecoveryInput> recoveryInputs;
			if (review.Plan.Policy.ReplacementBackupChoice == CollectionReplacementBackupChoice.CreateLocalCollection)
			{
				CollectionSaveCurrentSetupResult saved = await _localCapture.SaveCurrentSetupAsync(new CollectionSaveCurrentSetupRequest(
					"Before replacement - " + review.Plan.Revision.Collection.StableId, LocalCaptureCapability.LocallyRestorableWithinScope), cancellationToken).ConfigureAwait(false);
				if (!saved.IsSaved || saved.Capture == null || String.IsNullOrWhiteSpace(saved.PackageArtifactId))
					throw new InvalidOperationException("The requested Local Collection backup could not be sealed before replacement.");
				optionalBackup = CollectionReplacementOptionalBackupResult.Completed(saved.Capture.Identity.ToString());
				recoveryInputs = BuildRecoveryInputs(review, saved.PackageArtifactId);
			}
			else
			{
				optionalBackup = CollectionReplacementOptionalBackupResult.NotRequested();
				recoveryInputs = CaptureOperationRecoveryPackage(review, cancellationToken);
			}

			current = _nativeStateReader.CaptureReplacementCurrentSetup(review.Plan.Target);
			operation = _replacementCoordinator.PrepareMandatoryRecovery(review.Operation.Identity, review.Plan.Identity,
				current, optionalBackup, recoveryInputs);
			ProtectOutgoingProfile(review);

			CollectionReplacementOutgoingRemovalResult removal = await _outgoingRemoval.ExecuteAsync(operation.Identity,
				review.Plan.Identity, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			if (removal.Operation.Phase == CollectionOperationPhase.RecoveryRequired || removal.Operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.RecoveryRequired,
					removal.Operation, null, null, "Outgoing replacement removal requires recovery before continuation.");

			CollectionReplacementBarrierResult barrier = await _phaseBarrier.RevalidatePostOutgoingRemovalAsync(review.Operation.Identity,
				review.Plan.Identity, GetTargetPaths(), review.Baseline,
				(context, token) => Task.FromResult((IReadOnlyList<PreparedCollectionNativeRecipe>)ReprepareRequiredRecipes(review, context, token)),
				cancellationToken).ConfigureAwait(false);
			if (barrier.RequiresExplicitReview)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.ExplicitReviewRequired,
					barrier.Operation, null, barrier.ObservationArtifactId, barrier.Message);
			if (barrier.RequiresRecovery)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.RecoveryRequired,
					barrier.Operation, null, null, barrier.Message);

			CollectionReplacementIncomingInstallationResult incoming = await _incomingInstallation.ExecuteAsync(review.Operation.Identity,
				review.Plan, review.Baseline, review.VerifiedArchives, review.PreparedRecipes, GetTargetPaths(),
				(context, token) => Task.FromResult((IReadOnlyList<PreparedCollectionNativeRecipe>)ReprepareRequiredRecipes(review, context, token)),
				cancellationToken).ConfigureAwait(false);
			if (incoming.Status == CollectionReplacementIncomingInstallationStatus.ExplicitReviewRequired)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.ExplicitReviewRequired,
					incoming.Operation, null, null, incoming.Message);
			if (incoming.Status == CollectionReplacementIncomingInstallationStatus.NativeLaneBusy)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.PausedAtSafeBoundary,
					incoming.Operation, null, null, incoming.Message);
			if (incoming.Status != CollectionReplacementIncomingInstallationStatus.Completed)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.RecoveryRequired,
					incoming.Operation, null, null, incoming.Message);

			CollectionReplacementFinalizationResult finalization = await _aggregateVerification.VerifyAndFinalizeAsync(review.Operation.Identity,
				review.Plan, review.PreparedRecipes, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.Committed,
				finalization.Operation, finalization, null, "Replacement completed and the final managed setup was verified.");
		}


		public async Task<CollectionReplacementWorkflowApplyResult> ResumeAsync(CollectionReplacementWorkflowReview review,
			CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			CollectionOperation operation = _operationStore.GetOperation(review.Operation.Identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal)
				throw new InvalidOperationException("The replacement operation is not available for resume.");
			if (operation.Phase == CollectionOperationPhase.AwaitingReplacementPhaseAmendment)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.ExplicitReviewRequired,
					operation, null, null, "The exact C8.5 amendment must be explicitly approved before replacement can continue.");
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired || operation.Phase == CollectionOperationPhase.Recovering ||
				operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.RecoveryRequired,
					operation, null, null, "Replacement requires recovery before it can continue.");

			if (operation.Phase == CollectionOperationPhase.OutgoingRemovalVerified ||
				operation.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired)
			{
				CollectionReplacementBarrierResult barrier = await _phaseBarrier.RevalidatePostOutgoingRemovalAsync(operation.Identity,
					review.Plan.Identity, GetTargetPaths(), review.Baseline,
					(context, token) => Task.FromResult((IReadOnlyList<PreparedCollectionNativeRecipe>)ReprepareRequiredRecipes(review, context, token)),
					cancellationToken).ConfigureAwait(false);
				if (barrier.RequiresExplicitReview)
					return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.ExplicitReviewRequired,
						barrier.Operation, null, barrier.ObservationArtifactId, barrier.Message);
				if (barrier.RequiresRecovery)
					return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.RecoveryRequired,
						barrier.Operation, null, null, barrier.Message);
				operation = barrier.Operation;
			}

			if (operation.Phase == CollectionOperationPhase.ReadyForIncomingNativeChildren ||
				operation.Phase == CollectionOperationPhase.InstallingIncomingNativeChildren)
			{
				CollectionReplacementIncomingInstallationResult incoming = await _incomingInstallation.ExecuteAsync(operation.Identity,
					review.Plan, review.Baseline, review.VerifiedArchives, review.PreparedRecipes, GetTargetPaths(),
					(context, token) => Task.FromResult((IReadOnlyList<PreparedCollectionNativeRecipe>)ReprepareRequiredRecipes(review, context, token)),
					cancellationToken).ConfigureAwait(false);
				if (incoming.Status == CollectionReplacementIncomingInstallationStatus.ExplicitReviewRequired)
					return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.ExplicitReviewRequired,
						incoming.Operation, null, null, incoming.Message);
				if (incoming.Status == CollectionReplacementIncomingInstallationStatus.NativeLaneBusy)
					return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.PausedAtSafeBoundary,
						incoming.Operation, null, null, incoming.Message);
				if (incoming.Status != CollectionReplacementIncomingInstallationStatus.Completed)
					return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.RecoveryRequired,
						incoming.Operation, null, null, incoming.Message);
				operation = incoming.Operation;
			}

			if (operation.Phase != CollectionOperationPhase.IncomingNativeChildrenVerified)
				throw new InvalidOperationException("Replacement resume did not reach an aggregate-verification boundary.");
			CollectionReplacementFinalizationResult finalization = await _aggregateVerification.VerifyAndFinalizeAsync(operation.Identity,
				review.Plan, review.PreparedRecipes, GetTargetPaths(), cancellationToken).ConfigureAwait(false);
			return new CollectionReplacementWorkflowApplyResult(CollectionReplacementWorkflowApplyStatus.Committed,
				finalization.Operation, finalization, null, "Replacement completed and the final managed setup was verified.");
		}

		public CollectionOperation ApprovePendingAmendment(CollectionReplacementWorkflowReview review, string observationArtifactId)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			return _phaseBarrier.ApprovePendingAmendment(review.Operation.Identity, review.Plan.Identity,
				observationArtifactId, review.Baseline);
		}

		public Task<CollectionReplacementRecoveryResult> RecoverAsync(CollectionReplacementWorkflowReview review,
			CancellationToken cancellationToken)
		{
			if (review == null) throw new ArgumentNullException(nameof(review));
			return _recovery.RecoverAsync(review.Operation.Identity, review.Plan, GetTargetPaths(), cancellationToken);
		}

		private List<PreparedCollectionNativeRecipe> PrepareReplacementRecipes(ResolvedCollectionPlan plan,
			CollectionReplacementDiffPlan diff, CollectionReplacementEnvironmentProjection baseline,
			CollectionMemberMatchSet matches, CollectionDependencyPhasePlan dependency, List<CollectionVerifiedArchive> archives,
			CollectionReviewedWorkflowRuntime additiveRuntime, CancellationToken cancellationToken)
		{
			var result = new List<PreparedCollectionNativeRecipe>();
			CollectionReplacementEnvironmentProjection projected = baseline;
			var required = new HashSet<CollectionMemberKey>(diff.IncomingMembers.Where(x =>
				x.Disposition == CollectionReplacementDiffDisposition.IncomingOnly || x.Disposition == CollectionReplacementDiffDisposition.ReinstallOrChange)
				.Select(x => x.Member.MemberKey));
			foreach (CollectionExecutionPhase phase in dependency.Phases)
			{
				foreach (CollectionPlannedPhaseMember phaseMember in phase.Members)
				{
					if (!required.Contains(phaseMember.MemberKey)) continue;
					CollectionMemberMatchResult match = matches.Members.Single(x => x.Member.MemberKey.Equals(phaseMember.MemberKey));
					CollectionVerifiedArchive archive = archives.SingleOrDefault(x => x.Request.MemberKey.Equals(phaseMember.MemberKey));
					if (archive == null) archive = match.VerifiedArchive;
					if (archive == null) throw new InvalidOperationException("Replacement preparation requires the exact verified archive for " + phaseMember.MemberKey + ".");
					IMod mod = ResolveManagedMod(match, archive, cancellationToken);
					PreparedCollectionNativeRecipe additiveRecipe = additiveRuntime.GetPreparedRecipe(phaseMember.MemberKey);
					ModInstallContext context = additiveRecipe == null ? ResolveInstallContext(match) : additiveRecipe.InstallContext;
					CollectionInstallDestination destination = CollectionInstallDestinationResolver.Resolve(_services.ModManager.GameMode,
						match.Member, mod, context, cancellationToken);
					bool skipReadme = additiveRecipe == null ? _services.ModManager.EnvironmentInfo.Settings.SkipReadmeFiles : additiveRecipe.SkipReadmeFiles;
					PreparedCollectionNativeRecipe prepared = _recipePreparer.PrepareReplacementExact(plan, match.Member, archive, mod,
						_services.ModManager.GameMode, _services.ModManager.EnvironmentInfo, destination.InstallContext, diff.CurrentSetup.NativeState,
						projected, skipReadme, _services.PluginManager, cancellationToken, destination.GameRootArchiveBaseDirectory);
					CollectionNativeModState retainedPrevious = match.MatchedNativeMod;
					if (retainedPrevious != null && diff.NativeMods.Any(x => x.NativeMod.Identity.Equals(retainedPrevious.Identity) &&
						(x.RemovalDecision == CollectionReplacementRemovalDecision.EligibleForReviewedRemoval || x.RemovalDecision == CollectionReplacementRemovalDecision.RequiresExplicitReview)))
						retainedPrevious = null;
					prepared = CollectionInstallRootCorrection.Prepare(prepared, diff.CurrentSetup.NativeState, retainedPrevious,
						_services.ModManager.DeploymentManager, _services.ModManager.VirtualModActivator, cancellationToken);
					result.Add(prepared);
					projected = _environmentProjector.Overlay(projected, new[] { prepared.EffectPreview });
				}
			}
			return result;
		}

		private List<PreparedCollectionNativeRecipe> ReprepareRequiredRecipes(CollectionReplacementWorkflowReview review,
			CollectionReplacementBarrierPreparationContext context, CancellationToken cancellationToken)
		{
			var result = new List<PreparedCollectionNativeRecipe>();
			var frozenMatcher = new CollectionMemberMatchEngine((member, archive, token) => review.PreparedRecipes.Any(x =>
				x.Member.MemberKey.Equals(member.MemberKey) && x.InstallContext.InstallRoot == ModInstallRoot.GameRoot), false);
			CollectionMemberMatchSet matches = frozenMatcher.MatchForReplacementExecution(review.Plan,
				context.CurrentSetup.NativeState, review.VerifiedArchives, cancellationToken);
			foreach (CollectionMemberKey memberKey in context.RequiredMembers)
			{
				CollectionMemberMatchResult match = matches.Members.Single(x => x.Member.MemberKey.Equals(memberKey));
				CollectionVerifiedArchive archive = review.VerifiedArchives.SingleOrDefault(x => x.Request.MemberKey.Equals(memberKey)) ?? match.VerifiedArchive;
				if (archive == null) throw new InvalidOperationException("Replacement barrier revalidation lost the exact verified archive for " + memberKey + ".");
				PreparedCollectionNativeRecipe prior = review.PreparedRecipes.Single(x => x.Member.MemberKey.Equals(memberKey));
				IMod mod = ResolveManagedMod(match, archive, cancellationToken);
				result.Add(CollectionInstallRootCorrection.Attach(_recipePreparer.PrepareReplacementExact(review.Plan, match.Member, archive, mod,
					_services.ModManager.GameMode, _services.ModManager.EnvironmentInfo, prior.InstallContext,
					context.CurrentSetup.NativeState, context.ObservedEnvironment, prior.SkipReadmeFiles,
					_services.PluginManager, cancellationToken, prior.GameRootArchiveBaseDirectory), prior.EffectPreview.InstallRootCorrection));
			}
			return result;
		}

		private CollectionReplacementProfileProtectionSnapshot CaptureProfileProtection()
		{
			if (_profileManager.CurrentProfile == null) return CollectionReplacementProfileProtectionSnapshot.NoCurrentProfile();
			_profileManager.UpdateCurrentDeploymentManifest();
			IModProfile profile = _profileManager.CurrentProfile;
			return CollectionReplacementProfileProtectionSnapshot.Preserve(profile.Id, profile.Name,
				CollectionLocalRestoreProfileBoundaryCoordinator.ComputeProfileContentFingerprint(_profileManager.GetProfilePath(profile)));
		}

		private void ProtectOutgoingProfile(CollectionReplacementWorkflowReview review)
		{
			CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(review.Operation.Identity, review.Plan.Identity);
			if (!intent.ProfileProtection.HadCurrentProfile) return;
			IModProfile current = _profileManager.CurrentProfile;
			if (current == null || !StringComparer.Ordinal.Equals(current.Id, intent.ProfileProtection.ProfileId))
				throw new InvalidOperationException("The current profile changed after replacement review.");
			string fingerprint = CollectionLocalRestoreProfileBoundaryCoordinator.ComputeProfileContentFingerprint(_profileManager.GetProfilePath(current));
			if (!StringComparer.Ordinal.Equals(fingerprint, intent.ProfileProtection.ContentFingerprint))
				throw new InvalidOperationException("The current profile content changed after replacement review.");
			_profileManager.SetCurrentProfile(null);
		}

		private IReadOnlyList<CollectionReplacementRecoveryInput> CaptureOperationRecoveryPackage(CollectionReplacementWorkflowReview review,
			CancellationToken cancellationToken)
		{
			GameStoragePathSet paths = GetTargetPaths();
			var boundary = new CollectionLocalCaptureBoundary(_gameStorageService, CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services));
			return boundary.Execute(paths, target => CaptureOperationRecoveryPackageWithinBoundary(review, target, cancellationToken), cancellationToken);
		}

		private IReadOnlyList<CollectionReplacementRecoveryInput> CaptureOperationRecoveryPackageWithinBoundary(CollectionReplacementWorkflowReview review,
			CollectionTargetIdentity target, CancellationToken cancellationToken)
		{
			LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.NewGuid());
			CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(CollectionIdentity.FromLocal(Guid.NewGuid()), Guid.NewGuid());
			var nativeStateReader = new NativeStateCaptureReader(_services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.ModManager.DeploymentManager, _services.PluginManager, _services.ModManager.GameMode);
			var installedIdentityReader = new CollectionInstalledIdentityCaptureReader(nativeStateReader, _associationStore,
				_services.ModRepository == null ? null : _services.ModRepository.GameDomainName);
			var ownerPayloadCapture = new CollectionOwnerPayloadCaptureService(nativeStateReader, _artifactStore, _referenceStore);
			var scriptedReplayCapture = new CollectionScriptedReplayCaptureService(nativeStateReader, installedIdentityReader, _artifactStore, _referenceStore);
			var nativeEffectCapture = new CollectionNativeEffectCaptureReader(nativeStateReader);
			IModFormatUserMetadata userMetadataFormat = _services.ModManager.ModFormats
				.FirstOrDefault(x => String.Equals(x.Id, "FOMod", StringComparison.OrdinalIgnoreCase)) as IModFormatUserMetadata;
			var userMetadataCapture = new CollectionUserMetadataCaptureService(nativeStateReader, installedIdentityReader,
				_services.ModManager.SortOrderService, userMetadataFormat, _artifactStore, _referenceStore);
			var nativeIndexReader = new CollectionNativeStateReader(_services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore);

			try
			{
				NativeStateCaptureSnapshot native = nativeStateReader.Capture();
				CollectionNativeStateIndex capturedIndex = nativeIndexReader.Capture(target, native);
				CollectionInstalledIdentitySnapshot identities = installedIdentityReader.Capture(target, native);
				IReadOnlyList<LocalCaptureNativeRecordMapping> mappings = CreateRecoveryMappings(captureIdentity, target, identities.Mods);
				CollectionOwnerPayloadSnapshot payloads = ownerPayloadCapture.Capture(target, captureIdentity, native, cancellationToken);
				CollectionScriptedReplaySnapshot replay = scriptedReplayCapture.Capture(target, captureIdentity, native, identities, cancellationToken);
				CollectionNativeEffectSnapshot effects = nativeEffectCapture.Capture(target, native);
				CollectionUserMetadataSnapshot metadata = userMetadataCapture.Capture(target, captureIdentity, native, identities, cancellationToken);
				CollectionNativeStateIndex sealing = nativeIndexReader.Capture(target);
				var scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion, Enum.GetValues(typeof(LocalCaptureScopeArea)).Cast<LocalCaptureScopeArea>().Where(x => x != LocalCaptureScopeArea.Unknown));
				var request = new CollectionCaptureSealRequest(captureIdentity, revisionIdentity, target, capturedIndex.Fingerprint, sealing.Fingerprint,
					scope, LocalCaptureCapability.LocallyRestorableWithinScope, identities, payloads, replay, effects, metadata,
					new CollectionCapturedArchiveArtifact[0], new LocalCaptureExclusion[0], mappings);
				CollectionCaptureSealResult sealedResult = new CollectionCaptureSealer(_artifactStore, _referenceStore).Seal(request, cancellationToken);
				if (!sealedResult.IsSealed) throw new InvalidOperationException("Mandatory replacement recovery capture could not be sealed.");
				byte[] packageBytes = CollectionLocalCapturePackageCodec.Serialize(sealedResult);
				CollectionsRetainedArtifact package;
				using (var stream = new MemoryStream(packageBytes, false)) package = _artifactStore.Publish(stream, cancellationToken);
				string ownerId = review.Operation.Identity.OperationId.ToString("D");
				_referenceStore.AcquireExclusiveRoleReference(package.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, RecoveryPackageRole);
				int ordinal = 0;
				foreach (RetainedArtifactReference retained in sealedResult.SealedCapture.Capture.RetainedArtifacts)
					_referenceStore.AcquireExclusiveRoleReference(retained.StableArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
						ownerId, "replacement-recovery-retained-v1-" + (++ordinal).ToString(CultureInfo.InvariantCulture));
				return BuildRecoveryInputs(review, package.ArtifactId);
			}
			finally
			{
				_referenceStore.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString());
			}
		}

		private static IReadOnlyList<LocalCaptureNativeRecordMapping> CreateRecoveryMappings(LocalCaptureIdentity captureIdentity,
			CollectionTargetIdentity target, IEnumerable<CollectionInstalledModIdentity> mods)
		{
			var result = new List<LocalCaptureNativeRecordMapping>();
			int ordinal = 0;
			foreach (CollectionInstalledModIdentity mod in mods.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase))
				result.Add(new LocalCaptureNativeRecordMapping(CollectionMemberKey.FromLocal(CreateDeterministicGuid(captureIdentity + "|" + (++ordinal).ToString(CultureInfo.InvariantCulture) + "|" + mod.NativeSnapshotKey)),
					new NativeModInstanceIdentity(target, mod.NativeSnapshotKey)));
			return result;
		}

		private static Guid CreateDeterministicGuid(string value)
		{
			using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
			{
				byte[] digest = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value ?? String.Empty));
				byte[] guid = new byte[16]; Buffer.BlockCopy(digest, 0, guid, 0, 16); return new Guid(guid);
			}
		}

		private IReadOnlyList<CollectionReplacementRecoveryInput> BuildRecoveryInputs(CollectionReplacementWorkflowReview review,
			string packageArtifactId)
		{
			var result = new List<CollectionReplacementRecoveryInput>();
			int index = 0;
			foreach (CollectionReplacementNativeModImpact impact in review.Diff.NativeMods.Where(x =>
				x.RemovalDecision == CollectionReplacementRemovalDecision.EligibleForReviewedRemoval ||
				x.RemovalDecision == CollectionReplacementRemovalDecision.RequiresExplicitReview))
			{
				string subject = impact.NativeMod.Identity.NativeModKey;
				string prefix = (++index).ToString(CultureInfo.InvariantCulture);
				result.Add(new CollectionReplacementRecoveryInput(CollectionReplacementRecoveryInputKind.NativeIdentity, subject, "native-identity-" + prefix, packageArtifactId));
				result.Add(new CollectionReplacementRecoveryInput(CollectionReplacementRecoveryInputKind.ReconstructionPayload, subject, "reconstruction-payload-" + prefix, packageArtifactId));
				result.Add(new CollectionReplacementRecoveryInput(CollectionReplacementRecoveryInputKind.NativeEffects, subject, "native-effects-" + prefix, packageArtifactId));
			}
			CollectionReplacementReviewedIntent intent = _replacementCoordinator.GetReviewedIntent(review.Operation.Identity, review.Plan.Identity);
			if (intent.ProfileProtection.HadCurrentProfile)
				result.Add(new CollectionReplacementRecoveryInput(CollectionReplacementRecoveryInputKind.ProfileProtection,
					intent.ProfileProtection.ProfileId, "profile-protection", packageArtifactId));
			return result;
		}

		private List<CollectionReplacementNativeApproval> BuildNativeApprovals(CollectionReplacementDiffPlan diff)
		{
			return diff.NativeMods.Select(x => new CollectionReplacementNativeApproval(x.NativeMod.Identity.NativeModKey,
				x.RemovalDecision == CollectionReplacementRemovalDecision.EligibleForReviewedRemoval || x.RemovalDecision == CollectionReplacementRemovalDecision.RequiresExplicitReview
					? CollectionReplacementNativeDecision.Remove : CollectionReplacementNativeDecision.Protected)).ToList();
		}

		private static List<CollectionReplacementAssociationApproval> BuildAssociationApprovals(CollectionReplacementDiffPlan diff)
		{
			return diff.Associations.Select(x => new CollectionReplacementAssociationApproval(x.Association.AssociationId,
				x.Disposition == CollectionReplacementAssociationDisposition.SurvivesCompatible
					? CollectionReplacementAssociationDecision.Preserve : CollectionReplacementAssociationDecision.TransitionOut)).ToList();
		}

		private static List<CollectionReplacementFileWinnerApproval> BuildWinnerApprovals(CollectionConflictImpactPlan impact)
		{
			return impact.FileImpacts.Where(x => x.Writers.Count > 1 && x.PlannedWinner != null)
				.Select(x => new CollectionReplacementFileWinnerApproval(x.Target, x.PlannedWinner)).ToList();
		}

		private ModInstallContext ResolveInstallContext(CollectionMemberMatchResult match)
		{
			if (match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired && match.MatchedNativeMod != null)
				return new ModInstallContext(match.MatchedNativeMod.InstallMethod, match.Member.RequiresGameRootInstall ? ModInstallRoot.GameRoot : match.MatchedNativeMod.InstallRoot);
			return _services.ModManager.CapturePreferredInstallContext(match.Member.RequiresGameRootInstall ? ModInstallRoot.GameRoot : ModInstallRoot.Default);
		}

		private IMod ResolveManagedMod(CollectionMemberMatchResult match, CollectionVerifiedArchive archive, CancellationToken cancellationToken)
		{
			if (match.MatchedNativeMod != null)
			{
				List<IMod> active = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
					StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), match.MatchedNativeMod.Identity.NativeModKey)).ToList();
				if (active.Count == 1 && CollectionArchiveContentMatcher.MatchesFile(CollectionArchiveContentMatcher.GetManagedArchivePath(active[0]), archive.Artifact, cancellationToken))
					return active[0];
			}
			List<IMod> exact = CollectionArchiveContentMatcher.FindExactManagedMods(_services.ModManager, archive.Artifact, cancellationToken);
			if (exact.Count == 1) return exact[0];
			if (exact.Count == 0) throw new InvalidOperationException("The exact Download / Prepare archive is no longer registered in the native mod library.");
			throw new InvalidOperationException("Multiple native managed archives match the exact replacement archive bytes.");
		}
	}
}
