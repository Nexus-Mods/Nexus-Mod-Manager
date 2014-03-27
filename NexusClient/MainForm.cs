using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands;
using Nexus.Client.DownloadMonitoring.UI;
using Nexus.UI.Controls;
using Nexus.Client.Games;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;
using Nexus.Client.Util;
using WeifenLuo.WinFormsUI.Docking;
using System.Diagnostics;

namespace Nexus.Client
{
	/// <summary>
	/// The main form of the mod manager.
	/// </summary>
	public partial class MainForm : ManagedFontForm
	{
		private MainFormVM m_vmlViewModel = null;
		private FormWindowState m_fwsLastWindowState = FormWindowState.Normal;
		private ModManagerControl mmgModManager = null;
		private PluginManagerControl pmcPluginManager = null;
		private DownloadMonitorControl dmcDownloadMonitor = null;
		private double m_dblDefaultActivityManagerAutoHidePortion = 0;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected MainFormVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				mmgModManager.ViewModel = m_vmlViewModel.ModManagerVM;
				if (ViewModel.UsesPlugins)
					pmcPluginManager.ViewModel = m_vmlViewModel.PluginManagerVM;
				dmcDownloadMonitor.ViewModel = m_vmlViewModel.DownloadMonitorVM;
				dmcDownloadMonitor.ViewModel.ActiveTasks.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveTasks_CollectionChanged);
				dmcDownloadMonitor.ViewModel.Tasks.CollectionChanged += new NotifyCollectionChangedEventHandler(Tasks_CollectionChanged);
				dmcDownloadMonitor.ViewModel.PropertyChanged += new System.ComponentModel.PropertyChangedEventHandler(ActiveTasks_PropertyChanged);
				this.ViewModel.ModRepository.UserStatusUpdate += new EventHandler(ModRepository_UserStatusUpdate);

				ApplyTheme(m_vmlViewModel.ModeTheme);

				Text = m_vmlViewModel.Title;

				m_vmlViewModel.ConfirmUpdaterAction = ConfirmUpdaterAction;

				foreach (HelpInformation.HelpLink hlpLink in m_vmlViewModel.HelpInfo.HelpLinks)
				{
					ToolStripMenuItem tmiHelp = new ToolStripMenuItem();
					tmiHelp.Tag = hlpLink;
					tmiHelp.Text = hlpLink.Name;
					tmiHelp.ToolTipText = hlpLink.Url;
					tmiHelp.ImageScaling = ToolStripItemImageScaling.None;
					tmiHelp.Click += new EventHandler(tmiHelp_Click);
					spbHelp.DropDownItems.Add(tmiHelp);
				}

				BindCommands();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the view with its dependencies.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public MainForm(MainFormVM p_vmlViewModel)
		{

			InitializeComponent();

			this.FormClosing += new FormClosingEventHandler(this.CheckDownloadsOnClosing);

			pmcPluginManager = new PluginManagerControl();
			mmgModManager = new ModManagerControl();
			dmcDownloadMonitor = new DownloadMonitorControl();
			dockPanel1.ActiveContentChanged += new EventHandler(dockPanel1_ActiveContentChanged);
			mmgModManager.SetTextBoxFocus += new EventHandler(mmgModManager_SetTextBoxFocus);
			mmgModManager.ResetSearchBox += new EventHandler(mmgModManager_ResetSearchBox);
			dmcDownloadMonitor.SetTextBoxFocus += new EventHandler(dmcDownloadMonitor_SetTextBoxFocus);

			ViewModel = p_vmlViewModel;

			try
			{
				InitializeDocuments();
			}
			catch
			{
				ResetUI();
			}

			p_vmlViewModel.EnvironmentInfo.Settings.WindowPositions.GetWindowPosition("MainForm", this);
			m_fwsLastWindowState = WindowState;
		}

		#endregion

