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
    public class GameStorageSetupForm : ManagedFontXtraForm, IView
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
            Text = LanguageManager.Format("GameStorage.Setup.Title", "Game Storage setup - {0}", currentPaths.GameName);
            Width = 1120;
            Height = 680;
            MinimizeBox = false;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterParent;

            _control = new GameStorageSetupControl();
            _control.ConfigureText(LanguageManager.Format("GameStorage.Setup.Title", "Game Storage setup - {0}", currentPaths.GameName),
                LanguageManager.Get("GameStorage.Setup.Description", "Choose where NMM should store this game's mod data. If NMM found an existing setup, use the recommended folders to preserve your current mods, install records, and mod archives. NMM will only create missing folders inside the paths selected below." + Environment.NewLine + Environment.NewLine +
                "Selected folders are the directories NMM will use for this game. You can edit them manually or choose one of the detected setups below." + Environment.NewLine + Environment.NewLine +
                "Selected folders check parses the selected folders for existing mod data, missing folders, invalid paths, and setup problems before applying the configuration." + Environment.NewLine + Environment.NewLine +
                "Detected setup options are the possible folder setups on your system. Compatible shared Mods libraries are listed separately and never replace this game's InstallInfo, VirtualInstall, overwrite state, or Link Folder."), true);
            _control.SetManualPaths(currentPaths);
            _control.RefreshRequested += RefreshRequested;
            _control.ManualVirtualInstallPathChanged += ManualVirtualInstallPathChanged;
            _control.ManualPathsChanged += ManualPathsChanged;
            _control.ApplyRequested += ApplyRequested;
            _control.CandidatePreviewRequested += CandidatePreviewRequested;
            _control.LegacySetupRequested += LegacySetupRequested;
            _control.CancelRequested += CancelRequested;
            Controls.Add(_control);

            SetHealth(healthCheck);
            RefreshCandidates();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public GameStorageCandidate SelectedCandidate { get; private set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool UseLegacySetup { get; private set; }

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
            _control.SetLinkFolderRequired(_service.IsLinkFolderRequired(_control.ManualVirtualInstallPath, _currentPaths.GameInstallPath));
        }

        private void ManualPathsChanged(object sender, EventArgs e)
        {
            var candidate = _control.ManualCandidate;
            if (candidate == null)
                return;

            candidate.GameId = _currentPaths.GameId;
            candidate.LinkFolderRequired = candidate.LinkFolderRequired ||
                _service.IsLinkFolderRequired(candidate.VirtualInstallPath, _currentPaths.GameInstallPath);

            var paths = CreatePathSetFromCandidate(candidate);
            _control.SetLinkFolderRequired(paths.LinkFolderRequired);
            SetHealth(_service.ValidateStorage(paths, false));
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
