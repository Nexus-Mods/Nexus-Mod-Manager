using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement.UI;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.UI;

namespace Nexus.Client.Wabbajack.UI
{
	/// <summary>Prepares the selected list in the gallery's right pane using NMM's normal download queue.</summary>
	internal sealed class WabbajackPreparationControl : XtraUserControl
	{
		private readonly MainFormVM _viewModel;
		private readonly WabbajackPackage _package;
		private readonly string _sourceId;
		private readonly Func<WabbajackConvertedCollection, string, Task> _openPrepared;
		private readonly ComboBoxEdit _profiles;
		private readonly TextEdit _downloads;
		private readonly TextEdit _output;
		private readonly WabbajackActivityControl _activity;
		private readonly GridControl _sources;
		private readonly GridView _sourceView;
		private readonly SimpleButton _check, _prepare, _download, _manual, _back, _stop;
		private readonly List<Control> _inputs = new List<Control>();
		private CancellationTokenSource _operation;
		private WabbajackCollectionConverter _converter;
		private IReadOnlyList<WabbajackArchive> _missing = new WabbajackArchive[0];
		private Exception _error;
		private string _phase = "profile";
		private bool _busy;
		private bool _sourcesChecked;
		internal event EventHandler BackRequested = delegate { };
		internal event EventHandler BusyChanged = delegate { };
		internal bool IsBusy => _busy;

		/// <summary>Keeps the package title, profile, source status and next action together.</summary>
		internal WabbajackPreparationControl(MainFormVM viewModel, WabbajackPackage package, string sourceId, Func<WabbajackConvertedCollection, string, Task> openPrepared, WabbajackActivityControl activity)
		{
			_viewModel = viewModel; _package = package; _sourceId = sourceId; _openPrepared = openPrepared; Dock = DockStyle.Fill;
			_profiles = new ComboBoxEdit { Dock = DockStyle.Fill }; _profiles.Properties.TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor;
			foreach (WabbajackProfile profile in WabbajackProfile.ReadAll(package, viewModel.WabbajackGallery)) _profiles.Properties.Items.Add(profile);
			if (_profiles.Properties.Items.Count == 0) throw new NotSupportedException(L("NoProfiles", "This list has no supported compiled MO2 profiles."));
			int defaultProfile = _profiles.Properties.Items.Cast<WabbajackProfile>().ToList().FindIndex(profile => profile.Name == package.DefaultProfile);
			_profiles.SelectedIndex = defaultProfile < 0 ? 0 : defaultProfile;
			_downloads = new TextEdit { Dock = DockStyle.Fill, Text = viewModel.EnvironmentInfo.Settings.ModFolder[viewModel.GameMode.ModeId] };
			_output = new TextEdit { Dock = DockStyle.Fill, Text = Path.Combine(Path.GetDirectoryName(package.FilePath), "NMM Wabbajack") };
			WabbajackSummaryControl summary = new WabbajackSummaryControl { Dock = DockStyle.Top }; summary.ShowPackage(package);
			TableLayoutPanel choices = WabbajackUi.Stack(3); choices.Padding = new Padding(8);
			choices.Controls.Add(Field(L("Profile", "Profile"), _profiles), 0, 0);
			choices.Controls.Add(Field(L("DownloadsFolder", "Downloads folder"), _downloads), 0, 1);
			choices.Controls.Add(Field(L("OutputFolder", "Collection files folder"), _output), 0, 2);
			_check = Button(L("CheckSources", "1. Check sources"), async () => await RunAsync("sources", CheckSourcesAsync), NmmIconAction.Refresh);
			_download = Button(L("QueueMissing", "2. Download missing"), async () => await RunAsync("download", DownloadAsync), NmmIconAction.DownloadMode);
			_prepare = Button(L("PrepareForCollections", "3. Create Collection"), async () => await RunAsync("convert", PrepareAsync), NmmIconAction.InstallEnable);
			_prepare.Appearance.FontStyleDelta = FontStyle.Bold;
			_manual = Button(L("OpenMissing", "Open next missing download"), OpenNextMissing, NmmIconAction.ExternalLink);
			_back = Button(L("BackToDetails", "Back to list details"), () => BackRequested(this, EventArgs.Empty), NmmIconAction.Browse);
			_stop = Button(L("StopPreparation", "Stop preparation"), () => _operation?.Cancel(), NmmIconAction.Cancel);
			SimpleButton export = Button(L("ExportReport", "Export technical report..."), ExportReport, NmmIconAction.Export);
			WabbajackActionRow steps = new WabbajackActionRow(); steps.Controls.AddRange(new Control[] { _check, _download, _prepare, _manual });
			WabbajackActionRow navigation = new WabbajackActionRow(); navigation.Controls.AddRange(new Control[] { _back, _stop, export });
			_activity = activity;
			_activity.ShowActivity(L("ChooseProfile", "Choose a profile, then check sources"), L("PreparationSteps", "Existing mods are reused. Download missing sources, then create a logical Collection. Only list-specific custom files are generated."), false);
			_sources = new GridControl { Dock = DockStyle.Fill }; _sourceView = new GridView(_sources); _sources.MainView = _sourceView; _sources.ViewCollection.Add(_sourceView);
			_sourceView.OptionsBehavior.Editable = false; _sourceView.OptionsView.ShowGroupPanel = false; _sourceView.OptionsView.ShowIndicator = false; _sourceView.OptionsView.ShowAutoFilterRow = true;
			_sourceView.Columns.AddVisible(nameof(SourceRow.Status), L("SourceStatus", "Status")).Width = 80;
			_sourceView.Columns.AddVisible(nameof(SourceRow.Name), L("SourceFile", "Source file")).Width = 260;
			_sourceView.Columns.AddVisible(nameof(SourceRow.Provider), L("SourceProvider", "Source")).Width = 90;
			_sources.DataSource = new List<SourceRow>();
			TableLayoutPanel heading = WabbajackUi.Stack(2); heading.Controls.Add(summary, 0, 0); heading.Controls.Add(choices, 0, 1);
			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(heading, 0, 0); layout.Controls.Add(steps, 0, 1); layout.Controls.Add(_sources, 0, 2); layout.Controls.Add(navigation, 0, 3); Controls.Add(layout);
			_profiles.SelectedIndexChanged += (sender, args) => InvalidateSources(); _downloads.EditValueChanged += (sender, args) => InvalidateSources(); _output.EditValueChanged += (sender, args) => InvalidateSources();
			UpdateActions();
		}

