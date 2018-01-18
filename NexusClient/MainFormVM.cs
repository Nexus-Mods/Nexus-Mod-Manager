using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadMonitoring;
using Nexus.Client.DownloadMonitoring.UI;
using Nexus.Client.Commands;
using Nexus.Client.Games;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModActivationMonitoring;
using Nexus.Client.ModActivationMonitoring.UI;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.Plugins;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.Settings;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using Nexus.Client.Commands.Generic;
using Nexus.UI.Controls;

namespace Nexus.Client
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display the main form.
	/// </summary>
	public class MainFormVM
	{
		#region ProfileSwitchToken
		protected class ProfileSwitchToken
		{
			#region Fields
			private bool _booisSilent;
			private Dictionary<string, string> _dicProfile = null;
			List<string> _lstScriptedMismatch = new List<string>();
			IModProfile _impCurrentProfile = null;
			List<IVirtualModLink> _lstVirtualLinks = new List<IVirtualModLink>();
			List<IVirtualModInfo> _lstMissingMods = new List<IVirtualModInfo>();
			#endregion

			#region Properties
			public bool IsSilent
			{
				get
				{
					return _booisSilent;
				}
			}

			public IModProfile Profile
			{
				get
				{
					return _impCurrentProfile;
				}
			}

			public List<IVirtualModLink> VirtualLinks
			{
				get
				{
					return _lstVirtualLinks;
				}
			}

			public List<IVirtualModInfo> MissingMods
			{
				get
				{
					return _lstMissingMods;
				}
			}

			public List<string> ScriptedMismatchList
			{
				get
				{
					return _lstScriptedMismatch;
				}
			}

			public Dictionary<string, string> ProfileDictionary
			{
				get
				{
					return _dicProfile;
				}
			}
			#endregion

			public ProfileSwitchToken(bool p_booIsSilent, IModProfile p_impProfile, List<IVirtualModLink> p_lstVirtualLinks, List<string> p_lstScriptedMismatch, List<IVirtualModInfo> p_lstMissingMods, Dictionary<string, string> p_dicProfile)
			{
				_booisSilent = p_booIsSilent;
				_impCurrentProfile = p_impProfile;
				_lstVirtualLinks = p_lstVirtualLinks;
				_lstMissingMods = p_lstMissingMods;
				_lstScriptedMismatch = p_lstScriptedMismatch;
				_dicProfile = p_dicProfile;
			}
		}
		#endregion

		private const string CHANGE_DEFAULT_GAME_MODE = "__changedefaultgamemode";
		private const string RESCAN_INSTALLED_GAMES = "__rescaninstalledgames";
		private bool m_booIsSwitching = false;

		#region Events

		/// <summary>
		/// Raised when the programme is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> Updating = delegate { };

		/// <summary>
		/// Raised when switching profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileSwitching = delegate { };

		/// <summary>
		/// Raised when switching profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> MigratingMods = delegate { };

		/// <summary>
		/// Raised when backuping.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> CreatingBackup = delegate { };

		/// <summary>
		/// Raised when restoring the backup.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> RestoringBackup = delegate { };

		/// <summary>
		/// Raised when Purging Loose Files.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> PurgingLooseFiles = delegate { };

		/// <summary>
		/// Raised when downloading profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileDownloading = delegate { };

		/// <summary>
		/// Raised when downloading profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileSharing = delegate { };

		/// <summary>
		/// Raised when rename profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileRenaming = delegate { };

		/// <summary>
		/// Raised when remove profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ConfigFilesFixing = delegate { };

		/// <summary>
		/// Raised when remove profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileRemoving = delegate { };

		/// <summary>
		/// Raised when Check the Online Profile Integrity.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> CheckingOnlineProfileIntegrity = delegate { };

		/// <summary>
		/// Raised when applying an imported loadorder.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ApplyingImportedLoadOrder = delegate { };

		public event EventHandler<EventArgs> AbortedProfileSwitch = delegate { };


		#endregion

		#region Delegates

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmActionMethod ConfirmUpdaterAction = delegate { return true; };

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmRememberedActionMethod ConfirmCloseAfterGameLaunch = delegate (out bool x) { x = false; return true; };

		#endregion

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to update the programme.
		/// </summary>
		/// <value>The command to update the programme.</value>
		public Command UpdateCommand { get; private set; }

		/// <summary>
		/// Gets the command to logout of the current mod repository.
		/// </summary>
		/// <value>The command to logout of the current mod repository.</value>
		public Command LogoutCommand { get; private set; }

		/// <summary>
		/// Gets the commands to change the managed game mode.
		/// </summary>
		/// <value>The commands to change the managed game mode.</value>
		public IEnumerable<Command> ChangeGameModeCommands { get; private set; }

		#endregion

		protected ProfileSwitchToken profileSwitchToken { get; private set; }

		/// <summary>
		/// Gets the update manager to use to perform updates.
		/// </summary>
		/// <value>The update manager to use to perform updates.</value>
		protected UpdateManager UpdateManager { get; private set; }

		/// <summary>
		/// Gets the profile manager to use to switch mod profiles.
		/// </summary>
		/// <value>The profile manager to use to switch mod profiles.</value>
		public ProfileManager ProfileManager { get; private set; }

		/// <summary>
		/// Gets the mod manager to use to manage mods.
		/// </summary>
		/// <value>The mod manager to use to manage mods.</value>
		public ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the mod activation monitor.
		/// </summary>
		/// <value>The mod activation monitor.</value>
		public ModActivationMonitor ModActivationMonitor { get; private set; }

		/// <summary>
		/// Gets the virtual mod activator.
		/// </summary>
		/// <value>The virtual mod activator.</value>
		public IVirtualModActivator VirtualModActivator
		{
			get
			{
				return ModManager.VirtualModActivator;
			}
		}

		/// <summary>
		/// Gets the backup manager.
		/// </summary>
		/// <value>The backup manager.</value>
		public BackupManager BackupManager { get; private set; }

		/// <summary>
		/// Gets the plugin manager to use.
		/// </summary>
		/// <value>The plugin manager to use.</value>
		public IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the repository we are logging in to.
		/// </summary>
		/// <value>The repository we are logging in to.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for displaying the mod manager.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for displaying the mod manager.</value>
		public ModManagerVM ModManagerVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for displaying the plugin manager.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for displaying the plugin manager.</value>
		public PluginManagerVM PluginManagerVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for displaying the download monitor.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for displaying the download monitor.</value>
		public DownloadMonitorVM DownloadMonitorVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for displaying the mod activation monitor.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for displaying the mod activation monitor.</value>
		public ModActivationMonitorVM ModActivationMonitorVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for displaying the settings view.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for displaying the settings view.</value>
		public SettingsFormVM SettingsFormVM { get; private set; }

		/// <summary>
		/// Gets the command to show the tip.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument to show the tip.
		/// </remarks>
		/// <value>The command to tag a mod.</value>
		public Command<string> TipsCommand { get; set; }

		/// <summary>
		/// Gets the id of the game mode to which to change, if a game mode change
		/// has been requested.
		/// </summary>
		/// <remarks>
		/// This value is <c>null</c> if no game mode change has been requested.
		/// </remarks>
		/// <value>The id of the game mode to which to change, if a game mode change
		/// has been requested.</value>
		public string RequestedGameMode { get; private set; }

		/// <summary>
		/// Gets whether a default game mode change has been requested.
		/// </summary>
		/// <value>Whether a default game mode change has been requested.</value>
		public bool DefaultGameModeChangeRequested { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		public IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the name of the currently managed game mode.
		/// </summary>
		/// <value>The name of the currently managed game mode.</value>
		public string CurrentGameModeName
		{
			get
			{
				return GameMode.Name;
			}
		}

		/// <summary>
		/// Gets the help information.
		/// </summary>
		/// <value>The help information.</value>
		public HelpInformation HelpInfo { get; private set; }

		/// <summary>
		/// Gets the title of the form.
		/// </summary>
		/// <value>The title of the form.</value>
		public string Title
		{
			get
			{
				return String.Format("{0} ({1}) - {2}", EnvironmentInfo.Settings.ModManagerName, EnvironmentInfo.ApplicationVersion, GameMode.Name);
			}
		}

		/// <summary>
		/// Gets the current game mode theme.
		/// </summary>
		/// <value>The current game mode theme.</value>
		public Theme ModeTheme
		{
			get
			{
				return GameMode.ModeTheme;
			}
		}

		/// <summary>
		/// Gets the game launcher for the currently manage game.
		/// </summary>
		/// <value>The game launcher for the currently manage game.</value>
		public IGameLauncher GameLauncher
		{
			get
			{
				return GameMode.GameLauncher;
			}
		}

		/// <summary>
		/// Gets the tool launcher for the currently manage game.
		/// </summary>
		/// <value>The tool launcher for the currently manage game.</value>
		public IToolLauncher GameToolLauncher
		{
			get
			{
				return GameMode.GameToolLauncher;
			}
		}

		/// <summary>
		/// Gets the SupportedTools launcher for the currently manage game.
		/// </summary>
		/// <value>The SupportedTools launcher for the currently manage game.</value>
		public ISupportedToolsLauncher SupportedToolsLauncher
		{
			get
			{
				return GameMode.SupportedToolsLauncher;
			}
		}

		/// <summary>
		/// Gets the id of the selected game launch command.
		/// </summary>
		/// <value>The id of the selected game launch command.</value>
		public string SelectedGameLaunchCommandId
		{
			get
			{
				return EnvironmentInfo.Settings.SelectedLaunchCommands[GameMode.ModeId];
			}
			set
			{
				EnvironmentInfo.Settings.SelectedLaunchCommands[GameMode.ModeId] = value;
				EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets whether the game mode uses plugins.
		/// </summary>
		/// <value>Whether the game mode uses plugins.</value>
		public bool UsesPlugins
		{
			get
			{
				return GameMode.UsesPlugins;
			}
		}

		/// <summary>
		/// Gets whether the manager is in offline mode.
		/// </summary>
		/// <value>Whether the manager is in offline mode.</value>
		public bool OfflineMode
		{
			get
			{
				return ModRepository.IsOffline;
			}
		}

		/// <summary>
		/// Gets the Game root folder.
		/// </summary>
		/// <value>The path to the game folder.</value>
		public string GamePath
		{
			get
			{
				return GameMode.GameModeEnvironmentInfo.InstallationPath;
			}
		}

		/// <summary>
		/// Gets NMM's mods folder.
		/// </summary>
		/// <value>The path to NMM's mods folder.</value>
		public string ModsPath
		{
			get
			{
				return GameMode.GameModeEnvironmentInfo.ModDirectory;
			}
		}

		/// <summary>
		/// Gets NMM's Install Info folder.
		/// </summary>
		/// <value>The path to NMM's Install Info folder.</value>
		public string InstallInfoPath
		{
			get
			{
				return GameMode.GameModeEnvironmentInfo.InstallInfoDirectory;
			}
		}

		/// <summary>
		/// Gets the user membership status.
		/// </summary>
		/// <value>Gets the user membership status.</value>
		public string[] UserStatus
		{
			get
			{
				return ModRepository.UserStatus;
			}
		}

		/// <summary>
		/// Gets whether the manager is currently installing/uninstalling a mod.
		/// </summary>
		/// <value>Whether  the manager is currently installing/uninstalling a mod.</value>
		public bool IsInstalling
		{
			get
			{
				return ModActivationMonitor.IsInstalling;
			}
		}

		/// <summary>
		/// Whether the plugin sorter is properly initialized.
		/// </summary>
		public bool PluginSorterInitialized
		{
			get
			{
				return GameMode.PluginSorterInitialized;
			}
		}

		/// <summary>
		/// Whether the current game mode support the automatic plugin sorting.
		/// </summary>
		public bool SupportsPluginAutoSorting
		{
			get
			{
				return GameMode.SupportsPluginAutoSorting;
			}
		}

		public bool IsSwitching
		{
			get
			{
				return m_booIsSwitching;
			}
			set
			{
				m_booIsSwitching = value;
			}
		}

        public bool UsesModLoadOrder
        {
            get
            {
                return GameMode.UsesModLoadOrder;
            }
        }

		#endregion

		#region Settings

		public bool RequiresStartupWarning()
		{
			if (GameMode.ModeId.Equals("Fallout4", StringComparison.InvariantCultureIgnoreCase))
			{
				if (EnvironmentInfo.Settings.ShowFallout4UpgradeDisclaimer)
				{
					if (GameMode.GameVersion > new Version(1, 5, 0, 0))
					{
						EnvironmentInfo.Settings.ShowFallout4UpgradeDisclaimer = false;
						EnvironmentInfo.Settings.Save();

						return GameMode.LoadOrderManager.ObsoleteConfigFiles;
					}
				}
			}
			return false;
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmrInstalledGames">The registry of insalled games.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_mrpModRepository">The repository we are logging in to.</param>
		/// <param name="p_dmtMonitor">The download monitor to use to track task progress.</param>
		/// <param name="p_umgUpdateManager">The update manager to use to perform updates.</param>
		/// <param name="p_mmgModManager">The <see cref="ModManager"/> to use to manage mods.</param>
		/// <param name="p_pmgPluginManager">The <see cref="PluginManager"/> to use to manage plugins.</param>
		public MainFormVM(IEnvironmentInfo p_eifEnvironmentInfo, GameModeRegistry p_gmrInstalledGames, IGameMode p_gmdGameMode, IModRepository p_mrpModRepository, DownloadMonitor p_dmtMonitor, ModActivationMonitor p_mamMonitor, ModManager p_mmgModManager, IPluginManager p_pmgPluginManager)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameMode = p_gmdGameMode;
			GameMode.GameLauncher.GameLaunching += new CancelEventHandler(GameLauncher_GameLaunching);
			ModManager = p_mmgModManager;
			PluginManager = p_pmgPluginManager;
			ProfileManager = new ProfileManager(ModManager.VirtualModActivator, ModManager, p_mrpModRepository, p_eifEnvironmentInfo.Settings.ModFolder[GameMode.ModeId], GameMode.UsesPlugins);
			ModManager.SetProfileManager(ProfileManager);
			ModRepository = p_mrpModRepository;
			UpdateManager = new UpdateManager(GameMode, EnvironmentInfo);
			BackupManager = new BackupManager(ModManager, ProfileManager);
			ModManagerVM = new ModManagerVM(p_mmgModManager, ProfileManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode.ModeTheme);
			DownloadMonitorVM = new DownloadMonitorVM(p_dmtMonitor, p_eifEnvironmentInfo.Settings, p_mmgModManager, p_mrpModRepository);
			ModActivationMonitor = p_mamMonitor;
			ModActivationMonitorVM = new ModActivationMonitorVM(p_mamMonitor, p_eifEnvironmentInfo.Settings, p_mmgModManager);
			if (GameMode.UsesPlugins)
				PluginManagerVM = new PluginManagerVM(p_pmgPluginManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode, p_mamMonitor, ModManager.VirtualModActivator);
			HelpInfo = new HelpInformation(p_eifEnvironmentInfo);

			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo);
			foreach (IModFormat mftFormat in  p_mmgModManager.ModFormats)
				gsgGeneralSettings.AddFileAssociation(mftFormat.Extension, mftFormat.Name);

			ModOptionsSettingsGroup mosModOptions = new ModOptionsSettingsGroup(p_eifEnvironmentInfo);

			List<ISettingsGroupView> lstSettingGroups = new List<ISettingsGroupView>();
			lstSettingGroups.Add(new GeneralSettingsPage(gsgGeneralSettings));
			lstSettingGroups.Add(new ModOptionsPage(mosModOptions));
			DownloadSettingsGroup dsgDownloadSettings = new DownloadSettingsGroup(p_eifEnvironmentInfo, ModRepository);
			lstSettingGroups.Add(new DownloadSettingsPage(dsgDownloadSettings));
			
			if (p_gmdGameMode.SettingsGroupViews != null)
				lstSettingGroups.AddRange(p_gmdGameMode.SettingsGroupViews);

			SettingsFormVM = new SettingsFormVM(p_gmdGameMode, p_eifEnvironmentInfo, lstSettingGroups);

			UpdateCommand = new Command("Update", String.Format("Update {0}", EnvironmentInfo.Settings.ModManagerName), UpdateProgramme);
			LogoutCommand = new Command("Logout", "Logout", Logout);

			List<Command> lstChangeGameModeCommands = new List<Command>();
			List<IGameModeDescriptor> lstSortedModes = new List<IGameModeDescriptor>(p_gmrInstalledGames.RegisteredGameModes);
			lstSortedModes.Sort((x, y) => x.Name.CompareTo(y.Name));
			foreach (IGameModeDescriptor gmdInstalledGame in lstSortedModes)
			{
				string strId = gmdInstalledGame.ModeId;
				string strName = gmdInstalledGame.Name;
				string strDescription = String.Format("Change game to {0}", gmdInstalledGame.Name);
				Image imgCommandIcon = new Icon(gmdInstalledGame.ModeTheme.Icon, 32, 32).ToBitmap();
				lstChangeGameModeCommands.Add(new Command(strId, strName, strDescription, imgCommandIcon, () => ChangeGameMode(strId), true));
			}
			lstChangeGameModeCommands.Add(new Command("Change Default Game...", "Change Default Game", () => ChangeGameMode(CHANGE_DEFAULT_GAME_MODE)));
			lstChangeGameModeCommands.Add(new Command("Rescan Installed Games...", "Rescan Installed Games", () => ChangeGameMode(RESCAN_INSTALLED_GAMES)));
			ChangeGameModeCommands = lstChangeGameModeCommands;
		}

		#endregion

		#region New Mod Install Migration

		public bool RequiresModMigration()
		{
			if ((!ModManager.VirtualModActivator.Initialized || (Directory.Exists(ModManager.VirtualModActivator.VirtualPath) && (Directory.GetDirectories(ModManager.VirtualModActivator.VirtualPath).Length == 0))) && (ModManager.InstallationLog.ActiveMods.Count > 0))
				return true;
			else
				if (!ModManager.VirtualModActivator.Initialized)
				ModManager.VirtualModActivator.Setup();

			return false;
		}

		#endregion

		/// <summary>
		/// Requests a game mode change.
		/// </summary>
		private void ChangeGameMode(string p_strGameModeId)
		{
			switch (p_strGameModeId)
			{
				case CHANGE_DEFAULT_GAME_MODE:
					DefaultGameModeChangeRequested = true;
					break;
				case RESCAN_INSTALLED_GAMES:
					EnvironmentInfo.Settings.InstalledGamesDetected = false;
					EnvironmentInfo.Settings.Save();
					DefaultGameModeChangeRequested = true;
					break;
				default:
					RequestedGameMode = p_strGameModeId;
					break;
			}
		}

		/// <summary>
		/// Updates the programme.
		/// </summary>
		private void UpdateProgramme()
		{
			UpdateProgramme(false);
		}

		/// <summary>
		/// Automatically sorts the plugin list.
		/// </summary>
		public void SortPlugins()
		{
			if (GameMode.UsesPlugins)
				PluginManagerVM.SortPlugins();
		}

		/// <summary>
		/// The Automatic Download.
		/// </summary>
		public void AutomaticDownload(List<string> p_lstMissingMods, ProfileManager p_pmProfileManager)
		{
			ModManagerVM.AutomaticDownload(p_lstMissingMods, p_pmProfileManager);
		}

		/// <summary>
		/// Download the profile.
		/// </summary>
		public void CheckOnlineProfileIntegrity(IModProfile p_impProfile, Dictionary<string, string> p_dicMissingMods, string p_strGameModeID)
		{
			if (!ModRepository.IsOffline)
				CheckingOnlineProfileIntegrity(this, new EventArgs<IBackgroundTask>(ProfileManager.CheckOnlineProfileIntegrity(p_impProfile, p_dicMissingMods, p_strGameModeID, ConfirmUpdaterAction)));
			else
			{
				ModManager.Login();
				ProfileManager.AsyncCheckOnlineProfileIntegrity(p_impProfile, p_dicMissingMods, p_strGameModeID, ConfirmUpdaterAction);
			}
		}

		public void FixConfigFiles(List<string> p_lstFiles, IModProfile p_mprProfile)
		{
			ConfigFilesFixing(this, new EventArgs<IBackgroundTask>(VirtualModActivator.FixConfigFiles(p_lstFiles, p_mprProfile, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Updates the programme.
		/// </summary>
		/// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
		private void UpdateProgramme(bool p_booIsAutoCheck)
		{
			Updating(this, new EventArgs<IBackgroundTask>(UpdateManager.Update(ConfirmUpdaterAction, p_booIsAutoCheck)));
		}

		/// <summary>
		/// Switches the active profile.
		/// </summary>
		public void ProfileSwitch(IModProfile p_impProfile, IList<IVirtualModLink> p_lstNewLinks, IList<IVirtualModLink> p_lstRemoveLinks, bool p_booStartupMigration, bool p_booRestoring)
		{
			ProfileSwitching(this, new EventArgs<IBackgroundTask>(ProfileManager.SwitchProfile(p_impProfile, ModManager, p_lstNewLinks, p_lstRemoveLinks, p_booStartupMigration, p_booRestoring, ConfirmUpdaterAction)));
		}
		
		/// <summary>
		/// Performs the startup mod migration.
		/// </summary>
		public void MigrateMods(ModManagerControl p_mmgModManagerControl, bool p_booMigrate)
		{
			MigratingMods(this, new EventArgs<IBackgroundTask>(ModMigration(p_mmgModManagerControl, p_booMigrate)));
		}

		public void RequestGameMode(string p_strGameModeID)
		{
			RequestedGameMode = p_strGameModeID;
		}

		#region Backup Management

		/// <summary>
		/// Opens the create backup form.
		/// </summary>
		public void CreateBackup(MainForm p_mfMainForm)
		{
			BackupManager.Initialize();
			BackupManagerForm bnfBackupForm = new BackupManagerForm(BackupManager);
			bnfBackupForm.ShowDialog();

			if (bnfBackupForm.DialogResult == DialogResult.OK)
			{
				bnfBackupForm.Close();
				CreateBackup(p_mfMainForm, BackupManager);
			}
			else
				bnfBackupForm.Close();
		}

		/// <summary>
		/// Performs the destination folder selection.
		/// </summary>
		public void CreateBackup(MainForm p_mfMainForm, BackupManager p_bmBackupManager)
		{
			FolderBrowserDialog fbd = new FolderBrowserDialog();
			fbd.Description = string.Format("Select the folder where {0} will save the Backup Archive.", EnvironmentInfo.Settings.ModManagerName);
			fbd.ShowNewFolderButton = true;

			fbd.ShowDialog(p_mfMainForm);

			if (!string.IsNullOrWhiteSpace(fbd.SelectedPath))
			{
				DirectoryInfo directoryInfo = new DirectoryInfo(fbd.SelectedPath);
				foreach (Environment.SpecialFolder folder in Enum.GetValues(typeof(Environment.SpecialFolder)))
				{
					if (string.Equals(directoryInfo.FullName, Environment.GetFolderPath(folder), StringComparison.OrdinalIgnoreCase))
					{
						DialogResult drResult = ExtendedMessageBox.Show(null, "You cannot select a system folder!", "Wrong folder.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						CreateBackup(p_mfMainForm, p_bmBackupManager);
						break;
					}
				}
				CreatingBackup(this, new EventArgs<IBackgroundTask>(CreateBackupTask(fbd.SelectedPath, p_bmBackupManager)));
			}
		}

		/// <summary>
		/// Sets up the create backup task.
		/// </summary>
		private IBackgroundTask CreateBackupTask(string p_strSelectedPath, BackupManager p_bmBackupManager)
		{
			CreateBackupTask bmtBackupManagerTask = new CreateBackupTask(ModManager.VirtualModActivator, ModManager, EnvironmentInfo, PluginManagerVM, PluginManager, ProfileManager, p_strSelectedPath, p_bmBackupManager, ConfirmUpdaterAction);
			bmtBackupManagerTask.Update(ConfirmUpdaterAction);
			return bmtBackupManagerTask;
		}

		/// <summary>
		/// Performs the restore backup form.
		/// </summary>
		public void RestoreBackup(ModManagerControl p_mmgModManager)
		{
			RestoreBackupForm rbfRestoreForm = new RestoreBackupForm(ModManager, ProfileManager);
			rbfRestoreForm.ShowDialog();

			if (rbfRestoreForm.DialogResult == DialogResult.Cancel)
				rbfRestoreForm.Close();
			else if (!string.IsNullOrEmpty(rbfRestoreForm.Text))
			{
				rbfRestoreForm.Close();

				if (rbfRestoreForm.DialogResult == DialogResult.Yes)
					if (ProfileManager.CurrentProfile != null)
						ProfileManager.RemoveProfile(ProfileManager.CurrentProfile);
					else
						ProfileManager.SetCurrentProfile(null);


				ProfileManager.SetCurrentProfile(null);
				p_mmgModManager.DeactivateAllMods(true, true);
				RestoreBackup(rbfRestoreForm, (rbfRestoreForm.DialogResult == DialogResult.Yes));
			}
		}

		public void RestoreBackup(RestoreBackupForm p_rbfRestoreForm, bool p_booPurgeFolders)
		{
			RestoringBackup(this, new EventArgs<IBackgroundTask>(RestoreBackup(p_rbfRestoreForm.strText, p_booPurgeFolders)));
		}

		/// <summary>
		/// Sets up the restore backup task.
		/// </summary>
		private IBackgroundTask RestoreBackup(string p_strFileName, bool p_booPurgeFolders)
		{
			RestoreBackupTask bmtBackupManagerTask = new RestoreBackupTask(ModManager.VirtualModActivator, ModManager, ProfileManager, EnvironmentInfo, p_strFileName, p_booPurgeFolders, ConfirmUpdaterAction);
			bmtBackupManagerTask.Update(ConfirmUpdaterAction);
			return bmtBackupManagerTask;
		}

		public void PurgeLooseFiles()
		{
			PurgingLooseFiles(this, new EventArgs<IBackgroundTask>(PurgeLooseFiles(BackupManager)));
		}

		/// <summary>
		/// Sets up the restore backup task.
		/// </summary>
		private IBackgroundTask PurgeLooseFiles(BackupManager p_BackupManager)
		{
			PurgeLooseFilesTask plfPurgeLooseFilesTask = new PurgeLooseFilesTask(p_BackupManager, ConfirmUpdaterAction);
			plfPurgeLooseFilesTask.Update(ConfirmUpdaterAction);
			return plfPurgeLooseFilesTask;
		}

		#endregion

		/// <summary>
		/// Sets up the mod migration task.
		/// </summary>
		private IBackgroundTask ModMigration(ModManagerControl p_mmgModManagerControl, bool p_booMigrate)
		{
			ModMigrationTask mmtModMigrationTask = new ModMigrationTask(this, p_mmgModManagerControl, p_booMigrate, ConfirmUpdaterAction);
			if (VirtualModActivator.GameMode.LoadOrderManager != null)
				VirtualModActivator.GameMode.LoadOrderManager.MonitorExternalTask(mmtModMigrationTask);
			else
				mmtModMigrationTask.Update(ConfirmUpdaterAction);
			return mmtModMigrationTask;
		}
			
		/// <summary>
		/// Updates the programme.
		/// </summary>
		/// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
		public void ProfilePluginImport()
		{
			IModProfile impCurrentProfile = ProfileManager.CurrentProfile;
			if (impCurrentProfile != null)
			{
				if ((impCurrentProfile.LoadOrder != null) && (impCurrentProfile.LoadOrder.Count > 0))
					PluginManagerVM.ImportLoadOrderFromDictionary(impCurrentProfile.LoadOrder);
				else
				{
					Dictionary<string, string> dicProfile;
					ProfileManager.LoadProfile(impCurrentProfile, out dicProfile);
					if ((dicProfile != null) && (dicProfile.Count > 0) && (dicProfile.ContainsKey("loadorder")))
					{
						PluginManagerVM.ImportLoadOrderFromString(dicProfile["loadorder"]);
					}
				}
			}
		}

		/// <summary>
		/// Applies the load order specified by the given list of registered plugins
		/// </summary>
		/// <param name="p_kvpRegisteredPlugins">The list of registered plugins.</param>
		/// <param name="p_booSortingOnly">Whether we just want to apply the sorting.</param>
		public void ApplyLoadOrder(Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly)
		{
			ApplyingImportedLoadOrder(this, new EventArgs<IBackgroundTask>(PluginManager.ApplyLoadOrder(p_kvpRegisteredPlugins, p_booSortingOnly)));
		}

		/// <summary>
		/// Gets the loadorder from the profile.
		/// </summary>
		/// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
		public Dictionary<Plugin, string> ImportProfileLoadOrder()
		{
			IModProfile impCurrentProfile = ProfileManager.CurrentProfile;
			if (impCurrentProfile != null)
			{
				if ((impCurrentProfile.LoadOrder != null) && (impCurrentProfile.LoadOrder.Count > 0))
					PluginManagerVM.ImportLoadOrderFromDictionary(impCurrentProfile.LoadOrder);
				else
				{
					Dictionary<string, string> dicProfile;
					ProfileManager.LoadProfile(impCurrentProfile, out dicProfile);
					if ((dicProfile != null) && (dicProfile.Count > 0) && (dicProfile.ContainsKey("loadorder")))
					{
						return PluginManagerVM.ParseLoadOrderFromString(dicProfile["loadorder"]);
					}
				}
			}

			return null;
		}

		public List<string> CheckMissingModsList(Dictionary<string, string> p_dctMissingMods, IModProfile p_impProfile)
		{
			List<string> lstMissingMods = new List<string>();

			bool booLoaded = ProfileManager.LoadProfileFileList(p_impProfile);

			if (booLoaded)
			{
				foreach (string fileName in p_dctMissingMods.Values)
				{
					IVirtualModInfo Mod = p_impProfile.ModList.Find(x => x.ModFileName == fileName);

					if (Mod != null)
					{
						bool booAlreadyDownloading = false;
						string strUrl = @"nxm://" + ModManager.GameMode.ModeId + "/mods/" + Mod.ModId + "/files/" + Mod.DownloadId;

						foreach (AddModTask adtTask in DownloadMonitorVM.Tasks)
						{
							if (adtTask.ReturnValue != null)
							{
								if (string.Equals(adtTask.ReturnValue.ToString(), strUrl.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
								{
									booAlreadyDownloading = true;
									break;
								}
							}
						}

						if (!booAlreadyDownloading)
							lstMissingMods.Add(strUrl);
					}
				}
			}

			return lstMissingMods;
		}

		/// <summary>
		/// Notifies the view model that the view has been displayed.
		/// </summary>
		public void ViewIsShown()
		{
			if (EnvironmentInfo.Settings.CheckForUpdatesOnStartup)
			{
				if (String.IsNullOrEmpty(EnvironmentInfo.Settings.LastUpdateCheckDate))
				{
					UpdateProgramme(true);
					EnvironmentInfo.Settings.LastUpdateCheckDate = DateTime.Today.ToShortDateString();
					EnvironmentInfo.Settings.Save();
				}
				else
				{
					try
					{
						if ((DateTime.Today - Convert.ToDateTime(EnvironmentInfo.Settings.LastUpdateCheckDate)).TotalDays >= EnvironmentInfo.Settings.UpdateCheckInterval)
						{
							UpdateProgramme(true);
							EnvironmentInfo.Settings.LastUpdateCheckDate = DateTime.Today.ToShortDateString();
							EnvironmentInfo.Settings.Save();
						}
					}
					catch
					{
						EnvironmentInfo.Settings.LastUpdateCheckDate = "";
						EnvironmentInfo.Settings.Save();
					}
				}
			}
		}

		public void DownloadProfileMissingMods(Form p_frmParent, IModProfile p_impProfile)
		{
		}

		public void SwitchProfile(Form p_frmParent, IModProfile p_impProfile, bool p_booSilentInstall, bool p_booRestoring)
		{
			if (p_booSilentInstall || (ProfileManager.CurrentProfile == null) || (p_impProfile.Id != ProfileManager.CurrentProfile.Id))
			{
				List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
				Dictionary<string, string> dicProfile = null;
				List<string> lstScriptedMismatch = new List<string>();
				List<IVirtualModInfo> lstMissingModInfo = new List<IVirtualModInfo>();

				if (!p_booSilentInstall || !(ProfileManager.CurrentProfile == null))
				{
					lstScriptedMismatch = ProfileManager.CheckScriptedInstallersIntegrity(ProfileManager.CurrentProfile, p_impProfile);
				}

				ProfileManager.LoadProfile(p_impProfile, out dicProfile);
				if ((dicProfile != null) && (dicProfile.Count > 0) && dicProfile.ContainsKey("modlist"))
				{
					lstVirtualLinks = ModManager.VirtualModActivator.LoadImportedList(dicProfile["modlist"], ProfileManager.GetProfilePath(p_impProfile));
					ModManager.VirtualModActivator.CheckLinkListIntegrity(lstVirtualLinks, out lstMissingModInfo, lstScriptedMismatch);
				}

				profileSwitchToken = new ProfileSwitchToken(p_booSilentInstall, p_impProfile, lstVirtualLinks, lstScriptedMismatch, lstMissingModInfo, dicProfile);

				if (!p_booSilentInstall && (lstMissingModInfo.Count > 0) && (p_impProfile.IsOnline && !string.IsNullOrEmpty(p_impProfile.OnlineID)))
				{
					Dictionary<string, string> MissingMods = new Dictionary<string, string>();

					foreach (IVirtualModInfo vmi in lstMissingModInfo)
					{
						List<IMod> lstManagedMods = new List<IMod>(ModManager.ManagedMods.ToList());
						IMod modMod = lstManagedMods.Find(x => GetIsSameMod(x, vmi));
						if (modMod == null)
							MissingMods.Add(vmi.ModFileName, vmi.DownloadId);
					}

					if (MissingMods.Count > 0)
					{
						bool booLoaded = ProfileManager.LoadProfileFileList(p_impProfile);

						if (booLoaded)
							CheckOnlineProfileIntegrity(p_impProfile, MissingMods, ModManager.GameMode.ModeId);
					
						return;
					}
				}

				ExecuteProfileSwitch(p_frmParent, p_booRestoring);
			}
		}

		public void ExecuteProfileSwitch(Form p_frmParent, bool p_booRestoring)
		{
			m_booIsSwitching = true;
			IModProfile impCurrentProfile = ProfileManager.CurrentProfile;

			if (((profileSwitchToken.ScriptedMismatchList != null) && (profileSwitchToken.ScriptedMismatchList.Count > 0)) || ((profileSwitchToken.MissingMods != null) && (profileSwitchToken.MissingMods.Count > 0)))
			{
				System.Text.StringBuilder sbMessage = new System.Text.StringBuilder();
				string strDetails;

				sbMessage.AppendLine("The selected profile contains files from mods not currently installed");
				sbMessage.AppendLine("The manager will automatically reinstall the needed files. Click CANCEL if you want to skip this step.");
				sbMessage.AppendLine();
				sbMessage.AppendLine("Depending on the mod, leaving it uninstalled could cause in game crashes.");
				System.Text.StringBuilder sbDetails = new System.Text.StringBuilder();

				bool booFoundOne = false;
				List<IMod> lstFoundMods = new List<IMod>();
				List<IMod> lstScriptedMods = new List<IMod>();
				List<IMod> lstManagedMods = new List<IMod>(ModManager.ManagedMods.ToList());
				foreach (IVirtualModInfo vmi in profileSwitchToken.MissingMods)
				{
					IMod modMod = lstManagedMods.Find(x => GetIsSameMod(x, vmi));
					if (modMod != null)
					{
						if (!booFoundOne)
							booFoundOne = true;
						lstFoundMods.Add(modMod);
					}

					sbDetails.AppendFormat("- Mod: {0} - filename: {1} - present: {2}", vmi.ModName, vmi.ModFileName, (modMod != null) ? "Yes" : "No").AppendLine();
				}

				if ((profileSwitchToken.ScriptedMismatchList != null) && (profileSwitchToken.ScriptedMismatchList.Count > 0))
					lstScriptedMods = lstManagedMods.Where(x => profileSwitchToken.ScriptedMismatchList.Contains(Path.GetFileName(x.Filename), StringComparer.CurrentCultureIgnoreCase)).ToList();

				ModManager.VirtualModActivator.DisableLinkCreation = true;

				if ((lstScriptedMods != null) && (lstScriptedMods.Count > 0))
				{
					ProfileManager.SetCurrentProfile(null);
					ThreadSafeObservableList<IMod> oclMods = new ThreadSafeObservableList<IMod>(lstScriptedMods);
					ModManagerVM.DeactivateMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), true, true, true);
					ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
					ModManagerVM.MultiModInstall(lstScriptedMods, false);
					ProfileManager.SetCurrentProfile(impCurrentProfile);
				}

				if (booFoundOne)
				{
					if (sbDetails.Length > 0)
						strDetails = sbDetails.ToString();
					else
						strDetails = null;

					if (profileSwitchToken.IsSilent)
					{
						ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
						ModManagerVM.MultiModInstall(lstFoundMods, false);
						ProfileManager.SetCurrentProfile(impCurrentProfile);
					}
					else
					{
						DialogResult drResult = ExtendedMessageBox.Show(p_frmParent, sbMessage.ToString(), ModManagerVM.Settings.ModManagerName, strDetails, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
						if (drResult == DialogResult.Yes)
						{
							if (lstFoundMods.Count > 0)
							{
								ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
								ModManagerVM.MultiModInstall(lstFoundMods, false);
								ProfileManager.SetCurrentProfile(impCurrentProfile);
							}
						}
						else if (drResult == DialogResult.Cancel)
						{
							ModManager.VirtualModActivator.DisableLinkCreation = false;
							m_booIsSwitching = false;
							ProfileManager.SetCurrentProfile(impCurrentProfile);
							AbortedProfileSwitch(this, new EventArgs());
							return;
						}
					}
				}
			}

			ModManager.VirtualModActivator.PurgeIniEdits();
			if ((profileSwitchToken.ProfileDictionary != null) && (profileSwitchToken.ProfileDictionary.Count > 0) && profileSwitchToken.ProfileDictionary.ContainsKey("iniEdits"))
				ModManager.VirtualModActivator.ImportIniEdits(profileSwitchToken.ProfileDictionary["iniEdits"]);

			if (GameMode.RequiresOptionalFilesCheckOnProfileSwitch)
				if ((profileSwitchToken.ProfileDictionary != null) && (profileSwitchToken.ProfileDictionary.Count > 0) && profileSwitchToken.ProfileDictionary.ContainsKey("optional"))
				{
					string[] strFiles = profileSwitchToken.ProfileDictionary["optional"].Split("#".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
					if ((strFiles != null) && (strFiles.Length > 0))
						GameMode.SetOptionalFilesList(strFiles);
					if (PluginManager != null)
						foreach (string strFile in strFiles)
						{
							if (PluginManager.IsActivatiblePluginFile(strFile))
								PluginManager.AddPlugin(strFile);
						}
				}

			List<IVirtualModLink> lstMissingLinks = new List<IVirtualModLink>();
			List<IVirtualModLink> lstUnneededLinks = new List<IVirtualModLink>();

			lstMissingLinks = profileSwitchToken.VirtualLinks.Except(VirtualModActivator.VirtualLinks, new VirtualModLinkEqualityComparer()).ToList();
			lstUnneededLinks = VirtualModActivator.VirtualLinks.Except(profileSwitchToken.VirtualLinks, new VirtualModLinkEqualityComparer()).ToList();

			ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
			ModManager.VirtualModActivator.DisableLinkCreation = false;
			ProfileSwitch(profileSwitchToken.Profile, lstMissingLinks, lstUnneededLinks, profileSwitchToken.IsSilent, p_booRestoring);
		}

		private bool GetIsSameMod(IMod p_modMod, IVirtualModInfo p_vmiModInfo)
		{
			string strFilename = Path.GetFileName(p_modMod.Filename);
			if (strFilename.Equals(p_vmiModInfo.ModFileName, StringComparison.InvariantCultureIgnoreCase))
				return true;

			if (!string.IsNullOrEmpty(p_modMod.DownloadId) && !string.IsNullOrEmpty(p_vmiModInfo.DownloadId))
				if (p_modMod.DownloadId.Equals(p_vmiModInfo.DownloadId))
					return true;

			if (!string.IsNullOrEmpty(p_modMod.DownloadId) && !string.IsNullOrEmpty(p_vmiModInfo.UpdatedDownloadId))
				if (p_modMod.DownloadId.Equals(p_vmiModInfo.UpdatedDownloadId))
					return true;

			return false;
		}

		public void PurgeSwitchToken()
		{
			profileSwitchToken = null;
		}
		
		public bool? CheckAlreadyDownloading(string p_strUrl, string p_strKey)
		{
			foreach (AddModTask adtTask in DownloadMonitorVM.Tasks)
			{
				if (!string.IsNullOrEmpty(adtTask.DescriptorSourcePath))
				{
					if (Path.GetFileName(adtTask.DescriptorSourcePath).Equals(p_strKey, StringComparison.OrdinalIgnoreCase))
					{
						if ((adtTask.Status == TaskStatus.Incomplete) || (adtTask.Status == TaskStatus.Paused))
							return null;
						else
							return true;
					}
					else if (adtTask.SourceUri.Equals(p_strUrl) && adtTask.Status == TaskStatus.Paused)
						return null;
				}
				else if (adtTask.SourceUri.Equals(p_strUrl) && adtTask.Status == TaskStatus.Paused)
					return null;
			}
			return false;
		}

		public void ResumeIncompleteDownloads(List<string> p_lstIncompleteDownloads)
		{
			List<AddModTask> lstAddModTask = new List<AddModTask>();
			
			foreach (AddModTask adtTask in DownloadMonitorVM.Tasks)
			{
				if (adtTask.ReturnValue != null)
				{
					if (p_lstIncompleteDownloads.Contains(adtTask.ReturnValue.ToString(), StringComparer.OrdinalIgnoreCase) && ((adtTask.Status == TaskStatus.Incomplete) || (adtTask.Status == TaskStatus.Paused)))
						lstAddModTask.Add(adtTask);
				}
			}

			if(lstAddModTask.Count > 0)
				DownloadMonitorVM.ResumeAllTasks(lstAddModTask);
		}

		private class VirtualModLinkEqualityComparer : IEqualityComparer<IVirtualModLink>
		{
			public bool Equals(IVirtualModLink x, IVirtualModLink y)
			{
				if (object.ReferenceEquals(x, y))
					return true;
				if (x == null || y == null)
					return false;
				return (x.RealModPath.Equals(y.RealModPath, StringComparison.InvariantCultureIgnoreCase) && x.VirtualModPath.Equals(y.VirtualModPath, StringComparison.InvariantCultureIgnoreCase));
			}

			public int GetHashCode(IVirtualModLink obj)
			{
				return obj.RealModPath.GetHashCode();
			}
		}

		/// <summary>
		/// Handles the <see cref="IGameLauncher.GameLaunching"/> event of the game launcher.
		/// </summary>
		/// <remarks>This displays, as appropriate, a message asking if the user wants the application to close
		/// after game launch.</remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		private void GameLauncher_GameLaunching(object sender, CancelEventArgs e)
		{
			if (!EnvironmentInfo.Settings.CloseModManagerAfterGameLaunchIsRemembered)
			{
				bool booRemember = false;
				bool booClose = ConfirmCloseAfterGameLaunch(out booRemember);
				EnvironmentInfo.Settings.CloseModManagerAfterGameLaunchIsRemembered = booRemember;
				EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch = booClose;
				EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Logs out of all mod repositories.
		/// </summary>
		private void Logout()
		{
			lock (ModRepository)
				if (ModRepository.IsOffline)
					ModManager.Login();
				else
				{
					ModRepository.Logout();
					ModManager.Logout();
					EnvironmentInfo.Settings.RepositoryAuthenticationTokens.Remove(ModRepository.Id);
					EnvironmentInfo.Settings.Save();
				}
		}
	}
}
