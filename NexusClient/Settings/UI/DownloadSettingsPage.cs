using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security;
using System.Security.Permissions;
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

			butChromeFix.Image = new Bitmap(Properties.Resources.uac_icon, 20, 20);

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

			if (cbxServerLocation.DataBindings != null)
				cbxServerLocation.DataBindings.Clear();
			BindingHelper.CreateFullBinding(cbxServerLocation, () => cbxServerLocation.SelectedValue, p_dsgSettings, () => p_dsgSettings.UserLocation);
			if (ckbPremiumOnly.DataBindings != null)
				ckbPremiumOnly.DataBindings.Clear();
			BindingHelper.CreateFullBinding(ckbPremiumOnly, () => ckbPremiumOnly.Checked, p_dsgSettings, () => p_dsgSettings.UseMultithreadedDownloads);
			ckbPremiumOnly.Enabled = p_dsgSettings.PremiumEnabled;
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

		private void butChromeFix_Click(object sender, EventArgs e)
		{
			try
			{
				ChromeFix();
			}
			catch (SecurityException)
			{
				MessageBox.Show("You MUST run the program as Administrator to use this functionality.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
		}

		private void ChromeFix()
		{
			Process[] prcName = Process.GetProcessesByName("chrome");
			if (prcName.Length > 0)
			{
				MessageBox.Show("Make sure Chrome is closed before running this fix.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			string strChromeAppDataPath = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), @"Google\Chrome\User Data\");
			FileInfo fiChrome = new FileInfo(Path.Combine(strChromeAppDataPath, "Local State"));
			if (fiChrome.Exists)
			{
				try
				{
					fiChrome.CopyTo(Path.Combine(strChromeAppDataPath, "Local State.nmm.bak"), true);
				}
				catch
				{
					DialogResult drWarning = MessageBox.Show("Unable to backup the 'Local State' file, do you want to continue anyway?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
					if (drWarning == DialogResult.No)
						return;
				}

				string[] lines = File.ReadAllLines(fiChrome.FullName);
				using (StreamWriter swChromeLocalState = new StreamWriter(fiChrome.FullName))
				{
					bool booFound = false;
					foreach (string line in lines)
					{
						string strLine = line;
						if (!booFound)
						{
							if (strLine.IndexOf("\"excluded_schemes\": {") >= 0)
							{
								swChromeLocalState.WriteLine(strLine);
								swChromeLocalState.WriteLine("         \"nxm\": false,");
								booFound = true;
								continue;
							}
						}
						else
						{
							if (strLine.IndexOf("\"nxm\": ") >= 0)
								continue;
						}

						swChromeLocalState.WriteLine(strLine);
					}
				}
			}
			else
			{
				MessageBox.Show("It seems that Chrome is not present (or properly installed) on your system.", "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			MessageBox.Show("The fix was successfully applied.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
	}
}
