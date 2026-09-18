using System;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one real game/storage target for collection association purposes.
	/// </summary>
	/// <remarks>
	/// The token is deliberately opaque. Live target identities are produced by <see cref="CollectionTargetIdentityResolver"/>
	/// from a canonical physical game-directory identity plus the authoritative Game Storage identity. Persisted values can
	/// still be rehydrated through <see cref="FromFingerprint"/> without teaching the domain about deployment-relative paths.
	/// </remarks>
	public sealed class CollectionTargetIdentity : IEquatable<CollectionTargetIdentity>
	{
		private const string CanonicalFingerprintPrefix = "target-sha256:";
		private CollectionTargetIdentity(string fingerprint)
		{
			Fingerprint = fingerprint;
		}

		/// <summary>
		/// Gets the canonical opaque target fingerprint supplied by the target-authority layer.
		/// </summary>
		public string Fingerprint { get; }

		/// <summary>
		/// Gets whether this identity uses the canonical live-target fingerprint format.
		/// </summary>
		public bool IsCanonical
		{
			get
			{
				if (Fingerprint == null || !Fingerprint.StartsWith(CanonicalFingerprintPrefix, StringComparison.Ordinal) ||
					Fingerprint.Length != CanonicalFingerprintPrefix.Length + 64)
					return false;

				for (int i = CanonicalFingerprintPrefix.Length; i < Fingerprint.Length; i++)
				{
					char value = Fingerprint[i];
					if (!((value >= '0' && value <= '9') || (value >= 'a' && value <= 'f')))
						return false;
				}

				return true;
			}
		}

		/// <summary>
		/// Rehydrates a target identity from an already persisted fingerprint.
		/// </summary>
		/// <remarks>
		/// New live target identities should be obtained from <see cref="CollectionTargetIdentityResolver"/>. This factory
		/// remains intentionally permissive for persisted state, deterministic fixtures and forward-compatible hydration.
		/// </remarks>
		public static CollectionTargetIdentity FromFingerprint(string fingerprint)
		{
			return new CollectionTargetIdentity(
				CollectionIdentityValidation.RequireOpaqueToken(fingerprint, nameof(fingerprint)));
		}

		/// <summary>
		/// Builds the opaque canonical target fingerprint from physical game and native-storage authority keys.
		/// </summary>
		internal static CollectionTargetIdentity FromCanonicalAuthority(string physicalGameKey, string storageId)
		{
			string physicalKey = CollectionIdentityValidation.RequireOpaqueToken(physicalGameKey, nameof(physicalGameKey));
			string nativeStorageId = CollectionIdentityValidation.RequireOpaqueToken(storageId, nameof(storageId));
			string payload = "nmmce-collection-target-v1\0" + physicalKey.ToUpperInvariant() + "\0" + nativeStorageId.ToUpperInvariant();

			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
				var builder = new StringBuilder(CanonicalFingerprintPrefix.Length + (hash.Length * 2));
				builder.Append(CanonicalFingerprintPrefix);
				foreach (byte value in hash)
					builder.Append(value.ToString("x2"));

				return new CollectionTargetIdentity(builder.ToString());
			}
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
