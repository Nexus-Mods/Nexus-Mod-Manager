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
    using Nexus.Client.Util.Localization;
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
        private const string GridColumnWidthsKey = GridLayoutKey + ".ColumnWidths";
        private const string HideFePluginIndexesSettingsKey = GridLayoutKey + ".HideFePluginIndexes";
        private const string SplitterSizeKey = "pluginManagerDX";
        private const int GridLayoutSaveDelayMs = 400;
        private const int DragAutoScrollIntervalMs = 75;
        private const int DragAutoScrollEdgeThreshold = 32;
        private const int DragAutoScrollHorizontalTolerance = 48;
        private const int DragAutoScrollAccelerationPixels = 48;
        private const int DragAutoScrollMaximumStep = 4;

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
		private readonly BarCheckItem _hideFePluginIndexesToggle;
		private readonly BarCheckItem _disablePluginSortingRestrictionsToggle;

		private readonly GridControl _gridControl;
        private readonly GridView _gridView;
        private readonly PictureEdit _pictureEdit;
        private readonly LabelControl _infoLabel;
        private readonly SplitContainerControl _splitContainer;
        private readonly PanelControl _infoScroll;
        private readonly BindingList<PluginManagerDXRow> _rows = new BindingList<PluginManagerDXRow>();
        private readonly Dictionary<Plugin, PluginManagerDXRow> _rowsByPlugin = new Dictionary<Plugin, PluginManagerDXRow>(PluginComparer.Filename);
        private readonly Dictionary<string, Tuple<DateTime, long, string>> _pluginDescriptionCache = new Dictionary<string, Tuple<DateTime, long, string>>(StringComparer.OrdinalIgnoreCase);
        private PluginSnapshot _pluginDescriptionSnapshot;
        private readonly RepositoryItemCheckEdit _activeCheckEdit;
        private readonly RepositoryItemCheckEdit _lockedActiveCheckEdit;
        private Point _dragStartPoint = Point.Empty;
        private int _dragSourceRowHandle = GridControl.InvalidRowHandle;
        private bool _updatingActiveCell;
		private bool _hideFePluginIndexes;
		private bool _synchronizingHideFePluginIndexesToggle;
		private bool _synchronizingPluginRestrictionsToggle;
		private bool _suppressManagedPluginsRefresh;
		private bool _managedPluginsRefreshPending;
		private bool _activePluginsRefreshPending;
		private bool _pluginRefreshScheduled;
		private bool _commandStateUpdateScheduled;
		private PluginManagerVM _viewModel;
        private IPluginManager _pluginManager;
        private readonly Timer _gridLayoutSaveTimer;
        private readonly Timer _dragAutoScrollTimer;
        private bool _restoringGridLayout;
        private bool _splitterUserDragActive;
        private bool _restoringSplitter;
        private bool _splitterPositionRestored;
        private Color _lockedPluginForeColor = SystemColors.GrayText;
        private Color _errorPluginForeColor = Color.Red;
        private Color _warningPluginForeColor = Color.DarkOrange;
        private readonly string _activeDisplayText;
        private readonly string _inactiveDisplayText;
        private readonly string _lockedDisplayText;
        private readonly string _diagnosticErrorsHeading;
        private readonly string _diagnosticWarningsHeading;
        private readonly string _pluginModLabelHtml;
        private readonly string _activeDependentsHeadingHtml;

        public event EventHandler UpdatePluginsCount;
        public event EventHandler PluginMoved;

		private DevExpress.Utils.Behaviors.BehaviorManager behaviorManager;

		public PluginManagerDXControl()
        {
            _activeDisplayText = LanguageManager.Get("Plugins.Values.Active", "Active");
            _inactiveDisplayText = LanguageManager.Get("Plugins.Values.Inactive", "Inactive");
            _lockedDisplayText = LanguageManager.Get("Plugins.Values.Locked", "Locked");
            _diagnosticErrorsHeading = LanguageManager.Get("Plugins.Diagnostics.ErrorsHeading", "Errors");
            _diagnosticWarningsHeading = LanguageManager.Get("Plugins.Diagnostics.WarningsHeading", "Warnings");
            _pluginModLabelHtml = LanguageManager.Get("Plugins.Details.ModLabelHtml", "<b>Mod:</b> {0}<br/><br/>");
            _activeDependentsHeadingHtml = LanguageManager.Get("Plugins.Details.ActiveDependentsHeadingHtml", "<b>Active plugins depending on this plugin:</b><br/>");

            Text = LanguageManager.Get("Plugins.Title", "Plugins");
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

			_toolbar = new Bar(_barManager, LanguageManager.Get("Plugins.Toolbar.Title", "Plugin Commands"))
			{
				DockStyle = BarDockStyle.Standalone,
				StandaloneBarDockControl = _toolbarHost
			};

			_toolbar.OptionsBar.AllowQuickCustomization = false;
			_toolbar.OptionsBar.DrawDragBorder = false;
			_toolbar.OptionsBar.UseWholeRow = true;

			_moveUpButton = new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.Up.Name", "Up"));
			_moveUpButton.ItemClick +=
				(sender, args) => MoveSelectedUp(sender, args);

			_moveDownButton = new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.Down.Name", "Down"));
			_moveDownButton.ItemClick +=
				(sender, args) => MoveSelectedDown(sender, args);

			_restoreLoadOrderButton =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.RestoreLoadOrder.Name", "Load Order Sorting"))
				{
					Hint =
						LanguageManager.Get("Plugins.Actions.RestoreLoadOrder.Tooltip", "Clear column sorting and restore the actual plugin load order.")
				};

			_restoreLoadOrderButton.ItemClick +=
				(sender, args) => RestoreLoadOrderView(sender, args);

			_disableAllButton =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.DisableAll.Name", "Disable All"));

			_disableAllButton.ItemClick +=
				(sender, args) => DisableAll(sender, args);

			_enableAllButton =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.EnableAll.Name", "Enable All"));

			_enableAllButton.ItemClick +=
				(sender, args) => EnableAll(sender, args);

			_exportButton =
				new BarSubItem(_barManager, LanguageManager.Get("Common.Action.Export", "Export"));

			_importButton =
				new BarSubItem(_barManager, LanguageManager.Get("Plugins.Actions.Import.Name", "Import"));

			BarButtonItem exportToClipboardItem =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.ToClipboard.Name", "To Clipboard"));

			exportToClipboardItem.ItemClick +=
				(sender, args) => ExportToClipboard(sender, args);

			BarButtonItem exportToFileItem =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.ToFile.Name", "To File..."));

			exportToFileItem.ItemClick +=
				(sender, args) => ExportToFile(sender, args);

			BarButtonItem importFromClipboardItem =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.FromClipboard.Name", "From Clipboard"));

			importFromClipboardItem.ItemClick +=
				(sender, args) => ImportFromClipboard(sender, args);

			BarButtonItem importFromFileItem =
				new BarButtonItem(_barManager, LanguageManager.Get("Plugins.Actions.FromFile.Name", "From File..."));

			importFromFileItem.ItemClick +=
				(sender, args) => ImportFromFile(sender, args);

			_exportButton.AddItem(exportToClipboardItem);
			_exportButton.AddItem(exportToFileItem);

			_importButton.AddItem(importFromClipboardItem);
			_importButton.AddItem(importFromFileItem);

			_hideFePluginIndexesToggle = new BarCheckItem(_barManager)
			{
				Caption = LanguageManager.Get("Plugins.Display.HideFePluginIndexes.Name", "Hide FE Plugin Indexes"),
				Hint = LanguageManager.Get("Plugins.Display.HideFePluginIndexes.Tooltip", "Hide FE:xxx values in the LO Index column for light/ESL plugins."),
				CheckBoxVisibility = CheckBoxVisibility.BeforeText
			};

			_hideFePluginIndexesToggle.CheckedChanged += HideFePluginIndexesToggleCheckedChanged;

			_disablePluginSortingRestrictionsToggle = new BarCheckItem(_barManager)
			{
				Caption = LanguageManager.Get("Plugins.SortingRestrictions.Disable.Name", "Disable Plugin Sorting Restrictions"),
				Hint = LanguageManager.Get("Plugins.SortingRestrictions.Disable.Tooltip", "Allow all non-critical, user-managed plugins to be freely reordered, enabled or disabled while retaining dependency warnings."),
				CheckBoxVisibility = CheckBoxVisibility.BeforeText
			};

			_disablePluginSortingRestrictionsToggle.CheckedChanged += PluginRestrictionsToggleCheckedChanged;

			_toolbar.AddItem(_moveUpButton);
			_toolbar.AddItem(_moveDownButton);
			_toolbar.AddItem(_restoreLoadOrderButton);
			_toolbar.AddItem(_hideFePluginIndexesToggle);

			_toolbar.AddItem(_disableAllButton).BeginGroup = true;
			_toolbar.AddItem(_enableAllButton);

			_toolbar.AddItem(_exportButton).BeginGroup = true;
			_toolbar.AddItem(_importButton);
			_toolbar.AddItem(_disablePluginSortingRestrictionsToggle).BeginGroup = true;

			NmmIconProvider.Bind(_moveUpButton, NmmIconAction.MoveUp);
			NmmIconProvider.Bind(_moveDownButton, NmmIconAction.MoveDown);
			NmmIconProvider.Bind(_restoreLoadOrderButton, NmmIconAction.Sort);
			NmmIconProvider.Bind(_hideFePluginIndexesToggle, NmmIconAction.DisplayOptions);
			NmmIconProvider.Bind(_disableAllButton, NmmIconAction.DisableAll);
			NmmIconProvider.Bind(_enableAllButton, NmmIconAction.EnableAll);
			NmmIconProvider.Bind(_exportButton, NmmIconAction.Export);
			NmmIconProvider.Bind(exportToClipboardItem, NmmIconAction.Copy);
			NmmIconProvider.Bind(exportToFileItem, NmmIconAction.Export);
			NmmIconProvider.Bind(_importButton, NmmIconAction.Import);
			NmmIconProvider.Bind(importFromClipboardItem, NmmIconAction.ImportFromClipboard);
			NmmIconProvider.Bind(importFromFileItem, NmmIconAction.ImportFromFile);
			NmmIconProvider.Bind(_disablePluginSortingRestrictionsToggle, NmmIconAction.Restrictions);
			NmmIconProvider.BindBar(_toolbar, NmmButtonPresentationScope.Plugins, false);

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

			_infoScroll = new PanelControl
			{
				Dock = DockStyle.Fill,
				BorderStyle = BorderStyles.NoBorder,
				AutoScroll = true
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

            _dragAutoScrollTimer = new Timer
            {
                Interval = DragAutoScrollIntervalMs
            };
            _dragAutoScrollTimer.Tick += DragAutoScrollTimerTick;

			SetupGrid();
			SetupDragAndDrop();
			ApplySkinAwareAppearance();
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
				
				behavior.BeginDragDrop += Behavior_BeginDragDrop;
				behavior.DragOver += Behavior_DragOver;
				behavior.DragDrop += Behavior_DragDrop;
				behavior.EndDragDrop += Behavior_EndDragDrop;
			});
		}

		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
        {
            if (settings == null) return;

            DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
            DevExpressDisplaySettingsApplier.ApplyToBarManager(
                _barManager,
                settings);
			UpdateToolbarHostHeight();
            ApplySkinAwareAppearance();
            _gridControl.Invalidate();
        }

		/// <summary>
		/// Keeps the standalone toolbar host in sync with the actual DevExpress bar height.
		/// Large SVG sizes can make the bar taller than the original 28px host; if the host
		/// is not resized as well, the bar paints over the grid/header area below it.
		/// </summary>
		private void UpdateToolbarHostHeight()
		{
			if (_toolbarHost == null || _toolbar == null)
				return;

			int barHeight = _toolbar.Size.Height;
			int iconHeight = NmmIconProvider.CurrentIconSize + 8;
			int targetHeight = Math.Max(28, Math.Max(barHeight, iconHeight));

			if (_toolbarHost.Height == targetHeight)
				return;

			_toolbarHost.Height = targetHeight;
			_toolbarHost.Parent?.PerformLayout();
		}

		/// <summary>
		/// Refreshes the plugin detail surface and custom row colors from the active DevExpress skin.
		/// </summary>
		private void ApplySkinAwareAppearance()
		{
			DevExpressDisplaySettingsApplier.ApplySkinSurface(_infoScroll);
			DevExpressDisplaySettingsApplier.ApplySkinSurface(_pictureEdit);

			_infoLabel.Appearance.ForeColor = DevExpressDisplaySettingsApplier.GetSkinColor(
				"ControlText",
				SystemColors.ControlText);
			_infoLabel.Appearance.Options.UseForeColor = true;

			_lockedPluginForeColor = DevExpressDisplaySettingsApplier.GetMutedSkinTextColor();

			NmmColorPalette palette = NmmColorPalette.Resolve(
				NmmIconProvider.CurrentColorProfile,
				DevExpressDisplaySettingsApplier.IsDarkSkinSurface());
			_errorPluginForeColor = palette.PluginErrorColor;
			_warningPluginForeColor = palette.PluginWarningColor;

			_gridView.InvalidateRows();

			if (_viewModel != null)
				UpdatePluginInfo();
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
                    RestoreHideFePluginIndexesSetting();
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
            DevExpressGridLayoutPersistence.ConfigureSessionOnlyFilters(_gridView);
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

            AddColumn(ColActive, LanguageManager.Get("Plugins.Columns.Active.Header", "Active"), 58, true).ColumnEdit = _activeCheckEdit;
            AddColumn(ColLoadOrder, LanguageManager.Get("Plugins.Columns.LoadOrder.Header", "LO Index"), 84, false).AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn(ColIndex, LanguageManager.Get("Plugins.Columns.RelativePosition.Header", "Rel. Position"), 58, false).AppearanceCell.TextOptions.HAlignment = HorzAlignment.Far;
            AddColumn(ColPlugin, LanguageManager.Get("Plugins.Columns.Plugin.Header", "Plugin"), 260, false);
            AddColumn(ColType, LanguageManager.Get("Common.Column.Type", "Type"), 110, false);
            AddColumn(ColOwner, LanguageManager.Get("Plugins.Columns.Owner.Header", "Owner"), 220, false);
            AddColumn(ColStatus, LanguageManager.Get("Common.Column.Status", "Status"), 200, false);
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
			_commandStateUpdateScheduled = false;
			_pluginDescriptionSnapshot = null;
			_pluginDescriptionCache.Clear();
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
			row.StatusSeverity = GetRowDiagnosticSeverity(entry);
		}

		#region Helpers

		/// <summary>
		/// Restores the persisted preference controlling whether FE load-order indexes are rendered.
		/// </summary>
		private void RestoreHideFePluginIndexesSetting()
		{
			bool hideFePluginIndexes = false;

			if (_viewModel?.Settings?.DockPanelLayouts != null &&
				_viewModel.Settings.DockPanelLayouts.ContainsKey(HideFePluginIndexesSettingsKey))
			{
				bool.TryParse(_viewModel.Settings.DockPanelLayouts[HideFePluginIndexesSettingsKey], out hideFePluginIndexes);
			}

			_hideFePluginIndexes = hideFePluginIndexes;
			_synchronizingHideFePluginIndexesToggle = true;

			try
			{
				_hideFePluginIndexesToggle.Checked = hideFePluginIndexes;
			}
			finally
			{
				_synchronizingHideFePluginIndexesToggle = false;
			}
		}

		/// <summary>
		/// Applies and persists the FE-index visibility preference selected from the plugin toolbar.
		/// </summary>
		/// <param name="sender">The event sender.</param>
		/// <param name="e">The item click event arguments.</param>
		private void HideFePluginIndexesToggleCheckedChanged(object sender, ItemClickEventArgs e)
		{
			if (_synchronizingHideFePluginIndexesToggle)
				return;

			_hideFePluginIndexes = _hideFePluginIndexesToggle.Checked;
			_gridView.RefreshData();

			if (_viewModel?.Settings?.DockPanelLayouts == null)
				return;

			_viewModel.Settings.DockPanelLayouts[HideFePluginIndexesSettingsKey] = _hideFePluginIndexes.ToString();
			_viewModel.Settings.Save();
		}

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
						string pluginName = x.Plugin == null ? LanguageManager.Get("Plugins.Validation.PluginStateLabel", "Plugin state") : Path.GetFileName(x.Plugin.Filename);
						return pluginName + ": " + x.Message;
					})
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList();

			StringBuilder message = new StringBuilder();

			message.AppendLine(LanguageManager.Get("Plugins.SortingRestrictions.ReenableBlocked.Message", "Plugin sorting restrictions cannot be re-enabled because the current plugin state is not valid under the normal restrictions."));
			message.AppendLine();
			message.AppendLine(LanguageManager.Get("Plugins.SortingRestrictions.ReenableBlocked.FixIssuesPrompt", "Correct the following issues first:"));

			foreach (string error in errors.Take(20))
				message.AppendLine("- " + error);

			if (errors.Count > 20)
			{
				message.AppendLine();
				message.AppendFormat(LanguageManager.GetFormat("Plugins.SortingRestrictions.ReenableBlocked.AdditionalIssues", "...and {0} additional issue(s)."), errors.Count - 20);
				message.AppendLine();
			}

			if (errors.Count == 0)
			{
				message.AppendLine(LanguageManager.Get("Plugins.SortingRestrictions.ReenableBlocked.NoSpecificError", "- The plugin manager rejected the current state without returning a specific validation error."));
			}

			message.AppendLine();
			message.Append(LanguageManager.Get("Plugins.SortingRestrictions.ReenableBlocked.RemainsEnabled", "The unrestricted mode remains enabled."));

			XtraMessageBox.Show(this, message.ToString(), LanguageManager.Get("Plugins.SortingRestrictions.Title", "Plugin sorting restrictions"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		/// <summary>
		/// Applies a plugin order change while suppressing intermediate collection refreshes and then synchronizes the existing grid rows with the resulting order.
		/// </summary>
		/// <param name="changeAction">The backend operation. It returns <c>null</c> on success or the blocking diagnostics on failure.</param>
		private void ApplyPluginOrderChange(Func<IList<PluginValidationDiagnostic>> changeAction)
		{
			if (changeAction == null)
				return;

			IList<PluginValidationDiagnostic> blockingDiagnostics = null;
			_suppressManagedPluginsRefresh = true;

			try
			{
				blockingDiagnostics = changeAction();
			}
			finally
			{
				_suppressManagedPluginsRefresh = false;
				_managedPluginsRefreshPending = false;

				RefreshRowsAfterPluginOrderChange();
			}

			if (blockingDiagnostics != null)
			{
				ShowPluginStateChangeBlockedMessage(LanguageManager.Get("Plugins.Validation.OrderChangeBlocked.Title", "Plugin order change blocked"), blockingDiagnostics);
				return;
			}

			PluginMoved?.Invoke(this, EventArgs.Empty);
			UpdatePluginsCount?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Reorders the existing grid rows to match the authoritative plugin order and refreshes only values affected by ordering.
		/// </summary>
		private void RefreshRowsAfterPluginOrderChange()
		{
			if (_viewModel == null || _pluginManager == null)
			{
				RebuildRows();
				return;
			}

			List<Plugin> managedPlugins = _viewModel.ManagedPlugins
				.Where(x => x != null)
				.ToList();

			/*
			 * An order-only operation should not add or remove plugins.
			 * Fall back to a complete rebuild if the collections no longer match.
			 */
			if (managedPlugins.Count != _rows.Count ||
				managedPlugins.Any(x => !_rowsByPlugin.ContainsKey(x)))
			{
				RebuildRows();
				return;
			}

			GridViewState state = CaptureGridViewState();
			PluginSnapshot snapshot = _pluginManager.CurrentSnapshot;
			List<PluginManagerDXRow> orderedRows = new List<PluginManagerDXRow>(managedPlugins.Count);

			_gridView.BeginDataUpdate();

			try
			{
				_rows.RaiseListChangedEvents = false;

				foreach (Plugin plugin in managedPlugins)
				{
					PluginManagerDXRow row = _rowsByPlugin[plugin];
					PluginSnapshotEntry entry = snapshot == null ? null : snapshot.GetEntry(plugin);

					row.LoadOrder = entry == null
						? String.Empty
						: entry.ModIndex;

					row.Index = entry == null || !entry.AllocatedIndex.HasValue
						? String.Empty
						: (entry.AllocatedIndex.Value + 1).ToString();

					row.PluginType = entry == null
						? plugin.EffectiveTypeDisplay
						: entry.EffectiveType;

					row.Status = GetStatus(plugin, entry);
					row.StatusSeverity = GetRowDiagnosticSeverity(entry);

					orderedRows.Add(row);
				}

				/*
				 * Reuse the existing row objects and emit one Reset instead of
				 * removing, recreating and rebinding every plugin independently.
				 */
				_rows.Clear();

				foreach (PluginManagerDXRow row in orderedRows)
					_rows.Add(row);
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
            if (plugin == null || _pluginManager == null)
                return new List<Plugin>();

            PluginSnapshot snapshot = _pluginManager.CurrentSnapshot;
            return snapshot == null ? new List<Plugin>() : snapshot.GetActiveDependents(plugin).ToList();
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
                LanguageManager.GetFormat("Plugins.Validation.ActivationBlocked.Message", "The plugin \"{0}\" cannot be enabled because one or more required masters are missing or inactive."),
                Path.GetFileName(plugin.Filename));

            if (missingMasters != null &&
                missingMasters.Count > 0)
            {
                message.AppendLine();
                message.AppendLine();
                message.AppendLine(LanguageManager.Get("Plugins.Validation.MissingMastersHeading", "Missing masters:"));

                foreach (string master in missingMasters)
                    message.AppendLine("- " + master);
            }

            if (inactiveMasters != null &&
                inactiveMasters.Count > 0)
            {
                message.AppendLine();
                message.AppendLine();
                message.AppendLine(LanguageManager.Get("Plugins.Validation.InactiveMastersHeading", "Inactive masters:"));

                foreach (string master in inactiveMasters)
                    message.AppendLine("- " + master);
            }

            message.AppendLine();
            message.AppendLine();
            message.Append(
                LanguageManager.Get("Plugins.Validation.ActivationBlocked.Resolution", "Install or enable the required masters before enabling this plugin."));

            XtraMessageBox.Show(
                this,
                message.ToString(),
                LanguageManager.Get("Plugins.Validation.ActivationBlocked.Title", "Plugin activation blocked"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private void ShowDeactivationBlockedMessage(
            Plugin plugin,
            IList<Plugin> activeDependents)
        {
            StringBuilder message = new StringBuilder();

            message.AppendFormat(
                LanguageManager.GetFormat("Plugins.Validation.DeactivationBlocked.Message", "The plugin \"{0}\" cannot be disabled because these active plugins depend on it:"),
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
                LanguageManager.Get("Plugins.Validation.DeactivationBlocked.Resolution", "Disable the dependent plugins first."));

            XtraMessageBox.Show(
                this,
                message.ToString(),
                LanguageManager.Get("Plugins.Validation.DeactivationBlocked.Title", "Plugin deactivation blocked"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

		/// <summary>
		/// Attempts to apply a plugin active-state change and reports authoritative backend validation failures.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose active state should be changed.</param>
		/// <param name="p_booRequestedActive">Whether the plugin should be active.</param>
		/// <returns><c>true</c> if the requested state was applied; otherwise, <c>false</c>.</returns>
		private bool TryApplyRequestedActiveState(Plugin p_plgPlugin, bool p_booRequestedActive)
		{
			IList<PluginValidationDiagnostic> blockingDiagnostics;

			if (_viewModel.TrySetPluginActivation(p_plgPlugin, p_booRequestedActive, out blockingDiagnostics))
				return true;

			ShowPluginStateChangeBlockedMessage(
				p_booRequestedActive
                    ? LanguageManager.Get("Plugins.Validation.ActivationBlocked.Title", "Plugin activation blocked")
                    : LanguageManager.Get("Plugins.Validation.DeactivationBlocked.Title", "Plugin deactivation blocked"),
				blockingDiagnostics);

			return false;
		}

		/// <summary>
		/// Shows validation errors returned by the authoritative plugin-state pipeline.
		/// </summary>
		/// <param name="p_strTitle">The dialog title.</param>
		/// <param name="p_lstDiagnostics">The blocking diagnostics.</param>
		private void ShowPluginStateChangeBlockedMessage(string p_strTitle, IList<PluginValidationDiagnostic> p_lstDiagnostics)
		{
			List<PluginValidationDiagnostic> diagnostics = (p_lstDiagnostics ?? new List<PluginValidationDiagnostic>())
				.Where(x => x != null && x.Severity == PluginValidationSeverity.Error)
				.ToList();

			StringBuilder message = new StringBuilder();
			message.AppendLine(LanguageManager.Get("Plugins.Validation.ChangeBlocked.Message", "The requested change was not applied because it would introduce a new plugin validation issue."));

			if (diagnostics.Count > 0)
			{
				message.AppendLine();

				foreach (PluginValidationDiagnostic diagnostic in diagnostics.Take(20))
				{
					string pluginName = diagnostic.Plugin == null
						? String.Empty
						: Path.GetFileName(diagnostic.Plugin.Filename);

					message.Append("- ");

					if (!String.IsNullOrWhiteSpace(pluginName))
						message.Append(pluginName + ": ");

					message.AppendLine(diagnostic.Message);
				}

				if (diagnostics.Count > 20)
				{
					message.AppendLine();
					message.AppendFormat(LanguageManager.GetFormat("Plugins.Validation.ChangeBlocked.AdditionalIssues", "...and {0} additional issue(s)."), diagnostics.Count - 20);
				}
			}
			else
			{
				message.AppendLine();
				message.Append(LanguageManager.Get("Plugins.Validation.ChangeBlocked.NoSpecificError", "The plugin manager did not return a specific validation error."));
			}

			XtraMessageBox.Show(this, message.ToString(), p_strTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
			if (p_lstCurrentOrder == null || p_lstProposedOrder == null || p_lstCurrentOrder.Count != p_lstProposedOrder.Count)
				return false;

			for (int index = 0; index < p_lstCurrentOrder.Count; index++)
			{
				Plugin plugin = p_lstCurrentOrder[index];

				if (IsPluginOrderLocked(plugin) && !PluginComparer.Filename.Equals(plugin, p_lstProposedOrder[index]))
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
                new HashSet<Plugin>(selectedPlugins, PluginComparer.Filename);

            if (direction < 0)
            {
                for (int index = 1; index < currentOrder.Count; index++)
                {
                    if (selection.Contains(currentOrder[index]) &&
                        !selection.Contains(currentOrder[index - 1]) &&
						IsPluginOrderLocked(currentOrder[index - 1]))
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
						IsPluginOrderLocked(currentOrder[index + 1]))
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
				return _lockedDisplayText;

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
		/// Gets the grid severity for a snapshot entry independently from whether restrictions currently block the operation.
		/// </summary>
		/// <param name="p_pseEntry">The snapshot entry to inspect.</param>
		/// <returns>Red for an active state that cannot load, orange for potentially unstable states, or <c>null</c> when no diagnostic is present.</returns>
		private static PluginValidationSeverity? GetRowDiagnosticSeverity(PluginSnapshotEntry p_pseEntry)
		{
			if (p_pseEntry == null || p_pseEntry.Diagnostics.Count == 0)
				return null;

			if (p_pseEntry.Active && p_pseEntry.Diagnostics.Any(x =>
				x.Kind == PluginValidationIssueKind.MissingMaster ||
				x.Kind == PluginValidationIssueKind.InactiveRequiredMaster ||
				x.Kind == PluginValidationIssueKind.DependencyCycle ||
				x.Kind == PluginValidationIssueKind.UnsupportedPluginClass ||
				x.Kind == PluginValidationIssueKind.AddressSpaceExhausted))
			{
				return PluginValidationSeverity.Error;
			}

			return p_pseEntry.Diagnostics.Any(x => x.Severity == PluginValidationSeverity.Error || x.Severity == PluginValidationSeverity.Warning)
				? PluginValidationSeverity.Warning
				: PluginValidationSeverity.Info;
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

			_gridView.BeginDataUpdate();

			try
			{
				_rows.RaiseListChangedEvents = false;

				foreach (Plugin plgPlugin in lstPlugins)
					AddOrUpdateRow(plgPlugin, psnSnapshot, hstActivePlugins, null);
			}
			finally
			{
				_rows.RaiseListChangedEvents = true;
				_rows.ResetBindings();
				_gridView.EndDataUpdate();
			}

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
                TryApplyRequestedActiveState(row.Plugin, requestedActive);
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

		/// <summary>
		/// Starts polling the global mouse position so the plugin grid can continue scrolling even after the pointer leaves its bounds.
		/// </summary>
		private void Behavior_BeginDragDrop(object sender, BeginDragDropEventArgs e)
		{
			if (_gridView.RowCount > 0)
				_dragAutoScrollTimer.Start();
		}

		/// <summary>
		/// Stops edge scrolling when the DevExpress drag operation completes or is cancelled.
		/// </summary>
		private void Behavior_EndDragDrop(object sender, EndDragDropEventArgs e)
		{
			StopDragAutoScroll();
		}

		/// <summary>
		/// Scrolls the plugin grid while a drag is held near or beyond its top or bottom edge.
		/// </summary>
		private void DragAutoScrollTimerTick(object sender, EventArgs e)
		{
			if (IsDisposed || Disposing || !_gridControl.IsHandleCreated ||
				(Control.MouseButtons & MouseButtons.Left) == MouseButtons.None)
			{
				StopDragAutoScroll();
				return;
			}

			Rectangle viewBounds = _gridView.ViewRect;
			if (viewBounds.Width <= 0 || viewBounds.Height <= 0 || _gridView.RowCount <= 0)
				return;

			Point mousePosition = _gridControl.PointToClient(Control.MousePosition);
			if (mousePosition.X < viewBounds.Left - DragAutoScrollHorizontalTolerance ||
				mousePosition.X > viewBounds.Right + DragAutoScrollHorizontalTolerance)
			{
				return;
			}

			int scrollDelta = GetDragAutoScrollDelta(mousePosition.Y, viewBounds);
			if (scrollDelta == 0)
				return;

			int previousTopRowIndex = _gridView.TopRowIndex;
			int nextTopRowIndex = Math.Max(0, Math.Min(_gridView.RowCount - 1, previousTopRowIndex + scrollDelta));
			if (nextTopRowIndex == previousTopRowIndex)
				return;

			_gridView.TopRowIndex = nextTopRowIndex;
			if (_gridView.TopRowIndex != previousTopRowIndex)
				_gridControl.Invalidate();
		}

		/// <summary>
		/// Calculates the vertical row step for the current pointer position, accelerating when the pointer is farther outside the grid.
		/// </summary>
		private static int GetDragAutoScrollDelta(int mouseY, Rectangle viewBounds)
		{
			int upperBoundary = viewBounds.Top + DragAutoScrollEdgeThreshold;
			if (mouseY <= upperBoundary)
			{
				int distance = upperBoundary - mouseY;
				return -Math.Min(DragAutoScrollMaximumStep, 1 + distance / DragAutoScrollAccelerationPixels);
			}

			int lowerBoundary = viewBounds.Bottom - DragAutoScrollEdgeThreshold;
			if (mouseY >= lowerBoundary)
			{
				int distance = mouseY - lowerBoundary;
				return Math.Min(DragAutoScrollMaximumStep, 1 + distance / DragAutoScrollAccelerationPixels);
			}

			return 0;
		}

		/// <summary>
		/// Stops the plugin drag auto-scroll timer.
		/// </summary>
		private void StopDragAutoScroll()
		{
			_dragAutoScrollTimer.Stop();
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

			// Allow the visual drag to continue. Edge scrolling is handled by the dedicated timer.
			e.Action = DragDropActions.Move;
			e.Cursor = System.Windows.Forms.Cursors.Default;
		}

		private void Behavior_DragDrop(object sender, DragDropEventArgs e)
		{
			StopDragAutoScroll();

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
				ApplyPluginOrderChange(() =>
				{
					IList<PluginValidationDiagnostic> blockingDiagnostics;
					return _pluginManager.TrySetPluginOrder(proposedOrder, out blockingDiagnostics) ? null : blockingDiagnostics;
				});
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

			ApplyPluginOrderChange(() =>
			{
				IList<PluginValidationDiagnostic> blockingDiagnostics;
				return _pluginManager.TrySetPluginOrder(proposedOrder, out blockingDiagnostics) ? null : blockingDiagnostics;
			});
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

            TryApplyRequestedActiveState(plugin, requestedActive);
            RequestActivePluginsRefresh();
        }

		private void GridViewFocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
		{
			UpdatePluginInfo();
		}

		private void GridViewCustomColumnDisplayText(object sender, CustomColumnDisplayTextEventArgs e)
        {
            if (e.Column == null)
                return;

            if (e.Column.FieldName == ColActive && e.Value is bool)
            {
                e.DisplayText = (bool)e.Value ? _activeDisplayText : _inactiveDisplayText;
                return;
            }

            // Suppress only the rendered data-cell value. The underlying index remains available
            // to sorting and filtering, so this option cannot alter the effective load-order view.
            if (_hideFePluginIndexes &&
                e.Column.FieldName == ColLoadOrder &&
                e.ListSourceRowIndex >= 0 &&
                e.Value is string loadOrderIndex &&
                loadOrderIndex.StartsWith("FE:", StringComparison.OrdinalIgnoreCase))
            {
                e.DisplayText = String.Empty;
            }
        }

		private void GridViewSelectionChanged(object sender, DevExpress.Data.SelectionChangedEventArgs e)
		{
			RequestCommandStateUpdate();
		}

		/// <summary>
		/// Coalesces selection-driven command-state updates into one UI callback.
		/// </summary>
		private void RequestCommandStateUpdate()
		{
			if (_commandStateUpdateScheduled || IsDisposed || Disposing || !IsHandleCreated)
				return;

			_commandStateUpdateScheduled = true;
			BeginInvoke((Action)FlushCommandStateUpdate);
		}

		/// <summary>
		/// Applies the latest coalesced command-state update.
		/// </summary>
		private void FlushCommandStateUpdate()
		{
			_commandStateUpdateScheduled = false;

			if (!IsDisposed && !Disposing)
				UpdateCommandState();
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

			bool isSelected =
				e.RowHandle == _gridView.FocusedRowHandle ||
				_gridView.IsRowSelected(e.RowHandle);

			if (String.Equals(row.Status, _lockedDisplayText, StringComparison.Ordinal))
			{
				// Keep selected/focused rows on the skin's native selection palette.
				if (!isSelected)
					e.Appearance.ForeColor = _lockedPluginForeColor;

				return;
			}

			if (isSelected)
				return;

			if (row.StatusSeverity == PluginValidationSeverity.Error)
			{
				e.Appearance.ForeColor = _errorPluginForeColor;
				return;
			}

			if (row.StatusSeverity == PluginValidationSeverity.Warning)
				e.Appearance.ForeColor = _warningPluginForeColor;
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

			AppendPluginDiagnosticSection(p_sbrDetails, entry.Diagnostics, PluginValidationSeverity.Error, _diagnosticErrorsHeading, _errorPluginForeColor);
			AppendPluginDiagnosticSection(p_sbrDetails, entry.Diagnostics, PluginValidationSeverity.Warning, _diagnosticWarningsHeading, _warningPluginForeColor);
		}

		/// <summary>
		/// Appends diagnostics of a specific severity to the plugin detail text.
		/// </summary>
		/// <param name="p_sbrDetails">The detail text builder.</param>
		/// <param name="p_lstDiagnostics">The diagnostics to inspect.</param>
		/// <param name="p_pvsSeverity">The severity to append.</param>
		/// <param name="p_strHeading">The section heading.</param>
		/// <param name="p_clrForeColor">The skin-aware foreground color for this diagnostic severity.</param>
		private static void AppendPluginDiagnosticSection(StringBuilder p_sbrDetails, IList<PluginValidationDiagnostic> p_lstDiagnostics, PluginValidationSeverity p_pvsSeverity, string p_strHeading, Color p_clrForeColor)
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

			p_sbrDetails.AppendFormat(
				"<color={0},{1},{2}><b>{3}:</b><br/>",
				p_clrForeColor.R,
				p_clrForeColor.G,
				p_clrForeColor.B,
				HtmlEncode(p_strHeading));

			foreach (string message in messages)
				p_sbrDetails.AppendFormat("• {0}<br/>", HtmlEncode(message));

			p_sbrDetails.Append("</color>");
		}

		/// <summary>
		/// Gets a plugin description cached for the current snapshot and physical file revision.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose description should be returned.</param>
		/// <returns>The current formatted plugin description.</returns>
		private string GetCachedPluginDescription(Plugin p_plgPlugin)
		{
			if (p_plgPlugin == null || _viewModel == null || String.IsNullOrWhiteSpace(p_plgPlugin.Filename))
				return String.Empty;

			PluginSnapshot currentSnapshot = _pluginManager == null ? null : _pluginManager.CurrentSnapshot;

			if (!ReferenceEquals(_pluginDescriptionSnapshot, currentSnapshot))
			{
				_pluginDescriptionSnapshot = currentSnapshot;
				_pluginDescriptionCache.Clear();
			}

			DateTime lastWriteTimeUtc = DateTime.MinValue;
			long fileLength = -1;

			try
			{
				FileInfo fileInfo = new FileInfo(p_plgPlugin.Filename);

				if (fileInfo.Exists)
				{
					lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
					fileLength = fileInfo.Length;
				}
			}
			catch
			{
			}

			Tuple<DateTime, long, string> cachedDescription;

			if (_pluginDescriptionCache.TryGetValue(p_plgPlugin.Filename, out cachedDescription) &&
				cachedDescription.Item1 == lastWriteTimeUtc &&
				cachedDescription.Item2 == fileLength)
			{
				return cachedDescription.Item3;
			}

			string description = _viewModel.GetPluginDescription(p_plgPlugin.Filename) ?? String.Empty;
			_pluginDescriptionCache[p_plgPlugin.Filename] = Tuple.Create(lastWriteTimeUtc, fileLength, description);
			return description;
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

			PluginManagerDXRow focusedRow;
			string owner = _rowsByPlugin.TryGetValue(plugin, out focusedRow)
				? focusedRow.Owner
				: _viewModel.GetPluginOwner(plugin);
			string description = GetCachedPluginDescription(plugin);

			StringBuilder details = new StringBuilder();

			if (!string.IsNullOrWhiteSpace(owner))
			{
				details.AppendFormat(
					_pluginModLabelHtml,
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
                details.Append(_activeDependentsHeadingHtml);
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

			HashSet<Plugin> activePlugins = _viewModel == null
				? new HashSet<Plugin>(PluginComparer.Filename)
				: new HashSet<Plugin>(_viewModel.ActivePlugins.Where(x => x != null), PluginComparer.Filename);

			_disableAllButton.Enabled = _viewModel != null && activePlugins.Any(x => _viewModel.CanChangeActiveState(x));
			_enableAllButton.Enabled = _viewModel != null && _viewModel.ManagedPlugins.Any(x => x != null && !activePlugins.Contains(x) && _viewModel.CanChangeActiveState(x));
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

			ApplyPluginOrderChange(() =>
			{
				IList<PluginValidationDiagnostic> blockingDiagnostics;
				return _viewModel.TryMovePluginsUp(selected, out blockingDiagnostics) ? null : blockingDiagnostics;
			});
		}

        private void MoveSelectedDown(object sender, EventArgs e)
        {
            IList<Plugin> selected = GetSelectedPlugins();

            if (!CanMoveSelectionAcrossOneRow(selected, 1))
            {
                RebuildRows();
                return;
            }

			ApplyPluginOrderChange(() =>
			{
				IList<PluginValidationDiagnostic> blockingDiagnostics;
				return _viewModel.TryMovePluginsDown(selected, out blockingDiagnostics) ? null : blockingDiagnostics;
			});
		}

        private void DisableAll(object sender, EventArgs e)
        {
            if (XtraMessageBox.Show(LanguageManager.Get("Plugins.DisableAll.Confirm.Message", "Do you want to disable all the active plugins?"), LanguageManager.Get("Plugins.DisableAll.Confirm.Title", "Disable Plugins"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                _viewModel.PluginsDisableAll();
        }

        private void EnableAll(object sender, EventArgs e)
        {
            if (XtraMessageBox.Show(LanguageManager.Get("Plugins.EnableAll.Confirm.Message", "Do you want to enable all the inactive plugins?"), LanguageManager.Get("Plugins.EnableAll.Confirm.Title", "Enable Plugins"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
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
            XtraMessageBox.Show(this, LanguageManager.Get("Plugins.Export.Failed.Message", "The current load order could not be exported."), LanguageManager.Get("Plugins.Export.Failed.Title", "Export Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ViewModelExportSucceeded(object sender, EventArgs e)
        {
            XtraMessageBox.Show(this, LanguageManager.Get("Plugins.Export.Succeeded.Message", "The current load order was successfully exported."), LanguageManager.Get("Plugins.Export.Succeeded.Title", "Export Succeeded"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ViewModelImportFailed(object sender, EventArgs e)
        {
            XtraMessageBox.Show(this, LanguageManager.Get("Plugins.Import.Failed.Message", "The selected load order could not be imported."), LanguageManager.Get("Plugins.Import.Failed.Title", "Import Failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                try
                {
                    if (_viewModel.Settings.DockPanelLayouts.ContainsKey(GridLayoutKey))
                    {
                        string layout = _viewModel.Settings.DockPanelLayouts[GridLayoutKey];
                        if (!String.IsNullOrWhiteSpace(layout))
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(layout);
                            using (MemoryStream stream = new MemoryStream(bytes))
                            {
                                _gridView.RestoreLayoutFromStream(stream);
                            }
                        }
                    }
                }
                catch
                {
                    _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
                }

                DevExpressGridLayoutPersistence.ClearTransientFilters(_gridView);
                if (_viewModel.Settings.DockPanelLayouts.ContainsKey(GridColumnWidthsKey))
                    DevExpressGridLayoutPersistence.RestoreColumnWidths(_gridView, _viewModel.Settings.DockPanelLayouts[GridColumnWidthsKey]);
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

                _viewModel.Settings.DockPanelLayouts[GridColumnWidthsKey] =
                    DevExpressGridLayoutPersistence.SerializeColumnWidths(_gridView);
            }
            catch
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridColumnWidthsKey);
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
                _dragAutoScrollTimer?.Stop();
                SaveGridLayout();

                if (_gridLayoutSaveTimer != null)
                {
                    _gridLayoutSaveTimer.Tick -=
                        GridLayoutSaveTimerTick;
                    _gridLayoutSaveTimer.Dispose();
                }

                if (_dragAutoScrollTimer != null)
                {
                    _dragAutoScrollTimer.Tick -= DragAutoScrollTimerTick;
                    _dragAutoScrollTimer.Dispose();
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
