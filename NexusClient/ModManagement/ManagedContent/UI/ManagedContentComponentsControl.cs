using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using Nexus.Client.UI;
using Nexus.Client.Util.Localization;
using WeifenLuo.WinFormsUI.Docking;

namespace Nexus.Client.ModManagement.ManagedContent.UI
{
	/// <summary>Capability-driven component observations with explicit opt-in reviewed order editing.</summary>
	public sealed class ManagedContentComponentsControl : ManagedFontDockContent
	{
		private readonly GridControl _grid;
		private readonly GridView _view;
		private readonly GridColumn _orderStateColumn;
		private readonly GridColumn _priorityColumn;
		private readonly GridColumn _enabledColumn;
		private readonly MemoEdit _status;
		private readonly MemoEdit _details;
		private readonly SimpleButton _refresh;
		private readonly SimpleButton _editOrder;
		private readonly SimpleButton _recovery;
		private ManagedContentOrderService _orderService;
		private IniOrderRecoveryInspection _pending;
		private readonly System.Windows.Forms.Timer _refreshTimer;
		private ManagedContentComponentViewService _reader;
		private NativeComponentLifecycleObserver _nativeChanges;
		private ManagedContentComponentViewSnapshot _snapshot;
		private Func<bool> _isActive;
		private int _dispatchQueued;
		private int _observedRevision = -1;
		private volatile bool _refreshNeeded = true;
		private bool _busy;
		private bool _released;

		/// <summary>Creates themed controls without capturing native state or scanning game folders.</summary>
		public ManagedContentComponentsControl()
		{
			Name = "ManagedContentComponentsControl";
			Text = L("ManagedContent.Components.Title", "Components");
			DockAreas = DockAreas.Document;
			_refreshTimer = new System.Windows.Forms.Timer { Interval = 450 };
			_refreshTimer.Tick += (sender, args) =>
			{
				_refreshTimer.Stop();
				if (_refreshNeeded && !_busy && _isActive?.Invoke() == true)
					_ = RefreshOnActivateAsync();
			};
			var description = new LabelControl
			{
				Dock = DockStyle.Top, AutoSizeMode = LabelAutoSizeMode.Vertical, Padding = new Padding(7), UseMnemonic = false,
				Text = L("ManagedContent.Components.Introduction", "Deployed component folders, their NMM contributors and observed disk presence. Refresh to observe manual changes.")
			};
			description.Appearance.TextOptions.WordWrap = WordWrap.Wrap;
			var commands = new FlowLayoutPanel { Dock = DockStyle.Top, AutoScroll = true, WrapContents = false, Height = 42, Padding = new Padding(5) };
			_refresh = new SimpleButton { AutoSize = true, Text = L("ManagedContent.Components.Refresh", "Refresh") };
			_refresh.Click += (sender, args) => _ = RefreshOnActivateAsync();
			commands.Controls.Add(_refresh);
			_editOrder = new SimpleButton { AutoSize = true, Visible = false, Text = L("ManagedContent.Order.Edit", "Edit game order") };
			_editOrder.Click += (sender, args) => _ = EditOrderAsync();
			_recovery = new SimpleButton { AutoSize = true, Visible = false, Text = L("ManagedContent.Order.Recovery", "Review pending recovery") };
			_recovery.Click += (sender, args) => _ = ReviewRecoveryAsync();
			commands.Controls.Add(_editOrder);
			commands.Controls.Add(_recovery);
			_grid = new GridControl { Dock = DockStyle.Fill };
			_view = new GridView(_grid);
			_grid.MainView = _view;
			_grid.ViewCollection.Add(_view);
			_view.OptionsBehavior.Editable = false;
			_view.OptionsBehavior.AutoPopulateColumns = false;
			_view.OptionsView.ShowGroupPanel = false;
			_view.OptionsView.ShowIndicator = false;
			_view.OptionsSelection.MultiSelect = false;
			_view.Columns.AddVisible(nameof(ComponentRow.Folder), L("ManagedContent.Components.Columns.Folder", "Component folder"));
			_view.Columns.AddVisible(nameof(ComponentRow.Kind), L("ManagedContent.Components.Columns.Kind", "Type"));
			_view.Columns.AddVisible(nameof(ComponentRow.Origin), L("ManagedContent.Components.Columns.Origin", "Source"));
			_view.Columns.AddVisible(nameof(ComponentRow.Presence), L("ManagedContent.Components.Columns.Presence", "Disk presence"));
			_view.Columns.AddVisible(nameof(ComponentRow.FileCount), L("ManagedContent.Components.Columns.Files", "Files"));
			_view.Columns.AddVisible(nameof(ComponentRow.ContributorCount), L("ManagedContent.Components.Columns.Owners", "NMM contributors"));
			_orderStateColumn = _view.Columns.AddVisible(nameof(ComponentRow.OrderConfiguration), L("ManagedContent.Order.Columns.Configuration", "Order configuration"));
			_priorityColumn = _view.Columns.AddVisible(nameof(ComponentRow.Priority), L("ManagedContent.Order.Columns.Priority", "Configured priority"));
			_enabledColumn = _view.Columns.AddVisible(nameof(ComponentRow.GameEnabled), L("ManagedContent.Order.Columns.Enabled", "Game enabled"));
			_orderStateColumn.Visible = _priorityColumn.Visible = _enabledColumn.Visible = false;
			_view.FocusedRowChanged += (sender, args) => UpdateDetails();
			_details = new MemoEdit { Dock = DockStyle.Bottom, Height = 110 };
			_details.Properties.ReadOnly = true;
			_status = new MemoEdit { Dock = DockStyle.Bottom, Height = 110 };
			_status.Properties.ReadOnly = true;
			_status.Text = L("ManagedContent.Components.Initial", "Open this tab to read the component inventory.");
			Controls.Add(_grid);
			Controls.Add(_details);
			Controls.Add(_status);
			Controls.Add(commands);
			Controls.Add(description);
		}

