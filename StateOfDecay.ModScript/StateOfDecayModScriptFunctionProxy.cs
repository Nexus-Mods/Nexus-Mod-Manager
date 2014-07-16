using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.StateOfDecay;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting.ModScript;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.StateOfDecay.Scripting.ModScript
{
    /// <summary>
    /// Implements the functions availabe to the StateOfDecay variant of Mod Script scripts.
    /// </summary>
    /// <remarks>
    /// The proxy allows sandboxed scripts to call functions that can perform
    /// actions outside of the sandbox.
    /// 
    /// All values in Mod Script are strings. Thus, all parameters to the functions
    /// are strings. If they represent another datatype, the functions must
    /// convert the values before using them.
    /// 
    /// There are a few exceptions, where some methods accept non-string parameters.
    /// This is becuase these methods are called by the interpreter, and not from
    /// the interpreted code.
    /// </remarks>
    public class StateOfDecayModScriptFunctionProxy : ModScriptFunctionProxy
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_modMod">The mod for which the script is running.</param>
        /// <param name="p_gmdGameMode">The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        /// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
        /// <param name="p_uipUIProxy">The UI manager to use to interact with UI elements.</param>
		public StateOfDecayModScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, InstallerGroup p_igpInstallers, ModScriptUIUtil p_uipUIProxy)
            : base(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_igpInstallers, p_uipUIProxy)
        {
        }

        #endregion

        #region Version Checking

        /// <summary>
        /// Determines if the current game version is greater than the given version.
        /// </summary>
        /// <remarks>
        /// This is an alis for <see cref="ModScriptFunctionProxy.GameNewerThan(string)"/>. It exists
        /// to maintain compatibility with old Mod Scripts.
        /// </remarks>
        /// <param name="p_strGameVersion">The version to which to compare the game's version.</param>
        /// <returns><c>true</c> if the game's version is greater than the given version.</returns>
        /// <seealso cref="ModScriptFunctionProxy.GameNewerThan(string)"/>
		public bool StateOfDecayNewerThan(string p_strGameVersion)
        {
            return GameNewerThan(p_strGameVersion);
        }

        /// <summary>
        /// Determines if the installed script extender version is greater than the given version.
        /// </summary>
        /// <param name="p_strGameVersion">The version to which to compare the script extender's version.</param>
        /// <returns><c>true</c> if the script extender's version is greater than the given version.</returns>
        public bool ScriptExtenderNewerThan(string p_strGameVersion)
        {
            Version verCompare = new Version(p_strGameVersion.Contains(".") ? p_strGameVersion : p_strGameVersion + ".0");
            return GetScriptExtenderVersion() > verCompare;
        }

        /// <summary>
        /// Determines if the script extender is installed.
        /// </summary>
        /// <returns><c>true</c> if the script extender is installed;
        /// <c>false</c> otherwise.</returns>
        public bool ScriptExtenderPresent()
        {
            return GetScriptExtenderVersion() != null;
        }

        /// <summary>
        /// Gets the version of the script extender that is installed.
        /// </summary>
        /// <returns>The version of the script extender, or <c>null</c> if
        /// the script extender is not installed.</returns>
        public Version GetScriptExtenderVersion()
        {
			return ((StateOfDecayGameMode)GameMode).ScriptExtenderVersion;
        }

        #endregion
    }
}
