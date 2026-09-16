using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods.GraphQl
{
    /// <summary>
    /// Provides the protocol-level Nexus Mods GraphQL v2 client without embedding domain-specific queries.
    /// </summary>
    public sealed class NexusGraphQlClient
    {
        private static readonly Uri Endpoint = new Uri("https://api.nexusmods.com/v2/graphql", UriKind.Absolute);
        private readonly NexusModsService _service;

        /// <summary>
        /// Initializes the GraphQL client over the shared Nexus Mods service.
        /// </summary>
        public NexusGraphQlClient(NexusModsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>
        /// Executes one GraphQL operation and preserves both typed partial data and GraphQL errors from a successful HTTP response.
        /// </summary>
        public async Task<NexusGraphQlResponse<TData>> ExecuteAsync<TData>(string query, object variables = null, string operationName = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("A GraphQL query document is required.", nameof(query));

            var request = new NexusGraphQlRequest(query, variables, operationName);
            ApiResponse response = await _service.SendAsync(
                NexusApiSurface.GraphQl,
                HttpMethod.Post,
                Endpoint,
                ApiJson.CreateContent(request),
                cancellationToken).ConfigureAwait(false);

            try
            {
                JObject payload = JObject.Parse(response.Content);
                if (payload.Property("data") == null && payload.Property("errors") == null)
                    throw new JsonSerializationException("The GraphQL response contains neither data nor errors.");

                NexusGraphQlResponse<TData> result = payload.ToObject<NexusGraphQlResponse<TData>>();
                if (result == null)
                    throw new JsonSerializationException("The GraphQL response deserialized to null.");

                return result;
            }
            catch (JsonException ex)
            {
                throw new ApiException(ApiErrorKind.InvalidResponse, "Nexus Mods returned an invalid GraphQL v2 response.", response.StatusCode, innerException: ex);
            }
        }
    }
}
