using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Controls;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using DevExpress.XtraLayout;
using DevExpress.XtraLayout.Utils;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.UI;

namespace Nexus.Client.ModManagement.LoadOrder.UI
{
	/// <summary>Capability-driven native Load Order document with detached edits, actionable diagnosis and explicit recovery.</summary>
	public sealed partial class LoadOrderControl : ManagedFontXtraForm
	{
		private readonly ComboBoxEdit _profile;
		private readonly SimpleButton _refresh, _discard, _save, _fix, _recovery, _cancel, _up, _down;
		private readonly SimpleButton _exportReport;
		private readonly LabelControl _status, _selectionTitle, _issueSummary;
		private readonly TokenEdit _selectionBadges;
		private readonly MemoEdit _details, _issueDetails;
		private readonly GridControl _grid, _issuesGrid, _relationsGrid;
		private readonly GridView _view, _issuesView, _relationsView;
		private readonly RepositoryItemTokenEdit _badges;
		private readonly ToolTipController _badgeToolTips;
		private readonly LayoutControlItem _progressItem, _cancelItem;
		private readonly System.Windows.Forms.Timer _refreshTimer;
		private NativeLoadOrderService _service;
		private MainFormVM _main;
		private NativeComponentLifecycleObserver _changes;
		private Func<bool> _isActive;
		private LoadOrderEditorSession _session;
		private DevExpressDisplaySettings _displaySettings;
		private CancellationTokenSource _cancellation;
		private FileSystemWatcher _packageWatch, _manifestWatch;
		private string _selectedProfile;
		private DateTime _observedAtUtc;
		private string _lastOperationError;
		private NativeLoadOrderRecovery _lastRecovery;
		private int _observedRevision = -1;
		private int _fileRevision;
		private bool _busy, _binding, _released, _fresh, _needsRefresh = true;
		private Color _errorColor, _warningColor;
		private Point _dragOrigin;
		private LoadOrderModuleRow _dragCandidate;
		private LoadOrderEditorSession _dragSession;
		private string _dragToken;

