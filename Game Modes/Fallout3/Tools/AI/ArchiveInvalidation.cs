namespace Nexus.Client.Games.Fallout3.Tools.AI
{
    using System;
    using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Windows.Forms;

	using Nexus.Client.Games.Gamebryo.Tools.AI;
    using Nexus.Client.Util;
    
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
			var pluginsPath = GameMode.PluginDirectory;

            try
            {

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("Fallout - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2008, 10, 1);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("Anchorage - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2008, 10, 2);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("ThePitt - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2008, 10, 3);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("BrokenSteel - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2008, 10, 4);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("PointLookout - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2008, 10, 5);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("Zeta - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2008, 10, 6);
                }

                var falloutIniPath = GameMode.SettingsFiles.IniPath;

                IniMethods.WritePrivateProfileInt32("Archive", "bInvalidateOlderFiles", 1, falloutIniPath);
                IniMethods.WritePrivateProfileInt32("General", "bLoadFaceGenHeadEGTFiles", 1, falloutIniPath);
                IniMethods.WritePrivateProfileString("Archive", "SInvalidationFile", "", falloutIniPath);
                File.Delete(Path.Combine(pluginsPath, "archiveinvalidation.txt"));
                File.WriteAllBytes(Path.Combine(pluginsPath, AI_BSA), new byte[]
                {
                    0x42, 0x53, 0x41, 0x00, 0x67, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x00, 0x03, 0x07, 0x00, 0x00,
                    0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00,
                    0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00,
                    0x36, 0x00, 0x00, 0x00, 0x01, 0x00, 0x61, 0x00, 0x01, 0x61, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x00, 0x00, 0x48, 0x00, 0x00, 0x00, 0x61, 0x00
                });
                IniMethods.WritePrivateProfileString("Archive", "SArchiveList", AI_BSA + ", " + GetBSAList(), falloutIniPath);
            }
            catch (Exception ex)
            {
                Trace.TraceError("ApplyAI - Could not apply ArchiveInvalidation.");
                TraceUtil.TraceException(ex);

                MessageBox.Show(
                    "Could not apply Archive Invalidation, at least one file could not be modified.\n" +
                    "Please try again, or check trace log for more info.\n\n" +
                    ex.Message,
                    "Archive Invalidation failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
			}
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
