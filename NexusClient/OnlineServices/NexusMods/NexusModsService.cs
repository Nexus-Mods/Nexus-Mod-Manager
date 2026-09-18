using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using Nexus.Client.OnlineServices.NexusMods.GraphQl;
using Nexus.Client.OnlineServices.NexusMods.V1;
using Nexus.Client.OnlineServices.NexusMods.V3;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Owns Nexus Mods session, request-policy, rate-limit, and error-mapping behavior shared by protocol-specific clients.
    /// </summary>
    public sealed class NexusModsService : IDisposable
    {
        private readonly object _sync = new object();
        private readonly ApiTransport _transport;
        private readonly bool _ownsTransport;
        private readonly List<NexusSessionContext> _retiredSessions = new List<NexusSessionContext>();
        private readonly Dictionary<NexusSessionContext, int> _inFlightRequests = new Dictionary<NexusSessionContext, int>();
        private NexusSessionContext _session;
        private long _generation;
        private bool _disposed;

        /// <summary>
        /// Initializes a Nexus Mods service around an existing provider-neutral transport.
        /// </summary>
        public NexusModsService(ApiTransport transport, string userAgent, string applicationName, string applicationVersion)
            : this(transport, userAgent, applicationName, applicationVersion, false)
        {
        }

        /// <summary>
        /// Initializes a Nexus Mods service which owns its transport.
        /// </summary>
        public NexusModsService(TimeSpan timeout, string userAgent, string applicationName, string applicationVersion)
            : this(new ApiTransport(timeout), userAgent, applicationName, applicationVersion, true)
        {
        }

        /// <summary>
        /// Initializes the shared service state and records transport ownership.
        /// </summary>
        private NexusModsService(ApiTransport transport, string userAgent, string applicationName, string applicationVersion, bool ownsTransport)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _ownsTransport = ownsTransport;
            RequestPolicy = new NexusRequestPolicy(userAgent, applicationName, applicationVersion);
            RateLimits = new NexusRateLimitTracker();
            V1 = new NexusV1Client(this);
            GraphQl = new NexusGraphQlClient(this);
            Collections = new NexusCollectionsClient(this);
            LegacyModsGraphQl = new NexusLegacyModGraphQlClient(this);
            V3 = new NexusV3Client(this);
            _session = new NexusSessionContext(0, NexusCredentials.None);
            RateLimits.Reset(0);
        }

        /// <summary>
        /// Gets the Nexus-specific request policy.
        /// </summary>
        public NexusRequestPolicy RequestPolicy { get; }

        /// <summary>
        /// Gets independently tracked Nexus API rate-limit snapshots.
        /// </summary>
        public NexusRateLimitTracker RateLimits { get; }

        /// <summary>
        /// Gets the NMM-owned REST v1 client.
        /// </summary>
        public NexusV1Client V1 { get; }

        /// <summary>
        /// Gets the NMM-owned GraphQL v2 protocol client.
        /// </summary>
        public NexusGraphQlClient GraphQl { get; }

        /// <summary>
        /// Gets the NMM-owned Nexus Collections provider.
        /// </summary>
        public INexusCollectionsProvider Collections { get; }

        /// <summary>
        /// Gets the Nexus GraphQL bulk resolver for legacy domain/mod identities.
        /// </summary>
        internal NexusLegacyModGraphQlClient LegacyModsGraphQl { get; }

        /// <summary>
        /// Gets the NMM-owned REST v3 protocol client.
        /// </summary>
        public NexusV3Client V3 { get; }

        /// <summary>
        /// Captures the current immutable session generation for diagnostic or coordination purposes.
        /// </summary>
        public NexusSessionContext CaptureSession()
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                return _session;
            }
        }

        /// <summary>
        /// Replaces the current credentials, invalidates in-flight work from the previous generation, and clears credential-scoped quota state.
        /// </summary>
        public NexusSessionContext ReplaceCredentials(NexusCredentials credentials)
        {
            if (credentials == null)
                throw new ArgumentNullException(nameof(credentials));

            NexusSessionContext previous;
            NexusSessionContext current;
            lock (_sync)
            {
                ThrowIfDisposed();
                previous = _session;
                _generation = checked(_generation + 1);
                current = new NexusSessionContext(_generation, credentials);
                _session = current;
                RateLimits.Reset(_generation);
            }

            try
            {
                previous.Cancel();
            }
            finally
            {
                bool disposePrevious;
                lock (_sync)
                {
                    disposePrevious = _disposed || !_inFlightRequests.ContainsKey(previous);
                    if (!disposePrevious)
                        _retiredSessions.Add(previous);
                }

                if (disposePrevious)
                    previous.Dispose();
            }

            return current;
        }

        /// <summary>
        /// Clears authentication through the same generation-changing lifecycle as a login or credential replacement.
        /// </summary>
        public NexusSessionContext ClearCredentials()
        {
            return ReplaceCredentials(NexusCredentials.None);
        }

        /// <summary>
        /// Sends one Nexus Mods request using credentials captured from the current session generation.
        /// Non-success HTTP responses are mapped to NMM-owned errors after quota headers are observed.
        /// </summary>
        public async Task<ApiResponse> SendAsync(NexusApiSurface surface, HttpMethod method, Uri uri, HttpContent content = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            NexusSessionContext session = AcquireRequestSession();
            try
            {
                using (HttpRequestMessage request = RequestPolicy.CreateRequest(method, uri, session.Credentials, content))
                using (var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Token))
                {
                    ApiResponse response;
                    try
                    {
                        response = await _transport.SendAsync(request, linkedCancellation.Token).ConfigureAwait(false);
                    }
                    catch (ApiException ex)
                    {
                        if (ex.ErrorKind == ApiErrorKind.Cancelled && !cancellationToken.IsCancellationRequested && !IsCurrent(session))
                            throw new ApiException(ApiErrorKind.Cancelled, "The Nexus Mods session changed before the request completed.", innerException: ex);

                        throw;
                    }

                    if (!IsCurrent(session))
                        throw new ApiException(ApiErrorKind.Cancelled, "The Nexus Mods session changed before the request completed.");

                    if (!RateLimits.Update(surface, session.Generation, response) || !IsCurrent(session))
                        throw new ApiException(ApiErrorKind.Cancelled, "The Nexus Mods session changed before the request completed.");

                    if (!response.IsSuccessStatusCode)
                        throw surface == NexusApiSurface.V3
                            ? NexusV3ErrorMapper.CreateException(response)
                            : NexusErrorMapper.CreateException(response);

                    return response;
                }
            }
            finally
            {
                ReleaseRequestSession(session);
            }
        }

        /// <summary>
        /// Releases the current session and any transport owned by this service.
        /// </summary>
        public void Dispose()
        {
            List<NexusSessionContext> sessions;
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                sessions = new List<NexusSessionContext>(_retiredSessions) { _session };
                _retiredSessions.Clear();
                _inFlightRequests.Clear();
                _session = null;
            }

            foreach (NexusSessionContext session in sessions)
            {
                session.Cancel();
                session.Dispose();
            }

            if (_ownsTransport)
                _transport.Dispose();
        }

        /// <summary>
        /// Captures the active session and records one in-flight request before credential replacement can retire it.
        /// </summary>
        private NexusSessionContext AcquireRequestSession()
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                NexusSessionContext session = _session;
                _inFlightRequests.TryGetValue(session, out int count);
                _inFlightRequests[session] = count + 1;
                return session;
            }
        }

        /// <summary>
        /// Releases one request lease and disposes a retired session as soon as its final request has completed.
        /// </summary>
        private void ReleaseRequestSession(NexusSessionContext session)
        {
            bool disposeSession = false;
            lock (_sync)
            {
                if (!_inFlightRequests.TryGetValue(session, out int count))
                    return;

                if (count > 1)
                {
                    _inFlightRequests[session] = count - 1;
                    return;
                }

                _inFlightRequests.Remove(session);
                if (_retiredSessions.Remove(session))
                    disposeSession = true;
            }

            if (disposeSession)
                session.Dispose();
        }

        /// <summary>
        /// Gets whether a captured session is still the active generation.
        /// </summary>
        private bool IsCurrent(NexusSessionContext session)
        {
            lock (_sync)
                return !_disposed && ReferenceEquals(_session, session) && _session.Generation == session.Generation;
        }

        /// <summary>
        /// Prevents use after the service has released its session state.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NexusModsService));
        }
    }
}
