using System;
using Newtonsoft.Json;
using Nexus.Client.OnlineServices.Infrastructure;

namespace Nexus.Client.OnlineServices.NexusMods.V3
{
    /// <summary>
    /// Maps REST v3 HTTP failures while retaining RFC 9457 Problem Details and validation metadata.
    /// </summary>
    internal static class NexusV3ErrorMapper
    {
        /// <summary>
        /// Creates a REST v3 exception for a non-success response.
        /// </summary>
        public static ApiException CreateException(ApiResponse response)
        {
            if (response == null)
                throw new ArgumentNullException(nameof(response));
            if (response.IsSuccessStatusCode)
                throw new ArgumentException("A successful response cannot be mapped to an API exception.", nameof(response));

            ApiException fallback = NexusErrorMapper.CreateException(response);
            NexusV3ProblemDetails problem = TryReadProblem(response.Content);
            string message = !string.IsNullOrWhiteSpace(problem?.Title)
                ? problem.Title
                : fallback.Message;

            return new NexusV3ApiException(fallback.ErrorKind, message, response.StatusCode, problem, fallback.RetryAfter);
        }

        /// <summary>
        /// Parses a v3 Problem Details payload without allowing malformed diagnostics to hide the HTTP failure.
        /// </summary>
        private static NexusV3ProblemDetails TryReadProblem(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                NexusV3ProblemDetails problem = ApiJson.Deserialize<NexusV3ProblemDetails>(content);
                if (problem == null ||
                    (string.IsNullOrWhiteSpace(problem.Type) && string.IsNullOrWhiteSpace(problem.Title) && problem.Status == null &&
                     string.IsNullOrWhiteSpace(problem.Detail) && string.IsNullOrWhiteSpace(problem.Instance) && problem.Errors == null))
                    return null;

                return problem;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
