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

	/// <summary>Immutable headless preparation result for one additive Collection operation.</summary>
	public sealed class CollectionAdditiveWorkflowPreparationResult
	{
		internal CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus status,
			CollectionOperation operation, CollectionMemberAcquisitionBatch acquisitionBatch,
			CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan,
			CollectionReviewedWorkflowRuntime runtime, string message)
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
		}

		public CollectionAdditiveWorkflowPreparationStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionMemberAcquisitionBatch AcquisitionBatch { get; }
		public CollectionDependencyPhasePlan DependencyPlan { get; }
		public CollectionConflictImpactPlan ImpactPlan { get; }
		public CollectionReviewedWorkflowRuntime Runtime { get; }
		public string Message { get; }
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
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionAdditivePlanPreparationService _planPreparationService;
		private readonly CollectionAdditivePlanRevalidationService _planRevalidationService;
		private readonly CollectionMemberAcquisitionCoordinator _memberAcquisitionCoordinator;
		private readonly CollectionNativeRecipePreparer _nativeRecipePreparer;
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
				authorityValidator, null, null)
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
			CollectionAdditiveWinnerReconciliationDelegate winnerReconciliationOverride)
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
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			_nativeChildApplyOverride = nativeChildApplyOverride;
			_winnerReconciliationOverride = winnerReconciliationOverride;
			if (_services.ModManager == null)
				throw new InvalidOperationException("C6.15.12 requires the live ModManager service.");
		}

		/// <summary>Starts headless additive preparation and returns either pending input, a blocked/action state, or one durable exact review.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> PrepareAsync(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			if (effectiveSelection == null) throw new ArgumentNullException(nameof(effectiveSelection));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			return Task.Run(() => PrepareCore(effectiveSelection, targetPaths, confirmOverwriteCallback, cancellationToken), cancellationToken);
		}

		/// <summary>Resumes a previously returned acquisition/input pause, then revalidates target state before any later planning.</summary>
		public Task<CollectionAdditiveWorkflowPreparationResult> ResumePreparationAsync(CollectionMemberAcquisitionBatch acquisitionBatch,
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (acquisitionBatch == null) throw new ArgumentNullException(nameof(acquisitionBatch));
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			return Task.Run(() => ResumePreparationCore(acquisitionBatch, targetPaths, cancellationToken), cancellationToken);
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
			if (operation.PlanIdentity == null || !operation.PlanIdentity.Equals(expectedPlan))
				throw new InvalidOperationException("Approval does not refer to the exact currently reviewed Collection plan version.");

			CollectionAdditiveWorkflowReview review = GetReview(operationIdentity);
			if (!review.IsReady)
				return HandleNonResumableApplyReview(review);
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
				CollectionAssociationFinalizationResult resumedFinalization = _associationCoordinator.FinalizeAppliedAssociation(operationIdentity,
					runtime.Plan, runtime.Matches);
				return ApplyResult(CollectionAdditiveWorkflowApplyStatus.Committed, resumedFinalization.Operation,
					resumedFinalization, "The additive Collection was finalized from its verified boundary.");
			}

			CollectionNativeStateIndex currentState = runtime.CurrentState;
			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeChildPreparationResult prepared = _childPreparationCoordinator.PrepareNext(operationIdentity,
					runtime.Plan, runtime.Matches, runtime.DependencyPlan, runtime.ImpactPlan, currentState,
					runtime.PreparedRecipes.Select(x => x.EffectPreview), runtime.VerifiedArchives, targetPaths.InstallInfoPath);
				if (prepared == null)
					break;

				PreparedCollectionNativeRecipe recipe = runtime.GetPreparedRecipe(prepared.Child.Member.MemberKey);
				if (recipe == null)
					throw new InvalidOperationException("The reviewed workflow is missing the exact prepared native recipe for its next mutating member.");
				ModInstallationRecipeInput childRecipe = recipe.RecipeInput.ForOperationIdentity(prepared.Child.NativeOperation);
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
					CollectionNativeChildExecutionResult execution = await _childExecutionCoordinator.SubmitPreparedChildAsync(operationIdentity,
						runtime.Plan, runtime.ImpactPlan, recipe.EffectPreview, childRecipe, targetPaths, cancellationToken).ConfigureAwait(false);
					if (execution == null)
					{
						operation = _operationCoordinator.PauseAtSafeBoundary(operationIdentity);
						return ApplyResult(CollectionAdditiveWorkflowApplyStatus.PausedAtSafeBoundary, operation, null,
							"The shared native operation lane is busy; the Collection remains paused at a durable safe boundary.");
					}

					try
					{
						verification = await _childVerificationCoordinator.VerifySubmittedChildAsync(execution, runtime.Plan,
							recipe.EffectPreview, targetPaths).ConfigureAwait(false);
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
						"Native durability is ambiguous; C6.9 restart reconciliation is required before any further child can run.");
				}

				CollectionAssociationReconciliationResult reconciled = _associationCoordinator.ReconcileVerifiedChild(verification, runtime.Plan);
				operation = reconciled.Operation;
				if (!verification.HasVerifiedCommit)
				{
					operation = _operationCoordinator.CompleteStoppedPartial(operationIdentity);
					return ApplyResult(CollectionAdditiveWorkflowApplyStatus.StoppedPartial, operation, null,
						"The native child did not commit. Known native reality was reconciled and the Collection stopped without retrying it.");
				}

				// C6.10 changes Collection association provenance, which participates in the next C6.1 fingerprint.
				currentState = _nativeStateReader.Capture(runtime.Plan.Target);
			}

			if (_winnerReconciliationOverride != null)
				await _winnerReconciliationOverride(operationIdentity, runtime.Plan, runtime.Matches, runtime.ImpactPlan,
					targetPaths, cancellationToken).ConfigureAwait(false);
			else
				await _winnerCoordinator.ReconcileAsync(operationIdentity, runtime.Plan, runtime.Matches,
					runtime.ImpactPlan, targetPaths, cancellationToken).ConfigureAwait(false);
			CollectionAssociationFinalizationResult finalization = _associationCoordinator.FinalizeAppliedAssociation(operationIdentity,
				runtime.Plan, runtime.Matches);
			return ApplyResult(CollectionAdditiveWorkflowApplyStatus.Committed, finalization.Operation, finalization,
				"The exact reviewed additive Collection plan was applied and verified.");
		}

		/// <summary>
		/// Runs target-scoped startup recovery first, reconciles any crossed native child through C6.9/C6.10, and classifies safe resumable work.
		/// </summary>
		public async Task<IReadOnlyList<CollectionAdditiveWorkflowRecoveryResult>> ReconcileIncompleteTargetAsync(
			GameStoragePathSet targetPaths, CancellationToken cancellationToken)
		{
			if (targetPaths == null) throw new ArgumentNullException(nameof(targetPaths));
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(targetPaths);
			await ReloadTargetAuthorityAsync(authority, targetPaths, cancellationToken).ConfigureAwait(false);

			var results = new List<CollectionAdditiveWorkflowRecoveryResult>();
			foreach (CollectionOperation persisted in _operationStore.GetIncompleteOperations(authority.Target))
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionOperation operation = RequireOperation(persisted.Identity);
				if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan)
					continue;

				if (operation.PlanIdentity == null || operation.Revision == null || !HasReviewedSnapshot(operation))
				{
					if (!operation.HasCrossedNativeBoundary)
						operation = CompleteUnresumablePreReview(operation);
					results.Add(RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired, operation, null,
						"This pre-review operation has no durable reviewed workflow to resume; prepare the Collection again."));
					continue;
				}

				CollectionReviewedWorkflowSnapshot snapshot = LoadReviewedSnapshot(operation);
				ResolvedCollectionPlan reviewedPlan = null;
				if (operation.HasUnreconciledNativeChild || operation.RequiresRecovery || operation.Phase == CollectionOperationPhase.Recovering)
				{
					reviewedPlan = _runtimeReconstructor.ReconstructPlan(snapshot);
					CollectionNativeChildRestartReconciliationResult restart = await _restartCoordinator.ReconcileAsync(operation.Identity,
						targetPaths, cancellationToken).ConfigureAwait(false);
					if (restart.RequiresRecovery)
					{
						operation = RequireOperation(operation.Identity);
						results.Add(RecoveryResult(CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired, operation, null,
							"Native durability is still ambiguous after C6.9 reconciliation."));
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

				CollectionReviewedWorkflowRehydrationResult rehydration = _workflowRehydrator.Rehydrate(operation.Identity);
				if (!rehydration.CanResume)
				{
					CollectionAdditiveWorkflowRecoveryStatus status = rehydration.Status == CollectionReviewedWorkflowRehydrationStatus.RecoveryRequired
						? CollectionAdditiveWorkflowRecoveryStatus.RecoveryRequired
						: CollectionAdditiveWorkflowRecoveryStatus.RepreparationRequired;
					results.Add(RecoveryResult(status, RequireOperation(operation.Identity), rehydration, rehydration.Message));
					continue;
				}

				_runtimeReconstructor.Reconstruct(rehydration); // Prove the persisted review is still executable, not merely parseable.
				operation = RequireOperation(operation.Identity);
				CollectionAdditiveWorkflowRecoveryStatus readyStatus = operation.Phase == CollectionOperationPhase.ReadyForReview
					? CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired
					: CollectionAdditiveWorkflowRecoveryStatus.ReadyToResume;
				results.Add(RecoveryResult(readyStatus, operation, rehydration,
					readyStatus == CollectionAdditiveWorkflowRecoveryStatus.ReviewRequired
						? "The exact reviewed workflow is valid and awaits explicit approval."
						: "The exact reviewed workflow is valid at its latest verified safe boundary and can be resumed explicitly."));
			}
			return new ReadOnlyCollection<CollectionAdditiveWorkflowRecoveryResult>(results);
		}

		private CollectionAdditiveWorkflowPreparationResult PrepareCore(CollectionEffectiveSelection effectiveSelection,
			GameStoragePathSet targetPaths, ConfirmOverwriteCallback confirmOverwriteCallback, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CollectionAdditivePlanBuildResult planBuild = _planPreparationService.Prepare(effectiveSelection, targetPaths);
			CollectionMemberAcquisitionBatch acquisition = _memberAcquisitionCoordinator.Begin(planBuild,
				confirmOverwriteCallback, cancellationToken);
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
			return FinalizePreparation(acquisition, cancellationToken);
		}

		private CollectionAdditiveWorkflowPreparationResult FinalizePreparation(CollectionMemberAcquisitionBatch acquisition,
			CancellationToken cancellationToken)
		{
			CollectionAdditivePlanBuildResult planBuild = acquisition.PlanBuild;
			ResolvedCollectionPlan plan = planBuild.Plan;
			CollectionMemberMatchSet matches = acquisition.MatchSet;
			CollectionDependencyPhasePlan dependencyPlan = _dependencyPlanner.Plan(plan, matches);
			if (!dependencyPlan.IsReady)
				return PreparationResult(CollectionAdditiveWorkflowPreparationStatus.Blocked, acquisition, dependencyPlan, null, null,
					"The selected Collection dependency/phase graph is blocked.");

			List<PreparedCollectionNativeRecipe> recipes;
			try
			{
				recipes = PrepareNativeRecipes(acquisition, cancellationToken);
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

			CollectionConflictImpactPlan impactPlan = _impactPlanner.Plan(plan, matches, dependencyPlan,
				planBuild.NativeState, recipes.Select(x => x.EffectPreview));
			if (!impactPlan.IsReady)
			{
				CollectionAdditiveWorkflowPreparationStatus status = impactPlan.Status == CollectionConflictImpactStatus.ActionRequired
					? CollectionAdditiveWorkflowPreparationStatus.ActionRequired
					: impactPlan.Status == CollectionConflictImpactStatus.PreparationRequired
						? CollectionAdditiveWorkflowPreparationStatus.PreparationRequired
						: CollectionAdditiveWorkflowPreparationStatus.Blocked;
				return PreparationResult(status, acquisition, dependencyPlan, impactPlan, null,
					impactPlan.Issues.Count == 0 ? "The Collection impact plan is not executable." : impactPlan.Issues[0].Message);
			}

			CollectionOperation ready = _operationCoordinator.MarkReadyForReview(planBuild.Operation.Identity,
				plan, dependencyPlan, impactPlan, recipes);
			CollectionAdditiveWorkflowReview durableReview = GetReview(ready.Identity);
			if (!durableReview.IsReady)
				throw new InvalidOperationException("The newly persisted reviewed workflow could not be rehydrated from its durable inputs: " + durableReview.Rehydration.Message);
			return new CollectionAdditiveWorkflowPreparationResult(CollectionAdditiveWorkflowPreparationStatus.ReadyForReview,
				durableReview.Operation, acquisition, dependencyPlan, impactPlan, durableReview.Runtime,
				"The exact additive Collection review is ready for explicit approval.");
		}

		private List<PreparedCollectionNativeRecipe> PrepareNativeRecipes(CollectionMemberAcquisitionBatch acquisition,
			CancellationToken cancellationToken)
		{
			ResolvedCollectionPlan plan = acquisition.PlanBuild.Plan;
			CollectionNativeStateIndex state = acquisition.PlanBuild.NativeState;
			var recipes = new List<PreparedCollectionNativeRecipe>();
			foreach (CollectionMemberMatchResult match in acquisition.MatchSet.Members)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (match.Disposition == CollectionMemberMatchDisposition.InstalledCompatible)
					continue;
				if (match.Disposition != CollectionMemberMatchDisposition.ArchiveOnlyReuse &&
					match.Disposition != CollectionMemberMatchDisposition.ReinstallRequired)
					throw new InvalidOperationException("A selected Collection member reached native recipe preparation without an executable C6.2 disposition.");

				CollectionMemberAcquisitionState acquisitionState = acquisition.Members.Single(x => x.Match.Member.MemberKey.Equals(match.Member.MemberKey));
				CollectionVerifiedArchive archive = acquisitionState.VerifiedArchive ?? match.VerifiedArchive;
				if (archive == null)
					throw new InvalidOperationException("A mutating Collection member does not have its exact verified immutable archive.");
				IMod managedMod = ResolveManagedMod(match);
				ModInstallContext installContext = match.Disposition == CollectionMemberMatchDisposition.ReinstallRequired
					? new ModInstallContext(match.MatchedNativeMod.InstallMethod, match.MatchedNativeMod.InstallRoot)
					: _services.ModManager.CapturePreferredInstallContext(ModInstallRoot.Default);
				bool skipReadme = _services.ModManager.EnvironmentInfo.Settings.SkipReadmeFiles;
				recipes.Add(_nativeRecipePreparer.PrepareBasicSimpleExact(plan, match.Member, archive, managedMod,
					_services.ModManager.GameMode, installContext, state, skipReadme, _services.PluginManager, cancellationToken));
			}
			return recipes;
		}

		private IMod ResolveManagedMod(CollectionMemberMatchResult match)
		{
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

			string domain;
			long nexusModId;
			long nexusFileId;
			if (!NexusCollectionModFileArtifactIdentity.TryParse(match.Member.ArtifactChoice.SelectedArtifact,
				out domain, out nexusModId, out nexusFileId))
				throw new NotSupportedException("C6.15 Gate A prepares only exact Nexus mod-file artifacts.");
			string currentDomain = _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName;
			if (!StringComparer.OrdinalIgnoreCase.Equals(domain, currentDomain))
				throw new InvalidOperationException("The active native repository game does not match the selected Collection artifact domain.");

			string modId = nexusModId.ToString(CultureInfo.InvariantCulture);
			string fileId = nexusFileId.ToString(CultureInfo.InvariantCulture);
			if (previous != null && ModFileIdentity.IsSameRepositoryFile(previous.Id, previous.DownloadId, modId, fileId))
				return previous;
			List<IMod> candidates = _services.ModManager.ManagedMods.Where(x => x != null &&
				ModFileIdentity.IsSameRepositoryFile(x.Id, x.DownloadId, modId, fileId)).ToList();
			if (candidates.Count != 1)
				throw new InvalidOperationException(candidates.Count == 0
					? "The exact verified incoming archive is not present in the native managed-mod registry. Import it through the existing Add Mod pipeline before preparing the Collection."
					: "Multiple native managed archives match the exact selected Nexus mod/file identity.");
			return candidates[0];
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
			CollectionResolvedPlanRecord record = _planStore.GetPlan(operation.PlanIdentity);
			return record != null && StringComparer.Ordinal.Equals(record.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat);
		}

		private CollectionReviewedWorkflowSnapshot LoadReviewedSnapshot(CollectionOperation operation)
		{
			if (operation.PlanIdentity == null)
				throw new InvalidOperationException("The incomplete Collection operation has no exact reviewed plan identity.");
			CollectionResolvedPlanRecord record = _planStore.GetPlan(operation.PlanIdentity);
			if (record == null || !StringComparer.Ordinal.Equals(record.PayloadFormat, CollectionReviewedWorkflowSnapshotCodec.PayloadFormat))
				throw new InvalidOperationException("The exact reviewed workflow v2 payload is unavailable for recovery.");
			CollectionReviewedWorkflowSnapshot snapshot = CollectionReviewedWorkflowSnapshotCodec.Deserialize(record.Payload);
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
			CollectionConflictImpactPlan impactPlan, CollectionReviewedWorkflowRuntime runtime, string message)
		{
			return new CollectionAdditiveWorkflowPreparationResult(status, acquisition.PlanBuild.Operation, acquisition,
				dependencyPlan, impactPlan, runtime, message);
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
