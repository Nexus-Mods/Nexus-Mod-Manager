using System;
using Nexus.Client.Games.Gamebryo.Tools.TESsnip;
using Nexus.Client.ModManagement.Scripting.CSharpScript;

namespace Nexus.Client.Games.Fallout3.Scripting.CSharpScript
{
	/// <summary>
	/// The base class for the Fallout variant of C# scripts.
	/// </summary>
	public abstract class FalloutCSharpBaseScript : CSharpBaseScript
	{
		#region File Management

		/// <summary>
		/// Installs the specified file from the mod to the specified location on the file system.
		/// </summary>
		/// <param name="p_strFrom">The path of the file in the mod to install.</param>
		/// <param name="p_strTo">The path on the file system where the file is to be created.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public static bool InstallFileFromFomod(string p_strFrom, string p_strTo)
		{
			return InstallFileFromMod(p_strFrom, p_strTo);
		}

		/// <summary>
		/// Installs the speified file from the mod to the file system.
		/// </summary>
		/// <param name="p_strFile">The path of the file to install.</param>
		/// <returns><c>true</c> if the file was written; <c>false</c> otherwise.</returns>
		public static bool InstallFileFromFomod(string p_strFile)
		{
			return InstallFileFromMod(p_strFile, p_strFile);
		}

		/// <summary>
		/// Retrieves the specified file from the mod.
		/// </summary>
		/// <param name="p_strFile">The file to retrieve.</param>
		/// <returns>The requested file data.</returns>
		public static byte[] GetFileFromFomod(string p_strFile)
		{
			return GetFileFromMod(p_strFile);
		}

		/// <summary>
		/// Retrieves the list of files in the mod.
		/// </summary>
		/// <returns>The list of files in the mod.</returns>
		public static string[] GetFomodFileList()
		{
			return GetModFileList();
		}

		#endregion

		#region Version Checking

		/// <summary>
		/// Gets the version of the mod manager.
		/// </summary>
		/// <returns>The version of the mod manager.</returns>
		public static Version GetFommVersion()
		{
			return GetModManagerVersion();
		}

