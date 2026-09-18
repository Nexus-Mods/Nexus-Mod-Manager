using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one exact normalized collection-member installation recipe.
	/// </summary>
	/// <remarks>
	/// C1.3 treats the fingerprint as opaque. Later manifest normalization/planning owns how it is produced and must
	/// include every supported choice/effect relevant to deciding whether an installed native instance can be reused.
	/// </remarks>
	public sealed class CollectionRecipeIdentity : IEquatable<CollectionRecipeIdentity>
	{
		private CollectionRecipeIdentity(string fingerprint)
		{
			Fingerprint = fingerprint;
		}

		/// <summary>
		/// Gets the exact normalized recipe fingerprint.
		/// </summary>
		public string Fingerprint { get; }

		/// <summary>
		/// Creates an exact recipe identity from a normalized fingerprint.
		/// </summary>
		public static CollectionRecipeIdentity FromFingerprint(string fingerprint)
		{
			return new CollectionRecipeIdentity(
				CollectionIdentityValidation.RequireOpaqueToken(fingerprint, nameof(fingerprint)));
		}

		/// <inheritdoc />
		public bool Equals(CollectionRecipeIdentity other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionRecipeIdentity);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			return StringComparer.Ordinal.GetHashCode(Fingerprint ?? string.Empty);
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Fingerprint;
		}
	}
}
