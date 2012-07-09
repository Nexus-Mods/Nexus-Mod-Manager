using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Nexus.Client.Commands;

namespace Nexus.Client.Games
{
	/// <summary>
	/// A base implementation of a game launcher.
	/// </summary>
	/// <remarks>
	/// This implements some common functionality for game launchers.
	/// </remarks>
	public abstract class GameLauncherBase : IGameLauncher
	{
		private List<Command> m_lstLaunchCommands = new List<Command>();
		private Command m_cmdDefault = null;

		#region IGameLauncher Members

		/// <summary>
		/// Raised when an attempt to launch the game is about to be made.
		/// </summary>
		public event CancelEventHandler GameLaunching = delegate { };

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
			protected set
			{
				m_cmdDefault = value;
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
		public GameLauncherBase(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			SetupCommands();
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="GameLaunching"/> event.
		/// </summary>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		/// <seealso cref="OnGameLaunching()"/>
		protected virtual void OnGameLaunching(CancelEventArgs e)
		{
			GameLaunching(this, e);
		}

		/// <summary>
		/// Raises the <see cref="GameLaunching"/> event.
		/// </summary>
		/// <returns><c>true</c> if the game launch should be cancelled;
		/// <c>false</c> otherwise.</returns>
		/// <seealso cref="OnGameLaunching(CancelEventArgs)"/>
		protected bool OnGameLaunching()
		{
			CancelEventArgs e = new CancelEventArgs(false);
			OnGameLaunching(e);
			return e.Cancel;
		}

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

		#endregion

		/// <summary>
		/// Initializes the game launch commands.
		/// </summary>
		protected abstract void SetupCommands();

		/// <summary>
		/// Clears all the launch commands.
		/// </summary>
		protected void ClearLaunchCommands()
		{
			m_lstLaunchCommands.Clear();
		}

		/// <summary>
		/// Adds the given launch command.
		/// </summary>
		/// <param name="p_cmdLaunch">The launch command to add.</param>
		protected void AddLaunchCommand(Command p_cmdLaunch)
		{
			m_lstLaunchCommands.Add(p_cmdLaunch);
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
		protected void Launch(string p_strCommand, string p_strCommandArgs)
		{
			if (OnGameLaunching())
			{
				Trace.TraceInformation("Cancelled");
				Trace.Unindent();
				return;
			}
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

		#endregion
	}
}
