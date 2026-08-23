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
            _control.SetLinkFolderRequired(paths.LinkFolderRequired);
            SetHealth(_service.ValidateStorage(paths, false));
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

            if (candidate.RequiresUserConfirmation)
            {
                string confirmationMessage = candidate.IsSharedModsLibrary
                    ? (candidate.SharedModsDescription ?? LanguageManager.Get("GameStorage.SharedMods.CompatibleFallback", "This Mods folder is already used by a compatible Game Mode.")) + Environment.NewLine + Environment.NewLine +
                      LanguageManager.Format("GameStorage.Recovery.ConfirmSharedMods", "Use it as a shared Mods library for {0}? Only the Mods folder will be shared. InstallInfo, VirtualInstall, overwrite state, and the Link Folder remain exclusive to this Game Mode.", _gameMode.Name)
                    : LanguageManager.Get("GameStorage.Recovery.ConfirmApply", "Apply the selected Game Storage paths for this game? NMM will update only this game's folder settings and will not move or delete any files.");

                var result = XtraMessageBox.Show(this, confirmationMessage, LanguageManager.Get("GameStorage.Recovery.ConfirmTitle", "Confirm Game Storage recovery"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (result != DialogResult.OK)
                    return;
            }

            if (_service.ApplyRecoveryCandidate(_gameMode, candidate, out var healthCheck))
            {
                DialogResult = DialogResult.OK;
                return;
            }

            if (_service.CanAcceptSuspiciousEmptyFolders(healthCheck))
            {
                SetHealth(healthCheck);
                var result = XtraMessageBox.Show(
                    this,
                    BuildSuspiciousEmptyConfirmation(healthCheck),
                    LanguageManager.Get("GameStorage.Recovery.EmptyFoldersConfirmTitle", "Confirm empty Game Storage"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                    return;

                if (_service.ApplyRecoveryCandidate(_gameMode, candidate, true, out healthCheck))
                {
                    DialogResult = DialogResult.OK;
                    return;
                }
            }

            SetHealth(healthCheck);
            XtraMessageBox.Show(this, healthCheck?.ToUserMessage() ?? LanguageManager.Get("GameStorage.Recovery.ApplyFailed", "The selected Game Storage candidate could not be applied."), LanguageManager.Get("GameStorage.Recovery.GenericTitle", "Game Storage recovery"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private string BuildSuspiciousEmptyConfirmation(GameStorageHealthCheck healthCheck)
        {
            var folders = healthCheck.Items
                .Where(x => x.Status == GameStorageHealthStatus.SuspiciousEmptyFolder && x.Role.HasValue)
                .Select(x => "- " + GameStorageLocalization.GetFolderRoleName(x.Role.Value) + ": " + x.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return LanguageManager.Format(
                "GameStorage.Recovery.EmptyFoldersConfirmMessage",
                "NMM previously recorded data in this Game Storage, but the following selected folders are now empty:\n\n{0}\n\nContinuing will accept the current empty folders as intentional and replace the previous Game Storage snapshot. NMM will not move or delete any files.\n\nContinue only if the old data was intentionally removed or you deliberately want to start with an empty Game Storage.\n\nDo you want to continue?",
                string.Join(Environment.NewLine, folders));
        }

        private void RefreshCandidates()
        {
            _candidates = _service.DiscoverRecoveryCandidates(_gameMode);
            _control.SetCandidates(_candidates);
            PreviewBestCandidate();
        }

        private void PreviewBestCandidate()
        {
            GameStorageCandidate bestCandidate = _candidates
                .Where(x => x != null)
                .OrderByDescending(x => x.ConfidenceScore)
                .ThenBy(x => x.CandidateKind)
                .FirstOrDefault();

            if (bestCandidate == null)
                return;

            _control.SelectCandidate(bestCandidate);
            PreviewCandidate(bestCandidate);
        }

        private void PreviewCandidate(GameStorageCandidate candidate)
        {
            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetManualPaths(paths);
            SetHealth(_service.ValidateStorage(paths, false));
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
            return new GameStoragePathSet
            {
                GameId = currentPaths.GameId,
                GameName = currentPaths.GameName,
                GameInstallPath = currentPaths.GameInstallPath,
                InstallInfoPath = candidate.InstallInfoPath,
                ModsPath = candidate.ModsPath,
                VirtualInstallPath = candidate.VirtualInstallPath,
                LinkFolderPath = candidate.LinkFolderPath,
                LinkFolderRequired = candidate.LinkFolderRequired || _service.IsLinkFolderRequired(candidate.VirtualInstallPath, currentPaths.GameInstallPath),
                CompatibleSharedModsGameIds = currentPaths.CompatibleSharedModsGameIds == null
                    ? new List<string>()
                    : new List<string>(currentPaths.CompatibleSharedModsGameIds)
            };
        }
    }
}
