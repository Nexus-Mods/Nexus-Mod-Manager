using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.Skyrim
{
	/// <summary>
	/// Launches Skyrim.
	/// </summary>
	public class SkyrimLauncher : GameLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public SkyrimLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
			:base(p_gmdGameMode, p_eifEnvironmentInfo)
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

			Image imgIcon = null;

			string strCommand = GetPlainLaunchCommand();
			Trace.TraceInformation("Plain Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if (File.Exists(strCommand))
			{
				var icon = string.IsNullOrEmpty(strCommand) ? null : Icon.ExtractAssociatedIcon(strCommand);
				imgIcon = icon == null ? null : icon.ToBitmap();
			}
			AddLaunchCommand(new Command("PlainLaunch", LanguageManager.Format("GameModes.Commands.Game.LaunchName", "Launch {0}", "Skyrim"), LanguageManager.Format("GameModes.Commands.Game.PlainLaunchDescription", "Launches plain {0}.", "Skyrim"), imgIcon, LaunchSkyrimPlain, true));

			strCommand = GetSkseLaunchCommand();
			Trace.TraceInformation("SKSE Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if (File.Exists(strCommand))
			{
				var icon = string.IsNullOrEmpty(strCommand) ? null : Icon.ExtractAssociatedIcon(strCommand);
				imgIcon = icon == null ? null : icon.ToBitmap();
			}
			AddLaunchCommand(new Command("SkseLaunch", LanguageManager.Format("GameModes.Commands.Tool.LaunchName", "Launch {0}", "SKSE"), AppendExecutablePathToDescription(LanguageManager.Format("GameModes.Commands.Game.LaunchWithToolDescription", "Launches {0} with {1}.", "Skyrim", "SKSE"), strCommand), imgIcon, LaunchSkyrimSKSE, true));

			strCommand = GetCustomLaunchCommand();
			Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if (File.Exists(strCommand))
			{
				var icon = string.IsNullOrEmpty(strCommand) ? null : Icon.ExtractAssociatedIcon(strCommand);
				imgIcon = icon == null ? null : icon.ToBitmap();
			}
			AddLaunchCommand(new Command("CustomLaunch", LanguageManager.Format("GameModes.Commands.Game.CustomLaunchName", "Launch Custom {0}", "Skyrim"), LanguageManager.Format("GameModes.Commands.Game.CustomLaunchDescription", "Launches {0} with custom command.", "Skyrim"), imgIcon, LaunchSkyrimCustom, true));

			DefaultLaunchCommand = new Command(LanguageManager.Format("GameModes.Commands.Game.LaunchName", "Launch {0}", "Skyrim"), LanguageManager.Format("GameModes.Commands.Game.LaunchDescription", "Launches {0}.", "Skyrim"), LaunchGame);
			Trace.Unindent();
		}

		#region Launch Commands

		#region Custom Command

		/// <summary>
		/// Launches the game with a custom command.
		/// </summary>
		private void LaunchSkyrimCustom()
		{
			Trace.TraceInformation("Launching Skyrim (Custom)...");
			Trace.Indent();

			string strCommand = GetCustomLaunchCommand();
			string strCommandArgs = EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId];
			if (String.IsNullOrEmpty(strCommand))
			{
				Trace.TraceError("No custom launch command has been set.");
				Trace.Unindent();
				OnGameLaunched(false, LanguageManager.Get("GameModes.Launch.NoCustomCommand", "No custom launch command has been set."));
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

		#region SKSE

		/// <summary>
		/// Launches the game, with SKSE.
		/// </summary>
		private void LaunchSkyrimSKSE()
		{
			Trace.TraceInformation("Launching Skyrim (SKSE)...");
			Trace.Indent();

			string strCommand = GetSkseLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);

			if (!File.Exists(strCommand))
			{
				Trace.TraceError("SKSE does not appear to be installed.");
				Trace.Unindent();
				OnGameLaunched(false, LanguageManager.Format("GameModes.Launch.ToolNotInstalled", "{0} does not appear to be installed.", "SKSE"));
				return;
			}
			LaunchDirectExecutable(strCommand, null, GameMode.GameModeEnvironmentInfo.InstallationPath);
		}

		/// <summary>
		/// Gets the SKSE launch command.
		/// </summary>
		/// <returns>The SKSE launch command.</returns>
		private string GetSkseLaunchCommand()
		{
			return Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "skse_loader.exe");
		}

		#endregion

		#region Vanilla Launch

		/// <summary>
		/// Launches the game, without OBSE.
		/// </summary>
		private void LaunchSkyrimPlain()
		{
			Trace.TraceInformation("Launching Skyrim (Plain)...");
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
			string strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "SkyrimLauncher.exe");
			return strCommand;
		}

		#endregion

		/// <summary>
		/// Launches the game, using SKSE if present.
		/// </summary>
		private void LaunchGame()
		{
			if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchSkyrimCustom();
			else if (File.Exists(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "skse_loader.exe")))
				LaunchSkyrimSKSE();
			else
				LaunchSkyrimPlain();
		}

		#endregion
	}
}
