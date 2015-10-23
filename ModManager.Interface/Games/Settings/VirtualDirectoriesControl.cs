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
	public partial class VirtualDirectoriesControl : ManagedFontUserControl
	{
		private VirtualDirectoriesControlVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public VirtualDirectoriesControlVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				m_vmlViewModel.LoadSettings();
				BindingHelper.CreateFullBinding(tbxVirtualDirectory, () => tbxVirtualDirectory.Text, m_vmlViewModel, () => m_vmlViewModel.VirtualDirectory);
				BindingHelper.CreateFullBinding(tbxLinkDirectory, () => tbxLinkDirectory.Text, m_vmlViewModel, () => m_vmlViewModel.LinkDirectory);
				m_vmlViewModel.Errors.ErrorChanged -= new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
				m_vmlViewModel.Errors.ErrorChanged += new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
				
				lblVirtualPrompt.Text = "Select the folder where NMM will place extracted and installed mods (NMM will automatically create a 'VirtualInstall' folder in it).";
				ckbUseMultiHDInstall.Text = "Enable Multi-HD install mode";
				lblLinkPrompt.Text = "Select the folder where NMM will place extracted files that need to be on the same hard-drive as your game (NMM will automatically create a 'NMMLink' folder in it).";

				string strInfo = "[Virtual Install] This is the default and recommended installation method. It REQUIRES this folder to be located on the same hard-drive where you normally install mods for this game." + Environment.NewLine +
				"If you want this folder to be located anywhere, on any hard-drive of your choosing, enable the Multi-HD install mode." + Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine + Environment.NewLine +
				"[Enable Multi-HD install mode] lets you place extracted mods on a different hard-drive to the one your game mods are usually installed on," +
				"allowing you to save space on your game drive." + Environment.NewLine +
				"NOTE: Some file extensions like .exe, .esp and .esm files need to be placed on the game's hard-drive irrespsective of this setting." + Environment.NewLine +
				"NOTE: Placing mods on a slow HD may degrade game performance and loading times." + Environment.NewLine + Environment.NewLine + Environment.NewLine +
				"[Required Link] Select the folder where NMM will place extracted files that need to be on the same HD as your games (.exe, .esp and .esm files)" + Environment.NewLine +
				"NOTE: This folder MUST be on the same HD where you normally install mods for this game." + Environment.NewLine;
				lbInfo.Text = strInfo;
				lbInfo.Visible = true;

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

				lblVirtualPrompt.MaximumSize = new System.Drawing.Size(grbMulti.Width - 10, 0);
				lblLinkPrompt.MaximumSize = new System.Drawing.Size(grbMulti.Width - 10, 0);
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
		public VirtualDirectoriesControl()
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
			if (e.Property.Equals(ObjectHelper.GetPropertyName<VirtualDirectoriesControlVM>(x => x.VirtualDirectory)))
				erpErrors.SetError(butSelectVirtualDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<VirtualDirectoriesControlVM>(x => x.LinkDirectory)))
				erpErrors.SetError(butSelectLinkDirectory, e.Error);
		}

		#endregion

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
