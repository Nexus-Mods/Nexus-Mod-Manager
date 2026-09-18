using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable snapshot of deliberate overrides and independently detected drift for one exact collection association baseline.
	/// </summary>
	/// <remarks>
	/// The two sets are intentionally separate. An override records user intent; a drift observation records reality differing
	/// from the state that should currently hold. This is the old/current-state side of later three-way update/repair planning.
	/// </remarks>
	public sealed class CollectionAssociationCustomization
	{
		private readonly ReadOnlyCollection<UserOverride> _userOverrides;
		private readonly ReadOnlyCollection<CollectionDriftObservation> _driftObservations;

		/// <summary>
		/// Creates a customization/drift snapshot for one association.
		/// </summary>
		public CollectionAssociationCustomization(CollectionTargetAssociation association,
			IEnumerable<UserOverride> userOverrides, IEnumerable<CollectionDriftObservation> driftObservations)
		{
			if (association == null)
				throw new ArgumentNullException(nameof(association));
			if (userOverrides == null)
				throw new ArgumentNullException(nameof(userOverrides));
			if (driftObservations == null)
				throw new ArgumentNullException(nameof(driftObservations));

			List<UserOverride> copiedOverrides = new List<UserOverride>();
			Dictionary<CollectionRequirementReference, UserOverride> overridesByRequirement =
				new Dictionary<CollectionRequirementReference, UserOverride>();
			HashSet<Guid> overrideIds = new HashSet<Guid>();
			foreach (UserOverride userOverride in userOverrides)
			{
				if (userOverride == null)
					throw new ArgumentException("A customization snapshot cannot contain a null user override.", nameof(userOverrides));
				ValidateRequirementAssociation(association, userOverride.Requirement, nameof(userOverrides));
				if (!overrideIds.Add(userOverride.OverrideId))
					throw new ArgumentException("A customization snapshot cannot contain the same override identity more than once.", nameof(userOverrides));
				if (overridesByRequirement.ContainsKey(userOverride.Requirement))
					throw new ArgumentException("Only one active deliberate override may exist for the same exact baseline requirement.", nameof(userOverrides));

				overridesByRequirement.Add(userOverride.Requirement, userOverride);
				copiedOverrides.Add(userOverride);
			}

			List<CollectionDriftObservation> copiedDrift = new List<CollectionDriftObservation>();
			HashSet<CollectionRequirementReference> driftRequirements = new HashSet<CollectionRequirementReference>();
			HashSet<Guid> driftIds = new HashSet<Guid>();
			foreach (CollectionDriftObservation drift in driftObservations)
			{
				if (drift == null)
					throw new ArgumentException("A customization snapshot cannot contain a null drift observation.", nameof(driftObservations));
				ValidateRequirementAssociation(association, drift.Requirement, nameof(driftObservations));
				if (!driftIds.Add(drift.ObservationId))
					throw new ArgumentException("A customization snapshot cannot contain the same drift observation identity more than once.", nameof(driftObservations));
				if (!driftRequirements.Add(drift.Requirement))
					throw new ArgumentException("Only one current drift observation may exist for the same exact requirement.", nameof(driftObservations));

				UserOverride userOverride;
				if (overridesByRequirement.TryGetValue(drift.Requirement, out userOverride) &&
					!userOverride.UserChosenState.Equals(drift.ExpectedState))
					throw new ArgumentException("Drift on a deliberately overridden requirement must be measured from the user's chosen state, not the curator baseline.", nameof(driftObservations));

				copiedDrift.Add(drift);
			}

			AssociationId = association.AssociationId;
			BaselineRevision = association.Revision;
			Target = association.Target;
			_userOverrides = new ReadOnlyCollection<UserOverride>(copiedOverrides);
			_driftObservations = new ReadOnlyCollection<CollectionDriftObservation>(copiedDrift);
		}

		/// <summary>
		/// Gets the durable association identity represented by this snapshot.
		/// </summary>
		public Guid AssociationId { get; }

		/// <summary>
		/// Gets the exact revision baseline against which these local decisions/observations were recorded.
		/// </summary>
		public CollectionRevisionIdentity BaselineRevision { get; }

		/// <summary>
		/// Gets the real target for the association.
		/// </summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>
		/// Gets deliberate local choices that differ from the curator/reference baseline.
		/// </summary>
		public ReadOnlyCollection<UserOverride> UserOverrides
		{
			get { return _userOverrides; }
		}

		/// <summary>
		/// Gets currently detected deviations that have not been established as deliberate user intent.
		/// </summary>
		public ReadOnlyCollection<CollectionDriftObservation> DriftObservations
		{
			get { return _driftObservations; }
		}

		/// <summary>
		/// Gets whether the association has at least one deliberate local decision.
		/// </summary>
		public bool HasUserOverrides
		{
			get { return _userOverrides.Count > 0; }
		}

		/// <summary>
		/// Gets whether native reality currently differs from the expected baseline/override state.
		/// </summary>
		public bool HasDetectedDrift
		{
			get { return _driftObservations.Count > 0; }
		}

		/// <summary>
		/// Gets whether any local difference must be included in update/repair review.
		/// </summary>
		public bool RequiresReview
		{
			get { return HasUserOverrides || HasDetectedDrift; }
		}

		private static void ValidateRequirementAssociation(CollectionTargetAssociation association,
			CollectionRequirementReference requirement, string parameterName)
		{
			if (requirement.AssociationId != association.AssociationId ||
				!Equals(requirement.BaselineRevision, association.Revision) ||
				!Equals(requirement.Target, association.Target))
				throw new ArgumentException("Every override/drift requirement must belong to the exact association revision and target represented by the snapshot.", parameterName);
		}
	}
}
