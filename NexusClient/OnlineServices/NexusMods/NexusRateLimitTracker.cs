using System;
using System.Globalization;
using System.Linq;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Tracks Nexus Mods rate limits independently per API surface and session generation.
    /// </summary>
    public sealed class NexusRateLimitTracker
    {
        private readonly object _sync = new object();
        private long _generation;
        private NexusRateLimitSnapshot _v1 = NexusRateLimitSnapshot.Empty;
        private NexusRateLimitSnapshot _graphQl = NexusRateLimitSnapshot.Empty;
        private NexusRateLimitSnapshot _collections = NexusRateLimitSnapshot.Empty;
        private NexusRateLimitSnapshot _v3 = NexusRateLimitSnapshot.Empty;

        /// <summary>
        /// Clears all credential-scoped quota information for a new session generation.
        /// </summary>
        public void Reset(long generation)
        {
            lock (_sync)
            {
                _generation = generation;
                _v1 = NexusRateLimitSnapshot.Empty;
                _graphQl = NexusRateLimitSnapshot.Empty;
                _collections = NexusRateLimitSnapshot.Empty;
                _v3 = NexusRateLimitSnapshot.Empty;
            }
        }

        /// <summary>
        /// Gets the latest known snapshot for an API surface.
        /// </summary>
        public NexusRateLimitSnapshot GetSnapshot(NexusApiSurface surface)
        {
            lock (_sync)
            {
                switch (surface)
                {
                    case NexusApiSurface.V1:
                        return _v1;
                    case NexusApiSurface.GraphQl:
                        return _graphQl;
                    case NexusApiSurface.Collections:
                        return _collections;
                    case NexusApiSurface.V3:
                        return _v3;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(surface));
                }
            }
        }

        /// <summary>
        /// Merges rate-limit headers from a response when it still belongs to the active session generation.
        /// Missing or malformed values are ignored, and stale responses cannot increase remaining quota within the same reset window.
        /// </summary>
        public bool Update(NexusApiSurface surface, long generation, ApiResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));

            NexusRateLimitSnapshot observed = ReadSnapshot(response);
            lock (_sync)
            {
                if (generation != _generation)
                    return false;

                switch (surface)
                {
                    case NexusApiSurface.V1:
                        _v1 = Merge(_v1, observed);
                        break;
                    case NexusApiSurface.GraphQl:
                        _graphQl = Merge(_graphQl, observed);
                        break;
                    case NexusApiSurface.Collections:
                        _collections = Merge(_collections, observed);
                        break;
                    case NexusApiSurface.V3:
                        _v3 = Merge(_v3, observed);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(surface));
                }

                return true;
            }
        }

        /// <summary>
        /// Reads the legacy Nexus rate-limit headers without requiring every header to be present.
        /// </summary>
        private static NexusRateLimitSnapshot ReadSnapshot(ApiResponse response)
        {
            return new NexusRateLimitSnapshot(
                ReadInt(response, "x-rl-daily-limit"),
                ReadInt(response, "x-rl-daily-remaining"),
                ReadDate(response, "x-rl-daily-reset"),
                ReadInt(response, "x-rl-hourly-limit"),
                ReadInt(response, "x-rl-hourly-remaining"),
                ReadDate(response, "x-rl-hourly-reset"));
        }

        /// <summary>
        /// Parses an integer header when available and valid.
        /// </summary>
        private static int? ReadInt(ApiResponse response, string name)
        {
            if (!response.TryGetHeader(name, out string[] values))
                return null;

            string value = values.FirstOrDefault();
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : (int?)null;
        }

        /// <summary>
        /// Parses a reset timestamp when available and valid.
        /// </summary>
        private static DateTimeOffset? ReadDate(ApiResponse response, string name)
        {
            if (!response.TryGetHeader(name, out string[] values))
                return null;

            string value = values.FirstOrDefault();
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset result))
                return result;

            return null;
        }

        /// <summary>
        /// Merges independently reset daily and hourly quota windows.
        /// </summary>
        private static NexusRateLimitSnapshot Merge(NexusRateLimitSnapshot current, NexusRateLimitSnapshot observed)
        {
            MergeWindow(current.DailyLimit, current.DailyRemaining, current.DailyReset,
                observed.DailyLimit, observed.DailyRemaining, observed.DailyReset,
                out int? dailyLimit, out int? dailyRemaining, out DateTimeOffset? dailyReset);
            MergeWindow(current.HourlyLimit, current.HourlyRemaining, current.HourlyReset,
                observed.HourlyLimit, observed.HourlyRemaining, observed.HourlyReset,
                out int? hourlyLimit, out int? hourlyRemaining, out DateTimeOffset? hourlyReset);

            return new NexusRateLimitSnapshot(dailyLimit, dailyRemaining, dailyReset, hourlyLimit, hourlyRemaining, hourlyReset);
        }

        /// <summary>
        /// Merges one quota window while preventing an older response from increasing the apparent remaining quota.
        /// </summary>
        private static void MergeWindow(int? currentLimit, int? currentRemaining, DateTimeOffset? currentReset,
            int? observedLimit, int? observedRemaining, DateTimeOffset? observedReset,
            out int? limit, out int? remaining, out DateTimeOffset? reset)
        {
            if (currentReset.HasValue && observedReset.HasValue && observedReset.Value < currentReset.Value)
            {
                limit = currentLimit;
                remaining = currentRemaining;
                reset = currentReset;
                return;
            }

            bool newWindow = currentReset.HasValue && observedReset.HasValue && observedReset.Value > currentReset.Value;
            limit = observedLimit ?? currentLimit;
            reset = observedReset ?? currentReset;

            if (newWindow || !currentRemaining.HasValue)
            {
                remaining = observedRemaining ?? currentRemaining;
                return;
            }

            if (!observedRemaining.HasValue)
            {
                remaining = currentRemaining;
                return;
            }

            remaining = Math.Min(currentRemaining.Value, observedRemaining.Value);
        }
    }
}
