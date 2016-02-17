using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Nexus.Client.Commands;

namespace Nexus.Client.Games
{
	/// <summary>
	/// A base implementation of the supported tools launcher.
	/// </summary>
	/// <remarks>
	/// This implements some common functionality for the supported tools launchers.
	/// </remarks>
	public abstract class SupportedToolsLauncherBase : ISupportedToolsLauncher
	{
		private List<Command> m_lstLaunchCommands = new List<Command>();
		private Command m_cmdDefault = null;

		#region ISupportedToolsLauncher Members

		/// <summary>
		/// Raised when an attempt to launch the SupportedTools is about to be made.
		/// </summary>
		public event CancelEventHandler SupportedToolsLaunching = delegate { };

		/// <summary>
		/// Raised when a attempt to launch the SupportedTools has been made.
		/// </summary>
		public event EventHandler<SupportedToolsLaunchEventArgs> SupportedToolsLaunched = delegate { };

		/// <summary>
		/// Raised when an attempt to change the SupportedTools path has been made.
		/// </summary>
		public event EventHandler ChangedToolPath = delegate { };

		/// <summary>
		/// Gets the list of available commands that can launch the SupportedTools.
		/// </summary>
		/// <value>The list of available commands that can launch the SupportedTools.</value>
		public IEnumerable<Command> LaunchCommands
		{
			get
			{
				return m_lstLaunchCommands;
			}
		}

		/// <summary>
		/// Gets the default command to use to launch the SupportedTools.
		/// </summary>
		/// <value>The default command to use to launch the SupportedTools.</value>
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
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }
		
		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		public SupportedToolsLauncherBase(IGameMode p_gmdGameMode, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			GameMode = p_gmdGameMode;
			EnvironmentInfo = p_eifEnvironmentInfo;
			SetupCommands();
		}

		#endregion

		#region Event Raising

		/// <summary>
		/// Raises the <see cref="SupportedToolsLaunching"/> event.
		/// </summary>
		/// <param name="e">A <see cref="CancelEventArgs"/> describing the event arguments.</param>
		/// <seealso cref="OnSupportedToolsLaunching()"/>
		protected virtual void OnSupportedToolsLaunching(CancelEventArgs e)
		{
			SupportedToolsLaunching(this, e);
		}

		/// <summary>
		/// Raises the <see cref="SupportedToolsLaunching"/> event.
		/// </summary>
		/// <returns><c>true</c> if the SupportedTools launch should be cancelled;
		/// <c>false</c> otherwise.</returns>
		/// <seealso cref="OnSupportedToolsLaunching(CancelEventArgs)"/>
		protected bool OnSupportedToolsLaunching()
		{
			CancelEventArgs e = new CancelEventArgs(false);
			OnSupportedToolsLaunching(e);
			return e.Cancel;
		}

		/// <summary>
		/// Raises the <see cref="SupportedToolsLaunched"/> event.
		/// </summary>
		/// <param name="e">A <see cref="GameLaunchEventArgs"/> describing the event arguments.</param>
		/// <seealso cref="OnSupportedToolsLaunched(bool, string)"/>
		protected virtual void OnSupportedToolsLaunched(SupportedToolsLaunchEventArgs e)
		{
			SupportedToolsLaunched(this, e);
		}

		/// <summary>
		/// Raises the <see cref="GameLaunched"/> event.
		/// </summary>
		/// <param name="p_booGameLaunched">Whether or not the game launched successfully.</param>
		/// <param name="p_strMessage">A message to display to the user.</param>
		/// <seealso cref="OnGameLaunched(GameLaunchEventArgs)"/>
		protected void OnSupportedToolsLaunched(bool p_booSupportedToolsLaunched, string p_strMessage)
		{
			OnSupportedToolsLaunched(new SupportedToolsLaunchEventArgs(p_booSupportedToolsLaunched, p_strMessage));
		}

		#endregion

		/// <summary>
		/// Initializes the SupportedTools launch commands.
		/// </summary>
		public abstract void SetupCommands();

		/// <summary>
		/// Clears all the launch commands.
		/// </summary>
		protected void ClearLaunchCommands()
		{
			m_lstLaunchCommands.Clear();
		}

		/// <summary>
		/// Raised when an attempt to change the SupportedTools path has been made.
		/// </summary>
		protected virtual void OnChangedToolPath(EventArgs e)
		{
			ChangedToolPath(this, e);
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
		/// Launches the SupportedTools.
		/// </summary>
		/// <remarks>
		/// This is the root launch method that all the other launch methods call. This method
		/// actually spawns the new process to launch the SupportedTools, using the given information.
		/// </remarks>
		/// <param name="p_strCommand">The command to execute to launch the game.</param>
		/// <param name="p_strCommandArgs">The command argumetns to pass to the launch command.</param>
		protected void Launch(string p_strCommand, string p_strCommandArgs)
		{
			if (OnSupportedToolsLaunching())
			{
				Trace.TraceInformation("Cancelled");
				Trace.Unindent();
				return;
			}
			try
			{
				ProcessStartInfo psiSupportedToolsLaunch = new ProcessStartInfo();
				if (!String.IsNullOrEmpty(p_strCommandArgs))
					psiSupportedToolsLaunch.Arguments = p_strCommandArgs;
				psiSupportedToolsLaunch.FileName = p_strCommand;
				psiSupportedToolsLaunch.WorkingDirectory = Path.GetDirectoryName(p_strCommand);
				if (Process.Start(psiSupportedToolsLaunch) == null)
				{
					Trace.TraceError("Failed (unknown error)");
					Trace.Unindent();
					OnSupportedToolsLaunched(false, String.Format("Failed to launch '{0}'.", Path.GetFileName(p_strCommand)));
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
				OnSupportedToolsLaunched(false, String.Format("Failed to launch '{0}'{1}{2}.", Path.GetFileName(p_strCommand), Environment.NewLine, ex.Message));
				return;
			}
			Trace.TraceInformation("Succeeded");
			Trace.Unindent();
			OnSupportedToolsLaunched(true, null);
		}

		/// <summary>
		/// Launches the default command if any.
		/// </summary>
		public virtual void LaunchDefaultCommand()
		{ }

		#endregion
	}
}
