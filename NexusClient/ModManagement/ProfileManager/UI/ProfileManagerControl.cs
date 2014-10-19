using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Nexus.UI.Controls;
using Nexus.Client.Settings;

namespace Nexus.Client.ModManagement.UI
{
	/// <summary>
	/// The view that exposes mod management functionality.
	/// </summary>
	public partial class ProfileManagerControl : ManagedFontDockContent
	{
		private ProfileManagerVM m_vmlViewModel = null;
		private List<IBackgroundTaskSet> lstRunningTaskSets = new List<IBackgroundTaskSet>();
		private bool m_booResizing = false;
		private Timer m_tmrColumnSizer = new Timer();
		BindingSource bs = new BindingSource();

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ProfileManagerVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				m_vmlViewModel.ProfileManager.ModProfiles.CollectionChanged += new NotifyCollectionChangedEventHandler(ModProfiles_CollectionChanged);
				//m_vmlViewModel.UpdatingCategory += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_UpdatingCategory);
				//m_vmlViewModel.UpdatingMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_UpdatingMods);
				//m_vmlViewModel.TogglingAllWarning += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_TogglingAllWarning);
				//m_vmlViewModel.ReadMeManagerSetup += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ReadMeManagerSetup);
				//m_vmlViewModel.AddingMod += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_AddingMod);
				//m_vmlViewModel.DeletingMod += new EventHandler<EventArgs<IBackgroundTaskSet>>(ViewModel_DeletingMod);
				//m_vmlViewModel.ActivatingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_ActivatingMultipleMods);
				//m_vmlViewModel.DisablingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_DeactivatingMultipleMods);
				//m_vmlViewModel.DeactivatingMultipleMods += new EventHandler<EventArgs<IBackgroundTask>>(ViewModel_DeactivatingMultipleMods);
				//m_vmlViewModel.ChangingModActivation += new EventHandler<EventArgs<IBackgroundTaskSet>>(ViewModel_ChangingModActivation);
				//m_vmlViewModel.TaggingMod += new EventHandler<EventArgs<ModTaggerVM>>(ViewModel_TaggingMod);
				//m_vmlViewModel.ManagedMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ManagedMods_CollectionChanged);
				//m_vmlViewModel.ActiveMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveMods_CollectionChanged);
				//foreach (IMod modMod in m_vmlViewModel.ManagedMods)
				//{
				//	modMod.PropertyChanged -= new PropertyChangedEventHandler(Mod_PropertyChanged);
				//	modMod.PropertyChanged += new PropertyChangedEventHandler(Mod_PropertyChanged);
				//}


				//LoadModFormatFilter();

				//tsbAddMod.DefaultItem = tsbAddMod.DropDownItems[m_vmlViewModel.Settings.SelectedAddModCommandIndex];
				//tsbAddMod.Text = tsbAddMod.DefaultItem.Text;
				//tsbAddMod.Image = tsbAddMod.DefaultItem.Image;

				//m_vmlViewModel.ConfirmModFileDeletion = ConfirmModFileDeletion;
				//m_vmlViewModel.ConfirmModFileOverwrite = ConfirmModFileOverwrite;
				//m_vmlViewModel.ConfirmItemOverwrite = ConfirmItemOverwrite;
				//m_vmlViewModel.ConfirmModUpgrade = ConfirmModUpgrade;

				//new ToolStripItemCommandBinding<IMod>(tsbDeleteMod, m_vmlViewModel.DeleteModCommand, GetSelectedMod);
				//new ToolStripItemCommandBinding<IMod>(tsbActivate, m_vmlViewModel.ActivateModCommand, GetSelectedMod);
				//new ToolStripItemCommandBinding<IMod>(tsbDeactivate, m_vmlViewModel.DisableModCommand, GetSelectedMod);
				//new ToolStripItemCommandBinding<IMod>(tsbTagMod, m_vmlViewModel.TagModCommand, GetSelectedMod);
				//Command cmdToggleEndorsement = new Command("Toggle Mod Endorsement", "Toggles the mod endorsement.", ToggleEndorsement);
				//new ToolStripItemCommandBinding(tsbToggleEndorse, cmdToggleEndorsement);
				//Command cmdCheckModVersions = new Command("Check Mod Versions", "Checks for new mod versions.", CheckModVersions);
				//new ToolStripItemCommandBinding(tsbCheckModVersions, cmdCheckModVersions);
				//ViewModel.DeleteModCommand.CanExecute = false;
				//ViewModel.ActivateModCommand.CanExecute = false;
				//ViewModel.DisableModCommand.CanExecute = false;
				//ViewModel.TagModCommand.CanExecute = false;
				//ViewModel.ParentForm = this;

