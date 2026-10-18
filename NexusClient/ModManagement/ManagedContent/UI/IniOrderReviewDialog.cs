using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.ManagedContent.UI
{
	/// <summary>Full proposed settings plus all semantic changes; confirmation is explicit and non-destructive.</summary>
	internal sealed class IniOrderReviewDialog : ManagedFontXtraForm
	{
		/// <summary>Displays every semantic delta and exact proposed file before explicit approval.</summary>
		internal IniOrderReviewDialog(IniOrderChangePreview preview)
		{
			if (preview == null || preview.Status != IniOrderChangePlanStatus.PreviewAvailable)
				throw new ArgumentException("A real change preview is required.", nameof(preview));
			Text = LanguageManager.Get("ManagedContent.Order.Review.Title", "Review game component order");
			StartPosition = FormStartPosition.CenterParent;
			MinimumSize = new Size(720, 480);
			ClientSize = new Size(950, 680);
			ShowInTaskbar = false;
			FormBorderStyle = FormBorderStyle.Sizable;
			AutoScaleMode = AutoScaleMode.Font;

			var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3,
				ColumnCount = 1, Padding = new Padding(10) };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);

			var heading = new LabelControl { Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical,
				UseMnemonic = false, Margin = new Padding(0, 0, 0, 8), Text = String.Format(preview.Kind == IniOrderChangeKind.RestoreGameDefaults
					? LanguageManager.Get("ManagedContent.Order.Review.DefaultSummary",
						"Remove configured order/enabled overrides for {0} components, including external entries? Headers, comments and other keys remain. The existing file is backed up and retained.")
					: LanguageManager.Get("ManagedContent.Order.Review.Summary",
						"{0} changes to the game order file. Review every changed value and the complete proposed file. The existing bytes will be checked again and backed up before replacement."), preview.Changes.Count) + Environment.NewLine + preview.FilePath };
			heading.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			root.Controls.Add(heading, 0, 0);

			var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			content.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
			content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			content.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
			var grid = new GridControl { Dock = DockStyle.Fill };
			var view = new GridView(grid);
			grid.MainView = view;
			grid.ViewCollection.Add(view);
			view.OptionsBehavior.Editable = false;
			view.OptionsCustomization.AllowSort = false;
			view.OptionsView.ShowGroupPanel = false;
			view.OptionsView.ShowIndicator = false;
			view.Columns.AddVisible("Folder", LanguageManager.Get("ManagedContent.Order.Columns.Folder", "Component"));
			view.Columns.AddVisible("BeforePriority", LanguageManager.Get("ManagedContent.Order.Review.BeforePriority", "Old priority"));
			view.Columns.AddVisible("AfterPriority", LanguageManager.Get("ManagedContent.Order.Review.AfterPriority", "New priority"));
			view.Columns.AddVisible("BeforeEnabled", LanguageManager.Get("ManagedContent.Order.Review.BeforeEnabled", "Old game enabled"));
			view.Columns.AddVisible("AfterEnabled", LanguageManager.Get("ManagedContent.Order.Review.AfterEnabled", "New game enabled"));
			string enabled = LanguageManager.Get("ManagedContent.Order.Values.Enabled", "Enabled");
			string disabled = LanguageManager.Get("ManagedContent.Order.Values.Disabled", "Disabled");
			grid.DataSource = new BindingList<ChangeRow>(preview.Changes
				.Select(x => new ChangeRow(x, enabled, disabled)).ToList());
			content.Controls.Add(grid, 0, 0);

			var settingsPreview = new MemoEdit { Dock = DockStyle.Fill, Text = preview.ProposedText };
			settingsPreview.Properties.ReadOnly = true;
			settingsPreview.Properties.WordWrap = false;
			var caption = new LabelControl { Dock = DockStyle.Top, Text = LanguageManager.Get(
				"ManagedContent.Order.Review.ExactFile", "Exact proposed order-file content (read-only)") };
			content.Controls.Add(caption, 0, 1);
			content.Controls.Add(settingsPreview, 0, 2);
			root.Controls.Add(content, 0, 1);

			var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
				FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
			var apply = new SimpleButton { Text = LanguageManager.Get("ManagedContent.Order.Review.Approve", "Approve and write game settings"),
				DialogResult = DialogResult.OK, AutoSize = true };
			var cancel = new SimpleButton { Text = LanguageManager.Get("Common.Action.Cancel", "Cancel"),
				DialogResult = DialogResult.Cancel, AutoSize = true };
			buttons.Controls.Add(apply);
			buttons.Controls.Add(cancel);
			root.Controls.Add(buttons, 0, 2);
			AcceptButton = apply;
			CancelButton = cancel;
		}

		/// <summary>Detached display values for one reviewed component.</summary>
		private sealed class ChangeRow
		{
			/// <summary>Formats a semantic delta without accessing disk.</summary>
			internal ChangeRow(IniOrderChange change, string enabled, string disabled)
			{
				Folder = change.FolderName;
				BeforePriority = change.PreviousPriority?.ToString() ?? "-";
				AfterPriority = change.ProposedPriority?.ToString() ?? "-";
				BeforeEnabled = Display(change.PreviousEnabled, enabled, disabled);
				AfterEnabled = Display(change.ProposedEnabled, enabled, disabled);
			}
			public string Folder { get; }
			public string BeforePriority { get; }
			public string AfterPriority { get; }
			public string BeforeEnabled { get; }
			public string AfterEnabled { get; }
			/// <summary>Preserves absent game-enabled values in the review.</summary>
			private static string Display(bool? value, string enabled, string disabled) =>
				!value.HasValue ? "-" : value.Value ? enabled : disabled;
		}
	}
}
