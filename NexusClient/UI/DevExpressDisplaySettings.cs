namespace Nexus.Client.UI
{
	using System;
	using System.Drawing;
	using System.Linq;
	using System.Windows.Forms;

	using DevExpress.Utils;
	using DevExpress.XtraBars;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Repository;
	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Views.Base;
	using DevExpress.XtraGrid.Views.Grid;

	internal sealed class DevExpressDisplaySettings : IDisposable
	{
		internal const string DefaultFontFamily = "Segoe UI";
		internal const float DefaultFontSizePt = 9f;
		internal const string DefaultDensity = "Compact";

		internal static readonly string[] FontChoices =
		{
			"Segoe UI", "Corbel", "Calibri", "Tahoma", "Verdana"
		};

		internal static readonly string[] FontSizeChoices =
		{
			"8 pt", "9 pt", "10 pt", "11 pt", "12 pt"
		};

		internal static readonly string[] DensityChoices =
		{
			"Compact", "Comfortable", "Spacious"
		};

		internal DevExpressDisplaySettings(
			string fontFamilyName,
			float fontSizePt,
			string density)
		{
			FontFamilyName = ResolveFontFamily(fontFamilyName);
			FontSizePt = ResolveFontSize(fontSizePt);
			Density = ResolveDensity(density);
			Font = new Font(
				FontFamilyName,
				FontSizePt,
				FontStyle.Regular,
				GraphicsUnit.Point);
		}

		internal string FontFamilyName { get; }
		internal float FontSizePt { get; }
		internal string Density { get; }
		internal Font Font { get; }

		public void Dispose()
		{
			Font.Dispose();
		}

		internal static string ResolveFontFamily(string fontName)
		{
			if (string.IsNullOrWhiteSpace(fontName))
				return DefaultFontFamily;

			if (fontName.Equals("Aptos", StringComparison.OrdinalIgnoreCase))
				fontName = "Corbel";

			FontFamily family = FontFamily.Families.FirstOrDefault(
				candidate => candidate.Name.Equals(
					fontName,
					StringComparison.OrdinalIgnoreCase));

			return family == null ? DefaultFontFamily : family.Name;
		}

		internal static float ResolveFontSize(float fontSize)
		{
			if (fontSize < 8f) return 8f;
			if (fontSize > 12f) return 12f;
			return (float)Math.Round(fontSize);
		}

		internal static string ResolveDensity(string density)
		{
			foreach (string choice in DensityChoices)
			{
				if (choice.Equals(density, StringComparison.OrdinalIgnoreCase))
					return choice;
			}

			return DefaultDensity;
		}

		internal static float ParseFontSize(string fontSizeText)
		{
			if (string.IsNullOrWhiteSpace(fontSizeText))
				return DefaultFontSizePt;

			string digits = new string(fontSizeText.Where(char.IsDigit).ToArray());
			int fontSize;
			return int.TryParse(digits, out fontSize)
				? ResolveFontSize(fontSize)
				: DefaultFontSizePt;
		}

		internal static string FormatFontSize(float fontSize)
		{
			return ((int)Math.Round(ResolveFontSize(fontSize))).ToString() + " pt";
		}

		internal static int GetRowHeight(string density, float fontSize)
		{
			int baseHeight = (int)Math.Round(fontSize * 2.55f);
			if (string.Equals(
					density,
					"Comfortable",
					StringComparison.OrdinalIgnoreCase))
			{
				return baseHeight + 4;
			}
			if (string.Equals(
					density,
					"Spacious",
					StringComparison.OrdinalIgnoreCase))
			{
				return baseHeight + 8;
			}
			return baseHeight;
		}

		internal static int GetHeaderHeight(string density, float fontSize)
		{
			return GetRowHeight(density, fontSize) + 2;
		}
	}

	internal static class DevExpressDisplaySettingsApplier
	{
		internal static void ApplyToControlTree(
			Control root,
			DevExpressDisplaySettings settings)
		{
			if (root == null || settings == null || root.IsDisposed)
				return;

			// Setting a DevExpress font can synchronously recreate child controls.
			// Snapshot the collection before applying anything to this node so the
			// recursive walk never enumerates a collection that is being mutated.
			Control[] children = root.Controls.Cast<Control>().ToArray();

			ApplyToControl(root, settings);

			foreach (Control child in children)
				ApplyToControlTree(child, settings);
		}

		internal static void ApplyToBarManager(
			BarManager barManager,
			DevExpressDisplaySettings settings)
		{
			if (barManager == null || settings == null)
				return;

			// Appearance changes can force DevExpress to rebuild bar links/items.
			// Work from snapshots for the same reason as the control-tree walk.
			Bar[] bars = barManager.Bars.Cast<Bar>().ToArray();
			BarItem[] items = barManager.Items.Cast<BarItem>().ToArray();

			foreach (Bar bar in bars)
			{
				ApplyAppearance(bar.BarAppearance.Normal, settings.Font);
				ApplyAppearance(bar.BarAppearance.Hovered, settings.Font);
				ApplyAppearance(bar.BarAppearance.Pressed, settings.Font);
				ApplyAppearance(bar.BarAppearance.Disabled, settings.Font);
			}

			foreach (BarItem item in items)
			{
				ApplyAppearance(item.ItemAppearance.Normal, settings.Font);
				ApplyAppearance(item.ItemAppearance.Hovered, settings.Font);
				ApplyAppearance(item.ItemAppearance.Pressed, settings.Font);
				ApplyAppearance(item.ItemAppearance.Disabled, settings.Font);

				ApplyAppearance(item.ItemInMenuAppearance.Normal, settings.Font);
				ApplyAppearance(item.ItemInMenuAppearance.Hovered, settings.Font);
				ApplyAppearance(item.ItemInMenuAppearance.Pressed, settings.Font);
				ApplyAppearance(item.ItemInMenuAppearance.Disabled, settings.Font);
			}
		}

		internal static void ApplyToRepositoryItem(
			RepositoryItem repositoryItem,
			DevExpressDisplaySettings settings)
		{
			if (repositoryItem == null || settings == null)
				return;

			ApplyAppearance(repositoryItem.Appearance, settings.Font);
			ApplyAppearance(repositoryItem.AppearanceDisabled, settings.Font);
			ApplyAppearance(repositoryItem.AppearanceFocused, settings.Font);
			ApplyAppearance(repositoryItem.AppearanceReadOnly, settings.Font);

			RepositoryItemLookUpEdit lookUpEdit =
				repositoryItem as RepositoryItemLookUpEdit;
			if (lookUpEdit == null)
				return;

			ApplyAppearance(lookUpEdit.AppearanceDropDown, settings.Font);
			ApplyAppearance(lookUpEdit.AppearanceDropDownHeader, settings.Font);
		}

		private static void ApplyToControl(
			Control control,
			DevExpressDisplaySettings settings)
		{
			GridControl gridControl = control as GridControl;
			RepositoryItem[] repositoryItems = null;
			BaseView[] views = null;

			if (gridControl != null)
			{
				// Changing GridControl.Font or a repository appearance may cause
				// DevExpress to add/remove internal editors or views immediately.
				// Capture both collections before the first appearance mutation.
				repositoryItems = gridControl.RepositoryItems
					.Cast<RepositoryItem>()
					.ToArray();
				views = gridControl.ViewCollection
					.Cast<BaseView>()
					.ToArray();
			}

			string controlNamespace = control.GetType().Namespace;
			if (!string.IsNullOrEmpty(controlNamespace) &&
				controlNamespace.StartsWith(
					"DevExpress.",
					StringComparison.Ordinal))
			{
				control.Font = settings.Font;
			}

			BaseStyleControl baseStyleControl = control as BaseStyleControl;
			if (baseStyleControl != null)
				ApplyAppearance(baseStyleControl.Appearance, settings.Font);

			if (gridControl == null)
				return;

			gridControl.Font = settings.Font;

			foreach (RepositoryItem repositoryItem in repositoryItems)
				ApplyToRepositoryItem(repositoryItem, settings);

			foreach (BaseView view in views)
			{
				GridView gridView = view as GridView;
				if (gridView != null)
					ApplyToGridView(gridView, settings);
			}
		}

		private static void ApplyToGridView(
			GridView gridView,
			DevExpressDisplaySettings settings)
		{
			gridView.RowHeight = DevExpressDisplaySettings.GetRowHeight(
				settings.Density,
				settings.FontSizePt);
			gridView.ColumnPanelRowHeight =
				DevExpressDisplaySettings.GetHeaderHeight(
					settings.Density,
					settings.FontSizePt);

			ApplyAppearance(gridView.Appearance.Row, settings.Font);
			ApplyAppearance(gridView.Appearance.EvenRow, settings.Font);
			ApplyAppearance(gridView.Appearance.OddRow, settings.Font);
			ApplyAppearance(gridView.Appearance.FocusedRow, settings.Font);
			ApplyAppearance(gridView.Appearance.SelectedRow, settings.Font);
			ApplyAppearance(gridView.Appearance.HideSelectionRow, settings.Font);
			ApplyAppearance(gridView.Appearance.HeaderPanel, settings.Font);
			ApplyAppearance(gridView.Appearance.FilterPanel, settings.Font);
			ApplyAppearance(gridView.Appearance.GroupPanel, settings.Font);
			ApplyAppearance(gridView.Appearance.GroupRow, settings.Font);

			gridView.LayoutChanged();
			gridView.InvalidateRows();
		}

		private static void ApplyAppearance(
			AppearanceObject appearance,
			Font font)
		{
			if (appearance == null || font == null)
				return;

			appearance.Font = font;
			appearance.Options.UseFont = true;
		}
	}
}
