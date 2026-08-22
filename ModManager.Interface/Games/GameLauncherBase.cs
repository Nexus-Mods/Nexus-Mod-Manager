using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util.Localization;

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
		/// Safely extracts the icon from an executable, returning a fallback icon if extraction fails.
		/// </summary>
		protected static Image SafeExtractIcon(string path)
		{
			try
			{
				var icon = Icon.ExtractAssociatedIcon(path);
				return icon?.ToBitmap() ?? CreateFallbackIcon();
			}
			catch
			{
				return CreateFallbackIcon();
			}
		}

		protected static Image CreateFallbackIcon()
		{
			using (var icon = new Icon(SystemIcons.Application, 16, 16))
			{
				return icon.ToBitmap();
			}
		}

		/// <summary>
		/// Appends the resolved executable path to the description of a launcher/tool command.
		/// This is primarily used by script-extender launch commands so the UI tooltip shows
		/// exactly which executable NMM is going to start.
		/// </summary>
		protected static string AppendExecutablePathToDescription(string description, string executablePath)
		{
			if (String.IsNullOrWhiteSpace(executablePath))
				return description;

			string resolvedPath = executablePath;
			try
			{
				resolvedPath = Path.GetFullPath(executablePath);
			}
			catch (Exception)
			{
				// Keep the original resolved launcher value if Path.GetFullPath cannot normalize it.
			}

			string pathLine = LanguageManager.Format(
				"GameModes.Commands.Tool.ExecutablePath",
				"Executable: {0}",
				resolvedPath);

			return String.IsNullOrWhiteSpace(description)
				? pathLine
				: description + Environment.NewLine + pathLine;
		}

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
		/// Launches the game using the normal Windows shell behavior.
		/// </summary>
		/// <param name="p_strCommand">The command to execute to launch the game.</param>
		/// <param name="p_strCommandArgs">The command arguments to pass to the launch command.</param>
		protected void Launch(string p_strCommand, string p_strCommandArgs)
		{
			LaunchProcess(p_strCommand, p_strCommandArgs, Path.GetDirectoryName(p_strCommand), true);
		}

		/// <summary>
		/// Launches an executable directly, bypassing ShellExecute and explicitly setting
		/// the child working directory. Intended for script extenders/loaders that resolve
		/// native dependencies relative to the game directory.
		/// </summary>
		protected void LaunchDirectExecutable(string p_strCommand, string p_strCommandArgs, string p_strWorkingDirectory)
		{
			LaunchProcess(p_strCommand, p_strCommandArgs, p_strWorkingDirectory, false);
		}

		private void LaunchProcess(string p_strCommand, string p_strCommandArgs, string p_strWorkingDirectory, bool p_booUseShellExecute)
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
				psiGameLaunch.WorkingDirectory = String.IsNullOrEmpty(p_strWorkingDirectory)
					? Path.GetDirectoryName(p_strCommand)
					: p_strWorkingDirectory;
				psiGameLaunch.UseShellExecute = p_booUseShellExecute;

				Trace.TraceInformation("Launch executable: {0}", psiGameLaunch.FileName);
				Trace.TraceInformation("Arguments: {0}", String.IsNullOrEmpty(psiGameLaunch.Arguments) ? "<none>" : psiGameLaunch.Arguments);
				Trace.TraceInformation("Working directory: {0}", psiGameLaunch.WorkingDirectory);
				Trace.TraceInformation("UseShellExecute: {0}", psiGameLaunch.UseShellExecute);
				Trace.TraceInformation("NMM current directory: {0}", Environment.CurrentDirectory);

				if (Process.Start(psiGameLaunch) == null)
				{
					Trace.TraceError("Failed (unknown error)");
					Trace.Unindent();
					OnGameLaunched(false, LanguageManager.Format("GameModes.Launch.Failed", "Failed to launch '{0}'.", Path.GetFileName(p_strCommand)));
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
				OnGameLaunched(false, LanguageManager.Format("GameModes.Launch.FailedWithError", "Failed to launch '{0}'{1}{2}.", Path.GetFileName(p_strCommand), Environment.NewLine, ex.Message));
				return;
			}
			Trace.TraceInformation("Succeeded");
			Trace.Unindent();
			OnGameLaunched(true, null);
		}

		#endregion
	}
}
