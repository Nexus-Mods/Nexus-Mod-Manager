using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.TableLayout;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Tile;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Wabbajack.UI
{
	/// <summary>Shared, skin-aware Wabbajack gallery; the active game JSON supplies its catalogue filter.</summary>
	internal sealed class WabbajackGalleryControl : ManagedFontDockContent
	{
		private const decimal GiB = 1073741824m;
		private readonly WabbajackClient _client = new WabbajackClient();
		private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
		private readonly Dictionary<string, Task<Image>> _images = new Dictionary<string, Task<Image>>();
		private readonly GridControl _grid;
		private readonly TileView _tiles;
		private readonly TextEdit _search;
		private readonly CheckEdit _unofficial;
		private readonly CheckEdit _nsfw;
		private readonly CheckedComboBoxEdit _tags;
		private readonly CheckedComboBoxEdit _mods;
		private readonly CheckEdit _excludeMods;
		private readonly SpinEdit _minimumSize;
		private readonly SpinEdit _maximumSize;
		private readonly ComboBoxEdit _sort;
		private readonly SimpleButton _refresh;
		private readonly SimpleButton _cancel;
		private readonly SimpleButton _download;
		private readonly SimpleButton _readme;
		private readonly SimpleButton _website;
		private readonly SimpleButton _contents;
		private readonly SimpleButton _openFile;
		private readonly SimpleButton _prepare;
		private readonly MemoEdit _details;
		private readonly LabelControl _status;
		private WabbajackCatalogue _catalogue;
		private GameModeWabbajackDefinition _configuration;
		private CancellationTokenSource _operation;
		private bool _busy;
		private bool _updatingFilters;
		private MainFormVM _viewModel;
		private Func<WabbajackConvertedCollection, string, Task> _openPrepared;
		private string _lastPackagePath;
		private string _lastSourceId;

		/// <summary>Creates native DevExpress tiles, filters and list actions without making network requests.</summary>
		public WabbajackGalleryControl()
		{
			Name = "WabbajackGalleryDocument";
			Text = "Wabbajack";
			AutoScaleMode = AutoScaleMode.Font;
			_search = new TextEdit { Width = 230 };
			_search.Properties.NullValuePrompt = L("Search", "Search lists...");
			_unofficial = new CheckEdit { Text = L("Unofficial", "Include unofficial"), AutoSize = true };
			_nsfw = new CheckEdit { Text = L("Nsfw", "Include NSFW"), AutoSize = true };
			_tags = new CheckedComboBoxEdit { Width = 180 };
			_mods = new CheckedComboBoxEdit { Width = 200 };
			_excludeMods = new CheckEdit { Text = L("ExcludeMods", "Exclude selected mods"), AutoSize = true };
			_minimumSize = CreateSizeEditor(0);
			_maximumSize = CreateSizeEditor(10000);
			_sort = new ComboBoxEdit { Width = 145 };
			_sort.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_sort.Properties.Items.AddRange(new[] { L("SortTitle", "Title"), L("SortUpdated", "Recently updated"), L("SortSize", "Installation size") });
			_sort.SelectedIndex = 0;
			_refresh = Button(L("Refresh", "Refresh"), async () => await LoadCatalogueAsync());
			_cancel = Button(L("Cancel", "Cancel"), () => _operation?.Cancel());
			_cancel.Enabled = false;
			_openFile = Button(L("OpenFile", "Open list file..."), async () => await OpenFileAsync());
			_prepare = Button(L("PrepareInstallation", "Prepare installation..."), async () => await PrepareInstallationAsync());

			FlowLayoutPanel filters = Flow();
			filters.Controls.AddRange(new Control[] { _search, _unofficial, _nsfw, _sort, _refresh, _openFile, _prepare, _cancel });
			FlowLayoutPanel advancedFilters = Flow();
			advancedFilters.Controls.AddRange(new Control[]
			{
				Label(L("Tags", "Tags")), _tags, Label(L("Mods", "Mods")), _mods, _excludeMods,
				Label(L("SizeRange", "Total size (GiB)")), _minimumSize, Label("–"), _maximumSize
			});

			_grid = new GridControl { Dock = DockStyle.Fill };
			_tiles = new TileView(_grid);
			_grid.MainView = _tiles;
			_grid.ViewCollection.Add(_tiles);
			_tiles.OptionsTiles.Orientation = Orientation.Vertical;
			_tiles.OptionsTiles.ItemSize = new Size(300, 245);
			_tiles.OptionsTiles.Padding = new Padding(12);
			_tiles.OptionsTiles.IndentBetweenItems = 12;
			_tiles.TileColumns.Add(new TableColumnDefinition { Length = new TableDefinitionLength(300) });
			_tiles.TileRows.Add(new TableRowDefinition { Length = new TableDefinitionLength(150) });
			_tiles.TileRows.Add(new TableRowDefinition { Length = new TableDefinitionLength(35) });
			_tiles.TileRows.Add(new TableRowDefinition { Length = new TableDefinitionLength(25) });
			_tiles.TileRows.Add(new TableRowDefinition { Length = new TableDefinitionLength(25) });
			TileViewItemElement image = Element(nameof(GalleryRow.Image), 0);
			image.ImageOptions.ImageScaleMode = TileItemImageScaleMode.ZoomInside;
			image.ImageOptions.ImageAlignment = TileItemContentAlignment.MiddleCenter;
			image.ImageOptions.ImageSize = new Size(280, 150);
			_tiles.TileTemplate.Add(image);
			_tiles.TileTemplate.Add(Element(nameof(GalleryRow.Title), 1));
			_tiles.TileTemplate.Add(Element(nameof(GalleryRow.Author), 2));
			_tiles.TileTemplate.Add(Element(nameof(GalleryRow.Summary), 3));
			_tiles.FocusedRowChanged += (sender, args) => ShowSelectedList();
			_grid.DataSource = new List<GalleryRow>();

			_details = new MemoEdit { Dock = DockStyle.Fill };
			_details.Properties.ReadOnly = true;
			_download = Button(L("Download", "Download list..."), async () => await DownloadSelectedAsync());
			_readme = Button(L("Readme", "Installation instructions"), () => OpenLink(SelectedList?.Links.Readme));
			_website = Button(L("Website", "Website"), () => OpenLink(SelectedList?.Links.WebsiteUrl));
			_contents = Button(L("Contents", "List contents"), () => OpenLink(SelectedList == null ? null :
				"https://www.wabbajack.org/search/" + Uri.EscapeDataString(SelectedList.Repository) + "/" + Uri.EscapeDataString(SelectedList.Links.MachineUrl)));
			FlowLayoutPanel actions = Flow();
			actions.Controls.AddRange(new Control[] { _download, _readme, _website, _contents });
			TableLayoutPanel detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
			detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			detailLayout.Controls.Add(_details, 0, 0);
			detailLayout.Controls.Add(actions, 0, 1);
			SplitContainerControl split = new SplitContainerControl { Dock = DockStyle.Fill, Size = new Size(1100, 500), Horizontal = true,
				SplitterPosition = 720, FixedPanel = SplitFixedPanel.Panel2 };
			split.Panel1.Controls.Add(_grid);
			split.Panel2.Controls.Add(detailLayout);
			_status = new LabelControl { Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical, Padding = new Padding(8) };
			LabelControl phase = Label(L("BrowseAndPrepare", "Browse lists or prepare a compiled profile for installation with NMM."));
			phase.Dock = DockStyle.Fill;
			phase.AutoSizeMode = LabelAutoSizeMode.Vertical;
			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(filters, 0, 0);
			layout.Controls.Add(advancedFilters, 0, 1);
			layout.Controls.Add(split, 0, 2);
			layout.Controls.Add(_status, 0, 3);
			layout.Controls.Add(phase, 0, 4);
			Controls.Add(layout);
			foreach (BaseEdit editor in new BaseEdit[] { _search, _unofficial, _nsfw, _tags, _mods, _excludeMods, _minimumSize, _maximumSize, _sort })
				editor.EditValueChanged += (sender, args) => ApplyFilters();
			ShowSelectedList();
		}

		/// <summary>Gets whether the active game explicitly enables the shared gallery.</summary>
		public bool IsAvailable => _configuration != null && _configuration.Enabled && !string.IsNullOrWhiteSpace(_configuration.Game);
		private WabbajackList SelectedList => (_tiles.GetFocusedRow() as GalleryRow)?.List;

		/// <summary>Supplies the active game's JSON configuration; omitted settings leave the tab unavailable.</summary>
		public void Initialize(GameModeWabbajackDefinition configuration, MainFormVM viewModel, Func<WabbajackConvertedCollection, string, Task> openPrepared)
		{
			_configuration = configuration; _viewModel = viewModel; _openPrepared = openPrepared;
		}

		/// <summary>Loads the gallery on its first activation, leaving game startup free of Wabbajack requests.</summary>
		public async Task EnsureLoadedAsync()
		{
			if (IsAvailable && _catalogue == null && !_busy) await LoadCatalogueAsync();
		}

		/// <summary>Applies NMM's existing font and skin-aware display infrastructure.</summary>
		public void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
		}

		/// <summary>Refreshes the public gallery and rebuilds its available filter values.</summary>
		private async Task LoadCatalogueAsync()
		{
			if (_busy || !IsAvailable) return;
			BeginOperation(L("Loading", "Loading Wabbajack lists..."));
			try
			{
				WabbajackCatalogue catalogue = await _client.LoadAsync(_operation.Token);
				if (IsDisposed) return;
				_catalogue = catalogue;
				_updatingFilters = true;
				HashSet<string> selectedTags = new HashSet<string>(_tags.Properties.Items.GetCheckedValues().Cast<string>());
				HashSet<string> selectedMods = new HashSet<string>(_mods.Properties.Items.GetCheckedValues().Cast<string>());
				_tags.Properties.Items.Clear();
				_mods.Properties.Items.Clear();
				_tags.SetEditValue(string.Empty);
				_mods.SetEditValue(string.Empty);
				foreach (string tag in catalogue.Lists.Where(list => string.Equals(list.Game, _configuration.Game, StringComparison.OrdinalIgnoreCase))
					.SelectMany(list => list.Tags).Distinct().OrderBy(tag => tag)) _tags.Properties.Items.Add(tag, tag);
				foreach (string mod in catalogue.SearchIndex.AllMods.OrderBy(mod => mod)) _mods.Properties.Items.Add(mod, mod);
				foreach (CheckedListBoxItem item in _tags.Properties.Items) item.CheckState = selectedTags.Contains((string)item.Value) ? CheckState.Checked : CheckState.Unchecked;
				foreach (CheckedListBoxItem item in _mods.Properties.Items) item.CheckState = selectedMods.Contains((string)item.Value) ? CheckState.Checked : CheckState.Unchecked;
				_updatingFilters = false;
				ApplyFilters();
			}
			catch (OperationCanceledException) { if (!IsDisposed) _status.Text = L("Cancelled", "Cancelled."); }
			catch (Exception error) { ReportError(error); }
			finally { _updatingFilters = false; EndOperation(); }
		}

		/// <summary>Filters catalogue metadata locally and fills native tile cards.</summary>
		private async void ApplyFilters()
		{
			if (_updatingFilters || _catalogue == null || IsDisposed) return;
			string selectedId = SelectedList?.Id;
			WabbajackGalleryFilter filter = new WabbajackGalleryFilter
			{
				Game = _configuration.Game, Search = _search.Text, IncludeUnofficial = _unofficial.Checked, IncludeNsfw = _nsfw.Checked,
				MinimumSize = (long)(_minimumSize.Value * GiB), MaximumSize = (long)(_maximumSize.Value * GiB),
				Tags = _tags.Properties.Items.GetCheckedValues().Cast<string>().ToArray(),
				Mods = _mods.Properties.Items.GetCheckedValues().Cast<string>().ToArray(), ExcludeMods = _excludeMods.Checked
			};
			IEnumerable<WabbajackList> matches = _catalogue.Lists.Where(list => filter.Matches(list, _catalogue.SearchIndex));
			IOrderedEnumerable<WabbajackList> ordered = _sort.SelectedIndex == 1 ? matches.OrderByDescending(list => list.DateUpdated) :
				_sort.SelectedIndex == 2 ? matches.OrderBy(list => list.DownloadMetadata?.TotalSize ?? 0) : matches.OrderBy(list => list.Title);
			List<GalleryRow> rows = ordered.Select(list => new GalleryRow(list)).ToList();
			_grid.DataSource = rows;
			int selectedIndex = rows.FindIndex(row => row.List.Id == selectedId);
			if (selectedIndex >= 0) _tiles.FocusedRowHandle = selectedIndex;
			ShowSelectedList();
			_status.Text = string.Format(L("ListCount", "{0} lists. {1} repositories could not be loaded."), rows.Count, _catalogue.FailedRepositories);
			await Task.WhenAll(rows.Where(row => IsWebLink(row.List.Links.Image)).Select(async row =>
			{
				Task<Image> request;
				if (!_images.TryGetValue(row.List.Links.Image, out request))
				{
					request = LoadImageAsync(row.List.Links.Image);
					_images.Add(row.List.Links.Image, request);
				}
				row.Image = await request;
				if (!IsDisposed) _tiles.RefreshData();
			}));
		}

		/// <summary>Loads artwork through Windows image codecs, including WebP when its system codec is installed.</summary>
		private async Task<Image> LoadImageAsync(string url)
		{
			try
			{
				byte[] bytes = await _client.ReadImageAsync(url, _lifetime.Token);
				Image image;
				using (MemoryStream source = new MemoryStream(bytes))
				{
					BitmapDecoder decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
					PngBitmapEncoder encoder = new PngBitmapEncoder();
					encoder.Frames.Add(decoder.Frames[0]);
					using (MemoryStream png = new MemoryStream())
					{
						encoder.Save(png);
						png.Position = 0;
						using (Image decoded = Image.FromStream(png)) image = new Bitmap(decoded);
					}
				}
				if (IsDisposed) { image.Dispose(); return null; }
				return image;
			}
			catch (OperationCanceledException) { return null; }
			catch (Exception error) { Trace.TraceWarning("Wabbajack artwork could not be loaded: {0}", error.Message); return null; }
		}

		/// <summary>Shows author metadata and enables only actions supplied by the selected list.</summary>
		private void ShowSelectedList()
		{
			WabbajackList list = SelectedList;
			if (_download == null) return;
			_prepare.Enabled = !_busy && IsAvailable && _openPrepared != null;
			_download.Enabled = !_busy && list != null && !list.IsUnavailable && IsWebLink(list.Links.Download);
			_readme.Enabled = list != null && IsWebLink(list.Links.Readme);
			_website.Enabled = list != null && IsWebLink(list.Links.WebsiteUrl);
			_contents.Enabled = list != null;
			_details.Text = list == null ? L("SelectList", "Select a list to see its details.") :
				list.Title + Environment.NewLine + list.Author + Environment.NewLine + list.Version + Environment.NewLine + Environment.NewLine +
				list.Description + Environment.NewLine + Environment.NewLine + string.Join(", ", list.Tags) + Environment.NewLine +
				string.Format(L("Sizes", "Downloads: {0}\r\nInstalled files: {1}"), FormatSize(list.DownloadMetadata?.SizeOfArchives ?? 0),
					FormatSize(list.DownloadMetadata?.SizeOfInstalledFiles ?? 0)) + Environment.NewLine +
				(list.IsUnavailable ? L("Unavailable", "Unavailable / under maintenance") : list.Status == null ? L("UnknownStatus", "Availability report unavailable") : L("Available", "Available"));
		}

		/// <summary>Downloads the selected compiled recipe and opens its inspection summary.</summary>
		private async Task DownloadSelectedAsync()
		{
			WabbajackList list = SelectedList;
			if (_busy || list == null || list.IsUnavailable) return;
			using (SaveFileDialog dialog = new SaveFileDialog { Filter = L("PackageFilter", "Wabbajack lists (*.wabbajack)|*.wabbajack"),
				FileName = string.Concat(list.Title.Split(Path.GetInvalidFileNameChars())) + ".wabbajack", DefaultExt = "wabbajack" })
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				BeginOperation(L("Downloading", "Downloading list..."));
				try
				{
					Progress<int> progress = new Progress<int>(percent => { if (!IsDisposed) _status.Text = string.Format(L("DownloadProgress", "Downloading list: {0}%"), percent); });
					await _client.DownloadAsync(list, dialog.FileName, progress, _operation.Token);
					await InspectAsync(dialog.FileName, list.Id);
					if (!IsDisposed) _status.Text = string.Format(L("Downloaded", "Downloaded: {0}"), dialog.FileName);
				}
				catch (OperationCanceledException) { if (!IsDisposed) _status.Text = L("Cancelled", "Cancelled."); }
				catch (Exception error) { ReportError(error); }
				finally { EndOperation(); }
			}
		}

		/// <summary>Inspects an existing package independently of the gallery's availability.</summary>
		private async Task OpenFileAsync()
		{
			if (_busy) return;
			using (OpenFileDialog dialog = new OpenFileDialog { Filter = L("PackageFilter", "Wabbajack lists (*.wabbajack)|*.wabbajack") })
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				BeginOperation(L("Reading", "Reading list..."));
				try { await InspectAsync(dialog.FileName); }
				catch (OperationCanceledException) { if (!IsDisposed) _status.Text = L("Cancelled", "Cancelled."); }
				catch (Exception error) { ReportError(error); }
				finally { EndOperation(); }
			}
		}

		/// <summary>Summarizes typed sources and directives while preserving the full recipe for later translation.</summary>
		private async Task InspectAsync(string filePath, string sourceId = null)
		{
			WabbajackPackage package = await Task.Run(() => WabbajackPackage.Read(filePath), _operation.Token);
			if (IsDisposed || _operation.IsCancellationRequested) return;
			_lastPackagePath = filePath; _lastSourceId = sourceId;
			StringBuilder text = new StringBuilder();
			text.AppendLine(package.Name).AppendLine(package.Author).AppendLine(package.GameType).AppendLine(package.Version).AppendLine();
			text.AppendLine(package.Description).AppendLine();
			text.AppendLine(L("Sources", "Sources:"));
			foreach (IGrouping<string, WabbajackArchive> group in package.Archives.GroupBy(archive => archive.State?.Type ?? "Unknown"))
				text.AppendLine(group.Key + ": " + group.Count());
			text.AppendLine().AppendLine(L("Instructions", "Installation instructions:"));
			foreach (IGrouping<string, WabbajackDirective> group in package.Directives.GroupBy(directive => directive.Type ?? "Unknown"))
				text.AppendLine(group.Key + ": " + group.Count());
			text.AppendLine().AppendLine(L("PrepareInspectionHint", "Prepare installation opens this profile as a Collection for NMM's installation review."));
			using (XtraForm form = new XtraForm { Text = package.Name ?? "Wabbajack", Size = new Size(700, 560), StartPosition = FormStartPosition.CenterParent })
			using (MemoEdit memo = new MemoEdit { Dock = DockStyle.Fill, Text = text.ToString() })
			{
				memo.Properties.ReadOnly = true;
				form.Controls.Add(memo);
				form.ShowDialog(this);
			}
		}

		/// <summary>Prepares a selected compiled list and passes it to the existing Collection installation surface.</summary>
		private async Task PrepareInstallationAsync()
		{
			if (_busy || _viewModel == null || _openPrepared == null) return;
			using (OpenFileDialog file = new OpenFileDialog { Filter = L("PackageFilter", "Wabbajack lists (*.wabbajack)|*.wabbajack"), FileName = _lastPackagePath ?? string.Empty })
			{
				if (file.ShowDialog(this) != DialogResult.OK) return;
				string sourceId = string.Equals(file.FileName, _lastPackagePath, StringComparison.OrdinalIgnoreCase) ? _lastSourceId : null;
				BeginOperation(L("Reading", "Reading list..."));
				try
				{
					WabbajackPackage package = await Task.Run(() => WabbajackPackage.Read(file.FileName), _operation.Token);
					if (IsDisposed || _operation.IsCancellationRequested) return;
					using (WabbajackPreparationDialog dialog = new WabbajackPreparationDialog(_viewModel, package, _client, sourceId))
					{
						if (dialog.ShowDialog(this) != DialogResult.OK) return;
						string settingsDirectory = (_viewModel.GameMode as Nexus.Client.Games.Gamebryo.GamebryoGameModeBase)?.UserGameDataPath;
						await _openPrepared(dialog.Result, settingsDirectory);
					}
				}
				catch (OperationCanceledException) { }
				catch (Exception error) { ReportError(error); }
				finally { EndOperation(); }
			}
		}

		/// <summary>Starts one user operation with cancellation and ordinary action state.</summary>
		private void BeginOperation(string message)
		{
			_busy = true;
			_operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			_refresh.Enabled = _openFile.Enabled = false;
			_cancel.Enabled = true;
			_status.Text = message;
			ShowSelectedList();
		}

		/// <summary>Finishes an operation without changing installed game state.</summary>
		private void EndOperation()
		{
			_operation?.Dispose();
			_operation = null;
			_busy = false;
			if (IsDisposed) return;
			_refresh.Enabled = _openFile.Enabled = true;
			_cancel.Enabled = false;
			ShowSelectedList();
		}

		/// <summary>Reports a network or package-read error through the existing UI and trace conventions.</summary>
		private void ReportError(Exception error)
		{
			Trace.TraceError("Wabbajack: {0}", error);
			if (!IsDisposed) _status.Text = L("Error", "Wabbajack: ") + error.Message;
		}

		/// <summary>Opens ordinary web links using the system browser.</summary>
		private void OpenLink(string url)
		{
			if (!IsWebLink(url)) return;
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch (Exception error) { ReportError(error); }
		}

		/// <summary>Recognizes browser links supplied by upstream metadata.</summary>
		private static bool IsWebLink(string url)
		{
			Uri uri;
			return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
		}

		/// <summary>Creates a skinned text element bound to a card field.</summary>
		private TileViewItemElement Element(string field, int row)
		{
			return new TileViewItemElement { Column = _tiles.Columns.AddVisible(field), RowIndex = row,
				TextAlignment = TileItemContentAlignment.MiddleLeft, MaxLineCount = row == 1 ? 2 : 1 };
		}

		/// <summary>Creates an automatically wrapping toolbar row.</summary>
		private static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(5) }; }

		/// <summary>Creates a skin-aware filter label.</summary>
		private static LabelControl Label(string text) { return new LabelControl { Text = text, Margin = new Padding(5, 7, 5, 3) }; }

		/// <summary>Creates a button using the existing DevExpress appearance.</summary>
		private static SimpleButton Button(string text, Action action)
		{
			SimpleButton button = new SimpleButton { Text = text, AutoSize = true };
			button.Click += (sender, args) => action();
			return button;
		}

		/// <summary>Creates a non-negative GiB range editor.</summary>
		private static SpinEdit CreateSizeEditor(decimal value)
		{
			SpinEdit editor = new SpinEdit { Width = 75 };
			editor.Properties.MinValue = 0;
			editor.Properties.MaxValue = 100000;
			editor.Value = value;
			return editor;
		}

		/// <summary>Formats upstream byte counts for cards and details.</summary>
		private static string FormatSize(long bytes) { return (bytes / GiB).ToString("0.0") + " GiB"; }

		/// <summary>Resolves gallery strings through NMM's language infrastructure.</summary>
		private static string L(string key, string fallback) { return LanguageManager.Get("Wabbajack." + key, fallback); }

		/// <summary>Cancels outstanding requests and releases downloaded artwork when the game session closes.</summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing && !_lifetime.IsCancellationRequested)
			{
				_lifetime.Cancel();
				_client.Dispose();
				foreach (Task<Image> request in _images.Values)
					if (request.Status == TaskStatus.RanToCompletion) request.Result?.Dispose();
			}
			base.Dispose(disposing);
		}

		/// <summary>Presentation fields used by the native DevExpress tile template.</summary>
		private sealed class GalleryRow
		{
			/// <summary>Creates a card over the original upstream metadata.</summary>
			public GalleryRow(WabbajackList list) { List = list; }
			public WabbajackList List { get; }
			public string Title => List.Title;
			public string Author => List.Author;
			public Image Image { get; set; }
			public string Summary => FormatSize(List.DownloadMetadata?.TotalSize ?? 0) + "  ·  " +
				(List.IsUnavailable ? L("Unavailable", "Unavailable / under maintenance") : List.Official ? L("Featured", "Featured") : L("Unofficial", "Unofficial"));
		}
	}
}
