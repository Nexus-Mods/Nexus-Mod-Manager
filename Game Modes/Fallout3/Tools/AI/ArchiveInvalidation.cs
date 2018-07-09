using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo.Tools.AI;
using Nexus.Client.Util;
using System.Windows.Forms;

namespace Nexus.Client.Games.Fallout3.Tools.AI
{
	/// <summary>
	/// UI.Controls ArchiveInvalidation.
	/// </summary>
	public class ArchiveInvalidation : ArchiveInvalidationBase
	{
		/// <summary>
		/// The name of the ArchiveInvalidation BSA file.
		/// </summary>
		private const string AI_BSA = "ArchiveInvalidationInvalidated!.bsa";

		#region Constructor

		/// <summary>
		/// Initialized the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		public ArchiveInvalidation(Fallout3GameMode p_gmdGameMode)
			:base(p_gmdGameMode)
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
			return IniMethods.GetPrivateProfileInt32("Archive", "bInvalidateOlderFiles", 0, strFalloutIniPath) != 0;
		}

		/// <summary>
		/// Gets the list of BSA files in the Fallout INI file.
		/// </summary>
		/// <returns>he list of BSA files in the Fallout INI file.</returns>
		private string GetBSAList()
		{
			string strFalloutIniPath = GameMode.SettingsFiles.IniPath;
			List<string> bsas = new List<string>(IniMethods.GetPrivateProfileString("Archive", "SArchiveList", null, strFalloutIniPath).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			for (int i = 0; i < bsas.Count; i++)
			{
				bsas[i] = bsas[i].Trim(' ');
				if (bsas[i] == AI_BSA)
					bsas.RemoveAt(i--);
			}
			return string.Join(", ", bsas.ToArray());
		}

		/// <summary>
		/// Enables AI.
		/// </summary>
		protected override void ApplyAI()
		{
			string strPluginsPath = GameMode.PluginDirectory;
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Fallout - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 1);
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Anchorage - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 2);
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("ThePitt - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 3);
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("BrokenSteel - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 4);
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("PointLookout - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 5);
			foreach (FileInfo fi in new DirectoryInfo(strPluginsPath).GetFiles("Zeta - *.bsa"))
				fi.LastWriteTime = new DateTime(2008, 10, 6);

			string strFalloutIniPath = GameMode.SettingsFiles.IniPath;
			IniMethods.WritePrivateProfileInt32("Archive", "bInvalidateOlderFiles", 1, strFalloutIniPath);
			IniMethods.WritePrivateProfileInt32("General", "bLoadFaceGenHeadEGTFiles", 1, strFalloutIniPath);
			IniMethods.WritePrivateProfileString("Archive", "SInvalidationFile", "", strFalloutIniPath);
			File.Delete(Path.Combine(strPluginsPath, "archiveinvalidation.txt"));
			File.WriteAllBytes(Path.Combine(strPluginsPath, AI_BSA), new byte[] {
                0x42, 0x53, 0x41, 0x00, 0x67, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x00, 0x03, 0x07, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
                0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
                0x36, 0x00, 0x00, 0x00, 0x01, 0x00, 0x61, 0x00, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x48, 0x00, 0x00, 0x00, 0x61, 0x00
            });
			IniMethods.WritePrivateProfileString("Archive", "SArchiveList", AI_BSA + ", " + GetBSAList(), strFalloutIniPath);
		}

		/// <summary>
		/// Disables AI.
		/// </summary>
		protected override void RemoveAI()
		{
			string strFalloutIniPath = GameMode.SettingsFiles.IniPath;
			IniMethods.WritePrivateProfileInt32("Archive", "bInvalidateOlderFiles", 0, strFalloutIniPath);
			IniMethods.WritePrivateProfileInt32("General", "bLoadFaceGenHeadEGTFiles", 0, strFalloutIniPath);
			IniMethods.WritePrivateProfileString("Archive", "SInvalidationFile", "ArchiveInvalidation.txt", strFalloutIniPath);
			File.Delete(Path.Combine(GameMode.PluginDirectory, AI_BSA));
			IniMethods.WritePrivateProfileString("Archive", "SArchiveList", GetBSAList(), strFalloutIniPath);
		}
	}
}
