using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using Nexus.Client.OnlineServices.Wabbajack;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Wabbajack.UI
{
	/// <summary>Selects a compiled profile, acquires its sources and prepares an ordinary NMM Collection for review.</summary>
	internal sealed class WabbajackPreparationDialog : XtraForm
	{
		private readonly MainFormVM _viewModel;
		private readonly WabbajackPackage _package;
		private readonly WabbajackClient _client;
		private readonly string _sourceId;
		private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
		private readonly ComboBoxEdit _profiles;
		private readonly TextEdit _downloads;
		private readonly TextEdit _output;
		private readonly MemoEdit _status;
		private readonly SimpleButton _prepare;
		private readonly SimpleButton _download;
		private readonly SimpleButton _manual;
		private readonly List<Control> _inputs = new List<Control>();
		private WabbajackCollectionConverter _converter;
		private IReadOnlyList<WabbajackArchive> _missing = new WabbajackArchive[0];
		private bool _busy;
		internal WabbajackConvertedCollection Result { get; private set; }

		/// <summary>Uses the active game's existing repository account and archive directory.</summary>
		internal WabbajackPreparationDialog(MainFormVM viewModel, WabbajackPackage package, WabbajackClient client, string sourceId)
		{
			_viewModel = viewModel; _package = package; _client = client; _sourceId = sourceId;
			Text = L("PrepareTitle", "Prepare Wabbajack installation");
			Size = new System.Drawing.Size(850, 580);
			StartPosition = FormStartPosition.CenterParent;
			_profiles = new ComboBoxEdit { Width = 300 };
			_profiles.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			foreach (WabbajackProfile profile in WabbajackProfile.ReadAll(package, viewModel.WabbajackGallery)) _profiles.Properties.Items.Add(profile);
			if (_profiles.Properties.Items.Count == 0) throw new NotSupportedException(L("NoProfiles", "This list has no supported compiled MO2 profiles."));
			int defaultProfile = _profiles.Properties.Items.Cast<WabbajackProfile>().ToList().FindIndex(profile => profile.Name == package.DefaultProfile);
			_profiles.SelectedIndex = defaultProfile < 0 ? 0 : defaultProfile;
			_downloads = new TextEdit { Width = 540, Text = viewModel.EnvironmentInfo.Settings.ModFolder[viewModel.GameMode.ModeId] };
			_output = new TextEdit { Width = 540, Text = Path.Combine(Path.GetDirectoryName(package.FilePath), "NMM Wabbajack") };
			_status = new MemoEdit { Dock = DockStyle.Fill, Text = L("PreparationHelp", "Choose the profile to install. Existing archives are reused by their Wabbajack hashes. Premium accounts can download Nexus sources automatically; free accounts can download the missing files in the browser and prepare again. Game files are read from your own installation.") };
			_status.Properties.ReadOnly = true;
			_prepare = Button(L("Prepare", "Prepare Collection"), async () => await PrepareAsync());
			_download = Button(L("DownloadMissing", "Download missing sources"), async () => await DownloadAsync());
			_manual = Button(L("OpenMissing", "Open next missing download"), OpenNextMissing);
			_download.Enabled = _manual.Enabled = false;
			TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
			layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			for (int index = 0; index < 3; index++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.Controls.Add(Row(L("Profile", "Profile"), _profiles), 0, 0);
			layout.Controls.Add(Row(L("DownloadsFolder", "Downloads folder"), _downloads), 0, 1);
			layout.Controls.Add(Row(L("OutputFolder", "Prepared Collection folder"), _output), 0, 2);
			layout.Controls.Add(_status, 0, 3);
			FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
			actions.Controls.AddRange(new Control[] { _prepare, _download, _manual,
				Button(L("Cancel", "Cancel"), () => { _cancellation.Cancel(); if (!_busy) Close(); }) });
			layout.Controls.Add(actions, 0, 4); Controls.Add(layout);
			FormClosing += (sender, args) => { _cancellation.Cancel(); args.Cancel = _busy; };
			_profiles.SelectedIndexChanged += (sender, args) => { _converter = null; _missing = new WabbajackArchive[0]; _download.Enabled = _manual.Enabled = false; };
		}

		/// <summary>Scans only selected sources and reconstructs the profile when all inputs are available.</summary>
		private async Task PrepareAsync()
		{
			if (_busy || _cancellation.IsCancellationRequested) return;
			SetBusy(true);
			try
			{
				WabbajackProfile profile = (WabbajackProfile)_profiles.SelectedItem;
				_converter = new WabbajackCollectionConverter(_package, profile, _viewModel.WabbajackGallery,
					_viewModel.GameMode.GameModeEnvironmentInfo.InstallationPath, _viewModel.ModRepository.GameDomainName);
				string[] directories = DownloadDirectories();
				Progress<string> progress = Progress();
				_missing = await Task.Run(() => _converter.FindMissingSources(directories, progress, _cancellation.Token));
				if (IsDisposed) return;
				if (_missing.Count > 0) { ShowMissing(); return; }
				string output = _output.Text;
				WabbajackConvertedCollection converted = await Task.Run(() => _converter.Convert(output, _sourceId, progress, _cancellation.Token));
				if (!IsDisposed && !_cancellation.IsCancellationRequested) { SetBusy(false); Result = converted; DialogResult = DialogResult.OK; Close(); }
			}
			catch (OperationCanceledException) { }
			catch (Exception error) { ShowError(error); }
			finally { SetBusy(false); }
		}

		/// <summary>Downloads sources through the current Nexus repository and the Wabbajack HTTP/CDN client.</summary>
		private async Task DownloadAsync()
		{
			if (_busy || _converter == null || _cancellation.IsCancellationRequested) return;
			SetBusy(true);
			try
			{
				string downloads = _downloads.Text;
				Progress<string> progress = Progress();
				await _converter.DownloadSourcesAsync(_missing, downloads, _viewModel.ModRepository, _client, progress, _cancellation.Token);
				string[] directories = DownloadDirectories();
				_missing = await Task.Run(() => _converter.FindMissingSources(directories, progress, _cancellation.Token));
				if (!IsDisposed) ShowMissing();
			}
			catch (OperationCanceledException) { }
			catch (Exception error) { ShowError(error); }
			finally { SetBusy(false); }
		}

		/// <summary>Includes the current archive library and cached vanilla inputs from earlier conversions.</summary>
		private string[] DownloadDirectories()
		{
			return new[] { _downloads.Text, _viewModel.EnvironmentInfo.Settings.ModFolder[_viewModel.GameMode.ModeId], Path.Combine(_output.Text, "Wabbajack Sources") };
		}

		/// <summary>Lists missing exact sources without replacing them with newer mod versions.</summary>
		private void ShowMissing()
		{
			_status.Text = _missing.Count == 0 ? L("SourcesReady", "Sources are ready. Choose Prepare Collection to continue.") :
				L("MissingSources", "Missing sources (download them into the selected folder, then prepare again):") + Environment.NewLine +
				string.Join(Environment.NewLine, _missing.Select(archive => archive.Name));
		}

		/// <summary>Opens the next manual source using its exact file ID and the normal Nexus website flow.</summary>
		private void OpenNextMissing()
		{
			WabbajackArchive archive = _missing.FirstOrDefault(item => !WabbajackCollectionConverter.Kind(item.State?.Type).StartsWith("GameFileSource", StringComparison.Ordinal));
			if (archive == null) { _status.Text = L("OriginalGameFiles", "The list requires original game/DLC files. Restore the matching game files before preparing this profile."); return; }
			if (WabbajackCollectionConverter.Kind(archive.State.Type).StartsWith("Nexus", StringComparison.Ordinal) &&
				!string.Equals(archive.State.GameName, _package.GameType, StringComparison.OrdinalIgnoreCase))
			{
				_status.Text = L("OtherGameSource", "This source belongs to another game's Nexus catalogue. Place its exact archive in the downloads folder."); return;
			}
			string url = WabbajackCollectionConverter.Kind(archive.State.Type).StartsWith("Nexus", StringComparison.Ordinal)
				? "https://www.nexusmods.com/" + _viewModel.ModRepository.GameDomainName + "/mods/" + archive.State.ModId + "?tab=files&file_id=" + archive.State.FileId
				: archive.State.Url;
			Uri uri;
			if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return;
			try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
			catch (Exception error) { ShowError(error); }
		}

		/// <summary>Creates a skinned directory/profile row using normal Windows folder selection.</summary>
		private FlowLayoutPanel Row(string text, Control editor)
		{
			FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
			row.Controls.Add(new LabelControl { Text = text, Width = 160, AutoSizeMode = LabelAutoSizeMode.None, Margin = new Padding(3, 7, 3, 3) });
			row.Controls.Add(editor); _inputs.Add(editor);
			if (editor is TextEdit && !(editor is ComboBoxEdit))
			{
				SimpleButton browse = Button("...", () => {
					using (FolderBrowserDialog dialog = new FolderBrowserDialog { SelectedPath = editor.Text, Description = text })
						if (dialog.ShowDialog(this) == DialogResult.OK) editor.Text = dialog.SelectedPath;
				});
				row.Controls.Add(browse); _inputs.Add(browse);
			}
			return row;
		}

		/// <summary>Routes background preparation progress to this dialog while it is still open.</summary>
		private Progress<string> Progress() { return new Progress<string>(message => { if (!IsDisposed) _status.Text = message; }); }

		/// <summary>Keeps the selected profile and source directories stable during a preparation operation.</summary>
		private void SetBusy(bool busy)
		{
			_busy = busy; if (IsDisposed) return;
			foreach (Control input in _inputs) input.Enabled = !busy;
			_prepare.Enabled = !busy;
			_download.Enabled = _manual.Enabled = !busy && _missing.Count > 0;
			if (!busy && _cancellation.IsCancellationRequested) Close();
		}

		/// <summary>Displays ordinary preparation failures without changing the installed setup.</summary>
		private void ShowError(Exception error) { Trace.TraceError("Wabbajack preparation: {0}", error); if (!IsDisposed) _status.Text = error.Message; }

		/// <summary>Creates a native DevExpress action button.</summary>
		private static SimpleButton Button(string text, Action action) { SimpleButton button = new SimpleButton { Text = text, AutoSize = true }; button.Click += (sender, args) => action(); return button; }

		/// <summary>Resolves preparation strings through NMM's existing localization system.</summary>
		private static string L(string key, string fallback) { return LanguageManager.Get("Wabbajack." + key, fallback); }
	}
}
