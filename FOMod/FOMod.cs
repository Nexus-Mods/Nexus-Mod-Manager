using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Util;
using SevenZip;

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

		private static readonly List<string> StopFolders = new List<string>() { "fomod", "textures",
																	"meshes", "music", "shaders", "video",
																	"facegen", "menus", "lodsettings", "lsdata",
																	"sound" };

		#region Fields

		private string m_strFilePath = null;
		private Archive m_arcFile = null;
		private Archive m_arcCacheFile;
		private string m_strPrefixPath = null;
		private Dictionary<string, string> m_dicMovedArchiveFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private string m_strReadmePath = null;
		private string m_strScreenshotPath = null;
		private string m_strInstallScriptPath = null;
		private IScriptType m_stpInstallScriptType = null;

		private string m_strModId = null;
		private string m_strModName = null;
		private string m_strHumanReadableVersion = null;
		private string m_strLastKnownVersion = null;
		private bool m_booIsEndorsed = false;
		private Version m_verMachineVersion = null;
		private string m_strAuthor = null;
		private string m_strDescription = null;
		private string m_strInstallDate = null;
		private Uri m_uriWebsite = null;
		private ExtendedImage m_ximScreenshot = null;
		private IScript m_scpInstallScript = null;

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
			private set
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
		/// Gets or sets the Endorsement state of the mod.
		/// </summary>
		/// <value>The Endorsement state of the mod.</value>
		public bool IsEndorsed
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
				return m_strFilePath;
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

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the FOMod from the specified file.
		/// </summary>
		/// <param name="p_strFilePath">The mod file from which to create the FOMod.</param>
		/// <param name="p_mftModFormat">The format of the mod.</param>
		/// <param name="p_mcmModCacheManager">The manager for the current game mode's mod cache.</param>
		/// <param name="p_stgScriptTypeRegistry">The registry of supported script types.</param>
		public FOMod(string p_strFilePath, FOModFormat p_mftModFormat, IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry)
		{
			Format = p_mftModFormat;
			ScriptTypeRegistry = p_stgScriptTypeRegistry;
			bool p_booUseCache = true;

			m_strFilePath = p_strFilePath;
			m_arcFile = new Archive(p_strFilePath);
			m_arcFile.ReadOnlyInitProgressUpdated += new CancelProgressEventHandler(ArchiveFile_ReadOnlyInitProgressUpdated);

			FindPathPrefix();
			if (p_booUseCache)
			{
				m_arcCacheFile = p_mcmModCacheManager.GetCacheFile(this);
				//check to make sure the cache isn't bad
				if ((m_arcCacheFile != null) && !m_arcCacheFile.ContainsFile(GetRealPath("fomod/info.xml")))
				{
					//bad cache - clear it
					m_arcCacheFile.Dispose();
					m_arcCacheFile = null;
				}
			}
			m_arcFile.FilesChanged += new EventHandler(Archive_FilesChanged);

			//check for script
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

			//check for readme
			string strBaseName = Path.GetFileNameWithoutExtension(p_strFilePath);
			for (int i = 0; i < Readme.ValidExtensions.Length; i++)
				if (ContainsFile("readme - " + strBaseName + Readme.ValidExtensions[i]))
				{
					m_strReadmePath = "Readme - " + strBaseName + Readme.ValidExtensions[i];
					break;
				}
			if (String.IsNullOrEmpty(m_strReadmePath))
				for (int i = 0; i < Readme.ValidExtensions.Length; i++)
					if (ContainsFile("docs/readme - " + strBaseName + Readme.ValidExtensions[i]))
					{
						m_strReadmePath = "docs/Readme - " + strBaseName + Readme.ValidExtensions[i];
						break;
					}

			//check for screenshot
			string[] strScreenshots = m_arcFile.GetFiles("fomod", "screenshot*", false);
			//TODO make sure the file is a valid image
			if (strScreenshots.Length > 0)
				m_strScreenshotPath = strScreenshots[0];

			if (p_booUseCache && (m_arcCacheFile == null) && (m_arcFile.IsSolid || m_arcFile.ReadOnly))
			{
				string strTmpInfo = p_mcmModCacheManager.FileUtility.CreateTempDirectory();
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

					m_arcCacheFile = p_mcmModCacheManager.CreateCacheFile(this, strTmpInfo);
				}
				finally
				{
					FileUtil.ForceDelete(strTmpInfo);
				}
			}

			ModName = Path.GetFileNameWithoutExtension(Filename);
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
					stkPaths.Push(strDirectory);
					if (StopFolders.Contains(Path.GetFileName(strDirectory).ToLowerInvariant()))
					{
						booFoundPrefix = true;
						break;
					}
					booFoundData |= Path.GetFileName(strDirectory).Equals("data", StringComparison.OrdinalIgnoreCase);
				}
				if (booFoundPrefix)
				{
					strPrefixPath = strSourcePath;
					break;
				}
				if (booFoundData)
				{
					strPrefixPath = Path.Combine(strSourcePath, "Data");
					break;
				}
				if (!booFoundData && (m_arcFile.GetFiles(strSourcePath, "*.esp", false).Length > 0 ||
										m_arcFile.GetFiles(strSourcePath, "*.esm", false).Length > 0 ||
										m_arcFile.GetFiles(strSourcePath, "*.bsa", false).Length > 0))
				{
					strPrefixPath = strSourcePath;
					break;
				}
			}
			strPrefixPath = (strPrefixPath == null) ? "" : strPrefixPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (!String.IsNullOrEmpty(strPrefixPath))
			{
				strPrefixPath = strPrefixPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				strPrefixPath = strPrefixPath.Trim(Path.DirectorySeparatorChar);
				strPrefixPath += Path.DirectorySeparatorChar;
				m_dicMovedArchiveFiles.Clear();
				string[] strFiles = m_arcFile.GetFiles("/", true);
				Int32 intTrimLength = strPrefixPath.Length;
				for (Int32 i = strFiles.Length - 1; i >= 0; i--)
				{
					strFiles[i] = strFiles[i].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
					string strFile = strFiles[i];
					string strNewFileName = null;
					if (!strFile.StartsWith(strPrefixPath, StringComparison.OrdinalIgnoreCase))
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
			}
			m_strPrefixPath = strPrefixPath;
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
			string strPath = p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPath = strPath.Trim(Path.DirectorySeparatorChar);
			if (m_dicMovedArchiveFiles.ContainsKey(strPath))
				return true;
			if (m_arcFile.ContainsFile(GetRealPath(strPath)))
				return true;
			return ((m_arcCacheFile != null) && m_arcCacheFile.ContainsFile(GetRealPath(strPath)));
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
			return Path.Combine(m_strPrefixPath, p_strPath);
		}

		/// <summary>
		/// Deletes the specified file.
		/// </summary>
		/// <param name="p_strPath">The path of the file to delete.</param>
		protected void DeleteFile(string p_strPath)
		{
			if (!m_arcFile.ReadOnly && !m_arcFile.IsSolid)
				m_arcFile.DeleteFile(GetRealPath(p_strPath));
			if ((m_arcCacheFile != null) && m_arcCacheFile.ContainsFile(GetRealPath(p_strPath)))
				m_arcCacheFile.DeleteFile(GetRealPath(p_strPath));
		}

		/// <summary>
		/// Replaces the specified file with the given data.
		/// </summary>
		/// <param name="p_strPath">The path of the file to replace.</param>
		/// <param name="p_bteData">The new file data.</param>
		protected void ReplaceFile(string p_strPath, byte[] p_bteData)
		{
			if (!m_arcFile.ReadOnly && !m_arcFile.IsSolid)
				m_arcFile.ReplaceFile(GetRealPath(p_strPath), p_bteData);
			if ((m_arcCacheFile != null) && (m_arcCacheFile.ContainsFile(GetRealPath(p_strPath)) || m_arcFile.ReadOnly))
				m_arcCacheFile.ReplaceFile(GetRealPath(p_strPath), p_bteData);
		}

		/// <summary>
		/// Replaces the specified file with the given text.
		/// </summary>
		/// <param name="p_strPath">The path of the file to replace.</param>
		/// <param name="p_strData">The new file text.</param>
		protected void ReplaceFile(string p_strPath, string p_strData)
		{
			if (!m_arcFile.ReadOnly && !m_arcFile.IsSolid)
				m_arcFile.ReplaceFile(GetRealPath(p_strPath), p_strData);
			if ((m_arcCacheFile != null) && (m_arcCacheFile.ContainsFile(GetRealPath(p_strPath)) || m_arcFile.ReadOnly))
				m_arcCacheFile.ReplaceFile(GetRealPath(p_strPath), p_strData);
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
				throw new FileNotFoundException("File doesn't exist in FOMod", p_strFile);
			if ((m_arcCacheFile != null) && m_arcCacheFile.ContainsFile(GetRealPath(p_strFile)))
				return m_arcCacheFile.GetFileContents(GetRealPath(p_strFile));
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
			string strThisVersion = rgxClean.Replace(m_strHumanReadableVersion, "");
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
		public void UpdateInfo(IModInfo p_mifInfo, bool p_booOverwriteAllValues)
		{
			bool booChangedValue = false;
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(Id))
			{
				Id = p_mifInfo.Id;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues || String.IsNullOrEmpty(ModName) || ModName.Equals(Path.GetFileNameWithoutExtension(Filename))) && !String.IsNullOrEmpty(p_mifInfo.ModName))
			{
				ModName = p_mifInfo.ModName;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(HumanReadableVersion))
			{
				HumanReadableVersion = p_mifInfo.HumanReadableVersion;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(LastKnownVersion))
			{
				LastKnownVersion = p_mifInfo.LastKnownVersion;
				booChangedValue = true;
			}
			if ((p_booOverwriteAllValues) || (IsEndorsed != p_mifInfo.IsEndorsed))
			{
				IsEndorsed = p_mifInfo.IsEndorsed;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || (MachineVersion == null))
			{
				MachineVersion = p_mifInfo.MachineVersion;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(Author))
			{
				Author = p_mifInfo.Author;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(Description))
			{
				Description = p_mifInfo.Description;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || String.IsNullOrEmpty(InstallDate))
			{
				InstallDate = p_mifInfo.InstallDate;
				booChangedValue = true;
			}
			if (p_booOverwriteAllValues || (Website == null))
			{
				Website = p_mifInfo.Website;
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

			if (p_booOverwriteAllValues || (Screenshot == null))
			{
				if (p_mifInfo.Screenshot == null)
				{
					if (Screenshot != null)
					{
						DeleteFile(m_strScreenshotPath);
						Screenshot = p_mifInfo.Screenshot;
					}
				}
				else
				{
					Screenshot = p_mifInfo.Screenshot;
					ReplaceFile(m_strScreenshotPath, Screenshot.Data);
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
			xndInfo.AppendChild(p_xmlDocument.CreateElement("LastKnownVersion")).InnerText = LastKnownVersion;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Id")).InnerText = Id;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Author")).InnerText = Author;
			xndInfo.AppendChild(p_xmlDocument.CreateElement("IsEndorsed")).InnerText = IsEndorsed.ToString();
			xndInfo.AppendChild(p_xmlDocument.CreateElement("Description")).InnerText = Description;
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
					MachineVersion = new Version(xatMachineVersion.Value);
			}

			XmlNode xndLastKnownVersion = xndRoot.SelectSingleNode("LastKnownVersion");
			if ((xndLastKnownVersion != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(LastKnownVersion)))
				LastKnownVersion = xndLastKnownVersion.InnerText;

			XmlNode xndId = xndRoot.SelectSingleNode("Id");
			if ((xndId != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Id)))
				Id = xndId.InnerText;

			XmlNode xndAuthor = xndRoot.SelectSingleNode("Author");
			if ((xndAuthor != null) && (!p_booFillOnlyEmptyValues || String.IsNullOrEmpty(Author)))
				Author = xndAuthor.InnerText;

			XmlNode xndEndorsed = xndRoot.SelectSingleNode("IsEndorsed");
			if (xndEndorsed != null)
			{
				try
				{
					IsEndorsed = Convert.ToBoolean(xndEndorsed.InnerText);
				}
				catch
				{
					IsEndorsed = false;
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
	}
}
