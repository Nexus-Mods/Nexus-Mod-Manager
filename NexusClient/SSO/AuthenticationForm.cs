namespace Nexus.Client.SSO
{
    using System;
    using System.ComponentModel;
    using System.Windows.Forms;
    using DevExpress.XtraEditors;
    using Games;
    using UI;
    using Util.Localization;

    /// <summary>
    /// A form that gathers login credentials.
    /// </summary>
    public partial class AuthenticationForm : ManagedFontXtraForm
    {
        private AuthenticationFormViewModel m_authenticationFormViewModel;

        private readonly AuthenticationFormTask _loginTask;

        private readonly SsoManager _ssoManager;
        private FlowLayoutPanel _buttonLayout;
        private TableLayoutPanel _contentLayout;
        private bool _updatingContentLayout;
        private int _minimumContentWidth;

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
            ApplyLocalization();
            NmmIconProvider.Bind(buttonSingleSignOn, NmmIconAction.Authorize);
            NmmIconProvider.Bind(buttonCancel, NmmIconAction.Cancel);
            ConfigureContentLayout();
            FormClosed += LoginForm_FormClosed;
            _loginTask = loginTask;
            ViewModel = viewModel;
            _ssoManager = new SsoManager();
            _ssoManager.ApiKeyReceived += (_, args) => Invoke((Action<string>)OnApiKeyReceived, args.ApiKey);
            _ssoManager.AuthenticationCancelled += OnAuthenticationCancelled;
        }

        private void ApplyLocalization()
        {
            Text = LanguageManager.Get("Authentication.Window.Title", "Authorization");
            buttonCancel.Text = LanguageManager.Get("Common.Action.Cancel", "Cancel");
            buttonSingleSignOn.Text = LanguageManager.Get("Authentication.Action.Authorize", "Authorize NMM");
            label1.Text = LanguageManager.Get("Authentication.ApiKeys.Info", "User authentication is now handled with API keys.");
            label2.Text = LanguageManager.Get("Authentication.ApiKeys.AuthorizeHint", "Use the Authorize button below to let NMM access your account details.");
        }

        /// <summary>
        /// Sizes localized text and icon buttons together as fonts, DPI and presentation settings change.
        /// </summary>
        private void ConfigureContentLayout()
        {
            SuspendLayout();
            _minimumContentWidth = ClientSize.Width;
            buttonSingleSignOn.AutoSize = true;
            buttonCancel.AutoSize = true;
            buttonSingleSignOn.Anchor = AnchorStyles.Left;
            buttonCancel.Anchor = AnchorStyles.Right;

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                WrapContents = false,
                Margin = new Padding(0, 8, 0, 0)
            };
            buttons.Controls.Add(buttonSingleSignOn);
            buttons.Controls.Add(buttonCancel);

            TableLayoutPanel content = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            label1.AutoSizeMode = LabelAutoSizeMode.Vertical;
            label2.AutoSizeMode = LabelAutoSizeMode.Vertical;
            label1.Dock = DockStyle.Top;
            label2.Dock = DockStyle.Top;
            label1.Margin = new Padding(0, 0, 0, 8);
            label2.Margin = Padding.Empty;
            content.Controls.Add(label1, 0, 0);
            content.Controls.Add(label2, 0, 1);
            content.Controls.Add(buttons, 0, 2);
            Controls.Add(content);
            _buttonLayout = buttons;
            _contentLayout = content;
            ClientSize = new System.Drawing.Size(Math.Max(ClientSize.Width,
                buttonSingleSignOn.CalcBestSize().Width + buttonCancel.CalcBestSize().Width + 48), ClientSize.Height);
            ResumeLayout(true);
        }

        /// <summary>Fits the window around wrapped labels and the buttons' preferred sizes.</summary>
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_contentLayout == null || _updatingContentLayout)
                return;
            _updatingContentLayout = true;
            try
            {
                int width = Math.Max(_minimumContentWidth,
                    _buttonLayout.PreferredSize.Width + _contentLayout.Padding.Horizontal);
                if (ClientSize.Width != width)
                    ClientSize = new System.Drawing.Size(width, ClientSize.Height);
                _contentLayout.PerformLayout();
                int height = _contentLayout.PreferredSize.Height;
                if (ClientSize.Height != height)
                    ClientSize = new System.Drawing.Size(width, height);
            }
            finally { _updatingContentLayout = false; }
        }

        private void OnAuthenticationCancelled(object sender, CancellationEventArgs e)
        {
            if (e.Reason == AuthenticationCancelledReason.ConnectionIssue)
            {
                XtraMessageBox.Show(this, LanguageManager.Get("Authentication.Error.Network", "Authentication failed due to network issues."), LanguageManager.Get("Authentication.Error.Title", "Authentication failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                var fallbackForm = new ManualApiKeyEntryForm(ViewModel);
                var result = fallbackForm.ShowDialog(this);

                if (result == DialogResult.OK)
                {
                    Authenticate(null, null);
                }
            }
            else if (e.Reason != AuthenticationCancelledReason.Manual)
            {
                XtraMessageBox.Show(this, LanguageManager.Get("Authentication.Error.Unknown", "Authentication failed for unknown reasons, check trace logs."), LanguageManager.Get("Authentication.Error.Title", "Authentication failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void ButtonSingleSignOn_Click(object sender, EventArgs e)
        {
            buttonSingleSignOn.Enabled = false;
            _ssoManager.Start();
        }
    }
}
