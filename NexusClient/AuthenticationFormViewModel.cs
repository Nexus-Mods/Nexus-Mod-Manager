namespace Nexus.Client
{
    using System.Diagnostics;
    using Games;
    using ModRepositories;
    using Util;

    /// <summary>
    /// This class encapsulates the data and the operations presented by UI
    /// elements that display the mod repository login UI.
    /// </summary>
    public class AuthenticationFormViewModel : ObservableObject
	{
		private string _apiKey;
		private string _errorMessage;

        #region Properties

        /// <summary>
        /// Gets the application's environment info.
        /// </summary>
        /// <value>The application's environment info.</value>
        protected IEnvironmentInfo EnvironmentInfo { get; }

		/// <summary>
		/// Gets the repository we are logging in to.
		/// </summary>
		/// <value>The repository we are logging in to.</value>
		protected IModRepository ModRepository { get; }

		/// <summary>
		/// Gets the theme to use for the UI.
		/// </summary>
		/// <value>The theme to use for the UI.</value>
		public Theme CurrentTheme { get; }

		/// <summary>
		/// Gets the prompt to display to the user.
		/// </summary>
		/// <value>The prompt to display to the user.</value>
		public string Prompt { get; }

		/// <summary>
		/// Gets the warning to display if the user tries to cancel the login.
		/// </summary>
		/// <value>The warning to display if the user tries to cancel the login.</value>
		public string CancelWarning { get; }

		/// <summary>
		/// Gets or sets the username.
		/// </summary>
		/// <value>The username.</value>
		public string ApiKey
		{
			get => _apiKey;
            set
			{
				SetPropertyIfChanged(ref _apiKey, value, () => ApiKey);
			}
		}

		/// <summary>
		/// Gets or sets the error message for the last login attempt.
		/// </summary>
		/// <value>The error message for the last login attempt.</value>
		public string ErrorMessage
		{
			get => _errorMessage;
            set
			{
				SetPropertyIfChanged(ref _errorMessage, value, () => ErrorMessage);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="environmentInfo">The application's environment info.</param>
		/// <param name="modRepository">The repository we are logging in to.</param>
		/// <param name="theme">The theme to use for the UI.</param>
		/// <param name="prompt">The prompt to display to the user.</param>
		/// <param name="error">The error to display.</param>
		/// <param name="cancelWarning">The warning to display if the user tries to cancel the login.</param>
		public AuthenticationFormViewModel(IEnvironmentInfo environmentInfo, IModRepository modRepository, Theme theme, string prompt, string error, string cancelWarning)
		{
			EnvironmentInfo = environmentInfo;
			ModRepository = modRepository;
			CurrentTheme = theme;
			Prompt = prompt;
			ErrorMessage = error;
			CancelWarning = cancelWarning;
			ApiKey = EnvironmentInfo.Settings.ApiKey;
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

            EnvironmentInfo.Settings.ApiKey = ApiKey;

            if (!ModRepository.Authenticate())
            {
                Trace.TraceWarning($"Couldn't authenticate with API key \"{ApiKey}\".");
                EnvironmentInfo.Settings.ApiKey = string.Empty;
                ErrorMessage = "Couldn't authenticate user.";

                return false;
            }

            EnvironmentInfo.Settings.Save();

			return true;
		}
	}
}
