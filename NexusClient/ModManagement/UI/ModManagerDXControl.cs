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

	using DevExpress.LookAndFeel;
	using DevExpress.XtraBars;
	using DevExpress.XtraEditors;
	using DevExpress.Utils;
	using DevExpress.XtraEditors.Controls;
	using DevExpress.XtraEditors.Drawing;
	using ButtonEdit = DevExpress.XtraEditors.ButtonEdit;
	using TextEdit = DevExpress.XtraEditors.TextEdit;
	using DevExpress.XtraEditors.Repository;
	using DevExpress.XtraEditors.ViewInfo;
	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Columns;
	using DevExpress.XtraGrid.Views.Grid;
	using DevExpress.XtraGrid.Views.Grid.ViewInfo;

	using Nexus.Client.BackgroundTasks;
	using Nexus.Client.BackgroundTasks.UI;
	using Nexus.Client.Commands;
	using Nexus.Client.Commands.Generic;
	using Nexus.Client.ModManagement;
	using Nexus.Client.ModRepositories;
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
		private RepositoryItemButtonEdit _renameButtonEdit;
		private Control _renameActiveEditor;
		private BarSubItem _displayOptionsButton;
		private BarButtonItem _toggleColouredCategoriesMenuItem;
		private BarButtonItem _toggleRowHighlightsMenuItem;
		private BarButtonItem _toggleActiveModsBoldMenuItem;
		private BarButtonItem _focusTopRowAfterSortingMenuItem;
		private BarButtonItem _focusTopRowAfterInstallDateChangeMenuItem;
		private IMod _renameMod;
		private string _renameOriginalName;
		private int _renameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
		private bool _renamingModName;
		private bool _cancelRenameEdit;
		private bool _refreshAfterRename;
		private bool _suppressNextDoubleClick;
		private bool _testingRenameButtonHit;
		private bool _missingArchiveScanQueued;
		private string _gridFontFamilyName = DefaultGridFontFamily;
		private float _gridFontSizePt = DefaultGridFontSizePt;
		private string _gridDensity = DefaultGridDensity;
		private bool _showColouredCategories = true;
		private bool _showRowHighlights = true;
		private bool _showActiveModsInBold;
		private bool _focusTopRowAfterSorting = true;
		private bool _focusTopRowAfterInstallDateChange = true;
		private bool _lastFindPanelVisible;
		private bool _restoringFindPanelVisibility;
		private bool _toolbarPositionLeft;
		private BarButtonItem _toolbarPositionButton;
		private BarStaticItem _toolbarSeparatorAfterDisable;
		private BarStaticItem _toolbarSeparatorAfterEndorse;
		private BarStaticItem _toolbarSeparatorAfterCategoryView;
		private PopupMenu _gridPopupMenu;
		private readonly List<ICommandBinding> _toolbarCommandBindings = new List<ICommandBinding>();
		private bool _restoringGridSort;
		private string _lastGridSortSignature = string.Empty;
		private readonly HashSet<string> _activeModFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<IMod> _installedMods = new HashSet<IMod>();
		private readonly Dictionary<IMod, ModVisualStatus> _modVisualStatusCache = new Dictionary<IMod, ModVisualStatus>();
		private readonly Dictionary<string, bool> _missingArchiveByFileName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		private readonly object _missingArchiveLock = new object();
		private Image _modInstalledDisabledIcon;
		private Image _modInstalledActiveIcon;

		// Cached resources used by hot grid paint/data callbacks.
		private Font _gridRegularFont;
		private Font _gridBoldFont;
		private Font _gridUnderlineFont;
		private Font _gridBoldUnderlineFont;
		private Font _gridSecondaryFont;
		private Font _gridSecondaryBoldFont;
		private Font _gridHeaderFont;
		private Font _gridBadgeFont;
		private Image _endorsedYesImage;
		private Image _endorsedNoImage;
		private Image _endorsedEmptyImage;
		private bool _usesLegacyLightRowPalette;
		private Timer _gridLayoutSaveTimer;

		private readonly Dictionary<IMod, bool> _outdatedModCache =
			new Dictionary<IMod, bool>();
		private readonly Dictionary<IMod, string> _categoryNameCache =
			new Dictionary<IMod, string>();
		private readonly Dictionary<string, Color> _categoryColorCache =
			new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, Size> _categoryTextSizeCache =
			new Dictionary<string, Size>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<int, SolidBrush> _categoryBrushCache =
			new Dictionary<int, SolidBrush>();

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

		private const string ColModStatus = "ModStatus";
		private const string ColModName = "ModName";
		private const string ColVersion = "HumanReadableVersion";
		private const string ColLastKnown = "LastKnownVersion";
		private const string ColAuthor = "Author";
		private const string ColCategory = "CategoryId";
		private const string ColInstallDate = "InstallDate";
		private const string ColDownloadDate = "DownloadDate";
		private const string ColEndorsed = "IsEndorsed";
		private const string ColDownloadId = "DownloadId";
		private const string GridLayoutKey = "modManagerDXGrid";
		private const string GridColumnWidthsKey = GridLayoutKey + ".ColumnWidths";
		private const string GridFindPanelVisibleKey = GridLayoutKey + ".FindPanelVisible";
		private const string GridSortKey = GridLayoutKey + ".Sort";
		private const string GridFontKey = GridLayoutKey + ".Font";
		private const string GridFontSizeKey = GridLayoutKey + ".FontSize";
		private const string GridDensityKey = GridLayoutKey + ".Density";
		private const string GridColouredCategoriesKey = GridLayoutKey + ".ColouredCategories";
		private const string GridRowHighlightsKey = GridLayoutKey + ".RowHighlights";
		private const string GridActiveModsBoldKey = GridLayoutKey + ".ActiveModsBold";
		private const string GridCategoryViewKey = GridLayoutKey + ".CategoryView";
		private const string GridCollapsedCategoriesKey = GridLayoutKey + ".CollapsedCategories";
		private const string GridFocusTopAfterSortKey = GridLayoutKey + ".FocusTopAfterSort";
		private const string GridFocusTopAfterInstallDateChangeKey = GridLayoutKey + ".FocusTopAfterInstallDateChange";
		private const string GridToolbarPositionKey = GridLayoutKey + ".ToolbarLeft";
		private const string DefaultGridFontFamily = "Segoe UI";
		private const float DefaultGridFontSizePt = 9f;
		private const string DefaultGridDensity = "Compact";
		private enum ColumnSizingRole
		{
			Fixed,
			Bounded,
			FlexiblePrimary,
			FlexibleSecondary,
		}

		private sealed class ColumnSizingDefinition
		{
			public string FieldName { get; }
			public ColumnSizingRole Role { get; }
			public int DefaultWidth { get; }
			public int MinimumWidth { get; }
			public int MaximumWidth { get; }
			public ColumnSizingDefinition(string fieldName, ColumnSizingRole role, int defaultWidth, int minimumWidth, int maximumWidth) { FieldName = fieldName; Role = role; DefaultWidth = defaultWidth; MinimumWidth = minimumWidth; MaximumWidth = maximumWidth; }
		}

		private static readonly ColumnSizingDefinition[] GridColumnSizingDefinitions =
		{
			new ColumnSizingDefinition(ColModStatus, ColumnSizingRole.Fixed, 58, 48, 80), new ColumnSizingDefinition(ColModName, ColumnSizingRole.FlexiblePrimary, 220, 100, 0), new ColumnSizingDefinition(ColVersion, ColumnSizingRole.Fixed, 70, 60, 110), new ColumnSizingDefinition(ColLastKnown, ColumnSizingRole.Fixed, 70, 60, 110), new ColumnSizingDefinition(ColAuthor, ColumnSizingRole.Bounded, 128, 90, 240), new ColumnSizingDefinition(ColCategory, ColumnSizingRole.Bounded, 90, 80, 220), new ColumnSizingDefinition(ColInstallDate, ColumnSizingRole.Bounded, 90, 80, 150), new ColumnSizingDefinition(ColDownloadDate, ColumnSizingRole.Bounded, 90, 80, 150), new ColumnSizingDefinition(ColDownloadId, ColumnSizingRole.Fixed, 80, 70, 120), new ColumnSizingDefinition(ColEndorsed, ColumnSizingRole.Fixed, 70, 50, 90),
		};
		private const int ModStatusIconSize = 20;
		private const int InlineEditIconSize = 18;
		private const int GridLayoutSaveDelayMs = 400;
		private const string RenameButtonActionRename = "Rename";
		private const string RenameButtonActionAccept = "Accept";
		private const string RenameButtonActionCancel = "Cancel";
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
			InitializePerformanceResources();
			UpdateSkinPaletteCache();
			InitializeToolbarIcons();
			ApplyToolbarActionLabels();
			Text = "Mods";
			InitializeInlineRenameEditor();
			SetupGrid();
			InitializeNewModCategoryView();
			InitializeGridDisplayOptions();
			InitializeToolbarPositionButton();
			InitializeToolbarSeparators();
			RebuildToolbarLinks();
			DevExpressDisplaySettingsApplier.NormalizeBarItemImages(barManagerMods, new System.Drawing.Size(16, 16));
			UpdateSwitchViewText();

			Shown += (sender, args) =>
				RestoreFindPanelVisibility();
		}

		/// <summary>
		/// Applies the MainForm-level Aa Display selection through the existing
		/// Mod Manager font, density and cached drawing resources.
		/// </summary>
		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{
			if (settings == null) return;

			SelectGridDisplay(
				settings.FontFamilyName,
				settings.FontSizePt,
				settings.Density,
				false);
			DevExpressDisplaySettingsApplier.ApplyToBarManager(barManagerMods, settings);
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
				{
					DetachNewModCategoryTracking();
					UnhookViewModel();
				}

				_viewModel = value;

				if (_viewModel != null)
				{
					HookViewModel();
					RestoreGridFont();
					RestoreGridDisplayOptions();
					AttachNewModCategoryTracking();
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

			UpdateSkinPaletteCache();
			gridView.InvalidateRows();
			gridView.Invalidate();
			gridControl.Invalidate();
		}

		/// <inheritdoc/>
		public void ResetColumns()
		{
			if (_viewModel?.Settings != null)
			{
				_viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridColumnWidthsKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridSortKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridColouredCategoriesKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridRowHighlightsKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridActiveModsBoldKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridCategoryViewKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridCollapsedCategoriesKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridFocusTopAfterSortKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridFocusTopAfterInstallDateChangeKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridToolbarPositionKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridFindPanelVisibleKey);
				_viewModel.Settings.Save();
			}

			_restoringGridLayout = true;
			gridView.Columns.Clear();
			BuildColumns();
			gridView.ClearSorting();
			_restoringGridLayout = false;
			SetColouredCategoriesVisible(true, false);
			SetRowHighlightsVisible(true, false);
			SetActiveModsBold(false, false);
			SetFocusTopRowAfterSorting(true, false);
			SetFocusTopRowAfterInstallDateChange(true, false);
			SetToolbarPosition(false, false);
			ApplyDefaultColumnSizing();
			SaveGridLayout();
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
				_viewModel.DisableModCommand.CanExecute = active;
				_viewModel.ActivateModCommand.CanExecute = !active;
				_viewModel.DeleteModCommand.CanExecute = true;
				_viewModel.TagModCommand.CanExecute = true;
			}
			else
			{
				_viewModel.DisableModCommand.CanExecute = false;
				_viewModel.ActivateModCommand.CanExecute = false;
				_viewModel.DeleteModCommand.CanExecute = false;
				_viewModel.TagModCommand.CanExecute = false;
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
			if (tsbSkyrimDownloads.ImageOptions.SvgImage == null)
				tsbSkyrimDownloads.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(
					_viewModel.SkyrimDownloadImage, new Size(16, 16));
			tsbSkyrimDownloads.Caption = "Download Mode: " + GetSkyrimDownloadModeLabel();
			tsbSkyrimDownloads.Hint = $"Skyrim SE current download mode: {_viewModel.SkyrimSEDownloadModeDescriptor}";
			tsbSkyrimDownloads.PaintStyle = BarItemPaintStyle.CaptionGlyph;
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
			_viewModel.UpdatingCategory += VM_UpdatingCategory;
			_viewModel.UpdatingMods += VM_UpdatingMods;
			_viewModel.UpdatingCategories += VM_UpdatingCategories;
			_viewModel.TogglingAllWarning += VM_TogglingAllWarning;
			_viewModel.TogglingModUpdateChecks += VM_TogglingModUpdateChecks;
			_viewModel.ReadMeManagerSetup += VM_ReadMeManagerSetup;
			_viewModel.AddingMod += VM_AddingMod;
			_viewModel.DeletingMod += VM_DeletingMod;
			_viewModel.ActivatingMultipleMods += VM_ActivatingMultipleMods;
			_viewModel.ActivatingMod += VM_ActivatingMod;
			_viewModel.ReinstallingMod += VM_ReinstallingMod;
			_viewModel.ReinstallCompleted += VM_ReinstallCompleted;
			_viewModel.DisablingMultipleMods += VM_DisablingMultipleMods;
			_viewModel.DeletingMultipleMods += VM_DeletingMultipleMods;
			_viewModel.DeactivatingMultipleMods += VM_DeactivatingMultipleMods;
			_viewModel.AutomaticDownloading += VM_AutomaticDownloading;
			_viewModel.ChangingModActivation += VM_ChangingModActivation;
			_viewModel.TaggingMod += VM_TaggingMod;
			_viewModel.ExportFailed += VM_ExportFailed;
			_viewModel.ExportSucceeded += VM_ExportSucceeded;

			_viewModel.ManagedMods.CollectionChanged += ManagedMods_CollectionChanged;
			_viewModel.ActiveMods.CollectionChanged += ActiveMods_CollectionChanged;
			if (_viewModel.CategoryManager != null)
				_viewModel.CategoryManager.CategoriesChanged += CategoryManager_CategoriesChanged;

			_viewModel.ConfirmModFileDeletion = ConfirmModFileDeletion;
			_viewModel.ConfirmModFileOverwrite = ConfirmModFileOverwrite;
			_viewModel.ConfirmItemOverwrite = ConfirmItemOverwrite;
			_viewModel.ConfirmModUpgrade = ConfirmModUpgrade;
			_viewModel.ParentForm = this;

			_viewModel.DeleteModCommand.CanExecute = false;
			_viewModel.ActivateModCommand.CanExecute = false;
			_viewModel.DisableModCommand.CanExecute = false;
			_viewModel.TagModCommand.CanExecute = false;

			DisposeToolbarCommandBindings();
			_toolbarCommandBindings.Add(new DevExpressBarItemCommandBinding<List<IMod>>(tsbActivate, _viewModel.ActivateModCommand, GetSelectedMods));
			ConfigureDeactivateDropDown();
			_toolbarCommandBindings.Add(new DevExpressBarItemCommandBinding<IMod>(tsbTagMod, _viewModel.TagModCommand, GetSelectedMod));
			_toolbarCommandBindings.Add(new DevExpressBarItemCommandBinding<string>(exportToTextFile, _viewModel.ExportModListToFileCommand, GetExportToFileArgs));
			_toolbarCommandBindings.Add(new DevExpressBarItemCommandBinding(exportToClipboard, _viewModel.ExportModListToClipboardCommand));

			_viewModel.ExportModListToFileCommand.CanExecute = _viewModel.CanExecuteExportCommands();
			_viewModel.ExportModListToClipboardCommand.CanExecute = _viewModel.CanExecuteExportCommands();

			tsbSkyrimDownloads.Visibility = _viewModel.IsSkyrimSEGameMode ? BarItemVisibility.Always : BarItemVisibility.Never;
			SetSkyrimDownloadModeFeedback();

			bool usesLoadOrder = _viewModel.ModManager.GameMode.UsesModLoadOrder;
			tsb_SaveModLoadOrder.Visibility = usesLoadOrder ? BarItemVisibility.Always : BarItemVisibility.Never;
			tsb_ModUpLoadOrder.Visibility = usesLoadOrder ? BarItemVisibility.Always : BarItemVisibility.Never;
			tsb_ModDownLoadOrder.Visibility = usesLoadOrder ? BarItemVisibility.Always : BarItemVisibility.Never;

			LoadMods();
		}

		/// <summary>
		/// Detaches all DevExpress toolbar command bindings owned by the current view model.
		/// </summary>
		private void DisposeToolbarCommandBindings()
		{
			foreach (ICommandBinding binding in _toolbarCommandBindings)
				binding.Unbind();
			_toolbarCommandBindings.Clear();
		}

		private void UnhookViewModel()
		{
			DisposeToolbarCommandBindings();
			_viewModel.UpdatingCategory -= VM_UpdatingCategory;
			_viewModel.UpdatingMods -= VM_UpdatingMods;
			_viewModel.UpdatingCategories -= VM_UpdatingCategories;
			_viewModel.TogglingAllWarning -= VM_TogglingAllWarning;
			_viewModel.TogglingModUpdateChecks -= VM_TogglingModUpdateChecks;
			_viewModel.ReadMeManagerSetup -= VM_ReadMeManagerSetup;
			_viewModel.AddingMod -= VM_AddingMod;
			_viewModel.DeletingMod -= VM_DeletingMod;
			_viewModel.ActivatingMultipleMods -= VM_ActivatingMultipleMods;
			_viewModel.ActivatingMod -= VM_ActivatingMod;
			_viewModel.ReinstallingMod -= VM_ReinstallingMod;
			_viewModel.ReinstallCompleted -= VM_ReinstallCompleted;
			_viewModel.DisablingMultipleMods -= VM_DisablingMultipleMods;
			_viewModel.DeletingMultipleMods -= VM_DeletingMultipleMods;
			_viewModel.DeactivatingMultipleMods -= VM_DeactivatingMultipleMods;
			_viewModel.AutomaticDownloading -= VM_AutomaticDownloading;
			_viewModel.ChangingModActivation -= VM_ChangingModActivation;
			_viewModel.TaggingMod -= VM_TaggingMod;
			_viewModel.ExportFailed -= VM_ExportFailed;
			_viewModel.ExportSucceeded -= VM_ExportSucceeded;

			_viewModel.ManagedMods.CollectionChanged -= ManagedMods_CollectionChanged;
			_viewModel.ActiveMods.CollectionChanged -= ActiveMods_CollectionChanged;
			if (_viewModel.CategoryManager != null)
				_viewModel.CategoryManager.CategoriesChanged -= CategoryManager_CategoriesChanged;

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
			_outdatedModCache.Clear();
			_categoryNameCache.Clear();

			foreach (IMod mod in _viewModel.ManagedMods)
			{
				mod.PropertyChanged += Mod_PropertyChanged;
				_modList.Add(mod);
			}

			RebuildActivationStateCache();
			QueueMissingArchiveScan();
			gridControl.RefreshDataSource();
			RestoreGridLayout();
			RestoreGridSort();
			RestoreGridCategoryView();

			if (IsHandleCreated)
			{
				BeginInvoke(
					(MethodInvoker)RestoreFindPanelVisibility);
			}

			_lastGridSortSignature = GetGridSortSignature();
			UpdateModCountLabel();
		}

		// ── Collection / property changed ────────────────────────────────────

		/// <summary>
		/// Invalidates category-derived grid values after category definitions change.
		/// </summary>
		private void CategoryManager_CategoriesChanged(object sender, EventArgs e)
		{
			if (IsDisposed || Disposing)
				return;
			if (InvokeRequired)
			{
				Invoke(new Action<object, EventArgs>(CategoryManager_CategoriesChanged), sender, e);
				return;
			}

			_categoryNameCache.Clear();
			_categoryColorCache.Clear();
			_categoryTextSizeCache.Clear();
			gridControl.RefreshDataSource();
			gridView.RefreshData();
		}

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

			_outdatedModCache.Clear();
			_categoryNameCache.Clear();
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
			bool focusTopAfterSortedPropertyChange = ShouldFocusTopAfterSortedPropertyChange(e.PropertyName);
			if (sender is IMod mod)
			{
				_outdatedModCache.Remove(mod);
				_categoryNameCache.Remove(mod);

				int srcIdx = _modList.IndexOf(mod);
				if (srcIdx >= 0)
				{
					int viewHandle = gridView.GetRowHandle(srcIdx);
					if (viewHandle != DevExpress.XtraGrid.GridControl.InvalidRowHandle)
						gridView.InvalidateRow(viewHandle);
				}
			}

			if (focusTopAfterSortedPropertyChange)
				QueueFocusFirstVisibleDataRow();
		}

		private bool ShouldFocusTopAfterSortedPropertyChange(string propertyName)
		{
			return _focusTopRowAfterInstallDateChange &&
				string.Equals(propertyName, ColInstallDate, StringComparison.Ordinal) &&
				IsGridSortedByColumn(ColInstallDate);
		}

		private bool IsGridSortedByColumn(string fieldName)
		{
			foreach (GridColumnSortInfo sortInfo in gridView.SortInfo)
			{
				if (sortInfo.Column != null && string.Equals(sortInfo.Column.FieldName, fieldName, StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		private void QueueFocusFirstVisibleDataRow()
		{
			if (IsDisposed || !IsHandleCreated) return;
			BeginInvoke(new MethodInvoker(FocusFirstVisibleDataRow));
		}

		private void FocusFirstVisibleDataRow()
		{
			if (IsDisposed || gridView == null || gridView.RowCount <= 0) return;
			int rowHandle = GetFirstVisibleDataRowHandle();
			if (rowHandle >= 0)
				FocusGridRow(rowHandle);
		}

		// ── Grid setup ───────────────────────────────────────────────────────

		/// <summary>
		/// Applies the embedded SVG toolbar assets to the DevExpress mod actions.
		/// </summary>
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

		/// <summary>
		/// Assigns one embedded SVG asset to a DevExpress bar item while retaining its caption.
		/// </summary>
		/// <param name="item">The toolbar item to configure.</param>
		/// <param name="resourceName">The trailing manifest-resource name of the SVG.</param>
		private static void ConfigureToolbarIcon(BarItem item, string resourceName)
		{
			if (item == null) return;

			DevExpress.Utils.Svg.SvgImage image = LoadSvgImage(resourceName);
			if (image == null) return;

			item.ImageOptions.Image = null;
			item.ImageOptions.SvgImage = image;
			item.PaintStyle = BarItemPaintStyle.CaptionGlyph;
		}

		/// <summary>
		/// Restores the canonical action captions after command bindings update the toolbar.
		/// </summary>
		private void ApplyToolbarActionLabels()
		{
			tsbDeactivate.Caption = "Disable Mod";
			tsbDeactivate.PaintStyle = BarItemPaintStyle.CaptionGlyph;
			tsbTagMod.Caption = "Get Mod Info";
			tsbTagMod.PaintStyle = BarItemPaintStyle.CaptionGlyph;
		}

		/// <summary>
		/// Loads one embedded SVG asset without rasterising it, allowing DevExpress to render it for the active skin and DPI.
		/// </summary>
		/// <param name="resourceName">The trailing manifest-resource name of the SVG.</param>
		/// <returns>The loaded SVG image, or <c>null</c> if the resource cannot be found.</returns>
		private static DevExpress.Utils.Svg.SvgImage LoadSvgImage(string resourceName)
		{
			var assembly = typeof(ModManagerDXControl).Assembly;
			string fullName = assembly.GetManifestResourceNames()
				.FirstOrDefault(name => name.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase));
			if (fullName == null) return null;

			using (Stream stream = assembly.GetManifestResourceStream(fullName))
			{
				return stream == null ? null : DevExpress.Utils.Svg.SvgImage.FromStream(stream);
			}
		}

		private void InitializePerformanceResources()
		{
			_endorsedYesImage = new Bitmap(Properties.Resources.thumb_up, 16, 16);
			_endorsedNoImage = new Bitmap(Properties.Resources.thumb_no, 16, 16);
			_endorsedEmptyImage = new Bitmap(16, 16);

			_gridLayoutSaveTimer = new Timer(components)
			{
				Interval = GridLayoutSaveDelayMs
			};
			_gridLayoutSaveTimer.Tick += GridLayoutSaveTimer_Tick;
		}

		private void UpdateSkinPaletteCache()
		{
			string skinName = UserLookAndFeel.Default.SkinName;

			_usesLegacyLightRowPalette =
				String.Equals(skinName, "Basic", StringComparison.OrdinalIgnoreCase) ||
				String.Equals(skinName, "DevExpress Style", StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Creates the DevExpress grid-display menu that controls mod-list visual options.
		/// </summary>
		private void InitializeGridDisplayOptions()
		{
			_displayOptionsButton = new BarSubItem(barManagerMods, "Display Options")
			{
				Alignment = BarItemLinkAlignment.Right,
				Hint = "Grid display options"
			};

			_toggleColouredCategoriesMenuItem = CreateCheckedDisplayOption("Toggle Coloured Categories", _showColouredCategories,
				(sender, args) => SetColouredCategoriesVisible(_toggleColouredCategoriesMenuItem.Down, true));
			_toggleRowHighlightsMenuItem = CreateCheckedDisplayOption("Toggle Row Highlights", _showRowHighlights,
				(sender, args) => SetRowHighlightsVisible(_toggleRowHighlightsMenuItem.Down, true));
			_toggleActiveModsBoldMenuItem = CreateCheckedDisplayOption("Show Active Mods in Bold", _showActiveModsInBold,
				(sender, args) => SetActiveModsBold(_toggleActiveModsBoldMenuItem.Down, true));
			_focusTopRowAfterSortingMenuItem = CreateCheckedDisplayOption("Focus top row after sorting", _focusTopRowAfterSorting,
				(sender, args) => SetFocusTopRowAfterSorting(_focusTopRowAfterSortingMenuItem.Down, true));
			_focusTopRowAfterInstallDateChangeMenuItem = CreateCheckedDisplayOption("Focus top row after install date changes", _focusTopRowAfterInstallDateChange,
				(sender, args) => SetFocusTopRowAfterInstallDateChange(_focusTopRowAfterInstallDateChangeMenuItem.Down, true));

			_displayOptionsButton.AddItem(_toggleColouredCategoriesMenuItem);
			_displayOptionsButton.AddItem(_toggleRowHighlightsMenuItem);
			_displayOptionsButton.AddItem(_toggleActiveModsBoldMenuItem);
			_displayOptionsButton.AddItem(_focusTopRowAfterSortingMenuItem);
			_displayOptionsButton.AddItem(_focusTopRowAfterInstallDateChangeMenuItem);
		}

		/// <summary>
		/// Creates one checkable DevExpress item for the grid-display menu.
		/// </summary>
		/// <param name="caption">The menu caption.</param>
		/// <param name="isChecked">The initial checked state.</param>
		/// <param name="handler">The action invoked after the item is toggled.</param>
		/// <returns>The configured checkable bar item.</returns>
		private BarButtonItem CreateCheckedDisplayOption(string caption, bool isChecked, ItemClickEventHandler handler)
		{
			var item = new BarButtonItem(barManagerMods, caption)
			{
				ButtonStyle = BarButtonStyle.Check,
				Down = isChecked
			};
			item.ItemClick += handler;
			return item;
		}

		private void RestoreGridDisplayOptions()
		{
			SetColouredCategoriesVisible(ReadGridDisplayOption(GridColouredCategoriesKey, true), false);
			SetRowHighlightsVisible(ReadGridDisplayOption(GridRowHighlightsKey, true), false);
			SetActiveModsBold(ReadGridDisplayOption(GridActiveModsBoldKey, false), false);
			SetFocusTopRowAfterSorting(ReadGridDisplayOption(GridFocusTopAfterSortKey, true), false);
			SetFocusTopRowAfterInstallDateChange(ReadGridDisplayOption(GridFocusTopAfterInstallDateChangeKey, true), false);
			SetToolbarPosition(ReadGridDisplayOption(GridToolbarPositionKey, false), false);
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
				_toggleColouredCategoriesMenuItem.Down = visible;

			RefreshGridDisplayStyles();
			SaveGridDisplayOption(GridColouredCategoriesKey, visible, save);
		}

		private void SetRowHighlightsVisible(bool visible, bool save)
		{
			_showRowHighlights = visible;
			if (_toggleRowHighlightsMenuItem != null)
				_toggleRowHighlightsMenuItem.Down = visible;

			RefreshGridDisplayStyles();
			SaveGridDisplayOption(GridRowHighlightsKey, visible, save);
		}

		private void SetActiveModsBold(bool visible, bool save)
		{
			_showActiveModsInBold = visible;
			if (_toggleActiveModsBoldMenuItem != null)
				_toggleActiveModsBoldMenuItem.Down = visible;

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

			ApplyGridFont(resolvedFontName);

			if (save && _viewModel?.Settings != null)
			{
				_viewModel.Settings.DockPanelLayouts[GridFontKey] = resolvedFontName;
				_viewModel.Settings.DockPanelLayouts[GridFontSizeKey] = FormatGridFontSize(resolvedFontSize);
				_viewModel.Settings.DockPanelLayouts[GridDensityKey] = resolvedDensity;
				_viewModel.Settings.Save();
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
			if (gridControl == null || gridView == null)
				return;

			_gridFontFamilyName = ResolveGridFontFamily(fontName);

			Font newRegularFont = new Font(
				_gridFontFamilyName,
				_gridFontSizePt,
				FontStyle.Regular,
				GraphicsUnit.Point);
			Font newBoldFont = new Font(
				_gridFontFamilyName,
				_gridFontSizePt,
				FontStyle.Bold,
				GraphicsUnit.Point);
			Font newUnderlineFont = new Font(
				_gridFontFamilyName,
				_gridFontSizePt,
				FontStyle.Underline,
				GraphicsUnit.Point);
			Font newBoldUnderlineFont = new Font(
				_gridFontFamilyName,
				_gridFontSizePt,
				FontStyle.Bold | FontStyle.Underline,
				GraphicsUnit.Point);
			Font newSecondaryFont = new Font(
				_gridFontFamilyName,
				GetSecondaryGridFontSize(_gridFontSizePt),
				FontStyle.Regular,
				GraphicsUnit.Point);
			Font newSecondaryBoldFont = new Font(
				_gridFontFamilyName,
				GetSecondaryGridFontSize(_gridFontSizePt),
				FontStyle.Bold,
				GraphicsUnit.Point);
			Font newHeaderFont = new Font(
				_gridFontFamilyName,
				_gridFontSizePt,
				FontStyle.Regular,
				GraphicsUnit.Point);
			Font newBadgeFont = new Font(
				_gridFontFamilyName,
				GetBadgeGridFontSize(_gridFontSizePt),
				FontStyle.Regular,
				GraphicsUnit.Point);
			Font oldRegularFont = _gridRegularFont;
			Font oldBoldFont = _gridBoldFont;
			Font oldUnderlineFont = _gridUnderlineFont;
			Font oldBoldUnderlineFont = _gridBoldUnderlineFont;
			Font oldSecondaryFont = _gridSecondaryFont;
			Font oldSecondaryBoldFont = _gridSecondaryBoldFont;
			Font oldHeaderFont = _gridHeaderFont;
			Font oldBadgeFont = _gridBadgeFont;

			_gridRegularFont = newRegularFont;
			_gridBoldFont = newBoldFont;
			_gridUnderlineFont = newUnderlineFont;
			_gridBoldUnderlineFont = newBoldUnderlineFont;
			_gridSecondaryFont = newSecondaryFont;
			_gridSecondaryBoldFont = newSecondaryBoldFont;
			_gridHeaderFont = newHeaderFont;
			_gridBadgeFont = newBadgeFont;

			_categoryTextSizeCache.Clear();

			gridControl.Font = _gridRegularFont;
			gridView.RowHeight = GetGridRowHeight(_gridDensity, _gridFontSizePt);
			gridView.ColumnPanelRowHeight = -1;
			gridView.Appearance.Row.Font = _gridRegularFont;
			gridView.Appearance.EvenRow.Font = _gridRegularFont;
			gridView.Appearance.OddRow.Font = _gridRegularFont;
			gridView.Appearance.FocusedRow.Font = _gridRegularFont;
			gridView.Appearance.SelectedRow.Font = _gridRegularFont;
			gridView.Appearance.HideSelectionRow.Font = _gridRegularFont;
			gridView.Appearance.HeaderPanel.Font = _gridHeaderFont;
			gridView.Appearance.FilterPanel.Font = _gridRegularFont;
			gridView.Appearance.GroupRow.Font = _gridRegularFont;

			gridView.LayoutChanged();
			gridView.InvalidateRows();

			DisposeFont(oldRegularFont);
			DisposeFont(oldBoldFont);
			DisposeFont(oldUnderlineFont);
			DisposeFont(oldBoldUnderlineFont);
			DisposeFont(oldSecondaryFont);
			DisposeFont(oldSecondaryBoldFont);
			DisposeFont(oldHeaderFont);
			DisposeFont(oldBadgeFont);
		}

		private static void DisposeFont(Font font)
		{
			if (font != null)
				font.Dispose();
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
			gridView.OptionsView.ShowGroupPanel = false;
			gridView.OptionsView.ShowIndicator = false;
			gridView.OptionsView.ShowVerticalLines = DefaultBoolean.False;
			gridView.OptionsView.EnableAppearanceEvenRow = true;
			gridView.OptionsView.EnableAppearanceOddRow = true;

			// Editing is enabled only so explicit mod renames can use a native cell editor.
			gridView.OptionsBehavior.Editable = true;
			gridView.OptionsBehavior.ReadOnly = false;
			gridView.OptionsBehavior.EditorShowMode = EditorShowMode.Click;
			gridView.OptionsSelection.MultiSelect = true;
			gridView.OptionsSelection.MultiSelectMode = GridMultiSelectMode.RowSelect;
			gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
			// The focused row is also the most recently selected row. Disabling its appearance hides
			// selection on uninstalled mods because they do not receive a custom installed-state colour.
			gridView.OptionsSelection.EnableAppearanceFocusedRow = true;
			gridView.OptionsCustomization.AllowColumnMoving = true;
			gridView.OptionsCustomization.AllowColumnResizing = true;
			gridView.OptionsCustomization.AllowSort = true;
			gridView.OptionsFind.AlwaysVisible = false;
			gridView.OptionsView.BestFitMaxRowCount = 50;
			gridView.OptionsView.ColumnAutoWidth = false;
			gridView.OptionsView.ShowColumnHeaders = true;
			DevExpressGridLayoutPersistence.ConfigureSessionOnlyFilters(gridView);
			gridView.OptionsView.ShowAutoFilterRow = true;
			gridView.OptionsView.ShowButtonMode = DevExpress.XtraGrid.Views.Base.ShowButtonModeEnum.ShowForFocusedRow;

			gridControl.DataSource = _modList;

			gridView.Columns.Clear();
			BuildColumns();
			ApplyAutoFilterDefaults();
			ApplyDateSortDefaults();

			_lastFindPanelVisible = gridView.IsFindPanelVisible;
			gridView.Layout += GridView_Layout;
			gridView.CustomUnboundColumnData += GridView_CustomUnboundColumnData;
			gridView.ShowingEditor += GridView_ShowingEditor;
			gridView.ShownEditor += GridView_ShownEditor;
			gridView.HiddenEditor += GridView_HiddenEditor;
			gridView.RowCellStyle += GridView_RowCellStyle;
			gridView.RowCellClick += GridView_RowCellClick;
			gridView.MouseDown += GridView_MouseDown;
			gridView.DoubleClick += GridView_DoubleClick;
			gridView.KeyDown += GridView_KeyDown;
			gridView.FocusedRowChanged += (s, e) => SetCommandExecutableStatus();
			gridView.SelectionChanged += (s, e) => SetCommandExecutableStatus();
			gridView.CustomDrawCell += GridView_CustomDrawCell;
			gridView.CustomDrawColumnHeader += GridView_CustomDrawColumnHeader;
			gridView.CustomColumnSort += GridView_CustomColumnSort;
			gridView.GroupRowExpanded += (s, e) => QueueGridLayoutSave();
			gridView.GroupRowCollapsed += (s, e) => QueueGridLayoutSave();
			gridView.ColumnWidthChanged += (s, e) => QueueGridLayoutSave();
			gridView.EndSorting += GridView_EndSorting;
		}

		private void GridView_Layout(object sender, EventArgs e)
		{
			if (_restoringGridLayout ||
				_restoringFindPanelVisibility ||
				_viewModel?.Settings == null)
			{
				return;
			}

			bool visible = gridView.IsFindPanelVisible;

			if (visible == _lastFindPanelVisible)
				return;

			_lastFindPanelVisible = visible;

			_viewModel.Settings.DockPanelLayouts[
				GridFindPanelVisibleKey] = visible.ToString();

			_viewModel.Settings.Save();
		}

		private void RestoreFindPanelVisibility()
		{
			if (_viewModel?.Settings == null)
				return;

			bool visible = gridView.IsFindPanelVisible;

			if (_viewModel.Settings.DockPanelLayouts.ContainsKey(
					GridFindPanelVisibleKey))
			{
				bool persistedVisibility;

				if (Boolean.TryParse(
						_viewModel.Settings.DockPanelLayouts[
							GridFindPanelVisibleKey],
						out persistedVisibility))
				{
					visible = persistedVisibility;
				}
			}

			_restoringFindPanelVisibility = true;

			try
			{
				// Never allow the serialized grid layout to make it permanently
				// visible and prevent the user's Hide action from taking effect.
				gridView.OptionsFind.AlwaysVisible = false;

				if (visible)
					gridView.ShowFindPanel();
				else
					gridView.HideFindPanel();

				_lastFindPanelVisible = visible;
			}
			finally
			{
				_restoringFindPanelVisibility = false;
			}
		}

		private void BuildColumns()
		{
			AddCol(ColModStatus, "Status", HorzAlignment.Center, true); GridColumn modNameCol = AddCol(ColModName, "MOD NAME", HorzAlignment.Default, true); AddCol(ColVersion, "VERSION", HorzAlignment.Center, false); AddCol(ColLastKnown, "LATEST", HorzAlignment.Center, false); AddCol(ColAuthor, "AUTHOR", HorzAlignment.Default, false); AddCol(ColCategory, "CATEGORY", HorzAlignment.Default, false); AddCol(ColInstallDate, "INSTALL DATE", HorzAlignment.Center, false); AddCol(ColDownloadDate, "DOWNLOAD DATE", HorzAlignment.Center, false); AddCol(ColDownloadId, "DOWNLOAD ID", HorzAlignment.Center, false);
			ConfigureModNameRenameColumn(modNameCol);
			GridColumn endorsedCol = AddCol(ColEndorsed, "ENDORSED", HorzAlignment.Center, false); RepositoryItemPictureEdit picRepo = new RepositoryItemPictureEdit { ShowMenu = false, SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom, NullText = "", }; endorsedCol.ColumnEdit = picRepo; gridControl.RepositoryItems.Add(picRepo);
		}
		private GridColumn AddCol(string field, string caption, HorzAlignment align, bool pin)
		{
			ColumnSizingDefinition sizing = GetColumnSizingDefinition(field); GridColumn col = new GridColumn { FieldName = field, Caption = caption, Width = sizing.DefaultWidth, Fixed = pin ? FixedStyle.Left : FixedStyle.None, UnboundType = field == ColEndorsed ? DevExpress.Data.UnboundColumnType.Object : DevExpress.Data.UnboundColumnType.String, OptionsColumn = { AllowEdit = false, AllowSort = DefaultBoolean.True, ReadOnly = true, FixedWidth = false }, AppearanceHeader = { TextOptions = { HAlignment = align } }, AppearanceCell = { TextOptions = { HAlignment = align } }, }; ApplyColumnSizingDefinition(col, sizing); if (field == ColInstallDate || field == ColDownloadDate) col.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom; ApplyAutoFilterDefaults(col); gridView.Columns.Add(col); col.Visible = true; col.VisibleIndex = gridView.Columns.Count - 1; return col;
		}

		private void ConfigureModNameRenameColumn(GridColumn column)
		{
			if (column == null) return;
			column.ColumnEdit = _renameButtonEdit;
			column.OptionsColumn.AllowEdit = true;
			column.OptionsColumn.ReadOnly = false;
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
			if (e.IsSetData && e.Column.FieldName == ColModName)
			{
				CommitInlineRenameValue(e.ListSourceRowIndex, e.Value);
				return;
			}

			if (!e.IsGetData) return;
			int idx = e.ListSourceRowIndex;
			if (idx < 0 || idx >= _modList.Count) return;
			IMod mod = _modList[idx];
			switch (e.Column.FieldName)
			{
				case ColModStatus: e.Value = GetModStatusText(mod); break;
				case ColModName: e.Value = mod.ModName; break;
				case ColVersion: e.Value = mod.HumanReadableVersion; break;
				case ColLastKnown: e.Value = mod.LastKnownVersion; break;
				case ColAuthor: e.Value = mod.Author; break;
				case ColInstallDate: e.Value = mod.InstallDate; break;
				case ColDownloadDate: e.Value = mod.DownloadDate; break;
				case ColDownloadId: e.Value = mod.DownloadId; break;
				case ColCategory:
					e.Value = GetCachedCategoryName(mod);
					break;
				case ColEndorsed:
					e.Value = mod.IsEndorsed == true
						? _endorsedYesImage
						: mod.IsEndorsed == false
							? _endorsedNoImage
							: _endorsedEmptyImage;
					break;
			}
		}

		private string GetCachedCategoryName(IMod mod)
		{
			if (mod == null)
				return String.Empty;

			string categoryName;
			if (_categoryNameCache.TryGetValue(mod, out categoryName))
				return categoryName;

			if (_viewModel?.CategoryManager != null)
			{
				IModCategory category = _viewModel.CategoryManager.FindCategory(
					mod.CustomCategoryId >= 0
						? mod.CustomCategoryId
						: mod.CategoryId);
				categoryName = category?.CategoryName ?? String.Empty;
			}
			else
			{
				categoryName = Convert.ToString(
					mod.CategoryId,
					CultureInfo.InvariantCulture);
			}

			_categoryNameCache[mod] = categoryName;
			return categoryName;
		}

		// ── Grid event handlers ──────────────────────────────────────────────

		private void ClearGridStateCaches()
		{
			_activeModFileNames.Clear();
			_installedMods.Clear();
			_modVisualStatusCache.Clear();
			_outdatedModCache.Clear();
			_categoryNameCache.Clear();
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

		private bool IsModOutdated(IMod mod)
		{
			if (mod == null)
				return false;

			bool outdated;
			if (!_outdatedModCache.TryGetValue(mod, out outdated))
			{
				outdated = IsVersionOutdated(
					mod.HumanReadableVersion,
					mod.LastKnownVersion);
				_outdatedModCache[mod] = outdated;
			}

			return outdated;
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
		private void GridView_RowCellStyle(
			object sender,
			RowCellStyleEventArgs e)
		{
			if (_viewModel == null || e.RowHandle < 0)
				return;

			int sourceIndex = gridView.GetDataSourceRowIndex(e.RowHandle);
			if (sourceIndex < 0 || sourceIndex >= _modList.Count)
				return;

			IMod mod = _modList[sourceIndex];
			ModVisualStatus status = GetModVisualStatus(mod);
			bool isActive = status == ModVisualStatus.InstalledActive;
			bool isInstalled = status != ModVisualStatus.Uninstalled;
			bool isSelected =
				gridView.IsRowSelected(e.RowHandle) ||
				e.RowHandle == gridView.FocusedRowHandle;

			if (_usesLegacyLightRowPalette &&
				_showRowHighlights &&
				isActive)
			{
				e.Appearance.BackColor = isSelected
					? Color.FromArgb(218, 240, 218)
					: Color.FromArgb(249, 254, 249);
				e.Appearance.ForeColor = Color.Black;
			}
			else if (_usesLegacyLightRowPalette &&
					 _showRowHighlights &&
					 isInstalled)
			{
				e.Appearance.BackColor = isSelected
					? Color.FromArgb(250, 230, 200)
					: Color.FromArgb(255, 251, 244);
				e.Appearance.ForeColor = Color.Black;
			}

			if (_showActiveModsInBold && isActive)
				e.Appearance.Font = _gridBoldFont;

			if (e.Column.FieldName == ColLastKnown &&
				!String.IsNullOrEmpty(mod.LastKnownVersion))
			{
				bool outdated = IsModOutdated(mod);

				if (!isSelected)
				{
					e.Appearance.ForeColor = outdated
						? Color.FromArgb(200, 40, 40)
						: Color.FromArgb(37, 99, 235);
				}

				e.Appearance.Font = _showActiveModsInBold && isActive
					? _gridBoldUnderlineFont
					: _gridUnderlineFont;
			}

			bool isSecondaryColumn =
				e.Column.FieldName == ColInstallDate ||
				e.Column.FieldName == ColDownloadDate ||
				e.Column.FieldName == ColDownloadId;

			if (!isSelected && isSecondaryColumn)
			{
				if (_usesLegacyLightRowPalette)
					e.Appearance.ForeColor = Color.FromArgb(90, 90, 90);

				e.Appearance.Font = _showActiveModsInBold && isActive
					? _gridSecondaryBoldFont
					: _gridSecondaryFont;
			}
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

			IMod mod = _modList[src];
			string gameDomain = _viewModel?.ModRepository?.GameDomainName;
			Uri url = NexusModLinkParser.ResolveNavigationUri(mod.Website, gameDomain, mod.Id, mod.DownloadId);
			if (url == null) return;

			try { System.Diagnostics.Process.Start(url.ToString()); }
			catch { /* ignore launch errors */ }
		}

		/// <summary>
		/// Starts inline rename only when the focused row's rename button is clicked, while allowing ordinary
		/// clicks and double-clicks on the mod name text to remain grid operations.
		/// </summary>
		private void GridView_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left || _renamingModName) return;

			GridView view = sender as GridView;
			if (view == null) return;

			GridHitInfo hit = view.CalcHitInfo(e.Location);
			if (!hit.InRowCell || !IsDataRowHandle(hit.RowHandle) || hit.Column == null || hit.Column.FieldName != ColModName) return;

			// ShowButtonMode is configured for the focused row, so a click on any other row must only select it.
			if (view.FocusedRowHandle != hit.RowHandle) return;

			view.FocusedColumn = hit.Column;
			_testingRenameButtonHit = true;
			try
			{
				view.ShowEditor();
				ButtonEdit editor = view.ActiveEditor as ButtonEdit;
				ButtonEditViewInfo editorViewInfo = editor?.GetViewInfo() as ButtonEditViewInfo;
				if (editor == null || editorViewInfo == null) return;

				Point screenPoint = view.GridControl.PointToScreen(e.Location);
				EditHitInfo editorHit = editorViewInfo.CalcHitInfo(editor.PointToClient(screenPoint));
				EditorButtonObjectInfoArgs buttonInfo = editorHit.HitTest == EditHitTest.Button
					? editorHit.HitObject as EditorButtonObjectInfoArgs
					: null;
				EditorButton button = buttonInfo?.Button;
				if (button == null || !String.Equals(button.Tag as string, RenameButtonActionRename, StringComparison.Ordinal)) return;

				DevExpress.Utils.DXMouseEventArgs dxMouseEvent = e as DevExpress.Utils.DXMouseEventArgs;
				if (dxMouseEvent != null) dxMouseEvent.Handled = true;

				view.HideEditor();
				BeginInvoke((MethodInvoker)(() => StartInlineRename(hit.RowHandle)));
			}
			finally
			{
				_testingRenameButtonHit = false;
				if (!_renamingModName && view.ActiveEditor != null)
					view.HideEditor();
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
					drawsLatestWarning = IsModOutdated(_modList[src]);
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
				int y = e.Bounds.Top + (e.Bounds.Height - icon.Height) / 2;
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
			string localNorm = local.TrimStart('v', 'V').Trim();
			string latestNorm = latest.TrimStart('v', 'V').Trim();
			if (Version.TryParse(localNorm, out Version localV) &&
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
						Alignment = StringAlignment.Center,
						LineAlignment = StringAlignment.Center,
					};
					g.DrawString("!", font, white, new RectangleF(0f, 2f, sz, sz - 2f), sf);
				}
			}
			return _warningIcon = bmp;
		}

		/// <summary>Highlights the active sort column header in blue.</summary>
		private void GridView_CustomDrawColumnHeader(object sender, ColumnHeaderCustomDrawEventArgs e)
		{
			if (!_usesLegacyLightRowPalette ||
				e.Column == null ||
				e.Column.SortOrder == DevExpress.Data.ColumnSortOrder.None)
			{
				return;
			}

			e.Appearance.BackColor = Color.FromArgb(219, 234, 254);
			e.Appearance.BackColor2 = Color.FromArgb(219, 234, 254);
			e.Appearance.ForeColor = Color.FromArgb(37, 99, 235);

			e.DefaultDraw();
			e.Handled = true;
		}

		/// <summary>Draws a coloured pill badge for the category cell.</summary>
		private void DrawCategoryBadge(
			DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
		{
			if (e.RowHandle < 0 || !_showColouredCategories)
				return;

			int sourceIndex = gridView.GetDataSourceRowIndex(e.RowHandle);
			if (sourceIndex < 0 || sourceIndex >= _modList.Count)
				return;

			// Do not depend on e.DisplayText here. During row invalidation (for
			// example when the mouse enters/leaves the Mod Name cell), DevExpress
			// may repaint the category cell before its unbound display text has
			// been refreshed. Resolve the stable cached value from the row instead.
			string categoryName = GetCachedCategoryName(_modList[sourceIndex]);

			string originalDisplayText = e.DisplayText;
			e.DisplayText = String.Empty;
			e.DefaultDraw();
			e.DisplayText = originalDisplayText;

			if (String.IsNullOrEmpty(categoryName) || _gridBadgeFont == null)
			{
				e.Handled = true;
				return;
			}

			const int horizontalPadding = 6;
			const int verticalPadding = 2;
			const int radius = 4;

			Size textSize = GetCachedCategoryTextSize(categoryName);
			int availableWidth = Math.Max(0, e.Bounds.Width - 4);
			int availableHeight = Math.Max(0, e.Bounds.Height - 2);
			int badgeWidth = Math.Min(
				textSize.Width + horizontalPadding * 2,
				availableWidth);
			int badgeHeight = Math.Min(
				textSize.Height + verticalPadding * 2,
				availableHeight);

			if (badgeWidth <= 0 || badgeHeight <= 0)
			{
				e.Handled = true;
				return;
			}

			Rectangle badgeBounds = new Rectangle(
				e.Bounds.Left + (e.Bounds.Width - badgeWidth) / 2,
				e.Bounds.Top + (e.Bounds.Height - badgeHeight) / 2,
				badgeWidth,
				badgeHeight);

			Color badgeColor = GetCachedCategoryColor(categoryName);
			SolidBrush badgeBrush = GetCachedCategoryBrush(badgeColor);

			SmoothingMode originalSmoothingMode = e.Graphics.SmoothingMode;
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (GraphicsPath path = GetRoundedRectPath(badgeBounds, radius))
				e.Graphics.FillPath(badgeBrush, path);
			e.Graphics.SmoothingMode = originalSmoothingMode;

			TextRenderer.DrawText(
				e.Graphics,
				categoryName,
				_gridBadgeFont,
				badgeBounds,
				Color.White,
				TextFormatFlags.HorizontalCenter |
				TextFormatFlags.VerticalCenter |
				TextFormatFlags.EndEllipsis |
				TextFormatFlags.SingleLine |
				TextFormatFlags.NoPadding);

			e.Handled = true;
		}

		private Size GetCachedCategoryTextSize(string categoryName)
		{
			Size size;
			if (!_categoryTextSizeCache.TryGetValue(categoryName, out size))
			{
				size = TextRenderer.MeasureText(
					categoryName,
					_gridBadgeFont,
					new Size(Int32.MaxValue, Int32.MaxValue),
					TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
				_categoryTextSizeCache[categoryName] = size;
			}

			return size;
		}

		private Color GetCachedCategoryColor(string categoryName)
		{
			Color color;
			if (!_categoryColorCache.TryGetValue(categoryName, out color))
			{
				color = GetCategoryColor(categoryName);
				_categoryColorCache[categoryName] = color;
			}

			return color;
		}

		private SolidBrush GetCachedCategoryBrush(Color color)
		{
			int key = color.ToArgb();
			SolidBrush brush;
			if (!_categoryBrushCache.TryGetValue(key, out brush))
			{
				brush = new SolidBrush(color);
				_categoryBrushCache[key] = brush;
			}

			return brush;
		}

		private static GraphicsPath GetRoundedRectPath(Rectangle r, int radius)
		{
			int d = radius * 2;
			var path = new GraphicsPath();
			path.AddArc(r.Left, r.Top, d, d, 180, 90);
			path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
			path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
			path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
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
			if (n.Contains("armor") || n.Contains("armour") || n.Contains("weapon"))
				return Color.FromArgb(139, 92, 246);   // violet  — armour / weapons
			if (n.Contains("bug") || n.Contains("fix") || n.Contains("patch"))
				return Color.FromArgb(59, 130, 246);   // blue    — bug fixes / patches
			if (n.Contains("body") || n.Contains("face") || n.Contains("skin") ||
				n.Contains("hair") || n.Contains("race"))
				return Color.FromArgb(236, 72, 153);   // pink    — body / face / skin
			if (n.Contains("follower") || n.Contains("companion") || n.Contains("npc"))
				return Color.FromArgb(20, 184, 166);   // teal    — followers / companions
			if (n.Contains("creature") || n.Contains("animal") || n.Contains("monster") ||
				n.Contains("beast"))
				return Color.FromArgb(245, 158, 11);   // amber   — creatures / animals

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
		private static ColumnSizingDefinition GetColumnSizingDefinition(string fieldName)
		{
			foreach (ColumnSizingDefinition definition in GridColumnSizingDefinitions) if (string.Equals(definition.FieldName, fieldName, StringComparison.Ordinal)) return definition;
			throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "A sizing definition is required for every mod grid column.");
		}
		private static void ApplyColumnSizingDefinition(GridColumn column, ColumnSizingDefinition definition) { column.MinWidth = definition.MinimumWidth; column.MaxWidth = definition.MaximumWidth; column.Width = definition.DefaultWidth; }
		private void ApplyDefaultColumnSizing() { gridView.BeginUpdate(); try { foreach (GridColumn column in gridView.Columns) ApplyColumnSizingDefinition(column, GetColumnSizingDefinition(column.FieldName)); } finally { gridView.EndUpdate(); } }
		private void BestFitColumn(GridColumn column)
		{
			if (column == null) return;
			ColumnSizingDefinition definition = GetColumnSizingDefinition(column.FieldName); gridView.BeginUpdate(); try { column.BestFit(); int width = Math.Max(definition.MinimumWidth, column.Width); if (definition.MaximumWidth > 0) width = Math.Min(definition.MaximumWidth, width); column.Width = width; } finally { gridView.EndUpdate(); }
			SaveGridLayout();
		}
		// Keep the grid layout with the existing UI layout settings so Reset UI can clear it.
		private bool RestoreGridLayout()
		{
			if (_viewModel?.Settings == null)
			{
				return false;
			}

			bool restored = false;
			_restoringGridLayout = true;
			try
			{
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
							restored = true;
						}
					}
				}
				catch
				{
					_viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
				}

				DevExpressGridLayoutPersistence.ClearTransientFilters(gridView);
				gridView.OptionsView.ShowColumnHeaders = true;
				gridView.ColumnPanelRowHeight = -1;
				if (_gridHeaderFont != null)
					gridView.Appearance.HeaderPanel.Font = _gridHeaderFont;
				if (_viewModel.Settings.DockPanelLayouts.ContainsKey(GridColumnWidthsKey))
					DevExpressGridLayoutPersistence.RestoreColumnWidths(gridView, _viewModel.Settings.DockPanelLayouts[GridColumnWidthsKey]);

				ApplyAutoFilterDefaults();
				ApplyDateSortDefaults();
				return restored;
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

			_restoringGridSort = true;
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
				_lastGridSortSignature = GetGridSortSignature();
				_restoringGridSort = false;
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

			tsbSwitchView.Caption = _categoryViewActive ? "Switch to Default View" : "Switch to Category View";
			tsbSwitchView.Hint = _categoryViewActive ? "Show the default flat mod list" : "Group the mod list by category";
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

		private void GridLayoutSaveTimer_Tick(object sender, EventArgs e)
		{
			_gridLayoutSaveTimer.Stop();
			SaveGridLayout();
		}

		private void SaveGridLayout()
		{
			if (_restoringGridLayout || _viewModel?.Settings == null)
			{
				return;
			}

			_gridLayoutSaveTimer?.Stop();

			bool findPanelVisible = gridView.IsFindPanelVisible;

			gridView.OptionsFind.AlwaysVisible = false;

			try
			{
				using (var stream = new MemoryStream())
				{
					gridView.SaveLayoutToStream(stream);
					_viewModel.Settings.DockPanelLayouts[GridLayoutKey] = Encoding.UTF8.GetString(stream.ToArray());
				}
				_viewModel.Settings.DockPanelLayouts[GridColumnWidthsKey] = DevExpressGridLayoutPersistence.SerializeColumnWidths(gridView);
			}
			catch
			{
				_viewModel.Settings.DockPanelLayouts.Remove(GridLayoutKey);
				_viewModel.Settings.DockPanelLayouts.Remove(GridColumnWidthsKey);
			}

			_viewModel.Settings.DockPanelLayouts[GridFindPanelVisibleKey] = findPanelVisible.ToString();

			SaveGridSort();
			SaveGridCategoryState();
			_viewModel.Settings.Save();
		}

		private void SaveGridSort()
		{
			string sortSignature = GetGridSortSignature();
			if (string.IsNullOrEmpty(sortSignature))
				_viewModel.Settings.DockPanelLayouts.Remove(GridSortKey);
			else
				_viewModel.Settings.DockPanelLayouts[GridSortKey] = sortSignature;
		}

		private string GetGridSortSignature()
		{
			List<string> parts = new List<string>();
			foreach (GridColumnSortInfo sortInfo in gridView.SortInfo)
			{
				if (sortInfo.Column == null || sortInfo.SortOrder == DevExpress.Data.ColumnSortOrder.None) continue;
				parts.Add(sortInfo.Column.FieldName + "|" + sortInfo.SortOrder);
			}
			return string.Join(";", parts);
		}
		protected override void OnClosed(EventArgs e)
		{
			_gridLayoutSaveTimer?.Stop();
			SaveGridLayout();
			base.OnClosed(e);
		}

		private void DisposePerformanceResources()
		{
			if (_gridLayoutSaveTimer != null)
			{
				_gridLayoutSaveTimer.Stop();
				_gridLayoutSaveTimer.Tick -= GridLayoutSaveTimer_Tick;
			}

			DisposeFont(_gridRegularFont);
			DisposeFont(_gridBoldFont);
			DisposeFont(_gridUnderlineFont);
			DisposeFont(_gridBoldUnderlineFont);
			DisposeFont(_gridSecondaryFont);
			DisposeFont(_gridSecondaryBoldFont);
			DisposeFont(_gridHeaderFont);
			DisposeFont(_gridBadgeFont);

			_gridRegularFont = null;
			_gridBoldFont = null;
			_gridUnderlineFont = null;
			_gridBoldUnderlineFont = null;
			_gridSecondaryFont = null;
			_gridSecondaryBoldFont = null;
			_gridHeaderFont = null;
			_gridBadgeFont = null;

			_endorsedYesImage?.Dispose();
			_endorsedNoImage?.Dispose();
			_endorsedEmptyImage?.Dispose();
			_modInstalledDisabledIcon?.Dispose();
			_modInstalledActiveIcon?.Dispose();
			_warningIcon?.Dispose();
			_inlineEditIcon?.Dispose();
			_inlineAcceptIcon?.Dispose();
			_inlineCancelIcon?.Dispose();

			_endorsedYesImage = null;
			_endorsedNoImage = null;
			_endorsedEmptyImage = null;
			_modInstalledDisabledIcon = null;
			_modInstalledActiveIcon = null;
			_warningIcon = null;
			_inlineEditIcon = null;
			_inlineAcceptIcon = null;
			_inlineCancelIcon = null;

			ClearGridPopupItems();
			_gridPopupMenu?.Dispose();
			_gridPopupMenu = null;
			ClearPopupMenuItems(popupDeactivate);

			foreach (SolidBrush brush in _categoryBrushCache.Values)
				brush.Dispose();
			_categoryBrushCache.Clear();
			_categoryColorCache.Clear();
			_categoryTextSizeCache.Clear();
			_categoryNameCache.Clear();
			_outdatedModCache.Clear();
		}

		private enum InlineEditGlyph
		{
			Pencil,
			Accept,
			Cancel,
		}

		private void InitializeInlineRenameEditor()
		{
			_renameButtonEdit = new RepositoryItemButtonEdit
			{
				AutoHeight = false,
			};
			_renameButtonEdit.ButtonClick += RenameButtonEdit_ButtonClick;
			ConfigureRenameEditorButtons(false);
			gridControl.RepositoryItems.Add(_renameButtonEdit);
		}

		private void ConfigureRenameEditorButtons(bool editing)
		{
			if (_renameButtonEdit == null) return;

			_renameButtonEdit.Buttons.Clear();
			_renameButtonEdit.TextEditStyle = editing ? TextEditStyles.Standard : TextEditStyles.DisableTextEditor;
			if (editing)
			{
				_renameButtonEdit.Buttons.Add(CreateRenameEditorButton(InlineEditGlyph.Accept, RenameButtonActionAccept, "Accept rename"));
				_renameButtonEdit.Buttons.Add(CreateRenameEditorButton(InlineEditGlyph.Cancel, RenameButtonActionCancel, "Cancel rename"));
			}
			else
			{
				_renameButtonEdit.Buttons.Add(CreateRenameEditorButton(InlineEditGlyph.Pencil, RenameButtonActionRename, "Rename mod"));
			}
		}

		private EditorButton CreateRenameEditorButton(InlineEditGlyph glyph, string action, string toolTip)
		{
			var button = new EditorButton(ButtonPredefines.Glyph)
			{
				Tag = action,
				ToolTip = toolTip,
			};
			button.ImageOptions.Image = GetInlineEditIcon(glyph);
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
			if (!IsModArchiveMissing(mod)) return;

			e.DefaultDraw();
			Bitmap warningIcon = GetWarningIcon();
			int x = e.Bounds.Right - warningIcon.Width - 5;
			int y = e.Bounds.Top + (e.Bounds.Height - warningIcon.Height) / 2;
			e.Graphics.DrawImage(warningIcon, x, y, warningIcon.Width, warningIcon.Height);
			e.Handled = true;
		}

		private bool StartInlineRename(int rowHandle)
		{
			int src = gridView.GetDataSourceRowIndex(rowHandle);
			if (_viewModel == null || src < 0 || src >= _modList.Count) return false;

			GridColumn column = gridView.Columns[ColModName];
			if (column == null) return false;

			_renameMod = _modList[src];
			_renameOriginalName = _renameMod.ModName ?? string.Empty;
			_renameRowHandle = rowHandle;
			_renamingModName = true;
			_cancelRenameEdit = false;
			_refreshAfterRename = false;
			_suppressNextDoubleClick = true;
			gridControl.Cursor = Cursors.Default;

			ConfigureRenameEditorButtons(true);
			gridView.MakeRowVisible(rowHandle, false);
			gridView.FocusedRowHandle = rowHandle;
			gridView.FocusedColumn = column;
			gridView.ShowEditor();
			if (gridView.ActiveEditor == null)
			{
				EndInlineRename();
				return false;
			}

			return true;
		}

		private void GridView_ShowingEditor(object sender, CancelEventArgs e)
		{
			if (gridView.FocusedRowHandle == DevExpress.XtraGrid.GridControl.AutoFilterRowHandle)
			{
				e.Cancel = false;
				return;
			}

			if (gridView.FocusedColumn == null || gridView.FocusedColumn.FieldName != ColModName || !IsDataRowHandle(gridView.FocusedRowHandle))
			{
				e.Cancel = true;
				return;
			}

			if (_testingRenameButtonHit)
			{
				e.Cancel = false;
				return;
			}

			e.Cancel = !_renamingModName || gridView.FocusedRowHandle != _renameRowHandle;
		}

		private void GridView_ShownEditor(object sender, EventArgs e)
		{
			_renameActiveEditor = gridView.ActiveEditor as Control;
			if (_renameActiveEditor != null)
				_renameActiveEditor.KeyDown += RenameEditor_KeyDown;

			ButtonEdit buttonEdit = gridView.ActiveEditor as ButtonEdit;
			if (!_renamingModName)
			{
				if (buttonEdit != null)
					buttonEdit.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
				return;
			}

			if (buttonEdit != null)
			{
				buttonEdit.Properties.TextEditStyle = TextEditStyles.Standard;
				buttonEdit.Properties.Buttons.Clear();
				buttonEdit.Properties.Buttons.Add(CreateRenameEditorButton(InlineEditGlyph.Accept, RenameButtonActionAccept, "Accept rename"));
				buttonEdit.Properties.Buttons.Add(CreateRenameEditorButton(InlineEditGlyph.Cancel, RenameButtonActionCancel, "Cancel rename"));
			}

			TextEdit textEdit = gridView.ActiveEditor as TextEdit;
			if (textEdit != null)
				textEdit.SelectAll();
		}

		private void GridView_HiddenEditor(object sender, EventArgs e)
		{
			if (_renameActiveEditor != null)
			{
				_renameActiveEditor.KeyDown -= RenameEditor_KeyDown;
				_renameActiveEditor = null;
			}

			if (!_renamingModName) return;

			int rowHandle = _renameRowHandle;
			bool refresh = _refreshAfterRename;
			EndInlineRename();

			if (refresh)
				gridControl.RefreshDataSource();
			if (rowHandle >= 0)
				gridView.InvalidateRow(rowHandle);
		}

		private void RenameEditor_KeyDown(object sender, KeyEventArgs e)
		{
			if (!_renamingModName)
			{
				if (e.KeyCode == Keys.F2)
				{
					int rowHandle = GetKeyboardRenameRowHandle();
					if (rowHandle >= 0)
					{
						e.Handled = true;
						e.SuppressKeyPress = true;
						gridView.HideEditor();
						BeginInvoke((MethodInvoker)(() => StartInlineRename(rowHandle)));
					}
				}
				else if (e.KeyCode == Keys.Escape)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;
					gridView.HideEditor();
				}
				return;
			}

			if (e.KeyCode == Keys.Enter)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				CommitActiveInlineRename();
			}
			else if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				CancelActiveInlineRename();
			}
		}

		private void RenameButtonEdit_ButtonClick(object sender, ButtonPressedEventArgs e)
		{
			string action = e.Button.Tag as string;
			if (String.Equals(action, RenameButtonActionAccept, StringComparison.Ordinal))
			{
				CommitActiveInlineRename();
				return;
			}

			if (String.Equals(action, RenameButtonActionCancel, StringComparison.Ordinal))
			{
				CancelActiveInlineRename();
				return;
			}

			if (String.Equals(action, RenameButtonActionRename, StringComparison.Ordinal))
			{
				int rowHandle = gridView.FocusedRowHandle;
				gridView.HideEditor();
				BeginInvoke((MethodInvoker)(() => StartInlineRename(rowHandle)));
			}
		}

		private void CommitActiveInlineRename()
		{
			if (!_renamingModName) return;
			gridView.PostEditor();
			gridView.CloseEditor();
		}

		private void CancelActiveInlineRename()
		{
			if (!_renamingModName) return;
			_cancelRenameEdit = true;
			gridView.HideEditor();
		}

		private void CommitInlineRenameValue(int listSourceRowIndex, object value)
		{
			if (!_renamingModName || _cancelRenameEdit) return;

			IMod mod = _renameMod;
			if (mod == null && listSourceRowIndex >= 0 && listSourceRowIndex < _modList.Count)
				mod = _modList[listSourceRowIndex];

			string newName = (value == null ? string.Empty : value.ToString()).Trim();
			if (mod != null && !string.IsNullOrEmpty(newName) &&
				!string.Equals(newName, _renameOriginalName, StringComparison.Ordinal))
			{
				_viewModel.UpdateModName(mod, newName);
				_refreshAfterRename = true;
			}
		}

		private void EndInlineRename()
		{
			if (_renameActiveEditor != null)
			{
				_renameActiveEditor.KeyDown -= RenameEditor_KeyDown;
				_renameActiveEditor = null;
			}

			_renameMod = null;
			_renameOriginalName = null;
			_renameRowHandle = DevExpress.XtraGrid.GridControl.InvalidRowHandle;
			_renamingModName = false;
			_cancelRenameEdit = false;
			_refreshAfterRename = false;
			_suppressNextDoubleClick = false;
			ConfigureRenameEditorButtons(false);
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
			if (_renamingModName) return;

			var info = gridView.CalcHitInfo(gridView.GridControl.PointToClient(Control.MousePosition));
			if (info.HitTest == GridHitTest.ColumnEdge && info.Column != null)
			{
				BestFitColumn(info.Column);
				return;
			}

			if (info.InRow || info.InRowCell)
				ToggleSelectedMod();
		}

		private void GridView_KeyDown(object sender, KeyEventArgs e)
		{
			if (_renamingModName) return;
			if (e.KeyCode == Keys.F2 && TryStartModNameRenameFromKeyboard()) { e.Handled = true; return; }
			if (e.KeyCode == Keys.Return) { e.Handled = true; ToggleSelectedMod(); return; }
			if (e.KeyCode == Keys.Delete) { e.Handled = true; DeleteSelectedModsFromKey(); return; }
			if (e.KeyData == (Keys.Control | Keys.F)) { SetTextBoxFocus?.Invoke(this, e); return; }

			if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.Home)
			{
				int rowHandle = GetFirstVisibleDataRowHandle();
				if (rowHandle >= 0) { FocusGridRow(rowHandle); e.Handled = true; }
				return;
			}
			if (!e.Control && !e.Alt && !e.Shift && e.KeyCode == Keys.End)
			{
				int rowHandle = GetLastVisibleDataRowHandle();
				if (rowHandle >= 0) { FocusGridRow(rowHandle); e.Handled = true; }
				return;
			}
			if (!e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
			{
				char letter = (char)e.KeyCode;
				if (NavigateToModByLetter(letter))
					e.Handled = true;
			}
		}

		private bool TryStartModNameRenameFromKeyboard()
		{
			int rowHandle = GetKeyboardRenameRowHandle();
			return rowHandle >= 0 && StartInlineRename(rowHandle);
		}

		private int GetKeyboardRenameRowHandle()
		{
			int rowHandle = GetSelectedModNameRowHandle();
			return rowHandle >= 0 ? rowHandle : GetHoveredModNameRowHandle();
		}

		private int GetSelectedModNameRowHandle()
		{
			int[] rows = gridView.GetSelectedRows();
			if (rows == null || rows.Length == 0)
				return DevExpress.XtraGrid.GridControl.InvalidRowHandle;

			int focusedRowHandle = gridView.FocusedRowHandle;
			foreach (int rowHandle in rows)
			{
				if (rowHandle == focusedRowHandle && IsDataRowHandle(rowHandle))
					return rowHandle;
			}

			foreach (int rowHandle in rows)
			{
				if (IsDataRowHandle(rowHandle))
					return rowHandle;
			}

			return DevExpress.XtraGrid.GridControl.InvalidRowHandle;
		}

		private bool TryStartHoveredModNameRename()
		{
			int rowHandle = GetHoveredModNameRowHandle();
			return rowHandle >= 0 && StartInlineRename(rowHandle);
		}

		private int GetHoveredModNameRowHandle()
		{
			Point clientPoint = gridControl.PointToClient(Control.MousePosition);
			var hit = gridView.CalcHitInfo(clientPoint);
			if (!hit.InRowCell || !IsDataRowHandle(hit.RowHandle) || hit.Column == null || hit.Column.FieldName != ColModName)
				return DevExpress.XtraGrid.GridControl.InvalidRowHandle;

			return hit.RowHandle;
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

		// ── keyboard navigation & row focus helpers ─────────────────────────────────────

		private void FocusGridRow(int rowHandle)
		{
			if (rowHandle < 0) return;
			gridView.ClearSelection();
			gridView.FocusedRowHandle = rowHandle;
			gridView.SelectRow(rowHandle);
			gridView.MakeRowVisible(rowHandle, false);
		}

		private int GetFirstVisibleDataRowHandle()
		{
			for (int i = 0; i < gridView.RowCount; i++)
			{
				int h = gridView.GetVisibleRowHandle(i);
				if (h >= 0 && !gridView.IsGroupRow(h))
					return h;
			}
			return DevExpress.XtraGrid.GridControl.InvalidRowHandle;
		}

		private int GetLastVisibleDataRowHandle()
		{
			for (int i = gridView.RowCount - 1; i >= 0; i--)
			{
				int h = gridView.GetVisibleRowHandle(i);
				if (h >= 0 && !gridView.IsGroupRow(h))
					return h;
			}
			return DevExpress.XtraGrid.GridControl.InvalidRowHandle;
		}

		/// <summary>
		/// Focuses the first visible data row whose mod name begins with <paramref name="letter"/>.
		/// Group rows are skipped. Returns true when a match is found.
		/// </summary>
		private bool NavigateToModByLetter(char letter)
		{
			for (int i = 0; i < gridView.RowCount; i++)
			{
				int rowHandle = gridView.GetVisibleRowHandle(i);
				if (rowHandle < 0 || gridView.IsGroupRow(rowHandle)) continue;
				int src = gridView.GetDataSourceRowIndex(rowHandle);
				if (src < 0 || src >= _modList.Count) continue;
				string modName = _modList[src].ModName;
				if (!string.IsNullOrEmpty(modName) && char.ToUpperInvariant(modName[0]) == letter)
				{
					FocusGridRow(rowHandle);
					return true;
				}
			}
			return false;
		}

		// ── column auto-fit toggle ─────────────────────────────────────────────────────

		/// <summary>
		/// Toggles auto-fit for <paramref name="column"/>. First double-click saves the current
		/// width and applies BestFit; a second double-click restores the saved width.
		/// </summary>
		private void GridView_EndSorting(object sender, EventArgs e)
		{
			string sortSignature = GetGridSortSignature();
			bool sortChanged = !string.Equals(_lastGridSortSignature, sortSignature, StringComparison.Ordinal);
			_lastGridSortSignature = sortSignature;

			QueueGridLayoutSave();
			if (_focusTopRowAfterSorting && sortChanged && !_restoringGridLayout && !_restoringGridSort)
			{
				int rowHandle = GetFirstVisibleDataRowHandle();
				if (rowHandle >= 0)
					FocusGridRow(rowHandle);
			}
		}

		private void SetFocusTopRowAfterSorting(bool enabled, bool save)
		{
			_focusTopRowAfterSorting = enabled;
			if (_focusTopRowAfterSortingMenuItem != null)
				_focusTopRowAfterSortingMenuItem.Down = enabled;
			SaveGridDisplayOption(GridFocusTopAfterSortKey, enabled, save);
		}

		private void SetFocusTopRowAfterInstallDateChange(bool enabled, bool save)
		{
			_focusTopRowAfterInstallDateChange = enabled;
			if (_focusTopRowAfterInstallDateChangeMenuItem != null)
				_focusTopRowAfterInstallDateChangeMenuItem.Down = enabled;
			SaveGridDisplayOption(GridFocusTopAfterInstallDateChangeKey, enabled, save);
		}

		/// <summary>
		/// Rebuilds the visible Mod Manager toolbar in the canonical order after all dynamic
		/// toolbar items have been created.
		/// </summary>
		private void RebuildToolbarLinks()
		{
			barModActions.ClearLinks();

			AddToolbarLink(tsbAddMod);
			AddToolbarLink(tsbActivate);
			AddToolbarLink(tsbDeactivate);
			AddToolbarLink(_toolbarSeparatorAfterDisable);
			AddToolbarLink(tsbTagMod);
			AddToolbarLink(tsbModOnlineChecks);
			AddToolbarLink(tsbToggleEndorse);
			AddToolbarLink(_toolbarSeparatorAfterEndorse);
			AddToolbarLink(tsbResetCategories);
			AddToolbarLink(tsbSwitchView);
			AddToolbarLink(_toolbarSeparatorAfterCategoryView);
			AddToolbarLink(tsbShowUpdatesOnly);
			AddToolbarLink(tsbExportModList);
			AddToolbarLink(tsbSkyrimDownloads);
			AddToolbarLink(_toolbarPositionButton);
			AddToolbarLink(_displayOptionsButton);
		}

		/// <summary>
		/// Adds one item to the Mod Manager action bar.
		/// </summary>
		/// <param name="item">The toolbar item to add.</param>
		private void AddToolbarLink(BarItem item)
		{
			if (item != null)
				barModActions.AddItem(item);
		}

		/// <summary>
		/// Creates the explicit visual separators used by the Mod Manager toolbar.
		/// </summary>
		private void InitializeToolbarSeparators()
		{
			_toolbarSeparatorAfterDisable = CreateToolbarSeparator();
			_toolbarSeparatorAfterEndorse = CreateToolbarSeparator();
			_toolbarSeparatorAfterCategoryView = CreateToolbarSeparator();
			UpdateToolbarSeparators(_toolbarPositionLeft);
		}

		/// <summary>
		/// Creates a skin-aware static separator that remains visible in both horizontal and vertical toolbar layouts.
		/// </summary>
		private BarStaticItem CreateToolbarSeparator()
		{
			return new BarStaticItem
			{
				Manager = barManagerMods,
				AutoSize = BarStaticItemSize.Content,
				Border = BorderStyles.NoBorder,
				PaintStyle = BarItemPaintStyle.Caption,
				ShowInCustomizationForm = false
			};
		}

		/// <summary>
		/// Updates toolbar separators so they are vertical in the top toolbar and horizontal in the side toolbar.
		/// </summary>
		/// <param name="left">Whether the toolbar is currently docked on the left.</param>
		private void UpdateToolbarSeparators(bool left)
		{
			string caption = left ? "────────────────" : "│";

			if (_toolbarSeparatorAfterDisable != null)
				_toolbarSeparatorAfterDisable.Caption = caption;
			if (_toolbarSeparatorAfterEndorse != null)
				_toolbarSeparatorAfterEndorse.Caption = caption;
			if (_toolbarSeparatorAfterCategoryView != null)
				_toolbarSeparatorAfterCategoryView.Caption = caption;
		}

		/// <summary>
		/// Adds the toolbar-position action to the right side of the DevExpress mod toolbar.
		/// </summary>
		private void InitializeToolbarPositionButton()
		{
			_toolbarPositionButton = new BarButtonItem(barManagerMods, "Toolbar Layout")
			{
				Alignment = BarItemLinkAlignment.Right,
				Hint = "Toolbar Layout – move to Left",
				PaintStyle = BarItemPaintStyle.CaptionGlyph
			};
			_toolbarPositionButton.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(
				Nexus.Client.Properties.Resources.toolbar_move_left,
				new Size(16, 16));
			_toolbarPositionButton.ItemClick += (sender, args) => SetToolbarPosition(!_toolbarPositionLeft, true);
		}

		/// <summary>
		/// Moves the DevExpress mod toolbar between the top and left edges and optionally persists the choice.
		/// </summary>
		/// <param name="left">Whether the toolbar should be docked on the left.</param>
		/// <param name="save">Whether the choice should be persisted.</param>
		private void SetToolbarPosition(bool left, bool save)
		{
			_toolbarPositionLeft = left;

			barManagerMods.BeginUpdate();
			try
			{
				barModActions.OptionsBar.RotateWhenVertical = false;
				barModActions.OptionsBar.UseWholeRow = !left;
				barModActions.DockStyle = left ? BarDockStyle.Left : BarDockStyle.Top;
				barModActions.DockCol = 0;
				barModActions.DockRow = 0;
				barModActions.Visible = true;
				UpdateToolbarSeparators(left);

				if (_toolbarPositionButton != null)
				{
					_toolbarPositionButton.Caption = "Toolbar Layout";
					_toolbarPositionButton.Hint = left ? "Toolbar Layout – move to Top" : "Toolbar Layout – move to Left";
					_toolbarPositionButton.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(
						left
							? Nexus.Client.Properties.Resources.toolbar_move_top
							: Nexus.Client.Properties.Resources.toolbar_move_left,
						new Size(16, 16));
				}
			}
			finally
			{
				barManagerMods.EndUpdate();
			}

			if (left)
				barDockControlLeft.BringToFront();
			else
				barDockControlTop.BringToFront();

			PerformLayout();
			SaveGridDisplayOption(GridToolbarPositionKey, left, save);
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

		/// <summary>
		/// Builds and shows the DevExpress row popup for the currently selected mod or mod set.
		/// </summary>
		private void gridView_PopupMenuShowing(object sender, PopupMenuShowingEventArgs e)
		{
			if (e.MenuType != GridMenuType.Row) return;
			gridView.FocusedRowHandle = e.HitInfo.RowHandle;
			IMod mod = SelectedMod;
			if (mod == null) return;

			List<IMod> mods = SelectedMods;
			if (mods.Count == 0) mods = new List<IMod> { mod };
			bool singleMod = mods.Count == 1;
			bool active = IsModActive(mod);
			bool installed = IsModInstalled(mod);

			EnsureGridPopupMenu();
			ClearGridPopupItems();

			BarButtonItem itemHeader = CreatePopupButton(Path.GetFileName(mod.Filename), Properties.Resources.document_save, null);
			itemHeader.Enabled = false;
			_gridPopupMenu.AddItem(itemHeader);

			if (singleMod)
			{
				if (!installed)
				{
					AddGridPopupItem(CreatePopupButton("Install and activate", Properties.Resources.dialog_ok_4_16,
						() => _viewModel?.ActivateModCommand.Execute(new List<IMod> { mod })), true);

					if (_viewModel?.ModManager?.GameMode?.SupportsGameRootModInstall == true)
					{
						string gameModeName = _viewModel.ModManager.GameMode.Name;
						if (String.IsNullOrWhiteSpace(gameModeName)) gameModeName = "game";
						AddGridPopupItem(CreatePopupButton(String.Format("Install to {0} root (eg. SKSE)", gameModeName), Properties.Resources.change_game_mode,
							() => _viewModel?.ActivateModInGameRoot(mod)));
					}
				}
				else if (!active)
				{
					AddGridPopupItem(CreatePopupButton("Activate", Properties.Resources.dialog_ok_4_16,
						() => _viewModel?.ActivateModCommand.Execute(new List<IMod> { mod })), true);
				}
				else
				{
					AddGridPopupItem(CreatePopupButton("Deactivate", Properties.Resources.dialog_ok_4_16,
						() => _viewModel?.DisableModCommand.Execute(new List<IMod> { mod })), true);
					AddGridPopupItem(CreatePopupButton("Reinstall Mod", Properties.Resources.change_game_mode,
						() => _viewModel?.ReinstallMod(mod, null)));
				}
			}
			else
			{
				AddGridPopupItem(CreatePopupButton("Reinstall Mod/s", Properties.Resources.change_game_mode,
					() => _viewModel?.ReinstallMultipleMods(mods)), true);
			}

			BarSubItem itemUninstall = CreatePopupSubItem("Uninstall or Delete", Properties.Resources.dialog_block);
			if (singleMod)
			{
				itemUninstall.AddItem(CreatePopupButton("From active profile", null, () =>
				{
					if (_viewModel != null && ConfirmMissingArchiveUninstall(mods))
						_viewModel.DeactivateMod(mod);
				}));
				itemUninstall.AddItem(CreatePopupButton("From all profiles", null, () =>
				{
					if (_viewModel == null || !ConfirmMissingArchiveUninstall(mods)) return;
					IBackgroundTaskSet btsDeactivate = _viewModel.ModManager.DeactivateMod(mod, _viewModel.ModManager.ActiveMods);
					if (btsDeactivate != null)
					{
						btsDeactivate.TaskSetCompleted += (taskSender, taskArgs) =>
						{
							if (!taskArgs.Success) return;
							if (InvokeRequired)
								Invoke((MethodInvoker)(() => UninstallModFromProfiles?.Invoke(this, new ModEventArgs(mod))));
							else
								UninstallModFromProfiles?.Invoke(this, new ModEventArgs(mod));
						};
						_viewModel.ModManager.ModActivationMonitor.AddActivity(btsDeactivate);
					}
					else
					{
						UninstallModFromProfiles?.Invoke(this, new ModEventArgs(mod));
					}
				}));

				BarButtonItem deleteItem = CreatePopupButton("Delete mod (permanently) and uninstall.", Properties.Resources.dialog_cancel_4_16, () =>
				{
					if (_viewModel == null) return;
					if (!ConfirmModFileDeletion(mods) || !ConfirmMissingArchiveUninstall(mods)) return;

					IBackgroundTaskSet btsDeactivate = _viewModel.ModManager.DeactivateMod(mod, _viewModel.ModManager.ActiveMods);
					Action deleteAfterDeactivate = () =>
					{
						UninstallModFromProfiles?.Invoke(this, new ModEventArgs(mod));
						var oclMods = new ThreadSafeObservableList<IMod>(mods);
						_viewModel.DeleteMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), true, true, false);
					};

					if (btsDeactivate != null)
					{
						btsDeactivate.TaskSetCompleted += (taskSender, taskArgs) =>
						{
							if (!taskArgs.Success) return;
							if (InvokeRequired) Invoke((MethodInvoker)(() => deleteAfterDeactivate()));
							else deleteAfterDeactivate();
						};
						_viewModel.ModManager.ModActivationMonitor.AddActivity(btsDeactivate);
					}
					else
					{
						deleteAfterDeactivate();
					}
				});
				BarItemLink deleteLink = itemUninstall.AddItem(deleteItem);
				deleteLink.BeginGroup = true;
			}
			else
			{
				itemUninstall.AddItem(CreatePopupButton("From active profile", null, () =>
				{
					if (_viewModel != null && ConfirmMissingArchiveUninstall(mods))
						_viewModel.DeactivateSelectedMods(mods);
				}));
			}
			AddGridPopupItem(itemUninstall);

			BarSubItem itemWarnings = CreatePopupSubItem("Mod Update Warnings", Properties.Resources.update_warning);
			BuildUpdateWarningsSubmenu(itemWarnings, mods);
			if (itemWarnings.ItemLinks.Count > 0) AddGridPopupItem(itemWarnings);

			BarSubItem itemChecks = CreatePopupSubItem("Mod Update Checks and Automatic Mod Rename", Properties.Resources.edit_find_and_replace);
			BuildUpdateChecksSubmenu(itemChecks, mods);
			if (itemChecks.ItemLinks.Count > 0) AddGridPopupItem(itemChecks);

			if (_viewModel?.CategoryManager != null)
			{
				BarSubItem itemMoveTo = CreatePopupSubItem("Move to", null);
				foreach (IModCategory cat in _viewModel.CategoryManager.Categories.OrderBy(category => category.CategoryName))
				{
					int catId = cat.Id;
					string catName = cat.CategoryName;
					itemMoveTo.AddItem(CreatePopupButton(catName, null, () => _viewModel?.SwitchModsToCategory(mods, catId)));
				}
				if (itemMoveTo.ItemLinks.Count > 0) AddGridPopupItem(itemMoveTo, true);
			}

			if (singleMod)
				AddGridPopupItem(CreatePopupButton("Reset Mod Cache", null, () => ResetSelectedModCache(mod)), true);

			_gridPopupMenu.ShowPopup(Control.MousePosition);
			e.Allow = false;
		}

		/// <summary>
		/// Creates the persistent DevExpress popup used for mod-grid row actions.
		/// </summary>
		private void EnsureGridPopupMenu()
		{
			if (_gridPopupMenu == null)
				_gridPopupMenu = new PopupMenu(barManagerMods);
		}

		/// <summary>
		/// Adds an item to the row popup and optionally starts a visual group before it.
		/// </summary>
		/// <param name="item">The item to add.</param>
		/// <param name="beginGroup">Whether the item starts a new visual group.</param>
		private void AddGridPopupItem(BarItem item, bool beginGroup = false)
		{
			BarItemLink link = _gridPopupMenu.AddItem(item);
			link.BeginGroup = beginGroup;
		}

		/// <summary>
		/// Creates a transient DevExpress popup button and optionally wires an action.
		/// </summary>
		/// <param name="caption">The item caption.</param>
		/// <param name="image">The optional raster image.</param>
		/// <param name="action">The optional action executed when clicked.</param>
		/// <returns>The configured popup button.</returns>
		private BarButtonItem CreatePopupButton(string caption, Image image, Action action)
		{
			var item = new BarButtonItem(barManagerMods, caption);
			if (image != null)
				item.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(image, new Size(16, 16));
			if (action != null) item.ItemClick += (sender, args) => action();
			return item;
		}

		/// <summary>
		/// Creates a transient DevExpress popup submenu.
		/// </summary>
		/// <param name="caption">The submenu caption.</param>
		/// <param name="image">The optional submenu image.</param>
		/// <returns>The configured submenu.</returns>
		private BarSubItem CreatePopupSubItem(string caption, Image image)
		{
			var item = new BarSubItem(barManagerMods, caption);
			if (image != null)
				item.ImageOptions.Image = DevExpressDisplaySettingsApplier.ResizeBarItemImage(image, new Size(16, 16));
			return item;
		}

		/// <summary>
		/// Removes and disposes all transient items from the reusable mod-grid popup.
		/// </summary>
		private void ClearGridPopupItems()
		{
			if (_gridPopupMenu == null) return;

			List<BarItem> items = _gridPopupMenu.ItemLinks.Cast<BarItemLink>()
				.Select(link => link.Item)
				.Where(item => item != null)
				.Distinct()
				.ToList();
			_gridPopupMenu.ClearLinks();
			foreach (BarItem item in items)
				DisposePopupItem(item);
		}

		/// <summary>
		/// Recursively disposes one transient popup item and any nested submenu items.
		/// </summary>
		/// <param name="item">The item to dispose.</param>
		private static void DisposePopupItem(BarItem item)
		{
			BarSubItem subItem = item as BarSubItem;
			if (subItem != null)
			{
				List<BarItem> children = subItem.ItemLinks.Cast<BarItemLink>()
					.Select(link => link.Item)
					.Where(child => child != null)
					.Distinct()
					.ToList();
				subItem.ClearLinks();
				foreach (BarItem child in children) DisposePopupItem(child);
			}
			item.Dispose();
		}

		private void ResetSelectedModCache(IMod mod)
		{
			if (_viewModel == null || mod == null) return;

			try
			{
				_viewModel.ResetModCacheCommand.Execute(mod);
				RebuildActivationStateCache();
				gridView.RefreshData();
			}
			catch (Exception ex)
			{
				XtraMessageBox.Show(this,
					"Unable to reset the selected mod cache." + Environment.NewLine + Environment.NewLine + ex.Message,
					"Reset Mod Cache", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void BuildUpdateWarningsSubmenu(BarSubItem parent, List<IMod> mods)
		{
			if (mods == null || mods.Count == 0) return;

			if (mods.Count == 1)
			{
				IMod mod = mods[0];
				parent.AddItem(CreatePopupButton(mod.UpdateWarningEnabled ? "Disable update warning" : "Enable update warning", null,
					() => _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(mods), !mod.UpdateWarningEnabled)));
			}
			else
			{
				bool hasEnabled = mods.Any(mod => mod.UpdateWarningEnabled);
				bool hasDisabled = mods.Any(mod => !mod.UpdateWarningEnabled);
				if (hasDisabled)
					parent.AddItem(CreatePopupButton("Enable for selected files", null,
						() => _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(mods), true)));
				if (hasEnabled)
					parent.AddItem(CreatePopupButton("Disable for selected files", null,
						() => _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(mods), false)));
			}

			BarButtonItem enableAll = CreatePopupButton("Enable for all files", null,
				() => _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(_viewModel.ManagedMods), true));
			BarItemLink enableAllLink = parent.AddItem(enableAll);
			enableAllLink.BeginGroup = parent.ItemLinks.Count > 1;
			parent.AddItem(CreatePopupButton("Disable for all files", null,
				() => _viewModel?.ToggleModUpdateWarning(new HashSet<IMod>(_viewModel.ManagedMods), false)));
		}

		private void BuildUpdateChecksSubmenu(BarSubItem parent, List<IMod> mods)
		{
			if (mods == null || mods.Count == 0) return;

			if (mods.Count == 1)
			{
				IMod mod = mods[0];
				parent.AddItem(CreatePopupButton(mod.UpdateChecksEnabled ? "Disable for this mod" : "Enable for this mod", null,
					() => _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(mods), !mod.UpdateChecksEnabled)));
			}
			else
			{
				bool hasEnabled = mods.Any(mod => mod.UpdateChecksEnabled);
				bool hasDisabled = mods.Any(mod => !mod.UpdateChecksEnabled);
				if (hasDisabled)
					parent.AddItem(CreatePopupButton("Enable for selected mods", null,
						() => _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(mods), true)));
				if (hasEnabled)
					parent.AddItem(CreatePopupButton("Disable for selected mods", null,
						() => _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(mods), false)));
			}

			BarButtonItem enableAll = CreatePopupButton("Enable for all mods", null,
				() => _viewModel?.ToggleModUpdateCheck(new HashSet<IMod>(_viewModel.ManagedMods), true));
			BarItemLink enableAllLink = parent.AddItem(enableAll);
			enableAllLink.BeginGroup = parent.ItemLinks.Count > 1;
		}

		// ── Toolbar helpers ──────────────────────────────────────────────────

		/// <summary>
		/// Loads an embedded SVG resource and renders it as a bitmap of the requested size.
		/// </summary>
		private static Image LoadSvgIcon(string resourceName, int size)
		{
			var assembly = typeof(ModManagerDXControl).Assembly;
			string fullName = assembly.GetManifestResourceNames()
				.FirstOrDefault(name => name.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase));

			if (fullName == null)
				return null;

			using (Stream stream = assembly.GetManifestResourceStream(fullName))
			{
				if (stream == null)
					return null;

				var svgImage = DevExpress.Utils.Svg.SvgImage.FromStream(stream);
				var svgBitmap = DevExpress.Utils.Svg.SvgBitmap.Create(svgImage);

				return svgBitmap.Render(
					new Size(size, size),
					null,
					DefaultBoolean.False,
					DefaultBoolean.False);
			}
		}

		private IMod GetSelectedMod() => SelectedMod;
		private List<IMod> GetSelectedMods()
		{
			var list = SelectedMods;
			return list.Count > 0 ? list : null;
		}

		/// <summary>
		/// Rebuilds the DevExpress Disable Mod popup with the profile-uninstall and permanent-delete actions.
		/// </summary>
		private void ConfigureDeactivateDropDown()
		{
			ClearPopupMenuItems(popupDeactivate);
			AddDeactivateDropDownItem("Uninstall mod from current profile", "mod-uninstall-from-profile.svg", UninstallSelectedModsFromCurrentProfile);
			AddDeactivateDropDownItem("Delete mod", "mod-remove.svg", DeleteSelectedModsFromKey);
		}

		/// <summary>
		/// Adds one action to the Disable Mod popup using the embedded SVG asset when available.
		/// </summary>
		/// <param name="text">The action caption.</param>
		/// <param name="iconResourceName">The embedded SVG resource name.</param>
		/// <param name="action">The action to execute.</param>
		private void AddDeactivateDropDownItem(string text, string iconResourceName, Action action)
		{
			var item = new BarButtonItem(barManagerMods, text);
			item.ImageOptions.SvgImage = LoadSvgImage(iconResourceName);
			item.ItemClick += (sender, args) => action();
			popupDeactivate.AddItem(item);
		}

		/// <summary>
		/// Clears and disposes transient items owned by a DevExpress popup menu.
		/// </summary>
		/// <param name="popupMenu">The popup menu to clear.</param>
		private static void ClearPopupMenuItems(PopupMenu popupMenu)
		{
			if (popupMenu == null) return;

			List<BarItem> items = popupMenu.ItemLinks.Cast<BarItemLink>()
				.Select(link => link.Item)
				.Where(item => item != null)
				.Distinct()
				.ToList();
			popupMenu.ClearLinks();
			foreach (BarItem item in items) DisposePopupItem(item);
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
			toolStripLabelModCount.Caption = $"Mods: {_modList.Count}";
		}

		// ── Toolbar button handlers ──────────────────────────────────────────

		private void tsbDeactivate_ButtonClick(object sender, ItemClickEventArgs e)
		{
			if (_viewModel == null || !_viewModel.DisableModCommand.CanExecute) return;

			List<IMod> mods = GetSelectedMods();
			if (mods == null) return;

			_viewModel.DisableModCommand.Execute(mods);
		}
		private void tsbAddMod_ButtonClick(object sender, ItemClickEventArgs e)
		{
			addModToolStripMenuItem_Click(sender, e);
		}

		private void addModToolStripMenuItem_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel == null) return;
			using (var ofd = new XtraOpenFileDialog())
			{
				ofd.Filter = "Mod Archives|*.zip;*.7z;*.rar;*.fomod;*.omod|All Files|*.*";
				ofd.Multiselect = true;
				if (ofd.ShowDialog(this) == DialogResult.OK)
					foreach (string f in ofd.FileNames)
						_viewModel.AddModCommand.Execute(f);
			}
		}

		private void addModFromURLToolStripMenuItem_Click(object sender, ItemClickEventArgs e)
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

		private void tsbSkyrimDownloads_Click(object sender, ItemClickEventArgs e)
		{
			_viewModel?.ToggleSkyrimSEDownloadMode();
			SetSkyrimDownloadModeFeedback();
		}

		private void tsb_SaveModLoadOrder_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel?.ModManager.GameMode.UsesModLoadOrder == true)
				_viewModel.SaveModLoadOrder();
		}

		private void tsb_ModUpLoadOrder_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel?.ModManager.GameMode.UsesModLoadOrder != true) return;
			var mods = SelectedMods;
			if (mods.Count == 0 && SelectedMod != null) mods = new List<IMod> { SelectedMod };
			foreach (var mod in mods)
				_viewModel.UpdateModLoadOrder(mod, mod.NewPlaceInModLoadOrder == -1 ? -1 : mod.NewPlaceInModLoadOrder - 1);
			gridView.InvalidateRows();
		}

		private void tsb_ModDownLoadOrder_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel?.ModManager.GameMode.UsesModLoadOrder != true) return;
			var mods = SelectedMods;
			if (mods.Count == 0 && SelectedMod != null) mods = new List<IMod> { SelectedMod };
			foreach (var mod in mods)
				_viewModel.UpdateModLoadOrder(mod, mod.NewPlaceInModLoadOrder == int.MaxValue ? int.MaxValue : mod.NewPlaceInModLoadOrder + 1);
			gridView.InvalidateRows();
		}

		private void tsbModOnlineChecks_ButtonClick(object sender, ItemClickEventArgs e)
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
					XtraMessageBox.Show(this,
						$"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
						"Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void withinTheLastDayToolStripMenuItem_Click(object sender, ItemClickEventArgs e)
		{
			RunUpdatedModsCheck("1d");
		}

		private void withinTheLastWeekToolStripMenuItem_Click(object sender, ItemClickEventArgs e)
		{
			RunUpdatedModsCheck("1w");
		}

		private void withinTheLastMonthToolStripMenuItem_Click(object sender, ItemClickEventArgs e)
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
					XtraMessageBox.Show(this,
						$"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
						"Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void checkFileDownloadId_Click(object sender, ItemClickEventArgs e)
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
					XtraMessageBox.Show(this,
						$"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
						"Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void checkMissingDownloadId_Click(object sender, ItemClickEventArgs e)
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
					XtraMessageBox.Show(this,
						$"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
						"Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void tsbToggleEndorse_Click(object sender, ItemClickEventArgs e)
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
				XtraMessageBox.Show(this,
					$"Unable to {(current != true ? "endorse" : "unendorse")} this file:{Environment.NewLine}{ex.Message}",
					"Endorsement Error:", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally
			{
				tsbToggleEndorse.Enabled = true;
			}
			SetCommandExecutableStatus();
		}

		private void tsbShowUpdatesOnly_Click(object sender, ItemClickEventArgs e)
		{
			_showUpdatesOnly = !_showUpdatesOnly;
			tsbShowUpdatesOnly.Down = _showUpdatesOnly;
			gridView.ActiveFilterString = _showUpdatesOnly
				? $"[{ColLastKnown}] != null And [{ColLastKnown}] != '' And [{ColVersion}] != [{ColLastKnown}]"
				: string.Empty;
		}

		private string GetExportToFileArgs()
		{
			if (_viewModel == null) return null;
			using (var sfd = new XtraSaveFileDialog())
			{
				sfd.FileName = _viewModel.GetDefaultExportFilename();
				sfd.Filter = _viewModel.GetExportFilterString();
				return sfd.ShowDialog(this) == DialogResult.OK ? sfd.FileName : null;
			}
		}

		// ── Category toolbar handlers ────────────────────────────────────────

		private void addNewCategory_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel == null) return;
			_viewModel.CategoryManager.AddCategory();
		}

		private void collapseAllCategories_Click(object sender, ItemClickEventArgs e)
			=> CollapseAllCategories();

		private void expandAllCategories_Click(object sender, ItemClickEventArgs e)
			=> ExpandAllCategories();

		/// <summary>Collapses all category groups in the mod grid (callable from CategoryManagerControl).</summary>
		public void CollapseAllCategories()
		{
			if (!_categoryViewActive)
				ApplyCategoryView(true, true);
			gridView.CollapseAllGroups();
			SaveGridLayout();
		}

		/// <summary>Expands all category groups in the mod grid (callable from CategoryManagerControl).</summary>
		public void ExpandAllCategories()
		{
			if (!_categoryViewActive)
				ApplyCategoryView(true, false);
			gridView.ExpandAllGroups();
			SaveGridLayout();
		}

		/// <summary>
		/// Updates Nexus and custom categories without clearing existing custom assignments.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">The event arguments.</param>
		private void updateNexusAndCustomCategories_Click(object sender, ItemClickEventArgs e)
		{
			try
			{
				_viewModel?.UpdateNexusAndCustomCategories();
			}
			catch (Exception ex)
			{
				if (ex.Message != "Login required")
					ExtendedMessageBox.Show(this, "Couldn't perform the category update, retry later." + Environment.NewLine + Environment.NewLine + ex.Message, "Category update", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void resetDefaultCategories_Click(object sender, ItemClickEventArgs e)
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
					XtraMessageBox.Show(this,
						$"Couldn't perform the update check, retry later.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
						"Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void resetUnassignedToDefaultCategories_Click(object sender, ItemClickEventArgs e)
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

		private void resetModsCategory_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel == null) return;
			_viewModel.ResetToUnassigned();
			gridView.InvalidateRows();
		}

		private void removeAllCategories_Click(object sender, ItemClickEventArgs e)
		{
			if (_viewModel == null) return;
			if (_viewModel.RemoveAllCategories())
			{
				gridView.InvalidateRows();
				ResetSearchBox?.Invoke(this, EventArgs.Empty);
			}
		}

		private void toggleHiddenCategories_Click(object sender, ItemClickEventArgs e) { /* flat grid has no hidden categories */ }

		private void tsbSwitchView_Click(object sender, ItemClickEventArgs e)
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
			CategoriesUpdateCheckTask categoryUpdateTask = e.Argument as CategoriesUpdateCheckTask;
			bool resetCategoryAssignments = categoryUpdateTask == null || categoryUpdateTask.ResetCategoryAssignmentsAfterUpdate;
			_viewModel?.CompleteCategoriesUpdate(e.Argument.ReturnValue != null, resetCategoryAssignments);
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
			e.Argument.TaskStarted += TaskSet_TaskStarted;
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
			RefreshActivationState();
		}

		private void VM_ReinstallCompleted(object sender, EventArgs e)
		{
			if (InvokeRequired) { Invoke((Action<object, EventArgs>)VM_ReinstallCompleted, sender, e); return; }
			RefreshActivationState();
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
			e.Argument.TaskStarted += TaskSet_TaskStarted;
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
			((IBackgroundTaskSet)sender).TaskStarted -= TaskSet_TaskStarted;
			((IBackgroundTaskSet)sender).TaskSetCompleted -= TaskSet_TaskSetCompleted;

			if (!string.IsNullOrEmpty(e.Message))
			{
				if (e.Success)
					XtraMessageBox.Show(this, e.Message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
				else
					XtraMessageBox.Show(this, e.Message, "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

			if (!File.Exists(oldPath))
				return oldPath;

			switch (XtraMessageBox.Show(this,
				$"A mod archive already exists at:\r\n{oldPath}\r\n\r\nWould you like to overwrite it?",
				"Overwrite?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
			{
				case DialogResult.Yes: return oldPath;
				case DialogResult.No: return newPath;
				default: return null;
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
			switch (XtraMessageBox.Show(this,
				$"A newer version of '{oldMod.ModName}' has been found.\r\nWould you like to upgrade?",
				"Upgrade Mod?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
			{
				case DialogResult.Yes: return ConfirmUpgradeResult.Upgrade;
				case DialogResult.No: return ConfirmUpgradeResult.NormalActivation;
				default: return ConfirmUpgradeResult.Cancel;
			}
		}
	}
}


