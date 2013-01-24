using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
		private bool m_booSettingModActiveCheck = false;
		private bool m_booResizing = false;
		private Timer m_tmrColumnSizer = new Timer();
		private ListViewItem.ListViewSubItem m_lsiLastSelectedWebVersion = null;
		private bool m_booControlIsLoaded = false;
		private bool m_booDisableSummary = true;

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
				m_vmlViewModel.AddingMod += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_AddingMod);
				m_vmlViewModel.DeletingMod += new EventHandler<EventArgs<IBackgroundTaskSet>>(ViewModel_DeletingMod);
				m_vmlViewModel.ChangingModActivation += new EventHandler<EventArgs<IBackgroundTaskSet>>(ViewModel_ChangingModActivation);
				m_vmlViewModel.TaggingMod += new EventHandler<EventArgs<ModTaggerVM>>(ViewModel_TaggingMod);
				m_vmlViewModel.NewestModInfo.CollectionChanged += new NotifyCollectionChangedEventHandler(NewestModInfo_CollectionChanged);
				foreach (IMod modMod in m_vmlViewModel.ManagedMods)
					AddModToList(modMod);
				m_vmlViewModel.ManagedMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ManagedMods_CollectionChanged);
				m_vmlViewModel.ActiveMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveMods_CollectionChanged);

				LoadModFormatFilter();

				tsbAddMod.DefaultItem = tsbAddMod.DropDownItems[m_vmlViewModel.Settings.SelectedAddModCommandIndex];
				tsbAddMod.Text = tsbAddMod.DefaultItem.Text;
				tsbAddMod.Image = tsbAddMod.DefaultItem.Image;

				m_vmlViewModel.ConfirmModFileDeletion = ConfirmModFileDeletion;
				m_vmlViewModel.ConfirmModFileOverwrite = ConfirmModFileOverwrite;
				m_vmlViewModel.ConfirmItemOverwrite = ConfirmItemOverwrite;
				m_vmlViewModel.ConfirmModUpgrade = ConfirmModUpgrade;

				new ToolStripItemCommandBinding<IMod>(tsbDeleteMod, m_vmlViewModel.DeleteModCommand, GetSelectedMod);
				new KeyDownCommandBinding<IMod>(lvwMods, m_vmlViewModel.DeleteModCommand, GetSelectedMod, Keys.Delete);
				new ToolStripItemCommandBinding<IMod>(tsbActivate, m_vmlViewModel.ActivateModCommand, GetSelectedMod);
				new ToolStripItemCommandBinding<IMod>(tsbDeactivate, m_vmlViewModel.DeactivateModCommand, GetSelectedMod);
				new ToolStripItemCommandBinding<IMod>(tsbTagMod, m_vmlViewModel.TagModCommand, GetSelectedMod);
				Command cmdToggleEndorsement = new Command("Toggle Mod Endorsement", "Toggles the mod endorsement.", ToggleEndorsement);
				new ToolStripItemCommandBinding(tsbToggleEndorse, cmdToggleEndorsement);
				Command cmdCheckModVersions = new Command("Check Mod Versions", "Checks for new mod versions.", CheckModVersions);
				new ToolStripItemCommandBinding(tsbCheckModVersions, cmdCheckModVersions);
				ViewModel.DeleteModCommand.CanExecute = false;
				ViewModel.ActivateModCommand.CanExecute = false;
				ViewModel.DeactivateModCommand.CanExecute = false;
				ViewModel.TagModCommand.CanExecute = false;

				addModFromURLToolStripMenuItem.Enabled = !m_vmlViewModel.OfflineMode;

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

			lvwMods.FontChanged += new EventHandler(lvwMods_FontChanged);
			clwCategoryView.CategorySwitch += new EventHandler(CategoryListView_CategorySwitch);
			clwCategoryView.CategoryRemoved += new EventHandler(CategoryListView_CategoryRemoved);
			clwCategoryView.CellEditFinishing += new BrightIdeasSoftware.CellEditEventHandler(CategoryListView_CellEditFinishing);
			clwCategoryView.CellToolTipShowing += new EventHandler<BrightIdeasSoftware.ToolTipShowingEventArgs>(CategoryListView_CellToolTipShowing);

			clmModName.Name = "ModName";
			clmCategory.Name = "Category";
			clmInstallDate.Name = "InstallDate";
			clmVersion.Name = "HumanReadableVersion";
			clmWebVersion.Name = "WebVersion";
			clmAuthor.Name = "Author";
			clmEndorsement.Name = "Endorsement";

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
				m_booControlIsLoaded = true;
				LoadMetrics();
				LoadCategoryView();
				if (!ViewModel.IsCategoryInitialized)
				{
					ViewModel.CheckCategoryManager();
					clwCategoryView.LoadData();
					clwCategoryView.SetupContextMenu();
					clwCategoryView.RebuildAll(true);
				}
				m_booDisableSummary = false;
			}
		}

		/// <summary>
		/// Loads the control's saved metrics.
		/// </summary>
		protected void LoadMetrics()
		{
			if (m_booControlIsLoaded && (ViewModel != null))
			{
				ViewModel.Settings.SplitterSizes.LoadSplitterSizes("modManager", sptMods);
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("modManager", lvwMods);

				FindForm().FormClosing += new FormClosingEventHandler(PluginManagerControl_FormClosing);
				SizeColumnsToFit();
				SizeColumnsToFitClw();
			}
		}

		/// <summary>
		/// Handles the <see cref="Form.Closing"/> event of the parent form.
		/// </summary>
		/// <remarks>
		/// This save the control's metrics.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="FormClosingEventArgs"/> describing the event arguments.</param>
		private void PluginManagerControl_FormClosing(object sender, FormClosingEventArgs e)
		{
			ViewModel.Settings.SplitterSizes.SaveSplitterSizes("modManager", sptMods);
			ViewModel.Settings.ColumnWidths.SaveColumnWidths("modManager", lvwMods);
			ViewModel.Settings.Save();
		}

		#endregion

		private void CheckModVersions()
		{
			try
			{
				ViewModel.CheckForUpdates(false);
			}
			catch (Exception e)
			{
				string strMessage = "Couldn't perform the update check, retry later.";
				strMessage += Environment.NewLine + Environment.NewLine + e.Message;
				MessageBox.Show(this, strMessage, "Update check", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void  ToggleEndorsement()
		{
			tsbToggleEndorse.Enabled = false;
			bool booCurrentState = false;

			try
			{
				IMod modMod = GetSelectedMod();
				booCurrentState = modMod.IsEndorsed;
				ViewModel.ToggleModEndorsement(modMod);
			}
			catch (Exception e)
			{
				MessageBox.Show(this, String.Format("Unable to {0} this file:", booCurrentState ? "endorse" : "unendorse") + Environment.NewLine + e.Message, "Endorsement Error:", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
			lvwMods.ItemCheck += new ItemCheckEventHandler(lvwMods_ItemCheck);
			lvwMods.ItemActivate += new EventHandler(lvwMods_ItemActivate);
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
				m_vmlViewModel.ActiveMods.CollectionChanged -= ActiveMods_CollectionChanged;
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
		}

		/// <summary>
		/// Retruns the mod that is currently selected in the view.
		/// </summary>
		/// <returns>The mod that is currently selected in the view, or
		/// <c>null</c> if no mod is selected.</returns>
		private IMod GetSelectedMod()
		{
			if ((lvwMods.Visible && (lvwMods.SelectedItems.Count == 0)) || (clwCategoryView.Visible && (clwCategoryView.SelectedIndices.Count == 0)))
				return null;

			if (lvwMods.Visible)
				return (IMod)lvwMods.SelectedItems[0].Tag;
			else
				return (IMod)clwCategoryView.GetSelectedItem.Tag;
		}

		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		protected void SetCommandExecutableStatus()
		{
			if (((lvwMods.SelectedItems.Count > 0) && lvwMods.Visible) || ((clwCategoryView.SelectedIndices.Count > 0) && clwCategoryView.Visible && (clwCategoryView.GetSelectedItem.Tag.GetType() != typeof(ModCategory))))
			{
				if (lvwMods.Visible)
					ViewModel.DeactivateModCommand.CanExecute = ViewModel.ActiveMods.Contains((IMod)lvwMods.SelectedItems[0].Tag);
				else if (clwCategoryView.Visible)
					ViewModel.DeactivateModCommand.CanExecute = ViewModel.ActiveMods.Contains((IMod)clwCategoryView.GetSelectedItem.Tag);

				ViewModel.ActivateModCommand.CanExecute = !ViewModel.DeactivateModCommand.CanExecute;
				ViewModel.DeleteModCommand.CanExecute = true;
				ViewModel.TagModCommand.CanExecute = !ViewModel.OfflineMode;
				tsbToggleEndorse.Enabled = !ViewModel.OfflineMode;
				tsbToggleEndorse.Image = GetSelectedMod().IsEndorsed ? Properties.Resources.unendorsed : Properties.Resources.endorsed;
			}
			else
			{
				ViewModel.ActivateModCommand.CanExecute = false;
				ViewModel.DeactivateModCommand.CanExecute = false;
				ViewModel.DeleteModCommand.CanExecute = false;
				ViewModel.TagModCommand.CanExecute = false;
				tsbToggleEndorse.Enabled = false;
				tsbToggleEndorse.Image = Properties.Resources.unendorsed;
			}
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
				if (e.Success)
					MessageBox.Show(this, e.Message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
				else
					MessageBox.Show(this, e.Message, "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
			m_booDisableSummary = false;
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

		#region Category Management

		/// <summary>
		/// Loads and initializes the CategoryListView control.
		/// </summary>
		private void LoadCategoryView()
		{
			if (clwCategoryView.Tag == null)
			{
				clwCategoryView.Setup(lvwMods, ViewModel.CategoryManager);

				this.clwCategoryView.SelectedIndexChanged += delegate(object sender, EventArgs e)
				{
					SetCommandExecutableStatus();
				};

				// Enables installing/uninstalling mods using the double click
				this.clwCategoryView.CellClick += delegate(object sender, BrightIdeasSoftware.CellClickEventArgs e)
				{
					if ((e.ClickCount == 2) && (e.Item != null))
					{
						try
						{
							IMod modMod = (IMod)((ListViewItem)e.Item.RowObject).Tag;
							if (modMod != null)
							{
								if (((ListViewItem)e.Item.RowObject).Checked)
									ViewModel.DeactivateModCommand.Execute(modMod);
								else
									ViewModel.ActivateModCommand.Execute(modMod);
							}
						}
						catch
						{
						}
					}
				};

				// Enables removing categories or mods using the DEL key
				this.clwCategoryView.KeyUp += delegate(object sender, KeyEventArgs e)
				{
					if (e.KeyData == Keys.Delete)
					{
						object objTag = clwCategoryView.GetSelectedItem.Tag;

						if (objTag.GetType() == typeof(ModCategory))
						{
							clwCategoryView.RemoveCategory((IModCategory)objTag);
						}
						else
						{
							try
							{
								IMod modMod = (IMod)objTag;
								ViewModel.DeleteMod(modMod);
								clwCategoryView.RebuildAll(true);
							}
							catch
							{
							}
						}
					}
				};

				clwCategoryView.LoadData();
			}
		}

		/// <summary>
		/// Handles the <see cref="CategoryListView.CellToolTipShowing"/>.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="BrightIdeasSoftware.ToolTipShowingEventArgs"/> describing the event arguments.</param>
		private void CategoryListView_CellToolTipShowing(object sender, BrightIdeasSoftware.ToolTipShowingEventArgs e)
		{
			if (e.Column.Text == "Latest Version")
			{
				IMod modMod = (IMod)((ListViewItem)e.Item.RowObject).Tag;
				if (!modMod.IsMatchingVersion())
				{
					e.AutoPopDelay = 10000;
					e.Title = "New Version Available Online";
					e.StandardIcon = BrightIdeasSoftware.ToolTipControl.StandardIcons.Warning;
					e.Text = "There's a new mod version available on the Nexus,\r\nclick on the version number to access the mod page in your default browser.";
					e.IsBalloon = true;
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
			if ((clwCategoryView.GetSelectedMod != null) && (sender != null))
			{
				IMod modMod = clwCategoryView.GetSelectedMod;
				ModCategory mctOldCategory = (ModCategory)ViewModel.CategoryManager.FindCategory(modMod.CustomCategoryId >= 0 ? modMod.CustomCategoryId : modMod.CategoryId);
				IModCategory imcNewCategory = (IModCategory)sender;

				ViewModel.SwitchModCategory(modMod, imcNewCategory.Id);
				if (clwCategoryView.RefreshData(new ModCategory(imcNewCategory)))
					clwCategoryView.AddData(imcNewCategory, false);
				if (clwCategoryView.GetCategoryModCount(mctOldCategory) == 0)
					clwCategoryView.RemoveData(mctOldCategory);
				clwCategoryView.RebuildAll(true);
			}
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
				bool booShowUnassigned = false;
				if (clwCategoryView.GetCategoryModCount(new ModCategory()) == 0)
					if (clwCategoryView.GetCategoryModCount((IModCategory)sender) > 0)
						booShowUnassigned = true;

				ViewModel.SwitchModsToUnassigned((IModCategory)sender);
				clwCategoryView.RebuildAll(true);

				if (booShowUnassigned)
					clwCategoryView.AddData(new ModCategory(), false);
			}
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the reset categories
		/// to repository default button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void resetDefaultCategories_Click(object sender, EventArgs e)
		{
			if (ViewModel.ResetDefaultCategories())
			{
				clwCategoryView.LoadData();
				clwCategoryView.SetupContextMenu();
				clwCategoryView.RebuildAll(true);
			}
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
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tsbSwitchCategory_Click(object sender, EventArgs e)
		{
			clwCategoryView.Visible = !clwCategoryView.Visible;
			lvwMods.Visible = !lvwMods.Visible;
			tsbResetCategories.Enabled = clwCategoryView.Visible;
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void collapseAllCategories_Click(object sender, EventArgs e)
		{
			clwCategoryView.CollapseAll();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void expandAllCategories_Click(object sender, EventArgs e)
		{
			clwCategoryView.ExpandAll();
		}

		/// <summary>
		/// Handles the <see cref="ToolStripItem.Click"/> event of the add new
		/// category button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void toggleHiddenCategories_Click(object sender, EventArgs e)
		{
			clwCategoryView.ShowHiddenCategories = !clwCategoryView.ShowHiddenCategories;
			clwCategoryView.LoadData();
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
				ListViewItem lviItem = (ListViewItem)e.ListViewItem.RowObject;

				if (lviItem.Tag.GetType() == typeof(ModCategory))
				{
					ModCategory mctUpdatedCategory = (ModCategory)lviItem.Tag;
					string strOldValue = mctUpdatedCategory.CategoryName;
					mctUpdatedCategory.CategoryName = strValue;
					mctUpdatedCategory.CategoryPath = strValue;
					ViewModel.CategoryManager.UpdateCategory();
					clwCategoryView.UpdateData(mctUpdatedCategory, strOldValue);
				}
				else
				{
					lock (lvwMods)
						ViewModel.UpdateModName((IMod)lviItem.Tag, strValue);
					clwCategoryView.RebuildAll(true);
				}
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
			if (!String.IsNullOrEmpty(strValue))
			{
				ListViewItem lviItem = (ListViewItem)clwCategoryView.SelectedItem.RowObject;

				if (lviItem.Tag.GetType() == typeof(ModCategory))
				{
					ModCategory mctUpdatedCategory = (ModCategory)lviItem.Tag;
					string strOldValue = mctUpdatedCategory.CategoryName;
					mctUpdatedCategory.CategoryName = strValue;
					mctUpdatedCategory.CategoryPath = strValue;
					ViewModel.CategoryManager.UpdateCategory();
					clwCategoryView.UpdateData(mctUpdatedCategory, strOldValue);
				}
				else
				{
					lock (clwCategoryView)
						ViewModel.UpdateModName((IMod)lviItem.Tag, strValue);
					clwCategoryView.RebuildAll(true);
				}
			}

			e.CancelEdit = true;
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
				clwCategoryView.RebuildAll(true);
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
				clwCategoryView.SetupContextMenu();
				clwCategoryView.RebuildAll(true);
			}
		}

		#endregion

		#region Mod Management

		#region Mod Addition

		/// <summary>
		/// Adds the given mod to the view's list. If the mod already exists in the list,
		/// it is replaced with the new version.
		/// </summary>
		/// <param name="p_modAdded">The mod to add to the view's list.</param>
		protected void AddModToList(IMod p_modAdded)
		{
			ListViewItem lviMod = null;
			lock (lvwMods)
			{
				if (lvwMods.Items.ContainsKey(p_modAdded.Filename.ToLowerInvariant()))
					lviMod = lvwMods.Items[p_modAdded.Filename.ToLowerInvariant()];
				else
				{
					lviMod = new ListViewItem();
					for (Int32 i = 1; i < lvwMods.Columns.Count; i++)
					{
						lviMod.SubItems.Add(new ListViewItem.ListViewSubItem());
						lviMod.SubItems[i].Name = lvwMods.Columns[i].Name;
					}
					lviMod.Name = p_modAdded.Filename.ToLowerInvariant();
					lviMod.UseItemStyleForSubItems = false;
					lvwMods.Items.Add(lviMod);
				}
			}
			lviMod.Tag = p_modAdded;
			lviMod.Text = p_modAdded.ModName;
			lviMod.SubItems[clmCategory.Name].Text = ViewModel.CategoryManager.FindCategory(p_modAdded.CustomCategoryId >= 0 ? p_modAdded.CustomCategoryId : p_modAdded.CategoryId).CategoryName;
			lviMod.SubItems[clmVersion.Name].Text = p_modAdded.HumanReadableVersion;
			lviMod.SubItems[clmWebVersion.Name].Text = p_modAdded.HumanReadableVersion;
			UpdateNewestVersion(lviMod);
			lviMod.SubItems[clmAuthor.Name].Text = p_modAdded.Author;
			lviMod.SubItems[clmInstallDate.Name].Text = p_modAdded.InstallDate;
			p_modAdded.PropertyChanged -= new PropertyChangedEventHandler(Mod_PropertyChanged);
			p_modAdded.PropertyChanged += new PropertyChangedEventHandler(Mod_PropertyChanged);
			SetModActivationCheck(lviMod, ViewModel.ActiveMods.Contains(p_modAdded));
			lvwMods.Sort();
		}

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
				ViewModel.AddModCommand.Execute(ofdChooseMod.FileName);
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
			string strURL = PromptDialog.ShowDialog(this, "NMM URL: (eg. nxm://Skyrim/mods/193/files/8998)", "Choose URL", strDefault, @"nxm://\w+/mods/\d+/files/\d+", "Must be a Nexus Mod URL.");
			if (!String.IsNullOrEmpty(strURL))
				ViewModel.AddModCommand.Execute(strURL);
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
		/// Removes the given mod from the view's list.
		/// </summary>
		/// <param name="p_modRemoved">The mod to remove from the view's list.</param>
		protected void RemoveModFromList(IMod p_modRemoved)
		{
			lock (lvwMods)
				lvwMods.Items.RemoveByKey(p_modRemoved.Filename.ToLowerInvariant());
		}

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
		private bool ConfirmModFileDeletion(IMod p_modMod)
		{
			if (InvokeRequired)
			{
				bool booResult = false;
				Invoke((MethodInvoker)(() => booResult = ConfirmModFileDeletion(p_modMod)));
				return booResult;
			}
			return MessageBox.Show(this, String.Format("Are you sure you want to delete {0}?", p_modMod.ModName), "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
		}

		#endregion

		#region Mod Updates

		/// <summary>
		/// Updates the latest mod version to reflect the newest available information.
		/// </summary>
		/// <param name="p_lviMod">The list item to update.</param>
		private void UpdateNewestVersion(ListViewItem p_lviMod)
		{
			IMod modMod = (IMod)p_lviMod.Tag;
			AutoUpdater.UpdateInfo uifNewModInfo = ViewModel.NewestModInfo.Find(x => x.Mod == modMod);
			ListViewItem.ListViewSubItem lsiWebVersion = p_lviMod.SubItems[clmWebVersion.Name];
			lvwMods.ClearMessage(lsiWebVersion);
			if ((uifNewModInfo != null) && (uifNewModInfo.NewestInfo != null))
			{
				lsiWebVersion.Text = uifNewModInfo.NewestInfo.HumanReadableVersion;
				lsiWebVersion.Font = new Font(p_lviMod.SubItems[clmWebVersion.Name].Font, FontStyle.Regular);
				if (uifNewModInfo.NewestInfo.Website != null)
				{
					if ((!String.IsNullOrEmpty(uifNewModInfo.NewestInfo.LastKnownVersion) && !uifNewModInfo.IsMatchingVersion(uifNewModInfo.NewestInfo.LastKnownVersion)) || !uifNewModInfo.IsMatchingVersion(modMod.HumanReadableVersion))
						lvwMods.SetMessage(lsiWebVersion, "Update available", Properties.Resources.dialog_warning_4);
					lsiWebVersion.ForeColor = Color.FromKnownColor(KnownColor.HotTrack);
				}
				else
					lsiWebVersion.ForeColor = lvwMods.ForeColor;

				lvwMods.SetMessage(p_lviMod.SubItems[clmEndorsement.Name], uifNewModInfo.NewestInfo.IsEndorsed ? "Endorsed" : "Unendorsed", uifNewModInfo.NewestInfo.IsEndorsed ? Properties.Resources.endorsed_small : Properties.Resources.unendorsed_small);
				lsiWebVersion.Tag = uifNewModInfo.NewestInfo;
			}
			else
			{
				lsiWebVersion.Text = String.IsNullOrEmpty(modMod.LastKnownVersion) ? modMod.HumanReadableVersion : modMod.LastKnownVersion;
				lsiWebVersion.Font = new Font(p_lviMod.SubItems[clmWebVersion.Name].Font, FontStyle.Regular);

				if (modMod.Website != null)
				{
					if (!modMod.IsMatchingVersion())
						lvwMods.SetMessage(lsiWebVersion, "Update available", Properties.Resources.dialog_warning_4);
					lsiWebVersion.ForeColor = Color.FromKnownColor(KnownColor.HotTrack);
					lvwMods.SetMessage(p_lviMod.SubItems[clmEndorsement.Name], modMod.IsEndorsed ? "Endorsed" : "Unendorsed", modMod.IsEndorsed ? Properties.Resources.endorsed_small : Properties.Resources.unendorsed_small);
					lsiWebVersion.Tag = modMod;
				}
				else
				{
					lsiWebVersion.ForeColor = lvwMods.ForeColor;
					lsiWebVersion.Tag = null;
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// newest mod info list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to the mewest available mod info.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void NewestModInfo_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (lvwMods.InvokeRequired)
			{
				lvwMods.Invoke((Action<object, NotifyCollectionChangedEventArgs>)NewestModInfo_CollectionChanged, sender, e);
				return;
			}
			lock (lvwMods)
			{
				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
					case NotifyCollectionChangedAction.Replace:
						foreach (AutoUpdater.UpdateInfo ui in e.NewItems)
							foreach (ListViewItem lviSearch in lvwMods.Items)
								if (lviSearch.Tag == ui.Mod)
								{
									UpdateNewestVersion(lviSearch);
									break;
								}
						break;
					case NotifyCollectionChangedAction.Remove:
					case NotifyCollectionChangedAction.Reset:
						foreach (AutoUpdater.UpdateInfo ui in e.OldItems)
							foreach (ListViewItem lviSearch in lvwMods.Items)
								if (lviSearch.Tag == ui.Mod)
								{
									UpdateNewestVersion(lviSearch);
									break;
								}
						break;
				}
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.MouseMove"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This fakes the web version column into looking like a hyperlink.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="MouseEventArgs"/> describing the event arguments.</param>
		private void lvwMods_MouseMove(object sender, MouseEventArgs e)
		{
			ListViewHitTestInfo htiInfo = lvwMods.HitTest(e.Location);
			ListViewItem.ListViewSubItem lsiSubItem = htiInfo.SubItem;
			if ((lsiSubItem == null) || (lsiSubItem == m_lsiLastSelectedWebVersion))
				return;
			if (m_lsiLastSelectedWebVersion != null)
			{
				m_lsiLastSelectedWebVersion.Font = new Font(m_lsiLastSelectedWebVersion.Font, FontStyle.Regular);
				m_lsiLastSelectedWebVersion = null;
			}
			lvwMods.Cursor = Cursors.Default;
			if ((lsiSubItem == htiInfo.Item.SubItems[clmWebVersion.Name]) && (lsiSubItem.Tag != null))
			{
				IModInfo mifNewInfo = (IModInfo)lsiSubItem.Tag;
				if (mifNewInfo.Website == null)
					return;
				m_lsiLastSelectedWebVersion = lsiSubItem;
				lsiSubItem.Font = new Font(lsiSubItem.Font, FontStyle.Underline);
				lvwMods.Cursor = Cursors.Hand;
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.MouseDown"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This opens the website for the newest version of the mod.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="MouseEventArgs"/> describing the event arguments.</param>
		private void lvwMods_MouseDown(object sender, MouseEventArgs e)
		{
			ListViewHitTestInfo htiInfo = lvwMods.HitTest(e.Location);
			ListViewItem.ListViewSubItem lsiSubItem = htiInfo.SubItem;
			if ((e.Button != MouseButtons.Left) || (lsiSubItem == null) || (lsiSubItem != htiInfo.Item.SubItems[clmWebVersion.Name]) || (lsiSubItem.Tag == null))
				return;
			IModInfo mifNewInfo = (IModInfo)lsiSubItem.Tag;
			if (mifNewInfo.Website == null)
				return;
			try
			{
				System.Diagnostics.Process.Start(mifNewInfo.Website.ToString());
			}
			catch (Win32Exception)
			{
				MessageBox.Show(this, "Cannot find programme to open: " + mifNewInfo.Website, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Trace.WriteLine("Cannot find programme to open: " + mifNewInfo.Website);
			}
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
			if (lvwMods.InvokeRequired)
			{
				lvwMods.Invoke((MethodInvoker)(() => ManagedMods_CollectionChanged(sender, e)));
				return;
			}

			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					foreach (IMod modAdded in e.NewItems)
					{
						AddModToList(modAdded);
					}
					break;
				case NotifyCollectionChangedAction.Remove:
					foreach (IMod modRemoved in e.OldItems)
					{
						RemoveModFromList(modRemoved);
					}
					break;
			}

			clwCategoryView.RebuildAll(true);
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
			if (!m_booDisableSummary) 
				UpdateSummary((IMod)sender);
			AddModToList((IMod)sender);
		}

		#endregion

		#region Mod Activation

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
			m_booSettingModActiveCheck = true;
			p_lviPlugin.Checked = p_booIsActive;
			m_booSettingModActiveCheck = false;
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
		/// Show the given mod as being either active or inactive in the view.
		/// </summary>
		/// <param name="p_modActivated">The mod whose active status has changed.</param>
		/// <param name="p_booIsActive">Whether the given mod is active.</param>
		protected void SetModActive(IMod p_modActivated, bool p_booIsActive)
		{
			ListViewItem lviMod = null;
			lock (lvwMods)
			{
				if (!lvwMods.Items.ContainsKey(p_modActivated.Filename.ToLowerInvariant()))
					return;
				lviMod = lvwMods.Items[p_modActivated.Filename.ToLowerInvariant()];
			}
			SetModActivationCheck(lviMod, p_booIsActive);
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
			if (lvwMods.InvokeRequired)
			{
				lvwMods.Invoke((MethodInvoker)(() => ActiveMods_CollectionChanged(sender, e)));
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					foreach (IMod modAdded in e.NewItems)
						SetModActive(modAdded, true);
					break;
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
					foreach (IMod modAdded in e.OldItems)
						SetModActive(modAdded, false);
					break;
			}
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

		/// <summary>
		/// Handles the <see cref="ListView.ItemCheck"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This prevents manual toggling of the check boxes.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ItemCheckEventArgs"/> describing the event arguments.</param>
		private void lvwMods_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			lock (lvwMods)
			{
				e.NewValue = ViewModel.ActiveMods.Contains((IMod)lvwMods.Items[e.Index].Tag) ? CheckState.Checked : CheckState.Unchecked;
				if (e.NewValue == CheckState.Checked)
				{
					if (String.IsNullOrEmpty(lvwMods.Items[e.Index].SubItems[clmInstallDate.Name].Text))
						lvwMods.Items[e.Index].SubItems[clmInstallDate.Name].Text = DateTime.Now.ToString();
				}
				else if (e.NewValue == CheckState.Unchecked)
					if (!String.IsNullOrEmpty(lvwMods.Items[e.Index].SubItems[clmInstallDate.Name].Text))
						lvwMods.Items[e.Index].SubItems[clmInstallDate.Name].Text = String.Empty;
			}
		}

		/// <summary>
		/// Handles the <see cref="ListView.ItemActivate"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This event is used instead of <see cref="ListView.ItemChecked"/> or
		/// <see cref="ListView.ItemCheck"/> as it is more deliberate (on the part of the user).
		/// It is too easy to accidentally check or uncheck a checkbox, which could be annoying
		/// for the user (accidentally deactivating a mod that take 30 minutes to activate would
		/// cause angst). This is unlike the plugin list, where activating/deactivating plugins
		/// is cheap, so the simplier events make more sense.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ItemCheckEventArgs"/> describing the event arguments.</param>
		private void lvwMods_ItemActivate(object sender, EventArgs e)
		{
			if (m_booSettingModActiveCheck)
				return;
			if (lvwMods.SelectedItems.Count == 0)
				return;
			ListViewItem lviMod = lvwMods.SelectedItems[0];
			if (!lviMod.Checked)
				ViewModel.ActivateModCommand.Execute((IMod)lviMod.Tag);
			else
				ViewModel.DeactivateModCommand.Execute((IMod)lviMod.Tag);	
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
			new ModTaggerForm(e.Argument).ShowDialog(this);
		}

		/// <summary>
		/// Handles the <see cref="ListView.AfterLabelEdit"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This updates the name of the mod.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="LabelEditEventArgs"/> describing the event arguments.</param>
		private void lvwMods_AfterLabelEdit(object sender, LabelEditEventArgs e)
		{
			if (String.IsNullOrEmpty(e.Label))
				return;
			lock (lvwMods)
				ViewModel.UpdateModName((IMod)lvwMods.Items[e.Item].Tag, e.Label);
			e.CancelEdit = true;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This displays the screenshot and description of the selected mod.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwMods_SelectedIndexChanged(object sender, EventArgs e)
		{
			if ((lvwMods.SelectedItems.Count > 0) && !m_booDisableSummary)
				UpdateSummary((IMod)lvwMods.SelectedItems[0].Tag);
			else
				UpdateSummary(null);
			SetCommandExecutableStatus();
		}

		/// <summary>
		/// Handles the <see cref="Control.FontChanged"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This updates the subitems to the new font.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwMods_FontChanged(object sender, EventArgs e)
		{
			foreach (ListViewItem lviItem in lvwMods.Items)
			{
				if (lviItem.Font != lvwMods.Font)
					lviItem.Font = new Font(lvwMods.Font.FontFamily, lviItem.Font.Size, lviItem.Font.Style);
				foreach (ListViewItem.ListViewSubItem lsiSubItem in lviItem.SubItems)
					if (lsiSubItem.Font != lvwMods.Font)
						lsiSubItem.Font = new Font(lvwMods.Font.FontFamily, lsiSubItem.Font.Size, lsiSubItem.Font.Style);
			}
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
			if (lvwMods.Columns.Count == 0)
				return;
			m_booResizing = true;
			Int32 intFixedWidth = 0;
			for (Int32 i = 0; i < lvwMods.Columns.Count; i++)
				if (lvwMods.Columns[i] != clmModName)
					intFixedWidth += lvwMods.Columns[i].Width;
			clmModName.Width = lvwMods.ClientSize.Width - intFixedWidth;
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
		private void lvwMods_Resize(object sender, EventArgs e)
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
		private void lvwMods_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			if (m_booResizing)
				return;
			ColumnHeader clmThis = lvwMods.Columns[e.ColumnIndex];
			ColumnHeader clmOther = null;
			if (e.ColumnIndex == lvwMods.Columns.Count - 1)
				clmOther = lvwMods.Columns[e.ColumnIndex - 1];
			else
				clmOther = lvwMods.Columns[e.ColumnIndex + 1];
			m_booResizing = true;
			clmOther.Width += (clmThis.Width - e.NewWidth);
			m_booResizing = false;
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
	}
}
