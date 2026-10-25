namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.CollectionManagement;
    using Nexus.Client.Games;
    using Nexus.Client.ModManagement.Witcher3;
    using Nexus.Client.Util.Localization;

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
        private bool _removingUntracked;
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
        private int _installedByCollectionFiles;
        private int _creationsFiles;
        private int _externalModManagerFiles;
        private int _untrackedFiles;
        private readonly string _notScannedStatus;
        private readonly string _unsupportedGameStatus;
        private readonly string _scanningStatus;
        private readonly string _scanProgressFormat;
        private readonly string _linkTypeProgressFormat;
        private readonly string _linkTypeIncompleteStatus;
        private readonly string _changedDuringScanStatus;
        private readonly string _scanCompleteStatus;
        private readonly string _changedSinceScanStatus;

        public FileManagerVM(ModManagerVM modManagerViewModel)
            : this(modManagerViewModel, null)
        {
        }

        public FileManagerVM(ModManagerVM modManagerViewModel, IFileManagerManualSourceStore manualSourceStore)
        {
            if (modManagerViewModel == null) throw new ArgumentNullException("modManagerViewModel");
            _modManagerViewModel = modManagerViewModel;
            _notScannedStatus = LanguageManager.Get("FileManager.Status.NotScanned", "Not scanned.");
            _unsupportedGameStatus = LanguageManager.Get("FileManager.Status.UnsupportedGame", "File Manager is available only for Gamebryo game modes.");
            _scanningStatus = LanguageManager.Get("FileManager.Status.Scanning", "Scanning deployment files...");
            _scanProgressFormat = LanguageManager.GetFormat("FileManager.Status.ScanningProgress", "Scanning deployment files... {0:N0} files found");
            _linkTypeProgressFormat = LanguageManager.GetFormat("FileManager.Status.DetectingLinkTypes", "Detecting link types... {0:N0}/{1:N0}");
            _linkTypeIncompleteStatus = LanguageManager.Get("FileManager.Status.LinkTypeIncomplete", "Scan complete, but link-type detection did not finish.");
            _changedDuringScanStatus = LanguageManager.Get("FileManager.Status.ChangedDuringScan", "Data changed while the scan was running. Click Refresh to update.");
            _scanCompleteStatus = LanguageManager.Get("FileManager.Status.ScanComplete", "Scan complete.");
            _changedSinceScanStatus = LanguageManager.Get("FileManager.Status.ChangedSinceScan", "Data changed since the last scan. Click Refresh to update.");
            _queryService = new FileManagerQueryService(manualSourceStore ?? new SettingsFileManagerManualSourceStore(modManagerViewModel.Settings));
            _deploymentService = new VirtualDeploymentService(modManagerViewModel.VirtualModActivator);
            _uiContext = SynchronizationContext.Current;
            Rows = new BindingList<FileManagerRow>();
            StatusMessage = _notScannedStatus;
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

        /// <summary>Gets the number of files currently supplied by mods installed for a tracked Collection.</summary>
        public int InstalledByCollectionFiles
        {
            get { return _installedByCollectionFiles; }
            private set { if (_installedByCollectionFiles != value) { _installedByCollectionFiles = value; OnPropertyChanged("InstalledByCollectionFiles"); } }
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
            get { return !IsScanning && !IsRemovingUntracked && !HasActiveOrQueuedInstallUninstallTasks(); }
        }

        /// <summary>Gets whether the confirmed untracked cleanup currently owns the File Manager operation.</summary>
        public bool IsRemovingUntracked
        {
            get { return _removingUntracked; }
            private set
            {
                if (_removingUntracked == value) return;
                _removingUntracked = value;
                OnPropertyChanged("IsRemovingUntracked");
                OnPropertyChanged("CanChangeFileOwner");
            }
        }

        /// <summary>Gets the complete unfiltered set currently classified as untracked.</summary>
        internal List<string> GetUntrackedRemovalPaths()
        {
            return Rows.Where(x => x.Source == FileManagerSource.Untracked).Select(x => x.RelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Removes the confirmed path set off the UI thread and refreshes the file list afterwards.</summary>
        internal async Task<FileManagerUntrackedRemovalResult> RemoveUntrackedFilesAsync(string reviewedRoot, IList<string> reviewedPaths)
        {
            if (!CanChangeFileOwner || _disposed) throw new InvalidOperationException("File Manager is busy.");
            IsRemovingUntracked = true;
            CancelLinkTypeResolution();
            Interlocked.Increment(ref _scanGeneration);
            string progressFormat = LanguageManager.GetFormat("FileManager.RemoveUntracked.Progress", "Removing untracked files ({0:N0}/{1:N0})...");
            StatusMessage = String.Format(progressFormat, 0, reviewedPaths.Count);
            Progress<int> progress = new Progress<int>(current =>
            {
                if (!_disposed && IsRemovingUntracked) StatusMessage = String.Format(progressFormat, current, reviewedPaths.Count);
            });
            try
            {
                return await Task.Run(() => _modManagerViewModel.ModManager.RemoveUntrackedFiles(
                    reviewedRoot, reviewedPaths, _queryService, progress)).ConfigureAwait(true);
            }
            finally
            {
                IsRemovingUntracked = false;
                if (!_disposed) await RefreshAsync().ConfigureAwait(true);
            }
        }

        public bool IsGamebryoMode
        {
            get { return IsGamebryoGameMode(GameMode); }
        }

        /// <summary>Marks the cached source labels stale when Collection tracking changes, including during a scan.</summary>
        internal void NotifyCollectionSourcesChanged()
        {
            Interlocked.Increment(ref _dataChangeRevision);
            MarkStaleAfterActivationTask();
        }

        public async Task LoadIfNeededAsync()
        {
            if (_loaded)
                return;

            await RefreshAsync().ConfigureAwait(true);
        }

        public async Task RefreshAsync()
        {
            if (_disposed || IsScanning || IsRemovingUntracked)
                return;

            if (!IsGamebryoMode)
            {
                StatusMessage = _unsupportedGameStatus;
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
            StatusMessage = _scanningStatus;

            try
            {
                IDictionary<string, string> registrationKeys = FileManagerCollectionSourceLookup.CaptureRegistrationKeys(_modManagerViewModel.ModManager);
                bool discoveryComplete = false;
                Progress<int> progress = new Progress<int>(count =>
                {
                    if (!_disposed && !discoveryComplete && IsScanning && Object.ReferenceEquals(_scanCancellation, cancellation) &&
                        scanGeneration == _scanGeneration && Object.ReferenceEquals(gameMode, GameMode))
                        StatusMessage = String.Format(_scanProgressFormat, count);
                });
                FileManagerScanResult result = await Task.Run(() =>
                {
                    Stopwatch collectionWatch = Stopwatch.StartNew();
                    ISet<string> collectionOwnerKeys = FileManagerCollectionSourceLookup.Load(gameMode, _modManagerViewModel.ModManager.EnvironmentInfo, registrationKeys);
                    long collectionMilliseconds = collectionWatch.ElapsedMilliseconds;
                    cancellation.Token.ThrowIfCancellationRequested();
                    FileManagerScanResult scanned = _queryService.Scan(gameMode, _modManagerViewModel.VirtualModActivator,
                        _modManagerViewModel.ModManager.DeploymentManager, cancellation.Token, progress, collectionOwnerKeys);
                    scanned.Diagnostics.CollectionSourceLoadMilliseconds = collectionMilliseconds;
                    return scanned;
                }, cancellation.Token).ConfigureAwait(true);
                discoveryComplete = true;
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
                        ? _changedDuringScanStatus
                        : _scanCompleteStatus;
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

            string previousOwnerKey = row.OwnerKey;
            string[] affectedOwnerKeys = row.OwnerCandidates == null
                ? new string[0]
                : row.OwnerCandidates.Select(x => x.OwnerKey).Where(x => !String.IsNullOrWhiteSpace(x)).ToArray();
            // File Manager switches are not native installer tasks. Their legacy path is
            // retained for other games; Witcher mutations must pass the shared root
            // authority boundary and must never reuse a pre-reload owner service.
            if ((Nexus.Client.Games.GameModeRuntimePolicies.For(GameMode.ModeId).NativeRootOwnershipProfile != null))
            {
                string reviewedRelativePath = row.RelativePath;
                ModDeploymentTarget reviewedTarget = row.DeploymentTarget;
                return Task.Run(() => SwitchWitcherOwnerUnderLease(reviewedRelativePath,
                    reviewedTarget, previousOwnerKey, selectedOwnerKey, affectedOwnerKeys));
            }
            IModDeploymentManager deploymentManager = _modManagerViewModel.ModManager.DeploymentManager;
            if (row.DeploymentTarget != null && deploymentManager != null && deploymentManager.IsPromoted(row.DeploymentTarget))
            {
                return Task.Run(() =>
                {
                    try
                    {
                        deploymentManager.SwitchPromotedOwner(row.DeploymentTarget, selectedOwnerKey);
                        if (_modManagerViewModel.ProfileManager != null)
                            _modManagerViewModel.ProfileManager.UpdateCurrentDeploymentManifest();
                        _modManagerViewModel.ModManager.RecordManualFileOwnerChange(previousOwnerKey, selectedOwnerKey, affectedOwnerKeys);
                        return VirtualFileOwnerSwitchResult.Succeeded(row.RelativePath, selectedOwnerKey);
                    }
                    catch (Exception ex)
                    {
                        return VirtualFileOwnerSwitchResult.Failed(ex);
                    }
                });
            }

            return Task.Run(() =>
            {
                VirtualFileOwnerSwitchResult result = _deploymentService.SwitchFileOwner(row.RelativePath, selectedOwnerKey);
                if (result != null && result.Success)
                    _modManagerViewModel.ModManager.RecordManualFileOwnerChange(previousOwnerKey, result.SelectedOwnerKey, affectedOwnerKeys);
                return result;
            });
        }

        /// <summary>
        /// Owner-selection safety boundary for a Witcher File Manager row. The UI's prior
        /// owner/candidate list is never treated as authority after a native state reload.
        /// </summary>
        internal static void RequireFreshWitcherOwnerChoice(ModDeploymentTarget reviewedTarget,
            string reviewedOwnerKey, string selectedOwnerKey, IReadOnlyList<string> reviewedOwnerKeys,
            FileManagerRow currentRow)
        {
            if (currentRow == null || currentRow.OwnerCount < 2 ||
                !Object.Equals(reviewedTarget, currentRow.DeploymentTarget) ||
                String.IsNullOrWhiteSpace(selectedOwnerKey) ||
                !String.Equals(reviewedOwnerKey, currentRow.OwnerKey, StringComparison.OrdinalIgnoreCase) ||
                reviewedOwnerKeys == null || currentRow.OwnerCandidates == null ||
                reviewedOwnerKeys.Count != currentRow.OwnerCandidates.Count ||
                !reviewedOwnerKeys.SequenceEqual(currentRow.OwnerCandidates.Select(candidate =>
                    candidate == null ? null : candidate.OwnerKey), StringComparer.OrdinalIgnoreCase) ||
                !currentRow.OwnerCandidates.Any(candidate =>
                    candidate != null && String.Equals(candidate.OwnerKey, selectedOwnerKey, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    "The File Manager owner stack changed since the selection was reviewed. Refresh before switching owners.");
        }

        /// <summary>
        /// All native services are obtained AFTER the target's cross-process reservation
        /// has reloaded InstallLog/VMA/deployment. No cached deployment service is reused.
        /// </summary>
        private VirtualFileOwnerSwitchResult SwitchWitcherOwnerUnderLease(string relativePath,
            ModDeploymentTarget reviewedTarget, string previousOwnerKey, string selectedOwnerKey,
            string[] affectedOwnerKeys)
        {
            try
            {
                ModManager manager = _modManagerViewModel.ModManager;
                using (CollectionTargetMutationLease lease = CollectionManualNativeMutationBoundary.AcquireFileManagerMutation(manager))
                {
                    if (!ReferenceEquals(manager, _modManagerViewModel.ModManager) ||
                        !(Nexus.Client.Games.GameModeRuntimePolicies.For(manager.GameMode.ModeId).NativeRootOwnershipProfile != null))
                        throw new InvalidOperationException("The active Witcher 3 manager changed during owner selection.");

                    var currentRow = new FileManagerRow
                    {
                        RelativePath = relativePath,
                        DeploymentTarget = reviewedTarget
                    };
                    IModDeploymentManager deploymentManager = manager.DeploymentManager;
                    _queryService.RefreshRowOwnership(currentRow, manager.GameMode,
                        manager.VirtualModActivator, deploymentManager);
                    RequireFreshWitcherOwnerChoice(reviewedTarget, previousOwnerKey, selectedOwnerKey,
                        affectedOwnerKeys, currentRow);

                    if (reviewedTarget != null && deploymentManager != null && deploymentManager.IsPromoted(reviewedTarget))
                    {
                        deploymentManager.SwitchPromotedOwner(reviewedTarget, selectedOwnerKey);
                        manager.ProfileManager?.UpdateCurrentDeploymentManifest();
                        manager.RecordManualFileOwnerChange(previousOwnerKey, selectedOwnerKey, affectedOwnerKeys);
                        return VirtualFileOwnerSwitchResult.Succeeded(relativePath, selectedOwnerKey);
                    }
                    if (reviewedTarget != null)
                        throw new InvalidOperationException("The promoted owner topology changed during review.");

                    // The old _deploymentService was constructed before ValidateAndReload.
                    // Its VMA may already have been replaced with a different live instance.
                    VirtualFileOwnerSwitchResult result = new VirtualDeploymentService(manager.VirtualModActivator,
                        deploymentManager).SwitchFileOwner(relativePath, selectedOwnerKey);
                    if (result != null && result.Success)
                        manager.RecordManualFileOwnerChange(previousOwnerKey, result.SelectedOwnerKey, affectedOwnerKeys);
                    return result;
                }
            }
            catch (Exception ex)
            {
                return VirtualFileOwnerSwitchResult.Failed(ex);
            }
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
            IModDeploymentManager deploymentManager = _modManagerViewModel.ModManager.DeploymentManager;
            if (row.DeploymentTarget != null && deploymentManager != null && deploymentManager.IsPromoted(row.DeploymentTarget))
                _queryService.RefreshRowOwnership(row, GameMode, _modManagerViewModel.VirtualModActivator, deploymentManager);
            else
                _queryService.ApplySelectedOwner(row, selectedOwnerKey);
            ChangeCounts(oldSource, row.Source);
        }

        public void RefreshRowOwnership(FileManagerRow row)
        {
            if (row == null) throw new ArgumentNullException("row");

            FileManagerSource oldSource = row.Source;
            _queryService.RefreshRowOwnership(row, GameMode, _modManagerViewModel.VirtualModActivator, _modManagerViewModel.ModManager.DeploymentManager);
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
            StatusMessage = String.Format(_linkTypeProgressFormat, 0, pendingCount);
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

            StatusMessage = String.Format(_linkTypeProgressFormat, Math.Min(completed, total), total);
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
                        StatusMessage = _linkTypeIncompleteStatus;
                    else if (IsStale)
                        StatusMessage = _changedDuringScanStatus;
                    else
                        StatusMessage = _scanCompleteStatus;

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
                StatusMessage = _changedSinceScanStatus;
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
            InstalledByCollectionFiles = counts.InstalledByCollection;
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
