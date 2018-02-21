using Nexus.Client.ModManagement.Scripting.ModScript;

namespace Nexus.Client.Games.Morrowind.Scripting.ModScript
{
    /// <summary>
    /// Interpets and executes the given Morrowind Mod Script script.
    /// </summary>
    public class MorrowindModScriptInterpreter : ModScriptInterpreter
    {
        #region Constructors

        /// <summary>
        /// A simple construtor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_msfFunctions">The object that implements the script functions.</param>
        /// <param name="p_strScript">The script to execute.</param>
        public MorrowindModScriptInterpreter(ModScriptFunctionProxy p_msfFunctions, string p_strScript)
            : base(p_msfFunctions, p_strScript)
        {
        }

        #endregion

        /// <summary>
        /// Creates the context object that tracks the state of the script being executed.
        /// </summary>
        /// <param name="p_msfFunctions">The object that implements the script functions.</param>
        /// <returns>The context object to use to track the state of the script being executed.</returns>
        protected override ModScriptInterpreterContext CreateInterpreterContext(ModScriptFunctionProxy p_msfFunctions)
        {
            return new MorrowindModScriptInterpreterContext(p_msfFunctions);
        }
    }
}
