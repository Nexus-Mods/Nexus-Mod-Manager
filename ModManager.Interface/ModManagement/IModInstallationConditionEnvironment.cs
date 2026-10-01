using System.Collections.Generic;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// Read-only effective environment used while an installer recipe is planned without mutating native state.
	/// </summary>
	/// <remarks>
	/// The contract is deliberately fail-closed: a provider returns <c>false</c> from a Try method when the requested
	/// fact cannot be proven. Callers must not reinterpret an unprovable fact as absence or an inactive plugin.
	/// </remarks>
	public interface IModInstallationConditionEnvironment
	{
		/// <summary>Gets the plugins proven to be registered in the effective environment.</summary>
		IReadOnlyList<string> RegisteredPlugins { get; }

		/// <summary>Gets the plugins proven to be active in the effective environment.</summary>
		IReadOnlyList<string> ActivePlugins { get; }

		/// <summary>Tries to resolve whether a deployment-root-relative file is visible.</summary>
		bool TryGetDataFileExists(ModDeploymentRoot root, string relativePath, out bool exists);

		/// <summary>Tries to resolve the effective text value of one native INI setting.</summary>
		bool TryGetIniString(string settingsFileName, string section, string key, out string value);

		/// <summary>Tries to resolve whether a script-visible plugin is registered.</summary>
		bool TryGetPluginRegistered(string pluginPath, out bool registered);

		/// <summary>Tries to resolve whether a script-visible plugin is active.</summary>
		bool TryGetPluginActive(string pluginPath, out bool active);
	}
}
