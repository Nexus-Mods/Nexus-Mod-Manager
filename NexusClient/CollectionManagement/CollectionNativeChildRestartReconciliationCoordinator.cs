using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Reconciles one Collection native child whose process-local C6.7/C6.8 execution context was lost across restart.
	/// </summary>
	/// <remarks>
	/// C6.9 never resubmits native work. It first runs native deployment/VMA recovery through the C4 authority reload,
	/// then compares the exact persisted child/evidence with authoritative native reality. Unknown reality remains
	/// RecoveryRequired for explicit later recovery rather than being guessed or replayed.
	/// </remarks>
	public sealed class CollectionNativeChildRestartReconciliationCoordinator
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

		/// <summary>Reconciles one restart-ambiguous native child while honoring cancellation before authority inspection begins.</summary>
		public async Task<CollectionNativeChildRestartReconciliationResult> ReconcileAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (_services.ModManager == null)
				throw new InvalidOperationException("C6.9 requires the live ModManager service.");

			CollectionOperation operation = RequireRecoverableOperation(operationIdentity);
			CollectionNativeChildOperation child = RequireRestartChild(operation);
			CollectionResolvedPlanRecord plan = RequirePersistedPlan(operation);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(operation.Target) || !authority.Target.Equals(plan.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the persisted Collection recovery target.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (operation.Phase != CollectionOperationPhase.Recovering)
					operation = _operationCoordinator.BeginRecovery(operationIdentity);

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
						paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod, out committedFailure);
					bool rolledBack = evidence != null && TryVerifyRolledBackState(recovery, evidence, state,
						paths.InstallInfoPath, _services.ModManager.GameMode, out rolledBackFailure);
					string replayRepairDetail = null;
					if (evidence != null && !committed && !rolledBack && priorDurability == ModOperationDurability.Unknown &&
						reportedStatus == ModOperationReportedStatus.Succeeded && TryRestoreExactReinstallReplay(recovery, evidence, state,
							_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
							paths.InstallInfoPath, _services.ModManager.GameMode, out replayRepairDetail))
					{
						state = CaptureReloadedState(operation.Target);
						committed = TryVerifyCommittedState(recovery, evidence, state,
							_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
							paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod, out committedFailure);
					}
					bool rollbackResidueRepaired = false;
					string rollbackRepairDetail = null;
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
						? "C6.9 execution evidence is missing."
						: BuildVerificationDiagnostics(committed, committedFailure, rolledBack, rolledBackFailure,
							rollbackResidueRepaired, rollbackRepairDetail);
					if (!String.IsNullOrWhiteSpace(replayRepairDetail))
						verificationDiagnostics += " Reinstall replay recovery: " + replayRepairDetail;
					ModOperationDurability durability = evidence == null
						? ModOperationDurability.Unknown
						: DetermineRestartDurability(priorDurability, committed, rolledBack);

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
						operation = _operationCoordinator.MarkRecoveryRequired(operation.Identity);

					return new CollectionNativeChildRestartReconciliationResult(operation, child, state, verifiedNativeMod,
						evidence != null, priorDurability, verificationDiagnostics);
				}
				catch
				{
					TryMarkRecoveryRequired(operationIdentity);
					throw;
				}
			}
		}

		private void TryMarkRecoveryRequired(CollectionOperationIdentity identity)
		{
			try
			{
				CollectionOperation current = _operationStore.GetOperation(identity);
				if (current != null && current.Phase == CollectionOperationPhase.Recovering && current.HasUnreconciledNativeChild)
					_operationCoordinator.MarkRecoveryRequired(identity);
			}
			catch
			{
				// Preserve the original native-recovery/reload exception. A failed feature-store update is itself reconciled on the next restart.
			}
		}

		private CollectionOperation RequireRecoverableOperation(CollectionOperationIdentity identity)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null)
				throw new InvalidOperationException("The Collection operation is not present in the durable operation journal.");
			if (operation.Kind != CollectionOperationKind.ApplyResolvedPlan ||
				(operation.ResultState != CollectionOperationResultState.Pending &&
				 operation.ResultState != CollectionOperationResultState.RecoveryRequired))
				throw new InvalidOperationException("C6.9 currently reconciles only additive Collection apply operations that crossed the native boundary.");
			switch (operation.Phase)
			{
				case CollectionOperationPhase.ApplyingNativeChildren:
				case CollectionOperationPhase.PausedAtSafeBoundary:
				case CollectionOperationPhase.Verifying:
				case CollectionOperationPhase.Recovering:
				case CollectionOperationPhase.RecoveryRequired:
					break;
				default:
					throw new InvalidOperationException("The Collection operation is not at a restart-reconcilable native boundary.");
			}
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

		private CollectionResolvedPlanRecord RequirePersistedPlan(CollectionOperation operation)
		{
			if (operation.PlanIdentity == null || operation.Revision == null)
				throw new InvalidOperationException("A restart-reconcilable Collection operation must retain its exact plan and revision identities.");
			CollectionResolvedPlanRecord plan = _planStore.GetPlan(operation.PlanIdentity);
			if (plan == null || !plan.Revision.Equals(operation.Revision) || !plan.Target.Equals(operation.Target) ||
				plan.PolicyKind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new InvalidOperationException("The exact persisted additive plan required for restart reconciliation is unavailable or mismatched.");
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
				throw new InvalidOperationException("The unreconciled child is not at a supported C6.9 additive restart boundary.");
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

		private static bool TryVerifyCommittedState(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string currentDomain,
			string installInfoDirectory, IGameMode gameMode, out CollectionNativeModState nativeMod, out string failureReason,
			bool verifyReplay = true)
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
			if (!isNexus && !isBundle)
			{
				failureReason = "selected artifact is neither an exact Nexus mod-file nor a retained Collection bundle.";
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
			var failures = new List<string>();
			string detail;
			if (!VerifyMemberEffects(state, nativeMod, evidence.ReviewedEffects, out detail)) failures.Add(detail);
			if (!VerifyFileEvidence(state, evidence.ExpectedFileContents, gameMode, out detail)) failures.Add(detail);
			if (verifyReplay && !VerifyExpectedReplayAtPath(evidence, installInfoDirectory, out detail)) failures.Add(detail);
			if (failures.Count != 0)
			{
				failureReason = String.Join("; ", failures.Where(x => !String.IsNullOrWhiteSpace(x)));
				nativeMod = null;
				return false;
			}
			return true;
		}

		private static bool TryVerifyRolledBackState(CollectionNativeChildRecoveryManifest recovery,
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

		private static bool VerifyMemberEffects(CollectionNativeStateIndex state, CollectionNativeModState nativeMod,
			CollectionMemberEffectPreview preview, out string failureReason)
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
				CollectionNativeFileState file;
				if (!state.Files.TryGetValue(effect.Target, out file))
				{
					failureReason = String.Format(CultureInfo.InvariantCulture,
						"member file effect '{0}' is absent from authoritative native state (expected owner '{1}').", effect.Target, ownerKey);
					return false;
				}
				if (!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, ownerKey))
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
				stagingPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".recovery.tmp");
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
				return plugin != null && plugin.Priority == effect.AbsoluteIndex.Value;
			}
			if (effect.Kind == CollectionPlannedPluginEffectKind.RelativeOrder)
			{
				int previous = Int32.MinValue;
				foreach (string path in effect.PluginPaths)
				{
					CollectionNativePluginState plugin = FindPlugin(state, path);
					if (plugin == null || plugin.Priority <= previous) return false;
					previous = plugin.Priority;
				}
				return true;
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

		private static bool MatchesArtifactFile(string path, CollectionRecoveryArtifact expected)
		{
			return expected != null && MatchesContentFile(path, expected.ContentHash, expected.ByteLength);
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
