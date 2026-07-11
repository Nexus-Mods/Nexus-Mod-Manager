namespace Nexus.Client.ModManagement
{
    using System.Diagnostics;

    using Nexus.Client.Mods;

    /// <summary>
    /// Behavior-preserving session boundary for one virtual deployment operation.
    /// </summary>
    internal sealed class VirtualDeploymentSession : IVirtualDeploymentSession
    {
        private readonly IMod _mod;
        private readonly Stopwatch _stopwatch;
        private string _sourceRoot;
        private int _fileCount;
        private int _linkedFileCount;
        private int _pluginCandidateCount;
        private bool _summaryTraced;

        public VirtualDeploymentSession(IMod mod)
        {
            _mod = mod;
            _stopwatch = Stopwatch.StartNew();
        }

        public void SetSourceRoot(string sourceRoot)
        {
            _sourceRoot = sourceRoot;
        }

        public void SetFileCount(int fileCount)
        {
            _fileCount = fileCount;
        }

        public void RecordLinkedFile(string linkedFilePath)
        {
            _linkedFileCount++;
        }

        public void RecordPluginCandidate(string linkedFilePath)
        {
            _pluginCandidateCount++;
        }

        public void TraceSummary()
        {
            if (_summaryTraced)
                return;

            _summaryTraced = true;
            _stopwatch.Stop();
            Trace.TraceInformation(
                "Virtual deployment activation: Mod={0}; SourceRoot={1}; Files={2}; Linked={3}; PluginCandidates={4}; InactiveLinks=unavailable; ElapsedMs={5}",
                _mod.Filename ?? _mod.ModName,
                _sourceRoot ?? string.Empty,
                _fileCount,
                _linkedFileCount,
                _pluginCandidateCount,
                _stopwatch.ElapsedMilliseconds);
        }
    }
}
