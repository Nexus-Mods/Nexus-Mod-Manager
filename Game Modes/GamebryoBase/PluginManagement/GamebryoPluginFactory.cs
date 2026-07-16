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
		private readonly PluginManagementPolicy m_pmpPluginPolicy;

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
			: this(p_strPluginDirectory, p_bstLoadOrder, null)
		{
		}

		public GamebryoPluginFactory(string p_strPluginDirectory, ILoadOrderManager p_bstLoadOrder, PluginManagementPolicy p_pmpPluginPolicy)
		{
			m_strPluginDirectory = p_strPluginDirectory;
			LoadOrderManager = p_bstLoadOrder;
			m_pmpPluginPolicy = p_pmpPluginPolicy ?? new PluginManagementPolicy();
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
				return new GamebryoPlugin(p_strPluginPath, strDescription, null, m_pmpPluginPolicy.Classify(p_strPluginPath, PluginHeaderFlags.None, 0, null, PluginParseStatus.Corrupt));
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

			PluginHeaderFlags parsedFlags = GetParsedHeaderFlags(p_strPluginPath, tpgPlugin);
			bool booHeaderMaster = (parsedFlags & PluginHeaderFlags.Master) == PluginHeaderFlags.Master;
			bool booHeaderLight = (parsedFlags & PluginHeaderFlags.Light) == PluginHeaderFlags.Light;

			if (tpgPlugin.Records[0].Name == "TES4")
			{
				if ((Path.GetExtension(p_strPluginPath).CompareTo(".esl") == 0) && (!booHeaderLight))
					stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esl, but its file header is missing the esl flag!</b></color><br/><br/>");
				if ((Path.GetExtension(p_strPluginPath).CompareTo(".esm") == 0) && (!booHeaderMaster))
					stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esm, but its file header is missing the esm flag!</b></color><br/><br/>");
				if (Path.GetExtension(p_strPluginPath).CompareTo(".esp") == 0)
				{
					if (booHeaderMaster && booHeaderLight)
						stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esp, but its file header marks it as an esl and esm!</b></color><br/><br/>");
					else if (booHeaderMaster)
						stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esp, but its file header marks it as an esm!</b></color><br/><br/>");
					else if (booHeaderLight)
						stbDescription.Append(@"<color=#00662d>This file is marked as a light plugin (so it doesn't use a full load order slot).</color><br><br>");
				}
			}

			uint intFormVersion = ((Record)tpgPlugin.Records[0]).Flags3;
			PluginMetadata metadata = m_pmpPluginPolicy.Classify(p_strPluginPath, parsedFlags, unchecked((int)intFormVersion), masters, PluginParseStatus.Parsed);

			stbDescription.AppendFormat(@"<b><u>{0}</u></b><br>", strPluginName);
			if ((name != null) && (name != string.Empty))
				stbDescription.AppendFormat(@"<b>Author:</b> {0}<br/>", name);
			if ((desc != null) && (desc != string.Empty))
			{
				desc = desc.Replace("\r\n", "\n").Replace("\n\r", "\n").Replace("\n", "<br>");
				stbDescription.AppendFormat(@"<b>Description:</b><br/>{0}<br/>", desc);
			}
			if (masters.Count > 0)
			{
				stbDescription.Append("<b>Masters:</b><br>");

				for (int i = 0; i < masters.Count; i++)
				{
					string masterPath = Path.Combine(m_strPluginDirectory, masters[i]);

					if (!File.Exists(masterPath))
					{
						stbDescription.AppendFormat(
							"<color=#ff1100>• {0} - MISSING</color><br>",
							masters[i]);
					}
					else if (!LoadOrderManager.IsPluginActive(masters[i]))
					{
						stbDescription.AppendFormat(
							"<color=#ffa500>• {0} - DISABLED</color><br>",
							masters[i]);
					}
					else
					{
						stbDescription.AppendFormat(
							"• {0}<br>",
							masters[i]);
					}
				}

				stbDescription.Append("<br>");
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

			Plugin pifInfo = new GamebryoPlugin(p_strPluginPath, stbDescription.ToString(), imgPicture, metadata);

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

			PluginHeaderFlags parsedFlags = GetParsedHeaderFlags(p_strPluginPath, tpgPlugin);
			bool booHeaderMaster = (parsedFlags & PluginHeaderFlags.Master) == PluginHeaderFlags.Master;
			bool booHeaderLight = (parsedFlags & PluginHeaderFlags.Light) == PluginHeaderFlags.Light;

			if (tpgPlugin.Records[0].Name == "TES4")
			{
				if ((Path.GetExtension(p_strPluginPath).CompareTo(".esl") == 0) && (!booHeaderLight))
					stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esl, but its file header is missing the esl flag!</b></color><br/><br/>");
				if ((Path.GetExtension(p_strPluginPath).CompareTo(".esm") == 0) && (!booHeaderMaster))
					stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esm, but its file header is missing the esm flag which marks it as an esp!</b></color><br/><br/>");
				if (Path.GetExtension(p_strPluginPath).CompareTo(".esp") == 0)
				{
					if (booHeaderMaster && booHeaderLight)
						stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esp, but its file header marks it as an esl and esm!</b></color><br/><br/>");
					else if (booHeaderMaster)
						stbDescription.Append(@"<color=#ff1100><b>WARNING: This plugin has the file extension .esp, but its file header marks it as an esm!</b></color><br/><br/>");
					else if (booHeaderLight)
						stbDescription.Append(@"<color=#00662d>This file is marked as a light plugin (so it doesn't use a full load order slot).</color><br><br>");
				}
			}

			stbDescription.AppendFormat(@"<b><u>{0}</u></b><br>", strPluginName);
			if ((name != null) && (name != string.Empty))
				stbDescription.AppendFormat(@"<b>Author:</b> {0}<br/>", name);
			if ((desc != null) && (desc != string.Empty))
			{
				desc = desc.Replace("\r\n", "\n").Replace("\n\r", "\n").Replace("\n", "<br>");
				stbDescription.AppendFormat(@"<b>Description:</b><br/>{0}<br/>", desc);
			}
			if (masters.Count > 0)
			{
				stbDescription.Append("<b>Masters:</b><br>");

				for (int i = 0; i < masters.Count; i++)
				{
					string masterPath = Path.Combine(m_strPluginDirectory, masters[i]);

					if (!File.Exists(masterPath))
					{
						stbDescription.AppendFormat(
							"<color=#ff1100>• {0} - MISSING</color><br>",
							masters[i]);
					}
					else if (!LoadOrderManager.IsPluginActive(masters[i]))
					{
						stbDescription.AppendFormat(
							"<color=#ffa500>• {0} - DISABLED</color><br>",
							masters[i]);
					}
					else
					{
						stbDescription.AppendFormat(
							"• {0}<br>",
							masters[i]);
					}
				}

				stbDescription.Append("<br>");
			}

			m_pmpPluginPolicy.Classify(p_strPluginPath, parsedFlags, unchecked((int)((Record)tpgPlugin.Records[0]).Flags3), masters, PluginParseStatus.Parsed);
			return stbDescription.ToString();
		}

		private PluginHeaderFlags GetParsedHeaderFlags(string p_strPluginPath, TesPlugin p_tpgPlugin)
		{
			if (p_tpgPlugin == null || p_tpgPlugin.Records.Count == 0)
				return PluginHeaderFlags.None;

			Record header = (Record)p_tpgPlugin.Records[0];
			PluginHeaderFlags flags = m_pmpPluginPolicy.MapHeaderFlags(header.Flags1, header.Flags2, header.Flags3);
			if (p_tpgPlugin.Records[0].Name == "TES3" && TesPlugin.GetIsEsm(p_strPluginPath))
				flags |= PluginHeaderFlags.Master;
			return flags;
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

			return File.Exists(p_strPath) && (strExt == ".esp" || strExt == ".esm" || strExt == ".esl") && Path.GetDirectoryName(p_strPath).Equals(m_strPluginDirectory, StringComparison.OrdinalIgnoreCase);
		}
	}
}
