using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Extensions;
using Nexus.Client.ModManagement.UI;
using Nexus.Client.ModRepositories;
using Nexus.Client.Mods;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using SevenZip;

namespace Nexus.Client.ModManagement
{
	public partial class ProfileManager : IProfileManager
	{
		#region Static Properties

		private static readonly Object m_objLock = new Object();
		private static readonly Version CURRENT_VERSION = new Version("0.1.0.0");
		public static readonly string PROFILE_FOLDER = "ModProfiles";
		public static readonly string PROFILE_FILE = "ProfileManagerCfg.xml";

		/// <summary>
		/// Gets the current support version of the category manager.
		/// </summary>
		/// <value>The current support version of the category manager.</value>
		public static Version CurrentVersion
		{
			get
			{
				return CURRENT_VERSION;
			}
		}

		/// <summary>
		/// Reads the version from the given profile file.
		/// </summary>
		/// <param name="p_strProfileConfig">The profile file whose version is to be read.</param>
		/// <returns>The version of the specified profile file, or a version of
		/// <c>0.0.0.0</c> if the file format is not recognized.</returns>
		public static Version ReadVersion(string p_strProfileConfig)
		{
			if (!File.Exists(p_strProfileConfig))
				return new Version("0.0.0.0");

			XDocument docProfile = XDocument.Load(p_strProfileConfig);

			XElement xelProfile = docProfile.Element("profileManager");
			if (xelProfile == null)
				return new Version("0.0.0.0");

			XAttribute xatVersion = xelProfile.Attribute("fileVersion");
			if (xatVersion == null)
				return new Version("0.0.0.0");

			return new Version(xatVersion.Value);
		}

		/// <summary>
		/// Determines if the profile file at the given path is valid.
		/// </summary>
		/// <param name="p_strProfileConfig">The path of the profile file to validate.</param>
		/// <returns><c>true</c> if the given config file is valid;
		/// <c>false</c> otherwise.</returns>
		protected static bool IsValid(string p_strProfileConfig)
		{
			if (!File.Exists(p_strProfileConfig))
				return false;
			try
			{
				XDocument docProfile = XDocument.Load(p_strProfileConfig);
			}
			catch (Exception e)
			{
				Trace.TraceError("Invalid Category File ({0}):", p_strProfileConfig);
				Trace.Indent();
				TraceUtil.TraceException(e);
				Trace.Unindent();
				return false;
			}
			return true;
		}

		#endregion

		private ThreadSafeObservableList<IModProfile> m_tslProfiles = new ThreadSafeObservableList<IModProfile>();
		private bool m_booInitialized = false;
		private bool m_booUsesPlugin = false;
		private string m_strProfileManagerPath = String.Empty;
		private string m_strProfileManagerConfigPath = String.Empty;
		private string m_strCurrentProfileId = String.Empty;

		#region Properties

		/// <summary>
		/// Gets the path of the directory where all of the mods are installed.
		/// </summary>
		/// <value>The path of the directory where all of the mods are installed.</value>
		protected string ModInstallDirectory { get; private set; }

		/// <summary>
		/// Gets the path of the directory where all of the mods are installed.
		/// </summary>
		/// <value>The path of the directory where all of the mods are installed.</value>
		protected IVirtualModActivator VirtualModActivator { get; private set; }

		/// <summary>
		/// Gets the mod manager to use.
		/// </summary>
		/// <value>The mod manager to use.</value>
		public ModManager ModManager { get; private set; }

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public bool IsValidPath
		{
			get
			{
				return IsValid(ModInstallDirectory);
			}
		}

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public IModProfile CurrentProfile
		{
			get
			{
				return m_tslProfiles.Find(x => x.Id == m_strCurrentProfileId);
			}
		}

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public ThreadSafeObservableList<IModProfile> ModProfiles
		{
			get
			{
				return m_tslProfiles;
			}
		}

		public string GetNextId
		{
			get
			{
				string RandomName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());
				while (m_tslProfiles.Find(x => x.Id == RandomName) != null)
					RandomName = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());

