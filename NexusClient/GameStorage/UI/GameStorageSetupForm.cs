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
    public class GameStorageSetupForm : ManagedFontForm, IView
    {
        private readonly GameStorageService _service;
        private readonly GameStoragePathSet _currentPaths;
        private readonly GameStorageHealthCheck _currentHealthCheck;
        private readonly GameStorageSetupControl _control;
        private List<GameStorageCandidate> _candidates = new List<GameStorageCandidate>();

        public GameStorageSetupForm(GameStorageService service, GameStoragePathSet currentPaths, GameStorageHealthCheck healthCheck)
        {
            _service = service;
            _currentPaths = currentPaths;
            _currentHealthCheck = healthCheck;
            Text = "Game Storage setup - " + currentPaths.GameName;
            Width = 1120;
            Height = 680;
            MinimizeBox = false;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            _control = new GameStorageSetupControl();
            _control.ConfigureText("Game Storage setup - " + currentPaths.GameName, "Select persistent folders for this game. These folders store mod archives, install records, and the virtual install staging area. NMM will create missing folders only for the paths you choose here.", true);
            _control.SetManualPaths(currentPaths);
            _control.BrowseRootRequested += BrowseRootRequested;
            _control.RefreshRequested += RefreshRequested;
            _control.ApplyRequested += ApplyRequested;
            _control.CandidatePreviewRequested += CandidatePreviewRequested;
            _control.LegacySetupRequested += LegacySetupRequested;
            _control.CancelRequested += (sender, args) => DialogResult = DialogResult.Cancel;
            Controls.Add(_control);

            SetHealth(healthCheck);
            RefreshCandidates();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameStorageCandidate SelectedCandidate { get; private set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool UseLegacySetup { get; private set; }

        private void BrowseRootRequested(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a Game Storage folder or a folder that contains Game Storage folders.";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                foreach (var candidate in _service.DiscoverRecoveryCandidatesFromRoot(_currentPaths, dialog.SelectedPath))
                    AddCandidate(candidate);
                _control.SetCandidates(_candidates);
                PreviewBestCandidateIfCurrentIsNotUsable();
            }
        }

        private void RefreshRequested(object sender, EventArgs e)
        {
            RefreshCandidates();
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
                manualCandidate.GameId = _currentPaths.GameId;
                manualCandidate.LinkFolderRequired = manualCandidate.LinkFolderRequired || _service.IsLinkFolderRequired(manualCandidate.VirtualInstallPath, _currentPaths.GameInstallPath);
            }

            if (candidate == null)
            {
                MessageBox.Show(this, "Select a Game Storage candidate or enter custom paths first.", "Game Storage setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (candidate.RequiresUserConfirmation)
            {
                var result = MessageBox.Show(this, "Use the selected Game Storage paths for this game? NMM will not move, rename, or delete existing folders.", "Confirm Game Storage setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
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

        private void RefreshCandidates()
        {
            _candidates = _service.DiscoverRecoveryCandidates(_currentPaths);
            _control.SetCandidates(_candidates);
            PreviewBestCandidateIfCurrentIsNotUsable();
        }


        private void PreviewBestCandidateIfCurrentIsNotUsable()
        {
            if (_currentHealthCheck != null && _currentHealthCheck.IsHealthy)
                return;

            GameStorageCandidate currentCandidate = _candidates.FirstOrDefault(x => string.Equals(x.CandidateKind, "Proposed setup", StringComparison.OrdinalIgnoreCase));
            GameStorageCandidate bestCandidate = _candidates
                .Where(x => x != null && !string.Equals(x.CandidateKind, "Proposed setup", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ConfidenceScore)
                .ThenBy(x => x.CandidateKind)
                .FirstOrDefault();

            if (bestCandidate == null)
                return;
            if (currentCandidate != null && currentCandidate.ConfidenceScore >= bestCandidate.ConfidenceScore)
                return;

            _control.SelectCandidate(bestCandidate);
            GameStoragePathSet paths = CreatePathSetFromCandidate(bestCandidate);
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
            return new GameStoragePathSet
            {
                GameId = _currentPaths.GameId,
                GameName = _currentPaths.GameName,
                GameInstallPath = _currentPaths.GameInstallPath,
                InstallInfoPath = candidate.InstallInfoPath,
                ModsPath = candidate.ModsPath,
                VirtualInstallPath = candidate.VirtualInstallPath,
                LinkFolderPath = candidate.LinkFolderPath,
                LinkFolderRequired = candidate.LinkFolderRequired || _service.IsLinkFolderRequired(candidate.VirtualInstallPath, _currentPaths.GameInstallPath)
            };
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
