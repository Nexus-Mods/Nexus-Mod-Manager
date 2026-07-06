using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinitionValidator
    {
        private static readonly HashSet<string> ValidBehaviorProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "generic", "gamebryo" };
        private static readonly HashSet<string> ValidInstallerProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default", "gamebryo" };
        private static readonly HashSet<string> ValidPathAdjustmentProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "none", "cyberpunk2077", "subnautica", "stardewvalley", "sims4", "nomanssky" };

        public IEnumerable<GameModeDefinitionIssue> Validate(GameModeDefinition definition)
        {
            var issues = new List<GameModeDefinitionIssue>();
            if (definition == null)
            {
                issues.Add(Error(null, null, "Definition could not be deserialized."));
                return issues;
            }

            if (definition.SchemaVersion != 1)
                issues.Add(Error(definition, "Unsupported schemaVersion. Expected 1."));
            if (string.IsNullOrWhiteSpace(definition.ModeId))
                issues.Add(Error(definition, "modeId is required."));
            if (string.IsNullOrWhiteSpace(definition.Name))
                issues.Add(Error(definition, "name is required."));
            if (definition.GameExecutables == null || definition.GameExecutables.Length == 0 || definition.GameExecutables.Any(string.IsNullOrWhiteSpace))
                issues.Add(Error(definition, "At least one game executable is required."));

            string behaviorProfile = definition.BehaviorProfile ?? "generic";
            if (!ValidBehaviorProfiles.Contains(behaviorProfile))
                issues.Add(Error(definition, "Invalid behaviorProfile '" + behaviorProfile + "'."));

            string installerProfile = definition.InstallerProfile ?? "default";
            if (!ValidInstallerProfiles.Contains(installerProfile))
                issues.Add(Error(definition, "Invalid installerProfile '" + installerProfile + "'."));

            string pathProfile = definition.ModInstall?.PathAdjustmentProfile ?? string.Empty;
            if (!ValidPathAdjustmentProfiles.Contains(pathProfile))
                issues.Add(Error(definition, "Invalid modInstall.pathAdjustmentProfile '" + pathProfile + "'."));

            ValidatePluginSorting(definition, behaviorProfile, issues);

            ValidateRelativeResource(definition, definition.Resources?.IconPath, "resources.iconPath", issues);
            ValidateRelativeResource(definition, definition.Resources?.CategoriesPath, "resources.categoriesPath", issues);
            ValidateRelativeResource(definition, definition.Resources?.BaseFilesPath, "resources.baseFilesPath", issues);

            if (definition.Launcher != null && string.IsNullOrWhiteSpace(definition.Launcher.DefaultExecutable))
                issues.Add(Error(definition, "launcher.defaultExecutable is required when launcher is present."));

            ValidateExtensions(definition, definition.PluginExtensions, "pluginExtensions", issues);
            ValidateExtensions(definition, definition.ModInstall?.HardlinkRequiredExtensions, "modInstall.hardlinkRequiredExtensions", issues);
            ValidateExtensions(definition, definition.ModInstall?.RealFileRequiredExtensions, "modInstall.realFileRequiredExtensions", issues);

            return issues;
        }

        private void ValidatePluginSorting(GameModeDefinition definition, string behaviorProfile, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Plugin == null || !definition.Plugin.SupportsPluginAutoSorting)
                return;

            if (!definition.Plugin.UsesPlugins)
            {
                issues.Add(Error(definition, "plugin.supportsPluginAutoSorting cannot be true when plugin.usesPlugins is false."));
                return;
            }

            if (!string.Equals(behaviorProfile, "gamebryo", StringComparison.OrdinalIgnoreCase))
                issues.Add(Error(definition, "plugin.supportsPluginAutoSorting is only supported by data-driven Gamebryo modes."));
        }

        private void ValidateRelativeResource(GameModeDefinition definition, string resourcePath, string fieldName, IList<GameModeDefinitionIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return;
            if (Path.IsPathRooted(resourcePath))
            {
                issues.Add(Error(definition, fieldName + " must be relative to the definition folder."));
                return;
            }
            if (!File.Exists(Path.Combine(definition.DefinitionDirectory ?? string.Empty, resourcePath)))
                issues.Add(Error(definition, fieldName + " references a missing file: " + resourcePath));
        }

        private void ValidateExtensions(GameModeDefinition definition, IEnumerable<string> extensions, string fieldName, IList<GameModeDefinitionIssue> issues)
        {
            if (extensions == null)
                return;
            foreach (string extension in extensions)
            {
                if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith("."))
                    issues.Add(Error(definition, fieldName + " contains an invalid extension: " + extension));
            }
        }

        private GameModeDefinitionIssue Error(GameModeDefinition definition, string message)
        {
            return Error(definition?.DefinitionPath, definition?.ModeId, message);
        }

        private GameModeDefinitionIssue Error(string filePath, string gameModeId, string message)
        {
            return new GameModeDefinitionIssue { FilePath = filePath, GameModeId = gameModeId, Severity = GameModeDefinitionIssueSeverity.Error, Message = message };
        }
    }
}
