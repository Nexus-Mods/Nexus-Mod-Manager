using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>One exact reviewed outgoing native instance to remove during C8.4.</summary>
	internal sealed class CollectionReplacementOutgoingRemovalRequest
	{
		internal CollectionReplacementOutgoingRemovalRequest(CollectionNativeModState nativeMod)
		{
			NativeMod = nativeMod ?? throw new ArgumentNullException(nameof(nativeMod));
			InstallContext = new ModInstallContext(nativeMod.InstallMethod, nativeMod.InstallRoot);
		}

		public CollectionNativeModState NativeMod { get; }
		public ModInstallContext InstallContext { get; }
	}

	/// <summary>Verified result of the C8.4 outgoing-removal phase.</summary>
	public sealed class CollectionReplacementOutgoingRemovalResult
	{
		private readonly ReadOnlyCollection<string> _removedNativeModKeys;

		internal CollectionReplacementOutgoingRemovalResult(CollectionOperation operation,
			CollectionReplacementCurrentSetupSnapshot finalState, IEnumerable<string> removedNativeModKeys)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
			_removedNativeModKeys = new ReadOnlyCollection<string>((removedNativeModKeys ?? throw new ArgumentNullException(nameof(removedNativeModKeys)))
				.OrderBy(x => x, StringComparer.Ordinal).ToList());
		}

		public CollectionOperation Operation { get; }
		public CollectionReplacementCurrentSetupSnapshot FinalState { get; }
		public ReadOnlyCollection<string> RemovedNativeModKeys { get { return _removedNativeModKeys; } }
		public bool IsOutgoingRemovalVerified
		{
			get { return Operation.Phase == CollectionOperationPhase.OutgoingRemovalVerified && Operation.ResultState == CollectionOperationResultState.Pending; }
		}
	}

	/// <summary>
	/// C8.4 executor for the exact outgoing native-removal set approved by C8.3.
	/// </summary>
	/// <remarks>
	/// This coordinator never calls DeleteMod, never runs C6.14/C7 top-level workflows and never publishes association transitions.
	/// Each deactivation is submitted through the existing Collection-owned native seam under one authoritative target lease, then
	/// native state is reloaded and inspected before the child can reconcile. Incoming installation and replacement-environment
	/// consistency remain C8.5/C8.6 work.
	/// </remarks>
	public sealed class CollectionReplacementOutgoingRemovalCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionReplacementOperationCoordinator _replacementOperationCoordinator;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionReplacementOutgoingRemovalCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, associationStore,
				new CollectionReplacementOperationCoordinator(operationStore, planStore, artifactStore, referenceStore),
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionReplacementOutgoingRemovalCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionReplacementOperationCoordinator replacementOperationCoordinator,
			CollectionTargetMutationLeaseManager mutationLeaseManager, CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_replacementOperationCoordinator = replacementOperationCoordinator ?? throw new ArgumentNullException(nameof(replacementOperationCoordinator));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		public Task<CollectionReplacementOutgoingRemovalResult> ExecuteAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, GameStoragePathSet paths)
		{
			return ExecuteAsync(operationIdentity, expectedPlan, paths, CancellationToken.None);
		}

		/// <summary>
		/// Executes only the reviewed outgoing-removal phase. Cancellation is honored before the phase begins and again only at
		/// safe child boundaries; once a native worker is accepted, that worker is always observed to terminal state.
		/// </summary>
		public async Task<CollectionReplacementOutgoingRemovalResult> ExecuteAsync(CollectionOperationIdentity operationIdentity,
			CollectionPlanIdentity expectedPlan, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (expectedPlan == null) throw new ArgumentNullException(nameof(expectedPlan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			cancellationToken.ThrowIfCancellationRequested();

			CollectionOperation initial = RequireOperation(operationIdentity, CollectionOperationPhase.ReadyToApply);
			if (initial.PlanIdentity == null || !initial.PlanIdentity.Equals(expectedPlan))
				throw new InvalidOperationException("The replacement outgoing-removal request does not refer to the exact reviewed plan.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(initial.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the reviewed replacement operation.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				RequireNoOtherIncompleteOperation(initial);
				CollectionReplacementCurrentSetupSnapshot current = CaptureReloadedCurrentSetup(initial.Target);
				CollectionReplacementReviewedIntent intent = _replacementOperationCoordinator.GetReviewedIntent(operationIdentity, expectedPlan);
				List<CollectionReplacementOutgoingRemovalRequest> removals = BuildReviewedRemovalSet(intent, current).ToList();
				CollectionOperation operation = _replacementOperationCoordinator.BeginOutgoingRemoval(operationIdentity, expectedPlan, current);

				try
				{
					int nextSequence = operation.NativeChildren.Count == 0 ? 1 : operation.NativeChildren.Max(x => x.Sequence) + 1;
					foreach (CollectionReplacementOutgoingRemovalRequest removal in removals)
					{
						operation = _operationStore.GetOperation(operation.Identity) ?? operation;
						ThrowIfCancellationRequestedAtChildBoundary(operation, cancellationToken);

						CollectionNativeChildOperation child = CreatePreparedChild(operation, removal, nextSequence++);
						operation = SaveChild(operation, child);

						IBackgroundTaskSet nativeTask = CreateNativeTask(removal, child, rootLease);
						if (nativeTask == null)
						{
							PauseOrRequireRecovery(operation);
							throw new InvalidOperationException("The reviewed outgoing native mod disappeared before its C8.4 deactivation could be submitted.");
						}
						operation = _operationStore.GetOperation(operation.Identity) ?? operation;
						ThrowIfCancellationRequestedAtChildBoundary(operation, cancellationToken);

						bool accepted;
						try
						{
							accepted = _services.ModActivationMonitor.SubmitWhenIdle(nativeTask, () =>
							{
								CollectionOperation currentOperation = RequireOperation(operation.Identity, CollectionOperationPhase.RemovingOutgoingNativeChildren);
								CollectionNativeChildOperation currentChild = RequireChild(currentOperation, child.Sequence,
									CollectionNativeChildCheckpoint.RecoveryInputsReady);
								currentOperation = ReplaceChild(currentOperation, new CollectionNativeChildOperation(currentChild.Sequence,
									currentChild.Member, currentChild.Action, currentChild.NativeOperation,
									CollectionNativeChildCheckpoint.NativeSubmitted, null));
								_operationStore.SaveOperation(currentOperation);
							});
						}
						catch
						{
							PauseOrRequireRecovery(_operationStore.GetOperation(operation.Identity) ?? operation);
							throw;
						}
						if (!accepted)
						{
							PauseOrRequireRecovery(_operationStore.GetOperation(operation.Identity) ?? operation);
							throw new InvalidOperationException("The shared native operation seam rejected the reviewed C8.4 deactivation child before worker start.");
						}

						await WaitForCompletionAsync(nativeTask).ConfigureAwait(true);
						bool exactResult;
						ModOperationResult reported = CaptureReportedResult(nativeTask, child.NativeOperation, out exactResult);
						_authorityValidator.ValidateAndReload(rootLease, authority, paths);
						CollectionReplacementCurrentSetupSnapshot observed = CaptureReloadedCurrentSetup(initial.Target);
						bool absent = IsNativeModFullyAbsent(observed.NativeState, removal.NativeMod.Identity);
						bool present = observed.NativeState.Mods.ContainsKey(removal.NativeMod.Identity);
						ModOperationDurability durability = DetermineDurability(reported, exactResult, absent, present);
						var verifiedResult = new ModOperationResult(child.NativeOperation, reported.ReportedStatus, durability, reported.Message);

						operation = _operationStore.GetOperation(operation.Identity) ?? operation;
						CollectionNativeChildOperation submitted = RequireChild(operation, child.Sequence, CollectionNativeChildCheckpoint.NativeSubmitted);
						var terminal = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
							submitted.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved, verifiedResult);
						operation = ReplaceChild(operation, terminal);
						_operationStore.SaveOperation(operation);

						var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
							terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
						operation = ReplaceChild(operation, reconciled);
						_operationStore.SaveOperation(operation);

						if (durability != ModOperationDurability.VerifiedCommitted)
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("The reviewed outgoing native removal did not verify as durably committed; replacement requires explicit recovery before any incoming work.");
						}
					}

					_authorityValidator.ValidateAndReload(rootLease, authority, paths);
					CollectionReplacementCurrentSetupSnapshot finalState = CaptureReloadedCurrentSetup(initial.Target);
					ValidateFinalOutgoingState(intent, finalState);
					operation = _operationStore.GetOperation(operation.Identity) ?? operation;
					operation = WithOperationState(operation, CollectionOperationPhase.OutgoingRemovalVerified,
						CollectionOperationResultState.Pending);
					_operationStore.SaveOperation(operation);
					return new CollectionReplacementOutgoingRemovalResult(operation, finalState,
						removals.Select(x => x.NativeMod.Identity.NativeModKey));
				}
				catch
				{
					CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
					if (persisted.Phase == CollectionOperationPhase.RemovingOutgoingNativeChildren)
						PauseOrRequireRecovery(persisted);
					throw;
				}
			}
		}

		/// <summary>Builds the exact C8.4 removal set from C8.3 approvals and the still-current reviewed setup.</summary>
		internal static IReadOnlyList<CollectionReplacementOutgoingRemovalRequest> BuildReviewedRemovalSet(
			CollectionReplacementReviewedIntent intent, CollectionReplacementCurrentSetupSnapshot current)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (current == null) throw new ArgumentNullException(nameof(current));
			intent.ValidateCurrentSetup(current);

			var modsByKey = current.NativeState.Mods.Values.ToDictionary(x => x.Identity.NativeModKey, StringComparer.Ordinal);
			var result = new List<CollectionReplacementOutgoingRemovalRequest>();
			foreach (CollectionReplacementNativeApproval approval in intent.NativeApprovals.OrderBy(x => x.NativeModKey, StringComparer.Ordinal))
			{
				CollectionNativeModState nativeMod;
				if (!modsByKey.TryGetValue(approval.NativeModKey, out nativeMod))
					throw new InvalidOperationException("The exact reviewed native setup no longer contains one of its approved replacement instances.");
				if (approval.Decision == CollectionReplacementNativeDecision.Remove)
					result.Add(new CollectionReplacementOutgoingRemovalRequest(nativeMod));
			}
			return new ReadOnlyCollection<CollectionReplacementOutgoingRemovalRequest>(result);
		}

		private IBackgroundTaskSet CreateNativeTask(CollectionReplacementOutgoingRemovalRequest removal,
			CollectionNativeChildOperation child, CollectionTargetMutationLease rootLease)
		{
			IMod nativeMod = ResolveLiveMod(removal.NativeMod.Identity.NativeModKey);
			IBackgroundTaskSet nativeTask = _services.ModManager.CreateCollectionDeactivationOperation(nativeMod,
				_services.ModManager.ActiveMods, child.NativeOperation);
			if (nativeTask == null)
				return null;
			ModInstallerBase installerTask = nativeTask as ModInstallerBase;
			if (installerTask == null || installerTask.OperationIdentity == null || !Matches(installerTask.OperationIdentity, child.NativeOperation))
				throw new InvalidOperationException("The native uninstaller did not retain the exact C8.4 replacement child identity.");
			installerTask.AssignParentMutationLease(rootLease);
			return nativeTask;
		}

		private CollectionNativeChildOperation CreatePreparedChild(CollectionOperation operation,
			CollectionReplacementOutgoingRemovalRequest removal, int sequence)
		{
			if (operation.Revision == null)
				throw new InvalidOperationException("A replacement operation must retain its concrete incoming revision before outgoing native removal.");
			var fingerprint = new ModOperationFingerprint(operation.Target.Fingerprint, removal.InstallContext, null);
			var identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection, fingerprint);
			var member = new CollectionOperationMemberReference(operation.Revision,
				CreateRemovalJournalKey(operation.Identity, removal.NativeMod.Identity.NativeModKey));
			return new CollectionNativeChildOperation(sequence, member, CollectionNativeChildAction.Deactivate, identity,
				CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
		}

		internal static CollectionMemberKey CreateRemovalJournalKey(CollectionOperationIdentity operationIdentity, string nativeModKey)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			nativeModKey = CollectionIdentityValidation.RequireOpaqueToken(nativeModKey, nameof(nativeModKey));
			byte[] bytes = Encoding.UTF8.GetBytes("replacement-remove-v1\n" + operationIdentity.OperationId.ToString("D") + "\n" + nativeModKey);
			using (SHA256 sha = SHA256.Create())
			{
				byte[] hash = sha.ComputeHash(bytes);
				byte[] guidBytes = new byte[16];
				Buffer.BlockCopy(hash, 0, guidBytes, 0, guidBytes.Length);
				return CollectionMemberKey.FromLocal(new Guid(guidBytes));
			}
		}

		private CollectionReplacementCurrentSetupSnapshot CaptureReloadedCurrentSetup(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			return new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				manager.VirtualModActivator, _services.PluginManager, manager.GameMode, _associationStore)
				.CaptureReplacementCurrentSetup(target);
		}

		private IMod ResolveLiveMod(string nativeModKey)
		{
			List<IMod> matches = _services.ModManager.ActiveMods.Where(mod =>
			{
				string key;
				try { key = _services.ModManager.InstallationLog.GetModKey(mod); }
				catch { return false; }
				return StringComparer.Ordinal.Equals(key, nativeModKey);
			}).ToList();
			if (matches.Count != 1)
				throw new InvalidOperationException("The reviewed C8.4 native mod cannot be resolved to exactly one live active mod instance.");
			return matches[0];
		}

		private static void ValidateFinalOutgoingState(CollectionReplacementReviewedIntent intent,
			CollectionReplacementCurrentSetupSnapshot finalState)
		{
			foreach (CollectionReplacementNativeApproval approval in intent.NativeApprovals)
			{
				NativeModInstanceIdentity identity = new NativeModInstanceIdentity(finalState.Target, approval.NativeModKey);
				if (approval.Decision == CollectionReplacementNativeDecision.Remove)
				{
					if (!IsNativeModFullyAbsent(finalState.NativeState, identity))
						throw new InvalidOperationException("C8.4 completed its child journal but authoritative native state still contains a reviewed outgoing managed instance or one of its managed effects.");
				}
				else if (!finalState.NativeState.Mods.ContainsKey(identity))
				{
					throw new InvalidOperationException("Outgoing removal unexpectedly removed a native instance that the exact replacement review required to remain present.");
				}
			}
		}

		private static bool IsNativeModFullyAbsent(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			string key = nativeMod.NativeModKey;
			return !state.Mods.ContainsKey(nativeMod) && !state.FilesByOwnerKey.ContainsKey(key) &&
				!state.IniEditsByOwnerKey.ContainsKey(key) && !state.GameValuesByOwnerKey.ContainsKey(key);
		}

		private void RequireNoOtherIncompleteOperation(CollectionOperation current)
		{
			if (_operationStore.GetIncompleteOperations(current.Target).Any(x => x.Identity.OperationId != current.Identity.OperationId))
				throw new InvalidOperationException("Replacement outgoing removal cannot begin while another Collection operation for this target remains incomplete.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionOperationPhase phase)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.ReplaceCurrentManagedSetup || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != phase)
				throw new InvalidOperationException("The replacement operation is not at the required C8.4 journal phase.");
			return operation;
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			List<CollectionNativeChildOperation> children = operation.NativeChildren.ToList();
			children.Add(child);
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			return updated;
		}

		private static CollectionOperation ReplaceChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
		}

		private static CollectionOperation WithOperationState(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result)
		{
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), phase, result,
				operation.NativeChildren);
		}

		private CollectionOperation MarkRecoveryRequired(CollectionOperation operation)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (operation.Phase == CollectionOperationPhase.RecoveryRequired && operation.ResultState == CollectionOperationResultState.RecoveryRequired)
				return operation;
			operation = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		private CollectionOperation PauseOrRequireRecovery(CollectionOperation operation)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (operation.HasCrossedNativeBoundary)
				return MarkRecoveryRequired(operation);
			if (operation.Phase == CollectionOperationPhase.RemovingOutgoingNativeChildren)
			{
				operation = WithOperationState(operation, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(operation);
			}
			return operation;
		}

		private void ThrowIfCancellationRequestedAtChildBoundary(CollectionOperation operation, CancellationToken cancellationToken)
		{
			if (!cancellationToken.IsCancellationRequested)
				return;
			PauseOrRequireRecovery(operation);
			cancellationToken.ThrowIfCancellationRequested();
		}

		private static CollectionNativeChildOperation RequireChild(CollectionOperation operation, int sequence,
			CollectionNativeChildCheckpoint checkpoint)
		{
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == sequence);
			if (child == null || child.Action != CollectionNativeChildAction.Deactivate || child.Checkpoint != checkpoint)
				throw new InvalidOperationException("The durable C8.4 outgoing child is not at the expected native safety checkpoint.");
			return child;
		}

		private static Task WaitForCompletionAsync(IBackgroundTaskSet task)
		{
			if (task.IsCompleted) return Task.FromResult<object>(null);
			var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
			EventHandler<TaskSetCompletedEventArgs> handler = null;
			handler = (sender, args) =>
			{
				task.TaskSetCompleted -= handler;
				completion.TrySetResult(null);
			};
			task.TaskSetCompleted += handler;
			if (task.IsCompleted)
			{
				task.TaskSetCompleted -= handler;
				completion.TrySetResult(null);
			}
			return completion.Task;
		}

		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expected,
			out bool exactIdentity)
		{
			exactIdentity = false;
			ModInstallerBase nativeTask = task as ModInstallerBase;
			if (nativeTask == null || nativeTask.OperationResult == null)
				return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
					"The native replacement uninstaller reached terminal state without publishing its identified operation result.");
			ModOperationResult result = nativeTask.OperationResult;
			if (!Matches(expected, result.Identity))
				return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
					"The native replacement uninstaller published a terminal result for a different operation attempt.");
			exactIdentity = true;
			return result;
		}

		internal static ModOperationDurability DetermineDurability(ModOperationResult reported, bool exactIdentity,
			bool absent, bool present)
		{
			if (!exactIdentity)
				return ModOperationDurability.Unknown;
			if (absent)
				return ModOperationDurability.VerifiedCommitted;
			if (!present)
				return ModOperationDurability.Unknown;
			if (reported.Durability == ModOperationDurability.NotStarted)
				return ModOperationDurability.NotStarted;
			if (reported.Durability == ModOperationDurability.VerifiedRolledBack)
				return ModOperationDurability.VerifiedRolledBack;
			return ModOperationDurability.Unknown;
		}

		private void RequireLiveServices()
		{
			if (_services.ModManager == null || _services.ModActivationMonitor == null)
				throw new InvalidOperationException("C8.4 requires the live ModManager and ModActivationMonitor services.");
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}
}
