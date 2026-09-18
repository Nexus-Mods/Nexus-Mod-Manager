using System;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Describes one sealed immutable blob owned by the Collections retained-content store.
	/// </summary>
	/// <remarks>
	/// The stable artifact identity and cryptographic digest identify bytes, not a deployed native owner or a temporary path.
	/// Reference/lifetime ownership is added separately by the retained-artifact reference layer.
	/// </remarks>
	public sealed class CollectionsRetainedArtifact : IEquatable<CollectionsRetainedArtifact>
	{
		/// <summary>
		/// Creates a sealed retained-artifact descriptor.
		/// </summary>
		public CollectionsRetainedArtifact(string artifactId, CollectionContentHash contentHash, long byteLength)
		{
			ArtifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			ContentHash = contentHash ?? throw new ArgumentNullException(nameof(contentHash));
			if (byteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength), "Retained artifact length cannot be negative.");

			ByteLength = byteLength;
		}

		/// <summary>
		/// Gets the stable Collections-owned artifact identity.
		/// </summary>
		public string ArtifactId { get; }

		/// <summary>
		/// Gets the exact cryptographic digest of the immutable bytes.
		/// </summary>
		public CollectionContentHash ContentHash { get; }

		/// <summary>
		/// Gets the exact byte length of the immutable bytes.
		/// </summary>
		public long ByteLength { get; }

		/// <inheritdoc />
		public bool Equals(CollectionsRetainedArtifact other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(ArtifactId, other.ArtifactId) &&
				Equals(ContentHash, other.ContentHash) &&
				ByteLength == other.ByteLength;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionsRetainedArtifact);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = StringComparer.Ordinal.GetHashCode(ArtifactId ?? string.Empty);
				hashCode = (hashCode * 397) ^ (ContentHash == null ? 0 : ContentHash.GetHashCode());
				hashCode = (hashCode * 397) ^ ByteLength.GetHashCode();
				return hashCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return ArtifactId;
		}
	}
}
