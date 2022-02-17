using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Text;
using ChinhDo.Transactions;
using Nexus.Client.Games.BaldursGate3.Settings;
using Nexus.Client.Games.BaldursGate3.Settings.UI;
using Nexus.Client.Games.BaldursGate3.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Settings.UI;
using Nexus.Client.Games.Tools;
using Nexus.Client.Updating;
using Nexus.Client.Util;
using System.Diagnostics;
using Newtonsoft.Json;

namespace Nexus.Client.Games.BaldursGate3
{
	public class BG3Json
	{
		public BG3Mod[] Mods;
		public string MD5;
		public bool IsBorked;
	}

	public class BG3Mod
	{
		public string Author;
		public string Name;
		public string modName;
		public string Folder;
		public string folderName;
		public string Version;
		public string Description;
		public string UUID;
		public string Created;
		public string[] Dependencies;
		public string Group;
	}

	public class BG3BorkedJson
	{
		public string name;
		public string version_number;
		public string website_url;
		public string description;
		public string[] dependencies;
	}

	/// <summary>
	/// Provides information required for the program to manage Legend of BaldursGate3 game's plugins and mods.
	/// </summary>
	public class BaldursGate3GameMode : GameModeBase
	{
		private BaldursGate3GameModeDescriptor m_gmdGameModeInfo = null;
		private BaldursGate3Launcher m_glnGameLauncher = null;
		private BaldursGate3ToolLauncher m_gtlToolLauncher = null;

		#region Properties

		/// <summary>
		/// Gets the version of the installed game.
		/// </summary>
		/// <value>The version of the installed game.</value>
		public override Version GameVersion
		{
			get
			{
				string strFullPath = null;
				foreach (string strExecutable in GameExecutables)
				{
					strFullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, strExecutable);
					if (File.Exists(strFullPath))
						return new Version(System.Diagnostics.FileVersionInfo.GetVersionInfo(strFullPath).FileVersion.Replace(", ", "."));
				}
				return null;
			}
		}

		/// <summary>
		/// Gets a list of paths to which the game mode writes.
		/// </summary>
		/// <value>A list of paths to which the game mode writes.</value>
		public override IEnumerable<string> WritablePaths
		{
			get
			{
				return null;
			}
		}

		/// <summary>
		/// Gets the installed version of the script extender.
		/// </summary>
		/// <remarks>
		/// <c>null</c> is returned if the script extender is not installed.
		/// </remarks>
		/// <value>The installed version of the script extender.</value>
		public virtual Version ScriptExtenderVersion
		{
			get
			{
				return null;
			}
		}

		/// <summary>
		/// Gets the path to the per user BaldursGate3 data.
		/// </summary>
		/// <value>The path to the per user BaldursGate3 data.</value>
		public string UserGameDataPath
		{
			get
			{
				return GameModeEnvironmentInfo.InstallationPath;
			}
		}

		/// <summary>
		/// Gets the game launcher for the game mode.
		/// </summary>
		/// <value>The game launcher for the game mode.</value>
		public override IGameLauncher GameLauncher
		{
			get
			{
				if (m_glnGameLauncher == null)
					m_glnGameLauncher = new BaldursGate3Launcher(this, EnvironmentInfo);
				return m_glnGameLauncher;
			}
		}

		/// <summary>
		/// Gets the tool launcher for the game mode.
		/// </summary>
		/// <value>The tool launcher for the game mode.</value>
		public override IToolLauncher GameToolLauncher
		{
			get
			{
				if (m_gtlToolLauncher == null)
					m_gtlToolLauncher = new BaldursGate3ToolLauncher(this, EnvironmentInfo);
				return m_gtlToolLauncher;
			}
		}

		/// <summary>
		/// Gets whether the game mode uses plugins.
		/// </summary>
		/// <remarks>
		/// This indicates whether the game mode used plugins that are
		/// installed by mods, or simply used mods, without
		/// plugins.
		/// 
		/// In games that use mods only, the installation of a mods package
		/// is sufficient to add the functionality to the game. The game
		/// will often have no concept of managable game modifications.
		/// 
		/// In games that use plugins, mods can install files that directly
		/// affect the game (similar to the mod-free use case), but can also
		/// install plugins that can be managed (for example activated/reordered)
		/// after the mod is installed.
		/// </remarks>
		/// <value>Whether the game mode uses plugins.</value>
		public override bool UsesPlugins
		{
			get
			{
				return false;
			}
		}

