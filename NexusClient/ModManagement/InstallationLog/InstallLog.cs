using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Nexus.Client.Mods;
using Nexus.Client.Util;
using Nexus.Transactions;
using System.Diagnostics;

namespace Nexus.Client.ModManagement.InstallationLog
{
	/// <summary>
	/// The log that tracks items that are installed by mods.
	/// </summary>
	/// <remarks>
	/// The install log can only be accessed by one install task at a time, so this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the log is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize(ModRegistry, string, string)"/> method returns the only instance that should be used.
	/// </remarks>
	public partial class InstallLog : IInstallLog
	{
		private static readonly IMod m_modOriginalValueMod = new DummyMod("ORIGINAL_VALUE", String.Format("Dummy Mod: {0}", "ORIGINAL_VALUE"));
		private static readonly IMod m_modModManagerValueMod = new DummyMod("MOD_MANAGER_VALUE", String.Format("Dummy Mod: {0}", "MOD_MANAGER_VALUE"));
		private static readonly Version CURRENT_VERSION = new Version("0.4.0.0");
		private static readonly Regex m_rgxCleanPath = new Regex("[" + Path.DirectorySeparatorChar + Path.AltDirectorySeparatorChar + "]{2,}");
		private static readonly object m_objEnlistmentLock = new object();
		private static Dictionary<string, TransactionEnlistment> m_dicEnlistments = null;

		#region Static Properties

		/// <summary>
		/// Gets the dummy mod used as a placeholder to indicate logged values that are original, meaning
		/// they weren't installed by a mod.
		/// </summary>
		/// <value>The dummy mod used as a placeholder to indicate logged values that are original, meaning
		/// they weren't installed by a mod.</value>
		public static IMod OriginalValueMod
		{
			get
			{
				return m_modOriginalValueMod;
			}
		}

		/// <summary>
		/// Gets the dummy mod used as a placeholder to indicate logged values that were installed
		/// the the client software itself, not a mod.
		/// </summary>
		/// <value>The dummy mod used as a placeholder to indicate logged values that were installed
		/// the the client software itself, not a mod.</value>
		public static IMod ModManagerValueMod
		{
			get
			{
				return m_modModManagerValueMod;
			}
		}

		/// <summary>
		/// Gets the current support version of the install log.
		/// </summary>
		/// <value>The current support version of the install log.</value>
		public static Version CurrentVersion
		{
			get
			{
				return CURRENT_VERSION;
			}
		}

		#endregion

		#region Singleton

		private static IInstallLog m_ilgCurrent = null;

		/// <summary>
		/// Initializes the install log.
		/// </summary>
		/// <param name="p_mdrManagedModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_strModInstallDirectory">The path of the directory where all of the mods are installed.</param>
		/// <param name="p_strLogPath">The path from which to load the install log information.</param>
		/// <returns>The initialized install log.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the install log has already
		/// been initialized.</exception>
		public static IInstallLog Initialize(ModRegistry p_mdrManagedModRegistry, string p_strModInstallDirectory, string p_strLogPath)
		{
			if (m_ilgCurrent != null)
				throw new InvalidOperationException("The Install Log has already been initialized.");
			m_ilgCurrent = new InstallLog(p_mdrManagedModRegistry, p_strModInstallDirectory, p_strLogPath);
			return m_ilgCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			m_ilgCurrent = null;
		}

		#endregion

		/// <summary>
		/// Reads the install log verion from the given install log.
		/// </summary>
		/// <param name="p_strLogPath">The install log whose version is to be read.</param>
		/// <returns>The version of the specified install log, or a version of
		/// <c>0.0.0.0</c> if the log format is not recognized.</returns>
		public static Version ReadVersion(string p_strLogPath)
		{
			if (!File.Exists(p_strLogPath))
				return new Version("0.0.0.0");

			XDocument docLog = XDocument.Load(p_strLogPath);

			XElement xelLog = docLog.Element("installLog");
			if (xelLog == null)
				return new Version("0.0.0.0");

			XAttribute xatVersion = xelLog.Attribute("fileVersion");
			if (xatVersion == null)
				return new Version("0.0.0.0");

			return new Version(xatVersion.Value);
		}

		private ActiveModRegistry m_amrModKeys = new ActiveModRegistry();

