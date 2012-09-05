using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Nexus.Client.Games.Gamebryo.PluginManagement.Boss;
using Nexus.Client.Games.Gamebryo.Plugins;
using Nexus.Client.Games.Gamebryo.Tools.TESsnip;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.Gamebryo.PluginManagement
{
	/// <summary>
	/// Creats <see cref="Plugin"/>s from Gamebryo based game plugin files.
	/// </summary>
	public class GamebryoPluginFactory : IPluginFactory
	{
		private string m_strPluginDirectory = null;

		#region Properties

		/// <summary>
		/// Gets the BOSS plugin sorter.
		/// </summary>
		/// <value>The BOSS plugin sorter.</value>
		protected IBossSorter BossSorter { get; private set; }

		#endregion

		#region Contructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strPluginDirectory">The directory where the plugins are installed.</param>
		/// <param name="p_bstBoss">The BOSS instance to use to set plugin order.</param>
		public GamebryoPluginFactory(string p_strPluginDirectory, IBossSorter p_bstBoss)
		{
			m_strPluginDirectory = p_strPluginDirectory;
			BossSorter = p_bstBoss;
		}

		#endregion

		/// <summary>
		/// Creates a plugin of the appropriate type from the specified file.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin file.</param>
		/// <returns>A plugin of the appropriate type from the specified file, if the type of the plugin
		/// can be determined; <c>null</c> otherwise.</returns>
		public Plugin CreatePlugin(string p_strPluginPath)
		{
			if (!File.Exists(p_strPluginPath))
				return null;

			string strPluginName = Path.GetFileName(p_strPluginPath);
			TesPlugin tpgPlugin;
			try
			{
				tpgPlugin = new TesPlugin(p_strPluginPath, true);
			}
			catch
			{
				tpgPlugin = null;
			}
            if (tpgPlugin == null || tpgPlugin.Records.Count == 0 || (tpgPlugin.Records[0].Name != "TES4" && tpgPlugin.Records[0].Name != "TES3"))
			{
				string strDescription = strPluginName + Environment.NewLine + "Warning: Plugin appears corrupt";
				return new GamebryoPlugin(p_strPluginPath, strDescription, null, false);
			}

			StringBuilder stbDescription = new StringBuilder();
			string name = null;
			string desc = null;
			byte[] pic = null;
			List<string> masters = new List<string>();

            foreach (SubRecord sr in ((Record)tpgPlugin.Records[0]).SubRecords)
            {
                switch (sr.Name)
                {
                    case "CNAM":
                        name = sr.GetStrData();
                        break;
                    case "SNAM":
                        desc = sr.GetStrData();
                        break;
                    case "MAST":
                        masters.Add(sr.GetStrData());
                        break;
                    case "SCRN":
                        pic = sr.GetData();
                        break;
                }
            }
            if (tpgPlugin.Records[0].Name == "TES4" && ((Path.GetExtension(p_strPluginPath).CompareTo(".esp") == 0) != ((((Record)tpgPlugin.Records[0]).Flags1 & 1) == 0)))
            {
                if ((((Record)tpgPlugin.Records[0]).Flags1 & 1) == 0)
                    stbDescription.Append(@"<span style='color:#ff1100;'><b>WARNING: This plugin has the file extension .esm, but its file header marks it as an esp!</b></span><br/><br/>");
                else
                    stbDescription.Append(@"<span style='color:#ff1100;'><b>WARNING: This plugin has the file extension .esp, but its file header marks it as an esm!</b></span><br/><br/>");
            }
            stbDescription.AppendFormat(@"<b><u>{0}</u></b><br/>", strPluginName);
            if ((name != null) && (name != string.Empty))
                stbDescription.AppendFormat(@"<b>Author:</b> {0}<br/>", name);
            if ((desc != null) && (desc != string.Empty))
            {
                desc = desc.Replace("\r\n", "\n").Replace("\n\r", "\n").Replace("\n", "<br/>");
                stbDescription.AppendFormat(@"<b>Description:</b><br/>{0}<br/>", desc);
            }
            if (masters.Count > 0)
            {
                stbDescription.Append(@"<b>Masters:</b><ul>");
                for (int i = 0; i < masters.Count; i++)
                {
                    stbDescription.AppendFormat("<li>{0}</li>", masters[i]);
                }
                stbDescription.Append(@"</ul>");
            }

            Image imgPicture = null;
            if (pic != null)
                imgPicture = Bitmap.FromStream(new MemoryStream(pic));


            Plugin pifInfo = new GamebryoPlugin(p_strPluginPath, stbDescription.ToString(), imgPicture, BossSorter.IsMaster(p_strPluginPath));
			
			return pifInfo;
		}

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		public bool IsActivatiblePluginFile(string p_strPath)
		{
#if DEBUG
			if (!Path.IsPathRooted(p_strPath))
				throw new Exception("When querying IsActivatiblePluginFile, path must be absolute: " + p_strPath);
#endif
			string strExt = Path.GetExtension(p_strPath).ToLowerInvariant();
			return (strExt == ".esp" || strExt == ".esm") && Path.GetDirectoryName(p_strPath).Equals(m_strPluginDirectory, StringComparison.OrdinalIgnoreCase);
		}
	}
}
