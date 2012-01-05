using System;
using System.Collections.Generic;
using Nexus.Client.ActivityMonitoring;
using Nexus.Client.ActivityMonitoring.UI;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Commands;
using Nexus.Client.Games;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.UI;
using Nexus.Client.Settings;
using Nexus.Client.Settings.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.Client.UI;
using System.ComponentModel;

namespace Nexus.Client
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display the main form.
	/// </summary>
	public class MainFormVM
	{
		#region Events

		/// <summary>
		/// Raised when the programme is being updated.
		/// </summary>
		public event EventHandler<EventArgs<IBackgroundTask>> Updating = delegate { };

		#endregion

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmActionMethod ConfirmUpdaterAction = delegate { return true; };

		/// <summary>
		/// Called when an updater's action needs to be confirmed.
		/// </summary>
		public ConfirmRememberedActionMethod ConfirmCloseAfterGameLaunch = delegate(out bool x) { x = false; return true; };

		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to update the programme.
		/// </summary>
		/// <value>The command to update the programme.</value>
		public Command UpdateCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the update manager to use to perform updates.
		/// </summary>
		/// <value>The update manager to use to perform updates.</value>
		protected UpdateManager UpdateManager { get; private set; }

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
		/// and operations for diaplying the activity monitor.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the activity monitor.</value>
		public ActivityMonitorVM ActivityMonitorVM { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data
		/// and operations for diaplying the settings view.
		/// </summary>
		/// <value>The view model that encapsulates the data
		/// and operations for diaplying the settings view.</value>
		public SettingsFormVM SettingsFormVM { get; private set; }

		/// <summary>
		/// Gets whether a game mode change hsa been requested.
		/// </summary>
		/// <value>Whether a game mode change hsa been requested.</value>
		public bool GameModeChangeRequested { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

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

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_amtMonitor">The activity monitor to use to track task progress.</param>
		/// <param name="p_umgUpdateManager">The update manager to use to perform updates.</param>
		/// <param name="p_mmgModManager">The <see cref="ModManager"/> to use to manage mods.</param>
		/// <param name="p_pmgPluginManager">The <see cref="PluginManager"/> to use to manage plugins.</param>
		public MainFormVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameMode p_gmdGameMode, ActivityMonitor p_amtMonitor, UpdateManager p_umgUpdateManager, ModManager p_mmgModManager, IPluginManager p_pmgPluginManager)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			
			GameMode = p_gmdGameMode;
			GameMode.GameLauncher.GameLaunching += new CancelEventHandler(GameLauncher_GameLaunching);

			UpdateManager = p_umgUpdateManager;
			ModManagerVM = new ModManagerVM(p_mmgModManager, p_eifEnvironmentInfo.Settings, p_gmdGameMode.ModeTheme);
			PluginManagerVM = new PluginManagerVM(p_pmgPluginManager, p_eifEnvironmentInfo.Settings);
			ActivityMonitorVM = new ActivityMonitorVM(p_amtMonitor, p_eifEnvironmentInfo.Settings);

			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo);
			foreach (IModFormat mftFormat in p_mmgModManager.ModFormats)
				gsgGeneralSettings.AddFileAssociation(mftFormat.Extension, mftFormat.Name);

			ModOptionsSettingsGroup mosModOptions = new ModOptionsSettingsGroup(p_eifEnvironmentInfo);

			List<ISettingsGroupView> lstSettingGroups = new List<ISettingsGroupView>();
			lstSettingGroups.Add(new GeneralSettingsPage(gsgGeneralSettings));
			lstSettingGroups.Add(new ModOptionsPage(mosModOptions));
			lstSettingGroups.AddRange(p_gmdGameMode.SettingsGroupViews);

			SettingsFormVM = new SettingsFormVM(p_gmdGameMode, p_eifEnvironmentInfo, lstSettingGroups);

			UpdateCommand = new Command("Update", String.Format("Update {0}", EnvironmentInfo.Settings.ModManagerName), UpdateProgramme);
		}

		#endregion

		/// <summary>
		/// Requests a game mode change.
		/// </summary>
		public void ChangeGameMode()
		{
			GameModeChangeRequested = true;
		}

		/// <summary>
		/// Updates the programme.
		/// </summary>
		protected void UpdateProgramme()
		{
			Updating(this, new EventArgs<IBackgroundTask>(UpdateManager.Update(ConfirmUpdaterAction)));
		}

		/// <summary>
		/// Notifies the view model that the view has been displayed.
		/// </summary>
		public void ViewIsShown()
		{
			if (EnvironmentInfo.Settings.CheckForUpdatesOnStartup)
				UpdateProgramme();
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
	}
}
