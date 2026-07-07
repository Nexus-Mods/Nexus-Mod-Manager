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
			AllowCustomLaunchCommand = definition.Settings == null || definition.Settings.AllowCustomLaunchCommand;
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
				retrieved = EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].TryGetValue(string.Format("InstallationPaths~{0}", GameMode.ModeId), out value);
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

			string normalizedPath = (InstallationPath ?? string.Empty).Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			string currentPath = null;
			EnvironmentInfo.Settings.InstallationPaths.TryGetValue(GameMode.ModeId, out currentPath);
			if (!string.Equals(currentPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
			{
				if (!EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameMode.ModeId))
					EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId] = new KeyedSettings<string>();

				string key = string.Format("InstallationPaths~{0}", GameMode.ModeId);
				EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].Remove(key);
				EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].Add(key, normalizedPath);
			}

			EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId] = FileUtil.StripInvalidPathChars(CustomLaunchCommand ?? string.Empty);
			EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId] = CustomLaunchCommandArguments ?? string.Empty;
			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
