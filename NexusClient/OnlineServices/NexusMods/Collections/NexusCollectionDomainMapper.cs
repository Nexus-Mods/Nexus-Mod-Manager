using System;
using System.Globalization;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Converts verified Nexus Collections provider metadata into the provider-neutral C1 domain models.
	/// </summary>
	/// <remarks>
	/// Provider numeric identifiers are converted to invariant opaque identity tokens here, at the provider adapter
	/// boundary. Slugs, names, download paths and curator-facing revision numbers are never substituted for those IDs.
	/// </remarks>
	public static class NexusCollectionDomainMapper
	{
		/// <summary>
		/// Creates the minimal immutable Collection definition available from concrete revision metadata.
		/// </summary>
		/// <remarks>
		/// Revision lookup already carries stable collection identity. This overload allows read-only preview/import to
		/// continue when decorative collection-summary fields are unavailable or their separate lookup fails.
		/// </remarks>
		/// <returns>The minimal definition, or <c>null</c> when stable collection identity was not returned.</returns>
		public static CollectionDefinition ToDefinition(NexusCollectionRevisionMetadata metadata)
		{
			if (metadata == null)
				throw new ArgumentNullException(nameof(metadata));
			if (!metadata.CollectionId.HasValue || metadata.CollectionId.Value <= 0)
				return null;

			return new CollectionDefinition(
				CollectionIdentity.FromNexus(ToProviderToken(metadata.CollectionId.Value)),
				null,
				null,
				null);
		}

		/// <summary>
		/// Maps collection-level provider metadata to an immutable Collection definition.
		/// </summary>
		/// <returns>
		/// The mapped definition, or <c>null</c> when the partial GraphQL response did not contain stable collection identity.
		/// </returns>
		public static CollectionDefinition ToDefinition(NexusCollectionSummaryMetadata metadata)
		{
			if (metadata == null)
				throw new ArgumentNullException(nameof(metadata));
			if (!metadata.HasStableIdentity)
				return null;

			return new CollectionDefinition(
				CollectionIdentity.FromNexus(ToProviderToken(metadata.CollectionId.Value)),
				metadata.Name,
				metadata.AuthorDisplayName,
				metadata.Summary);
		}

		/// <summary>
		/// Maps one exact provider revision under an already-mapped collection definition.
		/// </summary>
		/// <returns>
		/// The mapped immutable revision, or <c>null</c> when the partial GraphQL response did not contain stable revision identity.
		/// </returns>
		public static CollectionRevision ToRevision(CollectionDefinition definition, NexusCollectionRevisionMetadata metadata)
		{
			if (definition == null)
				throw new ArgumentNullException(nameof(definition));
			if (metadata == null)
				throw new ArgumentNullException(nameof(metadata));
			if (!metadata.HasStableIdentity)
				return null;
			if (definition.Origin != CollectionOrigin.NexusMods)
				throw new ArgumentException("Nexus provider revision metadata must map under a Nexus collection definition.", nameof(definition));

			CollectionIdentity providerCollection = CollectionIdentity.FromNexus(ToProviderToken(metadata.CollectionId.Value));
			if (!providerCollection.Equals(definition.Identity))
				throw new ArgumentException("The Nexus collection revision belongs to a different stable collection identity.", nameof(metadata));

			int? declaredMemberCount = null;
			if (metadata.ModCount.HasValue)
			{
				if (metadata.ModCount.Value < 0 || metadata.ModCount.Value > int.MaxValue)
					throw new ArgumentOutOfRangeException(nameof(metadata), "The Nexus provider member count is outside the supported domain range.");

				declaredMemberCount = (int)metadata.ModCount.Value;
			}

			CollectionRevisionIdentity identity = CollectionRevisionIdentity.FromNexus(
				definition.Identity,
				ToProviderToken(metadata.RevisionId.Value),
				metadata.RevisionNumber.Value);

			return new CollectionRevision(identity, null, null, declaredMemberCount);
		}

		private static string ToProviderToken(long providerId)
		{
			return providerId.ToString(CultureInfo.InvariantCulture);
		}
	}
}
