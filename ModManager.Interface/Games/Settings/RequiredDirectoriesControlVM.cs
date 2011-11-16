using System;
using System.IO;
using Nexus.Client.Util;
using Nexus.Client.Controls;

namespace Nexus.Client.Games.Settings
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that allow the selection of required directories.
	/// </summary>
	public class RequiredDirectoriesControlVM : ObservableObject
	{
		private string m_strInstallInfoDirectory = null;
		private string m_strModDirectory = null;

		#region Properties

		/// <summary>
		/// Gets the descriptor of the current game mode.
		/// </summary>
		/// <value>The descriptor of the current game mode.</value>
		public IGameModeDescriptor GameModeDescriptor { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the name of the games mode currently being managed.
		/// </summary>
		/// <value>The name of the games mode currently being managed.</value>
		public string GameModeName
		{
			get
			{
				return GameModeDescriptor.Name;
			}
		}

		/// <summary>
		/// Gets or sets the directory where installation information is stored for this game mode.
		/// </summary>
		/// <remarks>
		/// This is where install logs, overwrites, and the like are stored.
		/// </remarks>
		/// <value>The directory where installation information is stored for this game mode.</value>
		public string InstallInfoDirectory
		{
			get
			{
				return m_strInstallInfoDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strInstallInfoDirectory, value, () => InstallInfoDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where this Game Mode's mods are stored.
		/// </summary>
		/// <value>The path of the directory where this Game Mode's mods are stored.</value>
		public string ModDirectory
		{
			get
			{
				return m_strModDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strModDirectory, value, () => ModDirectory);
			}
		}

		/// <summary>
		/// Gets the validation errors for the current values.
		/// </summary>
		/// <value>The validation errors for the current values.</value>
		public ErrorContainer Errors { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the current game mode.</param>
		public RequiredDirectoriesControlVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			Errors = new ErrorContainer();
		}

		#endregion

		#region Validation

		/// <summary>
		/// Validates the specified directory.
		/// </summary>
		/// <returns><c>true</c> if the specified directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strPath, string p_strPathName, string p_strProperty)
		{
			Errors.Clear(p_strProperty);
			if (String.IsNullOrEmpty(p_strPath))
			{
				Errors.SetError(p_strProperty, String.Format("You must select a {0}.", p_strPathName));
				return false;
			}
			return true;
		}

		/// <summary>
		/// Validates the selected mod directory.
		/// </summary>
		/// <returns><c>true</c> if the selected mod directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateModDirectory()
		{
			return ValidateDirectory(ModDirectory, "Mod Directory", ObjectHelper.GetPropertyName(() => ModDirectory));
		}

		/// <summary>
		/// Validates the selected install info directory.
		/// </summary>
		/// <returns><c>true</c> if the selected install info directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateInstallInfoDirectory()
		{
			return ValidateDirectory(InstallInfoDirectory, "Install Info Directory", ObjectHelper.GetPropertyName(() => InstallInfoDirectory));
		}

		/// <summary>
		/// Validates the settings on this control.
		/// </summary>
		/// <returns><c>true</c> if the settings are valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateSettings()
		{
			return ValidateModDirectory() && ValidateInstallInfoDirectory();
		}


		#endregion

		/// <summary>
		/// Loads the user's settings into the control.
		/// </summary>
		public void LoadSettings()
		{
			string strInstalationPath = EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId];
			string strDirectory = EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId];
			if (String.IsNullOrEmpty(strDirectory))
			{
				string strDefault = Path.Combine(strInstalationPath, "mods");
				if (strDefault.StartsWith(Path.Combine(Path.GetPathRoot(strDefault), "Program Files"), StringComparison.InvariantCultureIgnoreCase))
					strDefault = Path.Combine(Path.GetPathRoot(strInstalationPath), "Games\\" + GameModeDescriptor.ModeId + "\\mods"); ;
				strDirectory = strDefault;
			}
			ModDirectory = strDirectory;

			strDirectory = EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId];
			if (String.IsNullOrEmpty(strDirectory))
			{
				string strDefault = Path.Combine(strInstalationPath, "Install Info");
				if (strDefault.StartsWith(Path.Combine(Path.GetPathRoot(strDefault), "Program Files"), StringComparison.InvariantCultureIgnoreCase))
					strDefault = Path.Combine(Path.GetPathRoot(strInstalationPath), "Games\\" + GameModeDescriptor.ModeId + "\\Install Info"); ;
				strDirectory = strDefault;
			}
			InstallInfoDirectory = strDirectory;

			ValidateSettings();
		}

		/// <summary>
		/// Persists the settings on this control.
		/// </summary>
		public void SaveSettings()
		{
			EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId] = InstallInfoDirectory;
			EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId] = ModDirectory;
			EnvironmentInfo.Settings.Save();
		}
	}
}
