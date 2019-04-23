namespace Nexus.Client
{
    using System;
    using System.Windows.Forms;

    using BackgroundTasks;
    using ModRepositories;
    using ModManagement;

    public class AuthenticationFormTask : ThreadedBackgroundTask
	{
		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected ModManager ModManager { get; }

        protected AuthenticationForm LoginForm { get; private set; }

        protected AuthenticationFormViewModel AuthenticationFormViewModel { get; private set; }

		public bool LoggedIn => (Status == TaskStatus.Complete);

        public bool LoggedOut => (Status == TaskStatus.Paused) || (Status == TaskStatus.Error);

        public bool LoggingIn => (Status == TaskStatus.Queued) || (Status == TaskStatus.Running) || (Status == TaskStatus.Incomplete);

        #endregion

		private string _error = string.Empty;
		private bool _credentialsExpired;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		public AuthenticationFormTask(ModManager modManager)
		{
			Status = TaskStatus.Paused;
			ModManager = modManager;
		}

		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		public void Update()
		{
			Start();
		}

        /// <summary>
        /// 
        /// </summary>
		public void Reset()
		{
			Status = TaskStatus.Error;
			_error = string.Empty;
			_credentialsExpired = false;
			OverallMessage = "You are not logged in.";
		}

		/// <inheritdoc />
        /// <summary>
        /// The method that is called to start the background task.
        /// </summary>
        /// <param name="args">Arguments to for the task execution.</param>
        /// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] args)
		{
			Status = TaskStatus.Queued;
			OverallMessage = "Attempting to login...";

            return TokenLogin() || LoginUser();
        }

		private void LoginForm_Authenticating(object sender, EventArgs e)
		{
			Control.CheckForIllegalCrossThreadCalls = false;
			AuthenticationFormViewModel.ErrorMessage = "Attempting to connect to login servers";

			Status = TaskStatus.Running;
			OverallMessage = "Sending login data...";

            if (AuthenticationFormViewModel.Login())
			{
				Status = TaskStatus.Complete;
				OverallMessage = "Logged in.";
				LoginForm.DialogResult = DialogResult.OK;
			}
			else
			{
				Status = TaskStatus.Error;
				OverallMessage = "Login error: " + AuthenticationFormViewModel.ErrorMessage;
			}
		}

		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		protected bool LoginUser()
		{
			var strMessage = $"You must log into the {ModManager.ModRepository.Name} website.";
			var strCancelWarning = $"If you do not login {ModManager.EnvironmentInfo.Settings.ModManagerName} will close.";
			_error = _credentialsExpired ? "You need to login using your Nexus username and password." : _error;

			AuthenticationFormViewModel = new AuthenticationFormViewModel(ModManager.EnvironmentInfo, ModManager.ModRepository, ModManager.GameMode.ModeTheme, strMessage, _error, strCancelWarning);

			LoginForm = new AuthenticationForm(AuthenticationFormViewModel, this);
			LoginForm.Authenticating += LoginForm_Authenticating;
			LoginForm.ShowDialog();

            switch (Status)
            {
                case TaskStatus.Complete:
                    return true;
                case TaskStatus.Incomplete:
                    OverallMessage = "You are not logged in.";
                    break;
            }

            return false;
        }


		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_mrpModRepository">The mod repository to use to retrieve mods and mod metadata.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		public bool TokenLogin()
		{
            if (string.IsNullOrEmpty(ModManager.EnvironmentInfo.Settings.ApiKey))
            {
                return false;
            }

			Status = TaskStatus.Running;

            OverallMessage = "Sending login token...";

			try
            {
                _credentialsExpired = !ModManager.ModRepository.Authenticate(ModManager.EnvironmentInfo.Settings.ApiKey);
            }
			catch (RepositoryUnavailableException e)
			{
				_error = e.Message;
			}

			if (_credentialsExpired)
			{
				Status = TaskStatus.Incomplete;
				OverallMessage = "Token expired: insert login credentials";
				return false;
			}
			else
			{
				Status = TaskStatus.Complete;
				OverallMessage = "Logged in.";
				return true;
			}
		}
	}
}
