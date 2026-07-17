using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _pendingDefinition = null;
        }

        private static GameModeDefinition PushDefinition(GameModeDefinition definition)
        {
            _pendingDefinition = definition;
            return definition;
        }

        protected override void SetupCommands()
        {
            GameModeDefinition definition = _definition ?? _pendingDefinition;
            if (definition == null)
                throw new InvalidOperationException("No data-driven GameMode definition was provided.");

            ClearLaunchCommands();

            string plainCommand = GetPlainLaunchCommand(definition);
            Image icon = SafeExtractIcon(plainCommand);
            AddLaunchCommand(new Command(
                definition.Launcher == null || string.IsNullOrWhiteSpace(definition.Launcher.PlainCommandName) ? "PlainLaunch" : definition.Launcher.PlainCommandName,
                definition.Launcher == null || string.IsNullOrWhiteSpace(definition.Launcher.PlainCommandText) ? "Launch " + GameMode.Name : definition.Launcher.PlainCommandText,
                "Launches " + GameMode.Name + ".",
                icon,
                LaunchPlain,
                true));

            string scriptExtenderCommand = GetInstalledScriptExtenderCommand(definition);
            if (!string.IsNullOrWhiteSpace(scriptExtenderCommand))
            {
                string scriptExtenderName = GetScriptExtenderDisplayName(definition, scriptExtenderCommand);
                AddLaunchCommand(new Command(
                    "ScriptExtenderLaunch",
                    "Launch " + scriptExtenderName,
                    "Launches " + GameMode.Name + " with " + scriptExtenderName + ".",
                    SafeExtractIcon(scriptExtenderCommand),
                    LaunchScriptExtender,
                    true));
            }

            if (AllowsCustomCommand(definition))
            {
                string customCommand = GetCustomLaunchCommand();
                AddLaunchCommand(new Command(
                    string.IsNullOrWhiteSpace(definition.Launcher.CustomCommandName) ? "CustomLaunch" : definition.Launcher.CustomCommandName,
                    string.IsNullOrWhiteSpace(definition.Launcher.CustomCommandText) ? "Launch Custom " + GameMode.Name : definition.Launcher.CustomCommandText,
                    "Launches " + GameMode.Name + " with a custom command.",
                    SafeExtractIcon(customCommand),
                    LaunchCustom,
                    true));
            }

            string defaultText = definition.Launcher == null || string.IsNullOrWhiteSpace(definition.Launcher.DefaultCommandText)
                ? "Launch " + GameMode.Name
                : definition.Launcher.DefaultCommandText;
            DefaultLaunchCommand = new Command(defaultText, "Launches " + GameMode.Name + ".", LaunchDefault);
        }

        private void LaunchDefault()
        {
            if (AllowsCustomCommand(_definition) && !string.IsNullOrWhiteSpace(GetConfiguredCustomCommand()))
            {
                LaunchCustom();
                return;
            }

            if (!string.IsNullOrWhiteSpace(GetInstalledScriptExtenderCommand(_definition)))
            {
                LaunchScriptExtender();
                return;
            }

            LaunchPlain();
        }

        private void LaunchPlain()
        {
            PrepareProfileForLaunch();
            Trace.TraceInformation("Launching {0} (Default)...", GameMode.Name);
            Trace.Indent();
            Launch(GetPlainLaunchCommand(_definition), null);
        }

        private void LaunchScriptExtender()
        {
            PrepareProfileForLaunch();
            string command = GetInstalledScriptExtenderCommand(_definition);
            if (string.IsNullOrWhiteSpace(command))
            {
                Trace.TraceError("The configured script extender does not appear to be installed.");
                OnGameLaunched(false, "The configured script extender does not appear to be installed.");
                return;
            }

            Trace.TraceInformation(
                "Launching {0} ({1})...",
                GameMode.Name,
                GetScriptExtenderDisplayName(_definition, command));
            Trace.Indent();
            Launch(command, null);
        }

        private void LaunchCustom()
        {
            PrepareProfileForLaunch();
            Trace.TraceInformation("Launching {0} (Custom)...", GameMode.Name);
            Trace.Indent();

            if (!AllowsCustomCommand(_definition))
            {
                Trace.TraceError("Custom launching is disabled by the Game Mode definition.");
                Trace.Unindent();
                OnGameLaunched(false, "Custom launching is disabled for this Game Mode.");
                return;
            }

            string command = GetCustomLaunchCommand();
            if (string.IsNullOrEmpty(command))
            {
                Trace.TraceError("No custom launch command has been set.");
                Trace.Unindent();
                OnGameLaunched(false, "No custom launch command has been set.");
                return;
            }

            string arguments = string.Empty;
            EnvironmentInfo.Settings.CustomLaunchCommandArguments.TryGetValue(GameMode.ModeId, out arguments);
            Launch(command, arguments);
        }

        private string GetPlainLaunchCommand(GameModeDefinition definition)
        {
            if (definition == null || definition.GameExecutables == null || definition.GameExecutables.Length == 0)
                throw new InvalidOperationException("The data-driven GameMode has no game executable.");

            string preferredExecutable = definition.Launcher == null || string.IsNullOrWhiteSpace(definition.Launcher.DefaultExecutable)
                ? definition.GameExecutables[0]
                : definition.Launcher.DefaultExecutable;

            string preferredCommand = CombineWithExecutablePath(preferredExecutable);
            if (File.Exists(preferredCommand))
                return preferredCommand;

            foreach (string candidate in definition.GameExecutables)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string candidateCommand = CombineWithExecutablePath(candidate);
                if (File.Exists(candidateCommand))
                    return candidateCommand;
            }

            return preferredCommand;
        }

        private string GetInstalledScriptExtenderCommand(GameModeDefinition definition)
        {
            if (definition == null || definition.Gamebryo == null)
                return null;

            if (HasInstalledScriptExtenderAutoLoadFile(definition))
                return GetPlainLaunchCommand(definition);

            foreach (string relativeExecutable in
                     definition.Gamebryo.ScriptExtenderExecutables ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(relativeExecutable))
                    continue;

                string command = CombineWithExecutablePath(relativeExecutable);
                if (File.Exists(command))
                    return command;
            }

            return null;
        }

        private bool HasInstalledScriptExtenderAutoLoadFile(GameModeDefinition definition)
        {
            if (definition == null || definition.Gamebryo == null)
                return false;

            foreach (string relativeFile in
                     definition.Gamebryo.ScriptExtenderAutoLoadFiles ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(relativeFile))
                    continue;

                if (File.Exists(CombineWithExecutablePath(relativeFile)))
                    return true;
            }

            return false;
        }

        private string CombineWithExecutablePath(string relativePath)
        {
            string executableRoot = GameMode.GameModeEnvironmentInfo.ExecutablePath ??
                                    GameMode.GameModeEnvironmentInfo.InstallationPath ??
                                    string.Empty;
            return Path.Combine(executableRoot, relativePath ?? string.Empty);
        }

        private string GetScriptExtenderDisplayName(
            GameModeDefinition definition,
            string command)
        {
            string displaySource = command;
            if (HasInstalledScriptExtenderAutoLoadFile(definition))
            {
                displaySource = (definition.Gamebryo.ScriptExtenderExecutables ??
                                 new string[0])
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
                                command;
            }

            string name = Path.GetFileNameWithoutExtension(displaySource);
            if (string.IsNullOrWhiteSpace(name))
                return "Script Extender";

            int loaderSuffix = name.IndexOf("_loader", StringComparison.OrdinalIgnoreCase);
            if (loaderSuffix > 0)
                name = name.Substring(0, loaderSuffix);

            switch (name.ToLowerInvariant())
            {
                case "nvse":
                    return "NVSE";
                case "fose":
                    return "FOSE";
                case "f4se":
                    return "F4SE";
                case "skse":
                case "skse64":
                    return "SKSE";
                case "obse":
                    return "OBSE";
                case "obse64":
                    return "OBSE64";
                case "mwse":
                    return "MWSE";
                default:
                    return name;
            }
        }

        private string GetConfiguredCustomCommand()
        {
            string command;
            return EnvironmentInfo.Settings.CustomLaunchCommands.TryGetValue(GameMode.ModeId, out command) ? command : null;
        }

        private string GetCustomLaunchCommand()
        {
            string command = GetConfiguredCustomCommand();
            if (string.IsNullOrEmpty(command))
                return command;

            command = Environment.ExpandEnvironmentVariables(command);
            command = FileUtil.StripInvalidPathChars(command);
            if (!Path.IsPathRooted(command))
                command = Path.Combine(GameMode.GameModeEnvironmentInfo.ExecutablePath ?? GameMode.GameModeEnvironmentInfo.InstallationPath ?? string.Empty, command);
            return command;
        }

        private void PrepareProfileForLaunch()
        {
            if (_definition == null ||
                _definition.ModInstall == null ||
                !string.Equals(
                    _definition.ModInstall.PathAdjustmentProfile,
                    "nomanssky",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string gameRoot = GameMode.GameModeEnvironmentInfo.ExecutablePath ??
                              GameMode.GameModeEnvironmentInfo.InstallationPath;
            if (string.IsNullOrWhiteSpace(gameRoot))
                return;

            string modsPath = Path.Combine(gameRoot, "GAMEDATA", "MODS");
            if (!Directory.Exists(modsPath) ||
                !Directory.EnumerateFileSystemEntries(modsPath).Any())
            {
                return;
            }

            string settingsPath = Path.Combine(
                gameRoot,
                "Binaries",
                "SETTINGS",
                "GCMODSETTINGS.MXML");
            if (!File.Exists(settingsPath))
                return;

            try
            {
                string text = File.ReadAllText(settingsPath);
                string updated = Regex.Replace(
                    text,
                    @"(<Property\s+name=""DisableAllMods""\s+value="")true(""\s*/>)",
                    "$1false$2",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

                if (!string.Equals(text, updated, StringComparison.Ordinal))
                    File.WriteAllText(settingsPath, updated);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Could not re-enable No Man's Sky mods before launch: " + ex.Message);
            }
        }

        private static bool AllowsCustomCommand(GameModeDefinition definition)
        {
            return definition != null && definition.Launcher != null && definition.Launcher.AllowCustomCommand == true;
        }
    }
}
