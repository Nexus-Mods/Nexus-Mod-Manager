using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands;
using Nexus.Client.ModManagement;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.DownloadMonitoring.UI
{
	/// <summary>
	/// The view that exposes Download monitoring functionality.
	/// </summary>
	public partial class DownloadMonitorControl : XtraUserControl
	{
		private readonly BindingList<DownloadTaskRow> _rows = new BindingList<DownloadTaskRow>();
		private DownloadMonitorVM m_vmlViewModel;
		private readonly string _titleAllActive = LanguageManager.GetFormat("MainForm.Dock.DownloadManager.Count", "Download Manager ({0})");
		private readonly string _titleSomeActive = LanguageManager.GetFormat("MainForm.Dock.DownloadManager.ActiveCount", "Download Manager ({0}/{1})");
		private const string ColumnWidthsSettingsKey = "DownloadMonitor";
		private bool _columnWidthsRestored;
		private bool _formClosingHooked;

		public event EventHandler SetTextBoxFocus;

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DownloadMonitorVM ViewModel
		{
			get { return m_vmlViewModel; }
			set
			{
				m_vmlViewModel = value;
				_rows.Clear();
				if (m_vmlViewModel == null)
					return;

				foreach (AddModTask task in m_vmlViewModel.Tasks)
					AddTaskToList(task);

				m_vmlViewModel.ActiveTasks.CollectionChanged += ActiveTasks_CollectionChanged;
				m_vmlViewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;

				new DevExpressBarItemCommandBinding<AddModTask>(tsbCancel, m_vmlViewModel.CancelTaskCommand, GetSelectedTask, true);
				new DevExpressBarItemCommandBinding<AddModTask>(tsbRemove, m_vmlViewModel.RemoveTaskCommand, GetSelectedTask, true);
				new DevExpressBarItemCommandBinding<AddModTask>(tsbPause, m_vmlViewModel.PauseTaskCommand, GetSelectedTask, true);
				new DevExpressBarItemCommandBinding<AddModTask>(tsbResume, m_vmlViewModel.ResumeTaskCommand, GetSelectedTask, true);

				Command removeAll = new Command(
					LanguageManager.Get("Downloads.Actions.RemoveAll.Name", "Remove all"),
					LanguageManager.Get("Downloads.Actions.RemoveAll.Description", "Purges the completed/failed downloads from the list."),
					ViewModel.RemoveAllTasks);
				new DevExpressBarItemCommandBinding(tsbRemoveAll, removeAll);
				Command resumeAll = new Command(
					LanguageManager.Get("Downloads.Actions.ResumeAll.Name", "Resume all"),
					LanguageManager.Get("Downloads.Actions.ResumeAll.Description", "Resumes all paused/queued downloads."),
					ViewModel.ResumeAllTasks);
				new DevExpressBarItemCommandBinding(tsbResumeAll, resumeAll);
				Command purgeDownloads = new Command(
					LanguageManager.Get("Downloads.Actions.Purge.Name", "Purge Downloads"),
					LanguageManager.Get("Downloads.Actions.Purge.Description", "Purges the paused/queued downloads from the list."),
					ViewModel.PurgeDownloads);
				new DevExpressBarItemCommandBinding(tsbPurgeDownloads, purgeDownloads);

				m_vmlViewModel.PurgingDownloads += ViewModel_PurgingDownloads;
				ViewModel.CancelTaskCommand.CanExecute = false;
				ViewModel.RemoveTaskCommand.CanExecute = false;
				ViewModel.PauseTaskCommand.CanExecute = false;
				ViewModel.ResumeTaskCommand.CanExecute = false;
				UpdateTitle();
				InitializeColumnWidthPersistence();
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DownloadMonitorControl"/> class.
		/// </summary>
		public DownloadMonitorControl()
		{
			InitializeComponent();
			Disposed += DownloadMonitorControl_Disposed;
			ApplyLocalization();
			NmmIconProvider.Bind(tsbResume, NmmIconAction.Resume);
			NmmIconProvider.Bind(tsbCancel, NmmIconAction.Cancel);
			NmmIconProvider.Bind(tsbPause, NmmIconAction.Pause);
			NmmIconProvider.Bind(tsbRemove, NmmIconAction.Remove);
			NmmIconProvider.Bind(tsbResumeAll, NmmIconAction.Resume);
			NmmIconProvider.Bind(tsbRemoveAll, NmmIconAction.RemoveAll);
			NmmIconProvider.Bind(tsbPurgeDownloads, NmmIconAction.Purge);
			NmmIconProvider.Bind(copyItem, NmmIconAction.Copy);
			NmmIconProvider.BindBar(barActions, NmmButtonPresentationScope.DownloadManager, true);
			DevExpressDisplaySettingsApplier.NormalizeBarItemImages(barManager, new System.Drawing.Size(32, 32));
			gridControl.DataSource = _rows;
			gridView.OptionsView.ColumnAutoWidth = true;
			ConfigureProgressPresentation();
			UpdateTitle();
		}

		/// <summary>
		/// Adds native Excel-style data bars without changing persisted column field names.
		/// </summary>
		private void ConfigureProgressPresentation()
		{
			gridView.OptionsView.ColumnAutoWidth = false;
			gridView.Columns.ColumnByFieldName("OverallMessage").Width = 420;
			gridView.Columns.ColumnByFieldName("OverallMessage").MinWidth = 180;
			gridView.Columns.ColumnByFieldName("Status").Width = 100;
			gridView.Columns.ColumnByFieldName("ItemMessage").Width = 100;
			gridView.Columns.ColumnByFieldName("FileServer").Width = 100;
			gridView.Columns.ColumnByFieldName("ETA").Width = 75;
			gridView.Columns.ColumnByFieldName("ItemProgress").Width = 90;
			DevExpress.XtraGrid.Columns.GridColumn progress = gridView.Columns.ColumnByFieldName("OverallProgress");
			progress.MinWidth = 150;
			progress.Width = 190;
			progress.AppearanceCell.TextOptions.HAlignment = DevExpress.Utils.HorzAlignment.Center;
			gridView.Columns.ColumnByFieldName("Status").MinWidth = 85;
			gridView.Columns.ColumnByFieldName("ETA").MinWidth = 65;
			gridView.RowHeight = Font.Height + 12;
			gridView.OptionsView.ShowVerticalLines = DevExpress.Utils.DefaultBoolean.False;
			gridView.OptionsView.ColumnAutoWidth = true;
			gridView.FormatRules.Add(new DevExpress.XtraGrid.GridFormatRule
			{
				Column = progress,
				Rule = new FormatConditionRuleDataBar
				{
					MinimumType = FormatConditionValueType.Number,
					MaximumType = FormatConditionValueType.Number,
					Minimum = 0,
					Maximum = 100,
					PredefinedName = "Green",
					ShowBarOnly = false
				}
			});
			gridView.CustomColumnDisplayText += GridView_CustomColumnDisplayText;
		}

		/// <summary>
		/// Keeps transferred sizes and task state readable over the numeric progress bar.
		/// </summary>
		private void GridView_CustomColumnDisplayText(object sender, DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventArgs e)
		{
			if (e.Column.FieldName != "OverallProgress" || e.ListSourceRowIndex < 0)
				return;
			DownloadTaskRow row = gridView.GetRow(gridView.GetRowHandle(e.ListSourceRowIndex)) as DownloadTaskRow;
			if (row != null)
				e.DisplayText = row.ProgressText;
		}


		/// <summary>
		/// Applies static UI text once when the control is created.
		/// </summary>
		private void ApplyLocalization()
		{
			barActions.BarName = LanguageManager.Get("Downloads.Toolbar.Title", "Download Actions");
			SetBarItemText(tsbResume, LanguageManager.Get("Downloads.Actions.Resume.Name", "Resume"));
			SetBarItemText(tsbCancel, LanguageManager.Get("Common.Action.Cancel", "Cancel"));
			SetBarItemText(tsbPause, LanguageManager.Get("Downloads.Actions.Pause.Name", "Pause"));
			SetBarItemText(tsbRemove, LanguageManager.Get("Downloads.Actions.Remove.Name", "Remove"));
			SetBarItemText(tsbResumeAll, LanguageManager.Get("Downloads.Actions.ResumeAll.Name", "Resume all"));
			SetBarItemText(tsbRemoveAll, LanguageManager.Get("Downloads.Actions.RemoveAll.Name", "Remove all"));
			SetBarItemText(tsbPurgeDownloads, LanguageManager.Get("Downloads.Actions.Purge.Name", "Purge Downloads"));
			copyItem.Caption = LanguageManager.Get("Common.Action.CopyToClipboard", "Copy to clipboard");

			SetColumnCaption("OverallMessage", LanguageManager.Get("Common.Column.Name", "Name"));
			SetColumnCaption("OverallProgress", LanguageManager.Get("Downloads.Columns.Progress", "Progress"));
			SetColumnCaption("Status", LanguageManager.Get("Common.Column.Status", "Status"));
			SetColumnCaption("ItemMessage", LanguageManager.Get("Downloads.Columns.SpeedStep", "Speed / Step"));
			SetColumnCaption("FileServer", LanguageManager.Get("Downloads.Columns.FileServer", "Fileserver"));
			SetColumnCaption("ETA", LanguageManager.Get("Downloads.Columns.Eta", "ETA"));
			SetColumnCaption("ItemProgress", LanguageManager.Get("Downloads.Columns.ThreadsStep", "Threads / Step"));
		}

		private static void SetBarItemText(BarItem item, string text)
		{
			item.Caption = text;
			item.Hint = text;
		}

		private void SetColumnCaption(string fieldName, string caption)
		{
			DevExpress.XtraGrid.Columns.GridColumn column = gridView.Columns.ColumnByFieldName(fieldName);
			if (column != null)
				column.Caption = caption;
		}

		/// <summary>
		/// Restores persisted column widths and hooks persistence to the owning form.
		/// </summary>
		/// <param name="e">The load event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			NmmIconProvider.BindBar(barActions, NmmButtonPresentationScope.DownloadManager, true);
			InitializeColumnWidthPersistence();
		}

		/// <summary>
		/// Restores persisted widths once and hooks the owning form once, regardless of whether
		/// the view model or the DevExpress dock host becomes available first.
		/// </summary>
		private void InitializeColumnWidthPersistence()
		{
			if (DesignMode || ViewModel == null)
				return;

			if (!_columnWidthsRestored)
				RestorePersistedColumnWidths();

			Form owner = FindForm();
			if (!_formClosingHooked && owner != null)
			{
				owner.FormClosing += DownloadMonitorControl_FormClosing;
				_formClosingHooked = true;
			}
		}

		/// <summary>
		/// Restores the saved widths after the owning dock panel has reached its final size.
		/// </summary>
		internal void RestorePersistedColumnWidths()
		{
			if (DesignMode || ViewModel == null)
				return;

			DevExpressGridLayoutPersistence.RestoreColumnWidths(gridView, ViewModel.Settings.ColumnWidths[ColumnWidthsSettingsKey]);
			_columnWidthsRestored = true;
		}

		/// <summary>
		/// Saves the current DevExpress grid column widths when the main form closes.
		/// </summary>
		private void DownloadMonitorControl_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (ViewModel == null)
				return;

			ViewModel.Settings.ColumnWidths[ColumnWidthsSettingsKey] = DevExpressGridLayoutPersistence.CaptureColumnWidths(gridView);
			ViewModel.Settings.Save();
		}

		private AddModTask GetSelectedTask()
		{
			DownloadTaskRow row = gridView.GetFocusedRow() as DownloadTaskRow;
			return row == null ? null : row.Task;
		}

		/// <summary>
		/// Updates the executable and visible state of per-download commands.
		/// </summary>
		protected void SetCommandExecutableStatus()
		{
			if (ViewModel == null)
				return;

			AddModTask task = GetSelectedTask();
			ViewModel.CancelTaskCommand.CanExecute = task != null && ViewModel.CanCancelTask(task);
			ViewModel.RemoveTaskCommand.CanExecute = task != null && ViewModel.CanRemoveDownload(task);
			ViewModel.PauseTaskCommand.CanExecute = task != null && ViewModel.CanPauseDownload(task) && !ViewModel.ModRepository.IsOffline;
			ViewModel.ResumeTaskCommand.CanExecute = task != null && ViewModel.CanResumeDownload(task);

		}

		/// <summary>
		/// Adds a download task to the DevExpress grid if it is not already present.
		/// </summary>
		protected void AddTaskToList(AddModTask task)
		{
			foreach (DownloadTaskRow row in _rows)
				if (row.Task == task)
					return;

			task.PropertyChanged -= Task_PropertyChanged;
			task.PropertyChanged += Task_PropertyChanged;
			_rows.Add(new DownloadTaskRow(task));
		}

		/// <summary>Persist current columns and release the owner-form hook before detaching game services.</summary>
		internal void DetachFromSessionHost(Form owner)
		{
			if (!_formClosingHooked) return;
			DownloadMonitorControl_FormClosing(this, new FormClosingEventArgs(CloseReason.None, false));
			owner.FormClosing -= DownloadMonitorControl_FormClosing;
			_formClosingHooked = false;
		}

		private void DownloadMonitorControl_Disposed(object sender, EventArgs e)
		{
			if (ViewModel != null)
			{
				ViewModel.ActiveTasks.CollectionChanged -= ActiveTasks_CollectionChanged;
				ViewModel.Tasks.CollectionChanged -= Tasks_CollectionChanged;
				ViewModel.PurgingDownloads -= ViewModel_PurgingDownloads;
			}
			foreach (DownloadTaskRow row in _rows)
				row.Task.PropertyChanged -= Task_PropertyChanged;
		}

		/// <summary>Posts a display update without making download workers wait for a closing UI thread.</summary>
		private void PostMonitorUpdate(Action update)
		{
			if (IsDisposed || Disposing || !IsHandleCreated) return;
			try
			{
				BeginInvoke((MethodInvoker)(() =>
				{
					if (!IsDisposed && !Disposing && IsHandleCreated) update();
				}));
			}
			catch (InvalidOperationException)
			{
				if (!IsDisposed && !Disposing && IsHandleCreated) throw;
			}
		}

		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (IsDisposed || Disposing) return;
			if (gridControl.InvokeRequired)
			{
				PostMonitorUpdate(() => Tasks_CollectionChanged(sender, e));
				return;
			}

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (AddModTask task in e.NewItems)
						AddTaskToList(task);
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (AddModTask task in e.OldItems)
					{
						for (int i = _rows.Count - 1; i >= 0; i--)
							if (_rows[i].Task == task)
								_rows.RemoveAt(i);
						task.PropertyChanged -= Task_PropertyChanged;
					}
					break;
				case NotifyCollectionChangedAction.Reset:
					_rows.Clear();
					break;
				case NotifyCollectionChangedAction.Move:
					break;
				default:
					throw new Exception("Unrecognized value for NotifyCollectionChangedAction.");
			}

			UpdateTitle();
		}

		private void ActiveTasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (IsDisposed || Disposing || !IsHandleCreated || ViewModel == null)
				return;

			lock (ViewModel.ModRepository)
			{
				if (ViewModel.ModRepository.IsOffline)
					return;

				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
					case NotifyCollectionChangedAction.Replace:
						foreach (AddModTask task in e.NewItems)
						{
							if (ViewModel.ModRepository.IsOffline)
								m_vmlViewModel.PauseTask(task);
							else if (m_vmlViewModel.RunningTasks.Count > m_vmlViewModel.MaxConcurrentDownloads && task.IsRemote)
								m_vmlViewModel.QueueTask(task);
						}
						break;
					case NotifyCollectionChangedAction.Remove:
						foreach (AddModTask task in e.OldItems)
						{
							if (m_vmlViewModel.RunningTasks.Count < m_vmlViewModel.MaxConcurrentDownloads && task.IsRemote)
							{
								AddModTask queuedTask = m_vmlViewModel.QueuedTask;
								if (queuedTask != null)
									m_vmlViewModel.ResumeTask(queuedTask);
							}
						}
						break;
					default:
						throw new Exception("Unrecognized value for NotifyCollectionChangedAction.");
				}
			}

			if (gridControl.InvokeRequired)
				PostMonitorUpdate(UpdateTitle);
			else
				UpdateTitle();
		}

		/// <summary>
		/// Updates the dock title to reflect active and total downloads.
		/// </summary>
		protected void UpdateTitle()
		{
			int activeCount = ViewModel == null ? 0 : ViewModel.ActiveTasks.Count;
			int totalCount = ViewModel == null ? 0 : ViewModel.Tasks.Count;
			Text = totalCount == activeCount
				? string.Format(_titleAllActive, totalCount)
				: string.Format(_titleSomeActive, activeCount, totalCount);
		}

		private void ViewModel_PurgingDownloads(object sender, EventArgs<IBackgroundTask> e)
		{
			if (IsDisposed || Disposing) return;
			if (InvokeRequired)
			{
				PostMonitorUpdate(() => ViewModel_PurgingDownloads(sender, e));
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument, true);
		}

		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (IsDisposed || Disposing) return;
			if (gridControl.InvokeRequired)
			{
				PostMonitorUpdate(() => Task_PropertyChanged(sender, e));
				return;
			}

			gridView.RefreshData();
			if (e.PropertyName == ObjectHelper.GetPropertyName<AddModTask>(x => x.Status))
				SetCommandExecutableStatus();
		}

		private void gridView_FocusedRowChanged(object sender, DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventArgs e)
		{
			SetCommandExecutableStatus();
		}

		private void gridControl_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.KeyData == (Keys.C | Keys.Control))
				CopyFocusedDownload();
			else if (e.KeyData == (Keys.Control | Keys.F) && SetTextBoxFocus != null)
				SetTextBoxFocus(this, e);
		}

		private void gridControl_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
				return;
			popupMenu.ShowPopup(gridControl.PointToScreen(e.Location));
		}

		private void copyItem_ItemClick(object sender, ItemClickEventArgs e)
		{
			CopyFocusedDownload();
		}

		/// <summary>
		/// Copies the focused download name to the clipboard.
		/// </summary>
		private void CopyFocusedDownload()
		{
			DownloadTaskRow row = gridView.GetFocusedRow() as DownloadTaskRow;
			if (row != null && !string.IsNullOrEmpty(row.OverallMessage))
				Clipboard.SetText(row.OverallMessage);
		}

		/// <summary>
		/// Presents a download task as a row in the DevExpress grid.
		/// </summary>
		private sealed class DownloadTaskRow
		{
			private static readonly string WorkingText = LanguageManager.Get("Common.Status.Working", "Working...");
			private static readonly string RetryingText = LanguageManager.Get("Common.Status.Retrying", "Retrying");
			private static readonly string DownloadingText = LanguageManager.Get("Downloads.Status.Downloading", "Downloading");
			private static readonly string MovingText = LanguageManager.Get("Downloads.Status.Moving", "Moving");
			private static readonly string IncompleteText = LanguageManager.Get("Common.Status.Incomplete", "Incomplete");
			private static readonly string CompleteText = LanguageManager.Get("Common.Status.Complete", "Complete");
			private static readonly string CancelledText = LanguageManager.Get("Common.Status.Cancelled", "Cancelled");
			private static readonly string CancellingText = LanguageManager.Get("Common.Status.Cancelling", "Cancelling");
			private static readonly string PausedText = LanguageManager.Get("Common.Status.Paused", "Paused");
			private static readonly string RunningText = LanguageManager.Get("Common.Status.Running", "Running");
			private static readonly string ErrorText = LanguageManager.Get("Common.Status.Error", "Error");
			private static readonly string QueuedText = LanguageManager.Get("Common.Status.Queued", "Queued");

			/// <summary>
			/// Initializes a new download row.
			/// </summary>
			public DownloadTaskRow(AddModTask task)
			{
				Task = task;
			}

			public AddModTask Task { get; private set; }
			public string OverallMessage { get { return Task.OverallMessage; } }
			/// <summary>Gets a bounded percentage, leaving unknown totals without a misleading bar.</summary>
			public double? OverallProgress
			{
				get
				{
					if (Task.Status == TaskStatus.Complete)
						return 100;
					if (Task.ShowOverallProgressAsMarquee || Task.DownloadMaximum <= 0)
						return null;
					return Math.Max(0, Math.Min(100, 100.0 * Task.DownloadProgress / Task.DownloadMaximum));
				}
			}

			/// <summary>Gets the localized status or transferred sizes displayed over the bar.</summary>
			public string ProgressText
			{
				get
				{
					if (Task.Status == TaskStatus.Complete)
						return CompleteText;
					if (Task.ShowOverallProgressAsMarquee)
						return WorkingText;
					if (Task.DownloadMaximum <= 0)
						return string.Empty;
					if (Task.Status != TaskStatus.Running && Task.Status != TaskStatus.Paused)
						return string.Empty;
					string transferred = Task.DownloadMaximum < 1024
						? string.Format("{0}KB / {1}KB", Task.DownloadProgress, Task.DownloadMaximum)
						: string.Format("{0}MB / {1}MB", Task.DownloadProgress / 1024, Task.DownloadMaximum / 1024);
					return string.Format("{0:0}%  {1}", OverallProgress, transferred);
				}
			}
			public string Status
			{
				get
				{
					if (Task.InnerTaskStatus == TaskStatus.Retrying && Task.Status != TaskStatus.Paused && Task.Status != TaskStatus.Queued)
						return RetryingText;
					if (Task.Status == TaskStatus.Running)
						return Task.IsRemote ? DownloadingText : MovingText;
					return GetStatusText(Task.Status);
				}
			}
			public string ItemMessage
			{
				get
				{
					if (Task.Status == TaskStatus.Error || Task.Status == TaskStatus.Incomplete)
						return Task.ItemMessage;
					if (Task.Status != TaskStatus.Running)
						return string.Empty;
					if (Task.TaskSpeed > 0)
						return string.Format("{0} KB/s", Task.TaskSpeed);
					return Task.ShowItemProgress ? Task.ItemMessage : string.Empty;
				}
			}
			public string FileServer { get { return Task.Status == TaskStatus.Running ? Task.FileServer : string.Empty; } }
			public string ETA { get { return Task.Status == TaskStatus.Running ? string.Format("{0:00}:{1:00}:{2:00}", Task.ETA_Hours, Task.ETA_Minutes, Task.ETA_Seconds) : string.Empty; } }
			private static string GetStatusText(TaskStatus status)
			{
				switch (status)
				{
					case TaskStatus.Incomplete: return IncompleteText;
					case TaskStatus.Complete: return CompleteText;
					case TaskStatus.Cancelled: return CancelledText;
					case TaskStatus.Cancelling: return CancellingText;
					case TaskStatus.Paused: return PausedText;
					case TaskStatus.Running: return RunningText;
					case TaskStatus.Error: return ErrorText;
					case TaskStatus.Retrying: return RetryingText;
					case TaskStatus.Queued: return QueuedText;
					default: return status.ToString();
				}
			}

			public string ItemProgress
			{
				get
				{
					if (Task.Status != TaskStatus.Running)
						return string.Empty;
					if (Task.ActiveThreads > 0)
						return Task.ActiveThreads.ToString();
					if (Task.ShowItemProgressAsMarquee)
						return WorkingText;
					return string.Empty;
				}
			}
		}
	}
}
