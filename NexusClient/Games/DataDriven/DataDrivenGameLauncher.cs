using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Nexus.Client.Commands;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
	public class DataDrivenGameLauncher : GameLauncherBase
	{
		[ThreadStatic]
		private static GameModeDefinition _pendingDefinition;

		private readonly GameModeDefinition _definition;

		public DataDrivenGameLauncher(IGameMode gameMode, IEnvironmentInfo environmentInfo, GameModeDefinition definition)
			: this(gameMode, environmentInfo, PushDefinition(definition), true)
		{
		}

		private DataDrivenGameLauncher(IGameMode gameMode, IEnvironmentInfo environmentInfo, GameModeDefinition definition, bool definitionIsPending)
			: base(gameMode, environmentInfo)
		{
			_definition = definition;
			_pendingDefinition = null;
		}

		private static GameModeDefinition PushDefinition(GameModeDefinition definition)
		{
			_pendingDefinition = definition;
			return definition;
		}

		protected override void SetupCommands()
		{
			var definition = _definition ?? _pendingDefinition;
			if (definition == null)
				throw new InvalidOperationException("No data-driven GameMode definition was provided.");

			ClearLaunchCommands();
			string plainCommand = GetPlainLaunchCommand(definition);
			Image icon = SafeExtractIcon(plainCommand);
			AddLaunchCommand(new Command(definition.Launcher?.PlainCommandName ?? "PlainLaunch", definition.Launcher?.PlainCommandText ?? "Launch " + GameMode.Name, "Launches " + GameMode.Name + ".", icon, LaunchPlain, true));

			if (definition.Launcher == null || definition.Launcher.AllowCustomCommand)
			{
				string customCommand = GetCustomLaunchCommand();
				AddLaunchCommand(new Command(definition.Launcher?.CustomCommandName ?? "CustomLaunch", definition.Launcher?.CustomCommandText ?? "Launch Custom " + GameMode.Name, "Launches " + GameMode.Name + " with a custom command.", SafeExtractIcon(customCommand), LaunchCustom, true));
			}

			DefaultLaunchCommand = new Command(definition.Launcher?.DefaultCommandText ?? "Launch " + GameMode.Name, "Launches " + GameMode.Name + ".", LaunchDefault);
		}

		private void LaunchDefault()
		{
			if ((_definition.Launcher == null || _definition.Launcher.AllowCustomCommand) && !string.IsNullOrEmpty(EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId]))
				LaunchCustom();
			else
				LaunchPlain();
		}

		private void LaunchPlain()
		{
			Trace.TraceInformation("Launching {0} (Default)...", GameMode.Name);
			Trace.Indent();
			Launch(GetPlainLaunchCommand(_definition), null);
		}

		private void LaunchCustom()
		{
			Trace.TraceInformation("Launching {0} (Custom)...", GameMode.Name);
			Trace.Indent();
			string command = GetCustomLaunchCommand();
			if (string.IsNullOrEmpty(command))
			{
				Trace.TraceError("No custom launch command has been set.");
				Trace.Unindent();
				OnGameLaunched(false, "No custom launch command has been set.");
				return;
			}
			Launch(command, EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId]);
		}

		private string GetPlainLaunchCommand(GameModeDefinition definition)
		{
			return Path.Combine(GameMode.ExecutablePath ?? string.Empty, definition.Launcher?.DefaultExecutable ?? definition.GameExecutables[0]);
		}

		private string GetCustomLaunchCommand()
		{
			string command = EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId];
			if (!string.IsNullOrEmpty(command))
			{
				command = Environment.ExpandEnvironmentVariables(command);
				command = FileUtil.StripInvalidPathChars(command);
				if (!Path.IsPathRooted(command))
					command = Path.Combine(GameMode.GameModeEnvironmentInfo.ExecutablePath, command);
			}
			return command;
		}
	}
}
