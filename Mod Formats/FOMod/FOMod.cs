namespace Nexus.Client.Mods.Formats.FOMod
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Xml;

    using Nexus.Client.ModManagement.Scripting;
    using Nexus.Client.Util;

    /// <summary>
    /// Encapsulates a FOMod mod archive.
    /// </summary>
    public class FOMod : ObservableObject, IMod
	{
		#region Events

		/// <inheritdoc />
		public event CancelProgressEventHandler ReadOnlyInitProgressUpdated = delegate { };

        #endregion

        private const bool AllowArchiveEdits = false;

        #region Fields

        private readonly string _nestedFilePath;
		private readonly Archive _archiveFile;
		private readonly string _cachePath;
		private readonly Dictionary<string, string> _movedArchiveFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly string _readmePath = null;
        private readonly bool _usesPlugins;
        private readonly IEnvironmentInfo _environmentInfo;

        private string _prefixPath;
        private string _installScriptPath;
		private IScriptType _installScriptType;
		private string _modId;
		private string _downloadId;
        private DateTime? _downloadDate;
		private string _modName;
		private string _fileName;
		private string _humanReadableVersion;
		private string _lastKnownVersion;
		private int _categoryId;
		private int _customCategoryId;
		private bool? _isEndorsed = false;
		private Version _machineVersion;
		private string _author;
		private string _description;
		private string _installDate;
        private int _placeInModLoadOrder = -1;
        private int _newPlaceInModLoadOrder = -1;
		private Uri _website;
		private ExtendedImage _screenshot;
		private bool _updateWarningEnabled = true;
		private bool _updateChecksEnabled = true;
		private IScript _installScript;
        private bool _movedArchiveInitialized;

        protected List<string> IgnoreFolders = new List<string> { "__MACOSX" };

        #endregion

        #region Properties

        #region IModInfo Members

        /// <inheritdoc />
        public string Id
		{
			get => _modId;
            set
			{
				SetPropertyIfChanged(ref _modId, value, () => Id);
			}
		}

        /// <inheritdoc />
        public string DownloadId
		{
			get => _downloadId;
            set
			{
				SetPropertyIfChanged(ref _downloadId, value, () => DownloadId);
			}
		}

        /// <inheritdoc />
        public DateTime? DownloadDate
        {
            get => _downloadDate;
            set
            {
                SetPropertyIfChanged(ref _downloadDate, value, () => DownloadDate);
            }
        }

        /// <inheritdoc />
        public string FileName
		{
			get => _fileName;
            private set
			{
				SetPropertyIfChanged(ref _fileName, value, () => FileName);
			}
		}

        /// <inheritdoc />
        public string ModName
		{
			get => _modName;
            private set
			{
				SetPropertyIfChanged(ref _modName, value, () => ModName);
			}
		}

        /// <inheritdoc />
        public string HumanReadableVersion
		{
			get => _humanReadableVersion;
            set
			{
				SetPropertyIfChanged(ref _humanReadableVersion, value, () => HumanReadableVersion);
			}
		}

        /// <inheritdoc />
        public string LastKnownVersion
		{
			get => _lastKnownVersion;
            private set
			{
				SetPropertyIfChanged(ref _lastKnownVersion, value, () => LastKnownVersion);
			}
		}

        /// <inheritdoc />
        public int CategoryId
		{
			get => _categoryId;
            private set
			{
				SetPropertyIfChanged(ref _categoryId, value, () => CategoryId);
			}
		}

        /// <inheritdoc />
        public int CustomCategoryId
		{
			get => _customCategoryId;
            private set
			{
				SetPropertyIfChanged(ref _customCategoryId, value, () => CustomCategoryId);
			}
		}

        /// <inheritdoc />
        public bool? IsEndorsed
		{
			get => _isEndorsed;
            set
			{
				SetPropertyIfChanged(ref _isEndorsed, value, () => IsEndorsed);
			}
		}

        /// <inheritdoc />
        public Version MachineVersion
		{
			get => _machineVersion;
            private set
			{
				SetPropertyIfChanged(ref _machineVersion, value, () => MachineVersion);
			}
		}

        /// <inheritdoc />
        public string Author
		{
			get => _author;
            private set
			{
				SetPropertyIfChanged(ref _author, value, () => Author);
			}
		}

        /// <inheritdoc />
        public string Description
		{
			get => _description;
            private set
			{
				SetPropertyIfChanged(ref _description, value, () => Description);
			}
		}

        /// <inheritdoc />
        public string InstallDate
		{
			get => _installDate;
            set
			{
				SetPropertyIfChanged(ref _installDate, value, () => InstallDate);
			}
		}

        /// <inheritdoc />
        public Uri Website
		{
			get => _website;
            private set
			{
				SetPropertyIfChanged(ref _website, value, () => Website);
			}
		}

        /// <inheritdoc />
        public ExtendedImage Screenshot
		{
			get
			{
				if (_screenshot == null && !string.IsNullOrEmpty(ScreenshotPath))
                {
                    _screenshot = new ExtendedImage(GetFile(ScreenshotPath));
                }

                return _screenshot;
			}
			private set
			{
				if (CheckIfChanged(_screenshot, value))
                {
                    ScreenshotPath = value == null ? null : "fomod/screenshot" + value.GetExtension();
                    SetPropertyIfChanged(ref _screenshot, value, () => Screenshot);
                }
			}
		}

        /// <inheritdoc />
        public bool UpdateWarningEnabled
		{
			get => _updateWarningEnabled;
            set
			{
				SetPropertyIfChanged(ref _updateWarningEnabled, value, () => UpdateWarningEnabled);
			}
		}

        /// <inheritdoc />
        public bool UpdateChecksEnabled
		{
			get => _updateChecksEnabled;
            set
			{
				SetPropertyIfChanged(ref _updateChecksEnabled, value, () => UpdateChecksEnabled);
			}
		}

        #endregion

        #region IScriptedMod Members

        /// <inheritdoc />
        public bool HasInstallScript => InstallScript != null;

        /// <summary>
		/// Gets or sets the mod's install script.
		/// </summary>
		/// <value>The mod's install script.</value>
		public IScript InstallScript
		{
			get
			{
				if (_installScript == null && !string.IsNullOrEmpty(_installScriptPath))
                {
                    _installScript = _installScriptType.LoadScript(TextUtil.ByteToString(GetFile(_installScriptPath)));
                }

                return _installScript;
			}
			set
			{
				_installScript = value;

                if (_installScript == null)
				{
					_installScriptType = null;
					_installScriptPath = null;
				}
				else
				{
					_installScriptType = _installScript.Type;
					_installScriptPath = Path.Combine("fomod", _installScriptType.FileNames[0]);
				}
			}
		}

        #endregion

        /// <inheritdoc />
        public string ScreenshotPath { get; private set; }

        /// <inheritdoc />
		public string Filename => string.IsNullOrEmpty(_nestedFilePath) ? ModArchivePath : _nestedFilePath;

        /// <inheritdoc />
		public string ModArchivePath { get; }

        public int PlaceInModLoadOrder
        {
            get => _placeInModLoadOrder;
            set
            {
                SetPropertyIfChanged(ref _placeInModLoadOrder, value, () => PlaceInModLoadOrder);
            }
        }

        public int NewPlaceInModLoadOrder
        {
            get => _newPlaceInModLoadOrder;
            set
            {
                SetPropertyIfChanged(ref _newPlaceInModLoadOrder, value, () => NewPlaceInModLoadOrder);
            }
        }

		/// <summary>
		/// Gets the registry of supported script types.
		/// </summary>
		/// <value>The registry of supported script types.</value>
		protected IScriptTypeRegistry ScriptTypeRegistry { get; }

        /// <inheritdoc />
        public IModFormat Format { get; }

		protected IList<string> StopFolders { get; }

        protected string PluginsDirectoryName { get; }

        protected IList<string> PluginExtensions { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// A simple constructor that initializes the FOMod from the specified file.
        /// </summary>
        /// <param name="filePath">The mod file from which to create the FOMod.</param>
        /// <param name="modFormat">The format of the mod.</param>
        /// <param name="stopFolders">A list of folders names that indicate the root of the mod file structure.</param>
        /// <param name="pluginsDirectoryName">The name of the folder that contains plugins.</param>
        /// <param name="modCacheManager">The manager for the current game mode's mod cache.</param>
        /// <param name="scriptTypeRegistry">The registry of supported script types.</param>
        public FOMod(string filePath, IModFormat modFormat, IEnumerable<string> stopFolders, string pluginsDirectoryName, IEnumerable<string> pluginExtensions, IModCacheManager modCacheManager, IScriptTypeRegistry scriptTypeRegistry, bool usePlugins, bool nestedArchives)
		{
            _environmentInfo = modCacheManager.EnvironmentInfo;
			StopFolders = new List<string>(stopFolders);

            if (!StopFolders.Contains("fomod", StringComparer.OrdinalIgnoreCase))
            {
                StopFolders.Add("fomod");
            }

            PluginsDirectoryName = pluginsDirectoryName;
			PluginExtensions = new List<string>(pluginExtensions);

            const bool useCache = true;

            Format = modFormat;
			ScriptTypeRegistry = scriptTypeRegistry;
			_usesPlugins = usePlugins;
            
            var checkNested = nestedArchives;
			var checkPrefix = true;
			var checkScript = true;
			var cacheInfo = false;
			var dirtyCache = false;
			string strCheckPrefix = null;
			string checkScriptPath = null;
			string checkScriptType = null;

			ModArchivePath = filePath;
			_archiveFile = new Archive(filePath);

            _downloadDate = File.GetLastWriteTime(ModArchivePath);

            modCacheManager.MigrateCacheFile(this);

			#region Check for cacheInfo.txt file

			var strCachePath = Path.Combine(modCacheManager.ModCacheDirectory, Path.GetFileNameWithoutExtension(filePath));
			_cachePath = strCachePath;
						
			if (Directory.Exists(strCachePath))
			{
				var strCacheInfoFile = Path.Combine(strCachePath, "cacheInfo.txt");

				if (File.Exists(strCacheInfoFile))
				{
					var bCacheInfo = File.ReadAllBytes(strCacheInfoFile);
					var sCacheInfo = Encoding.UTF8.GetString(bCacheInfo, 0, bCacheInfo.Length);
					var strPref = sCacheInfo.Split(new[] { "@@" }, StringSplitOptions.RemoveEmptyEntries);

                    if (strPref.Length > 0)
					{
						checkNested = Convert.ToBoolean(strPref[0]);

						if (strPref.Length > 1)
						{
							strCheckPrefix = strPref[1];

                            foreach (var folder in IgnoreFolders)
							{
								if (strCheckPrefix.IndexOf(folder, StringComparison.InvariantCultureIgnoreCase) >= 0)
								{
									checkNested = true;
									strCheckPrefix = string.Empty;
									dirtyCache = true;
									break;
								}
							}

							if (string.IsNullOrEmpty(strCheckPrefix) || !strCheckPrefix.Equals("-"))
							{
								if (!stopFolders.Any() && !usePlugins)
								{
									FileUtil.ForceDelete(_cachePath);
									checkNested = true;
									strCheckPrefix = string.Empty;
									dirtyCache = true;
								}
							}

							if (!dirtyCache)
							{

								if (strCheckPrefix.Equals("-"))
                                {
                                    strCheckPrefix = string.Empty;
                                }

                                checkPrefix = false;

								if (strPref.Length > 2)
								{
									checkScriptPath = strPref[2];

                                    if (checkScriptPath.Equals("-"))
                                    {
                                        checkScriptPath = string.Empty;
                                    }

                                    checkScriptType = strPref[3];

                                    if (checkScriptType.Equals("-"))
                                    {
                                        checkScriptType = string.Empty;
                                    }

                                    checkScript = false;
								}
							}
						}
					}
				}
			}

			#endregion

			if (checkNested && nestedArchives)
			{
                #region Temporary fix for nested .dazip files

                var strNested = _archiveFile.GetFiles("", "*.dazip", true);

                if (strNested.Length == 1)
				{
					var strFilePath = Path.Combine(Path.Combine(Path.GetTempPath(), "NMM"), strNested[0]);
					FileUtil.WriteAllBytes(strFilePath, GetFile(strNested[0]));

                    if (File.Exists(strFilePath))
					{
						_archiveFile = new Archive(strFilePath);
						_nestedFilePath = strFilePath;
					}
				}

				#endregion
			}

			_archiveFile.ReadOnlyInitProgressUpdated += ArchiveFile_ReadOnlyInitProgressUpdated;

			if (checkPrefix)
			{
				FindPathPrefix();
                cacheInfo = true;
			}
			else
			{
				_prefixPath = string.IsNullOrEmpty(strCheckPrefix) ? string.Empty : strCheckPrefix;
			}

			//check for script
			if (checkScript)
			{
				foreach (var stpScript in scriptTypeRegistry.Types)
				{
					foreach (var strScriptName in stpScript.FileNames)
					{
						var strScriptPath = Path.Combine("fomod", strScriptName);

                        if (ContainsFile(strScriptPath))
						{
							_installScriptPath = strScriptPath;
							_installScriptType = stpScript;
							break;
						}
					}

					if (!string.IsNullOrEmpty(_installScriptPath))
                    {
                        break;
                    }
                }

                cacheInfo = true;
			}
			else
			{
				_installScriptPath = checkScriptPath;
				_installScriptType = string.IsNullOrEmpty(checkScriptType) ? null : scriptTypeRegistry.Types.FirstOrDefault(x => x.TypeName.Equals(checkScriptType));
			}

			_archiveFile.FilesChanged += Archive_FilesChanged;
			
			//check for screenshot
			string[] strScreenshots;

			if (Directory.Exists(_cachePath))
			{
				var fileList = Directory.GetFiles(Path.Combine(_cachePath, GetRealPath("fomod")), "screenshot*", SearchOption.AllDirectories);
                strScreenshots = fileList;
            }
			else
            {
                strScreenshots = _archiveFile.GetFiles(GetRealPath("fomod"), "screenshot*", false);
            }

            //TODO make sure the file is a valid image
			if (strScreenshots.Length > 0)
            {
                ScreenshotPath = strScreenshots[0];
            }

            var cacheFile = Path.Combine(strCachePath, "cacheInfo.txt");

			if (!File.Exists(cacheFile))
            {
                var strTmpInfo = modCacheManager.FileUtility.CreateTempDirectory();

                try
				{
					Directory.CreateDirectory(Path.Combine(strTmpInfo, GetRealPath("fomod")));

					if (ContainsFile("fomod/info.xml"))
                    {
                        FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath("fomod/info.xml")), GetFile("fomod/info.xml"));
                    }
                    else
                    {
                        FileUtil.WriteAllText(Path.Combine(strTmpInfo, GetRealPath("fomod/info.xml")), "<fomod/>");
                    }

                    if (!string.IsNullOrEmpty(_readmePath))
                    {
                        FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(_readmePath)), GetFile(_readmePath));
                    }

                    if (!string.IsNullOrEmpty(ScreenshotPath))
                    {
                        FileUtil.WriteAllBytes(Path.Combine(strTmpInfo, GetRealPath(ScreenshotPath)), GetFile(ScreenshotPath));
                    }

                    modCacheManager.CreateCacheFile(this, strTmpInfo);
				}
				finally
				{
					FileUtil.ForceDelete(strTmpInfo);
				}
            }

			if (cacheInfo || !File.Exists(cacheFile))
			{

				var bteText = new UTF8Encoding(true).GetBytes(string.Format("{0}@@{1}@@{2}@@{3}",
					(!string.IsNullOrEmpty(_nestedFilePath)).ToString(),
					string.IsNullOrEmpty(_prefixPath) ? "-" : _prefixPath,
					string.IsNullOrEmpty(_installScriptPath) ? "-" : _installScriptPath,
					_installScriptType == null ? "-" : _installScriptType.TypeName));

				if (bteText != null)
				{
					try
					{
						File.WriteAllBytes(cacheFile, bteText);
					}
					catch (Exception e)
					{
					    Trace.TraceWarning("FOMod.FOMod() - Encountered an ignored Exception.");
					    TraceUtil.TraceException(e);
                    }
				}
					
			}

			ModName = Path.GetFileNameWithoutExtension(ModArchivePath);
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
                try
                {
                    var xmlInfo = new XmlDocument();
                    xmlInfo.LoadXml(TextUtil.ByteToString(GetFile("fomod/info.xml")));
                    LoadInfo(xmlInfo, false);
                }
                catch (XmlException e)
                {
                    Trace.TraceError("Error parsing FOMOD Info.xml file.");
                    TraceUtil.TraceException(e);

                    throw new InvalidDataException("Error parsing FOMOD Info.xml file.", e);
                }
			}
		}

        #endregion

        #region Read Transactions

        /// <inheritdoc />
        public void BeginReadOnlyTransaction(FileUtil fileUtil)
		{
			_archiveFile.BeginReadOnlyTransaction(fileUtil);
		}

        /// <inheritdoc />
        public void EndReadOnlyTransaction()
		{
			_archiveFile.EndReadOnlyTransaction();
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
			var stkPaths = new Stack<string>();
			stkPaths.Push("/");

			while (stkPaths.Count > 0)
			{
				var strSourcePath = stkPaths.Pop();
				var directories = _archiveFile.GetDirectories(strSourcePath);
				var booFoundData = false;
				var booFoundPrefix = false;

                foreach (var strDirectory in directories)
				{
					var booSkipFolder = false;

					foreach (var folder in IgnoreFolders)
                    {
                        if (strDirectory.IndexOf(folder, StringComparison.InvariantCultureIgnoreCase) >= 0)
                        {
                            booSkipFolder = true;
                            break;
                        }
                    }

                    if (booSkipFolder)
                    {
                        continue;
                    }

                    stkPaths.Push(strDirectory);

                    if (StopFolders.Contains(Path.GetFileName(strDirectory).ToLowerInvariant()))
					{
						booFoundPrefix = true;
						break;
					}

                    if (_usesPlugins)
                    {
                        booFoundData |= Path.GetFileName(strDirectory).Equals(PluginsDirectoryName, StringComparison.OrdinalIgnoreCase);
                    }
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
					var booFound = false;

                    foreach (var strExtension in PluginExtensions)
                    {
                        if (_archiveFile.GetFiles(strSourcePath, "*" + strExtension, false).Length > 0)
                        {
                            booFound = true;
                            break;
                        }
                    }

                    if (booFound)
					{
						strPrefixPath = strSourcePath;
						break;
					}
				}
			}

			strPrefixPath = strPrefixPath == null ? "" : strPrefixPath.Trim(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (!string.IsNullOrEmpty(strPrefixPath))
            {
                strPrefixPath = InitializeMovedArchive(strPrefixPath);
            }

            _movedArchiveInitialized = true;
			_prefixPath = strPrefixPath;
		}

		private string InitializeMovedArchive(string pathPrefix)
		{
			pathPrefix = pathPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			pathPrefix = pathPrefix.Trim(Path.DirectorySeparatorChar);
			pathPrefix += Path.DirectorySeparatorChar;

			_movedArchiveFiles.Clear();

            var files = _archiveFile.GetFiles("/", true);
			var intTrimLength = pathPrefix.Length;

            for (var i = files.Length - 1; i >= 0; i--)
			{
				files[i] = files[i].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				var file = files[i];

                string newFileName;

                if (!file.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase))
				{
					newFileName = file;
					var directoryName = Path.GetDirectoryName(newFileName);
					var fileName = Path.GetFileNameWithoutExtension(file);
					var extension = Path.GetExtension(file);

                    for (var j = 1; _movedArchiveFiles.ContainsKey(newFileName); j++)
                    {
                        newFileName = Path.Combine(directoryName, fileName + " " + j + extension);
                    }
                }
				else
                {
                    newFileName = file.Remove(0, intTrimLength);
                }

                _movedArchiveFiles[newFileName] = file;
			}

			return pathPrefix;
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
		/// <param name="path">The filename whose existence in the FOMod is to be determined.</param>
		/// <returns><c>true</c> if the specified file is in the FOMod; <c>false</c> otherwise.</returns>
		public bool ContainsFile(string path)
		{
			return ContainsFile(path, false);
		}

		/// <summary>
		/// Determines if the FOMod contains the given file.
		/// </summary>
		/// <param name="path">The filename whose existence in the FOMod is to be determined.</param>
		/// <returns><c>true</c> if the specified file is in the FOMod; <c>false</c> otherwise.</returns>
		private bool ContainsFile(string path, bool cacheOnly)
		{
			var strPath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			strPath = strPath.Trim(Path.DirectorySeparatorChar);

			if (Directory.Exists(_cachePath) && File.Exists(Path.Combine(_cachePath, GetRealPath(strPath))))
            {
                return true;
            }

            if (cacheOnly)
            {
                return false;
            }

            return _movedArchiveFiles.ContainsKey(strPath) || _archiveFile.ContainsFile(GetRealPath(strPath));
        }

		/// <summary>
		/// This method adjusts the given virtual path to the actual path to the
		/// file in the mod.
		/// </summary>
		/// <remarks>
		/// This method account for the virtual restructuring of the mod file structure performed by
		/// <see cref="FindPathPrefix()"/>.
		/// </remarks>
		/// <param name="path">The path to adjust.</param>
		/// <returns>The adjusted path.</returns>
		protected string GetRealPath(string path)
		{
			path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			path = path.Trim(Path.DirectorySeparatorChar);

            if (_movedArchiveFiles.TryGetValue(path, out var strAdjustedPath))
            {
                return strAdjustedPath;
            }

            if (string.IsNullOrEmpty(_prefixPath))
            {
                return path;
            }

            return path.ToLowerInvariant().IndexOf(_prefixPath.ToLowerInvariant(), StringComparison.Ordinal) == 0 ? path : Path.Combine(_prefixPath, path);
		}

		/// <summary>
		/// Deletes the specified file.
		/// </summary>
		/// <param name="path">The path of the file to delete.</param>
		protected void DeleteFile(string path)
		{
			if (AllowArchiveEdits && !_archiveFile.ReadOnly && !_archiveFile.IsSolid)
            {
                _archiveFile.DeleteFile(GetRealPath(path));
            }

            if (Directory.Exists(_cachePath) && File.Exists(Path.Combine(_cachePath, GetRealPath(path))))
            {
                FileUtil.ForceDelete(Path.Combine(_cachePath, GetRealPath(path)));
            }
        }

		/// <summary>
		/// Replaces the specified file with the given data.
		/// </summary>
		/// <param name="path">The path of the file to replace.</param>
		/// <param name="data">The new file data.</param>
		protected void ReplaceFile(string path, byte[] data)
		{
			if (AllowArchiveEdits && !_archiveFile.ReadOnly && !_archiveFile.IsSolid)
            {
                _archiveFile.ReplaceFile(GetRealPath(path), data);
            }

            var fileInfo = new FileInfo(Path.Combine(_cachePath, GetRealPath(path)));

            if (Directory.Exists(_cachePath) && (File.Exists(Path.Combine(_cachePath, GetRealPath(path))) || fileInfo.IsReadOnly))
			{
				File.WriteAllBytes(Path.Combine(_cachePath, GetRealPath(path)), data);
			}
		}

		/// <summary>
		/// Replaces the specified file with the given data.
		/// </summary>
		/// <param name="path">The path of the file to replace.</param>
		/// <param name="data">The new file data.</param>
		protected void CreateOrReplaceFile(string path, byte[] data)
		{
			if (AllowArchiveEdits && !_archiveFile.ReadOnly && !_archiveFile.IsSolid)
            {
                _archiveFile.ReplaceFile(GetRealPath(path), data);
            }

            if (Directory.Exists(_cachePath))
			{
				FileUtil.ForceDelete(Path.Combine(_cachePath, GetRealPath(path)));
				File.WriteAllBytes(Path.Combine(_cachePath, GetRealPath(path)), data);
			}
		}

		/// <summary>
		/// Replaces the specified file with the given text.
		/// </summary>
		/// <param name="path">The path of the file to replace.</param>
		/// <param name="data">The new file text.</param>
		protected void ReplaceFile(string path, string data)
		{
			if (AllowArchiveEdits && !_archiveFile.ReadOnly && !_archiveFile.IsSolid)
            {
                _archiveFile.ReplaceFile(GetRealPath(path), data);
            }

            var fiFile = new FileInfo(Path.Combine(_cachePath, GetRealPath(path)));
			
			if (Directory.Exists(_cachePath) && (File.Exists(Path.Combine(_cachePath, GetRealPath(path))) || fiFile.IsReadOnly))
			{
				FileUtil.ForceDelete(Path.Combine(_cachePath, GetRealPath(path)));
				File.WriteAllText(Path.Combine(_cachePath, GetRealPath(path)), data);
			}
		}

        #endregion

        #region File Management

        /// <inheritdoc />
        public byte[] GetFile(string file)
        {
            if (!ContainsFile(file))
            {
                return Path.GetFileNameWithoutExtension(file)?.ToLower() == "screenshot"
                    ? (byte[]) new ImageConverter().ConvertTo(new Bitmap(1, 1), typeof(byte[]))
                    : throw new FileNotFoundException("File doesn't exist in FOMod", file);
            }

            return Directory.Exists(_cachePath) && File.Exists(Path.Combine(_cachePath, GetRealPath(file)))
                ? File.ReadAllBytes(Path.Combine(_cachePath, GetRealPath(file)))
                : _archiveFile.GetFileContents(GetRealPath(file));
        }

        /// <inheritdoc />
        public FileStream GetFileStream(string file)
        {
            // File is present in cache
            if (Directory.Exists(_cachePath) && File.Exists(Path.Combine(_cachePath, GetRealPath(file))))
            {
                return new FileStream(file, FileMode.Open);
            }
            
            // Otherwise grab file from archive.
            return _archiveFile.GetFileStream(GetRealPath(file), _environmentInfo.TemporaryPath);
        }

        /// <inheritdoc />
        public List<string> GetFileList()
		{
			return GetFileList(null, true);
		}

        /// <inheritdoc />
        public bool IsMatchingVersion()
		{
			var rgxClean = new Regex(@"([v(ver)]\.?)|((\.0)+$)", RegexOptions.IgnoreCase);
			var strThisVersion = rgxClean.Replace(_humanReadableVersion ?? "", "");
			var strThatVersion = rgxClean.Replace(_lastKnownVersion ?? "", "");

            return string.IsNullOrEmpty(strThisVersion) || string.IsNullOrEmpty(strThatVersion) ||
                   string.Equals(strThisVersion, strThatVersion, StringComparison.OrdinalIgnoreCase);
        }

		/// <summary>
		/// Retrieves the list of all files in the specified FOMod folder.
		/// </summary>
		/// <param name="folderPath">The FOMod folder whose file list is to be retrieved.</param>
		/// <param name="recurse">Whether to return files that are in subdirectories of the given directory.</param>
		/// <returns>The list of all files in the specified FOMod folder.</returns>
		public List<string> GetFileList(string folderPath, bool recurse)
		{
			var files = new List<string>();

            if (!_movedArchiveInitialized)
			{
				if (!string.IsNullOrEmpty(_prefixPath))
                {
                    InitializeMovedArchive(_prefixPath);
                }

                _movedArchiveInitialized = true;
			}

            foreach (var file in _archiveFile.GetFiles(folderPath, recurse))
            {
                if (!_movedArchiveFiles.ContainsValue(file))
                {
                    if (!file.StartsWith("fomod", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(file);
                    }
                }
            }

            var pathPrefix = folderPath ?? "";
			pathPrefix = pathPrefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			pathPrefix = pathPrefix.Trim(Path.DirectorySeparatorChar);

            if (pathPrefix.Length > 0)
            {
                pathPrefix += Path.DirectorySeparatorChar;
            }

            foreach (var file in _movedArchiveFiles.Keys)
            {
                if (file.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) && !file.StartsWith("fomod", StringComparison.OrdinalIgnoreCase))
                {
                    files.Add(file);
                }
            }

            files.Sort(CompareOrderFoldersFirst);

            return files;
		}

		#endregion

		#region Mod Info Management
         
		/// <summary>
		/// Updates the object's properties to the values of the
		/// given <see cref="IModInfo"/>.
		/// </summary>
		/// <param name="modInfo">The <see cref="IModInfo"/> whose values
		/// are to be used to update this object's properties.</param>
		/// <param name="overwriteAllValues">Whether to overwrite the current info values,
		/// or just the empty ones.</param>
		public void UpdateInfo(IModInfo modInfo, bool? overwriteAllValues)
		{
			var booChangedValue = false;

            if (overwriteAllValues == true || string.IsNullOrEmpty(Id))
			{
				Id = modInfo.Id;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(DownloadId) || overwriteAllValues == null)
			{
				DownloadId = modInfo.DownloadId;
				booChangedValue = true;
			}

            if ((overwriteAllValues != false || string.IsNullOrEmpty(ModName) || ModName.Equals(Path.GetFileNameWithoutExtension(ModArchivePath))) && !string.IsNullOrEmpty(modInfo.ModName))
			{
				ModName = modInfo.ModName;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(FileName))
			{
				FileName = modInfo.FileName;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(HumanReadableVersion))
			{
				HumanReadableVersion = modInfo.HumanReadableVersion;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(LastKnownVersion) || LastKnownVersion != modInfo.LastKnownVersion)
			{
				LastKnownVersion = modInfo.LastKnownVersion;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || IsEndorsed != modInfo.IsEndorsed)
			{
				IsEndorsed = modInfo.IsEndorsed;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || MachineVersion == null)
			{
				MachineVersion = modInfo.MachineVersion;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(Author) || overwriteAllValues == null)
			{
				Author = modInfo.Author;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || CategoryId != modInfo.CategoryId || overwriteAllValues == null)
			{
				CategoryId = modInfo.CategoryId;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || CustomCategoryId != modInfo.CustomCategoryId)
			{
				CustomCategoryId = modInfo.CustomCategoryId;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(Description) || overwriteAllValues == null)
			{
				Description = modInfo.Description;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || string.IsNullOrEmpty(InstallDate))
			{
				InstallDate = modInfo.InstallDate;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || Website == null || overwriteAllValues == null)
			{
				Website = modInfo.Website;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || UpdateWarningEnabled != modInfo.UpdateWarningEnabled)
			{
				UpdateWarningEnabled = modInfo.UpdateWarningEnabled;
				booChangedValue = true;
			}

            if (overwriteAllValues == true || UpdateChecksEnabled != modInfo.UpdateChecksEnabled)
			{
				UpdateChecksEnabled = modInfo.UpdateChecksEnabled;
				booChangedValue = true;
			}

			if (booChangedValue)
			{
				var xmlInfo = new XmlDocument();
				xmlInfo.AppendChild(SaveInfo(xmlInfo));

                using (var mstInfo = new MemoryStream())
				{
					xmlInfo.Save(mstInfo);
					ReplaceFile("fomod/info.xml", mstInfo.ToArray());
				}
			}

			if (overwriteAllValues == true || Screenshot != modInfo.Screenshot)
			{
				if (modInfo.Screenshot == null)
				{
					if (Screenshot != null && overwriteAllValues == true)
					{
						DeleteFile(ScreenshotPath);
						Screenshot = modInfo.Screenshot;
					}
				}
				else
				{
					Screenshot = modInfo.Screenshot;
					CreateOrReplaceFile(ScreenshotPath, Screenshot.Data);
				}
			}
		}

		/// <summary>
		/// Serializes this <see cref="IModInfo"/> to an XML fragment.
		/// </summary>
		/// <param name="xmlDocument">The <see cref="XmlDocument"/> to use to create the XML elements
		/// created during the unparsing.</param>
		/// <returns>The <see cref="XmlNode"/> that is the root of the XML fragment
		/// that represents this <see cref="IModInfo"/>.</returns>
		protected XmlNode SaveInfo(XmlDocument xmlDocument)
		{
			XmlNode xndInfo = xmlDocument.CreateElement("fomod");
			xndInfo.AppendChild(xmlDocument.CreateElement("Name")).InnerText = ModName;
			var xndVersion = xndInfo.AppendChild(xmlDocument.CreateElement("Version"));
			xndVersion.InnerText = HumanReadableVersion;

            if (MachineVersion != null)
            {
                xndVersion.Attributes.Append(xmlDocument.CreateAttribute("MachineVersion")).Value = MachineVersion.ToString();
            }

            xndInfo.AppendChild(xmlDocument.CreateElement("LatestKnownVersion")).InnerText = LastKnownVersion;
			xndInfo.AppendChild(xmlDocument.CreateElement("Id")).InnerText = Id;
			xndInfo.AppendChild(xmlDocument.CreateElement("DownloadId")).InnerText = DownloadId;
			xndInfo.AppendChild(xmlDocument.CreateElement("Author")).InnerText = Author;
			xndInfo.AppendChild(xmlDocument.CreateElement("CategoryId")).InnerText = CategoryId.ToString();
			xndInfo.AppendChild(xmlDocument.CreateElement("CustomCategoryId")).InnerText = CustomCategoryId.ToString();
			xndInfo.AppendChild(xmlDocument.CreateElement("IsEndorsed")).InnerText = IsEndorsed.ToString();
			xndInfo.AppendChild(xmlDocument.CreateElement("Description")).InnerText = Description;
			xndInfo.AppendChild(xmlDocument.CreateElement("UpdateWarningEnabled")).InnerText = UpdateWarningEnabled.ToString();
			xndInfo.AppendChild(xmlDocument.CreateElement("UpdateChecksEnabled")).InnerText = UpdateChecksEnabled.ToString();
			xndInfo.AppendChild(xmlDocument.CreateElement("PlaceInLoadOrder")).InnerText = PlaceInModLoadOrder.ToString();

            if (Website != null)
            {
                xndInfo.AppendChild(xmlDocument.CreateElement("Website")).InnerText = Website.ToString();
            }

            return xndInfo;
		}

		/// <summary>
		/// Deserializes an <see cref="IModInfo"/> from the given XML fragment.
		/// </summary>
		/// <param name="p_xndInfo">The XML fragment from which to deserialize the <see cref="IModInfo"/>.</param>
		/// <param name="p_booFillOnlyEmptyValues">Whether to only overwrite <c>null</c> or empty values.</param>
		protected void LoadInfo(XmlNode p_xndInfo, bool p_booFillOnlyEmptyValues)
		{
			var xndRoot = p_xndInfo.SelectSingleNode("fomod");
			var xndModName = xndRoot.SelectSingleNode("Name");

            if (xndModName != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(ModName)))
            {
                ModName = xndModName.InnerText;
            }

            var xndVersion = xndRoot.SelectSingleNode("Version");

            if (xndVersion != null)
			{
				if (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(HumanReadableVersion))
                {
                    HumanReadableVersion = xndVersion.InnerText;
                }

                var xatMachineVersion = xndVersion.Attributes["MachineVersion"];

                if (xatMachineVersion != null && (!p_booFillOnlyEmptyValues || MachineVersion == null))
				{
					try
					{
						MachineVersion = new Version(xatMachineVersion.Value);
					}
					catch (FormatException)
					{
						MachineVersion = new Version(Regex.Replace(xatMachineVersion.Value, "[^.0-9]", ""));
					}
				}
			}

			var xndLastKnownVersion = xndRoot.SelectSingleNode("LatestKnownVersion");

            if (xndLastKnownVersion != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(LastKnownVersion)))
            {
                LastKnownVersion = xndLastKnownVersion.InnerText;
            }

            var xndId = xndRoot.SelectSingleNode("Id");

            if (xndId != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(Id)))
            {
                Id = xndId.InnerText;
            }

            var xndDownloadId = xndRoot.SelectSingleNode("DownloadId");

            if (xndDownloadId != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(DownloadId)))
            {
                DownloadId = xndDownloadId.InnerText;
            }

            var xndAuthor = xndRoot.SelectSingleNode("Author");

            if (xndAuthor != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(Author)))
            {
                Author = xndAuthor.InnerText;
            }

            var xndCategory = xndRoot.SelectSingleNode("CategoryId");

            CategoryId =
                xndCategory != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(xndCategory.InnerText))
                    ? Convert.ToInt32(xndCategory.InnerText)
                    : 0;

            var xndCustomCategory = xndRoot.SelectSingleNode("CustomCategoryId");

            CustomCategoryId =
                xndCustomCategory != null &&
                (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(xndCustomCategory.InnerText))
                    ? Convert.ToInt32(xndCustomCategory.InnerText)
                    : -1;

            var xndEndorsed = xndRoot.SelectSingleNode("IsEndorsed");

            if (xndEndorsed != null)
            {
                try
                {
                    IsEndorsed = bool.TryParse(xndEndorsed.InnerText, out var endorsed) ? (bool?)endorsed : null;
                }
				catch
				{
					IsEndorsed = null;
				}
            }

			var xndDescription = xndRoot.SelectSingleNode("Description");

            if (xndDescription != null && (!p_booFillOnlyEmptyValues || string.IsNullOrEmpty(Description)))
            {
                Description = xndDescription.InnerText;
            }

            var xndWebsite = xndRoot.SelectSingleNode("Website");

            if (xndWebsite != null && !string.IsNullOrEmpty(xndWebsite.InnerText) && (!p_booFillOnlyEmptyValues || Website == null) && UriUtil.TryBuildUri(xndWebsite.InnerText, out var website))
			{
                Website = website;
            }

			if (string.IsNullOrEmpty(LastKnownVersion))
            {
                UpdateWarningEnabled = false;
            }
            else
			{
				var xndUpdateWarningEnabled = xndRoot.SelectSingleNode("UpdateWarningEnabled");

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

			var xndUpdateChecksEnabled = xndRoot.SelectSingleNode("UpdateChecksEnabled");

            if (xndUpdateChecksEnabled != null)
			{
				try
				{
					UpdateChecksEnabled = Convert.ToBoolean(xndUpdateChecksEnabled.InnerText);
				}
				catch
				{
					UpdateChecksEnabled = true;
				}
			}
			else
            {
                UpdateChecksEnabled = true;
            }

            var xndPlaceInLoadOrder = xndRoot.SelectSingleNode("PlaceInLoadOrder");

            if (xndPlaceInLoadOrder != null && !string.IsNullOrEmpty(xndPlaceInLoadOrder.InnerText) && (!p_booFillOnlyEmptyValues || PlaceInModLoadOrder == -1))
            {
                PlaceInModLoadOrder = int.Parse(xndPlaceInLoadOrder.InnerText);
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
            if (string.IsNullOrEmpty(x))
            {
                return string.IsNullOrEmpty(y) ? 0 : -1;
            }

            if (string.IsNullOrEmpty(y))
            {
                return 1;
            }

            var xDir = Path.GetDirectoryName(x);
            var yDir = Path.GetDirectoryName(y);

            if (string.IsNullOrEmpty(xDir))
            {
                return string.IsNullOrEmpty(yDir) ? 0 : 1;
            }

            return string.IsNullOrEmpty(yDir) ? -1 : string.Compare(xDir, yDir, StringComparison.Ordinal);
        }
	}
}
