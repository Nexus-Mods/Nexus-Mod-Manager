using Nexus.Client.ModManagement.Scripting.ModScript;

namespace Nexus.Client.Games.DragonAge.Scripting.ModScript
{
    /// <summary>
	/// Provides the function context to use when executing DragonAge Mod Script scripts.
    /// </summary>
    public class DragonAgeModScriptInterpreterContext : ModScriptInterpreterContext
    {
        #region Constructors

        /// <summary>
        /// A simple construtor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_msfFunctions">The object that proxies the script function calls
        /// out of the sandbox.</param>
		public DragonAgeModScriptInterpreterContext(ModScriptFunctionProxy p_msfFunctions)
            : base(p_msfFunctions)
        {
        }

        #endregion
    }
}