		/// <summary>
		/// Initializes the main UI components.
		/// </summary>
		/// <remarks>
		/// If the metrics of the various UI components have been saved, they are loaded. Otherwise,
		/// the default layout is applied.
		/// </remarks>
		protected void InitializeDocuments()
		{
			string strTab = null;

			if (ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.ContainsKey("mainForm") && !String.IsNullOrEmpty(ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"]))
			{
				dockPanel1.LoadFromXmlString(ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"], LoadDockedContent);
				if (m_dblDefaultActivityManagerAutoHidePortion == 0)
					m_dblDefaultActivityManagerAutoHidePortion = dmcDownloadMonitor.AutoHidePortion;
				if (!ViewModel.UsesPlugins)
					pmcPluginManager.Hide();
			}
			else
			{
				if (ViewModel.UsesPlugins)
					pmcPluginManager.DockState = DockState.Unknown;
				mmgModManager.DockState = DockState.Unknown;
				dmcDownloadMonitor.DockState = DockState.Unknown;
				dmcDownloadMonitor.ShowHint = DockState.DockBottomAutoHide;
				dmcDownloadMonitor.Show(dockPanel1, DockState.DockBottomAutoHide);
				if (m_dblDefaultActivityManagerAutoHidePortion == 0)
					m_dblDefaultActivityManagerAutoHidePortion = dmcDownloadMonitor.Height;
				dmcDownloadMonitor.AutoHidePortion = m_dblDefaultActivityManagerAutoHidePortion;

				if (ViewModel.UsesPlugins)
					pmcPluginManager.Show(dockPanel1);
				mmgModManager.Show(dockPanel1);
			}

			strTab = dockPanel1.ActiveDocument.DockHandler.TabText;

			if (ViewModel.PluginManagerVM != null)
				pmcPluginManager.Show(dockPanel1);

			if ((ViewModel.UsesPlugins) && (strTab == "Plugins"))
				pmcPluginManager.Show(dockPanel1);
			else
				mmgModManager.Show(dockPanel1);

			if ((dmcDownloadMonitor == null) || ((dmcDownloadMonitor.VisibleState == DockState.Unknown) || (dmcDownloadMonitor.VisibleState == DockState.Hidden)))
			{
				dmcDownloadMonitor.Show(dockPanel1, DockState.DockBottomAutoHide);
				if (m_dblDefaultActivityManagerAutoHidePortion == 0)
					m_dblDefaultActivityManagerAutoHidePortion = dmcDownloadMonitor.Height;
				dmcDownloadMonitor.AutoHidePortion = m_dblDefaultActivityManagerAutoHidePortion;
			}

			UserStatusFeedback();
		}

		/// <summary>
		/// Sets the UI elements providing feedback on the user online status.
		/// </summary>
		protected void UserStatusFeedback()
		{

			if (ViewModel.OfflineMode)
			{
				if (tpbDownloadSpeed != null)
					tpbDownloadSpeed.Visible = false;
				tlbGoPremium.Visible = true;
				tlbGoPremium.Text = "You are not logged in.";
				tlbGoPremium.Font = new Font(base.Font, FontStyle.Bold);
				tsbGoPremium.Visible = false;
				tsbOnlineStatus.Image = new Bitmap(Properties.Resources.offline_icon, 36, 34);
				tlbDownloads.Visible = false;
			}
			else
			{
				tsbOnlineStatus.Image = new Bitmap(Properties.Resources.online_icon, 36, 34);
				Int32 UserStatus = (ViewModel.UserStatus == null) || String.IsNullOrEmpty(ViewModel.UserStatus[1]) ? 3 : Convert.ToInt32(ViewModel.UserStatus[1]);

				if ((UserStatus != 4) && (UserStatus != 6) && (UserStatus != 13) && (UserStatus != 27) && (UserStatus != 31) && (UserStatus != 32))
				{
					tlbGoPremium.Visible = true;
					tsbGoPremium.Visible = true;
					tsbGoPremium.Enabled = true;
					tlbGoPremium.Text = "Go Faster, Go Premium!";
					tlbGoPremium.Font = new Font(base.Font, FontStyle.Bold);
					if (tpbDownloadSpeed != null)
					{
						tpbDownloadSpeed.Maximum = 1024;
						tpbDownloadSpeed.Value = 0;
						tpbDownloadSpeed.ColorFillMode = Nexus.Client.UI.Controls.ProgressLabel.FillType.Descending;
						tpbDownloadSpeed.ShowOptionalProgress = false;
					}
					tlbDownloads.Tag = "Download Speed:";
				}
				else
				{
					tlbGoPremium.Visible = false;
					tsbGoPremium.Visible = false;
					tsbGoPremium.Enabled = false;
					if (tpbDownloadSpeed != null)
					{
						tpbDownloadSpeed.Maximum = 100;
						tpbDownloadSpeed.Value = 0;
						tpbDownloadSpeed.ColorFillMode = Nexus.Client.UI.Controls.ProgressLabel.FillType.Ascending;
						tpbDownloadSpeed.ShowOptionalProgress = true;
					}
					tlbDownloads.Tag = "Download Progress:";
				}
				if (tpbDownloadSpeed != null)
					tpbDownloadSpeed.Visible = true;
				tlbDownloads.Text = String.Format("{0} ({1} {2}) ", tlbDownloads.Tag, dmcDownloadMonitor.ViewModel.ActiveTasks.Count, (dmcDownloadMonitor.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files"));
			}
		}

		/// <summary>
		/// Resets the UI layout to the default.
		/// </summary>
		protected void ResetUI()
		{
			ViewModel.EnvironmentInfo.Settings.DockPanelLayouts.Remove("mainForm");
			InitializeDocuments();
		}

		/// <summary>
		/// Uninstall all active mods.
		/// </summary>
		protected void UninstallAllMods()
		{
			mmgModManager.DeactivateAllMods();
		}

		/// <summary>
		/// Opens the selected game folder.
		/// </summary>
		protected void OpenGameFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.GamePath))
				System.Diagnostics.Process.Start(ViewModel.GamePath);
		}

		/// <summary>
		/// Checks if there are any active downloads before closing the mod manager.
		/// </summary>
		/// <remarks>
		/// If there's an active download, the program will ask the user if he really wants to close it.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void CheckDownloadsOnClosing(object sender, FormClosingEventArgs e)
		{
			if (this.ViewModel.DownloadMonitorVM.ActiveTasks.Count > 0)
			{
				DialogResult drFormClose = MessageBox.Show(String.Format("There is an ongoing download, are you sure you want to close {0}?", Application.ProductName), "Closing", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
				if (drFormClose != DialogResult.Yes)
					e.Cancel = true;
			}
		}

		/// <summary>
		/// This will check whether the SearchBox should be visible.
		/// </summary>
		private void dockPanel1_ActiveContentChanged(object sender, EventArgs e)
		{
			if ((this.Visible) && (dockPanel1.ActiveDocument != null))
			{
				tstFind.Visible = (dockPanel1.ActiveDocument.DockHandler.TabText == "Mods");
				tstFind.Enabled = (dockPanel1.ActiveDocument.DockHandler.TabText == "Mods");
			}
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void mmgModManager_SetTextBoxFocus(object sender, EventArgs e)
		{
			tstFind.Focus();
		}

		private void mmgModManager_ResetSearchBox(object sender, EventArgs e)
		{
			tstFind.Clear();
		}

		/// <summary>
		/// Set the focus to the Search Textbox.
		/// </summary>
		private void dmcDownloadMonitor_SetTextBoxFocus(object sender, EventArgs e)
		{
			if (mmgModManager.Visible)
				tstFind.Focus();
		}

		/// <summary>
		/// Opens NMM's mods folder for the current game.
		/// </summary>
		protected void OpenModsFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.ModsPath))
				System.Diagnostics.Process.Start(ViewModel.ModsPath);
		}

		private void tstFind_KeyUp(object sender, KeyEventArgs e)
		{
			mmgModManager.FindItemWithText(this.tstFind.Text);
		}

		/// <summary>
		/// Opens NMM's install info folder for the current game.
		/// </summary>
		protected void OpenInstallFolder()
		{
			if (FileUtil.IsValidPath(ViewModel.InstallInfoPath))
				System.Diagnostics.Process.Start(ViewModel.InstallInfoPath);
		}

		#region Binding Helpers

		/// <summary>
		/// Binds the commands to the UI.
		/// </summary>
		protected void BindCommands()
		{
			ViewModel.Updating += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_Updating);
			new ToolStripItemCommandBinding(tsbUpdate, ViewModel.UpdateCommand);

			ViewModel.LogoutCommand.BeforeExecute += new EventHandler<CancelEventArgs>(LogoutCommand_BeforeExecute);
			new ToolStripItemCommandBinding(tsbOnlineStatus, ViewModel.LogoutCommand);

			BindLaunchCommands();
			BindToolCommands();
			BindFolderCommands();
			BindChangeModeCommands();
		}

