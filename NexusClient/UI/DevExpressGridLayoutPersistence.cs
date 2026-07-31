namespace Nexus.Client.UI
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;

	using DevExpress.Utils;
	using DevExpress.XtraGrid.Columns;
	using DevExpress.XtraGrid.Views.Grid;

	/// <summary>
	/// Provides deterministic persistence helpers for DevExpress grid layouts while keeping filters session-only.
	/// </summary>
	internal static class DevExpressGridLayoutPersistence
	{
		private const char EntrySeparator = ';';
		private const char ValueSeparator = '|';

		/// <summary>
		/// Configures a grid so filter criteria and Find Panel text are excluded from layout serialization and deserialization.
		/// </summary>
		/// <param name="view">The grid view to configure.</param>
		internal static void ConfigureSessionOnlyFilters(GridView view)
		{
			if (view == null) throw new ArgumentNullException(nameof(view));
			view.PropertySerializing -= GridView_PropertyPersistence;
			view.PropertyDeserializing -= GridView_PropertyPersistence;
			view.PropertySerializing += GridView_PropertyPersistence;
			view.PropertyDeserializing += GridView_PropertyPersistence;
		}

		/// <summary>
		/// Rejects filter-related properties while DevExpress saves or restores a grid layout.
		/// </summary>
		/// <param name="sender">The grid view performing layout persistence.</param>
		/// <param name="e">The property persistence event arguments.</param>
		private static void GridView_PropertyPersistence(object sender, PropertyAllowEventArgs e)
		{
			if (e == null) return;

			bool columnFilter = e.Owner is GridColumn && String.Equals(e.PropertyName, "FilterInfo", StringComparison.Ordinal);
			bool viewFilter = String.Equals(e.PropertyName, "ActiveFilterString", StringComparison.Ordinal) ||
				String.Equals(e.PropertyName, "FindFilterText", StringComparison.Ordinal);
			if (columnFilter || viewFilter) e.Allow = DefaultBoolean.False;
		}

		/// <summary>
		/// Clears all column and Find Panel filters so a restored grid always starts unfiltered.
		/// </summary>
		/// <param name="view">The grid view whose transient filters should be cleared.</param>
		internal static void ClearTransientFilters(GridView view)
		{
			if (view == null) return;
			view.ClearFindFilter();
			view.ClearColumnsFilter();
		}

		/// <summary>
		/// Serializes column widths independently from the DevExpress layout stream.
		/// </summary>
		/// <param name="view">The grid view whose column widths should be serialized.</param>
		/// <returns>A compact field-name-to-width mapping.</returns>
		internal static string SerializeColumnWidths(GridView view)
		{
			if (view == null) return String.Empty;

			var entries = new List<string>();
			foreach (GridColumn column in view.Columns)
			{
				if (String.IsNullOrEmpty(column.FieldName)) continue;
				entries.Add(Uri.EscapeDataString(column.FieldName) + ValueSeparator + column.Width.ToString(CultureInfo.InvariantCulture));
			}

			return String.Join(EntrySeparator.ToString(), entries);
		}

		/// <summary>
		/// Restores explicitly persisted column widths after the main DevExpress layout has been loaded.
		/// </summary>
		/// <param name="view">The target grid view.</param>
		/// <param name="serializedWidths">The serialized field-name-to-width mapping.</param>
		internal static void RestoreColumnWidths(GridView view, string serializedWidths)
		{
			if (view == null || String.IsNullOrWhiteSpace(serializedWidths)) return;

			view.BeginUpdate();
			try
			{
				foreach (string entry in serializedWidths.Split(new[] { EntrySeparator }, StringSplitOptions.RemoveEmptyEntries))
				{
					int separatorIndex = entry.LastIndexOf(ValueSeparator);
					if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1) continue;

					string fieldName;
					try
					{
						fieldName = Uri.UnescapeDataString(entry.Substring(0, separatorIndex));
					}
					catch (UriFormatException)
					{
						continue;
					}

					int width;
					if (!Int32.TryParse(entry.Substring(separatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out width)) continue;

					GridColumn column = view.Columns[fieldName];
					if (column == null) continue;

					width = Math.Max(column.MinWidth, width);
					if (column.MaxWidth > 0) width = Math.Min(column.MaxWidth, width);
					column.Width = width;
				}
			}
			finally
			{
				view.EndUpdate();
			}
		}
	}
}
