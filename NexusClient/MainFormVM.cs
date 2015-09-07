using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Xml.Linq;
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
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.Settings;
using Nexus.Client.Settings.UI;
using Nexus.Client.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
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
		private const string CHANGE_DEFAULT_GAME_MODE = "__changedefaultgamemode";
		private const string RESCAN_INSTALLED_GAMES = "__rescaninstalledgames";

		#region Events

		/// <summary>
		/// Raised when the programme is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> Updating = delegate { };

		/// <summary>
		/// Raised when switching profiles.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileSwitching = delegate { };

		#endregion

		#region Delegates

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmActionMethod ConfirmUpdaterAction = delegate { return true; };

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmRememberedActionMethod ConfirmCloseAfterGameLaunch = delegate(out bool x) { x = false; return true; };

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
		/// and operations for displaying the Profile manager.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for displaying the Profile manager.</value>
		public ProfileManagerVM ProfileManagerVM { get; private set; }

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
				return String.Format("{0} ({1}) - {2}", EnvironmentInfo.Settings.ModManagerName, EnvironmentInfo.ApplicationVersion + "a", GameMode.Name);
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
		public MainFormVM(IEnvironmentInfo p_eifEnvironmentInfo, GameModeRegistry p_gmrInstalledGames, IGameMode p_gmdGameMode, IModRepository p_mrpModRepository, DownloadMonitor p_dmtMonitor, ModActivationMonitor p_mamMonitor, UpdateManager p_umgUpdateManager, ModManager p_mmgModManager, IPluginManager p_pmgPluginManager)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameMode = p_gmdGameMode;
			GameMode.GameLauncher.GameLaunching += new CancelEventHandler(GameLauncher_GameLaunching);
			ModManager = p_mmgModManager;
			PluginManager = p_pmgPluginManager;
			ProfileManager = new ProfileManager(ModManager.VirtualModActivator, ModManager, p_eifEnvironmentInfo.Settings.ModFolder[GameMode.ModeId], GameMode.UsesPlugins);
			ModRepository = p_mrpModRepository;
			UpdateManager = p_umgUpdateManager;
			ModManagerVM = new ModManagerVM(p_mmgModManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode.ModeTheme);
			DownloadMonitorVM = new DownloadMonitorVM(p_dmtMonitor, p_eifEnvironmentInfo.Settings, p_mmgModManager, p_mrpModRepository);
			ModActivationMonitor = p_mamMonitor;
			ModActivationMonitorVM = new ModActivationMonitorVM(p_mamMonitor, p_eifEnvironmentInfo.Settings, p_mmgModManager);
			if (GameMode.UsesPlugins)
				PluginManagerVM = new PluginManagerVM(p_pmgPluginManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode, p_mamMonitor);
			ProfileManagerVM = new ProfileManagerVM(ProfileManager, ModManager.ManagedMods, ModRepository, p_eifEnvironmentInfo.Settings, p_gmdGameMode.ModeTheme);
			HelpInfo = new HelpInformation(p_eifEnvironmentInfo);

			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo);
			foreach (IModFormat mftFormat in p_mmgModManager.ModFormats)
				gsgGeneralSettings.AddFileAssociation(mftFormat.Extension, mftFormat.Name);

			ModOptionsSettingsGroup mosModOptions = new ModOptionsSettingsGroup(p_eifEnvironmentInfo);

			List<ISettingsGroupView> lstSettingGroups = new List<ISettingsGroupView>();
			lstSettingGroups.Add(new GeneralSettingsPage(gsgGeneralSettings));
			lstSettingGroups.Add(new ModOptionsPage(mosModOptions));
			DownloadSettingsGroup dsgDownloadSettings = new DownloadSettingsGroup(p_eifEnvironmentInfo, ModRepository);
			lstSettingGroups.Add(new DownloadSettingsPage(dsgDownloadSettings));

			if (p_gmdGameMode.SupportedToolsGroupViews != null)
				lstSettingGroups.AddRange(p_gmdGameMode.SupportedToolsGroupViews);

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
		/// Updates the programme.
		/// </summary>
		/// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
		private void UpdateProgramme(bool p_booIsAutoCheck)
		{
			Updating(this, new EventArgs<IBackgroundTask>(UpdateManager.Update(ConfirmUpdaterAction, p_booIsAutoCheck)));
		}

 		/// <summary>
		/// Updates the programme.
		/// </summary>
		/// <param name="p_booIsAutoCheck">Whether the check is automatic or user requested.</param>
		public void ProfileSwitch(IModProfile p_impProfile, IList<IVirtualModLink> p_lstNewLinks, IList<IVirtualModLink> p_lstRemoveLinks)
		{
			ProfileSwitching(this, new EventArgs<IBackgroundTask>(ProfileManager.SwitchProfile(p_impProfile, ModManager, p_lstNewLinks, p_lstRemoveLinks, ConfirmUpdaterAction)));
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
