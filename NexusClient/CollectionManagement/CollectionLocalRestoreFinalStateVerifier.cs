using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Performs the one aggregate C7 Local Collection restore verification and publishes the terminal committed journal state.
	/// </summary>
	/// <remarks>
	/// The verifier holds the canonical target mutation reservation while it reloads native authority, proves member/remap and
	/// extra-effect closure, then re-verifies every supported restored domain. Only that same stable observation may advance the
	/// restore operation to Completed/Committed.
	/// </remarks>
	public sealed class CollectionLocalRestoreFinalStateVerifier
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;
		private readonly CollectionLocalRestoreOwnershipExecutor _ownershipExecutor;
		private readonly CollectionLocalRestoreReplayExecutor _replayExecutor;
		private readonly CollectionLocalRestorePluginExecutor _pluginExecutor;
		private readonly CollectionLocalRestoreIniExecutor _iniExecutor;
		private readonly CollectionLocalRestoreGameValueExecutor _gameValueExecutor;
		private readonly CollectionLocalRestoreUserMetadataExecutor _userMetadataExecutor;
		private readonly CollectionLocalRestoreProfileAssociationExecutor _profileAssociationExecutor;

		/// <summary>Creates the production aggregate Local restore verifier over the current native/Collections services.</summary>
		public CollectionLocalRestoreFinalStateVerifier(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(services, gameStorageService, operationStore, associationStore, artifactStore, referenceStore,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionLocalRestoreFinalStateVerifier(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore,
			CollectionTargetMutationLeaseManager mutationLeaseManager, CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			if (referenceStore == null) throw new ArgumentNullException(nameof(referenceStore));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
			_ownershipExecutor = new CollectionLocalRestoreOwnershipExecutor(services, gameStorageService, operationStore,
				artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			_replayExecutor = new CollectionLocalRestoreReplayExecutor(services, gameStorageService, operationStore,
				artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			_pluginExecutor = new CollectionLocalRestorePluginExecutor(services, gameStorageService, operationStore,
				artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			_iniExecutor = new CollectionLocalRestoreIniExecutor(services, gameStorageService, operationStore,
				artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			_gameValueExecutor = new CollectionLocalRestoreGameValueExecutor(services, gameStorageService, operationStore,
				artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			_userMetadataExecutor = new CollectionLocalRestoreUserMetadataExecutor(services, gameStorageService, operationStore,
				artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			_profileAssociationExecutor = new CollectionLocalRestoreProfileAssociationExecutor(services, gameStorageService,
				operationStore, associationStore, artifactStore, referenceStore, mutationLeaseManager, authorityValidator);
			if (_services.ModManager == null)
				throw new InvalidOperationException("Final Local restore verification requires the active native ModManager.");
		}

		/// <summary>
		/// Re-verifies the complete supported restored state under one stable target authority and commits the operation journal.
		/// </summary>
		public async Task<CollectionOperation> VerifyAndCommitAsync(CollectionSealedCaptureSnapshot sealedCapture,
			CollectionLocalRestorePlan reviewedPlan, CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreProfileAssociationExecutionResult profilePhase, GameStoragePathSet paths,
			CancellationToken cancellationToken)
		{
			ValidateInputs(sealedCapture, reviewedPlan, memberPhase, profilePhase, paths);
			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The live canonical target no longer matches the reviewed Local Collection restore.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				CollectionOperation operation = RequireFinalizableOperation(profilePhase.Operation.Identity, reviewedPlan);
				try
				{
					CollectionLocalRestorePlan verifiedMembers = VerifyMemberClosure(sealedCapture, reviewedPlan, memberPhase, cancellationToken);
					_ownershipExecutor.VerifyFinalState(sealedCapture, reviewedPlan, memberPhase, cancellationToken);
					_replayExecutor.VerifyFinalState(sealedCapture, reviewedPlan, memberPhase, paths, cancellationToken);
					_pluginExecutor.VerifyFinalState(sealedCapture);
					_iniExecutor.VerifyFinalState(sealedCapture, reviewedPlan, memberPhase);
					_gameValueExecutor.VerifyFinalState(sealedCapture, reviewedPlan, memberPhase);
					_userMetadataExecutor.VerifyFinalState(sealedCapture, reviewedPlan, memberPhase);
					_profileAssociationExecutor.VerifyFinalState(operation, sealedCapture, reviewedPlan);
					cancellationToken.ThrowIfCancellationRequested();

					CollectionOperation completed = new CollectionOperation(operation.Identity, operation.Kind,
						operation.Collection, operation.Target, operation.Revision, operation.PlanIdentity,
						checked(operation.CheckpointSequence + 1), CollectionOperationPhase.Completed,
						CollectionOperationResultState.Committed, operation.NativeChildren);
					CollectionsAssociationTargetSnapshot associations = _associationStore.GetTargetSnapshot(operation.Target);
					IReadOnlyList<CollectionMemberBinding> verifiedBindings = CreateVerifiedAssociationBindings(
						sealedCapture.InstalledIdentities, verifiedMembers, associations);
					_associationStore.SaveVerifiedLocalRestore(completed, associations, verifiedBindings);
					return completed;
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch
				{
					MarkRecoveryRequired(_operationStore.GetOperation(operation.Identity) ?? operation);
					throw;
				}
			}
		}

		private CollectionLocalRestorePlan VerifyMemberClosure(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase, CancellationToken cancellationToken)
		{
			ModManager manager = _services.ModManager;
			NativeStateCaptureSnapshot native = new NativeStateCaptureReader(manager.InstallationLog, manager.VirtualModActivator,
				manager.DeploymentManager, _services.PluginManager, manager.GameMode).Capture();
			CollectionNativeStateIndex collection = new CollectionNativeStateReader(manager.InstallationLog, manager.VirtualModActivator,
				_services.PluginManager, manager.GameMode, _associationStore).Capture(reviewedPlan.Target, native);
			CollectionLocalRestorePlan finalPlan = new CollectionLocalRestorePlanner(_artifactStore).Plan(sealedCapture,
				reviewedPlan.Target, collection.Fingerprint, native, cancellationToken);
			if (finalPlan.Issues.Count != 0 || finalPlan.CurrentNativeKeysToRemove.Count != 0 ||
				finalPlan.Members.Count != reviewedPlan.Members.Count ||
				finalPlan.Members.Any(x => x.Action != CollectionLocalRestoreMemberAction.ReuseExistingNative ||
					String.IsNullOrWhiteSpace(x.CurrentNativeKey)))
				throw new InvalidOperationException("Final Local restore verification found missing members, extra managed effects or unresolved native registrations.");

			var remaps = new Dictionary<CollectionMemberKey, string>();
			foreach (CollectionLocalRestoreMemberRemap remap in memberPhase.MemberRemaps)
			{
				if (remap == null || remap.MemberKey == null || remaps.ContainsKey(remap.MemberKey))
					throw new InvalidOperationException("Final Local restore verification found a duplicate or invalid native-key remap.");
				remaps.Add(remap.MemberKey, remap.NativeKey);
			}
			if (remaps.Count != reviewedPlan.Members.Count)
				throw new InvalidOperationException("Final Local restore verification did not retain one native-key remap for every reviewed captured member.");

			foreach (CollectionLocalRestoreMemberPlan member in finalPlan.Members)
			{
				string expected;
				if (!remaps.TryGetValue(member.SnapshotMemberKey, out expected) ||
					!StringComparer.OrdinalIgnoreCase.Equals(expected, member.CurrentNativeKey))
					throw new InvalidOperationException("Final Local restore verification found a native-key remap that no longer matches the verified member phase.");
			}
			return finalPlan;
		}

		/// <summary>Maps captured provenance for unchanged association baselines onto the native registrations proved by final verification.</summary>
		internal static IReadOnlyList<CollectionMemberBinding> CreateVerifiedAssociationBindings(
			CollectionInstalledIdentitySnapshot capturedIdentities, CollectionLocalRestorePlan verifiedPlan,
			CollectionsAssociationTargetSnapshot associations)
		{
			if (capturedIdentities == null) throw new ArgumentNullException(nameof(capturedIdentities));
			if (verifiedPlan == null) throw new ArgumentNullException(nameof(verifiedPlan));
			if (associations == null) throw new ArgumentNullException(nameof(associations));
			if (!capturedIdentities.Target.Equals(verifiedPlan.Target) || !associations.Target.Equals(verifiedPlan.Target))
				throw new InvalidOperationException("Verified Local restore bindings must belong to the captured canonical target.");
			if (!verifiedPlan.IsReadyForReview || verifiedPlan.CurrentNativeKeysToRemove.Count != 0)
				throw new InvalidOperationException("Association rebinding requires a restored member set without unresolved or extra native effects.");

			var result = new List<CollectionMemberBinding>();
			var members = new HashSet<CollectionMemberBinding>();
			foreach (CollectionLocalRestoreMemberPlan member in verifiedPlan.Members)
			{
				if (member.Action != CollectionLocalRestoreMemberAction.ReuseExistingNative || String.IsNullOrWhiteSpace(member.CurrentNativeKey))
					throw new InvalidOperationException("Association rebinding requires a verified live native registration for every restored member.");
				CollectionInstalledModIdentity captured = capturedIdentities.Mods.Single(x =>
					StringComparer.OrdinalIgnoreCase.Equals(x.NativeSnapshotKey, member.CapturedNativeKey));
				foreach (CollectionInstalledMemberProvenance provenance in captured.Provenance)
				{
					CollectionMemberBinding baseline = associations.Bindings.SingleOrDefault(x =>
						x.Association.AssociationId == provenance.AssociationId && x.MemberKey.Equals(provenance.MemberKey));
					// A saved Local capture must never recreate untracked associations or overwrite a different remote revision/recipe.
					if (baseline == null || !baseline.Association.Revision.Equals(provenance.Revision) ||
						!baseline.VerifiedRecipe.Equals(provenance.VerifiedRecipe))
						continue;
					var verified = new CollectionMemberBinding(baseline.Association, baseline.MemberKey,
						new NativeModInstanceIdentity(verifiedPlan.Target, member.CurrentNativeKey), baseline.VerifiedRecipe, baseline.BindingKind);
					if (!members.Add(verified))
						throw new InvalidOperationException("Captured provenance maps one Collection member to multiple restored registrations.");
					result.Add(verified);
				}
			}
			return result;
		}

		private static void ValidateInputs(CollectionSealedCaptureSnapshot sealedCapture, CollectionLocalRestorePlan reviewedPlan,
			CollectionLocalRestoreMemberExecutionResult memberPhase,
			CollectionLocalRestoreProfileAssociationExecutionResult profilePhase, GameStoragePathSet paths)
		{
			if (sealedCapture == null) throw new ArgumentNullException(nameof(sealedCapture));
			if (reviewedPlan == null) throw new ArgumentNullException(nameof(reviewedPlan));
			if (memberPhase == null) throw new ArgumentNullException(nameof(memberPhase));
			if (profilePhase == null) throw new ArgumentNullException(nameof(profilePhase));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!reviewedPlan.IsReadyForReview || sealedCapture.Capture.Capability != LocalCaptureCapability.LocallyRestorableWithinScope)
				throw new InvalidOperationException("Final Local restore verification requires one reviewed locally-restorable plan.");
			if (!sealedCapture.Capture.Identity.Equals(reviewedPlan.CaptureIdentity) ||
				!sealedCapture.Capture.SourceTarget.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The final Local restore verification inputs do not belong to the same sealed target/capture.");
			if (!memberPhase.IsReadyForEffectReconciliation || !profilePhase.IsReadyForFinalVerification ||
				!memberPhase.Operation.Identity.Equals(profilePhase.Operation.Identity))
				throw new InvalidOperationException("Final Local restore verification requires the same operation at its verified member and C7.11 safe boundaries.");
		}

		private CollectionOperation RequireFinalizableOperation(CollectionOperationIdentity identity, CollectionLocalRestorePlan reviewedPlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.RestoreLocalCapture || operation.IsTerminal ||
				operation.RequiresRecovery || operation.HasUnreconciledNativeChild || operation.HasUnknownNativeDurability ||
				operation.Phase != CollectionOperationPhase.PausedAtSafeBoundary ||
				operation.ResultState != CollectionOperationResultState.Pending || !operation.Target.Equals(reviewedPlan.Target))
				throw new InvalidOperationException("The Local restore operation is not at the durable safe boundary required for aggregate final verification.");
			return operation;
		}

		private void MarkRecoveryRequired(CollectionOperation operation)
		{
			if (operation == null || operation.IsTerminal || (operation.Phase == CollectionOperationPhase.RecoveryRequired &&
				operation.ResultState == CollectionOperationResultState.RecoveryRequired)) return;
			_operationStore.SaveOperation(new CollectionOperation(operation.Identity, operation.Kind, operation.Collection,
				operation.Target, operation.Revision, operation.PlanIdentity, checked(operation.CheckpointSequence + 1),
				CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired,
				operation.NativeChildren));
		}
	}
}