		/// <summary>
		/// Gets the default game categories.
		/// </summary>
		/// <value>The default game categories stored in the resource file.</value>
		public override string GameDefaultCategories
		{
			get
			{
				return Properties.Resources.Categories;
			}
		}

		/// <summary>
		/// Whether the game has a secondary install path.
		/// </summary>
		public override bool HasSecondaryInstallPath
		{
			get
			{
				return true;
			}
		}

		public override bool RequiresSpecialFileInstallation
		{
			get
			{
				return true;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		public BaldursGate3GameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo)
		{
			SettingsGroupViews = new List<ISettingsGroupView>();
			GeneralSettingsGroup gsgGeneralSettings = new GeneralSettingsGroup(p_eifEnvironmentInfo, this);
			((List<ISettingsGroupView>)SettingsGroupViews).Add(new GeneralSettingsPage(gsgGeneralSettings));
		}

		#endregion

		#region Initialization

		#endregion

		#region Plugin Management

		/// <summary>
		/// Gets the factory that builds plugins for this game mode.
		/// </summary>
		/// <returns>The factory that builds plugins for this game mode.</returns>
		public override IPluginFactory GetPluginFactory()
		{
			return null;
		}

		/// <summary>
		/// Gets the serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.
		/// </summary>
		/// <param name="p_polPluginOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</param>
		/// <returns>The serailizer that serializes and deserializes the list of active plugins
		/// for this game mode.</returns>
		public override IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog)
		{
			return null;
		}

		/// <summary>
		/// Gets the discoverer to use to find the plugins managed by this game mode.
		/// </summary>
		/// <returns>The discoverer to use to find the plugins managed by this game mode.</returns>
		public override IPluginDiscoverer GetPluginDiscoverer()
		{
			return null;
		}

		/// <summary>
		/// Gets the serializer that serializes and deserializes the plugin order
		/// for this game mode.
		/// </summary>
		/// <returns>The serailizer that serializes and deserializes the plugin order
		/// for this game mode.</returns>
		public override IPluginOrderLogSerializer GetPluginOrderLogSerializer()
		{
			return null;
		}

		/// <summary>
		/// Gets the object that validates plugin order for this game mode.
		/// </summary>
		/// <returns>The object that validates plugin order for this game mode.</returns>
		public override IPluginOrderValidator GetPluginOrderValidator()
		{
			return null;
		}

		#endregion

		#region Game Specific Value Management

