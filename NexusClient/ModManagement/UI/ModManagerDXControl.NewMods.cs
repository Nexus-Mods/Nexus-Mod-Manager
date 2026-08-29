namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.Windows.Forms;

	using DevExpress.XtraBars;
	using DevExpress.XtraGrid.Views.Grid;

	using Nexus.Client.Mods;
	using Nexus.Client.UI;
	using Nexus.Client.Util.Localization;

	public partial class ModManagerDXControl
	{
		private readonly ModSessionNewTracker _newModTracker =
			new ModSessionNewTracker();

		private BarButtonItem _showOnlyCategoriesWithNewModsMenuItem;
		private bool _newModCategoryViewInitialized;
		private bool _showOnlyCategoriesWithNewMods;

		private void InitializeNewModCategoryView()
		{
			if (_newModCategoryViewInitialized)
				return;

			_newModCategoryViewInitialized = true;

			ApplyCategoryMenuLabels();

			_showOnlyCategoriesWithNewModsMenuItem = new BarButtonItem(barManagerMods, LanguageManager.Get("Mods.Categories.ShowOnlyWithNewMods.Name", "Show only categories with new mods"))
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

			toggleHiddenCategories.ButtonStyle = BarButtonStyle.Check;

			gridView.RowCellStyle += GridView_NewModsRowCellStyle;
			gridControl.MouseDown += GridControl_NewModsMouseDown;
			gridView.KeyUp += GridView_NewModsKeyUp;

			_newModTracker.TrackedModChanged += NewModTracker_TrackedModChanged;

			Disposed += (sender, args) =>
			{
				DetachNewModCategoryTracking();
				_newModTracker.TrackedModChanged -= NewModTracker_TrackedModChanged;
				_newModTracker.Dispose();
			};

			UpdateCategoryMenuVisibility();
		}

		private void AttachNewModCategoryTracking()
		{
			_newModTracker.ResetBaseline(_viewModel?.ManagedMods);
			SetShowOnlyCategoriesWithNewMods(false);
			UpdateCategoryMenuVisibility();
		}

		private void DetachNewModCategoryTracking()
		{
			_newModTracker.ResetBaseline(null);
			_showOnlyCategoriesWithNewMods = false;

			if (_showOnlyCategoriesWithNewModsMenuItem != null)
				_showOnlyCategoriesWithNewModsMenuItem.Down = false;
		}

		/// <summary>
		/// Applies a ManagedMods collection delta to the session new-mod tracker and refreshes affected surfaces.
		/// </summary>
		private void UpdateNewModTracking(NotifyCollectionChangedEventArgs e)
		{
			if (e == null)
				return;

			bool refreshFilter = false;

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					if (e.NewItems != null)
					{
						foreach (IMod mod in e.NewItems)
						{
							if (_newModTracker.TrackAddedMod(mod, _showOnlyCategoriesWithNewMods) &&
								_showOnlyCategoriesWithNewMods)
							{
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
							_newModTracker.RemoveMod(mod, true);
							refreshFilter = true;
						}
					}
					break;

				case NotifyCollectionChangedAction.Replace:
					if (e.OldItems != null)
					{
						foreach (IMod mod in e.OldItems)
							_newModTracker.RemoveMod(mod, false);
					}

					if (e.NewItems != null)
					{
						foreach (IMod mod in e.NewItems)
							_newModTracker.RegisterKnownMod(mod);
					}

					refreshFilter = true;
					break;

				case NotifyCollectionChangedAction.Reset:
					_newModTracker.ResetBaseline(_viewModel?.ManagedMods);
					SetShowOnlyCategoriesWithNewMods(false);
					refreshFilter = true;
					break;
			}

			if (refreshFilter && _showOnlyCategoriesWithNewMods)
				ApplyNewModsCategoryFilterToTree();

			gridView.InvalidateRows();
			gridView.Invalidate();
			_categoryModListSurface?.InvalidateRows();
		}

		/// <summary>
		/// Refreshes new-mod presentation when metadata changes on a tracked mod, marshaling to the UI thread when necessary.
		/// </summary>
		private void NewModTracker_TrackedModChanged(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				BeginInvoke((Action<object, EventArgs>)NewModTracker_TrackedModChanged, sender, e);
				return;
			}

			gridView.RefreshData();
			gridView.InvalidateRows();
			gridView.Invalidate();
			_categoryModListSurface?.InvalidateRows();
		}

		private void GridControl_NewModsMouseDown(
			object sender,
			MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Left)
				return;

			var hitInfo = gridView.CalcHitInfo(e.Location);
			if (!hitInfo.InRow ||
				hitInfo.RowHandle < 0)
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
			List<IMod> selectedMods = SelectedMods;

			if (selectedMods.Count == 0)
			{
				IMod fallbackMod = GetModAtRow(fallbackRowHandle);
				if (fallbackMod != null)
					selectedMods.Add(fallbackMod);
			}

			if (!_newModTracker.Acknowledge(selectedMods))
				return;

			// Deliberately do not refresh the custom filter here. In filtered
			// mode the current snapshot must remain stable until the toggle is
			// disabled and enabled again.
			gridView.InvalidateRows();
			gridView.Invalidate();
			_categoryModListSurface?.InvalidateRows();
		}

		private IMod GetModAtRow(int rowHandle)
		{
			if (rowHandle < 0)
				return null;

			int sourceIndex = gridView.GetDataSourceRowIndex(rowHandle);
			if (sourceIndex < 0 || sourceIndex >= _modList.Count)
				return null;

			return _modList[sourceIndex];
		}

		private void GridView_NewModsRowCellStyle(
			object sender,
			RowCellStyleEventArgs e)
		{
			if (e.RowHandle < 0)
			{
				return;
			}

			int sourceIndex = gridView.GetDataSourceRowIndex(e.RowHandle);
			if (sourceIndex < 0 || sourceIndex >= _modList.Count)
				return;

			IMod mod = _modList[sourceIndex];
			if (!_newModTracker.IsNew(mod))
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

		private void ShowOnlyCategoriesWithNewMods_Click(
			object sender,
			EventArgs e)
		{
			if (!IsCategoryViewActive)
				return;

			SetShowOnlyCategoriesWithNewMods(
				!_showOnlyCategoriesWithNewMods);
		}

		private void SetShowOnlyCategoriesWithNewMods(bool enabled)
		{
			enabled = enabled && IsCategoryViewActive;
			_showOnlyCategoriesWithNewMods = enabled;
			_newModTracker.CaptureFilterSnapshot(enabled);

			if (_showOnlyCategoriesWithNewModsMenuItem != null)
			{
				_showOnlyCategoriesWithNewModsMenuItem.Down =
					enabled;
			}

			ApplyNewModsCategoryFilterToTree();


			if (enabled && _categoryModListSurface != null)
				_categoryModListSurface.ExpandAllCategories();
		}

		/// <summary>
		/// Composes the New Mods and Updates Only predicates used by the Category Tree surface.
		/// </summary>
		private void ApplyNewModsCategoryFilterToTree()
		{
			if (_categoryModListSurface == null)
				return;

			if (!_showOnlyCategoriesWithNewMods && !_showUpdatesOnly)
			{
				_categoryModListSurface.SetVisibilityPredicate(null);
				return;
			}

			_categoryModListSurface.SetVisibilityPredicate(mod =>
				(!_showOnlyCategoriesWithNewMods || _newModTracker.IsInFilterSnapshot(mod)) &&
				(!_showUpdatesOnly || IsModOutdated(mod)));
		}

		private void CategoriesMenu_DropDownOpening(
			object sender,
			EventArgs e)
		{
			UpdateCategoryMenuVisibility();
		}

		private void ApplyCategoryMenuLabels()
		{
			addNewCategory.Caption = LanguageManager.Get("Mods.Categories.Add.ShortName", "Add new category");
			collapseAllCategories.Caption = LanguageManager.Get("Mods.Categories.CollapseAll.Name", "Collapse all categories");
			expandAllCategories.Caption = LanguageManager.Get("Mods.Categories.ExpandAll.Name", "Expand all categories");
			updateNexusAndCustomCategories.Caption = LanguageManager.Get("Mods.Categories.UpdateNexusCustom.Name", "Update Nexus and custom categories");
			resetDefaultCategories.Caption =
				LanguageManager.Get("Mods.Categories.ResetNexusDefaults.Name", "Update and reset to Nexus site defaults");
			resetUnassignedToDefaultCategories.Caption =
				LanguageManager.Get("Mods.Categories.ResetUnassigned.Name", "Reset unassigned mods to Nexus site defaults");
			resetModsCategory.Caption = LanguageManager.Get("Mods.Categories.ResetAllUnassigned.Name", "Reset all mods to unassigned");
			removeAllCategories.Caption = LanguageManager.Get("Mods.Categories.RemoveAll.Name", "Remove all categories");
			toggleHiddenCategories.Caption = LanguageManager.Get("Mods.Categories.ToggleHidden.Name", "Show empty categories");
			tsbResetCategories.Hint =
				LanguageManager.Get("Mods.Categories.Menu.ShortTooltip", "Add new category - Click the small arrow for more options");
		}

		private void UpdateCategoryMenuVisibility()
		{
			bool categoryView = IsCategoryViewActive;

			addNewCategory.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			collapseAllCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			expandAllCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			removeAllCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			toggleHiddenCategories.Visibility = categoryView ? BarItemVisibility.Always : BarItemVisibility.Never;
			toggleHiddenCategories.Down = _viewModel?.Settings?.ShowEmptyCategory == true;

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
