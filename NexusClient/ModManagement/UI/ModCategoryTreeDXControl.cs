namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Drawing;
	using System.Drawing.Drawing2D;
	using System.Drawing.Text;
	using System.Linq;
	using System.Windows.Forms;

	using DevExpress.Utils;
	using DevExpress.XtraEditors;
	using DevExpress.XtraTreeList;
	using DevExpress.XtraTreeList.Columns;
	using DevExpress.XtraTreeList.Nodes;

	using Nexus.Client.Mods;
	using Nexus.Client.UI;
	using Nexus.Client.Util.Localization;
	using Nexus.Client.UI.Controls;

	/// <summary>
	/// Carries a Mod Name edit request from the Category Tree frontend to the Mod Manager.
	/// </summary>
	internal sealed class ModTreeRenameEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a rename request for the specified mod.
		/// </summary>
		internal ModTreeRenameEventArgs(IMod mod, string newName)
		{
			Mod = mod;
			NewName = newName ?? String.Empty;
		}

		/// <summary>
		/// Gets the mod being renamed.
		/// </summary>
		internal IMod Mod { get; }
		/// <summary>
		/// Gets the requested display name.
		/// </summary>
		internal string NewName { get; }
	}

	/// <summary>
	/// Carries a validated nullable Sort-number edit from the Category Tree to the Mod Manager.
	/// </summary>
	internal sealed class ModTreeSortNumberEditEventArgs : EventArgs
	{
		/// <summary>
		/// Initializes a Sort-number edit request for the specified mod.
		/// </summary>
		internal ModTreeSortNumberEditEventArgs(IMod mod, int? sortNumber)
		{
			Mod = mod;
			SortNumber = sortNumber;
		}

		internal IMod Mod { get; }
		internal int? SortNumber { get; }
	}

	/// <summary>
	/// Hosts the hierarchical Category View frontend independently from the
	/// Mod Manager orchestration, toolbar and ViewModel lifecycle.
	/// </summary>
	internal partial class ModCategoryTreeDXControl : XtraUserControl
	{
		private Func<IMod, ModVisualStatus> _visualStatusResolver;
		private Func<IMod, Image> _statusImageResolver;
		private Func<IMod, Image> _endorsementImageResolver;
		private Func<IMod, bool> _outdatedResolver;
		private Func<IMod, bool> _newModResolver;
		private Func<IMod, bool> _missingArchiveResolver;
		private Func<string, Color> _categoryColorResolver;
		private Func<Image> _warningIconResolver;
		private Font _regularFont;
		private Font _boldFont;
		private Font _underlineFont;
		private Font _boldUnderlineFont;
		private Font _secondaryFont;
		private Font _secondaryBoldFont;
		private NmmColorPalette _colorPalette;
		private bool _usesLightRowPalette;
		private bool _showRowHighlights = true;
		private bool _showActiveModsInBold;
		private bool _showColouredCategories = true;
		private bool _renameRequested;
		private bool _renamingModName;
		private IMod _renameMod;
		private string _renameOriginalName;
		private Control _renameActiveEditor;
		private bool _editingSortNumber;
		private IMod _sortEditMod;
		private int? _sortOriginalValue;
		private long _editorSessionGeneration;
		private long _sortEditSessionGeneration;
		private bool _lastFindPanelVisible;
		private Color _latestVersionForeColor;
		private Color _outdatedVersionForeColor;
		private int _internalDataUpdateDepth;
		private ImageList _categoryModCountImages;
		private readonly Dictionary<int, int> _categoryModCountImageIndexes = new Dictionary<int, int>();
		private bool _showCategoryModCountIcons;
		private TreeGestureTarget _gestureTarget;
		private string _sortSignature = String.Empty;

		/// <summary>
		/// Captures the stable target of one native mouse gesture so later layout movement cannot retarget the action.
		/// </summary>
		private sealed class TreeGestureTarget
		{
			/// <summary>
			/// Initializes one stable mouse gesture target captured before later layout or focus changes.
			/// </summary>
			internal TreeGestureTarget(TreeListNode node, string columnFieldName, MouseButtons button, Point location, bool wasSelected)
			{
				Node = node;
				Mod = node?.Tag as IMod;
				ColumnFieldName = columnFieldName;
				Button = button;
				Location = location;
				WasSelected = wasSelected;
			}

			internal TreeListNode Node { get; }
			internal IMod Mod { get; }
			internal string ColumnFieldName { get; }
			internal MouseButtons Button { get; }
			internal Point Location { get; }
			internal bool WasSelected { get; }
		}

		/// <summary>
		/// Initializes the hierarchical Category View frontend and its DevExpress interaction rules.
		/// </summary>
		public ModCategoryTreeDXControl()
		{
			InitializeComponent();
			if (components == null)
				components = new Container();
			_categoryModCountImages = new ImageList(components)
			{
				ColorDepth = ColorDepth.Depth32Bit,
				ImageSize = new Size(16, 16)
			};
			ConfigureTree();
		}

		/// <summary>
		/// Gets the underlying DevExpress TreeList.
		/// </summary>
		internal TreeList TreeList => treeList;

		/// <summary>
		/// Gets whether a native TreeList editor or its pending Sort-number commit is active.
		/// </summary>
		internal bool HasActiveEditor => treeList.ActiveEditor != null || _editingSortNumber;

		/// <summary>
		/// Occurs when the user requests activation or deactivation of a specific mod.
		/// </summary>
		internal event EventHandler<ModEventArgs> ModToggleRequested;
		/// <summary>
		/// Occurs when the user requests deletion of the current mod selection.
		/// </summary>
		internal event EventHandler DeleteRequested;
		/// <summary>
		/// Occurs when the shared Mod Manager context menu should be shown.
		/// </summary>
		internal event EventHandler ContextMenuRequested;
		/// <summary>
		/// Occurs when the Latest column is activated for a specific mod.
		/// </summary>
		internal event EventHandler<ModEventArgs> LatestLinkRequested;
		/// <summary>
		/// Occurs when the user interacts with a mod row and any new-mod marker may be acknowledged.
		/// </summary>
		internal event EventHandler ModInteractionOccurred;
		/// <summary>
		/// Occurs when a category node changes expanded state.
		/// </summary>
		internal event EventHandler CategoryExpansionChanged;
		/// <summary>
		/// Occurs when a persistent TreeList layout property changes.
		/// </summary>
		internal event EventHandler LayoutStateChanged;
		/// <summary>
		/// Occurs after the TreeList completes a user-visible sort operation.
		/// </summary>
		internal event EventHandler SortingCompleted;
		/// <summary>
		/// Occurs when an inline Mod Name edit has been validated and should be committed by the Mod Manager.
		/// </summary>
		internal event EventHandler<ModTreeRenameEventArgs> RenameRequested;
		/// <summary>
		/// Occurs when a nullable Sort number has been validated and should be durably committed.
		/// </summary>
		internal event EventHandler<ModTreeSortNumberEditEventArgs> SortNumberEditRequested;
		/// <summary>
		/// Occurs after a native TreeList editor has finished and deferred surface updates may be reconciled.
		/// </summary>
		internal event EventHandler EditSessionEnded;
		/// <summary>
		/// Occurs when user navigation supersedes any queued automatic Category View navigation.
		/// </summary>
		internal event EventHandler UserNavigationOccurred;

		/// <summary>
		/// Applies shared Mods presentation settings, palette resolvers, fonts and row density to the TreeList.
		/// </summary>
		internal void ConfigurePresentation(
			Func<IMod, ModVisualStatus> visualStatusResolver,
			Func<IMod, Image> statusImageResolver,
			Func<IMod, Image> endorsementImageResolver,
			Func<IMod, bool> outdatedResolver,
			Func<IMod, bool> newModResolver,
			Func<IMod, bool> missingArchiveResolver,
			Func<string, Color> categoryColorResolver,
			Func<Image> warningIconResolver,
			NmmColorPalette colorPalette,
			bool usesLightRowPalette,
			bool showRowHighlights,
			bool showActiveModsInBold,
			bool showColouredCategories,
			Color latestVersionForeColor,
			Color outdatedVersionForeColor,
			Font regularFont,
			Font boldFont,
			Font underlineFont,
			Font boldUnderlineFont,
			Font secondaryFont,
			Font secondaryBoldFont,
			int rowHeight)
		{
			_visualStatusResolver = visualStatusResolver;
			_statusImageResolver = statusImageResolver;
			_endorsementImageResolver = endorsementImageResolver;
			_outdatedResolver = outdatedResolver;
			_newModResolver = newModResolver;
			_missingArchiveResolver = missingArchiveResolver;
			_categoryColorResolver = categoryColorResolver;
			_warningIconResolver = warningIconResolver;
			_colorPalette = colorPalette;
			_usesLightRowPalette = usesLightRowPalette;
			_showRowHighlights = showRowHighlights;
			_showActiveModsInBold = showActiveModsInBold;
			_showColouredCategories = showColouredCategories;
			_latestVersionForeColor = latestVersionForeColor;
			_outdatedVersionForeColor = outdatedVersionForeColor;
			_regularFont = regularFont;
			_boldFont = boldFont;
			_underlineFont = underlineFont;
			_boldUnderlineFont = boldUnderlineFont;
			_secondaryFont = secondaryFont;
			_secondaryBoldFont = secondaryBoldFont;

			if (regularFont != null)
			{
				treeList.Font = regularFont;
				treeList.Appearance.Row.Font = regularFont;
				treeList.Appearance.HeaderPanel.Font = regularFont;
			}

			if (rowHeight > 0)
				treeList.RowHeight = rowHeight;

			treeList.LayoutChanged();
			treeList.Invalidate();
		}

		/// <summary>
		/// Shows or hides the native TreeList category image containing the assigned mod count.
		/// </summary>
		internal void SetShowCategoryModCountIcons(bool visible)
		{
			_showCategoryModCountIcons = visible;
			if (!visible)
			{
				treeList.SelectImageList = null;
				foreach (TreeListNode node in treeList.Nodes)
				{
					if (!(node.Tag is ModCategoryTreeCategory))
						continue;
					node.ImageIndex = -1;
					node.SelectImageIndex = -1;
				}
				treeList.Invalidate();
				return;
			}

			RebuildCategoryModCountImageCache();
			treeList.SelectImageList = _categoryModCountImages;
			foreach (TreeListNode node in treeList.Nodes)
			{
				ModCategoryTreeCategory category = node.Tag as ModCategoryTreeCategory;
				if (category != null)
					UpdateCategoryModCountIcon(node, category.ModCount);
			}
			treeList.LayoutChanged();
			treeList.Invalidate();
		}

		/// <summary>
		/// Updates one category node's cached folder/count image after its aggregate count changes.
		/// </summary>
		internal void UpdateCategoryModCountIcon(TreeListNode node, int modCount)
		{
			if (node == null || !(node.Tag is ModCategoryTreeCategory))
				return;

			if (!_showCategoryModCountIcons)
			{
				node.ImageIndex = -1;
				node.SelectImageIndex = -1;
				return;
			}

			int imageIndex = GetCategoryModCountImageIndex(Math.Max(0, modCount));
			node.ImageIndex = imageIndex;
			node.SelectImageIndex = imageIndex;
		}

		private void RebuildCategoryModCountImageCache()
		{
			int rowBound = treeList.RowHeight > 0 ? Math.Max(16, treeList.RowHeight - 2) : NmmIconProvider.CurrentIconSize;
			int iconSize = Math.Max(16, Math.Min(NmmIconProvider.CurrentIconSize, rowBound));
			treeList.SelectImageList = null;
			_categoryModCountImages.Images.Clear();
			_categoryModCountImageIndexes.Clear();
			_categoryModCountImages.ImageSize = new Size(iconSize, iconSize);
		}

		private int GetCategoryModCountImageIndex(int modCount)
		{
			int imageIndex;
			if (_categoryModCountImageIndexes.TryGetValue(modCount, out imageIndex))
				return imageIndex;

			Bitmap image = CreateCategoryModCountImage(modCount, _categoryModCountImages.ImageSize.Width);
			_categoryModCountImages.Images.Add(image);
			imageIndex = _categoryModCountImages.Images.Count - 1;
			_categoryModCountImageIndexes[modCount] = imageIndex;
			return imageIndex;
		}

		private static Bitmap CreateCategoryModCountImage(int modCount, int size)
		{
			var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.Transparent);
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

				Image folder = NmmIconProvider.GetBitmap(NmmIconAction.OpenFolder, size, false);
				if (folder != null)
					graphics.DrawImage(folder, new Rectangle(0, 0, size, size));

				string text = modCount.ToString();
				var textBounds = new RectangleF(1f, size * 0.30f, size - 2f, size * 0.66f);
				float fontSize = Math.Max(4f, size * 0.48f);
				Font font = null;
				try
				{
					while (fontSize >= 4f)
					{
						font?.Dispose();
						font = new Font(FontFamily.GenericSansSerif, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
						SizeF measured = graphics.MeasureString(text, font);
						if (measured.Width <= textBounds.Width && measured.Height <= textBounds.Height)
							break;
						fontSize -= 0.5f;
					}

					Color textColor = ResolveCountTextColor(bitmap);
					Color shadowColor = textColor.GetBrightness() > 0.5f ? Color.FromArgb(210, 20, 20, 20) : Color.FromArgb(210, 245, 245, 245);
					using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
					using (var shadowBrush = new SolidBrush(shadowColor))
					using (var textBrush = new SolidBrush(textColor))
					{
						var shadowBounds = new RectangleF(textBounds.X + 1f, textBounds.Y + 1f, textBounds.Width, textBounds.Height);
						graphics.DrawString(text, font, shadowBrush, shadowBounds, format);
						graphics.DrawString(text, font, textBrush, textBounds, format);
					}
				}
				finally
				{
					font?.Dispose();
				}
			}
			return bitmap;
		}

		private static Color ResolveCountTextColor(Bitmap bitmap)
		{
			long luminance = 0;
			int samples = 0;
			for (int y = 0; y < bitmap.Height; y += 2)
			{
				for (int x = 0; x < bitmap.Width; x += 2)
				{
					Color pixel = bitmap.GetPixel(x, y);
					if (pixel.A < 64)
						continue;
					luminance += (pixel.R * 299L + pixel.G * 587L + pixel.B * 114L) / 1000L;
					samples++;
				}
			}

			return samples > 0 && luminance / samples > 150
				? Color.FromArgb(25, 25, 25)
				: Color.White;
		}

		/// <summary>
		/// Suppresses user-facing sort completion notifications while the surface mutates data.
		/// Calls may be nested by higher-level reconciliation operations.
		/// </summary>
		internal void BeginInternalDataUpdate()
		{
			_internalDataUpdateDepth++;
		}

		/// <summary>
		/// Ends a data-update notification suppression scope.
		/// </summary>
		internal void EndInternalDataUpdate()
		{
			if (_internalDataUpdateDepth > 0)
				_internalDataUpdateDepth--;

			if (_internalDataUpdateDepth == 0)
				SynchronizeSortSignature();
		}

		/// <summary>
		/// Synchronizes the current complete TreeList sort signature after a suppressed/programmatic sort change.
		/// </summary>
		internal void SynchronizeSortSignature()
		{
			_sortSignature = CaptureSortSignature();
		}

		/// <summary>
		/// Invalidates the current mouse gesture target after a structural or view change.
		/// </summary>
		internal void InvalidateGestureTarget()
		{
			_gestureTarget = null;
		}

		/// <summary>
		/// Configures TreeList behavior and wires frontend-only interaction events.
		/// </summary>
		private void ConfigureTree()
		{
			treeList.OptionsView.ShowIndicator = false;
			treeList.OptionsView.ShowVertLines = false;
			treeList.OptionsView.ShowHorzLines = true;
			treeList.OptionsView.ShowColumns = true;
			treeList.OptionsView.ShowAutoFilterRow = true;
			treeList.OptionsView.AutoWidth = false;
			treeList.OptionsBehavior.Editable = true;
			treeList.OptionsBehavior.AutoScrollOnSorting = false;
			treeList.OptionsSelection.MultiSelect = true;
			treeList.OptionsSelection.EnableAppearanceFocusedCell = false;
			treeList.OptionsSelection.EnableAppearanceFocusedRow = true;
			treeList.OptionsFilter.FilterMode = FilterMode.ParentBranch;
			treeList.OptionsCustomization.AllowColumnMoving = true;
			treeList.OptionsCustomization.AllowColumnResizing = true;
			treeList.OptionsCustomization.AllowSort = true;
			treeList.OptionsFind.AlwaysVisible = false;
			treeList.TreeLevelWidth = 18;
			_lastFindPanelVisible = treeList.IsFindPanelVisible;

			treeList.NodeCellStyle += TreeList_NodeCellStyle;
			treeList.CustomDrawNodeCell += TreeList_CustomDrawNodeCell;
			treeList.CustomDrawColumnHeader += TreeList_CustomDrawColumnHeader;
			treeList.ShowingEditor += TreeList_ShowingEditor;
			treeList.ShownEditor += TreeList_ShownEditor;
			treeList.HiddenEditor += TreeList_HiddenEditor;
			treeList.CellValueChanged += TreeList_CellValueChanged;
			treeList.DoubleClick += TreeList_DoubleClick;
			treeList.KeyDown += TreeList_KeyDown;
			treeList.KeyUp += TreeList_KeyUp;
			treeList.MouseDown += TreeList_MouseDown;
			treeList.MouseMove += TreeList_MouseMove;
			treeList.MouseUp += TreeList_MouseUp;
			treeList.MouseClick += TreeList_MouseClick;
			treeList.TopVisibleNodeIndexChanged += TreeList_TopVisibleNodeIndexChanged;
			treeList.AfterExpand += TreeList_CategoryExpansionChanged;
			treeList.AfterCollapse += TreeList_CategoryExpansionChanged;
			treeList.ColumnWidthChanged += (sender, args) => RaiseLayoutStateChanged();
			treeList.ColumnChanged += (sender, args) => RaiseLayoutStateChanged();
			treeList.EndSorting += (sender, args) =>
			{
				RaiseLayoutStateChanged();
				string currentSignature = CaptureSortSignature();
				bool sortChanged = !String.Equals(_sortSignature, currentSignature, StringComparison.Ordinal);
				_sortSignature = currentSignature;

				// EndSorting also fires when data changes reorder an already-sorted tree.
				// A user-facing sorting action requires both a changed complete sort signature
				// and execution outside NMM's suppressed/programmatic update scope.
				if (_internalDataUpdateDepth == 0 && sortChanged)
					SortingCompleted?.Invoke(this, EventArgs.Empty);
			};
			treeList.LayoutUpdated += TreeList_LayoutUpdated;
		}

		/// <summary>
		/// Captures all sorted columns in precedence order, including each direction.
		/// </summary>
		private string CaptureSortSignature()
		{
			var parts = new List<string>();
			for (int index = 0; index < treeList.SortedColumnCount; index++)
			{
				TreeListColumn column = treeList.GetSortColumn(index);
				if (column != null)
					parts.Add(String.Format("{0}:{1}", column.FieldName ?? String.Empty, column.SortOrder));
			}
			return String.Join("|", parts);
		}

		/// <summary>
		/// Applies row and cell appearance for category nodes, mod states, dates and the Latest column.
		/// </summary>
		private void TreeList_NodeCellStyle(object sender, GetCustomNodeCellStyleEventArgs e)
		{
			if (e.Node == null)
				return;

			// Category nodes carry aggregate state only. Keep them visually distinct from
			// mod rows and highlight the category when at least one child is new.
			ModCategoryTreeCategory category = e.Node.Tag as ModCategoryTreeCategory;
			if (category != null)
			{
				bool selected = treeList.Selection.Contains(e.Node) || treeList.FocusedNode == e.Node;
				bool containsNewMods = _newModResolver != null &&
					e.Node.Nodes.Cast<TreeListNode>()
						.Select(node => node.Tag as IMod)
						.Any(m => m != null && _newModResolver(m));

				if (containsNewMods && !selected && _colorPalette != null)
				{
					e.Appearance.BackColor = _colorPalette.ModNewGroupBackColor;
					e.Appearance.ForeColor = _colorPalette.ModNewGroupForeColor;
				}

				if (_boldFont != null)
					e.Appearance.Font = _boldFont;
				return;
			}

			IMod mod = e.Node.Tag as IMod;
			if (mod == null)
				return;

			ModVisualStatus status = _visualStatusResolver != null
				? _visualStatusResolver(mod)
				: ModVisualStatus.Uninstalled;
			bool isActive = status == ModVisualStatus.InstalledActive;
			bool isInstalled = status != ModVisualStatus.Uninstalled;
			bool isSelected = treeList.Selection.Contains(e.Node) || treeList.FocusedNode == e.Node;
			bool useProfileRowHighlights = _usesLightRowPalette || NmmIconProvider.CurrentColorProfile != NmmIconColorProfile.Base;

			// New-mod highlighting has precedence over installed/active row highlighting.
			// Selected rows retain the native skin selection so focus remains unambiguous.
			if (_newModResolver != null && _newModResolver(mod) && !isSelected && _colorPalette != null)
			{
				e.Appearance.BackColor = _colorPalette.ModNewRowBackColor;
				e.Appearance.ForeColor = _colorPalette.ModNewRowForeColor;
				if (_boldFont != null)
					e.Appearance.Font = _boldFont;
			}
			else if (useProfileRowHighlights && _showRowHighlights && _colorPalette != null)
			{
				if (isActive)
				{
					e.Appearance.BackColor = isSelected
						? _colorPalette.ModActiveSelectedRowBackColor
						: _colorPalette.ModActiveRowBackColor;
					e.Appearance.ForeColor = _colorPalette.ModRowForeColor;
				}
				else if (isInstalled)
				{
					e.Appearance.BackColor = isSelected
						? _colorPalette.ModInstalledSelectedRowBackColor
						: _colorPalette.ModInstalledRowBackColor;
					e.Appearance.ForeColor = _colorPalette.ModRowForeColor;
				}
			}

			if (_showActiveModsInBold && isActive && _boldFont != null)
				e.Appearance.Font = _boldFont;

			if (e.Column != null && e.Column.FieldName == ModCategoryTreeColumns.Latest && !String.IsNullOrEmpty(mod.LastKnownVersion))
			{
				if (!isSelected)
					e.Appearance.ForeColor = _outdatedResolver != null && _outdatedResolver(mod)
						? _outdatedVersionForeColor
						: _latestVersionForeColor;
				e.Appearance.Font = _showActiveModsInBold && isActive
					? (_boldUnderlineFont ?? _boldFont)
					: (_underlineFont ?? _regularFont);
			}

			if (!isSelected && e.Column != null &&
				(e.Column.FieldName == ModCategoryTreeColumns.InstallDate ||
				 e.Column.FieldName == ModCategoryTreeColumns.DownloadDate ||
				 e.Column.FieldName == ModCategoryTreeColumns.DownloadId))
			{
				if (_usesLightRowPalette)
					e.Appearance.ForeColor = DevExpressDisplaySettingsApplier.GetMutedSkinTextColor();
				e.Appearance.Font = _showActiveModsInBold && isActive
					? (_secondaryBoldFont ?? _boldFont)
					: (_secondaryFont ?? _regularFont);
			}
		}

		/// <summary>
		/// Applies the shared sorted-column header highlight when the active palette requires it.
		/// </summary>
		private void TreeList_CustomDrawColumnHeader(object sender, CustomDrawColumnHeaderEventArgs e)
		{
			if (!_usesLightRowPalette || _colorPalette == null ||
				e.Column == null || e.Column.SortOrder == SortOrder.None)
			{
				return;
			}

			e.Appearance.BackColor = _colorPalette.ModSortHeaderBackColor;
			e.Appearance.BackColor2 = _colorPalette.ModSortHeaderBackColor;
			e.Appearance.ForeColor = _colorPalette.ModSortHeaderForeColor;
			e.DefaultDraw();
			e.Handled = true;
		}

		/// <summary>
		/// Draws category colour accents and warning overlays without replacing the native TreeList layout.
		/// </summary>
		private void TreeList_CustomDrawNodeCell(object sender, CustomDrawNodeCellEventArgs e)
		{
			if (e.Node == null || e.Column == null)
				return;

			ModCategoryTreeCategory category = e.Node.Tag as ModCategoryTreeCategory;
			if (category != null && e.Column.FieldName == ModCategoryTreeColumns.ModName &&
				_showColouredCategories && _categoryColorResolver != null)
			{
				// Keep the native hierarchy glyph/text layout intact and add the same semantic
				// category colour used by the flat-grid badge as a compact accent.
				e.DefaultDraw();
				Color categoryColor = _categoryColorResolver(category.Name);
				int accentHeight = Math.Max(6, Math.Min(12, e.Bounds.Height - 6));
				var accentBounds = new Rectangle(
					e.Bounds.Right - 8,
					e.Bounds.Top + (e.Bounds.Height - accentHeight) / 2,
					4,
					accentHeight);
				using (var brush = new SolidBrush(categoryColor))
					e.Graphics.FillRectangle(brush, accentBounds);
				e.Handled = true;
				return;
			}

			IMod mod = e.Node.Tag as IMod;
			if (mod == null)
				return;

			if (e.Column.FieldName == ModCategoryTreeColumns.Status)
			{
				DrawImageCell(e, _statusImageResolver?.Invoke(mod));
				return;
			}

			if (e.Column.FieldName == ModCategoryTreeColumns.Endorsed)
			{
				DrawImageCell(e, _endorsementImageResolver?.Invoke(mod));
				return;
			}

			bool drawWarning =
				(e.Column.FieldName == ModCategoryTreeColumns.Latest && _outdatedResolver != null && _outdatedResolver(mod)) ||
				(e.Column.FieldName == ModCategoryTreeColumns.ModName && _missingArchiveResolver != null && _missingArchiveResolver(mod));
			if (!drawWarning)
				return;

			e.DefaultDraw();
			Image warning = _warningIconResolver?.Invoke();
			if (warning != null && e.Bounds.Width >= warning.Width + 5)
			{
				int x = e.Bounds.Right - warning.Width - 3;
				int y = e.Bounds.Top + (e.Bounds.Height - warning.Height) / 2;
				e.Graphics.DrawImage(warning, x, y, warning.Width, warning.Height);
			}
			e.Handled = true;
		}

		/// <summary>
		/// Draws an optional centered image while preserving the TreeList skin background.
		/// </summary>
		private static void DrawImageCell(CustomDrawNodeCellEventArgs e, Image image)
		{
			e.Appearance.DrawBackground(e.Cache, e.Bounds);
			if (image != null)
			{
				int maxWidth = Math.Max(1, e.Bounds.Width - 4);
				int maxHeight = Math.Max(1, e.Bounds.Height - 4);
				float scale = Math.Min(1f, Math.Min(maxWidth / (float)image.Width, maxHeight / (float)image.Height));
				int width = Math.Max(1, (int)Math.Round(image.Width * scale));
				int height = Math.Max(1, (int)Math.Round(image.Height * scale));
				int x = e.Bounds.Left + (e.Bounds.Width - width) / 2;
				int y = e.Bounds.Top + (e.Bounds.Height - height) / 2;
				e.Cache.DrawImage(image, new Rectangle(x, y, width, height));
			}
			e.Handled = true;
		}

		/// <summary>
		/// Starts inline editing of the focused mod name and records the original value for validation or rollback.
		/// </summary>
		private bool BeginInlineRename()
		{
			IMod mod = treeList.FocusedNode?.Tag as IMod;
			TreeListColumn column = treeList.Columns[ModCategoryTreeColumns.ModName];
			if (mod == null || column == null)
				return false;

			// ShowingEditor rejects all normal-node edits unless this flag is set. This
			// keeps the Auto Filter Row editable while preventing accidental row editing.
			_renameRequested = true;
			_renamingModName = true;
			_renameMod = mod;
			_renameOriginalName = mod.ModName ?? String.Empty;
			treeList.FocusedColumn = column;
			treeList.ShowEditor();
			if (treeList.ActiveEditor == null)
			{
				EndInlineRename();
				return false;
			}
			return true;
		}

		/// <summary>
		/// Restricts normal-node editing to an explicitly requested Mod Name rename.
		/// </summary>
		private void TreeList_ShowingEditor(object sender, CancelEventArgs e)
		{
			// ShowingEditor is not raised for the Auto Filter Row, so restricting normal
			// nodes here does not interfere with filter-row editing.
			long editorSession = ++_editorSessionGeneration;
			IMod focusedMod = treeList.FocusedNode?.Tag as IMod;
			string fieldName = treeList.FocusedColumn?.FieldName;
			if (focusedMod != null && fieldName == ModCategoryTreeColumns.SortNumber)
			{
				_editingSortNumber = true;
				_sortEditMod = focusedMod;
				_sortOriginalValue = ReadSortNumber(treeList.FocusedNode.GetValue(ModCategoryTreeColumns.SortNumber));
				_sortEditSessionGeneration = editorSession;
				e.Cancel = false;
				return;
			}

			InvalidateSortNumberEditSession();
			e.Cancel = !_renameRequested || focusedMod == null || fieldName != ModCategoryTreeColumns.ModName;
		}

		/// <summary>
		/// Attaches rename-specific keyboard handling after the DevExpress editor is created.
		/// </summary>
		private void TreeList_ShownEditor(object sender, EventArgs e)
		{
			_renameRequested = false;
			if (_editingSortNumber) return;
			_renameActiveEditor = treeList.ActiveEditor as Control;
			if (_renameActiveEditor != null)
				_renameActiveEditor.KeyDown += RenameEditor_KeyDown;

			TextEdit textEdit = treeList.ActiveEditor as TextEdit;
			textEdit?.SelectAll();
		}

		/// <summary>
		/// Detaches rename editor handlers and clears transient rename state.
		/// </summary>
		private void TreeList_HiddenEditor(object sender, EventArgs e)
		{
			if (_editingSortNumber)
			{
				// CellValueChanged can complete as the editor is being hidden. Preserve
				// the captured target/original value until this event chain is finished.
				long closingSession = _sortEditSessionGeneration;
				BeginInvoke((MethodInvoker)(() => EndSortNumberEdit(closingSession)));
				return;
			}

			if (_renameActiveEditor != null)
			{
				_renameActiveEditor.KeyDown -= RenameEditor_KeyDown;
				_renameActiveEditor = null;
			}
			EndInlineRename();
			EditSessionEnded?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Handles commit and cancel keys while the inline rename editor owns keyboard input.
		/// </summary>
		private void RenameEditor_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				treeList.CloseEditor();
			}
			else if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				treeList.HideEditor();
			}
		}

		/// <summary>
		/// Validates a user-edited mod name and raises a manager-owned rename request.
		/// </summary>
		private void TreeList_CellValueChanged(object sender, CellValueChangedEventArgs e)
		{
			if (_editingSortNumber && e.ChangedByUser && e.Column != null && e.Column.FieldName == ModCategoryTreeColumns.SortNumber)
			{
				IMod sortMod = e.Node?.Tag as IMod ?? _sortEditMod;
				int? sortNumber;
				if (sortMod == null || !TryReadSortNumber(e.Value, out sortNumber))
				{
					if (e.Node != null)
						e.Node.SetValue(ModCategoryTreeColumns.SortNumber, _sortOriginalValue);
					XtraMessageBox.Show(this,
						LanguageManager.Get("Mods.SortNumber.Invalid.Message", "Enter a whole number between -2147483648 and 2147483647, or leave the cell blank."),
						LanguageManager.Get("Common.Dialog.ErrorTitle", "Error"),
						MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}

				SortNumberEditRequested?.Invoke(this, new ModTreeSortNumberEditEventArgs(sortMod, sortNumber));
				return;
			}

			if (!_renamingModName || !e.ChangedByUser ||
				e.Column == null || e.Column.FieldName != ModCategoryTreeColumns.ModName)
				return;

			IMod mod = e.Node?.Tag as IMod ?? _renameMod;
			string newName = Convert.ToString(e.Value)?.Trim() ?? String.Empty;
			// Do not let an empty or unchanged editor value leak into the model. Restoring
			// the original node value also prevents the unbound UI from drifting from IMod.
			if (mod == null || String.IsNullOrEmpty(newName) ||
				String.Equals(newName, _renameOriginalName, StringComparison.Ordinal))
			{
				if (e.Node != null)
					e.Node.SetValue(ModCategoryTreeColumns.ModName, _renameOriginalName);
				return;
			}

			RenameRequested?.Invoke(this, new ModTreeRenameEventArgs(mod, newName));
		}

		/// <summary>
		/// Converts the SpinEdit value into the nullable signed integer contract used by Sort assignments.
		/// </summary>
		private static bool TryReadSortNumber(object value, out int? sortNumber)
		{
			sortNumber = null;
			if (value == null || value == DBNull.Value || String.IsNullOrWhiteSpace(Convert.ToString(value)))
				return true;

			try
			{
				decimal numeric = Convert.ToDecimal(value);
				if (numeric != Decimal.Truncate(numeric) || numeric < Int32.MinValue || numeric > Int32.MaxValue)
					return false;
				sortNumber = Decimal.ToInt32(numeric);
				return true;
			}
			catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
			{
				return false;
			}
		}

		/// <summary>
		/// Reads a previously validated node value for rollback bookkeeping.
		/// </summary>
		private static int? ReadSortNumber(object value)
		{
			int? sortNumber;
			return TryReadSortNumber(value, out sortNumber) ? sortNumber : null;
		}

		/// <summary>
		/// Clears Category Tree Sort-edit state only when the deferred callback still owns the active session.
		/// </summary>
		private void EndSortNumberEdit(long sessionGeneration)
		{
			if (_editingSortNumber && _sortEditSessionGeneration == sessionGeneration)
				InvalidateSortNumberEditSession();
			// A subsequent cancelled editor request may already have cleared the session.
			// Still notify the surface so pending refreshes are not stranded.
			EditSessionEnded?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Invalidates stale Sort-edit state before another tree editor session becomes authoritative.
		/// </summary>
		private void InvalidateSortNumberEditSession()
		{
			_editingSortNumber = false;
			_sortEditMod = null;
			_sortOriginalValue = null;
			_sortEditSessionGeneration = 0;
		}

		/// <summary>
		/// Clears transient state associated with an inline rename operation.
		/// </summary>
		private void EndInlineRename()
		{
			_renameRequested = false;
			_renamingModName = false;
			_renameMod = null;
			_renameOriginalName = null;
		}

		/// <summary>
		/// Detects Find Panel visibility changes that must be included in layout persistence.
		/// </summary>
		private void TreeList_LayoutUpdated(object sender, EventArgs e)
		{
			bool visible = treeList.IsFindPanelVisible;
			if (visible == _lastFindPanelVisible)
				return;
			_lastFindPanelVisible = visible;
			RaiseLayoutStateChanged();
		}

		/// <summary>
		/// Raises the consolidated layout-state notification used by persistence.
		/// </summary>
		private void RaiseLayoutStateChanged()
		{
			LayoutStateChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Handles double-click activation for mods and expand/collapse behavior for categories.
		/// </summary>
		private void TreeList_DoubleClick(object sender, EventArgs e)
		{
			TreeGestureTarget target = _gestureTarget;
			InvalidateGestureTarget();
			if (!IsGestureTargetValid(target, MouseButtons.Left))
				return;

			if (target.Mod != null)
			{
				if (String.Equals(target.ColumnFieldName, ModCategoryTreeColumns.SortNumber, StringComparison.Ordinal))
					return;
				ModInteractionOccurred?.Invoke(this, EventArgs.Empty);
				ModToggleRequested?.Invoke(this, new ModEventArgs(target.Mod));
				return;
			}

			if (target.Node.Tag is ModCategoryTreeCategory)
				target.Node.Expanded = !target.Node.Expanded;
		}

		/// <summary>
		/// Handles Category View keyboard commands that must be intercepted before default TreeList processing.
		/// </summary>
		private void TreeList_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control && e.KeyCode == Keys.F)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				treeList.ShowFindPanel();
				return;
			}

			// Sort editing owns keyboard gestures through the complete DevExpress commit chain.
			if (_editingSortNumber)
				return;

			// The active editor owns Enter/Escape while an inline rename is in progress.
			if (treeList.ActiveEditor != null)
				return;

			if (IsUserNavigationKey(e.KeyCode))
				UserNavigationOccurred?.Invoke(this, EventArgs.Empty);

			if (!(treeList.FocusedNode?.Tag is IMod focusedMod))
				return;

			if (e.KeyCode == Keys.F2)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				BeginInlineRename();
			}
			else if (e.KeyCode == Keys.Return)
			{
				e.Handled = true;
				ModToggleRequested?.Invoke(this, new ModEventArgs(focusedMod));
			}
			else if (e.KeyCode == Keys.Delete)
			{
				e.Handled = true;
				DeleteRequested?.Invoke(this, EventArgs.Empty);
			}
		}

		/// <summary>
		/// Acknowledges new-mod state after keyboard navigation or activation gestures.
		/// </summary>
		private void TreeList_KeyUp(object sender, KeyEventArgs e)
		{
			if (!(treeList.FocusedNode?.Tag is IMod))
				return;

			switch (e.KeyCode)
			{
				case Keys.Up:
				case Keys.Down:
				case Keys.PageUp:
				case Keys.PageDown:
				case Keys.Home:
				case Keys.End:
				case Keys.Enter:
				case Keys.Space:
					ModInteractionOccurred?.Invoke(this, EventArgs.Empty);
					break;
			}
		}

		/// <summary>
		/// Captures one stable mouse target before native focus/selection processing can be followed by layout movement.
		/// </summary>
		private void TreeList_MouseDown(object sender, MouseEventArgs e)
		{
			UserNavigationOccurred?.Invoke(this, EventArgs.Empty);

			// Preserve the first target during the second MouseDown of a native double-click.
			if (e.Clicks != 1)
				return;

			TreeListHitInfo hitInfo = treeList.CalcHitInfo(e.Location);
			bool wasSelected = hitInfo.Node != null && treeList.Selection.Contains(hitInfo.Node);
			_gestureTarget = new TreeGestureTarget(hitInfo.Node, hitInfo.Column?.FieldName, e.Button, e.Location, wasSelected);
		}

		/// <summary>
		/// Cancels a pending mouse target once pointer movement becomes a drag gesture.
		/// </summary>
		private void TreeList_MouseMove(object sender, MouseEventArgs e)
		{
			if (_gestureTarget == null || e.Button != MouseButtons.Left || _gestureTarget.Button != MouseButtons.Left)
				return;

			Size dragSize = SystemInformation.DragSize;
			Rectangle dragBounds = new Rectangle(
				_gestureTarget.Location.X - dragSize.Width / 2,
				_gestureTarget.Location.Y - dragSize.Height / 2,
				dragSize.Width,
				dragSize.Height);
			if (!dragBounds.Contains(e.Location))
				InvalidateGestureTarget();
		}

		/// <summary>
		/// Normalizes right-click focus and selection before requesting the shared context menu.
		/// </summary>
		private void TreeList_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
				return;

			TreeGestureTarget target = _gestureTarget;
			InvalidateGestureTarget();
			if (!IsGestureTargetValid(target, MouseButtons.Right))
				return;

			// Category nodes use the TreeList native node menu. The Mod Manager extends
			// that menu through PopupMenuShowing instead of opening a competing popup.
			if (target.Node.Tag is ModCategoryTreeCategory)
				return;

			if (target.Mod == null)
				return;

			// Capture selection state before changing focus. With DevExpress multi-select,
			// assigning FocusedNode may select the node immediately; testing afterwards
			// can therefore preserve a stale previous selection and make context actions
			// operate on both mods.
			if (!target.WasSelected)
			{
				treeList.Selection.Clear();
				treeList.Selection.Add(target.Node);
			}

			treeList.FocusedNode = target.Node;
			ContextMenuRequested?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Handles mod-row interaction and Latest-column navigation using the original MouseDown target.
		/// </summary>
		private void TreeList_MouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
				return;

			TreeGestureTarget target = _gestureTarget;
			if (!IsGestureTargetValid(target, MouseButtons.Left) || target.Mod == null)
				return;

			// Let the TreeList own normal focus and Ctrl/Shift selection semantics. The
			// captured target is only the identity used by actions originating from this gesture.
			ModInteractionOccurred?.Invoke(this, EventArgs.Empty);

			if (String.Equals(target.ColumnFieldName, ModCategoryTreeColumns.Latest, StringComparison.Ordinal))
				LatestLinkRequested?.Invoke(this, new ModEventArgs(target.Mod));
		}

		/// <summary>
		/// Invalidates queued automatic navigation when the user scrolls the TreeList outside an internal update.
		/// </summary>
		private void TreeList_TopVisibleNodeIndexChanged(object sender, EventArgs e)
		{
			if (_internalDataUpdateDepth == 0)
				UserNavigationOccurred?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Gets whether a captured gesture still represents the same node/action target.
		/// </summary>
		private static bool IsGestureTargetValid(TreeGestureTarget target, MouseButtons button)
		{
			if (target == null || target.Node == null || target.Button != button)
				return false;

			return target.Mod == null || ReferenceEquals(target.Node.Tag, target.Mod);
		}

		/// <summary>
		/// Gets whether a key can represent explicit user row/viewport navigation.
		/// </summary>
		private static bool IsUserNavigationKey(Keys keyCode)
		{
			switch (keyCode)
			{
				case Keys.Up:
				case Keys.Down:
				case Keys.PageUp:
				case Keys.PageDown:
				case Keys.Home:
				case Keys.End:
					return true;
				default:
					return false;
			}
		}

		/// <summary>
		/// Forwards category expansion changes so the current state can be persisted.
		/// </summary>
		private void TreeList_CategoryExpansionChanged(object sender, NodeEventArgs e)
		{
			if (e.Node?.Tag is ModCategoryTreeCategory)
				CategoryExpansionChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
