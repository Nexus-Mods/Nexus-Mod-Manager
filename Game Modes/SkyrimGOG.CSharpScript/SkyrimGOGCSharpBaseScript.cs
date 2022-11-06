using System;
using Nexus.Client.Games.Fallout3.Scripting.CSharpScript;

namespace Nexus.Client.Games.SkyrimGOG.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the SkyrimGOG variant of C# scripts.
	/// </summary>
	public class SkyrimGOGCSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of SKSE that is installed.
		/// </summary>
		/// <returns>The version of SKSE, or <c>null</c> if SKSE
		/// is not installed.</returns>
		public static Version GetSkseVersion()
		{
			return ExecuteMethod(() => ((SkyrimGOGCSharpScriptFunctionProxy)Functions).GetSkseVersion());
		}

		#endregion
	}
}
