using System.Collections.Generic;
using System.Drawing;
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
        private readonly GridControl _gridControl;
        private readonly GridView _gridView;
        private readonly SimpleButton _browseRootButton;
        private readonly SimpleButton _validateButton;
        private readonly SimpleButton _recoverButton;

        public GameStorageSetupControl()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);

            _titleLabel = new LabelControl
            {
                Text = "Game Storage",
                Dock = DockStyle.Top,
                Height = 28,
                Appearance = { Font = new Font("Segoe UI", 12f, FontStyle.Bold) }
            };

            _descriptionEdit = new MemoEdit
            {
                Dock = DockStyle.Top,
                Height = 76,
                ReadOnly = true,
                Text = "NMM needs persistent Game Storage folders for this managed game. These folders should survive NMM updates, reinstalling NMM, reinstalling Windows, and moving the NMM executable. Existing folders are validated and recovered without moving or deleting user data."
            };
            _descriptionEdit.Properties.BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder;

            _gridControl = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(_gridControl);
            _gridControl.MainView = _gridView;
            _gridControl.ViewCollection.Add(_gridView);
            ConfigureGrid();

            var buttonPanel = new PanelControl { Dock = DockStyle.Bottom, Height = 42, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder };
            _browseRootButton = new SimpleButton { Text = "Choose Game Storage Folder", Width = 180, Left = 0, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _validateButton = new SimpleButton { Text = "Validate", Width = 90, Left = 188, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _recoverButton = new SimpleButton { Text = "Recovery", Width = 90, Left = 286, Top = 8, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            buttonPanel.Controls.Add(_browseRootButton);
            buttonPanel.Controls.Add(_validateButton);
            buttonPanel.Controls.Add(_recoverButton);

            Controls.Add(_gridControl);
            Controls.Add(buttonPanel);
            Controls.Add(_descriptionEdit);
            Controls.Add(_titleLabel);
        }

        public void SetRows(IEnumerable<GameStorageSetupRow> rows)
        {
            _gridControl.DataSource = rows;
            _gridView.BestFitColumns();
        }

        private void ConfigureGrid()
        {
            _gridView.OptionsBehavior.Editable = false;
            _gridView.OptionsView.ShowGroupPanel = false;
            _gridView.OptionsView.ShowIndicator = false;
            _gridView.OptionsView.EnableAppearanceEvenRow = true;
            _gridView.OptionsView.EnableAppearanceOddRow = true;
            _gridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Role), Caption = "Folder", Visible = true, VisibleIndex = 0, Width = 110 });
            _gridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Path), Caption = "Path", Visible = true, VisibleIndex = 1, Width = 360 });
            _gridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Status), Caption = "Status", Visible = true, VisibleIndex = 2, Width = 110 });
            _gridView.Columns.Add(new GridColumn { FieldName = nameof(GameStorageSetupRow.Message), Caption = "Message", Visible = true, VisibleIndex = 3, Width = 320 });
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