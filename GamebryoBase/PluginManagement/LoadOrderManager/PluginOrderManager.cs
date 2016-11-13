using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.PluginManagement;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder
{
	/// <summary>
	/// The interface for LibLoadOrder functionality.
	/// </summary>
	/// <remarks>
	/// This use LibLoadOrder API to expose its plugin sorting and activation abilities.
	/// </remarks>
	public class PluginOrderManager : ILoadOrderManager, IDisposable
	{
		private static readonly object m_objLock = new object();
		private static Dictionary<string, string> dctFileHashes = new Dictionary<string, string>();
		private Regex m_rgxPluginFile = new Regex(@"(?i)^.+\.es[mp]$");
		private List<string> m_lstActivePlugins = new List<string>();
		private DateTime m_dtiMasterDate = DateTime.Now;
		private ThreadSafeObservableList<WriteLoadOrderTask> TaskList = new ThreadSafeObservableList<WriteLoadOrderTask>();
		private IBackgroundTask RunningTask = null;
		private IBackgroundTask ExternalTask = null;
		private bool Fallout4PluginManagement = false;

		#region Events

		public event EventHandler LoadOrderUpdate;
		public event EventHandler ActivePluginUpdate;
		public event EventHandler ExternalPluginAdded;
		public event EventHandler ExternalPluginRemoved;

		#endregion

		#region Properties

		/// <summary>
		/// Gets the path to the masterlist.
		/// </summary>
		/// <value>The path to the masterlist.</value>
		public string MasterlistPath
		{
			get
			{
				return String.Empty;
			}
		}

		/// <summary>
		/// Gets the path to the userlist.
		/// </summary>
		/// <value>The path to the userlist.</value>
		public string UserlistPath
		{
			get
			{
				return String.Empty;
			}
		}

		/// <summary>
		/// Gets whether the current config files may be obsolete.
		/// </summary>
		/// <value>Whether the current config files config files may be obsolete.</value>
		public bool ObsoleteConfigFiles { get; private set; }

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected GamebryoGameModeBase GameMode { get; private set; }

		/// <summary>
		/// Gets whether the load order is based on the plugin modified date.
		/// </summary>
		/// <value>Whether the load order is based on the plugin modified date.</value>
		protected bool TimestampOrder { get; private set; }

		/// <summary>
		/// Gets whether to ignore official plugins.
		/// </summary>
		/// <value>Whether to ignore official plugins.</value>
		protected bool IgnoreOfficialPlugins { get; private set; }

		/// <summary>
		/// Gets whether the load order is based on the plugin modified date.
		/// </summary>
		/// <value>Whether the load order is based on the plugin modified date.</value>
		protected bool ForcedReadOnly { get; private set; }

		/// <summary>
		/// Gets whether the load order and activation state are handled in the same file.
		/// </summary>
		/// <value>Whether the load order and activation state are handled in the same file.</value>
		protected bool SingleFileManagement { get; private set; }

		/// <summary>
		/// Gets the path to the file containing the list of active plugins.
		/// </summary>
		/// <value>The path to the file containing the list of active plugins.</value>
		protected string PluginsFilePath { get; private set; }

		/// <summary>
		/// Gets the path to the file containing the sorted list of plugins.
		/// </summary>
		/// <value>The path to the file containing the sorted list of plugins.</value>
		protected string LoadOrderFilePath { get; private set; }

		/// <summary>
		/// Gets the name of the game folder in AppData.
		/// </summary>
		/// <value>The name of the game folder in AppData.</value>
		protected string AppDataGameFolderName { get; private set; }

		/// <summary>
		/// Gets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; private set; }

		/// <summary>
		/// Gets the filewatcher class.
		/// </summary>
		/// <value>The filewatcher class.</value>
		protected List<FileSystemWatcher> FileWatchers { get; private set; }

		/// <summary>
		/// Gets the last valid load order.
		/// </summary>
		/// <value>The last valid load order.</value>
		protected List<string> LastValidLoadOrder { get; private set; }

		/// <summary>
		/// Gets the last valid active plugins list.
		/// </summary>
		/// <value>The last valid active plugins list.</value>
		protected List<string> LastValidActiveList { get; private set; }

		/// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected string[] OrderedCriticalPluginNames
		{
			get
			{
				if ((GameMode.OrderedCriticalPluginNames != null) && (GameMode.OrderedCriticalPluginNames.Count() > 0))
					return StripPluginDirectory(GameMode.OrderedCriticalPluginNames);
				else
					return null;
			}
		}

		/// <summary>
		/// Gets the list of official plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of official plugin names, ordered by load order.</value>
		protected string[] OrderedOfficialPluginNames
		{
			get
			{
				if ((GameMode.OrderedOfficialPluginNames != null) && (GameMode.OrderedOfficialPluginNames.Count() > 0))
					return StripPluginDirectory(GameMode.OrderedOfficialPluginNames);
				else
					return null;
			}
		}

		protected bool IsExternalInput
		{
			get
			{
				return ((((RunningTask == null) || ((RunningTask != null) && (RunningTask.Status == BackgroundTasks.TaskStatus.Complete))) && (TaskList.Count == 0)) && ((ExternalTask == null) || (ExternalTask.Status != BackgroundTasks.TaskStatus.Running)));
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameMode">The game mode for which plugins are being managed.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		/// <param name="p_strMasterlistPath">The path to the masterlist file to use.</param>
		public PluginOrderManager(IEnvironmentInfo p_eifEnvironmentInfo, GamebryoGameModeBase p_gmdGameMode, FileUtil p_futFileUtility)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameMode = p_gmdGameMode;
			FileUtility = p_futFileUtility;

			if (dctFileHashes != null)
				dctFileHashes.Clear();
			dctFileHashes.Add("plugins.txt", null);
			dctFileHashes.Add("loadorder.txt", null);

			InitializeManager();
		}

		/// <summary>
		/// The finalizer.
		/// </summary>
		/// <remarks>
		/// Disposes unmanaged resources used by LoadOrderManager.
		/// </remarks>
		~PluginOrderManager()
		{
			Dispose(false);
		}

		#endregion

		#region IDisposable Members

		/// <summary>
		/// Disposes of any resources that the LoadOrderManager allocated.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion

		/// <summary>
		/// Disposes of the unamanged resources need for LibLoadOrder API.
		/// </summary>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="Dispose()"/> method.</param>
		protected virtual void Dispose(bool p_booDisposing)
		{
		}

		/// <summary>
		/// Initializes the Plugin Order manager.
		/// </summary>
		private void InitializeManager()
		{
			AppDataGameFolderName = GameMode.ModeId;

			switch (GameMode.ModeId)
			{
				case "Oblivion":
					TimestampOrder = true;
					IgnoreOfficialPlugins = false;
					ForcedReadOnly = false;
					SingleFileManagement = false;
					Fallout4PluginManagement = false;
					break;
				case "Fallout3":
					TimestampOrder = true;
					IgnoreOfficialPlugins = false;
					ForcedReadOnly = false;
					SingleFileManagement = false;
					Fallout4PluginManagement = false;
					break;
				case "FalloutNV":
					TimestampOrder = true;
					IgnoreOfficialPlugins = false;
					ForcedReadOnly = false;
					SingleFileManagement = false;
					Fallout4PluginManagement = false;
					break;
				case "Skyrim":
					TimestampOrder = false;
					IgnoreOfficialPlugins = false;
					ForcedReadOnly = false;
					SingleFileManagement = false;
					Fallout4PluginManagement = false;
					break;
				case "Fallout4":
					if (GameMode.GameVersion >= new Version(1, 5, 0, 0))
					{
						TimestampOrder = false;
						SingleFileManagement = true;
						if (GameMode.GameVersion >= new Version(1, 5, 154, 0))
						{
							Fallout4PluginManagement = true;
							ForcedReadOnly = false;
						}
						else
							ForcedReadOnly = true;
					}
					else
					{
						TimestampOrder = false;
						ForcedReadOnly = true;
						SingleFileManagement = false;
						Fallout4PluginManagement = false;
					}
					IgnoreOfficialPlugins = true;
					break;
				case "SkyrimSE":
					AppDataGameFolderName = "Skyrim Special Edition";
					IgnoreOfficialPlugins = true;
					TimestampOrder = false;
					SingleFileManagement = true;
					Fallout4PluginManagement = true;
					ForcedReadOnly = false;
					break;
				default:
					throw new NotImplementedException(string.Format("Unsupported game: {0} ({1})", GameMode.Name, GameMode.ModeId));
			}

			string strLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string strGameModeLocalAppData = Path.Combine(strLocalAppData, AppDataGameFolderName);

			if (!Directory.Exists(AppDataGameFolderName))
			{
				try
				{
					FileUtil.CreateDirectory(AppDataGameFolderName);
				}
				catch { }
			}

			LastValidActiveList = RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray()).ToList();
			LastValidLoadOrder = RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray()).ToList();
			TaskList.CollectionChanged += new NotifyCollectionChangedEventHandler(TaskList_CollectionChanged);

			if (!TimestampOrder && !SingleFileManagement)
			{
				LoadOrderFilePath = Path.Combine(strGameModeLocalAppData, "loadorder.txt");
				if (!File.Exists(LoadOrderFilePath))
				{
					try
					{
						File.Create(LoadOrderFilePath).Dispose();
					}
					catch { }
				}
			}
			else if (SingleFileManagement)
			{
				LoadOrderFilePath = Path.Combine(strGameModeLocalAppData, "plugins.txt");
			}
			else
			{
				string strMasterPlugin = GameMode.OrderedCriticalPluginNames[0];
				if (File.Exists(strMasterPlugin))
					m_dtiMasterDate = File.GetLastWriteTime(strMasterPlugin);
			}

			PluginsFilePath = Path.Combine(strGameModeLocalAppData, "plugins.txt");

			Backup(strGameModeLocalAppData);

			SetupWatcher(strGameModeLocalAppData);
		}

		#region FileWatcher

		/// <summary>
		/// Initializes File Watcher.
		/// </summary>
		private void SetupWatcher(string p_strGameModeLocal)
		{
			FileWatchers = new List<FileSystemWatcher>();

			FileSystemWatcher FileWatcher = new FileSystemWatcher();

			if (Directory.Exists(p_strGameModeLocal))
			{
				FileWatcher.Path = p_strGameModeLocal;
				FileWatcher.IncludeSubdirectories = false;
				FileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
				FileWatcher.Filter = "*.txt";
				FileWatcher.Changed += new FileSystemEventHandler(FileWatcherOnChangedTxt);
				FileWatcher.EnableRaisingEvents = true;
				FileWatchers.Add(FileWatcher);
			}

			FileWatcher = new FileSystemWatcher();
			FileWatcher.Path = GameMode.PluginDirectory;
			FileWatcher.IncludeSubdirectories = false;
			FileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
			FileWatcher.Filter = "*.esp";
			if (TimestampOrder)
				FileWatcher.Changed += new FileSystemEventHandler(FileWatcherOnChangedLoose);
			FileWatcher.Deleted += new FileSystemEventHandler(FileWatcherOnDeletedLoose);
			FileWatcher.Created += new FileSystemEventHandler(FileWatcherOnCreatedLoose);
			FileWatcher.EnableRaisingEvents = true;
			FileWatchers.Add(FileWatcher);

			FileWatcher = new FileSystemWatcher();
			FileWatcher.Path = GameMode.PluginDirectory;
			FileWatcher.IncludeSubdirectories = false;
			FileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
			FileWatcher.Filter = "*.esm";
			if (TimestampOrder)
				FileWatcher.Changed += new FileSystemEventHandler(FileWatcherOnChangedLoose);
			FileWatcher.Deleted += new FileSystemEventHandler(FileWatcherOnDeletedLoose);
			FileWatcher.Created += new FileSystemEventHandler(FileWatcherOnCreatedLoose);
			FileWatcher.EnableRaisingEvents = true;
			FileWatchers.Add(FileWatcher);
		}

		/// <summary>
		/// Handles changes made to text files.
		/// </summary>
		private void FileWatcherOnChangedTxt(object source, FileSystemEventArgs e)
		{
			if ((source == null) || (e == null))
				return;

			string strFile = e.Name ?? string.Empty;
			string strPath = e.FullPath ?? string.Empty;

			if (IsExternalInput && !string.IsNullOrWhiteSpace(strFile))
			{
				int intRepeat = 0;
				bool? booReady = false;

				while (booReady == false)
				{
					Thread.Sleep(100);
					if (intRepeat++ >= 20)
						break;
					booReady = IsFileReady(strFile, ForcedReadOnly);
				}

				if (!CheckSameFileHash(strFile, strPath))
				{
					if (strFile.Equals("plugins.txt", StringComparison.InvariantCultureIgnoreCase))
					{
						if (ActivePluginUpdate != null)
							ActivePluginUpdate(GetActivePlugins(), new EventArgs());
					}
					else if (strFile.Equals("loadorder.txt", StringComparison.InvariantCultureIgnoreCase))
					{
						if (!TimestampOrder && !SingleFileManagement)
							if (LoadOrderUpdate != null)
								LoadOrderUpdate(GetLoadOrder(), new EventArgs());
					}
				}
			}
		}

		/// <summary>
		/// Handles changes made to files in the plugin installation folder.
		/// </summary>
		private void FileWatcherOnChangedLoose(object source, FileSystemEventArgs e)
		{
			if ((source == null) || (e == null))
				return;

			if (TimestampOrder && IsExternalInput)
			{
				int intRepeat = 0;
				bool? booReady = false;

				while (booReady == false)
				{
					Thread.Sleep(100);
					if (intRepeat++ >= 20)
						break;
					booReady = IsFileReady(e.FullPath, ForcedReadOnly);
				}

				if (LoadOrderUpdate != null)
					LoadOrderUpdate(GetLoadOrder(), new EventArgs());
			}
		}

		/// <summary>
		/// Handles new files added to the plugin installation folder.
		/// </summary>
		private void FileWatcherOnCreatedLoose(object source, FileSystemEventArgs e)
		{
			if ((source == null) || (e == null))
				return;

			if (IsExternalInput)
			{
				int intRepeat = 0;
				bool? booReady = false;

				while (booReady == false)
				{
					Thread.Sleep(100);
					if (intRepeat++ >= 20)
						break;
					booReady = IsFileReady(e.FullPath, ForcedReadOnly);
				}

				if (ExternalPluginAdded != null)
					ExternalPluginAdded(e.FullPath, new EventArgs());
			}
		}

		/// <summary>
		/// Handles files removed from the plugin installation folder.
		/// </summary>
		private void FileWatcherOnDeletedLoose(object source, FileSystemEventArgs e)
		{
			if ((source == null) || (e == null))
				return;

			if (IsExternalInput)
			{
				int intRepeat = 0;
				bool? booReady = IsFileReady(e.FullPath, ForcedReadOnly);

				while (intRepeat++ < 10)
				{
					Thread.Sleep(100);
					if (File.Exists(e.FullPath))
						return;
				}

				if (ExternalPluginRemoved != null)
					ExternalPluginRemoved(e.FullPath, new EventArgs());
			}
		}

		private bool CheckSameFileHash(string p_strName, string p_strFilePath)
		{
			string strHash = string.Empty;

			if (!dctFileHashes.ContainsKey(p_strName.ToLowerInvariant()))
				return true;

			using (var fs = new FileStream(p_strFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				SHA256CryptoServiceProvider sha = new SHA256CryptoServiceProvider();
				byte[] hash = sha.ComputeHash(fs);
				string strSHA = BitConverter.ToString(hash).Replace("-", string.Empty);

				strHash = strSHA;
			}

			if (string.Equals(strHash, dctFileHashes[p_strName.ToLowerInvariant()]))
				return true;
			else
			{
				dctFileHashes[p_strName.ToLowerInvariant()] = strHash;
				return false;
			}
		}

		#endregion

		/// <summary>
		/// Backup the plugins.txt and loadorder.txt files
		/// </summary>
		private void Backup(string p_strDataFolder)
		{
			if (File.Exists(LoadOrderFilePath) && !SingleFileManagement)
			{
				string strBakFilePath = Path.Combine(p_strDataFolder, "loadorder.nmm.bak");
				if (!File.Exists(strBakFilePath))
					File.Copy(LoadOrderFilePath, strBakFilePath, false);
			}

			if (File.Exists(PluginsFilePath))
			{
				string strBakFilePath = Path.Combine(p_strDataFolder, "plugins.nmm.bak");
				if (!File.Exists(strBakFilePath))
					File.Copy(PluginsFilePath, strBakFilePath, false);
			}
		}

		#region Plugin Helpers

		/// <summary>
		/// Removes the plugin directory from the given plugin paths.
		/// </summary>
		/// <remarks>
		/// BAPI expects plugin paths to be relative to the plugins directory. This
		/// adjusts the plugin paths for that purpose.
		/// </remarks>
		/// <param name="p_strPlugins">The array of plugin paths to adjust.</param>
		/// <returns>An array containing the given plugin path, in order, but relative to the plugins directory.</returns>
		protected string[] StripPluginDirectory(string[] p_strPlugins)
		{
			string[] strPlugins = new string[p_strPlugins.Length];
			for (Int32 i = strPlugins.Length - 1; i >= 0; i--)
				strPlugins[i] = StripPluginDirectory(p_strPlugins[i]);
			return strPlugins;
		}

		/// <summary>
		/// Removes the plugin directory from the given plugin path.
		/// </summary>
		/// <remarks>
		/// BAPI expects plugin paths to be relative to the plugins directory. This
		/// adjusts the plugin path for that purpose.
		/// </remarks>
		/// <param name="p_strPlugin">The plugin path to adjust.</param>
		/// <returns>The given plugin path, but relative to the plugins directory.</returns>
		protected string StripPluginDirectory(string p_strPlugin)
		{
			return FileUtil.RelativizePath(GameMode.PluginDirectory, p_strPlugin);
		}

		/// <summary>
		/// Makes the given plugin path absolute.
		/// </summary>
		/// <param name="p_strPlugin">The plugin path to adjust.</param>
		/// <returns>The absolute path to the specified plugin.</returns>
		protected string AddPluginDirectory(string p_strPlugin)
		{
			return Path.Combine(GameMode.PluginDirectory, p_strPlugin);
		}

		#endregion

		#region Plugin Management

		/// <summary>
		/// Removes non-existent and ghosted plugins from the given list.
		/// </summary>
		/// <param name="p_strPlugins">The list of plugins from which to remove non-existent and ghosted plugins.</param>
		/// <returns>The given list of plugins, with all non-existent and ghosted plugins removed.</returns>
		private string[] RemoveNonExistentPlugins(string[] p_strPlugins)
		{
			List<string> lstRealPlugins = new List<string>();
			if (p_strPlugins != null)
			{
				foreach (string strPlugin in p_strPlugins)
					if (File.Exists(strPlugin))
						lstRealPlugins.Add(strPlugin);
			}
			return lstRealPlugins.ToArray();
		}

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		public string[] GetActivePlugins()
		{
			int intRepeat = 0;
			bool? booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);
			int intPlugins = 0;
			int intDisabled = 0;

			while (booReady == false)
			{
				Thread.Sleep(500);
				if (intRepeat++ >= 20)
					break;
				booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);
			}

			if (booReady == true)
			{
				List<string> lstActivePlugins = new List<string>();

				if (Fallout4PluginManagement)
				{
					lstActivePlugins.AddRange(GameMode.OrderedCriticalPluginNames);
					lstActivePlugins.AddRange(GameMode.OrderedOfficialPluginNames);
				}

				try
				{
					if (File.Exists(PluginsFilePath))
					{
						string[] strFile = WriteSafeReadAllLines(PluginsFilePath);

						foreach (string line in strFile)
						{
							if (!string.IsNullOrWhiteSpace(line))
							{
								if (line.StartsWith("#"))
									continue;

								if (SingleFileManagement)
								{
									intPlugins++;
									if (line.StartsWith("*"))
									{
										string strPlugin = line.Substring(1);
										if (Fallout4PluginManagement)
											if (GameMode.OrderedOfficialPluginNames.Contains(strPlugin, StringComparer.InvariantCultureIgnoreCase))
												continue;
										if (m_rgxPluginFile.IsMatch(strPlugin))
											lstActivePlugins.Add(AddPluginDirectory(strPlugin));
									}
									else if (m_rgxPluginFile.IsMatch(line))
										intDisabled++;
								}
								else
									if (m_rgxPluginFile.IsMatch(line))
									lstActivePlugins.Add(AddPluginDirectory(line));
							}
						}
					}

					if (SingleFileManagement)
						if (intPlugins == intDisabled)
							ObsoleteConfigFiles = true;

					m_lstActivePlugins = lstActivePlugins;
					LastValidActiveList = lstActivePlugins;

					if (LastValidActiveList.Count > 0)
						return RemoveNonExistentPlugins(lstActivePlugins.ToArray());
				}
				catch { }
			}
			else
			{
				string[] strActivePlugins;
				if (LastValidActiveList.Count > 0)
				{
					strActivePlugins = RemoveNonExistentPlugins(LastValidActiveList.ToArray());
					if (booReady == null)
						SetActivePlugins(strActivePlugins);
				}
				else
				{
					strActivePlugins = RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray());
					if (booReady == null)
						SetActivePlugins(strActivePlugins);
				}

				if (strActivePlugins.Count() > 0)
					return strActivePlugins;
			}

			return GameMode.OrderedCriticalPluginNames;
		}

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		private List<string> GetActiveList()
		{
			int intRepeat = 0;
			bool? booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);

			while (booReady == false)
			{
				Thread.Sleep(500);
				if (intRepeat++ >= 20)
					break;
				booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);
			}

			if (booReady == true)
			{
				List<string> lstActivePlugins = new List<string>();

				if (Fallout4PluginManagement)
				{
					lstActivePlugins.AddRange(GameMode.OrderedCriticalPluginNames);
					lstActivePlugins.AddRange(GameMode.OrderedOfficialPluginNames);
				}

				try
				{
					if (File.Exists(PluginsFilePath))
					{
						string[] strFile = WriteSafeReadAllLines(PluginsFilePath);

						foreach (string line in strFile)
						{
							if (!string.IsNullOrWhiteSpace(line))
							{
								if (line.StartsWith("#"))
									continue;

								if (SingleFileManagement)
								{
									if (line.StartsWith("*"))
									{
										string strPlugin = line.Substring(1);
										if (Fallout4PluginManagement)
											if (GameMode.OrderedOfficialPluginNames.Contains(strPlugin, StringComparer.InvariantCultureIgnoreCase))
												continue;
										if (m_rgxPluginFile.IsMatch(strPlugin))
											lstActivePlugins.Add(AddPluginDirectory(strPlugin));
									}
								}
								else
									if (m_rgxPluginFile.IsMatch(line))
									lstActivePlugins.Add(AddPluginDirectory(line));
							}
						}
					}

					return lstActivePlugins;
				}
				catch { }
			}

			if (LastValidActiveList.Count > 0)
				return LastValidActiveList;
			else
				return RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray()).ToList();
		}

		/// <summary>
		/// Sets the list of active plugins.
		/// </summary>
		/// <param name="p_strActivePlugins">The list of plugins to set as active.</param>
		public void SetActivePlugins(string[] p_strActivePlugins)
		{
			LastValidActiveList = p_strActivePlugins.ToList();
			SetActivePluginsTask(p_strActivePlugins);
		}

		/// <summary>
		/// Sets the list of active plugins.
		/// </summary>
		/// <param name="p_strActivePlugins">The list of plugins to set as active.</param>
		private void SetActivePluginsTask(string[] p_strActivePlugins)
		{
			string[] strActivePluginNames;

			if ((p_strActivePlugins == null) || (p_strActivePlugins.Length == 0))
				return;
			else
				strActivePluginNames = StripPluginDirectory(p_strActivePlugins);

			if (SingleFileManagement)
			{
				string[] strOrderedPluginNames;
				int offset = 0;
				string[] strPlugins;

				if (Fallout4PluginManagement)
				{
					if (IgnoreOfficialPlugins)
						strPlugins = StripPluginDirectory((LastValidLoadOrder.Except(GameMode.OrderedCriticalPluginNames).Except(GameMode.OrderedOfficialPluginNames)).ToArray());
					else
						strPlugins = StripPluginDirectory((LastValidLoadOrder.Except(GameMode.OrderedCriticalPluginNames)).ToArray());

					offset = 2;
					strOrderedPluginNames = new string[(strPlugins.Count() + offset)];
					strOrderedPluginNames[0] = "# This file is used by the game to keep track of your downloaded content.";
					strOrderedPluginNames[1] = "# Please do not modify this file.";
				}
				else
				{
					strPlugins = StripPluginDirectory(LastValidLoadOrder.ToArray());
					strOrderedPluginNames = new string[strPlugins.Count()];
				}

				for (int i = 0; i < strPlugins.Count(); i++)
				{
					string strPlugin = strPlugins[i];
					if (strActivePluginNames.Contains(strPlugin))
						strOrderedPluginNames[(i + offset)] = "*" + strPlugin;
					else
						strOrderedPluginNames[(i + offset)] = strPlugin;
				}

				strActivePluginNames = strOrderedPluginNames;
			}

			WriteLoadOrder(PluginsFilePath, strActivePluginNames, ForcedReadOnly);
			m_lstActivePlugins = strActivePluginNames.ToList<string>();
		}

		/// <summary>
		/// Gets the list of plugin, sorted by load order.
		/// </summary>
		/// <returns>The list of plugins, sorted by load order.</returns>
		public string[] GetLoadOrder()
		{
			if (TimestampOrder)
				return GetTimestampLoadOrder();
			else
				return GetSortedListLoadOrder();
		}

		/// <summary>
		/// Gets the list of plugin, sorted by load order.
		/// </summary>
		/// <returns>The list of plugins, sorted by load order.</returns>
		private string[] GetTimestampLoadOrder()
		{
			List<string> lstOrderedPlugins = new List<string>();
			DirectoryInfo diPluginFolder = new DirectoryInfo(GameMode.PluginDirectory);

			try
			{
				if (diPluginFolder.Exists)
				{
					lstOrderedPlugins = diPluginFolder
										.EnumerateFiles()
										.OrderBy(file => file.LastWriteTime)
										.Where(file => file.Extension.Equals(".esp", StringComparison.InvariantCultureIgnoreCase) || file.Extension.Equals(".esm", StringComparison.InvariantCultureIgnoreCase))
										.Select(file => file.FullName)
										.ToList();
				}
			}
			catch { }
			finally
			{
				AddMissingElements(lstOrderedPlugins, true);
				LastValidLoadOrder = lstOrderedPlugins;
			}

			if (lstOrderedPlugins.Count > 0)
				return RemoveNonExistentPlugins(lstOrderedPlugins.ToArray());
			else if (LastValidLoadOrder.Count > 0)
				return RemoveNonExistentPlugins(LastValidLoadOrder.ToArray());
			else
				return RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray());
		}

		/// <summary>
		/// Gets the list of plugin from a file, sorted by load order.
		/// </summary>
		/// <returns>The list of plugins from a file, sorted by load order.</returns>
		private string[] GetSortedListLoadOrder()
		{
			int intRepeat = 0;
			bool? booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);

			while (booReady == false)
			{
				Thread.Sleep(500);
				if (intRepeat++ >= 20)
					break;
				booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);
			}

			if (booReady != false)
			{
				List<string> lstOrderedPlugins = new List<string>();

				if (Fallout4PluginManagement)
				{
					lstOrderedPlugins.AddRange(GameMode.OrderedCriticalPluginNames);
					lstOrderedPlugins.AddRange(GameMode.OrderedOfficialPluginNames);
				}

				try
				{
					if (File.Exists(LoadOrderFilePath))
					{
						string[] strFile = WriteSafeReadAllLines(LoadOrderFilePath);

						foreach (string line in strFile)
						{
							if (!string.IsNullOrWhiteSpace(line))
							{
								if (line.StartsWith("#"))
									continue;

								if (SingleFileManagement)
								{
									string strPlugin = line.StartsWith("*") ? line.Substring(1) : line;
									if (Fallout4PluginManagement)
										if (GameMode.OrderedOfficialPluginNames.Contains(strPlugin, StringComparer.InvariantCultureIgnoreCase))
											continue;
									if (m_rgxPluginFile.IsMatch(strPlugin))
										lstOrderedPlugins.Add(AddPluginDirectory(strPlugin));
								}
								else
									if (m_rgxPluginFile.IsMatch(line))
									lstOrderedPlugins.Add(AddPluginDirectory(line));
							}
						}
					}
				}
				catch { }
				finally
				{
					AddMissingElements(lstOrderedPlugins, false);
					LastValidLoadOrder = lstOrderedPlugins;
				}

				if (lstOrderedPlugins.Count > 0)
				{
					if (booReady == null)
						SetSortedListLoadOrder(RemoveNonExistentPlugins(lstOrderedPlugins.ToArray()));
					return RemoveNonExistentPlugins(lstOrderedPlugins.ToArray());
				}
			}

			if (LastValidLoadOrder.Count > 0)
				return RemoveNonExistentPlugins(LastValidLoadOrder.ToArray());
			else
				return RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray());
		}

		/// <summary>
		/// Adds plugins missing from the loadorder file to the ordered list.
		/// </summary>
		private void AddMissingElements(IList<string> p_lstOrdered, bool p_booPluginFileOnly)
		{
			List<string> lstActivePlugins = GetActiveList();

			if (lstActivePlugins.Count > 0)
			{
				List<string> lstMissing = lstActivePlugins.Except(p_lstOrdered, StringComparer.InvariantCultureIgnoreCase).ToList();

				if ((lstMissing != null) && (lstMissing.Count > 0))
				{
					foreach (string missingPlugin in lstMissing)
					{
						int intIndex = lstActivePlugins.IndexOf(missingPlugin);

						if (intIndex > 0)
						{
							bool booFound = false;
							int intPrevious = intIndex - 1;
							while (!booFound)
							{
								string strPrevious = lstActivePlugins[intPrevious];
								int intOrdered = p_lstOrdered.IndexOf(strPrevious);

								if (intOrdered >= 0)
								{
									booFound = true;
									p_lstOrdered.Insert(intOrdered + 1, missingPlugin);
								}

								if (--intPrevious < 0)
									break;
							}

							if (!booFound)
								p_lstOrdered.Add(missingPlugin);
						}
					}
				}
			}

			List<string> lstLoosePlugins = new List<string>();
			DirectoryInfo diPluginFolder = new DirectoryInfo(GameMode.PluginDirectory);

			try
			{
				if ((diPluginFolder.Exists) && !p_booPluginFileOnly)
				{
					lstLoosePlugins = diPluginFolder
										.EnumerateFiles()
										.OrderBy(file => file.LastWriteTime)
										.Where(file => file.Extension.Equals(".esp", StringComparison.InvariantCultureIgnoreCase) || file.Extension.Equals(".esm", StringComparison.InvariantCultureIgnoreCase))
										.Select(file => file.FullName)
										.ToList();

					if ((lstLoosePlugins != null) && (lstLoosePlugins.Count > 0))
					{
						List<string> lstMissing = lstLoosePlugins.Except(p_lstOrdered, StringComparer.InvariantCultureIgnoreCase).ToList();

						foreach (string plugin in lstMissing)
							p_lstOrdered.Add((plugin));
					}
				}
			}
			catch { }
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		/// </remarks>
		public void SetLoadOrder(string[] p_strPlugins)
		{
			string[] strOrderedPluginNames;
			List<string> lstActiveList = new List<string>();

			if ((LastValidActiveList != null) && (LastValidActiveList.Count() > 0))
				lstActiveList = new List<string>(StripPluginDirectory(LastValidActiveList.ToArray()));

			if ((p_strPlugins == null) || (p_strPlugins.Length == 0))
				return;
			else
			{
				LastValidLoadOrder = p_strPlugins.ToList();
				strOrderedPluginNames = p_strPlugins;
			}

			if (TimestampOrder)
			{
				try
				{
					WriteLoadOrderTask wltTask = new WriteLoadOrderTask(String.Empty, strOrderedPluginNames, TimestampOrder, false, m_dtiMasterDate);
					TaskList.Add(wltTask);
				}
				catch { }
			}
			else if (SingleFileManagement)
			{
				string[] strPluginNames;
				int offset = 0;

				if (Fallout4PluginManagement)
				{
					if (IgnoreOfficialPlugins)
						strOrderedPluginNames = StripPluginDirectory((strOrderedPluginNames.Except(GameMode.OrderedCriticalPluginNames).Except(GameMode.OrderedOfficialPluginNames)).ToArray());
					else
						strOrderedPluginNames = StripPluginDirectory((strOrderedPluginNames.Except(GameMode.OrderedCriticalPluginNames)).ToArray());

					offset = 2;
					strPluginNames = new string[(strOrderedPluginNames.Count() + offset)];
					strPluginNames[0] = "# This file is used by the game to keep track of your downloaded content.";
					strPluginNames[1] = "# Please do not modify this file.";
				}
				else
				{
					strOrderedPluginNames = StripPluginDirectory(strOrderedPluginNames);
					strPluginNames = new string[strOrderedPluginNames.Count()];
				}

				for (int i = 0; i < strOrderedPluginNames.Count(); i++)
				{
					string strPlugin = strOrderedPluginNames[i];
					if (lstActiveList.Contains(strPlugin, StringComparer.InvariantCultureIgnoreCase))
						strPluginNames[(i + offset)] = "*" + strPlugin;
					else
						strPluginNames[(i + offset)] = strPlugin;
				}

				SetSortedListLoadOrder(strPluginNames);
			}
			else
			{
				strOrderedPluginNames = StripPluginDirectory(strOrderedPluginNames);
				SetSortedListLoadOrder(strOrderedPluginNames);
			}

			if (!TimestampOrder && !SingleFileManagement && ((m_lstActivePlugins != null) && (m_lstActivePlugins.Count > 0)))
			{
				string[] strOrderedActivePluginNames = strOrderedPluginNames.Intersect(StripPluginDirectory(m_lstActivePlugins.ToArray()), StringComparer.InvariantCultureIgnoreCase).ToArray();
				if ((strOrderedActivePluginNames != null) && (strOrderedActivePluginNames.Length > 0))
					SetActivePluginsTask(strOrderedActivePluginNames);
			}
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		private void SetTimestampLoadOrder(string[] p_strPlugins)
		{
			for (int i = 0; i < p_strPlugins.Length; i++)
			{
				string strPluginFile = p_strPlugins[i];
				if (!String.IsNullOrWhiteSpace(strPluginFile) && (File.Exists(strPluginFile)))
					File.SetLastWriteTime(strPluginFile, m_dtiMasterDate.AddMinutes(i));
			}
		}

		/// <summary>
		/// Sets the load order of the plugins in a file.
		/// </summary>
		/// <remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		private void SetSortedListLoadOrder(string[] p_strPlugins)
		{
			WriteLoadOrder(LoadOrderFilePath, p_strPlugins, false);
		}

		/// <summary>
		/// Determines if the specified plugin is active.
		/// </summary>
		/// <param name="p_strPlugin">The plugins whose active state is to be determined.</param>
		/// <returns><c>true</c> if the specfified plugin is active;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginActive(string p_strPlugin)
		{
			string strPlugin = StripPluginDirectory(p_strPlugin);

			if (Fallout4PluginManagement)
			{
				if (OrderedCriticalPluginNames.Contains(strPlugin, StringComparer.CurrentCultureIgnoreCase))
					return true;
				else if (OrderedOfficialPluginNames != null)
					if (OrderedOfficialPluginNames.Contains(strPlugin, StringComparer.CurrentCultureIgnoreCase))
						return true;
			}

			if (File.Exists(PluginsFilePath))
			{
				int intRepeat = 0;
				bool? booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);

				while (booReady == false)
				{
					Thread.Sleep(500);
					if (intRepeat++ >= 20)
						break;
					booReady = IsFileReady(PluginsFilePath, ForcedReadOnly, true);
				}

				if (booReady == true)
				{
					foreach (string line in WriteSafeReadAllLines(PluginsFilePath))
					{
						if (!string.IsNullOrWhiteSpace(line))
						{
							if (line.StartsWith("#"))
								continue;

							if (SingleFileManagement)
							{
								string strCurrent = line;
								if (strCurrent.StartsWith("*"))
								{
									strCurrent = strCurrent.Substring(1);
									if (m_rgxPluginFile.IsMatch(strCurrent))
										if (strCurrent.Equals(strPlugin, StringComparison.InvariantCultureIgnoreCase))
											return true;
								}
							}
							else
								if (m_rgxPluginFile.IsMatch(line))
									if (line.Equals(strPlugin, StringComparison.InvariantCultureIgnoreCase))
										return true;
						}
					}
				}
				else
					return true;
			}

			return false;
		}

		private string[] WriteSafeReadAllLines(string path)
		{
			using (var csv = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var sr = new StreamReader(csv))
			{
				List<string> file = new List<string>();
				while (!sr.EndOfStream)
				{
					file.Add(sr.ReadLine());
				}

				return file.ToArray();
			}
		}

		/// <summary>
		/// Sorts the user's mods
		/// </summary>
		/// <param name="p_booTrialOnly">Whether the sort should actually be performed, or just previewed.</param>
		/// <returns>The list of plugins, sorted by load order.</returns>
		public string[] SortMods(bool p_booTrialOnly)
		{
			// Not implemented here, this will be handled by the LOOT API
			return null;
		}

		/// <summary>
		/// Gets the load index of the specified plugin.
		/// </summary>
		/// <param name="p_strPlugin">The plugin whose load order is to be retrieved.</param>
		/// <returns>The load index of the specified plugin.</returns>
		public Int32 GetPluginLoadOrder(string p_strPlugin)
		{
			throw new NotImplementedException();
			UInt32 uintIndex = 0;
			//notimplemented
			return Convert.ToInt32(uintIndex);
		}

		/// <summary>
		/// Sets the load order of the specified plugin.
		/// </summary>
		/// <remarks>
		/// Sets the load order of the specified plugin, removing it from its current position 
		/// if it has one. The first position in the load order is 0. If the index specified is
		/// greater than the number of plugins in the load order, the plugin will be inserted at
		/// the end of the load order.
		/// </remarks>
		/// <param name="p_strPlugin">The plugin whose load order is to be set.</param>
		/// <param name="p_intIndex">The load index at which to place the specified plugin.</param>
		public void SetPluginLoadOrder(string p_strPlugin, Int32 p_intIndex)
		{
			throw new NotImplementedException();
			UInt32 uintIndex = 0;
			//notimplemented
			Convert.ToInt32(uintIndex);
		}

		/// <summary>
		/// Sets the active status of the specified plugin.
		/// </summary>
		/// <param name="p_strPlugin">The plugin whose active status is to be set.</param>
		/// <param name="p_booActive">Whether the specified plugin should be made active or inactive.</param>
		public void SetPluginActive(string p_strPlugin, bool p_booActive)
		{
			throw new NotImplementedException();
			UInt32 uintIndex = 0;
			//notimplemented
			Convert.ToInt32(uintIndex);
		}

		/// <summary>
		/// Gets the plugin at the specified load index.
		/// </summary>
		/// <param name="p_intIndex">The load index of the plugin to retrieve.</param>
		/// <returns>The name of the plugin at the specified index.</returns>
		public string GetIndexedPlugin(Int32 p_intIndex)
		{
			throw new NotImplementedException();
			//notimplemented
			return String.Empty;
		}

		#endregion

		#region Task Management

		/// <summary>
		/// Handles the <see cref="INotifyCollectionChanged.CollectionChanged"/> event of the view model's
		/// installed mod list.
		/// </summary>
		/// <remarks>
		/// This updates the list of mods to refelct changes to the installed mod list.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="NotifyCollectionChangedEventArgs"/> describing the event arguments.</param>
		private void TaskList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
				case NotifyCollectionChangedAction.Remove:
					lock (m_objLock)
					{
						if ((RunningTask == null) || ((RunningTask.Status != TaskStatus.Queued) && (RunningTask.Status != TaskStatus.Running) && (RunningTask.Status != TaskStatus.Retrying)))
						{
							if ((TaskList != null) && (TaskList.Count > 0))
							{
								lock (TaskList)
								{
									WriteLoadOrderTask NextTask = TaskList.FirstOrDefault();
									RunningTask = NextTask;
									RunningTask.TaskEnded += new EventHandler<TaskEndedEventArgs>(RunningTask_TaskEnded);
									NextTask.Update();
								}
							}
						}
					}
					break;
			}
		}

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
			if (RunningTask != null)
			{
				lock (m_objLock)
				{
					if (RunningTask != null)
					{

						if ((e.ReturnValue != null)  && (e.ReturnValue.GetType() == typeof (KeyValuePair<string, string>)))
						{
							KeyValuePair<string, string> kvpSHA = (KeyValuePair<string, string>)e.ReturnValue;

							if (kvpSHA.Key != null)
								if (dctFileHashes.ContainsKey(kvpSHA.Key.ToLowerInvariant()))
									dctFileHashes[kvpSHA.Key.ToLowerInvariant()] = kvpSHA.Value;
						}

						RunningTask.TaskEnded -= RunningTask_TaskEnded;
						int intPosition = TaskList.IndexOf((WriteLoadOrderTask)RunningTask);
						RunningTask = null;
						TaskList.RemoveAt(intPosition);
					}
				}
			}
		}

		#endregion

		#region Utility Methods

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		public void UpdateMasterlist()
		{
			// Not implemented here, this will be handled by the LOOT API
		}

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		/// <returns><c>true</c> if an update to the masterlist is available;
		/// <c>false</c> otherwise.</returns>
		public bool MasterlistHasUpdate()
		{
			// Not implemented here, this will be handled by the LOOT API
			return false;
		}

		/// <summary>
		/// Handles write operations to the load order file.
		/// </summary>
		private void WriteLoadOrder(string p_strFilePath, string[] p_strPlugins, bool p_booReadOnly)
		{
			WriteLoadOrderTask wltTask = new WriteLoadOrderTask(p_strFilePath, p_strPlugins, false, p_booReadOnly, m_dtiMasterDate);
			TaskList.Add(wltTask);
		}

		/// <summary>
		/// Checks whether the file to write to is currently free for use.
		/// </summary>
		private static bool? IsFileReady(string p_strFilePath, bool p_booRemoveReadOnlyFlag, bool p_booReadOnly = false)
		{
			try
			{
				using (FileStream inputStream = File.Open(p_strFilePath, FileMode.Open, p_booReadOnly ? FileAccess.Read : FileAccess.ReadWrite, p_booReadOnly ? FileShare.Read : FileShare.ReadWrite))
				{
					if (inputStream.Length > 0)
						return true;
					else if (inputStream.Length == 0)
						return null;
				}
			}
			catch
			{
				if (p_booRemoveReadOnlyFlag)
					if (IsFileReadOnly(p_strFilePath))
						SetFileReadAccess(p_strFilePath, false);
			}
			finally
			{
			}

			return false;
		}

		/// <summary>
		/// Returns whether a file is read-only.
		/// </summary>
		private static bool IsFileReadOnly(string p_strFileName)
		{
			// Create a new FileInfo object.
			FileInfo fiInfo = new FileInfo(p_strFileName);

			// Return the IsReadOnly property value.
			return fiInfo.IsReadOnly;
		}

		/// <summary>
		/// Sets the read-only value of a file.
		/// </summary>
		private static void SetFileReadAccess(string p_strFileName, bool p_booSetReadOnly)
		{
			try
			{
				// Create a new FileInfo object.
				FileInfo fInfo = new FileInfo(p_strFileName);

				// Set the IsReadOnly property.
				fInfo.IsReadOnly = p_booSetReadOnly;
			}
			catch { }
		}

		/// <summary>
		/// Gets whether the plugin is a master file.
		/// </summary>
		/// <param name="p_strPlugin">The plugin for which it is to be determined if the file is a plugin.</param>
		/// <returns><c>true</c> if the given plugin is a master file;
		/// <c>false</c> otherwise.</returns>
		public bool IsMaster(string p_strPlugin)
		{
			return false;
			//notimplemented
		}

		#endregion

		/// <summary>
		/// Sets an external task to monitor that could interact with the load order.
		/// </summary>
		/// <param name="p_tskTask">The task to monitor.</param>
		public void MonitorExternalTask(IBackgroundTask p_tskTask)
		{
			int intRepeat = 0;
			bool booLocked = false;

			while ((ExternalTask != null) && (ExternalTask.Status != TaskStatus.Running))
			{
				Thread.Sleep(500);
				if (intRepeat++ > 20)
				{
					booLocked = true;
					break;
				}
			}

			if (!booLocked)
			{
				ExternalTask = p_tskTask;
				ExternalTask.TaskEnded += ExternalTask_TaskEnded;
				ExternalTask.Resume();
			}
		}

		/// <summary>
		/// Handles the <see cref="IBackgroundTask.TaskEnded"/> event of a task set.
		/// </summary>
		/// <remarks>
		/// This displays the confirmation message.
		/// </remarks>
		/// <param name="sender">The object that raised the event.</param>
		/// <param name="e">A <see cref="TaskSetCompletedEventArgs"/> describing the event arguments.</param>
		private void ExternalTask_TaskEnded(object sender, TaskEndedEventArgs e)
		{
			if (ExternalTask != null)
			{
				lock (m_objLock)
				{
					if (ExternalTask != null)
					{
						ExternalTask.TaskEnded -= ExternalTask_TaskEnded;
						ExternalTask = null;
					}
				}
			}
		}
	}
}
