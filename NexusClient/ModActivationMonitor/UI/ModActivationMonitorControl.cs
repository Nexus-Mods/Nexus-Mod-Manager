using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.Client.ModManagement;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.ModActivationMonitoring.UI
{
	/// <summary>
	/// The view that exposes Mod Activation monitoring functionality.
	/// </summary>
	public partial class ModActivationMonitorControl : ManagedFontDockContent
	{
		private ModActivationMonitorVM m_vmlViewModel = null;
		private float m_fltColumnRatio = 0.5f;
		private bool m_booResizing = false;
		private Timer m_tmrColumnSizer = new Timer();
		private string m_strTitleAllActive = "Mod Activation Queue ({0})";
		private string m_strTitleSomeActive = "Mod Activation Queue ({0}/{1})";
		private bool m_booControlIsLoaded = false;
		
		public List<IBackgroundTaskSet> QueuedTasks = new List<IBackgroundTaskSet>();
		private bool booQueued = false;
		private string m_strPopupErrorMessage = string.Empty;
		private string m_strPopupErrorMessageType = string.Empty;
		private string m_strDetailsErrorMessageType = string.Empty;
		private Int32 m_intFocusBoundsX = 0;

		/// <summary>
		/// Gets the messages and images associated with the sub items.
		/// </summary>
		/// <value>The messages and images associated with the sub items.</value>
		protected Dictionary<ListViewItem.ListViewSubItem, KeyValuePair<string, Image>> Messages { get; private set; }

		#region Events

		public event EventHandler EmptyQueue;
		public event EventHandler SetTextBoxFocus;
		public event EventHandler UpdateBottomBarFeedback;

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModActivationMonitorVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				if (m_vmlViewModel != null)
				{
					if (m_vmlViewModel.Tasks != null)
						foreach (IBackgroundTaskSet tskBasicInstall in m_vmlViewModel.Tasks)
							AddTaskToList(tskBasicInstall);
															
					m_vmlViewModel.Tasks.CollectionChanged += new NotifyCollectionChangedEventHandler(Tasks_CollectionChanged);
													
					Command cmdRemoveAll = new Command("Remove all", "Purges the completed activations from the list.", RemoveAllTasks);
					new ToolStripItemCommandBinding(tsbRemoveAll, cmdRemoveAll);
					Command cmdRemoveQueued = new Command("Remove queued", "Purges the queued activations from the list.", RemoveQueuedTasks);
					new ToolStripItemCommandBinding(tsbRemoveQueued, cmdRemoveQueued);
					Command cmdRemoveSelected = new Command("Remove selected", "Purges the selected activation from the list.", RemoveSelectedTask);
					new ToolStripItemCommandBinding(tsbCancel, cmdRemoveSelected);

					SetCommandExecutableStatus(false);
				}

				LoadMetrics();
				UpdateTitle();
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

			clmOverallMessage.Name = "ModName";
			clmOverallProgress.Name = "Status";
			//clmIcon.Name = "Progress";
						
			m_tmrColumnSizer.Interval = 100;
			m_tmrColumnSizer.Tick += new EventHandler(ColumnSizer_Tick);

			Messages = new Dictionary<ListViewItem.ListViewSubItem, KeyValuePair<string, Image>>();
			
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
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("ModActivationMonitor", lvwActiveTasks);

				FindForm().FormClosing += new FormClosingEventHandler(ModActivationMonitorControl_FormClosing);
				m_fltColumnRatio = (float)clmOverallMessage.Width / (clmOverallMessage.Width);

				this.lvwActiveTasks.DrawSubItem += new System.Windows.Forms.DrawListViewSubItemEventHandler(ModActivationMonitorControl_DrawSubItem);
				this.lvwActiveTasks.DrawColumnHeader += new System.Windows.Forms.DrawListViewColumnHeaderEventHandler(ModActivationMonitorControl_DrawColumnHeader);

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
		private void ModActivationMonitorControl_FormClosing(object sender, FormClosingEventArgs e)
		{
			ViewModel.Settings.ColumnWidths.SaveColumnWidths("ModActivationMonitor", lvwActiveTasks);
			ViewModel.Settings.Save();
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="Control.MouseClick"/> event of the controls.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="MouseEventArgs"/> describing the event arguments.</param>
		private void ModActivationMonitorControl_MouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Right)
			{
				ContextMenu m = new ContextMenu();
				m.MenuItems.Clear();
				m.MenuItems.Add(new MenuItem("Copy to clipboard", new EventHandler(cmsContextMenu_Copy)));
				m.Show((Control)(sender), e.Location);
			}
			else if (e.Button == MouseButtons.Left)
			{
				ListViewItem lvItem = lvwActiveTasks.GetItemAt(e.X, e.Y);
								
				if (lvItem == null)
					return;
				ListViewItem.ListViewSubItem subItem = lvItem.GetSubItemAt(e.X, e.Y);
				if (subItem == null)
					return;
				if (subItem.Name == "?")
				{
					if (subItem.Text != string.Empty)
					{
						if((m_strPopupErrorMessageType == "Error") || (String.IsNullOrEmpty(m_strPopupErrorMessageType)))
							ExtendedMessageBox.Show(this, subItem.Text, "Failed", m_strDetailsErrorMessageType, MessageBoxButtons.OK, MessageBoxIcon.Error);
						else if(m_strPopupErrorMessageType == "Warning")
							ExtendedMessageBox.Show(this, subItem.Text, "Warning", m_strDetailsErrorMessageType, MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}
			}
		}

		/// <summary>
		/// During the backup ebables or disables the Activate Mods Monitoring icons
		/// </summary>
		/// <param name="p_booCheck">The boolean value.</param>
		public void SetCommandBackupAMCStatus(bool p_booCheck)
		{
			Control.CheckForIllegalCrossThreadCalls = false;
			
			tsbCancel.Enabled = p_booCheck;
			tsbRemoveAll.Enabled = p_booCheck;
			tsbRemoveQueued.Enabled = p_booCheck;
		}

		/// <summary>
		/// Hanldes the <see cref="Control.KeyUp"/> event of the controls.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="MouseEventArgs"/> describing the event arguments.</param>
		private void ModActivationMonitorControl_KeyUp(object sender, KeyEventArgs e)
		{
			if (e.KeyData == (Keys.C | Keys.Control))
			{
				Clipboard.SetText(lvwActiveTasks.FocusedItem.SubItems["ModName"].Text + " // " + lvwActiveTasks.FocusedItem.SubItems["Status"].Text + " // " + lvwActiveTasks.FocusedItem.SubItems["Operation"].Text + " // " + lvwActiveTasks.FocusedItem.SubItems["Progress"].Text);
			}
			if (e.KeyData == (Keys.Control | Keys.F))
			{
				SetTextBoxFocus(this, e);
			}
		}

		#region Binding

		private void RemoveAllTasks()
		{
			List<IBackgroundTaskSet> lstTask = new List<IBackgroundTaskSet>();
			foreach (ModActivationMonitorListViewItem Item in lvwActiveTasks.Items)
			{
				if (Item.IsRemovable)
					lstTask.Add(Item.Task);
			}
			
			if (lstTask.Count > 0)
				ViewModel.RemoveAllTasks(lstTask);

			UpdateBottomBarFeedback(null, new EventArgs());
		}

		private void RemoveQueuedTasks()
		{
			ViewModel.RemoveQueuedTasks();
			QueuedTasks.RemoveAll(x => x.IsQueued);
			UpdateBottomBarFeedback(null, new EventArgs());
		}
		
		private void RemoveSelectedTask()
		{
			string strTaskName = GetSelectedTask();
			ViewModel.RemoveSelectedTask(strTaskName);
			if (QueuedTasks.Count > 0)
			{
				ViewModel.RunningTask = QueuedTasks.First();
				QueuedTasks.Remove(ViewModel.RunningTask);
			}
			UpdateBottomBarFeedback(null, new EventArgs());
		}

		/// <summary>
		/// Retruns the <see cref="BasicInstallTask"/> that is currently selected in the view.
		/// </summary>
		/// <returns>The <see cref="BasicInstallTask"/> that is currently selected in the view, or
		/// <c>null</c> if no <see cref="BasicInstallTask"/> is selected.</returns>
		private string GetSelectedTask()
		{
			if (lvwActiveTasks.SelectedItems.Count > 0)
				return lvwActiveTasks.SelectedItems[0].Text;
			else
				return null;
		}
				
		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		protected void SetCommandExecutableStatus(bool p_booCheckStatus)
		{
			tsbCancel.Enabled = (lvwActiveTasks.SelectedItems.Count > 0) && p_booCheckStatus;
		}
			
		#endregion

		#region Task Addition/Removal

		/// <summary>
		/// Adds the given <see cref="BasicInstallTask"/> to the view's list. If the <see cref="BasicInstallTask"/>
		/// already exists in the list, nothing is done.
		/// </summary>
		/// <param name="p_tskTask">The <see cref="BasicInstallTask"/> to add to the view's list.</param>
		protected void AddTaskToList(IBackgroundTaskSet p_tskTask)
		{
			foreach (ModActivationMonitorListViewItem lviExisitingTask in lvwActiveTasks.Items)
				if (lviExisitingTask.Task == p_tskTask)
					return;

			if (ViewModel.RunningTask != null)
			{
				if (p_tskTask.GetType() == typeof(ModInstaller))
				{
					foreach (IBackgroundTaskSet iBk in QueuedTasks)
					{
						if (iBk.GetType() == typeof(ModInstaller))
						{
							if (((ModInstaller)iBk).ModFileName == ((ModInstaller)p_tskTask).ModFileName)
								if (((ModInstaller)iBk).IsQueued)
									booQueued = true;
						}
						else if (iBk.GetType() == typeof(ModUninstaller))
						{
							if (((ModUninstaller)iBk).ModFileName == ((ModInstaller)p_tskTask).ModFileName)
								if (((ModUninstaller)iBk).IsQueued)
									booQueued = true;
						}
						else if (iBk.GetType() == typeof(ModUpgrader))
						{
							if (((ModUpgrader)iBk).ModFileName == ((ModInstaller)p_tskTask).ModFileName)
								if (((ModUpgrader)iBk).IsQueued)
									booQueued = true;
						}
					}

					if (ViewModel.RunningTask.GetType() == typeof(ModInstaller))
					{
						if ((((ModInstaller)ViewModel.RunningTask).ModFileName == ((ModInstaller)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTask(((ModInstaller)p_tskTask));
							return;
						}
					}
					//else if (ViewModel.RunningTask.GetType() == typeof(ModUninstaller))
					//{
					//	if ((((ModUninstaller)ViewModel.RunningTask).ModFileName == ((ModInstaller)p_tskTask).ModFileName) || booQueued)
					//	{
					//		booQueued = false;
					//		m_vmlViewModel.RemoveUselessTask(((ModInstaller)p_tskTask));
					//		return;
					//	}
					//}
					else if (ViewModel.RunningTask.GetType() == typeof(ModUpgrader))
					{
						if ((((ModUpgrader)ViewModel.RunningTask).ModFileName == ((ModInstaller)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTask(((ModInstaller)p_tskTask));
							return;
						}
					} 
				}
				else if(p_tskTask.GetType() == typeof(ModUninstaller))
				{
					foreach (IBackgroundTaskSet iBk in QueuedTasks)
					{
						if (iBk.GetType() == typeof(ModInstaller))
						{
							if (((ModInstaller)iBk).ModFileName == ((ModUninstaller)p_tskTask).ModFileName)
								if (((ModInstaller)iBk).IsQueued)
									booQueued = true;
						}
						else if (iBk.GetType() == typeof(ModUninstaller))
						{
							if (((ModUninstaller)iBk).ModFileName == ((ModUninstaller)p_tskTask).ModFileName)
								if (((ModUninstaller)iBk).IsQueued)
									booQueued = true;
						}
						else if (iBk.GetType() == typeof(ModUpgrader))
						{
							if (((ModUpgrader)iBk).ModFileName == ((ModUninstaller)p_tskTask).ModFileName)
								if (((ModUpgrader)iBk).IsQueued)
									booQueued = true;
						}
					}

					//if (ViewModel.RunningTask.GetType() == typeof(ModInstaller))
					//{
					//	if ((((ModInstaller)ViewModel.RunningTask).ModFileName == ((ModUninstaller)p_tskTask).ModFileName) || booQueued)
					//	{
					//		booQueued = false;
					//		m_vmlViewModel.RemoveUselessTaskUn(((ModUninstaller)p_tskTask));
					//		return;
					//	}
					//}
					//else 
					if (ViewModel.RunningTask.GetType() == typeof(ModUninstaller))
					{
						if ((((ModUninstaller)ViewModel.RunningTask).ModFileName == ((ModUninstaller)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTaskUn(((ModUninstaller)p_tskTask));
							return;
						}
					}
					else if (ViewModel.RunningTask.GetType() == typeof(ModUpgrader))
					{
						if ((((ModUpgrader)ViewModel.RunningTask).ModFileName == ((ModUninstaller)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTaskUn(((ModUninstaller)p_tskTask));
							return;
						}
					}
				}
				else if (p_tskTask.GetType() == typeof(ModUpgrader))
				{
					foreach (IBackgroundTaskSet iBk in QueuedTasks)
					{
						if (iBk.GetType() == typeof(ModInstaller))
						{
							if (((ModInstaller)iBk).ModFileName == ((ModUpgrader)p_tskTask).ModFileName)
								if (((ModInstaller)iBk).IsQueued)
									booQueued = true;
						}
						else if (iBk.GetType() == typeof(ModUninstaller))
						{
							if (((ModUninstaller)iBk).ModFileName == ((ModUpgrader)p_tskTask).ModFileName)
								if (((ModUninstaller)iBk).IsQueued)
									booQueued = true;
						}
						else if (iBk.GetType() == typeof(ModUpgrader))
						{
							if (((ModUpgrader)iBk).ModFileName == ((ModUpgrader)p_tskTask).ModFileName)
								if (((ModUpgrader)iBk).IsQueued)
									booQueued = true;
						}
					}

					if (ViewModel.RunningTask.GetType() == typeof(ModInstaller))
					{
						if ((((ModInstaller)ViewModel.RunningTask).ModFileName == ((ModUpgrader)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTaskUpg(((ModUpgrader)p_tskTask));
							return;
						}
					}
					else if (ViewModel.RunningTask.GetType() == typeof(ModUninstaller))
					{
						if ((((ModUninstaller)ViewModel.RunningTask).ModFileName == ((ModUpgrader)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTaskUpg(((ModUpgrader)p_tskTask));
							return;
						}
					}
					else if (ViewModel.RunningTask.GetType() == typeof(ModUpgrader))
					{
						if ((((ModUpgrader)ViewModel.RunningTask).ModFileName == ((ModUpgrader)p_tskTask).ModFileName) || booQueued)
						{
							booQueued = false;
							m_vmlViewModel.RemoveUselessTaskUpg(((ModUpgrader)p_tskTask));
							return;
						}
					}
				}
			}

			p_tskTask.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(TaskSet_TaskSetCompleted);
			ModActivationMonitorListViewItem lviActivation = new ModActivationMonitorListViewItem(p_tskTask, this);
			UpdateBottomBarFeedback(lviActivation, new EventArgs());
			lvwActiveTasks.Items.Add(lviActivation);

			try
			{
				lviActivation.EnsureVisible();
			}
			catch { }

			if ((ViewModel.RunningTask == null) || (ViewModel.RunningTask.IsCompleted))
			{
				ViewModel.RunningTask = p_tskTask;
				if (ViewModel.RunningTask.GetType() == typeof(ModInstaller))
					((ModInstaller)ViewModel.RunningTask).Install();
				else if (ViewModel.RunningTask.GetType() == typeof(ModUninstaller))
					((ModUninstaller)ViewModel.RunningTask).Install();
				else if (ViewModel.RunningTask.GetType() == typeof(ModUpgrader))
					((ModUpgrader)ViewModel.RunningTask).Install();
			}
			else
			{
				QueuedTasks.Add(p_tskTask);
			}
		}

		private void TaskSet_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			ModInstallerBase mibModInstaller;

			try
			{
				mibModInstaller = (ModInstallerBase)sender;
				m_strPopupErrorMessage = mibModInstaller.PopupErrorMessage;
				m_strPopupErrorMessageType = mibModInstaller.PopupErrorMessageType;
				m_strDetailsErrorMessageType = mibModInstaller.DetailsErrorMessage;
			}
			catch { }
			
			IBackgroundTaskSet btsCompletedTask = null;
			if (sender != null)
			{
				btsCompletedTask = (IBackgroundTaskSet)sender;
			}

			if ((ViewModel.RunningTask == null) || (ViewModel.RunningTask == btsCompletedTask))
			{
				ViewModel.RunningTask = null;

				if (QueuedTasks.Count > 0)
				{
				ViewModel.RunningTask = QueuedTasks.First();
				QueuedTasks.Remove(ViewModel.RunningTask);
				if (ViewModel.RunningTask.GetType() == typeof(ModInstaller))
					((ModInstaller)ViewModel.RunningTask).Install();
				else if (ViewModel.RunningTask.GetType() == typeof(ModUninstaller))
					((ModUninstaller)ViewModel.RunningTask).Install();
				else if (ViewModel.RunningTask.GetType() == typeof(ModUpgrader))
					((ModUpgrader)ViewModel.RunningTask).Install();
				}
				else
					if (EmptyQueue != null)
						EmptyQueue(this, new EventArgs());
			}
		}
		
		private void lvwActiveTasks_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
		{
			e.DrawDefault = true;
		}

		/// <summary>
		/// Handles the <see cref="ModActivationMonitorControl.ColumnWidthChanging"/> event of the Mod Activation list.
		/// </summary>
		void ModActivationMonitorControl_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			if ((e.ColumnIndex == 4) || (e.ColumnIndex == 3))
			{
				e.NewWidth = this.lvwActiveTasks.Columns[e.ColumnIndex].Width;
				e.Cancel = true;
			}
		}

		/// <summary>
		/// Raises the <see cref="ModActivationMonitorControl_DrawItem"/> event.
		/// </summary>
		void ModActivationMonitorControl_DrawItem(object sender, DrawListViewItemEventArgs e)
		{
			e.DrawDefault = true;
		}

		/// <summary>
		/// Raises the <see cref="ModActivationMonitorControl_DrawSubItem"/> event.
		/// </summary>
		void ModActivationMonitorControl_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
		{
			if (m_strPopupErrorMessageType == "Error")
				SetMessage(e.SubItem, m_strPopupErrorMessage, global::Nexus.Client.Properties.Resources.edit_delete_16);
			else if(m_strPopupErrorMessageType == "Warning")
				SetMessage(e.SubItem, m_strPopupErrorMessage, global::Nexus.Client.Properties.Resources.dialog_warning_4);

			OnDrawSubItem(e);
		}

		/// <summary>
		/// Raises the <see cref="ListView.DrawSubItem"/> event.
		/// </summary>
		/// <remarks>
		/// This is where the owner draws the specific sub item. We handle this.
		/// </remarks>
		/// <param name="e">A <see cref="DrawListViewSubItemEventArgs"/> describing the event arguments.</param>
		protected void OnDrawSubItem(DrawListViewSubItemEventArgs e)
		{
			if (e.Item.ListView == null)
				return;
			
			e.DrawBackground();

			Int32 intBoundsX = e.Bounds.X;
			Int32 intBoundsY = e.Bounds.Y;
			Int32 intBoundsWidth = e.Bounds.Width;
			Int32 intFontX = e.Bounds.X + 3;
			Int32 intFontWidth = e.Bounds.Width - 3;
			if (e.Item.SubItems[0] == e.SubItem)
			{
				intBoundsX += 4;
				intBoundsWidth -= 4;
							
				m_intFocusBoundsX = intBoundsX;
			}

			Color clrForeColor = e.SubItem.ForeColor;
			if (e.Item.Selected)
			{
				clrForeColor = e.Item.ListView.Focused ? SystemColors.HighlightText : clrForeColor;
				Color clrBackColor = e.Item.ListView.Focused ? SystemColors.Highlight : SystemColors.Control;
				e.Graphics.FillRectangle(new SolidBrush(clrBackColor), new Rectangle(intBoundsX, intBoundsY, intBoundsWidth, e.Bounds.Height));
			}

			if ((Messages.ContainsKey(e.SubItem)) && (e.ColumnIndex == 4) && (e.SubItem.Text != ""))
			{
				Image imgIcon = Messages[e.SubItem].Value;
				Rectangle rctIconBounds = GetMessageIconBounds(e.Bounds, imgIcon, String.IsNullOrEmpty(e.SubItem.Text) ? true : false);
				Rectangle rctPaint = new Rectangle(new Point(rctIconBounds.X, intBoundsY + rctIconBounds.Y), rctIconBounds.Size);
				e.Graphics.DrawImage(imgIcon, rctPaint);
				intFontWidth -= rctIconBounds.Width;
			}

			Rectangle rctTextBounds = new Rectangle(intFontX, intBoundsY + 2, intFontWidth, e.Bounds.Height - 4);
			TextRenderer.DrawText(e.Graphics, e.SubItem.Text, e.SubItem.Font, rctTextBounds, clrForeColor, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);

			if (e.Item.Focused)
			{
				Pen penFocusRectangle = new Pen(Brushes.Black);
				penFocusRectangle.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
				e.Graphics.DrawRectangle(penFocusRectangle, new Rectangle(m_intFocusBoundsX, intBoundsY, e.Item.Bounds.Width - m_intFocusBoundsX - 1, e.Item.Bounds.Height - 1));
			}
		}

		/// <summary>
		/// Gets the bounds of our custom message icon.
		/// </summary>
		/// <returns>The bounds of our custom message icon.</returns>
		protected Rectangle GetMessageIconBounds(Rectangle p_rctCellBounds, Image p_imgIcon, bool p_booCentered)
		{
			Int32 intYOffset = (p_rctCellBounds.Height - p_imgIcon.Height) / 2;
			Int32 intXOffset = 0;
			if (p_booCentered)
				intXOffset = p_rctCellBounds.Left + (p_rctCellBounds.Width / 2) - (p_imgIcon.Width / 2);
			else
				intXOffset = p_rctCellBounds.Right - p_imgIcon.Width - intYOffset;
			Rectangle rctIconBounds = new Rectangle(new Point(intXOffset, intYOffset), p_imgIcon.Size);
			return rctIconBounds;
		}

		/// <summary>
		/// Sets the message and image for the given sub item.
		/// </summary>
		/// <param name="p_lsiSubItem">The sub item for which to set the message and image.</param>
		/// <param name="p_strMessage">The message to associate with the sub item.</param>
		/// <param name="p_imgIcon">The image to associate with the subitem.</param>
		public void SetMessage(ListViewItem.ListViewSubItem p_lsiSubItem, string p_strMessage, Image p_imgIcon)
		{
			if (p_imgIcon == null)
				p_imgIcon = new Bitmap(16, 16);
											
			Messages[p_lsiSubItem] = new KeyValuePair<string, Image>(p_strMessage, p_imgIcon);
			this.Invalidate(p_lsiSubItem.Bounds);
		}

		/// <summary>
		/// Removes any messages and images assocaited with the given sub item.
		/// </summary>
		/// <param name="p_lsiSubItem">The sub item from which to remove any associated messages and images.</param>
		public void ClearMessage(ListViewItem.ListViewSubItem p_lsiSubItem)
		{
			if (Messages.ContainsKey(p_lsiSubItem))
			{
				Messages.Remove(p_lsiSubItem);
				this.Invalidate(p_lsiSubItem.Bounds);
			}
		}
		
		void ModActivationMonitorControl_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
		{
			e.DrawDefault = true;
		}


		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// task list.
		/// </summary>
		/// <remarks>
		/// This updates the list of tasks to refelct changes to the monitored Mod Activation list.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (lvwActiveTasks.InvokeRequired)
			{
				lvwActiveTasks.Invoke((MethodInvoker)(() => Tasks_CollectionChanged(sender, e)));
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (IBackgroundTaskSet tskAdded in e.NewItems)
							AddTaskToList(tskAdded);
					break;
				case NotifyCollectionChangedAction.Move:
					//TODO Download order matters (some tasks depend on others)
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (IBackgroundTaskSet tskRemoved in e.OldItems)
					{
						for (Int32 i = lvwActiveTasks.Items.Count - 1; i >= 0; i--)
						{
							if (tskRemoved == ((ModActivationMonitorListViewItem)lvwActiveTasks.Items[i]).Task)	
									lvwActiveTasks.Items.RemoveAt(i);
						}
						tskRemoved.TaskSetCompleted -= new EventHandler<TaskSetCompletedEventArgs>(TaskSet_TaskSetCompleted);
					}
					break;
				case NotifyCollectionChangedAction.Reset:
					lvwActiveTasks.Items.Clear();
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
			if (this.IsHandleCreated)
			{
				lock (ViewModel.ModRepository)
					
				if (lvwActiveTasks.InvokeRequired)
				{
					lvwActiveTasks.Invoke((Action)UpdateTitle);
				}
				else
				{
					UpdateTitle();
				}
			}
		}

		/// <summary>
		/// Updates the control's title to reflect the current state of activities.
		/// </summary>
		protected void UpdateTitle()
		{
			Int32 intActiveCount = 0;
			Int32 intTotalCount = 0;
			if ((ViewModel != null) && (ViewModel.Tasks != null))
			{
				intActiveCount = ViewModel.Tasks.Count;
				intTotalCount = ViewModel.Tasks.Count;
			}
			if (intTotalCount == intActiveCount)
				Text = String.Format(m_strTitleAllActive, intTotalCount);
			else
				Text = String.Format(m_strTitleSomeActive, intActiveCount, intTotalCount);
		}

		#endregion


		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the Mod Activation list.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwTasks_SelectedIndexChanged(object sender, EventArgs e)
		{
			//bool booCheckStatus = ViewModel.CheckTaskStatus(lvwActiveTasks.FocusedItem.Text);
			//SetCommandExecutableStatus(booCheckStatus);
			SetCommandExecutableStatus(((ModActivationMonitorListViewItem)lvwActiveTasks.FocusedItem).IsRemovable);
		}

		/// <summary>
		/// Handles the cmsContextMenu.ReadmeScan event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void cmsContextMenu_Copy(object sender, EventArgs e)
		{
			Clipboard.SetText(lvwActiveTasks.FocusedItem.SubItems["ModName"].Text + " // " + lvwActiveTasks.FocusedItem.SubItems["Status"].Text + " // " + lvwActiveTasks.FocusedItem.SubItems["Operation"].Text + " // " + lvwActiveTasks.FocusedItem.SubItems["Progress"].Text);
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
			if (lvwActiveTasks.Columns.Count == 0)
				return;
			m_booResizing = true;
			Int32 intFixedWidth = 0;
			for (Int32 i = 0; i < lvwActiveTasks.Columns.Count; i++)
				if (lvwActiveTasks.Columns[i] != clmOverallMessage)
					intFixedWidth += lvwActiveTasks.Columns[i].Width;

			clmOverallMessage.Width = lvwActiveTasks.ClientSize.Width - intFixedWidth;
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
			ColumnHeader clmThis = lvwActiveTasks.Columns[e.ColumnIndex];
			ColumnHeader clmOther = null;
			if (e.ColumnIndex == lvwActiveTasks.Columns.Count - 1)
				clmOther = lvwActiveTasks.Columns[e.ColumnIndex - 1];
			else
				clmOther = lvwActiveTasks.Columns[e.ColumnIndex + 1];
			m_booResizing = true;
			clmOther.Width += (clmThis.Width - e.NewWidth);
			m_booResizing = false;
		}

		#endregion

		public void CallUpdateBottomBarFeedback(ModActivationMonitorListViewItem p_ActivateModList)
		{
			UpdateBottomBarFeedback(p_ActivateModList, new EventArgs());
		}
	}
}
