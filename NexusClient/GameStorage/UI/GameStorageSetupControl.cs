using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
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
        private readonly SimpleButton _browseRootButton;
        private readonly SimpleButton _refreshButton;
        private readonly SimpleButton _applyButton;
        private readonly SimpleButton _cancelButton;

        public event EventHandler BrowseRootRequested;
        public event EventHandler RefreshRequested;
        public event EventHandler ApplyRequested;
        public event EventHandler CancelRequested;

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
                Height = 86,
                ReadOnly = true,
                Text = "NMM could not validate the storage folders for this game. Select a known or discovered Game Storage candidate to restore the per-game paths. NMM will not move, rename, or delete folders during recovery."
            };
            _descriptionEdit.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

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
            _candidateGridControl.MainView = _candidateGridView;
            _candidateGridControl.ViewCollection.Add(_candidateGridView);
            ConfigureCandidateGrid();

            splitContainer.Panel1.Text = "Current validation";
            splitContainer.Panel1.Controls.Add(_healthGridControl);
            splitContainer.Panel2.Text = "Recovery candidates";
            splitContainer.Panel2.Controls.Add(_candidateGridControl);

            var buttonPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 44, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _browseRootButton = new SimpleButton { Text = "Browse...", Width = 90, Left = 0, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _refreshButton = new SimpleButton { Text = "Refresh", Width = 90, Left = 98, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _applyButton = new SimpleButton { Text = "Apply selected", Width = 120, Left = 196, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _cancelButton = new SimpleButton { Text = "Cancel", Width = 90, Left = 324, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _browseRootButton.Click += (sender, args) => BrowseRootRequested?.Invoke(this, EventArgs.Empty);
            _refreshButton.Click += (sender, args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
            _applyButton.Click += (sender, args) => ApplyRequested?.Invoke(this, EventArgs.Empty);
            _cancelButton.Click += (sender, args) => CancelRequested?.Invoke(this, EventArgs.Empty);
            buttonPanel.Controls.Add(_browseRootButton);
            buttonPanel.Controls.Add(_refreshButton);
            buttonPanel.Controls.Add(_applyButton);
            buttonPanel.Controls.Add(_cancelButton);

            Controls.Add(splitContainer);
            Controls.Add(buttonPanel);
            Controls.Add(_descriptionEdit);
            Controls.Add(_titleLabel);
        }

        public GameStorageCandidate SelectedCandidate => _candidateGridView.GetFocusedRow() as GameStorageCandidate;

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

        private void ConfigureHealthGrid()
        {
            _healthGridView.OptionsBehavior.Editable = false;
            _healthGridView.OptionsView.ShowGroupPanel = false;
            _healthGridView.OptionsView.ShowIndicator = false;
            _healthGridView.OptionsView.EnableAppearanceEvenRow = true;
            _healthGridView.OptionsView.EnableAppearanceOddRow = true;
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Role), Caption = "Folder", Visible = true, VisibleIndex = 0, Width = 110 });
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Path), Caption = "Path", Visible = true, VisibleIndex = 1, Width = 360 });
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Status), Caption = "Status", Visible = true, VisibleIndex = 2, Width = 130 });
            _healthGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Message), Caption = "Message", Visible = true, VisibleIndex = 3, Width = 360 });
        }

        private void ConfigureCandidateGrid()
        {
            _candidateGridView.OptionsBehavior.Editable = false;
            _candidateGridView.OptionsView.ShowGroupPanel = false;
            _candidateGridView.OptionsView.ShowIndicator = false;
            _candidateGridView.OptionsView.EnableAppearanceEvenRow = true;
            _candidateGridView.OptionsView.EnableAppearanceOddRow = true;
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.CandidateKind), Caption = "Source", Visible = true, VisibleIndex = 0, Width = 130 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.ConfidenceScore), Caption = "Score", Visible = true, VisibleIndex = 1, Width = 60 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.ConfidenceLevel), Caption = "Confidence", Visible = true, VisibleIndex = 2, Width = 90 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.CandidateRoot), Caption = "Root", Visible = true, VisibleIndex = 3, Width = 260 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.InstallInfoPath), Caption = "InstallInfo", Visible = true, VisibleIndex = 4, Width = 260 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.ModsPath), Caption = "Mods", Visible = true, VisibleIndex = 5, Width = 260 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.VirtualInstallPath), Caption = "VirtualInstall", Visible = true, VisibleIndex = 6, Width = 260 });
            _candidateGridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageCandidate.LinkFolderPath), Caption = "Link Folder", Visible = true, VisibleIndex = 7, Width = 260 });
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