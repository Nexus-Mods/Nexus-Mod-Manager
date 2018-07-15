namespace Nexus.Client
{
    using System;
    using System.ComponentModel;
    using System.Net.Mail;
    using System.Windows.Forms;

    using Games;
    using UI;
    using Util;

    /// <summary>
    /// A form that gathers login credentials.
    /// </summary>
    public partial class LoginForm : ManagedFontForm
	{
		private LoginFormVM _loginFormViewModel;

        private readonly LoginFormTask _loginTask;

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
				return _loginFormViewModel;
			}
			set
			{
				_loginFormViewModel = value;
				BindingHelper.CreateFullBinding(tbxUsername, () => tbxUsername.Text, _loginFormViewModel, () => _loginFormViewModel.Username);
				BindingHelper.CreateFullBinding(tbxPassword, () => tbxPassword.Text, _loginFormViewModel, () => _loginFormViewModel.Password);
				BindingHelper.CreateFullBinding(lblError, () => lblError.Text, _loginFormViewModel, () => _loginFormViewModel.ErrorMessage);
				BindingHelper.CreateFullBinding(ckbStayLoggedIn, () => ckbStayLoggedIn.Checked, _loginFormViewModel, () => _loginFormViewModel.StayLoggedIn);

				lblPrompt.Text = _loginFormViewModel.Prompt;

				ApplyTheme(_loginFormViewModel.CurrentTheme);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor the initializes the object with the given values.
		/// </summary>
		/// <param name="viewModel">The view model that provides the data and operations for this view.</param>
		/// <param name="loginTask"></param>
		public LoginForm(LoginFormVM viewModel, LoginFormTask loginTask)
        {
			InitializeComponent();
			lblError.Visible = true;
			lblError.TextChanged += lblError_TextChanged;
            FormClosed += LoginForm_FormClosed;
            _loginTask = loginTask;
			ViewModel = viewModel;
		}

		#endregion

		/// <summary>
		/// Applies the given theme to the form.
		/// </summary>
		/// <param name="theme">The theme to apply.</param>
		protected void ApplyTheme(Theme theme)
		{
			Icon = theme.Icon;
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
			lblError.Visible = !string.IsNullOrEmpty(lblError.Text);
			
		    // Force the form to resize.
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
			    if (IsEmail(tbxUsername.Text))
			    {
			        MessageBox.Show(this, "You need to use your Nexus username, not your email address.", "Your email is not your username", MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
			        tbxUsername.Clear();
			        tbxUsername.Focus();
                }
			    else
			    {
                    Hide();
                    Authenticating(this, new EventArgs());
                }
			}
		}

        /// <summary>
        /// Checks if the given string is a valid email address or not.
        /// </summary>
        /// <param name="input">String to check.</param>
        /// <returns>True if a valid email address, otherwise false.</returns>
	    private bool IsEmail(string input)
	    {
	        try
	        {
	            var mailAddress = new MailAddress(input);
	            return true;
	        }
	        catch (FormatException)
	        {
	            return false;
	        }
        }

		private void butOffline_Click(object sender, EventArgs e)
		{
            _loginTask.Reset();
            DialogResult = DialogResult.No;
		}

        private void LoginForm_FormClosed(object sender, EventArgs e)
		{
		    if (!_loginTask.LoggedIn)
            {
                _loginTask.Reset();
            }
        }
	}
}
