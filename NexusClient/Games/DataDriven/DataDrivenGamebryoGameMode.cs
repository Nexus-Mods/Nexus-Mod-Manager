using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Gamebryo;
using Nexus.Client.Games.Tools;
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
        private DataDrivenGamebryoGameModeDescriptor _descriptor;
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
            _definition = definition;
            _pendingDefinition = null;
        }

        private static GameModeDefinition PushDefinition(GameModeDefinition definition)
        {
            _pendingDefinition = definition;
            return definition;
        }

        protected override string[] ScriptExtenderExecutables => GetDefinition().Gamebryo?.ScriptExtenderExecutables ?? new string[0];

        public override string UserGameDataPath => ResolvePath(GetDefinition().Gamebryo?.UserGameDataPath) ?? GameModeEnvironmentInfo.InstallationPath;
        public override List<string> SupportedFormats => GetDefinition().SupportedFormats?.ToList() ?? new List<string> { "fomod" };
        public override IGameLauncher GameLauncher => _gameLauncher ?? (_gameLauncher = new DataDrivenGameLauncher(this, EnvironmentInfo, GetDefinition()));
        public override IToolLauncher GameToolLauncher => _toolLauncher ?? (_toolLauncher = new DataDrivenToolLauncher(this, EnvironmentInfo, GetDefinition()));
        public override ISupportedToolsLauncher SupportedToolsLauncher => _supportedToolsLauncher ?? (_supportedToolsLauncher = new DataDrivenSupportedToolsLauncher(this, EnvironmentInfo, GetDefinition()));
        public override string GameDefaultCategories => _categories ?? (_categories = ReadResourceText(GetDefinition().Resources?.CategoriesPath));
        public override string BaseGameFiles => _baseFiles ?? (_baseFiles = ReadResourceText(GetDefinition().Resources?.BaseFilesPath));
        public override bool SupportsPluginAutoSorting => GetDefinition().Plugin == null || GetDefinition().Plugin.SupportsPluginAutoSorting;
        public override PluginManagementPolicy PluginManagementPolicy => DataDrivenPluginPolicyBuilder.Build(GetDefinition(), PluginExtensions, OrderedCriticalPluginNames, OrderedOfficialPluginNames, OrderedOfficialUnmanagedPluginNames, MaxAllowedActivePluginsCount);

        protected override GamebryoSettingsFiles CreateSettingsFileContainer()
        {
            return new GamebryoSettingsFiles();
        }

        protected override void SetupSettingsFiles()
        {
            GameModeDefinition definition = GetDefinition();
            GameModeGamebryoDefinition gamebryo = definition.Gamebryo ?? new GameModeGamebryoDefinition();

            SettingsFiles.RendererFilePath = ResolvePath(gamebryo.RendererFilePath) ?? Path.Combine(UserGameDataPath, "RendererInfo.txt");
            SettingsFiles.PluginsFilePath = ResolvePath(gamebryo.PluginsFilePath) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), definition.ModeId, "plugins.txt");
            SettingsFiles.IniPath = ResolvePath(gamebryo.IniFilePath);
            EnsureFile(SettingsFiles.PluginsFilePath);

            if (gamebryo.AdditionalSettingsFiles != null)
            {
                foreach (KeyValuePair<string, string> pair in gamebryo.AdditionalSettingsFiles)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                        SettingsFiles[pair.Key] = ResolvePath(pair.Value);
                }
            }
        }

        protected override IGameModeDescriptor CreateGameModeDescriptor()
        {
            GameModeDefinition definition = GetDefinition();
            if (definition == null)
                throw new InvalidOperationException("No data-driven Gamebryo GameMode definition was provided.");

            return _descriptor = new DataDrivenGamebryoGameModeDescriptor(EnvironmentInfo, definition);
        }

        private GameModeDefinition GetDefinition()
        {
            return _definition ?? _pendingDefinition;
        }

        private string ResolvePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string path = value.Replace("{PersonalData}", EnvironmentInfo.PersonalDataFolderPath ?? string.Empty)
                               .Replace("{LocalApplicationData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                               .Replace("{GamePath}", GameModeEnvironmentInfo.InstallationPath ?? string.Empty)
                               .Replace("{ExecutablePath}", GameModeEnvironmentInfo.ExecutablePath ?? string.Empty)
                               .Replace("{ModeId}", ModeId ?? string.Empty);

            if (path.IndexOf("{UserGameData}", StringComparison.OrdinalIgnoreCase) >= 0)
                path = path.Replace("{UserGameData}", UserGameDataPath ?? string.Empty);

            return path;
        }

        private string ReadResourceText(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            string path = Path.Combine(GetDefinition().DefinitionDirectory ?? string.Empty, relativePath);
            return File.Exists(path) ? File.ReadAllText(path) : null;
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