		/// <summary>Uses the existing localization fallback for every static page caption.</summary>
		private static string L(string key, string fallback) => LanguageManager.Get(key, fallback);

		/// <summary>Binds to current native services without retaining an InstallLog across a native reload.</summary>
		public void Initialize(MainFormVM main, Func<bool> isActiveDocument)
		{
			if (main == null) throw new ArgumentNullException(nameof(main));
			if (_reader != null) throw new InvalidOperationException("The component page is already initialized.");
			_isActive = isActiveDocument ?? throw new ArgumentNullException(nameof(isActiveDocument));
			_reader = new ManagedContentComponentViewService(main.GameMode,
				() => new NativeStateCaptureReader(main.ModManager.InstallationLog,
					main.ModManager.VirtualModActivator, null, main.PluginManager, main.GameMode).CaptureForCollectionIndex(), () => main.EnvironmentInfo.PersonalDataFolderPath);
			_orderStateColumn.Visible = _priorityColumn.Visible = _enabledColumn.Visible = _reader.HasOrderDocument;
			_orderService = main.ManagedContentOrder;
			_editOrder.Visible = _recovery.Visible = _orderService != null;
			UpdateCommands();
			_nativeChanges = new NativeComponentLifecycleObserver(main.ModActivationMonitor, main.ModManager);
			_nativeChanges.Changed += NativeStateChanged;
		}

		/// <summary>Marks the view stale from native callbacks, marshaling display work without reading disk there.</summary>
		private void NativeStateChanged(object sender, EventArgs args)
		{
			_refreshNeeded = true;
			if (_released || IsDisposed || Disposing || !IsHandleCreated || Interlocked.Exchange(ref _dispatchQueued, 1) != 0) return;
			try
			{
				BeginInvoke((MethodInvoker)(() =>
				{
					Interlocked.Exchange(ref _dispatchQueued, 0);
					if (_released || IsDisposed || Disposing) return;
					// A callback queued before a completed refresh need not invalidate that newer observation.
					if (_snapshot != null && _observedRevision == _nativeChanges.Revision && !_refreshNeeded) return;
					_refreshNeeded = true;
					UpdateCommands();
					_status.Text = L("ManagedContent.Components.Stale", "Mod deployment changed. The displayed inventory needs a refresh.");
					ScheduleRefresh();
				}));
			}
			catch (InvalidOperationException) { Interlocked.Exchange(ref _dispatchQueued, 0); }
		}

		/// <summary>Coalesces native event bursts and refreshes only an active, idle component page.</summary>
		private void ScheduleRefresh()
		{
			if (_released || _busy || !_refreshNeeded || _isActive?.Invoke() != true) return;
			_refreshTimer.Stop();
			_refreshTimer.Start();
		}

