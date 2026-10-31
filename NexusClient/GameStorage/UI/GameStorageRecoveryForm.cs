using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Nexus.Client.Games;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;
using Nexus.UI.Controls;

namespace Nexus.Client.GameStorage.UI
{
    public class GameStorageRecoveryForm : ManagedFontXtraForm, IView
    {
        private readonly GameStorageService _service;
        private readonly IGameMode _gameMode;
        private readonly GameStorageSetupControl _control;
        private List<GameStorageCandidate> _candidates = new List<GameStorageCandidate>();

        public GameStorageRecoveryForm(GameStorageService service, IGameMode gameMode, GameStorageHealthCheck healthCheck)
        {
            _service = service;
            _gameMode = gameMode;
            Text = LanguageManager.Format("GameStorage.Recovery.Title", "Game Storage recovery - {0}", gameMode.Name);
            Width = 1120;
            Height = 680;
            MinimizeBox = false;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            _control = new GameStorageSetupControl();
            _control.ConfigureText(LanguageManager.Format("GameStorage.Recovery.Title", "Game Storage recovery - {0}", gameMode.Name), LanguageManager.Get("GameStorage.Recovery.Description", "NMM could not validate the storage folders for this game. Select a known candidate or enter custom paths. Compatible shared Mods libraries are allowed only when both Game Mode definitions opt in. NMM will not move, rename, or delete folders during recovery."), false);
            _control.RefreshRequested += RefreshRequested;
            _control.ManualVirtualInstallPathChanged += ManualVirtualInstallPathChanged;
            _control.ManualPathsChanged += ManualPathsChanged;
            _control.ApplyRequested += ApplyRequested;
            _control.CandidatePreviewRequested += CandidatePreviewRequested;
            _control.CancelRequested += CancelRequested;
            Controls.Add(_control);

            _control.SetManualPaths(_service.FromGameMode(_gameMode));
            SetHealth(healthCheck);
            RefreshCandidates();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameStorageCandidate SelectedCandidate => _control.SelectedCandidate;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool WasCancelled { get; private set; }

        private void RefreshRequested(object sender, EventArgs e)
        {
            RefreshCandidates();
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

            base.OnFormClosing(e);
        }

        private void ManualVirtualInstallPathChanged(object sender, EventArgs e)
        {
            _control.SetLinkFolderRequired(_service.IsLinkFolderRequired(_control.ManualVirtualInstallPath, _gameMode.GameModeEnvironmentInfo.InstallationPath));
        }

        private void ManualPathsChanged(object sender, EventArgs e)
        {
            var candidate = _control.ManualCandidate;
            if (candidate == null)
                return;

            GameStoragePathSet currentPaths = _service.FromGameMode(_gameMode);
            candidate.GameId = currentPaths.GameId;
            candidate.LinkFolderRequired = candidate.LinkFolderRequired ||
                _service.IsLinkFolderRequired(candidate.VirtualInstallPath, currentPaths.GameInstallPath);

            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetResolvedLinkFolderPath(paths.LinkFolderPath, paths.LinkFolderRequired);
            SetHealth(_service.ValidateRecoverySelection(currentPaths, candidate));
        }

        private void CandidatePreviewRequested(object sender, EventArgs e)
        {
            var candidate = _control.SelectedCandidate;
            if (candidate == null)
                return;

            PreviewCandidate(candidate);
        }

        private void ApplyRequested(object sender, EventArgs e)
        {
            var selectedCandidate = _control.SelectedCandidate;
            var manualCandidate = selectedCandidate == null ? _control.ManualCandidate : null;
            var candidate = selectedCandidate ?? manualCandidate;
            if (manualCandidate != null)
            {
                manualCandidate.GameId = _gameMode.ModeId;
                manualCandidate.LinkFolderRequired = manualCandidate.LinkFolderRequired || _service.IsLinkFolderRequired(manualCandidate.VirtualInstallPath, _gameMode.GameModeEnvironmentInfo.InstallationPath);
            }

            if (candidate == null)
            {
                XtraMessageBox.Show(this, LanguageManager.Get("GameStorage.Common.SelectCandidateFirst", "Select a Game Storage candidate or enter custom paths first."), LanguageManager.Get("GameStorage.Recovery.GenericTitle", "Game Storage recovery"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GameStoragePathSet currentPaths = _service.FromGameMode(_gameMode);
            GameStorageHealthCheck healthCheck = _service.ValidateRecoverySelection(currentPaths, candidate);
            if (candidate.RequiresUserConfirmation || !healthCheck.IsHealthy)
            {
                string confirmationMessage = candidate.IsSharedModsLibrary
                    ? (candidate.SharedModsDescription ?? LanguageManager.Get("GameStorage.SharedMods.CompatibleFallback", "This Mods folder is already used by a compatible Game Mode.")) + Environment.NewLine + Environment.NewLine +
                      LanguageManager.Format("GameStorage.Recovery.ConfirmSharedMods", "Use it as a shared Mods library for {0}? Only the Mods folder will be shared. InstallInfo, VirtualInstall, overwrite state, and the Link Folder remain exclusive to this Game Mode.", _gameMode.Name)
                    : LanguageManager.Get("GameStorage.Common.ConfirmSelectedFolders", "Use these folders for this game? Missing folders will be created. Existing files will not be moved or deleted.");

                if (!healthCheck.IsHealthy)
                    confirmationMessage = healthCheck.ToUserMessage() + Environment.NewLine + Environment.NewLine + confirmationMessage;

                var result = XtraMessageBox.Show(this, confirmationMessage, LanguageManager.Get("GameStorage.Recovery.ConfirmTitle", "Confirm Game Storage recovery"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (result != DialogResult.OK)
                    return;
            }

            // Saving is the explicit acceptance of the reviewed empty folders and same-game IDs.
            if (_service.ApplyRecoveryCandidate(_gameMode, candidate, true, true, out healthCheck) && healthCheck.IsHealthy)
            {
                DialogResult = DialogResult.OK;
                return;
            }

            if (_service.ApplySelectedCandidatePaths(currentPaths, candidate, out healthCheck))
            {
                DialogResult = DialogResult.OK;
                return;
            }

            SetHealth(healthCheck);
            XtraMessageBox.Show(this, healthCheck?.ToUserMessage() ?? LanguageManager.Get("GameStorage.Recovery.ApplyFailed", "The selected Game Storage candidate could not be applied."), LanguageManager.Get("GameStorage.Recovery.GenericTitle", "Game Storage recovery"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// Refreshes alternatives without replacing the configured or manually entered
        /// folders with a higher-scoring historical registry candidate.
        /// </summary>
        private void RefreshCandidates()
        {
            GameStorageCandidate selectedCandidate = _control.SelectedCandidate;
            _candidates = _service.DiscoverRecoveryCandidates(_gameMode);
            _control.SetCandidates(_candidates);

            if (selectedCandidate != null)
                _control.SelectCandidate(selectedCandidate);
            else
                _control.ClearCandidateSelection();
        }

        private void PreviewCandidate(GameStorageCandidate candidate)
        {
            GameStoragePathSet currentPaths = _service.FromGameMode(_gameMode);
            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetManualPaths(paths);
            SetHealth(_service.ValidateRecoverySelection(currentPaths, candidate));
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
            GameStoragePathSet currentPaths = _service.FromGameMode(_gameMode);
            return _service.ResolveRecoveryPaths(currentPaths, candidate);
        }
    }
}
