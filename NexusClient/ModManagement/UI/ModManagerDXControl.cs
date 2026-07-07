namespace Nexus.Client.ModManagement.UI
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Windows.Forms;

    using DevExpress.Utils;
    using DevExpress.XtraEditors.Repository;
    using DevExpress.XtraGrid;
    using DevExpress.XtraGrid.Columns;
    using DevExpress.XtraGrid.Views.Grid;
    using DevExpress.XtraGrid.Views.Grid.ViewInfo;

    using Nexus.Client.BackgroundTasks;
    using Nexus.Client.BackgroundTasks.UI;
    using Nexus.Client.Commands;
    using Nexus.Client.Commands.Generic;
    using Nexus.Client.ModManagement;
    using Nexus.Client.Mods;
    using Nexus.Client.UI;
    using Nexus.Client.UI.Controls;
    using Nexus.Client.Util;
    using Nexus.Client.Util.Collections;
    using Nexus.UI.Controls;
    using WeifenLuo.WinFormsUI.Docking;

    /// <summary>
    /// DevExpress XtraGrid-based mod list panel.
    /// Implements <see cref="IModManagerView"/> so it is a drop-in replacement for
    /// the legacy <see cref="ModManagerControl"/> inside <see cref="MainForm"/>.
    /// </summary>
    public partial class ModManagerDXControl : ManagedFontDockContent, IModManagerView
    {
        // ── fields ──────────────────────────────────────────────────────────

        private ModManagerVM _viewModel;
        private bool _disableSummary;
        private bool _showUpdatesOnly;
        private bool _categoryViewActive;
        private bool _restoringGridLayout;

        // lazy-initialised flat warning-triangle icon drawn in GetWarningIcon()
        private Bitmap _warningIcon;
        private Bitmap _inlineEditIcon;
        private Bitmap _inlineAcceptIcon;
        private Bitmap _inlineCancelIcon;
        private Panel _renamePanel;
        private TextBox _renameTextBox;
        private Button _renameAcceptButton;
        private Button _renameCancelButton;
        private ToolStripDropDownButton _displayButton;
        private ToolStripDropDownButton _displayOptionsButton;
        private ToolStripMenuItem _toggleColouredCategoriesMenuItem;
        private ToolStripMenuItem _toggleRowHighlightsMenuItem;
        private ToolStripMenuItem _toggleActiveModsBoldMenuItem;
        private ComboBox _gridFontCombo;
        private ComboBox _gridFontSizeCombo;
        private ComboBox _gridDensityCombo;
        private IMod _renameMod;
        private string _renameOriginalName;
        private int _renameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
        private int _hoveredModNameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
        private Rectangle _hoveredModNameCellBounds = Rectangle.Empty;
        private Rectangle _hoveredModNameIconBounds = Rectangle.Empty;
        private bool _suppressNextDoubleClick;
        private bool _updatingGridDisplayControls;
        private Timer _columnFillTimer;
        private bool _pendingColumnSizing;
        private bool _missingArchiveScanQueued;
        private string _gridFontFamilyName = DefaultGridFontFamily;
        private float _gridFontSizePt = DefaultGridFontSizePt;
        private string _gridDensity = DefaultGridDensity;
        private bool _showColouredCategories = true;
        private bool _showRowHighlights = true;
        private bool _showActiveModsInBold;
        private readonly HashSet<string> _activeModFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<IMod> _installedMods = new HashSet<IMod>();
        private readonly Dictionary<IMod, ModVisualStatus> _modVisualStatusCache = new Dictionary<IMod, ModVisualStatus>();
        private readonly Dictionary<string, bool> _missingArchiveByFileName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly object _missingArchiveLock = new object();
        private Image _modInstalledDisabledIcon;
        private Image _modInstalledActiveIcon;

        /// <summary>
        /// Flat list that backs both the DevExpress grid and internal row lookups.
        /// Using a plain List&lt;IMod&gt; (not BindingList) is intentional:
        /// BindingList&lt;T&gt; subscribes to every item's INotifyPropertyChanged and then fires
        /// ListChanged on whichever thread raises the event.  When the mod installer sets
        /// a property from a background thread DevExpress detects the cross-thread call and
        /// throws InvalidOperationException.  With a plain List we push all mutations and
        /// RefreshDataSource calls through the UI thread ourselves, so DevExpress never sees
        /// a background-thread notification.
        /// </summary>
        private readonly List<IMod> _modList = new List<IMod>();

        // column field-name constants (used as column names, not as PropertyDescriptor field names)
        private enum ModVisualStatus { Uninstalled, InstalledUnlinked, InstalledActive }

        private const string ColModStatus    = "ModStatus";
        private const string ColModName      = "ModName";
        private const string ColVersion      = "HumanReadableVersion";
        private const string ColLastKnown    = "LastKnownVersion";
        private const string ColAuthor       = "Author";
        private const string ColCategory     = "CategoryId";
        private const string ColInstallDate  = "InstallDate";
        private const string ColDownloadDate = "DownloadDate";
        private const string ColEndorsed     = "IsEndorsed";
        private const string ColDownloadId   = "DownloadId";
        private const string GridLayoutKey   = "modManagerDXGrid";
        private const string GridSortKey     = GridLayoutKey + ".Sort";
        private const string GridFontKey     = GridLayoutKey + ".Font";
        private const string GridFontSizeKey = GridLayoutKey + ".FontSize";
        private const string GridDensityKey  = GridLayoutKey + ".Density";
        private const string GridColouredCategoriesKey = GridLayoutKey + ".ColouredCategories";
        private const string GridRowHighlightsKey = GridLayoutKey + ".RowHighlights";
        private const string GridActiveModsBoldKey = GridLayoutKey + ".ActiveModsBold";
        private const string GridCategoryViewKey = GridLayoutKey + ".CategoryView";
        private const string GridCollapsedCategoriesKey = GridLayoutKey + ".CollapsedCategories";
        private const string DefaultGridFontFamily = "Segoe UI";
        private const float DefaultGridFontSizePt = 9f;
        private const string DefaultGridDensity = "Compact";
        private const int ModStatusIconSize = 20;
        private const int InlineEditIconSize = 18;
        private static readonly string[] GridFontChoices = { "Segoe UI", "Corbel", "Calibri", "Tahoma", "Verdana" };
        private static readonly string[] GridFontSizeChoices = { "8 pt", "9 pt", "10 pt", "11 pt", "12 pt" };
        private static readonly string[] GridDensityChoices = { "Compact", "Comfortable", "Spacious" };

        // ── IModManagerView events ────────────────────────────────────────────

        /// <inheritdoc/>
        public event EventHandler SetTextBoxFocus;
        /// <inheritdoc/>
        public event EventHandler ResetSearchBox;
        /// <inheritdoc/>
        public event EventHandler UpdateModsCount;
        /// <inheritdoc/>
        public event EventHandler<ModEventArgs> UninstallModFromProfiles;
        /// <inheritdoc/>
        public event EventHandler UninstalledAllMods;

        // ── constructor ──────────────────────────────────────────────────────

        public ModManagerDXControl()
        {
            InitializeComponent();
            InitializeToolbarIcons();
            ApplyToolbarActionLabels();
            _columnFillTimer = new Timer(components) { Interval = 120 };
            _columnFillTimer.Tick += ColumnFillTimer_Tick;
            Text = "Mods";
            InitializeInlineRenameEditor();
            SetupGrid();
            InitializeGridDisplayOptions();
            InitializeGridFontSelector();
            UpdateSwitchViewText();
        }

        // ── IModManagerView : ViewModel ──────────────────────────────────────

        /// <inheritdoc/>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ModManagerVM ViewModel
        {
            get => _viewModel;
            set
            {
                if (_viewModel != null)
                    UnhookViewModel();

                _viewModel = value;

                if (_viewModel != null)
                {
                    HookViewModel();
                    RestoreGridFont();
                    RestoreGridDisplayOptions();
                }
            }
        }

        // ── IModManagerView : operations ─────────────────────────────────────

        /// <inheritdoc/>
        public void DeactivateAllMods(bool forceUninstall, bool silent)
        {
            _viewModel?.DeactivateMultipleMods(_viewModel.ActiveMods, forceUninstall, silent, false);
        }

        /// <inheritdoc/>
        public void DeactivateAllMods(IList<IMod> mods, bool forceUninstall, bool silent, bool filesOnly)
        {
            if (_viewModel == null) return;
            var oclMods = new ThreadSafeObservableList<IMod>(mods);
            _viewModel.DeactivateMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), forceUninstall, silent, filesOnly);
        }

        /// <inheritdoc/>
        public void DisableAllMods(bool silent)
        {
            if (_viewModel == null) return;
            var enabled = _viewModel.ActiveMods
                .Where(x => _viewModel.VirtualModActivator.ActiveModList
                    .Contains(Path.GetFileName(x.Filename).ToLowerInvariant()))
                .ToList();
            if (enabled.Count > 0)
                _viewModel.DisableMultipleMods(enabled, silent);
        }

        /// <inheritdoc/>
        public void ForceListRefresh()
        {
            if (InvokeRequired) { Invoke((MethodInvoker)ForceListRefresh); return; }
            gridView.InvalidateRows();
        }

        /// <inheritdoc/>
        public void ResetColumns()
        {
            if (_viewModel?.Settings != null)
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridSortKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridFontKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridFontSizeKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridDensityKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridColouredCategoriesKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridRowHighlightsKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridActiveModsBoldKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridCategoryViewKey);
                _viewModel.Settings.DockPanelLayouts.Remove(GridCollapsedCategoriesKey);
                _viewModel.Settings.Save();
            }

            _restoringGridLayout = true;
            gridView.Columns.Clear();
            BuildColumns();
            gridView.ClearSorting();
            _restoringGridLayout = false;
            SelectGridDisplay(DefaultGridFontFamily, DefaultGridFontSizePt, DefaultGridDensity, false);
            SetColouredCategoriesVisible(true, false);
            SetRowHighlightsVisible(true, false);
            SetActiveModsBold(false, false);
            ApplyColumnSizing();
        }

        /// <inheritdoc/>
        public void SetCommandExecutableStatus()
        {
            if (_viewModel == null) return;
            var mod = SelectedMod;
            if (mod != null)
            {
                bool active = _viewModel.VirtualModActivator.ActiveModList
                    .Contains(Path.GetFileName(mod.Filename).ToLowerInvariant());
                _viewModel.DisableModCommand.CanExecute  = active;
                _viewModel.ActivateModCommand.CanExecute = !active;
                _viewModel.DeleteModCommand.CanExecute   = true;
                _viewModel.TagModCommand.CanExecute      = true;
            }
            else
            {
                _viewModel.DisableModCommand.CanExecute  = false;
                _viewModel.ActivateModCommand.CanExecute = false;
                _viewModel.DeleteModCommand.CanExecute   = false;
                _viewModel.TagModCommand.CanExecute      = false;
            }
            UpdateToolbarState();
        }

        /// <inheritdoc/>
        public void ToggleDisabledSummary(bool disabled)
        {
            _disableSummary = disabled;
        }

        /// <inheritdoc/>
        public void FindItemWithText(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
                gridView.ActiveFilterString = string.Empty;
            else
                gridView.ActiveFilterString = $"[{ColModName}] Like '%{filter.Replace("'", "''")}%'";
        }

        /// <inheritdoc/>
        public void SetSkyrimDownloadModeFeedback()
        {
            if (_viewModel == null || !_viewModel.IsSkyrimSEGameMode) return;
            tsbSkyrimDownloads.Image       = LoadSvgIcon("toolbar_skyrim.svg", 16) ?? _viewModel.SkyrimDownloadImage;
            tsbSkyrimDownloads.Text        = "Download Mode: " + GetSkyrimDownloadModeLabel();
            tsbSkyrimDownloads.ToolTipText = $"Skyrim SE current download mode: {_viewModel.SkyrimSEDownloadModeDescriptor}";
            tsbSkyrimDownloads.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            tsbSkyrimDownloads.ImageScaling = ToolStripItemImageScaling.None;
            tsbSkyrimDownloads.TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        // ── public helpers ───────────────────────────────────────────────────

        /// <summary>Returns the currently focused mod, or <c>null</c>.</summary>
        public IMod SelectedMod
        {
            get
            {
                int h = gridView.FocusedRowHandle;
                if (h < 0) return null;
                int src = gridView.GetDataSourceRowIndex(h);
                if (src < 0 || src >= _modList.Count) return null;
                return _modList[src];
            }
        }

        /// <summary>Returns all selected mods.</summary>
        public List<IMod> SelectedMods
        {
            get
            {
                var list = new List<IMod>();
                int[] rows = gridView.GetSelectedRows();
                if (rows == null) return list;
                foreach (int h in rows)
                {
                    if (h < 0) continue;
                    int src = gridView.GetDataSourceRowIndex(h);
                    if (src >= 0 && src < _modList.Count)
                        list.Add(_modList[src]);
                }
                return list;
            }
        }

        // ── ViewModel wiring ─────────────────────────────────────────────────

        private void HookViewModel()
        {
            _viewModel.UpdatingCategory      += VM_UpdatingCategory;
            _viewModel.UpdatingMods          += VM_UpdatingMods;
            _viewModel.UpdatingCategories    += VM_UpdatingCategories;
            _viewModel.TogglingAllWarning       += VM_TogglingAllWarning;
            _viewModel.TogglingModUpdateChecks  += VM_TogglingModUpdateChecks;
            _viewModel.ReadMeManagerSetup       += VM_ReadMeManagerSetup;
            _viewModel.AddingMod             += VM_AddingMod;
            _viewModel.DeletingMod           += VM_DeletingMod;
            _viewModel.ActivatingMultipleMods+= VM_ActivatingMultipleMods;
            _viewModel.ActivatingMod         += VM_ActivatingMod;
            _viewModel.ReinstallingMod       += VM_ReinstallingMod;
            _viewModel.DisablingMultipleMods += VM_DisablingMultipleMods;
            _viewModel.DeletingMultipleMods  += VM_DeletingMultipleMods;
            _viewModel.DeactivatingMultipleMods += VM_DeactivatingMultipleMods;
            _viewModel.AutomaticDownloading  += VM_AutomaticDownloading;
            _viewModel.ChangingModActivation += VM_ChangingModActivation;
            _viewModel.TaggingMod            += VM_TaggingMod;
            _viewModel.ExportFailed          += VM_ExportFailed;
            _viewModel.ExportSucceeded       += VM_ExportSucceeded;

            _viewModel.ManagedMods.CollectionChanged += ManagedMods_CollectionChanged;
            _viewModel.ActiveMods.CollectionChanged  += ActiveMods_CollectionChanged;

            _viewModel.ConfirmModFileDeletion  = ConfirmModFileDeletion;
            _viewModel.ConfirmModFileOverwrite = ConfirmModFileOverwrite;
            _viewModel.ConfirmItemOverwrite    = ConfirmItemOverwrite;
            _viewModel.ConfirmModUpgrade       = ConfirmModUpgrade;
            _viewModel.ParentForm              = this;

            _viewModel.DeleteModCommand.CanExecute   = false;
            _viewModel.ActivateModCommand.CanExecute = false;
            _viewModel.DisableModCommand.CanExecute  = false;
            _viewModel.TagModCommand.CanExecute      = false;

            new ToolStripItemCommandBinding<List<IMod>>(tsbActivate,   _viewModel.ActivateModCommand, GetSelectedMods);
            tsbDeactivate.ButtonClick -= tsbDeactivate_ButtonClick;
            tsbDeactivate.ButtonClick += tsbDeactivate_ButtonClick;
            ConfigureDeactivateDropDown();
            new ToolStripItemCommandBinding<IMod>      (tsbTagMod,     _viewModel.TagModCommand,      GetSelectedMod);
            new ToolStripItemCommandBinding<string>    (exportToTextFile,    _viewModel.ExportModListToFileCommand,      GetExportToFileArgs);
            new ToolStripItemCommandBinding            (exportToClipboard,   _viewModel.ExportModListToClipboardCommand);

            _viewModel.ExportModListToFileCommand.CanExecute      = _viewModel.CanExecuteExportCommands();
            _viewModel.ExportModListToClipboardCommand.CanExecute = _viewModel.CanExecuteExportCommands();

            tsbSkyrimDownloads.Visible = _viewModel.IsSkyrimSEGameMode;
            SetSkyrimDownloadModeFeedback();

            bool usesLoadOrder = _viewModel.ModManager.GameMode.UsesModLoadOrder;
            tsb_SaveModLoadOrder.Visible = usesLoadOrder;
            tsb_ModUpLoadOrder.Visible   = usesLoadOrder;
            tsb_ModDownLoadOrder.Visible = usesLoadOrder;

            LoadMods();
        }

        private void UnhookViewModel()
        {
            _viewModel.UpdatingCategory         -= VM_UpdatingCategory;
            _viewModel.UpdatingMods             -= VM_UpdatingMods;
            _viewModel.UpdatingCategories       -= VM_UpdatingCategories;
            _viewModel.TogglingAllWarning          -= VM_TogglingAllWarning;
            _viewModel.TogglingModUpdateChecks     -= VM_TogglingModUpdateChecks;
            _viewModel.ReadMeManagerSetup          -= VM_ReadMeManagerSetup;
            _viewModel.AddingMod                -= VM_AddingMod;
            _viewModel.DeletingMod              -= VM_DeletingMod;
            _viewModel.ActivatingMultipleMods   -= VM_ActivatingMultipleMods;
            _viewModel.ActivatingMod            -= VM_ActivatingMod;
            _viewModel.ReinstallingMod          -= VM_ReinstallingMod;
            _viewModel.DisablingMultipleMods    -= VM_DisablingMultipleMods;
            _viewModel.DeletingMultipleMods     -= VM_DeletingMultipleMods;
            _viewModel.DeactivatingMultipleMods -= VM_DeactivatingMultipleMods;
            _viewModel.AutomaticDownloading     -= VM_AutomaticDownloading;
            _viewModel.ChangingModActivation    -= VM_ChangingModActivation;
            _viewModel.TaggingMod               -= VM_TaggingMod;
            _viewModel.ExportFailed             -= VM_ExportFailed;
            _viewModel.ExportSucceeded          -= VM_ExportSucceeded;

            _viewModel.ManagedMods.CollectionChanged -= ManagedMods_CollectionChanged;
            _viewModel.ActiveMods.CollectionChanged  -= ActiveMods_CollectionChanged;

            foreach (IMod mod in _modList)
                mod.PropertyChanged -= Mod_PropertyChanged;
            _modList.Clear();
            ClearGridStateCaches();
            gridControl.RefreshDataSource();
        }

        private void LoadMods()
        {
            foreach (IMod mod in _modList)
                mod.PropertyChanged -= Mod_PropertyChanged;
            _modList.Clear();

            foreach (IMod mod in _viewModel.ManagedMods)
            {
                mod.PropertyChanged += Mod_PropertyChanged;
                _modList.Add(mod);
            }

            RebuildActivationStateCache();
            QueueMissingArchiveScan();
            gridControl.RefreshDataSource();
            bool restoredLayout = RestoreGridLayout();
            RestoreGridSort();
            RestoreGridCategoryView();
            if (restoredLayout)
                ScheduleModNameFill();
            else
                ScheduleColumnSizing();
            UpdateModCountLabel();
        }

        // ── Collection / property changed ────────────────────────────────────

        private void ManagedMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { Invoke(new Action(() => ManagedMods_CollectionChanged(sender, e))); return; }

            IMod focusedMod = SelectedMod;
            int focusedVisibleIndex = GetFocusedVisibleIndex();
            bool removedFocusedMod = focusedMod != null && e.Action == NotifyCollectionChangedAction.Remove && ContainsMod(e.OldItems, focusedMod);

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (e.NewItems != null)
                        foreach (IMod mod in e.NewItems)
                        {
                            mod.PropertyChanged += Mod_PropertyChanged;
                            _modList.Add(mod);
                        }
                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                        foreach (IMod mod in e.OldItems)
                        {
                            mod.PropertyChanged -= Mod_PropertyChanged;
                            _modList.Remove(mod);
                        }
                    break;
                case NotifyCollectionChangedAction.Reset:
                    foreach (IMod mod in _modList)
                        mod.PropertyChanged -= Mod_PropertyChanged;
                    _modList.Clear();
                    break;
            }
            RebuildActivationStateCache();
            QueueMissingArchiveScan();
            gridControl.RefreshDataSource();
            RestoreFocusAfterModListChange(focusedMod, focusedVisibleIndex, removedFocusedMod || e.Action == NotifyCollectionChangedAction.Reset);
            UpdateModCountLabel();
            UpdateModsCount?.Invoke(this, EventArgs.Empty);
        }

        private int GetFocusedVisibleIndex()
        {
            int rowHandle = gridView.FocusedRowHandle;
            return rowHandle >= 0 ? gridView.GetVisibleIndex(rowHandle) : -1;
        }

        private static bool ContainsMod(System.Collections.IList items, IMod mod)
        {
            if (items == null || mod == null) return false;
            foreach (object item in items)
            {
                if (ReferenceEquals(item, mod)) return true;
            }
            return false;
        }

        private void RestoreFocusAfterModListChange(IMod previousFocusedMod, int previousVisibleIndex, bool restoreByVisibleIndex)
        {
            if (gridView.RowCount <= 0) return;

            int rowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
            if (restoreByVisibleIndex)
            {
                int targetVisibleIndex = Math.Max(0, Math.Min(previousVisibleIndex, gridView.RowCount - 1));
                rowHandle = gridView.GetVisibleRowHandle(targetVisibleIndex);
            }
            else if (previousFocusedMod != null)
            {
                int sourceIndex = _modList.IndexOf(previousFocusedMod);
                if (sourceIndex >= 0)
                    rowHandle = gridView.GetRowHandle(sourceIndex);
            }

            if (rowHandle < 0) return;
            gridView.ClearSelection();
            gridView.FocusedRowHandle = rowHandle;
            gridView.SelectRow(rowHandle);
            gridView.MakeRowVisible(rowHandle, false);
        }

        private void ActiveMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired) { Invoke(new Action(() => ActiveMods_CollectionChanged(sender, e))); return; }
            RebuildActivationStateCache();
            RefreshActivationState();
        }

        private void Mod_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (InvokeRequired) { Invoke(new Action(() => Mod_PropertyChanged(sender, e))); return; }
            if (sender is IMod mod)
            {
                int srcIdx = _modList.IndexOf(mod);
                if (srcIdx >= 0)
                {
                    int viewHandle = gridView.GetRowHandle(srcIdx);
                    if (viewHandle != DevExpress.XtraGrid.GridControl.InvalidRowHandle)
                        gridView.InvalidateRow(viewHandle);
                }
            }
        }

        // ── Grid setup ───────────────────────────────────────────────────────

        private void InitializeToolbarIcons()
        {
            ConfigureToolbarIcon(tsbAddMod, "toolbar_add_mod.svg");
            ConfigureToolbarIcon(tsbActivate, "toolbar_install_enable.svg");
            ConfigureToolbarIcon(tsbDeactivate, "toolbar_disable.svg");
            ConfigureToolbarIcon(tsbTagMod, "toolbar_tag.svg");
            ConfigureToolbarIcon(tsbModOnlineChecks, "toolbar_updates.svg");
            ConfigureToolbarIcon(tsbToggleEndorse, "toolbar_endorse.svg");
            ConfigureToolbarIcon(tsbResetCategories, "toolbar_categories.svg");
            ConfigureToolbarIcon(tsbSwitchView, "toolbar_view.svg");
            ConfigureToolbarIcon(tsbExportModList, "toolbar_export.svg");
            ConfigureToolbarIcon(tsbShowUpdatesOnly, "toolbar_updates_only.svg");
            ConfigureToolbarIcon(tsbSkyrimDownloads, "toolbar_skyrim.svg");
        }

        private static void ConfigureToolbarIcon(ToolStripItem item, string resourceName)
        {
            Image image = LoadSvgIcon(resourceName, 16);
            if (image == null) return;

            item.Image = image;
            item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            item.ImageScaling = ToolStripItemImageScaling.None;
            item.TextImageRelation = TextImageRelation.ImageBeforeText;
        }
        private void ApplyToolbarActionLabels()
        {
            tsbDeactivate.Text = "Disable Mod";
            tsbDeactivate.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            tsbDeactivate.TextImageRelation = TextImageRelation.ImageBeforeText;
            tsbTagMod.Text = "Get Mod Info";
            tsbTagMod.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            tsbTagMod.TextImageRelation = TextImageRelation.ImageBeforeText;
        }

        private static Image LoadSvgIcon(string resourceName, int size)
        {
            var assembly = typeof(ModManagerDXControl).Assembly;
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

        private void InitializeGridDisplayOptions()
        {
            _displayOptionsButton = new ToolStripDropDownButton
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = "Display Options",
                ToolTipText = "Grid display options"
            };

            _toggleColouredCategoriesMenuItem = new ToolStripMenuItem("Toggle Coloured Categories")
            {
                CheckOnClick = true,
                Checked = _showColouredCategories
            };
            _toggleColouredCategoriesMenuItem.Click += (s, e) => SetColouredCategoriesVisible(_toggleColouredCategoriesMenuItem.Checked, true);

            _toggleRowHighlightsMenuItem = new ToolStripMenuItem("Toggle Row Highlights")
            {
                CheckOnClick = true,
                Checked = _showRowHighlights
            };
            _toggleRowHighlightsMenuItem.Click += (s, e) => SetRowHighlightsVisible(_toggleRowHighlightsMenuItem.Checked, true);

            _toggleActiveModsBoldMenuItem = new ToolStripMenuItem("Show Active Mods in Bold")
            {
                CheckOnClick = true,
                Checked = _showActiveModsInBold
            };
            _toggleActiveModsBoldMenuItem.Click += (s, e) => SetActiveModsBold(_toggleActiveModsBoldMenuItem.Checked, true);

            _displayOptionsButton.DropDownItems.Add(_toggleColouredCategoriesMenuItem);
            _displayOptionsButton.DropDownItems.Add(_toggleRowHighlightsMenuItem);
            _displayOptionsButton.DropDownItems.Add(_toggleActiveModsBoldMenuItem);
            toolStrip1.Items.Add(_displayOptionsButton);
        }

        private void InitializeGridFontSelector()
        {
            _displayButton = new ToolStripDropDownButton
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Text = "Aa Display",
                ToolTipText = "Grid display settings"
            };

            var panel = new TableLayoutPanel
            {
                AutoSize = false,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(8),
                Size = new Size(260, 126)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58f));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));

            _gridFontCombo = CreateDisplayCombo(GridFontChoices, 162);
            _gridFontSizeCombo = CreateDisplayCombo(GridFontSizeChoices, 84);
            _gridDensityCombo = CreateDisplayCombo(GridDensityChoices, 132);

            _gridFontCombo.SelectedIndexChanged += GridDisplayControl_SelectedIndexChanged;
            _gridFontSizeCombo.SelectedIndexChanged += GridDisplayControl_SelectedIndexChanged;
            _gridDensityCombo.SelectedIndexChanged += GridDisplayControl_SelectedIndexChanged;

            AddDisplayRow(panel, 0, "Font:", _gridFontCombo);
            AddDisplayRow(panel, 1, "Size:", _gridFontSizeCombo);
            AddDisplayRow(panel, 2, "Density:", _gridDensityCombo);

            var resetButton = new Button
            {
                Text = "Reset",
                AutoSize = false,
                Height = 24,
                Dock = DockStyle.Left,
                Width = 76
            };
            resetButton.Click += (s, e) => ResetGridDisplaySettings();
            panel.Controls.Add(resetButton, 1, 3);

            _displayButton.DropDownItems.Add(new ToolStripControlHost(panel)
            {
                AutoSize = false,
                Size = panel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            });

            toolStrip1.Items.Add(_displayButton);
            SelectGridDisplay(DefaultGridFontFamily, DefaultGridFontSizePt, DefaultGridDensity, false);
        }

        private static ComboBox CreateDisplayCombo(IEnumerable<string> items, int width)
        {
            var combo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = width,
                Anchor = AnchorStyles.Left | AnchorStyles.Right
            };

            foreach (string item in items)
                combo.Items.Add(item);

            return combo;
        }

        private static void AddDisplayRow(TableLayoutPanel panel, int row, string labelText, Control control)
        {
            var label = new Label
            {
                Text = labelText,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill
            };

            control.Dock = DockStyle.Fill;
            panel.Controls.Add(label, 0, row);
            panel.Controls.Add(control, 1, row);
        }

        private void GridDisplayControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_updatingGridDisplayControls) return;
            SelectGridDisplay(_gridFontCombo.SelectedItem as string, ParseGridFontSize(_gridFontSizeCombo.SelectedItem as string), _gridDensityCombo.SelectedItem as string, true);
        }

        private void RestoreGridDisplayOptions()
        {
            SetColouredCategoriesVisible(ReadGridDisplayOption(GridColouredCategoriesKey, true), false);
            SetRowHighlightsVisible(ReadGridDisplayOption(GridRowHighlightsKey, true), false);
            SetActiveModsBold(ReadGridDisplayOption(GridActiveModsBoldKey, false), false);
        }

        private bool ReadGridDisplayOption(string key, bool defaultValue)
        {
            if (_viewModel?.Settings?.DockPanelLayouts.ContainsKey(key) != true)
                return defaultValue;

            bool value;
            return bool.TryParse(_viewModel.Settings.DockPanelLayouts[key], out value) ? value : defaultValue;
        }

        private void SetColouredCategoriesVisible(bool visible, bool save)
        {
            _showColouredCategories = visible;
            if (_toggleColouredCategoriesMenuItem != null)
                _toggleColouredCategoriesMenuItem.Checked = visible;

            RefreshGridDisplayStyles();
            SaveGridDisplayOption(GridColouredCategoriesKey, visible, save);
        }

        private void SetRowHighlightsVisible(bool visible, bool save)
        {
            _showRowHighlights = visible;
            if (_toggleRowHighlightsMenuItem != null)
                _toggleRowHighlightsMenuItem.Checked = visible;

            RefreshGridDisplayStyles();
            SaveGridDisplayOption(GridRowHighlightsKey, visible, save);
        }

        private void SetActiveModsBold(bool visible, bool save)
        {
            _showActiveModsInBold = visible;
            if (_toggleActiveModsBoldMenuItem != null)
                _toggleActiveModsBoldMenuItem.Checked = visible;

            RefreshGridDisplayStyles();
            SaveGridDisplayOption(GridActiveModsBoldKey, visible, save);
        }

        private void RefreshGridDisplayStyles()
        {
            if (gridView == null || gridControl == null || gridControl.IsDisposed)
                return;

            gridView.RefreshData();
            gridView.InvalidateRows();
            gridView.Invalidate();
            gridControl.Refresh();
        }

        private void SaveGridDisplayOption(string key, bool value, bool save)
        {
            if (!save || _viewModel?.Settings == null)
                return;

            _viewModel.Settings.DockPanelLayouts[key] = value.ToString();
            _viewModel.Settings.Save();
        }

        private void RestoreGridFont()
        {
            string fontName = DefaultGridFontFamily;
            float fontSize = DefaultGridFontSizePt;
            string density = DefaultGridDensity;

            if (_viewModel?.Settings?.DockPanelLayouts.ContainsKey(GridFontKey) == true)
                fontName = _viewModel.Settings.DockPanelLayouts[GridFontKey];
            if (_viewModel?.Settings?.DockPanelLayouts.ContainsKey(GridFontSizeKey) == true)
                fontSize = ParseGridFontSize(_viewModel.Settings.DockPanelLayouts[GridFontSizeKey]);
            if (_viewModel?.Settings?.DockPanelLayouts.ContainsKey(GridDensityKey) == true)
                density = ResolveGridDensity(_viewModel.Settings.DockPanelLayouts[GridDensityKey]);

            SelectGridDisplay(fontName, fontSize, density, false);
        }

        private void ResetGridDisplaySettings()
        {
            SelectGridDisplay(DefaultGridFontFamily, DefaultGridFontSizePt, DefaultGridDensity, true);
        }

        private void SelectGridDisplay(string fontName, float fontSize, string density, bool save)
        {
            string resolvedFontName = ResolveGridFontFamily(fontName);
            float resolvedFontSize = ResolveGridFontSize(fontSize);
            string resolvedDensity = ResolveGridDensity(density);

            _gridFontFamilyName = resolvedFontName;
            _gridFontSizePt = resolvedFontSize;
            _gridDensity = resolvedDensity;

            UpdateGridDisplayControls();
            ApplyGridFont(resolvedFontName);

            if (save && _viewModel?.Settings != null)
            {
                _viewModel.Settings.DockPanelLayouts[GridFontKey] = resolvedFontName;
                _viewModel.Settings.DockPanelLayouts[GridFontSizeKey] = FormatGridFontSize(resolvedFontSize);
                _viewModel.Settings.DockPanelLayouts[GridDensityKey] = resolvedDensity;
                _viewModel.Settings.Save();
            }
        }

        private void UpdateGridDisplayControls()
        {
            if (_gridFontCombo == null || _gridFontSizeCombo == null || _gridDensityCombo == null)
                return;

            _updatingGridDisplayControls = true;
            try
            {
                if (!_gridFontCombo.Items.Contains(_gridFontFamilyName))
                    _gridFontCombo.Items.Add(_gridFontFamilyName);
                _gridFontCombo.SelectedItem = _gridFontFamilyName;
                _gridFontSizeCombo.SelectedItem = FormatGridFontSize(_gridFontSizePt);
                _gridDensityCombo.SelectedItem = _gridDensity;
            }
            finally
            {
                _updatingGridDisplayControls = false;
            }
        }

        private static string FormatGridFontSize(float fontSize)
        {
            return ((int)Math.Round(ResolveGridFontSize(fontSize))).ToString() + " pt";
        }

        private static float ParseGridFontSize(string fontSizeText)
        {
            if (string.IsNullOrWhiteSpace(fontSizeText))
                return DefaultGridFontSizePt;

            string digits = new string(fontSizeText.Where(char.IsDigit).ToArray());
            int fontSize;
            return int.TryParse(digits, out fontSize) ? ResolveGridFontSize(fontSize) : DefaultGridFontSizePt;
        }

        private static float ResolveGridFontSize(float fontSize)
        {
            if (fontSize < 8f) return 8f;
            if (fontSize > 12f) return 12f;
            return (float)Math.Round(fontSize);
        }

        private static string ResolveGridDensity(string density)
        {
            foreach (string choice in GridDensityChoices)
            {
                if (choice.Equals(density, StringComparison.OrdinalIgnoreCase))
                    return choice;
            }

            return DefaultGridDensity;
        }

        private static int GetGridRowHeight(string density, float fontSize)
        {
            int baseHeight = (int)Math.Round(fontSize * 2.55f);
            if (string.Equals(density, "Comfortable", StringComparison.OrdinalIgnoreCase)) return baseHeight + 4;
            if (string.Equals(density, "Spacious", StringComparison.OrdinalIgnoreCase)) return baseHeight + 8;
            return baseHeight;
        }

        private static int GetGridColumnHeaderHeight(string density, float fontSize)
        {
            return GetGridRowHeight(density, fontSize) + 2;
        }

        private static float GetSecondaryGridFontSize(float fontSize)
        {
            return Math.Max(8f, fontSize - 0.75f);
        }

        private static float GetBadgeGridFontSize(float fontSize)
        {
            return Math.Max(7.5f, fontSize - 1f);
        }

        private static string ResolveGridFontFamily(string fontName)
        {
            if (string.IsNullOrWhiteSpace(fontName))
                return DefaultGridFontFamily;

            if (fontName.Equals("Aptos", StringComparison.OrdinalIgnoreCase))
                fontName = "Corbel";

            foreach (FontFamily family in FontFamily.Families)
            {
                if (family.Name.Equals(fontName, StringComparison.OrdinalIgnoreCase))
                    return family.Name;
            }

            return DefaultGridFontFamily;
        }

        private void ApplyGridFont(string fontName)
        {
            if (gridControl == null || gridView == null) return;
            _gridFontFamilyName = ResolveGridFontFamily(fontName);
            var rowFont = new Font(_gridFontFamilyName, _gridFontSizePt, FontStyle.Regular, GraphicsUnit.Point);
            var headerFont = new Font(_gridFontFamilyName, GetSecondaryGridFontSize(_gridFontSizePt), FontStyle.Regular, GraphicsUnit.Point);

            gridControl.Font = rowFont;
            gridView.RowHeight = GetGridRowHeight(_gridDensity, _gridFontSizePt);
            gridView.ColumnPanelRowHeight = GetGridColumnHeaderHeight(_gridDensity, _gridFontSizePt);
            gridView.Appearance.Row.Font = rowFont;
            gridView.Appearance.EvenRow.Font = rowFont;
            gridView.Appearance.OddRow.Font = rowFont;
            gridView.Appearance.FocusedRow.Font = rowFont;
            gridView.Appearance.SelectedRow.Font = rowFont;
            gridView.Appearance.HideSelectionRow.Font = rowFont;
            gridView.Appearance.HeaderPanel.Font = headerFont;
            gridView.Appearance.FilterPanel.Font = rowFont;
            gridView.Appearance.GroupRow.Font = rowFont;
            ApplyToolbarFont(rowFont);
            gridView.LayoutChanged();
            gridView.InvalidateRows();
            ScheduleModNameFill();
        }

        private void ApplyToolbarFont(Font font)
        {
            if (toolStrip1 == null || font == null) return;

            Font toolbarFont = new Font(font.FontFamily, font.Size, FontStyle.Regular, GraphicsUnit.Point);
            ApplyToolStripFont(toolStrip1, toolbarFont);
        }

        private static void ApplyToolStripFont(ToolStrip toolStrip, Font font)
        {
            if (toolStrip == null || font == null) return;

            toolStrip.Font = font;
            foreach (ToolStripItem item in toolStrip.Items)
                ApplyToolStripItemFont(item, font);
        }

        private static void ApplyToolStripItemFont(ToolStripItem item, Font font)
        {
            if (item == null || font == null) return;

            item.Font = font;
            ToolStripDropDownItem dropDownItem = item as ToolStripDropDownItem;
            if (dropDownItem == null) return;

            dropDownItem.DropDown.Font = font;
            foreach (ToolStripItem child in dropDownItem.DropDownItems)
                ApplyToolStripItemFont(child, font);
        }

        private string GetSkyrimDownloadModeLabel()
        {
            if (_viewModel == null) return string.Empty;
            string mode = _viewModel.SkyrimSEDownloadOverride;
            if (string.Equals(mode, "SkyrimSE", StringComparison.OrdinalIgnoreCase)) return "Steam";
            if (string.Equals(mode, "SkyrimGOG", StringComparison.OrdinalIgnoreCase)) return "GOG";
            return _viewModel.SkyrimSEDownloadModeDescriptor;
        }

        private void SetupGrid()
        {
            // Use unbound mode so we supply cell values via CustomUnboundColumnData.
            // This avoids the issue where BindingList<IMod> (interface type) prevents
            // DevExpress from resolving property descriptors on the concrete mod type.
            gridView.OptionsView.ShowGroupPanel          = false;
            gridView.OptionsView.ShowIndicator           = false;
            gridView.OptionsView.ShowVerticalLines       = DefaultBoolean.False;
            gridView.OptionsView.EnableAppearanceEvenRow = true;
            gridView.OptionsView.EnableAppearanceOddRow  = true;
            gridView.Appearance.Empty.BackColor    = Color.FromArgb(245, 245, 248);
            gridView.Appearance.Row.BackColor      = Color.FromArgb(248, 248, 251);
            gridView.Appearance.EvenRow.BackColor  = Color.FromArgb(242, 242, 246);
            gridView.Appearance.OddRow.BackColor   = Color.FromArgb(248, 248, 251);
            // Compact uppercase header: small font, muted foreground
            gridView.Appearance.HeaderPanel.Font      = new Font(_gridFontFamilyName, GetSecondaryGridFontSize(_gridFontSizePt), FontStyle.Regular, GraphicsUnit.Point);
            gridView.Appearance.HeaderPanel.ForeColor = Color.FromArgb(100, 100, 100);
            gridView.OptionsBehavior.Editable            = false;
            gridView.OptionsBehavior.ReadOnly            = true;
            gridView.OptionsSelection.MultiSelect        = true;
            gridView.OptionsSelection.MultiSelectMode    = GridMultiSelectMode.RowSelect;
            gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
            gridView.OptionsSelection.EnableAppearanceFocusedRow  = false;
            gridView.OptionsCustomization.AllowColumnMoving   = true;
            gridView.OptionsCustomization.AllowColumnResizing = true;
            gridView.OptionsCustomization.AllowSort           = true;
            gridView.OptionsFind.AlwaysVisible                = false;
            gridView.OptionsView.BestFitMaxRowCount           = 50;
            gridView.OptionsView.ColumnAutoWidth             = false;
            gridView.OptionsView.ShowAutoFilterRow           = true;

            gridControl.DataSource = _modList;

            gridView.Columns.Clear();
            BuildColumns();
            ApplyAutoFilterDefaults();
            ApplyDateSortDefaults();

            gridView.CustomUnboundColumnData += GridView_CustomUnboundColumnData;
            gridView.RowCellStyle            += GridView_RowCellStyle;
            gridView.RowCellClick            += GridView_RowCellClick;
            gridView.DoubleClick             += GridView_DoubleClick;
            gridView.KeyDown                 += GridView_KeyDown;
            gridView.FocusedRowChanged       += (s, e) => SetCommandExecutableStatus();
            gridView.SelectionChanged        += (s, e) => SetCommandExecutableStatus();
            gridView.CustomDrawCell          += GridView_CustomDrawCell;
            gridView.CustomDrawColumnHeader  += GridView_CustomDrawColumnHeader;
            gridView.CustomColumnSort       += GridView_CustomColumnSort;
            gridView.GroupRowExpanded       += (s, e) => SaveGridLayout();
            gridView.GroupRowCollapsed      += (s, e) => SaveGridLayout();
            gridControl.SizeChanged          += (s, e) => ScheduleModNameFill();
            gridView.ColumnWidthChanged      += (s, e) => { if (_restoringGridLayout) return; if (e.Column?.FieldName != ColModName) ScheduleModNameFill(); SaveGridLayout(); };
            gridView.EndSorting              += (s, e) => SaveGridLayout();
            gridControl.MouseMove            += GridControl_MouseMove;
            gridControl.MouseLeave           += GridControl_MouseLeave;
            gridControl.MouseDown            += GridControl_MouseDown;
        }

        private void BuildColumns()
        {
            AddCol(ColModStatus,    "Status",        58, HorzAlignment.Center,  true);
            AddCol(ColModName,      "MOD NAME",       220, HorzAlignment.Default, true);
            AddCol(ColVersion,      "VERSION",         70, HorzAlignment.Center,  false);
            AddCol(ColLastKnown,    "LATEST",          70, HorzAlignment.Center,  false);
            AddCol(ColAuthor,       "AUTHOR",         128, HorzAlignment.Default, false);
            AddCol(ColCategory,     "CATEGORY",        90, HorzAlignment.Default, false);
            AddCol(ColInstallDate,  "INSTALL DATE",    90, HorzAlignment.Center,  false);
            AddCol(ColDownloadDate, "DOWNLOAD DATE",   90, HorzAlignment.Center,  false);
            AddCol(ColDownloadId,   "DOWNLOAD ID",     80, HorzAlignment.Center,  false);

            // Endorsed uses an image — attach a picture editor so DevExpress renders Image values
            var endorsedCol = AddCol(ColEndorsed, "ENDORSED", 70, HorzAlignment.Center, false);
            var picRepo = new RepositoryItemPictureEdit
            {
                ShowMenu = false,
                SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom,
                NullText = "",
            };
            endorsedCol.ColumnEdit = picRepo;
            gridControl.RepositoryItems.Add(picRepo);
        }

        private GridColumn AddCol(string field, string caption, int width, HorzAlignment align, bool pin)
        {
            var col = new GridColumn
            {
                FieldName   = field,
                Caption     = caption,
                Width       = width,
                Fixed       = pin ? FixedStyle.Left : FixedStyle.None,
                UnboundType = field == ColEndorsed ? DevExpress.Data.UnboundColumnType.Object : DevExpress.Data.UnboundColumnType.String,
                OptionsColumn = { AllowEdit = false, AllowSort = DefaultBoolean.True, ReadOnly = true, FixedWidth = field != ColModName },
                AppearanceHeader = { TextOptions = { HAlignment = align } },
                AppearanceCell   = { TextOptions = { HAlignment = align } },
            };
            if (field == ColInstallDate || field == ColDownloadDate)
                col.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;

            col.MinWidth = field == ColModName ? 100 : Math.Min(width, 70);
            ApplyAutoFilterDefaults(col);
            gridView.Columns.Add(col);
            col.Visible      = true;
            col.VisibleIndex = gridView.Columns.Count - 1;
            return col;
        }

        private void ApplyAutoFilterDefaults()
        {
            foreach (GridColumn col in gridView.Columns)
                ApplyAutoFilterDefaults(col);
        }

        private void ApplyDateSortDefaults()
        {
            GridColumn installDateColumn = gridView.Columns[ColInstallDate];
            if (installDateColumn != null)
                installDateColumn.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;

            GridColumn downloadDateColumn = gridView.Columns[ColDownloadDate];
            if (downloadDateColumn != null)
                downloadDateColumn.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;
        }

        private void ApplyAutoFilterDefaults(GridColumn col)
        {
            if (col == null || col.FieldName == ColEndorsed) return;
            col.OptionsFilter.AutoFilterCondition = AutoFilterCondition.Contains;
            col.OptionsFilter.AllowFilterModeChanging = DefaultBoolean.True;
        }

        private void GridView_CustomUnboundColumnData(object sender, DevExpress.XtraGrid.Views.Base.CustomColumnDataEventArgs e)
        {
            if (!e.IsGetData) return;
            int idx = e.ListSourceRowIndex;
            if (idx < 0 || idx >= _modList.Count) return;
            IMod mod = _modList[idx];
            switch (e.Column.FieldName)
            {
                case ColModStatus:    e.Value = GetModStatusText(mod);    break;
                case ColModName:      e.Value = mod.ModName;              break;
                case ColVersion:      e.Value = mod.HumanReadableVersion; break;
                case ColLastKnown:    e.Value = mod.LastKnownVersion;     break;
                case ColAuthor:       e.Value = mod.Author;               break;
                case ColInstallDate:  e.Value = mod.InstallDate;          break;
                case ColDownloadDate: e.Value = mod.DownloadDate;         break;
                case ColDownloadId:   e.Value = mod.DownloadId;           break;
                case ColCategory:
                    if (_viewModel?.CategoryManager != null)
                    {
                        IModCategory cat = _viewModel.CategoryManager.FindCategory(mod.CustomCategoryId > 0 ? mod.CustomCategoryId : mod.CategoryId);
                        e.Value = cat != null ? cat.CategoryName : null;
                    }
                    else
                    {
                        e.Value = mod.CategoryId;
                    }
                    break;
                case ColEndorsed:
                    if (mod.IsEndorsed == true)
                        e.Value = new System.Drawing.Bitmap(Properties.Resources.thumb_up,  16, 16);
                    else if (mod.IsEndorsed == false)
                        e.Value = new System.Drawing.Bitmap(Properties.Resources.thumb_no, 16, 16);
                    else
                        e.Value = new System.Drawing.Bitmap(16, 16); // transparent — renders as empty cell
                    break;
            }
        }

        // ── Grid event handlers ──────────────────────────────────────────────

        private void ClearGridStateCaches()
        {
            _activeModFileNames.Clear();
            _installedMods.Clear();
            _modVisualStatusCache.Clear();
            lock (_missingArchiveLock)
                _missingArchiveByFileName.Clear();
        }

        private void RebuildActivationStateCache()
        {
            _activeModFileNames.Clear();
            _installedMods.Clear();
            _modVisualStatusCache.Clear();

            if (_viewModel == null)
                return;

            foreach (string fileName in _viewModel.VirtualModActivator.ActiveModList)
                if (!string.IsNullOrWhiteSpace(fileName))
                    _activeModFileNames.Add(fileName);

            foreach (IMod mod in _viewModel.ActiveMods)
                if (mod != null)
                    _installedMods.Add(mod);
        }

        private bool IsModActive(IMod mod)
        {
            return GetModVisualStatus(mod) == ModVisualStatus.InstalledActive;
        }

        private bool IsModInstalled(IMod mod)
        {
            return mod != null && _installedMods.Contains(mod);
        }

        private ModVisualStatus GetModVisualStatus(IMod mod)
        {
            if (mod == null)
                return ModVisualStatus.Uninstalled;

            ModVisualStatus status;
            if (_modVisualStatusCache.TryGetValue(mod, out status))
                return status;

            bool installed = IsModInstalled(mod);
            bool linked = installed && !string.IsNullOrEmpty(mod.Filename) && _activeModFileNames.Contains(Path.GetFileName(mod.Filename));

            status = linked ? ModVisualStatus.InstalledActive : installed ? ModVisualStatus.InstalledUnlinked : ModVisualStatus.Uninstalled;
            _modVisualStatusCache[mod] = status;
            return status;
        }

        private string GetModStatusText(IMod mod)
        {
            switch (GetModVisualStatus(mod))
            {
                case ModVisualStatus.InstalledActive:
                    return "Installed/Active";
                case ModVisualStatus.InstalledUnlinked:
                    return "Installed/Unlinked";
                default:
                    return "Uninstalled";
            }
        }

        private Image GetModStatusIcon(ModVisualStatus status)
        {
            if (status == ModVisualStatus.InstalledActive)
                return _modInstalledActiveIcon ?? (_modInstalledActiveIcon = LoadSvgIcon("mod-installed-active.svg", ModStatusIconSize));
            if (status == ModVisualStatus.InstalledUnlinked)
                return _modInstalledDisabledIcon ?? (_modInstalledDisabledIcon = LoadSvgIcon("mod-installed-disabled.svg", ModStatusIconSize));
            return null;
        }

        private void QueueMissingArchiveScan()
        {
            if (_missingArchiveScanQueued)
                return;

            var snapshot = _modList
                .Where(x => x != null && !string.IsNullOrEmpty(x.Filename))
                .Select(x => x.Filename)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (snapshot.Count == 0)
                return;

            _missingArchiveScanQueued = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (string fileName in snapshot)
                    results[fileName] = !File.Exists(fileName);

                if (IsDisposed || !IsHandleCreated)
                {
                    _missingArchiveScanQueued = false;
                    return;
                }

                try
                {
                    BeginInvoke((MethodInvoker)(() =>
                    {
                        lock (_missingArchiveLock)
                        {
                            foreach (var item in results)
                                _missingArchiveByFileName[item.Key] = item.Value;
                        }
                        _missingArchiveScanQueued = false;
                        gridView.InvalidateRows();
                    }));
                }
                catch (InvalidOperationException)
                {
                    _missingArchiveScanQueued = false;
                }
            });
        }

        private bool IsModArchiveMissing(IMod mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.Filename)) return false;
            lock (_missingArchiveLock)
                return _missingArchiveByFileName.TryGetValue(mod.Filename, out bool missing) && missing;
        }
        private static bool IsModArchiveMissingOnDisk(IMod mod)
        {
            return mod != null && !string.IsNullOrEmpty(mod.Filename) && !File.Exists(mod.Filename);
        }
        private void RefreshActivationState()
        {
            RebuildActivationStateCache();
            gridControl.RefreshDataSource();
            gridView.InvalidateRows();
            SetCommandExecutableStatus();
            UpdateModsCount?.Invoke(this, EventArgs.Empty);
        }

        private void GridView_CustomColumnSort(object sender, DevExpress.XtraGrid.Views.Base.CustomColumnSortEventArgs e)
        {
            if (e.Column == null || (e.Column.FieldName != ColInstallDate && e.Column.FieldName != ColDownloadDate))
                return;

            DateTime left;
            DateTime right;
            bool hasLeft = TryParseGridDate(Convert.ToString(e.Value1, CultureInfo.CurrentCulture), out left);
            bool hasRight = TryParseGridDate(Convert.ToString(e.Value2, CultureInfo.CurrentCulture), out right);

            if (hasLeft && hasRight)
                e.Result = left.CompareTo(right);
            else if (hasLeft)
                e.Result = 1;
            else if (hasRight)
                e.Result = -1;
            else
                e.Result = StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(e.Value1, CultureInfo.CurrentCulture), Convert.ToString(e.Value2, CultureInfo.CurrentCulture));

            e.Handled = true;
        }

        private static bool TryParseGridDate(string value, out DateTime result)
        {
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out result))
                return true;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result))
                return true;

            string[] formats =
            {
                "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yyyy HH:mm", "d.M.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "d.M.yyyy HH:mm:ss",
                "dd/MM/yyyy", "d/M/yyyy", "dd\\MM\\yyyy", "d\\M\\yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss"
            };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result);
        }
        private void GridView_RowCellStyle(object sender, RowCellStyleEventArgs e)
        {
            if (_viewModel == null) return;
            if (e.RowHandle < 0) return;
            int src = gridView.GetDataSourceRowIndex(e.RowHandle);
            if (src < 0 || src >= _modList.Count) return;
            IMod mod = _modList[src];

            ModVisualStatus status = GetModVisualStatus(mod);
            bool isActive = status == ModVisualStatus.InstalledActive;
            bool isInstalled = status != ModVisualStatus.Uninstalled;
            bool isSelected  = gridView.IsRowSelected(e.RowHandle)
                            || e.RowHandle == gridView.FocusedRowHandle;

            if (_showRowHighlights && isActive)
            {
                e.Appearance.BackColor = isSelected
                    ? Color.FromArgb(218, 240, 218)
                    : Color.FromArgb(249, 254, 249);
                e.Appearance.ForeColor = Color.Black;
            }
            else if (_showRowHighlights && isInstalled)
            {
                e.Appearance.BackColor = isSelected
                    ? Color.FromArgb(250, 230, 200)
                    : Color.FromArgb(255, 251, 244);
                e.Appearance.ForeColor = Color.Black;
            }
            else if (!_showRowHighlights && isInstalled && !isSelected)
            {
                e.Appearance.BackColor = GetDefaultRowBackColor(e.RowHandle);
                e.Appearance.ForeColor = Color.Black;
            }
            else if (isSelected)
            {
                // Not active but selected/focused - pin an explicit colour so the
                // DevExpress HideSelectionRow dark-grey never shows.
                e.Appearance.BackColor = Color.FromArgb(184, 207, 229);  // standard Windows selection blue
                e.Appearance.ForeColor = Color.Black;
            }

            if (_showActiveModsInBold && isActive)
                e.Appearance.Font = new Font(_gridFontFamilyName, _gridFontSizePt, FontStyle.Bold, GraphicsUnit.Point);

            // Latest: red (underline) when outdated, blue (underline) when current
            if (e.Column.FieldName == ColLastKnown && !string.IsNullOrEmpty(mod.LastKnownVersion))
            {
                bool outdated = IsVersionOutdated(mod.HumanReadableVersion, mod.LastKnownVersion);
                e.Appearance.ForeColor = outdated
                    ? Color.FromArgb(200, 40, 40)
                    : Color.FromArgb(37, 99, 235);
                e.Appearance.Font = new Font(e.Appearance.GetFont(), e.Appearance.GetFont().Style | FontStyle.Underline);
            }

            // Secondary style: muted colour + compact font for non-priority info columns
            if (!isSelected &&
                (e.Column.FieldName == ColInstallDate  ||
                 e.Column.FieldName == ColDownloadDate ||
                 e.Column.FieldName == ColDownloadId))
            {
                e.Appearance.ForeColor = Color.FromArgb(90, 90, 90);
                FontStyle dateStyle = _showActiveModsInBold && isActive ? FontStyle.Bold : FontStyle.Regular;
                e.Appearance.Font = new Font(_gridFontFamilyName, GetSecondaryGridFontSize(_gridFontSizePt), dateStyle, GraphicsUnit.Point);
            }
        }

        private Color GetDefaultRowBackColor(int rowHandle)
        {
            int visibleIndex = gridView.GetVisibleIndex(rowHandle);
            return visibleIndex >= 0 && visibleIndex % 2 == 0
                ? Color.FromArgb(242, 242, 246)
                : Color.FromArgb(248, 248, 251);
        }

        private void DrawModStatusCell(DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            int src = gridView.GetDataSourceRowIndex(e.RowHandle);
            if (src < 0 || src >= _modList.Count)
                return;

            ModVisualStatus status = GetModVisualStatus(_modList[src]);
            Image icon = GetModStatusIcon(status);
            string displayText = e.DisplayText;
            e.DisplayText = string.Empty;
            e.DefaultDraw();
            e.DisplayText = displayText;

            if (icon != null)
            {
                int x = e.Bounds.Left + (e.Bounds.Width - icon.Width) / 2;
                int y = e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2;
                e.Graphics.DrawImage(icon, x, y, icon.Width, icon.Height);
            }

            e.Handled = true;
        }
        private void GridView_RowCellClick(object sender, RowCellClickEventArgs e)
        {
            if (e.Column.FieldName != ColLastKnown) return;
            int src = gridView.GetDataSourceRowIndex(e.RowHandle);
            if (src < 0 || src >= _modList.Count) return;
            Uri url = _modList[src].Website;
            if (url != null)
            {
                try { System.Diagnostics.Process.Start(url.ToString()); }
                catch { /* ignore launch errors */ }
            }
        }

        private void GridView_CustomDrawCell(object sender, DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            bool handledByModName = false;
            if (e.Column.FieldName == ColModStatus && e.RowHandle >= 0)
            {
                DrawModStatusCell(e);
                return;
            }

            if (e.Column.FieldName == ColModName)
            {
                DrawModNameCell(e);
                handledByModName = e.Handled;
            }

            if (!handledByModName && e.Column.FieldName == ColCategory)
            {
                DrawCategoryBadge(e);
                return;
            }

            bool drawsLatestWarning = false;
            if (!handledByModName && e.Column.FieldName == ColLastKnown && e.RowHandle >= 0)
            {
                int src = gridView.GetDataSourceRowIndex(e.RowHandle);
                if (src >= 0 && src < _modList.Count)
                    drawsLatestWarning = IsVersionOutdated(_modList[src].HumanReadableVersion, _modList[src].LastKnownVersion);
            }

            if (handledByModName)
            {
                return;
            }

            if (!drawsLatestWarning)
            {
                return;
            }

            // Draw default cell content first (background, text, hyperlink colour from RowCellStyle)
            e.DefaultDraw();

            // Overlay flat warning icon at the right edge so the centred text is unaffected
            Bitmap icon = GetWarningIcon();
            if (e.Bounds.Width >= icon.Width + 4)
            {
                int x = e.Bounds.Right - icon.Width - 2;
                int y = e.Bounds.Top  + (e.Bounds.Height - icon.Height) / 2;
                e.Graphics.DrawImage(icon, x, y, icon.Width, icon.Height);
            }
            e.Handled = true;
        }

        private bool DrawAutoFilterMatchHighlight(DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.RowHandle < 0 || e.Column == null || e.Column.FieldName == ColEndorsed)
                return false;

            string filterText = GetAutoFilterText(e.Column);
            if (string.IsNullOrWhiteSpace(filterText))
                return false;

            string displayText = gridView.GetRowCellDisplayText(e.RowHandle, e.Column);
            if (string.IsNullOrEmpty(displayText))
                return false;

            int matchIndex = displayText.IndexOf(filterText, StringComparison.CurrentCultureIgnoreCase);
            if (matchIndex < 0)
                return false;

            if (!e.Handled)
                e.DefaultDraw();

            Rectangle textBounds = GetCellTextBounds(e, displayText);
            using (var brush = new SolidBrush(Color.FromArgb(120, 255, 230, 120)))
            {
                while (matchIndex >= 0)
                {
                    Rectangle matchBounds = GetTextRangeBounds(e.Graphics, e.Appearance.GetFont(), displayText, matchIndex, filterText.Length, textBounds);
                    e.Graphics.FillRectangle(brush, matchBounds);
                    matchIndex = displayText.IndexOf(filterText, matchIndex + filterText.Length, StringComparison.CurrentCultureIgnoreCase);
                }
            }

            e.Handled = true;
            return true;
        }

        private string GetAutoFilterText(GridColumn column)
        {
            object filterInfo = column.GetType().GetProperty("FilterInfo")?.GetValue(column, null);
            object value = filterInfo?.GetType().GetProperty("Value")?.GetValue(filterInfo, null);
            return value?.ToString()?.Trim();
        }

        private Rectangle GetCellTextBounds(DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e, string displayText)
        {
            Rectangle bounds = e.Bounds;
            bounds.Inflate(-4, 0);

            Size textSize = TextRenderer.MeasureText(displayText, e.Appearance.GetFont(), bounds.Size, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            if (e.Appearance.TextOptions.HAlignment == HorzAlignment.Center)
                bounds.X += Math.Max(0, (bounds.Width - textSize.Width) / 2);
            else if (e.Appearance.TextOptions.HAlignment == HorzAlignment.Far)
                bounds.X = bounds.Right - textSize.Width;

            bounds.Width = Math.Min(bounds.Width, textSize.Width);
            return bounds;
        }

        private static Rectangle GetTextRangeBounds(Graphics graphics, Font font, string text, int start, int length, Rectangle textBounds)
        {
            string prefix = start > 0 ? text.Substring(0, start) : string.Empty;
            string match = text.Substring(start, length);
            int prefixWidth = string.IsNullOrEmpty(prefix)
                ? 0
                : TextRenderer.MeasureText(graphics, prefix, font, textBounds.Size, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int matchWidth = TextRenderer.MeasureText(graphics, match, font, textBounds.Size, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

            return new Rectangle(textBounds.Left + prefixWidth, textBounds.Top + 2, Math.Max(2, matchWidth), Math.Max(2, textBounds.Height - 4));
        }
        /// <summary>
        /// Returns true when <paramref name="latest"/> is a newer version than <paramref name="local"/>.
        /// Uses numeric Version comparison when both strings are parseable; falls back to a
        /// case-insensitive string diff so that non-semver author strings (e.g. "v1.0.abcd") still
        /// trigger a warning whenever the values differ.
        /// </summary>
        private static bool IsVersionOutdated(string local, string latest)
        {
            if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(latest))
                return false;
            string localNorm  = local.TrimStart('v', 'V').Trim();
            string latestNorm = latest.TrimStart('v', 'V').Trim();
            if (Version.TryParse(localNorm,  out Version localV) &&
                Version.TryParse(latestNorm, out Version latestV))
                return localV < latestV;
            return !string.Equals(localNorm, latestNorm, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns a lazy-initialised 13×13 flat amber warning triangle with a white "!".
        /// Drawn with GDI+ so there is no dependency on external image resources.
        /// </summary>
        private Bitmap GetWarningIcon()
        {
            if (_warningIcon != null) return _warningIcon;
            const int sz = 13;
            var bmp = new Bitmap(sz, sz);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                // flat amber filled triangle
                PointF[] tri =
                {
                    new PointF(sz / 2f,     0.5f),
                    new PointF(sz - 0.5f,  sz - 0.5f),
                    new PointF(0.5f,       sz - 0.5f),
                };
                using (var fill = new SolidBrush(Color.FromArgb(242, 160, 2)))
                    g.FillPolygon(fill, tri);
                // white "!" centred inside the triangle
                using (var font = new Font("Segoe UI", sz * 0.50f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var white = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat
                    {
                        Alignment     = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                    };
                    g.DrawString("!", font, white, new RectangleF(0f, 2f, sz, sz - 2f), sf);
                }
            }
            return _warningIcon = bmp;
        }

        /// <summary>Highlights the active sort column header in blue.</summary>
        private void GridView_CustomDrawColumnHeader(object sender, DevExpress.XtraGrid.Views.Grid.ColumnHeaderCustomDrawEventArgs e)
        {
            if (e.Column == null || e.Column.SortOrder == DevExpress.Data.ColumnSortOrder.None)
                return;
            e.Appearance.BackColor  = Color.FromArgb(219, 234, 254);
            e.Appearance.BackColor2 = Color.FromArgb(219, 234, 254);
            e.Appearance.ForeColor  = Color.FromArgb(37, 99, 235);
            e.DefaultDraw();
            e.Handled = true;
        }

        /// <summary>Draws a coloured pill badge for the category cell.</summary>
        private void DrawCategoryBadge(DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.RowHandle < 0 || !_showColouredCategories) return;
            int src = gridView.GetDataSourceRowIndex(e.RowHandle);
            if (src < 0 || src >= _modList.Count) return;
            IMod mod = _modList[src];

            string catName = string.Empty;
            if (_viewModel?.CategoryManager != null)
            {
                var cat = _viewModel.CategoryManager.FindCategory(
                    mod.CustomCategoryId > 0 ? mod.CustomCategoryId : mod.CategoryId);
                catName = cat?.CategoryName ?? string.Empty;
            }

            // Paint row background (inherits selection / active-mod colour set by RowCellStyle)
            Color bg = e.Appearance.BackColor;
            if (bg == Color.Empty) bg = SystemColors.Window;
            using (var bgBrush = new SolidBrush(bg))
                e.Graphics.FillRectangle(bgBrush, e.Bounds);

            if (!string.IsNullOrEmpty(catName))
            {
                Color badgeColor = GetCategoryColor(catName);
                const int padH = 6, padV = 2, radius = 4;
                using (var badgeFont = new Font(_gridFontFamilyName, GetBadgeGridFontSize(_gridFontSizePt), FontStyle.Regular, GraphicsUnit.Point))
                {
                    SizeF textSize = e.Graphics.MeasureString(catName, badgeFont);
                    int badgeW = Math.Min((int)textSize.Width + padH * 2, e.Bounds.Width - 4);
                    int badgeH = (int)textSize.Height + padV * 2;
                    int bx = e.Bounds.Left + (e.Bounds.Width  - badgeW) / 2;
                    int by = e.Bounds.Top  + (e.Bounds.Height - badgeH) / 2;
                    var badgeRect = new Rectangle(bx, by, badgeW, badgeH);

                    var savedMode = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = GetRoundedRectPath(badgeRect, radius))
                    using (var fill = new SolidBrush(badgeColor))
                        e.Graphics.FillPath(fill, path);
                    e.Graphics.SmoothingMode = savedMode;

                    var sf = new StringFormat
                    {
                        Alignment     = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming      = StringTrimming.EllipsisCharacter,
                        FormatFlags   = StringFormatFlags.NoWrap,
                    };
                    using (var white = new SolidBrush(Color.White))
                        e.Graphics.DrawString(catName, badgeFont, white, (RectangleF)badgeRect, sf);
                }
            }

            e.Handled = true;
        }

        private static GraphicsPath GetRoundedRectPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left,          r.Top,           d, d, 180, 90);
            path.AddArc(r.Right - d,     r.Top,           d, d, 270, 90);
            path.AddArc(r.Right - d,     r.Bottom - d,    d, d,   0, 90);
            path.AddArc(r.Left,          r.Bottom - d,    d, d,  90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Maps a category name to a semantic badge colour via keyword matching.</summary>
        // Palette used for category names that don't match a semantic keyword.
        // Ordered so adjacent indices stay visually distinct.
        private static readonly Color[] _categoryPalette =
        {
            Color.FromArgb(139,  92, 246),   // violet
            Color.FromArgb( 59, 130, 246),   // blue
            Color.FromArgb(236,  72, 153),   // pink
            Color.FromArgb( 20, 184, 166),   // teal
            Color.FromArgb(245, 158,  11),   // amber
            Color.FromArgb( 34, 197,  94),   // green
            Color.FromArgb(249, 115,  22),   // orange
            Color.FromArgb( 99, 102, 241),   // indigo
            Color.FromArgb(220,  38,  38),   // red
            Color.FromArgb( 14, 165, 233),   // sky
            Color.FromArgb(168,  85, 247),   // purple
            Color.FromArgb( 13, 148, 136),   // dark teal
        };

        /// <summary>Maps a category name to a semantic badge colour.</summary>
        private static Color GetCategoryColor(string categoryName)
        {
            // Empty or explicitly unassigned → neutral grey
            if (string.IsNullOrWhiteSpace(categoryName) ||
                categoryName.Equals("unassigned", StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(107, 114, 128);

            string n = categoryName.ToLowerInvariant();

            // Semantic keyword matches
            if (n.Contains("armor")    || n.Contains("armour")    || n.Contains("weapon"))
                return Color.FromArgb(139,  92, 246);   // violet  — armour / weapons
            if (n.Contains("bug")      || n.Contains("fix")       || n.Contains("patch"))
                return Color.FromArgb( 59, 130, 246);   // blue    — bug fixes / patches
            if (n.Contains("body")     || n.Contains("face")      || n.Contains("skin") ||
                n.Contains("hair")     || n.Contains("race"))
                return Color.FromArgb(236,  72, 153);   // pink    — body / face / skin
            if (n.Contains("follower") || n.Contains("companion") || n.Contains("npc"))
                return Color.FromArgb( 20, 184, 166);   // teal    — followers / companions
            if (n.Contains("creature") || n.Contains("animal")   || n.Contains("monster") ||
                n.Contains("beast"))
                return Color.FromArgb(245, 158,  11);   // amber   — creatures / animals

            // Any other named category — deterministic colour from hash so the same
            // category always gets the same colour and different categories look different.
            uint hash = 2166136261u;
            foreach (char c in n)
                hash = (hash ^ (uint)c) * 16777619u;
            return _categoryPalette[hash % (uint)_categoryPalette.Length];
        }

        /// <summary>
        /// Sizes all columns to their content, pins Author at 128 px,
        /// and lets MOD NAME absorb the remaining grid width.
        /// </summary>
        private void ApplyColumnSizing()
        {
            if (!IsHandleCreated) return;
            gridView.BeginUpdate();
            try
            {
                // Author: always 128 px
                var authorCol = gridView.Columns[ColAuthor];
                if (authorCol != null) authorCol.Width = 128;

                // Best-fit every other visible column except Mod Name
                if (_modList.Count > 0)
                {
                    for (int i = 0; i < gridView.VisibleColumns.Count; i++)
                    {
                        GridColumn col = gridView.VisibleColumns[i];
                        if (col.FieldName == ColModName || col.FieldName == ColAuthor) continue;
                        col.BestFit();
                    }
                }

                ApplyAutoFilterDefaults();
                ApplyDateSortDefaults();
                SetModNameFill();
            }
            finally { gridView.EndUpdate(); }
        }

        private void ScheduleColumnSizing()
        {
            _pendingColumnSizing = true;
            _columnFillTimer.Stop();
            _columnFillTimer.Start();
        }

        private void ScheduleModNameFill()
        {
            _columnFillTimer.Stop();
            _columnFillTimer.Start();
        }

        private void ColumnFillTimer_Tick(object sender, EventArgs e)
        {
            _columnFillTimer.Stop();
            if (_pendingColumnSizing)
            {
                _pendingColumnSizing = false;
                ApplyColumnSizing();
                return;
            }
            SetModNameFill();
        }

        /// <summary>Sets MOD NAME width to whatever horizontal space is left after all other columns.</summary>
        private void SetModNameFill()
        {
            var modNameCol = gridView.Columns[ColModName];
            if (modNameCol == null || !IsHandleCreated) return;
            int viewWidth = gridView.ViewRect.Width;
            if (viewWidth <= 0) return;
            int used = 0;
            for (int i = 0; i < gridView.VisibleColumns.Count; i++)
            {
                GridColumn col = gridView.VisibleColumns[i];
                if (col.FieldName != ColModName) used += col.Width;
            }
            int fill = Math.Max(modNameCol.MinWidth, viewWidth - used - SystemInformation.VerticalScrollBarWidth - 4);
            if (Math.Abs(modNameCol.Width - fill) > 1)
                modNameCol.Width = fill;
        }

        // Keep the grid layout with the existing UI layout settings so Reset UI can clear it.
        private bool RestoreGridLayout()
        {
            if (_viewModel?.Settings == null)
            {
                return false;
            }

            _restoringGridLayout = true;
            try
            {
                if (_viewModel.Settings.DockPanelLayouts.ContainsKey(GridLayoutKey))
                {
                    string layout = _viewModel.Settings.DockPanelLayouts[GridLayoutKey];
                    if (!string.IsNullOrWhiteSpace(layout))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(layout);
                        using (var stream = new MemoryStream(bytes))
                        {
                            gridView.RestoreLayoutFromStream(stream);
                        }
                        ApplyAutoFilterDefaults();
                        ApplyDateSortDefaults();
                        SetModNameFill();
                        return true;
                    }
                }

                ApplyAutoFilterDefaults();
                ApplyDateSortDefaults();
                SetModNameFill();
                return false;
            }
            catch
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
                return false;
            }
            finally
            {
                _restoringGridLayout = false;
            }
        }

        private void RestoreGridSort()
        {
            if (_viewModel?.Settings?.DockPanelLayouts.ContainsKey(GridSortKey) != true)
            {
                return;
            }

            string sortLayout = _viewModel.Settings.DockPanelLayouts[GridSortKey];
            if (string.IsNullOrWhiteSpace(sortLayout))
            {
                return;
            }

            gridView.SortInfo.BeginUpdate();
            try
            {
                gridView.SortInfo.Clear();
                foreach (string item in sortLayout.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = item.Split('|');
                    if (parts.Length != 2) continue;

                    GridColumn column = gridView.Columns[parts[0]];
                    if (column == null) continue;

                    if (Enum.TryParse(parts[1], out DevExpress.Data.ColumnSortOrder order) &&
                        order != DevExpress.Data.ColumnSortOrder.None)
                    {
                        gridView.SortInfo.Add(column, order);
                    }
                }
            }
            finally
            {
                gridView.SortInfo.EndUpdate();
            }
        }

        private void RestoreGridCategoryView()
        {
            bool wasRestoring = _restoringGridLayout;
            _restoringGridLayout = true;
            try
            {
                bool active = _viewModel?.Settings?.DockPanelLayouts.ContainsKey(GridCategoryViewKey) == true &&
                              string.Equals(_viewModel.Settings.DockPanelLayouts[GridCategoryViewKey], bool.TrueString, StringComparison.OrdinalIgnoreCase);

                ApplyCategoryView(active, true);
                if (active)
                    RestoreCollapsedCategoryGroups();
            }
            finally
            {
                _restoringGridLayout = wasRestoring;
            }
        }

        private void ApplyCategoryView(bool active, bool expandAll)
        {
            _categoryViewActive = active;
            var catCol = gridView.Columns[ColCategory];
            if (_categoryViewActive && catCol != null)
            {
                gridView.OptionsView.ShowGroupPanel = true;
                ApplyCategoryGroupSummary();
                catCol.SortOrder = DevExpress.Data.ColumnSortOrder.Ascending;
                catCol.GroupIndex = 0;
                if (expandAll)
                    gridView.ExpandAllGroups();
            }
            else
            {
                gridView.ClearGrouping();
                gridView.GroupSummary.Clear();
                gridView.OptionsView.ShowGroupPanel = false;
            }

            UpdateSwitchViewText();
        }

        private void ApplyCategoryGroupSummary()
        {
            gridView.GroupSummary.Clear();
            gridView.GroupSummary.Add(new GridGroupSummaryItem
            {
                SummaryType = DevExpress.Data.SummaryItemType.Count,
                FieldName = string.Empty,
                DisplayFormat = "{0} mods",
                ShowInGroupColumnFooter = null
            });
            gridView.GroupFormat = "{0}: {1} ({2})";
        }

        private void SaveGridCategoryState()
        {
            if (_viewModel?.Settings == null)
                return;

            _viewModel.Settings.DockPanelLayouts[GridCategoryViewKey] = _categoryViewActive.ToString();
            if (!_categoryViewActive)
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridCollapsedCategoriesKey);
                return;
            }

            List<string> collapsed = GetCollapsedCategoryNames();
            if (collapsed.Count == 0)
                _viewModel.Settings.DockPanelLayouts.Remove(GridCollapsedCategoriesKey);
            else
                _viewModel.Settings.DockPanelLayouts[GridCollapsedCategoriesKey] = string.Join("\n", collapsed);
        }

        private List<string> GetCollapsedCategoryNames()
        {
            var categories = new List<string>();
            for (int visibleIndex = 0; visibleIndex < gridView.RowCount; visibleIndex++)
            {
                int rowHandle = gridView.GetVisibleRowHandle(visibleIndex);
                if (!gridView.IsGroupRow(rowHandle) || gridView.GetRowExpanded(rowHandle))
                    continue;

                object value = gridView.GetGroupRowValue(rowHandle);
                string categoryName = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(categoryName) && !categories.Contains(categoryName, StringComparer.OrdinalIgnoreCase))
                    categories.Add(categoryName);
            }
            return categories;
        }

        private void RestoreCollapsedCategoryGroups()
        {
            if (_viewModel?.Settings?.DockPanelLayouts.ContainsKey(GridCollapsedCategoriesKey) != true)
                return;

            var collapsed = new HashSet<string>(
                (_viewModel.Settings.DockPanelLayouts[GridCollapsedCategoriesKey] ?? string.Empty)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);

            if (collapsed.Count == 0)
                return;

            for (int visibleIndex = 0; visibleIndex < gridView.RowCount; visibleIndex++)
            {
                int rowHandle = gridView.GetVisibleRowHandle(visibleIndex);
                if (!gridView.IsGroupRow(rowHandle))
                    continue;

                object value = gridView.GetGroupRowValue(rowHandle);
                string categoryName = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (collapsed.Contains(categoryName))
                    gridView.CollapseGroupRow(rowHandle);
            }
        }

        private void UpdateSwitchViewText()
        {
            if (tsbSwitchView == null)
                return;

            tsbSwitchView.Text = _categoryViewActive ? "Switch to Default View" : "Switch to Category View";
            tsbSwitchView.ToolTipText = _categoryViewActive ? "Show the default flat mod list" : "Group the mod list by category";
        }
        private void SaveGridLayout()
        {
            if (_restoringGridLayout || _viewModel?.Settings == null)
            {
                return;
            }

            try
            {
                using (var stream = new MemoryStream())
                {
                    gridView.SaveLayoutToStream(stream);
                    _viewModel.Settings.DockPanelLayouts[GridLayoutKey] = Encoding.UTF8.GetString(stream.ToArray());
                }
            }
            catch
            {
                _viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
            }

            SaveGridSort();
            SaveGridCategoryState();
            _viewModel.Settings.Save();
        }

        private void SaveGridSort()
        {
            var parts = new List<string>();
            foreach (GridColumnSortInfo sortInfo in gridView.SortInfo)
            {
                if (sortInfo.Column == null || sortInfo.SortOrder == DevExpress.Data.ColumnSortOrder.None) continue;
                parts.Add(sortInfo.Column.FieldName + "|" + sortInfo.SortOrder);
            }

            if (parts.Count == 0)
                _viewModel.Settings.DockPanelLayouts.Remove(GridSortKey);
            else
                _viewModel.Settings.DockPanelLayouts[GridSortKey] = string.Join(";", parts);
        }
        protected override void OnClosed(EventArgs e)
        {
            SaveGridLayout();
            base.OnClosed(e);
        }

        private enum InlineEditGlyph
        {
            Pencil,
            Accept,
            Cancel,
        }

        private void InitializeInlineRenameEditor()
        {
            _renamePanel = new Panel
            {
                Visible = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
            };
            _renameTextBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            };
            _renameAcceptButton = CreateInlineRenameButton(GetInlineEditIcon(InlineEditGlyph.Accept), CommitInlineRename);
            _renameCancelButton = CreateInlineRenameButton(GetInlineEditIcon(InlineEditGlyph.Cancel), CancelInlineRename);
            _renameTextBox.KeyDown += RenameTextBox_KeyDown;
            _renamePanel.Controls.Add(_renameTextBox);
            _renamePanel.Controls.Add(_renameAcceptButton);
            _renamePanel.Controls.Add(_renameCancelButton);
            gridControl.Controls.Add(_renamePanel);
            _renamePanel.BringToFront();
        }

        private Button CreateInlineRenameButton(Image image, EventHandler handler)
        {
            var button = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Image = image,
                TabStop = false,
                Width = InlineEditIconSize + 4,
                Height = InlineEditIconSize + 4,
                BackColor = Color.Transparent,
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += handler;
            return button;
        }

        private bool IsDataRowHandle(int rowHandle)
        {
            return rowHandle >= 0 && !gridView.IsGroupRow(rowHandle);
        }

        private void DrawModNameCell(DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (!IsDataRowHandle(e.RowHandle)) return;

            int sourceRow = gridView.GetDataSourceRowIndex(e.RowHandle);
            IMod mod = sourceRow >= 0 && sourceRow < _modList.Count ? _modList[sourceRow] : null;
            bool archiveMissing = IsModArchiveMissing(mod);
            bool isRenameRow = _renamePanel?.Visible == true && e.RowHandle == _renameRowHandle;
            bool isHoverRow = e.RowHandle == _hoveredModNameRowHandle;
            if (!isHoverRow && !isRenameRow && !archiveMissing)
            {
                return;
            }

            e.DefaultDraw();
            if (isRenameRow)
            {
                e.Handled = true;
                return;
            }

            int right = e.Bounds.Right - 5;
            if (isHoverRow)
            {
                _hoveredModNameCellBounds = e.Bounds;
                int iconSize = InlineEditIconSize;
                int x = right - iconSize;
                int y = e.Bounds.Top + (e.Bounds.Height - iconSize) / 2;
                _hoveredModNameIconBounds = new Rectangle(x, y, iconSize, iconSize);
                right = _hoveredModNameIconBounds.Left - 4;

                using (var pen = new Pen(Color.FromArgb(180, 190, 205)))
                {
                    var border = e.Bounds;
                    border.Width -= 1;
                    border.Height -= 1;
                    e.Graphics.DrawRectangle(pen, border);
                }
                e.Graphics.DrawImage(GetInlineEditIcon(InlineEditGlyph.Pencil), _hoveredModNameIconBounds);
            }

            if (archiveMissing)
            {
                Bitmap warningIcon = GetWarningIcon();
                int x = right - warningIcon.Width;
                int y = e.Bounds.Top + (e.Bounds.Height - warningIcon.Height) / 2;
                e.Graphics.DrawImage(warningIcon, x, y, warningIcon.Width, warningIcon.Height);
            }

            e.Handled = true;
        }

        private void GridControl_MouseMove(object sender, MouseEventArgs e)
        {
            if (_renamePanel?.Visible == true)
            {
                return;
            }

            var hit = gridView.CalcHitInfo(e.Location);
            int newHover = hit.InRowCell && IsDataRowHandle(hit.RowHandle) && hit.Column?.FieldName == ColModName
                ? hit.RowHandle
                : DevExpress.XtraGrid.GridControl.InvalidRowHandle;
            if (newHover != _hoveredModNameRowHandle)
            {
                int oldHover = _hoveredModNameRowHandle;
                _hoveredModNameRowHandle = newHover;
                _hoveredModNameCellBounds = Rectangle.Empty;
                _hoveredModNameIconBounds = Rectangle.Empty;
                if (oldHover >= 0) gridView.InvalidateRow(oldHover);
                if (newHover >= 0) gridView.InvalidateRow(newHover);
            }
            gridControl.Cursor = _hoveredModNameIconBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        private void GridControl_MouseLeave(object sender, EventArgs e)
        {
            if (_renamePanel?.Visible == true) return;
            int oldHover = _hoveredModNameRowHandle;
            _hoveredModNameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
            _hoveredModNameCellBounds = Rectangle.Empty;
            _hoveredModNameIconBounds = Rectangle.Empty;
            gridControl.Cursor = Cursors.Default;
            if (oldHover >= 0) gridView.InvalidateRow(oldHover);
        }

        private void GridControl_MouseDown(object sender, MouseEventArgs e)
        {
            if (_renamePanel?.Visible == true)
            {
                if (!_renamePanel.Bounds.Contains(e.Location))
                {
                    CommitInlineRename(sender, EventArgs.Empty);
                }
                return;
            }

            var hit = gridView.CalcHitInfo(e.Location);
            if (hit.InRowCell && IsDataRowHandle(hit.RowHandle) && hit.Column?.FieldName == ColModName && _hoveredModNameIconBounds.Contains(e.Location))
            {
                StartInlineRename(hit.RowHandle, _hoveredModNameCellBounds);
            }
        }

        private void StartInlineRename(int rowHandle, Rectangle cellBounds)
        {
            int src = gridView.GetDataSourceRowIndex(rowHandle);
            if (_viewModel == null || src < 0 || src >= _modList.Count || cellBounds.IsEmpty) return;

            _renameMod = _modList[src];
            _renameOriginalName = _renameMod.ModName ?? string.Empty;
            _renameRowHandle = rowHandle;
            _suppressNextDoubleClick = true;

            int panelHeight = Math.Max(24, cellBounds.Height - 2);
            _renamePanel.Bounds = new Rectangle(cellBounds.Left + 1, cellBounds.Top + 1, Math.Max(150, cellBounds.Width - 2), panelHeight);
            int buttonSize = Math.Min(panelHeight - 2, InlineEditIconSize + 4);
            _renameAcceptButton.SetBounds(_renamePanel.Width - buttonSize * 2 - 3, 1, buttonSize, buttonSize);
            _renameCancelButton.SetBounds(_renamePanel.Width - buttonSize - 2, 1, buttonSize, buttonSize);
            _renameTextBox.SetBounds(4, Math.Max(2, (panelHeight - _renameTextBox.PreferredHeight) / 2), _renamePanel.Width - buttonSize * 2 - 10, _renameTextBox.PreferredHeight);
            _renameTextBox.Text = _renameOriginalName;
            _renamePanel.Visible = true;
            _renamePanel.BringToFront();
            gridView.InvalidateRow(rowHandle);
            _renameTextBox.Focus();
            _renameTextBox.SelectAll();
        }

        private void RenameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitInlineRename(sender, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelInlineRename(sender, EventArgs.Empty);
            }
        }

        private void CommitInlineRename(object sender, EventArgs e)
        {
            if (_renamePanel?.Visible != true) return;
            string newName = (_renameTextBox.Text ?? string.Empty).Trim();
            IMod mod = _renameMod;
            int rowHandle = _renameRowHandle;
            HideInlineRenameEditor();

            if (mod != null && !string.IsNullOrEmpty(newName) &&
                !string.Equals(newName, _renameOriginalName, StringComparison.Ordinal))
            {
                _viewModel.UpdateModName(mod, newName);
                gridControl.RefreshDataSource();
            }
            if (rowHandle >= 0) gridView.InvalidateRow(rowHandle);
        }

        private void CancelInlineRename(object sender, EventArgs e)
        {
            int rowHandle = _renameRowHandle;
            HideInlineRenameEditor();
            if (rowHandle >= 0) gridView.InvalidateRow(rowHandle);
        }

        private void HideInlineRenameEditor()
        {
            _renamePanel.Visible = false;
            _renameMod = null;
            _renameOriginalName = null;
            _renameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
            _hoveredModNameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
            _hoveredModNameCellBounds = Rectangle.Empty;
            _hoveredModNameIconBounds = Rectangle.Empty;
            gridControl.Cursor = Cursors.Default;
        }

        private Bitmap GetInlineEditIcon(InlineEditGlyph glyph)
        {
            switch (glyph)
            {
                case InlineEditGlyph.Accept:
                    return _inlineAcceptIcon ?? (_inlineAcceptIcon = LoadInlineEditIcon(glyph));
                case InlineEditGlyph.Cancel:
                    return _inlineCancelIcon ?? (_inlineCancelIcon = LoadInlineEditIcon(glyph));
                default:
                    return _inlineEditIcon ?? (_inlineEditIcon = LoadInlineEditIcon(glyph));
            }
        }

        private static Bitmap LoadInlineEditIcon(InlineEditGlyph glyph)
        {
            Image image = LoadSvgIcon(GetInlineEditIconResourceName(glyph), InlineEditIconSize);
            if (image != null) return new Bitmap(image);
            return CreateInlineEditIcon(glyph);
        }

        private static string GetInlineEditIconResourceName(InlineEditGlyph glyph)
        {
            switch (glyph)
            {
                case InlineEditGlyph.Accept: return "inline_edit_checkmark.svg";
                case InlineEditGlyph.Cancel: return "inline_edit_cancel.svg";
                default: return "inline_edit_pencil.svg";
            }
        }

        private static Bitmap CreateInlineEditIcon(InlineEditGlyph glyph)
        {
            const int sz = InlineEditIconSize;
            var bmp = new Bitmap(sz, sz);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                Color fill = glyph == InlineEditGlyph.Accept ? Color.FromArgb(234, 248, 240) : Color.FromArgb(243, 246, 250);
                Color stroke = glyph == InlineEditGlyph.Accept ? Color.FromArgb(36, 163, 90) : Color.FromArgb(75, 85, 99);
                var rect = new Rectangle(2, 2, sz - 4, sz - 4);
                using (var path = GetRoundedRectPath(rect, 4))
                using (var brush = new SolidBrush(fill))
                using (var pen = new Pen(stroke, 1.4f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }

                using (var pen = new Pen(stroke, glyph == InlineEditGlyph.Accept ? 1.8f : 1.6f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    if (glyph == InlineEditGlyph.Accept)
                    {
                        g.DrawLines(pen, new[] { new PointF(5.8f, 9.1f), new PointF(8.4f, 11.6f), new PointF(13.2f, 6.6f) });
                    }
                    else if (glyph == InlineEditGlyph.Cancel)
                    {
                        g.DrawLine(pen, 6.7f, 6.7f, 11.3f, 11.3f);
                        g.DrawLine(pen, 11.3f, 6.7f, 6.7f, 11.3f);
                    }
                    else
                    {
                        g.DrawPolygon(pen, new[] { new PointF(6.0f, 12.0f), new PointF(7.0f, 9.3f), new PointF(11.5f, 4.8f), new PointF(13.2f, 6.5f), new PointF(8.7f, 11.0f) });
                        g.DrawLine(pen, 10.8f, 5.5f, 12.5f, 7.2f);
                        g.DrawLine(pen, 6.0f, 12.0f, 8.0f, 11.3f);
                    }
                }
            }
            return bmp;
        }
        private void GridView_DoubleClick(object sender, EventArgs e)
        {
            if (_suppressNextDoubleClick) { _suppressNextDoubleClick = false; return; }
            if (_renamePanel?.Visible == true) return;

            var info = gridView.CalcHitInfo(gridView.GridControl.PointToClient(Control.MousePosition));
            if (info.HitTest == GridHitTest.ColumnEdge && info.Column != null)
            {
                info.Column.BestFit();
                if (info.Column.FieldName != ColModName) SetModNameFill();
                return;
            }

            if (info.InRow || info.InRowCell)
                ToggleSelectedMod();
        }

        private void GridView_KeyDown(object sender, KeyEventArgs e)
        {
            if (_renamePanel?.Visible == true) return;
            if (e.KeyCode == Keys.F2 && TryStartHoveredModNameRename()) { e.Handled = true; return; }
            if (e.KeyCode == Keys.Return) { e.Handled = true; ToggleSelectedMod(); }
            if (e.KeyCode == Keys.Delete) { e.Handled = true; DeleteSelectedModsFromKey(); }
            if (e.KeyData == (Keys.Control | Keys.F)) SetTextBoxFocus?.Invoke(this, e);
        }

        private bool TryStartHoveredModNameRename()
        {
            Point clientPoint = gridControl.PointToClient(Control.MousePosition);
            var hit = gridView.CalcHitInfo(clientPoint);
            if (!hit.InRowCell || !IsDataRowHandle(hit.RowHandle) || hit.Column == null || hit.Column.FieldName != ColModName)
                return false;

            Rectangle cellBounds = _hoveredModNameCellBounds;
            if (cellBounds.IsEmpty || !cellBounds.Contains(clientPoint))
            {
                GridViewInfo viewInfo = gridView.GetViewInfo() as GridViewInfo;
                GridCellInfo cellInfo = viewInfo?.GetGridCellInfo(hit.RowHandle, hit.Column);
                if (cellInfo != null)
                    cellBounds = cellInfo.Bounds;
            }

            if (cellBounds.IsEmpty)
                return false;

            StartInlineRename(hit.RowHandle, cellBounds);
            return true;
        }

        private void DeleteSelectedModsFromKey()
        {
            if (_viewModel == null || !_viewModel.DeleteModCommand.CanExecute) return;

            var mods = SelectedMods;
            if (mods.Count == 0) return;
            if (!ConfirmModFileDeletion(mods)) return;
            if (!ConfirmMissingArchiveUninstall(mods)) return;

            DeactivateAllMods(mods, true, true, false);

            var oclMods = new ThreadSafeObservableList<IMod>(mods);
            _viewModel.DeleteMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), true, true, false);
        }

        private void ToggleSelectedMod()
        {
            var mod = SelectedMod;
            if (mod == null || _viewModel == null) return;
            SetCommandExecutableStatus();
            bool active = _viewModel.VirtualModActivator.ActiveModList
                .Contains(Path.GetFileName(mod.Filename).ToLowerInvariant());
            if (active)
                _viewModel.DisableModCommand.Execute(new List<IMod> { mod });
            else
                _viewModel.ActivateModCommand.Execute(new List<IMod> { mod });
        }

        // ── Context menu (popup) ─────────────────────────────────────────────

        private void gridView_PopupMenuShowing(object sender, PopupMenuShowingEventArgs e)
        {
            if (e.MenuType != GridMenuType.Row) return;
            gridView.FocusedRowHandle = e.HitInfo.RowHandle;
            var mod = SelectedMod;
            if (mod == null) return;

            var mods = SelectedMods;
            if (mods.Count == 0) mods = new List<IMod> { mod };
            bool singleMod = mods.Count == 1;

            bool active    = _viewModel?.ActiveMods.Contains(mod) ?? false;
            bool installed = active || IsModInstalled(mod);

            var menu = new ContextMenuStrip();
            ApplyToolStripFont(menu, new Font(_gridFontFamilyName, _gridFontSizePt, FontStyle.Regular, GraphicsUnit.Point));

            // ── mod filename header (disabled, for identification) ────────────
            var itemHeader = new ToolStripMenuItem(Path.GetFileName(mod.Filename),
                new System.Drawing.Bitmap(Properties.Resources.document_save, 16, 16));
            itemHeader.Enabled = false;
            menu.Items.Add(itemHeader);
            menu.Items.Add(new ToolStripSeparator());

            // ── activate / deactivate / reinstall ────────────────────────────
            if (singleMod)
            {
                if (!installed)
                {
                    var itemActivate = new ToolStripMenuItem("Install and activate",
                        new System.Drawing.Bitmap(Properties.Resources.dialog_ok_4_16, 16, 16));
                    itemActivate.Click += (s, ev) =>
                        _viewModel?.ActivateModCommand.Execute(new List<IMod> { mod });
                    menu.Items.Add(itemActivate);
                }
                else if (!active)
                {
                    var itemActivate = new ToolStripMenuItem("Activate",
                        new System.Drawing.Bitmap(Properties.Resources.dialog_ok_4_16, 16, 16));
                    itemActivate.Click += (s, ev) =>
                        _viewModel?.ActivateModCommand.Execute(new List<IMod> { mod });
                    menu.Items.Add(itemActivate);
                }
                else
                {
                    var itemDeactivate = new ToolStripMenuItem("Deactivate",
                        ToolStripRenderer.CreateDisabledImage(new System.Drawing.Bitmap(Properties.Resources.dialog_ok_4_16, 16, 16)));
                    itemDeactivate.Click += (s, ev) =>
                        _viewModel?.DisableModCommand.Execute(new List<IMod> { mod });
                    menu.Items.Add(itemDeactivate);

                    var itemReinstall = new ToolStripMenuItem("Reinstall Mod",
                        new System.Drawing.Bitmap(Properties.Resources.change_game_mode, 16, 16));
                    itemReinstall.Click += (s, ev) =>
                    {
                        if (_viewModel != null)
                            _viewModel.ReinstallMod(mod, null);
                    };
                    menu.Items.Add(itemReinstall);
                }
            }
            else
            {
                var itemReinstall = new ToolStripMenuItem("Reinstall Mod/s",
                    new System.Drawing.Bitmap(Properties.Resources.change_game_mode, 16, 16));
                itemReinstall.Click += (s, ev) =>
                {
                    if (_viewModel != null)
                        _viewModel.ReinstallMultipleMods(mods);
                };
                menu.Items.Add(itemReinstall);
            }

            // ── Uninstall or Delete submenu ───────────────────────────────────
            var itemUninstall = new ToolStripMenuItem("Uninstall or Delete",
                new System.Drawing.Bitmap(Properties.Resources.dialog_block, 16, 16));
            if (singleMod)
            {
                itemUninstall.DropDownItems.Add("From active profile", null, (s, ev) =>
                {
                    if (_viewModel != null && ConfirmMissingArchiveUninstall(mods))
                        _viewModel.DeactivateMod(mod);
                });
                itemUninstall.DropDownItems.Add("From all profiles", null, (s, ev) =>
                {
                    if (_viewModel == null || !ConfirmMissingArchiveUninstall(mods)) return;
                    _viewModel.DeactivateMod(mod);
                    UninstallModFromProfiles?.Invoke(this, new ModEventArgs(mod));
                });
                itemUninstall.DropDownItems.Add(new ToolStripSeparator());
                itemUninstall.DropDownItems.Add(
                    "Delete mod (permanently) and uninstall.",
                    new System.Drawing.Bitmap(Properties.Resources.dialog_cancel_4_16, 16, 16),
                    (s, ev) =>
                    {
                        if (_viewModel == null) return;
                        if (ConfirmModFileDeletion(mods) && ConfirmMissingArchiveUninstall(mods))
                        {
                            _viewModel.DeactivateMod(mod);
                            UninstallModFromProfiles?.Invoke(this, new ModEventArgs(mod));
                            var oclMods = new ThreadSafeObservableList<IMod>(mods);
                            _viewModel.DeleteMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), true, true, false);
                        }
                    });
            }
            else
            {
                itemUninstall.DropDownItems.Add("From active profile", null, (s, ev) =>
                {
                    if (_viewModel != null && ConfirmMissingArchiveUninstall(mods))
                        _viewModel.DeactivateSelectedMods(mods);
                });
            }
            menu.Items.Add(itemUninstall);

            // ── Mod Update Warnings submenu ───────────────────────────────────
            var itemWarnings = new ToolStripMenuItem("Mod Update Warnings",
                new System.Drawing.Bitmap(Properties.Resources.update_warning, 16, 16));
            BuildUpdateWarningsSubmenu(itemWarnings, mods);
            if (itemWarnings.DropDownItems.Count > 0)
                menu.Items.Add(itemWarnings);

            // ── Mod Update Checks submenu ─────────────────────────────────────
            var itemChecks = new ToolStripMenuItem("Mod Update Checks and Automatic Mod Rename",
                new System.Drawing.Bitmap(Properties.Resources.edit_find_and_replace, 16, 16));
            BuildUpdateChecksSubmenu(itemChecks, mods);
            if (itemChecks.DropDownItems.Count > 0)
                menu.Items.Add(itemChecks);

            // ── Move to (category) submenu ────────────────────────────────────
            if (_viewModel?.CategoryManager != null)
            {
                var itemMoveTo = new ToolStripMenuItem("Move to");
                foreach (IModCategory cat in _viewModel.CategoryManager.Categories
                    .OrderBy(c => c.CategoryName))
                {
                    var catId   = cat.Id;
                    var catName = cat.CategoryName;
                    itemMoveTo.DropDownItems.Add(catName, null, (s, ev) =>
                    {
                        if (_viewModel != null)
                            _viewModel.SwitchModsToCategory(mods, catId);
                    });
                }
                if (itemMoveTo.DropDownItems.Count > 0)
                {
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(itemMoveTo);
                }
            }

            menu.Show(gridControl, gridControl.PointToClient(Control.MousePosition));
            e.Allow = false;
        }

        private void BuildUpdateWarningsSubmenu(ToolStripMenuItem parent, List<IMod> mods)
        {
            if (mods == null || mods.Count == 0) return;

            if (mods.Count == 1)
            {
                var m = mods[0];
                parent.DropDownItems.Add(
                    m.UpdateWarningEnabled ? "Disable update warning" : "Enable update warning",
                    null,
                    (s, e) =>
                    {
                        if (_viewModel != null)
                            _viewModel.ToggleModUpdateWarning(new HashSet<IMod>(mods), !m.UpdateWarningEnabled);
                    });
            }
            else
            {
                bool hasEnabled  = mods.Any(x => x.UpdateWarningEnabled);
                bool hasDisabled = mods.Any(x => !x.UpdateWarningEnabled);

                if (hasDisabled)
                    parent.DropDownItems.Add("Enable for selected files", null, (s, e) =>
                        _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(mods), true));
                if (hasEnabled)
                    parent.DropDownItems.Add("Disable for selected files", null, (s, e) =>
                        _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(mods), false));
            }

            if (parent.DropDownItems.Count > 0)
                parent.DropDownItems.Add(new ToolStripSeparator());

            parent.DropDownItems.Add("Enable for all files", null, (s, e) =>
                _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(_viewModel.ManagedMods), true));
            parent.DropDownItems.Add("Disable for all files", null, (s, e) =>
                _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(_viewModel.ManagedMods), false));
        }

        private void BuildUpdateChecksSubmenu(ToolStripMenuItem parent, List<IMod> mods)
        {
            if (mods == null || mods.Count == 0) return;

            if (mods.Count == 1)
            {
                var m = mods[0];
                parent.DropDownItems.Add(
                    m.UpdateChecksEnabled ? "Disable for this mod" : "Enable for this mod",
                    null,
                    (s, e) =>
                    {
                        if (_viewModel != null)
                            _viewModel.ToggleModUpdateCheck(new HashSet<IMod>(mods), !m.UpdateChecksEnabled);
                    });
            }
            else
            {
                bool hasEnabled  = mods.Any(x => x.UpdateChecksEnabled);
                bool hasDisabled = mods.Any(x => !x.UpdateChecksEnabled);

                if (hasDisabled)
                    parent.DropDownItems.Add("Enable for selected mods", null, (s, e) =>
                        _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(mods), true));
                if (hasEnabled)
                    parent.DropDownItems.Add("Disable for selected mods", null, (s, e) =>
                        _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(mods), false));
            }

            if (parent.DropDownItems.Count > 0)
                parent.DropDownItems.Add(new ToolStripSeparator());

            parent.DropDownItems.Add("Enable for all mods", null, (s, e) =>
                _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(_viewModel.ManagedMods), true));
        }

        // ── Toolbar helpers ──────────────────────────────────────────────────

        private IMod GetSelectedMod() => SelectedMod;
        private List<IMod> GetSelectedMods()
        {
            var list = SelectedMods;
            return list.Count > 0 ? list : null;
        }

        private void ConfigureDeactivateDropDown()
        {
            tsbDeactivate.DropDownItems.Clear();
            AddDeactivateDropDownItem("Uninstall mod from current profile", "mod-uninstall-from-profile.svg", UninstallSelectedModsFromCurrentProfile);
            AddDeactivateDropDownItem("Delete mod", "mod-remove.svg", DeleteSelectedModsFromKey);
        }

        private void AddDeactivateDropDownItem(string text, string iconResourceName, Action action)
        {
            var item = new ToolStripMenuItem(text, LoadSvgIcon(iconResourceName, 24), (s, e) => action())
            {
                ImageScaling = ToolStripItemImageScaling.None
            };
            tsbDeactivate.DropDownItems.Add(item);
        }

        private void UninstallSelectedModsFromCurrentProfile()
        {
            if (_viewModel == null) return;

            List<IMod> mods = SelectedMods;
            if (mods.Count == 0 || !ConfirmMissingArchiveUninstall(mods)) return;

            if (mods.Count == 1)
                _viewModel.DeactivateMod(mods[0]);
            else
                _viewModel.DeactivateSelectedMods(mods);
        }

        private void UpdateToolbarState()
        {
            tsbDeactivate.Enabled = SelectedMods.Count > 0;
            ApplyToolbarActionLabels();
        }

        private void UpdateModCountLabel()
        {
            toolStripLabelModCount.Text = $"Mods: {_modList.Count}";
        }

        // ── Toolbar button handlers ──────────────────────────────────────────

        private void tsbDeactivate_ButtonClick(object sender, EventArgs e)
        {
            if (_viewModel == null || !_viewModel.DisableModCommand.CanExecute) return;

            List<IMod> mods = GetSelectedMods();
            if (mods == null) return;

            _viewModel.DeactivateSelectedMods(mods);
        }
        private void tsbAddMod_ButtonClick(object sender, EventArgs e)
        {
            addModToolStripMenuItem_Click(sender, e);
        }

        private void addModToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter      = "Mod Archives|*.zip;*.7z;*.rar;*.fomod;*.omod|All Files|*.*";
                ofd.Multiselect = true;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                    foreach (string f in ofd.FileNames)
                        _viewModel.AddModCommand.Execute(f);
            }
        }

        private void addModFromURLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            string strDefault = "nxm://";
            if (Clipboard.ContainsText())
            {
                string clip = Clipboard.GetText();
                if (!string.IsNullOrEmpty(clip) && clip.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                    strDefault = clip;
            }
            var dlg = PromptDialog.ShowDialog(null, this,
                "NMM URL: (eg. nxm://Skyrim/mods/193/files/8998)",
                "Choose URL", strDefault,
                @"nxm://\w+/mods/\d+/files/\d+",
                "Must be a Nexus Mod URL.");
            if (dlg != null && !string.IsNullOrEmpty(dlg.EnteredText))
                _viewModel.AddModCommand.Execute(dlg.EnteredText);
        }

        private void tsbSkyrimDownloads_Click(object sender, EventArgs e)
        {
            _viewModel?.ToggleSkyrimSEDownloadMode();
            SetSkyrimDownloadModeFeedback();
        }

        private void tsb_SaveModLoadOrder_Click(object sender, EventArgs e)
        {
            if (_viewModel?.ModManager.GameMode.UsesModLoadOrder == true)
                _viewModel.SaveModLoadOrder();
        }

        private void tsb_ModUpLoadOrder_Click(object sender, EventArgs e)
        {
            if (_viewModel?.ModManager.GameMode.UsesModLoadOrder != true) return;
            var mods = SelectedMods;
            if (mods.Count == 0 && SelectedMod != null) mods = new List<IMod> { SelectedMod };
            foreach (var mod in mods)
                _viewModel.UpdateModLoadOrder(mod, mod.NewPlaceInModLoadOrder == -1 ? -1 : mod.NewPlaceInModLoadOrder - 1);
            gridView.InvalidateRows();
        }

        private void tsb_ModDownLoadOrder_Click(object sender, EventArgs e)
        {
            if (_viewModel?.ModManager.GameMode.UsesModLoadOrder != true) return;
            var mods = SelectedMods;
            if (mods.Count == 0 && SelectedMod != null) mods = new List<IMod> { SelectedMod };
            foreach (var mod in mods)
                _viewModel.UpdateModLoadOrder(mod, mod.NewPlaceInModLoadOrder == int.MaxValue ? int.MaxValue : mod.NewPlaceInModLoadOrder + 1);
            gridView.InvalidateRows();
        }

        private void tsbModOnlineChecks_ButtonClick(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                _disableSummary = true;
                _viewModel.CheckForUpdates(false);
                _disableSummary = false;
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                    MessageBox.Show(this,
                        $"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void withinTheLastDayToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RunUpdatedModsCheck("1d");
        }

        private void withinTheLastWeekToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RunUpdatedModsCheck("1w");
        }

        private void withinTheLastMonthToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RunUpdatedModsCheck("1m");
        }

        private void RunUpdatedModsCheck(string period)
        {
            if (_viewModel == null) return;
            try
            {
                _disableSummary = true;
                _viewModel.CheckUpdatedMods(period);
                _disableSummary = false;
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                    MessageBox.Show(this,
                        $"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void checkFileDownloadId_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                _disableSummary = true;
                _viewModel.CheckModFileDownloadId(null);
                _disableSummary = false;
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                    MessageBox.Show(this,
                        $"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void checkMissingDownloadId_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                _disableSummary = true;
                _viewModel.CheckModFileDownloadId(true);
                _disableSummary = false;
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                    MessageBox.Show(this,
                        $"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void tsbToggleEndorse_Click(object sender, EventArgs e)
        {
            var mod = SelectedMod;
            if (mod == null || _viewModel == null) return;
            tsbToggleEndorse.Enabled = false;
            bool? current = mod.IsEndorsed;
            try
            {
                var hashMods = new System.Collections.Generic.HashSet<IMod>(SelectedMods);
                if (hashMods.Count == 0) hashMods.Add(mod);
                _viewModel.ToggleModEndorsement(mod, hashMods, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Unable to {(current != true ? "endorse" : "unendorse")} this file:{Environment.NewLine}{ex.Message}",
                    "Endorsement Error:", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                tsbToggleEndorse.Enabled = true;
            }
            SetCommandExecutableStatus();
        }

        private void tsbShowUpdatesOnly_Click(object sender, EventArgs e)
        {
            _showUpdatesOnly = !_showUpdatesOnly;
            tsbShowUpdatesOnly.Checked = _showUpdatesOnly;
            gridView.ActiveFilterString = _showUpdatesOnly
                ? $"[{ColLastKnown}] != null And [{ColLastKnown}] != '' And [{ColVersion}] != [{ColLastKnown}]"
                : string.Empty;
        }

        private string GetExportToFileArgs()
        {
            if (_viewModel == null) return null;
            using (var sfd = new SaveFileDialog())
            {
                sfd.FileName = _viewModel.GetDefaultExportFilename();
                sfd.Filter   = _viewModel.GetExportFilterString();
                return sfd.ShowDialog(this) == DialogResult.OK ? sfd.FileName : null;
            }
        }

        // ── Category toolbar handlers ────────────────────────────────────────

        private void addNewCategory_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.CategoryManager.AddCategory();
        }

        private void collapseAllCategories_Click(object sender, EventArgs e)
        {
            if (!_categoryViewActive)
                ApplyCategoryView(true, true);
            gridView.CollapseAllGroups();
            SaveGridLayout();
        }

        private void expandAllCategories_Click(object sender, EventArgs e)
        {
            if (!_categoryViewActive)
                ApplyCategoryView(true, false);
            gridView.ExpandAllGroups();
            SaveGridLayout();
        }

        private void resetDefaultCategories_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            try
            {
                _disableSummary = true;
                _viewModel.CheckCategoriesUpdates();
                _disableSummary = false;
            }
            catch (Exception ex)
            {
                if (ex.Message != "Login required")
                    MessageBox.Show(this,
                        $"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                        "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void resetUnassignedToDefaultCategories_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            var lstSelectedMods = _viewModel.ManagedMods
                .Where(m => m.CategoryId > 0 && m.CustomCategoryId == 0)
                .ToList();
            if (lstSelectedMods.Count > 0)
                _viewModel.SwitchModsToCategory(lstSelectedMods, -1);
            _viewModel.CheckForUpdates(true);
            gridView.InvalidateRows();
            ResetSearchBox?.Invoke(this, EventArgs.Empty);
        }

        private void resetModsCategory_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.ResetToUnassigned();
            gridView.InvalidateRows();
        }

        private void removeAllCategories_Click(object sender, EventArgs e)
        {
            if (_viewModel == null) return;
            if (_viewModel.RemoveAllCategories())
            {
                gridView.InvalidateRows();
                ResetSearchBox?.Invoke(this, EventArgs.Empty);
            }
        }

        private void toggleHiddenCategories_Click(object sender, EventArgs e) { /* flat grid has no hidden categories */ }

        private void tsbSwitchView_Click(object sender, EventArgs e)
        {
            ApplyCategoryView(!_categoryViewActive, true);
            SaveGridLayout();
        }

        // ── VM event handlers (progress dialogs, dialogs) ────────────────────

        private void VM_UpdatingCategory(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingCategory, sender, e); return; }
            _disableSummary = true;
            ProgressDialog.ShowDialog(this, e.Argument);
            _viewModel?.VirtualModActivator.SaveList();
            RefreshActivationState();
            _disableSummary = false;
        }

        private void VM_UpdatingMods(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingMods, sender, e); return; }
            _disableSummary = true;
            ProgressDialog.ShowDialog(this, e.Argument);
            _disableSummary = false;
            if (e.Argument.ReturnValue is Dictionary<string, string> dct)
                _viewModel?.UpdateVirtualListDownloadId(dct);
            else if (e.Argument.ReturnValue != null)
            {
                string msg = e.Argument.ReturnValue.ToString();
                if (msg.Length > 2)
                    ExtendedMessageBox.Show(this, msg, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void VM_UpdatingCategories(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_UpdatingCategories, sender, e); return; }
            _disableSummary = true;
            ProgressDialog.ShowDialog(this, e.Argument);
            _disableSummary = false;
            if (e.Argument.ReturnValue != null)
                ExtendedMessageBox.Show(this, "Unable to update the category list online, it will use base categories: " + Environment.NewLine + e.Argument.ReturnValue, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _viewModel?.ResetDefaultCategories(e.Argument.ReturnValue != null);
            ResetSearchBox?.Invoke(this, e);
        }

        private void VM_TogglingAllWarning(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_TogglingAllWarning, sender, e); return; }
            _disableSummary = true;
            ProgressDialog.ShowDialog(this, e.Argument);
            _disableSummary = false;
        }

        private void VM_TogglingModUpdateChecks(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_TogglingModUpdateChecks, sender, e); return; }
            _disableSummary = true;
            ProgressDialog.ShowDialog(this, e.Argument);
            _disableSummary = false;
        }

        private void VM_ReadMeManagerSetup(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_ReadMeManagerSetup, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
        }

        private void VM_AddingMod(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_AddingMod, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
        }

        private void VM_DeletingMod(object sender, EventArgs<IBackgroundTaskSet> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTaskSet>>)VM_DeletingMod, sender, e); return; }
            e.Argument.TaskStarted     += TaskSet_TaskStarted;
            e.Argument.TaskSetCompleted += TaskSet_TaskSetCompleted;
        }

        private void VM_ActivatingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_ActivatingMultipleMods, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
        }

        private void VM_ActivatingMod(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_ActivatingMod, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshActivationState();
        }

        private void VM_ReinstallingMod(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_ReinstallingMod, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
        }

        private void VM_DisablingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_DisablingMultipleMods, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshActivationState();
            UninstalledAllMods?.Invoke(this, System.EventArgs.Empty);
        }

        private void VM_DeletingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_DeletingMultipleMods, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
        }

        private void VM_DeactivatingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_DeactivatingMultipleMods, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
            RefreshActivationState();
            UninstalledAllMods?.Invoke(this, System.EventArgs.Empty);
        }

        private void VM_AutomaticDownloading(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)VM_AutomaticDownloading, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument, false);
        }

        private void VM_ChangingModActivation(object sender, EventArgs<IBackgroundTaskSet> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTaskSet>>)VM_ChangingModActivation, sender, e); return; }
            e.Argument.TaskStarted      += TaskSet_TaskStarted;
            e.Argument.TaskSetCompleted += TaskSet_TaskSetCompleted;
        }

        private void VM_TaggingMod(object sender, EventArgs<ModTaggerVM> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<ModTaggerVM>>)VM_TaggingMod, sender, e); return; }
            if (_viewModel != null && !_viewModel.ModRepository.IsOffline)
                new ModTaggerForm(e.Argument).ShowDialog(this);
        }

        private void VM_ExportFailed(object sender, ExportFailedEventArgs e)
        {
            if (InvokeRequired) { Invoke((Action<object, ExportFailedEventArgs>)VM_ExportFailed, sender, e); return; }
            string msg = "An error was encountered trying to export the current mod list."
                + Environment.NewLine + Environment.NewLine
                + "Full details are available in the trace log.";
            string details = "<b>Error:</b> " + e.Message;
            ExtendedMessageBox.Show(this, msg, "Export Failed", details, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void VM_ExportSucceeded(object sender, ExportSucceededEventArgs e)
        {
            if (InvokeRequired) { Invoke((Action<object, ExportSucceededEventArgs>)VM_ExportSucceeded, sender, e); return; }
            string msg = "The current mod list was successfully exported to";
            if (string.IsNullOrEmpty(e.Filename))
                msg += " the clipboard.";
            else
                msg += ":" + Environment.NewLine + Environment.NewLine + e.Filename;
            string details = string.Format("{0} {1} successfully exported.",
                e.ExportedModCount, e.ExportedModCount == 1 ? "mod was" : "mods were");
            ExtendedMessageBox.Show(this, msg, "Export Succeeded", details, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Background task set handlers ─────────────────────────────────────

        private void TaskSet_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
        {
            if (InvokeRequired) { Invoke((Action<object, EventArgs<IBackgroundTask>>)TaskSet_TaskStarted, sender, e); return; }
            ProgressDialog.ShowDialog(this, e.Argument);
        }

        private void TaskSet_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
        {
            if (InvokeRequired) { Invoke((Action<object, TaskSetCompletedEventArgs>)TaskSet_TaskSetCompleted, sender, e); return; }
            ((IBackgroundTaskSet)sender).TaskStarted      -= TaskSet_TaskStarted;
            ((IBackgroundTaskSet)sender).TaskSetCompleted -= TaskSet_TaskSetCompleted;

            if (!string.IsNullOrEmpty(e.Message))
            {
                if (e.Success)
                    MessageBox.Show(this, e.Message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    MessageBox.Show(this, e.Message, "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Confirm dialogs ──────────────────────────────────────────────────

        private bool ConfirmModFileDeletion(List<IMod> mods)
        {
            if (InvokeRequired)
            {
                bool r = false;
                Invoke((MethodInvoker)(() => r = ConfirmModFileDeletion(mods)));
                return r;
            }
            int n = 0;
            string msg = string.Empty;
            foreach (IMod m in mods)
            {
                if (++n > 25) { msg += $"And {mods.Count - 25} more mods.\r\n"; break; }
                msg += $"- {m.ModName}\r\n";
            }
            msg += "\r\nThese mods will be uninstalled and permanently deleted from your hard drive.\r\nAre you sure?\r\n\r\nThis operation cannot be undone.";
            return ExtendedMessageBox.Show(this, msg, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private bool ConfirmMissingArchiveUninstall(List<IMod> mods)
        {
            if (InvokeRequired)
            {
                bool r = false;
                Invoke((MethodInvoker)(() => r = ConfirmMissingArchiveUninstall(mods)));
                return r;
            }

            var missingMods = mods.Where(IsModArchiveMissingOnDisk).ToList();
            if (missingMods.Count == 0) return true;

            var msg = new StringBuilder();
            if (missingMods.Count == 1)
            {
                msg.AppendLine("The archive for this mod is missing. NMM can uninstall it from the current setup, but it will not be reinstallable from the mod manager unless the archive is restored.");
                msg.AppendLine();
                msg.AppendLine("Missing archive:");
            }
            else
            {
                msg.AppendLine($"{missingMods.Count} selected mod archives are missing. NMM can uninstall these mods from the current setup, but they will not be reinstallable from the mod manager unless the archives are restored.");
                msg.AppendLine();
                msg.AppendLine("Missing archives:");
            }

            int n = 0;
            foreach (IMod mod in missingMods)
            {
                if (++n > 10) { msg.AppendLine($"And {missingMods.Count - 10} more mods."); break; }
                msg.AppendLine($"- {mod.ModName}");
            }

            msg.AppendLine();
            msg.AppendLine("Continue?");
            return ExtendedMessageBox.Show(this, msg.ToString(), "Missing Mod Archive", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private string ConfirmModFileOverwrite(string oldPath, string newPath)
        {
            if (InvokeRequired)
            {
                string r = null;
                Invoke((MethodInvoker)(() => r = ConfirmModFileOverwrite(oldPath, newPath)));
                return r;
            }
            switch (MessageBox.Show(this,
                $"A mod archive already exists at:\r\n{oldPath}\r\n\r\nWould you like to overwrite it?",
                "Overwrite?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
            {
                case DialogResult.Yes:    return newPath;
                case DialogResult.No:     return oldPath;
                default:                  return null;
            }
        }

        private OverwriteResult ConfirmItemOverwrite(string itemMessage, bool allowPerGroup, bool allowPerMod)
        {
            if (InvokeRequired)
            {
                OverwriteResult r = OverwriteResult.No;
                Invoke((MethodInvoker)(() => r = ConfirmItemOverwrite(itemMessage, allowPerGroup, allowPerMod)));
                return r;
            }
            return OverwriteForm.ShowDialog(this, itemMessage, allowPerGroup, allowPerMod);
        }

        private ConfirmUpgradeResult ConfirmModUpgrade(IMod oldMod, IMod newMod)
        {
            if (InvokeRequired)
            {
                ConfirmUpgradeResult r = ConfirmUpgradeResult.Cancel;
                Invoke((MethodInvoker)(() => r = ConfirmModUpgrade(oldMod, newMod)));
                return r;
            }
            switch (MessageBox.Show(this,
                $"A newer version of '{oldMod.ModName}' has been found.\r\nWould you like to upgrade?",
                "Upgrade Mod?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
            {
                case DialogResult.Yes:    return ConfirmUpgradeResult.Upgrade;
                case DialogResult.No:     return ConfirmUpgradeResult.NormalActivation;
                default:                  return ConfirmUpgradeResult.Cancel;
            }
        }
    }
}


