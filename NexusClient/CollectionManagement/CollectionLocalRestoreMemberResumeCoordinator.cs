using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies production restart/resume of the C7.10a Local Collection member phase.</summary>
	public enum CollectionLocalRestoreMemberResumeStatus
	{
		MemberPhaseComplete = 1,
		RecoveryRequired = 2,
		CurrentStateChanged = 3,
		RetainedInputInvalid = 4
	}

	/// <summary>Result of one production dispatch for an incomplete RestoreLocalCapture member phase.</summary>
	public sealed class CollectionLocalRestoreMemberResumeResult
	{
		internal CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus status,
			CollectionOperation operation, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreMemberRehydrationResult rehydration, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionLocalRestoreMemberResumeStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			MemberPhase = memberPhase;
			Rehydration = rehydration;
			Message = message ?? String.Empty;
		}

		public CollectionLocalRestoreMemberResumeStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionLocalRestoreMemberExecutionResult MemberPhase { get; }
		public CollectionLocalRestoreMemberRehydrationResult Rehydration { get; }
		public string Message { get; }
		public bool IsMemberPhaseComplete { get { return Status == CollectionLocalRestoreMemberResumeStatus.MemberPhaseComplete; } }
	}

	/// <summary>
	/// Production C7.10a restart dispatcher: native recovery first, then exact-intent member continuation without creating a new restore operation.
	/// </summary>
	public sealed class CollectionLocalRestoreMemberResumeCoordinator
	{
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionLocalRestoreMemberRehydrator _rehydrator;
		private readonly CollectionLocalRestoreMemberExecutor _executor;

		/// <summary>Creates the production Local Collection member restart/resume coordinator.</summary>
		public CollectionLocalRestoreMemberResumeCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsLocalCaptureStore localCaptureStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(gameStorageService, operationStore,
				new CollectionLocalRestoreMemberRehydrator(services, gameStorageService, operationStore, associationStore,
					localCaptureStore, artifactStore, referenceStore),
				new CollectionLocalRestoreMemberExecutor(services, gameStorageService, operationStore, associationStore,
					artifactStore, referenceStore),
				CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreMemberResumeCoordinator(GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionLocalRestoreMemberRehydrator rehydrator,
			CollectionLocalRestoreMemberExecutor executor, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_rehydrator = rehydrator ?? throw new ArgumentNullException(nameof(rehydrator));
			_executor = executor ?? throw new ArgumentNullException(nameof(executor));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>Reconciles any interrupted native child, then resumes remaining C7.10a work from the durable reviewed intent.</summary>
		public Task<CollectionLocalRestoreMemberResumeResult> ResumeAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths)
		{
			return ResumeAsync(operationIdentity, paths, CancellationToken.None);
		}

		/// <summary>Resumes one exact persisted RestoreLocalCapture member phase without clearing or replacing its operation journal.</summary>
		public async Task<CollectionLocalRestoreMemberResumeResult> ResumeAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));

			CollectionOperation requested = RequireOperation(operationIdentity);
			if (_operationStore.GetIncompleteOperations(requested.Target).Any(x => !x.Identity.Equals(operationIdentity)))
				return new CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus.RecoveryRequired,
					requested, null, null, "Another incomplete Collection operation targets the same game/storage authority; Local restore resume was not started.");

			for (int pass = 0; pass < 4; pass++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CollectionLocalRestoreMemberRehydrationResult rehydration = await _rehydrator
					.RehydrateAsync(operationIdentity, paths, cancellationToken).ConfigureAwait(true);
				switch (rehydration.Status)
				{
					case CollectionLocalRestoreMemberRehydrationStatus.MemberPhaseComplete:
					case CollectionLocalRestoreMemberRehydrationStatus.ReadyToResume:
					{
						CollectionLocalRestoreMemberExecutionResult completed = await _executor
							.ResumeAsync(rehydration, paths, cancellationToken).ConfigureAwait(true);
						return new CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus.MemberPhaseComplete,
							completed.Operation, completed, rehydration,
							"The persisted Local Collection native-member phase was resumed and verified at its durable safe boundary.");
					}
					case CollectionLocalRestoreMemberRehydrationStatus.NativeRecoveryRequired:
						if (!await ReconcileInterruptedChildAsync(operationIdentity, paths, cancellationToken).ConfigureAwait(true))
						{
							CollectionOperation blocked = RequireOperation(operationIdentity);
							return new CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus.RecoveryRequired,
								blocked, null, rehydration,
								"Native recovery could not prove either the committed or rolled-back Local restore child state; no retry was submitted.");
						}
						continue;
					case CollectionLocalRestoreMemberRehydrationStatus.CurrentStateChanged:
						return new CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus.CurrentStateChanged,
							rehydration.Operation, null, rehydration, rehydration.Message);
					case CollectionLocalRestoreMemberRehydrationStatus.RetainedInputInvalid:
						return new CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus.RetainedInputInvalid,
							rehydration.Operation, null, rehydration, rehydration.Message);
					default:
						throw new InvalidOperationException("Unsupported Local restore member rehydration status.");
				}
			}

			CollectionOperation operation = RequireOperation(operationIdentity);
			return new CollectionLocalRestoreMemberResumeResult(CollectionLocalRestoreMemberResumeStatus.RecoveryRequired,
				operation, null, null, "Local restore restart reconciliation did not converge to a safe member boundary.");
		}

		private async Task<bool> ReconcileInterruptedChildAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			CollectionOperation initial = RequireOperation(operationIdentity);
			if (!authority.Target.Equals(initial.Target))
				return false;

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					// Native deployment/VMA recovery is always the first native-state action after restart.
					_authorityValidator.ValidateAndReload(lease, authority, paths);
					CollectionLocalRestoreMemberRehydrationResult state = _rehydrator.RehydrateReloaded(operationIdentity,
						authority, cancellationToken);
					CollectionOperation operation = RequireOperation(operationIdentity);
					List<CollectionNativeChildOperation> ambiguous = operation.NativeChildren
						.Where(x => x.HasCrossedNativeBoundary && !x.IsReconciled).ToList();
					if (ambiguous.Count != 1 || state.CurrentPlan == null || state.CurrentPlan.Issues.Count != 0)
					{
						MarkRecoveryRequired(operation);
						return false;
					}

					CollectionNativeChildOperation child = ambiguous[0];
					ModOperationDurability durability = ClassifyDurability(state, child);
					if (durability == ModOperationDurability.Unknown)
					{
						MarkRecoveryRequired(operation);
						return false;
					}

					operation = WithOperationState(operation, CollectionOperationPhase.Recovering, CollectionOperationResultState.Pending);
					_operationStore.SaveOperation(operation);
					operation = RequireOperation(operationIdentity);
					child = operation.NativeChildren.Single(x => x.Sequence == child.Sequence);

					ModOperationResult prior = child.NativeResult;
					ModOperationReportedStatus reported = prior == null ? ModOperationReportedStatus.Failed : prior.ReportedStatus;
					string message = prior == null
						? "The process restarted after Local restore native submission; durability was reconstructed from reloaded authoritative native state."
						: prior.Message;
					var verified = new ModOperationResult(child.NativeOperation, reported, durability, message);
					var terminal = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
						child.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved, verified);
					operation = SaveChild(operation, terminal);

					var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
						terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
					operation = CompleteRecovery(operation, reconciled, IsRetryableRollback(durability));
					return true;
				}
				catch
				{
					CollectionOperation current = _operationStore.GetOperation(operationIdentity);
					if (current != null && !current.IsTerminal)
						TryMarkRecoveryRequired(current);
					throw;
				}
			}
		}

		internal static ModOperationDurability ClassifyDurability(CollectionLocalRestoreMemberRehydrationResult state,
			CollectionNativeChildOperation child)
		{
			if (state == null || child == null || state.CurrentPlan == null || state.CurrentPlan.Issues.Count != 0)
				return ModOperationDurability.Unknown;

			CollectionLocalRestoreRemovalProgress removal = state.Removals.SingleOrDefault(x => SameChild(x.Child, child));
			if (removal != null)
			{
				bool stillRequiresRemoval = state.CurrentPlan.CurrentNativeKeysToRemove
					.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x, removal.NativeKey));
				ModOperationDurability observed;
				if (stillRequiresRemoval)
					observed = ModOperationDurability.VerifiedRolledBack;
				else if (state.CurrentPlan.Members.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.CurrentNativeKey, removal.NativeKey)))
					observed = ModOperationDurability.Unknown;
				else
					observed = ModOperationDurability.VerifiedCommitted;
				return RespectPersistedDurability(child, observed);
			}

			CollectionLocalRestoreMemberProgress memberProgress = state.Members.SingleOrDefault(x => SameChild(x.Child, child));
			if (memberProgress == null)
				return ModOperationDurability.Unknown;
			CollectionLocalRestoreMemberPlan current = state.CurrentPlan.Members
				.SingleOrDefault(x => x.SnapshotMemberKey.Equals(memberProgress.ReviewedMember.SnapshotMemberKey));
			if (current == null || current.InstallContext.Method != memberProgress.ReviewedMember.InstallContext.Method ||
				current.InstallContext.InstallRoot != memberProgress.ReviewedMember.InstallContext.InstallRoot)
				return ModOperationDurability.Unknown;
			ModOperationDurability memberObserved = ModOperationDurability.Unknown;
			if (current.Action == CollectionLocalRestoreMemberAction.ReuseExistingNative && !String.IsNullOrWhiteSpace(current.CurrentNativeKey))
				memberObserved = ModOperationDurability.VerifiedCommitted;
			else if (current.Action == CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive)
				memberObserved = ModOperationDurability.VerifiedRolledBack;
			return RespectPersistedDurability(child, memberObserved);
		}

		private static ModOperationDurability RespectPersistedDurability(CollectionNativeChildOperation child,
			ModOperationDurability observed)
		{
			ModOperationDurability prior = child.NativeResult == null ? ModOperationDurability.Unknown : child.NativeResult.Durability;
			if (prior == ModOperationDurability.Unknown)
				return observed;
			if (prior == ModOperationDurability.NotStarted)
				return observed == ModOperationDurability.VerifiedRolledBack ? ModOperationDurability.NotStarted : ModOperationDurability.Unknown;
			return prior == observed ? observed : ModOperationDurability.Unknown;
		}

		private static bool IsRetryableRollback(ModOperationDurability durability)
		{
			return durability == ModOperationDurability.VerifiedRolledBack || durability == ModOperationDurability.NotStarted;
		}

		private static bool SameChild(CollectionNativeChildOperation left, CollectionNativeChildOperation right)
		{
			return left != null && right != null && left.Sequence == right.Sequence &&
				left.NativeOperation.OperationId == right.NativeOperation.OperationId &&
				left.NativeOperation.AttemptId == right.NativeOperation.AttemptId;
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			return RequireOperation(updated.Identity);
		}

		/// <summary>Reconciles the interrupted attempt and, after a proven rollback, appends one immutable recovery retry intent atomically.</summary>
		private CollectionOperation CompleteRecovery(CollectionOperation operation, CollectionNativeChildOperation reconciled, bool retry)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == reconciled.Sequence ? reconciled : x).ToList();
			if (retry)
			{
				int nextSequence = children.Count == 0 ? 1 : checked(children.Max(x => x.Sequence) + 1);
				ModOperationIdentity retryIdentity = ModOperationIdentity.CreateNew(ModOperationOrigin.Recovery,
					reconciled.NativeOperation.Fingerprint);
				children.Add(new CollectionNativeChildOperation(nextSequence, reconciled.Member, reconciled.Action, retryIdentity,
					CollectionNativeChildCheckpoint.RecoveryInputsReady, null));
			}

			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1),
				CollectionOperationPhase.ApplyingNativeChildren, CollectionOperationResultState.Pending, children);
			_operationStore.SaveOperation(updated);
			return RequireOperation(updated.Identity);
		}

		private static CollectionOperation WithOperationState(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState state)
		{
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), phase, state, operation.NativeChildren);
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired &&
				operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return;
			_operationStore.SaveOperation(WithOperationState(operation, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired));
		}

		private void TryMarkRecoveryRequired(CollectionOperation operation)
		{
			try { MarkRecoveryRequired(operation); }
			catch { }
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal)
				throw new InvalidOperationException("The active Local Collection restore operation journal is unavailable or no longer mutable.");
			return operation;
		}
	}
}
