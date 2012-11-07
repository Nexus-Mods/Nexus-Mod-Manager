using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Games;
using Nexus.Client.Util;

namespace Nexus.Client.Settings
{
	/// <summary>
	/// The group of general settings.
	/// </summary>
	public class GeneralSettingsGroup : SettingsGroup
	{
		/// <summary>
		/// Describes a file type association with the client.
		/// </summary>
		public class FileAssociationSetting
		{
			#region Properties

			/// <summary>
			/// Gets or sets the extention of the file type association.
			/// </summary>
			/// <value>The extention of the file type association.</value>
			public string Extension { get; set; }

			/// <summary>
			/// Gets or sets the description of the association.
			/// </summary>
			/// <value>The description of the association.</value>
			public string Description { get; set; }

			/// <summary>
			/// Gets or sets whether the file is associated with the client.
			/// </summary>
			/// <value>Whether the file is associated with the client.</value>
			public bool IsAssociated { get; set; }

			#endregion

			#region Constructors

			/// <summary>
			/// A simple constructor that initializes the object with the given values.
			/// </summary>
			/// <param name="p_strExtension">The extention of the file type association.</param>
			/// <param name="p_strDescription">The description of the association.</param>
			/// <param name="p_booIsAssociated">Whether the file is associated with the client.</param>
			public FileAssociationSetting(string p_strExtension, string p_strDescription, bool p_booIsAssociated)
			{
				if (!p_strExtension.StartsWith("."))
					p_strExtension = "." + p_strExtension;
				Extension = p_strExtension;
				Description = p_strDescription;
				IsAssociated = p_booIsAssociated;
			}

			#endregion
		}

		private bool m_booCheckForNewMods = true;
		private bool m_booScanSubfoldersForMods = true;
		private bool m_booAddMissingModInfo = true;
		private bool m_booAddShellExtensions = true;
		private bool m_booAssociateNxmUrls = true;
		private bool m_booCheckForUpdatesOnStartup = true;
		private bool m_booCloseModManagerAfterGameLaunch = true;
		private Int32 m_intUpdateCheckInterval = 0;
		private Int32 m_intModVersionsCheckInterval = 0;

		#region Properties

		/// <summary>
		/// Gets the title of the settings group.
		/// </summary>
		/// <value>The title of the settings group.</value>
		public override string Title
		{
			get
			{
				return "General";
			}
		}

		/// <summary>
		/// Gets the enumeration of file types associated with the client.
		/// </summary>
		/// <value>Ehe enumeration of file types associated with the client.</value>
		public IEnumerable<FileAssociationSetting> FileAssociations { get; private set; }

		/// <summary>
		/// Gets whether or not file associations can be made.
		/// </summary>
		/// <value>Whether or not file associations can be made.</value>
		public bool CanAssociateFiles
		{
			get
			{
				return UacUtil.IsElevated;
			}
		}

		/// <summary>
		/// Gets or sets whether or not the client should check for new mod versions.
		/// </summary>
		/// <value>Whether or not the client should check for new mod versions.</value>
		public bool CheckForNewMods
		{
			get
			{
				return m_booCheckForNewMods;
			}
			set
			{
				SetPropertyIfChanged(ref m_booCheckForNewMods, value, () => CheckForNewMods);
			}
		}

