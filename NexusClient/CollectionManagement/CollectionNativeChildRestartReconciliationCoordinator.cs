using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ChinhDo.Transactions;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Transactions;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reconciles one Collection native child whose process-local C6.7/C6.8 execution context was lost across restart.
	/// </summary>
	/// <remarks>
	/// Automatic C6.9 reconciliation never resubmits native work. An explicit recovery action can finish an unregistered
	/// Witcher activation through its retained recipe and normal native installer. Reconciliation first runs native
	/// deployment/VMA recovery through the C4 authority reload,
	/// then compares the exact persisted child/evidence with authoritative native reality. Unknown reality remains
	/// RecoveryRequired for explicit later recovery rather than being guessed or replayed.
	/// </remarks>
	public sealed partial class CollectionNativeChildRestartReconciliationCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsResolvedPlanStore _planStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionOperationCoordinator _operationCoordinator;

		/// <summary>Creates a production C6.9 restart reconciler over the established native and Collections services.</summary>
		public CollectionNativeChildRestartReconciliationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
			: this(services, gameStorageService, operationStore, planStore, associationStore, manifestStore,
				CollectionTargetMutationLeaseManager.Shared,
				new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		/// <summary>Creates a C6.9 restart reconciler over explicit coordination services.</summary>
		internal CollectionNativeChildRestartReconciliationCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore,
			CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_planStore = planStore ?? throw new ArgumentNullException(nameof(planStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			_operationCoordinator = new CollectionOperationCoordinator(operationStore, planStore);
		}

		/// <summary>Reconciles one restart-ambiguous native child against authoritative native state without replaying it.</summary>
		public Task<CollectionNativeChildRestartReconciliationResult> ReconcileAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths)
		{
			return ReconcileAsync(operationIdentity, paths, CancellationToken.None);
		}

		/// <summary>Reconciles one restart-ambiguous C8.6 incoming child without replaying replacement work.</summary>
		internal Task<CollectionNativeChildRestartReconciliationResult> ReconcileReplacementIncomingAsync(
			CollectionOperationIdentity operationIdentity, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			return ReconcileCoreAsync(operationIdentity, paths, cancellationToken, CollectionNativeChildWorkflowMode.Replacement);
		}

		/// <summary>Reconciles one restart-ambiguous C10.6 candidate revision-update child without replaying native work.</summary>
		internal Task<CollectionNativeChildRestartReconciliationResult> ReconcileRevisionUpdateIncomingAsync(
			CollectionOperationIdentity operationIdentity, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			return ReconcileCoreAsync(operationIdentity, paths, cancellationToken, CollectionNativeChildWorkflowMode.RevisionUpdate);
		}

		/// <summary>Reconciles one restart-ambiguous native child while honoring cancellation before authority inspection begins.</summary>
		public Task<CollectionNativeChildRestartReconciliationResult> ReconcileAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			return ReconcileCoreAsync(operationIdentity, paths, cancellationToken, CollectionNativeChildWorkflowMode.Additive);
		}

		private async Task<CollectionNativeChildRestartReconciliationResult> ReconcileCoreAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken, CollectionNativeChildWorkflowMode workflowMode,
			PreparedCollectionNativeRecipe repairRecipe = null)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (_services.ModManager == null)
				throw new InvalidOperationException("C6.9 requires the live ModManager service.");

			CollectionOperation operation = RequireRecoverableOperation(operationIdentity, workflowMode);
			CollectionNativeChildOperation child = RequireRestartChild(operation);
			CollectionResolvedPlanRecord plan = RequirePersistedPlan(operation, workflowMode);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(operation.Target) || !authority.Target.Equals(plan.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the persisted Collection recovery target.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (operation.Phase != CollectionOperationPhase.Recovering)
					operation = workflowMode == CollectionNativeChildWorkflowMode.Additive
						? _operationCoordinator.BeginRecovery(operationIdentity)
						: SaveOperationState(operation, CollectionOperationPhase.Recovering, CollectionOperationResultState.Pending);

				try
				{
					// This is the first native-state action after restart. ValidateAndReload runs the existing deployment/VMA
					// recovery path before Collections examines or classifies the child.
					_authorityValidator.ValidateAndReload(lease, authority, paths);

					operation = RequireRecoveringOperation(operationIdentity);
					child = RequireRestartChild(operation);
					CollectionNativeChildRecoveryManifest recovery = RequireRecoveryManifest(operation, child, plan);
					if (child.NativeResult != null && child.NativeResult.ReportedStatus == ModOperationReportedStatus.Succeeded)
						ModManagerCollectionManagedArchiveSource.RestoreMissingNativeRepositoryIdentity(_services.ModManager, recovery, cancellationToken);
					CollectionNativeStateIndex state = CaptureReloadedState(operation.Target);
					if (repairRecipe != null && recovery.ExecutionEvidence == null &&
						workflowMode == CollectionNativeChildWorkflowMode.Additive)
					{
						await RepairUnregisteredWitcherActivationAsync(operation, child, recovery, state, repairRecipe,
							paths, lease, cancellationToken).ConfigureAwait(true);
						operation = RequireRecoveringOperation(operationIdentity);
						child = RequireRestartChild(operation);
						recovery = RequireRecoveryManifest(operation, child, plan);
						state = CaptureReloadedState(operation.Target);
					}

					ModOperationResult priorResult = child.NativeResult;
					ModOperationReportedStatus reportedStatus = priorResult == null
						? ModOperationReportedStatus.Failed
						: priorResult.ReportedStatus;
					string reportedMessage = priorResult == null
						? "The process restarted after native submission without a durable terminal native report; C6.9 reconstructed durability from authoritative native state."
						: priorResult.Message;
					ModOperationDurability priorDurability = priorResult == null ? ModOperationDurability.Unknown : priorResult.Durability;

					CollectionNativeChildExecutionEvidence evidence = recovery.ExecutionEvidence;
					CollectionNativeModState verifiedNativeMod = null;
					string committedFailure = null;
					string rolledBackFailure = null;
					bool committed = evidence != null && TryVerifyCommittedState(recovery, evidence, state,
						_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
						paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod, out committedFailure,
						getVirtualSource: _services.ModManager.VirtualModActivator.GetVirtualSourceForOwner);
					if (!committed && evidence == null && workflowMode == CollectionNativeChildWorkflowMode.Additive)
						committed = TryVerifyRecordedCommittedState(recovery, child, plan, state, out verifiedNativeMod);
					string stoppedActivationDetail = null;
					bool stoppedActivationRecovered = !committed && evidence != null && repairRecipe != null &&
						workflowMode == CollectionNativeChildWorkflowMode.Additive && reportedStatus == ModOperationReportedStatus.Failed &&
						priorDurability == ModOperationDurability.Unknown && TryRecoverFailedUnregisteredFileActivation(recovery,
							evidence, state, _services.ModManager.VirtualModActivator.ActiveModList,
							paths.InstallInfoPath, _services.ModManager.GameMode, out stoppedActivationDetail);
					bool rolledBack = stoppedActivationRecovered || (evidence != null && TryVerifyRolledBackState(recovery, evidence, state,
						paths.InstallInfoPath, _services.ModManager.GameMode, out rolledBackFailure));
					string virtualRelinkDetail = null;
					if (workflowMode == CollectionNativeChildWorkflowMode.Additive && evidence != null &&
						!committed && !rolledBack && priorDurability == ModOperationDurability.Unknown &&
						reportedStatus == ModOperationReportedStatus.Succeeded &&
						TryRepairExactNewActivationLinks(recovery, evidence, state, paths.InstallInfoPath, out virtualRelinkDetail))
					{
						state = CaptureReloadedState(operation.Target);
						committed = TryVerifyCommittedState(recovery, evidence, state,
							_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
							paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod, out committedFailure,
							getVirtualSource: _services.ModManager.VirtualModActivator.GetVirtualSourceForOwner);
					}
					if (workflowMode == CollectionNativeChildWorkflowMode.RevisionUpdate && evidence != null &&
						!committed && !rolledBack && priorDurability == ModOperationDurability.Unknown &&
						reportedStatus == ModOperationReportedStatus.Succeeded && TryRepairExactVirtualUpgradeLinks(recovery,
							evidence, state, paths.InstallInfoPath, out virtualRelinkDetail))
					{
						state = CaptureReloadedState(operation.Target);
						committed = TryVerifyCommittedState(recovery, evidence, state,
							_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
							paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod, out committedFailure);
					}
					string replayRepairDetail = null;
					if (evidence != null && !committed && !rolledBack && priorDurability == ModOperationDurability.Unknown)
					{
						string currentDomain = _services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName;
						// A failed terminal report can still leave an exact committed installation. Recover its missing
						// record from the retained recipe only after proving the complete installed postimage.
						bool replayRestored = reportedStatus == ModOperationReportedStatus.Succeeded &&
							TryCompleteExactActivationReplay(recovery, evidence, state, currentDomain, paths.InstallInfoPath, out replayRepairDetail);
						if (!replayRestored)
						{
							string missingReplayDetail;
							replayRestored = TryRestoreExactNewActivationReplay(recovery, evidence, state, currentDomain,
								paths.InstallInfoPath, _services.ModManager.GameMode, out missingReplayDetail);
							if (!String.IsNullOrWhiteSpace(missingReplayDetail)) replayRepairDetail = missingReplayDetail;
						}
						if (!replayRestored && reportedStatus == ModOperationReportedStatus.Succeeded)
						{
							string reinstallDetail;
							replayRestored = TryRestoreExactReinstallReplay(recovery, evidence, state, currentDomain,
								paths.InstallInfoPath, _services.ModManager.GameMode, out reinstallDetail);
							if (!String.IsNullOrWhiteSpace(reinstallDetail)) replayRepairDetail = reinstallDetail;
						}
						if (replayRestored)
						{
							state = CaptureReloadedState(operation.Target);
							committed = TryVerifyCommittedState(recovery, evidence, state, currentDomain,
								paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod, out committedFailure,
								getVirtualSource: _services.ModManager.VirtualModActivator.GetVirtualSourceForOwner);
						}
					}
					bool rollbackResidueRepaired = stoppedActivationRecovered;
					string rollbackRepairDetail = stoppedActivationDetail;
					if (evidence != null && !committed && !rolledBack && priorDurability == ModOperationDurability.Unknown)
					{
						rollbackResidueRepaired = TryRepairExactNewActivationRollbackResidue(recovery, evidence, state,
							paths.InstallInfoPath, _services.ModManager.GameMode, out rollbackRepairDetail);
						if (rollbackResidueRepaired)
						{
							// Re-capture after the bounded filesystem/replay repair. The repair is acceptable only if the
							// complete authoritative native fingerprint still equals the exact C6.6 preparation state.
							state = CaptureReloadedState(operation.Target);
							rolledBack = TryVerifyRolledBackState(recovery, evidence, state,
								paths.InstallInfoPath, _services.ModManager.GameMode, out rolledBackFailure);
							if (!rolledBack)
								rollbackRepairDetail = (rollbackRepairDetail ?? "Exact rollback residue repair was applied.") +
									" Post-repair rollback verification still failed: " +
									(rolledBackFailure ?? "no specific failure was recorded");
						}
					}
					string verificationDiagnostics = evidence == null
						? (committed ? "The recorded native commit and retained terminal state were verified."
							: "The interrupted install has no saved file evidence. Choose Check recovery and continue to complete an unchanged, unregistered Witcher installation from its saved recipe. Changed or owned files require manual recovery.")
						: BuildVerificationDiagnostics(committed, committedFailure, rolledBack, rolledBackFailure,
							rollbackResidueRepaired, rollbackRepairDetail);
					if (!String.IsNullOrWhiteSpace(virtualRelinkDetail))
						verificationDiagnostics += " Virtual upgrade relink recovery: " + virtualRelinkDetail;
					if (!String.IsNullOrWhiteSpace(replayRepairDetail))
						verificationDiagnostics += " Installation replay recovery: " + replayRepairDetail;
					ModOperationDurability durability = DetermineRestartDurability(priorDurability, committed, rolledBack);

					if (durability == ModOperationDurability.VerifiedCommitted)
					{
						if (recovery.TerminalStateFingerprint == null)
						{
							_manifestStore.SaveManifest(recovery.WithTerminalStateFingerprint(state.Fingerprint));
							recovery = RequireRecoveryManifest(operation, child, plan);
						}
						if (!state.Fingerprint.Equals(recovery.TerminalStateFingerprint))
							throw new InvalidDataException("The retained committed terminal-state fingerprint does not match authoritative C6.9 native state.");
					}

					var reconciledResult = new ModOperationResult(child.NativeOperation, reportedStatus, durability, reportedMessage);
					child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
						CollectionNativeChildCheckpoint.NativeTerminalObserved, reconciledResult);
					operation = SaveChild(operation, child);

					if (durability == ModOperationDurability.Unknown)
						operation = workflowMode == CollectionNativeChildWorkflowMode.Additive
							? _operationCoordinator.MarkRecoveryRequired(operation.Identity)
							: SaveOperationState(operation, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);

					return new CollectionNativeChildRestartReconciliationResult(operation, child, state, verifiedNativeMod,
						evidence != null, priorDurability, verificationDiagnostics);
				}
				catch
				{
					TryMarkRecoveryRequired(operationIdentity, workflowMode);
					throw;
				}
			}
		}

		private void TryMarkRecoveryRequired(CollectionOperationIdentity identity, CollectionNativeChildWorkflowMode workflowMode)
		{
			try
			{
				CollectionOperation current = _operationStore.GetOperation(identity);
				if (current != null && current.Phase == CollectionOperationPhase.Recovering && current.HasUnreconciledNativeChild)
				{
					if (workflowMode == CollectionNativeChildWorkflowMode.Additive) _operationCoordinator.MarkRecoveryRequired(identity);
					else SaveOperationState(current, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired);
				}
			}
			catch
			{
				// Preserve the original native-recovery/reload exception. A failed feature-store update is itself reconciled on the next restart.
			}
		}

		private CollectionOperation SaveOperationState(CollectionOperation operation, CollectionOperationPhase phase, CollectionOperationResultState result)
		{
			if (operation.CheckpointSequence == Int64.MaxValue) throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, operation.NativeChildren);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private CollectionOperation RequireRecoverableOperation(CollectionOperationIdentity identity, CollectionNativeChildWorkflowMode workflowMode)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			CollectionOperationKind requiredKind;
			switch (workflowMode)
			{
				case CollectionNativeChildWorkflowMode.Replacement:
					requiredKind = CollectionOperationKind.ReplaceCurrentManagedSetup;
					break;
				case CollectionNativeChildWorkflowMode.RevisionUpdate:
					requiredKind = CollectionOperationKind.UpdateRevision;
					break;
				default:
					requiredKind = CollectionOperationKind.ApplyResolvedPlan;
					break;
			}
			if (operation.Kind != requiredKind ||
				(operation.ResultState != CollectionOperationResultState.Pending && operation.ResultState != CollectionOperationResultState.RecoveryRequired))
				throw new InvalidOperationException("The Collection operation kind/result is not valid for native-child restart reconciliation in this workflow.");
			bool phaseAllowed;
			if (workflowMode == CollectionNativeChildWorkflowMode.Replacement)
				phaseAllowed = operation.Phase == CollectionOperationPhase.InstallingIncomingNativeChildren || operation.Phase == CollectionOperationPhase.Recovering || operation.Phase == CollectionOperationPhase.RecoveryRequired;
			else if (workflowMode == CollectionNativeChildWorkflowMode.RevisionUpdate)
				phaseAllowed = operation.Phase == CollectionOperationPhase.InstallingCandidateRevisionChildren || operation.Phase == CollectionOperationPhase.Recovering || operation.Phase == CollectionOperationPhase.RecoveryRequired;
			else
				phaseAllowed = operation.Phase == CollectionOperationPhase.ApplyingNativeChildren || operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary ||
					operation.Phase == CollectionOperationPhase.Verifying || operation.Phase == CollectionOperationPhase.Recovering || operation.Phase == CollectionOperationPhase.RecoveryRequired;
			if (!phaseAllowed) throw new InvalidOperationException("The Collection operation is not at a restart-reconcilable native boundary.");
			if (!operation.HasCrossedNativeBoundary)
				throw new InvalidOperationException("C6.9 cannot be used before a native child crossed the submission boundary.");
			return operation;
		}

		private CollectionOperation RequireRecoveringOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Phase != CollectionOperationPhase.Recovering ||
				operation.ResultState != CollectionOperationResultState.Pending)
				throw new InvalidOperationException("The Collection operation did not remain at the durable Recovering checkpoint.");
			return operation;
		}

		private CollectionResolvedPlanRecord RequirePersistedPlan(CollectionOperation operation, CollectionNativeChildWorkflowMode workflowMode)
		{
			if (operation.PlanIdentity == null || operation.Revision == null)
				throw new InvalidOperationException("A restart-reconcilable Collection operation must retain its exact plan and revision identities.");
			CollectionResolvedPlanRecord plan = _planStore.GetPlan(operation.PlanIdentity);
			CollectionExecutionPolicyKind expectedPolicy = workflowMode == CollectionNativeChildWorkflowMode.Replacement
				? CollectionExecutionPolicyKind.ReplaceCurrentManagedSetup : CollectionExecutionPolicyKind.InstallIntoCurrentSetup;
			if (plan == null || !plan.Revision.Equals(operation.Revision) || !plan.Target.Equals(operation.Target) || plan.PolicyKind != expectedPolicy)
				throw new InvalidOperationException("The exact persisted plan required for restart reconciliation is unavailable or mismatched.");
			return plan;
		}

		private static CollectionNativeChildOperation RequireRestartChild(CollectionOperation operation)
		{
			List<CollectionNativeChildOperation> pending = operation.NativeChildren.Where(x => x.HasCrossedNativeBoundary && !x.IsReconciled).ToList();
			if (pending.Count != 1)
				throw new InvalidOperationException("C6.9 requires exactly one serialized unreconciled child that crossed the native boundary.");
			CollectionNativeChildOperation child = pending[0];
			if (child.Action != CollectionNativeChildAction.ActivateOrReinstall || child.NativeOperation.Origin != ModOperationOrigin.Collection ||
				(child.Checkpoint != CollectionNativeChildCheckpoint.NativeSubmitted &&
				 child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved))
				throw new InvalidOperationException("The unreconciled child is not at a supported C6.9 restart boundary.");
			return child;
		}

		private CollectionNativeChildRecoveryManifest RequireRecoveryManifest(CollectionOperation operation,
			CollectionNativeChildOperation child, CollectionResolvedPlanRecord plan)
		{
			CollectionNativeChildRecoveryManifest recovery = _manifestStore.GetManifest(operation, child);
			if (recovery == null)
				throw new InvalidDataException("The restart-ambiguous child is missing its retained C6.6/C6.7 recovery manifest.");
			if (!recovery.PlanIdentity.Equals(plan.Identity) || !recovery.Member.MemberKey.Equals(child.Member.MemberKey) ||
				recovery.Action != child.Action || !Matches(recovery.NativeOperation, child.NativeOperation))
				throw new InvalidDataException("The retained recovery manifest does not correlate to the exact restart-ambiguous native attempt.");
			return recovery;
		}

		private CollectionNativeStateIndex CaptureReloadedState(CollectionTargetIdentity target)
		{
			ModManager modManager = _services.ModManager;
			return new CollectionNativeStateReader(modManager.InstallationLog, modManager.VirtualModActivator,
				_services.PluginManager, modManager.GameMode, _associationStore).Capture(target);
		}

		private static ModOperationDurability DetermineRestartDurability(ModOperationDurability priorDurability,
			bool committedStateVerified, bool rolledBackStateVerified)
		{
			switch (priorDurability)
			{
				case ModOperationDurability.VerifiedCommitted:
					return committedStateVerified ? ModOperationDurability.VerifiedCommitted : ModOperationDurability.Unknown;
				case ModOperationDurability.NotStarted:
					return rolledBackStateVerified ? ModOperationDurability.NotStarted : ModOperationDurability.Unknown;
				case ModOperationDurability.VerifiedRolledBack:
					return rolledBackStateVerified ? ModOperationDurability.VerifiedRolledBack : ModOperationDurability.Unknown;
				case ModOperationDurability.Unknown:
				default:
					if (committedStateVerified == rolledBackStateVerified) return ModOperationDurability.Unknown;
					return committedStateVerified
						? ModOperationDurability.VerifiedCommitted
						: ModOperationDurability.VerifiedRolledBack;
			}
		}

		internal static bool TryVerifyCommittedState(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string currentDomain,
			string installInfoDirectory, IGameMode gameMode, out CollectionNativeModState nativeMod, out string failureReason,
			bool verifyReplay = true, bool verifyFileContents = true,
			Func<ModDeploymentTarget, string, string> getVirtualSource = null)
		{
			nativeMod = null;
			failureReason = null;
			if (recovery == null || evidence == null || state == null || gameMode == null || !evidence.ReviewedEffects.IsComplete ||
				String.IsNullOrWhiteSpace(installInfoDirectory))
			{
				failureReason = "committed prerequisites are incomplete (recovery/evidence/state/game mode/review/install-info path).";
				return false;
			}

			string expectedDomain;
			long expectedModId;
			long expectedFileId;
			bool isNexus = NexusCollectionModFileArtifactIdentity.TryParse(evidence.SelectedArtifact,
				out expectedDomain, out expectedModId, out expectedFileId);
			bool isBundle = CollectionBundledArtifactIdentity.IsBundle(evidence.SelectedArtifact);
			string externalMd5; long externalByteLength;
			bool isExternal = CollectionExternalArtifactIdentity.TryParse(evidence.SelectedArtifact, out externalMd5, out externalByteLength);
			if (!isNexus && !isBundle && !isExternal)
			{
				failureReason = "selected artifact is not a supported Nexus, external or bundle archive identity.";
				return false;
			}
			if (isNexus && !StringComparer.OrdinalIgnoreCase.Equals(currentDomain, expectedDomain))
			{
				failureReason = String.Format(CultureInfo.InvariantCulture,
					"Nexus game domain mismatch (expected '{0}', observed '{1}').", expectedDomain, currentDomain ?? "<null>");
				return false;
			}

			string modId = isNexus ? expectedModId.ToString(CultureInfo.InvariantCulture) : null;
			string fileId = isNexus ? expectedFileId.ToString(CultureInfo.InvariantCulture) : null;
			List<CollectionNativeModState> candidates = state.Mods.Values.Where(x =>
				x.InstallMethod == evidence.ReviewedEffects.InstallMethod && x.InstallRoot == evidence.ReviewedEffects.InstallRoot &&
				StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName) &&
				MatchesArtifactFile(x.ArchivePath, recovery.IncomingArchive) &&
				(!isNexus || (StringComparer.Ordinal.Equals(x.NexusModId, modId) &&
					StringComparer.Ordinal.Equals(x.NexusFileId, fileId)))).ToList();
			if (candidates.Count != 1)
			{
				List<CollectionNativeModState> sameFileName = state.Mods.Values
					.Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName)).ToList();
				string observedCandidates = sameFileName.Count == 0
					? "<none>"
					: String.Join(" | ", sameFileName.Take(4).Select(x => String.Format(CultureInfo.InvariantCulture,
						"key='{0}', method={1}, root={2}, nexusModId='{3}', nexusFileId='{4}', archiveMatch={5}",
						x.Identity.NativeModKey, x.InstallMethod, x.InstallRoot, x.NexusModId ?? "<null>", x.NexusFileId ?? "<null>",
						MatchesArtifactFile(x.ArchivePath, recovery.IncomingArchive))));
				failureReason = String.Format(CultureInfo.InvariantCulture,
					"native candidate count is {0}, expected exactly 1 (same filename={1}, file='{2}', expected method={3}, root={4}, archiveSha256={5}, archiveLength={6}; observed: {7}).",
					candidates.Count, sameFileName.Count, evidence.IncomingFileName, evidence.ReviewedEffects.InstallMethod,
					evidence.ReviewedEffects.InstallRoot, recovery.IncomingArchive.ContentHash.Value, recovery.IncomingArchive.ByteLength,
					observedCandidates);
				return false;
			}

			nativeMod = candidates[0];
			HashSet<ModDeploymentTarget> inactiveFiles = GetVerifiedInactiveFileTargets(state, nativeMod, evidence, getVirtualSource);
			var failures = new List<string>();
			string detail;
			if (!VerifyMemberEffects(state, nativeMod, evidence.ReviewedEffects, !evidence.DeferPluginReconciliation, out detail, inactiveFiles)) failures.Add(detail);
			if (verifyFileContents && !VerifyFileEvidence(state, evidence.ExpectedFileContents.Where(x => !inactiveFiles.Contains(x.Target)), gameMode, out detail)) failures.Add(detail);
			if (verifyFileContents && evidence.ReviewedEffects.InstallRootCorrection != null && !evidence.ReviewedEffects.InstallRootCorrection.Verify(state, true))
				failures.Add("The reviewed old-folder cleanup has not completed.");
			if (verifyReplay && !VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out detail)) failures.Add(detail);
			if (failures.Count != 0)
			{
				failureReason = String.Join("; ", failures.Where(x => !String.IsNullOrWhiteSpace(x)));
				nativeMod = null;
				return false;
			}
			return true;
		}

		/// <summary>Proves unchanged native ownership, exact game-file preimages and replay state for live or restarted verification.</summary>
		internal static bool TryVerifyRolledBackState(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string installInfoDirectory, IGameMode gameMode,
			out string failureReason)
		{
			failureReason = null;
			if (recovery == null || evidence == null || state == null || gameMode == null || String.IsNullOrWhiteSpace(installInfoDirectory))
			{
				failureReason = "rollback prerequisites are incomplete (recovery/evidence/state/game mode/install-info path).";
				return false;
			}

			var failures = new List<string>();
			if (!state.Fingerprint.Equals(recovery.PreparationStateFingerprint))
				failures.Add(String.Format(CultureInfo.InvariantCulture,
					"preparation fingerprint mismatch (expected {0}:{1}, observed {2}:{3}, deploymentSequence={4}).",
					recovery.PreparationStateFingerprint.FormatVersion, recovery.PreparationStateFingerprint.Value,
					state.Fingerprint.FormatVersion, state.Fingerprint.Value, state.DeploymentCommitSequence));

			string detail;
			if (!VerifyFileEvidence(state, evidence.PreFileContents, gameMode, out detail)) failures.Add(detail);
			if (!VerifyReplayContentEvidence(evidence.IncomingFileName, installInfoDirectory, evidence.IncomingReplayPreimage, out detail)) failures.Add(detail);

			if (recovery.PreviousNativeMod == null)
			{
				List<CollectionNativeModState> incomingRegistrations = state.Mods.Values.Where(x =>
					StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName) &&
					MatchesArtifactFile(x.ArchivePath, recovery.IncomingArchive)).ToList();
				if (incomingRegistrations.Count != 0)
					failures.Add(String.Format(CultureInfo.InvariantCulture,
						"rollback pre-state had no previous native mod, but {0} exact incoming registration(s) remain: [{1}].",
						incomingRegistrations.Count, String.Join(", ", incomingRegistrations.Select(x => x.Identity.NativeModKey))));
				if (recovery.PreviousArchive != null)
					failures.Add("no previous native mod was captured, but a previous archive recovery artifact exists.");
				if (recovery.ScriptedReplay.ReplayFileExisted || recovery.ScriptedReplay.PayloadDirectoryExisted || recovery.ScriptedReplay.Payloads.Count != 0)
					failures.Add("no previous native mod was captured, but previous scripted-replay artifacts were retained.");
			}
			else
			{
				if (!VerifyPreviousNativeMod(recovery, state, out detail)) failures.Add(detail);
				if (!VerifyPreviousReplayPreimage(recovery, installInfoDirectory, out detail)) failures.Add(detail);
			}

			failureReason = failures.Count == 0 ? null : String.Join("; ", failures.Where(x => !String.IsNullOrWhiteSpace(x)));
			return failures.Count == 0;
		}

		/// <summary>Proves an installed inactive writer and its unchanged deployed winner before final Collection conflict reconciliation.</summary>
		internal static HashSet<ModDeploymentTarget> GetVerifiedInactiveFileTargets(CollectionNativeStateIndex state,
			CollectionNativeModState nativeMod, CollectionNativeChildExecutionEvidence evidence,
			Func<ModDeploymentTarget, string, string> getVirtualSource)
		{
			var verified = new HashSet<ModDeploymentTarget>();
			if (state == null || nativeMod == null || evidence == null || getVirtualSource == null ||
				!evidence.DeferPluginReconciliation || evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual)
				return verified;
			string owner = nativeMod.Identity.NativeModKey;
			Dictionary<ModDeploymentTarget, CollectionNativeFileContentEvidence> before = evidence.PreFileContents.ToDictionary(x => x.Target);
			Dictionary<ModDeploymentTarget, CollectionNativeFileContentEvidence> expected = evidence.ExpectedFileContents.ToDictionary(x => x.Target);
			foreach (CollectionPlannedFileEffect effect in evidence.ReviewedEffects.Files)
			{
				CollectionNativeFileState file;
				CollectionNativeFileContentEvidence preimage, postimage;
				if ((evidence.ReviewedEffects.InstallRootCorrection != null &&
					evidence.ReviewedEffects.InstallRootCorrection.GetPreservedDestination(effect.Target) != null) ||
					!state.Files.TryGetValue(effect.Target, out file) || file.Promoted || !file.RecordedByVirtualState ||
					StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, owner) ||
					!before.TryGetValue(effect.Target, out preimage) || !preimage.Existed ||
					!expected.TryGetValue(effect.Target, out postimage) || !postimage.Existed)
					continue;
				List<CollectionNativeOwnerState> writers = file.VirtualOwners.Where(x =>
					StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, owner)).ToList();
				List<CollectionNativeOwnerState> active = file.VirtualOwners.Where(x => x.VirtualLinkActive == true).ToList();
				if (writers.Count != 1 || writers[0].Kind != CollectionNativeOwnerKind.NativeMod || writers[0].VirtualLinkActive != false ||
					active.Count != 1 || active[0].Kind != CollectionNativeOwnerKind.NativeMod ||
					!StringComparer.OrdinalIgnoreCase.Equals(active[0].OwnerKey, file.EffectiveOwnerKey))
					continue;
				try
				{
					if (MatchesContentFile(getVirtualSource(effect.Target, owner), postimage.ContentHash, postimage.ByteLength) &&
						MatchesContentFile(file.PhysicalPath, preimage.ContentHash, preimage.ByteLength) &&
						MatchesContentFile(getVirtualSource(effect.Target, active[0].OwnerKey), preimage.ContentHash, preimage.ByteLength))
						verified.Add(effect.Target);
				}
				catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
				{
					// An unproven inactive writer stays subject to the normal ownership/content failure diagnostics.
				}
			}
			return verified;
		}

		private static bool VerifyMemberEffects(CollectionNativeStateIndex state, CollectionNativeModState nativeMod,
			CollectionMemberEffectPreview preview, bool verifyPluginEffects, out string failureReason,
			ISet<ModDeploymentTarget> verifiedInactiveFiles = null)
		{
			failureReason = null;
			if (state == null || nativeMod == null || preview == null || !preview.IsComplete)
			{
				failureReason = "member-effect verification prerequisites are incomplete.";
				return false;
			}
			string ownerKey = nativeMod.Identity.NativeModKey;
			foreach (CollectionPlannedFileEffect effect in preview.Files)
			{
				if (verifiedInactiveFiles != null && verifiedInactiveFiles.Contains(effect.Target)) continue;
				CollectionNativeFileState file;
				if (!state.Files.TryGetValue(effect.Target, out file))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"member file effect '{0}' is absent from authoritative native state (expected owner '{1}').", effect.Target, ownerKey);
					return false;
				}
				CollectionInstallRootDestination preserved = preview.InstallRootCorrection == null ? null : preview.InstallRootCorrection.GetPreservedDestination(effect.Target);
				if (!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, preserved == null ? ownerKey : preserved.CurrentOwnerKey) ||
					(preserved != null && !file.InstallLogOwners.Concat(file.DeploymentOwners).Concat(file.VirtualOwners).Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey))))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"member file effect '{0}' has owner '{1}', expected '{2}' (installLog={3}, virtual={4}, installLogOwners=[{5}], virtualOwners=[{6}]).",
						effect.Target, file.EffectiveOwnerKey ?? "<none>", ownerKey, file.RecordedByInstallLog,
						file.RecordedByVirtualState, FormatOwners(file.InstallLogOwners), FormatOwners(file.VirtualOwners));
					return false;
				}
				if (String.IsNullOrWhiteSpace(file.PhysicalPath) || !File.Exists(file.PhysicalPath))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"member file effect '{0}' has no existing physical file at '{1}'.", effect.Target, file.PhysicalPath ?? "<null>");
					return false;
				}
			}
			foreach (CollectionPlannedIniEffect effect in preview.IniEdits)
			{
				CollectionNativeIniState ini;
				if (!state.IniEdits.TryGetValue(effect.Key, out ini) || ini.Values.Count == 0)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture, "INI effect '{0}' is absent from authoritative native state.", effect.Key);
					return false;
				}
				CollectionNativeTextOwnerValue current = ini.Values[ini.Values.Count - 1];
				if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) ||
					!StringComparer.Ordinal.Equals(current.Value, effect.Value))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"INI effect '{0}' differs (owner expected '{1}', observed '{2}'; value expected '{3}', observed '{4}').",
						effect.Key, ownerKey, current.OwnerKey ?? "<none>", effect.Value ?? "<null>", current.Value ?? "<null>");
					return false;
				}
			}
			foreach (CollectionPlannedGameValueEffect effect in preview.GameValues)
			{
				CollectionNativeGameValueState value;
				if (!state.GameValues.TryGetValue(effect.Key, out value) || value.Values.Count == 0)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture, "game-value effect '{0}' is absent from authoritative native state.", effect.Key);
					return false;
				}
				CollectionNativeBinaryOwnerValue current = value.Values[value.Values.Count - 1];
				if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) ||
					!ByteArraysEqual(current.UnsafeValue, effect.UnsafeValue))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"game-value effect '{0}' differs (owner expected '{1}', observed '{2}', expectedBytes={3}, observedBytes={4}).",
						effect.Key, ownerKey, current.OwnerKey ?? "<none>", effect.UnsafeValue == null ? -1 : effect.UnsafeValue.Length,
						current.UnsafeValue == null ? -1 : current.UnsafeValue.Length);
					return false;
				}
			}
			if (!verifyPluginEffects) return true;

			if (preview.PluginEffects.Count > 0 && state.PluginCoverage != CollectionNativeStateCoverage.Complete)
			{
				failureReason = String.Format(CultureInfo.InvariantCulture,
					"plugin effect verification is unavailable (coverage={0}).", state.PluginCoverage);
				return false;
			}
			foreach (CollectionPlannedPluginEffect effect in preview.PluginEffects)
			{
				if (!VerifyPluginEffect(state, effect))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"plugin effect '{0}' does not match authoritative state for [{1}].", effect.Kind, String.Join(", ", effect.PluginPaths));
					return false;
				}
			}
			return true;
		}

		private static bool VerifyFileEvidence(CollectionNativeStateIndex state,
			IEnumerable<CollectionNativeFileContentEvidence> evidence, IGameMode gameMode, out string failureReason)
		{
			failureReason = null;
			try
			{
				foreach (CollectionNativeFileContentEvidence expected in evidence)
				{
					CollectionNativeFileState file;
					string path = state.Files.TryGetValue(expected.Target, out file) && !String.IsNullOrWhiteSpace(file.PhysicalPath)
						? file.PhysicalPath
						: ModDeploymentTargetResolver.GetPhysicalPath(gameMode, expected.Target);
					bool exists = !String.IsNullOrWhiteSpace(path) && File.Exists(path);
					if (expected.Existed)
					{
						if (!MatchesContentFile(path, expected.ContentHash, expected.ByteLength))
						{
							failureReason = String.Format(CultureInfo.InvariantCulture,
								"file content mismatch at '{0}' (path='{1}', expected sha256={2}, length={3}; observed {4}).",
								expected.Target, path ?? "<null>", expected.ContentHash == null ? "<null>" : expected.ContentHash.Value,
								expected.ByteLength, DescribeObservedFile(path));
							return false;
						}
					}
					else if (exists)
					{
						failureReason = String.Format(CultureInfo.InvariantCulture,
							"file '{0}' should be absent but exists at '{1}' ({2}).", expected.Target, path, DescribeObservedFile(path));
						return false;
					}
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				failureReason = "file evidence inspection failed: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
		}

		private static bool VerifyExpectedReplayAtPath(CollectionNativeChildExecutionEvidence evidence, string installInfoDirectory,
			out string failureReason, string replayPathOverride = null)
		{
			failureReason = null;
			try
			{
				string replayPath = replayPathOverride ?? ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				var cache = new ScriptedFileSelectionCache(replayPath);
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (evidence.ExpectedReplayOperations.Count == 0)
				{
					if (!cache.Exists && !Directory.Exists(payloadDirectory)) return true;
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"unexpected live replay artifacts exist (replayExists={0}, payloadDirectoryExists={1}, replayPath='{2}').",
						cache.Exists, Directory.Exists(payloadDirectory), replayPath);
					return false;
				}
				if (!cache.HasCompleteReplay)
				{
					failureReason = "expected committed scripted replay is missing or incomplete at '" + replayPath + "'.";
					return false;
				}
				IReadOnlyList<ScriptedReplayOperation> actual = cache.LoadReplayOperations();
				if (actual == null || actual.Count != evidence.ExpectedReplayOperations.Count)
				{
					IReadOnlyList<CollectionExpectedReplayOperation> missing;
					if (TryGetMissingArchiveReplayOperations(evidence.ExpectedReplayOperations, actual, out missing))
					{
						failureReason = "scripted replay is missing selected archive file(s): " + String.Join(", ", missing.Select(x => x.DestinationPath));
						return false;
					}
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"scripted replay operation count mismatch (expected={0}, observed={1}).",
						evidence.ExpectedReplayOperations.Count, actual == null ? -1 : actual.Count);
					return false;
				}

				var referencedPayloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (int index = 0; index < actual.Count; index++)
				{
					CollectionExpectedReplayOperation expected = evidence.ExpectedReplayOperations[index];
					ScriptedReplayOperation replay = actual[index];
					if (replay.Kind != expected.Kind || !StringComparer.Ordinal.Equals(replay.DestinationPath, expected.DestinationPath))
					{
						failureReason = String.Format(CultureInfo.InvariantCulture,
							"scripted replay operation {0} mismatch (expected kind={1}, destination='{2}'; observed kind={3}, destination='{4}').",
							index, expected.Kind, expected.DestinationPath, replay.Kind, replay.DestinationPath);
						return false;
					}
					if (expected.Kind == ScriptedReplayOperationKind.ArchiveFile)
					{
						if (!StringComparer.Ordinal.Equals(replay.SourcePath, expected.SourcePath))
						{
							failureReason = String.Format(CultureInfo.InvariantCulture,
								"scripted replay source mismatch at operation {0} (expected '{1}', observed '{2}').",
								index, expected.SourcePath, replay.SourcePath);
							return false;
						}
						continue;
					}
					if (replay.PayloadLength != expected.PayloadLength ||
						!StringComparer.OrdinalIgnoreCase.Equals(replay.PayloadHash, expected.PayloadSha256) ||
						String.IsNullOrWhiteSpace(replay.PayloadPath) ||
						!MatchesContentFile(replay.PayloadPath, CollectionContentHash.FromSha256(expected.PayloadSha256), expected.PayloadLength))
					{
						failureReason = String.Format(CultureInfo.InvariantCulture,
							"generated replay payload mismatch at operation {0} (expected sha256={1}, length={2}; observed path='{3}', hash={4}, length={5}).",
							index, expected.PayloadSha256, expected.PayloadLength, replay.PayloadPath ?? "<null>",
							replay.PayloadHash ?? "<null>", replay.PayloadLength);
						return false;
					}
					referencedPayloads.Add(Path.GetFullPath(replay.PayloadPath));
				}

				if (referencedPayloads.Count == 0)
				{
					if (!Directory.Exists(payloadDirectory)) return true;
					failureReason = "an unexpected scripted replay payload directory exists at '" + payloadDirectory + "'.";
					return false;
				}
				if (!Directory.Exists(payloadDirectory))
				{
					failureReason = "the expected scripted replay payload directory is missing at '" + payloadDirectory + "'.";
					return false;
				}
				string[] livePayloads = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories).Select(Path.GetFullPath).ToArray();
				if (livePayloads.Length != referencedPayloads.Count || !livePayloads.All(referencedPayloads.Contains))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"scripted replay payload set mismatch (expected files={0}, observed files={1}).", referencedPayloads.Count, livePayloads.Length);
					return false;
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				failureReason = "committed replay inspection failed: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
		}

		private static bool VerifyReplayContentEvidence(string incomingFileName, string installInfoDirectory,
			CollectionReplayContentEvidence expected, out string failureReason)
		{
			failureReason = null;
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(incomingFileName, installInfoDirectory);
				bool replayExists = File.Exists(replayPath);
				if (replayExists != expected.ReplayFileExisted)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"incoming replay preimage presence mismatch (expected={0}, observed={1}, path='{2}').",
						expected.ReplayFileExisted, replayExists, replayPath);
					return false;
				}
				if (expected.ReplayFileExisted && !MatchesContentFile(replayPath, expected.ReplayFileHash, expected.ReplayFileLength))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"incoming replay preimage content mismatch at '{0}' (expected sha256={1}, length={2}; observed {3}).",
						replayPath, expected.ReplayFileHash == null ? "<null>" : expected.ReplayFileHash.Value,
						expected.ReplayFileLength, DescribeObservedFile(replayPath));
					return false;
				}
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				bool payloadDirectoryExists = Directory.Exists(payloadDirectory);
				if (payloadDirectoryExists != expected.PayloadDirectoryExisted)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"incoming replay payload-directory presence mismatch (expected={0}, observed={1}, path='{2}').",
						expected.PayloadDirectoryExisted, payloadDirectoryExists, payloadDirectory);
					return false;
				}
				if (!expected.PayloadDirectoryExisted) return true;
				var expectedByPath = expected.Payloads.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
				string[] files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
				if (files.Length != expectedByPath.Count)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"incoming replay payload count mismatch (expected={0}, observed={1}).", expectedByPath.Count, files.Length);
					return false;
				}
				foreach (string file in files)
				{
					string relative = GetCanonicalRelativePath(payloadDirectory, file);
					CollectionReplayPayloadContentEvidence payload;
					if (!expectedByPath.TryGetValue(relative, out payload))
					{
						failureReason = "incoming replay contains unexpected payload path '" + relative + "'.";
						return false;
					}
					if (!MatchesContentFile(file, payload.ContentHash, payload.ByteLength))
					{
						failureReason = String.Format(CultureInfo.InvariantCulture,
							"incoming replay payload '{0}' content mismatch (expected sha256={1}, length={2}; observed {3}).",
							relative, payload.ContentHash.Value, payload.ByteLength, DescribeObservedFile(file));
						return false;
					}
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				failureReason = "rollback replay inspection failed: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
		}

		private static bool VerifyPreviousNativeMod(CollectionNativeChildRecoveryManifest recovery, CollectionNativeStateIndex state,
			out string failureReason)
		{
			failureReason = null;
			CollectionNativeModState previous = recovery.PreviousNativeMod;
			if (previous == null || recovery.PreviousArchive == null)
			{
				failureReason = "previous-native-mod recovery evidence is incomplete.";
				return false;
			}
			CollectionNativeModState current;
			if (!state.ModsByNativeKey.TryGetValue(previous.Identity.NativeModKey, out current))
			{
				failureReason = "previous native mod key '" + previous.Identity.NativeModKey + "' is not registered in authoritative state.";
				return false;
			}
			if (current.InstallMethod != previous.InstallMethod || current.InstallRoot != previous.InstallRoot ||
				!StringComparer.Ordinal.Equals(current.NexusModId, previous.NexusModId) ||
				!StringComparer.Ordinal.Equals(current.NexusFileId, previous.NexusFileId) ||
				!MatchesArtifactFile(current.ArchivePath, recovery.PreviousArchive))
			{
				failureReason = String.Format(CultureInfo.InvariantCulture,
					"previous native mod differs (key='{0}', method expected/observed={1}/{2}, root={3}/{4}, nexusModId='{5}'/'{6}', nexusFileId='{7}'/'{8}', archiveMatch={9}).",
					previous.Identity.NativeModKey, previous.InstallMethod, current.InstallMethod, previous.InstallRoot, current.InstallRoot,
					previous.NexusModId ?? "<null>", current.NexusModId ?? "<null>", previous.NexusFileId ?? "<null>", current.NexusFileId ?? "<null>",
					MatchesArtifactFile(current.ArchivePath, recovery.PreviousArchive));
				return false;
			}
			return true;
		}

		private static bool VerifyPreviousReplayPreimage(CollectionNativeChildRecoveryManifest recovery, string installInfoDirectory,
			out string failureReason)
		{
			failureReason = null;
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(recovery.PreviousNativeMod.FileName, installInfoDirectory);
				CollectionScriptedReplayRecoverySnapshot expected = recovery.ScriptedReplay;
				bool replayExists = File.Exists(replayPath);
				if (replayExists != expected.ReplayFileExisted)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"previous replay presence mismatch (expected={0}, observed={1}, path='{2}').", expected.ReplayFileExisted, replayExists, replayPath);
					return false;
				}
				if (expected.ReplayFileExisted && !MatchesArtifactFile(replayPath, expected.ReplayFile))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"previous replay content mismatch at '{0}' (expected sha256={1}, length={2}; observed {3}).",
						replayPath, expected.ReplayFile.ContentHash.Value, expected.ReplayFile.ByteLength, DescribeObservedFile(replayPath));
					return false;
				}
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				bool payloadDirectoryExists = Directory.Exists(payloadDirectory);
				if (payloadDirectoryExists != expected.PayloadDirectoryExisted)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"previous replay payload-directory presence mismatch (expected={0}, observed={1}, path='{2}').",
						expected.PayloadDirectoryExisted, payloadDirectoryExists, payloadDirectory);
					return false;
				}
				if (!expected.PayloadDirectoryExisted) return true;
				var expectedByPath = expected.Payloads.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
				string[] files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
				if (files.Length != expectedByPath.Count)
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"previous replay payload count mismatch (expected={0}, observed={1}).", expectedByPath.Count, files.Length);
					return false;
				}
				foreach (string file in files)
				{
					string relative = GetCanonicalRelativePath(payloadDirectory, file);
					CollectionReplayRecoveryPayload payload;
					if (!expectedByPath.TryGetValue(relative, out payload))
					{
						failureReason = "previous replay contains unexpected payload path '" + relative + "'.";
						return false;
					}
					if (!MatchesArtifactFile(file, payload.Artifact))
					{
						failureReason = String.Format(CultureInfo.InvariantCulture,
							"previous replay payload '{0}' content mismatch (expected sha256={1}, length={2}; observed {3}).",
							relative, payload.Artifact.ContentHash.Value, payload.Artifact.ByteLength, DescribeObservedFile(file));
						return false;
					}
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				failureReason = "previous replay inspection failed: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
		}

		/// <summary>
		/// Completes a successful same-owner Virtual upgrade whose exact staged postimage was not linked into the game.
		/// </summary>
		private bool TryRepairExactVirtualUpgradeLinks(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string installInfoDirectory,
			out string detail)
		{
			detail = null;
			if (recovery.TerminalStateFingerprint != null || recovery.PreviousNativeMod == null || recovery.PreviousArchive == null ||
				recovery.PreviousNativeMod.InstallMethod != ModInstallMethod.Virtual ||
				evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual ||
				((recovery.PreviousNativeMod.InstallRoot != evidence.ReviewedEffects.InstallRoot ||
				  recovery.PreviousArchive.ContentHash.Equals(recovery.IncomingArchive.ContentHash)) &&
				 (evidence.ReviewedEffects.InstallRootCorrection == null || !evidence.ReviewedEffects.InstallRootCorrection.MatchesPrevious(recovery.PreviousNativeMod) ||
				  !evidence.ReviewedEffects.InstallRootCorrection.Verify(state, true))))
				return false;

			ModManager modManager = _services.ModManager;
			CollectionNativeModState nativeMod;
			string failure;
			// Registration, archive identity, replay, ownership and plugin/configuration effects must already
			// be committed. Only deployment of the retained exact file postimage may remain incomplete.
			if (!TryVerifyCommittedState(recovery, evidence, state,
				modManager.ModRepository == null ? null : modManager.ModRepository.GameDomainName,
				installInfoDirectory, modManager.GameMode, out nativeMod, out failure, true, false) ||
				!nativeMod.Identity.Equals(recovery.PreviousNativeMod.Identity) ||
				!StringComparer.Ordinal.Equals(nativeMod.NexusModId, recovery.PreviousNativeMod.NexusModId))
				return false;

			string ownerKey = nativeMod.Identity.NativeModKey;
			VirtualModActivator backend = modManager.VirtualModActivator;
			IReadOnlyList<ModDeploymentTarget> targets;
			if (!TryCollectExactVirtualRelinkTargets(evidence, state, ownerKey, backend.GetVirtualSourceForOwner,
				out targets, out detail) || targets.Count == 0)
				return false;

			List<IMod> previous = modManager.ManagedMods.Where(x => x != null &&
				StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x.Filename), recovery.PreviousNativeMod.FileName) &&
				MatchesArtifactFile(x.Filename, recovery.PreviousArchive)).ToList();
			List<IMod> incoming = modManager.ManagedMods.Where(x => x != null &&
				StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x.Filename), evidence.IncomingFileName) &&
				MatchesArtifactFile(x.Filename, recovery.IncomingArchive)).ToList();
			if (previous.Count != 1 || incoming.Count != 1 ||
				!StringComparer.OrdinalIgnoreCase.Equals(modManager.InstallationLog.GetModKey(incoming[0]), ownerKey))
			{
				detail = "The exact previous/replacement native archive records are unavailable; links were not changed.";
				return false;
			}

			using (TransactionScope transaction = new TransactionScope())
			{
				TxFileManager files = new TxFileManager();
				backend.RebindVirtualModInfoForUpgrade(previous[0], incoming[0]);
				// Recheck after canonicalizing legacy metadata: it must resolve to the same reviewed source bytes.
				if (!TryCollectExactVirtualRelinkTargets(evidence, CaptureReloadedState(state.Target), ownerKey,
					backend.GetVirtualSourceForOwner, out targets, out failure))
					throw new InvalidDataException("Exact Virtual relink preconditions changed: " + failure);
				foreach (ModDeploymentTarget target in targets)
					backend.DeploySpecificVirtualLink(target, ownerKey, files);
				if (!TryVerifyCommittedState(recovery, evidence, CaptureReloadedState(state.Target),
					modManager.ModRepository == null ? null : modManager.ModRepository.GameDomainName,
					installInfoDirectory, modManager.GameMode, out nativeMod, out failure))
					throw new InvalidDataException("Exact Virtual relink did not satisfy the complete reviewed result: " + failure);
				transaction.Complete();
			}
			detail = String.Format(CultureInfo.InvariantCulture,
				"Relinked {0} exact reviewed replacement file(s) for native owner '{1}'; the complete native result was verified.",
				targets.Count, ownerKey);
			return true;
		}

		/// <summary>Repairs only a successful first Collection activation whose staged ownership, replay and unchanged game preimages are proven.</summary>
		private bool TryRepairExactNewActivationLinks(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string installInfoDirectory, out string detail)
		{
			detail = null;
			if (recovery.PreviousNativeMod != null || recovery.TerminalStateFingerprint != null ||
				!evidence.DeferPluginReconciliation || evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual ||
				evidence.ReviewedEffects.InstallRootCorrection != null)
				return false;
			ModManager manager = _services.ModManager;
			string domain = manager.ModRepository == null ? null : manager.ModRepository.GameDomainName;
			CollectionNativeModState nativeMod;
			string failure;
			// Verify every non-content effect, the exact registered archive and complete replay first.
			if (!TryVerifyCommittedState(recovery, evidence, state, domain, installInfoDirectory, manager.GameMode,
				out nativeMod, out failure, true, false)) return false;
			string owner = nativeMod.Identity.NativeModKey;
			IReadOnlyList<ModDeploymentTarget> targets;
			if (!TryCollectExactVirtualRelinkTargets(evidence, state, owner, manager.VirtualModActivator.GetVirtualSourceForOwner,
				out targets, out detail, manager.InstallationLog.GetCurrentFileOwnerKey) || targets.Count == 0)
				return false;
			state = CaptureReloadedState(state.Target);
			if (!TryVerifyCommittedState(recovery, evidence, state, domain, installInfoDirectory, manager.GameMode,
				out nativeMod, out failure, true, false) ||
				!TryCollectExactVirtualRelinkTargets(evidence, state, owner, manager.VirtualModActivator.GetVirtualSourceForOwner,
					out targets, out failure, manager.InstallationLog.GetCurrentFileOwnerKey))
				return false;
			using (TransactionScope transaction = new TransactionScope())
			{
				TxFileManager files = new TxFileManager();
				foreach (ModDeploymentTarget target in targets)
					manager.VirtualModActivator.DeploySpecificVirtualLink(target, owner, files);
				transaction.Complete();
			}
			// Committed-state readers cannot run inside the transaction. Keep child durability unknown until this fresh reload passes.
			if (!TryVerifyCommittedState(recovery, evidence, CaptureReloadedState(state.Target), domain,
				installInfoDirectory, manager.GameMode, out nativeMod, out failure))
			{
				detail = "Activation links were restored but authoritative verification still failed: " + failure;
				return false;
			}
			detail = String.Format(CultureInfo.InvariantCulture, "Restored {0} reviewed activation link(s) from independently owned staged files.", targets.Count);
			return true;
		}

		/// <summary>
		/// Selects only reviewed Virtual files with an exact staged postimage and an unchanged deployed preimage.
		/// </summary>
		internal static bool TryCollectExactVirtualRelinkTargets(CollectionNativeChildExecutionEvidence evidence,
			CollectionNativeStateIndex state, string ownerKey, Func<ModDeploymentTarget, string, string> getSource,
			out IReadOnlyList<ModDeploymentTarget> targets, out string failure, Func<string, string> getStagedOwner = null)
		{
			targets = new ModDeploymentTarget[0];
			failure = null;
			if (evidence == null || state == null || String.IsNullOrWhiteSpace(ownerKey) || getSource == null ||
				evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual)
				return false;
			try
			{
				var preimages = evidence.PreFileContents.ToDictionary(x => x.Target);
				var reviewedTargets = new HashSet<ModDeploymentTarget>(evidence.ExpectedFileContents.Select(x => x.Target));
				if (state.Files.Values.Any(x => !reviewedTargets.Contains(x.Target) && x.VirtualOwners.Any(y =>
					StringComparer.OrdinalIgnoreCase.Equals(y.OwnerKey, ownerKey))))
				{
					failure = "The Virtual owner has files outside this exact reviewed child; links were not changed.";
					return false;
				}
				var pending = new List<ModDeploymentTarget>();
				foreach (CollectionNativeFileContentEvidence expected in evidence.ExpectedFileContents.Where(x =>
					evidence.ReviewedEffects.Files.Any(y => y.Target.Equals(x.Target)) &&
					(evidence.ReviewedEffects.InstallRootCorrection == null || evidence.ReviewedEffects.InstallRootCorrection.GetPreservedDestination(x.Target) == null)))
				{
					CollectionNativeFileState file;
					string source = getSource(expected.Target, ownerKey);
					bool stagedOwnerProven = getStagedOwner != null && !String.IsNullOrWhiteSpace(source) &&
						StringComparer.OrdinalIgnoreCase.Equals(getStagedOwner(source), ownerKey);
					if (!state.Files.TryGetValue(expected.Target, out file) || file.Promoted ||
						!file.RecordedByVirtualState ||
						!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, ownerKey) ||
						(!stagedOwnerProven && (!file.RecordedByInstallLog || file.InstallLogOwners.Count == 0 ||
						 !StringComparer.OrdinalIgnoreCase.Equals(file.InstallLogOwners.Last().OwnerKey, ownerKey))) ||
						!MatchesContentFile(source, expected.ContentHash, expected.ByteLength))
					{
						failure = "The reviewed Virtual owner/source cannot be proven for '" + expected.Target + "'; links were not changed.";
						return false;
					}
					List<CollectionNativeOwnerState> active = file.VirtualOwners.Where(x => x.VirtualLinkActive == true).ToList();
					if (active.Count == 0 || active.Any(x => x.Kind != CollectionNativeOwnerKind.NativeMod ||
						!StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey) ||
						!(StringComparer.OrdinalIgnoreCase.Equals(x.VirtualStagedSourcePath, source) ||
						  (stagedOwnerProven && !String.IsNullOrWhiteSpace(x.VirtualStagedSourcePath) &&
						   !Path.IsPathRooted(x.VirtualStagedSourcePath) &&
						   source.EndsWith(Path.DirectorySeparatorChar + x.VirtualStagedSourcePath, StringComparison.OrdinalIgnoreCase)))))
					{
						failure = "Active Virtual ownership is ambiguous for '" + expected.Target + "'; links were not changed.";
						return false;
					}
					if (MatchesContentFile(file.PhysicalPath, expected.ContentHash, expected.ByteLength))
						continue;
					CollectionNativeFileContentEvidence preimage = preimages[expected.Target];
					if (!preimage.Existed || !MatchesContentFile(file.PhysicalPath, preimage.ContentHash, preimage.ByteLength))
					{
						failure = "The deployed file is neither the reviewed previous nor replacement bytes at '" + expected.Target + "'; links were not changed.";
						return false;
					}
					pending.Add(expected.Target);
				}
				targets = pending.AsReadOnly();
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				failure = "Exact Virtual relink inspection failed: " + exception.Message;
				return false;
			}
		}

		/// <summary>Completes an existing replay only when all recorded selections match and every omitted archive file has exact owned staging.</summary>
		private bool TryCompleteExactActivationReplay(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string currentDomain,
			string installInfoDirectory, out string detail)
		{
			detail = null;
			if (recovery.PreviousNativeMod != null || recovery.TerminalStateFingerprint != null ||
				!evidence.DeferPluginReconciliation || evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual)
				return false;
			string temporary = null;
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				var cache = new ScriptedFileSelectionCache(replayPath);
				if (!cache.HasCompleteReplay || Directory.Exists(ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath))) return false;
				byte[] before = File.ReadAllBytes(replayPath);
				IReadOnlyList<CollectionExpectedReplayOperation> missing;
				if (!TryGetMissingArchiveReplayOperations(evidence.ExpectedReplayOperations, cache.LoadReplayOperations(), out missing)) return false;
				ModManager manager = _services.ModManager;
				CollectionNativeModState nativeMod;
				string failure;
				if (!TryVerifyCommittedState(recovery, evidence, state, currentDomain, installInfoDirectory, manager.GameMode,
					out nativeMod, out failure, false, getVirtualSource: manager.VirtualModActivator.GetVirtualSourceForOwner))
				{
					detail = "Incomplete replay was not repaired: " + failure;
					return false;
				}
				IMod mod = manager.GetModByFilename(evidence.IncomingFileName);
				string owner = nativeMod.Identity.NativeModKey;
				if (mod == null || !StringComparer.OrdinalIgnoreCase.Equals(manager.InstallationLog.GetModKey(mod), owner))
				{
					detail = "Incomplete replay was not repaired because its exact native mod owner is unavailable.";
					return false;
				}
				Dictionary<ModDeploymentTarget, CollectionNativeFileContentEvidence> contents = evidence.ExpectedFileContents.ToDictionary(x => x.Target);
				foreach (CollectionExpectedReplayOperation operation in missing)
				{
					ModDeploymentTarget target = ModDeploymentTargetResolver.Resolve(manager.GameMode, mod, operation.DestinationPath, evidence.ReviewedEffects.InstallRoot);
					CollectionNativeFileContentEvidence expected;
					if (!contents.TryGetValue(target, out expected) || !expected.Existed)
					{
						detail = "Incomplete replay has no retained file evidence for '" + target + "'.";
						return false;
					}
					string source = manager.VirtualModActivator.GetVirtualSourceForOwner(target, owner);
					if (!StringComparer.OrdinalIgnoreCase.Equals(manager.InstallationLog.GetCurrentFileOwnerKey(source), owner) ||
						!MatchesContentFile(source, expected.ContentHash, expected.ByteLength))
					{
						detail = "Incomplete replay was not repaired because staged ownership or bytes differ for '" + target + "'.";
						return false;
					}
				}
				XDocument document;
				using (var stream = new MemoryStream(before, false)) document = XDocument.Load(stream);
				XElement root = document.Root;
				if (root == null || root.Name != "FileList" || root.Elements().Any(x => x.Name != "File")) return false;
				// Preserve the existing root metadata and every existing selection. Insert only proven omissions in recipe order.
				List<XElement> recorded = root.Elements().ToList();
				var selections = new List<XElement>();
				int index = 0;
				foreach (CollectionExpectedReplayOperation operation in evidence.ExpectedReplayOperations)
				{
					if (index < recorded.Count && StringComparer.Ordinal.Equals((string)recorded[index].Attribute("FileFrom"), operation.SourcePath) &&
						StringComparer.Ordinal.Equals((string)recorded[index].Attribute("FileTo"), operation.DestinationPath))
						selections.Add(new XElement(recorded[index++]));
					else selections.Add(new XElement("File", new XAttribute("FileFrom", operation.SourcePath), new XAttribute("FileTo", operation.DestinationPath)));
				}
				if (index != recorded.Count) return false;
				root.ReplaceNodes(selections);
				temporary = Path.Combine(Path.GetDirectoryName(replayPath), Nexus.Client.Util.FileUtil.CreateTempToken() + ".recovery.tmp");
				document.Save(temporary);
				if (!VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out failure, temporary)) return false;
				using (TransactionScope transaction = new TransactionScope())
				{
					if (!File.ReadAllBytes(replayPath).SequenceEqual(before)) return false;
					new TxFileManager().Copy(temporary, replayPath, true);
					transaction.Complete();
				}
				try
				{
					// Reload committed authority after the metadata transaction; accept recovery only after complete verification.
					if (!TryVerifyCommittedState(recovery, evidence, CaptureReloadedState(state.Target), currentDomain,
						installInfoDirectory, manager.GameMode, out nativeMod, out failure,
						getVirtualSource: manager.VirtualModActivator.GetVirtualSourceForOwner))
						throw new InvalidDataException("Completed replay did not satisfy the retained installation: " + failure);
				}
				catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
				{
					if (File.ReadAllBytes(replayPath).SequenceEqual(File.ReadAllBytes(temporary)))
					{
						using (TransactionScope rollback = new TransactionScope())
						{
							new TxFileManager().WriteAllBytes(replayPath, before);
							rollback.Complete();
						}
					}
					throw;
				}
				detail = String.Format(CultureInfo.InvariantCulture, "Restored {0} omitted archive-file replay entry/entries from exact owned staging; existing selections and game files were kept.", missing.Count);
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException || exception is System.Xml.XmlException)
			{
				detail = "Incomplete replay recovery failed: " + exception.Message;
				return false;
			}
			finally { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); }
		}

		/// <summary>Accepts only an unchanged ordered subset of the retained archive-file selections and identifies the omitted entries.</summary>
		internal static bool TryGetMissingArchiveReplayOperations(IReadOnlyList<CollectionExpectedReplayOperation> expected,
			IReadOnlyList<ScriptedReplayOperation> actual, out IReadOnlyList<CollectionExpectedReplayOperation> missing)
		{
			missing = new CollectionExpectedReplayOperation[0];
			if (expected == null || actual == null || actual.Count == 0 || actual.Count >= expected.Count ||
				expected.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile) || actual.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile) ||
				expected.Select(x => x.DestinationPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != expected.Count)
				return false;
			var omitted = new List<CollectionExpectedReplayOperation>();
			int index = 0;
			foreach (CollectionExpectedReplayOperation operation in expected)
			{
				if (index < actual.Count && StringComparer.Ordinal.Equals(actual[index].SourcePath, operation.SourcePath) &&
					StringComparer.Ordinal.Equals(actual[index].DestinationPath, operation.DestinationPath)) index++;
				else omitted.Add(operation);
			}
			if (index != actual.Count) return false;
			missing = omitted.AsReadOnly();
			return true;
		}

		/// <summary>Reconstructs missing archive-file replay metadata for an exactly verified first Virtual activation.</summary>
		internal static bool TryRestoreExactNewActivationReplay(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string currentDomain,
			string installInfoDirectory, IGameMode gameMode, out string detail)
		{
			detail = null;
			if (recovery == null || evidence == null || state == null || gameMode == null ||
				String.IsNullOrWhiteSpace(installInfoDirectory) || recovery.PreviousNativeMod != null || recovery.PreviousArchive != null ||
				recovery.ScriptedReplay.ReplayFileExisted || recovery.ScriptedReplay.PayloadDirectoryExisted ||
				evidence.IncomingReplayPreimage.ReplayFileExisted || evidence.IncomingReplayPreimage.PayloadDirectoryExisted ||
				evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual ||
				evidence.ReviewedEffects.IniEdits.Count != 0 || evidence.ReviewedEffects.GameValues.Count != 0 ||
				evidence.ReviewedEffects.PluginEffects.Count != 0 || evidence.ReviewedEffects.Files.Count == 0 ||
				evidence.ExpectedReplayOperations.Count == 0 ||
				evidence.ExpectedReplayOperations.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile))
				return false;

			string stagingPath = null;
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (File.Exists(replayPath) || Directory.Exists(replayPath) || Directory.Exists(payloadDirectory)) return false;

				CollectionNativeModState nativeMod;
				string failure;
				if (!TryVerifyCommittedState(recovery, evidence, state, currentDomain, installInfoDirectory, gameMode,
					out nativeMod, out failure, false))
				{
					detail = "Missing activation replay was not reconstructed: " + failure;
					return false;
				}
				string ownerKey = nativeMod.Identity.NativeModKey;
				var reviewedTargets = new HashSet<ModDeploymentTarget>(evidence.ReviewedEffects.Files.Select(x => x.Target));
				Dictionary<ModDeploymentTarget, CollectionNativeFileContentEvidence> expectedFiles = evidence.ExpectedFileContents.ToDictionary(x => x.Target);
				foreach (CollectionNativeFileState file in state.Files.Values)
				{
					bool owned = file.InstallLogOwners.Concat(file.DeploymentOwners).Concat(file.VirtualOwners)
						.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
					if ((owned && !reviewedTargets.Contains(file.Target)) || (reviewedTargets.Contains(file.Target) &&
						(file.Promoted || !file.RecordedByInstallLog || !file.RecordedByVirtualState ||
						 !file.InstallLogOwners.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey)) ||
						 !file.VirtualOwners.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey) && x.VirtualLinkActive == true &&
							MatchesContentFile(x.VirtualStagedSourcePath, expectedFiles[file.Target].ContentHash, expectedFiles[file.Target].ByteLength)))))
					{
						detail = "Missing activation replay was not reconstructed because native file ownership differs from the reviewed installation.";
						return false;
					}
				}

				// Write only metadata, through the existing replay writer. Do not rerun the installer or replace
				// game files, existing replay records, or generated payloads whose original bytes are unavailable.
				string directory = Path.GetDirectoryName(replayPath);
				Directory.CreateDirectory(directory);
				stagingPath = Path.Combine(directory, Nexus.Client.Util.FileUtil.CreateTempToken() + ".recovery.tmp");
				var cache = new ScriptedFileSelectionCache(stagingPath);
				foreach (CollectionExpectedReplayOperation replay in evidence.ExpectedReplayOperations)
					cache.RecordSelection(replay.SourcePath, replay.DestinationPath);
				if (!VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out failure, stagingPath) ||
					!TryVerifyCommittedState(recovery, evidence, state, currentDomain, installInfoDirectory, gameMode,
						out nativeMod, out failure, false))
				{
					detail = "Missing activation replay was not published: " + failure;
					return false;
				}
				if (File.Exists(replayPath) || Directory.Exists(replayPath) || Directory.Exists(payloadDirectory)) return false;
				File.Move(stagingPath, replayPath);
				stagingPath = null;
				detail = "Reconstructed the missing archive-file replay from retained execution evidence after verifying the exact installed files and native ownership.";
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				detail = "Missing activation replay recovery failed safely: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
			finally
			{
				if (stagingPath != null && File.Exists(stagingPath)) File.Delete(stagingPath);
			}
		}

		/// <summary>
		/// Restores a missing same-archive reinstall replay only when its retained XML exactly matches the reviewed recipe.
		/// </summary>
		private bool TryRestoreExactReinstallReplay(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string currentDomain,
			string installInfoDirectory, IGameMode gameMode, out string detail)
		{
			detail = null;
			if (recovery.PreviousNativeMod == null || recovery.PreviousArchive == null ||
				!recovery.PreviousArchive.ContentHash.Equals(recovery.IncomingArchive.ContentHash) ||
				recovery.PreviousArchive.ByteLength != recovery.IncomingArchive.ByteLength ||
				!state.Fingerprint.Equals(recovery.PreparationStateFingerprint) ||
				!StringComparer.OrdinalIgnoreCase.Equals(recovery.PreviousNativeMod.FileName, evidence.IncomingFileName) ||
				evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual ||
				!recovery.ScriptedReplay.ReplayFileExisted || recovery.ScriptedReplay.ReplayFile == null ||
				recovery.ScriptedReplay.PayloadDirectoryExisted || recovery.ScriptedReplay.Payloads.Count != 0 ||
				evidence.ExpectedReplayOperations.Count == 0 ||
				evidence.ExpectedReplayOperations.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile))
				return false;

			string stagingPath = null;
			bool stagingCreated = false;
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (File.Exists(replayPath) || Directory.Exists(replayPath) || Directory.Exists(payloadDirectory)) return false;

				// The native registration, ownership, exact installed bytes and plugin/effect state must already
				// satisfy the reviewed postimage. Only the missing replay XML is eligible for this repair.
				CollectionNativeModState nativeMod;
				string failure;
				if (!TryVerifyCommittedState(recovery, evidence, state, currentDomain, installInfoDirectory, gameMode,
					out nativeMod, out failure, false) || !nativeMod.Identity.Equals(recovery.PreviousNativeMod.Identity))
					return false;

				string directory = Path.GetDirectoryName(replayPath);
				Directory.CreateDirectory(directory);
				stagingPath = Path.Combine(directory, Nexus.Client.Util.FileUtil.CreateTempToken() + ".recovery.tmp");
				using (Stream retained = _manifestStore.OpenScriptedReplayRecoveryFile(recovery))
				using (var staging = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					stagingCreated = true;
					retained.CopyTo(staging);
				}

				if (!MatchesArtifactFile(stagingPath, recovery.ScriptedReplay.ReplayFile) ||
					!VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out failure, stagingPath))
				{
					detail = "Retained previous replay does not match the exact reviewed postimage; it was not restored.";
					return false;
				}
				if (File.Exists(replayPath) || Directory.Exists(replayPath) || Directory.Exists(payloadDirectory)) return false;
				File.Move(stagingPath, replayPath);
				stagingPath = null;
				detail = "Restored the exact retained same-archive replay XML for full committed-state verification.";
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				detail = "Missing reinstall replay recovery failed safely: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
			finally
			{
				if (stagingCreated && stagingPath != null && File.Exists(stagingPath)) File.Delete(stagingPath);
			}
		}

		/// <summary>Stops a failed first activation after proving its own effects were rolled back, without reverting unrelated state changes.</summary>
		/// <remarks>The additive recovery caller completes StoppedPartial and requires a fresh review; it never resumes the old reviewed plan.</remarks>
		internal static bool TryRecoverFailedUnregisteredFileActivation(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, IEnumerable<string> virtualRegistrations,
			string installInfoDirectory, IGameMode gameMode, out string detail)
		{
			detail = null;
			if (recovery == null || evidence == null || state == null || gameMode == null || virtualRegistrations == null ||
				String.IsNullOrWhiteSpace(installInfoDirectory) || recovery.PreviousNativeMod != null || recovery.PreviousArchive != null ||
				recovery.ScriptedReplay.ReplayFileExisted || recovery.ScriptedReplay.PayloadDirectoryExisted ||
				evidence.IncomingReplayPreimage.ReplayFileExisted || evidence.IncomingReplayPreimage.PayloadDirectoryExisted ||
				evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual || evidence.ReviewedEffects.Files.Count == 0 ||
				evidence.ReviewedEffects.IniEdits.Count != 0 || evidence.ReviewedEffects.GameValues.Count != 0 ||
				evidence.ReviewedEffects.PluginEffects.Count != 0 || evidence.ExpectedReplayOperations.Count == 0 ||
				evidence.ExpectedReplayOperations.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile)) return false;

			string fileName = Path.GetFileName(evidence.IncomingFileName);
			if (state.Mods.Values.Any(mod => StringComparer.OrdinalIgnoreCase.Equals(mod.FileName, fileName) ||
				(evidence.NexusFileId > 0 && StringComparer.Ordinal.Equals(mod.NexusFileId, evidence.NexusFileId.ToString(CultureInfo.InvariantCulture)) &&
				 StringComparer.Ordinal.Equals(mod.NexusModId, evidence.NexusModId.ToString(CultureInfo.InvariantCulture)))) ||
				virtualRegistrations.Any(name => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(name), fileName))) return false;

			try
			{
				string failure;
				if (!VerifyFileEvidence(state, evidence.PreFileContents, gameMode, out failure))
				{
					detail = "The failed member's game files have not returned to their saved pre-install contents: " + failure;
					return false;
				}
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				if (Directory.Exists(ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath))) return false;
				if (File.Exists(replayPath))
				{
					var cache = new ScriptedFileSelectionCache(replayPath);
					IReadOnlyList<ScriptedReplayOperation> actual = cache.LoadReplayOperations();
					IReadOnlyList<CollectionExpectedReplayOperation> missing;
					bool matches = cache.HasCompleteReplay && actual != null &&
						(actual.Count == evidence.ExpectedReplayOperations.Count
							? VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out failure)
							: TryGetMissingArchiveReplayOperations(evidence.ExpectedReplayOperations, actual, out missing));
					if (!matches)
					{
						detail = "The failed member's leftover install record does not match its retained recipe; it was preserved.";
						return false;
					}
					File.Delete(replayPath);
				}
				if (!VerifyReplayContentEvidence(evidence.IncomingFileName, installInfoDirectory, evidence.IncomingReplayPreimage, out failure))
				{
					detail = failure;
					return false;
				}
				detail = "The failed file-only member has no native or Virtual registration and its game files match the saved pre-install contents. " +
					"Any matching leftover install record is now absent. Completed mods and unrelated native state were preserved; a fresh review is required.";
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				detail = "Failed-member recovery could not finish: " + exception.Message;
				return false;
			}
		}

		private static bool TryRepairExactNewActivationRollbackResidue(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string installInfoDirectory,
			IGameMode gameMode, out string detail)
		{
			detail = null;
			if (recovery == null || evidence == null || state == null || gameMode == null || String.IsNullOrWhiteSpace(installInfoDirectory))
			{
				detail = "Exact rollback residue repair prerequisites are incomplete.";
				return false;
			}

			// This repair is deliberately narrower than general rollback. It handles only a first-time activation
			// whose authoritative native state is already the exact pre-child state, but whose C5 file/replay
			// postimage leaked to disk before native persistence failed. No prior managed bytes are reconstructed.
			if (recovery.PreviousNativeMod != null || recovery.PreviousArchive != null ||
				recovery.ScriptedReplay.ReplayFileExisted || recovery.ScriptedReplay.PayloadDirectoryExisted ||
				recovery.ScriptedReplay.Payloads.Count != 0)
			{
				detail = "Exact rollback residue repair applies only to a new activation with no previous native/replay state.";
				return false;
			}
			if (!state.Fingerprint.Equals(recovery.PreparationStateFingerprint))
			{
				detail = "Exact rollback residue repair refused because authoritative native state no longer equals the C6.6 preparation fingerprint.";
				return false;
			}
			if (evidence.ReviewedEffects.InstallMethod != ModInstallMethod.Virtual ||
				evidence.ReviewedEffects.IniEdits.Count != 0 || evidence.ReviewedEffects.GameValues.Count != 0 ||
				evidence.ReviewedEffects.PluginEffects.Count != 0 ||
				evidence.ExpectedReplayOperations.Any(x => x.Kind != ScriptedReplayOperationKind.ArchiveFile))
			{
				detail = "Exact rollback residue repair is limited to the characterized file-only Virtual activation path with archive-file replay operations.";
				return false;
			}

			List<CollectionNativeModState> incomingRegistrations = state.Mods.Values.Where(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName) &&
				MatchesArtifactFile(x.ArchivePath, recovery.IncomingArchive)).ToList();
			if (incomingRegistrations.Count != 0)
			{
				detail = "Exact rollback residue repair refused because an exact incoming native registration still exists.";
				return false;
			}

			CollectionReplayContentEvidence replayPreimage = evidence.IncomingReplayPreimage;
			if (replayPreimage.ReplayFileExisted || replayPreimage.PayloadDirectoryExisted || replayPreimage.Payloads.Count != 0)
			{
				detail = "Exact rollback residue repair cannot reconstruct a pre-existing incoming replay; manual recovery remains required.";
				return false;
			}

			Dictionary<ModDeploymentTarget, CollectionNativeFileContentEvidence> expectedByTarget =
				evidence.ExpectedFileContents.ToDictionary(x => x.Target);
			var filesToDelete = new List<KeyValuePair<string, CollectionNativeFileContentEvidence>>();
			try
			{
				foreach (CollectionNativeFileContentEvidence preimage in evidence.PreFileContents)
				{
					CollectionNativeFileState fileState;
					string path = state.Files.TryGetValue(preimage.Target, out fileState) && !String.IsNullOrWhiteSpace(fileState.PhysicalPath)
						? fileState.PhysicalPath
						: ModDeploymentTargetResolver.GetPhysicalPath(gameMode, preimage.Target);
					if (preimage.Existed)
					{
						if (!MatchesContentFile(path, preimage.ContentHash, preimage.ByteLength))
						{
							detail = String.Format(CultureInfo.InvariantCulture,
								"Exact rollback residue repair cannot restore changed pre-existing file '{0}' (observed {1}).",
								preimage.Target, DescribeObservedFile(path));
							return false;
						}
						continue;
					}

					if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
						continue;
					CollectionNativeFileContentEvidence expectedPostimage;
					if (!expectedByTarget.TryGetValue(preimage.Target, out expectedPostimage) || !expectedPostimage.Existed ||
						!MatchesContentFile(path, expectedPostimage.ContentHash, expectedPostimage.ByteLength))
					{
						detail = String.Format(CultureInfo.InvariantCulture,
							"Exact rollback residue repair refused unexpected bytes at '{0}' (observed {1}).",
							preimage.Target, DescribeObservedFile(path));
						return false;
					}
					filesToDelete.Add(new KeyValuePair<string, CollectionNativeFileContentEvidence>(path, expectedPostimage));
				}

				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				bool replayResidueExists = File.Exists(replayPath) || Directory.Exists(payloadDirectory);
				if (replayResidueExists)
				{
					string replayFailure;
					if (!VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out replayFailure))
					{
						detail = "Exact rollback residue repair refused non-postimage replay artifacts: " +
							(replayFailure ?? "no specific replay mismatch was recorded");
						return false;
					}
				}

				if (filesToDelete.Count == 0 && !replayResidueExists)
				{
					detail = "No exact rollback residue was found to repair.";
					return false;
				}

				// Recheck every destructive target immediately before mutation. A concurrent/manual change causes a
				// fail-closed result rather than deleting bytes that were not produced by this exact child.
				foreach (KeyValuePair<string, CollectionNativeFileContentEvidence> item in filesToDelete)
					if (!MatchesContentFile(item.Key, item.Value.ContentHash, item.Value.ByteLength))
					{
						detail = "Exact rollback residue changed before cleanup at '" + item.Key + "'.";
						return false;
					}
				if (replayResidueExists)
				{
					string replayFailure;
					if (!VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out replayFailure))
					{
						detail = "Exact rollback replay residue changed before cleanup: " +
							(replayFailure ?? "no specific replay mismatch was recorded");
						return false;
					}
				}

				foreach (KeyValuePair<string, CollectionNativeFileContentEvidence> item in filesToDelete)
					File.Delete(item.Key);
				if (replayResidueExists)
					ScriptedFileSelectionCache.DeleteArtifacts(replayPath);

				detail = String.Format(CultureInfo.InvariantCulture,
					"Removed {0} exact child postimage file residue(s){1}; authoritative native state was already at the exact preparation fingerprint.",
					filesToDelete.Count, replayResidueExists ? " and the exact incoming replay residue" : String.Empty);
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				detail = "Exact rollback residue repair failed safely: " + exception.GetType().Name + ": " + exception.Message;
				return false;
			}
		}

		private static string BuildVerificationDiagnostics(bool committed, string committedFailure,
			bool rolledBack, string rolledBackFailure, bool rollbackResidueRepaired, string rollbackRepairDetail)
		{
			string baseDiagnostics = String.Format(CultureInfo.InvariantCulture,
				"C6.9 committed verification: {0}. C6.9 rollback verification: {1}.",
				committed ? "PASS" : "FAIL - " + (committedFailure ?? "no specific failure was recorded"),
				rolledBack ? "PASS" : "FAIL - " + (rolledBackFailure ?? "no specific failure was recorded"));
			if (String.IsNullOrWhiteSpace(rollbackRepairDetail)) return baseDiagnostics;
			return baseDiagnostics + " C6.9 exact rollback residue repair: " +
				(rollbackResidueRepaired ? "APPLIED - " : "NOT APPLIED - ") + rollbackRepairDetail;
		}

		private static string FormatOwners(IEnumerable<CollectionNativeOwnerState> owners)
		{
			if (owners == null) return String.Empty;
			return String.Join(", ", owners.Select(x => String.Format(CultureInfo.InvariantCulture,
				"{0}:active={1}:priority={2}", x.OwnerKey ?? x.UnresolvedReference,
				x.VirtualLinkActive.HasValue ? x.VirtualLinkActive.Value.ToString() : "n/a",
				x.VirtualPriority.HasValue ? x.VirtualPriority.Value.ToString(CultureInfo.InvariantCulture) : "n/a")));
		}

		private static string DescribeObservedFile(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) return "path unavailable";
			if (!File.Exists(path)) return "missing";
			try
			{
				var info = new FileInfo(path);
				using (SHA256 sha = SHA256.Create())
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
				{
					string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
					return String.Format(CultureInfo.InvariantCulture, "sha256={0}, length={1}", hash, info.Length);
				}
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException)
			{
				return "unreadable (" + exception.GetType().Name + ": " + exception.Message + ")";
			}
		}

		private static bool VerifyPluginEffect(CollectionNativeStateIndex state, CollectionPlannedPluginEffect effect)
		{
			if (effect.Kind == CollectionPlannedPluginEffectKind.Activation)
			{
				CollectionNativePluginState plugin = FindPlugin(state, effect.PluginPaths[0]);
				return plugin != null && plugin.Active == effect.Active.Value;
			}
			if (effect.Kind == CollectionPlannedPluginEffectKind.AbsoluteOrderIndex)
			{
				CollectionNativePluginState plugin = FindPlugin(state, effect.PluginPaths[0]);
				return plugin != null && (state.IsGameManagedPluginOrder(plugin) || plugin.Priority == effect.AbsoluteIndex.Value);
			}
			if (effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder)
			{
				return CollectionPluginRelativeOrderApplicator.IsOrderSatisfied(state, effect.PluginPaths);
			}
			return false;
		}

		private static CollectionNativePluginState FindPlugin(CollectionNativeStateIndex state, string path)
		{
			string fileName = Path.GetFileName((path ?? String.Empty).Replace('/', Path.DirectorySeparatorChar));
			if (String.IsNullOrWhiteSpace(fileName)) return null;
			CollectionNativePluginState plugin;
			if (state.Plugins.TryGetValue(fileName, out plugin)) return plugin;
			return state.Plugins.Values.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(x.FileName), fileName));
		}

		/// <summary>Recovers a content-check-free install only when its durable committed result and sealed terminal state still agree.</summary>
		internal static bool TryVerifyRecordedCommittedState(CollectionNativeChildRecoveryManifest recovery, CollectionNativeChildOperation child,
			CollectionResolvedPlanRecord plan, CollectionNativeStateIndex state, out CollectionNativeModState nativeMod)
		{
			nativeMod = null;
			if (recovery == null || child == null || plan == null || state == null || child.NativeResult == null ||
				child.NativeResult.ReportedStatus != ModOperationReportedStatus.Succeeded ||
				child.NativeResult.Durability != ModOperationDurability.VerifiedCommitted || recovery.TerminalStateFingerprint == null ||
				!recovery.TerminalStateFingerprint.Equals(state.Fingerprint) || !plan.Target.Equals(state.Target) ||
				!child.Member.Revision.Equals(plan.Revision) || !recovery.PlanIdentity.Equals(plan.Identity) ||
				!recovery.Member.MemberKey.Equals(child.Member.MemberKey) ||
				!recovery.NativeOperation.Fingerprint.Equals(child.NativeOperation.Fingerprint)) return false;
			List<CollectionNativeModState> candidates = state.Mods.Values.Where(x =>
				x.InstallMethod == child.NativeOperation.Fingerprint.InstallMethod && x.InstallRoot == child.NativeOperation.Fingerprint.InstallRoot &&
				MatchesArtifactFile(x.ArchivePath, recovery.IncomingArchive)).ToList();
			if (candidates.Count != 1) return false;
			nativeMod = candidates[0];
			return true;
		}

		private static bool MatchesArtifactFile(string path, CollectionRecoveryArtifact expected)
		{
			if (expected == null) return false;
			try
			{
				return CollectionArchiveContentMatcher.MatchesFile(path, expected.ByteLength, expected.ContentHash,
					CancellationToken.None, true);
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException)
			{
				return false;
			}
		}

		private static bool MatchesContentFile(string path, CollectionContentHash expectedHash, long expectedLength)
		{
			if (String.IsNullOrWhiteSpace(path) || expectedHash == null || expectedHash.Algorithm != CollectionContentHashAlgorithm.Sha256 || !File.Exists(path)) return false;
			try
			{
				var info = new FileInfo(path);
				if (info.Length != expectedLength) return false;
				using (SHA256 sha = SHA256.Create())
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
				{
					string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", String.Empty).ToLowerInvariant();
					return StringComparer.OrdinalIgnoreCase.Equals(actual, expectedHash.Value);
				}
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException)
			{
				return false;
			}
		}

		private static string GetCanonicalRelativePath(string rootDirectory, string filePath)
		{
			string root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string full = Path.GetFullPath(filePath);
			if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A scripted replay payload escaped its expected sidecar directory.");
			return full.Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
		}

		private static bool IsVerificationIoException(Exception exception)
		{
			return exception is IOException || exception is UnauthorizedAccessException || exception is NotSupportedException ||
				exception is System.Security.SecurityException || exception is CryptographicException;
		}

		private static bool ByteArraysEqual(byte[] left, byte[] right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Length != right.Length) return false;
			for (int index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
			return true;
		}

		private CollectionOperation SaveChild(CollectionOperation operation, CollectionNativeChildOperation child)
		{
			var children = operation.NativeChildren.Select(x => x.Sequence == child.Sequence ? child : x).ToList();
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1), operation.Phase,
				operation.ResultState, children);
			_operationStore.SaveOperation(updated);
			CollectionOperation persisted = _operationStore.GetOperation(updated.Identity);
			if (persisted == null) throw new InvalidDataException("The Collection operation disappeared after restart reconciliation checkpointing.");
			return persisted;
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}

	/// <summary>Result of C6.9 restart reconciliation for one previously submitted native child.</summary>
	/// <remarks>C6.10 still owns Collection association/provenance writes and the child's Reconciled checkpoint.</remarks>
	public sealed class CollectionNativeChildRestartReconciliationResult
	{
		/// <summary>Creates one immutable C6.9 restart-reconciliation result from the reloaded authoritative state.</summary>
		internal CollectionNativeChildRestartReconciliationResult(CollectionOperation operation,
			CollectionNativeChildOperation child, CollectionNativeStateIndex nativeState,
			CollectionNativeModState verifiedNativeMod, bool hadExactExecutionEvidence,
			ModOperationDurability priorDurability, string verificationDiagnostics)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Child = child ?? throw new ArgumentNullException(nameof(child));
			NativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
			VerifiedNativeMod = verifiedNativeMod;
			HadExactExecutionEvidence = hadExactExecutionEvidence;
			PriorDurability = priorDurability;
			VerificationDiagnostics = verificationDiagnostics ?? String.Empty;
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved || child.NativeResult == null)
				throw new ArgumentException("A C6.9 result requires a durable NativeTerminalObserved child result.", nameof(child));
		}

		public CollectionOperation Operation { get; }
		public CollectionNativeChildOperation Child { get; }
		public CollectionNativeStateIndex NativeState { get; }
		public CollectionNativeModState VerifiedNativeMod { get; }
		public bool HadExactExecutionEvidence { get; }
		public ModOperationDurability PriorDurability { get; }
		/// <summary>Gets the exact C6.9 committed/rollback predicate result used to classify this restart state.</summary>
		public string VerificationDiagnostics { get; }
		public ModOperationDurability VerifiedDurability { get { return Child.NativeResult.Durability; } }
		public bool HasVerifiedCommit { get { return VerifiedDurability == ModOperationDurability.VerifiedCommitted; } }
		public bool RequiresRecovery { get { return VerifiedDurability == ModOperationDurability.Unknown; } }
	}
}
