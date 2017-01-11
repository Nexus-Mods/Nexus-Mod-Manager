using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Util;
using SevenZip;
using System.Text;

namespace Nexus.Client.Mods.Formats.FOMod
{
	/// <summary>
	/// Encapsulates a FOMod mod archive.
	/// </summary>
	public class FOMod : ObservableObject, IMod
	{
		#region Events

		/// <summary>
		/// Raised to update listeners on the progress of the read-only initialization.
		/// </summary>
		public event CancelProgressEventHandler ReadOnlyInitProgressUpdated = delegate { };

		#endregion

		#region Fields

		private string m_strFilePath = null;
		private string m_strNestedFilePath = null;
		private Archive m_arcFile = null;
		private string m_strCachePath = null;
		private string m_strPrefixPath = null;
		private Dictionary<string, string> m_dicMovedArchiveFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private string m_strReadmePath = null;
		private string m_strScreenshotPath = null;
		private string m_strInstallScriptPath = null;
		private IScriptType m_stpInstallScriptType = null;

		private string m_strModId = null;
		private string m_strDownloadId = null;
		private string m_strModName = null;
		private string m_strFileName = null;
		private string m_strHumanReadableVersion = null;
		private string m_strLastKnownVersion = null;
		private Int32 m_strCategoryId = 0;
		private Int32 m_strCustomCategoryId = 0;
		private bool? m_booIsEndorsed = false;
		private Version m_verMachineVersion = null;
		private string m_strAuthor = null;
		private string m_strDescription = null;
		private string m_strInstallDate = null;
        private Int32 m_intPlaceInModLoadOrder = -1;
        private Int32 m_intNewPlaceInMoadLoadOrder = -1;
		private Uri m_uriWebsite = null;
		private ExtendedImage m_ximScreenshot = null;
		private bool m_booUpdateWarningEnabled = true;
		private IScript m_scpInstallScript = null;
		private bool m_booUsesPlugins = false;
		private bool m_booMovedArchiveInitialized = false;
		private bool m_booAllowArchiveEdits = false;
		private bool m_booNestedArchives = false;

		protected List<string> IgnoreFolders = new List<string> { "__MACOSX" };

		#endregion

		#region Properties

		#region IModInfo Members

		/// <summary>
		/// Gets or sets the Id of the mod.
		/// </summary>
		/// <remarks>The id of the mod</remarks>
		public string Id
		{
			get
			{
				return m_strModId;
			}
			set
			{
				SetPropertyIfChanged(ref m_strModId, value, () => Id);
			}
		}

		/// <summary>
		/// Gets or sets the DownloadId of the mod.
		/// </summary>
		/// <remarks>The DownloadId of the mod</remarks>
		public string DownloadId
		{
			get
			{
				return m_strDownloadId;
			}
			set
			{
				SetPropertyIfChanged(ref m_strDownloadId, value, () => DownloadId);
			}
		}

		/// <summary>
		/// Gets or sets the filename of the mod.
		/// </summary>
		/// <value>The filename of the mod.</value>
		public string FileName
		{
			get
			{
				return m_strFileName;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strFileName, value, () => FileName);
			}
		}

