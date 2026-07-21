using System.Collections.Generic;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.PluginManagement.OrderLog
{
	/// <summary>
	/// The log that tracks the order of plugins.
	/// </summary>
	public interface IPluginOrderLog
	{
		#region Singleton

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		void Release();

		#endregion

		#region Properties

		/// <summary>
		/// Gets the list of ordered plugins.
		/// </summary>
		/// <value>The list of ordered plugins.</value>
		ReadOnlyObservableList<Plugin> OrderedPlugins { get; }

		#endregion

		#region Plugin Order Management

		/// <summary>
		/// Sets the order of the plugins to the given order.
		/// </summary>
		/// <remarks>
		/// If the given list does not include all registered plugins, then the plugins are ordered in a manner
		/// so as to not displace the positions of the plugins whose order was not specified.
		/// </remarks>
		/// <param name="p_lstOrderedPlugins">The list indicating the desired order of the plugins.</param>
		void SetPluginOrder(IList<Plugin> p_lstOrderedPlugins);

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The full path to the plugin file whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex);

		/// <summary>
		/// Removes the given plugin from the order list.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to remove from the order list.</param>
		void RemovePlugin(Plugin p_plgPlugin);

		#endregion
	}
}
