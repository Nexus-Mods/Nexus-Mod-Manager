using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.TESO
{
    /// <summary>
	/// Launches TESO.
    /// </summary>
    public class TESOLauncher : GameLauncherBase
    {
		private string ESOLaunchPath;

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with the given dependencies.
        /// </summary>
        /// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
        /// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public TESOLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			AddLaunchCommand(new Command("PlainLaunch", "Launch The Elder Scrolls Online", "Launches plain The Elder Scrolls Online.", imgIcon, LaunchTESOPlain, true));

			strCommand = GetESOLauncherLaunchCommand();
			Trace.TraceInformation("ESO Launcher Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if (File.Exists(strCommand))
			{
				imgIcon = Icon.ExtractAssociatedIcon(strCommand).ToBitmap();
				AddLaunchCommand(new Command("ESOLauncher", "Launch the ESO Launcher", "Launches the ESO Launcher.", imgIcon, LaunchESOLauncher, true));
			}

            strCommand = GetCustomLaunchCommand();
            Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("CustomLaunch", "Launch Custom The Elder Scrolls Online", "Launches The Elder Scrolls Online with custom command.", imgIcon, LaunchTESOCustom, true));

			DefaultLaunchCommand = new Command("Launch the ESO Launcher", "Launches the ESO Launcher.", LaunchGame);

            Trace.Unindent();
        }

        #region Launch Commands

        #region Custom Command

        /// <summary>
        /// Launches the game with a custom command.
        /// </summary>
		private void LaunchTESOCustom()
        {
			Trace.TraceInformation("Launching Elder Scrolls Online (Custom)...");
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
        /// Launches the game without the launcher.
        /// </summary>
		private void LaunchTESOPlain()
        {
			Trace.TraceInformation("Launching Elder Scrolls Online (Plain)...");
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
			string strCommand = Path.Combine(GameMode.ExecutablePath, "eso.exe");
            return strCommand;
        }

        #endregion

		#region Launcher

		/// <summary>
		/// Launches the ESO Launcher.
		/// </summary>
		private void LaunchESOLauncher()
		{
			Trace.TraceInformation("Launching ESO Launcher...");
			Trace.Indent();

			string strCommand = GetESOLauncherLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);

			if (!File.Exists(strCommand))
			{
				Trace.TraceError("ESO Launcher does not appear to be installed.");
				Trace.Unindent();
				OnGameLaunched(false, "ESO Launcher does not appear to be installed.");
				return;
			}
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the ESO Launcher launch command.
		/// </summary>
		/// <returns>The ESO Launcher launch command.</returns>
		private string GetESOLauncherLaunchCommand()
		{
			try
			{
				string strInstallPath = Path.GetDirectoryName(Path.GetDirectoryName(GameMode.ExecutablePath));
				ESOLaunchPath = Path.Combine(Path.GetDirectoryName(strInstallPath), Path.Combine("Launcher", "Bethesda.net_Launcher.exe"));
				return ESOLaunchPath;
			}
			catch
			{
				return String.Empty;
			}
		}

		#endregion

        /// <summary>
        /// Launches the game, using the custom command or the launcher if present.
        /// </summary>
        private void LaunchGame()
        {
			if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchTESOCustom();
			else if (File.Exists(ESOLaunchPath))
				LaunchESOLauncher();
			else
				LaunchTESOPlain();
        }

        #endregion
    }
}
