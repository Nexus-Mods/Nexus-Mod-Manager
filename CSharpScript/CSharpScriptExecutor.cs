using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using System.Diagnostics;

namespace Nexus.Client.ModManagement.Scripting.CSharpScript
{
	/// <summary>
	/// Executes a C# script.
	/// </summary>
	public class CSharpScriptExecutor : ScriptExecutorBase
	{

		private static Regex m_regScriptClass = new Regex(@"(class\s+Script\s*:.*?)(\S*BaseScript)");
		private static Regex m_regFommUsing = new Regex(@"\s*using\s*fomm.Scripting\s*;");
		private CSharpScriptFunctionProxy m_csfFunctions = null;
		private IGameMode m_gmdGameMode = null;
		private IEnvironmentInfo m_eifEnvironmentInfo = null;
		private string m_strVirtualActivatorPath = String.Empty;

		#region Properties

		/// <summary>
		/// Gets the type of the base script for all C# scripts.
		/// </summary>
		/// <value>The type of the base script for all C# scripts.</value>
		protected Type BaseScriptType { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_csfFunctions">The proxy providing the implementations of the functions available to the C# script.</param>
		/// <param name="p_tpeBaseScriptType">The type of the base script from which all C# scripts should derive.</param>
		/// <param name="p_strVirtualActivatorFolder">The virtual mod activator's folder.</param>
		public CSharpScriptExecutor(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, CSharpScriptFunctionProxy p_csfFunctions, Type p_tpeBaseScriptType, string p_strVirtualActivatorFolder)
		{
			m_gmdGameMode = p_gmdGameMode;
			m_eifEnvironmentInfo = p_eifEnvironmentInfo;
			m_csfFunctions = p_csfFunctions;
			BaseScriptType = p_tpeBaseScriptType;
			m_strVirtualActivatorPath = p_strVirtualActivatorFolder;
		}

		#endregion

		#region ScriptExecutorBase Members

		/// <summary>
		/// Executes the script.
		/// </summary>
		/// <param name="p_scpScript">The C# Script to execute.</param>
		/// <returns><c>true</c> if the script completes successfully;
		/// <c>false</c> otherwise.</returns>
		/// <exception cref="ArgumentException">Thrown if <paramref name="p_scpScript"/> is not a
		/// <see cref="CSharpScript"/>.</exception>
		public override bool DoExecute(IScript p_scpScript)
		{
			if (!(p_scpScript is CSharpScript))
				throw new ArgumentException("The given script must be of type CSharpScript.", "p_scpScript");

			CSharpScript cscScript = (CSharpScript)p_scpScript;

			byte[] bteScript = Compile(cscScript.Code);
			if (bteScript == null)
				return false;

			AppDomain admScript = CreateSandbox(p_scpScript);
			try
			{
				m_csfFunctions.TaskStarted += new EventHandler<EventArgs<IBackgroundTask>>(Functions_TaskStarted);
				object[] args = { m_csfFunctions };
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
				return srnRunner.Execute(bteScript);
			}
			finally
			{
				m_csfFunctions.TaskStarted -= Functions_TaskStarted;
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
		/// Compiles the given C# script code.
		/// </summary>
		/// <remarks>
		/// The compiled script is not loaded into the current domain.
		/// </remarks>
		/// <param name="p_strCode">The code to compile.</param>
		/// <returns>The bytes of the assembly containing the script to execute.</returns>
		protected byte[] Compile(string p_strCode)
		{
			CSharpScriptCompiler sccCompiler = new CSharpScriptCompiler();
			CompilerErrorCollection cecErrors = null;

			string strBaseScriptClassName = m_regScriptClass.Match(p_strCode).Groups[2].ToString();
			string strCode = m_regScriptClass.Replace(p_strCode, "using " + BaseScriptType.Namespace + ";\r\n$1" + BaseScriptType.Name);
			Regex regOtherScriptClasses = new Regex(String.Format(@"(class\s+\S+\s*:.*?)(?<!\w){0}", strBaseScriptClassName));
			strCode = regOtherScriptClasses.Replace(strCode, "$1" + BaseScriptType.Name);
			strCode = m_regFommUsing.Replace(strCode, "");
			byte[] bteAssembly = sccCompiler.Compile(strCode, BaseScriptType, out cecErrors);

			if (cecErrors != null)
			{
				StringBuilder stbErrors = new StringBuilder();
				if (cecErrors.HasErrors)
				{
					stbErrors.Append("<h3 style='color:red'>Errors</h3><ul>");
					foreach (CompilerError cerError in cecErrors)
						if (!cerError.IsWarning)
							stbErrors.AppendFormat("<li><b>{0},{1}:</b> {2} <i>(Error {3})</i></li>", cerError.Line, cerError.Column, cerError.ErrorText, cerError.ErrorNumber);
					stbErrors.Append("</ul>");
				}
				if (cecErrors.HasWarnings)
				{
					stbErrors.Append("<h3 style='color:#ffd700;'>Warnings</h3><ul>");
					foreach (CompilerError cerError in cecErrors)
						if (cerError.IsWarning)
							stbErrors.AppendFormat("<li><b>{0},{1}:</b> {2} <i>(Error {3})</i></li>", cerError.Line, cerError.Column, cerError.ErrorText, cerError.ErrorNumber);
					stbErrors.Append("</ul>");
				}
				if (cecErrors.HasErrors)
				{
					string strMessage = "Could not compile script; errors were found.";
					m_csfFunctions.ExtendedMessageBox(strMessage, "Error", stbErrors.ToString(), MessageBoxButtons.OK, MessageBoxIcon.Error);
					return null;
				}
			}
			return bteAssembly;
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
			Trace.TraceInformation("Creating C# Script Sandbox...");
			Trace.Indent();

			Evidence eviSecurityInfo = null;
			AppDomainSetup adsInfo = new AppDomainSetup();
			//should this be different from the current ApplicationBase?
			adsInfo.ApplicationBase = Path.GetDirectoryName(Application.ExecutablePath);
			Set<string> setPaths = new Set<string>(StringComparer.OrdinalIgnoreCase);
			Type tpeBaseScript = BaseScriptType;
			while ((tpeBaseScript != null) && (tpeBaseScript != typeof(object)))
			{
				setPaths.Add(Path.GetDirectoryName(Assembly.GetAssembly(tpeBaseScript).Location));
				tpeBaseScript = tpeBaseScript.BaseType;
			}
			Type tpeScript = p_scpScript.Type.GetType();
			while ((tpeScript != null) && (tpeScript != typeof(object)))
			{
				setPaths.Add(Path.GetDirectoryName(Assembly.GetAssembly(tpeScript).Location));
				tpeScript = tpeScript.BaseType;
			}
			adsInfo.PrivateBinPath = String.Join(";", setPaths.ToArray());

			Trace.TraceInformation("ApplicationBase: {0}", adsInfo.ApplicationBase);
			Trace.TraceInformation("PrivateBinPath: {0}", adsInfo.PrivateBinPath);

			adsInfo.ApplicationName = "ScriptRunner";
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
			pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, m_gmdGameMode.GameModeEnvironmentInfo.ModDirectory));
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

				string strHDLink = m_eifEnvironmentInfo.Settings.HDLinkFolder[m_gmdGameMode.ModeId];
				if (!String.IsNullOrWhiteSpace(strHDLink))
				{
					pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Write, strHDLink));
					pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read, strHDLink));
					pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.Append, strHDLink));
					pstGrantSet.AddPermission(new FileIOPermission(FileIOPermissionAccess.PathDiscovery, strHDLink));
				}
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

			return AppDomain.CreateDomain("ScriptRunnerDomain", eviSecurityInfo, adsInfo, pstGrantSet);
		}
	}
}