		/// <summary>
		/// Gets the installer to use to install game specific values.
		/// </summary>
		/// <param name="p_modMod">The mod being installed.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log the installation of the game specific values.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns>The installer to use to manage game specific values, or <c>null</c> if the game mode does not
		/// install any game specific values.</returns>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return null;
		}

		/// <summary>
		/// Gets the installer to use to upgrade game specific values.
		/// </summary>
		/// <param name="p_modMod">The mod being upgraded.</param>
		/// <param name="p_ilgInstallLog">The install log to use to log the installation of the game specific values.</param>
		/// <param name="p_tfmFileManager">The transactional file manager to use to interact with the file system.</param>
		/// <returns>The installer to use to manage game specific values, or <c>null</c> if the game mode does not
		/// install any game specific values.</returns>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_dlgOverwriteConfirmationDelegate">The method to call in order to confirm an overwrite.</param>
		public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate)
		{
			return null;
		}

		#endregion

		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent)
		{
			//if (Path.GetFileName(p_strPath).Equals("info.json", StringComparison.InvariantCultureIgnoreCase) || Path.GetFileName(p_strPath).Equals("manifest.json", StringComparison.InvariantCultureIgnoreCase))
			if (Path.GetFileName(p_strPath).Equals("info.json", StringComparison.InvariantCultureIgnoreCase))
			{
				return string.Empty;
			}
			else
				return p_strPath;
		}

		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
		{
			//if (Path.GetFileName(p_strPath).Equals("info.json", StringComparison.InvariantCultureIgnoreCase) || Path.GetFileName(p_strPath).Equals("manifest.json", StringComparison.InvariantCultureIgnoreCase))
			if (Path.GetFileName(p_strPath).Equals("info.json", StringComparison.InvariantCultureIgnoreCase))
			{
				return string.Empty;
			}
			else
				return p_strPath;
		}

		private void AddManifest(IMod p_modMod)
		{
			string profilesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Larian Studios\Baldur's Gate 3\PlayerProfiles");

			if (Directory.Exists(profilesDirectory))
			{
				DirectoryInfo diProfiles = new DirectoryInfo(profilesDirectory);

				foreach (DirectoryInfo dir in diProfiles.GetDirectories())
				{
					if (!dir.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
					{
						string manifest = Path.Combine(dir.FullName, "modsettings.lsx");

						if (File.Exists(manifest))
						{
							XmlDocument XDoc = new XmlDocument();
							XDoc.Load(manifest);
							XmlNode xmlMods = XDoc.DocumentElement.SelectSingleNode("//*[@id='Mods']");
							XmlNode xmlChildren = xmlMods.FirstChild;
							BG3Json currentMod = null;

							if (p_modMod.GetFileList().Contains("info.json", StringComparer.CurrentCultureIgnoreCase))
								currentMod = LoadJson(Encoding.ASCII.GetString(p_modMod.GetFile("info.json")), false);
							//else if (p_modMod.GetFileList().Contains("manifest.json", StringComparer.CurrentCultureIgnoreCase))
							//	currentMod = LoadJson(Encoding.ASCII.GetString(p_modMod.GetFile("manifest.json")), true);

							if (currentMod == null || currentMod.Mods == null)
								continue;

							foreach (BG3Mod mod in currentMod.Mods)
							{
								XmlNode xmlCheck = XDoc.DocumentElement.SelectSingleNode("//*[@value='" + mod.UUID + "']");

								if (xmlCheck == null)
								{
									XmlElement modDesc = XDoc.CreateElement("node");
									modDesc.SetAttribute("id", "ModuleShortDesc");
									XmlElement attribute = XDoc.CreateElement("attribute");
									attribute.SetAttribute("id", "Folder");
									attribute.SetAttribute("type", "LSString");
									attribute.SetAttribute("value", mod.Folder ?? mod.folderName);
									modDesc.AppendChild(attribute);
									attribute = XDoc.CreateElement("attribute");
									attribute.SetAttribute("id", "MD5");
									attribute.SetAttribute("type", "LSString");
									attribute.SetAttribute("value", string.Empty);
									modDesc.AppendChild(attribute);
									attribute = XDoc.CreateElement("attribute");
									attribute.SetAttribute("id", "Name");
									attribute.SetAttribute("type", "LSString");
									attribute.SetAttribute("value", mod.Name ?? mod.modName);
									modDesc.AppendChild(attribute);
									attribute = XDoc.CreateElement("attribute");
									attribute.SetAttribute("id", "UUID");
									attribute.SetAttribute("type", "FixedString");
									attribute.SetAttribute("value", mod.UUID);
									modDesc.AppendChild(attribute);
									attribute = XDoc.CreateElement("attribute");
									attribute.SetAttribute("id", "Version64");
									attribute.SetAttribute("type", "int64");
									attribute.SetAttribute("value", mod.Version ?? "1");
									modDesc.AppendChild(attribute);
									xmlChildren.AppendChild(modDesc);
								}
							}

							XmlWriterSettings settings = new XmlWriterSettings();
							settings.Indent = true;
							settings.IndentChars = "\t";
							using (XmlWriter writer = XmlWriter.Create(manifest, settings))
							{
								XDoc.Save(writer);
							}
						}
					}
				}
			}
		}

		private void RemoveManifest(IMod p_modMod)
		{
			string profilesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Larian Studios\Baldur's Gate 3\PlayerProfiles");

			if (Directory.Exists(profilesDirectory))
			{
				DirectoryInfo diProfiles = new DirectoryInfo(profilesDirectory);

				foreach (DirectoryInfo dir in diProfiles.GetDirectories())
				{
					if (!dir.Name.Equals("default", StringComparison.OrdinalIgnoreCase))
					{
						string manifest = Path.Combine(dir.FullName, "modsettings.lsx");

						if (File.Exists(manifest))
						{
							XmlDocument XDoc = new XmlDocument();
							XDoc.Load(manifest);
							XmlNode xmlMods = XDoc.DocumentElement.SelectSingleNode("//*[@id='Mods']");
							XmlNode xmlChildren = xmlMods.FirstChild;
							BG3Json currentMod = null;

							if (p_modMod.GetFileList().Contains("info.json", StringComparer.CurrentCultureIgnoreCase))
								currentMod = LoadJson(Encoding.ASCII.GetString(p_modMod.GetFile("info.json")), false);
							//else if (p_modMod.GetFileList().Contains("manifest.json", StringComparer.CurrentCultureIgnoreCase))
							//	currentMod = LoadJson(Encoding.ASCII.GetString(p_modMod.GetFile("manifest.json")), true);

							if (currentMod == null || currentMod.Mods == null)
								continue;

							foreach (BG3Mod mod in currentMod.Mods)
							{
								XmlNode xmlMod = null;
								
								if (!currentMod.IsBorked)
									xmlMod = XDoc.DocumentElement.SelectSingleNode("//*[@value='" + mod.UUID + "']");
								else
									xmlMod = XDoc.DocumentElement.SelectSingleNode("//*[@value='" + mod.Name + "']");

								if (xmlMod != null)
								{
									XmlNode parent = xmlMod.ParentNode;
									xmlChildren.RemoveChild(parent);
								}
							}

							XmlWriterSettings settings = new XmlWriterSettings();
							settings.Indent = true;
							settings.IndentChars = "\t";
							using (XmlWriter writer = XmlWriter.Create(manifest, settings))
							{
								XDoc.Save(writer);
							}
						}
					}
				}
			}
		}

		public BG3Json LoadJson(string json, bool isBorked)
		{
			BG3Json items = new BG3Json();
			BG3BorkedJson borked = null;

			if (!isBorked)
				items = JsonConvert.DeserializeObject<BG3Json>(json);
			else
			{
				borked = JsonConvert.DeserializeObject<BG3BorkedJson>(json);
				items.MD5 = string.Empty;
				BG3Mod[] mods = new BG3Mod[1];
				BG3Mod borkedMod = new BG3Mod();
				borkedMod.Folder = borked.name;
				borkedMod.UUID = Guid.NewGuid().ToString();
				borkedMod.Name = borked.name;
				borkedMod.Version = borked.version_number;
				mods[0] = borkedMod;
				items.Mods = mods;
			}

			items.IsBorked = isBorked;

			return items;
		}

		public override IEnumerable<string> SpecialFileInstall(IMod p_modSelectedMod)
		{
			List<string> fileList = p_modSelectedMod.GetFileList();
			if (fileList.Contains("info.json", StringComparer.OrdinalIgnoreCase))
				AddManifest(p_modSelectedMod);

			return null;
		}

		public override void SpecialFileUninstall(IMod p_modSelectedMod)
		{
			List<string> fileList = p_modSelectedMod.GetFileList();
			if (fileList.Contains("info.json", StringComparer.OrdinalIgnoreCase))
				RemoveManifest(p_modSelectedMod);
		}

		public override bool IsSpecialFile(IEnumerable<string> p_strFiles)
		{
			return p_strFiles.Contains("info.json", StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Gets the updaters used by the game mode.
		/// </summary>
		/// <returns>The updaters used by the game mode.</returns>
		public override IEnumerable<IUpdater> GetUpdaters()
		{
			return null;
		}

		/// <summary>
		/// Creates a game mode descriptor for the current game mode.
		/// </summary>
		/// <returns>A game mode descriptor for the current game mode.</returns>
		protected override IGameModeDescriptor CreateGameModeDescriptor()
		{
			if (m_gmdGameModeInfo == null)
				m_gmdGameModeInfo = new BaldursGate3GameModeDescriptor(EnvironmentInfo);
			return m_gmdGameModeInfo;
		}

		/// <summary>
		/// Disposes of the unamanged resources.
		/// </summary>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="IDisposable.Dispose()"/> method.</param>
		protected override void Dispose(bool p_booDisposing)
		{
		}
	}
}
