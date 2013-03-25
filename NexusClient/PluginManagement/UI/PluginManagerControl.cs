using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Nexus.Client.Commands;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Games;
using Nexus.Client.Plugins;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// A view that exposes plugin management functionality.
	/// </summary>
	public partial class PluginManagerControl : ManagedFontDockContent
	{
		private PluginManagerVM m_vmlViewModel = null;
		private bool m_booResizing = false;
		private bool m_booSettingPluginActiveCheck = false;
		private Timer m_tmrColumnSizer = new Timer();
		private bool m_booControlIsLoaded = false;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public PluginManagerVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				Trace.TraceInformation(String.Format("Loading Plugins into Plugin List: {0}", m_vmlViewModel.ManagedPlugins.Count));
				Trace.Indent();
				foreach (Plugin plgPlugin in m_vmlViewModel.ManagedPlugins)
				{
					Trace.TraceInformation(String.Format("Loading: {0} ({1})", plgPlugin, plgPlugin == null));
					AddPluginToList(plgPlugin);
				}
				Trace.Unindent();
				m_vmlViewModel.ManagedPlugins.CollectionChanged += new NotifyCollectionChangedEventHandler(ManagedPlugins_CollectionChanged);
				m_vmlViewModel.ActivePlugins.CollectionChanged += new NotifyCollectionChangedEventHandler(ActivePlugins_CollectionChanged);
				m_vmlViewModel.ExportFailed += new EventHandler<ExportFailedEventArgs>(ViewModel_ExportFailed);
				m_vmlViewModel.ExportSucceeded += new EventHandler<ExportSucceededEventArgs>(ViewModel_ExportSucceeded);
				m_vmlViewModel.ImportFailed += new EventHandler<ImportFailedEventArgs>(ViewModel_ImportFailed);
				m_vmlViewModel.ImportPartiallySucceeded += new EventHandler<ImportSucceededEventArgs>(ViewModel_ImportPartiallySucceeded);
				m_vmlViewModel.ImportSucceeded += new EventHandler<ImportSucceededEventArgs>(ViewModel_ImportSucceeded);

				new ToolStripItemCommandBinding<IEnumerable<Plugin>>(tsbMoveUp, m_vmlViewModel.MoveUpCommand, GetSelectedPlugins);
				new ToolStripItemCommandBinding<IList<Plugin>>(tsbMoveDown, m_vmlViewModel.MoveDownCommand, GetSelectedPlugins);
				Command cmdDisableAll = new Command("Disable All Plugins", "Disable all the active plugins.", DisableAllPlugins);
				new ToolStripItemCommandBinding(tsbDisableAll, cmdDisableAll);
				Command cmdEnableAll = new Command("Enable All Plugins", "Enable all the inactive plugins.", EnableAllPlugins);
				new ToolStripItemCommandBinding(tsbEnableAll, cmdEnableAll);
				new ToolStripItemCommandBinding<string>(tsmiExportToTextFile, m_vmlViewModel.ExportLoadOrderToFileCommand, GetExportToFileArgs);
				new ToolStripItemCommandBinding(tsmiExportToClipboard, m_vmlViewModel.ExportLoadOrderToClipboardCommand);
				new ToolStripItemCommandBinding<string>(tsmiImportFromTextFile, m_vmlViewModel.ImportLoadOrderFromFileCommand, GetImportFromFileArgs);
				new ToolStripItemCommandBinding(tsmiImportFromClipboard, m_vmlViewModel.ImportLoadOrderFromClipboardCommand);

				ViewModel.ActivatePluginCommand.CanExecute = false;
				ViewModel.DeactivatePluginCommand.CanExecute = false;
				ViewModel.MoveUpCommand.CanExecute = false;
				ViewModel.MoveDownCommand.CanExecute = false;
				ViewModel.ExportLoadOrderToFileCommand.CanExecute = m_vmlViewModel.CanExecuteExportCommands();
				ViewModel.ExportLoadOrderToClipboardCommand.CanExecute = m_vmlViewModel.CanExecuteExportCommands();
				ViewModel.ImportLoadOrderFromFileCommand.CanExecute = m_vmlViewModel.CanExecuteImportCommands();
				ViewModel.ImportLoadOrderFromClipboardCommand.CanExecute = m_vmlViewModel.CanExecuteImportCommands();
				LoadMetrics();
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public PluginManagerControl()
		{
			this.Load += new EventHandler(PluginManagerControl_Load);
			InitializeComponent();
			clmName.Name = "PluginName";
			clmIndexHex.Name = "IndexHex";
			clmIndex.Name = "Index";
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
			}
		}

		/// <summary>
		/// Loads the control's saved metrics.
		/// </summary>
		protected void LoadMetrics()
		{
			if (m_booControlIsLoaded && (ViewModel != null))
			{
				ViewModel.Settings.SplitterSizes.LoadSplitterSizes("pluginManager", splitContainer1);
				ViewModel.Settings.ColumnWidths.LoadColumnWidths("pluginManager", rlvPlugins);

				FindForm().FormClosing += new FormClosingEventHandler(PluginManagerControl_FormClosing);
				SizeColumnsToFit();
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
			ViewModel.Settings.SplitterSizes.SaveSplitterSizes("pluginManager", splitContainer1);
			ViewModel.Settings.ColumnWidths.SaveColumnWidths("pluginManager", rlvPlugins);
			ViewModel.Settings.Save();
		}

		#endregion

		/// <summary>
		/// Disable all plugins.
		/// </summary>
		protected void DisableAllPlugins()
		{
			if (MessageBox.Show("Do you really want to disable all active plugins?", "Confirm?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly) == DialogResult.Yes)
				foreach (ListViewItem items in rlvPlugins.Items)
				{
					if ((items.Checked) && (items.Index > 0))
						ViewModel.DeactivatePlugin((Plugin)items.Tag);
				}
		}

		/// <summary>
		/// Enable all plugins.
		/// </summary>
		protected void EnableAllPlugins()
		{
			if (MessageBox.Show("Do you really want to enable all inactive plugins?", "Confirm?", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly) == DialogResult.Yes)
				foreach (ListViewItem items in rlvPlugins.Items)
				{
					if ((items.Checked == false) && (items.Index > 0))
						ViewModel.ActivatePlugin((Plugin)items.Tag);
				}
		}

		#region Binding

		/// <summary>
		/// Handles the <see cref="UserControl.Load"/> event.
		/// </summary>
		/// <remarks>
		/// This wires up the <see cref="ListView.ItemCheck"/> event of the plugin list. We need to
		/// wire it up after the control has loaded to that plugin activation status isn't
		/// superfluously changed as items are first added to the list. A simple boolean flag won't
		/// work, as items can be added before the control is loaded, which delays the firing of
		/// the <see cref="ListView.ItemCheck"/> event until after the control is loaded, at which
		/// point the flag would have been reset.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event.</param>
		private void PluginManagerControl_Load(object sender, EventArgs e)
		{
			rlvPlugins.ItemCheck += new ItemCheckEventHandler(rlvPlugins_ItemCheck);
		}

		/// <summary>
		/// Retruns the plugin that is currently selected in the view.
		/// </summary>
		/// <returns>The plugin that is currently selected in the view, or
		/// <c>null</c> if no plugin is selected.</returns>
		private Plugin GetSelectedPlugin()
		{
			if (rlvPlugins.SelectedItems.Count == 0)
				return null;
			return (Plugin)rlvPlugins.SelectedItems[0].Tag;
		}

		/// <summary>
		/// Retruns the plugins that are currently selected in the view.
		/// </summary>
		/// <returns>The plugins that are currently selected in the view, or
		/// <c>null</c> if no plugin is selected.</returns>
		private IList<Plugin> GetSelectedPlugins()
		{
			if (rlvPlugins.SelectedItems.Count == 0)
				return null;
			List<Plugin> lstPlugins = new List<Plugin>();
			foreach (ListViewItem lviPlugin in rlvPlugins.SelectedItems)
				lstPlugins.Add((Plugin)lviPlugin.Tag);
			return lstPlugins;
		}

		/// <summary>
		/// Sets the executable status of the commands.
		/// </summary>
		protected void SetCommandExecutableStatus()
		{
			if (rlvPlugins.SelectedItems.Count > 0)
			{
				ViewModel.DeactivatePluginCommand.CanExecute = ViewModel.ActivePlugins.Contains(GetSelectedPlugin());
				ViewModel.ActivatePluginCommand.CanExecute = !ViewModel.DeactivatePluginCommand.CanExecute;
				ViewModel.MoveUpCommand.CanExecute = ViewModel.CanMovePluginUp(GetSelectedPlugin());
				ViewModel.MoveDownCommand.CanExecute = ViewModel.CanMovePluginsDown(GetSelectedPlugins());
			}
			else
			{
				ViewModel.ActivatePluginCommand.CanExecute = false;
				ViewModel.DeactivatePluginCommand.CanExecute = false;
				ViewModel.MoveUpCommand.CanExecute = false;
				ViewModel.MoveDownCommand.CanExecute = false;
			}

			ViewModel.ExportLoadOrderToFileCommand.CanExecute = ViewModel.CanExecuteExportCommands();
			ViewModel.ExportLoadOrderToClipboardCommand.CanExecute = ViewModel.CanExecuteExportCommands();
			ViewModel.ImportLoadOrderFromFileCommand.CanExecute = ViewModel.CanExecuteImportCommands();
			ViewModel.ImportLoadOrderFromClipboardCommand.CanExecute = ViewModel.CanExecuteImportCommands();
		}

		#endregion

		/// <summary>
		/// Refreshes the displayed indicies of the listed plugins.
		/// </summary>
		protected void RefreshPluginIndices()
		{
			Int32 intIndex = 0;
			foreach (ListViewItem lviPlugin in rlvPlugins.Items)
			{
				if (ViewModel.ActivePlugins.Contains((Plugin)lviPlugin.Tag))
				{
					lviPlugin.SubItems[clmIndexHex.Name].Text = String.Format("{0:x2}", intIndex++).ToUpper();
					lviPlugin.SubItems[clmIndex.Name].Text = intIndex.ToString();
				}
				else
				{
					lviPlugin.SubItems[clmIndexHex.Name].Text = null;
					lviPlugin.SubItems[clmIndex.Name].Text = null;
				}
			}
		}

		#region Plugin Addition

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// installed plugin list.
		/// </summary>
		/// <remarks>
		/// This updates the list of plugins to refelct changes to the installed plugin list.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ManagedPlugins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (rlvPlugins.InvokeRequired)
			{
				rlvPlugins.Invoke((Action<object, NotifyCollectionChangedEventArgs>)ManagedPlugins_CollectionChanged, sender, e);
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Replace:
					Int32 intIndex = e.NewStartingIndex;
					foreach (Plugin plgAdded in e.NewItems)
						InsertPluginToList(intIndex++, plgAdded);
					break;
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
					foreach (Plugin plgRemoved in e.OldItems)
						rlvPlugins.Items.RemoveByKey(plgRemoved.Filename.ToLowerInvariant());
					RefreshPluginIndices();
					break;
				case NotifyCollectionChangedAction.Move:
					Int32 intNewIndex = e.NewStartingIndex;
					Int32 intOldIndex = e.OldStartingIndex;
					PluginComparer pcpComparer = PluginComparer.Filename;
					foreach (Plugin plgMoved in e.NewItems)
					{
						//if the item at the new index equals the moved item,
						// then the item has already been moved in the UI,
						// so do nothing...
						if (!pcpComparer.Equals(plgMoved, (Plugin)rlvPlugins.Items[intNewIndex].Tag))
						{
							//...otherwise, move the item
							ListViewItem lviMoved = rlvPlugins.Items[intOldIndex];
							rlvPlugins.Items.RemoveAt(intOldIndex);
							rlvPlugins.Items.Insert(intNewIndex, lviMoved);
						}
						intNewIndex++;
						intOldIndex++;
					}
					if (rlvPlugins.SelectedItems.Count > 0)
						rlvPlugins.SelectedItems[0].Focused = true;
					SetCommandExecutableStatus();
					RefreshPluginIndices();
					break;
			}
		}

		/// <summary>
		/// Adds the given plugin to the view's list. If the plugin already exists in the list,
		/// it is replaced with the new version.
		/// </summary>
		/// <param name="p_plgAdded">The plugin to add to the view's list.</param>
		protected void AddPluginToList(Plugin p_plgAdded)
		{
			InsertPluginToList(rlvPlugins.Items.Count, p_plgAdded);
		}

		/// <summary>
		/// Inserts the given plugin to the view's list. If the plugin already exists in the list,
		/// it is replaced with the new version an moved to the given index.
		/// </summary>
		/// <param name="p_intIndex">The index at which to insert the plugin.</param>
		/// <param name="p_plgAdded">The plugin to add to the view's list.</param>
		protected void InsertPluginToList(Int32 p_intIndex, Plugin p_plgAdded)
		{
			Int32 intLineTracker = 0;
			try
			{
				ListViewItem lviPlugin = null;
				intLineTracker = 1;
				if (rlvPlugins.Items.ContainsKey(p_plgAdded.Filename.ToLowerInvariant()))
				{
					intLineTracker = 2;
					lviPlugin = rlvPlugins.Items[p_plgAdded.Filename.ToLowerInvariant()];
					intLineTracker = 3;
					if (lviPlugin.Index != p_intIndex)
					{
						intLineTracker = 4;
						rlvPlugins.Items.Remove(lviPlugin);
						intLineTracker = 5;
						rlvPlugins.Items.Insert(p_intIndex, lviPlugin);
						intLineTracker = 6;
					}
					intLineTracker = 7;
				}
				else
				{
					intLineTracker = 8;
					lviPlugin = new ListViewItem();
					intLineTracker = 9;
					for (Int32 i = 1; i < rlvPlugins.Columns.Count; i++)
					{
						intLineTracker = 10;
						lviPlugin.SubItems.Add(new ListViewItem.ListViewSubItem());
						intLineTracker = 11;
						lviPlugin.SubItems[i].Name = rlvPlugins.Columns[i].Name;
						intLineTracker = 12;
					}
					intLineTracker = 13;
					lviPlugin.Name = p_plgAdded.Filename.ToLowerInvariant();
					intLineTracker = 14;
					if (p_intIndex > rlvPlugins.Items.Count - 1)
					{
						intLineTracker = 15;
						rlvPlugins.Items.Add(lviPlugin);
					}
					else
					{
						intLineTracker = 16;
						rlvPlugins.Items.Insert(p_intIndex, lviPlugin);
					}
					intLineTracker = 17;
				}
				intLineTracker = 18;
				lviPlugin.Tag = p_plgAdded;
				intLineTracker = 19;
				lviPlugin.Text = Path.GetFileName(p_plgAdded.Filename);
				intLineTracker = 20;
				SetPluginActivationCheck(lviPlugin, ViewModel.ActivePlugins.Contains(p_plgAdded));
				if (!ViewModel.CanChangeActiveState(p_plgAdded))
					lviPlugin.ForeColor = SystemColors.GrayText;
				intLineTracker = 21;
				RefreshPluginIndices();
				intLineTracker = 22;
			}
			catch (NullReferenceException)
			{
				Trace.TraceError(String.Format("InsertPluginToList: NullReferenceException: LineTracker: {0}, Plugin: {1}", intLineTracker, p_plgAdded));
				throw;
			}
		}

		#endregion

		#region Plugin Activation

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
		protected void SetPluginActivationCheck(ListViewItem p_lviPlugin, bool p_booIsActive)
		{
			m_booSettingPluginActiveCheck = true;
			p_lviPlugin.Checked = p_booIsActive;
			m_booSettingPluginActiveCheck = false;
		}

		/// <summary>
		/// Show the given plugin as being either active or inactive in the view.
		/// </summary>
		/// <param name="p_plgActivated">The plugin whose active status has changed.</param>
		/// <param name="p_booIsActive">Whether the given plugin is active.</param>
		protected void SetPluginActive(Plugin p_plgActivated, bool p_booIsActive)
		{
			if (!rlvPlugins.Items.ContainsKey(p_plgActivated.Filename.ToLowerInvariant()))
				return;
			ListViewItem lviPlugin = rlvPlugins.Items[p_plgActivated.Filename.ToLowerInvariant()];
			SetPluginActivationCheck(lviPlugin, p_booIsActive);
		}

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// active plugin list.
		/// </summary>
		/// <remarks>
		/// This updates the list of plugins to refelct changes to which plugins are active.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void ActivePlugins_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (rlvPlugins.InvokeRequired)
			{
				rlvPlugins.Invoke((MethodInvoker)(() => ActivePlugins_CollectionChanged(sender, e)));
				return;
			}
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					foreach (Plugin plgAdded in e.NewItems)
						SetPluginActive(plgAdded, true);
					break;
				case NotifyCollectionChangedAction.Remove:
				case NotifyCollectionChangedAction.Reset:
					foreach (Plugin plgAdded in e.OldItems)
						SetPluginActive(plgAdded, false);
					break;
			}
			SetCommandExecutableStatus();
			RefreshPluginIndices();
		}

		/// <summary>
		/// Handles the <see cref="ListView.ItemCheck"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This toggles activation of the selected plugin. This event is used instead of
		/// <see cref="ListView.ItemChecked"/> so that we can make sure the checked state
		/// only changes if the activation/deactivation was successful; if it wasn't, we
		/// keep the state the same.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ItemCheckEventArgs"/> describing the event arguments.</param>
		private void rlvPlugins_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			if (m_booSettingPluginActiveCheck)
				return;
			if (ViewModel.CanChangeActiveState((Plugin)rlvPlugins.Items[e.Index].Tag))
			{
				if (e.NewValue == CheckState.Checked)
					if ((ViewModel.MaxAllowedActivePluginsCount > 0) && (ViewModel.ActivePlugins.Count >= ViewModel.MaxAllowedActivePluginsCount))
					{
						string strTooManyPlugins = String.Format("The requested change to the active plugins list would result in over {0} plugins being active.", ViewModel.MaxAllowedActivePluginsCount);
						strTooManyPlugins += Environment.NewLine + String.Format("The current game doesn't support more than {0} active plugins, you need to disable at least one plugin to continue.", ViewModel.MaxAllowedActivePluginsCount);
						strTooManyPlugins += Environment.NewLine + Environment.NewLine + String.Format("NOTE: This is a game engine limitation, not {0}'s.", Application.ProductName);
						MessageBox.Show(strTooManyPlugins, "Too many active plugins",  MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
					else
						ViewModel.ActivatePlugin((Plugin)rlvPlugins.Items[e.Index].Tag);
				else if (e.NewValue == CheckState.Unchecked)
					ViewModel.DeactivatePlugin((Plugin)rlvPlugins.Items[e.Index].Tag);
			}
			e.NewValue = ViewModel.ActivePlugins.Contains((Plugin)rlvPlugins.Items[e.Index].Tag) ? CheckState.Checked : CheckState.Unchecked;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ListView.SelectedIndexChanged"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This displays the picture and description of the selected plugin.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void rlvPlugins_SelectedIndexChanged(object sender, EventArgs e)
		{
			Plugin plgPlugin = GetSelectedPlugin();
			if (plgPlugin != null)
			{
				ipbImage.Image = plgPlugin.Picture;
				hlbPluginInfo.Text = plgPlugin.Description;
			}
			else
			{
				ipbImage.Image = null;
				hlbPluginInfo.Text = null;
			}
			SetCommandExecutableStatus();
		}

		#region Plugin Order

		/// <summary>
		/// Handles the <see cref="ReorderableListView.ItemsReordered"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This reorders the moved plugins.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ReorderedItemsEventArgs"/> describing the event arguments.</param>
		private void rlvPlugins_ItemsReordered(object sender, ReorderedItemsEventArgs e)
		{
			foreach (ReorderedItemsEventArgs.ReorderedListViewItem lviPlugin in e.ReorderedListViewItems)
				ViewModel.SetPluginOrderIndex((Plugin)lviPlugin.Item.Tag, lviPlugin.NewIndex);
		}

		/// <summary>
		/// Handles the <see cref="ReorderableListView.ItemsReordering"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This makes sure the new order is going to be valid. If not, the reordering event is stopped.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="ReorderingItemsEventArgs"/> describing the event arguments.</param>
		private void rlvPlugins_ItemsReordering(object sender, ReorderingItemsEventArgs e)
		{
			List<Plugin> lstNewOrder = new List<Plugin>(ViewModel.ManagedPlugins);
			foreach (ReorderedItemsEventArgs.ReorderedListViewItem lviPlugin in e.ReorderedListViewItems)
			{
				Plugin plgMoved = (Plugin)lviPlugin.Item.Tag;
				lstNewOrder.RemoveAt(lviPlugin.OldIndex);
				lstNewOrder.Insert(lviPlugin.NewIndex, plgMoved);
			}
			e.Cancel = !ViewModel.ValidatePluginOrder(lstNewOrder);
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
		/// This resizes the columns to fill the list view. The index column is fixed width.
		/// </summary>
		protected void SizeColumnsToFit()
		{
			if (rlvPlugins.Columns.Count == 0)
				return;
			m_booResizing = true;
			ColumnHeader[] clmFixedWidthColumns = new ColumnHeader[] { clmIndexHex, clmIndex };
			Int32 intFixedWidth = 0;
			foreach (ColumnHeader clmFixed in clmFixedWidthColumns)
				intFixedWidth += clmFixed.Width;
			Int32 intWidth = (rlvPlugins.ClientSize.Width - intFixedWidth) / (rlvPlugins.Columns.Count - clmFixedWidthColumns.Length);
			for (Int32 i = 0; i < rlvPlugins.Columns.Count; i++)
				if (Array.IndexOf(clmFixedWidthColumns, rlvPlugins.Columns[i]) < 0)
					rlvPlugins.Columns[i].Width = intWidth;
			m_booResizing = false;
		}

		/// <summary>
		/// Handles the <see cref="Control.Resize"/> event of the plugin list.
		/// </summary>
		/// <remarks>
		/// This resizes the columns to fill the list view. The index column is fixed width.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void rlvPlugins_Resize(object sender, EventArgs e)
		{
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
		private void rlvPlugins_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
		{
			if (m_booResizing)
				return;
			ColumnHeader clmThis = rlvPlugins.Columns[e.ColumnIndex];
			ColumnHeader clmOther = null;
			if (e.ColumnIndex == rlvPlugins.Columns.Count - 1)
				clmOther = rlvPlugins.Columns[e.ColumnIndex - 1];
			else
				clmOther = rlvPlugins.Columns[e.ColumnIndex + 1];
			m_booResizing = true;
			clmOther.Width += (clmThis.Width - e.NewWidth);
			m_booResizing = false;
		}

		#endregion

		#region Load Order IO

		#region Export
		
		/// <summary>
		/// Returns the full path of the text file to export to.
		/// </summary>
		/// <returns>The full path of the text file to export to.</returns>
		private string GetExportToFileArgs()
		{
			sfdChooseExport.FileName = ViewModel.GetDefaultExportFilename();
			sfdChooseExport.Filter = ViewModel.GetExportFilterString();

			if (sfdChooseExport.ShowDialog() == DialogResult.OK)
				return sfdChooseExport.FileName;

			return null;
		}

		/// <summary>
		/// Handles the <see cref="PluginManagerVM.ExportFailed"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays a simple error message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ExportFailedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_ExportFailed(object sender, ExportFailedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, ExportFailedEventArgs>)ViewModel_ExportFailed, sender, e);
				return;
			}

			BeginInvoke(new MethodInvoker(() =>
			{
				if (string.IsNullOrEmpty(e.Filename))
					Trace.TraceError("Failed to export the current load order to the clipboard");
				else
					Trace.TraceError("Failed to export the current load order to: {0}", e.Filename);
				Trace.Indent();
				Trace.TraceError("Reason: {0}", e.Message);
				if (e.Error != null)
					TraceUtil.TraceException(e.Error);
				Trace.Unindent();
			}));

			string message
				= "An error was encountered trying to export the current load order." + Environment.NewLine
				+ Environment.NewLine
				+ "Full details are available in the trace log.";
			string details = "<b>Error:</b> " + e.Message;
			ExtendedMessageBox.Show(this, message, Application.ProductName, details, MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		/// <summary>
		/// Handles the <see cref="PluginManagerVM.ExportSucceeded"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays a simple success message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{String}"/> describing the event arguments.</param>
		private void ViewModel_ExportSucceeded(object sender, ExportSucceededEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, ExportSucceededEventArgs>)ViewModel_ExportSucceeded, sender, e);
				return;
			}

			string message = "The current load order was successfully exported to";
			if (string.IsNullOrEmpty(e.Filename))
				message += " the clipboard.";
			else
				message += ":" + Environment.NewLine + Environment.NewLine + e.Filename;

			string details = string.Format("{0} {1} successfully exported.", e.ExportedPluginCount, (e.ExportedPluginCount == 1) ? "plugin was" : "plugins were");
			ExtendedMessageBox.Show(this, message, ViewModel.Settings.ModManagerName, details.ToString(), ExtendedMessageBoxButtons.OK, MessageBoxIcon.Information);
		} 

		#endregion

		#region Import

		/// <summary>
		/// Formats the text to display when importing a load order succeeds.
		/// </summary>
		/// <param name="p_iseEventArgs">An <see cref="ImportSucceededEventArgs"/> describing the event arguments.</param>
		/// <param name="p_sbMessage">The formatted success message.</param>
		/// <param name="p_strDetails">The names of the plugins that were not imported.</param>
		private void FormatPluginCountsReport(ImportSucceededEventArgs p_iseEventArgs, ref System.Text.StringBuilder p_sbMessage, out string p_strDetails)
		{
			if (string.IsNullOrEmpty(p_iseEventArgs.Filename))
				p_sbMessage.AppendLine(" the clipboard.");
			else
			{
				p_sbMessage.AppendLine(":");
				p_sbMessage.AppendLine();
				p_sbMessage.AppendLine();
				p_sbMessage.AppendLine(p_iseEventArgs.Filename);
			}
			p_sbMessage.AppendLine();

			if (p_iseEventArgs.TotalPluginCount == 1)
				p_sbMessage.AppendLine("- 1 plugin found in the import source");
			else
				p_sbMessage.AppendFormat("- {0} plugins found in the import source", p_iseEventArgs.TotalPluginCount).AppendLine();

			if (p_iseEventArgs.ImportedPluginCount == 1)
				p_sbMessage.AppendLine("- 1 plugin successfully imported");
			else
				p_sbMessage.AppendFormat("- {0} plugin(s) successfully imported", p_iseEventArgs.ImportedPluginCount).AppendLine();

			if (p_iseEventArgs.PluginsNotImported.Count == 1)
				p_sbMessage.AppendLine("- 1 plugin not imported (not managed)");
			else
				p_sbMessage.AppendFormat("- {0} plugins not imported (not managed)", p_iseEventArgs.PluginsNotImported.Count).AppendLine();

			System.Text.StringBuilder sbDetails = new System.Text.StringBuilder();
			foreach (string strPluginFilename in p_iseEventArgs.PluginsNotImported)
				sbDetails.AppendFormat("- {0}", strPluginFilename).AppendLine();

			if (sbDetails.Length > 0)
				sbDetails.Insert(0, "The following plugin(s) were not imported: " + Environment.NewLine);
			
			if (sbDetails.Length > 0)
				p_strDetails = sbDetails.ToString();
			else
				p_strDetails = null;
		}

		/// <summary>
		/// Returns the full filename specifying the text file to import from.
		/// </summary>
		/// <returns>The full filename specifying the text file to import from.</returns>
		private string GetImportFromFileArgs()
		{
			ofdChooseImport.FileName = null;
			ofdChooseImport.Filter = ViewModel.GetImportFilterString();

			if (ofdChooseImport.ShowDialog() == DialogResult.OK)
				return ofdChooseImport.FileName;

			return null;
		}

		/// <summary>
		/// Handles the <see cref="PluginManagerVM.ImportFailed"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays a simple error message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ImportFailedEventArgs"/> describing the event arguments.</param>
		private void ViewModel_ImportFailed(object sender, ImportFailedEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, ImportFailedEventArgs>)ViewModel_ImportFailed, sender, e);
				return;
			}

			BeginInvoke(new MethodInvoker(() =>
			{
				if (string.IsNullOrEmpty(e.Filename))
					Trace.TraceError("Failed to import the load order from the clipboard.");
				else
					Trace.TraceError("Failed to import the load order from: {0}", e.Filename);
				Trace.Indent();
				Trace.TraceError("Reason: {0}", e.Message);
				if (e.Error != null)
					TraceUtil.TraceException(e.Error);
				Trace.Unindent();
			}));

			string message
				= "An error was encountered while trying to import the load order." + Environment.NewLine
				+ Environment.NewLine
				+ e.Message + Environment.NewLine;
			ExtendedMessageBox.Show(this, message, Application.ProductName, null, MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		/// <summary>
		/// Handles the <see cref="PluginManagerVM.ImportPartiallySucceeded"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays a message indicating partial success and detailing the total, imported and not imported plugin counts.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{String}"/> describing the event arguments.</param>
		private void ViewModel_ImportPartiallySucceeded(object sender, ImportSucceededEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, ImportSucceededEventArgs>)ViewModel_ImportPartiallySucceeded, sender, e);
				return;
			}

			System.Text.StringBuilder sbMessage = new System.Text.StringBuilder();
			string strDetails;

			sbMessage.Append("The load order was partially imported from");

			FormatPluginCountsReport(e, ref sbMessage, out strDetails);

			ExtendedMessageBox.Show(this, sbMessage.ToString(), ViewModel.Settings.ModManagerName, strDetails, ExtendedMessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		/// <summary>
		/// Handles the <see cref="PluginManagerVM.ImportSucceeded"/> event of the view model.
		/// </summary>
		/// <remarks>
		/// This displays a simple success message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{String}"/> describing the event arguments.</param>
		private void ViewModel_ImportSucceeded(object sender, ImportSucceededEventArgs e)
		{
			if (InvokeRequired)
			{
				Invoke((Action<object, ImportSucceededEventArgs>)ViewModel_ImportSucceeded, sender, e);
				return;
			}

			System.Text.StringBuilder sbMessage = new System.Text.StringBuilder();
			string strDetails;

			sbMessage.Append("The load order was successfully imported from");

			FormatPluginCountsReport(e, ref sbMessage, out strDetails);

			ExtendedMessageBox.Show(this, sbMessage.ToString(), ViewModel.Settings.ModManagerName, strDetails, ExtendedMessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		
		#endregion
		
		#endregion
	}
}