		/// <summary>Builds DevExpress controls only, without reading packages, profiles or the helper.</summary>
		public LoadOrderControl()
		{
			Name = "LoadOrderDocument"; Text = T("Title", "Load Order"); AutoScaleMode = AutoScaleMode.Font;
			var root = LoadOrderUiLayout.Table(new[] { 100D }, new[] { 0D, 0D, 70D, 30D, 0D, 0D, 0D }); Controls.Add(root);
			LoadOrderUiLayout.Put(root, LoadOrderUiLayout.Label(T("Introduction", "Choose the game's profile, include the modules you want, and arrange them from top to bottom. Changes take effect when you save. Overrides remain active until disabled through NMM.")), 0);
			LayoutControlGroup toolbar = LoadOrderUiLayout.Group(root, 1, new[] { 100D, 0D, 0D, 0D });
			_profile = new ComboBoxEdit { AccessibleName = T("Profile", "Game profile") };
			_profile.Properties.TextEditStyle = TextEditStyles.DisableTextEditor;
			_profile.Properties.NullValuePrompt = T("ChooseProfile", "Choose a game profile");
			_profile.ToolTip = T("ProfileHint", "Select an existing profile stored by the game. This is separate from NMM profiles; you do not need to create an NMM profile.");
			LoadOrderUiLayout.Put(root, _profile, 0, caption: T("Profile", "Game profile"), parent: toolbar);
			_refresh = Button(T("Refresh", "Refresh"), async () => await RefreshAsync(true));
			_refresh.ToolTip = T("RefreshHint", "Reinspect all packages. Automatic refresh after mod changes reuses unchanged package observations.");
			_discard = Button(T("Discard", "Discard changes"), () => { if (ConfirmDiscard()) { ResetDraft(); BindRows(); UpdateStatus(); } return Task.CompletedTask; });
			_recovery = Button(T("Recovery", "Recovery"), OpenRecoveryAsync);
			_fix = Button(T("Fix", "Fix automatically"), FixAsync);
			_exportReport = Button(T("ExportReport", "Export diagnostic report…"), () => { ExportDiagnosticReport(); return Task.CompletedTask; });
			_exportReport.ToolTip = T("ExportReportHint", "Save the current inspection evidence, exact errors, module metadata and dependency checks as JSON. Includes local file paths. Does not rescan or change the game.");
			NmmIconProvider.Bind(_exportReport, NmmIconAction.Export);
			_save = Button(T("Save", "Save"), SaveAsync);
			_cancel = Button(T("Cancel", "Cancel operation"), () => { _cancellation?.Cancel(); return Task.CompletedTask; });
			SimpleButton[] commands = { _refresh, _recovery, _cancel };
			for (int index = 0; index < commands.Length; index++)
			{
				LayoutControlItem item = LoadOrderUiLayout.Put(root, commands[index], 0, index + 1, parent: toolbar);
				if (commands[index] == _cancel) _cancelItem = item;
			}
			_cancelItem.Visibility = LayoutVisibility.Never;
			NmmIconProvider.Bind(_refresh, NmmIconAction.Refresh); NmmIconProvider.Bind(_discard, NmmIconAction.Reset);
			NmmIconProvider.Bind(_recovery, NmmIconAction.Restore); NmmIconProvider.Bind(_fix, NmmIconAction.Repair);
			NmmIconProvider.Bind(_save, NmmIconAction.Save); NmmIconProvider.Bind(_cancel, NmmIconAction.Cancel);
			_fix.ToolTip = T("FixHint", "Review safe repairs for the saved order, including folder cleanup and dependency sorting. Save or discard pending edits first.");
			_save.ToolTip = T("SaveHint", "Write this order to the selected game profile. NMM rechecks the files and retains an original backup.");
			_profile.EditValueChanged += async (sender, args) =>
			{
				if (_binding || _busy) return;
				if (!ConfirmDiscard()) { _binding = true; _profile.EditValue = _selectedProfile; _binding = false; return; }
				CancelPendingAutomaticFix(); _session = null; _selectedProfile = _profile.EditValue as string; _fresh = false; BindRows();
				await RefreshAsync(false, true);
			};

			var split = new SplitContainerControl { Horizontal = true, FixedPanel = SplitFixedPanel.None, SplitterPosition = 660 };
			split.Panel1.MinSize = 280; split.Panel2.MinSize = 240;
			LoadOrderUiLayout.Put(root, split, 2);
			var list = LoadOrderUiLayout.Table(new[] { 100D }, new[] { 0D, 100D }); split.Panel1.Controls.Add(list);
			LayoutControlGroup listCommands = LoadOrderUiLayout.Group(list, 0, new[] { 100D, 0D, 0D });
			LoadOrderUiLayout.Put(list, LoadOrderUiLayout.Label(T("OrderHeading", "Modules · top loads first")), 0, parent: listCommands);
			_up = Button(T("MoveUp", "Move up"), () => { MoveSelected(-1); return Task.CompletedTask; });
			_down = Button(T("MoveDown", "Move down"), () => { MoveSelected(1); return Task.CompletedTask; });
			NmmIconProvider.Bind(_up, NmmIconAction.MoveUp); NmmIconProvider.Bind(_down, NmmIconAction.MoveDown);
			_up.ToolTip = _down.ToolTip = T("MoveHint", "Alt+Up / Alt+Down moves an included module. Clear search and filters before reordering.");
			LoadOrderUiLayout.Put(list, _up, 0, 1, parent: listCommands); LoadOrderUiLayout.Put(list, _down, 0, 2, parent: listCommands);
			_grid = CreateGrid(out _view, true); LoadOrderUiLayout.Put(list, _grid, 1);
			_view.OptionsFind.AlwaysVisible = true;
			var include = new RepositoryItemCheckEdit { AllowGrayed = false }; _grid.RepositoryItems.Add(include);
			var lockedInclude = new RepositoryItemCheckEdit { ReadOnly = true, AllowGrayed = false }; _grid.RepositoryItems.Add(lockedInclude);
			var noInclude = new RepositoryItemTextEdit { ReadOnly = true, BorderStyle = BorderStyles.NoBorder }; _grid.RepositoryItems.Add(noInclude);
			include.EditValueChanged += (sender, args) => _view.PostEditor();
			_view.Columns.AddVisible(nameof(LoadOrderModuleRow.Included), T("Columns.Include", "Include")).ColumnEdit = include;
			_view.Columns.AddVisible(nameof(LoadOrderModuleRow.Position), T("Columns.Order", "Order"));
			_view.Columns.AddVisible(nameof(LoadOrderModuleRow.Name), T("Columns.Mod", "Mod"));
			_view.Columns.AddVisible(nameof(LoadOrderModuleRow.State), T("Columns.State", "State"));
			_badges = CreateBadgeRepository(); _grid.RepositoryItems.Add(_badges);
			_badgeToolTips = new ToolTipController(); _grid.ToolTipController = _badgeToolTips;
			_badgeToolTips.GetActiveObjectInfo += BadgeToolTipInfo;
			_view.Columns.AddVisible(nameof(LoadOrderModuleRow.Badges), T("Columns.Content", "Content")).ColumnEdit = _badges;
			foreach (DevExpress.XtraGrid.Columns.GridColumn column in _view.Columns) column.OptionsColumn.AllowEdit = column.FieldName == nameof(LoadOrderModuleRow.Included);
			_view.Columns[nameof(LoadOrderModuleRow.Included)].MaxWidth = 72;
			_view.Columns[nameof(LoadOrderModuleRow.Position)].MaxWidth = 65;
			_view.Columns[nameof(LoadOrderModuleRow.Name)].MinWidth = 130;
			_view.ShowingEditor += (sender, args) => args.Cancel = !CanEdit || !((_view.GetFocusedRow() as LoadOrderModuleRow)?.CanToggle ?? false);
			_view.CustomRowCellEdit += (sender, args) =>
			{
				if (args.Column.FieldName != nameof(LoadOrderModuleRow.Included)) return;
				LoadOrderModuleRow row = _view.GetRow(args.RowHandle) as LoadOrderModuleRow;
				args.RepositoryItem = row?.Included == null ? (RepositoryItem)noInclude : row.CanToggle && CanEdit ? include : lockedInclude;
			};
			_view.CustomColumnDisplayText += (sender, args) => { if (args.Column.FieldName == nameof(LoadOrderModuleRow.Included) && (args.Value == null || args.Value == DBNull.Value)) args.DisplayText = "—"; };
			_view.CellValueChanged += (sender, args) =>
			{
				if (_binding || !CanEdit || args.Column.FieldName != nameof(LoadOrderModuleRow.Included)) return;
				LoadOrderModuleRow row = _view.GetRow(args.RowHandle) as LoadOrderModuleRow;
				try
				{
					CancelPendingAutomaticFix(); LoadOrderEditorSession edited = _session; edited.Include(row, args.Value is bool && (bool)args.Value); UpdateStatus();
					// Finish posting the checkbox editor before replacing its bound rows.
					BeginInvoke((MethodInvoker)(() => { if (!_released && !_busy && ReferenceEquals(edited, _session)) { BindRows(row.Key); UpdateStatus(); } }));
				}
				catch (Exception error) when (!(error is OutOfMemoryException)) { ShowError(error); BindRows(row?.Key); }
			};
			_view.FocusedRowChanged += (sender, args) => { if (!_binding) { BindDetails(); UpdateCommands(); } };
			_view.ColumnFilterChanged += (sender, args) => UpdateCommands();
			_grid.AllowDrop = true; _grid.MouseDown += GridMouseDown; _grid.MouseMove += GridMouseMove;
			_grid.DragEnter += GridDragOver; _grid.DragOver += GridDragOver; _grid.DragDrop += GridDragDrop;
			_grid.DragLeave += (sender, args) => UpdateStatus();

			var detail = LoadOrderUiLayout.Table(new[] { 100D }, new[] { 0D, 0D, 60D, 40D }); split.Panel2.Controls.Add(detail);
			_selectionTitle = LoadOrderUiLayout.Label(T("SelectMod", "Select a mod to see its details")); LoadOrderUiLayout.Put(detail, _selectionTitle, 0);
			_selectionTitle.Appearance.FontStyleDelta = FontStyle.Bold;
			_selectionBadges = new TokenEdit(); ConfigureBadgeRepository(_selectionBadges.Properties);
			_selectionBadges.ToolTipController = _badgeToolTips;
			_selectionBadges.ToolTipTitle = T("Columns.Content", "Content");
			LoadOrderUiLayout.Put(detail, _selectionBadges, 1);
			_details = LoadOrderUiLayout.Memo(); LoadOrderUiLayout.Put(detail, _details, 2);
			_relationsGrid = CreateGrid(out _relationsView, false); LoadOrderUiLayout.Put(detail, _relationsGrid, 3);
			_relationsView.Columns.AddVisible("Kind", T("Columns.Relationship", "Relationship"));
			_relationsView.Columns.AddVisible("Name", T("Columns.RequiredMod", "Mod"));
			_relationsView.Columns.AddVisible("Version", T("Columns.RequiredVersion", "Declared version"));
			_relationsView.Columns.AddVisible("State", T("Columns.Availability", "Availability"));

			var problems = LoadOrderUiLayout.Table(new[] { 100D }, new[] { 0D, 100D }); LoadOrderUiLayout.Put(root, problems, 3);
			LayoutControlGroup problemCommands = LoadOrderUiLayout.Group(problems, 0, new[] { 100D, 0D, 0D });
			_issueSummary = LoadOrderUiLayout.Label(T("Problems", "Problems")); LoadOrderUiLayout.Put(problems, _issueSummary, 0, parent: problemCommands);
			_issueSummary.Appearance.FontStyleDelta = FontStyle.Bold;
			LoadOrderUiLayout.Put(problems, _fix, 0, 1, parent: problemCommands);
			LoadOrderUiLayout.Put(problems, _exportReport, 0, 2, parent: problemCommands);
			var problemSplit = new SplitContainerControl { Horizontal = true, FixedPanel = SplitFixedPanel.None, SplitterPosition = 580 };
			problemSplit.Panel1.MinSize = 200; problemSplit.Panel2.MinSize = 200;
			LoadOrderUiLayout.Put(problems, problemSplit, 1);
			_issuesGrid = CreateGrid(out _issuesView, false); _issuesGrid.Dock = DockStyle.Fill; problemSplit.Panel1.Controls.Add(_issuesGrid);
			_issuesView.Columns.AddVisible("Severity", T("Columns.Severity", "Level"));
			_issuesView.Columns.AddVisible("Subject", T("Columns.Subject", "Mod or location"));
			_issuesView.Columns.AddVisible("Message", T("Columns.Problem", "Problem"));
			_issueDetails = LoadOrderUiLayout.Memo(); _issueDetails.Dock = DockStyle.Fill; problemSplit.Panel2.Controls.Add(_issueDetails);
			_issuesView.FocusedRowChanged += (sender, args) => BindIssueDetails();
			_issuesView.RowCellStyle += (sender, args) =>
			{
				IssueRow row = _issuesView.GetRow(args.RowHandle) as IssueRow; if (row == null) return;
				args.Appearance.ForeColor = row.Issue.Severity == LoadOrderDiagnosticSeverity.Error ? _errorColor : _warningColor; args.Appearance.Options.UseForeColor = true;
			};
			_automaticFix = new CheckEdit { AccessibleName = T("AutomaticFix", "Automatically fix module order after installing or removing mods") };
			_automaticFix.Properties.Caption = _automaticFix.AccessibleName;
			_automaticFix.Properties.AllowGrayed = false;
			_automaticFix.ToolTip = T("AutomaticFixHint", "Off by default. Applies safe dependency sorting and module-list repairs to the selected game profile after successful mod installs/removals. May reorder modules or enable required dependencies. Unsaved edits are preserved; files and folders are never moved. Enabling this option does not change the current order.");
			_automaticFix.CheckedChanged += AutomaticFixSettingChanged;
			LoadOrderUiLayout.Put(root, _automaticFix, 4);
			LayoutControlGroup footer = LoadOrderUiLayout.Group(root, 5, new[] { 100D, 0D, 0D });
			_status = LoadOrderUiLayout.Label(T("Initial", "Open this page to inspect the game's existing profiles and packages.")); LoadOrderUiLayout.Put(root, _status, 0, parent: footer);
			LoadOrderUiLayout.Put(root, _discard, 0, 1, parent: footer); LoadOrderUiLayout.Put(root, _save, 0, 2, parent: footer);
			_progressItem = LoadOrderUiLayout.Put(root, new MarqueeProgressBarControl(), 6); _progressItem.Visibility = LayoutVisibility.Never;
			_refreshTimer = new System.Windows.Forms.Timer { Interval = 450 };
			_refreshTimer.Tick += async (sender, args) => { _refreshTimer.Stop(); if (_automaticFixPending) await TryAutomaticFixAsync(); else await RefreshOnActivateAsync(); };
			FormClosing += (sender, args) => { if (!ConfirmLeave()) args.Cancel = true; };
			UpdateCommands();
		}

