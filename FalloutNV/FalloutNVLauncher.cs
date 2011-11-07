using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// Launches Fallout: New Vegas.
	/// </summary>
	public class FalloutNVLauncher : IGameLauncher
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
		public FalloutNVLauncher(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
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
			Icon icnIcon = Icon.ExtractAssociatedIcon(GetPlainLaunchCommand());
			m_lstLaunchCommands.Add(new Command("PlainLaunch", "Launch Fallout: New Vegas", "Launches plain Fallout: New Vegas.", icnIcon.ToBitmap(), LaunchFalloutNVPlain, true));
			string strCommand = GetNvseLaunchCommand();
			if (File.Exists(strCommand))
			{
				icnIcon = Icon.ExtractAssociatedIcon(strCommand);
				m_lstLaunchCommands.Add(new Command("NvseLaunch", "Launch NVSE", "Launches Fallout: New Vegas with NVSE.", icnIcon.ToBitmap(), LaunchFalloutNVNVSE, true));
			}
			strCommand = GetCustomLaunchCommand();
			icnIcon = String.IsNullOrEmpty(strCommand) ? null : Icon.ExtractAssociatedIcon(GetCustomLaunchCommand());
			m_lstLaunchCommands.Add(new Command("CustomLaunch", "Launch Custom Fallout: New Vegas", "Launches Fallout: New Vegas with custom command.", (icnIcon == null) ? null : icnIcon.ToBitmap(), LaunchFalloutNVCustom, true));

			m_cmdDefault = new Command("Launch Fallout: New Vegas", "Launches Fallout: New Vegas.", LaunchGame);
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
		private void LaunchFalloutNVCustom()
		{
			Trace.TraceInformation("Launching Fallout: New Vegas (Custom)...");
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
				if (!Path.IsPathRooted(strCommand))
					strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, strCommand);
			}
			return strCommand;
		}

		#endregion

		#region FOSE

		/// <summary>
		/// Launches the game, with NVSE.
		/// </summary>
		private void LaunchFalloutNVNVSE()
		{
			Trace.TraceInformation("Launching Fallout: New Vegas (NVSE)...");
			Trace.Indent();

			string strCommand = GetNvseLaunchCommand();
			Trace.TraceInformation("Command: " + strCommand);

			if (!File.Exists(strCommand))
			{
				Trace.TraceError("NVSE does not appear to be installed.");
				Trace.Unindent();
				OnGameLaunched(false, "NVSE does not appear to be installed.");
				return;
			}
			Launch(strCommand, null);
		}

		/// <summary>
		/// Gets the NVSE launch command.
		/// </summary>
		/// <returns>The NVSE launch command.</returns>
		private string GetNvseLaunchCommand()
		{
			return Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "nvse_loader.exe");
		}

		#endregion

		#region Vanilla Launch

		/// <summary>
		/// Launches the game, without FOSE.
		/// </summary>
		private void LaunchFalloutNVPlain()
		{
			Trace.TraceInformation("Launching Fallout: New Vegas (Plain)...");
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
			string strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "falloutNV.exe");
			if (!File.Exists(strCommand))
				strCommand = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "falloutNVng.exe");
			return strCommand;
		}

		#endregion

		/// <summary>
		/// Launches the game, using NVSE if present.
		/// </summary>
		private void LaunchGame()
		{
			if (!String.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchFalloutNVCustom();
			else if (File.Exists(Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, "nvse_loader.exe")))
				LaunchFalloutNVNVSE();
			else
				LaunchFalloutNVPlain();
		}

		#endregion
	}
}