		/// <summary>
		/// Gets the version of the game that is installed.
		/// </summary>
		/// <returns>The version of the game, or <c>null</c> if Fallout
		/// is not installed.</returns>
		public static Version GetFalloutVersion()
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetFalloutVersion());
		}

		/// <summary>
		/// Indicates whether or not a script extender is present.
		/// </summary>
		/// <returns><c>true</c> if a script extender installed; <c>false</c> otherwise.</returns>
		public static bool ScriptExtenderPresent()
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).ScriptExtenderPresent());
		}

		#endregion

		#region BSA Management

		/// <summary>
		/// Retrieves the list of files in the specified BSA.
		/// </summary>
		/// <param name="p_strBsa">The BSA whose file listing is requested.</param>
		/// <returns>The list of files contained in the specified BSA.</returns>
		public static string[] GetBSAFileList(string p_strBsa)
		{

			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetBSAFileList(p_strBsa));
		}

		/// <summary>
		/// Gets the specified file from the specified BSA.
		/// </summary>
		/// <param name="p_strBsa">The BSA from which to extract the specified file.</param>
		/// <param name="p_strFile">The files to extract form the specified BSA.</param>
		/// <returns>The data of the specified file.</returns>
		public static byte[] GetDataFileFromBSA(string p_strBsa, string p_strFile)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetDataFileFromBSA(p_strBsa, p_strFile));
		}

		#endregion

		#region Ini File Value Management

		#region Ini File Value Retrieval

		/// <summary>
		/// Retrieves the specified Fallout.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetFalloutIniInt(string, string)"/>
		public static string GetFalloutIniString(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetFalloutIniString(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified Fallout.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <seealso cref="GetFalloutIniString(string, string)"/>
		public static int GetFalloutIniInt(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetFalloutIniInt(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified FalloutPrefs.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetPrefsIniInt(string, string)"/>
		public static string GetPrefsIniString(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetPrefsIniString(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified FalloutPrefs.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		/// <seealso cref="GetPrefsIniString(string, string)"/>
		public static int GetPrefsIniInt(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetPrefsIniInt(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified GECKCustom.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetGeckIniInt(string, string)"/>
		public static string GetGeckIniString(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetGeckIniString(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified GECKCustom.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		/// <seealso cref="GetGeckIniString(string, string)"/>
		public static int GetGeckIniInt(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetGeckIniInt(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified GECKPrefs.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetGeckPrefsIniInt(string, string)"/>
		public static string GetGeckPrefsIniString(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetGeckPrefsIniString(p_strSection, p_strKey));
		}

		/// <summary>
		/// Retrieves the specified GECKPrefs.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		/// <seealso cref="GetGeckPrefsIniString(string, string)"/>
		public static int GetGeckPrefsIniInt(string p_strSection, string p_strKey)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetGeckPrefsIniInt(p_strSection, p_strKey));
		}

		#endregion

		#region Ini Editing

		/// <summary>
		/// Sets the specified value in the Fallout.ini file to the given value. 
		/// </summary>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <param name="p_booSaveOld">Not used.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public static bool EditFalloutINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).EditFalloutINI(p_strSection, p_strKey, p_strValue, p_booSaveOld));
		}

		/// <summary>
		/// Sets the specified value in the FalloutPrefs.ini file to the given value. 
		/// </summary>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <param name="p_booSaveOld">Not used.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public static bool EditPrefsINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).EditPrefsINI(p_strSection, p_strKey, p_strValue, p_booSaveOld));
		}

		/// <summary>
		/// Sets the specified value in the GECKCustom.ini file to the given value. 
		/// </summary>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <param name="p_booSaveOld">Not used.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public static bool EditGeckINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).EditGeckINI(p_strSection, p_strKey, p_strValue, p_booSaveOld));
		}

		/// <summary>
		/// Sets the specified value in the GECKPrefs.ini file to the given value. 
		/// </summary>
		/// <param name="p_strSection">The section in the Ini file to edit.</param>
		/// <param name="p_strKey">The key in the Ini file to edit.</param>
		/// <param name="p_strValue">The value to which to set the key.</param>
		/// <param name="p_booSaveOld">Not used.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public static bool EditGeckPrefsINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).EditGeckPrefsINI(p_strSection, p_strKey, p_strValue, p_booSaveOld));
		}

		#endregion

		#endregion

		#region Misc Info

		/// <summary>
		/// Gets the specified value from the RendererInfo.txt file.
		/// </summary>
		/// <param name="p_strValue">The value to retrieve from the file.</param>
		/// <returns>The specified value from the RendererInfo.txt file, or
		/// <c>null</c> if the value is not found.</returns>
		public static string GetRendererInfo(string p_strValue)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetRendererInfo(p_strValue));
		}

		/// <summary>
		/// Determines if archive invalidation is active.
		/// </summary>
		/// <returns><c>true</c> if archive invalidation is active;
		/// <c>false</c> otherwise.</returns>
		public static bool IsAIActive()
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).IsAIActive());
		}

		#endregion

		#region Shader Editing

		/// <summary>
		/// Edits the specified shader with the specified data.
		/// </summary>
		/// <param name="p_intPackage">The package containing the shader to edit.</param>
		/// <param name="p_strShaderName">The shader to edit.</param>
		/// <param name="p_bteData">The value to which to edit the shader.</param>
		/// <returns><c>true</c> if the value was set; <c>false</c>
		/// if the user chose not to overwrite the existing value.</returns>
		public static bool EditShader(int p_intPackage, string p_strShaderName, byte[] p_bteData)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).EditShader(p_intPackage, p_strShaderName, p_bteData));
		}

		#endregion

		#region Script Compilation

		/// <summary>
		/// Sets up the script compiler for the given plugins.
		/// </summary>
		/// <param name="p_plgPlugins">The plugins for which to set up the script compiler.</param>
		public static void SetupScriptCompiler(TesPlugin[] p_plgPlugins)
		{
			TesPlugin[] tspPlugins = new TesPlugin[p_plgPlugins.Length];
			for (int i = 0; i < p_plgPlugins.Length; i++)
				tspPlugins[i] = p_plgPlugins[i];
			ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).SetupScriptCompiler(tspPlugins));
		}

		/// <summary>
		/// Compiles the result script.
		/// </summary>
		public static void CompileResultScript(SubRecord sr, out Record r2, out string msg)
		{
			Record r;
			try
			{
				((FalloutCSharpScriptFunctionProxy)Functions).CompileResultScript(sr, out r, out msg);
			}
			catch (Exception e)
			{
				LastError = e.Message;
				r = null;
				msg = null;
			}
			if (r != null)
				r2 = (Record)r.Clone();
			else
				r2 = null;
		}

		/// <summary>
		/// Compiles a script.
		/// </summary>
		public static void CompileScript(Record r2, out string msg)
		{
			try
			{
				((FalloutCSharpScriptFunctionProxy)Functions).CompileScript(r2, out msg);
				r2.SubRecords.Clear();
				for (int i = 0; i < r2.SubRecords.Count; i++)
					r2.SubRecords.Add((SubRecord)r2.SubRecords[i].Clone());
			}
			catch (Exception e)
			{
				LastError = e.Message;
				msg = null;
			}
		}

		#endregion

		#region Plugin Management

		#region Load Order Management

		/// <summary>
		/// Determines if the plugins have been auto-sorted.
		/// </summary>
		/// <returns><c>true</c> if the plugins have been auto-sorted;
		/// <c>false</c> otherwise.</returns>
		public static bool IsLoadOrderAutoSorted()
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).IsLoadOrderAutoSorted());
		}

		/// <summary>
		/// Determins where in the load order the specified plugin would be inserted
		/// if the plugins were auto-sorted.
		/// </summary>
		/// <param name="p_strPlugin">The name of the plugin whose auto-sort insertion
		/// point is to be determined.</param>
		/// <returns>The index where the specified plugin would be inserted were the
		/// plugins to be auto-sorted.</returns>
		public static int GetAutoInsertionPoint(string p_strPlugin)
		{
			return ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).GetAutoInsertionPoint(p_strPlugin));
		}

		/// <summary>
		/// Auto-sorts the specified plugins.
		/// </summary>
		/// <remarks>
		/// This is, apparently, a beta function. Use with caution.
		/// </remarks>
		/// <param name="p_strPlugins">The list of plugins to auto-sort.</param>
		public static void AutoSortPlugins(string[] p_strPlugins)
		{
			ExecuteMethod(() => ((FalloutCSharpScriptFunctionProxy)Functions).AutoSortPlugins(p_strPlugins));
		}

		#endregion

		#endregion
	}
}
