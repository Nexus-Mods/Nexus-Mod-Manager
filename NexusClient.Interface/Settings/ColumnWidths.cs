using System;
using System.Windows.Forms;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// Stores the widths of columns.
	/// </summary>
	public class ColumnWidths : KeyedSettings<SettingsList>
	{
		/// <summary>
		/// Stores the given control's column widths using the given name.
		/// </summary>
		/// <param name="p_strName">The name under which to store the column widths.</param>
		/// <param name="p_lvwListView">The control whose column widths are to be stored.</param>
		public void SaveColumnWidths(string p_strName, ListView p_lvwListView)
		{
			Int32[] intColumnWidths = new Int32[p_lvwListView.Columns.Count];
			foreach (ColumnHeader chdHeader in p_lvwListView.Columns)
				intColumnWidths[chdHeader.Index] = chdHeader.Width;
			this[p_strName] = intColumnWidths;
		}

		/// <summary>
		/// Stores the given control's column widths using the given name.
		/// </summary>
		/// <param name="p_strName">The name under which to store the column widths.</param>
		/// <param name="p_dgvDataGridView">The control whose column widths are to be stored.</param>
		public void SaveColumnWidths(string p_strName, DataGridView p_dgvDataGridView)
		{
			Int32[] intColumnWidths = new Int32[p_dgvDataGridView.Columns.Count];
			foreach (DataGridViewColumn chdHeader in p_dgvDataGridView.Columns)
				intColumnWidths[chdHeader.Index] = chdHeader.Width;
			this[p_strName] = intColumnWidths;
		}

		/// <summary>
		/// Sets the widths of the columns in the given control based on the stored values.
		/// </summary>
		/// <param name="p_strName">The name of the settings to use to size the columns.</param>
		/// <param name="p_lvwListView">The control whose columns are to be sized.</param>
		public void LoadColumnWidths(string p_strName, ListView p_lvwListView)
		{
			Int32[] intColumnWidths = this[p_strName];
			if (intColumnWidths != null)
				for (Int32 i = 0; i < intColumnWidths.Length && i < p_lvwListView.Columns.Count; i++)
					p_lvwListView.Columns[i].Width = intColumnWidths[i];
		}

		/// <summary>
		/// Sets the widths of the columns in the given control based on the stored values.
		/// </summary>
		/// <param name="p_strName">The name of the settings to use to size the columns.</param>
		/// <param name="p_dgvDataGridView">The control whose columns are to be sized.</param>
		public void LoadColumnWidths(string p_strName, DataGridView p_dgvDataGridView)
		{
			Int32[] intColumnWidths = this[p_strName];
			if (intColumnWidths != null)
				for (Int32 i = 0; i < intColumnWidths.Length && i < p_dgvDataGridView.Columns.Count; i++)
					p_dgvDataGridView.Columns[i].Width = intColumnWidths[i];
		}
	}
}
