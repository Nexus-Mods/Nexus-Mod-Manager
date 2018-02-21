using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.Sorter
{
	/// <summary>
	/// The interface for Plugin Sorter functionality.
	/// </summary>
	/// <remarks>
	/// This use LOOT API to expose PluginSorter's sorting and activation abilities.
	/// </remarks>
	public class PluginSorter : IPluginSorter, IDisposable
	{
		#region Native Methods

		
		[DllImport("kernel32.dll", BestFitMapping = true, ThrowOnUnmappableChar = true)]
		private static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPStr)] string fileName);

		[DllImport("Kernel32.dll", EntryPoint = "GetProcAddress", ExactSpelling = true, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
		public extern static IntPtr GetProcAddress(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)]string funcname);

		[DllImport("kernel32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool FreeLibrary(IntPtr hModule);

		#region Native SORTERAPI Methods

		#region Delegates

		#region Error Handling Functions


		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 GetLastErrorDetailsDelegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] out string details);
		
		#endregion

		#region Lifecycle Management Functions
				
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 CreateSorterDbDelegate(ref IntPtr db, UInt32 clientGame, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string dataPath, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string dataLocalPath);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 DestroySorterDbDelegate(IntPtr db);


		#endregion

		#region Database Loading Functions

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 LoadDelegate(IntPtr db, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string masterlistPath, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string userlistPath);
		
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 EvalConditionalsDelegate(IntPtr db, UInt32 languageid);

		
			
		#endregion

		#region Masterlist Updating

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 UpdateMasterlistDelegate(IntPtr db, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string masterlistPath, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string remoteUrl, [MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] string remoteBranch, ref bool updated);
		
		#endregion

		#region Plugin Sorting Functions
		
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 SortPluginsDelegate(IntPtr db, out IntPtr sortedPlugins, out UInt32 numPlugins);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 ApplyLoadOrderDelegate(IntPtr db, IntPtr plugins, UInt32 numPlugins);

		#endregion

		#region Utility Methods
		
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 CleanupDelegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] out string details);

		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 GetBuildIdDelegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] out string details);
		
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 GetDirtyInfoDelegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] out string details);
		
		[UnmanagedFunctionPointer(CallingConvention.StdCall)]
		private delegate UInt32 GetMasterlistRevisionDelegate([MarshalAs(UnmanagedType.CustomMarshaler, MarshalTypeRef = typeof(StringMarshaler), MarshalCookie = "UTF8")] out string details);
		

		
		

		#endregion

		#endregion


		private ApplyLoadOrderDelegate m_dlgApplyLoadOrder = null;
		private CleanupDelegate m_dlgCleanup = null;
		private CreateSorterDbDelegate m_dlgCreateDb = null;
		private DestroySorterDbDelegate m_dlgDestroyDb = null;
		private EvalConditionalsDelegate m_dlgEvalLists = null;
		private GetBuildIdDelegate m_dlgGetBuildId = null;
		private GetDirtyInfoDelegate m_dlgGetDirtyInfo = null;
		private GetLastErrorDetailsDelegate m_dlgGetErrorMessage = null;
		private GetMasterlistRevisionDelegate m_dlgGetMasterlistRevision = null;
		private LoadDelegate m_dlgLoadLists = null;
		private SortPluginsDelegate m_dlgSortPlugins = null;
		private UpdateMasterlistDelegate m_dlgUpdateMasterlist = null;
				
		#endregion

		#endregion

		private const Int32 SORTER_API_OK_NO_UPDATE_NECESSARY = 31;
		private IntPtr m_ptrSorterApi = IntPtr.Zero;
		private IntPtr m_ptrSorterDb = IntPtr.Zero;
		private bool m_booInitialized = false;

		#region Properties
		
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
		/// Gets the path to the masterlist.
		/// </summary>
		/// <value>The path to the masterlist.</value>
		public string MasterlistPath { get; private set; }

		/// <summary>
		/// Gets the path to the userlist.
		/// </summary>
		/// <value>The path to the userlist.</value>
		public string UserlistPath { get; private set; }

		/// <summary>
		/// Gets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		protected FileUtil FileUtility { get; private set; }

		/// <summary>
		/// Gets whether the sorting library successfully initialized.
		/// </summary>
		/// <value>Whether the sorting library successfully initialized.</value>
		public bool Initialized 
		{
			get
			{
				return m_booInitialized;
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
		public PluginSorter(IEnvironmentInfo p_eifEnvironmentInfo, GamebryoGameModeBase p_gmdGameMode, FileUtil p_futFileUtility, string p_strMasterlistPath)
		{
			EnvironmentInfo = p_eifEnvironmentInfo;
			GameMode = p_gmdGameMode;
			FileUtility = p_futFileUtility;

			string strSorterAPIPath = Path.Combine(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "data"), p_eifEnvironmentInfo.Is64BitProcess ? "loot64.dll" : "loot32.dll");

			m_ptrSorterApi = LoadLibrary(strSorterAPIPath);
			if (m_ptrSorterApi == IntPtr.Zero)
				throw new SorterException(String.Format("Could not load BAPI library: {0}", strSorterAPIPath));

			LoadMethods();

			m_ptrSorterDb = CreateSorterDb();

			if (m_ptrSorterDb == IntPtr.Zero)
				m_booInitialized = false;
			else
			{
				MasterlistPath = p_strMasterlistPath;
				string strUserList = Path.Combine(Path.GetDirectoryName(p_strMasterlistPath), "userlist.yaml");
				if (File.Exists(strUserList))
					UserlistPath = strUserList;
				else
					UserlistPath = null;

				if (!String.IsNullOrEmpty(MasterlistPath) && File.Exists(MasterlistPath))
					Load(MasterlistPath, UserlistPath);

				m_booInitialized = true;
			}
		}

		/// <summary>
		/// The finalizer.
		/// </summary>
		/// <remarks>
		/// Disposes unmanaged resources used by SORTER.
		/// </remarks>
		~PluginSorter()
		{
			Dispose(false);
		}

		#endregion

		/// <summary>
		/// Loads the native SORTERAPI methods.
		/// </summary>
		private void LoadMethods()
		{
			m_dlgApplyLoadOrder = (ApplyLoadOrderDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_apply_load_order"), typeof(ApplyLoadOrderDelegate));
			m_dlgCleanup = (CleanupDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_cleanup"), typeof(CleanupDelegate));
			m_dlgCreateDb = (CreateSorterDbDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_create_db"), typeof(CreateSorterDbDelegate));
			m_dlgDestroyDb = (DestroySorterDbDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_destroy_db"), typeof(DestroySorterDbDelegate));
			m_dlgEvalLists = (EvalConditionalsDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_eval_lists"), typeof(EvalConditionalsDelegate));
			m_dlgGetBuildId = (GetBuildIdDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_get_build_id"), typeof(GetBuildIdDelegate));
			m_dlgGetDirtyInfo = (GetDirtyInfoDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_get_dirty_info"), typeof(GetDirtyInfoDelegate));
			m_dlgGetErrorMessage = (GetLastErrorDetailsDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_get_error_message"), typeof(GetLastErrorDetailsDelegate));
			m_dlgGetMasterlistRevision = (GetMasterlistRevisionDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_get_masterlist_revision"), typeof(GetMasterlistRevisionDelegate));
			m_dlgLoadLists = (LoadDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_load_lists"), typeof(LoadDelegate));
			m_dlgSortPlugins = (SortPluginsDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_sort_plugins"), typeof(SortPluginsDelegate));
			m_dlgUpdateMasterlist = (UpdateMasterlistDelegate)Marshal.GetDelegateForFunctionPointer(GetProcAddress(m_ptrSorterApi, "loot_update_masterlist"), typeof(UpdateMasterlistDelegate));
		}

		#region IDisposable Members

		/// <summary>
		/// Disposes of any resources that the sorter allocated.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion

		/// <summary>
		/// Disposes of the unamanged resources need for SORTERAPI.
		/// </summary>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="Dispose()"/> method.</param>
		protected virtual void Dispose(bool p_booDisposing)
		{
			if (m_ptrSorterDb != IntPtr.Zero)
				DestroySorterDb();
			if (m_ptrSorterApi != IntPtr.Zero)
				FreeLibrary(m_ptrSorterApi);
		}

		/// <summary>
		/// Handles the status code returned by the SORTERAPI methods.
		/// </summary>
		/// <param name="p_uintStatusCode">The status code to handle.</param>
		private void HandleStatusCode(UInt32 p_uintStatusCode)
		{
			if (p_uintStatusCode == 0)
				return;

			string strDetails = GetLastErrorDetails();
			switch (p_uintStatusCode)
			{
				case SORTER_API_OK_NO_UPDATE_NECESSARY:
					//SORTER_API_OK_NO_UPDATE_NECESSARY;
					break;
				case 1:
					//loot_error_liblo_error
					throw new SorterException("loot_error_liblo_error: " + strDetails);
				case 2:
					//loot_error_file_write_fail;
					throw new SorterException("loot_error_file_write_fail: " + strDetails);
				case 3:
					//loot_error_parse_fail;
					throw new SorterException("loot_error_parse_fail: " + strDetails);	
				case 4:
					//loot_error_condition_eval_fail;
					throw new SorterException("loot_error_condition_eval_fail: " + strDetails);
				case 5:
					//loot_error_regex_eval_fail;
					throw new SorterException("loot_error_regex_eval_fail: " + strDetails);
				case 6:
					//loot_error_no_mem;
					throw new SorterException("loot_error_no_mem: " + strDetails);
				case 7:
					//loot_error_invalid_args;
					throw new SorterException("loot_error_invalid_args: " + strDetails);
				case 8:
					//loot_error_no_tag_map;
					throw new SorterException("loot_error_no_tag_map: " + strDetails);
				case 9:
					//loot_error_path_not_found
					throw new SorterException("loot_error_path_not_found: " + strDetails);
				case 10:
					//loot_error_no_game_detected;
					throw new SorterException("loot_error_no_game_detected: " + strDetails);
				case 12:
					//loot_error_git_error
					throw new SorterException("loot_error_git_error: " + strDetails);
				case 13:
					//loot_error_windows_error
					throw new SorterException("loot_error_windows_error: " + strDetails);
				case 14:
					//loot_error_sorting_error
					throw new SorterException("loot_error_sorting_error: " + strDetails);				
				default:
					throw new SorterException(String.Format("Unrecognized error value {1}: {0}", strDetails, p_uintStatusCode));
			}
		}
			
		#region Plugin Helpers

		/// <summary>
		/// Marshal the given pointer to an array of plugins.
		/// </summary>
		/// <remarks>
		/// This adjusts the plugin paths to be in the format expected by the  mod manager.
		/// </remarks>
		/// <param name="p_ptrPluginArray">The pointer to the array of plugin names to marshal.</param>
		/// <param name="p_uintLength">the length of the array to marshal.</param>
		/// <returns>The array of plugins names pointed to by the given pointer.</returns>
		protected string[] MarshalPluginArray(IntPtr p_ptrPluginArray, UInt32 p_uintLength, bool p_booKeepRelativePath)
		{
			if (p_ptrPluginArray == IntPtr.Zero)
				return null;
			string[] strPlugins = null;
			using (StringArrayManualMarshaler ammMarshaler = new StringArrayManualMarshaler("UTF8"))
				strPlugins = ammMarshaler.MarshalNativeToManaged(p_ptrPluginArray, Convert.ToInt32(p_uintLength));

			if (!p_booKeepRelativePath)
				for (Int32 i = strPlugins.Length - 1; i >= 0; i--)
					strPlugins[i] = Path.Combine(GameMode.PluginDirectory, strPlugins[i]);
			return strPlugins;
		}

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

		#region Error Handling Functions

		/// <summary>
		/// Gets the details of the last error.
		/// </summary>
		/// <returns>The details of the last error.</returns>
		private string GetLastErrorDetails()
		{
			IntPtr ptrDetails = IntPtr.Zero;
			string strDetails = null;
			UInt32 uintStatus = m_dlgGetErrorMessage(out strDetails);
			HandleStatusCode(uintStatus);
			return strDetails;
		}
		
		#endregion

		#region Lifecycle Management Functions

		/// <summary>
		/// Creates the LOOT DB.
		/// </summary>
		/// <remarks>
		/// Explicitly manage database lifetime. Allows clients to free memory when
		/// they want/need to. This function also checks that
		/// plugins.txt and loadorder.txt (if they both exist) are in sync.
		/// </remarks>
		/// <returns>The created LOOT DB.</returns>
		/// <exception cref="SorterException">Thrown if the LOOT DB could not be created.</exception>
		private IntPtr CreateSorterDb()
		{
			IntPtr ptrSorterDb = IntPtr.Zero;
			UInt32 uintClientGameId = 0;
			switch (GameMode.ModeId)
			{
				case "Oblivion":
					uintClientGameId = 1;
					break;
				case "Fallout3":
					uintClientGameId = 3;
					break;
				case "FalloutNV":
					uintClientGameId = 4;
					break;
				case "Skyrim":
					uintClientGameId = 2;
					break;
				default:
					throw new SorterException(String.Format("Unsupported game: {0} ({1})", GameMode.Name, GameMode.ModeId));
			}

			Backup();

			UInt32 uintStatus = m_dlgCreateDb(ref ptrSorterDb, uintClientGameId, GameMode.InstallationPath, null);

			//if ((uintStatus == 1) && (ptrSorterDb == IntPtr.Zero))
			//{
			//	string strGameModeLocalAppData = Path.Combine(Environment.GetEnvironmentVariable("LocalAppData"), GameMode.ModeId);
			//	string strLoadOrderFilePath = Path.Combine(strGameModeLocalAppData, "loadorder.txt");

			//	if (File.Exists(strLoadOrderFilePath))
			//	{
			//		string strBakFilePath = Path.Combine(strGameModeLocalAppData, "loadorder.nmmbak");
			//		if (File.Exists(strBakFilePath))
			//		{
			//			FileUtil.Move(strBakFilePath, Path.Combine(strGameModeLocalAppData, Path.GetRandomFileName() + ".loadorder.bak"), true);
			//		}

			//		FileUtil.Move(strLoadOrderFilePath, strBakFilePath, true);

			//		uintStatus = m_dlgCreateDb(ref ptrSorterDb, uintClientGameId, GameMode.InstallationPath, null);
			//	}
			//}

			//HandleStatusCode(uintStatus);
			//if (ptrSorterDb == IntPtr.Zero)
			//	throw new SorterException("Could not create LOOT DB.");
			return ptrSorterDb;
		}

		/// <summary>
		/// Backup the plugins.txt and loadorder.txt files
		/// </summary>
		private void Backup()
		{
			string strLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			string strGameModeLocalAppData = Path.Combine(strLocalAppData, GameMode.ModeId);
			string strLoadOrderFilePath = Path.Combine(strGameModeLocalAppData, "loadorder.txt");
			string strPluginsFilePath = Path.Combine(strGameModeLocalAppData, "plugins.txt");

			if (File.Exists(strLoadOrderFilePath))
			{
				string strBakFilePath = Path.Combine(strGameModeLocalAppData, "loadorder.backup.nmm");
				if (!File.Exists(strBakFilePath))
					File.Copy(strLoadOrderFilePath, strBakFilePath, false);
			}

			if (File.Exists(strPluginsFilePath))
			{
				string strBakFilePath = Path.Combine(strGameModeLocalAppData, "plugins.backup.nmm");
				if (!File.Exists(strBakFilePath))
					File.Copy(strPluginsFilePath, strBakFilePath, false);
			}
		}

		/// <summary>
		/// Destroys the LOOT DB.
		/// </summary>
		private void DestroySorterDb()
		{
			m_dlgDestroyDb(m_ptrSorterDb);
		}
		
		#endregion

		#region Database Loading Functions

		/// <summary>
		/// Loads the specified masterlist.
		/// </summary>
		/// <remarks>
		/// Loads the masterlist and userlist from the paths specified.
		/// Can be called multiple times. On error, the database is unchanged.
		/// </remarks>
		/// <param name="p_strMasterlistPath">The path to the masterlist to load.</param>
		/// <param name="p_strUserlistPath">The path to the userlist to load.</param>
		protected void Load(string p_strMasterlistPath, string p_strUserlistPath)
		{
			UInt32 uintStatus = m_dlgLoadLists(m_ptrSorterDb, p_strMasterlistPath, p_strUserlistPath);

			if (uintStatus == 3)
				uintStatus = m_dlgLoadLists(m_ptrSorterDb, p_strMasterlistPath, null);

			if (uintStatus == 3)
				throw new NotSupportedException("The current masterlist is not supported by this version.");

			HandleStatusCode(uintStatus);
		}

		/// <summary>
		/// Evaluates the loaded masterlist.
		/// </summary>
		/// <remarks>
		/// Evaluates all conditional lines and regex mods the loaded masterlist. 
		/// This exists so that Load() doesn't need to be called whenever the mods 
		/// installed are changed. Evaluation does not take place unless this function 
		/// is called. Repeated calls re-evaluate the masterlist from scratch each time, 
		/// ignoring the results of any previous evaluations. Paths are case-sensitive 
		/// if the underlying filesystem is case-sensitive.
		/// </remarks>
		private void EvaluateMasterlist()
		{
			UInt32 uintStatus = m_dlgEvalLists(m_ptrSorterDb, 1);
			HandleStatusCode(uintStatus);
		}
		
		#endregion

		#region Masterlist Updating

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		public void UpdateMasterlist()
		{
			if (MasterlistHasUpdate())
			{
				bool booExist = File.Exists(MasterlistPath);
				bool booUpdated = false;
				UInt32 uintStatus = 0;
				string strDirectory = Path.GetDirectoryName(MasterlistPath);
				if (!Directory.Exists(strDirectory))
					Directory.CreateDirectory(strDirectory);
				try
				{
					string remoteUrl = String.Format("http://github.com/loot/{0}.git", GameMode.ModeId);
					string remoteBranch = "master";

					uintStatus = m_dlgUpdateMasterlist(m_ptrSorterDb, MasterlistPath, remoteUrl, remoteBranch, ref booUpdated);
				}
				catch (SorterException e)
				{
				}
				catch (AccessViolationException)
				{
				}
				HandleStatusCode(uintStatus);

				if (booExist && booUpdated)
					EvaluateMasterlist();
				else if (booUpdated)
					Load(MasterlistPath, UserlistPath);
			}
		}

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		/// <returns><c>true</c> if an update to the masterlist is available;
		/// <c>false</c> otherwise.</returns>
		public bool MasterlistHasUpdate()
		{
			UInt32 uintStatus = 0;
			bool booRequiresUpdate = false;
			string remoteBranch = "master";

			try
			{
				string remoteUrl = String.Format("http://github.com/loot/{0}.git", GameMode.ModeId);
				uintStatus = m_dlgUpdateMasterlist(m_ptrSorterDb, MasterlistPath, remoteUrl, remoteBranch, ref booRequiresUpdate);
			}
			catch (SorterException e)
			{
			}
			catch (AccessViolationException)
			{
			}

			return booRequiresUpdate;
		}
		
		#endregion

		#region Plugin Sorting Functions

		/// <summary>
		/// Sorts the plugins.
		/// </summary>
		/// <param name="p_strPlugins">The plugins list.</param>
		/// <returns>The list of plugins sorted.</returns>
		public string[] SortPlugins(string[] p_strPlugins)
		{
			UInt32 uintStatus = 0;
			string[] strSortedPlugins = p_strPlugins;
			IntPtr ptrPlugins = IntPtr.Zero;
			UInt32 numPlugins;

			if (!String.IsNullOrEmpty(MasterlistPath))
				if (!File.Exists(MasterlistPath))
					UpdateMasterlist();

			uintStatus = m_dlgSortPlugins(m_ptrSorterDb, out ptrPlugins, out numPlugins);

			if (uintStatus == 0)
			{
				UInt32 uintListLength = numPlugins;
				using (StringArrayManualMarshaler ammMarshaler = new StringArrayManualMarshaler("UTF8"))
					uintStatus = m_dlgApplyLoadOrder(m_ptrSorterDb, ammMarshaler.MarshalManagedToNative(MarshalPluginArray(ptrPlugins, uintListLength, true)), uintListLength);

				if (uintStatus == 0)
					return RemoveNonExistentPlugins(MarshalPluginArray(ptrPlugins, uintListLength, false));
			}

			return null;
		}

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// The returned list of sorted plugins will include plugins that were not
		/// included in the specified order list, if plugins exist that weren't included.
		/// The extra plugins will be apeended to the end of the given order.
		/// </remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		public void SetLoadOrder(string[] p_strPlugins)
		{
			string[] strSortedPlugins = p_strPlugins;
			UInt32 uintStatus = 0;
			using (StringArrayManualMarshaler ammMarshaler = new StringArrayManualMarshaler("UTF8"))
				uintStatus = m_dlgApplyLoadOrder(m_ptrSorterDb, ammMarshaler.MarshalManagedToNative(StripPluginDirectory(strSortedPlugins)), Convert.ToUInt32(strSortedPlugins.Length));
			HandleStatusCode(uintStatus);
		}

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

		#endregion
	}
}
