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
            _control.BrowseRootRequested += BrowseRootRequested;
            _control.RefreshRequested += RefreshRequested;
            _control.ApplyRequested += ApplyRequested;
            _control.CancelRequested += (sender, args) => DialogResult = DialogResult.Cancel;
            Controls.Add(_control);

            SetHealth(healthCheck);
            RefreshCandidates();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameStorageCandidate SelectedCandidate => _control.SelectedCandidate;

        private void BrowseRootRequested(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a Game Storage folder or a folder that contains Game Storage folders.";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                foreach (var candidate in _service.DiscoverRecoveryCandidatesFromRoot(_gameMode, dialog.SelectedPath))
                    AddCandidate(candidate);
                _control.SetCandidates(_candidates);
            }
        }

        private void RefreshRequested(object sender, EventArgs e)
        {
            RefreshCandidates();
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

        private void AddCandidate(GameStorageCandidate candidate)
        {
            if (candidate == null)
                return;
            string key = string.Join("|", candidate.GameId, candidate.StorageId, candidate.InstallInfoPath, candidate.ModsPath, candidate.VirtualInstallPath, candidate.LinkFolderPath);
            if (_candidates.Any(x => string.Equals(string.Join("|", x.GameId, x.StorageId, x.InstallInfoPath, x.ModsPath, x.VirtualInstallPath, x.LinkFolderPath), key, StringComparison.OrdinalIgnoreCase)))
                return;
            _candidates.Add(candidate);
            _candidates = _candidates.OrderByDescending(x => x.ConfidenceScore).ToList();
        }
    }
}
