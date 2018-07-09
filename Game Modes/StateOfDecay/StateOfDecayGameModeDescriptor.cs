using System.Drawing;
using System.IO;

namespace Nexus.Client.Games.StateOfDecay
{
    /// <summary>
    /// Provides common information about StateOfDecay based games.
    /// </summary>
    public class StateOfDecayGameModeDescriptor : GameModeDescriptorBase
    {
        private static readonly string[] EXECUTABLES = { "StateOfDecay.exe" };
        private const string MODE_ID = "StateOfDecay";

        #region Properties

        /// <summary>
        /// Gets the directory where StateOfDecay plugins are installed.
        /// </summary>
        /// <value>The directory where StateOfDecay plugins are installed.</value>
        public override string PluginDirectory
        {
            get
            {
                return null;
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
                return "State of Decay";
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
        /// Gets the theme to use for this game mode.
        /// </summary>
        /// <value>The theme to use for this game mode.</value>
        public override Theme ModeTheme
        {
            get
            {
                return new Theme(Properties.Resources.sod_nexus_logo, Color.FromArgb(250, 167, 64), null);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        public StateOfDecayGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
            : base(p_eifEnvironmentInfo)
        {
        }

        #endregion
    }
}
