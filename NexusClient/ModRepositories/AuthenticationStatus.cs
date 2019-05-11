namespace Nexus.Client.ModRepositories
{
    /// <summary>
    /// Different possible statuses of an authentication attempt.
    /// </summary>
    public enum AuthenticationStatus
    {
        /// <summary>
        /// OK.
        /// </summary>
        Successful,

        /// <summary>
        /// Invalid API key.
        /// </summary>
        InvalidKey,

        /// <summary>
        /// Network problem.
        /// </summary>
        NetworkError,

        /// <summary>
        /// Unknown authentication problem.
        /// </summary>
        Unknown
    }
}
