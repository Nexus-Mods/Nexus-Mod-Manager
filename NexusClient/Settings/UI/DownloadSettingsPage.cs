namespace Nexus.Client.Settings.UI
{
    using System;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Security;
    using System.Windows.Forms;
    using Nexus.Client.Util;

    /// <summary>
    /// A view allowing the editing of mod options.
    /// </summary>
    public partial class DownloadSettingsPage : UserControl, ISettingsGroupView
	{
		private enum MaxDownloadsInterval
		{
			One = 1,
			Three = 3,
			Five = 5,
			Ten = 10
		}

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes teh object with the given values.
		/// </summary>
		/// <param name="downloadSettingsGroup">The settings group whose settings will be editable with this view.</param>
		public DownloadSettingsPage(DownloadSettingsGroup downloadSettingsGroup)
		{
			SettingsGroup = downloadSettingsGroup;
			downloadSettingsGroup.UpdatedSettings += dsgSettings_UpdatedSettings;
			InitializeComponent();
			
			buttonChromeFix.Image = new Bitmap(Properties.Resources.uac_icon, 20, 20);

			SetBindings(downloadSettingsGroup);
		}

		#endregion

		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; }

		#endregion

		private void SetBindings(DownloadSettingsGroup downloadSettingsGroup)
		{
			checkBoxMaxConcurrentDownloads.DataSource = Enum.GetValues(typeof(MaxDownloadsInterval))
				.Cast<MaxDownloadsInterval>()
				.Select(p => new { Value = (int)p, Key = p.ToString() })
				.ToList();
			checkBoxMaxConcurrentDownloads.DisplayMember = "Key";
			checkBoxMaxConcurrentDownloads.ValueMember = "Value";

            checkBoxPremiumOnly.DataBindings?.Clear();
            BindingHelper.CreateFullBinding(checkBoxPremiumOnly, () => checkBoxPremiumOnly.Checked, downloadSettingsGroup, () => downloadSettingsGroup.UseMultithreadedDownloads);
			checkBoxPremiumOnly.Enabled = downloadSettingsGroup.PremiumEnabled;

            checkBoxMaxConcurrentDownloads.DataBindings?.Clear();
            BindingHelper.CreateFullBinding(checkBoxMaxConcurrentDownloads, () => checkBoxMaxConcurrentDownloads.SelectedValue, downloadSettingsGroup, () => downloadSettingsGroup.MaxConcurrentDownloads);
		}

		private void dsgSettings_UpdatedSettings(object sender, EventArgs e)
		{
			var downloadSettingsGroup = (DownloadSettingsGroup)sender;

            if (downloadSettingsGroup != null)
            {
                SetBindings(downloadSettingsGroup);
            }
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

		private static void ChromeFix()
		{
			var processes = Process.GetProcessesByName("chrome");

            if (processes.Length > 0)
			{
				MessageBox.Show("Make sure Chrome is closed before running this fix.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			var chromeAppDataPath = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), @"Google\Chrome\User Data\");
			var chrome = new FileInfo(Path.Combine(chromeAppDataPath, "Local State"));

            if (chrome.Exists)
			{
				try
				{
					chrome.CopyTo(Path.Combine(chromeAppDataPath, "Local State.nmm.bak"), true);
				}
				catch
				{
					var dialogResult = MessageBox.Show("Unable to backup the 'Local State' file, do you want to continue anyway?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.No)
                    {
                        return;
                    }
                }

				var lines = File.ReadAllLines(chrome.FullName);

                using (var chromeLocalState = new StreamWriter(chrome.FullName))
				{
					var found = false;

                    foreach (var line in lines)
					{
						var tempLine = line;

                        if (!found)
						{
							if (tempLine.IndexOf("\"excluded_schemes\": {") >= 0)
							{
								chromeLocalState.WriteLine(tempLine);
								chromeLocalState.WriteLine("         \"nxm\": false,");
								found = true;
								continue;
							}
						}
						else
						{
							if (tempLine.IndexOf("\"nxm\": ") >= 0)
                            {
                                continue;
                            }
                        }

						chromeLocalState.WriteLine(tempLine);
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