		/// <summary>
		/// Gets or sets the interval (in days) to wait before checking for mod updates.
		/// </summary>
		/// <value>The interval (in days) to wait before checking for mod updates.</value>
		public Int32 ModVersionsCheckInterval
		{
			get
			{
				return m_intModVersionsCheckInterval;
			}
			set
			{
				SetPropertyIfChanged(ref m_intModVersionsCheckInterval, value, () => ModVersionsCheckInterval);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should add missing info to mods.
		/// </summary>
		/// <value>Whether the client should add missing info to mods.</value>
		public bool AddMissingModInfo
		{
			get
			{
				return m_booAddMissingModInfo;
			}
			set
			{
				SetPropertyIfChanged(ref m_booAddMissingModInfo, value, () => AddMissingModInfo);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should integrate with the explorer shell (right-click menu).
		/// </summary>
		/// <value>Whether the client should integrate with the explorer shell (right-click menu).</value>
		public bool AddShellExtensions
		{
			get
			{
				return m_booAddShellExtensions;
			}
			set
			{
				SetPropertyIfChanged(ref m_booAddShellExtensions, value, () => AddShellExtensions);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should be associated with NXM URLs.
		/// </summary>
		/// <value>Whether the client should be associated with NXM URLs.</value>
		public bool AssociateNxmUrl
		{
			get
			{
				return m_booAssociateNxmUrls;
			}
			set
			{
				SetPropertyIfChanged(ref m_booAssociateNxmUrls, value, () => AssociateNxmUrl);
			}
		}

		/// <summary>
		/// Gets or sets whether the client should check for updates on startup.
		/// </summary>
		/// <value>Whether the client should check for updates on startup.</value>
		public bool CheckForUpdatesOnStartup
		{
			get
			{
				return m_booCheckForUpdatesOnStartup;
			}
			set
			{
				SetPropertyIfChanged(ref m_booCheckForUpdatesOnStartup, value, () => CheckForUpdatesOnStartup);
			}
		}

		/// <summary>
		/// Gets or sets the interval (in days) to wait before checking for a program update.
		/// </summary>
		/// <value>The interval (in days) to wait before checking for a program update.</value>
		public Int32 UpdateCheckInterval
		{
			get
			{
				return m_intUpdateCheckInterval;
			}
			set
			{
				SetPropertyIfChanged(ref m_intUpdateCheckInterval, value, () => UpdateCheckInterval);
			}
		}

		/// <summary>
		/// Gets or sets whether to scan sub directories of the mod directory for mods.
		/// </summary>
		/// <value>Whether to scan sub directories of the mod directory for mods.</value>
		public bool ScanSubfoldersForMods
		{
			get
			{
				return m_booScanSubfoldersForMods;
			}
			set
			{
				SetPropertyIfChanged(ref m_booScanSubfoldersForMods, value, () => ScanSubfoldersForMods);
			}
		}

		/// <summary>
		/// Gets or sets whether the mod manager should be closed after a game is launched.
		/// </summary>
		/// <value>Whether the mod manager should be closed after a game is launched.</value>
		public bool CloseModManagerAfterGameLaunch
		{
			get
			{
				return m_booCloseModManagerAfterGameLaunch;
			}
			set
			{
				m_booCloseModManagerAfterGameLaunch = value;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GeneralSettingsGroup(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
			FileAssociations = new List<FileAssociationSetting>();
		}

		#endregion

		#region File Association

		/// <summary>
		/// Add a possible client programme file association.
		/// </summary>
		/// <param name="p_strExtension">The extension to allow to be associated with the client.</param>
		/// <param name="p_strDescription">A description of the file type.</param>
		public void AddFileAssociation(string p_strExtension, string p_strDescription)
		{
			((List<FileAssociationSetting>)FileAssociations).Add(new FileAssociationSetting(p_strExtension, p_strDescription, IsAssociated(p_strExtension)));
		}

		/// <summary>
		/// Determines if the specified file type is associated with the client.
		/// </summary>
		/// <param name="p_strExtension">The extension of the file type for which it is to be determined
		/// whether it is associated with the client.</param>
		/// <returns><c>true</c> if the file type is associated with the client;
		/// <c>false</c> otherwise.</returns>
		protected bool IsAssociated(string p_strExtension)
		{
			if (!p_strExtension.StartsWith("."))
				p_strExtension = "." + p_strExtension;
			string strFileId = p_strExtension.TrimStart('.').ToUpperInvariant() + "_File_Type";

			string key = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + p_strExtension, null, null) as string;
			return (strFileId.Equals(key));
		}

		/// <summary>
		/// Associates the specifed file type with the client.
		/// </summary>
		/// <param name="p_fasFileAssociation">The description of the file type association to create.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to create the association.</exception>
		protected void AssociateFile(FileAssociationSetting p_fasFileAssociation)
		{
			if (!UacUtil.IsElevated)
				throw new InvalidOperationException("You must have administrative privileges to change file associations.");

			string strFileId = p_fasFileAssociation.Extension.TrimStart('.').ToUpperInvariant() + "_File_Type";
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + p_fasFileAssociation.Extension, null, strFileId);
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + strFileId, null, p_fasFileAssociation.Description, RegistryValueKind.String);
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + strFileId + @"\DefaultIcon", null, Application.ExecutablePath + ",0", RegistryValueKind.String);
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + strFileId + @"\shell\open\command", null, "\"" + Application.ExecutablePath + "\" \"%1\"", RegistryValueKind.String);
		}

		/// <summary>
		/// Removes the association of the specifed file type with the client.
		/// </summary>
		/// <param name="p_fasFileAssociation">The description of the file type association to remove.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to remove the association.</exception>
		protected void UnassociateFile(FileAssociationSetting p_fasFileAssociation)
		{
			if (!UacUtil.IsElevated)
				throw new InvalidOperationException("You must have administrative privileges to change file associations.");

			string strFileId = p_fasFileAssociation.Extension.TrimStart('.').ToUpperInvariant() + "_File_Type";
			string[] strKeys = Registry.ClassesRoot.GetSubKeyNames();
			if (Array.IndexOf<string>(strKeys, strFileId) != -1)
			{
				Registry.ClassesRoot.DeleteSubKeyTree(strFileId);
				Registry.ClassesRoot.DeleteSubKeyTree(p_fasFileAssociation.Extension);
			}
		}

		#endregion

		#region URL Association

		/// <summary>
		/// Determines if the specified URL protocol is associated with the client.
		/// </summary>
		/// <param name="p_strUrlProtocol">The protocol of the URL for which it is to be determined
		/// whether it is associated with the client.</param>
		/// <returns><c>true</c> if the URL protocol is associated with the client;
		/// <c>false</c> otherwise.</returns>
		protected bool IsUrlAssociated(string p_strUrlProtocol)
		{
			string key = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + p_strUrlProtocol, null, null) as string;
			return !String.IsNullOrEmpty(key);
		}

		/// <summary>
		/// Associates the specifed URL protocol with the client.
		/// </summary>
		/// <param name="p_strUrlProtocol">The URL protocol for which to create an association.</param>
		/// <param name="p_strDescription">The description of the URL protocol.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to create the association.</exception>
		protected void AssociateUrl(string p_strUrlProtocol, string p_strDescription)
		{
			if (!UacUtil.IsElevated)
				throw new InvalidOperationException("You must have administrative privileges to change URL associations.");

			string strUrlId = "URL:" + p_strDescription;
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + p_strUrlProtocol, null, strUrlId, RegistryValueKind.String);
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + p_strUrlProtocol, "URL Protocol", "", RegistryValueKind.String);
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + p_strUrlProtocol + @"\DefaultIcon", null, Application.ExecutablePath + ",0", RegistryValueKind.String);
			Registry.SetValue(@"HKEY_CLASSES_ROOT\" + p_strUrlProtocol + @"\shell\open\command", null, "\"" + Application.ExecutablePath + "\" \"%1\"", RegistryValueKind.String);
		}

