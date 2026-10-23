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
			: this(scheme, stableId, expectedContentHash, null)
		{
		}

		/// <summary>Retains an optional manifest size constraint without changing legacy provider identity or serialized formats.</summary>
		internal CollectionArtifactReference(string scheme, string stableId, CollectionContentHash expectedContentHash, long? expectedByteLength)
			: this(scheme, stableId, expectedContentHash, expectedByteLength, null)
		{
		}

		/// <summary>Retains exact manifest MD5/size evidence while preserving persisted provider identity.</summary>
		internal CollectionArtifactReference(string scheme, string stableId, CollectionContentHash expectedContentHash,
			long? expectedByteLength, string expectedMd5)
		{
			if (expectedByteLength.HasValue && expectedByteLength.Value < 0)
				throw new ArgumentOutOfRangeException(nameof(expectedByteLength));
			string normalizedMd5 = null;
			if (expectedMd5 != null && (!expectedByteLength.HasValue || expectedByteLength.Value <= 0 ||
				!CollectionExternalArtifactIdentity.TryNormalizeMd5(expectedMd5, out normalizedMd5)))
				throw new ArgumentException("Manifest MD5 evidence requires a valid digest and a positive byte length.", nameof(expectedMd5));
			Scheme = CollectionIdentityValidation.RequireOpaqueToken(scheme, nameof(scheme));
			StableId = CollectionIdentityValidation.RequireOpaqueToken(stableId, nameof(stableId));

			Uri resolvedUrl;
			if (Uri.TryCreate(StableId, UriKind.Absolute, out resolvedUrl) &&
				(resolvedUrl.Scheme == Uri.UriSchemeHttp || resolvedUrl.Scheme == Uri.UriSchemeHttps))
				throw new ArgumentException("An ephemeral HTTP download URL cannot be used as stable artifact identity.", nameof(stableId));

			ExpectedContentHash = expectedContentHash;
			ExpectedByteLength = expectedByteLength;
			ExpectedMd5 = normalizedMd5;
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

		/// <summary>Gets optional source metadata reconstructed from the retained manifest, separate from stable artifact identity.</summary>
		internal long? ExpectedByteLength { get; }

		/// <summary>Gets exact Nexus archive content evidence reconstructed from the retained manifest, when supplied.</summary>
		internal string ExpectedMd5 { get; }

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
