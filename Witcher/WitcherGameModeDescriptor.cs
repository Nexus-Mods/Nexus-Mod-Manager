using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nexus.Client.Games.Witcher
{
    public class WitcherGameModeDescriptor : GameModeDescriptorBase
    {
        private static string[] EXECUTABLES = { "launcher.exe", "System\\djinni!.exe", "System\\witcher.exe" };
        private const string MODE_ID = "Witcher";


        #region Properties
        
        /// <summary>
        /// Gets the directory where The Witcher plugins are installed.
        /// </summary>
        /// <value>The directory where The Witcher plugins are installed.</value>
        public override string PluginDirectory
        {
            get
            {
                string strPath = Path.Combine(Path.GetDirectoryName(ExecutablePath), "Data", "Override");
                if (!Directory.Exists(strPath))
                    Directory.CreateDirectory(strPath);
                return strPath;
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
                return "The Witcher";
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
                return new Theme(Properties.Resources.witcher_logo, Color.FromArgb(80, 45, 23), null);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        public WitcherGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
        }

        #endregion
    }
}
