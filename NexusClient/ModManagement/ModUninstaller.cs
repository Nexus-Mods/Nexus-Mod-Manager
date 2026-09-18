using System;
using System.Collections.Generic;
using System.IO;
using ChinhDo.Transactions;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Threading;
using Nexus.Transactions;
using Nexus.Client.Util.Collections;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// This installs mods.
	/// </summary>
	public class ModUninstaller : ModInstallerBase
	{
		private bool m_booNativeMutationStarted;

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

		/// <summary>
		/// Gets the method-neutral deployment coordinator.
		/// </summary>
		protected IModDeploymentManager DeploymentManager { get; private set; }

		/// <summary>
		/// Gets the profile manager used to flush deferred deployment snapshots.
		/// </summary>
		protected IProfileManager ProfileManager { get; private set; }

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
		public ModUninstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IModDeploymentManager p_mdmDeploymentManager, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, ReadOnlyObservableList<IMod> p_rolActiveMods)
			: this(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_mdmDeploymentManager, p_ilgModInstallLog, p_pmgPluginManager, p_rolActiveMods, null)
		{
		}

		/// <summary>
		/// Initializes an uninstaller with the profile manager used to flush the final deployment snapshot.
		/// </summary>
		public ModUninstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, IModDeploymentManager p_mdmDeploymentManager, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, ReadOnlyObservableList<IMod> p_rolActiveMods, IProfileManager p_ipmProfileManager)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			ModInstallLog = p_ilgModInstallLog;
			PluginManager = p_pmgPluginManager;
			ActiveMods = p_rolActiveMods;
			VirtualModActivator = p_ivaVirtualModActivator;
			DeploymentManager = p_mdmDeploymentManager;
			ProfileManager = p_ipmProfileManager;
		}

		#endregion

		/// <summary>
		/// Uninstalls the mod.
		/// </summary>
		public void Install()
		{
			bool booIsInstallLogActive = ModInstallLog.ActiveMods.Contains(Mod);
			bool booHasManagedFiles = DeploymentManager != null
				? DeploymentManager.HasManagedFiles(Mod)
				: VirtualModActivator != null && VirtualModActivator.CheckHasActiveLinks(Mod);
			if (!booIsInstallLogActive && !booHasManagedFiles)
			{
				OnTaskSetCompleted(ModOperationReportedStatus.NoOp, ModOperationDurability.NotStarted, true,
					"The mod was successfully deactivated.", Mod);
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
			bool booCancelled = false;
			ModOperationReportedStatus reportedStatus = ModOperationReportedStatus.Failed;
			string strErrorMessage = String.Empty;
			try
			{
				lock (objUninstallLock)
				{
					bool booIsInstallLogActive = ModInstallLog.ActiveMods.Contains(Mod);
					bool booHasVirtualLinks = VirtualModActivator != null && VirtualModActivator.CheckHasActiveLinks(Mod);
					ModInstallMethod installMethod = booIsInstallLogActive
						? ModInstallLog.GetModInstallMethod(Mod)
						: ModInstallMethod.Virtual;
					bool booHasPromotedFiles = DeploymentManager != null && DeploymentManager.HasPromotedFiles(Mod);

					if (installMethod == ModInstallMethod.Virtual && booHasVirtualLinks && !booHasPromotedFiles)
					{
						VirtualModDisableTask vdtDisableTask = new VirtualModDisableTask(Mod, VirtualModActivator, DisableVirtualFilesOnly);
						OnTaskStarted(vdtDisableTask);
						m_booNativeMutationStarted = true;
						if (!vdtDisableTask.Execute())
						{
							booCancelled = vdtDisableTask.Status == TaskStatus.Cancelled || vdtDisableTask.Status == TaskStatus.Cancelling;
							reportedStatus = booCancelled ? ModOperationReportedStatus.Cancelled : ModOperationReportedStatus.Failed;
							strErrorMessage = vdtDisableTask.ErrorMessage;
							OnTaskSetCompleted(reportedStatus, DetermineOperationDurability(), false,
								"The mod was not deactivated." + Environment.NewLine + strErrorMessage, Mod);
							return;
						}

						if (vdtDisableTask.Status == TaskStatus.Cancelled || vdtDisableTask.Status == TaskStatus.Cancelling)
						{
							OnTaskSetCompleted(ModOperationReportedStatus.Cancelled, DetermineOperationDurability(), false,
								"The mod deactivation was cancelled.", Mod);
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
						m_booNativeMutationStarted = true;
						using (TransactionScope tsTransaction = new TransactionScope())
						{
							TxFileManager tfmFileManager = new TxFileManager();
							bool deploymentRemovalSucceeded = true;

							if (installMethod == ModInstallMethod.Direct || booHasPromotedFiles)
							{
								MixedModDeploymentRemovalTask removalTask = new MixedModDeploymentRemovalTask(Mod, DeploymentManager, tfmFileManager);
								OnTaskStarted(removalTask);
								deploymentRemovalSucceeded = removalTask.Execute();
								booCancelled = removalTask.Status == TaskStatus.Cancelled || removalTask.Status == TaskStatus.Cancelling;
								if (!deploymentRemovalSucceeded)
									strErrorMessage = removalTask.ErrorMessage;
								else if (PluginManager != null && removalTask.AbsentPaths.Count > 0)
								{
									List<string> removedPlugins = new List<string>();
									foreach (string path in removalTask.AbsentPaths)
									{
										if (PluginManager.IsActivatiblePluginFile(path))
											removedPlugins.Add(path);
									}
									if (removedPlugins.Count > 0)
										PluginManager.RemovePlugins(removedPlugins);
								}
							}

							if (deploymentRemovalSucceeded)
							{
								booSuccess = RunBasicUninstallScript(tfmFileManager, out strErrorMessage);
								if (booSuccess)
								{
									ModInstallLog.RemoveMod(Mod);
									tsTransaction.Complete();
									Mod.InstallDate = null;
								}
							}
						}
					}

					if (booSuccess)
					{
						VirtualModActivator.PublishPendingDeploymentChanges();
						ProfileManager?.UpdateCurrentDeploymentManifest();
						DeleteXMLInstalledFile(Mod);
					}
				}
			}
			catch (Exception ex)
			{
				strErrorMessage = ex.Message;
				booSuccess = false;
			}

			if (booCancelled)
			{
				reportedStatus = ModOperationReportedStatus.Cancelled;
				OnTaskSetCompleted(reportedStatus, DetermineOperationDurability(), false, "The mod deactivation was cancelled.", Mod);
			}
			else if (booSuccess)
			{
				reportedStatus = ModOperationReportedStatus.Succeeded;
				OnTaskSetCompleted(reportedStatus, DetermineOperationDurability(), true,
					"The mod was successfully deactivated." + Environment.NewLine + strErrorMessage, Mod);
			}
			else
			{
				OnTaskSetCompleted(reportedStatus, DetermineOperationDurability(), false,
					"The mod was not deactivated." + Environment.NewLine + strErrorMessage, Mod);
			}
		}

		/// <summary>
		/// Determines what can be proven about durable native state after this uninstaller terminates.
		/// </summary>
		protected virtual ModOperationDurability DetermineOperationDurability()
		{
			if (!m_booNativeMutationStarted)
				return ModOperationDurability.NotStarted;
			if (ModInstallLog == null)
				return ModOperationDurability.Unknown;

			try
			{
				if (!String.IsNullOrEmpty(ModInstallLog.GetModKey(Mod)))
					return ModOperationDurability.Unknown;
				if (VirtualModActivator != null && VirtualModActivator.CheckHasActiveLinks(Mod))
					return ModOperationDurability.Unknown;
				if (DeploymentManager != null && DeploymentManager.HasManagedFiles(Mod))
					return ModOperationDurability.Unknown;

				return ModOperationDurability.VerifiedCommitted;
			}
			catch
			{
				return ModOperationDurability.Unknown;
			}
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
			ScriptedFileSelectionCache.DeleteArtifacts(strInstallFilesPath);
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

	/// <summary>
	/// Reports progress and exposes cooperative cancellation while removing mixed deployment targets.
	/// </summary>
	internal sealed class MixedModDeploymentRemovalTask : BackgroundTask
	{
		private readonly IMod m_modMod;
		private readonly IModDeploymentManager m_mdmDeploymentManager;
		private readonly TxFileManager m_tfmFileManager;
		private readonly string m_strDisablingFilesText;
		private readonly string m_strDisablingFilesFormat;
		private readonly string m_strDisablingFileFormat;
		private readonly string m_strCancelledText;
		private readonly string m_strDisabledFilesText;

		/// <summary>
		/// Initializes a mixed deployment removal task.
		/// </summary>
		public MixedModDeploymentRemovalTask(IMod p_modMod, IModDeploymentManager p_mdmDeploymentManager, TxFileManager p_tfmFileManager)
		{
			m_modMod = p_modMod;
			m_mdmDeploymentManager = p_mdmDeploymentManager;
			m_tfmFileManager = p_tfmFileManager;
			m_strDisablingFilesText = LanguageManager.Get("Tasks.Mods.DisablingDeployedFiles", "Disabling deployed files...");
			m_strDisablingFilesFormat = LanguageManager.GetFormat("Tasks.Mods.DisablingDeployedFilesForMod", "Disabling deployed files: {0}");
			m_strDisablingFileFormat = LanguageManager.GetFormat("Tasks.ModLinks.DisablingFile", "Disabling: {0}");
			m_strCancelledText = LanguageManager.Get("Common.Status.Cancelled", "Cancelled");
			m_strDisabledFilesText = LanguageManager.Get("Tasks.Mods.DisabledDeployedFiles", "Disabled deployed files.");
			AbsentPaths = new string[0];
		}

		public string ErrorMessage { get; private set; }

		public IReadOnlyCollection<string> AbsentPaths { get; private set; }

		/// <summary>
		/// Removes mixed deployment targets synchronously inside the caller-owned transaction.
		/// </summary>
		public bool Execute()
		{
			OverallMessage = String.Format(m_strDisablingFilesFormat, m_modMod == null ? String.Empty : m_modMod.ModName);
			ItemMessage = m_strDisablingFilesText;
			ShowItemProgress = true;
			ShowItemProgressAsMarquee = true;
			ItemProgress = 0;
			ItemProgressStepSize = 1;

			try
			{
				ModDeploymentManager deploymentManager = m_mdmDeploymentManager as ModDeploymentManager;
				AbsentPaths = deploymentManager == null
					? m_mdmDeploymentManager.UninstallMixedMod(m_modMod, m_tfmFileManager)
					: deploymentManager.UninstallMixedMod(m_modMod, m_tfmFileManager, UpdateProgress, IsCancellationRequested);

				if (Status == TaskStatus.Cancelling)
				{
					Status = TaskStatus.Cancelled;
					OnTaskEnded(m_strCancelledText, m_modMod);
					return false;
				}

				ShowItemProgressAsMarquee = false;
				Status = TaskStatus.Complete;
				OnTaskEnded(m_strDisabledFilesText, m_modMod);
				return true;
			}
			catch (OperationCanceledException) when (IsCancellationRequested())
			{
				Status = TaskStatus.Cancelled;
				OnTaskEnded(m_strCancelledText, m_modMod);
				return false;
			}
			catch (Exception ex)
			{
				ErrorMessage = ex.Message;
				Status = TaskStatus.Error;
				OnTaskEnded(ex.Message, m_modMod);
				return false;
			}
		}

		/// <summary>
		/// Gets whether the user requested cancellation of this removal task.
		/// </summary>
		private bool IsCancellationRequested()
		{
			return Status == TaskStatus.Cancelling || Status == TaskStatus.Cancelled;
		}

		/// <summary>
		/// Updates the task progress after a mixed deployment target is removed.
		/// </summary>
		private void UpdateProgress(ModDeploymentTarget p_mdtTarget, int p_intProcessed, int p_intTotal)
		{
			ShowItemProgressAsMarquee = false;
			ItemProgressMaximum = Math.Max(1, p_intTotal);
			ItemProgress = Math.Min(p_intProcessed, ItemProgressMaximum);
			if (p_mdtTarget != null)
				ItemMessage = String.Format(m_strDisablingFileFormat, p_mdtTarget.RelativePath);
		}
	}

	internal sealed class VirtualModDisableTask : BackgroundTask
	{
		private readonly IMod m_modMod;
		private readonly IVirtualModActivator m_ivaVirtualModActivator;
		private readonly bool m_booFilesOnly;
		private readonly string m_strDisablingFilesText;
		private readonly string m_strDisablingFilesFormat;
		private readonly string m_strCancelledText;
		private readonly string m_strDisabledFilesText;

		public VirtualModDisableTask(IMod p_modMod, IVirtualModActivator p_ivaVirtualModActivator, bool p_booFilesOnly)
		{
			m_modMod = p_modMod;
			m_ivaVirtualModActivator = p_ivaVirtualModActivator;
			m_booFilesOnly = p_booFilesOnly;
			m_strDisablingFilesText = LanguageManager.Get("Tasks.Mods.DisablingDeployedFiles", "Disabling deployed files...");
			m_strDisablingFilesFormat = LanguageManager.GetFormat("Tasks.Mods.DisablingDeployedFilesForMod", "Disabling deployed files: {0}");
			m_strCancelledText = LanguageManager.Get("Common.Status.Cancelled", "Cancelled");
			m_strDisabledFilesText = LanguageManager.Get("Tasks.Mods.DisabledDeployedFiles", "Disabled deployed files.");
		}

		public string ErrorMessage { get; private set; }

		public bool Execute()
		{
			OverallMessage = String.Format(m_strDisablingFilesFormat, m_modMod == null ? String.Empty : m_modMod.ModName);
			ItemMessage = m_strDisablingFilesText;
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
					OnTaskEnded(m_strCancelledText, m_modMod);
					return false;
				}

				ShowItemProgressAsMarquee = false;
				Status = TaskStatus.Complete;
				OnTaskEnded(m_strDisabledFilesText, m_modMod);
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

			ItemMessage = String.IsNullOrEmpty(p_vdpProgress.Message) ? m_strDisablingFilesText : p_vdpProgress.Message;
			if (p_vdpProgress.Total > 0)
			{
				ShowItemProgressAsMarquee = false;
				ItemProgressMaximum = p_vdpProgress.Total;
				ItemProgress = p_vdpProgress.Current;
			}
		}
	}
}
