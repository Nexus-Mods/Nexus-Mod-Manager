using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.UI.Controls;
using Nexus.Client.Settings;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;



namespace Nexus.Client.Games.SkyrimSE.Settings.UI
{
	/// <summary>
	/// A view allowing the editing of mod options.
	/// </summary>
	public partial class SupportedToolsSettingsPage : UserControl, ISettingsGroupView
	{
		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		/// <value>This is for comptaiblity with the designer.</value>
		protected SupportedToolsSettingsPage()
		{
			InitializeComponent();
		}

		/// <summary>
		/// A sinmple consturctor that initializes teh object with the given values.
		/// </summary>
		/// <param name="p_dsgSettings">The settings group whose settings will be editable with this view.</param>
		public SupportedToolsSettingsPage(SupportedToolsSettingsGroup p_stsSettings)
			:this()
		{
			SettingsGroup = p_stsSettings;

			BindingHelper.CreateFullBinding(tbxBOSS, () => tbxBOSS.Text, p_stsSettings, () => p_stsSettings.BOSSDirectory);
			BindingHelper.CreateFullBinding(tbxLOOT, () => tbxLOOT.Text, p_stsSettings, () => p_stsSettings.LOOTDirectory);
			BindingHelper.CreateFullBinding(tbxWryeBashDirectory, () => tbxWryeBashDirectory.Text, p_stsSettings, () => p_stsSettings.WryeBashDirectory);
			BindingHelper.CreateFullBinding(tbxFNIS, () => tbxFNIS.Text, p_stsSettings, () => p_stsSettings.FNISDirectory);
			BindingHelper.CreateFullBinding(tbxBS, () => tbxBS.Text, p_stsSettings, () => p_stsSettings.BSDirectory);
			BindingHelper.CreateFullBinding(tbxSSEEdit, () => tbxSSEEdit.Text, p_stsSettings, () => p_stsSettings.SSEEditDirectory);

			p_stsSettings.Errors.ErrorChanged -= new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);
			p_stsSettings.Errors.ErrorChanged += new EventHandler<ErrorEventArguments>(Errors_ErrorChanged);

			lblBOSSPrompt.Text = String.Format(lblBOSSPrompt.Text, p_stsSettings.GameModeName);
			lblLOOTPrompt.Text = String.Format(lblLOOTPrompt.Text, p_stsSettings.GameModeName);
			lblWryeBashPrompt.Text = String.Format(lblWryeBashPrompt.Text, p_stsSettings.GameModeName);
			lblFNISPrompt.Text = String.Format(lblFNISPrompt.Text, p_stsSettings.GameModeName);
			lblBSPrompt.Text = String.Format(lblBSPrompt.Text, p_stsSettings.GameModeName);
			lblSSEEditPrompt.Text = String.Format(lblSSEEditPrompt.Text, p_stsSettings.GameModeName);
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
			if (e.Property.Equals(ObjectHelper.GetPropertyName<SupportedToolsSettingsGroup>(x => x.BOSSDirectory)))
				erpErrors.SetError(butSelectBOSSDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<SupportedToolsSettingsGroup>(x => x.WryeBashDirectory)))
				erpErrors.SetError(butSelectWryeBashDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<SupportedToolsSettingsGroup>(x => x.LOOTDirectory)))
				erpErrors.SetError(butSelectLOOTDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<SupportedToolsSettingsGroup>(x => x.BSDirectory)))
				erpErrors.SetError(butSelectBSDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<SupportedToolsSettingsGroup>(x => x.FNISDirectory)))
				erpErrors.SetError(butSelectFNISDirectory, e.Error);
			else if (e.Property.Equals(ObjectHelper.GetPropertyName<SupportedToolsSettingsGroup>(x => x.SSEEditDirectory)))
				erpErrors.SetError(butSelectSSEEditDirectory, e.Error);
		}

		#endregion


		#region ISettingsGroupView Members

		/// <summary>
		/// Gets the <see cref="SettingsGroup"/> whose settings will be editable with this view.
		/// </summary>
		/// <value>The <see cref="SettingsGroup"/> whose settings will be editable with this view.</value>
		public SettingsGroup SettingsGroup { get; private set; }

		#endregion

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select BOSS button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for BOSS.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectBOSSDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxBOSS.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxBOSS.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select LOOT button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for LOOT.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectLOOTDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxLOOT.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxLOOT.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select Wrye Bash button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for Wrye Bash.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectWryeBashDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxWryeBashDirectory.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxWryeBashDirectory.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the selectFNIS button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for FNIS.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectFNISDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxFNIS.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxFNIS.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the selectBS button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for BodySlide.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectBSDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxBS.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxBS.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the select SSEEdit button.
		/// </summary>
		/// <remarks>
		/// This opens the folder selection dialog for SSEEdit.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butSelectSSEEditDirectory_Click(object sender, EventArgs e)
		{
			fbdDirectory.SelectedPath = tbxSSEEdit.Text;
			if (fbdDirectory.ShowDialog(this) == DialogResult.OK)
			{
				tbxSSEEdit.Text = fbdDirectory.SelectedPath;
				//force the data binding on the textbox to push the value to the bound view model
				ValidateChildren();
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
