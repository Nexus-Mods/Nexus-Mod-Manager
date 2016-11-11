using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using Nexus.Client.Util;
using Nexus.UI.Controls;
using Microsoft.Win32;

namespace Nexus.Client.Games.Settings
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that allow the selection of required directories.
	/// </summary>
	public class SetupDirectoriesControlVM : ObservableObject
	{
		private string m_strInstallInfoDirectory = null;
		private string m_strModDirectory = null;
		private string m_strToolDirectory = null;
		private string m_strVirtualDirectory = null;
		private string m_strLinkDirectory = null;
		private bool m_booRequiredTool = false;
		private List<string> lstModsAttempts = new List<string>();
		private List<string> lstIIAttempts = new List<string>();
		private bool m_booUseAdditionalChecks = false;

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
		/// Gets or sets the path of the directory where this Game Mode's required tool is installed.
		/// </summary>
		/// <value>The path of the directory where this Game Mode's required tool is installed.</value>
		public string ToolDirectory
		{
			get
			{
				return m_strToolDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strToolDirectory, value, () => ToolDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where this Game Mode's Virtual mods are stored.
		/// </summary>
		/// <value>The path of the directory where this Game Mode's Virtual mods are stored.</value>
		public string VirtualDirectory
		{
			get
			{
				return m_strVirtualDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strVirtualDirectory, value, () => VirtualDirectory);
			}
		}

		/// <summary>
		/// Gets or sets the path of the directory where this Game Mode's Virtual mods are stored.
		/// </summary>
		/// <value>The path of the directory where this Game Mode's Virtual mods are stored.</value>
		public string LinkDirectory
		{
			get
			{
				return m_strLinkDirectory;
			}
			set
			{
				SetPropertyIfChanged(ref m_strLinkDirectory, value, () => LinkDirectory);
			}
		}

		/// <summary>
		/// Gets whether the game mode is using a required tool for modding.
		/// </summary>
		/// <value>Whether the game mode is using a required tool for modding.</value>
		public bool RequiredTool
		{
			get
			{
				return m_booRequiredTool;
			}
		}

		/// <summary>
		/// Gets whether to show additional settings.
		/// </summary>
		/// <value>Whether to show additional settings.</value>
		public bool OptionalSettings
		{
			get
			{
				return m_booUseAdditionalChecks;
			}
		}

		/// <summary>
		/// Gets whether to use the multi HD mod install.
		/// </summary>
		/// <value>Whether to use the multi HD mod install.</value>
		public bool MultiHDInstall { get; set; }

		/// <summary>
		/// Gets the name of the required tool (if any) for the current game mode.
		/// </summary>
		/// <value>The name of the required tool (if any) for the current game mode.</value>
		public string RequiredToolName 
		{
			get
			{
				return GameModeDescriptor.RequiredToolName;
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
		public SetupDirectoriesControlVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			m_booRequiredTool = GameModeDescriptor.OrderedRequiredToolFileNames != null;
			Errors = new ErrorContainer();
			m_booUseAdditionalChecks = false;
		}

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameModeInfo">The descriptor of the current game mode.</param>
		/// <param name="p_booUseAdditionalChecks">Whether to use additional checks to validate the folders.</param>
		public SetupDirectoriesControlVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo, bool p_booUseAdditionalChecks)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			m_booRequiredTool = GameModeDescriptor.OrderedRequiredToolFileNames != null;
			m_booUseAdditionalChecks = p_booUseAdditionalChecks;
			Errors = new ErrorContainer();
		}

		#endregion

		#region Validation

		/// <summary>
		/// Checks if the specified directory are equals.
		/// </summary>
		/// <returns><c>true</c> if the specified directory are not equals;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strModPath, string p_strModPathName, string p_strModProperty, string p_strInstallPath, string p_strInstallPathName, string p_strInstallProperty, string p_strToolPath, string p_strToolPathName, string p_strToolProperty)
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

			if (m_booUseAdditionalChecks)
			{
				if (!CheckCleanModsFolder(p_strModPath))
				{
					if ((CheckCleanInstallInfoFolder(p_strInstallPath)) && !lstIIAttempts.Contains(p_strInstallPath))
					{
						lstIIAttempts.Add(p_strInstallPath);
						Errors.SetError("WARNING", "the selected Install Info folder is empty, if you confirm this path your installed mods will show as uninstalled in NMM." +
							Environment.NewLine + "If this is a fresh install ignore this warning and continue.");
						return false;
					}
				}
				else
				{
					if (!CheckCleanInstallInfoFolder(p_strInstallPath) && !lstModsAttempts.Contains(p_strModPath))
					{
						lstModsAttempts.Add(p_strModPath);
						Errors.SetError("WARNING", "the selected Mods folder is empty but there's a mod install registry present in the selected Install Info folder." +
							" If you confirm this path NMM will prompt you to uninstall all installed mods whose archive is not present in the Mods folder." +
							Environment.NewLine + "If this is a fresh install ignore this warning and continue.");
						return false;
					}
					else if (CheckCleanInstallInfoFolder(p_strInstallPath) && (!lstIIAttempts.Contains(p_strInstallPath) || !lstModsAttempts.Contains(p_strModPath)))
					{
						lstIIAttempts.Add(p_strInstallPath);
						lstModsAttempts.Add(p_strModPath);
						Errors.SetError("WARNING", "The folders you selected are empty. If this is your first time running NMM for this game, or if you want a fresh install," +
							" then this isn't a problem and you can continue. If you have previously installed mods for this game via NMM then you should go back" +
							" and choose the correct folder paths that you previously setup in NMM.");
						return false;
					}
				}
			}
			
			if (m_booRequiredTool)
			{
				Errors.Clear(p_strToolProperty);
				if (!String.IsNullOrEmpty(p_strToolPath))
				{
					foreach (string ToolFile in GameModeDescriptor.OrderedRequiredToolFileNames)
						try
						{
							if (!File.Exists(Path.Combine(p_strToolPath, Path.GetFileName(ToolFile))))
							{
								Errors.SetError(p_strToolProperty, String.Format("The file {0} is not present in the selected path.", Path.GetFileName(ToolFile)));
								return false;
							}
						}
						catch
						{
							Errors.SetError(p_strToolProperty, String.Format("You must select a valid {0} path.", p_strToolPathName));
							return false;
						}
				}
			}

			return true;
		}

		/// <summary>
		/// Checks whether the selected Mods folder is clean.
		/// </summary>
		/// <returns><c>true</c> if the specified folder is clean;
		/// <c>false</c> otherwise.</returns>
		protected bool CheckCleanModsFolder(string p_strMods)
		{
			if (Directory.Exists(p_strMods))
			{
				if (Directory.Exists(Path.Combine(p_strMods, "cache")))
				{
					return ((Directory.GetFiles(p_strMods, "*.zip", SearchOption.TopDirectoryOnly).Length + Directory.GetFiles(p_strMods, "*.7z", SearchOption.TopDirectoryOnly).Length + Directory.GetFiles(p_strMods, "*.rar", SearchOption.TopDirectoryOnly).Length) <= 0);
				}
			}

			return true;
		}

		/// <summary>
		/// Checks whether the selected Mods folder is clean.
		/// </summary>
		/// <returns><c>true</c> if the specified folder is clean;
		/// <c>false</c> otherwise.</returns>
		protected bool CheckCleanInstallInfoFolder(string p_strInstallInfo)
		{
			if (Directory.Exists(p_strInstallInfo))
				return !(File.Exists(Path.Combine(p_strInstallInfo, "InstallLog.xml")));

			return true;
		}

		/// <summary>
		/// Validates the specified directory.
		/// </summary>
		/// <returns><c>true</c> if the specified directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strPath, string p_strPathName, string p_strProperty, bool p_booGameHDCheck)
		{
			string strExpected;
			Errors.Clear(p_strProperty);
			if (String.IsNullOrEmpty(p_strPath))
			{
				Errors.SetError(p_strProperty, String.Format("You must select a {0}.", p_strPathName));
				return false;
			}
			else if (
				(String.Equals(EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId], p_strPath)) ||
				((new DirectoryInfo(p_strPath).Parent) == null) ||
				(String.Equals(GameModeDescriptor.InstallationPath, p_strPath))
				)
			{
				Errors.SetError(p_strProperty, string.Format("You can't set the {0} equal to the following:" + Environment.NewLine +
					"HD root - {2}" + Environment.NewLine +
					"Game root folder - {1}" + Environment.NewLine +
					"Mod install folder - {3}" + Environment.NewLine,
					p_strPathName, EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId],
					Path.GetPathRoot(p_strPath), GameModeDescriptor.InstallationPath));
				return false;
			}
			else if (p_booGameHDCheck && (!CheckOnGameHD(p_strPath, out strExpected)))
			{
				Errors.SetError(p_strProperty, string.Format("You MUST set the {0} on the same HD as the usual mod install folder:" + Environment.NewLine +
					"Selected HD: {1} - Expected HD: {2}" + Environment.NewLine,
					p_strPathName, Path.GetPathRoot(p_strPath), strExpected ?? String.Empty));
				return false;
			}
			else
			{
				try
				{
					if (String.Equals(GameModeDescriptor.PluginDirectory, p_strPath))
					{
						Errors.SetError(p_strProperty, string.Format("You can't set the {0} equal to the plugin folder.", p_strPathName));
						return false;
					}
				}
				catch (ArgumentNullException)
				{
					// If the game doesn't supports plugins no need to check for it.
				}
				catch (DirectoryNotFoundException e)
				{
					Errors.SetError(p_strProperty, e.Message + Environment.NewLine + "If the drive no longer exists, uninstall NMM removing config files when asked and reinstall it.");
					return false;
				}
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
			return ValidateDirectory(ModDirectory, "Mod Directory", ObjectHelper.GetPropertyName(() => ModDirectory), false);
		}

		/// <summary>
		/// Validates the selected install info directory.
		/// </summary>
		/// <returns><c>true</c> if the selected install info directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateInstallInfoDirectory()
		{
			return ValidateDirectory(InstallInfoDirectory, "Install Info Directory", ObjectHelper.GetPropertyName(() => InstallInfoDirectory), false);
		}

		/// <summary>
		/// Validates the selected virtual directory.
		/// </summary>
		/// <returns><c>true</c> if the selected virtual directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateVirtualDirectory()
		{
			return ValidateDirectory(VirtualDirectory, "Virtual Directory", ObjectHelper.GetPropertyName(() => VirtualDirectory), !MultiHDInstall);
		}

		/// <summary>
		/// Validates the selected HD Link directory.
		/// </summary>
		/// <returns><c>true</c> if the selected HD Link directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateLinkDirectory()
		{
			return ValidateDirectory(LinkDirectory, "Link Directory", ObjectHelper.GetPropertyName(() => LinkDirectory), true);
		}

		/// <summary>
		/// Validates the settings on this control.
		/// </summary>
		/// <returns><c>true</c> if the settings are valid;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateSettings()
		{
			if (ValidateDirectory(ModDirectory, "Mod Directory", ObjectHelper.GetPropertyName(() => ModDirectory), InstallInfoDirectory, "Install Info Directory", ObjectHelper.GetPropertyName(() => InstallInfoDirectory), ToolDirectory, "Required Tool Directory", ObjectHelper.GetPropertyName(() => ToolDirectory)))
			{
				if (m_booUseAdditionalChecks)
				{
					return ValidateModDirectory() && ValidateInstallInfoDirectory() && ValidateVirtualDirectory() && (!MultiHDInstall || ValidateLinkDirectory());
				}
				else
					return ValidateModDirectory() && ValidateInstallInfoDirectory();
			}
			else
				return false;
		}

		#endregion

		#region Registry

		/// <summary>
		/// Check the correct path on the Registry.
		/// </summary>
		/// <param name="strGameMode">The selected game mode.</param>
		/// <param name="strMods">The selected Mods path.</param>
		/// <param name="strInstallInfo">The selected Install Info path.</param>
		public void SaveRegistry(string p_strGameMode, string p_strMods, string p_strInstallInfo, string p_strVirtual, string p_strHDLink, bool p_booMultiHDInstall)
		{
			try
			{
				RegistryKey rkKey = null;
				string strNMMKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\NexusModManager\";
				string strGameKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\NexusModManager\" + p_strGameMode;

				if (RegistryUtil.CanReadKey(strNMMKey) && RegistryUtil.CanWriteKey(strNMMKey))
				{
					rkKey = Registry.LocalMachine.OpenSubKey(strNMMKey, true);
					if (rkKey == null)
						if (RegistryUtil.CanCreateKey(strNMMKey))
							Registry.LocalMachine.CreateSubKey(strNMMKey);
				}

				if (rkKey != null)
				{
					if (RegistryUtil.CanCreateKey(strGameKey))
						Registry.LocalMachine.CreateSubKey(strGameKey);

					if (RegistryUtil.CanReadKey(strGameKey) && RegistryUtil.CanWriteKey(strGameKey))
					{
						rkKey = Registry.LocalMachine.OpenSubKey(strGameKey, true);
						rkKey.SetValue("Mods", p_strMods);
						rkKey.SetValue("InstallInfo", p_strInstallInfo);
						rkKey.SetValue("Virtual", p_strVirtual);
						rkKey.SetValue("HDLink", p_strHDLink);
						rkKey.SetValue("MultiHDInstall", p_booMultiHDInstall);
					}
				}
			}
			catch
			{
				return;
			}
		}

		#endregion

		protected bool CheckOnGameHD(string p_strPath)
		{
			return String.Equals(Path.GetPathRoot(p_strPath), (RequiredTool ? Path.GetPathRoot(ToolDirectory) : (Path.GetPathRoot(GameModeDescriptor.InstallationPath) ?? Path.GetPathRoot(Application.ExecutablePath))), StringComparison.InvariantCultureIgnoreCase);
		}

		protected bool CheckOnGameHD(string p_strPath, out string p_strExpected)
		{
			string strExpectedPath = RequiredTool ? ToolDirectory : GameModeDescriptor.InstallationPath;
			if (!String.IsNullOrWhiteSpace(strExpectedPath))
				p_strExpected = Path.GetPathRoot(strExpectedPath);
			else
				p_strExpected = String.Empty;
			return String.Equals(Path.GetPathRoot(p_strPath), RequiredTool ? Path.GetPathRoot(ToolDirectory) : Path.GetPathRoot(GameModeDescriptor.InstallationPath), StringComparison.InvariantCultureIgnoreCase);
		}

		#region Settings
		/// <summary>
		/// Loads the user's settings into the control.
		/// </summary>
		public void LoadSettings()
		{
			string strRegGameMode = @"HKEY_LOCAL_MACHINE\SOFTWARE\NexusModManager\" + GameModeDescriptor.ModeId;
			string strRegMods = String.Empty;
			string strRegInstallInfo = String.Empty;
			string strRegVirtual = String.Empty;
			string strRegHDLink = String.Empty;
			bool? booRegMultiHDInstall = null;
			if (RegistryUtil.CanReadKey(strRegGameMode))
			{
				RegistryKey rkKey = Registry.LocalMachine.OpenSubKey(strRegGameMode, false);
				if (rkKey != null)
				{
					strRegMods = (string)rkKey.GetValue("Mods", null);
					strRegInstallInfo = (string)rkKey.GetValue("InstallInfo", null);
					strRegVirtual = (string)rkKey.GetValue("Virtual", null);
					strRegHDLink = (string)rkKey.GetValue("HDLink", null);
					booRegMultiHDInstall = (bool?)rkKey.GetValue("MultiHDInstall", null);
				}
			}

			string strInstallationPath = EnvironmentInfo.Settings.InstallationPaths[GameModeDescriptor.ModeId];
			if (string.IsNullOrWhiteSpace(strInstallationPath))
				strInstallationPath = Application.ExecutablePath;

			string strDirectory = null;
			string strRandomGameKey = String.Empty;
			bool booRetrieved = false;

			if (EnvironmentInfo.Settings.ModFolder.ContainsKey(GameModeDescriptor.ModeId) && !String.IsNullOrEmpty(EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId]))
				ModDirectory = EnvironmentInfo.Settings.ModFolder[GameModeDescriptor.ModeId];
			else if (!String.IsNullOrEmpty(strRegMods))
				ModDirectory = strRegMods;
			else if (EnvironmentInfo.Settings.ModFolder.Keys.Count > 0)
			{
				strRandomGameKey = EnvironmentInfo.Settings.ModFolder.FirstOrDefault().Key;
				if (!String.IsNullOrEmpty(strRandomGameKey))
					strRegMods = EnvironmentInfo.Settings.ModFolder[strRandomGameKey];

				if (!String.IsNullOrEmpty(strRegMods))
				{
					try
					{
						if (strRegMods.IndexOf(strRandomGameKey) >= 0)
							ModDirectory = strRegMods.Replace(strRandomGameKey, GameModeDescriptor.ModeId);
						else
							ModDirectory = strRegMods.Replace(Path.GetFileName(strRegMods), GameModeDescriptor.ModeId + Path.DirectorySeparatorChar + Path.GetFileName(strRegMods));
					}
					catch { }
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
					string strDefault = Path.Combine(Path.GetPathRoot(strInstallationPath), "Games", EnvironmentInfo.Settings.ModManagerName, GameModeDescriptor.ModeId, "Mods");
					strDirectory = strDefault;
				}
				ModDirectory = strDirectory;
			}

			if (EnvironmentInfo.Settings.InstallInfoFolder.ContainsKey(GameModeDescriptor.ModeId) && !String.IsNullOrEmpty(EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId]))
				InstallInfoDirectory = EnvironmentInfo.Settings.InstallInfoFolder[GameModeDescriptor.ModeId];
			else if (!String.IsNullOrEmpty(strRegInstallInfo))
				InstallInfoDirectory = strRegInstallInfo;
			else if (EnvironmentInfo.Settings.ModFolder.Keys.Count > 0)
			{
				strRandomGameKey = EnvironmentInfo.Settings.InstallInfoFolder.FirstOrDefault().Key;
				if (String.IsNullOrEmpty(strRandomGameKey))
					strRegInstallInfo = EnvironmentInfo.Settings.InstallInfoFolder[strRandomGameKey];
				
				if (!String.IsNullOrEmpty(strRegInstallInfo))
				{
					try
					{
						if (strRegInstallInfo.IndexOf(strRandomGameKey) >= 0)
							InstallInfoDirectory = strRegInstallInfo.Replace(strRandomGameKey, GameModeDescriptor.ModeId);
						else
							InstallInfoDirectory = strRegInstallInfo.Replace(Path.GetFileName(strRegInstallInfo), GameModeDescriptor.ModeId + Path.DirectorySeparatorChar + Path.GetFileName(strRegInstallInfo));
					}
					catch { }
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
					string strDefault = Path.Combine(Path.GetPathRoot(strInstallationPath), "Games", EnvironmentInfo.Settings.ModManagerName, GameModeDescriptor.ModeId, "Install Info");
					strDirectory = strDefault;
				}
				InstallInfoDirectory = strDirectory;
			}

			if (m_booRequiredTool)
			{
				if (EnvironmentInfo.Settings.ToolFolder.ContainsKey(GameModeDescriptor.ModeId))
					ToolDirectory = EnvironmentInfo.Settings.ToolFolder[GameModeDescriptor.ModeId];
				if (string.IsNullOrEmpty(ToolDirectory))
				{
					strDirectory = null;
					booRetrieved = false;
					foreach (string ToolFile in GameModeDescriptor.OrderedRequiredToolFileNames)
						if (File.Exists(ToolFile))
							booRetrieved = true;
						else
						{
							booRetrieved = false;
							break;
						}

					if (booRetrieved)
						strDirectory = Path.GetDirectoryName(GameModeDescriptor.OrderedRequiredToolFileNames[0]);
					if (!String.IsNullOrEmpty(strDirectory))
						ToolDirectory = strDirectory;
				}
			}

			if (EnvironmentInfo.Settings.MultiHDInstall.ContainsKey(GameModeDescriptor.ModeId))
				MultiHDInstall = EnvironmentInfo.Settings.MultiHDInstall[GameModeDescriptor.ModeId];
			else if (booRegMultiHDInstall != null)
				MultiHDInstall = (bool)booRegMultiHDInstall;
			if (booRegMultiHDInstall == null)
			{
				if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameModeDescriptor.ModeId))
					booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].TryGetValue(String.Format("MultiHDInstall~{0}", GameModeDescriptor.ModeId), out strDirectory);
				if (booRetrieved)
					booRegMultiHDInstall = Convert.ToBoolean(strDirectory);
				if (booRegMultiHDInstall == null)
					booRegMultiHDInstall = false;
				MultiHDInstall = (bool)booRegMultiHDInstall;
			}

			if (EnvironmentInfo.Settings.VirtualFolder.ContainsKey(GameModeDescriptor.ModeId) && !String.IsNullOrEmpty(EnvironmentInfo.Settings.VirtualFolder[GameModeDescriptor.ModeId]))
				VirtualDirectory = EnvironmentInfo.Settings.VirtualFolder[GameModeDescriptor.ModeId];
			else if (!String.IsNullOrEmpty(strRegVirtual))
				VirtualDirectory = strRegVirtual;
			if (string.IsNullOrEmpty(VirtualDirectory) || 
				(!MultiHDInstall && (!CheckOnGameHD(VirtualDirectory))))
			{
				if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameModeDescriptor.ModeId))
					booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].TryGetValue(String.Format("VirtualFolder~{0}", GameModeDescriptor.ModeId), out strDirectory);
				if (!booRetrieved)
					EnvironmentInfo.Settings.VirtualFolder.TryGetValue(GameModeDescriptor.ModeId, out strDirectory);
				if (String.IsNullOrEmpty(strDirectory))
				{
					string strDefault = String.Empty;
					strDefault = ModDirectory;
					if (!MultiHDInstall && (!CheckOnGameHD(strDefault)))
						strDefault = Path.Combine(((m_booRequiredTool ? Path.GetPathRoot(ToolDirectory) : Path.GetPathRoot(GameModeDescriptor.InstallationPath)) ?? Path.GetPathRoot(Application.ExecutablePath)), "Games", EnvironmentInfo.Settings.ModManagerName, GameModeDescriptor.ModeId);

					strDirectory = strDefault;
				}
				VirtualDirectory = strDirectory;
			}

			if (MultiHDInstall)
			{
				if (EnvironmentInfo.Settings.HDLinkFolder.ContainsKey(GameModeDescriptor.ModeId) && !String.IsNullOrEmpty(EnvironmentInfo.Settings.HDLinkFolder[GameModeDescriptor.ModeId]))
					LinkDirectory = EnvironmentInfo.Settings.HDLinkFolder[GameModeDescriptor.ModeId];
				else if (!String.IsNullOrEmpty(strRegHDLink))
					LinkDirectory = strRegHDLink;
				if (string.IsNullOrEmpty(LinkDirectory) ||
					!CheckOnGameHD(LinkDirectory))
				{
					if (EnvironmentInfo.Settings.DelayedSettings.ContainsKey(GameModeDescriptor.ModeId))
						booRetrieved = EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].TryGetValue(String.Format("LinkFolder~{0}", GameModeDescriptor.ModeId), out strDirectory);
					if (!booRetrieved)
						EnvironmentInfo.Settings.HDLinkFolder.TryGetValue(GameModeDescriptor.ModeId, out strDirectory);
					if (String.IsNullOrEmpty(strDirectory) || !CheckOnGameHD(strDirectory))
					{
						strDirectory = Path.Combine(Path.GetPathRoot(GameModeDescriptor.InstallationPath ?? Path.GetPathRoot(Application.ExecutablePath)), "Games", EnvironmentInfo.Settings.ModManagerName, GameModeDescriptor.ModeId);
					}
					LinkDirectory = strDirectory;
				}
			}

			ValidateSettings();
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
			if (!String.Equals(EnvironmentInfo.Settings.VirtualFolder[GameModeDescriptor.ModeId], VirtualDirectory))
			{
				if (p_booDelaySettings)
					EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].Add(String.Format("VirtualFolder~{0}", GameModeDescriptor.ModeId), VirtualDirectory);
				else
					EnvironmentInfo.Settings.VirtualFolder[GameModeDescriptor.ModeId] = VirtualDirectory;
			}
			if (!String.Equals(EnvironmentInfo.Settings.HDLinkFolder[GameModeDescriptor.ModeId], LinkDirectory))
			{
				if (p_booDelaySettings)
					EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].Add(String.Format("HDLinkFolder~{0}", GameModeDescriptor.ModeId), LinkDirectory);
				else
					EnvironmentInfo.Settings.HDLinkFolder[GameModeDescriptor.ModeId] = LinkDirectory;
			}
			if (m_booRequiredTool)
			{
				if (!String.Equals(EnvironmentInfo.Settings.ToolFolder[GameModeDescriptor.ModeId], ToolDirectory))
				{
					EnvironmentInfo.Settings.ToolFolder[GameModeDescriptor.ModeId] = ToolDirectory;
				}
			}
			if (MultiHDInstall == !EnvironmentInfo.Settings.MultiHDInstall[GameModeDescriptor.ModeId])
			{
				if (p_booDelaySettings)
					EnvironmentInfo.Settings.DelayedSettings[GameModeDescriptor.ModeId].Add(String.Format("MultiHDInstall~{0}", GameModeDescriptor.ModeId), MultiHDInstall.ToString());
				else
					EnvironmentInfo.Settings.MultiHDInstall[GameModeDescriptor.ModeId] = MultiHDInstall;
			}

			SaveRegistry(GameModeDescriptor.ModeId, ModDirectory, InstallInfoDirectory, VirtualDirectory, LinkDirectory, MultiHDInstall);

			EnvironmentInfo.Settings.Save();
		}
		#endregion
	}
}
