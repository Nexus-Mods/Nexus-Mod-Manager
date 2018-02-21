using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.FalloutNV.Tools.AI
{
	/// <summary>
	/// Controls ArchiveInvalidation.
	/// </summary>
	public class ArchiveInvalidation : ArchiveInvalidationBase
	{
		/// <summary>
		/// The name of the ArchiveInvalidation BSA file.
		/// </summary>
		private const string AI_BSA = "Fallout - AI!.bsa";

		/// <summary>
		/// The old name of the AI BSA file.
		/// </summary>
		private const string OLD_AI_BSA = "ArchiveInvalidationInvalidated!.bsa";

		#region Constructor

		/// <summary>
		/// Initialized the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		public ArchiveInvalidation(FalloutNVGameMode p_gmdGameMode)
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
			string strFalloutIniPath = GameMode.SettingsFiles.IniPath;
			if (!File.Exists(strFalloutIniPath))
				return false;
			List<string> bsas = new List<string>((IniMethods.GetPrivateProfileString("Archive", "SArchiveList", null, strFalloutIniPath) ?? "").Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			Int32 intInvalidate = IniMethods.GetPrivateProfileInt32("Archive", "bInvalidateOlderFiles", 0, strFalloutIniPath);
			return bsas.Contains(AI_BSA) || (intInvalidate != 0);
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
			string strIniPath = ((FalloutNVSettingsFiles)GameMode.SettingsFiles).FODefaultIniPath;
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
			string strIniPath = ((FalloutNVSettingsFiles)GameMode.SettingsFiles).FODefaultIniPath;
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
			string strFalloutIniPath = GameMode.SettingsFiles.IniPath;
			List<string> bsas = new List<string>(IniMethods.GetPrivateProfileString("Archive", "SArchiveList", null, strFalloutIniPath).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			List<string> lstNewBSAs = new List<string>();
			for (int i = 0; i < bsas.Count; i++)
			{
				bsas[i] = bsas[i].Trim(' ');
				if (bsas[i] == OLD_AI_BSA)
					continue;
				if (bsas[i].Contains("Misc"))
					lstNewBSAs.Insert(0, bsas[i]);
				else if (bsas[i] != AI_BSA)
					lstNewBSAs.Add(bsas[i]);
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
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Fallout - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 1);
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("ClassicPack - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 1);

			WriteIniInt("Archive", "bInvalidateOlderFiles", 1);
			WriteIniInt("General", "bLoadFaceGenHeadEGTFiles", 1);
			WriteIniString("Archive", "SInvalidationFile", "");
			File.Delete(Path.Combine(strPluginsPath, "archiveinvalidation.txt"));
			File.Delete(Path.Combine(strPluginsPath, OLD_AI_BSA));
			File.WriteAllBytes(Path.Combine(strPluginsPath, AI_BSA), new byte[] {
                0x42, 0x53, 0x41, 0x00, 0x67, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x00, 0x03, 0x07, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
                0x36, 0x00, 0x00, 0x00, 0x01, 0x00, 0x61, 0x00, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x48, 0x00, 0x00, 0x00, 0x61, 0x00
            });
			WriteIniString("Archive", "SArchiveList", GetBSAList(true));
		}

		/// <summary>
		/// Disables AI.
		/// </summary>
		protected override void RemoveAI()
		{
			string strPluginsPath = GameMode.PluginDirectory;
			WriteIniInt("Archive", "bInvalidateOlderFiles", 0);
			WriteIniInt("General", "bLoadFaceGenHeadEGTFiles", 0);
			WriteIniString("Archive", "SInvalidationFile", "ArchiveInvalidation.txt");
			File.Delete(Path.Combine(strPluginsPath, AI_BSA));
			File.Delete(Path.Combine(strPluginsPath, OLD_AI_BSA));
			WriteIniString("Archive", "SArchiveList", GetBSAList(false));
		}
	}
}
