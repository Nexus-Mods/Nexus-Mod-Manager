namespace Nexus.Client.ModManagement.InstallationLog
{
    using System.Collections.Generic;

    using Nexus.Client.Mods;
    using Nexus.Client.Util.Collections;

	/// <summary>
	/// Describes the properties and methods of an install log.
	/// </summary>
	/// <remarks>
	/// An install log tracks items that are installed by mods.
	/// </remarks>
	public interface IInstallLog
	{
		#region Properties

		/// <summary>
		/// Gets the key of the mod representing original values.
		/// </summary>
		/// <remarks>
		/// Original values are values that were preexisting on the system.
		/// </remarks>
		/// <value>The key of the mod representing original values.</value>
		string OriginalValuesKey { get; }

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		ReadOnlyObservableList<IMod> ActiveMods { get; }

		#endregion

		#region Singleton

		/// <summary>
		/// Releases the log's hold on physical resources.
		/// </summary>
		void Release();

		#endregion

		#region Mod Tracking

		/// <summary>
		/// Adds a mod to the install log.
		/// </summary>
		/// <remarks>
		/// Adding a mod to the install log assigns it a key. Keys are used to track file and
		/// edit versions.
		/// 
		/// If there is no current transaction, the mod is added directly to the install log. Otherwise,
		/// the mod is added to a buffer than can later be committed or rolled back.
		/// </remarks>
		/// <param name="mod">The <see cref="IMod"/> being added.</param>
		void AddActiveMod(IMod mod);

		IInstallLog ReInitialize(string logPath);

		/// <summary>
		/// Replaces a mod in the install log, in a transaction.
		/// </summary>
		/// <remarks>
		/// This replaces a mod in the install log without changing its key.
		/// </remarks>
		/// <param name="oldMod">The mod with to be replaced with the new mod in the install log.</param>
		/// <param name="newMod">The mod with which to replace the old mod in the install log.</param>
		void ReplaceActiveMod(IMod oldMod, IMod newMod);

		/// <summary>
		/// Gets the key that was assigned to the specified mod.
		/// </summary>
		/// <param name="mod">The mod whose key is to be retrieved.</param>
		/// <returns>The key that was assigned to the specified mod, or <c>null</c> if
		/// the specified mod has no key.</returns>
		string GetModKey(IMod mod);

		/// <summary>
		/// Gets the list of mods whose versions don't match the version in the install log.
		/// </summary>
		/// <returns>The list of mods whose versions don't match the version in the install log.
		/// The key is the mod info stored in the install log. The value is the actual mod
		/// registered with the mod manager.</returns>
		IEnumerable<KeyValuePair<IMod, IMod>> GetMismatchedVersionMods();

		#endregion

		#region Uninstall

		/// <summary>
		/// Removes the mod, as well as entries for items installed by the given mod,
		/// from the install log.
		/// </summary>
		/// <param name="modToRemove">The mod to remove.</param>
		void RemoveMod(IMod modToRemove);

		#endregion

		#region File Version Management

		/// <summary>
		/// Logs the specified data file as having been installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod installing the specified data file.</param>
		/// <param name="dataFilePath">The file being installed.</param>
		void AddDataFile(IMod installingMod, string dataFilePath);

		/// <summary>
		/// Removes the specified data file as having been installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod for which to remove the specified data file.</param>
		/// <param name="dataFilePath">The file being removed for the given mod.</param>
		void RemoveDataFile(IMod installingMod, string dataFilePath);

		/// <summary>
		/// Gets the mod that owns the specified file.
		/// </summary>
		/// <param name="path">The path of the file whose owner is to be retrieved.</param>
		/// <returns>The mod that owns the specified file.</returns>
		IMod GetCurrentFileOwner(string path);

		/// <summary>
		/// Gets the mod that owned the specified file prior to the current owner.
		/// </summary>
		/// <param name="path">The path of the file whose previous owner is to be retrieved.</param>
		/// <returns>The mod that owned the specified file prior to the current owner.</returns>
		IMod GetPreviousFileOwner(string path);

		/// <summary>
		/// Gets the key of the mod that owns the specified file.
		/// </summary>
		/// <param name="path">The path of the file whose owner is to be retrieved.</param>
		/// <returns>The key of the mod that owns the specified file.</returns>
		string GetCurrentFileOwnerKey(string path);

		/// <summary>
		/// Gets the key of the mod that owned the specified file prior to the current owner.
		/// </summary>
		/// <param name="path">The path of the file whose previous owner is to be retrieved.</param>
		/// <returns>The key of the mod that owned the specified file prior to the current owner.</returns>
		string GetPreviousFileOwnerKey(string path);

		/// <summary>
		/// Logs that the specified data file is an original value.
		/// </summary>
		/// <remarks>
		/// Logging an original data file prepares it to be overwritten by a mod's file.
		/// </remarks>
		/// <param name="dataFilePath">The path of the data file to log as an
		/// original value.</param>
		void LogOriginalDataFile(string dataFilePath);

		/// <summary>
		/// Gets the list of files that were installed by the given mod.
		/// </summary>
		/// <param name="installer">The mod whose installed files are to be returned.</param>
		/// <returns>The list of files that were installed by the given mod.</returns>
		IList<string> GetInstalledModFiles(IMod installer);

		/// <summary>
		/// Gets all of the mods that have installed the specified file.
		/// </summary>
		/// <param name="path">The path of the file whose installers are to be retrieved.</param>
		/// <returns>All of the mods that have installed the specified file.</returns>
		IList<IMod> GetFileInstallers(string path);

		#endregion

		#region INI Version Management

		/// <summary>
		/// Logs the specified INI edit as having been installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod installing the specified INI edit.</param>
		/// <param name="settingsFileName">The name of the edited INI file.</param>
		/// <param name="section">The section containing the INI edit.</param>
		/// <param name="key">The key of the edited INI value.</param>
		/// <param name="value">The value installed by the mod.</param>
		void AddIniEdit(IMod installingMod, string settingsFileName, string section, string key, string value);

		/// <summary>
		/// Replaces the edited value of the specified INI edit installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod whose INI edit value is to be replaced.</param>
		/// <param name="settingsFileName">The name of the Ini value whose edited value is to be replaced.</param>
		/// <param name="section">The section of the Ini value whose edited value is to be replaced.</param>
		/// <param name="key">The key of the Ini value whose edited value is to be replaced.</param>
		/// <param name="value">The value with which to replace the edited value of the specified INI edit installed by the given mod.</param>
		void ReplaceIniEdit(IMod installingMod, string settingsFileName, string section, string key, string value);

		/// <summary>
		/// Removes the specified ini edit as having been installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod for which to remove the specified ini edit.</param>
		/// <param name="settingsFileName">The name of the edited INI file containing the INI edit being removed for the given mod.</param>
		/// <param name="section">The section containing the INI edit being removed for the given mod.</param>
		/// <param name="key">The key of the edited INI value whose edit is being removed for the given mod.</param>
		void RemoveIniEdit(IMod installingMod, string settingsFileName, string section, string key);

		/// <summary>
		/// Gets the mod that owns the specified INI edit.
		/// </summary>
		/// <param name="settingsFileName">The name of the edited INI file.</param>
		/// <param name="section">The section containing the INI edit.</param>
		/// <param name="key">The key of the edited INI value.</param>
		/// <returns>The mod that owns the specified INI edit.</returns>
		IMod GetCurrentIniEditOwner(string settingsFileName, string section, string key);

		/// <summary>
		/// Gets the key of the mod that owns the specified INI edit.
		/// </summary>
		/// <param name="settingsFileName">The name of the edited INI file.</param>
		/// <param name="section">The section containing the INI edit.</param>
		/// <param name="key">The key of the edited INI value.</param>
		/// <returns>The key of the mod that owns the specified INI edit.</returns>
		string GetCurrentIniEditOwnerKey(string settingsFileName, string section, string key);

		/// <summary>
		/// Gets the value of the specified key before it was most recently overwritten.
		/// </summary>
		/// <param name="settingsFileName">The Ini file containing the key whose previous value is to be retrieved.</param>
		/// <param name="section">The section containing the key whose previous value is to be retrieved.</param>
		/// <param name="key">The key whose previous value is to be retrieved.</param>
		/// <returns>The value of the specified key before it was most recently overwritten, or
		/// <c>null</c> if there was no previous value.</returns>
		string GetPreviousIniValue(string settingsFileName, string section, string key);

		/// <summary>
		/// Logs that the specified INI value is an original value.
		/// </summary>
		/// <remarks>
		/// Logging an original INI value prepares it to be overwritten by a mod's value.
		/// </remarks>
		/// <param name="settingsFileName">The name of the INI file containing the original value to log.</param>
		/// <param name="section">The section containing the original INI value to log.</param>
		/// <param name="key">The key of the original INI value to log.</param>
		/// <param name="value">The original value.</param>
		void LogOriginalIniValue(string settingsFileName, string section, string key, string value);

		/// <summary>
		/// Gets the list of INI edits that were installed by the given mod.
		/// </summary>
		/// <param name="installer">The mod whose installed edits are to be returned.</param>
		/// <returns>The list of edits that was installed by the given mod.</returns>
		IList<IniEdit> GetInstalledIniEdits(IMod installer);

		/// <summary>
		/// Gets all of the mods that have installed the specified Ini edit.
		/// </summary>
		/// <param name="settingsFileName">The Ini file containing the key whose installers are to be retrieved.</param>
		/// <param name="section">The section containing the key whose installers are to be retrieved.</param>
		/// <param name="key">The key whose installers are to be retrieved.</param>
		/// <returns>All of the mods that have installed the specified Ini edit.</returns>
		IList<IMod> GetIniEditInstallers(string settingsFileName, string section, string key);

		#endregion

		#region Game Specific Value Version Management

		/// <summary>
		/// Logs the specified Game Specific Value edit as having been installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod installing the specified INI edit.</param>
		/// <param name="key">The key of the edited Game Specific Value.</param>
		/// <param name="value">The value installed by the mod.</param>
		void AddGameSpecificValueEdit(IMod installingMod, string key, byte[] value);

		/// <summary>
		/// Replaces the edited value of the specified game specific value edit installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod whose game specific value edit value is to be replaced.</param>
		/// <param name="key">The key of the game specified value whose edited value is to be replaced.</param>
		/// <param name="value">The value with which to replace the edited value of the specified game specific value edit installed by the given mod.</param>
		void ReplaceGameSpecificValueEdit(IMod installingMod, string key, byte[] value);

		/// <summary>
		/// Removes the specified Game Specific Value edit as having been installed by the given mod.
		/// </summary>
		/// <param name="installingMod">The mod for which to remove the specified Game Specific Value edit.</param>
		/// <param name="key">The key of the Game Specific Value whose edit is being removed for the given mod.</param>
		void RemoveGameSpecificValueEdit(IMod installingMod, string key);

		/// <summary>
		/// Gets the mod that owns the specified Game Specific Value edit.
		/// </summary>
		/// <param name="key">The key of the edited Game Specific Value.</param>
		/// <returns>The mod that owns the specified Game Specific Value edit.</returns>
		IMod GetCurrentGameSpecificValueEditOwner(string key);

		/// <summary>
		/// Gets the key of the mod that owns the specified Game Specific Value edit.
		/// </summary>
		/// <param name="key">The key of the edited Game Specific Value.</param>
		/// <returns>The key of the mod that owns the specified Game Specific Value edit.</returns>
		string GetCurrentGameSpecificValueEditOwnerKey(string key);

		/// <summary>
		/// Gets the value of the specified key before it was most recently overwritten.
		/// </summary>
		/// <param name="key">The key of the edited Game Specific Value.</param>
		/// <returns>The value of the specified key before it was most recently overwritten, or
		/// <c>null</c> if there was no previous value.</returns>
		byte[] GetPreviousGameSpecificValue(string key);

		/// <summary>
		/// Logs that the specified Game Specific Value is an original value.
		/// </summary>
		/// <remarks>
		/// Logging an original Game Specific Value prepares it to be overwritten by a mod's value.
		/// </remarks>
		/// <param name="key">The key of the edited Game Specific Value.</param>
		/// <param name="value">The original value.</param>
		void LogOriginalGameSpecificValue(string key, byte[] value);

		/// <summary>
		/// Gets the list of Game Specific Value edited keys that were installed by the given mod.
		/// </summary>
		/// <param name="installer">The mod whose installed edits are to be returned.</param>
		/// <returns>The list of edited keys that was installed by the given mod.</returns>
		IList<string> GetInstalledGameSpecificValueEdits(IMod installer);

		/// <summary>
		/// Gets all of the mods that have installed the specified game specific value edit.
		/// </summary>
		/// <param name="key">The key whose installers are to be retrieved.</param>
		/// <returns>All of the mods that have installed the specified game specific value edit.</returns>
		IList<IMod> GetGameSpecificValueEditInstallers(string key);

		#endregion

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		byte[] GetXmlModList();
		
		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
        byte[] GetXmlIniList();

		/// <summary>
		/// This backups the install log.
		/// </summary>
		void Backup();
	}
}
