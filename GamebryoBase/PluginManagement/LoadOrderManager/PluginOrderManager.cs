using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
		private static readonly Object m_objLock = new Object();
		private Regex m_rgxPluginFile = new Regex(@"(?i)^.+\.es[mp]$");
		private List<string> m_lstActivePlugins = new List<string>();
		private DateTime m_dtiMasterDate = DateTime.Now;
		private ThreadSafeObservableList<WriteLoadOrderTask> TaskList = new ThreadSafeObservableList<WriteLoadOrderTask>();
		private IBackgroundTask RunningTask = null;
		private IBackgroundTask ExternalTask = null;

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
		/// Gets the path to the file containing the list of active plugins.
		/// </summary>
		/// <value>The path to the file containing the list of active plugins.</value>
		protected string PluginsFilePath { get; private set; }

		/// <summary>
		/// Gets the path to the file containing the sorted list of plugins.
		/// </summary>
		/// <value>the path to the file containing the sorted list of plugins.</value>
		protected string LoadOrderFilePath { get; private set; }

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
			switch (GameMode.ModeId)
			{
				case "Oblivion":
					TimestampOrder = true;
					break;
				case "Fallout3":
					TimestampOrder = true;
					break;
				case "FalloutNV":
					TimestampOrder = true;
					break;
				case "Skyrim":
					TimestampOrder = false;
					break;
				default:
					throw new NotImplementedException(String.Format("Unsupported game: {0} ({1})", GameMode.Name, GameMode.ModeId));
			}

			string strLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string strGameModeLocalAppData = Path.Combine(strLocalAppData, GameMode.ModeId);
			LastValidActiveList = RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray()).ToList();
			LastValidLoadOrder = RemoveNonExistentPlugins(GameMode.OrderedCriticalPluginNames.Concat(GameMode.OrderedOfficialPluginNames).ToArray()).ToList();
			TaskList.CollectionChanged += new NotifyCollectionChangedEventHandler(TaskList_CollectionChanged);

			if (!TimestampOrder)
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
			FileWatcher.Path = p_strGameModeLocal;
			FileWatcher.IncludeSubdirectories = false;
			FileWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
			FileWatcher.Filter = "*.txt";
			FileWatcher.Changed += new FileSystemEventHandler(FileWatcherOnChangedTxt);
			FileWatcher.EnableRaisingEvents = true;
			FileWatchers.Add(FileWatcher);

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

			string strFile = e.Name ?? String.Empty;

			if (IsExternalInput && !String.IsNullOrWhiteSpace(strFile))
			{
				int intRepeat = 0;
				bool? booReady = false;

				while (booReady == false)
				{
					Thread.Sleep(100);
					if (intRepeat++ >= 20)
						break;
					booReady = IsFileReady(strFile);
				}

				if (strFile.Equals("plugins.txt", StringComparison.InvariantCultureIgnoreCase))
				{
					if (ActivePluginUpdate != null)
						ActivePluginUpdate(GetActivePlugins(), new EventArgs());
				}
				else if (strFile.Equals("loadorder.txt", StringComparison.InvariantCultureIgnoreCase))
				{
					if (LoadOrderUpdate != null)
						LoadOrderUpdate(GetLoadOrder(), new EventArgs());
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
					booReady = IsFileReady(e.FullPath);
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
				bool? booReady = IsFileReady(e.FullPath);

				while (booReady == false)
				{
					Thread.Sleep(100);
					if (intRepeat++ >= 20)
						break;
					booReady = IsFileReady(e.FullPath);
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
				bool? booReady = IsFileReady(e.FullPath);

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

		#endregion

		/// <summary>
		/// Backup the plugins.txt and loadorder.txt files
		/// </summary>
		private void Backup(string p_strDataFolder)
		{
			if (File.Exists(LoadOrderFilePath))
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
			bool? booReady = IsFileReady(PluginsFilePath);

			while (booReady == false)
			{
				Thread.Sleep(500);
				if (intRepeat++ >= 20)
						break;
				booReady = IsFileReady(PluginsFilePath);
			}

			if (booReady == true)
			{
				List<string> lstActivePlugins = new List<string>();

				try
				{
					if (File.Exists(PluginsFilePath))
					{
						foreach (string line in File.ReadLines(PluginsFilePath))
						{
							if (!string.IsNullOrWhiteSpace(line))
								if (m_rgxPluginFile.IsMatch(line))
									lstActivePlugins.Add(AddPluginDirectory(line));
						}
					}

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
			bool? booReady = IsFileReady(PluginsFilePath);

			while (booReady == false)
			{
				Thread.Sleep(500);
				if (intRepeat++ >= 20)
					break;
				booReady = IsFileReady(PluginsFilePath);
			}

			if (booReady == true)
			{
				List<string> lstActivePlugins = new List<string>();

				try
				{
					if (File.Exists(PluginsFilePath))
					{
						foreach (string line in File.ReadLines(PluginsFilePath))
						{
							if (!string.IsNullOrWhiteSpace(line))
								if (m_rgxPluginFile.IsMatch(line))
									lstActivePlugins.Add(AddPluginDirectory(line));
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

			WriteLoadOrder(PluginsFilePath, strActivePluginNames);
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
			bool? booReady = IsFileReady(PluginsFilePath);

			while (booReady == false)
			{
				Thread.Sleep(500);
				if (intRepeat++ >= 20)
					break;
				booReady = IsFileReady(PluginsFilePath);
			}

			if (booReady != false)
			{
				List<string> lstOrderedPlugins = new List<string>();

				try
				{
					if (File.Exists(LoadOrderFilePath))
					{
						foreach (string line in File.ReadLines(LoadOrderFilePath))
						{
							if (!string.IsNullOrWhiteSpace(line))
								if (m_rgxPluginFile.IsMatch(line))
									lstOrderedPlugins.Add(AddPluginDirectory(line));
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
			catch{}
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		public void SetLoadOrder(string[] p_strPlugins)
		{
			string[] strOrderedPluginNames;

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
					WriteLoadOrderTask wltTask = new WriteLoadOrderTask(String.Empty, strOrderedPluginNames, TimestampOrder, m_dtiMasterDate);
					TaskList.Add(wltTask);
				}
				catch { }
			}
			else
			{
				strOrderedPluginNames = StripPluginDirectory(strOrderedPluginNames);
				SetSortedListLoadOrder(strOrderedPluginNames);
			}

			if (!TimestampOrder && ((m_lstActivePlugins != null) && (m_lstActivePlugins.Count > 0)))
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
			WriteLoadOrder(LoadOrderFilePath, p_strPlugins);
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

			if (File.Exists(PluginsFilePath))
			{
				foreach (string line in File.ReadLines(PluginsFilePath))
				{
					if (!string.IsNullOrWhiteSpace(line))
						if (m_rgxPluginFile.IsMatch(line))
							if (line.Equals(strPlugin, StringComparison.InvariantCultureIgnoreCase))
								return true;
				}
			}

			return false;
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
						if ((RunningTask == null) || ((RunningTask.Status != BackgroundTasks.TaskStatus.Queued) && (RunningTask.Status != BackgroundTasks.TaskStatus.Running) && (RunningTask.Status != BackgroundTasks.TaskStatus.Retrying)))
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
		private void WriteLoadOrder(string p_strFilePath, string[] p_strPlugins)
		{
			WriteLoadOrderTask wltTask = new WriteLoadOrderTask(p_strFilePath, p_strPlugins, false, m_dtiMasterDate);
			TaskList.Add(wltTask);
		}

		/// <summary>
		/// Checks whether the file to write to is currently free for use.
		/// </summary>
		private static bool? IsFileReady(String p_strFilePath)
		{
			try
			{
				using (FileStream inputStream = File.Open(p_strFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					if (inputStream.Length > 0)
						return true;
					else if (inputStream.Length == 0)
						return null;
				}
			}
			catch { }

			return false;
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

			while ((ExternalTask != null) && (ExternalTask.Status != BackgroundTasks.TaskStatus.Running))
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
