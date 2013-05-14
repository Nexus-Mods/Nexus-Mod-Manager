using Nexus.Client.ModManagement.Scripting.ModScript;

namespace Nexus.Client.Games.Witcher2.Scripting.ModScript
{
    /// <summary>
    /// Provides the function context to use when executing Grimrock Mod Script scripts.
    /// </summary>
    public class Witcher2ModScriptInterpreterContext : ModScriptInterpreterContext
    {
        #region Constructors

        /// <summary>
        /// A simple construtor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_msfFunctions">The object that proxies the script function calls
        /// out of the sandbox.</param>
		public Witcher2ModScriptInterpreterContext(ModScriptFunctionProxy p_msfFunctions)
            : base(p_msfFunctions)
        {
        }

        #endregion
    }
}
