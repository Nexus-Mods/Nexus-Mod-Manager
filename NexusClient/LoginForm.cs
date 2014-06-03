using System;
using System.ComponentModel;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client
{
	/// <summary>
	/// A form that gathers login credentials.
	/// </summary>
	public partial class LoginForm : ManagedFontForm
	{
		private LoginFormVM m_vmlViewModel = null;

        private LoginFormTask m_lftLoginTask = null;

		#region Events

		/// <summary>
		/// Raised when the programme is being updated.
		/// </summary>
		public event EventHandler Authenticating = delegate { };

		#endregion

		#region Properties

		/// <summary>
		/// Gets or sets the view model that provides the data and operations for this view.
		/// </summary>
		/// <value>The view model that provides the data and operations for this view.</value>
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		protected LoginFormVM ViewModel
		{
			get
			{
				return m_vmlViewModel;
			}
			set
			{
				m_vmlViewModel = value;
				BindingHelper.CreateFullBinding(tbxUsername, () => tbxUsername.Text, m_vmlViewModel, () => m_vmlViewModel.Username);
				BindingHelper.CreateFullBinding(tbxPassword, () => tbxPassword.Text, m_vmlViewModel, () => m_vmlViewModel.Password);
				BindingHelper.CreateFullBinding(lblError, () => lblError.Text, m_vmlViewModel, () => m_vmlViewModel.ErrorMessage);
				BindingHelper.CreateFullBinding(ckbStayLoggedIn, () => ckbStayLoggedIn.Checked, m_vmlViewModel, () => m_vmlViewModel.StayLoggedIn);

				lblPrompt.Text = m_vmlViewModel.Prompt;

				ApplyTheme(m_vmlViewModel.CurrentTheme);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor the initializes the object with the given values.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		public LoginForm(LoginFormVM p_vmlViewModel, LoginFormTask p_lftLoginTask)
        {
			InitializeComponent();
			lblError.Visible = true;
			lblError.TextChanged += new EventHandler(lblError_TextChanged);
            this.FormClosed += new FormClosedEventHandler(LoginForm_FormClosed);
            m_lftLoginTask = p_lftLoginTask;
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

		/// <summary>
		/// Handles the <see cref="Control.TextChanged"/> event of the error label.
		/// </summary>
		/// <remarks>
		/// Show or hides the error label, depending on whether it contains any text.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void lblError_TextChanged(object sender, EventArgs e)
		{
			lblError.Visible = !String.IsNullOrEmpty(lblError.Text);
			//force the form to resize
			PerformLayout();
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the login button.
		/// </summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butLogin_Click(object sender, EventArgs e)
		{
			if (Authenticating != null)
			{
				this.Hide();
				Authenticating(this, new EventArgs());
			}
		}

		/// <summary>
		/// Handles the <see cref="Control.Click"/> event of the conacel button.
		/// </summary>
		/// <param name="sender">The object that triggered the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void butCancel_Click(object sender, EventArgs e)
		{
            m_lftLoginTask.Reset();
            DialogResult = DialogResult.No;
		}

		private void butOffline_Click(object sender, EventArgs e)
		{
            m_lftLoginTask.Reset();
            DialogResult = DialogResult.No;
		}

         private void LoginForm_FormClosed(object sender, EventArgs e)
		{
			if (!m_lftLoginTask.LoggedIn)
				m_lftLoginTask.Reset();
 		}
	}
}
