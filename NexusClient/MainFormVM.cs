using System;
using System.Collections.Generic;
using System.ComponentModel;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadMonitoring;
using Nexus.Client.DownloadMonitoring.UI;
using Nexus.Client.Commands;
using Nexus.Client.Games;
using Nexus.Client.Games.Tools;
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
using System.Drawing;

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
		/// Gets the repository we are logging in to.
		/// </summary>
		/// <value>The repository we are logging in to.</value>
		protected IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying the mod manager.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the mod manager.</value>
		public ModManagerVM ModManagerVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying the plugin manager.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the plugin manager.</value>
		public PluginManagerVM PluginManagerVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying the download monitor.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the download monitor.</value>
		public DownloadMonitorVM DownloadMonitorVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying the settings view.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the settings view.</value>
		public SettingsFormVM SettingsFormVM { get; private set; }

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
		protected IGameMode GameMode { get; private set; }

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
		public MainFormVM(IEnvironmentInfo p_eifEnvironmentInfo, GameModeRegistry p_gmrInstalledGames, IGameMode p_gmdGameMode, IModRepository p_mrpModRepository, DownloadMonitor p_dmtMonitor, UpdateManager p_umgUpdateManager, ModManager p_mmgModManager, IPluginManager p_pmgPluginManager)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;

			GameMode = p_gmdGameMode;
			GameMode.GameLauncher.GameLaunching += new CancelEventHandler(GameLauncher_GameLaunching);

			ModRepository = p_mrpModRepository;
			UpdateManager = p_umgUpdateManager;
			ModManagerVM = new ModManagerVM(p_mmgModManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode.ModeTheme);
			if (GameMode.UsesPlugins)
				PluginManagerVM = new PluginManagerVM(p_pmgPluginManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode);
			DownloadMonitorVM = new DownloadMonitorVM(p_dmtMonitor, p_eifEnvironmentInfo.Settings, OfflineMode);
			HelpInfo = new HelpInformation(p_eifEnvironmentInfo);

			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo);
			foreach (IModFormat mftFormat in p_mmgModManager.ModFormats)
				gsgGeneralSettings.AddFileAssociation(mftFormat.Extension, mftFormat.Name);

			ModOptionsSettingsGroup mosModOptions = new ModOptionsSettingsGroup(p_eifEnvironmentInfo);

			List<ISettingsGroupView> lstSettingGroups = new List<ISettingsGroupView>();
			lstSettingGroups.Add(new GeneralSettingsPage(gsgGeneralSettings));
			lstSettingGroups.Add(new ModOptionsPage(mosModOptions));
			if (!ModRepository.IsOffline)
			{
				DownloadSettingsGroup dsgDownloadSettings = new DownloadSettingsGroup(p_eifEnvironmentInfo, ModRepository.FileServerZones, ModRepository.AllowedConnections, (ModRepository.UserStatus == null) || String.IsNullOrEmpty(ModRepository.UserStatus[1]) ? 3 : Convert.ToInt32(ModRepository.UserStatus[1]));
				lstSettingGroups.Add(new DownloadSettingsPage(dsgDownloadSettings));
			}
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
			if (!OfflineMode)
				Updating(this, new EventArgs<IBackgroundTask>(UpdateManager.Update(ConfirmUpdaterAction)));
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
					UpdateProgramme();
					EnvironmentInfo.Settings.LastUpdateCheckDate = DateTime.Today.ToShortDateString();
					EnvironmentInfo.Settings.Save();
				}

				try
				{
					if ((DateTime.Today - Convert.ToDateTime(EnvironmentInfo.Settings.LastUpdateCheckDate)).TotalDays >= EnvironmentInfo.Settings.UpdateCheckInterval)
					{
						UpdateProgramme();
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
			ModRepository.Logout();
			EnvironmentInfo.Settings.RepositoryAuthenticationTokens.Remove(ModRepository.Id);
			EnvironmentInfo.Settings.Save();
		}
	}
}
