using System;
using Nexus.Client.BackgroundTasks;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// The interface for the LoadOrder Manager.
	/// </summary>
	/// <remarks>
	/// This exposes the LoadOrder Manager's plugin sorting and activation abilities.
	/// </remarks>
	public interface ILoadOrderManager
	{

		#region Events

		event EventHandler LoadOrderUpdate;
		event EventHandler ActivePluginUpdate;
		event EventHandler ExternalPluginAdded;
		event EventHandler ExternalPluginRemoved;

		#endregion

		#region Properties

		/// <summary>
		/// Gets the path to the masterlist.
		/// </summary>
		/// <value>The path to the masterlist.</value>
		string MasterlistPath { get; }

		/// <summary>
		/// Gets the path to the userlist.
		/// </summary>
		/// <value>The path to the userlist.</value>
		string UserlistPath { get; }

		/// <summary>
		/// Gets whether the current config files may be obsolete.
		/// </summary>
		/// <value>Whether the current config files config files may be obsolete.</value>
		bool ObsoleteConfigFiles { get; }

		#endregion

		#region Masterlist Updating

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		void UpdateMasterlist();

		/// <summary>
		/// Updates the masterlist at the given path.
		/// </summary>
		/// <returns><c>true</c> if an update to the masterlist is available;
		/// <c>false</c> otherwise.</returns>
		bool MasterlistHasUpdate();

		#endregion

		#region Plugin Sorting Functions

		/// <summary>
		/// Sorts the user's mods
		/// </summary>
		/// <param name="p_booTrialOnly">Whether the sort should actually be performed, or just previewed.</param>
		/// <returns>The list of plugins, sorted by load order.</returns>
		string[] SortMods(bool p_booTrialOnly);

		/// <summary>
		/// Gets the list of plugin, sorted by load order.
		/// </summary>
		/// <returns>The list of plugins, sorted by load order.</returns>
		string[] GetLoadOrder();

		/// <summary>
		/// Sets the load order of the plugins.
		/// </summary>
		/// <remarks>
		/// The returned list of sorted plugins will include plugins that were not
		/// included in the specified order list, if plugins exist that weren't included.
		/// The extra plugins will be apeended to the end of the given order.
		/// </remarks>
		/// <param name="p_strPlugins">The list of plugins in the desired order.</param>
		void SetLoadOrder(string[] p_strPlugins);

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		string[] GetActivePlugins();

		/// <summary>
		/// Sets the list of active plugins.
		/// </summary>
		/// <param name="p_strActivePlugins">The list of plugins to set as active.</param>
		void SetActivePlugins(string[] p_strActivePlugins);

		/// <summary>
		/// Gets the load index of the specified plugin.
		/// </summary>
		/// <param name="p_strPlugin">The plugin whose load order is to be retrieved.</param>
		/// <returns>The load index of the specified plugin.</returns>
		Int32 GetPluginLoadOrder(string p_strPlugin);

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
		void SetPluginLoadOrder(string p_strPlugin, Int32 p_intIndex);

		/// <summary>
		/// Gets the plugin at the specified load index.
		/// </summary>
		/// <param name="p_intIndex">The load index of the plugin to retrieve.</param>
		/// <returns>The name of the plugin at the specified index.</returns>
		string GetIndexedPlugin(Int32 p_intIndex);

		/// <summary>
		/// Sets the active status of the specified plugin.
		/// </summary>
		/// <param name="p_strPlugin">The plugin whose active status is to be set.</param>
		/// <param name="p_booActive">Whether the specified plugin should be made active or inactive.</param>
		void SetPluginActive(string p_strPlugin, bool p_booActive);

		/// <summary>
		/// Determines if the specified plugin is active.
		/// </summary>
		/// <param name="p_strPlugin">The plugins whose active state is to be determined.</param>
		/// <returns><c>true</c> if the specfified plugin is active;
		/// <c>false</c> otherwise.</returns>
		bool IsPluginActive(string p_strPlugin);

		#endregion

		#region Utility Methods

		/// <summary>
		/// Gets whether the plugin is a master file.
		/// </summary>
		/// <param name="p_strPlugin">The plugin for which it is to be determined if the file is a plugin.</param>
		/// <returns><c>true</c> if the given plugin is a master file;
		/// <c>false</c> otherwise.</returns>
		bool IsMaster(string p_strPlugin);

		#endregion

		/// <summary>
		/// Sets an external task to monitor that could interact with the load order.
		/// </summary>
		/// <param name="p_tskTask">The task to monitor.</param>
		void MonitorExternalTask(IBackgroundTask p_tskTask);
	}
}
