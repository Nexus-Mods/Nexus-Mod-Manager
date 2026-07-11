namespace Nexus.Client.ModManagement
{
    /// <summary>
    /// Tracks one compatibility virtual deployment operation without owning caller policy or persistence.
    /// </summary>
    internal interface IVirtualDeploymentSession
    {
        void SetSourceRoot(string sourceRoot);
        void SetFileCount(int fileCount);
        void RecordLinkedFile(string linkedFilePath);
        void RecordPluginCandidate(string linkedFilePath);
        void TraceSummary();
    }
}