		/// <summary>Runs one preparation step and retains diagnostic context on failure.</summary>
		private async Task RunAsync(string phase, Func<CancellationToken, Task> action)
		{
			if (_busy) return;
			_operation = new CancellationTokenSource(); _phase = phase; _error = null; _busy = true; UpdateActions(); BusyChanged(this, EventArgs.Empty);
			_activity.ShowActivity(phase == "download" ? L("DownloadingSources", "Downloading sources in Download Manager") : phase == "convert" ? L("PreparingProfile", "Creating a logical Collection") : L("CheckingSources", "Checking source files"), _package.Name + " · " + _profiles.Text, true);
			try { await action(_operation.Token); }
			catch (OperationCanceledException) { if (!IsDisposed) _activity.ShowActivity(L("PreparationStopped", "Preparation stopped"), L("DownloadsContinue", "Queued downloads remain available in Download Manager. Check sources when you are ready to continue."), false); }
			catch (Exception error) { _error = error; Trace.TraceError("Wabbajack preparation: {0}", error); if (!IsDisposed) _activity.ShowActivity(L("PreparationFailed", "Preparation could not finish"), error.Message + Environment.NewLine + L("ReportHint", "Export the technical report for the file and error details."), false, true); }
			finally { _busy = false; _operation.Dispose(); _operation = null; if (!IsDisposed) { UpdateActions(); BusyChanged(this, EventArgs.Empty); } }
		}

		/// <summary>Stops preparation from the gallery's shared activity area.</summary>
		internal void CancelWork() { _operation?.Cancel(); }

		/// <summary>Locates exact inputs for the selected profile.</summary>
		private async Task CheckSourcesAsync(CancellationToken token)
		{
			_sourcesChecked = false;
			_converter = new WabbajackCollectionConverter(_package, (WabbajackProfile)_profiles.SelectedItem, _viewModel.WabbajackGallery, _viewModel.GameMode.GameModeEnvironmentInfo.InstallationPath, _viewModel.ModRepository.GameDomainName);
			await ScanSourcesAsync(token);
		}

