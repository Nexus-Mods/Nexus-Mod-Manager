using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// Describes the properties and methods of a plugin factory.
	/// </summary>
	/// <remarks>
	/// A plugin factory creates <see cref="Plugin"/>s from files.
	/// </remarks>
	public interface IPluginFactory
	{
		/// <summary>
		/// Creates a plugin of the appropriate type from the specified file.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin file.</param>
		/// <returns>A plugin of the appropriate type from the specified file, if the type of the plugin
		/// can be determined; <c>null</c> otherwise.</returns>
		Plugin CreatePlugin(string p_strPluginPath);

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		bool IsActivatiblePluginFile(string p_strPath);
	}
}
