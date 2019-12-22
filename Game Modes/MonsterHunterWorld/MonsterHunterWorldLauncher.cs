using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.MonsterHunterWorld
{
	/// <summary>
	/// Launches MonsterHunterWorld.
	/// </summary>
public class MonsterHunterWorldLauncher : GameLauncherBase
{
#region Constructors

/// <summary>
/// A simple constructor that initializes the object with the given dependencies.
/// </summary>
/// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
public MonsterHunterWorldLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			AddLaunchCommand(new Command("PlainLaunch", "Launch Monster Hunter: World", "Launches default Monster Hunter: World.", imgIcon, LaunchMonsterHunterWorldPlain, true));

			strCommand = GetCustomLaunchCommand();
			Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("CustomLaunch", "Launch Custom Monster Hunter: World", "Launches Monster Hunter: World with custom command.", imgIcon, LaunchMonsterHunterWorldCustom, true));

			DefaultLaunchCommand = new Command("Launch Monster Hunter: World", "Launches Monster Hunter: World.", LaunchGame);

			Trace.Unindent();
		}

		#region Launch Commands

		#region Custom Command

		/// <summary>
		/// Launches the game with a custom command.
		/// </summary>
		private void LaunchMonsterHunterWorldCustom()
		{
			Trace.TraceInformation("Launching Monster Hunter: World (Custom)...");
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
		private void LaunchMonsterHunterWorldPlain()
		{
			Trace.TraceInformation("Launching Monster Hunter: World (Default)...");
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
			string strCommand = Path.Combine(GameMode.ExecutablePath, "MonsterHunterWorld.exe");
			return strCommand;
		}

		#endregion

		/// <summary>
		/// Launches the game
		/// </summary>
		private void LaunchGame()
		{
			if (!string.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchMonsterHunterWorldCustom();
			else
				LaunchMonsterHunterWorldPlain();
		}

		#endregion
	}
}
