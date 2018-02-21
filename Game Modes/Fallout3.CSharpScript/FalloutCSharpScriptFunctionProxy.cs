using System;
using System.IO;
using Nexus.Client.Games.Gamebryo.ModManagement;
using Nexus.Client.Games.Gamebryo.ModManagement.Scripting;
using Nexus.Client.Games.Gamebryo.Tools.TESsnip;
using Nexus.Client.Games.Gamebryo.Tools.TESsnip.ScriptCompiler;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.ModManagement.Scripting.CSharpScript;
using Nexus.Client.Mods;

namespace Nexus.Client.Games.Fallout3.Scripting.CSharpScript
{
	/// <summary>
	/// Implements the functions availabe to Fallout C# scripts.
	/// </summary>
	public abstract class FalloutCSharpScriptFunctionProxy : CSharpScriptFunctionProxy
	{
		#region Properties

		/// <summary>
		/// Gets the manager to use to work with BSA archives.
		/// </summary>
		/// <value>The manager to use to work with BSA archives.</value>
		public BsaManager BsaManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initialies the object with the given values.
		/// </summary>
		/// <param name="p_modMod">The mod for which the script is running.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
		/// <param name="p_bamBsaManager">The manager to use to work with BSA files.</param>
		/// <param name="p_uipUIProxy">The UI manager to use to interact with UI elements.</param>
		public FalloutCSharpScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, BsaManager p_bamBsaManager, UIUtil p_uipUIProxy)
			: base(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_igpInstallers, p_uipUIProxy)
		{
			BsaManager = p_bamBsaManager;
		}

		#endregion

		#region Version Checking

		/// <summary>
		/// Gets the version of the game that is installed.
		/// </summary>
		/// <returns>The version of the game, or <c>null</c> if Fallout
		/// is not installed.</returns>
		public Version GetFalloutVersion()
		{
			return GameMode.GameVersion;
		}

		/// <summary>
		/// Indicates whether or a script extender is present.
		/// </summary>
		/// <returns><c>true</c> if a script extender is installed; <c>false</c> otherwise.</returns>
		public bool ScriptExtenderPresent()
		{
			return ((Fallout3GameMode)GameMode).ScriptExtenderVersion != null;
		}

		#endregion

		#region BSA Management

		/// <summary>
		/// Retrieves the list of files in the specified BSA.
		/// </summary>
		/// <param name="p_strBsa">The BSA whose file listing is requested.</param>
		/// <returns>The list of files contained in the specified BSA.</returns>
		public string[] GetBSAFileList(string p_strBsa)
		{
			return BsaManager.GetBSAFileList(p_strBsa);
		}

