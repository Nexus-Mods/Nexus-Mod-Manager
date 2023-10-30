using System;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;

namespace Nexus.Client.Games.Starfield.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the Starfield variant of C# scripts.
	/// </summary>
	public class StarfieldCSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of SKSE that is installed.
		/// </summary>
		/// <returns>The version of SKSE, or <c>null</c> if SKSE
		/// is not installed.</returns>
		public static Version GetSkseVersion()
		{
			return ExecuteMethod(() => ((StarfieldCSharpScriptFunctionProxy)Functions).GetSkseVersion());
		}

		#endregion
	}
}
