namespace Nexus.Client.Games.Oblivion.Tools.AI
{
    using System;
    using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Windows.Forms;

	using Nexus.Client.Games.Gamebryo.Tools.AI;
    using Nexus.Client.Util;
	
    /// <summary>
	/// Controls ArchiveInvalidation.
	/// </summary>
	public class ArchiveInvalidation : ArchiveInvalidationBase
	{
		/// <summary>
		/// The name of the ArchiveInvalidation BSA file.
		/// </summary>
		private const string AI_BSA = "BSARedirection.bsa";

		/// <summary>
		/// The old name of the AI BSA file.
		/// </summary>
		private const string OLD_AI_BSA = @"..\obmm\BSARedirection.bsa";

		#region Constructor

		/// <summary>
		/// Initialized the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		public ArchiveInvalidation(OblivionGameMode p_gmdGameMode)
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
			string strPluginsPath = GameMode.PluginDirectory;
			return File.Exists(Path.Combine(strPluginsPath, AI_BSA));
		}

		/// <summary>
		/// Gets the list of BSA files in the INI file.
		/// </summary>
		/// <param name="p_booInsertAI">Whether to insert the AI BSA into the returned list.</param>
		/// <returns>The list of BSA files in the INI file.</returns>
		private string GetBSAList(bool p_booInsertAI)
		{
			string strIniPath = GameMode.SettingsFiles.IniPath;
			List<string> bsas = new List<string>(IniMethods.GetPrivateProfileString("Archive", "SArchiveList", null, strIniPath).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
			List<string> lstNewBSAs = new List<string>();
			for (int i = 0; i < bsas.Count; i++)
			{
				bsas[i] = bsas[i].Trim(' ');
				if (bsas[i] == OLD_AI_BSA)
					continue;
				if (bsas[i] != AI_BSA)
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
			var pluginsPath = GameMode.PluginDirectory;

            try
            {
                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("Oblivion - *.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2005, 10, 1);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("DLC*.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2005, 10, 1);
                }

                foreach (var fi in new DirectoryInfo(pluginsPath).GetFiles("Knights.bsa"))
                {
                    fi.LastWriteTime = new DateTime(2005, 10, 1);
                }

                var iniPath = GameMode.SettingsFiles.IniPath;
                IniMethods.WritePrivateProfileString("Archive", "SInvalidationFile", "", iniPath);
                FileUtil.ForceDelete(Path.Combine(pluginsPath, "archiveinvalidation.txt"));
                FileUtil.ForceDelete(
                    Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "obmm"), AI_BSA));
                File.WriteAllBytes(Path.Combine(pluginsPath, AI_BSA), new byte[]
                {
                    0x42, 0x53, 0x41, 0x00, 0x67, 0x00, 0x00, 0x00, 0x24, 0x00, 0x00, 0x00, 0x03, 0x07, 0x00, 0x00,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                    0x02, 0x00, 0x00, 0x00
                });
                IniMethods.WritePrivateProfileString("Archive", "SArchiveList", GetBSAList(true), iniPath);
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
			string strIniPath = GameMode.SettingsFiles.IniPath;
			IniMethods.WritePrivateProfileString("Archive", "SInvalidationFile", "ArchiveInvalidation.txt", strIniPath);
			FileUtil.ForceDelete(Path.Combine(GameMode.PluginDirectory, AI_BSA));
			FileUtil.ForceDelete(Path.Combine(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "obmm"), AI_BSA));
			IniMethods.WritePrivateProfileString("Archive", "SArchiveList", GetBSAList(false), strIniPath);
		}
	}
}
