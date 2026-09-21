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
					bool committed = evidence != null && TryVerifyCommittedState(recovery, evidence, state,
						_services.ModManager.ModRepository == null ? null : _services.ModManager.ModRepository.GameDomainName,
						paths.InstallInfoPath, _services.ModManager.GameMode, out verifiedNativeMod);
					bool rolledBack = evidence != null && TryVerifyRolledBackState(recovery, evidence, state,
						paths.InstallInfoPath, _services.ModManager.GameMode);
					ModOperationDurability durability = evidence == null
						? ModOperationDurability.Unknown
						: DetermineRestartDurability(priorDurability, committed, rolledBack);

					var reconciledResult = new ModOperationResult(child.NativeOperation, reportedStatus, durability, reportedMessage);
					child = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
						CollectionNativeChildCheckpoint.NativeTerminalObserved, reconciledResult);
					operation = SaveChild(operation, child);

					if (durability == ModOperationDurability.Unknown)
						operation = _operationCoordinator.MarkRecoveryRequired(operation.Identity);

					return new CollectionNativeChildRestartReconciliationResult(operation, child, state, verifiedNativeMod,
						evidence != null, priorDurability);
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
			string installInfoDirectory, IGameMode gameMode, out CollectionNativeModState nativeMod)
		{
			nativeMod = null;
			if (recovery == null || evidence == null || state == null || gameMode == null || !evidence.ReviewedEffects.IsComplete ||
				String.IsNullOrWhiteSpace(installInfoDirectory) ||
				!StringComparer.OrdinalIgnoreCase.Equals(currentDomain, evidence.NexusGameDomain))
				return false;

			string modId = evidence.NexusModId.ToString(CultureInfo.InvariantCulture);
			string fileId = evidence.NexusFileId.ToString(CultureInfo.InvariantCulture);
			List<CollectionNativeModState> candidates = state.Mods.Values.Where(x =>
				StringComparer.Ordinal.Equals(x.NexusModId, modId) && StringComparer.Ordinal.Equals(x.NexusFileId, fileId) &&
				x.InstallMethod == evidence.ReviewedEffects.InstallMethod && x.InstallRoot == evidence.ReviewedEffects.InstallRoot &&
				StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName) &&
				MatchesArtifactFile(x.ArchivePath, recovery.IncomingArchive)).ToList();
			if (candidates.Count != 1)
				return false;

			nativeMod = candidates[0];
			if (!VerifyMemberEffects(state, nativeMod, evidence.ReviewedEffects) ||
				!VerifyFileEvidence(state, evidence.ExpectedFileContents, gameMode) ||
				!VerifyExpectedReplayAtPath(evidence, installInfoDirectory))
			{
				nativeMod = null;
				return false;
			}
			return true;
		}

		private static bool TryVerifyRolledBackState(CollectionNativeChildRecoveryManifest recovery,
			CollectionNativeChildExecutionEvidence evidence, CollectionNativeStateIndex state, string installInfoDirectory, IGameMode gameMode)
		{
			if (recovery == null || evidence == null || state == null || gameMode == null || String.IsNullOrWhiteSpace(installInfoDirectory))
				return false;
			if (!state.Fingerprint.Equals(recovery.PreparationStateFingerprint) ||
				!VerifyFileEvidence(state, evidence.PreFileContents, gameMode) ||
				!VerifyReplayContentEvidence(evidence.IncomingFileName, installInfoDirectory, evidence.IncomingReplayPreimage))
				return false;

			if (recovery.PreviousNativeMod == null)
				return recovery.PreviousArchive == null && !recovery.ScriptedReplay.ReplayFileExisted &&
					!recovery.ScriptedReplay.PayloadDirectoryExisted && recovery.ScriptedReplay.Payloads.Count == 0;

			return VerifyPreviousNativeMod(recovery, state) && VerifyPreviousReplayPreimage(recovery, installInfoDirectory);
		}

		private static bool VerifyMemberEffects(CollectionNativeStateIndex state, CollectionNativeModState nativeMod,
			CollectionMemberEffectPreview preview)
		{
			if (state == null || nativeMod == null || preview == null || !preview.IsComplete) return false;
			string ownerKey = nativeMod.Identity.NativeModKey;
			foreach (CollectionPlannedFileEffect effect in preview.Files)
			{
				CollectionNativeFileState file;
				if (!state.Files.TryGetValue(effect.Target, out file) ||
					!StringComparer.OrdinalIgnoreCase.Equals(file.EffectiveOwnerKey, ownerKey) ||
					String.IsNullOrWhiteSpace(file.PhysicalPath) || !File.Exists(file.PhysicalPath)) return false;
			}
			foreach (CollectionPlannedIniEffect effect in preview.IniEdits)
			{
				CollectionNativeIniState ini;
				if (!state.IniEdits.TryGetValue(effect.Key, out ini) || ini.Values.Count == 0) return false;
				CollectionNativeTextOwnerValue current = ini.Values[ini.Values.Count - 1];
				if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) ||
					!StringComparer.Ordinal.Equals(current.Value, effect.Value)) return false;
			}
			foreach (CollectionPlannedGameValueEffect effect in preview.GameValues)
			{
				CollectionNativeGameValueState value;
				if (!state.GameValues.TryGetValue(effect.Key, out value) || value.Values.Count == 0) return false;
				CollectionNativeBinaryOwnerValue current = value.Values[value.Values.Count - 1];
				if (!StringComparer.OrdinalIgnoreCase.Equals(current.OwnerKey, ownerKey) ||
					!ByteArraysEqual(current.UnsafeValue, effect.UnsafeValue)) return false;
			}
			if (preview.PluginEffects.Count > 0 && state.PluginCoverage != CollectionNativeStateCoverage.Complete) return false;
			foreach (CollectionPlannedPluginEffect effect in preview.PluginEffects)
				if (!VerifyPluginEffect(state, effect)) return false;
			return true;
		}

		private static bool VerifyFileEvidence(CollectionNativeStateIndex state,
			IEnumerable<CollectionNativeFileContentEvidence> evidence, IGameMode gameMode)
		{
			try
			{
				foreach (CollectionNativeFileContentEvidence expected in evidence)
				{
					CollectionNativeFileState file;
					string path = state.Files.TryGetValue(expected.Target, out file) && !String.IsNullOrWhiteSpace(file.PhysicalPath)
						? file.PhysicalPath
						: ModDeploymentTargetResolver.GetPhysicalPath(gameMode, expected.Target);
					if (expected.Existed)
					{
						if (!MatchesContentFile(path, expected.ContentHash, expected.ByteLength)) return false;
					}
					else if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) return false;
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				return false;
			}
		}

		private static bool VerifyExpectedReplayAtPath(CollectionNativeChildExecutionEvidence evidence, string installInfoDirectory)
		{
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(evidence.IncomingFileName, installInfoDirectory);
				var cache = new ScriptedFileSelectionCache(replayPath);
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (evidence.ExpectedReplayOperations.Count == 0)
					return !cache.Exists && !Directory.Exists(payloadDirectory);
				if (!cache.HasCompleteReplay) return false;
				IReadOnlyList<ScriptedReplayOperation> actual = cache.LoadReplayOperations();
				if (actual == null || actual.Count != evidence.ExpectedReplayOperations.Count) return false;

				var referencedPayloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				for (int index = 0; index < actual.Count; index++)
				{
					CollectionExpectedReplayOperation expected = evidence.ExpectedReplayOperations[index];
					ScriptedReplayOperation replay = actual[index];
					if (replay.Kind != expected.Kind || !StringComparer.Ordinal.Equals(replay.DestinationPath, expected.DestinationPath)) return false;
					if (expected.Kind == ScriptedReplayOperationKind.ArchiveFile)
					{
						if (!StringComparer.Ordinal.Equals(replay.SourcePath, expected.SourcePath)) return false;
						continue;
					}
					if (replay.PayloadLength != expected.PayloadLength ||
						!StringComparer.OrdinalIgnoreCase.Equals(replay.PayloadHash, expected.PayloadSha256) ||
						String.IsNullOrWhiteSpace(replay.PayloadPath) ||
						!MatchesContentFile(replay.PayloadPath, CollectionContentHash.FromSha256(expected.PayloadSha256), expected.PayloadLength)) return false;
					referencedPayloads.Add(Path.GetFullPath(replay.PayloadPath));
				}

				if (referencedPayloads.Count == 0) return !Directory.Exists(payloadDirectory);
				if (!Directory.Exists(payloadDirectory)) return false;
				string[] livePayloads = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories).Select(Path.GetFullPath).ToArray();
				return livePayloads.Length == referencedPayloads.Count && livePayloads.All(referencedPayloads.Contains);
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				return false;
			}
		}

		private static bool VerifyReplayContentEvidence(string incomingFileName, string installInfoDirectory,
			CollectionReplayContentEvidence expected)
		{
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(incomingFileName, installInfoDirectory);
				if (File.Exists(replayPath) != expected.ReplayFileExisted) return false;
				if (expected.ReplayFileExisted && !MatchesContentFile(replayPath, expected.ReplayFileHash, expected.ReplayFileLength)) return false;
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (Directory.Exists(payloadDirectory) != expected.PayloadDirectoryExisted) return false;
				if (!expected.PayloadDirectoryExisted) return true;
				var expectedByPath = expected.Payloads.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
				string[] files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
				if (files.Length != expectedByPath.Count) return false;
				foreach (string file in files)
				{
					string relative = GetCanonicalRelativePath(payloadDirectory, file);
					CollectionReplayPayloadContentEvidence payload;
					if (!expectedByPath.TryGetValue(relative, out payload) ||
						!MatchesContentFile(file, payload.ContentHash, payload.ByteLength)) return false;
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				return false;
			}
		}

		private static bool VerifyPreviousNativeMod(CollectionNativeChildRecoveryManifest recovery, CollectionNativeStateIndex state)
		{
			CollectionNativeModState previous = recovery.PreviousNativeMod;
			if (previous == null || recovery.PreviousArchive == null) return false;
			CollectionNativeModState current;
			if (!state.ModsByNativeKey.TryGetValue(previous.Identity.NativeModKey, out current)) return false;
			return current.InstallMethod == previous.InstallMethod && current.InstallRoot == previous.InstallRoot &&
				StringComparer.Ordinal.Equals(current.NexusModId, previous.NexusModId) &&
				StringComparer.Ordinal.Equals(current.NexusFileId, previous.NexusFileId) &&
				MatchesArtifactFile(current.ArchivePath, recovery.PreviousArchive);
		}

		private static bool VerifyPreviousReplayPreimage(CollectionNativeChildRecoveryManifest recovery, string installInfoDirectory)
		{
			try
			{
				string replayPath = ScriptedFileSelectionCache.GetDefaultFilePath(recovery.PreviousNativeMod.FileName, installInfoDirectory);
				CollectionScriptedReplayRecoverySnapshot expected = recovery.ScriptedReplay;
				if (File.Exists(replayPath) != expected.ReplayFileExisted) return false;
				if (expected.ReplayFileExisted && !MatchesArtifactFile(replayPath, expected.ReplayFile)) return false;
				string payloadDirectory = ScriptedFileSelectionCache.GetPayloadDirectoryPath(replayPath);
				if (Directory.Exists(payloadDirectory) != expected.PayloadDirectoryExisted) return false;
				if (!expected.PayloadDirectoryExisted) return true;
				var expectedByPath = expected.Payloads.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
				string[] files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories);
				if (files.Length != expectedByPath.Count) return false;
				foreach (string file in files)
				{
					string relative = GetCanonicalRelativePath(payloadDirectory, file);
					CollectionReplayRecoveryPayload payload;
					if (!expectedByPath.TryGetValue(relative, out payload) || !MatchesArtifactFile(file, payload.Artifact)) return false;
				}
				return true;
			}
			catch (Exception exception) when (IsVerificationIoException(exception) || exception is ArgumentException || exception is InvalidOperationException)
			{
				return false;
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
			ModOperationDurability priorDurability)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Child = child ?? throw new ArgumentNullException(nameof(child));
			NativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
			VerifiedNativeMod = verifiedNativeMod;
			HadExactExecutionEvidence = hadExactExecutionEvidence;
			PriorDurability = priorDurability;
			if (child.Checkpoint != CollectionNativeChildCheckpoint.NativeTerminalObserved || child.NativeResult == null)
				throw new ArgumentException("A C6.9 result requires a durable NativeTerminalObserved child result.", nameof(child));
		}

		public CollectionOperation Operation { get; }
		public CollectionNativeChildOperation Child { get; }
		public CollectionNativeStateIndex NativeState { get; }
		public CollectionNativeModState VerifiedNativeMod { get; }
		public bool HadExactExecutionEvidence { get; }
		public ModOperationDurability PriorDurability { get; }
		public ModOperationDurability VerifiedDurability { get { return Child.NativeResult.Durability; } }
		public bool HasVerifiedCommit { get { return VerifiedDurability == ModOperationDurability.VerifiedCommitted; } }
		public bool RequiresRecovery { get { return VerifiedDurability == ModOperationDurability.Unknown; } }
	}
}
