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

namespace Nexus.Client.Games.FalloutNV
{
	/// <summary>
	/// The group of download settings.
	/// </summary>
	public class SupportedToolsSettingsGroup : SettingsGroup
	{
		private string m_strBOSSDirectory = null;
		private string m_strLOOTDirectory = null;

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
		/// Gets or sets the directory where LOOT is installed.
		/// </summary>
		/// <value>The directory where LOOT is installed.</value>
		public string LOOTDirectory
		{
			get
			{
				return m_strLOOTDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strLOOTDirectory, value, () => LOOTDirectory);
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
			GameModeDescriptor = p_gmGameMode;
			Errors = new ErrorContainer();
		}

		/// <summary>
		/// Validates the specified directory.
		/// </summary>
		/// <returns><c>true</c> if the specified directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strPath, string p_strPathName, string p_strProperty)
		{
			Errors.Clear(p_strProperty);
			if (String.IsNullOrWhiteSpace(p_strPath))
			{
				return true;
			}
			if (p_strPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
			{
				Errors.SetError(p_strProperty, String.Format("The selected path is not valid: {0}.", p_strPathName));
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
		/// Validates the selected LOOT directory.
		/// </summary>
		/// <returns><c>true</c> if the selected LOOT directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateLOOTDirectory()
		{
			return ValidateDirectory(LOOTDirectory, "LOOT Directory", ObjectHelper.GetPropertyName(() => LOOTDirectory));
		}

		/// <summary>
		/// Validates the settings on this control.
		/// </summary>
		/// <returns><c>true</c> if the settings are valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateSettings()
		{
			return ValidateBOSSDirectory() && ValidateLOOTDirectory();
		}

		#endregion

		/// <summary>
		/// Loads the user's settings into the control.
		/// </summary>
		public override void Load()
		{
			string strBOSSPath = String.Empty;
			string strBOSSReg = String.Empty;
			string strLOOTPath = String.Empty;
			string strLOOTReg = String.Empty;

			if (IntPtr.Size == 8)
				strBOSSReg = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\BOSS\";
			else
				strBOSSReg = @"HKEY_LOCAL_MACHINE\SOFTWARE\BOSS\";

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BOSS"))
				strBOSSPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BOSS"];

			if (String.IsNullOrEmpty(strBOSSPath))
				if (RegistryUtil.CanReadKey(strBOSSReg))
				{
					string strRegPath = (string)Registry.GetValue(strBOSSReg, "Installed Path", null);
					if (!String.IsNullOrWhiteSpace(strRegPath) && ((strRegPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strRegPath)))
					{
						strBOSSPath = String.Empty;
						EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BOSS"] = strBOSSPath;
						EnvironmentInfo.Settings.Save();
					}
					else
						strBOSSPath = strRegPath;
				}

			if (!String.IsNullOrEmpty(strBOSSPath) && Directory.Exists(strBOSSPath))
				BOSSDirectory = strBOSSPath;

			if (IntPtr.Size == 8)
				strLOOTReg = @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\LOOT\";
			else
				strLOOTReg = @"HKEY_LOCAL_MACHINE\SOFTWARE\LOOT\";

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("LOOT"))
				strLOOTPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["LOOT"];

			if (String.IsNullOrEmpty(strLOOTPath))
				if (RegistryUtil.CanReadKey(strLOOTReg))
				{
					string strRegPath = (string)Registry.GetValue(strLOOTReg, "Installed Path", null);
					if (!String.IsNullOrWhiteSpace(strRegPath) && ((strRegPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0) || !Directory.Exists(strRegPath)))
					{
						strLOOTPath = String.Empty;
						EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["LOOT"] = strLOOTPath;
						EnvironmentInfo.Settings.Save();
					}
					else
						strLOOTPath = strRegPath;
				}

			if (!String.IsNullOrEmpty(strLOOTPath) && Directory.Exists(strLOOTPath))
				LOOTDirectory = strLOOTPath;


			ValidateSettings();
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			if (!ValidateSettings())
				return false;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BOSS") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], BOSSDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BOSS"] = BOSSDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("LOOT") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], LOOTDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["LOOT"] = LOOTDirectory;

			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
