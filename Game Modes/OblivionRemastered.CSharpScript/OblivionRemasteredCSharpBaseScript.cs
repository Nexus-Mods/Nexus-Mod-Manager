using System;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;

namespace Nexus.Client.Games.OblivionRemastered.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the OblivionRemastered variant of C# scripts.
	/// </summary>
	public class OblivionRemasteredCSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of SKSE that is installed.
		/// </summary>
		/// <returns>The version of SKSE, or <c>null</c> if SKSE
		/// is not installed.</returns>
		public static Version GetSkseVersion()
		{
			return ExecuteMethod(() => ((OblivionRemasteredCSharpScriptFunctionProxy)Functions).GetSkseVersion());
		}

		#endregion
	}
}
