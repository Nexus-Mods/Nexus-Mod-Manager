using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Defines the NMM-owned Nexus Collections metadata operations consumed by the Collections feature.
	/// </summary>
	/// <remarks>
	/// This contract keeps provider metadata, bundle authorization/final ephemeral locations, manifest normalization,
	/// and native installation as separate boundaries.
	/// </remarks>
	public interface INexusCollectionsProvider
	{
		/// <summary>
		/// Looks up collection-level provider metadata while preserving GraphQL partial data and errors.
		/// </summary>
		/// <param name="collectionSlug">The public Nexus collection slug used only as a lookup locator.</param>
		/// <param name="cancellationToken">Cancels the lookup without changing Nexus session state.</param>
		/// <returns>The provider metadata and any field-level GraphQL errors returned with it.</returns>
		Task<NexusCollectionSummaryLookupResult> GetSummaryAsync(
			string collectionSlug,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Resolves one collection revision selector to provider metadata while preserving GraphQL partial data and errors.
		/// </summary>
		/// <param name="request">The collection slug and latest/concrete revision selector.</param>
		/// <param name="cancellationToken">Cancels the lookup without changing Nexus session state.</param>
		/// <returns>The provider metadata and any field-level GraphQL errors returned with it.</returns>
		Task<NexusCollectionRevisionLookupResult> GetRevisionAsync(
			NexusCollectionRevisionRequest request,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Resolves the provider-owned authorization endpoint for one concrete revision into ephemeral download locations.
		/// </summary>
		/// <param name="revision">Concrete provider revision metadata returned by <see cref="GetRevisionAsync"/>.</param>
		/// <param name="cancellationToken">Cancels authorization without changing Nexus session state.</param>
		/// <returns>Ephemeral final download locations which are not stable artifact identity.</returns>
		Task<NexusCollectionBundleResolutionResult> ResolveBundleAsync(
			NexusCollectionRevisionMetadata revision,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