		/// <summary>Indicates worker/review activity that must complete before a session can be detached.</summary>
		internal bool HasActiveSessionWork => _busy || _dragToken != null;
		internal bool HasUnsavedChanges => _session?.IsDirty == true;
		private bool ObservationCurrent => _fresh && _changes != null && _changes.Revision == _observedRevision;
		private bool CanEdit => !_busy && ObservationCurrent && _session?.Draft != null && !_changes.HasRunningNativeTask && !_main.IsSwitching;

		/// <summary>Binds capability-selected services without reading a game or branching on its identity.</summary>
		public void Initialize(MainFormVM main, Func<bool> isActive)
		{
			if (_service != null) throw new InvalidOperationException("Load Order is already initialized.");
			_main = main ?? throw new ArgumentNullException(nameof(main));
			_service = main.NativeLoadOrder ?? throw new ArgumentException("The current game has no native load-order capability.");
			_isActive = isActive ?? throw new ArgumentNullException(nameof(isActive));
			_changes = new NativeComponentLifecycleObserver(main.ModActivationMonitor, main.ModManager); _changes.Changed += NativeChanged;
			string key = "loadOrder.SelectedProfile." + _service.ModeId;
			if (main.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey(key)) _selectedProfile = main.EnvironmentInfo.Settings.DockPanelLayouts[key];
			InitializeAutomaticFix();
			UpdateCommands();
		}

		/// <summary>Refreshes only an active stale page; pending user edits are never silently replaced.</summary>
		public Task RefreshOnActivateAsync() => _isActive?.Invoke() == true && _needsRefresh && _session?.IsDirty != true && !_busy && _dragToken == null ? RefreshAsync(false) : Task.CompletedTask;

		/// <summary>Invalidates the current observation after a completed or compensated NMM profile transition.</summary>
		internal void ProfileChanged()
		{
			CancelPendingAutomaticFix();
			_needsRefresh = true; _fresh = false; UpdateStatus();
			if (!_busy && _isActive?.Invoke() == true && _session?.IsDirty != true) _ = RefreshOnActivateAsync();
		}

		/// <summary>Asks before dropping edits and blocks closure while native work still owns this page's services.</summary>
		internal bool ConfirmLeave()
		{
			if (_released) return true;
			if (HasActiveSessionWork) { _status.Text = T("CloseBusy", "Finish or cancel the load-order operation before leaving this game."); return false; }
			if (_session?.IsDirty != true) return true;
			if (!ConfirmDiscard()) return false;
			ResetDraft(); BindRows(); UpdateStatus(); return true;
		}

