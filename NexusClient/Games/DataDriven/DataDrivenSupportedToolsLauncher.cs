using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Commands;
using Nexus.Client.Settings;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenSupportedToolsLauncher : SupportedToolsLauncherBase
    {
        private readonly GameModeDefinition _definition;

        public DataDrivenSupportedToolsLauncher(IGameMode gameMode, IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(gameMode, environmentInfo)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            SetupCommands();
        }

        public override void SetupCommands()
        {
            ClearLaunchCommands();
            DefaultLaunchCommand = null;
            EnsureToolSettings();

            // SupportedToolsLauncherBase calls this virtual method from its
            // constructor, before this derived constructor can assign
            // _definition. The explicit SetupCommands() call in this
            // constructor rebuilds the commands after assignment.
            GameModeDefinition definition = _definition;
            if (definition == null || definition.SupportedTools == null)
                return;

            foreach (GameModeToolDefinition tool in definition.SupportedTools)
            {
                if (tool == null || string.IsNullOrWhiteSpace(tool.Id))
                    continue;

                string command = GetToolLaunchCommand(tool);
                if (!string.IsNullOrWhiteSpace(command) && File.Exists(command))
                {
                    Image icon = SafeExtractIcon(command);
                    AddLaunchCommand(new Command(tool.Id, "Launch " + tool.Name, "Launches " + tool.Name + ".", icon, () => LaunchTool(tool), true));
                    if (DefaultLaunchCommand == null)
                        DefaultLaunchCommand = new Command("Launch " + tool.Name, "Launches " + tool.Name + ".", () => LaunchTool(tool));
                }
                else
                {
                    AddLaunchCommand(new Command("Config#" + tool.Id, "Config " + tool.Name, "Configures " + tool.Name + ".", null, () => ConfigTool(tool), true));
                }
            }
        }

        public override void ConfigCommand(string p_strCommandID)
        {
            GameModeDefinition definition = _definition;
            if (string.IsNullOrWhiteSpace(p_strCommandID) ||
                definition == null ||
                definition.SupportedTools == null)
            {
                return;
            }

            string toolId = p_strCommandID.StartsWith("Config#", StringComparison.OrdinalIgnoreCase)
                ? p_strCommandID.Substring(7)
                : p_strCommandID;
            GameModeToolDefinition tool = definition.SupportedTools.FirstOrDefault(x => x != null && string.Equals(x.Id, toolId, StringComparison.OrdinalIgnoreCase));
            if (tool != null)
                ConfigTool(tool);
        }

        private void LaunchTool(GameModeToolDefinition tool)
        {
            Trace.TraceInformation("Launching {0}", tool.Name);
            Trace.Indent();

            string command = GetToolLaunchCommand(tool);
            if (string.IsNullOrWhiteSpace(command))
            {
                Trace.TraceError("Failed: no safe executable path resolved.");
                Trace.Unindent();
                OnSupportedToolsLaunched(false, "Could not find a safe executable for '" + tool.Name + "'.");
                return;
            }

            Launch(command, BuildArgumentString(tool.ArgumentTokens));
        }

        private string GetToolLaunchCommand(GameModeToolDefinition tool)
        {
            EnsureToolSettings();

            string configuredDirectory;
            string settingsKey = GetToolSettingsKey(tool);
            KeyedSettings<string> settings = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId];
            if (settings != null && settings.TryGetValue(settingsKey, out configuredDirectory))
            {
                string configuredCommand = FindExecutable(configuredDirectory, tool);
                if (!string.IsNullOrWhiteSpace(configuredCommand))
                    return configuredCommand;
            }

            foreach (GameModeToolDiscoveryRuleDefinition rule in tool.DiscoveryRules ?? Enumerable.Empty<GameModeToolDiscoveryRuleDefinition>())
            {
                string command = FindExecutable(ResolveDiscoveryPath(rule), tool);
                if (!string.IsNullOrWhiteSpace(command))
                    return command;
            }

            return FindExecutable(ResolveCandidateDirectory(tool.ExecutableDirectory), tool);
        }

        private string ResolveDiscoveryPath(GameModeToolDiscoveryRuleDefinition rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.Source))
                return null;

            if (string.Equals(rule.Source, "Registry", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(rule.RegistryKey) || !RegistryUtil.CanReadKey(rule.RegistryKey))
                    return null;

                string path = Convert.ToString(Registry.GetValue(rule.RegistryKey, rule.RegistryValueName ?? string.Empty, null));
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                if (!Path.IsPathRooted(path))
                {
                    Trace.TraceWarning("Registry discovery returned a non-rooted path for '{0}': {1}", rule.RegistryKey, path);
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(rule.PathSuffix))
                    path = Path.Combine(path, rule.PathSuffix);
                return Path.GetFullPath(path);
            }

            if (string.Equals(rule.Source, "GameRelative", StringComparison.Ordinal))
            {
                return DataDrivenPathResolver.ResolvePath(rule.Path, CreatePathContext(), GetGameRootPath());
            }

            if (string.Equals(rule.Source, "Path", StringComparison.Ordinal))
            {
                return ResolveCandidateDirectory(rule.Path);
            }

            Trace.TraceWarning("Unsupported data-driven tool discovery source '{0}' for tool definition '{1}'.", rule.Source, _definition.ModeId);
            return null;
        }

        private string ResolveCandidateDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            return DataDrivenPathResolver.ResolvePath(value, CreatePathContext(), GetGameRootPath());
        }

        private string FindExecutable(string folderPath, GameModeToolDefinition tool)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return null;

            string expanded = Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"'));
            if (!Path.IsPathRooted(expanded) || !Directory.Exists(expanded))
                return null;

            foreach (string executableName in GetExecutableNames(tool))
            {
                string command = Path.Combine(expanded, executableName);
                if (File.Exists(command))
                    return Path.GetFullPath(command);
            }

            return null;
        }

        private IEnumerable<string> GetExecutableNames(GameModeToolDefinition tool)
        {
            if (tool.ExecutableNames != null)
            {
                foreach (string executableName in tool.ExecutableNames.Where(DataDrivenDefinitionRules.IsSafeToolExecutableName))
                    yield return executableName;
            }
            else if (DataDrivenDefinitionRules.IsSafeToolExecutableName(tool.ExecutableName))
            {
                yield return tool.ExecutableName;
            }
        }

        private string GetToolSettingsKey(GameModeToolDefinition tool)
        {
            return string.IsNullOrWhiteSpace(tool.SettingsKey) ? tool.Id : tool.SettingsKey;
        }

        private void ConfigTool(GameModeToolDefinition tool)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = string.Format("Select the folder where the {0} executable is located.", tool.Name);
                dialog.ShowNewFolderButton = false;

                string current;
                KeyedSettings<string> settings = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId];
                if (settings != null && settings.TryGetValue(GetToolSettingsKey(tool), out current) && Directory.Exists(current))
                    dialog.SelectedPath = current;

                if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    return;
                if (string.IsNullOrWhiteSpace(FindExecutable(dialog.SelectedPath, tool)))
                {
                    MessageBox.Show("The selected folder does not contain a supported executable for " + tool.Name + ".", "Tool not found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                EnsureToolSettings();
                EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][GetToolSettingsKey(tool)] = dialog.SelectedPath;
                EnvironmentInfo.Settings.Save();
                OnChangedToolPath(EventArgs.Empty);
            }
        }

        private string BuildArgumentString(IEnumerable<string> argumentTokens)
        {
            if (argumentTokens == null)
                return string.Empty;

            var arguments = new List<string>();
            foreach (string token in argumentTokens.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                string resolved = DataDrivenPathResolver.Expand(token, CreatePathContext());
                arguments.Add(QuoteArgument(resolved));
            }

            return string.Join(" ", arguments.ToArray());
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return "\"\"";
            if (argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
                return argument;

            var builder = new StringBuilder();
            builder.Append('"');
            int backslashes = 0;
            foreach (char current in argument)
            {
                if (current == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (current == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append(current);
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(current);
                }
                backslashes = 0;
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        private DataDrivenPathContext CreatePathContext()
        {
            string userGameDataPath = null;
            DataDrivenGamebryoGameMode gamebryoMode = GameMode as DataDrivenGamebryoGameMode;
            if (gamebryoMode != null)
                userGameDataPath = gamebryoMode.UserGameDataPath;

            string gameRootPath = GetGameRootPath();
            return new DataDrivenPathContext(
                EnvironmentInfo,
                gameRootPath,
                GameMode.GameModeEnvironmentInfo.ExecutablePath ?? gameRootPath,
                GameMode.ModeId,
                userGameDataPath);
        }

        private string GetGameRootPath()
        {
            return GameMode.GameModeEnvironmentInfo.ExecutablePath ??
                   GameMode.GameModeEnvironmentInfo.InstallationPath;
        }

        private void EnsureToolSettings()
        {
            if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameMode.ModeId) || EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] == null)
                EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] = new KeyedSettings<string>();
        }
    }
}
