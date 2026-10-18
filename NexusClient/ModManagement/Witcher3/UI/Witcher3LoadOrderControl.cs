using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;
using WeifenLuo.WinFormsUI.Docking;

namespace Nexus.Client.ModManagement.Witcher3.UI
{
	/// <summary>
	/// Dedicated Witcher *game-folder* ordering page. Never passes folder rows through Bethesda
	/// plugins, SortMods, archive sorting, or a reinstall. All file writes require a separate
	/// W3R.5 preview, on-screen approval and the W3R.5c authority-gated publisher.
	/// </summary>
	public sealed class Witcher3LoadOrderControl : ManagedFontDockContent
	{
		private readonly GridControl _grid;
		private readonly GridView _view;
		private readonly MemoEdit _status;
		private readonly MemoEdit _remasteredHealth;
		private readonly MemoEdit _details;
		private readonly SimpleButton _up;
		private readonly SimpleButton _down;
		private readonly SimpleButton _enable;
		private readonly SimpleButton _disable;
		private readonly SimpleButton _add;
		private readonly SimpleButton _addAll;
		private readonly SimpleButton _removeAddition;
		private readonly SimpleButton _restoreDefaults;
		private readonly SimpleButton _resetDraft;
		private readonly SimpleButton _reload;
		private readonly SimpleButton _apply;
		private readonly SimpleButton _recovery;
		private readonly SimpleButton _inspectMerger;
		private readonly string _enabledText;
		private readonly string _disabledText;
		private readonly string _unconfiguredText;
		private readonly System.Windows.Forms.Timer _nativeRefreshTimer;
		private Witcher3LoadOrderViewService _readService;
		private Witcher3ModSettingsLifecycleService _lifecycle;
		private Witcher3LoadOrderViewSnapshot _snapshot;
		private Witcher3NativeLifecycleObserver _nativeChanges;
		private Func<bool> _isActiveDocument;
		private int _observedNativeRevision = -1;
		private int _nativeUiDispatchQueued;
		private bool _nativeRefreshNeeded;
		private bool _busy;
		// A user-selected external tool is scoped to the observed physical game root.
		// This is not a registered NMM tool or a persisted executable authorization.
		private string _selectedMergerExecutable;
		private string _selectedMergerGameRoot;
		private Witcher3ScriptMergerToolObservation _mergerObservation;

		public Witcher3LoadOrderControl()
		{
			Name = "Witcher3LoadOrderControl";
			Text = L("Witcher3.Order.Title", "Witcher Load Order");
			DockAreas = DockAreas.Document;
			_enabledText = L("Witcher3.Order.Values.Enabled", "Enabled");
			_disabledText = L("Witcher3.Order.Values.Disabled", "Disabled");
			_unconfiguredText = L("Witcher3.Order.Values.Unconfigured", "Not configured");
			// Coalesce bursts from native authority reload / multi-child operations.
			_nativeRefreshTimer = new System.Windows.Forms.Timer { Interval = 450 };
			_nativeRefreshTimer.Tick += (sender, args) =>
			{
				_nativeRefreshTimer.Stop();
				if (Witcher3NativeRefreshPolicy.CanReadAutomatically(
					_isActiveDocument?.Invoke() == true, _busy, _snapshot?.Editor.IsDirty == true,
					NativeOperationRunning, _nativeRefreshNeeded))
					_ = RefreshFromGameAsync(false);
			};

			var description = new LabelControl { Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical,
				Text = L("Witcher3.Order.Introduction", "Witcher Mods/mod* folders (not archive Sort or Bethesda plugins). Lower Priority loads first and wins conflicts; alphabetical folder names break ties. A game-enabled flag does not install or uninstall an NMM archive."),
				Padding = new Padding(7), UseMnemonic = false };
			description.Appearance.TextOptions.WordWrap = WordWrap.Wrap;

			var commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42,
				AutoScroll = true, WrapContents = false, Padding = new Padding(5, 4, 5, 2) };
			_up = Button(commands, "Witcher3.Order.Actions.Up", "Move up", () => Move(-1));
			_down = Button(commands, "Witcher3.Order.Actions.Down", "Move down", () => Move(1));
			_enable = Button(commands, "Witcher3.Order.Actions.Enable", "Enable in game", () => SetEnabled(true));
			_disable = Button(commands, "Witcher3.Order.Actions.Disable", "Disable in game", () => SetEnabled(false));
			_add = Button(commands, "Witcher3.Order.Actions.Add", "Add folder to order", AddSelectedFolder);
			_addAll = Button(commands, "Witcher3.Order.Actions.AddAll", "Add all observed folders", AddAllObservedFolders);
			_removeAddition = Button(commands, "Witcher3.Order.Actions.RemoveAddition", "Remove draft addition", RemoveSelectedAddition);
			_restoreDefaults = Button(commands, "Witcher3.Order.Actions.RestoreDefaults", "Restore default ordering", () => _ = ReviewAndApplyAsync(true));
			_resetDraft = Button(commands, "Witcher3.Order.Actions.ResetDraft", "Discard draft", ResetDraft);
			_reload = Button(commands, "Witcher3.Order.Actions.Import", "Import from game / Refresh", () => _ = RefreshFromGameAsync(true));
			_apply = Button(commands, "Witcher3.Order.Actions.Apply", "Review and apply", () => _ = ReviewAndApplyAsync());
			_recovery = Button(commands, "Witcher3.Order.Actions.Recovery", "Review pending recovery", () => _ = ReviewRecoveryAsync());
			_inspectMerger = Button(commands, "Witcher3.Health.InspectMerger", "Inspect Script Merger executable...",
				() => _ = InspectMergerAsync());
			_inspectMerger.Visible = false;

