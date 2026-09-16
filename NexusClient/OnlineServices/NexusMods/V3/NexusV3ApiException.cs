using System;
using System.Net;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods.V3
{
    /// <summary>
    /// Represents a Nexus Mods REST v3 failure while preserving RFC 9457 Problem Details metadata.
    /// </summary>
    public sealed class NexusV3ApiException : ApiException
    {
        /// <summary>
        /// Initializes the exception from the provider-neutral classification and optional v3 problem payload.
        /// </summary>
        public NexusV3ApiException(ApiErrorKind errorKind, string message, HttpStatusCode statusCode, NexusV3ProblemDetails problem, TimeSpan? retryAfter = null)
            : base(errorKind, message, statusCode, problem?.Type, retryAfter)
        {
            Problem = problem;
        }

        /// <summary>
        /// Gets the parsed RFC 9457 problem payload, or <c>null</c> when the provider returned another error representation.
        /// </summary>
        public NexusV3ProblemDetails Problem { get; }

        /// <summary>
        /// Gets the validation errors supplied by Nexus Mods, if any.
        /// </summary>
        public NexusV3ValidationProblemItem[] ValidationErrors => Problem?.Errors ?? new NexusV3ValidationProblemItem[0];
    }
}
