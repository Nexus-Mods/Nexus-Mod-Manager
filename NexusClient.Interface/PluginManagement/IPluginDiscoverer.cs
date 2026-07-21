using System.Collections.Generic;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// Describes the properties and methods of a plugin discoverer.
	/// </summary>
	/// <remarks>
	/// A plugin discoverer finds the current game mode's <see cref="Nexus.Client.Plugins.Plugin"/>s on the file system.
	/// </remarks>
	public interface IPluginDiscoverer
	{
		/// <summary>
		/// Returns the list of plugin files for the current game mode.
		/// </summary>
		/// <returns>The list of plugin files for the current game mode.</returns>
		IEnumerable<string> FindPlugins();

		/// <summary>
		/// Determines if the given path points at a plugin.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the file to be idecntified.</param>
		/// <returns><c>true</c> if the given path represents a plugin file;
		/// <c>false</c> otherwise.</returns>
		bool IsPlugin(string p_strPluginPath);
	}
}
