using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Verifies Nexus-specific session, authentication, rate-limit, and error behavior independently from repository integration.
    /// </summary>
    public class NexusModsProviderInfrastructureTests
    {
        /// <summary>
        /// Ensures API-key credentials are attached only after the request destination is approved.
        /// </summary>
        [Test]
        public void RequestPolicy_ApiKeyIsAppliedOnlyToApprovedApiHost()
        {
            var policy = new NexusRequestPolicy("NMM-Test/1.0", "NMM-Test", "1.0");
            NexusCredentials credentials = NexusCredentials.FromApiKey("secret-api-key");

            using (HttpRequestMessage request = policy.CreateRequest(HttpMethod.Get, new Uri("https://api.nexusmods.com/v1/games.json"), credentials))
            {
                Assert.IsTrue(request.Headers.TryGetValues("apikey", out IEnumerable<string> values));
                CollectionAssert.AreEqual(new[] { "secret-api-key" }, values.ToArray());
                CollectionAssert.AreEqual(new[] { "NMM-Test" }, request.Headers.GetValues("Application-Name").ToArray());
                CollectionAssert.AreEqual(new[] { "1.0" }, request.Headers.GetValues("Application-Version").ToArray());
                Assert.IsNull(request.Headers.Authorization);
                IReadOnlyDictionary<string, string[]> sanitized = ApiDiagnosticSanitizer.SanitizeHeaders(request.Headers, NexusRequestPolicy.SensitiveHeaderNames);
                CollectionAssert.AreEqual(new[] { "<redacted>" }, sanitized["apikey"]);
            }

            Assert.Throws<InvalidOperationException>(() =>
                policy.CreateRequest(HttpMethod.Get, new Uri("https://cdn.nexusmods.com/file.zip"), credentials));
            Assert.Throws<InvalidOperationException>(() =>
                policy.CreateRequest(HttpMethod.Get, new Uri("http://api.nexusmods.com/v1/games.json"), credentials));
        }

        /// <summary>
        /// Ensures future bearer credentials use the Authorization header without also emitting the legacy API-key header.
        /// </summary>
        [Test]
        public void RequestPolicy_BearerTokenUsesAuthorizationHeader()
        {
            var policy = new NexusRequestPolicy("NMM-Test/1.0", "NMM-Test", "1.0");
            using (HttpRequestMessage request = policy.CreateRequest(HttpMethod.Get, new Uri("https://api.nexusmods.com/v3/games"), NexusCredentials.FromBearerToken("oauth-token")))
            {
                Assert.AreEqual("Bearer", request.Headers.Authorization.Scheme);
                Assert.AreEqual("oauth-token", request.Headers.Authorization.Parameter);
                Assert.IsFalse(request.Headers.Contains("apikey"));
            }
        }

        /// <summary>
        /// Ensures replacing credentials advances the generation, cancels the previous session, and clears credential-scoped quotas.
        /// </summary>
        [Test]
        public void Service_ReplaceCredentialsInvalidatesPreviousSessionAndQuotaState()
        {
            using (var transport = new ApiTransport(new StubHttpMessageHandler((request, token) => Task.FromResult(CreateHttpResponse(HttpStatusCode.OK,
                new Dictionary<string, string> { { "x-rl-hourly-remaining", "4" } }))), TimeSpan.FromSeconds(5)))
            using (var service = new NexusModsService(transport, "NMM-Test/1.0", "NMM-Test", "1.0"))
            {
                NexusSessionContext first = service.ReplaceCredentials(NexusCredentials.FromApiKey("first"));
                service.SendAsync(NexusApiSurface.V1, HttpMethod.Get, new Uri("https://api.nexusmods.com/v1/games.json")).GetAwaiter().GetResult();
                Assert.AreEqual(4, service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);

                NexusSessionContext second = service.ReplaceCredentials(NexusCredentials.FromApiKey("second"));
                Assert.Greater(second.Generation, first.Generation);
                Assert.IsTrue(first.Token.IsCancellationRequested);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
            }
        }

        /// <summary>
        /// Ensures a response which wins a cancellation race cannot update or escape from an invalidated account generation.
        /// </summary>
        [Test]
        public void Service_LateResponseFromInvalidatedSessionIsRejected()
        {
            var started = new TaskCompletionSource<bool>();
            var release = new TaskCompletionSource<bool>();
            var handler = new StubHttpMessageHandler(async (request, token) =>
            {
                started.TrySetResult(true);
                await release.Task.ConfigureAwait(false);
                return CreateHttpResponse(HttpStatusCode.OK, new Dictionary<string, string> { { "x-rl-hourly-remaining", "19" } });
            });

            using (var transport = new ApiTransport(handler, TimeSpan.FromSeconds(5)))
            using (var service = new NexusModsService(transport, "NMM-Test/1.0", "NMM-Test", "1.0"))
            {
                service.ReplaceCredentials(NexusCredentials.FromApiKey("first"));
                Task<ApiResponse> request = service.SendAsync(NexusApiSurface.V1, HttpMethod.Get, new Uri("https://api.nexusmods.com/v1/games.json"));
                started.Task.GetAwaiter().GetResult();

                service.ReplaceCredentials(NexusCredentials.FromApiKey("second"));
                release.TrySetResult(true);

                ApiException exception = Assert.Throws<ApiException>(() => request.GetAwaiter().GetResult());
                Assert.AreEqual(ApiErrorKind.Cancelled, exception.ErrorKind);
                Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
            }
        }

        /// <summary>
        /// Ensures quota state is isolated by API surface and stale responses cannot increase the remaining count in the same window.
        /// </summary>
        [Test]
        public void RateLimits_AreSurfaceScopedAndConservativeWithinResetWindow()
        {
            var tracker = new NexusRateLimitTracker();
            tracker.Reset(7);
            ApiResponse first = CreateApiResponse(HttpStatusCode.OK, new Dictionary<string, string>
            {
                { "x-rl-hourly-limit", "500" },
                { "x-rl-hourly-remaining", "100" },
                { "x-rl-hourly-reset", "2030-01-01T01:00:00Z" }
            });
            ApiResponse stale = CreateApiResponse(HttpStatusCode.OK, new Dictionary<string, string>
            {
                { "x-rl-hourly-limit", "500" },
                { "x-rl-hourly-remaining", "120" },
                { "x-rl-hourly-reset", "2030-01-01T01:00:00Z" }
            });
            ApiResponse graphQl = CreateApiResponse(HttpStatusCode.OK, new Dictionary<string, string>
            {
                { "x-rl-hourly-remaining", "77" },
                { "x-rl-hourly-reset", "2030-01-01T01:00:00Z" }
            });

            Assert.IsTrue(tracker.Update(NexusApiSurface.V1, 7, first));
            Assert.IsTrue(tracker.Update(NexusApiSurface.V1, 7, stale));
            Assert.IsTrue(tracker.Update(NexusApiSurface.GraphQl, 7, graphQl));
            Assert.AreEqual(100, tracker.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
            Assert.AreEqual(77, tracker.GetSnapshot(NexusApiSurface.GraphQl).HourlyRemaining);
            Assert.IsNull(tracker.GetSnapshot(NexusApiSurface.V3).HourlyRemaining);
        }

        /// <summary>
        /// Ensures a response from a previous session generation cannot repopulate freshly reset quota state.
        /// </summary>
        [Test]
        public void RateLimits_IgnorePreviousSessionGeneration()
        {
            var tracker = new NexusRateLimitTracker();
            tracker.Reset(3);
            ApiResponse response = CreateApiResponse(HttpStatusCode.OK, new Dictionary<string, string> { { "x-rl-daily-remaining", "900" } });

            tracker.Reset(4);
            Assert.IsFalse(tracker.Update(NexusApiSurface.V1, 3, response));
            Assert.IsNull(tracker.GetSnapshot(NexusApiSurface.V1).DailyRemaining);
        }

        /// <summary>
        /// Ensures malformed or absent rate-limit headers remain unknown instead of breaking otherwise valid API responses.
        /// </summary>
        [Test]
        public void RateLimits_MissingAndMalformedHeadersRemainUnknown()
        {
            var tracker = new NexusRateLimitTracker();
            tracker.Reset(1);
            ApiResponse response = CreateApiResponse(HttpStatusCode.OK, new Dictionary<string, string>
            {
                { "x-rl-hourly-remaining", "not-a-number" },
                { "x-rl-daily-reset", "not-a-date" }
            });

            Assert.IsTrue(tracker.Update(NexusApiSurface.V1, 1, response));
            NexusRateLimitSnapshot snapshot = tracker.GetSnapshot(NexusApiSurface.V1);
            Assert.IsNull(snapshot.HourlyRemaining);
            Assert.IsNull(snapshot.DailyReset);
        }

        /// <summary>
        /// Ensures status codes, not error-message wording, determine Nexus error classification.
        /// </summary>
        [Test]
        public void ErrorMapper_UsesStatusInsteadOfMessageText()
        {
            ApiResponse forbidden = CreateApiResponse(HttpStatusCode.Forbidden, null, "{\"code\":403,\"message\":\"rate limit exceeded according to message text\"}");
            ApiException forbiddenException = NexusErrorMapper.CreateException(forbidden);
            Assert.AreEqual(ApiErrorKind.Forbidden, forbiddenException.ErrorKind);
            Assert.AreEqual("403", forbiddenException.ProviderCode);

            ApiResponse limited = CreateApiResponse((HttpStatusCode)429, new Dictionary<string, string> { { "Retry-After", "120" } }, "{\"code\":429,\"message\":\"slow down\"}");
            ApiException limitedException = NexusErrorMapper.CreateException(limited);
            Assert.AreEqual(ApiErrorKind.RateLimit, limitedException.ErrorKind);
            Assert.AreEqual(TimeSpan.FromSeconds(120), limitedException.RetryAfter);
        }

        /// <summary>
        /// Ensures credential formatting never reveals the stored secret.
        /// </summary>
        [Test]
        public void Credentials_ToStringDoesNotExposeSecret()
        {
            NexusCredentials credentials = NexusCredentials.FromApiKey("super-secret-value");
            Assert.AreEqual("ApiKey", credentials.ToString());
            StringAssert.DoesNotContain("super-secret-value", credentials.ToString());
        }

        /// <summary>
        /// Creates a buffered API response through the real provider-neutral transport.
        /// </summary>
        private static ApiResponse CreateApiResponse(HttpStatusCode statusCode, IDictionary<string, string> headers = null, string content = "")
        {
            using (var transport = new ApiTransport(new StubHttpMessageHandler((request, token) => Task.FromResult(CreateHttpResponse(statusCode, headers, content))), TimeSpan.FromSeconds(5)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api"))
                return transport.SendAsync(request).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Creates an HTTP response with optional headers for deterministic transport tests.
        /// </summary>
        private static HttpResponseMessage CreateHttpResponse(HttpStatusCode statusCode, IDictionary<string, string> headers = null, string content = "")
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content ?? string.Empty)
            };
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return response;
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
    }
}
