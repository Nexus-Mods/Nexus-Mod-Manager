using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one collection independently from its display name, revisions or installed target associations.
	/// </summary>
	/// <remarks>
	/// Nexus identifiers are deliberately opaque here. The provider adapter decides which verified API field is the
	/// stable collection identifier; the domain layer does not assume a numeric REST route or a Vortex-specific token.
	/// Local collections use NMM-generated GUID identities.
	/// </remarks>
	public sealed class CollectionIdentity : IEquatable<CollectionIdentity>
	{
		private CollectionIdentity(CollectionOrigin origin, string stableId)
		{
			Origin = origin;
			StableId = stableId;
		}

		/// <summary>
		/// Gets the owner of the collection identity.
		/// </summary>
		public CollectionOrigin Origin { get; }

		/// <summary>
		/// Gets the stable identity token within <see cref="Origin"/>.
		/// </summary>
		public string StableId { get; }

		/// <summary>
		/// Creates an identity for a Nexus Mods collection.
		/// </summary>
		/// <param name="stableId">The verified stable collection identifier supplied by the Nexus provider adapter.</param>
		/// <returns>The collection identity.</returns>
		public static CollectionIdentity FromNexus(string stableId)
		{
			return new CollectionIdentity(CollectionOrigin.NexusMods,
				CollectionIdentityValidation.RequireOpaqueToken(stableId, nameof(stableId)));
		}

		/// <summary>
		/// Creates an identity for an NMM-owned Local Collection.
		/// </summary>
		/// <param name="localId">The persistent local collection identifier.</param>
		/// <returns>The collection identity.</returns>
		public static CollectionIdentity FromLocal(Guid localId)
		{
			return new CollectionIdentity(CollectionOrigin.Local,
				CollectionIdentityValidation.RequireGuid(localId, nameof(localId)));
		}

		/// <inheritdoc />
		public bool Equals(CollectionIdentity other)
		{
			return !ReferenceEquals(other, null) &&
				Origin == other.Origin &&
				StringComparer.Ordinal.Equals(StableId, other.StableId);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return ((int)Origin * 397) ^ StringComparer.Ordinal.GetHashCode(StableId ?? string.Empty);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Origin + ":" + StableId;
		}
	}

	/// <summary>
	/// Shared validation for exact collection identity tokens.
	/// </summary>
	internal static class CollectionIdentityValidation
	{
		public static string RequireOpaqueToken(string value, string parameterName)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A stable identity token is required.", parameterName);
			if (!StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("Identity tokens must not contain leading or trailing whitespace.", parameterName);

			return value;
		}

		public static string RequireGuid(Guid value, string parameterName)
		{
			if (value == Guid.Empty)
				throw new ArgumentException("A non-empty identifier is required.", parameterName);

			return value.ToString("D");
		}
	}
}
