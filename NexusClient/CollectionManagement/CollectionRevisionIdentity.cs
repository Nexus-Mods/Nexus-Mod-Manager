using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one immutable remote revision or one sealed/local revision of a collection.
	/// </summary>
	/// <remarks>
	/// A Nexus provider revision identifier and its curator-facing revision number are separate values. The provider
	/// identifier is used for exact identity; the revision number is retained for display/comparison and must never be
	/// substituted for the provider identifier. Local revisions use NMM-generated GUID identities and have no Nexus
	/// revision number.
	/// </remarks>
	public sealed class CollectionRevisionIdentity : IEquatable<CollectionRevisionIdentity>
	{
		private CollectionRevisionIdentity(CollectionIdentity collection, string stableRevisionId, long? nexusRevisionNumber)
		{
			Collection = collection;
			StableRevisionId = stableRevisionId;
			NexusRevisionNumber = nexusRevisionNumber;
		}

		/// <summary>
		/// Gets the owning collection lineage.
		/// </summary>
		public CollectionIdentity Collection { get; }

		/// <summary>
		/// Gets the stable revision identity token within the owning collection.
		/// </summary>
		public string StableRevisionId { get; }

		/// <summary>
		/// Gets the Nexus curator-facing revision number, or <c>null</c> for a Local Collection revision.
		/// </summary>
		public long? NexusRevisionNumber { get; }

		/// <summary>
		/// Creates an exact Nexus collection revision identity.
		/// </summary>
		/// <param name="collection">The Nexus collection identity.</param>
		/// <param name="stableRevisionId">The verified stable provider revision identifier.</param>
		/// <param name="revisionNumber">The concrete Nexus revision number resolved for this revision.</param>
		/// <returns>The revision identity.</returns>
		public static CollectionRevisionIdentity FromNexus(CollectionIdentity collection, string stableRevisionId, long revisionNumber)
		{
			if (collection == null)
				throw new ArgumentNullException(nameof(collection));
			if (collection.Origin != CollectionOrigin.NexusMods)
				throw new ArgumentException("A Nexus revision must belong to a Nexus collection identity.", nameof(collection));
			if (revisionNumber <= 0)
				throw new ArgumentOutOfRangeException(nameof(revisionNumber), "A concrete Nexus revision number must be positive.");

			return new CollectionRevisionIdentity(collection,
				CollectionIdentityValidation.RequireOpaqueToken(stableRevisionId, nameof(stableRevisionId)), revisionNumber);
		}

		/// <summary>
		/// Creates an identity for a Local Collection revision.
		/// </summary>
		/// <param name="collection">The Local Collection identity.</param>
		/// <param name="localRevisionId">The persistent local revision identifier.</param>
		/// <returns>The revision identity.</returns>
		public static CollectionRevisionIdentity FromLocal(CollectionIdentity collection, Guid localRevisionId)
		{
			if (collection == null)
				throw new ArgumentNullException(nameof(collection));
			if (collection.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A local revision must belong to a Local Collection identity.", nameof(collection));

			return new CollectionRevisionIdentity(collection,
				CollectionIdentityValidation.RequireGuid(localRevisionId, nameof(localRevisionId)), null);
		}

		/// <inheritdoc />
		public bool Equals(CollectionRevisionIdentity other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(Collection, other.Collection) &&
				StringComparer.Ordinal.Equals(StableRevisionId, other.StableRevisionId) &&
				NexusRevisionNumber == other.NexusRevisionNumber;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionRevisionIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = Collection == null ? 0 : Collection.GetHashCode();
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(StableRevisionId ?? string.Empty);
				hashCode = (hashCode * 397) ^ NexusRevisionNumber.GetHashCode();
				return hashCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (NexusRevisionNumber.HasValue)
				return Collection + "/revision:" + StableRevisionId + "#" + NexusRevisionNumber.Value;

			return Collection + "/revision:" + StableRevisionId;
		}
	}
}
