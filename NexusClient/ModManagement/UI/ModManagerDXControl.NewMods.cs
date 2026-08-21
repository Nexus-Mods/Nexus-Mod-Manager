namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.ComponentModel;
	using System.Drawing;
	using System.IO;
	using System.Windows.Forms;

	using DevExpress.XtraBars;
	using DevExpress.XtraGrid.Views.Base;
	using DevExpress.XtraGrid.Views.Grid;

	using Nexus.Client.Mods;
	using Nexus.Client.UI;

	public partial class ModManagerDXControl
	{
		private readonly HashSet<IMod> _sessionNewMods =
			new HashSet<IMod>();

		private readonly HashSet<IMod> _newModsFilterSnapshot =
			new HashSet<IMod>();

		private readonly HashSet<string> _knownSessionModFiles =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		private ModManagerVM _newModsTrackedViewModel;
		private BarButtonItem _showOnlyCategoriesWithNewModsMenuItem;
		private bool _newModCategoryViewInitialized;
		private bool _showOnlyCategoriesWithNewMods;

		private void InitializeNewModCategoryView()
		{
			if (_newModCategoryViewInitialized)
				return;

			_newModCategoryViewInitialized = true;

			ApplyCategoryMenuLabels();

			_showOnlyCategoriesWithNewModsMenuItem = new BarButtonItem(barManagerMods, "Show only categories with new mods")
			{
				ButtonStyle = BarButtonStyle.Check,
				Down = false
			};
			_showOnlyCategoriesWithNewModsMenuItem.ItemClick +=
				(sender, args) => ShowOnlyCategoriesWithNewMods_Click(sender, EventArgs.Empty);
			NmmIconProvider.Bind(_showOnlyCategoriesWithNewModsMenuItem, NmmIconAction.Filter);

			// Rebuild the persistent category popup once so the session-only filter
			// stays in the same position as the legacy ToolStrip implementation.
			popupCategories.ClearLinks();
			popupCategories.AddItem(addNewCategory);
			popupCategories.AddItem(collapseAllCategories);
			popupCategories.AddItem(expandAllCategories);
			popupCategories.AddItem(updateNexusAndCustomCategories);
			popupCategories.AddItem(_showOnlyCategoriesWithNewModsMenuItem);
			popupCategories.AddItem(resetDefaultCategories);
			popupCategories.AddItem(resetUnassignedToDefaultCategories);
			popupCategories.AddItem(resetModsCategory);
			popupCategories.AddItem(removeAllCategories);
			popupCategories.AddItem(toggleHiddenCategories);

			popupCategories.BeforePopup +=
				(sender, args) => CategoriesMenu_DropDownOpening(sender, EventArgs.Empty);

			// The designer wires the normal switch handler first. This listener
			// therefore runs after the view state has actually changed.
			tsbSwitchView.ItemClick +=
				(sender, args) => CategoryViewSwitchCompleted(sender, EventArgs.Empty);

			gridView.CustomRowFilter += GridView_NewModsCustomRowFilter;
			gridView.CustomDrawGroupRow += GridView_NewModsCustomDrawGroupRow;
			gridView.RowCellStyle += GridView_NewModsRowCellStyle;
			gridControl.MouseDown += GridControl_NewModsMouseDown;
			gridView.KeyUp += GridView_NewModsKeyUp;

			Disposed += (sender, args) => DetachNewModCategoryTracking();

			UpdateCategoryMenuVisibility();
		}

		private void AttachNewModCategoryTracking()
		{
			if (_newModsTrackedViewModel == _viewModel)
				return;

			DetachNewModCategoryTracking();
			_newModsTrackedViewModel = _viewModel;

			_sessionNewMods.Clear();
			_newModsFilterSnapshot.Clear();
			_knownSessionModFiles.Clear();
			SetShowOnlyCategoriesWithNewMods(false);

			if (_newModsTrackedViewModel == null)
				return;

			foreach (IMod mod in _newModsTrackedViewModel.ManagedMods)
				RegisterKnownSessionMod(mod);

			_newModsTrackedViewModel.ManagedMods.CollectionChanged +=
				NewModsManagedMods_CollectionChanged;

			UpdateCategoryMenuVisibility();
		}

		private void DetachNewModCategoryTracking()
		{
			if (_newModsTrackedViewModel != null)
			{
				_newModsTrackedViewModel.ManagedMods.CollectionChanged -=
					NewModsManagedMods_CollectionChanged;
			}

			foreach (IMod mod in _sessionNewMods)
			{
				if (mod != null)
					mod.PropertyChanged -= TrackedNewMod_PropertyChanged;
			}

			_newModsTrackedViewModel = null;
			_sessionNewMods.Clear();
			_newModsFilterSnapshot.Clear();
			_knownSessionModFiles.Clear();
			_showOnlyCategoriesWithNewMods = false;

			if (_showOnlyCategoriesWithNewModsMenuItem != null)
				_showOnlyCategoriesWithNewModsMenuItem.Down = false;
		}

		private void NewModsManagedMods_CollectionChanged(
			object sender,
			NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				BeginInvoke(
					(Action<object, NotifyCollectionChangedEventArgs>)
						NewModsManagedMods_CollectionChanged,
					sender,
					e);

				return;
			}

			bool refreshFilter = false;

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					if (e.NewItems != null)
					{
						foreach (IMod mod in e.NewItems)
						{
							if (!RegisterKnownSessionMod(mod))
								continue;

							MarkModAsNew(mod);

							if (_showOnlyCategoriesWithNewMods)
							{
								_newModsFilterSnapshot.Add(mod);
								refreshFilter = true;
							}
						}
					}
					break;

				case NotifyCollectionChangedAction.Remove:
					if (e.OldItems != null)
					{
						foreach (IMod mod in e.OldItems)
						{
							RemoveTrackedMod(mod, true);
							refreshFilter = true;
						}
					}
					break;

				case NotifyCollectionChangedAction.Replace:
					if (e.OldItems != null)
					{
						foreach (IMod mod in e.OldItems)
							RemoveTrackedMod(mod, false);
					}

					if (e.NewItems != null)
					{
						foreach (IMod mod in e.NewItems)
							RegisterKnownSessionMod(mod);
					}

					refreshFilter = true;
					break;

				case NotifyCollectionChangedAction.Reset:
					ResetNewModSessionBaseline();
					refreshFilter = true;
					break;
			}

			if (refreshFilter && _showOnlyCategoriesWithNewMods)
				gridView.RefreshData();

			gridView.InvalidateRows();
			gridView.Invalidate();
		}

		private bool RegisterKnownSessionMod(IMod mod)
		{
			if (mod == null)
				return false;

			string key = GetSessionModKey(mod);
			return !String.IsNullOrEmpty(key) &&
				_knownSessionModFiles.Add(key);
		}

		private static string GetSessionModKey(IMod mod)
		{
			if (mod == null || String.IsNullOrWhiteSpace(mod.Filename))
				return String.Empty;

			try
			{
				return Path.GetFullPath(mod.Filename);
			}
			catch
			{
				return mod.Filename.Trim();
			}
		}

		private void MarkModAsNew(IMod mod)
		{
			if (mod == null || !_sessionNewMods.Add(mod))
				return;

			mod.PropertyChanged += TrackedNewMod_PropertyChanged;
		}

		private void RemoveTrackedMod(IMod mod, bool removeKnownKey)
		{
			if (mod == null)
				return;

			if (_sessionNewMods.Remove(mod))
				mod.PropertyChanged -= TrackedNewMod_PropertyChanged;

			_newModsFilterSnapshot.Remove(mod);

			if (removeKnownKey)
			{
				string key = GetSessionModKey(mod);
				if (!String.IsNullOrEmpty(key))
					_knownSessionModFiles.Remove(key);
			}
		}

		private void ResetNewModSessionBaseline()
		{
			foreach (IMod mod in _sessionNewMods)
			{
				if (mod != null)
					mod.PropertyChanged -= TrackedNewMod_PropertyChanged;
			}

			_sessionNewMods.Clear();
			_newModsFilterSnapshot.Clear();
			_knownSessionModFiles.Clear();

			if (_newModsTrackedViewModel != null)
			{
				foreach (IMod mod in _newModsTrackedViewModel.ManagedMods)
					RegisterKnownSessionMod(mod);
			}

			SetShowOnlyCategoriesWithNewMods(false);
		}

		private void TrackedNewMod_PropertyChanged(
			object sender,
			PropertyChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				BeginInvoke(
					(Action<object, PropertyChangedEventArgs>)
						TrackedNewMod_PropertyChanged,
					sender,
					e);

				return;
			}

			gridView.RefreshData();
			gridView.InvalidateRows();
			gridView.Invalidate();
		}

		private void GridControl_NewModsMouseDown(
			object sender,
			MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
				return;

			var hitInfo = gridView.CalcHitInfo(e.Location);
			if (!hitInfo.InRow ||
				hitInfo.RowHandle < 0 ||
				gridView.IsGroupRow(hitInfo.RowHandle))
			{
				return;
			}

			int clickedRowHandle = hitInfo.RowHandle;
			BeginInvoke((MethodInvoker)(() =>
				AcknowledgeSelectedNewMods(clickedRowHandle)));
		}

		private void GridView_NewModsKeyUp(object sender, KeyEventArgs e)
		{
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
					AcknowledgeSelectedNewMods(gridView.FocusedRowHandle);
					break;
			}
		}

		private void AcknowledgeSelectedNewMods(int fallbackRowHandle)
		{
			bool changed = false;
			List<IMod> selectedMods = SelectedMods;

			if (selectedMods.Count == 0)
			{
				IMod fallbackMod = GetModAtRow(fallbackRowHandle);
				if (fallbackMod != null)
					selectedMods.Add(fallbackMod);
			}

			foreach (IMod mod in selectedMods)
			{
				if (!_sessionNewMods.Remove(mod))
					continue;

				mod.PropertyChanged -= TrackedNewMod_PropertyChanged;
				changed = true;
			}

			if (!changed)
				return;

			// Deliberately do not refresh the custom filter here. In filtered
			// mode the current snapshot must remain stable until the toggle is
			// disabled and enabled again.
			gridView.InvalidateRows();
			gridView.Invalidate();
		}

		private IMod GetModAtRow(int rowHandle)
		{
			if (rowHandle < 0 || gridView.IsGroupRow(rowHandle))
				return null;

			int sourceIndex = gridView.GetDataSourceRowIndex(rowHandle);
			if (sourceIndex < 0 || sourceIndex >= _modList.Count)
				return null;

			return _modList[sourceIndex];
		}

		private void GridView_NewModsCustomRowFilter(
			object sender,
			RowFilterEventArgs e)
		{
			if (!_showOnlyCategoriesWithNewMods)
				return;

			if (e.ListSourceRow < 0 || e.ListSourceRow >= _modList.Count)
			{
				e.Visible = false;
				e.Handled = true;
				return;
			}

			e.Visible =
				_newModsFilterSnapshot.Contains(
					_modList[e.ListSourceRow]);
			e.Handled = true;
		}

		private void GridView_NewModsRowCellStyle(
			object sender,
			RowCellStyleEventArgs e)
		{
			if (e.RowHandle < 0 ||
				gridView.IsGroupRow(e.RowHandle))
			{
				return;
			}

			int sourceIndex = gridView.GetDataSourceRowIndex(e.RowHandle);
			if (sourceIndex < 0 || sourceIndex >= _modList.Count)
				return;

			IMod mod = _modList[sourceIndex];
			if (!_sessionNewMods.Contains(mod))
				return;

			bool selected =
				gridView.IsRowSelected(e.RowHandle) ||
				e.RowHandle == gridView.FocusedRowHandle;

			if (!selected)
			{
				e.Appearance.BackColor = _colorPalette.ModNewRowBackColor;
				e.Appearance.ForeColor = _colorPalette.ModNewRowForeColor;
			}

			if (_gridBoldFont != null)
				e.Appearance.Font = _gridBoldFont;
		}

		private void GridView_NewModsCustomDrawGroupRow(
			object sender,
			RowObjectCustomDrawEventArgs e)
		{
			if (!_categoryViewActive ||
				e.RowHandle >= 0 ||
				!gridView.IsGroupRow(e.RowHandle))
			{
				return;
			}

			string categoryName = Convert.ToString(
				gridView.GetGroupRowValue(e.RowHandle));

			if (!CategoryContainsNewMods(categoryName))
				return;

			e.Appearance.BackColor = _colorPalette.ModNewGroupBackColor;
			e.Appearance.ForeColor = _colorPalette.ModNewGroupForeColor;

			if (_gridBoldFont != null)
				e.Appearance.Font = _gridBoldFont;

			e.DefaultDraw();
			e.Handled = true;
		}

		private bool CategoryContainsNewMods(string categoryName)
		{
			foreach (IMod mod in _sessionNewMods)
			{
				if (String.Equals(
						GetCachedCategoryName(mod),
						categoryName,
						StringComparison.CurrentCultureIgnoreCase))
				{
					return true;
				}
			}

			return false;
		}

		private void ShowOnlyCategoriesWithNewMods_Click(
			object sender,
			EventArgs e)
		{
			if (!_categoryViewActive)
				return;

			SetShowOnlyCategoriesWithNewMods(
				!_showOnlyCategoriesWithNewMods);
		}

		private void SetShowOnlyCategoriesWithNewMods(bool enabled)
		{
			enabled = enabled && _categoryViewActive;
			_showOnlyCategoriesWithNewMods = enabled;
			_newModsFilterSnapshot.Clear();

			if (enabled)
			{
				foreach (IMod mod in _sessionNewMods)
					_newModsFilterSnapshot.Add(mod);
			}

			if (_showOnlyCategoriesWithNewModsMenuItem != null)
			{
				_showOnlyCategoriesWithNewModsMenuItem.Down =
					enabled;
			}

			if (gridView == null ||
				gridControl == null ||
				gridControl.IsDisposed)
			{
				return;
			}

			gridView.RefreshData();

			if (enabled)
				gridView.ExpandAllGroups();

			gridView.InvalidateRows();
			gridView.Invalidate();
		}

		private void CategoriesMenu_DropDownOpening(
			object sender,
			EventArgs e)
		{
			if (!_categoryViewActive &&
				_showOnlyCategoriesWithNewMods)
			{
				SetShowOnlyCategoriesWithNewMods(false);
			}

			UpdateCategoryMenuVisibility();
		}

		private void CategoryViewSwitchCompleted(
			object sender,
			EventArgs e)
		{
			if (!_categoryViewActive)
				SetShowOnlyCategoriesWithNewMods(false);

			UpdateCategoryMenuVisibility();
		}

		private void ApplyCategoryMenuLabels()
		{
			addNewCategory.Caption = "Add new category";
			collapseAllCategories.Caption = "Collapse all categories";
			expandAllCategories.Caption = "Expand all categories";
			updateNexusAndCustomCategories.Caption = "Update Nexus and custom categories";
			resetDefaultCategories.Caption =
				"Update and reset to Nexus site defaults";
			resetUnassignedToDefaultCategories.Caption =
				"Reset unassigned mods to Nexus site defaults";
			resetModsCategory.Caption = "Reset all mods to unassigned";
			removeAllCategories.Caption = "Remove all categories";
			toggleHiddenCategories.Caption = "Toggle hidden categories";
			tsbResetCategories.Hint =
				"Add new category - Click the small arrow for more options";
		}

		private void UpdateCategoryMenuVisibility()
		{
			bool categoryView = _categoryViewActive;

			addNewCategory.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			collapseAllCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			expandAllCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			removeAllCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			toggleHiddenCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;

			if (_showOnlyCategoriesWithNewModsMenuItem != null)
			{
				_showOnlyCategoriesWithNewModsMenuItem.Visibility =
					categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			}

			// These are the only commands retained in the flat/default view.
			updateNexusAndCustomCategories.Visibility = BarItemVisibility.Always;
			resetDefaultCategories.Visibility = BarItemVisibility.Always;
			resetUnassignedToDefaultCategories.Visibility = BarItemVisibility.Always;
			resetModsCategory.Visibility = BarItemVisibility.Always;
		}
	}
}
