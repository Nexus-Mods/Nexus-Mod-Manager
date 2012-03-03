using System.IO;
using Nexus.Client.Games.Fallout3;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Skyrim.Tools;
using Nexus.Client.Games.Tools;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Skyrim
{
	/// <summary>
	/// Provides information required for the programme to manage Skyrim plugins and mods.
	/// </summary>
	public class SkyrimGameMode : Fallout3GameMode
	{
		private static string[] SCRIPT_EXTENDER_EXECUTABLES = { "skse_loader.exe" };
		private static string[] CRITICAL_PLUGINS = { "skyrim.esm", "update.esm" };
		private SkyrimGameModeDescriptor m_gmdGameModeInfo = new SkyrimGameModeDescriptor();
		private SkyrimLauncher m_glnGameLauncher = null;
		private SkyrimToolLauncher m_gtlToolLauncher = null;

		#region Properties

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables
		{
			get
			{
				return m_gmdGameModeInfo.GameExecutables;
			}
		}

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
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return m_gmdGameModeInfo.Name;
			}
		}

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override string ModeId
		{
			get
			{
				return m_gmdGameModeInfo.ModeId;
			}
		}

		/// <summary>
		/// Gets the theme to use for this game mode.
		/// </summary>
		/// <value>The theme to use for this game mode.</value>
		public override Theme ModeTheme
		{
			get
			{
				return m_gmdGameModeInfo.ModeTheme;
			}
		}

		/// <summary>
		/// Gets the path to the per user Skyrim data.
		/// </summary>
		/// <value>The path to the per user Skyrim data.</value>
		public override string UserGameDataPath
		{
			get
			{
				return Path.Combine(EnvironmentInfo.PersonalDataFolderPath, "My games\\Skyrim");
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
					m_glnGameLauncher = new SkyrimLauncher(this, EnvironmentInfo);
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
					m_gtlToolLauncher = new SkyrimToolLauncher(this, EnvironmentInfo);
				return m_gtlToolLauncher;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public override string[] OrderedCriticalPluginNames
		{
			get
			{
				return CRITICAL_PLUGINS;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
		/// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
		public SkyrimGameMode(IEnvironmentInfo p_eifEnvironmentInfo, FileUtil p_futFileUtility)
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
			return new SkyrimSettingsFiles();
		}

		/// <summary>
		/// Adds the settings files to the game mode's list.
		/// </summary>
		protected override void SetupSettingsFiles()
		{	
			base.SetupSettingsFiles();
			SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "Skyrim.ini");
			((FalloutSettingsFiles)SettingsFiles).FOPrefsIniPath = Path.Combine(UserGameDataPath, "SkyrimPrefs.ini");
		}

		#endregion
	}
}
