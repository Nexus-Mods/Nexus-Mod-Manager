using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Nexus.Client.OnlineServices.Infrastructure
{
    /// <summary>
    /// Executes provider-neutral HTTP requests with bounded timeouts and buffered responses.
    /// </summary>
    public sealed class ApiTransport : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly TimeSpan _defaultTimeout;
        private bool _disposed;

        /// <summary>
        /// Initializes a transport backed by a decompression-enabled HTTP handler with automatic redirects disabled.
        /// </summary>
        public ApiTransport(TimeSpan defaultTimeout)
            : this(CreateDefaultHandler(), defaultTimeout)
        {
        }

        /// <summary>
        /// Initializes a transport with a caller-supplied handler, primarily for provider customization and deterministic tests.
        /// </summary>
        public ApiTransport(HttpMessageHandler handler, TimeSpan defaultTimeout)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (defaultTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "The API timeout must be greater than zero.");

            _defaultTimeout = defaultTimeout;
            _httpClient = new HttpClient(handler, true)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        /// <summary>
        /// Sends a request and returns a fully buffered response without interpreting provider-specific HTTP semantics.
        /// </summary>
        public async Task<ApiResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            ThrowIfDisposed();
            var transportStopwatch = Stopwatch.StartNew();
            HttpStatusCode? observedStatus = null;
            using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                requestCancellation.CancelAfter(_defaultTimeout);
                try
                {
                    using (HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, requestCancellation.Token).ConfigureAwait(false))
                    {
                        observedStatus = response.StatusCode;
                        string content = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ApiResponse.FromHttpResponse(response, content);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new ApiException(ApiErrorKind.Cancelled, "The API request was cancelled by the caller.", innerException: ex);

                    throw new ApiException(ApiErrorKind.Timeout, "The API request exceeded the configured timeout.", innerException: ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new ApiException(ApiErrorKind.Network, "The API request failed because of a network error.", innerException: ex);
                }
                finally
                {
                    transportStopwatch.Stop();
                    if (transportStopwatch.ElapsedMilliseconds >= 250)
                    {
                        Trace.TraceInformation("NMM API transport completed: method={0}, status={1}, elapsedMs={2}.",
                            request.Method == null ? "unknown" : request.Method.Method,
                            observedStatus.HasValue ? ((int)observedStatus.Value).ToString() : "none",
                            transportStopwatch.ElapsedMilliseconds);
                    }
                }
            }
        }

        /// <summary>
        /// Releases the underlying HTTP client and handler owned by this transport.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _httpClient.Dispose();
        }

        /// <summary>
        /// Creates the default handler without automatic redirects so provider request policies remain authoritative for every destination.
        /// </summary>
        private static HttpMessageHandler CreateDefaultHandler()
        {
            return new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = false
            };
        }

        /// <summary>
        /// Prevents use after the transport has released its HTTP resources.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ApiTransport));
        }
    }
}
