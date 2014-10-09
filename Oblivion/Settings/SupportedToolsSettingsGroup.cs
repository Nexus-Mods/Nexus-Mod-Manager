using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.Util;
using Nexus.UI.Controls;
using Microsoft.Win32;
using System.IO;
using Nexus.Client.Settings;

namespace Nexus.Client.Games.Oblivion
{
	/// <summary>
	/// The group of download settings.
	/// </summary>
	public class SupportedToolsSettingsGroup : SettingsGroup
	{
		private string m_strBOSSDirectory = null;
		private string m_strWryeBashDirectory = null;

		#region Properties

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title
		{
			get
			{
				return "Supported Tools";
			}
		}

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
		/// Gets or sets the directory where BOSS is installed.
		/// </summary>
		/// <value>The directory where BOSS is installed.</value>
		public string BOSSDirectory
		{
			get
			{
				return m_strBOSSDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strBOSSDirectory, value, () => BOSSDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where Wrye Bash is installed.
		/// </summary>
		/// <value>The path of the directory where Wrye Bash is installed.</value>
		public string WryeBashDirectory
		{
			get
			{
				return m_strWryeBashDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strWryeBashDirectory, value, () => WryeBashDirectory);
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
		public SupportedToolsSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo, IGameMode p_gmGameMode)
			: base(p_eifEnvironmentInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmGameMode;
			Errors = new ErrorContainer();
		}

		/// <summary>
		/// Checks if the specified directory are equals.
		/// </summary>
		/// <returns><c>true</c> if the specified directory are not equals;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strBOSSPath, string p_strBOSSPathName, string p_strBOSSProperty, string p_strWryeBashPath, string p_strWryeBashPathName, string p_strWryeBashProperty)
		{
			Errors.Clear(p_strBOSSProperty);
			if (String.IsNullOrEmpty(p_strBOSSPath))
			{
				Errors.SetError(p_strBOSSProperty, String.Format("You must select a {0}.", p_strBOSSPathName));
				return false;
			}
			Errors.Clear(p_strWryeBashProperty);
			if (String.IsNullOrEmpty(p_strWryeBashPath))
			{
				Errors.SetError(p_strWryeBashProperty, String.Format("You must select a {0}.", p_strWryeBashPathName));
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
		/// Validates the selected BOSS directory.
		/// </summary>
		/// <returns><c>true</c> if the selected BOSS directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateBOSSDirectory()
		{
			return ValidateDirectory(BOSSDirectory, "BOSS Directory", ObjectHelper.GetPropertyName(() => BOSSDirectory));
		}

		/// <summary>
		/// Validates the selected Wrye Bash directory.
		/// </summary>
		/// <returns><c>true</c> if the selected Wrye Bash directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateWryeBashDirectory()
		{
			return ValidateDirectory(WryeBashDirectory, "Wrye Bash Directory", ObjectHelper.GetPropertyName(() => WryeBashDirectory));
		}

		/// <summary>
		/// Validates the settings on this control.
		/// </summary>
		/// <returns><c>true</c> if the settings are valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateSettings()
		{
			if (ValidateDirectory(BOSSDirectory, "BOSS Directory", ObjectHelper.GetPropertyName(() => BOSSDirectory), WryeBashDirectory, "Wrye Bash Directory", ObjectHelper.GetPropertyName(() => WryeBashDirectory)))
				return ValidateBOSSDirectory() && ValidateWryeBashDirectory();
			else
				return false;
		}

		#endregion

		/// <summary>
		/// Loads the user's settings into the control.
		/// </summary>
		public override void Load()
		{
			string strBOSSPath = String.Empty;
			string strBOSSReg = String.Empty;
			string strWryePath = String.Empty;

			if (IntPtr.Size == 8)
				strBOSSReg = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\BOSS\";
			else
				strBOSSReg = @"HKEY_LOCAL_MACHINE\SOFTWARE\BOSS\";

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BOSS"))
				strBOSSPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BOSS"];

			if (String.IsNullOrEmpty(strBOSSPath))
				if (RegistryUtil.CanReadKey(strBOSSReg))
					strBOSSPath = (string)Registry.GetValue(strBOSSReg, "Installed Path", null);

			if (!String.IsNullOrEmpty(strBOSSPath))
				BOSSDirectory = strBOSSPath;

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("WryeBash"))
			{
				strWryePath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["WryeBash"];
				if (!String.IsNullOrEmpty(strWryePath))
					WryeBashDirectory = strWryePath;
			}

			ValidateSettings();
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BOSS") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], BOSSDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BOSS"] = BOSSDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("WryeBash") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], WryeBashDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["WryeBash"] = WryeBashDirectory;

			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
