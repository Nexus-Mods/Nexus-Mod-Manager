using System;
using System.Collections.Generic;
using Nexus.UI.Controls;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using System.CodeDom.Compiler;
using System.Threading;

namespace Nexus.Client.ModManagement.Scripting.CSharpScript
{
	/// <summary>
	/// Describes the C# script type.
	/// </summary>
	/// <remarks>
	/// This is the script that allows scripting using the C# language. It is meant
	/// to be the most advanced and flexible script.
	/// </remarks>
	public class CSharpScriptType : IScriptType
	{
		private static List<string> m_lstFileNames = new List<string>() { "script.cs" };

		#region IScriptType Members

		/// <summary>
		/// Gets the name of the script type.
		/// </summary>
		/// <value>The name of the script type.</value>
		public string TypeName
		{
			get
			{
				return "C# Script";
			}
		}

		/// <summary>
		/// Gets the unique id of the script type.
		/// </summary>
		/// <value>The unique id of the script type.</value>
		public string TypeId
		{
			get
			{
				return "CSharpScript";
			}
		}

		/// <summary>
		/// Gets the list of file names used by scripts of the current type.
		/// </summary>
		/// <remarks>
		/// The list is in order of preference, with the first item being the preferred
		/// file name.
		/// </remarks>
		/// <value>The list of file names used by scripts of the current type.</value>
		public IList<string> FileNames
		{
			get
			{
				return m_lstFileNames;
			}
		}

		/// <summary>
		/// Creates an editor for the script type.
		/// </summary>
		/// <param name="p_lstModFiles">The list of files if the current mod.</param>
		/// <returns>An editor for the script type.</returns>
		public IScriptEditor CreateEditor(IList<VirtualFileSystemItem> p_lstModFiles)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Creates an executor that can run the script type.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The game mode currently bieng managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <returns>An executor that can run the script type.</returns>
		public IScriptExecutor CreateExecutor(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, SynchronizationContext p_scxUIContext)
		{
			CSharpScriptFunctionProxy csfFunctions = GetScriptFunctionProxy(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_igpInstallers, p_scxUIContext);
			return new CSharpScriptExecutor(p_gmdGameMode, p_eifEnvironmentInfo, csfFunctions, BaseScriptType, p_ivaVirtualModActivator.VirtualPath);
		}

		/// <summary>
		/// Loads the script from the given text representation.
		/// </summary>
		/// <param name="p_strScriptData">The text to convert into a script.</param>
		/// <returns>The <see cref="IScript"/> represented by the given data.</returns>
		public IScript LoadScript(string p_strScriptData)
		{
			return new CSharpScript(this, p_strScriptData);
		}

		/// <summary>
		/// Saves the given script into a text representation.
		/// </summary>
		/// <param name="p_scpScript">The <see cref="IScript"/> to save.</param>
		/// <returns>The text represnetation of the given <see cref="IScript"/>.</returns>
		public string SaveScript(IScript p_scpScript)
		{
			return ((CSharpScript)p_scpScript).Code;
		}

		/// <summary>
		/// Determines if the given script is valid.
		/// </summary>
		/// <param name="p_scpScript">The script to validate.</param>
		/// <returns><c>true</c> if the given script is valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateScript(IScript p_scpScript)
		{
			CSharpScriptCompiler sccCompiler = new CSharpScriptCompiler();
			CompilerErrorCollection cecErrors = null;
			sccCompiler.Compile(((CSharpScript)p_scpScript).Code, BaseScriptType, out cecErrors);
			return cecErrors == null;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the type of the base script for all C# scripts.
		/// </summary>
		/// <value>The type of the base script for all C# scripts.</value>
		protected virtual Type BaseScriptType
		{
			get
			{
				return typeof(CSharpBaseScript);
			}
		}

		#endregion

		/// <summary>
		/// Returns a proxy that implements the functions available to C# scripts.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_gmdGameMode">The game mode currently bieng managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_ivaVirtualModActivator">The virtual mod activator.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
		/// <returns>A proxy that implements the functions available to C# scripts.</returns>
		protected virtual CSharpScriptFunctionProxy GetScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, SynchronizationContext p_scxUIContext)
		{
			return new CSharpScriptFunctionProxy(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_igpInstallers, new UIUtil(p_gmdGameMode, p_eifEnvironmentInfo, p_scxUIContext));
		}
	}
}
