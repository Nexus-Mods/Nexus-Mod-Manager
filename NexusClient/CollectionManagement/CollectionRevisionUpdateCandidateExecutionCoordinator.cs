using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.CollectionManagement
{
	public enum CollectionRevisionUpdateCandidateExecutionStatus
	{
		Unknown = 0,
		Completed = 1,
		ExplicitReviewRequired = 2,
		RecoveryRequired = 3,
		NativeLaneBusy = 4
	}

	public sealed class CollectionRevisionUpdateCandidateExecutionResult
	{
		internal CollectionRevisionUpdateCandidateExecutionResult(CollectionRevisionUpdateCandidateExecutionStatus status,
			CollectionOperation operation, string message, CollectionRevisionUpdateCandidateExecutionPlanning executionPlanning = null,
			CollectionRevisionUpdatePreparationBatch preparation = null)
		{
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateCandidateExecutionStatus), status) ||
				status == CollectionRevisionUpdateCandidateExecutionStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Message = message ?? String.Empty;
			ExecutionPlanning = executionPlanning;
			Preparation = preparation;
		}

		public CollectionRevisionUpdateCandidateExecutionStatus Status { get; }
		public CollectionOperation Operation { get; }
		public string Message { get; }
		internal CollectionRevisionUpdateCandidateExecutionPlanning ExecutionPlanning { get; }
		internal CollectionRevisionUpdatePreparationBatch Preparation { get; }
		public bool IsCompleted { get { return Status == CollectionRevisionUpdateCandidateExecutionStatus.Completed; } }
	}

	/// <summary>
	/// C10.6 executes candidate activate/reinstall children only after C10.4 reached a verified old-effect safe boundary.
	/// It reuses the C6 native child recovery pipeline and deliberately stops before final winner/override/association publication.
	/// </summary>
	public sealed class CollectionRevisionUpdateCandidateExecutionCoordinator
	{
		private readonly ServiceManager _services;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionNativeStateReader _nativeStateReader;
		private readonly CollectionRevisionUpdateNativeRecipePreparationService _recipePreparation;
		private readonly CollectionRevisionUpdateCandidateExecutionPlanner _executionPlanner;
		private readonly CollectionNativeChildPreparationCoordinator _childPreparation;
		private readonly CollectionNativeChildExecutionCoordinator _childExecution;
		private readonly CollectionNativeChildVerificationCoordinator _childVerification;
		private readonly CollectionNativeChildRestartReconciliationCoordinator _childRestart;
		private readonly CollectionRevisionUpdateCandidateChildReconciliationCoordinator _childReconciliation;

		public CollectionRevisionUpdateCandidateExecutionCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsNativeChildRecoveryManifestStore manifestStore,
			CollectionRevisionUpdateNativeRecipePreparationService recipePreparation)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			if (gameStorageService == null) throw new ArgumentNullException(nameof(gameStorageService));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			if (associationStore == null) throw new ArgumentNullException(nameof(associationStore));
			if (artifactStore == null) throw new ArgumentNullException(nameof(artifactStore));
			if (referenceStore == null) throw new ArgumentNullException(nameof(referenceStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_recipePreparation = recipePreparation ?? throw new ArgumentNullException(nameof(recipePreparation));
			if (_services.ModManager == null) throw new InvalidOperationException("C10.6 requires the live ModManager service.");
			_nativeStateReader = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, associationStore);
			_executionPlanner = new CollectionRevisionUpdateCandidateExecutionPlanner();
			_childPreparation = new CollectionNativeChildPreparationCoordinator(operationStore, planStore, artifactStore, referenceStore, manifestStore);
			_childExecution = new CollectionNativeChildExecutionCoordinator(services, gameStorageService, operationStore, planStore, associationStore, manifestStore);
			_childVerification = new CollectionNativeChildVerificationCoordinator(services, gameStorageService, operationStore, planStore, associationStore, manifestStore);
			_childRestart = new CollectionNativeChildRestartReconciliationCoordinator(services, gameStorageService, operationStore, planStore, associationStore, manifestStore);
			_childReconciliation = new CollectionRevisionUpdateCandidateChildReconciliationCoordinator(operationStore, manifestStore);
		}

		public async Task<CollectionRevisionUpdateCandidateExecutionResult> ExecuteAsync(
			CollectionRevisionUpdatePreparationBatch preparedBatch, CollectionRevisionUpdateObsoleteEffectResult obsoleteResult,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (preparedBatch == null) throw new ArgumentNullException(nameof(preparedBatch));
			if (obsoleteResult == null) throw new ArgumentNullException(nameof(obsoleteResult));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!obsoleteResult.IsVerified && !HasDurablyPassedObsoleteBoundary(obsoleteResult.Operation))
				throw new InvalidOperationException("Candidate revision installation requires the verified obsolete-effect boundary before it can continue.");
			if (!obsoleteResult.Operation.Identity.Equals(preparedBatch.Operation.Identity) ||
				obsoleteResult.Operation.PlanIdentity == null || !obsoleteResult.Operation.PlanIdentity.Equals(preparedBatch.CurrentPlan.NewPlan.Identity))
				throw new ArgumentException("The C10.3/C10.4 inputs do not belong to the same exact revision-update operation.");
			cancellationToken.ThrowIfCancellationRequested();

			CollectionRevisionUpdateOverridePreservationPlan preservation =
				new CollectionRevisionUpdateOverridePreservationPlanner().RequireQualified(preparedBatch.ReviewedIntent, preparedBatch.CurrentPlan);
			CollectionOperation operation = RequireOperation(preparedBatch.Operation.Identity, preparedBatch.CurrentPlan.NewPlan);
			if (operation.Phase == CollectionOperationPhase.CandidateRevisionChildrenVerified)
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.Completed, operation,
					"Candidate revision native children are already verified.");
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired || operation.Phase == CollectionOperationPhase.Recovering ||
				operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
					"Revision update recovery must reconcile the crossed native boundary before candidate execution continues.");
			if (operation.NativeChildren.Any(x => x.HasCrossedNativeBoundary && !x.IsReconciled))
			{
				operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
					"A candidate native child crossed the native boundary without reconciliation; restart/evidence recovery is required.");
			}

			HashSet<CollectionMemberKey> initiallyCommitted = GetCommittedCandidateKeys(operation);
			CollectionNativeStateIndex initialState = _nativeStateReader.Capture(preparedBatch.CurrentPlan.NewPlan.Target);
			CollectionCurrentStateFingerprint expectedBoundary = GetExpectedSafeBoundaryFingerprint(operation, obsoleteResult);
			if (!initialState.Fingerprint.Equals(expectedBoundary))
			{
				if (operation.HasCrossedNativeBoundary)
				{
					operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
						"Authoritative native state no longer equals the latest verified revision-update safe boundary.");
				}
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation,
					"Native state changed after the C10.4 observation; candidate preparation must be reviewed again before mutation.");
			}

			Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> effective;
			CollectionRevisionUpdatePreparationBatch effectiveBatch = preparedBatch;
			if (CanReusePreparedRecipesAtBoundary(preparedBatch, initialState))
			{
				effective = GetReviewedPreparedRecipes(preparedBatch, preservation, initiallyCommitted);
			}
			else if (initiallyCommitted.Count == 0)
			{
				try
				{
					effective = ReprepareAgainstBoundary(preparedBatch, initialState, preservation, initiallyCommitted, cancellationToken);
					effectiveBatch = RebindPreparationAtBoundary(preparedBatch, operation, initialState.Fingerprint, effective);
				}
				catch (InvalidOperationException ex)
				{
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation, ex.Message);
				}
			}
			else
				effective = GetReviewedPreparedRecipes(preparedBatch, preservation, initiallyCommitted);

			CollectionRevisionUpdateCandidateExecutionPlanning initialPlanning = _executionPlanner.Build(preparedBatch.CurrentPlan,
				initialState, preservation, preparedBatch.Members, effective.Values, initiallyCommitted, cancellationToken);
			if (!initialPlanning.IsReady)
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation,
					"The revision change is blocked by the member, dependency or file/plugin findings listed below.", initialPlanning, effectiveBatch);
			if (RequiresDurableFileWinnerReconciliation(initialPlanning.ImpactPlan))
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation,
					"This revision change needs file-provider reconciliation that its approved review does not support. No candidate mod was installed.", initialPlanning, effectiveBatch);

			if (operation.Phase == CollectionOperationPhase.ObsoleteRevisionEffectsVerified)
				operation = SavePhase(operation, CollectionOperationPhase.InstallingCandidateRevisionChildren, CollectionOperationResultState.Pending);
			else if (operation.Phase != CollectionOperationPhase.InstallingCandidateRevisionChildren)
				throw new InvalidOperationException("C10.6 requires the verified obsolete-effect boundary or an existing candidate-child phase.");

			while (true)
			{
				cancellationToken.ThrowIfCancellationRequested();
				operation = RequireOperation(preparedBatch.Operation.Identity, preparedBatch.CurrentPlan.NewPlan);
				HashSet<CollectionMemberKey> committed = GetCommittedCandidateKeys(operation);
				CollectionNativeStateIndex state = _nativeStateReader.Capture(preparedBatch.CurrentPlan.NewPlan.Target);
				CollectionRevisionUpdateCandidateExecutionPlanning planning = _executionPlanner.Build(preparedBatch.CurrentPlan,
					state, preservation, preparedBatch.Members, effective.Values, committed, cancellationToken);
				if (!planning.IsReady)
				{
					if (committed.Count == 0)
						return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation,
							"The revision change is blocked by the member, dependency or file/plugin findings listed below.", planning, effectiveBatch);
					operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
						"The remaining revision changes are blocked after verified installation progress. Review the findings listed below.", planning, effectiveBatch);
				}
				if (RequiresDurableFileWinnerReconciliation(planning.ImpactPlan))
				{
					if (committed.Count == 0)
						return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation,
							"This revision change needs file-provider reconciliation that its approved review does not support.", planning, effectiveBatch);
					operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
						"The remaining revision changes need file-provider reconciliation after verified installation progress.", planning, effectiveBatch);
				}

				double? nextMutationPhase = GetNextMutationPhase(planning.DependencyPlan, operation);
				double? committedPhase = GetHighestCommittedCandidatePhase(planning.ExecutionPlan, operation);
				if (nextMutationPhase.HasValue && committedPhase.HasValue && nextMutationPhase.Value > committedPhase.Value &&
					!CanReusePreparedRecipesAtBoundary(effectiveBatch, state))
				{
					if (!state.Fingerprint.Equals(GetExpectedSafeBoundaryFingerprint(operation, obsoleteResult)))
					{
						operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
						return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
							"The game setup changed after the completed installation phase. NMM must verify that change before installing the remaining mods.", null, effectiveBatch);
					}
					try
					{
						// Earlier phases are now visible to installer conditions. Rebuild only unfinished recipes and
						// retain the original consent only when their prepared identity and exact effects are unchanged.
						effective = ReprepareAgainstBoundary(effectiveBatch, state, preservation, committed, cancellationToken);
						effectiveBatch = RebindPreparationAtBoundary(effectiveBatch, operation, state.Fingerprint, effective);
					}
					catch (InvalidOperationException ex)
					{
						return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation, ex.Message, null, effectiveBatch);
					}
					// Rebuild matches/dependencies/impacts from the refreshed preparation before preparing a native child.
					continue;
				}

				CollectionNativeChildPreparationResult child;
				try
				{
					child = _childPreparation.PrepareNextForRevisionUpdate(
						operation.Identity, planning.ExecutionPlan, planning.Matches, planning.DependencyPlan, planning.ImpactPlan,
						state, effective.Values.Select(x => x.EffectPreview), preparedBatch.Members.Where(x => x.VerifiedArchive != null)
							.Select(x => x.VerifiedArchive), paths.InstallInfoPath);
				}
				catch (InvalidOperationException ex)
				{
					if (committed.Count == 0)
						return Result(CollectionRevisionUpdateCandidateExecutionStatus.ExplicitReviewRequired, operation, ex.Message, null, effectiveBatch);
					operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
						"The authoritative safe boundary changed after a candidate child committed: " + ex.Message, null, effectiveBatch);
				}
				if (child == null)
					break;

				PreparedCollectionNativeRecipe prepared;
				if (!effective.TryGetValue(child.Child.Member.MemberKey, out prepared))
					throw new InvalidOperationException("The next C10.6 native child is missing its exact execution-boundary prepared recipe.");
				ModInstallationRecipeInput childRecipe = prepared.RecipeInput.ForOperationIdentity(child.Child.NativeOperation);
				CollectionNativeChildExecutionResult execution = await _childExecution.SubmitPreparedRevisionUpdateChildAsync(
					operation.Identity, planning.ExecutionPlan, planning.ImpactPlan, prepared.EffectPreview, childRecipe,
					paths, cancellationToken).ConfigureAwait(false);
				if (execution == null)
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.NativeLaneBusy,
						RequireOperation(operation.Identity, preparedBatch.CurrentPlan.NewPlan),
						"The shared native operation lane is busy; the prepared candidate child has not started.", null, effectiveBatch);

				CollectionNativeChildVerificationResult verification;
				try
				{
					verification = await _childVerification.VerifySubmittedRevisionUpdateChildAsync(execution,
						planning.ExecutionPlan, prepared.EffectPreview, paths).ConfigureAwait(false);
				}
				catch
				{
					MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
					throw;
				}
				if (verification.RequiresRecovery)
				{
					// Unknown durability must remain unreconciled. Preserve the verifier's member-specific evidence instead
					// of masking it with the known-terminal-only reconciliation gate, or allowing another native submission.
					operation = MarkRecoveryRequired(verification.Operation, preparedBatch.CurrentPlan.NewPlan);
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
						String.IsNullOrWhiteSpace(verification.Child.NativeResult.Message)
							? "NMM could not verify the candidate mod installation. Its native result requires recovery before the revision change can continue."
							: verification.Child.NativeResult.Message, null, effectiveBatch);
				}
				operation = _childReconciliation.ReconcileVerifiedChild(verification, planning.ExecutionPlan);
				if (!verification.HasVerifiedCommit)
				{
					operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
					return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
						String.IsNullOrWhiteSpace(verification.Child.NativeResult.Message)
							? "A candidate revision native child did not verify as durably committed."
							: verification.Child.NativeResult.Message, null, effectiveBatch);
				}
			}

			operation = RequireOperation(preparedBatch.Operation.Identity, preparedBatch.CurrentPlan.NewPlan);
			HashSet<CollectionMemberKey> finalCommitted = GetCommittedCandidateKeys(operation);
			ValidateAllExpectedMutationsCommitted(preparedBatch, preservation, finalCommitted);
			CollectionNativeStateIndex finalState = _nativeStateReader.Capture(preparedBatch.CurrentPlan.NewPlan.Target);
			CollectionRevisionUpdateCandidateExecutionPlanning finalPlanning = _executionPlanner.Build(preparedBatch.CurrentPlan,
				finalState, preservation, preparedBatch.Members, effective.Values, finalCommitted, cancellationToken);
			if (!finalPlanning.IsReady)
			{
				operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
					"Candidate children committed, but the resulting safe boundary contains the blocking findings listed below.", finalPlanning, effectiveBatch);
			}
			if (RequiresDurableFileWinnerReconciliation(finalPlanning.ImpactPlan))
			{
				operation = MarkRecoveryRequired(operation, preparedBatch.CurrentPlan.NewPlan);
				return Result(CollectionRevisionUpdateCandidateExecutionStatus.RecoveryRequired, operation,
					"Candidate children committed but final file-provider reconciliation is not supported by this approved revision change.", finalPlanning, effectiveBatch);
			}

			operation = SavePhase(operation, CollectionOperationPhase.CandidateRevisionChildrenVerified,
				CollectionOperationResultState.Pending);
			return Result(CollectionRevisionUpdateCandidateExecutionStatus.Completed, operation,
				"All required candidate activate/reinstall children are durably verified. Final override/winner/effect verification and revision publication remain pending.", null, effectiveBatch);
		}

		/// <summary>Runs the existing C6.9 evidence classifier for one restart-ambiguous candidate child without replaying it.</summary>
		public async Task<CollectionOperation> ReconcileInterruptedCandidateChildAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan candidatePlan, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (candidatePlan == null) throw new ArgumentNullException(nameof(candidatePlan));
			CollectionNativeChildRestartReconciliationResult result = await _childRestart.ReconcileRevisionUpdateIncomingAsync(
				operationIdentity, paths, cancellationToken).ConfigureAwait(false);
			if (result.Child.NativeResult == null || result.Child.NativeResult.Durability == Nexus.Client.ModManagement.Operations.ModOperationDurability.Unknown)
				return result.Operation;
			ResolvedCollectionPlan executionPlan = CollectionRevisionUpdateCandidateExecutionPlanner.RebindState(candidatePlan,
				result.NativeState.Fingerprint);
			CollectionOperation reconciled = _childReconciliation.ReconcileRestartedChild(result, executionPlan);
			Nexus.Client.ModManagement.Operations.ModOperationDurability durability = result.Child.NativeResult.Durability;
			if (durability == Nexus.Client.ModManagement.Operations.ModOperationDurability.VerifiedCommitted ||
				durability == Nexus.Client.ModManagement.Operations.ModOperationDurability.VerifiedRolledBack ||
				durability == Nexus.Client.ModManagement.Operations.ModOperationDurability.NotStarted)
			{
				// A committed child advances the safe boundary. A verified rollback/not-started result proves that the
				// exact pre-child boundary still holds, so a later explicit continuation may create a fresh attempt.
				return SavePhase(reconciled, CollectionOperationPhase.InstallingCandidateRevisionChildren, CollectionOperationResultState.Pending);
			}
			return MarkRecoveryRequired(reconciled, candidatePlan);
		}

		/// <summary>
		/// Recognizes the durable candidate-install phase as proof that C10.4 was already crossed successfully.
		/// Every persisted obsolete deactivation must still be reconciled and verified committed. This is used only
		/// when reconstructing the C10.4 result after restart; it does not allow skipping C10.4 for a fresh operation.
		/// </summary>
		internal static bool HasDurablyPassedObsoleteBoundary(CollectionOperation operation)
		{
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision ||
				operation.ResultState != CollectionOperationResultState.Pending ||
				operation.Phase != CollectionOperationPhase.InstallingCandidateRevisionChildren)
				return false;
			return !operation.NativeChildren.Any(x => x.Action == CollectionNativeChildAction.Deactivate &&
				(!x.IsReconciled || !x.HasVerifiedCommittedNativeState));
		}

		/// <summary>
		/// Reuses an in-memory C10 preparation only when it was produced from the exact authoritative state now observed.
		/// This removes the duplicate full archive/recipe pass performed by post-mutation resume while preserving fail-closed
		/// re-preparation whenever the state boundary differs.
		/// </summary>
		internal static bool CanReusePreparedRecipesAtBoundary(CollectionRevisionUpdatePreparationBatch batch,
			CollectionNativeStateIndex currentState)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			return batch.PreparedAgainstStateFingerprint != null &&
				batch.PreparedAgainstStateFingerprint.Equals(currentState.Fingerprint);
		}

		/// <summary>
		/// Carries an execution-boundary preparation forward in memory so a user recheck at the same unchanged safe boundary
		/// does not reopen and translate every candidate archive again. Rows not present in the effective set are deliberately
		/// cleared rather than mislabeled as having been prepared against this boundary.
		/// </summary>
		internal static CollectionRevisionUpdatePreparationBatch RebindPreparationAtBoundary(
			CollectionRevisionUpdatePreparationBatch batch, CollectionOperation operation,
			CollectionCurrentStateFingerprint boundaryFingerprint,
			IDictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> effectiveRecipes)
		{
			if (batch == null) throw new ArgumentNullException(nameof(batch));
			if (operation == null) throw new ArgumentNullException(nameof(operation));
			if (boundaryFingerprint == null) throw new ArgumentNullException(nameof(boundaryFingerprint));
			if (effectiveRecipes == null) throw new ArgumentNullException(nameof(effectiveRecipes));
			if (!operation.Identity.Equals(batch.Operation.Identity) || operation.PlanIdentity == null ||
				!operation.PlanIdentity.Equals(batch.CurrentPlan.NewPlan.Identity))
				throw new ArgumentException("Execution-boundary preparation must remain attached to the exact revision-update operation.", nameof(operation));

			var states = new List<CollectionRevisionUpdatePreparationMemberState>(batch.Members.Count);
			foreach (CollectionRevisionUpdatePreparationMemberState member in batch.Members)
			{
				PreparedCollectionNativeRecipe prepared;
				effectiveRecipes.TryGetValue(member.UpdateMember.MemberKey, out prepared);
				states.Add(new CollectionRevisionUpdatePreparationMemberState(member.UpdateMember, member.Disposition, member.Request,
					member.VerifiedArchive, member.QueueCorrelation, member.PendingAction, member.RestartResult, member.PremiumAvailability, prepared));
			}
			return new CollectionRevisionUpdatePreparationBatch(operation, batch.ReviewedIntent, batch.CurrentPlan, states,
				batch.ArchiveOverwritePolicy, boundaryFingerprint);
		}

		private Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> GetReviewedPreparedRecipes(
			CollectionRevisionUpdatePreparationBatch batch, CollectionRevisionUpdateOverridePreservationPlan preservation,
			ISet<CollectionMemberKey> committed)
		{
			var suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			var result = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe>();
			foreach (CollectionRevisionUpdatePreparationMemberState member in batch.Members)
			{
				if (suppressed.Contains(member.UpdateMember.MemberKey) || committed.Contains(member.UpdateMember.MemberKey))
					continue;
				if (member.VerifiedArchive == null || member.PreparedRecipe == null)
					throw new InvalidOperationException("A C10.6 mutating candidate member is missing its exact reviewed C10.3 preparation input.");
				result.Add(member.UpdateMember.MemberKey, member.PreparedRecipe);
			}
			return result;
		}

		private CollectionCurrentStateFingerprint GetExpectedSafeBoundaryFingerprint(CollectionOperation operation,
			CollectionRevisionUpdateObsoleteEffectResult obsoleteResult)
		{
			CollectionNativeChildOperation latest = operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled && x.HasVerifiedCommittedNativeState)
				.OrderByDescending(x => x.Sequence).FirstOrDefault();
			if (latest == null) return obsoleteResult.FinalState.Fingerprint;
			CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, latest);
			if (manifest == null || manifest.SafeBoundaryStateFingerprint == null)
				throw new InvalidOperationException("The latest committed C10.6 child is missing its durable safe-boundary fingerprint.");
			return manifest.SafeBoundaryStateFingerprint;
		}

		private Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> ReprepareAgainstBoundary(
			CollectionRevisionUpdatePreparationBatch batch, CollectionNativeStateIndex state,
			CollectionRevisionUpdateOverridePreservationPlan preservation, ISet<CollectionMemberKey> committed,
			CancellationToken cancellationToken)
		{
			var suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			var result = new Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe>();
			foreach (CollectionRevisionUpdatePreparationMemberState member in batch.Members)
			{
				if (suppressed.Contains(member.UpdateMember.MemberKey) || committed.Contains(member.UpdateMember.MemberKey))
					continue;
				if (member.VerifiedArchive == null || member.PreparedRecipe == null)
					throw new InvalidOperationException("A C10.6 mutating candidate member is missing its exact C10.3 archive/preparation input.");
				PreparedCollectionNativeRecipe current = _recipePreparation.PrepareAtExecutionBoundary(batch.CurrentPlan,
					member.UpdateMember, member.VerifiedArchive, state, cancellationToken,
					member.PreparedRecipe.InstallContext, member.PreparedRecipe.GameRootArchiveBaseDirectory, member.PreparedRecipe.EffectPreview.InstallRootCorrection);
				RequireUnchangedBoundaryPreparation(member.PreparedRecipe, current);
				result.Add(member.UpdateMember.MemberKey, current);
			}
			return result;
		}

		/// <summary>Retains the approved recipe only when phase-boundary re-preparation produces the same inputs and effects.</summary>
		internal static void RequireUnchangedBoundaryPreparation(PreparedCollectionNativeRecipe reviewed, PreparedCollectionNativeRecipe current)
		{
			if (reviewed == null) throw new ArgumentNullException(nameof(reviewed));
			if (current == null) throw new ArgumentNullException(nameof(current));
			bool unchanged = current.PreparedNativeIdentity.Equals(reviewed.PreparedNativeIdentity) &&
				current.Member.MemberKey.Equals(reviewed.Member.MemberKey) && current.ProviderRecipeIdentity.Equals(reviewed.ProviderRecipeIdentity) &&
				StringComparer.Ordinal.Equals(current.RecipeInput.TargetFingerprint, reviewed.RecipeInput.TargetFingerprint) &&
				current.InstallContext.Method == reviewed.InstallContext.Method && current.InstallContext.InstallRoot == reviewed.InstallContext.InstallRoot &&
				current.Validation.ExpectedContent.ByteLength == reviewed.Validation.ExpectedContent.ByteLength &&
				StringComparer.OrdinalIgnoreCase.Equals(current.Validation.ExpectedContent.Sha256, reviewed.Validation.ExpectedContent.Sha256) &&
				StringComparer.Ordinal.Equals(CollectionReplacementPreparedRecipeApproval.ComputeEffectFingerprint(current.EffectPreview),
					CollectionReplacementPreparedRecipeApproval.ComputeEffectFingerprint(reviewed.EffectPreview));
			if (unchanged)
			{
				// The shared effect fingerprint covers destinations, INI/game values and plugin actions. Compare exact
				// file bytes separately so unchanged paths cannot conceal different generated or merged payloads.
				Dictionary<ModDeploymentTarget, CollectionPlannedFileEffect> currentFiles = current.EffectPreview.Files.ToDictionary(x => x.Target);
				unchanged = reviewed.EffectPreview.Files.All(x => currentFiles.ContainsKey(x.Target) &&
					Equals(x.ExpectedContentHash, currentFiles[x.Target].ExpectedContentHash) && x.ExpectedByteLength == currentFiles[x.Target].ExpectedByteLength);
			}
			if (!unchanged)
			{
				string subject = String.IsNullOrWhiteSpace(reviewed.Member.DisplayName) ? reviewed.Member.MemberKey.ToString() : reviewed.Member.DisplayName;
				throw new InvalidOperationException("The remaining installation choices or effects for '" + subject +
					"' changed after earlier mods finished. NMM stopped before installing this mod; its changed preparation needs a new review.");
			}
		}

		internal static bool RequiresDurableFileWinnerReconciliation(CollectionConflictImpactPlan impactPlan)
		{
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			return impactPlan.FileImpacts.Any(x => x != null && (x.PreserveCurrentOwner || x.Writers.Count > 1));
		}

		private static double? GetNextMutationPhase(CollectionDependencyPhasePlan dependencyPlan, CollectionOperation operation)
		{
			var completed = new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled && x.HasVerifiedCommittedNativeState)
				.Select(x => x.Member.MemberKey));
			foreach (CollectionExecutionPhase phase in dependencyPlan.Phases)
				foreach (CollectionPlannedPhaseMember member in phase.Members)
					if (!completed.Contains(member.MemberKey) && member.MatchDisposition != CollectionMemberMatchDisposition.InstalledCompatible)
						return phase.PhaseNumber;
			return null;
		}

		private static double? GetHighestCommittedCandidatePhase(ResolvedCollectionPlan plan, CollectionOperation operation)
		{
			Dictionary<CollectionMemberKey, double> phases = plan.SelectedMembers.ToDictionary(x => x.MemberKey, x => x.InstallationPhase);
			var values = new List<double>();
			foreach (CollectionNativeChildOperation child in operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled && x.HasVerifiedCommittedNativeState))
			{
				double phase;
				if (!phases.TryGetValue(child.Member.MemberKey, out phase))
					throw new InvalidOperationException("A committed C10.6 child no longer belongs to the selected candidate member closure.");
				values.Add(phase);
			}
			return values.Count == 0 ? (double?)null : values.Max();
		}

		private static void ValidateAllExpectedMutationsCommitted(CollectionRevisionUpdatePreparationBatch batch,
			CollectionRevisionUpdateOverridePreservationPlan preservation, ISet<CollectionMemberKey> committed)
		{
			var suppressed = new HashSet<CollectionMemberKey>(preservation.MembersWhoseCandidateMutationIsSuppressed);
			foreach (CollectionRevisionUpdatePreparationMemberState member in batch.Members)
				if (!suppressed.Contains(member.UpdateMember.MemberKey) && !committed.Contains(member.UpdateMember.MemberKey))
					throw new InvalidOperationException("C10.6 cannot close the candidate-child barrier while a required mutating member lacks VerifiedCommitted reconciliation.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan candidatePlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(candidatePlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(candidatePlan.Revision) || !operation.Target.Equals(candidatePlan.Target))
				throw new InvalidOperationException("The C10.6 request does not belong to the active exact revision-update operation.");
			return operation;
		}

		private CollectionOperation MarkRecoveryRequired(CollectionOperation operation, ResolvedCollectionPlan candidatePlan)
		{
			operation = RequireOperation(operation.Identity, candidatePlan);
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired && operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return operation;
			return SavePhase(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
		}

		private CollectionOperation SavePhase(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result)
		{
			if (operation.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, operation.NativeChildren);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private static HashSet<CollectionMemberKey> GetCommittedCandidateKeys(CollectionOperation operation)
		{
			return new HashSet<CollectionMemberKey>(operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.IsReconciled && x.HasVerifiedCommittedNativeState)
				.Select(x => x.Member.MemberKey));
		}

		private static CollectionRevisionUpdateCandidateExecutionResult Result(CollectionRevisionUpdateCandidateExecutionStatus status,
			CollectionOperation operation, string message, CollectionRevisionUpdateCandidateExecutionPlanning executionPlanning = null,
			CollectionRevisionUpdatePreparationBatch preparation = null)
		{
			return new CollectionRevisionUpdateCandidateExecutionResult(status, operation, message, executionPlanning, preparation);
		}
	}
}
