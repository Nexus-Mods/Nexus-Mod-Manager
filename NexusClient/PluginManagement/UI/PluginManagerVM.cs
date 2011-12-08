using System;
using System.Collections.Generic;
using Nexus.Client.Commands.Generic;
using Nexus.Client.Plugins;
using Nexus.Client.Settings;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.PluginManagement.UI
{
	/// <summary>
	/// This class encapsulates the data and the operations presented by UI
	/// elements that display plugin management.
	/// </summary>
	public class PluginManagerVM
	{
		#region Properties

		#region Commands

		/// <summary>
		/// Gets the command to activate a plugin.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugin to be activated.
		/// </remarks>
		/// <value>The command to activate a plugin.</value>
		public Command<Plugin> ActivatePluginCommand { get; private set; }

		/// <summary>
		/// Gets the command to deactivate a plugin.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugin to be deactivated.
		/// </remarks>
		/// <value>The command to deactivate a plugin.</value>
		public Command<Plugin> DeactivatePluginCommand { get; private set; }

		/// <summary>
		/// Gets the command to move a plugin up in the load order.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugin to be moved. Moving up decreases
		/// the load order.
		/// </remarks>
		/// <value>The command to move a plugin up in the load order.</value>
		public Command<Plugin> MoveUpCommand { get; private set; }

		/// <summary>
		/// Gets the command to move a plugin down in the load order.
		/// </summary>
		/// <remarks>
		/// The commands takes an argument describing the plugin to be moved. Moving down increases
		/// the load order.
		/// </remarks>
		/// <value>The command to move a plugin down in the load order.</value>
		public Command<Plugin> MoveDownCommand { get; private set; }

		#endregion

		/// <summary>
		/// Gets the plugin manager to use to manage plugins.
		/// </summary>
		/// <value>The plugin manager to use to manage plugins.</value>
		protected IPluginManager PluginManager { get; private set; }

		/// <summary>
		/// Gets the list of plugins being managed by the plugin manager.
		/// </summary>
		/// <value>The list of plugins being managed by the plugin manager.</value>
		public ReadOnlyObservableList<Plugin> ManagedPlugins
		{
			get
			{
				return PluginManager.ManagedPlugins;
			}
		}

		/// <summary>
		/// Gets the list of active plugins.
		/// </summary>
		/// <value>The list of active plugins.</value>
		public ReadOnlyObservableList<Plugin> ActivePlugins
		{
			get
			{
				return PluginManager.ActivePlugins;
			}
		}

		/// <summary>
		/// Gets the application and user settings.
		/// </summary>
		/// <value>The application and user settings.</value>
		public ISettings Settings { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_pmgPluginManager">The plugin manager to use to manage plugins.</param>
		/// <param name="p_setSettings">The application and user settings.</param>
		public PluginManagerVM(IPluginManager p_pmgPluginManager, ISettings p_setSettings)
		{
			PluginManager = p_pmgPluginManager;
			Settings = p_setSettings;

			ActivatePluginCommand = new Command<Plugin>("Activate Plugin", "Activates the selected plugin.", ActivatePlugin);
			DeactivatePluginCommand = new Command<Plugin>("Deactivate Plugin", "Deactivates the selected plugin.", DeactivatePlugin);
			MoveUpCommand = new Command<Plugin>("Move Plugin Up", "Moves the plugin up in the load order.", MovePluginUp);
			MoveDownCommand = new Command<Plugin>("Move Plugin Down", "Moves the plugin down in the load order.", MovePluginDown);
		}

		#endregion

		#region Mod Activation/Deactivation

		/// <summary>
		/// Activates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to activate.</param>
		public void ActivatePlugin(Plugin p_plgPlugin)
		{
			PluginManager.ActivatePlugin(p_plgPlugin);
		}

		/// <summary>
		/// deactivates the given plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to deactivate.</param>
		public void DeactivatePlugin(Plugin p_plgPlugin)
		{
			PluginManager.DeactivatePlugin(p_plgPlugin);
		}

		#endregion

		#region Plugin Ordering

		/// <summary>
		/// Sets the load order of the specifid plugin.
		/// </summary>
		/// <param name="p_plgPlugin">The full path to the plugin file whose load order is to be set.</param>
		/// <param name="p_intNewIndex">The new load order index of the plugin.</param>
		public void SetPluginOrderIndex(Plugin p_plgPlugin, int p_intNewIndex)
		{
			PluginManager.SetPluginOrderIndex(p_plgPlugin, p_intNewIndex);
		}

		/// <summary>
		/// Determines if the specified plugin order is valid.
		/// </summary>
		/// <param name="p_lstPlugins">The plugins whose order is to be validated.</param>
		/// <returns><c>true</c> if the given plugins are in a valid order;
		/// <c>false</c> otherwise.</returns>
		public bool ValidatePluginOrder(IList<Plugin> p_lstPlugins)
		{
			return PluginManager.ValidateOrder(p_lstPlugins);
		}

		/// <summary>
		/// Moves the given plugin up in the load order.
		/// </summary>
		/// <remarks>
		/// Moving a plugin up in the load order decreases the load order, meaning it is loaded sooner.
		/// </remarks>
		/// <param name="p_plgPlugin">The plugin whose load order is to be changed.</param>
		protected void MovePluginUp(Plugin p_plgPlugin)
		{
			Int32 intOldIndex = PluginManager.ManagedPlugins.IndexOf(p_plgPlugin);
			if (intOldIndex > 0)
				PluginManager.SetPluginOrderIndex(p_plgPlugin, intOldIndex - 1);
		}

		/// <summary>
		/// Moves the given plugin down in the load order.
		/// </summary>
		/// <remarks>
		/// Moving a plugin down in the load order increases the load order, meaning it is loaded later.
		/// </remarks>
		/// <param name="p_plgPlugin">The plugin whose load order is to be changed.</param>
		protected void MovePluginDown(Plugin p_plgPlugin)
		{
			Int32 intOldIndex = PluginManager.ManagedPlugins.IndexOf(p_plgPlugin);
			if (intOldIndex < PluginManager.ManagedPlugins.Count - 1)
				PluginManager.SetPluginOrderIndex(p_plgPlugin, intOldIndex + 1);
		}

		/// <summary>
		/// Determines if the given plugin can be moved up in the load order.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the load order can be decreased.</param>
		/// <returns><c>true</c> if the given plugin's load oder can be decreased;
		/// <c>false</c> otherwise.</returns>
		public bool CanMovePluginUp(Plugin p_plgPlugin)
		{
			Int32 intOldIndex = PluginManager.ManagedPlugins.IndexOf(p_plgPlugin);
			if (intOldIndex < 1)
				return false;
			List<Plugin> lstPlugins = new List<Plugin>(PluginManager.ManagedPlugins);
			lstPlugins.Swap(intOldIndex, intOldIndex - 1);
			return PluginManager.ValidateOrder(lstPlugins);
		}

		/// <summary>
		/// Determines if the given plugin can be moved down in the load order.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin for which it is to be determined if the load order can be increased.</param>
		/// <returns><c>true</c> if the given plugin's load oder can be increased;
		/// <c>false</c> otherwise.</returns>
		public bool CanMovePluginDown(Plugin p_plgPlugin)
		{
			Int32 intOldIndex = PluginManager.ManagedPlugins.IndexOf(p_plgPlugin);
			if ((intOldIndex < 0) || (intOldIndex > PluginManager.ManagedPlugins.Count - 2))
				return false;
			List<Plugin> lstPlugins = new List<Plugin>(PluginManager.ManagedPlugins);
			lstPlugins.Swap(intOldIndex, intOldIndex + 1);
			return PluginManager.ValidateOrder(lstPlugins);
		}

		#endregion
	}
}
