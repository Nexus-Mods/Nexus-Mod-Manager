using System;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using System.Diagnostics;

namespace Nexus.Client.ModManagement.Scripting.ModScript
{
	/// <summary>
	/// Executes a Mod Script script.
	/// </summary>
	public class ModScriptExecutor : ScriptExecutorBase
	{
		private ModScriptFunctionProxy m_msfFunctions = null;
		private IGameMode m_gmdGameMode = null;
		private IEnvironmentInfo m_eifEnvironmentInfo = null;
		private string m_strVirtualActivatorPath = String.Empty;

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_msfFunctions">The proxy providing the implementations of the functions available to the mod script script.</param>
		public ModScriptExecutor(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, ModScriptFunctionProxy p_msfFunctions, string p_strVirtualActivatorPath)
		{
			m_gmdGameMode = p_gmdGameMode;
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_msfFunctions = p_msfFunctions;
			m_strVirtualActivatorPath = p_strVirtualActivatorPath;
		}

		#endregion

		#region ScriptExecutorBase Members

		/// <summary>
		/// Executes the script.
		/// </summary>
		/// <param name="p_scpScript">The Mod Script to execute.</param>
		/// <returns><c>true</c> if the script completes successfully;
		/// <c>false</c> otherwise.</returns>
		/// <exception cref="ArgumentException">Thrown if <paramref name="p_scpScript"/> is not a
		/// <see cref="ModScript"/>.</exception>
		public override bool DoExecute(IScript p_scpScript)
		{
			if (!(p_scpScript is ModScript))
				throw new ArgumentException("The given script must be of type ModScript.", "p_scpScript");

			ModScript mscScript = (ModScript)p_scpScript;

			if (String.IsNullOrEmpty(mscScript.Code))
				return false;

			AppDomain admScript = CreateSandbox(p_scpScript);
			try
			{
				m_msfFunctions.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(Functions_TaskStarted);
				object[] args = { m_msfFunctions };
				AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
				ScriptRunner srnRunner = null;
				try
				{
					srnRunner = (ScriptRunner)admScript.CreateInstanceFromAndUnwrap(typeof(ScriptRunner).Assembly.ManifestModule.FullyQualifiedName, typeof(ScriptRunner).FullName, false, BindingFlags.Default, null, args, null, null);
				}
				finally
				{
					AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
				}
				return srnRunner.Execute(mscScript.Code);
			}
			finally
			{
				m_msfFunctions.TaskStarted -= Functions_TaskStarted;
				AppDomain.Unload(admScript);
			}
		}

		#endregion

		/// <summary>
		/// Handles the <see cref="ScriptFunctionProxy.TaskStarted"/> event of script executors.
		/// </summary>
		/// <remarks>
		/// This bubbles the started task to any listeners.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{IBackgroundTask}"/> describing the event arguments.</param>
		private void Functions_TaskStarted(object sender, EventArgs<IBackgroundTask> e)
		{
			OnTaskStarted(e.Argument);
		}

		/// <summary>
		/// Handles the <see cref="AppDomain.AssemblyResolve"/> event.
		/// </summary>
		/// <remarks>
		/// Assemblies that have been load dynamically aren't accessible by assembly name. So, when, for example,
		/// this class looks for the assembly containing the ScriptRunner type that was CreateInstanceFromAndUnwrap-ed,
		/// the class can't find the type. This handler searches through loaded assemblies and finds the required assembly.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="args">A <see cref="ResolveEventArgs"/> describing the event arguments.</param>
		/// <returns>The assembly being looked for, or <c>null</c> if the assembly cannot
		/// be found.</returns>
		private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
		{
			foreach (Assembly asmLoaded in AppDomain.CurrentDomain.GetAssemblies())
				if (asmLoaded.FullName == args.Name)
					return asmLoaded;
			return null;
		}

