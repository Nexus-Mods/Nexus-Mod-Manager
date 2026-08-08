using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;
using DevExpress.XtraBars;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands;
using Nexus.Client.ModManagement;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.DownloadMonitoring.UI
{
	/// <summary>
	/// The view that exposes Download monitoring functionality.
	/// </summary>
	public partial class DownloadMonitorControl : ManagedFontDockContent
	{
		private readonly BindingList<DownloadTaskRow> _rows = new BindingList<DownloadTaskRow>();
		private DownloadMonitorVM m_vmlViewModel;
		private const string TitleAllActive = "Download Manager ({0})";
		private const string TitleSomeActive = "Download Manager ({0}/{1})";
		private const string ColumnWidthsSettingsKey = "DownloadMonitor";

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

				new DevExpressBarItemCommandBinding<AddModTask>(tsbCancel, m_vmlViewModel.CancelTaskCommand, GetSelectedTask);
				new DevExpressBarItemCommandBinding<AddModTask>(tsbRemove, m_vmlViewModel.RemoveTaskCommand, GetSelectedTask);
				new DevExpressBarItemCommandBinding<AddModTask>(tsbPause, m_vmlViewModel.PauseTaskCommand, GetSelectedTask);
				new DevExpressBarItemCommandBinding<AddModTask>(tsbResume, m_vmlViewModel.ResumeTaskCommand, GetSelectedTask);

				Command removeAll = new Command("Remove all", "Purges the completed/failed downloads from the list.", ViewModel.RemoveAllTasks);
				new DevExpressBarItemCommandBinding(tsbRemoveAll, removeAll);
				Command resumeAll = new Command("Resume all", "Resumes all paused/queued downloads.", ViewModel.ResumeAllTasks);
				new DevExpressBarItemCommandBinding(tsbResumeAll, resumeAll);
				Command purgeDownloads = new Command("Purge Downloads", "Purges the paused/queued downloads from the list.", ViewModel.PurgeDownloads);
				new DevExpressBarItemCommandBinding(tsbPurgeDownloads, purgeDownloads);

				m_vmlViewModel.PurgingDownloads += ViewModel_PurgingDownloads;
				ViewModel.CancelTaskCommand.CanExecute = false;
				ViewModel.RemoveTaskCommand.CanExecute = false;
				ViewModel.PauseTaskCommand.CanExecute = false;
				ViewModel.ResumeTaskCommand.CanExecute = false;
				UpdateTitle();
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="DownloadMonitorControl"/> class.
		/// </summary>
		public DownloadMonitorControl()
		{
			InitializeComponent();
			DevExpressDisplaySettingsApplier.NormalizeBarItemImages(barManager, new System.Drawing.Size(32, 32));
			gridControl.DataSource = _rows;
			gridView.OptionsView.ColumnAutoWidth = false;
			UpdateTitle();
		}

		/// <summary>
		/// Restores persisted column widths and hooks persistence to the owning form.
		/// </summary>
		/// <param name="e">The load event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			if (DesignMode || ViewModel == null)
				return;

			DevExpressGridLayoutPersistence.RestoreColumnWidths(gridView, ViewModel.Settings.ColumnWidths[ColumnWidthsSettingsKey]);

			Form owner = Parent?.FindForm() ?? FindForm();
			if (owner != null)
				owner.FormClosing += DownloadMonitorControl_FormClosing;
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

			tsbCancel.Visibility = ViewModel.CancelTaskCommand.CanExecute ? BarItemVisibility.Always : BarItemVisibility.Never;
			tsbPause.Visibility = ViewModel.PauseTaskCommand.CanExecute ? BarItemVisibility.Always : BarItemVisibility.Never;
			tsbRemove.Visibility = ViewModel.RemoveTaskCommand.CanExecute ? BarItemVisibility.Always : BarItemVisibility.Never;
			tsbResume.Visibility = ViewModel.ResumeTaskCommand.CanExecute ? BarItemVisibility.Always : BarItemVisibility.Never;
			tsbResumeAll.Visibility = BarItemVisibility.Always;
			tsbRemoveAll.Visibility = BarItemVisibility.Always;
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

		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (gridControl.InvokeRequired)
			{
				gridControl.Invoke((MethodInvoker)(() => Tasks_CollectionChanged(sender, e)));
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
			if (!IsHandleCreated || ViewModel == null)
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
				gridControl.Invoke((Action)UpdateTitle);
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
				? string.Format(TitleAllActive, totalCount)
				: string.Format(TitleSomeActive, activeCount, totalCount);
		}

		private void ViewModel_PurgingDownloads(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_PurgingDownloads, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument, true);
		}

		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (gridControl.InvokeRequired)
			{
				gridControl.Invoke((Action)(() => Task_PropertyChanged(sender, e)));
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
			/// <summary>
			/// Initializes a new download row.
			/// </summary>
			public DownloadTaskRow(AddModTask task)
			{
				Task = task;
			}

			public AddModTask Task { get; private set; }
			public string OverallMessage { get { return Task.OverallMessage; } }
			public string OverallProgress
			{
				get
				{
					if (Task.ShowOverallProgressAsMarquee)
						return "Working...";
					if (Task.DownloadMaximum <= 0)
						return string.Empty;
					if (Task.Status != TaskStatus.Running && Task.Status != TaskStatus.Paused)
						return string.Empty;
					return Task.DownloadMaximum < 1024
						? string.Format("{0}KB / {1}KB", Task.DownloadProgress, Task.DownloadMaximum)
						: string.Format("{0}MB / {1}MB", Task.DownloadProgress / 1024, Task.DownloadMaximum / 1024);
				}
			}
			public string Status
			{
				get
				{
					if (Task.InnerTaskStatus.ToString() == "Retrying" && Task.Status != TaskStatus.Paused && Task.Status != TaskStatus.Queued)
						return "Retrying";
					if (Task.Status == TaskStatus.Running)
						return Task.IsRemote ? "Downloading" : "Moving";
					return Task.Status.ToString();
				}
			}
			public string ItemMessage
			{
				get
				{
					if (Task.Status != TaskStatus.Running)
						return string.Empty;
					if (Task.TaskSpeed > 0)
						return string.Format("{0} KB/s", Task.TaskSpeed);
					return Task.ShowItemProgress ? Task.ItemMessage : string.Empty;
				}
			}
			public string FileServer { get { return Task.Status == TaskStatus.Running ? Task.FileServer : string.Empty; } }
			public string ETA { get { return Task.Status == TaskStatus.Running ? string.Format("{0:00}:{1:00}:{2:00}", Task.ETA_Hours, Task.ETA_Minutes, Task.ETA_Seconds) : string.Empty; } }
			public string ItemProgress
			{
				get
				{
					if (Task.Status != TaskStatus.Running)
						return string.Empty;
					if (Task.ActiveThreads > 0)
						return Task.ActiveThreads.ToString();
					if (Task.ShowItemProgressAsMarquee)
						return "Working...";
					return string.Empty;
				}
			}
		}
	}
}
