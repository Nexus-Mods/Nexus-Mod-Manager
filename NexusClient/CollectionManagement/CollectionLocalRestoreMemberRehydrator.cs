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
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies read-only reconstruction of the C7.10a member phase after process restart.</summary>
	public enum CollectionLocalRestoreMemberRehydrationStatus
	{
		ReadyToResume = 1,
		MemberPhaseComplete = 2,
		NativeRecoveryRequired = 3,
		CurrentStateChanged = 4,
		RetainedInputInvalid = 5
	}

	/// <summary>Classifies one reviewed captured member at the reconstructed C7.10a boundary.</summary>
	public enum CollectionLocalRestoreMemberProgressStatus
	{
		ReuseVerified = 1,
		RecreatedVerified = 2,
		PendingRecreation = 3,
		NativeRecoveryRequired = 4
	}

	/// <summary>Read-only restart reconstruction for one reviewed Local Collection member.</summary>
	public sealed class CollectionLocalRestoreMemberProgress
	{
		internal CollectionLocalRestoreMemberProgress(CollectionLocalRestoreMemberPlan reviewedMember,
			CollectionLocalRestoreMemberProgressStatus status, CollectionNativeChildOperation child, string currentNativeKey)
		{
			ReviewedMember = reviewedMember ?? throw new ArgumentNullException(nameof(reviewedMember));
			if (!Enum.IsDefined(typeof(CollectionLocalRestoreMemberProgressStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Child = child;
			CurrentNativeKey = currentNativeKey ?? String.Empty;
		}

		public CollectionLocalRestoreMemberPlan ReviewedMember { get; }
		public CollectionLocalRestoreMemberProgressStatus Status { get; }
		public CollectionNativeChildOperation Child { get; }
		public string CurrentNativeKey { get; }
		public bool IsComplete { get { return Status == CollectionLocalRestoreMemberProgressStatus.ReuseVerified || Status == CollectionLocalRestoreMemberProgressStatus.RecreatedVerified; } }
	}

	/// <summary>Read-only restart reconstruction for one reviewed current-native removal.</summary>
	public sealed class CollectionLocalRestoreRemovalProgress
	{
		internal CollectionLocalRestoreRemovalProgress(string nativeKey, CollectionNativeChildOperation child, bool complete, bool requiresRecovery)
		{
			if (String.IsNullOrWhiteSpace(nativeKey)) throw new ArgumentException("A reviewed native removal key is required.", nameof(nativeKey));
			NativeKey = nativeKey;
			Child = child;
			IsComplete = complete;
			RequiresRecovery = requiresRecovery;
		}

		public string NativeKey { get; }
		public CollectionNativeChildOperation Child { get; }
		public bool IsComplete { get; }
		public bool RequiresRecovery { get; }
	}

	/// <summary>Immutable read-only reconstruction of persisted C7.10a review, journal progress and current native remaps.</summary>
	public sealed class CollectionLocalRestoreMemberRehydrationResult
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreMemberRemap> _memberRemaps;
		private readonly ReadOnlyCollection<CollectionLocalRestoreMemberProgress> _members;
		private readonly ReadOnlyCollection<CollectionLocalRestoreRemovalProgress> _removals;

		internal CollectionLocalRestoreMemberRehydrationResult(CollectionLocalRestoreMemberRehydrationStatus status,
			CollectionOperation operation, CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestorePlan currentPlan, IEnumerable<CollectionLocalRestoreMemberRemap> memberRemaps,
			IEnumerable<CollectionLocalRestoreMemberProgress> members, IEnumerable<CollectionLocalRestoreRemovalProgress> removals,
			string message)
		{
			if (!Enum.IsDefined(typeof(CollectionLocalRestoreMemberRehydrationStatus), status))
				throw new ArgumentOutOfRangeException(nameof(status));
			Status = status;
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			SealedCapture = sealedCapture;
			ReviewedPlan = reviewedPlan;
			CurrentPlan = currentPlan;
			_memberRemaps = new ReadOnlyCollection<CollectionLocalRestoreMemberRemap>((memberRemaps ?? Enumerable.Empty<CollectionLocalRestoreMemberRemap>()).ToList());
			_members = new ReadOnlyCollection<CollectionLocalRestoreMemberProgress>((members ?? Enumerable.Empty<CollectionLocalRestoreMemberProgress>()).ToList());
			_removals = new ReadOnlyCollection<CollectionLocalRestoreRemovalProgress>((removals ?? Enumerable.Empty<CollectionLocalRestoreRemovalProgress>()).ToList());
			Message = message ?? String.Empty;
		}

		public CollectionLocalRestoreMemberRehydrationStatus Status { get; }
		public CollectionOperation Operation { get; }
		public CollectionSealedCaptureSnapshot SealedCapture { get; }
		public CollectionLocalRestorePlan ReviewedPlan { get; }
		public CollectionLocalRestorePlan CurrentPlan { get; }
		public ReadOnlyCollection<CollectionLocalRestoreMemberRemap> MemberRemaps { get { return _memberRemaps; } }
		public ReadOnlyCollection<CollectionLocalRestoreMemberProgress> Members { get { return _members; } }
		public ReadOnlyCollection<CollectionLocalRestoreRemovalProgress> Removals { get { return _removals; } }
		public string Message { get; }
		public bool CanResumeMemberWork { get { return Status == CollectionLocalRestoreMemberRehydrationStatus.ReadyToResume; } }
		public bool IsMemberPhaseComplete { get { return Status == CollectionLocalRestoreMemberRehydrationStatus.MemberPhaseComplete; } }
	}

	/// <summary>
	/// Rehydrates the exact reviewed C7.10a intent and reconstructs durable member progress/remaps without submitting native work.
	/// </summary>
	/// <remarks>
	/// This reader never clears or restarts an incomplete operation. Submitted/unreconciled native children remain explicitly
	/// recovery-required so C7.10 resume composition can reconcile native durability before choosing any continuation action.
	/// </remarks>
	public sealed class CollectionLocalRestoreMemberRehydrator
	{
		private const string RestoreIntentRole = "local-restore-plan-v1";
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsLocalCaptureStore _localCaptureStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates the production read-only C7.10a restart rehydrator.</summary>
		public CollectionLocalRestoreMemberRehydrator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsLocalCaptureStore localCaptureStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, associationStore, localCaptureStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreMemberRehydrator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsLocalCaptureStore localCaptureStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_localCaptureStore = localCaptureStore ?? throw new ArgumentNullException(nameof(localCaptureStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>Rehydrates one persisted Local restore operation under a stable current-target observation boundary.</summary>
		public Task<CollectionLocalRestoreMemberRehydrationResult> RehydrateAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths)
		{
			return RehydrateAsync(operationIdentity, paths, CancellationToken.None);
		}

		/// <summary>Rehydrates one persisted Local restore operation without executing or retrying any native child.</summary>
		public async Task<CollectionLocalRestoreMemberRehydrationResult> RehydrateAsync(CollectionOperationIdentity operationIdentity,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateRequest(operationIdentity, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				return RehydrateReloaded(operationIdentity, authority, cancellationToken);
			}
		}

		/// <summary>Rehydrates against an authority already reloaded while the caller still holds its target mutation lease.</summary>
		internal CollectionLocalRestoreMemberRehydrationResult RehydrateReloaded(CollectionOperationIdentity operationIdentity,
			CollectionTargetAuthority authority, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (authority == null) throw new ArgumentNullException(nameof(authority));
			if (_services.ModManager == null)
				throw new InvalidOperationException("C7.10a restart rehydration requires the live native ModManager.");

			CollectionOperation operation = _operationStore.GetOperation(operationIdentity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal)
				throw new InvalidOperationException("The requested Local Collection restore operation is unavailable or no longer resumable.");

			CollectionSealedCaptureSnapshot sealedCapture;
			CollectionLocalRestorePlan reviewedPlan;
			try
			{
				byte[] intentBytes = LoadIntentBytes(operation, cancellationToken);
				LocalCaptureIdentity captureIdentity = CollectionLocalRestoreIntentCodec.ReadCaptureIdentity(intentBytes);
				sealedCapture = LoadSealedCapture(captureIdentity, cancellationToken);
				reviewedPlan = CollectionLocalRestoreIntentCodec.Deserialize(intentBytes, sealedCapture);
				ValidateDurableBindings(operation, sealedCapture, reviewedPlan);
			}
			catch (Exception exception) when (exception is InvalidDataException || exception is InvalidOperationException || exception is FileNotFoundException || exception is ArgumentException)
			{
				return Result(CollectionLocalRestoreMemberRehydrationStatus.RetainedInputInvalid, operation, null, null, null,
					null, null, null, exception.Message);
			}

			if (!authority.Target.Equals(operation.Target))
				return Result(CollectionLocalRestoreMemberRehydrationStatus.CurrentStateChanged, operation, sealedCapture, reviewedPlan,
					null, null, null, null, "The current canonical target no longer matches the persisted Local restore operation.");

			cancellationToken.ThrowIfCancellationRequested();
			NativeObservation observation = CaptureState(operation.Target);
			var planner = new CollectionLocalRestorePlanner(_artifactStore);
			// Only an unreconciled native child may use the smaller observation. Once it is reconciled, the next pass
			// verifies the complete capture before returning a state from which any new restore work can be submitted.
			CollectionLocalRestorePlan currentPlan = operation.HasUnreconciledNativeChild
				? planner.PlanForNativeReconciliation(sealedCapture, operation.Target, observation.CollectionState.Fingerprint,
					observation.NativeState, cancellationToken)
				: planner.Plan(sealedCapture, operation.Target, observation.CollectionState.Fingerprint, observation.NativeState, cancellationToken);
			if (currentPlan.Issues.Count != 0)
			{
				if (operation.RequiresRecovery || operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability)
					return Result(CollectionLocalRestoreMemberRehydrationStatus.NativeRecoveryRequired, operation, sealedCapture, reviewedPlan,
						currentPlan, null, null, null, "Native recovery must reconcile the interrupted restore boundary before current-state differences can be classified safely.");
				bool retainedInvalid = currentPlan.Issues.Any(IsRetainedInputIssue);
				return Result(retainedInvalid ? CollectionLocalRestoreMemberRehydrationStatus.RetainedInputInvalid : CollectionLocalRestoreMemberRehydrationStatus.CurrentStateChanged,
					operation, sealedCapture, reviewedPlan, currentPlan, null, null, null,
					String.Join(" ", currentPlan.Issues.Select(x => x.Message)));
			}
			return ReconstructProgress(operation, sealedCapture, reviewedPlan, currentPlan, observation.NativeState, cancellationToken);
		}

		private void ValidateRequest(CollectionOperationIdentity operationIdentity, GameStoragePathSet paths)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (_services.ModManager == null)
				throw new InvalidOperationException("C7.10a restart rehydration requires the live native ModManager.");
		}

		private CollectionLocalRestoreMemberRehydrationResult ReconstructProgress(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestorePlan currentPlan, NativeStateCaptureSnapshot nativeState, CancellationToken cancellationToken)
		{
			List<ExpectedChild> expected = BuildExpectedChildren(operation, sealedCapture.Capture.Identity, reviewedPlan);
			List<CollectionNativeChildOperation> actual = operation.NativeChildren.OrderBy(x => x.Sequence).ToList();
			List<CollectionNativeChildOperation> currentAttempts;
			string journalError;
			if (!TryMapAttemptHistory(expected, actual, reviewedPlan.Target, out currentAttempts, out journalError))
				return InvalidJournal(operation, sealedCapture, reviewedPlan, currentPlan, journalError);

			var activeKeys = new HashSet<string>(nativeState.InstallLog.Mods.Where(x => !x.Hidden).Select(x => x.ModKey), StringComparer.OrdinalIgnoreCase);
			var currentMembers = currentPlan.Members.ToDictionary(x => x.SnapshotMemberKey);
			var capturedIdentities = sealedCapture.InstalledIdentities.Mods
				.GroupBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase)
				.Where(x => x.Count() == 1)
				.ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
			var remaps = new List<CollectionLocalRestoreMemberRemap>();
			var usedRemapKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var committedRecreatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var memberProgress = new List<CollectionLocalRestoreMemberProgress>();
			var removalProgress = new List<CollectionLocalRestoreRemovalProgress>();
			bool nativeRecoveryRequired = operation.RequiresRecovery || operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability;
			int expectedIndex = 0;

			foreach (string nativeKey in reviewedPlan.CurrentNativeKeysToRemove)
			{
				CollectionNativeChildOperation child = currentAttempts[expectedIndex++];
				bool removalComplete = IsVerifiedCommitted(child);
				bool rolledBack = IsSafeRolledBack(child);
				bool requiresRecovery = child != null && child.HasCrossedNativeBoundary && !child.IsReconciled;
				if (removalComplete && activeKeys.Contains(nativeKey))
					return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
						"A removal journaled as committed is present again in current native state.");
				if (!removalComplete && !requiresRecovery && !activeKeys.Contains(nativeKey))
					return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
						"A reviewed native removal disappeared without a reconciled committed child.");
				if (child != null && child.IsReconciled && !removalComplete && !rolledBack)
					return InvalidJournal(operation, sealedCapture, reviewedPlan, currentPlan, "A reconciled Local restore removal has neither a verified committed nor verified rolled-back result.");
				removalProgress.Add(new CollectionLocalRestoreRemovalProgress(nativeKey, rolledBack ? null : child, removalComplete, requiresRecovery));
				nativeRecoveryRequired |= requiresRecovery;
			}

			foreach (CollectionLocalRestoreMemberPlan reviewedMember in reviewedPlan.Members)
			{
				CollectionLocalRestoreMemberPlan currentMember;
				if (!currentMembers.TryGetValue(reviewedMember.SnapshotMemberKey, out currentMember) ||
					!StringComparer.OrdinalIgnoreCase.Equals(reviewedMember.CapturedNativeKey, currentMember.CapturedNativeKey) ||
					reviewedMember.InstallContext.Method != currentMember.InstallContext.Method ||
					reviewedMember.InstallContext.InstallRoot != currentMember.InstallContext.InstallRoot)
					return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
						"The current native-state projection no longer contains the reviewed captured member closure.");

				if (reviewedMember.Action == CollectionLocalRestoreMemberAction.ReuseExistingNative)
				{
					if (currentMember.Action != CollectionLocalRestoreMemberAction.ReuseExistingNative ||
						!StringComparer.OrdinalIgnoreCase.Equals(currentMember.CurrentNativeKey, reviewedMember.CurrentNativeKey) ||
						!usedRemapKeys.Add(currentMember.CurrentNativeKey))
						return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
							"A reviewed reused native member no longer matches the exact native registration approved before restore.");
					remaps.Add(new CollectionLocalRestoreMemberRemap(reviewedMember.SnapshotMemberKey, currentMember.CurrentNativeKey));
					memberProgress.Add(new CollectionLocalRestoreMemberProgress(reviewedMember,
						CollectionLocalRestoreMemberProgressStatus.ReuseVerified, null, currentMember.CurrentNativeKey));
					continue;
				}

				if (reviewedMember.Action != CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive)
					return InvalidJournal(operation, sealedCapture, reviewedPlan, currentPlan, "The retained reviewed intent contains a member action that C7.10a cannot resume automatically.");

				CollectionNativeChildOperation child = currentAttempts[expectedIndex++];
				if (child != null && child.HasCrossedNativeBoundary && !child.IsReconciled)
				{
					nativeRecoveryRequired = true;
					memberProgress.Add(new CollectionLocalRestoreMemberProgress(reviewedMember,
						CollectionLocalRestoreMemberProgressStatus.NativeRecoveryRequired, child, String.Empty));
					continue;
				}
				bool rolledBack = IsSafeRolledBack(child);
				if (child != null && child.IsReconciled && !IsVerifiedCommitted(child) && !rolledBack)
					return InvalidJournal(operation, sealedCapture, reviewedPlan, currentPlan, "A reconciled recreated Local restore member has neither a verified committed nor verified rolled-back result.");

				if (IsVerifiedCommitted(child))
				{
					string committedNativeKey = currentMember.Action == CollectionLocalRestoreMemberAction.ReuseExistingNative
						? currentMember.CurrentNativeKey : null;
					if (String.IsNullOrWhiteSpace(committedNativeKey))
					{
						CollectionInstalledModIdentity capturedIdentity;
						if (capturedIdentities.TryGetValue(reviewedMember.CapturedNativeKey, out capturedIdentity))
							committedNativeKey = ResolveVerifiedCommittedRecreatedNativeKey(capturedIdentity, reviewedMember.RetainedArchive,
								nativeState.InstallLog.Mods, usedRemapKeys, cancellationToken);
					}
					if (String.IsNullOrWhiteSpace(committedNativeKey) || !usedRemapKeys.Add(committedNativeKey))
						return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
							"A recreated member journaled as committed is not present under a uniquely verified current native registration.");

					// A fresh restore plan may conservatively request recreation when downstream owner restoration has already
					// changed that member's file effects. The committed child still proves the native registration itself;
					// ownership/final verification remains responsible for proving the exact captured effect set.
					committedRecreatedKeys.Add(committedNativeKey);
					remaps.Add(new CollectionLocalRestoreMemberRemap(reviewedMember.SnapshotMemberKey, committedNativeKey));
					memberProgress.Add(new CollectionLocalRestoreMemberProgress(reviewedMember,
						CollectionLocalRestoreMemberProgressStatus.RecreatedVerified, child, committedNativeKey));
				}
				else
				{
					if (currentMember.Action != CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive)
						return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
							"A member that has not crossed a reconciled native restore boundary already appears satisfied by different current state.");
					memberProgress.Add(new CollectionLocalRestoreMemberProgress(reviewedMember,
						CollectionLocalRestoreMemberProgressStatus.PendingRecreation, rolledBack ? null : child, String.Empty));
				}
			}

			if (expectedIndex != expected.Count)
				return InvalidJournal(operation, sealedCapture, reviewedPlan, currentPlan, "The reviewed Local restore intent could not be assigned to deterministic member progress.");

			if (nativeRecoveryRequired)
				return Result(CollectionLocalRestoreMemberRehydrationStatus.NativeRecoveryRequired, operation, sealedCapture, reviewedPlan,
					currentPlan, remaps, memberProgress, removalProgress,
					"At least one Local restore child crossed the native boundary without completed restart reconciliation.");

			HashSet<string> pendingRemovals = new HashSet<string>(removalProgress.Where(x => !x.IsComplete).Select(x => x.NativeKey), StringComparer.OrdinalIgnoreCase);
			var currentRemovalKeys = new HashSet<string>(currentPlan.CurrentNativeKeysToRemove, StringComparer.OrdinalIgnoreCase);
			currentRemovalKeys.ExceptWith(committedRecreatedKeys);
			if (!currentRemovalKeys.SetEquals(pendingRemovals))
				return CurrentStateChanged(operation, sealedCapture, reviewedPlan, currentPlan, remaps, memberProgress, removalProgress,
					"Current native registrations differ from the exact remaining removals implied by the durable Local restore journal.");

			bool complete = removalProgress.All(x => x.IsComplete) && memberProgress.All(x => x.IsComplete);
			return Result(complete ? CollectionLocalRestoreMemberRehydrationStatus.MemberPhaseComplete : CollectionLocalRestoreMemberRehydrationStatus.ReadyToResume,
				operation, sealedCapture, reviewedPlan, currentPlan, remaps.OrderBy(x => x.MemberKey.ToString(), StringComparer.Ordinal),
				memberProgress, removalProgress, complete
					? "The exact reviewed C7.10a member set and native-key remaps were reconstructed from durable state."
					: "The exact reviewed C7.10a intent was reconstructed and remaining member work is still before a safe native submission boundary.");
		}

		private byte[] LoadIntentBytes(CollectionOperation operation, CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.OperationId.ToString("D"), RestoreIntentRole);
			if (reference == null || !_artifactStore.VerifyArtifact(reference.ArtifactId, cancellationToken))
				throw new InvalidDataException("The durable reviewed Local restore intent is missing or failed retained-artifact verification.");
			using (Stream source = _artifactStore.OpenRead(reference.ArtifactId))
			using (var buffer = new MemoryStream())
			{
				source.CopyTo(buffer);
				return buffer.ToArray();
			}
		}

		private CollectionSealedCaptureSnapshot LoadSealedCapture(LocalCaptureIdentity captureIdentity, CancellationToken cancellationToken)
		{
			LocalCapture stored = _localCaptureStore.GetCapture(captureIdentity);
			if (stored == null)
				throw new InvalidDataException("The Local restore intent references a sealed capture that is no longer persisted.");
			string packageArtifactId = _localCaptureStore.GetPackageArtifactId(captureIdentity);
			if (String.IsNullOrWhiteSpace(packageArtifactId) || !_artifactStore.VerifyArtifact(packageArtifactId, cancellationToken))
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

		private static void ValidateDurableBindings(CollectionOperation operation, CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan)
		{
			if (operation.Revision == null || !operation.Revision.Equals(sealedCapture.Capture.Revision) ||
				!operation.Collection.Equals(sealedCapture.Capture.Revision.Collection) ||
				!operation.Target.Equals(sealedCapture.Capture.SourceTarget) || !reviewedPlan.Target.Equals(operation.Target) ||
				!reviewedPlan.CaptureIdentity.Equals(sealedCapture.Capture.Identity))
				throw new InvalidDataException("The retained Local restore intent/capture does not match its durable operation identity, revision or target.");
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

		/// <summary>
		/// Resolves a native registration whose recreation was already journaled as verified committed. Unlike normal
		/// planning, this deliberately ignores extra current file effects because downstream owner restoration may already
		/// have changed them. Recovery uses only facts the legacy InstallLog actually persists across restart: exact retained
		/// archive bytes, install context, and one unique active registration. Nexus mod/file identifiers are not durable here.
		/// </summary>
		internal static string ResolveVerifiedCommittedRecreatedNativeKey(CollectionInstalledModIdentity capturedMod,
			CollectionCapturedArchiveArtifact retainedArchive, IEnumerable<InstallLogReadMod> currentMods,
			ISet<string> unavailableKeys, CancellationToken cancellationToken)
		{
			if (capturedMod == null || retainedArchive == null || currentMods == null)
				return null;

			var candidates = new List<InstallLogReadMod>();
			foreach (InstallLogReadMod current in currentMods)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (current == null || current.Hidden || (unavailableKeys != null && unavailableKeys.Contains(current.ModKey)) ||
					capturedMod.InstallContext.Method != current.InstallMethod || capturedMod.InstallContext.InstallRoot != current.InstallRoot ||
					!CollectionArchiveContentMatcher.MatchesFile(current.ArchivePath, retainedArchive.RetainedArtifact.ByteLength,
						retainedArchive.RetainedArtifact.ContentHash, cancellationToken))
					continue;
				candidates.Add(current);
			}

			InstallLogReadMod exact = candidates.FirstOrDefault(x =>
				StringComparer.OrdinalIgnoreCase.Equals(x.ModKey, capturedMod.NativeSnapshotKey));
			if (exact != null)
				return exact.ModKey;
			return candidates.Count == 1 ? candidates[0].ModKey : null;
		}

		private static List<ExpectedChild> BuildExpectedChildren(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan reviewedPlan)
		{
			var result = new List<ExpectedChild>();
			foreach (string nativeKey in reviewedPlan.CurrentNativeKeysToRemove)
				result.Add(ExpectedChild.Removal(nativeKey, CreateRemovalJournalKey(operation, nativeKey), operation.Revision));
			foreach (CollectionLocalRestoreMemberPlan member in reviewedPlan.Members)
			{
				if (member.Action == CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive)
					result.Add(ExpectedChild.Recreation(member, CollectionLocalRestoreMemberExecutor.CreateRecipeFingerprint(captureIdentity, member), operation.Revision));
			}
			return result;
		}

		/// <summary>Maps append-only retry history to the current attempt for each reviewed native action.</summary>
		private static bool TryMapAttemptHistory(IList<ExpectedChild> expected, IList<CollectionNativeChildOperation> actual,
			CollectionTargetIdentity target, out List<CollectionNativeChildOperation> currentAttempts, out string error)
		{
			currentAttempts = Enumerable.Repeat<CollectionNativeChildOperation>(null, expected.Count).ToList();
			error = null;
			int actualIndex = 0;
			for (int expectedIndex = 0; expectedIndex < expected.Count && actualIndex < actual.Count; expectedIndex++)
			{
				ExpectedChild expectedChild = expected[expectedIndex];
				if (!MatchesExpected(actual[actualIndex], expectedChild, target))
				{
					error = "The Local restore native-child journal is not an exact ordered prefix of the reviewed intent.";
					return false;
				}

				CollectionNativeChildOperation latest = null;
				int attempt = 0;
				while (actualIndex < actual.Count && MatchesExpected(actual[actualIndex], expectedChild, target))
				{
					CollectionNativeChildOperation child = actual[actualIndex];
					if (child.Sequence != actualIndex + 1)
					{
						error = "The Local restore native-child journal sequence is not contiguous.";
						return false;
					}

					if (attempt == 0)
					{
						if (child.NativeOperation.Origin != ModOperationOrigin.LocalRestore)
						{
							error = "The first native attempt for a reviewed Local restore action is not a LocalRestore attempt.";
							return false;
						}
					}
					else
					{
						if (!IsSafeRolledBack(latest) || child.NativeOperation.Origin != ModOperationOrigin.Recovery ||
							!child.NativeOperation.Fingerprint.Equals(latest.NativeOperation.Fingerprint))
						{
							error = "A Local restore retry exists without an immediately preceding reconciled verified rollback of the same native intent.";
							return false;
						}
					}

					latest = child;
					attempt++;
					actualIndex++;
					if (!IsSafeRolledBack(latest))
						break;
				}

				currentAttempts[expectedIndex] = latest;
				if (actualIndex < actual.Count && !IsVerifiedCommitted(latest))
				{
					error = "The Local restore journal advances to another reviewed action before the preceding action is verified committed.";
					return false;
				}
			}

			if (actualIndex != actual.Count)
			{
				error = "The Local restore journal contains a native child that cannot be assigned to reviewed member progress.";
				return false;
			}
			return true;
		}

		private static bool MatchesExpected(CollectionNativeChildOperation child, ExpectedChild expected, CollectionTargetIdentity target)
		{
			if (child == null || expected == null || !child.Member.Revision.Equals(expected.Revision) ||
				!child.Member.MemberKey.Equals(expected.MemberKey) || child.Action != expected.Action ||
				!StringComparer.Ordinal.Equals(child.NativeOperation.Fingerprint.TargetFingerprint, target.Fingerprint))
				return false;
			if (expected.IsRemoval)
				return child.Action == CollectionNativeChildAction.Deactivate && String.IsNullOrWhiteSpace(child.NativeOperation.Fingerprint.RecipeFingerprint);
			return child.NativeOperation.Fingerprint.InstallMethod == expected.Member.InstallContext.Method &&
				child.NativeOperation.Fingerprint.InstallRoot == expected.Member.InstallContext.InstallRoot &&
				StringComparer.Ordinal.Equals(child.NativeOperation.Fingerprint.RecipeFingerprint, expected.RecipeFingerprint);
		}

		private static CollectionMemberKey CreateRemovalJournalKey(CollectionOperation operation, string nativeKey)
		{
			return CollectionLocalRestoreMemberExecutor.CreateRemovalJournalKey(operation, nativeKey);
		}

		private static bool IsVerifiedCommitted(CollectionNativeChildOperation child)
		{
			return child != null && child.IsReconciled && child.NativeResult != null &&
				child.NativeResult.Durability == ModOperationDurability.VerifiedCommitted;
		}

		private static bool IsSafeRolledBack(CollectionNativeChildOperation child)
		{
			return child != null && child.IsReconciled && child.NativeResult != null &&
				(child.NativeResult.Durability == ModOperationDurability.VerifiedRolledBack ||
				 child.NativeResult.Durability == ModOperationDurability.NotStarted);
		}

		private static bool IsRetainedInputIssue(CollectionLocalRestorePlanIssue issue)
		{
			switch (issue.Kind)
			{
				case CollectionLocalRestorePlanIssueKind.CurrentNativeStateAmbiguous:
				case CollectionLocalRestorePlanIssueKind.CurrentNativeMatchAmbiguous:
				case CollectionLocalRestorePlanIssueKind.CurrentOriginalOwnerUnavailable:
					return false;
				default:
					return true;
			}
		}

		private static CollectionLocalRestoreMemberRehydrationResult InvalidJournal(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestorePlan currentPlan,
			string message)
		{
			return Result(CollectionLocalRestoreMemberRehydrationStatus.RetainedInputInvalid, operation, sealedCapture, reviewedPlan,
				currentPlan, null, null, null, message);
		}

		private static CollectionLocalRestoreMemberRehydrationResult CurrentStateChanged(CollectionOperation operation,
			CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestorePlan currentPlan,
			IEnumerable<CollectionLocalRestoreMemberRemap> remaps, IEnumerable<CollectionLocalRestoreMemberProgress> members,
			IEnumerable<CollectionLocalRestoreRemovalProgress> removals, string message)
		{
			return Result(CollectionLocalRestoreMemberRehydrationStatus.CurrentStateChanged, operation, sealedCapture, reviewedPlan,
				currentPlan, remaps, members, removals, message);
		}

		private static CollectionLocalRestoreMemberRehydrationResult Result(CollectionLocalRestoreMemberRehydrationStatus status,
			CollectionOperation operation, CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestorePlan currentPlan, IEnumerable<CollectionLocalRestoreMemberRemap> remaps,
			IEnumerable<CollectionLocalRestoreMemberProgress> members, IEnumerable<CollectionLocalRestoreRemovalProgress> removals,
			string message)
		{
			return new CollectionLocalRestoreMemberRehydrationResult(status, operation, sealedCapture, reviewedPlan, currentPlan,
				remaps, members, removals, message);
		}

		private sealed class ExpectedChild
		{
			private ExpectedChild(bool isRemoval, string nativeKey, CollectionMemberKey memberKey,
				CollectionRevisionIdentity revision, CollectionNativeChildAction action,
				CollectionLocalRestoreMemberPlan member, string recipeFingerprint)
			{
				IsRemoval = isRemoval;
				NativeKey = nativeKey;
				MemberKey = memberKey;
				Revision = revision;
				Action = action;
				Member = member;
				RecipeFingerprint = recipeFingerprint;
			}

			internal bool IsRemoval { get; }
			internal string NativeKey { get; }
			internal CollectionMemberKey MemberKey { get; }
			internal CollectionRevisionIdentity Revision { get; }
			internal CollectionNativeChildAction Action { get; }
			internal CollectionLocalRestoreMemberPlan Member { get; }
			internal string RecipeFingerprint { get; }

			internal static ExpectedChild Removal(string nativeKey, CollectionMemberKey memberKey, CollectionRevisionIdentity revision)
			{
				return new ExpectedChild(true, nativeKey, memberKey, revision, CollectionNativeChildAction.Deactivate, null, null);
			}

			internal static ExpectedChild Recreation(CollectionLocalRestoreMemberPlan member, string recipeFingerprint, CollectionRevisionIdentity revision)
			{
				return new ExpectedChild(false, null, member.SnapshotMemberKey, revision, CollectionNativeChildAction.ActivateOrReinstall,
					member, recipeFingerprint);
			}
		}

		private sealed class NativeObservation
		{
			internal NativeObservation(NativeStateCaptureSnapshot nativeState, CollectionNativeStateIndex collectionState)
			{
				NativeState = nativeState;
				CollectionState = collectionState;
			}
			internal NativeStateCaptureSnapshot NativeState { get; }
			internal CollectionNativeStateIndex CollectionState { get; }
		}
	}
}
