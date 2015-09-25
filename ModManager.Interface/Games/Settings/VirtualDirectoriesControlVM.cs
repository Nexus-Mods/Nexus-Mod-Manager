using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using Microsoft.Win32;
using Nexus.Client.ModManagement;
using Nexus.Client.Util;
using Nexus.UI.Controls;

namespace Nexus.Client.Games.Settings
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that allow the selection of required directories.
	/// </summary>
	public class VirtualDirectoriesControlVM : ObservableObject
	{
		private string m_strVirtualDirectory = null;
		private string m_strLinkDirectory = null;
		private bool m_booRequiredTool = false;
		private bool m_booUseAdditionalChecks = false;

		#region Properties

		/// <summary>
		/// Gets the virtual mod activator.
		/// </summary>
		/// <value>The virtual mod activator.</value>
		public IVirtualModActivator VirtualActivator { get; private set; }

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
		public VirtualDirectoriesControlVM(IEnvironmentInfo p_eifEnvironmentInfo, IGameModeDescriptor p_gmdGameModeInfo, IVirtualModActivator p_ivaVirtualActivator)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameModeDescriptor = p_gmdGameModeInfo;
			VirtualActivator = p_ivaVirtualActivator;
			Errors = new ErrorContainer();
			m_booUseAdditionalChecks = true;
		}

		#endregion

		#region Validation

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
		/// Validates the specified directory.
		/// </summary>
		/// <returns><c>true</c> if the specified directory is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateDirectory(string p_strPath, string p_strPathName, string p_strProperty, bool p_booGameHDCheck)
		{
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
			else if (p_booGameHDCheck && (!CheckOnGameHD(p_strPath)))
			{
				Errors.SetError(p_strProperty, string.Format("You MUST set the {0} on the same HD as the usual mod install folder:" + Environment.NewLine +
					"HD - {1}" + Environment.NewLine,
					p_strPathName, Path.GetPathRoot(p_strPath)));
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
			}

			return true;
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
			return ValidateVirtualDirectory() && (!MultiHDInstall || ValidateLinkDirectory());
		}

		#endregion

		#region Registry

		/// <summary>
		/// Check the correct path on the Registry.
		/// </summary>
		/// <param name="strGameMode">The selected game mode.</param>
		/// <param name="strMods">The selected Mods path.</param>
		/// <param name="strInstallInfo">The selected Install Info path.</param>
		public void SaveRegistry(string p_strGameMode, string p_strVirtual, string p_strHDLink, bool p_booMultiHDInstall)
		{
			try
			{
				RegistryKey rkKey = null;
				string strNMMKey = @"SOFTWARE\NexusModManager\";
				string strGameKey = @"SOFTWARE\NexusModManager\" + p_strGameMode;

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
		

		/// <summary>
		/// Loads the user's settings into the control.
		/// </summary>
		public void LoadSettings()
		{
			string strRegGameMode = @"HKEY_LOCAL_MACHINE\SOFTWARE\NexusModManager\" + GameModeDescriptor.ModeId;
			string strRegVirtual = String.Empty;
			string strRegHDLink = String.Empty;
			bool? booRegMultiHDInstall = null;

			string strDirectory = null;
			string strRandomGameKey = String.Empty;
			bool booRetrieved = false;

			if (EnvironmentInfo.Settings.MultiHDInstall.ContainsKey(GameModeDescriptor.ModeId))
				MultiHDInstall = EnvironmentInfo.Settings.MultiHDInstall[GameModeDescriptor.ModeId];
			else if (booRegMultiHDInstall != null)
				MultiHDInstall = (bool)booRegMultiHDInstall;
			else if (booRegMultiHDInstall == null)
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
						strDirectory = Path.Combine(Path.Combine(Path.Combine(Path.GetPathRoot(GameModeDescriptor.InstallationPath), "Games"), EnvironmentInfo.Settings.ModManagerName), GameModeDescriptor.ModeId);
					}
					LinkDirectory = strDirectory;
				}
			}

			ValidateSettings();
		}

		protected bool CheckOnGameHD(string p_strPath)
		{
			return String.Equals(Path.GetPathRoot(p_strPath), Path.GetPathRoot(GameModeDescriptor.InstallationPath), StringComparison.InvariantCultureIgnoreCase);
		}

		/// <summary>
		/// Persists the settings on this control.
		/// </summary>
		public bool SaveSettings()
		{
			string strVirtual = String.Empty;
			string strLink = String.Empty;
			bool? booMulti = null;
			bool booChanged = false;

			if (!String.Equals(EnvironmentInfo.Settings.VirtualFolder[GameModeDescriptor.ModeId], VirtualDirectory))
			{
				strVirtual = VirtualDirectory;
				booChanged = true;
			}
			if (!String.Equals(EnvironmentInfo.Settings.HDLinkFolder[GameModeDescriptor.ModeId], LinkDirectory))
			{
				strLink = LinkDirectory;
				booChanged = true;
			}
			if (MultiHDInstall == !EnvironmentInfo.Settings.MultiHDInstall[GameModeDescriptor.ModeId])
			{
				booMulti = MultiHDInstall;
				booChanged = true;
			}

			if (booChanged)
				VirtualActivator.SetNewFolders(strVirtual, strLink, booMulti);

			return booChanged;
		}
	}
}
