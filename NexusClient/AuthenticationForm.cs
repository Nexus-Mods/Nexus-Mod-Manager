namespace Nexus.Client
{
    using System;
    using System.ComponentModel;
    using System.Windows.Forms;

    using Games;
    using UI;
    using Util;

    /// <summary>
    /// A form that gathers login credentials.
    /// </summary>
    public partial class AuthenticationForm : ManagedFontForm
    {
        private AuthenticationFormViewModel m_authenticationFormViewModel;

        private readonly AuthenticationFormTask _loginTask;

        #region Events

        /// <summary>
        /// Raised when the user is being authenticated.
        /// </summary>
        public event EventHandler Authenticating = delegate { };

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the view model that provides the data and operations for this view.
        /// </summary>
        /// <value>The view model that provides the data and operations for this view.</value>
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        protected AuthenticationFormViewModel ViewModel
        {
            get
            {
                return m_authenticationFormViewModel;
            }
            set
            {
                m_authenticationFormViewModel = value;
                BindingHelper.CreateFullBinding(textBoxApiKey, () => textBoxApiKey.Text, m_authenticationFormViewModel, () => m_authenticationFormViewModel.ApiKey);
                BindingHelper.CreateFullBinding(labelErrorMessage, () => labelErrorMessage.Text, m_authenticationFormViewModel, () => m_authenticationFormViewModel.ErrorMessage);

                //lblPrompt.Text = m_authenticationFormViewModel.Prompt;

                ApplyTheme(m_authenticationFormViewModel.CurrentTheme);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor the initializes the object with the given values.
        /// </summary>
        /// <param name="viewModel">The view model that provides the data and operations for this view.</param>
        /// <param name="loginTask"></param>
        public AuthenticationForm(AuthenticationFormViewModel viewModel, AuthenticationFormTask loginTask)
        {
            InitializeComponent();
            labelErrorMessage.Visible = true;
            labelErrorMessage.TextChanged += lblError_TextChanged;
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
            labelErrorMessage.Visible = !string.IsNullOrEmpty(labelErrorMessage.Text);

            // Force the form to resize.
            PerformLayout();
        }

        /// <summary>
        /// Handles the <see cref="Control.Click"/> event of the login button.
        /// </summary>
        /// <param name="sender">The object that triggered the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
        private void ButtonAuthenticate_Click(object sender, EventArgs e)
        {
            if (Authenticating != null)
            {
                Hide();
                Authenticating(this, new EventArgs());
            }
        }

        private void ButtonCancel_Click(object sender, EventArgs e)
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
