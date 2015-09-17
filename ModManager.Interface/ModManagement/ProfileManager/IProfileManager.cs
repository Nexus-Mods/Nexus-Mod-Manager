﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	public interface IProfileManager
	{
		#region Properties

		/// <summary>
		/// Gets the list of profiles.
		/// </summary>
		/// <value>The list of profiles.</value>
		ThreadSafeObservableList<IModProfile> ModProfiles { get; }

		/// <summary>
		/// Gets the list of profiles.
		/// </summary>
		/// <value>The list of profiles.</value>
		bool Initialized { get; }

		#endregion

		#region Singleton

		/// <summary>
		/// Releases the manager's hold on physical resources.
		/// </summary>
		void Release();

		#endregion

		#region Profile Management

		/// <summary>
		/// Adds a profile to the profile manager.
		/// </summary>
		/// <remarks>
		/// Adding a profile to the profile manager assigns it a unique key.
		/// </remarks>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		IModProfile AddProfile(byte[] p_bteLoadOrder, string p_strGameModeId, string[] p_strOptionalFiles);

		/// <summary>
		/// Adds a profile to the profile manager.
		/// </summary>
		/// <remarks>
		/// Adding a profile to the profile manager assigns it a unique key.
		/// </remarks>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		IModProfile AddProfile(byte[] p_bteModList, byte[] p_bteIniEdits, byte[] p_bteLoadOrder, string p_strGameModeId, Int32 p_intModCount, string[] p_strOptionalFiles);

		/// <summary>
		/// Adds the backup profile to the profile manager.
		/// </summary>
		bool RestoreBackupProfile(string p_strGameModeId, out string p_strErrMessage);

		/// <summary>
		/// Creates and archives the profile backup.
		/// </summary>
		bool BackupProfile(byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string p_strGameModeId, Int32 p_intModCount, string[] p_strOptionalFiles);

		/// <summary>
		/// Updates the category file.
		/// </summary>
		void UpdateProfile(IModProfile p_impModProfile, byte[] p_bteIniEdits, byte[] p_bteLoadOrder, string[] p_strOptionalFiles);

		/// <summary>
		/// Updates the profile file.
		/// </summary>
		void UpdateProfileLoadOrder(IModProfile p_impModProfile, byte[] p_bteLoadOrder);

		bool LoadProfileFileList(IModProfile p_impModProfile);

		/// <summary>
		/// Removes the profile from the profile manager.
		/// </summary>
		/// <param name="p_impModProfile">The category to remove.</param>
		void RemoveProfile(IModProfile p_impModProfile);

		/// <summary>
		/// Updates the profile file.
		/// </summary>
		void LoadProfile(IModProfile p_impModProfile, out Dictionary<string, string> p_dicFileStream);

		/// <summary>
		/// Updates the profile file.
		/// </summary>
		void SaveProfile(IModProfile p_impModProfile, byte[] p_bteModList, byte[] p_bteIniEdits, byte[] p_bteLoadOrder, string[] p_strOptionalFiles);

		void ExportProfile(IModProfile p_impModProfile, string p_strPath);

		bool RestoreBackupProfile();

		IModProfile ImportProfile(string p_strPath);

		void PurgeModsFromProfiles(List<IMod> p_lstMods);

		string IsScriptedLogPresent(string p_strModFile);

		#endregion

		/// <summary>
		/// This backsup the profile manager.
		/// </summary>
		void Backup();
	}
}
