using System;
using System.Collections.Generic;
using Nexus.Client.Games;
using Nexus.Client.ModRepositories;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using System.Diagnostics;

namespace Nexus.Client
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display the mod repository login UI.
	/// </summary>
	public class LoginFormVM : ObservableObject
	{
		private string m_strUsername = null;
		private string m_strPassword = null;
		private string m_strErrorMessage = null;
		private bool m_booStayLoggedIn = false;

		#region Properties

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the repository we are logging in to.
		/// </summary>
		/// <value>The repository we are logging in to.</value>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the theme to use for the UI.
		/// </summary>
		/// <value>The theme to use for the UI.</value>
		public Theme CurrentTheme { get; private set; }

		/// <summary>
		/// Gets the prompt to display to the user.
		/// </summary>
		/// <value>The prompt to display to the user.</value>
		public string Prompt { get; private set; }

		/// <summary>
		/// Gets the warning to display if the user tries to cancel the login.
		/// </summary>
		/// <value>The warning to display if the user tries to cancel the login.</value>
		public string CancelWarning { get; private set; }

		/// <summary>
		/// Gets or sets the username.
		/// </summary>
		/// <value>The username.</value>
		public string Username
		{
			get
			{
				return m_strUsername;
			}
			set
			{
				SetPropertyIfChanged(ref m_strUsername, value, () => Username);
			}
		}

		/// <summary>
		/// Gets or set the password.
		/// </summary>
		/// <value>The password.</value>
		public string Password
		{
			get
			{
				return m_strPassword;
			}
			set
			{
				SetPropertyIfChanged(ref m_strPassword, value, () => Password);
			}
		}

		/// <summary>
		/// Gets whether or not to keep the user logged in.
		/// </summary>
		/// <value>Whether or not to keep the user logged in.</value>
		public bool StayLoggedIn
		{
			get
			{
				return m_booStayLoggedIn;
			}
			set
			{
				SetPropertyIfChanged(ref m_booStayLoggedIn, value, () => StayLoggedIn);
			}
		}

		/// <summary>
		/// Gets or sets the error message for the last login attempt.
		/// </summary>
		/// <value>The error message for the last login attempt.</value>
		public string ErrorMessage
		{
			get
			{
				return m_strErrorMessage;
			}
			set
			{
				SetPropertyIfChanged(ref m_strErrorMessage, value, () => ErrorMessage);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The repository we are logging in to.</param>
		/// <param name="p_thmTheme">The theme to use for the UI.</param>
		/// <param name="p_strPrompt">The prompt to display to the user.</param>
		/// <param name="p_strError">The error to display.</param>
		/// <param name="p_strCancelWarning">The warning to display if the user tries to cancel the login.</param>
		public LoginFormVM(IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, Theme p_thmTheme, string p_strPrompt, string p_strError, string p_strCancelWarning)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			ModRepository = p_mrpModRepository;
			CurrentTheme = p_thmTheme;
			Prompt = p_strPrompt;
			ErrorMessage = p_strError;
			CancelWarning = p_strCancelWarning;
			Username = EnvironmentInfo.Settings.RepositoryUsernames[ModRepository.Id];
			StayLoggedIn = EnvironmentInfo.Settings.RepositoryAuthenticationTokens.ContainsKey(ModRepository.Id);
		}

		#endregion

		/// <summary>
		/// Attempt to login to the mod repository.
		/// </summary>
		/// <returns><c>true</c> if the login was successful;
		/// <c>false</c> otherwise.</returns>
		public bool Login()
		{
			ErrorMessage = null;
			EnvironmentInfo.Settings.RepositoryUsernames[ModRepository.Id] = Username;
			Dictionary<string, string> dicAuthTokens = null;
			try
			{
				if (!ModRepository.Login(Username, Password, out dicAuthTokens))
				{
					ErrorMessage = "The given login information is invalid.";
					return false;
				}
			}
			catch (RepositoryUnavailableException e)
			{
				ErrorMessage = e.Message;
				return false;
			}
			if (StayLoggedIn)
				EnvironmentInfo.Settings.RepositoryAuthenticationTokens[ModRepository.Id] = new KeyedSettings<string>(dicAuthTokens);
			else
				EnvironmentInfo.Settings.RepositoryAuthenticationTokens.Remove(ModRepository.Id);
			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
