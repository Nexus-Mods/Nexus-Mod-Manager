using System;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;

namespace Nexus.Client.ModManagement.LoadOrder.UI
{
	/// <summary>Small DevExpress-only layout helpers shared by the load-order page and its review/recovery dialogs.</summary>
	internal static class LoadOrderUiLayout
	{
		/// <summary>Creates a skin-aware table with content-sized command rows and flexible content rows/columns.</summary>
		internal static LayoutControl Table(double[] columns, double[] rows)
		{
			var layout = new LayoutControl { Dock = DockStyle.Fill, AllowCustomization = false };
			layout.Root.EnableIndentsWithoutBorders = DefaultBoolean.True;
			layout.Root.GroupBordersVisible = false;
			Configure(layout.Root, columns, rows);
			return layout;
		}

		/// <summary>Creates a content-sized command group within the same layout tree.</summary>
		internal static LayoutControlGroup Group(LayoutControl layout, int row, double[] columns)
		{
			LayoutControlGroup group = layout.Root.AddGroup(); group.GroupBordersVisible = false;
			group.OptionsTableLayoutItem.RowIndex = row; Configure(group, columns, new[] { 0D }); return group;
		}

		/// <summary>Configures the supported DevExpress table layout for either a root or nested group.</summary>
		private static void Configure(LayoutControlGroup group, double[] columns, double[] rows)
		{
			group.LayoutMode = LayoutMode.Table;
			group.OptionsTableLayoutGroup.ColumnDefinitions.Clear(); group.OptionsTableLayoutGroup.RowDefinitions.Clear();
			foreach (double width in columns) group.OptionsTableLayoutGroup.ColumnDefinitions.Add(new ColumnDefinition { SizeType = width == 0 ? SizeType.AutoSize : SizeType.Percent, Width = width });
			foreach (double height in rows) group.OptionsTableLayoutGroup.RowDefinitions.Add(new RowDefinition { SizeType = height == 0 ? SizeType.AutoSize : SizeType.Percent, Height = height });
		}

		/// <summary>Positions a control without fixed pixel heights or a second layout framework.</summary>
		internal static LayoutControlItem Put(LayoutControl layout, Control control, int row, int column = 0, int columnSpan = 1, string caption = null, LayoutControlGroup parent = null)
		{
			layout.Controls.Add(control);
			LayoutControlItem item = (parent ?? layout.Root).AddItem(caption ?? String.Empty, control);
			item.TextVisible = caption != null;
			item.OptionsTableLayoutItem.RowIndex = row; item.OptionsTableLayoutItem.ColumnIndex = column; item.OptionsTableLayoutItem.ColumnSpan = columnSpan;
			return item;
		}

		/// <summary>Creates plain, wrapping text using the active skin and font.</summary>
		internal static LabelControl Label(string text)
		{
			var label = new LabelControl { Text = text, AutoSizeMode = LabelAutoSizeMode.Vertical, UseMnemonic = false, AllowHtmlString = false };
			label.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			return label;
		}

		/// <summary>Creates a copyable, read-only text surface without HTML interpretation of package content.</summary>
		internal static MemoEdit Memo()
		{
			var memo = new MemoEdit(); memo.Properties.ReadOnly = true; memo.Properties.WordWrap = true; return memo;
		}
	}
}
