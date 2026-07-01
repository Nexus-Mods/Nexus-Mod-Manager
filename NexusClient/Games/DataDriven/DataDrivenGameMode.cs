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