			_grid = new GridControl { Dock = DockStyle.Fill, Margin = new Padding(0) };
			_view = new GridView(_grid);
			_grid.MainView = _view;
			_grid.ViewCollection.Add(_view);
			_view.OptionsBehavior.Editable = false;
			_view.OptionsCustomization.AllowSort = false;
			_view.OptionsCustomization.AllowColumnMoving = false;
			_view.OptionsView.ShowGroupPanel = false;
			_view.OptionsView.ShowIndicator = false;
			_view.OptionsSelection.MultiSelect = false;
			_view.Columns.AddVisible(nameof(DisplayRow.DraftPosition), L("Witcher3.Order.Columns.Draft", "Draft order"));
			_view.Columns.AddVisible(nameof(DisplayRow.GamePriority), L("Witcher3.Order.Columns.Priority", "Game priority"));
			_view.Columns.AddVisible(nameof(DisplayRow.FolderName), L("Witcher3.Order.Columns.Folder", "Mod folder"));
			_view.Columns.AddVisible(nameof(DisplayRow.GameEnabled), L("Witcher3.Order.Columns.Enabled", "Game enabled"));
			_view.Columns.AddVisible(nameof(DisplayRow.Origin), L("Witcher3.Order.Columns.Origin", "Origin"));
			_view.Columns.AddVisible(nameof(DisplayRow.Presence), L("Witcher3.Order.Columns.Presence", "Folder status"));
			_view.Columns.AddVisible(nameof(DisplayRow.Owners), L("Witcher3.Order.Columns.Owners", "NMM archive contributors"));
			_view.FocusedRowChanged += (sender, args) => UpdateSelectedDetails();

			_details = new MemoEdit { Dock = DockStyle.Bottom, Height = 82 };
			_details.Properties.ReadOnly = true;
			_status = new MemoEdit { Dock = DockStyle.Bottom, Height = 105 };
			_status.Properties.ReadOnly = true;
			_remasteredHealth = new MemoEdit { Dock = DockStyle.Bottom, Height = 220, Visible = false };
			_remasteredHealth.Properties.ReadOnly = true;
			Controls.Add(_grid);
			Controls.Add(_details);
			Controls.Add(_status);
			Controls.Add(_remasteredHealth);
			Controls.Add(commands);
			Controls.Add(description);
			_status.Text = L("Witcher3.Order.Initial", "Open this tab to read the game's current mod-folder order.");
			UpdateCommands();
		}

		private static string L(string key, string fallback) => LanguageManager.Get(key, fallback);

		private static SimpleButton Button(Control container, string key, string fallback, Action onClick)
		{
			var button = new SimpleButton { AutoSize = true, Text = L(key, fallback), Margin = new Padding(3, 0, 3, 0) };
			button.Click += (sender, args) => onClick();
			container.Controls.Add(button);
			return button;
		}

