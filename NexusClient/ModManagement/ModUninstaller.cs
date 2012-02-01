using ChinhDo.Transactions;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Threading;
using Nexus.Transactions;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This installs mods.
	/// </summary>
	public class ModUninstaller : ModInstallerBase
	{
		#region Properties

		/// <summary>
		/// Gets or sets the mod being installed.
		/// </summary>
		/// <value>The mod being installed.</value>
		protected IMod Mod { get; set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The the current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets or sets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; set; }

		/// <summary>
		/// Gets the install log that tracks mod install info
		/// for the current game mode.
		/// </summary>
		/// <value>The install log that tracks mod install info
		/// for the current game mode.</value>
		protected IInstallLog ModInstallLog { get; private set; }

		/// <summary>
		/// Gets the manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info
		/// for the current game mode</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		public ModUninstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			ModInstallLog = p_ilgModInstallLog;
			PluginManager = p_pmgPluginManager;
		}

		#endregion

		/// <summary>
		/// Uninstalls the mod.
		/// </summary>
		public void Install()
		{
			if (!ModInstallLog.ActiveMods.Contains(Mod))
			{
				OnTaskSetCompleted(true, "The mod was successfully deactivated.", Mod);
				return;
			}
			TrackedThread thdWorker = new TrackedThread(RunTasks);
			thdWorker.Thread.IsBackground = false;
			thdWorker.Start();
		}

		/// <summary>
		/// Runs the uninstall tasks.
		/// </summary>
		protected void RunTasks()
		{
			//the install process modifies INI and config files.
			// if multiple sources (i.e., installs) try to modify
			// these files simultaneously the outcome is not well known
			// (e.g., one install changes SETTING1 in a config file to valueA
			// while simultaneously another install changes SETTING1 in the
			// file to value2 - after each install commits its changes it is
			// not clear what the value of SETTING1 will be).
			// as a result, we only allow one mod to be installed at a time,
			// hence the lock.
			bool booSuccess = false;
			lock (objInstallLock)
			{
				using (TransactionScope tsTransaction = new TransactionScope())
				{
					TxFileManager tfmFileManager = new TxFileManager();

					booSuccess = RunBasicUninstallScript(tfmFileManager);
					if (booSuccess)
					{
						ModInstallLog.RemoveMod(Mod);
						tsTransaction.Complete();
					}
				}
			}
			if (booSuccess)
				OnTaskSetCompleted(booSuccess, "The mod was successfully deactivated.", Mod);
			else
				OnTaskSetCompleted(booSuccess, "The mod was not deactivated.", Mod);
		}

		/// <summary>
		/// Runs the basic uninstall script.
		/// </summary>
		/// <remarks>
		/// A basic uninstall uninstalls all of the changes made when the mod was installed.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns><c>true</c> if the uninstallation was successful;
		/// <c>false</c> otherwise.</returns>
		protected bool RunBasicUninstallScript(TxFileManager p_tfmFileManager)
		{
			IDataFileUtil dfuDataFileUtility = new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath);

			IModFileInstaller mfiFileInstaller = new ModFileInstaller(GameMode.GameModeEnvironmentInfo, Mod, ModInstallLog, PluginManager, dfuDataFileUtility, p_tfmFileManager, null);
			IIniInstaller iniIniInstaller = new IniInstaller(Mod, ModInstallLog, p_tfmFileManager, null);
			IGameSpecificValueInstaller gviGameSpecificValueInstaller = GameMode.GetGameSpecificValueInstaller(Mod, ModInstallLog, p_tfmFileManager, new NexusFileUtil(EnvironmentInfo), null);
			
			InstallerGroup ipgInstallers = new InstallerGroup(dfuDataFileUtility, mfiFileInstaller, iniIniInstaller, gviGameSpecificValueInstaller, PluginManager);
			BasicUninstallTask butTask = new BasicUninstallTask(Mod, ipgInstallers, ModInstallLog);
			OnTaskStarted(butTask);
			bool booResult = butTask.Execute();

			mfiFileInstaller.FinalizeInstall();
			iniIniInstaller.FinalizeInstall();
			gviGameSpecificValueInstaller.FinalizeInstall();

			return booResult;
		}
	}
}
