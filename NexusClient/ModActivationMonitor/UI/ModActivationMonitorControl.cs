using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraBars;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Grid;
using DevExpress.XtraGrid.Views.Grid.ViewInfo;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands;
using Nexus.Client.ModManagement;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.ModActivationMonitoring.UI
{
	/// <summary>
	/// The view that exposes Mod Activation monitoring functionality.
	/// </summary>
	public partial class ModActivationMonitorControl : XtraUserControl
	{
		private readonly BindingList<ModActivationMonitorRow> _rows = new BindingList<ModActivationMonitorRow>();
		private ModActivationMonitorVM m_vmlViewModel;
		private readonly string m_strTitleAllActive = "Mod Activation Queue ({0})";
		private const string ColumnWidthsSettingsKey = "ModActivationMonitor";
		private bool _columnWidthsRestored;
		private bool _formClosingHooked;

		public List<IBackgroundTaskSet> QueuedTasks = new List<IBackgroundTaskSet>();

		#region Events

		public event EventHandler EmptyQueue;
		public event EventHandler SetTextBoxFocus;
		public event EventHandler UpdateBottomBarFeedback;

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModActivationMonitorVM ViewModel
		{
			get { return m_vmlViewModel; }
			set
			{
				m_vmlViewModel = value;
				_rows.Clear();
				QueuedTasks.Clear();
				if (m_vmlViewModel == null)
					return;

				foreach (IBackgroundTaskSet task in m_vmlViewModel.Tasks)
					AddTaskToList(task);

				m_vmlViewModel.Tasks.CollectionChanged += Tasks_CollectionChanged;

				Command cmdRemoveAll = new Command("Remove all", "Purges the completed activations from the list.", RemoveAllTasks);
				new DevExpressBarItemCommandBinding(tsbRemoveAll, cmdRemoveAll);
				Command cmdRemoveQueued = new Command("Remove queued", "Purges the queued activations from the list.", RemoveQueuedTasks);
				new DevExpressBarItemCommandBinding(tsbRemoveQueued, cmdRemoveQueued);
				Command cmdRemoveSelected = new Command("Remove selected", "Purges the selected activation from the list.", RemoveSelectedTask);
				new DevExpressBarItemCommandBinding(tsbCancel, cmdRemoveSelected);

				SetCommandExecutableStatus(false);
				UpdateTitle();
				InitializeColumnWidthPersistence();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModActivationMonitorControl()
		{
			InitializeComponent();
			DevExpressDisplaySettingsApplier.NormalizeBarItemImages(barManager, new System.Drawing.Size(32, 32));
			gridControl.DataSource = _rows;
			gridView.OptionsView.ColumnAutoWidth = true;
			UpdateTitle();
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="UserControl.Load"/> event of the control.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
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
			{
				DevExpressGridLayoutPersistence.RestoreColumnWidths(gridView, ViewModel.Settings.ColumnWidths[ColumnWidthsSettingsKey]);
				_columnWidthsRestored = true;
			}

			Form owner = FindForm();
			if (!_formClosingHooked && owner != null)
			{
				owner.FormClosing += ModActivationMonitorControl_FormClosing;
				_formClosingHooked = true;
			}
		}

		/// <summary>
		/// Saves the current DevExpress grid column widths when the main form closes.
		/// </summary>
		private void ModActivationMonitorControl_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (ViewModel == null)
				return;

			ViewModel.Settings.ColumnWidths[ColumnWidthsSettingsKey] = DevExpressGridLayoutPersistence.CaptureColumnWidths(gridView);
			ViewModel.Settings.Save();
		}

		/// <summary>
		/// During backup enables or disables the Activate Mods Monitoring icons.
		/// </summary>
		/// <param name="p_booCheck">The boolean value.</param>
		public void SetCommandBackupAMCStatus(bool p_booCheck)
		{
			Control.CheckForIllegalCrossThreadCalls = false;
			tsbCancel.Enabled = p_booCheck;
			tsbRemoveAll.Enabled = p_booCheck;
			tsbRemoveQueued.Enabled = p_booCheck;
		}

		private void RemoveAllTasks()
		{
			List<IBackgroundTaskSet> tasks = new List<IBackgroundTaskSet>();
			foreach (ModActivationMonitorRow row in _rows)
			{
				if (row.IsRemovable)
					tasks.Add(row.Task);
			}
			if (tasks.Count > 0)
				ViewModel.RemoveAllTasks(tasks);
			UpdateBottomBarFeedback(null, EventArgs.Empty);
		}

		private void RemoveQueuedTasks()
		{
			ViewModel.RemoveQueuedTasks();
			QueuedTasks.RemoveAll(x => x.IsQueued);
			UpdateBottomBarFeedback(null, EventArgs.Empty);
		}

		private void RemoveSelectedTask()
		{
			string taskName = GetSelectedTask();
			ViewModel.RemoveSelectedTask(taskName);
			if (QueuedTasks.Count > 0)
			{
				ViewModel.RunningTask = QueuedTasks.First();
				QueuedTasks.Remove(ViewModel.RunningTask);
			}
			UpdateBottomBarFeedback(null, EventArgs.Empty);
		}

		/// <summary>
		/// Returns the selected task name.
		/// </summary>
		private string GetSelectedTask()
		{
			ModActivationMonitorRow row = gridView.GetFocusedRow() as ModActivationMonitorRow;
			return row == null ? null : row.ModName;
		}

		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		protected void SetCommandExecutableStatus(bool removable)
		{
			tsbCancel.Enabled = removable && gridView.FocusedRowHandle >= 0;
		}

		/// <summary>
		/// Adds the given task to the view's list if it is not already present.
		/// </summary>
		protected void AddTaskToList(IBackgroundTaskSet task)
		{
			foreach (ModActivationMonitorRow existing in _rows)
				if (existing.Task == task)
					return;

			if (ShouldDiscardDuplicateTask(task))
			{
				DiscardDuplicateTask(task);
				return;
			}

			task.TaskSetCompleted += TaskSet_TaskSetCompleted;
			ModActivationMonitorRow row = new ModActivationMonitorRow(task, this);
			_rows.Add(row);
			gridView.RefreshData();
			CallUpdateBottomBarFeedback(row);
			EnsureVisible(row);

			if ((ViewModel.RunningTask == null) || ViewModel.RunningTask.IsCompleted)
			{
				ViewModel.RunningTask = task;
				StartTask(ViewModel.RunningTask);
			}
			else
			{
				QueuedTasks.Add(task);
			}
		}

		private static string GetTaskModFileName(IBackgroundTaskSet task)
		{
			if (task is ModInstaller installer)
				return installer.ModFileName;
			if (task is ModUninstaller uninstaller)
				return uninstaller.ModFileName;
			if (task is ModUpgrader upgrader)
				return upgrader.ModFileName;
			return null;
		}

		private bool ShouldDiscardDuplicateTask(IBackgroundTaskSet task)
		{
			if (ViewModel == null || ViewModel.RunningTask == null)
				return false;

			string taskFileName = GetTaskModFileName(task);
			if (String.IsNullOrEmpty(taskFileName))
				return false;

			if (QueuedTasks.Any(x => x.IsQueued && String.Equals(GetTaskModFileName(x), taskFileName, StringComparison.OrdinalIgnoreCase)))
				return true;

			string runningFileName = GetTaskModFileName(ViewModel.RunningTask);
			return !String.IsNullOrEmpty(runningFileName) && String.Equals(runningFileName, taskFileName, StringComparison.OrdinalIgnoreCase);
		}

		private void DiscardDuplicateTask(IBackgroundTaskSet task)
		{
			if (task is ModInstaller installer)
				m_vmlViewModel.RemoveUselessTask(installer);
			else if (task is ModUninstaller uninstaller)
				m_vmlViewModel.RemoveUselessTaskUn(uninstaller);
			else if (task is ModUpgrader upgrader)
				m_vmlViewModel.RemoveUselessTaskUpg(upgrader);
		}

		private static void StartTask(IBackgroundTaskSet task)
		{
			if (task is ModInstaller installer)
				installer.Install();
			else if (task is ModUninstaller uninstaller)
				uninstaller.Install();
			else if (task is ModUpgrader upgrader)
				upgrader.Install();
		}

		private void TaskSet_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			IBackgroundTaskSet completedTask = sender as IBackgroundTaskSet;
			if ((ViewModel.RunningTask == null) || (ViewModel.RunningTask == completedTask))
			{
				ViewModel.RunningTask = null;
				if (QueuedTasks.Count > 0)
				{
					ViewModel.RunningTask = QueuedTasks.First();
					QueuedTasks.Remove(ViewModel.RunningTask);
					StartTask(ViewModel.RunningTask);
				}
				else if (EmptyQueue != null)
				{
					EmptyQueue(this, EventArgs.Empty);
				}
			}
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
					foreach (IBackgroundTaskSet task in e.NewItems)
						AddTaskToList(task);
					break;
				case NotifyCollectionChangedAction.Move:
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (IBackgroundTaskSet task in e.OldItems)
					{
						for (int i = _rows.Count - 1; i >= 0; i--)
						{
							if (_rows[i].Task == task)
							{
								_rows[i].Detach();
								_rows.RemoveAt(i);
							}
						}
						task.TaskSetCompleted -= TaskSet_TaskSetCompleted;
					}
					break;
				case NotifyCollectionChangedAction.Reset:
					foreach (ModActivationMonitorRow row in _rows)
						row.Detach();
					_rows.Clear();
					break;
				default:
					throw new Exception("Unrecognized value for NotifyCollectionChangedAction.");
			}
			UpdateTitle();
		}

		/// <summary>
		/// Updates the control's title to reflect the current state of activities.
		/// </summary>
		protected void UpdateTitle()
		{
			int totalCount = ViewModel == null || ViewModel.Tasks == null ? 0 : ViewModel.Tasks.Count;
			Text = String.Format(m_strTitleAllActive, totalCount);
		}

		private void gridView_FocusedRowChanged(object sender, FocusedRowChangedEventArgs e)
		{
			ModActivationMonitorRow row = gridView.GetFocusedRow() as ModActivationMonitorRow;
			SetCommandExecutableStatus(row != null && row.IsRemovable);
		}

		private void gridView_RowCellClick(object sender, RowCellClickEventArgs e)
		{
			if (e.Button != MouseButtons.Left || e.Column == null || e.Column.FieldName != "ErrorInfo")
				return;

			ModActivationMonitorRow row = gridView.GetRow(e.RowHandle) as ModActivationMonitorRow;
			if (row == null || String.IsNullOrEmpty(row.ErrorMessage))
				return;

			if (String.Equals(row.PopupErrorMessageType, "Warning", StringComparison.OrdinalIgnoreCase))
				ExtendedMessageBox.Show(this, row.ErrorMessage, "Warning", row.DetailsErrorMessageType, MessageBoxButtons.OK, MessageBoxIcon.Warning);
			else
				ExtendedMessageBox.Show(this, row.ErrorMessage, "Failed", row.DetailsErrorMessageType, MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private void gridControl_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right)
				return;

			GridHitInfo hitInfo = gridView.CalcHitInfo(e.Location);
			if (hitInfo.InRow)
				gridView.FocusedRowHandle = hitInfo.RowHandle;
			popupMenu.ShowPopup(gridControl.PointToScreen(e.Location));
		}

		private void copyItem_ItemClick(object sender, ItemClickEventArgs e)
		{
			CopySelectedRowToClipboard();
		}

		private void gridControl_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.KeyData == (Keys.C | Keys.Control))
			{
				CopySelectedRowToClipboard();
			}
			if (e.KeyData == (Keys.Control | Keys.F) && SetTextBoxFocus != null)
			{
				SetTextBoxFocus(this, e);
			}
		}

		private void CopySelectedRowToClipboard()
		{
			ModActivationMonitorRow row = gridView.GetFocusedRow() as ModActivationMonitorRow;
			if (row == null)
				return;
			Clipboard.SetText(row.ModName + " // " + row.Status + " // " + row.Operation + " // " + row.Progress);
		}

		internal void EnsureVisible(ModActivationMonitorRow row)
		{
			if (row == null)
				return;
			int rowHandle = _rows.IndexOf(row);
			if (rowHandle >= 0)
				gridView.MakeRowVisible(rowHandle);
		}

		internal void RefreshRow(ModActivationMonitorRow row)
		{
			if (row == null)
				return;
			int rowHandle = _rows.IndexOf(row);
			if (rowHandle >= 0)
				gridView.RefreshRow(rowHandle);
			else
				gridView.RefreshData();
		}

		public void CallUpdateBottomBarFeedback(ModActivationMonitorRow row)
		{
			UpdateBottomBarFeedback(row, EventArgs.Empty);
		}

		/// <summary>
		/// Compatibility overload for the legacy list-view item.
		/// </summary>
		public void CallUpdateBottomBarFeedback(ModActivationMonitorListViewItem item)
		{
			UpdateBottomBarFeedback(item, EventArgs.Empty);
		}
	}

	/// <summary>
	/// Represents a row in the Mod Activation monitor grid.
	/// </summary>
	public sealed class ModActivationMonitorRow : INotifyPropertyChanged
	{
		private readonly ModActivationMonitorControl _control;
		private IBackgroundTask _startedTask;
		private bool _isRemovable;
		private string _modName;
		private string _status;
		private string _operation;
		private string _progress;
		private string _errorMessage;
		private string _popupErrorMessageType;
		private string _detailsErrorMessageType;

		/// <summary>
		/// Raised whenever a property changes.
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged;

		/// <summary>
		/// Initializes a new activation-monitor row.
		/// </summary>
		public ModActivationMonitorRow(IBackgroundTaskSet task, ModActivationMonitorControl control)
		{
			Task = task;
			_control = control;
			ModName = GetTaskModName(task);
			Status = "Queued";
			Operation = String.Empty;
			Progress = String.Empty;
			ErrorMessage = String.Empty;
			PopupErrorMessageType = String.Empty;
			DetailsErrorMessageType = String.Empty;
			IsRemovable = true;
			task.IsQueued = true;
			task.TaskStarted += TaskSet_TaskSetStarted;
			task.TaskSetCompleted += TaskSet_TaskSetCompleted;
		}

		/// <summary>Gets the task associated with this row.</summary>
		public IBackgroundTaskSet Task { get; private set; }
		/// <summary>Gets the mod name.</summary>
		public string ModName { get { return _modName; } private set { SetField(ref _modName, value, nameof(ModName)); } }
		/// <summary>Gets the status text.</summary>
		public string Status { get { return _status; } private set { SetField(ref _status, value, nameof(Status)); } }
		/// <summary>Gets the operation text.</summary>
		public string Operation { get { return _operation; } private set { SetField(ref _operation, value, nameof(Operation)); } }
		/// <summary>Gets the progress text.</summary>
		public string Progress { get { return _progress; } private set { SetField(ref _progress, value, nameof(Progress)); } }
		/// <summary>Gets the popup error message.</summary>
		public string ErrorMessage { get { return _errorMessage; } private set { if (!String.Equals(_errorMessage, value, StringComparison.Ordinal)) { _errorMessage = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorMessage))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ErrorInfo))); } } }
		/// <summary>Gets the popup error message type.</summary>
		public string PopupErrorMessageType { get { return _popupErrorMessageType; } private set { SetField(ref _popupErrorMessageType, value, nameof(PopupErrorMessageType)); } }
		/// <summary>Gets the details error message type.</summary>
		public string DetailsErrorMessageType { get { return _detailsErrorMessageType; } private set { SetField(ref _detailsErrorMessageType, value, nameof(DetailsErrorMessageType)); } }
		/// <summary>Gets a visible error marker.</summary>
		public string ErrorInfo { get { return String.IsNullOrEmpty(ErrorMessage) ? String.Empty : "!"; } }
		/// <summary>Gets whether the row can be removed.</summary>
		public bool IsRemovable { get { return _isRemovable; } private set { SetField(ref _isRemovable, value, nameof(IsRemovable)); } }

		/// <summary>
		/// Detaches event subscriptions.
		/// </summary>
		public void Detach()
		{
			Task.TaskStarted -= TaskSet_TaskSetStarted;
			Task.TaskSetCompleted -= TaskSet_TaskSetCompleted;
			if (_startedTask != null)
				_startedTask.PropertyChanged -= Task_PropertyChanged;
		}

		private static string GetTaskModName(IBackgroundTaskSet task)
		{
			if (task is ModInstaller installer)
				return installer.ModName;
			if (task is ModUninstaller uninstaller)
				return uninstaller.ModName;
			if (task is ModUpgrader upgrader)
				return upgrader.ModName;
			return String.Empty;
		}

		private void TaskSet_TaskSetStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			Control invokeTarget = _control;
			if ((invokeTarget != null) && invokeTarget.InvokeRequired)
			{
				invokeTarget.Invoke((Action<IBackgroundTaskSet, EventArgs<IBackgroundTask>>)TaskSet_TaskSetStarted, sender, e);
				return;
			}

			_startedTask = e.Argument;
			_startedTask.PropertyChanged += Task_PropertyChanged;

			IsRemovable = false;
			Status = "Running";
			if (sender is ModInstaller)
				Operation = "Install";
			else if (sender is ModUninstaller)
				Operation = "Uninstall";
			else if (sender is ModUpgrader)
				Operation = "Upgrading";

			Task.IsQueued = false;
			_control.CallUpdateBottomBarFeedback(this);
			_control.EnsureVisible(this);
		}

		private void TaskSet_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			Control invokeTarget = _control;
			if ((invokeTarget != null) && invokeTarget.InvokeRequired)
			{
				invokeTarget.Invoke((Action<IBackgroundTaskSet, TaskSetCompletedEventArgs>)TaskSet_TaskSetCompleted, sender, e);
				return;
			}

			bool complete = false;
			string popupErrorMessage = String.Empty;
			if (sender is ModInstallerBase installerBase)
			{
				complete = installerBase.IsCompleted;
				if (!String.IsNullOrEmpty(installerBase.PopupErrorMessage))
					popupErrorMessage = installerBase.PopupErrorMessage;
				PopupErrorMessageType = installerBase.PopupErrorMessageType;
				DetailsErrorMessageType = installerBase.DetailsErrorMessage;
			}

			if (complete)
			{
				if (!String.IsNullOrEmpty(popupErrorMessage))
					ErrorMessage = popupErrorMessage;

				if (!e.Success)
				{
					Status = e.Message;
					Progress = String.Empty;
				}
				else
				{
					Status = "Complete";
					Progress = "100%";
				}
			}
			else
			{
				Status = e.Message;
				Progress = String.Empty;
			}

			_control.CallUpdateBottomBarFeedback(this);
			_control.RefreshRow(this);
			IsRemovable = true;
		}

		private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			try
			{
				Control invokeTarget = _control;
				if ((invokeTarget != null) && invokeTarget.InvokeRequired)
				{
					invokeTarget.Invoke((Action<IBackgroundTask, string>)HandleChangedTaskProperty, (IBackgroundTask)sender, e.PropertyName);
					return;
				}
				HandleChangedTaskProperty((IBackgroundTask)sender, e.PropertyName);
			}
			catch { }
		}

		private void HandleChangedTaskProperty(IBackgroundTask task, string propertyName)
		{
			try
			{
				if (task is BasicUninstallTask)
				{
					if ((propertyName == nameof(IBackgroundTask.ItemProgress)) && (task.ItemProgress > 0))
						Progress = "Uninstalling, please wait...(" + ((task.ItemProgress * 100) / task.ItemProgressMaximum) + "%)";
				}
				else if (task is PrepareModTask)
				{
					if (propertyName == nameof(IBackgroundTask.OverallProgress))
						Progress = "Unpacking, please wait...(" + (((task.OverallProgress * 100) / task.OverallProgressMaximum) / 2) + "%)";
				}
				else
				{
					if (propertyName == nameof(IBackgroundTask.OverallProgress))
						Progress = "Installing, please wait...(" + ((((task.OverallProgress * 100) / task.OverallProgressMaximum) / 2) + 50) + "%)";
				}
				_control.RefreshRow(this);
			}
			catch (NullReferenceException)
			{
			}
			catch (ArgumentOutOfRangeException)
			{
			}
		}

		private void SetField<T>(ref T field, T value, string propertyName)
		{
			if (EqualityComparer<T>.Default.Equals(field, value))
				return;
			field = value;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
