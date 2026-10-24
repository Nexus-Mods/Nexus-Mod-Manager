using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
		private readonly WabbajackSummaryControl _details;
		private readonly WabbajackActivityControl _activity;
		private readonly WabbajackActivityControl _unofficialSupportWarning;
		private readonly LabelControl _modAuthorWarning;
		private readonly GroupControl _detailGroup;
		private readonly TableLayoutPanel _listDetails;
		private WabbajackPreparationControl _preparation;
		private readonly Dictionary<string, string> _downloadedPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private WabbajackCatalogue _catalogue;
		private GameModeWabbajackDefinition _configuration;
		private CancellationTokenSource _operation;
		private bool _busy;
		private bool _updatingFilters;
		private MainFormVM _viewModel;
		private Func<WabbajackConvertedCollection, string, Task> _openPrepared;
		private bool _operationFailed;

		/// <summary>Creates native DevExpress tiles, filters and list actions without making network requests.</summary>
		public WabbajackGalleryControl()
		{
			Name = "WabbajackGalleryDocument";
			Text = "Wabbajack";
			AutoScaleMode = AutoScaleMode.Font;
			_search = new TextEdit { Width = 230, MinimumSize = new Size(230, 0) };
			_search.Properties.NullValuePrompt = L("Search", "Search lists...");
			_unofficial = Check(L("Unofficial", "Include unofficial"));
			_nsfw = Check(L("Nsfw", "Include NSFW"));
			_tags = new CheckedComboBoxEdit { Width = 180, MinimumSize = new Size(180, 0) };
			_mods = new CheckedComboBoxEdit { Width = 200, MinimumSize = new Size(200, 0) };
			_excludeMods = Check(L("ExcludeMods", "Exclude selected mods"));
			_minimumSize = CreateSizeEditor(0);
			_maximumSize = CreateSizeEditor(10000);
			_sort = new ComboBoxEdit { Width = 180, MinimumSize = new Size(180, 0) };
			_sort.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_sort.Properties.Items.AddRange(new[] { L("SortTitle", "Title"), L("SortUpdated", "Recently updated"), L("SortSize", "Installation size") });
			_sort.SelectedIndex = 0;
			_refresh = Button(L("Refresh", "Refresh"), async () => await LoadCatalogueAsync());
			_cancel = Button(L("Cancel", "Cancel"), () => { if (_preparation?.IsBusy == true) _preparation.CancelWork(); else _operation?.Cancel(); _cancel.Enabled = false; _activity.Detail = L("Cancelling", "Cancelling the current operation..."); });
			_cancel.Enabled = false;
			_openFile = Button(L("OpenDownloadedList", "Open downloaded list..."), async () => await OpenFileAsync());
			NmmIconProvider.Bind(_refresh, NmmIconAction.Refresh);
			NmmIconProvider.Bind(_openFile, NmmIconAction.OpenFolder);
			NmmIconProvider.Bind(_cancel, NmmIconAction.Cancel);

			WabbajackActionRow filters = new WabbajackActionRow();
			filters.Controls.AddRange(new Control[] { Label(L("SearchLabel", "Search")), _search, Label(L("SortLabel", "Sort by")), _sort, _unofficial, _nsfw });
			WabbajackActionRow advancedFilters = new WabbajackActionRow();
			advancedFilters.Controls.AddRange(new Control[]
			{
				Label(L("Tags", "Tags")), _tags, Label(L("Mods", "Mods")), _mods, _excludeMods,
				Label(L("SizeRange", "Total size (GiB)")), _minimumSize, Label("–"), _maximumSize
			});
			WabbajackActionRow galleryActions = new WabbajackActionRow();
			galleryActions.Controls.AddRange(new Control[] { _refresh, _openFile });


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

			_details = new WabbajackSummaryControl();
			_download = Button(L("DownloadAndPrepare", "Download & prepare"), async () => await DownloadSelectedAsync());
			_download.Appearance.FontStyleDelta = FontStyle.Bold;
			_readme = Button(L("Readme", "Installation instructions"), () => OpenLink(SelectedList?.Links.Readme));
			_website = Button(L("Website", "Website"), () => OpenLink(SelectedList?.Links.WebsiteUrl));
			_contents = Button(L("Contents", "List contents"), () => OpenLink(SelectedList == null ? null :
				"https://www.wabbajack.org/search/" + Uri.EscapeDataString(SelectedList.Repository) + "/" + Uri.EscapeDataString(SelectedList.Links.MachineUrl)));
			NmmIconProvider.Bind(_download, NmmIconAction.InstallEnable);
			NmmIconProvider.Bind(_readme, NmmIconAction.Help);
			NmmIconProvider.Bind(_website, NmmIconAction.ExternalLink);
			NmmIconProvider.Bind(_contents, NmmIconAction.Browse);
			WabbajackActionRow primaryAction = new WabbajackActionRow();
			primaryAction.Controls.Add(_download);
			WabbajackActionRow links = new WabbajackActionRow();
			links.Controls.AddRange(new Control[] { _readme, _website, _contents });
			XtraScrollableControl detailScroll = new XtraScrollableControl { Dock = DockStyle.Fill };
			_details.Dock = DockStyle.Top; detailScroll.Controls.Add(_details);
			_detailGroup = new GroupControl { Dock = DockStyle.Fill, Text = L("SelectedList", "Selected modlist"), Padding = new Padding(6) };
			TableLayoutPanel detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			_listDetails = detailLayout;
			detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			detailLayout.Controls.Add(detailScroll, 0, 0);
			detailLayout.Controls.Add(primaryAction, 0, 1); detailLayout.Controls.Add(links, 0, 2);
			_detailGroup.Controls.Add(detailLayout);
			SplitContainerControl split = new SplitContainerControl { Dock = DockStyle.Fill, Size = new Size(1200, 500), Horizontal = true,
				SplitterPosition = 700, FixedPanel = SplitFixedPanel.Panel2 };
			split.Panel2.Controls.Add(_detailGroup);
			_activity = new WabbajackActivityControl();
			_activity.ShowActivity(L("Ready", "Ready to browse"), L("GallerySteps", "Choose a modlist → Download & prepare → Review and install in Collections."), false);
			_modAuthorWarning = WabbajackUi.Caption(L("ModAuthorSupportWarning", "Wabbajack lists may contain mod edits that are neither authorized nor endorsed by the mod authors. Do not ask mod authors to troubleshoot broken mods installed through these lists."));
			_modAuthorWarning.Dock = DockStyle.Top;
			_modAuthorWarning.Margin = new Padding(6, 4, 6, 4);
			_modAuthorWarning.ImageAlignToText = ImageAlignToText.LeftCenter;
			_modAuthorWarning.IndentBetweenImageAndText = 8;
			_modAuthorWarning.ImageOptions.Image = NmmIconProvider.GetBitmap(NmmIconAction.Warning, NmmIconProvider.CurrentIconSize, false);
			TableLayoutPanel feedback = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
			feedback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); feedback.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
			feedback.RowStyles.Add(new RowStyle(SizeType.AutoSize)); feedback.Controls.Add(_activity, 0, 0);
			_cancel.Anchor = AnchorStyles.Right; feedback.Controls.Add(_cancel, 1, 0);

			_unofficialSupportWarning = new WabbajackActivityControl { Margin = new Padding(0, 0, 0, 8) };
			_unofficialSupportWarning.ShowActivity(L("UnofficialSupportTitle", "Unofficial Wabbajack support"),
				L("UnofficialSupportWarning", "Wabbajack support in NMM CE is unofficial. Report issues with this integration to NMM CE. Do not ask list authors or Wabbajack curators to troubleshoot installations made through NMM CE."), false, true);
			// Gallery filters belong to the cards pane; preparation uses the full height alongside it.
			TableLayoutPanel galleryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
			galleryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			galleryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			galleryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			galleryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			galleryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			galleryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			galleryLayout.Controls.Add(_unofficialSupportWarning, 0, 0);
			galleryLayout.Controls.Add(filters, 0, 1);
			galleryLayout.Controls.Add(advancedFilters, 0, 2);
			galleryLayout.Controls.Add(galleryActions, 0, 3);
			galleryLayout.Controls.Add(_grid, 0, 4);
			split.Panel1.Controls.Add(galleryLayout);
			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(split, 0, 0);
			layout.Controls.Add(feedback, 0, 1);
			layout.Controls.Add(_modAuthorWarning, 0, 2);
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
			_activity.RefreshIcon();
			_unofficialSupportWarning.RefreshIcon();
			_modAuthorWarning.ImageOptions.Image = NmmIconProvider.GetBitmap(NmmIconAction.Warning, NmmIconProvider.CurrentIconSize, false);
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
			catch (OperationCanceledException) { if (!IsDisposed) _activity.Detail = L("Cancelled", "Cancelled."); }
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
			if (!_busy && _preparation == null) _activity.ShowActivity(L("GalleryReady", "Gallery ready"), CatalogueStatus(rows.Count), false, _catalogue.FailedRepositories > 0);
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
			_download.Enabled = !_busy && list != null && !list.IsUnavailable && IsWebLink(list.Links.Download) && _openPrepared != null;
			_readme.Enabled = list != null && IsWebLink(list.Links.Readme);
			_website.Enabled = list != null && IsWebLink(list.Links.WebsiteUrl);
			_contents.Enabled = list != null;
			_details.ShowList(list);
			if (_preparation == null) _detailGroup.Text = list?.Title ?? L("SelectedList", "Selected modlist");
		}

		/// <summary>Downloads the selected list through Download Manager and prepares that exact package in its details pane.</summary>
		private async Task DownloadSelectedAsync()
		{
			WabbajackList list = SelectedList;
			if (_busy || list == null || list.IsUnavailable || _viewModel == null || _preparation != null) return;
			string key = list.Id + "/" + list.Version + "/" + list.DownloadMetadata?.Hash;
			string existing;
			if (_downloadedPackages.TryGetValue(key, out existing) && File.Exists(existing))
			{
				BeginOperation(L("Reading", "Reading list..."));
				try { await OpenPreparationAsync(existing, list.Id); } catch (Exception error) { ReportError(error); } finally { EndOperation(); }
				return;
			}
			using (SaveFileDialog dialog = new SaveFileDialog { Filter = L("PackageFilter", "Wabbajack lists (*.wabbajack)|*.wabbajack"),
				FileName = string.Concat(list.Title.Split(Path.GetInvalidFileNameChars())) + ".wabbajack", DefaultExt = "wabbajack" })
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				BeginOperation(string.Format(L("DownloadingNamedList", "Downloading {0}"), list.Title));
				try
				{
					Progress<string> progress = new Progress<string>(message => { if (!IsDisposed && _busy) _activity.Detail = message; });
					string path = await WabbajackDownloadQueue.DownloadAsync(_viewModel.ModManager, new Uri(list.Links.Download), dialog.FileName, list.DownloadMetadata?.Hash, list.DownloadMetadata?.Size ?? 0, progress, _operation.Token);
					_downloadedPackages[key] = path;
					await OpenPreparationAsync(path, list.Id);
				}
				catch (OperationCanceledException) { if (!IsDisposed) _activity.Detail = L("DownloadsContinue", "Queued downloads remain available in Download Manager. Check sources when you are ready to continue."); }
				catch (Exception error) { ReportError(error); }
				finally { EndOperation(); }
			}
		}

		/// <summary>Inspects an existing package independently of the gallery's availability.</summary>
		private async Task OpenFileAsync()
		{
			if (_busy || _preparation != null) return;
			using (OpenFileDialog dialog = new OpenFileDialog { Filter = L("PackageFilter", "Wabbajack lists (*.wabbajack)|*.wabbajack") })
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				BeginOperation(L("Reading", "Reading list..."));
				try { await OpenPreparationAsync(dialog.FileName); }
				catch (OperationCanceledException) { if (!IsDisposed) _activity.Detail = L("Cancelled", "Cancelled."); }
				catch (Exception error) { ReportError(error); }
				finally { EndOperation(); }
			}
		}

		/// <summary>Replaces the selected-list details with preparation for the actual downloaded package.</summary>
		private async Task OpenPreparationAsync(string path, string sourceId = null)
		{
			WabbajackPackage package = await Task.Run(() => WabbajackPackage.Read(path), _operation.Token);
			if (IsDisposed || _operation.IsCancellationRequested) return;
			WabbajackPreparationControl preparation = new WabbajackPreparationControl(_viewModel, package, sourceId, _openPrepared, _activity);
			preparation.BackRequested += (sender, args) => {
				_detailGroup.Controls.Remove(preparation); preparation.Dispose(); _preparation = null;
				_detailGroup.Controls.Add(_listDetails); ShowSelectedList();
				_refresh.Enabled = _openFile.Enabled = true;
				_activity.ShowActivity(L("GalleryReady", "Gallery ready"), CatalogueStatus((_grid.DataSource as List<GalleryRow>)?.Count ?? 0), false);
			};
			preparation.BusyChanged += (sender, args) => { _cancel.Enabled = preparation.IsBusy; };
			_preparation = preparation;
			_detailGroup.Controls.Remove(_listDetails);
			_detailGroup.Text = string.Format(L("PreparingNamedList", "Prepare {0}"), package.Name);
			_detailGroup.Controls.Add(preparation);
			using (DevExpressDisplaySettings display = DevExpressDisplaySettings.CreateFromSettings(_viewModel.EnvironmentInfo.Settings))
				DevExpressDisplaySettingsApplier.ApplyToControlTree(preparation, display);
		}
		/// <summary>Starts one user operation with cancellation and ordinary action state.</summary>
		private void BeginOperation(string message)
		{
			_busy = true;
			_operationFailed = false;
			_operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
			_refresh.Enabled = _openFile.Enabled = false;
			_cancel.Enabled = true;
			_activity.ShowActivity(message, L("DownloadManagerHint", "Downloads appear in Download Manager. You can pause, resume or cancel them there."), true);
			ShowSelectedList();
		}

		/// <summary>Finishes an operation without changing installed game state.</summary>
		private void EndOperation()
		{
			_operation?.Dispose();
			_operation = null;
			_busy = false;
			if (IsDisposed) return;
			_refresh.Enabled = _preparation == null;
			_openFile.Enabled = _preparation == null;
			_cancel.Enabled = false;
			if (_operationFailed || _preparation == null)
				_activity.ShowActivity(_operationFailed ? L("ActionRequired", "Action required") : L("GalleryReady", "Gallery ready"),
					_operationFailed ? _activity.Detail : CatalogueStatus((_grid.DataSource as List<GalleryRow>)?.Count ?? 0), false, _operationFailed);
			ShowSelectedList();
		}

		/// <summary>Reports a network or package-read error through the existing UI and trace conventions.</summary>
		private void ReportError(Exception error)
		{
			Trace.TraceError("Wabbajack: {0}", error);
			_operationFailed = true;
			if (!IsDisposed) _activity.ShowActivity(L("ActionRequired", "Action required"), error.Message, false, true);
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

		/// <summary>Describes the loaded gallery and the next action without a redundant zero-failure warning.</summary>
		private string CatalogueStatus(int count)
		{
			return string.Format(L("GalleryCount", "{0} modlists. Select one, then choose Download & prepare."), count) +
				(_catalogue != null && _catalogue.FailedRepositories > 0 ? " " + string.Format(L("RepositoryFailures", "{0} repositories could not be loaded."), _catalogue.FailedRepositories) : string.Empty);
		}

		/// <summary>Sizes checkbox captions through DevExpress rather than the default fixed editor width.</summary>
		private static CheckEdit Check(string text) { CheckEdit check = new CheckEdit { Text = text, AutoSize = true, Margin = new Padding(8, 4, 8, 4) }; check.Properties.AutoWidth = true; return check; }

		/// <summary>Creates a skin-aware filter label.</summary>
		private static LabelControl Label(string text) { return new LabelControl { Text = text, AutoSizeMode = LabelAutoSizeMode.Horizontal, Margin = new Padding(6, 7, 6, 4) }; }

		/// <summary>Creates a button using the existing DevExpress appearance.</summary>
		private static SimpleButton Button(string text, Action action)
		{
			SimpleButton button = new SimpleButton { Text = text, AutoSize = true, Margin = new Padding(4, 4, 4, 4) };
			button.Click += (sender, args) => action();
			return button;
		}

		/// <summary>Creates a non-negative GiB range editor.</summary>
		private static SpinEdit CreateSizeEditor(decimal value)
		{
			SpinEdit editor = new SpinEdit { Width = 75, MinimumSize = new Size(75, 0) };
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
