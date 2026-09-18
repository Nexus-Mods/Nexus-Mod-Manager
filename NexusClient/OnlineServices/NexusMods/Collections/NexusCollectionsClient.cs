using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Implements Nexus Collections metadata lookups and bundle authorization over the shared provider/session foundation.
	/// </summary>
	internal sealed class NexusCollectionsClient : INexusCollectionsProvider
	{
		private const string SummaryOperationName = "NmmCollectionSummary";
		private const string LatestOperationName = "NmmCollectionRevisionLatest";
		private const string ConcreteOperationName = "NmmCollectionRevisionByNumber";

		private const string CollectionSummaryQuery = @"query NmmCollectionSummary($slug: String!) {
  collection(slug: $slug) {
    id
    slug
    name
    summary
    user {
      name
    }
  }
}";

		private const string LatestRevisionQuery = @"query NmmCollectionRevisionLatest($slug: String!) {
  collectionRevision(slug: $slug) {
    id
    collectionId
    revisionNumber
    modCount
    downloadLink
  }
}";

		private const string ConcreteRevisionQuery = @"query NmmCollectionRevisionByNumber($slug: String!, $revision: Int!) {
  collectionRevision(slug: $slug, revision: $revision) {
    id
    collectionId
    revisionNumber
    modCount
    downloadLink
  }
}";

		private readonly NexusModsService _service;

		/// <summary>
		/// Initializes collection operations over the provider-scoped Nexus service.
		/// </summary>
		public NexusCollectionsClient(NexusModsService service)
		{
			_service = service ?? throw new ArgumentNullException(nameof(service));
		}

		/// <inheritdoc />
		public async Task<NexusCollectionSummaryLookupResult> GetSummaryAsync(
			string collectionSlug,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			string validatedSlug = NexusCollectionRevisionRequest.RequireSlug(collectionSlug);
			NexusSessionContext session = _service.CaptureSession();
			using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token))
			{
				NexusGraphQlResponse<NexusCollectionSummaryData> response = await _service.GraphQl.ExecuteAsync<NexusCollectionSummaryData>(
					CollectionSummaryQuery,
					new NexusCollectionSummaryVariables(validatedSlug),
					SummaryOperationName,
					linkedCancellation.Token).ConfigureAwait(false);

				EnsureSessionIsCurrent(session, "collection summary lookup");

				NexusCollectionSummaryNode node = response == null || response.Data == null
					? null
					: response.Data.Collection;
				NexusCollectionSummaryMetadata metadata = node == null
					? null
					: new NexusCollectionSummaryMetadata(
						node.Id,
						node.Slug,
						node.Name,
						node.User == null ? null : node.User.Name,
						node.Summary);

				return new NexusCollectionSummaryLookupResult(
					validatedSlug,
					session.Generation,
					metadata,
					response == null ? null : response.Errors);
			}
		}

		/// <inheritdoc />
		public async Task<NexusCollectionRevisionLookupResult> GetRevisionAsync(
			NexusCollectionRevisionRequest request,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (request == null)
				throw new ArgumentNullException(nameof(request));

			NexusSessionContext session = _service.CaptureSession();
			using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token))
			{
				NexusGraphQlResponse<NexusCollectionRevisionData> response;
				if (request.IsLatest)
				{
					response = await _service.GraphQl.ExecuteAsync<NexusCollectionRevisionData>(
						LatestRevisionQuery,
						new NexusCollectionRevisionVariables(request.CollectionSlug, null),
						LatestOperationName,
						linkedCancellation.Token).ConfigureAwait(false);
				}
				else
				{
					response = await _service.GraphQl.ExecuteAsync<NexusCollectionRevisionData>(
						ConcreteRevisionQuery,
						new NexusCollectionRevisionVariables(request.CollectionSlug, request.RevisionNumber),
						ConcreteOperationName,
						linkedCancellation.Token).ConfigureAwait(false);
				}

				EnsureSessionIsCurrent(session, "collection revision lookup");

				NexusCollectionRevisionNode node = response == null || response.Data == null
					? null
					: response.Data.CollectionRevision;
				NexusCollectionRevisionMetadata metadata = node == null
					? null
					: new NexusCollectionRevisionMetadata(
						node.CollectionId,
						node.Id,
						node.RevisionNumber,
						node.ModCount,
						node.DownloadLink);

				return new NexusCollectionRevisionLookupResult(
					request,
					session.Generation,
					metadata,
					response == null ? null : response.Errors);
			}
		}

		/// <inheritdoc />
		public async Task<NexusCollectionBundleResolutionResult> ResolveBundleAsync(
			NexusCollectionRevisionMetadata revision,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (!revision.HasStableIdentity)
				throw new ArgumentException("A concrete Nexus collection/revision identity is required before bundle authorization.", nameof(revision));

			Uri resolutionUri = BuildBundleResolutionUri(revision);
			NexusSessionContext session = _service.CaptureSession();
			using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token))
			{
				ApiResponse response = await _service.SendAsync(
					NexusApiSurface.Collections,
					HttpMethod.Get,
					resolutionUri,
					cancellationToken: linkedCancellation.Token).ConfigureAwait(false);

				EnsureSessionIsCurrent(session, "collection bundle authorization");

				NexusCollectionBundleResolutionWire wire;
				try
				{
					wire = ApiJson.Deserialize<NexusCollectionBundleResolutionWire>(response.Content);
				}
				catch (JsonException ex)
				{
					throw new ApiException(ApiErrorKind.InvalidResponse, "Nexus Mods returned an invalid collection bundle authorization response.", response.StatusCode, innerException: ex);
				}

				NexusCollectionBundleDownloadLocation[] locations;
				try
				{
					locations = NormalizeDownloadLocations(wire);
				}
				catch (ArgumentException ex)
				{
					throw new ApiException(ApiErrorKind.InvalidResponse, "Nexus Mods returned an unsafe collection bundle download location.", response.StatusCode, innerException: ex);
				}

				if (locations.Length == 0)
					throw new ApiException(ApiErrorKind.InvalidResponse, "Nexus Mods returned no collection bundle download locations.", response.StatusCode);

				return new NexusCollectionBundleResolutionResult(revision, session.Generation, locations);
			}
		}

		/// <summary>
		/// Converts the provider-supplied authorization path to the one approved Nexus API route for this exact revision.
		/// </summary>
		private Uri BuildBundleResolutionUri(NexusCollectionRevisionMetadata revision)
		{
			if (string.IsNullOrWhiteSpace(revision.BundleResolutionPath))
				throw new InvalidOperationException("The Nexus collection revision does not expose a bundle authorization path.");

			Uri uri;
			if (Uri.TryCreate(revision.BundleResolutionPath, UriKind.Absolute, out Uri absolute))
			{
				uri = absolute;
			}
			else
			{
				if (!revision.BundleResolutionPath.StartsWith("/", StringComparison.Ordinal))
					throw new InvalidOperationException("The Nexus collection bundle authorization path must be rooted at the approved API origin.");

				uri = new Uri(new Uri("https://" + NexusRequestPolicy.ApiHost, UriKind.Absolute), revision.BundleResolutionPath);
			}

			if (!_service.RequestPolicy.IsApprovedApiDestination(uri))
				throw new InvalidOperationException("The Nexus collection bundle authorization path is not on the approved Nexus API origin.");
			if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
				throw new InvalidOperationException("The Nexus collection bundle authorization path must not contain query or fragment data.");

			string expectedPath = string.Format(
				CultureInfo.InvariantCulture,
				"/v2/collections/{0}/revisions/{1}/download_link",
				revision.CollectionId.Value,
				revision.RevisionId.Value);
			if (!StringComparer.Ordinal.Equals(uri.AbsolutePath, expectedPath))
				throw new InvalidOperationException("The Nexus collection bundle authorization path does not match the resolved collection/revision identity.");

			return uri;
		}

		private static NexusCollectionBundleDownloadLocation[] NormalizeDownloadLocations(NexusCollectionBundleResolutionWire wire)
		{
			if (wire == null)
				return new NexusCollectionBundleDownloadLocation[0];

			IEnumerable<NexusCollectionBundleLocationWire> source;
			if (wire.DownloadLinks != null)
				source = wire.DownloadLinks;
			else if (wire.DownloadLink != null)
				source = new[] { wire.DownloadLink };
			else
				return new NexusCollectionBundleDownloadLocation[0];

			return source.Select(location =>
			{
				if (location == null || string.IsNullOrWhiteSpace(location.Uri))
					throw new ArgumentException("A collection bundle download location is missing its URI.");

				if (!System.Uri.TryCreate(location.Uri, UriKind.Absolute, out Uri parsedUri))
					throw new ArgumentException("A collection bundle download location contains an invalid URI.");

				return new NexusCollectionBundleDownloadLocation(parsedUri, location.Name, location.ShortName);
			}).ToArray();
		}

		private void EnsureSessionIsCurrent(NexusSessionContext session, string operationName)
		{
			NexusSessionContext currentSession = _service.CaptureSession();
			if (currentSession.Generation != session.Generation)
				throw new ApiException(ApiErrorKind.Cancelled, "The Nexus Mods session changed before the " + operationName + " completed.");
		}

		private sealed class NexusCollectionBundleResolutionWire
		{
			[JsonProperty("download_links")]
			public NexusCollectionBundleLocationWire[] DownloadLinks { get; set; }

			[JsonProperty("download_link")]
			public NexusCollectionBundleLocationWire DownloadLink { get; set; }
		}

		private sealed class NexusCollectionBundleLocationWire
		{
			[JsonProperty("URI")]
			public string Uri { get; set; }

			[JsonProperty("name")]
			public string Name { get; set; }

			[JsonProperty("short_name")]
			public string ShortName { get; set; }
		}

		/// <summary>
		/// Contains the typed GraphQL data field returned by collection.
		/// </summary>
		private sealed class NexusCollectionSummaryData
		{
			[JsonProperty("collection")]
			public NexusCollectionSummaryNode Collection { get; set; }
		}

		/// <summary>
		/// Contains only collection-level fields used by the C2 summary/domain adapter.
		/// </summary>
		private sealed class NexusCollectionSummaryNode
		{
			[JsonProperty("id")]
			public long? Id { get; set; }

			[JsonProperty("slug")]
			public string Slug { get; set; }

			[JsonProperty("name")]
			public string Name { get; set; }

			[JsonProperty("summary")]
			public string Summary { get; set; }

			[JsonProperty("user")]
			public NexusCollectionAuthorNode User { get; set; }
		}

		private sealed class NexusCollectionAuthorNode
		{
			[JsonProperty("name")]
			public string Name { get; set; }
		}

		/// <summary>
		/// Contains the typed GraphQL data field returned by collectionRevision.
		/// </summary>
		private sealed class NexusCollectionRevisionData
		{
			[JsonProperty("collectionRevision")]
			public NexusCollectionRevisionNode CollectionRevision { get; set; }
		}

		/// <summary>
		/// Contains only provider fields required by the C2 revision metadata contract.
		/// Nullable scalars preserve partial GraphQL data when a field fails independently.
		/// </summary>
		private sealed class NexusCollectionRevisionNode
		{
			[JsonProperty("id")]
			public long? Id { get; set; }

			[JsonProperty("collectionId")]
			public long? CollectionId { get; set; }

			[JsonProperty("revisionNumber")]
			public long? RevisionNumber { get; set; }

			[JsonProperty("modCount")]
			public long? ModCount { get; set; }

			[JsonProperty("downloadLink")]
			public string DownloadLink { get; set; }
		}

		private sealed class NexusCollectionSummaryVariables
		{
			public NexusCollectionSummaryVariables(string slug)
			{
				Slug = slug;
			}

			[JsonProperty("slug")]
			public string Slug { get; }
		}

		/// <summary>
		/// Represents variables for either the latest or concrete revision operation.
		/// The revision property is omitted entirely for latest lookup rather than using a magic sentinel.
		/// </summary>
		private sealed class NexusCollectionRevisionVariables
		{
			public NexusCollectionRevisionVariables(string slug, int? revision)
			{
				Slug = slug;
				Revision = revision;
			}

			[JsonProperty("slug")]
			public string Slug { get; }

			[JsonProperty("revision", NullValueHandling = NullValueHandling.Ignore)]
			public int? Revision { get; }
		}
	}
}
