using Nexus.Client.ModManagement.Scripting.ModScript;

namespace Nexus.Client.Games.Oblivion.Scripting.ModScript
{
	/// <summary>
	/// Provides the function context to use when executing Oblivion Mod Script scripts.
	/// </summary>
	public class OblivionModScriptInterpreterContext : ModScriptInterpreterContext
	{
		#region Constructors

		/// <summary>
		/// A simple construtor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_msfFunctions">The object that proxies the script function calls
		/// out of the sandbox.</param>
		public OblivionModScriptInterpreterContext(ModScriptFunctionProxy p_msfFunctions)
			: base(p_msfFunctions)
		{
		}

		#endregion

		#region Renderer Info

		/// <summary>
		/// Reads the specified value from the renderer info file and stores it in the specified variable.
		/// </summary>
		/// <param name="p_strVariableName">The name of the variable in which to store the value.</param>
		/// <param name="p_strRendererValue">The key of the value to return.</param>
		public void ReadRendererInfo(string p_strVariableName, string p_strRendererValue)
		{
			Variables[p_strVariableName] = ((OblivionModScriptFunctionProxy)FunctionProxy).ReadRendererInfo(p_strRendererValue);
		}

		#endregion
	}
}
