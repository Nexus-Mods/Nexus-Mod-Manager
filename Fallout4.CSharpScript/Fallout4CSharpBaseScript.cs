using System;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;

namespace Nexus.Client.Games.Fallout4.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the Fallout4 variant of C# scripts.
	/// </summary>
	public class Fallout4CSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of SKSE that is installed.
		/// </summary>
		/// <returns>The version of SKSE, or <c>null</c> if SKSE
		/// is not installed.</returns>
		public static Version GetSkseVersion()
		{
			return ExecuteMethod(() => ((Fallout4CSharpScriptFunctionProxy)Functions).GetSkseVersion());
		}

		#endregion
	}
}