		private InstalledItemDictionary<string, object> m_dicInstalledFiles = null;
		private InstalledItemDictionary<IniEdit, string> m_dicInstalledIniEdits = null;
		private InstalledItemDictionary<string, byte[]> m_dicInstalledGameSpecificValueEdits = null;

		#region Properties

		/// <summary>
		/// Gets the <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.
		/// </summary>
		/// <value>The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</value>
		protected ModRegistry ManagedModRegistry { get; private set; }

		/// <summary>
		/// Gets the path of the install log file.
		/// </summary>
		/// <value>The path of the install log file.</value>
		protected string LogPath { get; private set; }

		/// <summary>
		/// Gets the path of the directory where all of the mods are installed.
		/// </summary>
		/// <value>The path of the directory where all of the mods are installed.</value>
		protected string ModInstallDirectory { get; private set; }

		/// <summary>
		/// Gets the key of the mod representing original values.
		/// </summary>
		/// <remarks>
		/// Origianl values are values that were preexisting on the system.
		/// </remarks>
		/// <value>The key of the mod representing original values.</value>
		public string OriginalValuesKey
		{
			get
			{
				return GetModKey(OriginalValueMod);
			}
		}

		/// <summary>
		/// Gets the list of active mods.
		/// </summary>
		/// <value>The list of active mods.</value>
		public ReadOnlyObservableList<IMod> ActiveMods
		{
			get
			{
				return m_amrModKeys.RegisteredMods;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_mdrManagedModRegistry">The <see cref="ModRegistry"/> that contains the list
		/// of managed <see cref="IMod"/>s.</param>
		/// <param name="p_strModInstallDirectory">The path of the directory where all of the mods are installed.</param>
		/// <param name="p_strLogPath">The path from which to load the install log information.</param>
		private InstallLog(ModRegistry p_mdrManagedModRegistry, string p_strModInstallDirectory, string p_strLogPath)
		{
			m_dicInstalledFiles = new InstalledItemDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
			m_dicInstalledIniEdits = new InstalledItemDictionary<IniEdit, string>();
			m_dicInstalledGameSpecificValueEdits = new InstalledItemDictionary<string, byte[]>();
			ModInstallDirectory = p_strModInstallDirectory.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			ManagedModRegistry = p_mdrManagedModRegistry;
			LogPath = p_strLogPath;
			LoadInstallLog();
			if (!m_amrModKeys.IsModRegistered(OriginalValueMod))
				AddActiveMod(OriginalValueMod, true);
		}

		#endregion

		#region Serialization/Deserialization

		/// <summary>
		/// Gets the mod info that is currently in the install log, indexed by mod key.
		/// </summary>
		/// <returns>The mod info that is currently in the install log, indexed by mod key.</returns>
		private IDictionary<string, IMod> GetInstallLogModInfo()
		{
			Dictionary<string, IMod> dicLoggedModInfo = new Dictionary<string, IMod>();
			XDocument docLog = XDocument.Load(LogPath);

			string strLogVersion = docLog.Element("installLog").Attribute("fileVersion").Value;
			if (!CURRENT_VERSION.ToString().Equals(strLogVersion))
				throw new Exception(String.Format("Invalid Install Log version: {0} Expecting {1}", strLogVersion, CURRENT_VERSION));

			XElement xelModList = docLog.Descendants("modList").FirstOrDefault();
			if (xelModList != null)
			{
				foreach (XElement xelMod in xelModList.Elements("mod"))
				{
					string strModPath = xelMod.Attribute("path").Value;
					if (!OriginalValueMod.Filename.Equals(strModPath) && !ModManagerValueMod.Filename.Equals(strModPath))
					{
						strModPath = Path.Combine(ModInstallDirectory, strModPath);
						XElement xelVersion = xelMod.Element("version");
						string strVersion = xelVersion.Attribute("machineVersion").Value;
						Version verVersion = String.IsNullOrEmpty(strVersion) ? null : new Version(strVersion);
						strVersion = xelVersion.Value;
						string strModName = xelMod.Element("name").Value;
						IMod modMod = new DummyMod(strModName, strModPath, verVersion, strVersion);
						dicLoggedModInfo[xelMod.Attribute("key").Value] = modMod;
					}
				}
			}
			return dicLoggedModInfo;
		}

		/// <summary>
		/// Loads the data from the Install Log file.
		/// </summary>
		private void LoadInstallLog()
		{
			Trace.TraceInformation(String.Format("Path: {0}", LogPath));
			if (!File.Exists(LogPath))
				SaveInstallLog();
			XDocument docLog = XDocument.Load(LogPath);
			Trace.TraceInformation("Loaded from XML.");

			string strLogVersion = docLog.Element("installLog").Attribute("fileVersion").Value;
			if (!CURRENT_VERSION.ToString().Equals(strLogVersion))
				throw new Exception(String.Format("Invalid Install Log version: {0} Expecting {1}", strLogVersion, CURRENT_VERSION));

			XElement xelModList = docLog.Descendants("modList").FirstOrDefault();
			if (xelModList != null)
			{
				foreach (XElement xelMod in xelModList.Elements("mod"))
				{
					string strModPath = xelMod.Attribute("path").Value;
					Trace.Write("Found " + strModPath + "...");
					if (OriginalValueMod.Filename.Equals(strModPath))
					{
						m_amrModKeys.RegisterMod(OriginalValueMod, xelMod.Attribute("key").Value, true);
						Trace.WriteLine("OK");
					}
					else if (ModManagerValueMod.Filename.Equals(strModPath))
					{
						m_amrModKeys.RegisterMod(ModManagerValueMod, xelMod.Attribute("key").Value, true);
						Trace.WriteLine("OK");
					}
					else
					{
						string strModName = xelMod.Element("name").Value;
						strModPath = Path.Combine(ModInstallDirectory, strModPath);
						XElement xelVersion = xelMod.Element("version");
						string strVersion = xelVersion.Attribute("machineVersion").Value;
						Version verVersion = String.IsNullOrEmpty(strVersion) ? null : new Version(strVersion);
						strVersion = xelVersion.Value;
						IMod modMod = ManagedModRegistry.GetMod(strModPath) ?? new DummyMod(strModName, strModPath, verVersion, strVersion);
						m_amrModKeys.RegisterMod(modMod, xelMod.Attribute("key").Value);
						if (modMod is DummyMod)
							Trace.WriteLine("Missing");
						else
							Trace.WriteLine("OK");
					}
				}
			}

			XElement xelFiles = docLog.Descendants("dataFiles").FirstOrDefault();
			if (xelFiles != null)
			{
				foreach (XElement xelFile in xelFiles.Elements("file"))
				{
					string strPath = xelFile.Attribute("path").Value;
					foreach (XElement xelMod in xelFile.Descendants("mod"))
						m_dicInstalledFiles[strPath].Push(xelMod.Attribute("key").Value, null);
				}
			}

			XElement xelIniEdits = docLog.Descendants("iniEdits").FirstOrDefault();
			if (xelIniEdits != null)
			{
				foreach (XElement xelIniEdit in xelIniEdits.Elements("ini"))
				{
					string strFile = xelIniEdit.Attribute("file").Value;
					string strSection = xelIniEdit.Attribute("section").Value;
					string strKey = xelIniEdit.Attribute("key").Value;
					IniEdit iniEntry = new IniEdit(strFile, strSection, strKey);
					foreach (XElement xelMod in xelIniEdit.Descendants("mod"))
						m_dicInstalledIniEdits[iniEntry].Push(xelMod.Attribute("key").Value, xelMod.Value);
				}
			}

			XElement xelGameSpecificValueEdits = docLog.Descendants("gameSpecificEdits").FirstOrDefault();
			if (xelGameSpecificValueEdits != null)
			{
				foreach (XElement xelGameSpecificValueEdit in xelGameSpecificValueEdits.Elements("edit"))
				{
					string strKey = xelGameSpecificValueEdit.Attribute("key").Value;
					foreach (XElement xelMod in xelGameSpecificValueEdit.Descendants("mod"))
						m_dicInstalledGameSpecificValueEdits[strKey].Push(xelMod.Attribute("key").Value, Convert.FromBase64String(xelMod.Value));
				}
			}
		}

		/// <summary>
		/// Save the data to the Install Log file.
		/// </summary>
		protected void SaveInstallLog()
		{
			XDocument docLog = new XDocument();
			XElement xelRoot = new XElement("installLog", new XAttribute("fileVersion", CURRENT_VERSION));
			docLog.Add(xelRoot);

			XElement xelModList = new XElement("modList");
			xelRoot.Add(xelModList);
			xelModList.Add(from kvp in m_amrModKeys.Registrations
						   select new XElement("mod",
									new XAttribute("path", (kvp.Key is DummyMod) ? kvp.Key.Filename : kvp.Key.Filename.Substring(ModInstallDirectory.Length)),
									new XAttribute("key", kvp.Value),
									new XElement("version",
										new XAttribute("machineVersion", kvp.Key.MachineVersion ?? new Version()),
										new XText(kvp.Key.HumanReadableVersion ?? "")),
									new XElement("name",
										new XText(kvp.Key.ModName))));

			XElement xelFiles = new XElement("dataFiles");
			xelRoot.Add(xelFiles);
			xelFiles.Add(from itm in m_dicInstalledFiles
						 select new XElement("file",
								new XAttribute("path", itm.Item),
								new XElement("installingMods",
									from m in itm.Installers
									select new XElement("mod",
										new XAttribute("key", m.InstallerKey)))));

			XElement xelIniEdits = new XElement("iniEdits");
			xelRoot.Add(xelIniEdits);
			xelIniEdits.Add(from itm in m_dicInstalledIniEdits
							select new XElement("ini",
								   new XAttribute("file", itm.Item.File),
								   new XAttribute("section", itm.Item.Section),
								   new XAttribute("key", itm.Item.Key),
								   new XElement("installingMods",
									   from m in itm.Installers
									   select new XElement("mod",
										   new XAttribute("key", m.InstallerKey),
										   new XText(m.Value)))));

			XElement xelGameSpecificValueEdits = new XElement("gameSpecificEdits");
			xelRoot.Add(xelGameSpecificValueEdits);
			xelGameSpecificValueEdits.Add(from itm in m_dicInstalledGameSpecificValueEdits
										  select new XElement("edit",
												 new XAttribute("key", itm.Item),
												 new XElement("installingMods",
													 from m in itm.Installers
													 select new XElement("mod",
														 new XAttribute("key", m.InstallerKey),
														 new XText(Convert.ToBase64String(m.Value))))));

			if (!Directory.Exists(Path.GetDirectoryName(LogPath)))
				Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
			docLog.Save(LogPath);
		}

		#endregion

		/// <summary>
		/// Normalizes the given path.
		/// </summary>
		/// <remarks>
		/// This removes multiple consecutive path separators and makes sure all path
		/// separators are <see cref="Path.DirectorySeparatorChar"/>.
		/// </remarks>
		/// <param name="p_strPath">The path to normalize.</param>
		/// <returns>The normalized path.</returns>
		protected string NormalizePath(string p_strPath)
		{
			string strNormalizedPath = m_rgxCleanPath.Replace(p_strPath, Path.DirectorySeparatorChar.ToString());
			strNormalizedPath = strNormalizedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
			return strNormalizedPath;
		}

		#region Transaction Handling

		/// <summary>
		/// Gets an enlistment into the ambient transaction, if one exists.
		/// </summary>
		/// <returns>An enlistment into the ambient transaction, or <c>null</c> if there is no ambient
		/// transaction.</returns>
		private TransactionEnlistment GetEnlistment()
		{
			Transaction txTransaction = Transaction.Current;
			TransactionEnlistment enlEnlistment = null; ;

			if (txTransaction != null)
			{
				lock (m_objEnlistmentLock)
				{
					if (m_dicEnlistments == null)
						m_dicEnlistments = new Dictionary<string, TransactionEnlistment>();

					if (m_dicEnlistments.ContainsKey(txTransaction.TransactionInformation.LocalIdentifier))
						enlEnlistment = m_dicEnlistments[txTransaction.TransactionInformation.LocalIdentifier];
					else
					{
						enlEnlistment = new TransactionEnlistment(txTransaction, this);
						m_dicEnlistments.Add(txTransaction.TransactionInformation.LocalIdentifier, enlEnlistment);
					}
				}
			}
			else
				enlEnlistment = new TransactionEnlistment(null, this);

			return enlEnlistment;
		}

		#endregion

		#region Mod Tracking

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
		/// <param name="p_modMod">The <see cref="IMod"/> being added.</param>
		public void AddActiveMod(IMod p_modMod)
		{
			AddActiveMod(p_modMod, false);
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
		/// <param name="p_modMod">The <see cref="IMod"/> being added.</param>
		/// <param name="p_booIsSpecial">Indicates that the mod is a special mod, internal to the
		/// install log, and show not be included in the list of active mods.</param>
		protected void AddActiveMod(IMod p_modMod, bool p_booIsSpecial)
		{
			GetEnlistment().AddActiveMod(p_modMod, p_booIsSpecial);
		}

		/// <summary>
		/// Replaces a mod in the install log, in a transaction.
		/// </summary>
		/// <remarks>
		/// This replaces a mod in the install log without changing its key.
		/// </remarks>
		/// <param name="p_modOldMod">The mod with to be replaced with the new mod in the install log.</param>
		/// <param name="p_modNewMod">The mod with which to replace the old mod in the install log.</param>
		public void ReplaceActiveMod(IMod p_modOldMod, IMod p_modNewMod)
		{
			GetEnlistment().ReplaceActiveMod(p_modOldMod, p_modNewMod);
		}

		/// <summary>
		/// Gets the key that was assigned to the specified mod.
		/// </summary>
		/// <param name="p_modMod">The mod whose key is to be retrieved.</param>
		/// <returns>The key that was assigned to the specified mod, or <c>null</c> if
		/// the specified mod has no key.</returns>
		public string GetModKey(IMod p_modMod)
		{
			return GetEnlistment().GetModKey(p_modMod);
		}

		/// <summary>
		/// Gets the mod identified by the given key.
		/// </summary>
		/// <param name="p_strKey">The key of the mod to be retrieved.</param>
		/// <returns>The mod identified by the given key, or <c>null</c> if
		/// no mod is identified by the given key.</returns>
		protected IMod GetMod(string p_strKey)
		{
			return GetEnlistment().GetMod(p_strKey);
		}

		/// <summary>
		/// Gets the list of mods whose versions don't match the version in the install log.
		/// </summary>
		/// <returns>The list of mods whose versions don't match the version in the install log.
		/// The key is the mod info stored in the install log. The value is the actual mod
		/// registered with the mod manager.</returns>
		public IEnumerable<KeyValuePair<IMod, IMod>> GetMismatchedVersionMods()
		{
			foreach (KeyValuePair<string, IMod> kvpMod in GetInstallLogModInfo())
			{
				IMod modRegistered = GetMod(kvpMod.Key);
				if ((modRegistered != null) && File.Exists(modRegistered.Filename) && !String.Equals(modRegistered.HumanReadableVersion ?? "", kvpMod.Value.HumanReadableVersion ?? ""))
					yield return new KeyValuePair<IMod, IMod>(kvpMod.Value, modRegistered);
			}
		}

		#endregion

		#region Uninstall

		/// <summary>
		/// Removes the mod, as well as entries for items installed by the given mod,
		/// from the install log.
		/// </summary>
		/// <param name="p_modUninstaller">The mod to remove.</param>
		public void RemoveMod(IMod p_modUninstaller)
		{
			GetEnlistment().RemoveMod(p_modUninstaller);
		}

		#endregion

		#region File Version Management

		/// <summary>
		/// Logs the specified data file as having been installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod installing the specified data file.</param>
		/// <param name="p_strDataFilePath">The file bieng installed.</param>
		public void AddDataFile(IMod p_modInstallingMod, string p_strDataFilePath)
		{
			GetEnlistment().AddDataFile(p_modInstallingMod, p_strDataFilePath);
		}

		/// <summary>
		/// Removes the specified data file as having been installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod for which to remove the specified data file.</param>
		/// <param name="p_strDataFilePath">The file being removed for the given mod.</param>
		public void RemoveDataFile(IMod p_modInstallingMod, string p_strDataFilePath)
		{
			GetEnlistment().RemoveDataFile(p_modInstallingMod, p_strDataFilePath);
		}

		/// <summary>
		/// Gets the mod that owns the specified file.
		/// </summary>
		/// <param name="p_strPath">The path of the file whose owner is to be retrieved.</param>
		/// <returns>The mod that owns the specified file.</returns>
		public IMod GetCurrentFileOwner(string p_strPath)
		{
			return GetEnlistment().GetCurrentFileOwner(p_strPath);
		}

		/// <summary>
		/// Gets the mod that owned the specified file prior to the current owner.
		/// </summary>
		/// <param name="p_strPath">The path of the file whose previous owner is to be retrieved.</param>
		/// <returns>The mod that owned the specified file prior to the current owner.</returns>
		public IMod GetPreviousFileOwner(string p_strPath)
		{
			return GetEnlistment().GetPreviousFileOwner(p_strPath);
		}

		/// <summary>
		/// Gets the key of the mod that owns the specified file.
		/// </summary>
		/// <param name="p_strPath">The path of the file whose owner is to be retrieved.</param>
		/// <returns>The key of the mod that owns the specified file.</returns>
		public string GetCurrentFileOwnerKey(string p_strPath)
		{
			IMod modCurrentOwner = GetCurrentFileOwner(p_strPath);
			return (modCurrentOwner == null) ? null : GetModKey(modCurrentOwner);
		}

		/// <summary>
		/// Gets the key of the mod that owned the specified file prior to the current owner.
		/// </summary>
		/// <param name="p_strPath">The path of the file whose previous owner is to be retrieved.</param>
		/// <returns>The key of the mod that owned the specified file prior to the current owner.</returns>
		public string GetPreviousFileOwnerKey(string p_strPath)
		{
			IMod modPreviousOwner = GetPreviousFileOwner(p_strPath);
			return (modPreviousOwner == null) ? null : GetModKey(modPreviousOwner);
		}

		/// <summary>
		/// Logs that the specified data file is an original value.
		/// </summary>
		/// <remarks>
		/// Logging an original data file prepares it to be overwritten by a mod's file.
		/// </remarks>
		/// <param name="p_strDataFilePath">The path of the data file to log as an
		/// original value.</param>
		public void LogOriginalDataFile(string p_strDataFilePath)
		{
			GetEnlistment().LogOriginalDataFile(p_strDataFilePath);
		}

		/// <summary>
		/// Gets the list of files that were installed by the given mod.
		/// </summary>
		/// <param name="p_modInstaller">The mod whose isntalled files are to be returned.</param>
		/// <returns>The list of files that were installed by the given mod.</returns>
		public IList<string> GetInstalledModFiles(IMod p_modInstaller)
		{
			return GetEnlistment().GetInstalledModFiles(p_modInstaller);
		}

		/// <summary>
		/// Gets all of the mods that have installed the specified file.
		/// </summary>
		/// <param name="p_strPath">The path of the file whose installers are to be retrieved.</param>
		/// <returns>All of the mods that have installed the specified file.</returns>
		public IList<IMod> GetFileInstallers(string p_strPath)
		{
			return GetEnlistment().GetFileInstallers(p_strPath);
		}

		#endregion

		#region INI Version Management

		/// <summary>
		/// Logs the specified INI edit as having been installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod installing the specified INI edit.</param>
		/// <param name="p_strSettingsFileName">The name of the edited INI file.</param>
		/// <param name="p_strSection">The section containting the INI edit.</param>
		/// <param name="p_strKey">The key of the edited INI value.</param>
		/// <param name="p_strValue">The value installed by the mod.</param>
		public void AddIniEdit(IMod p_modInstallingMod, string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			GetEnlistment().AddIniEdit(p_modInstallingMod, p_strSettingsFileName, p_strSection, p_strKey, p_strValue);
		}

		/// <summary>
		/// Replaces the edited value of the specified INI edit installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod whose INI edit value is to be replaced.</param>
		/// <param name="p_strSettingsFileName">The name of the Ini value whose edited value is to be replaced.</param>
		/// <param name="p_strSection">The section of the Ini value whose edited value is to be replaced.</param>
		/// <param name="p_strKey">The key of the Ini value whose edited value is to be replaced.</param>
		/// <param name="p_strValue">The value with which to replace the edited value of the specified INI edit installed by the given mod.</param>
		public void ReplaceIniEdit(IMod p_modInstallingMod, string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			GetEnlistment().ReplaceIniEdit(p_modInstallingMod, p_strSettingsFileName, p_strSection, p_strKey, p_strValue);
		}

		/// <summary>
		/// Removes the specified ini edit as having been installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod for which to remove the specified ini edit.</param>
		/// <param name="p_strSettingsFileName">The name of the edited INI file containing the INI edit being removed for the given mod.</param>
		/// <param name="p_strSection">The section containting the INI edit being removed for the given mod.</param>
		/// <param name="p_strKey">The key of the edited INI value whose edit is being removed for the given mod.</param>
		public void RemoveIniEdit(IMod p_modInstallingMod, string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			GetEnlistment().RemoveIniEdit(p_modInstallingMod, p_strSettingsFileName, p_strSection, p_strKey);
		}

		/// <summary>
		/// Gets the mod that owns the specified INI edit.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the edited INI file.</param>
		/// <param name="p_strSection">The section containting the INI edit.</param>
		/// <param name="p_strKey">The key of the edited INI value.</param>
		/// <returns>The mod that owns the specified INI edit.</returns>
		public IMod GetCurrentIniEditOwner(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return GetEnlistment().GetCurrentIniEditOwner(p_strSettingsFileName, p_strSection, p_strKey);
		}

		/// <summary>
		/// Gets the key of the mod that owns the specified INI edit.
		/// </summary>
		/// <param name="p_strSettingsFileName">The name of the edited INI file.</param>
		/// <param name="p_strSection">The section containting the INI edit.</param>
		/// <param name="p_strKey">The key of the edited INI value.</param>
		/// <returns>The key of the mod that owns the specified INI edit.</returns>
		public string GetCurrentIniEditOwnerKey(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			IMod modCurrentOwner = GetCurrentIniEditOwner(p_strSettingsFileName, p_strSection, p_strKey);
			return (modCurrentOwner == null) ? null : GetModKey(modCurrentOwner);
		}

		/// <summary>
		/// Gets the value of the specified key before it was most recently overwritten.
		/// </summary>
		/// <param name="p_strSettingsFileName">The Ini file containing the key whose previous value is to be retrieved.</param>
		/// <param name="p_strSection">The section containing the key whose previous value is to be retrieved.</param>
		/// <param name="p_strKey">The key whose previous value is to be retrieved.</param>
		/// <returns>The value of the specified key before it was most recently overwritten, or
		/// <c>null</c> if there was no previous value.</returns>
		public string GetPreviousIniValue(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return GetEnlistment().GetPreviousIniValue(p_strSettingsFileName, p_strSection, p_strKey);
		}

		/// <summary>
		/// Logs that the specified INI value is an original value.
		/// </summary>
		/// <remarks>
		/// Logging an original INI value prepares it to be overwritten by a mod's value.
		/// </remarks>
		/// <param name="p_strSettingsFileName">The name of the INI file containing the original value to log.</param>
		/// <param name="p_strSection">The section containting the original INI value to log.</param>
		/// <param name="p_strKey">The key of the original INI value to log.</param>
		/// <param name="p_strValue">The original value.</param>
		public void LogOriginalIniValue(string p_strSettingsFileName, string p_strSection, string p_strKey, string p_strValue)
		{
			GetEnlistment().LogOriginalIniValue(p_strSettingsFileName, p_strSection, p_strKey, p_strValue);
		}

		/// <summary>
		/// Gets the list of INI edits that were installed by the given mod.
		/// </summary>
		/// <param name="p_modInstaller">The mod whose isntalled edits are to be returned.</param>
		/// <returns>The list of edits that was installed by the given mod.</returns>
		public IList<IniEdit> GetInstalledIniEdits(IMod p_modInstaller)
		{
			return GetEnlistment().GetInstalledIniEdits(p_modInstaller);
		}

		/// <summary>
		/// Gets all of the mods that have installed the specified Ini edit.
		/// </summary>
		/// <param name="p_strSettingsFileName">The Ini file containing the key whose installers are to be retrieved.</param>
		/// <param name="p_strSection">The section containing the key whose installers are to be retrieved.</param>
		/// <param name="p_strKey">The key whose installers are to be retrieved.</param>
		/// <returns>All of the mods that have installed the specified Ini edit.</returns>
		public IList<IMod> GetIniEditInstallers(string p_strSettingsFileName, string p_strSection, string p_strKey)
		{
			return GetEnlistment().GetIniEditInstallers(p_strSettingsFileName, p_strSection, p_strKey);
		}

		#endregion

		#region Game Specific Value Version Management

		/// <summary>
		/// Logs the specified Game Specific Value edit as having been installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod installing the specified INI edit.</param>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <param name="p_bteValue">The value installed by the mod.</param>
		public void AddGameSpecificValueEdit(IMod p_modInstallingMod, string p_strKey, byte[] p_bteValue)
		{
			GetEnlistment().AddGameSpecificValueEdit(p_modInstallingMod, p_strKey, p_bteValue);
		}

		/// <summary>
		/// Replaces the edited value of the specified game specific value edit installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod whose game specific value edit value is to be replaced.</param>
		/// <param name="p_strKey">The key of the game spcified value whose edited value is to be replaced.</param>
		/// <param name="p_bteValue">The value with which to replace the edited value of the specified game specific value edit installed by the given mod.</param>
		public void ReplaceGameSpecificValueEdit(IMod p_modInstallingMod, string p_strKey, byte[] p_bteValue)
		{
			GetEnlistment().ReplaceGameSpecificValueEdit(p_modInstallingMod, p_strKey, p_bteValue);
		}

		/// <summary>
		/// Removes the specified Game Specific Value edit as having been installed by the given mod.
		/// </summary>
		/// <param name="p_modInstallingMod">The mod for which to remove the specified Game Specific Value edit.</param>
		/// <param name="p_strKey">The key of the Game Specific Value whose edit is being removed for the given mod.</param>
		public void RemoveGameSpecificValueEdit(IMod p_modInstallingMod, string p_strKey)
		{
			GetEnlistment().RemoveGameSpecificValueEdit(p_modInstallingMod, p_strKey);
		}

		/// <summary>
		/// Gets the mod that owns the specified Game Specific Value edit.
		/// </summary>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <returns>The mod that owns the specified Game Specific Value edit.</returns>
		public IMod GetCurrentGameSpecificValueEditOwner(string p_strKey)
		{
			return GetEnlistment().GetCurrentGameSpecificValueEditOwner(p_strKey);
		}

		/// <summary>
		/// Gets the key of the mod that owns the specified Game Specific Value edit.
		/// </summary>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <returns>The key of the mod that owns the specified Game Specific Value edit.</returns>
		public string GetCurrentGameSpecificValueEditOwnerKey(string p_strKey)
		{
			IMod modCurrentOwner = GetCurrentGameSpecificValueEditOwner(p_strKey);
			return (modCurrentOwner == null) ? null : GetModKey(modCurrentOwner);
		}

		/// <summary>
		/// Gets the value of the specified key before it was most recently overwritten.
		/// </summary>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <returns>The value of the specified key before it was most recently overwritten, or
		/// <c>null</c> if there was no previous value.</returns>
		public byte[] GetPreviousGameSpecificValue(string p_strKey)
		{
			return GetEnlistment().GetPreviousGameSpecificValue(p_strKey);
		}

		/// <summary>
		/// Logs that the specified Game Specific Value is an original value.
		/// </summary>
		/// <remarks>
		/// Logging an original Game Specific Value prepares it to be overwritten by a mod's value.
		/// </remarks>
		/// <param name="p_strKey">The key of the edited Game Specific Value.</param>
		/// <param name="p_bteValue">The original value.</param>
		public void LogOriginalGameSpecificValue(string p_strKey, byte[] p_bteValue)
		{
			GetEnlistment().LogOriginalGameSpecificValue(p_strKey, p_bteValue);
		}

		/// <summary>
		/// Gets the list of Game Specific Value edited keys that were installed by the given mod.
		/// </summary>
		/// <param name="p_modInstaller">The mod whose isntalled edits are to be returned.</param>
		/// <returns>The list of edited keys that was installed by the given mod.</returns>
		public IList<string> GetInstalledGameSpecificValueEdits(IMod p_modInstaller)
		{
			return GetEnlistment().GetInstalledGameSpecificValueEdits(p_modInstaller);
		}

		/// <summary>
		/// Gets all of the mods that have installed the specified game specific value edit.
		/// </summary>
		/// <param name="p_strKey">The key whose installers are to be retrieved.</param>
		/// <returns>All of the mods that have installed the specified game specific value edit.</returns>
		public IList<IMod> GetGameSpecificValueEditInstallers(string p_strKey)
		{
			return GetEnlistment().GetGameSpecificValueEditInstallers(p_strKey);
		}

		#endregion
	}
}
