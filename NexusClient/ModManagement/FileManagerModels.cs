namespace Nexus.Client.ModManagement
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;

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
        {
            OwnerKey = ownerKey;
            ModName = modName;
            Priority = priority;
        }

        public string OwnerKey { get; private set; }
        public string ModName { get; private set; }
        public int Priority { get; private set; }
    }

    public sealed class FileManagerRow : INotifyPropertyChanged
    {
        private FileManagerSource _source;
        private bool _sourceEditable;
        private string _ownerKey;
        private string _ownerName;
        private List<FileManagerOwnerCandidate> _ownerCandidates = new List<FileManagerOwnerCandidate>();

        public event PropertyChangedEventHandler PropertyChanged;

        public string FullPath { get; set; }
        public string FileName { get; set; }
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
                _ownerCandidates = value ?? new List<FileManagerOwnerCandidate>();
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
            return ManualSourceOptions.Any(x => x.Source == source);
        }
    }

    public sealed class FileManagerScanResult
    {
        public FileManagerScanResult(string deploymentRoot, List<FileManagerRow> rows, DateTime scannedAt)
        {
            DeploymentRoot = deploymentRoot;
            Rows = rows ?? new List<FileManagerRow>();
            ScannedAt = scannedAt;
        }

        public string DeploymentRoot { get; private set; }
        public List<FileManagerRow> Rows { get; private set; }
        public DateTime ScannedAt { get; private set; }
        public int TotalFiles { get { return Rows.Count; } }
        public int BaseGameFiles { get { return Rows.Count(x => x.Source == FileManagerSource.BaseGame); } }
        public int InstalledByNmmFiles { get { return Rows.Count(x => x.Source == FileManagerSource.InstalledByNmm); } }
        public int CreationsFiles { get { return Rows.Count(x => x.Source == FileManagerSource.Creations); } }
        public int ExternalModManagerFiles { get { return Rows.Count(x => x.Source == FileManagerSource.ExternalModManager); } }
        public int UntrackedFiles { get { return Rows.Count(x => x.Source == FileManagerSource.Untracked); } }
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