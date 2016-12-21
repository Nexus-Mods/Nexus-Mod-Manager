using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.UI;
using Nexus.UI.Controls;

namespace Nexus.Client.Games.BreakingWheel
{
	/// <summary>
	/// This is the setup form for theBreakingWheel game mode.
	/// </summary>
	public partial class SetupForm : ManagedFontForm, IView
	{
		private BreakingWheelSetupVM m_vmlViewModel = null;

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public SetupBaseVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			private set
			{
				m_vmlViewModel = value as BreakingWheelSetupVM;
				if (m_vmlViewModel == null)
					throw new ArgumentException("The given view model must be aBreakingWheelSetupVM. Type found: " + value.GetType().FullName);
				lblTitle.Text = String.Format(lblTitle.Text, m_vmlViewModel.GameModeDescriptor.Name);
				Text = String.Format(Text, m_vmlViewModel.GameModeDescriptor.Name);
				rdcDirectories.ViewModel = m_vmlViewModel.SetupDirectoriesControlVM;
				ApplyTheme(m_vmlViewModel.GameModeDescriptor.ModeTheme);
			}
		}

		#endregion

		#region Contructors

		/// <summary>
		/// The default constructor.
		/// </summary>
		protected SetupForm()
		{
			InitializeComponent();
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public SetupForm(BreakingWheelSetupVM p_vmlViewModel)
			: this()
		{
			ViewModel = p_vmlViewModel;
		}

		#endregion

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
				if (!ViewModel.SetupDirectoriesControlVM.ValidateSettings())
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
			if (MessageBox.Show(this, String.Format("If you cancel the setup {0} will close.", ViewModel.EnvironmentInfo.Settings.ModManagerName), "Confirm", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
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
		}

		#endregion
	}
}
