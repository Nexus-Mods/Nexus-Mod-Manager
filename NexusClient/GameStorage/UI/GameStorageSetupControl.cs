using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
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
        private readonly SimpleButton _manualLinkFolderButton;
        private readonly SimpleButton _legacySetupButton;
        private GridColumn _candidateUseColumn;
        private Image _candidateUseImage;
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
                Height = 150,
                ReadOnly = true,
                Text = "NMM could not validate the storage folders for this game. Select a known candidate or enter custom paths. NMM will not move, rename, or delete folders during recovery."
            };
            _descriptionEdit.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

            var manualPanel = new GroupControl { Text = "Selected folders", Dock = DockStyle.Top, Height = 138 };
            _manualInstallInfoEdit = CreateManualPathEdit(manualPanel, "Install info", 30);
            _manualModsEdit = CreateManualPathEdit(manualPanel, "Mod archives", 56);
            _manualVirtualInstallEdit = CreateManualPathEdit(manualPanel, "Virtual install", 82);
            _manualLinkFolderEdit = CreateManualPathEdit(manualPanel, "Link folder", 108);
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

            var healthGroup = new GroupControl { Text = "Selected folders check", Dock = DockStyle.Fill };
            healthGroup.Controls.Add(_healthGridControl);
            var candidateGroup = new GroupControl { Text = "Detected setup options", Dock = DockStyle.Fill };
            candidateGroup.Controls.Add(_candidateGridControl);
            splitContainer.Panel1.Controls.Add(healthGroup);
            splitContainer.Panel2.Controls.Add(candidateGroup);

            var buttonPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 44, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            var refreshButton = new SimpleButton { Text = "Refresh", Width = 90, Top = 8 };
            var applyButton = new SimpleButton { Text = "Apply selected", Width = 118, Top = 8 };
            _legacySetupButton = new SimpleButton { Text = "Keep legacy setup", Width = 128, Top = 8, Visible = false };
            var cancelButton = new SimpleButton { Text = "Cancel", Width = 90, Top = 8 };
            StyleActionButton(applyButton);
            StyleActionButton(_legacySetupButton);
            StyleCancelButton(cancelButton);
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
            if (e.Column != _candidateUseColumn || !_candidateGridView.IsDataRow(e.RowHandle))
                return;

            e.Appearance.DrawBackground(e.Cache, e.Bounds);
            if (_candidateUseImage != null)
            {
                int left = e.Bounds.Left + (e.Bounds.Width - _candidateUseImage.Width) / 2;
                int top = e.Bounds.Top + (e.Bounds.Height - _candidateUseImage.Height) / 2;
                e.Graphics.DrawImage(_candidateUseImage, left, top, _candidateUseImage.Width, _candidateUseImage.Height);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, "Use", e.Appearance.Font, e.Bounds, Color.FromArgb(55, 85, 110), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            ConfigureSetupGridLook(_candidateGridView, false);

            _candidateUseImage = LoadSvgIcon("game_storage_use.svg", 16);
            _candidateUseColumn = new GridColumn { Caption = "Select", Visible = true, VisibleIndex = 0, Width = 54 };
            _candidateUseColumn.OptionsColumn.AllowEdit = false;
            _candidateUseColumn.OptionsColumn.FixedWidth = true;
            _candidateGridView.Columns.Add(_candidateUseColumn);
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateKind), "Source", 1, 130));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.SharedModsDescription), "Shared Mods library", 2, 250));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.Recommendation), "Reason / recommendation", 3, 330));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceScore), "Score", 4, 60));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ConfidenceLevel), "Confidence", 5, 90));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.CandidateRoot), "Root", 6, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.InstallInfoPath), "Install info", 7, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.ModsPath), "Mod archives", 8, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.VirtualInstallPath), "Virtual install", 9, 260));
            _candidateGridView.Columns.Add(CreateReadOnlyColumn(nameof(GameStorageCandidate.LinkFolderPath), "Link folder", 10, 260));
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
            view.Appearance.Empty.BackColor = Color.FromArgb(252, 252, 253);
            view.Appearance.Empty.Options.UseBackColor = true;
            view.Appearance.Row.BackColor = Color.White;
            view.Appearance.Row.Options.UseBackColor = true;
            view.Appearance.EvenRow.BackColor = Color.FromArgb(250, 250, 252);
            view.Appearance.EvenRow.Options.UseBackColor = true;
            view.Appearance.OddRow.BackColor = Color.White;
            view.Appearance.OddRow.Options.UseBackColor = true;
            view.Appearance.FocusedRow.BackColor = Color.FromArgb(226, 238, 249);
            view.Appearance.FocusedRow.ForeColor = Color.Black;
            view.Appearance.FocusedRow.Options.UseBackColor = true;
            view.Appearance.FocusedRow.Options.UseForeColor = true;
            view.Appearance.SelectedRow.BackColor = Color.FromArgb(226, 238, 249);
            view.Appearance.SelectedRow.ForeColor = Color.Black;
            view.Appearance.SelectedRow.Options.UseBackColor = true;
            view.Appearance.SelectedRow.Options.UseForeColor = true;
            view.Appearance.HideSelectionRow.BackColor = Color.FromArgb(226, 238, 249);
            view.Appearance.HideSelectionRow.ForeColor = Color.Black;
            view.Appearance.HideSelectionRow.Options.UseBackColor = true;
            view.Appearance.HideSelectionRow.Options.UseForeColor = true;
            view.Appearance.HeaderPanel.BackColor = Color.FromArgb(247, 247, 249);
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
