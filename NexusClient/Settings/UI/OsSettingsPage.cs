namespace Nexus.Client.Settings.UI
{
    using System;
    using System.Windows.Forms;

    using Util;

    /// <summary>
    /// A view allowing the editing of general settings.
    /// </summary>
    public partial class OsSettingsPage : UserControl, ISettingsGroupView
	{
		private bool _toolTipShown;
		
		#region Constructors

		/// <summary>
		/// A sinmple consturctor that initializes the object with the given values.
		/// </summary>
		/// <param name="settings">The settings group whose settings will be editable with this view.</param>
		public OsSettingsPage(OsSettingsGroup settings)
		{
			SettingsGroup = settings;
			InitializeComponent();
            
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
	}
}
