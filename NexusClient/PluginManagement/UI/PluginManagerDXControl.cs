namespace Nexus.Client.PluginManagement.UI
{
    using DevExpress.Utils;
	using DevExpress.XtraBars;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Controls;
    using DevExpress.XtraEditors.Repository;
    using DevExpress.XtraGrid;
    using DevExpress.XtraGrid.Columns;
    using DevExpress.XtraGrid.Views.Base;
    using DevExpress.XtraGrid.Views.Grid;
    using DevExpress.XtraGrid.Views.Grid.ViewInfo;
	using Nexus.Client.Games.Gamebryo.ModManagement.Scripting;
    using Nexus.Client.Plugins;
    using Nexus.Client.UI;
    using Nexus.Client.Util;
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Drawing;
    using System.IO;
    using System.Linq;
	using System.Text;
	using System.Windows.Forms;
    using WeifenLuo.WinFormsUI.Docking;

	/// <summary>
	/// DevExpress-based plugin manager surface. The legacy PluginManagerControl remains available as a compatibility fallback.
	/// </summary>
    public sealed class PluginManagerDXControl : ManagedFontDockContent
    {
        private const string ColActive = "Active";
        private const string ColLoadOrder = "LoadOrder";
        private const string ColIndex = "Index";
        private const string ColPlugin = "PluginName";
        private const string ColType = "PluginType";
        private const string ColOwner = "Owner";
        private const string ColStatus = "Status";

		private readonly BarManager _barManager;
		private readonly StandaloneBarDockControl _toolbarHost;
		private readonly Bar _toolbar;
		private readonly BarButtonItem _moveUpButton;
		private readonly BarButtonItem _moveDownButton;
		private readonly BarButtonItem _disableAllButton;
		private readonly BarButtonItem _enableAllButton;
		private readonly BarButtonItem _restoreLoadOrderButton;
		private readonly BarSubItem _exportButton;
		private readonly BarSubItem _importButton;

		private readonly GridControl _gridControl;
        private readonly GridView _gridView;
        private readonly PictureEdit _pictureEdit;
        private readonly LabelControl _infoLabel;
        private readonly SplitContainerControl _splitContainer;
        private readonly XtraScrollableControl _infoScroll;
        private readonly BindingList<PluginManagerDXRow> _rows = new BindingList<PluginManagerDXRow>();
        private readonly Dictionary<Plugin, PluginManagerDXRow> _rowsByPlugin = new Dictionary<Plugin, PluginManagerDXRow>();
        private readonly RepositoryItemCheckEdit _activeCheckEdit;
        private readonly RepositoryItemCheckEdit _lockedActiveCheckEdit;
        private Point _dragStartPoint = Point.Empty;
        private int _dragSourceRowHandle = GridControl.InvalidRowHandle;
        private bool _updatingActiveCell;
		private bool _suppressManagedPluginsRefresh;
		private bool _managedPluginsRefreshPending;
		private bool _activePluginsRefreshPending;
		private bool _pluginRefreshScheduled;
		private PluginManagerVM _viewModel;
        private IPluginManager _pluginManager;

        public event EventHandler UpdatePluginsCount;
        public event EventHandler PluginMoved;

        public PluginManagerDXControl()
        {
            Text = "Plugins";
            Name = "PluginManagerDXControl";
            DockAreas = DockAreas.Document;

			_barManager = new BarManager
			{
				Form = this
			};

			_toolbarHost = new StandaloneBarDockControl
			{
				Dock = DockStyle.Top,
				Height = 28,
				CausesValidation = false,
				Manager = _barManager
			};

			_toolbar = new Bar(_barManager, "Plugin Commands")
			{
				DockStyle = BarDockStyle.Standalone,
				StandaloneBarDockControl = _toolbarHost
			};

			_toolbar.OptionsBar.AllowQuickCustomization = false;
			_toolbar.OptionsBar.DrawDragBorder = false;
			_toolbar.OptionsBar.UseWholeRow = true;

			_moveUpButton = new BarButtonItem(_barManager, "Up");
			_moveUpButton.ItemClick +=
				(sender, args) => MoveSelectedUp(sender, args);

			_moveDownButton = new BarButtonItem(_barManager, "Down");
			_moveDownButton.ItemClick +=
				(sender, args) => MoveSelectedDown(sender, args);

			_restoreLoadOrderButton =
				new BarButtonItem(_barManager, "Load Order Sorting")
				{
					Hint =
						"Clear column sorting and restore the actual plugin load order."
				};

			_restoreLoadOrderButton.ItemClick +=
				(sender, args) => RestoreLoadOrderView(sender, args);

			_disableAllButton =
				new BarButtonItem(_barManager, "Disable All");

			_disableAllButton.ItemClick +=
				(sender, args) => DisableAll(sender, args);

			_enableAllButton =
				new BarButtonItem(_barManager, "Enable All");

			_enableAllButton.ItemClick +=
				(sender, args) => EnableAll(sender, args);

			_exportButton =
				new BarSubItem(_barManager, "Export");

			_importButton =
				new BarSubItem(_barManager, "Import");

			BarButtonItem exportToClipboardItem =
				new BarButtonItem(_barManager, "To Clipboard");

			exportToClipboardItem.ItemClick +=
				(sender, args) => ExportToClipboard(sender, args);

			BarButtonItem exportToFileItem =
				new BarButtonItem(_barManager, "To File...");

			exportToFileItem.ItemClick +=
				(sender, args) => ExportToFile(sender, args);

			BarButtonItem importFromClipboardItem =
				new BarButtonItem(_barManager, "From Clipboard");

			importFromClipboardItem.ItemClick +=
				(sender, args) => ImportFromClipboard(sender, args);

			BarButtonItem importFromFileItem =
				new BarButtonItem(_barManager, "From File...");

			importFromFileItem.ItemClick +=
				(sender, args) => ImportFromFile(sender, args);

			_exportButton.AddItem(exportToClipboardItem);
			_exportButton.AddItem(exportToFileItem);

			_importButton.AddItem(importFromClipboardItem);
			_importButton.AddItem(importFromFileItem);

			_toolbar.AddItem(_moveUpButton);
			_toolbar.AddItem(_moveDownButton);
			_toolbar.AddItem(_restoreLoadOrderButton);

			_toolbar.AddItem(_disableAllButton).BeginGroup = true;
			_toolbar.AddItem(_enableAllButton);

			_toolbar.AddItem(_exportButton).BeginGroup = true;
			_toolbar.AddItem(_importButton);

			_gridControl = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(_gridControl);
            _gridControl.MainView = _gridView;
            _gridControl.ViewCollection.Add(_gridView);
            _gridControl.DataSource = _rows;

            _activeCheckEdit = new RepositoryItemCheckEdit { AllowGrayed = false };
            _activeCheckEdit.EditValueChanging +=
                ActiveCheckEditEditValueChanging;

            _lockedActiveCheckEdit =
                new RepositoryItemCheckEdit
                {
                    AllowGrayed = false,
                    ReadOnly = true
                };            _gridControl.RepositoryItems.Add(_activeCheckEdit);
            _gridControl.RepositoryItems.Add(_lockedActiveCheckEdit);

            _pictureEdit = new PictureEdit
            {
                Dock = DockStyle.Top,
                Height = 150,
                Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom, ShowMenu = false, BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.NoBorder }
            };
			_pictureEdit.LookAndFeel.UseDefaultLookAndFeel = true;

			_infoLabel = new LabelControl
            {
                Dock = DockStyle.Top,
                AutoSizeMode = LabelAutoSizeMode.Vertical,
                Padding = new Padding(8),
                AllowHtmlString = true,
                UseMnemonic = false,
                Appearance =
                {
                    TextOptions =
                    {
                        VAlignment = VertAlignment.Top,
                        WordWrap = WordWrap.Wrap
                    }
                }
            };
			_infoLabel.Appearance.Options.UseTextOptions = true;
			_infoLabel.LookAndFeel.UseDefaultLookAndFeel = true;
			_infoLabel.Appearance.Options.UseBackColor = false;

			_infoScroll = new XtraScrollableControl
			{
				Dock = DockStyle.Fill
			};

			_infoScroll.LookAndFeel.UseDefaultLookAndFeel = true;

			_infoScroll.Controls.Add(_infoLabel);

			PanelControl infoPanel = new PanelControl
			{
				Dock = DockStyle.Fill,
				BorderStyle = BorderStyles.NoBorder
			};

			infoPanel.LookAndFeel.UseDefaultLookAndFeel = true;

			infoPanel.Controls.Add(_infoScroll);
			infoPanel.Controls.Add(_pictureEdit);

			_splitContainer = new SplitContainerControl
			{
				Dock = DockStyle.Fill,
				FixedPanel = SplitFixedPanel.None,
				SplitterPosition = 660,
				BorderStyle = BorderStyles.NoBorder
			};

			_splitContainer.LookAndFeel.UseDefaultLookAndFeel = true;

			_splitContainer.Panel1.Controls.Add(_gridControl);
            _splitContainer.Panel2.Controls.Add(infoPanel);

			PanelControl rootPanel = new PanelControl
			{
				Dock = DockStyle.Fill,
				BorderStyle = BorderStyles.NoBorder
			};

			rootPanel.LookAndFeel.UseDefaultLookAndFeel = true;
			rootPanel.Appearance.Options.UseBackColor = false;

			rootPanel.Controls.Add(_splitContainer);
			rootPanel.Controls.Add(_toolbarHost);

			Controls.Add(rootPanel);
			SetupGrid();
            UpdateCommandState();
        }

        internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
        {
            if (settings == null) return;

            DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
            DevExpressDisplaySettingsApplier.ApplyToBarManager(
                _barManager,
                settings);
            _gridControl.Invalidate();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public PluginManagerVM ViewModel
        {
            get { return _viewModel; }
            set
            {
                if (_viewModel != null)
                    UnhookViewModel();

                _viewModel = value;

                if (_viewModel != null)
                    HookViewModel();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IPluginManager PluginManager
        {
            get { return _pluginManager; }
            set
            {
                _pluginManager = value;
                RebuildRows();
            }
        }

        private void SetupGrid()
        {
            _gridView.OptionsBehavior.AllowAddRows = DefaultBoolean.False;
            _gridView.OptionsBehavior.AllowDeleteRows = DefaultBoolean.False;
            _gridView.OptionsSelection.MultiSelect = true;
            _gridView.OptionsView.ShowGroupPanel = false;
            _gridView.OptionsView.ShowAutoFilterRow = true;
            _gridView.OptionsView.ColumnAutoWidth = false;
            _gridView.OptionsNavigation.AutoFocusNewRow = false;
            _gridView.RowCellClick += GridViewRowCellClick;
            _gridView.FocusedRowChanged += GridViewFocusedRowChanged;
			_gridView.SelectionChanged += GridViewSelectionChanged;
			_gridView.CellValueChanging += GridViewCellValueChanging;
            _gridView.CustomRowCellEdit += GridViewCustomRowCellEdit;
            _gridView.ShowingEditor += GridViewShowingEditor;            _gridView.CustomColumnDisplayText += GridViewCustomColumnDisplayText;
            _gridView.RowCellStyle += GridViewRowCellStyle;
            _gridControl.AllowDrop = true;
            _gridControl.MouseDown += GridControlMouseDown;
            _gridControl.MouseMove += GridControlMouseMove;
            _gridControl.DragOver += GridControlDragOver;
            _gridControl.DragDrop += GridControlDragDrop;
            _gridView.EndSorting += GridViewEndSorting;

            AddColumn(ColActive, "Active", 58, true).ColumnEdit = _activeCheckEdit;
            AddColumn(ColLoadOrder, "Load Order", 84, false).AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn(ColIndex, "Index", 58, false).AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn(ColPlugin, "Plugin", 260, false);
            AddColumn(ColType, "Type", 110, false);
            AddColumn(ColOwner, "Owner", 220, false);
            AddColumn(ColStatus, "Status", 200, false);
        }

        private GridColumn AddColumn(string fieldName, string caption, int width, bool allowEdit)
        {
            GridColumn column = new GridColumn
            {
                FieldName = fieldName,
                Caption = caption,
                Width = width,
                Visible = true,
                OptionsColumn = { AllowEdit = allowEdit, ReadOnly = !allowEdit, AllowSort = DefaultBoolean.True }
            };
            _gridView.Columns.Add(column);
            return column;
        }

        private void RestoreLoadOrderView(object sender, EventArgs e)
        {
            _gridView.BeginUpdate();

            try
            {
                _gridView.ClearSorting();
            }
            finally
            {
                _gridView.EndUpdate();
            }

            _gridView.RefreshData();
            UpdateCommandState();
        }

        private void HookViewModel()
        {
            _viewModel.ManagedPlugins.CollectionChanged += ManagedPluginsCollectionChanged;
            _viewModel.ActivePlugins.CollectionChanged += ActivePluginsCollectionChanged;
            _viewModel.SortingPlugins += ViewModelTaskStarted;
            _viewModel.ManagingMultiplePlugins += ViewModelTaskStarted;
            _viewModel.ExportFailed += ViewModelExportFailed;
            _viewModel.ExportSucceeded += ViewModelExportSucceeded;
            _viewModel.ImportFailed += ViewModelImportFailed;
            _viewModel.ImportPartiallySucceeded += ViewModelImportSucceeded;
            _viewModel.ImportSucceeded += ViewModelImportSucceeded;
            RebuildRows();
        }

        private void UnhookViewModel()
        {
            _viewModel.ManagedPlugins.CollectionChanged -= ManagedPluginsCollectionChanged;
            _viewModel.ActivePlugins.CollectionChanged -= ActivePluginsCollectionChanged;
            _viewModel.SortingPlugins -= ViewModelTaskStarted;
            _viewModel.ManagingMultiplePlugins -= ViewModelTaskStarted;
            _viewModel.ExportFailed -= ViewModelExportFailed;
            _viewModel.ExportSucceeded -= ViewModelExportSucceeded;
            _viewModel.ImportFailed -= ViewModelImportFailed;
            _viewModel.ImportPartiallySucceeded -= ViewModelImportSucceeded;
            _viewModel.ImportSucceeded -= ViewModelImportSucceeded;

			_managedPluginsRefreshPending = false;
			_activePluginsRefreshPending = false;
			_pluginRefreshScheduled = false;
        }

		private void RebuildRows()
		{
			GridViewState state = CaptureGridViewState();

			_gridView.BeginDataUpdate();

			try
			{
				_rows.RaiseListChangedEvents = false;

				_rows.Clear();
				_rowsByPlugin.Clear();

				if (_viewModel != null)
				{
					PluginSnapshot snapshot =
						_pluginManager == null
							? null
							: _pluginManager.CurrentSnapshot;

					foreach (Plugin plugin in _viewModel.ManagedPlugins)
						AddOrUpdateRow(plugin, snapshot);
				}
			}
			finally
			{
				_rows.RaiseListChangedEvents = true;
				_rows.ResetBindings();
				_gridView.EndDataUpdate();
			}

			RestoreGridViewState(state);
			UpdatePluginInfo();
			UpdateCommandState();
		}

		private void AddOrUpdateRow(Plugin plugin, PluginSnapshot snapshot)
        {
            if (plugin == null)
                return;

            PluginManagerDXRow row;
            if (!_rowsByPlugin.TryGetValue(plugin, out row))
            {
                row = new PluginManagerDXRow(plugin);
                _rowsByPlugin.Add(plugin, row);
                _rows.Add(row);
            }

            PluginSnapshotEntry entry = snapshot == null ? null : snapshot.GetEntry(plugin);
            row.Active = _viewModel.ActivePlugins.Contains(plugin);
            row.LoadOrder = entry == null ? String.Empty : entry.ModIndex;
            row.Index = entry == null || !entry.AllocatedIndex.HasValue ? String.Empty : (entry.AllocatedIndex.Value + 1).ToString();
            row.PluginName = Path.GetFileName(plugin.Filename);
            row.PluginType = entry == null ? plugin.EffectiveTypeDisplay : entry.EffectiveType;
            row.Owner = _viewModel.GetPluginOwner(plugin);
            row.Status = GetStatus(plugin, entry);
            row.NotifyAll();
        }

		#region Helpers

		private void ApplyPluginOrderChange(Action changeAction)
		{
			if (changeAction == null)
				return;

			_suppressManagedPluginsRefresh = true;

			try
			{
				changeAction();
			}
			finally
			{
				_suppressManagedPluginsRefresh = false;

				// Perform exactly one UI rebuild after the backend finishes.
				RebuildRows();
			}

			PluginMoved?.Invoke(this, EventArgs.Empty);
			UpdatePluginsCount?.Invoke(this, EventArgs.Empty);
		}

		private static string HtmlEncode(string value)
		{
			if (String.IsNullOrEmpty(value))
				return String.Empty;

			return value
				.Replace("&", "&amp;")
				.Replace("<", "&lt;")
				.Replace(">", "&gt;")
				.Replace("\"", "&quot;");
		}

		private bool IsPluginLocked(Plugin plugin)
        {
            return plugin != null
                && _viewModel != null
                && !_viewModel.CanChangeActiveState(plugin);
        }

        private List<Plugin> GetActiveDependentPlugins(Plugin plugin)
        {
            if (plugin == null || _viewModel == null)
                return new List<Plugin>();

            string pluginName = Path.GetFileName(plugin.Filename);

            return _viewModel.ActivePlugins
                .Where(candidate =>
                    candidate != null &&
                    candidate.Masters != null &&
                    !String.Equals(
                        Path.GetFileName(candidate.Filename),
                        pluginName,
                        StringComparison.OrdinalIgnoreCase) &&
                    candidate.Masters.Any(master =>
                        String.Equals(
                            Path.GetFileName(master),
                            pluginName,
                            StringComparison.OrdinalIgnoreCase)))
                .OrderBy(
                    candidate =>
                        _viewModel.ManagedPlugins.IndexOf(candidate))
                .ToList();
        }

        private List<string> GetMissingMasters(Plugin plugin)
        {
            if (plugin == null ||
                plugin.Masters == null ||
                _viewModel == null)
            {
                return new List<string>();
            }

            return plugin.Masters
                .Where(master => !_viewModel.PluginExists(master))
                .Select(Path.GetFileName)
                .Where(master => !String.IsNullOrWhiteSpace(master))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<string> GetInactiveMasters(Plugin plugin)
        {
            if (plugin == null ||
                plugin.Masters == null ||
                _viewModel == null)
            {
                return new List<string>();
            }

            return plugin.Masters
                .Where(
                    master =>
                        _viewModel.PluginExists(master) &&
                        !_viewModel.PluginIsActive(master))
                .Select(Path.GetFileName)
                .Where(master => !String.IsNullOrWhiteSpace(master))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool CanApplyRequestedActiveState(
            Plugin plugin,
            bool requestedActive,
            bool showMessage)
        {
            if (plugin == null ||
                _viewModel == null ||
                IsPluginLocked(plugin))
            {
                return false;
            }

            bool currentlyActive =
                _viewModel.ActivePlugins.Contains(plugin);

            if (currentlyActive == requestedActive)
                return true;

            if (requestedActive)
            {
                List<string> missingMasters =
                    GetMissingMasters(plugin);

                List<string> inactiveMasters =
                    GetInactiveMasters(plugin);

                if (missingMasters.Count == 0 &&
                    inactiveMasters.Count == 0)
                {
                    return true;
                }

                if (showMessage)
                {
                    ShowActivationBlockedMessage(
                        plugin,
                        missingMasters,
                        inactiveMasters);
                }

                return false;
            }

            List<Plugin> activeDependents =
                GetActiveDependentPlugins(plugin);

            if (activeDependents.Count == 0)
                return true;

            if (showMessage)
            {
                ShowDeactivationBlockedMessage(
                    plugin,
                    activeDependents);
            }

            return false;
        }

        private void ShowActivationBlockedMessage(
            Plugin plugin,
            IList<string> missingMasters,
            IList<string> inactiveMasters)
        {
            StringBuilder message = new StringBuilder();

            message.AppendFormat(
                "The plugin \"{0}\" cannot be enabled because one or more required masters are missing or inactive.",
                Path.GetFileName(plugin.Filename));

            if (missingMasters != null &&
                missingMasters.Count > 0)
            {
                message.AppendLine();
                message.AppendLine();
                message.AppendLine("Missing masters:");

                foreach (string master in missingMasters)
                    message.AppendLine("- " + master);
            }

            if (inactiveMasters != null &&
                inactiveMasters.Count > 0)
            {
                message.AppendLine();
                message.AppendLine();
                message.AppendLine("Inactive masters:");

                foreach (string master in inactiveMasters)
                    message.AppendLine("- " + master);
            }

            message.AppendLine();
            message.AppendLine();
            message.Append(
                "Install or enable the required masters before enabling this plugin.");

            XtraMessageBox.Show(
                this,
                message.ToString(),
                "Plugin activation blocked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ShowDeactivationBlockedMessage(
            Plugin plugin,
            IList<Plugin> activeDependents)
        {
            StringBuilder message = new StringBuilder();

            message.AppendFormat(
                "The plugin \"{0}\" cannot be disabled because these active plugins depend on it:",
                Path.GetFileName(plugin.Filename));

            message.AppendLine();
            message.AppendLine();

            foreach (Plugin dependent in activeDependents)
            {
                message.AppendLine(
                    "- " + Path.GetFileName(dependent.Filename));
            }

            message.AppendLine();
            message.Append(
                "Disable the dependent plugins first.");

            XtraMessageBox.Show(
                this,
                message.ToString(),
                "Plugin deactivation blocked",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        private bool ContainsLockedPlugin(IEnumerable<Plugin> plugins)
        {
            return plugins != null && plugins.Any(IsPluginLocked);
        }

        private bool PreservesLockedPluginPositions(
            IList<Plugin> currentOrder,
            IList<Plugin> proposedOrder)
        {
            for (int index = 0; index < currentOrder.Count; index++)
            {
                Plugin plugin = currentOrder[index];

                if (IsPluginLocked(plugin) &&
                    proposedOrder.IndexOf(plugin) != index)
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanMoveSelectionAcrossOneRow(
            IList<Plugin> selectedPlugins,
            int direction)
        {
            if (selectedPlugins == null ||
                selectedPlugins.Count == 0 ||
                ContainsLockedPlugin(selectedPlugins))
            {
                return false;
            }

            List<Plugin> currentOrder =
                new List<Plugin>(_viewModel.ManagedPlugins);

            HashSet<Plugin> selection =
                new HashSet<Plugin>(selectedPlugins);

            if (direction < 0)
            {
                for (int index = 1; index < currentOrder.Count; index++)
                {
                    if (selection.Contains(currentOrder[index]) &&
                        !selection.Contains(currentOrder[index - 1]) &&
                        IsPluginLocked(currentOrder[index - 1]))
                    {
                        return false;
                    }
                }
            }
            else
            {
                for (int index = currentOrder.Count - 2; index >= 0; index--)
                {
                    if (selection.Contains(currentOrder[index]) &&
                        !selection.Contains(currentOrder[index + 1]) &&
                        IsPluginLocked(currentOrder[index + 1]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private string GetStatus(Plugin plugin, PluginSnapshotEntry entry)
        {
            if (IsPluginLocked(plugin))
                return "Locked";
            if (entry != null && entry.HasErrors)
                return String.Join("; ", entry.Diagnostics.Where(x => x.Severity == PluginValidationSeverity.Error).Select(x => x.Message).ToArray());
            if (plugin.Masters.Any(x => !_viewModel.PluginExists(x)))
                return "Missing master";
            if (plugin.Masters.Any(x => !_viewModel.PluginIsActive(x)))
                return "Inactive master";
            return String.Empty;
        }

        #endregion

        private Plugin GetFocusedPlugin()
        {
            PluginManagerDXRow row = _gridView.GetFocusedRow() as PluginManagerDXRow;
            return row == null ? null : row.Plugin;
        }

        private IList<Plugin> GetSelectedPlugins()
        {
            return _gridView.GetSelectedRows().Select(x => _gridView.GetRow(x) as PluginManagerDXRow).Where(x => x != null).Select(x => x.Plugin).ToList();
        }

        private void RefreshSnapshotRows()
        {
            PluginSnapshot snapshot = _pluginManager == null ? null : _pluginManager.CurrentSnapshot;
            foreach (Plugin plugin in _viewModel.ManagedPlugins)
                AddOrUpdateRow(plugin, snapshot);
            _gridView.RefreshData();
            UpdatePluginInfo();
            UpdateCommandState();
            UpdatePluginsCount?.Invoke(this, EventArgs.Empty);
        }

        private void RequestManagedPluginsRefresh()
        {
            if (IsDisposed || Disposing)
                return;

            _managedPluginsRefreshPending = true;
            SchedulePluginRefresh();
        }

        private void RequestActivePluginsRefresh()
        {
            if (IsDisposed || Disposing)
                return;

            _activePluginsRefreshPending = true;
            SchedulePluginRefresh();
        }

        private void SchedulePluginRefresh()
        {
            if (_pluginRefreshScheduled || IsDisposed || Disposing)
                return;

            if (!IsHandleCreated)
                return;

            _pluginRefreshScheduled = true;
            BeginInvoke((Action)FlushPluginRefresh);
        }

        private void FlushPluginRefresh()
        {
            bool rebuildRows = _managedPluginsRefreshPending;
            bool refreshRows = _activePluginsRefreshPending;

            _pluginRefreshScheduled = false;
            _managedPluginsRefreshPending = false;
            _activePluginsRefreshPending = false;

            if (IsDisposed || Disposing || _viewModel == null)
                return;

            if (rebuildRows)
            {
                RebuildRows();
                UpdatePluginsCount?.Invoke(this, EventArgs.Empty);
            }
            else if (refreshRows)
            {
                RefreshSnapshotRows();
            }
        }

		private void ManagedPluginsCollectionChanged(
			object sender,
			NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				BeginInvoke(
					(Action<object, NotifyCollectionChangedEventArgs>)
						ManagedPluginsCollectionChanged,
					sender,
					e);

				return;
			}

			if (_suppressManagedPluginsRefresh)
				return;

			RequestManagedPluginsRefresh();
		}

		private void ActivePluginsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action<object, NotifyCollectionChangedEventArgs>)ActivePluginsCollectionChanged, sender, e);
                return;
            }

            RequestActivePluginsRefresh();
        }

        private void GridViewCustomRowCellEdit(
            object sender,
            CustomRowCellEditEventArgs e)
        {
            if (e.Column == null ||
                e.Column.FieldName != ColActive)
            {
                return;
            }

            PluginManagerDXRow row =
                _gridView.GetRow(e.RowHandle)
                    as PluginManagerDXRow;

            if (row != null && IsPluginLocked(row.Plugin))
                e.RepositoryItem = _lockedActiveCheckEdit;
        }

        private void GridViewShowingEditor(
            object sender,
            CancelEventArgs e)
        {
            if (_gridView.FocusedColumn == null ||
                _gridView.FocusedColumn.FieldName != ColActive)
            {
                return;
            }

            PluginManagerDXRow row =
                _gridView.GetFocusedRow()
                    as PluginManagerDXRow;

            if (row == null || IsPluginLocked(row.Plugin))
                e.Cancel = true;
        }

        private void ActiveCheckEditEditValueChanging(
            object sender,
            ChangingEventArgs e)
        {
            if (_updatingActiveCell)
                return;

            PluginManagerDXRow row =
                _gridView.GetFocusedRow()
                    as PluginManagerDXRow;

            if (row == null || IsPluginLocked(row.Plugin))
            {
                e.Cancel = true;
                return;
            }

            bool requestedActive;

            try
            {
                requestedActive =
                    Convert.ToBoolean(e.NewValue);
            }
            catch
            {
                e.Cancel = true;
                return;
            }

            if (!CanApplyRequestedActiveState(
                    row.Plugin,
                    requestedActive,
                    true))
            {
                e.Cancel = true;
            }
        }
        private void GridViewCellValueChanging(object sender, CellValueChangedEventArgs e)
        {
            if (_updatingActiveCell || e.Column == null || e.Column.FieldName != ColActive)
                return;

            PluginManagerDXRow row = _gridView.GetRow(e.RowHandle) as PluginManagerDXRow;
            if (row == null || !_viewModel.CanChangeActiveState(row.Plugin))
                return;

            try
            {
                _updatingActiveCell = true;
                bool requestedActive = Convert.ToBoolean(e.Value);
                if (requestedActive)
                    _viewModel.ActivatePlugin(row.Plugin);
                else
                    _viewModel.DeactivatePlugin(row.Plugin);
            }
            finally
            {
                _updatingActiveCell = false;
                RequestActivePluginsRefresh();
            }
        }

        private void GridControlMouseDown(object sender, MouseEventArgs e)
        {
            GridHitInfo hit = _gridView.CalcHitInfo(e.Location);

            if (e.Button == MouseButtons.Left &&
                hit.InRow &&
                hit.RowHandle >= 0)
            {
                PluginManagerDXRow row =
                    _gridView.GetRow(hit.RowHandle) as PluginManagerDXRow;

                if (row != null && !IsPluginLocked(row.Plugin))
                {
                    _dragStartPoint = e.Location;
                    _dragSourceRowHandle = hit.RowHandle;
                    return;
                }
            }

            _dragStartPoint = Point.Empty;
            _dragSourceRowHandle = GridControl.InvalidRowHandle;
        }

        private void GridControlMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _dragSourceRowHandle == GridControl.InvalidRowHandle)
                return;

            Rectangle dragRectangle = new Rectangle(
                _dragStartPoint.X - SystemInformation.DragSize.Width / 2,
                _dragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                SystemInformation.DragSize.Width,
                SystemInformation.DragSize.Height);

            if (!dragRectangle.Contains(e.Location))
            {
                PluginManagerDXRow row = _gridView.GetRow(_dragSourceRowHandle) as PluginManagerDXRow;
                if (row != null && !IsPluginLocked(row.Plugin))
                    _gridControl.DoDragDrop(row, DragDropEffects.Move);
                _dragSourceRowHandle = GridControl.InvalidRowHandle;
            }
        }

        private void GridControlDragOver(object sender, DragEventArgs e)
        {
            PluginManagerDXRow draggedRow =
                e.Data.GetData(typeof(PluginManagerDXRow))
                    as PluginManagerDXRow;

            e.Effect =
                draggedRow != null &&
                !IsPluginLocked(draggedRow.Plugin)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
        }

        private void GridControlDragDrop(object sender, DragEventArgs e)
        {
            PluginManagerDXRow draggedRow =
                e.Data.GetData(typeof(PluginManagerDXRow))
                    as PluginManagerDXRow;

            if (draggedRow == null ||
                _pluginManager == null ||
                IsPluginLocked(draggedRow.Plugin))
            {
                RebuildRows();
                return;
            }

            Point clientPoint =
                _gridControl.PointToClient(new Point(e.X, e.Y));

            GridHitInfo hit = _gridView.CalcHitInfo(clientPoint);

            int targetRowHandle =
                hit.RowHandle >= 0
                    ? hit.RowHandle
                    : _gridView.RowCount - 1;

            PluginManagerDXRow targetRow =
                _gridView.GetRow(targetRowHandle)
                    as PluginManagerDXRow;

            if (targetRow == null || targetRow == draggedRow)
                return;

            List<Plugin> currentOrder =
                new List<Plugin>(_viewModel.ManagedPlugins);

            List<Plugin> proposedOrder =
                new List<Plugin>(currentOrder);

            int sourceIndex =
                proposedOrder.IndexOf(draggedRow.Plugin);

            int targetIndex =
                proposedOrder.IndexOf(targetRow.Plugin);

            proposedOrder.RemoveAt(sourceIndex);

            if (sourceIndex < targetIndex)
                targetIndex--;

            targetIndex =
                Math.Max(0, Math.Min(targetIndex, proposedOrder.Count));

            proposedOrder.Insert(targetIndex, draggedRow.Plugin);

            if (!PreservesLockedPluginPositions(currentOrder, proposedOrder))
            {
                RebuildRows();
                return;
            }

			ApplyPluginOrderChange(
				() => _pluginManager.SetPluginOrder(proposedOrder));
		}

        private void GridViewEndSorting(object sender, EventArgs e)
        {
            UpdateCommandState();
        }

        private void GridViewRowCellClick(
            object sender,
            RowCellClickEventArgs e)
        {
            if (e.Clicks != 2 ||
                e.Column == null ||
                e.Column.FieldName == ColActive)
            {
                return;
            }

            Plugin plugin = GetFocusedPlugin();

            if (plugin == null ||
                !_viewModel.CanChangeActiveState(plugin))
            {
                return;
            }

            bool requestedActive =
                !_viewModel.ActivePlugins.Contains(plugin);

            if (!CanApplyRequestedActiveState(
                    plugin,
                    requestedActive,
                    true))
            {
                return;
            }

            if (requestedActive)
                _viewModel.ActivatePlugin(plugin);
            else
                _viewModel.DeactivatePlugin(plugin);

            RequestActivePluginsRefresh();
        }
		private void GridViewFocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
		{
			UpdatePluginInfo();
		}

		private void GridViewCustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column != null && e.Column.FieldName == ColActive && e.Value is bool)
                e.DisplayText = (bool)e.Value ? "Active" : "Inactive";
        }

		private void GridViewSelectionChanged(object sender, DevExpress.Data.SelectionChangedEventArgs e)
		{
			if (IsDisposed || Disposing)
				return;

			BeginInvoke((Action)UpdateCommandState);
		}

		private void GridViewRowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            PluginManagerDXRow row = _gridView.GetRow(e.RowHandle) as PluginManagerDXRow;
            if (row == null || String.IsNullOrEmpty(row.Status))
                return;

            if (row.Status == "Locked")
                e.Appearance.ForeColor = SystemColors.GrayText;
            else
                e.Appearance.ForeColor = Color.DarkRed;
        }

		private void UpdatePluginInfo()
		{
			Plugin plugin = GetFocusedPlugin();

			if (plugin == null)
			{
				_pictureEdit.Image = null;
				_pictureEdit.Visible = false;
				_infoLabel.Text = String.Empty;
				return;
			}

			_pictureEdit.Image = plugin.Picture;
			_pictureEdit.Visible = plugin.Picture != null;

			string owner = _viewModel.GetPluginOwner(plugin);
			string description =
				_viewModel.GetPluginDescription(plugin.Filename);

			StringBuilder details = new StringBuilder();

			if (!String.IsNullOrWhiteSpace(owner))
			{
				details.AppendFormat(
					"<b>Mod:</b> {0}<br/><br/>",
					HtmlEncode(owner));
			}

			if (!String.IsNullOrWhiteSpace(description))
				details.Append(description);

            List<Plugin> activeDependents =
            GetActiveDependentPlugins(plugin);
            if (activeDependents.Count > 0)
            {
                if (details.Length > 0)
                    details.Append("<br/><br/>");
                details.Append(
                    "<b>Active plugins depending on this plugin:</b><br/>");
                foreach (Plugin dependent in activeDependents)
                {

					details.AppendFormat(
						"• {0}<br>",
						HtmlEncode(
							Path.GetFileName(
								dependent.Filename)));
				}
            }
			_infoLabel.Text = details.ToString();
		}

		private void UpdateCommandState()
        {
			IList<Plugin> selected = GetSelectedPlugins();
			Plugin focused = GetFocusedPlugin();

			if (selected.Count == 0 && focused != null)
				selected = new List<Plugin> { focused };

			bool canMoveUp =
                _viewModel != null &&
                selected.Count > 0 &&
                CanMoveSelectionAcrossOneRow(selected, -1) &&
                _viewModel.CanMovePluginUp(focused);

            bool canMoveDown =
                _viewModel != null &&
                selected.Count > 0 &&
                CanMoveSelectionAcrossOneRow(selected, 1) &&
                _viewModel.CanMovePluginsDown(selected);
            _moveUpButton.Enabled = canMoveUp;
            _moveDownButton.Enabled = canMoveDown;

            _restoreLoadOrderButton.Enabled = _gridView != null && _gridView.SortInfo.Count > 0;

            _disableAllButton.Enabled = _viewModel != null && _viewModel.ActivePlugins.Count > 0;
            _enableAllButton.Enabled = _viewModel != null && _viewModel.ManagedPlugins.Count > _viewModel.ActivePlugins.Count;
            _exportButton.Enabled = _viewModel != null && _viewModel.CanExecuteExportCommands();
            _importButton.Enabled = _viewModel != null && _viewModel.CanExecuteImportCommands();
        }

        private void MoveSelectedUp(object sender, EventArgs e)
        {
            IList<Plugin> selected = GetSelectedPlugins();

            if (!CanMoveSelectionAcrossOneRow(selected, -1))
            {
                RebuildRows();
                return;
            }

			ApplyPluginOrderChange(() => _viewModel.MoveUpCommand.Execute(selected));
		}

        private void MoveSelectedDown(object sender, EventArgs e)
        {
            IList<Plugin> selected = GetSelectedPlugins();

            if (!CanMoveSelectionAcrossOneRow(selected, 1))
            {
                RebuildRows();
                return;
            }

			ApplyPluginOrderChange(() => _viewModel.MoveDownCommand.Execute(selected));
		}

        private void DisableAll(object sender, EventArgs e)
        {
            if (XtraMessageBox.Show("Do you want to disable all the active plugins?", "Disable Plugins", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _viewModel.PluginsDisableAll();
        }

        private void EnableAll(object sender, EventArgs e)
        {
            if (XtraMessageBox.Show("Do you want to enable all the inactive plugins?", "Enable Plugins", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _viewModel.PluginsEnableAll();
        }

        private void ExportToClipboard(object sender, EventArgs e)
        {
            _viewModel.ExportLoadOrderToClipboardCommand.Execute();
        }

        private void ExportToFile(object sender, EventArgs e)
        {
            using (XtraSaveFileDialog dialog = new XtraSaveFileDialog())
            {
                dialog.FileName = _viewModel.GetDefaultExportFilename();
                dialog.Filter = _viewModel.GetExportFilterString();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _viewModel.ExportLoadOrderToFileCommand.Execute(dialog.FileName);
            }
        }

        private void ImportFromClipboard(object sender, EventArgs e)
        {
            _viewModel.ImportLoadOrderFromClipboardCommand.Execute();
        }

        private void ImportFromFile(object sender, EventArgs e)
        {
            using (XtraOpenFileDialog dialog = new XtraOpenFileDialog())
            {
                dialog.Filter = _viewModel.GetImportFilterString();
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _viewModel.ImportLoadOrderFromFileCommand.Execute(dialog.FileName);
            }
        }

        private void ViewModelTaskStarted(object sender, EventArgs<BackgroundTasks.IBackgroundTask> e)
        {
            BackgroundTasks.UI.ProgressDialog.ShowDialog(this, e.Argument);
            RequestManagedPluginsRefresh();
        }

        private void ViewModelExportFailed(object sender, EventArgs e)
        {
            MessageBox.Show(this, "The current load order could not be exported.", "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ViewModelExportSucceeded(object sender, EventArgs e)
        {
            MessageBox.Show(this, "The current load order was successfully exported.", "Export Succeeded", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ViewModelImportFailed(object sender, EventArgs e)
        {
            MessageBox.Show(this, "The selected load order could not be imported.", "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ViewModelImportSucceeded(object sender, EventArgs e)
        {
            RequestManagedPluginsRefresh();
        }

		private GridViewState CaptureGridViewState()
		{
			PluginManagerDXRow topRow =
				_gridView.GetRow(_gridView.TopRowIndex)
					as PluginManagerDXRow;

			return new GridViewState
			{
				FocusedPlugin = GetFocusedPlugin(),
				SelectedPlugins = GetSelectedPlugins().ToList(),
				TopPlugin = topRow == null ? null : topRow.Plugin,
				TopRowIndex = _gridView.TopRowIndex
			};
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_barManager?.Dispose();

			base.Dispose(disposing);
		}

		private int GetPluginRowHandle(Plugin plugin)
		{
			if (plugin == null)
				return GridControl.InvalidRowHandle;

			PluginManagerDXRow row;

			if (!_rowsByPlugin.TryGetValue(plugin, out row))
				return GridControl.InvalidRowHandle;

			int dataSourceIndex = _rows.IndexOf(row);

			return dataSourceIndex < 0
				? GridControl.InvalidRowHandle
				: _gridView.GetRowHandle(dataSourceIndex);
		}

		private void RestoreGridViewState(GridViewState state)
		{
			if (state == null)
				return;

			_gridView.ClearSelection();

			foreach (Plugin plugin in state.SelectedPlugins ?? new List<Plugin>())
			{
				int rowHandle = GetPluginRowHandle(plugin);

				if (rowHandle >= 0)
					_gridView.SelectRow(rowHandle);
			}

			int focusedRowHandle =
				GetPluginRowHandle(state.FocusedPlugin);

			if (focusedRowHandle >= 0)
				_gridView.FocusedRowHandle = focusedRowHandle;

			int topRowHandle =
				GetPluginRowHandle(state.TopPlugin);

			if (topRowHandle >= 0)
			{
				_gridView.TopRowIndex = topRowHandle;
			}
			else if (_gridView.RowCount > 0)
			{
				_gridView.TopRowIndex = Math.Max(
					0,
					Math.Min(state.TopRowIndex, _gridView.RowCount - 1));
			}
		}

		private sealed class PluginManagerDXRow : INotifyPropertyChanged
        {
            public PluginManagerDXRow(Plugin plugin)
            {
                Plugin = plugin;
            }

            public event PropertyChangedEventHandler PropertyChanged;
            public Plugin Plugin { get; private set; }
            public bool Active { get; set; }
            public string LoadOrder { get; set; }
            public string Index { get; set; }
            public string PluginName { get; set; }
            public string PluginType { get; set; }
            public string Owner { get; set; }
            public string Status { get; set; }

            public void NotifyAll()
            {
                OnPropertyChanged(ColActive);
                OnPropertyChanged(ColLoadOrder);
                OnPropertyChanged(ColIndex);
                OnPropertyChanged(ColPlugin);
                OnPropertyChanged(ColType);
                OnPropertyChanged(ColOwner);
                OnPropertyChanged(ColStatus);
            }

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

		private sealed class GridViewState
		{
			public Plugin FocusedPlugin { get; set; }
			public List<Plugin> SelectedPlugins { get; set; }
			public Plugin TopPlugin { get; set; }
			public int TopRowIndex { get; set; }
		}
	}
}
