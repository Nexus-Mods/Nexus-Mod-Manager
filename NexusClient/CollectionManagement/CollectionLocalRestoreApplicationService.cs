using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Immutable explicit-review payload for one sealed Local Collection restore.</summary>
	public sealed class CollectionLocalRestorePreview
	{
		internal CollectionLocalRestorePreview(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan plan)
		{
			SealedCapture = sealedCapture ?? throw new ArgumentNullException(nameof(sealedCapture));
			Plan = plan ?? throw new ArgumentNullException(nameof(plan));
		}

		public CollectionSealedCaptureSnapshot SealedCapture { get; }
		public CollectionLocalRestorePlan Plan { get; }
		public bool IsReadyForRestore { get { return Plan.IsReadyForReview; } }
	}

	/// <summary>Classifies application-level Local Collection restore/recovery results.</summary>
	public enum CollectionLocalRestoreWorkflowStatus
	{
		Completed = 1,
		RecoveryRequired = 2,
		CurrentStateChanged = 3,
		RetainedInputInvalid = 4
	}

	/// <summary>Application-level result for one complete or reconciled Local Collection restore operation.</summary>
	public sealed class CollectionLocalRestoreWorkflowResult
	{
		internal CollectionLocalRestoreWorkflowResult(CollectionLocalRestoreWorkflowStatus status,
			CollectionOperation operation, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionLocalRestoreWorkflowStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Message = message ?? String.Empty;
		}

		public CollectionLocalRestoreWorkflowStatus Status { get; }
		public CollectionOperation Operation { get; }
		public string Message { get; }
		public bool IsSuccessful { get { return Status == CollectionLocalRestoreWorkflowStatus.Completed && Operation.IsSuccessful; } }
	}

	/// <summary>
	/// Product-level C7 Local Collection restore workflow: preview, explicit apply, complete phase composition and startup resume.
	/// </summary>
	/// <remarks>
	/// This service coordinates the already-owned native/member/owner/replay/effect/profile executors. It never introduces a
	/// parallel deployment engine. Every new restore begins from an explicit immutable C7.9 review; restart reconstructs that same
	/// retained review and continues only from verified safe boundaries.
	/// </remarks>
	public sealed class CollectionLocalRestoreApplicationService
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsLocalCaptureStore _localCaptureStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionLocalRestoreMemberExecutor _memberExecutor;
		private readonly CollectionLocalRestoreMemberRehydrator _memberRehydrator;
		private readonly CollectionLocalRestoreMemberResumeCoordinator _memberResumeCoordinator;
		private readonly CollectionLocalRestoreOwnershipExecutor _ownershipExecutor;
		private readonly CollectionLocalRestoreReplayExecutor _replayExecutor;
		private readonly CollectionLocalRestorePluginExecutor _pluginExecutor;
		private readonly CollectionLocalRestoreIniExecutor _iniExecutor;
		private readonly CollectionLocalRestoreGameValueExecutor _gameValueExecutor;
		private readonly CollectionLocalRestoreUserMetadataExecutor _userMetadataExecutor;
		private readonly CollectionLocalRestoreProfileAssociationExecutor _profileAssociationExecutor;
		private readonly CollectionLocalRestoreFinalStateVerifier _finalVerifier;

		/// <summary>Creates the production Local restore application workflow for the active game/storage target.</summary>
		public CollectionLocalRestoreApplicationService(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsLocalCaptureStore localCaptureStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_localCaptureStore = localCaptureStore ?? throw new ArgumentNullException(nameof(localCaptureStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			if (referenceStore == null) throw new ArgumentNullException(nameof(referenceStore));
			if (_services.ModManager == null)
				throw new InvalidOperationException("Local Collection restore requires the active native ModManager.");

			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(_gameStorageService, _services);
			_memberExecutor = new CollectionLocalRestoreMemberExecutor(_services, _gameStorageService, _operationStore,
				_associationStore, _artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_memberRehydrator = new CollectionLocalRestoreMemberRehydrator(_services, _gameStorageService, _operationStore, _associationStore,
				_localCaptureStore, _artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_memberResumeCoordinator = new CollectionLocalRestoreMemberResumeCoordinator(_gameStorageService, _operationStore,
				_memberRehydrator, _memberExecutor, _mutationLeaseManager, _authorityValidator);
			_ownershipExecutor = new CollectionLocalRestoreOwnershipExecutor(_services, _gameStorageService, _operationStore,
				_artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_replayExecutor = new CollectionLocalRestoreReplayExecutor(_services, _gameStorageService, _operationStore,
				_artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_pluginExecutor = new CollectionLocalRestorePluginExecutor(_services, _gameStorageService, _operationStore,
				_artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_iniExecutor = new CollectionLocalRestoreIniExecutor(_services, _gameStorageService, _operationStore,
				_artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_gameValueExecutor = new CollectionLocalRestoreGameValueExecutor(_services, _gameStorageService, _operationStore,
				_artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_userMetadataExecutor = new CollectionLocalRestoreUserMetadataExecutor(_services, _gameStorageService, _operationStore,
				_artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_profileAssociationExecutor = new CollectionLocalRestoreProfileAssociationExecutor(_services, _gameStorageService,
				_operationStore, _associationStore, _artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
			_finalVerifier = new CollectionLocalRestoreFinalStateVerifier(_services, _gameStorageService, _operationStore,
				_associationStore, _artifactStore, referenceStore, _mutationLeaseManager, _authorityValidator);
		}

		/// <summary>Builds one read-only C7.9 restore review from the current authoritative target state.</summary>
		public async Task<CollectionLocalRestorePreview> PreviewAsync(LocalCaptureIdentity captureIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (captureIdentity == null) throw new ArgumentNullException(nameof(captureIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			CollectionSealedCaptureSnapshot sealedCapture = LoadSealedCapture(captureIdentity);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(sealedCapture.Capture.SourceTarget))
				throw new InvalidOperationException("The selected Local Collection was captured from a different canonical game/storage target.");
			if (_operationStore.GetIncompleteOperations(authority.Target).Count != 0)
				throw new InvalidOperationException("An incomplete Collection operation already exists for this target. Reconcile it before reviewing a new Local Collection restore.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				if (_operationStore.GetIncompleteOperations(authority.Target).Count != 0)
					throw new InvalidOperationException("An incomplete Collection operation already exists for this target. Reconcile it before reviewing a new Local Collection restore.");
				cancellationToken.ThrowIfCancellationRequested();
				NativeObservation observation = CaptureState(authority.Target);
				CollectionLocalRestorePlan plan = new CollectionLocalRestorePlanner(_artifactStore).Plan(sealedCapture,
					authority.Target, observation.CollectionState.Fingerprint, observation.NativeState, cancellationToken);
				return new CollectionLocalRestorePreview(sealedCapture, plan);
			}
		}

		/// <summary>Executes one exact explicitly reviewed Local restore and all later C7 phases through terminal verification.</summary>
		public async Task<CollectionLocalRestoreWorkflowResult> ApplyAsync(CollectionLocalRestorePreview preview,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (preview == null) throw new ArgumentNullException(nameof(preview));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!preview.IsReadyForRestore)
				throw new InvalidOperationException("Only a Local restore preview with no blocking C7.9 issues can be applied.");

			CollectionLocalRestoreMemberExecutionResult memberPhase = await _memberExecutor.ExecuteAsync(
				preview.SealedCapture, preview.Plan, paths, cancellationToken).ConfigureAwait(true);
			return await ContinueAfterMemberAsync(preview.SealedCapture, preview.Plan, memberPhase,
				paths, cancellationToken).ConfigureAwait(true);
		}

		/// <summary>Reconciles and resumes one persisted Local restore through all remaining phases and final verification.</summary>
		public async Task<CollectionLocalRestoreWorkflowResult> ResumeAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));

			CollectionOperation persisted = _operationStore.GetOperation(operationIdentity);
			if (CanReenterDownstreamRecovery(persisted))
			{
				CollectionLocalRestoreWorkflowResult downstream = await ReenterDownstreamRecoveryAsync(operationIdentity,
					paths, cancellationToken).ConfigureAwait(true);
				if (downstream != null)
					return downstream;
			}

			CollectionLocalRestoreMemberResumeResult member = await _memberResumeCoordinator.ResumeAsync(operationIdentity,
				paths, cancellationToken).ConfigureAwait(true);
			if (!member.IsMemberPhaseComplete)
				return FromMemberResume(member);
			if (member.Rehydration == null || member.Rehydration.SealedCapture == null || member.Rehydration.ReviewedPlan == null)
				throw new InvalidDataException("The resumed Local restore member boundary did not retain its reviewed capture/plan reconstruction.");
			return await ContinueAfterMemberAsync(member.Rehydration.SealedCapture, member.Rehydration.ReviewedPlan,
				member.MemberPhase, paths, cancellationToken).ConfigureAwait(true);
		}

		private async Task<CollectionLocalRestoreWorkflowResult> ReenterDownstreamRecoveryAsync(
			CollectionOperationIdentity operationIdentity, GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			CollectionSealedCaptureSnapshot sealedCapture;
			CollectionLocalRestorePlan reviewedPlan;
			CollectionLocalRestoreMemberExecutionResult memberPhase;

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionOperation current = _operationStore.GetOperation(operationIdentity);
				if (!CanReenterDownstreamRecovery(current) || !authority.Target.Equals(current.Target))
					return null;

				CollectionLocalRestoreMemberRehydrationResult rehydration = _memberRehydrator.RehydrateReloaded(
					operationIdentity, authority, cancellationToken);
				if (rehydration.Status == CollectionLocalRestoreMemberRehydrationStatus.RetainedInputInvalid)
					return new CollectionLocalRestoreWorkflowResult(CollectionLocalRestoreWorkflowStatus.RetainedInputInvalid,
						rehydration.Operation, rehydration.Message);
				if (rehydration.Status == CollectionLocalRestoreMemberRehydrationStatus.CurrentStateChanged)
					return new CollectionLocalRestoreWorkflowResult(CollectionLocalRestoreWorkflowStatus.CurrentStateChanged,
						rehydration.Operation, rehydration.Message);
				if (!IsExactCompletedMemberBoundary(rehydration))
					return new CollectionLocalRestoreWorkflowResult(CollectionLocalRestoreWorkflowStatus.RecoveryRequired,
						rehydration.Operation, "The Local restore remains recovery-required because the completed native-member boundary could not be proven independently.");

				current = _operationStore.GetOperation(operationIdentity);
				if (!CanReenterDownstreamRecovery(current))
					return null;
				var reopened = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
					current.Revision, current.PlanIdentity, checked(current.CheckpointSequence + 1),
					CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending, current.NativeChildren);
				_operationStore.SaveOperation(reopened);
				CollectionOperation persistedReopened = _operationStore.GetOperation(operationIdentity);
				if (persistedReopened == null || persistedReopened.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
					persistedReopened.ResultState != CollectionOperationResultState.Pending)
					throw new InvalidDataException("The Local restore recovery boundary could not be durably reopened after exact member-state verification.");

				sealedCapture = rehydration.SealedCapture;
				reviewedPlan = rehydration.ReviewedPlan;
				memberPhase = new CollectionLocalRestoreMemberExecutionResult(persistedReopened, rehydration.MemberRemaps);
			}

			return await ContinueAfterMemberAsync(sealedCapture, reviewedPlan, memberPhase, paths, cancellationToken).ConfigureAwait(true);
		}

		private static bool CanReenterDownstreamRecovery(CollectionOperation operation)
		{
			return operation != null && !operation.IsTerminal && operation.Kind == CollectionOperationKind.RestoreLocalCapture &&
				operation.Phase == CollectionOperationPhase.RecoveryRequired &&
				operation.ResultState == CollectionOperationResultState.RecoveryRequired &&
				!operation.HasUnreconciledNativeChild && !operation.HasUnknownNativeDurability;
		}

		private static bool IsExactCompletedMemberBoundary(CollectionLocalRestoreMemberRehydrationResult rehydration)
		{
			if (rehydration == null || rehydration.SealedCapture == null || rehydration.ReviewedPlan == null ||
				rehydration.CurrentPlan == null || rehydration.CurrentPlan.Issues.Count != 0)
				return false;
			if (rehydration.Members.Count != rehydration.ReviewedPlan.Members.Count ||
				rehydration.Members.Any(x => !x.IsComplete) ||
				rehydration.Removals.Any(x => !x.IsComplete || x.RequiresRecovery) ||
				rehydration.CurrentPlan.CurrentNativeKeysToRemove.Count != 0 ||
				rehydration.CurrentPlan.Members.Count != rehydration.ReviewedPlan.Members.Count ||
				rehydration.CurrentPlan.Members.Any(x => x.Action != CollectionLocalRestoreMemberAction.ReuseExistingNative))
				return false;

			var remaps = new Dictionary<CollectionMemberKey, CollectionLocalRestoreMemberRemap>();
			foreach (CollectionLocalRestoreMemberRemap remap in rehydration.MemberRemaps)
			{
				if (remap == null || remap.MemberKey == null || remaps.ContainsKey(remap.MemberKey))
					return false;
				remaps.Add(remap.MemberKey, remap);
			}
			if (remaps.Count != rehydration.ReviewedPlan.Members.Count)
				return false;
			foreach (CollectionLocalRestoreMemberPlan current in rehydration.CurrentPlan.Members)
			{
				CollectionLocalRestoreMemberRemap remap;
				if (!remaps.TryGetValue(current.SnapshotMemberKey, out remap) || String.IsNullOrWhiteSpace(current.CurrentNativeKey) ||
					!StringComparer.OrdinalIgnoreCase.Equals(current.CurrentNativeKey, remap.NativeKey))
					return false;
			}
			return true;
		}

		private async Task<CollectionLocalRestoreWorkflowResult> ContinueAfterMemberAsync(
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, GameStoragePathSet paths,
			CancellationToken cancellationToken)
		{
			CollectionLocalRestoreOwnershipExecutionResult ownership = await _ownershipExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, memberPhase, paths, cancellationToken).ConfigureAwait(true);
			CollectionLocalRestoreReplayExecutionResult replay = await _replayExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, memberPhase, ownership, paths, cancellationToken).ConfigureAwait(true);
			CollectionLocalRestorePluginExecutionResult plugins = await _pluginExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, replay, paths, cancellationToken).ConfigureAwait(true);
			CollectionLocalRestoreIniExecutionResult ini = await _iniExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, memberPhase, plugins, paths, cancellationToken).ConfigureAwait(true);
			CollectionLocalRestoreGameValueExecutionResult gameValues = await _gameValueExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, memberPhase, ini, paths, cancellationToken).ConfigureAwait(true);
			CollectionLocalRestoreUserMetadataExecutionResult metadata = await _userMetadataExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, memberPhase, gameValues, paths, cancellationToken).ConfigureAwait(true);
			CollectionLocalRestoreProfileAssociationExecutionResult profile = await _profileAssociationExecutor.ExecuteAsync(sealedCapture,
				reviewedPlan, metadata, paths, cancellationToken).ConfigureAwait(true);
			CollectionOperation completed = await _finalVerifier.VerifyAndCommitAsync(sealedCapture, reviewedPlan,
				memberPhase, profile, paths, cancellationToken).ConfigureAwait(true);
			return new CollectionLocalRestoreWorkflowResult(CollectionLocalRestoreWorkflowStatus.Completed, completed,
				"The Local Collection restore was fully verified and committed.");
		}

		private static CollectionLocalRestoreWorkflowResult FromMemberResume(CollectionLocalRestoreMemberResumeResult member)
		{
			CollectionLocalRestoreWorkflowStatus status;
			switch (member.Status)
			{
				case CollectionLocalRestoreMemberResumeStatus.CurrentStateChanged:
					status = CollectionLocalRestoreWorkflowStatus.CurrentStateChanged;
					break;
				case CollectionLocalRestoreMemberResumeStatus.RetainedInputInvalid:
					status = CollectionLocalRestoreWorkflowStatus.RetainedInputInvalid;
					break;
				default:
					status = CollectionLocalRestoreWorkflowStatus.RecoveryRequired;
					break;
			}
			return new CollectionLocalRestoreWorkflowResult(status, member.Operation, member.Message);
		}

		private CollectionSealedCaptureSnapshot LoadSealedCapture(LocalCaptureIdentity captureIdentity)
		{
			LocalCapture stored = _localCaptureStore.GetCapture(captureIdentity);
			if (stored == null)
				throw new InvalidDataException("The selected sealed Local Collection capture is no longer persisted.");
			string packageArtifactId = _localCaptureStore.GetPackageArtifactId(captureIdentity);
			if (String.IsNullOrWhiteSpace(packageArtifactId) || !_artifactStore.VerifyArtifact(packageArtifactId))
				throw new InvalidDataException("The sealed Local Collection package is missing or failed retained-artifact verification.");
			byte[] bytes;
			using (Stream source = _artifactStore.OpenRead(packageArtifactId))
			using (var buffer = new MemoryStream())
			{
				source.CopyTo(buffer);
				bytes = buffer.ToArray();
			}
			CollectionSealedCaptureSnapshot snapshot = CollectionLocalCapturePackageCodec.Deserialize(bytes);
			if (!MatchesStoredCapture(stored, snapshot.Capture))
				throw new InvalidDataException("The retained Local Collection package does not match its persisted sealed-capture contract.");
			return snapshot;
		}

		private static bool MatchesStoredCapture(LocalCapture stored, LocalCapture packaged)
		{
			return stored != null && packaged != null && stored.Identity.Equals(packaged.Identity) &&
				stored.Revision.Equals(packaged.Revision) && stored.SourceTarget.Equals(packaged.SourceTarget) &&
				stored.CapturedStateFingerprint.Equals(packaged.CapturedStateFingerprint) &&
				stored.Capability == packaged.Capability && stored.SchemaVersion == packaged.SchemaVersion &&
				stored.CapabilityVersion == packaged.CapabilityVersion && stored.Scope.Equals(packaged.Scope) &&
				new HashSet<RetainedArtifactReference>(stored.RetainedArtifacts).SetEquals(packaged.RetainedArtifacts) &&
				new HashSet<LocalCaptureExclusion>(stored.Exclusions).SetEquals(packaged.Exclusions) &&
				new HashSet<LocalCaptureNativeRecordMapping>(stored.NativeRecordMappings).SetEquals(packaged.NativeRecordMappings);
		}

		private NativeObservation CaptureState(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			NativeStateCaptureSnapshot native = new NativeStateCaptureReader(manager.InstallationLog, manager.VirtualModActivator,
				manager.DeploymentManager, _services.PluginManager, manager.GameMode).Capture();
			CollectionNativeStateIndex collection = new CollectionNativeStateReader(manager.InstallationLog, manager.VirtualModActivator,
				_services.PluginManager, manager.GameMode, _associationStore).Capture(target, native);
			return new NativeObservation(native, collection);
		}

		private sealed class NativeObservation
		{
			internal NativeObservation(NativeStateCaptureSnapshot nativeState, CollectionNativeStateIndex collectionState)
			{
				NativeState = nativeState ?? throw new ArgumentNullException(nameof(nativeState));
				CollectionState = collectionState ?? throw new ArgumentNullException(nameof(collectionState));
			}
			internal NativeStateCaptureSnapshot NativeState { get; }
			internal CollectionNativeStateIndex CollectionState { get; }
		}
	}
}
