namespace Nexus.Client.OnlineServices.NexusMods
{
    /// <summary>
    /// Identifies independently tracked Nexus Mods API surfaces.
    /// </summary>
    public enum NexusApiSurface
    {
        /// <summary>
        /// Legacy REST v1 endpoints.
        /// </summary>
        V1,

        /// <summary>
        /// Nexus Mods GraphQL endpoints.
        /// </summary>
        GraphQl,

        /// <summary>
        /// Nexus Mods REST v3 endpoints.
        /// </summary>
        V3
    }
}
