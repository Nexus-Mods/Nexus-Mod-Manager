using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Wabbajack.UI
{
	/// <summary>Visible operation feedback using the same semantic refresh icon and rotation as Collections.</summary>
	internal sealed class WabbajackActivityControl : PanelControl
	{
		private readonly TableLayoutPanel _layout;
		private readonly LabelControl _heading;
		private readonly LabelControl _detail;
		private readonly Timer _animation;
		private Bitmap _image;
		private bool _working;
		private bool _warning;
		private int _frame;

		/// <summary>Creates an accessible, skinned activity surface.</summary>
		internal WabbajackActivityControl()
		{
			Dock = DockStyle.Fill; AutoSize = true; Padding = new Padding(10); Margin = new Padding(0, 8, 0, 0);
			_layout = WabbajackUi.Stack(2);
			_heading = WabbajackUi.Caption(string.Empty, true);
			_heading.ImageAlignToText = ImageAlignToText.LeftCenter; _heading.IndentBetweenImageAndText = 8;
			_detail = WabbajackUi.Caption(string.Empty);
			_layout.Controls.Add(_heading, 0, 0); _layout.Controls.Add(_detail, 0, 1); Controls.Add(_layout);
			_animation = new Timer { Interval = 125 };
			_animation.Tick += (sender, args) => { _frame = (_frame + 1) % 8; RenderIcon(); };
		}

		/// <summary>Shows the operation, current work detail, and whether work is actively progressing.</summary>
		internal void ShowActivity(string heading, string detail, bool working, bool warning = false)
		{
			_heading.Text = heading; Detail = detail; _working = working; _warning = warning;
			if (working) _animation.Start(); else { _animation.Stop(); _frame = 0; }
			RenderIcon();
		}

		/// <summary>Updates progress without replacing the operation heading.</summary>
		internal string Detail { get => _detail.Text; set { _detail.Text = value; AccessibleDescription = value; } }

		/// <summary>Measures the wrapped feedback text at the available width.</summary>
		public override Size GetPreferredSize(Size proposedSize)
		{
			if (_layout == null) return base.GetPreferredSize(proposedSize);
			Size size = _layout.GetPreferredSize(new Size(Math.Max(1, proposedSize.Width - Padding.Horizontal - 4), 0));
			return new Size(size.Width + Padding.Horizontal + 4, size.Height + Padding.Vertical + 4);
		}

		/// <summary>Refreshes icon size and palette along with the rest of the display settings.</summary>
		internal void RefreshIcon() { RenderIcon(); }

		/// <summary>Rotates NMM's refresh artwork while working; idle and failure states use semantic icons.</summary>
		private void RenderIcon()
		{
			int size = NmmIconProvider.CurrentIconSize;
			Image source = NmmIconProvider.GetBitmap(_working ? NmmIconAction.Refresh : _warning ? NmmIconAction.Warning : NmmIconAction.GetModInfo, size, false);
			Bitmap next = new Bitmap(size, size);
			using (Graphics graphics = Graphics.FromImage(next))
			{
				graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
				graphics.TranslateTransform(size / 2F, size / 2F);
				if (_working) graphics.RotateTransform(-_frame * 45F);
				if (source != null) graphics.DrawImage(source, new RectangleF(-size / 2F, -size / 2F, size, size));
			}
			Bitmap old = _image; _image = next; _heading.ImageOptions.Image = next; old?.Dispose();
		}

		/// <summary>Releases the animation timer and owned icon frames.</summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing) { _animation.Dispose(); _heading.ImageOptions.Image = null; _image?.Dispose(); }
			base.Dispose(disposing);
		}
	}

	/// <summary>Shared, skin-aware presentation for a selected gallery list and its downloaded package.</summary>
	internal sealed class WabbajackSummaryControl : XtraUserControl
	{
		private readonly LabelControl _title;
		private readonly LabelControl _author;
		private readonly LabelControl _availability;
		private readonly LabelControl _description;
		private readonly LabelControl _tags;
		private readonly LabelControl _downloads;
		private readonly LabelControl _installed;
		private readonly TableLayoutPanel _sizes;
		private readonly TableLayoutPanel _layout;

		/// <summary>Creates a readable overview without an editable text-box surface.</summary>
		internal WabbajackSummaryControl()
		{
			Dock = DockStyle.Fill;
			AutoSize = true;
			TableLayoutPanel layout = WabbajackUi.Stack(6);
			_layout = layout;
			layout.Padding = new Padding(12);
			_title = WabbajackUi.Caption(string.Empty, true);
			_title.Appearance.FontSizeDelta = 4;
			_author = WabbajackUi.Caption(string.Empty);
			_availability = WabbajackUi.Caption(string.Empty, true);
			_availability.ImageAlignToText = ImageAlignToText.LeftCenter;
			_availability.IndentBetweenImageAndText = 6;
			_description = WabbajackUi.Caption(string.Empty);
			_description.Margin = new Padding(0, 12, 0, 12);
			_tags = WabbajackUi.Caption(string.Empty);
			_sizes = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 12, 0, 0) };
			_sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			_sizes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
			_sizes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			_sizes.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			_sizes.Controls.Add(WabbajackUi.Caption(WabbajackUi.L("DownloadSize", "Downloads")), 0, 0);
			_sizes.Controls.Add(WabbajackUi.Caption(WabbajackUi.L("InstalledSize", "Installed files")), 1, 0);
			_downloads = WabbajackUi.Caption(string.Empty, true);
			_installed = WabbajackUi.Caption(string.Empty, true);
			_downloads.Appearance.FontSizeDelta = _installed.Appearance.FontSizeDelta = 2;
			_sizes.Controls.Add(_downloads, 0, 1); _sizes.Controls.Add(_installed, 1, 1);
			layout.Controls.Add(_title, 0, 0); layout.Controls.Add(_author, 0, 1);
			layout.Controls.Add(_availability, 0, 2); layout.Controls.Add(_sizes, 0, 3);
			layout.Controls.Add(_description, 0, 4); layout.Controls.Add(_tags, 0, 5);
			Controls.Add(layout);
		}

		/// <summary>Shows the selected list's metadata and download requirements.</summary>
		internal void ShowList(WabbajackList list)
		{
			_title.Text = list?.Title ?? WabbajackUi.L("SelectListTitle", "Choose a modlist");
			_author.Text = list == null ? string.Empty : string.Format(WabbajackUi.L("AuthorVersion", "{0} · Version {1}"), list.Author, list.Version);
			_availability.Text = list == null ? string.Empty : list.IsUnavailable ? WabbajackUi.L("Unavailable", "Unavailable / under maintenance") :
				list.Status == null ? WabbajackUi.L("UnknownStatus", "Availability report unavailable") : WabbajackUi.L("Available", "Available");
			_availability.ImageOptions.Image = list == null ? null : Nexus.Client.UI.NmmIconProvider.GetBitmap(list.IsUnavailable ? Nexus.Client.UI.NmmIconAction.Warning : Nexus.Client.UI.NmmIconAction.GetModInfo, 16, false);
			_description.Text = list?.Description ?? WabbajackUi.L("SelectListHelp", "Select a card, then choose Download & prepare. You will choose its profile before reviewing installation in Collections.");
			_tags.Text = list == null ? string.Empty : string.Join(" · ", list.Tags);
			_sizes.Visible = list?.DownloadMetadata != null;
			_downloads.Text = WabbajackUi.FormatSize(list?.DownloadMetadata?.SizeOfArchives ?? 0);
			_installed.Text = WabbajackUi.FormatSize(list?.DownloadMetadata?.SizeOfInstalledFiles ?? 0);
		}

		/// <summary>Shows the actual package being prepared, rather than an unrelated gallery selection.</summary>
		internal void ShowPackage(WabbajackPackage package)
		{
			_title.Text = package.Name;
			_author.Text = string.Format(WabbajackUi.L("AuthorVersion", "{0} · Version {1}"), package.Author, package.Version);
			_availability.Text = WabbajackUi.L("PackageReady", "Downloaded list · Choose a profile below");
			_description.Text = package.Description;
			_description.Visible = false;
			_tags.Text = string.Empty;
			_sizes.Visible = false;
		}

		/// <summary>Fits the overview to its text instead of reserving a text editor's default height.</summary>
		public override Size GetPreferredSize(Size proposedSize) { return _layout == null ? base.GetPreferredSize(proposedSize) : _layout.GetPreferredSize(proposedSize); }
	}

	/// <summary>A wrapping action/filter row measured using DevExpress control sizes at the current font and DPI.</summary>
	internal sealed class WabbajackActionRow : PanelControl
	{
		/// <summary>Creates a compact surface that inherits the active skin.</summary>
		internal WabbajackActionRow() { Dock = DockStyle.Fill; AutoSize = true; BorderStyle = BorderStyles.NoBorder; Padding = new Padding(4); Margin = Padding.Empty; }

		/// <summary>Measures the same wrapped rows that will be displayed.</summary>
		public override Size GetPreferredSize(Size proposedSize) { return Arrange(proposedSize.Width > 0 ? proposedSize.Width : Math.Max(1, Width), false); }

		/// <summary>Reflows controls when the available width or font changes.</summary>
		protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange(Math.Max(1, ClientSize.Width), true); }

		/// <summary>Uses native best sizes so checkbox captions and buttons cannot be cut to their default widths.</summary>
		private Size Arrange(int width, bool apply)
		{
			int x = Padding.Left, y = Padding.Top, rowHeight = 0, usedWidth = 0;
			int available = Math.Max(1, width - Padding.Horizontal);
			foreach (Control child in Controls)
			{
				if (!child.Visible) continue;
				Size size = child is BaseCheckEdit check ? check.CalcBestSize() : child is SimpleButton button ? button.CalcBestSize() :
					child.AutoSize ? child.GetPreferredSize(new Size(available, 0)) : child.Size;
				size.Width = Math.Min(available, Math.Max(child.MinimumSize.Width, size.Width));
				size.Height = Math.Max(child.MinimumSize.Height, size.Height);
				if (x > Padding.Left && x + size.Width + child.Margin.Horizontal > width - Padding.Right)
				{ x = Padding.Left; y += rowHeight; rowHeight = 0; }
				if (apply) child.Bounds = new Rectangle(x + child.Margin.Left, y + child.Margin.Top, size.Width, size.Height);
				x += size.Width + child.Margin.Horizontal;
				rowHeight = Math.Max(rowHeight, size.Height + child.Margin.Vertical);
				usedWidth = Math.Max(usedWidth, x);
			}
			return new Size(usedWidth + Padding.Right, y + rowHeight + Padding.Bottom);
		}
	}

	/// <summary>Small shared layout and localization helpers for the gallery and preparation dialog.</summary>
	internal static class WabbajackUi
	{
		/// <summary>Creates a single-column layout whose content determines its height.</summary>
		internal static TableLayoutPanel Stack(int rows)
		{
			TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = rows, Margin = Padding.Empty };
			table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			for (int index = 0; index < rows; index++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			return table;
		}

		/// <summary>Creates a naturally wrapping, skin-aware label with an optional heading weight.</summary>
		internal static LabelControl Caption(string text, bool heading = false)
		{
			LabelControl label = new LabelControl { Text = text, Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical, Margin = new Padding(0, 3, 0, 3) };
			label.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			label.Appearance.Options.UseTextOptions = true;
			if (heading) label.Appearance.FontStyleDelta = FontStyle.Bold;
			return label;
		}

		/// <summary>Matches standard layout surfaces to the skin used by their DevExpress labels and editors.</summary>
		internal static void ApplySkinSurfaces(Control root)
		{
			if (root == null || root.IsDisposed) return;
			Control[] children = root.Controls.Cast<Control>().ToArray();
			if (root is TableLayoutPanel || root is WabbajackActionRow || root is XtraScrollableControl || root is ManagedFontDockContent)
				DevExpressDisplaySettingsApplier.ApplySkinSurface(root);
			foreach (Control child in children) ApplySkinSurfaces(child);
		}

		/// <summary>Formats gallery byte sizes consistently.</summary>
		internal static string FormatSize(long bytes) { return (bytes / 1073741824m).ToString("0.0") + " GiB"; }

		/// <summary>Resolves strings through the existing language infrastructure.</summary>
		internal static string L(string key, string fallback) { return LanguageManager.Get("Wabbajack." + key, fallback); }
	}
}
