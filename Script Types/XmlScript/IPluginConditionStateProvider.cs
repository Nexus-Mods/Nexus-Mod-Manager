namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Provides plugin registration and activation state for XML scripted-installer conditions.
	/// </summary>
	public interface IPluginConditionStateProvider
	{
		/// <summary>
		/// Determines whether the specified script-visible plugin is registered in the effective condition state.
		/// </summary>
		/// <param name="p_strPluginPath">The plugin path as declared by the XML script.</param>
		/// <returns><c>true</c> when the plugin is registered; otherwise, <c>false</c>.</returns>
		bool IsPluginRegistered(string p_strPluginPath);

		/// <summary>
		/// Determines whether the specified script-visible plugin is active in the effective condition state.
		/// </summary>
		/// <param name="p_strPluginPath">The plugin path as declared by the XML script.</param>
		/// <returns><c>true</c> when the plugin is active; otherwise, <c>false</c>.</returns>
		bool IsPluginActive(string p_strPluginPath);
	}
}