		/// <summary>
		/// Gets the specified file from the specified BSA.
		/// </summary>
		/// <param name="p_strBsa">The BSA from which to extract the specified file.</param>
		/// <param name="p_strFile">The files to extract form the specified BSA.</param>
		/// <returns>The data of the specified file.</returns>
		public byte[] GetDataFileFromBSA(string p_strBsa, string p_strFile)
		{
			return BsaManager.GetDataFileFromBSA(p_strBsa, p_strFile);
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
		public string GetFalloutIniString(string p_strSection, string p_strKey)
		{
			return GetIniString(((Fallout3GameMode)GameMode).SettingsFiles.IniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified Fallout.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <seealso cref="GetFalloutIniString(string, string)"/>
		public int GetFalloutIniInt(string p_strSection, string p_strKey)
		{
			return GetIniInt(((Fallout3GameMode)GameMode).SettingsFiles.IniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified FalloutPrefs.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetPrefsIniInt(string, string)"/>
		public string GetPrefsIniString(string p_strSection, string p_strKey)
		{
			return GetIniString(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).FOPrefsIniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified FalloutPrefs.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		/// <seealso cref="GetPrefsIniString(string, string)"/>
		public int GetPrefsIniInt(string p_strSection, string p_strKey)
		{
			return GetIniInt(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).FOPrefsIniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified GECKCustom.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetGeckIniInt(string, string)"/>
		public string GetGeckIniString(string p_strSection, string p_strKey)
		{
			return GetIniString(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).GeckIniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified GECKCustom.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		/// <seealso cref="GetGeckIniString(string, string)"/>
		public int GetGeckIniInt(string p_strSection, string p_strKey)
		{
			return GetIniInt(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).GeckIniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified GECKPrefs.ini value as a string.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as a string.</returns>
		/// <seealso cref="GetGeckPrefsIniInt(string, string)"/>
		public string GetGeckPrefsIniString(string p_strSection, string p_strKey)
		{
			return GetIniString(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).GeckPrefsIniPath, p_strSection, p_strKey);
		}

		/// <summary>
		/// Retrieves the specified GECKPrefs.ini value as an integer.
		/// </summary>
		/// <param name="p_strSection">The section containing the value to retrieve.</param>
		/// <param name="p_strKey">The key of the value to retrieve.</param>
		/// <returns>The specified value as an integer.</returns>
		/// <seealso cref="GetGeckPrefsIniString(string, string)"/>
		public int GetGeckPrefsIniInt(string p_strSection, string p_strKey)
		{
			return GetIniInt(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).GeckPrefsIniPath, p_strSection, p_strKey);
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
		public bool EditFalloutINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return EditIni(((Fallout3GameMode)GameMode).SettingsFiles.IniPath, p_strSection, p_strKey, p_strValue);
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
		public bool EditPrefsINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return EditIni(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).FOPrefsIniPath, p_strSection, p_strKey, p_strValue);
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
		public bool EditGeckINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return EditIni(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).GeckIniPath, p_strSection, p_strKey, p_strValue);
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
		public bool EditGeckPrefsINI(string p_strSection, string p_strKey, string p_strValue, bool p_booSaveOld)
		{
			return EditIni(((FalloutSettingsFiles)((Fallout3GameMode)GameMode).SettingsFiles).GeckPrefsIniPath, p_strSection, p_strKey, p_strValue);
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
		public string GetRendererInfo(string p_strValue)
		{
			string[] strLines = File.ReadAllLines(((Fallout3GameMode)GameMode).SettingsFiles.RendererFilePath);
			for (int i = 1; i < strLines.Length; i++)
			{
				if (!strLines[i].Contains(":"))
					continue;
				string strCurrentValue = strLines[i].Remove(strLines[i].IndexOf(':')).Trim();
				if (strCurrentValue.Equals(p_strValue))
					return strLines[i].Substring(strLines[i].IndexOf(':') + 1).Trim();
			}
			return null;
		}

		/// <summary>
		/// Determines if archive invalidation is active.
		/// </summary>
		/// <returns><c>true</c> if archive invalidation is active;
		/// <c>false</c> otherwise.</returns>
		public bool IsAIActive()
		{
			//TODO change this when AI is implemented
			return false;
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
		public bool EditShader(int p_intPackage, string p_strShaderName, byte[] p_bteData)
		{
			GamebryoGameSpecificValueInstaller.ShaderEdit sedShader = new GamebryoGameSpecificValueInstaller.ShaderEdit(p_intPackage, p_strShaderName);
			return Installers.GameSpecificValueInstaller.EditGameSpecificValue(sedShader.ToString(), p_bteData);
		}

		#endregion

		#region Script Compilation

		/// <summary>
		/// Sets up the script compiler for the given plugins.
		/// </summary>
		/// <param name="p_plgPlugins">The plugins for which to set up the script compiler.</param>
		public void SetupScriptCompiler(TesPlugin[] p_plgPlugins)
		{
			ScriptCompiler.Setup(p_plgPlugins);
		}

		/// <summary>
		/// Compiles the result script.
		/// </summary>
		public void CompileResultScript(SubRecord sr, out Record r2, out string msg)
		{
			ScriptCompiler.CompileResultScript(sr, out r2, out msg);
		}

		/// <summary>
		/// Compiles a script.
		/// </summary>
		public void CompileScript(Record r2, out string msg)
		{
			ScriptCompiler.Compile(r2, out msg);
		}

		#endregion

		#region Load Order Management

		/// <summary>
		/// Determines if the plugins have been auto-sorted.
		/// </summary>
		/// <returns><c>true</c> if the plugins have been auto-sorted;
		/// <c>false</c> otherwise.</returns>
		public bool IsLoadOrderAutoSorted()
		{
			//TODO change when auto sorting is implemented
			return false;
		}

		/// <summary>
		/// Determins where in the load order the specified plugin would be inserted
		/// if the plugins were auto-sorted.
		/// </summary>
		/// <param name="p_strPlugin">The name of the plugin whose auto-sort insertion
		/// point is to be determined.</param>
		/// <returns>The index where the specified plugin would be inserted were the
		/// plugins to be auto-sorted.</returns>
		public int GetAutoInsertionPoint(string p_strPlugin)
		{
			//TODO change when auto sorting is implemented
			return Installers.PluginManager.ManagedPlugins.Count;
		}

		/// <summary>
		/// Auto-sorts the specified plugins.
		/// </summary>
		/// <remarks>
		/// This is, apparently, a beta function. Use with caution.
		/// </remarks>
		/// <param name="p_strPlugins">The list of plugins to auto-sort.</param>
		public void AutoSortPlugins(string[] p_strPlugins)
		{
			//TODO change when auto sorting is implemented
		}

		#endregion
	}
}
