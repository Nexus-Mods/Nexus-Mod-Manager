namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.Games;

    public sealed class FileManagerVM : INotifyPropertyChanged, IDisposable
    {
        private readonly ModManagerVM _modManagerViewModel;
        private readonly FileManagerQueryService _queryService;
        private readonly IVirtualDeploymentService _deploymentService;
        private readonly SynchronizationContext _uiContext;
        private readonly HashSet<IBackgroundTaskSet> _watchedActivationTasks = new HashSet<IBackgroundTaskSet>();
        private CancellationTokenSource _scanCancellation;
        private CancellationTokenSource _linkTypeCancellation;
        private Task _linkTypeResolutionTask;
        private FileManagerSourceCounts _counts = new FileManagerSourceCounts();
        private bool _loaded;
        private bool _stale;
        private bool _scanning;
        private bool _resolvingLinkTypes;
        private bool _disposed;
        private int _scanGeneration;
        private int _dataChangeRevision;
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
            _uiContext = SynchronizationContext.Current;
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

        public bool IsResolvingLinkTypes
        {
            get { return _resolvingLinkTypes; }
            private set
            {
                if (_resolvingLinkTypes == value)
                    return;

                _resolvingLinkTypes = value;
                OnPropertyChanged("IsResolvingLinkTypes");
            }
        }

        public bool IsStale
        {
            get { return _stale; }
            private set
            {
                if (_stale != value)
                {
                    _stale = value;
                    OnPropertyChanged("IsStale");
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
            if (_disposed || IsScanning)
                return;

            if (!IsGamebryoMode)
            {
                StatusMessage = "File Manager is available only for Gamebryo game modes.";
                return;
            }

            CancelLinkTypeResolution();
            CancellationTokenSource previousCancellation = _scanCancellation;
            if (previousCancellation != null)
                previousCancellation.Cancel();

            CancellationTokenSource cancellation = new CancellationTokenSource();
            _scanCancellation = cancellation;
            int scanGeneration = Interlocked.Increment(ref _scanGeneration);
            int dataChangeRevision = Interlocked.CompareExchange(ref _dataChangeRevision, 0, 0);
            IGameMode gameMode = GameMode;
            IsScanning = true;
            StatusMessage = "Scanning deployment files...";

            try
            {
                FileManagerScanResult result = await Task.Run(() => _queryService.Scan(gameMode, _modManagerViewModel.VirtualModActivator, cancellation.Token), cancellation.Token).ConfigureAwait(true);
                if (_disposed || cancellation.IsCancellationRequested || scanGeneration != _scanGeneration || !Object.ReferenceEquals(gameMode, GameMode))
                    return;

                Stopwatch publishWatch = Stopwatch.StartNew();
                ApplyScanResult(result);
                publishWatch.Stop();
                result.Diagnostics.GridPublicationMilliseconds = publishWatch.ElapsedMilliseconds;
                Trace.TraceInformation("File Manager grid publication completed. Rows={0}, publish={1}ms, scan={2}", result.Rows.Count, publishWatch.ElapsedMilliseconds, result.Diagnostics);
                _loaded = true;
                IsStale = dataChangeRevision != Interlocked.CompareExchange(ref _dataChangeRevision, 0, 0);
                int pendingLinkTypes = FileManagerLinkTypeResolver.CountPendingRows(result.Rows);
                result.Diagnostics.PendingLinkTypeCount = pendingLinkTypes;
                if (pendingLinkTypes > 0)
                {
                    StartLinkTypeResolution(result.Rows, pendingLinkTypes, scanGeneration, gameMode, result.Diagnostics);
                }
                else
                {
                    StatusMessage = IsStale
                        ? "Data changed while the scan was running. Click Refresh to update."
                        : "Scan complete.";
                    Trace.TraceInformation("File Manager diagnostics finalized. {0}", result.Diagnostics);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                Rows = new BindingList<FileManagerRow>();
                _counts = new FileManagerSourceCounts();
                ApplyCounts(_counts);
                OnPropertyChanged("Rows");
                throw;
            }
            finally
            {
                if (Object.ReferenceEquals(_scanCancellation, cancellation))
                    _scanCancellation = null;

                IsScanning = false;
                cancellation.Dispose();
                if (previousCancellation != null)
                    previousCancellation.Dispose();
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
            ChangeCounts(previousSource, row.Source);
        }

        public void ApplySelectedOwner(FileManagerRow row, string selectedOwnerKey)
        {
            if (row == null) throw new ArgumentNullException("row");

            FileManagerSource oldSource = row.Source;
            _queryService.ApplySelectedOwner(row, selectedOwnerKey);
            ChangeCounts(oldSource, row.Source);
        }

        public void RefreshRowOwnership(FileManagerRow row)
        {
            if (row == null) throw new ArgumentNullException("row");

            FileManagerSource oldSource = row.Source;
            _queryService.RefreshRowOwnership(row, GameMode, _modManagerViewModel.VirtualModActivator);
            ChangeCounts(oldSource, row.Source);
        }

        public void Dispose()
        {
            _disposed = true;
            Interlocked.Increment(ref _scanGeneration);
            CancelLinkTypeResolution();
            UnwatchModActivationQueue();

            CancellationTokenSource cancellation = _scanCancellation;
            if (cancellation != null)
                cancellation.Cancel();
        }

        /// <summary>
        /// Starts background link-type detection for rows published by the current scan.
        /// </summary>
        /// <param name="rows">The published File Manager rows.</param>
        /// <param name="pendingCount">The number of rows requiring handle-based detection.</param>
        /// <param name="scanGeneration">The scan generation owning the rows.</param>
        /// <param name="gameMode">The game mode owning the scan.</param>
        /// <param name="diagnostics">The scan diagnostics receiving background detection results.</param>
        private void StartLinkTypeResolution(IList<FileManagerRow> rows, int pendingCount, int scanGeneration, IGameMode gameMode, FileManagerScanDiagnostics diagnostics)
        {
            CancelLinkTypeResolution();
            CancellationTokenSource cancellation = new CancellationTokenSource();
            _linkTypeCancellation = cancellation;
            IsResolvingLinkTypes = true;
            if (diagnostics != null)
                diagnostics.LinkTypeStartedTimestamp = Stopwatch.GetTimestamp();
            StatusMessage = String.Format("Detecting link types... 0/{0:N0}", pendingCount);
            _linkTypeResolutionTask = ResolveLinkTypesAsync(rows, pendingCount, scanGeneration, gameMode, cancellation, diagnostics);
        }

        /// <summary>
        /// Runs bounded link-type detection outside the UI thread and marshals completed batches back to the view model.
        /// </summary>
        /// <param name="rows">The rows whose link types must be detected.</param>
        /// <param name="pendingCount">The total number of pending rows.</param>
        /// <param name="scanGeneration">The scan generation owning the rows.</param>
        /// <param name="gameMode">The game mode owning the scan.</param>
        /// <param name="cancellation">The cancellation source controlling the resolver.</param>
        /// <param name="diagnostics">The scan diagnostics receiving background detection results.</param>
        /// <returns>A task representing the background resolution operation.</returns>
        private async Task ResolveLinkTypesAsync(IList<FileManagerRow> rows, int pendingCount, int scanGeneration, IGameMode gameMode, CancellationTokenSource cancellation, FileManagerScanDiagnostics diagnostics)
        {
            Exception failure = null;
            FileManagerLinkTypeResolutionDiagnostics resolutionDiagnostics = null;
            try
            {
                resolutionDiagnostics = await Task.Run(() => FileManagerLinkTypeResolver.Resolve(rows, pendingCount,
                    (batch, completed, total) => PostLinkTypeBatch(batch, completed, total, scanGeneration, gameMode, cancellation, diagnostics),
                    cancellation.Token), cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                failure = ex;
                Trace.TraceError("File Manager link-type resolution failed: {0}", ex);
            }
            finally
            {
                PostLinkTypeResolutionCompleted(scanGeneration, gameMode, cancellation, diagnostics, resolutionDiagnostics, failure);
            }
        }

        /// <summary>
        /// Marshals one resolved link-type batch to the UI synchronization context.
        /// </summary>
        /// <param name="batch">The completed link-type updates.</param>
        /// <param name="completed">The aggregate number of completed rows.</param>
        /// <param name="total">The total number of pending rows.</param>
        /// <param name="scanGeneration">The scan generation owning the rows.</param>
        /// <param name="gameMode">The game mode owning the scan.</param>
        /// <param name="cancellation">The cancellation source controlling the resolver.</param>
        /// <param name="diagnostics">The scan diagnostics receiving batched UI update timings.</param>
        private void PostLinkTypeBatch(IList<FileManagerLinkTypeUpdate> batch, int completed, int total, int scanGeneration, IGameMode gameMode, CancellationTokenSource cancellation, FileManagerScanDiagnostics diagnostics)
        {
            Action apply = () => ApplyLinkTypeBatch(batch, completed, total, scanGeneration, gameMode, cancellation, diagnostics);
            if (_uiContext != null)
                _uiContext.Post(_ => apply(), null);
            else
                apply();
        }

        /// <summary>
        /// Applies one resolved link-type batch without producing one binding notification per row.
        /// </summary>
        /// <param name="batch">The completed link-type updates.</param>
        /// <param name="completed">The aggregate number of completed rows.</param>
        /// <param name="total">The total number of pending rows.</param>
        /// <param name="scanGeneration">The scan generation owning the rows.</param>
        /// <param name="gameMode">The game mode owning the scan.</param>
        /// <param name="cancellation">The cancellation source controlling the resolver.</param>
        /// <param name="diagnostics">The scan diagnostics receiving batched UI update timings.</param>
        private void ApplyLinkTypeBatch(IList<FileManagerLinkTypeUpdate> batch, int completed, int total, int scanGeneration, IGameMode gameMode, CancellationTokenSource cancellation, FileManagerScanDiagnostics diagnostics)
        {
            if (_disposed || cancellation.IsCancellationRequested || !Object.ReferenceEquals(_linkTypeCancellation, cancellation) || scanGeneration != _scanGeneration || !Object.ReferenceEquals(gameMode, GameMode))
                return;

            long updateStart = Stopwatch.GetTimestamp();
            for (int index = 0; index < batch.Count; index++)
            {
                FileManagerLinkTypeUpdate update = batch[index];
                if (update.Row != null)
                    update.Row.SetLinkTypeState(update.State, false);
            }

            StatusMessage = String.Format("Detecting link types... {0:N0}/{1:N0}", Math.Min(completed, total), total);
            OnPropertyChanged("LinkTypeResolutionBatch");
            if (diagnostics != null)
                diagnostics.LinkTypeUiUpdateTicks += Stopwatch.GetTimestamp() - updateStart;
        }

        /// <summary>
        /// Marshals resolver completion to the UI synchronization context.
        /// </summary>
        /// <param name="scanGeneration">The scan generation owning the rows.</param>
        /// <param name="gameMode">The game mode owning the scan.</param>
        /// <param name="cancellation">The cancellation source controlling the resolver.</param>
        /// <param name="diagnostics">The scan diagnostics receiving the completed resolver results.</param>
        /// <param name="resolutionDiagnostics">The completed resolver diagnostics, or <c>null</c> when cancelled or failed.</param>
        /// <param name="failure">The resolver failure, or <c>null</c> when resolution completed or was cancelled.</param>
        private void PostLinkTypeResolutionCompleted(int scanGeneration, IGameMode gameMode, CancellationTokenSource cancellation, FileManagerScanDiagnostics diagnostics, FileManagerLinkTypeResolutionDiagnostics resolutionDiagnostics, Exception failure)
        {
            Action complete = () => CompleteLinkTypeResolution(scanGeneration, gameMode, cancellation, diagnostics, resolutionDiagnostics, failure);
            if (_uiContext != null)
                _uiContext.Post(_ => complete(), null);
            else
                complete();
        }

        /// <summary>
        /// Completes the current link-type operation and releases its cancellation source.
        /// </summary>
        /// <param name="scanGeneration">The scan generation owning the rows.</param>
        /// <param name="gameMode">The game mode owning the scan.</param>
        /// <param name="cancellation">The cancellation source controlling the resolver.</param>
        /// <param name="diagnostics">The scan diagnostics receiving the completed resolver results.</param>
        /// <param name="resolutionDiagnostics">The completed resolver diagnostics, or <c>null</c> when cancelled or failed.</param>
        /// <param name="failure">The resolver failure, or <c>null</c> when resolution completed or was cancelled.</param>
        private void CompleteLinkTypeResolution(int scanGeneration, IGameMode gameMode, CancellationTokenSource cancellation, FileManagerScanDiagnostics diagnostics, FileManagerLinkTypeResolutionDiagnostics resolutionDiagnostics, Exception failure)
        {
            bool isCurrent = Object.ReferenceEquals(_linkTypeCancellation, cancellation);
            if (isCurrent)
            {
                _linkTypeCancellation = null;
                _linkTypeResolutionTask = null;
                IsResolvingLinkTypes = false;

                if (diagnostics != null)
                {
                    diagnostics.LinkTypeUiUpdateMilliseconds = diagnostics.LinkTypeUiUpdateTicks <= 0
                        ? 0
                        : (diagnostics.LinkTypeUiUpdateTicks * 1000L) / Stopwatch.Frequency;
                    diagnostics.LinkTypeEndToEndMilliseconds = diagnostics.LinkTypeStartedTimestamp <= 0
                        ? 0
                        : ((Stopwatch.GetTimestamp() - diagnostics.LinkTypeStartedTimestamp) * 1000L) / Stopwatch.Frequency;
                }

                if (resolutionDiagnostics != null && diagnostics != null)
                {
                    diagnostics.LinkTypeResolutionMilliseconds = resolutionDiagnostics.ElapsedMilliseconds;
                    diagnostics.ResolvedLinkTypeCount = resolutionDiagnostics.CompletedCount;
                    diagnostics.LinkTypeWorkerCount = resolutionDiagnostics.WorkerCount;
                    diagnostics.LinkTypeBatchCount = resolutionDiagnostics.BatchCount;
                    diagnostics.RealFileCount = resolutionDiagnostics.RealFileCount;
                    diagnostics.HardLinkCount = resolutionDiagnostics.HardLinkCount;
                    diagnostics.SymbolicLinkCount += resolutionDiagnostics.SymbolicLinkCount;
                    diagnostics.NotFoundLinkCount = resolutionDiagnostics.NotFoundCount;
                    diagnostics.UnavailableLinkCount = resolutionDiagnostics.UnavailableCount;
                }

                if (!_disposed && scanGeneration == _scanGeneration && Object.ReferenceEquals(gameMode, GameMode))
                {
                    if (failure != null)
                        StatusMessage = "Scan complete, but link-type detection did not finish.";
                    else if (IsStale)
                        StatusMessage = "Data changed while the scan was running. Click Refresh to update.";
                    else
                        StatusMessage = "Scan complete.";

                    if (diagnostics != null)
                        Trace.TraceInformation("File Manager diagnostics finalized. {0}", diagnostics);
                }
            }

            cancellation.Dispose();
        }

        /// <summary>
        /// Requests cancellation of the current background link-type operation.
        /// </summary>
        private void CancelLinkTypeResolution()
        {
            CancellationTokenSource cancellation = _linkTypeCancellation;
            if (cancellation == null)
                return;

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
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
            Interlocked.Increment(ref _dataChangeRevision);

            if (_uiContext != null)
            {
                _uiContext.Post(_ => MarkStaleAfterActivationTask(), null);
                return;
            }

            MarkStaleAfterActivationTask();
        }

        private void MarkStaleAfterActivationTask()
        {
            if (_disposed)
                return;

            if (_loaded)
            {
                IsStale = true;
                StatusMessage = "Data changed since the last scan. Click Refresh to update.";
            }

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
            Rows = new BindingList<FileManagerRow>(result.Rows);
            _counts = result.Counts.Clone();
            OnPropertyChanged("Rows");

            DeploymentRoot = result.DeploymentRoot;
            ApplyCounts(_counts);
            LastScannedDisplay = result.ScannedAt.ToString("g");
        }

        private void ChangeCounts(FileManagerSource oldSource, FileManagerSource newSource)
        {
            _counts.Change(oldSource, newSource);
            ApplyCounts(_counts);
        }

        private void ApplyCounts(FileManagerSourceCounts counts)
        {
            TotalFiles = counts.Total;
            BaseGameFiles = counts.BaseGame;
            InstalledByNmmFiles = counts.InstalledByNmm;
            CreationsFiles = counts.Creations;
            ExternalModManagerFiles = counts.ExternalModManager;
            UntrackedFiles = counts.Untracked;
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