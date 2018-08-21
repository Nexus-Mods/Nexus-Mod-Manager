namespace Nexus.Client.Settings
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Windows.Forms;

    using Microsoft.Win32;

    using Util;

    /// <summary>
    /// The group of general settings.
    /// </summary>
    public class GeneralSettingsGroup : SettingsGroup
	{
		private bool _checkForNewMods = true;
		private bool _scanSubfoldersForMods = true;
		private bool _overrideLocalModNames = true;
		private bool _addMissingModInfo = true;
		
		private bool _checkForUpdatesOnStartup = true;
	    private bool _checkForTipsOnStartup = true;
	    private int _updateCheckInterval;
		private int _modVersionsCheckInterval;
		private string _traceLogPath;
		private string _tempPath;

		#region Properties

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title { get; } = "General";

	    /// <summary>
		/// Gets or sets whether or not the client should check for new mod versions.
		/// </summary>
		/// <value>Whether or not the client should check for new mod versions.</value>
		public bool CheckForNewMods
		{
			get => _checkForNewMods;
	        set
			{
				SetPropertyIfChanged(ref _checkForNewMods, value, () => CheckForNewMods);
			}
		}

		/// <summary>
		/// Gets or sets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		public string TraceLogPath
		{
			get => _traceLogPath;
		    set
			{
				SetPropertyIfChanged(ref _traceLogPath, value, () => TraceLogPath);
			}
		}

		/// <summary>
		/// Gets or sets the path to which Temporary files should be installed.
		/// </summary>
		/// <value>The path to which Temporary files should be installed.</value>
		public string TempPath
		{
			get => _tempPath;
		    set
			{
				SetPropertyIfChanged(ref _tempPath, value, () => TempPath);
			}
		}

		/// <summary>
		/// Gets or sets the interval (in days) to wait before checking for mod updates.
		/// </summary>
		/// <value>The interval (in days) to wait before checking for mod updates.</value>
		public int ModVersionsCheckInterval
		{
			get => _modVersionsCheckInterval;
		    set
			{
				SetPropertyIfChanged(ref _modVersionsCheckInterval, value, () => ModVersionsCheckInterval);
			}
		}

		/// <summary>
	    /// Gets or sets whether the client should check for updates on startup.
        /// </summary>
        /// <value>Whether the client should check for updates on startup.</value>
        public bool CheckForTipsOnStartup
        {
            get => _checkForTipsOnStartup;
            set
            {
                SetPropertyIfChanged(ref _checkForTipsOnStartup, value, () => CheckForTipsOnStartup);
            }
        }

        /// <summary>
        /// Gets or sets whether the client should add missing info to mods.
        /// </summary>
        /// <value>Whether the client should add missing info to mods.</value>
        public bool AddMissingModInfo
		{
			get => _addMissingModInfo;
            set
			{
				SetPropertyIfChanged(ref _addMissingModInfo, value, () => AddMissingModInfo);
			}
		}

		

		/// <summary>
		/// Gets or sets whether the client should check for updates on startup.
		/// </summary>
		/// <value>Whether the client should check for updates on startup.</value>
		public bool CheckForUpdatesOnStartup
		{
			get => _checkForUpdatesOnStartup;
		    set
			{
				SetPropertyIfChanged(ref _checkForUpdatesOnStartup, value, () => CheckForUpdatesOnStartup);
			}
		}

		/// <summary>
		/// Gets or sets the interval (in days) to wait before checking for a program update.
		/// </summary>
		/// <value>The interval (in days) to wait before checking for a program update.</value>
		public Int32 UpdateCheckInterval
		{
			get => _updateCheckInterval;
		    set
			{
				SetPropertyIfChanged(ref _updateCheckInterval, value, () => UpdateCheckInterval);
			}
		}

		/// <summary>
		/// Gets or sets whether to scan sub directories of the mod directory for mods.
		/// </summary>
		/// <value>Whether to scan sub directories of the mod directory for mods.</value>
		public bool ScanSubfoldersForMods
		{
			get => _scanSubfoldersForMods;
		    set
			{
				SetPropertyIfChanged(ref _scanSubfoldersForMods, value, () => ScanSubfoldersForMods);
			}
		}

		/// <summary>
		/// Gets or sets whether to scan sub directories of the mod directory for mods.
		/// </summary>
		/// <value>Whether to scan sub directories of the mod directory for mods.</value>
		public bool OverrideLocalModNames
		{
			get => _overrideLocalModNames;
		    set
			{
				SetPropertyIfChanged(ref _overrideLocalModNames, value, () => OverrideLocalModNames);
			}
		}


		/// <summary>
		/// Gets or sets whether the mod manager should be closed after a game is launched.
		/// </summary>
		/// <value>Whether the mod manager should be closed after a game is launched.</value>
		public bool CloseModManagerAfterGameLaunch { get; set; }

	    /// <summary>
		/// Gets or sets whether or not to show the side panel.
		/// </summary>
		/// <value>Whether or not to show the side panel.</value>
		public bool ShowSidePanel { get; set; } = true;

	    /// <summary>
		/// Gets or sets whether the manager should prevent the extraction of readme files.
		/// </summary>
		/// <value>Whether the manager should prevent the extraction of readme files.</value>
		public bool SkipReadmeFiles { get; set; } = true;

	    /// <summary>
		/// Gets or sets whether the manager should prevent the visualization of the Mod Warning Icon.
		/// </summary>
		/// <value>Whether the manager should prevent the visualization of the Mod Warning Icon.</value>
		public bool HideModUpdateWarningIcon { get; set; } = true;

	    #endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="environmentInfo">The application's envrionment info.</param>
		public GeneralSettingsGroup(IEnvironmentInfo environmentInfo)
			: base(environmentInfo)
		{
		}

		#endregion
        
		#region Serialization/Deserialization

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			

            
			CheckForNewMods = EnvironmentInfo.Settings.CheckForNewModVersions;
			ModVersionsCheckInterval = EnvironmentInfo.Settings.ModVersionsCheckInterval;
			AddMissingModInfo = EnvironmentInfo.Settings.AddMissingInfoToMods;
			CheckForUpdatesOnStartup = EnvironmentInfo.Settings.CheckForUpdatesOnStartup;
			CheckForTipsOnStartup = EnvironmentInfo.Settings.CheckForTipsOnStartup;
			UpdateCheckInterval = EnvironmentInfo.Settings.UpdateCheckInterval;
			ScanSubfoldersForMods = EnvironmentInfo.Settings.ScanSubfoldersForMods;
			OverrideLocalModNames = EnvironmentInfo.Settings.OverrideLocalModNames;
			CloseModManagerAfterGameLaunch = EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch;
			ShowSidePanel = EnvironmentInfo.Settings.ShowSidePanel;
			SkipReadmeFiles = EnvironmentInfo.Settings.SkipReadmeFiles;
			HideModUpdateWarningIcon = EnvironmentInfo.Settings.HideModUpdateWarningIcon;
			TraceLogPath = string.IsNullOrEmpty(EnvironmentInfo.Settings.TraceLogFolder) ? EnvironmentInfo.ApplicationPersonalDataFolderPath : EnvironmentInfo.Settings.TraceLogFolder;
			TempPath = string.IsNullOrEmpty(EnvironmentInfo.Settings.TempPathFolder) ? EnvironmentInfo.TemporaryPath : EnvironmentInfo.Settings.TempPathFolder;
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			EnvironmentInfo.Settings.CheckForNewModVersions = CheckForNewMods;
			EnvironmentInfo.Settings.ModVersionsCheckInterval = ModVersionsCheckInterval;
			EnvironmentInfo.Settings.AddMissingInfoToMods = AddMissingModInfo;
			EnvironmentInfo.Settings.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
			EnvironmentInfo.Settings.UpdateCheckInterval = UpdateCheckInterval;
			EnvironmentInfo.Settings.ScanSubfoldersForMods = ScanSubfoldersForMods;
			EnvironmentInfo.Settings.OverrideLocalModNames = OverrideLocalModNames;
			EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch = CloseModManagerAfterGameLaunch;
			EnvironmentInfo.Settings.ShowSidePanel = ShowSidePanel;
			EnvironmentInfo.Settings.SkipReadmeFiles = SkipReadmeFiles;
			EnvironmentInfo.Settings.HideModUpdateWarningIcon = HideModUpdateWarningIcon;

		    try
			{
				if (!Directory.Exists(TraceLogPath))
                {
                    TraceLogPath = EnvironmentInfo.ApplicationPersonalDataFolderPath;
                }
            }
			catch
			{
				TraceLogPath = EnvironmentInfo.ApplicationPersonalDataFolderPath;
			}

		    EnvironmentInfo.Settings.TraceLogFolder = TraceLogPath;
			EnvironmentInfo.Settings.TempPathFolder = TempPath;
			EnvironmentInfo.Settings.Save();

		    return true;
		}

		#endregion
	}
}
