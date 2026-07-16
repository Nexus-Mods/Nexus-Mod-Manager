using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinitionValidator
    {
        private static readonly HashSet<string> ValidBehaviorProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "generic", "gamebryo" };
        private static readonly HashSet<string> ValidInstallerProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "default", "gamebryo" };
        private static readonly HashSet<string> ValidPathAdjustmentProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "", "none", "cyberpunk2077", "subnautica", "stardewvalley", "sims4", "nomanssky" };
        private static readonly HashSet<string> BlockedToolExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe"
        };

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
            ValidatePluginPolicy(definition, issues);

            ValidateResourceFileName(definition, definition.Resources?.IconPath, "resources.iconPath", issues);
            ValidateResourceFileName(definition, definition.Resources?.CategoriesPath, "resources.categoriesPath", issues);
            ValidateResourceFileName(definition, definition.Resources?.BaseFilesPath, "resources.baseFilesPath", issues);

            if (definition.Launcher != null && string.IsNullOrWhiteSpace(definition.Launcher.DefaultExecutable))
                issues.Add(Error(definition, "launcher.defaultExecutable is required when launcher is present."));

            ValidateSupportedTools(definition, issues);
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

        private void ValidatePluginPolicy(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            GameModePluginPolicyDefinition policy = definition.Plugin == null ? null : definition.Plugin.Policy;
            if (policy == null)
                return;

            if (policy.SchemaVersion != 1)
                issues.Add(Error(definition, "plugin.policy.schemaVersion must be 1."));

            foreach (GameModePluginExtensionPolicyDefinition extension in policy.Extensions ?? Enumerable.Empty<GameModePluginExtensionPolicyDefinition>())
            {
                ValidateExtensions(definition, new[] { extension.Extension }, "plugin.policy.extensions[].extension", issues);
                if (!string.IsNullOrWhiteSpace(extension.ForcedAddressClass) && !Enum.TryParse(extension.ForcedAddressClass, true, out PluginAddressClass _))
                    issues.Add(Error(definition, "plugin.policy.extensions[].forcedAddressClass is invalid: " + extension.ForcedAddressClass));
                if (!string.IsNullOrWhiteSpace(extension.ForcedFlags) && !Enum.TryParse(extension.ForcedFlags, true, out PluginHeaderFlags _))
                    issues.Add(Error(definition, "plugin.policy.extensions[].forcedFlags is invalid: " + extension.ForcedFlags));
            }

            foreach (GameModePluginAddressSpaceDefinition addressSpace in policy.AddressSpaces ?? Enumerable.Empty<GameModePluginAddressSpaceDefinition>())
            {
                if (!Enum.TryParse(addressSpace.AddressClass, true, out PluginAddressClass addressClass) || addressClass == PluginAddressClass.None)
                    issues.Add(Error(definition, "plugin.policy.addressSpaces[].addressClass is invalid: " + addressSpace.AddressClass));
                if (addressSpace.MaxCount < 0)
                    issues.Add(Error(definition, "plugin.policy.addressSpaces[].maxCount cannot be negative."));
            }
        }

        private void ValidateResourceFileName(GameModeDefinition definition, string resourcePath, string fieldName, IList<GameModeDefinitionIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return;
            if (!IsSafeFileName(resourcePath))
            {
                issues.Add(Error(definition, fieldName + " must be a file name without a path."));
                return;
            }
            if (!File.Exists(Path.Combine(definition.DefinitionDirectory ?? string.Empty, resourcePath)))
                issues.Add(Error(definition, fieldName + " references a missing file: " + resourcePath));
        }

        private void ValidateSupportedTools(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.SupportedTools == null)
                return;

            foreach (GameModeToolDefinition tool in definition.SupportedTools.Where(x => x != null))
            {
                string toolName = string.IsNullOrWhiteSpace(tool.Id) ? "supportedTools[]" : "supportedTools['" + tool.Id + "']";
                foreach (string executableName in GetConfiguredExecutableNames(tool))
                {
                    if (!IsSafeToolExecutableName(executableName))
                        issues.Add(Error(definition, toolName + ".executableName must be an allowlisted executable file name without a path: " + executableName));
                }

                if (!string.IsNullOrWhiteSpace(tool.Arguments))
                    issues.Add(Error(definition, toolName + ".arguments is not supported. Use argumentTokens instead."));

                ValidateSafeConfiguredPath(definition, toolName + ".executablePath", tool.ExecutablePath, issues);
                ValidateSafeArgumentTokens(definition, toolName + ".argumentTokens", tool.ArgumentTokens, issues);

                foreach (GameModeToolDiscoveryRuleDefinition rule in tool.DiscoveryRules ?? Enumerable.Empty<GameModeToolDiscoveryRuleDefinition>())
                {
                    ValidateSafeConfiguredPath(definition, toolName + ".discoveryRules[].path", rule.Path, issues);
                    ValidateSafeConfiguredPath(definition, toolName + ".discoveryRules[].pathSuffix", rule.PathSuffix, issues);
                }
            }
        }

        private IEnumerable<string> GetConfiguredExecutableNames(GameModeToolDefinition tool)
        {
            if (tool.ExecutableNames != null)
            {
                foreach (string executableName in tool.ExecutableNames.Where(x => !string.IsNullOrWhiteSpace(x)))
                    yield return executableName;
            }

            if (!string.IsNullOrWhiteSpace(tool.ExecutableName))
                yield return tool.ExecutableName;
        }

        private void ValidateSafeArgumentTokens(GameModeDefinition definition, string fieldName, IEnumerable<string> tokens, IList<GameModeDefinitionIssue> issues)
        {
            if (tokens == null)
                return;

            foreach (string token in tokens.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (LooksLikePath(token) && ContainsParentTraversal(token))
                    issues.Add(Error(definition, fieldName + " cannot contain parent-directory traversal: " + token));
            }
        }

        private void ValidateSafeConfiguredPath(GameModeDefinition definition, string fieldName, string value, IList<GameModeDefinitionIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (ContainsParentTraversal(value))
                issues.Add(Error(definition, fieldName + " cannot contain parent-directory traversal: " + value));
        }

        private bool IsSafeToolExecutableName(string executableName)
        {
            return IsSafeFileName(executableName) &&
                   string.Equals(Path.GetExtension(executableName), ".exe", StringComparison.OrdinalIgnoreCase) &&
                   !BlockedToolExecutables.Contains(executableName);
        }

        private bool IsSafeFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) &&
                   fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   !Path.IsPathRooted(fileName);
        }

        private bool LooksLikePath(string value)
        {
            return value.IndexOf('\\') >= 0 || value.IndexOf('/') >= 0 || value.IndexOf('{') >= 0;
        }

        private bool ContainsParentTraversal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] segments = value.Replace('/', '\\').Split('\\');
            return segments.Any(x => string.Equals(x.Trim(), "..", StringComparison.Ordinal));
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
