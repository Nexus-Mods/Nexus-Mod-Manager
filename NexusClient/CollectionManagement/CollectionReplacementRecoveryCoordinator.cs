using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies the verified C8.7 recovery boundary reached after native-state reconciliation.</summary>
	public enum CollectionReplacementRecoveryStatus
	{
		Unknown = 0,
		OutgoingRemovalBarrierReady = 1,
		IncomingNativeWorkReady = 2,
		RolledBack = 3,
		RecoveryRequired = 4
	}

	/// <summary>Result of one bounded replacement recovery/reconciliation pass.</summary>
	public sealed class CollectionReplacementRecoveryResult
	{
		internal CollectionReplacementRecoveryResult(CollectionReplacementRecoveryStatus status,
			CollectionOperation operation, CollectionReplacementCurrentSetupSnapshot currentState, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionReplacementRecoveryStatus), status) || status == CollectionReplacementRecoveryStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			CurrentState = currentState;
			Message = message ?? String.Empty;
		}

		public CollectionReplacementRecoveryStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionReplacementCurrentSetupSnapshot CurrentState { get; }
		public string Message { get; }
		public bool IsRolledBack { get { return Status == CollectionReplacementRecoveryStatus.RolledBack; } }
		public bool RequiresRecovery { get { return Status == CollectionReplacementRecoveryStatus.RecoveryRequired; } }
	}

	/// <summary>
	/// C8.7 replacement recovery coordinator. It reconciles already-crossed native children from authoritative evidence,
	/// resumes only at verified replacement phase boundaries, and recognizes compensation only when the complete original
	/// reviewed setup is proven restored. It never guesses a missing outgoing reconstruction or replays an ambiguous child.
	/// </summary>
	public sealed class CollectionReplacementRecoveryCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionReplacementOperationCoordinator _replacementOperationCoordinator;
		private readonly CollectionNativeChildRestartReconciliationCoordinator _childRestart;
		private readonly CollectionReplacementIncomingChildReconciliationCoordinator _incomingReconciliation;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionReplacementRecoveryCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			if (artifactStore == null) throw new ArgumentNullException(nameof(artifactStore));
			if (manifestStore == null) throw new ArgumentNullException(nameof(manifestStore));
			_replacementOperationCoordinator = new CollectionReplacementOperationCoordinator(operationStore, planStore,
				artifactStore, referenceStore);
			_childRestart = new CollectionNativeChildRestartReconciliationCoordinator(services, gameStorageService,
				operationStore, planStore, associationStore, manifestStore);
			_incomingReconciliation = new CollectionReplacementIncomingChildReconciliationCoordinator(operationStore, manifestStore);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
		}

		/// <summary>
		/// Reconciles a replacement that crossed the native boundary. The exact reviewed plan is required only so C6.9/C8.6
		/// incoming child evidence can be correlated to the immutable incoming member recipe; it is never rebuilt from guesses.
		/// </summary>
		public async Task<CollectionReplacementRecoveryResult> RecoverAsync(CollectionOperationIdentity operationIdentity,
			ResolvedCollectionPlan reviewedPlan, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (reviewedPlan.Policy.Kind != CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup)
				throw new ArgumentException("C8.7 requires the exact replacement reviewed plan.", nameof(reviewedPlan));
			if (_services.ModManager == null)
				throw new InvalidOperationException("C8.7 replacement recovery requires the active native ModManager.");
			cancellationToken.ThrowIfCancellationRequested();

			CollectionOperation operation = RequireOperation(operationIdentity, reviewedPlan);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(operation.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the replacement recovery journal.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				try
				{
					_authorityValidator.ValidateAndReload(lease, authority, paths);
					operation = RequireOperation(operationIdentity, reviewedPlan);
					CollectionReplacementReviewedIntent intent = _replacementOperationCoordinator.GetReviewedIntent(operationIdentity, reviewedPlan.Identity);
					// Integrity-check every mandatory operation-owned recovery input before interpreting native reality.
					_replacementOperationCoordinator.GetRecoveryBoundary(operationIdentity, reviewedPlan.Identity);

					CollectionReplacementCurrentSetupSnapshot current = CaptureCurrent(operation.Target);
					if (OriginalReviewedStateIsRestored(intent, current))
						return CompleteVerifiedRollback(operation, current, "The complete original reviewed setup is already authoritatively restored.");

					operation = SaveOperation(operation, CollectionOperationPhase.Recovering, CollectionOperationResultState.Pending,
						operation.NativeChildren);

					operation = ReconcileOutgoingChildren(operation, intent);
					if (operation.Phase == CollectionOperationPhase.RecoveryRequired ||
						operation.ResultState == CollectionOperationResultState.RecoveryRequired || operation.HasUnknownNativeDurability)
						return RequireRecovery(operation, CaptureCurrent(operation.Target),
							"At least one outgoing replacement child remains ambiguous after authoritative reload.");

					while (HasUnreconciledIncomingChild(operation))
					{
						cancellationToken.ThrowIfCancellationRequested();
						CollectionNativeChildRestartReconciliationResult restarted = await _childRestart
							.ReconcileReplacementIncomingAsync(operation.Identity, paths, cancellationToken).ConfigureAwait(true);
						ResolvedCollectionPlan executionPlan = RebindState(reviewedPlan, restarted.NativeState.Fingerprint);
						operation = _incomingReconciliation.ReconcileRestartedChild(restarted, executionPlan);
						if (operation.HasUnknownNativeDurability)
							return RequireRecovery(operation, CaptureCurrent(operation.Target),
								"An incoming replacement child could not be classified as committed or rolled back from retained evidence.");
					}

					_authorityValidator.ValidateAndReload(lease, authority, paths);
					current = CaptureCurrent(operation.Target);
					if (OriginalReviewedStateIsRestored(intent, current))
						return CompleteVerifiedRollback(operation, current,
							"Replacement compensation/native recovery restored the complete exact pre-replacement setup.");

					bool hasIncomingProgress = operation.NativeChildren.Any(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
						x.HasCrossedNativeBoundary);
					if (!AllReviewedOutgoingRemovalsHaveCommittedChildren(operation, intent))
						return RequireRecovery(operation, current,
							"The exact reviewed outgoing removal set is not fully represented by verified committed deactivation children; C8.7 will not invent missing removal lineage.");

					if (!hasIncomingProgress)
					{
						if (!AllReviewedOutgoingRemovalsAreAbsent(intent, current.NativeState))
							return RequireRecovery(operation, current,
								"The reviewed outgoing removal set is only partially reflected in authoritative native state; C8.7 will not invent missing compensation or removal work.");
						operation = SaveOperation(operation, CollectionOperationPhase.OutgoingRemovalVerified,
							CollectionOperationResultState.Pending, operation.NativeChildren);
						return new CollectionReplacementRecoveryResult(CollectionReplacementRecoveryStatus.OutgoingRemovalBarrierReady,
							operation, current, "Outgoing replacement removals are verified; resume from the C8.5 post-removal barrier.");
					}

					operation = SaveOperation(operation, CollectionOperationPhase.InstallingIncomingNativeChildren,
						CollectionOperationResultState.Pending, operation.NativeChildren);
					return new CollectionReplacementRecoveryResult(CollectionReplacementRecoveryStatus.IncomingNativeWorkReady,
						operation, current, "Crossed incoming children are reconciled; C8.6 may resume from this verified safe boundary.");
				}
				catch
				{
					TryMarkRecoveryRequired(operationIdentity);
					throw;
				}
			}
		}

		/// <summary>
		/// Releases only operation-owned retained recovery references after a verified terminal commit or rollback.
		/// Capture-owned Local Collection references are intentionally unaffected.
		/// </summary>
		public int ReleaseTerminalRecoveryReferences(CollectionOperationIdentity operationIdentity)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || !operation.IsTerminal ||
				(operation.ResultState != CollectionOperationResultState.Committed && operation.ResultState != CollectionOperationResultState.RolledBack))
				throw new InvalidOperationException("Replacement recovery references may be released only after verified terminal commit or compensation.");
			return _referenceStore.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Operation,
				operation.Identity.OperationId.ToString("D"));
		}

		private CollectionOperation ReconcileOutgoingChildren(CollectionOperation operation, CollectionReplacementReviewedIntent intent)
		{
			CollectionReplacementCurrentSetupSnapshot state = CaptureCurrent(operation.Target);
			var removalKeys = intent.NativeApprovals.Where(x => x.Decision == CollectionReplacementNativeDecision.Remove)
				.Select(x => x.NativeModKey).ToList();
			foreach (CollectionNativeChildOperation child in operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.Deactivate &&
				x.HasCrossedNativeBoundary && !x.IsReconciled).OrderBy(x => x.Sequence).ToList())
			{
				string nativeKey = ResolveOutgoingNativeKey(operation.Identity, child, removalKeys);
				NativeModInstanceIdentity identity = new NativeModInstanceIdentity(operation.Target, nativeKey);
				bool absent = IsNativeModFullyAbsent(state.NativeState, identity);
				ModOperationResult result = child.NativeResult;
				if (result != null && result.Durability == ModOperationDurability.VerifiedCommitted)
				{
					// Preserve the historical verified child outcome even when a later incoming child recreated the same native identity.
				}
				else if (absent)
				{
					ModOperationReportedStatus status = result == null ? ModOperationReportedStatus.Succeeded : result.ReportedStatus;
					result = new ModOperationResult(child.NativeOperation, status, ModOperationDurability.VerifiedCommitted,
						result == null ? "C8.7 verified the outgoing native instance and managed effects are absent after restart." : result.Message);
				}
				else if (result == null || (result.Durability != ModOperationDurability.VerifiedRolledBack &&
					result.Durability != ModOperationDurability.NotStarted))
				{
					return SaveOperation(operation, CollectionOperationPhase.RecoveryRequired,
						CollectionOperationResultState.RecoveryRequired, operation.NativeChildren);
				}

				var reconciled = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
					child.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, result);
				operation = ReplaceChild(operation, reconciled);
				_operationStore.SaveOperation(operation);
				state = CaptureCurrent(operation.Target);
			}
			return _operationStore.GetOperation(operation.Identity) ?? operation;
		}

		private CollectionReplacementRecoveryResult CompleteVerifiedRollback(CollectionOperation operation,
			CollectionReplacementCurrentSetupSnapshot current, string message)
		{
			var children = new List<CollectionNativeChildOperation>();
			foreach (CollectionNativeChildOperation child in operation.NativeChildren)
			{
				if (!child.HasCrossedNativeBoundary)
				{
					children.Add(child);
					continue;
				}
				ModOperationResult result = child.NativeResult;
				if (result == null || result.Durability == ModOperationDurability.Unknown)
				{
					ModOperationReportedStatus status = result == null ? ModOperationReportedStatus.NoOp : result.ReportedStatus;
					result = new ModOperationResult(child.NativeOperation, status,
						ModOperationDurability.VerifiedRolledBack, "C8.7 verified the complete reviewed pre-replacement setup is restored.");
				}
				else if (result.Durability == ModOperationDurability.VerifiedCommitted)
				{
					// A committed child may have been compensated by later native recovery. The aggregate exact preimage is authoritative.
					result = new ModOperationResult(child.NativeOperation, result.ReportedStatus,
						ModOperationDurability.VerifiedCommitted, result.Message);
				}
				children.Add(new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action,
					child.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, result));
			}
			operation = SaveOperation(operation, CollectionOperationPhase.Completed,
				CollectionOperationResultState.RolledBack, children);
			ReleaseTerminalRecoveryReferences(operation.Identity);
			return new CollectionReplacementRecoveryResult(CollectionReplacementRecoveryStatus.RolledBack, operation, current, message);
		}

		private CollectionReplacementRecoveryResult RequireRecovery(CollectionOperation operation,
			CollectionReplacementCurrentSetupSnapshot current, string message)
		{
			operation = SaveOperation(operation, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, operation.NativeChildren);
			return new CollectionReplacementRecoveryResult(CollectionReplacementRecoveryStatus.RecoveryRequired,
				operation, current, message);
		}

		private void TryMarkRecoveryRequired(CollectionOperationIdentity identity)
		{
			try
			{
				CollectionOperation operation = _operationStore.GetOperation(identity);
				if (operation != null && operation.Kind == CollectionOperationKind.ReplaceCurrentManagedSetup && !operation.IsTerminal &&
					(operation.Phase != CollectionOperationPhase.RecoveryRequired || operation.ResultState != CollectionOperationResultState.RecoveryRequired))
					SaveOperation(operation, CollectionOperationPhase.RecoveryRequired,
						CollectionOperationResultState.RecoveryRequired, operation.NativeChildren);
			}
			catch
			{
				// Preserve the primary native recovery failure. The durable journal is retried on the next startup pass.
			}
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan reviewedPlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(reviewedPlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(reviewedPlan.Revision) || !operation.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("C8.7 requires the active exact replacement operation and reviewed plan.");
			return operation;
		}

		private CollectionReplacementCurrentSetupSnapshot CaptureCurrent(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			return new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				manager.VirtualModActivator, _services.PluginManager, manager.GameMode, _associationStore)
				.CaptureReplacementCurrentSetup(target);
		}

		private static bool OriginalReviewedStateIsRestored(CollectionReplacementReviewedIntent intent,
			CollectionReplacementCurrentSetupSnapshot current)
		{
			try
			{
				intent.ValidateCurrentSetup(current);
				return true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private static bool AllReviewedOutgoingRemovalsHaveCommittedChildren(CollectionOperation operation,
			CollectionReplacementReviewedIntent intent)
		{
			foreach (CollectionReplacementNativeApproval approval in intent.NativeApprovals.Where(x => x.Decision == CollectionReplacementNativeDecision.Remove))
			{
				CollectionMemberKey expectedKey = CollectionReplacementOutgoingRemovalCoordinator.CreateRemovalJournalKey(
					operation.Identity, approval.NativeModKey);
				List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x => x.Action == CollectionNativeChildAction.Deactivate &&
					x.Member.MemberKey.Equals(expectedKey)).ToList();
				if (children.Count != 1 || !children[0].IsReconciled || !children[0].HasVerifiedCommittedNativeState)
					return false;
			}
			return true;
		}

		private static bool AllReviewedOutgoingRemovalsAreAbsent(CollectionReplacementReviewedIntent intent,
			CollectionNativeStateIndex state)
		{
			foreach (CollectionReplacementNativeApproval approval in intent.NativeApprovals.Where(x => x.Decision == CollectionReplacementNativeDecision.Remove))
			{
				if (!IsNativeModFullyAbsent(state, new NativeModInstanceIdentity(state.Target, approval.NativeModKey)))
					return false;
			}
			return true;
		}

		private static bool IsNativeModFullyAbsent(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			string key = nativeMod.NativeModKey;
			return !state.Mods.ContainsKey(nativeMod) && !state.FilesByOwnerKey.ContainsKey(key) &&
				!state.IniEditsByOwnerKey.ContainsKey(key) && !state.GameValuesByOwnerKey.ContainsKey(key);
		}

		private static string ResolveOutgoingNativeKey(CollectionOperationIdentity operationIdentity,
			CollectionNativeChildOperation child, IEnumerable<string> candidateKeys)
		{
			List<string> matches = candidateKeys.Where(x => CollectionReplacementOutgoingRemovalCoordinator
				.CreateRemovalJournalKey(operationIdentity, x).Equals(child.Member.MemberKey)).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The interrupted outgoing replacement child cannot be correlated to exactly one reviewed native removal.");
			return matches[0];
		}

		private static bool HasUnreconciledIncomingChild(CollectionOperation operation)
		{
			return operation.NativeChildren.Any(x => x.Action == CollectionNativeChildAction.ActivateOrReinstall &&
				x.HasCrossedNativeBoundary && !x.IsReconciled);
		}

		private CollectionOperation ReplaceChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			return updated;
		}

		private CollectionOperation SaveOperation(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result, IEnumerable<CollectionNativeChildOperation> children)
		{
			if (operation.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, children);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity) ?? updated;
		}

		private static ResolvedCollectionPlan RebindState(ResolvedCollectionPlan reviewedPlan,
			CollectionCurrentStateFingerprint fingerprint)
		{
			return new ResolvedCollectionPlan(reviewedPlan.Identity, reviewedPlan.Target, reviewedPlan.Policy, fingerprint,
				reviewedPlan.CapabilityReport, reviewedPlan.SelectedMembers);
		}
	}
}
