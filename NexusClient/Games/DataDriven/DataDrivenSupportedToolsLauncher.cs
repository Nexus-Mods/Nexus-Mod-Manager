using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Nexus.Client.Commands;
using Nexus.Client.Settings;

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
                if (tool == null || string.IsNullOrWhiteSpace(tool.Id) || string.IsNullOrWhiteSpace(tool.ExecutablePath))
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
            Launch(GetToolLaunchCommand(tool), ResolveArguments(tool.Arguments));
        }

        private string GetToolLaunchCommand(GameModeToolDefinition tool)
        {
            EnsureToolSettings();

            string folder = null;
            if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId].ContainsKey(tool.Id))
                folder = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId][tool.Id];

            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                return Path.Combine(folder, Path.GetFileName(tool.ExecutablePath));

            string expanded = ResolvePath(tool.ExecutablePath);
            if (Path.IsPathRooted(expanded))
                return expanded;

            string gameRelative = Path.Combine(GameMode.GameModeEnvironmentInfo.ExecutablePath ?? string.Empty, expanded);
            return File.Exists(gameRelative) ? gameRelative : null;
        }

        private void ConfigTool(GameModeToolDefinition tool)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = string.Format("Select the folder where the {0} executable is located.", tool.Name);
                dialog.ShowNewFolderButton = false;
                if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    return;

                string executablePath = Path.Combine(dialog.SelectedPath, Path.GetFileName(tool.ExecutablePath));
                if (!File.Exists(executablePath))
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
                        .Replace("{ModeId}", GameMode.ModeId ?? string.Empty);
        }

        private string ResolveArguments(string value)
        {
            return ResolvePath(value);
        }

        private void EnsureToolSettings()
        {
            if (EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] == null)
                EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId] = new KeyedSettings<string>();
        }
    }
}