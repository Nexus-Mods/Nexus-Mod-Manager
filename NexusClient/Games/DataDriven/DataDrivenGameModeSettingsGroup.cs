using System;
using System.IO;
using Nexus.Client.Games.Settings;
using Nexus.Client.Settings;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGameModeSettingsGroup : SettingsGroup
    {
        private string _installationPath;
        private string _customLaunchCommand;
        private string _customLaunchCommandArguments;

        private IGameMode GameMode { get; }

        public DataDrivenGameModeSettingsGroup(IEnvironmentInfo environmentInfo, IGameMode gameMode, GameModeDefinition definition)
            : base(environmentInfo)
        {
            GameMode = gameMode;
            AllowCustomLaunchCommand = definition != null && definition.Launcher != null && definition.Launcher.AllowCustomCommand == true;
            RequiredDirectoriesVM = new RequiredDirectoriesControlVM(environmentInfo, gameMode);
        }

        public RequiredDirectoriesControlVM RequiredDirectoriesVM { get; }
        public bool AllowCustomLaunchCommand { get; }
        public override string Title => GameMode.Name;

        public string InstallationPath
        {
            get => _installationPath;
            set => SetPropertyIfChanged(ref _installationPath, value, () => InstallationPath);
        }

        public string CustomLaunchCommand
        {
            get => _customLaunchCommand;
            set => SetPropertyIfChanged(ref _customLaunchCommand, value, () => CustomLaunchCommand);
        }

        public string CustomLaunchCommandArguments
        {
            get => _customLaunchCommandArguments;
            set => SetPropertyIfChanged(ref _customLaunchCommandArguments, value, () => CustomLaunchCommandArguments);
        }

        public override void Load()
        {
            string value;
            bool retrieved = false;
            if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameMode.ModeId))
                retrieved = EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].TryGetValue("InstallationPaths~" + GameMode.ModeId, out value);
            else
                value = null;

            if (!retrieved)
                EnvironmentInfo.Settings.InstallationPaths.TryGetValue(GameMode.ModeId, out value);
            InstallationPath = value;

            EnvironmentInfo.Settings.CustomLaunchCommands.TryGetValue(GameMode.ModeId, out value);
            CustomLaunchCommand = value;

            EnvironmentInfo.Settings.CustomLaunchCommandArguments.TryGetValue(GameMode.ModeId, out value);
            CustomLaunchCommandArguments = value;

            RequiredDirectoriesVM.LoadSettings();
        }

        public override bool Save()
        {
            if (!RequiredDirectoriesVM.ValidateSettings())
                return false;

            RequiredDirectoriesVM.SaveSettings(true);

            string normalizedPath = NormalizeDirectoryPath(InstallationPath);
            string currentPath;
            EnvironmentInfo.Settings.InstallationPaths.TryGetValue(GameMode.ModeId, out currentPath);
            if (!string.Equals(currentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameMode.ModeId))
                    EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId] = new KeyedSettings<string>();

                string key = "InstallationPaths~" + GameMode.ModeId;
                EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].Remove(key);
                EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].Add(key, normalizedPath);
            }

            if (AllowCustomLaunchCommand)
            {
                EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId] = FileUtil.StripInvalidPathChars(CustomLaunchCommand ?? string.Empty);
                EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId] = CustomLaunchCommandArguments ?? string.Empty;
            }

            EnvironmentInfo.Settings.Save();
            return true;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string normalized = path.Trim();
            string root = Path.GetPathRoot(normalized);
            while (normalized.Length > (root == null ? 0 : root.Length) &&
                   (normalized.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                    normalized.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }
            return normalized;
        }
    }
}
