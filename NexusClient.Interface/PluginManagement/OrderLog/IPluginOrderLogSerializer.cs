using System.Collections.Generic;
using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement.OrderLog
{
	/// <summary>
	/// Describes the properties and methods of an object that serializes and deserializes
	/// data from a plugin order log permanent store.
	/// </summary>
	public interface IPluginOrderLogSerializer
	{
		/// <summary>
		/// Deserializes the plugin order from the permanent store.
		/// </summary>
		/// <returns>The ordered list of plugins.</returns>
		IEnumerable<string> LoadPluginOrder();

		/// <summary>
		/// Serializes the plugin order to the permanent store.
		/// </summary>
		/// <param name="p_lstOrderedPlugins">The list of ordered plugins.</param>
		void SavePluginOrder(IList<Plugin> p_lstOrderedPlugins);
	}
}
