using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	public enum CollectionReplacementIncomingInstallationStatus
	{
		Unknown = 0,
		Completed = 1,
		ExplicitReviewRequired = 2,
		RecoveryRequired = 3,
		NativeLaneBusy = 4
	}

	public sealed class CollectionReplacementIncomingInstallationResult
	{
		internal CollectionReplacementIncomingInstallationResult(CollectionReplacementIncomingInstallationStatus status,
			CollectionOperation operation, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionReplacementIncomingInstallationStatus), status) || status == CollectionReplacementIncomingInstallationStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Message = message ?? String.Empty;
		}

		public CollectionReplacementIncomingInstallationStatus Status { get; }
		public CollectionOperation Operation { get; }
		public string Message { get; }
		public bool IsCompleted { get { return Status == CollectionReplacementIncomingInstallationStatus.Completed; } }
	}

	/// <summary>
	/// C8.6 coordinator for reviewed incoming replacement activation/reinstall children and final native winner/plugin reconciliation.
	/// </summary>
	/// <remarks>
	/// This coordinator deliberately does not publish incoming/outgoing Collection association transitions. C8.8 owns aggregate
	/// verification and association publication. C8.6 reuses C6 child recovery/execution/verification and C8.5 visibility barriers.
	/// </remarks>
	public sealed class CollectionReplacementIncomingInstallationCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionMemberMatchEngine _matchEngine;
		private readonly CollectionDependencyPhasePlanner _dependencyPlanner;
		private readonly CollectionConflictImpactPlanner _impactPlanner;
		private readonly CollectionNativeChildPreparationCoordinator _childPreparation;
		private readonly CollectionNativeChildExecutionCoordinator _childExecution;
		private readonly CollectionNativeChildVerificationCoordinator _childVerification;
		private readonly CollectionNativeChildRestartReconciliationCoordinator _childRestart;
		private readonly CollectionReplacementIncomingChildReconciliationCoordinator _childReconciliation;
		private readonly CollectionReviewedFileWinnerReconciliationCoordinator _winnerCoordinator;
		private readonly CollectionReplacementPhaseBarrierCoordinator _barrierCoordinator;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionReplacementIncomingInstallationCoordinator(ServiceManager services, IProfileManager profileManager,
			GameStorageService gameStorageService, CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			if (artifactStore == null) throw new ArgumentNullException(nameof(artifactStore));
			if (referenceStore == null) throw new ArgumentNullException(nameof(referenceStore));
			if (manifestStore == null) throw new ArgumentNullException(nameof(manifestStore));
			if (profileManager == null) throw new ArgumentNullException(nameof(profileManager));
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			_matchEngine = new CollectionMemberMatchEngine();
			_dependencyPlanner = new CollectionDependencyPhasePlanner();
			_impactPlanner = new CollectionConflictImpactPlanner();
			_childPreparation = new CollectionNativeChildPreparationCoordinator(operationStore, planStore, artifactStore, referenceStore, manifestStore);
			_childExecution = new CollectionNativeChildExecutionCoordinator(services, gameStorageService, operationStore, planStore, associationStore, manifestStore);
			_childVerification = new CollectionNativeChildVerificationCoordinator(services, gameStorageService, operationStore, planStore, associationStore, manifestStore);
			_childRestart = new CollectionNativeChildRestartReconciliationCoordinator(services, gameStorageService, operationStore, planStore, associationStore, manifestStore);
			_childReconciliation = new CollectionReplacementIncomingChildReconciliationCoordinator(operationStore, manifestStore);
			_winnerCoordinator = new CollectionReviewedFileWinnerReconciliationCoordinator(services, profileManager, gameStorageService,
				operationStore, planStore, associationStore, artifactStore, referenceStore);
			_barrierCoordinator = new CollectionReplacementPhaseBarrierCoordinator(services, gameStorageService, operationStore,
				planStore, associationStore, artifactStore, referenceStore);
		}

		public Task<CollectionReplacementIncomingInstallationResult> ExecuteAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan reviewedPlan, CollectionReplacementEnvironmentProjection reviewedBaseline,
			IEnumerable<CollectionVerifiedArchive> verifiedArchives, IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes,
			GameStoragePathSet paths, CollectionReplacementRecipeRevalidationCallback recipeRevalidation,
			CancellationToken cancellationToken)
		{
			return ExecuteCoreAsync(operationIdentity, reviewedPlan, reviewedBaseline, verifiedArchives, preparedRecipes,
				paths, recipeRevalidation, cancellationToken);
		}

		private async Task<CollectionReplacementIncomingInstallationResult> ExecuteCoreAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan reviewedPlan, CollectionReplacementEnvironmentProjection reviewedBaseline,
			IEnumerable<CollectionVerifiedArchive> verifiedArchives, IEnumerable<PreparedCollectionNativeRecipe> preparedRecipes,
			GameStoragePathSet paths, CollectionReplacementRecipeRevalidationCallback recipeRevalidation,
			CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (reviewedBaseline == null) throw new ArgumentNullException(nameof(reviewedBaseline));
			if (verifiedArchives == null) throw new ArgumentNullException(nameof(verifiedArchives));
			if (preparedRecipes == null) throw new ArgumentNullException(nameof(preparedRecipes));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (reviewedPlan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("C8.6 requires an explicit replacement reviewed plan.", nameof(reviewedPlan));
			cancellationToken.ThrowIfCancellationRequested();

			CollectionOperation operation = RequireOperation(operationIdentity, reviewedPlan);
			if (operation.Phase == CollectionOperationPhase.IncomingNativeChildrenVerified)
				return Result(CollectionReplacementIncomingInstallationStatus.Completed, operation, "Incoming replacement native work is already verified.");
			if (operation.Phase == CollectionOperationPhase.AwaitingReplacementPhaseAmendment ||
				operation.Phase == CollectionOperationPhase.ReplacementBarrierRevalidationRequired)
				return Result(CollectionReplacementIncomingInstallationStatus.ExplicitReviewRequired, operation, "C8.5 replacement review/revalidation must complete before incoming native work can continue.");
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired || operation.Phase == CollectionOperationPhase.Recovering ||
				operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return Result(CollectionReplacementIncomingInstallationStatus.RecoveryRequired, operation, "Replacement requires C8.7 recovery before incoming installation can continue.");
			if (operation.Phase == CollectionOperationPhase.ReadyForIncomingNativeChildren)
				operation = SavePhase(operation, CollectionOperationPhase.InstallingIncomingNativeChildren, CollectionOperationResultState.Pending);
			else if (operation.Phase != CollectionOperationPhase.InstallingIncomingNativeChildren)
				throw new InvalidOperationException("C8.6 requires the verified post-removal incoming boundary.");

			List<CollectionVerifiedArchive> archives = verifiedArchives.ToList();
			Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes = IndexAndValidateEffectiveRecipes(operationIdentity,
				reviewedPlan.Identity, preparedRecipes);

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionNativeStateIndex state = _nativeStateReader.Capture(reviewedPlan.Target);
				ExecutionPlanning planning = BuildExecutionPlanning(reviewedPlan, state, archives, recipes.Values);
				CollectionPlannedPhaseMember next = FindNextActionable(planning.DependencyPlan, RequireOperation(operationIdentity, reviewedPlan));
				if (next == null)
					break;

				int phaseIndex = planning.DependencyPlan.Phases.ToList().FindIndex(x => x.PhaseNumber == next.InstallationPhase);
				if (phaseIndex > 0 && !HasCommittedIncomingChildInPhase(operationIdentity, reviewedPlan, planning.DependencyPlan.Phases[phaseIndex]))
				{
					CollectionExecutionPhase previousPhase = planning.DependencyPlan.Phases[phaseIndex - 1];
					CollectionExecutionPhase nextPhase = planning.DependencyPlan.Phases[phaseIndex];
					List<PreparedCollectionNativeRecipe> completed = GetCommittedRecipes(operationIdentity, reviewedPlan, planning.DependencyPlan,
						recipes, previousPhase.PhaseNumber);
					List<CollectionMemberKey> required = nextPhase.Members.Select(x => x.MemberKey).Where(recipes.ContainsKey).ToList();
					IReadOnlyList<PreparedCollectionNativeRecipe> revalidatedPrepared = null;
					CollectionReplacementRecipeRevalidationCallback capture = async (context, token) =>
					{
						if (recipeRevalidation == null)
							throw new ArgumentNullException(nameof(recipeRevalidation), "A replacement visibility barrier with incoming recipes requires read-only recipe re-preparation.");
						revalidatedPrepared = await recipeRevalidation(context, token).ConfigureAwait(true);
						return revalidatedPrepared;
					};
					CollectionReplacementBarrierResult barrier = await _barrierCoordinator.RevalidateIncomingVisibilityBarrierAsync(
						operationIdentity, reviewedPlan.Identity, paths, phaseIndex, previousPhase.PhaseNumber, nextPhase.PhaseNumber,
						reviewedBaseline, completed, required, required.Count == 0 ? null : capture, cancellationToken).ConfigureAwait(false);
					if (barrier.RequiresExplicitReview)
						return Result(CollectionReplacementIncomingInstallationStatus.ExplicitReviewRequired, barrier.Operation, barrier.Message);
					if (barrier.RequiresRecovery)
						return Result(CollectionReplacementIncomingInstallationStatus.RecoveryRequired, barrier.Operation, barrier.Message);
					if (revalidatedPrepared != null)
						foreach (PreparedCollectionNativeRecipe recipe in revalidatedPrepared) recipes[recipe.Member.MemberKey] = recipe;
					continue;
				}

				PreparedCollectionNativeRecipe prepared;
				if (!recipes.TryGetValue(next.MemberKey, out prepared))
					throw new InvalidDataException("The next mutating replacement member is missing its exact effective reviewed native recipe.");
				CollectionNativeChildPreparationResult child = _childPreparation.PrepareNextForReplacement(operationIdentity,
					planning.ExecutionPlan, planning.Matches, planning.DependencyPlan, planning.ImpactPlan, state,
					recipes.Values.Select(x => x.EffectPreview), archives, paths.InstallInfoPath);
				if (child == null)
					break;
				if (!child.Child.Member.MemberKey.Equals(next.MemberKey))
					throw new InvalidOperationException("C8.6 child preparation crossed a dependency phase barrier without C8.5 revalidation.");

				ModInstallationRecipeInput childRecipe = prepared.RecipeInput.ForOperationIdentity(child.Child.NativeOperation);
				CollectionNativeChildExecutionResult execution = await _childExecution.SubmitPreparedReplacementChildAsync(operationIdentity,
					planning.ExecutionPlan, planning.ImpactPlan, prepared.EffectPreview, childRecipe, paths, cancellationToken).ConfigureAwait(false);
				if (execution == null)
					return Result(CollectionReplacementIncomingInstallationStatus.NativeLaneBusy,
						RequireOperation(operationIdentity, reviewedPlan), "The shared native operation lane is busy; the prepared replacement child has not started.");

				CollectionNativeChildVerificationResult verification;
				try
				{
					verification = await _childVerification.VerifySubmittedReplacementChildAsync(execution, planning.ExecutionPlan,
						prepared.EffectPreview, paths).ConfigureAwait(false);
				}
				catch
				{
					MarkRecoveryRequired(operationIdentity, reviewedPlan);
					throw;
				}
				operation = _childReconciliation.ReconcileVerifiedChild(verification, planning.ExecutionPlan);
				if (!verification.HasVerifiedCommit)
				{
					operation = MarkRecoveryRequired(operationIdentity, reviewedPlan);
					return Result(CollectionReplacementIncomingInstallationStatus.RecoveryRequired, operation,
						"An incoming replacement child did not verify as durably committed; C8.7 recovery is required before continuation.");
				}
			}

			CollectionNativeStateIndex finalState = _nativeStateReader.Capture(reviewedPlan.Target);
			ExecutionPlanning finalPlanning = BuildExecutionPlanning(reviewedPlan, finalState, archives, recipes.Values);
			ValidateAllMutatingMembersCommitted(operationIdentity, reviewedPlan, finalPlanning.DependencyPlan);
			await _winnerCoordinator.ReconcileReplacementAsync(operationIdentity, finalPlanning.ExecutionPlan, finalPlanning.Matches,
				finalPlanning.ImpactPlan, paths, cancellationToken).ConfigureAwait(false);
			await ReconcileReviewedPluginStateAsync(finalPlanning.ExecutionPlan, finalPlanning.ImpactPlan, paths, cancellationToken).ConfigureAwait(false);

			operation = RequireOperation(operationIdentity, reviewedPlan);
			operation = SavePhase(operation, CollectionOperationPhase.IncomingNativeChildrenVerified, CollectionOperationResultState.Pending);
			return Result(CollectionReplacementIncomingInstallationStatus.Completed, operation,
				"All reviewed incoming replacement children, file winners and supported plugin state were verified.");
		}

		/// <summary>Runs the existing C6.9 evidence classifier for one restart-ambiguous incoming replacement child without replaying it.</summary>
		public async Task<CollectionOperation> ReconcileInterruptedIncomingChildAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan reviewedPlan, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			CollectionNativeChildRestartReconciliationResult result = await _childRestart.ReconcileReplacementIncomingAsync(
				operationIdentity, paths, cancellationToken).ConfigureAwait(false);
			ResolvedCollectionPlan executionPlan = RebindState(reviewedPlan, result.NativeState.Fingerprint);
			return _childReconciliation.ReconcileRestartedChild(result, executionPlan);
		}

		private ExecutionPlanning BuildExecutionPlanning(ResolvedCollectionPlan reviewedPlan, CollectionNativeStateIndex state,
			IEnumerable<CollectionVerifiedArchive> archives, IEnumerable<PreparedCollectionNativeRecipe> recipes)
		{
			ResolvedCollectionPlan executionPlan = RebindState(reviewedPlan, state.Fingerprint);
			CollectionMemberMatchSet matches = _matchEngine.MatchForReplacementExecution(executionPlan, state, archives);
			CollectionDependencyPhasePlan dependency = _dependencyPlanner.Plan(executionPlan, matches);
			CollectionConflictImpactPlan impact = _impactPlanner.PlanForReplacementExecution(executionPlan, matches, dependency,
				state, recipes.Select(x => x.EffectPreview));
			if (!dependency.IsReady || !impact.IsReady || matches.HasBlockedMembers || matches.HasAcquisitionRequired)
				throw new InvalidOperationException("The current replacement incoming execution baseline is no longer fully actionable and must return to explicit review/recovery.");
			ValidateReviewedFileWinners(executionPlan.Identity, impact);
			return new ExecutionPlanning(executionPlan, matches, dependency, impact);
		}

		private Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> IndexAndValidateEffectiveRecipes(
			CollectionOperationIdentity operationIdentity, CollectionPlanIdentity planIdentity, IEnumerable<PreparedCollectionNativeRecipe> supplied)
		{
			Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes = supplied.ToDictionary(x => x.Member.MemberKey);
			IReadOnlyList<CollectionReplacementPreparedRecipeApproval> approved = _barrierCoordinator.GetEffectiveReviewedRecipes(operationIdentity, planIdentity);
			foreach (CollectionReplacementPreparedRecipeApproval approval in approved)
			{
				PreparedCollectionNativeRecipe recipe;
				if (!recipes.TryGetValue(approval.MemberKey, out recipe) || !Matches(approval, recipe))
					throw new InvalidDataException("The supplied C8.6 prepared recipe set does not match the currently effective durable replacement review/amendment chain.");
			}
			return recipes;
		}

		private void ValidateReviewedFileWinners(CollectionPlanIdentity planIdentity, CollectionConflictImpactPlan impactPlan)
		{
			CollectionResolvedPlanRecord persisted = _planStore.GetPlan(planIdentity);
			if (persisted == null || !StringComparer.Ordinal.Equals(persisted.PayloadFormat, CollectionReplacementReviewedIntentCodec.PayloadFormat))
				throw new InvalidDataException("The replacement reviewed intent required for C8.6 file-winner validation is unavailable.");
			CollectionReplacementReviewedIntent intent = CollectionReplacementReviewedIntentCodec.Deserialize(persisted.Payload);
			Dictionary<ModDeploymentTarget, CollectionReplacementFileWinnerApproval> approvals = intent.FileWinnerApprovals.ToDictionary(x => x.Target);
			foreach (CollectionFileImpact impact in impactPlan.FileImpacts.Where(x => x.Writers.Count > 1))
			{
				CollectionReplacementFileWinnerApproval approval;
				if (impact.PlannedWinner == null || !approvals.TryGetValue(impact.Target, out approval) || !approval.WinnerMemberKey.Equals(impact.PlannedWinner))
					throw new InvalidOperationException("A multi-writer replacement target no longer matches its exact durable C8.3 winner approval.");
			}
		}

		private async Task ReconcileReviewedPluginStateAsync(ResolvedCollectionPlan plan, CollectionConflictImpactPlan impactPlan,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			List<CollectionPluginImpact> reviewedEffects = impactPlan.PluginImpacts.Where(x =>
				x.Effect.Kind == CollectionPlannedPluginEffectKind.Activation || x.Effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder).ToList();
			if (reviewedEffects.Count == 0) return;
			if (_services.PluginManager == null)
				throw new InvalidOperationException("The reviewed replacement requires plugin-state/order reconciliation, but the native plugin manager is unavailable.");

			var requested = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionPluginImpact impact in reviewedEffects.Where(x => x.Effect.Kind == CollectionPlannedPluginEffectKind.Activation))
			{
				string path = impact.Effect.PluginPaths[0];
				bool active = impact.Effect.Active.Value;
				bool prior;
				if (requested.TryGetValue(path, out prior) && prior != active)
					throw new InvalidDataException("The reviewed replacement contains contradictory final plugin activation requests.");
				requested[path] = active;
			}

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(plan.Target)) throw new InvalidOperationException("The live target changed before C8.6 plugin reconciliation.");
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionNativeStateIndex current = _nativeStateReader.Capture(plan.Target);
				if (reviewedEffects.All(x => CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(current, x.Effect))) return;

				if (requested.Count > 0)
				{
					IList<Nexus.Client.PluginManagement.PluginValidationDiagnostic> diagnostics;
					if (!_services.PluginManager.TryReconcileDeployedPlugins(requested.Keys.ToList(), requested, out diagnostics))
						throw new InvalidOperationException("The native plugin policy rejected the exact reviewed replacement plugin state.");
				}

				CollectionPluginRelativeOrderApplicator.Apply(_services.PluginManager, reviewedEffects.Select(x => x.Effect));
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionNativeStateIndex verified = _nativeStateReader.Capture(plan.Target);
				if (!reviewedEffects.All(x => CollectionNativeChildVerificationCoordinator.VerifyPluginEffect(verified, x.Effect)))
					throw new InvalidOperationException("The native plugin service returned success, but authoritative state does not match the exact reviewed replacement plugin state/order.");
			}
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan reviewedPlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(reviewedPlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(reviewedPlan.Revision) || !operation.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The C8.6 request does not belong to the active exact replacement operation.");
			return operation;
		}

		private CollectionOperation MarkRecoveryRequired(CollectionOperationIdentity identity, ResolvedCollectionPlan plan)
		{
			CollectionOperation operation = RequireOperation(identity, plan);
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired && operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return operation;
			return SavePhase(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
		}

		private CollectionOperation SavePhase(CollectionOperation operation, CollectionOperationPhase phase, CollectionOperationResultState result)
		{
			if (operation.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, operation.NativeChildren);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private static ResolvedCollectionPlan RebindState(ResolvedCollectionPlan reviewedPlan, CollectionCurrentStateFingerprint fingerprint)
		{
			return new ResolvedCollectionPlan(reviewedPlan.Identity, reviewedPlan.Target, reviewedPlan.Policy, fingerprint,
				reviewedPlan.CapabilityReport, reviewedPlan.SelectedMembers);
		}

		private static CollectionPlannedPhaseMember FindNextActionable(CollectionDependencyPhasePlan dependencyPlan, CollectionOperation operation)
		{
			var completed = new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				x.IsReconciled && x.HasVerifiedCommittedNativeState).Select(x => x.Member.MemberKey));
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
				foreach (CollectionPlannedPhaseMember member in phase.Members)
					if (!completed.Contains(member.MemberKey) && member.MatchDisposition != CollectionMemberMatchDisposition.InstalledCompatible)
						return member;
			return null;
		}

		private bool HasCommittedIncomingChildInPhase(CollectionOperationIdentity identity, ResolvedCollectionPlan plan, CollectionExecutionPhase phase)
		{
			CollectionOperation operation = RequireOperation(identity, plan);
			var keys = new HashSet<CollectionMemberKey>(phase.Members.Select(x => x.MemberKey));
			return operation.NativeChildren.Any(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled &&
				x.HasVerifiedCommittedNativeState && keys.Contains(x.Member.MemberKey));
		}

		private List<PreparedCollectionNativeRecipe> GetCommittedRecipes(CollectionOperationIdentity identity, ResolvedCollectionPlan plan,
			CollectionDependencyPhasePlan dependencyPlan, IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipes, double throughPhase)
		{
			CollectionOperation operation = RequireOperation(identity, plan);
			var allowed = new HashSet<CollectionMemberKey>(dependencyPlan.Phases.Where(x => x.PhaseNumber <= throughPhase)
				.SelectMany(x => x.Members).Select(x => x.MemberKey));
			var result = new List<PreparedCollectionNativeRecipe>();
			foreach (CollectionNativeChildOperation child in operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				x.IsReconciled && x.HasVerifiedCommittedNativeState && allowed.Contains(x.Member.MemberKey)))
			{
				PreparedCollectionNativeRecipe recipe;
				if (!recipes.TryGetValue(child.Member.MemberKey, out recipe))
					throw new InvalidDataException("A committed incoming replacement child is missing its effective reviewed recipe for barrier projection.");
				result.Add(recipe);
			}
			return result;
		}

		private void ValidateAllMutatingMembersCommitted(CollectionOperationIdentity identity, ResolvedCollectionPlan plan,
			CollectionDependencyPhasePlan dependencyPlan)
		{
			CollectionOperation operation = RequireOperation(identity, plan);
			var committed = new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				x.IsReconciled && x.HasVerifiedCommittedNativeState).Select(x => x.Member.MemberKey));
			foreach (CollectionPlannedPhaseMember member in dependencyPlan.Phases.SelectMany(x => x.Members))
				if (member.MatchDisposition != CollectionMemberMatchDisposition.InstalledCompatible && !committed.Contains(member.MemberKey))
					throw new InvalidOperationException("C8.6 cannot close its incoming barrier while a reviewed mutating member lacks VerifiedCommitted reconciliation.");
		}

		private static bool Matches(CollectionReplacementPreparedRecipeApproval approval, PreparedCollectionNativeRecipe recipe)
		{
			return approval.MemberKey.Equals(recipe.Member.MemberKey) &&
				StringComparer.Ordinal.Equals(approval.ProviderRecipeFingerprint, recipe.ProviderRecipeIdentity.Fingerprint) &&
				StringComparer.Ordinal.Equals(approval.PreparedNativeFingerprint, recipe.PreparedNativeIdentity.Fingerprint) &&
				StringComparer.Ordinal.Equals(approval.EffectFingerprint, CollectionReplacementPreparedRecipeApproval.ComputeEffectFingerprint(recipe.EffectPreview)) &&
				approval.Method == recipe.InstallContext.Method && approval.Root == recipe.InstallContext.InstallRoot &&
				StringComparer.Ordinal.Equals(approval.AdapterId, recipe.AdapterId) && approval.AdapterVersion == recipe.AdapterVersion;
		}

		private static CollectionReplacementIncomingInstallationResult Result(CollectionReplacementIncomingInstallationStatus status,
			CollectionOperation operation, string message)
		{
			return new CollectionReplacementIncomingInstallationResult(status, operation, message);
		}

		private sealed class ExecutionPlanning
		{
			internal ExecutionPlanning(ResolvedCollectionPlan executionPlan, CollectionMemberMatchSet matches,
				CollectionDependencyPhasePlan dependencyPlan, CollectionConflictImpactPlan impactPlan)
			{
				ExecutionPlan=executionPlan; Matches=matches; DependencyPlan=dependencyPlan; ImpactPlan=impactPlan;
			}
			internal ResolvedCollectionPlan ExecutionPlan; internal CollectionMemberMatchSet Matches;
			internal CollectionDependencyPhasePlan DependencyPlan; internal CollectionConflictImpactPlan ImpactPlan;
		}
	}
}
