namespace Nexus.Client
{
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Drawing;
	using System.IO;
	using System.Linq;
	using System.Windows.Forms;
	using DevExpress.XtraEditors;
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
	using Pathoschild.FluentNexus.Models;
	using System.Configuration;

	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display the main form.
	/// </summary>
	public class MainFormVM
	{
		#region ProfileSwitchToken
		/// <summary>
		/// Stores the target and rollback state for an in-progress profile switch.
		/// </summary>
		protected class ProfileSwitchToken
		{
			#region Properties

			public bool IsSilent { get; }

			public bool IsRestoring { get; }

			public IModProfile Profile { get; }

			/// <summary>
			/// Gets the profile that was active before the switch started.
			/// </summary>
			public IModProfile PreviousProfile { get; }

			public List<IVirtualModLink> VirtualLinks { get; }

			/// <summary>
			/// Gets the virtual-link snapshot that was active before the switch started.
			/// </summary>
			public List<IVirtualModLink> PreviousVirtualLinks { get; }

			public List<IVirtualModInfo> MissingMods { get; }

			public List<string> ScriptedMismatchList { get; }

			public Dictionary<string, string> ProfileDictionary { get; }

			/// <summary>
			/// Gets the serialized settings of the profile that was active before the switch started.
			/// </summary>
			public Dictionary<string, string> PreviousProfileDictionary { get; }

			/// <summary>
			/// Gets the live plugin order and active state captured before the switch started.
			/// </summary>
			public string PreviousLoadOrder { get; }

			#endregion

			/// <summary>
			/// Initializes the state required to complete or roll back a profile switch.
			/// </summary>
			/// <param name="isSilent">Whether the profile switch suppresses normal user prompts.</param>
			/// <param name="isRestoring">Whether the switch is restoring a backup profile.</param>
			/// <param name="modProfile">The target profile.</param>
			/// <param name="previousProfile">The profile active before the switch.</param>
			/// <param name="virtualLinks">The virtual links required by the target profile.</param>
			/// <param name="previousVirtualLinks">The virtual links active before the switch.</param>
			/// <param name="scriptedMismatch">The scripted installers that require reconciliation.</param>
			/// <param name="missingMods">The target profile mods whose deployed files are missing.</param>
			/// <param name="profiles">The serialized target profile files.</param>
			/// <param name="previousProfiles">The serialized previous profile files.</param>
			/// <param name="previousLoadOrder">The live plugin order and active state captured before switching.</param>
			public ProfileSwitchToken(bool isSilent, bool isRestoring, IModProfile modProfile, IModProfile previousProfile, List<IVirtualModLink> virtualLinks, List<IVirtualModLink> previousVirtualLinks, List<string> scriptedMismatch, List<IVirtualModInfo> missingMods, Dictionary<string, string> profiles, Dictionary<string, string> previousProfiles, string previousLoadOrder)
			{
				IsSilent = isSilent;
				IsRestoring = isRestoring;
				Profile = modProfile;
				PreviousProfile = previousProfile;
				VirtualLinks = virtualLinks;
				PreviousVirtualLinks = previousVirtualLinks;
				MissingMods = missingMods;
				ScriptedMismatchList = scriptedMismatch;
				ProfileDictionary = profiles;
				PreviousProfileDictionary = previousProfiles;
				PreviousLoadOrder = previousLoadOrder;
			}
		}

		#endregion

		private const string CHANGE_DEFAULT_GAME_MODE = "__changedefaultgamemode";
		private const string RESCAN_INSTALLED_GAMES = "__rescaninstalledgames";
		private bool m_booIsSwitching;

		#region Events

		/// <summary>
		/// Raised when the program is being updated.
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
		/// Gets the command to update the program.
		/// </summary>
		/// <value>The command to update the program.</value>
		public Command UpdateCommand { get; private set; }

		/// <summary>
		/// Gets the command to log in/out of the current mod repository.
		/// </summary>
		/// <value>The command to in/out of the current mod repository.</value>
		public Command ToggleLoginCommand { get; private set; }

		/// <summary>
		/// Gets the commands to change the managed game mode.
		/// </summary>
		/// <value>The commands to change the managed game mode.</value>
		public IEnumerable<Command> ChangeGameModeCommands { get; }

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
		public IVirtualModActivator VirtualModActivator => ModManager.VirtualModActivator;

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
		public string CurrentGameModeName => GameMode.Name;

		/// <summary>
		/// Gets the help information.
		/// </summary>
		/// <value>The help information.</value>
		public HelpInformation HelpInfo { get; private set; }

		/// <summary>
		/// Gets the title of the form.
		/// </summary>
		/// <value>The title of the form.</value>
		public string Title => $"{CommonData.ModManagerName} ({EnvironmentInfo.ApplicationVersion}) - {GameMode.Name}";

		/// <summary>
		/// Gets the current game mode theme.
		/// </summary>
		/// <value>The current game mode theme.</value>
		public Theme ModeTheme => GameMode.ModeTheme;

		/// <summary>
		/// Gets the game launcher for the currently manage game.
		/// </summary>
		/// <value>The game launcher for the currently manage game.</value>
		public IGameLauncher GameLauncher => GameMode.GameLauncher;

		/// <summary>
		/// Gets the tool launcher for the currently manage game.
		/// </summary>
		/// <value>The tool launcher for the currently manage game.</value>
		public IToolLauncher GameToolLauncher => GameMode.GameToolLauncher;

