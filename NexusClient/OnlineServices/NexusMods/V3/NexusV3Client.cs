using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods.V3
{
    /// <summary>
    /// Provides the protocol-level Nexus Mods REST v3 client without embedding feature-specific endpoint wrappers.
    /// </summary>
    public sealed class NexusV3Client
    {
        private static readonly Uri BaseUri = new Uri("https://api.nexusmods.com/v3/", UriKind.Absolute);
        private readonly NexusModsService _service;

        /// <summary>
        /// Initializes the REST v3 client over the shared Nexus Mods service.
        /// </summary>
        public NexusV3Client(NexusModsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Executes one REST v3 operation and deserializes the complete successful JSON response into the requested type.
        /// </summary>
        public async Task<TResponse> ExecuteAsync<TResponse>(HttpMethod method, string relativePath, object requestBody = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Uri uri = BuildUri(relativePath);
            HttpContent content = requestBody == null ? null : ApiJson.CreateContent(requestBody);
            ApiResponse response = await _service.SendAsync(NexusApiSurface.V3, method, uri, content, cancellationToken).ConfigureAwait(false);

            try
            {
                TResponse result = ApiJson.Deserialize<TResponse>(response.Content);
                if (ReferenceEquals(result, null))
                    throw new JsonSerializationException("The response JSON deserialized to null.");

                return result;
            }
            catch (JsonException ex)
            {
                throw new ApiException(ApiErrorKind.InvalidResponse, "Nexus Mods returned an invalid REST v3 response.", response.StatusCode, innerException: ex);
            }
        }

        /// <summary>
        /// Executes one REST v3 operation whose successful response body is intentionally ignored, such as a 204 response.
        /// </summary>
        public async Task ExecuteAsync(HttpMethod method, string relativePath, object requestBody = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Uri uri = BuildUri(relativePath);
            HttpContent content = requestBody == null ? null : ApiJson.CreateContent(requestBody);
            await _service.SendAsync(NexusApiSurface.V3, method, uri, content, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a URI constrained to the REST v3 base path so callers cannot escape the approved API surface.
        /// </summary>
        private static Uri BuildUri(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("A REST v3 relative path is required.", nameof(relativePath));
            if (Uri.TryCreate(relativePath, UriKind.Absolute, out Uri absolute))
                throw new ArgumentException("REST v3 operations must use a relative path.", nameof(relativePath));

            string normalized = relativePath.TrimStart('/');
            Uri uri = new Uri(BaseUri, normalized);
            if (!string.Equals(uri.Scheme, BaseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                !uri.IsDefaultPort ||
                !uri.AbsolutePath.StartsWith(BaseUri.AbsolutePath, StringComparison.Ordinal))
                throw new ArgumentException("The REST v3 relative path cannot escape the v3 API base path.", nameof(relativePath));

            return uri;
        }
    }
}
