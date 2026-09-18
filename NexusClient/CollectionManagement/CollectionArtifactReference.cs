using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the exact source artifact requested by a normalized collection member without storing an ephemeral download URL.
	/// </summary>
	/// <remarks>
	/// <see cref="Scheme"/> and <see cref="StableId"/> are canonical values emitted by a validated normalizer/provider adapter.
	/// This type intentionally does not interpret raw source labels or imply that a source is automatically downloadable.
	/// Authorization and final artifact URLs remain separate acquisition concerns.
	/// </remarks>
	public sealed class CollectionArtifactReference : IEquatable<CollectionArtifactReference>
	{
		/// <summary>
		/// Creates a normalized artifact reference.
		/// </summary>
		/// <param name="scheme">Canonical source/artifact scheme understood by the normalizer.</param>
		/// <param name="stableId">Stable artifact identity within the scheme.</param>
		/// <param name="expectedContentHash">Optional provider/manifest digest that acquired bytes must satisfy.</param>
		public CollectionArtifactReference(string scheme, string stableId, CollectionContentHash expectedContentHash)
		{
			Scheme = CollectionIdentityValidation.RequireOpaqueToken(scheme, nameof(scheme));
			StableId = CollectionIdentityValidation.RequireOpaqueToken(stableId, nameof(stableId));

			Uri resolvedUrl;
			if (Uri.TryCreate(StableId, UriKind.Absolute, out resolvedUrl) &&
				(resolvedUrl.Scheme == Uri.UriSchemeHttp || resolvedUrl.Scheme == Uri.UriSchemeHttps))
				throw new ArgumentException("An ephemeral HTTP download URL cannot be used as stable artifact identity.", nameof(stableId));

			ExpectedContentHash = expectedContentHash;
		}

		/// <summary>
		/// Gets the canonical normalized source/artifact scheme.
		/// </summary>
		public string Scheme { get; }

		/// <summary>
		/// Gets the stable artifact identity within <see cref="Scheme"/>.
		/// </summary>
		public string StableId { get; }

		/// <summary>
		/// Gets an optional expected digest supplied by the trusted normalized manifest.
		/// </summary>
		public CollectionContentHash ExpectedContentHash { get; }

		/// <inheritdoc />
		public bool Equals(CollectionArtifactReference other)
		{
			return !ReferenceEquals(other, null) &&
				StringComparer.Ordinal.Equals(Scheme, other.Scheme) &&
				StringComparer.Ordinal.Equals(StableId, other.StableId) &&
				Equals(ExpectedContentHash, other.ExpectedContentHash);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionArtifactReference);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = StringComparer.Ordinal.GetHashCode(Scheme ?? string.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(StableId ?? string.Empty);
				hashCode = (hashCode * 397) ^ (ExpectedContentHash == null ? 0 : ExpectedContentHash.GetHashCode());
				return hashCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Scheme + ":" + StableId;
		}
	}
}
