using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Morrowind
{
    /// <summary>
    /// Launches Morrowind.
    /// </summary>
    public class MorrowindLauncher : GameLauncherBase
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        public MorrowindLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
            : base(p_gmdGameMode, p_eifEnvironmentInfo)
        {
        }

        #endregion

        /// <summary>
        /// Initializes the game launch commands.
        /// </summary>
        protected override void SetupCommands()
        {
            Trace.TraceInformation("Launch Commands:");
            Trace.Indent();

            ClearLaunchCommands();

            string strCommand = GetPlainLaunchCommand();
            Trace.TraceInformation("Plain Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            Image imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
            AddLaunchCommand(new Command("PlainLaunch", "Launch Morrowind", "Launches plain Morrowind.", imgIcon, LaunchMorrowindPlain, true));

            strCommand = GetMwseLaunchCommand();
            Trace.TraceInformation("MWSE Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            if (File.Exists(strCommand))
            {
                imgIcon = Icon.ExtractAssociatedIcon(strCommand).ToBitmap();
                AddLaunchCommand(new Command("MwseLaunch", "Launch MWSE", "Launches Morrowind with MWSE.", imgIcon, LaunchMorrowindMWSE, true));
            }

            strCommand = GetCustomLaunchCommand();
            Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
            AddLaunchCommand(new Command("CustomLaunch", "Launch Custom Morrowind", "Launches Morrowind with custom command.", imgIcon, LaunchMorrowindCustom, true));

            DefaultLaunchCommand = new Command("Launch Morrowind", "Launches Morrowind.", LaunchGame);

            Trace.Unindent();
        }

        #region Launch Commands

        #region Custom Command

        /// <summary>
        /// Launches the game with a custom command.
        /// </summary>
        private void LaunchMorrowindCustom()
        {
            Trace.TraceInformation("Launching Morrowind (Custom)...");
            Trace.Indent();

            string strCommand = GetCustomLaunchCommand();
            string strCommandArgs = EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId];
            if (String.IsNullOrEmpty(strCommand))
            {
                Trace.TraceError("No custom launch command has been set.");
                Trace.Unindent();
                OnGameLaunched(false, "No custom launch command has been set.");
                return;
            }
            Launch(strCommand, strCommandArgs);
        }

        /// <summary>
        /// Gets the custom launch command.
        /// </summary>
        /// <returns>The custom launch command.</returns>
        private string GetCustomLaunchCommand()
        {
            string strCommand = EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId];
            if (!String.IsNullOrEmpty(strCommand))
            {
                strCommand = Environment.ExpandEnvironmentVariables(strCommand);
                strCommand = FileUtil.StripInvalidPathChars(strCommand);
                if (!Path.IsPathRooted(strCommand))
                    strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, strCommand);
            }
            return strCommand;
        }

        #endregion

        #region MWSE

        /// <summary>
        /// Launches the game, with MWSE.
        /// </summary>
        private void LaunchMorrowindMWSE()
        {
            Trace.TraceInformation("Launching Morrowind (MWSE)...");
            Trace.Indent();

            string strCommand = GetMwseLaunchCommand();
            Trace.TraceInformation("Command: " + strCommand);

            if (!File.Exists(strCommand))
            {
                Trace.TraceError("MWSE does not appear to be installed.");
                Trace.Unindent();
                OnGameLaunched(false, "MWSE does not appear to be installed.");
                return;
            }
            Launch(strCommand, null);
        }

        /// <summary>
        /// Gets the OBSE launch command.
        /// </summary>
        /// <returns>The OBSE launch command.</returns>
        private string GetMwseLaunchCommand()
        {
            return Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "mwse_loader.exe");
        }

        #endregion

        #region Vanilla Launch

        /// <summary>
        /// Launches the game, without OBSE.
        /// </summary>
        private void LaunchMorrowindPlain()
        {
            Trace.TraceInformation("Launching Morrowind (Plain)...");
            Trace.Indent();
            string strCommand = GetPlainLaunchCommand();
            Trace.TraceInformation("Command: " + strCommand);
            Launch(strCommand, null);
        }

        /// <summary>
        /// Gets the plain launch command.
        /// </summary>
        /// <returns>The plain launch command.</returns>
        private string GetPlainLaunchCommand()
        {
            string strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "morrowind.exe");
            return strCommand;
        }

        #endregion

        /// <summary>
        /// Launches the game, using FOSE if present.
        /// </summary>
        private void LaunchGame()
        {
            if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
                LaunchMorrowindCustom();
            else if (File.Exists(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "mwse_loader.exe")))
                LaunchMorrowindMWSE();
            else
                LaunchMorrowindPlain();
        }

        #endregion
    }
}