		/// <summary>Performs one serialized background read, rejecting observations crossed by native operations.</summary>
		public async Task RefreshOnActivateAsync()
		{
			if (_released || IsDisposed || Disposing || _busy || _reader == null) return;
			_refreshTimer.Stop();
			NativeComponentLifecycleObserver changes = _nativeChanges;
			if (changes.HasRunningNativeTask)
			{
				_refreshNeeded = true;
				_status.Text = L("ManagedContent.Components.NativeBusy", "A mod operation is running. The inventory will refresh when it finishes.");
				return;
			}
			_busy = true;
			_refresh.Enabled = false;
			UpdateCommands();
			_status.Text = L("ManagedContent.Components.Reading", "Reading component folders and NMM contributors...");
			bool retry = false;
			try
			{
				for (int attempt = 0; attempt < 2; attempt++)
				{
					if (changes.HasRunningNativeTask) break;
					int revision = changes.Revision;
					var observed = await Task.Run(() => Tuple.Create(_reader.Read(), _orderService?.InspectRecovery())).ConfigureAwait(true);
					ManagedContentComponentViewSnapshot current = observed.Item1;
					if (_released || IsDisposed || Disposing) return;
					if (revision != changes.Revision || changes.HasRunningNativeTask) continue;
					_snapshot = current;
					_pending = observed.Item2;
					_observedRevision = revision;
					_refreshNeeded = false;
					BindRows();
					UpdateStatus();
					return;
				}
				_refreshNeeded = true;
				retry = !changes.HasRunningNativeTask;
				_status.Text = L("ManagedContent.Components.ChangedDuringRead", "Mod deployment changed during the read. The displayed inventory is stale; it will refresh after mod operations finish.");
			}
			catch (Exception error)
			{
				_refreshNeeded = true;
				if (!_released && !IsDisposed && !Disposing)
					_status.Text = L("ManagedContent.Components.ReadFailed", "Could not refresh the component inventory: ") + error.Message;
			}
			finally
			{
				if (!_released && !IsDisposed && !Disposing)
				{
					_busy = false;
					_refresh.Enabled = true;
					UpdateCommands();
					if (retry) ScheduleRefresh();
				}
			}
		}

		/// <summary>Requires accepted native observations and a usable file before offering draft editing.</summary>
		private void UpdateCommands()
		{
			bool current = !_busy && !_released && !_refreshNeeded && _nativeChanges != null &&
				!_nativeChanges.HasRunningNativeTask && _observedRevision == _nativeChanges.Revision;
			_editOrder.Enabled = current && _orderService != null && _pending?.Status == IniOrderRecoveryStatus.None &&
				_snapshot?.Order?.IsLoaded == true && _snapshot.Order.Document?.IsUnambiguous == true;
			_recovery.Enabled = current && _orderService != null && _pending != null && _pending.Status != IniOrderRecoveryStatus.None;
		}

		/// <summary>Rejects modal review when the native inventory or exact source file changed.</summary>
		private bool ReviewIsCurrent(int revision) => !_released && !IsDisposed && !Disposing &&
			_nativeChanges.Revision == revision && !_nativeChanges.HasRunningNativeTask;

