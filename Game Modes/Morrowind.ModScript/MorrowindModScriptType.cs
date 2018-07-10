using System;
using System.Threading;
using Nexus.Client.Games.Gamebryo.ModManagement.Scripting;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.ModScript;
using Nexus.Client.Mods;

namespace Nexus.Client.Games.Morrowind.Scripting.ModScript
{
    /// <summary>
    /// Describes the Morrowind variant of the Mod Script type.
    /// </summary>
    public class MorrowindModScriptType : ModScriptType
    {
        /// <summary>
        /// Returns a proxy that implements the functions available to Mod Script scripts.
        /// </summary>
        /// <param name="p_modMod">The mod being installed.</param>
        /// <param name="p_gmdGameMode">The game mode currently bieng managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        /// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
        /// <param name="p_scxUIContext">The <see cref="SynchronizationContext"/> to use to marshall UI interactions to the UI thread.</param>
        /// <returns>A proxy that implements the functions available to Mod Script scripts.</returns>
		protected override ModScriptFunctionProxy GetScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, SynchronizationContext p_scxUIContext)
        {
            return new MorrowindModScriptFunctionProxy(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_igpInstallers, new ModScriptUIUtil(p_gmdGameMode, p_eifEnvironmentInfo, p_scxUIContext));
        }
    }
}
