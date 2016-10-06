using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexus.Client.ModManagement.Scripting;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using SevenZip;
using SevenZip.Sdk.Compression.Lzma;

namespace Nexus.Client.Mods.Formats.OMod
{
	/// <summary>
	/// Encapsulates a OMod mod archive.
	/// </summary>
	public partial class OMod : ObservableObject, IMod
	{
		private const float FILE_BLOCK_EXTRACTION_PROGRESS_BLOCK_SIZE = 0.8f;
		private const float FILE_WRITE_PROGRESS_BLOCK_SIZE = 0.15f;
		private const string CONVERSION_FOLDER = "omod conversion data";

		#region Events

		/// <summary>
		/// Raised to update listeners on the progress of the read-only initialization.
		/// </summary>
		public event CancelProgressEventHandler ReadOnlyInitProgressUpdated = delegate { };

		#endregion

		#region Fields

		private string m_strFilePath = null;
		private Archive m_arcFile = null;
		private string m_strCachePath = null;
		private string m_strPrefixPath = null;
		private Dictionary<string, string> m_dicMovedArchiveFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private string m_strReadOnlyTempDirectory = null;

		private bool m_booHasReadme = false;
		private bool m_booHasScreenshot = false;
		private bool m_booHasInstallScript = false;
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
		private Uri m_uriWebsite = null;
        private Int32 m_intPlaceInModLoadOrder = -1;
        private Int32 m_intNewPlaceInModLoadOrder = -1;
		private ExtendedImage m_ximScreenshot = null;
		private bool m_booUpdateWarningEnabled = true;
		private IScript m_scpInstallScript = null;

		private Int32 m_intReadOnlyInitFileBlockExtractionStages = 0;
		private Int32 m_intReadOnlyInitFileBlockExtractionCurrentStage = 0;
		private float m_fltReadOnlyInitCurrentBaseProgress = 0f;

		private bool m_booAllowArchiveEdits = false;

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
                return m_intNewPlaceInModLoadOrder;
            }
            set
            {
                SetPropertyIfChanged(ref m_intNewPlaceInModLoadOrder, value, () => NewPlaceInModLoadOrder);
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
				if ((m_ximScreenshot == null) && m_booHasScreenshot)
					m_ximScreenshot = new ExtendedImage(GetSpecialFile(ScreenshotPath));
				return m_ximScreenshot;
			}
			private set
			{
				if (CheckIfChanged(m_ximScreenshot, value))
				{
					m_ximScreenshot = value;
					m_booHasScreenshot = (value != null);
					OnPropertyChanged(() => Screenshot);
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
				return m_booHasInstallScript;
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
				if ((m_scpInstallScript == null) && m_booHasInstallScript)
					m_scpInstallScript = m_stpInstallScriptType.LoadScript(TextUtil.ByteToString(GetSpecialFile("script")));
				return m_scpInstallScript;
			}
			set
			{
				m_scpInstallScript = value;
				if (m_scpInstallScript == null)
				{
					m_stpInstallScriptType = null;
					m_booHasInstallScript = false;
				}
				else
				{
					m_stpInstallScriptType = m_scpInstallScript.Type;
					m_booHasInstallScript = true;
				}
			}
		}

		#endregion

		/// <summary>
		/// Gets the OMod format version of the mod.
		/// </summary>
		/// <value>The OMod format version of the mod.</value>
		public byte OModVersion { get; private set; }

		/// <summary>
		/// Gets the OMod format version of the mod.
		/// </summary>
		/// <value>The OMod format version of the mod.</value>
		public byte OModBaseVersion { get; private set; }

		/// <summary>
		/// Gets the contact email of the mod.
		/// </summary>
		/// <value>The contact email of the mod.</value>
		public string Email { get; private set; }

		/// <summary>
		/// Gets the time the mod was created.
		/// </summary>
		/// <value>The time the mod was created.</value>
		public DateTime CreationTime { get; private set; }

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
				return m_booHasScreenshot ? (IsPacked ? "image" : "screenshot") : null;
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

		/// <summary>
		/// Gets the format of the mod.
		/// </summary>
		/// <value>The format of the mod.</value>
		public IModFormat Format { get; private set; }

		/// <summary>
		/// Gets whether the mod is a packed OMod, or an OMod-ready archive.
		/// </summary>
		/// <value>Whether the mod is a packed OMod, or an OMod-ready archive.</value>
		public bool IsPacked { get; private set; }

		/// <summary>
		/// Gets or sets the type of compression used by the mod.
		/// </summary>
		/// <value>The type of compression used by the mod.</value>
		protected InArchiveFormat CompressionType { get; set; }

		/// <summary>
		/// Gets the list of plugins in the mod.
		/// </summary>
		/// <value>The list of plugins in the mod.</value>
		protected List<FileInfo> PluginList { get; private set; }

