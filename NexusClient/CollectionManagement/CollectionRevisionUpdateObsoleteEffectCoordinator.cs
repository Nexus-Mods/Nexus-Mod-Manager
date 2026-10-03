using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
	/// <summary>Describes how C10.4 handles obsolete state from one old-revision member.</summary>
	public enum CollectionRevisionUpdateObsoleteMemberDisposition
	{
		RemoveExclusiveNativeMod = 1,
		PreserveSharedNativeMod = 2,
		PreserveForOverride = 3,
		DeferToCandidateReinstall = 4
	}

	/// <summary>One immutable qualified-reversion decision for obsolete old-revision state.</summary>
	public sealed class CollectionRevisionUpdateObsoleteMemberAction
	{
		internal CollectionRevisionUpdateObsoleteMemberAction(CollectionMemberKey memberKey,
			CollectionRevisionUpdateObsoleteMemberDisposition disposition, CollectionMemberBinding binding,
			CollectionNativeModState nativeMod, string reason)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateObsoleteMemberDisposition), disposition))
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			Binding = binding;
			NativeMod = nativeMod;
			Reason = reason ?? String.Empty;
			if (disposition == CollectionRevisionUpdateObsoleteMemberDisposition.RemoveExclusiveNativeMod &&
				(binding == null || nativeMod == null || !binding.NativeMod.Equals(nativeMod.Identity)))
				throw new ArgumentException("A qualified native removal requires the exact old binding and current native instance.");
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionRevisionUpdateObsoleteMemberDisposition Disposition { get; }
		public CollectionMemberBinding Binding { get; }
		public CollectionNativeModState NativeMod { get; }
		public string Reason { get; }
		public bool RequiresNativeRemoval { get { return Disposition == CollectionRevisionUpdateObsoleteMemberDisposition.RemoveExclusiveNativeMod; } }
	}

	/// <summary>Read-only C10.4 qualification of obsolete old-revision state before native reversion.</summary>
	public sealed class CollectionRevisionUpdateObsoleteEffectPlan
	{
		private readonly ReadOnlyCollection<CollectionRevisionUpdateObsoleteMemberAction> _actions;

		internal CollectionRevisionUpdateObsoleteEffectPlan(CollectionRevisionUpdateReviewedIntent reviewedIntent,
			CollectionRevisionUpdatePlan updatePlan, CollectionNativeStateIndex currentState,
			IEnumerable<CollectionRevisionUpdateObsoleteMemberAction> actions)
		{
			ReviewedIntent = reviewedIntent ?? throw new ArgumentNullException(nameof(reviewedIntent));
			UpdatePlan = updatePlan ?? throw new ArgumentNullException(nameof(updatePlan));
			CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
			List<CollectionRevisionUpdateObsoleteMemberAction> copied = (actions ?? throw new ArgumentNullException(nameof(actions))).ToList();
			if (copied.Any(x => x == null) || copied.Select(x => x.MemberKey).Distinct().Count() != copied.Count)
				throw new ArgumentException("A C10.4 obsolete-effect plan cannot contain null or duplicate member actions.", nameof(actions));
			_actions = new ReadOnlyCollection<CollectionRevisionUpdateObsoleteMemberAction>(copied
				.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal).ToList());
		}

		public CollectionRevisionUpdateReviewedIntent ReviewedIntent { get; }
		public CollectionRevisionUpdatePlan UpdatePlan { get; }
		public CollectionNativeStateIndex CurrentState { get; }
		public ReadOnlyCollection<CollectionRevisionUpdateObsoleteMemberAction> Actions { get { return _actions; } }
		public bool RequiresNativeMutation { get { return _actions.Any(x => x.RequiresNativeRemoval); } }
		public ReadOnlyCollection<CollectionMemberKey> DeferredOverrideMembers
		{
			get { return new ReadOnlyCollection<CollectionMemberKey>(_actions.Where(x => x.Disposition == CollectionRevisionUpdateObsoleteMemberDisposition.PreserveForOverride).Select(x => x.MemberKey).ToList()); }
		}
	}

	/// <summary>Verified result of the bounded C10.4 qualified obsolete-member reversion phase.</summary>
	public sealed class CollectionRevisionUpdateObsoleteEffectResult
	{
		private readonly ReadOnlyCollection<string> _removedNativeModKeys;

		internal CollectionRevisionUpdateObsoleteEffectResult(CollectionOperation operation,
			CollectionRevisionUpdateObsoleteEffectPlan plan, CollectionNativeStateIndex finalState,
			IEnumerable<string> removedNativeModKeys)
		{
			Operation = operation ?? throw new ArgumentNullException(nameof(operation));
			Plan = plan ?? throw new ArgumentNullException(nameof(plan));
			FinalState = finalState ?? throw new ArgumentNullException(nameof(finalState));
			_removedNativeModKeys = new ReadOnlyCollection<string>((removedNativeModKeys ?? throw new ArgumentNullException(nameof(removedNativeModKeys)))
				.OrderBy(x => x, StringComparer.Ordinal).ToList());
		}

		public CollectionOperation Operation { get; }
		public CollectionRevisionUpdateObsoleteEffectPlan Plan { get; }
		public CollectionNativeStateIndex FinalState { get; }
		public ReadOnlyCollection<string> RemovedNativeModKeys { get { return _removedNativeModKeys; } }
		public bool IsVerified { get { return Operation.Phase == CollectionOperationPhase.ObsoleteRevisionEffectsVerified && Operation.ResultState == CollectionOperationResultState.Pending; } }
	}

	/// <summary>
	/// C10.4 qualified reversion for obsolete old-revision members. Only a fully removed member whose native instance is still
	/// proven exclusive and dispensable is deactivated. Shared native instances remain installed, explicit C9 customization is
	/// deferred to the later override-preservation slice, and changed members are left for normal candidate reinstall execution.
	/// </summary>
	public sealed class CollectionRevisionUpdateObsoleteEffectCoordinator
	{
		private readonly ServiceManager _services;
		private readonly GameStorageService _gameStorageService;
		private readonly CollectionsOperationStore _operationStore;
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionRevisionUpdateReviewCoordinator _reviewCoordinator;
		private readonly CollectionTargetMutationLeaseManager _mutationLeaseManager;
		private readonly CollectionTargetOwnershipAuthorityValidator _authorityValidator;

		public CollectionRevisionUpdateObsoleteEffectCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionRevisionUpdateReviewCoordinator reviewCoordinator)
			: this(services, gameStorageService, operationStore, associationStore, reviewCoordinator,
				CollectionTargetMutationLeaseManager.Shared, new CollectionTargetOwnershipAuthorityValidator(gameStorageService, services))
		{
		}

		internal CollectionRevisionUpdateObsoleteEffectCoordinator(ServiceManager services, GameStorageService gameStorageService,
			CollectionsOperationStore operationStore, CollectionsAssociationStore associationStore,
			CollectionRevisionUpdateReviewCoordinator reviewCoordinator, CollectionTargetMutationLeaseManager mutationLeaseManager,
			CollectionTargetOwnershipAuthorityValidator authorityValidator)
		{
			_services = services ?? throw new ArgumentNullException(nameof(services));
			_gameStorageService = gameStorageService ?? throw new ArgumentNullException(nameof(gameStorageService));
			_operationStore = operationStore ?? throw new ArgumentNullException(nameof(operationStore));
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_reviewCoordinator = reviewCoordinator ?? throw new ArgumentNullException(nameof(reviewCoordinator));
			_mutationLeaseManager = mutationLeaseManager ?? throw new ArgumentNullException(nameof(mutationLeaseManager));
			_authorityValidator = authorityValidator ?? throw new ArgumentNullException(nameof(authorityValidator));
		}

		/// <summary>Builds the exact qualified old-member reversion plan. This method performs no native or Collection mutation.</summary>
		public CollectionRevisionUpdateObsoleteEffectPlan Plan(CollectionRevisionUpdatePreparationBatch preparedBatch,
			CollectionNativeStateIndex currentState)
		{
			if (preparedBatch == null) throw new ArgumentNullException(nameof(preparedBatch));
			if (!preparedBatch.IsReady)
				throw new InvalidOperationException("C10.4 requires every changed/new candidate input to be prepared before obsolete old state can be removed.");
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.ValidateApprovedPlan(preparedBatch.Operation.Identity,
				preparedBatch.CurrentPlan.NewPlan.Identity, preparedBatch.CurrentPlan);
			ValidateCurrentState(preparedBatch.CurrentPlan, currentState);
			CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner().RequireQualified(intent, preparedBatch.CurrentPlan);
			return BuildQualifiedPlan(_associationStore, intent, preparedBatch.CurrentPlan, currentState, preservation);
		}

		/// <summary>
		/// Executes only qualified whole-native-mod removals for members absent from the candidate revision. The old association
		/// is deliberately retained until later aggregate verification/publication, preventing mixed-revision metadata publication.
		/// </summary>
		public async Task<CollectionRevisionUpdateObsoleteEffectResult> ExecuteAsync(CollectionRevisionUpdatePreparationBatch preparedBatch,
			GameStoragePathSet paths, CancellationToken cancellationToken)
		{
			RequireLiveServices();
			if (preparedBatch == null) throw new ArgumentNullException(nameof(preparedBatch));
			if (!preparedBatch.IsReady)
				throw new InvalidOperationException("C10.4 cannot cross the native boundary before C10.3 has prepared every required candidate member.");
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			cancellationToken.ThrowIfCancellationRequested();

			CollectionOperation initial = RequireOperation(preparedBatch.Operation.Identity, CollectionOperationPhase.ReadyToApply);
			if (initial.PlanIdentity == null || !initial.PlanIdentity.Equals(preparedBatch.CurrentPlan.NewPlan.Identity))
				throw new InvalidOperationException("The C10.4 operation no longer refers to the exact approved candidate plan.");
			CollectionRevisionUpdateReviewedIntent intent = _reviewCoordinator.ValidateApprovedPlan(initial.Identity,
				preparedBatch.CurrentPlan.NewPlan.Identity, preparedBatch.CurrentPlan);

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(_gameStorageService).Resolve(paths);
			if (!authority.Target.Equals(initial.Target))
				throw new InvalidOperationException("The live canonical game/storage target no longer matches the reviewed revision update.");

			using (CollectionTargetMutationLease rootLease = await _mutationLeaseManager.AcquireAsync(authority, cancellationToken).ConfigureAwait(true))
			{
				_authorityValidator.ValidateAndReload(rootLease, authority, paths);
				RequireNoOtherIncompleteOperation(initial);
				CollectionNativeStateIndex currentState = CaptureReloadedState(initial.Target);
				ValidateCurrentState(preparedBatch.CurrentPlan, currentState);
				CollectionRevisionUpdateOverridePreservationPlan preservation = new CollectionRevisionUpdateOverridePreservationPlanner().RequireQualified(intent, preparedBatch.CurrentPlan);
				CollectionRevisionUpdateObsoleteEffectPlan qualified = BuildQualifiedPlan(_associationStore, intent,
					preparedBatch.CurrentPlan, currentState, preservation);
				List<CollectionRevisionUpdateObsoleteMemberAction> removals = qualified.Actions.Where(x => x.RequiresNativeRemoval).ToList();
				CollectionOperation operation = WithOperationState(initial, CollectionOperationPhase.RemovingObsoleteRevisionEffects,
					CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(operation);

				try
				{
					int nextSequence = operation.NativeChildren.Count == 0 ? 1 : operation.NativeChildren.Max(x => x.Sequence) + 1;
					foreach (CollectionRevisionUpdateObsoleteMemberAction removal in removals)
					{
						operation = _operationStore.GetOperation(operation.Identity) ?? operation;
						ThrowIfCancellationRequestedAtChildBoundary(operation, cancellationToken);
						CollectionNativeStateIndex before = CaptureReloadedState(initial.Target);
						CollectionInstalledMemberRemovalPlan liveRemoval = RequireLiveRemovalAuthorization(intent, removal, before, preservation);
						CollectionNativeChildOperation child = CreatePreparedChild(operation, intent.OldRevision, removal, liveRemoval, nextSequence++);
						operation = SaveChild(operation, child);

						IBackgroundTaskSet nativeTask = CreateNativeTask(removal, child, rootLease);
						if (nativeTask == null)
						{
							PauseOrRequireRecovery(operation);
							throw new InvalidOperationException("The qualified obsolete native mod disappeared before its C10.4 deactivation could be submitted.");
						}

						bool accepted;
						try
						{
							accepted = _services.ModActivationMonitor.SubmitWhenIdle(nativeTask, () =>
							{
								cancellationToken.ThrowIfCancellationRequested();
								CollectionOperation latest = RequireOperation(operation.Identity, CollectionOperationPhase.RemovingObsoleteRevisionEffects);
								CollectionNativeChildOperation latestChild = RequireChild(latest, child.Sequence, CollectionNativeChildCheckpoint.RecoveryInputsReady);
								RequireLiveRemovalAuthorization(intent, removal, before, preservation);
								latest = ReplaceChild(latest, new CollectionNativeChildOperation(latestChild.Sequence, latestChild.Member,
									latestChild.Action, latestChild.NativeOperation, CollectionNativeChildCheckpoint.NativeSubmitted, null));
								_operationStore.SaveOperation(latest);
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
							throw new InvalidOperationException("The shared native operation seam rejected the qualified C10.4 deactivation.");
						}

						await WaitForCompletionAsync(nativeTask).ConfigureAwait(true);
						bool exactResult;
						ModOperationResult reported = CaptureReportedResult(nativeTask, child.NativeOperation, out exactResult);
						_authorityValidator.ValidateAndReload(rootLease, authority, paths);
						CollectionNativeStateIndex after = CaptureReloadedState(initial.Target);
						ModOperationDurability durability = DetermineDurability(reported, exactResult,
							IsNativeModFullyAbsent(after, removal.Binding.NativeMod), after.Mods.ContainsKey(removal.Binding.NativeMod));
						operation = _operationStore.GetOperation(operation.Identity) ?? operation;
						CollectionNativeChildOperation submitted = RequireChild(operation, child.Sequence, CollectionNativeChildCheckpoint.NativeSubmitted);
						var terminalResult = new ModOperationResult(child.NativeOperation, reported.ReportedStatus, durability, reported.Message);
						var terminal = new CollectionNativeChildOperation(submitted.Sequence, submitted.Member, submitted.Action,
							submitted.NativeOperation, CollectionNativeChildCheckpoint.NativeTerminalObserved, terminalResult);
						operation = ReplaceChild(operation, terminal);
						_operationStore.SaveOperation(operation);
						var reconciled = new CollectionNativeChildOperation(terminal.Sequence, terminal.Member, terminal.Action,
							terminal.NativeOperation, CollectionNativeChildCheckpoint.Reconciled, terminal.NativeResult);
						operation = ReplaceChild(operation, reconciled);
						_operationStore.SaveOperation(operation);
						if (durability != ModOperationDurability.VerifiedCommitted)
						{
							MarkRecoveryRequired(operation);
							throw new InvalidOperationException("The qualified obsolete-member removal did not verify as durably committed; revision update recovery is required.");
						}
					}

					_authorityValidator.ValidateAndReload(rootLease, authority, paths);
					CollectionNativeStateIndex finalState = CaptureReloadedState(initial.Target);
					ValidateFinalState(qualified, finalState);
					operation = _operationStore.GetOperation(operation.Identity) ?? operation;
					operation = WithOperationState(operation, CollectionOperationPhase.ObsoleteRevisionEffectsVerified,
						CollectionOperationResultState.Pending);
					_operationStore.SaveOperation(operation);
					return new CollectionRevisionUpdateObsoleteEffectResult(operation, qualified, finalState,
						removals.Select(x => x.Binding.NativeMod.NativeModKey));
				}
				catch
				{
					CollectionOperation persisted = _operationStore.GetOperation(operation.Identity) ?? operation;
					if (persisted.Phase == CollectionOperationPhase.RemovingObsoleteRevisionEffects)
						PauseOrRequireRecovery(persisted);
					throw;
				}
			}
		}

		internal static CollectionRevisionUpdateObsoleteEffectPlan BuildQualifiedPlan(CollectionsAssociationStore associationStore,
			CollectionRevisionUpdateReviewedIntent intent, CollectionRevisionUpdatePlan updatePlan, CollectionNativeStateIndex currentState)
		{
			return BuildQualifiedPlan(associationStore, intent, updatePlan, currentState, null);
		}

		internal static CollectionRevisionUpdateObsoleteEffectPlan BuildQualifiedPlan(CollectionsAssociationStore associationStore,
			CollectionRevisionUpdateReviewedIntent intent, CollectionRevisionUpdatePlan updatePlan, CollectionNativeStateIndex currentState,
			CollectionRevisionUpdateOverridePreservationPlan preservation)
		{
			if (associationStore == null) throw new ArgumentNullException(nameof(associationStore));
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			if (currentState == null) throw new ArgumentNullException(nameof(currentState));
			if (!intent.OldRevision.Equals(updatePlan.OldPlan.Revision) || !intent.CandidateRevision.Equals(updatePlan.NewPlan.Revision) ||
				intent.AssociationId != updatePlan.Association.AssociationId || !intent.Target.Equals(currentState.Target))
				throw new InvalidOperationException("C10.4 inputs do not belong to the exact reviewed old/current/new transition.");

			var removalsAuthorizedByReview = new HashSet<CollectionMemberKey>(updatePlan.Members
				.Where(x => x.ChangeKind == CollectionRevisionUpdateChangeKind.Removed &&
					x.Disposition == CollectionRevisionUpdateDisposition.RemoveFromOldRevision)
				.Select(x => x.MemberKey));
			var actions = new List<CollectionRevisionUpdateObsoleteMemberAction>();
			foreach (CollectionRevisionUpdateMemberPlan member in updatePlan.Members.OrderBy(x => x.MemberKey.Kind).ThenBy(x => x.MemberKey.Value, StringComparer.Ordinal))
			{
				if (member.OldMember == null) continue;
				if (member.ChangeKind == CollectionRevisionUpdateChangeKind.Removed)
				{
					if (member.Disposition == CollectionRevisionUpdateDisposition.PreserveOverrideForReview)
					{
						bool removalSatisfiesOverride = preservation != null && preservation.Actions
							.Where(x => x.UserOverride.Requirement.MemberKey != null && x.UserOverride.Requirement.MemberKey.Equals(member.MemberKey))
							.All(x => x.Disposition == CollectionRevisionUpdateOverridePreservationDisposition.SatisfiedByCandidateRemoval) &&
							preservation.Actions.Any(x => x.UserOverride.Requirement.MemberKey != null && x.UserOverride.Requirement.MemberKey.Equals(member.MemberKey));
						if (!removalSatisfiesOverride)
						{
							actions.Add(new CollectionRevisionUpdateObsoleteMemberAction(member.MemberKey,
								CollectionRevisionUpdateObsoleteMemberDisposition.PreserveForOverride, member.Binding,
								ResolveNative(currentState, member.Binding), "The removed old member has an explicit C9 override whose qualified state requires preserving the native instance."));
							continue;
						}
					}
					if ((member.Disposition != CollectionRevisionUpdateDisposition.RemoveFromOldRevision &&
						member.Disposition != CollectionRevisionUpdateDisposition.PreserveOverrideForReview) || member.Binding == null)
						throw new InvalidOperationException("A removed old-revision member is not authorized for C10.4 qualified reversion.");

					CollectionTargetAssociation association = associationStore.GetAssociation(intent.AssociationId);
					if (association == null || !association.Revision.Equals(intent.OldRevision) || !association.Target.Equals(intent.Target))
						throw new InvalidOperationException("The installed association changed before C10.4 qualified reversion.");
					CollectionMemberBinding binding = associationStore.GetBindings(intent.AssociationId).SingleOrDefault(x => x.MemberKey.Equals(member.MemberKey));
					if (binding == null || !binding.NativeMod.Equals(member.Binding.NativeMod) || !binding.VerifiedRecipe.Equals(member.Binding.VerifiedRecipe))
						throw new InvalidOperationException("The old member binding changed before C10.4 qualified reversion.");
					IEnumerable<Guid> satisfiedRemovalOverrides = preservation == null ? Enumerable.Empty<Guid>() : preservation.Actions
						.Where(x => x.Disposition == CollectionRevisionUpdateOverridePreservationDisposition.SatisfiedByCandidateRemoval &&
							x.UserOverride.Requirement.MemberKey != null && x.UserOverride.Requirement.MemberKey.Equals(member.MemberKey))
						.Select(x => x.UserOverride.OverrideId);
					CollectionInstalledMemberRemovalPlan safe = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
						associationStore, association, binding, currentState, satisfiedRemovalOverrides);
					switch (safe.Disposition)
					{
						case CollectionInstalledMemberRemovalDisposition.RemoveNativeMod:
							CollectionNativeModState nativeMod;
							if (!currentState.Mods.TryGetValue(binding.NativeMod, out nativeMod))
								throw new InvalidOperationException("The qualified obsolete member lost its exact current native registration.");
							actions.Add(new CollectionRevisionUpdateObsoleteMemberAction(member.MemberKey,
								CollectionRevisionUpdateObsoleteMemberDisposition.RemoveExclusiveNativeMod, binding, nativeMod, safe.Reason));
							break;
						case CollectionInstalledMemberRemovalDisposition.PreserveOtherMember:
							IReadOnlyList<CollectionMemberBinding> sameNativeBindings = associationStore.GetBindingsForNativeMod(binding.NativeMod);
							List<CollectionMemberBinding> sameAssociationSiblings = sameNativeBindings.Where(x =>
								x.Association.AssociationId == association.AssociationId && !x.MemberKey.Equals(binding.MemberKey)).ToList();
							if (sameAssociationSiblings.Count > 0 && sameAssociationSiblings.All(x => removalsAuthorizedByReview.Contains(x.MemberKey)))
								throw new InvalidOperationException("Several removed old-revision members share one native mod. C10.4 fails closed until their one native deactivation can be durably coalesced instead of treating a disappearing sibling as a surviving dependency.");
							actions.Add(new CollectionRevisionUpdateObsoleteMemberAction(member.MemberKey,
								CollectionRevisionUpdateObsoleteMemberDisposition.PreserveSharedNativeMod, binding,
								ResolveNative(currentState, binding), safe.Reason));
							break;
						case CollectionInstalledMemberRemovalDisposition.PreserveSharedCollection:
							actions.Add(new CollectionRevisionUpdateObsoleteMemberAction(member.MemberKey,
								CollectionRevisionUpdateObsoleteMemberDisposition.PreserveSharedNativeMod, binding,
								ResolveNative(currentState, binding), safe.Reason));
							break;
						case CollectionInstalledMemberRemovalDisposition.PreserveCustomized:
							if (intent.PreservedOverrideIds.Count == 0)
								throw new InvalidOperationException("The old member acquired customization that was not part of the approved C10 review.");
							actions.Add(new CollectionRevisionUpdateObsoleteMemberAction(member.MemberKey,
								CollectionRevisionUpdateObsoleteMemberDisposition.PreserveForOverride, binding,
								ResolveNative(currentState, binding), safe.Reason));
							break;
						default:
							throw new InvalidOperationException("The old member is no longer provably dispensable under the approved C10 review: " + safe.Reason);
					}
					continue;
				}

				bool hasObsoleteEffect = updatePlan.Effects.Any(x => x.MemberKey.Equals(member.MemberKey) &&
					(x.ChangeKind == CollectionRevisionUpdateEffectChangeKind.Removed || x.ChangeKind == CollectionRevisionUpdateEffectChangeKind.Changed));
				if (hasObsoleteEffect && member.NewMember != null && member.ChangeKind != CollectionRevisionUpdateChangeKind.Unchanged)
					actions.Add(new CollectionRevisionUpdateObsoleteMemberAction(member.MemberKey,
						member.Disposition == CollectionRevisionUpdateDisposition.PreserveOverrideForReview
							? CollectionRevisionUpdateObsoleteMemberDisposition.PreserveForOverride
							: CollectionRevisionUpdateObsoleteMemberDisposition.DeferToCandidateReinstall,
						member.Binding, ResolveNative(currentState, member.Binding),
						"Obsolete effects inside a member retained by the candidate revision are reverted only through the later reviewed candidate reinstall/override path."));
			}
			return new CollectionRevisionUpdateObsoleteEffectPlan(intent, updatePlan, currentState, actions);
		}

		private CollectionInstalledMemberRemovalPlan RequireLiveRemovalAuthorization(CollectionRevisionUpdateReviewedIntent intent,
			CollectionRevisionUpdateObsoleteMemberAction removal, CollectionNativeStateIndex currentState,
			CollectionRevisionUpdateOverridePreservationPlan preservation)
		{
			CollectionTargetAssociation association = _associationStore.GetAssociation(intent.AssociationId);
			if (association == null || !association.Revision.Equals(intent.OldRevision) || !association.Target.Equals(intent.Target) ||
				association.State != intent.AssociationState)
				throw new InvalidOperationException("The old Collection association changed after update review; obsolete-member removal is no longer authorized.");
			CollectionMemberBinding binding = _associationStore.GetBindings(intent.AssociationId).SingleOrDefault(x => x.MemberKey.Equals(removal.MemberKey));
			if (binding == null || !binding.NativeMod.Equals(removal.Binding.NativeMod) || !binding.VerifiedRecipe.Equals(removal.Binding.VerifiedRecipe))
				throw new InvalidOperationException("The old member binding changed after update review; obsolete-member removal is no longer authorized.");
			IEnumerable<Guid> satisfiedRemovalOverrides = preservation == null ? Enumerable.Empty<Guid>() : preservation.Actions
				.Where(x => x.Disposition == CollectionRevisionUpdateOverridePreservationDisposition.SatisfiedByCandidateRemoval &&
					x.UserOverride.Requirement.MemberKey != null && x.UserOverride.Requirement.MemberKey.Equals(removal.MemberKey))
				.Select(x => x.UserOverride.OverrideId);
			CollectionInstalledMemberRemovalPlan live = CollectionInstalledMemberRemovalCoordinator.BuildPlanForState(
				_associationStore, association, binding, currentState, satisfiedRemovalOverrides);
			if (live.Disposition != CollectionInstalledMemberRemovalDisposition.RemoveNativeMod)
				throw new InvalidOperationException("The old member gained sharing, customization, provenance protection or incomplete native coverage before removal: " + live.Reason);
			return live;
		}

		private IBackgroundTaskSet CreateNativeTask(CollectionRevisionUpdateObsoleteMemberAction removal,
			CollectionNativeChildOperation child, CollectionTargetMutationLease rootLease)
		{
			IMod nativeMod = ResolveLiveMod(removal.Binding.NativeMod.NativeModKey);
			IBackgroundTaskSet task = _services.ModManager.CreateCollectionDeactivationOperation(nativeMod,
				_services.ModManager.ActiveMods, child.NativeOperation);
			if (task == null) return null;
			ModInstallerBase installerTask = task as ModInstallerBase;
			if (installerTask == null || installerTask.OperationIdentity == null || !Matches(installerTask.OperationIdentity, child.NativeOperation))
				throw new InvalidOperationException("The native uninstaller did not retain the exact C10.4 child identity.");
			installerTask.AssignParentMutationLease(rootLease);
			return task;
		}

		private static CollectionNativeChildOperation CreatePreparedChild(CollectionOperation operation,
			CollectionRevisionIdentity oldRevision, CollectionRevisionUpdateObsoleteMemberAction removal,
			CollectionInstalledMemberRemovalPlan liveRemoval, int sequence)
		{
			if (!liveRemoval.InstallMethod.HasValue || !liveRemoval.InstallRoot.HasValue)
				throw new InvalidOperationException("The qualified obsolete member is missing its installed native context.");
			var identity = ModOperationIdentity.CreateNew(ModOperationOrigin.Collection,
				new ModOperationFingerprint(operation.Target.Fingerprint,
					new ModInstallContext(liveRemoval.InstallMethod.Value, liveRemoval.InstallRoot.Value), null));
			return new CollectionNativeChildOperation(sequence,
				new CollectionOperationMemberReference(oldRevision, removal.MemberKey), CollectionNativeChildAction.Deactivate,
				identity, CollectionNativeChildCheckpoint.RecoveryInputsReady, null);
		}

		private static CollectionNativeModState ResolveNative(CollectionNativeStateIndex state, CollectionMemberBinding binding)
		{
			if (binding == null) return null;
			CollectionNativeModState native;
			return state.Mods.TryGetValue(binding.NativeMod, out native) ? native : null;
		}

		private static void ValidateCurrentState(CollectionRevisionUpdatePlan plan, CollectionNativeStateIndex state)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (state == null) throw new ArgumentNullException(nameof(state));
			if (!plan.NewPlan.Target.Equals(state.Target) || !plan.ObservedStateFingerprint.Equals(state.Fingerprint) ||
				!plan.NewPlan.CurrentStateFingerprint.Equals(state.Fingerprint))
				throw new InvalidOperationException("C10.4 requires the exact authoritative native-state snapshot approved before mutation.");
		}

		private static void ValidateFinalState(CollectionRevisionUpdateObsoleteEffectPlan plan, CollectionNativeStateIndex state)
		{
			foreach (CollectionRevisionUpdateObsoleteMemberAction action in plan.Actions)
			{
				if (action.RequiresNativeRemoval)
				{
					if (!IsNativeModFullyAbsent(state, action.Binding.NativeMod))
						throw new InvalidOperationException("C10.4 completed its child journal but an obsolete exclusive native instance or one of its managed effects remains present.");
				}
				else if (action.Disposition == CollectionRevisionUpdateObsoleteMemberDisposition.PreserveSharedNativeMod &&
					action.Binding != null && !state.Mods.ContainsKey(action.Binding.NativeMod))
				{
					throw new InvalidOperationException("C10.4 unexpectedly removed a native instance that another Collection/member still protects.");
				}
			}
		}

		private CollectionNativeStateIndex CaptureReloadedState(CollectionTargetIdentity target)
		{
			ModManager manager = _services.ModManager;
			return new CollectionNativeStateReader(() => _services.ModManager.InstallationLog,
				manager.VirtualModActivator, _services.PluginManager, manager.GameMode, _associationStore).Capture(target);
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
				throw new InvalidOperationException("The qualified obsolete Collection member cannot be resolved to exactly one live active native mod instance.");
			return matches[0];
		}

		private void RequireNoOtherIncompleteOperation(CollectionOperation current)
		{
			if (_operationStore.GetIncompleteOperations(current.Target).Any(x => x.Identity.OperationId != current.Identity.OperationId))
				throw new InvalidOperationException("Revision update obsolete-effect removal cannot begin while another Collection operation for this target remains incomplete.");
		}

		private CollectionOperation RequireOperation(CollectionOperationIdentity identity, CollectionOperationPhase phase)
		{
			if (identity == null) throw new ArgumentNullException(nameof(identity));
			CollectionOperation operation = _operationStore.GetOperation(identity);
			if (operation == null || operation.Kind != CollectionOperationKind.UpdateRevision || operation.IsTerminal ||
				operation.ResultState != CollectionOperationResultState.Pending || operation.Phase != phase)
				throw new InvalidOperationException("The revision-update operation is not at the required C10.4 journal phase.");
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
			if (operation.Phase == CollectionOperationPhase.RemovingObsoleteRevisionEffects)
			{
				operation = WithOperationState(operation, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending);
				_operationStore.SaveOperation(operation);
			}
			return operation;
		}

		private void ThrowIfCancellationRequestedAtChildBoundary(CollectionOperation operation, CancellationToken cancellationToken)
		{
			if (!cancellationToken.IsCancellationRequested) return;
			PauseOrRequireRecovery(operation);
			cancellationToken.ThrowIfCancellationRequested();
		}

		private static CollectionNativeChildOperation RequireChild(CollectionOperation operation, int sequence,
			CollectionNativeChildCheckpoint checkpoint)
		{
			CollectionNativeChildOperation child = operation.NativeChildren.SingleOrDefault(x => x.Sequence == sequence);
			if (child == null || child.Action != CollectionNativeChildAction.Deactivate || child.Checkpoint != checkpoint)
				throw new InvalidOperationException("The durable C10.4 obsolete-member child is not at the expected native safety checkpoint.");
			return child;
		}

		private static bool IsNativeModFullyAbsent(CollectionNativeStateIndex state, NativeModInstanceIdentity nativeMod)
		{
			string key = nativeMod.NativeModKey;
			return !state.Mods.ContainsKey(nativeMod) && !state.FilesByOwnerKey.ContainsKey(key) &&
				!state.IniEditsByOwnerKey.ContainsKey(key) && !state.GameValuesByOwnerKey.ContainsKey(key);
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

		private static ModOperationDurability DetermineDurability(ModOperationResult reported, bool exactIdentity, bool absent, bool present)
		{
			if (!exactIdentity) return ModOperationDurability.Unknown;
			if (absent) return ModOperationDurability.VerifiedCommitted;
			if (!present) return ModOperationDurability.Unknown;
			if (reported.Durability == ModOperationDurability.NotStarted) return ModOperationDurability.NotStarted;
			if (reported.Durability == ModOperationDurability.VerifiedRolledBack) return ModOperationDurability.VerifiedRolledBack;
			return ModOperationDurability.Unknown;
		}

		private static ModOperationResult CaptureReportedResult(IBackgroundTaskSet task, ModOperationIdentity expected,
			out bool exactIdentity)
		{
			exactIdentity = false;
			ModInstallerBase nativeTask = task as ModInstallerBase;
			if (nativeTask == null || nativeTask.OperationResult == null)
				return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
					"The native revision-update uninstaller reached terminal state without publishing its identified operation result.");
			ModOperationResult result = nativeTask.OperationResult;
			if (!Matches(expected, result.Identity))
				return new ModOperationResult(expected, ModOperationReportedStatus.Failed, ModOperationDurability.Unknown,
					"The native revision-update uninstaller published a terminal result for a different operation attempt.");
			exactIdentity = true;
			return result;
		}

		private void RequireLiveServices()
		{
			if (_services.ModManager == null || _services.ModActivationMonitor == null)
				throw new InvalidOperationException("C10.4 requires the live ModManager and ModActivationMonitor services.");
		}

		private static bool Matches(ModOperationIdentity left, ModOperationIdentity right)
		{
			return left != null && right != null && left.OperationId == right.OperationId && left.AttemptId == right.AttemptId &&
				left.Origin == right.Origin && left.Fingerprint.Equals(right.Fingerprint);
		}
	}
}
