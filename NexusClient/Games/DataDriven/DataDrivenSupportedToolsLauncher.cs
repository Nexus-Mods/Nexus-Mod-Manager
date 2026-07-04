using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
            Launch(GetToolLaunchCommand(tool), ResolvePath(tool.Arguments));
        }

        private string GetToolLaunchCommand(GameModeToolDefinition tool)
        {
            EnsureToolSettings();

            string folder = null;
            if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey(tool.Id))
                folder = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][tool.Id];

            string command = FindExecutable(folder, tool);
            if (!string.IsNullOrWhiteSpace(command))
                return command;

            foreach (GameModeToolDiscoveryRuleDefinition rule in tool.DiscoveryRules ?? Enumerable.Empty<GameModeToolDiscoveryRuleDefinition>())
            {
                command = FindExecutable(ResolveDiscoveryPath(rule), tool);
                if (!string.IsNullOrWhiteSpace(command))
                    return command;
            }

            return FindExecutable(ResolvePath(tool.ExecutablePath), tool);
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
                path = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath ?? string.Empty, rule.Path ?? string.Empty);
            }
            else
            {
                path = ResolvePath(rule.Path);
            }

            if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(rule.PathSuffix))
                path = Path.Combine(path, ResolvePath(rule.PathSuffix));

            return path;
        }

        private string FindExecutable(string path, GameModeToolDefinition tool)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string expanded = ResolvePath(path);
            if (File.Exists(expanded))
                return expanded;

            if (!Directory.Exists(expanded))
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
                foreach (string executableName in tool.ExecutableNames.Where(x => !string.IsNullOrWhiteSpace(x)))
                    yield return executableName;
            }

            if (!string.IsNullOrWhiteSpace(tool.ExecutableName))
                yield return tool.ExecutableName;

            if (!string.IsNullOrWhiteSpace(tool.ExecutablePath))
                yield return Path.GetFileName(tool.ExecutablePath);
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
                EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][tool.Id] = dialog.SelectedPath;
                EnvironmentInfo.Settings.Save();
                OnChangedToolPath(EventArgs.Empty);
            }
        }

        private string ResolvePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return value.Replace("{GamePath}", GameMode.GameModeEnvironmentInfo.InstallationPath ?? string.Empty)
                        .Replace("{ExecutablePath}", GameMode.GameModeEnvironmentInfo.ExecutablePath ?? string.Empty)
                        .Replace("{ModeId}", GameMode.ModeId ?? string.Empty)
                        .Replace("{LocalApplicationData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                        .Replace("{PersonalData}", Environment.GetFolderPath(Environment.SpecialFolder.Personal))
                        .Replace("{ProgramFiles}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                        .Replace("{ProgramFilesX86}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        }

        private void EnsureToolSettings()
        {
            if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] == null)
                EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] = new KeyedSettings<string>();
        }
    }
}
