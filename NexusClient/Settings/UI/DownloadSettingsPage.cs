using System;
using System.Collections.Generic;
using System.Drawing;
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
		private IList<FileServerZone> m_fszFileServerZone;

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
			m_fszFileServerZone = p_dsgSettings.FileServerZones;
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

		private void cbxServerLocation_DrawItem(object sender, DrawItemEventArgs e)
		{
			string strImageFont = "Arial";
			System.Drawing.FontStyle fsFontStyle = FontStyle.Regular;

			if (!IsFontInstalled(strImageFont))
				strImageFont = this.Font.FontFamily.ToString();

			Font objFont = new Font(strImageFont, 11, fsFontStyle, System.Drawing.GraphicsUnit.Pixel);

			// Let's highlight the currently selected item like any well 
			// behaved combo box should
			e.Graphics.FillRectangle(Brushes.Bisque, e.Bounds);
			e.Graphics.DrawString(m_fszFileServerZone[e.Index].FileServerName, objFont, Brushes.Black,
								  new Point(m_fszFileServerZone[e.Index].FileServerFlag.Width * 2, e.Bounds.Y));
			e.Graphics.DrawImage(m_fszFileServerZone[e.Index].FileServerFlag, new Point(e.Bounds.X, e.Bounds.Y));
          
			if ((e.State & DrawItemState.Focus) == 0)
			{
				e.Graphics.FillRectangle(Brushes.White, e.Bounds);
				e.Graphics.DrawString(m_fszFileServerZone[e.Index].FileServerName, objFont, Brushes.Black,
									  new Point(m_fszFileServerZone[e.Index].FileServerFlag.Width * 2, e.Bounds.Y));
				e.Graphics.DrawImage(m_fszFileServerZone[e.Index].FileServerFlag, new Point(e.Bounds.X, e.Bounds.Y));
			}  
		}

		/// <summary>
		/// This checks if the passed font is present.
		/// </summary>
		private bool IsFontInstalled(string fontName)
		{
			try
			{
				using (var testFont = new Font(fontName, 8))
				{
					return 0 == string.Compare(
					  fontName,
					  testFont.Name,
					  StringComparison.InvariantCultureIgnoreCase);
				}
			}
			catch
			{
				return false;
			}
		}
	}
}
