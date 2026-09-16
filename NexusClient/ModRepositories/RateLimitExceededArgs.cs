namespace Nexus.Client.ModRepositories
{
    using System;

    /// <summary>
    /// Arguments related to events for exceeding the rate limit.
    /// </summary>
    public class RateLimitExceededArgs : EventArgs
    {
        /// <summary>
        /// Current Rate Limit status.
        /// </summary>
        public RepositoryRateLimit RateLimit { get; }

        /// <summary>
        /// Creates a new <see cref="RateLimitExceededArgs"/>.
        /// </summary>
        /// <param name="rateLimit">Rate limit status when this event was invoked.</param>
        public RateLimitExceededArgs(RepositoryRateLimit rateLimit)
        {
            RateLimit = rateLimit;
        }
    }
}
