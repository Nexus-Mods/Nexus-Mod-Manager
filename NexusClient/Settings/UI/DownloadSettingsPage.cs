using System.Collections.Generic;
using System.Windows.Forms;
using Nexus.Client.ModRepositories;
using Nexus.Client.Util;


namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// A view allowing the editing of mod options.
	/// </summary>
	public partial class DownloadSettingsPage : UserControl, ISettingsGroupView
	{
		#region Constructors

		/// <summary>
		/// A sinmple consturctor that initializes teh object with the given values.
		/// </summary>
		/// <param name="p_dsgSettings">The settings group whose settings will be editable with this view.</param>
		public DownloadSettingsPage(DownloadSettingsGroup p_dsgSettings)
		{
			SettingsGroup = p_dsgSettings;
			InitializeComponent();

			BindingSource bsFileServerZones = new BindingSource();
			bsFileServerZones.DataSource = p_dsgSettings.FileServerZones;

			cbxConnections.DataSource = p_dsgSettings.AllowedConnections;
			cbxServerLocation.DataSource = bsFileServerZones.DataSource;
			cbxServerLocation.DisplayMember = "FileServerName";
			cbxServerLocation.ValueMember = "FileServerID";

			BindingHelper.CreateFullBinding(cbxServerLocation, () => cbxServerLocation.SelectedValue, p_dsgSettings, () => p_dsgSettings.UserLocation);
			BindingHelper.CreateFullBinding(cbxConnections, () => cbxConnections.SelectedItem, p_dsgSettings, () => p_dsgSettings.NumberOfConnections);
			if (p_dsgSettings.PremiumEnabled)
				BindingHelper.CreateFullBinding(ckbPremiumOnly, () => ckbPremiumOnly.Checked, p_dsgSettings, () => p_dsgSettings.PremiumOnly);
			else
			{
				cbxConnections.Enabled = false;
				ckbPremiumOnly.Enabled = false;
			}
		}

		#endregion

		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; private set; }

		#endregion
	}
}
