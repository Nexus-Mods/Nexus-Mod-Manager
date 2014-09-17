using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Nexus.Client.ModRepositories;
using Nexus.Client.Util;
using Nexus.UI.Controls;


namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// A view allowing the editing of mod options.
	/// </summary>
	public partial class SupportedToolsSettingsPage : UserControl, ISettingsGroupView
	{
		#region Constructors

		/// <summary>
		/// A sinmple consturctor that initializes teh object with the given values.
		/// </summary>
		/// <param name="p_dsgSettings">The settings group whose settings will be editable with this view.</param>
		public SupportedToolsSettingsPage(SupportedToolsSettingsGroup p_stsSettings)
		{
			SettingsGroup = p_stsSettings;
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