		#region Logout

		/// <summary>
		/// Handles the <see cref="Command.BeforeExecute"/> event of the logout command.
		/// </summary>
		/// <remarks>
		/// This confirms whether the user wants to logout.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		private void LogoutCommand_BeforeExecute(object sender, CancelEventArgs e)
		{
			if (!ViewModel.OfflineMode)
				if (ExtendedMessageBox.Show(this, "Do you want to logout? This will require you to authenticate using your username and password the next time you try to log in.", "Logout", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) != System.Windows.Forms.DialogResult.Yes)
					e.Cancel = true;
		}

		#endregion

		#region Change Game Mode

		/// <summary>
		/// Handles the <see cref="Command.Executed"/> event of the change game mode command.
		/// </summary>
		/// <remarks>
		/// This closes the application.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void ChangeGameModeCommand_Executed(object sender, EventArgs e)
		{
			Close();
		}

		#endregion

		#region Tasks

		/// <summary>
		/// Handles the <see cref="ModRepository.UserStatusUpdate"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Updates the UI elements.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ModRepository_UserStatusUpdate(object sender, EventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs>)ModRepository_UserStatusUpdate, sender, e);
				return;
			}
			UserStatusFeedback();
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Displays the activity monitor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void Tasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)Tasks_CollectionChanged, sender, e);
				return;
			}
			dmcDownloadMonitor.Activate();

			if (!ViewModel.OfflineMode)
			{
				tlbDownloads.Text = String.Format("{0} ({1} {2}) ", tlbDownloads.Tag, dmcDownloadMonitor.ViewModel.ActiveTasks.Count, (dmcDownloadMonitor.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files"));
				if (dmcDownloadMonitor.ViewModel.ActiveTasks.Count <= 0)
					UpdateProgressBarSpeed("TotalSpeed", true);
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the active tasks list.
		/// </summary>
		/// <remarks>
		/// Displays the activity monitor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, NotifyCollectionChangedEventArgs>)ActiveTasks_CollectionChanged, sender, e);
				return;
			}
			dmcDownloadMonitor.Activate();

			if (!ViewModel.OfflineMode)
			{
				if ((e.OldItems != null) && (e.OldItems.Count > 0))
				{
					foreach (AddModTask Task in e.OldItems)
						if (!String.IsNullOrEmpty(Task.ErrorCode) && (Task.ErrorCode == "666") && !((Task.Status == TaskStatus.Cancelling) || (Task.Status == TaskStatus.Cancelled) || (Task.Status == TaskStatus.Complete)))
						{
							MessageBox.Show(String.Format("The NMM web services have currently been disabled by staff of the sites."
								+ " This is NOT an error with NMM and you DO NOT need to report this error to us."
								+ " This is normally a temporary problem so please try again a bit later on in the day." + Environment.NewLine
								+ "If the staff have provided a reason for this down time we'll display it below: {0}", Environment.NewLine + Environment.NewLine + Task.ErrorInfo), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
						}
				}
				tlbDownloads.Text = String.Format("{0} ({1} {2}) ", tlbDownloads.Tag, dmcDownloadMonitor.ViewModel.ActiveTasks.Count, (dmcDownloadMonitor.ViewModel.ActiveTasks.Count == 1 ? "File" : "Files"));
				if (dmcDownloadMonitor.ViewModel.ActiveTasks.Count <= 0)
					UpdateProgressBarSpeed("TotalSpeed", true);
			}
		}

		/// <summary>
		/// Handles the <see cref="System.ComponentModel.ProgressChangedEventHandler"/> event of the active tasks list.
		/// </summary>
		/// <remarks>
		/// Checks the current downloading speed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="System.ComponentModel.PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveTasks_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, System.ComponentModel.PropertyChangedEventArgs>)ActiveTasks_PropertyChanged, sender, e);
				return;
			}
			UpdateProgressBarSpeed(e.PropertyName, false);
		}

		/// <summary>
		/// Checks if the downloading speed progress bar needs to be updated.
		/// </summary>
		/// <param name="PropertyName">The property name.</param>
		/// <param name="OverrideSpeed">If true the speed value is overridden with a 0.</param>
		private void UpdateProgressBarSpeed(string PropertyName, bool OverrideSpeed)
		{
			if (tpbDownloadSpeed != null)
			{
				if ((tpbDownloadSpeed.IsValid) && ((PropertyName == "TotalSpeed") || (PropertyName == "TotalProgress")))
				{
					if (OverrideSpeed)
					{
						tpbDownloadSpeed.Value = 0;
						if ((tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Fixed))
							tpbDownloadSpeed.Maximum = 1;
						tpbDownloadSpeed.Visible = false;
					}
					else if (tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Fixed)
					{
						tpbDownloadSpeed.Visible = true;
						tpbDownloadSpeed.Maximum = dmcDownloadMonitor.ViewModel.TotalSpeed > 0 ? dmcDownloadMonitor.ViewModel.TotalSpeed : 1;
						tpbDownloadSpeed.Value = tpbDownloadSpeed.Maximum;
					}
					else if (tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Ascending)
					{
						tpbDownloadSpeed.Visible = true;
						if (dmcDownloadMonitor.ViewModel.TotalMaxProgress > 0)
						{
							tpbDownloadSpeed.Value = Convert.ToInt32((Convert.ToSingle(dmcDownloadMonitor.ViewModel.TotalProgress) / Convert.ToSingle(dmcDownloadMonitor.ViewModel.TotalMaxProgress)) * 100);
							tpbDownloadSpeed.OptionalValue = dmcDownloadMonitor.ViewModel.TotalSpeed;
						}
					}
					else if (tpbDownloadSpeed.ColorFillMode == Nexus.Client.UI.Controls.ProgressLabel.FillType.Descending)
					{
						tpbDownloadSpeed.Visible = true;
						if (dmcDownloadMonitor.ViewModel.TotalSpeed <= 1024)
							tpbDownloadSpeed.Value = dmcDownloadMonitor.ViewModel.TotalSpeed;
						else
							tpbDownloadSpeed.Value = 1024;
					}
				}
			}
		}

		#endregion

		#endregion

		#region Control Metrics Serialization

		/// <summary>
		/// Raises the <see cref="Form.Closed"/> event of the form.
		/// </summary>
		/// <remarks>
		/// This saves the form's metrics.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			if (!DesignMode)
			{
				ViewModel.EnvironmentInfo.Settings.DockPanelLayouts["mainForm"] = dockPanel1.SaveAsXml();
				ViewModel.EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Returns the UI component being requested when the form's metrics are being loaded.
		/// </summary>
		/// <param name="p_strContentId">The id of the component to return to be positioned.</param>
		/// <returns>The component to return to be positioned.</returns>
		protected IDockContent LoadDockedContent(string p_strContentId)
		{
			if (p_strContentId == typeof(PluginManagerControl).ToString())
				return pmcPluginManager;
			else if (p_strContentId == typeof(ModManagerControl).ToString())
				return mmgModManager;
			else if (p_strContentId == typeof(DownloadMonitorControl).ToString())
				return dmcDownloadMonitor;
			else
				return null;
		}

		#endregion

		#region Maintenance Binding Helpers

		/// <summary>
		/// Handles the <see cref="MainFormVM.Updating"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_Updating(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_Updating, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the setting button.
		/// </summary>
		/// <remarks>Displays the settings form.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbSettings_Click(object sender, EventArgs e)
		{
			SettingsForm frmSettings = new SettingsForm(ViewModel.SettingsFormVM);
			if (frmSettings.ShowDialog(this) == DialogResult.OK)
				mmgModManager.RefreshModList();
		}

		/// <summary>
		/// This asks the user to confirm an updater action.
		/// </summary>
		/// <param name="p_strMessage">The message describing the action to confirm.</param>
		/// <param name="p_strTitle">The title of the action to confirm.</param>
		/// <returns><c>true</c> if the action has been confirmed;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmUpdaterAction(string p_strMessage, string p_strTitle)
		{
			if (InvokeRequired)
				return (bool)Invoke((ConfirmActionMethod)ConfirmUpdaterAction, p_strMessage, p_strTitle);
			return MessageBox.Show(this, p_strMessage, p_strTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
		}

		#endregion

		#region Change Game Mode Binding Helpers

		/// <summary>
		/// Binds the change game mode commands to the UI.
		/// </summary>
		protected void BindChangeModeCommands()
		{
			foreach (Command cmdChangeCommand in ViewModel.ChangeGameModeCommands)
			{
				cmdChangeCommand.Executed += new EventHandler(ChangeGameModeCommand_Executed);
				ToolStripMenuItem tmiChange = new ToolStripMenuItem();
				new ToolStripItemCommandBinding(tmiChange, cmdChangeCommand);
				spbChangeMode.DropDownItems.Add(tmiChange);
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the change game mode button.
		/// </summary>
		/// <remarks>
		/// This displays the list of game modes when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbChangeMode_ButtonClick(object sender, EventArgs e)
		{
			spbChangeMode.DropDown.Show();
		}

		#endregion

		#region Tools Binding Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindToolCommands()
		{
			Command cmdResetUI = new Command("Reset UI", "Resets the UI to the default layout.", ResetUI);
			ToolStripMenuItem tmiResetTool = new ToolStripMenuItem();
			tmiResetTool.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiResetTool, cmdResetUI);
			spbTools.DropDownItems.Add(tmiResetTool);

			Command cmdUninstallAllMods = new Command("Uninstall all active mods", "Uninstalls all active mods.", UninstallAllMods);
			ToolStripMenuItem tmiUninstallAllMods = new ToolStripMenuItem();
			tmiUninstallAllMods.Image = global::Nexus.Client.Properties.Resources.edit_delete;
			new ToolStripItemCommandBinding(tmiUninstallAllMods, cmdUninstallAllMods);
			spbTools.DropDownItems.Add(tmiUninstallAllMods);

			foreach (ITool tolTool in ViewModel.GameToolLauncher.Tools)
			{
				ToolStripMenuItem tmiTool = new ToolStripMenuItem();
				tmiTool.Tag = tolTool;
				tmiTool.ImageScaling = ToolStripItemImageScaling.None;
				new ToolStripItemCommandBinding(tmiTool, tolTool.LaunchCommand);
				tolTool.DisplayToolView += new EventHandler<DisplayToolViewEventArgs>(Tool_DisplayToolView);
				tolTool.CloseToolView += new EventHandler<DisplayToolViewEventArgs>(Tool_CloseToolView);
				spbTools.DropDownItems.Add(tmiTool);
			}
		}

		/// <summary>
		/// Handles the <see cref="ITool.CloseToolView"/> event of a tool.
		/// </summary>
		/// <remarks>
		/// This closes the tool's view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DisplayToolViewEventArgs"/> describing the event arguments.</param>
		private void Tool_CloseToolView(object sender, DisplayToolViewEventArgs e)
		{
			((Form)e.ToolView).Close();
		}

		/// <summary>
		/// Handles the <see cref="ITool.DisplayToolView"/> event of a tool.
		/// </summary>
		/// <remarks>
		/// This shows the tool's view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="DisplayToolViewEventArgs"/> describing the event arguments.</param>
		private void Tool_DisplayToolView(object sender, DisplayToolViewEventArgs e)
		{
			if (e.IsModal)
				((Form)e.ToolView).ShowDialog(this);
			else
				((Form)e.ToolView).Show(this);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbTools_ButtonClick(object sender, EventArgs e)
		{
			spbTools.DropDown.Show();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the Go Premium button.
		/// </summary>
		/// <remarks>
		/// Opens a default browser window on the Premium webpage.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbGoPremium_Click(object sender, EventArgs e)
		{
			System.Diagnostics.Process.Start("http://skyrim.nexusmods.com/users/premium/");
		}

		#endregion

		#region Open Folders Helpers

		/// <summary>
		/// Binds the tool launch commands to the UI.
		/// </summary>
		protected void BindFolderCommands()
		{
			Command cmdGameFolder = new Command("Open Game Folder", "Open the game's root folder in the explorer window.", OpenGameFolder);
			ToolStripMenuItem tmiGameFolder = new ToolStripMenuItem();
			tmiGameFolder.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiGameFolder, cmdGameFolder);
			spbFolders.DropDownItems.Add(tmiGameFolder);

			Command cmdModsFolder = new Command("Open NMM's Mods Folder", "Open NMM's mods folder in the explorer window.", OpenModsFolder);
			ToolStripMenuItem tmiModsFolder = new ToolStripMenuItem();
			tmiModsFolder.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiModsFolder, cmdModsFolder);
			spbFolders.DropDownItems.Add(tmiModsFolder);

			Command cmdInstallFolder = new Command("Open NMM's Install Info Folder", "Open NMM's install info folder in the explorer window.", OpenInstallFolder);
			ToolStripMenuItem tmiInstallFolder = new ToolStripMenuItem();
			tmiInstallFolder.ImageScaling = ToolStripItemImageScaling.None;
			new ToolStripItemCommandBinding(tmiInstallFolder, cmdInstallFolder);
			spbFolders.DropDownItems.Add(tmiInstallFolder);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbFolders_ButtonClick(object sender, EventArgs e)
		{
			spbFolders.DropDown.Show();
		}

		#endregion

		#region Help Links Binding Helpers

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help button.
		/// </summary>
		/// <remarks>
		/// This displays the list of help items when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void spbHelp_ButtonClick(object sender, EventArgs e)
		{
			spbHelp.DropDown.Show();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the help links.
		/// </summary>
		/// <remarks>
		/// This launches the link in the user's browser.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tmiHelp_Click(object sender, EventArgs e)
		{
			HelpInformation.HelpLink hlpLink = (HelpInformation.HelpLink)((ToolStripMenuItem)sender).Tag;
			if (hlpLink == null)
				return;
			try
			{
				System.Diagnostics.Process.Start(hlpLink.Url);
			}
			catch (Win32Exception)
			{
				MessageBox.Show(this, "Cannot find programme to open: " + hlpLink.Url, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Trace.WriteLine("Cannot find programme to open: " + hlpLink.Url);
			}
		}

		#endregion

		#region Game Launch Binding Helpers

		/// <summary>
		/// Binds the game launch commands to the UI.
		/// </summary>
		protected void BindLaunchCommands()
		{
			foreach (Command cmdLaunch in ViewModel.GameLauncher.LaunchCommands)
			{
				ToolStripMenuItem tmiLaunch = new ToolStripMenuItem();
				tmiLaunch.Tag = cmdLaunch;
				new ToolStripItemCommandBinding(tmiLaunch, cmdLaunch);
				spbLaunch.DropDownItems.Add(tmiLaunch);
				if (String.Equals(cmdLaunch.Id, m_vmlViewModel.SelectedGameLaunchCommandId))
				{
					spbLaunch.DefaultItem = tmiLaunch;
					spbLaunch.Text = spbLaunch.DefaultItem.Text;
					spbLaunch.Image = spbLaunch.DefaultItem.Image;
				}
			}
			if (spbLaunch.DefaultItem == null)
			{
				if (spbLaunch.DropDownItems.Count > 0)
				{
					spbLaunch.DefaultItem = spbLaunch.DropDownItems[0];
					spbLaunch.Text = spbLaunch.DefaultItem.Text;
					spbLaunch.Image = spbLaunch.DefaultItem.Image;
				}
				else
				{
					spbLaunch.Text = "Launch Game";
					spbLaunch.Image = null;
					spbLaunch.Enabled = false;
				}
			}
			ViewModel.ConfirmCloseAfterGameLaunch = ConfirmCloseAfterGameLaunch;
			ViewModel.GameLauncher.GameLaunched += new EventHandler<GameLaunchEventArgs>(GameLauncher_GameLaunched);
		}

		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the launch game
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void spbLaunch_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			spbLaunch.DefaultItem = e.ClickedItem;
			spbLaunch.Text = e.ClickedItem.Text;
			toolStrip1.SuspendLayout();
			spbLaunch.Image = e.ClickedItem.Image;
			toolStrip1.ResumeLayout();
			m_vmlViewModel.SelectedGameLaunchCommandId = ((Command)e.ClickedItem.Tag).Id;
		}

		/// <summary>
		/// Confirms if the manager should close after launching the game.
		/// </summary>
		/// <param name="p_booRememberSelection">Whether the selected response should be remembered.</param>
		/// <returns><c>true</c> if the manager should close after game launch;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmCloseAfterGameLaunch(out bool p_booRememberSelection)
		{
			bool booRemember = false;
			bool booClose = (ExtendedMessageBox.Show(this, String.Format("Would you like {0} to close after launching the game?", ViewModel.EnvironmentInfo.Settings.ModManagerName), "Close", "Details", MessageBoxButtons.YesNo, MessageBoxIcon.Question, out booRemember) == DialogResult.Yes);
			p_booRememberSelection = booRemember;
			return booClose;
		}

		/// <summary>
		/// Handles the <see cref="IGameLauncher.GameLaunched"/> event of the game launcher.
		/// </summary>
		/// <remarks>This displays any message resulting from the game launch. If the launch was successful, the
		/// form is closed.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="GameLaunchEventArgs"/> describing the event arguments.</param>
		private void GameLauncher_GameLaunched(object sender, GameLaunchEventArgs e)
		{
			if (!e.Launched)
				MessageBox.Show(this, e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			else if (ViewModel.EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch)
				Close();
		}

		#endregion

		/// <summary>
		/// Applies the given theme to the form.
		/// </summary>
		/// <param name="p_thmTheme">The theme to apply.</param>
		protected void ApplyTheme(Theme p_thmTheme)
		{
			Icon = p_thmTheme.Icon;

			Bitmap imgChangeMod = new Bitmap(spbChangeMode.Image);
			Color clrOld = Color.Fuchsia;
			for (Int32 y = 0; y < imgChangeMod.Height; y++)
			{
				for (Int32 x = 0; x < imgChangeMod.Width; x++)
				{
					clrOld = imgChangeMod.GetPixel(x, y);

					byte r = clrOld.R;
					byte g = clrOld.G;
					byte b = clrOld.B;

					r = g = b = (byte)(0.21 * r + 0.72 * g + 0.07 * b);

					r = (byte)(r / 255.0 * p_thmTheme.PrimaryColour.R);
					g = (byte)(g / 255.0 * p_thmTheme.PrimaryColour.G);
					b = (byte)(b / 255.0 * p_thmTheme.PrimaryColour.B);

					imgChangeMod.SetPixel(x, y, Color.FromArgb(clrOld.A, (Int32)r, (Int32)g, (Int32)b));
				}
			}
			spbChangeMode.Image = imgChangeMod;
		}

		#region Form Events

		/// <summary>
		/// Raises the <see cref="Form.Closing"/> event.
		/// </summary>
		/// <remarks>
		/// This saves the current window position.
		/// </remarks>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		protected override void OnClosing(CancelEventArgs e)
		{
			base.OnClosing(e);
			ViewModel.EnvironmentInfo.Settings.WindowPositions.SetWindowPosition("MainForm", this);
		}

		/// <summary>
		/// Raises the <see cref="Control.Resize"/> event.
		/// </summary>
		/// <remarks>
		/// This saves the last window state before the form was minimized.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (WindowState != FormWindowState.Minimized)
				m_fwsLastWindowState = WindowState;
		}

		/// <summary>
		/// Raises the <see cref="Form.Shown"/> event.
		/// </summary>
		/// <remarks>
		/// This notifies the view model the view is visible.
		/// </remarks>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ViewModel.ViewIsShown();
		}

		#endregion

		/// <summary>
		/// Restores focus to the form.
		/// </summary>
		public void RestoreFocus()
		{
			WindowState = m_fwsLastWindowState;
			Activate();
		}
	}
}
