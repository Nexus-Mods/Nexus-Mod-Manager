namespace Nexus.Client.Settings
{
    using System;
    using ModRepositories;

    /// <summary>
    /// The group of download settings.
    /// </summary>
    public class DownloadSettingsGroup : SettingsGroup
	{
        private readonly IModRepository _modRepository;
        private bool _useMultithreadedDownloads;
		private bool _premiumEnabled;
		private int _maxConcurrentDownloads = 10;
		
		#region Custom Events

		public event EventHandler UpdatedSettings;

		#endregion

		#region Properties

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title => "Download Options";

        /// <summary>
		/// Gets or sets whether to use multithreaded downloads.
		/// </summary>
		/// <value>Whether to use multithreaded downloads.</value>
		public bool UseMultithreadedDownloads
		{
			get => _useMultithreadedDownloads;
            set
			{
				SetPropertyIfChanged(ref _useMultithreadedDownloads, value, () => UseMultithreadedDownloads);
			}
		}

		/// <summary>
		/// Gets or sets the interval (in days) to wait before checking for a program update.
		/// </summary>
		/// <value>The interval (in days) to wait before checking for a program update.</value>
		public int MaxConcurrentDownloads
		{
			get => _maxConcurrentDownloads;
            set
			{
				SetPropertyIfChanged(ref _maxConcurrentDownloads, value, () => MaxConcurrentDownloads);
			}
		}

		/// <summary>
		/// Gets or sets whether the user wants to use only Premium Server.
		/// </summary>
		/// <value>Whether the user wants to use only Premium Server.</value>
		public bool PremiumEnabled
		{
			get => _premiumEnabled;
            private set
			{
				SetPropertyIfChanged(ref _premiumEnabled, value, () => PremiumEnabled);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="environmentInfo">The application's environment info.</param>
		public DownloadSettingsGroup(IEnvironmentInfo environmentInfo, IModRepository modRepository)
			: base(environmentInfo)
		{
			_modRepository = modRepository;
			_modRepository.UserStatusUpdate += ModRepositoryUserStatusUpdate;

            LoadRepositorySettings();
			Load();
			Save();
		}

		#endregion

		private void LoadRepositorySettings()
		{
            if (_modRepository.UserStatus != null && _modRepository.UserStatus.IsPremium)
			{
                PremiumEnabled = true;
            }
			else
			{
                PremiumEnabled = false;
			}

			MaxConcurrentDownloads = _modRepository.MaxConcurrentDownloads;
		}

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public sealed override void Load()
		{
			UseMultithreadedDownloads = EnvironmentInfo.Settings.UseMultithreadedDownloads;

			if (MaxConcurrentDownloads == 0)
            {
                MaxConcurrentDownloads = EnvironmentInfo.Settings.MaxConcurrentDownloads;
            }
        }

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public sealed override bool Save()
		{
			EnvironmentInfo.Settings.UseMultithreadedDownloads = UseMultithreadedDownloads;
			EnvironmentInfo.Settings.MaxConcurrentDownloads = MaxConcurrentDownloads;

            lock (EnvironmentInfo.Settings)
            {
                EnvironmentInfo.Settings.Save();
            }

            return true;
		}

		/// <summary>
		/// Handles the <see cref="_modRepository.UserStatusUpdate"/> event of the tasks list.
		/// </summary>
		/// <remarks>
		/// Updates the UI elements.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void ModRepositoryUserStatusUpdate(object sender, EventArgs e)
		{
			LoadRepositorySettings();
            UpdatedSettings?.Invoke(this, new EventArgs());
        }
	}
}
