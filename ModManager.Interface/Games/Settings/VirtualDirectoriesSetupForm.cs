using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.UI;
using Nexus.Client.ModManagement;
using Nexus.UI.Controls;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.Settings
{
	/// <summary>
	/// This is the setup form for the Grimrock game mode.
	/// </summary>
	public partial class VirtualDirectoriesSetupForm : ManagedFontForm, IView
	{
		private VirtualDirectoriesSetupVM m_vmlViewModel = null;
		private ComboBox m_cbxInstallMethod;
		private Label m_lblInstallMethodDescription;
		private bool m_booUpdatingInstallMethod;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public VirtualDirectoriesSetupVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			private set
			{
				m_vmlViewModel = value as VirtualDirectoriesSetupVM;
				if (m_vmlViewModel == null)
					throw new ArgumentException("The given view model must be a VirtualDirectoriesSetupVM. Type found: " + value.GetType().FullName);
				lblTitle.Text = String.Format(lblTitle.Text, m_vmlViewModel.GameModeDescriptor.Name);
				Text = String.Format(Text, m_vmlViewModel.GameModeDescriptor.Name);
				rdcDirectories.ViewModel = m_vmlViewModel.VirtualDirectoriesControlVM;
				RefreshInstallMethodSelector();
				ApplyTheme(m_vmlViewModel.GameModeDescriptor.ModeTheme);
			}
		}

		#endregion

		#region Contructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		protected VirtualDirectoriesSetupForm()
		{
			InitializeComponent();
			InitializeInstallMethodSelector();
			string setupTitle = LanguageManager.GetFormat("GameStorage.Manager.Title", "{0} Game Storage Manager");
			lblTitle.Text = setupTitle;
			Text = setupTitle;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public VirtualDirectoriesSetupForm(VirtualDirectoriesSetupVM p_vmlViewModel)
			: this()
		{
			ViewModel = p_vmlViewModel;
		}

		#endregion

		/// <summary>
		/// Adds the per-game install-method selector without changing the existing storage-folder controls.
		/// </summary>
		private void InitializeInstallMethodSelector()
		{
			var panel = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 82,
				Padding = new Padding(12, 8, 12, 8)
			};
			var label = new Label
			{
				AutoSize = true,
				Left = 12,
				Top = 12,
				Text = LanguageManager.Get("GameStorage.InstallMethod.Label", "Install Method:")
			};
			m_cbxInstallMethod = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList,
				Left = 112,
				Top = 8,
				Width = 150
			};
			m_cbxInstallMethod.Items.Add(LanguageManager.Get("GameStorage.InstallMethod.Virtual", "Virtual Install"));
			m_cbxInstallMethod.Items.Add(LanguageManager.Get("GameStorage.InstallMethod.Direct", "Direct Install"));
			m_cbxInstallMethod.SelectedIndexChanged += InstallMethod_SelectedIndexChanged;

			m_lblInstallMethodDescription = new Label
			{
				AutoEllipsis = true,
				Left = 12,
				Top = 38,
				Width = Math.Max(100, vtpDirectories.ClientSize.Width - 24),
				Height = 38,
				Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
			};

			panel.Controls.Add(label);
			panel.Controls.Add(m_cbxInstallMethod);
			panel.Controls.Add(m_lblInstallMethodDescription);
			vtpDirectories.Controls.Add(panel);
			panel.BringToFront();
		}

		private void RefreshInstallMethodSelector()
		{
			if (ViewModel == null || m_cbxInstallMethod == null)
				return;

			m_booUpdatingInstallMethod = true;
			try
			{
				m_cbxInstallMethod.SelectedIndex = ViewModel.PreferredInstallMethod == ModInstallMethod.Direct ? 1 : 0;
				UpdateInstallMethodDescription();
			}
			finally
			{
				m_booUpdatingInstallMethod = false;
			}
		}

		private void InstallMethod_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (m_booUpdatingInstallMethod || ViewModel == null)
				return;

			ViewModel.PreferredInstallMethod = m_cbxInstallMethod.SelectedIndex == 1
				? ModInstallMethod.Direct
				: ModInstallMethod.Virtual;
			UpdateInstallMethodDescription();
		}

		private void UpdateInstallMethodDescription()
		{
			if (m_lblInstallMethodDescription == null || m_cbxInstallMethod == null)
				return;

			m_lblInstallMethodDescription.Text = m_cbxInstallMethod.SelectedIndex == 1
				? LanguageManager.Get("GameStorage.InstallMethod.Direct.Description", "Direct Install copies mod files directly into the game folders without staging them in VirtualInstall. Virtual storage folders remain configured so you can switch back to Virtual Install at any time.")
				: LanguageManager.Get("GameStorage.InstallMethod.Virtual.Description", "Virtual Install stages mod files in VirtualInstall and deploys them through NMM's virtual file system.");
		}

		/// <summary>
		/// Applies the given theme to the form.
		/// </summary>
		/// <param name="p_thmTheme">The theme to apply.</param>
		protected void ApplyTheme(Theme p_thmTheme)
		{
			Icon = p_thmTheme.Icon;
		}

		#region Navigation

		/// <summary>
		/// Handles the <see cref="VerticalTabControl.SelectedTabPageChanged"/> event of the wizard control.
		/// </summary>
		/// <remarks>
		/// This validates each page as it is navigated away from.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="VerticalTabControl.TabPageEventArgs"/> describing the event arguments.</param>
		private void wizSetup_SelectedTabPageChanged(object sender, VerticalTabControl.TabPageEventArgs e)
		{
			if (e.TabPage == vtpDirectories)
			{
				if (!ViewModel.VirtualDirectoriesControlVM.ValidateSettings())
					wizSetup.SelectedTabPage = e.TabPage;
			}
		}

		/// <summary>
		/// Handles the <see cref="WizardControl.Cancelled"/> event of the wizard control.
		/// </summary>
		/// <remarks>
		/// This cancels the wizard.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void wizSetup_Cancelled(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
		}

		/// <summary>
		/// Handles the <see cref="WizardControl.Finished"/> event of the wizard control.
		/// </summary>
		/// <remarks>
		/// This finishes the wizard and persists the selected values.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void wizSetup_Finished(object sender, EventArgs e)
		{
			if (ViewModel.Save())
				DialogResult = DialogResult.OK;
			else if (ViewModel.IsSetupComplete)
				DialogResult = DialogResult.Cancel;
		}

		#endregion
	}
}
