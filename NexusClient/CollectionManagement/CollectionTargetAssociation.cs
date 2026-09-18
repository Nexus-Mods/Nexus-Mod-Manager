using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the currently observed applied-state dimension of a collection/target association.
	/// </summary>
	public enum CollectionAssociationState
	{
		/// <summary>
		/// No trusted association state has been established.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The associated revision is verified as applied for the target.
		/// </summary>
		Applied = 1,

		/// <summary>
		/// The target remains associated but deliberate edits or detected drift differ from the reference revision.
		/// </summary>
		Modified = 2,

		/// <summary>
		/// Required effects are missing or the collection operation stopped partially.
		/// </summary>
		Incomplete = 3,

		/// <summary>
		/// Native recovery or collection reconciliation is still required/in progress.
		/// </summary>
		Recovering = 4
	}

	/// <summary>
	/// Associates one concrete collection revision with one real game/storage target.
	/// </summary>
	/// <remarks>
	/// A saved collection with no association is simply not applied. This record therefore represents only an existing
	/// target association; it is not an exclusive "active collection" slot and several compatible associations may
	/// coexist for the same target.
	/// </remarks>
	public sealed class CollectionTargetAssociation : IEquatable<CollectionTargetAssociation>
	{
		/// <summary>
		/// Creates an immutable association snapshot.
		/// </summary>
		public CollectionTargetAssociation(Guid associationId, CollectionRevisionIdentity revision,
			CollectionTargetIdentity target, CollectionAssociationState state)
		{
			if (associationId == Guid.Empty)
				throw new ArgumentException("A non-empty association identifier is required.", nameof(associationId));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (!Enum.IsDefined(typeof(CollectionAssociationState), state) || state == CollectionAssociationState.Unknown)
				throw new ArgumentOutOfRangeException(nameof(state));

			AssociationId = associationId;
			Revision = revision;
			Target = target;
			State = state;
		}

		/// <summary>
		/// Gets the durable identity of this target association.
		/// </summary>
		public Guid AssociationId { get; }

		/// <summary>
		/// Gets the exact remote/local revision associated with the target.
		/// </summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>
		/// Gets the real target associated with the revision.
		/// </summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>
		/// Gets the current applied-state dimension for this association.
		/// </summary>
		public CollectionAssociationState State { get; }

		/// <summary>
		/// Returns a new snapshot for the same association with a different applied state.
		/// </summary>
		public CollectionTargetAssociation WithState(CollectionAssociationState state)
		{
			return new CollectionTargetAssociation(AssociationId, Revision, Target, state);
		}

		/// <inheritdoc />
		public bool Equals(CollectionTargetAssociation other)
		{
			return !ReferenceEquals(other, null) && AssociationId == other.AssociationId;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionTargetAssociation);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return AssociationId.GetHashCode();
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return AssociationId.ToString("D") + ":" + Revision + "@" + Target + "[" + State + "]";
		}
	}
}
