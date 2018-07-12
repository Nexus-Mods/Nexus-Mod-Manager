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
		private bool _addShellExtensions = true;
		private bool _associateNxmUrls = true;
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
		/// Gets the enumeration of file types associated with the client.
		/// </summary>
		/// <value>Ehe enumeration of file types associated with the client.</value>
		public IEnumerable<FileAssociationSetting> FileAssociations { get; }

		/// <summary>
		/// Gets whether or not file associations can be made.
		/// </summary>
		/// <value>Whether or not file associations can be made.</value>
		public bool CanAssociateFiles { get; } = UacUtil.IsElevated;

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
		/// Gets or sets whether the client should integrate with the explorer shell (right-click menu).
		/// </summary>
		/// <value>Whether the client should integrate with the explorer shell (right-click menu).</value>
		public bool AddShellExtensions
		{
			get => _addShellExtensions;
		    set
			{
				SetPropertyIfChanged(ref _addShellExtensions, value, () => AddShellExtensions);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should be associated with NXM URLs.
		/// </summary>
		/// <value>Whether the client should be associated with NXM URLs.</value>
		public bool AssociateNxmUrl
		{
			get => _associateNxmUrls;
		    set
			{
				SetPropertyIfChanged(ref _associateNxmUrls, value, () => AssociateNxmUrl);
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
			FileAssociations = new List<FileAssociationSetting>();
		}

		#endregion

		#region File Association

		/// <summary>
		/// Add a possible client programme file association.
		/// </summary>
		/// <param name="extension">The extension to allow to be associated with the client.</param>
		/// <param name="description">A description of the file type.</param>
		public void AddFileAssociation(string extension, string description)
		{
			((List<FileAssociationSetting>)FileAssociations).Add(new FileAssociationSetting(extension, description, IsAssociated(extension)));
		}

		/// <summary>
		/// Determines if the specified file type is associated with the client.
		/// </summary>
		/// <param name="extension">The extension of the file type for which it is to be determined
		/// whether it is associated with the client.</param>
		/// <returns><c>true</c> if the file type is associated with the client;
		/// <c>false</c> otherwise.</returns>
		protected bool IsAssociated(string extension)
		{
			if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            var fileId = extension.TrimStart('.').ToUpperInvariant() + "_File_Type";

			var key = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + extension, null, null) as string;

		    return fileId.Equals(key);
		}

		/// <summary>
		/// Associates the specifed file type with the client.
		/// </summary>
		/// <param name="fileAssociation">The description of the file type association to create.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to create the association.</exception>
		protected void AssociateFile(FileAssociationSetting fileAssociation)
		{
			if (!UacUtil.IsElevated)
            {
                throw new InvalidOperationException("You must have administrative privileges to change file associations.");
            }

            var fileId = fileAssociation.Extension.TrimStart('.').ToUpperInvariant() + "_File_Type";

			try
			{
				Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileAssociation.Extension, null, fileId);
				Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileId, null, fileAssociation.Description, RegistryValueKind.String);
				Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileId + @"\DefaultIcon", null, Application.ExecutablePath + ",0", RegistryValueKind.String);
				Registry.SetValue(@"HKEY_CLASSES_ROOT\" + fileId + @"\shell\open\command", null, "\"" + Application.ExecutablePath + "\" \"%1\"", RegistryValueKind.String);
			}
			catch (UnauthorizedAccessException)
			{
				throw new InvalidOperationException("Something (usually your antivirus) is preventing the program from interacting with the registry and changing the file associations.");
			}
		}

		/// <summary>
		/// Removes the association of the specifed file type with the client.
		/// </summary>
		/// <param name="fileAssociation">The description of the file type association to remove.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to remove the association.</exception>
		protected void UnassociateFile(FileAssociationSetting fileAssociation)
		{
			if (!UacUtil.IsElevated)
            {
                throw new InvalidOperationException("You must have administrative privileges to change file associations.");
            }

            var fileId = fileAssociation.Extension.TrimStart('.').ToUpperInvariant() + "_File_Type";
			var keys = Registry.ClassesRoot.GetSubKeyNames();

		    if (Array.IndexOf(keys, fileId) != -1)
			{
				Registry.ClassesRoot.DeleteSubKeyTree(fileId);
				Registry.ClassesRoot.DeleteSubKeyTree(fileAssociation.Extension);
			}
		}

		#endregion
        
		#region Serialization/Deserialization

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			foreach (var fasFileAssociation in FileAssociations)
            {
                fasFileAssociation.IsAssociated = IsAssociated(fasFileAssociation.Extension);
            }

            AddShellExtensions = ShellExtensionUtil.ReadShellExtensions();
			AssociateNxmUrl = UrlAssociationUtil.IsUrlAssociated("nxm");
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
			if (UacUtil.IsElevated)
			{
                foreach (var fasFileAssociation in FileAssociations)
                {
                    if (fasFileAssociation.IsAssociated)
                    {
                        AssociateFile(fasFileAssociation);
                    }
                    else
                    {
                        UnassociateFile(fasFileAssociation);
                    }
                }

                if (AssociateNxmUrl)
                {
                    UrlAssociationUtil.AssociateUrl("nxm", "Nexus Mod");
                }
                else
                {
                    UrlAssociationUtil.UnassociateUrl("nxm");
                }

                if (AddShellExtensions)
                {
                    if (!ShellExtensionUtil.AddShellExtensions())
                    {
                        MessageBox.Show("Couldn't add shell extensions.\nCheck TraceLog for more info.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                    }
                }
                else
                {
                    if (!ShellExtensionUtil.RemoveShellExtensions())
                    {
                        MessageBox.Show("Couldn't remove shell extensions.\nCheck TraceLog for more info.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);
                    }
				}
			}

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
