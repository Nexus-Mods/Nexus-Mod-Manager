using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
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
        private GridColumn _candidateUseColumn;
        private Image _candidateUseImage;
        private readonly List<Tuple<TextEdit, SimpleButton>> _manualPathRows = new List<Tuple<TextEdit, SimpleButton>>();

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
        }

        public void SetCandidates(IEnumerable<GameStorageCandidate> candidates)
        {
            _candidateGridControl.DataSource = candidates?.ToList() ?? new List<GameStorageCandidate>();
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

        private static Image LoadSvgIcon(string resourceName, int size)
        {
            var assembly = typeof(GameStorageSetupControl).Assembly;
            string fullName = assembly.GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase));
            if (fullName == null) return null;

            using (Stream stream = assembly.GetManifestResourceStream(fullName))
            {
                if (stream == null) return null;
                var svgImage = DevExpress.Utils.Svg.SvgImage.FromStream(stream);
                var svgBitmap = DevExpress.Utils.Svg.SvgBitmap.Create(svgImage);
                return svgBitmap.Render(new Size(size, size), null, DefaultBoolean.False, DefaultBoolean.False);
            }
        }
        private TextEdit CreateManualPathEdit(Control parent, string caption, int top)
        {
            var label = new LabelControl { Text = caption, Left = 8, Top = top + 3, Width = 84 };
            var edit = new TextEdit { Left = 96, Top = top, Width = 724 };
            var button = new SimpleButton { Text = "...", Left = 828, Top = top - 1, Width = 28, Height = 22 };
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

        private void CandidateGridViewRowCellClick(object sender, DevExpress.XtraGrid.Views.Grid.RowCellClickEventArgs e)
        {
            if (e.Column == _candidateUseColumn)
                PreviewSelectedCandidate();
        }

        private void CandidateGridViewCustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column != _candidateUseColumn)
                return;

            e.Appearance.DrawBackground(e.Cache, e.Bounds);
            Rectangle buttonBounds = new Rectangle(e.Bounds.Left + 13, e.Bounds.Top + 3, 28, Math.Max(18, e.Bounds.Height - 6));
            using (var background = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(177, 186, 196)))
            {
                e.Graphics.FillRectangle(background, buttonBounds);
                e.Graphics.DrawRectangle(border, buttonBounds);
            }

            if (_candidateUseImage != null)
            {
                int left = buttonBounds.Left + (buttonBounds.Width - _candidateUseImage.Width) / 2;
                int top = buttonBounds.Top + (buttonBounds.Height - _candidateUseImage.Height) / 2;
                e.Graphics.DrawImage(_candidateUseImage, left, top, _candidateUseImage.Width, _candidateUseImage.Height);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, "Use", e.Appearance.Font, buttonBounds, Color.FromArgb(55, 85, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            e.Handled = true;
        }
        private void ConfigureHealthGrid()
        {
            ConfigureSetupGridLook(_healthGridView, false);
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Role), "Folder", 0, 105));
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Path), "Path", 1, 330));
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Status), "Status", 2, 130));
            _healthGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageSetupRow.Message), "Message", 3, 360));
        }

        private GridColumn CreateReadOnlyColumn(string fieldName, string caption, int visibleIndex, int width)
        {
            var column = new GridColumn { FieldName = fieldName, Caption = caption, Visible = true, VisibleIndex = visibleIndex, Width = width };
            column.OptionsColumn.AllowEdit = false;
            return column;
        }
        private void ConfigureCandidateGrid()
        {
            ConfigureSetupGridLook(_candidateGridView, true);

            var useButtonEdit = new RepositoryItemButtonEdit { TextEditStyle = TextEditStyles.HideTextEditor };
            useButtonEdit.Buttons.Clear();
            var useButton = new EditorButton(ButtonPredefines.Glyph) { Caption = string.Empty };
            _candidateUseImage = LoadSvgIcon("game_storage_use.svg", 16);
            if (_candidateUseImage != null)
                useButton.ImageOptions.Image = _candidateUseImage;
            else
                useButton.Caption = "Use";
            useButtonEdit.Buttons.Add(useButton);
            useButtonEdit.ButtonClick += (sender, args) => PreviewSelectedCandidate();
            _candidateGridControl.RepositoryItems.Add(useButtonEdit);

            _candidateUseColumn = new GridColumn { Caption = "", Visible = true, VisibleIndex = 0, Width = 54, ColumnEdit = useButtonEdit };
            _candidateUseColumn.OptionsColumn.AllowEdit = true;
            _candidateUseColumn.OptionsColumn.FixedWidth = true;
            _candidateGridView.Columns.Add(_candidateUseColumn);
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateKind), "Source", 1, 130));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceScore), "Score", 2, 60));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceLevel), "Confidence", 3, 90));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateRoot), "Root", 4, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.InstallInfoPath), "Install info", 5, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ModsPath), "Mod archives", 6, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.VirtualInstallPath), "Virtual install", 7, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.LinkFolderPath), "Link folder", 8, 260));
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
            view.Appearance.Empty.BackColor = Color.FromArgb(245, 245, 248);
            view.Appearance.Empty.Options.UseBackColor = true;
            view.Appearance.Row.BackColor = Color.FromArgb(248, 248, 251);
            view.Appearance.Row.Options.UseBackColor = true;
            view.Appearance.EvenRow.BackColor = Color.FromArgb(242, 242, 246);
            view.Appearance.EvenRow.Options.UseBackColor = true;
            view.Appearance.OddRow.BackColor = Color.FromArgb(248, 248, 251);
            view.Appearance.OddRow.Options.UseBackColor = true;
            view.Appearance.FocusedRow.BackColor = Color.FromArgb(184, 207, 229);
            view.Appearance.FocusedRow.ForeColor = Color.Black;
            view.Appearance.FocusedRow.Options.UseBackColor = true;
            view.Appearance.FocusedRow.Options.UseForeColor = true;
            view.Appearance.SelectedRow.BackColor = Color.FromArgb(184, 207, 229);
            view.Appearance.SelectedRow.ForeColor = Color.Black;
            view.Appearance.SelectedRow.Options.UseBackColor = true;
            view.Appearance.SelectedRow.Options.UseForeColor = true;
            view.Appearance.HideSelectionRow.BackColor = Color.FromArgb(184, 207, 229);
            view.Appearance.HideSelectionRow.ForeColor = Color.Black;
            view.Appearance.HideSelectionRow.Options.UseBackColor = true;
            view.Appearance.HideSelectionRow.Options.UseForeColor = true;
            view.Appearance.HeaderPanel.BackColor = Color.FromArgb(238, 238, 241);
            view.Appearance.HeaderPanel.ForeColor = Color.FromArgb(80, 80, 80);
            view.Appearance.HeaderPanel.Options.UseBackColor = true;
            view.Appearance.HeaderPanel.Options.UseForeColor = true;
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
