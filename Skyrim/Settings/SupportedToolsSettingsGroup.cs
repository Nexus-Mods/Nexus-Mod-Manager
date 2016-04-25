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

namespace Nexus.Client.Games.Skyrim
{
	/// <summary>
	/// The group of download settings.
	/// </summary>
	public class SupportedToolsSettingsGroup : SettingsGroup
	{
		private string m_strBOSSDirectory = null;
		private string m_strLOOTDirectory = null;
		private string m_strWryeBashDirectory = null;
		private string m_strFNISDirectory = null;
		private string m_strTES5EditDirectory = null;
		private string m_strBSDirectory = null;
		private string m_strDSRPDirectory = null;
		private string m_strPMDirectory = null;

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
		/// Gets or sets the path of the directory where FNIS is installed.
		/// </summary>
		/// <value>The path of the directory where FNIS is installed.</value>
		public string FNISDirectory
		{
			get
			{
				return m_strFNISDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strFNISDirectory, value, () => FNISDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where BodySlide is installed.
		/// </summary>
		/// <value>The path of the directory where BodySlide is installed.</value>
		public string BSDirectory
		{
			get
			{
				return m_strBSDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strBSDirectory, value, () => BSDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where Dual Sheat Redux Patch is installed.
		/// </summary>
		/// <value>The path of the directory where Dual Sheat Redux Patch is installed.</value>
		public string DSRPDirectory
		{
			get
			{
				return m_strDSRPDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strDSRPDirectory, value, () => DSRPDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where Patchus Maximus is installed.
		/// </summary>
		/// <value>The path of the directory where Patchus Maximus is installed.</value>
		public string PMDirectory
		{
			get
			{
				return m_strPMDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strPMDirectory, value, () => PMDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the directory where TES5Edit is installed.
		/// </summary>
		/// <value>The directory where TES5Edit is installed.</value>
		public string TES5EditDirectory
		{
			get
			{
				return m_strTES5EditDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strTES5EditDirectory, value, () => TES5EditDirectory);
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
		/// Checks if the specified directory are equals.
		/// </summary>
		/// <returns><c>true</c> if the specified directory are not equals;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strBOSSPath, string p_strBOSSPathName, string p_strBOSSProperty, string p_strWryeBashPath, string p_strWryeBashPathName, string p_strWryeBashProperty, string p_strFNISPath, string p_strFNISPathName, string p_strFNISProperty, string p_strBSPath, string p_strBSPathName, string p_strBSProperty, string p_strLOOTPath, string p_strLOOTPathName, string p_strLOOTProperty, string p_strTES5EditPath, string p_strTES5EditPathName, string p_strTES5EditProperty, string p_strDSRPPath, string p_strDSRPPathName, string p_strDSRPProperty, string p_strPMPath, string p_strPMPathName, string p_strPMProperty)
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
			Errors.Clear(p_strFNISProperty);
			if (String.IsNullOrEmpty(p_strFNISPath))
			{
				Errors.SetError(p_strFNISProperty, String.Format("You must select a {0}.", p_strFNISPathName));
				return false;
			}
			Errors.Clear(p_strBSProperty);
			if (String.IsNullOrEmpty(p_strBSPath))
			{
				Errors.SetError(p_strBSProperty, String.Format("You must select a {0}.", p_strBSPathName));
				return false;
			}
			Errors.Clear(p_strDSRPProperty);
			if (String.IsNullOrEmpty(p_strDSRPPath))
			{
				Errors.SetError(p_strDSRPProperty, String.Format("You must select a {0}.", p_strDSRPPathName));
				return false;
			}
			Errors.Clear(p_strPMProperty);
			if (String.IsNullOrEmpty(p_strPMPath))
			{
				Errors.SetError(p_strPMProperty, String.Format("You must select a {0}.", p_strPMPathName));
				return false;
			}
			Errors.Clear(p_strLOOTProperty);
			if (String.IsNullOrEmpty(p_strLOOTPath))
			{
				Errors.SetError(p_strLOOTProperty, String.Format("You must select a {0}.", p_strLOOTPathName));
				return false;
			}
			Errors.Clear(p_strTES5EditProperty);
			if (String.IsNullOrEmpty(p_strTES5EditPath))
			{
				Errors.SetError(p_strTES5EditProperty, String.Format("You must select a {0}.", p_strTES5EditPathName));
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
			if (String.IsNullOrWhiteSpace(p_strPath))
			{
				return true;
			}
			if (Directory.Exists(p_strPath) || File.Exists(p_strPath))
			{
				FileAttributes attr = File.GetAttributes(p_strPath);
				if (!attr.HasFlag(FileAttributes.Directory))
				{
					Errors.SetError(p_strProperty, String.Format("You need to select the FOLDER not the FILE: {0}.", p_strPathName));
					return false;
				}
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
		/// Validates the selected Wrye Bash directory.
		/// </summary>
		/// <returns><c>true</c> if the selected Wrye Bash directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateWryeBashDirectory()
		{
			return ValidateDirectory(WryeBashDirectory, "Wrye Bash Directory", ObjectHelper.GetPropertyName(() => WryeBashDirectory));
		}

		/// <summary>
		/// Validates the selected FNIS directory.
		/// </summary>
		/// <returns><c>true</c> if the selected FNIS directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateFNISDirectory()
		{
			return ValidateDirectory(FNISDirectory, "FNIS Directory", ObjectHelper.GetPropertyName(() => FNISDirectory));
		}

		/// <summary>
		/// Validates the selected BodySlide directory.
		/// </summary>
		/// <returns><c>true</c> if the selected BodySlide directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateBSDirectory()
		{
			return ValidateDirectory(BSDirectory, "BodySlide Directory", ObjectHelper.GetPropertyName(() => BSDirectory));
		}

		/// <summary>
		/// Validates the selected Dual Sheat Redux Patch directory.
		/// </summary>
		/// <returns><c>true</c> if the selected Dual Sheat Redux Patch directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDSRPDirectory()
		{
			return ValidateDirectory(DSRPDirectory, "Dual Sheat Redux Patch Directory", ObjectHelper.GetPropertyName(() => DSRPDirectory));
		}

		/// <summary>
		/// Validates the selected Patchus Maximus directory.
		/// </summary>
		/// <returns><c>true</c> if the selected Patchus Maximus directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidatePMDirectory()
		{
			return ValidateDirectory(PMDirectory, "Patchus Maximus Directory", ObjectHelper.GetPropertyName(() => PMDirectory));
		}

		/// <summary>
		/// Validates the selected TES5Edit directory.
		/// </summary>
		/// <returns><c>true</c> if the selected TES5Edit directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateTES5EditDirectory()
		{
			return ValidateDirectory(TES5EditDirectory, "TES5Edit Directory", ObjectHelper.GetPropertyName(() => TES5EditDirectory));
		}

		/// <summary>
		/// Validates the settings on this control.
		/// </summary>
		/// <returns><c>true</c> if the settings are valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateSettings()
		{
			return ValidateBOSSDirectory() && ValidateLOOTDirectory() && ValidateWryeBashDirectory() && ValidateFNISDirectory() && ValidateTES5EditDirectory() && ValidatePMDirectory() && ValidateDSRPDirectory() && ValidateBSDirectory();
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
			string strWryePath = String.Empty;
			string strFNISPath = String.Empty;
			string strBSPath = String.Empty;
			string strDSRPPath = String.Empty;
			string strPMPath = String.Empty;
			string strTES5EditPath = String.Empty;
			bool booReset = false;

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

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("WryeBash"))
			{
				strWryePath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["WryeBash"];
				if (!String.IsNullOrEmpty(strWryePath) && Directory.Exists(strWryePath))
					WryeBashDirectory = strWryePath;
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("FNIS"))
			{
				strFNISPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["FNIS"];

				if (String.IsNullOrEmpty(strFNISPath))
				{
					booReset = true;
				}
				else if (Directory.Exists(strFNISPath) || File.Exists(strFNISPath))
				{
					FileAttributes attr = File.GetAttributes(strFNISPath);
					booReset = !attr.HasFlag(FileAttributes.Directory);
				}
				else
					booReset = true;

				if (booReset)
				{
					string strFNIS = Path.Combine(GameModeDescriptor.InstallationPath, @"Data\tools\GenerateFNIS_for_Users");
					if (Directory.Exists(strFNIS))
						strFNISPath = strFNIS;
					else
						strFNISPath = String.Empty;

					booReset = false;
				}

				if (!String.IsNullOrEmpty(strFNISPath) && Directory.Exists(strFNISPath))
					FNISDirectory = strFNISPath;
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BS2"))
			{
				strBSPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BS2"];

				if (String.IsNullOrEmpty(strBSPath))
				{
					booReset = true;
				}
				else if (Directory.Exists(strBSPath) || File.Exists(strBSPath))
				{
					FileAttributes attr = File.GetAttributes(strBSPath);
					booReset = !attr.HasFlag(FileAttributes.Directory);
				}
				else
					booReset = true;

				if (booReset)
				{
					string strBS = Path.Combine(GameModeDescriptor.InstallationPath, @"Data\CalienteTools\BodySlide");
					if (Directory.Exists(strBS))
						strBSPath = strBS;
					else
						strBSPath = String.Empty;

					booReset = false;
				}

				if (!String.IsNullOrEmpty(strBSPath) && Directory.Exists(strBSPath))
					BSDirectory = strBSPath;
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("DSRP"))
			{
				strDSRPPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["DSRP"];

				if (String.IsNullOrEmpty(strDSRPPath))
				{
					booReset = true;
				}
				else if (Directory.Exists(strDSRPPath) || File.Exists(strDSRPPath))
				{
					FileAttributes attr = File.GetAttributes(strDSRPPath);
					booReset = !attr.HasFlag(FileAttributes.Directory);
				}
				else
					booReset = true;

				if (booReset)
				{
					string strDSRP = Path.Combine(GameModeDescriptor.InstallationPath, @"Data\SkyProc Patchers\T3nd0_PatchusMaximus");
					if (Directory.Exists(strDSRP))
						strDSRPPath = strDSRP;
					else
						strDSRPPath = String.Empty;

					booReset = false;
				}

				if (!String.IsNullOrEmpty(strDSRPPath) && Directory.Exists(strDSRPPath))
					DSRPDirectory = strDSRPPath;
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("PM"))
			{
				strPMPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["PM"];

				if (String.IsNullOrEmpty(strPMPath))
				{
					booReset = true;
				}
				else if (Directory.Exists(strPMPath) || File.Exists(strPMPath))
				{
					FileAttributes attr = File.GetAttributes(strPMPath);
					booReset = !attr.HasFlag(FileAttributes.Directory);
				}
				else
					booReset = true;

				if (booReset)
				{
					string strPM = Path.Combine(GameModeDescriptor.InstallationPath, @"Data\SkyProc Patchers\Dual Sheath Redux Patch");
					if (Directory.Exists(strPM))
						strPMPath = strPM;
					else
						strPMPath = String.Empty;

					booReset = false;
				}

				if (!String.IsNullOrEmpty(strPMPath) && Directory.Exists(strPMPath))
					PMDirectory = strPMPath;
			}

			if (EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("TES5Edit"))
			{
				strTES5EditPath = EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["TES5Edit"];
				if (!String.IsNullOrEmpty(strTES5EditPath) && Directory.Exists(strTES5EditPath))
					TES5EditDirectory = strTES5EditPath;
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
			if (!ValidateSettings())
				return false;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BOSS") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], BOSSDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BOSS"] = BOSSDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("LOOT") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], LOOTDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["LOOT"] = LOOTDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("WryeBash") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], WryeBashDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["WryeBash"] = WryeBashDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("FNIS") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], FNISDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["FNIS"] = FNISDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("BS2") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], BSDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["BS2"] = BSDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("DSRP") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], DSRPDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["DSRP"] = DSRPDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("PM") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], PMDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["PM"] = PMDirectory;

			if (!EnvironmentInfo.Settings.SupportedTools.ContainsKey(GameModeDescriptor.ModeId) || !EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId].ContainsKey("TES5Edit") || !String.Equals(EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId], TES5EditDirectory))
				EnvironmentInfo.Settings.SupportedTools[GameModeDescriptor.ModeId]["TES5Edit"] = TES5EditDirectory;

			EnvironmentInfo.Settings.Save();
			return true;
		}
	}
}
