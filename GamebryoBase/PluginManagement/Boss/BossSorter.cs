using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexus.Client.Util.Threading;
using System.Threading;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.Boss
{
	/// <summary>
	/// A wrapper for the <see cref="BossSorterCore"/> class that makes using it
	/// thread safe.
	/// </summary>
	/// <remarks>
	/// This is required as calling the native BAPI DLLs can only been done
	/// from the thread on which the DLLs were created.
	/// </remarks>
	public class BossSorter : IBossSorter, IDisposable
	{
		private TrackedThread m_thdBoss = null;
		private Queue<KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent>> m_queEvents = null;
		private ManualResetEvent m_mreEvent = null;
		private BossSorterCore m_bssBoss = null;

		#region Properties

		/// <summary>
		/// Gets the path to the masterlist.
		/// </summary>
		/// <value>The path to the masterlist.</value>
		public string MasterlistPath
		{
			get
			{
				return (string)(ExecuteMethod(() => m_bssBoss.MasterlistPath));
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
				return (string)(ExecuteMethod(() => m_bssBoss.UserlistPath));
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
		public BossSorter(IEnvironmentInfo p_eifEnvironmentInfo, GamebryoGameModeBase p_gmdGameMode, FileUtil p_futFileUtility, string p_strMasterlistPath)
		{
			Init(p_eifEnvironmentInfo, p_gmdGameMode, p_futFileUtility, p_strMasterlistPath);
		}

		/// <summary>
		/// Initializes the BOSS API.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		/// <param name="p_gmdGameMode">The game mode for which plugins are being managed.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		protected void Init(IEnvironmentInfo p_eifEnvironmentInfo, GamebryoGameModeBase p_gmdGameMode, FileUtil p_futFileUtility, string p_strMasterlistPath)
		{
			m_queEvents = new Queue<KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent>>();
			m_mreEvent = new ManualResetEvent(false);
			m_thdBoss = new TrackedThread(RunThread);
			m_thdBoss.Thread.IsBackground = false;
			m_thdBoss.Thread.Name = "BAPI";

			object[] objArgs = { p_eifEnvironmentInfo, p_gmdGameMode, p_futFileUtility, p_strMasterlistPath };

			ManualResetEvent mreDoneStart = new ManualResetEvent(false);
			m_queEvents.Enqueue(new KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent>(null, mreDoneStart));
			m_thdBoss.Start(objArgs);
			mreDoneStart.WaitOne();
		}

		#endregion

		/// <summary>
		/// The run method of the thread on which the <see cref="SevenZipExtractor"/> is created.
		/// </summary>
		/// <remarks>
		/// This method creates a <see cref="SevenZipExtractor"/> and then watches for events to execute.
		/// Other methods signal the thread that an action needs to be taken, and this thread executes said
		/// actions.
		/// </remarks>
		protected void RunThread(object p_objArgs)
		{
			object[] objArgs = (object[])p_objArgs;
			IEnvironmentInfo eifEnvironmentInfo = (IEnvironmentInfo)objArgs[0];
			GamebryoGameModeBase gmdGameMode = (GamebryoGameModeBase)objArgs[1];
			FileUtil futFileUtility = (FileUtil)objArgs[2];
			string strMasterlistPath = (string)objArgs[3];

			m_bssBoss = new BossSorterCore(eifEnvironmentInfo, gmdGameMode, futFileUtility, strMasterlistPath);
			try
			{
				KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent> kvpStartEvent = m_queEvents.Dequeue();
				kvpStartEvent.Value.Set();
				while (true)
				{
					m_mreEvent.WaitOne();
					KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent> kvpEvent = m_queEvents.Dequeue();
					if (kvpEvent.Key == null)
						break;
					kvpEvent.Key();
					m_mreEvent.Reset();
					kvpEvent.Value.Set();
				}
			}
			finally
			{
				m_bssBoss.Dispose();
			}
		}

		protected delegate void GenereicVoidMethodDelegate();
		protected delegate object GenereicReturnMethodDelegate();

		/// <summary>
		/// Executes the given void method.
		/// </summary>
		/// <remarks>
		/// This method is used to execute all void method calls the script needs to make.
		/// This allows for centralized error handling.
		/// 
		/// It should be noted that using delegates does engender a very slight performance hit,
		/// but given the nature of this application (more precisely, that this is a single-user
		/// application) there should not be any noticable difference.
		/// </remarks>
		/// <param name="p_gmdMethod">The method to execute.</param>
		/// <see cref="ExecuteMethod(GenereicReturnMethodDelegate p_gmdMethod)"/>
		protected void ExecuteMethod(GenereicVoidMethodDelegate p_gmdMethod)
		{
			ManualResetEvent mreDoneEvent = new ManualResetEvent(false);
			m_queEvents.Enqueue(new KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent>(p_gmdMethod, mreDoneEvent));
			m_mreEvent.Set();
			mreDoneEvent.WaitOne();
		}

		/// <summary>
		/// Executes the given method with a return value.
		/// </summary>
		/// <remarks>
		/// This method is used to execute all method calls that return a value that
		/// the script needs to make. This allows for centralized error handling.
		/// 
		/// It should be noted that using delegates does engender a very slight performance hit,
		/// but given the nature of this application (more precisely, that this is a single-user
		/// application) there should not be any noticable difference.
		/// </remarks>
		/// <param name="p_gmdMethod">The method to execute.</param>
		/// <see cref="ExecuteMethod(GenereicVoidMethodDelegate p_gmdMethod)"/>
		protected object ExecuteMethod(GenereicReturnMethodDelegate p_gmdMethod)
		{
			ManualResetEvent mreDoneEvent = new ManualResetEvent(false);
			object objValue= null;
			m_queEvents.Enqueue(new KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent>(() => { objValue = p_gmdMethod(); }, mreDoneEvent));
			m_mreEvent.Set();
			mreDoneEvent.WaitOne();
			return objValue;
		}

		#region Masterlist Updating

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		public void UpdateMasterlist()
		{
			ExecuteMethod(() => m_bssBoss.UpdateMasterlist());
		}

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		/// <returns><c>true</c> if an update to the masterlist is available;
		/// <c>false</c> otherwise.</returns>
		public bool MasterlistHasUpdate()		
		{
			return (bool)(ExecuteMethod(() => m_bssBoss.MasterlistHasUpdate()));
		}

		#endregion

		#region Plugin Sorting Functions

		/// <summary>
		/// Sorts the user's mods
		/// </summary>
		/// <param name="p_booTrialOnly">Whether the sort should actually be performed, or just previewed.</param>
		/// <returns>The list of plugins, sorted by load order.</returns>
		public string[] SortMods(bool p_booTrialOnly)
		{
			return (string[])(ExecuteMethod(() => m_bssBoss.SortMods(p_booTrialOnly)));
		}

		/// <summary>
		/// Gets the list of plugin, sorted by load order.
		/// </summary>
		/// <returns>The list of plugins, sorted by load order.</returns>
		public string[] GetLoadOrder()
		{
			return (string[])(ExecuteMethod(() => m_bssBoss.GetLoadOrder()));
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
			ExecuteMethod(() => m_bssBoss.SetLoadOrder(p_strPlugins));
		}

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		public string[] GetActivePlugins()
		{
			return (string[])(ExecuteMethod(() => m_bssBoss.GetActivePlugins()));
		}

		/// <summary>
		/// Sets the list of active plugins.
		/// </summary>
		/// <param name="p_strActivePlugins">The list of plugins to set as active.</param>
		public void SetActivePlugins(string[] p_strActivePlugins)
		{
			ExecuteMethod(() => m_bssBoss.SetActivePlugins(p_strActivePlugins));
		}

		/// <summary>
		/// Gets the load index of the specified plugin.
		/// </summary>
		/// <param name="p_strPlugin">The plugin whose load order is to be retrieved.</param>
		/// <returns>The load index of the specified plugin.</returns>
		public Int32 GetPluginLoadOrder(string p_strPlugin)
		{
			return (Int32)(ExecuteMethod(() => m_bssBoss.GetPluginLoadOrder(p_strPlugin)));
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
			ExecuteMethod(() => m_bssBoss.SetPluginLoadOrder(p_strPlugin, p_intIndex));
		}

		/// <summary>
		/// Gets the plugin at the specified load index.
		/// </summary>
		/// <param name="p_intIndex">The load index of the plugin to retrieve.</param>
		/// <returns>The name of the plugin at the specified index.</returns>
		public string GetIndexedPlugin(Int32 p_intIndex)
		{
			return (string)(ExecuteMethod(() => m_bssBoss.GetIndexedPlugin(p_intIndex)));
		}

		/// <summary>
		/// Sets the active status of the specified plugin.
		/// </summary>
		/// <param name="p_strPlugin">The plugin whose active status is to be set.</param>
		/// <param name="p_booActive">Whether the specified plugin should be made active or inactive.</param>
		public void SetPluginActive(string p_strPlugin, bool p_booActive)
		{
			ExecuteMethod(() => m_bssBoss.SetPluginActive(p_strPlugin, p_booActive));
		}

		/// <summary>
		/// Determines if the specified plugin is active.
		/// </summary>
		/// <param name="p_strPlugin">The plugins whose active state is to be determined.</param>
		/// <returns><c>true</c> if the specfified plugin is active;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginActive(string p_strPlugin)
		{
			return (bool)(ExecuteMethod(() => m_bssBoss.IsPluginActive(p_strPlugin)));
		}

		#endregion

		#region IDisposable Members

		/// <summary>
		/// Ensures all used resources are released.
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion

		/// <summary>
		/// Disposes of the unamanged resources needed for BAPI.
		/// </summary>
		/// <remarks>
		/// This terminates the thread upon which the <see cref="BossSorterCore"/> was created.
		/// </remarks>
		/// <param name="p_booDisposing">Whether the method is being called from the <see cref="Dispose()"/> method.</param>
		protected virtual void Dispose(bool p_booDisposing)
		{
			m_queEvents.Enqueue(new KeyValuePair<GenereicVoidMethodDelegate, ManualResetEvent>(null, null));
			m_mreEvent.Set();
			m_thdBoss.Thread.Join();
		}
	}
}
