using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinitionValidator
    {
        private static readonly HashSet<string> ValidBehaviorProfiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "generic",
            "gamebryo"
        };

        private static readonly HashSet<string> ValidPathAdjustmentProfiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "none",
            "cyberpunk2077",
            "fallout4",
            "oblivionremastered",
            "starfield",
            "stardewvalley",
            "subnautica",
            "sims4",
            "nomanssky"
        };

        private static readonly HashSet<string> ValidStores = new HashSet<string>(StringComparer.Ordinal)
        {
            "Steam",
            "GOG",
            "Epic",
            "MicrosoftStore",
            "Xbox",
            "Custom"
        };

        public IEnumerable<GameModeDefinitionIssue> Validate(GameModeDefinition definition)
        {
            var issues = new List<GameModeDefinitionIssue>();
            if (definition == null)
            {
                issues.Add(Error(null, null, null, "Definition could not be deserialized."));
                return issues;
            }

            if (definition.SchemaVersion != 2)
                issues.Add(Error(definition, "schemaVersion", "Unsupported schemaVersion. Expected 2."));

            if (!DataDrivenDefinitionRules.IsIdentifier(definition.ModeId))
                issues.Add(Error(definition, "modeId", "modeId is required and must begin with a letter and contain only letters, digits, '.', '_' or '-'."));

            if (string.IsNullOrWhiteSpace(definition.Name))
                issues.Add(Error(definition, "name", "name is required."));

            if (definition.CriticalFilesErrorMessage != null && string.IsNullOrWhiteSpace(definition.CriticalFilesErrorMessage))
                issues.Add(Error(definition, "criticalFilesErrorMessage", "criticalFilesErrorMessage cannot be empty when present."));

            ValidateUniqueStrings(definition, definition.CompatibilityNotes, "compatibilityNotes", issues, false);

            if (string.IsNullOrWhiteSpace(definition.BehaviorProfile) || !ValidBehaviorProfiles.Contains(definition.BehaviorProfile))
                issues.Add(Error(definition, "behaviorProfile", "behaviorProfile must be either 'generic' or 'gamebryo'."));

            ValidateExecutablePaths(definition, definition.GameExecutables, "gameExecutables", true, issues);
            ValidateRelativePaths(definition, definition.StopFolders, "stopFolders", issues);
            ValidateExtensions(definition, definition.PluginExtensions, "pluginExtensions", issues);
            ValidateFileNames(definition, definition.OrderedCriticalPluginNames, "orderedCriticalPluginNames", issues);
            ValidateFileNames(definition, definition.OrderedOfficialPluginNames, "orderedOfficialPluginNames", issues);
            ValidateFileNames(definition, definition.OrderedOfficialUnmanagedPluginNames, "orderedOfficialUnmanagedPluginNames", issues);
            ValidateIdentifiers(definition, definition.SupportedFormats, "supportedFormats", issues);
            ValidateRequiredTool(definition, issues);
            ValidateDiscovery(definition, issues);
            ValidateResources(definition, issues);
            ValidateLauncher(definition, issues);
            ValidateTheme(definition, issues);
            ValidateSetupAndSettings(definition, issues);
            ValidateStorage(definition, issues);
            ValidateModInstall(definition, issues);
            ValidateProfile(definition, issues);
            ValidateGamebryo(definition, issues);

            return issues;
        }

        private void ValidateStorage(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Storage == null || definition.Storage.ShareModsStorageWith == null)
                return;

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definition.Storage.ShareModsStorageWith.Length; i++)
            {
                string modeId = definition.Storage.ShareModsStorageWith[i];
                string path = "storage.shareModsStorageWith[" + i + "]";

                if (!DataDrivenDefinitionRules.IsIdentifier(modeId))
                    issues.Add(Error(definition, path, "Value must be an exact Game Mode modeId."));
                else if (string.Equals(modeId, definition.ModeId, StringComparison.OrdinalIgnoreCase))
                    issues.Add(Error(definition, path, "A Game Mode cannot share its Mods storage with itself."));
                else if (!unique.Add(modeId))
                    issues.Add(Error(definition, path, "Duplicate modeId: " + modeId));
            }
        }

        private void ValidateProfile(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (string.Equals(definition.BehaviorProfile, "generic", StringComparison.OrdinalIgnoreCase))
            {
                if (definition.Plugin != null)
                    issues.Add(Error(definition, "plugin", "Generic data-driven Game Modes cannot declare plugin management settings."));
                if (definition.PluginExtensions != null)
                    issues.Add(Error(definition, "pluginExtensions", "Generic data-driven Game Modes cannot declare pluginExtensions."));
                if (definition.OrderedCriticalPluginNames != null)
                    issues.Add(Error(definition, "orderedCriticalPluginNames", "Generic data-driven Game Modes cannot declare ordered plugin groups."));
                if (definition.OrderedOfficialPluginNames != null)
                    issues.Add(Error(definition, "orderedOfficialPluginNames", "Generic data-driven Game Modes cannot declare ordered plugin groups."));
                if (definition.OrderedOfficialUnmanagedPluginNames != null)
                    issues.Add(Error(definition, "orderedOfficialUnmanagedPluginNames", "Generic data-driven Game Modes cannot declare ordered plugin groups."));
                if (definition.Gamebryo != null)
                    issues.Add(Error(definition, "gamebryo", "Generic data-driven Game Modes cannot declare Gamebryo settings."));
                return;
            }

            if (!string.Equals(definition.BehaviorProfile, "gamebryo", StringComparison.OrdinalIgnoreCase))
                return;

            if (definition.Plugin == null)
            {
                issues.Add(Error(definition, "plugin", "Gamebryo data-driven Game Modes require a plugin section."));
                return;
            }

            if (definition.PluginExtensions == null || definition.PluginExtensions.Length == 0)
                issues.Add(Error(definition, "pluginExtensions", "Gamebryo data-driven Game Modes require at least one plugin extension."));

            if (!definition.Plugin.UsesPlugins.HasValue)
                issues.Add(Error(definition, "plugin.usesPlugins", "plugin.usesPlugins is required."));
            else if (!definition.Plugin.UsesPlugins.Value)
                issues.Add(Error(definition, "plugin.usesPlugins", "Gamebryo data-driven Game Modes must set plugin.usesPlugins to true."));

            if (definition.Plugin.PluginDirectorySuffix == null)
                issues.Add(Error(definition, "plugin.pluginDirectorySuffix", "plugin.pluginDirectorySuffix is required."));
            else if (!DataDrivenDefinitionRules.IsSafeRelativePath(definition.Plugin.PluginDirectorySuffix, true))
                issues.Add(Error(definition, "plugin.pluginDirectorySuffix", "plugin.pluginDirectorySuffix must be a safe relative path or an empty string."));

            if (!definition.Plugin.SupportsPluginAutoSorting.HasValue)
                issues.Add(Error(definition, "plugin.supportsPluginAutoSorting", "plugin.supportsPluginAutoSorting is required."));

            if (!definition.Plugin.MaxAllowedActivePluginsCount.HasValue)
                issues.Add(Error(definition, "plugin.maxAllowedActivePluginsCount", "plugin.maxAllowedActivePluginsCount is required."));
            else if (definition.Plugin.MaxAllowedActivePluginsCount.Value < 0)
                issues.Add(Error(definition, "plugin.maxAllowedActivePluginsCount", "plugin.maxAllowedActivePluginsCount cannot be negative."));

            if (definition.Plugin.OfficialUnmanagedPluginListFiles != null)
            {
                ValidatePathTemplates(definition, definition.Plugin.OfficialUnmanagedPluginListFiles, "plugin.officialUnmanagedPluginListFiles", issues);
                for (int i = 0; i < definition.Plugin.OfficialUnmanagedPluginListFiles.Length; i++)
                {
                    if (DataDrivenDefinitionRules.ContainsPlaceholder(definition.Plugin.OfficialUnmanagedPluginListFiles[i], "{UserGameData}"))
                        issues.Add(Error(definition, "plugin.officialUnmanagedPluginListFiles[" + i + "]", "Official unmanaged plugin list paths cannot use {UserGameData}."));
                }
            }

            ValidatePluginPolicy(definition, definition.Plugin.Policy, issues);
        }

        private void ValidatePluginPolicy(GameModeDefinition definition, GameModePluginPolicyDefinition policy, IList<GameModeDefinitionIssue> issues)
        {
            if (policy == null)
                return;

            if (policy.SchemaVersion != 1)
                issues.Add(Error(definition, "plugin.policy.schemaVersion", "plugin.policy.schemaVersion must be 1."));

            ValidateStrategyIdentifier(definition, policy.ParserStrategy, "plugin.policy.parserStrategy", issues);
            ValidateStrategyIdentifier(definition, policy.PersistenceStrategy, "plugin.policy.persistenceStrategy", issues);

            if (policy.EncodingName != null)
            {
                if (string.IsNullOrWhiteSpace(policy.EncodingName))
                {
                    issues.Add(Error(definition, "plugin.policy.encodingName", "encodingName cannot be empty when present."));
                }
                else
                {
                    try
                    {
                        Encoding.GetEncoding(policy.EncodingName);
                    }
                    catch (Exception)
                    {
                        issues.Add(Error(definition, "plugin.policy.encodingName", "Unknown text encoding: " + policy.EncodingName));
                    }
                }
            }

            if (policy.AppDataGameFolderName != null && string.IsNullOrWhiteSpace(policy.AppDataGameFolderName))
                issues.Add(Error(definition, "plugin.policy.appDataGameFolderName", "appDataGameFolderName cannot be empty when present."));

            ValidateOptionalPathTemplate(definition, policy.PluginsFilePath, "plugin.policy.pluginsFilePath", issues);
            ValidateOptionalPathTemplate(definition, policy.LoadOrderFilePath, "plugin.policy.loadOrderFilePath", issues);

            var extensionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (policy.Extensions == null ? 0 : policy.Extensions.Count); i++)
            {
                GameModePluginExtensionPolicyDefinition extension = policy.Extensions[i];
                string path = "plugin.policy.extensions[" + i + "]";
                if (extension == null)
                {
                    issues.Add(Error(definition, path, "Extension policy cannot be null."));
                    continue;
                }

                if (!DataDrivenDefinitionRules.IsFileExtension(extension.Extension))
                    issues.Add(Error(definition, path + ".extension", "A valid extension beginning with '.' is required."));
                else if (!extensionKeys.Add(extension.Extension))
                    issues.Add(Error(definition, path + ".extension", "Duplicate extension policy: " + extension.Extension));

                PluginHeaderFlags parsedFlags;
                if (extension.ForcedFlags != null && !DataDrivenDefinitionRules.TryParseHeaderFlags(extension.ForcedFlags, out parsedFlags))
                    issues.Add(Error(definition, path + ".forcedFlags", "Invalid non-empty plugin header flags: " + extension.ForcedFlags));

                PluginAddressClass addressClass;
                if (extension.ForcedAddressClass != null && !DataDrivenDefinitionRules.TryParseAddressClass(extension.ForcedAddressClass, true, out addressClass))
                    issues.Add(Error(definition, path + ".forcedAddressClass", "Invalid plugin address class: " + extension.ForcedAddressClass));
            }

            var mappingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (policy.HeaderFlagMappings == null ? 0 : policy.HeaderFlagMappings.Count); i++)
            {
                GameModePluginHeaderFlagMappingDefinition mapping = policy.HeaderFlagMappings[i];
                string path = "plugin.policy.headerFlagMappings[" + i + "]";
                if (mapping == null)
                {
                    issues.Add(Error(definition, path, "Header flag mapping cannot be null."));
                    continue;
                }

                PluginHeaderFlagSource source;
                if (!DataDrivenDefinitionRules.TryParseHeaderFlagSource(mapping.Source, out source))
                    issues.Add(Error(definition, path + ".source", "Invalid header flag source: " + mapping.Source));

                uint mask;
                if (!DataDrivenDefinitionRules.TryParseUInt32(mapping.Mask, out mask) || mask == 0)
                    issues.Add(Error(definition, path + ".mask", "Header flag mask must be a non-zero UInt32 integer or 0x hexadecimal string."));

                PluginHeaderFlags flags;
                if (!DataDrivenDefinitionRules.TryParseHeaderFlags(mapping.Flags, out flags) || flags == PluginHeaderFlags.None)
                    issues.Add(Error(definition, path + ".flags", "Header flag mapping must specify valid non-empty flags."));

                string key = (mapping.Source ?? string.Empty) + "|" + mask.ToString(CultureInfo.InvariantCulture);
                if (!mappingKeys.Add(key))
                    issues.Add(Error(definition, path, "Duplicate header flag mapping for source and mask."));
            }

            var addressClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (policy.AddressSpaces == null ? 0 : policy.AddressSpaces.Count); i++)
            {
                GameModePluginAddressSpaceDefinition addressSpace = policy.AddressSpaces[i];
                string path = "plugin.policy.addressSpaces[" + i + "]";
                if (addressSpace == null)
                {
                    issues.Add(Error(definition, path, "Address-space policy cannot be null."));
                    continue;
                }

                PluginAddressClass addressClass;
                if (!DataDrivenDefinitionRules.TryParseAddressClass(addressSpace.AddressClass, false, out addressClass))
                    issues.Add(Error(definition, path + ".addressClass", "Invalid non-None plugin address class: " + addressSpace.AddressClass));
                else if (!addressClasses.Add(addressSpace.AddressClass))
                    issues.Add(Error(definition, path + ".addressClass", "Duplicate address-space class: " + addressSpace.AddressClass));

                if (!addressSpace.FirstIndex.HasValue || addressSpace.FirstIndex.Value < 0)
                    issues.Add(Error(definition, path + ".firstIndex", "firstIndex is required and cannot be negative."));
                if (!addressSpace.MaxCount.HasValue || addressSpace.MaxCount.Value < 0)
                    issues.Add(Error(definition, path + ".maxCount", "maxCount is required and cannot be negative."));

                if (addressSpace.DisplayFormat != null)
                {
                    if (string.IsNullOrWhiteSpace(addressSpace.DisplayFormat))
                        issues.Add(Error(definition, path + ".displayFormat", "displayFormat cannot be empty when present."));
                    else if (!DataDrivenDefinitionRules.IsUsableDisplayFormat(addressSpace.DisplayFormat))
                        issues.Add(Error(definition, path + ".displayFormat", "displayFormat is not a usable integer format."));
                }
            }

            ValidateUniqueFileNames(definition, policy.OfficialPlugins, "plugin.policy.officialPlugins", issues);
            ValidateUniqueFileNames(definition, policy.CriticalPlugins, "plugin.policy.criticalPlugins", issues);
            ValidateUniqueFileNames(definition, policy.FixedOrderPlugins, "plugin.policy.fixedOrderPlugins", issues);
            ValidateUniqueFileNames(definition, policy.ForcedActivePlugins, "plugin.policy.forcedActivePlugins", issues);
            ValidateUniqueFileNames(definition, policy.BlueprintPlugins, "plugin.policy.blueprintPlugins", issues);
            ValidateUniqueStrings(definition, policy.BlueprintPluginPrefixes, "plugin.policy.blueprintPluginPrefixes", issues, false);
        }

        private void ValidateStrategyIdentifier(GameModeDefinition definition, string value, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (value != null && !DataDrivenDefinitionRules.IsIdentifier(value))
                issues.Add(Error(definition, path, "Strategy names must be non-empty stable identifiers."));
        }

        private void ValidateRequiredTool(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            bool anyFieldPresent = definition.RequiredToolName != null || definition.OrderedRequiredToolFileNames != null || definition.RequiredToolErrorMessage != null;
            if (!anyFieldPresent)
                return;

            bool hasName = !string.IsNullOrWhiteSpace(definition.RequiredToolName);
            bool hasFiles = definition.OrderedRequiredToolFileNames != null;
            bool hasMessage = !string.IsNullOrWhiteSpace(definition.RequiredToolErrorMessage);

            if (!hasName)
                issues.Add(Error(definition, "requiredToolName", "requiredToolName is required when mandatory-tool fields are used."));
            if (!hasFiles || definition.OrderedRequiredToolFileNames.Length == 0)
                issues.Add(Error(definition, "orderedRequiredToolFileNames", "At least one required-tool file is required."));
            else
                ValidateRelativePaths(definition, definition.OrderedRequiredToolFileNames, "orderedRequiredToolFileNames", issues);
            if (!hasMessage)
                issues.Add(Error(definition, "requiredToolErrorMessage", "requiredToolErrorMessage is required when mandatory-tool fields are used."));
        }

        private void ValidateDiscovery(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            GameModeDiscoveryDefinition discovery = definition.Discovery;
            if (discovery == null)
                return;

            bool hasSteam = !string.IsNullOrWhiteSpace(discovery.SteamAppId);
            bool hasGog = !string.IsNullOrWhiteSpace(discovery.GogRegistryKey);
            bool hasStores = discovery.Stores != null && discovery.Stores.Count > 0;
            if (!hasSteam && !hasGog && !hasStores)
                issues.Add(Error(definition, "discovery", "discovery must contain at least one Steam, GOG or stores locator."));

            if (discovery.Stores != null && discovery.Stores.Count == 0)
                issues.Add(Error(definition, "discovery.stores", "stores cannot be empty when present."));

            if (discovery.SteamAppId != null && (!hasSteam || !Regex.IsMatch(discovery.SteamAppId, "^[0-9]+$")))
                issues.Add(Error(definition, "discovery.steamAppId", "steamAppId must be a non-empty digits-only string."));

            if (discovery.SteamInstallFolderName != null)
            {
                if (string.IsNullOrWhiteSpace(discovery.SteamInstallFolderName))
                    issues.Add(Error(definition, "discovery.steamInstallFolderName", "steamInstallFolderName cannot be empty when present."));
                if (!hasSteam)
                    issues.Add(Error(definition, "discovery.steamInstallFolderName", "steamInstallFolderName requires steamAppId."));
            }

            if (discovery.SteamExecutableName != null)
            {
                if (!hasSteam)
                    issues.Add(Error(definition, "discovery.steamExecutableName", "steamExecutableName requires steamAppId."));
                if (!DataDrivenDefinitionRules.IsSafeExecutableRelativePath(discovery.SteamExecutableName))
                    issues.Add(Error(definition, "discovery.steamExecutableName", "steamExecutableName must be a safe game-relative .exe path."));
            }

            if (discovery.GogRegistryKey != null && !hasGog)
                issues.Add(Error(definition, "discovery.gogRegistryKey", "gogRegistryKey cannot be empty when present."));
            if (discovery.GogPathValueName != null && !hasGog)
                issues.Add(Error(definition, "discovery.gogPathValueName", "gogPathValueName requires gogRegistryKey."));

            for (int i = 0; i < (discovery.Stores == null ? 0 : discovery.Stores.Count); i++)
            {
                GameModeStoreDiscoveryDefinition store = discovery.Stores[i];
                string path = "discovery.stores[" + i + "]";
                if (store == null)
                {
                    issues.Add(Error(definition, path, "Store discovery entry cannot be null."));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(store.Store) || !ValidStores.Contains(store.Store))
                {
                    issues.Add(Error(definition, path + ".store", "Unsupported store: " + store.Store));
                    continue;
                }

                ValidateOptionalNonEmptyString(definition, store.Id, path + ".id", issues);
                ValidateOptionalNonEmptyString(definition, store.InstallFolderName, path + ".installFolderName", issues);
                ValidateOptionalNonEmptyString(definition, store.RegistryKey, path + ".registryKey", issues);

                bool anyLocator = !string.IsNullOrWhiteSpace(store.Id) || !string.IsNullOrWhiteSpace(store.InstallFolderName) ||
                                  !string.IsNullOrWhiteSpace(store.ExecutableName) || !string.IsNullOrWhiteSpace(store.RegistryKey);
                if (!anyLocator)
                    issues.Add(Error(definition, path, "Store discovery entry must contain at least one locator."));

                if ((store.Store.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
                     store.Store.Equals("Epic", StringComparison.OrdinalIgnoreCase) ||
                     store.Store.Equals("MicrosoftStore", StringComparison.OrdinalIgnoreCase) ||
                     store.Store.Equals("Xbox", StringComparison.OrdinalIgnoreCase)) && string.IsNullOrWhiteSpace(store.Id))
                    issues.Add(Error(definition, path + ".id", store.Store + " discovery requires id."));

                if (store.Store.Equals("GOG", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(store.Id) && string.IsNullOrWhiteSpace(store.RegistryKey))
                    issues.Add(Error(definition, path, "GOG discovery requires id or registryKey."));

                if (store.ExecutableName != null && !DataDrivenDefinitionRules.IsSafeExecutableRelativePath(store.ExecutableName))
                    issues.Add(Error(definition, path + ".executableName", "executableName must be a safe game-relative .exe path."));
                if (store.PathSuffix != null && !DataDrivenDefinitionRules.IsSafeRelativePath(store.PathSuffix, false))
                    issues.Add(Error(definition, path + ".pathSuffix", "pathSuffix must be a safe non-empty relative path."));
            }
        }

        private void ValidateOptionalNonEmptyString(GameModeDefinition definition, string value, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
                issues.Add(Error(definition, path, "Value cannot be empty when present."));
        }

        private void ValidateResources(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Resources == null)
                return;

            var resources = new[]
            {
                new KeyValuePair<string, string>("resources.iconPath", definition.Resources.IconPath),
                new KeyValuePair<string, string>("resources.categoriesPath", definition.Resources.CategoriesPath),
                new KeyValuePair<string, string>("resources.baseFilesPath", definition.Resources.BaseFilesPath)
            };

            if (resources.All(x => string.IsNullOrWhiteSpace(x.Value)))
                issues.Add(Error(definition, "resources", "resources must contain at least one resource file name."));

            foreach (KeyValuePair<string, string> resource in resources)
            {
                if (string.IsNullOrWhiteSpace(resource.Value))
                    continue;
                if (!DataDrivenDefinitionRules.IsSafeFileName(resource.Value))
                {
                    issues.Add(Error(definition, resource.Key, "Resource must be a file name without a path."));
                    continue;
                }

                if (!File.Exists(Path.Combine(definition.DefinitionDirectory ?? string.Empty, resource.Value)))
                    issues.Add(Error(definition, resource.Key, "Referenced resource file is missing: " + resource.Value));
            }
        }

        private void ValidateLauncher(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Launcher == null)
                return;

            if (!DataDrivenDefinitionRules.IsSafeExecutableRelativePath(definition.Launcher.DefaultExecutable))
                issues.Add(Error(definition, "launcher.defaultExecutable", "launcher.defaultExecutable is required and must be a safe game-relative .exe path."));
            else if (definition.GameExecutables != null && !definition.GameExecutables.Any(x => DataDrivenDefinitionRules.PathsEqual(x, definition.Launcher.DefaultExecutable)))
                issues.Add(Error(definition, "launcher.defaultExecutable", "launcher.defaultExecutable must also be listed in gameExecutables."));

            if (!definition.Launcher.AllowCustomCommand.HasValue)
                issues.Add(Error(definition, "launcher.allowCustomCommand", "launcher.allowCustomCommand is required."));

            ValidateOptionalIdentifier(definition, definition.Launcher.PlainCommandName, "launcher.plainCommandName", issues);
            ValidateOptionalIdentifier(definition, definition.Launcher.CustomCommandName, "launcher.customCommandName", issues);
            ValidateOptionalNonEmptyString(definition, definition.Launcher.PlainCommandText, "launcher.plainCommandText", issues);
            ValidateOptionalNonEmptyString(definition, definition.Launcher.CustomCommandText, "launcher.customCommandText", issues);
            ValidateOptionalNonEmptyString(definition, definition.Launcher.DefaultCommandText, "launcher.defaultCommandText", issues);
        }

        private void ValidateOptionalIdentifier(GameModeDefinition definition, string value, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (value != null && !DataDrivenDefinitionRules.IsIdentifier(value))
                issues.Add(Error(definition, path, "Value must be a non-empty stable identifier."));
        }

        private void ValidateTheme(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Theme == null)
                return;
            if (string.IsNullOrWhiteSpace(definition.Theme.PrimaryColor) || !Regex.IsMatch(definition.Theme.PrimaryColor, "^#[0-9A-Fa-f]{6}$"))
                issues.Add(Error(definition, "theme.primaryColor", "theme.primaryColor is required and must use #RRGGBB format."));
        }

        private void ValidateSetupAndSettings(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Setup != null && !definition.Setup.UseGenericSetup.HasValue)
                issues.Add(Error(definition, "setup.useGenericSetup", "setup.useGenericSetup is required when setup is present."));
            if (definition.Settings != null && !definition.Settings.UseGenericSettings.HasValue)
                issues.Add(Error(definition, "settings.useGenericSettings", "settings.useGenericSettings is required when settings is present."));
        }

        private void ValidateModInstall(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.ModInstall == null)
                return;

            if (string.IsNullOrWhiteSpace(definition.ModInstall.PathAdjustmentProfile) || !ValidPathAdjustmentProfiles.Contains(definition.ModInstall.PathAdjustmentProfile))
                issues.Add(Error(definition, "modInstall.pathAdjustmentProfile", "pathAdjustmentProfile must be one of: none, cyberpunk2077, fallout4, oblivionremastered, starfield, stardewvalley, subnautica, sims4, nomanssky."));
            if (!definition.ModInstall.SupportsGameRootInstall.HasValue)
                issues.Add(Error(definition, "modInstall.supportsGameRootInstall", "supportsGameRootInstall is required when modInstall is present."));

            ValidateOptionalPathTemplate(definition, definition.ModInstall.ManagedInstallationPath, "modInstall.managedInstallationPath", issues);
            if (DataDrivenDefinitionRules.ContainsPlaceholder(definition.ModInstall.ManagedInstallationPath, "{UserGameData}"))
                issues.Add(Error(definition, "modInstall.managedInstallationPath", "managedInstallationPath cannot use {UserGameData}."));
            ValidateExtensions(definition, definition.ModInstall.HardlinkRequiredExtensions, "modInstall.hardlinkRequiredExtensions", issues);
            ValidateExtensions(definition, definition.ModInstall.RealFileRequiredExtensions, "modInstall.realFileRequiredExtensions", issues);
        }

        private void ValidateGamebryo(GameModeDefinition definition, IList<GameModeDefinitionIssue> issues)
        {
            if (definition.Gamebryo == null)
                return;

            bool hasAny = definition.Gamebryo.ScriptExtenderExecutables != null ||
                          definition.Gamebryo.ScriptExtenderAutoLoadFiles != null ||
                          definition.Gamebryo.RequiresOptionalFilesCheckOnProfileSwitch.HasValue ||
                          definition.Gamebryo.OptionalFileNamePrefixes != null ||
                          !string.IsNullOrWhiteSpace(definition.Gamebryo.PostProfileSwitchToolPath) ||
                          !string.IsNullOrWhiteSpace(definition.Gamebryo.PostProfileSwitchToolMessage) ||
                          !string.IsNullOrWhiteSpace(definition.Gamebryo.UserGameDataPath) ||
                          !string.IsNullOrWhiteSpace(definition.Gamebryo.IniFilePath) ||
                          !string.IsNullOrWhiteSpace(definition.Gamebryo.RendererFilePath) ||
                          !string.IsNullOrWhiteSpace(definition.Gamebryo.PluginsFilePath) ||
                          (definition.Gamebryo.AdditionalSettingsFiles != null && definition.Gamebryo.AdditionalSettingsFiles.Count > 0);
            if (!hasAny)
                issues.Add(Error(definition, "gamebryo", "gamebryo must contain at least one setting."));

            ValidateExecutablePaths(definition, definition.Gamebryo.ScriptExtenderExecutables, "gamebryo.scriptExtenderExecutables", false, issues);
            ValidateRelativePaths(definition, definition.Gamebryo.ScriptExtenderAutoLoadFiles, "gamebryo.scriptExtenderAutoLoadFiles", issues);
            ValidateUniqueStrings(definition, definition.Gamebryo.OptionalFileNamePrefixes, "gamebryo.optionalFileNamePrefixes", issues, false);
            ValidateOptionalPathTemplate(definition, definition.Gamebryo.PostProfileSwitchToolPath, "gamebryo.postProfileSwitchToolPath", issues);

            bool hasPostSwitchPath = !string.IsNullOrWhiteSpace(definition.Gamebryo.PostProfileSwitchToolPath);
            bool hasPostSwitchMessage = !string.IsNullOrWhiteSpace(definition.Gamebryo.PostProfileSwitchToolMessage);
            if (hasPostSwitchPath != hasPostSwitchMessage)
            {
                issues.Add(Error(
                    definition,
                    "gamebryo",
                    "postProfileSwitchToolPath and postProfileSwitchToolMessage must be declared together."));
            }

            ValidateOptionalPathTemplate(definition, definition.Gamebryo.UserGameDataPath, "gamebryo.userGameDataPath", issues);
            if (DataDrivenDefinitionRules.ContainsPlaceholder(definition.Gamebryo.UserGameDataPath, "{UserGameData}"))
                issues.Add(Error(definition, "gamebryo.userGameDataPath", "userGameDataPath cannot reference {UserGameData} recursively."));
            ValidateOptionalPathTemplate(definition, definition.Gamebryo.IniFilePath, "gamebryo.iniFilePath", issues);
            ValidateOptionalPathTemplate(definition, definition.Gamebryo.RendererFilePath, "gamebryo.rendererFilePath", issues);
            ValidateOptionalPathTemplate(definition, definition.Gamebryo.PluginsFilePath, "gamebryo.pluginsFilePath", issues);

            if (definition.Gamebryo.AdditionalSettingsFiles != null)
            {
                if (definition.Gamebryo.AdditionalSettingsFiles.Count == 0)
                    issues.Add(Error(definition, "gamebryo.additionalSettingsFiles", "additionalSettingsFiles cannot be empty when present."));

                foreach (KeyValuePair<string, string> pair in definition.Gamebryo.AdditionalSettingsFiles)
                {
                    if (!DataDrivenDefinitionRules.IsIdentifier(pair.Key))
                        issues.Add(Error(definition, "gamebryo.additionalSettingsFiles", "Additional settings-file keys must be identifiers: " + pair.Key));
                    ValidateOptionalPathTemplate(definition, pair.Value, "gamebryo.additionalSettingsFiles." + pair.Key, issues);
                }
            }
        }

        private void ValidateExecutablePaths(GameModeDefinition definition, IEnumerable<string> values, string path, bool required, IList<GameModeDefinitionIssue> issues)
        {
            if (values == null)
            {
                if (required)
                    issues.Add(Error(definition, path, "At least one executable is required."));
                return;
            }

            string[] entries = values.ToArray();
            if (required && entries.Length == 0)
                issues.Add(Error(definition, path, "At least one executable is required."));

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entries.Length; i++)
            {
                if (!DataDrivenDefinitionRules.IsSafeExecutableRelativePath(entries[i]))
                    issues.Add(Error(definition, path + "[" + i + "]", "Executable must be a safe relative .exe path."));
                else if (!unique.Add(DataDrivenDefinitionRules.NormalizeRelativePath(entries[i])))
                    issues.Add(Error(definition, path + "[" + i + "]", "Duplicate executable path."));
            }
        }

        private void ValidateRelativePaths(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (values == null)
                return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (string value in values)
            {
                if (!DataDrivenDefinitionRules.IsSafeRelativePath(value, false))
                    issues.Add(Error(definition, path + "[" + i + "]", "Value must be a safe relative path."));
                else if (!unique.Add(DataDrivenDefinitionRules.NormalizeRelativePath(value)))
                    issues.Add(Error(definition, path + "[" + i + "]", "Duplicate path."));
                i++;
            }
        }

        private void ValidatePathTemplates(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (values == null)
                return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (string value in values)
            {
                if (!DataDrivenDefinitionRules.IsSafePathTemplate(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Value contains traversal, an unknown placeholder, or is empty."));
                else if (!unique.Add(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Duplicate path template."));
                i++;
            }
        }

        private void ValidateOptionalPathTemplate(GameModeDefinition definition, string value, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (value != null && !DataDrivenDefinitionRules.IsSafePathTemplate(value))
                issues.Add(Error(definition, path, "Value must be a non-empty path template without traversal or unknown placeholders."));
        }

        private void ValidateExtensions(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (values == null)
                return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (string value in values)
            {
                if (!DataDrivenDefinitionRules.IsFileExtension(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Invalid file extension: " + value));
                else if (!unique.Add(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Duplicate extension: " + value));
                i++;
            }
        }

        private void ValidateFileNames(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (values == null)
                return;
            ValidateUniqueFileNames(definition, values, path, issues);
        }

        private void ValidateUniqueFileNames(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues)
        {
            if (values == null)
                return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (string value in values)
            {
                if (!DataDrivenDefinitionRules.IsSafeFileName(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Value must be a safe file name."));
                else if (!unique.Add(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Duplicate file name: " + value));
                i++;
            }
        }

        private void ValidateIdentifiers(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues)
        {
            ValidateUniqueStrings(definition, values, path, issues, true);
        }

        private void ValidateUniqueStrings(GameModeDefinition definition, IEnumerable<string> values, string path, IList<GameModeDefinitionIssue> issues, bool identifiers)
        {
            if (values == null)
                return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            foreach (string value in values)
            {
                bool valid = identifiers ? DataDrivenDefinitionRules.IsFormatIdentifier(value) : !string.IsNullOrWhiteSpace(value);
                if (!valid)
                    issues.Add(Error(definition, path + "[" + i + "]", "Value is empty or invalid: " + value));
                else if (!unique.Add(value))
                    issues.Add(Error(definition, path + "[" + i + "]", "Duplicate value: " + value));
                i++;
            }
        }

        private GameModeDefinitionIssue Error(GameModeDefinition definition, string propertyPath, string message)
        {
            return Error(definition == null ? null : definition.DefinitionPath, definition == null ? null : definition.ModeId, propertyPath, message);
        }

        internal static GameModeDefinitionIssue Error(string filePath, string gameModeId, string propertyPath, string message)
        {
            return new GameModeDefinitionIssue
            {
                FilePath = filePath,
                GameModeId = gameModeId,
                PropertyPath = propertyPath,
                Severity = GameModeDefinitionIssueSeverity.Error,
                Message = message
            };
        }
    }

    public class GameModeSupportedToolsDefinitionValidator
    {
        public IEnumerable<GameModeDefinitionIssue> Validate(GameModeSupportedToolsDefinition definition, GameModeDefinition gameModeDefinition)
        {
            var issues = new List<GameModeDefinitionIssue>();
            string filePath = definition == null ? null : definition.DefinitionPath;
            string gameModeId = gameModeDefinition == null ? null : gameModeDefinition.ModeId;
            bool supportsUserGameData = gameModeDefinition != null && string.Equals(gameModeDefinition.BehaviorProfile, "gamebryo", StringComparison.Ordinal);
            if (definition == null)
            {
                issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, null, "Supported-tools definition could not be deserialized."));
                return issues;
            }

            if (definition.SchemaVersion != 1)
                issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, "schemaVersion", "Unsupported supported-tools schemaVersion. Expected 1."));
            if (definition.SupportedTools == null || definition.SupportedTools.Count == 0)
            {
                issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, "supportedTools", "supportedTools must contain at least one tool."));
                return issues;
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < definition.SupportedTools.Count; i++)
            {
                GameModeToolDefinition tool = definition.SupportedTools[i];
                string path = "supportedTools[" + i + "]";
                if (tool == null)
                {
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path, "Tool entry cannot be null."));
                    continue;
                }

                if (!DataDrivenDefinitionRules.IsIdentifier(tool.Id))
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".id", "Tool id is required and must be an identifier."));
                else if (!ids.Add(tool.Id))
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".id", "Duplicate tool id: " + tool.Id));

                if (string.IsNullOrWhiteSpace(tool.Name))
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".name", "Tool name is required."));
                if (!string.IsNullOrWhiteSpace(tool.SettingsKey) && !DataDrivenDefinitionRules.IsIdentifier(tool.SettingsKey))
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".settingsKey", "settingsKey must be an identifier."));

                bool hasSingle = !string.IsNullOrWhiteSpace(tool.ExecutableName);
                bool hasMany = tool.ExecutableNames != null && tool.ExecutableNames.Length > 0;
                if (tool.ExecutableName != null && !hasSingle)
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".executableName", "executableName cannot be empty when present."));
                if (tool.ExecutableNames != null && tool.ExecutableNames.Length == 0)
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".executableNames", "executableNames cannot be empty when present."));
                if (hasSingle == hasMany)
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path, "Specify exactly one of executableName or executableNames."));

                var executableNames = new List<string>();
                if (hasSingle)
                    executableNames.Add(tool.ExecutableName);
                if (hasMany)
                    executableNames.AddRange(tool.ExecutableNames);
                var uniqueExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < executableNames.Count; j++)
                {
                    string executable = executableNames[j];
                    string executablePath = hasSingle ? path + ".executableName" : path + ".executableNames[" + j + "]";
                    if (!DataDrivenDefinitionRules.IsSafeToolExecutableName(executable))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, executablePath, "Only safe direct .exe file names are supported: " + executable));
                    else if (!uniqueExecutables.Add(executable))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, executablePath, "Duplicate executable alternative: " + executable));
                }

                if (tool.ExecutableDirectory != null)
                {
                    if (!DataDrivenDefinitionRules.IsSafePathTemplate(tool.ExecutableDirectory))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".executableDirectory", "executableDirectory must be a non-empty path template without traversal or unknown placeholders."));
                    else if (!supportsUserGameData && DataDrivenDefinitionRules.ContainsPlaceholder(tool.ExecutableDirectory, "{UserGameData}"))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".executableDirectory", "{UserGameData} is available only to gamebryo Game Modes."));
                }

                if (tool.ArgumentTokens != null)
                {
                    for (int j = 0; j < tool.ArgumentTokens.Length; j++)
                    {
                        string token = tool.ArgumentTokens[j];
                        if (string.IsNullOrWhiteSpace(token))
                            issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".argumentTokens[" + j + "]", "Argument tokens cannot be empty."));
                        else if (DataDrivenDefinitionRules.HasParentTraversal(token) || DataDrivenDefinitionRules.ContainsUnknownPlaceholder(token))
                            issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".argumentTokens[" + j + "]", "Argument token contains traversal or an unknown placeholder."));
                        else if (!supportsUserGameData && DataDrivenDefinitionRules.ContainsPlaceholder(token, "{UserGameData}"))
                            issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".argumentTokens[" + j + "]", "{UserGameData} is available only to gamebryo Game Modes."));
                    }
                }

                ValidateDiscoveryRules(tool, filePath, gameModeId, path, supportsUserGameData, issues);
            }

            return issues;
        }

        private void ValidateDiscoveryRules(GameModeToolDefinition tool, string filePath, string gameModeId, string toolPath, bool supportsUserGameData, IList<GameModeDefinitionIssue> issues)
        {
            if (tool.DiscoveryRules == null)
                return;
            if (tool.DiscoveryRules.Count == 0)
            {
                issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, toolPath + ".discoveryRules", "discoveryRules cannot be empty when present."));
                return;
            }

            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tool.DiscoveryRules.Count; i++)
            {
                GameModeToolDiscoveryRuleDefinition rule = tool.DiscoveryRules[i];
                string path = toolPath + ".discoveryRules[" + i + "]";
                if (rule == null)
                {
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path, "Discovery rule cannot be null."));
                    continue;
                }

                string source = rule.Source ?? string.Empty;
                if (source.Equals("Registry", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(rule.RegistryKey))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".registryKey", "Registry discovery requires a non-empty registryKey."));
                    if (rule.Path != null)
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".path", "Registry discovery does not accept path."));
                    if (rule.PathSuffix != null && !DataDrivenDefinitionRules.IsSafeRelativePath(rule.PathSuffix, false))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".pathSuffix", "Registry pathSuffix must be a safe non-empty relative path."));
                }
                else if (source.Equals("GameRelative", StringComparison.Ordinal))
                {
                    if (!DataDrivenDefinitionRules.IsSafeRelativePath(rule.Path, false))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".path", "GameRelative discovery requires a safe relative path."));
                    if (rule.RegistryKey != null || rule.RegistryValueName != null || rule.PathSuffix != null)
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path, "GameRelative discovery accepts only source and path."));
                }
                else if (source.Equals("Path", StringComparison.Ordinal))
                {
                    if (!DataDrivenDefinitionRules.IsSafePathTemplate(rule.Path))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".path", "Path discovery requires a safe non-empty path template."));
                    else if (!supportsUserGameData && DataDrivenDefinitionRules.ContainsPlaceholder(rule.Path, "{UserGameData}"))
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".path", "{UserGameData} is available only to gamebryo Game Modes."));
                    if (rule.RegistryKey != null || rule.RegistryValueName != null || rule.PathSuffix != null)
                        issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path, "Path discovery accepts only source and path."));
                }
                else
                {
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path + ".source", "Discovery source must be Registry, GameRelative or Path."));
                }

                string identity = source + "|" + (rule.Path ?? string.Empty) + "|" + (rule.RegistryKey ?? string.Empty) + "|" +
                                  (rule.RegistryValueName ?? string.Empty) + "|" + (rule.PathSuffix ?? string.Empty);
                if (!identities.Add(identity))
                    issues.Add(GameModeDefinitionValidator.Error(filePath, gameModeId, path, "Duplicate discovery rule."));
            }
        }
    }

    internal static class DataDrivenDefinitionRules
    {
        private static readonly Regex IdentifierRegex = new Regex("^[A-Za-z][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex FormatIdentifierRegex = new Regex("^[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex FileExtensionRegex = new Regex("^\\.[A-Za-z0-9][A-Za-z0-9._+-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex PlaceholderRegex = new Regex("\\{[^{}]+\\}", RegexOptions.CultureInvariant);
        private static readonly Regex EnumFlagsRegex = new Regex("^[A-Za-z][A-Za-z0-9]*(?:\\s*\\|\\s*[A-Za-z][A-Za-z0-9]*)*$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> KnownPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "{GamePath}",
            "{InstallationPath}",
            "{ExecutablePath}",
            "{ModeId}",
            "{Documents}",
            "{PersonalData}",
            "{LocalApplicationData}",
            "{ProgramFiles}",
            "{ProgramFilesX86}",
            "{UserGameData}"
        };

        private static readonly HashSet<string> BlockedToolExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cmd.exe",
            "powershell.exe",
            "pwsh.exe",
            "wscript.exe",
            "cscript.exe",
            "mshta.exe",
            "rundll32.exe",
            "regsvr32.exe"
        };

        public static bool IsIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && IdentifierRegex.IsMatch(value);
        }

        public static bool IsFormatIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && FormatIdentifierRegex.IsMatch(value);
        }

        public static bool IsSafeFileName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
                   value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
                   !value.Any(char.IsControl) &&
                   !Path.IsPathRooted(value) &&
                   value != "." && value != ".." &&
                   !value.EndsWith(" ", StringComparison.Ordinal) &&
                   !value.EndsWith(".", StringComparison.Ordinal);
        }

        public static bool IsSafeToolExecutableName(string value)
        {
            return IsSafeFileName(value) &&
                   string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase) &&
                   !BlockedToolExecutables.Contains(value);
        }

        public static bool IsSafeExecutableRelativePath(string value)
        {
            return IsSafeRelativePath(value, false) && string.Equals(Path.GetExtension(value), ".exe", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSafeRelativePath(string value, bool allowEmpty)
        {
            if (string.IsNullOrEmpty(value))
                return allowEmpty;
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || HasParentTraversal(value) || ContainsUnknownPlaceholder(value))
                return false;
            if (value.Any(char.IsControl) || value.IndexOfAny(new[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0)
                return false;
            return true;
        }

        public static bool IsSafePathTemplate(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl) && !HasParentTraversal(value) && !ContainsUnknownPlaceholder(value);
        }

        public static bool HasParentTraversal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            return value.Replace('/', '\\').Split('\\').Any(x => string.Equals(x.Trim(), "..", StringComparison.Ordinal));
        }

        public static bool ContainsUnknownPlaceholder(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            foreach (Match match in PlaceholderRegex.Matches(value))
            {
                if (!KnownPlaceholders.Contains(match.Value))
                    return true;
            }
            return value.IndexOf('{') >= 0 || value.IndexOf('}') >= 0
                ? PlaceholderRegex.Replace(value, string.Empty).IndexOfAny(new[] { '{', '}' }) >= 0
                : false;
        }

        public static bool ContainsPlaceholder(string value, string placeholder)
        {
            return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeholder) &&
                   value.IndexOf(placeholder, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsFileExtension(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && FileExtensionRegex.IsMatch(value);
        }

        public static string NormalizeRelativePath(string value)
        {
            return string.IsNullOrEmpty(value) ? value : value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
        }

        public static bool PathsEqual(string left, string right)
        {
            return string.Equals(NormalizeRelativePath(left), NormalizeRelativePath(right), StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseUInt32(object value, out uint result)
        {
            result = 0;
            if (value == null)
                return false;

            string text = value as string;
            if (text != null)
            {
                text = text.Trim();
                if (!text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return false;
                return text.Length > 2 && text.Length <= 10 &&
                       uint.TryParse(text.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result) &&
                       result != 0;
            }

            try
            {
                TypeCode typeCode = Type.GetTypeCode(value.GetType());
                switch (typeCode)
                {
                    case TypeCode.Byte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                        ulong unsigned = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                        if (unsigned == 0 || unsigned > uint.MaxValue)
                            return false;
                        result = (uint)unsigned;
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryParseHeaderFlags(string value, out PluginHeaderFlags flags)
        {
            flags = PluginHeaderFlags.None;
            if (string.IsNullOrWhiteSpace(value) || !EnumFlagsRegex.IsMatch(value) || IsNumeric(value))
                return false;
            if (!Enum.TryParse(value.Replace("|", ","), true, out flags))
                return false;

            ulong allowed = 0;
            foreach (PluginHeaderFlags defined in Enum.GetValues(typeof(PluginHeaderFlags)))
                allowed |= Convert.ToUInt64(defined, CultureInfo.InvariantCulture);
            ulong parsed = Convert.ToUInt64(flags, CultureInfo.InvariantCulture);
            return (parsed & ~allowed) == 0;
        }

        public static bool TryParseAddressClass(string value, bool allowNone, out PluginAddressClass addressClass)
        {
            addressClass = PluginAddressClass.None;
            if (string.IsNullOrWhiteSpace(value) || IsNumeric(value) || !Enum.TryParse(value, false, out addressClass) || !Enum.IsDefined(typeof(PluginAddressClass), addressClass))
                return false;
            return allowNone || addressClass != PluginAddressClass.None;
        }

        public static bool TryParseHeaderFlagSource(string value, out PluginHeaderFlagSource source)
        {
            source = PluginHeaderFlagSource.RecordFlags1;
            return !string.IsNullOrWhiteSpace(value) && !IsNumeric(value) && Enum.TryParse(value, false, out source) && Enum.IsDefined(typeof(PluginHeaderFlagSource), source);
        }

        public static string NormalizeDisplayFormat(string displayFormat)
        {
            if (string.IsNullOrWhiteSpace(displayFormat))
                return "{0:X2}";
            return displayFormat.IndexOf("{0", StringComparison.Ordinal) >= 0 ? displayFormat : "{0:" + displayFormat + "}";
        }

        public static bool IsUsableDisplayFormat(string displayFormat)
        {
            try
            {
                string normalized = NormalizeDisplayFormat(displayFormat);
                string value = string.Format(CultureInfo.InvariantCulture, normalized, 0);
                return !string.IsNullOrEmpty(value);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool IsNumeric(string value)
        {
            int ignored;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ignored);
        }
    }
}
