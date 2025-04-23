using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.OblivionRemastered
{
	/// <summary>
	/// Launches OblivionRemastered.
	/// </summary>
	public class OblivionRemasteredLauncher : GameLauncherBase
	{
		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public OblivionRemasteredLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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

			string strCommand = GetPlainLaunchCommand();
			Trace.TraceInformation("Plain Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			Image imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("PlainLaunch", "Launch Oblivion Remastered", "Launches plain Oblivion Remastered.", imgIcon, LaunchOblivionRemasteredPlain, true));
		
			strCommand = GetOrseLaunchCommand();
			Trace.TraceInformation("ORSE Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			if (File.Exists(strCommand))
			{
				imgIcon = Icon.ExtractAssociatedIcon(strCommand).ToBitmap();
				AddLaunchCommand(new Command("OrseLaunch", "Launch ORSE", "Launches Oblivion Remastered with ORSE.", imgIcon, LaunchOblivionRemasteredORSE, true));
			}
			
			strCommand = GetCustomLaunchCommand();
			Trace.TraceInformation("Custom Command: {0} (IsNull={1})", strCommand, (strCommand == null));
			imgIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand).ToBitmap() : null;
			AddLaunchCommand(new Command("CustomLaunch", "Launch Custom Oblivion Remastered", "Launches Oblivion Remastered with custom command.", imgIcon, LaunchOblivionRemasteredCustom, true));

			DefaultLaunchCommand = new Command("Launch Oblivion Remastered", "Launches Oblivion Remastered.", LaunchGame);

			Trace.Unindent();
		}

		#region Launch Commands

		#region Custom Command

		/// <summary>
		/// Launches the game with a custom command.
		/// </summary>
		private void LaunchOblivionRemasteredCustom()
		{
			Trace.TraceInformation("Launching Oblivion Remastered (Custom)...");
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

		#region ORSE

		/// <summary>
		/// Launches the game, with SKSE.
		/// </summary>
		private void LaunchOblivionRemasteredORSE()
		{
			Trace.TraceInformation("Launching Oblivion Remastered (ORSE)...");
			Trace.Indent();

			string strCommand = GetOrseLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);

			if (!File.Exists(strCommand))
			{
				Trace.TraceError("ORSE does not appear to be installed.");
				Trace.Unindent();
				OnGameLaunched(false, "ORSE does not appear to be installed.");
				return;
			}
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the ORSE launch command.
		/// </summary>
		/// <returns>The ORSE launch command.</returns>
		private string GetOrseLaunchCommand()
		{
			return Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "ORSE_loader.exe");
		}

		#endregion

		#region Vanilla Launch

		/// <summary>
		/// Launches the game, without OBSE.
		/// </summary>
		private void LaunchOblivionRemasteredPlain()
		{
			Trace.TraceInformation("Launching Oblivion Remastered (Plain)...");
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
			string strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "OblivionRemastered.exe");
			return strCommand;
		}

		#endregion

		/// <summary>
		/// Launches the game, using SKSE if present.
		/// </summary>
		private void LaunchGame()
		{
			if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchOblivionRemasteredCustom();
			else if (File.Exists(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "orse_loader.exe")))
				LaunchOblivionRemasteredORSE();
			else
				LaunchOblivionRemasteredPlain();
		}

		#endregion
	}
}
