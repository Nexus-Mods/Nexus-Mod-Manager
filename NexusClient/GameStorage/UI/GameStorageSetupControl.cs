using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.GameStorage.UI
{
    public class GameStorageSetupControl : XtraUserControl
    {
        private readonly LabelControl _titleLabel;
        private readonly MemoEdit _descriptionEdit;
        private readonly GridControl _healthGridControl;
        private readonly GridView _healthGridView;
        private readonly GridControl _candidateGridControl;
        private readonly GridView _candidateGridView;
        private readonly TextEdit _manualInstallInfoEdit;
        private readonly TextEdit _manualModsEdit;
        private readonly TextEdit _manualVirtualInstallEdit;
        private readonly TextEdit _manualLinkFolderEdit;
        private readonly SimpleButton _manualLinkFolderButton;
        private readonly SimpleButton _legacySetupButton;
        private readonly string _candidateUseText;
        private GridColumn _candidateUseColumn;
        private bool _suppressManualPathChanged;
        private bool _manualPathsEdited;
        private readonly List<Tuple<TextEdit, SimpleButton>> _manualPathRows = new List<Tuple<TextEdit, SimpleButton>>();

        public event EventHandler RefreshRequested;
        public event EventHandler ApplyRequested;
        public event EventHandler CandidatePreviewRequested;
        public event EventHandler ManualVirtualInstallPathChanged;
        public event EventHandler ManualPathsChanged;
        public event EventHandler CancelRequested;
        public event EventHandler LegacySetupRequested;

        public GameStorageSetupControl()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            _candidateUseText = LanguageManager.Get("GameStorage.Common.Use", "Use");

            _titleLabel = new LabelControl
            {
                Text = LanguageManager.Get("GameStorage.Recovery.GenericTitle", "Game Storage recovery"),
                Dock = DockStyle.Top,
                Height = 28
            };
            _titleLabel.Appearance.FontSizeDelta = 3;
            _titleLabel.Appearance.FontStyleDelta = FontStyle.Bold;

            _descriptionEdit = new MemoEdit
            {
                Dock = DockStyle.Top,
                Height = 150,
                ReadOnly = true,
                Text = LanguageManager.Get("GameStorage.Recovery.ControlDescription", "NMM could not validate the storage folders for this game. Select a known candidate or enter custom paths. NMM will not move, rename, or delete folders during recovery.")
            };
            _descriptionEdit.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

            var manualPanel = new GroupControl { Text = LanguageManager.Get("GameStorage.Common.SelectedFolders", "Selected folders"), Dock = DockStyle.Top, Height = 138 };
            _manualInstallInfoEdit = CreateManualPathEdit(manualPanel, LanguageManager.Get("GameStorage.Common.InstallInfo", "Install info"), 30);
            _manualModsEdit = CreateManualPathEdit(manualPanel, LanguageManager.Get("GameStorage.Common.ModArchives", "Mod archives"), 56);
            _manualVirtualInstallEdit = CreateManualPathEdit(manualPanel, LanguageManager.Get("GameStorage.Common.VirtualInstall", "Virtual install"), 82);
            _manualLinkFolderEdit = CreateManualPathEdit(manualPanel, LanguageManager.Get("GameStorage.Common.LinkFolder", "Link folder"), 108);
            _manualInstallInfoEdit.EditValueChanged += ManualPathEditValueChanged;
            _manualModsEdit.EditValueChanged += ManualPathEditValueChanged;
            _manualVirtualInstallEdit.EditValueChanged += ManualVirtualInstallEditValueChanged;
            _manualLinkFolderEdit.EditValueChanged += ManualPathEditValueChanged;
            _manualLinkFolderButton = _manualPathRows.Last().Item2;
            manualPanel.Resize += (sender, args) => LayoutManualPathRows(manualPanel);
            LayoutManualPathRows(manualPanel);

            var splitContainer = new SplitContainerControl
            {
                Dock = DockStyle.Fill,
                Horizontal = false,
                SplitterPosition = 170
            };

            _healthGridControl = new GridControl { Dock = DockStyle.Fill };
            _healthGridView = new GridView(_healthGridControl);
            _healthGridControl.MainView = _healthGridView;
            _healthGridControl.ViewCollection.Add(_healthGridView);
            ConfigureHealthGrid();

            _candidateGridControl = new GridControl { Dock = DockStyle.Fill };
            _candidateGridView = new GridView(_candidateGridControl);
            _candidateGridView.MouseDown += CandidateGridViewMouseDown;
            _candidateGridView.RowCellClick += CandidateGridViewRowCellClick;
            _candidateGridView.CustomDrawCell += CandidateGridViewCustomDrawCell;
            _candidateGridView.DoubleClick += (sender, args) => PreviewSelectedCandidate();
            _candidateGridControl.MainView = _candidateGridView;
            _candidateGridControl.ViewCollection.Add(_candidateGridView);
            ConfigureCandidateGrid();

            var healthGroup = new GroupControl { Text = LanguageManager.Get("GameStorage.Common.SelectedFoldersCheck", "Selected folders check"), Dock = DockStyle.Fill };
            healthGroup.Controls.Add(_healthGridControl);
            var candidateGroup = new GroupControl { Text = LanguageManager.Get("GameStorage.Common.DetectedSetupOptions", "Detected setup options"), Dock = DockStyle.Fill };
            candidateGroup.Controls.Add(_candidateGridControl);
            splitContainer.Panel1.Controls.Add(healthGroup);
            splitContainer.Panel2.Controls.Add(candidateGroup);

            var buttonPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 44, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var refreshButton = new SimpleButton { Text = LanguageManager.Get("Common.Action.Refresh", "Refresh"), Width = 90, Top = 8 };
            var applyButton = new SimpleButton { Text = LanguageManager.Get("GameStorage.Common.ApplySelected", "Apply selected"), Width = 118, Top = 8 };
            _legacySetupButton = new SimpleButton { Text = LanguageManager.Get("GameStorage.Common.KeepLegacySetup", "Keep legacy setup"), Width = 128, Top = 8, Visible = false };
            var cancelButton = new SimpleButton { Text = LanguageManager.Get("Common.Action.Cancel", "Cancel"), Width = 90, Top = 8 };
            NmmIconProvider.Bind(refreshButton, NmmIconAction.Refresh);
            NmmIconProvider.Bind(applyButton, NmmIconAction.Apply);
            NmmIconProvider.Bind(_legacySetupButton, NmmIconAction.Restore);
            NmmIconProvider.Bind(cancelButton, NmmIconAction.Cancel);
            refreshButton.Click += (sender, args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            applyButton.Click += (sender, args) => ApplyRequested?.Invoke(this, EventArgs.Empty);
            _legacySetupButton.Click += (sender, args) => LegacySetupRequested?.Invoke(this, EventArgs.Empty);
            cancelButton.Click += (sender, args) => CancelRequested?.Invoke(this, EventArgs.Empty);
            buttonPanel.Controls.Add(refreshButton);
            buttonPanel.Controls.Add(applyButton);
            buttonPanel.Controls.Add(_legacySetupButton);
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Resize += (sender, args) => LayoutButtons(buttonPanel, refreshButton, _legacySetupButton, cancelButton, applyButton);
            _legacySetupButton.VisibleChanged += (sender, args) => LayoutButtons(buttonPanel, refreshButton, _legacySetupButton, cancelButton, applyButton);
            LayoutButtons(buttonPanel, refreshButton, _legacySetupButton, cancelButton, applyButton);

            Controls.Add(splitContainer);
            Controls.Add(buttonPanel);
            Controls.Add(manualPanel);
            Controls.Add(_descriptionEdit);
            Controls.Add(_titleLabel);
        }

        public GameStorageCandidate SelectedCandidate => _manualPathsEdited ? null : _candidateGridView.GetFocusedRow() as GameStorageCandidate;

        public string ManualVirtualInstallPath => _manualVirtualInstallEdit.Text;

        public void PreviewCandidate(GameStorageCandidate candidate)
        {
            if (candidate == null)
                return;

            SetManualPathValues(
                candidate.InstallInfoPath,
                candidate.ModsPath,
                candidate.VirtualInstallPath,
                candidate.LinkFolderPath);
        }

        public GameStorageCandidate ManualCandidate
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_manualInstallInfoEdit.Text) && string.IsNullOrWhiteSpace(_manualModsEdit.Text) && string.IsNullOrWhiteSpace(_manualVirtualInstallEdit.Text))
                    return null;

                return new GameStorageCandidate
                {
                    CandidateKind = "Manual paths",
                    InstallInfoPath = _manualInstallInfoEdit.Text,
                    ModsPath = _manualModsEdit.Text,
                    VirtualInstallPath = _manualVirtualInstallEdit.Text,
                    LinkFolderPath = _manualLinkFolderEdit.Enabled ? _manualLinkFolderEdit.Text : null,
                    LinkFolderRequired = _manualLinkFolderEdit.Enabled,
                    ConfidenceScore = 55,
                    ConfidenceLevel = GameStorageCandidateConfidence.Medium,
                    RequiresUserConfirmation = true,
                    Evidence = { "User-entered custom Game Storage paths." }
                };
            }
        }

        private void SetManualPathValues(string installInfoPath, string modsPath, string virtualInstallPath, string linkFolderPath)
        {
            _suppressManualPathChanged = true;
            try
            {
                _manualInstallInfoEdit.Text = installInfoPath ?? string.Empty;
                _manualModsEdit.Text = modsPath ?? string.Empty;
                _manualVirtualInstallEdit.Text = virtualInstallPath ?? string.Empty;
                _manualLinkFolderEdit.Text = linkFolderPath ?? string.Empty;
                _manualPathsEdited = false;
            }
            finally
            {
                _suppressManualPathChanged = false;
            }
        }

        private void ManualPathEditValueChanged(object sender, EventArgs e)
        {
            OnManualPathsChanged(false);
        }

        private void ManualVirtualInstallEditValueChanged(object sender, EventArgs e)
        {
            OnManualPathsChanged(true);
        }

        private void OnManualPathsChanged(bool virtualInstallChanged)
        {
            if (_suppressManualPathChanged)
                return;

            _manualPathsEdited = true;
            _candidateGridView.ClearSelection();
            _candidateGridView.FocusedRowHandle = GridControl.InvalidRowHandle;

            if (virtualInstallChanged)
                ManualVirtualInstallPathChanged?.Invoke(this, EventArgs.Empty);

            ManualPathsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ConfigureText(string title, string description, bool showLegacySetupButton)
        {
            _titleLabel.Text = title;
            _descriptionEdit.Text = description;
            _legacySetupButton.Visible = showLegacySetupButton;
        }

        public void SetManualPaths(GameStoragePathSet paths)
        {
            SetManualPathValues(
                paths?.InstallInfoPath,
                paths?.ModsPath,
                paths?.VirtualInstallPath,
                paths?.LinkFolderPath);
            SetLinkFolderRequired(paths != null && paths.LinkFolderRequired);
        }

        public void SetLinkFolderRequired(bool required)
        {
            _manualLinkFolderEdit.Enabled = required;
            if (_manualLinkFolderButton != null)
                _manualLinkFolderButton.Enabled = required;
        }

        public void SetResolvedLinkFolderPath(string path, bool required)
        {
            SetLinkFolderRequired(required);
            if (!required || string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(_manualLinkFolderEdit.Text))
                return;

            _suppressManualPathChanged = true;
            try
            {
                _manualLinkFolderEdit.Text = path;
            }
            finally
            {
                _suppressManualPathChanged = false;
            }
        }

        public void SetRows(IEnumerable<GameStorageSetupRow> rows)
        {
            _healthGridControl.DataSource = rows?.ToList() ?? new List<GameStorageSetupRow>();
        }

        public void SetCandidates(IEnumerable<GameStorageCandidate> candidates)
        {
            _candidateGridControl.DataSource = candidates?.ToList() ?? new List<GameStorageCandidate>();
            _candidateGridView.RefreshData();
        }

        public void SelectCandidate(GameStorageCandidate candidate)
        {
            if (candidate == null)
                return;

            List<GameStorageCandidate> candidates = _candidateGridControl.DataSource as List<GameStorageCandidate>;
            if (candidates == null)
                return;

            int index = candidates.FindIndex(x => CandidateMatches(x, candidate));
            if (index < 0)
                return;

            int rowHandle = _candidateGridView.GetRowHandle(index);
            if (rowHandle < 0)
                return;

            _candidateGridView.FocusedRowHandle = rowHandle;
            _candidateGridView.ClearSelection();
            _candidateGridView.SelectRow(rowHandle);
        }

        private static bool CandidateMatches(GameStorageCandidate left, GameStorageCandidate right)
        {
            if (left == null || right == null)
                return false;

            return string.Equals(left.GameId, right.GameId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.StorageId, right.StorageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.InstallInfoPath, right.InstallInfoPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.ModsPath, right.ModsPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.VirtualInstallPath, right.VirtualInstallPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.LinkFolderPath, right.LinkFolderPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void LayoutButtons(PanelControl panel, SimpleButton refreshButton, SimpleButton legacySetupButton, SimpleButton cancelButton, SimpleButton applyButton)
        {
            const int top = 8;
            const int gap = 8;

            refreshButton.Left = 0;
            legacySetupButton.Left = refreshButton.Right + gap;

            applyButton.Left = panel.ClientSize.Width - applyButton.Width;
            cancelButton.Left = applyButton.Left - gap - cancelButton.Width;

            refreshButton.Top = top;
            legacySetupButton.Top = top;
            cancelButton.Top = top;
            applyButton.Top = top;
        }

        private TextEdit CreateManualPathEdit(Control parent, string caption, int top)
        {
            var label = new LabelControl { Text = caption, Left = 8, Top = top + 3, Width = 84 };
            var edit = new TextEdit { Left = 96, Top = top, Width = 724 };
            var button = new SimpleButton { Text = "...", Left = 828, Top = top - 1, Width = 28, Height = 22 };
            NmmIconProvider.Bind(button, NmmIconAction.Browse);
            button.Click += (sender, args) => BrowseForFolder(edit, caption);
            parent.Controls.Add(label);
            parent.Controls.Add(edit);
            parent.Controls.Add(button);
            _manualPathRows.Add(Tuple.Create(edit, button));
            return edit;
        }

        private void LayoutManualPathRows(Control parent)
        {
            const int editLeft = 96;
            const int buttonWidth = 28;
            const int rightPadding = 10;
            const int gap = 6;

            int buttonLeft = Math.Max(editLeft + 80, parent.ClientSize.Width - rightPadding - buttonWidth);
            int editWidth = Math.Max(80, buttonLeft - gap - editLeft);

            foreach (var row in _manualPathRows)
            {
                row.Item1.Left = editLeft;
                row.Item1.Width = editWidth;
                row.Item2.Left = buttonLeft;
            }
        }

        private void BrowseForFolder(TextEdit edit, string caption)
        {
            using (var dialog = new XtraFolderBrowserDialog())
            {
                dialog.Description = LanguageManager.Format("GameStorage.Common.SelectFolderPrompt", "Select {0} folder.", caption);
                if (!string.IsNullOrWhiteSpace(edit.Text))
                    dialog.SelectedPath = edit.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    edit.Text = dialog.SelectedPath;
            }
        }

        private void CandidateGridViewMouseDown(object sender, MouseEventArgs e)
        {
            var hitInfo = _candidateGridView.CalcHitInfo(e.Location);
            if (hitInfo.InRowCell || hitInfo.InRow)
                _candidateGridView.FocusedRowHandle = hitInfo.RowHandle;
        }

        private void PreviewSelectedCandidate()
        {
            var candidate = SelectedCandidate;
            if (candidate == null)
                return;

            PreviewCandidate(candidate);
            CandidatePreviewRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CandidateGridViewRowCellClick(object sender, DevExpress.XtraGrid.Views.Grid.RowCellClickEventArgs e)
        {
            if (e.Column == _candidateUseColumn)
                PreviewSelectedCandidate();
        }

        private void CandidateGridViewCustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column != _candidateUseColumn || !_candidateGridView.IsDataRow(e.RowHandle))
                return;

            e.Appearance.DrawBackground(e.Cache, e.Bounds);
            Image candidateUseImage = NmmIconProvider.GetBitmap(NmmIconAction.Apply, 16, false);
            if (candidateUseImage != null)
            {
                int left = e.Bounds.Left + (e.Bounds.Width - candidateUseImage.Width) / 2;
                int top = e.Bounds.Top + (e.Bounds.Height - candidateUseImage.Height) / 2;
                e.Graphics.DrawImage(candidateUseImage, left, top, candidateUseImage.Width, candidateUseImage.Height);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, _candidateUseText, e.Appearance.Font, e.Bounds, e.Appearance.ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            e.Handled = true;
        }

        private void ConfigureHealthGrid()
        {
            ConfigureSetupGridLook(_healthGridView, false);
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Role), LanguageManager.Get("GameStorage.Columns.Folder", "Folder"), 0, 105));
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Path), LanguageManager.Get("GameStorage.Columns.Path", "Path"), 1, 330));
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Status), LanguageManager.Get("GameStorage.Columns.Status", "Status"), 2, 130));
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Message), LanguageManager.Get("GameStorage.Columns.Message", "Message"), 3, 360));
        }

        private GridColumn CreateReadOnlyColumn(string fieldName, string caption, int visibleIndex, int width)
        {
            var column = new GridColumn { FieldName = fieldName, Caption = caption, Visible = true, VisibleIndex = visibleIndex, Width = width };
            column.OptionsColumn.AllowEdit = false;
            return column;
        }

        private void ConfigureCandidateGrid()
        {
            ConfigureSetupGridLook(_candidateGridView, false);

            _candidateUseColumn = new GridColumn { Caption = LanguageManager.Get("GameStorage.Columns.Select", "Select"), Visible = true, VisibleIndex = 0, Width = 54 };
            _candidateUseColumn.OptionsColumn.AllowEdit = false;
            _candidateUseColumn.OptionsColumn.FixedWidth = true;
            _candidateGridView.Columns.Add(_candidateUseColumn);
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateKindDisplay), LanguageManager.Get("GameStorage.Columns.Source", "Source"), 1, 130));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.SharedModsDescription), LanguageManager.Get("GameStorage.Columns.SharedModsLibrary", "Shared Mods library"), 2, 250));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.RecommendationDisplay), LanguageManager.Get("GameStorage.Columns.ReasonRecommendation", "Reason / recommendation"), 3, 330));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceScore), LanguageManager.Get("GameStorage.Columns.Score", "Score"), 4, 60));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceDisplay), LanguageManager.Get("GameStorage.Columns.Confidence", "Confidence"), 5, 90));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateRoot), LanguageManager.Get("GameStorage.Columns.Root", "Root"), 6, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.InstallInfoPath), LanguageManager.Get("GameStorage.Common.InstallInfo", "Install info"), 7, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ModsPath), LanguageManager.Get("GameStorage.Common.ModArchives", "Mod archives"), 8, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.VirtualInstallPath), LanguageManager.Get("GameStorage.Common.VirtualInstall", "Virtual install"), 9, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.LinkFolderPath), LanguageManager.Get("GameStorage.Common.LinkFolder", "Link folder"), 10, 260));
        }

        private static void ConfigureSetupGridLook(GridView view, bool editable)
        {
            view.OptionsBehavior.Editable = editable;
            view.OptionsView.ShowGroupPanel = false;
            view.OptionsView.ShowIndicator = false;
            view.OptionsView.EnableAppearanceEvenRow = true;
            view.OptionsView.EnableAppearanceOddRow = true;
            view.OptionsView.ColumnAutoWidth = true;
            view.OptionsView.ShowHorizontalLines = DefaultBoolean.True;
            view.OptionsView.ShowVerticalLines = DefaultBoolean.False;
            view.OptionsSelection.EnableAppearanceFocusedCell = false;
        }
    }

    public class GameStorageSetupRow
    {
        public string Role { get; set; }
        public string Path { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
    }
}
