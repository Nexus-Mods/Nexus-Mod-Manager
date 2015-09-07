using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.ModRepositories;
using System.Windows.Forms;
using Nexus.Client.ModManagement;

namespace Nexus.Client
{
	public class LoginFormTask : ThreadedBackgroundTask
	{

		#region Properties

		/// <summary>
		/// Gets the current ModManager.
		/// </summary>
		/// <value>The current ModManager.</value>
		protected ModManager ModManager { get; private set; }
		protected LoginForm LoginForm { get; private set; }
		protected LoginFormVM LoginFormVM { get; private set; }

		public bool LoggedIn 
		{
			get
			{
				return (Status == TaskStatus.Complete);
			}
		}

		public bool LoggedOut
		{
			get
			{
				return (Status == TaskStatus.Paused) || (Status == TaskStatus.Error);
			}
		}

		public bool LoggingIn
		{
			get
			{
				return (Status == TaskStatus.Queued) || (Status == TaskStatus.Running) || (Status == TaskStatus.Incomplete);
			}
		}

		#endregion

		private string strError = String.Empty;
		private bool booCredentialsExpired = false;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_ModManager">The current ModManager.</param>
		public LoginFormTask(ModManager p_mmModManager)
		{
			Status = TaskStatus.Paused;
			ModManager = p_mmModManager;
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="IBackgroundTask.TaskEnded"/> event.
		/// </summary>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		protected override void OnTaskEnded(TaskEndedEventArgs e)
		{
			base.OnTaskEnded(e);
		}
		#endregion

		/// <summary>
		/// Starts the update.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		public void Update()
		{
			Start();
		}

		public void Reset()
		{
			Status = TaskStatus.Error;
			strError = String.Empty;
			booCredentialsExpired = false;
			OverallMessage = "You are not logged in.";
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>Always <c>null</c>.</returns>
		protected override object DoWork(object[] p_objArgs)
		{
            Status = TaskStatus.Queued;
            OverallMessage = "Attempting to login...";
			if (!TokenLogin())
				return LoginUser();
			else
				return true;
		}

		private void LoginForm_Authenticating(object sender, EventArgs e)
		{
			LoginForm.CheckForIllegalCrossThreadCalls = false;
			LoginFormVM.ErrorMessage = "Attempting to connect to login servers";

			Status = TaskStatus.Running;
			OverallMessage = "Sending login data...";
			if (LoginFormVM.Login())
			{
				Status = TaskStatus.Complete;
				OverallMessage = "Logged in.";
				LoginForm.DialogResult = DialogResult.OK;
			}
			else
			{
				Status = TaskStatus.Error;
				OverallMessage = "Login error: " + LoginFormVM.ErrorMessage;
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
			string strMessage = String.Format("You must log into the {0} website.", ModManager.ModRepository.Name);
			string strCancelWarning = String.Format("If you do not login {0} will close.", ModManager.EnvironmentInfo.Settings.ModManagerName);
            strError = booCredentialsExpired ? "You need to login using your Nexus username and password." : strError;

			LoginFormVM = new LoginFormVM(ModManager.EnvironmentInfo, ModManager.ModRepository, ModManager.GameMode.ModeTheme, strMessage, strError, strCancelWarning);

			LoginForm = new LoginForm(LoginFormVM, this);
			LoginForm.Authenticating += new EventHandler(LoginForm_Authenticating);
			LoginForm.ShowDialog();
			if (Status == TaskStatus.Complete)
				return true;
			else
			{
				if (Status == TaskStatus.Incomplete)
					OverallMessage = "You are not logged in.";
				return false;
			}
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
			if (ModManager.EnvironmentInfo.Settings.RepositoryAuthenticationTokens[ModManager.ModRepository.Id] == null)
				ModManager.EnvironmentInfo.Settings.RepositoryAuthenticationTokens[ModManager.ModRepository.Id] = new KeyedSettings<string>();

			Dictionary<string, string> dicAuthTokens = new Dictionary<string, string>(ModManager.EnvironmentInfo.Settings.RepositoryAuthenticationTokens[ModManager.ModRepository.Id]);
                        
            Status = TaskStatus.Running;
            OverallMessage = "Sending login token...";

			try
			{
				booCredentialsExpired = !ModManager.ModRepository.Login(dicAuthTokens);
			}
			catch (RepositoryUnavailableException e)
			{
				strError = e.Message;
				dicAuthTokens.Clear();
			}

			if (dicAuthTokens.Count == 0)
			{
				Status = TaskStatus.Incomplete;
				OverallMessage = "Insert login credentials";
				return false;
			}
			else if (booCredentialsExpired)
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