		/// <summary>
		/// Gets or sets the name of the mod.
		/// </summary>
		/// <value>The name of the mod.</value>
		public string ModName
		{
			get
			{
				return m_strModName;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strModName, value, () => ModName);
			}
		}

		/// <summary>
		/// Gets or sets the human readable form of the mod's version.
		/// </summary>
		/// <value>The human readable form of the mod's version.</value>
		public string HumanReadableVersion
		{
			get
			{
				return m_strHumanReadableVersion;
			}
			set
			{
				SetPropertyIfChanged(ref m_strHumanReadableVersion, value, () => HumanReadableVersion);
			}
		}

		/// <summary>
		/// Gets or sets the last known mod version.
		/// </summary>
		/// <value>The last known mod version.</value>
		public string LastKnownVersion
		{
			get
			{
				return m_strLastKnownVersion;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strLastKnownVersion, value, () => LastKnownVersion);
			}
		}

		/// <summary>
		/// Gets or sets the category Id.
		/// </summary>
		/// <value>The category Id.</value>
		public Int32 CategoryId
		{
			get
			{
				return m_strCategoryId;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strCategoryId, value, () => CategoryId);
			}
		}

		/// <summary>
		/// Gets or sets the custom category Id.
		/// </summary>
		/// <value>The custom category Id.</value>
		public Int32 CustomCategoryId
		{
			get
			{
				return m_strCustomCategoryId;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strCustomCategoryId, value, () => CustomCategoryId);
			}
		}

		/// <summary>
		/// Gets or sets the Endorsement state of the mod.
		/// </summary>
		/// <value>The Endorsement state of the mod.</value>
		public bool? IsEndorsed
		{
			get
			{
				return m_booIsEndorsed;
			}
			set
			{
				SetPropertyIfChanged(ref m_booIsEndorsed, value, () => IsEndorsed);
			}
		}

		/// <summary>
		/// Gets or sets the version of the mod.
		/// </summary>
		/// <value>The version of the mod.</value>
		public Version MachineVersion
		{
			get
			{
				return m_verMachineVersion;
			}
			private set
			{
				SetPropertyIfChanged(ref m_verMachineVersion, value, () => MachineVersion);
			}
		}

		/// <summary>
		/// Gets or sets the author of the mod.
		/// </summary>
		/// <value>The author of the mod.</value>
		public string Author
		{
			get
			{
				return m_strAuthor;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strAuthor, value, () => Author);
			}
		}

		/// <summary>
		/// Gets or sets the description of the mod.
		/// </summary>
		/// <value>The description of the mod.</value>
		public string Description
		{
			get
			{
				return m_strDescription;
			}
			private set
			{
				SetPropertyIfChanged(ref m_strDescription, value, () => Description);
			}
		}

		/// <summary>
		/// Gets or sets the install date of the mod.
		/// </summary>
		/// <value>The install date of the mod.</value>
		public string InstallDate
		{
			get
			{
				return m_strInstallDate;
			}
			set
			{
				SetPropertyIfChanged(ref m_strInstallDate, value, () => InstallDate);
			}
		}

		/// <summary>
		/// Gets or sets the website of the mod.
		/// </summary>
		/// <value>The website of the mod.</value>
		public Uri Website
		{
			get
			{
				return m_uriWebsite;
			}
			private set
			{
				SetPropertyIfChanged(ref m_uriWebsite, value, () => Website);
			}
		}

		/// <summary>
		/// Gets or sets the mod's screenshot.
		/// </summary>
		/// <value>The mod's screenshot.</value>
		public ExtendedImage Screenshot
		{
			get
			{
				if ((m_ximScreenshot == null) && !String.IsNullOrEmpty(m_strScreenshotPath))
					m_ximScreenshot = new ExtendedImage(GetFile(m_strScreenshotPath));
				return m_ximScreenshot;
			}
			private set
			{
				if (CheckIfChanged(m_ximScreenshot, value))
				{
					if (value == null)
						m_strScreenshotPath = null;
					else
						m_strScreenshotPath = "fomod/screenshot" + value.GetExtension();
					SetPropertyIfChanged(ref m_ximScreenshot, value, () => Screenshot);
				}
			}
		}

		/// <summary>
		/// Gets or sets whether the user wants to be warned about new versions.
		/// </summary>
		/// <value>Whether the user wants to be warned about new versions</value>
		public bool UpdateWarningEnabled
		{
			get
			{
				return m_booUpdateWarningEnabled;
			}
			set
			{
				SetPropertyIfChanged(ref m_booUpdateWarningEnabled, value, () => UpdateWarningEnabled);
			}
		}

		#endregion

		#region IScriptedMod Members

		/// <summary>
		/// Gets whether the mod has a custom install script.
		/// </summary>
		/// <value>Whether the mod has a custom install script.</value>
		public bool HasInstallScript
		{
			get
			{
				return InstallScript != null;
			}
		}

		/// <summary>
		/// Gets or sets the mod's install script.
		/// </summary>
		/// <value>The mod's install script.</value>
		public IScript InstallScript
		{
			get
			{
				if ((m_scpInstallScript == null) && !String.IsNullOrEmpty(m_strInstallScriptPath))
					m_scpInstallScript = m_stpInstallScriptType.LoadScript(TextUtil.ByteToString(GetFile(m_strInstallScriptPath)));
				return m_scpInstallScript;
			}
			set
			{
				m_scpInstallScript = value;
				if (m_scpInstallScript == null)
				{
					m_stpInstallScriptType = null;
					m_strInstallScriptPath = null;
				}
				else
				{
					m_stpInstallScriptType = m_scpInstallScript.Type;
					m_strInstallScriptPath = Path.Combine("fomod", m_stpInstallScriptType.FileNames[0]);
				}
			}
		}

		#endregion

		/// <summary>
		/// Gets the internal path to the screenshot.
		/// </summary>
		/// <remarks>
		/// This property return a path that can be passed to the <see cref="GetFile(string)"/>
		/// method.
		/// </remarks>
		/// <value>The internal path to the screenshot.</value>
		public string ScreenshotPath
		{
			get
			{
				return m_strScreenshotPath;
			}
		}

		/// <summary>
		/// Gets the filename of the mod.
		/// </summary>
		/// <value>The filename of the mod.</value>
		public string Filename
		{
			get
			{
				return String.IsNullOrEmpty(m_strNestedFilePath) ? m_strFilePath : m_strNestedFilePath;
			}
		}

		/// <summary>
		/// Gets the path to the mod archive.
		/// </summary>
		/// <value>The path to the mod archive.</value>
		public string ModArchivePath
		{
			get
			{
				return m_strFilePath;
			}
		}

        public int PlaceInModLoadOrder
        {
            get
            {
                return m_intPlaceInModLoadOrder;
            }
            set
            {
                SetPropertyIfChanged(ref m_intPlaceInModLoadOrder, value, () => PlaceInModLoadOrder);
            }
        }

        public int NewPlaceInModLoadOrder
        {
            get
            {
                return m_intNewPlaceInMoadLoadOrder;
            }
            set
            {
                SetPropertyIfChanged(ref m_intNewPlaceInMoadLoadOrder, value, () => NewPlaceInModLoadOrder);
            }
        }

		/// <summary>
		/// Gets the registry of supported script types.
		/// </summary>
		/// <value>The registry of supported script types.</value>
		protected IScriptTypeRegistry ScriptTypeRegistry { get; private set; }

		/// <summary>
		/// Gets the format of the mod.
		/// </summary>
		/// <value>The format of the mod.</value>
		public IModFormat Format { get; private set; }

		protected IList<string> StopFolders { get; private set; }
		protected string PluginsDirectoryName { get; private set; }
		protected IList<string> PluginExtensions { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the FOMod from the specified file.
		/// </summary>
		/// <param name="p_strFilePath">The mod file from which to create the FOMod.</param>
		/// <param name="p_mftModFormat">The format of the mod.</param>
		/// <param name="p_strStopFolders">A list of folders names that indicate the root of the mod file structure.</param>
		/// <param name="p_strPluginsDirectoryName">The name of the folder that contains plugins.</param>
		/// <param name="p_mcmModCacheManager">The manager for the current game mode's mod cache.</param>
		/// <param name="p_stgScriptTypeRegistry">The registry of supported script types.</param>
		public FOMod(string p_strFilePath, FOModFormat p_mftModFormat, IEnumerable<string> p_enmStopFolders, string p_strPluginsDirectoryName, IEnumerable<string> p_enmPluginExtensions, IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry, bool p_booUsePlugins, bool p_booNestedArchives)
		{
			StopFolders = new List<string>(p_enmStopFolders);
			if (!StopFolders.Contains("fomod", StringComparer.OrdinalIgnoreCase))
				StopFolders.Add("fomod");
			PluginsDirectoryName = p_strPluginsDirectoryName;
			PluginExtensions = new List<string>(p_enmPluginExtensions);
			m_booNestedArchives = p_booNestedArchives;

			Format = p_mftModFormat;
			ScriptTypeRegistry = p_stgScriptTypeRegistry;
			bool p_booUseCache = true;
			m_booUsesPlugins = p_booUsePlugins;
			bool booCheckNested = m_booNestedArchives;
			bool booCheckPrefix = true;
			bool booCheckScript = true;
			bool booUpdateCacheInfo = false;
			bool booDirtyCache = false;
			string strCheckPrefix = null;
			string strCheckScriptPath = null;
			string strCheckScriptType = null;

			m_strFilePath = p_strFilePath;
			m_arcFile = new Archive(p_strFilePath);

			p_mcmModCacheManager.MigrateCacheFile(this);

			#region Check for cacheInfo.txt file

			string strCachePath = Path.Combine(p_mcmModCacheManager.ModCacheDirectory, Path.GetFileNameWithoutExtension(p_strFilePath));
			m_strCachePath = strCachePath;
						
			if (Directory.Exists(strCachePath))
			{
				string strCacheInfoFile = Path.Combine(strCachePath, "cacheInfo.txt");

				if (File.Exists(strCacheInfoFile))
				{
					byte[] bCacheInfo = File.ReadAllBytes(strCacheInfoFile);
					string sCacheInfo = Encoding.UTF8.GetString(bCacheInfo, 0, bCacheInfo.Length);
					string[] strPref = sCacheInfo.Split(new string[] { "@@" }, StringSplitOptions.RemoveEmptyEntries);
					if (strPref.Length > 0)
					{
						booCheckNested = Convert.ToBoolean(strPref[0]);

						if (strPref.Length > 1)
						{
							strCheckPrefix = strPref[1];
							foreach (string Folder in IgnoreFolders)
							{
								if (strCheckPrefix.IndexOf(Folder, StringComparison.InvariantCultureIgnoreCase) >= 0)
								{
									booCheckNested = true;
									strCheckPrefix = string.Empty;
									booDirtyCache = true;
									break;
								}
							}

							if (string.IsNullOrEmpty(strCheckPrefix) || !strCheckPrefix.Equals("-"))
							{
								if (((p_enmStopFolders == null) || (p_enmStopFolders.Count() <= 0)) && !p_booUsePlugins)
								{
									FileUtil.ForceDelete(m_strCachePath);
									booCheckNested = true;
									strCheckPrefix = string.Empty;
									booDirtyCache = true;
								}
							}

							if (!booDirtyCache)
							{

								if (strCheckPrefix.Equals("-"))
									strCheckPrefix = String.Empty;
								booCheckPrefix = false;

								if (strPref.Length > 2)
								{
									strCheckScriptPath = strPref[2];
									if (strCheckScriptPath.Equals("-"))
										strCheckScriptPath = String.Empty;
									strCheckScriptType = strPref[3];
									if (strCheckScriptType.Equals("-"))
										strCheckScriptType = String.Empty;
									booCheckScript = false;
								}
							}
						}
					}
				}
			}

			#endregion

			if (booCheckNested && m_booNestedArchives)
			{
				#region Temporary fix for nested .dazip files
				string[] strNested = m_arcFile.GetFiles("", "*.dazip", true);
				if (strNested.Length == 1)
				{
					string strFilePath = Path.Combine(Path.Combine(Path.GetTempPath(), "NMM"), strNested[0]);
					FileUtil.WriteAllBytes(strFilePath, GetFile(strNested[0]));
					if (File.Exists(strFilePath))
					{
						m_arcFile = new Archive(strFilePath);
						m_strNestedFilePath = strFilePath;
					}
				}
				#endregion
			}

			m_arcFile.ReadOnlyInitProgressUpdated += new CancelProgressEventHandler(ArchiveFile_ReadOnlyInitProgressUpdated);

			if (booCheckPrefix)
			{
				FindPathPrefix();
				booUpdateCacheInfo = true;
			}
			else
			{
				m_strPrefixPath = String.IsNullOrEmpty(strCheckPrefix) ? String.Empty : strCheckPrefix;
			}

			//check for script
			if (booCheckScript)
			{
				foreach (IScriptType stpScript in p_stgScriptTypeRegistry.Types)
				{
					foreach (string strScriptName in stpScript.FileNames)
					{
						string strScriptPath = Path.Combine("fomod", strScriptName);
						if (ContainsFile(strScriptPath))
						{
							m_strInstallScriptPath = strScriptPath;
							m_stpInstallScriptType = stpScript;
							break;
						}
					}
					if (!String.IsNullOrEmpty(m_strInstallScriptPath))
						break;
				}

				booUpdateCacheInfo = true;
			}
			else
			{
				m_strInstallScriptPath = strCheckScriptPath;
				m_stpInstallScriptType = String.IsNullOrEmpty(strCheckScriptType) ? null : p_stgScriptTypeRegistry.Types.FirstOrDefault(x => x.TypeName.Equals(strCheckScriptType));
			}

			m_arcFile.FilesChanged += new EventHandler(Archive_FilesChanged);
			
			//check for screenshot
			string[] strScreenshots = null;

			if ((p_booUseCache) && (Directory.Exists(m_strCachePath)))
			{
				string[] fileList = Directory.GetFiles(Path.Combine(m_strCachePath, GetRealPath("fomod")), "screenshot*", SearchOption.AllDirectories);
				//if (fileList.Length > 0)
					strScreenshots = fileList;
			}
			else
				strScreenshots = m_arcFile.GetFiles(GetRealPath("fomod"), "screenshot*", false);

			//TODO make sure the file is a valid image
			if (strScreenshots.Length > 0)
				m_strScreenshotPath = strScreenshots[0];

			string strTmpInfo = string.Empty;
			string cacheFile = Path.Combine(strCachePath, "cacheInfo.txt");

			if ((p_booUseCache) && (!File.Exists(cacheFile)))
			{
				strTmpInfo = p_mcmModCacheManager.FileUtility.CreateTempDirectory();
				try
				{
					Directory.CreateDirectory(Path.Combine(strTmpInfo, GetRealPath("fomod")));

					if (ContainsFile("fomod/info.xml"))
						FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath("fomod/info.xml")), GetFile("fomod/info.xml"));
					else
						FileUtil.WriteAllText(Path.Combine(strTmpInfo, GetRealPath("fomod/info.xml")), "<fomod/>");

					if (!String.IsNullOrEmpty(m_strReadmePath))
						FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(m_strReadmePath)), GetFile(m_strReadmePath));

					if (!String.IsNullOrEmpty(m_strScreenshotPath))
						FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(m_strScreenshotPath)), GetFile(m_strScreenshotPath));

					p_mcmModCacheManager.CreateCacheFile(this, strTmpInfo);
				}
				finally
				{
					FileUtil.ForceDelete(strTmpInfo);
				}
			}

			if (booUpdateCacheInfo || (!File.Exists(cacheFile)))
			{

				Byte[] bteText = new UTF8Encoding(true).GetBytes(String.Format("{0}@@{1}@@{2}@@{3}",
					(!String.IsNullOrEmpty(m_strNestedFilePath)).ToString(),
					String.IsNullOrEmpty(m_strPrefixPath) ? "-" : m_strPrefixPath,
					String.IsNullOrEmpty(m_strInstallScriptPath) ? "-" : m_strInstallScriptPath,
					(m_stpInstallScriptType == null) ? "-" : m_stpInstallScriptType.TypeName));

				if (bteText != null)
				{
					try
					{
						File.WriteAllBytes(cacheFile, bteText);
					}
					catch (Exception ex)
					{

					}
				}
					
			}

			ModName = Path.GetFileNameWithoutExtension(m_strFilePath);
			LoadInfo();
		}

		#endregion

		#region Initialization

		/// <summary>
		/// Loads the mod metadata from the info file.
		/// </summary>
		protected void LoadInfo()
		{
			if (ContainsFile("fomod/info.xml"))
			{
				XmlDocument xmlInfo = new XmlDocument();
				xmlInfo.LoadXml(TextUtil.ByteToString(GetFile("fomod/info.xml")));
				LoadInfo(xmlInfo, false);
			}
		}

		#endregion

		#region Read Transactions

		/// <summary>
		/// Starts a read-only transaction.
		/// </summary>
		/// <remarks>
		/// This puts the FOMod into read-only mode.
		/// 
		/// Read-only mode can greatly increase the speed at which multiple file are extracted.
		/// </remarks>
		public void BeginReadOnlyTransaction(FileUtil p_futFileUtil)
		{
			m_arcFile.BeginReadOnlyTransaction(p_futFileUtil);
		}

		/// <summary>
		/// Ends a read-only transaction.
		/// </summary>
		/// <remarks>
		/// This takes the FOMod out of read-only mode.
		/// 
		/// Read-only mode can greatly increase the speed at which multiple file are extracted.
		/// </remarks>
		public void EndReadOnlyTransaction()
		{
			m_arcFile.EndReadOnlyTransaction();
		}

		/// <summary>
		/// Handles the <see cref="Archive.ReadOnlyInitProgressUpdated"/> event of the mod's archive file.
		/// </summary>
		/// <remarks>
		/// This raises the mod's <see cref="ReadOnlyInitProgressUpdated"/> event.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="CancelProgressEventArgs"/> describing the event arguments.</param>
		private void ArchiveFile_ReadOnlyInitProgressUpdated(object sender, CancelProgressEventArgs e)
		{
			ReadOnlyInitProgressUpdated(this, e);
		}

		#endregion

		#region Archive Interaction

		/// <summary>
		/// This finds where in the archive the FOMod file structure begins.
		/// </summary>
		/// <remarks>
		/// This methods finds the path prefix to the folder containing the core files and folders of the FOMod. If
		/// there are any files that are above the core folder, than they are given new file names inside the
		/// core folder.
		/// </remarks>
		protected void FindPathPrefix()
		{
			string strPrefixPath = null;
			Stack<string> stkPaths = new Stack<string>();
			stkPaths.Push("/");

			while (stkPaths.Count > 0)
			{
				string strSourcePath = stkPaths.Pop();
				string[] directories = m_arcFile.GetDirectories(strSourcePath);
				bool booFoundData = false;
				bool booFoundPrefix = false;
				foreach (string strDirectory in directories)
				{
					bool booSkipFolder = false;

					foreach (string Folder in IgnoreFolders)
						if (strDirectory.IndexOf(Folder, StringComparison.InvariantCultureIgnoreCase) >= 0)
						{
							booSkipFolder = true;
							break;
						}

					if (booSkipFolder)
						continue;

					stkPaths.Push(strDirectory);
					if (StopFolders.Contains(Path.GetFileName(strDirectory).ToLowerInvariant()))
					{
						booFoundPrefix = true;
						break;
					}
					if (m_booUsesPlugins)
						booFoundData |= Path.GetFileName(strDirectory).Equals(PluginsDirectoryName, StringComparison.OrdinalIgnoreCase);
				}
				if (booFoundPrefix)
				{
					strPrefixPath = strSourcePath;
					break;
				}
				if (booFoundData)
				{
					strPrefixPath = Path.Combine(strSourcePath, PluginsDirectoryName);
					break;
				}
				if (!booFoundData)
				{
					bool booFound = false;
					foreach (string strExtension in PluginExtensions)
						if (m_arcFile.GetFiles(strSourcePath, "*" + strExtension, false).Length > 0)
						{
							booFound = true;
							break;
						}
					if (booFound)
					{
						strPrefixPath = strSourcePath;
						break;
					}
				}
			}

			strPrefixPath = (strPrefixPath == null) ? "" : strPrefixPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (!String.IsNullOrEmpty(strPrefixPath))
				strPrefixPath = InitializeMovedArchive(strPrefixPath);

			m_booMovedArchiveInitialized = true;
			m_strPrefixPath = strPrefixPath;
		}

		private string InitializeMovedArchive(string p_strPathPrefix)
		{
			p_strPathPrefix = p_strPathPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			p_strPathPrefix = p_strPathPrefix.Trim(Path.DirectorySeparatorChar);
			p_strPathPrefix += Path.DirectorySeparatorChar;
			m_dicMovedArchiveFiles.Clear();
			string[] strFiles = m_arcFile.GetFiles("/", true);
			Int32 intTrimLength = p_strPathPrefix.Length;
			for (Int32 i = strFiles.Length - 1; i >= 0; i--)
			{
				strFiles[i] = strFiles[i].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				string strFile = strFiles[i];
				string strNewFileName = null;
				if (!strFile.StartsWith(p_strPathPrefix, StringComparison.OrdinalIgnoreCase))
				{
					strNewFileName = strFile;
					string strDirectory = Path.GetDirectoryName(strNewFileName);
					string strFileName = Path.GetFileNameWithoutExtension(strFile);
					string strExtension = Path.GetExtension(strFile);
					for (Int32 j = 1; m_dicMovedArchiveFiles.ContainsKey(strNewFileName); j++)
						strNewFileName = Path.Combine(strDirectory, strFileName + " " + j + strExtension);
				}
				else
					strNewFileName = strFile.Remove(0, intTrimLength);
				m_dicMovedArchiveFiles[strNewFileName] = strFile;
			}

			return p_strPathPrefix;
		}

		/// <summary>
		/// Handles the <see cref="Archive.FilesChanged"/> event of the FOMod's archive.
		/// </summary>
		/// <remarks>
		/// This ensures that the path prefix that points to the folder in the archive that contains the core files
		/// and folders of the FOMod is updated when the archive changes.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs"/> describing the event arguments.</param>
		private void Archive_FilesChanged(object sender, EventArgs e)
		{
			FindPathPrefix();
		}

		/// <summary>
		/// Determines if the FOMod contains the given file.
		/// </summary>
		/// <param name="p_strPath">The filename whose existence in the FOMod is to be determined.</param>
		/// <returns><c>true</c> if the specified file is in the FOMod; <c>false</c> otherwise.</returns>
		public bool ContainsFile(string p_strPath)
		{
			return ContainsFile(p_strPath, false);
		}

		/// <summary>
		/// Determines if the FOMod contains the given file.
		/// </summary>
		/// <param name="p_strPath">The filename whose existence in the FOMod is to be determined.</param>
		/// <returns><c>true</c> if the specified file is in the FOMod; <c>false</c> otherwise.</returns>
		private bool ContainsFile(string p_strPath, bool p_booCacheOnly)
		{
			string strPath = p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPath = strPath.Trim(Path.DirectorySeparatorChar);

			if ((Directory.Exists(m_strCachePath) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(strPath))))))
				return true;

			if (p_booCacheOnly)
				return false;

			if (m_dicMovedArchiveFiles.ContainsKey(strPath))
				return true;
			if (m_arcFile.ContainsFile(GetRealPath(strPath)))
				return true;
			return false;
		}

		/// <summary>
		/// This method adjusts the given virtual path to the actual path to the
		/// file in the mod.
		/// </summary>
		/// <remarks>
		/// This method account for the virtual restructuring of the mod file structure performed by
		/// <see cref="FindPathPrefix()"/>.
		/// </remarks>
		/// <param name="p_strPath">The path to adjust.</param>
		/// <returns>The adjusted path.</returns>
		protected string GetRealPath(string p_strPath)
		{
			string strPath = p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPath = strPath.Trim(Path.DirectorySeparatorChar);
			string strAdjustedPath = null;
			if (m_dicMovedArchiveFiles.TryGetValue(strPath, out strAdjustedPath))
				return strAdjustedPath;
			if (String.IsNullOrEmpty(m_strPrefixPath))
				return p_strPath;
			if (strPath.ToLowerInvariant().IndexOf(m_strPrefixPath.ToLowerInvariant()) == 0)
				return p_strPath;
			else
				return Path.Combine(m_strPrefixPath, p_strPath);
		}

		/// <summary>
		/// Deletes the specified file.
		/// </summary>
		/// <param name="p_strPath">The path of the file to delete.</param>
		protected void DeleteFile(string p_strPath)
		{
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly && !m_arcFile.IsSolid))
				m_arcFile.DeleteFile(GetRealPath(p_strPath));
			if ((Directory.Exists(m_strCachePath) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strPath))))))
				FileUtil.ForceDelete(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
		}

		/// <summary>
		/// Replaces the specified file with the given data.
		/// </summary>
		/// <param name="p_strPath">The path of the file to replace.</param>
		/// <param name="p_bteData">The new file data.</param>
		protected void ReplaceFile(string p_strPath, byte[] p_bteData)
		{
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly && !m_arcFile.IsSolid))
				m_arcFile.ReplaceFile(GetRealPath(p_strPath), p_bteData);
			FileInfo fiFile = new FileInfo(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
			if ((Directory.Exists(m_strCachePath) && ((File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strPath)))) || (fiFile.IsReadOnly))))
			{
				//FileUtil.ForceDelete(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
				File.WriteAllBytes(Path.Combine(m_strCachePath, GetRealPath(p_strPath)), p_bteData);
			}
		}

		/// <summary>
		/// Replaces the specified file with the given data.
		/// </summary>
		/// <param name="p_strPath">The path of the file to replace.</param>
		/// <param name="p_bteData">The new file data.</param>
		protected void CreateOrReplaceFile(string p_strPath, byte[] p_bteData)
		{
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly && !m_arcFile.IsSolid))
				m_arcFile.ReplaceFile(GetRealPath(p_strPath), p_bteData);
			if (Directory.Exists(m_strCachePath))
			{
				FileUtil.ForceDelete(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
				File.WriteAllBytes(Path.Combine(m_strCachePath, GetRealPath(p_strPath)), p_bteData);
			}
		}

		/// <summary>
		/// Replaces the specified file with the given text.
		/// </summary>
		/// <param name="p_strPath">The path of the file to replace.</param>
		/// <param name="p_strData">The new file text.</param>
		protected void ReplaceFile(string p_strPath, string p_strData)
		{
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly && !m_arcFile.IsSolid))
				m_arcFile.ReplaceFile(GetRealPath(p_strPath), p_strData);
			FileInfo fiFile = new FileInfo(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
			
			if ((Directory.Exists(m_strCachePath) && ((File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strPath)))) || (fiFile.IsReadOnly))))
			{
				FileUtil.ForceDelete(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
				File.WriteAllText(Path.Combine(m_strCachePath, GetRealPath(p_strPath)), p_strData);
			}
		}

		#endregion

		#region File Management

		/// <summary>
		/// Retrieves the specified file from the fomod.
		/// </summary>
		/// <param name="p_strFile">The file to retrieve.</param>
		/// <returns>The requested file data.</returns>
		/// <exception cref="FileNotFoundException">Thrown if the specified file
		/// is not in the fomod.</exception>
		public byte[] GetFile(string p_strFile)
		{
			if (!ContainsFile(p_strFile))
			{
				if (Path.GetFileNameWithoutExtension(p_strFile).ToLower() == "screenshot")
					return (byte[])(new ImageConverter().ConvertTo(new Bitmap(1, 1), typeof(byte[])));
				else
					throw new FileNotFoundException("File doesn't exist in FOMod", p_strFile);
			}

			if ((Directory.Exists(m_strCachePath) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strFile))))))
				return File.ReadAllBytes(Path.Combine(m_strCachePath, GetRealPath(p_strFile)));		
			return m_arcFile.GetFileContents(GetRealPath(p_strFile));
		}

		/// <summary>
		/// Retrieves the list of files in this FOMod.
		/// </summary>
		/// <returns>The list of files in this FOMod.</returns>
		public List<string> GetFileList()
		{
			return GetFileList(null, true);
		}

		/// <summary>
		/// Determines if last known version is the same as the current version.
		/// </summary>
		/// <returns><c>true</c> if the versions are the same;
		/// <c>false</c> otherwise.</returns>
		public bool IsMatchingVersion()
		{
			Regex rgxClean = new Regex(@"([v(ver)]\.?)|((\.0)+$)", RegexOptions.IgnoreCase);
			string strThisVersion = rgxClean.Replace(m_strHumanReadableVersion ?? "", "");
			string strThatVersion = rgxClean.Replace(m_strLastKnownVersion ?? "", "");
			if (String.IsNullOrEmpty(strThisVersion) || string.IsNullOrEmpty(strThatVersion))
				return true;
			else
				return String.Equals(strThisVersion, strThatVersion, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Retrieves the list of all files in the specified FOMod folder.
		/// </summary>
		/// <param name="p_strFolderPath">The FOMod folder whose file list is to be retrieved.</param>
		/// <param name="p_booRecurse">Whether to return files that are in subdirectories of the given directory.</param>
		/// <returns>The list of all files in the specified FOMod folder.</returns>
		public List<string> GetFileList(string p_strFolderPath, bool p_booRecurse)
		{
			List<string> lstFiles = new List<string>();
			if (!m_booMovedArchiveInitialized)
			{
				if (!String.IsNullOrEmpty(m_strPrefixPath))
					InitializeMovedArchive(m_strPrefixPath);
				m_booMovedArchiveInitialized = true;
			}
			foreach (string strFile in m_arcFile.GetFiles(p_strFolderPath, p_booRecurse))
				if (!m_dicMovedArchiveFiles.ContainsValue(strFile))
					if (!strFile.StartsWith("fomod", StringComparison.OrdinalIgnoreCase))
						lstFiles.Add(strFile);
			string strPathPrefix = p_strFolderPath ?? "";
			strPathPrefix = strPathPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPathPrefix = strPathPrefix.Trim(Path.DirectorySeparatorChar);
			if (strPathPrefix.Length > 0)
				strPathPrefix += Path.DirectorySeparatorChar;
			foreach (string strFile in m_dicMovedArchiveFiles.Keys)
				if (strFile.StartsWith(strPathPrefix, StringComparison.OrdinalIgnoreCase) && !strFile.StartsWith("fomod", StringComparison.OrdinalIgnoreCase))
					lstFiles.Add(strFile);
			lstFiles.Sort(CompareOrderFoldersFirst);
			return lstFiles;
		}

		#endregion

		#region Mod Info Management

		/// <summary>
		/// Updates the object's properties to the values of the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="p_mifInfo">The <see cref="IModInfo"/> whose values
		/// are to be used to update this object's properties.</param>
		/// <param name="p_booOverwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		public void UpdateInfo(IModInfo p_mifInfo, bool? p_booOverwriteAllValues)
		{
			bool booChangedValue = false;
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(Id))
			{
				Id = p_mifInfo.Id;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(DownloadId) || (p_booOverwriteAllValues == null))
			{
				DownloadId = p_mifInfo.DownloadId;
				booChangedValue = true;
			}
			if (((p_booOverwriteAllValues != false) || String.IsNullOrEmpty(ModName) || ModName.Equals(Path.GetFileNameWithoutExtension(m_strFilePath))) && !String.IsNullOrEmpty(p_mifInfo.ModName))
			{
				ModName = p_mifInfo.ModName;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(FileName))
			{
				FileName = p_mifInfo.FileName;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(HumanReadableVersion))
			{
				HumanReadableVersion = p_mifInfo.HumanReadableVersion;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(LastKnownVersion) || (LastKnownVersion != p_mifInfo.LastKnownVersion))
			{
				LastKnownVersion = p_mifInfo.LastKnownVersion;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || (IsEndorsed != p_mifInfo.IsEndorsed))
			{
				IsEndorsed = p_mifInfo.IsEndorsed;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || (MachineVersion == null))
			{
				MachineVersion = p_mifInfo.MachineVersion;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(Author) || (p_booOverwriteAllValues == null))
			{
				Author = p_mifInfo.Author;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || (CategoryId != p_mifInfo.CategoryId) || (p_booOverwriteAllValues == null))
			{
				CategoryId = p_mifInfo.CategoryId;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || (CustomCategoryId != p_mifInfo.CustomCategoryId))
			{
				CustomCategoryId = p_mifInfo.CustomCategoryId;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(Description) || (p_booOverwriteAllValues == null))
			{
				Description = p_mifInfo.Description;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || String.IsNullOrEmpty(InstallDate))
			{
				InstallDate = p_mifInfo.InstallDate;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || (Website == null) || (p_booOverwriteAllValues == null))
			{
				Website = p_mifInfo.Website;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues == true) || (UpdateWarningEnabled != p_mifInfo.UpdateWarningEnabled))
			{
				UpdateWarningEnabled = p_mifInfo.UpdateWarningEnabled;
				booChangedValue = true;
			}

			if (booChangedValue)
			{
				XmlDocument xmlInfo = new XmlDocument();
				xmlInfo.AppendChild(SaveInfo(xmlInfo));
				using (MemoryStream mstInfo = new MemoryStream())
				{
					xmlInfo.Save(mstInfo);
					ReplaceFile("fomod/info.xml", mstInfo.ToArray());
				}
			}

			if ((p_booOverwriteAllValues == true) || (Screenshot != p_mifInfo.Screenshot))
			{
				if (p_mifInfo.Screenshot == null)
				{
					if ((Screenshot != null) && (p_booOverwriteAllValues == true))
					{
						DeleteFile(m_strScreenshotPath);
						Screenshot = p_mifInfo.Screenshot;
					}
				}
				else
				{
					Screenshot = p_mifInfo.Screenshot;
					CreateOrReplaceFile(m_strScreenshotPath, Screenshot.Data);
				}
			}
		}

		/// <summary>
		/// Serializes this <see cref="IModInfo"/> to an XML fragment.
		/// </summary>
		/// <param name="p_xmlDocument">The <see cref="XmlDocument"/> to use to create the XML elements
		/// created during the unparsing.</param>
		/// <returns>The <see cref="XmlNode"/> that is the root of the XML fragement
		/// that represents this <see cref="IModInfo"/>.</returns>
		protected XmlNode SaveInfo(XmlDocument p_xmlDocument)
		{
			XmlNode xndInfo = p_xmlDocument.CreateElement("fomod");
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Name")).InnerText = ModName;
			XmlNode xndVersion = xndInfo.AppendChild(p_xmlDocument.CreateElement("Version"));
			xndVersion.InnerText = HumanReadableVersion;
			if (MachineVersion != null)
				xndVersion.Attributes.Append(p_xmlDocument.CreateAttribute("MachineVersion")).Value = MachineVersion.ToString();
			xndInfo.AppendChild(p_xmlDocument.CreateElement("LatestKnownVersion")).InnerText = LastKnownVersion;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Id")).InnerText = Id;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("DownloadId")).InnerText = DownloadId;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Author")).InnerText = Author;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("CategoryId")).InnerText = CategoryId.ToString();
			xndInfo.AppendChild(p_xmlDocument.CreateElement("CustomCategoryId")).InnerText = CustomCategoryId.ToString();
			xndInfo.AppendChild(p_xmlDocument.CreateElement("IsEndorsed")).InnerText = IsEndorsed.ToString();
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Description")).InnerText = Description;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("UpdateWarningEnabled")).InnerText = UpdateWarningEnabled.ToString();
            xndInfo.AppendChild(p_xmlDocument.CreateElement("PlaceInLoadOrder")).InnerText = PlaceInModLoadOrder.ToString();
			if (Website != null)
				xndInfo.AppendChild(p_xmlDocument.CreateElement("Website")).InnerText = Website.ToString();
			return xndInfo;
		}

		/// <summary>
		/// Deserializes an <see cref="IModInfo"/> from the given XML fragment.
		/// </summary>
		/// <param name="p_xndInfo">The XML fragment from which to deserialize the <see cref="IModInfo"/>.</param>
		/// <param name="p_booFillOnlyEmptyValues">Whether to only overwrite <c>null</c> or empty values.</param>
		protected void LoadInfo(XmlNode p_xndInfo, bool p_booFillOnlyEmptyValues)
		{
			XmlNode xndRoot = p_xndInfo.SelectSingleNode("fomod");
			XmlNode xndModName = xndRoot.SelectSingleNode("Name");
			if ((xndModName != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(ModName)))
				ModName = xndModName.InnerText;

			XmlNode xndVersion = xndRoot.SelectSingleNode("Version");
			if (xndVersion != null)
			{
				if ((!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(HumanReadableVersion)))
					HumanReadableVersion = xndVersion.InnerText;

				XmlAttribute xatMachineVersion = xndVersion.Attributes["MachineVersion"];
				if ((xatMachineVersion != null) && (!p_booFillOnlyEmptyValues || (MachineVersion == null)))
				{
					try
					{
						MachineVersion = new Version(xatMachineVersion.Value);
					}
					catch (System.FormatException)
					{
						MachineVersion = new Version(Regex.Replace(xatMachineVersion.Value, "[^.0-9]", ""));
					}
				}
			}

			XmlNode xndLastKnownVersion = xndRoot.SelectSingleNode("LatestKnownVersion");
			if ((xndLastKnownVersion != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(LastKnownVersion)))
				LastKnownVersion = xndLastKnownVersion.InnerText;

			XmlNode xndId = xndRoot.SelectSingleNode("Id");
			if ((xndId != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Id)))
				Id = xndId.InnerText;

			XmlNode xndDownloadId = xndRoot.SelectSingleNode("DownloadId");
			if ((xndDownloadId != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(DownloadId)))
				DownloadId = xndDownloadId.InnerText;

			XmlNode xndAuthor = xndRoot.SelectSingleNode("Author");
			if ((xndAuthor != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Author)))
				Author = xndAuthor.InnerText;

			XmlNode xndCategory = xndRoot.SelectSingleNode("CategoryId");
			if ((xndCategory != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(xndCategory.InnerText)))
				CategoryId = Convert.ToInt32(xndCategory.InnerText);
			else
				CategoryId = 0;

			XmlNode xndCustomCategory = xndRoot.SelectSingleNode("CustomCategoryId");
			if ((xndCustomCategory != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(xndCustomCategory.InnerText)))
				CustomCategoryId = Convert.ToInt32(xndCustomCategory.InnerText);
			else
				CustomCategoryId = -1;

			XmlNode xndEndorsed = xndRoot.SelectSingleNode("IsEndorsed");
			if (xndEndorsed != null)
			{
				try
				{
					bool booEndorsed;
					IsEndorsed = Boolean.TryParse(xndEndorsed.InnerText, out booEndorsed) ? (bool?)booEndorsed : null;
				}
				catch
				{
					IsEndorsed = null;
				}
			}

			XmlNode xndDescription = xndRoot.SelectSingleNode("Description");
			if ((xndDescription != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Description)))
				Description = xndDescription.InnerText;

			XmlNode xndWebsite = xndRoot.SelectSingleNode("Website");
			if ((xndWebsite != null) && (!String.IsNullOrEmpty(xndWebsite.InnerText)) && (!p_booFillOnlyEmptyValues || (Website == null)))
			{
				Uri uriUrl = null;
				if (UriUtil.TryBuildUri(xndWebsite.InnerText, out uriUrl))
					Website = uriUrl;
			}

			if (String.IsNullOrEmpty(LastKnownVersion))
				UpdateWarningEnabled = false;
			else
			{
				XmlNode xndUpdateWarningEnabled = xndRoot.SelectSingleNode("UpdateWarningEnabled");
				if (xndUpdateWarningEnabled != null)
				{
					try
					{
						UpdateWarningEnabled = Convert.ToBoolean(xndUpdateWarningEnabled.InnerText);
					}
					catch
					{
						UpdateWarningEnabled = true;
					}
				}
			}

            XmlNode xndPlaceInLoadOrder = xndRoot.SelectSingleNode("PlaceInLoadOrder");
            if (xndPlaceInLoadOrder != null && !String.IsNullOrEmpty(xndPlaceInLoadOrder.InnerText) && (!p_booFillOnlyEmptyValues || PlaceInModLoadOrder == -1))
            {
                PlaceInModLoadOrder = Int32.Parse(xndPlaceInLoadOrder.InnerText);
                NewPlaceInModLoadOrder = PlaceInModLoadOrder;
            }
		}

		#endregion

		/// <summary>
		/// Uses the mod name to represent the mod.
		/// </summary>
		/// <returns>The mod name.</returns>
		public override string ToString()
		{
			return ModName;
		}

		public static int CompareOrderFoldersFirst(string x, string y)
		{
			if (String.IsNullOrEmpty(x))
			{
				if (String.IsNullOrEmpty(y))
					return 0;
				else
					return -1;

			}
			else
			{
				if (String.IsNullOrEmpty(y))
					return 1;
				else
				{
					string xDir = Path.GetDirectoryName(x);
					string yDir = Path.GetDirectoryName(y);

					if (String.IsNullOrEmpty(xDir))
					{
						if (String.IsNullOrEmpty(yDir))
							return 0;
						else
							return 1;
					}
					else
					{
						if (String.IsNullOrEmpty(yDir))
							return -1;
						else
						{
							return xDir.CompareTo(yDir);
						}
					}
				}
			}
		}
	}
}
