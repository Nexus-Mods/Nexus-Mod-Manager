using System;
using System.IO;
using ChinhDo.Transactions;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Threading;
using Nexus.Transactions;
using Nexus.Client.Util.Collections;

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
		/// Gets or sets the mod name.
		/// </summary>
		/// <value>The mod name.</value>
		public string ModName
		{
			get
			{
				if (Mod != null)
					return Mod.ModName;
				else
					return null;
			}
		}

		/// <summary>
		/// Gets or sets the mod file name.
		/// </summary>
		/// <value>The mod file name.</value>
		public string ModFileName
		{
			get
			{
				if (Mod != null)
					return Mod.Filename;
				else
					return null;
			}
		}

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

		/// <summary>
		/// Gets the current virtual mod activator.
		/// </summary>
		/// <value>The current virtual mod activator.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		public bool DisableVirtualFilesOnly { get; set; }

		public bool Succeeded { get; private set; }

		public string CompletionMessage { get; private set; }

		protected ReadOnlyObservableList<IMod> ActiveMods { get; set; }

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
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		public ModUninstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			ModInstallLog = p_ilgModInstallLog;
			PluginManager = p_pmgPluginManager;
			ActiveMods = p_rolActiveMods;
			VirtualModActivator = p_ivaVirtualModActivator;
		}

		#endregion

		/// <summary>
		/// Uninstalls the mod.
		/// </summary>
		public void Install()
		{
			bool booIsInstallLogActive = ModInstallLog.ActiveMods.Contains(Mod);
			bool booHasVirtualLinks = VirtualModActivator != null && VirtualModActivator.CheckHasActiveLinks(Mod);
			if (!booIsInstallLogActive && !booHasVirtualLinks)
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
			string strErrorMessage = String.Empty;
			try
			{
				lock (objUninstallLock)
				{
					bool booIsInstallLogActive = ModInstallLog.ActiveMods.Contains(Mod);
					bool booHasVirtualLinks = VirtualModActivator != null && VirtualModActivator.CheckHasActiveLinks(Mod);

					if (booHasVirtualLinks)
					{
						VirtualModDisableTask vdtDisableTask = new VirtualModDisableTask(Mod, VirtualModActivator, DisableVirtualFilesOnly);
						OnTaskStarted(vdtDisableTask);
						if (!vdtDisableTask.Execute())
						{
							strErrorMessage = vdtDisableTask.ErrorMessage;
							OnTaskSetCompleted(false, "The mod was not deactivated." + Environment.NewLine + strErrorMessage, Mod);
							return;
						}

						if (vdtDisableTask.Status == TaskStatus.Cancelled || vdtDisableTask.Status == TaskStatus.Cancelling)
						{
							OnTaskSetCompleted(false, "The mod deactivation was cancelled.", Mod);
							return;
						}
					}

					if (!booIsInstallLogActive)
					{
						Mod.InstallDate = null;
						booSuccess = true;
					}
					else
					{
						using (TransactionScope tsTransaction = new TransactionScope())
						{
							TxFileManager tfmFileManager = new TxFileManager();

							booSuccess = RunBasicUninstallScript(tfmFileManager, out strErrorMessage);
							if (booSuccess)
							{
								Mod.InstallDate = null;
								ModInstallLog.RemoveMod(Mod);
								tsTransaction.Complete();
							}
						}
					}

					if (booSuccess)
						DeleteXMLInstalledFile(Mod);
				}
			}
			catch (Exception ex)
			{
				strErrorMessage = ex.Message;
				booSuccess = false;
			}

			if (booSuccess)
				OnTaskSetCompleted(booSuccess, "The mod was successfully deactivated." + Environment.NewLine + strErrorMessage, Mod);
			else
				OnTaskSetCompleted(false, "The mod was not deactivated." + Environment.NewLine + strErrorMessage, Mod);
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
		protected override void OnTaskSetCompleted(TaskSetCompletedEventArgs e)
		{
			Succeeded = e.Success;
			CompletionMessage = e.Message;
			base.OnTaskSetCompleted(e);
		}

		private void DeleteXMLInstalledFile(IMod p_modMod)
		{
			string strInstallFilesPath = Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(p_modMod.Filename)) + ".xml";
			if (File.Exists(strInstallFilesPath))
				FileUtil.ForceDelete(strInstallFilesPath);
		}

		protected bool RunBasicUninstallScript(TxFileManager p_tfmFileManager, out string p_strErrorMessage)
		{
			p_strErrorMessage = null;
			IDataFileUtil dfuDataFileUtility = new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath);
			ModInstallRoot installRoot = ModInstallLog.GetModInstallRoot(Mod);
			string installBasePath = installRoot == ModInstallRoot.GameRoot ? GameMode.InstallationPath : GameMode.GameModeEnvironmentInfo.InstallationPath;

			IModFileInstaller mfiFileInstaller = new ModFileInstaller(GameMode.GameModeEnvironmentInfo, Mod, ModInstallLog, PluginManager, dfuDataFileUtility, p_tfmFileManager, null, GameMode.UsesPlugins, EnvironmentInfo, installBasePath);
			IIniInstaller iniIniInstaller = new IniInstaller(Mod, ModInstallLog, VirtualModActivator, p_tfmFileManager, null);
			IGameSpecificValueInstaller gviGameSpecificValueInstaller = GameMode.GetGameSpecificValueInstaller(Mod, ModInstallLog, p_tfmFileManager, new NexusFileUtil(EnvironmentInfo), null);

			InstallerGroup ipgInstallers = new InstallerGroup(dfuDataFileUtility, mfiFileInstaller, iniIniInstaller, gviGameSpecificValueInstaller, PluginManager);
			BasicUninstallTask butTask = new BasicUninstallTask(Mod, VirtualModActivator, ipgInstallers, ModInstallLog, GameMode, ActiveMods);
			OnTaskStarted(butTask);

			bool booResult = butTask.Execute();

			if (mfiFileInstaller.InstallErrors.Count > 0)
			{
				p_strErrorMessage = Environment.NewLine + "There were issues while installing/uninstalling this mod:" + Environment.NewLine;
				foreach (string strPath in mfiFileInstaller.InstallErrors)
					DetailsErrorMessage += strPath + Environment.NewLine;

				PopupErrorMessage = p_strErrorMessage;
				PopupErrorMessageType = butTask.strPopupErrorMessageType;
			}

			mfiFileInstaller.FinalizeInstall();
			iniIniInstaller.FinalizeInstall();
			if (gviGameSpecificValueInstaller != null)
				gviGameSpecificValueInstaller.FinalizeInstall();

			return booResult;
		}
	}

	internal sealed class VirtualModDisableTask : BackgroundTask
	{
		private readonly IMod m_modMod;
		private readonly IVirtualModActivator m_ivaVirtualModActivator;
		private readonly bool m_booFilesOnly;

		public VirtualModDisableTask(IMod p_modMod, IVirtualModActivator p_ivaVirtualModActivator, bool p_booFilesOnly)
		{
			m_modMod = p_modMod;
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_booFilesOnly = p_booFilesOnly;
		}

		public string ErrorMessage { get; private set; }

		public bool Execute()
		{
			OverallMessage = "Disabling deployed files: " + (m_modMod == null ? String.Empty : m_modMod.ModName);
			ItemMessage = "Disabling deployed files...";
			ShowItemProgress = true;
			ShowItemProgressAsMarquee = true;
			ItemProgress = 0;
			ItemProgressStepSize = 1;

			try
			{
				VirtualModActivator vmaCompatibility = m_ivaVirtualModActivator as VirtualModActivator;
				if (vmaCompatibility != null)
				{
					vmaCompatibility.DisableModWithProgress(m_modMod, m_booFilesOnly, UpdateProgress);
				}
				else if (m_ivaVirtualModActivator != null)
				{
					if (m_booFilesOnly)
						m_ivaVirtualModActivator.DisableModFiles(m_modMod);
					else
						m_ivaVirtualModActivator.DisableMod(m_modMod);
				}

				if (Status == TaskStatus.Cancelling)
				{
					Status = TaskStatus.Cancelled;
					OnTaskEnded("Cancelled", m_modMod);
					return false;
				}

				ShowItemProgressAsMarquee = false;
				Status = TaskStatus.Complete;
				OnTaskEnded("Disabled deployed files.", m_modMod);
				return true;
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
				Status = TaskStatus.Error;
				OnTaskEnded(ex.Message, m_modMod);
				return false;
			}
		}

		private void UpdateProgress(VirtualModDisableProgress p_vdpProgress)
		{
			if (p_vdpProgress == null)
				return;

			ItemMessage = String.IsNullOrEmpty(p_vdpProgress.Message) ? "Disabling deployed files..." : p_vdpProgress.Message;
			if (p_vdpProgress.Total > 0)
			{
				ShowItemProgressAsMarquee = false;
				ItemProgressMaximum = p_vdpProgress.Total;
				ItemProgress = p_vdpProgress.Current;
			}
		}
	}
}