		/// <summary>Refreshes source availability after native downloads or manual acquisition.</summary>
		private async Task ScanSourcesAsync(CancellationToken token)
		{
			string[] directories = { _downloads.Text, _viewModel.EnvironmentInfo.Settings.ModFolder[_viewModel.GameMode.ModeId], _viewModel.GameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory, Path.Combine(_output.Text, "Wabbajack Sources") };
			Progress<string> progress = Progress();
			_missing = await Task.Run(() => _converter.FindMissingSources(directories, progress, token), token);
			if (IsDisposed) return;
			_sourcesChecked = true;
			HashSet<string> missing = new HashSet<string>(_missing.Select(archive => archive.Hash));
			_sources.DataSource = _converter.RequiredArchives.Select(archive => new SourceRow(archive, missing.Contains(archive.Hash))).ToList();
			_activity.ShowActivity(_missing.Count == 0 ? L("SourcesReadyTitle", "All sources are ready") : string.Format(L("MissingSourceCount", "{0} source files are missing"), _missing.Count),
				_missing.Count == 0 ? L("PrepareNext", "Choose Create Collection to reference the existing mods and prepare only list-specific custom files.") : L("DownloadNext", "Choose Download missing for automatic sources. For manual files, select a row and open its download page, then check sources again."), false);
		}

		/// <summary>Acquires missing files through the regular Download Manager.</summary>
		private async Task DownloadAsync(CancellationToken token)
		{
			await _converter.DownloadSourcesAsync(_missing, _downloads.Text, _viewModel.ModRepository, _viewModel.ModManager, Progress(), token);
			await ScanSourcesAsync(token);
		}

		/// <summary>Creates archive references and list-specific custom files, then opens the logical Collection for installation review.</summary>
		private async Task PrepareAsync(CancellationToken token)
		{
			string output = _output.Text;
			Progress<string> progress = Progress();
			WabbajackConvertedCollection result = await Task.Run(() => _converter.Convert(output, _sourceId, progress, token), token);
			if (IsDisposed || token.IsCancellationRequested) return;
			await _openPrepared(result, (_viewModel.GameMode as Nexus.Client.Games.Gamebryo.GamebryoGameModeBase)?.UserGameDataPath);
			if (!IsDisposed) _activity.ShowActivity(L("CollectionPrepared", "Profile ready in Collections"), L("ContinueCollections", "Continue in Collections to review and install the prepared profile."), false);
		}

		/// <summary>Opens the selected missing source through the normal browser flow.</summary>
		private void OpenNextMissing()
		{
			WabbajackArchive selected = (_sourceView.GetFocusedRow() as SourceRow)?.Archive;
			WabbajackArchive archive = selected != null && _missing.Contains(selected) ? selected : _missing.FirstOrDefault();
			if (archive == null) return;
			string kind = WabbajackCollectionConverter.Kind(archive.State?.Type);
			if (kind.StartsWith("GameFileSource", StringComparison.Ordinal)) { _activity.Detail = L("OriginalGameFiles", "The list requires original game/DLC files. Restore the matching game files before preparing this profile."); return; }
			if (kind.StartsWith("Nexus", StringComparison.Ordinal) && !string.Equals(archive.State.GameName, _package.GameType, StringComparison.OrdinalIgnoreCase)) { _activity.Detail = L("OtherGameSource", "This source belongs to another game's Nexus catalogue. Place its exact archive in the downloads folder."); return; }
			string url = kind.StartsWith("Nexus", StringComparison.Ordinal) ? "https://www.nexusmods.com/" + _viewModel.ModRepository.GameDomainName + "/mods/" + archive.State.ModId + "?tab=files&file_id=" + archive.State.FileId : archive.State?.Url;
			Uri uri; if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" && uri.Scheme != "http") return;
			try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception error) { _error = error; _activity.Detail = error.Message; }
		}

