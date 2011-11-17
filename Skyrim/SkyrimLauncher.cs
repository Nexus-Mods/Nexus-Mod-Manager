using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Skyrim
{
	/// <summary>
	/// Launches Skyrim.
	/// </summary>
	public class SkyrimLauncher : IGameLauncher
	{
		private List<Command> m_lstLaunchCommands = new List<Command>();
		private Command m_cmdDefault = null;

		#region IGameLauncher Members

		/// <summary>
		/// Raised when a attempt to launch the game has been made.
		/// </summary>
		public event EventHandler<GameLaunchEventArgs> GameLaunched = delegate { };

		/// <summary>
		/// Gets the list of available commands that can launch the game.
		/// </summary>
		/// <value>The list of available commands that can launch the game.</value>
		public IEnumerable<Command> LaunchCommands
		{
			get
			{
				return m_lstLaunchCommands;
			}
		}

		/// <summary>
		/// Gets the default command to use to launch the game.
		/// </summary>
		/// <value>The default command to use to launch the game.</value>
		public Command DefaultLaunchCommand
		{
			get
			{
				return m_cmdDefault;
			}
		}

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">>The game mode currently being managed.</param>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public SkyrimLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			SetupCommands();
		}

		#endregion

		/// <summary>
		/// Raises the <see cref="GameLaunched"/> event.
		/// </summary>
		/// <param name="e">A <see cref="GameLaunchEventArgs"/> describing the event arguments.</param>
		/// <seealso cref="OnGameLaunched(bool, string)"/>
		protected virtual void OnGameLaunched(GameLaunchEventArgs e)
		{
			GameLaunched(this, e);
		}

		/// <summary>
		/// Raises the <see cref="GameLaunched"/> event.
		/// </summary>
		/// <param name="p_booGameLaunched">Whether or not the game launched successfully.</param>
		/// <param name="p_strMessage">A message to display to the user.</param>
		/// <seealso cref="OnGameLaunched(GameLaunchEventArgs)"/>
		protected void OnGameLaunched(bool p_booGameLaunched, string p_strMessage)
		{
			OnGameLaunched(new GameLaunchEventArgs(p_booGameLaunched, p_strMessage));
		}

		/// <summary>
		/// Initializes the game launch commands.
		/// </summary>
		protected void SetupCommands()
		{
			m_lstLaunchCommands.Clear();
			string strCommand = GetPlainLaunchCommand();
			Icon icnIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand) : null;
			m_lstLaunchCommands.Add(new Command("PlainLaunch", "Launch Skyrim", "Launches plain Skyrim.", icnIcon.ToBitmap(), LaunchSkyrimPlain, true));
			strCommand = GetSkseLaunchCommand();
			if (File.Exists(strCommand))
			{
				icnIcon = Icon.ExtractAssociatedIcon(strCommand);
				m_lstLaunchCommands.Add(new Command("SkseLaunch", "Launch SKSE", "Launches Skyrim with SKSE.", icnIcon.ToBitmap(), LaunchSkyrimSKSE, true));
			}
			strCommand = GetCustomLaunchCommand();
			icnIcon = File.Exists(strCommand) ? Icon.ExtractAssociatedIcon(strCommand) : null;
			m_lstLaunchCommands.Add(new Command("CustomLaunch", "Launch Custom Skyrim", "Launches Skyrim with custom command.", (icnIcon == null) ? null : icnIcon.ToBitmap(), LaunchSkyrimCustom, true));
			
			m_cmdDefault = new Command("Launch Skyrim", "Launches Skyrim.", LaunchGame);
		}

		#region Launch Commands

		/// <summary>
		/// Launches the game.
		/// </summary>
		/// <remarks>
		/// This is the root launch method that all the other launch methods call. This method
		/// actually spawns the new process to launch the game, using the given information.
		/// </remarks>
		/// <param name="p_strCommand">The command to execute to launch the game.</param>
		/// <param name="p_strCommandArgs">The command argumetns to pass to the launch command.</param>
		private void Launch(string p_strCommand, string p_strCommandArgs)
		{
			try
			{
				ProcessStartInfo psiGameLaunch = new ProcessStartInfo();
				if (!String.IsNullOrEmpty(p_strCommandArgs))
					psiGameLaunch.Arguments = p_strCommandArgs;
				psiGameLaunch.FileName = p_strCommand;
				psiGameLaunch.WorkingDirectory = Path.GetDirectoryName(p_strCommand);
				if (Process.Start(psiGameLaunch) == null)
				{
					Trace.TraceError("Failed (unknown error)");
					Trace.Unindent();
					OnGameLaunched(false, String.Format("Failed to launch '{0}'.", Path.GetFileName(p_strCommand)));
					return;
				}
			}
			catch (Exception ex)
			{
				Trace.TraceError("Failed:");
				Trace.Indent();
				Trace.TraceError(ex.ToString());
				Trace.Unindent();
				Trace.Unindent();
				OnGameLaunched(false, String.Format("Failed to launch '{0}'{1}{2}.", Path.GetFileName(p_strCommand), Environment.NewLine, ex.Message));
				return;
			}
			Trace.TraceInformation("Succeeded");
			Trace.Unindent();
			OnGameLaunched(true, null);
		}

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
				OnGameLaunched(false, "SKSE does not appear to be installed.");
				return;
			}
			Launch(strCommand, null);
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
