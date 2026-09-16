namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Describes the credential mechanism used for a Nexus Mods session.
    /// </summary>
    public enum NexusCredentialKind
    {
        /// <summary>
        /// No credentials are available.
        /// </summary>
        None,

        /// <summary>
        /// A legacy Nexus Mods API key is used.
        /// </summary>
        ApiKey,

        /// <summary>
        /// A bearer token is used.
        /// </summary>
        BearerToken
    }
}
