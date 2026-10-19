using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.Games;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;
using Nexus.UI.Controls;

namespace Nexus.Client.GameStorage.UI
{
    public class GameStorageSetupForm : ManagedFontXtraForm, IView
    {
        private readonly GameStorageService _service;
        private readonly GameStoragePathSet _currentPaths;
        private readonly GameStorageSetupControl _control;
        private List<GameStorageCandidate> _candidates = new List<GameStorageCandidate>();
        private readonly SemaphoreSlim _readGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _closingCancellation = new CancellationTokenSource();
        private CancellationTokenSource _draftCancellation;
        private bool _refreshBusy;
        private bool _validationBusy;
        private bool _initialRefreshStarted;
        private bool _closing;
        private int _refreshGeneration;
        private string _candidateMessage = string.Empty;

        public GameStorageSetupForm(GameStorageService service, GameStoragePathSet currentPaths, GameStorageHealthCheck healthCheck, bool allowLegacySetup = true)
        {
            _service = service;
            _currentPaths = currentPaths;
            Text = LanguageManager.Format("GameStorage.Setup.Title", "Game Storage setup - {0}", currentPaths.GameName);
            Width = 1120;
            Height = 680;
            MinimizeBox = false;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            _control = new GameStorageSetupControl();
            _control.ConfigureText(LanguageManager.Format("GameStorage.Setup.Title", "Game Storage setup - {0}", currentPaths.GameName),
                LanguageManager.Get("GameStorage.Setup.ShortDescription",
                    "Choose folders for this game's install records, mod archives and virtual installation. Keep existing paths to reuse your data, or select Use on a detected setup to preview it. NMM won't move or delete existing files."), allowLegacySetup);
            _control.SetApplyCaption(LanguageManager.Get("GameStorage.Setup.SaveConfiguration", "Save configuration"));
            _control.SetManualPaths(currentPaths);
            _control.ClearCandidateSelection();
            _control.RefreshRequested += RefreshRequested;
            _control.ManualVirtualInstallPathChanged += ManualVirtualInstallPathChanged;
            _control.ManualPathsChanged += ManualPathsChanged;
            _control.DraftChanged += DraftChanged;
            _control.ApplyRequested += ApplyRequested;
            _control.CandidatePreviewRequested += CandidatePreviewRequested;
            _control.LegacySetupRequested += LegacySetupRequested;
            _control.CancelRequested += CancelRequested;
            Controls.Add(_control);

            SetHealth(healthCheck);
            _candidateMessage = LanguageManager.Get("GameStorage.Setup.LookingForCandidates", "Existing setups will be checked after this window opens.");
            UpdateBusyState();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameStorageCandidate SelectedCandidate { get; private set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool UseLegacySetup { get; private set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool WasCancelled { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_initialRefreshStarted)
                return;
            _initialRefreshStarted = true;
            // File-system discovery can be slow on large or disconnected storage drives.
            // Do not run it in the constructor before the dialog can paint.
            _ = RefreshCandidatesAsync();
        }

        private void RefreshRequested(object sender, EventArgs e)
        {
            _ = RefreshCandidatesAsync();
        }

        private void CancelRequested(object sender, EventArgs e)
        {
            WasCancelled = true;
            DialogResult = DialogResult.Cancel;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult != DialogResult.OK)
                WasCancelled = true;
            _closing = true;
            _closingCancellation.Cancel();
            _draftCancellation?.Cancel();
            base.OnFormClosing(e);
        }

        private void DraftChanged(object sender, EventArgs e)
        {
            // Discard pending validation as soon as the user types, not 400 ms later.
            CancellationTokenSource previousValidation = _draftCancellation;
            _draftCancellation = null;
            previousValidation?.Cancel();
            _validationBusy = true;
            UpdateBusyState();
        }

        private void ManualVirtualInstallPathChanged(object sender, EventArgs e)
        {
            _control.SetLinkFolderRequired(_service.IsLinkFolderRequired(_control.ManualVirtualInstallPath, _currentPaths.GameInstallPath));
        }

        private void ManualPathsChanged(object sender, EventArgs e)
        {
            var candidate = _control.ManualCandidate;
            if (candidate == null)
            {
                _draftCancellation?.Cancel();
                _validationBusy = false;
                UpdateBusyState();
                return;
            }

            candidate.GameId = _currentPaths.GameId;
            candidate.LinkFolderRequired = candidate.LinkFolderRequired ||
                _service.IsLinkFolderRequired(candidate.VirtualInstallPath, _currentPaths.GameInstallPath);

            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetLinkFolderRequired(paths.LinkFolderRequired);
            _ = ValidateDraftAsync(paths, _control.DraftGeneration);
        }

        private void CandidatePreviewRequested(object sender, EventArgs e)
        {
            var candidate = _control.SelectedCandidate;
            if (candidate == null)
                return;

            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetManualPaths(paths);
            _ = ValidateDraftAsync(paths, _control.DraftGeneration);
        }

        private void ApplyRequested(object sender, EventArgs e)
        {
            // Guard keyboard/programmatic activation as well as the disabled button.
            if (_closing || _refreshBusy || _validationBusy)
                return;

            var selectedCandidate = _control.SelectedCandidate;
            var manualCandidate = selectedCandidate == null ? _control.ManualCandidate : null;
            var candidate = selectedCandidate ?? manualCandidate;
            if (manualCandidate != null)
            {
                manualCandidate.GameId = _currentPaths.GameId;
                manualCandidate.LinkFolderRequired = manualCandidate.LinkFolderRequired || _service.IsLinkFolderRequired(manualCandidate.VirtualInstallPath, _currentPaths.GameInstallPath);
            }

            if (candidate == null)
            {
                XtraMessageBox.Show(this, LanguageManager.Get("GameStorage.Common.SelectCandidateFirst", "Select a Game Storage candidate or enter custom paths first."), LanguageManager.Get("GameStorage.Setup.GenericTitle", "Game Storage setup"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (candidate.RequiresUserConfirmation)
            {
                string confirmationMessage = candidate.IsSharedModsLibrary
                    ? (candidate.SharedModsDescription ?? LanguageManager.Get("GameStorage.SharedMods.CompatibleFallback", "This Mods folder is already used by a compatible Game Mode.")) + Environment.NewLine + Environment.NewLine +
                      LanguageManager.Format("GameStorage.Setup.ConfirmSharedMods", "Use it as a shared Mods library for {0}? Only the Mods folder will be shared. InstallInfo, VirtualInstall, overwrite state, and the Link Folder remain exclusive to this Game Mode.", _currentPaths.GameName)
                    : LanguageManager.Get("GameStorage.Setup.ConfirmApply", "Use the selected Game Storage paths for this game? NMM will not move, rename, or delete existing folders.");

                var result = XtraMessageBox.Show(this, confirmationMessage, LanguageManager.Get("GameStorage.Setup.ConfirmTitle", "Confirm Game Storage setup"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (result != DialogResult.OK)
                    return;
            }

            SelectedCandidate = candidate;
            DialogResult = DialogResult.OK;
        }

        private void LegacySetupRequested(object sender, EventArgs e)
        {
            UseLegacySetup = true;
            DialogResult = DialogResult.OK;
        }

        private async Task RefreshCandidatesAsync()
        {
            if (_closing || _refreshBusy)
                return;

            _refreshBusy = true;
            int refreshGeneration = ++_refreshGeneration;
            GameStoragePathSet originalPaths = CopyPaths(_currentPaths);
            UpdateBusyState();

            try
            {
                // Serialize reads using the existing service. It reads the same registry and
                // manifests as validation; never inspect a mutable editor/control on a worker.
                var result = await Task.Run(async () =>
                {
                    await _readGate.WaitAsync(_closingCancellation.Token).ConfigureAwait(false);
                    try
                    {
                        _closingCancellation.Token.ThrowIfCancellationRequested();
                        List<GameStorageCandidate> candidates = _service.DiscoverRecoveryCandidates(originalPaths);
                        GameStorageCandidate best = _service.GetBestRecoveryCandidate(originalPaths,
                            candidates.Where(x => x != null && !string.Equals(x.CandidateKind, "Proposed setup", StringComparison.OrdinalIgnoreCase)));
                        return Tuple.Create(candidates, best);
                    }
                    finally { _readGate.Release(); }
                }, _closingCancellation.Token);

                if (_closing || IsDisposed || refreshGeneration != _refreshGeneration)
                    return;
                // Restore the user's *current* explicit choice, not the choice that
                // happened to be active when the search started.
                GameStorageCandidate currentSelection = _control.SelectedCandidate;
                _candidates = result.Item1;
                _control.SetCandidates(_candidates);
                if (currentSelection != null)
                    _control.SelectCandidate(currentSelection);

                // Suggest using the recovery service's health/usability policy, not
                // confidence alone. Never silently replace existing or edited folders.
                _candidateMessage = result.Item2 == null
                    ? LanguageManager.Get("GameStorage.Setup.NoRecommendation", "Review the selected folders or choose a detected setup.")
                    : LanguageManager.Format("GameStorage.Setup.RecommendedOption", "Suggested setup: {0}. Select its Use action to preview it.", result.Item2.CandidateKindDisplay);
            }
            catch (OperationCanceledException) when (_closingCancellation.IsCancellationRequested) { }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                Trace.TraceWarning("Game Storage candidate discovery failed: {0}", ex);
                if (!_closing && !IsDisposed)
                    _candidateMessage = LanguageManager.Get("GameStorage.Setup.SearchFailed", "Could not search existing storage setups. Enter folders manually or refresh.");
            }
            finally
            {
                if (!_closing && !IsDisposed)
                {
                    _refreshBusy = false;
                    UpdateBusyState();
                }
            }
        }

        private async Task ValidateDraftAsync(GameStoragePathSet snapshot, int generation)
        {
            _draftCancellation?.Cancel();
            var request = CancellationTokenSource.CreateLinkedTokenSource(_closingCancellation.Token);
            _draftCancellation = request;
            _validationBusy = true;
            UpdateBusyState();
            try
            {
                GameStorageHealthCheck health = await Task.Run(async () =>
                {
                    await _readGate.WaitAsync(request.Token).ConfigureAwait(false);
                    try
                    {
                        request.Token.ThrowIfCancellationRequested();
                        return _service.ValidateStorage(snapshot);
                    }
                    finally { _readGate.Release(); }
                }, request.Token);

                if (!_closing && !IsDisposed && !request.IsCancellationRequested &&
                    ReferenceEquals(_draftCancellation, request) && generation == _control.DraftGeneration)
                    SetHealth(health);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                Trace.TraceWarning("Game Storage draft validation failed: {0}", ex);
                if (!_closing && !IsDisposed && ReferenceEquals(_draftCancellation, request) &&
                    generation == _control.DraftGeneration)
                    _control.SetRows(new[] { new GameStorageSetupRow
                    {
                        Status = LanguageManager.Get("GameStorage.Setup.ValidationFailed", "Validation unavailable"),
                        Message = ex.Message
                    } });
            }
            finally
            {
                if (ReferenceEquals(_draftCancellation, request))
                {
                    _draftCancellation = null;
                    _validationBusy = false;
                    if (!_closing && !IsDisposed)
                        UpdateBusyState();
                }
                request.Dispose();
            }
        }

        private void UpdateBusyState()
        {
            if (_closing || IsDisposed)
                return;
            string message = _refreshBusy
                ? LanguageManager.Get("GameStorage.Setup.Searching", "Checking existing storage setups...")
                : _validationBusy
                    ? LanguageManager.Get("GameStorage.Setup.Validating", "Checking selected folders...")
                    : _candidateMessage;
            _control.SetReadOnlyChecksBusy(_refreshBusy, _validationBusy, message);
        }

        private static GameStoragePathSet CopyPaths(GameStoragePathSet source)
        {
            return new GameStoragePathSet
            {
                GameId = source.GameId, GameName = source.GameName,
                GameInstallPath = source.GameInstallPath,
                InstallInfoPath = source.InstallInfoPath,
                ModsPath = source.ModsPath,
                VirtualInstallPath = source.VirtualInstallPath,
                LinkFolderPath = source.LinkFolderPath,
                LinkFolderRequired = source.LinkFolderRequired,
                CompatibleSharedModsGameIds = source.CompatibleSharedModsGameIds == null
                    ? new List<string>() : new List<string>(source.CompatibleSharedModsGameIds)
            };
        }

        private void SetHealth(GameStorageHealthCheck healthCheck)
        {
            var rows = healthCheck?.Items.Select(x => new GameStorageSetupRow
            {
                Role = GameStorageLocalization.GetFolderRoleName(x.Role),
                Path = x.Path,
                Status = GameStorageLocalization.GetHealthStatusName(x.Status),
                Message = x.Message
            }) ?? Enumerable.Empty<GameStorageSetupRow>();
            _control.SetRows(rows);
        }

        private GameStoragePathSet CreatePathSetFromCandidate(GameStorageCandidate candidate)
        {
            return new GameStoragePathSet
            {
                GameId = _currentPaths.GameId,
                GameName = _currentPaths.GameName,
                GameInstallPath = _currentPaths.GameInstallPath,
                InstallInfoPath = candidate.InstallInfoPath,
                ModsPath = candidate.ModsPath,
                VirtualInstallPath = candidate.VirtualInstallPath,
                LinkFolderPath = candidate.LinkFolderPath,
                LinkFolderRequired = candidate.LinkFolderRequired || _service.IsLinkFolderRequired(candidate.VirtualInstallPath, _currentPaths.GameInstallPath),
                CompatibleSharedModsGameIds = _currentPaths.CompatibleSharedModsGameIds == null
                    ? new List<string>()
                    : new List<string>(_currentPaths.CompatibleSharedModsGameIds)
            };
        }
    }
}