				return RandomName;
			}
		}

		/// <summary>
		/// Gets the list of categories.
		/// </summary>
		/// <value>The list of categories.</value>
		public bool Initialized 
		{
			get
			{
				return m_booInitialized;
			}
		}

		#endregion

		#region Singleton

		/// <summary>
		/// Releases the manager's hold on physical resources.
		/// </summary>
		public void Release()
		{
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_strModInstallDirectory">The path of the directory where all of the mods are installed.</param>
		/// <param name="p_strCategoryPath">The path from which to load the categories.</param>
		public ProfileManager(IVirtualModActivator p_ivaVirtualModActivator, ModManager p_mmgModManager, string p_strModInstallDirectory, bool p_booUsesPlugin)
		{
			VirtualModActivator = p_ivaVirtualModActivator;
			ModManager = p_mmgModManager;
			VirtualModActivator.ModActivationChanged += new EventHandler(VirtualModActivator_ModActivationChanged);
			ModManager.ActiveMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveMods_CollectionChanged);
			m_strProfileManagerPath = Path.Combine(p_strModInstallDirectory, PROFILE_FOLDER);
			m_strProfileManagerConfigPath = Path.Combine(m_strProfileManagerPath, PROFILE_FILE);
			m_booUsesPlugin = p_booUsesPlugin;
			Initialize();
			if (!Initialized)
				Setup();
		}

		public void Initialize()
		{
			if (IsValid(m_strProfileManagerConfigPath))
				if (ReadVersion(m_strProfileManagerConfigPath) == CURRENT_VERSION)
				{
					SetConfig(LoadConfig(m_strProfileManagerConfigPath));
					m_booInitialized = true;
				}
		}

		public void Setup()
		{
			SaveConfig();
			SetConfig(LoadConfig(m_strProfileManagerConfigPath));
			m_booInitialized = true;
		}
		#endregion

		#region Profile Management

		public void SaveConfig()
		{
			XDocument docProfile = new XDocument();
			XElement xelRoot = new XElement("profileManager", new XAttribute("fileVersion", CURRENT_VERSION));
			docProfile.Add(xelRoot);

			XElement xelProfiles = new XElement("profileList");
			xelRoot.Add(xelProfiles);
			xelProfiles.Add(from prf in m_tslProfiles
							select new XElement("profile",
								new XAttribute("profileId", prf.Id),
								new XAttribute("profileName", prf.Name),
								new XAttribute("isDefault", prf.IsDefault ? "1" : "0"),
								new XElement("gameModeId",
									new XText(prf.GameModeId)),
								new XElement("modCount",
									new XText(prf.ModCount.ToString()))));

			if (!Directory.Exists(m_strProfileManagerPath))
				Directory.CreateDirectory(m_strProfileManagerPath);
			docProfile.Save(m_strProfileManagerConfigPath);
		}

		public void SetConfig(ThreadSafeObservableList<IModProfile> p_iplProfiles)
		{
			m_tslProfiles.Clear();
			m_tslProfiles = new ThreadSafeObservableList<IModProfile>(p_iplProfiles);
		}

		public ThreadSafeObservableList<IModProfile> LoadConfig(string p_strXMLFilePath)
		{
			ThreadSafeObservableList<IModProfile> lstProfiles = new ThreadSafeObservableList<IModProfile>();

			if (File.Exists(p_strXMLFilePath))
			{
				XDocument docProfile = XDocument.Load(p_strXMLFilePath);
				string strVersion = docProfile.Element("profileManager").Attribute("fileVersion").Value;
				if (!CURRENT_VERSION.ToString().Equals(strVersion))
					throw new Exception(String.Format("Invalid Profile Manager version: {0} Expecting {1}", strVersion, CURRENT_VERSION));

				try
				{
					XElement xelProfiles = docProfile.Descendants("profileList").FirstOrDefault();
					if ((xelProfiles != null) && xelProfiles.HasElements)
					{
						foreach (XElement xelProfile in xelProfiles.Elements("profile"))
						{
							string strProfileId = xelProfile.Attribute("profileId").Value;
							string strProfileName = xelProfile.Attribute("profileName").Value;
							bool booDefault = (xelProfile.Attribute("isDefault").Value == "1") ? true : false;
							string strGameModeId = xelProfile.Element("gameModeId").Value;
							Int32 intModCount = Int32.TryParse(xelProfile.Element("modCount").Value, out intModCount) ? intModCount : 0;
							lstProfiles.Add(new ModProfile(strProfileId, strProfileName, strGameModeId, intModCount, booDefault));
							if (booDefault)
								m_strCurrentProfileId = strProfileId;
						}
					}
				}
				catch { }
			}

			return lstProfiles;
		}

		/// <summary>
		/// Adds a profile to the profile manager.
		/// </summary>
		/// <remarks>
		/// Adding a profile to the profile manager assigns it a unique key.
		/// </remarks>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		public IModProfile AddProfile(byte[] p_bteLoadOrder, string p_strGameModeId, string[] p_strOptionalFiles)
		{
			return AddProfile(null, null, p_bteLoadOrder, p_strGameModeId, -1, p_strOptionalFiles);
		}

		/// <summary>
		/// Adds a profile to the profile manager.
		/// </summary>
		/// <remarks>
		/// Adding a profile to the profile manager assigns it a unique key.
		/// </remarks>
		/// <param name="p_mctCategory">The <see cref="IModCategory"/> being added.</param>
		public IModProfile AddProfile(byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string p_strGameModeId, Int32 p_intModCount, string[] p_strOptionalFiles)
		{
			string strId = GetNextId;
			int intNewProfile = 1;
			if (m_tslProfiles.Count > 0)
			{
				List<IModProfile> lstNewProfile = m_tslProfiles.Where(x => x.Name.IndexOf("Profile") == 0).ToList();
				if ((lstNewProfile != null) && (lstNewProfile.Count > 0))
				{
					List<Int32> lstID = new List<Int32>();
					foreach (IModProfile imp in lstNewProfile)
					{
						string n = imp.Name.Substring(8);
						int i = 0;
						if (int.TryParse(n, out i))
							lstID.Add(Convert.ToInt32(i));
					}
					if (lstID.Count > 0)
					{
						intNewProfile = Enumerable.Range(1, lstID.Max() + 1).Except(lstID).Min();
					}
				}
			}
			ModProfile mprModProfile = new ModProfile(strId, "Profile " + intNewProfile.ToString(), p_strGameModeId, (p_intModCount < 0 ? VirtualModActivator.ModCount : p_intModCount));
			mprModProfile.IsDefault = true;
			SaveProfile(mprModProfile, p_bteModList, p_bteIniList, p_bteLoadOrder, p_strOptionalFiles);
			m_tslProfiles.Add(mprModProfile);
			m_strCurrentProfileId = mprModProfile.Id;
			SetDefaultProfile(mprModProfile);
			SaveConfig();
			string strLogPath = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted");
			if (Directory.Exists(strLogPath))
				lock (m_objLock)
					DirectoryCopy(strLogPath, Path.Combine(m_strProfileManagerPath, mprModProfile.Id, "Scripted"), true);
			return mprModProfile;
		}

		private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
		{
			DirectoryInfo dir = new DirectoryInfo(sourceDirName);
			DirectoryInfo[] dirs = dir.GetDirectories();

			// If the source directory does not exist, throw an exception.
			if (!dir.Exists)
				return;

			// If the destination directory does not exist, create it.
			if (!Directory.Exists(destDirName))
			{
				Directory.CreateDirectory(destDirName);
			}


			// Get the file contents of the directory to copy.
			FileInfo[] files = dir.GetFiles();

			foreach (FileInfo file in files)
			{
				// Create the path to the new copy of the file.
				string temppath = Path.Combine(destDirName, file.Name);

				// Copy the file.
				file.CopyTo(temppath, false);
			}

			// If copySubDirs is true, copy the subdirectories.
			if (copySubDirs)
			{

				foreach (DirectoryInfo subdir in dirs)
				{
					// Create the subdirectory.
					string temppath = Path.Combine(destDirName, subdir.Name);

					// Copy the subdirectories.
					DirectoryCopy(subdir.FullName, temppath, copySubDirs);
				}
			}
		}

		/// <summary>
		/// Creates and archives the profile backup.
		/// </summary>
		public bool BackupProfile(byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string p_strGameModeId, Int32 p_intModCount, string[] p_strOptionalFiles)
		{
			string strId = GetNextId;
			int intNewProfile = 1;
			if (m_tslProfiles.Count > 0)
			{
				List<IModProfile> lstNewProfile = m_tslProfiles.Where(x => x.Name.IndexOf("Profile") == 0).ToList();
				if ((lstNewProfile != null) && (lstNewProfile.Count > 0))
				{
					List<Int32> lstID = new List<Int32>();
					foreach (IModProfile imp in lstNewProfile)
					{
						string n = imp.Name.Substring(8);
						int i = 0;
						if (int.TryParse(n, out i))
							lstID.Add(Convert.ToInt32(i));
					}
					if (lstID.Count > 0)
					{
						intNewProfile = Enumerable.Range(1, lstID.Max() + 1).Except(lstID).Min();
					}
				}
			}
			ModProfile mprModProfile = new ModProfile(strId, "Profile " + intNewProfile.ToString(), p_strGameModeId, (p_intModCount < 0 ? VirtualModActivator.ModCount : p_intModCount));
			mprModProfile.IsDefault = true;
			SaveProfile(mprModProfile, p_bteModList, p_bteIniList, p_bteLoadOrder, p_strOptionalFiles);
			string strLogPath = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted");
			if (Directory.Exists(strLogPath))
				lock (m_objLock)
					DirectoryCopy(strLogPath, Path.Combine(m_strProfileManagerPath, mprModProfile.Id, "Scripted"), true);

			ExportProfile(mprModProfile, Path.Combine(VirtualModActivator.VirtualPath, "Backup_DONOTDELETE.zip"));

			if (Directory.Exists(Path.Combine(m_strProfileManagerPath, mprModProfile.Id)))
				FileUtil.ForceDelete(Path.Combine(m_strProfileManagerPath, mprModProfile.Id));

			return true;
		}

		/// <summary>
		/// Adds the backup profile to the profile manager.
		/// </summary>
		public bool RestoreBackupProfile(string p_strGameModeId, out string p_strErrMessage)
		{
			string strId = GetNextId;
			int intNewProfile = 1;
			p_strErrMessage = String.Empty;

			if (m_tslProfiles.Count > 0)
			{
				List<IModProfile> lstNewProfile = m_tslProfiles.Where(x => x.Name.IndexOf("Backup Profile") == 0).ToList();
				if ((lstNewProfile != null) && (lstNewProfile.Count > 0))
				{
					List<Int32> lstID = new List<Int32>();
					foreach (IModProfile imp in lstNewProfile)
					{
						string n = imp.Name.Substring(8);
						int i = 0;
						if (int.TryParse(n, out i))
							lstID.Add(Convert.ToInt32(i));
					}
					if (lstID.Count > 0)
					{
						intNewProfile = Enumerable.Range(1, lstID.Max() + 1).Except(lstID).Min();
					}
				}
			}
			ModProfile mprModProfile = new ModProfile(strId, "Backup Profile " + intNewProfile.ToString(), p_strGameModeId, 0);
			SaveProfile(mprModProfile, null, null, null, null);
			p_strErrMessage = mprModProfile.Name;
			string strBackupProfileArchivePath = Path.Combine(VirtualModActivator.VirtualPath, "Backup_DONOTDELETE.zip");
			string strBackupProfilePath = Path.Combine(m_strProfileManagerPath, mprModProfile.Id);
			if (File.Exists(strBackupProfileArchivePath))
			{
				try
				{
					using (StreamReader srArchive = new StreamReader(strBackupProfileArchivePath))
					{
						using (ZipArchive zaArchive = new ZipArchive(srArchive.BaseStream))
						{
							zaArchive.ExtractToDirectory(strBackupProfilePath, true);
						}
					}
				}
				catch (Exception e)
				{
					p_strErrMessage = e.Message;
					return false;
				}
			}
			else
			{
				p_strErrMessage = "The backup files does not exist: " + strBackupProfileArchivePath;
				return false;
			}

			m_tslProfiles.Add(mprModProfile);
			SaveConfig();

			return true;
		}

		/// <summary>
		/// Updates the profile.
		/// </summary>
		public void UpdateProfile(IModProfile p_impModProfile, byte[] p_bteIniEdits, byte[] p_bteLoadOrder, string[] p_strOptionalFiles)
		{
			UpdateCurrentProfileModCount();
			SaveProfile(p_impModProfile, null, p_bteIniEdits, p_bteLoadOrder, p_strOptionalFiles);
			m_tslProfiles.Remove(CurrentProfile);
			m_tslProfiles.Add(p_impModProfile);
			SaveConfig();
		}

		/// <summary>
		/// Updates the profile file.
		/// </summary>
		public void UpdateProfileLoadOrder(IModProfile p_impModProfile, byte[] p_bteLoadOrder)
		{
			if (p_impModProfile != null)
			{
				string strProfilePath = Path.Combine(m_strProfileManagerPath, p_impModProfile.Id);

				if (!Directory.Exists(strProfilePath))
					Directory.CreateDirectory(strProfilePath);

				if ((m_booUsesPlugin) && (p_bteLoadOrder != null) && (p_bteLoadOrder.Length > 0))
					File.WriteAllBytes(Path.Combine(strProfilePath, "loadorder.txt"), p_bteLoadOrder);
			}
		}

		public bool LoadProfileFileList(IModProfile p_impModProfile)
		{
			List<IVirtualModLink> lstModLinks;
			List<IVirtualModInfo> lstModList;
			string strPath = Path.Combine(m_strProfileManagerPath, Path.Combine(p_impModProfile.Id, "modlist.xml"));
			bool booSuccess = VirtualModActivator.LoadListOnDemand(strPath, out lstModLinks, out lstModList);

			p_impModProfile.UpdateLists(lstModLinks, lstModList);

			return booSuccess;
		}

		/// <summary>
		/// Removes the profile from the profile manager.
		/// </summary>
		/// <param name="p_impModProfile">The profile to remove.</param>
		public void RemoveProfile(IModProfile p_impModProfile)
		{
			if (Directory.Exists(Path.Combine(m_strProfileManagerPath, p_impModProfile.Id)))
				FileUtil.ForceDelete(Path.Combine(m_strProfileManagerPath, p_impModProfile.Id));

			m_tslProfiles.Remove(p_impModProfile);
			SaveConfig();
		}

		public void SetDefaultProfile(IModProfile p_impProfile)
		{
			foreach (IModProfile Profile in m_tslProfiles)
			{
				if (Profile == p_impProfile)
					Profile.IsDefault = true;
				else
					Profile.IsDefault = false;
			}
		}

		public void SetCurrentProfile(IModProfile p_impProfile)
		{
			if (p_impProfile != null)
				m_strCurrentProfileId = p_impProfile.Id;
			else
				m_strCurrentProfileId = null;
		}

		/// <summary>
		/// Updates the profile file.
		/// </summary>
		public void SaveProfile(IModProfile p_impModProfile, byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string[] p_strOptionalFiles)
		{
			if (p_impModProfile != null)
			{
				string strProfilePath = Path.Combine(m_strProfileManagerPath, p_impModProfile.Id);

				if (!Directory.Exists(strProfilePath))
					Directory.CreateDirectory(strProfilePath);

				if ((m_booUsesPlugin) && (p_bteLoadOrder != null) && (p_bteLoadOrder.Length > 0))
					File.WriteAllBytes(Path.Combine(strProfilePath, "loadorder.txt"), p_bteLoadOrder);

				if ((p_bteModList != null) && (p_bteModList.Length > 0))
					File.WriteAllBytes(Path.Combine(strProfilePath, "modlist.xml"), p_bteModList);
				else if (VirtualModActivator.VirtualLinks != null)
				{
					if (VirtualModActivator.ModCount > 0)
						VirtualModActivator.SaveModList(Path.Combine(strProfilePath, "modlist.xml"));
					else
						FileUtil.ForceDelete(Path.Combine(strProfilePath, "modlist.xml"));
				}

				if ((p_bteIniList != null) && (p_bteIniList.Length > 0))
					File.WriteAllBytes(Path.Combine(strProfilePath, "IniEdits.xml"), p_bteIniList);

				File.WriteAllBytes(Path.Combine(strProfilePath, "profile.xml"), GetProfileBytes(p_impModProfile));

				FileUtil.ForceDelete(Path.Combine(strProfilePath, "Optional"));
				if ((p_strOptionalFiles != null) && (p_strOptionalFiles.Length > 0))
				{
					string strOptionalFolder = Path.Combine(strProfilePath, "Optional");
					if (!Directory.Exists(strOptionalFolder))
						Directory.CreateDirectory(strOptionalFolder);

					foreach (string strFile in p_strOptionalFiles)
						File.Copy(strFile, Path.Combine(strOptionalFolder, Path.GetFileName(strFile)), true);
				}
			}
		}

		private byte[] GetProfileBytes(IModProfile p_impModProfile)
		{
			XElement xelProfile = new XElement("profile",
					new XAttribute("profileId", p_impModProfile.Id),
					new XAttribute("profileName", p_impModProfile.Name),
					new XAttribute("isDefault", p_impModProfile.IsDefault ? "1" : "0"),
					new XElement("gameModeId",
						new XText(p_impModProfile.GameModeId)),
					new XElement("modCount",
						new XText(p_impModProfile.ModCount.ToString())));

			return System.Text.Encoding.UTF8.GetBytes(xelProfile.ToString());
		}

		private MemoryStream GetModFilesStream(XDocument p_xdcModList)
		{
			MemoryStream ms = new MemoryStream();
			XmlWriterSettings xws = new XmlWriterSettings();
			xws.OmitXmlDeclaration = true;
			xws.Indent = true;;

			using (XmlWriter xw = XmlWriter.Create(ms, xws))
			{
				p_xdcModList.WriteTo(xw);
				xw.Flush();
				ms.Position = 0;
				return ms;
			}
		}

		/// <summary>
		/// Loads the selected profile files from the hd.
		/// </summary>
		public void LoadProfile(IModProfile p_impModProfile, out Dictionary<string, string> p_dicFileStream)
		{
			p_dicFileStream = new Dictionary<string, string>();
			string strLoadOrder = String.Empty;
			string strModList = String.Empty;

			string strProfilePath = Path.Combine(m_strProfileManagerPath, p_impModProfile.Id);

			if (m_booUsesPlugin)
			{
				if (File.Exists(Path.Combine(strProfilePath, "loadorder.txt")))
					using (StreamReader srLoadOrder = new StreamReader(Path.Combine(strProfilePath, "loadorder.txt")))
					{
						strLoadOrder = srLoadOrder.ReadToEnd();
						p_dicFileStream.Add("loadorder", strLoadOrder);
					}
			}

			if (File.Exists(Path.Combine(strProfilePath, "modlist.xml")))
				using (StreamReader srModFiles = new StreamReader(Path.Combine(strProfilePath, "modlist.xml")))
				{
					strModList = srModFiles.ReadToEnd();
					p_dicFileStream.Add("modlist", strModList);
				}

			if (File.Exists(Path.Combine(strProfilePath, "IniEdits.xml")))
				using (StreamReader srModFiles = new StreamReader(Path.Combine(strProfilePath, "IniEdits.xml")))
				{
					string strIniList = srModFiles.ReadToEnd();
					p_dicFileStream.Add("iniEdits", strIniList);
				}

			string strOptionalFolder = Path.Combine(strProfilePath, "Optional");
			if (Directory.Exists(strOptionalFolder))
			{
				string[] strFiles = Directory.GetFiles(strOptionalFolder);

				if ((strFiles != null) && (strFiles.Length > 0))
				{
					string strOptionalFiles = String.Empty;
					foreach (string strFile in strFiles)
						strOptionalFiles += (strFile + "#");

					p_dicFileStream.Add("optional", strOptionalFiles);
				}
			}

			m_strCurrentProfileId = p_impModProfile.Id;
		}

		public void ExportProfile(IModProfile p_impModProfile, string p_strPath)
		{
			string strProfilePath = Path.Combine(m_strProfileManagerPath, p_impModProfile.Id);
			if (File.Exists(p_strPath))
			{
				string strRandomPath = p_strPath + "_" + Path.GetRandomFileName();
				FileUtil.Move(p_strPath, strRandomPath, true);
			}
			ZipFile.CreateFromDirectory(strProfilePath, p_strPath);
		}

		public bool RestoreBackupProfile()
		{
			IModProfile impImported = ImportProfile(Path.Combine(VirtualModActivator.VirtualPath, "Backup_DONOTDELETE.zip"));
			if (impImported != null)
			{
				m_strCurrentProfileId = impImported.Id;
				SetDefaultProfile(impImported);
				return true;
			}
			else
				return false;
		}

		public IModProfile ImportProfile(string p_strPath)
		{
			string strId = GetNextId;
			string strProfilePath = Path.Combine(m_strProfileManagerPath, strId);
			int intNewProfile = 1;
			if (m_tslProfiles.Count > 0)
			{
				List<IModProfile> lstNewProfile = m_tslProfiles.Where(x => x.Name.IndexOf("Imported") == 0).ToList();
				if ((lstNewProfile != null) && (lstNewProfile.Count > 0))
				{
					List<Int32> lstID = new List<Int32>();
					foreach (IModProfile imp in lstNewProfile)
					{
						string n = imp.Name.Substring(9);
						int i = 0;
						if (int.TryParse(n, out i))
							lstID.Add(Convert.ToInt32(i));
					}
					if (lstID.Count > 0)
					{
						intNewProfile = Enumerable.Range(1, lstID.Max() + 1).Except(lstID).Min();
					}
				}
			}

			try
			{
				ZipFile.ExtractToDirectory(p_strPath, strProfilePath);

				ModProfile mprModProfile = new ModProfile(strId, "Imported " + intNewProfile.ToString(), ModManager.GameMode.ModeId, 0);
				m_tslProfiles.Add(mprModProfile);
				LoadProfileFileList(mprModProfile);
				SaveConfig();
				return mprModProfile;
			}
			catch {	}

			return null;
		}

		private void VirtualModActivator_ModActivationChanged(object sender, EventArgs e)
		{
			if (!VirtualModActivator.DisableLinkCreation)
				if (CurrentProfile != null)
					UpdateCurrentProfileModCount();
		}

		private void ActiveMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (!VirtualModActivator.DisableLinkCreation)
				if (CurrentProfile != null)
				{
					string strLogPath = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted");
					string CurrentProfileScriptedLogPath = GetCurrentProfileScriptedLogPath();

					switch (e.Action)
					{
						case NotifyCollectionChangedAction.Add:
							if (!Directory.Exists(strLogPath))
								break;
							foreach (IMod modAdded in e.NewItems)
							{
								string strModLogPath = Path.Combine(strLogPath, Path.GetFileNameWithoutExtension(modAdded.Filename)) + ".xml";
								string strProfileLogPath = Path.Combine(CurrentProfileScriptedLogPath, Path.GetFileNameWithoutExtension(modAdded.Filename)) + ".xml";
								if (!Directory.Exists(CurrentProfileScriptedLogPath))
									lock (m_objLock)
										Directory.CreateDirectory(CurrentProfileScriptedLogPath);
								if (File.Exists(strModLogPath))
									lock (m_objLock)
										File.Copy(strModLogPath, strProfileLogPath, true);
							}
							break;
						case NotifyCollectionChangedAction.Remove:
							foreach (IMod modRemoved in e.OldItems)
							{
								string strProfileModLogPath = Path.Combine(CurrentProfileScriptedLogPath, Path.GetFileNameWithoutExtension(modRemoved.Filename)) + ".xml";
								if (File.Exists(strProfileModLogPath))
									FileUtil.ForceDelete(strProfileModLogPath);
							}
							break;
					}
					UpdateCurrentProfileModCount();
				}
		}

		private void UpdateCurrentProfileModCount()
		{
			ModProfile mopCurrentProfile = (ModProfile)m_tslProfiles.Find(x => x.Id == m_strCurrentProfileId);
			if (mopCurrentProfile != null)
				mopCurrentProfile.ModCount = VirtualModActivator.ModCount;
		}

		#endregion

		public void PurgeModsFromProfiles(List<IMod> p_lstMods)
		{
			foreach (IModProfile Profile in ModProfiles)
			{
				if (Profile.Id != CurrentProfile.Id)
				{
					string strPath = Path.Combine(m_strProfileManagerPath, Profile.Id);
					VirtualModActivator.PurgeMods(p_lstMods, strPath);

					foreach (IMod modMod in p_lstMods)
					{
						string strProfileModLogPath = Path.Combine(strPath, "Scripted", Path.GetFileNameWithoutExtension(modMod.Filename)) + ".xml";
						if (File.Exists(strProfileModLogPath))
							FileUtil.ForceDelete(strProfileModLogPath);
					}
				}
			}
		}

		public string IsScriptedLogPresent(string p_strModFile)
		{
			if (CurrentProfile == null)
				return null;

			string CurrentProfileScriptedLogPath = GetCurrentProfileScriptedLogPath();

			if (Directory.Exists(CurrentProfileScriptedLogPath))
			{
				string strModLog = Path.GetFileNameWithoutExtension(p_strModFile);
				strModLog += ".xml";
				strModLog = Path.Combine(CurrentProfileScriptedLogPath, strModLog);

				if (File.Exists(strModLog))
					return strModLog;
			}

			return null;
		}

		private string GetCurrentProfileScriptedLogPath()
		{
			return Path.Combine(m_strProfileManagerPath, CurrentProfile.Id, "Scripted");
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_ModManager">The Mod Manager.</param>
		/// <param name="p_lstMods">The list of mods to update.</param>
		/// <param name="p_intNewValue">The new category id value.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask SwitchProfile(IModProfile p_impProfile, ModManager p_ModManager, IList<IVirtualModLink> p_lstNewLinks, IList<IVirtualModLink> p_lstRemoveLinks, bool p_booStartupMigration, ConfirmActionMethod p_camConfirm)
		{
			ProfileActivationTask patProfileSwitch = new ProfileActivationTask(p_ModManager, p_lstNewLinks, p_lstRemoveLinks, p_booStartupMigration);
			if (VirtualModActivator.GameMode.LoadOrderManager != null)
				VirtualModActivator.GameMode.LoadOrderManager.MonitorExternalTask(patProfileSwitch);
			else
				patProfileSwitch.Update(p_camConfirm);
			return patProfileSwitch;
		}

		/// <summary>
		/// Sets up the mod migration task.
		/// </summary>
		public IBackgroundTask ModMigration(MainFormVM p_vmlViewModel, ModManagerControl p_mmgModManagerControl, bool p_booMigrate, ConfirmActionMethod p_camConfirm)
		{
			ModMigrationTask mmtModMigrationTask = new ModMigrationTask(p_vmlViewModel, p_mmgModManagerControl, p_booMigrate, p_camConfirm);
			if (VirtualModActivator.GameMode.LoadOrderManager != null)
				VirtualModActivator.GameMode.LoadOrderManager.MonitorExternalTask(mmtModMigrationTask);
			else
				mmtModMigrationTask.Update(p_camConfirm);
			return mmtModMigrationTask;
		}

		/// <summary>
		/// This backsup the profile manager.
		/// </summary>
		public void Backup()
		{
		}
	}
}
