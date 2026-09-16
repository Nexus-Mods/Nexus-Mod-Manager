using System;
using System.Net;

namespace Nexus.Client.OnlineServices.Infrastructure
{
    /// <summary>
    /// Represents an online-service failure using NMM-owned, provider-neutral error information.
    /// </summary>
    public class ApiException : Exception
    {
        /// <summary>
        /// Initializes an API exception with optional HTTP and provider metadata.
        /// </summary>
        public ApiException(ApiErrorKind errorKind, string message, HttpStatusCode? statusCode = null, string providerCode = null, TimeSpan? retryAfter = null, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorKind = errorKind;
            StatusCode = statusCode;
            ProviderCode = providerCode;
            RetryAfter = retryAfter;
        }

        /// <summary>
        /// Gets the provider-neutral failure category.
        /// </summary>
        public ApiErrorKind ErrorKind { get; }

        /// <summary>
        /// Gets the HTTP status when the failure originated from an HTTP response.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Gets an optional provider-specific error code.
        /// </summary>
        public string ProviderCode { get; }

        /// <summary>
        /// Gets the provider-supplied delay before a safe retry, when available.
        /// </summary>
        public TimeSpan? RetryAfter { get; }
    }
}