		/// <summary>
		/// Creates a sandboxed domain.
		/// </summary>
		/// <remarks>
		/// The sandboxed domain is only given permission to alter the parts of the system
		/// that are relevant to mod management for the current game mode.
		/// </remarks>
		/// <param name="p_scpScript">The script we are going to execute. This is required so we can include
		/// the folder containing the script's script type class in the sandboxes PrivateBinPath.
		/// We need to do this so that any helper classes and libraries used by the script
		/// can be found.</param>
		/// <returns>A sandboxed domain.</returns>
		protected AppDomain CreateSandbox(IScript p_scpScript)
		{
			Trace.TraceInformation("Creating Mod Script Sandbox...");
			Trace.Indent();

			Evidence eviSecurityInfo = null;
			AppDomainSetup adsInfo = new AppDomainSetup();
			//should this be different from the current ApplicationBase?
			adsInfo.ApplicationBase = Path.GetDirectoryName(Application.ExecutablePath);
			Set<string> setPaths = new Set<string>(StringComparer.OrdinalIgnoreCase);
			Type tpeScript = p_scpScript.Type.GetType();
			while ((tpeScript != null) && (tpeScript != typeof(object)))
			{
				setPaths.Add(Path.GetDirectoryName(Assembly.GetAssembly(tpeScript).Location));
				tpeScript = tpeScript.BaseType;
			}
			adsInfo.PrivateBinPath = String.Join(";", setPaths.ToArray());

			Trace.TraceInformation("ApplicationBase: {0}", adsInfo.ApplicationBase);
			Trace.TraceInformation("PrivateBinPath: {0}", adsInfo.PrivateBinPath);

			adsInfo.ApplicationName = "ModScriptRunner";
			adsInfo.DisallowBindingRedirects = true;
			adsInfo.DisallowCodeDownload = true;
			adsInfo.DisallowPublisherPolicy = true;
			PermissionSet pstGrantSet = new PermissionSet(PermissionState.None);
			pstGrantSet.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, Path.GetDirectoryName(Application.ExecutablePath)));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, Path.GetDirectoryName(Application.ExecutablePath)));
			pstGrantSet.AddPermission(new ReflectionPermission(ReflectionPermissionFlag.RestrictedMemberAccess));

#if DEBUG
			//this is unsafe, and should only be used to view exceptions inside the sandbox
			pstGrantSet.AddPermission(new SecurityPermission(SecurityPermissionFlag.ControlEvidence));
			pstGrantSet.AddPermission(new SecurityPermission(SecurityPermissionFlag.ControlPolicy));
#endif

			//It's not clear to me if these permissions are dangerous
			pstGrantSet.AddPermission(new UIPermission(UIPermissionClipboard.NoClipboard));
			pstGrantSet.AddPermission(new UIPermission(UIPermissionWindow.AllWindows));
			pstGrantSet.AddPermission(new MediaPermission(MediaPermissionImage.SafeImage));

			//add the specific permissions the script will need
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_gmdGameMode.GameModeEnvironmentInfo.ModDirectory));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_gmdGameMode.GameModeEnvironmentInfo.ModCacheDirectory));

			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, m_gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_gmdGameMode.GameModeEnvironmentInfo.InstallInfoDirectory));

			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, Path.GetTempPath()));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, Path.GetTempPath()));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, Path.GetTempPath()));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, Path.GetTempPath()));

			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, m_eifEnvironmentInfo.TemporaryPath));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_eifEnvironmentInfo.TemporaryPath));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, m_eifEnvironmentInfo.TemporaryPath));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, m_eifEnvironmentInfo.TemporaryPath));

			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, m_gmdGameMode.GameModeEnvironmentInfo.InstallationPath));

			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, m_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, m_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory));
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, m_gmdGameMode.GameModeEnvironmentInfo.OverwriteDirectory));

			if (!String.IsNullOrEmpty(m_strVirtualActivatorPath))
			{
				pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, m_strVirtualActivatorPath));
				pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, m_strVirtualActivatorPath));
				pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, m_strVirtualActivatorPath));
				pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, m_strVirtualActivatorPath));
			}

			foreach (string strPath in m_gmdGameMode.WritablePaths)
			{
				pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, strPath));
				pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, strPath));
				if (Directory.Exists(strPath))
				{
					pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, strPath));
					pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, strPath));
				}
			}

			Trace.Unindent();

			return AppDomain.CreateDomain("ModScriptRunnerDomain", eviSecurityInfo, adsInfo, pstGrantSet);
		}
	}
}
