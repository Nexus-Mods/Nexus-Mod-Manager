using System;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;

namespace Nexus.Client.Games.SkyrimSE.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the SkyrimSE variant of C# scripts.
	/// </summary>
	public class SkyrimSECSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of SKSE that is installed.
		/// </summary>
		/// <returns>The version of SKSE, or <c>null</c> if SKSE
		/// is not installed.</returns>
		public static Version GetSkseVersion()
		{
			return ExecuteMethod(() => ((SkyrimSECSharpScriptFunctionProxy)Functions).GetSkseVersion());
		}

		#endregion
	}
}
