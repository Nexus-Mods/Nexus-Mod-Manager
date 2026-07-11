namespace Nexus.Client.ModManagement
{
    using System.Collections.Generic;

    public sealed class VirtualDeploymentResult
    {
        public VirtualDeploymentResult()
        {
            PluginCandidatePaths = new List<string>();
        }

        public string SourceRoot { get; internal set; }
        public int FileCount { get; internal set; }
        public int LinkedFileCount { get; internal set; }
        public IList<string> PluginCandidatePaths { get; private set; }
    }
}