using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// References one verified immutable artifact retained by Collections-owned storage.
	/// </summary>
	/// <remarks>
	/// Holding this reference protects retained content from feature cleanup according to its owning record's lifetime.
	/// It is not a native deployment owner and does not require the referenced mod/effect to remain installed.
	/// Paths and temporary download URLs are deliberately not part of durable retained-content identity.
	/// </remarks>
	public sealed class RetainedArtifactReference : IEquatable<RetainedArtifactReference>
	{
		/// <summary>
		/// Creates a retained immutable-content reference.
		/// </summary>
		public RetainedArtifactReference(string stableArtifactId, string role, CollectionContentHash contentHash, long byteLength)
		{
			StableArtifactId = CollectionIdentityValidation.RequireOpaqueToken(stableArtifactId, nameof(stableArtifactId));
			Role = CollectionIdentityValidation.RequireOpaqueToken(role, nameof(role));
			if (contentHash == null)
				throw new ArgumentNullException(nameof(contentHash));
			if (byteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength), "Retained artifact length cannot be negative.");

			Uri absoluteUri;
			if (Uri.TryCreate(StableArtifactId, UriKind.Absolute, out absoluteUri) &&
				(absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
				throw new ArgumentException("A temporary HTTP download URL cannot be retained as local artifact identity.", nameof(stableArtifactId));

			ContentHash = contentHash;
			ByteLength = byteLength;
		}

		/// <summary>
		/// Gets the stable Collections-owned immutable-object identity.
		/// </summary>
		public string StableArtifactId { get; }

		/// <summary>
		/// Gets the canonical semantic role this artifact serves in the capture/recovery record.
		/// </summary>
		public string Role { get; }

		/// <summary>
		/// Gets the verified content digest of the immutable retained bytes.
		/// </summary>
		public CollectionContentHash ContentHash { get; }

		/// <summary>
		/// Gets the verified byte length of the immutable retained object.
		/// </summary>
		public long ByteLength { get; }

		/// <summary>
		/// Gets whether the owning retained reference protects the object from Collections garbage collection.
		/// </summary>
		public bool ProtectsRetainedContentFromCleanup
		{
			get { return true; }
		}

		/// <summary>
		/// Gets whether this reference by itself requires a corresponding native mod/effect to remain deployed.
		/// </summary>
		public bool AuthorizesNativeDeploymentRetention
		{
			get { return false; }
		}

		/// <inheritdoc />
		public bool Equals(RetainedArtifactReference other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(StableArtifactId, other.StableArtifactId) &&
				StringComparer.Ordinal.Equals(Role, other.Role) &&
				Equals(ContentHash, other.ContentHash) &&
				ByteLength == other.ByteLength;
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as RetainedArtifactReference);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = StringComparer.Ordinal.GetHashCode(StableArtifactId ?? string.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Role ?? string.Empty);
				hashCode = (hashCode * 397) ^ (ContentHash == null ? 0 : ContentHash.GetHashCode());
				hashCode = (hashCode * 397) ^ ByteLength.GetHashCode();
				return hashCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Role + ":" + StableArtifactId;
		}
	}
}