		/// <summary>
		/// Gets the list of data files in the mod.
		/// </summary>
		/// <value>The list of data files in the mod.</value>
		protected List<FileInfo> DataFileList { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the OMod from the specified file.
		/// </summary>
		/// <param name="p_strFilePath">The mod file from which to create the OMod.</param>
		/// <param name="p_mftModFormat">The format of the mod.</param>
		/// <param name="p_mcmModCacheManager">The manager for the current game mode's mod cache.</param>
		/// <param name="p_stgScriptTypeRegistry">The registry of supported script types.</param>
		public OMod(string p_strFilePath, OModFormat p_mftModFormat, IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry)
		{
			Format = p_mftModFormat;
			m_strFilePath = p_strFilePath;
			m_arcFile = new Archive(p_strFilePath);
			ModName = Path.GetFileNameWithoutExtension(Filename);
			bool p_booUseCache = true;

			PluginList = new List<FileInfo>();
			DataFileList = new List<FileInfo>();

			FindPathPrefix();
			IsPacked = !m_arcFile.ContainsFile(GetRealPath(Path.Combine(CONVERSION_FOLDER, "config")));

			string strCachePath = Path.Combine(p_mcmModCacheManager.ModCacheDirectory, Path.GetFileNameWithoutExtension(this.ModArchivePath));
			m_strCachePath = strCachePath;

			if (!IsPacked)
				InitializeUnpackedOmod(p_booUseCache, p_mcmModCacheManager, p_stgScriptTypeRegistry);
			else
				InitializePackedOmod(p_stgScriptTypeRegistry);
			m_arcFile.FilesChanged += new EventHandler(Archive_FilesChanged);
		}

		#endregion

		#region Initialization

		/// <summary>
		/// Initializes an OMod in OMod-ready archive format.
		/// </summary>
		/// <param name="p_booUseCache">Whether to use the mod cache.</param>
		/// <param name="p_mcmModCacheManager">The manager for the current game mode's mod cache.</param>
		/// <param name="p_stgScriptTypeRegistry">The registry of supported script types.</param>
		private void InitializeUnpackedOmod(bool p_booUseCache, IModCacheManager p_mcmModCacheManager, IScriptTypeRegistry p_stgScriptTypeRegistry)
		{
			p_mcmModCacheManager.MigrateCacheFile(this);		

			//check for script
			m_booHasInstallScript = false;
			foreach (IScriptType stpScript in p_stgScriptTypeRegistry.Types)
			{
				foreach (string strScriptName in stpScript.FileNames)
				{
					if (ContainsFile(Path.Combine(CONVERSION_FOLDER, strScriptName)))
					{
						StreamReader sreScript = null;
						string strCode = String.Empty;

						if (File.Exists(Path.Combine(CONVERSION_FOLDER, strScriptName)))
						{
							sreScript = new StreamReader(Path.Combine(CONVERSION_FOLDER, strScriptName));
							strCode = sreScript.ReadToEnd();
							sreScript.Close();
						}
						else
							strCode = TextUtil.ByteToString(GetFile(Path.Combine(CONVERSION_FOLDER, strScriptName))).Trim('\0');

						if (!String.IsNullOrEmpty(strCode))
						{
							if (stpScript.ValidateScript(stpScript.LoadScript(strCode)))
							{
								m_booHasInstallScript = true;
								m_stpInstallScriptType = stpScript;
								break;
							}
						}
					}
				}
				if (m_booHasInstallScript)
					break;
			}

			//check for readme
			m_booHasReadme = ContainsFile(Path.Combine(CONVERSION_FOLDER, "readme"));

			//check for screenshot
			m_booHasScreenshot = ContainsFile(Path.Combine(CONVERSION_FOLDER, "screenshot"));

			if (p_booUseCache && (!Directory.Exists(m_strCachePath)))
			{
				string strTmpInfo = p_mcmModCacheManager.FileUtility.CreateTempDirectory();
				try
				{
					FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(Path.Combine(CONVERSION_FOLDER, "config"))), GetSpecialFile("config"));

					if (m_booHasReadme)
						FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(Path.Combine(CONVERSION_FOLDER, "readme"))), GetSpecialFile("readme"));

