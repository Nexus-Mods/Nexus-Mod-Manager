using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.ActivityMonitoring.UI
{
	/// <summary>
	/// The view that exposes activity monitoring functionality.
	/// </summary>
	public partial class ActivityMonitorControl : ManagedFontDockContent
	{
		private readonly BindingList<ActivityTaskRow> _rows = new BindingList<ActivityTaskRow>();
		private ActivityMonitorVM m_vmlViewModel;
		private const string TitleAllActive = "Download Manager ({0})";
		private const string TitleSomeActive = "Download Manager ({0}/{1})";

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ActivityMonitorVM ViewModel
		{
			get { return m_vmlViewModel; }
			set
			{
				m_vmlViewModel = value;
				_rows.Clear();
				if (m_vmlViewModel == null)
					return;

				foreach (IBackgroundTask task in m_vmlViewModel.Tasks)
					AddTaskToList(task);

				m_vmlViewModel.ActiveTasks.CollectionChanged += ActiveTasks_CollectionChanged;
				m_vmlViewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;

				new DevExpressBarItemCommandBinding<IBackgroundTask>(tsbCancel, m_vmlViewModel.CancelTaskCommand, GetSelectedTask);
				new DevExpressBarItemCommandBinding<IBackgroundTask>(tsbRemove, m_vmlViewModel.RemoveTaskCommand, GetSelectedTask);
				new DevExpressBarItemCommandBinding<IBackgroundTask>(tsbPause, m_vmlViewModel.PauseTaskCommand, GetSelectedTask);
				new DevExpressBarItemCommandBinding<IBackgroundTask>(tsbResume, m_vmlViewModel.ResumeTaskCommand, GetSelectedTask);

				ViewModel.CancelTaskCommand.CanExecute = false;
				ViewModel.RemoveTaskCommand.CanExecute = false;
				ViewModel.PauseTaskCommand.CanExecute = false;
				ViewModel.ResumeTaskCommand.CanExecute = false;
				UpdateTitle();
			}
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ActivityMonitorControl"/> class.
		/// </summary>
		public ActivityMonitorControl()
		{
			InitializeComponent();
			gridControl.DataSource = _rows;
			UpdateTitle();
		}

		/// <summary>
		/// Gets the currently selected background task.
		/// </summary>
		private IBackgroundTask GetSelectedTask()
		{
			ActivityTaskRow row = gridView.GetFocusedRow() as ActivityTaskRow;
			return row == null ? null : row.Task;
		}

		/// <summary>
		/// Updates command availability for the selected task.
		/// </summary>
		protected void SetCommandExecutableStatus()
		{
			if (ViewModel == null)
				return;

			IBackgroundTask task = GetSelectedTask();
			ViewModel.CancelTaskCommand.CanExecute = task != null && ViewModel.CanCancelTask(task);
			ViewModel.RemoveTaskCommand.CanExecute = task != null && ViewModel.CanRemoveActivity(task);
			ViewModel.PauseTaskCommand.CanExecute = task != null && ViewModel.CanPauseActivity(task);
			ViewModel.ResumeTaskCommand.CanExecute = task != null && ViewModel.CanResumeActivity(task);
		}

		/// <summary>
		/// Adds a task to the DevExpress activity grid if it is not already present.
		/// </summary>
		protected void AddTaskToList(IBackgroundTask task)
		{
			foreach (ActivityTaskRow row in _rows)
				if (row.Task == task)
					return;

			task.PropertyChanged -= Task_PropertyChanged;
			task.PropertyChanged += Task_PropertyChanged;
			_rows.Add(new ActivityTaskRow(task));
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
					foreach (IBackgroundTask task in e.NewItems)
						AddTaskToList(task);
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (IBackgroundTask task in e.OldItems)
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
			if (gridControl.InvokeRequired)
				gridControl.Invoke((Action)UpdateTitle);
			else
				UpdateTitle();
		}

		/// <summary>
		/// Updates the dock title to reflect active and total tasks.
		/// </summary>
		protected void UpdateTitle()
		{
			int activeCount = ViewModel == null ? 0 : ViewModel.ActiveTasks.Count;
			int totalCount = ViewModel == null ? 0 : ViewModel.Tasks.Count;
			Text = totalCount == activeCount
				? string.Format(TitleAllActive, totalCount)
				: string.Format(TitleSomeActive, activeCount, totalCount);
		}

		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (gridControl.InvokeRequired)
			{
				gridControl.Invoke((Action)(() => Task_PropertyChanged(sender, e)));
				return;
			}

			gridView.RefreshData();
			if (e.PropertyName == ObjectHelper.GetPropertyName<IBackgroundTask>(x => x.Status))
				SetCommandExecutableStatus();
		}

		private void gridView_FocusedRowChanged(object sender, DevExpress.XtraGrid.Views.Base.FocusedRowChangedEventArgs e)
		{
			SetCommandExecutableStatus();
		}

		/// <summary>
		/// Presents one background task as a row in the DevExpress grid.
		/// </summary>
		private sealed class ActivityTaskRow
		{
			/// <summary>
			/// Initializes a new activity row.
			/// </summary>
			public ActivityTaskRow(IBackgroundTask task)
			{
				Task = task;
			}

			public IBackgroundTask Task { get; private set; }
			public string OverallMessage { get { return Task.OverallMessage; } }
			public string OverallProgress { get { return FormatProgress(Task.ShowOverallProgressAsMarquee, true, Task.OverallProgress, Task.OverallProgressMinimum, Task.OverallProgressMaximum); } }
			public string ItemMessage { get { return Task.ShowItemProgress ? Task.ItemMessage : string.Empty; } }
			public string ItemProgress { get { return FormatProgress(Task.ShowItemProgressAsMarquee, Task.ShowItemProgress, Task.ItemProgress, Task.ItemProgressMinimum, Task.ItemProgressMaximum); } }
			public string Status { get { return Task.Status.ToString(); } }

			/// <summary>
			/// Formats a background-task progress range for display.
			/// </summary>
			private static string FormatProgress(bool marquee, bool visible, long value, long minimum, long maximum)
			{
				if (!visible)
					return string.Empty;
				if (marquee)
					return "Working...";
				long denominator = maximum - minimum;
				float percentage = denominator == 0 ? 0 : ((float)(value - minimum)) / denominator;
				return percentage.ToString("P0");
			}
		}
	}
}
