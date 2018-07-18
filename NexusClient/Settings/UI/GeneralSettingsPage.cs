namespace Nexus.Client.Settings.UI
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Drawing;
    using System.Windows.Forms;

    using Util;

    /// <summary>
    /// A view allowing the editing of general settings.
    /// </summary>
    public partial class GeneralSettingsPage : UserControl, ISettingsGroupView
	{
		private bool _toolTipShown;

		private enum DaysInterval
		{
			One = 1,
			Two,
			Three,
			Four,
			Five,
			Six,
			Seven
		}
		
		#region Constructors

		/// <summary>
		/// A sinmple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="settings">The settings group whose settings will be editable with this view.</param>
		public GeneralSettingsPage(GeneralSettingsGroup settings)
		{
			SettingsGroup = settings;
			InitializeComponent();

			cbxProgramUpdateCheckInterval.DataSource = Enum.GetValues(typeof(DaysInterval))
				.Cast<DaysInterval>()
				.Select(p => new { Value = (int)p, Key = p.ToString() })
				.ToList();
			cbxProgramUpdateCheckInterval.DisplayMember = "Key";
			cbxProgramUpdateCheckInterval.ValueMember = "Value";

			foreach (var fasFileAssociation in settings.FileAssociations)
			{
			    var ckbFileAssociation = new CheckBox
			    {
			        Tag = fasFileAssociation,
			        Text = $"Associate with {fasFileAssociation.Description} (*{fasFileAssociation.Extension}) files",
			        AutoSize = true
			    };

			    BindingHelper.CreateFullBinding(ckbFileAssociation, () => ckbFileAssociation.Checked, fasFileAssociation, () => fasFileAssociation.IsAssociated);
				flpFileAssociations.Controls.Add(ckbFileAssociation);
			}

			BindingHelper.CreateFullBinding(ckbShellExtensions, () => ckbShellExtensions.Checked, settings, () => settings.AddShellExtensions);
			BindingHelper.CreateFullBinding(ckbAssociateURL, () => ckbAssociateURL.Checked, settings, () => settings.AssociateNxmUrl);

			BindingHelper.CreateFullBinding(ckbCheckForUpdates, () => ckbCheckForUpdates.Checked, settings, () => settings.CheckForUpdatesOnStartup);
			BindingHelper.CreateFullBinding(ckbAddMissingInfo, () => ckbAddMissingInfo.Checked, settings, () => settings.AddMissingModInfo);
			BindingHelper.CreateFullBinding(ckbScanSubfolders, () => ckbScanSubfolders.Checked, settings, () => settings.ScanSubfoldersForMods);
			BindingHelper.CreateFullBinding(ckbOverrideLocalNames, () => ckbOverrideLocalNames.Checked, settings, () => settings.OverrideLocalModNames);
			BindingHelper.CreateFullBinding(ckbCloseManagerAfterGameLaunch, () => ckbCloseManagerAfterGameLaunch.Checked, settings, () => settings.CloseModManagerAfterGameLaunch);
			BindingHelper.CreateFullBinding(ckbShowSidePanel, () => ckbShowSidePanel.Checked, settings, () => settings.ShowSidePanel);
			BindingHelper.CreateFullBinding(ckbSkipReadmeFiles, () => ckbSkipReadmeFiles.Checked, settings, () => settings.SkipReadmeFiles);
			BindingHelper.CreateFullBinding(ckbHideModUpdateWarningIcon, () => ckbHideModUpdateWarningIcon.Checked, settings, () => settings.HideModUpdateWarningIcon);

			BindingHelper.CreateFullBinding(cbxProgramUpdateCheckInterval, () => cbxProgramUpdateCheckInterval.SelectedValue, settings, () => settings.UpdateCheckInterval);

			BindingHelper.CreateFullBinding(tbxTraceLogDirectory, () => tbxTraceLogDirectory.Text, settings, () => settings.TraceLogPath);
			BindingHelper.CreateFullBinding(tbxTempPathDirectory, () => tbxTempPathDirectory.Text, settings, () => settings.TempPath);

			try
			{
				if (!settings.CanAssociateFiles)
				{
					gbxAssociations.Enabled = false;
					ttpTip.SetToolTip(gbxAssociations, $"Run {settings.EnvironmentInfo.Settings.ModManagerName} as Administrator to change these settings.");
				}
			}
			catch(MissingMethodException)
			{
				var strErrorMessage = string.Format("Looks like you have a broken or incomplete .Net Framework!" + Environment.NewLine + 
					"You need to install .NetFramework 4.5.2 or 4.6 . " + Environment.NewLine + 
					"You could alse be required to download the latest Windows updates" +Environment.NewLine + Environment.NewLine +
					"{0} will be unable to run until you do that and will now close.", SettingsGroup.EnvironmentInfo.Settings.ModManagerName);
				MessageBox.Show(strErrorMessage, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);

			    Environment.Exit(0);
			}

			ckbCloseManagerAfterGameLaunch.Text = string.Format(ckbCloseManagerAfterGameLaunch.Text, settings.EnvironmentInfo.Settings.ModManagerName);
		}

		#endregion

		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; }

		#endregion

		#region Tool Tip

		/// <summary>
		/// Handles the <see cref="Control.MouseHover"/> event of the general settings flow panel.
		/// </summary>
		/// <remarks>
		/// This displays the tool tip for the file associations group box when it is disabled.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void flpGeneral_MouseHover(object sender, EventArgs e)
		{
			if (!gbxAssociations.Enabled && gbxAssociations.ClientRectangle.Contains(gbxAssociations.PointToClient(Cursor.Position)))
			{
				_toolTipShown = true;
				var pntToolTipLocation = gbxAssociations.PointToClient(Cursor.Position);
				ttpTip.Show(ttpTip.GetToolTip(gbxAssociations), gbxAssociations, pntToolTipLocation.X, pntToolTipLocation.Y + Cursor.Current.Size.Height);
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.MouseMove"/> event of the general settings flow panel.
		/// </summary>
		/// <remarks>
		/// This hides the tool tip for the file associations group box when appropriate.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="MouseEventArgs"/> describing the event arguments.</param>
		private void flpGeneral_MouseMove(object sender, MouseEventArgs e)
		{
			if (_toolTipShown && !gbxAssociations.ClientRectangle.Contains(gbxAssociations.PointToClient(Cursor.Position)))
			{
				_toolTipShown = false;
				ttpTip.Hide(gbxAssociations);
			}
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select working directory button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the selection of the working directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectTraceLogDirectory_Click(object sender, EventArgs e)
		{
			fbdTraceLogDirectory.SelectedPath = tbxTraceLogDirectory.Text;

		    if (fbdTraceLogDirectory.ShowDialog(FindForm()) == DialogResult.OK)
			{
				tbxTraceLogDirectory.Text = fbdTraceLogDirectory.SelectedPath;
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select working directory button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the selection of the working directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectTempPathDirectory_Click(object sender, EventArgs e)
		{
			fbdTempPathDirectory.SelectedPath = tbxTempPathDirectory.Text;

		    if (fbdTempPathDirectory.ShowDialog(this.FindForm()) == DialogResult.OK)
			{
				var strPath = Path.GetFileName(fbdTempPathDirectory.SelectedPath);
				if (string.IsNullOrEmpty(strPath))
                {
                    strPath = Path.GetDirectoryName(fbdTempPathDirectory.SelectedPath);
                }

                if (string.IsNullOrWhiteSpace(strPath) || (!(strPath.ToLower().Contains("temp"))))
                {
                    tbxTempPathDirectory.Text = Path.Combine(fbdTempPathDirectory.SelectedPath, "Temp");
                }
                else
                {
                    tbxTempPathDirectory.Text = fbdTempPathDirectory.SelectedPath;
                }

                ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.KeyUp"/> event of the select working directory button.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void tbxTempPathDirectory_LostFocus(object sender, EventArgs e)
		{
			if (!Equals(tbxTempPathDirectory.Text, SettingsGroup.EnvironmentInfo.TemporaryPath))
			{
				var strPath = Path.GetFileName(tbxTempPathDirectory.Text);

				if (string.IsNullOrEmpty(strPath))
                {
                    strPath = Path.GetDirectoryName(tbxTempPathDirectory.Text);
                }

                if (!string.IsNullOrEmpty(strPath))
				{
					if (!(strPath.ToLower().Contains("temp")))
                    {
                        tbxTempPathDirectory.Text = Path.Combine(tbxTempPathDirectory.Text, "Temp");
                    }
                }
			}

			ValidateChildren();
		}
	}
}
