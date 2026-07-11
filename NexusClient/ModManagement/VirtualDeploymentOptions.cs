namespace Nexus.Client.ModManagement
{
    using System;

    public sealed class VirtualDeploymentOptions
    {
        /// <summary>
        /// Notifies the caller immediately after each successful link. The return value identifies a caller-owned plugin candidate.
        /// </summary>
        public Func<string, bool> LinkedFileHandler { get; set; }

        /// <summary>
        /// Receives deployment progress without taking ownership of UI presentation.
        /// </summary>
        public Action<VirtualDeploymentProgress> Progress { get; set; }
    }
}
