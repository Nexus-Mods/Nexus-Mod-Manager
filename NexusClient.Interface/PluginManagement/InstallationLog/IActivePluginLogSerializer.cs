using System.Collections.Generic;
using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement.InstallationLog
{
	/// <summary>
	/// Describes the properties and methods of an object that serializes and deserializes
	/// data from an active plugin log permanent store.
	/// </summary>
	public interface IActivePluginLogSerializer
	{
		/// <summary>
		/// Deserializes the list of active plugins from the permanent store.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		IEnumerable<string> LoadPluginLog();

		/// <summary>
		/// Serializes the list of active plugins to the permanent store.
		/// </summary>
		/// <param name="p_lstActivePlugins">The list of active plugins.</param>
		void SavePluginLog(IList<Plugin> p_lstActivePlugins);
	}
}
