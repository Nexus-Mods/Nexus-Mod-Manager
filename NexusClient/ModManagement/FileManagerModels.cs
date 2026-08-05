namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;

    using Nexus.Client.Util;

    public enum FileManagerSource
    {
        InstalledByNmm,
        BaseGame,
        Creations,
        ExternalModManager,
        Untracked
    }

    /// <summary>
    /// Represents the compact internal state used to display a File Manager link type.
    /// </summary>
    internal enum FileManagerLinkTypeState
    {
        Pending,
        Unavailable,
        NotFound,
        SymbolicLink,
        HardLink,
        Real
    }

    public sealed class FileManagerSourceOption
    {
        public FileManagerSourceOption(FileManagerSource source, string displayText)
        {
            Source = source;
            DisplayText = displayText;
        }

        public FileManagerSource Source { get; private set; }
        public string DisplayText { get; private set; }
    }

    public sealed class FileManagerOwnerCandidate
    {
        private string _previewFilePath;
        private readonly string _previewSourcePath;
        private readonly IList<string> _previewSourceRoots;
        private bool _previewFilePathResolved;

        public FileManagerOwnerCandidate(string ownerKey, string modName, int priority)
            : this(ownerKey, modName, priority, String.Empty)
        {
        }

        public FileManagerOwnerCandidate(string ownerKey, string modName, int priority, string previewFilePath)
        {
            OwnerKey = ownerKey;
            ModName = modName;
            Priority = priority;
            _previewFilePath = previewFilePath ?? String.Empty;
            _previewFilePathResolved = true;
        }

        /// <summary>
        /// Initializes an owner candidate whose preview path is resolved only when first requested.
        /// </summary>
        /// <param name="ownerKey">The stable owner identifier.</param>
        /// <param name="modName">The display name of the owning mod.</param>
        /// <param name="priority">The virtual-link priority.</param>
        /// <param name="previewSourcePath">The source path recorded by the virtual link.</param>
        /// <param name="previewSourceRoots">The roots against which relative source paths should be resolved.</param>
        internal FileManagerOwnerCandidate(string ownerKey, string modName, int priority, string previewSourcePath, IList<string> previewSourceRoots)
        {
            OwnerKey = ownerKey;
            ModName = modName;
            Priority = priority;
            _previewSourcePath = previewSourcePath ?? String.Empty;
            _previewSourceRoots = previewSourceRoots;
        }

        public string OwnerKey { get; private set; }
        public string ModName { get; private set; }
        public int Priority { get; private set; }
        public string PreviewFilePath
        {
            get { return ResolvePreviewFilePath(); }
        }

        /// <summary>
        /// Resolves and caches the first existing preview path for this owner.
        /// </summary>
        /// <returns>The resolved source file path, or an empty string when no source file exists.</returns>
        private string ResolvePreviewFilePath()
        {
            if (_previewFilePathResolved)
                return _previewFilePath ?? String.Empty;

            Stopwatch watch = Stopwatch.StartNew();
            int probeCount = 0;
            _previewFilePathResolved = true;
            _previewFilePath = String.Empty;
            try
            {
                if (String.IsNullOrWhiteSpace(_previewSourcePath))
                    return _previewFilePath;

                if (System.IO.Path.IsPathRooted(_previewSourcePath))
                {
                    probeCount++;
                    if (System.IO.File.Exists(_previewSourcePath))
                        _previewFilePath = _previewSourcePath;
                    return _previewFilePath;
                }

                if (_previewSourceRoots != null)
                {
                    foreach (string sourceRoot in _previewSourceRoots)
                    {
                        if (String.IsNullOrWhiteSpace(sourceRoot))
                            continue;

                        string filePath = System.IO.Path.Combine(sourceRoot, _previewSourcePath);
                        probeCount++;
                        if (!System.IO.File.Exists(filePath))
                            continue;

                        _previewFilePath = filePath;
                        break;
                    }
                }
            }
            catch
            {
                _previewFilePath = String.Empty;
            }
            finally
            {
                watch.Stop();
                Trace.TraceInformation("File Manager preview path resolution completed. Owner={0}, probes={1}, found={2}, elapsed={3}ms.", OwnerKey, probeCount, !String.IsNullOrEmpty(_previewFilePath), watch.ElapsedMilliseconds);
            }

            return _previewFilePath;
        }
    }

    public sealed class FileManagerRow : INotifyPropertyChanged
    {
        public static readonly List<FileManagerOwnerCandidate> EmptyOwnerCandidates = new List<FileManagerOwnerCandidate>(0);

        private FileManagerSource _source;
        private bool _sourceEditable;
        private string _ownerKey;
        private string _ownerName;
        private FileManagerLinkTypeState _linkTypeState;
        private int _ownerCount;
        private List<FileManagerOwnerCandidate> _ownerCandidates = EmptyOwnerCandidates;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FullPath { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public long RawSize { get; set; }
        public string RelativePath { get; set; }

        /// <summary>
        /// Provides a compatibility alias for the normalized relative path without storing a duplicate per-row reference.
        /// </summary>
        public string NormalizedRelativePath
        {
            get { return RelativePath; }
            set { RelativePath = value; }
        }

        public string LinkType
        {
            get { return GetLinkTypeDisplayText(_linkTypeState); }
        }

        internal bool IsLinkTypePending
        {
            get { return _linkTypeState == FileManagerLinkTypeState.Pending; }
        }

        public FileManagerSource Source
        {
            get { return _source; }
            set
            {
                if (_source == value)
                    return;

                _source = value;
                OnPropertyChanged("Source");
                OnPropertyChanged("SourceDisplay");
                OnPropertyChanged("OwnerEditable");
            }
        }

        public bool SourceEditable
        {
            get { return _sourceEditable; }
            set
            {
                if (_sourceEditable == value)
                    return;

                _sourceEditable = value;
                OnPropertyChanged("SourceEditable");
            }
        }

        public string SourceDisplay
        {
            get { return FileManagerSourceDisplay.GetDisplayText(Source); }
        }

        public string OwnerKey
        {
            get { return _ownerKey; }
            set
            {
                if (String.Equals(_ownerKey, value, StringComparison.Ordinal))
                    return;

                _ownerKey = value;
                OnPropertyChanged("OwnerKey");
            }
        }

        public string OwnerName
        {
            get { return _ownerName; }
            set
            {
                if (String.Equals(_ownerName, value, StringComparison.Ordinal))
                    return;

                _ownerName = value;
                OnPropertyChanged("OwnerName");
            }
        }

        public int OwnerCount
        {
            get { return _ownerCount; }
        }

        public List<FileManagerOwnerCandidate> OwnerCandidates
        {
            get { return _ownerCandidates; }
            set
            {
                _ownerCandidates = value ?? EmptyOwnerCandidates;
                _ownerCount = _ownerCandidates.Count;
                OnPropertyChanged("OwnerCandidates");
                OnPropertyChanged("OwnerCount");
                OnPropertyChanged("OwnerEditable");
            }
        }

        public bool OwnerEditable
        {
            get { return Source == FileManagerSource.InstalledByNmm && OwnerCount > 1 && OwnerCandidates.Count > 1; }
        }

        /// <summary>
        /// Updates the number of distinct owners represented by this row.
        /// </summary>
        /// <param name="ownerCount">The number of distinct virtual-mod owners.</param>
        internal void SetOwnerCount(int ownerCount)
        {
            int normalizedCount = Math.Max(0, ownerCount);
            if (_ownerCount == normalizedCount)
                return;

            _ownerCount = normalizedCount;
            OnPropertyChanged("OwnerCount");
            OnPropertyChanged("OwnerEditable");
        }

        /// <summary>
        /// Converts a native file-link type to the compact state stored by File Manager rows.
        /// </summary>
        /// <param name="linkType">The detected native file-link type.</param>
        /// <returns>The corresponding File Manager link-type state.</returns>
        internal static FileManagerLinkTypeState GetLinkTypeState(FileLinkType linkType)
        {
            switch (linkType)
            {
                case FileLinkType.SymbolicLink:
                    return FileManagerLinkTypeState.SymbolicLink;
                case FileLinkType.HardLink:
                    return FileManagerLinkTypeState.HardLink;
                case FileLinkType.Real:
                    return FileManagerLinkTypeState.Real;
                default:
                    return FileManagerLinkTypeState.NotFound;
            }
        }

        /// <summary>
        /// Updates the compact link-type state and optionally raises a row-level notification.
        /// </summary>
        /// <param name="linkTypeState">The new compact link-type state.</param>
        /// <param name="raisePropertyChanged">Whether to notify bound controls immediately.</param>
        internal void SetLinkTypeState(FileManagerLinkTypeState linkTypeState, bool raisePropertyChanged)
        {
            if (_linkTypeState == linkTypeState)
                return;

            _linkTypeState = linkTypeState;
            if (raisePropertyChanged)
                OnPropertyChanged("LinkType");
        }

        /// <summary>
        /// Returns the shared display text associated with a compact link-type state.
        /// </summary>
        /// <param name="linkTypeState">The compact link-type state.</param>
        /// <returns>The text displayed in the Link Type column.</returns>
        private static string GetLinkTypeDisplayText(FileManagerLinkTypeState linkTypeState)
        {
            switch (linkTypeState)
            {
                case FileManagerLinkTypeState.Unavailable:
                    return FileManagerLinkTypeResolver.UnavailableDisplayText;
                case FileManagerLinkTypeState.NotFound:
                    return "NotFound";
                case FileManagerLinkTypeState.SymbolicLink:
                    return "SymbolicLink";
                case FileManagerLinkTypeState.HardLink:
                    return "HardLink";
                case FileManagerLinkTypeState.Real:
                    return "Real";
                default:
                    return FileManagerLinkTypeResolver.PendingDisplayText;
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class FileManagerSourceDisplay
    {
        public static readonly IList<FileManagerSourceOption> ManualSourceOptions = new List<FileManagerSourceOption>
        {
            new FileManagerSourceOption(FileManagerSource.Untracked, "Untracked"),
            new FileManagerSourceOption(FileManagerSource.BaseGame, "Base Game"),
            new FileManagerSourceOption(FileManagerSource.Creations, "Creations"),
            new FileManagerSourceOption(FileManagerSource.ExternalModManager, "External Mod Manager")
        }.AsReadOnly();

        public static readonly IList<FileManagerSourceOption> AllSourceOptions = new List<FileManagerSourceOption>
        {
            new FileManagerSourceOption(FileManagerSource.InstalledByNmm, "Installed by NMM"),
            new FileManagerSourceOption(FileManagerSource.BaseGame, "Base Game"),
            new FileManagerSourceOption(FileManagerSource.Creations, "Creations"),
            new FileManagerSourceOption(FileManagerSource.ExternalModManager, "External Mod Manager"),
            new FileManagerSourceOption(FileManagerSource.Untracked, "Untracked")
        }.AsReadOnly();

        public static string GetDisplayText(FileManagerSource source)
        {
            switch (source)
            {
                case FileManagerSource.InstalledByNmm:
                    return "Installed by NMM";
                case FileManagerSource.BaseGame:
                    return "Base Game";
                case FileManagerSource.Creations:
                    return "Creations";
                case FileManagerSource.ExternalModManager:
                    return "External Mod Manager";
                default:
                    return "Untracked";
            }
        }

        public static bool IsManualSource(FileManagerSource source)
        {
            return source == FileManagerSource.Untracked || source == FileManagerSource.BaseGame || source == FileManagerSource.Creations || source == FileManagerSource.ExternalModManager;
        }
    }

    /// <summary>
    /// Records File Manager scan, ownership, publication, and background link-type diagnostics.
    /// </summary>
    public sealed class FileManagerScanDiagnostics
    {
        public long VirtualLinkIndexMilliseconds { get; set; }
        public long VirtualLinkSnapshotMilliseconds { get; set; }
        public long VirtualPathMappingMilliseconds { get; set; }
        public long DeploymentPathMappingMilliseconds { get; set; }
        public long OwnershipGroupingMilliseconds { get; set; }
        public long BaseFileIndexMilliseconds { get; set; }
        public long ManualSourceLoadMilliseconds { get; set; }
        public long FileEnumerationMilliseconds { get; set; }
        public long FileMetadataMilliseconds { get; set; }
        public long RowConstructionMilliseconds { get; set; }
        public long ClassificationMilliseconds { get; set; }
        public long IndexConstructionMilliseconds { get; set; }
        public long GridPublicationMilliseconds { get; set; }
        public long LinkTypeResolutionMilliseconds { get; set; }
        public long LinkTypeUiUpdateMilliseconds { get; set; }
        public long LinkTypeEndToEndMilliseconds { get; set; }
        internal long LinkTypeUiUpdateTicks { get; set; }
        internal long LinkTypeStartedTimestamp { get; set; }
        public long TotalMilliseconds { get; set; }
        public int EnumeratedFileCount { get; set; }
        public int PublishedFileCount { get; set; }
        public int NativeMetadataFileCount { get; set; }
        public int SkippedFileCount { get; set; }
        public int SkippedDirectoryCount { get; set; }
        public int SkippedReparseDirectoryCount { get; set; }
        public int ReparseFileCount { get; set; }
        public int PendingLinkTypeCount { get; set; }
        public int ResolvedLinkTypeCount { get; set; }
        public int RealFileCount { get; set; }
        public int HardLinkCount { get; set; }
        public int SymbolicLinkCount { get; set; }
        public int NotFoundLinkCount { get; set; }
        public int UnavailableLinkCount { get; set; }
        public int LinkTypeWorkerCount { get; set; }
        public int LinkTypeBatchCount { get; set; }
        public int VirtualLinkCount { get; set; }
        public int VirtualPathEntryCount { get; set; }
        public int DeploymentPathEntryCount { get; set; }
        public int MappedDeploymentPathEntryCount { get; set; }
        public int OwnershipLinkReferenceCount { get; set; }
        public int OwnershipPathCount { get; set; }
        public int ActiveOwnershipPathCount { get; set; }
        public int SingleOwnerPathCount { get; set; }
        public int ConflictingOwnerPathCount { get; set; }

        public override string ToString()
        {
            string timings = String.Format(
                "scan={0}ms, ownership={1}ms [snapshot={2}, virtualMap={3}, deploymentMap={4}, group={5}], base={6}ms, manual={7}ms, files=[enum={8}, metadata={9}, rows={10}, classify={11}, indexes={12}]ms, publish={13}ms, linkTypes=[resolve={14}, ui={15}, endToEnd={16}]ms",
                TotalMilliseconds,
                VirtualLinkIndexMilliseconds,
                VirtualLinkSnapshotMilliseconds,
                VirtualPathMappingMilliseconds,
                DeploymentPathMappingMilliseconds,
                OwnershipGroupingMilliseconds,
                BaseFileIndexMilliseconds,
                ManualSourceLoadMilliseconds,
                FileEnumerationMilliseconds,
                FileMetadataMilliseconds,
                RowConstructionMilliseconds,
                ClassificationMilliseconds,
                IndexConstructionMilliseconds,
                GridPublicationMilliseconds,
                LinkTypeResolutionMilliseconds,
                LinkTypeUiUpdateMilliseconds,
                LinkTypeEndToEndMilliseconds);
            string counts = String.Format(
                "files=[enumerated={0}, published={1}, metadataInline={2}, reparse={3}, skipped={4}, skippedDirs={5}, skippedReparseDirs={6}], ownership=[links={7}, virtualEntries={8}, deploymentEntries={9}, mappedDeployment={10}, references={11}, paths={12}, active={13}, single={14}, conflicts={15}], linkTypes=[pending={16}, resolved={17}, workers={18}, batches={19}, real={20}, hard={21}, symbolic={22}, notFound={23}, unavailable={24}]",
                EnumeratedFileCount,
                PublishedFileCount,
                NativeMetadataFileCount,
                ReparseFileCount,
                SkippedFileCount,
                SkippedDirectoryCount,
                SkippedReparseDirectoryCount,
                VirtualLinkCount,
                VirtualPathEntryCount,
                DeploymentPathEntryCount,
                MappedDeploymentPathEntryCount,
                OwnershipLinkReferenceCount,
                OwnershipPathCount,
                ActiveOwnershipPathCount,
                SingleOwnerPathCount,
                ConflictingOwnerPathCount,
                PendingLinkTypeCount,
                ResolvedLinkTypeCount,
                LinkTypeWorkerCount,
                LinkTypeBatchCount,
                RealFileCount,
                HardLinkCount,
                SymbolicLinkCount,
                NotFoundLinkCount,
                UnavailableLinkCount);
            return timings + ", " + counts;
        }
    }

    public sealed class FileManagerScanResult
    {
        public FileManagerScanResult(string deploymentRoot, List<FileManagerRow> rows, Dictionary<string, FileManagerRow> rowsByNormalizedPath, FileManagerSourceCounts counts, DateTime scannedAt, FileManagerScanDiagnostics diagnostics)
        {
            DeploymentRoot = deploymentRoot;
            Rows = rows ?? new List<FileManagerRow>();
            RowsByNormalizedPath = rowsByNormalizedPath ?? new Dictionary<string, FileManagerRow>(StringComparer.OrdinalIgnoreCase);
            Counts = counts ?? new FileManagerSourceCounts();
            ScannedAt = scannedAt;
            Diagnostics = diagnostics ?? new FileManagerScanDiagnostics();
        }

        public string DeploymentRoot { get; private set; }
        public List<FileManagerRow> Rows { get; private set; }
        public Dictionary<string, FileManagerRow> RowsByNormalizedPath { get; private set; }
        public FileManagerSourceCounts Counts { get; private set; }
        public DateTime ScannedAt { get; private set; }
        public FileManagerScanDiagnostics Diagnostics { get; private set; }
        public int TotalFiles { get { return Counts.Total; } }
        public int BaseGameFiles { get { return Counts.BaseGame; } }
        public int InstalledByNmmFiles { get { return Counts.InstalledByNmm; } }
        public int CreationsFiles { get { return Counts.Creations; } }
        public int ExternalModManagerFiles { get { return Counts.ExternalModManager; } }
        public int UntrackedFiles { get { return Counts.Untracked; } }
    }

    public sealed class FileManagerSourceCounts
    {
        public int Total { get; private set; }
        public int BaseGame { get; private set; }
        public int InstalledByNmm { get; private set; }
        public int Creations { get; private set; }
        public int ExternalModManager { get; private set; }
        public int Untracked { get; private set; }

        public void Add(FileManagerSource source)
        {
            Total++;
            Increment(source, 1);
        }

        public void Change(FileManagerSource oldSource, FileManagerSource newSource)
        {
            if (oldSource == newSource)
                return;

            Increment(oldSource, -1);
            Increment(newSource, 1);
        }

        public FileManagerSourceCounts Clone()
        {
            return new FileManagerSourceCounts
            {
                Total = Total,
                BaseGame = BaseGame,
                InstalledByNmm = InstalledByNmm,
                Creations = Creations,
                ExternalModManager = ExternalModManager,
                Untracked = Untracked
            };
        }

        private void Increment(FileManagerSource source, int amount)
        {
            switch (source)
            {
                case FileManagerSource.InstalledByNmm:
                    InstalledByNmm += amount;
                    break;
                case FileManagerSource.BaseGame:
                    BaseGame += amount;
                    break;
                case FileManagerSource.Creations:
                    Creations += amount;
                    break;
                case FileManagerSource.ExternalModManager:
                    ExternalModManager += amount;
                    break;
                default:
                    Untracked += amount;
                    break;
            }
        }
    }

    public sealed class VirtualFileOwnerSwitchResult
    {
        public static VirtualFileOwnerSwitchResult Failed(string message)
        {
            return new VirtualFileOwnerSwitchResult { FailureMessage = message };
        }

        public static VirtualFileOwnerSwitchResult Failed(Exception exception)
        {
            return new VirtualFileOwnerSwitchResult { Failure = exception, FailureMessage = exception == null ? null : exception.Message };
        }

        public static VirtualFileOwnerSwitchResult Succeeded(string relativePath, string selectedOwnerKey)
        {
            return new VirtualFileOwnerSwitchResult { Success = true, RelativePath = relativePath, SelectedOwnerKey = selectedOwnerKey };
        }

        public bool Success { get; private set; }
        public string RelativePath { get; private set; }
        public string SelectedOwnerKey { get; private set; }
        public string FailureMessage { get; private set; }
        public Exception Failure { get; private set; }
    }
}
