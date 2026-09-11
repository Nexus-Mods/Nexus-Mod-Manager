using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Permissions;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting.Operations;
using Nexus.Client.Mods;
using Nexus.Client.Plugins;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement.Scripting
{
	/// <summary>
	/// Implements the functions availabe to scripts.
	/// </summary>
	/// <remarks>
	/// The proxy allows sandboxed scripts to call functions that can perform
	/// actions outside of the sandbox.
	/// </remarks>
	public class ScriptFunctionProxy : MarshalByRefObject
	{
		private readonly IScriptedFileSelectionCache m_sfcFileSelectionCache;
		private readonly HashSet<string> m_hstPlannedStagingWrites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<string> m_hstProjectedActiveLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private HashSet<string> m_hstModFiles;
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

		#region Properties

		/// <summary>
		/// Gets the mod for which the script is running.
		/// </summary>
		/// <value>The mod for which the script is running.</value>
		protected IMod Mod { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the installer group to use to install mod items.
		/// </summary>
		/// <value>The installer group to use to install mod items.</value>
		protected InstallerGroup Installers { get; private set; }

		/// <summary>
		/// Gets the virtual mod activator to use.
		/// </summary>
		/// <value>The virtual mod activator to use.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the mod link installer to use.
		/// </summary>
		/// <value>The mod link installer to use.</value>
		protected IModLinkInstaller ModLinkInstaller { get; private set; }

		/// <summary>
		/// Gets the scripted installation session used to record and execute logical installation operations.
		/// </summary>
		/// <value>The current scripted installation session.</value>
		protected ScriptedInstallationSession InstallationSession { get; private set; }

		/// <summary>
		/// Gets the manager to use to display UI elements.
		/// </summary>
		/// <value>The manager to use to display UI elements.</value>
		protected UIUtil UIManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod for which the script is running.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_uipUIProxy">The UI manager to use to interact with UI elements.</param>
		public ScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, UIUtil p_uipUIProxy)
		{
			Mod = p_modMod;
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			Installers = p_igpInstallers;
			UIManager = p_uipUIProxy;
			VirtualModActivator = p_ivaVirtualModActivator;
			ModLinkInstaller = VirtualModActivator.GetModLinkInstaller();
			m_sfcFileSelectionCache = new ScriptedFileSelectionCache(Mod, GameMode);
			ConfigureInstallationSession(ScriptedInstallationSessionMode.Immediate, null);
		}

		#endregion

		#region Installation Session Configuration

		/// <summary>
		/// Attempts to switch the current script proxy to deferred installation planning with projected-state support.
		/// </summary>
		/// <returns><c>true</c> when all required decision-separation capabilities are available and deferred planning is enabled; otherwise, <c>false</c>.</returns>
		protected bool TryEnableDeferredInstallation()
		{
			if (!(Installers.FileInstaller is IModFileInstallDecisionSupport) ||
				!(ModLinkInstaller is IModLinkInstallDecisionSupport) ||
				!(Installers.IniInstaller is IIniEditDecisionSupport) ||
				((Installers.GameSpecificValueInstaller != null) && !(Installers.GameSpecificValueInstaller is IGameSpecificValueInstallDecisionSupport)))
				return false;

			m_hstPlannedStagingWrites.Clear();
			m_hstProjectedActiveLinks.Clear();
			ScriptedInstallationProjectedState spsProjectedState = new ScriptedInstallationProjectedState(Mod, GameMode, Installers);
			ConfigureInstallationSession(ScriptedInstallationSessionMode.Deferred, spsProjectedState);
			return true;
		}

		/// <summary>
		/// Executes all operations that remain pending in the current scripted installation session.
		/// </summary>
		/// <returns><c>true</c> when every pending operation completes successfully; otherwise, <c>false</c>.</returns>
		protected bool ExecutePendingInstallationOperations()
		{
			return ExecuteWithFullTrust(() => InstallationSession.ExecutePendingOperations());
		}

		/// <summary>
		/// Configures the proxy to execute every submitted operation synchronously for legacy scripts that can observe the filesystem directly.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when deferred operations are still pending.</exception>
		protected void UseImmediateCompatibilityInstallation()
		{
			if (InstallationSession.HasPendingOperations)
				throw new InvalidOperationException("Deferred scripted installation operations must be committed before enabling immediate compatibility execution.");

			m_hstPlannedStagingWrites.Clear();
			m_hstProjectedActiveLinks.Clear();
			ConfigureInstallationSession(ScriptedInstallationSessionMode.ImmediateCompatibility, null);
		}

		/// <summary>
		/// Switches the current proxy back to immediate execution after all deferred operations have been committed.
		/// </summary>
		/// <exception cref="InvalidOperationException">Thrown when deferred operations are still pending.</exception>
		protected void SwitchToImmediateInstallation()
		{
			if (InstallationSession.HasPendingOperations)
				throw new InvalidOperationException("Deferred scripted installation operations must be committed before switching to immediate execution.");

			m_hstPlannedStagingWrites.Clear();
			m_hstProjectedActiveLinks.Clear();
			ConfigureInstallationSession(ScriptedInstallationSessionMode.Immediate, null);
		}

		/// <summary>
		/// Gets whether the current proxy is collecting installation operations for deferred execution.
		/// </summary>
		protected bool IsDeferredInstallationEnabled
		{
			get { return InstallationSession.Mode == ScriptedInstallationSessionMode.Deferred; }
		}

		/// <summary>
		/// Recreates the scripted installation session with the requested execution mode and projected-state view.
		/// </summary>
		/// <param name="p_simMode">The requested execution mode.</param>
		/// <param name="p_spsProjectedState">The projected-state overlay used while planning, or <c>null</c> for immediate execution.</param>
		private void ConfigureInstallationSession(ScriptedInstallationSessionMode p_simMode, ScriptedInstallationProjectedState p_spsProjectedState)
		{
			ImmediateScriptedInstallOperationExecutor sioExecutor = new ImmediateScriptedInstallOperationExecutor(Mod, GameMode, EnvironmentInfo, VirtualModActivator, ModLinkInstaller, Installers, OnTaskStarted, m_sfcFileSelectionCache);
			InstallationSession = new ScriptedInstallationSession(sioExecutor, p_simMode, p_spsProjectedState);
		}

		#endregion

		#region Security

		/// <summary>
		/// Executes a trusted NMM operation without propagating the sandbox caller's restricted permission set into host code.
		/// </summary>
		/// <typeparam name="T">The operation result type.</typeparam>
		/// <param name="p_fncOperation">The trusted operation to execute.</param>
		/// <returns>The result returned by the operation.</returns>
		private static T ExecuteWithFullTrust<T>(Func<T> p_fncOperation)
		{
			if (p_fncOperation == null)
				throw new ArgumentNullException(nameof(p_fncOperation));

			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				return p_fncOperation();
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
		}

		/// <summary>
		/// Executes a trusted NMM operation without propagating the sandbox caller's restricted permission set into host code.
		/// </summary>
		/// <param name="p_actOperation">The trusted operation to execute.</param>
		private static void ExecuteWithFullTrust(Action p_actOperation)
		{
			if (p_actOperation == null)
				throw new ArgumentNullException(nameof(p_actOperation));

			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				p_actOperation();
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
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

		#region Installation

		/// <summary>
		/// Performs a basic install of the mod.
		/// </summary>
		/// <remarks>
		/// A basic install installs all of the file in the mod to the Data directory
		/// or activates all esp and esm files.
		/// </remarks>
		/// <returns><c>true</c> if the installation succeed;
		/// <c>false</c> otherwise.</returns>
		public bool PerformBasicInstall()
		{
			return ExecuteWithFullTrust(() => InstallationSession.Submit(new PerformBasicInstallOperation()));
		}

		#endregion

		#region File Management

		/// <summary>
		/// Installs the files in the specified folder from the mod to the file system.
		/// </summary>
		/// <param name="p_strFrom">The path of the folder in the mod containing the files to install.</param>
		/// <param name="p_booRecurse">Whether to install all files in all subfolders.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public bool InstallFolderFromMod(string p_strFrom, bool p_booRecurse)
		{
			string strFrom = p_strFrom.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
			return InstallFolderFromMod(strFrom, strFrom, p_booRecurse);
		}

		/// <summary>
		/// Installs the files in the specified folder from the mod to the specified location on the file system.
		/// </summary>
		/// <param name="p_strFrom">The path of the folder in the mod containing the files to install.</param>
		/// <param name="p_strTo">The path on the file system where the files are to be created.</param>
		/// <param name="p_booRecurse">Whether to install all files in all subfolders.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public bool InstallFolderFromMod(string p_strFrom, string p_strTo, bool p_booRecurse)
		{
			string strFrom = p_strFrom.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToLowerInvariant();
			if (!strFrom.EndsWith(Path.DirectorySeparatorChar.ToString()))
				strFrom += Path.DirectorySeparatorChar;
			string strTo = p_strTo.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if ((strTo.Length > 0) && (!strTo.EndsWith(Path.DirectorySeparatorChar.ToString())))
				strTo += Path.DirectorySeparatorChar;
			foreach (string strMODFile in GetModFileList(strFrom, p_booRecurse))
			{
				string strNewFileName = strMODFile.Substring(strFrom.Length);
				if (!InstallFileFromMod(strMODFile, Path.Combine(strTo, strNewFileName)))
					return false;
			}
			return true;
		}

		/// <summary>
		/// Installs the specified file from the mod to the specified location on the file system.
		/// </summary>
		/// <param name="p_strFrom">The path of the file in the mod to install.</param>
		/// <param name="p_strTo">The path on the file system where the file is to be created.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public virtual bool InstallFileFromMod(string p_strFrom, string p_strTo)
		{
			if (ModInstallFileFilter.IsIgnored(p_strFrom) || ModInstallFileFilter.IsIgnored(p_strTo))
				return true;

			return ExecuteWithFullTrust(() =>
			{
				if (!IsDeferredInstallationEnabled)
					return InstallationSession.Submit(new InstallModFileOperation(p_strFrom, p_strTo));

				string strDestination = p_strTo.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string strStagingPath = ScriptedInstallStagingPathResolver.GetStagingPath(Mod, GameMode, VirtualModActivator, strDestination, false);
				bool booSourceAvailable = ModContainsFile(p_strFrom);
				bool booStageFile = ResolveDeferredStagingWrite(strStagingPath, booSourceAvailable);
				ModLinkInstallDecision midLinkDecision = ResolveDeferredLinkDecision(strDestination);
				if (!InstallationSession.Submit(new InstallModFileOperation(p_strFrom, p_strTo, strStagingPath, booStageFile, midLinkDecision)))
					return false;

				// Legacy InstallFileFromMod reports success only when AddFileLink creates or replaces the active link.
				// Existing staged content can still satisfy the operation when the incoming archive file is unavailable.
				return midLinkDecision.CreatesActiveLink && (booSourceAvailable || File.Exists(strStagingPath));
			});
		}



		/// <summary>
		/// Installs the specified file from the mod to the file system.
		/// </summary>
		/// <param name="p_strFile">The path of the file to install.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public bool InstallFileFromMod(string p_strFile)
		{
			string strFile = p_strFile.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return InstallFileFromMod(strFile, strFile);
		}

		/// <summary>
		/// Retrieves the list of files in the mod.
		/// </summary>
		/// <returns>The list of files in the mod.</returns>
		public string[] GetModFileList()
		{
			string[] strFiles = null;
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				strFiles = Mod.GetFileList().ToArray();
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
			for (Int32 i = strFiles.Length - 1; i >= 0; i--)
				strFiles[i] = strFiles[i].Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return strFiles;
		}

		/// <summary>
		/// Retrieves the list of files in the specified folder in the mod.
		/// </summary>
		/// <param name="p_strFolder">The folder whose file list is to be retrieved.</param>
		/// <param name="p_booRecurse">Whether to return files that are in subdirectories of the given directory.</param>
		/// <returns>The list of files in the specified folder in the mod.</returns>
		public string[] GetModFileList(string p_strFolder, bool p_booRecurse)
		{
			string[] strFiles = null;
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				strFiles = Mod.GetFileList(p_strFolder, p_booRecurse).ToArray();
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
			for (Int32 i = strFiles.Length - 1; i >= 0; i--)
				strFiles[i] = strFiles[i].Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			return strFiles;
		}

		/// <summary>
		/// Retrieves the specified file from the mod.
		/// </summary>
		/// <param name="p_strFile">The file to retrieve.</param>
		/// <returns>The requested file data.</returns>
		public byte[] GetFileFromMod(string p_strFile)
		{
			byte[] bteFile = null;
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();
				bteFile = Mod.GetFile(p_strFile);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
			return bteFile;
		}

		/// <summary>
		/// Gets a filtered list of all files in a user's Data directory.
		/// </summary>
		/// <param name="p_strPath">The subdirectory of the Data directory from which to get the listing.</param>
		/// <param name="p_strPattern">The pattern against which to filter the file paths.</param>
		/// <param name="p_booAllFolders">Whether or not to search through subdirectories.</param>
		/// <returns>A filtered list of all files in a user's Data directory.</returns>
		public string[] GetExistingDataFileList(string p_strPath, string p_strPattern, bool p_booAllFolders)
		{
			return ExecuteWithFullTrust(() =>
			{
				if (InstallationSession.ProjectedState != null)
					return InstallationSession.ProjectedState.GetExistingDataFileList(p_strPath, p_strPattern, p_booAllFolders);

				string strPath = p_strPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string strFixedPath = GameMode.GetModFormatAdjustedPath(Mod.Format, strPath, false);
				return Installers.DataFileUtility.GetExistingDataFileList(strFixedPath, p_strPattern, p_booAllFolders);
			});
		}

		/// <summary>
		/// Determines if the specified file exists in the user's Data directory.
		/// </summary>
		/// <param name="p_strPath">The path of the file whose existence is to be verified.</param>
		/// <returns><c>true</c> if the specified file exists; <c>false</c>
		/// otherwise.</returns>
		public bool DataFileExists(string p_strPath)
		{
			return ExecuteWithFullTrust(() =>
			{
				if (InstallationSession.ProjectedState != null)
					return InstallationSession.ProjectedState.DataFileExists(p_strPath);

				string strPath = p_strPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string strFixedPath = GameMode.GetModFormatAdjustedPath(Mod.Format, strPath, false);
				return Installers.DataFileUtility.DataFileExists(strFixedPath);
			});
		}

		/// <summary>
		/// Gets the speified file from the user's Data directory.
		/// </summary>
		/// <param name="p_strPath">The path of the file to retrieve.</param>
		/// <returns>The specified file, or <c>null</c> if the file does not exist.</returns>
		public byte[] GetExistingDataFile(string p_strPath)
		{
			return ExecuteWithFullTrust(() =>
			{
				if (InstallationSession.ProjectedState != null)
					return InstallationSession.ProjectedState.GetExistingDataFile(p_strPath);

				string strPath = p_strPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string strFixedPath = GameMode.GetModFormatAdjustedPath(Mod.Format, strPath, false);
				return Installers.DataFileUtility.GetExistingDataFile(strFixedPath);
			});
		}

		/// <summary>
		/// Writes the file represented by the given byte array to the given path.
		/// </summary>
		/// <remarks>
		/// This method writes the given data as a file at the given path. If the file
		/// already exists the user is prompted to overwrite the file.
		/// </remarks>
		/// <param name="p_strPath">The path where the file is to be created.</param>
		/// <param name="p_bteData">The data that is to make up the file.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public bool GenerateDataFile(string p_strPath, byte[] p_bteData)
		{
			return ExecuteWithFullTrust(() =>
			{
				if (!IsDeferredInstallationEnabled)
					return InstallationSession.Submit(new GenerateDataFileOperation(p_strPath, p_bteData));

				string strDestination = p_strPath.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string strStagingPath = ScriptedInstallStagingPathResolver.GetStagingPath(Mod, GameMode, VirtualModActivator, strDestination, true);
				bool booStageFile = ResolveDeferredStagingWrite(strStagingPath, true);
				ModLinkInstallDecision midLinkDecision = ResolveDeferredLinkDecision(strDestination);
				return InstallationSession.Submit(new GenerateDataFileOperation(p_strPath, p_bteData, strStagingPath, booStageFile, midLinkDecision));
			});
		}

		#endregion

		#region UI

		#region MessageBox

		/// <summary>
		/// Shows a message box with the given message.
		/// </summary>
		/// <param name="p_strMessage">The message to display in the message box.</param>
		public void MessageBox(string p_strMessage)
		{
			UIManager.ShowMessageBox(p_strMessage);
		}

		/// <summary>
		/// Shows a message box with the given message and title.
		/// </summary>
		/// <param name="p_strMessage">The message to display in the message box.</param>
		/// <param name="p_strTitle">The message box's title, display in the title bar.</param>
		public void MessageBox(string p_strMessage, string p_strTitle)
		{
			UIManager.ShowMessageBox(p_strMessage, p_strTitle);
		}

		/// <summary>
		/// Shows a message box with the given message, title, and buttons.
		/// </summary>
		/// <param name="p_strMessage">The message to display in the message box.</param>
		/// <param name="p_strTitle">The message box's title, display in the title bar.</param>
		/// <param name="p_mbbButtons">The buttons to show in the message box.</param>
		public DialogResult MessageBox(string p_strMessage, string p_strTitle, MessageBoxButtons p_mbbButtons)
		{
			return UIManager.ShowMessageBox(p_strMessage, p_strTitle, p_mbbButtons);
		}

		/// <summary>
		/// Shows a message box with the given message, title, buttons, and icon.
		/// </summary>
		/// <param name="p_strMessage">The message to display in the message box.</param>
		/// <param name="p_strTitle">The message box's title, display in the title bar.</param>
		/// <param name="p_mbbButtons">The buttons to show in the message box.</param>
		/// <param name="p_mdiIcon">The icon to display in the message box.</param>
		public DialogResult MessageBox(string p_strMessage, string p_strTitle, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mdiIcon)
		{
			return UIManager.ShowMessageBox(p_strMessage, p_strTitle, p_mbbButtons, p_mdiIcon);
		}

		#endregion

		#region ExtendedMessageBox

		/// <summary>
		/// Shows an extended message box with the given message, title, details, buttons, and icon.
		/// </summary>
		/// <param name="p_strMessage">The message to display in the message box.</param>
		/// <param name="p_strTitle">The message box's title, displayed in the title bar.</param>
		/// <param name="p_strDetails">The message box's details, displayed in the details area.</param>
		/// <param name="p_mbbButtons">The buttons to show in the message box.</param>
		/// <param name="p_mdiIcon">The icon to display in the message box.</param>
		public DialogResult ExtendedMessageBox(string p_strMessage, string p_strTitle, string p_strDetails, MessageBoxButtons p_mbbButtons, MessageBoxIcon p_mdiIcon)
		{
			return UIManager.ShowExtendedMessageBox(p_strMessage, p_strTitle, p_strDetails, p_mbbButtons, p_mdiIcon);
		}

		#endregion

		#region Select

		/// <summary>
		/// Displays a selection form to the user.
		/// </summary>
		/// <param name="p_lstOptions">The options from which to select.</param>
		/// <param name="p_strTitle">The title of the selection form.</param>
		/// <param name="p_booSelectMany">Whether more than one items can be selected.</param>
		/// <returns>The selected option names.</returns>
		public string[] Select(IList<SelectOption> p_lstOptions, string p_strTitle, bool p_booSelectMany)
		{
			return UIManager.Select(p_lstOptions, p_strTitle, p_booSelectMany);
		}

		#endregion

		#endregion

		#region Version Checking

		/// <summary>
		/// Gets the version of the mod manager.
		/// </summary>
		/// <returns>The version of the mod manager.</returns>
		public virtual Version GetModManagerVersion()
		{
			return EnvironmentInfo.ApplicationVersion;
		}

		/// <summary>
		/// Gets the version of the game that is installed.
		/// </summary>
		/// <returns>The version of the game, or <c>null</c> if Fallout
		/// is not installed.</returns>
		public Version GetGameVersion()
		{
			return GameMode.GameVersion;
		}

		#endregion

		#region Plugin Management

		/// <summary>
		/// The returns a list of the paths of the given plugins, relative to the game mode's installation path.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose paths are to be made relative.</param>
		/// <returns>A list of the paths of the given plugins, relative to the game mode's installation path.</returns>
		protected string[] RelativizePluginPaths(IList<Plugin> p_lstPlugins)
		{
			string[] strPlugins = new string[p_lstPlugins.Count];
			string strInstallationPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, GameMode.GetModFormatAdjustedPath(Mod.Format, null, false));
			Int32 intTrimLength = strInstallationPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Length + 1;
			for (Int32 i = 0; i < p_lstPlugins.Count; i++)
				strPlugins[i] = p_lstPlugins[i].Filename.Remove(0, intTrimLength);
			return strPlugins;
		}

		/// <summary>
		/// Gets a list of all installed plugins.
		/// </summary>
		/// <returns>A list of all installed plugins.</returns>
		public string[] GetAllPlugins()
		{
			return ExecuteWithFullTrust(() => InstallationSession.ProjectedState == null
				? RelativizePluginPaths(Installers.PluginManager.ManagedPlugins)
				: InstallationSession.ProjectedState.GetAllPlugins());
		}

		#region Plugin Activation Management

		/// <summary>
		/// Retrieves a list of currently active plugins.
		/// </summary>
		/// <returns>A list of currently active plugins.</returns>
		public string[] GetActivePlugins()
		{
			return ExecuteWithFullTrust(() => InstallationSession.ProjectedState == null
				? RelativizePluginPaths(Installers.PluginManager.ActivePlugins)
				: InstallationSession.ProjectedState.GetActivePlugins());
		}

		/// <summary>
		/// Sets the activated status of a plugin (i.e., and esp or esm file).
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to activate or deactivate.</param>
		/// <param name="p_booActivate">Whether to activate the plugin.</param>
		public void SetPluginActivation(string p_strPluginPath, bool p_booActivate)
		{
			ExecuteWithFullTrust(() => InstallationSession.Submit(new SetPluginActivationOperation(p_strPluginPath, p_booActivate)));
		}

		#endregion

		#region Load Order Management

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_strPlugin">The path to the plugin file whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		protected void DoSetPluginOrderIndex(string p_strPlugin, int p_intNewIndex)
		{
			InstallationSession.Submit(new SetPluginOrderIndexOperation(p_strPlugin, p_intNewIndex));
		}


		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_strPlugin">The path to the plugin file whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		public void SetPluginOrderIndex(string p_strPlugin, int p_intNewIndex)
		{
			ExecuteWithFullTrust(() => DoSetPluginOrderIndex(p_strPlugin, p_intNewIndex));
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// Each plugin will be moved from its current index to its indices' position
		/// in <paramref name="p_intPlugins"/>.
		/// </remarks>
		/// <param name="p_intPlugins">The new load order of the plugins. Each entry in this array
		/// contains the current index of a plugin. This array must contain all current indices.</param>
		protected void DoSetLoadOrder(int[] p_intPlugins)
		{
			InstallationSession.Submit(new SetLoadOrderOperation(p_intPlugins));
		}


		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// Each plugin will be moved from its current index to its indices' position
		/// in <paramref name="p_intPlugins"/>.
		/// </remarks>
		/// <param name="p_intPlugins">The new load order of the plugins. Each entry in this array
		/// contains the current index of a plugin. This array must contain all current indices.</param>
		public void SetLoadOrder(int[] p_intPlugins)
		{
			ExecuteWithFullTrust(() => DoSetLoadOrder(p_intPlugins));
		}

		/// <summary>
		/// Moves the specified plugins to the given position in the load order.
		/// </summary>
		/// <remarks>
		/// Note that the order of the given list of plugins is not maintained. They are re-ordered
		/// to be in the same order as they are in the before-operation load order. This, I think,
		/// is somewhat counter-intuitive and may change, though likely not so as to not break
		/// backwards compatibility.
		/// </remarks>
		/// <param name="p_intPlugins">The list of plugins to move to the given position in the
		/// load order. Each entry in this array contains the current index of a plugin.</param>
		/// <param name="p_intPosition">The position in the load order to which to move the specified
		/// plugins.</param>
		protected void DoSetLoadOrder(int[] p_intPlugins, int p_intPosition)
		{
			MovePluginsInLoadOrderOperation mloOperation = new MovePluginsInLoadOrderOperation(p_intPlugins, p_intPosition);

			// Preserve the legacy API side effect where the caller-supplied index array is sorted in place.
			Array.Sort<int>(p_intPlugins);
			InstallationSession.Submit(mloOperation);
		}


		/// <summary>
		/// Moves the specified plugins to the given position in the load order.
		/// </summary>
		/// <remarks>
		/// Note that the order of the given list of plugins is not maintained. They are re-ordered
		/// to be in the same order as they are in the before-operation load order. This, I think,
		/// is somewhat counter-intuitive and may change, though likely not so as to not break
		/// backwards compatibility.
		/// </remarks>
		/// <param name="p_intPlugins">The list of plugins to move to the given position in the
		/// load order. Each entry in this array contains the current index of a plugin.</param>
		/// <param name="p_intPosition">The position in the load order to which to move the specified
		/// plugins.</param>
		public void SetLoadOrder(int[] p_intPlugins, int p_intPosition)
		{
			ExecuteWithFullTrust(() => DoSetLoadOrder(p_intPlugins, p_intPosition));
		}

		/// <summary>
		/// Orders the plugins such that the specified plugins are in the specified
		/// order.
		/// </summary>
		/// <remarks>
		/// The given plugins may not end up consecutively ordered.
		/// </remarks>
		/// <param name="p_strRelativelyOrderedPlugins">The plugins to order relative to one another.</param>
		public void SetRelativeLoadOrder(string[] p_strRelativelyOrderedPlugins)
		{
			ExecuteWithFullTrust(() => InstallationSession.Submit(new SetRelativeLoadOrderOperation(p_strRelativelyOrderedPlugins)));
		}

		#endregion

		#endregion

		#region Ini File Value Management

		#region Ini File Value Retrieval

		/// <summary>
		/// Retrieves the specified settings value as a string.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file from which to retrieve the value.</param>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		public string GetIniString(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return ExecuteWithFullTrust(() => InstallationSession.ProjectedState == null
				? Installers.IniInstaller.GetIniString(p_strSettingsFileName, p_strSection, p_strKey)
				: InstallationSession.ProjectedState.GetIniString(p_strSettingsFileName, p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified settings value as an integer.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file from which to retrieve the value.</param>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		public Int32 GetIniInt(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return ExecuteWithFullTrust(() => InstallationSession.ProjectedState == null
				? Installers.IniInstaller.GetIniInt(p_strSettingsFileName, p_strSection, p_strKey)
				: InstallationSession.ProjectedState.GetIniInt(p_strSettingsFileName, p_strSection, p_strKey));
		}

		#endregion

		#region Ini Editing

		/// <summary>
		/// Sets the specified value in the specified Ini file to the given value.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public bool EditIni(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			return ExecuteWithFullTrust(() => SubmitIniEdit(p_strSettingsFileName, p_strSection, p_strKey, p_strValue, true));
		}

		/// <summary>
		/// Queues an INI value change without requesting an overwrite decision.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the settings file to edit.</param>
		/// <param name="p_strSection">The section containing the value to edit.</param>
		/// <param name="p_strKey">The key of the value to edit.</param>
		/// <param name="p_strValue">The value to assign.</param>
		/// <returns><c>true</c> when the edit is accepted for execution; otherwise, <c>false</c>.</returns>
		protected bool EditIniWithoutOverwriteDecision(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			return ExecuteWithFullTrust(() => SubmitIniEdit(p_strSettingsFileName, p_strSection, p_strKey, p_strValue, false));
		}

		/// <summary>
		/// Queues a game-specific value change, resolving its overwrite decision before deferred execution when required.
		/// </summary>
		/// <param name="p_strKey">The key identifying the game-specific value.</param>
		/// <param name="p_bteValue">The value to install.</param>
		/// <returns><c>true</c> when the change is accepted; otherwise, <c>false</c>.</returns>
		protected bool EditGameSpecificValue(string p_strKey, byte[] p_bteValue)
		{
			return ExecuteWithFullTrust(() =>
			{
				if (!IsDeferredInstallationEnabled)
					return InstallationSession.Submit(new EditGameSpecificValueOperation(p_strKey, p_bteValue));

				IGameSpecificValueInstallDecisionSupport gdsDecisionSupport = (IGameSpecificValueInstallDecisionSupport)Installers.GameSpecificValueInstaller;
				if (!gdsDecisionSupport.ResolveGameSpecificValueEdit(p_strKey))
					return false;

				return InstallationSession.Submit(new EditGameSpecificValueOperation(p_strKey, p_bteValue, true));
			});
		}

		#endregion

		#endregion

		#region Deferred Planning Helpers

		/// <summary>
		/// Resolves whether a deferred operation should replace its physical staging file.
		/// </summary>
		/// <param name="p_strStagingPath">The physical staging path.</param>
		/// <param name="p_booSourceAvailable">Whether the operation has content available to write.</param>
		/// <returns><c>true</c> when the staging file should be written; otherwise, <c>false</c>.</returns>
		private bool ResolveDeferredStagingWrite(string p_strStagingPath, bool p_booSourceAvailable)
		{
			if (!p_booSourceAvailable)
				return false;
			if (m_hstPlannedStagingWrites.Contains(p_strStagingPath))
				return true;

			string strDirectory = Path.GetDirectoryName(p_strStagingPath);
			bool booStageFile = !Directory.Exists(strDirectory) || ((IModFileInstallDecisionSupport)Installers.FileInstaller).ResolveDataFileOverwrite(p_strStagingPath);
			if (booStageFile)
				m_hstPlannedStagingWrites.Add(p_strStagingPath);
			return booStageFile;
		}

		/// <summary>
		/// Resolves the virtual-link outcome used by a deferred file operation without mutating deployment state.
		/// </summary>
		/// <param name="p_strDestinationPath">The logical destination path of the file.</param>
		/// <returns>The link decision and projected outcome to associate with the operation.</returns>
		private ModLinkInstallDecision ResolveDeferredLinkDecision(string p_strDestinationPath)
		{
			string strDestination = NormalizePlanningPath(p_strDestinationPath);
			if (m_hstProjectedActiveLinks.Contains(strDestination))
				return new ModLinkInstallDecision().WithLinkOutcome(true, false);

			ModLinkInstallDecision midDecision = ((IModLinkInstallDecisionSupport)ModLinkInstaller).ResolveFileLinkDecision(Mod, p_strDestinationPath, ModInstallRoot.Default);
			if (midDecision.HasLinkOutcome && (midDecision.LinkOutcome == true))
				m_hstProjectedActiveLinks.Add(strDestination);
			return midDecision;
		}

		/// <summary>
		/// Determines whether the specified archive path identifies a file in the current mod.
		/// </summary>
		/// <param name="p_strModFilePath">The archive-relative path to test.</param>
		/// <returns><c>true</c> when the mod contains the requested file; otherwise, <c>false</c>.</returns>
		private bool ModContainsFile(string p_strModFilePath)
		{
			if (m_hstModFiles == null)
			{
				m_hstModFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (string strFile in Mod.GetFileList())
					m_hstModFiles.Add(NormalizePlanningPath(strFile));
			}

			return m_hstModFiles.Contains(NormalizePlanningPath(p_strModFilePath));
		}

		/// <summary>
		/// Submits an INI edit after optionally resolving the current overwrite decision.
		/// </summary>
		/// <param name="p_strSettingsFileName">The settings file to edit.</param>
		/// <param name="p_strSection">The section containing the value.</param>
		/// <param name="p_strKey">The value key.</param>
		/// <param name="p_strValue">The requested value.</param>
		/// <param name="p_booResolveOverwrite">Whether overwrite-decision processing should be performed.</param>
		/// <returns><c>true</c> when the edit is accepted; otherwise, <c>false</c>.</returns>
		private bool SubmitIniEdit(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue, bool p_booResolveOverwrite)
		{
			if (!IsDeferredInstallationEnabled)
				return InstallationSession.Submit(new EditIniOperation(p_strSettingsFileName, p_strSection, p_strKey, p_strValue, !p_booResolveOverwrite));

			if (p_booResolveOverwrite)
			{
				string strCurrentValue = InstallationSession.ProjectedState.GetIniString(p_strSettingsFileName, p_strSection, p_strKey);
				IIniEditDecisionSupport idsDecisionSupport = (IIniEditDecisionSupport)Installers.IniInstaller;
				if (!idsDecisionSupport.ResolveIniEdit(p_strSettingsFileName, p_strSection, p_strKey, p_strValue, strCurrentValue))
					return false;
			}

			return InstallationSession.Submit(new EditIniOperation(p_strSettingsFileName, p_strSection, p_strKey, p_strValue, true));
		}

		/// <summary>
		/// Normalizes a relative path for case-insensitive planning-state comparisons.
		/// </summary>
		/// <param name="p_strPath">The path to normalize.</param>
		/// <returns>The normalized relative path.</returns>
		private static string NormalizePlanningPath(string p_strPath)
		{
			return (p_strPath ?? String.Empty).Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		}

		#endregion

		#region Obsolete/Ignored

		/// <summary>
		/// Registers a warning to be displayed when the user deactivates the specified plugin in the mod manager.
		/// </summary>
		/// <remarks>
		/// This method is ignored. Registering warnings is not supported by the currect implementation of the mod
		/// manager.
		/// </remarks>
		/// <param name="p_strPlugin">The plugin for which to register a warning.</param>
		/// <param name="p_strWarningType">The type of warning to register.</param>
		public void SetDeactivationWarning(string p_strPlugin, string p_strWarningType)
		{
			DeactivationWarningType dwtWarningType = (DeactivationWarningType)Enum.Parse(typeof(DeactivationWarningType), p_strWarningType);
			SetDeactivationWarning(p_strPlugin, dwtWarningType);
		}

		/// <summary>
		/// Registers a warning to be displayed when the user deactivates the specified plugin in the mod manager.
		/// </summary>
		/// <remarks>
		/// This method is ignored. Registering warnings is not supported by the currect implementation of the mod
		/// manager.
		/// </remarks>
		/// <param name="p_strPlugin">The plugin for which to register a warning.</param>
		/// <param name="p_dwtWarningType">The type of warning to register.</param>
		private void SetDeactivationWarning(string p_strPlugin, DeactivationWarningType p_dwtWarningType)
		{
			//TODO implement registering plugin deactivation warnings
			// in addition to generic warning types, we should allow custom messages
		}

		#endregion
	}

	/// <summary>
	/// List to possible warning types when registering a warning for a plugin deactivation.
	/// </summary>
	public enum DeactivationWarningType
	{
		/// <summary>
		/// Allow the deactivation.
		/// </summary>
		Allow,

		/// <summary>
		/// Warn that the deactivation may cause problems.
		/// </summary>
		WarnAgainst,

		/// <summary>
		/// Prevent the mod from being deactivated.
		/// </summary>
		Disallow
	}
}
