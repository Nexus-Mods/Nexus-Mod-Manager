using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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

namespace Nexus.Client.ModManagement
{
	public partial class ProfileManager : IProfileManager
	{
		#region Static Properties

		private static readonly Object m_objLock = new Object();
		private static readonly Version CURRENT_VERSION = new Version("0.1.0.0");
		public static readonly string PROFILE_FOLDER = "ModProfiles";
		public static readonly string PROFILE_FILE = "ProfileManagerCfg.xml";
		public static readonly string BACKEDPROFILE_FILE = "BackedProfileManagerCfg.xml";

		private static bool IsValidXmlString(string strText)
		{
			try
			{
				XmlConvert.VerifyXmlChars(strText);
				return true;
			}
			catch
			{
				return false;
			}
		}

		public int m_strProfileIdReturn = 0;

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
		private ThreadSafeObservableList<IModProfile> m_tslBackedProfiles = new ThreadSafeObservableList<IModProfile>();
		private bool m_booInitialized = false;
		private bool m_booUsesPlugin = false;
		private string m_strProfileManagerPath = String.Empty;
		private string m_strProfileManagerConfigPath = String.Empty;
		private string m_strBackedProfileManagerConfigPath = String.Empty;
		private string m_strCurrentProfileId = String.Empty;
		private IBackgroundTask m_tskRunningTask = null;

		#region Events

		public event EventHandler<EventArgs<IBackgroundTask>> RefreshBackedProfilesStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileShareStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> ProfileUpdateStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> ToggleSharingProfileStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> RemoveBackedProfileStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> RenameBackedProfileStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> DownloadProfileStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> CheckOnlineProfileIntegrityStarted = delegate { };
		public event EventHandler<EventArgs<IBackgroundTask>> UpdateCheckStarted = delegate { };
		public event EventHandler<TaskEndedEventArgs> UpdateModListEnded = delegate { };