		/// <summary>Handles the same standard unsaved-change choice for refresh, profile selection, close and session switching.</summary>
		private bool ConfirmDiscard() => _session?.IsDirty != true || XtraMessageBox.Show(this,
			T("DiscardPrompt", "Discard the unsaved load-order changes?"), T("UnsavedTitle", "Unsaved load order"), MessageBoxButtons.YesNo,
			MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

		/// <summary>Restores the detached observation, without writing a file or rescanning packages.</summary>
		private void ResetDraft() { CancelPendingAutomaticFix(); if (_session != null) _session = new LoadOrderEditorSession(_session.Analysis); }

		/// <summary>Queues native invalidation on the UI thread; callbacks never inspect packages or replace a draft.</summary>
		private void NativeChanged(object sender, EventArgs args)
		{
			if (_released || !IsHandleCreated) return;
			try
			{
				BeginInvoke((MethodInvoker)(() =>
				{
					if (_released || IsDisposed || _changes.Revision == _observedRevision) return;
					_needsRefresh = true; if (_busy) return;
					_fresh = false; UpdateStatus(); UpdateCommands();
					if ((_automaticFixPending || _isActive?.Invoke() == true) && _session?.IsDirty != true && !_changes.HasRunningNativeTask) { _refreshTimer.Stop(); _refreshTimer.Start(); }
				}));
			}
			catch (InvalidOperationException) { }
		}

		/// <summary>Runs a bounded cancellable read on a worker and retains explicit profile selection.</summary>
		private async Task RefreshAsync(bool requested, bool profileChanged = false)
		{
			if (_service == null || HasActiveSessionWork || _released || _changes.HasRunningNativeTask || _main.IsSwitching) return;
			if (!profileChanged && _session?.IsDirty == true)
			{
				if (!requested || !ConfirmDiscard()) return;
				ResetDraft();
			}
			if (requested) _service.ClearDisplayInspectionCache();
			await WorkAsync(T("Reading", "Inspecting packages and load order…"), token => ReadCoreAsync(token));
		}

		/// <summary>Displays the saved manifest first, then enriches a read-only preview while authoritative inspection runs.</summary>
		private async Task ReadCoreAsync(CancellationToken token)
		{
			_observedAtUtc = DateTime.UtcNow; _lastOperationError = null; _lastRecovery = null;
			string selected = _selectedProfile;
			PageRead read = await Task.Run(() =>
			{
				token.ThrowIfCancellationRequested();
				string[] profiles = _service.ListProfiles().ToArray();
				string profile = profiles.Contains(selected) ? selected : profiles.Length == 1 ? profiles[0] : null;
				var result = new PageRead { Profiles = profiles, Profile = profile, FileRevision = Volatile.Read(ref _fileRevision) };
				if (profile != null)
				{
					try
					{
						result.Document = _service.Read(profile);
						result.PackageRoot = ModDeploymentTargetResolver.GetPhysicalRootPath(_main.GameMode, ModDeploymentRoot.Data);
					}
					catch (Exception error) when (!(error is OperationCanceledException) && !(error is OutOfMemoryException)) { result.Error = error; }
				}
				result.Revision = _changes.Revision; return result;
			}, token);
			if (_released || IsDisposed) return;
			if (read.Revision != _changes.Revision || read.FileRevision != Volatile.Read(ref _fileRevision) || _changes.HasRunningNativeTask || _main.IsSwitching) throw new InvalidOperationException(T("ChangedDuringRead", "Files or mod deployment changed during inspection. Refresh after the operation finishes."));
			_binding = true;
			try { _profile.Properties.Items.Clear(); _profile.Properties.Items.AddRange(read.Profiles); _profile.EditValue = read.Profile; }
			finally { _binding = false; }
			_selectedProfile = read.Profile; _session = read.Document == null ? null : LoadOrderEditorSession.Preview(read.Document);
			if (read.Profile != null) _main.EnvironmentInfo.Settings.DockPanelLayouts["loadOrder.SelectedProfile." + _service.ModeId] = read.Profile;
			_observedRevision = read.Revision; _fresh = false; _needsRefresh = true;
			WatchFiles(_session?.Analysis, read.PackageRoot);
			BindRows();
			if (read.Profile != null)
			{
				_status.Text = read.Document == null ? T("CheckingRecoveryOnly", "Checking recovery and ownership…")
					: T("CheckingRecovery", "Saved order loaded. Checking recovery and ownership before inspecting packages…");
				var inspected = new List<LoadOrderPackageObservation>();
				var repaint = Stopwatch.StartNew();
				bool reporting = true;
				var progress = new Progress<LoadOrderInspectionProgress>(value =>
				{
					if (!reporting || _released || IsDisposed || token.IsCancellationRequested) return;
					if (value.Package != null) inspected.Add(value.Package);
					if (read.Document != null && value.Package != null && repaint.ElapsedMilliseconds >= 200)
					{
						_session = LoadOrderEditorSession.Preview(read.Document, inspected); BindRows(); repaint.Restart();
					}
					_status.Text = value.Completed == value.Total ? String.Format(T("CheckingDependenciesCached", "Checking dependencies and load-order problems… Reused {0} unchanged packages."), value.ReusedPackages)
						: String.Format(T("PackageProgressCached", "Checking package {0} of {1} · {2} · {3} unchanged packages reused"),
							Math.Min(value.Completed + (value.Package == null ? 1 : 0), value.Total), value.Total, value.FileName, value.ReusedPackages);
				});
				try
				{
					await Task.Run(() =>
					{
						read.Recovery = _service.InspectRecovery(read.Profile, token);
						if (read.Document != null && read.Recovery.State == NativeLoadOrderRecoveryState.None)
							try { read.Analysis = _service.Analyze(read.Profile, token, progress, true); }
							catch (Exception error) when (!(error is OperationCanceledException) && !(error is OutOfMemoryException)) { read.Error = error; }
						read.Revision = _changes.Revision;
					}, token);
				}
				finally { reporting = false; }
				if (_released || IsDisposed) return;
				_lastRecovery = read.Recovery;
				if (read.Revision != _changes.Revision || read.FileRevision != Volatile.Read(ref _fileRevision) || _changes.HasRunningNativeTask || _main.IsSwitching)
					throw new InvalidOperationException(T("ChangedDuringRead", "Files or mod deployment changed during inspection. Refresh after the operation finishes."));
				if (read.Analysis != null) _session = new LoadOrderEditorSession(read.Analysis);
				_observedRevision = read.Revision; _fresh = read.Analysis != null;
				if (read.Analysis != null) WatchFiles(read.Analysis);
				BindRows();
			}
			_needsRefresh = false;
			if (read.Error != null) { _lastOperationError = read.Error.ToString(); Trace.TraceWarning("Load order inspection: {0}", read.Error); _issueDetails.Text = read.Error.Message; _status.Text = T("ReadFailed", "The order could not be read. Review the details and recovery controls. ") + read.Error.Message; }
			else if (read.Recovery != null && read.Recovery.State != NativeLoadOrderRecoveryState.None)
			{ _status.Text = T("RecoveryNeeded", "An interrupted save needs recovery. Open Recovery before editing this profile."); _issueDetails.Text = read.Recovery.Diagnostic ?? _status.Text; }
			else if (read.Profiles.Length == 0) _status.Text = T("NoProfiles", "No existing game load-order files were found. Run the game once, then refresh. You do not need to create an NMM profile.");
			else if (read.Profile == null) _status.Text = T("ChooseProfile", "Choose a game profile");
			else UpdateStatus();
		}

		/// <summary>Publishes the displayed detached draft after review, with fresh package and ownership checks in the service.</summary>
		private async Task SaveAsync()
		{
			CancelPendingAutomaticFix();
			_view.CloseEditor(); _view.UpdateCurrentRow();
			if (_dragToken != null || !CanEdit || !_session.IsDirty) return;
			NativeLoadOrderDraft draft = _session.Draft; string profile = _selectedProfile;
			await WorkAsync(T("Saving", "Saving the selected game profile…"), async token =>
			{
				NativeLoadOrderReview review = await Task.Run(() => _service.Review(profile, draft, token), token);
				NativeLoadOrderRecovery result = await Task.Run(() => _service.Save(review, true, token), token);
				await RefreshAfterWriteAsync(token, result.State == NativeLoadOrderRecoveryState.None ? T("Saved", "Load order saved.") + (String.IsNullOrEmpty(result.BackupPath) ? String.Empty : " " + String.Format(T("Backup", "Original backup: {0}"), result.BackupPath))
					: T("RecoveryNeeded", "An interrupted save needs recovery. Open Recovery before editing this profile."));
			});
		}

		/// <summary>Reviews concrete repair actions, then applies exactly that review through existing recovery-aware services.</summary>
		private async Task FixAsync()
		{
			if (HasActiveSessionWork || !ObservationCurrent || _session == null || _session.IsDirty || _changes.HasRunningNativeTask || _main.IsSwitching) return;
			CancelPendingAutomaticFix();
			LoadOrderAnalysis analysis = _session.Analysis; string profile = _selectedProfile;
			await WorkAsync(T("PlanningFixes", "Preparing safe repairs…"), async token =>
			{
				LoadOrderAutomaticRepairReview review = await Task.Run(() => _service.ReviewAutomaticRepairs(profile, analysis, token), token);
				int revision = _changes.Revision;
				if (!review.HasRepairs)
				{
					_issueDetails.Text = String.Join(Environment.NewLine + Environment.NewLine, review.Folder.Issues.Select(issue => issue.Message + Environment.NewLine + issue.Direction));
					_status.Text = T("NoFixes", "No safe automatic repairs are available. Follow the directions for the remaining problems."); return;
				}
				using (var dialog = new LoadOrderRepairDialog(review, _session))
				{
					dialog.ApplyDisplaySettings(_displaySettings);
					if (dialog.ShowDialog(this) != DialogResult.OK) return;
				}
				if (_changes.Revision != revision || _changes.HasRunningNativeTask || _main.IsSwitching) throw new InvalidOperationException(T("StaleReview", "The mod state changed during review. Refresh and review the repairs again."));
				_status.Text = T("ApplyingFixes", "Applying the reviewed fixes…");
				LoadOrderAutomaticRepairResult result = await Task.Run(() => _service.ApplyAutomaticRepairs(review, true, token), token);
				await RefreshAfterWriteAsync(token, (result.Order != null && result.Order.State != NativeLoadOrderRecoveryState.None ? T("RecoveryNeeded", "An interrupted save needs recovery. Open Recovery before editing this profile.")
					: T("FixesApplied", "Reviewed fixes applied. Check the refreshed problems for anything that still needs attention.")) +
					(String.IsNullOrEmpty(result.RetainedContentDirectory) ? String.Empty : " " + String.Format(T("Retained", "Content retained at: {0}"), result.RetainedContentDirectory)));
			});
		}

		/// <summary>Never describes a confirmed publication as an unsaved draft merely because its following refresh was cancelled or failed.</summary>
		private async Task RefreshAfterWriteAsync(CancellationToken token, string resultMessage)
		{
			_session = null; _fresh = false; _needsRefresh = true; BindRows();
			try { await ReadCoreAsync(token); }
			catch (OperationCanceledException) { _status.Text = resultMessage + " " + T("RefreshCancelled", "Refresh was cancelled; refresh again to inspect the current state."); return; }
			catch (Exception error) when (!(error is OutOfMemoryException))
			{
				Trace.TraceWarning("Load Order saved-state refresh failed: {0}", error); _issueDetails.Text = error.Message;
				_status.Text = resultMessage + " " + T("RefreshNeeded", "Refresh is required before further editing."); return;
			}
			_status.Text = resultMessage + (_fresh ? String.Empty : Environment.NewLine + _status.Text);
		}

		/// <summary>Uses explicit recovery choices and keeps the current session alive while its owned dialog is open.</summary>
		private async Task OpenRecoveryAsync()
		{
			if (HasActiveSessionWork || _selectedProfile == null || _changes.HasRunningNativeTask || _main.IsSwitching || !ConfirmDiscard()) return;
			ResetDraft();
			await WorkAsync(T("ReadingRecovery", "Reviewing recovery information…"), async token =>
			{
				using (var dialog = new LoadOrderRecoveryDialog(_service, _selectedProfile))
				{
					dialog.ApplyDisplaySettings(_displaySettings); dialog.ShowDialog(this);
				}
				await ReadCoreAsync(token);
			});
		}

		/// <summary>Serializes page work and handles cancellation/failure without hiding retained native or folder recovery evidence.</summary>
		private async Task WorkAsync(string message, Func<CancellationToken, Task> work, bool showErrors = true)
		{
			if (_busy || _released) return;
			_busy = true; _refreshTimer.Stop(); _cancellation = new CancellationTokenSource(); _status.Text = message; UpdateCommands();
			bool completed = false;
			try { await work(_cancellation.Token); completed = true; }
			catch (OperationCanceledException) { if (!_released) { _lastOperationError = "Operation cancelled."; _fresh = false; _needsRefresh = true; _status.Text = T("Cancelled", "Operation cancelled. Refresh to confirm the current game state."); } }
			catch (Exception error) when (!(error is OutOfMemoryException))
			{
				if (!_released)
				{
					_lastOperationError = error.ToString();
					_fresh = false; _needsRefresh = true;
					if (showErrors) { ShowError(error); _status.Text = T("Failed", "The operation did not complete. Your draft is retained; refresh or inspect Recovery before continuing."); }
					else { Trace.TraceWarning("Automatic load-order repair stopped: {0}", error); _status.Text = T("AutomaticFixFailed", "Automatic order repair stopped. Refresh Load Order and review its problems or recovery controls. ") + error.Message; }
				}
			}
			finally
			{
				bool cancelled = _cancellation.IsCancellationRequested;
				_cancellation.Dispose(); _cancellation = null; _busy = false;
				if (!_released && !IsDisposed)
				{
					if (_changes.Revision != _observedRevision) { _fresh = false; _needsRefresh = true; }
					UpdateCommands();
					if (completed && !cancelled && (_automaticFixPending || _needsRefresh && _isActive?.Invoke() == true) && _session?.IsDirty != true && !_changes.HasRunningNativeTask && !_main.IsSwitching) _refreshTimer.Start();
				}
			}
		}

		/// <summary>Preserves focused identity and the user's viewport while background inspection replaces immutable rows.</summary>
		private void BindRows(string focus = null)
		{
			Text = T("Title", "Load Order") + (_session?.IsDirty == true ? " *" : String.Empty);
			focus = focus ?? (_view.GetFocusedRow() as LoadOrderModuleRow)?.Key;
			bool preserveViewport = _busy && _view.RowCount > 0;
			int top = Math.Max(0, _view.TopRowIndex), left = _view.LeftCoord;
			string topKey = preserveViewport ? (_view.GetRow(_view.GetVisibleRowHandle(top)) as LoadOrderModuleRow)?.Key : null;
			var focusedColumn = _view.FocusedColumn;
			_binding = true;
			_view.BeginDataUpdate();
			try
			{
				_grid.DataSource = new BindingList<LoadOrderModuleRow>(_session?.Rows() ?? new List<LoadOrderModuleRow>());
			}
			finally
			{
				try
				{
					_view.EndDataUpdate();
					if (focus != null) { int handle = _view.LocateByValue(nameof(LoadOrderModuleRow.Key), focus); if (handle >= 0) _view.FocusedRowHandle = handle; }
					if (focusedColumn != null) _view.FocusedColumn = focusedColumn;
					if (preserveViewport)
					{
						int handle = topKey == null ? -1 : _view.LocateByValue(nameof(LoadOrderModuleRow.Key), topKey);
						int visible = handle < 0 ? -1 : _view.GetVisibleIndex(handle);
						_view.TopRowIndex = visible >= 0 ? visible : Math.Max(0, Math.Min(top, _view.RowCount - 1));
						_view.LeftCoord = left;
					}
				}
				finally { _binding = false; }
			}
			BindProblems(); BindDetails();
			UpdateCommands();
		}

		/// <summary>Shows dependency availability against the displayed draft, not an unrelated disk-only order.</summary>
		private void BindDetails()
		{
			if (_selectionTitle == null || _relationsGrid == null) return;
			LoadOrderModuleRow row = _view.GetFocusedRow() as LoadOrderModuleRow;
			_selectionTitle.Text = row?.Name ?? T("SelectMod", "Select a mod to see its details"); _selectionBadges.EditValue = row?.Badges;
			_details.Text = row?.Details() ?? String.Empty;
			_selectionBadges.ToolTip = BadgeDescriptions(row?.Badges);
			var relations = new List<RelationRow>();
			if (row?.Module != null)
			{
				foreach (LoadOrderModuleReference dependency in row.Module.Dependencies) relations.Add(Relation(dependency, false));
				foreach (LoadOrderModuleReference conflict in row.Module.Conflicts) relations.Add(Relation(conflict, true));
			}
			_relationsGrid.DataSource = relations;
		}

		/// <summary>Separates declared conflicts from required dependencies and preserves absent metadata.</summary>
		private RelationRow Relation(LoadOrderModuleReference reference, bool conflict)
		{
			bool available = _session.IsAvailable(reference.Identity);
			bool game = ((_main.GameMode as ILoadOrderCapabilityProvider)?.LoadOrder?.BuiltInModuleIdentities.Contains(reference.Identity) ?? false) || _session.Analysis.Document.Entries.Any(entry => entry.IsProtected && entry.Identity == reference.Identity);
			return new RelationRow { Name = reference.Name ?? _session.NameFor(reference.Identity), Version = reference.Version ?? "—", Kind = conflict ? T("Relation.Conflict", "Incompatible with") : T("Relation.Dependency", "Requires"),
				State = game ? T("Relation.Game", "Game-provided") : _session.Included.Contains(reference.Identity) ? T("Relation.Included", "Included in this order") : available ? T("Relation.Excluded", "Installed, excluded") : T("Relation.Missing", "Not installed") };
		}

		/// <summary>Re-evaluates detached edits locally; an automatic publication still reviews the actual saved-file preimage.</summary>
		private void BindProblems()
		{
			if (_session == null) { _issuesGrid.DataSource = null; _issueSummary.Text = T("Problems", "Problems"); _issueDetails.Text = String.Empty; return; }
			if (_session.IsInspecting)
			{
				_issuesGrid.DataSource = null; _issueSummary.Text = T("ProblemsPending", "Problems · checks incomplete");
				BindIssueDetails(); return;
			}
			LoadOrderAnalysis analysis = _session.IsDirty ? LarianLoadOrderAnalyzer.Analyze(_session.Draft.Prepare().After, _session.Analysis.Inventory,
				LarianOrderPreference.Decode(_session.Analysis.Preference), ((_main.GameMode as ILoadOrderCapabilityProvider)?.LoadOrder)?.BuiltInModuleIdentities) : _session.Analysis;
			IssueRow[] rows = analysis.Issues.Where(issue => issue.Severity != LoadOrderDiagnosticSeverity.Information).Select(issue => new IssueRow(issue, _session)).ToArray();
			_issuesGrid.DataSource = rows;
			_issueSummary.Text = String.Format(T("ProblemCount", "Problems: {0} errors · {1} warnings"), rows.Count(row => row.Issue.Severity == LoadOrderDiagnosticSeverity.Error), rows.Count(row => row.Issue.Severity == LoadOrderDiagnosticSeverity.Warning));
			BindIssueDetails();
		}

		/// <summary>Displays the next action alongside each readable finding, retaining exact paths and UUID evidence for copying.</summary>
		private void BindIssueDetails()
		{
			if (_session?.IsInspecting == true)
			{
				_issueDetails.Text = T("InspectionPending", "The saved order is shown. Package and dependency checks are incomplete; editing and automatic fixes remain unavailable until inspection finishes."); return;
			}
			IssueRow row = _issuesView.GetFocusedRow() as IssueRow;
			_issueDetails.Text = row == null ? T("NoProblems", "No load-order problems found in the current scan.") : row.Issue.Message + Environment.NewLine + Environment.NewLine +
				row.Issue.Direction + Environment.NewLine + Environment.NewLine + row.Issue.Path + Environment.NewLine + String.Join(Environment.NewLine, row.Issue.ModuleIdentities)
				+ (String.IsNullOrEmpty(row.Issue.TechnicalDetails) ? String.Empty : Environment.NewLine + Environment.NewLine + row.Issue.TechnicalDetails);
		}

		/// <summary>Moves an included module by one real position, with search/filter guards.</summary>
		private void MoveSelected(int offset)
		{
			if (!CanEdit || !String.IsNullOrEmpty(_view.ActiveFilterString) || !String.IsNullOrEmpty(_view.FindFilterText)) return;
			LoadOrderModuleRow row = _view.GetFocusedRow() as LoadOrderModuleRow;
			if (row?.Position == null || !row.CanToggle) return;
			CancelPendingAutomaticFix(); _session.Move(row, offset); BindRows(row.Key); UpdateStatus();
		}

		/// <summary>Records an included row as a possible drag, leaving checkbox clicks and fixed/physical package rows alone.</summary>
		private void GridMouseDown(object sender, MouseEventArgs args)
		{
			_dragCandidate = null;
			if (args.Button != MouseButtons.Left || !CanEdit || !String.IsNullOrEmpty(_view.ActiveFilterString) || !String.IsNullOrEmpty(_view.FindFilterText)) return;
			var hit = _view.CalcHitInfo(args.Location);
			if (!hit.InRowCell || hit.Column?.FieldName == nameof(LoadOrderModuleRow.Included)) return;
			LoadOrderModuleRow row = _view.GetRow(hit.RowHandle) as LoadOrderModuleRow;
			if (row?.CanToggle == true && row.Position.HasValue) { _dragOrigin = args.Location; _dragCandidate = row; }
		}

		/// <summary>Uses the OS drag threshold and a private text nonce; external file/object drops are never interpreted as modules.</summary>
		private void GridMouseMove(object sender, MouseEventArgs args)
		{
			if (_dragCandidate == null || args.Button != MouseButtons.Left || !CanEdit) return;
			Size threshold = SystemInformation.DragSize;
			if (new Rectangle(_dragOrigin.X - threshold.Width / 2, _dragOrigin.Y - threshold.Height / 2, threshold.Width, threshold.Height).Contains(args.Location)) return;
			_dragSession = _session; _dragToken = Guid.NewGuid().ToString("N");
			UpdateCommands();
			try { _grid.DoDragDrop(new DataObject(DataFormats.UnicodeText, _dragToken), DragDropEffects.Move); }
			finally
			{
				_dragToken = null; _dragCandidate = null; _dragSession = null;
				if (!_released && !IsDisposed) { UpdateStatus(); if (_needsRefresh && _session?.IsDirty != true) _refreshTimer.Start(); }
			}
		}

		/// <summary>Finds a valid in-session drop target and shows the before/after destination in plain language.</summary>
		private LoadOrderModuleRow DragTarget(DragEventArgs args, out bool after)
		{
			after = false;
			if (_dragToken == null || !ReferenceEquals(_dragSession, _session) || !CanEdit || !String.IsNullOrEmpty(_view.ActiveFilterString) || !String.IsNullOrEmpty(_view.FindFilterText) ||
				args.Data == null || !args.Data.GetDataPresent(DataFormats.UnicodeText, false) || !String.Equals(args.Data.GetData(DataFormats.UnicodeText, false) as string, _dragToken, StringComparison.Ordinal)) return null;
			Point point = _grid.PointToClient(new Point(args.X, args.Y)); var hit = _view.CalcHitInfo(point);
			LoadOrderModuleRow row = hit.InRow ? _view.GetRow(hit.RowHandle) as LoadOrderModuleRow : null;
			if (row?.CanToggle != true || !row.Position.HasValue) return null;
			GridRowInfo rowInfo = (_view.GetViewInfo() as GridViewInfo)?.GetGridRowInfo(hit.RowHandle);
			if (rowInfo == null || rowInfo.Bounds.Height <= 0) return null;
			Rectangle rectangle = rowInfo.Bounds;
			after = point.Y > rectangle.Top + rectangle.Height / 2; return row;
		}

		/// <summary>Provides a valid move cursor and destination feedback, without mutating the draft while hovering.</summary>
		private void GridDragOver(object sender, DragEventArgs args)
		{
			LoadOrderModuleRow target = DragTarget(args, out bool after); args.Effect = target == null ? DragDropEffects.None : DragDropEffects.Move;
			if (target == null) return;
			_view.FocusedRowHandle = _view.LocateByValue(nameof(LoadOrderModuleRow.Key), target.Key);
			_status.Text = String.Format(after ? T("DropAfter", "Move {0} after {1}") : T("DropBefore", "Move {0} before {1}"), _dragCandidate.Name, target.Name);
		}

		/// <summary>Applies a drop to the actual included order and refreshes the selected module's position and diagnosis.</summary>
		private void GridDragDrop(object sender, DragEventArgs args)
		{
			LoadOrderModuleRow target = DragTarget(args, out bool after); if (target == null) return;
			CancelPendingAutomaticFix(); _session.MoveRelative(_dragCandidate, target, after); BindRows(_dragCandidate.Key); UpdateStatus();
		}

		/// <summary>Offers standard keyboard save and accessible reordering without requiring drag precision.</summary>
		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == (Keys.Control | Keys.S)) { _ = SaveAsync(); return true; }
			if (_grid.ContainsFocus && keyData == (Keys.Alt | Keys.Up)) { MoveSelected(-1); return true; }
			if (_grid.ContainsFocus && keyData == (Keys.Alt | Keys.Down)) { MoveSelected(1); return true; }
			return base.ProcessCmdKey(ref msg, keyData);
		}

