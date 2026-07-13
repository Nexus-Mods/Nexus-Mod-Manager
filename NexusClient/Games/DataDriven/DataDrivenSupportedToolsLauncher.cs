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
        private static readonly HashSet<string> BlockedToolExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe"
        };

        private readonly GameModeDefinition _definition;

        public DataDrivenSupportedToolsLauncher(IGameMode gameMode, IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(gameMode, environmentInfo)
        {
            _definition = definition;
            SetupCommands();
        }

        public override void SetupCommands()
        {
            ClearLaunchCommands();
            EnsureToolSettings();

            if (_definition?.SupportedTools == null)
                return;

            foreach (GameModeToolDefinition tool in _definition.SupportedTools)
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
            if (string.IsNullOrWhiteSpace(p_strCommandID) || _definition?.SupportedTools == null)
                return;

            string toolId = p_strCommandID.StartsWith("Config#", StringComparison.OrdinalIgnoreCase) ? p_strCommandID.Substring(7) : p_strCommandID;
            foreach (GameModeToolDefinition tool in _definition.SupportedTools)
            {
                if (string.Equals(tool.Id, toolId, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigTool(tool);
                    return;
                }
            }
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

            string settingsKey = GetToolSettingsKey(tool);
            string folder = null;
            if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey(settingsKey))
                folder = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][settingsKey];

            string command = FindExecutable(folder, tool);
            if (!string.IsNullOrWhiteSpace(command))
                return command;

            foreach (GameModeToolDiscoveryRuleDefinition rule in tool.DiscoveryRules ?? Enumerable.Empty<GameModeToolDiscoveryRuleDefinition>())
            {
                command = FindExecutable(ResolveDiscoveryPath(rule), tool);
                if (!string.IsNullOrWhiteSpace(command))
                    return command;
            }

            return FindExecutable(ResolveSafePath(tool.ExecutablePath), tool);
        }

        private string ResolveDiscoveryPath(GameModeToolDiscoveryRuleDefinition rule)
        {
            if (rule == null)
                return null;

            string path = null;
            if (string.Equals(rule.Source, "Registry", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(rule.RegistryKey) && RegistryUtil.CanReadKey(rule.RegistryKey))
                    path = Convert.ToString(Registry.GetValue(rule.RegistryKey, rule.RegistryValueName ?? string.Empty, null));
            }
            else if (string.Equals(rule.Source, "GameRelative", StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = ResolveSafePath(rule.Path);
                if (relativePath != null)
                    path = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath ?? string.Empty, relativePath);
            }
            else
            {
                path = ResolveSafePath(rule.Path);
            }

            string pathSuffix = ResolveSafePath(rule.PathSuffix);
            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(pathSuffix))
                path = Path.Combine(path, pathSuffix);

            return path;
        }

        private string FindExecutable(string folderPath, GameModeToolDefinition tool)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return null;

            string expanded = ResolveSafePath(folderPath);
            if (string.IsNullOrWhiteSpace(expanded) || !Directory.Exists(expanded))
                return null;

            foreach (string executableName in GetExecutableNames(tool))
            {
                string command = Path.Combine(expanded, executableName);
                if (File.Exists(command))
                    return command;
            }

            return null;
        }

        private IEnumerable<string> GetExecutableNames(GameModeToolDefinition tool)
        {
            if (tool.ExecutableNames != null)
            {
                foreach (string executableName in tool.ExecutableNames.Where(IsSafeToolExecutableName))
                    yield return executableName;
            }

            if (IsSafeToolExecutableName(tool.ExecutableName))
                yield return tool.ExecutableName;
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
                if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    return;

                if (string.IsNullOrWhiteSpace(FindExecutable(dialog.SelectedPath, tool)))
                    return;

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
                string resolved = ResolveSafePath(token);
                if (resolved != null)
                    arguments.Add(QuoteArgument(resolved));
            }

            return string.Join(" ", arguments.ToArray());
        }

        private string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return "\"\"";
            if (argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '\"' }) < 0)
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

        private string ResolveSafePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            if (ContainsParentTraversal(value))
                return null;

            return value.Replace("{GamePath}", GameMode.GameModeEnvironmentInfo.InstallationPath ?? string.Empty)
                        .Replace("{ExecutablePath}", GameMode.GameModeEnvironmentInfo.ExecutablePath ?? string.Empty)
                        .Replace("{ModeId}", GameMode.ModeId ?? string.Empty)
                        .Replace("{LocalApplicationData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                        .Replace("{PersonalData}", Environment.GetFolderPath(Environment.SpecialFolder.Personal))
                        .Replace("{ProgramFiles}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                        .Replace("{ProgramFilesX86}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }

        private bool IsSafeToolExecutableName(string executableName)
        {
            return !string.IsNullOrWhiteSpace(executableName) &&
                   string.Equals(executableName, Path.GetFileName(executableName), StringComparison.Ordinal) &&
                   executableName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   !Path.IsPathRooted(executableName) &&
                   string.Equals(Path.GetExtension(executableName), ".exe", StringComparison.OrdinalIgnoreCase) &&
                   !BlockedToolExecutables.Contains(executableName);
        }

        private bool ContainsParentTraversal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] segments = value.Replace('/', '\\').Split('\\');
            return segments.Any(x => string.Equals(x.Trim(), "..", StringComparison.Ordinal));
        }

        private void EnsureToolSettings()
        {
            if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] == null)
                EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] = new KeyedSettings<string>();
        }
    }
}