		/// <summary>
		/// Gets the SupportedTools launcher for the currently manage game.
		/// </summary>
		/// <value>The SupportedTools launcher for the currently manage game.</value>
		public ISupportedToolsLauncher SupportedToolsLauncher => GameMode.SupportedToolsLauncher;

		/// <summary>
		/// Gets the id of the selected game launch command.
		/// </summary>
		/// <value>The id of the selected game launch command.</value>
		public string SelectedGameLaunchCommandId
		{
			get => EnvironmentInfo.Settings.SelectedLaunchCommands[GameMode.ModeId];
			set
			{
				EnvironmentInfo.Settings.SelectedLaunchCommands[GameMode.ModeId] = value;
				EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Gets the application's environment info.
		/// </summary>
		/// <value>The application's environment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets whether the game mode uses plugins.
		/// </summary>
		/// <value>Whether the game mode uses plugins.</value>
		public bool UsesPlugins => GameMode.UsesPlugins;

		/// <summary>
		/// Gets whether the manager is in offline mode.
		/// </summary>
		/// <value>Whether the manager is in offline mode.</value>
		public bool OfflineMode => ModRepository.IsOffline;

		/// <summary>
		/// Gets the Game root folder.
		/// </summary>
		/// <value>The path to the game folder.</value>
		public string GamePath => GameMode.GameModeEnvironmentInfo.InstallationPath;

		/// <summary>
		/// Gets NMM's mods folder.
		/// </summary>
		/// <value>The path to NMM's mods folder.</value>
		public string ModsPath => GameMode.GameModeEnvironmentInfo.ModDirectory;

		/// <summary>
		/// Gets NMM's cache folder.
		/// </summary>
		/// <value>The path to NMM's cache folder.</value>
		public string CachePath => GameMode.GameModeEnvironmentInfo.ModCacheDirectory;

		/// <summary>
		/// Gets NMM's Install Info folder.
		/// </summary>
		/// <value>The path to NMM's Install Info folder.</value>
		public string InstallInfoPath => GameMode.GameModeEnvironmentInfo.InstallInfoDirectory;

		/// <summary>
		/// Gets NMM's Config folder.
		/// </summary>
		/// <value>The path to NMM's Config folder.</value>
		public string ConfigPath => Path.GetDirectoryName(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath);

		/// <summary>
		/// Gets the user membership status.
		/// </summary>
		/// <value>Gets the user membership status.</value>
		public User UserStatus => ModRepository.UserStatus;

		/// <summary>
		/// Gets whether the manager is currently installing/uninstalling a mod.
		/// </summary>
		/// <value>Whether  the manager is currently installing/uninstalling a mod.</value>
		public bool IsInstalling => ModActivationMonitor.IsInstalling;

		/// <summary>
		/// Whether the plugin sorter is properly initialized.
		/// </summary>
		public bool PluginSorterInitialized => GameMode.PluginSorterInitialized;

		/// <summary>
		/// Whether the current game mode support the automatic plugin sorting.
		/// </summary>
		public bool SupportsPluginAutoSorting => GameMode.SupportsPluginAutoSorting;

		public bool IsSwitching
		{
			get => m_booIsSwitching;
			set => m_booIsSwitching = value;
		}

		public bool UsesModLoadOrder => GameMode.UsesModLoadOrder;

		#endregion

		#region Settings

		public string RequiresStartupWarning()
		{
			if (GameMode.ModeId.Equals("Fallout4", StringComparison.InvariantCultureIgnoreCase))
			{
				if (EnvironmentInfo.Settings.ShowFallout4UpgradeDisclaimer)
				{
					if (GameMode.GameVersion > new Version(1, 5, 0, 0))
					{
						EnvironmentInfo.Settings.ShowFallout4UpgradeDisclaimer = false;
						EnvironmentInfo.Settings.Save();

						if (GameMode.LoadOrderManager.ObsoleteConfigFiles)
						{
							string strWarning = "We've detected that you are using Fallout 4 version 1.5 (or later) for the first time with NMM. In version 1.5, " + Environment.NewLine +
								"Bethesda changed the way in which plugins were handled." + Environment.NewLine + Environment.NewLine +
								"Because of this, any plugin you previously had enabled will be disabled in NMM and you will need to reactivate " + Environment.NewLine +
								"them in order for your setup to work again." + Environment.NewLine + Environment.NewLine +
								"Unfortunately this is a side-effect of Bethesda's patching of Fallout 4 and nothing to do with us. Unless " + Environment.NewLine +
								"Bethesda change the modding method again in future patches you will only need to do this reactivation once " + Environment.NewLine + "(e.g. this won't happen every time you start NMM in the future!)." + Environment.NewLine + Environment.NewLine +
								"NOTE: If you are making use of NMM's profile system, simply go to the profile menu and select 'Import Load Order' " + Environment.NewLine +
								"from your profile, NMM will automatically reactivate all your plugins in the correct order for you." + Environment.NewLine + Environment.NewLine +
								"If you are not using the profiling system, you will need to activate all your plugins again manually.";

							return strWarning;
						}
					}
				}
			}
			else if (GameMode.ModeId.Equals("Cyberpunk2077", StringComparison.InvariantCultureIgnoreCase))
			{
				if (EnvironmentInfo.Settings.ShowCP2077UpgradeDisclaimer)
				{
					if (GameMode.GameVersion >= new Version(1, 2))
					{
						EnvironmentInfo.Settings.ShowCP2077UpgradeDisclaimer = false;
						EnvironmentInfo.Settings.Save();

						string strWarning = "We've detected that you are using Cyberpunk 2077 version 1.2 (or later) for the first time with NMM. In version 1.2, " + Environment.NewLine +
							"CDPR changed the folder where mod files are installed." + Environment.NewLine + Environment.NewLine +
							"Older NMM versions tried to disable every active Cyberpunk 2077 mod at this point. NMM will no longer do that automatically." + Environment.NewLine + Environment.NewLine +
							"Your existing files will be left in place. Please verify your Cyberpunk 2077 mod folder settings and reinstall or reactivate affected mods manually if your setup needs to be moved to the new folder." + Environment.NewLine;

						return strWarning;
					}
				}
			}

			return string.Empty;
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
			{
				PluginManagerVM = new PluginManagerVM(p_pmgPluginManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode, p_mamMonitor, ModManager.VirtualModActivator);
			}

			HelpInfo = new HelpInformation(p_eifEnvironmentInfo);

			var gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo);
			var gsgAssociationSettings = new OsSettingsGroup(p_eifEnvironmentInfo);

			foreach (var mftFormat in p_mmgModManager.ModFormats)
			{
				gsgAssociationSettings.AddFileAssociation(mftFormat.Extension, mftFormat.Name);
			}

			var mosModOptions = new ModOptionsSettingsGroup(p_eifEnvironmentInfo);

			var lstSettingGroups = new List<ISettingsGroupView>
			{
				new GeneralSettingsPage(gsgGeneralSettings),
				new OsSettingsPage(gsgAssociationSettings),
				new ModOptionsPage(mosModOptions)
			};

			var dsgDownloadSettings = new DownloadSettingsGroup(p_eifEnvironmentInfo, ModRepository);
			lstSettingGroups.Add(new DownloadSettingsPage(dsgDownloadSettings));

			if (p_gmdGameMode.SettingsGroupViews != null)
			{
				lstSettingGroups.AddRange(p_gmdGameMode.SettingsGroupViews);
			}

			SettingsFormVM = new SettingsFormVM(p_gmdGameMode, p_eifEnvironmentInfo, lstSettingGroups);

			UpdateCommand = new Command("Update", $"Update {CommonData.ModManagerName}", UpdateProgram);
			ToggleLoginCommand = new Command("ToggleLogin", "Login/Logout", ToggleLogin);

			var lstChangeGameModeCommands = new List<Command>();
			var lstSortedModes = new List<IGameModeDescriptor>(p_gmrInstalledGames.RegisteredGameModes);
			lstSortedModes.Sort((x, y) => x.Name.CompareTo(y.Name));

			foreach (var gmdInstalledGame in lstSortedModes)
			{
				var strId = gmdInstalledGame.ModeId;
				var strName = gmdInstalledGame.Name;
				var strDescription = $"Change game to {gmdInstalledGame.Name}";
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
			if ((!ModManager.VirtualModActivator.Initialized || Directory.Exists(ModManager.VirtualModActivator.VirtualPath) && Directory.GetDirectories(ModManager.VirtualModActivator.VirtualPath).Length == 0) && ModManager.InstallationLog.ActiveMods.Count > 0)
			{
				return true;
			}

			if (!ModManager.VirtualModActivator.Initialized)
			{
				ModManager.VirtualModActivator.Setup();
			}

			return false;
		}

		#endregion

		/// <summary>
		/// Requests a game mode change.
		/// </summary>
		private void ChangeGameMode(string gameModeId)
		{
			switch (gameModeId)
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
					RequestedGameMode = gameModeId;
					break;
			}
		}

		/// <summary>
		/// Updates the program.
		/// </summary>
		private void UpdateProgram()
		{
			UpdateProgram(false);
		}

		/// <summary>
		/// Automatically sorts the plugin list.
		/// </summary>
		public void SortPlugins()
		{
			if (GameMode.UsesPlugins)
			{
				PluginManagerVM.SortPlugins();
			}
		}

		/// <summary>
		/// The Automatic Download.
		/// </summary>
		public void AutomaticDownload(List<string> missingMods, ProfileManager profileManager)
		{
			ModManagerVM.AutomaticDownload(missingMods, profileManager);
		}

		/// <summary>
		/// Download the profile.
		/// </summary>
		public void CheckOnlineProfileIntegrity(IModProfile profile, Dictionary<string, string> missingMods, string gameModeID)
		{
			if (!ModRepository.IsOffline)
			{
				CheckingOnlineProfileIntegrity(this, new EventArgs<IBackgroundTask>(ProfileManager.CheckOnlineProfileIntegrity(profile, missingMods, gameModeID, ConfirmUpdaterAction)));
			}
			else
			{
				ModManager.Login();
				ProfileManager.AsyncCheckOnlineProfileIntegrity(profile, missingMods, gameModeID, ConfirmUpdaterAction);
			}
		}

		public void FixConfigFiles(List<string> files, IModProfile profile)
		{
			ConfigFilesFixing(this, new EventArgs<IBackgroundTask>(VirtualModActivator.FixConfigFiles(files, profile, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Updates the program.
		/// </summary>
		/// <param name="isAutoCheck">Whether the check is automatic or user requested.</param>
		private void UpdateProgram(bool isAutoCheck)
		{
			Updating(this, new EventArgs<IBackgroundTask>(UpdateManager.Update(ConfirmUpdaterAction, isAutoCheck)));
		}

		/// <summary>
		/// Switches the active profile.
		/// </summary>
		public void ProfileSwitch(IModProfile profile, IList<IVirtualModLink> newLinks, IList<IVirtualModLink> removeLinks, bool startupMigration, bool restoring)
		{
			ProfileSwitching(this, new EventArgs<IBackgroundTask>(ProfileManager.SwitchProfile(profile, ModManager, newLinks, removeLinks, startupMigration, restoring, ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Performs the startup mod migration.
		/// </summary>
		public void MigrateMods(IModManagerView modManagerControl, bool migrate)
		{
			MigratingMods(this, new EventArgs<IBackgroundTask>(ModMigration(modManagerControl, migrate)));
		}

		public void RequestGameMode(string gameModeId)
		{
			RequestedGameMode = gameModeId;
		}

		#region Backup Management

		/// <summary>
		/// Opens the create backup form.
		/// </summary>
		public void CreateBackup(MainForm mainForm)
		{
			BackupManager.Initialize();
			var bnfBackupForm = new BackupManagerForm(BackupManager);
			bnfBackupForm.ShowDialog();

			if (bnfBackupForm.DialogResult == DialogResult.OK)
			{
				bnfBackupForm.Close();
				CreateBackup(mainForm, BackupManager);
			}
			else
			{
				bnfBackupForm.Close();
			}
		}

		/// <summary>
		/// Performs the destination folder selection.
		/// </summary>
		public void CreateBackup(MainForm mainForm, BackupManager p_bmBackupManager)
		{
			using (var fbd = new XtraFolderBrowserDialog())
			{
				fbd.Description = $"Select the folder where {CommonData.ModManagerName} will save the Backup Archive.";
				fbd.ShowNewFolderButton = true;

				fbd.ShowDialog(mainForm);

				if (!string.IsNullOrWhiteSpace(fbd.SelectedPath))
				{
					var directoryInfo = new DirectoryInfo(fbd.SelectedPath);

					foreach (Environment.SpecialFolder folder in Enum.GetValues(typeof(Environment.SpecialFolder)))
					{
						if (string.Equals(directoryInfo.FullName, Environment.GetFolderPath(folder), StringComparison.OrdinalIgnoreCase))
						{
							var drResult = ExtendedMessageBox.Show(null, "You cannot select a system folder!", "Wrong folder.", "", MessageBoxButtons.OK, MessageBoxIcon.Warning);
							CreateBackup(mainForm, p_bmBackupManager);
							break;
						}
					}

					CreatingBackup(this, new EventArgs<IBackgroundTask>(CreateBackupTask(fbd.SelectedPath, p_bmBackupManager)));
				}
			}
		}

		/// <summary>
		/// Sets up the create backup task.
		/// </summary>
		private IBackgroundTask CreateBackupTask(string p_strSelectedPath, BackupManager p_bmBackupManager)
		{
			var bmtBackupManagerTask = new CreateBackupTask(ModManager.VirtualModActivator, ModManager, EnvironmentInfo, PluginManagerVM, PluginManager, ProfileManager, p_strSelectedPath, p_bmBackupManager, ConfirmUpdaterAction);
			bmtBackupManagerTask.Update(ConfirmUpdaterAction);
			return bmtBackupManagerTask;
		}

		/// <summary>
		/// Performs the restore backup form.
		/// </summary>
		public void RestoreBackup(IModManagerView p_mmgModManager)
		{
			var rbfRestoreForm = new RestoreBackupForm(ModManager, ProfileManager);
			rbfRestoreForm.ShowDialog();

			if (rbfRestoreForm.DialogResult == DialogResult.Cancel)
			{
				rbfRestoreForm.Close();
			}
			else if (!string.IsNullOrEmpty(rbfRestoreForm.Text))
			{
				rbfRestoreForm.Close();

				if (rbfRestoreForm.DialogResult == DialogResult.Yes)
				{
					if (ProfileManager.CurrentProfile != null)
					{
						ProfileManager.RemoveProfile(ProfileManager.CurrentProfile);
					}
					else
					{
						ProfileManager.SetCurrentProfile(null);
					}
				}

				ProfileManager.SetCurrentProfile(null);
				p_mmgModManager.DeactivateAllMods(true, true);
				RestoreBackup(rbfRestoreForm, rbfRestoreForm.DialogResult == DialogResult.Yes);
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
			var bmtBackupManagerTask = new RestoreBackupTask(ModManager.VirtualModActivator, ModManager, ProfileManager, EnvironmentInfo, p_strFileName, p_booPurgeFolders, ConfirmUpdaterAction);
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
			var plfPurgeLooseFilesTask = new PurgeLooseFilesTask(p_BackupManager, ConfirmUpdaterAction);
			plfPurgeLooseFilesTask.Update(ConfirmUpdaterAction);
			return plfPurgeLooseFilesTask;
		}

		#endregion

		/// <summary>
		/// Sets up the mod migration task.
		/// </summary>
		private IBackgroundTask ModMigration(IModManagerView modManagerControl, bool migrate)
		{
			var mmtModMigrationTask = new ModMigrationTask(this, modManagerControl, migrate, ConfirmUpdaterAction);

			if (VirtualModActivator.GameMode.LoadOrderManager != null)
			{
				VirtualModActivator.GameMode.LoadOrderManager.MonitorExternalTask(mmtModMigrationTask);
			}
			else
			{
				mmtModMigrationTask.Update(ConfirmUpdaterAction);
			}

			return mmtModMigrationTask;
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
		/// Gets the load order from the profile.
		/// </summary>
		public Dictionary<Plugin, string> ImportProfileLoadOrder()
		{
			IModProfile impProfile = profileSwitchToken == null ? ProfileManager.CurrentProfile : profileSwitchToken.Profile;
			Dictionary<string, string> dicProfile = profileSwitchToken == null ? null : profileSwitchToken.ProfileDictionary;
			return ParseProfileLoadOrder(impProfile, dicProfile);
		}

		/// <summary>
		/// Parses a profile's stored load order without changing the current plugin state.
		/// </summary>
		/// <param name="p_impProfile">The profile whose load order should be parsed.</param>
		/// <param name="p_dicProfile">The already loaded profile files, when available.</param>
		/// <returns>The registered plugins and requested active states, or <c>null</c> when the profile has no load order.</returns>
		private Dictionary<Plugin, string> ParseProfileLoadOrder(IModProfile p_impProfile, Dictionary<string, string> p_dicProfile)
		{
			if (p_impProfile == null)
				return null;

			if (p_impProfile.LoadOrder != null && p_impProfile.LoadOrder.Count > 0)
				return PluginManagerVM.ParseLoadOrderFromDictionary(p_impProfile.LoadOrder);

			Dictionary<string, string> dicProfile = p_dicProfile;
			if (dicProfile == null)
				ProfileManager.LoadProfile(p_impProfile, out dicProfile);

			return dicProfile != null && dicProfile.ContainsKey("loadorder")
				? PluginManagerVM.ParseLoadOrderFromString(dicProfile["loadorder"])
				: null;
		}

		/// <summary>
		/// Applies the pending profile's complete plugin order and active state.
		/// </summary>
		/// <returns>The background task applying the profile state, or <c>null</c> when the profile contains no load order.</returns>
		public IBackgroundTask ApplyPendingProfileLoadOrder()
		{
			Dictionary<Plugin, string> dicLoadOrder = ImportProfileLoadOrder();
			return dicLoadOrder == null || dicLoadOrder.Count == 0
				? null
				: PluginManager.ApplyLoadOrder(dicLoadOrder, false, true);
		}

		/// <summary>
		/// Restores the complete plugin order and active state saved before the pending profile switch.
		/// </summary>
		/// <returns>The background task restoring the previous profile state, or <c>null</c> when no previous load order is available.</returns>
		public IBackgroundTask ApplyPreviousProfileLoadOrder()
		{
			if (profileSwitchToken == null)
				return null;

			Dictionary<Plugin, string> dicLoadOrder = !String.IsNullOrWhiteSpace(profileSwitchToken.PreviousLoadOrder)
				? PluginManagerVM.ParseLoadOrderFromString(profileSwitchToken.PreviousLoadOrder)
				: ParseProfileLoadOrder(profileSwitchToken.PreviousProfile, profileSwitchToken.PreviousProfileDictionary);
			return dicLoadOrder == null || dicLoadOrder.Count == 0
				? null
				: PluginManager.ApplyLoadOrder(dicLoadOrder, false, true);
		}

		/// <summary>
		/// Waits for queued load-order persistence work to complete.
		/// </summary>
		/// <param name="p_strErrorMessage">The write failure or timeout message.</param>
		/// <returns><c>true</c> when all writes completed successfully; otherwise, <c>false</c>.</returns>
		public bool WaitForPendingLoadOrderWrites(out string p_strErrorMessage)
		{
			if (GameMode.LoadOrderManager == null)
			{
				p_strErrorMessage = null;
				return true;
			}

			return GameMode.LoadOrderManager.WaitForPendingWrites(30000, out p_strErrorMessage);
		}

		/// <summary>
		/// Commits the pending profile as the current profile after every switch phase succeeds.
		/// </summary>
		public void CommitProfileSwitch()
		{
			if (profileSwitchToken == null)
				return;

			ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
			profileSwitchToken = null;
		}

		/// <summary>
		/// Starts a best-effort restoration of the virtual links and settings that existed before the switch.
		/// </summary>
		/// <returns>The rollback task, or <c>null</c> when no virtual-link changes need to be restored.</returns>
		public IBackgroundTask RollbackProfileSwitch()
		{
			if (profileSwitchToken == null)
				return null;

			ApplyProfileConfiguration(profileSwitchToken.PreviousProfileDictionary);

			List<IVirtualModLink> lstCurrentLinks = new List<IVirtualModLink>(VirtualModActivator.VirtualLinks);
			List<IVirtualModLink> lstLinksToRestore = profileSwitchToken.PreviousVirtualLinks.Except(lstCurrentLinks, new VirtualModLinkEqualityComparer()).ToList();
			List<IVirtualModLink> lstLinksToRemove = lstCurrentLinks.Except(profileSwitchToken.PreviousVirtualLinks, new VirtualModLinkEqualityComparer()).ToList();

			if (lstLinksToRestore.Count == 0 && lstLinksToRemove.Count == 0)
				return null;

			return ProfileManager.SwitchProfile(profileSwitchToken.PreviousProfile, ModManager, lstLinksToRestore, lstLinksToRemove, false, false, ConfirmUpdaterAction);
		}

		/// <summary>
		/// Restores the previous profile selection and clears the pending switch state after rollback.
		/// </summary>
		public void CompleteProfileRollback()
		{
			if (profileSwitchToken != null)
				ProfileManager.SetCurrentProfile(profileSwitchToken.PreviousProfile);

			ModManager.VirtualModActivator.RestoreIniEdits();
			profileSwitchToken = null;
			m_booIsSwitching = false;
		}

		/// <summary>
		/// Applies the INI edits and optional files serialized for a profile.
		/// </summary>
		/// <param name="p_dicProfile">The serialized profile files.</param>
		private void ApplyProfileConfiguration(Dictionary<string, string> p_dicProfile)
		{
			ModManager.VirtualModActivator.PurgeIniEdits();

			if (p_dicProfile != null && p_dicProfile.ContainsKey("iniEdits"))
				ModManager.VirtualModActivator.ImportIniEdits(p_dicProfile["iniEdits"]);

			if (!GameMode.RequiresOptionalFilesCheckOnProfileSwitch || p_dicProfile == null || !p_dicProfile.ContainsKey("optional"))
				return;

			string[] strFiles = p_dicProfile["optional"].Split("#".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
			if (strFiles.Length > 0)
				GameMode.SetOptionalFilesList(strFiles);

			if (PluginManager == null)
				return;

			foreach (string strFile in strFiles)
			{
				if (PluginManager.IsActivatiblePluginFile(strFile))
					PluginManager.AddPlugin(strFile);
			}
		}

		/// <summary>
		/// Notifies the view model that the view has been displayed.
		/// </summary>
		public void ViewIsShown()
		{
			if (EnvironmentInfo.Settings.CheckForUpdatesOnStartup)
			{
				if (string.IsNullOrEmpty(EnvironmentInfo.Settings.LastUpdateCheckDate))
				{
					UpdateProgram(true);
					EnvironmentInfo.Settings.LastUpdateCheckDate = DateTime.Today.ToShortDateString();
					EnvironmentInfo.Settings.Save();
				}
				else
				{
					try
					{
						if ((DateTime.Today - Convert.ToDateTime(EnvironmentInfo.Settings.LastUpdateCheckDate)).TotalDays >= EnvironmentInfo.Settings.UpdateCheckInterval)
						{
							UpdateProgram(true);
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

		/// <summary>
		/// Profile switch initialization.
		/// </summary>
		/// <param name="p_frmParent">The form who should be the owner of an eventual message box (sadly this gimmick is required to prevent the message to appear beneath the main form).</param>
		/// <param name="p_impProfile">The profile we want to switch to.</param>
		/// <param name="p_booSilentInstall">Whether this switch is part of an automated procedure and thus should not provide any feedback to the user.</param>
		/// <param name="p_booRestoring">Whether we're restoring a backup profile.</param>
		public void SwitchProfile(Form p_frmParent, IModProfile p_impProfile, bool p_booSilentInstall, bool p_booRestoring)
		{
			// It's an automatic procedure? Otherwise the switch is allowed only when there's no active profile or
			// the profile we're switching to is not the same as the active one.
			if (p_booSilentInstall || ProfileManager.CurrentProfile == null || p_impProfile.Id != ProfileManager.CurrentProfile.Id)
			{
				List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
				List<string> lstScriptedMismatch = new List<string>();
				List<IVirtualModInfo> lstMissingModInfo = new List<IVirtualModInfo>();

				// Checks whether there's any inconsistecies in scripted installers between the active and the profile we're switching to.
				if (!p_booSilentInstall || ProfileManager.CurrentProfile != null)
				{
					lstScriptedMismatch = ProfileManager.CheckScriptedInstallersIntegrity(ProfileManager.CurrentProfile, p_impProfile);
				}

				// Loads all the info necessary for the profile switch to the new profile inside a dictionary.
				ProfileManager.LoadProfile(p_impProfile, out Dictionary<string, string> profiles);

				if (profiles != null && profiles.Count > 0 && profiles.ContainsKey("modlist"))
				{
					lstVirtualLinks = ModManager.VirtualModActivator.LoadImportedList(profiles["modlist"], ProfileManager.GetProfilePath(p_impProfile));
					ModManager.VirtualModActivator.CheckLinkListIntegrity(lstVirtualLinks, out lstMissingModInfo, lstScriptedMismatch);
				}

				IModProfile impPreviousProfile = ProfileManager.CurrentProfile;
				Dictionary<string, string> dicPreviousProfile = null;
				if (impPreviousProfile != null)
					ProfileManager.LoadProfile(impPreviousProfile, out dicPreviousProfile);

				List<IVirtualModLink> lstPreviousVirtualLinks = new List<IVirtualModLink>(VirtualModActivator.VirtualLinks);
				string strPreviousLoadOrder = GameMode.UsesPlugins && PluginManagerVM != null
					? System.Text.Encoding.UTF8.GetString(PluginManagerVM.ExportLoadOrder())
					: null;
				profileSwitchToken = new ProfileSwitchToken(p_booSilentInstall, p_booRestoring, p_impProfile, impPreviousProfile, lstVirtualLinks, lstPreviousVirtualLinks, lstScriptedMismatch, lstMissingModInfo, profiles, dicPreviousProfile, strPreviousLoadOrder);

				// Deprecated, online profiles are no longer supported by NexusMods.
				/*
				if (!p_booSilentInstall && lstMissingModInfo.Count > 0 && p_impProfile.IsOnline && !string.IsNullOrEmpty(p_impProfile.OnlineID))
				{
					Dictionary<string, string> missingMods = new Dictionary<string, string>();

					foreach (var vmi in lstMissingModInfo)
					{
						List<IMod> lstManagedMods = new List<IMod>(ModManager.ManagedMods.ToList());
						IMod modMod = lstManagedMods.Find(x => GetIsSameMod(x, vmi));

                        if (modMod == null)
                        {
                            missingMods.Add(vmi.ModFileName, vmi.DownloadId);
                        }
                    }

					if (missingMods.Count > 0)
					{
						bool booLoaded = ProfileManager.LoadProfileFileList(p_impProfile);

						if (booLoaded)
                        {
                            CheckOnlineProfileIntegrity(p_impProfile, missingMods, ModManager.GameMode.ModeId);
                        }

                        return;
					}
				}*/

				ExecuteProfileSwitch(p_frmParent);
			}
		}

		/// <summary>
		/// Executes the profile switch.
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="restoring"></param>
		public void SetupProfileSwitch(Form parent)
		{
			m_booIsSwitching = true;
			IModProfile impCurrentProfile = ProfileManager.CurrentProfile;

			// Prompts to automatically install any missing mod file
			if (profileSwitchToken.ScriptedMismatchList != null && profileSwitchToken.ScriptedMismatchList.Count > 0 || profileSwitchToken.MissingMods != null && profileSwitchToken.MissingMods.Count > 0)
			{
				System.Text.StringBuilder sbMessage = new System.Text.StringBuilder();

				sbMessage.AppendLine("The selected profile contains files from mods not currently installed");
				sbMessage.AppendLine("The manager will try to automatically reinstall the required files.");
				sbMessage.AppendLine("- Click YES to proceed.");
				sbMessage.AppendLine("- Click NO if you want to skip this step.");
				sbMessage.AppendLine("- Click CANCEL if you want to abort the profile switch.");
				sbMessage.AppendLine();
				sbMessage.AppendLine("Depending on the mod, leaving it uninstalled could cause in game crashes.");
				System.Text.StringBuilder sbDetails = new System.Text.StringBuilder();

				List<IMod> lstFoundMods = new List<IMod>();
				List<IMod> lstScriptedMods = new List<IMod>();
				ReadOnlyObservableList<IMod> oclMods = new ReadOnlyObservableList<IMod>(lstScriptedMods);
				List<IMod> lstManagedMods = new List<IMod>(ModManager.ManagedMods.ToList());
				foreach (IVirtualModInfo vmi in profileSwitchToken.MissingMods)
				{
					IMod modMod = lstManagedMods.Find(x => GetIsSameMod(x, vmi));
					if (modMod != null)
					{
						lstFoundMods.Add(modMod);
					}

					sbDetails.AppendFormat("- Mod: {0} - filename: {1} - present: {2}", vmi.ModName, vmi.ModFileName, modMod != null ? "Yes" : "No").AppendLine();
				}

				if (profileSwitchToken.ScriptedMismatchList != null && profileSwitchToken.ScriptedMismatchList.Count > 0)
				{
					lstScriptedMods = lstManagedMods.Where(x => profileSwitchToken.ScriptedMismatchList.Contains(Path.GetFileName(x.Filename), StringComparer.CurrentCultureIgnoreCase)).ToList();
				}

				ModManager.VirtualModActivator.DisableLinkCreation = true;

				if (lstScriptedMods.Count > 0)
				{
					oclMods = new ReadOnlyObservableList<IMod>(lstScriptedMods);
					//ModManagerVM.DeactivateMultipleMods(new ReadOnlyObservableList<IMod>(oclMods), true, true, true);
					//ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
					//ModManagerVM.MultiModInstall(lstScriptedMods, false);
					//ProfileManager.SetCurrentProfile(impCurrentProfile);
				}

				if (lstFoundMods.Count > 0)
				{
					string strDetails = sbDetails.Length > 0 ? sbDetails.ToString() : null;

					if (profileSwitchToken.IsSilent)
					{
						//ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
						//ModManagerVM.MultiModInstall(lstFoundMods, false);
						//ProfileManager.SetCurrentProfile(impCurrentProfile);
					}
					else
					{
						DialogResult drResult = ExtendedMessageBox.Show(parent, sbMessage.ToString(), CommonData.ModManagerName, strDetails, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

						if (drResult == DialogResult.Yes)
						{
							if (lstFoundMods.Count > 0)
							{
								//ProfileManager.SetCurrentProfile(profileSwitchToken.Profile);
								//ModManagerVM.MultiModInstall(lstFoundMods, false);
								//ProfileManager.SetCurrentProfile(impCurrentProfile);
							}
						}
						else if (drResult == DialogResult.Cancel)
						{
							ModManager.VirtualModActivator.DisableLinkCreation = false;
							m_booIsSwitching = false;
							ProfileManager.SetCurrentProfile(impCurrentProfile);
							profileSwitchToken = null;
							AbortedProfileSwitch(this, new EventArgs());
							return;
						}
						else if (drResult == DialogResult.No)
							lstFoundMods.Clear();
					}
				}

				lstFoundMods.AddRange(lstScriptedMods);

				ModManagerVM.ProfileSwitchSetup(oclMods, lstFoundMods, profileSwitchToken.Profile, impCurrentProfile);
			}
			else
				ExecuteProfileSwitch(parent);
		}

		/// <summary>
		/// Executes the profile switch.
		/// </summary>
		/// <param name="parent"></param>
		/// <param name="restoring"></param>
		public void ExecuteProfileSwitch(Form parent)
		{
			m_booIsSwitching = true;
			ApplyProfileConfiguration(profileSwitchToken.ProfileDictionary);

			var lstMissingLinks = profileSwitchToken.VirtualLinks.Except(VirtualModActivator.VirtualLinks, new VirtualModLinkEqualityComparer()).ToList();
			var lstUnneededLinks = VirtualModActivator.VirtualLinks.Except(profileSwitchToken.VirtualLinks, new VirtualModLinkEqualityComparer()).ToList();

			ModManager.VirtualModActivator.DisableLinkCreation = false;
			ProfileSwitch(profileSwitchToken.Profile, lstMissingLinks, lstUnneededLinks, profileSwitchToken.IsSilent, profileSwitchToken.IsRestoring);
		}

		private bool GetIsSameMod(IMod p_modMod, IVirtualModInfo p_vmiModInfo)
		{
			var strFilename = Path.GetFileName(p_modMod.Filename);

			if (strFilename.Equals(p_vmiModInfo.ModFileName, StringComparison.InvariantCultureIgnoreCase))
			{
				return true;
			}

			if (!string.IsNullOrEmpty(p_modMod.DownloadId) && !string.IsNullOrEmpty(p_vmiModInfo.DownloadId))
			{
				if (p_modMod.DownloadId.Equals(p_vmiModInfo.DownloadId))
				{
					return true;
				}
			}

			return !string.IsNullOrEmpty(p_modMod.DownloadId) && !string.IsNullOrEmpty(p_vmiModInfo.UpdatedDownloadId) && p_modMod.DownloadId.Equals(p_vmiModInfo.UpdatedDownloadId);
		}

		public bool? CheckAlreadyDownloading(string p_strUrl, string p_strKey)
		{
			foreach (var adtTask in DownloadMonitorVM.Tasks)
			{
				if (!string.IsNullOrEmpty(adtTask.DescriptorSourcePath))
				{
					if (Path.GetFileName(adtTask.DescriptorSourcePath).Equals(p_strKey, StringComparison.OrdinalIgnoreCase))
					{
						return adtTask.Status == TaskStatus.Incomplete || adtTask.Status == TaskStatus.Paused
							? (bool?)null
							: true;
					}

					if (adtTask.SourceUri.Equals(p_strUrl) && adtTask.Status == TaskStatus.Paused)
					{
						return null;
					}
				}
				else if (adtTask.SourceUri.Equals(p_strUrl) && adtTask.Status == TaskStatus.Paused)
				{
					return null;
				}
			}

			return false;
		}

		public void ResumeIncompleteDownloads(List<string> p_lstIncompleteDownloads)
		{
			var lstAddModTask = new List<AddModTask>();

			foreach (var adtTask in DownloadMonitorVM.Tasks)
			{
				if (adtTask.ReturnValue != null && p_lstIncompleteDownloads.Contains(adtTask.ReturnValue.ToString(), StringComparer.OrdinalIgnoreCase) && (adtTask.Status == TaskStatus.Incomplete || adtTask.Status == TaskStatus.Paused))
				{
					lstAddModTask.Add(adtTask);
				}
			}

			if (lstAddModTask.Count > 0)
			{
				DownloadMonitorVM.ResumeAllTasks(lstAddModTask);
			}
		}

		private class VirtualModLinkEqualityComparer : IEqualityComparer<IVirtualModLink>
		{
			public bool Equals(IVirtualModLink x, IVirtualModLink y)
			{
				if (ReferenceEquals(x, y))
				{
					return true;
				}

				if (x == null || y == null)
				{
					return false;
				}

				return x.RealModPath.Equals(y.RealModPath, StringComparison.InvariantCultureIgnoreCase) && x.VirtualModPath.Equals(y.VirtualModPath, StringComparison.InvariantCultureIgnoreCase);
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
				var booClose = ConfirmCloseAfterGameLaunch(out var booRemember);
				EnvironmentInfo.Settings.CloseModManagerAfterGameLaunchIsRemembered = booRemember;
				EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch = booClose;
				EnvironmentInfo.Settings.Save();
			}
		}

		/// <summary>
		/// Logs in/out of all mod repositories.
		/// </summary>
		private void ToggleLogin()
		{
			lock (ModRepository)
			{
				if (ModRepository.IsOffline)
				{
					ModManager.Login();
				}
				else
				{
					ModRepository.Logout();
					ModManager.Logout();

					EnvironmentInfo.Settings.ApiKey = string.Empty;
					EnvironmentInfo.Settings.Save();
				}
			}
		}
	}
}