				LoadMetrics();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public ProfileManagerControl()
		{
			this.Load += new EventHandler(ProfileManagerControl_Load);
			InitializeComponent();

			//clwCategoryView.BeforeSorting += new EventHandler<BrightIdeasSoftware.BeforeSortingEventArgs>(clwCategoryView_BeforeSorting);
			//clwCategoryView.ColumnClick += new ColumnClickEventHandler(clwCategoryView_ColumnClick);
			ofdChooseProfile.Filter = "Profile Zip (*.zip)|*.zip";

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
			}
		}

		/// <summary>
		/// Loads the control's saved metrics.
		/// </summary>
		protected void LoadMetrics()
		{
			if (ViewModel != null)
			{
				ViewModel.Settings.SplitterSizes.LoadSplitterSizes("ProfileManager", sptProfiles);
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("ProfileManager", plwProfiles);

				SizeColumnsToFit();
			}
		}

		#endregion

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
		private void ProfileManagerControl_Load(object sender, EventArgs e)
		{
			SetupProfiles();
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
				//m_vmlViewModel.AddingMod -= ViewModel_AddingMod;
				//m_vmlViewModel.ChangingModActivation -= ViewModel_ChangingModActivation;
				//m_vmlViewModel.ManagedMods.CollectionChanged -= ManagedMods_CollectionChanged;
			}
		}

		private void SetupProfiles()
		{
			this.lsbProfiles.SelectionMode = SelectionMode.One;
			this.lsbProfiles.DataSource = null;
			bs.DataSource = null;
			bs.DataSource = ViewModel.ProfileManager.ModProfiles;
			this.lsbProfiles.DataSource = bs;
			this.lsbProfiles.DisplayMember = "Name";

			

			ListBoxContextMenu.Items.Clear();
			ListBoxContextMenu.Items.Add("Remove selected profile", new Bitmap(Properties.Resources.dialog_cancel_4_16, 16, 16), new EventHandler(ListBoxContextMenu_RemoveSelected));
			//ListBoxContextMenu.Items.Add("Clone selected profile", new Bitmap(Properties.Resources.edit_copy_6, 16, 16), new EventHandler(ListBoxContextMenu_CloneSelected));

			this.plwProfiles.Setup(ViewModel.ManagedMods, ViewModel.ModRepository, ViewModel.ProfileManager, ViewModel.Settings);
		}

		private void LoadProfile(IModProfile p_impModProfile)
		{
			if ((p_impModProfile.ModList == null) || (p_impModProfile.ModFileList == null))
				ViewModel.ProfileManager.LoadProfileFileList(p_impModProfile);

			this.plwProfiles.LoadData(p_impModProfile.ModList, p_impModProfile.ModFileList);
		}

		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		public void SetCommandExecutableStatus()
		{
			//if (((clwCategoryView.SelectedIndices.Count > 0) && clwCategoryView.Visible && (clwCategoryView.GetSelectedItem.GetType() != typeof(ModCategory))))
			//{
			//	if (clwCategoryView.Visible)
			//		ViewModel.DisableModCommand.CanExecute = ViewModel.VirtualModActivator.ActiveModList.Contains(GetSelectedMod().Filename);

			//	ViewModel.ActivateModCommand.CanExecute = !ViewModel.DisableModCommand.CanExecute;

			//	ViewModel.DeleteModCommand.CanExecute = true;
			//	ViewModel.TagModCommand.CanExecute = true;
			//	tsbToggleEndorse.Enabled = true;
			//	tsbToggleEndorse.Image = Properties.Resources.thumbsup;
			//}
			//else
			//{
			//	ViewModel.ActivateModCommand.CanExecute = false;
			//	ViewModel.DisableModCommand.CanExecute = false;
			//	ViewModel.TagModCommand.CanExecute = false;
			//	tsbToggleEndorse.Enabled = false;
			//	tsbToggleEndorse.Image = Properties.Resources.thumbsup;
			//}

			//this.tsbDeactivate.Visible = ViewModel.DisableModCommand.CanExecute;
			//this.tsbActivate.Visible = ViewModel.ActivateModCommand.CanExecute;
		}

		#endregion

		#region ListBox

		private void ModProfiles_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (lsbProfiles.DataSource != null)
			{
				SetupProfiles();
			}			
		}
		
		/// <summary>
		/// Handles the ListBoxContextMenu.RemoveSelected event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void ListBoxContextMenu_RemoveSelected(object sender, EventArgs e)
		{
			if (lsbProfiles.SelectedItem.GetType() == typeof(ModProfile))
				ViewModel.ProfileManager.RemoveProfile((IModProfile)lsbProfiles.SelectedItem);
		}

		/// <summary>
		/// Handles the ListBoxContextMenu.CloneSelected event.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="System.EventArgs"/> describing the event arguments.</param>
		private void ListBoxContextMenu_CloneSelected(object sender, EventArgs e)
		{
			//if (lsbProfiles.SelectedItem.GetType() == typeof(IModProfile))
			//	ViewModel.ProfileManager.
		}

		#endregion
		
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
			if (plwProfiles.Visible)
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
		private void plwProfiles_Resize(object sender, EventArgs e)
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
		private void plwProfiles_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			//if (m_booResizing)
			//	return;
			//ColumnHeader clmThis = clwCategoryView.Columns[e.ColumnIndex];
			//ColumnHeader clmOther = null;
			//if (e.ColumnIndex == clwCategoryView.Columns.Count - 1)
			//	clmOther = clwCategoryView.Columns[e.ColumnIndex - 1];
			//else
			//	clmOther = clwCategoryView.Columns[e.ColumnIndex + 1];
			//m_booResizing = true;
			//clmOther.Width += (clmThis.Width - e.NewWidth);
			//m_booResizing = false;
		}

		/// <summary>
		/// Handles the <see cref="ListView.ColumnWidthChanged"/> event of the mod list.
		/// </summary>
		/// <remarks>
		/// This saves the column width.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ColumnWidthChangedEventHandler"/> describing the event arguments.</param>
		private void plwProfiles_ColumnWidthChanged(object sender, ColumnWidthChangedEventArgs e)
		{
			if (m_booResizing)
				return;

			ViewModel.Settings.ColumnWidths.SaveColumnWidths("ProfileManager", plwProfiles);
			ViewModel.Settings.Save();
		}

		/// <summary>
		/// This resizes the columns to fill the list view.
		/// </summary>
		protected void SizeColumnsToFitClw()
		{
			if (plwProfiles.Columns.Count == 0)
				return;
			m_booResizing = true;
			plwProfiles.SizeColumnsToFit();
			m_booResizing = false;
		}

		#endregion

		private void lsbProfiles_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Right)
			{
				lsbProfiles.SelectedIndex = lsbProfiles.IndexFromPoint(e.Location);
				if (lsbProfiles.SelectedIndex != -1)
				{
					Point pntPosition = lsbProfiles.PointToClient(Cursor.Position);
					ListBoxContextMenu.Show(lsbProfiles, pntPosition, ToolStripDropDownDirection.Right);
				}
			}
		}

		private void lsbProfiles_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				if ((lsbProfiles.SelectedItem != null) && (lsbProfiles.SelectedItem.GetType() == typeof(ModProfile)))
					LoadProfile((IModProfile)lsbProfiles.SelectedItem);
			}
		}

		private void tsbImport_Click(object sender, EventArgs e)
		{
			if (ofdChooseProfile.ShowDialog() == DialogResult.OK)
				if (!ViewModel.ProfileManager.ImportProfile(ofdChooseProfile.FileName))
					MessageBox.Show("Profile import failed, please make sure this is a valid NMM profile!", "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		private void tsbExport_Click(object sender, EventArgs e)
		{
			if ((lsbProfiles.SelectedItem != null) && (lsbProfiles.SelectedItem.GetType() == typeof(ModProfile)))
			{
				if (sfdChooseProfile.ShowDialog() == DialogResult.OK)
					ViewModel.ProfileManager.ExportProfile((IModProfile)lsbProfiles.SelectedItem, sfdChooseProfile.FileName);
			}
			else
				MessageBox.Show("Select a valid profile to export from the list!", "Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}
}
