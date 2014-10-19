using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using System.Security.Principal;
using Nexus.Client.Util;
using Nexus.UI.Controls;
using Nexus.Client.UI;

namespace Nexus.Client.Games.Settings
{
	/// <summary>
	/// A control that encapsulates the management of the critical directory settings.
	/// </summary>
	public partial class RequiredDirectoriesControl : ManagedFontUserControl
	{
		private RequiredDirectoriesControlVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public RequiredDirectoriesControlVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				m_vmlViewModel.LoadSettings();
				BindingHelper.CreateFullBinding(tbxInstallInfo, () => tbxInstallInfo.Text, m_vmlViewModel, () => m_vmlViewModel.InstallInfoDirectory);
				BindingHelper.CreateFullBinding(tbxModDirectory, () => tbxModDirectory.Text, m_vmlViewModel, () => m_vmlViewModel.ModDirectory);
				if (m_vmlViewModel.RequiredTool)
					BindingHelper.CreateFullBinding(tbxToolDirectory, () => tbxToolDirectory.Text, m_vmlViewModel, () => m_vmlViewModel.ToolDirectory);
				BindingHelper.CreateFullBinding(tbxVirtualDirectory, () => tbxVirtualDirectory.Text, m_vmlViewModel, () => m_vmlViewModel.VirtualDirectory);
				BindingHelper.CreateFullBinding(tbxLinkDirectory, () => tbxLinkDirectory.Text, m_vmlViewModel, () => m_vmlViewModel.LinkDirectory);
				m_vmlViewModel.Errors.ErrorChanged -= new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
				m_vmlViewModel.Errors.ErrorChanged += new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
				
				lblModPrompt.Text = String.Format(lblModPrompt.Text, ViewModel.GameModeName);
				lblInstallInfoPrompt.Text = String.Format(lblInstallInfoPrompt.Text, ViewModel.GameModeName);
				lblToolPrompt.Text = String.Format(lblToolPrompt.Text, ViewModel.RequiredToolName);
				lblVirtualPrompt.Text = "Select the folder where NMM will place extracted and installed mods (NMM will automatically create a 'Virtual' folder in it).";
				ckbUseMultiHDInstall.Text = "Enable Multi-HD install mode";
				lblLinkPrompt.Text = "Select the folder where NMM will place extracted files that need to be on the same hard-drive as your game (NMM will automatically create a 'NMMLink' folder in it).";

				string strInfo = "[Virtual Install] Select the folder where NMM will place extracted and installed mods." + Environment.NewLine +
				"If you are using the Simple Install Method then this folder MUST be located on the same hard-drive where you normally install mods for this game." + Environment.NewLine +
				"If you are NOT using the Simple Install Method then this folder can be located anywhere, on any hard-drive of your choosing." + Environment.NewLine + Environment.NewLine + Environment.NewLine +
				"[Enable Multi-HD install mode] lets you place extracted and installed mods on a different hard-drive to the one your games are installed on," + 
				"allowing you to save space on your game drive." + Environment.NewLine +
				"NOTE: Some file extensions like .exe, .esp and .esm files need to be placed on the game's hard-drive irrespsective of this setting." + Environment.NewLine +
				"NOTE: Placing your mods on a slow hard-drive may degrade your game performance and loading times." + Environment.NewLine + Environment.NewLine + Environment.NewLine +
				"[Required Link] Select the folder where NMM will place extracted files that need to be on the same hard-drive as your games, such as .exe, .esp and .esm files." + Environment.NewLine +
				"NOTE: This folder MUST be on the same hard-drive where you normally install mods for this game." + Environment.NewLine;
				lbInfo.Text = strInfo;
				lbInfo.Visible = true;
				
				lblToolDirectoryLabel.Visible = ViewModel.RequiredTool;
				tbxToolDirectory.Visible = ViewModel.RequiredTool;
				tbxToolDirectory.Enabled = ViewModel.RequiredTool;
				lblToolPrompt.Visible = ViewModel.RequiredTool;
				butSelectToolDirectory.Visible = ViewModel.RequiredTool;
				butSelectToolDirectory.Enabled = ViewModel.RequiredTool;

				lblVirtualDirectoryLabel.Visible = ViewModel.OptionalSettings;
				tbxVirtualDirectory.Visible = ViewModel.OptionalSettings;
				tbxVirtualDirectory.Enabled = ViewModel.OptionalSettings;
				lblVirtualPrompt.Visible = ViewModel.OptionalSettings;
				butSelectVirtualDirectory.Visible = ViewModel.OptionalSettings;
				butSelectVirtualDirectory.Enabled = ViewModel.OptionalSettings;
				grbMulti.Enabled = (new WindowsPrincipal(WindowsIdentity.GetCurrent())).IsInRole(WindowsBuiltInRole.Administrator);
				ckbUseMultiHDInstall.Visible = ViewModel.OptionalSettings;
				ckbUseMultiHDInstall.Checked = (grbMulti.Enabled) ? ViewModel.MultiHDInstall : false;
				lblLinkDirectoryLabel.Visible = ViewModel.OptionalSettings;
				tbxLinkDirectory.Visible = ViewModel.OptionalSettings;
				tbxLinkDirectory.Enabled = ckbUseMultiHDInstall.Checked;
				lblLinkPrompt.Visible = ViewModel.OptionalSettings;
				butSelectLinkDirectory.Visible = ViewModel.OptionalSettings;
				butSelectLinkDirectory.Enabled = ckbUseMultiHDInstall.Checked;

