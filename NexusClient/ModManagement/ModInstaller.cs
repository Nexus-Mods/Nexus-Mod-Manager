using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
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
	public class ModInstaller : ModInstallerBase
	{
		private ConfirmItemOverwriteDelegate m_dlgOverwriteConfirmationDelegate = null;
		private ModManager m_mmModManager = null;

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
				if(Mod != null)
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
		/// Gets or sets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; set; }

		/// <summary>
		/// Gets the mod activator to use to manage file installation.
		/// </summary>
		/// <value>The mod activator to use to manage file installation.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the profile manager.
		/// </summary>
		/// <value>The profile manager.</value>
		protected IProfileManager ProfileManager { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The the current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the manager to use to manage plugins.
		/// </summary>
		/// <value>The manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the install log that tracks mod install info
		/// for the current game mode.
		/// </summary>
		/// <value>The install log that tracks mod install info
		/// for the current game mode.</value>
		protected IInstallLog ModInstallLog { get; private set; }

		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; set; }

		/// <summary>
		/// Gets the <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.
		/// </summary>
		/// <value>The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</value>
		protected SynchronizationContext UIContext { get; private set; }

		protected ReadOnlyObservableList<IMod> ActiveMods { get; set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_ilgModInstallLog">The install log that tracks mod install info
		/// for the current game mode</param>
		/// <param name="p_pmgPluginManager">The plugin manager.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		public ModInstaller(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility, SynchronizationContext p_scxUIContext, IInstallLog p_ilgModInstallLog, IPluginManager p_pmgPluginManager, IVirtualModActivator p_ivaVirtualModActivator, IProfileManager p_prmProfileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate, ReadOnlyObservableList<IMod> p_rolActiveMods)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			FileUtility = p_futFileUtility;
			UIContext = p_scxUIContext;
			ModInstallLog = p_ilgModInstallLog;
			PluginManager = p_pmgPluginManager;
			VirtualModActivator = p_ivaVirtualModActivator;
			ProfileManager = p_prmProfileManager;
			m_dlgOverwriteConfirmationDelegate = p_dlgOverwriteConfirmationDelegate;
			ActiveMods = p_rolActiveMods;
		}

		#endregion

		/// <summary>
		/// Installs the mod.
		/// </summary>
		public void Install()
		{
			TrackedThread thdWorker = new TrackedThread(RunTasks);
			thdWorker.Thread.IsBackground = false;
			thdWorker.Start();
		}

		/// <summary>
		/// Runs the install tasks.
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
			string strMessage = "The mod was not activated.";
			
			try
			{
				lock (objInstallLock)
				{
					using (TransactionScope tsTransaction = new TransactionScope())
					{
						if (!File.Exists(Mod.Filename))
							throw new Exception("The selected file was not found: " + Mod.Filename);

						TxFileManager tfmFileManager = new TxFileManager();

						if (!BeginModReadOnlyTransaction())
							return;
						RegisterMod();
						booSuccess = RunScript(tfmFileManager);
						if (booSuccess)
						{
							Mod.InstallDate = DateTime.Now.ToString();
							tsTransaction.Complete();
							VirtualModActivator.SaveList(true);
							strMessage = "The mod was successfully activated.";
							GC.GetTotalMemory(true);
						}
					}
				}
			}
			catch (TransactionException)
			{
				throw;
			}
			catch (SecurityException)
			{
				throw;
			}
			catch (ObjectDisposedException)
			{
				throw;
			}
			//this blobck used to be conditionally excluded from debug builds,
			// presumably so that the debugger would break on the source of the
			// exception, however that prevents the full mod install flow from
			// happening, which lead to missed bugs
			catch (Exception e)
			{
				booSuccess = false;
				StringBuilder stbError = new StringBuilder(e.Message);
				if (e is FileNotFoundException)
					stbError.Append(" (" + ((FileNotFoundException)e).FileName + ")");
				if (e is IllegalFilePathException)
					stbError.Append(" (" + ((IllegalFilePathException)e).Path + ")");
				if (e.InnerException != null)
					stbError.AppendLine().AppendLine(e.InnerException.Message);
				if (e is RollbackException)
					foreach (RollbackException.ExceptedResourceManager erm in ((RollbackException)e).ExceptedResourceManagers)
					{
						stbError.AppendLine(erm.ResourceManager.ToString());
						stbError.AppendLine(erm.Exception.Message);
						if (erm.Exception.InnerException != null)
							stbError.AppendLine(erm.Exception.InnerException.Message);
					}
				string strExceptionMessageFormat = "A problem occurred during install: " + Environment.NewLine + "{0}" + Environment.NewLine + "The mod was not installed."; ;
				strMessage = String.Format(strExceptionMessageFormat, stbError.ToString());
				PopupErrorMessage = strMessage;
				PopupErrorMessageType = "Error";
			}
			finally
			{
				Mod.EndReadOnlyTransaction();
			}
			OnTaskSetCompleted(booSuccess, strMessage, Mod);
		}

		/// <summary>
		/// Puts the mod into read-only mode.
		/// </summary>
		/// <returns><c>true</c> if the the read only mode started;
		/// <c>false</c> otherwise.</returns>
		private bool BeginModReadOnlyTransaction()
		{
			PrepareModTask pmtTask = new PrepareModTask(FileUtility);
			OnTaskStarted(pmtTask);
			return pmtTask.PrepareMod(Mod);
		}

		#region Script Execution

		/// <summary>
		/// This executes the install script.
		/// </summary>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns><c>true</c> if the script completed successfully;
		/// <c>false</c> otherwise.</returns>
		protected bool RunScript(TxFileManager p_tfmFileManager)
		{
			IModFileInstaller mfiFileInstaller = CreateFileInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
			bool booResult = false;
			IIniInstaller iniIniInstaller = null;
			IGameSpecificValueInstaller gviGameSpecificValueInstaller = null;
			if (Mod.HasInstallScript)
			{
				if (CheckScriptedModLog())
					booResult = RunBasicInstallScript(mfiFileInstaller, ActiveMods, LoadXMLModFilesToInstall());
				else
				{
					try
					{
						IDataFileUtil dfuDataFileUtility = new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath);

						iniIniInstaller = CreateIniInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);
						gviGameSpecificValueInstaller = CreateGameSpecificValueInstaller(p_tfmFileManager, m_dlgOverwriteConfirmationDelegate);

						InstallerGroup ipgInstallers = new InstallerGroup(dfuDataFileUtility, mfiFileInstaller, iniIniInstaller, gviGameSpecificValueInstaller, PluginManager);
						IScriptExecutor sexScript = Mod.InstallScript.Type.CreateExecutor(Mod, GameMode, EnvironmentInfo, VirtualModActivator, ipgInstallers, UIContext);
						sexScript.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(ScriptExecutor_TaskStarted);
						sexScript.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(ScriptExecutor_TaskSetCompleted);
						booResult = sexScript.Execute(Mod.InstallScript);
					}
					catch (Exception ex)
					{
						PopupErrorMessage = ex.Message;
						PopupErrorMessageType = "Error";
					}

					iniIniInstaller.FinalizeInstall();

					if (gviGameSpecificValueInstaller != null)
						gviGameSpecificValueInstaller.FinalizeInstall();
				}
			}
			else
				booResult = RunBasicInstallScript(mfiFileInstaller, ActiveMods, null);
			mfiFileInstaller.FinalizeInstall();
			return booResult;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of script executors.
		/// </summary>
		/// <remarks>
		/// This unwires our listeners from the executor.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void ScriptExecutor_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			IBackgroundTaskSet btsExecutor = (IBackgroundTaskSet)sender;
			btsExecutor.TaskStarted -= ScriptExecutor_TaskStarted;
			btsExecutor.TaskSetCompleted -= ScriptExecutor_TaskSetCompleted;
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskStarted"/> event of script executors.
		/// </summary>
		/// <remarks>
		/// This bubbles the started task to any listeners.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void ScriptExecutor_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			OnTaskStarted(e.Argument);
		}

		/// <summary>
		/// Runs the basic install script.
		/// </summary>
		/// <remarks>
		/// A basic install installs all of the files in the mod to the installation directory,
		/// and activates all plugin files.
		/// </remarks>
		/// <param name="p_mfiFileInstaller">The file installer to use.</param>
		/// <param name="p_rolActiveMods">The list of active mods.</param>
		/// <param name="p_dicInstallFiles">The list of specific files to install, if null the mod will be installed as usual.</param>
		/// <returns><c>true</c> if the installation was successful;
		/// <c>false</c> otherwise.</returns>
		protected bool RunBasicInstallScript(IModFileInstaller p_mfiFileInstaller, ReadOnlyObservableList<IMod> p_rolActiveMods, List<KeyValuePair<string, string>> p_dicInstallFiles)
		{
			BasicInstallTask bitTask = new BasicInstallTask(Mod, GameMode, p_mfiFileInstaller, PluginManager, VirtualModActivator, EnvironmentInfo.Settings.SkipReadmeFiles, p_rolActiveMods, p_dicInstallFiles);
			OnTaskStarted(bitTask);
			return bitTask.Execute();
		}

		#endregion

		#region Installer Creation

		/// <summary>
		/// Creates the file installer to use to install the mod's files.
		/// </summary>
		/// <remarks>
		/// This returns the regular <see cref="ModFileInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected virtual IModFileInstaller CreateFileInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new ModFileInstaller(GameMode.GameModeEnvironmentInfo, Mod, ModInstallLog, PluginManager, new DataFileUtil(GameMode.GameModeEnvironmentInfo.InstallationPath), p_tfmFileManager, p_dlgOverwriteConfirmationDelegate, GameMode.UsesPlugins);
		}

		/// <summary>
		/// Creates the file installer to use to install the mod's ini edits.
		/// </summary>
		/// <remarks>
		/// This returns the regular <see cref="IniInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected virtual IIniInstaller CreateIniInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return new IniInstaller(Mod, ModInstallLog, VirtualModActivator, p_tfmFileManager, p_dlgOverwriteConfirmationDelegate);
		}

		/// <summary>
		/// Creates the file installer to use to install the mod's game specific value edits.
		/// </summary>
		/// <remarks>
		/// This returns a regular <see cref="IGameSpecificValueInstaller"/>.
		/// </remarks>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		/// <returns>The file installer to use to install the mod's files.</returns>
		protected virtual IGameSpecificValueInstaller CreateGameSpecificValueInstaller(TxFileManager p_tfmFileManager, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return GameMode.GetGameSpecificValueInstaller(Mod, ModInstallLog, p_tfmFileManager, new NexusFileUtil(EnvironmentInfo), p_dlgOverwriteConfirmationDelegate);
		}

		#endregion

		/// <summary>
		/// Checks whether there's a log file for the current scripted installer.
		/// </summary>
		protected bool CheckScriptedModLog()
		{
			string strModFilesPath = Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(Mod.Filename)) + ".xml";
			if ((ProfileManager != null) && !String.IsNullOrWhiteSpace(ProfileManager.IsScriptedLogPresent(Mod.Filename)))
				return true;
			if (Directory.Exists(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted")) && File.Exists(strModFilesPath))
				return true;

			return false;
		}

		/// <summary>
		/// Checks if there's an XML with the list of files to install for the current mod, if present the list of files will be returned.
		/// </summary>
		protected List<KeyValuePair<string, string>> LoadXMLModFilesToInstall()
		{
			string strModFilesPath = String.Empty;
			if (ProfileManager != null)
				strModFilesPath =  ProfileManager.IsScriptedLogPresent(Mod.Filename) ?? Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(Mod.Filename)) + ".xml";
			else
				strModFilesPath = Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted"), Path.GetFileNameWithoutExtension(Mod.Filename)) + ".xml";

			if (File.Exists(strModFilesPath))
			{
				XDocument docScripted = XDocument.Load(strModFilesPath);
				List<KeyValuePair<string, string>> dicFiles = new List<KeyValuePair<string, string>>();

				try
				{
					XElement xelFileList = docScripted.Descendants("FileList").FirstOrDefault();
					if ((xelFileList != null) && xelFileList.HasElements)
					{
						foreach (XElement xelModFile in xelFileList.Elements("File"))
						{
							string strFileFrom = xelModFile.Attribute("FileFrom").Value;
							string strFileTo = xelModFile.Attribute("FileTo").Value;
							if (!String.IsNullOrWhiteSpace(strFileFrom))
								dicFiles.Add(new KeyValuePair<string, string>(strFileFrom, strFileTo));
						}

						if (dicFiles.Count > 0)
							return dicFiles;
					}
				}
				catch (Exception e)
				{
					string prova = e.Message;
					if (String.IsNullOrEmpty(prova))
						if (dicFiles.Count > 0)
							return dicFiles;
				}
			}

			return null;
		}

		/// <summary>
		/// Registers the mod being installed with the install log.
		/// </summary>
		protected virtual void RegisterMod()
		{
			ModInstallLog.AddActiveMod(Mod);
		}
	}
}
