using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one real game/storage target for collection association purposes.
	/// </summary>
	/// <remarks>
	/// The token is deliberately opaque. The later target-authority layer is responsible for producing a canonical
	/// physical-target plus native-storage identity and for rejecting unsafe overlapping NMM contexts. This value type
	/// only prevents collection-domain code from falling back to folder names or deployment-relative paths as identity.
	/// </remarks>
	public sealed class CollectionTargetIdentity : IEquatable<CollectionTargetIdentity>
	{
		private CollectionTargetIdentity(string fingerprint)
		{
			Fingerprint = fingerprint;
		}

		/// <summary>
		/// Gets the canonical opaque target fingerprint supplied by the target-authority layer.
		/// </summary>
		public string Fingerprint { get; }

		/// <summary>
		/// Creates a target identity from a canonical target fingerprint.
		/// </summary>
		public static CollectionTargetIdentity FromFingerprint(string fingerprint)
		{
			return new CollectionTargetIdentity(
				CollectionIdentityValidation.RequireOpaqueToken(fingerprint, nameof(fingerprint)));
		}

		/// <inheritdoc />
		public bool Equals(CollectionTargetIdentity other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionTargetIdentity);
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
