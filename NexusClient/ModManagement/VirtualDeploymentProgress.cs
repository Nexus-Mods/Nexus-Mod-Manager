namespace Nexus.Client.ModManagement
{
    public sealed class VirtualDeploymentProgress
    {
        public VirtualDeploymentProgress(string sourceRoot, int fileCount, int processedFileCount, string currentFilePath)
        {
            SourceRoot = sourceRoot;
            FileCount = fileCount;
            ProcessedFileCount = processedFileCount;
            CurrentFilePath = currentFilePath;
        }

        public string SourceRoot { get; private set; }
        public int FileCount { get; private set; }
        public int ProcessedFileCount { get; private set; }
        public string CurrentFilePath { get; private set; }
        public bool IsStarting => ProcessedFileCount == 0;
    }
}
