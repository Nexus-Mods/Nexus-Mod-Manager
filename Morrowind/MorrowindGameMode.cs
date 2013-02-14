using System.IO;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Morrowind.Tools;
using Nexus.Client.Games.Tools;
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
            SettingsFiles.IniPath = Path.Combine(UserGameDataPath, "Morrowind.ini");
        }

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
        /// <returns>The given path, adjusted to be relative to the installation path of the game mode.</returns>
        public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath)
        {
            if (p_mftModFormat.Id.Equals("FOMod") || p_mftModFormat.Id.Equals("OMod"))
                return Path.Combine("Data Files", p_strPath ?? "");
            return p_strPath;
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
