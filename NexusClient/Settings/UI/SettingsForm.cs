using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.UI;

namespace Nexus.Client.Settings.UI
{
	/// <summary>
	/// The settings form.
	/// </summary>
	public partial class SettingsForm : ManagedFontForm
	{
		private SettingsFormVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected SettingsFormVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				Icon = m_vmlViewModel.GameMode.ModeTheme.Icon;

				foreach (ISettingsGroupView sgvSettingsPage in m_vmlViewModel.SettingsGroups)
				{
					Control ctlSettingsPage = sgvSettingsPage as Control;
					if (ctlSettingsPage == null)
						continue;

					SettingsGroup sgpSettingsGroup = sgvSettingsPage.SettingsGroup;
					tbcTabs.TabPages.Add(sgpSettingsGroup.Title, sgpSettingsGroup.Title);
					ctlSettingsPage.Dock = DockStyle.Fill;
					tbcTabs.TabPages[sgpSettingsGroup.Title].UseVisualStyleBackColor = true;
					tbcTabs.TabPages[sgpSettingsGroup.Title].Controls.Add(ctlSettingsPage);
					tbcTabs.TabPages[sgpSettingsGroup.Title].Tag = sgpSettingsGroup;
					tbcTabs.TabPages[sgpSettingsGroup.Title].Padding = new Padding(3);
					sgpSettingsGroup.Load();
				}
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		public SettingsForm(SettingsFormVM p_vmlViewModel)
		{
			InitializeComponent();
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Hanldes the <see cref="Control.Click"/> event of the OK button.
		/// </summary>
		/// <remarks>
		/// This persists the settings.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butOK_Click(object sender, EventArgs e)
		{
			if (!ViewModel.Save())
				return;

			DialogResult = DialogResult.OK;
		}

		#region Settings Persistence

		/// <summary>
		/// Persists the game-mode specific settings.
		/// </summary>
		/// <returns><c>true</c> if ettings were saved;
		/// <c>false</c> otherwise.</returns>
		protected bool SaveGameModeSettings()
		{
			/*bool booIsValid = true;
			bool booIsPageValid = true;
			foreach (TabPage tpgSettings in tbcTabs.TabPages)
				if (tpgSettings.Tag is SettingsPage)
				{
					booIsPageValid = ((SettingsPage)tpgSettings.Tag).SaveSettings();
					booIsValid &= booIsPageValid;
					if (!booIsPageValid)
						tbcTabs.SelectedTab = tpgSettings;
				}
			return booIsValid;
			 * */
			return true;
		}

		#endregion
	}
}
