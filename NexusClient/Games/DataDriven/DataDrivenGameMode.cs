using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGameMode : GameModeBase
    {
        [ThreadStatic]
        private static GameModeDefinition _pendingDefinition;

        private readonly GameModeDefinition _definition;
        private IGameLauncher _gameLauncher;
        private IToolLauncher _toolLauncher;
        private ISupportedToolsLauncher _supportedToolsLauncher;
        private string _categories;
        private string _baseFiles;
        private bool _gameVersionResolved;
        private Version _gameVersion;

        public DataDrivenGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition)
            : this(environmentInfo, fileUtility, PushDefinition(definition), true)
        {
        }

        private DataDrivenGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition, bool definitionIsPending)
            : base(environmentInfo)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _pendingDefinition = null;
            SettingsGroupViews = DataDrivenGameModeHelpers.BuildSettingsGroupViews(environmentInfo, this, _definition);
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
                if (_gameVersionResolved)
                    return _gameVersion;

                Version resolvedVersion = null;
                string executableRoot = GameModeEnvironmentInfo.ExecutablePath ??
                                        GameModeEnvironmentInfo.InstallationPath ??
                                        string.Empty;
                foreach (string executable in GameExecutables ?? new string[0])
                {
                    string fullPath = Path.Combine(executableRoot, executable);
                    if (!File.Exists(fullPath))
                        continue;

                    string version = FileVersionInfo.GetVersionInfo(fullPath).ProductVersion;
                    Version parsed;
                    if (!string.IsNullOrWhiteSpace(version) && Version.TryParse(version.Replace(", ", "."), out parsed))
                    {
                        resolvedVersion = parsed;
                        break;
                    }
                }

                _gameVersion = resolvedVersion;
                _gameVersionResolved = true;
                return _gameVersion;
            }
        }

        public override IEnumerable<string> WritablePaths => Enumerable.Empty<string>();
        public override List<string> SupportedFormats => _definition.SupportedFormats == null ? new List<string> { "fomod" } : _definition.SupportedFormats.ToList();
        public override IGameLauncher GameLauncher => _gameLauncher ?? (_gameLauncher = new DataDrivenGameLauncher(this, EnvironmentInfo, _definition));
        public override IToolLauncher GameToolLauncher => _toolLauncher ?? (_toolLauncher = new DataDrivenToolLauncher(this, EnvironmentInfo, _definition));
        public override ISupportedToolsLauncher SupportedToolsLauncher => _supportedToolsLauncher ?? (_supportedToolsLauncher = new DataDrivenSupportedToolsLauncher(this, EnvironmentInfo, _definition));
        public override bool UsesPlugins => false;
        public override bool UsesModLoadOrder => IsNoMansSkyProfile;
        public override bool RequiresSpecialFileInstallation => IsNoMansSkyProfile;
        public override bool SupportsPluginAutoSorting => false;
        public override int MaxAllowedActivePluginsCount => 0;
        public override PluginManagementPolicy PluginManagementPolicy => DataDrivenPluginPolicyBuilder.Build(_definition, Enumerable.Empty<string>(), Enumerable.Empty<string>(), Enumerable.Empty<string>(), Enumerable.Empty<string>(), 0, CreatePathContext());
        public override bool SupportsGameRootModInstall => _definition.ModInstall != null && _definition.ModInstall.SupportsGameRootInstall == true;
        public override string GameDefaultCategories => _categories ?? (_categories = DataDrivenGameModeHelpers.ReadResourceText(_definition, _definition.Resources == null ? null : _definition.Resources.CategoriesPath));
        public override string BaseGameFiles => _baseFiles ?? (_baseFiles = DataDrivenGameModeHelpers.ReadResourceText(_definition, _definition.Resources == null ? null : _definition.Resources.BaseFilesPath));

        public override IPluginFactory GetPluginFactory() => null;
        public override IActivePluginLogSerializer GetActivePluginLogSerializer(IPluginOrderLog p_polPluginOrderLog) => null;
        public override IPluginDiscoverer GetPluginDiscoverer() => null;
        public override IPluginOrderLogSerializer GetPluginOrderLogSerializer() => null;
        public override IPluginOrderValidator GetPluginOrderValidator() => null;
        public override IGameSpecificValueInstaller GetGameSpecificValueInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate) => null;
        public override IGameSpecificValueInstaller GetGameSpecificValueUpgradeInstaller(IMod p_modMod, IInstallLog p_ilgInstallLog, TxFileManager p_tfmFileManager, FileUtil p_futFileUtility, ConfirmItemOverwriteDelegate p_dlgOverwriteConfirmationDelegate) => null;
        public override IEnumerable<IUpdater> GetUpdaters() => Enumerable.Empty<IUpdater>();

        public override bool RequiresExternalConfig(out string p_strMessage)
        {
            if (!IsNoMansSkyProfile)
                return base.RequiresExternalConfig(out p_strMessage);

            p_strMessage = string.Empty;
            try
            {
                NoMansSkyPathAdjuster.EnsureModsDirectory(GameModeEnvironmentInfo.InstallationPath);
                return false;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Could not prepare the No Man's Sky MODS folder: " + ex.Message);
                p_strMessage = @"Could not create GAMEDATA\MODS. This folder is required for No Man's Sky mods.";
                return true;
            }
        }

        public override bool IsSpecialFile(IEnumerable<string> p_strFiles)
        {
            if (!IsNoMansSkyProfile)
                return base.IsSpecialFile(p_strFiles);

            return p_strFiles != null &&
                   p_strFiles.Any(path => string.Equals(
                       Path.GetExtension(path),
                       ".pak",
                       StringComparison.InvariantCultureIgnoreCase));
        }

        public override IEnumerable<string> SpecialFileInstall(IMod p_modSelectedMod)
        {
            if (!IsNoMansSkyProfile)
                return base.SpecialFileInstall(p_modSelectedMod);

            var files = p_modSelectedMod == null
                ? new List<string>()
                : p_modSelectedMod.GetFileList();

            var currentFiles = files
                .Where(path => !string.Equals(
                    Path.GetExtension(path),
                    ".pak",
                    StringComparison.InvariantCultureIgnoreCase))
                .ToList();

            string[] documentationExtensions =
            {
                ".txt", ".md", ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".url"
            };

            bool hasCurrentPayload = currentFiles.Any(path =>
                Path.GetFileName(path).Equals("LocTable.MXML", StringComparison.InvariantCultureIgnoreCase) ||
                !documentationExtensions.Contains(
                    Path.GetExtension(path),
                    StringComparer.InvariantCultureIgnoreCase));

            if (!hasCurrentPayload)
            {
                throw new InvalidDataException(
                    "This archive only contains legacy .pak content. " +
                    "No Man's Sky 5.50 and later require loose mod folders under GAMEDATA\\MODS.");
            }

            return currentFiles;
        }

        public override void SortMods(
            Action<IMod, IMod> p_actReinstallMod,
            ReadOnlyObservableList<IMod> p_lstActiveMods)
        {
            if (!IsNoMansSkyProfile)
            {
                base.SortMods(p_actReinstallMod, p_lstActiveMods);
                return;
            }

            NoMansSkyPathAdjuster.InvalidateModSettings(
                GameModeEnvironmentInfo.InstallationPath);

            if (p_actReinstallMod == null || p_lstActiveMods == null)
                return;

            foreach (IMod mod in p_lstActiveMods)
                p_actReinstallMod(mod, null);
        }

        private bool IsNoMansSkyProfile =>
            string.Equals(
                _definition.ModInstall == null
                    ? null
                    : _definition.ModInstall.PathAdjustmentProfile,
                "nomanssky",
                StringComparison.OrdinalIgnoreCase);

        public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
        {
            string baseAdjusted = base.GetModFormatAdjustedPath(p_mftModFormat, p_strPath, p_modMod, p_booIgnoreIfPresent);
            return DataDrivenPathAdjustmentDispatcher.Adjust(
                _definition.ModInstall == null ? null : _definition.ModInstall.PathAdjustmentProfile,
                p_strPath,
                baseAdjusted,
                p_modMod,
                GameVersion,
                GameModeEnvironmentInfo.InstallationPath);
        }

        public override bool HardlinkRequiredFilesType(string p_strFileName)
        {
            string[] configuredExtensions = _definition.ModInstall == null
                ? null
                : _definition.ModInstall.HardlinkRequiredExtensions;

            if (configuredExtensions != null)
            {
                return DataDrivenGameModeHelpers.MatchesExtension(
                    configuredExtensions,
                    Path.GetExtension(p_strFileName));
            }

            return base.HardlinkRequiredFilesType(p_strFileName);
        }

        public override bool RealFileRequired(string fileExtension)
        {
            return DataDrivenGameModeHelpers.MatchesExtension(_definition.ModInstall == null ? null : _definition.ModInstall.RealFileRequiredExtensions, fileExtension) ||
                   base.RealFileRequired(fileExtension);
        }

        protected override IGameModeDescriptor CreateGameModeDescriptor()
        {
            GameModeDefinition definition = _pendingDefinition;
            if (definition == null)
                throw new InvalidOperationException("No data-driven GameMode definition was provided.");
            return new DataDrivenGameModeDescriptor(EnvironmentInfo, definition);
        }

        private DataDrivenPathContext CreatePathContext()
        {
            return new DataDrivenPathContext(
                EnvironmentInfo,
                GameModeEnvironmentInfo.InstallationPath,
                GameModeEnvironmentInfo.ExecutablePath,
                ModeId,
                null);
        }

        protected override void Dispose(bool p_booDisposing)
        {
        }
    }

    internal static class DataDrivenGameModeHelpers
    {
        public static IEnumerable<ISettingsGroupView> BuildSettingsGroupViews(IEnvironmentInfo environmentInfo, IGameMode gameMode, GameModeDefinition definition)
        {
            if (definition == null || definition.Settings == null || definition.Settings.UseGenericSettings != true)
                return new List<ISettingsGroupView>();

            var group = new DataDrivenGameModeSettingsGroup(environmentInfo, gameMode, definition);
            return new List<ISettingsGroupView> { new DataDrivenGameModeSettingsPage(group) };
        }

        public static string ReadResourceText(GameModeDefinition definition, string relativePath)
        {
            if (definition == null || string.IsNullOrWhiteSpace(relativePath))
                return null;
            string path = Path.Combine(definition.DefinitionDirectory ?? string.Empty, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public static bool MatchesExtension(IEnumerable<string> extensions, string extension)
        {
            return extensions != null && !string.IsNullOrWhiteSpace(extension) &&
                   extensions.Any(x => string.Equals(x, extension, StringComparison.InvariantCultureIgnoreCase));
        }
    }

    internal sealed class DataDrivenPathContext
    {
        public DataDrivenPathContext(IEnvironmentInfo environmentInfo, string gamePath, string executablePath, string modeId, string userGameDataPath)
        {
            EnvironmentInfo = environmentInfo;
            GamePath = gamePath;
            ExecutablePath = executablePath;
            ModeId = modeId;
            UserGameDataPath = userGameDataPath;
        }

        public IEnvironmentInfo EnvironmentInfo { get; private set; }
        public string GamePath { get; private set; }
        public string ExecutablePath { get; private set; }
        public string ModeId { get; private set; }
        public string UserGameDataPath { get; private set; }
    }

    internal static class DataDrivenPathResolver
    {
        private static readonly Regex RemainingPlaceholderRegex = new Regex("\\{[^{}]+\\}", RegexOptions.CultureInvariant);

        public static string Expand(string value, DataDrivenPathContext context)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            if (DataDrivenDefinitionRules.HasParentTraversal(value))
                throw new InvalidOperationException("Path contains parent-directory traversal: " + value);
            if (DataDrivenDefinitionRules.ContainsUnknownPlaceholder(value))
                throw new InvalidOperationException("Path contains an unknown placeholder: " + value);

            context = context ?? new DataDrivenPathContext(null, null, null, null, null);
            string personalData = context.EnvironmentInfo == null ? null : context.EnvironmentInfo.PersonalDataFolderPath;
            string expanded = value;
            expanded = ReplaceIgnoreCase(expanded, "{GamePath}", context.GamePath);
            expanded = ReplaceIgnoreCase(expanded, "{InstallationPath}", context.GamePath);
            expanded = ReplaceIgnoreCase(expanded, "{ExecutablePath}", context.ExecutablePath);
            expanded = ReplaceIgnoreCase(expanded, "{ModeId}", context.ModeId);
            expanded = ReplaceIgnoreCase(expanded, "{Documents}", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            expanded = ReplaceIgnoreCase(expanded, "{PersonalData}", personalData ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal));
            expanded = ReplaceIgnoreCase(expanded, "{LocalApplicationData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            expanded = ReplaceIgnoreCase(expanded, "{ProgramFiles}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            expanded = ReplaceIgnoreCase(expanded, "{ProgramFilesX86}", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            expanded = ReplaceIgnoreCase(expanded, "{UserGameData}", context.UserGameDataPath);

            Match unresolved = RemainingPlaceholderRegex.Match(expanded);
            if (unresolved.Success)
                throw new InvalidOperationException("Path placeholder could not be resolved: " + unresolved.Value);

            return expanded.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        }

        public static string ResolvePath(string value, DataDrivenPathContext context, string relativeBasePath)
        {
            string expanded = Expand(value, context);
            if (string.IsNullOrWhiteSpace(expanded))
                return expanded;

            if (!Path.IsPathRooted(expanded))
            {
                string basePath = string.IsNullOrWhiteSpace(relativeBasePath) ? (context == null ? null : context.GamePath) : relativeBasePath;
                if (string.IsNullOrWhiteSpace(basePath))
                    throw new InvalidOperationException("A relative data-driven path has no explicit base directory: " + value);
                expanded = Path.Combine(basePath, expanded);
            }

            return Path.GetFullPath(expanded);
        }

        private static string ReplaceIgnoreCase(string input, string token, string replacement)
        {
            if (input.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                return input;
            if (replacement == null)
                return input;
            return Regex.Replace(input, Regex.Escape(token), match => replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    internal static class DataDrivenPluginPolicyBuilder
    {
        public static PluginManagementPolicy Build(
            GameModeDefinition gameModeDefinition,
            IEnumerable<string> pluginExtensions,
            IEnumerable<string> criticalPlugins,
            IEnumerable<string> officialPlugins,
            IEnumerable<string> officialUnmanagedPlugins,
            int fullSlotLimit,
            DataDrivenPathContext pathContext)
        {
            PluginManagementPolicy policy = PluginManagementPolicy.CreateDefault(
                pluginExtensions,
                criticalPlugins,
                officialPlugins,
                officialUnmanagedPlugins,
                fullSlotLimit);

            GameModePluginPolicyDefinition definition = gameModeDefinition == null || gameModeDefinition.Plugin == null
                ? null
                : gameModeDefinition.Plugin.Policy;
            if (definition == null)
                return policy;
            if (definition.SchemaVersion != 1)
                throw new InvalidOperationException("plugin.policy.schemaVersion must be 1.");

            if (!string.IsNullOrWhiteSpace(definition.ParserStrategy))
                policy.ParserStrategy = definition.ParserStrategy;
            if (!string.IsNullOrWhiteSpace(definition.PersistenceStrategy))
                policy.PersistenceStrategy = definition.PersistenceStrategy;
            if (definition.MasterPluginsMustLoadBeforeNonMasters.HasValue)
                policy.MasterPluginsMustLoadBeforeNonMasters = definition.MasterPluginsMustLoadBeforeNonMasters.Value;
            if (definition.ValidateDependencies.HasValue)
                policy.ValidateDependencies = definition.ValidateDependencies.Value;

            if (!string.IsNullOrWhiteSpace(definition.PluginsFilePath))
                policy.PluginsFilePath = DataDrivenPathResolver.ResolvePath(definition.PluginsFilePath, pathContext, pathContext == null ? null : pathContext.GamePath);
            if (!string.IsNullOrWhiteSpace(definition.LoadOrderFilePath))
                policy.LoadOrderFilePath = DataDrivenPathResolver.ResolvePath(definition.LoadOrderFilePath, pathContext, pathContext == null ? null : pathContext.GamePath);

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
                policy.AddExtension(new PluginExtensionPolicy(extension.Extension, ParseHeaderFlags(extension.ForcedFlags, true), ParseAddressClass(extension.ForcedAddressClass, true)));

            foreach (GameModePluginHeaderFlagMappingDefinition mapping in definition.HeaderFlagMappings ?? new List<GameModePluginHeaderFlagMappingDefinition>())
                policy.AddHeaderFlagMapping(new PluginHeaderFlagMapping(ParseHeaderFlagSource(mapping.Source), ParseUInt32(mapping.Mask), ParseHeaderFlags(mapping.Flags, false)));

            foreach (GameModePluginAddressSpaceDefinition addressSpace in definition.AddressSpaces ?? new List<GameModePluginAddressSpaceDefinition>())
            {
                if (!addressSpace.FirstIndex.HasValue || !addressSpace.MaxCount.HasValue)
                    throw new InvalidOperationException("Plugin address spaces require firstIndex and maxCount.");
                policy.AddAddressSpace(new PluginAddressSpacePolicy(
                    ParseAddressClass(addressSpace.AddressClass, false),
                    addressSpace.FirstIndex.Value,
                    addressSpace.MaxCount.Value,
                    DataDrivenDefinitionRules.NormalizeDisplayFormat(addressSpace.DisplayFormat)));
            }

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

        private static PluginHeaderFlags ParseHeaderFlags(string value, bool allowEmpty)
        {
            if (string.IsNullOrWhiteSpace(value) && allowEmpty)
                return PluginHeaderFlags.None;
            PluginHeaderFlags flags;
            if (!DataDrivenDefinitionRules.TryParseHeaderFlags(value, out flags))
                throw new InvalidOperationException("Invalid plugin header flags: " + value);
            return flags;
        }

        private static PluginAddressClass ParseAddressClass(string value, bool allowNone)
        {
            if (string.IsNullOrWhiteSpace(value) && allowNone)
                return PluginAddressClass.None;
            PluginAddressClass addressClass;
            if (!DataDrivenDefinitionRules.TryParseAddressClass(value, allowNone, out addressClass))
                throw new InvalidOperationException("Invalid plugin address class: " + value);
            return addressClass;
        }

        private static PluginHeaderFlagSource ParseHeaderFlagSource(string value)
        {
            PluginHeaderFlagSource source;
            if (!DataDrivenDefinitionRules.TryParseHeaderFlagSource(value, out source))
                throw new InvalidOperationException("Invalid plugin header flag source: " + value);
            return source;
        }

        private static uint ParseUInt32(object value)
        {
            uint result;
            if (!DataDrivenDefinitionRules.TryParseUInt32(value, out result) || result == 0)
                throw new InvalidOperationException("Plugin header flag mask must be a non-zero UInt32 value.");
            return result;
        }
    }

    internal static class DataDrivenPathAdjustmentDispatcher
    {
        public static string Adjust(string profile, string originalPath, string baseAdjustedPath, IMod mod, Version gameVersion, string installationPath)
        {
            if (string.IsNullOrWhiteSpace(profile) || string.Equals(profile, "none", StringComparison.OrdinalIgnoreCase))
                return baseAdjustedPath;
            if (string.Equals(profile, "cyberpunk2077", StringComparison.OrdinalIgnoreCase))
                return CyberpunkPathAdjuster.Adjust(originalPath, gameVersion);
            if (string.Equals(profile, "fallout4", StringComparison.OrdinalIgnoreCase))
                return Fallout4PathAdjuster.Adjust(originalPath);
            if (string.Equals(profile, "oblivionremastered", StringComparison.OrdinalIgnoreCase))
                return OblivionRemasteredPathAdjuster.Adjust(originalPath, baseAdjustedPath);
            if (string.Equals(profile, "starfield", StringComparison.OrdinalIgnoreCase))
                return StarfieldPathAdjuster.Adjust(originalPath);
            if (string.Equals(profile, "stardewvalley", StringComparison.OrdinalIgnoreCase))
                return StardewValleyPathAdjuster.Adjust(originalPath);
            if (string.Equals(profile, "subnautica", StringComparison.OrdinalIgnoreCase))
                return SubnauticaPathAdjuster.Adjust(originalPath);
            if (string.Equals(profile, "sims4", StringComparison.OrdinalIgnoreCase))
                return Sims4PathAdjuster.Adjust(originalPath);
            if (string.Equals(profile, "nomanssky", StringComparison.OrdinalIgnoreCase))
                return NoMansSkyPathAdjuster.Adjust(originalPath, mod, installationPath);
            throw new InvalidOperationException("Unsupported data-driven path adjustment profile: " + profile);
        }
    }



    internal static class OblivionRemasteredPathAdjuster
    {
        private static readonly string DataRoot =
            Path.Combine("OblivionRemastered", "Content", "Dev", "ObvData", "Data");
        private static readonly string Win64Root =
            Path.Combine("OblivionRemastered", "Binaries", "Win64");
        private static readonly string PaksRoot =
            Path.Combine("OblivionRemastered", "Content", "Paks");

        private static readonly HashSet<string> DataFolders =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "baseobjectswapper",
                "config",
                "distantlod",
                "facegen",
                "fonts",
                "interface",
                "lodsettings",
                "lsdata",
                "magicloader",
                "mapmarkers",
                "menus",
                "meshes",
                "music",
                "scripts",
                "shaders",
                "sound",
                "syncmap",
                "textures",
                "trees",
                "video"
            };

        private static readonly HashSet<string> DataFileExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".bsa",
                ".esm",
                ".esp"
            };

        private static readonly HashSet<string> NativeFileExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".asi",
                ".dll",
                ".exe"
            };

        private static readonly HashSet<string> PakFileExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".pak",
                ".ucas",
                ".utoc"
            };

        public static string Adjust(string originalPath, string baseAdjustedPath)
        {
            if (string.IsNullOrWhiteSpace(originalPath))
                return originalPath;

            string path = Normalize(originalPath);
            if (IsSamePathOrChild(path, "OblivionRemastered") ||
                IsSamePathOrChild(path, "Engine"))
            {
                return path;
            }

            if (IsSamePathOrChild(path, "Data"))
                return CombineAfterRoot(DataRoot, path, "Data");

            if (IsSamePathOrChild(path, "Binaries") ||
                IsSamePathOrChild(path, "Content"))
            {
                return Path.Combine("OblivionRemastered", path);
            }

            if (IsSamePathOrChild(path, "Paks"))
                return CombineAfterRoot(PaksRoot, path, "Paks");

            if (IsSamePathOrChild(path, "~mods") ||
                IsSamePathOrChild(path, "LogicMods"))
            {
                return Path.Combine(PaksRoot, path);
            }

            if (IsSamePathOrChild(path, "OBSE"))
                return Path.Combine(Win64Root, path);

            string extension = Path.GetExtension(path);
            string firstSegment = GetFirstSegment(path);

            if (PakFileExtensions.Contains(extension))
                return Path.Combine(PaksRoot, "~mods", Path.GetFileName(path));

            if (NativeFileExtensions.Contains(extension) &&
                path.IndexOf(Path.DirectorySeparatorChar) < 0)
            {
                return Path.Combine(Win64Root, path);
            }

            if (DataFolders.Contains(firstSegment) ||
                DataFileExtensions.Contains(extension))
            {
                return Path.Combine(DataRoot, path);
            }

            string basePath = Normalize(baseAdjustedPath);
            if (IsSamePathOrChild(basePath, "Data"))
                return CombineAfterRoot(DataRoot, basePath, "Data");

            return path;
        }

        private static string Normalize(string path)
        {
            return (path ?? string.Empty)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
        }

        private static string GetFirstSegment(string path)
        {
            int separator = path.IndexOf(Path.DirectorySeparatorChar);
            return separator < 0 ? path : path.Substring(0, separator);
        }

        private static bool IsSamePathOrChild(string path, string root)
        {
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(
                       root + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string CombineAfterRoot(
            string destinationRoot,
            string path,
            string sourceRoot)
        {
            if (path.Equals(sourceRoot, StringComparison.OrdinalIgnoreCase))
                return destinationRoot;

            return Path.Combine(
                destinationRoot,
                path.Substring(sourceRoot.Length + 1));
        }
    }

    internal static class Fallout4PathAdjuster
    {
        private static readonly string[] PluginExtensions = { ".esm", ".esl", ".esp" };

        public static string Adjust(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (result.Count(c => c == Path.DirectorySeparatorChar) != 1)
                return result;

            string extension = Path.GetExtension(result);
            if (!PluginExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return result;

            string root = Path.GetDirectoryName(result);
            if (string.IsNullOrWhiteSpace(root) ||
                root.Equals("Data", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            return Path.Combine("Data", Path.GetFileName(result));
        }
    }

    internal static class StarfieldPathAdjuster
    {
        private static readonly string[] PluginExtensions = { ".esm" };

        public static string Adjust(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            if (result.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith(
                    "Data" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            string extension = Path.GetExtension(result);
            if (!PluginExtensions.Contains(
                    extension,
                    StringComparer.OrdinalIgnoreCase))
            {
                return result;
            }

            // Preserve the legacy Starfield correction for archives containing
            // one incorrect wrapper directory around an ESM.
            int separatorCount =
                result.Count(character =>
                    character == Path.DirectorySeparatorChar);

            if (separatorCount <= 1)
                return Path.Combine("Data", Path.GetFileName(result));

            return result;
        }
    }

    internal static class StardewValleyPathAdjuster
    {
        public static string Adjust(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            if (result.Equals("Mods", StringComparison.OrdinalIgnoreCase) ||
                result.StartsWith(
                    "Mods" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            return Path.Combine("Mods", result);
        }
    }

    internal static class SubnauticaPathAdjuster
    {
        private static readonly HashSet<string> BepInExChildRoots =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cache",
                "config",
                "core",
                "patchers",
                "plugins"
            };

        private static readonly HashSet<string> GameRootFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".doorstop_version",
                "doorstop_config.ini",
                "steam_appid.txt",
                "winhttp.dll"
            };

        public static string Adjust(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = Normalize(path);

            // Framework archives and explicitly rooted mod archives already
            // describe their destination relative to the game directory.
            if (IsSamePathOrChild(result, "BepInEx") ||
                IsSamePathOrChild(result, "QMods") ||
                IsSamePathOrChild(result, "Subnautica_Data") ||
                IsSamePathOrChild(result, "SubnauticaZero_Data"))
            {
                return result;
            }

            string firstSegment = GetFirstSegment(result);

            // Some BepInEx mods omit the outer BepInEx folder but retain one
            // of its standard child roots.
            if (BepInExChildRoots.Contains(firstSegment))
                return Path.Combine("BepInEx", result);

            // Doorstop/BepInEx bootstrap files belong beside Subnautica.exe.
            if (result.IndexOf(Path.DirectorySeparatorChar) < 0 &&
                GameRootFiles.Contains(result))
            {
                return result;
            }

            // Modern Subnautica mods are BepInEx plugins by default. Keeping
            // the archive's relative layout also preserves mod-owned folders,
            // configuration files and bundled dependencies.
            return Path.Combine("BepInEx", "plugins", result);
        }

        private static string Normalize(string path)
        {
            return path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
        }

        private static string GetFirstSegment(string path)
        {
            int separator = path.IndexOf(Path.DirectorySeparatorChar);
            return separator < 0 ? path : path.Substring(0, separator);
        }

        private static bool IsSamePathOrChild(string path, string root)
        {
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(
                       root + Path.DirectorySeparatorChar,
                       StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class Sims4PathAdjuster
    {
        private static readonly string[] TrayExtensions =
        {
            ".householdbinary",
            ".trayitem",
            ".sgi",
            ".hhi",
            ".blueprint",
            ".bpi",
            ".room",
            ".rmi"
        };

        private static readonly string[] SaveExtensions = { ".save" };
        private static readonly string[] UnusedExtensions =
        {
            ".txt",
            ".png",
            ".jpg",
            ".pdf",
            ".doc",
            ".docx"
        };

        public static string Adjust(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = path
                .Replace(
                    Path.AltDirectorySeparatorChar,
                    Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);

            string extension = Path.GetExtension(result);
            if (UnusedExtensions.Contains(
                    extension,
                    StringComparer.InvariantCultureIgnoreCase))
            {
                return string.Empty;
            }

            if (IsSamePathOrChild(result, "Tray") ||
                IsSamePathOrChild(result, "Mods") ||
                IsSamePathOrChild(result, "saves"))
            {
                return result;
            }

            // Preserve the legacy wrapper-folder cleanup used by the original
            // Sims 4 Game Mode.
            int separatorCount =
                result.Count(character =>
                    character == Path.DirectorySeparatorChar);

            if (separatorCount >= 2)
            {
                result = result.Substring(
                    result.IndexOf(Path.DirectorySeparatorChar) + 1);
            }

            if (TrayExtensions.Contains(
                    extension,
                    StringComparer.InvariantCultureIgnoreCase))
            {
                return Path.Combine("Tray", result);
            }

            if (SaveExtensions.Contains(
                    extension,
                    StringComparer.InvariantCultureIgnoreCase))
            {
                return Path.Combine("saves", result);
            }

            return Path.Combine("Mods", result);
        }

        private static bool IsSamePathOrChild(string path, string root)
        {
            return path.Equals(
                       root,
                       StringComparison.InvariantCultureIgnoreCase) ||
                   path.StartsWith(
                       root + Path.DirectorySeparatorChar,
                       StringComparison.InvariantCultureIgnoreCase);
        }
    }

    internal static class NoMansSkyPathAdjuster
    {
        private static readonly HashSet<string> ContentRoots =
            new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
            {
                "AUDIO",
                "FONTS",
                "GLOBALS",
                "LANGUAGE",
                "MATERIALS",
                "METADATA",
                "MODELS",
                "PIPELINES",
                "SCENES",
                "SHADERS",
                "TEXTURES",
                "UI"
            };

        public static string Adjust(string path, IMod mod, string installationPath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string result = Normalize(path);
            string extension = Path.GetExtension(result);

            // The 5.50+ game no longer loads legacy PSARC .pak mods.
            if (extension.Equals(".pak", StringComparison.InvariantCultureIgnoreCase))
                return string.Empty;

            // Native injectors and executable helpers still belong beside NMS.exe.
            if (IsSamePathOrChild(result, "Binaries"))
                return result;

            if (!result.Contains(Path.DirectorySeparatorChar) &&
                (extension.Equals(".dll", StringComparison.InvariantCultureIgnoreCase) ||
                 extension.Equals(".exe", StringComparison.InvariantCultureIgnoreCase)))
            {
                return Path.Combine("Binaries", result);
            }

            EnsureModsDirectory(installationPath);
            InvalidateModSettings(installationPath);

            string contentPath = StripArchiveLayout(result);
            string modFolder = GetDeploymentFolderName(mod, installationPath);

            return Path.Combine("GAMEDATA", "MODS", modFolder, contentPath);
        }

        public static void EnsureModsDirectory(string installationPath)
        {
            if (string.IsNullOrWhiteSpace(installationPath))
                throw new InvalidOperationException("The No Man's Sky installation path is not configured.");

            string modsPath = Path.Combine(installationPath, "GAMEDATA", "MODS");
            if (!Directory.Exists(modsPath))
                Directory.CreateDirectory(modsPath);
        }

        public static void InvalidateModSettings(string installationPath)
        {
            if (string.IsNullOrWhiteSpace(installationPath))
                return;

            string settingsPath = Path.Combine(
                installationPath,
                "Binaries",
                "SETTINGS",
                "GCMODSETTINGS.MXML");

            try
            {
                if (File.Exists(settingsPath))
                    File.Delete(settingsPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Could not invalidate No Man's Sky mod settings: " + ex.Message);
            }
        }

        private static string GetDeploymentFolderName(IMod mod, string installationPath)
        {
            string baseName = GetSafeModName(mod);
            int oldOrder = mod == null ? -1 : mod.PlaceInModLoadOrder;
            int newOrder = mod == null ? -1 : mod.NewPlaceInModLoadOrder;

            string oldFolder = BuildOrderedFolderName(baseName, oldOrder);
            string oldPath = Path.Combine(
                installationPath ?? string.Empty,
                "GAMEDATA",
                "MODS",
                oldFolder);

            if (oldOrder >= 0 && Directory.Exists(oldPath))
                return oldFolder;

            int targetOrder = newOrder >= 0 ? newOrder : oldOrder;
            return BuildOrderedFolderName(baseName, targetOrder);
        }

        private static string BuildOrderedFolderName(string baseName, int order)
        {
            string orderPrefix = order >= 0
                ? order.ToString("D4", CultureInfo.InvariantCulture)
                : "9999";

            return orderPrefix + "_" + baseName;
        }

        private static string GetSafeModName(IMod mod)
        {
            string name = mod == null ? null : mod.ModName;
            if (string.IsNullOrWhiteSpace(name) && mod != null)
                name = Path.GetFileNameWithoutExtension(mod.Filename);
            if (string.IsNullOrWhiteSpace(name) && mod != null)
                name = mod.DownloadId;
            if (string.IsNullOrWhiteSpace(name) && mod != null)
                name = mod.Id;
            if (string.IsNullOrWhiteSpace(name))
                name = "NMMMod";

            name = Regex.Replace(name, @"[^A-Za-z0-9._ -]+", "_");
            name = Regex.Replace(name, @"\s+", " ").Trim(' ', '.');

            if (string.IsNullOrWhiteSpace(name))
                name = "NMMMod";

            // Current NMS mod-folder names must stay below the game's limit.
            return name.Length > 96 ? name.Substring(0, 96).TrimEnd(' ', '.') : name;
        }

        private static string StripArchiveLayout(string path)
        {
            string[] segments = Normalize(path)
                .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

            int index = 0;
            if (segments.Length >= 2 &&
                segments[0].Equals("GAMEDATA", StringComparison.InvariantCultureIgnoreCase) &&
                segments[1].Equals("MODS", StringComparison.InvariantCultureIgnoreCase))
            {
                index = 2;
            }
            else if (segments.Length >= 1 &&
                     segments[0].Equals("MODS", StringComparison.InvariantCultureIgnoreCase))
            {
                index = 1;
            }
            else if (segments.Length >= 1 &&
                     segments[0].Equals("GAMEDATA", StringComparison.InvariantCultureIgnoreCase))
            {
                index = 1;
            }

            // Archives commonly contain their own outer mod-name folder.
            if (segments.Length - index >= 2 &&
                !ContentRoots.Contains(segments[index]) &&
                ContentRoots.Contains(segments[index + 1]))
            {
                index++;
            }

            string contentPath = string.Join(
                Path.DirectorySeparatorChar.ToString(),
                segments.Skip(index));

            return string.IsNullOrWhiteSpace(contentPath)
                ? Path.GetFileName(path)
                : contentPath;
        }

        private static string Normalize(string path)
        {
            return path
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
        }

        private static bool IsSamePathOrChild(string path, string parentPath)
        {
            return path.Equals(parentPath, StringComparison.InvariantCultureIgnoreCase) ||
                   path.StartsWith(
                       parentPath + Path.DirectorySeparatorChar,
                       StringComparison.InvariantCultureIgnoreCase);
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

            if (!result.StartsWith("archive", StringComparison.InvariantCultureIgnoreCase) &&
                !result.StartsWith("red4ext", StringComparison.InvariantCultureIgnoreCase) &&
                !result.StartsWith("mods", StringComparison.InvariantCultureIgnoreCase))
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
            {
                result = result.Replace("patch", "mod");
            }

            return result;
        }
    }
}
