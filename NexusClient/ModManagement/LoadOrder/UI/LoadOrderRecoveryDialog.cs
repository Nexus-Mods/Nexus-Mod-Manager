using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.LoadOrder.UI
{
	/// <summary>Explicit native-save and retained-folder recovery, with conflict states disabling every destructive choice.</summary>
	internal sealed class LoadOrderRecoveryDialog : ManagedFontXtraForm
	{
		private readonly NativeLoadOrderService _service;
		private readonly string _profile;
		private readonly LabelControl _native, _status;
		private readonly ComboBoxEdit _batches;
		private readonly MemoEdit _folderDetails;
		private readonly SimpleButton _acknowledge, _restore, _publish, _finishCleanup, _restoreContent, _close, _cancel, _refresh;
		private readonly LayoutControlItem _progress;
		private NativeLoadOrderRecovery _pending;
		private LoadOrderFolderRecovery _folder;
		private CancellationTokenSource _cancellation;
		private bool _busy, _binding, _released;

		/// <summary>Creates a fully skinned recovery surface; inspection starts after its first paint.</summary>
		internal LoadOrderRecoveryDialog(NativeLoadOrderService service, string profile)
		{
			_service = service ?? throw new ArgumentNullException(nameof(service)); _profile = profile;
			Text = T("RecoveryTitle", "Load Order recovery"); ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
			AutoScaleMode = AutoScaleMode.Font; MinimumSize = new Size(620, 460); ClientSize = new Size(840, 640);
			var root = LoadOrderUiLayout.Table(new[] { 100D }, new[] { 0D, 0D, 0D, 0D, 100D, 0D, 0D, 0D, 0D }); Controls.Add(root);
			LoadOrderUiLayout.Put(root, LoadOrderUiLayout.Label(String.Format(T("RecoveryIntroduction", "Game profile: {0}. Inspect the evidence before choosing a recovery action. NMM will not overwrite unknown files or changed ownership."), profile)), 0);
			_native = LoadOrderUiLayout.Label(T("ReadingRecovery", "Reviewing recovery information…")); LoadOrderUiLayout.Put(root, _native, 1);
			LayoutControlGroup nativeActions = LoadOrderUiLayout.Group(root, 2, new[] { 100D, 0D, 0D, 0D });
			_acknowledge = Command(T("RecoveryKeep", "Keep current order"), () => RecoverNativeAsync(NativeLoadOrderRecoveryAction.Acknowledge));
			_restore = Command(T("RecoveryRestore", "Restore original order"), () => RecoverNativeAsync(NativeLoadOrderRecoveryAction.RestoreOriginal));
			_publish = Command(T("RecoveryComplete", "Complete proposed order"), () => RecoverNativeAsync(NativeLoadOrderRecoveryAction.CompletePublication));
			LoadOrderUiLayout.Put(root, _acknowledge, 0, 1, parent: nativeActions); LoadOrderUiLayout.Put(root, _restore, 0, 2, parent: nativeActions); LoadOrderUiLayout.Put(root, _publish, 0, 3, parent: nativeActions);
			_batches = new ComboBoxEdit(); _batches.Properties.TextEditStyle = TextEditStyles.DisableTextEditor; _batches.Properties.NullValuePrompt = T("ChooseBatch", "Choose a retained cleanup batch");
			LoadOrderUiLayout.Put(root, _batches, 3, caption: T("RetainedContent", "Retained folder content"));
			_batches.EditValueChanged += async (sender, args) => { if (!_binding && !_busy) await RunAsync(InspectFolderAsync); };
			_folderDetails = LoadOrderUiLayout.Memo(); LoadOrderUiLayout.Put(root, _folderDetails, 4);
			LayoutControlGroup folderActions = LoadOrderUiLayout.Group(root, 5, new[] { 100D, 0D, 0D });
			_finishCleanup = Command(T("RecoveryFinishCleanup", "Finish cleanup"), () => RecoverFolderAsync(false));
			_restoreContent = Command(T("RecoveryRestoreContent", "Put retained content back"), () => RecoverFolderAsync(true));
			LoadOrderUiLayout.Put(root, _finishCleanup, 0, 1, parent: folderActions); LoadOrderUiLayout.Put(root, _restoreContent, 0, 2, parent: folderActions);
			_status = LoadOrderUiLayout.Label(String.Empty); LoadOrderUiLayout.Put(root, _status, 6);
			_progress = LoadOrderUiLayout.Put(root, new MarqueeProgressBarControl(), 7);
			LayoutControlGroup footer = LoadOrderUiLayout.Group(root, 8, new[] { 100D, 0D, 0D, 0D });
			_refresh = Command(T("Refresh", "Refresh"), () => RunAsync(ReloadAsync));
			_cancel = Command(T("Cancel", "Cancel operation"), () => { _cancellation?.Cancel(); return Task.CompletedTask; });
			_close = new SimpleButton { Text = T("Close", "Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
			LoadOrderUiLayout.Put(root, _refresh, 0, 1, parent: footer); LoadOrderUiLayout.Put(root, _cancel, 0, 2, parent: footer); LoadOrderUiLayout.Put(root, _close, 0, 3, parent: footer); CancelButton = _close;
			Shown += async (sender, args) => await RunAsync(ReloadAsync);
			FormClosing += (sender, args) => { if (_busy) { args.Cancel = true; _status.Text = T("RecoveryBusy", "Finish or cancel recovery before closing this window."); } };
			UpdateCommands();
		}

		/// <summary>Re-inspects the selected game profile and discovers existing durable folder intents.</summary>
		private async Task ReloadAsync(CancellationToken token)
		{
			string selected = (_batches.SelectedItem as BatchChoice)?.Id;
			var state = await Task.Run(() => Tuple.Create(_service.InspectRecovery(_profile, token), _service.ListFolderRecoveryIds()), token);
			if (_released) return;
			_pending = state.Item1;
			string message = _pending.State == NativeLoadOrderRecoveryState.None ? T("RecoveryNone", "No interrupted load-order save for this profile.")
				: _pending.State == NativeLoadOrderRecoveryState.OriginalIntact ? T("RecoveryOriginal", "The original order is intact. Keep it, or complete the proposed order.")
				: _pending.State == NativeLoadOrderRecoveryState.PublishedIntact ? T("RecoveryPublished", "The proposed order is intact. Keep it, or restore the original order.")
				: _pending.State == NativeLoadOrderRecoveryState.Interrupted ? T("RecoveryInterrupted", "The save was interrupted. Choose the original or proposed order to align the game file and NMM history.")
				: T("RecoveryConflict", "The current files or recovery evidence changed. Automatic replacement is blocked. Review the diagnostic and retained original.");
			_native.Text = message + (String.IsNullOrEmpty(_pending.Diagnostic) ? String.Empty : Environment.NewLine + _pending.Diagnostic) +
				(String.IsNullOrEmpty(_pending.BackupPath) ? String.Empty : Environment.NewLine + String.Format(T("Backup", "Original backup: {0}"), _pending.BackupPath));
			_binding = true;
			try
			{
				_batches.Properties.Items.Clear();
				foreach (string id in state.Item2) _batches.Properties.Items.Add(new BatchChoice(id));
				_batches.SelectedItem = _batches.Properties.Items.Cast<BatchChoice>().FirstOrDefault(item => item.Id == selected) ?? (_batches.Properties.Items.Count == 1 ? _batches.Properties.Items[0] : null);
			}
			finally { _binding = false; }
			await InspectFolderAsync(token);
		}

		/// <summary>Shows the contents, locations and conflict state of one explicitly selected retained batch.</summary>
		private async Task InspectFolderAsync(CancellationToken token)
		{
			_folder = null; string id = (_batches.SelectedItem as BatchChoice)?.Id;
			if (id == null) { _folderDetails.Text = _batches.Properties.Items.Count == 0 ? T("RecoveryNoFolders", "No retained cleanup batches were found.") : T("ChooseBatch", "Choose a retained cleanup batch"); return; }
			LoadOrderFolderRecovery observed = await Task.Run(() => _service.InspectFolderRecovery(id, token), token);
			if (_released) return;
			_folder = observed;
			_folderDetails.Text = String.Format(T("RecoveryFolderSummary", "Game profile: {0}{1}State: {2}{1}Retained location: {3}{1}{1}"), observed.GameProfile, Environment.NewLine,
				T("FolderState." + observed.State, observed.State == LoadOrderFolderRecoveryState.OriginalIntact ? "Content is in the original folder" : observed.State == LoadOrderFolderRecoveryState.RetainedIntact ? "Content is retained outside the package folder" : observed.State == LoadOrderFolderRecoveryState.Mixed ? "Cleanup was interrupted; copies are in mixed locations" : "Conflicting copies, content or ownership"), observed.Directory)
				+ String.Join(Environment.NewLine, observed.Items.Select(item => item.Name)) + Environment.NewLine + Environment.NewLine + observed.Diagnostic;
		}

		/// <summary>Checks that the displayed native evidence is still the evidence associated with the chosen recovery action.</summary>
		private async Task RecoverNativeAsync(NativeLoadOrderRecoveryAction action)
		{
			if (_busy || _pending == null || _pending.State == NativeLoadOrderRecoveryState.None || _pending.State == NativeLoadOrderRecoveryState.Conflict) return;
			NativeLoadOrderRecovery reviewed = _pending;
			await RunAsync(async token =>
			{
				NativeLoadOrderRecovery current = await Task.Run(() => _service.InspectRecovery(_profile, token), token);
				if (current.State != reviewed.State || current.BackupPath != reviewed.BackupPath) throw new InvalidOperationException(T("RecoveryChanged", "Recovery evidence changed. Inspect it again before choosing an action."));
				NativeLoadOrderRecovery result = await Task.Run(() => _service.Recover(_profile, action, true, token), token);
				await ReloadAsync(token); _status.Text = result.State == NativeLoadOrderRecoveryState.None ? T("RecoveryDone", "Load-order recovery completed.") : result.Diagnostic;
			});
		}

		/// <summary>Explicitly confirms restoration because returning unexpected content can reintroduce the game's order-reset condition.</summary>
		private async Task RecoverFolderAsync(bool restore)
		{
			if (_busy || _folder == null || _folder.State == LoadOrderFolderRecoveryState.Conflict) return;
			if (restore && XtraMessageBox.Show(this, T("RestoreFolderPrompt", "Putting these files or folders back can cause the game to reset its custom load order. Restore the retained content?"),
				T("RecoveryTitle", "Load Order recovery"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
			LoadOrderFolderRecovery reviewed = _folder;
			await RunAsync(async token =>
			{
				LoadOrderFolderRecovery current = await Task.Run(() => _service.InspectFolderRecovery(reviewed.RecoveryId, token), token);
				if (current.State != reviewed.State || !current.Items.Select(item => item.Name + "|" + item.ContentIdentity).SequenceEqual(reviewed.Items.Select(item => item.Name + "|" + item.ContentIdentity)))
					throw new InvalidOperationException(T("RecoveryChanged", "Recovery evidence changed. Inspect it again before choosing an action."));
				await Task.Run(() => _service.RecoverFolderRepairs(reviewed.RecoveryId, restore, true, token), token);
				await ReloadAsync(token); _status.Text = restore ? T("ContentRestored", "Retained content restored. Refresh the order to inspect the resulting folder problems.") : T("CleanupCompleted", "Folder cleanup completed. Retained content remains available here.");
			});
		}

		/// <summary>Serializes recovery work and reports any partial/unknown state without closing the review window.</summary>
		private async Task RunAsync(Func<CancellationToken, Task> action)
		{
			if (_busy || _released) return;
			_busy = true; _cancellation = new CancellationTokenSource(); _status.Text = T("ReadingRecovery", "Reviewing recovery information…"); UpdateCommands();
			try { await action(_cancellation.Token); }
			catch (OperationCanceledException) { if (!_released) { _pending = null; _folder = null; _status.Text = T("RecoveryCancelled", "Recovery cancelled. Refresh to inspect the current state before choosing another action."); } }
			catch (Exception error) when (!(error is OutOfMemoryException))
			{
				if (!_released) { _pending = null; _folder = null; Trace.TraceWarning("Load Order recovery: {0}", error); _status.Text = error.Message; }
			}
			finally { _cancellation.Dispose(); _cancellation = null; _busy = false; if (!_released) UpdateCommands(); }
		}

		/// <summary>Allows actions only for the recognized evidence currently displayed.</summary>
		private void UpdateCommands()
		{
			bool native = !_busy && _pending != null && _pending.State != NativeLoadOrderRecoveryState.None && _pending.State != NativeLoadOrderRecoveryState.Conflict;
			_acknowledge.Enabled = native && _pending.State != NativeLoadOrderRecoveryState.Interrupted; _restore.Enabled = _publish.Enabled = native;
			_finishCleanup.Enabled = !_busy && (_folder?.State == LoadOrderFolderRecoveryState.OriginalIntact || _folder?.State == LoadOrderFolderRecoveryState.Mixed);
			_restoreContent.Enabled = !_busy && (_folder?.State == LoadOrderFolderRecoveryState.RetainedIntact || _folder?.State == LoadOrderFolderRecoveryState.Mixed);
			_batches.Enabled = !_busy; _refresh.Enabled = !_busy; _close.Enabled = !_busy; _cancel.Enabled = _busy; _progress.Visibility = _busy ? LayoutVisibility.Always : LayoutVisibility.Never;
		}

		/// <summary>Applies the active font/density settings to the complete DevExpress dialog.</summary>
		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{ if (settings != null) { Font = settings.Font; DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings); } }
		/// <summary>Creates an asynchronous DevExpress recovery command.</summary>
		private static SimpleButton Command(string text, Func<Task> action)
		{ var button = new SimpleButton { Text = text, AutoSize = true, AccessibleName = text }; button.Click += async (sender, args) => await action(); return button; }
		/// <summary>Uses the shared localized page resource namespace.</summary>
		private static string T(string key, string fallback) => LoadOrderControl.T(key, fallback);
		/// <summary>Releases cancellation after the owned dialog finishes; active work blocks normal close.</summary>
		protected override void Dispose(bool disposing)
		{ if (disposing) { _released = true; _cancellation?.Cancel(); } base.Dispose(disposing); }
		/// <summary>Stable batch identity with a short readable selector caption; contents are displayed before action.</summary>
		private sealed class BatchChoice
		{
			/// <summary>Retains the complete opaque recovery identity.</summary>
			internal BatchChoice(string id) { Id = id; }
			internal string Id { get; }
			/// <summary>Displays a compact identifier without guessing a timestamp from a GUID.</summary>
			public override string ToString() => String.Format(T("RecoveryBatch", "Cleanup batch {0}"), Id.Substring(0, 8));
		}
	}
}