		/// <summary>Communicates dirty, stale and blocked states without relying on a color alone.</summary>
		private void UpdateStatus()
		{
			if (_session == null) return;
			if (_session.IsInspecting) { UpdateCommands(); return; }
			_status.Text = !_fresh ? T("Stale", "Files or mod deployment changed. Your draft is preserved; refresh before saving.")
				: _session.IsDirty ? T("Unsaved", "Unsaved changes · Save writes this order to the game. Discard restores the last scan.")
				: !_session.Analysis.CanPrepareRepair ? T("Blocked", "Resolve the inventory or recovery problems before editing. Fix automatically can review eligible folder cleanup.")
				: String.Format(T("Ready", "Saved order · {0} included modules. Check inclusion, then move modules into the order you want."), _session.Included.Count);
			Text = T("Title", "Load Order") + (_session.IsDirty ? " *" : String.Empty); UpdateCommands();
		}

		/// <summary>Disables contradictory actions during reads, review, native activity and stale observations.</summary>
		private void UpdateCommands()
		{
			if (_progressItem == null || _cancelItem == null || _up == null || _down == null) return;
			bool idle = !HasActiveSessionWork && _changes?.HasRunningNativeTask != true && _main?.IsSwitching != true;
			_profile.Enabled = idle; _refresh.Enabled = idle && _service != null; _discard.Enabled = !HasActiveSessionWork && _session?.IsDirty == true;
			_automaticFix.Enabled = idle && _selectedProfile != null;
			_save.Enabled = _dragToken == null && CanEdit && _session.IsDirty; _fix.Enabled = idle && ObservationCurrent && _session != null && !_session.IsDirty && !_session.Analysis.Issues.Any(issue => issue.Kind == LoadOrderIssueKind.RecoveryRequired);
			_recovery.Enabled = idle && _selectedProfile != null; _cancel.Enabled = _busy;
			_exportReport.Enabled = !HasActiveSessionWork && _session != null;
			_cancelItem.Visibility = _busy ? LayoutVisibility.Always : LayoutVisibility.Never; _progressItem.Visibility = _busy ? LayoutVisibility.Always : LayoutVisibility.Never;
			LoadOrderModuleRow row = _view.GetFocusedRow() as LoadOrderModuleRow;
			bool move = _dragToken == null && CanEdit && row?.CanToggle == true && row.Position.HasValue && String.IsNullOrEmpty(_view.ActiveFilterString) && String.IsNullOrEmpty(_view.FindFilterText);
			_up.Enabled = move && row.Position > 1; _down.Enabled = move && row.Position < _session.Included.Count;
		}

