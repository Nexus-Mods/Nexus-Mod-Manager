namespace Nexus.Client.SSO
{
    using System;
    using System.ComponentModel;
    using System.Windows.Forms;
    using Games;
    using UI;

    /// <summary>
    /// A form that gathers login credentials.
    /// </summary>
    public partial class AuthenticationForm : ManagedFontForm
    {
        private AuthenticationFormViewModel m_authenticationFormViewModel;

        private readonly AuthenticationFormTask _loginTask;

        private readonly SsoManager _ssoManager;

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
            FormClosed += LoginForm_FormClosed;
            _loginTask = loginTask;
            ViewModel = viewModel;
            _ssoManager = new SsoManager();
            _ssoManager.ApiKeyReceived += (_, args) => Invoke((Action<string>)OnApiKeyReceived, args.ApiKey);
            _ssoManager.AuthenticationCancelled += OnAuthenticationCancelled;
        }

        private void OnAuthenticationCancelled(object sender, CancellationEventArgs e)
        {
            if (e.Reason == AuthenticationCancelledReason.ConnectionIssue)
            {
                MessageBox.Show(this, "Authentication failed due to network issues.", "Authentication failed", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
            }
            else if (e.Reason != AuthenticationCancelledReason.Manual)
            {
                MessageBox.Show(this, "Authentication failed for unknown reasons, check trace logs.", "Authentication failed", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
            }

            Close();
        }

        #endregion

        private void OnApiKeyReceived(string apiKey)
        {
            ViewModel.ApiKey = apiKey;
            Authenticate(null, null);
        }

        /// <summary>
        /// Applies the given theme to the form.
        /// </summary>
        /// <param name="theme">The theme to apply.</param>
        protected void ApplyTheme(Theme theme)
        {
            Icon = theme.Icon;
        }

        /// <summary>
        /// Handles the <see cref="Control.Click"/> event of the login button.
        /// </summary>
        /// <param name="sender">The object that triggered the event.</param>
        /// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
        private void Authenticate(object sender, EventArgs e)
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
            _ssoManager.Cancel();
            DialogResult = DialogResult.No;
        }

        private void LoginForm_FormClosed(object sender, EventArgs e)
        {
            if (!_loginTask.LoggedIn)
            {
                _loginTask.Reset();
            }
        }

        private void linkLabelManageApiKeys_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("https://www.nexusmods.com/users/myaccount?tab=api%20access");
        }

        private void ButtonSingleSignOn_Click(object sender, EventArgs e)
        {
            buttonSingleSignOn.Enabled = false;
            _ssoManager.Start();
        }
    }
}
