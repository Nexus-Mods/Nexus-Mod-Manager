namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Windows.Forms;

	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Views.Grid;

	using Nexus.Client.Mods;

	/// <summary>
	/// Flat Mods-list surface backed by the DevExpress XtraGrid frontend.
	/// </summary>
	internal sealed class GridModListSurface : IModListSurface
	{
		private readonly ModGridDXControl _viewControl;
		private readonly GridControl _gridControl;
		private readonly GridView _gridView;
		private readonly IList<IMod> _mods;
		private readonly string _modNameFieldName;

		/// <summary>
		/// Initializes the flat Mods surface around the extracted DevExpress grid control.
		/// </summary>
		public GridModListSurface(ModGridDXControl viewControl, IList<IMod> mods, string modNameFieldName)
		{
			if (viewControl == null) throw new ArgumentNullException(nameof(viewControl));
			if (mods == null) throw new ArgumentNullException(nameof(mods));
			if (String.IsNullOrWhiteSpace(modNameFieldName)) throw new ArgumentException("A mod-name field is required.", nameof(modNameFieldName));

			_viewControl = viewControl;
			_gridControl = viewControl.GridControl;
			_gridView = viewControl.GridView;
			_mods = mods;
			_modNameFieldName = modNameFieldName;

			_gridView.FocusedRowChanged += GridView_SelectionChanged;
			_gridView.SelectionChanged += GridView_SelectionChanged;
		}

		/// <summary>
		/// Gets the WinForms control hosted by the Mod Manager.
		/// </summary>
		public Control ViewControl => _viewControl;

		/// <summary>
		/// Gets the mod represented by the currently focused data row.
		/// </summary>
		public IMod FocusedMod
		{
			get
			{
				int rowHandle = _gridView.FocusedRowHandle;
				if (rowHandle < 0) return null;

				int sourceIndex = _gridView.GetDataSourceRowIndex(rowHandle);
				return sourceIndex >= 0 && sourceIndex < _mods.Count ? _mods[sourceIndex] : null;
			}
		}

		/// <summary>
		/// Gets the mods represented by the currently selected data rows.
		/// </summary>
		public IList<IMod> SelectedMods
		{
			get
			{
				var selectedMods = new List<IMod>();
				int[] rows = _gridView.GetSelectedRows();
				if (rows == null) return selectedMods;

				foreach (int rowHandle in rows)
				{
					if (rowHandle < 0) continue;

					int sourceIndex = _gridView.GetDataSourceRowIndex(rowHandle);
					if (sourceIndex >= 0 && sourceIndex < _mods.Count)
						selectedMods.Add(_mods[sourceIndex]);
				}

				return selectedMods;
			}
		}

		/// <summary>
		/// Occurs when the focused row or row selection changes.
		/// </summary>
		public event EventHandler SelectionChanged;

		/// <summary>
		/// Rebinds the grid after the backing Mods collection has been replaced or reset.
		/// </summary>
		public void SetMods(IEnumerable<IMod> mods)
		{
			RefreshDataSource();
		}

		/// <summary>
		/// Refreshes the grid after mods have been added to the shared backing list.
		/// </summary>
		public void AddMods(IEnumerable<IMod> mods)
		{
			RefreshDataSource();
		}

		/// <summary>
		/// Refreshes the grid after mods have been removed from the shared backing list.
		/// </summary>
		public void RemoveMods(IEnumerable<IMod> mods)
		{
			RefreshDataSource();
		}

		/// <summary>
		/// Invalidates the row for a mod whose presentation state has changed.
		/// </summary>
		public void RefreshMod(IMod mod, string propertyName)
		{
			InvalidateMod(mod);
		}

		/// <summary>
		/// Applies the Mod Manager text search to the mod-name column.
		/// </summary>
		public void ApplyTextFilter(string filter)
		{
			_gridView.ActiveFilterString = String.IsNullOrWhiteSpace(filter)
				? String.Empty
				: $"[{_modNameFieldName}] Like '%{filter.Replace("'", "''")}%'";
		}

		/// <summary>
		/// Focuses, selects and scrolls the specified mod into view when present.
		/// </summary>
		public void FocusMod(IMod mod)
		{
			if (mod == null) return;

			int sourceIndex = _mods.IndexOf(mod);
			if (sourceIndex < 0) return;

			int rowHandle = _gridView.GetRowHandle(sourceIndex);
			if (rowHandle < 0) return;

			_gridView.ClearSelection();
			_gridView.FocusedRowHandle = rowHandle;
			_gridView.SelectRow(rowHandle);
			_gridView.MakeRowVisible(rowHandle, false);
		}

		/// <summary>
		/// Refreshes the grid data source after collection membership changes.
		/// </summary>
		public void RefreshDataSource()
		{
			_gridControl.RefreshDataSource();
		}

		/// <summary>
		/// Refreshes displayed values without rebinding the data source.
		/// </summary>
		public void RefreshData()
		{
			_gridView.RefreshData();
		}

		/// <summary>
		/// Invalidates all rendered mod rows.
		/// </summary>
		public void InvalidateRows()
		{
			_gridView.InvalidateRows();
		}

		/// <summary>
		/// Invalidates both the grid view and its containing grid control.
		/// </summary>
		public void InvalidateView()
		{
			_gridView.Invalidate();
			_gridControl.Invalidate();
		}

		/// <summary>
		/// Invalidates the data row associated with the specified mod.
		/// </summary>
		public void InvalidateMod(IMod mod)
		{
			if (mod == null) return;

			int sourceIndex = _mods.IndexOf(mod);
			if (sourceIndex < 0) return;

			int rowHandle = _gridView.GetRowHandle(sourceIndex);
			if (rowHandle != GridControl.InvalidRowHandle)
				_gridView.InvalidateRow(rowHandle);
		}

		/// <summary>
		/// Forwards DevExpress focus and selection changes through the surface contract.
		/// </summary>
		private void GridView_SelectionChanged(object sender, EventArgs e)
		{
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}
	}
}
