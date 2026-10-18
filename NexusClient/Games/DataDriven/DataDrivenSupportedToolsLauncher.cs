using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using Microsoft.Win32;
using Nexus.Client.Commands;
using Nexus.Client.Games.Witcher3;
using Nexus.Client.ModManagement.ManagedContent;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenSupportedToolsLauncher : SupportedToolsLauncherBase
    {
        private readonly GameModeDefinition _definition;
        private readonly Dictionary<GameModeToolDefinition, ExternalToolLaunchRule> _reviewRules =
            new Dictionary<GameModeToolDefinition, ExternalToolLaunchRule>();
        private bool _reviewInProgress;

        public DataDrivenSupportedToolsLauncher(IGameMode gameMode, IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(gameMode, environmentInfo)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            foreach (GameModeToolDefinition tool in (definition.SupportedTools ?? Enumerable.Empty<GameModeToolDefinition>()).Where(item => item != null))
            {
                ExternalToolLaunchRule rule = GameModeToolReviewDefinition.Create(tool, definition);
                if (rule != null) _reviewRules.Add(tool, rule);
            }
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
                    AddLaunchCommand(new Command(tool.Id, LanguageManager.Format("GameModes.Commands.Tool.LaunchName", "Launch {0}", tool.Name), LanguageManager.Format("GameModes.Commands.Tool.LaunchDescription", "Launches {0}.", tool.Name), icon, () => LaunchTool(tool), true));
                    if (DefaultLaunchCommand == null)
                        DefaultLaunchCommand = new Command(LanguageManager.Format("GameModes.Commands.Tool.LaunchName", "Launch {0}", tool.Name), LanguageManager.Format("GameModes.Commands.Tool.LaunchDescription", "Launches {0}.", tool.Name), () => LaunchTool(tool));
                }
                else
                {
                    AddLaunchCommand(new Command("Config#" + tool.Id, LanguageManager.Format("GameModes.Commands.Tool.ConfigName", "Config {0}", tool.Name), LanguageManager.Format("GameModes.Commands.Tool.ConfigDescription", "Configures {0}.", tool.Name), null, () => ConfigTool(tool), true));
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
            ExternalToolLaunchRule rule;
            if (_reviewRules.TryGetValue(tool, out rule))
            {
                LaunchReviewedTool(tool, rule);
                return;
            }
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

        /// <summary>Reviews detached executable evidence and unmanaged output before an explicit tool launch.</summary>
        private async void LaunchReviewedTool(GameModeToolDefinition tool, ExternalToolLaunchRule rule)
        {
            if (_reviewInProgress) return;
            _reviewInProgress = true;
            try
            {
                string command = GetToolLaunchCommand(tool);
                string arguments = BuildArgumentString(tool.ArgumentTokens);
                string gameRoot = GetGameRootPath();
                string blockMessage = GetReviewedTargetBlockMessage(rule, gameRoot);
                if (blockMessage != null)
                {
                    ShowReviewFailure(blockMessage);
                    return;
                }
                ExternalToolLaunchObservation observed = await Task.Run(() =>
                    ExternalToolLaunchReview.Inspect(rule, command)).ConfigureAwait(true);
                if (!observed.CanLaunch)
                {
                    ShowReviewFailure(observed.Assessment);
                    return;
                }
                string message = LanguageManager.Format("ManagedContent.Tool.Review",
                    "Executable: {0}\nProduct: {1}\nVersion: {2}\nArguments: {3}\nCurrent game root: {4}\nSHA-256: {5}\n\n{6}\n\nVersion information is self-reported and does not prove authenticity or working merges. Confirm the tool's own target directory before applying changes. NMM will not track or undo files written by this tool. Starting the tool does not mean a merge succeeded. Finish installing, enabling or disabling mods before using the tool, then refresh Components after closing it.\n\nLaunch this external tool?",
                    observed.ExecutablePath, observed.Product, observed.Version, arguments, gameRoot, observed.Sha256, observed.Assessment);
                if (XtraMessageBox.Show(message, LanguageManager.Get("ManagedContent.Tool.ReviewTitle", "Review external tool launch"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                ExternalToolLaunchObservation current = await Task.Run(() =>
                    ExternalToolLaunchReview.Inspect(rule, command)).ConfigureAwait(true);
                if (!observed.Matches(current) ||
                    !String.Equals(command, GetToolLaunchCommand(tool), StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(gameRoot, GetGameRootPath(), StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(arguments, BuildArgumentString(tool.ArgumentTokens), StringComparison.Ordinal))
                {
                    ShowReviewFailure(LanguageManager.Get("ManagedContent.Tool.ReviewChanged",
                        "The executable, arguments or current game root changed during review. Start a new review."));
                    return;
                }
                blockMessage = GetReviewedTargetBlockMessage(rule, gameRoot);
                if (blockMessage != null)
                {
                    ShowReviewFailure(blockMessage);
                    return;
                }
                // Keep the existing launch event, process setup and result reporting. This is not a native effect.
                Trace.TraceInformation("Launching reviewed external tool {0}", tool.Name);
                Trace.Indent();
                Launch(command, arguments);
            }
            catch (Exception error)
            {
                Trace.TraceError("External tool review failed: {0}", error);
                ShowReviewFailure(LanguageManager.Get("ManagedContent.Tool.ReviewFailed",
                    "The external tool could not be reviewed or launched. Check its configuration and try again."));
            }
            finally
            {
                _reviewInProgress = false;
            }
        }

        /// <summary>Rechecks existing target/edition policy without granting native write authority.</summary>
        private string GetReviewedTargetBlockMessage(ExternalToolLaunchRule rule, string gameRoot)
        {
            if (String.IsNullOrWhiteSpace(gameRoot) || !Path.IsPathRooted(gameRoot) || !Directory.Exists(gameRoot))
                return LanguageManager.Get("ManagedContent.Tool.TargetUnavailable", "The current game root is unavailable. Refresh the game setup before launching this tool.");
            if (rule.Compatibility == ExternalToolLaunchRule.RemasteredCompatibility)
            {
                Witcher3LaunchDecision decision = Witcher3LaunchPlanner.ReviewCurrentInstallation(
                    GameMode.GameModeEnvironmentInfo.InstallationPath,
                    GameMode.GameModeEnvironmentInfo.ExecutablePath, Witcher3Edition.Remastered);
                return decision.CanLaunch ? null : decision.GetUserMessage();
            }
            return null;
        }

        /// <summary>Shows a local review failure without reporting an external process as started.</summary>
        private static void ShowReviewFailure(string message)
        {
            XtraMessageBox.Show(message, LanguageManager.Get("ManagedContent.Tool.ReviewTitle", "Review external tool launch"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            using (var dialog = new XtraFolderBrowserDialog())
            {
                dialog.Description = LanguageManager.Format("GameModes.SupportedTools.SelectExecutableFolder", "Select the folder where the {0} executable is located.", tool.Name);

                string current;
                KeyedSettings<string> settings = EnvironmentInfo.Settings.SupportedTools[GameMode.ModeId];
                if (settings != null && settings.TryGetValue(GetToolSettingsKey(tool), out current) && Directory.Exists(current))
                    dialog.SelectedPath = current;

                if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                    return;
                if (string.IsNullOrWhiteSpace(FindExecutable(dialog.SelectedPath, tool)))
                {
                    XtraMessageBox.Show(LanguageManager.Format("GameModes.DataDriven.ToolNotFound.Message", "The selected folder does not contain a supported executable for {0}.", tool.Name), LanguageManager.Get("GameModes.DataDriven.ToolNotFound.Title", "Tool not found"), MessageBoxButtons.OK, MessageBoxIcon.Information);
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
