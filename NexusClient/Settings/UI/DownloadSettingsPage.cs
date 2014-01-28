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
			p_dsgSettings.UpdatedSettings += new EventHandler(dsgSettings_UpdatedSettings);
			InitializeComponent();

			SetBindings(p_dsgSettings);
		}

		#endregion

		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; private set; }

		#endregion

		private void SetBindings(DownloadSettingsGroup p_dsgSettings)
		{
			BindingSource bsFileServerZones = new BindingSource();
			if (m_fszFileServerZone != null)
				m_fszFileServerZone.Clear();
			m_fszFileServerZone = p_dsgSettings.FileServerZones;

			bsFileServerZones.DataSource = p_dsgSettings.FileServerZones;

			cbxServerLocation.DataSource = bsFileServerZones.DataSource;
			cbxServerLocation.DisplayMember = "FileServerName";
			cbxServerLocation.ValueMember = "FileServerID";

			cbxServerLocation.DataBindings.Clear();
			BindingHelper.CreateFullBinding(cbxServerLocation, () => cbxServerLocation.SelectedValue, p_dsgSettings, () => p_dsgSettings.UserLocation);
			if (p_dsgSettings.PremiumEnabled)
			{
				ckbPremiumOnly.DataBindings.Clear();
				BindingHelper.CreateFullBinding(ckbPremiumOnly, () => ckbPremiumOnly.Checked, p_dsgSettings, () => p_dsgSettings.UseMultithreadedDownloads);
				ckbPremiumOnly.Enabled = true;
			}
			else
			{
				ckbPremiumOnly.Enabled = false;
			}
		}

		private void cbxServerLocation_DrawItem(object sender, DrawItemEventArgs e)
		{
			string strImageFont = "Arial";
			System.Drawing.FontStyle fsFontStyle = FontStyle.Regular;

			if (!IsFontInstalled(strImageFont))
				strImageFont = this.Font.FontFamily.ToString();

			Font objFont = new Font(strImageFont, 11, fsFontStyle, System.Drawing.GraphicsUnit.Pixel);

			// Let's highlight the currently selected item like any well 
			// behaved combo box should
			try
			{
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
			catch
			{
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

		private void dsgSettings_UpdatedSettings(object sender, EventArgs e)
		{
			DownloadSettingsGroup dsgSettings = (DownloadSettingsGroup)sender;
			if (dsgSettings != null)
				SetBindings(dsgSettings);
		}
	}
}
