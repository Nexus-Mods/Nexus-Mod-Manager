using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Fallout3;
using Nexus.Client.Games.FalloutNV.Tools;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Tools;
using Nexus.Client.Util;
using Nexus.Client.Games.FalloutNV.Settings;
using Nexus.Client.Games.FalloutNV.Settings.UI;
using Nexus.Client.Settings.UI;

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// Provides information required for the programme to manage Fallout: New Vegas plugins and mods.
	/// </summary>
	public class FalloutNVGameMode : Fallout3GameMode
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "nvse_loader.exe" };
		private FalloutNVGameModeDescriptor m_gmdGameModeInfo = null;
		private FalloutNVLauncher m_glnGameLauncher = null;
		private FalloutNVToolLauncher m_gtlToolLauncher = null;
		private FalloutNVSupportedTools m_stlSupportedTools = null;

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
		/// Gets the path to the per user Fallout: New Vegas data.
		/// </summary>
		/// <value>The path to the per user Fallout: New Vegas data.</value>
		public override string UserGameDataPath
		{
			get
			{
				return Path.Combine(EnvironmentInfo.PersonalDataFolderPath, "My games\\FalloutNV");
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
					m_glnGameLauncher = new FalloutNVLauncher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new FalloutNVToolLauncher(this, EnvironmentInfo);
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
					m_stlSupportedTools = new FalloutNVSupportedTools(this, EnvironmentInfo);
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

		/// <summary>
		/// Gets the game Base files.
		/// </summary>
		/// <value>The default game categories stored in the resource file.</value>
		public override string BaseGameFiles
		{
			get
			{
				return Properties.Resources.falloutNV_base;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		public FalloutNVGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
			: base(p_eifEnvironmentInfo, p_futFileUtility)
		{
		
		}

		#endregion

		#region Initialization

		/// <summary>
		/// Instantiates the container to use to store the list of settings files.
		/// </summary>
		/// <returns>The container to use to store the list of settings files.</returns>
		protected override GamebryoSettingsFiles CreateSettingsFileContainer()
		{
			return new FalloutNVSettingsFiles();
		}

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{
			base.SetupSettingsFiles();
			((FalloutNVSettingsFiles)SettingsFiles).FODefaultIniPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath, "fallout_default.ini");
		}

		#endregion

		/// <summary>
		/// Creates a game mode descriptor for the current game mode.
		/// </summary>
		/// <returns>A game mode descriptor for the current game mode.</returns>
		protected override IGameModeDescriptor CreateGameModeDescriptor()
		{
			if (m_gmdGameModeInfo == null)
				m_gmdGameModeInfo = new FalloutNVGameModeDescriptor(EnvironmentInfo);
			return m_gmdGameModeInfo;
		}
	}
}