		/// <summary>
		/// Removes the association of the specifed URL protocol with the client.
		/// </summary>
		/// <param name="p_strUrlProtocol">The URL protocol for which to remove the association.</param>
		/// <exception cref="InvalidOperationException">Thrown if the user does not have sufficient priviledges
		/// to remove the association.</exception>
		protected void UnassociateUrl(string p_strUrlProtocol)
		{
			if (!UacUtil.IsElevated)
				throw new InvalidOperationException("You must have administrative privileges to change URL associations.");

			string[] strKeys = Registry.ClassesRoot.GetSubKeyNames();
			if (Array.IndexOf<string>(strKeys, p_strUrlProtocol) != -1)
				Registry.ClassesRoot.DeleteSubKeyTree(p_strUrlProtocol);
		}

		#endregion

		#region Shell Extensions

		/// <summary>
		/// Adds a shell extension for the file type represented by the specified key.
		/// </summary>
		/// <param name="p_strExtension">The extension of the file type for which to add a shell extension.</param>
		private void AddShellExtension(string p_strExtension)
		{
			string strKey = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + p_strExtension, null, null) as string;
			if (strKey == null)
				return;
			string strCommandKey = "Add_to_" + EnvironmentInfo.Settings.ModManagerName.Replace(' ', '_');
			Registry.SetValue("HKEY_CLASSES_ROOT\\" + strKey + "\\Shell\\" + strCommandKey, null, "Add to " + EnvironmentInfo.Settings.ModManagerName);
			Registry.SetValue("HKEY_CLASSES_ROOT\\" + strKey + "\\Shell\\" + strCommandKey + "\\command", null, "\"" + Application.ExecutablePath + "\" \"%1\"", RegistryValueKind.String);
		}

		/// <summary>
		/// Removes a shell extension for the file type represented by the specified key.
		/// </summary>
		/// <param name="p_strExtension">The key representing the file type for which to remove a shell extension.</param>
		private void RemoveShellExtension(string p_strExtension)
		{
			string strKey = Registry.GetValue(@"HKEY_CLASSES_ROOT\" + p_strExtension, null, null) as string;
			if (strKey == null)
				return;
			RegistryKey rk = Registry.ClassesRoot.OpenSubKey(strKey + "\\Shell", true);
			if (rk == null)
				return;
			string strCommandKey = "Add_to_" + EnvironmentInfo.Settings.ModManagerName.Replace(' ', '_');
			if (Array.IndexOf<string>(rk.GetSubKeyNames(), strCommandKey) != -1)
				rk.DeleteSubKeyTree(strCommandKey);
			rk.Close();
		}

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Loads the grouped setting values from the persistent store.
		/// </summary>
		public override void Load()
		{
			foreach (FileAssociationSetting fasFileAssociation in FileAssociations)
				fasFileAssociation.IsAssociated = IsAssociated(fasFileAssociation.Extension);

			string strKey = Registry.GetValue(@"HKEY_CLASSES_ROOT\.zip", null, null) as string;
			if (strKey == null)
				AddShellExtensions = false;
			else
				AddShellExtensions = (Registry.GetValue(String.Format("HKEY_CLASSES_ROOT\\{0}\\Shell\\Add_to_{1}", strKey, EnvironmentInfo.Settings.ModManagerName.Replace(' ', '_')), null, null) != null);

			AssociateNxmUrl = IsUrlAssociated("nxm");
			CheckForNewMods = EnvironmentInfo.Settings.CheckForNewModVersions;
			ModVersionsCheckInterval = EnvironmentInfo.Settings.ModVersionsCheckInterval;
			AddMissingModInfo = EnvironmentInfo.Settings.AddMissingInfoToMods;
			CheckForUpdatesOnStartup = EnvironmentInfo.Settings.CheckForUpdatesOnStartup;
			UpdateCheckInterval = EnvironmentInfo.Settings.UpdateCheckInterval;
			ScanSubfoldersForMods = EnvironmentInfo.Settings.ScanSubfoldersForMods;
			CloseModManagerAfterGameLaunch = EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch;
		}

		/// <summary>
		/// Persists the grouped setting values to the persistent store.
		/// </summary>
		/// <returns><c>true</c> if the settings were persisted;
		/// <c>false</c> otherwise.</returns>
		public override bool Save()
		{
			if (UacUtil.IsElevated)
			{
				foreach (FileAssociationSetting fasFileAssociation in FileAssociations)
					if (fasFileAssociation.IsAssociated)
						AssociateFile(fasFileAssociation);
					else
						UnassociateFile(fasFileAssociation);

				if (AssociateNxmUrl)
					AssociateUrl("nxm", "Nexus Mod");
				else
					UnassociateUrl("nxm");

				if (AddShellExtensions)
				{
					AddShellExtension(".zip");
					AddShellExtension(".rar");
					AddShellExtension(".7z");
				}
				else
				{
					RemoveShellExtension(".zip");
					RemoveShellExtension(".rar");
					RemoveShellExtension(".7z");
				}
			}

			EnvironmentInfo.Settings.CheckForNewModVersions = CheckForNewMods;
			EnvironmentInfo.Settings.ModVersionsCheckInterval = ModVersionsCheckInterval;
			EnvironmentInfo.Settings.AddMissingInfoToMods = AddMissingModInfo;
			EnvironmentInfo.Settings.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;
			EnvironmentInfo.Settings.UpdateCheckInterval = UpdateCheckInterval;
			EnvironmentInfo.Settings.ScanSubfoldersForMods = ScanSubfoldersForMods;
			EnvironmentInfo.Settings.CloseModManagerAfterGameLaunch = CloseModManagerAfterGameLaunch;
			EnvironmentInfo.Settings.Save();
			return true;
		}

		#endregion
	}
}
