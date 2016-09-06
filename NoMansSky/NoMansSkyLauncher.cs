using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.NoMansSky
{
	/// <summary>
	/// Launches NoMansSky.
	/// </summary>
public class NoMansSkyLauncher : GameLauncherBase
{
#region Constructors

/// <summary>
/// A simple constructor that initializes the object with the given dependencies.
/// </summary>
/// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
public NoMansSkyLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			AddLaunchCommand(new Command("PlainLaunch", "Launch No Man's Sky", "Launches default No Man's Sky.", imgIcon, LaunchNoMansSkyPlain, true));

            strCommand = GetNmseLaunchCommand();
            Trace.TraceInformation("NMSE Command: {0} (IsNull={1})", strCommand, (strCommand == null));
            if (File.Exists(strCommand))
            {
                imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
                AddLaunchCommand(new Command("NMSELaunch", "Launch No Man's Sky using NMSE", "Launches No Man's Sky using the Extender", imgIcon, LaunchNoMansSkyExtender, true));
            }

			strCommand = GetCustomLaunchCommand();
			Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("CustomLaunch", "Launch Custom No Man's Sky", "Launches No Man's Sky with custom command.", imgIcon, LaunchNoMansSkyCustom, true));

			DefaultLaunchCommand = new Command("Launch No Man's Sky", "Launches No Man's Sky.", LaunchGame);

			Trace.Unindent();
		}

        #region Launch Commands

        #region Custom Command

        /// <summary>
        /// Launches the game using No Man's Sky Extender
        /// </summary>
        private void LaunchNoMansSkyExtender()
        {
            Trace.TraceInformation("Launching No Man's Sky (extender)...");
            Trace.Indent();

            string strCommand = GetNmseLaunchCommand();
            string[] strRequiredDlls = new[] { "NMSE_Core_1_0", "NMSE_steam" };
            string strBinariesFolder = Directory.GetParent(GetNmseLaunchCommand()).FullName;

            if(!File.Exists(Path.Combine(strBinariesFolder, strRequiredDlls[0])) || !File.Exists(Path.Combine(strBinariesFolder, strRequiredDlls[1])) || !File.Exists(strCommand))
            {
                
            }
        }

        private string GetNmseLaunchCommand()
        {
            return Path.Combine(Directory.GetParent(GameMode.GameModeEnvironmentInfo.InstallationPath).FullName, "Binaries", "NMSELauncher.exe");
        }

        /// <summary>
        /// Launches the game with a custom command.
        /// </summary>
        private void LaunchNoMansSkyCustom()
		{
			Trace.TraceInformation("Launching No Man's Sky (Custom)...");
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
		/// Launches the game in DX mode.
		/// </summary>
		private void LaunchNoMansSkyPlain()
		{
			Trace.TraceInformation("Launching No Man's Sky (Default)...");
			Trace.Indent();
			string strCommand = GetPlainLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);
			Launch(strCommand, null);
		}


		/// <summary>
		/// Gets the default launch command.
		/// </summary>
		/// <returns>The default launch command.</returns>
		private string GetPlainLaunchCommand()
		{
			string strCommand = GameMode.ExecutablePath;

			if (strCommand.IndexOf("steam", StringComparison.InvariantCultureIgnoreCase) >= 0)
				strCommand = @"steam://run/275850";
			else
				strCommand = Path.Combine(strCommand, "Binaries", "NMS.exe");

			return strCommand;
		}

        #endregion

        /// <summary>
        /// Launches the game
        /// </summary>
        private void LaunchGame()
		{
			if (!string.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchNoMansSkyCustom();
			else
				LaunchNoMansSkyPlain();
		}

		#endregion
	}
}
