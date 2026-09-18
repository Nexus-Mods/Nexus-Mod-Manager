using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Records a verified difference between the expected collection/local-override state and observed native reality.
	/// </summary>
	/// <remarks>
	/// Detection alone never establishes user intent. Drift is therefore deliberately separate from <see cref="UserOverride"/>
	/// and cannot be silently converted into an override or automatically repaired.
	/// </remarks>
	public sealed class CollectionDriftObservation : IEquatable<CollectionDriftObservation>
	{
		/// <summary>
		/// Creates an immutable verified drift observation.
		/// </summary>
		public CollectionDriftObservation(Guid observationId, CollectionRequirementReference requirement,
			CollectionRequirementState expectedState, CollectionRequirementState observedState, string detail)
		{
			if (observationId == Guid.Empty)
				throw new ArgumentException("A non-empty drift observation identifier is required.", nameof(observationId));
			if (requirement == null)
				throw new ArgumentNullException(nameof(requirement));
			if (expectedState == null)
				throw new ArgumentNullException(nameof(expectedState));
			if (observedState == null)
				throw new ArgumentNullException(nameof(observedState));
			if (expectedState.Equals(observedState))
				throw new ArgumentException("A drift observation must differ from the expected state.", nameof(observedState));

			ObservationId = observationId;
			Requirement = requirement;
			ExpectedState = expectedState;
			ObservedState = observedState;
			Detail = CollectionDomainValidation.OptionalDisplayValue(detail, nameof(detail));
		}

		/// <summary>
		/// Gets the durable identity of this drift observation.
		/// </summary>
		public Guid ObservationId { get; }

		/// <summary>
		/// Gets the exact requirement whose reality differed.
		/// </summary>
		public CollectionRequirementReference Requirement { get; }

		/// <summary>
		/// Gets the state that was expected immediately before verification.
		/// </summary>
		/// <remarks>
		/// When a deliberate <see cref="UserOverride"/> exists for the same requirement this is the user's chosen state,
		/// not the curator baseline. The aggregate contract validates that relationship.
		/// </remarks>
		public CollectionRequirementState ExpectedState { get; }

		/// <summary>
		/// Gets the state actually observed from native reality.
		/// </summary>
		public CollectionRequirementState ObservedState { get; }

		/// <summary>
		/// Gets optional diagnostic context supplied by the verifier.
		/// </summary>
		public string Detail { get; }

		/// <summary>
		/// Gets whether this observation itself proves a deliberate user decision.
		/// </summary>
		public bool IsUserIntent
		{
			get { return false; }
		}

		/// <summary>
		/// Gets that a repair must be previewed/reconciled rather than silently applied.
		/// </summary>
		public bool RequiresRepairPreview
		{
			get { return true; }
		}

		/// <inheritdoc />
		public bool Equals(CollectionDriftObservation other)
		{
			return !ReferenceEquals(other, null) && ObservationId == other.ObservationId;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionDriftObservation);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return ObservationId.GetHashCode();
		}
	}
}
