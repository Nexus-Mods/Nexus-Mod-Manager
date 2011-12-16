using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Nexus.Client.ActivityMonitoring;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.InstallationLog.Upgraders;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModRepositories;
using Nexus.Client.ModRepositories.Nexus;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using Nexus.Client.Controls;
using Nexus.Client.ModManagement.UI;

namespace Nexus.Client
{
	/// <summary>
	/// Confirms whether a file system item should be made writable.
	/// </summary>
	/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
	/// <param name="p_strFileSystemItemPath">The path of the file system item to be made writable</param>
	/// <param name="p_booRememberSelection">Whether to remember the selection.</param>
	/// <returns><c>true</c> if the file system item should be made writable;
	/// <c>false</c> otherwise.</returns>
	public delegate bool ConfirmMakeWritableDelegate(IEnvironmentInfo p_eifEnvironmentInfo, string p_strFileSystemItemPath, out bool p_booRememberSelection);

	/// <summary>
	/// The task that initializes the application.
	/// </summary>
	public class ApplicationInitializer : ThreadedBackgroundTask
	{
		#region Events

		/// <summary>
		/// Raised when a task in the set has started.
		/// </summary>
		/// <remarks>
		/// The argument passed with the event args is the task that
		/// has been started.
		/// </remarks>
		public event EventHandler<EventArgs<IBackgroundTask>> TaskStarted = delegate { };

		#endregion

		private AutoResetEvent m_areTaskWait = new AutoResetEvent(false);
		private IGameMode m_gmdGameMode = null;
		private ServiceManager m_svmServiceManager = null;

		#region Properties

		#region Delegates

		/// <summary>
		/// Gets or sets the delegate that logs the user into the repository being used.
		/// </summary>
		/// <value>The delegate that logs the user into the repository being used.</value>
		public Func<LoginFormVM, bool> LoginUser { get; set; }

		/// <summary>
		/// Gets or sets the delegate that is used to get a candidate path for the current
		/// game mode's installation path.
		/// </summary>
		/// <value>The delegate that is used to get a candidate path for the current
		/// game mode's installation path.</value>
		public GetInstallationPathCandidateDelegate GetInstallationPathCandidate { get; set; }

		/// <summary>
		/// Gets or sets the delegate that displays a view.
		/// </summary>
		/// <value>The delegate that displays a view.</value>
		public ShowViewDelegate ShowView { get; set; }

		/// <summary>
		/// Gets or sets the delegate that displays a message.
		/// </summary>
		/// <value>The delegate that displays a message.</value>
		public ShowMessageDelegate ShowMessage { get; set; }

		/// <summary>
		/// Gets or sets the delegate that confirms that an itm should be made writable.
		/// </summary>
		/// <value>The delegate that confirms that an itm should be made writable.</value>
		public ConfirmMakeWritableDelegate ConfirmMakeWritable { get; set; }

		/// <summary>
		/// Gets or sets the delegate that confirms the overwriting of an item.
		/// </summary>
		/// <value>The delegate that confirms the overwriting of an item.</value>
		public ConfirmItemOverwriteDelegate ConfirmItemOverwrite { get; set; }

		#endregion

		/// <summary>
		/// Gets the game mode that was created during the initalization process.
		/// </summary>
		/// <value>The game mode that was created during the initalization process.</value>
		public IGameMode GameMode
		{
			get
			{
				if (m_gmdGameMode == null)
					throw new InvalidOperationException(String.Format("{0} cannot be accessed until the initializaer has completed it's work.", ObjectHelper.GetPropertyName(() => GameMode)));
				return m_gmdGameMode;
			}
			private set
			{
				m_gmdGameMode = value;
			}
		}

