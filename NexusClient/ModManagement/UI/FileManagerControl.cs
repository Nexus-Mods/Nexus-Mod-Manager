namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Drawing;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using System.Windows.Forms;

    using DevExpress.Utils;
    using DevExpress.XtraEditors;
    using DevExpress.XtraEditors.Repository;
    using DevExpress.XtraGrid;
    using DevExpress.XtraGrid.Columns;
    using DevExpress.XtraGrid.Views.Base;
    using DevExpress.XtraGrid.Views.Grid;
    using DevExpress.XtraGrid.Views.Grid.ViewInfo;

    using Nexus.Client.Settings;
    using Nexus.Client.UI;

    public sealed class FileManagerControl : ManagedFontDockContent
    {
        private const int GridSplitterContentPadding = 36;
        private const string FileManagerSplitterSizeKey = "fileManager";
        private const string GridLayoutKey = "fileManagerGrid";
        private const int GridLayoutSaveDelayMs = 400;
        private const int OwnerLookupMinimumPopupWidth = 480;
        private const int OwnerLookupMaximumPopupWidth = 900;

        private readonly Label _deploymentRootLabel;
        private readonly Button _refreshButton;
        private readonly SplitContainerControl _splitContainer;
        private readonly GridControl _gridControl;
        private readonly GridView _gridView;
        private readonly FilePreviewControl _previewControl;
        private readonly Label _summaryLabel;
        private readonly Label _statusLabel;
        private readonly RepositoryItemLookUpEdit _emptyOwnerLookup;
        private readonly RepositoryItemLookUpEdit _ownerLookup;
        private readonly RepositoryItemLookUpEdit _emptySourceLookup;
        private readonly RepositoryItemLookUpEdit _sourceLookup;
        private readonly RepositoryItemLookUpEdit _sourceFilterLookup;
        private readonly ContextMenuStrip _sourceContextMenu;
        private readonly ToolStripMenuItem _switchSourceMenuItem;
        private readonly Timer _previewSelectionTimer;
        private readonly Timer _gridLayoutSaveTimer;
        private readonly Dictionary<FileManagerRow, string> _previousOwnerKeys = new Dictionary<FileManagerRow, string>();
        private readonly Dictionary<FileManagerRow, FileManagerSource> _previousSources = new Dictionary<FileManagerRow, FileManagerSource>();
        private FileManagerVM _fileManagerVM;
        private ModManagerVM _viewModel;
        private bool _suppressOwnerChange;
        private bool _suppressSourceChange;
        private bool _suppressPreviewSelection;
        private bool _splitterUserDragActive;
        private bool _restoringSplitter;
        private bool _splitterPositionRestored;
        private bool _restoringGridLayout;
        private DevExpressDisplaySettings _displaySettings;

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
				Margin = new Padding(0)
			};
			_refreshButton.Click += RefreshButton_Click;

			topPanel.Controls.Add(descriptionLabel);
			topPanel.Controls.Add(_deploymentRootLabel);

			FlowLayoutPanel fileListToolbar = new FlowLayoutPanel
			{
				Dock = DockStyle.Top,
				Height = 37,
				FlowDirection = FlowDirection.RightToLeft,
				WrapContents = false,
				Padding = new Padding(0, 5, 8, 5)
			};

			fileListToolbar.Controls.Add(_refreshButton);

			_splitContainer = new SplitContainerControl
            {
                Dock = DockStyle.Fill,
                Horizontal = true,
                FixedPanel = SplitFixedPanel.None,
                SplitterPosition = 720
            };
            _splitContainer.Panel1.MinSize = 360;
            _splitContainer.Panel2.MinSize = 260;
            _splitContainer.SizeChanged += SplitContainer_SizeChanged;
            _splitContainer.BeginSplitterMoving += SplitContainer_BeginSplitterMoving;
            _splitContainer.SplitterMoved += SplitContainer_SplitterMoved;
            Shown += FileManagerControl_Shown;

            _gridControl = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(_gridControl);
            _gridControl.MainView = _gridView;
            _gridControl.ViewCollection.Add(_gridView);
            _gridView.OptionsView.ShowGroupPanel = false;
            _gridView.OptionsView.ShowIndicator = false;
            _gridView.OptionsDetail.EnableMasterViewMode = false;
            _gridView.OptionsBehavior.Editable = true;
            _gridView.OptionsView.ColumnAutoWidth = false;
            _gridView.OptionsView.BestFitMaxRowCount = 50;
            _gridView.OptionsView.ShowAutoFilterRow = true;
            _gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            _gridView.OptionsSelection.MultiSelect = true;
            _gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
            _gridView.CustomRowCellEdit += GridView_CustomRowCellEdit;
            _gridView.CustomRowCellEditForEditing += GridView_CustomRowCellEditForEditing;
            _gridView.CustomColumnDisplayText += GridView_CustomColumnDisplayText;
            _gridView.ShowingEditor += GridView_ShowingEditor;
            _gridView.CellValueChanging += GridView_CellValueChanging;
            _gridView.CellValueChanged += GridView_CellValueChanged;
            _gridView.RowCellStyle += GridView_RowCellStyle;
            _gridView.FocusedRowChanged += GridView_FocusedRowChanged;
			_gridControl.MouseUp += GridControl_MouseUp;

            _gridLayoutSaveTimer = new Timer
            {
                Interval = GridLayoutSaveDelayMs
            };
            _gridLayoutSaveTimer.Tick += GridLayoutSaveTimer_Tick;

            _gridView.ColumnWidthChanged +=
                (sender, args) => QueueGridLayoutSave();
            _gridView.ColumnPositionChanged +=
                (sender, args) => QueueGridLayoutSave();
            _gridView.ColumnFilterChanged +=
                (sender, args) => QueueGridLayoutSave();
            _gridView.EndSorting +=
                (sender, args) => QueueGridLayoutSave();

            ConfigureColumns();

            _emptyOwnerLookup = new RepositoryItemLookUpEdit { NullText = String.Empty, ShowHeader = false };
            _gridControl.RepositoryItems.Add(_emptyOwnerLookup);
            _ownerLookup = new RepositoryItemLookUpEdit
            {
                DisplayMember = "ModName",
                ValueMember = "OwnerKey",
                NullText = String.Empty,
                ShowHeader = false,
                TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor
            };
            _ownerLookup.Columns.Add(new DevExpress.XtraEditors.Controls.LookUpColumnInfo("ModName", "Mod"));
            _gridControl.RepositoryItems.Add(_ownerLookup);
            _emptySourceLookup = new RepositoryItemLookUpEdit { NullText = String.Empty, ShowHeader = false };
            _gridControl.RepositoryItems.Add(_emptySourceLookup);
            _sourceLookup = new RepositoryItemLookUpEdit
            {
                DataSource = FileManagerSourceDisplay.ManualSourceOptions,
                DisplayMember = "DisplayText",
                ValueMember = "Source",
                NullText = String.Empty,
                ShowHeader = false,
                TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor
            };
            _sourceLookup.Columns.Add(new DevExpress.XtraEditors.Controls.LookUpColumnInfo("DisplayText", "Source"));
            _gridControl.RepositoryItems.Add(_sourceLookup);

            _sourceFilterLookup = new RepositoryItemLookUpEdit
            {
                DataSource = FileManagerSourceDisplay.AllSourceOptions,
                DisplayMember = "DisplayText",
                ValueMember = "Source",
                NullText = String.Empty,
                ShowHeader = false,
                TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor
            };
            _sourceFilterLookup.Columns.Add(new DevExpress.XtraEditors.Controls.LookUpColumnInfo("DisplayText", "Source"));
            _gridControl.RepositoryItems.Add(_sourceFilterLookup);
            _gridView.Columns["Source"].ColumnEdit = _sourceFilterLookup;
            _sourceContextMenu = new ContextMenuStrip();
            _switchSourceMenuItem = new ToolStripMenuItem("Switch Source to");
            foreach (FileManagerSourceOption option in FileManagerSourceDisplay.ManualSourceOptions)
            {
                FileManagerSource source = option.Source;
                ToolStripMenuItem sourceItem = new ToolStripMenuItem(option.DisplayText);
                sourceItem.Tag = source;
                sourceItem.Click += SourceContextMenuItem_Click;
                _switchSourceMenuItem.DropDownItems.Add(sourceItem);
            }
            _sourceContextMenu.Items.Add(_switchSourceMenuItem);

			_previewControl = new FilePreviewControl
			{
				Dock = DockStyle.Fill
			};

            _previewSelectionTimer = new Timer
            {
                Interval = 200
            };
            _previewSelectionTimer.Tick += PreviewSelectionTimer_Tick;

			Panel fileListPanel = new Panel
			{
				Dock = DockStyle.Fill
			};

			fileListPanel.Controls.Add(_gridControl);
			fileListPanel.Controls.Add(fileListToolbar);

			_splitContainer.Panel1.Controls.Add(fileListPanel);
			_splitContainer.Panel2.Controls.Add(_previewControl);

			Panel bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 30, Padding = new Padding(10, 6, 10, 4) };
            _summaryLabel = new Label { AutoSize = true, Location = new Point(10, 7) };
            _statusLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(Width - 240, 7) };
            bottomPanel.Resize += (sender, args) => _statusLabel.Left = Math.Max(10, bottomPanel.ClientSize.Width - _statusLabel.Width - 10);
            bottomPanel.Controls.Add(_summaryLabel);
            bottomPanel.Controls.Add(_statusLabel);

            Controls.Add(_splitContainer);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);
        }

        internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
        {
            if (settings == null) return;

            _displaySettings = settings;
            DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
            DevExpressDisplaySettingsApplier.ApplyToRepositoryItem(_ownerLookup, settings);
            _gridControl.Invalidate();
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
                    _previewControl.SetSelectedRow(null);
                }

                _viewModel = value;
                _splitterPositionRestored = false;
                if (_viewModel != null)
                {
                    _fileManagerVM = new FileManagerVM(_viewModel);
                    _fileManagerVM.PropertyChanged += FileManagerVM_PropertyChanged;
                    BindRows(_fileManagerVM.Rows);
                    UpdateLabels();
                    RestoreGridLayout();
                }
            }
        }

        internal Task EnsureInitialLoadAsync()
        {
            QueueFileManagerSplitterRestore();
            return LoadIfNeededAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gridLayoutSaveTimer?.Stop();
                SaveGridLayout();

                if (_gridLayoutSaveTimer != null)
                {
                    _gridLayoutSaveTimer.Tick -= GridLayoutSaveTimer_Tick;
                    _gridLayoutSaveTimer.Dispose();
                }

                if (_fileManagerVM != null)
                {
                    _fileManagerVM.Dispose();
                    _fileManagerVM = null;
                }

                if (_sourceContextMenu != null)
                    _sourceContextMenu.Dispose();

                _previewSelectionTimer.Stop();
                _previewSelectionTimer.Tick -= PreviewSelectionTimer_Tick;
                _previewSelectionTimer.Dispose();
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
            else if (e.PropertyName == "Rows")
            {
                BindRows(_fileManagerVM.Rows);
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
            _summaryLabel.Text = String.Format("Total files: {0:N0}   |   Base Game: {1:N0}   |   Installed by NMM: {2:N0}   |   Creations: {3:N0}   |   External: {4:N0}   |   Untracked: {5:N0}",
                _fileManagerVM.TotalFiles,
                _fileManagerVM.BaseGameFiles,
                _fileManagerVM.InstalledByNmmFiles,
                _fileManagerVM.CreationsFiles,
                _fileManagerVM.ExternalModManagerFiles,
                _fileManagerVM.UntrackedFiles);
            if (_fileManagerVM.IsStale)
            {
                _statusLabel.Text = String.IsNullOrEmpty(_fileManagerVM.LastScannedDisplay)
                    ? "Refresh required"
                    : "Refresh required - last scanned: " + _fileManagerVM.LastScannedDisplay;
                _refreshButton.Text = "Refresh *";
                _refreshButton.UseVisualStyleBackColor = false;
                _refreshButton.BackColor = Color.Khaki;
            }
            else
            {
                _statusLabel.Text = String.IsNullOrEmpty(_fileManagerVM.LastScannedDisplay) ? _fileManagerVM.StatusMessage : "Last scanned: " + _fileManagerVM.LastScannedDisplay;
                _refreshButton.Text = "Refresh";
                _refreshButton.BackColor = SystemColors.Control;
                _refreshButton.UseVisualStyleBackColor = true;
            }

            if (_statusLabel.Parent != null)
                _statusLabel.Left = Math.Max(10, _statusLabel.Parent.ClientSize.Width - _statusLabel.Width - 10);
            _refreshButton.Enabled = !_fileManagerVM.IsScanning;
        }

        private void BindRows(BindingList<FileManagerRow> rows)
        {
            _ownerLookup.DataSource = null;
            _suppressPreviewSelection = true;
            _gridView.BeginUpdate();
            try
            {
                _gridControl.DataSource = rows;
            }
            finally
            {
                _gridView.EndUpdate();
                _suppressPreviewSelection = false;
            }

            UpdatePreviewFromFocusedRow();
            QueueFileManagerSplitterRestore();
        }

        private void QueueGridLayoutSave()
        {
            if (_restoringGridLayout ||
                _viewModel?.Settings == null ||
                _gridLayoutSaveTimer == null ||
                IsDisposed)
            {
                return;
            }

            _gridLayoutSaveTimer.Stop();
            _gridLayoutSaveTimer.Start();
        }

        private void GridLayoutSaveTimer_Tick(
            object sender,
            EventArgs e)
        {
            _gridLayoutSaveTimer.Stop();
            SaveGridLayout();
        }

        private void RestoreGridLayout()
        {
            if (_viewModel?.Settings == null)
                return;

            _restoringGridLayout = true;
            try
            {
                if (!_viewModel.Settings.DockPanelLayouts.ContainsKey(
                        GridLayoutKey))
                {
                    return;
                }

                string layout =
                    _viewModel.Settings.DockPanelLayouts[GridLayoutKey];

                if (String.IsNullOrWhiteSpace(layout))
                    return;

                byte[] bytes = Encoding.UTF8.GetBytes(layout);
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    _gridView.RestoreLayoutFromStream(stream);
                }
            }
            catch
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
            }
            finally
            {
                _restoringGridLayout = false;
            }
        }

        private void SaveGridLayout()
        {
            if (_restoringGridLayout || _viewModel?.Settings == null)
                return;

            _gridLayoutSaveTimer?.Stop();

            try
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    _gridView.SaveLayoutToStream(stream);
                    _viewModel.Settings.DockPanelLayouts[GridLayoutKey] =
                        Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
            }

            _viewModel.Settings.Save();
        }

        private void FileManagerControl_Shown(object sender, EventArgs e)
        {
            RestoreFileManagerSplitterPosition();
        }

        private void SplitContainer_SizeChanged(object sender, EventArgs e)
        {
            RestoreFileManagerSplitterPosition();
        }

        private void QueueFileManagerSplitterRestore()
        {
            if (_splitterPositionRestored || !Visible || !IsHandleCreated || IsDisposed || Disposing)
                return;

            BeginInvoke((MethodInvoker)RestoreFileManagerSplitterPosition);
        }

        private void SplitContainer_BeginSplitterMoving(object sender, BeginSplitMovingEventArgs e)
        {
            _splitterUserDragActive = true;
        }

        private void SplitContainer_SplitterMoved(object sender, EventArgs e)
        {
            if (_restoringSplitter || !_splitterUserDragActive)
                return;

            _splitterUserDragActive = false;
            SaveFileManagerSplitterPosition();
        }

        private void RestoreFileManagerSplitterPosition()
        {
            if (_splitterPositionRestored || !Visible || _splitContainer.ClientSize.Width <= 0)
                return;

            int splitterPosition = GetSavedSplitterPosition();
            if (splitterPosition <= 0)
            {
                splitterPosition = GetDefaultSplitterPosition();
                if (splitterPosition <= 0)
                    return;
            }

            int minimum = _splitContainer.Panel1.MinSize;
            int maximum = GetMaximumSplitterPosition();
            if (maximum < minimum)
                return;

            int restoredPosition = Math.Max(minimum, Math.Min(splitterPosition, maximum));
            _splitterPositionRestored = true;
            _restoringSplitter = true;
            try
            {
                _splitContainer.SplitterPosition = restoredPosition;
            }
            finally
            {
                _restoringSplitter = false;
            }
        }

        private int GetSavedSplitterPosition()
        {
            if (_viewModel?.Settings?.SplitterSizes == null)
                return 0;

            SettingsList splitterSizes = _viewModel.Settings.SplitterSizes[FileManagerSplitterSizeKey];
            if (splitterSizes == null || splitterSizes.Count == 0)
                return 0;

            int splitterPosition;
            return Int32.TryParse(splitterSizes[0], out splitterPosition) ? splitterPosition : 0;
        }

        private int GetDefaultSplitterPosition()
        {
            int maximum = GetMaximumSplitterPosition();
            if (maximum < _splitContainer.Panel1.MinSize)
                return 0;

            int gridContentWidth = GridSplitterContentPadding;
            foreach (GridColumn column in _gridView.Columns)
            {
                if (column.Visible)
                    gridContentWidth += column.Width;
            }

            return Math.Min(maximum, Math.Max(_splitContainer.Panel1.MinSize, gridContentWidth));
        }

        private int GetMaximumSplitterPosition()
        {
            return _splitContainer.ClientSize.Width - _splitContainer.Panel2.MinSize - _splitContainer.SplitterBounds.Width;
        }

        private void SaveFileManagerSplitterPosition()
        {
            if (_restoringSplitter || _viewModel?.Settings?.SplitterSizes == null)
                return;

            _viewModel.Settings.SplitterSizes[FileManagerSplitterSizeKey] = new List<Int32> { _splitContainer.SplitterPosition };
            _viewModel.Settings.Save();
        }

        private void ConfigureOwnerLookup(List<FileManagerOwnerCandidate> candidates)
        {
            int popupWidth = GetOwnerLookupPopupWidth(candidates);
            _ownerLookup.DataSource = candidates ?? FileManagerRow.EmptyOwnerCandidates;
            _ownerLookup.PopupWidth = popupWidth;
            if (_ownerLookup.Columns.Count > 0)
                _ownerLookup.Columns[0].Width = Math.Max(0, popupWidth - 24);
        }

        private int GetOwnerLookupPopupWidth(List<FileManagerOwnerCandidate> candidates)
        {
            int width = OwnerLookupMinimumPopupWidth;
            if (candidates != null)
            {
                foreach (FileManagerOwnerCandidate candidate in candidates)
                {
                    string modName = candidate == null ? String.Empty : candidate.ModName;
                    Font measurementFont = _displaySettings == null
                        ? Font
                        : _displaySettings.Font;
                    int measuredWidth = TextRenderer.MeasureText(
                        modName ?? string.Empty,
                        measurementFont).Width + 56;
                    width = Math.Max(width, measuredWidth);
                }
            }

            return Math.Min(OwnerLookupMaximumPopupWidth, width);
        }

        private void ConfigureColumns()
        {
            AddColumn("FileName", "File Name", 220, false);
            AddColumn("FileType", "File Type", 70, false);
            GridColumn size = AddColumn("RawSize", "Size", 90, false);
            size.DisplayFormat.FormatType = FormatType.Custom;
            size.DisplayFormat.Format = new FileSizeFormatter();
            size.AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn("RelativePath", "Relative Path", 260, false);
            GridColumn source = AddColumn("Source", "Source", 160, true);
            source.OptionsColumn.AllowEdit = true;
			GridColumn owners = AddColumn(
				"OwnerCount",
				"Owners",
				68,
				false);

			owners.AppearanceCell.TextOptions.HAlignment =
				HorzAlignment.Center;

			owners.AppearanceHeader.TextOptions.HAlignment =
				HorzAlignment.Center;

			GridColumn owner = AddColumn(
				"OwnerKey",
				"Owner",
				260,
				true);

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

        private void GridView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
        {
            if (_suppressPreviewSelection)
                return;

            _previewSelectionTimer.Stop();
            _previewSelectionTimer.Start();
        }

        private void PreviewSelectionTimer_Tick(object sender, EventArgs e)
        {
            _previewSelectionTimer.Stop();
            UpdatePreviewFromFocusedRow();
        }

        private void UpdatePreviewFromFocusedRow()
        {
            _previewSelectionTimer.Stop();
            if (_previewControl == null)
                return;
            FileManagerRow row = _gridView.GetFocusedRow() as FileManagerRow;
            _previewControl.SetSelectedRow(row);
        }

		private void GridView_CustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
		{
			if (e.Column == null)
				return;

			if (e.Column.FieldName == "Source" &&
				e.Value is FileManagerSource)
			{
				e.DisplayText =
					FileManagerSourceDisplay.GetDisplayText(
						(FileManagerSource)e.Value);

				return;
			}

            if (e.Column.FieldName == "OwnerKey")
            {
                FileManagerRow row = GetRowByListSourceIndex(e.ListSourceRowIndex);
                e.DisplayText = row == null ? String.Empty : row.OwnerName ?? String.Empty;
                return;
            }

			if (e.Column.FieldName == "OwnerCount" &&
				e.Value != null &&
				Convert.ToInt32(e.Value) == 0)
			{
				e.DisplayText = String.Empty;
			}
		}

        private FileManagerRow GetRowByListSourceIndex(int listSourceRowIndex)
        {
            if (_fileManagerVM == null ||
                _fileManagerVM.Rows == null ||
                listSourceRowIndex < 0 ||
                listSourceRowIndex >= _fileManagerVM.Rows.Count)
            {
                return null;
            }

            return _fileManagerVM.Rows[listSourceRowIndex];
        }

		private void GridView_CustomRowCellEdit(object sender, CustomRowCellEditEventArgs e)
        {
            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (e.Column.FieldName == "Source")
            {
                e.RepositoryItem = row != null && row.SourceEditable ? _sourceLookup : _emptySourceLookup;
                return;
            }

            if (e.Column.FieldName != "OwnerKey")
                return;

            e.RepositoryItem = _emptyOwnerLookup;
        }

        private void GridView_CustomRowCellEditForEditing(object sender, CustomRowCellEditEventArgs e)
        {
            if (e.Column == null || e.Column.FieldName != "OwnerKey")
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null || !row.OwnerEditable || _fileManagerVM == null || !_fileManagerVM.CanChangeFileOwner)
            {
                e.RepositoryItem = _emptyOwnerLookup;
                return;
            }

            ConfigureOwnerLookup(row.OwnerCandidates);
            e.RepositoryItem = _ownerLookup;
        }

        private void GridView_ShowingEditor(object sender, CancelEventArgs e)
        {
            if (_gridView.FocusedColumn == null)
                return;

            FileManagerRow row = _gridView.GetFocusedRow() as FileManagerRow;
            if (_gridView.FocusedColumn.FieldName == "Source")
            {
                if (row == null || !row.SourceEditable)
                    e.Cancel = true;
                return;
            }

            if (_gridView.FocusedColumn.FieldName != "OwnerKey")
                return;

            if (row == null || !row.OwnerEditable || _fileManagerVM == null || !_fileManagerVM.CanChangeFileOwner)
                e.Cancel = true;
        }

        private void GridView_CellValueChanging(object sender, CellValueChangedEventArgs e)
        {
            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null)
                return;

            if (e.Column.FieldName == "Source")
            {
                if (!_previousSources.ContainsKey(row))
                    _previousSources[row] = row.Source;
                return;
            }

            if (e.Column.FieldName != "OwnerKey")
                return;

            if (!_previousOwnerKeys.ContainsKey(row))
                _previousOwnerKeys[row] = row.OwnerKey;
        }

        private async void GridView_CellValueChanged(object sender, CellValueChangedEventArgs e)
        {
            if (e.Column.FieldName == "Source")
            {
                HandleSourceValueChanged(e);
                return;
            }

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

                Stopwatch updateWatch = Stopwatch.StartNew();
                _fileManagerVM.ApplySelectedOwner(row, selectedOwnerKey);
                updateWatch.Stop();
                Trace.TraceInformation("File Manager owner update completed in {0}ms.", updateWatch.ElapsedMilliseconds);
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

        private void HandleSourceValueChanged(CellValueChangedEventArgs e)
        {
            if (_suppressSourceChange)
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null)
                return;

            FileManagerSource selectedSource = e.Value is FileManagerSource ? (FileManagerSource)e.Value : row.Source;
            FileManagerSource previousSource;
            if (!_previousSources.TryGetValue(row, out previousSource))
                previousSource = row.Source;
            _previousSources.Remove(row);

            if (previousSource == selectedSource)
                return;

            try
            {
                Stopwatch updateWatch = Stopwatch.StartNew();
                _fileManagerVM.SetManualSource(row, selectedSource, previousSource);
                updateWatch.Stop();
                Trace.TraceInformation("File Manager source update completed in {0}ms.", updateWatch.ElapsedMilliseconds);
                _gridView.RefreshRow(e.RowHandle);
                UpdateLabels();
            }
            catch (Exception ex)
            {
                _suppressSourceChange = true;
                row.Source = previousSource;
                _gridView.RefreshRow(e.RowHandle);
                _suppressSourceChange = false;
                MessageBox.Show(this, ex.Message, "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void GridControl_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            GridHitInfo hitInfo = _gridView.CalcHitInfo(e.Location);
            if (hitInfo == null || !hitInfo.InRow)
                return;

            FileManagerRow clickedRow = _gridView.GetRow(hitInfo.RowHandle) as FileManagerRow;
            List<FileManagerRow> selectedRows = GetSelectedBulkSourceRows();
            if (!IsBulkSourceEligible(clickedRow) || !selectedRows.Contains(clickedRow) || selectedRows.Count <= 1)
                return;

            _sourceContextMenu.Show(_gridControl, e.Location);
        }

        private void SourceContextMenuItem_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem menuItem = sender as ToolStripMenuItem;
            if (menuItem == null || !(menuItem.Tag is FileManagerSource))
                return;

            ApplySourceToSelectedRows((FileManagerSource)menuItem.Tag);
        }

        private void ApplySourceToSelectedRows(FileManagerSource selectedSource)
        {
            List<FileManagerSourceChange> sourceChanges = GetSelectedSourceChanges(selectedSource);
            if (sourceChanges.Count == 0)
                return;

            List<FileManagerSourceChange> appliedChanges = new List<FileManagerSourceChange>();
            try
            {
                Stopwatch updateWatch = Stopwatch.StartNew();
                _suppressSourceChange = true;
                try
                {
                    foreach (FileManagerSourceChange sourceChange in sourceChanges)
                    {
                        _fileManagerVM.SetManualSource(sourceChange.Row, selectedSource, sourceChange.PreviousSource);
                        appliedChanges.Add(sourceChange);
                    }
                }
                finally
                {
                    _suppressSourceChange = false;
                }

                updateWatch.Stop();
                Trace.TraceInformation("File Manager source update completed for {0} selected file(s) in {1}ms.", appliedChanges.Count, updateWatch.ElapsedMilliseconds);
                RefreshSourceRows(appliedChanges);
                UpdateLabels();
            }
            catch (Exception ex)
            {
                RestoreSourceChanges(appliedChanges);
                RefreshSourceRows(sourceChanges);
                MessageBox.Show(this, ex.Message, "File Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private List<FileManagerSourceChange> GetSelectedSourceChanges(FileManagerSource selectedSource)
        {
            List<FileManagerSourceChange> sourceChanges = new List<FileManagerSourceChange>();
            List<FileManagerRow> selectedRows = GetSelectedBulkSourceRows();
            foreach (FileManagerRow selectedRow in selectedRows)
            {
                if (selectedRow.Source == selectedSource)
                    continue;

                sourceChanges.Add(new FileManagerSourceChange(selectedRow, selectedRow.Source));
            }

            return sourceChanges;
        }

        private List<FileManagerRow> GetSelectedBulkSourceRows()
        {
            List<FileManagerRow> selectedRows = new List<FileManagerRow>();
            int[] selectedRowHandles = _gridView.GetSelectedRows();
            if (selectedRowHandles == null)
                return selectedRows;

            foreach (int rowHandle in selectedRowHandles)
            {
                if (rowHandle < 0)
                    continue;

                FileManagerRow selectedRow = _gridView.GetRow(rowHandle) as FileManagerRow;
                if (!IsBulkSourceEligible(selectedRow) || selectedRows.Contains(selectedRow))
                    continue;

                selectedRows.Add(selectedRow);
            }

            return selectedRows;
        }

        private static bool IsBulkSourceEligible(FileManagerRow row)
        {
            return row != null && row.SourceEditable;
        }

        private void RestoreSourceChanges(List<FileManagerSourceChange> appliedChanges)
        {
            _suppressSourceChange = true;
            try
            {
                for (int i = appliedChanges.Count - 1; i >= 0; i--)
                {
                    FileManagerSourceChange sourceChange = appliedChanges[i];
                    try
                    {
                        _fileManagerVM.SetManualSource(sourceChange.Row, sourceChange.PreviousSource, sourceChange.Row.Source);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning("File Manager failed to restore manual source for {0}: {1}", sourceChange.Row == null ? String.Empty : sourceChange.Row.RelativePath, ex.Message);
                        if (sourceChange.Row != null)
                            sourceChange.Row.Source = sourceChange.PreviousSource;
                    }
                }
            }
            finally
            {
                _suppressSourceChange = false;
            }
        }

        private void RefreshSourceRows(IEnumerable<FileManagerSourceChange> sourceChanges)
        {
            foreach (FileManagerSourceChange sourceChange in sourceChanges)
                RefreshSourceRow(sourceChange.Row);
        }

        private void RefreshSourceRow(FileManagerRow row)
        {
            if (row == null || _fileManagerVM == null || _fileManagerVM.Rows == null)
                return;

            int rowIndex = _fileManagerVM.Rows.IndexOf(row);
            if (rowIndex < 0)
                return;

            int rowHandle = _gridView.GetRowHandle(rowIndex);
            if (rowHandle >= 0)
                _gridView.RefreshRow(rowHandle);
        }

        private void GridView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (e.Column.FieldName != "Source")
                return;

            FileManagerRow row = _gridView.GetRow(e.RowHandle) as FileManagerRow;
            if (row == null)
                return;

            if (row.Source == FileManagerSource.BaseGame)
                e.Appearance.ForeColor = Color.RoyalBlue;
            else if (row.Source == FileManagerSource.InstalledByNmm)
                e.Appearance.ForeColor = Color.ForestGreen;
            else if (row.Source == FileManagerSource.Creations)
                e.Appearance.ForeColor = Color.DarkViolet;
            else if (row.Source == FileManagerSource.ExternalModManager)
                e.Appearance.ForeColor = Color.DarkCyan;
            else
                e.Appearance.ForeColor = Color.OrangeRed;
        }

        private sealed class FileManagerSourceChange
        {
            public FileManagerSourceChange(FileManagerRow row, FileManagerSource previousSource)
            {
                Row = row;
                PreviousSource = previousSource;
            }

            public FileManagerRow Row { get; private set; }
            public FileManagerSource PreviousSource { get; private set; }
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
