﻿using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands.Generic;
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
		private DownloadMonitorVM m_vmlViewModel = null;
		private float m_fltColumnRatio = 0.5f;
		private bool m_booResizing = false;
		private Timer m_tmrColumnSizer = new Timer();
		private string m_strTitleAllActive = "Download Manager ({0})";
		private string m_strTitleSomeActive = "Download Manager ({0}/{1})";
		private bool m_booControlIsLoaded = false;
		
		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public DownloadMonitorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				foreach (AddModTask tskDownload in m_vmlViewModel.Tasks)
					AddTaskToList(tskDownload);
				m_vmlViewModel.ActiveTasks.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveTasks_CollectionChanged);
				m_vmlViewModel.Tasks.CollectionChanged += new NotifyCollectionChangedEventHandler(Tasks_CollectionChanged);

				new ToolStripItemCommandBinding<AddModTask>(tsbCancel, m_vmlViewModel.CancelTaskCommand, GetSelectedTask);
				new ToolStripItemCommandBinding<AddModTask>(tsbRemove, m_vmlViewModel.RemoveTaskCommand, GetSelectedTask);
				new ToolStripItemCommandBinding<AddModTask>(tsbPause, m_vmlViewModel.PauseTaskCommand, GetSelectedTask);
				new ToolStripItemCommandBinding<AddModTask>(tsbResume, m_vmlViewModel.ResumeTaskCommand, GetSelectedTask);
				ViewModel.CancelTaskCommand.CanExecute = false;
				ViewModel.RemoveTaskCommand.CanExecute = false;
				ViewModel.PauseTaskCommand.CanExecute = false;
				ViewModel.ResumeTaskCommand.CanExecute = false;

				LoadMetrics();
				UpdateTitle();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public DownloadMonitorControl()
		{
			InitializeComponent();
			clmOverallMessage.Name = ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallMessage);
			clmOverallProgress.Name = ObjectHelper.GetPropertyName<AddModTask>(x => x.OverallProgress);
			clmItemMessage.Name = ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemMessage);
			clmItemProgress.Name = ObjectHelper.GetPropertyName<AddModTask>(x => x.ItemProgress);
			clmStatus.Name = ObjectHelper.GetPropertyName<AddModTask>(x => x.Status);

			m_tmrColumnSizer.Interval = 100;
			m_tmrColumnSizer.Tick += new EventHandler(ColumnSizer_Tick);

			UpdateTitle();
		}

		#endregion

		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="UserControl.Load"/> event of the control.
		/// </summary>
		/// <remarks>
		/// This loads any saved control metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);

			if (!DesignMode)
			{
				m_booControlIsLoaded = true;
				LoadMetrics();
			}
		}

		/// <summary>
		/// Loads the control's saved metrics.
		/// </summary>
		protected void LoadMetrics()
		{
			if (m_booControlIsLoaded && (ViewModel != null))
			{
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("DownloadMonitor", lvwTasks);

				FindForm().FormClosing += new FormClosingEventHandler(DownloadMonitorControl_FormClosing);
				m_fltColumnRatio = (float)clmOverallMessage.Width / (clmOverallMessage.Width + clmItemMessage.Width);
				SizeColumnsToFit();
			}
		}

		/// <summary>
		/// Handles the <see cref="Form.Closing"/> event of the parent form.
		/// </summary>
		/// <remarks>
		/// This saves the control's metrics.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void DownloadMonitorControl_FormClosing(object sender, FormClosingEventArgs e)
		{
			ViewModel.Settings.ColumnWidths.SaveColumnWidths("DownloadMonitor", lvwTasks);
			ViewModel.Settings.Save();
		}

		#endregion

		#region Binding

		/// <summary>
		/// Retruns the <see cref="IBackgroundTask"/> that is currently selected in the view.
		/// </summary>
		/// <returns>The <see cref="IBackgroundTask"/> that is currently selected in the view, or
		/// <c>null</c> if no <see cref="IBackgroundTask"/> is selected.</returns>
		private AddModTask GetSelectedTask()
		{
			if (lvwTasks.SelectedItems.Count == 0)
				return null;
			return ((DownloadListViewItem)lvwTasks.SelectedItems[0]).Task;
		}

		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		protected void SetCommandExecutableStatus()
		{
			ViewModel.CancelTaskCommand.CanExecute = (lvwTasks.SelectedItems.Count > 0) && ViewModel.CanCancelTask(GetSelectedTask()) && !ViewModel.OfflineMode;
			ViewModel.RemoveTaskCommand.CanExecute = (lvwTasks.SelectedItems.Count > 0) && ViewModel.CanRemoveDownload(GetSelectedTask());
			ViewModel.PauseTaskCommand.CanExecute = (lvwTasks.SelectedItems.Count > 0) && ViewModel.CanPauseDownload(GetSelectedTask()) && !ViewModel.OfflineMode;
			ViewModel.ResumeTaskCommand.CanExecute = (lvwTasks.SelectedItems.Count > 0) && ViewModel.CanResumeDownload(GetSelectedTask()) && !ViewModel.OfflineMode;

			this.tsbCancel.Visible = ViewModel.CancelTaskCommand.CanExecute;
			this.tsbPause.Visible = ViewModel.PauseTaskCommand.CanExecute;
			this.tsbRemove.Visible = ViewModel.RemoveTaskCommand.CanExecute;
			this.tsbResume.Visible = ViewModel.ResumeTaskCommand.CanExecute;
		}

		#endregion

		#region Task Addition/Removal

		/// <summary>
		/// Adds the given <see cref="IBackgroundTask"/> to the view's list. If the <see cref="IBackgroundTask"/>
		/// already exists in the list, nothing is done.
		/// </summary>
		/// <param name="p_tskTask">The <see cref="IBackgroundTask"/> to add to the view's list.</param>
		protected void AddTaskToList(AddModTask p_tskTask)
		{
			foreach (DownloadListViewItem lviExisitingDownload in lvwTasks.Items)
				if (lviExisitingDownload.Task == p_tskTask)
					return;
			p_tskTask.PropertyChanged -= new PropertyChangedEventHandler(Task_PropertyChanged);
			p_tskTask.PropertyChanged += new PropertyChangedEventHandler(Task_PropertyChanged);
			DownloadListViewItem lviDownload = new DownloadListViewItem(p_tskTask);
			lvwTasks.Items.Add(lviDownload);
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// task list.
		/// </summary>
		/// <remarks>
		/// This updates the list of tasks to refelct changes to the monitored Download list.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (lvwTasks.InvokeRequired)
			{
				lvwTasks.Invoke((MethodInvoker)(() => Tasks_CollectionChanged(sender, e)));
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (AddModTask tskAdded in e.NewItems)
						AddTaskToList(tskAdded);
					break;
				case NotifyCollectionChangedAction.Move:
					//TODO Download order matters (some tasks depend on others)
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (AddModTask tskRemoved in e.OldItems)
					{
						for (Int32 i = lvwTasks.Items.Count - 1; i >= 0; i--)
							if (((DownloadListViewItem)lvwTasks.Items[i]).Task == tskRemoved)
								lvwTasks.Items.RemoveAt(i);
						tskRemoved.PropertyChanged -= new PropertyChangedEventHandler(Task_PropertyChanged);
					}
					break;
				case NotifyCollectionChangedAction.Reset:
					lvwTasks.Items.Clear();
					break;
				default:
					throw new Exception("Unrecognized value for NotifyCollectionChangedAction.");
			}
			UpdateTitle();
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// active task list.
		/// </summary>
		/// <remarks>
		/// This updates the control title.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (lvwTasks.InvokeRequired)
			{
				if (m_vmlViewModel.ActiveTasks.Count < m_vmlViewModel.MaxConcurrentDownloads)
				{
					AddModTask amtQueued = m_vmlViewModel.QueuedTask;
					if (amtQueued != null)
						m_vmlViewModel.ResumeTask(amtQueued);
				}
				lvwTasks.Invoke((Action)UpdateTitle);
			}
			else
			{
				if (m_vmlViewModel.ActiveTasks.Count < m_vmlViewModel.MaxConcurrentDownloads)
				{
					AddModTask amtQueued = m_vmlViewModel.QueuedTask;
					if (amtQueued != null)
						m_vmlViewModel.ResumeTask(amtQueued);
				}
				UpdateTitle();
			}
		}

		/// <summary>
		/// Updates the control's title to reflect the current state of activities.
		/// </summary>
		protected void UpdateTitle()
		{
			Int32 intActiveCount =0;
			Int32 intTotalCount =0;
			if (ViewModel != null)
			{
				intActiveCount = ViewModel.ActiveTasks.Count;
				intTotalCount = ViewModel.Tasks.Count;
			}
			if (intTotalCount == intActiveCount)
				Text = String.Format(m_strTitleAllActive, intTotalCount);
			else
				Text = String.Format(m_strTitleSomeActive, intActiveCount, intTotalCount);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> of the tasks being monitored.
		/// </summary>
		/// <remarks>
		/// This adjusts the command availability based on the task's status.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == ObjectHelper.GetPropertyName<AddModTask>(x => x.Status))
			{
				AddModTask tskTask = (AddModTask)sender;
				if (tskTask.Status == TaskStatus.Queued)
					if (m_vmlViewModel.ActiveTasks.Count < m_vmlViewModel.MaxConcurrentDownloads)
						m_vmlViewModel.ResumeTask(tskTask);

				if (InvokeRequired)
					Invoke((Action)SetCommandExecutableStatus);
				else
					SetCommandExecutableStatus();
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> of the active tasks being monitored.
		/// </summary>
		/// <remarks>
		/// This adjusts the command availability based on the active task's status.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == ObjectHelper.GetPropertyName<AddModTask>(x => x.Status))
			{

			}
		}

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the Download list.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwTasks_SelectedIndexChanged(object sender, EventArgs e)
		{
			SetCommandExecutableStatus();
		}

		#region Column Resizing

		/// <summary>
		/// Handles the <see cref="Timer.Tick"/> event of the column sizer timer.
		/// </summary>
		/// <remarks>
		/// We use a timer to autosize the columns in the list view. This is because
		/// there is a bug in the control such that if we reszize the columns continuously
		/// while the list view is being resized, the item will sometimes disappear.
		/// 
		/// To work around this, the list view resize event continually resets the timer.
		/// This means the timer will only fire occasionally during the resize, and avoid
		/// the disappearing items issue.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ColumnSizer_Tick(object sender, EventArgs e)
		{
			((Timer)sender).Stop();
			SizeColumnsToFit();
		}

		/// <summary>
		/// This resizes the columns to fill the list view.
		/// </summary>
		protected void SizeColumnsToFit()
		{
			if (lvwTasks.Columns.Count == 0)
				return;
			m_booResizing = true;
			Int32 intFixedWidth = 0;
			for (Int32 i = 0; i < lvwTasks.Columns.Count; i++)
				if (lvwTasks.Columns[i] != clmOverallMessage)
					intFixedWidth += lvwTasks.Columns[i].Width;
			clmOverallMessage.Width = lvwTasks.ClientSize.Width - intFixedWidth;
			m_booResizing = false;
		}

		/// <summary>
		/// Handles the <see cref="Control.Resize"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This resizes the columns to fill the list view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwTasks_Resize(object sender, EventArgs e)
		{
			if (m_booResizing)
				return;
			m_tmrColumnSizer.Stop();
			m_tmrColumnSizer.Start();
		}

		/// <summary>
		/// Handles the <see cref="ListView.ColumnWidthChanging"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This resizes the column next to the column being resized to resize as well,
		/// so that the columns keep the list view filled.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ColumnWidthChangingEventArgs"/> describing the event arguments.</param>
		private void lvwTasks_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			if (m_booResizing)
				return;
			ColumnHeader clmThis = lvwTasks.Columns[e.ColumnIndex];
			ColumnHeader clmOther = null;
			if (e.ColumnIndex == lvwTasks.Columns.Count - 1)
				clmOther = lvwTasks.Columns[e.ColumnIndex - 1];
			else
				clmOther = lvwTasks.Columns[e.ColumnIndex + 1];
			m_booResizing = true;
			clmOther.Width += (clmThis.Width - e.NewWidth);
			m_booResizing = false;
		}

		#endregion
	}
}
