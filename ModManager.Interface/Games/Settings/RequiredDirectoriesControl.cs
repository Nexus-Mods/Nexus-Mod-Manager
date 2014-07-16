using System;
using System.IO;
using System.Windows.Forms;
using System.ComponentModel;
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
				m_vmlViewModel.Errors.ErrorChanged -= new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
				m_vmlViewModel.Errors.ErrorChanged += new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
				
				lblModPrompt.Text = String.Format(lblModPrompt.Text, ViewModel.GameModeName);
				lblInstallInfoPrompt.Text = String.Format(lblInstallInfoPrompt.Text, ViewModel.GameModeName);
				lblToolPrompt.Text = String.Format(lblToolPrompt.Text, ViewModel.RequiredToolName);
				lblToolDirectoryLabel.Visible = ViewModel.RequiredTool;
				tbxToolDirectory.Visible = ViewModel.RequiredTool;
				tbxToolDirectory.Enabled = ViewModel.RequiredTool;
				lblToolPrompt.Visible = ViewModel.RequiredTool;
				butSelectToolDirectory.Visible = ViewModel.RequiredTool;
				butSelectToolDirectory.Enabled = ViewModel.RequiredTool;
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
	}
}
