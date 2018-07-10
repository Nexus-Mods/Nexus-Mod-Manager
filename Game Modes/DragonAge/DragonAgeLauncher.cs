using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DragonAge
{
	/// <summary>
	/// Launches Dragon Age.
	/// </summary>
	public class DragonAgeLauncher : GameLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public DragonAgeLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			AddLaunchCommand(new Command("PlainLaunch", "Launch Dragon Age", "Launches Dragon Age.", imgIcon, LaunchDragonAgePlain, true));

			strCommand = GetCustomLaunchCommand();
			Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("CustomLaunch", "Launch Dragon Age Custom", "Launches Dragon Age Custom.", imgIcon, LaunchDragonAgeCustom, true));

			strCommand = GetLauncherLaunchCommand();
			Trace.TraceInformation("Launcher Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("LauncherLaunch", "Launch Dragon Age Launcher", "Launches Dragon Age Launcher.", imgIcon, LaunchDragonAgeLauncher, true));

			DefaultLaunchCommand = new Command("Launch Dragon Age", "Launches Dragon Age", LaunchGame);

			Trace.Unindent();
		}

		#region Launch Commands

		#region Custom Command

		/// <summary>
		/// Launches the game with a custom command.
		/// </summary>
		private void LaunchDragonAgeCustom()
		{
			Trace.TraceInformation("Launching Dragon Age (Custom)...");
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

		/// <summary>
		/// Launches the game with the Luancher.
		/// </summary>
		private void LaunchDragonAgeLauncher()
		{
			Trace.TraceInformation("Launching Dragon Age (Launcher)...");
			Trace.Indent();

			string strCommand = GetLauncherLaunchCommand();
			string strCommandArgs = EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId];
			if (String.IsNullOrEmpty(strCommand))
			{
				Trace.TraceError("No launcher launch command has been set.");
				Trace.Unindent();
				OnGameLaunched(false, "No launcher launch command has been set.");
				return;
			}
			Launch(strCommand, strCommandArgs);
		}

		/// <summary>
		/// Gets the launcher launch command.
		/// </summary>
		/// <returns>The custom launch command.</returns>
		private string GetLauncherLaunchCommand()
		{
			string strPath = Path.GetDirectoryName(GameMode.GameModeEnvironmentInfo.ExecutablePath);
			return Path.Combine(strPath, "DAOriginsLauncher.exe");
		}

		#endregion

		#region Vanilla Launch

		/// <summary>
		/// Launches the game.
		/// </summary>
		private void LaunchDragonAgePlain()
		{
			Trace.TraceInformation("Launching Dragon Age ...");
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
			string strCommand = Path.Combine(GameMode.ExecutablePath, "daorigins.exe");
			return strCommand;
		}

		#endregion

		/// <summary>
		/// Launches the game, using FOSE if present.
		/// </summary>
		private void LaunchGame()
		{
			if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchDragonAgeCustom();
			else
				LaunchDragonAgePlain();
		}

		#endregion
	}
}
