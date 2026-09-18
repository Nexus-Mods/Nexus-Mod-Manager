using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records one deliberate user decision to keep a collection requirement different from its reference baseline.
	/// </summary>
	/// <remarks>
	/// An override records intent; it is not detected drift and must never be silently repaired. Later update/repair planning
	/// compares the old baseline, this explicit chosen state and the incoming/new baseline before proposing a transition.
	/// </remarks>
	public sealed class UserOverride : IEquatable<UserOverride>
	{
		/// <summary>
		/// Creates an immutable deliberate override record.
		/// </summary>
		public UserOverride(Guid overrideId, CollectionRequirementReference requirement,
			CollectionRequirementState baselineState, CollectionRequirementState userChosenState, string note)
		{
			if (overrideId == Guid.Empty)
				throw new ArgumentException("A non-empty override identifier is required.", nameof(overrideId));
			if (requirement == null)
				throw new ArgumentNullException(nameof(requirement));
			if (baselineState == null)
				throw new ArgumentNullException(nameof(baselineState));
			if (userChosenState == null)
				throw new ArgumentNullException(nameof(userChosenState));
			if (baselineState.Equals(userChosenState))
				throw new ArgumentException("A user override must actually differ from the collection baseline.", nameof(userChosenState));

			OverrideId = overrideId;
			Requirement = requirement;
			BaselineState = baselineState;
			UserChosenState = userChosenState;
			Note = CollectionDomainValidation.OptionalDisplayValue(note, nameof(note));
		}

		/// <summary>
		/// Gets the durable identity of this deliberate override decision.
		/// </summary>
		public Guid OverrideId { get; }

		/// <summary>
		/// Gets the exact baseline requirement being overridden.
		/// </summary>
		public CollectionRequirementReference Requirement { get; }

		/// <summary>
		/// Gets the old/current collection baseline state against which the decision was made.
		/// </summary>
		public CollectionRequirementState BaselineState { get; }

		/// <summary>
		/// Gets the explicit local state the user chose to preserve/use instead.
		/// </summary>
		public CollectionRequirementState UserChosenState { get; }

		/// <summary>
		/// Gets optional user-facing context for the deliberate decision.
		/// </summary>
		public string Note { get; }

		/// <summary>
		/// Gets that later repair/update logic must ask how to handle this override rather than silently restoring baseline.
		/// </summary>
		public bool RequiresExplicitRepairDecision
		{
			get { return true; }
		}

		/// <inheritdoc />
		public bool Equals(UserOverride other)
		{
			return !ReferenceEquals(other, null) && OverrideId == other.OverrideId;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as UserOverride);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return OverrideId.GetHashCode();
		}
	}
}
