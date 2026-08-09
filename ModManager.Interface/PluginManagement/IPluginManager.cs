using System;
using System.Collections.Generic;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Plugins;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// Describes the properties and methods of a plugin manager, which encapsulates managing plugins.
	/// </summary>
	public interface IPluginManager
	{
		#region Properties

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		ReadOnlyObservableList<Plugin> ManagedPlugins { get; }

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		ReadOnlyObservableList<Plugin> ActivePlugins { get; }

		/// <summary>
		/// Gets the max allowed number of active plugins.
		/// </summary>
		/// <value>The max allowed number of active plugins (0 if there's no limit).</value>
		Int32  MaxAllowedActivePluginsCount { get; }

		/// <summary>
		/// Gets the current authoritative plugin snapshot.
		/// </summary>
		PluginSnapshot CurrentSnapshot { get; }

		/// <summary>
		/// Gets whether non-critical plugin sorting and dependency restrictions
		/// are disabled.
		/// </summary>
		bool PluginRestrictionsDisabled { get; }

		#endregion

		#region Singleton

		/// <summary>
		/// Releases the manager's hold on physical resources.
		/// </summary>
		void Release();

		#endregion

		#region Plugin Registration

		/// <summary>
		/// Adds the specified plugin to the list of managed plugins.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to add.</param>
		/// <returns><c>true</c> if the specified plugin was added;
		/// <c>false</c> otherwise.</returns>
		bool AddPlugin(string p_strPluginPath);

		/// <summary>
		/// Registers all deployed plugin files before applying their requested
		/// activation state through the authoritative policy pipeline.
		/// </summary>
		/// <param name="p_lstPluginPaths">The deployed plugin paths to integrate.</param>
		void IntegrateDeployedPlugins(IList<string> p_lstPluginPaths);

		/// <summary>
		/// Removes the given plugin from the list of managed plugins.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to remove.</param>
		void RemovePlugin(Plugin p_plgPlugin);

		/// <summary>
		/// Removes the specified plugin from the list of managed plugins.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to remove.</param>
		void RemovePlugin(string p_strPluginPath);

		/// <summary>
		/// Removes multiple plugins from the managed, ordered and active collections as one transaction.
		/// </summary>
		/// <param name="p_lstPluginPaths">The plugin paths to remove.</param>
		void RemovePlugins(IList<string> p_lstPluginPaths);

		/// <summary>
		/// Determines if the specified plugin is registered.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose registration status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is registered;
		/// <c>false</c> otherwise.</returns>
		bool IsPluginRegistered(string p_strPath);

		/// <summary>
		/// Gets the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path of the plugin to retrieve.</param>
		/// <returns>The specified plugin, or <c>null</c> if the plugin is not registered.</returns>
		Plugin GetRegisteredPlugin(string p_strPath);

		#endregion

		#region Plugin Activation/Deactivation

		/// <summary>
		/// Sets the activations status of the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose status is to be set.</param>
		/// <param name="p_booActive">Whether to activate the plugin, or deactivate it.</param>
		void SetPluginActivation(string p_strPath, bool p_booActive);

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to activate.</param>
		void ActivatePlugin(Plugin p_plgPlugin);

		/// <summary>
		/// Activates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to activate.</param>
		void ActivatePlugin(string p_strPath);

		/// <summary>
		/// Deactivates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to deactivate.</param>
		void DeactivatePlugin(Plugin p_plgPlugin);

		/// <summary>
		/// Deactivates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to deactivate.</param>
		void DeactivatePlugin(string p_strPath);

		/// <summary>
		/// Sets multiple plugin activations through the authoritative policy pipeline.
		/// </summary>
		void SetPluginActivation(IList<Plugin> p_lstPlugins, bool p_booActive);

		/// <summary>
		/// Attempts to set multiple plugin activations without introducing new validation errors.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose activation state should be changed.</param>
		/// <param name="p_booActive">Whether the plugins should be active.</param>
		/// <param name="p_lstBlockingDiagnostics">The newly introduced validation errors that blocked the operation.</param>
		/// <returns><c>true</c> if the requested activation state was applied; otherwise, <c>false</c>.</returns>
		bool TrySetPluginActivation(IList<Plugin> p_lstPlugins, bool p_booActive, out IList<PluginValidationDiagnostic> p_lstBlockingDiagnostics);

		/// <summary>
		/// Applies a complete ordered plugin state through the authoritative policy pipeline.
		/// </summary>
		void ApplyPluginState(IList<Plugin> p_lstOrderedPlugins, IList<Plugin> p_lstActivePlugins);

		/// <summary>
		/// Determines if the specified plugin is active.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose active status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is active;
		/// <c>false</c> otherwise.</returns>
		bool IsPluginActive(string p_strPath);

		/// <summary>
		/// Determines if the active state of the given plugin can be changed.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the active state can be changed.</param>
		/// <returns><c>true</c> if the given plugin's active state can be changed;
		/// <c>false</c> otherwise.</returns>
		bool CanChangeActiveState(Plugin p_plgPlugin);

		/// <summary>
		/// Enables or disables non-critical plugin sorting and dependency
		/// restrictions.
		/// </summary>
		/// <param name="p_booDisabled">
		/// Whether restrictions should be disabled.
		/// </param>
		/// <param name="p_psnValidationSnapshot">
		/// The resulting snapshot, or the snapshot that prevented the transition.
		/// </param>
		/// <returns>
		/// True if the requested mode was applied; false otherwise.
		/// </returns>
		bool TrySetPluginRestrictionsDisabled(bool p_booDisabled, out PluginSnapshot p_psnValidationSnapshot);

		#endregion

		#region Plugin Ordering

		/// <summary>
		/// Determines whether the plugin can be reordered.
		/// </summary>
		bool CanChangePluginOrder(Plugin p_plgPlugin);

		/// <summary>
		/// Gets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose load order is to be returned.</param>
		/// <returns>The index of the given plugin, or -1 if the plugin is not being managed.</returns>
		Int32 GetPluginOrderIndex(Plugin p_plgPlugin);

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex);

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
		/// Attempts to set the plugin order without introducing new validation errors.
		/// </summary>
		/// <param name="p_lstOrderedPlugins">The requested plugin order.</param>
		/// <param name="p_lstBlockingDiagnostics">The newly introduced validation errors that blocked the operation.</param>
		/// <returns><c>true</c> if the requested order was applied; otherwise, <c>false</c>.</returns>
		bool TrySetPluginOrder(IList<Plugin> p_lstOrderedPlugins, out IList<PluginValidationDiagnostic> p_lstBlockingDiagnostics);

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		bool ValidateOrder(IList<Plugin> p_lstPlugins);

		#endregion

		/// <summary>
		/// Runs the managed updaters.
		/// </summary>
		/// <param name="p_hashMods">The hash of mods.</param>
		/// <param name="p_booEnable">Enable/Disable/Toggle.</param>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the updaters.</returns>
		IBackgroundTask ManageMultiplePluginsTask(List<Plugin> p_lstPlugins, bool p_booEnable, ConfirmActionMethod p_camConfirm);

		/// <summary>
		/// Automatically sorts the managed plugins.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the sorting.</returns>
		IBackgroundTask AutoPluginSorting(ConfirmActionMethod p_camConfirm);

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		bool IsActivatiblePluginFile(string p_strPath);

		/// <summary>
		/// Determines if the game mode can handle more active plugins.
		/// </summary>
		/// <returns><c>true</c> if it can;
		/// <c>false</c> otherwise.</returns>
		bool CanActivatePlugins();

		List<Plugin> GetOrphanedPlugins(string p_strMasterName);

		/// <summary>
		/// Gets the plugin description.
		/// </summary>
		string GetPluginDescription(string p_strPlugin);

		/// <summary>
		/// Applies the load order specified by the given list of registered plugins
		/// </summary>
		/// <param name="p_kvpRegisteredPlugins">The list of registered plugins.</param>
		/// <param name="p_booSortingOnly">Whether we just want to apply the sorting.</param>
		IBackgroundTask ApplyLoadOrder(Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly);

		/// <summary>
		/// Applies an imported load order and optionally replaces the current active-plugin state.
		/// </summary>
		/// <param name="p_kvpRegisteredPlugins">The ordered plugins and their requested active states.</param>
		/// <param name="p_booSortingOnly">Whether only plugin ordering should be changed.</param>
		/// <param name="p_booReplaceActiveState">Whether unspecified non-protected plugins should be deactivated.</param>
		/// <returns>The background task applying the requested state.</returns>
		IBackgroundTask ApplyLoadOrder(Dictionary<Plugin, string> p_kvpRegisteredPlugins, bool p_booSortingOnly, bool p_booReplaceActiveState);
	}
}
