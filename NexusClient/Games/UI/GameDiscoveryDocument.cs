namespace Nexus.Client.Games.UI
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Windows.Forms;

	using DevExpress.XtraEditors;
	using DevExpress.XtraEditors.Controls;
	using Nexus.Client.Games;
	using Nexus.Client.Util.Localization;

	/// <summary>
	/// Lightweight, text-first discovery document. Displaying a candidate must never
	/// adopt storage, select a default game or initiate the recursive disk scanner.
	/// Manage/Configure/Locate commands are wired by the future session controller.
	/// </summary>
	internal sealed class GameDiscoveryDocument : XtraForm
	{
		private readonly TextEdit _search;
		private readonly SimpleButton _refresh;
		private readonly SimpleButton _scan;
		private readonly SimpleButton _stopScan;
		private readonly SimpleButton _manage;
		private readonly SimpleButton _configure;
		private readonly SimpleButton _locate;
		private readonly ComboBoxEdit _installations;
		private readonly LabelControl _status;
		private readonly MemoEdit _details;
		private readonly GroupControl[] _groups;
		private readonly ListBoxControl[] _lists;
		private GameDiscoveryCatalog _catalog;
		private string _selectedModeId;
		private string _selectedInstallationRoot;
		private bool _updatingInstallations;
		private bool _repopulating;

		internal event EventHandler RefreshRequested;
		internal event EventHandler ScanRequested;
		internal event EventHandler StopScanRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> ManageRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> ConfigureRequested;
		internal event EventHandler<GameDiscoveryGameRequestedEventArgs> LocateRequested;
		internal GameDiscoveryCatalog Catalog => _catalog;

		internal GameDiscoveryDocument()
		{
			Text = L("GameDiscovery.Title", "Game Discovery");
			FormBorderStyle = FormBorderStyle.None;
			ControlBox = false;

			var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 162));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
			Controls.Add(root);

			var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4 };
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 285));
			header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
			var introduction = new LabelControl { Text = L("GameDiscovery.Introduction", "Choose a game to manage. Found games need one-time configuration."), Dock = DockStyle.Fill, AutoSizeMode = LabelAutoSizeMode.Vertical };
			header.Controls.Add(introduction, 0, 0);
			header.SetColumnSpan(introduction, 4);
			_status = new LabelControl { Text = L("GameDiscovery.Loading", "Checking supported games..."), Dock = DockStyle.Fill };
			header.Controls.Add(_status, 0, 1);
			_search = new TextEdit { Dock = DockStyle.Fill };
			_search.Properties.NullValuePrompt = L("GameDiscovery.Search", "Search games...");
			_search.EditValueChanged += (sender, args) => Populate();
			header.Controls.Add(_search, 1, 1);
			_refresh = new SimpleButton { Text = L("GameDiscovery.Refresh", "Refresh detected games"), Dock = DockStyle.Fill };
			_refresh.Click += (sender, args) => RefreshRequested?.Invoke(this, EventArgs.Empty);
			header.Controls.Add(_refresh, 3, 1);
			_installations = new ComboBoxEdit { Dock = DockStyle.Fill, Enabled = false };
			_installations.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_installations.Properties.NullValuePrompt = L("GameDiscovery.ChooseInstallation", "Select installation...");
			_installations.SelectedIndexChanged += (sender, args) =>
			{
				if (_updatingInstallations) return;
				_selectedInstallationRoot = (_installations.SelectedItem as CandidateChoice)?.Candidate.GameRoot;
				UpdateActions();
			};
			header.Controls.Add(_installations, 2, 1);
			_manage = new SimpleButton
			{
				Text = L("GameDiscovery.Manage", "Manage selected game"),
				Dock = DockStyle.Fill,
				Enabled = false
			};
			_manage.Click += (sender, args) =>
			{
				var entry = SelectedEntry;
				if (GameSessionEntryGate.CanManage(entry))
					ManageRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(entry.ModeId));
			};
			header.Controls.Add(_manage, 1, 2);
			_configure = new SimpleButton { Text = L("GameDiscovery.Configure", "Configure game storage"), Dock = DockStyle.Fill };
			_configure.Click += (sender, args) =>
			{
				if (CanConfigure(SelectedEntry) && !String.IsNullOrWhiteSpace(_selectedInstallationRoot))
					ConfigureRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(SelectedEntry.ModeId, _selectedInstallationRoot));
			};
			header.Controls.Add(_configure, 2, 2);
			_locate = new SimpleButton { Text = L("GameDiscovery.Locate", "Locate game..."), Dock = DockStyle.Fill };
			_locate.Click += (sender, args) =>
			{
				if (SelectedEntry != null && !_sessionLoading)
					LocateRequested?.Invoke(this, new GameDiscoveryGameRequestedEventArgs(SelectedEntry.ModeId));
			};
			header.Controls.Add(_locate, 3, 2);
			var scanScope = new LabelControl
			{
				Text = L("GameDiscovery.ScanScope", "Full scan: all ready fixed drives (only when requested)"),
				Dock = DockStyle.Fill
			};
			header.Controls.Add(scanScope, 0, 3);
			header.SetColumnSpan(scanScope, 2);
			_scan = new SimpleButton { Text = L("GameDiscovery.Scan", "Scan for games..."), Dock = DockStyle.Fill, Enabled = false };
			_scan.Click += (sender, args) => ScanRequested?.Invoke(this, EventArgs.Empty);
			header.Controls.Add(_scan, 2, 3);
			_stopScan = new SimpleButton { Text = L("GameDiscovery.StopScan", "Stop scan"), Dock = DockStyle.Fill, Enabled = false };
			_stopScan.Click += (sender, args) => StopScanRequested?.Invoke(this, EventArgs.Empty);
			header.Controls.Add(_stopScan, 3, 3);
			root.Controls.Add(header, 0, 0);

			var sections = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
			for (int index = 0; index < 3; index++)
				sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3f));
			_groups = new GroupControl[3];
			_lists = new ListBoxControl[3];
			for (int index = 0; index < 3; index++)
			{
				int section = index;
				_groups[index] = new GroupControl { Dock = DockStyle.Fill };
				_lists[index] = new ListBoxControl { Dock = DockStyle.Fill };
				_lists[index].SelectedIndexChanged += (sender, args) => SelectEntry(section);
				_groups[index].Controls.Add(_lists[index]);
				sections.Controls.Add(_groups[index], index, 0);
			}
			root.Controls.Add(sections, 0, 1);

			_details = new MemoEdit { Dock = DockStyle.Fill };
			_details.Properties.ReadOnly = true;
			_details.Properties.ScrollBars = ScrollBars.Vertical;
			_details.Text = L("GameDiscovery.DetailsHint", "Select a game to see its saved paths and discovery details.");
			root.Controls.Add(_details, 0, 2);
			UpdateGroupCaptions(0, 0, 0);
		}

		private static string L(string key, string fallback) => LanguageManager.Get(key, fallback);

		private bool _sessionLoading;
		private bool _sessionActionsAvailable;
		private bool _refreshing;
		private bool _scanning;

		internal void EnableFirstSessionActions()
		{
			_sessionActionsAvailable = true;
			UpdateActions();
		}

		private GameDiscoveryEntry SelectedEntry => _catalog?.Entries.FirstOrDefault(item =>
			String.Equals(item.ModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase));

		internal void SetSessionReady()
		{
			UpdateActions();
		}

		internal void SetSessionInactive()
		{
			UpdateActions();
		}

		internal void SetSessionLoading(string name, bool loading)
		{
			_sessionLoading = loading;
			UpdateActions();
			UpdateDiscoveryButtons();
			if (loading)
				_status.Text = String.Format(L("GameDiscovery.LoadingGame", "Loading {0}..."), name);
		}

		internal void SetSessionMessage(string message)
		{
			_status.Text = message;
		}

		internal void SetBusy(bool busy)
		{
			_refreshing = busy;
			UpdateDiscoveryButtons();
			UpdateActions();
			_status.Text = busy ? L("GameDiscovery.Refreshing", "Checking launcher settings...")
				: L("GameDiscovery.Ready", "Launcher check complete. A full drive scan is never automatic.");
		}

		internal void SetScanning(bool scanning)
		{
			_scanning = scanning;
			UpdateDiscoveryButtons();
			UpdateActions();
			if (scanning)
				_status.Text = L("GameDiscovery.Scanning", "Scanning fixed drives for game executables...");
		}

		internal void SetScanProgress(string path)
		{
			if (_scanning && !String.IsNullOrWhiteSpace(path))
				_status.Text = L("GameDiscovery.ScanningPath", "Scanning:") + " " + path;
		}

		internal void SetScanStopping()
		{
			_stopScan.Enabled = false;
			_status.Text = L("GameDiscovery.ScanStopping", "Stopping scan; confirmed results will be kept...");
		}

		internal void SetScanResult(int foundCount, bool cancelled)
		{
			_status.Text = String.Format(cancelled
				? L("GameDiscovery.ScanStopped", "Scan stopped. {0} verified installation(s) retained.")
				: L("GameDiscovery.ScanDone", "Scan complete. {0} verified installation(s) found."), foundCount);
		}

		internal void SetError(string reason)
		{
			UpdateDiscoveryButtons();
			_status.Text = L("GameDiscovery.Error", "Could not refresh game discovery:") + " " + reason;
		}

		internal void SetScanError(string reason)
		{
			_status.Text = L("GameDiscovery.ScanError", "Game scan failed:") + " " + reason;
		}

		internal void SetRefreshFinished()
		{
			_refreshing = false;
			UpdateDiscoveryButtons();
			UpdateActions();
		}

		private void UpdateDiscoveryButtons()
		{
			_refresh.Enabled = !_refreshing && !_scanning && !_sessionLoading;
			_scan.Enabled = _catalog != null && !_refreshing && !_scanning && !_sessionLoading;
			_stopScan.Enabled = _scanning;
		}

		internal void SetCatalog(GameDiscoveryCatalog catalog)
		{
			_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
			Populate();
			UpdateDiscoveryButtons();
		}

		private void Populate()
		{
			if (_catalog == null) return;
			string query = Convert.ToString(_search.EditValue)?.Trim() ?? String.Empty;
			var collections = new[] { GameDiscoveryCollection.Managed, GameDiscoveryCollection.ToConfigure, GameDiscoveryCollection.Supported };
			int[] counts = new int[3];
			_repopulating = true;
			try
			{
				for (int index = 0; index < _lists.Length; index++)
				{
					var list = _lists[index];
					var matches = _catalog.Entries.Where(item => item.Collection == collections[index] &&
						(query.Length == 0 || item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
						item.ModeId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
					counts[index] = matches.Count;
					list.BeginUpdate();
					try
					{
						list.Items.Clear();
						foreach (GameDiscoveryEntry entry in matches)
							list.Items.Add(new DisplayEntry(entry));
						foreach (DisplayEntry row in list.Items)
							if (String.Equals(row.Entry.ModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase))
							{
								list.SelectedItem = row;
								break;
							}
					}
					finally { list.EndUpdate(); }
				}
			}
			finally { _repopulating = false; }
			UpdateGroupCaptions(counts[0], counts[1], counts[2]);
			UpdateInstallationChoices();
			UpdateDetails();
			UpdateActions();
		}

		private void UpdateGroupCaptions(int managed, int unconfigured, int supported)
		{
			_groups[0].Text = String.Format("{0} ({1})", L("GameDiscovery.Managed", "Managed"), managed);
			_groups[1].Text = String.Format("{0} ({1})", L("GameDiscovery.ToConfigure", "To configure"), unconfigured);
			_groups[2].Text = String.Format("{0} ({1})", L("GameDiscovery.Supported", "Supported"), supported);
		}

		private void SelectEntry(int selectionIndex)
		{
			if (_repopulating) return;
			var selected = _lists[selectionIndex].SelectedItem as DisplayEntry;
			if (selected == null) return;
			_selectedModeId = selected.Entry.ModeId;
			for (int index = 0; index < _lists.Length; index++)
				if (index != selectionIndex) _lists[index].SelectedIndex = -1;
			_selectedInstallationRoot = null;
			UpdateInstallationChoices();
			UpdateDetails();
			UpdateActions();
		}

		private void UpdateDetails()
		{
			GameDiscoveryEntry entry = _catalog?.Entries.FirstOrDefault(item =>
				String.Equals(item.ModeId, _selectedModeId, StringComparison.OrdinalIgnoreCase));
			if (entry == null)
			{
				_details.Text = _catalog != null && _catalog.UnavailableSavedModeIds.Count > 0
					? L("GameDiscovery.UnavailableSaved", "Unsupported saved game IDs:") + " " + String.Join(", ", _catalog.UnavailableSavedModeIds)
					: L("GameDiscovery.DetailsHint", "Select a game to see its saved paths and discovery details.");
				return;
			}
			var lines = new List<string> { entry.Name + " (" + entry.ModeId + ")" };
			lines.Add(entry.Collection == GameDiscoveryCollection.Managed
				? L("GameDiscovery.ManagedStatus", "Previously configured game")
				: entry.Collection == GameDiscoveryCollection.ToConfigure
					? L("GameDiscovery.NeedsSetup", "Installation found; storage configuration required")
					: L("GameDiscovery.NotLocated", "Supported; installation not yet selected"));
			if (entry.NeedsAttention) lines.Add(L("GameDiscovery.NeedsAttention", "Saved configuration needs review"));
			if (entry.IsStartupDefault) lines.Add(L("GameDiscovery.Default", "Remembered startup game"));
			if (!String.IsNullOrWhiteSpace(entry.SavedInstallationPath)) lines.Add(L("GameDiscovery.StoragePath", "Storage:") + " " + entry.SavedInstallationPath);
			if (!String.IsNullOrWhiteSpace(entry.SavedExecutablePath)) lines.Add(L("GameDiscovery.InstallationPath", "Installation:") + " " + entry.SavedExecutablePath);
			foreach (var candidate in entry.Candidates)
				lines.Add(candidate.Source + ": " + candidate.GameRoot);
			if (_catalog.UnavailableSavedModeIds.Count > 0)
				lines.Add(L("GameDiscovery.UnavailableSaved", "Unsupported saved game IDs:") + " " + String.Join(", ", _catalog.UnavailableSavedModeIds));
			_details.Text = String.Join(Environment.NewLine, lines);
		}

		internal void SelectMode(string modeId)
		{
			_selectedModeId = modeId;
			_selectedInstallationRoot = null;
			_search.EditValue = String.Empty;
			Populate();
		}

		private static bool CanConfigure(GameDiscoveryEntry entry)
		{
			return entry != null && (entry.Collection == GameDiscoveryCollection.ToConfigure ||
				(entry.Collection == GameDiscoveryCollection.Managed && entry.NeedsAttention));
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
				// Multiple installations require an explicit user choice.
				if (index < 0 && _installations.Properties.Items.Count == 1) index = 0;
				_installations.SelectedIndex = index;
				_selectedInstallationRoot = (_installations.SelectedItem as CandidateChoice)?.Candidate.GameRoot;
			}
			finally { _updatingInstallations = false; }
		}

		private void UpdateActions()
		{
			GameDiscoveryEntry entry = SelectedEntry;
			_manage.Enabled = _sessionActionsAvailable && !_sessionLoading && !_scanning && !_refreshing && GameSessionEntryGate.CanManage(entry);
			_configure.Enabled = _sessionActionsAvailable && !_sessionLoading && !_scanning && !_refreshing && CanConfigure(entry) &&
				!String.IsNullOrWhiteSpace(_selectedInstallationRoot);
			_locate.Enabled = _sessionActionsAvailable && !_sessionLoading && !_scanning && !_refreshing && entry != null &&
				(entry.Collection != GameDiscoveryCollection.Managed || entry.NeedsAttention);
			_installations.Enabled = _sessionActionsAvailable && !_sessionLoading && !_scanning && !_refreshing && CanConfigure(entry) &&
				_installations.Properties.Items.Count > 0;
		}

		private sealed class CandidateChoice
		{
			internal CandidateChoice(GameDiscoveryInstallationCandidate candidate) { Candidate = candidate; }
			internal GameDiscoveryInstallationCandidate Candidate { get; }
			public override string ToString() => Candidate.Source + ": " + Candidate.GameRoot;
		}

		private sealed class DisplayEntry
		{
			internal DisplayEntry(GameDiscoveryEntry entry) { Entry = entry; }
			internal GameDiscoveryEntry Entry { get; }
			public override string ToString() => Entry.Name + (Entry.NeedsAttention ? "  [Needs attention]" :
				Entry.IsStartupDefault ? "  [Default]" : String.Empty);
		}
	}
}
