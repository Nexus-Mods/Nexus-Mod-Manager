using System;
using System.Threading;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Captures immutable credentials and cancellation state for one generation of a Nexus Mods session.
    /// </summary>
    public sealed class NexusSessionContext
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly CancellationToken _token;
        private bool _disposed;

        /// <summary>
        /// Initializes a session generation.
        /// </summary>
        internal NexusSessionContext(long generation, NexusCredentials credentials)
        {
            Generation = generation;
            Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _cancellation = new CancellationTokenSource();
            _token = _cancellation.Token;
        }

        /// <summary>
        /// Gets the monotonically increasing session generation.
        /// </summary>
        public long Generation { get; }

        /// <summary>
        /// Gets the credentials captured by this session generation.
        /// </summary>
        public NexusCredentials Credentials { get; }

        /// <summary>
        /// Gets a token cancelled when this session generation is invalidated.
        /// </summary>
        public CancellationToken Token => _token;

        /// <summary>
        /// Cancels this session generation without exposing its cancellation source to callers.
        /// </summary>
        internal void Cancel()
        {
            if (!_disposed && !_cancellation.IsCancellationRequested)
                _cancellation.Cancel();
        }

        /// <summary>
        /// Releases the cancellation source associated with this session generation.
        /// </summary>
        internal void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Dispose();
        }
    }
}
