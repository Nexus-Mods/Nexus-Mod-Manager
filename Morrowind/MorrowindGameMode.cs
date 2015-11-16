using System.IO;
using System.Collections.Generic;
using System;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Morrowind.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.Games.Morrowind.PluginManagement.Boss;
using Nexus.Client.Games.Gamebryo.PluginManagement.InstallationLog;
using Nexus.Client.Games.Gamebryo.PluginManagement.OrderLog;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using Nexus.Client.Mods;
using Nexus.Client.Util;


namespace Nexus.Client.Games.Morrowind
{
	/// <summary>
	/// Provides information required for the programme to manage Morrowind plugins and mods.
	/// </summary>
	public class MorrowindGameMode : GamebryoGameModeBase
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "mwse_loader.exe" };
		private MorrowindGameModeDescriptor m_gmdGameModeInfo = null;
		private MorrowindLauncher m_glnGameLauncher = null;
		private MorrowindToolLauncher m_gtlToolLauncher = null;
		private GamebryoActivePluginLogSerializer m_apsActivePluginLogSerializer = null;
		private GamebryoPluginOrderLogSerializer m_posPluginOrderSerializer = null;

		#region Properties
               
		/// <summary>
		/// Gets the list of possible script extender executable files for the game.
		/// </summary>
		/// <value>The list of possible script extender executable files for the game.</value>
		protected override string[] ScriptExtenderExecutables
		{
			get
			{
				return SCRIPT_EXTENDER_EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the path to the per user Morrowind data.
		/// </summary>
		/// <value>The path to the per user Morrowind data.</value>
		public override string UserGameDataPath
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
					m_glnGameLauncher = new MorrowindLauncher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new MorrowindToolLauncher(this, EnvironmentInfo);
				return m_gtlToolLauncher;
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
		/// Gets whether the game mode supports the automatic sorting
		/// functionality for plugins.
		/// </summary>
		public override bool SupportsPluginAutoSorting
		{
			get
			{
				return false;
			}
		}

		/// <summary>
		/// Gets the plugin loadorder manager.
		/// </summary>
		/// <value>The plugin loadorder manager.</value>
		public override ILoadOrderManager LoadOrderManager
		{
			get
			{
				return BossSorter;
			}
		}

		/// <summary>
		/// Gets the BossSorter plugin manager.
		/// </summary>
		/// <value>The BossSorter plugin manager.</value>
		protected BossSorter BossSorter { get; private set; }

		/// <summary>
		/// Whether the plugin sorter is properly initialized.
		/// </summary>
		public override bool PluginSorterInitialized
		{
			get
			{
				return false;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		public MorrowindGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo, p_futFileUtility)
		{
			string strPath = p_eifEnvironmentInfo.ApplicationPersonalDataFolderPath;
			strPath = Path.Combine(Path.Combine(strPath, "boss"), "masterlist.txt");
			BossSorter = new BossSorter(p_eifEnvironmentInfo, this, p_futFileUtility, strPath);
		}

		#endregion

		#region Initialization

		/// <summary>
		/// Instantiates the container to use to store the list of settings files.
		/// </summary>
		/// <returns>The container to use to store the list of settings files.</returns>
		protected override GamebryoSettingsFiles CreateSettingsFileContainer()
		{
			return new GamebryoSettingsFiles();
		}

		/// <summary>
		/// Gets the serializer that serializes and deserializes the plugin order
		/// for this game mode.
		/// </summary>
		/// <returns>The serailizer that serializes and deserializes the plugin order
		/// for this game mode.</returns>
		public override IPluginOrderLogSerializer GetPluginOrderLogSerializer()
		{
			if (m_posPluginOrderSerializer == null)
				m_posPluginOrderSerializer = new GamebryoPluginOrderLogSerializer(BossSorter, null);
			return m_posPluginOrderSerializer;
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
			if (m_apsActivePluginLogSerializer == null)
				m_apsActivePluginLogSerializer = new GamebryoActivePluginLogSerializer(this, p_polPluginOrderLog, BossSorter);
			return m_apsActivePluginLogSerializer;
		}

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{
			base.SetupSettingsFiles();
			SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "Morrowind.ini");
		}

		/// <summary>
		/// Setup for the plugin management libraries.
		/// </summary>
		protected override void SetupPluginManagement(FileUtil p_futFileUtility)
		{
		}

		#endregion

		#region Plugin Management

		///// <summary>
		///// Gets the factory that builds plugins for this game mode.
		///// </summary>
		///// <returns>The factory that builds plugins for this game mode.</returns>
		//public override IPluginFactory GetPluginFactory()
		//{
		//	if (m_pgfPluginFactory == null)
		//		m_pgfPluginFactory = new GamebryoPluginFactory(PluginDirectory, BossSorter);
		//	return m_pgfPluginFactory;
		//}

		#endregion

		/// <summary>
		/// Adjusts the given path to be relative to the installation path of the game mode.
		/// </summary>
		/// <remarks>
		/// This is basically a hack to allow older FOMod/OMods to work. Older FOMods assumed
		/// the installation path of Fallout games to be &lt;games>/data, but this new manager specifies
		/// the installation path to be &lt;games>. This breaks the older FOMods, so this method can detect
		/// the older FOMods (or other mod formats that needs massaging), and adjusts the given path
		/// to be relative to the new instaalation path to make things work.
		/// </remarks>
		/// <param name="p_mftModFormat">The mod format for which to adjust the path.</param>
		/// <param name="p_strPath">The path to adjust</param>
		/// <param name="p_booIgnoreIfPresent">Whether to ignore the path if the specific root is already present</param>
		/// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
		public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, bool p_booIgnoreIfPresent)
		{
			if ((p_mftModFormat != null) && (p_mftModFormat.Id.Equals("FOMod") || p_mftModFormat.Id.Equals("OMod")))
			{
				if (p_booIgnoreIfPresent && !String.IsNullOrEmpty(p_strPath) && p_strPath.StartsWith("Data Files" + Path.DirectorySeparatorChar, System.StringComparison.InvariantCultureIgnoreCase))
					return p_strPath.Substring(11);
				else if (!p_booIgnoreIfPresent && !String.IsNullOrEmpty(p_strPath) && !p_strPath.StartsWith("Data Files" + Path.DirectorySeparatorChar, System.StringComparison.InvariantCultureIgnoreCase))
					return Path.Combine("Data Files", p_strPath ?? "");
				else if (!p_booIgnoreIfPresent && String.IsNullOrEmpty(p_strPath))
					return Path.Combine("Data Files", p_strPath ?? "");
			}
			else if (p_mftModFormat == null)
			{
				if (p_booIgnoreIfPresent && !String.IsNullOrEmpty(p_strPath) && p_strPath.StartsWith("Data Files" + Path.DirectorySeparatorChar, System.StringComparison.InvariantCultureIgnoreCase))
					return p_strPath.Substring(11);
				else if (!p_booIgnoreIfPresent && !String.IsNullOrEmpty(p_strPath) && !p_strPath.StartsWith("Data Files" + Path.DirectorySeparatorChar, System.StringComparison.InvariantCultureIgnoreCase))
					return Path.Combine("Data Files", p_strPath ?? "");
				else if (!p_booIgnoreIfPresent && String.IsNullOrEmpty(p_strPath))
					return Path.Combine("Data Files", p_strPath ?? "");
			}

			return p_strPath;
		}

		/// <summary>
		/// Sorts the plugins.
		/// </summary>
		/// <param name="p_lstPlugins">The list of plugin to order.</param>
		public override string[] SortPlugins(IList<Plugin> p_lstPlugins)
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
				m_gmdGameModeInfo = new MorrowindGameModeDescriptor(EnvironmentInfo);
			return m_gmdGameModeInfo;
		}
	}
}
