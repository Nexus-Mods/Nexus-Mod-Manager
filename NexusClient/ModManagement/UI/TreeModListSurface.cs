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
		/// <summary>
		/// Captures a stable viewport anchor across TreeList hierarchy and visibility changes.
		/// </summary>
		private sealed class TreeViewportState
		{
			internal IMod TopMod { get; set; }
			internal string TopCategoryName { get; set; }
			internal int TopVisibleIndex { get; set; }
		}

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
		private readonly HashSet<IMod> _pendingModRefreshes = new HashSet<IMod>();
		private IList<IMod> _pendingStructuralMods;
		private bool _pendingFullRefresh;
		private bool _pendingStructuralRefresh;
		private bool _pendingFilterReconciliation;
		private bool _deferredReconcileQueued;
		private bool _applyingDeferredRefresh;
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
			_viewControl.SynchronizeSortSignature();
			_treeList.FocusedNodeChanged += TreeList_SelectionChanged;
			_treeList.SelectionChanged += TreeList_SelectionChanged;
			_treeList.CustomColumnSort += TreeList_CustomColumnSort;
			_viewControl.EditSessionEnded += ViewControl_EditSessionEnded;
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
		/// Occurs after an editor has closed and all deferred Category View work has been reconciled.
		/// </summary>
		internal event EventHandler EditReconciliationCompleted;

		/// <summary>
		/// Gets whether editor-driven work is still pending or currently being reconciled.
		/// </summary>
		internal bool HasPendingDeferredUpdates => HasPendingDeferredRefresh || _deferredReconcileQueued || _applyingDeferredRefresh;

		/// <summary>
		/// Gets whether disruptive TreeList work should be deferred until the native editor finishes.
		/// </summary>
		private bool ShouldDeferDisruptiveUpdate => !_applyingDeferredRefresh && _viewControl.HasActiveEditor;

		/// <summary>
		/// Gets whether any deferred refresh work is waiting for editor completion.
		/// </summary>
		private bool HasPendingDeferredRefresh => _pendingStructuralRefresh || _pendingFullRefresh || _pendingFilterReconciliation || _pendingModRefreshes.Count > 0;

		/// <summary>
		/// Queues a structural rebuild and supersedes all less comprehensive deferred refreshes.
		/// </summary>
		private void QueueDeferredStructuralRefresh(IEnumerable<IMod> mods = null)
		{
			_pendingStructuralRefresh = true;
			_pendingStructuralMods = mods != null && !ReferenceEquals(mods, _mods)
				? mods.Where(mod => mod != null).ToList()
				: null;
			_pendingFullRefresh = false;
			_pendingFilterReconciliation = false;
			_pendingModRefreshes.Clear();
		}

		/// <summary>
		/// Queues one full value reconciliation and supersedes individual mod refreshes.
		/// </summary>
		private void QueueDeferredFullRefresh()
		{
			if (_pendingStructuralRefresh)
				return;

			_pendingFullRefresh = true;
			_pendingModRefreshes.Clear();
		}

		/// <summary>
		/// Queues visibility reconciliation independently so clearing the final filter still restores hidden nodes.
		/// </summary>
		private void QueueDeferredFilterReconciliation()
		{
			if (!_pendingStructuralRefresh)
				_pendingFilterReconciliation = true;
		}

		/// <summary>
		/// Queues one mod refresh, promoting category changes to a structural rebuild.
		/// </summary>
		private void QueueDeferredModRefresh(IMod mod, string propertyName)
		{
			if (String.Equals(propertyName, "CategoryId", StringComparison.Ordinal) ||
				String.Equals(propertyName, "CustomCategoryId", StringComparison.Ordinal))
			{
				QueueDeferredStructuralRefresh();
				return;
			}

			if (!_pendingStructuralRefresh && !_pendingFullRefresh && mod != null)
				_pendingModRefreshes.Add(mod);
		}

		/// <summary>
		/// Schedules deferred refresh reconciliation after DevExpress has fully closed the native editor.
		/// </summary>
		private void ViewControl_EditSessionEnded(object sender, EventArgs e)
		{
			QueueDeferredReconciliation();
		}

		/// <summary>
		/// Posts a single deferred-refresh reconciliation callback to avoid running inside HiddenEditor.
		/// </summary>
		private void QueueDeferredReconciliation()
		{
			if (_deferredReconcileQueued || _viewControl.IsDisposed || _viewControl.Disposing)
				return;

			_deferredReconcileQueued = true;
			_viewControl.BeginInvoke(new Action(ApplyDeferredRefreshes));
		}

		/// <summary>
		/// Applies the highest-priority coalesced refresh after the editor has ended.
		/// </summary>
		private void ApplyDeferredRefreshes()
		{
			_deferredReconcileQueued = false;
			if (_viewControl.IsDisposed || _viewControl.Disposing || _viewControl.HasActiveEditor)
				return;

			if (!HasPendingDeferredRefresh)
			{
				EditReconciliationCompleted?.Invoke(this, EventArgs.Empty);
				return;
			}

			bool rebuild = _pendingStructuralRefresh;
			bool fullRefresh = _pendingFullRefresh;
			bool reconcileFilter = _pendingFilterReconciliation;
			IList<IMod> structuralMods = _pendingStructuralMods;
			IList<IMod> modRefreshes = _pendingModRefreshes.Where(mod => mod != null && _mods.Contains(mod)).ToList();

			_pendingStructuralRefresh = false;
			_pendingStructuralMods = null;
			_pendingFullRefresh = false;
			_pendingFilterReconciliation = false;
			_pendingModRefreshes.Clear();

			_applyingDeferredRefresh = true;
			try
			{
				if (rebuild)
				{
					SetMods(structuralMods ?? _mods);
				}
				else if (fullRefresh)
				{
					RefreshData(reconcileFilter);
				}
				else if (modRefreshes.Count > 0)
				{
					RefreshModValues(modRefreshes, reconcileFilter);
				}
				else if (reconcileFilter)
				{
					ApplyDeferredVisibilityFilter();
				}
			}
			finally
			{
				_applyingDeferredRefresh = false;
			}

			if (HasPendingDeferredRefresh && !_viewControl.HasActiveEditor)
			{
				QueueDeferredReconciliation();
				return;
			}

			EditReconciliationCompleted?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Reconciles a filter-only deferred edit inside the internal guard so viewport restoration is not mistaken for user navigation.
		/// </summary>
		private void ApplyDeferredVisibilityFilter()
		{
			_viewControl.BeginInternalDataUpdate();
			try
			{
				_viewControl.InvalidateGestureTarget();
				ApplyVisibilityFilter();
			}
			finally
			{
				_viewControl.EndInternalDataUpdate();
			}
		}

		/// <summary>
		/// Rebuilds the Category Tree from the supplied mods while preserving expansion, focus and selection state.
		/// </summary>
		public void SetMods(IEnumerable<IMod> mods)
		{
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredStructuralRefresh(mods);
				return;
			}

			_viewControl.InvalidateGestureTarget();
			TreeViewportState viewport = CaptureViewportState();
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
			RestoreViewportState(viewport);
		}

		/// <summary>
		/// Adds mod nodes incrementally without rebuilding unaffected categories.
		/// </summary>
		public void AddMods(IEnumerable<IMod> mods)
		{
			if (mods == null) return;
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredStructuralRefresh();
				return;
			}

			_viewControl.InvalidateGestureTarget();
			TreeViewportState viewport = CaptureViewportState();
			TreeListNode focused = _treeList.FocusedNode;
			IList<TreeListNode> selected = _treeList.Selection.Cast<TreeListNode>().ToList();
			bool wasSuppressingSelectionChanged = _suppressSelectionChanged;
			_suppressSelectionChanged = true;
			_viewControl.BeginInternalDataUpdate();
			try
			{
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
				}
				ApplyVisibilityFilterAfterStructureChange();
				RestoreNodeSelection(selected, focused);
				RestoreViewportState(viewport);
			}
			finally
			{
				_viewControl.EndInternalDataUpdate();
				_suppressSelectionChanged = wasSuppressingSelectionChanged;
			}
			if (!wasSuppressingSelectionChanged)
				SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Removes mod nodes incrementally and removes empty category nodes when appropriate.
		/// </summary>
		public void RemoveMods(IEnumerable<IMod> mods)
		{
			if (mods == null) return;
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredStructuralRefresh();
				return;
			}

			_viewControl.InvalidateGestureTarget();
			TreeViewportState viewport = CaptureViewportState();
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
			RestoreViewportState(viewport);
		}

		/// <summary>
		/// Refreshes a mod node and preserves the current interaction state when the update only changes values.
		/// </summary>
		public void RefreshMod(IMod mod, string propertyName)
		{
			if (mod == null) return;
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredModRefresh(mod, propertyName);
				return;
			}

			TreeListNode node;
			if (!_modNodes.TryGetValue(mod, out node))
			{
				if (_mods.Contains(mod))
					AddMods(new[] { mod });
				return;
			}

			bool categoryChanged = String.Equals(propertyName, "CategoryId", StringComparison.Ordinal) ||
				String.Equals(propertyName, "CustomCategoryId", StringComparison.Ordinal);
			if (!categoryChanged)
			{
				RefreshModValues(new[] { mod }, false);
				return;
			}

			string categoryName = ResolveCategoryName(mod);
			if (node.ParentNode?.Tag is ModCategoryTreeCategory currentCategory &&
				String.Equals(currentCategory.Name, categoryName, StringComparison.CurrentCultureIgnoreCase))
			{
				RefreshModValues(new[] { mod }, false);
				return;
			}

			TreeViewportState viewport = CaptureViewportState();
			TreeListNode focused = _treeList.FocusedNode;
			IList<TreeListNode> selected = _treeList.Selection.Cast<TreeListNode>().ToList();
			IMod focusedModBefore = FocusedMod;
			IList<IMod> selectedModsBefore = SelectedMods;
			bool wasSuppressingSelectionChanged = _suppressSelectionChanged;
			_suppressSelectionChanged = true;

			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginSort();
			try
			{
				_treeList.BeginUnboundLoad();
				try
				{
					MoveModToCategory(mod, node, categoryName, false);
				}
				finally
				{
					_treeList.EndUnboundLoad();
				}

				ApplyVisibilityFilterAfterStructureChange(false);
				_viewControl.InvalidateGestureTarget();
			}
			finally
			{
				try
				{
					_treeList.EndSort();
					RestoreNodeSelection(selected, focused, true, viewport);
					RestoreViewportState(viewport);
				}
				finally
				{
					_viewControl.EndInternalDataUpdate();
					_suppressSelectionChanged = wasSuppressingSelectionChanged;
				}
			}

			if (!wasSuppressingSelectionChanged && HasEffectiveModSelectionChanged(focusedModBefore, selectedModsBefore))
				SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Reconciles one or more value-only mod updates in one TreeList transaction and sorts only when an active sort key changed.
		/// </summary>
		private void RefreshModValues(IEnumerable<IMod> mods, bool forceVisibilityReconciliation)
		{
			var refreshItems = (mods ?? Enumerable.Empty<IMod>())
				.Where(mod => mod != null && _mods.Contains(mod))
				.Distinct()
				.Select(mod => new KeyValuePair<IMod, TreeListNode>(mod, _modNodes.TryGetValue(mod, out TreeListNode node) ? node : null))
				.ToList();
			if (refreshItems.Count == 0)
			{
				if (forceVisibilityReconciliation)
					ApplyVisibilityFilter();
				return;
			}

			if (refreshItems.Any(item => item.Value == null))
			{
				SetMods(_mods);
				return;
			}

			TreeViewportState viewport = CaptureViewportState();
			TreeListNode focused = _treeList.FocusedNode;
			IList<TreeListNode> selected = _treeList.Selection.Cast<TreeListNode>().ToList();
			IMod focusedModBefore = FocusedMod;
			IList<IMod> selectedModsBefore = SelectedMods;
			bool sortRequired = refreshItems.Any(item => HasChangedSortedModValue(item.Value, item.Key));
			bool focusedNodeModified = false;
			bool visibilityChanged = false;
			bool wasSuppressingSelectionChanged = _suppressSelectionChanged;
			_suppressSelectionChanged = true;

			_viewControl.BeginInternalDataUpdate();
			if (sortRequired)
				_treeList.BeginSort();
			try
			{
				foreach (KeyValuePair<IMod, TreeListNode> item in refreshItems)
				{
					bool nodeModified = UpdateModNode(item.Value, item.Key);
					if (nodeModified && ReferenceEquals(item.Value, focused))
						focusedNodeModified = true;
					if (nodeModified)
						_treeList.RefreshNode(item.Value);
				}

				visibilityChanged = forceVisibilityReconciliation
					? ApplyVisibilityFilter(false)
					: ApplyVisibilityFilterAfterStructureChange(false);
				if (visibilityChanged)
					_viewControl.InvalidateGestureTarget();
			}
			finally
			{
				try
				{
					if (sortRequired)
						_treeList.EndSort();
					SettleProgrammaticFocusedNodeEdit(focusedNodeModified);
					RestoreNodeSelection(selected, focused, visibilityChanged, viewport);
					if (visibilityChanged)
						RestoreViewportState(viewport);
					else
						RestoreViewportIndex(viewport);
				}
				finally
				{
					_viewControl.EndInternalDataUpdate();
					_suppressSelectionChanged = wasSuppressingSelectionChanged;
				}
			}

			if (!wasSuppressingSelectionChanged && HasEffectiveModSelectionChanged(focusedModBefore, selectedModsBefore))
				SelectionChanged?.Invoke(this, EventArgs.Empty);
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
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredFilterReconciliation();
				return;
			}

			_viewControl.InvalidateGestureTarget();
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
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredFilterReconciliation();
				return;
			}

			_viewControl.InvalidateGestureTarget();
			ApplyVisibilityFilter();
		}

		/// <summary>
		/// Sets the category-level visibility rule composed with all mod filters.
		/// </summary>
		internal void SetCategoryVisibilityPredicate(Func<ModCategoryTreeCategory, bool> predicate)
		{
			_categoryVisibilityPredicate = predicate;
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredFilterReconciliation();
				return;
			}

			_viewControl.InvalidateGestureTarget();
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

			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredStructuralRefresh();
				return;
			}

			_viewControl.InvalidateGestureTarget();
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
			_viewControl.InvalidateGestureTarget();
			TreeListNode node;
			if (!_modNodes.TryGetValue(mod, out node)) return;
			if (node.ParentNode != null) node.ParentNode.Expanded = true;
			_treeList.Selection.Clear();
			_treeList.Selection.Add(node);
			_treeList.FocusedNode = node;
			// Assigning the same focused node does not scroll after a sort moves it off-screen.
			_treeList.MakeNodeVisible(node);
		}

		/// <summary>
		/// Rebuilds the Category Tree from the shared backing mod list.
		/// </summary>
		public void RefreshDataSource()
		{
			SetMods(_mods);
		}

		/// <summary>
		/// Refreshes all mod-node values and fully settles sort state before restoring selection and viewport.
		/// </summary>
		public void RefreshData()
		{
			RefreshData(false);
		}

		/// <summary>
		/// Refreshes all values and optionally forces visibility reconciliation even when the resulting filter set is empty.
		/// </summary>
		private void RefreshData(bool forceVisibilityReconciliation)
		{
			if (ShouldDeferDisruptiveUpdate)
			{
				QueueDeferredFullRefresh();
				return;
			}

			TreeViewportState viewport = CaptureViewportState();
			TreeListNode focused = _treeList.FocusedNode;
			IList<TreeListNode> selected = _treeList.Selection.Cast<TreeListNode>().ToList();
			bool focusedNodeModified = false;
			bool visibilityChanged = false;
			bool wasSuppressingSelectionChanged = _suppressSelectionChanged;
			_suppressSelectionChanged = true;

			_viewControl.BeginInternalDataUpdate();
			_treeList.BeginSort();
			try
			{
				// Batch unbound cell changes while sorting is locked so repeated property
				// updates cannot repeatedly rearrange the tree.
				_treeList.BeginUnboundLoad();
				try
				{
					ResetCategoryAggregateState();
					foreach (KeyValuePair<IMod, TreeListNode> pair in _modNodes.ToList())
					{
						bool nodeModified = UpdateModNode(pair.Value, pair.Key);
						if (nodeModified && ReferenceEquals(pair.Value, focused))
							focusedNodeModified = true;
						AccumulateCategoryState(pair.Value.ParentNode, pair.Key);
					}
					focusedNodeModified |= RefreshCategoryCaptions(focused);
				}
				finally
				{
					_treeList.EndUnboundLoad();
				}

				visibilityChanged = forceVisibilityReconciliation
					? ApplyVisibilityFilter(false)
					: ApplyVisibilityFilterAfterStructureChange(false);
				if (visibilityChanged)
					_viewControl.InvalidateGestureTarget();
			}
			finally
			{
				try
				{
					_treeList.EndSort();
					SettleProgrammaticFocusedNodeEdit(focusedNodeModified);
					RestoreNodeSelection(selected, focused, visibilityChanged, viewport);
					if (visibilityChanged)
						RestoreViewportState(viewport);
					else
						RestoreViewportIndex(viewport);
					_treeList.Invalidate();
				}
				finally
				{
					_viewControl.EndInternalDataUpdate();
					_suppressSelectionChanged = wasSuppressingSelectionChanged;
				}
			}
			if (!wasSuppressingSelectionChanged)
				SelectionChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Finalizes programmatic changes on the focused node without forcing another sort or disturbing a real editor.
		/// </summary>
		private void SettleProgrammaticFocusedNodeEdit(bool focusedNodeModified)
		{
			if (focusedNodeModified && _treeList.ActiveEditor == null)
				_treeList.EndCurrentEdit(false);
		}

		/// <summary>
		/// Restores surviving node identities after a refresh and optionally chooses a visible fallback after a structural change.
		/// </summary>
		private void RestoreNodeSelection(IList<TreeListNode> selected, TreeListNode focused, bool allowFallback = false, TreeViewportState viewport = null)
		{
			TreeListNode focusedNode = ResolveCurrentNode(focused);
			if (focusedNode == null || _treeList.GetVisibleIndexByNode(focusedNode) < 0)
				focusedNode = allowFallback && focused != null ? FindVisibleFallbackNode(viewport?.TopVisibleIndex ?? 0) : null;

			_treeList.FocusedNode = focusedNode;
			_treeList.Selection.Clear();
			foreach (TreeListNode capturedNode in selected ?? Enumerable.Empty<TreeListNode>())
			{
				TreeListNode currentNode = ResolveCurrentNode(capturedNode);
				if (currentNode != null && _treeList.GetVisibleIndexByNode(currentNode) >= 0)
					_treeList.Selection.Add(currentNode);
			}
		}

		/// <summary>
		/// Resolves a captured node to its current mod or category node after a structural refresh.
		/// </summary>
		private TreeListNode ResolveCurrentNode(TreeListNode capturedNode)
		{
			if (capturedNode == null)
				return null;

			if (capturedNode.Tag is IMod mod)
			{
				TreeListNode modNode;
				return _modNodes.TryGetValue(mod, out modNode) ? modNode : null;
			}

			if (capturedNode.Tag is ModCategoryTreeCategory category)
			{
				TreeListNode categoryNode;
				return _categoryNodes.TryGetValue(category.Name, out categoryNode) ? categoryNode : null;
			}

			return null;
		}

		/// <summary>
		/// Finds a deterministic visible focus fallback near the previous viewport, preferring mod rows over category rows.
		/// </summary>
		private TreeListNode FindVisibleFallbackNode(int preferredVisibleIndex)
		{
			int lastVisibleIndex = GetLastDisplayedNodeIndex();
			if (lastVisibleIndex < 0)
				return null;

			int startIndex = Math.Max(0, Math.Min(preferredVisibleIndex, lastVisibleIndex));
			for (int index = startIndex; index <= lastVisibleIndex; index++)
			{
				TreeListNode node = GetNodeByVisibleIndexSafe(index);
				if (node?.Tag is IMod)
					return node;
			}

			for (int index = startIndex - 1; index >= 0; index--)
			{
				TreeListNode node = GetNodeByVisibleIndexSafe(index);
				if (node?.Tag is IMod)
					return node;
			}

			return GetNodeByVisibleIndexSafe(startIndex);
		}

		/// <summary>
		/// Gets a visible node while tolerating stale DevExpress visible indexes during a structural update.
		/// </summary>
		private TreeListNode GetNodeByVisibleIndexSafe(int visibleIndex)
		{
			try
			{
				return _treeList.GetNodeByVisibleIndex(visibleIndex);
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}

		/// <summary>
		/// Determines whether a refresh changed the effective focused or selected mod identities.
		/// </summary>
		private bool HasEffectiveModSelectionChanged(IMod focusedBefore, IList<IMod> selectedBefore)
		{
			if (!ReferenceEquals(focusedBefore, FocusedMod))
				return true;

			IList<IMod> selectedAfter = SelectedMods;
			if ((selectedBefore?.Count ?? 0) != selectedAfter.Count)
				return true;

			foreach (IMod mod in selectedBefore ?? Enumerable.Empty<IMod>())
			{
				if (!selectedAfter.Any(selectedMod => ReferenceEquals(selectedMod, mod)))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Invalidates the rendered TreeList rows without mutating sorted node data.
		/// </summary>
		public void InvalidateRows()
		{
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
			_viewControl.InvalidateGestureTarget();
			_treeList.CollapseAll();
		}

		/// <summary>
		/// Expands all root category nodes.
		/// </summary>
		public void ExpandAllCategories()
		{
			_viewControl.InvalidateGestureTarget();
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
			TreeViewportState viewport = CaptureViewportState();
			var collapsed = new HashSet<string>(categoryNames ?? Enumerable.Empty<string>(), StringComparer.CurrentCultureIgnoreCase);
			_pendingCollapsedCategoryNames.Clear();
			_pendingCollapsedCategoryNames.UnionWith(collapsed);

			foreach (KeyValuePair<string, TreeListNode> pair in _categoryNodes)
			{
				bool expanded = !collapsed.Contains(pair.Key);
				if (pair.Value.Expanded != expanded)
					pair.Value.Expanded = expanded;
			}
			RestoreViewportState(viewport);
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

			_viewControl.InvalidateGestureTarget();
			_viewControl.BeginInternalDataUpdate();
			try
			{
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
			finally
			{
				_viewControl.EndInternalDataUpdate();
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
		/// Determines whether the specified field is the primary TreeList sort column.
		/// </summary>
		internal bool IsPrimarySortColumn(string fieldName)
		{
			TreeListColumn column = _treeList.SortedColumnCount > 0 ? _treeList.GetSortColumn(0) : null;
			return column != null && String.Equals(column.FieldName, fieldName, StringComparison.Ordinal);
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
			_treeList.OptionsBehavior.AutoScrollOnSorting = false;
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
			_viewControl.InvalidateGestureTarget();
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
		/// Updates a category node caption with its current mod count and reports whether the cell value changed.
		/// </summary>
		private bool UpdateCategoryCaption(TreeListNode categoryNode)
		{
			ModCategoryTreeCategory category = categoryNode?.Tag as ModCategoryTreeCategory;
			if (category == null) return false;

			string countText = _categoryCountFormatter(category.ActiveModCount, category.ModCount);
			bool changed = SetValueIfChanged(
				categoryNode,
				ModCategoryTreeColumns.ModName,
				String.IsNullOrWhiteSpace(countText)
					? category.Name
					: String.Format("{0} ({1})", category.Name, countText));
			_viewControl.UpdateCategoryModCountIcon(categoryNode, category.ModCount);
			return changed;
		}

		/// <summary>
		/// Captures the current top visible row without relying on TopVisibleNode, which can throw when DevExpress retains a stale index.
		/// </summary>
		private TreeViewportState CaptureViewportState()
		{
			var state = new TreeViewportState { TopVisibleIndex = Math.Max(0, _treeList.TopVisibleNodeIndex) };
			TreeListNode topNode = null;
			try
			{
				topNode = _treeList.GetNodeByVisibleIndex(state.TopVisibleIndex);
			}
			catch (ArgumentOutOfRangeException)
			{
				// A structural update can leave DevExpress with a stale top-visible index until it is normalized below.
			}

			IMod topMod = topNode?.Tag as IMod;
			if (topMod != null)
				state.TopMod = topMod;
			else
				state.TopCategoryName = (topNode?.Tag as ModCategoryTreeCategory)?.Name;
			return state;
		}

		/// <summary>
		/// Restores a valid top-visible row after hierarchy or filter changes and clamps removed anchors to the last visible node.
		/// </summary>
		private void RestoreViewportState(TreeViewportState state)
		{
			if (state == null)
				return;

			TreeListNode targetNode = null;
			if (state.TopMod != null)
				_modNodes.TryGetValue(state.TopMod, out targetNode);
			else if (!String.IsNullOrEmpty(state.TopCategoryName))
				_categoryNodes.TryGetValue(state.TopCategoryName, out targetNode);

			int targetIndex = targetNode == null ? -1 : _treeList.GetVisibleIndexByNode(targetNode);
			if (targetIndex < 0)
			{
				int lastVisibleIndex = GetLastDisplayedNodeIndex();
				if (lastVisibleIndex < 0)
				{
					_treeList.TopVisibleNodeIndex = 0;
					return;
				}

				targetIndex = Math.Max(0, Math.Min(state.TopVisibleIndex, lastVisibleIndex));
			}

			_treeList.TopVisibleNodeIndex = targetIndex;
		}

		/// <summary>
		/// Restores the previous top visible index after a sort-driven row move without following the moved mod identity.
		/// </summary>
		private void RestoreViewportIndex(TreeViewportState state)
		{
			if (state == null)
				return;

			int lastVisibleIndex = GetLastDisplayedNodeIndex();
			if (lastVisibleIndex < 0)
			{
				_treeList.TopVisibleNodeIndex = 0;
				return;
			}

			_treeList.TopVisibleNodeIndex = Math.Max(0, Math.Min(state.TopVisibleIndex, lastVisibleIndex));
		}

		/// <summary>
		/// Gets the last displayed-row index without counting children hidden inside collapsed categories.
		/// </summary>
		private int GetLastDisplayedNodeIndex()
		{
			return _treeList.VisibleNodesCount > 0 ? _treeList.VisibleNodesCount - 1 : -1;
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
		/// Gets whether at least one currently sorted mod value would change during this refresh.
		/// </summary>
		private bool HasChangedSortedModValue(TreeListNode node, IMod mod)
		{
			if (node == null || mod == null)
				return false;

			for (int index = 0; index < _treeList.SortedColumnCount; index++)
			{
				TreeListColumn column = _treeList.GetSortColumn(index);
				if (column != null && !Object.Equals(node.GetValue(column.FieldName), GetModColumnValue(mod, column.FieldName)))
					return true;
			}
			return false;
		}

		/// <summary>
		/// Resolves the displayed value used by one Category View mod column.
		/// </summary>
		private object GetModColumnValue(IMod mod, string fieldName)
		{
			switch (fieldName)
			{
				case ModCategoryTreeColumns.Status: return _statusTextResolver?.Invoke(mod) ?? String.Empty;
				case ModCategoryTreeColumns.ModName: return mod.ModName;
				case ModCategoryTreeColumns.Version: return mod.HumanReadableVersion;
				case ModCategoryTreeColumns.Latest: return mod.LastKnownVersion;
				case ModCategoryTreeColumns.Author: return mod.Author;
				case ModCategoryTreeColumns.InstallDate: return mod.InstallDate;
				case ModCategoryTreeColumns.DownloadDate: return mod.DownloadDate;
				case ModCategoryTreeColumns.DownloadId: return mod.DownloadId;
				case ModCategoryTreeColumns.Endorsed: return mod.IsEndorsed;
				default: return null;
			}
		}

		/// <summary>
		/// Synchronizes changed displayed values of an existing mod node and reports whether any cell changed.
		/// </summary>
		private bool UpdateModNode(TreeListNode node, IMod mod)
		{
			if (node == null || mod == null) return false;

			bool changed = false;
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.Status, _statusTextResolver?.Invoke(mod) ?? String.Empty);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.ModName, mod.ModName);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.Version, mod.HumanReadableVersion);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.Latest, mod.LastKnownVersion);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.Author, mod.Author);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.InstallDate, mod.InstallDate);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.DownloadDate, mod.DownloadDate);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.DownloadId, mod.DownloadId);
			changed |= SetValueIfChanged(node, ModCategoryTreeColumns.Endorsed, mod.IsEndorsed);
			return changed;
		}

		/// <summary>
		/// Writes an unbound TreeList cell only when its effective value has changed.
		/// </summary>
		private static bool SetValueIfChanged(TreeListNode node, string fieldName, object value)
		{
			if (node == null || Object.Equals(node.GetValue(fieldName), value))
				return false;

			node.SetValue(fieldName, value);
			return true;
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
		private bool ApplyVisibilityFilterAfterStructureChange(bool restoreViewport = true)
		{
			if (String.IsNullOrEmpty(_textFilter) && _visibilityPredicate == null && _categoryVisibilityPredicate == null)
				return false;

			return ApplyVisibilityFilter(restoreViewport);
		}

		/// <summary>
		/// Applies text and predicate filters to mod nodes, optionally restoring the structural viewport anchor.
		/// </summary>
		private bool ApplyVisibilityFilter(bool restoreViewport = true)
		{
			TreeViewportState viewport = restoreViewport ? CaptureViewportState() : null;
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
				if (restoreViewport)
					RestoreViewportState(viewport);
			}

			return changed;
		}

		/// <summary>
		/// Reconciles category definitions without rebuilding unaffected mod nodes.
		/// </summary>
		private void ReconcileCategories(IEnumerable<string> removedCategoryNames)
		{
			TreeViewportState viewport = CaptureViewportState();
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
			RestoreViewportState(viewport);
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
		/// Refreshes category captions and reports whether the currently focused category value changed.
		/// </summary>
		private bool RefreshCategoryCaptions(TreeListNode focusedNode)
		{
			bool focusedNodeModified = false;
			foreach (TreeListNode categoryNode in _categoryNodes.Values)
			{
				bool changed = UpdateCategoryCaption(categoryNode);
				if (changed && ReferenceEquals(categoryNode, focusedNode))
					focusedNodeModified = true;
			}
			return focusedNodeModified;
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
