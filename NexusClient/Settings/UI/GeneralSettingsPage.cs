using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.Util;

namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// A view allowing the editing of general settings.
	/// </summary>
	public partial class GeneralSettingsPage : UserControl, ISettingsGroupView
	{
		private bool booToolTipShown = false;

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
		/// <param name="p_gsgSettings">The settings group whose settings will be editable with this view.</param>
		public GeneralSettingsPage(GeneralSettingsGroup p_gsgSettings)
		{
			SettingsGroup = p_gsgSettings;
			InitializeComponent();

			cbxProgramUpdateCheckInterval.DataSource = Enum.GetValues(typeof(DaysInterval))
				.Cast<DaysInterval>()
				.Select(p => new { Value = (int)p, Key = p.ToString() })
				.ToList();
			cbxProgramUpdateCheckInterval.DisplayMember = "Key";
			cbxProgramUpdateCheckInterval.ValueMember = "Value";

			foreach (GeneralSettingsGroup.FileAssociationSetting fasFileAssociation in p_gsgSettings.FileAssociations)
			{
				CheckBox ckbFileAssociation = new CheckBox();
				ckbFileAssociation.Tag = fasFileAssociation;
				ckbFileAssociation.Text = String.Format("Associate with {0} (*{1}) files", fasFileAssociation.Description, fasFileAssociation.Extension);
				ckbFileAssociation.AutoSize = true;
				BindingHelper.CreateFullBinding(ckbFileAssociation, () => ckbFileAssociation.Checked, fasFileAssociation, () => fasFileAssociation.IsAssociated);
				flpFileAssociations.Controls.Add(ckbFileAssociation);
			}
			BindingHelper.CreateFullBinding(ckbShellExtensions, () => ckbShellExtensions.Checked, p_gsgSettings, () => p_gsgSettings.AddShellExtensions);
			BindingHelper.CreateFullBinding(ckbAssociateURL, () => ckbAssociateURL.Checked, p_gsgSettings, () => p_gsgSettings.AssociateNxmUrl);

			BindingHelper.CreateFullBinding(ckbCheckForUpdates, () => ckbCheckForUpdates.Checked, p_gsgSettings, () => p_gsgSettings.CheckForUpdatesOnStartup);
			BindingHelper.CreateFullBinding(ckbAddMissingInfo, () => ckbAddMissingInfo.Checked, p_gsgSettings, () => p_gsgSettings.AddMissingModInfo);
			BindingHelper.CreateFullBinding(ckbScanSubfolders, () => ckbScanSubfolders.Checked, p_gsgSettings, () => p_gsgSettings.ScanSubfoldersForMods);
			BindingHelper.CreateFullBinding(ckbCloseManagerAfterGameLaunch, () => ckbCloseManagerAfterGameLaunch.Checked, p_gsgSettings, () => p_gsgSettings.CloseModManagerAfterGameLaunch);
			BindingHelper.CreateFullBinding(ckbShowSidePanel, () => ckbShowSidePanel.Checked, p_gsgSettings, () => p_gsgSettings.ShowSidePanel);
			BindingHelper.CreateFullBinding(ckbSkipReadmeFiles, () => ckbSkipReadmeFiles.Checked, p_gsgSettings, () => p_gsgSettings.SkipReadmeFiles);
			BindingHelper.CreateFullBinding(ckbHideModUpdateWarningIcon, () => ckbHideModUpdateWarningIcon.Checked, p_gsgSettings, () => p_gsgSettings.HideModUpdateWarningIcon);

			BindingHelper.CreateFullBinding(cbxProgramUpdateCheckInterval, () => cbxProgramUpdateCheckInterval.SelectedValue, p_gsgSettings, () => p_gsgSettings.UpdateCheckInterval);

			BindingHelper.CreateFullBinding(tbxTraceLogDirectory, () => tbxTraceLogDirectory.Text, p_gsgSettings, () => p_gsgSettings.TraceLogPath);
			BindingHelper.CreateFullBinding(tbxTempPathDirectory, () => tbxTempPathDirectory.Text, p_gsgSettings, () => p_gsgSettings.TempPath);

			try
			{
				if (!p_gsgSettings.CanAssociateFiles)
				{
					gbxAssociations.Enabled = false;
					ttpTip.SetToolTip(gbxAssociations, String.Format("Run {0} as Administrator to change these settings.", p_gsgSettings.EnvironmentInfo.Settings.ModManagerName));
				}
			}
			catch(MissingMethodException ex)
			{
				string strErrorMessage = string.Format("Looks like you have a broken or incomplete .Net Framework!" + Environment.NewLine + 
					"You need to install .NetFramework 4.6 . " + Environment.NewLine + 
					"You could alse be required to download the latest Windows updates" +Environment.NewLine + Environment.NewLine +
					"{0} will be unable to run until you do that and will now close.", SettingsGroup.EnvironmentInfo.Settings.ModManagerName);
				MessageBox.Show(strErrorMessage, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				System.Environment.Exit(0);
			}

			ckbCloseManagerAfterGameLaunch.Text = String.Format(ckbCloseManagerAfterGameLaunch.Text, p_gsgSettings.EnvironmentInfo.Settings.ModManagerName);
		}

		#endregion

		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; private set; }

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
				booToolTipShown = true;
				Point pntToolTipLocation = gbxAssociations.PointToClient(Cursor.Position);
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
			if (booToolTipShown && !gbxAssociations.ClientRectangle.Contains(gbxAssociations.PointToClient(Cursor.Position)))
			{
				booToolTipShown = false;
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
			if (fbdTraceLogDirectory.ShowDialog(this.FindForm()) == DialogResult.OK)
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
				string strPath = Path.GetFileName(fbdTempPathDirectory.SelectedPath);
				if (String.IsNullOrEmpty(strPath))
					strPath = Path.GetDirectoryName(fbdTempPathDirectory.SelectedPath);

				if (!(strPath.ToLower().Contains("temp")))
					tbxTempPathDirectory.Text = Path.Combine(fbdTempPathDirectory.SelectedPath, "Temp");
				else
					tbxTempPathDirectory.Text = fbdTempPathDirectory.SelectedPath;

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
			if (!Path.Equals(tbxTempPathDirectory.Text, SettingsGroup.EnvironmentInfo.TemporaryPath))
			{
				string strPath = Path.GetFileName(tbxTempPathDirectory.Text);

				if (String.IsNullOrEmpty(strPath))
					strPath = Path.GetDirectoryName(tbxTempPathDirectory.Text);
				if (!String.IsNullOrEmpty(strPath))
				{
					if (!(strPath.ToLower().Contains("temp")))
						tbxTempPathDirectory.Text = Path.Combine(tbxTempPathDirectory.Text, "Temp");
				}
			}
			ValidateChildren();
		}
	}
}
