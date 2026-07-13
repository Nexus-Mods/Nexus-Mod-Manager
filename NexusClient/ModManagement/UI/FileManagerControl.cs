namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    using DevExpress.Utils;
    using DevExpress.XtraEditors.Repository;
    using DevExpress.XtraGrid;
    using DevExpress.XtraGrid.Columns;
    using DevExpress.XtraGrid.Views.Base;
    using DevExpress.XtraGrid.Views.Grid;

    using Nexus.Client.UI;

    public sealed class FileManagerControl : ManagedFontDockContent
    {
        private readonly Label _deploymentRootLabel;
        private readonly Button _refreshButton;
        private readonly GridControl _gridControl;
        private readonly GridView _gridView;
        private readonly Label _summaryLabel;
        private readonly Label _statusLabel;
        private readonly RepositoryItemLookUpEdit _emptyOwnerLookup;
        private readonly Dictionary<FileManagerRow, string> _previousOwnerKeys = new Dictionary<FileManagerRow, string>();
        private FileManagerVM _fileManagerVM;
        private ModManagerVM _viewModel;
        private bool _suppressOwnerChange;

        public FileManagerControl()
        {
            Text = "File Manager";
            HideOnClose = true;

            Panel topPanel = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(10, 8, 10, 4) };
            Label descriptionLabel = new Label
            {
                AutoSize = true,
                Text = "Shows all files contained in the game's deployment directory.",
                Location = new Point(10, 8)
            };
            _deploymentRootLabel = new Label
            {
                AutoSize = true,
                Text = "Deployment root:",
                Location = new Point(10, 38)
            };
            _refreshButton = new Button
            {
                Text = "Refresh",
                Width = 92,
                Height = 27,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(Width - 112, 34)
            };
            _refreshButton.Click += RefreshButton_Click;
            topPanel.Resize += (sender, args) => _refreshButton.Left = topPanel.ClientSize.Width - _refreshButton.Width - 10;
            topPanel.Controls.Add(descriptionLabel);
            topPanel.Controls.Add(_deploymentRootLabel);
            topPanel.Controls.Add(_refreshButton);

            _gridControl = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(_gridControl);
            _gridControl.MainView = _gridView;
            _gridControl.ViewCollection.Add(_gridView);
            _gridView.OptionsView.ShowGroupPanel = false;
            _gridView.OptionsView.ShowIndicator = false;
            _gridView.OptionsBehavior.Editable = true;
            _gridView.CustomRowCellEdit += GridView_CustomRowCellEdit;
            _gridView.ShowingEditor += GridView_ShowingEditor;
            _gridView.CellValueChanging += GridView_CellValueChanging;
            _gridView.CellValueChanged += GridView_CellValueChanged;
            _gridView.RowCellStyle += GridView_RowCellStyle;
            ConfigureColumns();

            _emptyOwnerLookup = new RepositoryItemLookUpEdit { NullText = String.Empty, ShowHeader = false };
            _gridControl.RepositoryItems.Add(_emptyOwnerLookup);

            Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(10, 6, 10, 4) };
            _summaryLabel = new Label { AutoSize = true, Location = new Point(10, 7) };
            _statusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(Width - 240, 7) };
            bottomPanel.Resize += (sender, args) => _statusLabel.Left = Math.Max(10, bottomPanel.ClientSize.Width - _statusLabel.Width - 10);
            bottomPanel.Controls.Add(_summaryLabel);
            bottomPanel.Controls.Add(_statusLabel);

            Controls.Add(_gridControl);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModManagerVM ViewModel
        {
            get { return _viewModel; }
            set
            {
                if (_fileManagerVM != null)
                {
                    _fileManagerVM.PropertyChanged -= FileManagerVM_PropertyChanged;
                    _fileManagerVM.Dispose();
                    _fileManagerVM = null;
                }

                _viewModel = value;
                if (_viewModel != null)
                {
                    _fileManagerVM = new FileManagerVM(_viewModel);
                    _fileManagerVM.PropertyChanged += FileManagerVM_PropertyChanged;
                    _gridControl.DataSource = _fileManagerVM.Rows;
                    UpdateLabels();
                }
            }
        }

        protected override async void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
                await LoadIfNeededAsync().ConfigureAwait(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _fileManagerVM != null)
            {
                _fileManagerVM.Dispose();
                _fileManagerVM = null;
            }

            base.Dispose(disposing);
        }

        private async Task LoadIfNeededAsync()
        {
            if (_fileManagerVM == null)
                return;

            try
            {
                await _fileManagerVM.LoadIfNeededAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            if (_fileManagerVM == null)
                return;

            try
            {
                await _fileManagerVM.RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void FileManagerVM_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)(() => FileManagerVM_PropertyChanged(sender, e)));
                return;
            }

            if (e.PropertyName == "IsScanning")
            {
                if (_fileManagerVM.IsScanning)
                    _gridView.ShowLoadingPanel();
                else
                    _gridView.HideLoadingPanel();
            }
            else if (e.PropertyName == "CanChangeFileOwner")
            {
                _gridView.RefreshData();
            }

            UpdateLabels();
        }

        private void UpdateLabels()
        {
            if (_fileManagerVM == null)
                return;

            _deploymentRootLabel.Text = "Deployment root: " + (_fileManagerVM.DeploymentRoot ?? FileManagerQueryService.GetDeploymentRoot(_fileManagerVM.GameMode));
            _summaryLabel.Text = String.Format("Total files: {0:N0}   |   Base game files: {1:N0}   |   Installed by NMM: {2:N0}   |   Untracked: {3:N0}",
                _fileManagerVM.TotalFiles,
                _fileManagerVM.BaseGameFiles,
                _fileManagerVM.InstalledByNmmFiles,
                _fileManagerVM.UntrackedFiles);
            _statusLabel.Text = String.IsNullOrEmpty(_fileManagerVM.LastScannedDisplay) ? _fileManagerVM.StatusMessage : "Last scanned: " + _fileManagerVM.LastScannedDisplay;
            if (_statusLabel.Parent != null)
                _statusLabel.Left = Math.Max(10, _statusLabel.Parent.ClientSize.Width - _statusLabel.Width - 10);
            _refreshButton.Enabled = !_fileManagerVM.IsScanning;
        }

        private void ConfigureColumns()
        {
            AddColumn("FileName", "File Name", 220, false);
            GridColumn size = AddColumn("RawSize", "Size", 90, false);
            size.DisplayFormat.FormatType = FormatType.Custom;
            size.DisplayFormat.Format = new FileSizeFormatter();
            size.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn("RelativePath", "Relative Path", 260, false);
            AddColumn("SourceDisplay", "Source", 140, false);
            GridColumn owner = AddColumn("OwnerKey", "Owner", 260, true);
            owner.OptionsColumn.AllowEdit = true;
        }

        private GridColumn AddColumn(string fieldName, string caption, int width, bool allowEdit)
        {
            GridColumn column = new GridColumn
            {
                FieldName = fieldName,
                Caption = caption,
                Width = width,
                Visible = true,
                VisibleIndex = _gridView.Columns.Count
            };
            column.OptionsColumn.AllowSort = DefaultBoolean.True;
            column.OptionsColumn.AllowEdit = allowEdit;
            _gridView.Columns.Add(column);
            return column;
        }

        private void GridView_CustomRowCellEdit(object sender, CustomRowCellEditEventArgs e)
        {
            if (e.Column.FieldName != "OwnerKey")
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null || !row.OwnerEditable || _fileManagerVM == null || !_fileManagerVM.CanChangeFileOwner)
            {
                e.RepositoryItem = _emptyOwnerLookup;
                return;
            }

            RepositoryItemLookUpEdit ownerLookup = new RepositoryItemLookUpEdit
            {
                DataSource = row.OwnerCandidates,
                DisplayMember = "ModName",
                ValueMember = "OwnerKey",
                NullText = String.Empty,
                ShowHeader = false,
                TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor
            };
            ownerLookup.Columns.Add(new DevExpress.XtraEditors.Controls.LookUpColumnInfo("ModName", "Mod"));
            _gridControl.RepositoryItems.Add(ownerLookup);
            e.RepositoryItem = ownerLookup;
        }

        private void GridView_ShowingEditor(object sender, CancelEventArgs e)
        {
            if (_gridView.FocusedColumn == null || _gridView.FocusedColumn.FieldName != "OwnerKey")
                return;

            FileManagerRow row = _gridView.GetFocusedRow() as FileManagerRow;
            if (row == null || !row.OwnerEditable || _fileManagerVM == null || !_fileManagerVM.CanChangeFileOwner)
                e.Cancel = true;
        }

        private void GridView_CellValueChanging(object sender, CellValueChangedEventArgs e)
        {
            if (e.Column.FieldName != "OwnerKey")
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row != null && !_previousOwnerKeys.ContainsKey(row))
                _previousOwnerKeys[row] = row.OwnerKey;
        }

        private async void GridView_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (_suppressOwnerChange || e.Column.FieldName != "OwnerKey")
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null)
                return;

            string selectedOwnerKey = e.Value as string;
            string previousOwnerKey;
            if (!_previousOwnerKeys.TryGetValue(row, out previousOwnerKey))
                previousOwnerKey = row.OwnerKey;
            _previousOwnerKeys.Remove(row);

            if (String.Equals(previousOwnerKey, selectedOwnerKey, StringComparison.OrdinalIgnoreCase))
                return;

            if (_fileManagerVM == null || !_fileManagerVM.CanChangeFileOwner)
            {
                _suppressOwnerChange = true;
                row.OwnerKey = previousOwnerKey;
                _gridView.RefreshRow(e.RowHandle);
                _suppressOwnerChange = false;
                return;
            }

            try
            {
                _refreshButton.Enabled = false;
                VirtualFileOwnerSwitchResult result = await _fileManagerVM.SwitchOwnerAsync(row, selectedOwnerKey).ConfigureAwait(true);
                if (!result.Success)
                    throw result.Failure ?? new InvalidOperationException(result.FailureMessage ?? "Unable to switch file owner.");

                _fileManagerVM.RefreshRowOwnership(row);
                _gridView.RefreshRow(e.RowHandle);
            }
            catch (Exception ex)
            {
                _suppressOwnerChange = true;
                row.OwnerKey = previousOwnerKey;
                _gridView.RefreshRow(e.RowHandle);
                _suppressOwnerChange = false;
                MessageBox.Show(this, ex.Message, "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _refreshButton.Enabled = _fileManagerVM == null || !_fileManagerVM.IsScanning;
            }
        }

        private void GridView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.Column.FieldName != "SourceDisplay")
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null)
                return;

            if (row.Source == FileManagerSource.BaseGameFile)
                e.Appearance.ForeColor = Color.RoyalBlue;
            else if (row.Source == FileManagerSource.InstalledByNmm)
                e.Appearance.ForeColor = Color.ForestGreen;
            else
                e.Appearance.ForeColor = Color.OrangeRed;
        }

        private sealed class FileSizeFormatter : IFormatProvider, ICustomFormatter
        {
            public object GetFormat(Type formatType)
            {
                return formatType == typeof(ICustomFormatter) ? this : null;
            }

            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                if (arg == null)
                    return String.Empty;

                long bytes = Convert.ToInt64(arg);
                if (bytes < 1024 * 1024)
                    return String.Format("{0:0.00} KB", bytes / 1024.0);

                return String.Format("{0:0.00} MB", bytes / 1024.0 / 1024.0);
            }
        }
    }
}