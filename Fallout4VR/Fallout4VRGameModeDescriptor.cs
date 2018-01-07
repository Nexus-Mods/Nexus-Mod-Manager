using System.Drawing;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Fallout4VR
{
	/// <summary>
	/// Provides the basic information about the Fallout4 game mode.
	/// </summary>
	public class Fallout4VRGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "Fallout4Launcher.exe" };
		private static string[] CRITICAL_PLUGINS = { "Fallout4.esm" };
		private static string[] OFFICIAL_PLUGINS = { "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm", "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm" };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = { "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm", "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
																"ccbgsfo4001-pipboy(black).esl",
																"ccbgsfo4002-pipboy(blue).esl",
																"ccbgsfo4003-pipboy(camo01).esl",
																"ccbgsfo4004-pipboy(camo02).esl",
																"ccbgsfo4006-pipboy(chrome).esl",
																"ccbgsfo4012-pipboy(red).esl",
																"ccbgsfo4014-pipboy(white).esl",
																"ccbgsfo4016-prey.esl",
																"ccbgsfo4017-mauler.esl",
																"ccbgsfo4018-gaussrifleprototype.esl",
																"ccbgsfo4019-chinesestealtharmor.esl",
																"ccbgsfo4020-powerarmorskin(black).esl",
																"ccbgsfo4038-horsearmor.esl",
																"ccbgsfo4039-tunnelsnakes.esl",
																"ccbgsfo4041-doommarinearmor.esl",
																"ccbgsfo4042-bfg.esl",
																"ccbgsfo4043-doomchainsaw.esl",
																"ccbgsfo4044-hellfirepowerarmor.esl",
																"ccfsvfo4001-modularmilitarybackpack.esl",
																"ccfsvfo4002-midcenturymodern.esl",
																"ccfrsfo4001-handmadeshotgun.esl",
																"cceejfo4001-decorationpack.esl" };
        private const string MODE_ID = "Fallout4VR";

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Fallout 4 VR";
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
				return MODE_ID;
			}
		}

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables
		{
			get
			{
				return EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected override string[] OrderedCriticalPluginFilenames
		{
			get
			{
				return CRITICAL_PLUGINS;
			}
		}

        /// <summary>
        /// Gets the list of official plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official plugin names, ordered by load order.</value>
        protected override string[] OrderedOfficialPluginFilenames
		{
			get
			{
				return OFFICIAL_PLUGINS;
			}
		}

        /// <summary>
        /// Gets the list of official unmanageable plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official unmanageable plugin names, ordered by load order.</value>
        protected override string[] OrderedOfficialUnmanagedPluginFilenames
        {
            get
            {
                return OFFICIAL_UNMANAGED_PLUGINS;
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
				return new Theme(Properties.Resources.Fallout4VR_logo,Color.FromArgb(50, 104, 158),null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public Fallout4VRGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
