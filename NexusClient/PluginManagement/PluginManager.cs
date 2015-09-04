using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.Games;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using Nexus.Client.UI;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.PluginManagement
{
	/// <summary>
	/// The class the encapsulates managing plugins.
	/// </summary>
	/// <remarks>
	/// The list of managed plugins needs to be centralized to ensure integrity; having multiple managers, each
	/// with a potentially different list of managed plugins, would be disastrous. As such, this
	/// object is a singleton to help enforce that policy.
	/// Note, however, that the singleton nature of the manager is not meant to provide global access to the object.
	/// As such, there is no static accessor to retrieve the singleton instance. Instead, the
	/// <see cref="Initialize"/> method returns the only instance that should be used.
	/// </remarks>
	public class PluginManager : IPluginManager
	{
		#region Singleton

		private static IPluginManager m_pmgCurrent = null;

		/// <summary>
		/// Initializes the singleton intances of the mod manager.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the
		/// current game mode.</param>
		/// <param name="p_polOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the
		/// current game mode.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		/// <exception cref="InvalidOperationException">Thrown if the plugin manager has already
		/// been initialized.</exception>
		public static IPluginManager Initialize(IGameMode p_gmdGameMode, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, IPluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator)
		{
			if (m_pmgCurrent != null)
				throw new InvalidOperationException("The Plugin Manager has already been initialized.");
			m_pmgCurrent = new PluginManager(p_gmdGameMode, p_mprManagedPluginRegistry, p_aplPluginLog, p_polOrderLog, p_povOrderValidator);
			return m_pmgCurrent;
		}

		/// <summary>
		/// This disposes of the singleton object, allowing it to be re-initialized.
		/// </summary>
		public void Release()
		{
			m_pmgCurrent = null;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.
		/// </summary>
		/// <value>The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</value>
		public PluginRegistry ManagedPluginRegistry { get; set; }

		/// <summary>
		/// Gets the <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.
		/// </summary>
		/// <value>The <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.</value>
		public ActivePluginLog ActivePluginLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.
		/// </summary>
		/// <value>The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</value>
		public IPluginOrderLog PluginOrderLog { get; private set; }

		/// <summary>
		/// Gets the object that validates plugin order.
		/// </summary>
		/// <value>The object that validates plugin order.</value>
		protected IPluginOrderValidator OrderValidator { get; private set; }

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<Plugin> ManagedPlugins
		{
			get
			{
				return PluginOrderLog.OrderedPlugins;
			}
		}

		/// <summary>
		/// Gets the list of mods being managed by the mod manager.
		/// </summary>
		/// <value>The list of mods being managed by the mod manager.</value>
		public ReadOnlyObservableList<Plugin> ActivePlugins
		{
			get
			{
				return ActivePluginLog.ActivePlugins;
			}
		}

		/// <summary>
		/// Gets the max allowed number of active plugins.
		/// </summary>
		/// <value>The max allowed number of active plugins (0 if there's no limit).</value>
		public Int32 MaxAllowedActivePluginsCount 
		{
			get
			{
				return GameMode.MaxAllowedActivePluginsCount;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the
		/// current game mode.</param>
		/// <param name="p_polOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the
		/// current game mode.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		private PluginManager(IGameMode p_gmdGameMode, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, IPluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator)
		{
			GameMode = p_gmdGameMode;
			ManagedPluginRegistry = p_mprManagedPluginRegistry;
			ActivePluginLog = p_aplPluginLog;
			PluginOrderLog = p_polOrderLog;
			OrderValidator = p_povOrderValidator;

            if (GameMode.OrderedCriticalPluginNames != null)
            {
                foreach (string strPlugin in GameMode.OrderedCriticalPluginNames)
                    ActivePluginLog.ActivatePlugin(strPlugin);
                List<Plugin> lstPlugins = new List<Plugin>(PluginOrderLog.OrderedPlugins);
                if (!OrderValidator.ValidateOrder(lstPlugins))
                {
                    OrderValidator.CorrectOrder(lstPlugins);
                    PluginOrderLog.SetPluginOrder(lstPlugins);
                }
            }		
		}

		#endregion

		#region Plugin Registration

		/// <summary>
		/// Adds the specified plugin to the list of managed plugins.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to add.</param>
		/// <returns><c>true</c> if the specified plugin was added;
		/// <c>false</c> otherwise.</returns>
		public bool AddPlugin(string p_strPluginPath)
		{
			bool booSuccess = ManagedPluginRegistry.RegisterPlugin(p_strPluginPath);
			if (booSuccess)
				PluginOrderLog.SetPluginOrderIndex(ManagedPluginRegistry.GetPlugin(p_strPluginPath), PluginOrderLog.OrderedPlugins.Count);
			return booSuccess;
		}

		/// <summary>
		/// Removes the given plugin from the list of managed plugins.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to remove.</param>
		public void RemovePlugin(Plugin p_plgPlugin)
		{
			ActivePluginLog.DeactivatePlugin(p_plgPlugin);
			PluginOrderLog.RemovePlugin(p_plgPlugin);
			ManagedPluginRegistry.UnregisterPlugin(p_plgPlugin);
		}

		/// <summary>
		/// Removes the specified plugin from the list of managed plugins.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin to remove.</param>
		public void RemovePlugin(string p_strPluginPath)
		{
			RemovePlugin(ManagedPluginRegistry.GetPlugin(p_strPluginPath));
		}

		/// <summary>
		/// Automatically sorts the managed plugins.
		/// </summary>
		/// <param name="p_camConfirm">The delegate to call to confirm an action.</param>
		/// <returns>The background task that will run the sorting.</returns>
		public IBackgroundTask AutoPluginSorting(ConfirmActionMethod p_camConfirm)
		{
			AutoPluginSortingTask pstPluginSortingTask = new AutoPluginSortingTask(GameMode, ManagedPlugins, p_camConfirm);
			pstPluginSortingTask.Update(p_camConfirm);
			return pstPluginSortingTask;
		}

		/// <summary>
		/// Determines if the specified plugin is registered.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose registration status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is registered;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginRegistered(string p_strPath)
		{
			return GetRegisteredPlugin(p_strPath) != null;
		}

		/// <summary>
		/// Gets the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path of the plugin to retrieve.</param>
		/// <returns>The specified plugin, or <c>null</c> if the plugin is not registered.</returns>
		public Plugin GetRegisteredPlugin(string p_strPath)
		{
			//TODO this check doesn't work for Gamegryo based games
			// GetFormatSpecificInstallPath() (or whatever it is called) should be
			// used instead of InstallationPath
			// but we can't use it because asking for the mod format here makes no
			// sense
			// as such, mods should pass in the full path, or at least a path relative to
			// InstallationPath
			// Really, I think GetFormatSpecificInstallPath() should be scrapped,
			// and mods should adjust for the current game mode, not the game mode for the
			// current mod format
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, p_strPath);
			return ManagedPluginRegistry.GetPlugin(strPath);
		}

		#endregion

		#region Plugin Activation/Deactivation

		/// <summary>
		/// Sets the activations status of the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose status is to be set.</param>
		/// <param name="p_booActive">Whether to activate the plugin, or deactivate it.</param>
		public void SetPluginActivation(string p_strPath, bool p_booActive)
		{
			if (p_booActive)
				ActivatePlugin(p_strPath);
			else
				DeactivatePlugin(p_strPath);
		}

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to activate.</param>
		public void ActivatePlugin(Plugin p_plgPlugin)
		{
			ActivePluginLog.ActivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// Activates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to activate.</param>
		public void ActivatePlugin(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, p_strPath);
			ActivePluginLog.ActivatePlugin(strPath);
		}

		/// <summary>
		/// Deactivates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to deactivate.</param>
		public void DeactivatePlugin(Plugin p_plgPlugin)
		{
			ActivePluginLog.DeactivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// Deactivates the specified plugin.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin to deactivate.</param>
		public void DeactivatePlugin(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, p_strPath);
			ActivePluginLog.DeactivatePlugin(strPath);
		}

		/// <summary>
		/// Determines if the specified plugin is active.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose active status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is active;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginActive(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, p_strPath);
			return ActivePlugins.Contains(ManagedPluginRegistry.GetPlugin(strPath));
		}

		/// <summary>
		/// Determines if the active state of the given plugin can be changed.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the active state can be changed.</param>
		/// <returns><c>true</c> if the given plugin's active state can be changed;
		/// <c>false</c> otherwise.</returns>
		public bool CanChangeActiveState(Plugin p_plgPlugin)
		{
			return !GameMode.IsCriticalPlugin(p_plgPlugin);
		}

		#endregion

		#region Plugin Ordering

		/// <summary>
		/// Gets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose load order is to be returned.</param>
		/// <returns>The index of the given plugin, or -1 if the plugin is not being managed.</returns>
		public Int32 GetPluginOrderIndex(Plugin p_plgPlugin)
		{
			return PluginOrderLog.OrderedPlugins.IndexOf(p_plgPlugin);
		}

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		public void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex)
		{
			PluginOrderLog.SetPluginOrderIndex(p_plgPlugin, p_intNewIndex);
		}

		/// <summary>
		/// Sets the order of the plugins to the given order.
		/// </summary>
		/// <remarks>
		/// If the given list does not include all registered plugins, then the plugins are ordered in a manner
		/// so as to not displace the positions of the plugins whose order was not specified.
		/// </remarks>
		/// <param name="p_lstOrderedPlugins">The list indicating the desired order of the plugins.</param>
		public void SetPluginOrder(IList<Plugin> p_lstOrderedPlugins)
		{
			PluginOrderLog.SetPluginOrder(p_lstOrderedPlugins);
		}

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		public bool ValidateOrder(IList<Plugin> p_lstPlugins)
		{
			return OrderValidator.ValidateOrder(p_lstPlugins);
		}

		#endregion

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		public bool IsActivatiblePluginFile(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameMode.GameModeEnvironmentInfo.InstallationPath, p_strPath);

			return ManagedPluginRegistry.IsActivatiblePluginFile(strPath);
		}

		/// <summary>
		/// Determines if the game mode can handle more active plugins.
		/// </summary>
		/// <returns><c>true</c> if it can;
		/// <c>false</c> otherwise.</returns>
		public bool CanActivatePlugins()
		{ 
			return !((GameMode.MaxAllowedActivePluginsCount > 0) && (ActivePlugins.Count >= GameMode.MaxAllowedActivePluginsCount));
		}
	}
}
