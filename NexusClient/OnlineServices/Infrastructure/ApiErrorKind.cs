namespace Nexus.Client.OnlineServices.Infrastructure
{
    /// <summary>
    /// Describes service and transport error categories without depending on a specific provider.
    /// </summary>
    public enum ApiErrorKind
    {
        /// <summary>
        /// The failure could not be classified more specifically.
        /// </summary>
        Unknown,

        /// <summary>
        /// The request failed because of an underlying network problem.
        /// </summary>
        Network,

        /// <summary>
        /// The request exceeded the configured timeout.
        /// </summary>
        Timeout,

        /// <summary>
        /// The caller explicitly cancelled the request.
        /// </summary>
        Cancelled,

        /// <summary>
        /// Authentication failed or is required.
        /// </summary>
        Authentication,

        /// <summary>
        /// The authenticated caller is not allowed to perform the operation.
        /// </summary>
        Forbidden,

        /// <summary>
        /// The requested resource was not found.
        /// </summary>
        NotFound,

        /// <summary>
        /// A provider rate limit prevented the operation.
        /// </summary>
        RateLimit,

        /// <summary>
        /// The provider rejected request data as invalid.
        /// </summary>
        Validation,

        /// <summary>
        /// The provider returned a response that could not be interpreted safely.
        /// </summary>
        InvalidResponse,

        /// <summary>
        /// The remote service failed while processing the request.
        /// </summary>
        ServerError
    }
}
