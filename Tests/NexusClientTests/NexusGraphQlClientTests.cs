using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies the protocol-level Nexus Mods GraphQL v2 client without depending on domain-specific queries.
    /// </summary>
    public class NexusGraphQlClientTests
    {
        /// <summary>
        /// Ensures GraphQL operations use the v2 endpoint, standard request envelope, and current session credentials.
        /// </summary>
        [Test]
        public void Execute_PostsStandardEnvelopeToV2Endpoint()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService("{\"data\":{\"viewer\":{\"name\":\"Alice\"}}}", request => capture = RequestCapture.From(request)))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("graphql-api-key"));
                const string query = "query Viewer($id: ID!) { viewer(id: $id) { name } }";

                NexusGraphQlResponse<ViewerData> result = service.GraphQl.ExecuteAsync<ViewerData>(query, new { id = "123" }, "Viewer").GetAwaiter().GetResult();

                Assert.AreEqual("Alice", result.Data.Viewer.Name);
                Assert.AreEqual(HttpMethod.Post, capture.Method);
                Assert.AreEqual("https://api.nexusmods.com/v2/graphql", capture.Uri.AbsoluteUri);
                CollectionAssert.AreEqual(new[] { "graphql-api-key" }, capture.Headers["apikey"]);
                GraphQlRequestFixture body = ApiJson.Deserialize<GraphQlRequestFixture>(capture.Body);
                Assert.AreEqual(query, body.Query);
                Assert.AreEqual("Viewer", body.OperationName);
                Assert.AreEqual("123", body.Variables.Id);
            }
        }

        /// <summary>
        /// Ensures a successful HTTP response can retain useful partial data and detailed GraphQL errors simultaneously.
        /// </summary>
        [Test]
        public void Execute_PreservesPartialDataAndErrors()
        {
            const string fixture = "{\"data\":{\"viewer\":{\"name\":\"Alice\"}},\"errors\":[{\"message\":\"Email is unavailable\",\"path\":[\"viewer\",\"email\"],\"locations\":[{\"line\":3,\"column\":5}],\"extensions\":{\"code\":\"FORBIDDEN\",\"reason\":\"private\"}}]}";
            using (NexusModsService service = CreateService(fixture))
            {
                NexusGraphQlResponse<ViewerData> result = service.GraphQl.ExecuteAsync<ViewerData>("query { viewer { name email } }").GetAwaiter().GetResult();

                Assert.AreEqual("Alice", result.Data.Viewer.Name);
                Assert.IsTrue(result.HasErrors);
                Assert.AreEqual(1, result.Errors.Length);
                Assert.AreEqual("Email is unavailable", result.Errors[0].Message);
                CollectionAssert.AreEqual(new[] { "viewer", "email" }, result.Errors[0].Path.Select(segment => segment.ToString()).ToArray());
                Assert.AreEqual(3, result.Errors[0].Locations[0].Line);
                Assert.AreEqual(5, result.Errors[0].Locations[0].Column);
                Assert.AreEqual("FORBIDDEN", result.Errors[0].Code);
                Assert.AreEqual("private", result.Errors[0].Extensions["reason"].ToString());
            }
        }

        /// <summary>
        /// Ensures GraphQL errors without a data property are returned as protocol results rather than converted into HTTP failures.
        /// </summary>
        [Test]
        public void Execute_ErrorsWithoutDataRemainGraphQlResult()
        {
            const string fixture = "{\"errors\":[{\"message\":\"Not allowed\",\"extensions\":{\"code\":\"FORBIDDEN\"}}]}";
            using (NexusModsService service = CreateService(fixture))
            {
                NexusGraphQlResponse<ViewerData> result = service.GraphQl.ExecuteAsync<ViewerData>("query { viewer { name } }").GetAwaiter().GetResult();

                Assert.IsNull(result.Data);
                Assert.IsTrue(result.HasErrors);
                Assert.AreEqual("FORBIDDEN", result.Errors[0].Code);
            }
        }

        /// <summary>
        /// Ensures malformed or structurally invalid GraphQL envelopes use the shared invalid-response error category.
        /// </summary>
        [TestCase("not-json")]
        [TestCase("{\"unexpected\":true}")]
        public void Execute_InvalidEnvelopeThrowsOwnedInvalidResponse(string fixture)
        {
            using (NexusModsService service = CreateService(fixture))
            {
                ApiException exception = Assert.Throws<ApiException>(() =>
                    service.GraphQl.ExecuteAsync<ViewerData>("query { viewer { name } }").GetAwaiter().GetResult());

                Assert.AreEqual(ApiErrorKind.InvalidResponse, exception.ErrorKind);
                Assert.AreEqual(HttpStatusCode.OK, exception.StatusCode);
            }
        }

        /// <summary>
        /// Ensures GraphQL responses update only the GraphQL quota surface and do not overwrite REST v1 state.
        /// </summary>
        [Test]
        public void Execute_TracksGraphQlQuotaIndependently()
        {
            var headers = new Dictionary<string, string>
            {
                { "x-rl-hourly-limit", "500" },
                { "x-rl-hourly-remaining", "44" },
                { "x-rl-hourly-reset", "2030-01-01T01:00:00Z" }
            };
            using (NexusModsService service = CreateService("{\"data\":{\"viewer\":null}}", headers: headers))
            {
                service.GraphQl.ExecuteAsync<ViewerData>("query { viewer { name } }").GetAwaiter().GetResult();

                Assert.AreEqual(44, service.RateLimits.GetSnapshot(NexusApiSurface.GraphQl).HourlyRemaining);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.V3).HourlyRemaining);
            }
        }

        /// <summary>
        /// Creates a Nexus Mods service backed by deterministic GraphQL fixture responses.
        /// </summary>
        private static NexusModsService CreateService(string content, Action<HttpRequestMessage> inspect = null, IDictionary<string, string> headers = null)
        {
            var handler = new StubHttpMessageHandler((request, token) =>
            {
                inspect?.Invoke(request);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                };
                if (headers != null)
                {
                    foreach (KeyValuePair<string, string> header in headers)
                        response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                return Task.FromResult(response);
            });
            return new NexusModsService(new ApiTransport(handler, TimeSpan.FromSeconds(5)), "NMM-Test/1.0", "NMM-Test", "1.0");
        }

        /// <summary>
        /// Provides deterministic HTTP responses without external network access.
        /// </summary>
        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            /// <summary>
            /// Initializes the handler with the response function used by a test.
            /// </summary>
            public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
            {
                _send = send ?? throw new ArgumentNullException(nameof(send));
            }

            /// <summary>
            /// Delegates request execution to the configured test function.
            /// </summary>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _send(request, cancellationToken);
            }
        }

        /// <summary>
        /// Captures request details before the transport disposes the request content.
        /// </summary>
        private sealed class RequestCapture
        {
            /// <summary>
            /// Gets the request HTTP method.
            /// </summary>
            public HttpMethod Method { get; private set; }

            /// <summary>
            /// Gets the absolute request URI.
            /// </summary>
            public Uri Uri { get; private set; }

            /// <summary>
            /// Gets the buffered request body.
            /// </summary>
            public string Body { get; private set; }

            /// <summary>
            /// Gets captured request headers keyed case-insensitively.
            /// </summary>
            public IDictionary<string, string[]> Headers { get; private set; }

            /// <summary>
            /// Captures a request synchronously for deterministic test inspection.
            /// </summary>
            public static RequestCapture From(HttpRequestMessage request)
            {
                return new RequestCapture
                {
                    Method = request.Method,
                    Uri = request.RequestUri,
                    Body = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
                    Headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
                };
            }
        }


        /// <summary>
        /// Represents the request envelope captured by the GraphQL protocol test.
        /// </summary>
        private sealed class GraphQlRequestFixture
        {
            /// <summary>
            /// Gets or sets the submitted GraphQL document.
            /// </summary>
            public string Query { get; set; }

            /// <summary>
            /// Gets or sets the submitted operation name.
            /// </summary>
            public string OperationName { get; set; }

            /// <summary>
            /// Gets or sets the submitted fixture variables.
            /// </summary>
            public GraphQlVariablesFixture Variables { get; set; }
        }

        /// <summary>
        /// Represents the variables captured by the GraphQL protocol test.
        /// </summary>
        private sealed class GraphQlVariablesFixture
        {
            /// <summary>
            /// Gets or sets the fixture identifier.
            /// </summary>
            public string Id { get; set; }
        }

        /// <summary>
        /// Represents the typed data payload used by GraphQL protocol tests.
        /// </summary>
        private sealed class ViewerData
        {
            /// <summary>
            /// Gets or sets fixture viewer data.
            /// </summary>
            public Viewer Viewer { get; set; }
        }

        /// <summary>
        /// Represents the fixture viewer returned by GraphQL protocol tests.
        /// </summary>
        private sealed class Viewer
        {
            /// <summary>
            /// Gets or sets the fixture viewer name.
            /// </summary>
            public string Name { get; set; }
        }
    }
}
