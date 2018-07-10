using System;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;

namespace Nexus.Client.Games.FalloutNV.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the Fallout: New Vegas variant of C# scripts.
	/// </summary>
	public class FalloutNVCSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of NVSE that is installed.
		/// </summary>
		/// <returns>The version of NVSE, or <c>null</c> if NVSE
		/// is not installed.</returns>
		public static Version GetNvseVersion()
		{
			return ExecuteMethod(() => ((FalloutNVCSharpScriptFunctionProxy)Functions).GetNvseVersion());
		}

		#endregion
	}
}