		/// <summary>Applies current user fonts, density and palette to all grids, editors and token badges.</summary>
		internal void ApplyDisplaySettings(DevExpressDisplaySettings settings)
		{
			_displaySettings = settings;
			if (settings != null)
			{
				Font = settings.Font; DevExpressDisplaySettingsApplier.ApplyToControlTree(this, settings); DevExpressDisplaySettingsApplier.ApplyToRepositoryItem(_selectionBadges.Properties, settings);
				_badgeToolTips.Appearance.Font = settings.Font; _badgeToolTips.AppearanceTitle.Font = settings.Font;
				_view.RowHeight = Math.Max(_view.RowHeight, settings.Font.Height + 8);
				_view.Columns[nameof(LoadOrderModuleRow.Included)].MaxWidth = TextRenderer.MeasureText(_view.Columns[nameof(LoadOrderModuleRow.Included)].Caption, settings.Font).Width + 24;
				_view.Columns[nameof(LoadOrderModuleRow.Position)].MaxWidth = TextRenderer.MeasureText(_view.Columns[nameof(LoadOrderModuleRow.Position)].Caption, settings.Font).Width + 24;
			}
			NmmColorPalette palette = NmmColorPalette.Resolve(NmmIconProvider.CurrentColorProfile, DevExpressDisplaySettingsApplier.IsDarkSkinSurface());
			_errorColor = palette.PluginErrorColor; _warningColor = palette.PluginWarningColor; _issuesView.InvalidateRows();
			_grid.Invalidate(); _selectionBadges.Invalidate();
		}

