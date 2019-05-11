namespace Nexus.Client.SSO
{
    /// <summary>
    /// Reasons for cancelling the authentication process.
    /// </summary>
    public enum AuthenticationCancelledReason
    {
        /// <summary>
        /// User initiated cancellation.
        /// </summary>
        Manual,

        /// <summary>
        /// Connection issue during authentication.
        /// </summary>
        ConnectionIssue,

        /// <summary>
        /// Unknown reason, check trace logs.
        /// </summary>
        Unknown
    }
}
