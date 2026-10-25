using System;
using System.Collections.Generic;
using Nexus.Client.ModManagement.ManagedContent;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>Preserves the reserved Witcher native host and its historical write-scope compatibility.</summary>
    internal static class Witcher3DefinitionPolicy
    {
        /// <summary>Identifies the reserved host even when a definition attempts to change its profile.</summary>
        internal static bool IsNativeHost(GameModeDefinition definition) => definition != null &&
            (String.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase) ||
             String.Equals(definition.BehaviorProfile, "witcher3remastered", StringComparison.OrdinalIgnoreCase));

        /// <summary>Retains the existing Script Merger host and executable compatibility contract.</summary>
        internal static void RequireReviewedTool(GameModeDefinition game, IEnumerable<string> names)
        {
            if (game == null || game.ModeId != "Witcher3Remastered" || game.BehaviorProfile != "witcher3remastered")
                throw new ArgumentException("The Remastered Script Merger compatibility provider requires the Remastered host.");
            foreach (string name in names)
                if (!ExternalToolLaunchRule.IsScriptMergerName(name))
                    throw new ArgumentException("The Remastered provider accepts only characterized Script Merger executable names.");
        }

        /// <summary>Creates a definition issue without changing its persisted path or game identity.</summary>
        private static GameModeDefinitionIssue Error(GameModeDefinition definition, string path, string message) =>
            GameModeDefinitionValidator.Error(definition.DefinitionPath, definition.ModeId, path, message);

        /// <summary>Validates the native host's fixed executable, catalog and storage compatibility.</summary>
        internal static void Validate(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            // The specific ModeId is reserved too: renaming the profile to 'generic' must
            // never unlock its native installer or bypass edition-aware discovery.
            if (string.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(definition.BehaviorProfile, "witcher3remastered", StringComparison.Ordinal))
                issues.Add(Error(definition, "behaviorProfile", "Witcher3Remastered requires its dedicated edition-aware behavior profile."));

            if (String.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase) &&
                definition.NexusDomain != null && definition.NexusDomain != "witcher3")
                issues.Add(Error(definition, "nexusDomain", "Witcher3Remastered must use the witcher3 Nexus catalog."));

            if (!string.Equals(definition.BehaviorProfile, "witcher3remastered", StringComparison.OrdinalIgnoreCase))
                return;

            // The reserved native adapter has a fixed write scope and a separate release gate.
            // JSON cannot select the generic installer or widen the reviewed native workflow.
            if (!string.Equals(definition.ModeId, "Witcher3Remastered", StringComparison.Ordinal))
                issues.Add(Error(definition, "modeId", "The witcher3remastered profile is reserved for the Witcher3Remastered ModeId."));
            if (definition.GameExecutables == null || definition.GameExecutables.Length != 1 ||
                !DataDrivenDefinitionRules.PathsEqual(definition.GameExecutables[0], @"bin\x64_dx12\witcher3.exe"))
                issues.Add(Error(definition, "gameExecutables", "The Remastered edition must use only the DX12 executable."));
            if (definition.ModInstall == null ||
                !string.Equals(definition.ModInstall.PathAdjustmentProfile, "none", StringComparison.Ordinal) ||
                definition.ModInstall.SupportsGameRootInstall != false ||
                !string.IsNullOrWhiteSpace(definition.ModInstall.ManagedInstallationPath))
                issues.Add(Error(definition, "modInstall", "The Remastered profile requires ordinary Data-root installation with path adjustment set to none."));
            if (definition.Storage != null && definition.Storage.ShareModsStorageWith != null &&
                definition.Storage.ShareModsStorageWith.Length > 0)
                issues.Add(Error(definition, "storage.shareModsStorageWith", "Witcher3Remastered must not share native storage with another ModeId."));
            if (definition.Launcher == null ||
                !DataDrivenDefinitionRules.PathsEqual(definition.Launcher.DefaultExecutable, @"bin\x64_dx12\witcher3.exe"))
                issues.Add(Error(definition, "launcher.defaultExecutable", "The Remastered launcher must be the DX12 executable."));
        }
    }
}
