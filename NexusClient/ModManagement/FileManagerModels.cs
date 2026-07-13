namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;

    public enum FileManagerSource
    {
        InstalledByNmm,
        BaseGame,
        Creations,
        ExternalModManager,
        Untracked
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
        public FileManagerOwnerCandidate(string ownerKey, string modName, int priority)
            : this(ownerKey, modName, priority, String.Empty)
        {
        }

        public FileManagerOwnerCandidate(string ownerKey, string modName, int priority, string previewFilePath)
        {
            OwnerKey = ownerKey;
            ModName = modName;
            Priority = priority;
            PreviewFilePath = previewFilePath ?? String.Empty;
        }

        public string OwnerKey { get; private set; }
        public string ModName { get; private set; }
        public int Priority { get; private set; }
        public string PreviewFilePath { get; private set; }
    }

    public sealed class FileManagerRow : INotifyPropertyChanged
    {
        public static readonly List<FileManagerOwnerCandidate> EmptyOwnerCandidates = new List<FileManagerOwnerCandidate>(0);

        private FileManagerSource _source;
        private bool _sourceEditable;
        private string _ownerKey;
        private string _ownerName;
        private List<FileManagerOwnerCandidate> _ownerCandidates = EmptyOwnerCandidates;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FullPath { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public long RawSize { get; set; }
        public string SizeDisplay { get; set; }
        public string RelativePath { get; set; }
        public string NormalizedRelativePath { get; set; }

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

        public List<FileManagerOwnerCandidate> OwnerCandidates
        {
            get { return _ownerCandidates; }
            set
            {
                _ownerCandidates = value ?? EmptyOwnerCandidates;
                OnPropertyChanged("OwnerCandidates");
                OnPropertyChanged("OwnerEditable");
            }
        }

        public bool OwnerEditable
        {
            get { return Source == FileManagerSource.InstalledByNmm && OwnerCandidates.Count > 0; }
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

    public sealed class FileManagerScanDiagnostics
    {
        public long VirtualLinkIndexMilliseconds { get; set; }
        public long BaseFileIndexMilliseconds { get; set; }
        public long ManualSourceLoadMilliseconds { get; set; }
        public long FileEnumerationMilliseconds { get; set; }
        public long FileMetadataMilliseconds { get; set; }
        public long ClassificationMilliseconds { get; set; }
        public long IndexConstructionMilliseconds { get; set; }
        public long TotalMilliseconds { get; set; }

        public override string ToString()
        {
            return String.Format("total={0}ms, links={1}ms, base={2}ms, manual={3}ms, enum={4}ms, metadata={5}ms, classify={6}ms, indexes={7}ms",
                TotalMilliseconds,
                VirtualLinkIndexMilliseconds,
                BaseFileIndexMilliseconds,
                ManualSourceLoadMilliseconds,
                FileEnumerationMilliseconds,
                FileMetadataMilliseconds,
                ClassificationMilliseconds,
                IndexConstructionMilliseconds);
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