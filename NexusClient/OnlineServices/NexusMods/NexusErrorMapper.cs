using System;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Maps Nexus Mods HTTP failures into NMM-owned API errors without relying on response-message text classification.
    /// </summary>
    public static class NexusErrorMapper
    {
        /// <summary>
        /// Creates a provider-aware exception for a non-success Nexus Mods response.
        /// </summary>
        public static ApiException CreateException(ApiResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (response.IsSuccessStatusCode)
                throw new ArgumentException("A successful response cannot be mapped to an API exception.", nameof(response));

            string providerCode = null;
            string providerMessage = null;
            TryReadProviderError(response.Content, out providerCode, out providerMessage);
            string message = string.IsNullOrWhiteSpace(providerMessage)
                ? $"Nexus Mods returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.".TrimEnd()
                : providerMessage;

            return new ApiException(MapKind(response.StatusCode), message, response.StatusCode, providerCode, ReadRetryAfter(response));
        }

        /// <summary>
        /// Maps HTTP status codes to provider-neutral error categories.
        /// </summary>
        private static ApiErrorKind MapKind(HttpStatusCode statusCode)
        {
            int value = (int)statusCode;
            if (statusCode == HttpStatusCode.Unauthorized)
                return ApiErrorKind.Authentication;
            if (statusCode == HttpStatusCode.Forbidden)
                return ApiErrorKind.Forbidden;
            if (statusCode == HttpStatusCode.NotFound)
                return ApiErrorKind.NotFound;
            if (statusCode == (HttpStatusCode)429)
                return ApiErrorKind.RateLimit;
            if (statusCode == HttpStatusCode.RequestTimeout)
                return ApiErrorKind.Timeout;
            if (statusCode == HttpStatusCode.BadRequest || value == 409 || value == 422)
                return ApiErrorKind.Validation;
            if (value >= 500 && value <= 599)
                return ApiErrorKind.ServerError;

            return ApiErrorKind.Unknown;
        }

        /// <summary>
        /// Reads the Nexus v1-style code/message error payload when present, while tolerating non-JSON error bodies.
        /// </summary>
        private static void TryReadProviderError(string content, out string providerCode, out string providerMessage)
        {
            providerCode = null;
            providerMessage = null;
            if (string.IsNullOrWhiteSpace(content))
                return;

            try
            {
                JObject payload = JObject.Parse(content);
                providerCode = payload["code"]?.ToString();
                providerMessage = payload["message"]?.ToString();
                if (string.IsNullOrWhiteSpace(providerMessage))
                    providerMessage = payload["detail"]?.ToString() ?? payload["title"]?.ToString();
            }
            catch (JsonException)
            {
                // Provider error bodies are diagnostic input; an unexpected body must not hide the HTTP failure.
            }
        }

        /// <summary>
        /// Reads Retry-After in either delta-seconds or HTTP-date form.
        /// </summary>
        private static TimeSpan? ReadRetryAfter(ApiResponse response)
        {
            if (!response.TryGetHeader("Retry-After", out string[] values))
                return null;

            string value = values.FirstOrDefault();
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) && seconds >= 0)
                return TimeSpan.FromSeconds(seconds);

            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset retryAt))
            {
                TimeSpan delay = retryAt - DateTimeOffset.UtcNow;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }

            return null;
        }
    }
}