		#endregion

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
				if (m_tslProfiles.Count > 0)
					return m_tslProfiles.Find(x => x.Id == m_strCurrentProfileId);
				else
					return null;
			}
		}

		/// <summary>
		/// Gets the list of Profiles.
		/// </summary>
		/// <value>The list of active mods.</value>
		public ThreadSafeObservableList<IModProfile> ModProfiles
		{
			get
			{
				return m_tslProfiles;
			}
		}


		/// <summary>
		/// Gets the list of Online Profiles.
		/// </summary>
		/// <value>The list of active mods.</value>
		public ThreadSafeObservableList<IModProfile> ModBackedProfiles
		{
			get
			{
				return m_tslBackedProfiles;
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

		protected IModRepository ModRepository { get; private set; }

		public bool ProfileIsSharing { get; set; }

		/// <summary>
		/// Gets the Profile Manager Path.
		/// </summary>
		public string ProfileManagerPath
		{
			get
			{
				return m_strProfileManagerPath;
			}
		}

		/// <summary>
		/// Gets the IsSharing flag.
		/// </summary>
		/// <value>The list of categories.</value>
		public bool IsSharing
		{
			get
			{
				return !(m_tskRunningTask == null);
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
		public ProfileManager(IVirtualModActivator p_ivaVirtualModActivator, ModManager p_mmgModManager, IModRepository p_mrModRepository, string p_strModInstallDirectory, bool p_booUsesPlugin)
		{
			VirtualModActivator = p_ivaVirtualModActivator;
			ModManager = p_mmgModManager;
			ModRepository = p_mrModRepository;
			VirtualModActivator.ModActivationChanged += new EventHandler(VirtualModActivator_ModActivationChanged);
			ModManager.ActiveMods.CollectionChanged += new NotifyCollectionChangedEventHandler(ActiveMods_CollectionChanged);
			m_strProfileManagerPath = Path.Combine(p_strModInstallDirectory, PROFILE_FOLDER);
			m_strProfileManagerConfigPath = Path.Combine(m_strProfileManagerPath, PROFILE_FILE);
			m_strBackedProfileManagerConfigPath = Path.Combine(m_strProfileManagerPath, BACKEDPROFILE_FILE);
			m_booUsesPlugin = p_booUsesPlugin;
			Initialize();
			if (!Initialized)
				Setup();
		}

		public void Initialize()
		{
			if (IsValid(m_strProfileManagerConfigPath))
			{
				if (ReadVersion(m_strProfileManagerConfigPath) == CURRENT_VERSION)
				{
					SetConfig(LoadConfig(m_strProfileManagerConfigPath));
					m_booInitialized = true;
				}
			}

			if (IsValid(m_strBackedProfileManagerConfigPath))
			{
				if (ReadVersion(m_strBackedProfileManagerConfigPath) == CURRENT_VERSION)
				{
					SetOnlineConfig(LoadOnlineConfig(m_strBackedProfileManagerConfigPath));
					m_booInitialized = true;
				}
			}
		}

		public void Setup()
		{
			SaveConfig();
			SetConfig(LoadConfig(m_strProfileManagerConfigPath));
			SetOnlineConfig(LoadOnlineConfig(m_strBackedProfileManagerConfigPath));
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
								new XAttribute("profileName", XmlConvert.EncodeName(prf.Name)),
								new XAttribute("isDefault", prf.IsDefault ? "1" : "0"),
								new XElement("gameModeId",
									new XText(prf.GameModeId)),
								new XElement("modCount",
									new XText(prf.ModCount.ToString())),
									new XElement("onlineId",
									new XText(prf.OnlineID ?? string.Empty)),
									new XElement("onlineName",
									new XText(XmlConvert.EncodeName(prf.OnlineName ?? string.Empty))),
									new XElement("BackupDate",
									new XText(prf.BackupDate ?? string.Empty)),
									new XElement("Version",
									new XText(prf.Version.ToString() ?? string.Empty)),
									new XElement("Author",
									new XText(prf.Author ?? string.Empty)),
									new XElement("isEdited",
									new XText(prf.IsEdited.ToString() ?? string.Empty)),
									new XElement("WorksWithSaves",
									new XText(prf.WorksWithSaves.ToString() ?? string.Empty)),
									new XElement("isShared",
									new XText(prf.IsShared ? "1" : "0"))));

			if (!Directory.Exists(m_strProfileManagerPath))
				Directory.CreateDirectory(m_strProfileManagerPath);

			bool booSaved = false;
			int intRetries = 0;

			while (!booSaved && (intRetries < 12))
			{
				try
				{
					docProfile.Save(m_strProfileManagerConfigPath);
					booSaved = true;
					break;
				}
				catch (Exception e)
				{
					intRetries++;
					Thread.Sleep(250);
					if (intRetries >= 12)
						throw e;
				}
			}
		}

		public void SaveOnlineConfig()
		{
			XDocument docProfile = new XDocument();
			XElement xelRoot = new XElement("profileManager", new XAttribute("fileVersion", CURRENT_VERSION));
			docProfile.Add(xelRoot);

			XElement xelProfiles = new XElement("profileList");
			xelRoot.Add(xelProfiles);
			xelProfiles.Add(from prf in m_tslBackedProfiles
							select new XElement("profile",
								new XAttribute("profileId", prf.Id),
								new XAttribute("profileName", XmlConvert.EncodeName(prf.Name)),
								new XAttribute("isDefault", prf.IsDefault ? "1" : "0"),
								new XElement("gameModeId",
									new XText(prf.GameModeId)),
								new XElement("modCount",
									new XText(prf.ModCount.ToString())),
									new XElement("onlineId",
									new XText(prf.OnlineID)),
									new XElement("onlineName",
									new XText(XmlConvert.EncodeName(prf.OnlineName))),
									new XElement("BackupDate",
									new XText(prf.BackupDate)),
									new XElement("Version",
									new XText(prf.Version.ToString())),
									new XElement("Author",
									new XText(prf.Author)),
									new XElement("isEdited",
									new XText(prf.IsEdited.ToString())),
									new XElement("WorksWithSaves",
									new XText(prf.WorksWithSaves.ToString())),
									new XElement("isShared",
									new XText(prf.IsShared ? "1" : "0"))));

			if (!Directory.Exists(m_strProfileManagerPath))
				Directory.CreateDirectory(m_strProfileManagerPath);

			if (File.Exists(m_strBackedProfileManagerConfigPath))
				FileUtil.ForceDelete(m_strBackedProfileManagerConfigPath);

			docProfile.Save(m_strBackedProfileManagerConfigPath);
		}

		public void SetConfig(ThreadSafeObservableList<IModProfile> p_iplProfiles)
		{
			m_tslProfiles.Clear();
			m_tslProfiles = new ThreadSafeObservableList<IModProfile>(p_iplProfiles);
		}

		public void SetOnlineConfig(ThreadSafeObservableList<IModProfile> p_iplBackedProfiles)
		{
			m_tslBackedProfiles.Clear();
			m_tslBackedProfiles = new ThreadSafeObservableList<IModProfile>(p_iplBackedProfiles);
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
							bool booShared = false;
							bool booEdited = false;
							string strOnlineID = string.Empty;
							string strOnlineName = string.Empty;
							string booBackedUp = string.Empty;
							string strProfileVersion = "0";
							string strProfileAuthor = string.Empty;
							int intWorksWithSaves = 0;


							string strProfileId = xelProfile.Attribute("profileId").Value;
							string strProfileName = XmlConvert.DecodeName(xelProfile.Attribute("profileName").Value);
							strProfileName = strProfileName.Replace("|", string.Empty);
							bool booDefault = (xelProfile.Attribute("isDefault").Value == "1") ? true : false;
							string strGameModeId = xelProfile.Element("gameModeId").Value;
							int intModCount = int.TryParse(xelProfile.Element("modCount").Value, out intModCount) ? intModCount : 0;

							try
							{
								booShared = (xelProfile.Element("isShared").Value == "1") ? true : false;
							}
							catch { }

							try
							{
								strOnlineID = xelProfile.Element("onlineId").Value;
							}
							catch { }

							try
							{
								strOnlineName = XmlConvert.DecodeName(xelProfile.Element("onlineName").Value);
							}
							catch { }

							if (!string.IsNullOrWhiteSpace(strOnlineName))
								strOnlineName = strOnlineName.Replace("|", string.Empty);

							try
							{
								booBackedUp = xelProfile.Element("BackupDate").Value;
							}
							catch { }

							try
							{
								strProfileVersion = xelProfile.Element("Version").Value;
							}
							catch { }

							try
							{
								strProfileAuthor = xelProfile.Element("Author").Value;
							}
							catch { }

							try
							{
								intWorksWithSaves = int.Parse(xelProfile.Element("WorksWithSaves").Value);
							}
							catch { }

							try
							{
								booEdited = (xelProfile.Element("isEdited").Value == "True") ? true : false;
							}
							catch { }

							lstProfiles.Add(new ModProfile(strProfileId, strProfileName, strGameModeId, intModCount, booDefault, strOnlineID, strOnlineName, booBackedUp, booShared, strProfileVersion, strProfileAuthor, intWorksWithSaves, booEdited));

							if (booDefault)
								m_strCurrentProfileId = strProfileId;
						}
					}
				}
				catch { }
			}

			return lstProfiles;
		}

		public ThreadSafeObservableList<IModProfile> LoadOnlineConfig(string p_strXMLFilePath)
		{
			ThreadSafeObservableList<IModProfile> lstBackedProfiles = new ThreadSafeObservableList<IModProfile>();

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
							bool booShared = false;
							bool booEdited = false;
							string strOnlineID = string.Empty;
							string strOnlineName = string.Empty;
							string booBackedUp = string.Empty;
							string strProfileVersion = "0";
							string strProfileAuthor = string.Empty;
							int intWorksWithSaves = 0;

							string strProfileId = xelProfile.Attribute("profileId").Value;
							string strProfileName = XmlConvert.DecodeName(xelProfile.Attribute("profileName").Value);
							strProfileName = strProfileName.Replace("|", string.Empty);
							bool booDefault = (xelProfile.Attribute("isDefault").Value == "1") ? true : false;
							string strGameModeId = xelProfile.Element("gameModeId").Value;
							Int32 intModCount = Int32.TryParse(xelProfile.Element("modCount").Value, out intModCount) ? intModCount : 0;

							try
							{
								booShared = (xelProfile.Element("isShared").Value == "1") ? true : false;
							}
							catch { }

							try
							{
								strOnlineID = xelProfile.Element("onlineId").Value;
							}
							catch { }

							try
							{
								strOnlineName = XmlConvert.DecodeName(xelProfile.Element("onlineName").Value);
							}
							catch { }

							if (!string.IsNullOrWhiteSpace(strOnlineName))
								strOnlineName = strOnlineName.Replace("|", string.Empty);

							try
							{
								booBackedUp = xelProfile.Element("BackupDate").Value;
							}
							catch { }

							try
							{
								strProfileVersion = xelProfile.Element("Version").Value;
							}
							catch { }

							try
							{
								strProfileAuthor = xelProfile.Element("Author").Value;
							}
							catch { }

							try
							{
								intWorksWithSaves = int.Parse(xelProfile.Element("WorksWithSaves").Value);
							}
							catch { }

							try
							{
								booEdited = (xelProfile.Element("isEdited").Value == "True") ? true : false;
							}
							catch { }

							if (strOnlineID != "")
							{
								lstBackedProfiles.Add(new ModProfile(strProfileId, strProfileName, strGameModeId, intModCount, booDefault, strOnlineID, strOnlineName, booBackedUp, booShared, strProfileVersion, strProfileAuthor, intWorksWithSaves, booEdited));

								if (booDefault)
									m_strCurrentProfileId = strProfileId;
							}
						}
					}
				}
				catch { }
			}

			return lstBackedProfiles;
		}

		private string ImportedProfileOnlineId(string p_strXMLFilePath)
		{
			string strOnlineId = string.Empty;

			if (File.Exists(p_strXMLFilePath))
			{
				XDocument docProfile = XDocument.Load(p_strXMLFilePath);

				try
				{

					XElement xelProfile = docProfile.Descendants("profile").FirstOrDefault();
					strOnlineId = xelProfile.Element("onlineId").Value;
				}
				catch { }
			}

			return strOnlineId;
		}

		private string ImportedProfileBackupDate(string p_strXMLFilePath)
		{
			string strBackupDate = string.Empty;

			if (File.Exists(p_strXMLFilePath))
			{
				XDocument docProfile = XDocument.Load(p_strXMLFilePath);

				try
				{

					XElement xelProfile = docProfile.Descendants("profile").FirstOrDefault();
					strBackupDate = xelProfile.Element("BackupDate").Value;
				}
				catch { }
			}

			return strBackupDate;
		}

		private Int32 ImportedProfileModCount(string p_strXMLFilePath)
		{
			Int32 intModCount = 0;

			if (File.Exists(p_strXMLFilePath))
			{
				XDocument docProfile = XDocument.Load(p_strXMLFilePath);

				try
				{

					XElement xelProfile = docProfile.Descendants("profile").FirstOrDefault();
					intModCount = Int32.TryParse(xelProfile.Element("modCount").Value, out intModCount) ? intModCount : 0;
				}
				catch { }
			}

			return intModCount;
		}

		private string ImportedProfileName(string p_strTxtFilePath)
		{
			string[] strArgParts = null;

			if (File.Exists(p_strTxtFilePath))
			{
				try
				{
					using (StreamReader srProfile = new StreamReader(p_strTxtFilePath))
					{
						string strProfileName = srProfile.ReadToEnd();
						strArgParts = strProfileName.Split('|');
					}
				}
				catch { }
			}

			return strArgParts == null ? string.Empty : strArgParts[0];
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

			string strActiveProfileID = string.Empty;
			if (CurrentProfile != null)
				strActiveProfileID = CurrentProfile.Id;

			ModProfile mprModProfile = new ModProfile(strId, "Profile " + intNewProfile.ToString(), p_strGameModeId, (p_intModCount < 0 ? VirtualModActivator.ModCount : p_intModCount), true, "", "", "", false, "", "", 0, false);
			mprModProfile.IsDefault = true;
			SaveProfile(mprModProfile, p_bteModList, p_bteIniList, p_bteLoadOrder, p_strOptionalFiles);
			string strLogPath = string.IsNullOrEmpty(strActiveProfileID) ? Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted") : Path.Combine(m_strProfileManagerPath, strActiveProfileID, "Scripted");
			if (Directory.Exists(strLogPath))
				lock (m_objLock)
					DirectoryCopy(strLogPath, Path.Combine(m_strProfileManagerPath, mprModProfile.Id, "Scripted"), true);
			m_tslProfiles.Add(mprModProfile);
			m_strCurrentProfileId = mprModProfile.Id;
			SetDefaultProfile(mprModProfile);
			SaveConfig();
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
		public void UpdateProfile(IModProfile p_impModProfile, byte[] p_bteIniEdits, byte[] p_bteLoadOrder, string[] p_strOptionalFiles, out string p_strError)
		{
			p_strError = null;
			if (p_impModProfile.Id == m_strCurrentProfileId)
				UpdateCurrentProfileModCount();

			p_strError = SaveProfile(p_impModProfile, null, p_bteIniEdits, p_bteLoadOrder, p_strOptionalFiles);

			if (p_impModProfile.Id == m_strCurrentProfileId)
				m_tslProfiles.Remove(CurrentProfile);
			else
				m_tslProfiles.Remove(p_impModProfile);

			m_tslProfiles.Add(p_impModProfile);
			SaveConfig();
		}

		/// <summary>
		/// Updates the profile.
		/// </summary>
		public void UpdateProfile(IModProfile p_impModProfileOld, IModProfile p_impModProfileNew)
		{
			UpdateCurrentProfileModCount();
			m_tslProfiles.Remove(p_impModProfileOld);
			m_tslProfiles.Add(p_impModProfileNew);
			SaveConfig();
		}

		public void UpdateProfileDownloadId(IModProfile p_impProfile, Dictionary<string, string> p_dctNewDownloadID)
		{

			bool booEdited = false;
			if (p_impProfile == null)
				return;

			if (p_impProfile.ModList == null)
				LoadProfileFileList(p_impProfile);


			foreach (KeyValuePair<string, string> kvp in p_dctNewDownloadID)
			{
				IVirtualModInfo ModInfo = null;

				try
				{
					ModInfo = p_impProfile.ModList.Where(x => (x != null) && (!string.IsNullOrEmpty(x.ModFileName))).Where(x => x.ModFileName.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
				}
				catch { }

				if (ModInfo != null)
				{
					VirtualModInfo vmiModInfo = new VirtualModInfo(ModInfo);
					if (string.IsNullOrWhiteSpace(vmiModInfo.DownloadId))
						vmiModInfo.DownloadId = kvp.Value;
					else
						vmiModInfo.UpdatedDownloadId = kvp.Value;

					p_impProfile.ModList.Remove(ModInfo);
					p_impProfile.ModList.Add(vmiModInfo);

					if (!booEdited)
						booEdited = true;
				}
			}

			if (booEdited)
			{
				string strProfilePath = Path.Combine(m_strProfileManagerPath, p_impProfile.Id);
				VirtualModActivator.SaveModList(Path.Combine(strProfilePath, "modlist.xml"), p_impProfile.ModList, p_impProfile.ModFileList);
			}
		}

		/// <summary>
		/// Removes the profile from the profile manager.
		/// </summary>
		/// <param name="p_impModProfile">The profile to remove.</param>
		public void RemoveProfile(IModProfile p_impModProfile, bool p_booDeleteFile)
		{
			if (p_booDeleteFile)
			{
				if (Directory.Exists(Path.Combine(m_strProfileManagerPath, p_impModProfile.Id)))
					FileUtil.ForceDelete(Path.Combine(m_strProfileManagerPath, p_impModProfile.Id));
			}

			m_tslProfiles.Remove(p_impModProfile);
			SaveConfig();
		}

		public void RemoveBackedProfile(IModProfile p_impModProfile)
		{
			m_tslBackedProfiles.Remove(p_impModProfile);
			SaveOnlineConfig();
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
		/// Loads the Profile's mod list.
		/// </summary>
		public List<IVirtualModInfo> LoadProfileModsList(IModProfile p_impModProfile)
		{
			List<IVirtualModLink> lstModLinks;
			List<IVirtualModInfo> lstModList;
			string strPath = string.Empty;

			if (p_impModProfile == null)
				strPath = Path.Combine(m_strProfileManagerPath, Path.Combine("", "modlist.xml"));
			else
				strPath = Path.Combine(m_strProfileManagerPath, Path.Combine(p_impModProfile.Id, "modlist.xml"));

			bool booSuccess = VirtualModActivator.LoadListOnDemand(strPath, out lstModLinks, out lstModList);

			return lstModList;
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
		/// Saves the profile file.
		/// </summary>
		public string SaveProfile(IModProfile p_impModProfile, byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string[] p_strOptionalFiles)
		{
			return SaveProfile(p_impModProfile, p_bteModList, p_bteIniList, p_bteLoadOrder, p_strOptionalFiles, m_strProfileManagerPath);
		}

		/// <summary>
		/// Saves the profile file.
		/// </summary>
		public string SaveProfile(IModProfile p_impModProfile, byte[] p_bteModList, byte[] p_bteIniList, byte[] p_bteLoadOrder, string[] p_strOptionalFiles, string p_strProfileManagerPath)
		{
			if (p_impModProfile != null)
			{
				string strProfilePath = Path.Combine(p_strProfileManagerPath, p_impModProfile.Id);

				if (!Directory.Exists(strProfilePath))
					Directory.CreateDirectory(strProfilePath);

				try
				{
					if ((m_booUsesPlugin) && (p_bteLoadOrder != null) && (p_bteLoadOrder.Length > 0))
						File.WriteAllBytes(Path.Combine(strProfilePath, "loadorder.txt"), p_bteLoadOrder);
				}
				catch (Exception ex)
				{
					return "Error: " + ex.Message;
				}
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

				byte[] bteProfileBytes = GetProfileBytes(p_impModProfile);

				if ((bteProfileBytes != null) && (bteProfileBytes.Length > 0))
					File.WriteAllBytes(Path.Combine(strProfilePath, "profile.xml"), GetProfileBytes(p_impModProfile));

				string strOptionalFolder = Path.Combine(strProfilePath, "Optional");

				if (!(p_strOptionalFiles == null))
				{
					if (Directory.Exists(strOptionalFolder))
					{
						string[] strFiles = Directory.GetFiles(strOptionalFolder);

						foreach (string file in strFiles)
							lock (m_objLock)
								FileUtil.ForceDelete(file);
					}

					if ((p_strOptionalFiles != null) && (p_strOptionalFiles.Length > 0))
					{
						lock (m_objLock)
							if (!Directory.Exists(strOptionalFolder))
								Directory.CreateDirectory(strOptionalFolder);

						foreach (string strFile in p_strOptionalFiles)
						{
							lock (m_objLock)
								if (File.Exists(strFile))
									File.Copy(strFile, Path.Combine(strOptionalFolder, Path.GetFileName(strFile)), true);
						}
					}
				}
			}

			return null;
		}

		private byte[] GetProfileBytes(IModProfile p_impModProfile)
		{
			try
			{
				XElement xelProfile = new XElement("profile",
						new XAttribute("profileId", p_impModProfile.Id),
						new XAttribute("profileName", XmlConvert.DecodeName(p_impModProfile.Name)),
						new XAttribute("isDefault", p_impModProfile.IsDefault ? "1" : "0"),
						new XElement("gameModeId",
							new XText(p_impModProfile.GameModeId)),
						new XElement("modCount",
							new XText(p_impModProfile.ModCount.ToString())),
						new XElement("onlineId",
							new XText(p_impModProfile.OnlineID)),
						new XElement("Version",
							new XText(p_impModProfile.Version.ToString())),
						new XElement("Author",
							new XText(p_impModProfile.Author.ToString())),
						new XElement("WorksWithSaves",
							new XText(p_impModProfile.WorksWithSaves.ToString())),
						new XElement("onlineName",
							new XText(XmlConvert.DecodeName(p_impModProfile.OnlineName))));

				return System.Text.Encoding.UTF8.GetBytes(xelProfile.ToString());
			}
			catch
			{
				return null;
			}
		}

		private MemoryStream GetModFilesStream(XDocument p_xdcModList)
		{
			MemoryStream ms = new MemoryStream();
			XmlWriterSettings xws = new XmlWriterSettings();
			xws.OmitXmlDeclaration = true;
			xws.Indent = true; ;

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
			string strErrorMessage;
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
			catch (Exception ex)
			{
			}

			return null;
		}

		public IModProfile ImportProfile(string p_strPath, string p_strLabel, int p_intProfileId, out string p_strErrorMessage)
		{
			p_strErrorMessage = string.Empty;
			return null;
		}

		private void VirtualModActivator_ModActivationChanged(object sender, EventArgs e)
		{
			if (!VirtualModActivator.DisableLinkCreation)
				if (CurrentProfile != null)
				{
					UpdateCurrentProfileModCount();

					if (CurrentProfile.BackupDate != "")
					{
						CurrentProfile.IsEdited = true;
						SaveConfig();
					}
				}
		}

		private void ActiveMods_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (!VirtualModActivator.DisableLinkCreation)
				if (CurrentProfile != null)
				{
					string strLogPath = Path.Combine(ModManager.GameMode.GameModeEnvironmentInfo.InstallInfoDirectory, "Scripted");
					string CurrentProfileScriptedLogPath = GetCurrentProfileScriptedLogPath(CurrentProfile);

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

								if (CurrentProfile.BackupDate != "")
								{
									CurrentProfile.IsEdited = true;

									SaveConfig();
								}
							}
							break;
						case NotifyCollectionChangedAction.Remove:
						case NotifyCollectionChangedAction.Reset:
							foreach (IMod modRemoved in e.OldItems)
							{
								string strProfileModLogPath = Path.Combine(CurrentProfileScriptedLogPath, Path.GetFileNameWithoutExtension(modRemoved.Filename)) + ".xml";
								if (File.Exists(strProfileModLogPath))
									lock (m_objLock)
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

		/// <summary>
		/// Renames the profile.
		/// </summary>
		public bool RenameProfile(IModProfile p_impModProfile, string p_strName)
		{
			bool booAllowed = true;
			m_tslProfiles.Remove(p_impModProfile);
			p_impModProfile.Name = p_strName;

			if (string.IsNullOrWhiteSpace(p_impModProfile.Name))
			{
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

				booAllowed = false;
				p_impModProfile.Name = "Profile " + intNewProfile.ToString();
			}

			SaveProfile(p_impModProfile, null, null, null, null);
			m_tslProfiles.Add(p_impModProfile);
			SaveConfig();
			return booAllowed;
		}

		#endregion

		public void PurgeModsFromProfiles(List<IMod> p_lstMods)
		{
			if ((ModProfiles != null) && (ModProfiles.Count > 0))
			{
				foreach (IModProfile Profile in ModProfiles)
				{
					if ((Profile != null) && ((CurrentProfile == null) || (Profile.Id != CurrentProfile.Id)))
					{
						string strPath = Path.Combine(m_strProfileManagerPath, Profile.Id);
						VirtualModActivator.PurgeMods(p_lstMods, strPath);

						foreach (IMod modMod in p_lstMods)
						{
							if (modMod != null)
							{
								string strProfileModLogPath = Path.Combine(strPath, "Scripted", Path.GetFileNameWithoutExtension(modMod.Filename)) + ".xml";
								if (File.Exists(strProfileModLogPath))
									FileUtil.ForceDelete(strProfileModLogPath);
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Purges all the scripted installers log files inside the InstallInfo\Scripted folder.
		/// </summary>
		public void PurgeProfileXMLInstalledFile()
		{
			if (CurrentProfile != null)
			{
				string CurrentProfileScriptedLogPath = GetCurrentProfileScriptedLogPath(CurrentProfile);

				if (Directory.Exists(CurrentProfileScriptedLogPath))
				{
					foreach (string file in Directory.EnumerateDirectories(CurrentProfileScriptedLogPath, "*.xml", SearchOption.TopDirectoryOnly))
					{
						if (File.Exists(file))
							FileUtil.ForceDelete(file);
					}
				}
			}
		}

		/// <summary>
		/// Check thescripted installers log files integrity.
		/// </summary>
		public List<string> CheckScriptedInstallersIntegrity(IModProfile p_impFrom, IModProfile p_impTo)
		{
			List<string> lstConflicts = new List<string>();
			string strToPath = GetCurrentProfileScriptedLogPath(p_impTo);

			if (!Directory.Exists(strToPath))
				return null;

			if (p_impFrom == null)
			{
				List<string> lstTo = new List<string>();
				lstTo = Directory.GetFiles(strToPath, "*.xml", SearchOption.TopDirectoryOnly).ToList();
				if ((lstTo != null) && (lstTo.Count > 0))
				{
					lstTo = lstTo.Select(x => Path.GetFileName(x)).ToList();

					foreach (string File in lstTo)
					{
						IVirtualModInfo modMod = VirtualModActivator.VirtualMods.Find(x => Path.GetFileNameWithoutExtension(x.ModFileName).Equals(Path.GetFileNameWithoutExtension(File), StringComparison.CurrentCultureIgnoreCase));
						if (modMod != null)
							lstConflicts.Add(Path.GetFileName(modMod.ModFileName));
					}
				}
			}
			else
			{
				string strFromPath = GetCurrentProfileScriptedLogPath(p_impFrom);
				if (!Directory.Exists(strFromPath))
					return null;

				List<string> lstFrom = new List<string>();
				List<string> lstTo = new List<string>();
				List<string> lstCommon = new List<string>();
				Int32 intConflicts = 0;

				try
				{
					lstFrom = Directory.GetFiles(strFromPath, "*.xml", SearchOption.TopDirectoryOnly).ToList();
					lstTo = Directory.GetFiles(strToPath, "*.xml", SearchOption.TopDirectoryOnly).ToList();

					if ((lstFrom != null) && (lstFrom.Count > 0))
						lstFrom = lstFrom.Select(x => Path.GetFileName(x)).ToList();
					else
						return lstConflicts;

					if ((lstTo != null) && (lstTo.Count > 0))
						lstTo = lstTo.Select(x => Path.GetFileName(x)).ToList();
					else
						return lstConflicts;

					lstCommon = lstFrom.Intersect(lstTo, StringComparer.CurrentCultureIgnoreCase).ToList();

					foreach (string File in lstCommon)
					{
						intConflicts = 0;
						List<KeyValuePair<string, string>> dicFrom = LoadXMLModFilesToInstall(Path.Combine(strFromPath, File));
						List<KeyValuePair<string, string>> dicTo = LoadXMLModFilesToInstall(Path.Combine(strToPath, File));

						intConflicts += dicFrom.Where(x => !dicTo.Contains(x, StringComparer.CurrentCultureIgnoreCase)).Count();
						if (intConflicts <= 0)
							intConflicts += dicTo.Where(x => !dicFrom.Contains(x, StringComparer.CurrentCultureIgnoreCase)).Count();

						if (intConflicts > 0)
						{
							IVirtualModInfo modMod = VirtualModActivator.VirtualMods.Find(x => Path.GetFileNameWithoutExtension(x.ModFileName).Equals(Path.GetFileNameWithoutExtension(File), StringComparison.CurrentCultureIgnoreCase));
							if (modMod != null)
								lstConflicts.Add(Path.GetFileName(modMod.ModFileName));
						}
					}
				}
				catch
				{ }
			}

			return lstConflicts;
		}

		public string IsScriptedLogPresent(string p_strModFile)
		{
			return IsScriptedLogPresent(p_strModFile, CurrentProfile);
		}

		public string IsScriptedLogPresent(string p_strModFile, IModProfile p_impProfile)
		{
			if (p_impProfile == null)
				return null;

			string CurrentProfileScriptedLogPath = GetCurrentProfileScriptedLogPath(p_impProfile);

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

		private string GetCurrentProfileScriptedLogPath(IModProfile p_impProfile)
		{
			return Path.Combine(m_strProfileManagerPath, p_impProfile.Id, "Scripted");
		}

		#region CheckOnlineProfileIntegrity

		/// <summary>
		/// Installs the specified profile.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask CheckOnlineProfileIntegrity(IModProfile p_impProfile, Dictionary<string, string> p_dicMissingMods, string p_strGameModeID, ConfirmActionMethod p_camConfirm)
		{
			CheckOnlineProfileIntegrityTask cotCheckIntegrity = new CheckOnlineProfileIntegrityTask(ModRepository, p_impProfile, this, p_dicMissingMods, p_strGameModeID);
			cotCheckIntegrity.Update(p_camConfirm);
			return cotCheckIntegrity;
		}

		/// <summary>
		/// Installs the specified profile.
		/// </summary>
		/// <param name="p_strPath">The path to the mod to install.</param>
		/// <param name="p_cocConfirmOverwrite">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public void AsyncCheckOnlineProfileIntegrity(IModProfile p_impProfile, Dictionary<string, string> p_dicMissingMods, string p_strGameModeID, ConfirmActionMethod p_camConfirm)
		{
			CheckOnlineProfileIntegrityTask cotCheckIntegrity = new CheckOnlineProfileIntegrityTask(ModRepository, p_impProfile, this, p_dicMissingMods, p_strGameModeID);
			AsyncCheckOnlineProfileIntegrityTask(cotCheckIntegrity, p_camConfirm);
		}

		public async void AsyncCheckOnlineProfileIntegrityTask(CheckOnlineProfileIntegrityTask p_pstCheckOnlineProfileIntegrityTask, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (ModManager.LoginTask.LoggedIn)
				{
					p_pstCheckOnlineProfileIntegrityTask.Update(p_camConfirm);
					CheckOnlineProfileIntegrityStarted(this, new EventArgs<IBackgroundTask>(p_pstCheckOnlineProfileIntegrityTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		/// <summary>
		/// Checks if there's an XML with the list of files to install for the current mod, if present the list of files will be returned.
		/// </summary>
		protected List<KeyValuePair<string, string>> LoadXMLModFilesToInstall(string p_strPath)
		{
			string strModFilesPath = p_strPath;

			if (File.Exists(strModFilesPath))
			{
				XDocument docScripted = XDocument.Load(strModFilesPath);

				try
				{
					XElement xelFileList = docScripted.Descendants("FileList").FirstOrDefault();
					if ((xelFileList != null) && xelFileList.HasElements)
					{
						List<KeyValuePair<string, string>> dicFiles = new List<KeyValuePair<string, string>>();

						foreach (XElement xelModFile in xelFileList.Elements("File"))
						{
							string strFileFrom = xelModFile.Attribute("FileFrom").Value;
							string strFileTo = xelModFile.Attribute("FileTo").Value;
							if (!String.IsNullOrWhiteSpace(strFileFrom) && !Readme.IsValidExtension(Path.GetExtension(strFileFrom)))
								dicFiles.Add(new KeyValuePair<string, string>(strFileFrom, strFileTo));
						}

						if (dicFiles.Count > 0)
							return dicFiles;
					}
				}
				catch
				{ }
			}

			return null;
		}

		/// <summary>
		/// Get the Profile path.
		/// </summary>
		/// /// <param name="p_impProfile">The Profile.</param>
		public string GetProfilePath(IModProfile p_impProfile)
		{
			return Path.Combine(m_strProfileManagerPath, p_impProfile.Id);
		}

		/// <summary>
		/// Get the Profile's mod list path.
		/// </summary>
		/// /// <param name="p_impProfile">The Profile.</param>
		public string GetProfileModListPath(IModProfile p_impProfile)
		{
			if ((p_impProfile != null) && !string.IsNullOrEmpty(p_impProfile.Id))
				return Path.Combine(m_strProfileManagerPath, p_impProfile.Id, "modlist.xml");
			else
				return null;
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_ModManager">The Mod Manager.</param>
		/// <param name="p_lstMods">The list of mods to update.</param>
		/// <param name="p_intNewValue">The new category id value.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask SwitchProfile(IModProfile p_impProfile, ModManager p_ModManager, IList<IVirtualModLink> p_lstNewLinks, IList<IVirtualModLink> p_lstRemoveLinks, bool p_booStartupMigration, bool p_booRestoring, ConfirmActionMethod p_camConfirm)
		{
			ProfileActivationTask patProfileSwitch = new ProfileActivationTask(p_ModManager, p_lstNewLinks, p_lstRemoveLinks, p_booStartupMigration, p_booRestoring);
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
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		/// <param name="p_booMissingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public IBackgroundTask UpdateMods(List<IMod> p_lstModList, ConfirmActionMethod p_camConfirm, bool p_booOverrideCategorySetup, bool p_booMissingDownloadId)
		{
			if (ModRepository.UserStatus != null)
			{
				ModUpdateCheckTask mutModUpdateCheck = new ModUpdateCheckTask(ModManager.AutoUpdater, this, ModRepository, p_lstModList, p_booOverrideCategorySetup, p_booMissingDownloadId, ModManager.EnvironmentInfo.Settings.OverrideLocalModNames);
				mutModUpdateCheck.Update(p_camConfirm);
				return mutModUpdateCheck;
			}
			else
				throw new Exception("Login required");
		}

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_lstModList">The list of mods we need to update.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <param name="p_booOverrideCategorySetup">Whether to force a global update.</param>
		/// <param name="p_booMissingDownloadId">Whether to just look for missing download IDs.</param>
		/// <returns>The background task that will run the updaters.</returns>
		public void AsyncUpdateMods(List<IMod> p_lstModList, ConfirmActionMethod p_camConfirm, bool p_booOverrideCategorySetup, bool p_booMissingDownloadId)
		{
			ModUpdateCheckTask mutModUpdateCheck = new ModUpdateCheckTask(ModManager.AutoUpdater, this, ModRepository, p_lstModList, p_booOverrideCategorySetup, p_booMissingDownloadId, ModManager.EnvironmentInfo.Settings.OverrideLocalModNames);
			AsyncUpdateModsTask(mutModUpdateCheck, p_camConfirm);
		}

		public async Task AsyncUpdateModsTask(ModUpdateCheckTask p_mutModUpdateCheck, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (ModManager.LoginTask.LoggedIn)
				{
					p_mutModUpdateCheck.Update(p_camConfirm);
					UpdateCheckStarted(this, new EventArgs<IBackgroundTask>(p_mutModUpdateCheck));
					break;
				}
				else
					intRetry++;
			}
		}

		/// <summary>
		/// Updates the Profile's mod list.
		/// </summary>
		public IBackgroundTask UpdateModList(IModProfile p_imProfile, ConfirmActionMethod p_camConfirm)
		{
			return null;
		}

		private void UpdateModList_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			UpdateModListEnded(this, e);
		}

		#region ToggleSharingProfile

		/// <summary>
		/// Toggle the share of the specified profile.
		/// </summary>
		/// <param name="p_imModProfile">The Profile object.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask ToggleSharingProfile(IModProfile p_imProfile, ConfirmActionMethod p_camConfirm)
		{
			ToggleSharingProfileTask tspProfile = new ToggleSharingProfileTask(ModRepository, this, p_imProfile);
			tspProfile.Update(p_camConfirm);
			return tspProfile;
		}

		/// <summary>
		/// Toggle the share of the specified profile (async mode).
		/// </summary>
		/// <param name="p_imModProfile">The Profile object.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public void AsyncToggleSharingProfiles(IModProfile p_impProfile, ConfirmActionMethod p_camConfirm)
		{
			ToggleSharingProfileTask tspProfile = new ToggleSharingProfileTask(ModRepository, this, p_impProfile);
			AsyncToggleSharingProfileTask(tspProfile, p_impProfile, p_camConfirm);
		}

		/// <summary>
		/// The AsyncToggleSharingProfile Task.
		/// </summary>
		/// <param name="p_pstToggleSharingProfileTask">The ToggleSharingProfile Task.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		public async void AsyncToggleSharingProfileTask(ToggleSharingProfileTask p_pstToggleSharingProfileTask, IModProfile p_impProfile, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (ModManager.LoginTask.LoggedIn)
				{
					p_pstToggleSharingProfileTask.Update(p_camConfirm);
					ToggleSharingProfileStarted(p_impProfile, new EventArgs<IBackgroundTask>(p_pstToggleSharingProfileTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		#region Refresh Backed Profiles

		/// <summary>
		/// Refresh the Backed Profiles.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask RefreshBackedProfiles(ConfirmActionMethod p_camConfirm)
		{
			RefreshBackedProfilesTask ropRefreshProfiles = new RefreshBackedProfilesTask(ModRepository, this);
			ropRefreshProfiles.Update(p_camConfirm);
			return ropRefreshProfiles;
		}

		/// <summary>
		/// Refresh the Backed Profiles (async mode).
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public void AsyncRefreshBackedProfiles(ConfirmActionMethod p_camConfirm)
		{
			RefreshBackedProfilesTask ropRefreshProfiles = new RefreshBackedProfilesTask(ModRepository, this);
			AsyncRefreshBackedProfilesTask(ropRefreshProfiles, p_camConfirm);
		}

		/// <summary>
		/// The AsyncRefreshBackedProfiles Task.
		/// </summary>
		/// <param name="p_pstRefreshBackedProfilesTask">The RefreshBackedProfiles Task.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		public async void AsyncRefreshBackedProfilesTask(RefreshBackedProfilesTask p_pstRefreshBackedProfilesTask, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (ModManager.LoginTask.LoggedIn)
				{
					p_pstRefreshBackedProfilesTask.Update(p_camConfirm);
					RefreshBackedProfilesStarted(this, new EventArgs<IBackgroundTask>(p_pstRefreshBackedProfilesTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		#region Remove Backed Profile

		/// <summary>
		/// Remove the Backed Profiles.
		/// </summary>
		/// <param name="p_impProfile">The Profile object.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask RemoveBackedProfile(IModProfile p_impProfile, ConfirmActionMethod p_camConfirm)
		{
			RemoveBackedProfileTask rpoRemoveProfile = new RemoveBackedProfileTask(ModRepository, p_impProfile, this);
			rpoRemoveProfile.Update(p_camConfirm);
			return rpoRemoveProfile;
		}

		/// <summary>
		/// Remove the Backed Profiles (async mode).
		/// </summary>
		/// <param name="p_impProfile">The Profile object.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public void AsyncRemoveBackedProfile(IModProfile p_impProfile, ConfirmActionMethod p_camConfirm)
		{
			RemoveBackedProfileTask rpoRemoveProfile = new RemoveBackedProfileTask(ModRepository, p_impProfile, this);
			AsyncRemoveBackedProfileTask(rpoRemoveProfile, p_camConfirm);
		}

		/// <summary>
		/// The AsyncRemoveBackedProfile Task.
		/// </summary>
		/// <param name="p_pstRemoveBackedProfileTask">The RemoveBackedProfile Task.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		public async void AsyncRemoveBackedProfileTask(RemoveBackedProfileTask p_pstRemoveBackedProfileTask, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (ModManager.LoginTask.LoggedIn)
				{
					p_pstRemoveBackedProfileTask.Update(p_camConfirm);
					RemoveBackedProfileStarted(this, new EventArgs<IBackgroundTask>(p_pstRemoveBackedProfileTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		#region Rename Backed Profile

		/// <summary>
		/// Rename the Backed Profiles.
		/// </summary>
		/// <param name="p_impProfile">The Profile object.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <param name="p_strname">The new Profile name.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask RenameBackedProfile(IModProfile p_impModProfile, string p_strname, ConfirmActionMethod p_camConfirm)
		{
			RenameBackedProfileTask rpoRenameProfile = new RenameBackedProfileTask(ModRepository, p_impModProfile, p_strname);
			rpoRenameProfile.Update(p_camConfirm);
			return rpoRenameProfile;
		}

		/// <summary>
		/// Rename the Backed Profiles (async mode).
		/// </summary>
		/// <param name="p_impProfile">The Profile object.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		/// <param name="p_strname">The new Profile name.</param>
		/// <returns>A background task set allowing the caller to track the progress of the operation.</returns>
		public IBackgroundTask AsyncRenameBackedProfile(IModProfile p_impModProfile, string p_strname, ConfirmActionMethod p_camConfirm)
		{
			RenameBackedProfileTask rpoRenameProfile = new RenameBackedProfileTask(ModRepository, p_impModProfile, p_strname);
			AsyncRenameBackedProfileTask(rpoRenameProfile, p_impModProfile, p_camConfirm);
			return rpoRenameProfile;
		}

		/// <summary>
		/// The AsyncRenameBackedProfile Task.
		/// </summary>
		/// <param name="p_pstRenameBackedProfileTask">The RenameBackedProfile Task.</param>
		/// <param name="p_camConfirm">The delegate to call to resolve conflicts with existing files.</param>
		public async void AsyncRenameBackedProfileTask(RenameBackedProfileTask p_pstRenameBackedProfileTask, IModProfile p_impModProfile, ConfirmActionMethod p_camConfirm)
		{
			int intRetry = 0;

			while (intRetry < 5)
			{
				await Task.Delay(3000);
				if (ModManager.LoginTask.LoggedIn)
				{
					p_pstRenameBackedProfileTask.Update(p_camConfirm);
					RenameBackedProfileStarted(p_impModProfile, new EventArgs<IBackgroundTask>(p_pstRenameBackedProfileTask));
					break;
				}
				else
				{
					intRetry++;
				}
			}
		}

		#endregion

		#region Task Management

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of a task set.
		/// </summary>
		/// <remarks>
		/// This displays the confirmation message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void RunningTask_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			if (m_tskRunningTask != null)
			{
				lock (m_objLock)
				{
					if (m_tskRunningTask != null)
					{
						m_tskRunningTask.TaskEnded -= RunningTask_TaskEnded;
						m_tskRunningTask = null;
					}
				}
			}
		}

		#endregion

		/// <summary>
		/// This backsup the profile manager.
		/// </summary>
		public void Backup()
		{
		}
	}
}
