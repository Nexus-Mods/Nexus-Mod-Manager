namespace Nexus.Client.PluginManagement.UI
{
    using DevExpress.Utils;
	using DevExpress.Utils.DragDrop;
	using DevExpress.XtraBars;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Controls;
    using DevExpress.XtraEditors.Repository;
    using DevExpress.XtraGrid;
    using DevExpress.XtraGrid.Columns;
    using DevExpress.XtraGrid.Views.Base;
    using DevExpress.XtraGrid.Views.Grid;
    using DevExpress.XtraGrid.Views.Grid.ViewInfo;
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
        private const string GridLayoutKey = "pluginManagerDXGrid";
        private const string SplitterSizeKey = "pluginManagerDX";
        private const int GridLayoutSaveDelayMs = 400;

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
		private readonly BarCheckItem _disablePluginSortingRestrictionsToggle;

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
		private bool _synchronizingPluginRestrictionsToggle;
		private bool _suppressManagedPluginsRefresh;
		private bool _managedPluginsRefreshPending;
		private bool _activePluginsRefreshPending;
		private bool _pluginRefreshScheduled;
		private PluginManagerVM _viewModel;
        private IPluginManager _pluginManager;
        private readonly Timer _gridLayoutSaveTimer;
        private bool _restoringGridLayout;
        private bool _splitterUserDragActive;
        private bool _restoringSplitter;
        private bool _splitterPositionRestored;

        public event EventHandler UpdatePluginsCount;
        public event EventHandler PluginMoved;

		private DevExpress.Utils.Behaviors.BehaviorManager behaviorManager;

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

			_disablePluginSortingRestrictionsToggle = new BarCheckItem(_barManager)
			{
				Caption = "Disable Plugin Sorting Restrictions",
				Hint = "Allow all non-critical, user-managed plugins to be freely reordered, enabled or disabled while retaining dependency warnings.",
				CheckBoxVisibility = CheckBoxVisibility.BeforeText
			};

			_disablePluginSortingRestrictionsToggle.CheckedChanged += PluginRestrictionsToggleCheckedChanged;

			_toolbar.AddItem(_moveUpButton);
			_toolbar.AddItem(_moveDownButton);
			_toolbar.AddItem(_restoreLoadOrderButton);

			_toolbar.AddItem(_disableAllButton).BeginGroup = true;
			_toolbar.AddItem(_enableAllButton);

			_toolbar.AddItem(_exportButton).BeginGroup = true;
			_toolbar.AddItem(_importButton);
			_toolbar.AddItem(_disablePluginSortingRestrictionsToggle).BeginGroup = true;

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
            _splitContainer.Panel1.MinSize = 280;
            _splitContainer.Panel2.MinSize = 220;
            _splitContainer.SizeChanged += SplitContainerSizeChanged;
            _splitContainer.BeginSplitterMoving += SplitContainerBeginSplitterMoving;
            _splitContainer.SplitterMoved += SplitContainerSplitterMoved;
            Shown += PluginManagerDXControlShown;

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

            _gridLayoutSaveTimer = new Timer
            {
                Interval = GridLayoutSaveDelayMs
            };
            _gridLayoutSaveTimer.Tick += GridLayoutSaveTimerTick;

			SetupGrid();
			SetupDragAndDrop();
			UpdateCommandState();
        }

		private void SetupDragAndDrop()
		{
			// Ensure the standard OLE drag-drop is disabled to avoid conflicts
			_gridControl.AllowDrop = false;
			_gridView.OptionsDragDrop.AllowDataReordering = true;

			// Instantiate the manager (pass components if available for proper disposal)
			this.behaviorManager = new DevExpress.Utils.Behaviors.BehaviorManager();

			this.behaviorManager.Attach<DragDropBehavior>(_gridView, behavior =>
			{
				behavior.Properties.AllowDrop = true;
				behavior.Properties.InsertIndicatorVisible = true; // Draws a clean line between rows
				behavior.Properties.PreviewVisible = true;         // Ghost image of dragged plugin
				
				behavior.DragOver += Behavior_DragOver;
				behavior.DragDrop += Behavior_DragDrop;
			});
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
                _splitterPositionRestored = false;

                if (_viewModel != null)
                {
                    HookViewModel();
                    RestoreGridLayout();
                    QueuePluginManagerSplitterRestore();
                    UpdateCommandState();
                }

				SynchronizePluginRestrictionsToggle();
			}
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IPluginManager PluginManager
        {
            get { return _pluginManager; }
            set
            {
				_pluginManager = value;
				SynchronizePluginRestrictionsToggle();
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
			_gridView.KeyDown += GridViewKeyDown;
			_gridView.CustomRowCellEdit += GridViewCustomRowCellEdit;
            _gridView.ShowingEditor += GridViewShowingEditor;            _gridView.CustomColumnDisplayText += GridViewCustomColumnDisplayText;
            _gridView.RowCellStyle += GridViewRowCellStyle;
            _gridControl.AllowDrop = false;
            //_gridControl.MouseDown += GridControlMouseDown;
            //_gridControl.MouseMove += GridControlMouseMove;
            //_gridControl.DragOver += GridControlDragOver;
            //_gridControl.DragDrop += GridControlDragDrop;
            _gridView.EndSorting += GridViewEndSorting;
            _gridView.ColumnWidthChanged +=
                (sender, args) => QueueGridLayoutSave();
            _gridView.ColumnPositionChanged +=
                (sender, args) => QueueGridLayoutSave();
            _gridView.ColumnFilterChanged +=
                (sender, args) => QueueGridLayoutSave();

            AddColumn(ColActive, "Active", 58, true).ColumnEdit = _activeCheckEdit;
            AddColumn(ColLoadOrder, "LO Index", 84, false).AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn(ColIndex, "Rel. Position", 58, false).AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
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
            QueueGridLayoutSave();
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

		/// <summary>
		/// Rebuilds all plugin grid rows using one snapshot, one active-plugin lookup and one owner lookup.
		/// </summary>
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
					List<Plugin> lstPlugins = _viewModel.ManagedPlugins.Where(x => x != null).ToList();
					PluginSnapshot psnSnapshot = _pluginManager == null ? null : _pluginManager.CurrentSnapshot;
					HashSet<Plugin> hstActivePlugins = new HashSet<Plugin>(_viewModel.ActivePlugins.Where(x => x != null), PluginComparer.Filename);
					IDictionary<Plugin, string> dicOwners = _viewModel.GetPluginOwners(lstPlugins);

					foreach (Plugin plgPlugin in lstPlugins)
						AddOrUpdateRow(plgPlugin, psnSnapshot, hstActivePlugins, dicOwners);
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

		/// <summary>
		/// Adds or updates a plugin grid row using precomputed snapshot, activation and ownership data.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin represented by the row.</param>
		/// <param name="p_psnSnapshot">The current plugin snapshot.</param>
		/// <param name="p_setActivePlugins">The active-plugin lookup.</param>
		/// <param name="p_dicOwners">The plugin-owner lookup.</param>
		private void AddOrUpdateRow(Plugin p_plgPlugin, PluginSnapshot p_psnSnapshot, ISet<Plugin> p_setActivePlugins, IDictionary<Plugin, string> p_dicOwners)
		{
			if (p_plgPlugin == null)
				return;

			PluginManagerDXRow row;

			if (!_rowsByPlugin.TryGetValue(p_plgPlugin, out row))
			{
				row = new PluginManagerDXRow(p_plgPlugin);
				_rowsByPlugin.Add(p_plgPlugin, row);
				_rows.Add(row);
			}

			PluginSnapshotEntry entry = p_psnSnapshot == null ? null : p_psnSnapshot.GetEntry(p_plgPlugin);
			string strOwner;

			row.Active = p_setActivePlugins != null && p_setActivePlugins.Contains(p_plgPlugin);
			row.LoadOrder = entry == null ? String.Empty : entry.ModIndex;
			row.Index = entry == null || !entry.AllocatedIndex.HasValue ? String.Empty : (entry.AllocatedIndex.Value + 1).ToString();
			row.PluginName = Path.GetFileName(p_plgPlugin.Filename);
			row.PluginType = entry == null ? p_plgPlugin.EffectiveTypeDisplay : entry.EffectiveType;
			row.Owner = p_dicOwners != null && p_dicOwners.TryGetValue(p_plgPlugin, out strOwner) ? strOwner : row.Owner;
			row.Status = GetStatus(p_plgPlugin, entry);
			row.NotifyAll();
		}

		#region Helpers

		/// <summary>
		/// Synchronizes the restriction toggle with the authoritative state exposed by the view model.
		/// </summary>
		private void SynchronizePluginRestrictionsToggle()
		{
			bool restrictionsDisabled = _viewModel != null && _viewModel.PluginRestrictionsDisabled;

			_disablePluginSortingRestrictionsToggle.Enabled = _viewModel != null && _pluginManager != null;

			if (_disablePluginSortingRestrictionsToggle.Checked == restrictionsDisabled)
				return;

			_synchronizingPluginRestrictionsToggle = true;

			try
			{
				_disablePluginSortingRestrictionsToggle.Checked = restrictionsDisabled;
			}
			finally
			{
				_synchronizingPluginRestrictionsToggle = false;
			}
		}

		/// <summary>
		/// Applies a plugin restriction mode change requested through the toolbar toggle.
		/// </summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The item click event arguments.</param>
		private void PluginRestrictionsToggleCheckedChanged(object sender, ItemClickEventArgs e)
		{
			if (_synchronizingPluginRestrictionsToggle)
				return;

			if (_viewModel == null || _pluginManager == null)
			{
				SynchronizePluginRestrictionsToggle();
				return;
			}

			bool requestedDisabled = _disablePluginSortingRestrictionsToggle.Checked;
			List<Plugin> previousOrder = new List<Plugin>(_viewModel.ManagedPlugins);
			PluginSnapshot validationSnapshot;

			if (!_viewModel.TrySetPluginRestrictionsDisabled(requestedDisabled, out validationSnapshot))
			{
				SynchronizePluginRestrictionsToggle();
				ShowPluginRestrictionTransitionBlockedMessage(validationSnapshot);
				RebuildRows();
				return;
			}

			SynchronizePluginRestrictionsToggle();
			RebuildRows();

			if (HasPluginOrderChanged(previousOrder, _viewModel.ManagedPlugins))
				PluginMoved?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Determines whether the plugin order changed between two snapshots.
		/// </summary>
		/// <param name="p_lstPreviousOrder">The order before the operation.</param>
		/// <param name="p_lstCurrentOrder">The order after the operation.</param>
		/// <returns><c>true</c> if the order changed; otherwise, <c>false</c>.</returns>
		private static bool HasPluginOrderChanged(IList<Plugin> p_lstPreviousOrder, IList<Plugin> p_lstCurrentOrder)
		{
			if (p_lstPreviousOrder == null || p_lstCurrentOrder == null || p_lstPreviousOrder.Count != p_lstCurrentOrder.Count)
				return true;

			for (int index = 0; index < p_lstPreviousOrder.Count; index++)
			{
				if (!PluginComparer.Filename.Equals(p_lstPreviousOrder[index], p_lstCurrentOrder[index]))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Shows the validation errors that prevented plugin sorting restrictions from being re-enabled.
		/// </summary>
		/// <param name="p_psnSnapshot">The strict validation snapshot that rejected the transition.</param>
		private void ShowPluginRestrictionTransitionBlockedMessage(PluginSnapshot p_psnSnapshot)
		{
			List<string> errors = p_psnSnapshot == null
				? new List<string>()
				: p_psnSnapshot.Diagnostics
					.Where(x => x.Severity == PluginValidationSeverity.Error)
					.Select(x =>
					{
						string pluginName = x.Plugin == null ? "Plugin state" : Path.GetFileName(x.Plugin.Filename);
						return pluginName + ": " + x.Message;
					})
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

			StringBuilder message = new StringBuilder();

			message.AppendLine("Plugin sorting restrictions cannot be re-enabled because the current plugin state is not valid under the normal restrictions.");
			message.AppendLine();
			message.AppendLine("Correct the following issues first:");

			foreach (string error in errors.Take(20))
				message.AppendLine("- " + error);

			if (errors.Count > 20)
			{
				message.AppendLine();
				message.AppendFormat("...and {0} additional issue(s).", errors.Count - 20);
				message.AppendLine();
			}

			if (errors.Count == 0)
			{
				message.AppendLine("- The plugin manager rejected the current state without returning a specific validation error.");
			}

			message.AppendLine();
			message.Append("The unrestricted mode remains enabled.");

			XtraMessageBox.Show(this, message.ToString(), "Plugin sorting restrictions", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

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

		/// <summary>
		/// Determines whether the active state of the specified plugin is protected.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to inspect.</param>
		/// <returns><c>true</c> if the plugin active state cannot be changed; otherwise, <c>false</c>.</returns>
		private bool IsPluginActivationLocked(Plugin p_plgPlugin)
		{
			return p_plgPlugin != null && _viewModel != null && !_viewModel.CanChangeActiveState(p_plgPlugin);
		}

		/// <summary>
		/// Determines whether the load-order position of the specified plugin is protected.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to inspect.</param>
		/// <returns><c>true</c> if the plugin cannot be reordered; otherwise, <c>false</c>.</returns>
		private bool IsPluginOrderLocked(Plugin p_plgPlugin)
		{
			return p_plgPlugin != null && _viewModel != null && !_viewModel.CanChangePluginOrder(p_plgPlugin);
		}

		/// <summary>
		/// Determines whether the specified plugin is protected from both activation and ordering changes.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to inspect.</param>
		/// <returns><c>true</c> if the plugin is fully protected; otherwise, <c>false</c>.</returns>
		private bool IsPluginFullyLocked(Plugin p_plgPlugin)
		{
			return IsPluginActivationLocked(p_plgPlugin) && IsPluginOrderLocked(p_plgPlugin);
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

        private bool CanApplyRequestedActiveState(Plugin plugin, bool requestedActive, bool showMessage)
        {
			if (plugin == null || _viewModel == null || IsPluginActivationLocked(plugin))
			{
                return false;
            }

            bool currentlyActive =
                _viewModel.ActivePlugins.Contains(plugin);

            if (currentlyActive == requestedActive)
                return true;

			if (_viewModel.PluginRestrictionsDisabled)
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

		/// <summary>
		/// Determines whether a plugin collection contains a plugin whose order is protected.
		/// </summary>
		/// <param name="p_enmPlugins">The plugins to inspect.</param>
		/// <returns><c>true</c> if an order-protected plugin is present; otherwise, <c>false</c>.</returns>
		private bool ContainsOrderLockedPlugin(IEnumerable<Plugin> p_enmPlugins)
		{
			return p_enmPlugins != null && p_enmPlugins.Any(IsPluginOrderLocked);
		}

		/// <summary>
		/// Determines whether a proposed order preserves every order-protected plugin position.
		/// </summary>
		/// <param name="p_lstCurrentOrder">The current plugin order.</param>
		/// <param name="p_lstProposedOrder">The proposed plugin order.</param>
		/// <returns><c>true</c> if protected positions are preserved; otherwise, <c>false</c>.</returns>
		private bool PreservesOrderLockedPluginPositions(IList<Plugin> p_lstCurrentOrder, IList<Plugin> p_lstProposedOrder)
		{
			for (int index = 0; index < p_lstCurrentOrder.Count; index++)
			{
				Plugin plugin = p_lstCurrentOrder[index];

				if (IsPluginOrderLocked(plugin) && p_lstProposedOrder.IndexOf(plugin) != index)
					return false;
			}

			return true;
		}

		private bool CanMoveSelectionAcrossOneRow(
            IList<Plugin> selectedPlugins,
            int direction)
        {
            if (selectedPlugins == null ||
                selectedPlugins.Count == 0 ||
                ContainsOrderLockedPlugin(selectedPlugins))
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
						IsPluginActivationLocked(currentOrder[index - 1]))
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
						IsPluginActivationLocked(currentOrder[index + 1]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

		/// <summary>
		/// Builds the status text displayed for the specified plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to describe.</param>
		/// <param name="p_pseEntry">The current snapshot entry.</param>
		/// <returns>The status text to display.</returns>
		private string GetStatus(Plugin p_plgPlugin, PluginSnapshotEntry p_pseEntry)
		{
			if (IsPluginFullyLocked(p_plgPlugin))
				return "Locked";

			if (p_pseEntry == null)
				return String.Empty;

			return String.Join(
				"; ",
				p_pseEntry.Diagnostics
					.Where(x => x.Severity == PluginValidationSeverity.Error || x.Severity == PluginValidationSeverity.Warning)
					.OrderByDescending(x => x.Severity)
					.Select(x => x.Message)
					.Where(x => !String.IsNullOrWhiteSpace(x))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray());
		}

		/// <summary>
		/// Gets the highest validation severity associated with a snapshot entry.
		/// </summary>
		/// <param name="p_pseEntry">The snapshot entry to inspect.</param>
		/// <returns>The highest severity, or <c>null</c> when no diagnostic is present.</returns>
		private static PluginValidationSeverity? GetHighestDiagnosticSeverity(PluginSnapshotEntry p_pseEntry)
		{
			if (p_pseEntry == null || p_pseEntry.Diagnostics.Count == 0)
				return null;

			if (p_pseEntry.Diagnostics.Any(x => x.Severity == PluginValidationSeverity.Error))
				return PluginValidationSeverity.Error;

			if (p_pseEntry.Diagnostics.Any(x => x.Severity == PluginValidationSeverity.Warning))
				return PluginValidationSeverity.Warning;

			return PluginValidationSeverity.Info;
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

		/// <summary>
		/// Refreshes plugin snapshot-dependent row values without rebuilding the grid data source.
		/// </summary>
		private void RefreshSnapshotRows()
		{
			if (_viewModel == null)
				return;

			List<Plugin> lstPlugins = _viewModel.ManagedPlugins.Where(x => x != null).ToList();
			PluginSnapshot psnSnapshot = _pluginManager == null ? null : _pluginManager.CurrentSnapshot;
			HashSet<Plugin> hstActivePlugins = new HashSet<Plugin>(_viewModel.ActivePlugins.Where(x => x != null), PluginComparer.Filename);

			foreach (Plugin plgPlugin in lstPlugins)
				AddOrUpdateRow(plgPlugin, psnSnapshot, hstActivePlugins, null);

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

            if (row != null && IsPluginActivationLocked(row.Plugin))
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

            if (row == null || IsPluginActivationLocked(row.Plugin))
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

            if (row == null || IsPluginActivationLocked(row.Plugin))
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

		private void GridViewKeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Space)
				return;

			if (_gridView.ActiveEditor != null)
				return;

			if (_gridView.IsFilterRow(_gridView.FocusedRowHandle))
				return;

			e.Handled = true;

			ToggleSelectedPluginsActiveState();
		}

		private void ToggleSelectedPluginsActiveState()
		{
			if (_viewModel == null)
				return;

			IList<Plugin> selected = GetSelectedPlugins();

			if (selected.Count == 0)
			{
				Plugin focused = GetFocusedPlugin();

				if (focused == null)
					return;

				selected = new List<Plugin> { focused };
			}

			List<Plugin> toActivate = new List<Plugin>();
			List<Plugin> toDeactivate = new List<Plugin>();

			foreach (Plugin plugin in selected)
			{
				if (IsPluginActivationLocked(plugin))
					continue;

				bool requestedActive = !_viewModel.ActivePlugins.Contains(plugin);

				// Silent check (no popup per plugin during a bulk toggle).
				if (!CanApplyRequestedActiveState(plugin, requestedActive, false))
					continue;

				if (requestedActive)
					toActivate.Add(plugin);
				else
					toDeactivate.Add(plugin);
			}

			if (toActivate.Count == 0 && toDeactivate.Count == 0)
				return;

			_viewModel.ManagePlugins(toActivate, toDeactivate);
		}

		private void Behavior_DragOver(object sender, DragOverEventArgs e)
		{
			DragOverGridEventArgs args = DragOverGridEventArgs.GetDragOverGridEventArgs(e);
			if (args == null) return;

			// DevExpress passes dragged items as an array of row handles
			int[] draggedHandles = e.GetData<int[]>();
			if (draggedHandles == null || draggedHandles.Length == 0) return;

			// Prevent dragging if the user grabs a locked plugin
			PluginManagerDXRow draggedRow = _gridView.GetRow(draggedHandles[0]) as PluginManagerDXRow;

			if (draggedRow == null || IsPluginActivationLocked(draggedRow.Plugin))
			{
				e.Action = DragDropActions.None;
				e.Cursor = System.Windows.Forms.Cursors.No;
				e.Handled = true; // We want to block DevExpress from processing a locked mod
				return;
			}

			// Allow the visual drag to continue
			e.Action = DragDropActions.Move;
			e.Cursor = System.Windows.Forms.Cursors.Default;

			// By NOT setting e.Handled here, DevExpress will continue its default background 
			// processing, which includes firing the edge auto-scroll timer.
		}

		private void Behavior_DragDrop(object sender, DragDropEventArgs e)
		{
			GridView targetView = e.Target as GridView;
			DragDropGridEventArgs args = DragDropGridEventArgs.GetDragDropGridEventArgs(e);

			if (targetView == null || args == null) return;

			int[] draggedHandles = e.GetData<int[]>();
			if (draggedHandles == null || draggedHandles.Length == 0) return;

			PluginManagerDXRow draggedRow = targetView.GetRow(draggedHandles[0]) as PluginManagerDXRow;

			// Legacy Early Exit Checks
			if (draggedRow == null || _pluginManager == null || IsPluginActivationLocked(draggedRow.Plugin))
			{
				RebuildRows();
				e.Handled = true;
				return;
			}

			int targetRowHandle = args.HitInfo.RowHandle >= 0
				? args.HitInfo.RowHandle
				: targetView.RowCount - 1;

			PluginManagerDXRow targetRow = targetView.GetRow(targetRowHandle) as PluginManagerDXRow;

			if (targetRow == null || targetRow == draggedRow)
			{
				e.Handled = true;
				return;
			}

			// NMM BACKEND LOGIC
			List<Plugin> currentOrder = new List<Plugin>(_viewModel.ManagedPlugins);
			List<Plugin> proposedOrder = new List<Plugin>(currentOrder);

			int sourceIndex = proposedOrder.IndexOf(draggedRow.Plugin);
			int targetIndex = proposedOrder.IndexOf(targetRow.Plugin);

			proposedOrder.RemoveAt(sourceIndex);

			// UI Adjustment: DevExpress draws an indicator line either BEFORE or AFTER a row.
			// We must adjust the target index based on where that visual line is drawn so the 
			// drop matches user expectation.
			if (args.InsertType == InsertType.After)
			{
				targetIndex++;
			}

			// Legacy adjustment: if we removed a plugin from *above* the drop target, 
			// the target index shifts up by 1.
			if (sourceIndex < targetIndex)
			{
				targetIndex--;
			}

			targetIndex = Math.Max(0, Math.Min(targetIndex, proposedOrder.Count));

			proposedOrder.Insert(targetIndex, draggedRow.Plugin);

			// Final safety check for locked mods
			if (!PreservesOrderLockedPluginPositions(currentOrder, proposedOrder))
			{
				RebuildRows();
				e.Handled = true;
				return;
			}

			// Suspend UI drawing while the backend processes the change
			targetView.BeginUpdate();
			try
			{
				ApplyPluginOrderChange(() => _pluginManager.SetPluginOrder(proposedOrder));
			}
			finally
			{
				targetView.EndUpdate();
			}

			// Tell DevExpress we completed the drop manually so it doesn't try to manipulate the UI
			e.Handled = true;
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

                if (row != null && !IsPluginActivationLocked(row.Plugin))
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
                if (row != null && !IsPluginActivationLocked(row.Plugin))
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
                !IsPluginActivationLocked(draggedRow.Plugin)
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
				IsPluginActivationLocked(draggedRow.Plugin))
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

            if (!PreservesOrderLockedPluginPositions(currentOrder, proposedOrder))
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
            QueueGridLayoutSave();
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

		/// <summary>
		/// Applies the visual status style to a plugin row.
		/// </summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The row cell style event arguments.</param>
		private void GridViewRowCellStyle(object sender, RowCellStyleEventArgs e)
		{
			PluginManagerDXRow row = _gridView.GetRow(e.RowHandle) as PluginManagerDXRow;

			if (row == null || String.IsNullOrEmpty(row.Status))
				return;

			if (row.Status == "Locked")
			{
				e.Appearance.ForeColor = SystemColors.GrayText;
				return;
			}

			if (row.StatusSeverity == PluginValidationSeverity.Error)
			{
				e.Appearance.ForeColor = Color.DarkRed;
				return;
			}

			if (row.StatusSeverity == PluginValidationSeverity.Warning)
				e.Appearance.ForeColor = Color.DarkOrange;
		}

		/// <summary>
		/// Appends the current validation diagnostics for a plugin to its detail panel.
		/// </summary>
		/// <param name="p_sbrDetails">The detail text builder.</param>
		/// <param name="p_plgPlugin">The plugin whose diagnostics should be appended.</param>
		private void AppendPluginDiagnostics(StringBuilder p_sbrDetails, Plugin p_plgPlugin)
		{
			if (p_sbrDetails == null || p_plgPlugin == null || _pluginManager == null)
				return;

			PluginSnapshotEntry entry = _pluginManager.CurrentSnapshot.GetEntry(p_plgPlugin);

			if (entry == null || entry.Diagnostics.Count == 0)
				return;

			AppendPluginDiagnosticSection(p_sbrDetails, entry.Diagnostics, PluginValidationSeverity.Error, "Errors");
			AppendPluginDiagnosticSection(p_sbrDetails, entry.Diagnostics, PluginValidationSeverity.Warning, "Warnings");
		}

		/// <summary>
		/// Appends diagnostics of a specific severity to the plugin detail text.
		/// </summary>
		/// <param name="p_sbrDetails">The detail text builder.</param>
		/// <param name="p_lstDiagnostics">The diagnostics to inspect.</param>
		/// <param name="p_pvsSeverity">The severity to append.</param>
		/// <param name="p_strHeading">The section heading.</param>
		private static void AppendPluginDiagnosticSection(StringBuilder p_sbrDetails, IList<PluginValidationDiagnostic> p_lstDiagnostics, PluginValidationSeverity p_pvsSeverity, string p_strHeading)
		{
			List<string> messages = p_lstDiagnostics
				.Where(x => x.Severity == p_pvsSeverity)
				.Select(x => x.Message)
				.Where(x => !String.IsNullOrWhiteSpace(x))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (messages.Count == 0)
				return;

			if (p_sbrDetails.Length > 0)
				p_sbrDetails.Append("<br/><br/>");

			p_sbrDetails.AppendFormat("<b>{0}:</b><br/>", HtmlEncode(p_strHeading));

			foreach (string message in messages)
				p_sbrDetails.AppendFormat("• {0}<br/>", HtmlEncode(message));
		}

		private void UpdatePluginInfo()
		{
			Plugin plugin = GetFocusedPlugin();

			if (plugin == null)
			{
				_pictureEdit.Image = null;
				_pictureEdit.Visible = false;
				_infoLabel.Text = string.Empty;
				return;
			}

			_pictureEdit.Image = plugin.Picture;
			_pictureEdit.Visible = plugin.Picture != null;

			string owner = _viewModel.GetPluginOwner(plugin);
			string description =
				_viewModel.GetPluginDescription(plugin.Filename);

			StringBuilder details = new StringBuilder();

			if (!string.IsNullOrWhiteSpace(owner))
			{
				details.AppendFormat(
					"<b>Mod:</b> {0}<br/><br/>",
					HtmlEncode(owner));
			}

			if (!string.IsNullOrWhiteSpace(description))
				details.Append(description);

			AppendPluginDiagnostics(details, plugin);

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
				_viewModel.CanMovePluginsUp(selected);

			bool canMoveDown =
                _viewModel != null &&
                selected.Count > 0 &&
                CanMoveSelectionAcrossOneRow(selected, 1) &&
                _viewModel.CanMovePluginsDown(selected);
            _moveUpButton.Enabled = canMoveUp;
            _moveDownButton.Enabled = canMoveDown;

            _restoreLoadOrderButton.Enabled = _gridView != null && _gridView.SortInfo.Count > 0;

			_disableAllButton.Enabled = _viewModel != null && _viewModel.ActivePlugins.Any(x => _viewModel.CanChangeActiveState(x));
			_enableAllButton.Enabled = _viewModel != null && _viewModel.ManagedPlugins.Any(x => !_viewModel.ActivePlugins.Contains(x) && _viewModel.CanChangeActiveState(x));
			_exportButton.Enabled = _viewModel != null && _viewModel.CanExecuteExportCommands();
            _importButton.Enabled = _viewModel != null && _viewModel.CanExecuteImportCommands();
			_disablePluginSortingRestrictionsToggle.Enabled = _viewModel != null && _pluginManager != null;
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

        private void GridLayoutSaveTimerTick(object sender, EventArgs e)
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

        private void PluginManagerDXControlShown(
            object sender,
            EventArgs e)
        {
            RestorePluginManagerSplitterPosition();
        }

        private void SplitContainerSizeChanged(
            object sender,
            EventArgs e)
        {
            RestorePluginManagerSplitterPosition();
        }

        private void QueuePluginManagerSplitterRestore()
        {
            if (_splitterPositionRestored ||
                !Visible ||
                !IsHandleCreated ||
                IsDisposed ||
                Disposing)
            {
                return;
            }

            BeginInvoke(
                (MethodInvoker)RestorePluginManagerSplitterPosition);
        }

        private void SplitContainerBeginSplitterMoving(
            object sender,
            BeginSplitMovingEventArgs e)
        {
            _splitterUserDragActive = true;
        }

        private void SplitContainerSplitterMoved(
            object sender,
            EventArgs e)
        {
            if (_restoringSplitter || !_splitterUserDragActive)
                return;

            _splitterUserDragActive = false;
            SavePluginManagerSplitterPosition();
        }

        private void RestorePluginManagerSplitterPosition()
        {
            if (_splitterPositionRestored ||
                !Visible ||
                _splitContainer.ClientSize.Width <= 0)
            {
                return;
            }

            int splitterPosition = GetSavedSplitterPosition();
            if (splitterPosition <= 0)
                return;

            int minimum = _splitContainer.Panel1.MinSize;
            int maximum =
                _splitContainer.ClientSize.Width -
                _splitContainer.Panel2.MinSize -
                _splitContainer.SplitterBounds.Width;

            if (maximum < minimum)
                return;

            int restoredPosition =
                Math.Max(minimum, Math.Min(splitterPosition, maximum));

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

            var splitterSizes =
                _viewModel.Settings.SplitterSizes[SplitterSizeKey];

            if (splitterSizes == null || splitterSizes.Count == 0)
                return 0;

            int splitterPosition;
            return Int32.TryParse(
                splitterSizes[0],
                out splitterPosition)
                ? splitterPosition
                : 0;
        }

        private void SavePluginManagerSplitterPosition()
        {
            if (_restoringSplitter ||
                _viewModel?.Settings?.SplitterSizes == null)
            {
                return;
            }

            _viewModel.Settings.SplitterSizes[SplitterSizeKey] =
                new List<Int32> { _splitContainer.SplitterPosition };
            _viewModel.Settings.Save();
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
            {
                _gridLayoutSaveTimer?.Stop();
                SaveGridLayout();

                if (_gridLayoutSaveTimer != null)
                {
                    _gridLayoutSaveTimer.Tick -=
                        GridLayoutSaveTimerTick;
                    _gridLayoutSaveTimer.Dispose();
                }

				_barManager?.Dispose();
            }

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
			public PluginValidationSeverity? StatusSeverity { get; set; }

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
