using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.BackgroundTasks.UI;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.UI.Controls;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Nexus.UI.Controls;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// The view that exposes mod management functionality.
	/// </summary>
	public partial class ModManagerControl : ManagedFontDockContent
	{
		private ModManagerVM m_vmlViewModel = null;
		private List<IBackgroundTaskSet> lstRunningTaskSets = new List<IBackgroundTaskSet>();
		private bool m_booResizing = false;
		private Timer m_tmrColumnSizer = new Timer();
		private bool m_booDisableSummary = true;

		public event EventHandler SetTextBoxFocus;
		public event EventHandler ResetSearchBox;
		public event EventHandler<ModEventArgs> UninstallModFromProfiles;
		public event EventHandler UpdateModsCount;
		public event EventHandler UninstalledAllMods;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ModManagerVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				
				m_vmlViewModel.UpdatingCategory += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_UpdatingCategory);
				m_vmlViewModel.UpdatingMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_UpdatingMods);
				m_vmlViewModel.UpdatingCategories += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_UpdatingCategories);
				m_vmlViewModel.TogglingAllWarning += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_TogglingAllWarning);
				m_vmlViewModel.ReadMeManagerSetup += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ReadMeManagerSetup);
				m_vmlViewModel.AddingMod += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_AddingMod);
				m_vmlViewModel.DeletingMod += new EventHandler<EventArgs<IBackgroundTaskSet>>(ViewModel_DeletingMod);
				m_vmlViewModel.ActivatingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ActivatingMultipleMods);
				m_vmlViewModel.ActivatingMod += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ActivatingMod);
				m_vmlViewModel.ReinstallingMod += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ReinstallingMod);
				m_vmlViewModel.DisablingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_DisablingMultipleMods);
				m_vmlViewModel.DeletingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_DeletingMultipleMods);
				m_vmlViewModel.DeactivatingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_DeactivatingMultipleMods);
				m_vmlViewModel.AutomaticDownloading += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_AutomaticDownloading);
				m_vmlViewModel.ChangingModActivation += new EventHandler<EventArgs<IBackgroundTaskSet>>(ViewModel_ChangingModActivation);
				m_vmlViewModel.TaggingMod += new EventHandler<EventArgs<ModTaggerVM>>(ViewModel_TaggingMod);
				m_vmlViewModel.ManagedMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ManagedMods_CollectionChanged);
				m_vmlViewModel.ActiveMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveMods_CollectionChanged);
				foreach (IMod modMod in m_vmlViewModel.ManagedMods)
				{
					modMod.PropertyChanged -= new PropertyChangedEventHandler(Mod_PropertyChanged);
					modMod.PropertyChanged += new PropertyChangedEventHandler(Mod_PropertyChanged);
				}


				LoadModFormatFilter();

				tsbAddMod.DefaultItem = tsbAddMod.DropDownItems[m_vmlViewModel.Settings.SelectedAddModCommandIndex];
				tsbAddMod.Text = tsbAddMod.DefaultItem.Text;
				tsbAddMod.Image = tsbAddMod.DefaultItem.Image;

				m_vmlViewModel.ConfirmModFileDeletion = ConfirmModFileDeletion;
				m_vmlViewModel.ConfirmModFileOverwrite = ConfirmModFileOverwrite;
				m_vmlViewModel.ConfirmItemOverwrite = ConfirmItemOverwrite;
				m_vmlViewModel.ConfirmModUpgrade = ConfirmModUpgrade;

				new ToolStripItemCommandBinding<List<IMod>>(tsbActivate, m_vmlViewModel.ActivateModCommand, GetSelectedMods);
				new ToolStripItemCommandBinding<IMod>(tsbDeactivate, m_vmlViewModel.DisableModCommand, GetSelectedMod);
				new ToolStripItemCommandBinding<IMod>(tsbTagMod, m_vmlViewModel.TagModCommand, GetSelectedMod);
				Command cmdToggleEndorsement = new Command("Toggle Mod Endorsement", "Toggles the mod endorsement.", ToggleEndorsement);
				new ToolStripItemCommandBinding(tsbToggleEndorse, cmdToggleEndorsement);
				ViewModel.DeleteModCommand.CanExecute = false;
				ViewModel.ActivateModCommand.CanExecute = false;
				ViewModel.DisableModCommand.CanExecute = false;
				ViewModel.TagModCommand.CanExecute = false;
				ViewModel.ParentForm = this;

				LoadMetrics();
				HidePanels();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ModManagerControl()
		{
			this.Load += new EventHandler(ModManagerControl_Load);
			InitializeComponent();

			clwCategoryView.BeforeSorting += new EventHandler<BrightIdeasSoftware.BeforeSortingEventArgs>(clwCategoryView_BeforeSorting);
			clwCategoryView.ColumnClick += new ColumnClickEventHandler(clwCategoryView_ColumnClick);
			clwCategoryView.CategorySwitch += new EventHandler(CategoryListView_CategorySwitch);
			clwCategoryView.CategoryRemoved += new EventHandler(CategoryListView_CategoryRemoved);
			clwCategoryView.CategoryShowEmptyToggled += clwCategoryView_CategoryShowEmptyToggled;
			clwCategoryView.FileDropped += new EventHandler(CategoryListView_FileDropped);
			clwCategoryView.UpdateWarningToggled += CategoryListView_ToggleUpdateWarning;
			clwCategoryView.AllUpdateWarningsToggled += CategoryListViewAllUpdateWarningsToggled;
			clwCategoryView.ModActionRequested += CategoryListView_ModActionRequested;
			clwCategoryView.ReadmeScan += new EventHandler(CategoryListView_ReadmeScan);
			clwCategoryView.ModReadmeFileRequested += CategoryListView_OpenReadMeFile;
			clwCategoryView.CellEditFinishing += new BrightIdeasSoftware.CellEditEventHandler(CategoryListView_CellEditFinishing);
			clwCategoryView.CellToolTipShowing += new EventHandler<BrightIdeasSoftware.ToolTipShowingEventArgs>(CategoryListView_CellToolTipShowing);

			tsbAddMod.DefaultItem = tsbAddMod.DropDownItems[0];
			tsbAddMod.Text = tsbAddMod.DefaultItem.Text;
			tsbAddMod.Image = tsbAddMod.DefaultItem.Image;
			tsbAddMod.DropDownItemClicked += new ToolStripItemClickedEventHandler(tsbAddMod_DropDownItemClicked);

			tsbResetCategories.DefaultItem = tsbResetCategories.DropDownItems[0];

			m_tmrColumnSizer.Interval = 100;
			m_tmrColumnSizer.Tick += new EventHandler(ColumnSizer_Tick);
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
				ViewModel.CheckReadMeManager();

				clwCategoryView.CategoryModeEnabled = ViewModel.Settings.UseCategoryView;
				LoadCategoryView();

				if (!ViewModel.IsCategoryInitialized)
				{
					ViewModel.CheckCategoryManager();
					clwCategoryView.LoadData();
					clwCategoryView.RefreshContextMenuCategoryList();
					clwCategoryView.ReloadList(false);
					ResetSearchBox(this, e);
				}

				m_booDisableSummary = false;
			}
		}

		/// <summary>
		/// Loads the control's saved metrics.
		/// </summary>
		protected void LoadMetrics()
		{
			if (ViewModel != null)
			{
				if (!ViewModel.ModManager.GameMode.UsesModLoadOrder)
				{
					try
					{
						clwCategoryView.Columns.RemoveAt(2);
						tsb_SaveModLoadOrder.Visible = false;
						tsb_ModUpLoadOrder.Visible = false;
						tsb_ModDownLoadOrder.Visible = false;
						toolStrip1.Items.RemoveByKey("tsb_SaveModLoadOrder");
						toolStrip1.Items.RemoveByKey("tsb_ModUpLoadOrder");
						toolStrip1.Items.RemoveByKey("tsb_ModDownLoadOrder");
					}
					catch { }
				}
				
				ViewModel.Settings.SplitterSizes.LoadSplitterSizes("modManager", sptMods);
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("modManager", clwCategoryView);

				SizeColumnsToFit();
			}
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void checkModUpdates_Click(object sender, EventArgs e)
		{
			try
			{
				m_booDisableSummary = true;
				ViewModel.CheckForUpdates(false);
				m_booDisableSummary = false;
			}
			catch (Exception ex)
			{
				if (ex.Message != "Login required")
				{
					string strMessage = "Couldn't perform the update check, retry later.";
					strMessage += Environment.NewLine + Environment.NewLine + ex.Message;
					MessageBox.Show(this, strMessage, "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the tools button.
		/// </summary>
		/// <remarks>
		/// This displays the list of tools when the button is clicked.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbModOnlineChecks_ButtonClick(object sender, EventArgs e)
		{
			tsbModOnlineChecks.DropDown.Show(new System.Drawing.Point(1,1));
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void checkFileDownloadId_Click(object sender, EventArgs e)
		{
			try
			{
				m_booDisableSummary = true;
				ViewModel.CheckModFileDownloadId(null);
				m_booDisableSummary = false;
			}
			catch (Exception ex)
			{
				if (ex.Message != "Login required")
				{
					string strMessage = "Couldn't perform the update check, retry later.";
					strMessage += Environment.NewLine + Environment.NewLine + ex.Message;
					MessageBox.Show(this, strMessage, "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void checkMissingDownloadId_Click(object sender, EventArgs e)
		{
			try
			{
				m_booDisableSummary = true;
				ViewModel.CheckModFileDownloadId(true);
				m_booDisableSummary = false;
			}
			catch (Exception ex)
			{
				if (ex.Message != "Login required")
				{
					string strMessage = "Couldn't perform the update check, retry later.";
					strMessage += Environment.NewLine + Environment.NewLine + ex.Message;
					MessageBox.Show(this, strMessage, "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
		}

		private void ToggleEndorsement()
		{
			tsbToggleEndorse.Enabled = false;
			bool? booCurrentState = false;

			try
			{
				HashSet<IMod> hashMods = GetSelectedModsHashset();
				IMod modMod = GetSelectedMod();
				booCurrentState = modMod.IsEndorsed;
				ViewModel.ToggleModEndorsement(modMod, hashMods, null);
				tsbToggleEndorse.Enabled = true;
			}
			catch (Exception e)
			{
				MessageBox.Show(this, String.Format("Unable to {0} this file:", booCurrentState != true ? "endorse" : "unendorse") + Environment.NewLine + e.Message, "Endorsement Error:", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

			SetCommandExecutableStatus();
		}

		#region Binding

		/// <summary>
		/// Handles the <see cref="UserControl.Load"/> event.
		/// </summary>
		/// <remarks>
		/// This wires up the <see cref="ListView.ItemCheck"/> event of the mod list. We need to
		/// wire it up after the control has loaded so that mod activation status isn't
		/// superfluously changed as items are first added to the list. A simple boolean flag won't
		/// work, as items can be added before the control is loaded, which delays the firing of
		/// the <see cref="ListView.ItemCheck"/> event until after the control is loaded, at which
		/// point the flag would have been reset.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event.</param>
		private void ModManagerControl_Load(object sender, EventArgs e)
		{
		}

		/// <summary>
		/// Allows extension of the dispose method.
		/// </summary>
		/// <remarks>
		/// This unwires listeners that are wired to object on other threads. This is
		/// because if the form is closed before the threads are finished the threads may
		/// raise events to which we are listening, which will cause access to the control
		/// after it has been disposed (which will raise an exception).
		/// </remarks>
		partial void DoDispose()
		{
			if (m_vmlViewModel != null)
			{
				m_vmlViewModel.AddingMod -= ViewModel_AddingMod;
				m_vmlViewModel.ChangingModActivation -= ViewModel_ChangingModActivation;
				m_vmlViewModel.ManagedMods.CollectionChanged -= ManagedMods_CollectionChanged;
			}

			foreach (IBackgroundTaskSet btsSet in lstRunningTaskSets)
			{
				btsSet.TaskSetCompleted -= TaskSet_TaskSetCompleted;
				btsSet.TaskStarted -= TaskSet_TaskStarted;
			}
		}

		/// <summary>
		/// Builds a filter string for open file dialog boxes that will display
		/// only supported mod files.
		/// </summary>
		protected void LoadModFormatFilter()
		{
			IList<string> lstExtensions = ViewModel.GetModFormatExtensions();
			Int32 intFormatCount = lstExtensions.Count;

			StringBuilder stbModTypesDesc = new StringBuilder("Mod Files (");
			StringBuilder stbModTypesFilter = new StringBuilder();
			Int32 i = 0;
			foreach (string strFormat in lstExtensions)
			{
				stbModTypesDesc.Append("*").Append(strFormat);
				stbModTypesFilter.Append("*").Append(strFormat);
				if (i++ < intFormatCount - 1)
				{
					stbModTypesDesc.Append(", ");
					stbModTypesFilter.Append(";");
				}
			}
			stbModTypesDesc.Append(")|");
            ofdChooseMod.Filter = stbModTypesDesc.ToString() + stbModTypesFilter.ToString() + "|All Files (*.*)|*.*";
            ofdChooseMod.Multiselect = true;
		}

		/// <summary>
		/// Retruns the mod that is currently selected in the view.
		/// </summary>
		/// <returns>The mod that is currently selected in the view, or
		/// <c>null</c> if no mod is selected.</returns>
		private IMod GetSelectedMod()
		{
			if ((clwCategoryView.Visible && (clwCategoryView.SelectedIndices.Count == 0)) || (clwCategoryView.GetSelectedItem == null))
				return null;
			else
				return clwCategoryView.SelectedMod;
		}

		/// <summary>
		/// Retruns the mod that is currently selected in the view.
		/// </summary>
		/// <returns>The mod that is currently selected in the view, or
		/// <c>null</c> if no mod is selected.</returns>
		private List<IMod> GetSelectedMods()
		{
			if ((clwCategoryView.Visible && (clwCategoryView.SelectedIndices.Count == 0)) || (clwCategoryView.GetSelectedItem == null))
				return null;
			else
				return clwCategoryView.GetSelectedItems.OfType<IMod>().ToList();
		}

		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		public void SetCommandExecutableStatus()
		{
			if ((((clwCategoryView.SelectedIndices.Count > 0) || (clwCategoryView.SelectedObjects.Count > 0)) && clwCategoryView.Visible && (clwCategoryView.GetSelectedItem.GetType() != typeof(ModCategory))))
			{
				if (clwCategoryView.Visible)
					ViewModel.DisableModCommand.CanExecute = ViewModel.VirtualModActivator.ActiveModList.Contains(Path.GetFileName(GetSelectedMod().Filename).ToLowerInvariant());

				ViewModel.ActivateModCommand.CanExecute = !ViewModel.DisableModCommand.CanExecute;

				ViewModel.DeleteModCommand.CanExecute = true;
				ViewModel.TagModCommand.CanExecute = true;
				tsbToggleEndorse.Enabled = true;
				tsbToggleEndorse.Image = Properties.Resources.thumbsup;
			}
			else
			{
				ViewModel.ActivateModCommand.CanExecute = false;
				ViewModel.DisableModCommand.CanExecute = false;
				ViewModel.TagModCommand.CanExecute = false;
				ViewModel.DeleteModCommand.CanExecute = false;
				tsbToggleEndorse.Enabled = false;
				tsbToggleEndorse.Image = Properties.Resources.thumbsup;
			}

			this.tsbDeactivate.Visible = ViewModel.DisableModCommand.CanExecute;
			this.tsbActivate.Visible = ViewModel.ActivateModCommand.CanExecute;
		}

		#endregion

		#region Task Set Handling

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of a task set.
		/// </summary>
		/// <remarks>
		/// This displays the confirmation message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void TaskSet_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, TaskSetCompletedEventArgs>)TaskSet_TaskSetCompleted, sender, e);
				return;
			}
			lstRunningTaskSets.Remove((IBackgroundTaskSet)sender);
			((IBackgroundTaskSet)sender).TaskStarted -= TaskSet_TaskStarted;
			((IBackgroundTaskSet)sender).TaskSetCompleted -= TaskSet_TaskSetCompleted;
			if (!String.IsNullOrEmpty(e.Message))
			{
				IMod modMod = (IMod)e.ReturnValue;
				bool booActiveLinksCheck = true;

				if (sender.GetType() == typeof(ModInstaller) || sender.GetType() == typeof(ModUpgrader))
					booActiveLinksCheck = ViewModel.VirtualModActivator.CheckHasActiveLinks(modMod);

				if (e.Success && booActiveLinksCheck)
					MessageBox.Show(this, e.Message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
				else
				{
					string strMessage = String.Empty;

					if ((booActiveLinksCheck == false) && e.Success)
					{
						StringBuilder stbMessage = new StringBuilder();
						stbMessage.AppendLine("The mod you just tried to install didn't contain any files. If this is not correct, this usually occurs if the mod used a scripted installer.");
						stbMessage.AppendLine("Verify that you have actually selected installation options within the installer.").AppendLine();
						stbMessage.AppendLine("Right-click the mod from the mod list and select 'Uninstall from all profiles' and then attempt to install the mod again properly, ensuring you follow any guidelines in the scripted installer from the author.");
						strMessage = stbMessage.ToString();
					}
					else
					{
						if (modMod != null)
							ViewModel.VirtualModActivator.DisableMod(modMod);
						strMessage = e.Message;
					}

					MessageBox.Show(this, strMessage, "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskStarted"/> event of a task set.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void TaskSet_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)TaskSet_TaskStarted, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.Updating"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_UpdatingCategory(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_UpdatingCategory, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			ViewModel.VirtualModActivator.SaveList();
			m_booDisableSummary = false;
		}

		private void ViewModel_AutomaticDownloading(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_AutomaticDownloading, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument, false);
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.Updating"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_UpdatingMods(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_UpdatingMods, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;

			if (e.Argument.ReturnValue != null)
			{
				if (e.Argument.ReturnValue.GetType() == typeof(Dictionary<string, string>))
				{
					Dictionary<string, string> dctDownloadID = (Dictionary<string, string>)e.Argument.ReturnValue;
					ViewModel.UpdateVirtualListDownloadId(dctDownloadID);
				}
				else
				{
					string strResult = e.Argument.ReturnValue.ToString();
					if (strResult.Length > 2)
						ExtendedMessageBox.Show(this, strResult, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
			}
		}

		private void ViewModel_UpdatingCategories(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_UpdatingCategories, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;

			if (e.Argument.ReturnValue != null)
				ExtendedMessageBox.Show(this, "Unable to update the category list online, it will use the base categories: " + Environment.NewLine + e.Argument.ReturnValue.ToString(), "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);


			ViewModel.ResetDefaultCategories(e.Argument.ReturnValue != null);
			clwCategoryView.Visible = false;
			clwCategoryView.LoadData();
			clwCategoryView.RefreshContextMenuCategoryList();
			clwCategoryView.ReloadList(false);
			ResetSearchBox(this, e);
			clwCategoryView.Visible = true;
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.ToggleModUpdateWarning"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_TogglingAllWarning(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_TogglingAllWarning, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;
		}

		/// <summary>
		/// Handles the <see cref="MainFormVM.ReadMeManagerSetup"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ReadMeManagerSetup(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ReadMeManagerSetup, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;

			string strMessage = "NMM has gone through the mod list and linked as many ReadMe's it could find to your mods.";
			strMessage += "You can view your mod's ReadMe's by right-clicking a mod in your mod list and clicking 'Open ReadMe file'.";
			MessageBox.Show(strMessage, "ReadMe Manager Setup Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ToolStripDropDownItem.DropDownItemClicked"/> of the add mod
		/// split button.
		/// </summary>
		/// <remarks>
		/// This makes the last selected function the new default for the button.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ToolStripItemClickedEventArgs"/> describing the event arguments.</param>
		private void tsbAddMod_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			tsbAddMod.DefaultItem = e.ClickedItem;
			tsbAddMod.Text = e.ClickedItem.Text;
			toolStrip1.SuspendLayout();
			tsbAddMod.Image = e.ClickedItem.Image;
			toolStrip1.ResumeLayout();
			ViewModel.Settings.SelectedAddModCommandIndex = tsbAddMod.DropDownItems.IndexOf(e.ClickedItem);
			ViewModel.Settings.Save();
		}

		/// <summary>
		/// Disables all the currently active mods.
		/// </summary>
		public void DisableAllMods()
		{
			List<IMod> lstEnabledMods = ViewModel.ActiveMods.Where(x => ViewModel.VirtualModActivator.ActiveModList.Contains(Path.GetFileName(x.Filename).ToLowerInvariant())).ToList();
			if ((lstEnabledMods != null) && (lstEnabledMods.Count > 0))
				ViewModel.DisableMultipleMods(lstEnabledMods);
		}

		/// <summary>
		/// Uninstall all the currently installed mods.
		/// </summary>
		public void DeactivateAllMods(bool p_booForceUninstall, bool p_booSilent)
		{
			ViewModel.DeactivateMultipleMods(ViewModel.ActiveMods, p_booForceUninstall, p_booSilent, false);
		}

		/// <summary>
		/// Uninstall all the currently installed mods.
		/// </summary>
		public void DeactivateAllMods(IList<IMod> p_lstMods, bool p_booForceUninstall, bool p_booSilent, bool p_booFilesOnly)
		{
			ThreadSafeObservableList<IMod> oclMods = new ThreadSafeObservableList<IMod>(p_lstMods);

			ViewModel.DeactivateMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), p_booForceUninstall, p_booSilent, p_booFilesOnly);
		}

		/// <summary>
		/// Deletes the selected mods.
		/// </summary>
		public void DeleteAllMods(IList<IMod> p_lstMods, bool p_booForceUninstall, bool p_booSilent, bool p_booFilesOnly)
		{
			ThreadSafeObservableList<IMod> oclMods = new ThreadSafeObservableList<IMod>(p_lstMods);

			ViewModel.DeleteMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), p_booForceUninstall, p_booSilent, p_booFilesOnly);
		}

		/// <summary>
		/// Installs multiple mods.
		/// </summary>
		public void MultiModInstall(List<IMod> p_lstMods, bool p_booAllowCancel)
		{
			ViewModel.MultiModInstall(p_lstMods, p_booAllowCancel);
		}

		#region Category Management

		/// <summary>
		/// Loads and initializes the CategoryListView control.
		/// </summary>
		private void LoadCategoryView()
		{
			clwCategoryView.ShowHiddenCategories = ViewModel.Settings.ShowEmptyCategory;
			SortOrder sroDefaultSortOrder = SortOrder.Ascending;
			if (Enum.IsDefined(typeof(SortOrder), ViewModel.Settings.CategoryViewDefaultSortOrder))
				sroDefaultSortOrder = (SortOrder)ViewModel.Settings.CategoryViewDefaultSortOrder;
			clwCategoryView.SetPrimarySortColumn(ViewModel.Settings.CategoryViewDefaultSortColumn, sroDefaultSortOrder);

			if (clwCategoryView.Tag == null)
			{
				clwCategoryView.Setup(ViewModel.ManagedMods, ViewModel.ActiveMods, ViewModel.ModRepository, ViewModel.VirtualModActivator, ViewModel.CategoryManager, ViewModel.Settings);

				// handles the selectedindexchanged event of the cateogry view
				this.clwCategoryView.SelectedIndexChanged += delegate(object sender, EventArgs e)
				{
					if ((clwCategoryView.SelectedObjects.Count > 0) && !m_booDisableSummary && ViewModel.Settings.ShowSidePanel)
					{
						if (clwCategoryView.GetSelectedItem.GetType() != typeof(ModCategory))
							UpdateSummary((IMod)clwCategoryView.GetSelectedItem);
					}
					else
						UpdateSummary(null);
					SetCommandExecutableStatus();
				};

				// Enables installing/uninstalling mods using the double click; if a category is clicked it will be expanded/collapsed
				this.clwCategoryView.CellClick += delegate(object sender, BrightIdeasSoftware.CellClickEventArgs e)
				{
					clwCategoryView.RetrieveVirtualItem += new RetrieveVirtualItemEventHandler(clwCategoryView_RetrieveVirtualItem);
					if ((e.ClickCount == 2) && (e.Item != null))
					{
						try
						{
							if (e.Item.RowObject.GetType() == typeof(ModCategory))
							{
								if (clwCategoryView.IsExpanded(e.Item.RowObject))
									clwCategoryView.Collapse(e.Item.RowObject);
								else
									clwCategoryView.Expand(e.Item.RowObject);
							}
							else
							{
								IMod modMod = (IMod)e.Item.RowObject;
								if (modMod != null)
								{
									SetCommandExecutableStatus();

									if (ViewModel.VirtualModActivator.ActiveModList.Contains(Path.GetFileName(modMod.Filename).ToLowerInvariant()))
										ViewModel.DisableModCommand.Execute(modMod);
									else
										ViewModel.ActivateModCommand.Execute(new List<IMod>() { modMod });
								}
							}
						}
						catch
						{
						}
					}
				};

				// The CRTL-F
				this.clwCategoryView.KeyDown += delegate(object sender, KeyEventArgs e)
				{
					if (e.KeyData == (Keys.Control | Keys.F))
					{
						SetTextBoxFocus(this, e);
					}
				};

				this.clwCategoryView.ModInfoRequested += (s, e) =>
				{
					var files = ViewModel.GetModReadMe(e.Mod);
					if (files == null || files.Length < 1)
					{
						return;
					}
					e.ReadmeFiles.AddRange(files);
				};

				// Enables removing categories or mods using the DEL key
				this.clwCategoryView.KeyUp += delegate(object sender, KeyEventArgs e)
				{
					if (e.KeyData == Keys.Delete)
					{
						if (clwCategoryView.GetSelectedItem != null)
						{
							object objSelectedItem = clwCategoryView.GetSelectedItem;

							if (objSelectedItem.GetType() == typeof(ModCategory))
							{
								clwCategoryView.RemoveCategory((IModCategory)objSelectedItem);
							}
							else
							{
								try
								{
									List<IMod> modList = GetSelectedMods();
									if (ViewModel.DeleteModCommand.CanExecute)
									{
										if (modList.Count > 0)
										{
											if (ConfirmModFileDeletion(modList))
											{
												DeactivateAllMods(modList, true, true, false);
												DeleteAllMods(modList, true, true, false);
											}
										}
									}
								}
								catch
								{
								}
							}
						}
					}

				};

				// populates the context menu for the selected item
				this.clwCategoryView.CellRightClick += delegate(object sender, BrightIdeasSoftware.CellRightClickEventArgs e)
				{
					e.MenuStrip = e.Item != null ? clwCategoryView.CategoryViewContextMenu : null;
				};

				clwCategoryView.LoadData();

				if (ViewModel.Settings.ShowExpandedCategories)
					clwCategoryView.ExpandAll();
			}
		}

		/// <summary>
		//The Shift Key + mouse click event 
		/// </summary>
		void clwCategoryView_RetrieveVirtualItem(object sender,RetrieveVirtualItemEventArgs e)
		{
			clwCategoryView.RetrieveVirtualItem -= new RetrieveVirtualItemEventHandler(clwCategoryView_RetrieveVirtualItem);
			SetCommandExecutableStatus();
		}

		/// <summary>
		//Resets the CategoryView Columns to the original width
		/// </summary>
		public void ResetColumns()
		{
			clwCategoryView.ResetColumns();
		}

		/// <summary>
		/// Reload the mod list control if needed
		/// </summary>
		public void RefreshModList()
		{
			if (!clwCategoryView.CategoryModeEnabled)
				clwCategoryView.LoadData();
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.CellToolTipShowing"/>.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="BrightIdeasSoftware.ToolTipShowingEventArgs"/> describing the event arguments.</param>
		private void CategoryListView_CellToolTipShowing(object sender, BrightIdeasSoftware.ToolTipShowingEventArgs e)
		{
			if (e.Column.Text == "Mod Version")
			{
				if (e.Item.RowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)e.Item.RowObject;
					if (!modMod.IsMatchingVersion())
					{
						e.AutoPopDelay = 10000;
						e.Title = String.Format("Online Version: {0}", modMod.LastKnownVersion);
						e.StandardIcon = BrightIdeasSoftware.ToolTipControl.StandardIcons.Info;
						e.Text = "It seems there's a new mod version available on the Nexus,\r\nclick on the version number to access the mod page in your default browser.";
						e.IsBalloon = true;
					}
				}
				else
				{
					IModCategory imcModCategory = (IModCategory)e.Item.RowObject;
					List<IMod> lstOutdatedMods = new List<IMod>(clwCategoryView.GetOutdatedModList(imcModCategory.Id));
					if (lstOutdatedMods.Count > 0)
					{
						e.AutoPopDelay = 10000;
						e.Title = "Some mods in this category could require an update";
						e.StandardIcon = BrightIdeasSoftware.ToolTipControl.StandardIcons.Info;
						string strModlist = String.Empty;
						foreach (IMod modMod in lstOutdatedMods)
							strModlist += modMod.ModName + "\r\n";
						e.Text = strModlist;
						e.IsBalloon = true;
					}
				}
			}

			if (e.Column.Text == "Download Id")
			{
				if (e.Item.RowObject.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)e.Item.RowObject;

					if ((modMod.DownloadId == "0") || (modMod.DownloadId == "-1") || (string.IsNullOrEmpty(modMod.DownloadId)))
					{
						e.AutoPopDelay = 10000;
						e.Title = String.Format("Nexus file ID:");
						e.StandardIcon = BrightIdeasSoftware.ToolTipControl.StandardIcons.Info;
						e.Text = "Either this mod is not present on the Nexus or you need to \r\nuse the 'Check for missing/outdated download IDs' function to retrieve the correct ID.";
						e.IsBalloon = true;
					}
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="TreeView.Expanding"/> event of the category view.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="BrightIdeasSoftware.TreeBranchExpandingEventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_TreeBranchExpanding(object sender, BrightIdeasSoftware.TreeBranchExpandingEventArgs e)
		{
			if (e.Item != null)
			{
				if (e.Item.RowObject.GetType() == typeof(ModCategory))
				{
					ModCategory mctUpdatedCategory = (ModCategory)e.Item.RowObject;
					mctUpdatedCategory.NewMods = 0;
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="TreeView.Collapsing"/> event of the category view.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="BrightIdeasSoftware.TreeBranchExpandingEventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_TreeBranchCollapsing(object sender, BrightIdeasSoftware.TreeBranchCollapsingEventArgs e)
		{
			if (e.Item != null)
			{
				if (e.Item.RowObject.GetType() == typeof(ModCategory))
				{
					ModCategory mctUpdatedCategory = (ModCategory)e.Item.RowObject;
					mctUpdatedCategory.NewMods = 0;
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.CategorySwitch"/> of the switch
		/// mod category context menu.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void CategoryListView_CategorySwitch(object sender, EventArgs e)
		{
			if ((clwCategoryView.SelectedObjects.Count > 0) && (sender != null))
			{
				IModCategory imcNewCategory = (IModCategory)sender;
				List<IMod> lstSelectedMods = new List<IMod>();
				foreach (object Item in clwCategoryView.GetSelectedItems)
				{
					if (Item.GetType() != typeof(ModCategory))
					{
						IMod modMod = (IMod)Item;
						lstSelectedMods.Add(modMod);
					}
				}

				ViewModel.SwitchModsToCategory(lstSelectedMods, imcNewCategory.Id);
				clwCategoryView.ReloadList(true);

				// for some reason reloading the list once ignores Settings.ShowEmptyCategory value
				// calling it twice works correctly
				clwCategoryView.ReloadList(false);
			}
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.ModActionRequested"/> event.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void CategoryListView_ModActionRequested(object sender, ModActionEventArgs e)
		{
			switch (e.Action)
			{
				case ModAction.Activate:
					{
						if (ViewModel.ActivateModCommand.CanExecute)
						{
							ViewModel.ActivateModCommand.Execute(new List<IMod> { e.Mod });
						}
					}
					break;

				case ModAction.Deactivate:
					{
						if (ViewModel.DisableModCommand.CanExecute)
						{
							ViewModel.DisableModCommand.Execute(e.Mod);
						}
					}
					break;

				case ModAction.Uninstall:
					{
						ViewModel.DeactivateMod(e.Mod);
					}
					break;

				case ModAction.Reinstall:
					{
						ViewModel.ReinstallMod(e.Mod, null);
					}
					break;

				case ModAction.UninstallAll:
					{
						UninstallModGlobally(e.Mod);
					}
					break;

				case ModAction.Delete:
					{
						List<IMod> modList = GetSelectedMods();
						if (ViewModel.DeleteModCommand.CanExecute)
						{
							if (modList.Count > 0)
							{
								if (ConfirmModFileDeletion(modList))
								{
									DeactivateAllMods(modList, true, true, false);
									DeleteAllMods(modList, true, true, false);

								}
							}
						}
					}
					break;
			}
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.ModReadmeFileRequested"/> of the opening
		/// of the ReaMe file.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void CategoryListView_OpenReadMeFile(object sender, ModReadmeRequestEventArgs e)
		{
			ViewModel.OpenReadMe(e.Mod, e.ReadmeFileName);
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.EnableMod"/> event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void CategoryListView_EnableMod(object sender, EventArgs e)
		{
			ViewModel.EnableMod(clwCategoryView.SelectedMod);
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.UpdateWarningToggled"/> of the toggle
		/// mod update warning context menu.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ModUpdateWarningEventArgs"/> describing the event arguments.</param>
		private void CategoryListView_ToggleUpdateWarning(object sender, ModUpdateWarningEventArgs e)
		{
			HashSet<IMod> hashMods = GetSelectedModsHashset();

			if ((hashMods != null) && (hashMods.Count > 0))
			{
				ViewModel.ToggleModUpdateWarning(hashMods, e.EnableWarning);
				clwCategoryView.ReloadList(true);
			}
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.AllUpdateWarningsToggled"/> of the toggle
		/// mod update warning context menu.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ModUpdateWarningEventArgs"/> describing the event arguments.</param>
		private void CategoryListViewAllUpdateWarningsToggled(object sender, ModUpdateWarningEventArgs e)
		{
			if (ViewModel.ManagedMods.Count > 0)
			{
				var hashMods = new HashSet<IMod>(ViewModel.ManagedMods);
				if (hashMods.Count > 0)
				{
					ViewModel.ToggleModUpdateWarning(hashMods, e.EnableWarning);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.UpdateWarningToggled"/> of the toggle
		/// mod update warning context menu.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void CategoryListView_ReadmeScan(object sender, EventArgs e)
		{
			HashSet<IMod> hashMods = GetSelectedModsHashset();

			if ((hashMods != null) && (hashMods.Count > 0))
			{
				ViewModel.SetupReadMeManager(hashMods.ToList<IMod>());
				clwCategoryView.ReloadList(true);
			}
		}

		/// <summary>
		/// Parse the mod control list for selected mods.
		/// </summary>
		/// <returns>The hasset containing the selected mods.</returns>
		private HashSet<IMod> GetSelectedModsHashset()
		{
			HashSet<IMod> hashMods = new HashSet<IMod>();

			foreach (object Item in clwCategoryView.GetSelectedItems)
			{
				if (Item.GetType() != typeof(ModCategory))
				{
					IMod modMod = (IMod)Item;
					hashMods.Add(modMod);
				}
				else
				{
					IModCategory imcCategory = (IModCategory)Item;
					var CategoryMods = ViewModel.ManagedMods.Where(Mod => (Mod.CustomCategoryId >= 0 ? Mod.CustomCategoryId : Mod.CategoryId) == imcCategory.Id).ToList();
					foreach (IMod Mod in CategoryMods)
						hashMods.Add(Mod);
				}
			}

			return hashMods;
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.CategoryRemoved"/> of the switch
		/// mod category context menu.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void CategoryListView_CategoryRemoved(object sender, EventArgs e)
		{
			if (sender != null)
			{
				clwCategoryView.RemoveObject((IModCategory)sender);
				ViewModel.SwitchModsToUnassigned((IModCategory)sender);
				clwCategoryView.ReloadList(false);
				ResetSearchBox(this, e);
			}
		}

		/// <summary>
		/// Handles change of visibility of empty categories.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_CategoryShowEmptyToggled(object sender, EventArgs e)
		{
            this.ResetSearchBox?.Invoke(this, e);
        }
		
		/// <summary>
		/// Handles the <see cref="CategoryListView.FileDropped"/> of the switch
		/// mod category context menu.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="EventArgs"/> describing the event arguments.</param>
		private void CategoryListView_FileDropped(object sender, EventArgs e)
		{
			ViewModel.AddModCommand.Execute(sender.ToString());
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the reset categories
		/// to repository default button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void resetDefaultCategories_Click(object sender, EventArgs e)
		{
			try
			{
				m_booDisableSummary = true;
				ViewModel.CheckCategoriesUpdates();
				m_booDisableSummary = false;
			}
			catch (Exception ex)
			{
				if (ex.Message != "Login required")
				{
					string strMessage = "Couldn't perform the update check, retry later.";
					strMessage += Environment.NewLine + Environment.NewLine + ex.Message;
					MessageBox.Show(this, strMessage, "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the reset unassigned mods
		/// to repository default button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void resetUnassignedToDefaultCategories_Click(object sender, EventArgs e)
		{
			List<IMod> lstSelectedMods = new List<IMod>();
			lstSelectedMods.AddRange(from Mod in ViewModel.ManagedMods
									 where ((Mod.CategoryId > 0) && (Mod.CustomCategoryId == 0))
									 select Mod);
			if (lstSelectedMods.Count > 0)
				ViewModel.SwitchModsToCategory(lstSelectedMods, -1);

			ViewModel.CheckForUpdates(true);

			clwCategoryView.Visible = false;
			clwCategoryView.LoadData();
			clwCategoryView.RefreshContextMenuCategoryList();
			clwCategoryView.ReloadList(false);
			ResetSearchBox(this, e);
			clwCategoryView.Visible = true;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void addNewCategory_Click(object sender, EventArgs e)
		{
			clwCategoryView.AddNewCategory();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the switch view
		/// button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbSwitchCategory_Click(object sender, EventArgs e)
		{
			clwCategoryView.CategoryModeEnabled = !clwCategoryView.CategoryModeEnabled;
			clwCategoryView.Visible = false;
			clwCategoryView.LoadData();
			clwCategoryView.ReloadList(false);
			ViewModel.Settings.UseCategoryView = !ViewModel.Settings.UseCategoryView;
			ViewModel.Settings.Save();
			ResetSearchBox(this, e);
			clwCategoryView.Visible = true;
			SetCommandExecutableStatus();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void collapseAllCategories_Click(object sender, EventArgs e)
		{
			clwCategoryView.CollapseAllCategories();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void expandAllCategories_Click(object sender, EventArgs e)
		{
			clwCategoryView.ExpandAllCategories();
		}

		/// <summary>
		/// Toggles display of empty categories.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void toggleHiddenCategories_Click(object sender, EventArgs e)
		{
			clwCategoryView.ToggleShowEmptyCategories();
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.CellEditFinishing"/>.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="BrightIdeasSoftware.CellEditEventArgs"/> describing the event arguments.</param>
		private void CategoryListView_CellEditFinishing(object sender, BrightIdeasSoftware.CellEditEventArgs e)
		{
			string strValue = e.NewValue.ToString();
			if (!String.IsNullOrEmpty(strValue))
			{
				if (e.ListViewItem.RowObject.GetType() == typeof(ModCategory))
				{
					ModCategory mctUpdatedCategory = (ModCategory)e.ListViewItem.RowObject;
					string strOldValue = mctUpdatedCategory.CategoryName;
					mctUpdatedCategory.CategoryName = strValue;
					mctUpdatedCategory.CategoryPath = Path.GetInvalidFileNameChars().Aggregate(strValue, (current, c) => current.Replace(c.ToString(), string.Empty));
					ViewModel.CategoryManager.UpdateCategoryFile();
					clwCategoryView.UpdateData(mctUpdatedCategory, strOldValue);
				}
				else
				{
					lock (clwCategoryView)
						ViewModel.UpdateModName((IMod)e.ListViewItem.RowObject, strValue);
				}

				clwCategoryView.ReloadList(true);
			}

			e.Cancel = true;
		}

		/// <summary>
		/// Handles the <see cref="ListView.AfterLabelEdit"/> event of the category view.
		/// </summary>
		/// <remarks>
		/// This updates the name of the mod/category.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="LabelEditEventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_AfterLabelEdit(object sender, LabelEditEventArgs e)
		{
			string strValue = e.Label;
			if (!String.IsNullOrEmpty(strValue) && (clwCategoryView.SelectedItem != null))
			{
				if (clwCategoryView.SelectedItem.RowObject.GetType() == typeof(ModCategory))
				{
					ModCategory mctListCategory = (ModCategory)clwCategoryView.SelectedItem.RowObject;
					if (mctListCategory != null)
					{
						IModCategory mctUpdatedCategory = ViewModel.CategoryManager.FindCategory(mctListCategory.Id);
						if (mctUpdatedCategory != null)
						{
							string strOldValue = mctUpdatedCategory.CategoryName;
							mctListCategory.CategoryName = strValue;
							mctListCategory.CategoryPath = Path.GetInvalidFileNameChars().Aggregate(strValue, (current, c) => current.Replace(c.ToString(), string.Empty));
							mctUpdatedCategory.CategoryName = mctListCategory.CategoryName;
							mctUpdatedCategory.CategoryPath = mctListCategory.CategoryPath;
							ViewModel.CategoryManager.UpdateCategoryFile();
							clwCategoryView.UpdateData(new ModCategory(mctListCategory), strOldValue);
						}
					}
				}
				else
				{
					lock (clwCategoryView)
						ViewModel.UpdateModName((IMod)clwCategoryView.SelectedItem.RowObject, strValue);
					clwCategoryView.ReloadList(true);
				}
				clwCategoryView.Sort();
				if (clwCategoryView.SelectedItem != null)
					clwCategoryView.SelectedItem.EnsureVisible();
			}

			e.CancelEdit = true;
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.ColumnClick"/> event of the category view.
		/// </summary>
		/// <remarks>
		/// Saves the sort order.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="LabelEditEventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			ViewModel.Settings.CategoryViewDefaultSortColumn = e.Column;
			ViewModel.Settings.CategoryViewDefaultSortOrder = (int)clwCategoryView.LastSortOrder;
			ViewModel.Settings.Save();
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.BeforeSorting"/> event of the category view.
		/// </summary>
		/// <remarks>
		/// Saves the sort order.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="BrightIdeasSoftware.BeforeSortingEventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_BeforeSorting(object sender, BrightIdeasSoftware.BeforeSortingEventArgs e)
		{
			clwCategoryView.SetSecondarySortColumn();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the reset mods to
		/// the Unassigned category.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void resetModsCategory_Click(object sender, EventArgs e)
		{
			if (ViewModel.ResetToUnassigned())
			{
				clwCategoryView.LoadData();
				clwCategoryView.ReloadList(true);
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the remove all
		/// categories.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void removeAllCategories_Click(object sender, EventArgs e)
		{
			if (ViewModel.RemoveAllCategories())
			{
				clwCategoryView.LoadData();
				clwCategoryView.RefreshContextMenuCategoryList();
				clwCategoryView.ReloadList(false);
				ResetSearchBox(this, e);
			}
		}

		#endregion

		#region Mod Management

		#region Mod Addition

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add mod
		/// button.
		/// </summary>
		/// <remarks>
		/// This prompts the user for a file, and then passes the filename as an argument to the
		/// <see cref="ModManagerVM.AddModCommand"/> command.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void addModToolStripMenuItem_Click(object sender, EventArgs e)
		{
            
            if (ofdChooseMod.ShowDialog() == DialogResult.OK)
                foreach (string File in ofdChooseMod.FileNames)
                {
                    ViewModel.AddModCommand.Execute(File);
                }
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add mod
		/// from URL button.
		/// </summary>
		/// <remarks>
		/// This prompts the user for a URL, and then passes the URL as an argument to the
		/// <see cref="ModManagerVM.AddModCommand"/> command.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void addModFromURLToolStripMenuItem_Click(object sender, EventArgs e)
		{
			string strDefault = "nxm://";
			if (Clipboard.ContainsText())
			{
				string strClipboard = Clipboard.GetText();
				if (!String.IsNullOrEmpty(strClipboard) && strClipboard.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
					strDefault = strClipboard;
			}
			PromptDialog ptDialog = PromptDialog.ShowDialog(null, this, "NMM URL: (eg. nxm://Skyrim/mods/193/files/8998)", "Choose URL", strDefault, @"nxm://\w+/mods/\d+/files/\d+", "Must be a Nexus Mod URL.");
			if (ptDialog != null)
			{
				if (!String.IsNullOrEmpty(ptDialog.EnteredText))
					ViewModel.AddModCommand.Execute(ptDialog.EnteredText);
			}
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.AddingMod"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_AddingMod(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_AddingMod, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// This asks the use to confirm the overwriting of the specified mod file.
		/// </summary>
		/// <param name="p_strFileName">The name of the file that is to be overwritten.</param>
		/// <param name="p_strNewFileName">An alternate name the file can be renamed to, instead of being overwritten.</param>
		/// <returns>The name of the file to use to write the file, or <c>null</c> if the operation
		/// should be cancelled.</returns>
		private string ConfirmModFileOverwrite(string p_strFileName, string p_strNewFileName)
		{
			if (InvokeRequired)
			{
				string strResult = null;
				Invoke((MethodInvoker)(() => strResult = ConfirmModFileOverwrite(p_strFileName, p_strNewFileName)));
				return strResult;
			}

			return p_strFileName;
			if (!p_strFileName.Equals(p_strNewFileName))
			{
				switch (MessageBox.Show(this, "File '" + p_strFileName + "' already exists. The old file can be replaced, or the new file can be named '" + p_strNewFileName + "'." + Environment.NewLine + "Do you want to overwrite the old file?", "Warning", MessageBoxButtons.YesNoCancel))
				{
					case DialogResult.Yes:
						return p_strFileName;
					case DialogResult.No:
						return p_strNewFileName;
					case DialogResult.Cancel:
						return null;
				}
			}
			return p_strFileName;
		}

		#endregion

		#region Mod Removal

		/// <summary>
		/// Handles the <see cref="ModManagerVM.DeletingMod"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This watches the deletion task set for starting task, so progress dialogs can be displayed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTaskSet}"/> describing the event arguments.</param>
		private void ViewModel_DeletingMod(object sender, EventArgs<IBackgroundTaskSet> e)
		{
			e.Argument.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(TaskSet_TaskStarted);
			e.Argument.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(TaskSet_TaskSetCompleted);
			lstRunningTaskSets.Add(e.Argument);
		}


		/// <summary>
		/// This asks the use to confirm the deleting of the given mod file.
		/// </summary>
		/// <param name="p_modMod">The mod whose deletion is to be confirmed.</param>
		/// <returns><c>true</c> if the mod should be deleted;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmModFileDeletion(List<IMod> p_lstMod)
		{
			if (InvokeRequired)
			{
				bool booResult = false;
				Invoke((MethodInvoker)(() => booResult = ConfirmModFileDeletion(p_lstMod)));
				return booResult;
			}

			string WarningMessage = string.Empty;

			foreach(IMod mod in p_lstMod)
			{
				WarningMessage = WarningMessage + String.Format("- {0}", mod.ModName + "\r\n");
			}

			WarningMessage = WarningMessage + "\r\nThese mods will be uninstalled and all their files and their archives will be permanently deleted from your hard drive.\r\nAre you sure?\r\n\r\nThis operation cannot be undone.";

			return ExtendedMessageBox.Show(this, WarningMessage, "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// installed mod list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to the installed mod list.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ManagedMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (clwCategoryView.InvokeRequired)
			{
				clwCategoryView.BeginInvoke((MethodInvoker)(() => ManagedMods_CollectionChanged(sender, e)));
				return;
			}

			ModCategory mctCategory = null;

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (IMod modAdded in e.NewItems)
					{
						mctCategory = (ModCategory)ViewModel.CategoryManager.FindCategory(modAdded.CategoryId);
						if (mctCategory.Id == 0)
							ViewModel.SwitchModCategory(modAdded, 0);
						modAdded.PropertyChanged -= new PropertyChangedEventHandler(Mod_PropertyChanged);
						modAdded.PropertyChanged += new PropertyChangedEventHandler(Mod_PropertyChanged);
					}
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (IMod modRemoved in e.OldItems)
					{
						ViewModel.DeleteReadMe(modRemoved);
					}
					break;
			}

			if (clwCategoryView.CategoryModeEnabled)
			{
				if (mctCategory != null)
				{
					if ((e.NewItems != null) && (e.NewItems.Count > 0))
					{
						mctCategory.NewMods += 1;
					}
				}
			}
			else
			{
				if ((e.NewItems != null) && (e.NewItems.Count > 0))
				{
					clwCategoryView.AddObjects(e.NewItems);
				}
				else if ((e.OldItems != null) && (e.OldItems.Count > 0))
				{
					clwCategoryView.RemoveObjects(e.OldItems);
				}
			}

			clwCategoryView.ReloadList(false);
			UpdateModsCount(this, e);
			SetCommandExecutableStatus();
		}

		/// <summary>
		/// Handles the <see cref="INotifyPropertyChanged.PropertyChanged"/> event of the mods.
		/// </summary>
		/// <remarks>
		/// This updates list with the mod's updated information.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="PropertyChangedEventArgs"/> describing the event arguments.</param>
		private void Mod_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, PropertyChangedEventArgs>)Mod_PropertyChanged, sender, e);
				return;
			}
			if ((!clwCategoryView.CategoryModeEnabled) && (!String.Equals(e.PropertyName, "InstallDate")))
			{
				clwCategoryView.ReloadList(true);
			}

			if (clwCategoryView.CategoryModeEnabled)
			{
				if (String.Equals(e.PropertyName, "CustomCategoryId") || String.Equals(e.PropertyName, "ModName"))
					clwCategoryView.Refresh();
				else
					clwCategoryView.RefreshObject((IMod)sender);
			}

			if (!m_booDisableSummary && ViewModel.Settings.ShowSidePanel)
				UpdateSummary((IMod)sender);
		}

		/// <summary>
		/// Applies a string filter to the list view.
		/// </summary>
		public void FindItemWithText(string p_strFilter)
		{
			if (clwCategoryView.CategoryModeEnabled)
				clwCategoryView.ExpandAll();
			clwCategoryView.AddStringFilter(p_strFilter);
			clwCategoryView.Refresh();
		}

		#endregion

		#region Mod Activation

		/// <summary>
		/// Handles the <see cref="ModManagerVM.DisablingMultipleMods"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_DisablingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_DisablingMultipleMods, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;

			if (this.Visible == true)
			{
				string strMessage = "All the active mods were disabled.";
				MessageBox.Show(strMessage, "Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.DeletingMultipleMods"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_DeletingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_DeletingMultipleMods, sender, e);
				return;
			}
			ProgressDialog.ShowDialog(this, e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.DeactivatingMultipleMods"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_DeactivatingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_DeactivatingMultipleMods, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;

			bool booSilent = (bool)sender;

			if (!booSilent && (this.Visible == true))
			{
				string strMessage = "All the active mods were uninstalled.";
				MessageBox.Show(strMessage, "Mod uninstall complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}

			if (e.Argument.ReturnValue == null)
			{
				ViewModel.PurgeXMLInstalledFile();
				if (UninstalledAllMods != null)
					UninstalledAllMods(this, new EventArgs());
			}
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.ActivatingMultipleMods"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ActivatingMultipleMods(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ActivatingMultipleMods, sender, e);
				return;
			}
			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);
			m_booDisableSummary = false;
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.ActivatingMod"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the progress dialog.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ActivatingMod(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ActivatingMod, sender, e);
				return;
			}

			m_booDisableSummary = true;
			ProgressDialog.ShowDialog(this, e.Argument);

			IMod modMod = (IMod)sender;
			if (sender != null)
				clwCategoryView.RefreshObject(modMod);
			m_booDisableSummary = false;
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.ReinstallingMod"/> event of the view model.
 		/// </summary>
 		/// <remarks>
 		/// This displays the progress dialog.
 		/// </remarks>
 		/// <param name="sender">The object that raised the event.</param>
 		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ViewModel_ReinstallingMod(object sender, EventArgs<IBackgroundTask> e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, EventArgs<IBackgroundTask>>)ViewModel_ReinstallingMod, sender, e);
				return;
			}
			
			ProgressDialog.ShowDialog(this, e.Argument);
		}

	/// <summary>
	/// Sets the checked state of the given list view item.
	/// </summary>
	/// <remarks>
	/// This sets the flag indicating we are changing the checked state of the
	/// list view item (as opposed to the user making the change),
	/// thus preventing the execution of commands as a result.
	/// </remarks>
	/// <param name="p_lviPlugin">The list view item whose checked state is to be changed.</param>
	/// <param name="p_booIsActive">Whether the item should be active.</param>
	protected void SetModActivationCheck(ListViewItem p_lviPlugin, bool p_booIsActive)
		{
			p_lviPlugin.Checked = p_booIsActive;
		}

		/// <summary>
		/// Handles the <see cref="ModManagerVM.ChangingModActivation"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This watches the activations task set for starting task, so progress dialogs can be displayed.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTaskSet}"/> describing the event arguments.</param>
		private void ViewModel_ChangingModActivation(object sender, EventArgs<IBackgroundTaskSet> e)
		{
			e.Argument.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(TaskSet_TaskStarted);
			e.Argument.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(TaskSet_TaskSetCompleted);
			lstRunningTaskSets.Add(e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// active mod list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to which mods are active.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActiveMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (clwCategoryView.InvokeRequired)
			{
				clwCategoryView.BeginInvoke((MethodInvoker)(() => ActiveMods_CollectionChanged(sender, e)));
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					//foreach (IMod modAdded in e.NewItems)
					//    if (!clwCategoryView.CategoryModeEnabled)
					//        clwCategoryView.RefreshObject(modAdded);
					break;
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
					//foreach (IMod modAdded in e.OldItems)
					//    if (!clwCategoryView.CategoryModeEnabled)
					//        clwCategoryView.RefreshObject(modAdded);
					break;
			}

			if (!clwCategoryView.CategoryModeEnabled)
				clwCategoryView.ReloadList(true);

			UpdateModsCount(this, e);
			SetCommandExecutableStatus();
		}

		/// <summary>
		/// This asks the user to confirm the overwriting of the specified item.
		/// </summary>
		/// <param name="p_strItemMessage">The message describing the item being overwritten..</param>
		/// <param name="p_booAllowPerGroupChoice">Whether to allow the user to make the decision to make the selection for all items in the current item's group.</param>
		/// <param name="p_booAllowPerModChoice">Whether to allow the user to make the decision to make the selection for all items in the current Mod.</param>
		/// <returns>The user's choice.</returns>
		private OverwriteResult ConfirmItemOverwrite(string p_strItemMessage, bool p_booAllowPerGroupChoice, bool p_booAllowPerModChoice)
		{
			OverwriteResult orsResult = OverwriteResult.No;
			if (InvokeRequired)
				Invoke((MethodInvoker)(() => orsResult = OverwriteForm.ShowDialog(this, p_strItemMessage, p_booAllowPerGroupChoice, p_booAllowPerModChoice)));
			else
				orsResult = OverwriteForm.ShowDialog(this, p_strItemMessage, p_booAllowPerGroupChoice, p_booAllowPerModChoice);
			return orsResult;
		}

		/// <summary>
		/// This asks the use to confirm the upgrading of the given old mod to the given new mod.
		/// </summary>
		/// <param name="p_modOld">The old mod to be upgrade to the new mod.</param>
		/// <param name="p_modNew">The new mod to which to upgrade from the old.</param>
		/// <returns>The user's choice.</returns>
		private ConfirmUpgradeResult ConfirmModUpgrade(IMod p_modOld, IMod p_modNew)
		{
			if (InvokeRequired)
				return (ConfirmUpgradeResult)Invoke((ConfirmModUpgradeDelegate)ConfirmModUpgrade, p_modOld, p_modNew);
			else
			{
				string strUpgradeMessage = "A different version of {0} has been detected. The installed version is {1}, the new version is {2}. Would you like to upgrade?" + Environment.NewLine + "Selecting No will install the new Mod normally.";
				strUpgradeMessage = String.Format(strUpgradeMessage, p_modOld.ModName, p_modOld.HumanReadableVersion, p_modNew.HumanReadableVersion);
				switch (MessageBox.Show(this, strUpgradeMessage, "Upgrade", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
				{
					case DialogResult.Yes:
						return ConfirmUpgradeResult.Upgrade;
					case DialogResult.No:
						return ConfirmUpgradeResult.NormalActivation;
					case DialogResult.Cancel:
						return ConfirmUpgradeResult.Cancel;
					default:
						throw new Exception("Unrecognized result from YesNoCancel message box.");
				}
			}
		}

		#endregion

		#region Tagging

		/// <summary>
		/// Handles the <see cref="ModManagerVM.TaggingMod"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays the mod tagging view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{ModTaggerVM}"/> describing the event arguments.</param>
		private void ViewModel_TaggingMod(object sender, EventArgs<ModTaggerVM> e)
		{
			if (!ViewModel.ModRepository.IsOffline)
				new ModTaggerForm(e.Argument).ShowDialog(this);
		}

		#endregion

		/// <summary>
		/// This will force the list view to refresh.
		/// </summary>
		public void ForceListRefresh()
		{
			clwCategoryView.Refresh();
		}

		/// <summary>
		/// Updates the displayed summary information to reflect the
		/// given mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to display the summary information.</param>
		protected void UpdateSummary(IMod p_modMod)
		{
			ipbScreenShot.Image = (p_modMod == null) ? null : p_modMod.Screenshot;
			flbInfo.Text = (p_modMod == null) ? null : p_modMod.Description;
			HidePanels();
		}

		/// <summary>
		/// Hides the description and image panels if they are empty.
		/// </summary>
		private void HidePanels()
		{
			bool booCollapseImage = (ipbScreenShot.Image == null);
			bool booCollapseDescription = String.IsNullOrEmpty(flbInfo.Text);
			sptMods.Panel2Collapsed = booCollapseImage && booCollapseDescription;
			if (!(booCollapseImage && booCollapseDescription))
			{
				sptSummaryInfo.Panel1Collapsed = booCollapseImage;
				sptSummaryInfo.Panel2Collapsed = booCollapseDescription;
			}
		}

		private void UninstallModGlobally(IMod p_modMod)
		{
			ViewModel.DeactivateMod(p_modMod);

            UninstallModFromProfiles?.Invoke(this, new ModEventArgs(p_modMod));
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
			if (clwCategoryView.Visible)
				SizeColumnsToFitClw();
		}

		/// <summary>
		/// Handles the <see cref="Control.Resize"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This resizes the columns to fill the list view.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_Resize(object sender, EventArgs e)
		{
			m_tmrColumnSizer.Stop();
			m_tmrColumnSizer.Start();
		}

		/// <summary>
		/// Handles the <see cref="ListView.ColumnWidthChanging"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This resizes the column next to the column being resized to resize as well,
		/// so that the columns keep the list view filled.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ColumnWidthChangingEventArgs"/> describing the event arguments.</param>
		private void clwCategoryView_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			if (m_booResizing)
				return;
			ColumnHeader clmThis = clwCategoryView.Columns[e.ColumnIndex];
			ColumnHeader clmOther = null;
			if (e.ColumnIndex == clwCategoryView.Columns.Count - 1)
				clmOther = clwCategoryView.Columns[e.ColumnIndex - 1];
			else
				clmOther = clwCategoryView.Columns[e.ColumnIndex + 1];
			m_booResizing = true;
			clmOther.Width += (clmThis.Width - e.NewWidth);
			m_booResizing = false;
		}

		/// <summary>
		/// Handles the <see cref="ListView.ColumnWidthChanged"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This saves the column width.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ColumnWidthChangedEventHandler"/> describing the event arguments.</param>
		private void clwCategoryView_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
		{
			if (m_booResizing)
				return;

			ViewModel.Settings.ColumnWidths.SaveColumnWidths("modManager", clwCategoryView);
			ViewModel.Settings.Save();
		}

		/// <summary>
		/// This resizes the columns to fill the list view.
		/// </summary>
		protected void SizeColumnsToFitClw()
		{
			if (clwCategoryView.Columns.Count == 0)
				return;
			m_booResizing = true;
			clwCategoryView.SizeColumnsToFit();
			m_booResizing = false;
		}

		#endregion

		#region Mod Sorting
		private void tsb_SaveModLoadOrder_Click(Object sender, EventArgs e)
        {
			if (ViewModel.ModManager.GameMode.UsesModLoadOrder)
	            ViewModel.SaveModLoadOrder();
        }

        private void tsb_ModUpLoadOrder_Click(Object sender, EventArgs e)
        {
			if (ViewModel.ModManager.GameMode.UsesModLoadOrder)
			{
				if (clwCategoryView.SelectedMod != null)
					ViewModel.UpdateModLoadOrder(clwCategoryView.SelectedMod, clwCategoryView.SelectedMod.NewPlaceInModLoadOrder == -1 ? -1 : --clwCategoryView.SelectedMod.NewPlaceInModLoadOrder);
				else if (clwCategoryView.GetSelectedItems.Count > 0)
				{
					IEnumerable<IMod> cast = clwCategoryView.GetSelectedItems.Cast<IMod>();
					foreach (IMod mod in cast)
						ViewModel.UpdateModLoadOrder(mod, mod.NewPlaceInModLoadOrder == -1 ? -1 : --mod.NewPlaceInModLoadOrder);
				}

				Refresh();
			}
        }

        private void tsb_ModDownLoadOrder_Click(Object sender, EventArgs e)
        {
			if (ViewModel.ModManager.GameMode.UsesModLoadOrder)
			{
				if (clwCategoryView.SelectedMod != null)
					ViewModel.UpdateModLoadOrder(clwCategoryView.SelectedMod, clwCategoryView.SelectedMod.NewPlaceInModLoadOrder == int.MaxValue ? int.MaxValue : ++clwCategoryView.SelectedMod.NewPlaceInModLoadOrder);
				else if (clwCategoryView.GetSelectedItems.Count > 0)
				{
					IEnumerable<IMod> cast = clwCategoryView.GetSelectedItems.Cast<IMod>();
					foreach (IMod mod in cast)
						ViewModel.UpdateModLoadOrder(mod, mod.NewPlaceInModLoadOrder == int.MaxValue ? int.MaxValue : ++mod.NewPlaceInModLoadOrder);
				}

				Refresh();
			}
        }
    }

	#endregion
}
