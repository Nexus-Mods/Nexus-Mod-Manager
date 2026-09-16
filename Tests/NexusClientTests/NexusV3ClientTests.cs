using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.V3;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies the protocol-level Nexus Mods REST v3 client without depending on feature-specific endpoints.
    /// </summary>
    public class NexusV3ClientTests
    {
        /// <summary>
        /// Ensures typed v3 requests use the v3 base URI, session credentials, and JSON serialization.
        /// </summary>
        [Test]
        public void Execute_UsesV3BaseAndTypedJson()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(HttpStatusCode.OK, "{\"data\":{\"id\":\"mod-1\",\"count\":2}}", request => capture = RequestCapture.From(request)))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("v3-api-key"));

                FixtureEnvelope result = service.V3.ExecuteAsync<FixtureEnvelope>(
                    HttpMethod.Post,
                    "protocol-test?mode=typed",
                    new { name = "sample" }).GetAwaiter().GetResult();

                Assert.AreEqual("mod-1", result.Data.Id);
                Assert.AreEqual(2, result.Data.Count);
                Assert.AreEqual(HttpMethod.Post, capture.Method);
                Assert.AreEqual("https://api.nexusmods.com/v3/protocol-test?mode=typed", capture.Uri.AbsoluteUri);
                CollectionAssert.AreEqual(new[] { "v3-api-key" }, capture.Headers["apikey"]);
                StringAssert.Contains("\"name\":\"sample\"", capture.Body);
            }
        }

        /// <summary>
        /// Ensures v3 requests support the future Bearer/JWT credential path already modeled by the shared request policy.
        /// </summary>
        [Test]
        public void Execute_UsesBearerCredentialWhenSessionUsesBearer()
        {
            RequestCapture capture = null;
            using (NexusModsService service = CreateService(HttpStatusCode.OK, "{\"data\":{\"id\":\"ok\",\"count\":1}}", request => capture = RequestCapture.From(request)))
            {
                service.ReplaceCredentials(NexusCredentials.FromBearerToken("oauth-token"));

                service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Get, "protocol-test").GetAwaiter().GetResult();

                Assert.AreEqual("Bearer", capture.AuthorizationScheme);
                Assert.AreEqual("oauth-token", capture.AuthorizationParameter);
                Assert.IsFalse(capture.Headers.ContainsKey("apikey"));
            }
        }

        /// <summary>
        /// Ensures successful no-content v3 operations do not attempt JSON deserialization.
        /// </summary>
        [Test]
        public void Execute_NoContentSuccessCanBeIgnored()
        {
            using (NexusModsService service = CreateService(HttpStatusCode.NoContent, string.Empty))
                service.V3.ExecuteAsync(HttpMethod.Put, "protocol-test", new { enabled = true }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Ensures RFC 9457 Problem Details and validation entries survive v3 error mapping.
        /// </summary>
        [Test]
        public void Execute_PreservesProblemDetailsAndValidationErrors()
        {
            const string fixture = "{\"type\":\"https://api.nexusmods.com/problems/validation\",\"title\":\"Validation failed\",\"status\":422,\"detail\":\"The request body is invalid.\",\"instance\":\"/v3/protocol-test\",\"errors\":[{\"detail\":\"A mod id is required.\",\"pointer\":\"/mod_id\"}],\"trace_id\":\"abc123\"}";
            using (NexusModsService service = CreateService((HttpStatusCode)422, fixture))
            {
                NexusV3ApiException exception = Assert.Throws<NexusV3ApiException>(() =>
                    service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Post, "protocol-test", new { }).GetAwaiter().GetResult());

                Assert.AreEqual(ApiErrorKind.Validation, exception.ErrorKind);
                Assert.AreEqual((HttpStatusCode)422, exception.StatusCode);
                Assert.AreEqual("https://api.nexusmods.com/problems/validation", exception.ProviderCode);
                Assert.AreEqual("Validation failed", exception.Message);
                Assert.AreEqual("The request body is invalid.", exception.Problem.Detail);
                Assert.AreEqual("/v3/protocol-test", exception.Problem.Instance);
                Assert.AreEqual(1, exception.ValidationErrors.Length);
                Assert.AreEqual("A mod id is required.", exception.ValidationErrors[0].Detail);
                Assert.AreEqual("/mod_id", exception.ValidationErrors[0].Pointer);
                Assert.AreEqual("abc123", exception.Problem.AdditionalFields["trace_id"].ToString());
            }
        }

        /// <summary>
        /// Ensures malformed error bodies retain status-based classification instead of hiding the HTTP failure.
        /// </summary>
        [Test]
        public void Execute_NonProblemErrorBodyFallsBackToHttpClassification()
        {
            using (NexusModsService service = CreateService(HttpStatusCode.Forbidden, "not-json"))
            {
                NexusV3ApiException exception = Assert.Throws<NexusV3ApiException>(() =>
                    service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Get, "protocol-test").GetAwaiter().GetResult());

                Assert.AreEqual(ApiErrorKind.Forbidden, exception.ErrorKind);
                Assert.IsNull(exception.Problem);
            }
        }

        /// <summary>
        /// Ensures successful payloads which cannot be deserialized use the shared invalid-response classification.
        /// </summary>
        [Test]
        public void Execute_InvalidSuccessPayloadThrowsOwnedInvalidResponse()
        {
            using (NexusModsService service = CreateService(HttpStatusCode.OK, "not-json"))
            {
                ApiException exception = Assert.Throws<ApiException>(() =>
                    service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Get, "protocol-test").GetAwaiter().GetResult());

                Assert.AreEqual(ApiErrorKind.InvalidResponse, exception.ErrorKind);
                Assert.AreEqual(HttpStatusCode.OK, exception.StatusCode);
            }
        }

        /// <summary>
        /// Ensures v3 responses update only the v3 quota surface.
        /// </summary>
        [Test]
        public void Execute_TracksV3QuotaIndependently()
        {
            var headers = new Dictionary<string, string>
            {
                { "x-rl-hourly-limit", "500" },
                { "x-rl-hourly-remaining", "31" },
                { "x-rl-hourly-reset", "2030-01-01T01:00:00Z" }
            };
            using (NexusModsService service = CreateService(HttpStatusCode.OK, "{\"data\":{\"id\":\"ok\",\"count\":1}}", headers: headers))
            {
                service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Get, "protocol-test").GetAwaiter().GetResult();

                Assert.AreEqual(31, service.RateLimits.GetSnapshot(NexusApiSurface.V3).HourlyRemaining);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.GraphQl).HourlyRemaining);
            }
        }

        /// <summary>
        /// Ensures caller cancellation flows through the shared transport and remains distinguishable from timeouts.
        /// </summary>
        [Test]
        public void Execute_PreservesCallerCancellation()
        {
            var handler = new StubHttpMessageHandler(async (request, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
            using (var service = new NexusModsService(new ApiTransport(handler, TimeSpan.FromSeconds(5)), "NMM-Test/1.0", "NMM-Test", "1.0"))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();

                ApiException exception = Assert.Throws<ApiException>(() =>
                    service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Get, "protocol-test", cancellationToken: cancellation.Token).GetAwaiter().GetResult());

                Assert.AreEqual(ApiErrorKind.Cancelled, exception.ErrorKind);
            }
        }

        /// <summary>
        /// Ensures the protocol executor cannot be used to escape the REST v3 API base path.
        /// </summary>
        [TestCase("https://example.test/steal")]
        [TestCase("../v1/users/validate.json")]
        public void Execute_RejectsPathsOutsideV3Base(string path)
        {
            using (NexusModsService service = CreateService(HttpStatusCode.OK, "{}"))
                Assert.Throws<ArgumentException>(() => service.V3.ExecuteAsync<FixtureEnvelope>(HttpMethod.Get, path).GetAwaiter().GetResult());
        }

        /// <summary>
        /// Creates a Nexus Mods service backed by deterministic REST v3 fixture responses.
        /// </summary>
        private static NexusModsService CreateService(HttpStatusCode statusCode, string content, Action<HttpRequestMessage> inspect = null, IDictionary<string, string> headers = null)
        {
            var handler = new StubHttpMessageHandler((request, token) =>
            {
                inspect?.Invoke(request);
                var response = new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(content ?? string.Empty)
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
            /// Gets the Authorization scheme when present.
            /// </summary>
            public string AuthorizationScheme { get; private set; }

            /// <summary>
            /// Gets the Authorization parameter when present.
            /// </summary>
            public string AuthorizationParameter { get; private set; }

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
                    Headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                    AuthorizationScheme = request.Headers.Authorization?.Scheme,
                    AuthorizationParameter = request.Headers.Authorization?.Parameter
                };
            }
        }

        /// <summary>
        /// Provides a test-only typed REST v3 response envelope.
        /// </summary>
        private sealed class FixtureEnvelope
        {
            /// <summary>
            /// Gets or sets the test response data.
            /// </summary>
            public FixtureData Data { get; set; }
        }

        /// <summary>
        /// Provides test-only typed response data.
        /// </summary>
        private sealed class FixtureData
        {
            /// <summary>
            /// Gets or sets the test identifier.
            /// </summary>
            public string Id { get; set; }

            /// <summary>
            /// Gets or sets the test count.
            /// </summary>
            public int Count { get; set; }
        }
    }
}
