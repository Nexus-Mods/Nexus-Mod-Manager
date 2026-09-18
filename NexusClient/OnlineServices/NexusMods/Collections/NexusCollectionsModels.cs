using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>
	/// Contains collection-level Nexus metadata used to construct an NMM Collection definition.
	/// </summary>
	/// <remarks>
	/// The public slug remains a lookup locator. Stable Collection identity is supplied by the provider numeric ID and
	/// converted to an opaque domain identity only by <see cref="NexusCollectionDomainMapper"/>.
	/// </remarks>
	public sealed class NexusCollectionSummaryMetadata
	{
		internal NexusCollectionSummaryMetadata(
			long? collectionId,
			string collectionSlug,
			string name,
			string authorDisplayName,
			string summary)
		{
			CollectionId = collectionId;
			CollectionSlug = collectionSlug;
			Name = name;
			AuthorDisplayName = authorDisplayName;
			Summary = summary;
		}

		/// <summary>
		/// Gets the stable Nexus provider collection identifier when that field was returned successfully.
		/// </summary>
		public long? CollectionId { get; }

		/// <summary>
		/// Gets the public Nexus collection slug returned by the provider.
		/// </summary>
		/// <remarks>The slug is not used as durable Collection identity.</remarks>
		public string CollectionSlug { get; }

		/// <summary>
		/// Gets the optional provider display name. Decorative metadata may be absent in a partial response.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Gets the optional curator/author display name.
		/// </summary>
		public string AuthorDisplayName { get; }

		/// <summary>
		/// Gets the optional provider summary exactly as returned.
		/// </summary>
		public string Summary { get; }

		/// <summary>
		/// Gets whether the provider returned the stable identity needed to create a Collection definition.
		/// </summary>
		public bool HasStableIdentity
		{
			get { return CollectionId.HasValue && CollectionId.Value > 0; }
		}
	}

	/// <summary>
	/// Preserves one Nexus collection summary lookup, including useful partial data and GraphQL field errors.
	/// </summary>
	public sealed class NexusCollectionSummaryLookupResult
	{
		private readonly ReadOnlyCollection<NexusGraphQlError> _errors;

		internal NexusCollectionSummaryLookupResult(
			string requestedSlug,
			long sessionGeneration,
			NexusCollectionSummaryMetadata collection,
			NexusGraphQlError[] errors)
		{
			RequestedSlug = requestedSlug;
			SessionGeneration = sessionGeneration;
			Collection = collection;

			NexusGraphQlError[] errorCopy = errors == null
				? new NexusGraphQlError[0]
				: (NexusGraphQlError[])errors.Clone();
			_errors = Array.AsReadOnly(errorCopy);
		}

		/// <summary>
		/// Gets the exact slug locator submitted to Nexus.
		/// </summary>
		public string RequestedSlug { get; }

		/// <summary>
		/// Gets the Nexus credential/session generation under which this result was obtained.
		/// </summary>
		public long SessionGeneration { get; }

		/// <summary>
		/// Gets provider summary metadata, which may be partial or null when GraphQL reported field/query errors.
		/// </summary>
		public NexusCollectionSummaryMetadata Collection { get; }

		/// <summary>
		/// Gets GraphQL field/query errors returned alongside any partial collection data.
		/// </summary>
		public IReadOnlyList<NexusGraphQlError> Errors
		{
			get { return _errors; }
		}

		/// <summary>
		/// Gets whether Nexus returned one or more GraphQL errors with the response.
		/// </summary>
		public bool HasErrors
		{
			get { return _errors.Count > 0; }
		}
	}

	/// <summary>
	/// Identifies which Nexus collection revision should be resolved without using magic revision sentinels.
	/// </summary>
	public sealed class NexusCollectionRevisionRequest
	{
		private NexusCollectionRevisionRequest(string collectionSlug, int? revisionNumber)
		{
			CollectionSlug = RequireSlug(collectionSlug);
			if (revisionNumber.HasValue && revisionNumber.Value <= 0)
				throw new ArgumentOutOfRangeException(nameof(revisionNumber), "A concrete Nexus collection revision number must be positive.");

			RevisionNumber = revisionNumber;
		}

		/// <summary>
		/// Gets the public Nexus collection slug used to locate the collection.
		/// </summary>
		/// <remarks>
		/// The slug is a lookup locator, not the durable collection identity. The provider response supplies the
		/// stable numeric collection and revision identifiers which the Collection domain records separately.
		/// </remarks>
		public string CollectionSlug { get; }

		/// <summary>
		/// Gets the requested concrete revision number, or <c>null</c> when the latest published revision is requested.
		/// </summary>
		public int? RevisionNumber { get; }

		/// <summary>
		/// Gets whether this request asks Nexus to resolve the latest published revision.
		/// </summary>
		public bool IsLatest
		{
			get { return !RevisionNumber.HasValue; }
		}

		/// <summary>
		/// Creates a request which resolves the current latest published revision once.
		/// </summary>
		public static NexusCollectionRevisionRequest Latest(string collectionSlug)
		{
			return new NexusCollectionRevisionRequest(collectionSlug, null);
		}

		/// <summary>
		/// Creates a request for one concrete curator-facing revision number.
		/// </summary>
		public static NexusCollectionRevisionRequest Concrete(string collectionSlug, int revisionNumber)
		{
			return new NexusCollectionRevisionRequest(collectionSlug, revisionNumber);
		}

		internal static string RequireSlug(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("A Nexus collection slug is required.", nameof(value));
			if (!StringComparer.Ordinal.Equals(value, value.Trim()))
				throw new ArgumentException("A Nexus collection slug must not contain leading or trailing whitespace.", nameof(value));

			return value;
		}
	}

	/// <summary>
	/// Contains the provider fields needed to identify a concrete Nexus collection revision and continue acquisition.
	/// </summary>
	/// <remarks>
	/// Numeric provider IDs remain provider metadata here. The Collection domain deliberately converts them to opaque
	/// stable identity tokens at its adapter boundary instead of assuming numeric identity globally.
	/// </remarks>
	public sealed class NexusCollectionRevisionMetadata
	{
		internal NexusCollectionRevisionMetadata(
			long? collectionId,
			long? revisionId,
			long? revisionNumber,
			long? modCount,
			string bundleResolutionPath)
		{
			CollectionId = collectionId;
			RevisionId = revisionId;
			RevisionNumber = revisionNumber;
			ModCount = modCount;
			BundleResolutionPath = bundleResolutionPath;
		}

		/// <summary>
		/// Gets the stable Nexus provider collection identifier when that field was returned successfully.
		/// </summary>
		public long? CollectionId { get; }

		/// <summary>
		/// Gets the stable Nexus provider revision identifier when that field was returned successfully.
		/// </summary>
		public long? RevisionId { get; }

		/// <summary>
		/// Gets the curator-facing concrete revision number resolved by Nexus.
		/// </summary>
		public long? RevisionNumber { get; }

		/// <summary>
		/// Gets the provider-declared member/mod count when supplied.
		/// </summary>
		public long? ModCount { get; }

		/// <summary>
		/// Gets the Nexus API path used by the later authorized bundle-resolution operation.
		/// </summary>
		/// <remarks>
		/// This is not a final artifact URL and must not be persisted as artifact identity. C2 bundle resolution validates
		/// the path against the approved Nexus API origin before credentials are attached.
		/// </remarks>
		public string BundleResolutionPath { get; }

		/// <summary>
		/// Gets whether the provider returned the exact collection/revision identity required for durable provenance.
		/// </summary>
		public bool HasStableIdentity
		{
			get
			{
				return CollectionId.HasValue && CollectionId.Value > 0 &&
					RevisionId.HasValue && RevisionId.Value > 0 &&
					RevisionNumber.HasValue && RevisionNumber.Value > 0;
			}
		}
	}

	/// <summary>
	/// Describes one ephemeral provider-authorized location from which a collection bundle may be acquired.
	/// </summary>
	/// <remarks>
	/// The URI may contain short-lived CDN authorization material. It is acquisition data only: callers must not use it
	/// as collection/revision/artifact identity and should resolve it again near use after long pauses.
	/// </remarks>
	public sealed class NexusCollectionBundleDownloadLocation
	{
		internal NexusCollectionBundleDownloadLocation(Uri uri, string name, string shortName)
		{
			if (uri == null)
				throw new ArgumentNullException(nameof(uri));
			if (!uri.IsAbsoluteUri || !StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, System.Uri.UriSchemeHttps))
				throw new ArgumentException("A Nexus collection bundle download location must be an absolute HTTPS URI.", nameof(uri));
			if (!string.IsNullOrEmpty(uri.UserInfo))
				throw new ArgumentException("A Nexus collection bundle download location must not contain URI user information.", nameof(uri));

			Uri = uri;
			Name = name;
			ShortName = shortName;
		}

		/// <summary>
		/// Gets the ephemeral final HTTPS location returned by Nexus.
		/// </summary>
		public Uri Uri { get; }

		/// <summary>
		/// Gets the optional provider display name for the download location.
		/// </summary>
		public string Name { get; }

		/// <summary>
		/// Gets the optional provider short/server name for the download location.
		/// </summary>
		public string ShortName { get; }
	}

	/// <summary>
	/// Preserves one authorized collection-bundle resolution result for an exact provider revision.
	/// </summary>
	/// <remarks>
	/// The returned locations are intentionally ephemeral and separate from durable collection/artifact identity. This
	/// object authorizes no installation and does not imply that the bundle has been downloaded or verified.
	/// </remarks>
	public sealed class NexusCollectionBundleResolutionResult
	{
		private readonly ReadOnlyCollection<NexusCollectionBundleDownloadLocation> _downloadLocations;

		internal NexusCollectionBundleResolutionResult(
			NexusCollectionRevisionMetadata revision,
			long sessionGeneration,
			NexusCollectionBundleDownloadLocation[] downloadLocations)
		{
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			SessionGeneration = sessionGeneration;
			NexusCollectionBundleDownloadLocation[] copy = downloadLocations == null
				? new NexusCollectionBundleDownloadLocation[0]
				: (NexusCollectionBundleDownloadLocation[])downloadLocations.Clone();
			_downloadLocations = Array.AsReadOnly(copy);
		}

		/// <summary>
		/// Gets the exact concrete provider revision whose bundle was authorized.
		/// </summary>
		public NexusCollectionRevisionMetadata Revision { get; }

		/// <summary>
		/// Gets the Nexus credential/session generation under which the authorization request completed.
		/// </summary>
		public long SessionGeneration { get; }

		/// <summary>
		/// Gets the provider-authorized ephemeral final download locations.
		/// </summary>
		public IReadOnlyList<NexusCollectionBundleDownloadLocation> DownloadLocations
		{
			get { return _downloadLocations; }
		}

		/// <summary>
		/// Gets whether Nexus returned at least one usable final location.
		/// </summary>
		public bool HasDownloadLocations
		{
			get { return _downloadLocations.Count > 0; }
		}
	}

	/// <summary>
	/// Preserves one Nexus collection revision lookup, including useful partial data and GraphQL field errors.
	/// </summary>
	public sealed class NexusCollectionRevisionLookupResult
	{
		private readonly ReadOnlyCollection<NexusGraphQlError> _errors;

		internal NexusCollectionRevisionLookupResult(
			NexusCollectionRevisionRequest request,
			long sessionGeneration,
			NexusCollectionRevisionMetadata revision,
			NexusGraphQlError[] errors)
		{
			Request = request ?? throw new ArgumentNullException(nameof(request));
			SessionGeneration = sessionGeneration;
			Revision = revision;

			NexusGraphQlError[] errorCopy = errors == null
				? new NexusGraphQlError[0]
				: (NexusGraphQlError[])errors.Clone();
			_errors = Array.AsReadOnly(errorCopy);
		}

		/// <summary>
		/// Gets the exact request whose selector produced this result.
		/// </summary>
		public NexusCollectionRevisionRequest Request { get; }

		/// <summary>
		/// Gets the Nexus credential/session generation under which this result was obtained.
		/// </summary>
		public long SessionGeneration { get; }

		/// <summary>
		/// Gets provider revision metadata, which may be partial or null when GraphQL reported field/query errors.
		/// </summary>
		public NexusCollectionRevisionMetadata Revision { get; }

		/// <summary>
		/// Gets GraphQL field/query errors returned alongside any partial revision data.
		/// </summary>
		public IReadOnlyList<NexusGraphQlError> Errors
		{
			get { return _errors; }
		}

		/// <summary>
		/// Gets whether Nexus returned one or more GraphQL errors with the response.
		/// </summary>
		public bool HasErrors
		{
			get { return _errors.Count > 0; }
		}
	}
}
