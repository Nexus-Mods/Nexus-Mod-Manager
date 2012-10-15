using System;
using Nexus.Client.Games.Gamebryo.ModManagement.Scripting;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Mods;

namespace Nexus.Client.Games.Fallout3.Scripting.CSharpScript
{
	/// <summary>
	/// Implements the functions availabe to Fallout 3 C# scripts.
	/// </summary>
	public class Fallout3CSharpScriptFunctionProxy : FalloutCSharpScriptFunctionProxy
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initialies the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod for which the script is running.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_bamBsaManager">The manager to use to work with BSA files.</param>
		/// <param name="p_uipUIProxy">The UI manager to use to interact with UI elements.</param>
		public Fallout3CSharpScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, InstallerGroup p_igpInstallers, BsaManager p_bamBsaManager, UIUtil p_uipUIProxy)
			: base(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_igpInstallers, p_bamBsaManager, p_uipUIProxy)
		{
		}

		#endregion

		#region Version Checking

		/// <summary>
		/// Gets the version of FOSE that is installed.
		/// </summary>
		/// <returns>The version of FOSE, or <c>null</c> if FOSE
		/// is not installed.</returns>
		public Version GetFoseVersion()
		{
			return ((Fallout3GameMode)GameMode).ScriptExtenderVersion;
		}

		#endregion
	}
}
