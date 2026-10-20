using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraTab;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.CollectionManagement.UI
{
	/// <summary>Reviews exact setup files, optional personal presets, README evidence and editable root-relative mappings.</summary>
	internal sealed class CollectionSetupPackageReviewDialog : ManagedFontXtraForm
	{
		private readonly CollectionSetupPackage _package;
		private readonly CollectionSetupPackageService _service;
		private readonly GridView _view;
		private readonly GridControl _grid;
		private readonly MemoEdit _detail;
		private readonly MemoEdit _before;
		private readonly MemoEdit _after;
		private readonly TextEdit _documents;
		private bool _refreshing;

		/// <summary>Constructs a review without granting permission to write until Apply is explicitly selected.</summary>
		internal CollectionSetupPackageReviewDialog(CollectionSetupPackage package, CollectionSetupPackageService service, bool canApply)
		{
			_package = package; _service = service;
			Text = L("ReviewTitle", "Review Collection setup package"); StartPosition = FormStartPosition.CenterParent;
			ClientSize = new Size(1100, 760); MinimumSize = new Size(820, 580); AutoScaleMode = AutoScaleMode.Font;
			ShowInTaskbar = false;
			var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			Controls.Add(root);
			root.Controls.Add(new LabelControl { Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical,
				Text = package.ArchiveName + Environment.NewLine + package.GameDirectory + Environment.NewLine + (!canApply ? L("ReviewOnly", "Review only: installation of this exact Collection revision must finish before setup can be applied.") + Environment.NewLine : String.Empty) + L("ReviewHelp", "Select files to apply after the Collection finishes. Personal presets are unchecked. Review disabled mods, key bindings and graphics settings. Identical files are skipped; changed files receive complete backups. Undo setup files before repairing, updating or removing the Collection.") }, 0, 0);
			var folders = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
			folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); folders.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); folders.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			folders.Controls.Add(new LabelControl { Text = L("DocumentsFolder", "Game's Documents folder:"), AutoSizeMode = LabelAutoSizeMode.Horizontal }, 0, 0);
			_documents = new TextEdit { Dock = DockStyle.Fill, Text = package.DocumentsDirectory ?? String.Empty };
			_documents.Properties.ReadOnly = true; folders.Controls.Add(_documents, 1, 0);
			var browse = new SimpleButton { Text = L("ChooseFolder", "Choose folder..."), AutoSize = true }; folders.Controls.Add(browse, 2, 0);
			browse.Click += ChooseDocuments; root.Controls.Add(folders, 0, 1);
			_grid = new GridControl { Dock = DockStyle.Fill }; _view = new GridView(_grid);
			_grid.MainView = _view; _grid.ViewCollection.Add(_view); _view.OptionsView.ShowGroupPanel = false;
			_view.OptionsView.ColumnAutoWidth = false; _view.OptionsBehavior.AllowAddRows = DevExpress.Utils.DefaultBoolean.False;
			_view.OptionsBehavior.AllowDeleteRows = DevExpress.Utils.DefaultBoolean.False;
			AddColumn("Selected", "Select", "Apply", 55, true); AddColumn("Source", "Source", "Archive file", 310, false);
			var mapping = AddColumn("Root", "Root", "Destination root", 110, true);
			var roots = new RepositoryItemImageComboBox();
			roots.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem(L("RootUnmapped", "Choose destination"), CollectionSetupRoot.Unmapped));
			roots.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem(L("RootGame", "Game folder"), CollectionSetupRoot.Game));
			roots.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem(L("RootDocuments", "Game Documents"), CollectionSetupRoot.Documents));
			roots.Items.Add(new DevExpress.XtraEditors.Controls.ImageComboBoxItem(L("RootSkip", "Skip"), CollectionSetupRoot.Skip));
			_grid.RepositoryItems.Add(roots); mapping.ColumnEdit = roots;
			AddColumn("RelativePath", "RelativePath", "Path beneath selected root", 320, true);
			AddColumn("Status", "Status", "Planned action", 260, false);
			_grid.DataSource = new BindingList<CollectionSetupFile>(package.Files); root.Controls.Add(_grid, 0, 2);
			var tabs = new XtraTabControl { Dock = DockStyle.Fill };
			_detail = AddTextTab(tabs, L("Details", "Review details")); _before = AddTextTab(tabs, L("Before", "Current file"));
			_after = AddTextTab(tabs, L("After", "Package file")); AddTextTab(tabs, L("Readmes", "Included READMEs")).Text = package.Readmes;
			root.Controls.Add(tabs, 0, 3);
			var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
			var cancel = new SimpleButton { Text = L("Cancel", "Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
			var apply = new SimpleButton { Text = L("Apply", "Apply selected setup files"), AutoSize = true, Enabled = canApply };
			actions.Controls.Add(cancel); actions.Controls.Add(apply); root.Controls.Add(actions, 0, 4);
			CancelButton = cancel; if (canApply) AcceptButton = apply;
			_view.FocusedRowChanged += (sender, args) => ShowFile();
			_view.CellValueChanged += (sender, args) => { if (!_refreshing && (args.Column.FieldName == "Root" || args.Column.FieldName == "RelativePath")) RefreshMappings(); };
			apply.Click += (sender, args) =>
			{
				_view.PostEditor(); _view.CloseEditor(); _view.UpdateCurrentRow();
				var selected = package.Files.Where(x => x.Selected && x.Root != CollectionSetupRoot.Skip).ToList();
				if (selected.Count == 0 || selected.Any(x => !x.Reviewed || x.Root == CollectionSetupRoot.Unmapped) ||
					selected.Where(x => x.Destination != null).GroupBy(x => x.Destination, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
				{
					XtraMessageBox.Show(this, L("ResolveMappings", "Select files and resolve every selected destination before applying. Two selected files cannot share a destination."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
				}
				DialogResult = DialogResult.OK; Close();
			};
			ShowFile();
		}

		/// <summary>Adds a localized bound review column with explicit edit permissions.</summary>
		private DevExpress.XtraGrid.Columns.GridColumn AddColumn(string field, string key, string caption, int width, bool editable)
		{
			var column = _view.Columns.AddVisible(field, L(key, caption)); column.Width = width;
			column.OptionsColumn.AllowEdit = editable; return column;
		}
		/// <summary>Shows a read-only exact-text view while limiting large binary previews.</summary>
		private static MemoEdit AddTextTab(XtraTabControl tabs, string caption)
		{
			var page = new XtraTabPage { Text = caption }; tabs.TabPages.Add(page);
			var text = new MemoEdit { Dock = DockStyle.Fill }; text.Properties.ReadOnly = true; text.Properties.WordWrap = false;
			text.Properties.ScrollBars = ScrollBars.Both; page.Controls.Add(text); return text;
		}
		/// <summary>Lets the user select a game subfolder beneath redirected Documents instead of trusting README absolute paths.</summary>
		private void ChooseDocuments(object sender, EventArgs args)
		{
			using (var dialog = new FolderBrowserDialog { Description = L("ChooseDocuments", "Choose this game's folder beneath your Documents folder"), SelectedPath = _package.DocumentsDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				_package.DocumentsDirectory = dialog.SelectedPath; _documents.Text = dialog.SelectedPath; RefreshMappings();
			}
		}
		/// <summary>Rebuilds the mapping preview only when the user edits a destination.</summary>
		private void RefreshMappings() { _refreshing = true; try { _service.Refresh(_package); _grid.RefreshDataSource(); ShowFile(); } finally { _refreshing = false; } }
		/// <summary>Displays the destination and exact pre/postimage for the selected payload.</summary>
		private void ShowFile()
		{
			if (_detail == null) return;
			CollectionSetupFile file = _view.GetFocusedRow() as CollectionSetupFile;
			_detail.Text = file == null ? String.Empty : file.Source + Environment.NewLine + file.Destination + Environment.NewLine + file.Status + Environment.NewLine + file.Detail;
			_before.Text = file == null ? String.Empty : PreviewText(file.Before); _after.Text = file == null ? String.Empty : PreviewText(file.Content);
		}
		/// <summary>Previews text without modifying bytes or parsing repeated input bindings as a dictionary.</summary>
		private static string PreviewText(byte[] bytes)
		{
			if (bytes == null) return L("Absent", "File does not exist.");
			if (bytes.Take(1024).Contains((byte)0) && !(bytes.Length >= 2 && (bytes[0] == 255 || bytes[0] == 254)))
				return LanguageManager.Format("Collections.Setup.Binary", "Binary file ({0} bytes)", bytes.Length);
			string text = CollectionSetupPackageReader.Decode(bytes);
			return text.Length <= 256 * 1024 ? text : text.Substring(0, 256 * 1024) + Environment.NewLine + L("Truncated", "Preview truncated; application uses the complete file.");
		}
		/// <summary>Resolves setup review localization.</summary>
		private static string L(string key, string fallback) { return LanguageManager.Get("Collections.Setup." + key, fallback); }
	}
}
