using System.Collections.Generic;

namespace Nexus.Client.Games.DataDriven
{
    public class GameModeDefinition
    {
        public int SchemaVersion { get; set; }
        public string ModeId { get; set; }
        public string Name { get; set; }
        public string BehaviorProfile { get; set; }
        public string InstallerProfile { get; set; }
        public bool LegacyFallback { get; set; }
        public string[] GameExecutables { get; set; }
        public string[] StopFolders { get; set; }
        public string[] PluginExtensions { get; set; }
        public string[] OrderedCriticalPluginNames { get; set; }
        public string[] OrderedOfficialPluginNames { get; set; }
        public string[] OrderedOfficialUnmanagedPluginNames { get; set; }
        public string RequiredToolName { get; set; }
        public string[] OrderedRequiredToolFileNames { get; set; }
        public string RequiredToolErrorMessage { get; set; }
        public string CriticalFilesErrorMessage { get; set; }
        public string[] SupportedFormats { get; set; }
        public List<GameModeToolDefinition> SupportedTools { get; set; } = new List<GameModeToolDefinition>();
        public GameModeDiscoveryDefinition Discovery { get; set; }
        public GameModePluginDefinition Plugin { get; set; }
        public GameModeLauncherDefinition Launcher { get; set; }
        public GameModeResourcesDefinition Resources { get; set; }
        public GameModeThemeDefinition Theme { get; set; }
        public GameModeSetupDefinition Setup { get; set; }
        public GameModeSettingsDefinition Settings { get; set; }
        public GameModeModInstallDefinition ModInstall { get; set; }
        public GameModeGamebryoDefinition Gamebryo { get; set; }
        public List<string> CompatibilityNotes { get; set; } = new List<string>();
        public string DefinitionPath { get; set; }
        public string DefinitionDirectory { get; set; }
    }

    public class GameModeDiscoveryDefinition
    {
        public string SteamAppId { get; set; }
        public string SteamInstallFolderName { get; set; }
        public string SteamExecutableName { get; set; }
        public string GogRegistryKey { get; set; }
        public string GogPathValueName { get; set; }
        public List<GameModeStoreDiscoveryDefinition> Stores { get; set; } = new List<GameModeStoreDiscoveryDefinition>();
    }

    public class GameModeStoreDiscoveryDefinition
    {
        public string Store { get; set; }
        public string Id { get; set; }
        public string InstallFolderName { get; set; }
        public string ExecutableName { get; set; }
        public string RegistryKey { get; set; }
        public string RegistryValueName { get; set; }
        public string PathSuffix { get; set; }
    }

    public class GameModePluginDefinition
    {
        public bool UsesPlugins { get; set; }
        public string PluginDirectorySuffix { get; set; }
        public bool SupportsPluginAutoSorting { get; set; }
        public int MaxAllowedActivePluginsCount { get; set; }
        public string[] OfficialUnmanagedPluginListFiles { get; set; }
    }

    public class GameModeLauncherDefinition
    {
        public string DefaultExecutable { get; set; }
        public string PlainCommandName { get; set; }
        public string PlainCommandText { get; set; }
        public string CustomCommandName { get; set; }
        public string CustomCommandText { get; set; }
        public string DefaultCommandText { get; set; }
        public bool AllowCustomCommand { get; set; }
    }

    public class GameModeResourcesDefinition
    {
        public string IconPath { get; set; }
        public string CategoriesPath { get; set; }
        public string BaseFilesPath { get; set; }
    }

    public class GameModeThemeDefinition
    {
        public string PrimaryColor { get; set; }
    }

    public class GameModeSetupDefinition
    {
        public bool UseGenericSetup { get; set; }
    }

    public class GameModeSettingsDefinition
    {
        public bool UseGenericSettings { get; set; }
        public bool AllowCustomLaunchCommand { get; set; }
    }

    public class GameModeToolDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SettingsKey { get; set; }
        public string ExecutablePath { get; set; }
        public string ExecutableName { get; set; }
        public string[] ExecutableNames { get; set; }
        public string Arguments { get; set; }
        public string[] ArgumentTokens { get; set; }
        public List<GameModeToolDiscoveryRuleDefinition> DiscoveryRules { get; set; } = new List<GameModeToolDiscoveryRuleDefinition>();
    }

    public class GameModeSupportedToolsDefinition
    {
        public int SchemaVersion { get; set; }
        public List<GameModeToolDefinition> SupportedTools { get; set; } = new List<GameModeToolDefinition>();
    }

    public class GameModeToolDiscoveryRuleDefinition
    {
        public string Source { get; set; }
        public string Path { get; set; }
        public string RegistryKey { get; set; }
        public string RegistryValueName { get; set; }
        public string PathSuffix { get; set; }
    }
    public class GameModeGamebryoDefinition
    {
        public string[] ScriptExtenderExecutables { get; set; }
        public string UserGameDataPath { get; set; }
        public string IniFilePath { get; set; }
        public string RendererFilePath { get; set; }
        public string PluginsFilePath { get; set; }
        public Dictionary<string, string> AdditionalSettingsFiles { get; set; } = new Dictionary<string, string>();
    }
    public class GameModeModInstallDefinition
    {
        public string PathAdjustmentProfile { get; set; }
        public string ManagedInstallationPath { get; set; }
        public string PluginDirectoryPath { get; set; }
        public string[] HardlinkRequiredExtensions { get; set; }
        public string[] RealFileRequiredExtensions { get; set; }
        public bool SupportsGameRootInstall { get; set; }
    }
}



