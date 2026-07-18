using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.UI;
using Nexus.UI.Controls;

namespace Nexus.Client.GameStorage.UI
{
    public class GameStorageRecoveryForm : ManagedFontForm, IView
    {
        private readonly GameStorageService _service;
        private readonly IGameMode _gameMode;
        private readonly GameStorageSetupControl _control;
        private List<GameStorageCandidate> _candidates = new List<GameStorageCandidate>();

        public GameStorageRecoveryForm(GameStorageService service, IGameMode gameMode, GameStorageHealthCheck healthCheck)
        {
            _service = service;
            _gameMode = gameMode;
            Text = "Game Storage recovery - " + gameMode.Name;
            Width = 1120;
            Height = 680;
            MinimizeBox = false;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            _control = new GameStorageSetupControl();
            _control.ConfigureText("Game Storage recovery - " + gameMode.Name, "NMM could not validate the storage folders for this game. Select a known candidate or enter custom paths. Compatible shared Mods libraries are allowed only when both Game Mode definitions opt in. NMM will not move, rename, or delete folders during recovery.", false);
            _control.RefreshRequested += RefreshRequested;
            _control.ManualVirtualInstallPathChanged += ManualVirtualInstallPathChanged;
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
                MessageBox.Show(this, "Select a Game Storage candidate or enter custom paths first.", "Game Storage recovery", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (candidate.RequiresUserConfirmation)
            {
                string confirmationMessage = candidate.IsSharedModsLibrary
                    ? (candidate.SharedModsDescription ?? "This Mods folder is already used by a compatible Game Mode.") + Environment.NewLine + Environment.NewLine +
                      "Use it as a shared Mods library for " + _gameMode.Name + "? Only the Mods folder will be shared. InstallInfo, VirtualInstall, overwrite state, and the Link Folder remain exclusive to this Game Mode."
                    : "Apply the selected Game Storage paths for this game? NMM will update only this game's folder settings and will not move or delete any files.";

                var result = MessageBox.Show(this, confirmationMessage, "Confirm Game Storage recovery", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (result != DialogResult.OK)
                    return;
            }

            if (_service.ApplyRecoveryCandidate(_gameMode, candidate, out var healthCheck))
            {
                DialogResult = DialogResult.OK;
                return;
            }

            SetHealth(healthCheck);
            MessageBox.Show(this, healthCheck?.ToUserMessage() ?? "The selected Game Storage candidate could not be applied.", "Game Storage recovery", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                Role = x.Role?.ToString() ?? string.Empty,
                Path = x.Path,
                Status = x.Status.ToString(),
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
