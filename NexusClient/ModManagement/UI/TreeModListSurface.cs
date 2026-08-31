namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;
	using System.Text;
	using System.Linq;
	using System.Windows.Forms;

	using DevExpress.Utils;
	using DevExpress.XtraEditors.Controls;
	using DevExpress.XtraTreeList;
	using DevExpress.XtraTreeList.Columns;
	using DevExpress.XtraTreeList.Nodes;
	using DevExpress.XtraTreeList.Nodes.Operations;

	using Nexus.Client.Mods;
	using Nexus.Client.Util.Localization;

	/// <summary>
	/// Defines the stable field names shared by Category Tree nodes, layout persistence and interaction logic.
	/// </summary>
	internal static class ModCategoryTreeColumns
	{
		internal const string Status = "ModStatus";
		internal const string ModName = "ModName";
		internal const string Version = "HumanReadableVersion";
		internal const string Latest = "LastKnownVersion";
		internal const string Author = "Author";
		internal const string InstallDate = "InstallDate";
		internal const string DownloadDate = "DownloadDate";
		internal const string DownloadId = "DownloadId";
		internal const string Endorsed = "IsEndorsed";
	}

	/// <summary>
	/// Identifies a root category node in the unbound Category Tree.
	/// </summary>
	internal sealed class ModCategoryTreeCategory
	{
		/// <summary>
		/// Initializes a category-node identity with a stable display name.
		/// </summary>
		internal ModCategoryTreeCategory(int id, string name)
		{
			Id = id;
			Name = name ?? String.Empty;
		}

		/// <summary>Gets or sets the stable Category Manager identifier represented by the node.</summary>
		internal int Id { get; set; }

		/// <summary>
		/// Gets the category name represented by the root node.
		/// </summary>
		internal string Name { get; }

		/// <summary>
		/// Gets or sets the number of mod nodes currently assigned to the category.
		/// </summary>
		internal int ModCount { get; set; }

		/// <summary>
		/// Gets or sets the number of active mods currently assigned to the category.
		/// </summary>
		internal int ActiveModCount { get; set; }

		/// <summary>
		/// Gets or sets the number of mods in the category that are still marked as new.
		/// </summary>
		internal int NewModCount { get; set; }
	}

	/// <summary>
	/// Unbound TreeList implementation of the Category View. Category nodes are
	/// stable parents; normal mod operations update only their affected child nodes.
	/// </summary>
	internal sealed class TreeModListSurface : IModCategorySurface
	{
		private readonly ModCategoryTreeDXControl _viewControl;
		private readonly TreeList _treeList;
		private readonly IList<IMod> _mods;
		private readonly Func<IMod, string> _categoryNameResolver;
		private readonly Func<IMod, string> _statusTextResolver;
		private readonly Func<IMod, bool> _newModResolver;
		private readonly Func<IMod, bool> _activeModResolver;
		private readonly Func<int, int, string> _categoryCountFormatter;
		private readonly Dictionary<string, TreeListNode> _categoryNodes =
			new Dictionary<string, TreeListNode>(StringComparer.CurrentCultureIgnoreCase);
		private readonly Dictionary<IMod, TreeListNode> _modNodes =
			new Dictionary<IMod, TreeListNode>();
		private readonly HashSet<string> _availableCategoryNames =
			new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
		private readonly Dictionary<string, int> _availableCategoryIds =
			new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
		private readonly HashSet<string> _pendingCollapsedCategoryNames =
			new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
		private bool _showEmptyCategories;
		private string _textFilter = String.Empty;
		private Func<IMod, bool> _visibilityPredicate;
		private Func<ModCategoryTreeCategory, bool> _categoryVisibilityPredicate;
		private bool _suppressSelectionChanged;

		/// <summary>
		/// Initializes the unbound Category Tree surface and its shared mod/category resolvers.
		/// </summary>
		public TreeModListSurface(
			ModCategoryTreeDXControl viewControl,
			IList<IMod> mods,
			Func<IMod, string> categoryNameResolver,
			Func<IMod, string> statusTextResolver,
			Func<IMod, bool> newModResolver,
			Func<IMod, bool> activeModResolver,
			Func<int, int, string> categoryCountFormatter)
		{
			if (viewControl == null) throw new ArgumentNullException(nameof(viewControl));
			if (mods == null) throw new ArgumentNullException(nameof(mods));
			if (categoryNameResolver == null) throw new ArgumentNullException(nameof(categoryNameResolver));

			_viewControl = viewControl;
			_treeList = viewControl.TreeList;
			_mods = mods;
			_categoryNameResolver = categoryNameResolver;
			_statusTextResolver = statusTextResolver;
			_newModResolver = newModResolver;
			_activeModResolver = activeModResolver;
			_categoryCountFormatter = categoryCountFormatter ?? ((active, total) => String.Format("{0}/{1} Mods", active, total));

			BuildColumns();
			_treeList.FocusedNodeChanged += TreeList_SelectionChanged;
			_treeList.SelectionChanged += TreeList_SelectionChanged;
			_treeList.CustomColumnSort += TreeList_CustomColumnSort;
		}

		/// <summary>
		/// Gets the WinForms control hosted by the Mod Manager.
		/// </summary>
		public Control ViewControl => _viewControl;

		/// <summary>
		/// Gets the mod represented by the currently focused TreeList node.
		/// </summary>
		public IMod FocusedMod => _treeList.FocusedNode?.Tag as IMod;

		/// <summary>
		/// Gets the mods represented by the currently selected TreeList nodes.
		/// </summary>
		public IList<IMod> SelectedMods
		{
			get
			{
				var result = new List<IMod>();
				foreach (TreeListNode node in _treeList.Selection)
				{
					IMod mod = node?.Tag as IMod;
					if (mod != null && !result.Contains(mod))
						result.Add(mod);
				}
				return result;
			}
		}

		/// <summary>
		/// Occurs when the effective mod selection changes.
		/// </summary>
		public event EventHandler SelectionChanged;

		/// <summary>
		/// Rebuilds the Category Tree from the supplied mods while preserving expansion, focus and selection state.
		/// </summary>
		public void SetMods(IEnumerable<IMod> mods)
		{
			// Full rebuilds are intentionally limited to initial population and collection
			// resets. Category-only changes are reconciled incrementally by SetAvailableCategories.
			IList<string> collapsed = _categoryNodes.Count > 0
				? GetCollapsedCategoryNames()
				: _pendingCollapsedCategoryNames.ToList();
			IMod focused = FocusedMod;
			IList<IMod> selected = SelectedMods;

			var source = (mods ?? Enumerable.Empty<IMod>())
				.Where(item => item != null)
				.ToList();

			_suppressSelectionChanged = true;
			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginUpdate();
			_treeList.BeginUnboundLoad();
			try
			{
				_treeList.Nodes.Clear();
				_categoryNodes.Clear();
				_modNodes.Clear();

				foreach (IGrouping<string, IMod> categoryGroup in source
					.GroupBy(ResolveCategoryName, StringComparer.CurrentCultureIgnoreCase)
					.OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
				{
					TreeListNode categoryNode = GetOrCreateCategoryNode(categoryGroup.Key);
					foreach (IMod mod in categoryGroup.OrderBy(item => item.ModName, StringComparer.CurrentCultureIgnoreCase))
						AppendModNode(mod, categoryNode);
					UpdateCategoryCaption(categoryNode);
				}

				if (_showEmptyCategories)
				{
					foreach (string categoryName in _availableCategoryNames.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
						GetOrCreateCategoryNode(categoryName);
				}
			}
			finally
			{
				_treeList.EndUnboundLoad();
				_treeList.EndUpdate();
				_viewControl.EndInternalDataUpdate();
				_suppressSelectionChanged = false;
			}

			RestoreCollapsedCategories(collapsed);
			ApplyVisibilityFilterAfterStructureChange();
			RestoreSelection(selected, focused);
		}

		/// <summary>
		/// Adds mod nodes incrementally without rebuilding unaffected categories.
		/// </summary>
		public void AddMods(IEnumerable<IMod> mods)
		{
			if (mods == null) return;
			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginUnboundLoad();
			try
			{
				foreach (IMod mod in mods)
					if (mod != null && !_modNodes.ContainsKey(mod))
						AddModCore(mod);
			}
			finally
			{
				_treeList.EndUnboundLoad();
				_viewControl.EndInternalDataUpdate();
			}
			ApplyVisibilityFilterAfterStructureChange();
		}

		/// <summary>
		/// Removes mod nodes incrementally and removes empty category nodes when appropriate.
		/// </summary>
		public void RemoveMods(IEnumerable<IMod> mods)
		{
			if (mods == null) return;
			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginUnboundLoad();
			try
			{
				foreach (IMod mod in mods.ToList())
					RemoveModCore(mod);
			}
			finally
			{
				_treeList.EndUnboundLoad();
				_viewControl.EndInternalDataUpdate();
			}
			ApplyVisibilityFilterAfterStructureChange();
		}

		/// <summary>
		/// Refreshes a mod node and moves it between category parents when its category assignment changes.
		/// </summary>
		public void RefreshMod(IMod mod, string propertyName)
		{
			if (mod == null) return;
			TreeListNode node;
			if (!_modNodes.TryGetValue(mod, out node))
			{
				if (_mods.Contains(mod))
					AddMods(new[] { mod });
				return;
			}

			_viewControl.BeginInternalDataUpdate();
			try
			{
				// Category changes alter hierarchy, not just cell values. Move only the affected
				// node so expansion and viewport state of unrelated categories remain untouched.
				if (String.Equals(propertyName, "CategoryId", StringComparison.Ordinal) ||
					String.Equals(propertyName, "CustomCategoryId", StringComparison.Ordinal))
				{
					string categoryName = ResolveCategoryName(mod);
					if (!(node.ParentNode?.Tag is ModCategoryTreeCategory currentCategory) ||
						!String.Equals(currentCategory.Name, categoryName, StringComparison.CurrentCultureIgnoreCase))
					{
						MoveModToCategory(mod, node, categoryName);
						return;
					}
				}

				UpdateModNode(node, mod);
				ApplyVisibilityFilterAfterStructureChange();
				_treeList.RefreshNode(node);
			}
			finally
			{
				_viewControl.EndInternalDataUpdate();
			}
		}

		/// <summary>
		/// Applies the Mod Manager text search to mod nodes while retaining matching parent categories.
		/// </summary>
		public void ApplyTextFilter(string filter)
		{
			string normalized = filter?.Trim() ?? String.Empty;
			if (String.Equals(_textFilter, normalized, StringComparison.CurrentCultureIgnoreCase))
				return;

			_textFilter = normalized;
			ApplyVisibilityFilter();
		}

		/// <summary>
		/// Sets the additional mod predicate used by Updates Only and New Mods filters.
		/// </summary>
		internal void SetVisibilityPredicate(Func<IMod, bool> predicate)
		{
			if (ReferenceEquals(_visibilityPredicate, predicate))
				return;

			_visibilityPredicate = predicate;
			ApplyVisibilityFilter();
		}

		/// <summary>
		/// Sets the category-level visibility rule composed with all mod filters.
		/// </summary>
		internal void SetCategoryVisibilityPredicate(Func<ModCategoryTreeCategory, bool> predicate)
		{
			_categoryVisibilityPredicate = predicate;
			ApplyVisibilityFilter();
		}

		/// <summary>
		/// Reconciles the known category set and the Show Empty Categories option without rebuilding unaffected mod nodes.
		/// </summary>
		internal void SetAvailableCategories(IEnumerable<IModCategory> categories, bool showEmptyCategories)
		{
			var newCategoryNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
			var newCategoryIds = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
			if (categories != null)
			{
				foreach (IModCategory category in categories)
				{
					if (category == null || String.IsNullOrWhiteSpace(category.CategoryName))
						continue;

					string categoryName = category.CategoryName.Trim();
					newCategoryNames.Add(categoryName);
					newCategoryIds[categoryName] = category.Id;
				}
			}

			bool categorySetChanged = !_availableCategoryNames.SetEquals(newCategoryNames) ||
				newCategoryIds.Any(pair => !_availableCategoryIds.TryGetValue(pair.Key, out int id) || id != pair.Value);
			bool showEmptyChanged = _showEmptyCategories != showEmptyCategories;
			if (!categorySetChanged && !showEmptyChanged)
			{
				if (_modNodes.Count == 0 && (_mods.Count > 0 || (_showEmptyCategories && _availableCategoryNames.Count > 0)))
					SetMods(_mods);
				return;
			}

			List<string> removedCategoryNames = _availableCategoryNames
				.Where(name => !newCategoryNames.Contains(name))
				.ToList();
			_availableCategoryNames.Clear();
			_availableCategoryNames.UnionWith(newCategoryNames);
			_availableCategoryIds.Clear();
			foreach (KeyValuePair<string, int> pair in newCategoryIds)
				_availableCategoryIds[pair.Key] = pair.Value;
			_showEmptyCategories = showEmptyCategories;

			foreach (KeyValuePair<string, TreeListNode> pair in _categoryNodes)
			{
				if (pair.Value?.Tag is ModCategoryTreeCategory treeCategory &&
					_availableCategoryIds.TryGetValue(pair.Key, out int categoryId))
				{
					treeCategory.Id = categoryId;
				}
			}

			// When the backing collection was cleared as part of a ViewModel switch, a
			// direct rebuild is both cheaper and safer than reconciling stale mod nodes.
			if (_mods.Count == 0 && _modNodes.Count > 0)
			{
				SetMods(_mods);
				return;
			}

			if (_modNodes.Count == 0 && _categoryNodes.Count == 0)
			{
				SetMods(_mods);
				return;
			}

			ReconcileCategories(removedCategoryNames);
		}

		/// <summary>
		/// Expands the containing category, selects the mod and makes it the focused node.
		/// </summary>
		public void FocusMod(IMod mod)
		{
			if (mod == null) return;
			TreeListNode node;
			if (!_modNodes.TryGetValue(mod, out node)) return;
			if (node.ParentNode != null) node.ParentNode.Expanded = true;
			_treeList.Selection.Clear();
			_treeList.Selection.Add(node);
			_treeList.FocusedNode = node;
		}

		/// <summary>
		/// Rebuilds the Category Tree from the shared backing mod list.
		/// </summary>
		public void RefreshDataSource()
		{
			SetMods(_mods);
		}

		/// <summary>
		/// Refreshes all mod-node values without changing category membership.
		/// </summary>
		public void RefreshData()
		{
			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginUpdate();
			try
			{
				ResetCategoryAggregateState();
				foreach (KeyValuePair<IMod, TreeListNode> pair in _modNodes.ToList())
				{
					UpdateModNode(pair.Value, pair.Key);
					AccumulateCategoryState(pair.Value.ParentNode, pair.Key);
				}
				RefreshCategoryCaptions();
			}
			finally
			{
				_treeList.EndUpdate();
				_viewControl.EndInternalDataUpdate();
			}
			ApplyVisibilityFilterAfterStructureChange();
			_treeList.Invalidate();
		}

		/// <summary>
		/// Invalidates the rendered TreeList rows.
		/// </summary>
		public void InvalidateRows()
		{
			RefreshCategoryAggregateState();
			_treeList.Invalidate();
		}

		/// <summary>
		/// Invalidates the TreeList and its hosting control.
		/// </summary>
		public void InvalidateView()
		{
			_treeList.Invalidate();
			_viewControl.Invalidate();
		}

		/// <summary>
		/// Refreshes the rendered node associated with the specified mod.
		/// </summary>
		public void InvalidateMod(IMod mod)
		{
			TreeListNode node;
			if (mod != null && _modNodes.TryGetValue(mod, out node))
				_treeList.RefreshNode(node);
		}

		/// <summary>
		/// Collapses all root category nodes.
		/// </summary>
		public void CollapseAllCategories()
		{
			_treeList.CollapseAll();
		}

		/// <summary>
		/// Expands all root category nodes.
		/// </summary>
		public void ExpandAllCategories()
		{
			_treeList.ExpandAll();
		}

		/// <summary>
		/// Gets the stable category names whose root nodes are collapsed.
		/// </summary>
		public IList<string> GetCollapsedCategoryNames()
		{
			return _categoryNodes.Values
				.Where(node => node != null && !node.Expanded)
				.Select(node => ((ModCategoryTreeCategory)node.Tag).Name)
				.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
		}

		/// <summary>
		/// Restores root-node expansion state from persisted category names.
		/// </summary>
		public void RestoreCollapsedCategories(IEnumerable<string> categoryNames)
		{
			var collapsed = new HashSet<string>(categoryNames ?? Enumerable.Empty<string>(), StringComparer.CurrentCultureIgnoreCase);
			_pendingCollapsedCategoryNames.Clear();
			_pendingCollapsedCategoryNames.UnionWith(collapsed);

			foreach (KeyValuePair<string, TreeListNode> pair in _categoryNodes)
			{
				bool expanded = !collapsed.Contains(pair.Key);
				if (pair.Value.Expanded != expanded)
					pair.Value.Expanded = expanded;
			}
		}

		/// <summary>
		/// Serializes the DevExpress TreeList layout to the string form stored in Mod Manager settings.
		/// </summary>
		internal string SaveLayout()
		{
			using (var stream = new MemoryStream())
			{
				_treeList.SaveLayoutToStream(stream);
				return Encoding.UTF8.GetString(stream.ToArray());
			}
		}

		/// <summary>
		/// Restores a persisted TreeList layout and reapplies invariants that user layouts must not override.
		/// </summary>
		internal void RestoreLayout(string serializedLayout)
		{
			if (String.IsNullOrEmpty(serializedLayout))
				return;
			try
			{
				// DevExpress layout data is persisted as text in the existing settings store.
				// Reapply invariants afterwards because older layouts may contain obsolete
				// editability, hierarchy or grouping state.
				byte[] bytes = Encoding.UTF8.GetBytes(serializedLayout);
				using (var stream = new MemoryStream(bytes))
				{
					_treeList.ForceInitialize();
					_treeList.RestoreLayoutFromStream(stream);
				}
				EnsureTreeColumnInvariants();
			}
			catch
			{
				// Ignore stale/incompatible layouts; the current defaults remain usable.
			}
		}

		/// <summary>
		/// Gets whether the TreeList Find Panel is currently visible.
		/// </summary>
		internal bool IsFindPanelVisible => _treeList.IsFindPanelVisible;

		/// <summary>
		/// Restores the persisted Find Panel visibility state.
		/// </summary>
		internal void SetFindPanelVisible(bool visible)
		{
			_treeList.OptionsFind.AlwaysVisible = false;
			if (visible)
				_treeList.ShowFindPanel();
			else
				_treeList.HideFindPanel();
		}

		/// <summary>
		/// Determines whether the specified field participates in the current TreeList sort.
		/// </summary>
		internal bool IsSortedByColumn(string fieldName)
		{
			for (int i = 0; i < _treeList.SortedColumnCount; i++)
			{
				TreeListColumn column = _treeList.GetSortColumn(i);
				if (column != null && String.Equals(column.FieldName, fieldName, StringComparison.Ordinal))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Focuses the first visible mod node in the TreeList's current visual order.
		/// </summary>
		internal void FocusFirstVisibleMod()
		{
			// NodesIterator.Visible follows the TreeList's current visual order, including
			// sorting/filtering/expansion. Do not use the lookup dictionaries here because
			// their enumeration order is not a UI ordering contract on .NET Framework.
			TreeListNode node = _treeList.NodesIterator.Visible
				.FirstOrDefault(candidate =>
					candidate != null &&
					candidate.Tag is IMod &&
					(candidate.ParentNode == null || candidate.ParentNode.Expanded));
			if (node == null)
				return;

			// This option is a viewport/focus convenience, not a navigation command.
			// Never expand a category that the user deliberately collapsed.
			_treeList.Selection.Clear();
			_treeList.Selection.Add(node);
			_treeList.FocusedNode = node;
		}

		/// <summary>
		/// Reapplies column editability, filtering and hierarchy rules after layout restoration.
		/// </summary>
		private void EnsureTreeColumnInvariants()
		{
			TreeListColumn modName = _treeList.Columns[ModCategoryTreeColumns.ModName];
			if (modName != null)
			{
				modName.Visible = true;
				modName.OptionsColumn.AllowEdit = true;
				modName.OptionsColumn.ReadOnly = false;
				_treeList.HierarchyColumn = modName;
			}
			foreach (TreeListColumn column in _treeList.Columns)
			{
				column.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;
				column.OptionsFilter.AutoFilterCondition = AutoFilterCondition.Contains;
				column.OptionsFilter.AllowAutoFilter = column.FieldName != ModCategoryTreeColumns.Endorsed;
				if (!ReferenceEquals(column, modName))
				{
					column.OptionsColumn.AllowEdit = false;
					column.OptionsColumn.ReadOnly = true;
				}
			}
		}

		/// <summary>
		/// Sorts category roots and mod siblings according to their distinct semantic rules.
		/// </summary>
		private void TreeList_CustomColumnSort(object sender, CustomColumnSortEventArgs e)
		{
			if (e.Node1 == null || e.Node2 == null || e.Column == null)
				return;

			ModCategoryTreeCategory category1 = e.Node1.Tag as ModCategoryTreeCategory;
			ModCategoryTreeCategory category2 = e.Node2.Tag as ModCategoryTreeCategory;
			if (category1 != null && category2 != null)
			{
				// Category order is semantic/navigation structure, not part of the selected
				// mod sort. Keep roots alphabetic even when mod siblings sort descending.
				int result = StringComparer.CurrentCultureIgnoreCase.Compare(category1.Name, category2.Name);
				e.Result = e.SortOrder == SortOrder.Descending ? -result : result;
				return;
			}

			IMod mod1 = e.Node1.Tag as IMod;
			IMod mod2 = e.Node2.Tag as IMod;
			if (mod1 == null || mod2 == null)
				return;

			e.Result = CompareMods(mod1, mod2, e.Column.FieldName);
		}

		/// <summary>
		/// Compares two mod nodes using the data semantics of the active TreeList column.
		/// </summary>
		private int CompareMods(IMod left, IMod right, string fieldName)
		{
			switch (fieldName)
			{
				case ModCategoryTreeColumns.Status:
					return StringComparer.CurrentCultureIgnoreCase.Compare(
						_statusTextResolver?.Invoke(left) ?? String.Empty,
						_statusTextResolver?.Invoke(right) ?? String.Empty);
				case ModCategoryTreeColumns.ModName:
					return StringComparer.CurrentCultureIgnoreCase.Compare(left.ModName ?? String.Empty, right.ModName ?? String.Empty);
				case ModCategoryTreeColumns.Version:
					return StringComparer.CurrentCultureIgnoreCase.Compare(left.HumanReadableVersion ?? String.Empty, right.HumanReadableVersion ?? String.Empty);
				case ModCategoryTreeColumns.Latest:
					return StringComparer.CurrentCultureIgnoreCase.Compare(left.LastKnownVersion ?? String.Empty, right.LastKnownVersion ?? String.Empty);
				case ModCategoryTreeColumns.Author:
					return StringComparer.CurrentCultureIgnoreCase.Compare(left.Author ?? String.Empty, right.Author ?? String.Empty);
				case ModCategoryTreeColumns.InstallDate:
					return CompareDates(left.InstallDate, right.InstallDate);
				case ModCategoryTreeColumns.DownloadDate:
					return CompareDates(left.DownloadDate, right.DownloadDate);
				case ModCategoryTreeColumns.DownloadId:
					return StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(left.DownloadId, CultureInfo.CurrentCulture), Convert.ToString(right.DownloadId, CultureInfo.CurrentCulture));
				case ModCategoryTreeColumns.Endorsed:
					return Nullable.Compare(left.IsEndorsed, right.IsEndorsed);
				default:
					return 0;
			}
		}

		/// <summary>
		/// Compares date-like values while preserving deterministic ordering for unparsable legacy values.
		/// </summary>
		private static int CompareDates(object leftValue, object rightValue)
		{
			DateTime left;
			DateTime right;
			bool hasLeft = TryParseDate(Convert.ToString(leftValue, CultureInfo.CurrentCulture), out left);
			bool hasRight = TryParseDate(Convert.ToString(rightValue, CultureInfo.CurrentCulture), out right);
			if (hasLeft && hasRight) return left.CompareTo(right);
			if (hasLeft) return 1;
			if (hasRight) return -1;
			return StringComparer.CurrentCultureIgnoreCase.Compare(
				Convert.ToString(leftValue, CultureInfo.CurrentCulture),
				Convert.ToString(rightValue, CultureInfo.CurrentCulture));
		}

		/// <summary>
		/// Parses current-culture, invariant and known legacy NMM date formats.
		/// </summary>
		private static bool TryParseDate(string value, out DateTime result)
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

		/// <summary>
		/// Builds the Category Tree columns and shared image editors from the current localized captions.
		/// </summary>
		private void BuildColumns()
		{
			_treeList.Columns.Clear();
			AddColumn(ModCategoryTreeColumns.Status, LanguageManager.Get("Common.Column.Status", "Status"), 58, 48, 80, HorzAlignment.Center);
			AddColumn(ModCategoryTreeColumns.ModName, LanguageManager.Get("Mods.Columns.ModName.Header", "MOD NAME"), 220, 100, 0, HorzAlignment.Default);
			AddColumn(ModCategoryTreeColumns.Version, LanguageManager.Get("Mods.Columns.Version.Header", "VERSION"), 70, 60, 110, HorzAlignment.Center);
			AddColumn(ModCategoryTreeColumns.Latest, LanguageManager.Get("Mods.Columns.Latest.Header", "LATEST"), 70, 60, 110, HorzAlignment.Center);
			AddColumn(ModCategoryTreeColumns.Author, LanguageManager.Get("Mods.Columns.Author.Header", "AUTHOR"), 128, 90, 240, HorzAlignment.Default);
			AddColumn(ModCategoryTreeColumns.InstallDate, LanguageManager.Get("Mods.Columns.InstallDate.Header", "INSTALL DATE"), 180, 100, 0, HorzAlignment.Center);
			AddColumn(ModCategoryTreeColumns.DownloadDate, LanguageManager.Get("Mods.Columns.DownloadDate.Header", "DOWNLOAD DATE"), 180, 100, 0, HorzAlignment.Center);
			AddColumn(ModCategoryTreeColumns.DownloadId, LanguageManager.Get("Mods.Columns.DownloadId.Header", "DOWNLOAD ID"), 80, 70, 120, HorzAlignment.Center);
			AddColumn(ModCategoryTreeColumns.Endorsed, LanguageManager.Get("Mods.Columns.Endorsed.Header", "ENDORSED"), 70, 50, 90, HorzAlignment.Center);

			// Status and Endorsed retain semantic values so sorting/filtering do not depend
			// on image editors. Their icons are painted by ModCategoryTreeDXControl.
			_treeList.Columns[ModCategoryTreeColumns.Endorsed].OptionsFilter.AllowAutoFilter = false;
			_treeList.HierarchyColumn = _treeList.Columns[ModCategoryTreeColumns.ModName];
			_treeList.ForceInitialize();
			EnsureTreeColumnInvariants();
		}

		/// <summary>
		/// Creates and configures one Category Tree column with the standard sizing and filtering rules.
		/// </summary>
		private TreeListColumn AddColumn(string fieldName, string caption, int width, int minWidth, int maxWidth, HorzAlignment alignment)
		{
			var column = new TreeListColumn
			{
				FieldName = fieldName,
				Caption = caption,
				Width = width,
				MinWidth = minWidth,
				Visible = true,
				VisibleIndex = _treeList.Columns.Count
			};
			if (maxWidth > 0) column.MaxWidth = maxWidth;
			column.OptionsColumn.AllowEdit = fieldName == ModCategoryTreeColumns.ModName;
			column.OptionsColumn.ReadOnly = fieldName != ModCategoryTreeColumns.ModName;
			column.SortMode = DevExpress.XtraGrid.ColumnSortMode.Custom;
			column.OptionsFilter.AutoFilterCondition = AutoFilterCondition.Contains;
			column.AppearanceHeader.TextOptions.HAlignment = alignment;
			column.AppearanceCell.TextOptions.HAlignment = alignment;
			_treeList.Columns.Add(column);
			return column;
		}

		/// <summary>
		/// Adds a single mod node beneath its effective category and updates the category count.
		/// </summary>
		private void AddModCore(IMod mod)
		{
			string categoryName = ResolveCategoryName(mod);
			TreeListNode categoryNode = GetOrCreateCategoryNode(categoryName);
			AppendModNode(mod, categoryNode);
			UpdateCategoryCaption(categoryNode);
		}

		/// <summary>
		/// Appends a mod node and updates the cached aggregate state of its category.
		/// </summary>
		private TreeListNode AppendModNode(IMod mod, TreeListNode categoryNode)
		{
			TreeListNode node = _treeList.AppendNode(BuildModValues(mod), categoryNode, CheckState.Unchecked, mod);
			node.ImageIndex = -1;
			node.SelectImageIndex = -1;
			_modNodes[mod] = node;

			ModCategoryTreeCategory category = categoryNode?.Tag as ModCategoryTreeCategory;
			if (category != null)
			{
				category.ModCount++;
				if (_activeModResolver != null && _activeModResolver(mod))
					category.ActiveModCount++;
				if (_newModResolver != null && _newModResolver(mod))
					category.NewModCount++;
			}

			return node;
		}

		/// <summary>
		/// Removes a single mod node and reconciles the parent category node.
		/// </summary>
		private void RemoveModCore(IMod mod)
		{
			if (mod == null) return;
			TreeListNode node;
			if (!_modNodes.TryGetValue(mod, out node)) return;
			TreeListNode parent = node.ParentNode;
			ModCategoryTreeCategory category = parent?.Tag as ModCategoryTreeCategory;
			if (category != null)
			{
				category.ModCount = Math.Max(0, category.ModCount - 1);
				if (_activeModResolver != null && _activeModResolver(mod))
					category.ActiveModCount = Math.Max(0, category.ActiveModCount - 1);
				if (_newModResolver != null && _newModResolver(mod))
					category.NewModCount = Math.Max(0, category.NewModCount - 1);
			}
			_modNodes.Remove(mod);
			node.Remove();
			if (!RemoveCategoryIfEmpty(parent))
				UpdateCategoryCaption(parent);
		}

		/// <summary>
		/// Moves a mod between category parents while preserving its focus and selection state.
		/// </summary>
		private void MoveModToCategory(IMod mod, TreeListNode oldNode, string categoryName, bool applyVisibility = true)
		{
			// Reparenting an unbound node is implemented as remove/add. Preserve identity at
			// the surface level so a category reassignment does not appear as lost selection.
			bool wasFocused = ReferenceEquals(_treeList.FocusedNode, oldNode);
			bool wasSelected = _treeList.Selection.Contains(oldNode);
			TreeListNode oldParent = oldNode.ParentNode;
			ModCategoryTreeCategory oldCategory = oldParent?.Tag as ModCategoryTreeCategory;
			if (oldCategory != null)
			{
				oldCategory.ModCount = Math.Max(0, oldCategory.ModCount - 1);
				if (_activeModResolver != null && _activeModResolver(mod))
					oldCategory.ActiveModCount = Math.Max(0, oldCategory.ActiveModCount - 1);
				if (_newModResolver != null && _newModResolver(mod))
					oldCategory.NewModCount = Math.Max(0, oldCategory.NewModCount - 1);
			}

			oldNode.Remove();
			TreeListNode newParent = GetOrCreateCategoryNode(categoryName);
			TreeListNode newNode = AppendModNode(mod, newParent);
			UpdateCategoryCaption(newParent);
			if (!RemoveCategoryIfEmpty(oldParent))
				UpdateCategoryCaption(oldParent);
			newParent.Expanded = true;
			if (wasSelected) _treeList.Selection.Add(newNode);
			if (wasFocused) _treeList.FocusedNode = newNode;
			if (applyVisibility)
				ApplyVisibilityFilterAfterStructureChange();
		}

		/// <summary>
		/// Gets an existing root category node or creates and registers a new one.
		/// </summary>
		private TreeListNode GetOrCreateCategoryNode(string categoryName)
		{
			TreeListNode node;
			if (_categoryNodes.TryGetValue(categoryName, out node))
				return node;

			object[] values = new object[_treeList.Columns.Count];
			values[_treeList.Columns[ModCategoryTreeColumns.ModName].AbsoluteIndex] = categoryName;
			int categoryId;
			if (!_availableCategoryIds.TryGetValue(categoryName, out categoryId))
				categoryId = String.Equals(categoryName, LanguageManager.Get("Mods.Values.Unassigned", "Unassigned"), StringComparison.CurrentCultureIgnoreCase) ? 0 : -1;
			node = _treeList.AppendNode(values, null, CheckState.Unchecked, new ModCategoryTreeCategory(categoryId, categoryName));
			node.Expanded = true;
			_categoryNodes[categoryName] = node;
			UpdateCategoryCaption(node);
			return node;
		}

		/// <summary>
		/// Removes an empty category node unless empty categories are configured to remain visible.
		/// </summary>
		private bool RemoveCategoryIfEmpty(TreeListNode categoryNode)
		{
			if (!(categoryNode?.Tag is ModCategoryTreeCategory category) || category.ModCount != 0)
				return false;

			if (_showEmptyCategories && _availableCategoryNames.Contains(category.Name))
			{
				UpdateCategoryCaption(categoryNode);
				return false;
			}

			_categoryNodes.Remove(category.Name);
			categoryNode.Remove();
			return true;
		}

		/// <summary>
		/// Updates a category node caption with its current mod count.
		/// </summary>
		private void UpdateCategoryCaption(TreeListNode categoryNode)
		{
			ModCategoryTreeCategory category = categoryNode?.Tag as ModCategoryTreeCategory;
			if (category == null) return;

			string countText = _categoryCountFormatter(category.ActiveModCount, category.ModCount);
			categoryNode.SetValue(
				ModCategoryTreeColumns.ModName,
				String.IsNullOrWhiteSpace(countText)
					? category.Name
					: String.Format("{0} ({1})", category.Name, countText));
			_viewControl.UpdateCategoryModCountIcon(categoryNode, category.ModCount);
		}

		/// <summary>
		/// Builds the unbound TreeList value array for a mod node.
		/// </summary>
		private object[] BuildModValues(IMod mod)
		{
			var values = new object[_treeList.Columns.Count];
			values[_treeList.Columns[ModCategoryTreeColumns.Status].AbsoluteIndex] = _statusTextResolver?.Invoke(mod) ?? String.Empty;
			values[_treeList.Columns[ModCategoryTreeColumns.ModName].AbsoluteIndex] = mod.ModName;
			values[_treeList.Columns[ModCategoryTreeColumns.Version].AbsoluteIndex] = mod.HumanReadableVersion;
			values[_treeList.Columns[ModCategoryTreeColumns.Latest].AbsoluteIndex] = mod.LastKnownVersion;
			values[_treeList.Columns[ModCategoryTreeColumns.Author].AbsoluteIndex] = mod.Author;
			values[_treeList.Columns[ModCategoryTreeColumns.InstallDate].AbsoluteIndex] = mod.InstallDate;
			values[_treeList.Columns[ModCategoryTreeColumns.DownloadDate].AbsoluteIndex] = mod.DownloadDate;
			values[_treeList.Columns[ModCategoryTreeColumns.DownloadId].AbsoluteIndex] = mod.DownloadId;
			values[_treeList.Columns[ModCategoryTreeColumns.Endorsed].AbsoluteIndex] = mod.IsEndorsed;
			return values;
		}

		/// <summary>
		/// Synchronizes all displayed values of an existing mod node.
		/// </summary>
		private void UpdateModNode(TreeListNode node, IMod mod)
		{
			if (node == null || mod == null) return;
			node.SetValue(ModCategoryTreeColumns.Status, _statusTextResolver?.Invoke(mod) ?? String.Empty);
			node.SetValue(ModCategoryTreeColumns.ModName, mod.ModName);
			node.SetValue(ModCategoryTreeColumns.Version, mod.HumanReadableVersion);
			node.SetValue(ModCategoryTreeColumns.Latest, mod.LastKnownVersion);
			node.SetValue(ModCategoryTreeColumns.Author, mod.Author);
			node.SetValue(ModCategoryTreeColumns.InstallDate, mod.InstallDate);
			node.SetValue(ModCategoryTreeColumns.DownloadDate, mod.DownloadDate);
			node.SetValue(ModCategoryTreeColumns.DownloadId, mod.DownloadId);
			node.SetValue(ModCategoryTreeColumns.Endorsed, mod.IsEndorsed);
		}

		/// <summary>
		/// Resolves the effective category name and falls back to the localized Unassigned category.
		/// </summary>
		private string ResolveCategoryName(IMod mod)
		{
			string categoryName = _categoryNameResolver(mod);
			return String.IsNullOrWhiteSpace(categoryName)
				? LanguageManager.Get("Mods.Values.Unassigned", "Unassigned")
				: categoryName;
		}

		/// <summary>
		/// Re-evaluates node visibility after a structural change only when an actual filter is active.
		/// </summary>
		private void ApplyVisibilityFilterAfterStructureChange()
		{
			if (String.IsNullOrEmpty(_textFilter) && _visibilityPredicate == null)
				return;

			ApplyVisibilityFilter();
		}

		/// <summary>
		/// Applies text and predicate filters to mod nodes and derives visibility of their category parents.
		/// </summary>
		private void ApplyVisibilityFilter()
		{
			bool changed = false;
			_treeList.BeginUpdate();
			try
			{
				// Child visibility is computed first; a category remains visible only when it
				// contains a matching mod, or when it is an explicitly requested empty category.
				foreach (KeyValuePair<string, TreeListNode> categoryPair in _categoryNodes)
				{
					ModCategoryTreeCategory category = categoryPair.Value.Tag as ModCategoryTreeCategory;
					bool categoryAllowed = category == null || _categoryVisibilityPredicate == null || _categoryVisibilityPredicate(category);
					bool anyVisible = false;
					foreach (TreeListNode modNode in categoryPair.Value.Nodes)
					{
						IMod mod = modNode.Tag as IMod;
						bool visible = categoryAllowed && mod != null &&
							(String.IsNullOrEmpty(_textFilter) ||
							 (mod.ModName ?? String.Empty).IndexOf(_textFilter, StringComparison.CurrentCultureIgnoreCase) >= 0) &&
							(_visibilityPredicate == null || _visibilityPredicate(mod));
						if (modNode.Visible != visible)
						{
							modNode.Visible = visible;
							changed = true;
						}
						anyVisible |= visible;
					}

					bool showEmptyCategory = categoryAllowed && _showEmptyCategories &&
						category != null &&
						category.ModCount == 0 &&
						String.IsNullOrEmpty(_textFilter) &&
						_visibilityPredicate == null;
					bool categoryVisible = anyVisible || showEmptyCategory;
					if (categoryPair.Value.Visible != categoryVisible)
					{
						categoryPair.Value.Visible = categoryVisible;
						changed = true;
					}
				}
			}
			finally
			{
				_treeList.EndUpdate();
			}

			if (changed)
			{
				_treeList.LayoutChanged();
				_treeList.Invalidate();
			}
		}

		/// <summary>
		/// Reconciles category definitions without rebuilding unaffected mod nodes.
		/// </summary>
		private void ReconcileCategories(IEnumerable<string> removedCategoryNames)
		{
			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginUpdate();
			_treeList.BeginUnboundLoad();
			try
			{
				// Only mods under removed/renamed roots can acquire a different effective
				// category. Pure additions and Show Empty toggles therefore avoid any mod scan.
				foreach (string removedCategoryName in removedCategoryNames ?? Enumerable.Empty<string>())
				{
					TreeListNode removedCategoryNode;
					if (!_categoryNodes.TryGetValue(removedCategoryName, out removedCategoryNode))
						continue;

					foreach (TreeListNode modNode in removedCategoryNode.Nodes.Cast<TreeListNode>().ToList())
					{
						IMod mod = modNode.Tag as IMod;
						if (mod == null)
							continue;

						string categoryName = ResolveCategoryName(mod);
						if (!String.Equals(removedCategoryName, categoryName, StringComparison.CurrentCultureIgnoreCase))
							MoveModToCategory(mod, modNode, categoryName, false);
					}
				}

				foreach (KeyValuePair<string, TreeListNode> pair in _categoryNodes.ToList())
				{
					ModCategoryTreeCategory category = pair.Value.Tag as ModCategoryTreeCategory;
					if (category == null || category.ModCount != 0)
						continue;

					if (!_showEmptyCategories || !_availableCategoryNames.Contains(category.Name))
					{
						_categoryNodes.Remove(pair.Key);
						pair.Value.Remove();
					}
				}

				if (_showEmptyCategories)
				{
					foreach (string categoryName in _availableCategoryNames.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
						GetOrCreateCategoryNode(categoryName);
				}
			}
			finally
			{
				_treeList.EndUnboundLoad();
				_treeList.EndUpdate();
				_viewControl.EndInternalDataUpdate();
			}

			ApplyVisibilityFilterAfterStructureChange();
		}

		/// <summary>
		/// Recomputes cached category aggregate counts after activation or new-mod state changes.
		/// </summary>
		private void RefreshCategoryAggregateState()
		{
			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginUpdate();
			try
			{
				ResetCategoryAggregateState();
				foreach (KeyValuePair<IMod, TreeListNode> pair in _modNodes)
					AccumulateCategoryState(pair.Value.ParentNode, pair.Key);
				RefreshCategoryCaptions();
			}
			finally
			{
				_treeList.EndUpdate();
				_viewControl.EndInternalDataUpdate();
			}
		}

		/// <summary>
		/// Resets active/new counters while preserving structural mod totals.
		/// </summary>
		private void ResetCategoryAggregateState()
		{
			foreach (TreeListNode categoryNode in _categoryNodes.Values)
			{
				ModCategoryTreeCategory category = categoryNode.Tag as ModCategoryTreeCategory;
				if (category == null)
					continue;
				category.ActiveModCount = 0;
				category.NewModCount = 0;
			}
		}

		/// <summary>
		/// Adds one mod's current activation and new-mod state to its parent category aggregates.
		/// </summary>
		private void AccumulateCategoryState(TreeListNode categoryNode, IMod mod)
		{
			ModCategoryTreeCategory category = categoryNode?.Tag as ModCategoryTreeCategory;
			if (category == null || mod == null)
				return;

			if (_activeModResolver != null && _activeModResolver(mod))
				category.ActiveModCount++;
			if (_newModResolver != null && _newModResolver(mod))
				category.NewModCount++;
		}

		/// <summary>
		/// Refreshes category captions after aggregate counters have changed.
		/// </summary>
		private void RefreshCategoryCaptions()
		{
			foreach (TreeListNode categoryNode in _categoryNodes.Values)
				UpdateCategoryCaption(categoryNode);
		}

		/// <summary>
		/// Restores surviving selected and focused mods after a tree rebuild without emitting intermediate selection events.
		/// </summary>
		private void RestoreSelection(IList<IMod> selected, IMod focused)
		{
			// Restoring selected nodes can raise several DevExpress selection events. Emit
			// one consolidated notification after the complete selection has been restored.
			_suppressSelectionChanged = true;
			try
			{
				_treeList.Selection.Clear();
				if (selected != null)
				{
					foreach (IMod mod in selected)
					{
						TreeListNode node;
						if (_modNodes.TryGetValue(mod, out node) && node.Visible)
							_treeList.Selection.Add(node);
					}
				}
				TreeListNode focusedNode;
				if (focused != null && _modNodes.TryGetValue(focused, out focusedNode) && focusedNode.Visible)
					_treeList.FocusedNode = focusedNode;
			}
			finally
			{
				_suppressSelectionChanged = false;
			}
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Forwards TreeList selection changes unless notifications are temporarily suppressed.
		/// </summary>
		private void TreeList_SelectionChanged(object sender, EventArgs e)
		{
			if (!_suppressSelectionChanged)
				SelectionChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
