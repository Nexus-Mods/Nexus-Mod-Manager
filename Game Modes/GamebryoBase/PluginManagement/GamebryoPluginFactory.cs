using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder;
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
		/// Gets the LoadOrder plugin manager.
		/// </summary>
		/// <value>The LoadOrder plugin manager.</value>
		protected ILoadOrderManager LoadOrderManager { get; private set; }

		#endregion

		#region Contructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strPluginDirectory">The directory where the plugins are installed.</param>
		/// <param name="p_bstLoadOrder">The LoadOrder instance to use to set the plugin order.</param>
		public GamebryoPluginFactory(string p_strPluginDirectory, ILoadOrderManager p_bstLoadOrder)
		{
			m_strPluginDirectory = p_strPluginDirectory;
			LoadOrderManager = p_bstLoadOrder;
		}

		#endregion

		/// <summary>
		/// Creates a plugin of the appropriate type from the specified file.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin file.</param>
		/// <returns>A plugin of the appropriate type from the specified file, if the type of the plugin
		/// can be determined; <c>null</c> otherwise.</returns>
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
				return new GamebryoPlugin(p_strPluginPath, strDescription, null, false, false);
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

			uint intIsMaster = 0;
			uint intIsLightMaster = 0;
			if (tpgPlugin.Records[0].Name == "TES4")
			{
				intIsMaster = ((Record)tpgPlugin.Records[0]).Flags1 & 1;
			    intIsLightMaster = ((Record)tpgPlugin.Records[0]).Flags1 & 512;
			}
			else if (tpgPlugin.Records[0].Name == "TES3")
				intIsMaster = Convert.ToUInt32(TesPlugin.GetIsEsm(p_strPluginPath));

			if (tpgPlugin.Records[0].Name == "TES4" && ((Path.GetExtension(p_strPluginPath).CompareTo(".esp") == 0) != ((intIsMaster == 0) && (intIsLightMaster == 0))))
			{
				if ((intIsMaster == 0) && (intIsLightMaster == 0))
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
					if (File.Exists(Path.Combine(m_strPluginDirectory, masters[i])))
					{
						if (LoadOrderManager.IsPluginActive(masters[i]))
							stbDescription.AppendFormat("<li>{0}</li>", masters[i]);
						else
						{
							stbDescription.AppendFormat("<span style='color:#ffa500;'><li>{0} - DISABLED</li></span>", masters[i]);
						}
					}
					else
					{
						stbDescription.AppendFormat("<span style='color:#ff1100;'><li>{0} - MISSING</li></span>", masters[i]);
					}
				}
				stbDescription.Append(@"</ul>");
			}

			Image imgPicture = null;
			if (pic != null)
			{
				try
				{
					imgPicture = Bitmap.FromStream(new MemoryStream(pic));
				}
				catch { }
			}

			Plugin pifInfo = new GamebryoPlugin(p_strPluginPath, stbDescription.ToString(), imgPicture, (intIsMaster == 1), (intIsLightMaster == 512));
			pifInfo.SetMasters(masters);

			return pifInfo;
		}

		/// <summary>
		/// Gets the updated plugin info.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin file.</param>
		/// <returns>A plugin of the appropriate type from the specified file, if the type of the plugin
		/// can be determined; <c>null</c> otherwise.</returns>
		public string GetUpdatedPluginInfo(string p_strPluginPath)
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
				string strDescription =  strPluginName + Environment.NewLine + "Warning: Plugin appears corrupt";
				return strDescription;
			}

			StringBuilder stbDescription = new StringBuilder();
			string name = null;
			string desc = null;
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
				}
			}

			uint intIsMaster = 0;
			uint intIsLightMaster = 0;
			if (tpgPlugin.Records[0].Name == "TES4")
			{
			    intIsMaster = ((Record)tpgPlugin.Records[0]).Flags1 & 1;
			    intIsLightMaster = ((Record)tpgPlugin.Records[0]).Flags1 & 512;
			}
			else if (tpgPlugin.Records[0].Name == "TES3")
				intIsMaster = Convert.ToUInt32(TesPlugin.GetIsEsm(p_strPluginPath));
			if (tpgPlugin.Records[0].Name == "TES4" && ((Path.GetExtension(p_strPluginPath).CompareTo(".esp") == 0) != ((intIsMaster == 0) && (intIsLightMaster == 0))))
			{
				if ((intIsMaster == 0) && (intIsLightMaster == 0))
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
					if (File.Exists(Path.Combine(m_strPluginDirectory, masters[i])))
					{
						if (LoadOrderManager.IsPluginActive(masters[i]))
							stbDescription.AppendFormat("<li>{0}</li>", masters[i]);
						else
						{
							stbDescription.AppendFormat("<span style='color:#ffa500;'><li>{0} - DISABLED</li></span>", masters[i]);
						}
					}
					else
					{
						stbDescription.AppendFormat("<span style='color:#ff1100;'><li>{0} - MISSING</li></span>", masters[i]);
					}
				}
				stbDescription.Append(@"</ul>");
			}

			return stbDescription.ToString();
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
