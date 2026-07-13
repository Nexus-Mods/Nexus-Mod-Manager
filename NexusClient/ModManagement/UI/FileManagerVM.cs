namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.Games;

    public sealed class FileManagerVM : INotifyPropertyChanged, IDisposable
    {
        private readonly ModManagerVM _modManagerViewModel;
        private readonly FileManagerQueryService _queryService;
        private readonly IVirtualDeploymentService _deploymentService;
        private readonly HashSet<IBackgroundTaskSet> _watchedActivationTasks = new HashSet<IBackgroundTaskSet>();
        private CancellationTokenSource _scanCancellation;
        private bool _loaded;
        private bool _scanning;
        private string _deploymentRoot;
        private string _statusMessage;
        private string _lastScannedDisplay;
        private int _totalFiles;
        private int _baseGameFiles;
        private int _installedByNmmFiles;
        private int _creationsFiles;
        private int _externalModManagerFiles;
        private int _untrackedFiles;

        public FileManagerVM(ModManagerVM modManagerViewModel)
            : this(modManagerViewModel, null)
        {
        }

        public FileManagerVM(ModManagerVM modManagerViewModel, IFileManagerManualSourceStore manualSourceStore)
        {
            if (modManagerViewModel == null) throw new ArgumentNullException("modManagerViewModel");
            _modManagerViewModel = modManagerViewModel;
            _queryService = new FileManagerQueryService(manualSourceStore ?? new SettingsFileManagerManualSourceStore(modManagerViewModel.Settings));
            _deploymentService = new VirtualDeploymentService(modManagerViewModel.VirtualModActivator);
            Rows = new BindingList<FileManagerRow>();
            StatusMessage = "Not scanned.";
            WatchModActivationQueue();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public BindingList<FileManagerRow> Rows { get; private set; }

        public IGameMode GameMode
        {
            get { return _modManagerViewModel.ModManager.GameMode; }
        }

        public string DeploymentRoot
        {
            get { return _deploymentRoot; }
            private set { if (_deploymentRoot != value) { _deploymentRoot = value; OnPropertyChanged("DeploymentRoot"); } }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            private set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged("StatusMessage"); } }
        }

        public string LastScannedDisplay
        {
            get { return _lastScannedDisplay; }
            private set { if (_lastScannedDisplay != value) { _lastScannedDisplay = value; OnPropertyChanged("LastScannedDisplay"); } }
        }

        public int TotalFiles
        {
            get { return _totalFiles; }
            private set { if (_totalFiles != value) { _totalFiles = value; OnPropertyChanged("TotalFiles"); } }
        }

        public int BaseGameFiles
        {
            get { return _baseGameFiles; }
            private set { if (_baseGameFiles != value) { _baseGameFiles = value; OnPropertyChanged("BaseGameFiles"); } }
        }

        public int InstalledByNmmFiles
        {
            get { return _installedByNmmFiles; }
            private set { if (_installedByNmmFiles != value) { _installedByNmmFiles = value; OnPropertyChanged("InstalledByNmmFiles"); } }
        }

        public int CreationsFiles
        {
            get { return _creationsFiles; }
            private set { if (_creationsFiles != value) { _creationsFiles = value; OnPropertyChanged("CreationsFiles"); } }
        }

        public int ExternalModManagerFiles
        {
            get { return _externalModManagerFiles; }
            private set { if (_externalModManagerFiles != value) { _externalModManagerFiles = value; OnPropertyChanged("ExternalModManagerFiles"); } }
        }

        public int UntrackedFiles
        {
            get { return _untrackedFiles; }
            private set { if (_untrackedFiles != value) { _untrackedFiles = value; OnPropertyChanged("UntrackedFiles"); } }
        }

        public bool IsScanning
        {
            get { return _scanning; }
            private set
            {
                if (_scanning != value)
                {
                    _scanning = value;
                    OnPropertyChanged("IsScanning");
                    OnPropertyChanged("CanChangeFileOwner");
                }
            }
        }

        public bool CanChangeFileOwner
        {
            get { return !IsScanning && !HasActiveOrQueuedInstallUninstallTasks(); }
        }

        public bool IsGamebryoMode
        {
            get { return IsGamebryoGameMode(GameMode); }
        }

        public async Task LoadIfNeededAsync()
        {
            if (_loaded)
                return;

            await RefreshAsync().ConfigureAwait(true);
        }

        public async Task RefreshAsync()
        {
            if (IsScanning)
                return;

            if (!IsGamebryoMode)
            {
                StatusMessage = "File Manager is available only for Gamebryo game modes.";
                return;
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            _scanCancellation = cancellation;
            IGameMode gameMode = GameMode;
            IsScanning = true;
            StatusMessage = "Scanning deployment files...";

            try
            {
                FileManagerScanResult result = await Task.Run(() => _queryService.Scan(gameMode, _modManagerViewModel.VirtualModActivator, cancellation.Token), cancellation.Token).ConfigureAwait(true);
                if (cancellation.IsCancellationRequested || !Object.ReferenceEquals(gameMode, GameMode))
                    return;

                ApplyScanResult(result);
                _loaded = true;
                StatusMessage = "Scan complete.";
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                Rows.Clear();
                throw;
            }
            finally
            {
                if (Object.ReferenceEquals(_scanCancellation, cancellation))
                    _scanCancellation = null;

                IsScanning = false;
                cancellation.Dispose();
            }
        }

        public Task<VirtualFileOwnerSwitchResult> SwitchOwnerAsync(FileManagerRow row, string selectedOwnerKey)
        {
            if (row == null) throw new ArgumentNullException("row");
            return Task.Run(() => _deploymentService.SwitchFileOwner(row.RelativePath, selectedOwnerKey));
        }

        public void SetManualSource(FileManagerRow row, FileManagerSource source, FileManagerSource previousSource)
        {
            if (row == null) throw new ArgumentNullException("row");

            _queryService.ChangeManualSource(GameMode.ModeId, row, source, previousSource);
            UpdateCountsFromRows();
        }

        public void RefreshRowOwnership(FileManagerRow row)
        {
            _queryService.RefreshRowOwnership(row, _modManagerViewModel.VirtualModActivator);
            UpdateCountsFromRows();
        }

        public void Dispose()
        {
            UnwatchModActivationQueue();

            CancellationTokenSource cancellation = _scanCancellation;
            if (cancellation != null)
                cancellation.Cancel();
        }

        private void WatchModActivationQueue()
        {
            if (_modManagerViewModel.ModManager == null || _modManagerViewModel.ModManager.ModActivationMonitor == null)
                return;

            _modManagerViewModel.ModManager.ModActivationMonitor.Tasks.CollectionChanged += ModActivationTasks_CollectionChanged;
            foreach (IBackgroundTaskSet task in _modManagerViewModel.ModManager.ModActivationMonitor.Tasks)
                WatchModActivationTask(task);
        }

        private void UnwatchModActivationQueue()
        {
            if (_modManagerViewModel.ModManager != null && _modManagerViewModel.ModManager.ModActivationMonitor != null)
                _modManagerViewModel.ModManager.ModActivationMonitor.Tasks.CollectionChanged -= ModActivationTasks_CollectionChanged;

            foreach (IBackgroundTaskSet task in _watchedActivationTasks)
                task.TaskSetCompleted -= ModActivationTaskSetCompleted;
            _watchedActivationTasks.Clear();
        }

        private void ModActivationTasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (IBackgroundTaskSet task in e.OldItems)
                    UnwatchModActivationTask(task);

            if (e.NewItems != null)
                foreach (IBackgroundTaskSet task in e.NewItems)
                    WatchModActivationTask(task);

            OnPropertyChanged("CanChangeFileOwner");
        }

        private void WatchModActivationTask(IBackgroundTaskSet task)
        {
            if (task == null || !IsInstallOrUninstallTask(task) || !_watchedActivationTasks.Add(task))
                return;

            task.TaskSetCompleted += ModActivationTaskSetCompleted;
        }

        private void UnwatchModActivationTask(IBackgroundTaskSet task)
        {
            if (task == null || !_watchedActivationTasks.Remove(task))
                return;

            task.TaskSetCompleted -= ModActivationTaskSetCompleted;
        }

        private void ModActivationTaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
        {
            OnPropertyChanged("CanChangeFileOwner");
        }

        private bool HasActiveOrQueuedInstallUninstallTasks()
        {
            if (_modManagerViewModel.ModManager == null || _modManagerViewModel.ModManager.ModActivationMonitor == null)
                return false;

            foreach (IBackgroundTaskSet task in _modManagerViewModel.ModManager.ModActivationMonitor.Tasks)
                if (IsInstallOrUninstallTask(task) && !task.IsCompleted)
                    return true;

            return false;
        }

        private static bool IsInstallOrUninstallTask(IBackgroundTaskSet task)
        {
            return task is ModInstaller || task is ModUninstaller || task is ModUpgrader;
        }

        private void ApplyScanResult(FileManagerScanResult result)
        {
            Rows.RaiseListChangedEvents = false;
            Rows.Clear();
            foreach (FileManagerRow row in result.Rows)
                Rows.Add(row);
            Rows.RaiseListChangedEvents = true;
            Rows.ResetBindings();

            DeploymentRoot = result.DeploymentRoot;
            UpdateCountsFromRows();
            LastScannedDisplay = result.ScannedAt.ToString("g");
        }

        private void UpdateCountsFromRows()
        {
            int total = 0;
            int baseGame = 0;
            int installed = 0;
            int creations = 0;
            int external = 0;
            int untracked = 0;

            foreach (FileManagerRow row in Rows)
            {
                total++;
                switch (row.Source)
                {
                    case FileManagerSource.InstalledByNmm:
                        installed++;
                        break;
                    case FileManagerSource.BaseGame:
                        baseGame++;
                        break;
                    case FileManagerSource.Creations:
                        creations++;
                        break;
                    case FileManagerSource.ExternalModManager:
                        external++;
                        break;
                    default:
                        untracked++;
                        break;
                }
            }

            TotalFiles = total;
            BaseGameFiles = baseGame;
            InstalledByNmmFiles = installed;
            CreationsFiles = creations;
            ExternalModManagerFiles = external;
            UntrackedFiles = untracked;
        }

        private static bool IsGamebryoGameMode(IGameMode gameMode)
        {
            Type type = gameMode == null ? null : gameMode.GetType();
            while (type != null)
            {
                if (String.Equals(type.Name, "GamebryoGameModeBase", StringComparison.OrdinalIgnoreCase))
                    return true;

                type = type.BaseType;
            }

            return false;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}