					if (m_booHasScreenshot)
						FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(Path.Combine(CONVERSION_FOLDER, ScreenshotPath))), GetSpecialFile(ScreenshotPath));

					p_mcmModCacheManager.CreateCacheFile(this, strTmpInfo);
				}
				finally
				{
					FileUtil.ForceDelete(strTmpInfo);
				}
			}

			LoadInfo(GetSpecialFile("config"));
		}

		/// <summary>
		/// Initializes an OMod in packed format.
		/// </summary>
		/// <param name="p_stgScriptTypeRegistry">The registry of supported script types.</param>
		private void InitializePackedOmod(IScriptTypeRegistry p_stgScriptTypeRegistry)
		{
			using (SevenZipExtractor szeOmod = new SevenZipExtractor(m_strFilePath))
			{
				ExtractConfig(szeOmod);
				ExtractPluginList(szeOmod);
				ExtractDataFileList(szeOmod);

				if (szeOmod.ArchiveFileNames.Contains("plugins.crc"))
					m_intReadOnlyInitFileBlockExtractionStages++;
				if (szeOmod.ArchiveFileNames.Contains("data.crc"))
					m_intReadOnlyInitFileBlockExtractionStages++;

				//check for script
				m_booHasInstallScript = false;
				foreach (IScriptType stpScript in p_stgScriptTypeRegistry.Types)
				{
					foreach (string strScriptName in stpScript.FileNames)
					{
						if (szeOmod.ArchiveFileNames.Contains(strScriptName))
						{
							using (MemoryStream stmScript = new MemoryStream())
							{
								szeOmod.ExtractFile(strScriptName, stmScript);
								string strCode = System.Text.Encoding.Default.GetString(stmScript.ToArray());
								if (stpScript.ValidateScript(stpScript.LoadScript(strCode)))
								{
									m_booHasInstallScript = true;
									m_stpInstallScriptType = stpScript;
									break;
								}
							}
						}
					}
					if (m_booHasInstallScript)
						break;
				}

				//check for readme
				m_booHasReadme = szeOmod.ArchiveFileNames.Contains("readme");

				//check for screenshot
				m_booHasScreenshot = szeOmod.ArchiveFileNames.Contains("image");
			}
		}

		/// <summary>
		/// Loads the mod metadata from the config file.
		/// </summary>
		/// <param name="p_szeOmod">The extractor from which to read the config file.</param>
		protected void ExtractConfig(SevenZipExtractor p_szeOmod)
		{
			using (MemoryStream stmConfig = new MemoryStream())
			{
				p_szeOmod.ExtractFile("config", stmConfig);
				stmConfig.Position = 0;
				LoadInfo(stmConfig.GetBuffer());
			}
		}

		/// <summary>
		/// Loads the list of plugins in the mod.
		/// </summary>
		/// <param name="p_szeOmod">The extractor from which to read the plugin list.</param>
		protected void ExtractPluginList(SevenZipExtractor p_szeOmod)
		{
			if (!p_szeOmod.ArchiveFileNames.Contains("plugins.crc"))
				return;
			using (Stream stmPluginList = new MemoryStream())
			{
				p_szeOmod.ExtractFile("plugins.crc", stmPluginList);
				stmPluginList.Position = 0;
				using (BinaryReader brdPluginList = new BinaryReader(stmPluginList))
				{
					while (brdPluginList.PeekChar() != -1)
						PluginList.Add(new FileInfo(brdPluginList.ReadString(), brdPluginList.ReadUInt32(), brdPluginList.ReadInt64()));
				}
			}
		}

		/// <summary>
		/// Loads the list of data files in the mod.
		/// </summary>
		/// <param name="p_szeOmod">The extractor from which to read the file list.</param>
		protected void ExtractDataFileList(SevenZipExtractor p_szeOmod)
		{
			if (!p_szeOmod.ArchiveFileNames.Contains("data.crc"))
				return;
			using (Stream stmDataFileList = new MemoryStream())
			{
				p_szeOmod.ExtractFile("data.crc", stmDataFileList);
				stmDataFileList.Position = 0;
				using (BinaryReader brdDataFileList = new BinaryReader(stmDataFileList))
				{
					while (brdDataFileList.PeekChar() != -1)
						DataFileList.Add(new FileInfo(brdDataFileList.ReadString(), brdDataFileList.ReadUInt32(), brdDataFileList.ReadInt64()));
				}
			}
		}

		#endregion

		#region Read Transactions

		/// <summary>
		/// Starts a read-only transaction.
		/// </summary>
		/// <remarks>
		/// This puts the OMod into read-only mode.
		/// 
		/// Read-only mode can greatly increase the speed at which multiple file are extracted.
		/// </remarks>
		public void BeginReadOnlyTransaction(FileUtil p_futFileUtil)
		{
			if (!IsPacked)
			{
				m_arcFile.ReadOnlyInitProgressUpdated += new CancelProgressEventHandler(ArchiveFile_ReadOnlyInitProgressUpdated);
				m_arcFile.BeginReadOnlyTransaction(p_futFileUtil);
				return;
			}

			m_strReadOnlyTempDirectory = p_futFileUtil.CreateTempDirectory();

			string[] strFileStreamNames = { "plugins", "data" };
			List<FileInfo> lstFiles = null;
			byte[] bteUncompressedFileData = null;
			m_intReadOnlyInitFileBlockExtractionCurrentStage = 0;
			m_fltReadOnlyInitCurrentBaseProgress = 0;
			foreach (string strFileStreamName in strFileStreamNames)
			{
				//extract the compressed file block...
				using (Stream stmCompressedFiles = new MemoryStream())
				{
					using (SevenZipExtractor szeOmod = new SevenZipExtractor(m_strFilePath))
					{
						if (!szeOmod.ArchiveFileNames.Contains(strFileStreamName))
							continue;
						m_intReadOnlyInitFileBlockExtractionCurrentStage++;
						szeOmod.ExtractFile(strFileStreamName, stmCompressedFiles);
						switch (strFileStreamName)
						{
							case "plugins":
								lstFiles = PluginList;
								break;
							case "data":
								lstFiles = DataFileList;
								break;
							default:
								throw new Exception("Unexpected value for file stream name: " + strFileStreamName);
						}
					}

					stmCompressedFiles.Position = 0;
					Int64 intTotalLength = lstFiles.Sum(x => x.Length);
					bteUncompressedFileData = new byte[intTotalLength];
					switch (CompressionType)
					{
						case InArchiveFormat.SevenZip:
							byte[] bteProperties = new byte[5];
							stmCompressedFiles.Read(bteProperties, 0, 5);
							Decoder dcrDecoder = new Decoder();
							dcrDecoder.SetDecoderProperties(bteProperties);
							DecoderProgressWatcher dpwWatcher = new DecoderProgressWatcher(stmCompressedFiles.Length);
							dpwWatcher.ProgressUpdated += new EventHandler<EventArgs<int>>(dpwWatcher_ProgressUpdated);
							using (Stream stmUncompressedFiles = new MemoryStream(bteUncompressedFileData))
								dcrDecoder.Code(stmCompressedFiles, stmUncompressedFiles, stmCompressedFiles.Length - stmCompressedFiles.Position, intTotalLength, dpwWatcher);
							break;
						case InArchiveFormat.Zip:
							using (SevenZipExtractor szeZip = new SevenZipExtractor(stmCompressedFiles))
							{
								szeZip.Extracting += new EventHandler<ProgressEventArgs>(szeZip_Extracting);
								using (Stream stmFile = new MemoryStream(bteUncompressedFileData))
								{
									szeZip.ExtractFile(0, stmFile);
								}
							}
							break;
						default:
							throw new Exception("Cannot get files: unsupported compression type: " + CompressionType.ToString());
					}
				}

				float fltFileStreamPercentBlockSize = FILE_BLOCK_EXTRACTION_PROGRESS_BLOCK_SIZE / m_intReadOnlyInitFileBlockExtractionStages;
				float fltFileWritingPercentBlockSize = FILE_WRITE_PROGRESS_BLOCK_SIZE / m_intReadOnlyInitFileBlockExtractionStages;
				m_fltReadOnlyInitCurrentBaseProgress += fltFileStreamPercentBlockSize;

				//...then write each file to the temporary location
				Int64 intFileStart = 0;
				byte[] bteFile = null;
				Crc32 crcChecksum = new Crc32();
				for (Int32 i = 0; i < lstFiles.Count; i++)
				{
					FileInfo ofiFile = lstFiles[i];
					bteFile = new byte[ofiFile.Length];
					Array.Copy(bteUncompressedFileData, intFileStart, bteFile, 0, ofiFile.Length);
					intFileStart += ofiFile.Length;
					FileUtil.WriteAllBytes(Path.Combine(m_strReadOnlyTempDirectory, ofiFile.Name), bteFile);
					crcChecksum.Initialize();
					crcChecksum.ComputeHash(bteFile);
					if (crcChecksum.CrcValue != ofiFile.CRC)
						throw new Exception(String.Format("Unable to extract {0}: checksums did not match. OMod is corrupt.", ofiFile.Name));
					UpdateReadOnlyInitProgress(m_fltReadOnlyInitCurrentBaseProgress, fltFileWritingPercentBlockSize, i / lstFiles.Count);
				}
				m_fltReadOnlyInitCurrentBaseProgress += fltFileWritingPercentBlockSize;
			}
			m_fltReadOnlyInitCurrentBaseProgress = FILE_BLOCK_EXTRACTION_PROGRESS_BLOCK_SIZE + FILE_WRITE_PROGRESS_BLOCK_SIZE;

			Int32 intRemainingSteps = (m_booHasInstallScript ? 1 : 0) + (m_booHasReadme ? 1 : 0) + (m_booHasScreenshot ? 1 : 0);
			Int32 intStepCounter = 1;
			if (m_booHasScreenshot)
			{
				File.WriteAllBytes(Path.Combine(m_strReadOnlyTempDirectory, ScreenshotPath), GetSpecialFile(ScreenshotPath));
				UpdateReadOnlyInitProgress(m_fltReadOnlyInitCurrentBaseProgress, 1f - m_fltReadOnlyInitCurrentBaseProgress, 1f / intRemainingSteps * intStepCounter++);
			}
			if (m_booHasInstallScript)
			{
				File.WriteAllBytes(Path.Combine(m_strReadOnlyTempDirectory, "script"), GetSpecialFile("script"));
				UpdateReadOnlyInitProgress(m_fltReadOnlyInitCurrentBaseProgress, 1f - m_fltReadOnlyInitCurrentBaseProgress, 1f / intRemainingSteps * intStepCounter++);
			}
			if (m_booHasReadme)
			{
				File.WriteAllBytes(Path.Combine(m_strReadOnlyTempDirectory, "readme"), GetSpecialFile("readme"));
				UpdateReadOnlyInitProgress(m_fltReadOnlyInitCurrentBaseProgress, 1f - m_fltReadOnlyInitCurrentBaseProgress, 1f / intRemainingSteps * intStepCounter);
			}
		}

		/// <summary>
		/// Handles the <see cref="DecoderProgressWatcher.ProgressUpdated"/> event of the watcher that is
		/// monitoring the extraction of compressed files blocks.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="EventArgs{Int32}"/> describing the event arguments.</param>
		private void dpwWatcher_ProgressUpdated(object sender, EventArgs<Int32> e)
		{
			UpdateFileStreamExtractionProgress(e.Argument / 100);
		}

		/// <summary>
		/// Handles the <see cref="SevenZipExtractor.Extracting"/> event of the extractor that is
		/// extracting the compressed files blocks.
		/// </summary>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">An <see cref="ProgressEventArgs"/> describing the event arguments.</param>
		private void szeZip_Extracting(object sender, ProgressEventArgs e)
		{
			UpdateFileStreamExtractionProgress(e.PercentDone / 100);
		}

		/// <summary>
		/// Updates the progress of the read only initialization while extracting file blocks.
		/// </summary>
		/// <param name="p_fltPercentDone">The percentage of the file block that has been extracted.</param>
		private void UpdateFileStreamExtractionProgress(float p_fltPercentDone)
		{
			float fltFileStreamPercentBlockSize = FILE_BLOCK_EXTRACTION_PROGRESS_BLOCK_SIZE / m_intReadOnlyInitFileBlockExtractionStages;
			UpdateReadOnlyInitProgress(m_fltReadOnlyInitCurrentBaseProgress, fltFileStreamPercentBlockSize, p_fltPercentDone);

		}

		/// <summary>
		/// Updates the progress of the read only initialization.
		/// </summary>
		/// <param name="p_fltBasePercent">The base percentage of work that has been done.</param>
		/// <param name="p_fltPercentBlockSize">The size of the current block of work being done.</param>
		/// <param name="p_fltPercentDone">The percentage of the current block of work that has been completed.</param>
		private void UpdateReadOnlyInitProgress(float p_fltBasePercent, float p_fltPercentBlockSize, float p_fltPercentDone)
		{
			float fltScaledPercentDone = p_fltBasePercent + p_fltPercentBlockSize * p_fltPercentDone;
			ReadOnlyInitProgressUpdated(this, new CancelProgressEventArgs(fltScaledPercentDone));
		}

		/// <summary>
		/// Ends a read-only transaction.
		/// </summary>
		/// <remarks>
		/// This takes the OMod out of read-only mode.
		/// 
		/// Read-only mode can greatly increase the speed at which multiple file are extracted.
		/// </remarks>
		public void EndReadOnlyTransaction()
		{
			if (!IsPacked)
			{
				m_arcFile.EndReadOnlyTransaction();
				m_arcFile.ReadOnlyInitProgressUpdated -= new CancelProgressEventHandler(ArchiveFile_ReadOnlyInitProgressUpdated);
			}
			if (!String.IsNullOrEmpty(m_strReadOnlyTempDirectory))
				FileUtil.ForceDelete(m_strReadOnlyTempDirectory);
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
				bool booFoundPrefix = false;
				foreach (string strDirectory in directories)
				{
					stkPaths.Push(strDirectory);
					if (CONVERSION_FOLDER.Equals(Path.GetFileName(strDirectory), StringComparison.OrdinalIgnoreCase))
					{
						booFoundPrefix = true;
						break;
					}
				}
				if (booFoundPrefix)
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
		/// Determines if the OMod contains the given file.
		/// </summary>
		/// <param name="p_strPath">The filename whose existence in the OMod is to be determined.</param>
		/// <returns><c>true</c> if the specified file is in the OMod; <c>false</c> otherwise.</returns>
		public bool ContainsFile(string p_strPath)
		{
			if (String.IsNullOrEmpty(p_strPath))
				return false;

			string strPath = p_strPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPath = strPath.Trim(Path.DirectorySeparatorChar);

			if (!IsPacked)
			{
				if (m_dicMovedArchiveFiles.ContainsKey(strPath))
					return true;
				if (m_arcFile.ContainsFile(GetRealPath(strPath)))
					return true;
				return ((Directory.Exists(m_strCachePath)) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(strPath)))));
			}
			if (PluginList.Contains(x => x.Name.Equals(strPath, StringComparison.OrdinalIgnoreCase)))
				return true;
			return DataFileList.Contains(x => x.Name.Equals(strPath, StringComparison.OrdinalIgnoreCase));
		}

		/// <summary>
		/// Deletes the specified file.
		/// </summary>
		/// <param name="p_strPath">The path of the file to delete.</param>
		protected void DeleteSpecialFile(string p_strPath)
		{
			string strPath = GetRealPath(IsPacked ? p_strPath : Path.Combine(CONVERSION_FOLDER, p_strPath));
			//if this is a packed OMod, and the file is read-only, we want it to crash
			// if it's no patcked, then we simply remove it from the cache, so we don't want
			// a crash.
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly || IsPacked))
				m_arcFile.DeleteFile(strPath);
			if ((Directory.Exists(m_strCachePath) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strPath))))))
				FileUtil.ForceDelete(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
		}

		/// <summary>
		/// Replaces the specified file with the given data.
		/// </summary>
		/// <param name="p_strPath">The path of the file to replace.</param>
		/// <param name="p_bteData">The new file data.</param>
		protected void ReplaceSpecialFile(string p_strPath, byte[] p_bteData)
		{
			string strPath = GetRealPath(IsPacked ? p_strPath : Path.Combine(CONVERSION_FOLDER, p_strPath));
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly || IsPacked))
				m_arcFile.ReplaceFile(p_strPath, p_bteData);

			System.IO.FileInfo fiFile = new System.IO.FileInfo(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));

			if ((Directory.Exists(m_strCachePath) && ((File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strPath)))) || (fiFile.IsReadOnly))))
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
		protected void ReplaceSpecialFile(string p_strPath, string p_strData)
		{
			string strPath = GetRealPath(IsPacked ? p_strPath : Path.Combine(CONVERSION_FOLDER, p_strPath));
			if (m_booAllowArchiveEdits && (!m_arcFile.ReadOnly || IsPacked))
				m_arcFile.ReplaceFile(p_strPath, p_strData);

			System.IO.FileInfo fiFile = new System.IO.FileInfo(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
						
			if ((Directory.Exists(m_strCachePath) && ((File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strPath)))) || (fiFile.IsReadOnly))))
			{
				FileUtil.ForceDelete(Path.Combine(m_strCachePath, GetRealPath(p_strPath)));
				File.WriteAllText(Path.Combine(m_strCachePath, GetRealPath(p_strPath)), p_strData);
			}
		}

		#endregion

		#region File Management

		/// <summary>
		/// Retrieves the specified file from the OMod.
		/// </summary>
		/// <param name="p_strFile">The file to retrieve.</param>
		/// <returns>The requested file data.</returns>
		/// <exception cref="FileNotFoundException">Thrown if the specified file
		/// is not in the OMod.</exception>
		protected byte[] GetSpecialFile(string p_strFile)
		{
			bool booFound = false;
			bool booIsBinary = false;
			switch (p_strFile)
			{
				case "config":
					booFound = true;
					booIsBinary = true;
					break;
				case "image":
				case "screenshot":
					booFound = m_booHasScreenshot;
					booIsBinary = true;
					break;
				case "readme":
					booFound = m_booHasReadme;
					break;
				case "script":
					if (!IsPacked)
						p_strFile = "script.txt";
					booFound = m_booHasInstallScript;
					break;
			}
			if (!booFound)
				throw new FileNotFoundException("Special File doesn't exist in OMod", p_strFile);

			byte[] bteFile = null;
			if (!IsPacked)
			{
				string strPath = Path.Combine(CONVERSION_FOLDER, p_strFile);

				if ((Directory.Exists(m_strCachePath) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(strPath))))))
					return (File.ReadAllBytes(Path.Combine(m_strCachePath, GetRealPath(strPath))));
												
				bteFile = m_arcFile.GetFileContents(GetRealPath(strPath));
				if (!booIsBinary)
					bteFile = System.Text.Encoding.Default.GetBytes(TextUtil.ByteToString(bteFile).Trim('\0'));
			}
			else
			{
				using (MemoryStream msmFile = new MemoryStream())
				{
					using (SevenZipExtractor szeOmod = new SevenZipExtractor(m_strFilePath))
						szeOmod.ExtractFile(p_strFile, msmFile);
					if (booIsBinary)
						bteFile = msmFile.GetBuffer();
					else
					{
						using (BinaryReader brdReader = new BinaryReader(msmFile))
						{
							msmFile.Position = 0;
							bteFile = System.Text.Encoding.Default.GetBytes(brdReader.ReadString().Trim('\0'));
							brdReader.Close();
						}
					}
					msmFile.Close();
				}
			}
			return bteFile;
		}

		/// <summary>
		/// Retrieves the specified file from the OMod.
		/// </summary>
		/// <param name="p_strFile">The file to retrieve.</param>
		/// <returns>The requested file data.</returns>
		/// <exception cref="FileNotFoundException">Thrown if the specified file
		/// is not in the OMod.</exception>
		public byte[] GetFile(string p_strFile)
		{
			if (!ContainsFile(p_strFile))
				throw new FileNotFoundException("File doesn't exist in OMod", p_strFile);

			if (!IsPacked)
			{
				if ((Directory.Exists(m_strCachePath) && (File.Exists(Path.Combine(m_strCachePath, GetRealPath(p_strFile))))))
					return (File.ReadAllBytes(Path.Combine(m_strCachePath, GetRealPath(p_strFile))));

				return m_arcFile.GetFileContents(GetRealPath(p_strFile));
			}

			if (!String.IsNullOrEmpty(m_strReadOnlyTempDirectory))
				return File.ReadAllBytes(Path.Combine(m_strReadOnlyTempDirectory, p_strFile));

			List<FileInfo> lstFiles = null;
			byte[] bteFileBlock = null;
			using (Stream stmDataFiles = new MemoryStream())
			{
				using (SevenZipExtractor szeOmod = new SevenZipExtractor(m_strFilePath))
				{
					if (Path.GetExtension(p_strFile).Equals(".esm", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(p_strFile).Equals(".esp", StringComparison.OrdinalIgnoreCase))
					{
						szeOmod.ExtractFile("plugins", stmDataFiles);
						lstFiles = PluginList;
					}
					else
					{
						szeOmod.ExtractFile("data", stmDataFiles);
						lstFiles = DataFileList;
					}
				}
				stmDataFiles.Position = 0;

				Int64 intTotalLength = lstFiles.Sum(x => x.Length);
				bteFileBlock = new byte[intTotalLength];
				switch (CompressionType)
				{
					case InArchiveFormat.SevenZip:
						byte[] bteProperties = new byte[5];
						stmDataFiles.Read(bteProperties, 0, 5);
						Decoder dcrDecoder = new Decoder();
						dcrDecoder.SetDecoderProperties(bteProperties);
						using (Stream stmFile = new MemoryStream(bteFileBlock))
							dcrDecoder.Code(stmDataFiles, stmFile, stmDataFiles.Length - stmDataFiles.Position, intTotalLength, null);
						break;
					case InArchiveFormat.Zip:
						using (SevenZipExtractor szeZip = new SevenZipExtractor(stmDataFiles))
						{
							using (Stream stmFile = new MemoryStream(bteFileBlock))
							{
								szeZip.ExtractFile(0, stmFile);
							}
						}
						break;
					default:
						throw new Exception("Cannot get file: unsupported compression type: " + CompressionType.ToString());
				}
			}
			Int64 intFileStart = 0;
			byte[] bteFile = null;
			foreach (FileInfo ofiFile in lstFiles)
			{
				if (!ofiFile.Name.Equals(p_strFile, StringComparison.OrdinalIgnoreCase))
					intFileStart += ofiFile.Length;
				else
				{
					bteFile = new byte[ofiFile.Length];
					Array.Copy(bteFileBlock, intFileStart, bteFile, 0, ofiFile.Length);
					break;
				}
			}
			return bteFile;
		}

		/// <summary>
		/// Retrieves the list of files in this OMod.
		/// </summary>
		/// <returns>The list of files in this OMod.</returns>
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
		/// Retrieves the list of all files in the specified OMod folder.
		/// </summary>
		/// <param name="p_strFolderPath">The OMod folder whose file list is to be retrieved.</param>
		/// <param name="p_booRecurse">Whether to return files that are in subdirectories of the given directory.</param>
		/// <returns>The list of all files in the specified OMod folder.</returns>
		public List<string> GetFileList(string p_strFolderPath, bool p_booRecurse)
		{
			List<string> lstFiles = new List<string>();
			if (!IsPacked)
			{
				foreach (string strFile in m_arcFile.GetFiles(p_strFolderPath, p_booRecurse))
					if (!m_dicMovedArchiveFiles.ContainsValue(strFile))
						if (!strFile.StartsWith(CONVERSION_FOLDER, StringComparison.OrdinalIgnoreCase))
							lstFiles.Add(strFile);
				string strPathPrefix = p_strFolderPath ?? "";
				strPathPrefix = strPathPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				strPathPrefix = strPathPrefix.Trim(Path.DirectorySeparatorChar);
				if (strPathPrefix.Length > 0)
					strPathPrefix += Path.DirectorySeparatorChar;
				foreach (string strFile in m_dicMovedArchiveFiles.Keys)
					if (strFile.StartsWith(strPathPrefix, StringComparison.OrdinalIgnoreCase) && !strFile.StartsWith(CONVERSION_FOLDER, StringComparison.OrdinalIgnoreCase))
						lstFiles.Add(strFile);
				return lstFiles;
			}

			string strFolderPath = (p_strFolderPath ?? "").Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			if (!strFolderPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
				strFolderPath += Path.DirectorySeparatorChar;
			foreach (FileInfo fifFile in PluginList.Union(DataFileList))
			{
				if ((p_booRecurse && fifFile.Name.StartsWith(strFolderPath, StringComparison.OrdinalIgnoreCase))
					|| (Path.GetDirectoryName(fifFile.Name) + Path.DirectorySeparatorChar).Equals(strFolderPath, StringComparison.OrdinalIgnoreCase))
					lstFiles.Add(fifFile.Name);
			}
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

			if (booChangedValue)
			{
				byte[] bteInfo = SaveInfo();
				ReplaceSpecialFile("config", bteInfo);
			}

			if ((p_booOverwriteAllValues == true) || (Screenshot == null))
			{
				if (p_mifInfo.Screenshot == null)
				{
					if (m_booHasScreenshot)
					{
						DeleteSpecialFile(ScreenshotPath);
						Screenshot = p_mifInfo.Screenshot;
					}
				}
				else
				{
					Screenshot = p_mifInfo.Screenshot;
					ReplaceSpecialFile(ScreenshotPath, Screenshot.Data);
				}
			}
		}

		/// <summary>
		/// Serializes this <see cref="IModInfo"/> to a data block, suitable to write to a file.
		/// </summary>
		/// <returns>The serialized representation of this <see cref="IModInfo"/>.</returns>
		protected byte[] SaveInfo()
		{
			using (MemoryStream stmConfig = new MemoryStream())
			{
				Int64 intLength = 0;
				using (BinaryWriter bwrConfig = new BinaryWriter(stmConfig))
				{
					bwrConfig.Write(OModBaseVersion);
					bwrConfig.Write(ModName);
					Int32 intBuildVersion = 0;
					if (MachineVersion != null)
					{
						bwrConfig.Write(MachineVersion.Major);
						bwrConfig.Write(MachineVersion.Minor);
						intBuildVersion = MachineVersion.Build;
					}
					else
					{
						bwrConfig.Write(0);
						bwrConfig.Write(0);
					}
					bwrConfig.Write(Author);
					bwrConfig.Write(Email);
					if (Website == null)
						bwrConfig.Write("");
					else
						bwrConfig.Write(Website.ToString());
					bwrConfig.Write(Description ?? "");
					if (OModBaseVersion >= 2)
						bwrConfig.Write(CreationTime.ToBinary());
					else
						bwrConfig.Write(CreationTime.ToString("dd\\/MM\\/yyyy HH:mm"));
					switch (CompressionType)
					{
						case InArchiveFormat.SevenZip:
							bwrConfig.Write((byte)0);
							break;
						case InArchiveFormat.Zip:
							bwrConfig.Write((byte)1);
							break;
						default:
							throw new Exception("Unsupported compression type for OMod: " + CompressionType);
					}
					if (OModBaseVersion >= 1)
						bwrConfig.Write(intBuildVersion);
					bwrConfig.Write(OModVersion >= 7 ? OModVersion : Convert.ToByte(7));
					bwrConfig.Write(String.IsNullOrEmpty(Id) ? "" : Id);
					bwrConfig.Write(String.IsNullOrEmpty(DownloadId) ? "" : DownloadId);
					bwrConfig.Write(String.IsNullOrEmpty(LastKnownVersion) ? "" : LastKnownVersion);
					bwrConfig.Write(IsEndorsed.ToString());
					bwrConfig.Write(CategoryId);
					bwrConfig.Write(CustomCategoryId);
					intLength = stmConfig.Length;
				}
				byte[] bteConfig = new byte[intLength];
				Array.Copy(stmConfig.GetBuffer(), bteConfig, intLength);
				return bteConfig;
			}
		}

		/// <summary>
		/// Deserializes an <see cref="IModInfo"/> from the given data.
		/// </summary>
		/// <param name="p_bteData">The data from which to deserialize the <see cref="IModInfo"/>.</param>
		protected void LoadInfo(byte[] p_bteData)
		{
			using (Stream stmConfig = new MemoryStream(p_bteData))
			{
				using (BinaryReader brdConfig = new BinaryReader(stmConfig))
				{
					OModBaseVersion = brdConfig.ReadByte();
					ModName = brdConfig.ReadString();
					Int32 intMajorVersion = brdConfig.ReadInt32();
					Int32 intMinorVersion = brdConfig.ReadInt32();
					if (intMinorVersion < 0)
						intMinorVersion = 0;
					Author = brdConfig.ReadString();
					Email = brdConfig.ReadString();
					string strUrl = brdConfig.ReadString();
					Uri uriUrl = null;
					if (UriUtil.TryBuildUri(strUrl, out uriUrl))
						Website = uriUrl;
					Description = brdConfig.ReadString();
					if (OModBaseVersion >= 2)
						CreationTime = DateTime.FromBinary(brdConfig.ReadInt64());
					else
					{
						DateTime dteCreation = new DateTime(2006, 1, 1);
						string sCreationTime = brdConfig.ReadString();
						if (DateTime.TryParseExact(sCreationTime, "d\\/M\\/yyyy HH:mm", null, DateTimeStyles.None, out dteCreation))
							CreationTime = dteCreation;
						else if (DateTime.TryParseExact(sCreationTime, "d\\/M\\/yyyy h:mm tt", null, DateTimeStyles.None, out dteCreation))
							CreationTime = dteCreation;
					}
					byte bteCompressionType = brdConfig.ReadByte();
					switch (bteCompressionType)
					{
						case 0:
							CompressionType = InArchiveFormat.SevenZip;
							break;
						case 1:
							CompressionType = InArchiveFormat.Zip;
							break;
						default:
							throw new Exception("Unrecognizes OMod compression type: " + bteCompressionType);
					}
					Int32 intBuildVersion = (OModBaseVersion >= 1) ? brdConfig.ReadInt32() : -1;
					if (intBuildVersion < 0)
						MachineVersion = new Version(intMajorVersion, intMinorVersion);
					else
						MachineVersion = new Version(intMajorVersion, intMinorVersion, intBuildVersion, 0);
					HumanReadableVersion = MachineVersion.ToString();
					if (brdConfig.BaseStream.Position < brdConfig.BaseStream.Length)
						OModVersion = brdConfig.ReadByte();
					if (OModVersion >= 5)
					{
						Id = brdConfig.ReadString();
						if (OModVersion >= 7)
							DownloadId = brdConfig.ReadString();
						LastKnownVersion = brdConfig.ReadString();
						try
						{
							IsEndorsed = Convert.ToBoolean(brdConfig.ReadString());
						}
						catch
						{
							IsEndorsed = false;
						}

						if (OModVersion >= 6)
						{
							CategoryId = brdConfig.ReadInt32();
							CustomCategoryId = brdConfig.ReadInt32();
						}
					}
					else
						OModVersion = OModBaseVersion;
				}
			}
		}

		/// <summary>
		/// Validates an OMod config file.
		/// </summary>
		/// <param name="p_bteData">The OMod config file.</param>
		/// <returns><c>true</c> if the given file is valid;
		/// <c>false</c> otherwise.</returns>
		protected bool ValidateConfig(byte[] p_bteData)
		{
			try
			{
				using (Stream stmConfig = new MemoryStream(p_bteData))
				{
					using (BinaryReader brdConfig = new BinaryReader(stmConfig))
					{
						byte bteOModBaseVersion = brdConfig.ReadByte();
						brdConfig.ReadString();
						brdConfig.ReadInt32();
						brdConfig.ReadInt32();
						brdConfig.ReadString();
						brdConfig.ReadString();
						brdConfig.ReadString();
						brdConfig.ReadString();
						if (bteOModBaseVersion >= 2)
							brdConfig.ReadInt64();
						byte bteCompressionType = brdConfig.ReadByte();
						switch (bteCompressionType)
						{
							case 0:
								CompressionType = InArchiveFormat.SevenZip;
								break;
							case 1:
								CompressionType = InArchiveFormat.Zip;
								break;
							default:
								return false;
						}
						if (bteOModBaseVersion >= 1)
							brdConfig.ReadInt32();
						byte bteOModVersion = brdConfig.ReadByte();
						if (bteOModVersion >= 5)
						{
							brdConfig.ReadString();
							if (bteOModVersion >= 7)
								brdConfig.ReadString();
							brdConfig.ReadString();
							brdConfig.ReadString();

							if (bteOModVersion >= 6)
							{
								brdConfig.ReadInt32();
								brdConfig.ReadInt32();
							}
						}
					}
				}
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}

		#endregion

		/// <summary>
		/// Uses the mod name to represent to mod.
		/// </summary>
		/// <returns>The mod name.</returns>
		public override string ToString()
		{
			return ModName;
		}
	}
}
