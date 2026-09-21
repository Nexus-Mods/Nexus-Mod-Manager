using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes one Collection member whose verified association pins an ordinary native mod instance to an exact recipe baseline.
	/// </summary>
	/// <remarks>
	/// A pin is advisory provenance, not a native lock. Ordinary NMM actions remain allowed after explicit user review; later
	/// mutation tracking records the resulting difference as drift until the user deliberately adopts it as an override.
	/// </remarks>
	public sealed class CollectionMemberPinImpact
	{
		private readonly ReadOnlyCollection<UserOverride> _memberOverrides;
		private readonly ReadOnlyCollection<CollectionDriftObservation> _memberDrift;

		internal CollectionMemberPinImpact(CollectionMemberBinding binding, IEnumerable<UserOverride> memberOverrides,
			IEnumerable<CollectionDriftObservation> memberDrift)
		{
			Binding = binding ?? throw new ArgumentNullException(nameof(binding));
			List<UserOverride> copiedOverrides = new List<UserOverride>(memberOverrides ?? throw new ArgumentNullException(nameof(memberOverrides)));
			if (copiedOverrides.Any(x => x == null || x.Requirement.MemberKey == null || !x.Requirement.MemberKey.Equals(binding.MemberKey) ||
				x.Requirement.AssociationId != binding.Association.AssociationId))
				throw new ArgumentException("Every member pin override must belong to the exact bound Collection member.", nameof(memberOverrides));

			List<CollectionDriftObservation> copiedDrift = new List<CollectionDriftObservation>(memberDrift ?? throw new ArgumentNullException(nameof(memberDrift)));
			if (copiedDrift.Any(x => x == null || x.Requirement.MemberKey == null || !x.Requirement.MemberKey.Equals(binding.MemberKey) ||
				x.Requirement.AssociationId != binding.Association.AssociationId))
				throw new ArgumentException("Every member pin drift observation must belong to the exact bound Collection member.", nameof(memberDrift));

			_memberOverrides = new ReadOnlyCollection<UserOverride>(copiedOverrides);
			_memberDrift = new ReadOnlyCollection<CollectionDriftObservation>(copiedDrift);
		}

		/// <summary>Gets the exact persisted member/native provenance that establishes this pin.</summary>
		public CollectionMemberBinding Binding { get; }

		/// <summary>Gets the exact associated revision whose member recipe is pinned.</summary>
		public CollectionRevisionIdentity Revision { get { return Binding.Association.Revision; } }

		/// <summary>Gets the exact verified recipe identity currently associated with the native member.</summary>
		public CollectionRecipeIdentity VerifiedRecipe { get { return Binding.VerifiedRecipe; } }

		/// <summary>Gets deliberate member-scoped overrides already recorded for this association member.</summary>
		public ReadOnlyCollection<UserOverride> MemberOverrides { get { return _memberOverrides; } }

		/// <summary>Gets current member-scoped detected drift that has not been established as user intent.</summary>
		public ReadOnlyCollection<CollectionDriftObservation> MemberDrift { get { return _memberDrift; } }

		/// <summary>Gets whether the member already carries at least one explicit local decision.</summary>
		public bool HasExplicitLocalDecision { get { return _memberOverrides.Count > 0; } }

		/// <summary>Gets whether native reality currently contains detected member-scoped drift for this pin.</summary>
		public bool HasDetectedDrift { get { return _memberDrift.Count > 0; } }

		/// <summary>Gets that callers should review the Collection impact rather than silently replacing the pinned recipe.</summary>
		public bool RequiresExplicitMutationDecision { get { return true; } }
	}

	/// <summary>
	/// Result of atomically recording one explicit Collection user decision against current observed state.
	/// </summary>
	public sealed class CollectionOverrideDecisionResult
	{
		internal CollectionOverrideDecisionResult(CollectionTargetAssociation association, UserOverride userOverride,
			CollectionDriftObservation drift, CollectionAssociationCustomization customization)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			UserOverride = userOverride;
			Drift = drift;
			Customization = customization ?? throw new ArgumentNullException(nameof(customization));
		}

		/// <summary>Gets the persisted association after the decision.</summary>
		public CollectionTargetAssociation Association { get; }

		/// <summary>Gets the active deliberate override, or <c>null</c> when the user chose the curator baseline.</summary>
		public UserOverride UserOverride { get; }

		/// <summary>Gets remaining drift when native reality does not yet match the explicit decision, otherwise <c>null</c>.</summary>
		public CollectionDriftObservation Drift { get; }

		/// <summary>Gets the complete current customization snapshot after the decision.</summary>
		public CollectionAssociationCustomization Customization { get; }

		/// <summary>Gets whether a deliberate non-baseline choice remains active.</summary>
		public bool HasOverride { get { return UserOverride != null; } }

		/// <summary>Gets whether native reality still differs from the state the user explicitly chose.</summary>
		public bool HasDrift { get { return Drift != null; } }
	}

	/// <summary>
	/// Implements C6.12 pin discovery and explicit user-override decisions without mutating native state.
	/// </summary>
	/// <remarks>
	/// Collection bindings/revisions are the pin baseline; they do not prevent ordinary NMM actions. A deliberate local choice
	/// is persisted separately as <see cref="UserOverride"/> and never authorizes hidden repair. Detected drift remains separate
	/// until the user explicitly adopts it. Clearing an override also never repairs native state: if reality still differs from
	/// the curator baseline the difference is immediately retained as drift.
	/// </remarks>
	public sealed class CollectionPinOverrideCoordinator
	{
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionTargetIdentity _target;

		/// <summary>Creates a C6.12 coordinator for one exact Collection/native target.</summary>
		public CollectionPinOverrideCoordinator(CollectionsAssociationStore associationStore, CollectionTargetIdentity target)
		{
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_target = target ?? throw new ArgumentNullException(nameof(target));
		}

		/// <summary>
		/// Creates a coordinator for the current target only when Collections tracking already exists.
		/// </summary>
		/// <remarks>
		/// A missing feature store is a normal no-pin result. Existing-store/target failures are deliberately propagated so
		/// callers cannot mistake unavailable pin state for an unassociated mod.
		/// </remarks>
		internal static CollectionPinOverrideCoordinator CreateForCurrentTargetIfTracked(IGameMode gameMode, IEnvironmentInfo environmentInfo)
		{
			if (gameMode == null) throw new ArgumentNullException(nameof(gameMode));
			if (environmentInfo == null) throw new ArgumentNullException(nameof(environmentInfo));

			var storageService = new GameStorageService(environmentInfo);
			GameStoragePathSet paths = storageService.FromGameMode(gameMode);
			var store = new CollectionsStore(paths);
			if (!store.Exists)
				return null;

			CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(storageService).Resolve(paths);
			return new CollectionPinOverrideCoordinator(new CollectionsAssociationStore(store), authority.Target);
		}

		/// <summary>
		/// Returns every Collection member association pinned to the supplied target-scoped native mod instance.
		/// </summary>
		public IReadOnlyList<CollectionMemberPinImpact> GetMemberPins(string nativeModKey)
		{
			return GetMemberPins(new NativeModInstanceIdentity(_target, nativeModKey));
		}

		/// <summary>
		/// Returns every Collection member association pinned to the supplied target-scoped native mod instance.
		/// </summary>
		public IReadOnlyList<CollectionMemberPinImpact> GetMemberPins(NativeModInstanceIdentity nativeMod)
		{
			if (nativeMod == null)
				throw new ArgumentNullException(nameof(nativeMod));
			if (!nativeMod.Target.Equals(_target))
				throw new ArgumentException("The native mod belongs to another Collection target.", nameof(nativeMod));

			var result = new List<CollectionMemberPinImpact>();
			foreach (CollectionMemberBinding binding in _associationStore.GetBindingsForNativeMod(nativeMod)
				.OrderBy(x => x.Association.AssociationId).ThenBy(x => x.MemberKey.ToString(), StringComparer.Ordinal))
			{
				UserOverride[] memberOverrides = _associationStore.GetOverrides(binding.Association.AssociationId)
					.Where(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(binding.MemberKey))
					.OrderBy(x => x.OverrideId).ToArray();
				CollectionDriftObservation[] memberDrift = _associationStore.GetDriftObservations(binding.Association.AssociationId)
					.Where(x => x.Requirement.MemberKey != null && x.Requirement.MemberKey.Equals(binding.MemberKey))
					.OrderBy(x => x.ObservationId).ToArray();
				result.Add(new CollectionMemberPinImpact(binding, memberOverrides, memberDrift));
			}
			return new ReadOnlyCollection<CollectionMemberPinImpact>(result);
		}

		/// <summary>Loads the complete explicit-override/detected-drift snapshot for one association.</summary>
		public CollectionAssociationCustomization GetCustomization(Guid associationId)
		{
			CollectionTargetAssociation association = RequireAssociation(associationId);
			return new CollectionAssociationCustomization(association,
				_associationStore.GetOverrides(associationId), _associationStore.GetDriftObservations(associationId));
		}

		/// <summary>
		/// Records an explicit user decision and the currently observed native state without changing native reality.
		/// </summary>
		/// <remarks>
		/// Choosing the curator baseline removes the active override. If the observed state does not match the chosen state,
		/// a drift observation is retained against the chosen expectation rather than silently applying either state.
		/// </remarks>
		public CollectionOverrideDecisionResult RecordDecision(CollectionRequirementReference requirement,
			CollectionRequirementState baselineState, CollectionRequirementState chosenState,
			CollectionRequirementState observedState, string note)
		{
			if (requirement == null) throw new ArgumentNullException(nameof(requirement));
			if (baselineState == null) throw new ArgumentNullException(nameof(baselineState));
			if (chosenState == null) throw new ArgumentNullException(nameof(chosenState));
			if (observedState == null) throw new ArgumentNullException(nameof(observedState));

			CollectionAssociationCustomization snapshot = GetCustomization(requirement.AssociationId);
			ValidateRequirement(snapshot, requirement);
			UserOverride existingOverride = snapshot.UserOverrides.SingleOrDefault(x => x.Requirement.Equals(requirement));
			CollectionDriftObservation existingDrift = snapshot.DriftObservations.SingleOrDefault(x => x.Requirement.Equals(requirement));
			if (existingOverride != null && !existingOverride.BaselineState.Equals(baselineState))
				throw new InvalidOperationException("The explicit Collection decision uses a different baseline than the existing override.");

			return ApplyDecision(snapshot, requirement, baselineState, chosenState, observedState, note,
				existingOverride, existingDrift);
		}

		/// <summary>
		/// Explicitly adopts one current drift observation as the user's local decision.
		/// </summary>
		/// <remarks>The exact observation identity is compare-and-swapped so stale UI state cannot overwrite newer drift.</remarks>
		public CollectionOverrideDecisionResult AcceptCurrentDrift(Guid associationId, Guid observationId, string note)
		{
			if (observationId == Guid.Empty)
				throw new ArgumentException("A non-empty drift observation identifier is required.", nameof(observationId));
			CollectionAssociationCustomization snapshot = GetCustomization(associationId);
			CollectionDriftObservation drift = snapshot.DriftObservations.SingleOrDefault(x => x.ObservationId == observationId);
			if (drift == null)
				throw new InvalidOperationException("The requested Collection drift observation is no longer current.");
			UserOverride existingOverride = snapshot.UserOverrides.SingleOrDefault(x => x.Requirement.Equals(drift.Requirement));
			CollectionRequirementState baseline = existingOverride == null ? drift.ExpectedState : existingOverride.BaselineState;
			if (existingOverride != null && !drift.ExpectedState.Equals(existingOverride.UserChosenState))
				throw new InvalidOperationException("The current drift observation is inconsistent with the active explicit override.");

			return ApplyDecision(snapshot, drift.Requirement, baseline, drift.ObservedState, drift.ObservedState, note,
				existingOverride, drift);
		}

		/// <summary>
		/// Clears one deliberate override without changing native state, retaining drift if reality still differs from baseline.
		/// </summary>
		public CollectionOverrideDecisionResult ClearOverride(Guid associationId, Guid overrideId,
			CollectionRequirementState observedState)
		{
			if (overrideId == Guid.Empty)
				throw new ArgumentException("A non-empty override identifier is required.", nameof(overrideId));
			if (observedState == null)
				throw new ArgumentNullException(nameof(observedState));

			CollectionAssociationCustomization snapshot = GetCustomization(associationId);
			UserOverride existingOverride = snapshot.UserOverrides.SingleOrDefault(x => x.OverrideId == overrideId);
			if (existingOverride == null)
				throw new InvalidOperationException("The requested Collection user override is no longer current.");
			CollectionDriftObservation existingDrift = snapshot.DriftObservations
				.SingleOrDefault(x => x.Requirement.Equals(existingOverride.Requirement));
			return ApplyDecision(snapshot, existingOverride.Requirement, existingOverride.BaselineState,
				existingOverride.BaselineState, observedState, null, existingOverride, existingDrift);
		}

		private CollectionOverrideDecisionResult ApplyDecision(CollectionAssociationCustomization snapshot,
			CollectionRequirementReference requirement, CollectionRequirementState baselineState,
			CollectionRequirementState chosenState, CollectionRequirementState observedState, string note,
			UserOverride existingOverride, CollectionDriftObservation existingDrift)
		{
			if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
			if (snapshot.AssociationId != requirement.AssociationId)
				throw new ArgumentException("The customization snapshot does not own the supplied requirement.", nameof(requirement));

			CollectionTargetAssociation association = _associationStore.GetAssociation(snapshot.AssociationId);
			if (association == null || !association.Target.Equals(_target) || !association.Revision.Equals(snapshot.BaselineRevision))
				throw new InvalidOperationException("The Collection association changed while recording the user decision.");
			if (association.State == CollectionAssociationState.Recovering)
				throw new InvalidOperationException("A recovering Collection association must be reconciled before user overrides can be changed.");

			UserOverride userOverride = null;
			if (!chosenState.Equals(baselineState))
			{
				Guid overrideId = existingOverride == null ? Guid.NewGuid() : existingOverride.OverrideId;
				userOverride = new UserOverride(overrideId, requirement, baselineState, chosenState, note);
			}
			else if (note != null)
				throw new ArgumentException("An override note cannot be stored when the user chooses the Collection baseline.", nameof(note));

			CollectionDriftObservation drift = null;
			if (!observedState.Equals(chosenState))
			{
				drift = new CollectionDriftObservation(Guid.NewGuid(), requirement, chosenState, observedState,
					"Observed native state does not match the explicit Collection user decision.");
			}

			_associationStore.SaveUserOverrideDecision(requirement, baselineState, userOverride, drift,
				existingOverride == null ? Guid.Empty : existingOverride.OverrideId,
				existingDrift == null ? Guid.Empty : existingDrift.ObservationId);

			CollectionAssociationCustomization persisted = GetCustomization(requirement.AssociationId);
			UserOverride persistedOverride = persisted.UserOverrides.SingleOrDefault(x => x.Requirement.Equals(requirement));
			CollectionDriftObservation persistedDrift = persisted.DriftObservations.SingleOrDefault(x => x.Requirement.Equals(requirement));
			return new CollectionOverrideDecisionResult(_associationStore.GetAssociation(requirement.AssociationId),
				persistedOverride, persistedDrift, persisted);
		}

		private CollectionTargetAssociation RequireAssociation(Guid associationId)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty Collection association identifier is required.", nameof(associationId));
			CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
			if (association == null)
				throw new InvalidOperationException("The requested Collection association does not exist.");
			if (!association.Target.Equals(_target))
				throw new InvalidOperationException("The requested Collection association belongs to another native target.");
			return association;
		}

		private static void ValidateRequirement(CollectionAssociationCustomization snapshot,
			CollectionRequirementReference requirement)
		{
			if (snapshot.AssociationId != requirement.AssociationId ||
				!snapshot.BaselineRevision.Equals(requirement.BaselineRevision) || !snapshot.Target.Equals(requirement.Target))
				throw new InvalidOperationException("The explicit Collection decision does not belong to the current association baseline.");
		}
	}
}
