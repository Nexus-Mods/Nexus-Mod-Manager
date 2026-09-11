using System;

namespace Nexus.Client.ModManagement.Scripting.XmlScript
{
	/// <summary>
	/// Exposes a deferred scripted-installation projection to XML plugin conditions.
	/// </summary>
	internal sealed class ProjectedPluginConditionStateProvider : IPluginConditionStateProvider
	{
		private readonly ScriptedInstallationProjectedState m_spsProjectedState;

		/// <summary>
		/// Initializes a condition-state provider backed by the supplied projected installation state.
		/// </summary>
		/// <param name="p_spsProjectedState">The projected state maintained while the XML installation plan is built.</param>
		public ProjectedPluginConditionStateProvider(ScriptedInstallationProjectedState p_spsProjectedState)
		{
			if (p_spsProjectedState == null)
				throw new ArgumentNullException(nameof(p_spsProjectedState));

			m_spsProjectedState = p_spsProjectedState;
		}

		/// <summary>
		/// Determines whether the specified plugin is registered in the projected installation state.
		/// </summary>
		/// <param name="p_strPluginPath">The plugin path as declared by the XML script.</param>
		/// <returns><c>true</c> when the plugin is registered; otherwise, <c>false</c>.</returns>
		public bool IsPluginRegistered(string p_strPluginPath)
		{
			return m_spsProjectedState.IsPluginRegistered(p_strPluginPath);
		}

		/// <summary>
		/// Determines whether the specified plugin is active in the projected installation state.
		/// </summary>
		/// <param name="p_strPluginPath">The plugin path as declared by the XML script.</param>
		/// <returns><c>true</c> when the plugin is active; otherwise, <c>false</c>.</returns>
		public bool IsPluginActive(string p_strPluginPath)
		{
			return m_spsProjectedState.IsPluginActive(p_strPluginPath);
		}
	}
}
