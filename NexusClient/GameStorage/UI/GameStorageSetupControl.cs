using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;

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
        private readonly SimpleButton _legacySetupButton;

        public event EventHandler BrowseRootRequested;
        public event EventHandler RefreshRequested;
        public event EventHandler ApplyRequested;
        public event EventHandler CandidatePreviewRequested;
        public event EventHandler CancelRequested;
        public event EventHandler LegacySetupRequested;

        public GameStorageSetupControl()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);

            _titleLabel = new LabelControl
            {
                Text = "Game Storage recovery",
                Dock = DockStyle.Top,
                Height = 28,
                Appearance = { Font = new Font("Segoe UI", 12f, FontStyle.Bold) }
            };

            _descriptionEdit = new MemoEdit
            {
                Dock = DockStyle.Top,
                Height = 78,
                ReadOnly = true,
                Text = "NMM could not validate the storage folders for this game. Select a known candidate or enter custom paths. NMM will not move, rename, or delete folders during recovery."
            };
            _descriptionEdit.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

            var manualPanel = new GroupControl { Text = "Current folders", Dock = DockStyle.Top, Height = 138 };
            _manualInstallInfoEdit = CreateManualPathEdit(manualPanel, "Install info", 30);
            _manualModsEdit = CreateManualPathEdit(manualPanel, "Mod archives", 56);
            _manualVirtualInstallEdit = CreateManualPathEdit(manualPanel, "Virtual install", 82);
            _manualLinkFolderEdit = CreateManualPathEdit(manualPanel, "Link folder", 108);

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
            _candidateGridView.DoubleClick += (sender, args) => PreviewSelectedCandidate();
            _candidateGridControl.MainView = _candidateGridView;
            _candidateGridControl.ViewCollection.Add(_candidateGridView);
            ConfigureCandidateGrid();

            var healthGroup = new GroupControl { Text = "Current validation", Dock = DockStyle.Fill };
            healthGroup.Controls.Add(_healthGridControl);
            var candidateGroup = new GroupControl { Text = "Available setups", Dock = DockStyle.Fill };
            candidateGroup.Controls.Add(_candidateGridControl);
            splitContainer.Panel1.Controls.Add(healthGroup);
            splitContainer.Panel2.Controls.Add(candidateGroup);

            var buttonPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 44, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var browseRootButton = new SimpleButton { Text = "Browse root...", Width = 100, Top = 8 };
            var refreshButton = new SimpleButton { Text = "Refresh", Width = 90, Top = 8 };
            var applyButton = new SimpleButton { Text = "Apply selected", Width = 118, Top = 8 };
            _legacySetupButton = new SimpleButton { Text = "Keep legacy setup", Width = 128, Top = 8, Visible = false };
            var cancelButton = new SimpleButton { Text = "Cancel", Width = 90, Top = 8 };
            StyleActionButton(applyButton);
            StyleActionButton(_legacySetupButton);
            StyleCancelButton(cancelButton);
            browseRootButton.Click += (sender, args) => BrowseRootRequested?.Invoke(this, EventArgs.Empty);
            refreshButton.Click += (sender, args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            applyButton.Click += (sender, args) => ApplyRequested?.Invoke(this, EventArgs.Empty);
            _legacySetupButton.Click += (sender, args) => LegacySetupRequested?.Invoke(this, EventArgs.Empty);
            cancelButton.Click += (sender, args) => CancelRequested?.Invoke(this, EventArgs.Empty);
            buttonPanel.Controls.Add(browseRootButton);
            buttonPanel.Controls.Add(refreshButton);
            buttonPanel.Controls.Add(applyButton);
            buttonPanel.Controls.Add(_legacySetupButton);
            buttonPanel.Controls.Add(cancelButton);
            buttonPanel.Resize += (sender, args) => LayoutButtons(buttonPanel, browseRootButton, refreshButton, _legacySetupButton, cancelButton, applyButton);
            _legacySetupButton.VisibleChanged += (sender, args) => LayoutButtons(buttonPanel, browseRootButton, refreshButton, _legacySetupButton, cancelButton, applyButton);
            LayoutButtons(buttonPanel, browseRootButton, refreshButton, _legacySetupButton, cancelButton, applyButton);

            Controls.Add(splitContainer);
            Controls.Add(buttonPanel);
            Controls.Add(manualPanel);
            Controls.Add(_descriptionEdit);
            Controls.Add(_titleLabel);
        }

        public GameStorageCandidate SelectedCandidate => _candidateGridView.GetFocusedRow() as GameStorageCandidate;

        public void PreviewCandidate(GameStorageCandidate candidate)
        {
            if (candidate == null)
                return;

            _manualInstallInfoEdit.Text = candidate.InstallInfoPath ?? string.Empty;
            _manualModsEdit.Text = candidate.ModsPath ?? string.Empty;
            _manualVirtualInstallEdit.Text = candidate.VirtualInstallPath ?? string.Empty;
            _manualLinkFolderEdit.Text = candidate.LinkFolderPath ?? string.Empty;
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
                    LinkFolderPath = _manualLinkFolderEdit.Text,
                    ConfidenceScore = 55,
                    ConfidenceLevel = GameStorageCandidateConfidence.Medium,
                    RequiresUserConfirmation = true,
                    Evidence = { "User-entered custom Game Storage paths." }
                };
            }
        }

        public void ConfigureText(string title, string description, bool showLegacySetupButton)
        {
            _titleLabel.Text = title;
            _descriptionEdit.Text = description;
            _legacySetupButton.Visible = showLegacySetupButton;
        }

        public void SetManualPaths(GameStoragePathSet paths)
        {
            _manualInstallInfoEdit.Text = paths?.InstallInfoPath ?? string.Empty;
            _manualModsEdit.Text = paths?.ModsPath ?? string.Empty;
            _manualVirtualInstallEdit.Text = paths?.VirtualInstallPath ?? string.Empty;
            _manualLinkFolderEdit.Text = paths?.LinkFolderPath ?? string.Empty;
        }

        public void SetRows(IEnumerable<GameStorageSetupRow> rows)
        {
            _healthGridControl.DataSource = rows?.ToList() ?? new List<GameStorageSetupRow>();
            _healthGridView.BestFitColumns();
        }

        public void SetCandidates(IEnumerable<GameStorageCandidate> candidates)
        {
            _candidateGridControl.DataSource = candidates?.ToList() ?? new List<GameStorageCandidate>();
            _candidateGridView.BestFitColumns();
        }

        private static void StyleActionButton(SimpleButton button)
        {
            button.LookAndFeel.UseDefaultLookAndFeel = false;
            button.Appearance.BackColor = Color.FromArgb(218, 241, 220);
            button.Appearance.BorderColor = Color.FromArgb(159, 205, 163);
            button.Appearance.ForeColor = Color.FromArgb(35, 86, 42);
            button.Appearance.Options.UseBackColor = true;
            button.Appearance.Options.UseBorderColor = true;
            button.Appearance.Options.UseForeColor = true;
        }

        private static void StyleCancelButton(SimpleButton button)
        {
            button.LookAndFeel.UseDefaultLookAndFeel = false;
            button.Appearance.BackColor = Color.FromArgb(250, 224, 213);
            button.Appearance.BorderColor = Color.FromArgb(222, 156, 132);
            button.Appearance.ForeColor = Color.FromArgb(119, 53, 38);
            button.Appearance.Options.UseBackColor = true;
            button.Appearance.Options.UseBorderColor = true;
            button.Appearance.Options.UseForeColor = true;
        }

        private static void LayoutButtons(PanelControl panel, SimpleButton browseRootButton, SimpleButton refreshButton, SimpleButton legacySetupButton, SimpleButton cancelButton, SimpleButton applyButton)
        {
            const int top = 8;
            const int gap = 8;

            browseRootButton.Left = 0;
            refreshButton.Left = browseRootButton.Right + gap;
            legacySetupButton.Left = refreshButton.Right + gap;

            applyButton.Left = panel.ClientSize.Width - applyButton.Width;
            cancelButton.Left = applyButton.Left - gap - cancelButton.Width;

            browseRootButton.Top = top;
            refreshButton.Top = top;
            legacySetupButton.Top = top;
            cancelButton.Top = top;
            applyButton.Top = top;
        }

        private TextEdit CreateManualPathEdit(Control parent, string caption, int top)
        {
            var label = new LabelControl { Text = caption, Left = 8, Top = top + 3, Width = 84 };
            var edit = new TextEdit { Left = 96, Top = top, Width = 724, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            var button = new SimpleButton { Text = "...", Left = 828, Top = top - 1, Width = 28, Height = 22, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            button.Click += (sender, args) => BrowseForFolder(edit, caption);
            parent.Controls.Add(label);
            parent.Controls.Add(edit);
            parent.Controls.Add(button);
            return edit;
        }

        private void BrowseForFolder(TextEdit edit, string caption)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select " + caption + " folder.";
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
        private void ConfigureHealthGrid()
        {
            _healthGridView.OptionsBehavior.Editable = false;
            _healthGridView.OptionsView.ShowGroupPanel = false;
            _healthGridView.OptionsView.ShowIndicator = false;
            _healthGridView.OptionsView.EnableAppearanceEvenRow = true;
            _healthGridView.OptionsView.EnableAppearanceOddRow = true;
            _healthGridView.OptionsView.ColumnAutoWidth = false;
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Role), Caption = "Folder", Visible = true, VisibleIndex = 0, Width = 110 });
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Path), Caption = "Path", Visible = true, VisibleIndex = 1, Width = 360 });
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Status), Caption = "Status", Visible = true, VisibleIndex = 2, Width = 130 });
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Message), Caption = "Message", Visible = true, VisibleIndex = 3, Width = 360 });
        }

        private GridColumn CreateReadOnlyColumn(string fieldName, string caption, int visibleIndex, int width)
        {
            var column = new GridColumn { FieldName = fieldName, Caption = caption, Visible = true, VisibleIndex = visibleIndex, Width = width };
            column.OptionsColumn.AllowEdit = false;
            return column;
        }
        private void ConfigureCandidateGrid()
        {
            _candidateGridView.OptionsBehavior.Editable = true;
            _candidateGridView.OptionsView.ShowGroupPanel = false;
            _candidateGridView.OptionsView.ShowIndicator = false;
            _candidateGridView.OptionsView.EnableAppearanceEvenRow = true;
            _candidateGridView.OptionsView.EnableAppearanceOddRow = true;
            _candidateGridView.OptionsView.ColumnAutoWidth = false;

            var useButtonEdit = new RepositoryItemButtonEdit { TextEditStyle = TextEditStyles.HideTextEditor };
            useButtonEdit.Buttons.Clear();
            useButtonEdit.Buttons.Add(new EditorButton(ButtonPredefines.Glyph) { Caption = "Use" });
            useButtonEdit.ButtonClick += (sender, args) => PreviewSelectedCandidate();
            _candidateGridControl.RepositoryItems.Add(useButtonEdit);

            var useColumn = new GridColumn { Caption = "", Visible = true, VisibleIndex = 0, Width = 54, ColumnEdit = useButtonEdit };
            useColumn.OptionsColumn.AllowEdit = true;
            useColumn.OptionsColumn.FixedWidth = true;
            _candidateGridView.Columns.Add(useColumn);
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateKind), "Source", 1, 130));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceScore), "Score", 2, 60));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceLevel), "Confidence", 3, 90));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateRoot), "Root", 4, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.InstallInfoPath), "Install info", 5, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ModsPath), "Mod archives", 6, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.VirtualInstallPath), "Virtual install", 7, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.LinkFolderPath), "Link folder", 8, 260));
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
