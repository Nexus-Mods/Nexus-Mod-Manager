using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.DownloadMonitoring;
using Nexus.Client.Games;
using Nexus.Client.ModActivationMonitoring;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// The class the encapsulates managing mods.
	/// </summary>
	/// <remarks>
	/// The list of managed mods needs to be centralized to ensure integrity; having multiple mod managers, each
	/// with a potentially different list of managed mods, would be disastrous. As such, this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the manager is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize"/> method returns the only instance that should be used.
	/// </remarks>
	public partial class ModManager
	{
		#region Singleton

		private static ModManager m_mmgCurrent = null;

		/// <summary>
		/// Initializes the singleton intances of the mod manager.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_dmrMonitor">The download monitor to use to track task progress.</param>
		/// <param name="p_mamMonitor">The mod activation monitor to use to track task progress.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mrgModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log tracking mod activations for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		/// <returns>The initialized mod manager.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the mod manager has already
		/// been initialized.</exception>
		public static ModManager Initialize(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, DownloadMonitor p_dmrMonitor, ModActivationMonitor p_mamMonitor, IModFormatRegistry p_frgFormatRegistry, ModRegistry p_mrgModRegistry, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			if (m_mmgCurrent != null)
				throw new InvalidOperationException("The Mod Manager has already been initialized.");
			m_mmgCurrent = new ModManager(p_gmdGameMode, p_eifEnvironmentInfo, p_mrpModRepository, p_dmrMonitor, p_mamMonitor, p_frgFormatRegistry, p_mrgModRegistry, p_futFileUtility, p_scxUIContext, p_ilgInstallLog, p_pmgPluginManager);
			return m_mmgCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			ModAdditionQueue.Dispose();
			ModAdditionQueue = null;
			m_mmgCurrent = null;
		}

		#endregion

		private ModActivator m_macModActivator = null;
		private VirtualModActivator m_vmaVirtualModActivator = null;
		private ReadMeManager m_rmmReadMeManager = null;

		#region events

		public event EventHandler<EventArgs<IBackgroundTask>> UpdateCheckStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> UpdateCategoriesCheckStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> AutomaticDownloadStarted = delegate { };

		#endregion

		#region Properties

		/// <summary>
		/// The loginform Task.
		/// </summary>
		public LoginFormTask LoginTask;

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		public IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the mod repository from which to get mods and mod metadata.
		/// </summary>
		/// <value>The mod repository from which to get mods and mod metadata.</value>
		public IModRepository ModRepository { get; private set; }

		/// <summary>
		/// Gets the mod auto updater.
		/// </summary>
		/// <value>The mod auto updater.</value>
		public AutoUpdater AutoUpdater { get; private set; }

		/// <summary>
		/// Gets the mod auto updater.
		/// </summary>
		/// <value>The mod auto updater.</value>
		protected ModActivator Activator
		{
			get
			{
				return m_macModActivator ?? (m_macModActivator = new ModActivator(InstallationLog, InstallerFactory));
			}
		}

		/// <summary>
		/// Gets the download monitor to use to display status.
		/// </summary>
		/// <value>The download monitor to use to display status.</value>
		protected DownloadMonitor DownloadMonitor { get; private set; }

		/// <summary>
		/// Gets the mod activation monitor to use to display status.
		/// </summary>
		/// <value>The mod activation monitor to use to display status.</value>
		public ModActivationMonitor ModActivationMonitor { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.
		/// </summary>
		/// <value>The <see cref="ModInstallerFactory"/> to use to create
		/// <see cref="ModInstaller"/>s.</value>
		protected ModInstallerFactory InstallerFactory { get; private set; }

		/// <summary>
		/// Gets the install log tracking mod activations for the current game mode.
		/// </summary>
		/// <value>The install log tracking mod activations for the current game mode.</value>
		public IInstallLog InstallationLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.
		/// </summary>
		/// <value>The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</value>
		protected IModFormatRegistry FormatRegistry { get; private set; }

		/// <summary>
		/// Gets the <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.
		/// </summary>
		/// <value>The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</value>
		protected ModRegistry ManagedModRegistry { get; private set; }

		/// <summary>
		/// Gets the <see cref="AddModQueue"/> that contains the list
		/// of <see cref="IMod"/>s to be added to the mod manager.
		/// </summary>
		/// <value>The <see cref="AddModQueue"/> that contains the list
		/// of <see cref="IMod"/>s to be added to the mod manager.</value>
		protected AddModQueue ModAdditionQueue { get; private set; }

		/// <summary>
		/// Gets the current Virtual Mod Activator.
		/// </summary>
		/// <value>The current Virtual Mod Activator.</value>
		public VirtualModActivator VirtualModActivator
		{
			get
			{
				return m_vmaVirtualModActivator;
			}
		}

		/// <summary>
		/// Gets the newest available information about the managed mods.
		/// </summary>
		/// <value>The newest available information about the managed mods.</value>
		public ReadOnlyObservableList<AutoUpdater.UpdateInfo> NewestModInfo
		{
			get
			{
				return AutoUpdater.NewestModInfo;
			}
		}

		/// <summary>
		/// Gets the list of supported mod formats.
		/// </summary>
		/// <value>The list of supported mod formats.</value>
		public ICollection<IModFormat> ModFormats
		{
			get
			{
				return FormatRegistry.Formats;
			}
		}

		/// <summary>
		/// Gets the newest available information about the managed mods.
		/// </summary>
		/// <value>The newest available information about the managed mods.</value>
		public ReadMeManager ReadMeManager
		{
			get
			{
				return m_rmmReadMeManager;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ManagedMods
		{
			get
			{
				return ManagedModRegistry.RegisteredMods;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<IMod> ActiveMods
		{
			get
			{
				return InstallationLog.ActiveMods;
			}
		}

		/// <summary>
		/// Gets whether the repository is in offline mode.
		/// </summary>
		/// <value>Whether the repository is in offline mode.</value>
		public bool RepositoryOfflineMode
		{
			get
			{
				return ModRepository.IsOffline;
			}
		}

		/// <summary>
		/// Gets the current game mode Mod directory.
		/// </summary>
		/// <value>The current game mode Mod directory.</value>
		public string CurrentGameModeModDirectory
		{
			get
			{
				return GameMode.GameModeEnvironmentInfo.ModDirectory;
			}
		}

		/// <summary>
		/// Gets the current game mode default categories.
		/// </summary>
		/// <value>The current game mode default categories.</value>
		public string CurrentGameModeDefaultCategories
		{
			get
			{
				return GameMode.GameDefaultCategories;
			}
		}

		/// <summary>
		/// Gets whether the required tool for the current game mode is missing.
		/// </summary>
		/// <value>Whether the required tool for the current game mode is missing.</value>
		public string RequiredToolErrorMessage
		{
			get
			{
				if (GameMode.OrderedRequiredToolFileNames != null)
					if (String.IsNullOrEmpty(GameMode.InstallationPath))
						return GameMode.RequiredToolErrorMessage;
				return String.Empty;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mrpModRepository">The mod repository from which to get mods and mod metadata.</param>
		/// <param name="p_dmrMonitor">The download monitor to use to track task progress.</param>
		/// <param name="p_mamMonitor">The mod activation monitor to use to track task progress.</param>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_mdrManagedModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgInstallLog">The install log tracking mod activations for the current game mode.</param>
		/// <param name="p_pmgPluginManager">The plugin manager to use to work with plugins.</param>
		private ModManager(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IModRepository p_mrpModRepository, DownloadMonitor p_dmrMonitor, ModActivationMonitor p_mamMonitor, IModFormatRegistry p_frgFormatRegistry, ModRegistry p_mdrManagedModRegistry, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgInstallLog, IPluginManager p_pmgPluginManager)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			m_rmmReadMeManager = new ReadMeManager(EnvironmentInfo.Settings.ModFolder[GameMode.ModeId]);
			ModRepository = p_mrpModRepository;
			FormatRegistry = p_frgFormatRegistry;
			ManagedModRegistry = p_mdrManagedModRegistry;
			InstallationLog = p_ilgInstallLog;
			m_vmaVirtualModActivator = new VirtualModActivator(this, p_pmgPluginManager, p_gmdGameMode, p_ilgInstallLog, p_eifEnvironmentInfo, EnvironmentInfo.Settings.ModFolder[GameMode.ModeId]);
			m_vmaVirtualModActivator.Initialize();
			InstallerFactory = new ModInstallerFactory(p_gmdGameMode, p_eifEnvironmentInfo, p_futFileUtility, p_scxUIContext, p_ilgInstallLog, p_pmgPluginManager, m_vmaVirtualModActivator);
			DownloadMonitor = p_dmrMonitor;
			ModActivationMonitor = p_mamMonitor;
			ModAdditionQueue = new AddModQueue(p_eifEnvironmentInfo, this);
			AutoUpdater = new AutoUpdater(p_mrpModRepository, p_mdrManagedModRegistry, p_eifEnvironmentInfo);
			LoginTask = new LoginFormTask(this);
		}

		#endregion

		#region Login Management

		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_vmlViewModel">The view model that provides the data and operations for this view.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		public bool Login()
		{
			if (LoginTask.LoggedOut)
				LoginTask.Update();
			else if (LoginTask.LoggingIn)
				MessageBox.Show("Wait for the login attempt.", "Login in progress...", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return LoginTask.LoggedIn;
		}

		public void Logout()
		{
			LoginTask.Reset();
		}

		#endregion

		#region Mod Addition

		/// <summary>
		/// Installs the specified mod.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask AddMod(string p_strPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			Uri uriPath = new Uri(p_strPath);
			if (uriPath.Scheme.ToLowerInvariant().ToString() == "nxm")
			{
				if (!ModRepository.IsOffline)
				{
					return ModAdditionQueue.AddMod(uriPath, p_cocConfirmOverwrite);
				}
				else
				{
					Login();
					return AsyncAddMod(uriPath, p_cocConfirmOverwrite);
				}
			}
			else
			{
				return ModAdditionQueue.AddMod(uriPath, p_cocConfirmOverwrite);
			}
		}

		/// <summary>
		/// Loads the list of mods that are queued to be added to the mod manager.
		/// </summary>
		public void LoadQueuedMods()
		{
			ModAdditionQueue.LoadQueuedMods();
		}

		#endregion

		#region Mod Removal

		/// <summary>
		/// Deletes the given mod.
		/// </summary>
		/// <remarks>
		/// The mod is deactivated, unregistered, and then deleted.
		/// </remarks>
		/// <param name="p_modMod">The mod to delete.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation,
		/// or <c>null</c> if no long-running operation needs to be done.</returns>
		public IBackgroundTaskSet DeleteMod(IMod p_modMod, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			ModDeleter mddDeleter = InstallerFactory.CreateDelete(p_modMod, p_rolActiveMods);
			mddDeleter.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(Deactivator_TaskSetCompleted);
			mddDeleter.Install();
			DeleteXMLInstalledFile(p_modMod);
			return mddDeleter;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of the mod deletion
		/// mod deativator.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void Deactivator_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			if (e.Success)
				ManagedModRegistry.UnregisterMod((IMod)e.ReturnValue);
		}

		#endregion

		public void ReinitializeInstallLog(string p_strInstallLogPath)
		{
			InstallationLog = InstallationLog.ReInitialize(p_strInstallLogPath);
		}

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to activate.</param>
		/// <param name="p_dlgUpgradeConfirmationDelegate">The delegate that is called to confirm whether an upgrade install should be performed.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list or Active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ActivateMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			if (InstallationLog.ActiveMods.Contains(p_modMod))
				return null;
			DeleteXMLInstalledFile(p_modMod);
			return Activator.Activate(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods, false);
		}

		/// <summary>
		/// Reinstalls the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to reinstall.</param>
		/// <param name="p_dlgUpgradeConfirmationDelegate">The delegate that is called to confirm whether an upgrade install should be performed.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list or Active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ReinstallMod(IMod p_modMod, ConfirmModUpgradeDelegate p_dlgUpgradeConfirmationDelegate, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			DeleteXMLInstalledFile(p_modMod);
			return Activator.Activate(p_modMod, p_dlgUpgradeConfirmationDelegate, p_dlgOverwriteConfirmationDelegate, p_rolActiveMods, true);
		}

	/// <summary>
	/// Forces an upgrade from one mod to another.
	/// </summary>
	/// <remarks>
	/// No checks as to whether the two mods are actually related are performed. The new mod is reactivated
	/// as if it were the old mod, and the old mod is replaced by the new mod.
	/// </remarks>
	/// <param name="p_modOldMod">The mod from which to upgrade.</param>
	/// <param name="p_modNewMod">The mod to which to upgrade.</param>
	/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
	/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
	public IBackgroundTaskSet ForceUpgrade(IMod p_modOldMod, IMod p_modNewMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return Activator.ForceUpgrade(p_modOldMod, p_modNewMod, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Reactivates the given mod.
		/// </summary>
		/// <remarks>
		/// A reactivation is an upgrade of a mod to itself. It re-runs the activation,
		/// without changing the installed precedence of its files and installed values.
		/// </remarks>
		/// <param name="p_modMod">The mod to reactivate.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet ReactivateMod(IMod p_modMod, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			if (!InstallationLog.ActiveMods.Contains(p_modMod))
				throw new InvalidOperationException(String.Format("Cannot reactivate the given mod, {0}. It is not active.", p_modMod.ModName));
			ModActivator marActivator = new ModActivator(InstallationLog, InstallerFactory);
			return marActivator.Reactivate(p_modMod, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// deactivates the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to deactivate.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTaskSet DeactivateMod(IMod p_modMod, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			if (!InstallationLog.ActiveMods.Contains(p_modMod))
				return null;
			ModUninstaller munUninstaller = InstallerFactory.CreateUninstaller(p_modMod, p_rolActiveMods);
			munUninstaller.Install();
			DeleteXMLInstalledFile(p_modMod);
			return munUninstaller;
		}

		#endregion

		#region Mod Tagging

		/// <summary>
		/// Gets the tagger to use to tag mods with metadata.
		/// </summary>
		/// <returns>The tagger to use to tag mods with metadata.</returns>
		public AutoTagger GetModTagger()
		{
			return new AutoTagger(ModRepository);
		}

		#endregion

		#region Mod Updating

		/// <summary>
		/// Toggles the endorsement for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to endorse/unendorse.</param>
		public void ToggleModEndorsement(IMod p_modMod)
		{
			AutoUpdater.ToggleModEndorsement(p_modMod);
		}

		/// <summary>
		/// Switches the mod category.
		/// </summary>
		/// <param name="p_modMod">The mod.</param>
		/// <param name="p_intCategoryId">The new category id.</param>
		public void SwitchModCategory(IMod p_modMod, Int32 p_intCategoryId)
		{
			AutoUpdater.SwitchModCategory(p_modMod, p_intCategoryId);
		}

		public IBackgroundTask UpdateCategories(CategoryManager p_cmCategoryManager, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm)
		{
			if (ModRepository.UserStatus != null)
			{
				CategoriesUpdateCheckTask cutCategoriesUpdateCheck = new CategoriesUpdateCheckTask(p_cmCategoryManager, p_pmProfileManager, ModRepository, CurrentGameModeModDirectory);
				cutCategoriesUpdateCheck.Update(p_camConfirm);
				return cutCategoriesUpdateCheck;
			}
			else
				throw new Exception("Login required");
		}

		#region asyncUpdateCategories

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		/// <param name="p_booMissingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public void AsyncUpdateCategories(CategoryManager p_cmCategoryManager, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm)
		{
			CategoriesUpdateCheckTask cutCategoriesUpdateCheck = new CategoriesUpdateCheckTask(p_cmCategoryManager, p_pmProfileManager, ModRepository, CurrentGameModeModDirectory);
			AsyncUpdateCategoriesTask(cutCategoriesUpdateCheck, p_camConfirm);
		}

		public async Task AsyncUpdateCategoriesTask(CategoriesUpdateCheckTask p_cutCategoriesUpdateCheck, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_cutCategoriesUpdateCheck.Update(p_camConfirm);
					UpdateCategoriesCheckStarted(this, new EventArgs<IBackgroundTask>(p_cutCategoriesUpdateCheck));
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ActivateMultipleMods(List<IMod> p_lstModList, bool p_booAllowCancel, ConfirmActionMethod p_camConfirm, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			ActivateMultipleModsTask ammActivateAllMods = new ActivateMultipleModsTask(p_lstModList, p_booAllowCancel, this.InstallationLog, this.InstallerFactory, p_camConfirm, p_dlgOverwriteConfirmationDelegate);
			ammActivateAllMods.Update(p_camConfirm);
			return ammActivateAllMods;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		/// <param name="p_booMissingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask UpdateMods(List<IMod> p_lstModList, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, bool p_booOverrideCategorySetup, bool? p_booMissingDownloadId)
		{
			if (ModRepository.UserStatus != null)
			{
				ModUpdateCheckTask mutModUpdateCheck = new ModUpdateCheckTask(AutoUpdater, p_pmProfileManager, ModRepository, p_lstModList, p_booOverrideCategorySetup, p_booMissingDownloadId, EnvironmentInfo.Settings.OverrideLocalModNames);
				mutModUpdateCheck.Update(p_camConfirm);
				return mutModUpdateCheck;
			}
			else
				throw new Exception("Login required");
		}

		/// <summary>
		/// Disables multiple mods.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask DisableMultipleMods(List<IMod> p_rolModList, ConfirmActionMethod p_camConfirm)
		{
			DisableMultipleModsTask dmmDisableAllMods = new DisableMultipleModsTask(p_rolModList, VirtualModActivator);
			dmmDisableAllMods.Update(p_camConfirm);
			return dmmDisableAllMods;
		}

		/// <summary>
		/// Disables multiple mods.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask DeleteMultipleMods(ReadOnlyObservableList<IMod> p_rolModList, ConfirmActionMethod p_camConfirm)
		{
			DeleteMultipleModsTask dmmDeleteAllMods = new DeleteMultipleModsTask(p_rolModList, VirtualModActivator, ManagedModRegistry, this, InstallationLog.ActiveMods, InstallerFactory);
			dmmDeleteAllMods.Update(p_camConfirm);
			return dmmDeleteAllMods;
		}

		/// <summary>
		/// Uninstalls multiple mods.
		/// </summary>
		/// <param name="p_rolModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask DeactivateMultipleMods(ReadOnlyObservableList<IMod> p_rolModList, bool p_booFilesOnly, ConfirmActionMethod p_camConfirm)
		{
			DeactivateMultipleModsTask dmmDeactivateAllMods = new DeactivateMultipleModsTask(p_rolModList, this.InstallationLog, this.InstallerFactory, this.VirtualModActivator, GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, p_booFilesOnly);
			dmmDeactivateAllMods.Update(p_camConfirm);
			return dmmDeactivateAllMods;
		}

		#region asyncTag

		public async void AsyncTagMod(ModManagement.UI.ModManagerVM p_ModManagerVM, ModManagement.UI.ModTaggerVM p_ModTaggerVM, EventHandler<EventArgs<ModManagement.UI.ModTaggerVM>> p_TaggingMod)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_TaggingMod(p_ModManagerVM, new EventArgs<ModManagement.UI.ModTaggerVM>(p_ModTaggerVM));
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion

		#region asyncAddMod

		public IBackgroundTask AsyncAddMod(Uri p_uriPath, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			IBackgroundTask tskAddModTask = ModAdditionQueue.AddMod(p_uriPath, p_cocConfirmOverwrite);
			AsyncAddModTask(tskAddModTask);
			return tskAddModTask;
		}

		public async void AsyncAddModTask(IBackgroundTask p_tskAddModTask)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					if (p_tskAddModTask.Status == BackgroundTasks.TaskStatus.Paused)
						p_tskAddModTask.Resume();
					break;
				}
				else
				{
					intRetry++;
				}
			}
		} 
		#endregion

		#region asyncUpdate

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		/// <param name="p_booMissingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public void AsyncUpdateMods(List<IMod> p_lstModList, IProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, bool p_booOverrideCategorySetup, bool? p_booMissingDownloadId)
		{
			ModUpdateCheckTask mutModUpdateCheck = new ModUpdateCheckTask(AutoUpdater, p_pmProfileManager, ModRepository, p_lstModList, p_booOverrideCategorySetup, p_booMissingDownloadId, EnvironmentInfo.Settings.OverrideLocalModNames);
			AsyncUpdateModsTask(mutModUpdateCheck, p_camConfirm);
		}

		public async Task AsyncUpdateModsTask(ModUpdateCheckTask p_mutModUpdateCheck, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_mutModUpdateCheck.Update(p_camConfirm);
					UpdateCheckStarted(this, new EventArgs<IBackgroundTask>(p_mutModUpdateCheck));
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion

		#region asyncEndorse

		/// <summary>
		/// Async toggle of the endorsement for the given mod.
		/// </summary>
		/// <param name="p_modMod">The mod to endorse/unendorse.</param>
		public async void AsyncEndorseMod(IMod p_modMod)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					AutoUpdater.ToggleModEndorsement(p_modMod);
					break;
				}
				else
					intRetry++;
			}
		}

		#endregion


		#region Automatic Download

		/// <summary>
		/// Starts the Automatic Download.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask AutomaticDownload(List<string> p_strMissingMods, ProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			AutomaticDownloadTask amtProfileAdder = new AutomaticDownloadTask(p_strMissingMods, this, p_pmProfileManager, p_cocConfirmOverwrite);
			amtProfileAdder.Update(p_camConfirm);
			return amtProfileAdder;
		}

		/// <summary>
		/// Starts the Automatic Download.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public void AsyncAutomaticDownload(List<string> p_strMissingMods, ProfileManager p_pmProfileManager, ConfirmActionMethod p_camConfirm, ConfirmOverwriteCallback p_cocConfirmOverwrite)
		{
			AutomaticDownloadTask adtAutomaticDownload = new AutomaticDownloadTask(p_strMissingMods, this, p_pmProfileManager, p_cocConfirmOverwrite);
			AsyncAutomaticDownloadTask(adtAutomaticDownload, p_camConfirm);
		}

		public async void AsyncAutomaticDownloadTask(AutomaticDownloadTask p_pstAutomaticDownloadTask, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (LoginTask.LoggedIn)
				{
					p_pstAutomaticDownloadTask.Update(p_camConfirm);
					AutomaticDownloadStarted(this, new EventArgs<IBackgroundTask>(p_pstAutomaticDownloadTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The mod list.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask SetupReadMeManager(List<IMod> p_lstModList, ConfirmActionMethod p_camConfirm)
		{
			ReadMeSetupTask rmsReadMeManagerSetup = new ReadMeSetupTask(ReadMeManager, p_lstModList);
			rmsReadMeManagerSetup.Update(p_camConfirm);
			return rmsReadMeManagerSetup;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_hashMods">The hash of mods.</param>
		/// <param name="p_booEnable">Enable/Disable/Toggle.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask ToggleUpdateWarningTask(HashSet<IMod> p_hashMods, bool? p_booEnable, ConfirmActionMethod p_camConfirm)
		{
			ToggleUpdateWarningTask tuwToggleWarning = new ToggleUpdateWarningTask(p_hashMods, p_booEnable, p_camConfirm);
			tuwToggleWarning.Update(p_camConfirm);
			return tuwToggleWarning;
		}

		#endregion

		#region Mod Search
		
		/// <summary>
		/// Returns the mod registered with the given file name.
		/// </summary>
		/// <param name="p_strFilename">The path of the mod to return</param>
		/// <returns>The mod registered with the given file name, or
		/// <c>null</c> if there is no registered mod with the given file name.</returns>
		public IMod GetModByFilename(string p_strFilename)
		{
			return ManagedModRegistry.GetModByFilename(p_strFilename);
		}

		/// <summary>
		/// Returns the mod registered with the given downloadId.
		/// </summary>
		/// <param name="p_strDownloadID">The path of the mod to return</param>
		/// <returns>The mod registered with the given downloadId, or
		/// <c>null</c> if there is no registered mod with the given downloadId.</returns>
		public IMod GetModByDownloadID(string p_strDownloadID)
		{
			return ManagedModRegistry.GetModByDownloadID(p_strDownloadID);
		}

		#endregion

		/// <summary>
		/// Sets the current profile manager.
		/// </summary>
		public void SetProfileManager(IProfileManager p_ipmProfileManager)
		{
			InstallerFactory.SetProfileManager(p_ipmProfileManager);
		}

		/// <summary>
		/// If the mod is scripted, deletes the XMLInstalledFiles file inside the InstallInfo\Scripted folder.
		/// </summary>
		private void DeleteXMLInstalledFile(IMod p_modMod)
		{
			string strInstallFilesPath = Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(p_modMod.Filename)) + ".xml";
			if (File.Exists(strInstallFilesPath))
				FileUtil.ForceDelete(strInstallFilesPath);
		}

		/// <summary>
		/// Purges all the scripted installers log files inside the InstallInfo\Scripted folder.
		/// </summary>
		public void PurgeXMLInstalledFile()
		{
			string strInstallFilesPath = Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"));
			if (Directory.Exists(strInstallFilesPath))
			{
				foreach (string file in Directory.EnumerateDirectories(strInstallFilesPath, "*.xml", SearchOption.TopDirectoryOnly))
				{
					if (File.Exists(file))
						FileUtil.ForceDelete(file);
				}
			}
		}

		public bool IsMatchingVersion(string p_strLocalVersion, string p_strProfileVersion)
		{
			Regex rgxClean = new Regex(@"([v(ver)]\.?)|((\.0)+$)", RegexOptions.IgnoreCase);
			string strThisVersion = rgxClean.Replace(p_strProfileVersion ?? "", "");
			string strThatVersion = rgxClean.Replace(p_strLocalVersion ?? "", "");
			if (String.IsNullOrEmpty(strThisVersion) || string.IsNullOrEmpty(strThatVersion))
				return true;
			else
				return String.Equals(strThisVersion, strThatVersion, StringComparison.OrdinalIgnoreCase);
		}
	}
}