				ViewModel.MultiHDInstall = false;

				lbInfo.MaximumSize = new System.Drawing.Size(grbInfo.Width - 10, 0);
				lblVirtualPrompt.MaximumSize = new System.Drawing.Size(grbMulti.Width - 10, 0);
				lblLinkPrompt.MaximumSize = new System.Drawing.Size(grbMulti.Width - 10, 0);
			}
		}

		/// <summary>
		/// Gets or sets the label for the mod directory path.
		/// </summary>
		/// <value>The label for the mod directory path.</value>
		public string ModDirectoryLabel
		{
			get
			{
				return lblModDirectoryLabel.Text;
			}
			set
			{
				lblModDirectoryLabel.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the label for the install info path.
		/// </summary>
		/// <value>The label for the install info path.</value>
		public string InstallInfoLabel
		{
			get
			{
				return lblInstallInfoLabel.Text;
			}
			set
			{
				lblInstallInfoLabel.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the label for the tool path.
		/// </summary>
		/// <value>The label for the tool path.</value>
		public string ToolLabel
		{
			get
			{
				return lblToolDirectoryLabel.Text;
			}
			set
			{
				lblToolDirectoryLabel.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the label for the Virtual path.
		/// </summary>
		/// <value>The label for the Virtual path.</value>
		public string VirtualLabel
		{
			get
			{
				return lblVirtualDirectoryLabel.Text;
			}
			set
			{
				lblVirtualDirectoryLabel.Text = value;
			}
		}

		/// <summary>
		/// Gets or sets the label for the Link path.
		/// </summary>
		/// <value>The label for the Link path.</value>
		public string LinkLabel
		{
			get
			{
				return lblLinkDirectoryLabel.Text;
			}
			set
			{
				lblLinkDirectoryLabel.Text = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public RequiredDirectoriesControl()
		{
			InitializeComponent();
		}

		#endregion

		#region Validation

		/// <summary>
		/// Handles the <see cref="ErrorContainer.ErrorChanged"/> event of the validation
		/// errors object.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ErrorEventArguments"/> describing the event arguments.</param>
		private void Errors_ErrorChanged(object sender, ErrorEventArguments e)
		{
			if (e.Property.Equals(ObjectHelper.GetPropertyName<RequiredDirectoriesControlVM>(x => x.InstallInfoDirectory)))
				erpErrors.SetError(butSelectInfoDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<RequiredDirectoriesControlVM>(x => x.ModDirectory)))
				erpErrors.SetError(butSelectModDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<RequiredDirectoriesControlVM>(x => x.ToolDirectory)))
				erpErrors.SetError(butSelectToolDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<RequiredDirectoriesControlVM>(x => x.VirtualDirectory)))
				erpErrors.SetError(butSelectVirtualDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<RequiredDirectoriesControlVM>(x => x.LinkDirectory)))
				erpErrors.SetError(butSelectLinkDirectory, e.Error);

			if (e.Property.Equals("WARNING"))
			{
				lbWarning.Text = "Warning: " + e.Error;
				lbWarning.Visible = true;
			}
			else
				lbWarning.Visible = false;
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select mod directory button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the mod directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectModDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxModDirectory.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxModDirectory.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select tool directory button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the tool directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectToolDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxToolDirectory.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxToolDirectory.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select Virtual directory button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the Virtual directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectVirtualDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxVirtualDirectory.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxVirtualDirectory.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select Link directory button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the Link directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectLinkDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxLinkDirectory.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxLinkDirectory.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select install info button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for the install info directory.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectInfoDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxInstallInfo.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxInstallInfo.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		private void ckbUseMultiHDInstall_CheckedChanged(object sender, EventArgs e)
		{
			lblLinkDirectoryLabel.Enabled = ckbUseMultiHDInstall.Checked;
			tbxLinkDirectory.Enabled = ckbUseMultiHDInstall.Checked;
			lblLinkPrompt.Enabled = ckbUseMultiHDInstall.Checked;
			butSelectLinkDirectory.Enabled = ckbUseMultiHDInstall.Checked;
			ViewModel.MultiHDInstall = ckbUseMultiHDInstall.Checked;
		}
	}
}
