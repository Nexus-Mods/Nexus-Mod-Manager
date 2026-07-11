namespace Nexus.Client.ModManagement
{
    using System;

    public sealed class VirtualDeploymentOptions
    {
        /// <summary>
        /// Lets the caller identify linked files that require caller-owned plugin handling.
        /// </summary>
        public Func<string, bool> IsPluginCandidate { get; set; }

        /// <summary>
        /// Receives deployment progress without taking ownership of UI presentation.
        /// </summary>
        public Action<VirtualDeploymentProgress> Progress { get; set; }
    }
}