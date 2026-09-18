using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the exact raw manifest bytes and normalizer contract that produced a normalized collection manifest.
	/// </summary>
	/// <remarks>
	/// The source bytes themselves are retained by later feature storage. This value object deliberately stores no path,
	/// stream or signed URL; it binds normalized data to immutable raw content by digest, size, schema and normalizer version.
	/// </remarks>
	public sealed class CollectionManifestSourceSnapshot : IEquatable<CollectionManifestSourceSnapshot>
	{
		/// <summary>
		/// Creates an exact manifest-source snapshot.
		/// </summary>
		public CollectionManifestSourceSnapshot(CollectionContentHash contentHash, long byteLength, string schemaIdentity, string normalizerVersion)
		{
			if (contentHash == null)
				throw new ArgumentNullException(nameof(contentHash));
			if (byteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength), "Manifest byte length cannot be negative.");

			ContentHash = contentHash;
			ByteLength = byteLength;
			SchemaIdentity = CollectionIdentityValidation.RequireOpaqueToken(schemaIdentity, nameof(schemaIdentity));
			NormalizerVersion = CollectionIdentityValidation.RequireOpaqueToken(normalizerVersion, nameof(normalizerVersion));
		}

		/// <summary>
		/// Gets the cryptographic identity of the exact raw bytes presented to the normalizer.
		/// </summary>
		public CollectionContentHash ContentHash { get; }

		/// <summary>
		/// Gets the exact raw byte length associated with <see cref="ContentHash"/>.
		/// </summary>
		public long ByteLength { get; }

		/// <summary>
		/// Gets the validated schema/container identity understood by the normalizer.
		/// </summary>
		public string SchemaIdentity { get; }

		/// <summary>
		/// Gets the normalizer implementation/version identity used to produce the normalized model.
		/// </summary>
		public string NormalizerVersion { get; }

		/// <inheritdoc />
		public bool Equals(CollectionManifestSourceSnapshot other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(ContentHash, other.ContentHash) &&
				ByteLength == other.ByteLength &&
				StringComparer.Ordinal.Equals(SchemaIdentity, other.SchemaIdentity) &&
				StringComparer.Ordinal.Equals(NormalizerVersion, other.NormalizerVersion);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionManifestSourceSnapshot);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = ContentHash.GetHashCode();
				hashCode = (hashCode * 397) ^ ByteLength.GetHashCode();
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(SchemaIdentity ?? string.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(NormalizerVersion ?? string.Empty);
				return hashCode;
			}
		}
	}
}
