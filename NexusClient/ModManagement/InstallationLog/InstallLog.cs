namespace Nexus.Client.ModManagement.InstallationLog
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml;
    using System.Xml.Linq;
    
    using Nexus.Client.Mods;
    using Nexus.Client.Util.Collections;
    using Nexus.Client.Games;
    using Nexus.Transactions;
    using Nexus.Client.Util;

	/// <summary>
	/// The log that tracks items that are installed by mods.
	/// </summary>
	/// <remarks>
	/// The install log can only be accessed by one install task at a time, so this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the log is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize(ModRegistry, IGameMode, string, string)"/> method returns the only instance that should be used.
	/// </remarks>
	public partial class InstallLog : IInstallLog
	{
        private static readonly object EnlistmentLock = new object();
		private static Dictionary<string, TransactionEnlistment> _enlistments;
		private static Dictionary<string, IMod> _fileOwner;

		#region Static Properties

		/// <summary>
		/// Gets the dummy mod used as a placeholder to indicate logged values that are original, meaning
		/// they weren't installed by a mod.
		/// </summary>
		/// <value>The dummy mod used as a placeholder to indicate logged values that are original, meaning
		/// they weren't installed by a mod.</value>
		public static IMod OriginalValueMod { get; } = new DummyMod("ORIGINAL_VALUE", "Dummy Mod: ORIGINAL_VALUE");

        /// <summary>
		/// Gets the dummy mod used as a placeholder to indicate logged values that were installed
		/// the the client software itself, not a mod.
		/// </summary>
		/// <value>The dummy mod used as a placeholder to indicate logged values that were installed
		/// the the client software itself, not a mod.</value>
		public static IMod ModManagerValueMod { get; } = new DummyMod("MOD_MANAGER_VALUE", "Dummy Mod: MOD_MANAGER_VALUE");

        /// <summary>
		/// Gets the current support version of the install log.
		/// </summary>
		/// <value>The current support version of the install log.</value>
		public static Version CurrentVersion { get; } = new Version("0.5.0.0");

        #endregion

		#region Singleton

		private static IInstallLog _instance;

        /// <summary>
        /// Initializes the install log.
        /// </summary>
        /// <param name="managedModRegistry">The <see cref="ModRegistry"/> that contains the list of managed <see cref="IMod"/>s.</param>
        /// <param name="gameMode">The current game mode.</param>
        /// <param name="modInstallDirectory">The path of the directory where all of the mods are installed.</param>
        /// <param name="logPath">The path from which to load the install log information.</param>
        /// <returns>The initialized install log.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the install log has already
        /// been initialized.</exception>
        public static IInstallLog Initialize(ModRegistry managedModRegistry, IGameMode gameMode, string modInstallDirectory, string logPath)
		{
			if (_instance != null)
            {
                throw new InvalidOperationException("The Install Log has already been initialized.");
            }

            _instance = new InstallLog(managedModRegistry, gameMode, modInstallDirectory, logPath);

			return _instance;
		}

		/// <inheritdoc />
		public IInstallLog ReInitialize(string logPath)
		{
			_instance = new InstallLog(ManagedModRegistry, GameMode, ModInstallDirectory, logPath);
			
            return _instance;
		}

		#endregion

		/// <summary>
		/// Reads the install log version from the given install log.
		/// </summary>
		/// <param name="logPath">The install log whose version is to be read.</param>
		/// <returns>The version of the specified install log, or a version of
		/// <c>0.0.0.0</c> if the log format is not recognized.</returns>
		public static Version ReadVersion(string logPath)
		{
			if (!File.Exists(logPath))
            {
                return new Version("0.0.0.0");
            }

            var docLog = XDocument.Load(logPath);

			var xelLog = docLog.Element("installLog");
			
            if (xelLog == null)
            {
                return new Version("0.0.0.0");
            }

            var xatVersion = xelLog.Attribute("fileVersion");
			
            if (xatVersion == null)
            {
                return new Version("0.0.0.0");
            }

            return new Version(xatVersion.Value);
		}

		/// <summary>
		/// Determines if the log at the given path is valid.
		/// </summary>
		/// <param name="logPath">The path of the log to validate.</param>
		/// <returns><c>true</c> if the given log is valid;
		/// <c>false</c> otherwise.</returns>
		public static bool IsLogValid(string logPath)
		{
			if (!File.Exists(logPath))
            {
                return false;
            }

            try
			{
				var docLog = XDocument.Load(logPath);
                return true;
			}
			catch (Exception e)
			{
				Trace.TraceError("Invalid Install Log ({0}):", logPath);
				Trace.Indent();
				TraceUtil.TraceException(e);
				Trace.Unindent();
				
                return false;
			}
        }

		private readonly ActiveModRegistry _activeModRegistry = new ActiveModRegistry();

		private readonly InstalledItemDictionary<string, object> _installedFiles;
		private readonly InstalledItemDictionary<IniEdit, string> _installedIniEdits;
		private readonly InstalledItemDictionary<string, byte[]> _gameSpecificValueEdits;

		#region Properties

		/// <summary>
		/// Gets the <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.
		/// </summary>
		/// <value>The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</value>
		protected ModRegistry ManagedModRegistry { get; }

		protected IGameMode GameMode { get; }

		/// <summary>
		/// Gets the path of the install log file.
		/// </summary>
		/// <value>The path of the install log file.</value>
		protected string LogPath { get; }

		/// <summary>
		/// Gets the path of the directory where all of the mods are installed.
		/// </summary>
		/// <value>The path of the directory where all of the mods are installed.</value>
		protected string ModInstallDirectory { get; }

        /// <inheritdoc />
		public string OriginalValuesKey => GetModKey(OriginalValueMod);

        /// <inheritdoc />
		public ReadOnlyObservableList<IMod> ActiveMods => _activeModRegistry.RegisteredMods;

        #endregion

		#region Constructors

        /// <summary>
        /// A simple constructor that initializes the object with its dependencies.
        /// </summary>
        /// <param name="managedModRegistry">The <see cref="ModRegistry"/> that contains the list
        /// of managed <see cref="IMod"/>s.</param>
        /// <param name="gameMode">The current game mode.</param>
        /// <param name="modInstallDirectory">The path of the directory where all of the mods are installed.</param>
        /// <param name="logPath">The path from which to load the install log information.</param>
        private InstallLog(ModRegistry managedModRegistry, IGameMode gameMode, string modInstallDirectory, string logPath)
		{
			_installedFiles = new InstalledItemDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			_installedIniEdits = new InstalledItemDictionary<IniEdit, string>();
			_gameSpecificValueEdits = new InstalledItemDictionary<string, byte[]>();
			
            ModInstallDirectory = modInstallDirectory.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			ManagedModRegistry = managedModRegistry;
			LogPath = logPath;
			GameMode = gameMode;
			
            LoadInstallLog();
			
            if (!_activeModRegistry.IsModRegistered(OriginalValueMod))
            {
                AddActiveMod(OriginalValueMod, true);
            }
        }

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Gets the mod info that is currently in the install log, indexed by mod key.
		/// </summary>
		/// <returns>The mod info that is currently in the install log, indexed by mod key.</returns>
		private IDictionary<string, IMod> GetInstallLogModInfo()
		{
			var loggedModInfo = new Dictionary<string, IMod>();
			var log = XDocument.Load(LogPath);

			var logVersion = log.Element("installLog")?.Attribute("fileVersion")?.Value;

            if (!CurrentVersion.ToString().Equals(logVersion))
            {
                throw new Exception($"Invalid Install Log version: \"{logVersion}\", expected \"{CurrentVersion}\".");
            }

            var modList = log.Descendants("modList").FirstOrDefault();
			
            if (modList != null)
			{
				foreach (var mod in modList.Elements("mod"))
				{
					var modPath = mod.Attribute("path")?.Value;

					if (!OriginalValueMod.Filename.Equals(modPath) && !ModManagerValueMod.Filename.Equals(modPath))
					{
                        if (string.IsNullOrEmpty(modPath))
                        {
                            throw new Exception($"Could not determine path to mod \"{mod}\"");
                        }

						modPath = Path.Combine(ModInstallDirectory, modPath);
						var version = mod.Element("version");
						var humanReadableVersion = version?.Attribute("machineVersion")?.Value;
						var machineVersion = string.IsNullOrEmpty(humanReadableVersion) ? null : new Version(humanReadableVersion);
						humanReadableVersion = version?.Value;
						var modName = mod.Element("name")?.Value;
						var installDate = "<No Data>";
						
                        if (mod.Element("installDate") != null)
                        {
                            installDate = mod.Element("installDate")?.Value;
                        }

                        IMod dummyMod = new DummyMod(modName, modPath, machineVersion, humanReadableVersion, "", installDate);

                        var loggedModInfoKey = mod.Attribute("key")?.Value;

                        if (loggedModInfoKey != null)
                        {
                            loggedModInfo[loggedModInfoKey] = dummyMod;
                        }
					}
				}
			}

			return loggedModInfo;
		}

		/// <summary>
		/// Loads the data from the Install Log file.
		/// </summary>
		private void LoadInstallLog()
		{
			Trace.TraceInformation($"Path: {LogPath}");
			
            if (!File.Exists(LogPath))
            {
                SaveInstallLog();
            }

            var docLog = XDocument.Load(LogPath);
			Trace.TraceInformation("Loaded from XML.");

			var logVersion = docLog.Element("installLog")?.Attribute("fileVersion")?.Value;
			
            if (!CurrentVersion.ToString().Equals(logVersion))
            {
				throw new Exception($"Invalid Install Log version: \"{logVersion}\", expected \"{CurrentVersion}\"");
			}

            var modList = docLog.Descendants("modList").FirstOrDefault();
			
            if (modList != null)
			{
				foreach (var mod in modList.Elements("mod"))
				{
					var modPath = mod.Attribute("path")?.Value;
					Trace.Write("Found " + modPath + "...");
					
                    if (OriginalValueMod.ModArchivePath.Equals(modPath))
					{
						_activeModRegistry.RegisterMod(OriginalValueMod, mod.Attribute("key")?.Value, true);
						Trace.WriteLine("OK");
					}
					else if (ModManagerValueMod.ModArchivePath.Equals(modPath))
					{
						_activeModRegistry.RegisterMod(ModManagerValueMod, mod.Attribute("key")?.Value, true);
						Trace.WriteLine("OK");
					}
					else
					{
						var strModName = mod.Element("name")?.Value;
						var installDate = "<No Data>";
						
                        if (mod.Element("installDate") != null)
                        {
                            installDate = mod.Element("installDate")?.Value;
                        }

                        modPath = Path.Combine(ModInstallDirectory, modPath);
						
                        var version = mod.Element("version");
						var humanReadableVersion = version.Attribute("machineVersion").Value;
						var machineVersion = string.IsNullOrEmpty(humanReadableVersion) ? null : new Version(humanReadableVersion);
						humanReadableVersion = version.Value;
						
                        var modMod = ManagedModRegistry.GetMod(modPath) ?? new DummyMod(strModName, modPath, machineVersion, humanReadableVersion, "", installDate);
						modMod.InstallDate = installDate;

						try
						{
							_activeModRegistry.RegisterMod(modMod, mod.Attribute("key").Value);
						}
						catch (ArgumentException) { }

                        Trace.WriteLine(modMod is DummyMod ? "Missing" : "OK");
                    }
				}
			}

			var files = docLog.Descendants("dataFiles").FirstOrDefault();
			
            if (files != null)
			{
				foreach (var file in files.Elements("file"))
				{
					var path = file.Attribute("path").Value;
					
                    foreach (var mod in file.Descendants("mod"))
                    {
                        _installedFiles[path].Push(mod.Attribute("key") != null ? mod.Attribute("key").Value : string.Empty, null);
                    }
                }
			}

			var iniEdits = docLog.Descendants("iniEdits").FirstOrDefault();
			
            if (iniEdits != null)
			{
				foreach (var iniEdit in iniEdits.Elements("ini"))
				{
					var file = iniEdit.Attribute("file").Value;
					var section = iniEdit.Attribute("section").Value;
					var key = iniEdit.Attribute("key").Value;
					var iniEntry = new IniEdit(file, section, key);
					
                    foreach (var xelMod in iniEdit.Descendants("mod"))
                    {
                        _installedIniEdits[iniEntry].Push(xelMod.Attribute("key").Value, xelMod.Value);
                    }
                }
			}

			var gameSpecificValueEdits = docLog.Descendants("gameSpecificEdits").FirstOrDefault();
			
            if (gameSpecificValueEdits != null)
			{
				foreach (var gameSpecificValueEdit in gameSpecificValueEdits.Elements("edit"))
				{
					var key = gameSpecificValueEdit.Attribute("key").Value;
					
                    foreach (var mod in gameSpecificValueEdit.Descendants("mod"))
                    {
                        _gameSpecificValueEdits[key].Push(mod.Attribute("key").Value, Convert.FromBase64String(mod.Value));
                    }
                }
			}
		}

		/// <summary>
		/// Save the data to the Install Log file.
		/// </summary>
		protected void SaveInstallLog()
		{
			var log = new XDocument();
			var root = new XElement("installLog", new XAttribute("fileVersion", CurrentVersion));
			log.Add(root);

			var modList = new XElement("modList");
			root.Add(modList);
			modList.Add(from kvp in _activeModRegistry.Registrations
						select new XElement("mod",
									new XAttribute("path", kvp.Key is DummyMod ? kvp.Key.ModArchivePath : kvp.Key.ModArchivePath.Substring(ModInstallDirectory.Length)),
									new XAttribute("key", kvp.Value),
									new XElement("version",
										new XAttribute("machineVersion", kvp.Key.MachineVersion ?? new Version()),
										new XText(kvp.Key.HumanReadableVersion ?? "")),
									new XElement("name",
										new XText(kvp.Key.ModName)),
									new XElement("installDate",
										new XText(kvp.Key.InstallDate ?? DateTime.Now.ToString()))));

			var files = new XElement("dataFiles");
			root.Add(files);
			files.Add(from itm in _installedFiles
						 select new XElement("file",
								new XAttribute("path", itm.Item),
								new XElement("installingMods",
									from m in itm.Installers
									select new XElement("mod",
										new XAttribute("key", m.InstallerKey)))));

			var iniEdits = new XElement("iniEdits");
			root.Add(iniEdits);
			iniEdits.Add(from itm in _installedIniEdits
							select new XElement("ini",
									new XAttribute("file", itm.Item.File),
									new XAttribute("section", itm.Item.Section),
									new XAttribute("key", itm.Item.Key),
									new XElement("installingMods",
									from m in itm.Installers
										select new XElement("mod",
											new XAttribute("key", m.InstallerKey),
											new XText(m.Value)))));

			var gameSpecificValueEdits = new XElement("gameSpecificEdits");
			root.Add(gameSpecificValueEdits);
			gameSpecificValueEdits.Add(from itm in _gameSpecificValueEdits
										  select new XElement("edit",
												 new XAttribute("key", itm.Item),
												 new XElement("installingMods",
													 from m in itm.Installers
													 select new XElement("mod",
														 new XAttribute("key", m.InstallerKey),
														 new XText(Convert.ToBase64String(m.Value))))));

            var logDirectory = Path.GetDirectoryName(LogPath);

            if (string.IsNullOrEmpty(logDirectory))
            {
                throw new Exception($"Directory of {nameof(LogPath)} \"{LogPath}\" is invalid.");
            }

			if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            log.Save(LogPath);
		}

		#endregion

		/// <inheritdoc />
		public byte[] GetXmlIniList()
        {
            if (_installedIniEdits.Any())
			{
				var fileName = Path.GetRandomFileName() + ".xml";
				var tempPath = Path.Combine(Path.GetTempPath(), fileName);
				var virtualDoc = new XDocument();
				var root = new XElement("virtualModActivator", new XAttribute("fileVersion", VirtualModActivator.CurrentVersion.ToString()));
				virtualDoc.Add(root);

				var iniEdits = new XElement("iniEdits");
				root.Add(iniEdits);
				iniEdits.Add(from itm in _installedIniEdits
								select new XElement("iniEdit",
									new XAttribute("modFile", _activeModRegistry.Registrations.FirstOrDefault(x => x.Value == itm.Installers.FirstOrDefault()?.InstallerKey).Key.Filename),
									new XElement("iniFile",
										new XText(itm.Item.File)),
									new XElement("iniSection",
										new XText(itm.Item.Section)),
									new XElement("iniKey",
										new XText(itm.Item.Key)),
									new XElement("iniValue",
										new XText(itm.Installers.FirstOrDefault()?.Value ?? throw new InvalidOperationException($"{nameof(itm.Installers)} has no value.")))));
				virtualDoc.Save(tempPath);

				var xmlDocument = new XmlDocument();
				xmlDocument.Load(tempPath);
				FileUtil.ForceDelete(tempPath);
				return Encoding.UTF8.GetBytes(xmlDocument.OuterXml);
			}

            return null;
        }

        /// <inheritdoc />
		public byte[] GetXmlModList()
		{
			var strFileName = Path.GetRandomFileName() + ".xml";
			var strTempPath = Path.Combine(Path.GetTempPath(), strFileName);
			var docVirtual = new XDocument();
			var xelRoot = new XElement("virtualModActivator", new XAttribute("fileVersion", VirtualModActivator.CurrentVersion.ToString()));
			docVirtual.Add(xelRoot);

			var xelModList = new XElement("modList");
			xelRoot.Add(xelModList);
			_fileOwner = new Dictionary<string, IMod>();

			foreach (var mod in _activeModRegistry.Registrations.Where(x => !(x.Key is DummyMod)))
			{
				//List<InstalledItemDictionary<string, object>.ItemInstallers> lstItems = _installedFiles.Where(x => CheckFileKeyValuePair(x) && (GetCurrentFileOwnerLogged(x.Item) != null) && (GetCurrentFileOwnerLogged(x.Item).Filename.ToLowerInvariant() == mod.Key.Filename.ToLowerInvariant())).ToList();
				var lstItems = _installedFiles.Where(x => GetCurrentFileOwnerLogged(x.Item) != null && GetCurrentFileOwnerLogged(x.Item).Filename.ToLowerInvariant() == mod.Key.Filename.ToLowerInvariant()).ToList();
				
                if (lstItems.Count > 0)
				{
                    var modFileName = mod.Key.Filename;

                    if (string.IsNullOrEmpty(modFileName))
                    {
                        throw new Exception($"Could not determine filename of mod \"{mod.Key.ModName}\".");
                    }

					var xelMod = new XElement("modInfo", 
						new XAttribute("modId", mod.Key.Id ?? string.Empty), 
						new XAttribute("modName", mod.Key.ModName), 
						new XAttribute("modFileName", Path.GetFileName(modFileName)), 
						new XAttribute("modFilePath", Path.GetDirectoryName(modFileName)));
					xelModList.Add(xelMod);

					foreach (var item in lstItems)
                    {
                        var xelFile = new XElement("fileLink",
							new XAttribute("realPath", Path.Combine(Path.GetFileNameWithoutExtension(modFileName), GameMode.GetModFormatAdjustedPath(mod.Key.Format, item.Item, mod.Key, true))),
							new XAttribute("virtualPath", GameMode.GetModFormatAdjustedPath(mod.Key.Format, item.Item, mod.Key, true)),
                            new XElement("linkPriority", "0"),
							new XElement("isActive", "true"));
						xelMod.Add(xelFile);
					}
				}
			}

			docVirtual.Save(strTempPath);

			var document = new XmlDocument();
			document.Load(strTempPath);
			FileUtil.ForceDelete(strTempPath);
			_fileOwner = null;
			
            return Encoding.UTF8.GetBytes(document.OuterXml);
		}

		private bool CheckModKeyValuePair(KeyValuePair<IMod, string> keyValuePair)
		{
			Trace.WriteLine("Mod kvp:");
			
            try
			{
				Trace.WriteLine(keyValuePair.Key.Id);
				Trace.WriteLine(keyValuePair.Key.ModName);
				Trace.WriteLine(keyValuePair.Key.Filename);
			}
			catch {}

			return true;
		}

		private bool CheckFileKeyValuePair(InstalledItemDictionary<string, object>.ItemInstallers p_item)
		{
			Trace.WriteLine("File kvp:");
			
            try
			{
				Trace.WriteLine(p_item.Item);
				Trace.WriteLine(GetCurrentFileOwnerLogged(p_item.Item).Filename);
			}
			catch {}

			return true;
		}

		#region Transaction Handling

		/// <summary>
		/// Gets an enlistment into the ambient transaction, if one exists.
		/// </summary>
		/// <returns>An enlistment into the ambient transaction, or <c>null</c> if there is no ambient
		/// transaction.</returns>
		private TransactionEnlistment GetEnlistment()
		{
			var transaction = Transaction.Current;
			TransactionEnlistment enlistment;

			if (transaction != null)
			{
				lock (EnlistmentLock)
				{
					if (_enlistments == null)
                    {
                        _enlistments = new Dictionary<string, TransactionEnlistment>();
                    }

                    if (_enlistments.ContainsKey(transaction.TransactionInformation.LocalIdentifier))
                    {
                        enlistment = _enlistments[transaction.TransactionInformation.LocalIdentifier];
                    }
                    else
					{
						enlistment = new TransactionEnlistment(transaction, this);
						_enlistments.Add(transaction.TransactionInformation.LocalIdentifier, enlistment);
					}
				}
			}
			else
            {
                enlistment = new TransactionEnlistment(null, this);
            }

            return enlistment;
		}

		#endregion

		#region Mod Tracking

        /// <inheritdoc />
		public void AddActiveMod(IMod mod)
		{
			AddActiveMod(mod, false);
		}

		/// <summary>
		/// Adds a mod to the install log.
		/// </summary>
		/// <remarks>
		/// Adding a mod to the install log assigns it a key. Keys are used to track file and
		/// edit versions.
		/// 
		/// If there is no current transaction, the mod is added directly to the install log. Otherwise,
		/// the mod is added to a buffer than can later be committed or rolled back.
		/// </remarks>
		/// <param name="mod">The <see cref="IMod"/> being added.</param>
		/// <param name="isSpecial">Indicates that the mod is a special mod, internal to the
		/// install log, and show not be included in the list of active mods.</param>
		protected void AddActiveMod(IMod mod, bool isSpecial)
		{
			GetEnlistment().AddActiveMod(mod, isSpecial);
		}

        /// <inheritdoc />
		public void ReplaceActiveMod(IMod oldMod, IMod newMod)
		{
			GetEnlistment().ReplaceActiveMod(oldMod, newMod);
		}

        /// <inheritdoc />
		public string GetModKey(IMod mod)
		{
			return GetEnlistment().GetModKey(mod);
		}

		/// <summary>
		/// Gets the mod identified by the given key.
		/// </summary>
		/// <param name="key">The key of the mod to be retrieved.</param>
		/// <returns>The mod identified by the given key, or <c>null</c> if
		/// no mod is identified by the given key.</returns>
		protected IMod GetMod(string key)
		{
			return GetEnlistment().GetMod(key);
		}

        /// <inheritdoc />
		public IEnumerable<KeyValuePair<IMod, IMod>> GetMismatchedVersionMods()
		{
			foreach (var mod in GetInstallLogModInfo())
			{
				var modRegistered = GetMod(mod.Key);
				
                if (modRegistered != null && File.Exists(modRegistered.ModArchivePath) && !string.Equals(modRegistered.HumanReadableVersion ?? "", mod.Value.HumanReadableVersion ?? "", StringComparison.InvariantCultureIgnoreCase) && !(string.IsNullOrWhiteSpace(modRegistered.HumanReadableVersion) || string.IsNullOrWhiteSpace(mod.Value.HumanReadableVersion)))
                {
                    yield return new KeyValuePair<IMod, IMod>(mod.Value, modRegistered);
                }
            }
		}

		#endregion

		#region Uninstall

        /// <inheritdoc />
		public void RemoveMod(IMod modToRemove)
		{
			GetEnlistment().RemoveMod(modToRemove);
		}

		#endregion

		#region File Version Management

        /// <inheritdoc />
		public void AddDataFile(IMod installingMod, string dataFilePath)
		{
			GetEnlistment().AddDataFile(installingMod, dataFilePath);
		}

        /// <inheritdoc />
		public void RemoveDataFile(IMod installingMod, string dataFilePath)
		{
			GetEnlistment().RemoveDataFile(installingMod, dataFilePath);
		}

        /// <inheritdoc />
		public IMod GetCurrentFileOwner(string path)
		{

			return GetEnlistment().GetCurrentFileOwner(path);
		}

		/// <summary>
		/// Gets the mod that owns the specified file.
		/// </summary>
		/// <param name="path">The path of the file whose owner is to be retrieved.</param>
		/// <returns>The mod that owns the specified file.</returns>
		public IMod GetCurrentFileOwnerLogged(string path)
		{
			if (_fileOwner.ContainsKey(path))
            {
                return _fileOwner[path];
            }

            var modMod = GetEnlistment().GetCurrentFileOwner(path);
			
            if (modMod != null)
			{
				_fileOwner.Add(path, modMod);
			}
			
            return modMod;
		}

        /// <inheritdoc />
		public IMod GetPreviousFileOwner(string path)
		{
			return GetEnlistment().GetPreviousFileOwner(path);
		}

        /// <inheritdoc />
		public string GetCurrentFileOwnerKey(string path)
		{
			var modCurrentOwner = GetCurrentFileOwner(path);

			return modCurrentOwner == null ? null : GetModKey(modCurrentOwner);
		}

        /// <inheritdoc />
		public string GetPreviousFileOwnerKey(string path)
		{
			var modPreviousOwner = GetPreviousFileOwner(path);
			
            return modPreviousOwner == null ? null : GetModKey(modPreviousOwner);
		}

        /// <inheritdoc />
		public void LogOriginalDataFile(string dataFilePath)
		{
			GetEnlistment().LogOriginalDataFile(dataFilePath);
		}

        /// <inheritdoc />
		public IList<string> GetInstalledModFiles(IMod installer)
		{
			return GetEnlistment().GetInstalledModFiles(installer);
		}

        /// <inheritdoc />
		public IList<IMod> GetFileInstallers(string path)
		{
			return GetEnlistment().GetFileInstallers(path);
		}

		#endregion

		#region INI Version Management

        /// <inheritdoc />
		public void AddIniEdit(IMod installingMod, string settingsFileName, string section, string key, string value)
		{
			GetEnlistment().AddIniEdit(installingMod, settingsFileName, section, key, value);
		}

        /// <inheritdoc />
		public void ReplaceIniEdit(IMod installingMod, string settingsFileName, string section, string key, string value)
		{
			GetEnlistment().ReplaceIniEdit(installingMod, settingsFileName, section, key, value);
		}

        /// <inheritdoc />
		public void RemoveIniEdit(IMod installingMod, string settingsFileName, string section, string key)
		{
			GetEnlistment().RemoveIniEdit(installingMod, settingsFileName, section, key);
		}

        /// <inheritdoc />
		public IMod GetCurrentIniEditOwner(string settingsFileName, string section, string key)
		{
			return GetEnlistment().GetCurrentIniEditOwner(settingsFileName, section, key);
		}

        /// <inheritdoc />
		public string GetCurrentIniEditOwnerKey(string settingsFileName, string section, string key)
		{
			var modCurrentOwner = GetCurrentIniEditOwner(settingsFileName, section, key);
			
            return modCurrentOwner == null ? null : GetModKey(modCurrentOwner);
		}

        /// <inheritdoc />
		public string GetPreviousIniValue(string settingsFileName, string section, string key)
		{
			return GetEnlistment().GetPreviousIniValue(settingsFileName, section, key);
		}

        /// <inheritdoc />
		public void LogOriginalIniValue(string settingsFileName, string section, string key, string value)
		{
			GetEnlistment().LogOriginalIniValue(settingsFileName, section, key, value);
		}

        /// <inheritdoc />
		public IList<IniEdit> GetInstalledIniEdits(IMod installer)
		{
			return GetEnlistment().GetInstalledIniEdits(installer);
		}

        /// <inheritdoc />
		public IList<IMod> GetIniEditInstallers(string settingsFileName, string section, string key)
		{
			return GetEnlistment().GetIniEditInstallers(settingsFileName, section, key);
		}

		#endregion

		#region Game Specific Value Version Management

        /// <inheritdoc />
		public void AddGameSpecificValueEdit(IMod installingMod, string key, byte[] value)
		{
			GetEnlistment().AddGameSpecificValueEdit(installingMod, key, value);
		}

        /// <inheritdoc />
		public void ReplaceGameSpecificValueEdit(IMod installingMod, string key, byte[] value)
		{
			GetEnlistment().ReplaceGameSpecificValueEdit(installingMod, key, value);
		}

        /// <inheritdoc />
		public void RemoveGameSpecificValueEdit(IMod installingMod, string key)
		{
			GetEnlistment().RemoveGameSpecificValueEdit(installingMod, key);
		}

        /// <inheritdoc />
		public IMod GetCurrentGameSpecificValueEditOwner(string key)
		{
			return GetEnlistment().GetCurrentGameSpecificValueEditOwner(key);
		}

        /// <inheritdoc />
		public string GetCurrentGameSpecificValueEditOwnerKey(string key)
		{
			var modCurrentOwner = GetCurrentGameSpecificValueEditOwner(key);

			return modCurrentOwner == null ? null : GetModKey(modCurrentOwner);
		}

        /// <inheritdoc />
		public byte[] GetPreviousGameSpecificValue(string key)
		{
			return GetEnlistment().GetPreviousGameSpecificValue(key);
		}

        /// <inheritdoc />
		public void LogOriginalGameSpecificValue(string key, byte[] value)
		{
			GetEnlistment().LogOriginalGameSpecificValue(key, value);
		}

        /// <inheritdoc />
		public IList<string> GetInstalledGameSpecificValueEdits(IMod installer)
		{
			return GetEnlistment().GetInstalledGameSpecificValueEdits(installer);
		}

        /// <inheritdoc />
		public IList<IMod> GetGameSpecificValueEditInstallers(string key)
		{
			return GetEnlistment().GetGameSpecificValueEditInstallers(key);
		}

		#endregion

		#region Backup Management

        /// <inheritdoc />
		public void Backup()
		{
			if (File.Exists(LogPath))
			{
				var backupLogPath = LogPath + ".bak";
				var installLog = new FileInfo(LogPath);
				var installLogBak = File.Exists(backupLogPath) ? new FileInfo(backupLogPath) : null;

				if (installLogBak == null || installLogBak.LastWriteTimeUtc != installLog.LastWriteTimeUtc)
				{
					for (var i = 4; i > 0; i--)
					{
						if (File.Exists(backupLogPath + i))
                        {
                            File.Copy(backupLogPath + i, backupLogPath + (i + 1), true);
                        }
                    }

					if (File.Exists(backupLogPath))
                    {
                        File.Copy(backupLogPath, backupLogPath + "1", true);
                    }

                    File.Copy(LogPath, backupLogPath, true);
				}
			}
		}

		/// <summary>
		/// This restores the first valid backup of the install log.
		/// </summary>
		public static bool Restore(string logPath)
		{
			var suffix = "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bad";
			
            if (File.Exists(logPath))
            {
                FileUtil.Move(logPath, logPath + suffix, true);
            }

            var backupLogPath = logPath + ".bak";
			
            if (IsLogValid(backupLogPath))
			{
				File.Copy(backupLogPath, logPath, true);
				return true;
			}
			
            if (File.Exists(backupLogPath))
            {
                FileUtil.Move(backupLogPath, backupLogPath + suffix, true);
            }

            for (var i = 1; i < 6; i++)
			{
				if (IsLogValid(backupLogPath + i))
				{
					FileUtil.Move(backupLogPath + i, logPath, true);
					return true;
				}
				
                if (File.Exists(backupLogPath + i))
                {
                    FileUtil.Move(backupLogPath + i, backupLogPath + i + suffix, true);
                }
            }

			return false;
		}

		#endregion

        /// <inheritdoc />
		public void Release()
		{
			_instance = null;
		}
	}
}
