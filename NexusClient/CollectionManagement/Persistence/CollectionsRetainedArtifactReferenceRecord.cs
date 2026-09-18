using System;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Identifies the durable feature record that currently requires retained immutable bytes.
	/// </summary>
	public enum CollectionsRetainedArtifactOwnerKind
	{
		Unknown = 0,
		Revision = 1,
		Download = 2,
		Operation = 3,
		Capture = 4
	}

	/// <summary>
	/// Describes one durable lifetime reference from a Collections feature owner to a retained immutable artifact.
	/// </summary>
	/// <remarks>
	/// A reference protects content from Collections cleanup only. It does not own native deployment state and does not
	/// require the corresponding mod or effect to remain installed.
	/// </remarks>
	public sealed class CollectionsRetainedArtifactReferenceRecord : IEquatable<CollectionsRetainedArtifactReferenceRecord>
	{
		/// <summary>
		/// Creates a retained-artifact lifetime reference record.
		/// </summary>
		public CollectionsRetainedArtifactReferenceRecord(string referenceId, string artifactId,
			CollectionsRetainedArtifactOwnerKind ownerKind, string ownerId, string role)
		{
			ReferenceId = CollectionIdentityValidation.RequireOpaqueToken(referenceId, nameof(referenceId));
			ArtifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			if (!Enum.IsDefined(typeof(CollectionsRetainedArtifactOwnerKind), ownerKind) ||
				ownerKind == CollectionsRetainedArtifactOwnerKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(ownerKind));

			OwnerKind = ownerKind;
			OwnerId = CollectionsRetainedArtifactReferenceValidation.RequireOwnerId(ownerId, nameof(ownerId));
			Role = CollectionIdentityValidation.RequireOpaqueToken(role, nameof(role));
		}

		/// <summary>
		/// Gets the durable reference-row identity.
		/// </summary>
		public string ReferenceId { get; }

		/// <summary>
		/// Gets the content-addressed retained artifact identity being protected.
		/// </summary>
		public string ArtifactId { get; }

		/// <summary>
		/// Gets the kind of feature record holding this content reference.
		/// </summary>
		public CollectionsRetainedArtifactOwnerKind OwnerKind { get; }

		/// <summary>
		/// Gets the stable identity of the owning feature record within <see cref="OwnerKind"/>.
		/// </summary>
		public string OwnerId { get; }

		/// <summary>
		/// Gets the semantic purpose served by the artifact for this owner.
		/// </summary>
		public string Role { get; }

		/// <summary>
		/// Gets whether this record protects retained content from Collections garbage collection.
		/// </summary>
		public bool ProtectsRetainedContentFromCleanup
		{
			get { return true; }
		}

		/// <summary>
		/// Gets whether this record itself requires native mod/effect deployment to remain active.
		/// </summary>
		public bool AuthorizesNativeDeploymentRetention
		{
			get { return false; }
		}

		/// <inheritdoc />
		public bool Equals(CollectionsRetainedArtifactReferenceRecord other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(ReferenceId, other.ReferenceId) &&
				StringComparer.Ordinal.Equals(ArtifactId, other.ArtifactId) &&
				OwnerKind == other.OwnerKind &&
				StringComparer.Ordinal.Equals(OwnerId, other.OwnerId) &&
				StringComparer.Ordinal.Equals(Role, other.Role);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionsRetainedArtifactReferenceRecord);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = StringComparer.Ordinal.GetHashCode(ReferenceId ?? String.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(ArtifactId ?? String.Empty);
				hashCode = (hashCode * 397) ^ (int)OwnerKind;
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(OwnerId ?? String.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Role ?? String.Empty);
				return hashCode;
			}
		}
	}

	/// <summary>
	/// Validates durable retained-artifact reference identities before they enter feature persistence.
	/// </summary>
	internal static class CollectionsRetainedArtifactReferenceValidation
	{
		/// <summary>
		/// Requires a stable non-HTTP owner identity suitable for long-lived feature-store persistence.
		/// </summary>
		public static string RequireOwnerId(string value, string parameterName)
		{
			string ownerId = CollectionIdentityValidation.RequireOpaqueToken(value, parameterName);
			Uri absoluteUri;
			if (Uri.TryCreate(ownerId, UriKind.Absolute, out absoluteUri) &&
				(absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
				throw new ArgumentException("Temporary HTTP URLs cannot be used as durable retained-artifact owner identities.", parameterName);

			return ownerId;
		}
	}

}