		/// <summary>Initialized by MainForm from the existing mode-specific W3R.5c lifecycle.</summary>
		public void Initialize(MainFormVM main, Func<bool> isActiveDocument)
		{
			if (main == null) throw new ArgumentNullException(nameof(main));
			if (_readService != null) throw new InvalidOperationException("The Witcher load-order page is already initialized.");
			_isActiveDocument = isActiveDocument ?? throw new ArgumentNullException(nameof(isActiveDocument));
			_lifecycle = main.Witcher3ModSettingsLifecycle ?? throw new InvalidOperationException(
				"The selected game has no Witcher 3 load-order lifecycle.");
			if (!String.Equals(_lifecycle.ModeId, main.GameMode.ModeId, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("The Witcher load-order page is bound to a different game mode.");
			_readService = new Witcher3LoadOrderViewService(_lifecycle,
				() => main.GameMode.GameModeEnvironmentInfo.InstallationPath,
				() => new NativeStateCaptureReader(main.ModManager.InstallationLog,
					main.ModManager.VirtualModActivator, null, main.PluginManager, main.GameMode)
					.CaptureForCollectionIndex(),
				root => Witcher3ComponentDiskScan.Scan(root));
			_nativeChanges = new Witcher3NativeLifecycleObserver(main.ModActivationMonitor, main.ModManager);
			_nativeChanges.Changed += NativeStateChanged;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing) { _nativeRefreshTimer.Stop(); _nativeRefreshTimer.Dispose(); }
			if (disposing && _nativeChanges != null)
			{
				_nativeChanges.Changed -= NativeStateChanged;
				_nativeChanges.Dispose();
				_nativeChanges = null;
			}
			base.Dispose(disposing);
		}

		private bool NativeInventoryStale => _snapshot != null && _nativeChanges != null &&
			Witcher3NativeRefreshPolicy.IsStale(_observedNativeRevision, _nativeChanges.Revision);

		private bool NativeOperationRunning => _nativeChanges?.HasRunningNativeTask == true;

		/// <summary>Called on any native worker thread; NEVER scans disk or touches controls here.</summary>
		private void NativeStateChanged(object sender, EventArgs args)
		{
			if (IsDisposed || Disposing || !IsHandleCreated ||
				Interlocked.Exchange(ref _nativeUiDispatchQueued, 1) != 0) return;
			try
			{
				BeginInvoke((MethodInvoker)(() =>
				{
					Interlocked.Exchange(ref _nativeUiDispatchQueued, 0);
					if (IsDisposed || Disposing) return;
					_nativeRefreshNeeded = true;
					_remasteredHealth.Visible = false;
					_status.Text = L("Witcher3.Order.NativeChanged",
						"NMM deployment or ownership changed. Refresh the folder inventory; unsaved edits are preserved but cannot be applied until refreshed.");
					UpdateCommands();
					if (_isActiveDocument?.Invoke() == true && _snapshot?.Editor.IsDirty != true)
					{
						_nativeRefreshTimer.Stop();
						_nativeRefreshTimer.Start();
					}
				}));
			}
			catch (InvalidOperationException) { Interlocked.Exchange(ref _nativeUiDispatchQueued, 0); }
		}

		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{
			if (settings == null) return;
			DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
		}

		/// <summary>Called on tab activation. Unsaved edits are never discarded automatically.</summary>
		public Task RefreshOnActivateAsync()
		{
			if (_snapshot?.Editor.IsDirty == true)
			{
				_status.Text = NativeInventoryStale
					? L("Witcher3.Order.NativeChanged", "NMM deployment or ownership changed. Refresh the folder inventory; unsaved edits are preserved but cannot be applied until refreshed.")
					: L("Witcher3.Order.UnsavedWarning",
					"You have unsaved folder-order edits. Refresh will discard the draft; apply or import from game explicitly.");
				return Task.CompletedTask;
			}
			return RefreshFromGameAsync(false);
		}

		private async Task RefreshFromGameAsync(bool confirmDiscard)
		{
			if (_busy || _readService == null || IsDisposed || Disposing) return;
			if (confirmDiscard && _snapshot?.Editor.IsDirty == true &&
				XtraMessageBox.Show(this, L("Witcher3.Order.DiscardConfirm",
					"Discard your unsaved changes and read mods.settings from the game again?"),
					Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
				return;
			_busy = true;
			// The old health observation must not appear current during a re-read.
			_remasteredHealth.Visible = false;
			_status.Text = L("Witcher3.Order.Reading", "Reading game folders, ownership, and mods.settings...");
			UpdateCommands();
			try
			{
				// A native task may finish while the read is running. Retry once rather
				// than publishing an index from two different native generations.
				for (int attempt = 0; attempt < 2; attempt++)
				{
					if (NativeOperationRunning) break;
					int before = _nativeChanges?.Revision ?? 0;
					// The explicitly selected external binary is re-inspected on refresh, but
					// never scanned for automatically and never used as a mutation authority.
					string selectedTool = _selectedMergerExecutable;
					string selectedRoot = _selectedMergerGameRoot;
					var read = await Task.Run(() =>
					{
						Witcher3LoadOrderViewSnapshot current = _readService.Read();
						Witcher3ScriptMergerToolObservation tool = current.RemasteredHealth != null &&
							selectedTool != null && String.Equals(selectedRoot, current.PhysicalGameRoot,
								StringComparison.OrdinalIgnoreCase)
							? Witcher3ExternalScriptMergerInspector.Inspect(selectedTool) : null;
						return Tuple.Create(current, tool);
					}).ConfigureAwait(true);
					if (IsDisposed || Disposing) return;
					if (!Witcher3NativeRefreshPolicy.CanApprove(before, _nativeChanges?.Revision ?? 0,
						NativeOperationRunning)) continue;
					_snapshot = read.Item1;
					if (read.Item2 != null)
						_mergerObservation = read.Item2;
					else
					{
						// A game switch cannot carry a tool observation into another root.
						_selectedMergerExecutable = null;
						_selectedMergerGameRoot = null;
						_mergerObservation = null;
					}
					_observedNativeRevision = before;
					_nativeRefreshNeeded = false;
					BindRows(null);
					UpdateStatus();
					return;
				}
				_nativeRefreshNeeded = true;
				_status.Text = L("Witcher3.Order.NativeReadingBusy",
					"A native mod operation changed during the folder scan. The previous view is stale; refresh after native work finishes.");
			}
			catch (Exception error)
			{
				_nativeRefreshNeeded = true;
				if (!IsDisposed && !Disposing)
					_status.Text = L("Witcher3.Order.ReadFailed", "Could not refresh the Witcher load-order view: ") + error.Message;
			}
			finally
			{
				if (!IsDisposed && !Disposing) { _busy = false; UpdateCommands(); }
			}
		}

		private void BindRows(string select)
		{
			if (_snapshot == null) return;
			string focused = select ?? SelectedFolder();
			var data = _snapshot.Editor.Rows.Select(row => new DisplayRow(row,
				_enabledText, _disabledText, _unconfiguredText)).ToList();
			_grid.BeginUpdate();
			try
			{
				_grid.DataSource = new BindingList<DisplayRow>(data);
				int index = data.FindIndex(x => String.Equals(x.FolderName, focused, StringComparison.OrdinalIgnoreCase));
				if (index >= 0) _view.FocusedRowHandle = _view.GetRowHandle(index);
			}
			finally { _grid.EndUpdate(); }
			UpdateSelectedDetails();
			UpdateCommands();
		}

		private string SelectedFolder() => (_view.GetFocusedRow() as DisplayRow)?.FolderName;

		private void Move(int direction)
		{
			if (_snapshot?.Editor == null || _busy) return;
			string folder = SelectedFolder();
			if (_snapshot.Editor.Move(folder, direction))
			{
				BindRows(folder);
				UpdateStatus();
			}
		}

		private void SetEnabled(bool enabled)
		{
			if (_snapshot?.Editor == null || _busy) return;
			string folder = SelectedFolder();
			if (_snapshot.Editor.SetGameEnabled(folder, enabled))
			{
				BindRows(folder);
				UpdateStatus();
			}
		}

		private void AddSelectedFolder()
		{
			if (_snapshot?.Editor == null || _busy) return;
			string folder = SelectedFolder();
			if (_snapshot.Editor.AddToOrder(folder))
			{
				BindRows(folder);
				UpdateStatus();
			}
		}

		private void AddAllObservedFolders()
		{
			if (_snapshot?.Editor == null || _busy) return;
			string focused = SelectedFolder();
			if (_snapshot.Editor.AddAllObservedToOrder() > 0)
			{
				BindRows(focused);
				UpdateStatus();
			}
		}

		private void RemoveSelectedAddition()
		{
			if (_snapshot?.Editor == null || _busy) return;
			string folder = SelectedFolder();
			if (_snapshot.Editor.RemoveDraftAddition(folder))
			{
				BindRows(folder);
				UpdateStatus();
			}
		}

		private void ResetDraft()
		{
			if (_snapshot?.Editor == null || _busy) return;
			string focused = SelectedFolder();
			_snapshot.Editor.ResetDraft();
			BindRows(focused);
			UpdateStatus();
		}

		private async Task ReviewAndApplyAsync(bool restoreDefaults = false)
		{
			if (_busy || _snapshot?.Editor == null || !_snapshot.Editor.CanEdit ||
				NativeInventoryStale || NativeOperationRunning ||
				(restoreDefaults ? !_snapshot.Editor.CanRestoreGameDefaults || _snapshot.Editor.IsDirty
					: !_snapshot.Editor.IsDirty)) return;
			Witcher3LoadOrderEditorSession editor = _snapshot.Editor;
			bool shouldRefresh = false;
			_busy = true;
			UpdateCommands();
			try
			{
				// Re-observe disk components, the current game root, the external file and
				// edition BEFORE approval. The publisher independently revalidates the
				// settings preimage and game root under native cross-process authority.
				int reviewedRevision = _nativeChanges?.Revision ?? 0;
				Witcher3LoadOrderViewSnapshot now = await Task.Run(() => _readService.Read()).ConfigureAwait(true);
				if (IsDisposed || Disposing) return;
				if (!Witcher3NativeRefreshPolicy.CanApprove(_observedNativeRevision,
					_nativeChanges?.Revision ?? 0, NativeOperationRunning) || reviewedRevision != _observedNativeRevision ||
					now.Lifecycle.HasPendingPublication || !now.Editor.CanEdit ||
					now.Lifecycle.Edition != _snapshot.Lifecycle.Edition ||
					!String.Equals(_snapshot.PhysicalGameRoot, now.PhysicalGameRoot, StringComparison.OrdinalIgnoreCase) ||
					!editor.MatchesObservedFile(now.Lifecycle.Settings) ||
					(!restoreDefaults && !editor.AddedFoldersStillObserved(now.Projection)))
				{
					XtraMessageBox.Show(this, L("Witcher3.Order.Stale",
						"The game edition, file content or recovery state has changed. No settings were written. Import from Game to review the latest state."),
						Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				Witcher3ModSettingsChangePreview preview = restoreDefaults
					? editor.PrepareRestoreGameDefaults() : editor.PrepareChange();
				if (preview.Status == Witcher3ModSettingsChangePlanStatus.Blocked)
				{
					XtraMessageBox.Show(this, preview.Diagnostic, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				if (preview.Status == Witcher3ModSettingsChangePlanStatus.Unchanged)
				{
					editor.ResetDraft();
					BindRows(SelectedFolder());
					UpdateStatus();
					return;
				}
				using (var dialog = new Witcher3LoadOrderReviewDialog(preview))
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return;
				}
				if (!Witcher3NativeRefreshPolicy.CanApprove(reviewedRevision,
					_nativeChanges?.Revision ?? 0, NativeOperationRunning))
				{
					XtraMessageBox.Show(this, L("Witcher3.Order.Stale",
						"The game edition, file content or recovery state has changed. No settings were written. Import from Game to review the latest state."),
						Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}

				Witcher3ModSettingsPublishResult published = await Task.Run(() =>
					_lifecycle.Publish(preview, true, _snapshot.PhysicalGameRoot)).ConfigureAwait(true);
				if (IsDisposed || Disposing) return;
				shouldRefresh = published.Status == Witcher3ModSettingsPublishStatus.Published ||
					published.Status == Witcher3ModSettingsPublishStatus.Unchanged;
				XtraMessageBox.Show(this, String.Format(L("Witcher3.Order.PublishResult",
					"Game settings update: {0}. {1}"), published.Status, published.Diagnostic ?? String.Empty),
					Text, MessageBoxButtons.OK, published.Status == Witcher3ModSettingsPublishStatus.Published
						? MessageBoxIcon.Information : MessageBoxIcon.Warning);
			}
			catch (Exception error)
			{
				if (!IsDisposed && !Disposing)
					XtraMessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				if (!IsDisposed && !Disposing) { _busy = false; UpdateCommands(); }
			}
			if (shouldRefresh && !IsDisposed && !Disposing)
				await RefreshFromGameAsync(false).ConfigureAwait(true);
		}

		private async Task ReviewRecoveryAsync()
		{
			if (_busy || _snapshot == null || !_snapshot.Lifecycle.HasPendingPublication) return;
			Witcher3ModSettingsRecoveryInspection state = _lifecycle.Refresh().Recovery;
			bool safe = Witcher3ModSettingsLifecycleService.SupportsMode(_lifecycle.ModeId) &&
				(state.Status == Witcher3ModSettingsRecoveryStatus.OriginalIntact ||
				state.Status == Witcher3ModSettingsRecoveryStatus.PublishedIntact);
			var text = new StringBuilder();
			text.AppendLine(String.Format(L("Witcher3.Order.RecoveryState", "Pending publication: {0}"), state.Status));
			text.AppendLine(state.Diagnostic);
			text.AppendLine(String.Format(L("Witcher3.Order.RecoveryBackup", "Original backup: {0}"), state.BackupPath ?? "-"));
			text.AppendLine(safe
				? L("Witcher3.Order.RecoveryApproval", "Acknowledge this verified state? The original backup is retained; no game file is rewritten.")
				: L("Witcher3.Order.RecoveryConflict", "Manual review is required. This marker cannot be acknowledged automatically."));
			if (XtraMessageBox.Show(this, text.ToString(), Text,
				safe ? MessageBoxButtons.YesNo : MessageBoxButtons.OK,
				MessageBoxIcon.Warning) != DialogResult.Yes || !safe) return;
			_busy = true;
			UpdateCommands();
			try { await Task.Run(() => _lifecycle.AcknowledgePendingPublication(true)).ConfigureAwait(true); }
			catch (Exception error)
			{
				if (!IsDisposed && !Disposing)
					XtraMessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally { if (!IsDisposed && !Disposing) { _busy = false; UpdateCommands(); } }
			if (!IsDisposed && !Disposing) await RefreshFromGameAsync(false).ConfigureAwait(true);
		}

		/// <summary>Manual file selection only. The external tool is never executed.</summary>
		private async Task InspectMergerAsync()
		{
			if (_busy || _snapshot?.RemasteredHealth == null) return;
			string root = _snapshot.PhysicalGameRoot;
			string executable;
			using (var dialog = new OpenFileDialog
			{
				Filter = L("Witcher3.Health.MergerFileFilter", "Executable files (*.exe)|*.exe"),
				Title = L("Witcher3.Health.MergerSelectTitle", "Select a Script Merger executable to inspect"),
				FileName = "WitcherScriptMerger.exe",
				CheckFileExists = true,
				Multiselect = false
			})
			{
				if (dialog.ShowDialog(this) != DialogResult.OK) return;
				executable = dialog.FileName;
			}
			_busy = true;
			UpdateCommands();
			try
			{
				Witcher3ScriptMergerToolObservation observed = await Task.Run(() =>
					Witcher3ExternalScriptMergerInspector.Inspect(executable)).ConfigureAwait(true);
				if (IsDisposed || Disposing || _snapshot?.RemasteredHealth == null ||
					!String.Equals(root, _snapshot.PhysicalGameRoot, StringComparison.OrdinalIgnoreCase)) return;
				_selectedMergerExecutable = executable;
				_selectedMergerGameRoot = root;
				_mergerObservation = observed;
				UpdateRemasteredHealth();
			}
			catch (Exception)
			{
				if (!IsDisposed && !Disposing)
					XtraMessageBox.Show(this, L("Witcher3.Health.MergerReadFailed",
						"Could not inspect the selected executable's file-version metadata."),
						Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally
			{
				if (!IsDisposed && !Disposing) { _busy = false; UpdateCommands(); }
			}
		}

		private void UpdateCommands()
		{
			Witcher3LoadOrderEditorSession editor = _snapshot?.Editor;
			Witcher3LoadOrderEditorRow selected = editor?.Find(SelectedFolder());
			bool editable = !_busy && editor?.CanEdit == true && !NativeInventoryStale && !NativeOperationRunning;
			bool inDraft = editable && selected?.DraftPosition.HasValue == true;
			int draftCount = editor?.DraftCount ?? 0;
			_up.Enabled = inDraft && selected.DraftPosition > 1;
			_down.Enabled = inDraft && selected.DraftPosition < draftCount;
			_enable.Enabled = inDraft && selected.DraftEnabled != true;
			_disable.Enabled = inDraft && selected.DraftEnabled != false;
			_add.Enabled = editable && editor.CanAddToOrder(selected?.FolderName);
			_addAll.Enabled = editable && editor.UnconfiguredCount > 0 &&
				EditorHasAddableFolders(editor);
			_removeAddition.Enabled = inDraft && selected.IsNewInDraft;
			_restoreDefaults.Enabled = editable && editor.CanRestoreGameDefaults && !editor.IsDirty;
			_resetDraft.Enabled = !_busy && editor?.IsDirty == true;
			_apply.Enabled = editable && editor.IsDirty;
			_reload.Enabled = !_busy && _readService != null;
			_recovery.Enabled = !_busy && _snapshot?.Lifecycle.HasPendingPublication == true;
			_inspectMerger.Visible = _snapshot?.RemasteredHealth != null;
			_inspectMerger.Enabled = !_busy && _snapshot?.RemasteredHealth != null;
		}

		private static bool EditorHasAddableFolders(Witcher3LoadOrderEditorSession editor) =>
			editor.Rows.Any(row => editor.CanAddToOrder(row.FolderName));

		private void UpdateSelectedDetails()
		{
			Witcher3LoadOrderEditorRow row = _snapshot?.Editor.Find(SelectedFolder());
			if (row == null) { _details.Text = String.Empty; UpdateCommands(); return; }
			var sb = new StringBuilder();
			sb.AppendLine(row.Source.GameRelativeFolderPath);
			sb.AppendLine(String.Format(L("Witcher3.Order.Details.Origin", "Origin: {0}; folder status: {1}"),
				row.Source.Origin, row.Source.Presence));
			if (row.Source.NativeContributors.Count == 0)
				sb.AppendLine(L("Witcher3.Order.Details.External", "No recorded NMM owner (manual/external or unknown)."));
			else
				foreach (Witcher3ComponentNativeOwner owner in row.Source.NativeContributors)
					sb.AppendLine(String.Format("{0} [{1}] - {2} files ({3} native winners)",
						owner.ModName, owner.InstallMethod, owner.FileCount, owner.WinningFileCount));
			_details.Text = sb.ToString();
			UpdateCommands();
		}

		private void UpdateStatus()
		{
			if (_snapshot == null) return;
			Witcher3LoadOrderEditorSession editor = _snapshot.Editor;
			var sb = new StringBuilder();
			sb.AppendLine(String.Format(L("Witcher3.Order.Status.Summary",
				"Edition: {0}; mods.settings: {1}; configured folders: {2}; unconfigured folders: {3}."),
				_snapshot.Lifecycle.Edition, _snapshot.Lifecycle.Settings.Status,
				editor.ConfiguredCount, editor.UnconfiguredCount));
			if (_snapshot.Lifecycle.HasPendingPublication)
				sb.AppendLine(L("Witcher3.Order.Status.Pending",
					"A previous settings write needs review. All changes are blocked until recovery is resolved."));
			if (!editor.CanEdit && (_snapshot.Lifecycle.Settings.IsLoaded ||
				_snapshot.Lifecycle.Settings.Status == Witcher3ModSettingsReadStatus.Missing))
				sb.AppendLine(L("Witcher3.Order.Status.EditionBlocked",
					"Editing requires the selected game mode's confirmed edition and no pending recovery."));
			if (_snapshot.Lifecycle.Settings.Status == Witcher3ModSettingsReadStatus.Missing)
				sb.AppendLine(L("Witcher3.Order.Status.Default",
					"No mods.settings exists. Select observed folders (or Add all), then Review and apply to create an explicit game order. Cancel leaves the file absent."));
			if (editor.UnconfiguredCount > 0)
				sb.AppendLine(L("Witcher3.Order.Status.Unconfigured",
					"Observed unconfigured mod folders may be added to your draft. External folders remain external; DLC has no mod-folder order."));
			if (_snapshot.Projection.Issues.Count > 0 || _snapshot.Projection.NativeIndex.Issues.Count > 0)
			{
				sb.AppendLine(String.Format(L("Witcher3.Order.Status.Issues",
					"Inventory/settings warnings: {0}. Missing or external folders are not adopted or deleted."),
					_snapshot.Projection.Issues.Count + _snapshot.Projection.NativeIndex.Issues.Count));
				foreach (string message in _snapshot.Projection.Issues.Select(x => x.Message)
					.Concat(_snapshot.Projection.NativeIndex.Issues.Select(x => x.Message)).Take(3))
					sb.AppendLine("- " + message);
			}
			if (NativeInventoryStale)
				sb.AppendLine(L("Witcher3.Order.NativeChanged",
					"NMM deployment or ownership changed. Refresh the folder inventory; unsaved edits are preserved but cannot be applied until refreshed."));
			if (editor.IsDirty)
				sb.AppendLine(L("Witcher3.Order.Status.Draft",
					"Unsaved draft: Review and apply will show the complete file and every changed priority/Enabled value."));
			if (editor.CanRestoreGameDefaults)
				sb.AppendLine(L("Witcher3.Order.Status.DefaultResetLimit",
					"Restore default ordering removes explicit Priority/Enabled overrides from all mod folders after review. Section headers and third-party metadata remain; the file is not deleted."));
			if (!editor.CanEdit && _snapshot.Lifecycle.Settings.Status != Witcher3ModSettingsReadStatus.Missing)
				sb.AppendLine(L("Witcher3.Order.Status.NoEdit",
					"Changes require an unambiguous mods.settings observation, matching game edition, and no pending publication."));
			_status.Text = sb.ToString();
			UpdateRemasteredHealth();
			UpdateCommands();
		}

		/// <summary>Diagnostic presentation only; no control in this panel performs a game-settings fix.</summary>
		private void UpdateRemasteredHealth()
		{
			Witcher3RemasteredHealthObservation health = _snapshot?.RemasteredHealth;
			_remasteredHealth.Visible = health != null;
			if (health == null) { _remasteredHealth.Text = String.Empty; return; }

			var text = new StringBuilder();
			text.AppendLine(L("Witcher3.Health.Header", "Remastered health checks (read-only; no automatic fixes):"));
			text.AppendLine(String.Format(L("Witcher3.Health.Observed",
				"DX12 executable: {0}; dx12user.settings: {1}; Enabled: {2}; EnabledLocal: {3}"),
				health.Dx12ExecutableObserved
					? L("Witcher3.Health.Present", "found") : L("Witcher3.Health.Absent", "not found"),
				health.SettingsStatus, health.GlobalMods, health.LocalMods));
			foreach (Witcher3RemasteredHealthIssue issue in health.Issues)
			{
				text.Append(issue.Severity == Witcher3RemasteredHealthSeverity.Warning
					? L("Witcher3.Health.WarningPrefix", "Warning: ")
					: L("Witcher3.Health.InfoPrefix", "Info: "));
				text.AppendLine(HealthMessage(issue.Code));
			}
			if (health.Issues.Count == 0)
				text.AppendLine(L("Witcher3.Health.SwitchesEnabled",
					"Both content-manager switches are enabled. This does not prove any mod loads in-game."));

			Witcher3RemasteredComponentHealthReport components = _snapshot.RemasteredComponents;
			if (components != null)
			{
				text.AppendLine(String.Format(L("Witcher3.Health.ComponentSummary",
					"Mod folders: {0} indexed, {1} observed; disk inventory: {2}."),
					components.ModFolderCount, components.ObservedModFolderCount,
					components.HasCompleteDiskInventory
						? L("Witcher3.Health.InventoryComplete", "complete")
						: L("Witcher3.Health.InventoryIncomplete", "incomplete")));
				foreach (Witcher3RemasteredComponentHealthIssue issue in components.Issues.Take(40))
				{
					text.Append(issue.Severity == Witcher3RemasteredHealthSeverity.Warning
						? L("Witcher3.Health.WarningPrefix", "Warning: ")
						: L("Witcher3.Health.InfoPrefix", "Info: "));
					if (issue.GameRelativeFolderPath != null)
						text.Append(String.Format(L("Witcher3.Health.ComponentAttribution",
							"{0} [{1}; NMM contributors: {2}]: "), issue.GameRelativeFolderPath,
							ComponentOriginText(issue.Origin), issue.NativeContributorCount));
					text.AppendLine(ComponentHealthMessage(issue));
				}
				if (components.Issues.Count > 40)
					text.AppendLine(String.Format(L("Witcher3.Health.ComponentMore",
						"{0} additional findings; inspect individual folders before changing anything."),
						components.Issues.Count - 40));
				else if (components.Issues.Count == 0)
					text.AppendLine(L("Witcher3.Health.ComponentClear",
						"No long mod-folder names or observed legacy merged output found in this inventory."));
			}
			text.AppendLine();
			text.AppendLine(L("Witcher3.Health.MergerHeader",
				"External Script Merger (manual, read-only file-version inspection):"));
			if (_mergerObservation == null || !String.Equals(_selectedMergerGameRoot,
				_snapshot.PhysicalGameRoot, StringComparison.OrdinalIgnoreCase))
				text.AppendLine(L("Witcher3.Health.MergerNotSelected",
					"No external tool selected. NMM does not search for or automatically run Script Merger."));
			else
			{
				Witcher3ScriptMergerToolObservation tool = _mergerObservation;
				text.AppendLine(String.Format(L("Witcher3.Health.MergerObserved",
					"{0}: {1}; declared family: {2}; declared version: {3}."),
					System.IO.Path.GetFileName(tool.ExecutablePath), tool.Status, tool.Family,
					String.IsNullOrEmpty(tool.VersionLabel) ? "-" : tool.VersionLabel));
				text.AppendLine(MergerAssessmentMessage(tool));
			}
			text.AppendLine(L("Witcher3.Health.MergerDisclaimer",
				"Executable metadata cannot prove compatibility, game targeting or the origin/encoding of existing merged output. No tool was run."));
			_remasteredHealth.Text = text.ToString();
		}

		private static string MergerAssessmentMessage(Witcher3ScriptMergerToolObservation tool)
		{
			switch (tool.Status)
			{
				case Witcher3ScriptMergerReadStatus.Missing:
					return L("Witcher3.Health.MergerMissing", "The selected executable no longer exists; this does not prove no other merger is installed.");
				case Witcher3ScriptMergerReadStatus.UnexpectedExecutable:
					return L("Witcher3.Health.MergerUnexpected", "The selected executable name is not a recognized Script Merger candidate; no compatibility claim was made.");
				case Witcher3ScriptMergerReadStatus.TooLarge:
					return L("Witcher3.Health.MergerTooLarge", "The selected executable exceeds the inspection size limit.");
				case Witcher3ScriptMergerReadStatus.ChangedDuringRead:
					return L("Witcher3.Health.MergerChanged", "The executable changed during inspection. Select it again after the update.");
				case Witcher3ScriptMergerReadStatus.ReadFailed:
					return L("Witcher3.Health.MergerReadFailed", "Could not inspect the selected executable's file-version metadata.");
			}
			switch (tool.Compatibility)
			{
				case Witcher3ScriptMergerCompatibility.LegacyReleaseWarning:
					return L("Witcher3.Health.MergerLegacy",
						"Metadata matches an original/pre-5.x release family. Do not use it for Remastered merges; verify the tool's actual release source.");
				case Witcher3ScriptMergerCompatibility.RemasteredSupportWithGogCaveat:
					return L("Witcher3.Health.MergerGogCaveat",
						"SM-FAE rc.4 introduced Remastered support, but its GOG game-directory detection was corrected in rc.5. Update and verify the release source.");
				case Witcher3ScriptMergerCompatibility.RemasteredSupportClaimed:
					return L("Witcher3.Health.MergerClaimed",
						"Metadata matches a release family/version advertising Remastered support, but this is NOT runtime compatibility certification.");
				default:
					return L("Witcher3.Health.MergerUnconfirmed",
						"Release family/version cannot be established from this executable's metadata. Confirm it with the tool publisher.");
			}
		}

		private static string ComponentOriginText(Witcher3RemasteredComponentOrigin origin)
		{
			switch (origin)
			{
				case Witcher3RemasteredComponentOrigin.NativeManaged:
					return L("Witcher3.Health.OriginNative", "NMM-owned files recorded");
				case Witcher3RemasteredComponentOrigin.ExternalObserved:
					return L("Witcher3.Health.OriginExternal", "external/manual");
				case Witcher3RemasteredComponentOrigin.MixedNativeAndExternal:
					return L("Witcher3.Health.OriginMixed", "mixed NMM and external files");
				default:
					return L("Witcher3.Health.OriginUnknown", "ownership unconfirmed");
			}
		}

		private static string ComponentHealthMessage(Witcher3RemasteredComponentHealthIssue issue)
		{
			switch (issue.Code)
			{
				case Witcher3RemasteredComponentHealthCode.InventoryIncomplete:
					return L("Witcher3.Health.ComponentInventoryPartial",
						"The folder scan is incomplete; the absence of other findings is not proof that all folders are safe.");
				case Witcher3RemasteredComponentHealthCode.FolderNameExceedsLimit:
					return String.Format(L("Witcher3.Health.ComponentLongName",
						"Folder name is {0} UTF-16 units long (conservative limit: 63); no rename was attempted."),
						issue.FolderNameLength);
				case Witcher3RemasteredComponentHealthCode.LegacyMergedFolderObserved:
					return L("Witcher3.Health.ComponentOldMerge",
						"Legacy-style Script Merger output folder observed. Confirm its producer/version supports Remastered 5.x; the folder name alone does not prove incompatibility.");
				case Witcher3RemasteredComponentHealthCode.LegacyMergedScriptsObserved:
					return String.Format(L("Witcher3.Health.ComponentOldScripts",
							"{0} merged .ws script file(s) observed. Script encoding/tool version was NOT verified; do not assume old merger output is compatible."),
						issue.ObservedScriptCount);
				case Witcher3RemasteredComponentHealthCode.LegacyMergePresenceUnconfirmed:
					return L("Witcher3.Health.ComponentMergeUnknown",
						"Native state references a legacy-style merge folder, but this scan did not confirm that it exists on disk.");
				default:
					return L("Witcher3.Health.ComponentUnknown", "Unrecognized component health finding.");
			}
		}

		private static string HealthMessage(Witcher3RemasteredHealthIssueCode code)
		{
			switch (code)
			{
				case Witcher3RemasteredHealthIssueCode.EditionUnconfirmed:
					return L("Witcher3.Health.EditionUnconfirmed", "This installation is not confirmed as Remastered 5.x by its executable evidence.");
				case Witcher3RemasteredHealthIssueCode.Dx12ExecutableMissing:
					return L("Witcher3.Health.Dx12Missing", "No DX12 game executable was found at bin/x64_dx12/witcher3.exe.");
				case Witcher3RemasteredHealthIssueCode.SettingsMissing:
					return L("Witcher3.Health.SettingsMissing", "dx12user.settings is absent (possible before first launch); local-mod switches cannot be inferred.");
				case Witcher3RemasteredHealthIssueCode.SettingsReadFailed:
					return L("Witcher3.Health.SettingsReadFailed", "The game's dx12user.settings could not be read; local-mod switches are unknown.");
				case Witcher3RemasteredHealthIssueCode.SettingsTooLarge:
					return L("Witcher3.Health.SettingsTooLarge", "dx12user.settings exceeds the diagnostic read limit; switch state is unknown.");
				case Witcher3RemasteredHealthIssueCode.SettingsUnsupportedEncoding:
					return L("Witcher3.Health.SettingsEncoding", "dx12user.settings has unsupported or malformed encoding; no switch value was guessed.");
				case Witcher3RemasteredHealthIssueCode.SettingsChangedDuringRead:
					return L("Witcher3.Health.SettingsChanged", "dx12user.settings changed during the read. Refresh to observe the latest state.");
				case Witcher3RemasteredHealthIssueCode.SettingsAmbiguous:
					return L("Witcher3.Health.SettingsAmbiguous", "The content-manager section or keys are duplicated/ambiguous; game switch state was not guessed.");
				case Witcher3RemasteredHealthIssueCode.GlobalModsDisabled:
					return L("Witcher3.Health.GlobalDisabled", "[ContentManager/Mods] Enabled is explicitly false/0. The game can suppress mods despite successful NMM deployment.");
				case Witcher3RemasteredHealthIssueCode.LocalModsDisabled:
					return L("Witcher3.Health.LocalDisabled", "[ContentManager/Mods] EnabledLocal is explicitly false/0. Local installed mods may not load.");
				case Witcher3RemasteredHealthIssueCode.GlobalModsNotConfirmed:
					return L("Witcher3.Health.GlobalUnknown", "The global Enabled switch is absent or unrecognized; do not infer enabled or disabled.");
				case Witcher3RemasteredHealthIssueCode.LocalModsNotConfirmed:
					return L("Witcher3.Health.LocalUnknown", "The EnabledLocal switch is absent or unrecognized; do not infer enabled or disabled.");
				default:
					return L("Witcher3.Health.Unknown", "Unrecognized Remastered health finding.");
			}
		}

		private sealed class DisplayRow
		{
			internal DisplayRow(Witcher3LoadOrderEditorRow row, string enabled, string disabled, string unconfigured)
			{
				FolderName = row.FolderName;
				DraftPosition = row.DraftPosition?.ToString() ?? "-";
				GamePriority = row.GamePriority?.ToString() ?? "-";
				GameEnabled = !row.DraftPosition.HasValue ? unconfigured : !row.DraftEnabled.HasValue ? unconfigured
					: row.DraftEnabled.Value ? enabled : disabled;
				Origin = row.Source.Origin.ToString();
				Presence = row.Source.Presence.ToString();
				Owners = row.OwnerNames;
			}
			public string DraftPosition { get; }
			public string GamePriority { get; }
			public string FolderName { get; }
			public string GameEnabled { get; }
			public string Origin { get; }
			public string Presence { get; }
			public string Owners { get; }
		}
	}
}
