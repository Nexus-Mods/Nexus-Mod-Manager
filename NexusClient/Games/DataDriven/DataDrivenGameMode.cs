using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using Nexus.Client.Settings.UI;
using Nexus.Client.Updating;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGameMode : GameModeBase
    {
        [ThreadStatic]
        private static GameModeDefinition _pendingDefinition;

        private readonly GameModeDefinition _definition;
        private DataDrivenGameModeDescriptor _descriptor;
        private IGameLauncher _gameLauncher;
        private IToolLauncher _toolLauncher;
        private string _categories;
        private string _baseFiles;

        public DataDrivenGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition)
            : this(environmentInfo, fileUtility, PushDefinition(definition), true)
        {
        }

        private DataDrivenGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition, bool definitionIsPending)
            : base(environmentInfo)
        {
            _definition = definition;
            _pendingDefinition = null;
            SettingsGroupViews = BuildSettingsGroupViews(environmentInfo);
        }

        private static GameModeDefinition PushDefinition(GameModeDefinition definition)
        {
            _pendingDefinition = definition;
            return definition;
        }

        public override Version GameVersion
        {
            get
            {
                foreach (string executable in GameExecutables ?? new string[0])
                {
                    string fullPath = Path.Combine(GameModeEnvironmentInfo.InstallationPath ?? string.Empty, executable);
                    if (File.Exists(fullPath))
                    {
                        string version = FileVersionInfo.GetVersionInfo(fullPath).ProductVersion;
                        if (!string.IsNullOrWhiteSpace(version) && Version.TryParse(version.Replace(", ", "."), out var parsed))
                            return parsed;
                    }
                }
                return null;
            }
        }

        public override IEnumerable<string> WritablePaths => null;
        public override List<string> SupportedFormats => _definition.SupportedFormats?.ToList() ?? new List<string> { "fomod" };
        public override IGameLauncher GameLauncher => _gameLauncher ?? (_gameLauncher = new DataDrivenGameLauncher(this, EnvironmentInfo, _definition));
        public override IToolLauncher GameToolLauncher => _toolLauncher ?? (_toolLauncher = new DataDrivenToolLauncher(this, EnvironmentInfo, _definition));
        public override bool UsesPlugins => _definition.Plugin != null && _definition.Plugin.UsesPlugins;
        public override bool SupportsPluginAutoSorting => _definition.Plugin != null && _definition.Plugin.SupportsPluginAutoSorting;
        public override int MaxAllowedActivePluginsCount => _definition.Plugin?.MaxAllowedActivePluginsCount ?? 0;
        public override PluginManagementPolicy PluginManagementPolicy => DataDrivenPluginPolicyBuilder.Build(_definition, PluginExtensions, OrderedCriticalPluginNames, OrderedOfficialPluginNames, OrderedOfficialUnmanagedPluginNames, MaxAllowedActivePluginsCount);
        public override bool SupportsGameRootModInstall => _definition.ModInstall != null && _definition.ModInstall.SupportsGameRootInstall;

        public override string GameDefaultCategories => _categories ?? (_categories = ReadResourceText(_definition.Resources?.CategoriesPath));
        public override string BaseGameFiles => _baseFiles ?? (_baseFiles = ReadResourceText(_definition.Resources?.BaseFilesPath));

        public override IPluginFactory GetPluginFactory() => null;
        public override IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog) => null;
        public override IPluginDiscoverer GetPluginDiscoverer() => null;
        public override IPluginOrderLogSerializer GetPluginOrderLogSerializer() => null;
        public override IPluginOrderValidator GetPluginOrderValidator() => null;
        public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate) => null;
        public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate) => null;
        public override IEnumerable<IUpdater> GetUpdaters() => null;

        public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
        {
            if (string.Equals(_definition.ModInstall?.PathAdjustmentProfile, "cyberpunk2077", StringComparison.OrdinalIgnoreCase))
                return CyberpunkPathAdjuster.Adjust(p_strPath, GameVersion);
            return base.GetModFormatAdjustedPath(p_mftModFormat, p_strPath, p_modMod, p_booIgnoreIfPresent);
        }

        public override bool HardlinkRequiredFilesType(string p_strFileName)
        {
            return MatchesExtension(_definition.ModInstall?.HardlinkRequiredExtensions, Path.GetExtension(p_strFileName)) || base.HardlinkRequiredFilesType(p_strFileName);
        }

        public override bool RealFileRequired(string fileExtension)
        {
            return MatchesExtension(_definition.ModInstall?.RealFileRequiredExtensions, fileExtension) || base.RealFileRequired(fileExtension);
        }

        protected override IGameModeDescriptor CreateGameModeDescriptor()
        {
            var definition = _pendingDefinition;
            if (definition == null)
                throw new InvalidOperationException("No data-driven GameMode definition was provided.");
            return _descriptor = new DataDrivenGameModeDescriptor(EnvironmentInfo, definition);
        }

        private IEnumerable<ISettingsGroupView> BuildSettingsGroupViews(IEnvironmentInfo environmentInfo)
        {
            if (_definition.Settings == null || !_definition.Settings.UseGenericSettings)
                return new List<ISettingsGroupView>();
            var group = new DataDrivenGameModeSettingsGroup(environmentInfo, this, _definition);
            return new List<ISettingsGroupView> { new DataDrivenGameModeSettingsPage(group) };
        }

        private string ReadResourceText(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;
            string path = Path.Combine(_definition.DefinitionDirectory ?? string.Empty, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static bool MatchesExtension(IEnumerable<string> extensions, string extension)
        {
            return extensions != null && !string.IsNullOrWhiteSpace(extension) && extensions.Any(x => string.Equals(x, extension, StringComparison.InvariantCultureIgnoreCase));
        }

        protected override void Dispose(bool p_booDisposing)
        {
        }
    }


    internal static class DataDrivenPluginPolicyBuilder
    {
        public static PluginManagementPolicy Build(GameModeDefinition gameModeDefinition, IEnumerable<string> pluginExtensions, IEnumerable<string> criticalPlugins, IEnumerable<string> officialPlugins, IEnumerable<string> officialUnmanagedPlugins, int fullSlotLimit)
        {
            PluginManagementPolicy policy = Nexus.Client.PluginManagement.PluginManagementPolicy.CreateDefault(pluginExtensions, criticalPlugins, officialPlugins, officialUnmanagedPlugins, fullSlotLimit);
            GameModePluginPolicyDefinition definition = gameModeDefinition == null || gameModeDefinition.Plugin == null ? null : gameModeDefinition.Plugin.Policy;
            if (definition == null)
                return policy;

            if (definition.SchemaVersion <= 0)
                throw new InvalidOperationException("plugin.policy.schemaVersion must be specified for data-driven plugin policies.");

            if (!string.IsNullOrWhiteSpace(definition.ParserStrategy))
                policy.ParserStrategy = definition.ParserStrategy;
            if (!string.IsNullOrWhiteSpace(definition.PersistenceStrategy))
                policy.PersistenceStrategy = definition.PersistenceStrategy;
            if (definition.MasterPluginsMustLoadBeforeNonMasters.HasValue)
                policy.MasterPluginsMustLoadBeforeNonMasters = definition.MasterPluginsMustLoadBeforeNonMasters.Value;
            if (definition.ValidateDependencies.HasValue)
                policy.ValidateDependencies = definition.ValidateDependencies.Value;

            policy.PluginsFilePath = definition.PluginsFilePath ?? policy.PluginsFilePath;
            policy.LoadOrderFilePath = definition.LoadOrderFilePath ?? policy.LoadOrderFilePath;
            policy.EncodingName = definition.EncodingName ?? policy.EncodingName;
            policy.ActiveMarker = definition.ActiveMarker ?? policy.ActiveMarker;
            policy.InactiveMarker = definition.InactiveMarker ?? policy.InactiveMarker;
            policy.AppDataGameFolderName = definition.AppDataGameFolderName ?? policy.AppDataGameFolderName;
            policy.UseTimestampOrder = definition.UseTimestampOrder ?? policy.UseTimestampOrder;
            policy.IgnoreOfficialPlugins = definition.IgnoreOfficialPlugins ?? policy.IgnoreOfficialPlugins;
            policy.ForcedReadOnly = definition.ForcedReadOnly ?? policy.ForcedReadOnly;
            policy.SingleFileManagement = definition.SingleFileManagement ?? policy.SingleFileManagement;
            policy.OfficialPluginsAreImplicitlyActive = definition.OfficialPluginsAreImplicitlyActive ?? policy.OfficialPluginsAreImplicitlyActive;
            policy.LoadOrderInPluginDirectory = definition.LoadOrderInPluginDirectory ?? policy.LoadOrderInPluginDirectory;
            policy.ShowStarfieldCustomPluginsHeader = definition.ShowStarfieldCustomPluginsHeader ?? policy.ShowStarfieldCustomPluginsHeader;

            foreach (GameModePluginExtensionPolicyDefinition extension in definition.Extensions ?? new List<GameModePluginExtensionPolicyDefinition>())
                policy.AddExtension(new PluginExtensionPolicy(extension.Extension, ParseHeaderFlags(extension.ForcedFlags), ParseAddressClass(extension.ForcedAddressClass)));

            foreach (GameModePluginHeaderFlagMappingDefinition mapping in definition.HeaderFlagMappings ?? new List<GameModePluginHeaderFlagMappingDefinition>())
                policy.AddHeaderFlagMapping(new PluginHeaderFlagMapping(ParseHeaderFlagSource(mapping.Source), ParseUInt32(mapping.Mask), ParseHeaderFlags(mapping.Flags)));

            foreach (GameModePluginAddressSpaceDefinition addressSpace in definition.AddressSpaces ?? new List<GameModePluginAddressSpaceDefinition>())
                policy.AddAddressSpace(new PluginAddressSpacePolicy(ParseAddressClass(addressSpace.AddressClass), addressSpace.FirstIndex, addressSpace.MaxCount, addressSpace.DisplayFormat));

            foreach (string plugin in definition.OfficialPlugins ?? new string[0])
                policy.AddOfficialPlugin(plugin);
            foreach (string plugin in definition.CriticalPlugins ?? new string[0])
                policy.AddCriticalPlugin(plugin);
            foreach (string plugin in definition.FixedOrderPlugins ?? new string[0])
                policy.AddFixedOrderPlugin(plugin);
            foreach (string plugin in definition.ForcedActivePlugins ?? new string[0])
                policy.AddForcedActivePlugin(plugin);
            foreach (string plugin in definition.BlueprintPlugins ?? new string[0])
                policy.AddBlueprintPlugin(plugin);
            foreach (string prefix in definition.BlueprintPluginPrefixes ?? new string[0])
                policy.AddBlueprintPluginPrefix(prefix);

            return policy;
        }

        private static PluginHeaderFlags ParseHeaderFlags(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return PluginHeaderFlags.None;

            PluginHeaderFlags flags;
            return Enum.TryParse(value.Replace("|", ","), true, out flags) ? flags : PluginHeaderFlags.None;
        }

        private static PluginAddressClass ParseAddressClass(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return PluginAddressClass.None;

            PluginAddressClass addressClass;
            return Enum.TryParse(value, true, out addressClass) ? addressClass : PluginAddressClass.None;
        }

        private static PluginHeaderFlagSource ParseHeaderFlagSource(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return PluginHeaderFlagSource.RecordFlags1;

            PluginHeaderFlagSource source;
            return Enum.TryParse(value, true, out source) ? source : PluginHeaderFlagSource.RecordFlags1;
        }

        private static uint ParseUInt32(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToUInt32(value.Substring(2), 16);

            return Convert.ToUInt32(value);
        }
    }
    internal static class Sims4PathAdjuster
    {
        private static readonly string[] TrayExtensions = { ".householdbinary", ".trayitem", ".sgi", ".hhi", ".blueprint", ".bpi" };
        private static readonly string[] SaveExtensions = { ".save" };
        private static readonly string[] UnusedExtensions = { ".txt", ".png", ".jpg", ".pdf", ".doc", ".docx" };

        public static string Adjust(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path;
            string extension = Path.GetExtension(result);
            if (UnusedExtensions.Contains(extension, StringComparer.InvariantCultureIgnoreCase))
                return string.Empty;

            if (!result.StartsWith("Tray", StringComparison.InvariantCultureIgnoreCase) && !result.StartsWith("Mods", StringComparison.InvariantCultureIgnoreCase) && !result.StartsWith("saves", StringComparison.InvariantCultureIgnoreCase))
            {
                result = result.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                int subCount = result.Count(c => c == Path.DirectorySeparatorChar);
                if (subCount >= 2)
                    result = result.Substring(result.IndexOf(Path.DirectorySeparatorChar) + 1);

                if (TrayExtensions.Contains(extension, StringComparer.InvariantCultureIgnoreCase))
                    result = Path.Combine("Tray", result);
                else if (SaveExtensions.Contains(extension, StringComparer.InvariantCultureIgnoreCase))
                    result = Path.Combine("saves", result);
                else
                    result = Path.Combine("Mods", result);
            }

            return result;
        }
    }

    internal static class NoMansSkyPathAdjuster
    {
        private static readonly string[] SpecialFiles = { "opengl32.dll", "NMSE_steam.dll", "NMSE_Core_1_0.dll" };

        public static string Adjust(string path, IMod mod, string installationPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path;
            string extension = Path.GetExtension(result);
            if (extension.Equals(".pak", StringComparison.InvariantCultureIgnoreCase))
                result = GetPakInstallPath(result);
            else if (SpecialFiles.Any(s => Path.GetFileName(result).Equals(s, StringComparison.InvariantCultureIgnoreCase)) || extension.Equals(".exe", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!result.StartsWith("Binaries", StringComparison.InvariantCultureIgnoreCase))
                    result = Path.Combine("Binaries", result);
            }
            else if (mod != null && extension.Equals(".dll", StringComparison.InvariantCultureIgnoreCase))
            {
                if (!result.StartsWith("Binaries", StringComparison.InvariantCultureIgnoreCase))
                    result = result.StartsWith("NMSE", StringComparison.InvariantCultureIgnoreCase) ? Path.Combine("Binaries", result) : Path.Combine("Binaries", "NMSE", result);
            }

            if (mod != null && extension.Equals(".pak", StringComparison.InvariantCultureIgnoreCase))
                result = AdjustPakLoadOrder(result, mod, installationPath);

            return result;
        }

        private static string GetPakInstallPath(string path)
        {
            string result = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string modDirectory = Path.Combine("GAMEDATA", "MODS");
            return IsSamePathOrChild(result, modDirectory) ? result : Path.Combine(modDirectory, Path.GetFileName(result));
        }

        private static string AdjustPakLoadOrder(string path, IMod mod, string installationPath)
        {
            string fileName = Path.GetFileName(path);
            if (!fileName.Contains("_MOD"))
                fileName = fileName.Insert(0, "_MOD");

            string directory = Path.GetDirectoryName(path);
            string oldFile = mod.PlaceInModLoadOrder != -1 ? Path.Combine(directory, fileName.Insert(1, mod.PlaceInModLoadOrder.ToString())) : Path.Combine(directory, fileName);
            if (File.Exists(Path.Combine(installationPath ?? string.Empty, oldFile)))
                return oldFile;

            if (mod.NewPlaceInModLoadOrder != -1)
                fileName = fileName.Insert(1, mod.NewPlaceInModLoadOrder.ToString());
            return Path.Combine(directory, fileName);
        }

        private static bool IsSamePathOrChild(string path, string parentPath)
        {
            return path.Equals(parentPath, StringComparison.InvariantCultureIgnoreCase) || path.StartsWith(parentPath + Path.DirectorySeparatorChar, StringComparison.InvariantCultureIgnoreCase);
        }
    }

    internal static class CyberpunkPathAdjuster
    {
        public static string Adjust(string path, Version gameVersion)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path;
            bool oldGameVersion = gameVersion != null && gameVersion < new Version(1, 2);
            if (result.StartsWith("bin", StringComparison.InvariantCultureIgnoreCase) || result.StartsWith("x64", StringComparison.InvariantCultureIgnoreCase))
            {
                if (result.StartsWith("x64", StringComparison.InvariantCultureIgnoreCase))
                    result = Path.Combine("bin", result);
                return result;
            }

            if (!result.StartsWith("archive", StringComparison.InvariantCultureIgnoreCase) && !result.StartsWith("red4ext", StringComparison.InvariantCultureIgnoreCase) && !result.StartsWith("mods", StringComparison.InvariantCultureIgnoreCase))
            {
                string modPath = oldGameVersion ? "patch" : "mod";
                if (!oldGameVersion)
                    result = result.Replace("patch", "mod");

                if (result.StartsWith("pc", StringComparison.InvariantCultureIgnoreCase))
                    result = Path.Combine("archive", result);
                else if (result.StartsWith(modPath, StringComparison.InvariantCultureIgnoreCase))
                    result = Path.Combine("archive", "pc", result);

                if (!result.StartsWith("engine", StringComparison.InvariantCultureIgnoreCase) && !result.StartsWith("r6", StringComparison.InvariantCultureIgnoreCase))
                    result = Path.Combine("archive", "pc", modPath, result);
            }
            else if (!oldGameVersion)
                result = result.Replace("patch", "mod");

            return result;
        }
    }
}
