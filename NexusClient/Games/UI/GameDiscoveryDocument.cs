namespace Nexus.Client.Games.UI
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Drawing;
	using System.Drawing.Drawing2D;
	using System.Linq;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Controls;
	using DevExpress.XtraEditors.Repository;
	using DevExpress.XtraGrid;
	using DevExpress.XtraGrid.Views.Grid;
	using Nexus.Client.Games;
	using Nexus.Client.UI;
	using Nexus.Client.Util.Localization;

	/// <summary>
	/// Read-only, skin-aware game catalog. The three collections are selectors, not
	/// separate full-window grids. Changes to storage and active sessions are only
	/// initiated by explicit actions handled by the main window.
	/// </summary>
	internal sealed class GameDiscoveryDocument : XtraForm
	{
		private const int LogoRowHeight = 64;
		private readonly TextEdit _search;
		private readonly SimpleButton _refresh;
		private readonly SimpleButton _scan;
		private readonly SimpleButton _stopScan;
		private readonly SimpleButton _primaryAction;
		private readonly SimpleButton _startupDefault;
		private readonly SimpleButton _locate;
		private readonly ComboBoxEdit _installations;
		private readonly LabelControl _status;
		private readonly TableLayoutPanel _statusLayout;
		private readonly CompletionRing _loadingRing;
		private readonly PictureEdit _activeArtwork;
		private Image _activeArtworkSource;
		private readonly LabelControl _activeGameName;
		private readonly SimpleButton _changeArtwork;
		private readonly SimpleButton _resetArtwork;
		private readonly TableLayoutPanel _activeFooter;
		private readonly PanelControl _activeCard;
		private GameDiscoveryArtwork _artwork;
		private string _activeModeId;
		private readonly LabelControl _emptyList;
		private readonly LabelControl _gameTitle;
		private readonly LabelControl _gameState;
		private readonly LabelControl _sourceLine;
		private readonly LabelControl _pathTitle;
		private readonly LabelControl _startupValue;
		private readonly LabelControl _startupTitle;
		private readonly LabelControl _candidateHint;
		private readonly TextEdit _installationPath;
		private readonly MemoEdit _details;
		private readonly PanelControl _candidatePanel;
		private readonly TableLayoutPanel _detailLayout;
		private readonly GroupControl _listCard;
		private readonly GridControl _grid;
		private readonly GridView _view;
		private readonly Dictionary<string, Image> _logos = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
		private Func<string, Icon> _logoProvider;
		private Func<GameDiscoveryEntry, GameDiscoveryStatistics> _statisticsProvider;
		private readonly PanelControl[] _collectionCards = new PanelControl[3];
		private readonly LabelControl[] _collectionTitles = new LabelControl[3];
		private readonly LabelControl[] _collectionCounts = new LabelControl[3];
		private readonly GameDiscoveryCollection[] _collections =
		{
			GameDiscoveryCollection.Managed,
			GameDiscoveryCollection.ToConfigure,
			GameDiscoveryCollection.Supported
		};
		private readonly PanelControl _viewport;
		private readonly TableLayoutPanel _canvas;

		private GameDiscoveryCatalog _catalog;
		private GameDiscoveryCollection _visibleCollection = GameDiscoveryCollection.Managed;
		private string _selectedModeId;
		private string _selectedInstallationRoot;
		private bool _updatingInstallations;
		private bool _repopulating;
		private bool _sessionLoading;
		private bool _sessionActionsAvailable;
		private bool _refreshing;
		private bool _scanning;

		internal event EventHandler RefreshRequested;
		internal event EventHandler StatisticsRequested;
		internal event EventHandler ScanRequested;
		internal event EventHandler StopScanRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> ManageRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> StartupDefaultRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> ConfigureRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> LocateRequested;
		internal GameDiscoveryCatalog Catalog => _catalog;

		internal GameDiscoveryDocument()
		{
			Text = L("GameDiscovery.TabTitle", "Games");
			FormBorderStyle = FormBorderStyle.None;
			ControlBox = false;

			_viewport = new PanelControl { Dock = DockStyle.Fill, AutoScroll = true, BorderStyle = BorderStyles.NoBorder };
			Controls.Add(_viewport);
			_canvas = new TableLayoutPanel
			{
				ColumnCount = 2,
				RowCount = 5,
				Padding = new Padding(12, 14, 12, 10),
				Margin = Padding.Empty,
				Width = 1180,
				Height = 785
			};
			// The active game's cover lives beside the catalog, never in a footer.
			// Reserve its column only when a game is actually loaded.
			_canvas.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
			_canvas.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			_canvas.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			_canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
			_canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
			_canvas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			_canvas.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
			_viewport.Controls.Add(_canvas);
			_viewport.Resize += (sender, args) => ResizeCanvas();

			var heading = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 2 };
			heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
			heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			var headingTitle = new LabelControl
			{
				Text = L("GameDiscovery.Title", "Game Discovery"),
				Dock = DockStyle.Fill,
				UseMnemonic = false,
				Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 18f, FontStyle.Bold) }
			};
			var introduction = new LabelControl
			{
				Text = L("GameDiscovery.Introduction", "Managed: Double-click a game or click Manage to open its game mode.\nTo configure: Select a game and configure its NMM folders to start managing it.\nSupported: Select a game and set its installation path, or use Scan for games to find more games on your system."),
				Dock = DockStyle.Top,
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				UseMnemonic = false,
				Appearance = { TextOptions = { WordWrap = DevExpress.Utils.WordWrap.Wrap, VAlignment = DevExpress.Utils.VertAlignment.Top } }
			};
			heading.Controls.Add(headingTitle, 0, 0);
			heading.Controls.Add(introduction, 0, 1);
			_canvas.Controls.Add(heading, 1, 0);

			// Use fixed-height, top-aligned command controls. Stretching DevExpress
			// SimpleButtons through a short TableLayout row clipped their captions
			// under several DPI/font scale combinations.
			var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 6, 0, 0) };
			tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 182));
			tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
			tools.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			_canvas.Controls.Add(tools, 1, 1);
			_search = new TextEdit { Dock = DockStyle.Top, Height = 34, Margin = new Padding(0, 3, 16, 0) };
			_search.Properties.NullValuePrompt = L("GameDiscovery.Search", "Search games by name...");
			_search.EditValueChanged += (sender, args) => Populate();
			tools.Controls.Add(_search, 0, 0);
			_refresh = new SimpleButton { Dock = DockStyle.Top, Height = 36, Text = L("GameDiscovery.Refresh", "Refresh detected games"), Margin = new Padding(0, 2, 10, 0) };
			NmmIconProvider.BindDialogButton(_refresh, NmmIconAction.Refresh);
			_refresh.Click += (sender, args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
			tools.Controls.Add(_refresh, 1, 0);
			_scan = new SimpleButton { Dock = DockStyle.Top, Height = 36, Text = L("GameDiscovery.Scan", "Scan for games..."), Margin = Padding.Empty, Enabled = false };
			NmmIconProvider.BindDialogButton(_scan, NmmIconAction.Browse);
			_scan.ToolTip = L("GameDiscovery.ScanScope", "Search all fixed drives for games that weren’t detected.");
			_scan.Click += (sender, args) => ScanRequested?.Invoke(this, EventArgs.Empty);
			var scanContainer = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 0) };
			tools.Controls.Add(scanContainer, 2, 0);
			scanContainer.Controls.Add(_scan);
			_stopScan = new SimpleButton { Dock = DockStyle.Top, Height = 36, Text = L("GameDiscovery.StopScan", "Stop scan"), Margin = Padding.Empty, Enabled = false, Visible = false };
			NmmIconProvider.BindDialogButton(_stopScan, NmmIconAction.Cancel);
			_stopScan.Click += (sender, args) => StopScanRequested?.Invoke(this, EventArgs.Empty);
			scanContainer.Controls.Add(_stopScan);

			var categories = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Padding = new Padding(0, 4, 0, 12) };
			for (int index = 0; index < 3; index++)
				categories.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
			for (int index = 0; index < 3; index++)
			{
				int selectedIndex = index;
				var tile = new PanelControl
				{
					Dock = DockStyle.Fill,
					TabStop = true,
					Cursor = Cursors.Hand,
					Padding = new Padding(14, 6, 14, 4),
					BorderStyle = BorderStyles.NoBorder,
					Margin = new Padding(index == 0 ? 0 : 5, 0, index == 2 ? 0 : 5, 0)
				};
				var tileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Cursor = Cursors.Hand };
				tileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
				tileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
				var tileTitle = new LabelControl
				{
					Dock = DockStyle.Fill,
					UseMnemonic = false,
					Cursor = Cursors.Hand,
					Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Regular) }
				};
				var tileCount = new LabelControl
				{
					Dock = DockStyle.Fill,
					UseMnemonic = false,
					Cursor = Cursors.Hand,
					Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 19f, FontStyle.Bold) }
				};
				tile.Click += (sender, args) => SelectCollection(_collections[selectedIndex]);
				tileLayout.Click += (sender, args) => SelectCollection(_collections[selectedIndex]);
				tileTitle.Click += (sender, args) => SelectCollection(_collections[selectedIndex]);
				tileCount.Click += (sender, args) => SelectCollection(_collections[selectedIndex]);
				tile.KeyDown += (sender, args) =>
				{
					if (args.KeyCode != Keys.Enter && args.KeyCode != Keys.Space) return;
					SelectCollection(_collections[selectedIndex]);
					args.Handled = true;
				};
				tileLayout.Controls.Add(tileTitle, 0, 0);
				tileLayout.Controls.Add(tileCount, 0, 1);
				tile.Controls.Add(tileLayout);
				_collectionCards[index] = tile;
				_collectionTitles[index] = tileTitle;
				_collectionCounts[index] = tileCount;
				categories.Controls.Add(tile, index, 0);
			}
			_canvas.Controls.Add(categories, 1, 2);

			var mainArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
			mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
			mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
			mainArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
			_canvas.Controls.Add(mainArea, 1, 3);

			_listCard = new GroupControl { Dock = DockStyle.Fill, Text = L("GameDiscovery.ManagedGames", "Managed games") };
			mainArea.Controls.Add(_listCard, 0, 0);
			var listBody = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.NoBorder, Padding = new Padding(7, 8, 7, 8) };
			_listCard.Controls.Add(listBody);
			_grid = new GridControl { Dock = DockStyle.Fill };
			_view = new GridView(_grid);
			_grid.MainView = _view;
			_grid.ViewCollection.Add(_view);
			_view.OptionsBehavior.Editable = false;
			_view.OptionsBehavior.ReadOnly = true;
			_view.OptionsBehavior.AllowPixelScrolling = DevExpress.Utils.DefaultBoolean.False;
			_view.OptionsView.ShowGroupPanel = false;
			_view.OptionsView.ShowColumnHeaders = true;
			_view.OptionsView.ShowIndicator = false;
			_view.OptionsView.ColumnAutoWidth = true;
			_view.OptionsView.ShowHorizontalLines = DevExpress.Utils.DefaultBoolean.False;
			_view.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
			_view.OptionsSelection.EnableAppearanceFocusedCell = false;
			_view.OptionsCustomization.AllowColumnMoving = false;
			_view.OptionsCustomization.AllowSort = false;
			_view.OptionsCustomization.AllowFilter = false;
			_view.RowHeight = LogoRowHeight;
			// Global display density is reapplied after construction; logo rows still need room for their images.
			_view.CalcRowHeight += (sender, args) => args.RowHeight = Math.Max(LogoRowHeight, args.RowHeight);
			_view.OptionsDetail.EnableMasterViewMode = true;
			_view.OptionsDetail.DetailMode = DetailMode.Embedded;
			_view.OptionsDetail.ShowDetailTabs = false;
			_view.OptionsDetail.ShowEmbeddedDetailIndent = DevExpress.Utils.DefaultBoolean.True;
			_view.OptionsView.ShowDetailButtons = true;
			var logoEditor = new RepositoryItemPictureEdit { SizeMode = PictureSizeMode.Zoom, NullText = String.Empty, ShowMenu = false };
			_grid.RepositoryItems.Add(logoEditor);
			var logoColumn = _view.Columns.AddVisible(nameof(GameRow.Logo), String.Empty);
			logoColumn.ColumnEdit = logoEditor;
			logoColumn.Caption = " ";
			logoColumn.OptionsColumn.FixedWidth = true;
			logoColumn.Width = 144;
			var nameColumn = _view.Columns.AddVisible(nameof(GameRow.Name), L("GameDiscovery.GameColumn", "Game"));
			nameColumn.MinWidth = 140;
			nameColumn.AppearanceCell.FontStyleDelta = FontStyle.Bold;
			var activityColumn = _view.Columns.AddVisible(nameof(GameRow.LastManaged), L("GameDiscovery.LastManaged", "Last managed"));
			activityColumn.OptionsColumn.FixedWidth = true;
			activityColumn.Width = 145;
			activityColumn.ToolTip = L("GameDiscovery.LastManagedHint", "Last successful game-mode entry or game launch through NMM.");
			// DevExpress also asks with InvalidRowHandle to decide whether detail buttons exist at all.
			_view.MasterRowGetRelationCount += (sender, args) => args.RelationCount = args.RowHandle == GridControl.InvalidRowHandle
				? (_visibleCollection == GameDiscoveryCollection.Managed ? 1 : 0)
				: ((_view.GetRow(args.RowHandle) as GameRow)?.Entry.Collection == GameDiscoveryCollection.Managed ? 1 : 0);
			_view.MasterRowEmpty += (sender, args) => args.IsEmpty = false;
			_view.MasterRowGetRelationName += (sender, args) => args.RelationName = "Overview";
			_view.MasterRowGetChildList += (sender, args) => args.ChildList = (_view.GetRow(args.RowHandle) as GameRow)?.Details;
			_view.MasterRowExpanded += (sender, args) => StatisticsRequested?.Invoke(this, EventArgs.Empty);
			var statisticsView = new GridView(_grid);
			_grid.ViewCollection.Add(statisticsView);
			_grid.LevelTree.Nodes.Add("Overview", statisticsView);
			statisticsView.OptionsBehavior.Editable = false;
			statisticsView.OptionsBehavior.ReadOnly = true;
			statisticsView.OptionsView.ShowGroupPanel = false;
			statisticsView.OptionsView.ShowIndicator = false;
			statisticsView.OptionsSelection.EnableAppearanceFocusedCell = false;
			statisticsView.OptionsCustomization.AllowSort = false;
			statisticsView.OptionsCustomization.AllowFilter = false;
			statisticsView.OptionsCustomization.AllowColumnMoving = false;
			statisticsView.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
			statisticsView.OptionsView.RowAutoHeight = true;
			var detailText = new RepositoryItemMemoEdit();
			_grid.RepositoryItems.Add(detailText);
			statisticsView.Columns.AddVisible(nameof(StatisticRow.Content), L("GameDiscovery.Content", "Content")).ColumnEdit = detailText;
			var activeColumn = statisticsView.Columns.AddVisible(nameof(StatisticRow.Active), L("GameDiscovery.ActiveApplied", "Active / applied"));
			activeColumn.ColumnEdit = detailText;
			activeColumn.ToolTip = L("GameDiscovery.AppliedCollectionsHint", "Collections show verified applied associations; modified or incomplete associations remain in the managed total.");
			statisticsView.Columns.AddVisible(nameof(StatisticRow.Managed), L("GameDiscovery.Managed", "Managed"));
			_view.FocusedRowChanged += (sender, args) => SelectGridRow();
			_view.DoubleClick += (sender, args) =>
			{
				var position = _grid.PointToClient(Control.MousePosition);
				var hit = _view.CalcHitInfo(position);
				if (hit.InRowCell) InvokePrimaryAction();
			};
			listBody.Controls.Add(_grid);
			_emptyList = new LabelControl
			{
				Dock = DockStyle.Fill,
				Text = L("GameDiscovery.EmptySection", "No games in this section."),
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				Visible = false,
				Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center, VAlignment = DevExpress.Utils.VertAlignment.Center } }
			};
			listBody.Controls.Add(_emptyList);

			var detailCard = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.Simple, Padding = new Padding(18, 15, 18, 12) };
			mainArea.Controls.Add(detailCard, 2, 0);
			_detailLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 14 };
			var card = _detailLayout;
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
			card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
			detailCard.Controls.Add(card);

			// The old PictureEdit produced an unthemed white rectangle on some skins.
			// Use the game name itself as the card heading until real game icons exist.
			_gameTitle = new LabelControl
			{
				Text = L("GameDiscovery.SelectGameTitle", "Select a game"),
				Dock = DockStyle.Fill,
				UseMnemonic = false,
				Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 16f, FontStyle.Bold), TextOptions = { VAlignment = DevExpress.Utils.VertAlignment.Center } }
			};
			card.Controls.Add(_gameTitle, 0, 0);
			_gameState = new LabelControl { Dock = DockStyle.Fill, UseMnemonic = false };
			card.Controls.Add(_gameState, 0, 1);
			_sourceLine = new LabelControl { Dock = DockStyle.Fill, UseMnemonic = false };
			card.Controls.Add(_sourceLine, 0, 2);

			_pathTitle = Caption(L("GameDiscovery.InstallationHeading", "INSTALLATION PATH"));
			card.Controls.Add(_pathTitle, 0, 3);
			_installationPath = ReadOnlyPathEdit();
			card.Controls.Add(_installationPath, 0, 4);
			_startupTitle = Caption(L("GameDiscovery.StartupPreference", "STARTUP PREFERENCE"));
			card.Controls.Add(_startupTitle, 0, 5);
			_startupValue = new LabelControl { Dock = DockStyle.Fill, UseMnemonic = false };
			card.Controls.Add(_startupValue, 0, 6);
			_startupDefault = new SimpleButton { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), Visible = false };
			_startupDefault.Click += (sender, args) =>
			{
				var selected = SelectedEntry;
				if (_startupDefault.Enabled && selected != null && selected.Collection == GameDiscoveryCollection.Managed)
					StartupDefaultRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(selected.ModeId));
			};
			card.Controls.Add(_startupDefault, 0, 7);

			_primaryAction = new SimpleButton
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 7, 0, 5),
				Text = L("GameDiscovery.SelectGameTitle", "Select a game"),
				Enabled = false,
				Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold) }
			};
			NmmIconProvider.BindDialogButton(_primaryAction, NmmIconAction.ChangeGame);
			_primaryAction.Click += (sender, args) => InvokePrimaryAction();
			card.Controls.Add(_primaryAction, 0, 8);
			_locate = new SimpleButton { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 5), Visible = false, Text = L("GameDiscovery.Locate", "Locate game...") };
			NmmIconProvider.BindDialogButton(_locate, NmmIconAction.Browse);
			_locate.Click += (sender, args) =>
			{
				if (!_locate.Enabled || SelectedEntry == null) return;
				// A Managed game may be based on legacy folders without persisted
				// Game Storage identity. Review setup without entering its session.
				if (SelectedEntry.Collection == GameDiscoveryCollection.Managed &&
					!String.IsNullOrWhiteSpace(_selectedInstallationRoot))
					ConfigureRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(SelectedEntry.ModeId, _selectedInstallationRoot));
				else
					LocateRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(SelectedEntry.ModeId));
			};
			card.Controls.Add(_locate, 0, 9);

			_candidatePanel = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.NoBorder, Visible = false };
			var candidateLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
			candidateLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
			candidateLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			_candidateHint = new LabelControl { Dock = DockStyle.Fill, UseMnemonic = false, AutoSizeMode = LabelAutoSizeMode.Vertical };
			candidateLayout.Controls.Add(_candidateHint, 0, 0);
			_installations = new ComboBoxEdit { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 8), Enabled = false };
			_installations.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_installations.Properties.NullValuePrompt = L("GameDiscovery.ChooseInstallation", "Select installation...");
			_installations.SelectedIndexChanged += (sender, args) =>
			{
				if (_updatingInstallations) return;
				_selectedInstallationRoot = (_installations.SelectedItem as CandidateChoice)?.Candidate.GameRoot;
				UpdateDetails();
				UpdateActions();
			};
			candidateLayout.Controls.Add(_installations, 0, 1);
			_candidatePanel.Controls.Add(candidateLayout);
			card.Controls.Add(_candidatePanel, 0, 10);

			var evidencePanel = new PanelControl { Dock = DockStyle.Fill, BorderStyle = BorderStyles.NoBorder };
			var evidenceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
			evidenceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
			evidenceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			evidenceLayout.Controls.Add(Caption(L("GameDiscovery.EvidenceTitle", "DISCOVERY DETAILS")), 0, 0);
			// Long paths remain scrollable, but do not stretch a two-line description
			// into a large blank editor filling the entire details card.
			_details = new MemoEdit { Dock = DockStyle.Top, Height = 118 };
			_details.Properties.ReadOnly = true;
			_details.Properties.ScrollBars = ScrollBars.Vertical;
			_details.Properties.BorderStyle = BorderStyles.NoBorder;
			_details.Text = L("GameDiscovery.DetailsHint", "Select a game to see its discovery details.");
			evidenceLayout.Controls.Add(_details, 0, 1);
			evidencePanel.Controls.Add(evidenceLayout);
			card.Controls.Add(evidencePanel, 0, 12);

			// Span the selectors and catalog together, with matching outside edges.
			// The picture receives the remaining height beneath the active-game heading.
			_activeCard = new PanelControl
			{
				Dock = DockStyle.Fill,
				Margin = new Padding(0, categories.Margin.Top + categories.Padding.Top, 16, mainArea.Margin.Bottom + detailCard.Margin.Bottom),
				BorderStyle = BorderStyles.Simple,
				Visible = false
			};
			_activeFooter = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 5,
				Padding = new Padding(12, 12, 12, 12),
				Margin = Padding.Empty
			};
			_activeFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			_activeFooter.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
			_activeFooter.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			_activeFooter.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
			_activeFooter.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
			_activeFooter.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
			_activeCard.Controls.Add(_activeFooter);
			_canvas.Controls.Add(_activeCard, 0, 2);
			_canvas.SetRowSpan(_activeCard, 2);

			_activeGameName = new LabelControl
			{
				Dock = DockStyle.Fill,
				UseMnemonic = false,
				AutoSizeMode = LabelAutoSizeMode.Vertical,
				Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 12f, FontStyle.Bold),
					TextOptions = { VAlignment = DevExpress.Utils.VertAlignment.Center } }
			};
			_activeFooter.Controls.Add(_activeGameName, 0, 0);

			_activeArtwork = new PictureEdit { Dock = DockStyle.Fill, Margin = Padding.Empty, Cursor = Cursors.Hand };
			_activeArtwork.Properties.ShowMenu = false;
			_activeArtwork.Properties.BorderStyle = BorderStyles.NoBorder;
			_activeArtwork.Properties.SizeMode = PictureSizeMode.Zoom;
			_activeArtwork.SizeChanged += (sender, args) => RenderActiveArtwork(false);
			_activeArtwork.ToolTip = L("GameDiscovery.PortraitImageHint", "Game cover. Click to change; portrait images around 640 x 960 pixels work best. Images are cropped to fill the panel without stretching.");
			_activeArtwork.Click += (sender, args) => ChooseActiveArtwork();
			_activeFooter.Controls.Add(_activeArtwork, 0, 1);

			_changeArtwork = new SimpleButton
			{
				Text = L("GameDiscovery.ChangeImage", "Change image..."),
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 4, 0, 0)
			};
			_changeArtwork.Click += (sender, args) => ChooseActiveArtwork();
			_activeFooter.Controls.Add(_changeArtwork, 0, 3);
			_resetArtwork = new SimpleButton
			{
				Text = L("GameDiscovery.ResetImage", "Reset image"),
				Dock = DockStyle.Fill,
				Margin = new Padding(0, 4, 0, 0)
			};
			_resetArtwork.Click += (sender, args) => ResetActiveArtwork();
			_activeFooter.Controls.Add(_resetArtwork, 0, 4);
			// Match accumulated child insets again after DPI/font scaling changes their pixel sizes.
			_canvas.Layout += (sender, args) =>
			{
				var inset = new Padding(_activeCard.Margin.Left, categories.Margin.Top + categories.Padding.Top,
					_activeCard.Margin.Right, mainArea.Margin.Bottom + detailCard.Margin.Bottom);
				if (_activeCard.Margin != inset) _activeCard.Margin = inset;
			};

			// Loading progress and operational messages remain below the catalog,
			// but do not repeat the active-game heading.
			_statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
			_statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
			_statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			_loadingRing = new CompletionRing { Dock = DockStyle.Left, Width = 34, Height = 34, Visible = false };
			_statusLayout.Controls.Add(_loadingRing, 0, 0);
			_status = new LabelControl { Dock = DockStyle.Fill, UseMnemonic = false, AutoSizeMode = LabelAutoSizeMode.Vertical,
				Appearance = { TextOptions = { VAlignment = DevExpress.Utils.VertAlignment.Center } } };
			_statusLayout.Controls.Add(_status, 1, 0);
			_canvas.Controls.Add(_statusLayout, 1, 4);
			UpdateActiveBanner();
			SetStatus(L("GameDiscovery.Loading", "Checking supported games..."));
			ResizeCanvas();
			UpdateCollectionButtons(new int[3]);
			UpdateDetails();
			UpdateActions();
		}

		private void UpdateActiveBanner()
		{
			bool active = !String.IsNullOrWhiteSpace(_activeModeId);
			_canvas.ColumnStyles[0].Width = active ? 320 : 0;
			_activeCard.Visible = active;
			_activeArtwork.Visible = active;
			_changeArtwork.Visible = active && _artwork != null;
			_resetArtwork.Visible = active && _artwork != null && _artwork.HasOverride(_activeModeId);
			_activeFooter.RowStyles[3].Height = _changeArtwork.Visible ? 40 : 0;
			_activeFooter.RowStyles[4].Height = _resetArtwork.Visible ? 36 : 0;
			UpdateActiveBannerCaption();
			// An already displayed "Managing ..." status is the same message as
			// the heading, so show it in the cover card only.
			SetStatus(_status.Text);
			ResizeCanvas();
			if (!active) { ReleaseActiveArtwork(); return; }

			Image artwork = null;
			try { artwork = _artwork?.Load(_activeModeId); }
			catch (ArgumentException) { /* A legacy mode with an invalid ID cannot own artwork. */ }
			SetActiveArtwork(artwork);
		}

		private void UpdateActiveBannerCaption()
		{
			if (String.IsNullOrWhiteSpace(_activeModeId))
			{
				_activeGameName.Text = String.Empty;
				return;
			}
			var entry = _catalog?.Entries.FirstOrDefault(item =>
				String.Equals(item.ModeId, _activeModeId, StringComparison.OrdinalIgnoreCase));
			_activeGameName.Text = String.Format(L("GameDiscovery.ActiveGame", "Managing {0}"),
				entry?.Name ?? _activeModeId);
		}

		private void ChooseActiveArtwork()
		{
			if (String.IsNullOrWhiteSpace(_activeModeId) || _artwork == null) return;
			using (var picker = new OpenFileDialog
			{
				Title = L("GameDiscovery.ChoosePortraitImage", "Choose a game cover (portrait recommended, 640 x 960)"),
				Filter = L("GameDiscovery.ImageFilter", "Pictures (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"),
				CheckFileExists = true,
				Multiselect = false
			})
			{
				if (picker.ShowDialog(this) != DialogResult.OK) return;
				try
				{
					_artwork.Import(_activeModeId, picker.FileName);
					UpdateActiveBanner();
				}
				catch (Exception error) when (error is System.IO.IOException || error is ArgumentException ||
					error is UnauthorizedAccessException || error is System.Runtime.InteropServices.ExternalException)
				{
					XtraMessageBox.Show(this, error.Message, L("GameDiscovery.ImageError", "Cannot use this picture"),
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		private void ResetActiveArtwork()
		{
			if (_artwork == null || String.IsNullOrWhiteSpace(_activeModeId)) return;
			try { _artwork.ClearOverride(_activeModeId); UpdateActiveBanner(); }
			catch (Exception error) when (error is System.IO.IOException || error is UnauthorizedAccessException)
			{
				XtraMessageBox.Show(this, error.Message, L("GameDiscovery.ImageError", "Cannot use this picture"),
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		/// <summary>Retains the source separately so resizing never repeatedly crops an already cropped display image.</summary>
		private void SetActiveArtwork(Image artwork)
		{
			Image previous = _activeArtworkSource;
			_activeArtworkSource = artwork;
			RenderActiveArtwork(true);
			if (!Object.ReferenceEquals(previous, artwork)) previous?.Dispose();
		}

		/// <summary>Rebuilds a cover for the actual picture area, including when the optional Reset button changes its height.</summary>
		private void RenderActiveArtwork(bool force)
		{
			if (IsDisposed || Disposing || _activeArtwork.IsDisposed || _activeArtwork.Disposing || String.IsNullOrWhiteSpace(_activeModeId)) return;
			Size size = _activeArtwork.ClientSize;
			if (size.Width < 1 || size.Height < 1 || (!force && _activeArtwork.Image != null && _activeArtwork.Image.Size == size)) return;
			Image picture = _activeArtworkSource == null ? CreateDefaultArtwork(size) : CropArtwork(_activeArtworkSource, size);
			Image previous = _activeArtwork.Image;
			_activeArtwork.Image = picture;
			previous?.Dispose();
		}

		/// <summary>Releases both the retained source and its independently rendered preview.</summary>
		private void ReleaseActiveArtwork()
		{
			Image previous = _activeArtwork.Image;
			_activeArtwork.Image = null;
			previous?.Dispose();
			_activeArtworkSource?.Dispose();
			_activeArtworkSource = null;
		}

		/// <summary>Centre-crops a custom cover to fill its current bounds while preserving the source's proportions.</summary>
		private static Image CropArtwork(Image source, Size size)
		{
			float ratio = size.Width / (float)size.Height;
			float width = source.Width;
			float height = source.Height;
			if (width / height > ratio) width = height * ratio;
			else height = width / ratio;
			var picture = new Bitmap(size.Width, size.Height);
			try
			{
				using (Graphics graphics = Graphics.FromImage(picture))
				{
					graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
					graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
					graphics.DrawImage(source, new RectangleF(0, 0, size.Width, size.Height),
						new RectangleF((source.Width - width) / 2f, (source.Height - height) / 2f, width, height), GraphicsUnit.Pixel);
				}
				return picture;
			}
			catch { picture.Dispose(); throw; }
		}

		/// <summary>Fits the bundled default cover without cropping its instructions when the panel changes proportions.</summary>
		private static Image CreateDefaultArtwork(Size size)
		{
			var picture = new Bitmap(size.Width, size.Height);
			try
			{
				using (System.IO.Stream stream = typeof(GameDiscoveryDocument).Assembly.GetManifestResourceStream("Nexus.Client.GameDiscoveryDefault.png"))
				using (Image source = Image.FromStream(stream))
				using (Graphics graphics = Graphics.FromImage(picture))
				{
					graphics.Clear(Color.FromArgb(16, 22, 29));
					graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
					graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
					float scale = Math.Min(size.Width / (float)source.Width, size.Height / (float)source.Height);
					float width = source.Width * scale;
					float height = source.Height * scale;
					graphics.DrawImage(source, new RectangleF((size.Width - width) / 2f, (size.Height - height) / 2f, width, height));
				}
				return picture;
			}
			catch { picture.Dispose(); throw; }
		}

		/// <summary>Releases owned preview images when the discovery document is disposed.</summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing && _activeArtwork != null) ReleaseActiveArtwork();
			if (disposing)
				foreach (Image image in _logos.Values) image?.Dispose();
			base.Dispose(disposing);
		}

		private static LabelControl Caption(string text)
		{
			return new LabelControl
			{
				Text = text,
				Dock = DockStyle.Fill,
				UseMnemonic = false,
				Appearance = { Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f, FontStyle.Bold) }
			};
		}

		private static TextEdit ReadOnlyPathEdit()
		{
			var edit = new TextEdit { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6) };
			edit.Properties.ReadOnly = true;
			edit.Properties.BorderStyle = BorderStyles.NoBorder;
			return edit;
		}

		private void ResizeCanvas()
		{
			// Purposefully bound content width; the old grid wasted an entire wide monitor.
			// Allow vertical scrolling in small windows rather than clipping the setup controls.
			if (_viewport.IsDisposed || _canvas.IsDisposed) return;
			bool active = !String.IsNullOrWhiteSpace(_activeModeId);
			int width = Math.Max(active ? 1120 : 760, _viewport.ClientSize.Width - 20);
			int height = Math.Max(1, _viewport.ClientSize.Height - 8);
			_canvas.Width = Math.Min(active ? 1640 : 1180, width);
			_canvas.Height = Math.Max(770, Math.Min(855, height));
			_canvas.Left = Math.Max(0, (_viewport.ClientSize.Width - _canvas.Width) / 2);
			_canvas.Top = 0;
		}

		private static string L(string key, string fallback) => LanguageManager.Get(key, fallback);

		private GameDiscoveryEntry SelectedEntry => _catalog?.Entries.FirstOrDefault(item =>
			String.Equals(item.ModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase));

		internal void EnableFirstSessionActions()
		{
			_sessionActionsAvailable = true;
			UpdateActions();
		}

		internal void ConfigureArtwork(GameDiscoveryArtwork artwork)
		{
			_artwork = artwork ?? throw new ArgumentNullException(nameof(artwork));
			UpdateActiveBanner();
		}

		/// <summary>Supplies read-only row assets and cached statistics without constructing game modes.</summary>
		internal void ConfigureRows(Func<string, Icon> logoProvider, Func<GameDiscoveryEntry, GameDiscoveryStatistics> statisticsProvider)
		{
			_logoProvider = logoProvider;
			_statisticsProvider = statisticsProvider;
		}

		/// <summary>Refreshes expanded details without rebinding the master list or losing selection.</summary>
		internal void RefreshStatistics(string modeId)
		{
			var rows = _grid.DataSource as List<GameRow>;
			GameRow row = rows?.FirstOrDefault(item => String.Equals(item.Entry.ModeId, modeId, StringComparison.OrdinalIgnoreCase));
			if (row != null) UpdateStatistics(row);
		}

		/// <summary>Clones shared definition icons once per game and releases the owned images with the document.</summary>
		private Image GetLogo(string modeId)
		{
			Image image;
			if (_logos.TryGetValue(modeId, out image)) return image;
			try
			{
				Icon source = _logoProvider?.Invoke(modeId) ?? SystemIcons.Application;
				using (Icon icon = new Icon(source, 96, 96))
				using (Bitmap logo = icon.ToBitmap())
				{
					// Older transparent wordmarks use dark lettering. An image-local matte
					// keeps the original branding readable on both light and dark skins.
					image = new Bitmap(112, 112);
					using (Graphics graphics = Graphics.FromImage(image))
					{
						graphics.Clear(Color.White);
						graphics.DrawImage(logo, new Rectangle(8, 8, 96, 96));
					}
				}
			}
			catch (Exception error) when (error is ArgumentException || error is System.IO.IOException ||
				error is UnauthorizedAccessException || error is System.Runtime.InteropServices.ExternalException)
			{
				System.Diagnostics.Trace.TraceWarning("Could not load game logo {0}: {1}", modeId, error.Message);
				image = SystemIcons.Application.ToBitmap();
			}
			_logos[modeId] = image;
			return image;
		}

		/// <summary>Formats current or cached counts, preserving unknown and unsupported values.</summary>
		private void UpdateStatistics(GameRow row)
		{
			row.Details.RaiseListChangedEvents = false;
			row.Details.Clear();
			GameDiscoveryStatistics snapshot = _statisticsProvider?.Invoke(row.Entry);
			if (snapshot == null)
				row.Details.Add(new StatisticRow(L("GameDiscovery.SummaryUnavailable", "Manage this game to collect info"), String.Empty, String.Empty));
			else
			{
				row.Details.Add(new StatisticRow(L("MainForm.Tabs.Mods", "Mods"), Count(snapshot.ActiveMods), Count(snapshot.ManagedMods)));
				row.Details.Add(new StatisticRow(L("MainForm.Tabs.Collections", "Collections"), Count(snapshot.AppliedCollections), Count(snapshot.ManagedCollections)));
				if (snapshot.ManagedPlugins.HasValue)
					row.Details.Add(new StatisticRow(L("MainForm.Tabs.Plugins", "Plugins"), Count(snapshot.ActivePlugins), Count(snapshot.ManagedPlugins)));
				if (!String.IsNullOrWhiteSpace(snapshot.ProfileName))
					row.Details.Add(new StatisticRow(L("GameDiscovery.Profile", "Profile"), snapshot.ProfileName, String.Empty));
				row.Details.Add(new StatisticRow(L("GameDiscovery.Snapshot", "Last updated"), snapshot.UpdatedUtc.ToLocalTime().ToString("g"), String.Empty));
			}
			row.Details.RaiseListChangedEvents = true;
			row.Details.ResetBindings();
		}

		/// <summary>Displays unknown counts separately from known zero counts.</summary>
		private static string Count(int? value) => value.HasValue ? value.Value.ToString("N0") : L("GameDiscovery.UnknownCount", "Unavailable");

		internal void SetActiveManagedGame(string modeId)
		{
			_activeModeId = modeId;
			UpdateActiveBanner();
		}

		internal void SetSessionReady() => UpdateActions();
		internal void SetSessionInactive()
		{
			_activeModeId = null;
			UpdateActiveBanner();
			UpdateActions();
		}

		internal void SetSessionLoading(string name, bool loading)
		{
			_sessionLoading = loading;
			_loadingRing.Visible = loading;
			_statusLayout.ColumnStyles[0].Width = loading ? 42 : 0;
			if (loading) _loadingRing.SetProgress(0, 18);
			UpdateActions();
			UpdateDiscoveryButtons();
			if (loading) SetStatus(String.Format(L("GameDiscovery.LoadingGame", "Loading {0}..."), name));
			else SetStatus(_status.Text); // Collapse the duplicate ready message after loading ends.
		}

		internal void SetSessionProgress(long completed, long maximum, string phase)
		{
			if (!_sessionLoading) return;
			_loadingRing.SetProgress(completed, maximum);
			SetStatus(String.Format(L("GameDiscovery.LoadProgressPhase", "Initializing game: {0}/{1} - {2}"),
				Math.Max(0L, completed), Math.Max(1L, maximum), String.IsNullOrWhiteSpace(phase)
					? L("GameDiscovery.Phase.Settings", "Preparing game settings") : phase));
		}

		internal void SetSessionMessage(string message) => SetStatus(message);

		internal void SetBusy(bool busy)
		{
			_refreshing = busy;
			UpdateDiscoveryButtons();
			UpdateActions();
			SetStatus(busy ? L("GameDiscovery.Refreshing", "Refreshing detected games...") : String.Empty);
		}

		internal void SetScanning(bool scanning)
		{
			_scanning = scanning;
			UpdateDiscoveryButtons();
			UpdateActions();
			if (scanning) SetStatus(L("GameDiscovery.Scanning", "Scanning fixed drives for game executables..."));
		}

		internal void SetScanProgress(string path)
		{
			if (_scanning && !String.IsNullOrWhiteSpace(path))
				SetStatus(L("GameDiscovery.ScanningPath", "Scanning:") + " " + path);
		}

		internal void SetScanStopping()
		{
			_stopScan.Enabled = false;
			SetStatus(L("GameDiscovery.ScanStopping", "Stopping scan; confirmed results will be kept..."));
		}

		internal void SetScanResult(int foundCount, bool cancelled)
		{
			SetStatus(String.Format(cancelled
				? L("GameDiscovery.ScanStopped", "Scan stopped. {0} verified installation(s) retained.")
				: L("GameDiscovery.ScanDone", "Scan complete. {0} verified installation(s) found."), foundCount));
		}

		internal void SetError(string reason)
		{
			UpdateDiscoveryButtons();
			SetStatus(L("GameDiscovery.Error", "Could not refresh game discovery:") + " " + reason);
		}

		internal void SetScanError(string reason) => SetStatus(L("GameDiscovery.ScanError", "Game scan failed:") + " " + reason);

		internal void SetRefreshFinished()
		{
			_refreshing = false;
			UpdateDiscoveryButtons();
			UpdateActions();
		}

		private void SetStatus(string message)
		{
			string status = message ?? String.Empty;
			if (!_sessionLoading && !String.IsNullOrWhiteSpace(_activeModeId))
			{
				var entry = _catalog?.Entries.FirstOrDefault(item =>
					String.Equals(item.ModeId, _activeModeId, StringComparison.OrdinalIgnoreCase));
				string name = entry?.Name ?? _activeModeId;
				// The ready notification used to appear beneath the game list while
				// the new artwork panel repeated it. Keep exactly one "Managing ...".
				if (String.Equals(status, String.Format(L("GameDiscovery.GameReady", "Managing {0}"), name),
					StringComparison.CurrentCulture) || String.Equals(status, _activeGameName.Text, StringComparison.CurrentCulture))
					status = String.Empty;
			}
			_status.Text = status;
			_status.ToolTip = status;
		}

		private void UpdateDiscoveryButtons()
		{
			_refresh.Enabled = !_refreshing && !_scanning && !_sessionLoading;
			_scan.Enabled = _catalog != null && !_refreshing && !_scanning && !_sessionLoading;
			_scan.Visible = !_scanning;
			_stopScan.Visible = _scanning;
			_stopScan.Enabled = _scanning;
		}

		internal void SetCatalog(GameDiscoveryCatalog catalog)
		{
			_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			Populate();
			UpdateDiscoveryButtons();
		}

		private void SelectCollection(GameDiscoveryCollection section)
		{
			if (_visibleCollection == section) return;
			_visibleCollection = section;
			_selectedModeId = null;
			_selectedInstallationRoot = null;
			Populate();
		}

		private void Populate()
		{
			if (_catalog == null) return;
			string query = Convert.ToString(_search.EditValue)?.Trim() ?? String.Empty;
			var filtered = _catalog.Entries.Where(item => query.Length == 0 ||
				item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
				item.ModeId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			var counts = new int[3];
			for (int i = 0; i < _collections.Length; i++)
				counts[i] = filtered.Count(item => item.Collection == _collections[i]);

			// Keep the external game selection when a configuration moves sections.
			var previous = filtered.FirstOrDefault(item => String.Equals(item.ModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase));
			if (previous != null) _visibleCollection = previous.Collection;
			var rows = filtered.Where(item => item.Collection == _visibleCollection).Select(item => new GameRow(item, GetLogo(item.ModeId))).ToList();
			foreach (GameRow row in rows.Where(item => item.Entry.Collection == GameDiscoveryCollection.Managed)) UpdateStatistics(row);
			int focusIndex = rows.FindIndex(row => String.Equals(row.Entry.ModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase));
			if (focusIndex < 0)
			{
				focusIndex = rows.Count > 0 ? 0 : -1;
				_selectedModeId = focusIndex < 0 ? null : rows[0].Entry.ModeId;
				_selectedInstallationRoot = null;
			}
			_repopulating = true;
			try
			{
				_view.Columns[nameof(GameRow.LastManaged)].Visible = _visibleCollection == GameDiscoveryCollection.Managed;
				_grid.DataSource = rows;
				_view.FocusedRowHandle = focusIndex < 0 ? GridControl.InvalidRowHandle : focusIndex;
				_grid.Visible = rows.Count > 0;
				_emptyList.Visible = rows.Count == 0;
				_emptyList.Text = query.Length > 0
					? L("GameDiscovery.NoSearchMatches", "No games match your search in this section.")
					: L("GameDiscovery.EmptySection", "No games in this section.");
			}
			finally { _repopulating = false; }
			UpdateCollectionButtons(counts);
			UpdateActiveBannerCaption();
			UpdateInstallationChoices();
			UpdateDetails();
			UpdateActions();
		}

		private void UpdateCollectionButtons(int[] counts)
		{
			for (int index = 0; index < _collections.Length; index++)
			{
				string title = _collections[index] == GameDiscoveryCollection.Managed
					? L("GameDiscovery.Managed", "Managed")
					: _collections[index] == GameDiscoveryCollection.ToConfigure
						? L("GameDiscovery.ToConfigure", "To configure")
						: L("GameDiscovery.Supported", "Supported");
				bool selected = _visibleCollection == _collections[index];
				_collectionTitles[index].Text = (selected ? "●  " : "○  ") + title;
				_collectionCounts[index].Text = counts[index].ToString();
				_collectionCards[index].BorderStyle = selected ? BorderStyles.Simple : BorderStyles.NoBorder;
			}
			_listCard.Text = _visibleCollection == GameDiscoveryCollection.Managed
				? L("GameDiscovery.ManagedGames", "Managed games")
				: _visibleCollection == GameDiscoveryCollection.ToConfigure
					? L("GameDiscovery.ToConfigure", "To configure")
					: L("GameDiscovery.SupportedGames", "Supported games");
		}

		private void SelectGridRow()
		{
			if (_repopulating) return;
			var row = _view.GetFocusedRow() as GameRow;
			string nextModeId = row?.Entry.ModeId;
			if (String.Equals(nextModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase)) return;
			_selectedModeId = nextModeId;
			_selectedInstallationRoot = null;
			UpdateInstallationChoices();
			UpdateDetails();
			UpdateActions();
		}

		private void InvokePrimaryAction()
		{
			if (!_primaryAction.Enabled) return;
			var entry = SelectedEntry;
			if (entry == null) return;
			if (GameSessionEntryGate.CanManage(entry))
				ManageRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(entry.ModeId));
			else if (CanConfigure(entry) && !String.IsNullOrWhiteSpace(_selectedInstallationRoot))
				ConfigureRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(entry.ModeId, _selectedInstallationRoot));
			else if (CanLocate(entry))
				LocateRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(entry.ModeId));
		}

		private static bool CanConfigure(GameDiscoveryEntry entry)
		{
			// Managed denotes historical configuration, not proof of persisted
			// Game Storage identity. Its setup must remain explicitly reviewable.
			return entry != null && (entry.Collection == GameDiscoveryCollection.ToConfigure ||
				entry.Collection == GameDiscoveryCollection.Managed);
		}

		private static bool CanLocate(GameDiscoveryEntry entry)
		{
			return entry != null && (entry.Collection != GameDiscoveryCollection.Managed || entry.NeedsAttention);
		}

		private void UpdateInstallationChoices()
		{
			GameDiscoveryEntry entry = SelectedEntry;
			_updatingInstallations = true;
			try
			{
				_installations.Properties.Items.Clear();
				if (entry != null)
					foreach (var candidate in entry.Candidates)
						_installations.Properties.Items.Add(new CandidateChoice(candidate));
				int index = -1;
				if (!String.IsNullOrWhiteSpace(_selectedInstallationRoot))
					for (int i = 0; i < _installations.Properties.Items.Count; i++)
						if (String.Equals(((_installations.Properties.Items[i] as CandidateChoice)?.Candidate.GameRoot),
							_selectedInstallationRoot, StringComparison.OrdinalIgnoreCase))
						{ index = i; break; }
				// Multiple installations must never be silently selected for the user.
				if (index < 0 && _installations.Properties.Items.Count == 1) index = 0;
				_installations.SelectedIndex = index;
				_selectedInstallationRoot = (_installations.SelectedItem as CandidateChoice)?.Candidate.GameRoot;
			}
			finally { _updatingInstallations = false; }
		}

		private void UpdateDetails()
		{
			var entry = SelectedEntry;
			if (entry == null)
			{
				_gameTitle.Text = L("GameDiscovery.SelectGameTitle", "Select a game");
				_gameState.Text = L("GameDiscovery.SelectHint", "Choose a game from the list to view its configuration.");
				_sourceLine.Text = String.Empty;
				_installationPath.Text = String.Empty;
				_startupValue.Text = String.Empty;
				_candidateHint.Text = String.Empty;
				_details.Text = _catalog != null && _catalog.UnavailableSavedModeIds.Count > 0
					? L("GameDiscovery.UnavailableSaved", "Saved game modes unavailable in this build:") + " " + String.Join(", ", _catalog.UnavailableSavedModeIds)
					: L("GameDiscovery.DetailsHint", "Select a game to see its discovery details.");
				return;
			}
			_gameTitle.Text = entry.Name;
			_gameState.Text = GetStatus(entry);
			_sourceLine.Text = entry.Candidates.Count == 0
				? L("GameDiscovery.NoDetectionSource", "No installation detected")
				: L("GameDiscovery.DetectedVia", "Found via") + ": " + String.Join(", ", entry.Candidates.Select(item => item.Source)
					.Distinct(StringComparer.OrdinalIgnoreCase));
			_pathTitle.Text = entry.Collection == GameDiscoveryCollection.Managed
				? L("GameDiscovery.InstallationHeading", "INSTALLATION PATH")
				: L("GameDiscovery.DetectedInstallPath", "DETECTED INSTALLATION");
			string installPath = entry.Collection == GameDiscoveryCollection.Managed
				? entry.SavedInstallationPath
				: _selectedInstallationRoot;
			_installationPath.Text = String.IsNullOrWhiteSpace(installPath)
				? L("GameDiscovery.NotSelected", "Not selected")
				: installPath;
			_installationPath.ToolTip = _installationPath.Text;
			_startupValue.Text = entry.IsStartupDefault
				? "★  " + L("GameDiscovery.Default", "Remembered startup game")
				: L("GameDiscovery.NotStartupDefault", "Not the default game");
			_candidateHint.Text = entry.Candidates.Count > 1
				? L("GameDiscovery.MultipleFoundHint", "Multiple verified installations: choose one before configuring.")
				: L("GameDiscovery.OneFoundHint", "One installation was found for this game.");
			var lines = new List<string>();
			if (entry.NeedsAttention)
				lines.Add(L("GameDiscovery.AttentionExplanation", "An existing configuration may need review. Saved settings have not been changed."));
			else if (entry.Collection == GameDiscoveryCollection.Managed)
				lines.Add(L("GameDiscovery.ManagedOverview", "Your existing setup will be preserved."));
			else if (entry.Collection == GameDiscoveryCollection.ToConfigure)
				lines.Add(L("GameDiscovery.UnconfiguredOverview", "Finish configuration before managing mods."));
			else
				lines.Add(L("GameDiscovery.SupportedOverview", "Locate an installation to get started."));
			if (!String.IsNullOrWhiteSpace(entry.SavedExecutablePath))
			{
				lines.Add(String.Empty);
				lines.Add(L("GameDiscovery.SavedExePath", "Saved executable path") + ": " + entry.SavedExecutablePath);
			}
			if (entry.Candidates.Count > 1)
			{
				lines.Add(String.Empty);
				lines.Add(L("GameDiscovery.FoundLocations", "Verified installation locations:"));
				foreach (var candidate in entry.Candidates)
					lines.Add(candidate.Source + ": " + candidate.GameRoot);
			}
			_details.Text = String.Join(Environment.NewLine, lines);
		}

		private string GetStatus(GameDiscoveryEntry entry)
		{
			if (entry.NeedsAttention) return "!  " + L("GameDiscovery.NeedsAttention", "Needs attention");
			switch (entry.Collection)
			{
				case GameDiscoveryCollection.Managed: return "✓  " + L("GameDiscovery.ManagedStatus", "Previously configured");
				case GameDiscoveryCollection.ToConfigure: return "+  " + L("GameDiscovery.NeedsSetup", "Setup required");
				default: return "○  " + L("GameDiscovery.NotLocated", "Not located");
			}
		}

		private void UpdateActions()
		{
			var entry = SelectedEntry;
			bool readyForAction = _sessionActionsAvailable && !_sessionLoading && !_scanning && !_refreshing;
			bool canManage = GameSessionEntryGate.CanManage(entry);
			bool canConfigure = CanConfigure(entry);
			bool canLocate = CanLocate(entry);
			bool hasCandidate = !String.IsNullOrWhiteSpace(_selectedInstallationRoot);
			bool showDefaultAction = entry != null && entry.Collection == GameDiscoveryCollection.Managed;
			_startupTitle.Visible = showDefaultAction;
			_startupValue.Visible = showDefaultAction;
			_startupDefault.Visible = showDefaultAction;
			_detailLayout.RowStyles[5].Height = showDefaultAction ? 24 : 0;
			_detailLayout.RowStyles[6].Height = showDefaultAction ? 28 : 0;
			_detailLayout.RowStyles[7].Height = showDefaultAction ? 40 : 0;
			_startupDefault.Text = entry != null && entry.IsStartupDefault
				? L("GameDiscovery.ClearStartupDefault", "Clear startup default")
				: L("GameDiscovery.SetStartupDefault", "Set as startup game");
			_startupDefault.Enabled = readyForAction && showDefaultAction &&
				(entry.IsStartupDefault || canManage);
			_primaryAction.Text = entry == null ? L("GameDiscovery.SelectGameTitle", "Select a game") :
				canManage ? String.Format(L("GameDiscovery.ManageSpecific", "Manage {0}"), entry.Name) :
				canConfigure && hasCandidate ? L("GameDiscovery.Configure", "Configure game storage") :
				L("GameDiscovery.Locate", "Locate game...");
			_primaryAction.Enabled = readyForAction && entry != null && (canManage || canConfigure && hasCandidate || canLocate);
			bool isManaged = entry != null && entry.Collection == GameDiscoveryCollection.Managed;
			bool reviewManagedStorage = isManaged && hasCandidate;
			_locate.Text = reviewManagedStorage
				? L("GameDiscovery.ReviewStorage", "Review Game Storage...")
				: L("GameDiscovery.Locate", "Locate game...");
			_locate.Visible = reviewManagedStorage || (canConfigure && hasCandidate);
			_locate.Enabled = readyForAction && (reviewManagedStorage || canLocate);
			// Do not crowd ordinary Managed details with a selector when the
			// saved installation is the only candidate.
			_candidatePanel.Visible = canConfigure && entry != null && entry.Candidates.Count > 0 &&
				(!isManaged || entry.Candidates.Count > 1);
			_detailLayout.RowStyles[9].Height = _locate.Visible ? 42 : 0;
			_detailLayout.RowStyles[10].Height = _candidatePanel.Visible ? 83 : 0;
			_installations.Enabled = readyForAction && canConfigure && _installations.Properties.Items.Count > 0;
		}

		internal void SelectMode(string modeId)
		{
			_selectedModeId = modeId;
			_selectedInstallationRoot = null;
			var entry = _catalog?.Entries.FirstOrDefault(item => String.Equals(item.ModeId, modeId, StringComparison.OrdinalIgnoreCase));
			if (entry != null) _visibleCollection = entry.Collection;
			_search.EditValue = String.Empty;
			Populate();
		}

		/// <summary>A master row with owned-by-document artwork and read-only detail values.</summary>
		public sealed class GameRow
		{
			/// <summary>Builds a row without reading game files or initializing native services.</summary>
			internal GameRow(GameDiscoveryEntry entry, Image logo)
			{
				Entry = entry;
				Name = entry.Name;
				Logo = logo;
				LastManaged = entry.LastPlayedUtc.HasValue ? entry.LastPlayedUtc.Value.ToLocalTime().ToString("g") : L("GameDiscovery.NoManagementHistory", "Not recorded");
			}
			internal GameDiscoveryEntry Entry { get; }
			public string Name { get; }
			public Image Logo { get; }
			public string LastManaged { get; }
			public BindingList<StatisticRow> Details { get; } = new BindingList<StatisticRow>();
		}

		/// <summary>One localized metric in the managed game's expandable overview.</summary>
		public sealed class StatisticRow
		{
			/// <summary>Initializes already-formatted display values.</summary>
			internal StatisticRow(string content, string active, string managed)
			{
				Content = content;
				Active = active;
				Managed = managed;
			}
			public string Content { get; }
			public string Active { get; }
			public string Managed { get; }
		}

		/// <summary>A determinate ring; never estimates progress from elapsed time.</summary>
		private sealed class CompletionRing : Control
		{
			private long _completed;
			private long _maximum = 1;

			internal CompletionRing()
			{
				SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
					ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
				BackColor = Color.Transparent;
			}

			internal void SetProgress(long completed, long maximum)
			{
				_maximum = Math.Max(1L, maximum);
				_completed = Math.Max(0L, Math.Min(_maximum, completed));
				Invalidate();
			}

			protected override void OnPaint(PaintEventArgs e)
			{
				base.OnPaint(e);
				var rect = new Rectangle(5, 5, Math.Max(1, Math.Min(Width, Height) - 11), Math.Max(1, Math.Min(Width, Height) - 11));
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				using (var track = new Pen(Color.FromArgb(80, SystemColors.GrayText), 3.5f))
				using (var progress = new Pen(SystemColors.Highlight, 3.5f))
				{
					progress.StartCap = LineCap.Round;
					progress.EndCap = LineCap.Round;
					e.Graphics.DrawEllipse(track, rect);
					if (_completed > 0)
						e.Graphics.DrawArc(progress, rect, -90, (float)(360d * _completed / _maximum));
				}
			}
		}

		private sealed class CandidateChoice
		{
			internal CandidateChoice(GameDiscoveryInstallationCandidate candidate) { Candidate = candidate; }
			internal GameDiscoveryInstallationCandidate Candidate { get; }
			public override string ToString() => Candidate.Source + ": " + Candidate.GameRoot;
		}
	}
}
