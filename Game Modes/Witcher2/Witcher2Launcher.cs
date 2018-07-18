using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Witcher2
{
    /// <summary>
	/// Launches The Witcher 2.
    /// </summary>
    public class Witcher2Launcher : GameLauncherBase
    {
        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
        public Witcher2Launcher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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

			string strCommand = GetCMLaunchCommand();
			string strCMIcon = Path.Combine(GameMode.ExecutablePath, "editor.release.exe");
			Trace.TraceInformation("CM Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			Image imgIcon = File.Exists(strCMIcon) ? Icon.ExtractAssociatedIcon(strCMIcon).ToBitmap() : null;
			AddLaunchCommand(new Command("CMLaunch", "Launch The Witcher 2 CM", "Launches The Witcher 2 Content Manager.", imgIcon, LaunchWitcher2CM, true));

            strCommand = GetPlainLaunchCommand();
            Trace.TraceInformation("Plain Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
            AddLaunchCommand(new Command("PlainLaunch", "Launch The Witcher 2", "Launches The Witcher 2.", imgIcon, LaunchWitcher2Plain, true));

            strCommand = GetCustomLaunchCommand();
            Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
            AddLaunchCommand(new Command("CustomLaunch", "Launch The Witcher 2 Custom", "Launches The Witcher 2 Custom.", imgIcon, LaunchWitcher2Custom, true));

			DefaultLaunchCommand = new Command("Launch The Witcher 2 Content Manager", "Launches The Witcher 2 Content Manager.", LaunchGame);

            Trace.Unindent();
        }

        #region Launch Commands

        #region Custom Command

        /// <summary>
        /// Launches the game with a custom command.
        /// </summary>
        private void LaunchWitcher2Custom()
        {
			Trace.TraceInformation("Launching The Witcher 2 (Custom)...");
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
        /// Launches the game.
        /// </summary>
        private void LaunchWitcher2Plain()
        {
			Trace.TraceInformation("Launching The Witcher 2...");
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
			string strCommand = Path.Combine(GameMode.ExecutablePath, "Witcher2.exe");
            return strCommand;
        }

        #endregion

		#region CM Launch

		/// <summary>
		/// Launches the game's content manager.
		/// </summary>
		private void LaunchWitcher2CM()
		{
			Trace.TraceInformation("Launching The Witcher 2 Content Manager...");
			Trace.Indent();
			string strCommand = GetCMLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the CM launch command.
		/// </summary>
		/// <returns>The CM launch command.</returns>
		private string GetCMLaunchCommand()
		{
			string strCommand = Path.Combine(GameMode.ExecutablePath, "userContentManager.exe");
			return strCommand;
		}

		#endregion

        /// <summary>
        /// Launches the game, using FOSE if present.
        /// </summary>
        private void LaunchGame()
        {
            if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
                LaunchWitcher2Custom();
            else
                LaunchWitcher2Plain();
        }

        #endregion
    }
}
