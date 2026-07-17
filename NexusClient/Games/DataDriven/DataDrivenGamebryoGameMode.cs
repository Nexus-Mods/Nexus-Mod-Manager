using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ChinhDo.Transactions;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Tools;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods;
using Nexus.Client.PluginManagement;
using Nexus.Client.Settings.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGamebryoGameMode : GamebryoGameModeBase
    {
        [ThreadStatic]
        private static GameModeDefinition _pendingDefinition;

        private readonly GameModeDefinition _definition;
        private IGameLauncher _gameLauncher;
        private IToolLauncher _toolLauncher;
        private ISupportedToolsLauncher _supportedToolsLauncher;
        private string _categories;
        private string _baseFiles;

        public DataDrivenGamebryoGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition)
            : this(environmentInfo, fileUtility, PushDefinition(definition), true)
        {
        }

        private DataDrivenGamebryoGameMode(IEnvironmentInfo environmentInfo, FileUtil fileUtility, GameModeDefinition definition, bool definitionIsPending)
            : base(environmentInfo, fileUtility)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _pendingDefinition = null;
            if (_definition.Settings != null && _definition.Settings.UseGenericSettings == true)
                SettingsGroupViews = DataDrivenGameModeHelpers.BuildSettingsGroupViews(environmentInfo, this, _definition);
        }

        private static GameModeDefinition PushDefinition(GameModeDefinition definition)
        {
            _pendingDefinition = definition;
            return definition;
        }

        protected override string[] ScriptExtenderExecutables => GetDefinition().Gamebryo == null
            ? new string[0]
            : GetDefinition().Gamebryo.ScriptExtenderExecutables ?? new string[0];

        public override Version GameVersion
        {
            get
            {
                string executableRoot = GameModeEnvironmentInfo.ExecutablePath ??
                                        GameModeEnvironmentInfo.InstallationPath ??
                                        string.Empty;

                foreach (string executable in GameExecutables ?? new string[0])
                {
                    if (string.IsNullOrWhiteSpace(executable))
                        continue;

                    string fullPath = Path.Combine(executableRoot, executable);
                    if (!File.Exists(fullPath))
                        continue;

                    FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
                    Version parsedVersion;

                    if (TryParseExecutableVersion(versionInfo.ProductVersion, out parsedVersion))
                        return parsedVersion;

                    if (TryParseExecutableVersion(versionInfo.FileVersion, out parsedVersion))
                        return parsedVersion;
                }

                return null;
            }
        }

        public override string UserGameDataPath
        {
            get
            {
                string configured = GetDefinition().Gamebryo == null ? null : GetDefinition().Gamebryo.UserGameDataPath;
                if (string.IsNullOrWhiteSpace(configured))
                    return GameModeEnvironmentInfo.InstallationPath;
                return DataDrivenPathResolver.ResolvePath(configured, CreatePathContext(null), GameModeEnvironmentInfo.InstallationPath);
            }
        }

        public override List<string> SupportedFormats => GetDefinition().SupportedFormats == null
            ? new List<string> { "fomod" }
            : GetDefinition().SupportedFormats.ToList();

        public override IGameLauncher GameLauncher => _gameLauncher ?? (_gameLauncher = new DataDrivenGameLauncher(this, EnvironmentInfo, GetDefinition()));
        public override IToolLauncher GameToolLauncher => _toolLauncher ?? (_toolLauncher = new DataDrivenToolLauncher(this, EnvironmentInfo, GetDefinition()));
        public override ISupportedToolsLauncher SupportedToolsLauncher => _supportedToolsLauncher ?? (_supportedToolsLauncher = new DataDrivenSupportedToolsLauncher(this, EnvironmentInfo, GetDefinition()));
        public override string GameDefaultCategories => _categories ?? (_categories = DataDrivenGameModeHelpers.ReadResourceText(GetDefinition(), GetDefinition().Resources == null ? null : GetDefinition().Resources.CategoriesPath));
        public override string BaseGameFiles => _baseFiles ?? (_baseFiles = DataDrivenGameModeHelpers.ReadResourceText(GetDefinition(), GetDefinition().Resources == null ? null : GetDefinition().Resources.BaseFilesPath));
        public override bool SupportsPluginAutoSorting => GetDefinition().Plugin != null && GetDefinition().Plugin.SupportsPluginAutoSorting == true;
        public override int MaxAllowedActivePluginsCount => GetDefinition().Plugin == null ? 0 : GetDefinition().Plugin.MaxAllowedActivePluginsCount ?? 0;
        public override bool SupportsGameRootModInstall => GetDefinition().ModInstall != null && GetDefinition().ModInstall.SupportsGameRootInstall == true;
        public override bool RequiresOptionalFilesCheckOnProfileSwitch =>
            GetDefinition().Gamebryo != null &&
            GetDefinition().Gamebryo.RequiresOptionalFilesCheckOnProfileSwitch == true;

        public override PluginManagementPolicy PluginManagementPolicy => DataDrivenPluginPolicyBuilder.Build(
            GetDefinition(),
            PluginExtensions,
            OrderedCriticalPluginNames,
            OrderedOfficialPluginNames,
            OrderedOfficialUnmanagedPluginNames,
            MaxAllowedActivePluginsCount,
            CreatePathContext(UserGameDataPath));

        public override bool RequiresExternalConfig(out string p_strMessage)
        {
            if (!IsStarfieldProfile)
                return base.RequiresExternalConfig(out p_strMessage);

            p_strMessage = string.Empty;

            try
            {
                GameModeGamebryoDefinition gamebryo =
                    GetDefinition().Gamebryo ??
                    new GameModeGamebryoDefinition();

                string iniPath = ResolveConfiguredPath(
                    gamebryo.IniFilePath,
                    CreatePathContext(UserGameDataPath));

                if (string.IsNullOrWhiteSpace(iniPath))
                    throw new InvalidOperationException(
                        "Starfield requires a configured StarfieldCustom.ini path.");

                EnsureStarfieldCustomIni(iniPath);
                return false;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Could not normalize StarfieldCustom.ini: " +
                    ex.Message);

                p_strMessage =
                    "StarfieldCustom.ini could not be configured automatically. " +
                    "Loose-file loading requires bInvalidateOlderFiles=1 and " +
                    "sResourceDataDirsFinal= in its [Archive] section. " +
                    "Obsolete sTestFile plugin-loading entries must also be removed.";

                return true;
            }
        }

        public override string GetModFormatAdjustedPath(IModFormat p_mftModFormat, string p_strPath, IMod p_modMod, bool p_booIgnoreIfPresent)
        {
            string baseAdjusted = base.GetModFormatAdjustedPath(p_mftModFormat, p_strPath, p_modMod, p_booIgnoreIfPresent);
            return DataDrivenPathAdjustmentDispatcher.Adjust(
                GetDefinition().ModInstall == null ? null : GetDefinition().ModInstall.PathAdjustmentProfile,
                p_strPath,
                baseAdjusted,
                p_modMod,
                GameVersion,
                GameModeEnvironmentInfo.InstallationPath);
        }

        public override bool HardlinkRequiredFilesType(string p_strFileName)
        {
            GameModeDefinition definition = GetDefinition();
            string[] configuredExtensions = definition.ModInstall == null
                ? null
                : definition.ModInstall.HardlinkRequiredExtensions;

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
            return DataDrivenGameModeHelpers.MatchesExtension(
                       GetDefinition().ModInstall == null ? null : GetDefinition().ModInstall.RealFileRequiredExtensions,
                       fileExtension) ||
                   base.RealFileRequired(fileExtension);
        }

        public override string[] GetOptionalFilesList(string[] p_strList)
        {
            GameModeGamebryoDefinition gamebryo = GetDefinition().Gamebryo;
            string[] prefixes = gamebryo == null
                ? null
                : gamebryo.OptionalFileNamePrefixes;

            if (!RequiresOptionalFilesCheckOnProfileSwitch ||
                prefixes == null ||
                prefixes.Length == 0)
            {
                return base.GetOptionalFilesList(p_strList);
            }

            if (p_strList == null || p_strList.Length == 0)
                return new string[0];

            return p_strList
                .Where(path =>
                    !string.IsNullOrWhiteSpace(path) &&
                    prefixes.Any(prefix =>
                        !string.IsNullOrWhiteSpace(prefix) &&
                        Path.GetFileName(path).StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        public override string PostProfileSwitchTool(out string p_strMessage)
        {
            GameModeGamebryoDefinition gamebryo = GetDefinition().Gamebryo;
            if (gamebryo == null ||
                string.IsNullOrWhiteSpace(gamebryo.PostProfileSwitchToolPath))
            {
                return base.PostProfileSwitchTool(out p_strMessage);
            }

            p_strMessage = gamebryo.PostProfileSwitchToolMessage ?? string.Empty;
            return ResolveConfiguredPath(
                       gamebryo.PostProfileSwitchToolPath,
                       CreatePathContext(UserGameDataPath)) ??
                   string.Empty;
        }

        public override void SetOptionalFilesList(string[] p_strList)
        {
            if (!RequiresOptionalFilesCheckOnProfileSwitch || p_strList == null)
            {
                base.SetOptionalFilesList(p_strList);
                return;
            }

            foreach (string sourcePath in p_strList)
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                    continue;

                File.Copy(
                    sourcePath,
                    Path.Combine(PluginDirectory, Path.GetFileName(sourcePath)),
                    true);
            }
        }

        protected override GamebryoSettingsFiles CreateSettingsFileContainer()
        {
            return new GamebryoSettingsFiles();
        }

        protected override void SetupSettingsFiles()
        {
            GameModeDefinition definition = GetDefinition();
            GameModeGamebryoDefinition gamebryo = definition.Gamebryo ?? new GameModeGamebryoDefinition();
            DataDrivenPathContext context = CreatePathContext(UserGameDataPath);

            SettingsFiles.RendererFilePath = ResolveConfiguredPath(gamebryo.RendererFilePath, context) ?? Path.Combine(UserGameDataPath, "RendererInfo.txt");
            SettingsFiles.PluginsFilePath = ResolveConfiguredPath(gamebryo.PluginsFilePath, context) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), definition.ModeId, "plugins.txt");
            SettingsFiles.IniPath = ResolveConfiguredPath(gamebryo.IniFilePath, context);
            EnsureFile(SettingsFiles.PluginsFilePath);

            if (gamebryo.AdditionalSettingsFiles == null)
                return;

            foreach (KeyValuePair<string, string> pair in gamebryo.AdditionalSettingsFiles)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    SettingsFiles[pair.Key] = ResolveConfiguredPath(pair.Value, context);
            }
        }

        protected override IGameModeDescriptor CreateGameModeDescriptor()
        {
            GameModeDefinition definition = GetDefinition();
            if (definition == null)
                throw new InvalidOperationException("No data-driven Gamebryo GameMode definition was provided.");
            return new DataDrivenGamebryoGameModeDescriptor(EnvironmentInfo, definition);
        }

        private GameModeDefinition GetDefinition()
        {
            return _definition ?? _pendingDefinition;
        }

        private bool IsStarfieldProfile
        {
            get
            {
                GameModeDefinition definition = GetDefinition();
                return definition != null &&
                       definition.ModInstall != null &&
                       string.Equals(
                           definition.ModInstall.PathAdjustmentProfile,
                           "starfield",
                           StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void EnsureStarfieldCustomIni(string iniPath)
        {
            string directory = Path.GetDirectoryName(iniPath);
            if (!string.IsNullOrWhiteSpace(directory) &&
                !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            bool fileExists = File.Exists(iniPath);
            List<string> lines = fileExists
                ? File.ReadAllLines(iniPath).ToList()
                : new List<string>();

            bool changed = RemoveObsoleteStarfieldTestFileEntries(lines);

            int archiveSectionIndex = FindIniSection(lines, "Archive");
            if (archiveSectionIndex < 0)
            {
                if (lines.Count > 0 &&
                    !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                {
                    lines.Add(string.Empty);
                }

                lines.Add("[Archive]");
                lines.Add("bInvalidateOlderFiles=1");
                lines.Add("sResourceDataDirsFinal=");
                changed = true;
            }
            else
            {
                int archiveSectionEnd =
                    FindIniSectionEnd(lines, archiveSectionIndex);

                if (EnsureIniSetting(
                        lines,
                        archiveSectionIndex,
                        ref archiveSectionEnd,
                        "bInvalidateOlderFiles",
                        "1"))
                {
                    changed = true;
                }

                if (EnsureIniSetting(
                        lines,
                        archiveSectionIndex,
                        ref archiveSectionEnd,
                        "sResourceDataDirsFinal",
                        string.Empty))
                {
                    changed = true;
                }
            }

            if (!changed && fileExists)
                return;

            if (fileExists)
            {
                string backupPath = iniPath + ".nmm_backup";
                if (!File.Exists(backupPath))
                    File.Copy(iniPath, backupPath, false);
            }

            File.WriteAllLines(iniPath, lines.ToArray());
        }

        private static bool RemoveObsoleteStarfieldTestFileEntries(
            IList<string> lines)
        {
            bool changed = false;

            for (int index = lines.Count - 1; index >= 0; index--)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string trimmed = line.Trim();
                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                string key = trimmed.Substring(0, equalsIndex).Trim();
                const string prefix = "sTestFile";
                if (!key.StartsWith(
                        prefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int slot;
                if (!int.TryParse(
                        key.Substring(prefix.Length),
                        out slot) ||
                    slot < 1 ||
                    slot > 10)
                {
                    continue;
                }

                lines.RemoveAt(index);
                changed = true;
            }

            return changed;
        }

        private static int FindIniSection(
            IList<string> lines,
            string sectionName)
        {
            string expected = "[" + sectionName + "]";

            for (int index = 0; index < lines.Count; index++)
            {
                if (string.Equals(
                        lines[index].Trim(),
                        expected,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static int FindIniSectionEnd(
            IList<string> lines,
            int sectionIndex)
        {
            for (int index = sectionIndex + 1;
                 index < lines.Count;
                 index++)
            {
                string trimmed = lines[index].Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
                    trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return lines.Count;
        }

        private static bool EnsureIniSetting(
            IList<string> lines,
            int sectionIndex,
            ref int sectionEnd,
            string key,
            string value)
        {
            string expected = key + "=" + value;

            for (int index = sectionIndex + 1;
                 index < sectionEnd;
                 index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string trimmed = line.Trim();
                if (trimmed.StartsWith(";", StringComparison.Ordinal) ||
                    trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex <= 0)
                    continue;

                string currentKey =
                    trimmed.Substring(0, equalsIndex).Trim();

                if (!string.Equals(
                        currentKey,
                        key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(
                        trimmed,
                        expected,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                lines[index] = expected;
                return true;
            }

            lines.Insert(sectionEnd, expected);
            sectionEnd++;
            return true;
        }

        private static bool TryParseExecutableVersion(string value, out Version version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string normalized = value
                .Trim()
                .Replace(", ", ".")
                .Replace(',', '.');

            int suffixIndex = normalized.IndexOfAny(new[] { ' ', '-' });
            if (suffixIndex > 0)
                normalized = normalized.Substring(0, suffixIndex);

            return Version.TryParse(normalized, out version);
        }

        private DataDrivenPathContext CreatePathContext(string userGameDataPath)
        {
            GameModeDefinition definition = GetDefinition();
            return new DataDrivenPathContext(
                EnvironmentInfo,
                GameModeEnvironmentInfo.InstallationPath,
                GameModeEnvironmentInfo.ExecutablePath,
                definition == null ? null : definition.ModeId,
                userGameDataPath);
        }

        private string ResolveConfiguredPath(string value, DataDrivenPathContext context)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : DataDrivenPathResolver.ResolvePath(value, context, GameModeEnvironmentInfo.InstallationPath);
        }

        private static void EnsureFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
                return;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.Create(path).Close();
        }
    }
}
