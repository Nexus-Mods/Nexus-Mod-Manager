using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	/// <summary>Result of one deliberately bounded qualified repair execution.</summary>
	public sealed class CollectionVerifyRepairExecutionResult
	{
		internal CollectionVerifyRepairExecutionResult(CollectionCurrentStateFingerprint fingerprint, int repairedCount)
		{
			StateFingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
			RepairedCount = repairedCount;
		}
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public int RepairedCount { get; }
	}

	/// <summary>
	/// Executes only repair actions backed by exact typed C10 verification: Virtual enabled-state toggles and exact member
	/// reinstalls whose native recipe was reconstructed from retained manifest + verified archive bytes. Opaque C9 decisions
	/// remain non-executable.
	/// </summary>
	public sealed class CollectionVerifyRepairExecutionCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsStore _store;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionVerifyRepairExecutionCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsStore store, CollectionsAssociationStore associationStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_operationStore = new CollectionsOperationStore(store);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
		}

		public async Task<CollectionVerifyRepairExecutionResult> ExecuteAsync(CollectionVerifyRepairPlan plan,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!plan.CanExecuteQualifiedRepair)
				throw new InvalidOperationException("The verify/repair plan is not qualified for automatic repair.");

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(plan.Association.Target))
				throw new InvalidOperationException("The active canonical target changed after the repair preview.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionNativeStateIndex before = Capture(authority.Target);
				CollectionTargetAssociation currentAssociation = RequireAssociation(plan);
				List<CollectionMemberBinding> bindings = _associationStore.GetBindings(plan.Association.AssociationId).ToList();
				IReadOnlyList<UserOverride> currentOverrides = _associationStore.GetOverrides(plan.Association.AssociationId);
				CollectionOperation existingRepair = FindExistingRepairOperation(currentAssociation);
				bool crossedNativeBoundary = existingRepair != null && existingRepair.NativeChildren.Any(x => x.HasCrossedNativeBoundary);
				if ((existingRepair == null || (!crossedNativeBoundary && existingRepair.Phase != CollectionOperationPhase.QualifiedEffectsVerified)) &&
					!before.Fingerprint.Equals(plan.StateFingerprint))
					throw new InvalidOperationException("Native state changed after the verify/repair preview; build a new repair plan.");

				CollectionOperation operation = RequireOrCreateOperation(plan, currentAssociation);
				int repaired = 0;
				var repairedRequirements = new List<CollectionRequirementReference>();
				if (operation.Phase == CollectionOperationPhase.QualifiedEffectsVerified)
				{
					ValidateFinalRepairState(plan, bindings, before, currentOverrides);
					foreach (CollectionVerifyRepairFinding finding in plan.Findings.Where(x => x.IsRepairable && x.Requirement != null))
						repairedRequirements.Add(finding.Requirement);
					FinalizeFeatureState(plan, currentAssociation, operation, repairedRequirements);
					return new CollectionVerifyRepairExecutionResult(before.Fingerprint, 0);
				}

				Dictionary<CollectionMemberKey, PreparedCollectionNativeRecipe> recipeByMember = plan.PreparedRecipes
					.ToDictionary(x => x.Member.MemberKey);
				List<CollectionMemberKey> reinstallMembers = plan.Findings.Where(IsNativeReinstallFinding)
					.Select(x => x.MemberKey).Where(x => x != null).Distinct().OrderBy(x => x.ToString(), StringComparer.Ordinal).ToList();

				foreach (CollectionMemberKey memberKey in reinstallMembers)
				{
					cancellationToken.ThrowIfCancellationRequested();
					PreparedCollectionNativeRecipe prepared;
					if (!recipeByMember.TryGetValue(memberKey, out prepared))
						throw new InvalidOperationException("A qualified native-effect repair no longer has its exact prepared member recipe.");
					CollectionMemberBinding binding = bindings.SingleOrDefault(x => x.MemberKey.Equals(memberKey));
					if (binding == null)
						throw new InvalidOperationException("Automatic native-effect repair requires the existing member binding so installed context can be preserved.");

					CollectionNativeStateIndex liveBefore = Capture(authority.Target);
					CollectionNativeModState native;
					if (!liveBefore.Mods.TryGetValue(binding.NativeMod, out native))
						throw new InvalidOperationException("The native member disappeared before qualified repair could preserve its install context.");
					if (native.InstallMethod != prepared.InstallContext.Method || native.InstallRoot != prepared.InstallContext.InstallRoot)
						throw new InvalidOperationException("The native member install method/root changed after verify/repair preparation.");
					if (MemberEffectsSatisfied(plan.Association, binding, prepared.EffectPreview, liveBefore,
						_associationStore.GetOverrides(plan.Association.AssociationId)))
					{
						foreach (CollectionVerifyRepairFinding finding in plan.Findings.Where(x => x.IsRepairable && x.MemberKey != null && x.MemberKey.Equals(memberKey)))
							if (finding.Requirement != null) repairedRequirements.Add(finding.Requirement);
						continue;
					}

					CollectionNativeChildOperation child = GetOrCreatePreparedChild(operation, plan.Association.Revision, memberKey, prepared, native);
					operation = _operationStore.GetOperation(operation.Identity) ?? operation;
					if (child.Checkpoint == CollectionNativeChildCheckpoint.Reconciled)
					{
						if (child.NativeResult == null || child.NativeResult.Durability != ModOperationDurability.VerifiedCommitted)
							throw new InvalidOperationException("A previously reconciled verify/repair child is not durably committed.");
						continue;
					}
					if (child.HasCrossedNativeBoundary)
					{
						if (MemberEffectsSatisfied(plan.Association, binding, prepared.EffectPreview, liveBefore, currentOverrides))
						{
							operation = ReconcileRecoveredCommitted(operation, child);
							continue;
						}
						MarkRecoveryRequired(operation);
						throw new InvalidOperationException("A previous verify/repair child crossed the native boundary but its durable result is ambiguous; explicit recovery is required.");
					}

					IMod liveMod = ResolveLiveMod(binding.NativeMod.NativeModKey);
					ModInstallationRecipeInput recipeInput = prepared.RecipeInput.ForOperationIdentity(child.NativeOperation);
					IBackgroundTaskSet nativeTask = CreateReinstallTask(liveMod, recipeInput, lease);
					bool accepted = _services.ModActivationMonitor.SubmitWhenIdle(nativeTask, () =>
					{
						cancellationToken.ThrowIfCancellationRequested();
						CollectionOperation latest = RequireRepairOperation(operation.Identity);
						CollectionNativeChildOperation latestChild = RequireChild(latest, child.Sequence, CollectionNativeChildCheckpoint.RecoveryInputsReady);
						latest = ReplaceChild(latest, new CollectionNativeChildOperation(latestChild.Sequence, latestChild.Member,
							latestChild.Action, latestChild.NativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null));
						_operationStore.SaveOperation(latest);
					});
					if (!accepted)
						throw new InvalidOperationException("The shared native operation seam rejected the qualified verify/repair reinstall.");

					await WaitForCompletionAsync(nativeTask).ConfigureAwait(true);
					_authorityValidator.ValidateAndReload(lease, authority, paths);
					CollectionNativeStateIndex afterChild = Capture(authority.Target);
					bool satisfied = MemberEffectsSatisfied(plan.Association, binding, prepared.EffectPreview, afterChild,
						_associationStore.GetOverrides(plan.Association.AssociationId));
					operation = _operationStore.GetOperation(operation.Identity) ?? operation;
					CollectionNativeChildOperation submitted = RequireChild(operation, child.Sequence, CollectionNativeChildCheckpoint.NativeSubmitted);
					ModOperationResult reported = CaptureReportedResult(nativeTask, child.NativeOperation);
					ModOperationDurability durability = satisfied ? ModOperationDurability.VerifiedCommitted : ModOperationDurability.Unknown;
					var terminalResult = new ModOperationResult(child.NativeOperation, reported.ReportedStatus, durability, reported.Message);
					var terminal = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
						submitted.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved, terminalResult);
					operation = ReplaceChild(operation, terminal);
					_operationStore.SaveOperation(operation);
					var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
						terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
					operation = ReplaceChild(operation, reconciled);
					_operationStore.SaveOperation(operation);
					if (!satisfied)
					{
						MarkRecoveryRequired(operation);
						throw new InvalidOperationException("The native reinstall completed but exact member effects did not verify; repair recovery is required.");
					}
					repaired++;
					foreach (CollectionVerifyRepairFinding finding in plan.Findings.Where(x => x.IsRepairable && x.MemberKey != null && x.MemberKey.Equals(memberKey)))
						if (finding.Requirement != null) repairedRequirements.Add(finding.Requirement);
				}

				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionNativeStateIndex beforeEnabled = Capture(authority.Target);
				foreach (CollectionVerifyRepairFinding finding in plan.Findings.Where(x => x.IsRepairable &&
					x.Requirement != null && x.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState))
				{
					bool desiredEnabled;
					if (!CollectionMemberRequirementStates.TryGetEnabled(finding.ExpectedState, out desiredEnabled))
						throw new InvalidOperationException("Member enabled-state repair requires canonical bool-v1 expected state.");
					CollectionMemberBinding binding = bindings.SingleOrDefault(x => x.MemberKey.Equals(finding.MemberKey));
					CollectionNativeModState native;
					if (binding == null || !beforeEnabled.Mods.TryGetValue(binding.NativeMod, out native) || native.InstallMethod != ModInstallMethod.Virtual)
						throw new InvalidOperationException("Automatic enabled-state repair is supported only for a bound Virtual native mod.");
					IMod mod = ResolveLiveMod(native.Identity.NativeModKey);
					if (IsEnabled(mod) != desiredEnabled)
					{
						IVirtualModActivator activator = _services.ModManager.VirtualModActivator;
						if (activator == null) throw new InvalidOperationException("Virtual Mod Activator is unavailable for qualified repair.");
						if (desiredEnabled) activator.EnableMod(mod); else activator.DisableMod(mod);
						repaired++;
					}
					repairedRequirements.Add(finding.Requirement);
				}

				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionNativeStateIndex finalState = Capture(authority.Target);
				ValidateFinalRepairState(plan, bindings, finalState, _associationStore.GetOverrides(plan.Association.AssociationId));
				operation = _operationStore.GetOperation(operation.Identity) ?? operation;
				operation = WithOperationState(operation, CollectionOperationPhase.QualifiedEffectsVerified, CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(operation);

				FinalizeFeatureState(plan, currentAssociation, operation, repairedRequirements);
				return new CollectionVerifyRepairExecutionResult(finalState.Fingerprint, repaired);
			}
		}

		private CollectionTargetAssociation RequireAssociation(CollectionVerifyRepairPlan plan)
		{
			CollectionTargetAssociation current = _associationStore.GetAssociation(plan.Association.AssociationId);
			if (current == null || !current.Revision.Equals(plan.Association.Revision) || !current.Target.Equals(plan.Association.Target) || current.State != plan.Association.State)
				throw new InvalidOperationException("The Collection association changed after the verify/repair preview.");
			return current;
		}

		private CollectionOperation FindExistingRepairOperation(CollectionTargetAssociation association)
		{
			return _operationStore.GetIncompleteOperations(association.Target).SingleOrDefault(x => x.Kind == CollectionOperationKind.VerifyRepair &&
				x.Collection.Equals(association.Revision.Collection) && x.Revision != null && x.Revision.Equals(association.Revision));
		}

		private void FinalizeFeatureState(CollectionVerifyRepairPlan plan, CollectionTargetAssociation currentAssociation,
			CollectionOperation operation, IEnumerable<CollectionRequirementReference> repairedRequirements)
		{
			IReadOnlyList<UserOverride> finalOverrides = _associationStore.GetOverrides(plan.Association.AssociationId);
			CollectionAssociationState finalAssociationState = finalOverrides.Count == 0 ? CollectionAssociationState.Applied : CollectionAssociationState.Modified;
			_associationStore.SaveManualMutationDrift(new[] { currentAssociation.WithState(finalAssociationState) },
				Enumerable.Empty<CollectionDriftObservation>(), repairedRequirements.Distinct().ToArray());
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			operation = WithOperationState(operation, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed);
			_operationStore.SaveOperation(operation);
		}

		private CollectionOperation RequireOrCreateOperation(CollectionVerifyRepairPlan plan, CollectionTargetAssociation association)
		{
			List<CollectionOperation> incomplete = _operationStore.GetIncompleteOperations(association.Target).ToList();
			CollectionOperation existing = incomplete.SingleOrDefault(x => x.Kind == CollectionOperationKind.VerifyRepair &&
				x.Collection.Equals(association.Revision.Collection) && x.Revision != null && x.Revision.Equals(association.Revision));
			if (incomplete.Any(x => existing == null || !x.Identity.Equals(existing.Identity)))
				throw new InvalidOperationException("Another incomplete Collection operation already owns this target.");
			if (existing != null)
			{
				if (existing.Phase != CollectionOperationPhase.RepairingQualifiedEffects && existing.Phase != CollectionOperationPhase.RecoveryRequired &&
					existing.Phase != CollectionOperationPhase.QualifiedEffectsVerified)
					throw new InvalidOperationException("The existing verify/repair operation is not at a resumable repair boundary.");
				if (existing.Phase == CollectionOperationPhase.RecoveryRequired)
					existing = WithOperationState(existing, CollectionOperationPhase.RepairingQualifiedEffects, CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(existing);
				return existing;
			}
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.VerifyRepair,
				association.Revision.Collection, association.Target, association.Revision, null, 0,
				CollectionOperationPhase.RepairingQualifiedEffects, CollectionOperationResultState.Pending,
				Enumerable.Empty<CollectionNativeChildOperation>());
			_operationStore.SaveOperation(operation);
			return operation;
		}

		private CollectionNativeChildOperation GetOrCreatePreparedChild(CollectionOperation operation, CollectionRevisionIdentity revision,
			CollectionMemberKey memberKey, PreparedCollectionNativeRecipe prepared, CollectionNativeModState native)
		{
			CollectionNativeChildOperation existing = operation.NativeChildren.SingleOrDefault(x => x.Member.MemberKey.Equals(memberKey));
			if (existing != null) return existing;
			int sequence = operation.NativeChildren.Count == 0 ? 1 : operation.NativeChildren.Max(x => x.Sequence) + 1;
			var identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(operation.Target.Fingerprint,
					new ModInstallContext(native.InstallMethod, native.InstallRoot), prepared.Member.RecipeIdentity.Fingerprint));
			var child = new CollectionNativeChildOperation(sequence, new CollectionOperationMemberReference(revision, memberKey),
				CollectionNativeChildAction.ActivateOrReinstall, identity, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
			operation = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState,
				operation.NativeChildren.Concat(new[] { child }));
			_operationStore.SaveOperation(operation);
			return child;
		}

		private IBackgroundTaskSet CreateReinstallTask(IMod mod, ModInstallationRecipeInput recipeInput, CollectionTargetMutationLease rootLease)
		{
			ConfirmModUpgradeDelegate rejectUnexpectedUpgrade = (oldMod, newMod) => ConfirmUpgradeResult.Cancel;
			ConfirmItemOverwriteDelegate rejectUnexpectedOverwrite = (message, allowPerGroup, allowPerMod) =>
			{
				throw new InvalidOperationException("Verify/repair encountered an unexpected overwrite prompt after exact effect verification.");
			};
			IBackgroundTaskSet task = _services.ModManager.ReinstallMod(mod, rejectUnexpectedUpgrade, rejectUnexpectedOverwrite,
				_services.ModManager.ActiveMods, recipeInput.InstallContext, recipeInput);
			if (task == null) throw new InvalidOperationException("Native NMM returned no reinstall task for the qualified repair member.");
			ModInstallerBase installer = task as ModInstallerBase;
			if (installer == null || installer.OperationIdentity == null || !SameIdentity(installer.OperationIdentity, recipeInput.OperationIdentity))
				throw new InvalidOperationException("The native reinstall did not retain the exact verify/repair operation identity.");
			installer.AssignParentMutationLease(rootLease);
			return task;
		}

		private static bool IsNativeReinstallFinding(CollectionVerifyRepairFinding finding)
		{
			return finding != null && finding.IsRepairable && finding.MemberKey != null &&
				(finding.Kind == CollectionVerifyRepairFindingKind.ManagedFileEffectMismatch ||
				 finding.Kind == CollectionVerifyRepairFindingKind.IniEffectMismatch ||
				 finding.Kind == CollectionVerifyRepairFindingKind.GameValueEffectMismatch ||
				 finding.Kind == CollectionVerifyRepairFindingKind.PluginEffectMismatch);
		}

		private static bool MemberEffectsSatisfied(CollectionTargetAssociation association, CollectionMemberBinding binding,
			CollectionMemberEffectPreview preview, CollectionNativeStateIndex state, IEnumerable<UserOverride> overrides)
		{
			IReadOnlyList<CollectionVerifyRepairFinding> findings = new CollectionVerifyRepairExactEffectVerifier().Verify(association, state,
				new[] { binding }, new[] { preview }, overrides);
			return !findings.Any(x => x.IsRepairable || x.RequiresAction);
		}

		private void ValidateFinalRepairState(CollectionVerifyRepairPlan plan, IList<CollectionMemberBinding> bindings,
			CollectionNativeStateIndex state, IEnumerable<UserOverride> overrides)
		{
			foreach (PreparedCollectionNativeRecipe prepared in plan.PreparedRecipes)
			{
				CollectionMemberBinding binding = bindings.SingleOrDefault(x => x.MemberKey.Equals(prepared.Member.MemberKey));
				if (binding == null) continue;
				if (!MemberEffectsSatisfied(plan.Association, binding, prepared.EffectPreview, state, overrides))
					throw new InvalidOperationException("Qualified repair did not establish all exact characterized effects for a repaired member.");
			}
			foreach (CollectionVerifyRepairFinding finding in plan.Findings.Where(x => x.IsRepairable && findingIsEnabled(x)))
			{
				bool desired;
				if (!CollectionMemberRequirementStates.TryGetEnabled(finding.ExpectedState, out desired))
					throw new InvalidOperationException("Enabled-state verification lost its canonical expected value.");
				CollectionMemberBinding binding = bindings.Single(x => x.MemberKey.Equals(finding.MemberKey));
				CollectionNativeModState native;
				if (!state.Mods.TryGetValue(binding.NativeMod, out native))
					throw new InvalidOperationException("The repaired enabled-state member is no longer installed.");
				if (native.InstallMethod != ModInstallMethod.Virtual)
					throw new InvalidOperationException("The repaired enabled-state member is no longer Virtual.");
				IMod liveMod = ResolveLiveMod(native.Identity.NativeModKey);
				if (IsEnabled(liveMod) != desired)
					throw new InvalidOperationException("The repaired Virtual enabled state does not match the exact expected value.");
			}
		}

		private static bool findingIsEnabled(CollectionVerifyRepairFinding finding)
		{
			return finding.Requirement != null && finding.Requirement.Aspect == CollectionRequirementAspect.MemberEnabledState;
		}

		private CollectionNativeStateIndex Capture(CollectionTargetIdentity target)
		{
			return new CollectionNativeStateReader(() => _services.ModManager.InstallationLog, _services.ModManager.VirtualModActivator,
				_services.PluginManager, _services.ModManager.GameMode, _associationStore).Capture(target);
		}

		private IMod ResolveLiveMod(string nativeModKey)
		{
			List<IMod> matches = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
				StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), nativeModKey)).ToList();
			if (matches.Count != 1) throw new InvalidOperationException("The repair target no longer resolves to one live native NMM mod.");
			return matches[0];
		}

		private bool IsEnabled(IMod mod)
		{
			IVirtualModActivator activator = _services.ModManager.VirtualModActivator;
			if (activator == null) return false;
			string fileName = Path.GetFileName(mod.Filename) ?? String.Empty;
			return activator.ActiveModList.Contains(fileName.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
		}

		private CollectionOperation ReconcileRecoveredCommitted(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			ModOperationReportedStatus status = child.NativeResult == null ? ModOperationReportedStatus.NoOp : child.NativeResult.ReportedStatus;
			var result = new ModOperationResult(child.NativeOperation, status, ModOperationDurability.VerifiedCommitted,
				"Authoritative verify/repair state already satisfies the exact prepared member effects after restart.");
			var reconciled = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
				CollectionNativeChildCheckpoint.Reconciled, result);
			operation = ReplaceChild(operation, reconciled);
			_operationStore.SaveOperation(operation);
			return operation;
		}

		private CollectionOperation RequireRepairOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.VerifyRepair ||
				operation.Phase != CollectionOperationPhase.RepairingQualifiedEffects || operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("The verify/repair operation is no longer at its qualified native repair boundary.");
			return operation;
		}

		private static CollectionNativeChildOperation RequireChild(CollectionOperation operation, int sequence, CollectionNativeChildCheckpoint checkpoint)
		{
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == sequence);
			if (child == null || child.Checkpoint != checkpoint)
				throw new InvalidOperationException("The durable verify/repair child checkpoint changed unexpectedly.");
			return child;
		}

		private static CollectionOperation ReplaceChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, operation.Phase, operation.ResultState, children);
		}

		private static CollectionOperation WithOperationState(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result)
		{
			return new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, operation.NativeChildren);
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			operation = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (operation.IsTerminal) return;
			operation = WithOperationState(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
			_operationStore.SaveOperation(operation);
		}

		private static Task WaitForCompletionAsync(IBackgroundTaskSet task)
		{
			if (task.IsCompleted) return Task.FromResult(true);
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

		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expected)
		{
			ModInstallerBase installer = task as ModInstallerBase;
			ModOperationResult result = installer == null ? null : installer.OperationResult;
			if (result != null && SameIdentity(result.Identity, expected)) return result;
			return new ModOperationResult(expected, task.IsCompleted ? ModOperationReportedStatus.Failed : ModOperationReportedStatus.Rejected,
				ModOperationDurability.Unknown, "The native repair task did not expose an exact correlated terminal result.");
		}

		private static bool SameIdentity(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId;
		}
	}
}
