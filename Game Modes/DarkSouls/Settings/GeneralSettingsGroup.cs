using Nexus.Client.Settings;
using Nexus.Client.Games.Settings;
using Nexus.Client.Util;
using System.IO;
using System;

namespace Nexus.Client.Games.DarkSouls
{
	/// <summary>
	/// The group of general settings.
	/// </summary>
	public class GeneralSettingsGroup : SettingsGroup
	{
		private string m_strInstallationPath = null;
		private string m_strCustomCommand = null;
		private string m_strCustomCommandArguments = null;

		#region Properties

		/// <summary>
		/// Gets the game mode currently being managed.
		/// </summary>
		/// <value>The game mode currently being managed.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the view model that encapsulates the data and the operations presented by UI
		/// elements that allow the selection of required directories.
		/// </summary>
		/// <value>The view model that encapsulates the data and the operations presented by UI
		/// elements that allow the selection of required directories.</value>
		public RequiredDirectoriesControlVM RequiredDirectoriesVM { get; private set; }

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title
		{
			get
			{
				return GameMode.Name;
			}
		}

		/// <summary>
		/// Gets or sets the path to which mod files should be installed.
		/// </summary>
		/// <value>The path to which mod files should be installed.</value>
		public string InstallationPath
		{
			get
			{
				return m_strInstallationPath;
			}
			set
			{
				SetPropertyIfChanged(ref m_strInstallationPath, value, () => InstallationPath);
			}
		}

		/// <summary>
		/// Gets or sets the custom launch command.
		/// </summary>
		/// <value>The custom launch command.</value>
		public string CustomLaunchCommand
		{
			get
			{
				return m_strCustomCommand;
			}
			set
			{
				SetPropertyIfChanged(ref m_strCustomCommand, value, () => CustomLaunchCommand);
			}
		}

		/// <summary>
		/// Gets or set the custom launch command arguments.
		/// </summary>
		/// <value>The custom launch command arguments.</value>
		public string CustomLaunchCommandArguments
		{
			get
			{
				return m_strCustomCommandArguments;
			}
			set
			{
				SetPropertyIfChanged(ref m_strCustomCommandArguments, value, () => CustomLaunchCommandArguments);
			}
		}


		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameMode">The game mode currently being managed.</param>
		public GeneralSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, IGameMode p_gmdGameMode)
			: base(p_eifEnvironmentInfo)
		{
			GameMode = p_gmdGameMode;
			RequiredDirectoriesVM = new RequiredDirectoriesControlVM(p_eifEnvironmentInfo, p_gmdGameMode);
		}

		#endregion

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			string strValue = null;
			bool booRetrieved = false;
			if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameMode.ModeId))
				booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].TryGetValue(String.Format("InstallationPaths~{0}", GameMode.ModeId), out strValue);
			if (!booRetrieved)
				EnvironmentInfo.Settings.InstallationPaths.TryGetValue(GameMode.ModeId, out strValue);
			InstallationPath = strValue;

			strValue = null;
			EnvironmentInfo.Settings.CustomLaunchCommands.TryGetValue(GameMode.ModeId, out strValue);
			CustomLaunchCommand = strValue;

			strValue = null;
			EnvironmentInfo.Settings.CustomLaunchCommandArguments.TryGetValue(GameMode.ModeId, out strValue);
			CustomLaunchCommandArguments = strValue;

			RequiredDirectoriesVM.LoadSettings();
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			if (!RequiredDirectoriesVM.ValidateSettings())
				return false;
			RequiredDirectoriesVM.SaveSettings(true);

			if (!String.Equals(EnvironmentInfo.Settings.InstallationPaths[GameMode.ModeId], InstallationPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)))
				EnvironmentInfo.Settings.DelayedSettings[GameMode.ModeId].Add(String.Format("InstallationPaths~{0}", GameMode.ModeId), InstallationPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
			EnvironmentInfo.Settings.CustomLaunchCommands[GameMode.ModeId] = FileUtil.StripInvalidPathChars(CustomLaunchCommand);
			EnvironmentInfo.Settings.CustomLaunchCommandArguments[GameMode.ModeId] = CustomLaunchCommandArguments;
			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