		/// <summary>Reports the failure and any retained recovery directory; no native error is treated as a successful save.</summary>
		private void ShowError(Exception error)
		{
			Trace.TraceWarning("Load Order UI: {0}", error);
			string directory = (error as LoadOrderAutomaticRepairException)?.RetainedContentDirectory ?? (error as LoadOrderFolderRepairException)?.RetainedContentDirectory;
			XtraMessageBox.Show(this, error.Message + (String.IsNullOrEmpty(directory) ? String.Empty : Environment.NewLine + directory), T("ErrorTitle", "Load Order could not complete the operation"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		/// <summary>Observes external package-folder and manifest changes; events invalidate rather than editing or scanning on the callback thread.</summary>
		private void WatchFiles(LoadOrderAnalysis analysis, string packageRoot = null)
		{
			_packageWatch?.Dispose(); _manifestWatch?.Dispose(); _packageWatch = _manifestWatch = null;
			if (analysis == null) return;
			try
			{
				_manifestWatch = CreateWatcher(Path.GetDirectoryName(analysis.Document.FilePath), Path.GetFileName(analysis.Document.FilePath) + "*");
				packageRoot = packageRoot ?? analysis.Inventory.PackageRoot;
				if (!String.IsNullOrEmpty(packageRoot)) _packageWatch = CreateWatcher(packageRoot, "*");
			}
			catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is System.Security.SecurityException)
			{ Trace.TraceWarning("Load Order external-change monitoring unavailable; explicit refresh/save preflight remain authoritative: {0}", error.Message); }
		}

		/// <summary>Watches the top level only, including unexpected file/directory creation and atomic manifest replacement.</summary>
		private FileSystemWatcher CreateWatcher(string directory, string filter)
		{
			var watcher = new FileSystemWatcher(directory, filter) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
			watcher.Changed += (sender, args) => ExternalFileChanged(); watcher.Created += (sender, args) => ExternalFileChanged();
			watcher.Deleted += (sender, args) => ExternalFileChanged(); watcher.Renamed += (sender, args) => ExternalFileChanged();
			watcher.Error += (sender, args) => ExternalFileChanged();
			try { watcher.EnableRaisingEvents = true; return watcher; }
			catch { watcher.Dispose(); throw; }
		}

		/// <summary>Separates real filesystem changes during a scan from the scan's own native-authority reload notifications.</summary>
		private void ExternalFileChanged() { Interlocked.Increment(ref _fileRevision); _changes.NotifyExternalChange(); }

		/// <summary>Creates a readonly grid whose visual order cannot be mistaken for sorting the game's actual order.</summary>
		internal static GridControl CreateGrid(out GridView view, bool editable)
		{
			var grid = new GridControl(); view = new GridView(grid); grid.MainView = view; grid.ViewCollection.Add(view);
			view.OptionsBehavior.AutoPopulateColumns = false; view.OptionsBehavior.Editable = editable;
			view.OptionsView.ShowGroupPanel = false; view.OptionsView.ShowIndicator = false;
			view.OptionsCustomization.AllowSort = false; view.OptionsCustomization.AllowGroup = false;
			view.OptionsSelection.EnableAppearanceFocusedCell = false; return grid;
		}

		/// <summary>Uses skinned, read-only icon-and-label badges; glyphs never remove content or imply an editing action.</summary>
		private static RepositoryItemTokenEdit CreateBadgeRepository()
		{
			var tokens = new RepositoryItemTokenEdit(); ConfigureBadgeRepository(tokens); return tokens;
		}

		/// <summary>Configures each display independently so repository assignment cannot duplicate or drop custom-draw event handlers.</summary>
		private static void ConfigureBadgeRepository(RepositoryItemTokenEdit tokens)
		{
			tokens.ReadOnly = true; tokens.BorderStyle = BorderStyles.NoBorder;
			tokens.EditValueType = TokenEditValueType.String; tokens.EditValueSeparatorChar = ',';
			tokens.ShowTokenGlyph = true; tokens.TokenGlyphLocation = TokenEditGlyphLocation.Left; tokens.DeleteTokenOnGlyphClick = DefaultBoolean.False;
			tokens.ShowRemoveTokenButtons = false; tokens.ShowDropDown = false; tokens.ShowDropDownOnClick = DefaultBoolean.False;
			tokens.CustomDrawTokenGlyph += DrawBadgeGlyph;
			foreach (string value in new[] { "Game", "Lua", "Osiris", "Extender", "Fixer", "Override", "Unverified" })
				tokens.Tokens.Add(new TokenEditToken(BadgeCaption(value), value));
		}

		/// <summary>Draws only the glyph with the shared icon palette; DevExpress retains skin, text and DPI layout ownership.</summary>
		private static void DrawBadgeGlyph(object sender, TokenEditCustomDrawTokenGlyphEventArgs args)
		{
			string value = args.Value as string;
			NmmIconAction action = value == "Game" ? NmmIconAction.GameContent : value == "Lua" || value == "Osiris" ? NmmIconAction.Script
				: value == "Extender" ? NmmIconAction.SupportedTools : value == "Fixer" ? NmmIconAction.Repair
				: value == "Override" ? NmmIconAction.Copy : NmmIconAction.Warning;
			Rectangle bounds = args.GlyphBounds;
			int size = Math.Min(bounds.Width, bounds.Height);
			if (size > 0)
			{
				Image icon = NmmIconProvider.GetBitmap(action, size, false);
				if (icon != null) args.Cache.DrawImage(icon, new Rectangle(bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size));
			}
			args.Handled = true;
		}

		/// <summary>Explains the content flags on the hovered row without changing row focus or opening an editor.</summary>
		private void BadgeToolTipInfo(object sender, ToolTipControllerGetActiveObjectInfoEventArgs args)
		{
			if (args.SelectedControl != _grid || args.Info != null) return;
			var hit = _view.CalcHitInfo(args.ControlMousePosition);
			if (!hit.InRowCell || hit.Column?.FieldName != nameof(LoadOrderModuleRow.Badges)) return;
			LoadOrderModuleRow row = _view.GetRow(hit.RowHandle) as LoadOrderModuleRow;
			string description = BadgeDescriptions(row?.Badges);
			if (!String.IsNullOrEmpty(description)) args.Info = new ToolTipControlInfo(row, description);
		}

		/// <summary>Preserves short recognizable labels alongside the icons.</summary>
		private static string BadgeCaption(string value) => T("Badges." + value, value == "Extender" ? "Script Extender" : value == "Fixer" ? "Mod Fixer" : value);

		/// <summary>Explains positive feature evidence and uncertainty without equating module exclusion with uninstalling a package.</summary>
		private static string BadgeDescriptions(string badges)
		{
			if (String.IsNullOrEmpty(badges)) return String.Empty;
			return String.Join(Environment.NewLine + Environment.NewLine, badges.Split(',').Select(value => BadgeCaption(value) + ": " + T("Badges.Hint." + value,
				value == "Game" ? "Game-owned content. This entry stays included and cannot be moved."
				: value == "Lua" ? "Contains Lua scripts. Excluding its module may not stop scripts from running; disable or uninstall the mod through NMM to remove its installed content."
				: value == "Osiris" ? "Contains Osiris story scripts. Check the mod author's compatibility and save-game guidance."
				: value == "Extender" ? "Contains Script Extender content or declares a requirement. Check the details and verify that a compatible runtime is installed."
				: value == "Fixer" ? "Contains embedded Mod Fixer content. Check the author's guidance for compatibility with your game version."
				: value == "Override" ? "Overrides game files independently of module inclusion and ordering. Disable or uninstall the mod through NMM to stop its physical effects."
				: "Package inspection is pending, incomplete or ambiguous. Missing badges do not prove that scripts or overrides are absent.")));
		}

		/// <summary>Exports the visible observation, including partial or stale evidence, without running a new inspection or native operation.</summary>
		private void ExportDiagnosticReport()
		{
			if (HasActiveSessionWork || _session == null) return;
			try
			{
				string report = LoadOrderDiagnosticReport.Create(_session.Analysis, ((ILoadOrderCapabilityProvider)_main.GameMode).LoadOrder,
					_service.ModeId, _selectedProfile, _session.Included, !_session.IsInspecting, ObservationCurrent, _observedAtUtc, _lastOperationError, _lastRecovery);
				using (var dialog = new XtraSaveFileDialog
				{
					Title = T("ExportReport", "Export diagnostic report…"), Filter = T("ReportFilter", "Diagnostic reports (*.json)|*.json"),
					DefaultExt = "json", AddExtension = true, RestoreDirectory = true, FileName = "NMM-LoadOrder-Report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", OverwritePrompt = true
				})
				{
					if (dialog.ShowDialog(this) != DialogResult.OK) return;
					File.WriteAllText(dialog.FileName, report, new System.Text.UTF8Encoding(false));
					_status.Text = String.Format(T("ReportExported", "Diagnostic report saved: {0}"), dialog.FileName);
				}
			}
			catch (Exception error) when (!(error is OutOfMemoryException)) { ShowError(error); }
		}

		/// <summary>Creates a keyboard-accessible DevExpress command without a fixed font or color.</summary>
		private static SimpleButton Button(string text, Func<Task> action)
		{
			var button = new SimpleButton { Text = text, AutoSize = true, AccessibleName = text };
			button.Click += async (sender, args) => await action(); return button;
		}

		/// <summary>Uses the shared localized page resource namespace.</summary>
		internal static string T(string key, string fallback) => LoadOrderEditorSession.T(key, fallback);

		/// <summary>Releases invalidation subscriptions and cancellation when the per-game document is disposed.</summary>
		protected override void Dispose(bool disposing)
		{
			if (disposing && !_released)
			{
				_released = true; _cancellation?.Cancel(); _refreshTimer.Stop(); _refreshTimer.Dispose(); _packageWatch?.Dispose(); _manifestWatch?.Dispose(); _badgeToolTips?.Dispose();
				if (_changes != null) { _changes.OperationCompleted -= NativeOperationCompleted; _changes.Changed -= NativeChanged; _changes.Dispose(); }
			}
			base.Dispose(disposing);
		}

		/// <summary>One worker observation transferred to the UI as a unit.</summary>
		private sealed class PageRead
		{
			internal string[] Profiles; internal string Profile; internal string PackageRoot; internal LarianLoadOrderDocument Document; internal LoadOrderAnalysis Analysis; internal NativeLoadOrderRecovery Recovery; internal Exception Error; internal int Revision; internal int FileRevision;
		}
		/// <summary>Readable dependency/conflict fields with no live native services retained by grid binding.</summary>
		private sealed class RelationRow { public string Kind { get; set; } public string Name { get; set; } public string Version { get; set; } public string State { get; set; } }
		/// <summary>Readable issue subjects, preserving full diagnostic evidence for the details pane.</summary>
		private sealed class IssueRow
		{
			/// <summary>Resolves displayed module names without replacing stable issue identifiers.</summary>
			internal IssueRow(LoadOrderIssue issue, LoadOrderEditorSession session)
			{ Issue = issue; Severity = T("Severity." + issue.Severity, issue.Severity.ToString()); Subject = String.Join(" → ", issue.ModuleIdentities.Select(session.NameFor)); if (String.IsNullOrEmpty(Subject)) Subject = issue.Path; Message = issue.Message; }
			internal LoadOrderIssue Issue { get; }
			public string Severity { get; } public string Subject { get; } public string Message { get; }
		}
	}
}
