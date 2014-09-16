using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Gamebryo.ModManagement;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.Scripting.ModScript;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.Morrowind.Scripting.ModScript
{
    /// <summary>
    /// Implements the functions availabe to the Morrowind variant of Mod Script scripts.
    /// </summary>
    /// <remarks>
    /// The proxy allows sandboxed scripts to call functions that can perform
    /// actions outside of the sandbox.
    /// 
    /// All values in Mod Script are strings. Thus, all parameters to the functions
    /// are strings. If they represent another datatype, the functions must
    /// convert the values before using them.
    /// 
    /// There are a few exceptions, where some methods accept non-string parameters.
    /// This is becuase these methods are called by the interpreter, and not from
    /// the interpreted code.
    /// </remarks>
    public class MorrowindModScriptFunctionProxy : ModScriptFunctionProxy
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_modMod">The mod for which the script is running.</param>
        /// <param name="p_gmdGameMode">The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        /// <param name="p_igpInstallers">The utility class to use to install the mod items.</param>
        /// <param name="p_uipUIProxy">The UI manager to use to interact with UI elements.</param>
		public MorrowindModScriptFunctionProxy(IMod p_modMod, IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo, IVirtualModActivator p_ivaVirtualModActivator, InstallerGroup p_igpInstallers, ModScriptUIUtil p_uipUIProxy)
            : base(p_modMod, p_gmdGameMode, p_eifEnvironmentInfo, p_ivaVirtualModActivator, p_igpInstallers, p_uipUIProxy)
        {
        }

        #endregion

        #region Version Checking

        /// <summary>
        /// Determines if the current game version is greater than the given version.
        /// </summary>
        /// <remarks>
        /// This is an alis for <see cref="ModScriptFunctionProxy.GameNewerThan(string)"/>. It exists
        /// to maintain compatibility with old Mod Scripts.
        /// </remarks>
        /// <param name="p_strGameVersion">The version to which to compare the game's version.</param>
        /// <returns><c>true</c> if the game's version is greater than the given version.</returns>
        /// <seealso cref="ModScriptFunctionProxy.GameNewerThan(string)"/>
        public bool MorrowindNewerThan(string p_strGameVersion)
        {
            return GameNewerThan(p_strGameVersion);
        }

        /// <summary>
        /// Determines if the installed script extender version is greater than the given version.
        /// </summary>
        /// <param name="p_strGameVersion">The version to which to compare the script extender's version.</param>
        /// <returns><c>true</c> if the script extender's version is greater than the given version.</returns>
        public bool ScriptExtenderNewerThan(string p_strGameVersion)
        {
            Version verCompare = new Version(p_strGameVersion.Contains(".") ? p_strGameVersion : p_strGameVersion + ".0");
            return GetScriptExtenderVersion() > verCompare;
        }

        /// <summary>
        /// Determines if the script extender is installed.
        /// </summary>
        /// <returns><c>true</c> if the script extender is installed;
        /// <c>false</c> otherwise.</returns>
        public bool ScriptExtenderPresent()
        {
            return GetScriptExtenderVersion() != null;
        }

        /// <summary>
        /// Gets the version of the script extender that is installed.
        /// </summary>
        /// <returns>The version of the script extender, or <c>null</c> if
        /// the script extender is not installed.</returns>
        public Version GetScriptExtenderVersion()
        {
            return ((MorrowindGameMode)GameMode).ScriptExtenderVersion;
        }

        #endregion

        #region Ini Editing

        /// <summary>
        /// Sets the specified value in the Morrowind Ini file to the given value.
        /// </summary>
        /// <param name="p_strSection">The section in the Ini file to edit.</param>
        /// <param name="p_strKey">The key in the Ini file to edit.</param>
        /// <param name="p_strValue">The value to which to set the key.</param>
        /// <returns><c>true</c> if the value was set; <c>false</c>
        /// if the user chose not to overwrite the existing value.</returns>
        public bool EditINI(string p_strSection, string p_strKey, string p_strValue)
        {
            return Installers.IniInstaller.EditIni(((GamebryoGameModeBase)GameMode).SettingsFiles.IniPath, p_strSection, p_strKey, p_strValue);
        }

        #endregion

        #region BSA Management

        /// <summary>
        /// Gets the list of BSA files in the INI file.
        /// </summary>
        /// <returns>The list of BSA files in the INI file.</returns>
        private List<string> GetBSAList()
        {
            string strIniPath = ((MorrowindGameMode)GameMode).SettingsFiles.IniPath;
            List<string> lstBsas = new List<string>(IniMethods.GetPrivateProfileString("Archive", "SArchiveList", null, strIniPath).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            for (int i = 0; i < lstBsas.Count; i++)
                lstBsas[i] = lstBsas[i].Trim(' ');
            return lstBsas;
        }

        /// <summary>
        /// Registers the BSA in the game's BSA list.
        /// </summary>
        /// <param name="p_strBsaPath">The BSA to register.</param>
        public void RegisterBSA(string p_strBsaPath)
        {
            List<string> lstBsas = GetBSAList();
            string strFixedPath = GameMode.GetModFormatAdjustedPath(Mod.Format, p_strBsaPath, true);
            if (lstBsas.Contains(strFixedPath, StringComparer.OrdinalIgnoreCase))
                return;
            lstBsas.Add(strFixedPath);
            string strIniPath = ((MorrowindGameMode)GameMode).SettingsFiles.IniPath;
            IniMethods.WritePrivateProfileString("Archive", "SArchiveList", String.Join(", ", lstBsas.ToArray()), strIniPath);
        }

        /// <summary>
        /// Unregisters the BSA in the game's BSA list.
        /// </summary>
        /// <param name="p_strBsaPath">The BSA to unregister.</param>
        public void UnregisterBSA(string p_strBsaPath)
        {
            List<string> lstBsas = GetBSAList();
            string strFixedPath = GameMode.GetModFormatAdjustedPath(Mod.Format, p_strBsaPath, true);
            Int32 intIndex = lstBsas.IndexOf(strFixedPath, StringComparer.OrdinalIgnoreCase);
            if (intIndex < 0)
                return;
            lstBsas.RemoveAt(intIndex);
            string strIniPath = ((MorrowindGameMode)GameMode).SettingsFiles.IniPath;
            IniMethods.WritePrivateProfileString("Archive", "SArchiveList", String.Join(", ", lstBsas.ToArray()), strIniPath);
        }

        #endregion

        #region Shader Editing

        /// <summary>
        /// Edits the specified shader with the specified data.
        /// </summary>
        /// <param name="p_intPackage">The package containing the shader to edit.</param>
        /// <param name="p_strShaderName">The shader to edit.</param>
        /// <param name="p_strNewDataFilePath">The path to the mod file containing the value to which to edit the shader.</param>
        /// <returns><c>true</c> if the value was set; <c>false</c>
        /// if the user chose not to overwrite the existing value.</returns>
        public bool EditShader(string p_intPackage, string p_strShaderName, string p_strNewDataFilePath)
        {
            Int32 intPackage = Int32.Parse(p_intPackage);
            byte[] bteData = GetFileFromMod(p_strNewDataFilePath);
            GamebryoGameSpecificValueInstaller.ShaderEdit sedShader = new GamebryoGameSpecificValueInstaller.ShaderEdit(intPackage, p_strShaderName);
            return Installers.GameSpecificValueInstaller.EditGameSpecificValue(sedShader.ToString(), bteData);
        }

        #endregion

        #region Renderer Info

        /// <summary>
        /// Reads the specified value from the renderer info file.
        /// </summary>
        /// <param name="p_strRendererValue">The key of the value to return.</param>
        /// <returns>The specified renderer info value.</returns>
        public string ReadRendererInfo(string p_strRendererValue)
        {
            string[] strRendererInfo = File.ReadAllLines(((GamebryoGameModeBase)GameMode).SettingsFiles.RendererFilePath);
            for (Int32 i = 0; i < strRendererInfo.Length; i++)
            {
                string[] strLine = strRendererInfo[i].Split(':');
                if (strLine[0].Trim().Equals(p_strRendererValue, StringComparison.CurrentCultureIgnoreCase))
                {
                    if (strLine.Length < 2)
                        return null;
                    return String.Join(":", strLine, 1, strLine.Length - 1);
                }
            }
            return null;
        }

        #endregion

        #region Obsolete/Ignored

        #region Conflict

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod with which the mod being isntalled conflicts.</param>
        public void ConflictsWith(string p_strModName)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod with which the mod being isntalled conflicts.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void ConflictsWith(string p_strModName, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod with which the mod being isntalled conflicts.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        /// <param name="p_strLevel">How severe the mod conflict is.</param>
        public void ConflictsWith(string p_strModName, string p_strComment, string p_strLevel)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod with which the mod being isntalled conflicts.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        public void ConflictsWith(string p_strModName, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod with which the mod being isntalled conflicts.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void ConflictsWith(string p_strModName, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod with which the mod being isntalled conflicts.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        /// <param name="p_strLevel">How severe the mod conflict is.</param>
        public void ConflictsWith(string p_strModName, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion, string p_strComment, string p_strLevel)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods with which the mod being isntalled conflicts.</param>
        public void ConflictsWithRegex(string p_strModNameRegex)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods with which the mod being isntalled conflicts.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void ConflictsWithRegex(string p_strModNameRegex, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods with which the mod being isntalled conflicts.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        /// <param name="p_strLevel">How severe the mod conflict is.</param>
        public void ConflictsWithRegex(string p_strModNameRegex, string p_strComment, string p_strLevel)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods with which the mod being isntalled conflicts.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        public void ConflictsWithRegex(string p_strModNameRegex, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods with which the mod being isntalled conflicts.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void ConflictsWithRegex(string p_strModNameRegex, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed conflicts with the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a conflict message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods with which the mod being isntalled conflicts.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod with which the mod
        /// being installed conflicts. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        /// <param name="p_strLevel">How severe the mod conflict is.</param>
        public void ConflictsWithRegex(string p_strModNameRegex, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion, string p_strComment, string p_strLevel)
        {
        }

        #endregion

        #region Dependency

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod on which the mod being isntalled depends.</param>
        public void DependsOn(string p_strModName)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod on which the mod being isntalled depends.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void DependsOn(string p_strModName, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod on which the mod being isntalled depends.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        public void DependsOn(string p_strModName, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mod.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModName">The exact name of the mod on which the mod being isntalled depends.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void DependsOn(string p_strModName, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods on which the mod being isntalled depends.</param>
        public void DependsOnRegex(string p_strModNameRegex)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods on which the mod being isntalled depends.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void DependsOnRegex(string p_strModNameRegex, string p_strComment)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods on which the mod being isntalled depends.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        public void DependsOnRegex(string p_strModNameRegex, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion)
        {
        }

        /// <summary>
        /// Indicates that the mod being installed depends on the specified mods.
        /// </summary>
        /// <remarks>
        /// This has no effect on the running of the script, but displays a message in the mod manager's conflict report.
        /// 
        /// This method is ignored, as the mod manager no longer has a conflict report.
        /// </remarks>
        /// <param name="p_strModNameRegex">The regex pattern matching the names of the mods on which the mod being isntalled depends.</param>
        /// <param name="p_strMinMajorVersion">The major version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMinMinorVersion">The minor version number of the minimum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMinMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMajorVersion">The major version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMinorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strMaxMinorVersion">The minor version number of the maximum version of the mod on which the mod
        /// being installed depends. Ignored if <c>0</c> and <paramref name="p_strMaxMajorVersion"/> is also <c>0</c>.</param>
        /// <param name="p_strComment">The comment to display in the conflict report.</param>
        public void DependsOnRegex(string p_strModNameRegex, string p_strMinMajorVersion, string p_strMinMinorVersion, string p_strMaxMajorVersion, string p_strMaxMinorVersion, string p_strComment)
        {
        }

        #endregion

        #endregion
    }
}
