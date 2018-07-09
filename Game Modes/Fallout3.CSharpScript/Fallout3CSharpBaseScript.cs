using System;
using Nexus.Client.Games.Gamebryo.Tools.TESsnip;
using Nexus.Client.ModManagement.Scripting.CSharpScript;

namespace Nexus.Client.Games.Fallout3.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the Fallout 3 variant of C# scripts.
	/// </summary>
	public class Fallout3CSharpBaseScript : FalloutCSharpBaseScript
	{
		#region Version Checking

		/// <summary>
		/// Gets the version of FOSE that is installed.
		/// </summary>
		/// <returns>The version of FOSE, or <c>null</c> if FOSE
		/// is not installed.</returns>
		public static Version GetFoseVersion()
		{
			return ExecuteMethod(() => ((Fallout3CSharpScriptFunctionProxy)Functions).GetFoseVersion());
		}

		#endregion
	}
}
