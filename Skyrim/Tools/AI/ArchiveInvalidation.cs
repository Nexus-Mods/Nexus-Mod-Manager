using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Skyrim.Tools.AI
{
	/// <summary>
	/// Controls ArchiveInvalidation.
	/// </summary>
	public class ArchiveInvalidation : ArchiveInvalidationBase
	{
		/// <summary>
		/// The name of the ArchiveInvalidation BSA file.
		/// </summary>
		private const string AI_BSA = "Skyrim - AI!.bsa";

		#region Constructor

		/// <summary>
		/// Initialized the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		public ArchiveInvalidation(SkyrimGameMode p_gmdGameMode)
			: base(p_gmdGameMode)
		{
		}

		#endregion

		/// <summary>
		/// Gets whether AI is enabled.
		/// </summary>
		/// <returns><c>true</c> if AI is enabled;
		/// <c>false</c> otherwise.</returns>
		public override bool IsActive()
		{
			string strPluginsPath = GameMode.PluginDirectory;
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Skyrim - *.bsa"))
			{
				if (fi.LastWriteTime.CompareTo(new DateTime(2009, 1, 1)) > -1)
					return false;
			}
			return true;
			/*string strIniPath = GameMode.SettingsFiles.IniPath;
			if (!File.Exists(strIniPath))
				return false;
			string strBSAs = IniMethods.GetPrivateProfileString("Archive", "sResourceArchiveList", null, strIniPath);
			if (String.IsNullOrEmpty(strBSAs))
				return false;
			List<string> bsas = new List<string>(strBSAs.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			return bsas.Contains(AI_BSA);*/
		}

		/// <summary>
		/// Writes the given value to both the Fallout INI file and the default Fallout INI file.
		/// </summary>
		/// <param name="p_strSection">The section containing the key whose value is to be written.</param>
		/// <param name="p_strValueKey">The key whose value is to be written.</param>
		/// <param name="p_intValue">The value to write.</param>
		private void WriteIniInt(string p_strSection, string p_strValueKey, Int32 p_intValue)
		{
			IniMethods.WritePrivateProfileInt32(p_strSection, p_strValueKey, p_intValue, GameMode.SettingsFiles.IniPath);
			string strIniPath = ((SkyrimSettingsFiles)GameMode.SettingsFiles).SkyrimDefaultIniPath;
			if (File.Exists(strIniPath))
				IniMethods.WritePrivateProfileInt32(p_strSection, p_strValueKey, p_intValue, strIniPath);
		}

		/// <summary>
		/// Writes the given value to both the Fallout INI file and the default Fallout INI file.
		/// </summary>
		/// <param name="p_strSection">The section containing the key whose value is to be written.</param>
		/// <param name="p_strValueKey">The key whose value is to be written.</param>
		/// <param name="p_strValue">The value to write.</param>
		private void WriteIniString(string p_strSection, string p_strValueKey, string p_strValue)
		{
			IniMethods.WritePrivateProfileString(p_strSection, p_strValueKey, p_strValue, GameMode.SettingsFiles.IniPath);
			string strIniPath = ((SkyrimSettingsFiles)GameMode.SettingsFiles).SkyrimDefaultIniPath;
			if (File.Exists(strIniPath))
				IniMethods.WritePrivateProfileString(p_strSection, p_strValueKey, p_strValue, strIniPath);
		}

		/// <summary>
		/// Gets the list of BSA files in the Fallout INI file.
		/// </summary>
		/// <param name="p_booInsertAI">Whether to insert the AI BSA into the returned list.</param>
		/// <returns>The list of BSA files in the Fallout INI file.</returns>
		private string GetBSAList(bool p_booInsertAI)
		{
			string strIniPath = GameMode.SettingsFiles.IniPath;
			string strBSAs = IniMethods.GetPrivateProfileString("Archive", "sResourceArchiveList", null, strIniPath);
			List<string> lstBSAs = new List<string>();
			if (strBSAs != null)
				lstBSAs.AddRange(strBSAs.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			List<string> lstNewBSAs = new List<string>();
			for (int i = 0; i < lstBSAs.Count; i++)
			{
				lstBSAs[i] = lstBSAs[i].Trim(' ');
				if (lstBSAs[i].Contains("Misc"))
					lstNewBSAs.Insert(0, lstBSAs[i]);
				else if (lstBSAs[i] != AI_BSA)
					lstNewBSAs.Add(lstBSAs[i]);
			}
			if (p_booInsertAI)
				lstNewBSAs.Insert(0, AI_BSA);
			return string.Join(", ", lstNewBSAs.ToArray());
		}

		/// <summary>
		/// Enables AI.
		/// </summary>
		protected override void ApplyAI()
		{
			string strPluginsPath = GameMode.PluginDirectory;
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Skyrim - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 1);

			/*File.WriteAllBytes(Path.Combine(strPluginsPath, AI_BSA), new byte[] {
                0x42, 0x53, 0x41, 0x00, 0x67, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x00, 0x03, 0x07, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
                0x36, 0x00, 0x00, 0x00, 0x01, 0x00, 0x61, 0x00, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x48, 0x00, 0x00, 0x00, 0x61, 0x00
            });
			WriteIniString("Archive", "sResourceArchiveList", GetBSAList(true));*/
		}

		/// <summary>
		/// Disables AI.
		/// </summary>
		protected override void RemoveAI()
		{
			/*string strPluginsPath = GameMode.PluginDirectory;
			File.Delete(Path.Combine(strPluginsPath, AI_BSA));
			WriteIniString("Archive", "sResourceArchiveList", GetBSAList(false));*/
		}
	}
}
