using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Sims4
{
    /// <summary>
	/// Launches Sims4.
    /// </summary>
    public class Sims4Launcher : GameLauncherBase
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public Sims4Launcher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			AddLaunchCommand(new Command("PlainLaunch", "Launch The Sims 4", "Launches plain The Sims 4.", imgIcon, LaunchSims4Plain, true));

            strCommand = GetCustomLaunchCommand();
            Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("CustomLaunch", "Launch Custom The Sims 4", "Launches The Sims 4 with custom command.", imgIcon, LaunchSims4Custom, true));

			DefaultLaunchCommand = new Command("Launch The Sims 4", "Launches The Sims 4.", LaunchGame);

            Trace.Unindent();
        }

        #region Launch Commands

        #region Custom Command

        /// <summary>
        /// Launches the game with a custom command.
        /// </summary>
		private void LaunchSims4Custom()
        {
			Trace.TraceInformation("Launching The Sims 4 (Custom)...");
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
                    strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.ExecutablePath, strCommand);
            }
            return strCommand;
        }

        #endregion

        #region Vanilla Launch

        /// <summary>
        /// Launches the game, without OBSE.
        /// </summary>
		private void LaunchSims4Plain()
        {
			Trace.TraceInformation("Launching The Sims 4 (Plain)...");
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
			string strCommand = Path.Combine(GameMode.ExecutablePath, @"Game\Bin\TS4_x64.exe");
            return strCommand;
        }

        #endregion

        /// <summary>
        /// Launches the game, using FOSE if present.
        /// </summary>
        private void LaunchGame()
        {
            if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchSims4Custom();
            else
				LaunchSims4Plain();
        }

        #endregion
    }
}
