using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies an ordinary native/user mutation whose successful completion can invalidate Collection provenance.
	/// </summary>
	public enum CollectionManualMutationKind
	{
		Unknown = 0,
		Activate = 1,
		Reinstall = 2,
		Upgrade = 3,
		Deactivate = 4,
		Delete = 5,
		VirtualDisable = 6
	}

	/// <summary>
	/// Immutable pre-mutation Collection relationship capture used to invalidate only affected associations after native commit.
	/// </summary>
	public sealed class CollectionManualMutationCapture
	{
		internal CollectionManualMutationCapture(CollectionManualMutationKind kind, CollectionTargetIdentity target,
			IEnumerable<CollectionMemberBinding> directBindings, IEnumerable<Guid> relatedAssociationIds)
		{
			Kind = kind;
			Target = target ?? throw new ArgumentNullException(nameof(target));
			DirectBindings = new ReadOnlyCollection<CollectionMemberBinding>(new List<CollectionMemberBinding>(directBindings ?? throw new ArgumentNullException(nameof(directBindings))));
			RelatedAssociationIds = new ReadOnlyCollection<Guid>(new List<Guid>(relatedAssociationIds ?? throw new ArgumentNullException(nameof(relatedAssociationIds))));
		}

		/// <summary>Gets the ordinary mutation being observed.</summary>
		public CollectionManualMutationKind Kind { get; }

		/// <summary>Gets the exact target owning the captured relationships.</summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>Gets bindings that directly referenced the mutated native mod before mutation.</summary>
		public ReadOnlyCollection<CollectionMemberBinding> DirectBindings { get; }

		/// <summary>Gets other associations sharing one of the affected native owner stacks before mutation.</summary>
		public ReadOnlyCollection<Guid> RelatedAssociationIds { get; }
	}

	/// <summary>
	/// Converts verified ordinary NMM mutations into Collection drift/provenance invalidation without changing native state.
	/// </summary>
	/// <remarks>
	/// Native InstallLog/deployment remains authoritative. This coordinator never blocks or repairs a user mutation and never
	/// fabricates a deliberate <see cref="UserOverride"/>. Exact drift observations are emitted only for member removal/disable,
	/// where the changed requirement can be proven; reinstall/upgrade and file-winner changes conservatively invalidate the
	/// association without inventing an observed recipe/winner identity.
	/// </remarks>
	public sealed class CollectionManualMutationDriftCoordinator
	{
		private const string ParticipationFormat = "participation-v1";
		private const string EnabledFormat = "bool-v1";
		private readonly CollectionsAssociationStore _associationStore;
		private readonly CollectionTargetIdentity _target;

		/// <summary>Creates a coordinator over an existing Collection feature store and exact native target.</summary>
		public CollectionManualMutationDriftCoordinator(CollectionsAssociationStore associationStore, CollectionTargetIdentity target)
		{
			_associationStore = associationStore ?? throw new ArgumentNullException(nameof(associationStore));
			_target = target ?? throw new ArgumentNullException(nameof(target));
		}

		/// <summary>
		/// Creates a drift coordinator for the currently configured game/storage target when Collections tracking already exists.
		/// </summary>
		/// <remarks>A missing Collections database is deliberately not created by an ordinary native action.</remarks>
		internal static CollectionManualMutationDriftCoordinator TryCreateForCurrentTarget(IGameMode gameMode, IEnvironmentInfo environmentInfo)
		{
			if (gameMode == null || environmentInfo == null)
				return null;

			try
			{
				var storageService = new GameStorageService(environmentInfo);
				GameStoragePathSet paths = storageService.FromGameMode(gameMode);
				var store = new CollectionsStore(paths);
				if (!store.Exists)
					return null;

				CollectionTargetAuthority authority = new CollectionTargetIdentityResolver(storageService).Resolve(paths);
				return new CollectionManualMutationDriftCoordinator(new CollectionsAssociationStore(store), authority.Target);
			}
			catch (Exception ex)
			{
				Trace.TraceWarning("Collection drift tracking is unavailable for this manual mutation: {0}", ex.Message);
				return null;
			}
		}

		/// <summary>
		/// Captures bindings and owner-neighbor associations before an ordinary native mod mutation starts.
		/// </summary>
		public CollectionManualMutationCapture BeginNativeModMutation(CollectionManualMutationKind kind,
			string existingNativeModKey, IEnumerable<string> relatedOwnerKeys)
		{
			if (!Enum.IsDefined(typeof(CollectionManualMutationKind), kind) || kind == CollectionManualMutationKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));

			List<CollectionMemberBinding> directBindings = String.IsNullOrEmpty(existingNativeModKey)
				? new List<CollectionMemberBinding>()
				: _associationStore.GetBindingsForNativeMod(new NativeModInstanceIdentity(_target, existingNativeModKey)).ToList();

			var relatedAssociationIds = new HashSet<Guid>(directBindings.Select(x => x.Association.AssociationId));
			AddAssociationsForOwnerKeys(relatedAssociationIds, relatedOwnerKeys);
			return new CollectionManualMutationCapture(kind, _target, directBindings, relatedAssociationIds.OrderBy(x => x).ToArray());
		}

		/// <summary>
		/// Records a successfully committed ordinary native mutation and invalidates only associations that depended on it.
		/// </summary>
		public void RecordCommittedNativeMutation(CollectionManualMutationCapture capture, IEnumerable<string> postMutationOwnerKeys)
		{
			if (capture == null)
				throw new ArgumentNullException(nameof(capture));
			if (!capture.Target.Equals(_target))
				throw new ArgumentException("The manual mutation capture belongs to another Collection target.", nameof(capture));

			var affectedAssociationIds = new HashSet<Guid>(capture.RelatedAssociationIds);
			AddAssociationsForOwnerKeys(affectedAssociationIds, postMutationOwnerKeys);

			var directByAssociation = capture.DirectBindings.GroupBy(x => x.Association.AssociationId)
				.ToDictionary(x => x.Key, x => x.ToList());
			var updatedAssociations = new List<CollectionTargetAssociation>();
			var drift = new List<CollectionDriftObservation>();
			var clearedRequirements = new List<CollectionRequirementReference>();

			foreach (Guid associationId in affectedAssociationIds.OrderBy(x => x))
			{
				CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
				if (association == null || !association.Target.Equals(_target))
					continue;

				List<CollectionMemberBinding> directBindings;
				bool isDirect = directByAssociation.TryGetValue(associationId, out directBindings);
				CollectionAssociationState requestedState = CollectionAssociationState.Modified;
				bool hasMissingRequirement = false;

				if (isDirect && (capture.Kind == CollectionManualMutationKind.Deactivate ||
					capture.Kind == CollectionManualMutationKind.Delete || capture.Kind == CollectionManualMutationKind.VirtualDisable))
				{
					foreach (CollectionMemberBinding binding in directBindings)
					{
						CollectionRequirementAspect aspect = capture.Kind == CollectionManualMutationKind.VirtualDisable
							? CollectionRequirementAspect.MemberEnabledState
							: CollectionRequirementAspect.MemberParticipation;
						CollectionRequirementReference requirement = new CollectionRequirementReference(association,
							binding.MemberKey, aspect, null);
						CollectionRequirementState baseline = aspect == CollectionRequirementAspect.MemberEnabledState
							? CollectionRequirementState.Present(EnabledFormat, "enabled")
							: CollectionRequirementState.Present(ParticipationFormat, "included");
						CollectionRequirementState observed = aspect == CollectionRequirementAspect.MemberEnabledState
							? CollectionRequirementState.Present(EnabledFormat, "disabled")
							: CollectionRequirementState.Absent();
						CollectionRequirementState expected = ResolveExpectedState(requirement, baseline);
						if (expected.Equals(observed))
						{
							clearedRequirements.Add(requirement);
							continue;
						}

						hasMissingRequirement = true;
						drift.Add(new CollectionDriftObservation(Guid.NewGuid(), requirement, expected, observed,
							capture.Kind == CollectionManualMutationKind.VirtualDisable
								? "Ordinary NMM mod disable changed a Collection member's enabled state."
								: "Ordinary NMM mod removal removed a Collection member from the native setup."));
					}
					requestedState = hasMissingRequirement ? CollectionAssociationState.Incomplete : CollectionAssociationState.Modified;
				}

				CollectionAssociationState finalState = MergeAssociationState(association.State, requestedState);
				if (finalState != association.State)
					updatedAssociations.Add(association.WithState(finalState));
			}

			if (updatedAssociations.Count > 0 || drift.Count > 0 || clearedRequirements.Count > 0)
				_associationStore.SaveManualMutationDrift(updatedAssociations, drift, clearedRequirements);
		}

		/// <summary>
		/// Marks affected associations as recovering when a started ordinary native mutation has an ambiguous durable outcome.
		/// </summary>
		public void RecordAmbiguousNativeMutation(CollectionManualMutationCapture capture, IEnumerable<string> postMutationOwnerKeys)
		{
			if (capture == null)
				throw new ArgumentNullException(nameof(capture));
			if (!capture.Target.Equals(_target))
				throw new ArgumentException("The manual mutation capture belongs to another Collection target.", nameof(capture));

			var affectedAssociationIds = new HashSet<Guid>(capture.RelatedAssociationIds);
			AddAssociationsForOwnerKeys(affectedAssociationIds, postMutationOwnerKeys);
			var updates = new List<CollectionTargetAssociation>();
			foreach (Guid associationId in affectedAssociationIds.OrderBy(x => x))
			{
				CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
				if (association == null || !association.Target.Equals(_target) || association.State == CollectionAssociationState.Recovering)
					continue;
				updates.Add(association.WithState(CollectionAssociationState.Recovering));
			}

			if (updates.Count > 0)
				_associationStore.SaveManualMutationDrift(updates, new CollectionDriftObservation[0], new CollectionRequirementReference[0]);
		}

		/// <summary>
		/// Invalidates associations participating in an ordinary File Manager winner change.
		/// </summary>
		public void RecordFileOwnerChange(string previousOwnerKey, string selectedOwnerKey, IEnumerable<string> affectedOwnerKeys)
		{
			if (StringComparer.Ordinal.Equals(previousOwnerKey, selectedOwnerKey))
				return;

			var associationIds = new HashSet<Guid>();
			AddAssociationsForOwnerKeys(associationIds, affectedOwnerKeys);
			AddAssociationsForOwnerKeys(associationIds, new[] { previousOwnerKey, selectedOwnerKey });
			var updates = new List<CollectionTargetAssociation>();
			foreach (Guid associationId in associationIds.OrderBy(x => x))
			{
				CollectionTargetAssociation association = _associationStore.GetAssociation(associationId);
				if (association == null || !association.Target.Equals(_target))
					continue;
				CollectionAssociationState finalState = MergeAssociationState(association.State, CollectionAssociationState.Modified);
				if (finalState != association.State)
					updates.Add(association.WithState(finalState));
			}

			if (updates.Count > 0)
				_associationStore.SaveManualMutationDrift(updates, new CollectionDriftObservation[0], new CollectionRequirementReference[0]);
		}

		private CollectionRequirementState ResolveExpectedState(CollectionRequirementReference requirement,
			CollectionRequirementState baseline)
		{
			foreach (UserOverride userOverride in _associationStore.GetOverrides(requirement.AssociationId))
				if (userOverride.Requirement.Equals(requirement))
					return userOverride.UserChosenState;
			return baseline;
		}

		private void AddAssociationsForOwnerKeys(HashSet<Guid> associationIds, IEnumerable<string> ownerKeys)
		{
			if (ownerKeys == null)
				return;

			foreach (string ownerKey in ownerKeys.Where(x => !String.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal))
			{
				foreach (CollectionMemberBinding binding in _associationStore.GetBindingsForNativeMod(
					new NativeModInstanceIdentity(_target, ownerKey)))
				{
					associationIds.Add(binding.Association.AssociationId);
				}
			}
		}

		private static CollectionAssociationState MergeAssociationState(CollectionAssociationState current,
			CollectionAssociationState requested)
		{
			if (current == CollectionAssociationState.Recovering || current == CollectionAssociationState.Incomplete)
				return current;
			if (requested == CollectionAssociationState.Incomplete)
				return CollectionAssociationState.Incomplete;
			return current == CollectionAssociationState.Applied ? CollectionAssociationState.Modified : current;
		}
	}
}