		/// <summary>Drafts configured sections, re-observes before approval, then publishes through fresh native authority.</summary>
		private async Task EditOrderAsync()
		{
			if (!_editOrder.Enabled) return;
			_busy = true;
			_refresh.Enabled = false;
			UpdateCommands();
			try
			{
				int revision = _nativeChanges.Revision;
				var fresh = await Task.Run(() => Tuple.Create(_reader.Read(), _orderService.InspectRecovery())).ConfigureAwait(true);
				if (!ReviewIsCurrent(revision) || fresh.Item2.Status != IniOrderRecoveryStatus.None) return;
				ManagedContentComponentViewSnapshot source = fresh.Item1;
				if (source.Order?.IsLoaded != true || source.Order.Document?.IsUnambiguous != true) return;
				IniOrderChangePreview preview;
				using (var editor = new IniOrderEditorDialog(source.Order))
				{
					if (editor.ShowDialog(this) != DialogResult.OK) return;
					preview = editor.Preview;
				}
				if (!ReviewIsCurrent(revision)) return;
				var latest = await Task.Run(() => Tuple.Create(_reader.Read(), _orderService.InspectRecovery())).ConfigureAwait(true);
				if (!ReviewIsCurrent(revision) || latest.Item2.Status != IniOrderRecoveryStatus.None ||
					!String.Equals(source.PhysicalRoot, latest.Item1.PhysicalRoot, StringComparison.OrdinalIgnoreCase) ||
					!String.Equals(source.Order.FilePath, latest.Item1.Order?.FilePath, StringComparison.OrdinalIgnoreCase) ||
					!String.Equals(source.Order.ContentSha256, latest.Item1.Order?.ContentSha256, StringComparison.OrdinalIgnoreCase))
				{
					XtraMessageBox.Show(this, L("ManagedContent.Order.StaleReview", "The game, file or native state changed during review. Refresh and review again."),
						Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				using (var review = new IniOrderReviewDialog(preview))
					if (review.ShowDialog(this) != DialogResult.OK) return;
				if (!ReviewIsCurrent(revision)) return;
				IniOrderPublishResult result = await Task.Run(() => _orderService.Publish(preview, source.PhysicalRoot, true)).ConfigureAwait(true);
				if (_released || IsDisposed || Disposing) return;
				XtraMessageBox.Show(this, String.Format(L("ManagedContent.Order.PublishResult", "Game settings update: {0}. {1}"),
					L("ManagedContent.Order.PublishStatus." + result.Status, result.Status.ToString()), result.Diagnostic ?? String.Empty) + (String.IsNullOrEmpty(result.BackupPath) ? String.Empty : Environment.NewLine +
					String.Format(L("ManagedContent.Order.RecoveryBackup", "Original backup: {0}"), result.BackupPath)),
					Text, MessageBoxButtons.OK, result.Status == IniOrderPublishStatus.Published ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
			}
			catch (Exception error)
			{
				if (!_released && !IsDisposed && !Disposing) XtraMessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				if (!_released && !IsDisposed && !Disposing)
				{
					_busy = false;
					_refresh.Enabled = true;
					_refreshNeeded = true;
					await RefreshOnActivateAsync().ConfigureAwait(true);
				}
			}
		}

		/// <summary>Displays verified recovery evidence and asks explicitly before acknowledgement; never overwrites game bytes.</summary>
		private async Task ReviewRecoveryAsync()
		{
			if (!_recovery.Enabled) return;
			_busy = true;
			_refresh.Enabled = false;
			UpdateCommands();
			try
			{
				string root = _snapshot.PhysicalRoot;
				int revision = _nativeChanges.Revision;
				IniOrderRecoveryInspection state = await Task.Run(() => _orderService.InspectRecovery()).ConfigureAwait(true);
				if (!ReviewIsCurrent(revision)) return;
				bool safe = state.Status == IniOrderRecoveryStatus.OriginalIntact || state.Status == IniOrderRecoveryStatus.PublishedIntact;
				string text = state.Diagnostic + Environment.NewLine + state.MarkerPath + Environment.NewLine +
					String.Format(L("ManagedContent.Order.RecoveryBackup", "Original backup: {0}"), state.BackupPath ?? "-") + Environment.NewLine +
					(safe ? L("ManagedContent.Order.RecoveryApprove", "Acknowledge this verified publication? The original backup is retained and the game file is not rewritten.") :
					L("ManagedContent.Order.RecoveryConflict", "Manual review is required. This state cannot be acknowledged automatically."));
				if (XtraMessageBox.Show(this, text, Text, safe ? MessageBoxButtons.YesNo : MessageBoxButtons.OK, MessageBoxIcon.Warning) != DialogResult.Yes || !safe) return;
				if (!ReviewIsCurrent(revision)) return;
				IniOrderRecoveryInspection result = await Task.Run(() => _orderService.AcknowledgeRecovery(root, true)).ConfigureAwait(true);
				if (!_released && !IsDisposed && !Disposing && result.Status != IniOrderRecoveryStatus.None)
					XtraMessageBox.Show(this, result.Diagnostic, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch (Exception error)
			{
				if (!_released && !IsDisposed && !Disposing) XtraMessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				if (!_released && !IsDisposed && !Disposing)
				{
					_busy = false;
					_refresh.Enabled = true;
					_refreshNeeded = true;
					await RefreshOnActivateAsync().ConfigureAwait(true);
				}
			}
		}

		/// <summary>Preserves folder selection while rebinding the detached rows; no row rendering reads disk.</summary>
		private void BindRows()
		{
			string selected = (_view.GetFocusedRow() as ComponentRow)?.Folder;
			var rows = _snapshot.Rows.Select(row => new ComponentRow(row)).ToList();
			_grid.BeginUpdate();
			try
			{
				_grid.DataSource = new BindingList<ComponentRow>(rows);
				int index = rows.FindIndex(row => String.Equals(row.Folder, selected, StringComparison.OrdinalIgnoreCase));
				if (index >= 0) _view.FocusedRowHandle = _view.GetRowHandle(index);
			}
			finally { _grid.EndUpdate(); }
			UpdateDetails();
		}

		/// <summary>Shows bounded read diagnostics without presenting incomplete scans as complete absence evidence.</summary>
		private void UpdateStatus()
		{
			var text = new StringBuilder();
			if (_pending != null && _pending.Status != IniOrderRecoveryStatus.None)
				text.AppendLine(String.Format(L("ManagedContent.Order.Pending", "Pending order publication: {0}. Review recovery before editing."),
					L("ManagedContent.Order.RecoveryStatus." + _pending.Status, _pending.Status.ToString())));
			int totalFindings = _snapshot.Index.Issues.Count + _snapshot.Disk.Issues.Count +
				(_snapshot.Order?.Document?.Issues.Count ?? 0) + (_snapshot.Order != null && !_snapshot.Order.IsLoaded ? 1 : 0);
			text.AppendLine(String.Format(L("ManagedContent.Components.Summary", "Components: {0}; disk inventory: {1}; findings: {2}."),
				_snapshot.Index.Components.Count, _snapshot.Index.HasCompleteDiskInventory
					? L("ManagedContent.Components.Complete", "complete") : L("ManagedContent.Components.Incomplete", "incomplete"), totalFindings));
			text.AppendLine(_snapshot.PhysicalRoot);
			if (_snapshot.Order != null)
			{
				string state = _snapshot.Order.IsLoaded && _snapshot.Order.Document?.IsUnambiguous != true
					? L("ManagedContent.Order.State.Ambiguous", "Ambiguous")
					: L("ManagedContent.Order.ReadStatus." + _snapshot.Order.Status, _snapshot.Order.Status.ToString());
				text.AppendLine(String.Format(L("ManagedContent.Order.FileSummary", "Order file ({0}): {1}"), state,
					_snapshot.Order.FilePath ?? _snapshot.OrderRule.RelativePath));
				text.AppendLine(String.Format(L("ManagedContent.Order.Unmatched", "Settings entries without a matching component: {0}."),
					_snapshot.Rows.Count(row => row.Component == null)));
				if (!_snapshot.Order.IsLoaded) text.AppendLine(_snapshot.Order.Diagnostic);
			}
			var orderFindings = (_snapshot.Order?.Document?.Issues ?? Enumerable.Empty<IniOrderIssue>()).Select(issue => FormatFinding(
				String.Format(L("ManagedContent.Order.IssueLocation", "{0}, line {1}"), issue.Section ?? String.Empty, issue.LineNumber),
				L("ManagedContent.Order.Issues." + issue.Kind, issue.Message)));
			var findings = orderFindings.Concat(_snapshot.Index.Issues.Select(issue => FormatFinding(issue.Path,
				L("ManagedContent.Components.Issues." + issue.Kind, issue.Message))))
				.Concat(_snapshot.Disk.Issues.Select(issue => FormatFinding(issue.Path,
					L("ManagedContent.Components.ScanIssues." + issue.Kind, issue.Message) +
					(issue.Kind == ManagedContentComponentDiskIssueKind.EnumerationFailed ? " " + issue.Message : String.Empty)))).Take(20);
			foreach (string finding in findings) text.AppendLine(finding);
			int textFindings = totalFindings - (_snapshot.Order != null && !_snapshot.Order.IsLoaded ? 1 : 0);
			if (textFindings > 20)
				text.AppendLine(String.Format(L("ManagedContent.Components.MoreFindings", "{0} additional findings."), textFindings - 20));
			_status.Text = text.ToString();
		}

		/// <summary>Formats a localized diagnostic and its observed path.</summary>
		private static string FormatFinding(string path, string message) => String.IsNullOrEmpty(path) ? message : path + ": " + message;

		/// <summary>Shows native contributor provenance for the selected component without modifying any files.</summary>
		private void UpdateDetails()
		{
			if (_details == null) return;
			ComponentRow selected = _view.GetFocusedRow() as ComponentRow;
			if (selected == null) { _details.Text = String.Empty; return; }
			ManagedContentNativeComponent component = selected.Observation.Component;
			var text = new StringBuilder();
			text.AppendLine(selected.Folder);
			if (selected.Observation.OrderState != ManagedContentOrderState.NotApplicable)
				text.AppendLine(String.Format(L("ManagedContent.Order.RowSummary", "Order: {0}; configured priority: {1}; game enabled: {2}."),
					selected.OrderConfiguration, selected.Priority, selected.GameEnabled));
			if (component == null)
				text.AppendLine(L("ManagedContent.Order.SettingsOnlyExplanation", "This row comes from the settings file and does not establish installation or NMM ownership."));
			else
			{
				text.AppendLine(String.Format(L("ManagedContent.Components.FileSummary", "Recorded files: {0}; external files observed: {1}; files with an uncertain winner: {2}."),
					component.Files.Count, component.UnmanagedFileCount, component.Files.Count(file => file.WinnerAmbiguous)));
				text.AppendLine(L("ManagedContent.Components.ContributorExplanation", "Contributor file counts include shadowed files. Winning counts describe NMM's intended deployment."));
				foreach (ManagedContentComponentNativeOwner owner in component.Owners.Take(100))
				{
					string name = !String.IsNullOrWhiteSpace(owner.ModName) ? owner.ModName : owner.NativeModKey;
					text.AppendLine(String.Format(L("ManagedContent.Components.Contributor", "{0} ({1}): {2} files, {3} intended winners. {4}"), name,
						owner.InstallMethod == ModInstallMethod.Virtual ? L("ManagedContent.Components.Virtual", "Virtual") : L("ManagedContent.Components.Direct", "Direct"),
						owner.FileCount, owner.WinningFileCount, owner.ArchivePath));
				}
				if (component.Owners.Count > 100)
					text.AppendLine(String.Format(L("ManagedContent.Components.MoreContributors", "{0} additional contributors."), component.Owners.Count - 100));
			}
			_details.Text = text.ToString();
		}

		/// <summary>Applies the main window's existing DevExpress font/icon/display settings.</summary>
		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{
			if (settings != null) DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings);
		}

		/// <summary>Releases timers and native subscriptions even if the page was never opened as an MDI document.</summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing && !_released)
			{
				_released = true;
				_refreshTimer.Stop();
				_refreshTimer.Dispose();
				if (_nativeChanges != null)
				{
					_nativeChanges.Changed -= NativeStateChanged;
					_nativeChanges.Dispose();
				}
			}
			base.Dispose(disposing);
		}

		/// <summary>Detached localized grid values; property access never queries archives or disk.</summary>
		private sealed class ComponentRow
		{
			/// <summary>Converts one immutable component into display values once per accepted refresh.</summary>
			internal ComponentRow(ManagedContentComponentViewRow row)
			{
				Observation = row;
				ManagedContentNativeComponent component = row.Component;
				Folder = row.RootRelativeFolderPath;
				Kind = row.ComponentKind;
				Origin = component == null ? L("ManagedContent.Order.SettingsOnly", "Settings entry only") :
					component.HasMixedManagedAndUnmanagedFiles ? L("ManagedContent.Components.Mixed", "NMM and external") :
					component.IsEntirelyExternal ? L("ManagedContent.Components.External", "External files observed") :
					component.Owners.Count > 0 ? L("ManagedContent.Components.Managed", "NMM contributors recorded") : L("ManagedContent.Components.Unconfirmed", "Unconfirmed");
				Presence = row.ObservedOnDisk == true ? L("ManagedContent.Components.Present", "Observed") :
					row.ObservedOnDisk == false ? L("ManagedContent.Components.Missing", "Missing") : L("ManagedContent.Components.Unknown", "Unknown");
				FileCount = component?.Files.Count ?? 0;
				ContributorCount = component?.Owners.Count ?? 0;
				OrderConfiguration = L("ManagedContent.Order.State." + row.OrderState, row.OrderState.ToString());
				Priority = row.OrderState == ManagedContentOrderState.NotApplicable ? "-" : row.Priority?.ToString(CultureInfo.InvariantCulture) ?? L("ManagedContent.Components.Unknown", "Unknown");
				GameEnabled = row.OrderState == ManagedContentOrderState.NotApplicable ? "-" : row.Enabled.HasValue
					? row.Enabled.Value ? L("ManagedContent.Order.Enabled", "Enabled") : L("ManagedContent.Order.Disabled", "Disabled")
					: L("ManagedContent.Components.Unknown", "Unknown");
			}

			internal ManagedContentComponentViewRow Observation { get; }
			public string Folder { get; }
			public string Kind { get; }
			public string Origin { get; }
			public string Presence { get; }
			public int FileCount { get; }
			public int ContributorCount { get; }
			public string OrderConfiguration { get; }
			public string Priority { get; }
			public string GameEnabled { get; }
		}
	}
}
