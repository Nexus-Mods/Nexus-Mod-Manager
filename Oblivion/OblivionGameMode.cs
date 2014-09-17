using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Oblivion.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.Util;
using Nexus.Client.Games.Oblivion.Settings;
using Nexus.Client.Games.Oblivion.Settings.UI;
using Nexus.Client.Settings.UI;

namespace Nexus.Client.Games.Oblivion
{
	/// <summary>
	/// Provides information required for the programme to manage Oblivion plugins and mods.
	/// </summary>
	public class OblivionGameMode : GamebryoGameModeBase
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "obse_loader.exe" };
		private OblivionGameModeDescriptor m_gmdGameModeInfo = null;
		private OblivionLauncher m_glnGameLauncher = null;
		private OblivionToolLauncher m_gtlToolLauncher = null;
		private OblivionSupportedTools m_stlSupportedTools = null;

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
		/// Gets the path to the per user Fallout 3 data.
		/// </summary>
		/// <value>The path to the per user Fallout 3 data.</value>
		public override string UserGameDataPath
		{
			get
			{
				return Path.Combine(EnvironmentInfo.PersonalDataFolderPath, "My games\\Oblivion");
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
					m_glnGameLauncher = new OblivionLauncher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new OblivionToolLauncher(this, EnvironmentInfo);
				return m_gtlToolLauncher;
			}
		}

		/// <summary>
		/// Gets the supported tool launcher for the game mode.
		/// </summary>
		/// <value>The supported tool launcher for the game mode.</value>
		public override ISupportedToolsLauncher SupportedToolsLauncher
		{
			get
			{
				if (m_stlSupportedTools == null)
					m_stlSupportedTools = new OblivionSupportedTools(this, EnvironmentInfo);
				return m_stlSupportedTools;
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

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		public OblivionGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo, p_futFileUtility)
		{
			SupportedToolsGroupViews = new List<ISettingsGroupView>();
			SupportedToolsSettingsGroup stsgSupported = new SupportedToolsSettingsGroup(p_eifEnvironmentInfo, this);
			((List<ISettingsGroupView>)SupportedToolsGroupViews).Add(new SupportedToolsSettingsPage(stsgSupported));
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
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{	
			base.SetupSettingsFiles();
			SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "oblivion.ini");
		}

		#endregion

		/// <summary>
		/// Creates a game mode descriptor for the current game mode.
		/// </summary>
		/// <returns>A game mode descriptor for the current game mode.</returns>
		protected override IGameModeDescriptor CreateGameModeDescriptor()
		{
			if (m_gmdGameModeInfo == null)
				m_gmdGameModeInfo = new OblivionGameModeDescriptor(EnvironmentInfo);
			return m_gmdGameModeInfo;
		}
	}
}
