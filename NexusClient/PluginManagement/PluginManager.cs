using System;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.Plugins;
using Nexus.Client.Util;
using Nexus.Client.PluginManagement.OrderLog;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Nexus.Client.Games;
using System.IO;
using System.Collections.Generic;

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
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the
		/// current game mode.</param>
		/// <param name="p_polOrderLog">The <see cref="PluginOrderLog"/> tracking plugin order for the
		/// current game mode.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		/// <exception cref="InvalidOperationException">Thrown if the plugin manager has already
		/// been initialized.</exception>
		public static IPluginManager Initialize(IGameModeEnvironmentInfo p_gmiGameModeInfo, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, PluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator)
		{
			if (m_pmgCurrent != null)
				throw new InvalidOperationException("The Plugin Manager has already been initialized.");
			m_pmgCurrent = new PluginManager(p_gmiGameModeInfo, p_mprManagedPluginRegistry, p_aplPluginLog, p_polOrderLog, p_povOrderValidator);
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
		/// Gets the environment info of the current game mode.
		/// </summary>
		/// <value>The environment info of the current game mode.</value>
		protected IGameModeEnvironmentInfo GameModeInfo { get; private set; }

		/// <summary>
		/// Gets the <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.
		/// </summary>
		/// <value>The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</value>
		protected PluginRegistry ManagedPluginRegistry { get; private set; }

		/// <summary>
		/// Gets the <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.
		/// </summary>
		/// <value>The <see cref="ActivePluginLog"/> tracking plugin activations for the current game mode.</value>
		protected ActivePluginLog ActivePluginLog { get; private set; }

		/// <summary>
		/// Gets the <see cref="PluginOrderLog"/> tracking plugin order for the current game mode.
		/// </summary>
		/// <value>The <see cref="PluginOrderLog"/> tracking plugin order for the current game mode.</value>
		protected PluginOrderLog PluginOrderLog { get; private set; }

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

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_gmiGameModeInfo">The environment info of the current game mode.</param>
		/// <param name="p_mprManagedPluginRegistry">The <see cref="PluginRegistry"/> that contains the list
		/// of managed <see cref="Plugin"/>s.</param>
		/// <param name="p_aplPluginLog">The <see cref="ActivePluginLog"/> tracking plugin activations for the
		/// current game mode.</param>
		/// <param name="p_polOrderLog">The <see cref="PluginOrderLog"/> tracking plugin order for the
		/// current game mode.</param>
		/// <param name="p_povOrderValidator">The object that validates plugin order.</param>
		private PluginManager(IGameModeEnvironmentInfo p_gmiGameModeInfo, PluginRegistry p_mprManagedPluginRegistry, ActivePluginLog p_aplPluginLog, PluginOrderLog p_polOrderLog, IPluginOrderValidator p_povOrderValidator)
		{
			GameModeInfo = p_gmiGameModeInfo;
			ManagedPluginRegistry = p_mprManagedPluginRegistry;
			ActivePluginLog = p_aplPluginLog;
			PluginOrderLog = p_polOrderLog;
			OrderValidator = p_povOrderValidator;
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
		/// Determines if the specified plugin is registered.
		/// </summary>
		/// <param name="p_strPath">The path to the plugin whose registration status is to be determined.</param>
		/// <returns><c>true</c> if the specified plugin is registered;
		/// <c>false</c> otherwise.</returns>
		public bool IsPluginRegistered(string p_strPath)
		{
			string strPath = p_strPath;
			if (!Path.IsPathRooted(p_strPath))
				strPath = Path.Combine(GameModeInfo.InstallationPath, p_strPath);
			return ManagedPluginRegistry.GetPlugin(p_strPath) != null;
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
				strPath = Path.Combine(GameModeInfo.InstallationPath, p_strPath);
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
				strPath = Path.Combine(GameModeInfo.InstallationPath, p_strPath);
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
				strPath = Path.Combine(GameModeInfo.InstallationPath, p_strPath);
			return ActivePlugins.Contains(ManagedPluginRegistry.GetPlugin(strPath));
		}

		#endregion

		#region Plugin Ordering

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
				strPath = Path.Combine(GameModeInfo.InstallationPath, p_strPath);
			return ManagedPluginRegistry.IsActivatiblePluginFile(strPath);
		}
	}
}
