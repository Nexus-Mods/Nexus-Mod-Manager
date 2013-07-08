using System;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using Nexus.Client.Util;
using Nexus.UI.Controls;
using Microsoft.Win32;

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
		/// Checks if the specified directory are equals.
		/// </summary>
		/// <returns><c>true</c> if the specified directory are not equals;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strModPath, string p_strModPathName, string p_strModProperty, string p_strInstallPath, string p_strInstallPathName, string p_strInstallProperty)
		{
			Errors.Clear(p_strModProperty);
			if (String.IsNullOrEmpty(p_strModPath))
			{
				Errors.SetError(p_strModProperty, String.Format("You must select a {0}.", p_strModPathName));
				return false;
			}
			Errors.Clear(p_strInstallProperty);
			if (String.IsNullOrEmpty(p_strInstallPath))
			{
				Errors.SetError(p_strInstallProperty, String.Format("You must select a {0}.", p_strInstallPathName));
				return false;
			}

			if (String.Equals(p_strModPath, p_strInstallPath))
			{
				Errors.SetError(p_strModProperty, string.Format("You can't set the {0} equal to the {1}.", p_strModPathName, p_strInstallPathName));
				return false;
			}
			return true;
		}

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
			else if (
				(String.Equals(EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId], p_strPath)) ||
				(p_strPath.Length <= 4) ||
				(String.Equals(GameModeDescriptor.PluginDirectory, p_strPath))
				)
			{
				Errors.SetError(p_strProperty, string.Format("You can't set the {0} equal to the following:" + Environment.NewLine +
					"HD root - {2}" + Environment.NewLine +
					"Game root folder - {1}" + Environment.NewLine +
					"Game plugin folder - {3}",
					p_strPathName, EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId],
					Path.GetPathRoot(p_strPath),
					GameModeDescriptor.PluginDirectory));
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
			if (ValidateDirectory(ModDirectory, "Mod Directory", ObjectHelper.GetPropertyName(() => ModDirectory), InstallInfoDirectory, "Install Info Directory", ObjectHelper.GetPropertyName(() => InstallInfoDirectory)))
				return ValidateModDirectory() && ValidateInstallInfoDirectory();
			else
				return false;
		}

		#endregion

		/// <summary>
		/// Loads the user's settings into the control.
		/// </summary>
		public void LoadSettings()
		{
			string strRegMod = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\NexusModManager\" + GameModeDescriptor.ModeId + " ", "Mods", null);
			string strRegInst = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\NexusModManager\" + GameModeDescriptor.ModeId + " ", "InstallInfo", null);

			string strInstalationPath = EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId];
			string strDirectory = null;
			string strRandomGameKey = String.Empty;
			bool booRetrieved = false;

			if (strRegMod != null)
				ModDirectory = strRegMod;
			else if (EnvironmentInfo.Settings.ModFolder.ContainsKey(GameModeDescriptor.ModeId))
				ModDirectory = EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId];
			else if (EnvironmentInfo.Settings.ModFolder.Keys.Count > 0)
			{
				strRandomGameKey = EnvironmentInfo.Settings.ModFolder.FirstOrDefault().Key;
				strRegMod = EnvironmentInfo.Settings.ModFolder[strRandomGameKey];
				if (!String.IsNullOrEmpty(strRegMod))
				{
					if (strRegMod.IndexOf(strRandomGameKey) >= 0)
						ModDirectory = strRegMod.Replace(strRandomGameKey, GameModeDescriptor.ModeId);
					else
						ModDirectory = strRegMod.Replace(Path.GetFileName(strRegMod), GameModeDescriptor.ModeId + Path.DirectorySeparatorChar + Path.GetFileName(strRegMod));
				}

			}
			if (string.IsNullOrEmpty(ModDirectory))
			{
				if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameModeDescriptor.ModeId))
					booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].TryGetValue(String.Format("ModFolder~{0}", GameModeDescriptor.ModeId), out strDirectory);
				if (!booRetrieved)
					EnvironmentInfo.Settings.ModFolder.TryGetValue(GameModeDescriptor.ModeId, out strDirectory);
				if (String.IsNullOrEmpty(strDirectory))
				{
					string strDefault = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.GetPathRoot(strInstalationPath), "Games"), EnvironmentInfo.Settings.ModManagerName), GameModeDescriptor.ModeId), "Mods");
					strDirectory = strDefault;
				}
				ModDirectory = strDirectory;
			}

			if (strRegInst != null)
				InstallInfoDirectory = strRegInst;
			else if (EnvironmentInfo.Settings.InstallInfoFolder.ContainsKey(GameModeDescriptor.ModeId))
				InstallInfoDirectory = EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId];
			else if (EnvironmentInfo.Settings.ModFolder.Keys.Count > 0)
			{
				if (String.IsNullOrEmpty(strRandomGameKey))
					strRandomGameKey = EnvironmentInfo.Settings.InstallInfoFolder.FirstOrDefault().Key;
				strRegInst = EnvironmentInfo.Settings.InstallInfoFolder[strRandomGameKey];
				if (!String.IsNullOrEmpty(strRegInst))
				{
					if (strRegInst.IndexOf(strRandomGameKey) >= 0)
						InstallInfoDirectory = strRegInst.Replace(strRandomGameKey, GameModeDescriptor.ModeId);
					else
						InstallInfoDirectory = strRegInst.Replace(Path.GetFileName(strRegInst), GameModeDescriptor.ModeId + Path.DirectorySeparatorChar + Path.GetFileName(strRegInst));
				}
			}
			if (String.IsNullOrEmpty(InstallInfoDirectory))
			{
				strDirectory = null;
				booRetrieved = false;
				if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameModeDescriptor.ModeId))
					booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].TryGetValue(String.Format("InstallInfoFolder~{0}", GameModeDescriptor.ModeId), out strDirectory);
				if (!booRetrieved)
					EnvironmentInfo.Settings.InstallInfoFolder.TryGetValue(GameModeDescriptor.ModeId, out strDirectory);
				if (String.IsNullOrEmpty(strDirectory))
				{
					string strDefault = Path.Combine(Path.Combine(Path.Combine(Path.Combine(Path.GetPathRoot(strInstalationPath), "Games"), EnvironmentInfo.Settings.ModManagerName), GameModeDescriptor.ModeId), "Install Info");
					strDirectory = strDefault;
				}
				InstallInfoDirectory = strDirectory;
			}

			ValidateSettings();
		}

		/// <summary>
		/// Check the correct path on the Registry.
		/// </summary>
		/// <param name="game">The game selected.</param>
		/// <param name="modDirectory">The Mods path selected.</param>
		/// <param name="installInfoDirectory">The Install Info path selected.</param>
		public void SaveRegistry(string game, string modDirectory, string installInfoDirectory)
		{
			try
			{
				RegistryKey myKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NexusModManager\", true);
				if (myKey == null)
					Registry.LocalMachine.CreateSubKey(@"SOFTWARE\NexusModManager\");

				Registry.LocalMachine.CreateSubKey(@"SOFTWARE\NexusModManager\" + game + " ");
				myKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\NexusModManager\" + game + " ", true);
				myKey.SetValue("Mods", modDirectory);
				myKey.SetValue("InstallInfo", installInfoDirectory);
			}
			catch
			{
				return;
			}
		}

		/// <summary>
		/// Persists the settings on this control.
		/// </summary>
		/// <param name="p_booDelaySettings">Whether the settings should be delayed until the next application restart.</param>
		public void SaveSettings(bool p_booDelaySettings)
		{
			if (!String.Equals(EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId], InstallInfoDirectory))
			{
				if (p_booDelaySettings)
					EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].Add(String.Format("InstallInfoFolder~{0}", GameModeDescriptor.ModeId), InstallInfoDirectory);
				else
					EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId] = InstallInfoDirectory;
			}
			if (!String.Equals(EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId], ModDirectory))
			{
				if (p_booDelaySettings)
					EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].Add(String.Format("ModFolder~{0}", GameModeDescriptor.ModeId), ModDirectory);
				else
					EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId] = ModDirectory;
			}

			SaveRegistry(GameModeDescriptor.ModeId, ModDirectory, InstallInfoDirectory);

			EnvironmentInfo.Settings.Save();
		}
	}
}