		/// <summary>Exports the failure and current instruction using the Collections report redaction rules.</summary>
		private void ExportReport()
		{
			using (SaveFileDialog dialog = new SaveFileDialog { Filter = L("ReportFilter", "Technical reports (*.json)|*.json"), FileName = "Wabbajack-Preparation-Report.json" })
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				try
				{
					JObject report = new JObject { ["formatVersion"] = 1, ["createdUtc"] = DateTime.UtcNow, ["nmmVersion"] = _viewModel.EnvironmentInfo.ApplicationVersion.ToString(),
						["list"] = _package.Name, ["listVersion"] = _package.Version, ["wabbajackVersion"] = _package.WabbajackVersion, ["source"] = _package.SourceId ?? _sourceId,
						["game"] = _package.GameType, ["profile"] = _profiles.Text, ["phase"] = _phase, ["currentOutput"] = _converter?.CurrentOutput, ["instruction"] = _converter?.CurrentInstruction,
						["completedOutputs"] = _converter?.CompletedOutputs, ["totalOutputs"] = _converter?.TotalOutputs,
						["archivePath"] = new JArray(_package.Directives.FirstOrDefault(directive => directive.To == _converter?.CurrentOutput)?.ArchiveHashPath ?? new string[0]),
						["status"] = CollectionTechnicalReportSanitizer.SanitizeText(_activity.Detail), ["exception"] = CollectionTechnicalReportSanitizer.SanitizeText(_error?.ToString()),
						["downloads"] = new JArray(_viewModel.DownloadMonitorVM.Tasks.OfType<WabbajackDownloadTask>().Select(task => new JObject {
							["name"] = CollectionTechnicalReportSanitizer.SanitizeText(task.OverallMessage), ["status"] = task.Status.ToString(),
							["downloadedKiB"] = task.DownloadProgress, ["totalKiB"] = task.DownloadMaximum, ["message"] = CollectionTechnicalReportSanitizer.SanitizeText(task.ItemMessage),
							["exception"] = CollectionTechnicalReportSanitizer.SanitizeText(task.Failure?.ToString()) })),
						["missingSources"] = new JArray(_missing.Select(archive => new JObject { ["name"] = archive.Name, ["hash"] = archive.Hash, ["size"] = archive.Size, ["provider"] = WabbajackCollectionConverter.Kind(archive.State?.Type) })) };
					File.WriteAllText(dialog.FileName, report.ToString(), new System.Text.UTF8Encoding(false)); _activity.Detail = L("ReportExported", "Technical report exported.");
				}
				catch (Exception error) { _activity.Detail = error.Message; }
			}
		}

		/// <summary>Creates a full-width labeled editor with an optional folder picker.</summary>
		private TableLayoutPanel Field(string title, BaseEdit editor)
		{
			TableLayoutPanel field = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 4, 0, 4) };
			field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); field.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); field.RowStyles.Add(new RowStyle(SizeType.AutoSize)); field.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			field.Controls.Add(WabbajackUi.Caption(title, true), 0, 0); field.Controls.Add(editor, 0, 1); _inputs.Add(editor);
			if (!(editor is ComboBoxEdit))
			{
				SimpleButton browse = Button("...", () => { using (FolderBrowserDialog dialog = new FolderBrowserDialog { SelectedPath = editor.Text, Description = title }) if (dialog.ShowDialog(this) == DialogResult.OK) editor.Text = dialog.SelectedPath; }, NmmIconAction.OpenFolder);
				field.Controls.Add(browse, 1, 1); _inputs.Add(browse);
			}
			return field;
		}
		/// <summary>Requires a new scan when the profile or source locations change.</summary>
		private void InvalidateSources() { _converter = null; _sourcesChecked = false; _missing = new WabbajackArchive[0]; _sources.DataSource = new List<SourceRow>(); UpdateActions(); }
		/// <summary>Enables only the next valid actions while keeping choices stable during work.</summary>
		private void UpdateActions() { foreach (Control input in _inputs) input.Enabled = !_busy; _check.Enabled = _back.Enabled = !_busy; _download.Enabled = _manual.Enabled = !_busy && _missing.Count > 0; _prepare.Enabled = !_busy && _sourcesChecked && _converter != null && _missing.Count == 0; _stop.Enabled = _busy; }
		/// <summary>Marshals preparation details to the activity surface.</summary>
		private Progress<string> Progress() { return new Progress<string>(message => { if (!IsDisposed && _busy) _activity.Detail = message; }); }
		/// <summary>Creates a native action using NMM's icon settings.</summary>
		private static SimpleButton Button(string text, Action action, NmmIconAction icon) { SimpleButton button = new SimpleButton { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) }; button.Click += (sender, args) => action(); NmmIconProvider.Bind(button, icon); return button; }
		/// <summary>Resolves preparation strings through the existing language infrastructure.</summary>
		private static string L(string key, string fallback) { return WabbajackUi.L(key, fallback); }
		/// <summary>Stops view-owned work when the game session closes.</summary>
		protected override void Dispose(bool disposing) { if (disposing) _operation?.Cancel(); base.Dispose(disposing); }

		/// <summary>Human-readable source state for the native grid.</summary>
		private sealed class SourceRow
		{
			internal WabbajackArchive Archive { get; }
			public string Name => Archive.Name;
			public string Status { get; }
			public string Provider { get; }
			/// <summary>Creates a source row without serialized implementation type names.</summary>
			internal SourceRow(WabbajackArchive archive, bool missing) { Archive = archive; Status = missing ? L("SourceMissing", "Missing") : L("SourceAvailable", "Ready"); string kind = WabbajackCollectionConverter.Kind(archive.State?.Type); Provider = kind.StartsWith("Nexus", StringComparison.Ordinal) ? "Nexus Mods" : kind.StartsWith("GameFileSource", StringComparison.Ordinal) ? L("GameFiles", "Game files") : "HTTP / CDN"; }
		}
	}
}
