using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Nexus.Client.ModManagement.Scripting.XmlScript.UI
{
	/// <summary>
	/// Displays the optional plugin groups for a specific step in a mod's install.
	/// </summary>
	public partial class OptionFormStep : UserControl
	{
		/// <summary>
		/// Raised when an option is checked.
		/// </summary>
		public event EventHandler ItemChecked = delegate { };

		private ConditionStateManager m_csmStateManager = null;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_csmStateManager">The install state manager.</param>
		/// <param name="p_lstGroups">The option groups to display.</param>
		public OptionFormStep(ConditionStateManager p_csmStateManager, IList<OptionGroup> p_lstGroups)
		{
			m_csmStateManager = p_csmStateManager;

			InitializeComponent();

			LoadOptions(p_lstGroups);
			if (lvwPlugins.Items.Count > 0)
				lvwPlugins.Items[0].Selected = true;
		}

		#endregion

		#region Control Members

		/// <summary>
		/// Gets the list of files and folders that need to be installed.
		/// </summary>
		/// <remarks>
		/// The list returned is base upon the plugins that the user selected.
		/// </remarks>
		/// <value>The list of files and folders that need to be installed.</value>
		public List<InstallableFile> FilesToInstall
		{
			get
			{
				List<InstallableFile> lstInstall = new List<InstallableFile>();
				foreach (ListViewItem lviItem in lvwPlugins.Items)
				{
					Option optOption = (Option)lviItem.Tag;
					OptionType otpOptionType = optOption.OptionTypeResolver.ResolveOptionType(m_csmStateManager);
					if (lviItem.Checked)
						lstInstall.AddRange(optOption.Files);
					else
						foreach (InstallableFile iflFile in optOption.Files)
							if (iflFile.AlwaysInstall || (iflFile.InstallIfUsable && (otpOptionType != OptionType.NotUsable)))
								lstInstall.Add(iflFile);
				}
				lstInstall.Sort();
				return lstInstall;
			}
		}

		/// <summary>
		/// Gets the list of files, and folders that may contain files, that need to be activated.
		/// </summary>
		/// <remarks>
		/// The list returned is base upon the plugins that the user selected.
		/// </remarks>
		/// <value>The list of files, and folders that may contain files, that need to be activated.</value>
		public List<InstallableFile> PluginsToActivate
		{
			get
			{
				List<InstallableFile> lstActivate = new List<InstallableFile>();
				foreach (ListViewItem lviItem in lvwPlugins.Items)
				{
					Option optOption = (Option)lviItem.Tag;
					if (lviItem.Checked)
						foreach (InstallableFile iflFile in optOption.Files)
						{
							if (iflFile.IsFolder)
							{
								if (iflFile.Destination.Length == 0)
									lstActivate.Add(iflFile);
							}
							else if (String.IsNullOrEmpty(iflFile.Destination))
							{
								if (iflFile.Source.ToLower().EndsWith(".esm") || iflFile.Source.ToLower().EndsWith(".esp"))
									lstActivate.Add(iflFile);
							}
							else if (iflFile.Destination.ToLower().EndsWith(".esm") || iflFile.Destination.ToLower().EndsWith(".esp"))
								lstActivate.Add(iflFile);
						}
				}
				lstActivate.Sort();
				return lstActivate;
			}
		}

		/// <summary>
		/// Loads the options into the form.
		/// </summary>
		/// <param name="p_lstGroups">The list of grouped options.</param>
		private void LoadOptions(IList<OptionGroup> p_lstGroups)
		{
			AdjustListViewColumnWidth();
			foreach (OptionGroup ogpGroup in p_lstGroups)
			{
				ListViewGroup lvgGroup = AddGroup(ogpGroup);
				foreach (Option optOption in ogpGroup.Options)
					AddOption(lvgGroup, optOption);
			}
			CheckDefaults();
		}

		/// <summary>
		/// Checks the plugins that should be checked by default.
		/// </summary>
		private void CheckDefaults()
		{
			ListViewItem lviRequired = null;
			ListViewItem lviRecommended = null;
			Option optOption = null;
			foreach (ListViewGroup lvgGroup in lvwPlugins.Groups)
			{
				switch ((OptionGroupType)lvgGroup.Tag)
				{
					case OptionGroupType.SelectAll:
						foreach (ListViewItem lviPlugin in lvgGroup.Items)
							lviPlugin.Checked = true;
						break;
					case OptionGroupType.SelectExactlyOne:
						lviRequired = null;
						lviRecommended = null;
						foreach (ListViewItem lviPlugin in lvgGroup.Items)
						{
							optOption = (Option)lviPlugin.Tag;
							switch (optOption.OptionTypeResolver.ResolveOptionType(m_csmStateManager))
							{
								case OptionType.Recommended:
									lviRecommended = lviPlugin;
									break;
								case OptionType.Required:
									lviRequired = lviPlugin;
									break;
							}
						}
						if (lviRequired != null)
							lviRequired.Checked = true;
						else if (lviRecommended != null)
							lviRecommended.Checked = true;
						else if (lvgGroup.Items.Count > 0)
							lvgGroup.Items[0].Checked = true;
						break;
					case OptionGroupType.SelectAtLeastOne:
					default:
						bool booOneSelected = false;
						foreach (ListViewItem lviPlugin in lvgGroup.Items)
						{
							optOption = (Option)lviPlugin.Tag;
							switch (optOption.OptionTypeResolver.ResolveOptionType(m_csmStateManager))
							{
								case OptionType.Recommended:
								case OptionType.Required:
									lviPlugin.Checked = true;
									booOneSelected = true;
									break;
							}
						}
						if ((OptionGroupType.SelectAtLeastOne == (OptionGroupType)lvgGroup.Tag) && !booOneSelected && (lvgGroup.Items.Count > 0))
							lvgGroup.Items[0].Checked = true;
						break;
				}
			}
		}

		/// <summary>
		/// Sizes the column of the list view of plugins to fill the control.
		/// </summary>
		private void AdjustListViewColumnWidth()
		{
			lvwPlugins.Columns[0].Width = lvwPlugins.Width - SystemInformation.VerticalScrollBarWidth - 6;
		}

		/// <summary>
		/// Adds a group to the list of plugins.
		/// </summary>
		/// <param name="p_ogpGroup">The plugin group to add.</param>
		/// <returns>The new <see cref="ListViewGroup"/> representing the group.</returns>
		private ListViewGroup AddGroup(OptionGroup p_ogpGroup)
		{
			ListViewGroup lvgGroup = null;
			foreach (ListViewGroup lvgExistingGroup in lvwPlugins.Groups)
				if (lvgExistingGroup.Name.Equals(p_ogpGroup.Name))
				{
					lvgGroup = lvgExistingGroup;
					break;
				}
			if (lvgGroup == null)
			{
				lvgGroup = new ListViewGroup();
				lvwPlugins.Groups.Add(lvgGroup);
			}
			lvgGroup.Name = p_ogpGroup.Name;
			lvgGroup.Tag = p_ogpGroup.Type;
			switch (p_ogpGroup.Type)
			{
				case OptionGroupType.SelectAll:
					lvgGroup.Header = p_ogpGroup.Name + " (All Required)";
					break;
				case OptionGroupType.SelectAtLeastOne:
					lvgGroup.Header = p_ogpGroup.Name + " (One Required)";
					break;
				case OptionGroupType.SelectAtMostOne:
					lvgGroup.Header = p_ogpGroup.Name + " (Select Only One)";
					break;
				case OptionGroupType.SelectExactlyOne:
					lvgGroup.Header = p_ogpGroup.Name + " (Select One)";
					break;
				case OptionGroupType.SelectAny:
					lvgGroup.Header = p_ogpGroup.Name;
					break;
			}
			return lvgGroup;
		}

		/// <summary>
		/// Adds an option to the list of options.
		/// </summary>
		/// <param name="p_lvgGroup">The group to which to add the option.</param>
		/// <param name="p_optOption">The plugin to add.</param>
		private void AddOption(ListViewGroup p_lvgGroup, Option p_optOption)
		{
			string strName = p_optOption.Name;
			ListViewItem lviPlugin = null;
			foreach (ListViewItem lviExistingPlugin in p_lvgGroup.Items)
				if (lviExistingPlugin.Text.Equals(strName))
				{
					lviPlugin = lviExistingPlugin;
					break;
				}
			if (lviPlugin == null)
			{
				lviPlugin = new ListViewItem();
				lvwPlugins.Items.Add(lviPlugin);
			}

			lviPlugin.Text = strName;
			lviPlugin.Tag = p_optOption;
			lviPlugin.Group = p_lvgGroup;
			lviPlugin.Checked = false;
		}

		/// <summary>
		/// Handles the SizeChanged event of the list view of plugins.
		/// </summary>
		/// <remarks>
		/// This ensures that the column of the list view of plugins fills the control.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwPlugins_SizeChanged(object sender, EventArgs e)
		{
			AdjustListViewColumnWidth();
		}

		/// <summary>
		/// Handles the SelectedIndexChanged event of the list view of plugins.
		/// </summary>
		/// <remarks>
		/// This changes the displayed description to that of the selected plugin.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwPlugins_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (lvwPlugins.SelectedItems.Count > 0)
			{
				Option optOption = (Option)lvwPlugins.SelectedItems[0].Tag;
				tbxDescription.Text = optOption.Description;
				pbxImage.Image = m_csmStateManager.GetImage(optOption.ImagePath);
			}
			else
			{
				tbxDescription.Text = "";
				pbxImage.Image = null;
			}
			sptImage.Panel2Collapsed = (pbxImage.Image == null);
		}

		/// <summary>
		/// Handles the ItemCheck event of the list view of plugins.
		/// </summary>
		/// <remarks>
		/// This enforces any restrictions on the selection of plugins.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwPlugins_ItemCheck(object sender, ItemCheckEventArgs e)
		{
			Option optOption = (Option)lvwPlugins.Items[e.Index].Tag;
			switch (optOption.OptionTypeResolver.ResolveOptionType(m_csmStateManager))
			{
				case OptionType.Required:
					if (e.NewValue != CheckState.Checked)
						MessageBox.Show(this, optOption.Name + " is required. You cannot unselect it.");
					e.NewValue = CheckState.Checked;
					return;
				case OptionType.Recommended:
					if (e.NewValue != CheckState.Checked)
						if (MessageBox.Show(this, optOption.Name + " is recommended. Disabling it may result in game instability. Are you sure you want to continue?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
						{
							e.NewValue = CheckState.Checked;
							return;
						}
					break;
				case OptionType.NotUsable:
				case OptionType.CouldBeUsable:
					if (e.NewValue == CheckState.Checked)
						if (MessageBox.Show(this, optOption.Name + " is not usable with your loaded mods. Enabling it may result in game instability. Are you sure you want to continue?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
						{
							e.NewValue = CheckState.Unchecked;
							return;
						}
					break;
			}
			ListViewGroup lvgGroup = lvwPlugins.Items[e.Index].Group;
			switch ((OptionGroupType)lvgGroup.Tag)
			{
				case OptionGroupType.SelectAll:
					if (e.NewValue != CheckState.Checked)
						MessageBox.Show(this, optOption.Name + " is required. You cannot unselect it.");
					e.NewValue = CheckState.Checked;
					break;
				case OptionGroupType.SelectAtLeastOne:
					if (e.NewValue != CheckState.Checked)
					{
						bool booOtherChecked = false;
						foreach (ListViewItem lviGroupItem in lvgGroup.Items)
							if ((lviGroupItem.Index != e.Index) && (lviGroupItem.Checked))
							{
								booOtherChecked = true;
								break;
							}
						if (!booOtherChecked)
						{
							MessageBox.Show(this, "You must select at least one plugin in this group.");
							e.NewValue = CheckState.Checked;
						}
					}
					break;
				case OptionGroupType.SelectExactlyOne:
					if (e.NewValue != CheckState.Checked)
					{
						bool booOtherChecked = false;
						foreach (ListViewItem lviGroupItem in lvgGroup.Items)
							if ((lviGroupItem.Index != e.Index) && (lviGroupItem.Checked))
							{
								booOtherChecked = true;
								break;
							}
						if (!booOtherChecked)
						{
							MessageBox.Show(this, "You must select one plugin in this group.");
							e.NewValue = CheckState.Checked;
						}
					}
					break;
			}
		}

		/// <summary>
		/// Handles the ItemChecked event of the list view of plugins.
		/// </summary>
		/// <remarks>
		/// This enforces any restrictions on the selection of plugins.
		/// </remarks>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lvwPlugins_ItemChecked(object sender, ItemCheckedEventArgs e)
		{
			ListViewItem lviItem = e.Item;
			ListViewGroup lvgGroup = lviItem.Group;
			switch ((OptionGroupType)lvgGroup.Tag)
			{
				case OptionGroupType.SelectAtMostOne:
				case OptionGroupType.SelectExactlyOne:
					if (lviItem.Checked)
						foreach (ListViewItem lviGroupItem in lvgGroup.Items)
							if ((lviGroupItem != lviItem) && (lviGroupItem.Index > -1))
								lviGroupItem.Checked = false;
					break;
			}
			Option optOption = (Option)e.Item.Tag;
			if (lviItem.Checked)
			{
				foreach (ConditionalFlag cfgFlag in optOption.Flags)
					m_csmStateManager.SetFlagValue(cfgFlag.Name, cfgFlag.ConditionalValue, optOption);
			}
			else
				m_csmStateManager.RemoveFlags(optOption);
			ItemChecked(this, new EventArgs());
		}

		#endregion
	}
}
