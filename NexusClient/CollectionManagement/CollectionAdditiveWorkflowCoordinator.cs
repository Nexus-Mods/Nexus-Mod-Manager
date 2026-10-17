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
using Nexus.Client.Games;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Deterministic composition seam for exercising one reviewed C6.7/C6.8 native-child boundary in vertical tests.</summary>
	internal delegate Task<CollectionNativeChildVerificationResult> CollectionAdditiveNativeChildApplyDelegate(
		CollectionNativeChildPreparationResult prepared, ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan,
		CollectionMemberEffectPreview reviewedPreview, ModInstallationRecipeInput childRecipe, GameStoragePathSet targetPaths,
		CancellationToken cancellationToken);

	/// <summary>Deterministic composition seam for the final reviewed-winner barrier in vertical tests.</summary>
	internal delegate Task CollectionAdditiveWinnerReconciliationDelegate(CollectionOperationIdentity operationIdentity,
		ResolvedCollectionPlan plan, CollectionMemberMatchSet matches, CollectionConflictImpactPlan impactPlan,
		GameStoragePathSet targetPaths, CancellationToken cancellationToken);

	/// <summary>Describes the high-level preparation result exposed by the headless additive Collection workflow.</summary>
	public enum CollectionAdditiveWorkflowPreparationStatus
	{
		AwaitingInput = 1,
		PreparationRequired = 2,
		ActionRequired = 3,
		Blocked = 4,
		ReadyForReview = 5
	}

	/// <summary>Keeps the affected member attached to a native preparation failure for user-visible reporting.</summary>
	internal sealed class CollectionMemberPreparationException : Exception
	{
		/// <summary>Creates a failure with its member and original native diagnostic.</summary>
		internal CollectionMemberPreparationException(ResolvedCollectionMemberPlan member, string detail, Exception cause)
			: base(detail, cause)
		{
			Member = member ?? throw new ArgumentNullException(nameof(member));
		}

		internal ResolvedCollectionMemberPlan Member { get; }

		/// <summary>Preserves the existing distinction between a refreshable preparation failure and a blocker.</summary>
		internal CollectionAdditiveWorkflowPreparationStatus Status
		{
			get { return InnerException is InvalidOperationException
				? CollectionAdditiveWorkflowPreparationStatus.PreparationRequired : CollectionAdditiveWorkflowPreparationStatus.Blocked; }
		}
	}

	/// <summary>Immutable headless preparation result for one additive Collection operation.</summary>
	public sealed class CollectionAdditiveWorkflowPreparationResult
	{
		internal CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus status,
			CollectionOperation operation, CollectionMemberAcquisitionBatch acquisitionBatch,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
			CollectionReviewedWorkflowRuntime runtime, string message)
			: this(status, operation, acquisitionBatch, dependencyPlan, impactPlan, runtime, message, null)
		{
		}

		internal CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus status,
			CollectionOperation operation, CollectionMemberAcquisitionBatch acquisitionBatch,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
			CollectionReviewedWorkflowRuntime runtime, string message,
			IEnumerable<PreparedCollectionNativeRecipe> pendingFileWinnerRecipes,
			ResolvedCollectionMemberPlan failedMember = null, string failureReason = null,
			IEnumerable<CollectionMemberPreparationException> preparationFailures = null)
		{
			if (!Enum.IsDefined(typeof(CollectionAdditiveWorkflowPreparationStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			AcquisitionBatch = acquisitionBatch;
			DependencyPlan = dependencyPlan;
			ImpactPlan = impactPlan;
			Runtime = runtime;
			Message = message ?? String.Empty;
			PreparationFailures = new ReadOnlyCollection<CollectionMemberPreparationException>(
				(preparationFailures ?? Enumerable.Empty<CollectionMemberPreparationException>()).ToList());
			if (PreparationFailures.Any(x => x == null) || (PreparationFailures.Count > 0 && (runtime != null ||
				status == CollectionAdditiveWorkflowPreparationStatus.ReadyForReview)))
				throw new ArgumentException("Member preparation failures cannot authorize a reviewed installation.", nameof(preparationFailures));
			CollectionMemberPreparationException firstFailure = PreparationFailures.FirstOrDefault();
			FailedMember = failedMember ?? firstFailure?.Member;
			FailureReason = failureReason ?? firstFailure?.InnerException?.Message ?? String.Empty;
			// In-memory only: a conflict-only pause may reuse the already inspected native recipes.
			// This is never persisted and never authorizes installation by itself.
			PendingFileWinnerRecipes = new ReadOnlyCollection<PreparedCollectionNativeRecipe>(
				(pendingFileWinnerRecipes ?? Enumerable.Empty<PreparedCollectionNativeRecipe>()).ToList());
			if (PendingFileWinnerRecipes.Count > 0 && (status != CollectionAdditiveWorkflowPreparationStatus.ActionRequired ||
				impactPlan == null || runtime != null || PendingFileWinnerRecipes.Any(x => x == null)))
				throw new ArgumentException("Only a conflict-only pre-review pause may retain native recipes.", nameof(pendingFileWinnerRecipes));
		}

		public CollectionAdditiveWorkflowPreparationStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionMemberAcquisitionBatch AcquisitionBatch { get; }
		public CollectionDependencyPhasePlan DependencyPlan { get; }
		public CollectionConflictImpactPlan ImpactPlan { get; }
		public CollectionReviewedWorkflowRuntime Runtime { get; }
		public string Message { get; }
		internal ResolvedCollectionMemberPlan FailedMember { get; }
		internal string FailureReason { get; }
		internal ReadOnlyCollection<CollectionMemberPreparationException> PreparationFailures { get; }
		internal ReadOnlyCollection<PreparedCollectionNativeRecipe> PendingFileWinnerRecipes { get; }
		public bool IsReadyForReview { get { return Status == CollectionAdditiveWorkflowPreparationStatus.ReadyForReview; } }
	}

	/// <summary>Immutable read-only view of one persisted additive Collection review.</summary>
	public sealed class CollectionAdditiveWorkflowReview
	{
		internal CollectionAdditiveWorkflowReview(CollectionOperation operation,
			CollectionReviewedWorkflowRehydrationResult rehydration, CollectionReviewedWorkflowRuntime runtime)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Rehydration = rehydration ?? throw new ArgumentNullException(nameof(rehydration));
			Runtime = runtime;
		}

		public CollectionOperation Operation { get; }
		public CollectionReviewedWorkflowRehydrationResult Rehydration { get; }
		public CollectionReviewedWorkflowRuntime Runtime { get; }
		public bool IsReady { get { return Rehydration.CanResume && Runtime != null; } }
	}

	/// <summary>Describes the result of applying or resuming an exact approved additive Collection review.</summary>
	public enum CollectionAdditiveWorkflowApplyStatus
	{
		Committed = 1,
		PausedAtSafeBoundary = 2,
		RecoveryRequired = 3,
		RepreparationRequired = 4,
		StoppedPartial = 5
	}

	/// <summary>Immutable result of one headless additive apply/resume request.</summary>
	public sealed class CollectionAdditiveWorkflowApplyResult
	{
		internal CollectionAdditiveWorkflowApplyResult(CollectionAdditiveWorkflowApplyStatus status,
			CollectionOperation operation, CollectionAssociationFinalizationResult finalization, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionAdditiveWorkflowApplyStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Finalization = finalization;
			Message = message ?? String.Empty;
		}
		public CollectionAdditiveWorkflowApplyStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionAssociationFinalizationResult Finalization { get; }
		public string Message { get; }
		public bool IsCommitted { get { return Status == CollectionAdditiveWorkflowApplyStatus.Committed; } }
	}

	/// <summary>Describes startup/target-open reconciliation of one incomplete additive Collection operation.</summary>
	public enum CollectionAdditiveWorkflowRecoveryStatus
	{
		ReviewRequired = 1,
		ReadyToResume = 2,
		RepreparationRequired = 3,
		RecoveryRequired = 4,
		StoppedPartial = 5
	}

	/// <summary>Immutable startup reconciliation result for one incomplete additive Collection operation.</summary>
	public sealed class CollectionAdditiveWorkflowRecoveryResult
	{
		internal CollectionAdditiveWorkflowRecoveryResult(CollectionAdditiveWorkflowRecoveryStatus status,
			CollectionOperation operation, CollectionReviewedWorkflowRehydrationResult rehydration, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionAdditiveWorkflowRecoveryStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Rehydration = rehydration;
			Message = message ?? String.Empty;
		}
		public CollectionAdditiveWorkflowRecoveryStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionReviewedWorkflowRehydrationResult Rehydration { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Composes C2/C4/C5/C6 primitives into the first headless additive Collections application workflow.
	/// </summary>
	/// <remarks>
	/// This service owns sequencing only. Native NMM remains authoritative for installed state and every mutation still crosses the
	/// existing C3/C5 child execution seams under C4 target authority. WinForms must consume this high-level service rather than
	/// constructing or invoking the individual persistence/native coordinators directly.
	/// </remarks>
	public sealed class CollectionAdditiveWorkflowCoordinator
	{
		private readonly ServiceManager _services;
		private readonly CollectionAutomaticDependencyResolver _automaticDependencies = new CollectionAutomaticDependencyResolver();
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionAdditivePlanPreparationService _planPreparationService;
		private readonly CollectionAdditivePlanRevalidationService _planRevalidationService;
		private readonly CollectionMemberAcquisitionCoordinator _memberAcquisitionCoordinator;
		private readonly CollectionNativeRecipePreparer _nativeRecipePreparer;
		private readonly object _preparationCacheLock = new object();
		private ResolvedCollectionPlan _preparationCachePlan;
		private Dictionary<string, PreparedCollectionNativeRecipe> _completedRecipeCache = new Dictionary<string, PreparedCollectionNativeRecipe>(StringComparer.Ordinal);
		private readonly CollectionDependencyPhasePlanner _dependencyPlanner;
		private readonly CollectionConflictImpactPlanner _impactPlanner;
		private readonly CollectionOperationCoordinator _operationCoordinator;
		private readonly CollectionReviewedWorkflowRehydrator _workflowRehydrator;
		private readonly CollectionReviewedWorkflowRuntimeReconstructor _runtimeReconstructor;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionNativeChildPreparationCoordinator _childPreparationCoordinator;
		private readonly CollectionNativeChildExecutionCoordinator _childExecutionCoordinator;
		private readonly CollectionNativeChildVerificationCoordinator _childVerificationCoordinator;
		private readonly CollectionNativeChildRestartReconciliationCoordinator _restartCoordinator;
		private readonly CollectionAssociationReconciliationCoordinator _associationCoordinator;
		private readonly CollectionReviewedFileWinnerReconciliationCoordinator _winnerCoordinator;
		private readonly CollectionConflictResolutionCoordinator _conflictResolutionCoordinator;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionAdditiveNativeChildApplyDelegate _nativeChildApplyOverride;
		private readonly CollectionAdditiveWinnerReconciliationDelegate _winnerReconciliationOverride;

		/// <summary>Creates the C6.15.12 headless additive workflow over the already implemented planning/native seams.</summary>
		public CollectionAdditiveWorkflowCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionAdditivePlanPreparationService planPreparationService,
			CollectionAdditivePlanRevalidationService planRevalidationService,
			CollectionMemberAcquisitionCoordinator memberAcquisitionCoordinator,
			CollectionNativeRecipePreparer nativeRecipePreparer,
			CollectionDependencyPhasePlanner dependencyPlanner, CollectionConflictImpactPlanner impactPlanner,
			CollectionOperationCoordinator operationCoordinator, CollectionReviewedWorkflowRehydrator workflowRehydrator,
			CollectionReviewedWorkflowRuntimeReconstructor runtimeReconstructor, CollectionNativeStateReader nativeStateReader,
			CollectionNativeChildPreparationCoordinator childPreparationCoordinator,
			CollectionNativeChildExecutionCoordinator childExecutionCoordinator,
			CollectionNativeChildVerificationCoordinator childVerificationCoordinator,
			CollectionNativeChildRestartReconciliationCoordinator restartCoordinator,
			CollectionAssociationReconciliationCoordinator associationCoordinator,
			CollectionReviewedFileWinnerReconciliationCoordinator winnerCoordinator)
			: this(services, gameStorageService, operationStore, planStore, planPreparationService, planRevalidationService,
				memberAcquisitionCoordinator, nativeRecipePreparer, dependencyPlanner, impactPlanner, operationCoordinator,
				workflowRehydrator, runtimeReconstructor, nativeStateReader, childPreparationCoordinator, childExecutionCoordinator,
				childVerificationCoordinator, restartCoordinator, associationCoordinator, winnerCoordinator,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		/// <summary>Creates the production additive workflow with durable C9 conflict decisions composed into impact planning.</summary>
		internal CollectionAdditiveWorkflowCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionAdditivePlanPreparationService planPreparationService,
			CollectionAdditivePlanRevalidationService planRevalidationService,
			CollectionMemberAcquisitionCoordinator memberAcquisitionCoordinator,
			CollectionNativeRecipePreparer nativeRecipePreparer,
			CollectionDependencyPhasePlanner dependencyPlanner, CollectionConflictImpactPlanner impactPlanner,
			CollectionOperationCoordinator operationCoordinator, CollectionReviewedWorkflowRehydrator workflowRehydrator,
			CollectionReviewedWorkflowRuntimeReconstructor runtimeReconstructor, CollectionNativeStateReader nativeStateReader,
			CollectionNativeChildPreparationCoordinator childPreparationCoordinator,
			CollectionNativeChildExecutionCoordinator childExecutionCoordinator,
			CollectionNativeChildVerificationCoordinator childVerificationCoordinator,
			CollectionNativeChildRestartReconciliationCoordinator restartCoordinator,
			CollectionAssociationReconciliationCoordinator associationCoordinator,
			CollectionReviewedFileWinnerReconciliationCoordinator winnerCoordinator,
			CollectionConflictResolutionCoordinator conflictResolutionCoordinator)
			: this(services, gameStorageService, operationStore, planStore, planPreparationService, planRevalidationService,
				memberAcquisitionCoordinator, nativeRecipePreparer, dependencyPlanner, impactPlanner, operationCoordinator,
				workflowRehydrator, runtimeReconstructor, nativeStateReader, childPreparationCoordinator, childExecutionCoordinator,
				childVerificationCoordinator, restartCoordinator, associationCoordinator, winnerCoordinator,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services),
				null, null, conflictResolutionCoordinator)
		{
		}

		internal CollectionAdditiveWorkflowCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionAdditivePlanPreparationService planPreparationService,
			CollectionAdditivePlanRevalidationService planRevalidationService,
			CollectionMemberAcquisitionCoordinator memberAcquisitionCoordinator,
			CollectionNativeRecipePreparer nativeRecipePreparer,
			CollectionDependencyPhasePlanner dependencyPlanner, CollectionConflictImpactPlanner impactPlanner,
			CollectionOperationCoordinator operationCoordinator, CollectionReviewedWorkflowRehydrator workflowRehydrator,
			CollectionReviewedWorkflowRuntimeReconstructor runtimeReconstructor, CollectionNativeStateReader nativeStateReader,
			CollectionNativeChildPreparationCoordinator childPreparationCoordinator,
			CollectionNativeChildExecutionCoordinator childExecutionCoordinator,
			CollectionNativeChildVerificationCoordinator childVerificationCoordinator,
			CollectionNativeChildRestartReconciliationCoordinator restartCoordinator,
			CollectionAssociationReconciliationCoordinator associationCoordinator,
			CollectionReviewedFileWinnerReconciliationCoordinator winnerCoordinator,
			CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
			: this(services, gameStorageService, operationStore, planStore, planPreparationService, planRevalidationService,
				memberAcquisitionCoordinator, nativeRecipePreparer, dependencyPlanner, impactPlanner, operationCoordinator,
				workflowRehydrator, runtimeReconstructor, nativeStateReader, childPreparationCoordinator, childExecutionCoordinator,
				childVerificationCoordinator, restartCoordinator, associationCoordinator, winnerCoordinator, mutationLeaseManager,
				authorityValidator, null, null, null)
		{
		}

		/// <summary>Creates the headless workflow with deterministic native-boundary overrides used only by vertical composition tests.</summary>
		internal CollectionAdditiveWorkflowCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionAdditivePlanPreparationService planPreparationService,
			CollectionAdditivePlanRevalidationService planRevalidationService,
			CollectionMemberAcquisitionCoordinator memberAcquisitionCoordinator,
			CollectionNativeRecipePreparer nativeRecipePreparer,
			CollectionDependencyPhasePlanner dependencyPlanner, CollectionConflictImpactPlanner impactPlanner,
			CollectionOperationCoordinator operationCoordinator, CollectionReviewedWorkflowRehydrator workflowRehydrator,
			CollectionReviewedWorkflowRuntimeReconstructor runtimeReconstructor, CollectionNativeStateReader nativeStateReader,
			CollectionNativeChildPreparationCoordinator childPreparationCoordinator,
			CollectionNativeChildExecutionCoordinator childExecutionCoordinator,
			CollectionNativeChildVerificationCoordinator childVerificationCoordinator,
			CollectionNativeChildRestartReconciliationCoordinator restartCoordinator,
			CollectionAssociationReconciliationCoordinator associationCoordinator,
			CollectionReviewedFileWinnerReconciliationCoordinator winnerCoordinator,
			CollectionTargetMutationLeaseManager mutationLeaseManager, CollectionTargetOwnershipAuthorityValidator authorityValidator,
			CollectionAdditiveNativeChildApplyDelegate nativeChildApplyOverride,
			CollectionAdditiveWinnerReconciliationDelegate winnerReconciliationOverride,
			CollectionConflictResolutionCoordinator conflictResolutionCoordinator = null)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_planPreparationService = planPreparationService ?? throw new ArgumentNullException(nameof(planPreparationService));
			_planRevalidationService = planRevalidationService ?? throw new ArgumentNullException(nameof(planRevalidationService));
			_memberAcquisitionCoordinator = memberAcquisitionCoordinator ?? throw new ArgumentNullException(nameof(memberAcquisitionCoordinator));
			_nativeRecipePreparer = nativeRecipePreparer ?? throw new ArgumentNullException(nameof(nativeRecipePreparer));
			_dependencyPlanner = dependencyPlanner ?? throw new ArgumentNullException(nameof(dependencyPlanner));
			_impactPlanner = impactPlanner ?? throw new ArgumentNullException(nameof(impactPlanner));
			_operationCoordinator = operationCoordinator ?? throw new ArgumentNullException(nameof(operationCoordinator));
			_workflowRehydrator = workflowRehydrator ?? throw new ArgumentNullException(nameof(workflowRehydrator));
			_runtimeReconstructor = runtimeReconstructor ?? throw new ArgumentNullException(nameof(runtimeReconstructor));
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_childPreparationCoordinator = childPreparationCoordinator ?? throw new ArgumentNullException(nameof(childPreparationCoordinator));
			_childExecutionCoordinator = childExecutionCoordinator ?? throw new ArgumentNullException(nameof(childExecutionCoordinator));
			_childVerificationCoordinator = childVerificationCoordinator ?? throw new ArgumentNullException(nameof(childVerificationCoordinator));
			_restartCoordinator = restartCoordinator ?? throw new ArgumentNullException(nameof(restartCoordinator));
			_associationCoordinator = associationCoordinator ?? throw new ArgumentNullException(nameof(associationCoordinator));
			_winnerCoordinator = winnerCoordinator ?? throw new ArgumentNullException(nameof(winnerCoordinator));
			_conflictResolutionCoordinator = conflictResolutionCoordinator;
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			_nativeChildApplyOverride = nativeChildApplyOverride;
			_winnerReconciliationOverride = winnerReconciliationOverride;
			if (_services.ModManager == null)
				throw new InvalidOperationException("Collection workflow preparation requires the live ModManager service.");
		}

		/// <summary>Starts headless additive preparation and returns either pending input, a blocked/action state, or one durable exact review.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			return PrepareAsync(effectiveSelection, targetPaths, CollectionArchiveOverwritePolicy.Prompt,
				confirmOverwriteCallback, cancellationToken);
		}

		/// <summary>Starts headless additive preparation with one immutable archive-overwrite policy for all correlated acquisition work.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, CollectionArchiveOverwritePolicy archiveOverwritePolicy,
			ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			if (effectiveSelection == null) throw new ArgumentNullException(nameof(effectiveSelection));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			return PrepareAsync(effectiveSelection, targetPaths, archiveOverwritePolicy,
				confirmOverwriteCallback, null, cancellationToken);
		}

		/// <summary>Prepares a fresh reviewed plan using one sealed alternate external archive authorized by the user.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, CollectionArchiveOverwritePolicy archiveOverwritePolicy,
			ConfirmOverwriteCallback confirmOverwriteCallback, CollectionAlternateArchiveCandidate alternate,
			CancellationToken cancellationToken, CollectionExecutionPolicy policy = null)
		{
			if (effectiveSelection == null) throw new ArgumentNullException(nameof(effectiveSelection));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			if (archiveOverwritePolicy == null) throw new ArgumentNullException(nameof(archiveOverwritePolicy));
			return Task.Run(() => PrepareCore(effectiveSelection, targetPaths, archiveOverwritePolicy,
				confirmOverwriteCallback, alternate, cancellationToken, policy), cancellationToken);
		}

		/// <summary>Refreshes a stale pre-install review using the same selected artifacts and verified acquisitions.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> RefreshInstallationAsync(CollectionMemberAcquisitionBatch acquisition,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken, string reviewMessage = null)
		{
			if (acquisition == null || !acquisition.IsReady) throw new ArgumentException("Verified acquisition is required.", nameof(acquisition));
			return Task.Run(() =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionOperation operation = RequireOperation(acquisition.PlanBuild.Operation.Identity);
				if (operation.HasCrossedNativeBoundary || operation.Phase != CollectionOperationPhase.Preparing)
					throw new InvalidOperationException("Only a reopened pre-install review can be refreshed automatically.");
				CollectionAdditivePlanBuildResult refreshed = _planPreparationService.Refresh(operation, acquisition.PlanBuild.Plan, targetPaths);
				if (refreshed.Plan.Identity.Equals(acquisition.PlanBuild.Plan.Identity))
					return new CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus.PreparationRequired,
						operation, acquisition, null, null, null, reviewMessage ?? "The prepared review could not be resumed.");
				CollectionMemberAcquisitionBatch rebound = _memberAcquisitionCoordinator.RebindReadyBatch(acquisition, refreshed, cancellationToken);
				return ContinuePreparation(rebound, targetPaths, cancellationToken);
			}, cancellationToken);
		}

		/// <summary>Retries one paused member without rebuilding the plan or reacquiring other members.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> RetryMemberAcquisitionAsync(CollectionMemberAcquisitionBatch batch,
			CollectionMemberKey memberKey, GameStoragePathSet targetPaths, ConfirmOverwriteCallback confirmOverwriteCallback,
			CancellationToken cancellationToken)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			return Task.Run(() => ContinuePreparation(_memberAcquisitionCoordinator.RetryMember(batch, memberKey,
				confirmOverwriteCallback, cancellationToken), targetPaths, cancellationToken), cancellationToken);
		}

		/// <summary>Resumes a previously returned acquisition/input pause, then revalidates target state before any later planning.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> ResumePreparationAsync(CollectionMemberAcquisitionBatch acquisitionBatch,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (acquisitionBatch == null) throw new ArgumentNullException(nameof(acquisitionBatch));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			return Task.Run(() => ResumePreparationCore(acquisitionBatch, targetPaths, cancellationToken), cancellationToken);
		}

		/// <summary>
		/// Continues a conflict-only review after durable per-file choices were saved. On an unchanged
		/// authoritative target, re-plans file ownership from already prepared native effect previews;
		/// it does NOT re-extract archives, resolve Nexus policies, or install anything. Changes in
		/// native state require ordinary full preparation instead of replaying stale recipe effects.
		/// </summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> ResumeFileWinnerReviewAsync(
			CollectionAdditiveWorkflowPreparationResult previous, GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (previous == null) throw new ArgumentNullException(nameof(previous));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			if (_conflictResolutionCoordinator == null || previous.Status != CollectionAdditiveWorkflowPreparationStatus.ActionRequired ||
				previous.AcquisitionBatch == null || !previous.AcquisitionBatch.IsReady || previous.DependencyPlan == null ||
				previous.ImpactPlan == null || previous.PendingFileWinnerRecipes.Count == 0 ||
				previous.ImpactPlan.Issues.Count == 0 || previous.ImpactPlan.Issues.Any(x =>
					x.Kind != CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired))
				throw new InvalidOperationException("Only a fully prepared file-winner-only review can resume without full preparation.");
			return Task.Run(() => ResumeFileWinnerReviewCore(previous, targetPaths, cancellationToken), cancellationToken);
		}

		private CollectionAdditiveWorkflowPreparationResult ResumeFileWinnerReviewCore(
			CollectionAdditiveWorkflowPreparationResult previous, GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CollectionMemberAcquisitionBatch acquisition = previous.AcquisitionBatch;
			CollectionAdditivePlanBuildResult planBuild = acquisition.PlanBuild;
			ResolvedCollectionPlan plan = planBuild.Plan;
			CollectionOperation operation = RequireOperation(previous.Operation.Identity);
			if (operation.Phase != CollectionOperationPhase.Preparing || operation.IsTerminal || operation.HasCrossedNativeBoundary ||
				!operation.Identity.Equals(planBuild.Operation.Identity) || !previous.ImpactPlan.PlanIdentity.Equals(plan.Identity) ||
				!previous.DependencyPlan.PlanIdentity.Equals(plan.Identity))
				throw new InvalidOperationException("The previous file-winner review is no longer at its exact pre-installation boundary.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(targetPaths);
			if (!authority.Target.Equals(plan.Target))
				throw new InvalidOperationException("The active Collection target changed. Start a fresh preparation before installing.");
			using (CollectionTargetMutationLease lease = _mutationLeaseManager.Acquire(authority, cancellationToken))
			{
				// Reload/recover the authoritative native state behind the canonical process+target lock.
				_authorityValidator.ValidateAndReload(lease, authority, targetPaths);
				CollectionNativeStateIndex state = _nativeStateReader.Capture(authority.Target);
				cancellationToken.ThrowIfCancellationRequested();
				if (!state.Fingerprint.Equals(plan.CurrentStateFingerprint))
					return new CollectionAdditiveWorkflowPreparationResult(
						CollectionAdditiveWorkflowPreparationStatus.PreparationRequired, operation, acquisition,
						previous.DependencyPlan, null, null,
						"The installed-file ownership state changed after conflict review. Start a fresh Download / Prepare; no old file decision was applied to a changed owner.");

				IReadOnlyList<CollectionConflictResolutionDecision> decisions = _conflictResolutionCoordinator.GetDecisions(plan);
				CollectionConflictImpactPlan impact = _impactPlanner.Plan(plan, acquisition.MatchSet, previous.DependencyPlan,
					state, previous.PendingFileWinnerRecipes.Select(x => x.EffectPreview), decisions);
				if (!impact.IsReady)
				{
					CollectionAdditiveWorkflowPreparationStatus status = impact.Status == CollectionConflictImpactStatus.ActionRequired
						? CollectionAdditiveWorkflowPreparationStatus.ActionRequired
						: impact.Status == CollectionConflictImpactStatus.PreparationRequired
							? CollectionAdditiveWorkflowPreparationStatus.PreparationRequired
							: CollectionAdditiveWorkflowPreparationStatus.Blocked;
					return new CollectionAdditiveWorkflowPreparationResult(status, operation, acquisition,
						previous.DependencyPlan, impact, null,
						impact.Issues.Count == 0 ? "The new file-winner review is not executable." : impact.Issues[0].Message,
						status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired && impact.Issues.Count > 0 &&
						impact.Issues.All(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired)
							? previous.PendingFileWinnerRecipes : null);
				}

				// Same C6.1 fingerprint, same immutable requested/selected archives, same native recipes.
				// This publishes a review, not approval. GetReview/Approve still rehydrate and verify inputs.
				CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(
					plan, previous.DependencyPlan, impact, previous.PendingFileWinnerRecipes);
				cancellationToken.ThrowIfCancellationRequested();
				CollectionOperation ready = _operationCoordinator.MarkReadyForReview(operation.Identity,
					plan, previous.DependencyPlan, impact, previous.PendingFileWinnerRecipes);
				List<CollectionVerifiedArchive> archives = acquisition.Members
					.Where(x => x.VerifiedArchive != null).Select(x => x.VerifiedArchive).ToList();
				var runtime = new CollectionReviewedWorkflowRuntime(snapshot, plan, acquisition.MatchSet,
					previous.DependencyPlan, impact, state, snapshot.Members.Select(x => x.MemberKey),
					previous.PendingFileWinnerRecipes, archives);
				return new CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview,
					ready, acquisition, previous.DependencyPlan, impact, runtime,
					"File-conflict decisions were applied to the unchanged native state; the exact Collection review is ready for approval.");
			}
		}

		/// <summary>Verifies one user-selected archive for an exact pending manual/browse acquisition.</summary>
		public CollectionVerifiedArchive VerifyLocalFile(CollectionMemberAcquisitionBatch acquisitionBatch,
			CollectionManualAcquisitionPendingAction pendingAction, string localFilePath, CancellationToken cancellationToken)
		{
			return _memberAcquisitionCoordinator.VerifyLocalFile(acquisitionBatch, pendingAction, localFilePath, cancellationToken);
		}

		/// <summary>Inspects an external archive candidate without changing the existing approved/pending plan.</summary>
		public CollectionAlternateArchiveCandidate InspectAlternateLocalFile(CollectionMemberAcquisitionBatch batch,
			CollectionManualAcquisitionPendingAction pending, string path, CancellationToken token)
		{
			return _memberAcquisitionCoordinator.InspectAlternateLocalFile(batch, pending, path, token);
		}

		public void ReleaseAlternateLocalFile(CollectionAlternateArchiveCandidate candidate)
		{
			_memberAcquisitionCoordinator.ReleaseAlternateLocalFile(candidate);
		}

		/// <summary>Completes a superseded additive preparation before any native child has crossed the mutation boundary.</summary>
		public CollectionOperation CancelBeforeApply(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = RequireOperation(operationIdentity);
			if (operation.IsTerminal)
				return operation;
			if (operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("An additive Collection operation cannot be cancelled as superseded after native mutation has begun.");
			return _operationCoordinator.CompleteCancelledBeforeApply(operationIdentity);
		}

		/// <summary>Loads and revalidates the exact persisted review without authorizing mutation.</summary>
		public CollectionAdditiveWorkflowReview GetReview(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			RequireOperation(operationIdentity);
			CollectionReviewedWorkflowRehydrationResult rehydration = _workflowRehydrator.Rehydrate(operationIdentity);
			CollectionReviewedWorkflowRuntime runtime = rehydration.CanResume ? _runtimeReconstructor.Reconstruct(rehydration) : null;
			return new CollectionAdditiveWorkflowReview(RequireOperation(operationIdentity), rehydration, runtime);
		}

		/// <summary>Approves the exact reviewed plan when necessary and sequences C6.6-C6.10 plus reviewed-winner reconciliation to completion.</summary>
		public async Task<CollectionAdditiveWorkflowApplyResult> ApproveAndApplyAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));

			CollectionOperation operation = RequireOperation(operationIdentity);
			if (operation.IsSuccessful)
				return ApplyResult(CollectionAdditiveWorkflowApplyStatus.Committed, operation, null, "The additive Collection is already committed.");
			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.CheckingInstallationPlan", "Checking Collection installation plan..."), true);
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(expectedPlan))
				throw new InvalidOperationException("Approval does not refer to the exact currently reviewed Collection plan version.");

			CollectionAdditiveWorkflowReview review = GetReview(operationIdentity);
			if (!review.IsReady)
			{
				if (!review.Operation.HasCrossedNativeBoundary &&
					(review.Operation.Phase == CollectionOperationPhase.ReadyForReview ||
					 review.Operation.Phase == CollectionOperationPhase.ReadyToApply) &&
					review.Rehydration.Status != CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired)
				{
					CollectionOperation reopened = _operationCoordinator.ReopenPreparation(operationIdentity);
					return ApplyResult(CollectionAdditiveWorkflowApplyStatus.RepreparationRequired, reopened, null,
						review.Rehydration.Message);
				}
				return HandleNonResumableApplyReview(review);
			}
			if (!review.Runtime.Plan.Identity.Equals(expectedPlan))
				throw new InvalidOperationException("The reconstructed reviewed workflow does not match the exact approved plan identity.");

			operation = review.Operation;
			if (operation.Phase == CollectionOperationPhase.ReadyForReview)
			{
				operation = _operationCoordinator.MarkReadyToApply(operationIdentity, expectedPlan);
				// Approval is not a state lock. Re-capture/revalidate the exact reviewed safe boundary after approval and before mutation.
				review = GetReview(operationIdentity);
				if (!review.IsReady)
				{
					_operationCoordinator.ReopenPreparation(operationIdentity);
					return ApplyResult(CollectionAdditiveWorkflowApplyStatus.RepreparationRequired,
						RequireOperation(operationIdentity), null, review.Rehydration.Message);
				}
				operation = _operationCoordinator.BeginApplying(operationIdentity, expectedPlan);
			}
			else if (operation.Phase == CollectionOperationPhase.ReadyToApply)
				operation = _operationCoordinator.BeginApplying(operationIdentity, expectedPlan);
			else if (operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary)
				operation = _operationCoordinator.ResumeApplying(operationIdentity, expectedPlan);
			else if (operation.Phase != CollectionOperationPhase.ApplyingNativeChildren && operation.Phase != CollectionOperationPhase.Verifying)
				return ApplyResult(CollectionAdditiveWorkflowApplyStatus.RecoveryRequired, operation, null,
					"This Collection operation is not at a safe apply boundary; target recovery/reconciliation must run first.");

			CollectionReviewedWorkflowRuntime runtime = review.Runtime;
			if (operation.Phase == CollectionOperationPhase.Verifying)
			{
				CollectionAssociationFinalizationResult resumedFinalization = await VerifyAndFinalizeAppliedAssociationAsync(operationIdentity,
					runtime, targetPaths, cancellationToken).ConfigureAwait(false);
				return ApplyResult(CollectionAdditiveWorkflowApplyStatus.Committed, resumedFinalization.Operation,
					resumedFinalization, "The additive Collection was reverified and finalized from its durable verification boundary.");
			}

			CollectionNativeStateIndex currentState = runtime.CurrentState;
			int installCount = runtime.Matches.MembersByKey.Values.Count(x =>
				x.Disposition == CollectionMemberMatchDisposition.ArchiveOnlyReuse || x.Disposition == CollectionMemberMatchDisposition.ReinstallRequired);
			CollectionTargetMutationLease installSession = null;
			IDisposable memberProgress = null;
			try
			{
				if (installCount > 0 && _nativeChildApplyOverride == null && Properties.Settings.Default.CollectionSkipContentChecks)
				{
					CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(targetPaths);
					installSession = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false);
					_authorityValidator.ValidateAndReload(installSession, authority, targetPaths);
					CollectionNativeStateIndex initialState = _nativeStateReader.Capture(runtime.Plan.Target);
					if (!initialState.Fingerprint.Equals(currentState.Fingerprint))
						return ApplyResult(CollectionAdditiveWorkflowApplyStatus.RepreparationRequired, operation, null,
							"The current setup changed before Collection installation began; refresh the remaining installation review.");
					currentState = initialState;
				}
				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					int currentMember = operation.NativeChildren.Count(x => x.IsReconciled) + 1;
					memberProgress?.Dispose();
					memberProgress = currentMember <= installCount ? CollectionPreparationProgress.BeginMember(currentMember, installCount) : null;
					if (currentMember <= installCount)
						CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
							"Collections.Workflow.PreparingNextInstallation", "Preparing the next mod installation..."), true);
					CollectionNativeChildPreparationResult prepared;
					try
					{
						prepared = _childPreparationCoordinator.PrepareNext(operationIdentity,
							runtime.Plan, runtime.Matches, runtime.DependencyPlan, runtime.ImpactPlan, currentState,
							runtime.PreparedRecipes.Select(x => x.EffectPreview), runtime.VerifiedArchives, targetPaths.InstallInfoPath,
							currentMember, installCount);
					}
					catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException)
					{
						operation = _operationStore.GetOperation(operationIdentity);
						CollectionNativeChildOperation pending = operation.NativeChildren.SingleOrDefault(x => !x.IsReconciled);
						if (pending == null || pending.HasCrossedNativeBoundary) throw;
						if (!_nativeStateReader.Capture(runtime.Plan.Target).Fingerprint.Equals(currentState.Fingerprint))
							throw new InvalidOperationException("Native state changed during preparation failure; recovery is required before continuing.", failure);
						operation = _childPreparationCoordinator.RecordUnsubmittedFailure(operationIdentity, failure);
						continue;
					}
					if (prepared == null)
						break;

					PreparedCollectionNativeRecipe recipe = runtime.GetPreparedRecipe(prepared.Child.Member.MemberKey);
					if (recipe == null)
						throw new InvalidOperationException("The reviewed workflow is missing the exact prepared native recipe for its next mutating member.");
					CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
						"Collections.Workflow.InstallingMemberStatus", "Installing mod: {0}", recipe.Member.DisplayName), true);
					ModInstallationRecipeInput childRecipe = recipe.RecipeInput.ForOperationIdentity(prepared.Child.NativeOperation).WithDeferredPluginReconciliation();
					CollectionNativeChildVerificationResult verification;
					if (_nativeChildApplyOverride != null)
					{
						try
						{
							verification = await _nativeChildApplyOverride(prepared, runtime.Plan, runtime.ImpactPlan, recipe.EffectPreview,
								childRecipe, targetPaths, cancellationToken).ConfigureAwait(false);
						}
						catch
						{
							MarkRecoveryRequiredIfNeeded(operationIdentity);
							throw;
						}
					}
					else
					{
						CollectionNativeChildExecutionResult execution;
						try
						{
							execution = installSession == null
								? await _childExecutionCoordinator.SubmitPreparedChildAsync(operationIdentity, runtime.Plan, runtime.ImpactPlan,
									recipe.EffectPreview, childRecipe, targetPaths, cancellationToken).ConfigureAwait(false)
								: await _childExecutionCoordinator.SubmitPreparedChildInSessionAsync(operationIdentity, runtime.Plan, runtime.ImpactPlan,
									recipe.EffectPreview, childRecipe, targetPaths, cancellationToken, installSession, currentState).ConfigureAwait(false);
						}
						catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException)
						{
							operation = _operationStore.GetOperation(operationIdentity);
							CollectionNativeChildOperation pending = operation.NativeChildren.SingleOrDefault(x => !x.IsReconciled);
							if (pending == null || pending.HasCrossedNativeBoundary) throw;
							if (!_nativeStateReader.Capture(runtime.Plan.Target).Fingerprint.Equals(currentState.Fingerprint))
								throw new InvalidOperationException("Native state changed before a failed submission; recovery is required before continuing.", failure);
							operation = _childPreparationCoordinator.RecordUnsubmittedFailure(operationIdentity, failure);
							continue;
						}
						if (execution == null)
						{
							operation = _operationCoordinator.PauseAtSafeBoundary(operationIdentity);
							return ApplyResult(CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary, operation, null,
								"The shared native operation lane is busy; the Collection remains paused at a durable safe boundary.");
						}

						try
						{
							verification = await _childVerificationCoordinator.VerifySubmittedChildAsync(execution, runtime.Plan,
								recipe.EffectPreview, targetPaths, currentMember, installCount,
								installSession == null ? null : currentState).ConfigureAwait(false);
						}
						catch
						{
							MarkRecoveryRequiredIfNeeded(operationIdentity);
							throw;
						}
					}
					if (verification == null)
					{
						operation = _operationCoordinator.PauseAtSafeBoundary(operationIdentity);
						return ApplyResult(CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary, operation, null,
							"The shared native operation lane is busy; the Collection remains paused at a durable safe boundary.");
					}

					if (verification.RequiresRecovery)
					{
						operation = _operationCoordinator.MarkRecoveryRequired(operationIdentity);
						return ApplyResult(CollectionAdditiveWorkflowApplyStatus.RecoveryRequired, operation, null,
							"Installed-state durability is ambiguous; restart reconciliation is required before any further Collection member can run." +
							(String.IsNullOrWhiteSpace(verification.Child.NativeResult.Message) ? String.Empty :
								Environment.NewLine + verification.Child.NativeResult.Message));
					}

					CollectionAssociationReconciliationResult reconciled = _associationCoordinator.ReconcileVerifiedChild(verification, runtime.Plan);
					operation = reconciled.Operation;
					if (!verification.HasVerifiedCommit)
					{
						if (CollectionNativeChildPreparationCoordinator.IsSafeFailedAttempt(reconciled.Child))
						{
							// Verification proved that this attempt never changed, or exactly restored, its pre-child state.
							// Keep the failed result and advance to the next member instead of retrying it in this pass.
							currentState = verification.NativeState;
							continue;
						}
						operation = _operationCoordinator.MarkRecoveryRequired(operationIdentity);
						return ApplyResult(CollectionAdditiveWorkflowApplyStatus.RecoveryRequired, operation, null,
							"The native child has no confirmed committed or unchanged state. Recovery is required before further installation.");
					}

					// Reconciliation changes only Collection provenance. Reuse the verified native observation instead of
					// scanning every installed file again; submission revalidates it under the next target reservation.
					currentState = _nativeChildApplyOverride == null && reconciled.Association != null && reconciled.Binding != null
						? reconciled.ReconciledState ?? CollectionAssociationReconciliationCoordinator.BuildPostReconciliationState(
							verification.NativeState, reconciled.Association, reconciled.Binding)
						: _nativeStateReader.Capture(runtime.Plan.Target);
				}
			}
			finally
			{
				memberProgress?.Dispose();
				if (installSession != null) installSession.Dispose();
			}

			CollectionNativeChildOperation[] failedChildren = operation.NativeChildren.Where(x =>
				CollectionNativeChildPreparationCoordinator.IsSafeFailedAttempt(x)).ToArray();
			if (failedChildren.Length > 0)
			{
				operation = _operationCoordinator.CompleteStoppedPartial(operationIdentity);
				string failures = String.Join(Environment.NewLine, failedChildren.Select(x =>
				{
					ResolvedCollectionMemberPlan member = runtime.Plan.SelectedMembers.Single(m => m.MemberKey.Equals(x.Member.MemberKey));
					return member.DisplayName + ": " + (String.IsNullOrWhiteSpace(x.NativeResult.Message)
						? "This mod was not installed; its previous state was preserved." : x.NativeResult.Message);
				}));
				return ApplyResult(CollectionAdditiveWorkflowApplyStatus.StoppedPartial, operation, null,
					String.Format(CultureInfo.InvariantCulture, "The installation stage finished with {0} failed mod(s). Completed mods are kept. Review all failed mods below, then retry the remaining installation.",
						failedChildren.Length) + Environment.NewLine + failures);
			}

			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.ApplyingFileConflicts", "Applying Collection file conflict rules..."), true);
			if (_winnerReconciliationOverride != null)
				await _winnerReconciliationOverride(operationIdentity, runtime.Plan, runtime.Matches, runtime.ImpactPlan,
					targetPaths, cancellationToken).ConfigureAwait(false);
			else
				await _winnerCoordinator.ReconcileAsync(operationIdentity, runtime.Plan, runtime.Matches,
					runtime.ImpactPlan, targetPaths, cancellationToken).ConfigureAwait(false);
			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.ApplyingPluginRules", "Applying Collection plugin rules..."), true);
			await ReconcileReviewedPluginStateAsync(runtime.Plan, runtime.ImpactPlan, targetPaths, cancellationToken).ConfigureAwait(false);
			CollectionAssociationFinalizationResult finalization = await VerifyAndFinalizeAppliedAssociationAsync(operationIdentity,
				runtime, targetPaths, cancellationToken).ConfigureAwait(false);
			return ApplyResult(CollectionAdditiveWorkflowApplyStatus.Committed, finalization.Operation, finalization,
				"The exact reviewed additive Collection plan was applied, aggregate-verified and finalized.");
		}

		/// <summary>
		/// Runs target-scoped startup recovery first, reconciles any crossed native child through C6.9/C6.10, and classifies safe resumable work.
		/// </summary>
		public async Task<IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult>> ReconcileIncompleteTargetAsync(
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(targetPaths);
			List<CollectionOperation> incompleteApplyOperations = _operationStore.GetIncompleteOperations(authority.Target)
				.Where(x => x.Kind == CollectionOperationKind.ApplyResolvedPlan).ToList();
			if (incompleteApplyOperations.Count == 0)
				return new CollectionAdditiveWorkflowRecoveryResult[0];

			// Reload native authority only when an operation actually crossed (or may have crossed) the native mutation boundary.
			// A persisted ReadyForReview/ReadyToApply operation is safe pre-mutation state: startup may advertise it lazily and
			// defer retained multi-gigabyte archive verification until the user explicitly opens Review/Resume.
			if (incompleteApplyOperations.Any(RequiresStartupNativeRecovery))
				await ReloadTargetAuthorityAsync(authority, targetPaths, cancellationToken).ConfigureAwait(false);

			var results = new List<CollectionAdditiveWorkflowRecoveryResult>();
			foreach (CollectionOperation persisted in incompleteApplyOperations)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionOperation operation = RequireOperation(persisted.Identity);

				if (operation.PlanIdentity == null || operation.Revision == null || !HasReviewedSnapshot(operation))
				{
					if (!operation.HasCrossedNativeBoundary)
						operation = CompleteUnresumablePreReview(operation);
					results.Add(RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired, operation, null,
						"This pre-review operation has no durable reviewed workflow to resume; prepare the Collection again."));
					continue;
				}

				if (!RequiresStartupNativeRecovery(operation))
				{
					CollectionAdditiveWorkflowRecoveryStatus lazyStatus = operation.Phase == CollectionOperationPhase.ReadyForReview
						? CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired
						: CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume;
					results.Add(RecoveryResult(lazyStatus, operation, null,
						"A persisted reviewed workflow is available. Its retained content will be revalidated when Review/Resume is opened explicitly."));
					continue;
				}

				ResolvedCollectionPlan reviewedPlan = null;
				if (operation.HasUnreconciledNativeChild || operation.RequiresRecovery || operation.Phase == CollectionOperationPhase.Recovering)
				{
					CollectionReviewedWorkflowSnapshot snapshot = LoadReviewedSnapshot(operation);
					reviewedPlan = _runtimeReconstructor.ReconstructPlan(snapshot);
					CollectionNativeChildRestartReconciliationResult restart = await _restartCoordinator.ReconcileAsync(operation.Identity,
						targetPaths, cancellationToken).ConfigureAwait(false);
					if (restart.RequiresRecovery)
					{
						operation = RequireOperation(operation.Identity);
						results.Add(RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired, operation, null,
							"Installed-state durability is still ambiguous after restart reconciliation. " + restart.VerificationDiagnostics));
						continue;
					}

					CollectionAssociationReconciliationResult reconciled = _associationCoordinator.ReconcileRestartedChild(restart, reviewedPlan);
					operation = reconciled.Operation;
					if (!restart.HasVerifiedCommit)
					{
						operation = _operationCoordinator.CompleteStoppedPartial(operation.Identity);
						results.Add(RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.StoppedPartial, operation, null,
							"The interrupted native child is known not to have committed; the Collection stopped without replaying it."));
						continue;
					}
					operation = _operationCoordinator.ResumeApplyingAfterRecovery(operation.Identity, reviewedPlan.Identity);
				}

				CollectionReviewedWorkflowRehydrationResult rehydration = _workflowRehydrator.RehydrateForRecoveryStatus(operation.Identity);
				if (!rehydration.CanResume)
				{
					CollectionAdditiveWorkflowRecoveryStatus status = rehydration.Status == CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired
						? CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired
						: CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired;
					results.Add(RecoveryResult(status, RequireOperation(operation.Identity), rehydration, rehydration.Message));
					continue;
				}

				// Startup checks the recovery boundary. GetReview verifies inputs and constructs the executable runtime
				// when continuation is requested; doing it here would repeat that work and then discard the runtime.
				operation = RequireOperation(operation.Identity);
				CollectionAdditiveWorkflowRecoveryStatus readyStatus = operation.Phase == CollectionOperationPhase.ReadyForReview
					? CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired
					: CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume;
				results.Add(RecoveryResult(readyStatus, operation, rehydration,
					readyStatus == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired
						? "The recovered review awaits approval; installer inputs will be checked before installation."
						: "The interrupted installation is reconciled. Installer inputs will be checked when installation continues."));
			}
			return new ReadOnlyCollection<CollectionAdditiveWorkflowRecoveryResult>(results);
		}

		/// <summary>Stops only this revision's reconciled installation intent before an explicitly requested removal review.</summary>
		public async Task<IReadOnlyList<CollectionOperation>> StopReconciledInstallationForRemovalAsync(
			CollectionRevisionIdentity revision, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (revision == null) throw new ArgumentNullException(nameof(revision));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				List<CollectionOperation> pending = _operationStore.GetIncompleteOperations(authority.Target)
					.Where(x => x.Kind == CollectionOperationKind.ApplyResolvedPlan && revision.Equals(x.Revision)).ToList();
				foreach (CollectionOperation operation in pending)
					if (operation.RequiresRecovery || operation.HasUnknownNativeDurability || operation.HasUnreconciledNativeChild)
						throw new InvalidOperationException("The interrupted installation still has an unverified mod. Choose Check recovery and continue before uninstalling this Collection.");

				var stopped = new List<CollectionOperation>();
				foreach (CollectionOperation operation in pending)
					stopped.Add(operation.HasCrossedNativeBoundary
						? _operationCoordinator.CompleteStoppedPartial(operation.Identity)
						: _operationCoordinator.CompleteCancelledBeforeApply(operation.Identity));
				return new ReadOnlyCollection<CollectionOperation>(stopped);
			}
		}

		private static bool RequiresStartupNativeRecovery(CollectionOperation operation)
		{
			if (operation == null)
				return false;
			return operation.HasCrossedNativeBoundary || operation.HasUnreconciledNativeChild || operation.RequiresRecovery ||
				operation.Phase == CollectionOperationPhase.Recovering || operation.Phase == CollectionOperationPhase.RecoveryRequired;
		}

		private CollectionAdditiveWorkflowPreparationResult PrepareCore(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, CollectionArchiveOverwritePolicy archiveOverwritePolicy,
			ConfirmOverwriteCallback confirmOverwriteCallback, CollectionAlternateArchiveCandidate alternate, CancellationToken cancellationToken,
			CollectionExecutionPolicy policy)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (policy != null && policy.AutomaticallyInstall && policy.PreferIncomingCollection)
				effectiveSelection = new CollectionEffectiveSelectionBuilder().Build(effectiveSelection.CapabilityReport,
					Enumerable.Empty<CollectionOptionalMemberSelection>(), true);
			CollectionAdditivePlanBuildResult planBuild = _planPreparationService.Prepare(effectiveSelection, targetPaths, alternate, policy);
			CollectionMemberAcquisitionBatch acquisition = _memberAcquisitionCoordinator.Begin(planBuild,
				archiveOverwritePolicy, confirmOverwriteCallback, cancellationToken);
			return ContinuePreparation(acquisition, targetPaths, cancellationToken);
		}

		private CollectionAdditiveWorkflowPreparationResult ResumePreparationCore(CollectionMemberAcquisitionBatch acquisition,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (!acquisition.IsAwaitingInput)
				throw new InvalidOperationException("Only an AwaitingInput additive preparation can be resumed.");
			cancellationToken.ThrowIfCancellationRequested();
			CollectionMemberAcquisitionBatch refreshed = acquisition.IsReady
				? acquisition
				: _memberAcquisitionCoordinator.ProbeCompletedInput(acquisition, cancellationToken);
			return ContinuePreparation(refreshed, targetPaths, cancellationToken);
		}

		private CollectionAdditiveWorkflowPreparationResult ContinuePreparation(CollectionMemberAcquisitionBatch acquisition,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (acquisition.HasBlockedMembers)
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.Blocked, acquisition, null, null, null,
					"At least one selected Collection member cannot be matched/acquired safely for the current target.");
			if (!acquisition.IsReady)
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.AwaitingInput, acquisition, null, null, null,
					"One or more selected members still require Premium acquisition completion or explicit manual/free input.");

			if (acquisition.IsAwaitingInput)
				acquisition = _planRevalidationService.RevalidateAfterPause(acquisition, targetPaths, cancellationToken).AcquisitionBatch;
			return FinalizePreparation(acquisition, targetPaths, cancellationToken);
		}

		private CollectionAdditiveWorkflowPreparationResult FinalizePreparation(CollectionMemberAcquisitionBatch acquisition,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken,
			IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> reusableRecipes = null)
		{
			CollectionAdditivePlanBuildResult planBuild = acquisition.PlanBuild;
			ResolvedCollectionPlan plan = planBuild.Plan;
			CollectionMemberMatchSet matches = acquisition.MatchSet;
			CollectionDependencyPhasePlan dependencyPlan = _dependencyPlanner.Plan(plan, matches);
			if (!dependencyPlan.IsReady)
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.Blocked, acquisition, dependencyPlan, null, null,
					"The selected Collection dependency/phase graph is blocked.");

			List<PreparedCollectionNativeRecipe> recipes;
			var preparationFailures = new List<CollectionMemberPreparationException>();
			var pluginDependencies = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
			try
			{
				recipes = PrepareNativeRecipes(acquisition, dependencyPlan, targetPaths, cancellationToken, pluginDependencies, reusableRecipes, preparationFailures);
			}
			catch (CollectionMemberPreparationException ex)
			{
				CollectionAdditiveWorkflowPreparationStatus status = ex.InnerException is InvalidOperationException
					? CollectionAdditiveWorkflowPreparationStatus.PreparationRequired : CollectionAdditiveWorkflowPreparationStatus.Blocked;
				return PreparationResult(status, acquisition, dependencyPlan, null, null, ex.Message, ex.Member, ex.InnerException.Message);
			}
			catch (NotSupportedException ex)
			{
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.Blocked, acquisition, dependencyPlan, null, null, ex.Message);
			}
			catch (InvalidOperationException ex)
			{
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.PreparationRequired, acquisition, dependencyPlan, null, null, ex.Message);
			}
			catch (InvalidDataException ex)
			{
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.Blocked, acquisition, dependencyPlan, null, null, ex.Message);
			}

			if (preparationFailures.Count > 0)
			{
				CollectionAdditiveWorkflowPreparationStatus status = preparationFailures.Any(x => x.Status == CollectionAdditiveWorkflowPreparationStatus.Blocked)
					? CollectionAdditiveWorkflowPreparationStatus.Blocked : CollectionAdditiveWorkflowPreparationStatus.PreparationRequired;
				return new CollectionAdditiveWorkflowPreparationResult(status, planBuild.Operation, acquisition, dependencyPlan,
					null, null, String.Join(Environment.NewLine, preparationFailures.Select(x => x.Message)), null,
					preparationFailures: preparationFailures);
			}

			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.CheckingInstallationPlan", "Checking Collection installation plan..."), true);
			HashSet<string> requiredPlugins = null;
			if (plan.Policy.AutomaticallyInstall && plan.Policy.PreferIncomingCollection)
			{
				HashSet<string> providedPlugins;
				requiredPlugins = _automaticDependencies.GetRequiredPlugins(plan, matches, planBuild.NativeState, pluginDependencies,
					recipes.SelectMany(x => x.EffectPreview.PluginEffects)
						.Where(x => x.Kind == CollectionPlannedPluginEffectKind.Activation && x.Active == true)
						.Select(x => Path.GetFileName(x.PluginPaths[0])), out providedPlugins);
				var missingProviders = new HashSet<string>(requiredPlugins, StringComparer.OrdinalIgnoreCase);
				missingProviders.ExceptWith(providedPlugins);
				missingProviders.ExceptWith(planBuild.NativeState.Plugins.Values.Where(x => x.Active && File.Exists(x.FileName)).Select(x => Path.GetFileName(x.FileName)));
				CollectionEffectiveSelection expanded = _automaticDependencies.IncludePluginProviders(plan.CapabilityReport,
					missingProviders, _services.ModManager, cancellationToken);
				if (expanded.Manifest.Members.Count(x => x.IsSelected) == plan.SelectedMembers.Count && missingProviders.Count > 0)
					expanded = _automaticDependencies.IncludePluginProviders(plan.CapabilityReport, missingProviders,
						_services.ModManager, cancellationToken, true);
				if (expanded.Manifest.Members.Count(x => x.IsSelected) > plan.SelectedMembers.Count)
				{
					CollectionAdditivePlanBuildResult expandedPlan = _planPreparationService.ExpandPrerequisites(planBuild, expanded);
					CollectionMemberAcquisitionBatch expandedAcquisition = _memberAcquisitionCoordinator.BeginExpanded(expandedPlan, acquisition, cancellationToken);
					return expandedAcquisition.IsReady
						? FinalizePreparation(expandedAcquisition, targetPaths, cancellationToken, recipes.ToDictionary(x => x.Member.MemberKey))
						: ContinuePreparation(expandedAcquisition, targetPaths, cancellationToken);
				}
			}
			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.CheckingFileConflicts", "Checking mod file conflicts..."), true);
			IEnumerable<CollectionConflictResolutionDecision> conflictDecisions = _conflictResolutionCoordinator == null
				? Enumerable.Empty<CollectionConflictResolutionDecision>()
				: _conflictResolutionCoordinator.GetDecisions(plan);
			CollectionConflictImpactPlan impactPlan = requiredPlugins == null
				? _impactPlanner.Plan(plan, matches, dependencyPlan, planBuild.NativeState, recipes.Select(x => x.EffectPreview), conflictDecisions)
				: _impactPlanner.PlanAutomaticPrerequisites(plan, matches, dependencyPlan, planBuild.NativeState,
					recipes.Select(x => x.EffectPreview), conflictDecisions, requiredPlugins);
			if (!impactPlan.IsReady)
			{
				CollectionAdditiveWorkflowPreparationStatus status = impactPlan.Status == CollectionConflictImpactStatus.ActionRequired
					? CollectionAdditiveWorkflowPreparationStatus.ActionRequired
					: impactPlan.Status == CollectionConflictImpactStatus.PreparationRequired
						? CollectionAdditiveWorkflowPreparationStatus.PreparationRequired
						: CollectionAdditiveWorkflowPreparationStatus.Blocked;
				// Keep only conflict-only recipe previews in this in-process result. Exact saved file
				// decisions can then refresh impact planning without a second multi-gigabyte extraction.
				if (status == CollectionAdditiveWorkflowPreparationStatus.ActionRequired && impactPlan.Issues.Count > 0 &&
					impactPlan.Issues.All(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired))
					return new CollectionAdditiveWorkflowPreparationResult(status, planBuild.Operation, acquisition,
						dependencyPlan, impactPlan, null, impactPlan.Issues[0].Message, recipes);
				return PreparationResult(status, acquisition, dependencyPlan, impactPlan, null,
					impactPlan.Issues.Count == 0 ? "The Collection impact plan is not executable." : impactPlan.Issues[0].Message);
			}

			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.SavingInstallationPlan", "Saving Collection installation plan..."), true);
			CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshot.Create(
				plan, dependencyPlan, impactPlan, recipes);
			CollectionOperation ready = _operationCoordinator.MarkReadyForReview(planBuild.Operation.Identity,
				plan, dependencyPlan, impactPlan, recipes);

			// The exact review has just been built from the live C6.1 state and exact verified acquisition results in this
			// process. Do not immediately round-trip through restart rehydration: that path intentionally performs full
			// retained-artifact SHA verification and turns Download/Prepare back into an O(total archive bytes) operation.
			// GetReview/Approve still rehydrate and cryptographically verify durable inputs before any native mutation.
			List<CollectionVerifiedArchive> verifiedArchives = acquisition.Members
				.Where(x => x.VerifiedArchive != null)
				.Select(x => x.VerifiedArchive)
				.ToList();
			CollectionReviewedWorkflowRuntime runtime = new CollectionReviewedWorkflowRuntime(snapshot, plan, matches,
				dependencyPlan, impactPlan, planBuild.NativeState, snapshot.Members.Select(x => x.MemberKey),
				recipes, verifiedArchives);
			return new CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview,
				ready, acquisition, dependencyPlan, impactPlan, runtime,
				"The exact additive Collection review is ready for explicit approval.");
		}

		private List<PreparedCollectionNativeRecipe> PrepareNativeRecipes(CollectionMemberAcquisitionBatch acquisition,
			CollectionDependencyPhasePlan dependencyPlan, GameStoragePathSet targetPaths, CancellationToken cancellationToken,
			IDictionary<string, IList<string>> pluginDependencies, IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> reusableRecipes,
			IList<CollectionMemberPreparationException> preparationFailures)
		{
			if (dependencyPlan == null) throw new ArgumentNullException(nameof(dependencyPlan));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			ResolvedCollectionPlan plan = acquisition.PlanBuild.Plan;
			var retainedArtifactStore = new CollectionsRetainedArtifactStore(new CollectionsStore(targetPaths));
			CollectionNativeStateIndex state = acquisition.PlanBuild.NativeState;
			var inputs = new List<NativeRecipePreparationInput>();
			int informationIndex = 0;
			int informationCount = dependencyPlan.Phases.SelectMany(x => x.Members)
				.Count(x => x.Match.Disposition != CollectionMemberMatchDisposition.InstalledCompatible);
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
			{
				foreach (CollectionPlannedPhaseMember planned in phase.Members)
				{
					cancellationToken.ThrowIfCancellationRequested();
					CollectionMemberMatchResult match = planned.Match;
					if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
						continue;
					IDisposable informationProgress = CollectionPreparationProgress.BeginMember(++informationIndex, informationCount);
					try
					{
						if (match.Disposition != CollectionMemberMatchDisposition.ArchiveOnlyReuse &&
							match.Disposition != CollectionMemberMatchDisposition.ReinstallRequired)
							throw new InvalidOperationException("A selected Collection member reached installation preparation without an executable member-matching disposition.");

						CollectionMemberAcquisitionState acquisitionState = acquisition.Members.Single(x => x.Match.Member.MemberKey.Equals(match.Member.MemberKey));
						CollectionVerifiedArchive archive = acquisitionState.VerifiedArchive ?? match.VerifiedArchive;
						if (archive == null)
							throw new InvalidOperationException("A mutating Collection member does not have its exact verified immutable archive.");
						CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
							"Collections.Workflow.ReadingMemberInformation", "Reading mod information: {0}", match.Member.DisplayName), true);
						IMod managedMod = ResolveManagedMod(match, archive, retainedArtifactStore, cancellationToken, plan.Policy.AutomaticallyInstall);
						CollectionNativeRecipePreparer.ValidateInstallerRouting(match.Member, managedMod);
						CollectionInstallDestination destination = CollectionInstallDestinationResolver.Resolve(_services.ModManager.GameMode,
							match.Member, managedMod, ResolveInstallContext(match), cancellationToken);
						bool contributesMerge = !match.Member.HasVortexFileList && !match.Member.HasVortexFomodSelection && !managedMod.HasInstallScript &&
							CollectionNativeRecipePreparer.IsDeterministicModFileMergeContributor(_services.ModManager.GameMode, managedMod);
						inputs.Add(new NativeRecipePreparationInput(match, archive, managedMod, destination, contributesMerge));
					}
					catch (Exception ex) when (IsMemberPreparationFailure(ex))
					{
						RecordMemberPreparationFailure(preparationFailures, match.Member, ex, "Reading mod information");
					}
					finally
					{
						informationProgress.Dispose();
					}
				}
			}

			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.PreparingMods", "Preparing mods..."), true);
			NativeRecipePreparationInput mergeOwner = inputs.LastOrDefault(x => x.ContributesDeterministicMerge);
			List<IMod> projectedMergeBaseline = _services.ModManager.ActiveMods.ToList();
			foreach (NativeRecipePreparationInput input in inputs.Where(x => x.ContributesDeterministicMerge))
				ProjectActiveMod(projectedMergeBaseline, input.ManagedMod);
			try
			{
				ValidateProjectedMergeTarget(inputs, projectedMergeBaseline, _services.ModManager.GameMode);
			}
			catch (Exception ex) when (IsMemberPreparationFailure(ex))
			{
				foreach (NativeRecipePreparationInput input in inputs.Where(x => x.ContributesDeterministicMerge))
					RecordMemberPreparationFailure(preparationFailures, input.Match.Member, ex, "Preparing shared merge output");
			}

			// Recipe preparation now plans from metadata rather than hashing every payload. Validate
			// FOMOD choices in that same pass; a separate early preflight would repeat installer parsing.
			var recipes = new List<PreparedCollectionNativeRecipe>();
			Dictionary<string, PreparedCollectionNativeRecipe> completedRecipes = GetPreparationCache(plan);
			int recipeIndex = 0;
			foreach (NativeRecipePreparationInput input in inputs)
			{
				cancellationToken.ThrowIfCancellationRequested();
				recipeIndex++;
				if (preparationFailures.Any(x => x.Member.MemberKey.Equals(input.Match.Member.MemberKey)))
					continue;
				IDisposable recipeProgress = CollectionPreparationProgress.BeginMember(recipeIndex, inputs.Count);
				try
				{
					bool includeMergeOutput = !input.ContributesDeterministicMerge || ReferenceEquals(input, mergeOwner);
					IList<IMod> activeMods = input.ContributesDeterministicMerge
						? projectedMergeBaseline
						: _services.ModManager.ActiveMods.ToList();
					bool skipReadme = _services.ModManager.EnvironmentInfo.Settings.SkipReadmeFiles;
					PreparedCollectionNativeRecipe prepared;
					string cacheKey = input.ContributesDeterministicMerge
						? null : GetPreparationCacheKey(input, skipReadme);
					bool cached = false;
					prepared = null;
					if (cacheKey != null)
						lock (_preparationCacheLock) cached = completedRecipes.TryGetValue(cacheKey, out prepared);
					CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Format(
						cached ? "Collections.Workflow.ReusingMemberInstallationStatus" : "Collections.Workflow.PlanningMemberInstallationStatus",
						cached ? "Reusing prepared mod: {0}" : "Preparing installation instructions: {0}", input.Match.Member.DisplayName), !cached);
					// Dependency-only expansion keeps the captured target and exact existing artifacts unchanged.
					// Reuse ordinary file previews; merge composition and installer conditions still require fresh planning.
					if (!cached && (reusableRecipes == null || input.ContributesDeterministicMerge || input.Match.Member.HasVortexFomodSelection || input.ManagedMod.HasInstallScript ||
						!reusableRecipes.TryGetValue(input.Match.Member.MemberKey, out prepared) ||
						prepared.InstallContext.Method != input.InstallContext.Method || prepared.InstallContext.InstallRoot != input.InstallContext.InstallRoot))
						prepared = _nativeRecipePreparer.PrepareExact(plan, input.Match.Member, input.Archive, input.ManagedMod,
							_services.ModManager.GameMode, _services.ModManager.EnvironmentInfo, input.InstallContext, state, skipReadme,
							_services.PluginManager, activeMods, includeMergeOutput, cancellationToken, input.Destination.GameRootArchiveBaseDirectory,
							!input.Match.Member.HasVortexFileList && !input.Match.Member.HasVortexFomodSelection && input.ManagedMod.HasInstallScript
								? new CollectionInstallerConditionEnvironment(state, _services.ModManager.GameMode, plan.CapabilityReport.Manifest, recipes, plan.Policy.EnableUnspecifiedPlugins)
								: null);
					// The manifest is retained by every normal recipe. Generated inputs have a separate retention lifecycle.
					if (cacheKey != null && CanCachePreparedArtifacts(prepared.RetainedArtifactIds,
						input.Archive.Artifact.ArtifactId, "sha256:" + plan.ManifestSource.ContentHash.Value) &&
						StringComparer.Ordinal.Equals(cacheKey, GetPreparationCacheKey(input, skipReadme)))
						lock (_preparationCacheLock)
						{
							if (completedRecipes.Count >= 2048) completedRecipes.Clear();
							completedRecipes[cacheKey] = prepared;
						}
					recipes.Add(CollectionInstallRootCorrection.Prepare(prepared, state, input.Match.MatchedNativeMod,
						_services.ModManager.DeploymentManager, _services.ModManager.VirtualModActivator, cancellationToken));
					if (plan.Policy.AutomaticallyInstall && plan.Policy.PreferIncomingCollection)
						_automaticDependencies.AddRecipeDependencies(prepared, input.ManagedMod, _services.ModManager.GameMode,
							pluginDependencies, cancellationToken);
				}
				catch (Exception ex) when (IsMemberPreparationFailure(ex))
				{
					string detail = String.Format(CultureInfo.InvariantCulture,
						"Collection member '{0}' ({1}), artifact {2}, retained archive {3} ({4} bytes), native archive '{5}': {6}",
						input.Match.Member.DisplayName, input.Match.Member.MemberKey, input.Match.Member.ArtifactChoice.SelectedArtifact,
						input.Archive.Artifact.ArtifactId, input.Archive.Artifact.ByteLength,
						CollectionArchiveContentMatcher.GetManagedArchivePath(input.ManagedMod) ?? "unresolved", ex.Message);
					RecordMemberPreparationFailure(preparationFailures, input.Match.Member, ex, "Preparing mod files", detail);
				}
				finally
				{
					recipeProgress.Dispose();
				}
			}
			return recipes;
		}

		/// <summary>Admits the normal archive and manifest pair while excluding generated inputs with a separate retention lifecycle.</summary>
		internal static bool CanCachePreparedArtifacts(IReadOnlyList<string> retainedIds, string archiveId, string manifestId)
		{
			return retainedIds.Contains(archiveId, StringComparer.Ordinal) && retainedIds.All(id =>
				StringComparer.Ordinal.Equals(id, archiveId) || StringComparer.Ordinal.Equals(id, manifestId));
		}

		/// <summary>Retains successful file and FOMOD preparations across retries only while the complete planning context is unchanged.</summary>
		private Dictionary<string, PreparedCollectionNativeRecipe> GetPreparationCache(ResolvedCollectionPlan plan)
		{
			lock (_preparationCacheLock)
			{
				if (_preparationCachePlan == null || !_preparationCachePlan.Target.Equals(plan.Target) ||
					!_preparationCachePlan.Revision.Equals(plan.Revision) || !_preparationCachePlan.ManifestSource.Equals(plan.ManifestSource) ||
					!_preparationCachePlan.CurrentStateFingerprint.Equals(plan.CurrentStateFingerprint) || !_preparationCachePlan.Policy.Equals(plan.Policy) ||
					!_preparationCachePlan.SelectedMembers.Select(x => x.MemberKey).SequenceEqual(plan.SelectedMembers.Select(x => x.MemberKey)))
				{
					_preparationCachePlan = plan;
					_completedRecipeCache = new Dictionary<string, PreparedCollectionNativeRecipe>(StringComparer.Ordinal);
				}
				return _completedRecipeCache;
			}
		}

		/// <summary>Binds a reusable preparation to its exact archive, destination, recipe, and readme preference without reading payloads.</summary>
		private static string GetPreparationCacheKey(NativeRecipePreparationInput input, bool skipReadme)
		{
			var file = new FileInfo(CollectionArchiveContentMatcher.GetManagedArchivePath(input.ManagedMod));
			return String.Join("\0", input.Match.Member.MemberKey.ToString(), input.Match.Member.RecipeIdentity.Fingerprint,
				input.Archive.Artifact.ArtifactId, file.FullName, file.Length.ToString(CultureInfo.InvariantCulture),
				file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), input.ManagedMod.GetType().FullName,
				input.InstallContext.Method.ToString(), input.InstallContext.InstallRoot.ToString(), skipReadme.ToString(), Properties.Settings.Default.CollectionSkipContentChecks.ToString(),
				input.Destination.GameRootArchiveBaseDirectory ?? String.Empty);
		}

		/// <summary>Continues past member-local errors while allowing cancellation and unexpected infrastructure failures to propagate.</summary>
		private static bool IsMemberPreparationFailure(Exception exception)
		{
			return exception is NotSupportedException || exception is InvalidOperationException || exception is InvalidDataException || exception is IOException ||
				exception is UnauthorizedAccessException || exception is ArgumentException || exception is System.Xml.XmlException ||
				exception is System.Xml.Schema.XmlSchemaException || exception is ModFormatException || exception is SevenZip.SevenZipException;
		}

		/// <summary>Keeps one actionable diagnostic per failed member while checking the rest of the selected Collection.</summary>
		private static void RecordMemberPreparationFailure(IList<CollectionMemberPreparationException> failures,
			ResolvedCollectionMemberPlan member, Exception cause, string stage, string detail = null)
		{
			if (failures.Any(x => x.Member.MemberKey.Equals(member.MemberKey))) return;
			detail = detail ?? String.Format(CultureInfo.InvariantCulture, "Collection member '{0}' ({1}): {2}. {3}",
				member.DisplayName, member.MemberKey, stage, cause.Message);
			failures.Add(new CollectionMemberPreparationException(member, detail, cause));
		}

		private static void ValidateProjectedMergeTarget(IList<NativeRecipePreparationInput> inputs, IList<IMod> projectedActiveMods, IGameMode gameMode)
		{
			List<NativeRecipePreparationInput> contributors = inputs.Where(x => x.ContributesDeterministicMerge).ToList();
			if (contributors.Count <= 1) return;
			var provider = gameMode as IDeterministicModFileMergePlanProvider;
			if (provider == null)
				throw new NotSupportedException("Multi-member merge composition requires the active game mode's deterministic merge planner.");

			ModDeploymentTarget sharedTarget = null;
			foreach (NativeRecipePreparationInput contributor in contributors)
			{
				DeterministicModFileMergePlan mergePlan;
				try
				{
					mergePlan = provider.GetDeterministicModFileMergePlan(projectedActiveMods, contributor.ManagedMod);
				}
				catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
				{
					throw new NotSupportedException("Projected multi-member merge composition could not reproduce the deterministic shared output: " + ex.Message, ex);
				}
				if (mergePlan == null)
					throw new NotSupportedException("A projected merge contributor no longer produces the deterministic shared merge output.");
				ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(gameMode, contributor.ManagedMod,
					mergePlan.DestinationPath, contributor.InstallContext.InstallRoot);
				if (sharedTarget == null) sharedTarget = target;
				else if (!sharedTarget.Equals(target))
					throw new NotSupportedException("Projected merge contributors resolve to different native targets; one reviewed aggregate owner cannot represent that merge safely.");
			}
		}

		private static void ProjectActiveMod(IList<IMod> activeMods, IMod mod)
		{
			if (activeMods == null) throw new ArgumentNullException(nameof(activeMods));
			if (mod == null) throw new ArgumentNullException(nameof(mod));
			for (int index = activeMods.Count - 1; index >= 0; index--)
			{
				IMod existing = activeMods[index];
				if (existing != null && (ReferenceEquals(existing, mod) ||
					StringComparer.OrdinalIgnoreCase.Equals(existing.Filename, mod.Filename)))
					activeMods.RemoveAt(index);
			}
			activeMods.Add(mod);
		}

		private sealed class NativeRecipePreparationInput
		{
			public NativeRecipePreparationInput(CollectionMemberMatchResult match, CollectionVerifiedArchive archive,
				IMod managedMod, CollectionInstallDestination destination, bool contributesDeterministicMerge)
			{
				Match = match ?? throw new ArgumentNullException(nameof(match));
				Archive = archive ?? throw new ArgumentNullException(nameof(archive));
				ManagedMod = managedMod ?? throw new ArgumentNullException(nameof(managedMod));
				Destination = destination ?? throw new ArgumentNullException(nameof(destination));
				ContributesDeterministicMerge = contributesDeterministicMerge;
			}

			public CollectionMemberMatchResult Match { get; }
			public CollectionVerifiedArchive Archive { get; }
			public IMod ManagedMod { get; }
			public CollectionInstallDestination Destination { get; }
			public ModInstallContext InstallContext { get { return Destination.InstallContext; } }
			public bool ContributesDeterministicMerge { get; }
		}

		private ModInstallContext ResolveInstallContext(CollectionMemberMatchResult match)
		{
			if (match == null)
				throw new ArgumentNullException(nameof(match));

			if (!match.Member.RequiresGameRootInstall)
			{
				return match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired
					? new ModInstallContext(match.MatchedNativeMod.InstallMethod, match.MatchedNativeMod.InstallRoot)
					: _services.ModManager.CapturePreferredInstallContext(ModInstallRoot.Default);
			}

			if (!_services.ModManager.GameMode.SupportsGameRootModInstall)
			{
				throw new NotSupportedException(String.Format(
					"Collection member '{0}' requires NMM's native game-root installation mode, but the current game mode does not support game-root installs.",
					match.Member.DisplayName ?? match.Member.MemberKey.Value));
			}

			if (match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired)
			{
				if (match.MatchedNativeMod == null)
					throw new InvalidOperationException("A reviewed game-root reinstall does not identify exactly one current native mod instance.");
				return new ModInstallContext(match.MatchedNativeMod.InstallMethod, ModInstallRoot.GameRoot);
			}

			return _services.ModManager.CapturePreferredInstallContext(ModInstallRoot.GameRoot);
		}

		private IMod ResolveManagedMod(CollectionMemberMatchResult match, CollectionVerifiedArchive archive,
			CollectionsRetainedArtifactStore retainedArtifactStore, CancellationToken cancellationToken, bool automaticallySelectArchive)
		{
			if (archive == null) throw new ArgumentNullException(nameof(archive));
			if (retainedArtifactStore == null) throw new ArgumentNullException(nameof(retainedArtifactStore));
			IMod previous = null;
			if (match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired)
			{
				CollectionNativeModState previousState = match.MatchedNativeMod;
				if (previousState == null)
					throw new InvalidOperationException("A reviewed reinstall does not identify exactly one current native mod instance.");
				List<IMod> previousMatches = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
					StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), previousState.Identity.NativeModKey)).ToList();
				if (previousMatches.Count != 1)
					throw new InvalidOperationException("The reviewed reinstall target cannot be resolved unambiguously from the current InstallLog.");
				previous = previousMatches[0];
			}

			string memberName = match.Member.DisplayName ?? match.Member.MemberKey.Value;
			List<IMod> trustedCandidates = ModManagerCollectionManagedArchiveSource.FindTrustedCachedManagedMods(_services.ModManager, retainedArtifactStore, archive);
			if (previous != null && trustedCandidates.Contains(previous)) return previous;
			if (trustedCandidates.Count == 1) return trustedCandidates[0];
			if (automaticallySelectArchive && trustedCandidates.Count > 1)
				return ModManagerCollectionManagedArchiveSource.SelectDeterministicEquivalentManagedMod(trustedCandidates);
			if (trustedCandidates.Count > 1)
				throw new InvalidOperationException("Multiple native managed archives match the trusted Collection archive identity and saved file stamp.");
			if (previous != null && CollectionArchiveContentMatcher.MatchesFile(
				CollectionArchiveContentMatcher.GetManagedArchivePath(previous), archive.Artifact, cancellationToken))
				return previous;

			string externalMd5; long externalByteLength;
			if (CollectionBundledArtifactIdentity.IsBundle(match.Member.ArtifactChoice.SelectedArtifact) ||
				CollectionExternalArtifactIdentity.TryParse(match.Member.ArtifactChoice.SelectedArtifact, out externalMd5, out externalByteLength))
			{
				List<IMod> exactManaged = CollectionArchiveContentMatcher.FindExactManagedMods(_services.ModManager, archive.Artifact, cancellationToken);
				if (exactManaged.Count == 1) return exactManaged[0];
				if (automaticallySelectArchive && exactManaged.Count > 1)
					return ModManagerCollectionManagedArchiveSource.SelectDeterministicEquivalentManagedMod(exactManaged);
				if (exactManaged.Count > 1)
					throw new InvalidOperationException("Multiple native managed archives contain the exact materialized Collection bundle bytes; preparation cannot choose by filename or timing.");
				IMod repaired = ModManagerCollectionManagedArchiveSource.TryRegisterExactLibraryArchive(
					_services.ModManager, archive.Artifact, cancellationToken);
				if (repaired != null) return repaired;
				repaired = ModManagerCollectionManagedArchiveSource.MaterializeAndRegisterRetainedArchive(
					_services.ModManager, retainedArtifactStore, archive.Artifact, memberName, cancellationToken);
				if (repaired != null) return repaired;
				throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
					"The exact materialized archive for Collection member '{0}' is verified but could not be registered in the native mod library.", memberName));
			}

			string domain; long nexusModId; long nexusFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(match.Member.ArtifactChoice.SelectedArtifact, out domain, out nexusModId, out nexusFileId))
				throw new NotSupportedException("The selected Collection artifact scheme has no characterized native recipe input mapping.");
			string currentDomain = _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(domain, currentDomain))
				throw new InvalidOperationException("The active native repository game does not match the selected Collection artifact domain.");

			string modId = nexusModId.ToString(CultureInfo.InvariantCulture);
			string fileId = nexusFileId.ToString(CultureInfo.InvariantCulture);
			List<IMod> identityCandidates = _services.ModManager.ManagedMods.Where(x =>
				ModManagerCollectionManagedArchiveSource.MatchesRepositoryFileIdentity(x, _services.ModManager.SortOrderService, modId, fileId)).ToList();
			List<IMod> exactIdentityCandidates = identityCandidates.Where(x => CollectionArchiveContentMatcher.MatchesFile(
				CollectionArchiveContentMatcher.GetManagedArchivePath(x), archive.Artifact, cancellationToken)).ToList();
			if (exactIdentityCandidates.Count == 1 || (automaticallySelectArchive && exactIdentityCandidates.Count > 1))
			{
				IMod selected = ModManagerCollectionManagedArchiveSource.SelectDeterministicEquivalentManagedMod(exactIdentityCandidates);
				ModManagerCollectionManagedArchiveSource.ConfirmVerifiedArchiveRepositoryFileIdentity(_services.ModManager,
					selected, modId, fileId, archive.Artifact.ByteLength, archive.Artifact.ContentHash, cancellationToken);
				return selected;
			}
			if (exactIdentityCandidates.Count > 1)
				throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
					"Multiple native managed archives for Collection member '{0}' match both Nexus identity {1}/{2} and the exact verified bytes.", memberName, modId, fileId));

			List<IMod> exactContentCandidates = _services.ModManager.ManagedMods.Where(x =>
				ModManagerCollectionManagedArchiveSource.IsMetadataCompatibleForVerifiedContent(x, modId, fileId) &&
				CollectionArchiveContentMatcher.MatchesFile(CollectionArchiveContentMatcher.GetManagedArchivePath(x), archive.Artifact, cancellationToken)).ToList();
			if (exactContentCandidates.Count != 0)
			{
				IMod recovered = ModManagerCollectionManagedArchiveSource.SelectDeterministicEquivalentManagedMod(exactContentCandidates);
				ModManagerCollectionManagedArchiveSource.ConfirmVerifiedArchiveRepositoryFileIdentity(_services.ModManager,
					recovered, modId, fileId, archive.Artifact.ByteLength, archive.Artifact.ContentHash, cancellationToken);
				return recovered;
			}

			IMod registeredFromLibrary = ModManagerCollectionManagedArchiveSource.TryRegisterExactLibraryArchive(
				_services.ModManager, archive.Artifact, cancellationToken);
			if (registeredFromLibrary == null)
			{
				registeredFromLibrary = ModManagerCollectionManagedArchiveSource.MaterializeAndRegisterRetainedArchive(
					_services.ModManager, retainedArtifactStore, archive.Artifact, memberName, cancellationToken);
			}
			if (registeredFromLibrary != null)
			{
				ModManagerCollectionManagedArchiveSource.ConfirmVerifiedArchiveRepositoryFileIdentity(_services.ModManager,
					registeredFromLibrary, modId, fileId, archive.Artifact.ByteLength, archive.Artifact.ContentHash, cancellationToken);
				return registeredFromLibrary;
			}

			string reason = identityCandidates.Count == 0
				? "no native managed archive is registered for those exact bytes"
				: "the native managed entries claiming that Nexus identity do not contain the verified Collection bytes";
			throw new InvalidOperationException(String.Format(CultureInfo.InvariantCulture,
				"Collection member '{0}' ({1}/{2}) cannot be prepared because {3}. The exact retained archive could not be materialized and registered in the NMM mod library.",
				memberName, modId, fileId, reason));
		}


		private async Task ReconcileReviewedPluginStateAsync(ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			List<CollectionPluginImpact> reviewedEffects = impactPlan.PluginImpacts.Where(x =>
				x.Effect.Kind == CollectionPlannedPluginEffectKind.Activation || x.Effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder).ToList();
			if (reviewedEffects.Count == 0) return;
			if (_services.PluginManager == null)
				throw new InvalidOperationException("The reviewed Collection requires final plugin-state/order reconciliation, but the native plugin manager is unavailable.");

			var requested = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionPluginImpact impact in reviewedEffects.Where(x => x.Effect.Kind == CollectionPlannedPluginEffectKind.Activation))
			{
				string path = impact.Effect.PluginPaths[0];
				bool active = impact.Effect.Active.Value;
				bool prior;
				if (requested.TryGetValue(path, out prior) && prior != active)
					throw new InvalidDataException("The reviewed Collection contains contradictory final plugin activation requests.");
				requested[path] = active;
			}

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(targetPaths);
			if (!authority.Target.Equals(plan.Target))
				throw new InvalidOperationException("The live canonical target changed before reviewed Collection plugin-state reconciliation.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, targetPaths);
				CollectionNativeStateIndex currentState = _nativeStateReader.Capture(plan.Target);
				List<CollectionPlannedPluginEffect> effectiveEffects = CollectionPluginActivationResolver.Resolve(plan.Policy,
					plan.CapabilityReport.Manifest, currentState, reviewedEffects.Select(x => x.Effect));
				requested = effectiveEffects.Where(x => x.Kind == CollectionPlannedPluginEffectKind.Activation)
					.GroupBy(x => x.PluginPaths[0], StringComparer.OrdinalIgnoreCase)
					.ToDictionary(x => x.Key, x => x.First().Active.Value, StringComparer.OrdinalIgnoreCase);
				if (effectiveEffects.All(x => CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(currentState, x))) return;

				Nexus.Client.PluginManagement.ICollectionPluginStateManager collectionPluginManager =
					_services.PluginManager as Nexus.Client.PluginManagement.ICollectionPluginStateManager;
				if (collectionPluginManager != null)
				{
					IList<Nexus.Client.PluginManagement.PluginValidationDiagnostic> advisoryDiagnostics;
					List<IList<string>> relativeOrder = effectiveEffects
						.Where(x => x.Kind == CollectionPlannedPluginEffectKind.RelativeOrder)
						.Select(x => (IList<string>)x.PluginPaths.ToList()).ToList();
					if (!collectionPluginManager.TryReconcileCollectionPluginState(requested.Keys.ToList(), requested, relativeOrder, out advisoryDiagnostics))
						throw new InvalidOperationException("The native plugin service could not apply the Collection's requested plugin state/order." +
							(advisoryDiagnostics == null ? String.Empty : " " + String.Join("; ", advisoryDiagnostics.Where(x => x != null).Select(x => x.Message).Distinct())));
				}
				else
				{
					if (requested.Count > 0)
					{
						IList<Nexus.Client.PluginManagement.PluginValidationDiagnostic> diagnostics;
						if (!_services.PluginManager.TryReconcileDeployedPlugins(requested.Keys.ToList(), requested, out diagnostics))
						{
							string detail = diagnostics == null || diagnostics.Count == 0
								? "The native plugin policy could not honor every reviewed Collection activation request."
								: "The native plugin policy rejected the reviewed Collection activation state: " + String.Join(", ", diagnostics.Where(x => x != null).Select(x => String.IsNullOrWhiteSpace(x.Message) ? x.Kind.ToString() : x.Message).Distinct());
							throw new InvalidOperationException(detail);
						}
					}

					CollectionPluginRelativeOrderApplicator.Apply(_services.PluginManager, effectiveEffects);
				}
				_authorityValidator.ValidateAndReload(lease, authority, targetPaths);
				CollectionNativeStateIndex verified = _nativeStateReader.Capture(plan.Target);
				if (!CollectionPluginActivationResolver.Resolve(plan.Policy, plan.CapabilityReport.Manifest, verified, reviewedEffects.Select(x => x.Effect))
					.All(x => CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(verified, x)))
					throw new InvalidOperationException("The native plugin service returned success, but authoritative state does not match the exact reviewed Collection plugin state/order.");
			}
		}

		/// <summary>Recaptures complete native reality under the target reservation and publishes Applied only against that same observation boundary.</summary>
		private async Task<CollectionAssociationFinalizationResult> VerifyAndFinalizeAppliedAssociationAsync(
			CollectionOperationIdentity operationIdentity, CollectionReviewedWorkflowRuntime runtime,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
				"Collections.Workflow.FinishingInstallation", "Finishing Collection installation: checking mod registration and file ownership..."), true);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(targetPaths);
			if (!authority.Target.Equals(runtime.Plan.Target))
				throw new InvalidOperationException("The live canonical target changed before aggregate Collection verification.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, targetPaths);
				CollectionNativeStateIndex finalState = _nativeStateReader.Capture(runtime.Plan.Target);
				new CollectionAdditiveFinalStateVerifier().Verify(runtime.Plan, runtime.Matches, runtime.ImpactPlan,
					runtime.PreparedRecipes, finalState);
				CollectionAssociationFinalizationResult finalization = _associationCoordinator.FinalizeAppliedAssociation(
					operationIdentity, runtime.Plan, runtime.Matches);
				long metadataStarted = CollectionPerformanceMetrics.StartTiming();
				try
				{
					new CollectionNexusMetadataHydrator(_services.ModManager).Enrich(finalization.Bindings);
				}
				finally
				{
					CollectionPerformanceMetrics.RecordPostCommitMetadataEnrichment(metadataStarted);
				}
				return finalization;
			}
		}


		private async Task ReloadTargetAuthorityAsync(CollectionTargetAuthority authority, GameStoragePathSet paths,
			CancellationToken cancellationToken)
		{
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
			}
		}

		private bool HasReviewedSnapshot(CollectionOperation operation)
		{
			if (operation.PlanIdentity == null) return false;
			return StringComparer.Ordinal.Equals(_planStore.GetPayloadFormat(operation.PlanIdentity), CollectionReviewedWorkflowSnapshotCodec.PayloadFormat);
		}

		/// <summary>Restores the selected member metadata of a safely stopped partial installation for explicit re-preparation.</summary>
		internal CollectionAdditiveWorkflowRecoveryResult GetStoppedPartialInstallation(CollectionTargetAssociation association)
		{
			if (association == null) throw new ArgumentNullException(nameof(association));
			if (association.State != CollectionAssociationState.Incomplete) return null;
			CollectionOperation operation = _operationStore.GetLatestStoppedPartialApply(association);
			if (operation == null || operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability || !HasReviewedSnapshot(operation)) return null;
			CollectionReviewedWorkflowSnapshot snapshot = LoadReviewedSnapshot(operation);
			var rehydration = new CollectionReviewedWorkflowRehydrationResult(CollectionReviewedWorkflowRehydrationStatus.RepreparationRequired,
				snapshot, null, null, "The stopped installation retains its original selections; the remaining installation needs a fresh review.");
			return RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired, operation, rehydration, rehydration.Message);
		}

		private CollectionReviewedWorkflowSnapshot LoadReviewedSnapshot(CollectionOperation operation)
		{
			if (operation.PlanIdentity == null)
				throw new InvalidOperationException("The incomplete Collection operation has no exact reviewed plan identity.");
			CollectionResolvedPlanRecord record = _planStore.GetPlan(operation.PlanIdentity);
			if (record == null || !StringComparer.Ordinal.Equals(record.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat))
				throw new InvalidOperationException("The exact reviewed workflow v2 payload is unavailable for recovery.");
			CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshotCodec.Deserialize(record.UnsafePayload);
			if (!snapshot.Identity.Equals(operation.PlanIdentity) || operation.Revision == null || !snapshot.Revision.Equals(operation.Revision) ||
				!snapshot.Target.Equals(operation.Target))
				throw new InvalidDataException("The persisted reviewed workflow does not match the incomplete Collection operation journal.");
			return snapshot;
		}

		private CollectionOperation CompleteUnresumablePreReview(CollectionOperation operation)
		{
			if (operation.HasCrossedNativeBoundary)
				return operation;
			switch (operation.Phase)
			{
				case CollectionOperationPhase.Created:
				case CollectionOperationPhase.Resolving:
				case CollectionOperationPhase.Preparing:
				case CollectionOperationPhase.AwaitingInput:
				case CollectionOperationPhase.Revalidating:
				case CollectionOperationPhase.ReadyForReview:
				case CollectionOperationPhase.ReadyToApply:
				case CollectionOperationPhase.ApplyingNativeChildren:
				case CollectionOperationPhase.PausedAtSafeBoundary:
					return _operationCoordinator.CompleteFailedBeforeApply(operation.Identity);
				default:
					return operation;
			}
		}

		private CollectionAdditiveWorkflowApplyResult HandleNonResumableApplyReview(CollectionAdditiveWorkflowReview review)
		{
			CollectionAdditiveWorkflowApplyStatus status = review.Rehydration.Status == CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired
				? CollectionAdditiveWorkflowApplyStatus.RecoveryRequired
				: CollectionAdditiveWorkflowApplyStatus.RepreparationRequired;
			return ApplyResult(status, review.Operation, null, review.Rehydration.Message);
		}

		private void MarkRecoveryRequiredIfNeeded(CollectionOperationIdentity operationIdentity)
		{
			CollectionOperation operation = RequireOperation(operationIdentity);
			if (!operation.HasCrossedNativeBoundary || operation.Phase == CollectionOperationPhase.RecoveryRequired)
				return;
			if (operation.HasUnknownNativeDurability || operation.HasUnreconciledNativeChild)
				_operationCoordinator.MarkRecoveryRequired(operationIdentity);
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable journal.");
			return operation;
		}

		private static CollectionAdditiveWorkflowPreparationResult PreparationResult(CollectionAdditiveWorkflowPreparationStatus status,
			CollectionMemberAcquisitionBatch acquisition, CollectionDependencyPhasePlan dependencyPlan,
			CollectionConflictImpactPlan impactPlan, CollectionReviewedWorkflowRuntime runtime, string message,
			ResolvedCollectionMemberPlan failedMember = null, string failureReason = null)
		{
			return new CollectionAdditiveWorkflowPreparationResult(status, acquisition.PlanBuild.Operation, acquisition,
				dependencyPlan, impactPlan, runtime, message, null, failedMember, failureReason);
		}

		private static CollectionAdditiveWorkflowApplyResult ApplyResult(CollectionAdditiveWorkflowApplyStatus status,
			CollectionOperation operation, CollectionAssociationFinalizationResult finalization, string message)
		{
			return new CollectionAdditiveWorkflowApplyResult(status, operation, finalization, message);
		}

		private static CollectionAdditiveWorkflowRecoveryResult RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus status,
			CollectionOperation operation, CollectionReviewedWorkflowRehydrationResult rehydration, string message)
		{
			return new CollectionAdditiveWorkflowRecoveryResult(status, operation, rehydration, message);
		}
	}
}