		/// <summary>
		/// Gets the service manager that was created during the initalization process.
		/// </summary>
		/// <value>The service manager that was created during the initalization process.</value>
		public ServiceManager Services
		{
			get
			{
				return m_svmServiceManager;
			}
			private set
			{
				m_svmServiceManager = value;
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
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public ApplicationInitializer(EnvironmentInfo p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			LoginUser = delegate { return false; };
			GetInstallationPathCandidate = delegate(IGameModeDescriptor gmd, string pathDefault, out string path) { path = null; return false; };
			ShowMessage = delegate { return DialogResult.Cancel; };
			ConfirmMakeWritable = delegate(IEnvironmentInfo eif, string file, out bool remember) { remember = false; return false; };
			ShowView = delegate { return DialogResult.Cancel; };
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="TaskStarted"/> event.
		/// </summary>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the task that was started.</param>
		protected virtual void OnTaskStarted(EventArgs<IBackgroundTask> e)
		{
			TaskStarted(this, e);
		}

		/// <summary>
		/// Raises the <see cref="TaskStarted"/> event.
		/// </summary>
		/// <param name="p_bgtTask">The task that was started.</param>
		protected void OnTaskStarted(IBackgroundTask p_bgtTask)
		{
			OnTaskStarted(new EventArgs<IBackgroundTask>(p_bgtTask));
		}

		#endregion

		#region Application Initialization

		#region Launch Methods

		/// <summary>
		/// Initializes the application.
		/// </summary>
		/// <param name="p_gmfGameModeFactory">The factory to use to create the game mode for which we will be managing mods.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		public void Initialize(IGameModeFactory p_gmfGameModeFactory, SynchronizationContext p_scxUIContext)
		{
			Start(p_gmfGameModeFactory, p_scxUIContext);
		}

		/// <summary>
		/// The method that is called to start the backgound task.
		/// </summary>
		/// <param name="p_objArgs">Arguments to for the task execution.</param>
		/// <returns>A return value.</returns>
		/// <seealso cref="DoApplicationInitialize(IGameModeFactory, SynchronizationContext, out ViewMessage)"/>
		protected override object DoWork(object[] p_objArgs)
		{
			IGameModeFactory gmfGameModeFactory = (IGameModeFactory)p_objArgs[0];
			SynchronizationContext scxUIContext = (SynchronizationContext)p_objArgs[1];
			ViewMessage vwmErrorMessage = null;
			OverallProgress = 0;
			OverallProgressMaximum = 12;
			OverallProgressStepSize = 1;
			if (!DoApplicationInitialize(gmfGameModeFactory, scxUIContext, out vwmErrorMessage) && (Status != TaskStatus.Error))
				Status = TaskStatus.Incomplete;
			StepOverallProgress();
			return vwmErrorMessage;
		}

		#endregion

		/// <summary>
		/// Performs the applicatio initialization.
		/// </summary>
		/// <param name="p_gmfGameModeFactory">The factory to use to create the game mode for which we will be managing mods.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_vwmErrorMessage">The error message if the initialization failed.</param>
		/// <returns><c>true</c> if the initialization was successful;
		/// <c>false</c> otherwise.</returns>
		protected bool DoApplicationInitialize(IGameModeFactory p_gmfGameModeFactory, SynchronizationContext p_scxUIContext, out ViewMessage p_vwmErrorMessage)
		{
			InstallationPathInitializer ipiInstallPathInitializer = new InstallationPathInitializer(EnvironmentInfo, GetInstallationPathCandidate);
			if (!ipiInstallPathInitializer.InitializeInstallationPath(p_gmfGameModeFactory))
			{
				p_vwmErrorMessage = new ViewMessage(String.Format("Unable to locate {0}'s Installation Path.", p_gmfGameModeFactory.GameModeDescriptor.Name), null, "Error", MessageBoxIcon.Error);
				return false;
			}
			StepOverallProgress();

			string strUacCheckPath = EnvironmentInfo.Settings.InstallationPaths[p_gmfGameModeFactory.GameModeDescriptor.ModeId];
			if (!UacCheck(strUacCheckPath))
			{
				Trace.TraceError("Unable to get write permissions for: " + strUacCheckPath);
				string strMessage = "Unable to get write permissions for:" + Environment.NewLine + strUacCheckPath;
				string strDetails = String.Format("This error happens when you are running Windows Vista or later,  and have installed <b>{0}</b> in the <b>Program Files</b> folder. You need to do one of the following:<ol>" +
									"<li>Disable UAC (<i>not recommended</i>).</li>" +
									@"<li>Move <b>{0}</b> outside of the <b>Program Files</b> folder (for example, to <b>C:\Games\{0}</b>). This may require a reinstall.<br>With Oblivion you could just copy the game folder to a new location, run the game, and all would be well. This may not work with other games.</li>" +
									"<li>Run <b>{1}</b> as administrator. You can try this by right-clicking on <b>{1}</b> shortcut and selecting <i>Run as administrator</i>. Alternatively, right-click on the shortcut, select <i>Properties->Compatibility</i> and check <i>Run this program as an administrator</i>." +
									"</ol>" +
									"The best thing to do in order to avoid other problems, and the generally recommended solution, is to install <b>{0}</b> outside of the <b>Program Files</b> folder.",
									p_gmfGameModeFactory.GameModeDescriptor.Name, EnvironmentInfo.Settings.ModManagerName);
				p_vwmErrorMessage = new ViewMessage(strMessage, strDetails, "Error", MessageBoxIcon.Error);
				return false;
			}
			StepOverallProgress();

			if (!EnvironmentInfo.Settings.CompletedSetup.ContainsKey(p_gmfGameModeFactory.GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.CompletedSetup[p_gmfGameModeFactory.GameModeDescriptor.ModeId])
			{
				if (!p_gmfGameModeFactory.PerformInitialSetup(ShowView, ShowMessage))
				{
					p_vwmErrorMessage = null;
					return false;
				}
				EnvironmentInfo.Settings.CompletedSetup[p_gmfGameModeFactory.GameModeDescriptor.ModeId] = true;
				EnvironmentInfo.Settings.Save();
			}
			StepOverallProgress();

			if (!p_gmfGameModeFactory.PerformInitialization(ShowView, ShowMessage))
			{
				p_vwmErrorMessage = null;
				return false;
			}
			StepOverallProgress();

			ViewMessage vwmWarning = null;
			IGameMode gmdGameMode = p_gmfGameModeFactory.BuildGameMode(out vwmWarning);
			if (gmdGameMode == null)
			{
				p_vwmErrorMessage = new ViewMessage(String.Format("Could not initialize {0} Game Mode.", p_gmfGameModeFactory.GameModeDescriptor.Name), null, "Error", MessageBoxIcon.Error);
				return false;
			}
			if (vwmWarning != null)
				ShowMessage(vwmWarning);
			StepOverallProgress();

			if (!UacCheckEnvironment(gmdGameMode, out p_vwmErrorMessage))
			{
				//TODO it would be really nice of us if we, instead of closing,
				// force the game mode to reinitialize, and select new paths
				return false;
			}
			StepOverallProgress();

			if (!CreateEnvironmentPaths(gmdGameMode, out p_vwmErrorMessage))
				return false;
			StepOverallProgress();

			Trace.TraceInformation(String.Format("Game Mode Built: {0} ({1})", gmdGameMode.Name, gmdGameMode.ModeId));
			Trace.Indent();
			Trace.TraceInformation(String.Format("Installation Path: {0}", gmdGameMode.GameModeEnvironmentInfo.InstallationPath));
			Trace.TraceInformation(String.Format("Install Info Path: {0}", gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory));
			Trace.TraceInformation(String.Format("Mod Path: {0}", gmdGameMode.GameModeEnvironmentInfo.ModDirectory));
			Trace.TraceInformation(String.Format("Mod Cache Path: {0}", gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory));
			Trace.TraceInformation(String.Format("Mod Download Cache: {0}", gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory));
			Trace.TraceInformation(String.Format("Overwrite Path: {0}", gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory));
			Trace.Unindent();

			ScanForReadonlyFiles(gmdGameMode);
			StepOverallProgress();

			Trace.TraceInformation("Initializing Mod Repository...");
			Trace.Indent();
			IModRepository mrpModRepository = NexusModRepository.GetRepository(gmdGameMode);
			Trace.Unindent();
			StepOverallProgress();

			if (!Login(gmdGameMode, mrpModRepository))
			{
				p_vwmErrorMessage = null;
				Status = TaskStatus.Error;
				return false;
			}
			StepOverallProgress();

			ServiceManager svmServices = InitializeServices(gmdGameMode, mrpModRepository, p_scxUIContext, out p_vwmErrorMessage);
			if (svmServices == null)
			{
				p_vwmErrorMessage = p_vwmErrorMessage ?? new ViewMessage("Unable to initialize services.", null, "Error", MessageBoxIcon.Error);
				return false;
			}
			StepOverallProgress();

			UpgradeMismatchedVersionMods(svmServices.ModInstallLog, svmServices.ModManager);
			StepOverallProgress();

			if (!UninstallMissingMods(gmdGameMode, EnvironmentInfo, svmServices.ModManager))
			{
				p_vwmErrorMessage = null;
				return false;
			}
			StepOverallProgress();

			svmServices.ModManager.LoadQueuedMods();
			StepOverallProgress();

			GameMode = gmdGameMode;
			Services = svmServices;

			p_vwmErrorMessage = null;
			return true;
		}

		#endregion

		#region Support

		/// <summary>
		/// Ensures that the game mode environment's paths exist.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode whose paths are to be created.</param>
		/// <param name="p_vwmErrorMessage">The error message if the creation fails.</param>
		/// <returns><c>true</c> if the creation passed;
		/// <c>false</c> otherwise.</returns>
		protected bool CreateEnvironmentPaths(IGameMode p_gmdGameMode, out ViewMessage p_vwmErrorMessage)
		{
			string[] strPaths = new string[] { p_gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory,
												p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory,
												p_gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory,
												p_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory,
												p_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory};
			foreach (string strPath in strPaths)
				if (!Directory.Exists(strPath))
					Directory.CreateDirectory(strPath);
			p_vwmErrorMessage = null;
			return true;
		}

		/// <summary>
		/// Checks to see if UAC is interfering with file installation.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode whose paths are to be checked.</param>
		/// <param name="p_vwmErrorMessage">The error message if the initialization failed.</param>
		/// <returns><c>true</c> if the check passed;
		/// <c>false</c> otherwise.</returns>
		protected bool UacCheckEnvironment(IGameMode p_gmdGameMode, out ViewMessage p_vwmErrorMessage)
		{
			Dictionary<string, string> dicPaths = new Dictionary<string, string>();
			dicPaths[p_gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory] = "Install Info";
			if (!dicPaths.ContainsKey(p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory))
				dicPaths[p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory] = "Mods";
			if (!dicPaths.ContainsKey(p_gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory))
				dicPaths[p_gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory] = "Mods";
			if (!dicPaths.ContainsKey(p_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory))
				dicPaths[p_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory] = "Mods";
			if (!dicPaths.ContainsKey(p_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory))
				dicPaths[p_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory] = "Install Info";

			foreach (KeyValuePair<string, string> kvpUacCheckPath in dicPaths)
			{
				if (!UacCheck(kvpUacCheckPath.Key))
				{
					Trace.TraceError("Unable to get write permissions for: " + kvpUacCheckPath.Key);
					string strMessage = "Unable to get write permissions for:" + Environment.NewLine + kvpUacCheckPath.Key;
					string strDetails = String.Format("This error happens when you are running Windows Vista or later, and have put {0}'s <b>{1}</b> folder in the <b>Program Files</b> folder. You need to do one of the following:<ol>" +
										"<li>Disable UAC (<i>not recommended</i>).</li>" +
										@"<li>Move {0}'s <b>{1}</b> folder outside of the <b>Program Files</b> folder (for example, to <b>C:\Games\ModManagerInfo\{1}</b>).</li>" +
										"<li>Run <b>{0}</b> as administrator. You can try this by right-clicking on <b>{0}</b>'s shortcut and selecting <i>Run as administrator</i>. Alternatively, right-click on the shortcut, select <i>Properties->Compatibility</i> and check <i>Run this program as an administrator</i>." +
										"</ol>" +
										"The best thing to do in order to avoid other problems, and the generally recommended solution, is to Move {0}'s <b>{1}</b> folder outside of the <b>Program Files</b> folder.",
										EnvironmentInfo.Settings.ModManagerName, kvpUacCheckPath.Value);
					p_vwmErrorMessage = new ViewMessage(strMessage, strDetails, "Error", MessageBoxIcon.Error);
					return false;
				}
			}
			p_vwmErrorMessage = null;
			return true;
		}

		/// <summary>
		/// Checks to see if UAC is interfering with file installation.
		/// </summary>
		/// <param name="p_strPath">The path for which we are to check if UAC is intefeing.</param>
		/// <returns><c>true</c> if the check passed;
		/// <c>false</c> otherwise.</returns>
		protected bool UacCheck(string p_strPath)
		{
			string strInstallationPath = p_strPath;
			while (!Directory.Exists(strInstallationPath))
				strInstallationPath = Path.GetDirectoryName(strInstallationPath);
			string strTestFile = Path.Combine(strInstallationPath, "limited");
			try
			{
				string strVirtualStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualStore\\");
				strVirtualStore = Path.Combine(strVirtualStore, strInstallationPath.Remove(0, 3));
				strVirtualStore = Path.Combine(strVirtualStore, "limited");
				if (File.Exists(strVirtualStore))
					File.Delete(strVirtualStore);
				using (FileStream fs = File.Create(strTestFile)) { }
				if (File.Exists(strVirtualStore))
				{
					Trace.TraceError(String.Format("UAC is messing us up: {0}", p_strPath));
					return false;
				}
			}
			catch
			{
				Trace.TraceError(String.Format("UAC is messing us up: {0}", p_strPath));
				return false;
			}
			finally
			{
				if (File.Exists(strTestFile))
					File.Delete(strTestFile);
			}
			return true;
		}

		/// <summary>
		/// Logins the user into the current mod repository.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_mrpModRepository">The mod repository to use to retrieve mods and mod metadata.</param>
		/// <returns><c>true</c> if the user was successfully logged in;
		/// <c>false</c> otherwise</returns>
		protected bool Login(IGameMode p_gmdGameMode, IModRepository p_mrpModRepository)
		{
			if (EnvironmentInfo.Settings.RepositoryAuthenticationTokens[p_mrpModRepository.Id] == null)
				EnvironmentInfo.Settings.RepositoryAuthenticationTokens[p_mrpModRepository.Id] = new KeyedSettings<string>();

			Dictionary<string, string> dicAuthTokens = new Dictionary<string, string>(EnvironmentInfo.Settings.RepositoryAuthenticationTokens[p_mrpModRepository.Id]);
			if ((dicAuthTokens.Count == 0) || !p_mrpModRepository.Login(dicAuthTokens))
			{
				string strMessage = String.Format("You must log into the {0} website.", p_mrpModRepository.Name);
				string strCancelWarning = String.Format("If you do not login {0} will close.", EnvironmentInfo.Settings.ModManagerName);
				LoginFormVM vmlLoginVM = new LoginFormVM(EnvironmentInfo, p_mrpModRepository, p_gmdGameMode.ModeTheme, strMessage, strCancelWarning);
				return LoginUser(vmlLoginVM);
			}
			return true;
		}

		/// <summary>
		/// This initializes the services required to run the client.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode for which mods are being managed.</param>
		/// <param name="p_mrpModRepository">The mod repository to use to retrieve mods and mod metadata.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <param name="p_vwmErrorMessage">The error message if the UAC check failed.</param>
		/// <returns>A <see cref="ServiceManager"/> containing the initialized services, or <c>null</c> if the
		/// services didn't initialize properly.</returns>
		protected ServiceManager InitializeServices(IGameMode p_gmdGameMode, IModRepository p_mrpModRepository, SynchronizationContext p_scxUIContext, out ViewMessage p_vwmErrorMessage)
		{
			NexusFileUtil nfuFileUtility = new NexusFileUtil(EnvironmentInfo);
			IModCacheManager mcmModCacheManager = new NexusModCacheManager(p_gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory, p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory, nfuFileUtility);

			Trace.TraceInformation("Registering supported Script Types...");
			Trace.Indent();
			IScriptTypeRegistry stgScriptTypeRegistry = ScriptTypeRegistry.DiscoverScriptTypes(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ScriptTypes"), p_gmdGameMode);
			if (stgScriptTypeRegistry.Types.Count == 0)
			{
				p_vwmErrorMessage = new ViewMessage("No script types were found.", null, "No Script Types", MessageBoxIcon.Error);
				return null;
			}
			Trace.TraceInformation("Found {0} script types.", stgScriptTypeRegistry.Types.Count);
			Trace.Unindent();

			Trace.TraceInformation("Registering supported mod formats...");
			Trace.Indent();
			IModFormatRegistry mfrModFormatRegistry = ModFormatRegistry.DiscoverFormats(mcmModCacheManager, stgScriptTypeRegistry, Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ModFormats"));
			if (mfrModFormatRegistry.Formats.Count == 0)
			{
				p_vwmErrorMessage = new ViewMessage("No mod formats were found.", null, "No Mod Formats", MessageBoxIcon.Error);
				return null;
			}
			Trace.TraceInformation("Found {0} formats.", mfrModFormatRegistry.Formats.Count);
			Trace.Unindent();

			Trace.TraceInformation("Finding managed mods...");
			Trace.Indent();
			ModRegistry mrgModRegistry = ModRegistry.DiscoverManagedMods(mfrModFormatRegistry, p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory, EnvironmentInfo.Settings.ScanSubfoldersForMods, p_gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory, p_gmdGameMode.GameModeEnvironmentInfo.ModDownloadCacheDirectory);
			Trace.TraceInformation("Found {0} managed mods.", mrgModRegistry.RegisteredMods.Count);
			Trace.Unindent();

			Trace.TraceInformation("Initializing Install Log...");
			Trace.Indent();
			Trace.TraceInformation("Checking if upgrade is required...");
			InstallLogUpgrader iluUgrader = new InstallLogUpgrader();
			string strLogPath = Path.Combine(p_gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "InstallLog.xml");
			if (iluUgrader.NeedsUpgrade(strLogPath))
			{
				if (!iluUgrader.CanUpgrade(strLogPath))
				{
					p_vwmErrorMessage = new ViewMessage(String.Format("{0} does not support version {1} of the Install Log.", EnvironmentInfo.Settings.ModManagerName, InstallLog.ReadVersion(strLogPath)), null, "Install Log", MessageBoxIcon.Error);
					return null;
				}
				IBackgroundTask tskUpgrader = iluUgrader.UpgradeInstallLog(strLogPath, p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory, mrgModRegistry);
				m_areTaskWait.Reset();
				tskUpgrader.TaskEnded += new EventHandler<TaskEndedEventArgs>(Task_TaskEnded);
				OnTaskStarted(tskUpgrader);
				if (tskUpgrader.IsActive)
					m_areTaskWait.WaitOne();
				tskUpgrader.TaskEnded -= new EventHandler<TaskEndedEventArgs>(Task_TaskEnded);
				if (tskUpgrader.Status != TaskStatus.Complete)
				{
					string strDetails = (string)(tskUpgrader.ReturnValue ?? null);
					p_vwmErrorMessage = new ViewMessage("Install Log was not upgraded.", strDetails, "Install Log", MessageBoxIcon.Error);
					return null;
				}
			}
			IInstallLog ilgInstallLog = InstallLog.Initialize(mrgModRegistry, p_gmdGameMode.GameModeEnvironmentInfo.ModDirectory, strLogPath);
			Trace.Unindent();

			Trace.TraceInformation("Initializing Plugin Registry...");
			Trace.Indent();
			PluginRegistry prgPluginRegistry = PluginRegistry.DiscoverManagedPlugins(p_gmdGameMode.GetPluginFactory(), p_gmdGameMode.GetPluginDiscoverer());
			Trace.TraceInformation("Found {0} managed plugins.", prgPluginRegistry.RegisteredPlugins.Count);
			Trace.Unindent();

			Trace.TraceInformation("Initializing Active Plugin Log...");
			Trace.Indent();
			ActivePluginLog aplPluginLog = ActivePluginLog.Initialize(prgPluginRegistry, p_gmdGameMode.GetActivePluginLogSerializer());
			Trace.Unindent();

			Trace.TraceInformation("Initializing Plugin Order Log...");
			Trace.Indent();
			PluginOrderLog polPluginOrderLog = PluginOrderLog.Initialize(prgPluginRegistry, p_gmdGameMode.GetPluginOrderLogSerializer(), p_gmdGameMode.GetPluginOrderValidator());
			Trace.Unindent();

			Trace.TraceInformation("Initializing Plugin Manager...");
			Trace.Indent();
			IPluginManager pmgPluginManager = PluginManager.Initialize(p_gmdGameMode.GameModeEnvironmentInfo, prgPluginRegistry, aplPluginLog, polPluginOrderLog, p_gmdGameMode.GetPluginOrderValidator());
			Trace.Unindent();

			Trace.TraceInformation("Initializing Activity Monitor...");
			Trace.Indent();
			ActivityMonitor amtMonitor = new ActivityMonitor();
			Trace.Unindent();

			Trace.TraceInformation("Initializing Mod Manager...");
			Trace.Indent();
			ModManager mmgModManager = ModManager.Initialize(p_gmdGameMode, EnvironmentInfo, p_mrpModRepository, amtMonitor, mfrModFormatRegistry, mrgModRegistry, nfuFileUtility, p_scxUIContext, ilgInstallLog, pmgPluginManager);
			Trace.Unindent();

			Trace.TraceInformation("Initializing Update Manager...");
			Trace.Indent();
			UpdateManager umgUpdateManager = new UpdateManager(p_gmdGameMode, EnvironmentInfo);
			Trace.Unindent();

			p_vwmErrorMessage = null;
			return new ServiceManager(ilgInstallLog, aplPluginLog, polPluginOrderLog, mmgModManager, pmgPluginManager, amtMonitor, umgUpdateManager);
		}

		/// <summary>
		/// This chaecks for any files that are readonly.
		/// </summary>
		protected void ScanForReadonlyFiles(IGameMode p_gmdGameMode)
		{
			Trace.TraceInformation("Scanning for read-only files...");
			Trace.Indent();
			List<string> lstFiles = new List<string>();
			foreach (string strPath in p_gmdGameMode.WritablePaths)
				if (File.Exists(strPath))
					lstFiles.Add(strPath);
			foreach (string strFile in lstFiles)
			{
				FileInfo fifPlugin = new FileInfo(strFile);
				if (fifPlugin.Exists && ((fifPlugin.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly))
				{
					Trace.TraceInformation(String.Format("Found {0}", strFile));
					Trace.Indent();
					bool booAsk = (bool)EnvironmentInfo.Settings.CustomGameModeSettings[p_gmdGameMode.ModeId]["AskAboutReadOnlySettingsFiles"];
					bool booMakeWritable = (bool)EnvironmentInfo.Settings.CustomGameModeSettings[p_gmdGameMode.ModeId]["UnReadOnlySettingsFiles"];
					bool booRemember = false;
					if (booAsk)
						booMakeWritable = ConfirmMakeWritable(EnvironmentInfo, fifPlugin.Name, out booRemember);
					if (booMakeWritable)
					{
						Trace.TraceInformation("Made writable");
						fifPlugin.Attributes &= ~FileAttributes.ReadOnly;
					}
					else
						Trace.TraceInformation("NOT made writable");
					if (booRemember)
					{
						EnvironmentInfo.Settings.CustomGameModeSettings[p_gmdGameMode.ModeId]["AskAboutReadOnlySettingsFiles"] = false;
						EnvironmentInfo.Settings.CustomGameModeSettings[p_gmdGameMode.ModeId]["UnReadOnlySettingsFiles"] = booMakeWritable;
						EnvironmentInfo.Settings.Save();
					}
					Trace.Unindent();
				}
			}
			Trace.Unindent();
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of tasks run during initialization.
		/// </summary>
		/// <remarks>
		/// This signals the wait handle that is blocking the initialization thread, allowing it to continue
		/// with the initialization.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskEndedEventArgs"/> describing the event arguments.</param>
		private void Task_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			m_areTaskWait.Set();
		}

		#endregion

		#region Mod Cleanup

		#region Mod Upgrading

		/// <summary>
		/// Upgrade mods that have been manually replaced since the last time the mod
		/// manager ran.
		/// </summary>
		/// <param name="p_ilgInstallLog">The install log to use to log file installations.</param>
		/// <param name="p_mmgModManager">The mod manager to use to upgrade any replaced mods.</param>
		protected void UpgradeMismatchedVersionMods(IInstallLog p_ilgInstallLog, ModManager p_mmgModManager)
		{
			UpgradeMismatchedVersionScanner uvsScanner = new UpgradeMismatchedVersionScanner(p_ilgInstallLog, p_mmgModManager, ConfirmMismatchedVersionModUpgrade, ConfirmItemOverwrite);
			uvsScanner.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(TaskSet_TaskStarted);
			uvsScanner.Scan();
			WaitForSet(uvsScanner, false);
			uvsScanner.TaskStarted -= new EventHandler<EventArgs<IBackgroundTask>>(TaskSet_TaskStarted);
		}

		/// <summary>
		/// Asks the user if the given mod whose version does not its version in the install log
		/// should be upgraded.
		/// </summary>
		/// <remarks>
		/// If the user opts to not upgrade the mod, the verison is changed in the install log, but no
		/// other action is taken.
		/// </remarks>
		/// <param name="p_modOld">The mod info in the install log.</param>
		/// <param name="p_modNew">The mod with the mismatched version.</param>
		/// <returns><c>true</c> if the mod should be upgraded;
		/// <c>false</c> otherwise.</returns>
		private bool ConfirmMismatchedVersionModUpgrade(IMod p_modOld, IMod p_modNew)
		{
			string strUpgradeMessage = "A different version of {0} has been detected. The installed version is {1}, the new version is {2}. Would you like to upgrade?" + Environment.NewLine + "Selecting No will replace the mod in the mod list, but won't change any files.";
			switch ((DialogResult)ShowMessage(new ViewMessage(String.Format(strUpgradeMessage, p_modNew.ModName, p_modOld.HumanReadableVersion, p_modNew.HumanReadableVersion), null, "Upgrade", ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No, MessageBoxIcon.Question)))
			{
				case DialogResult.Yes:
					return true;
				case DialogResult.No:
					return false;
				default:
					throw new Exception("Unexpected value for mismatched version mod upgrade.");
			}
		}

		#endregion

		#region Task Set Handling

		/// <summary>
		/// Waits for the task set to complete, and notifies listeners of any task started by the set.
		/// </summary>
		/// <param name="p_btsTaskSet">The task set for which to wait.</param>
		/// <param name="p_booHookTaskStarted">Whether or not to attach a listener to the <see cref="IBackgroundTaskSet.TaskStarted"/> event.</param>
		protected void WaitForSet(IBackgroundTaskSet p_btsTaskSet, bool p_booHookTaskStarted)
		{
			if (p_booHookTaskStarted)
				p_btsTaskSet.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(TaskSet_TaskStarted);
			p_btsTaskSet.TaskSetCompleted += new EventHandler<TaskSetCompletedEventArgs>(TaskSet_TaskSetCompleted);
			m_areTaskWait.Reset();
			if (!p_btsTaskSet.IsCompleted)
				m_areTaskWait.WaitOne();
			p_btsTaskSet.TaskSetCompleted -= new EventHandler<TaskSetCompletedEventArgs>(TaskSet_TaskSetCompleted);
			if (p_booHookTaskStarted)
				p_btsTaskSet.TaskStarted -= new EventHandler<EventArgs<IBackgroundTask>>(TaskSet_TaskStarted);
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskStarted"/> event of a task set.
		/// </summary>
		/// <remarks>
		/// This lets listeners know a task has started.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void TaskSet_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			OnTaskStarted(e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTaskSet.TaskSetCompleted"/> event of a task set.
		/// </summary>
		/// <remarks>
		/// This signals the wait handle that is blocking the initialization thread, allowing it to continue
		/// with the initialization.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void TaskSet_TaskSetCompleted(object sender, TaskSetCompletedEventArgs e)
		{
			m_areTaskWait.Set();
		}

		#endregion

		/// <summary>
		/// Uninstalls mods that have been manually removed since the last time the mod manager
		/// ran.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_mmgModManager">The mod manager to use to uninstall any missing mods.</param>
		protected bool UninstallMissingMods(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, ModManager p_mmgModManager)
		{
			Trace.TraceInformation("Uninstalling missing Mods...");
			Trace.Indent();
			foreach (IMod modMissing in new List<IMod>(p_mmgModManager.ActiveMods))
			{
				if (!File.Exists(modMissing.Filename))
				{
					Trace.TraceInformation("{0} is missing...", modMissing.Filename);
					Trace.Indent();

					//look for another version of the mod
					List<IMod> lstInactiveMods = new List<IMod>();
					foreach (IMod modRegistered in p_mmgModManager.ManagedMods)
						if (!p_mmgModManager.ActiveMods.Contains(modRegistered))
							lstInactiveMods.Add(modRegistered);
					ModMatcher mmcMatcher = new ModMatcher(lstInactiveMods, false);
					IMod modNewVersion = mmcMatcher.FindAlternateVersion(modMissing, true);
					if (modNewVersion != null)
					{
						Trace.TraceInformation("Found alternate version...");
						string strUpgradeMessage = String.Format("'{0}' was deleted without being deactivated. " + Environment.NewLine +
										"However, a different version has been detected. The installed, missing, version is {1}; the new version is {2}." + Environment.NewLine +
										"You can either upgrade the mod or uninstall it. If you Cancel, {3} will close and you will " +
										"have to put the Mod ({4}) back in the mods folder." + Environment.NewLine +
										"Would you like to upgrade the mod?", modMissing.ModName, modMissing.HumanReadableVersion, modNewVersion.HumanReadableVersion, p_eifEnvironmentInfo.Settings.ModManagerName, modMissing.Filename);

						switch ((DialogResult)ShowMessage(new ViewMessage(strUpgradeMessage, "Missing Mod", ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No | ExtendedMessageBoxButtons.Cancel, MessageBoxIcon.Warning)))
						{
							case DialogResult.Yes:
								Trace.TraceInformation("Upgrading.");
								IBackgroundTaskSet btsUpgrader = p_mmgModManager.ForceUpgrade(modMissing, modNewVersion, ConfirmItemOverwrite);
								WaitForSet(btsUpgrader, true);
								Trace.Unindent();
								continue;
							case DialogResult.Cancel:
								Trace.TraceInformation("Aborting.");
								Trace.Unindent();
								Trace.Unindent();
								return false;
							case DialogResult.No:
								break;
							default:
								throw new Exception(String.Format("Unexpected value for cofnirmation of upgrading missing mod {0}.", modMissing.ModName));
						}
					}

					string strMessage = String.Format("'{0}' was deleted without being deactivated. " + Environment.NewLine +
										"If you don't uninstall the Mod, {1} will close and you will " +
										"have to put the Mod back in the mods folder." + Environment.NewLine +
										"Would you like to uninstall the missing Mod?", modMissing.Filename, p_eifEnvironmentInfo.Settings.ModManagerName);
					if ((DialogResult)ShowMessage(new ViewMessage(strMessage, "Missing Mod", ExtendedMessageBoxButtons.Yes | ExtendedMessageBoxButtons.No, MessageBoxIcon.Warning)) == DialogResult.No)
					{
						Trace.TraceInformation("Aborting.");
						Trace.Unindent();
						Trace.Unindent();
						return false;
					}
					Trace.TraceInformation("Removing.");
					IBackgroundTaskSet btsDeactivator = p_mmgModManager.DeactivateMod(modMissing);
					WaitForSet(btsDeactivator, true);

					Trace.TraceInformation("Uninstalled.");
					Trace.Unindent();
				}
			}
			Trace.Unindent();
			return true;
		}

		#endregion
	}
}
