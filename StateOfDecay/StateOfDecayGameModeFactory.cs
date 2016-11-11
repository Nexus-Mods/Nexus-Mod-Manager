using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Games.Steam;
using Nexus.Client.Settings;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.StateOfDecay
{
    /// <summary>
    /// The base game mode factory that provides the command functionality for
    /// factories that build game modes for State of Decay based games.
    /// </summary>
    public class StateOfDecayGameModeFactory : IGameModeFactory
    {
        private readonly IGameModeDescriptor m_gmdGameModeDescriptor = null;

        #region Properties

        /// <summary>
        /// Gets the application's environment info.
        /// </summary>
        /// <value>The application's environment info.</value>
        protected IEnvironmentInfo EnvironmentInfo { get; private set; }

        /// <summary>
        /// Gets the descriptor of the game mode that this factory builds.
        /// </summary>
        /// <value>The descriptor of the game mode that this factory builds.</value>
        public IGameModeDescriptor GameModeDescriptor
        {
            get
            {
                return m_gmdGameModeDescriptor;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given values.
        /// </summary>
        /// <param name="p_eifEnvironmentInfo">The application's environment info.</param>
        public StateOfDecayGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
        {
            EnvironmentInfo = p_eifEnvironmentInfo;
            m_gmdGameModeDescriptor = new StateOfDecayGameModeDescriptor(p_eifEnvironmentInfo);
        }

        #endregion

        /// <summary>
        /// Gets the path where mod files should be installed.
        /// </summary>
        /// <returns>The path where mod files should be installed, or
        /// <c>null</c> if the path could not be determined.</returns>
        public string GetInstallationPath()
        {
            string strValue = SteamInstallationPathDetector.Instance.GetSteamInstallationPath("241540", "state of decay", "StateOfDecay.exe");

            return strValue;
        }

        /// <summary>
        /// Gets the path where mod files should be installed.
        /// </summary>
        /// <remarks>
        /// This method uses the given path to the installed game
        /// to determine the installaiton path for mods.
        /// </remarks>
        /// <returns>The path where mod files should be installed, or
        /// <c>null</c> if the path could be be determined.</returns>
        public string GetInstallationPath(string p_strGameInstallPath)
        {
            return Path.Combine(p_strGameInstallPath, ""); // maynardloo 2014/01/20 - The mods should be installed in the root directory of the game's path.
                                                           //The second parameter would look for/create another folder in the game's directory that is not needed
        }

        /// <summary>
        /// Gets the path to the game executable.
        /// </summary>
        /// <returns>The path to the game executable, or
        /// <c>null</c> if the path could not be determined.</returns>
        public string GetExecutablePath(string p_strPath)
        {
            return p_strPath;
        }

        /// <summary>
        /// Builds the game mode.
        /// </summary>
        /// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
        /// <param name="p_imsWarning">The resultant warning resultant from the creation of the game mode.
        /// <c>null</c> if there are no warnings.</param>
        /// <returns>The game mode.</returns>
        public IGameMode BuildGameMode(FileUtil p_futFileUtility, out ViewMessage p_imsWarning)
        {
            if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();
            if (!EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId].ContainsKey("AskAboutReadOnlySettingsFiles"))
            {
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["AskAboutReadOnlySettingsFiles"] = true;
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["UnReadOnlySettingsFiles"] = true;
                EnvironmentInfo.Settings.Save();
            }

            StateOfDecayGameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
            p_imsWarning = null;

            return gmdGameMode;
        }

        /// <summary>
        /// Instantiates the game mode.
        /// </summary>
        /// <param name="p_futFileUtility">The file utility class to be used by the game mode.</param>
        /// <returns>The game mode for which this is a factory.</returns>
        protected StateOfDecayGameMode InstantiateGameMode(FileUtil p_futFileUtility)
        {
            return new StateOfDecayGameMode(EnvironmentInfo, p_futFileUtility);
        }

        /// <summary>
        /// Performs the initial setup for the game mode being created.
        /// </summary>
        /// <param name="p_dlgShowView">The delegate to use to display a view.</param>
        /// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
        /// <returns><c>true</c> if the setup completed successfully;
        /// <c>false</c> otherwise.</returns>
        public bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();

            StateOfDecaySetupVM vmlSetup = new StateOfDecaySetupVM(EnvironmentInfo, GameModeDescriptor);
            SetupForm frmSetup = new SetupForm(vmlSetup);
            if (((DialogResult)p_dlgShowView(frmSetup, true)) == DialogResult.Cancel)
                return false;
            return vmlSetup.Save();
        }

        /// <summary>
        /// Performs the initialization for the game mode being created.
        /// </summary>
        /// <param name="p_dlgShowView">The delegate to use to display a view.</param>
        /// <param name="p_dlgShowMessage">The delegate to use to display a message.</param>
        /// <returns><c>true</c> if the set-up completed successfully;
        /// <c>false</c> otherwise.</returns>
        public bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            return true;
        }
    }
}
