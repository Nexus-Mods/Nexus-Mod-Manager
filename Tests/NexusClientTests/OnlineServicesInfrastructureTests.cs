using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.OnlineServices.Infrastructure;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>
    /// Characterizes the website-neutral HTTP, JSON, error, and diagnostic infrastructure.
    /// </summary>
    public class OnlineServicesInfrastructureTests
    {
        /// <summary>
        /// Ensures HTTP status, body, and headers are buffered without provider-specific interpretation.
        /// </summary>
        [Test]
        public void Transport_BuffersResponseWithoutInterpretingHttpStatus()
        {
            var handler = new StubHttpMessageHandler((request, token) =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("limited"),
                    ReasonPhrase = "Too Many Requests"
                };
                response.Headers.Add("X-Test-Quota", "4");
                return Task.FromResult(response);
            });

            using (var transport = new ApiTransport(handler, TimeSpan.FromSeconds(5)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api"))
            {
                ApiResponse response = transport.SendAsync(request).GetAwaiter().GetResult();
                Assert.AreEqual((HttpStatusCode)429, response.StatusCode);
                Assert.IsFalse(response.IsSuccessStatusCode);
                Assert.AreEqual("limited", response.Content);
                Assert.IsTrue(response.TryGetHeader("x-test-quota", out string[] values));
                CollectionAssert.AreEqual(new[] { "4" }, values);
            }
        }

        /// <summary>
        /// Ensures explicit caller cancellation is distinguishable from a transport timeout.
        /// </summary>
        [Test]
        public void Transport_CallerCancellationUsesCancelledKind()
        {
            var handler = CreateBlockingHandler();
            using (var transport = new ApiTransport(handler, TimeSpan.FromSeconds(5)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api"))
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                ApiException exception = Assert.Throws<ApiException>(() => transport.SendAsync(request, cancellation.Token).GetAwaiter().GetResult());
                Assert.AreEqual(ApiErrorKind.Cancelled, exception.ErrorKind);
            }
        }

        /// <summary>
        /// Ensures expiry of the transport timeout is reported separately from caller cancellation.
        /// </summary>
        [Test]
        public void Transport_TimeoutUsesTimeoutKind()
        {
            var handler = CreateBlockingHandler();
            using (var transport = new ApiTransport(handler, TimeSpan.FromMilliseconds(50)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api"))
            {
                ApiException exception = Assert.Throws<ApiException>(() => transport.SendAsync(request).GetAwaiter().GetResult());
                Assert.AreEqual(ApiErrorKind.Timeout, exception.ErrorKind);
            }
        }

        /// <summary>
        /// Ensures transport-level HTTP failures are normalized without inventing provider semantics.
        /// </summary>
        [Test]
        public void Transport_HttpRequestFailureUsesNetworkKind()
        {
            var handler = new StubHttpMessageHandler((request, token) => Fault<HttpResponseMessage>(new HttpRequestException("offline")));
            using (var transport = new ApiTransport(handler, TimeSpan.FromSeconds(5)))
            using (var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test/api"))
            {
                ApiException exception = Assert.Throws<ApiException>(() => transport.SendAsync(request).GetAwaiter().GetResult());
                Assert.AreEqual(ApiErrorKind.Network, exception.ErrorKind);
                Assert.IsInstanceOf<HttpRequestException>(exception.InnerException);
            }
        }

        /// <summary>
        /// Ensures standard credential headers and provider-selected secret headers are redacted.
        /// </summary>
        [Test]
        public void Diagnostics_RedactsStandardAndCallerSelectedHeaders()
        {
            var headers = new[]
            {
                new KeyValuePair<string, IEnumerable<string>>("Authorization", new[] { "Bearer secret" }),
                new KeyValuePair<string, IEnumerable<string>>("X-Api-Key", new[] { "provider-secret" }),
                new KeyValuePair<string, IEnumerable<string>>("Accept", new[] { "application/json" })
            };

            IReadOnlyDictionary<string, string[]> sanitized = ApiDiagnosticSanitizer.SanitizeHeaders(headers, new[] { "X-Api-Key" });
            CollectionAssert.AreEqual(new[] { "<redacted>" }, sanitized["Authorization"]);
            CollectionAssert.AreEqual(new[] { "<redacted>" }, sanitized["X-Api-Key"]);
            CollectionAssert.AreEqual(new[] { "application/json" }, sanitized["Accept"]);
        }

        /// <summary>
        /// Ensures only caller-selected query parameters are removed from diagnostic URLs.
        /// </summary>
        [Test]
        public void Diagnostics_RedactsSelectedQueryParameters()
        {
            var uri = new Uri("https://example.test/file?id=42&key=secret&expires=123");
            string sanitized = ApiDiagnosticSanitizer.SanitizeUri(uri, new[] { "key" });

            StringAssert.Contains("id=42", sanitized);
            StringAssert.DoesNotContain("secret", sanitized);
            StringAssert.Contains("expires=123", sanitized);
            StringAssert.Contains("key=", sanitized);
            StringAssert.Contains("redacted", sanitized);
        }

        /// <summary>
        /// Ensures signed download diagnostics can redact authorization values without knowing provider-specific parameter names.
        /// </summary>
        [Test]
        public void Diagnostics_RedactsAllQueryValuesForSignedUrls()
        {
            var uri = new Uri("https://cdn.example.test/file?X-Amz-Credential=cred-secret-value&X-Amz-Signature=sig-secret-value&custom_auth=custom-secret-value");
            string sanitized = ApiDiagnosticSanitizer.SanitizeUri(uri, null, true);

            StringAssert.DoesNotContain("cred-secret-value", sanitized);
            StringAssert.DoesNotContain("sig-secret-value", sanitized);
            StringAssert.DoesNotContain("custom-secret-value", sanitized);
            StringAssert.Contains("X-Amz-Credential=", sanitized);
            StringAssert.Contains("X-Amz-Signature=", sanitized);
            StringAssert.Contains("custom_auth=", sanitized);
        }

        /// <summary>
        /// Ensures the shared JSON helper round-trips simple provider DTOs using Newtonsoft.Json.
        /// </summary>
        [Test]
        public void Json_RoundTripsSimplePayload()
        {
            var source = new TestPayload { Id = 7, Name = "sample" };
            string json = ApiJson.Serialize(source);
            TestPayload result = ApiJson.Deserialize<TestPayload>(json);

            Assert.AreEqual(source.Id, result.Id);
            Assert.AreEqual(source.Name, result.Name);
        }

        /// <summary>
        /// Creates a handler that remains pending until its cancellation token is signalled.
        /// </summary>
        private static StubHttpMessageHandler CreateBlockingHandler()
        {
            return new StubHttpMessageHandler(async (request, token) =>
            {
                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });
        }

        /// <summary>
        /// Creates a faulted task without relying on provider or runtime-specific HTTP behavior.
        /// </summary>
        private static Task<T> Fault<T>(Exception exception)
        {
            var completion = new TaskCompletionSource<T>();
            completion.SetException(exception);
            return completion.Task;
        }

        /// <summary>
        /// Provides deterministic HTTP responses for transport tests without external network access.
        /// </summary>
        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

            /// <summary>
            /// Initializes the handler with the response function used by the test.
            /// </summary>
            public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
            {
                _send = send ?? throw new ArgumentNullException(nameof(send));
            }

            /// <summary>
            /// Delegates request execution to the configured test response function.
            /// </summary>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return _send(request, cancellationToken);
            }
        }

        /// <summary>
        /// Provides a minimal JSON payload used to verify serializer ownership and behavior.
        /// </summary>
        private sealed class TestPayload
        {
            /// <summary>
            /// Gets or sets the test identifier.
            /// </summary>
            public int Id { get; set; }

            /// <summary>
            /// Gets or sets the test display name.
            /// </summary>
            public string Name { get; set; }
        }
    }
}
