using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Result of C10.7 replay/verification of the qualified C9 overrides after candidate execution.</summary>
	public sealed class CollectionRevisionUpdateOverrideReapplyResult
	{
		internal CollectionRevisionUpdateOverrideReapplyResult(CollectionOperation operation, int replayedCount)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			ReplayedCount = replayedCount;
		}

		public CollectionOperation Operation { get; }
		public int ReplayedCount { get; }
	}

	/// <summary>
	/// C10.7 replays only override state that NMM can reconstruct from a typed C9 representation. The current characterized
	/// subset is member enabled/disabled state for Virtual installs. Opaque file/plugin/configuration fingerprints remain
	/// ActionRequired in C10.5 and never reach this mutating boundary.
	/// </summary>
	public sealed class CollectionRevisionUpdateOverrideReapplyCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionsNativeChildRecoveryManifestStore _manifestStore;
		private readonly CollectionRevisionUpdateReviewCoordinator _reviewCoordinator;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionRevisionUpdateOverrideReapplyCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsResolvedPlanStore planStore,
			CollectionsAssociationStore associationStore, CollectionsNativeChildRecoveryManifestStore manifestStore)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_manifestStore = manifestStore ?? throw new ArgumentNullException(nameof(manifestStore));
			if (planStore == null) throw new ArgumentNullException(nameof(planStore));
			_reviewCoordinator = new CollectionRevisionUpdateReviewCoordinator(operationStore, planStore, associationStore);
			_mutationLeaseManager = CollectionTargetMutationLeaseManager.Shared;
			_authorityValidator = new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services);
		}

		/// <summary>
		/// Replays and verifies the exact typed override actions frozen by C10.2/C10.5. The operation is checkpointed before
		/// the first mutation; a restart may call this method again and already-satisfied actions are skipped idempotently.
		/// </summary>
		public async Task<CollectionRevisionUpdateOverrideReapplyResult> ExecuteAsync(CollectionOperationIdentity operationIdentity,
			CollectionRevisionUpdatePlan updatePlan, CollectionRevisionUpdateOverridePreservationPlan preservation,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			if (operationIdentity == null) throw new ArgumentNullException(nameof(operationIdentity));
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (preservation == null) throw new ArgumentNullException(nameof(preservation));
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			if (!preservation.IsQualified)
				throw new InvalidOperationException("C10.7 cannot replay overrides while the approved preservation plan still contains ActionRequired decisions.");
			if (!ReferenceEquals(preservation.UpdatePlan, updatePlan))
				preservation.ReviewedIntent.ValidateCurrentPlan(updatePlan);

			CollectionRevisionUpdateReviewedIntent reviewed = _reviewCoordinator.LoadReviewedIntent(operationIdentity);
			if (!StringComparer.Ordinal.Equals(reviewed.ReviewFingerprint, preservation.ReviewedIntent.ReviewFingerprint))
				throw new InvalidOperationException("The C10.7 preservation plan does not belong to the exact durable revision-update review.");

			List<CollectionRevisionUpdateOverridePreservationAction> replay = preservation.Actions
				.Where(x => x.RequiresPostCandidateReapply).OrderBy(x => x.UserOverride.OverrideId).ToList();
			ValidateReplayActions(replay);
			CollectionOperation operation = RequireOperation(operationIdentity, updatePlan.NewPlan);
			if (operation.Phase == CollectionOperationPhase.QualifiedRevisionOverridesVerified)
				return new CollectionRevisionUpdateOverrideReapplyResult(operation, 0);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(updatePlan.NewPlan.Target))
				throw new InvalidOperationException("The canonical target changed before C10.7 override reapplication.");

			using (CollectionTargetMutationLease lease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(false))
			{
				cancellationToken.ThrowIfCancellationRequested();
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				operation = RequireOperation(operationIdentity, updatePlan.NewPlan);
				ValidatePersistedCustomization(reviewed, preservation);

				if (operation.Phase == CollectionOperationPhase.CandidateRevisionChildrenVerified)
					operation = SavePhase(operation, CollectionOperationPhase.ReapplyingQualifiedRevisionOverrides,
						CollectionOperationResultState.Pending);
				else if (operation.Phase != CollectionOperationPhase.ReapplyingQualifiedRevisionOverrides)
					throw new InvalidOperationException("C10.7 can run only after candidate children are verified or while replaying qualified overrides.");

				int replayed = 0;
				foreach (CollectionRevisionUpdateOverridePreservationAction action in replay)
				{
					cancellationToken.ThrowIfCancellationRequested();
					bool desiredEnabled;
					if (!CollectionMemberRequirementStates.TryGetEnabled(action.UserOverride.UserChosenState, out desiredEnabled))
						throw new InvalidOperationException("A C10.7 enabled-state action lost its canonical bool-v1 representation.");

					IMod mod = ResolveLiveMember(operation, updatePlan, action.UserOverride.Requirement.MemberKey);
					ModInstallMethod method = _services.ModManager.InstallationLog.GetModInstallMethod(mod);
					if (method != ModInstallMethod.Virtual)
						throw new InvalidOperationException("C10.7 cannot replay a member enabled-state override for a non-Virtual native install.");

					bool currentEnabled = IsEnabled(mod);
					if (currentEnabled == desiredEnabled)
						continue;

					IVirtualModActivator activator = _services.ModManager.VirtualModActivator;
					if (activator == null)
						throw new InvalidOperationException("The current Virtual Mod Activator is unavailable for C10.7 override replay.");
					if (desiredEnabled)
						activator.EnableMod(mod);
					else
						activator.DisableMod(mod);
					replayed++;

					if (IsEnabled(mod) != desiredEnabled)
						throw new InvalidOperationException("The native Virtual activation state did not reach the exact reviewed C9 override value.");
				}

				// Re-establish current authority and verify every replayable decision from durable native state before advancing.
				_authorityValidator.ValidateAndReload(lease, authority, paths);
				operation = RequireOperation(operationIdentity, updatePlan.NewPlan);
				ValidatePersistedCustomization(reviewed, preservation);
				foreach (CollectionRevisionUpdateOverridePreservationAction action in replay)
				{
					bool desiredEnabled;
					if (!CollectionMemberRequirementStates.TryGetEnabled(action.UserOverride.UserChosenState, out desiredEnabled))
						throw new InvalidOperationException("A C10.7 enabled-state action lost its canonical bool-v1 representation.");
					IMod mod = ResolveLiveMember(operation, updatePlan, action.UserOverride.Requirement.MemberKey);
					if (IsEnabled(mod) != desiredEnabled)
						throw new InvalidOperationException("The reviewed C9 member enabled-state override is not satisfied after C10.7 replay.");
				}

				operation = SavePhase(operation, CollectionOperationPhase.QualifiedRevisionOverridesVerified,
					CollectionOperationResultState.Pending);
				return new CollectionRevisionUpdateOverrideReapplyResult(operation, replayed);
			}
		}

		private static void ValidateReplayActions(IEnumerable<CollectionRevisionUpdateOverridePreservationAction> actions)
		{
			foreach (CollectionRevisionUpdateOverridePreservationAction action in actions)
			{
				if (action == null || action.UserOverride == null || action.UserOverride.Requirement == null ||
					action.UserOverride.Requirement.Aspect != CollectionRequirementAspect.MemberEnabledState ||
					action.UserOverride.Requirement.MemberKey == null)
					throw new InvalidOperationException("C10.7 received a post-candidate override action without a characterized typed replay adapter.");
				bool ignored;
				if (!CollectionMemberRequirementStates.TryGetEnabled(action.UserOverride.UserChosenState, out ignored))
					throw new InvalidOperationException("C10.7 can replay only canonical bool-v1 member enabled-state overrides.");
			}
		}

		private void ValidatePersistedCustomization(CollectionRevisionUpdateReviewedIntent reviewed,
			CollectionRevisionUpdateOverridePreservationPlan preservation)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociation(reviewed.AssociationId);
			if (association == null || !association.Revision.Equals(reviewed.OldRevision) || !association.Target.Equals(reviewed.Target) ||
				association.State != reviewed.AssociationState)
				throw new InvalidOperationException("The old Collection association changed before qualified override replay completed.");

			Dictionary<Guid, UserOverride> expected = preservation.Actions.ToDictionary(x => x.UserOverride.OverrideId, x => x.UserOverride);
			Dictionary<Guid, UserOverride> current = _associationStore.GetOverrides(reviewed.AssociationId).ToDictionary(x => x.OverrideId);
			if (!new HashSet<Guid>(expected.Keys).SetEquals(current.Keys))
				throw new InvalidOperationException("The persisted C9 override set changed after revision-update approval.");
			foreach (Guid id in expected.Keys)
				if (!OverrideEquals(expected[id], current[id]))
					throw new InvalidOperationException("A persisted C9 override changed after revision-update approval.");
		}

		private IMod ResolveLiveMember(CollectionOperation operation, CollectionRevisionUpdatePlan updatePlan, CollectionMemberKey memberKey)
		{
			if (memberKey == null) throw new ArgumentNullException(nameof(memberKey));
			CollectionMemberKey candidateMemberKey = updatePlan.MemberCorrelations.ResolveCandidateMemberKey(memberKey);
			CollectionRevisionUpdateMemberPlan member = updatePlan.Members.SingleOrDefault(x => x.MemberKey.Equals(candidateMemberKey));
			if (member == null || member.NewMember == null)
				throw new InvalidOperationException("The C10.7 override target is not present in the candidate revision.");

			CollectionNativeModState native = ResolveCurrentNativeMember(operation, member, updatePlan.NewPlan.Target);
			List<IMod> live = _services.ModManager.InstallationLog.ActiveMods.Where(x => x != null &&
				StringComparer.OrdinalIgnoreCase.Equals(_services.ModManager.InstallationLog.GetModKey(x), native.Identity.NativeModKey)).ToList();
			if (live.Count != 1)
				throw new InvalidOperationException("The C10.7 override target no longer resolves to one live native NMM mod instance.");
			return live[0];
		}

		private CollectionNativeModState ResolveCurrentNativeMember(CollectionOperation operation,
			CollectionRevisionUpdateMemberPlan member, CollectionTargetIdentity target)
		{
			CollectionNativeStateIndex state = new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				_services.ModManager.VirtualModActivator, _services.PluginManager, _services.ModManager.GameMode, _associationStore).Capture(target);
			List<CollectionNativeChildOperation> children = operation.NativeChildren.Where(x =>
				x.Action == CollectionNativeChildAction.ActivateOrReinstall && x.Member.MemberKey.Equals(member.MemberKey) &&
				x.IsReconciled && x.HasVerifiedCommittedNativeState).ToList();
			if (children.Count > 1)
				throw new InvalidOperationException("Multiple committed candidate children exist for one C10.7 member.");
			if (children.Count == 1)
				return ResolveMutatedMember(operation, children[0], state);
			if (member.Binding == null)
				throw new InvalidOperationException("A non-mutated C10.7 member has no reviewed old-revision binding.");
			CollectionNativeModState bound;
			if (!state.Mods.TryGetValue(member.Binding.NativeMod, out bound))
				throw new InvalidOperationException("The preserved C10.7 member binding no longer resolves to installed native state.");
			return bound;
		}

		private CollectionNativeModState ResolveMutatedMember(CollectionOperation operation, CollectionNativeChildOperation child,
			CollectionNativeStateIndex state)
		{
			CollectionNativeChildRecoveryManifest manifest = _manifestStore.GetManifest(operation, child);
			if (manifest == null || manifest.ExecutionEvidence == null)
				throw new InvalidDataException("A committed C10.6 candidate child is missing execution evidence required by C10.7.");
			CollectionNativeChildExecutionEvidence evidence = manifest.ExecutionEvidence;
			IEnumerable<CollectionNativeModState> candidates = state.Mods.Values.Where(x =>
				x.InstallMethod == child.NativeOperation.Fingerprint.InstallMethod && x.InstallRoot == child.NativeOperation.Fingerprint.InstallRoot);
			string domain;
			long modId;
			long fileId;
			if (NexusCollectionModFileArtifactIdentity.TryParse(evidence.SelectedArtifact, out domain, out modId, out fileId))
				candidates = candidates.Where(x => ParseId(x.NexusModId) == modId && ParseId(x.NexusFileId) == fileId);
			else
				candidates = candidates.Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.FileName, evidence.IncomingFileName));
			List<CollectionNativeModState> resolved = candidates.ToList();
			if (resolved.Count != 1)
				throw new InvalidOperationException("The committed C10.6 candidate child no longer resolves to one exact native instance.");
			return resolved[0];
		}

		private bool IsEnabled(IMod mod)
		{
			IVirtualModActivator activator = _services.ModManager.VirtualModActivator;
			if (activator == null) return false;
			string fileName = Path.GetFileName(mod.Filename) ?? String.Empty;
			return activator.ActiveModList.Contains(fileName.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, ResolvedCollectionPlan candidatePlan)
		{
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal ||
				operation.PlanIdentity == null || !operation.PlanIdentity.Equals(candidatePlan.Identity) || operation.Revision == null ||
				!operation.Revision.Equals(candidatePlan.Revision) || !operation.Target.Equals(candidatePlan.Target))
				throw new InvalidOperationException("The C10.7 request does not belong to the active exact revision-update operation.");
			if (operation.Phase != CollectionOperationPhase.CandidateRevisionChildrenVerified &&
				operation.Phase != CollectionOperationPhase.ReapplyingQualifiedRevisionOverrides &&
				operation.Phase != CollectionOperationPhase.QualifiedRevisionOverridesVerified)
				throw new InvalidOperationException("C10.7 requires the verified candidate-child barrier before override replay.");
			return operation;
		}

		private CollectionOperation SavePhase(CollectionOperation operation, CollectionOperationPhase phase,
			CollectionOperationResultState result)
		{
			if (operation.CheckpointSequence == Int64.MaxValue)
				throw new InvalidOperationException("The Collection operation checkpoint sequence cannot be incremented further.");
			var updated = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target,
				operation.Revision, operation.PlanIdentity, operation.CheckpointSequence + 1, phase, result, operation.NativeChildren);
			_operationStore.SaveOperation(updated);
			return _operationStore.GetOperation(operation.Identity);
		}

		private static bool OverrideEquals(UserOverride left, UserOverride right)
		{
			return left != null && right != null && left.OverrideId == right.OverrideId && left.Requirement.Equals(right.Requirement) &&
				left.BaselineState.Equals(right.BaselineState) && left.UserChosenState.Equals(right.UserChosenState) &&
				StringComparer.Ordinal.Equals(left.Note, right.Note);
		}

		private static long ParseId(string value)
		{
			long parsed;
			return Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) ? parsed : -1;
		}
	}
}
