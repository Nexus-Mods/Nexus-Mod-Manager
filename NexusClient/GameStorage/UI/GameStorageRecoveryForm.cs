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
            _control.ConfigureText("Game Storage recovery - " + gameMode.Name, "NMM could not validate the storage folders for this game. Select a known candidate or enter custom paths. NMM will not move, rename, or delete folders during recovery.", false);
            _control.RefreshRequested += RefreshRequested;
            _control.ManualVirtualInstallPathChanged += ManualVirtualInstallPathChanged;
            _control.ApplyRequested += ApplyRequested;
            _control.CandidatePreviewRequested += CandidatePreviewRequested;
            _control.CancelRequested += (sender, args) => DialogResult = DialogResult.Cancel;
            Controls.Add(_control);

            SetHealth(healthCheck);
            RefreshCandidates();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameStorageCandidate SelectedCandidate => _control.SelectedCandidate;

        private void RefreshRequested(object sender, EventArgs e)
        {
            RefreshCandidates();
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

            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetManualPaths(paths);
            SetHealth(_service.ValidateStorage(paths, false));
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
                var result = MessageBox.Show(this, "Apply the selected Game Storage paths for this game? NMM will update only this game's folder settings and will not move or delete any files.", "Confirm Game Storage recovery", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
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
            return new GameStoragePathSet
            {
                GameId = _gameMode.ModeId,
                GameName = _gameMode.Name,
                GameInstallPath = _gameMode.GameModeEnvironmentInfo.InstallationPath,
                InstallInfoPath = candidate.InstallInfoPath,
                ModsPath = candidate.ModsPath,
                VirtualInstallPath = candidate.VirtualInstallPath,
                LinkFolderPath = candidate.LinkFolderPath,
                LinkFolderRequired = candidate.LinkFolderRequired || _service.IsLinkFolderRequired(candidate.VirtualInstallPath, _gameMode.GameModeEnvironmentInfo.InstallationPath)
            };
        }
    }
}
