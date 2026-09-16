using System;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Describes the rate-limit values observed for one Nexus Mods API surface.
    /// Missing response values remain unknown rather than being invented.
    /// </summary>
    public sealed class NexusRateLimitSnapshot
    {
        /// <summary>
        /// Initializes a rate-limit snapshot.
        /// </summary>
        public NexusRateLimitSnapshot(int? dailyLimit, int? dailyRemaining, DateTimeOffset? dailyReset, int? hourlyLimit, int? hourlyRemaining, DateTimeOffset? hourlyReset)
        {
            DailyLimit = dailyLimit;
            DailyRemaining = dailyRemaining;
            DailyReset = dailyReset;
            HourlyLimit = hourlyLimit;
            HourlyRemaining = hourlyRemaining;
            HourlyReset = hourlyReset;
        }

        /// <summary>
        /// Gets an entirely unknown rate-limit snapshot.
        /// </summary>
        public static NexusRateLimitSnapshot Empty { get; } = new NexusRateLimitSnapshot(null, null, null, null, null, null);

        /// <summary>
        /// Gets the daily request limit when reported.
        /// </summary>
        public int? DailyLimit { get; }

        /// <summary>
        /// Gets the remaining daily requests when reported.
        /// </summary>
        public int? DailyRemaining { get; }

        /// <summary>
        /// Gets the daily reset time when reported.
        /// </summary>
        public DateTimeOffset? DailyReset { get; }

        /// <summary>
        /// Gets the hourly request limit when reported.
        /// </summary>
        public int? HourlyLimit { get; }

        /// <summary>
        /// Gets the remaining hourly requests when reported.
        /// </summary>
        public int? HourlyRemaining { get; }

        /// <summary>
        /// Gets the hourly reset time when reported.
        /// </summary>
        public DateTimeOffset? HourlyReset { get; }
    }
}
