using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how one C7.10b1 target reached its exact verified owner topology.</summary>
	public enum CollectionLocalRestoreOwnershipOutcome
	{
		AlreadySatisfied = 1,
		RestoredAndVerified = 2,
		RecoveredCommitted = 3
	}

	/// <summary>One verified C7.10b1 owner/payload restoration result.</summary>
	public sealed class CollectionLocalRestoreOwnershipTargetResult
	{
		internal CollectionLocalRestoreOwnershipTargetResult(ModDeploymentTarget target,
			CollectionLocalRestoreOwnershipOutcome outcome)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			Outcome = outcome;
		}

		public ModDeploymentTarget Target { get; }
		public CollectionLocalRestoreOwnershipOutcome Outcome { get; }
	}

	/// <summary>Result of C7.10b1; replay/plugin/config/game effects remain pending for C7.10b2.</summary>
	public sealed class CollectionLocalRestoreOwnershipExecutionResult
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreOwnershipTargetResult> _targets;

		internal CollectionLocalRestoreOwnershipExecutionResult(CollectionOperation operation,
			IEnumerable<CollectionLocalRestoreOwnershipTargetResult> targets)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			_targets = new ReadOnlyCollection<CollectionLocalRestoreOwnershipTargetResult>(
				(targets ?? throw new ArgumentNullException(nameof(targets))).ToList());
		}

		public CollectionOperation Operation { get; }
		public ReadOnlyCollection<CollectionLocalRestoreOwnershipTargetResult> Targets { get { return _targets; } }
		public bool IsReadyForStatefulEffectReconciliation
		{
			get { return Operation.Phase == CollectionOperationPhase.PausedAtSafeBoundary && !Operation.HasUnreconciledNativeChild; }
		}
	}

	/// <summary>
	/// C7.10b1 exact owner-topology/payload restore executor over the native method-neutral deployment boundary.
	/// </summary>
	/// <remarks>
	/// Each target persists an immutable desired/preimage intent before mutation. Native restoration is transactional per target;
	/// restart accepts only the exact preimage or exact verified postimage. Replay, plugin, INI, game-specific and user metadata
	/// restoration is intentionally outside this patch and remains C7.10b2.
	/// </remarks>
	public sealed class CollectionLocalRestoreOwnershipExecutor
	{
		private const string IntentFormat = "nmm-ce.collections.local-restore-owner-intent/2";
		private const string IntentRolePrefix = "local-restore-owner-intent-";
		private const string VerifiedRolePrefix = "local-restore-owner-verified-";
		private const string CompleteRole = "local-restore-owner-phase-complete-v1";
		private const int CopyBufferSize = 81920;

		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		/// <summary>Creates the production C7.10b1 exact owner/payload restore executor.</summary>
		public CollectionLocalRestoreOwnershipExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreOwnershipExecutor(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			if (_services.ModManager == null || _services.ModManager.DeploymentManager == null)
				throw new InvalidOperationException("C7.10b1 requires the live native ModManager and deployment manager.");
		}

		/// <summary>Gets the deployment manager from the current post-reload native service graph.</summary>
		private IModDeploymentManager CurrentDeploymentManager
		{
			get
			{
				if (_services.ModManager == null || _services.ModManager.DeploymentManager == null)
					throw new InvalidOperationException("The current native deployment manager is unavailable after authority reload.");
				return _services.ModManager.DeploymentManager;
			}
		}

		/// <summary>Restores and verifies all captured managed file-owner targets after the C7.10a member phase.</summary>
		public Task<CollectionLocalRestoreOwnershipExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			GameStoragePathSet paths)
		{
			return ExecuteAsync(sealedCapture, reviewedPlan, memberPhase, paths, CancellationToken.None);
		}

		/// <summary>Restores C7.10b1 with cooperative cancellation between target-level atomic mutations.</summary>
		public async Task<CollectionLocalRestoreOwnershipExecutionResult> ExecuteAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, memberPhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				CollectionOperation operation = RequireOperation(memberPhase.Operation.Identity, reviewedPlan);
				Dictionary<CollectionMemberKey, string> remaps = BuildRemaps(reviewedPlan, memberPhase);
				var results = new List<CollectionLocalRestoreOwnershipTargetResult>();

				foreach (CollectionLocalRestoreDeploymentPlan deploymentPlan in reviewedPlan.DeploymentTargets)
				{
					cancellationToken.ThrowIfCancellationRequested();
					CollectionOwnerPayloadTarget captured = RequireCapturedTarget(sealedCapture, deploymentPlan);
					List<DesiredOwner> desired = BuildDesiredOwners(captured, deploymentPlan, remaps);
					string targetToken = CreateTargetToken(deploymentPlan.Target);
					string ownerId = operation.Identity.OperationId.ToString("D");
					string intentRole = IntentRolePrefix + targetToken;
					string verifiedRole = VerifiedRolePrefix + targetToken;

					try
					{
						if (TryRecoverVerified(ownerId, verifiedRole, captured, desired))
						{
							results.Add(new CollectionLocalRestoreOwnershipTargetResult(deploymentPlan.Target,
								CollectionLocalRestoreOwnershipOutcome.AlreadySatisfied));
							continue;
						}
					}
					catch
					{
						MarkRecoveryRequired(operation);
						throw;
					}

					CollectionsRetainedArtifactReferenceRecord existingIntent = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, intentRole);
					OwnerIntent intent;
					if (existingIntent == null)
					{
						intent = CaptureIntent(sealedCapture.Capture.Identity, reviewedPlan, captured, desired);
						PersistIntent(ownerId, intentRole, intent);
					}
					else
					{
						try
						{
							intent = ReadIntent(existingIntent.ArtifactId);
							ValidateIntent(intent, sealedCapture.Capture.Identity, reviewedPlan, captured, desired);
						}
						catch
						{
							MarkRecoveryRequired(operation);
							throw;
						}
						if (VerifyLiveTarget(captured, desired, cancellationToken))
						{
							MarkVerified(ownerId, verifiedRole, existingIntent.ArtifactId);
							results.Add(new CollectionLocalRestoreOwnershipTargetResult(deploymentPlan.Target,
								CollectionLocalRestoreOwnershipOutcome.RecoveredCommitted));
							continue;
						}
						if (!MatchesPreimage(intent))
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("A C7.10b1 owner target no longer matches either its durable preimage or reviewed restore postimage.");
						}
					}

					string tempDirectory = CreateTemporaryDirectory();
					try
					{
						List<ModDeploymentRestoreOwner> nativeOwners = MaterializeNativeOwners(desired, tempDirectory, cancellationToken);
						cancellationToken.ThrowIfCancellationRequested();
						try
						{
							ModDeploymentRestoreFallback nativeFallback = MaterializeNativeFallback(captured, tempDirectory, cancellationToken);
							CurrentDeploymentManager.RestoreCapturedOwnerStack(captured.Target, captured.Promoted, nativeOwners, nativeFallback);
						}
						catch
						{
							MarkRecoveryRequired(operation);
							throw;
						}
					}
					finally
					{
						TryDeleteDirectory(tempDirectory);
					}

					if (!VerifyLiveTarget(captured, desired, cancellationToken))
					{
						MarkRecoveryRequired(operation);
						throw new InvalidOperationException("Native deployment restoration returned without establishing the exact retained owner topology and payload bytes.");
					}
					CollectionsRetainedArtifactReferenceRecord persistedIntent = _referenceStore.GetReferenceForOwnerRole(
						CollectionsRetainedArtifactOwnerKind.Operation, ownerId, intentRole);
					MarkVerified(ownerId, verifiedRole, persistedIntent.ArtifactId);
					results.Add(new CollectionLocalRestoreOwnershipTargetResult(deploymentPlan.Target,
						CollectionLocalRestoreOwnershipOutcome.RestoredAndVerified));
				}

				operation = CompleteOwnershipPhase(operation, sealedCapture.Capture.Identity, reviewedPlan, results.Count);
				return new CollectionLocalRestoreOwnershipExecutionResult(operation, results);
			}
		}

		private void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("C7.10b1 requires one reviewed locally-restorable C7.9 plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) || !sealedCapture.OwnerPayloads.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("C7.10b1 inputs do not belong to the same Local Collection capture and target.");
			if (!memberPhase.IsReadyForEffectReconciliation)
				throw new InvalidOperationException("C7.10b1 requires C7.10a to have reached its reconciled safe boundary.");
			if (memberPhase.Operation.Kind != CollectionOperationKind.RestoreLocalCapture || !memberPhase.Operation.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The C7.10a result does not belong to the reviewed Local Collection restore.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan plan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				!operation.Target.Equals(plan.Target) || operation.HasUnreconciledNativeChild)
				throw new InvalidOperationException("C7.10b1 requires the exact active C7.10a operation at a reconciled safe boundary.");
			return operation;
		}

		private static Dictionary<CollectionMemberKey, string> BuildRemaps(CollectionLocalRestorePlan plan,
			CollectionLocalRestoreMemberExecutionResult memberPhase)
		{
			Dictionary<CollectionMemberKey, string> result = memberPhase.MemberRemaps.ToDictionary(x => x.MemberKey, x => x.NativeKey);
			if (result.Count != plan.Members.Count || plan.Members.Any(x => !result.ContainsKey(x.SnapshotMemberKey)))
				throw new InvalidOperationException("C7.10a did not provide a complete native-key remap for the reviewed captured member set.");
			return result;
		}

		private static CollectionOwnerPayloadTarget RequireCapturedTarget(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestoreDeploymentPlan plan)
		{
			CollectionOwnerPayloadTarget target = sealedCapture.OwnerPayloads.Targets.SingleOrDefault(x => x.Target.Equals(plan.Target));
			if (target == null || target.Promoted != plan.Promoted || target.Owners.Count != plan.Owners.Count)
				throw new InvalidDataException("The sealed owner-payload snapshot no longer matches the reviewed C7.9 deployment plan.");
			for (int index = 0; index < target.Owners.Count; index++)
			{
				CollectionOwnerPayloadOwner captured = target.Owners[index];
				CollectionLocalRestoreOwnerBinding binding = plan.Owners[index];
				if (captured.StackIndex != binding.StackIndex || captured.Kind != binding.CapturedOwnerKind ||
					!StringComparer.OrdinalIgnoreCase.Equals(captured.OwnerKey, binding.CapturedOwnerKey) ||
					captured.CurrentWinner != binding.CurrentWinner || captured.RetainedPayload == null)
					throw new InvalidDataException("The sealed owner-payload snapshot differs from the exact reviewed C7.9 owner binding.");
			}
			return target;
		}

		private List<DesiredOwner> BuildDesiredOwners(CollectionOwnerPayloadTarget captured,
			CollectionLocalRestoreDeploymentPlan plan, IDictionary<CollectionMemberKey, string> remaps)
		{
			var result = new List<DesiredOwner>(captured.Owners.Count);
			for (int index = 0; index < captured.Owners.Count; index++)
			{
				CollectionOwnerPayloadOwner owner = captured.Owners[index];
				CollectionLocalRestoreOwnerBinding binding = plan.Owners[index];
				string nativeKey;
				IMod mod = null;
				ModInstallRoot installRoot = ModInstallRoot.Default;
				switch (binding.BindingKind)
				{
					case CollectionLocalRestoreOwnerBindingKind.OriginalValue:
						nativeKey = _services.ModManager.InstallationLog.OriginalValuesKey;
						break;
					case CollectionLocalRestoreOwnerBindingKind.ExistingNative:
						nativeKey = binding.CurrentNativeKey;
						mod = RequireLiveOwner(nativeKey);
						installRoot = _services.ModManager.CaptureInstalledContext(mod).InstallRoot;
						break;
					case CollectionLocalRestoreOwnerBindingKind.RecreatedSnapshotMember:
						if (binding.RecreatedSnapshotMember == null || !remaps.TryGetValue(binding.RecreatedSnapshotMember, out nativeKey))
							throw new InvalidOperationException("A recreated captured owner has no C7.10a native-key remap.");
						mod = RequireLiveOwner(nativeKey);
						installRoot = _services.ModManager.CaptureInstalledContext(mod).InstallRoot;
						break;
					default:
						throw new InvalidOperationException("C7.10b1 cannot execute an unresolved C7.9 owner binding.");
				}
				result.Add(new DesiredOwner(owner, nativeKey, mod, installRoot));
			}
			return result;
		}

		private IMod RequireLiveOwner(string nativeKey)
		{
			IMod mod = CurrentDeploymentManager.GetOwnerMod(nativeKey);
			if (mod == null)
				throw new InvalidOperationException("A reviewed C7.10b1 native owner is no longer active.");
			return mod;
		}

		private List<ModDeploymentRestoreOwner> MaterializeNativeOwners(IEnumerable<DesiredOwner> desired,
			string tempDirectory, CancellationToken cancellationToken)
		{
			var result = new List<ModDeploymentRestoreOwner>();
			int index = 0;
			foreach (DesiredOwner owner in desired)
			{
				string path = Path.Combine(tempDirectory, index.ToString("D6", CultureInfo.InvariantCulture) + ".payload");
				Materialize(owner.Captured.RetainedPayload, path, cancellationToken);
				result.Add(new ModDeploymentRestoreOwner(owner.NativeKey, ToNativeKind(owner.Captured.Kind),
					owner.Mod, owner.InstallRoot, path));
				index++;
			}
			return result;
		}

		private void Materialize(CollectionOwnerPayloadRetention retained, string destination, CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(retained.StableArtifactId);
			if (artifact == null || artifact.ByteLength != retained.ByteLength || !artifact.ContentHash.Equals(retained.ContentHash) ||
				!_artifactStore.VerifyArtifact(retained.StableArtifactId, cancellationToken))
				throw new InvalidDataException("A retained owner payload no longer matches its sealed capture identity.");
			using (Stream source = _artifactStore.OpenRead(retained.StableArtifactId))
			using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize))
			{
				var buffer = new byte[CopyBufferSize];
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					output.Write(buffer, 0, read);
				}
				output.Flush(true);
			}
		}

		private bool VerifyLiveTarget(CollectionOwnerPayloadTarget captured, IList<DesiredOwner> desired,
			CancellationToken cancellationToken)
		{
			if (CurrentDeploymentManager.IsPromoted(captured.Target) != captured.Promoted)
				return false;
			IReadOnlyList<string> liveOwners = CurrentDeploymentManager.GetOwnerKeys(captured.Target);
			if (!liveOwners.SequenceEqual(desired.Select(x => x.NativeKey), StringComparer.OrdinalIgnoreCase))
				return false;

			for (int index = 0; index < desired.Count; index++)
			{
				DesiredOwner owner = desired[index];
				string sourcePath;
				if (owner.Captured.Kind == NativeStateCaptureDeploymentOwnerKind.OriginalValue)
					sourcePath = CurrentDeploymentManager.GetOwnerBackupPath(captured.Target, owner.NativeKey);
				else
					sourcePath = CurrentDeploymentManager.GetOwnerSourcePath(captured.Target, owner.NativeKey);
				if (!FileMatches(sourcePath, owner.Captured.RetainedPayload, cancellationToken))
					return false;
			}

			DesiredOwner winner = desired[desired.Count - 1];
			if (!FileMatches(CurrentDeploymentManager.GetDeploymentPath(captured.Target), winner.Captured.RetainedPayload, cancellationToken))
				return false;
			return MatchesDesiredVirtualFallback(captured, liveOwners, cancellationToken);
		}

		private bool MatchesDesiredVirtualFallback(CollectionOwnerPayloadTarget captured, IReadOnlyList<string> liveOwners,
			CancellationToken cancellationToken)
		{
			if (captured.Promoted)
				return captured.VirtualFallback != null && captured.VirtualFallback.State == CollectionOwnerPayloadVirtualFallbackState.NotApplicable;
			if (captured.VirtualFallback == null)
				return false;

			string fallbackPath;
			bool exists = TryResolvePureVirtualFallback(captured.Target, liveOwners, out fallbackPath);
			switch (captured.VirtualFallback.State)
			{
				case CollectionOwnerPayloadVirtualFallbackState.ExplicitlyAbsent:
					return !exists;
				case CollectionOwnerPayloadVirtualFallbackState.Retained:
					return exists && FileMatches(fallbackPath, captured.VirtualFallback.RetainedPayload, cancellationToken);
				default:
					return false;
			}
		}

		private static bool FileMatches(string path, CollectionOwnerPayloadRetention retained, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(path) || retained == null || !File.Exists(path))
				return false;
			var info = new FileInfo(path);
			if (info.Length != retained.ByteLength)
				return false;
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
			using (SHA256 sha = SHA256.Create())
			{
				var buffer = new byte[CopyBufferSize];
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, buffer, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				string hash = BitConverter.ToString(sha.Hash).Replace("-", String.Empty).ToLowerInvariant();
				return StringComparer.Ordinal.Equals(hash, retained.ContentHash.Value);
			}
		}

		private OwnerIntent CaptureIntent(LocalCaptureIdentity captureIdentity, CollectionLocalRestorePlan plan,
			CollectionOwnerPayloadTarget captured, IList<DesiredOwner> desired)
		{
			bool preimagePromoted = CurrentDeploymentManager.IsPromoted(captured.Target);
			string[] preimageOwnerKeys = CurrentDeploymentManager.GetOwnerKeys(captured.Target).ToArray();
			var preimageOwners = new List<OwnerPreimage>(preimageOwnerKeys.Length);
			foreach (string ownerKey in preimageOwnerKeys)
			{
				string sourcePath = ownerKey.Equals(_services.ModManager.InstallationLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase)
					? CurrentDeploymentManager.GetOwnerBackupPath(captured.Target, ownerKey)
					: CurrentDeploymentManager.GetOwnerSourcePath(captured.Target, ownerKey);
				preimageOwners.Add(new OwnerPreimage(ownerKey, CaptureFileState(sourcePath, CancellationToken.None)));
			}

			FileState deployment = CaptureFileState(CurrentDeploymentManager.GetDeploymentPath(captured.Target), CancellationToken.None);
			VirtualFallbackPreimage fallback = CaptureVirtualFallbackPreimage(captured.Target, preimagePromoted, preimageOwnerKeys);
			return new OwnerIntent(captureIdentity.ToString(), plan.PlanFingerprint, captured.Target, captured.Promoted,
				preimagePromoted, preimageOwners, deployment, fallback, desired.Select(x => x.NativeKey).ToArray(),
				desired.Select(x => x.Captured.RetainedPayload.StableArtifactId).ToArray(), CreateDesiredFallbackIdentity(captured));
		}

		private void PersistIntent(string ownerId, string role, OwnerIntent intent)
		{
			byte[] bytes = SerializeIntent(intent);
			CollectionsRetainedArtifact artifact;
			using (var stream = new MemoryStream(bytes, false)) artifact = _artifactStore.Publish(stream);
			_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, role);
		}

		private void MarkVerified(string ownerId, string role, string artifactId)
		{
			_referenceStore.AcquireExclusiveRoleReference(artifactId, CollectionsRetainedArtifactOwnerKind.Operation, ownerId, role);
		}

		private bool TryRecoverVerified(string ownerId, string verifiedRole, CollectionOwnerPayloadTarget captured,
			IList<DesiredOwner> desired)
		{
			CollectionsRetainedArtifactReferenceRecord verified = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, verifiedRole);
			if (verified == null)
				return false;
			if (!VerifyLiveTarget(captured, desired, CancellationToken.None))
				throw new InvalidOperationException("A durable C7.10b1 verified checkpoint no longer matches authoritative native owner state.");
			return true;
		}

		private bool MatchesPreimage(OwnerIntent intent)
		{
			if (CurrentDeploymentManager.IsPromoted(intent.Target) != intent.PreimagePromoted)
				return false;
			string[] liveOwners = CurrentDeploymentManager.GetOwnerKeys(intent.Target).ToArray();
			if (!liveOwners.SequenceEqual(intent.PreimageOwners.Select(x => x.OwnerKey), StringComparer.OrdinalIgnoreCase))
				return false;
			foreach (OwnerPreimage owner in intent.PreimageOwners)
			{
				string sourcePath = owner.OwnerKey.Equals(_services.ModManager.InstallationLog.OriginalValuesKey, StringComparison.OrdinalIgnoreCase)
					? CurrentDeploymentManager.GetOwnerBackupPath(intent.Target, owner.OwnerKey)
					: CurrentDeploymentManager.GetOwnerSourcePath(intent.Target, owner.OwnerKey);
				if (!MatchesFileState(sourcePath, owner.Payload, CancellationToken.None))
					return false;
			}
			if (!MatchesFileState(CurrentDeploymentManager.GetDeploymentPath(intent.Target), intent.PreimageDeployment, CancellationToken.None))
				return false;
			return MatchesVirtualFallbackPreimage(intent.Target, liveOwners, intent.PreimageFallback);
		}

		private void ValidateIntent(OwnerIntent intent, LocalCaptureIdentity captureIdentity, CollectionLocalRestorePlan plan,
			CollectionOwnerPayloadTarget captured, IList<DesiredOwner> desired)
		{
			if (intent == null || !StringComparer.Ordinal.Equals(intent.CaptureId, captureIdentity.ToString()) ||
				!StringComparer.Ordinal.Equals(intent.PlanFingerprint, plan.PlanFingerprint) || !intent.Target.Equals(captured.Target) ||
				intent.DesiredPromoted != captured.Promoted ||
				!intent.DesiredOwners.SequenceEqual(desired.Select(x => x.NativeKey), StringComparer.OrdinalIgnoreCase) ||
				!intent.ArtifactIds.SequenceEqual(desired.Select(x => x.Captured.RetainedPayload.StableArtifactId), StringComparer.Ordinal) ||
				!StringComparer.Ordinal.Equals(intent.DesiredFallbackIdentity, CreateDesiredFallbackIdentity(captured)))
				throw new InvalidDataException("The durable C7.10b1 owner intent differs from the reviewed restore plan.");
		}

		private OwnerIntent ReadIntent(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable C7.10b1 owner intent no longer matches its sealed retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], IntentFormat))
					throw new InvalidDataException("Unsupported C7.10b1 owner-intent format. Exact payload/fallback preimage evidence is required.");
				return new OwnerIntent((string)root["captureId"], (string)root["planFingerprint"],
					ReadTarget((JObject)root["target"]), (bool)root["desiredPromoted"], (bool)root["preimagePromoted"],
					ReadOwnerPreimages((JArray)root["preimageOwners"]), ReadFileState((JObject)root["preimageDeployment"]),
					ReadFallbackPreimage((JObject)root["preimageFallback"]), ReadStrings((JArray)root["desiredOwners"]),
					ReadStrings((JArray)root["artifactIds"]), (string)root["desiredFallbackIdentity"]);
			}
		}

		private static byte[] SerializeIntent(OwnerIntent intent)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue(IntentFormat);
				writer.WritePropertyName("captureId"); writer.WriteValue(intent.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(intent.PlanFingerprint);
				writer.WritePropertyName("target"); WriteTarget(writer, intent.Target);
				writer.WritePropertyName("desiredPromoted"); writer.WriteValue(intent.DesiredPromoted);
				writer.WritePropertyName("preimagePromoted"); writer.WriteValue(intent.PreimagePromoted);
				writer.WritePropertyName("preimageOwners"); writer.WriteStartArray();
				foreach (OwnerPreimage owner in intent.PreimageOwners)
				{
					writer.WriteStartObject(); writer.WritePropertyName("ownerKey"); writer.WriteValue(owner.OwnerKey);
					writer.WritePropertyName("payload"); WriteFileState(writer, owner.Payload); writer.WriteEndObject();
				}
				writer.WriteEndArray();
				writer.WritePropertyName("preimageDeployment"); WriteFileState(writer, intent.PreimageDeployment);
				writer.WritePropertyName("preimageFallback"); WriteFallbackPreimage(writer, intent.PreimageFallback);
				WriteStrings(writer, "desiredOwners", intent.DesiredOwners);
				WriteStrings(writer, "artifactIds", intent.ArtifactIds);
				writer.WritePropertyName("desiredFallbackIdentity"); writer.WriteValue(intent.DesiredFallbackIdentity);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private ModDeploymentRestoreFallback MaterializeNativeFallback(CollectionOwnerPayloadTarget captured, string tempDirectory,
			CancellationToken cancellationToken)
		{
			if (captured.Promoted)
				return null;
			if (captured.VirtualFallback == null)
				throw new InvalidDataException("A pure-Virtual restore is missing its sealed fallback state.");
			switch (captured.VirtualFallback.State)
			{
				case CollectionOwnerPayloadVirtualFallbackState.ExplicitlyAbsent:
					return new ModDeploymentRestoreFallback(ModDeploymentRestoreFallbackKind.ExplicitlyAbsent, null);
				case CollectionOwnerPayloadVirtualFallbackState.Retained:
					string path = Path.Combine(tempDirectory, "virtual-fallback.payload");
					Materialize(captured.VirtualFallback.RetainedPayload, path, cancellationToken);
					return new ModDeploymentRestoreFallback(ModDeploymentRestoreFallbackKind.Retained, path);
				default:
					throw new InvalidDataException("A pure-Virtual restore requires an explicit absent or retained fallback state.");
			}
		}

		private VirtualFallbackPreimage CaptureVirtualFallbackPreimage(ModDeploymentTarget target, bool promoted,
			IReadOnlyList<string> ownerKeys)
		{
			if (promoted)
				return new VirtualFallbackPreimage(VirtualFallbackPreimageKind.NotApplicable, null);
			string path;
			bool exists = TryResolvePureVirtualFallback(target, ownerKeys, out path);
			return exists
				? new VirtualFallbackPreimage(VirtualFallbackPreimageKind.Present, CaptureFileState(path, CancellationToken.None))
				: new VirtualFallbackPreimage(VirtualFallbackPreimageKind.ExplicitlyAbsent, null);
		}

		private bool MatchesVirtualFallbackPreimage(ModDeploymentTarget target, IReadOnlyList<string> ownerKeys,
			VirtualFallbackPreimage expected)
		{
			if (expected == null) return false;
			if (expected.Kind == VirtualFallbackPreimageKind.NotApplicable)
				return CurrentDeploymentManager.IsPromoted(target);
			string path;
			bool exists = TryResolvePureVirtualFallback(target, ownerKeys, out path);
			if (expected.Kind == VirtualFallbackPreimageKind.ExplicitlyAbsent) return !exists;
			return exists && MatchesFileState(path, expected.Payload, CancellationToken.None);
		}

		private bool TryResolvePureVirtualFallback(ModDeploymentTarget target, IReadOnlyList<string> ownerKeys, out string fallbackPath)
		{
			fallbackPath = String.Empty;
			if (ownerKeys == null || ownerKeys.Count == 0)
			{
				string deploymentPath = CurrentDeploymentManager.GetDeploymentPath(target);
				if (!File.Exists(deploymentPath)) return false;
				fallbackPath = deploymentPath;
				return true;
			}
			var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string ownerKey in ownerKeys)
			{
				string path = _services.ModManager.VirtualModActivator.GetVirtualOverwritePath(target, ownerKey);
				if (!String.IsNullOrWhiteSpace(path) && File.Exists(path)) paths.Add(Path.GetFullPath(path));
			}
			if (paths.Count > 1)
				throw new InvalidDataException("The pure-Virtual target exposes more than one unmanaged fallback payload.");
			if (paths.Count == 0) return false;
			fallbackPath = paths.Single();
			return true;
		}

		private static FileState CaptureFileState(string path, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new FileState(false, 0, String.Empty);
			var info = new FileInfo(path);
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
			using (SHA256 sha = SHA256.Create())
			{
				var buffer = new byte[CopyBufferSize]; int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha.TransformBlock(buffer, 0, read, buffer, 0);
				}
				sha.TransformFinalBlock(new byte[0], 0, 0);
				return new FileState(true, info.Length, BitConverter.ToString(sha.Hash).Replace("-", String.Empty).ToLowerInvariant());
			}
		}

		private static bool MatchesFileState(string path, FileState expected, CancellationToken cancellationToken)
		{
			if (expected == null) return false;
			FileState actual = CaptureFileState(path, cancellationToken);
			return actual.Exists == expected.Exists && actual.ByteLength == expected.ByteLength &&
				StringComparer.Ordinal.Equals(actual.Sha256, expected.Sha256);
		}

		private static string CreateDesiredFallbackIdentity(CollectionOwnerPayloadTarget captured)
		{
			if (captured.Promoted) return "not-applicable";
			if (captured.VirtualFallback == null) return "missing";
			if (captured.VirtualFallback.State == CollectionOwnerPayloadVirtualFallbackState.ExplicitlyAbsent) return "absent";
			if (captured.VirtualFallback.State == CollectionOwnerPayloadVirtualFallbackState.Retained && captured.VirtualFallback.RetainedPayload != null)
				return "retained:" + captured.VirtualFallback.RetainedPayload.StableArtifactId;
			return "unsupported:" + (int)captured.VirtualFallback.State;
		}

		private static void WriteFileState(JsonWriter writer, FileState value)
		{
			writer.WriteStartObject(); writer.WritePropertyName("exists"); writer.WriteValue(value.Exists);
			writer.WritePropertyName("byteLength"); writer.WriteValue(value.ByteLength);
			writer.WritePropertyName("sha256"); writer.WriteValue(value.Sha256); writer.WriteEndObject();
		}

		private static FileState ReadFileState(JObject value)
		{
			if (value == null) throw new InvalidDataException("A C7.10b1 exact file preimage is missing.");
			return new FileState((bool)value["exists"], (long)value["byteLength"], (string)value["sha256"]);
		}

		private static OwnerPreimage[] ReadOwnerPreimages(JArray array)
		{
			if (array == null) throw new InvalidDataException("The C7.10b1 owner preimage sequence is missing.");
			return array.Select(x => (JObject)x).Select(x => new OwnerPreimage((string)x["ownerKey"], ReadFileState((JObject)x["payload"]))).ToArray();
		}

		private static void WriteFallbackPreimage(JsonWriter writer, VirtualFallbackPreimage value)
		{
			writer.WriteStartObject(); writer.WritePropertyName("kind"); writer.WriteValue((int)value.Kind);
			writer.WritePropertyName("payload"); if (value.Payload == null) writer.WriteNull(); else WriteFileState(writer, value.Payload); writer.WriteEndObject();
		}

		private static VirtualFallbackPreimage ReadFallbackPreimage(JObject value)
		{
			if (value == null) throw new InvalidDataException("The C7.10b1 pure-Virtual fallback preimage is missing.");
			VirtualFallbackPreimageKind kind = (VirtualFallbackPreimageKind)(int)value["kind"];
			if (!Enum.IsDefined(typeof(VirtualFallbackPreimageKind), kind)) throw new InvalidDataException("Unknown C7.10b1 fallback preimage kind.");
			JObject payload = value["payload"] as JObject;
			return new VirtualFallbackPreimage(kind, payload == null ? null : ReadFileState(payload));
		}

		private CollectionOperation CompleteOwnershipPhase(CollectionOperation operation, LocalCaptureIdentity captureIdentity,
			CollectionLocalRestorePlan plan, int targetCount)
		{
			string ownerId = operation.Identity.OperationId.ToString("D");
			CollectionsRetainedArtifactReferenceRecord existing = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Operation, ownerId, CompleteRole);
			PhaseCompletion completion;
			if (existing == null)
			{
				CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
				completion = new PhaseCompletion(captureIdentity.ToString(), plan.PlanFingerprint, ownerId,
					current.CheckpointSequence, targetCount);
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(SerializePhaseCompletion(completion), false))
					artifact = _artifactStore.Publish(stream);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					ownerId, CompleteRole);
			}
			else
			{
				try
				{
					completion = ReadPhaseCompletion(existing.ArtifactId);
				}
				catch
				{
					MarkRecoveryRequired(operation);
					throw;
				}
				if (!StringComparer.Ordinal.Equals(completion.CaptureId, captureIdentity.ToString()) ||
					!StringComparer.Ordinal.Equals(completion.PlanFingerprint, plan.PlanFingerprint) ||
					!StringComparer.Ordinal.Equals(completion.OperationId, ownerId) || completion.TargetCount != targetCount)
				{
					MarkRecoveryRequired(operation);
					throw new InvalidDataException("The durable C7.10b1 phase-completion marker differs from the reviewed restore.");
				}
			}

			CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (persisted.CheckpointSequence == completion.CheckpointBefore)
				return AdvanceSafeBoundary(persisted);
			if (persisted.CheckpointSequence == completion.CheckpointBefore + 1 &&
				persisted.Phase == CollectionOperationPhase.PausedAtSafeBoundary &&
				persisted.ResultState == CollectionOperationResultState.Pending)
				return persisted;

			MarkRecoveryRequired(persisted);
			throw new InvalidOperationException("The C7.10b1 phase-completion checkpoint no longer matches the durable operation journal.");
		}

		private byte[] SerializePhaseCompletion(PhaseCompletion completion)
		{
			using (var stream = new MemoryStream())
			using (var text = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
			using (var writer = new JsonTextWriter(text))
			{
				writer.WriteStartObject();
				writer.WritePropertyName("format"); writer.WriteValue("nmm-ce.collections.local-restore-owner-phase/1");
				writer.WritePropertyName("captureId"); writer.WriteValue(completion.CaptureId);
				writer.WritePropertyName("planFingerprint"); writer.WriteValue(completion.PlanFingerprint);
				writer.WritePropertyName("operationId"); writer.WriteValue(completion.OperationId);
				writer.WritePropertyName("checkpointBefore"); writer.WriteValue(completion.CheckpointBefore);
				writer.WritePropertyName("targetCount"); writer.WriteValue(completion.TargetCount);
				writer.WriteEndObject(); writer.Flush(); text.Flush();
				return stream.ToArray();
			}
		}

		private PhaseCompletion ReadPhaseCompletion(string artifactId)
		{
			if (!_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The durable C7.10b1 phase-completion marker no longer matches its retained bytes.");
			using (Stream stream = _artifactStore.OpenRead(artifactId))
			using (var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false))
			{
				JObject root = JObject.Parse(reader.ReadToEnd());
				if (!StringComparer.Ordinal.Equals((string)root["format"], "nmm-ce.collections.local-restore-owner-phase/1"))
					throw new InvalidDataException("Unsupported C7.10b1 phase-completion format.");
				return new PhaseCompletion((string)root["captureId"], (string)root["planFingerprint"],
					(string)root["operationId"], (long)root["checkpointBefore"], (int)root["targetCount"]);
			}
		}

		private CollectionOperation AdvanceSafeBoundary(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, current.CheckpointSequence + 1, CollectionOperationPhase.PausedAtSafeBoundary,
				CollectionOperationResultState.Pending, current.NativeChildren);
			_operationStore.SaveOperation(updated);
			return updated;
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			CollectionOperation current = _operationStore.GetOperation(operation.Identity) ?? operation;
			if (current.IsTerminal || current.Phase == CollectionOperationPhase.RecoveryRequired)
				return;
			CollectionOperation updated = new CollectionOperation(current.Identity, current.Kind, current.Collection, current.Target,
				current.Revision, current.PlanIdentity, current.CheckpointSequence + 1, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, current.NativeChildren);
			_operationStore.SaveOperation(updated);
		}

		private static ModDeploymentRestoreOwnerKind ToNativeKind(NativeStateCaptureDeploymentOwnerKind kind)
		{
			switch (kind)
			{
				case NativeStateCaptureDeploymentOwnerKind.OriginalValue: return ModDeploymentRestoreOwnerKind.OriginalValue;
				case NativeStateCaptureDeploymentOwnerKind.Direct: return ModDeploymentRestoreOwnerKind.Direct;
				case NativeStateCaptureDeploymentOwnerKind.Virtual: return ModDeploymentRestoreOwnerKind.Virtual;
				default: throw new InvalidOperationException("An unresolved captured owner cannot be restored.");
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c710b1-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void TryDeleteDirectory(string path)
		{
			if (String.IsNullOrWhiteSpace(path)) return;
			try { if (Directory.Exists(path)) Directory.Delete(path, true); }
			catch { }
		}

		private static string CreateTargetToken(ModDeploymentTarget target)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(target.ToString()));
				return BitConverter.ToString(bytes).Replace("-", String.Empty).ToLowerInvariant().Substring(0, 24);
			}
		}

		private static void WriteTarget(JsonWriter writer, ModDeploymentTarget target)
		{
			writer.WriteStartObject();
			writer.WritePropertyName("root"); writer.WriteValue((int)target.Root);
			writer.WritePropertyName("relativePath"); writer.WriteValue(target.RelativePath);
			writer.WriteEndObject();
		}

		private static ModDeploymentTarget ReadTarget(JObject value)
		{
			if (value == null) throw new InvalidDataException("A C7.10b1 owner intent is missing its deployment target.");
			return ModDeploymentTargetResolver.FromCanonical((ModDeploymentRoot)(int)value["root"], (string)value["relativePath"]);
		}

		private static void WriteStrings(JsonWriter writer, string name, IEnumerable<string> values)
		{
			writer.WritePropertyName(name); writer.WriteStartArray();
			foreach (string value in values) writer.WriteValue(value);
			writer.WriteEndArray();
		}

		private static string[] ReadStrings(JArray array)
		{
			if (array == null) throw new InvalidDataException("A C7.10b1 owner intent is missing an owner/artifact sequence.");
			return array.Select(x => (string)x).ToArray();
		}

		private sealed class DesiredOwner
		{
			internal DesiredOwner(CollectionOwnerPayloadOwner captured, string nativeKey, IMod mod, ModInstallRoot installRoot)
			{
				Captured = captured; NativeKey = nativeKey; Mod = mod; InstallRoot = installRoot;
			}
			internal CollectionOwnerPayloadOwner Captured { get; }
			internal string NativeKey { get; }
			internal IMod Mod { get; }
			internal ModInstallRoot InstallRoot { get; }
		}

		private sealed class PhaseCompletion
		{
			internal PhaseCompletion(string captureId, string planFingerprint, string operationId, long checkpointBefore, int targetCount)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; OperationId = operationId;
				CheckpointBefore = checkpointBefore; TargetCount = targetCount;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal string OperationId { get; }
			internal long CheckpointBefore { get; }
			internal int TargetCount { get; }
		}

		private enum VirtualFallbackPreimageKind
		{
			NotApplicable = 0,
			ExplicitlyAbsent = 1,
			Present = 2
		}

		private sealed class FileState
		{
			internal FileState(bool exists, long byteLength, string sha256)
			{
				if (byteLength < 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
				if (exists && (String.IsNullOrWhiteSpace(sha256) || sha256.Length != 64))
					throw new InvalidDataException("An existing exact file preimage requires a SHA-256 digest.");
				if (!exists && (byteLength != 0 || !String.IsNullOrEmpty(sha256)))
					throw new InvalidDataException("An absent exact file preimage cannot carry payload metadata.");
				Exists = exists; ByteLength = byteLength; Sha256 = sha256 ?? String.Empty;
			}
			internal bool Exists { get; }
			internal long ByteLength { get; }
			internal string Sha256 { get; }
		}

		private sealed class OwnerPreimage
		{
			internal OwnerPreimage(string ownerKey, FileState payload)
			{
				if (String.IsNullOrWhiteSpace(ownerKey)) throw new InvalidDataException("An exact owner preimage requires its native key.");
				OwnerKey = ownerKey; Payload = payload ?? throw new ArgumentNullException(nameof(payload));
			}
			internal string OwnerKey { get; }
			internal FileState Payload { get; }
		}

		private sealed class VirtualFallbackPreimage
		{
			internal VirtualFallbackPreimage(VirtualFallbackPreimageKind kind, FileState payload)
			{
				if ((kind == VirtualFallbackPreimageKind.Present) != (payload != null))
					throw new InvalidDataException("Only a present pure-Virtual fallback preimage may carry payload bytes.");
				Kind = kind; Payload = payload;
			}
			internal VirtualFallbackPreimageKind Kind { get; }
			internal FileState Payload { get; }
		}

		private sealed class OwnerIntent
		{
			internal OwnerIntent(string captureId, string planFingerprint, ModDeploymentTarget target, bool desiredPromoted,
				bool preimagePromoted, IEnumerable<OwnerPreimage> preimageOwners, FileState preimageDeployment,
				VirtualFallbackPreimage preimageFallback, IEnumerable<string> desiredOwners, IEnumerable<string> artifactIds,
				string desiredFallbackIdentity)
			{
				CaptureId = captureId; PlanFingerprint = planFingerprint; Target = target; DesiredPromoted = desiredPromoted;
				PreimagePromoted = preimagePromoted; PreimageOwners = preimageOwners.ToArray();
				PreimageDeployment = preimageDeployment ?? throw new ArgumentNullException(nameof(preimageDeployment));
				PreimageFallback = preimageFallback ?? throw new ArgumentNullException(nameof(preimageFallback));
				DesiredOwners = desiredOwners.ToArray(); ArtifactIds = artifactIds.ToArray();
				DesiredFallbackIdentity = desiredFallbackIdentity ?? String.Empty;
			}
			internal string CaptureId { get; }
			internal string PlanFingerprint { get; }
			internal ModDeploymentTarget Target { get; }
			internal bool DesiredPromoted { get; }
			internal bool PreimagePromoted { get; }
			internal OwnerPreimage[] PreimageOwners { get; }
			internal FileState PreimageDeployment { get; }
			internal VirtualFallbackPreimage PreimageFallback { get; }
			internal string[] DesiredOwners { get; }
			internal string[] ArtifactIds { get; }
			internal string DesiredFallbackIdentity { get; }
		}

	}
}
