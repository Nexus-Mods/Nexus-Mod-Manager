using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder;
using Nexus.Client.PluginManagement;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.InstallationLog
{
	/// <summary>
	/// Serializes and deserializes data from the active plugin log permanent store.
	/// </summary>
	public class GamebryoActivePluginLogSerializer : IActivePluginLogSerializer
	{
		#region Properties

		/// <summary>
		/// Gets the current game mode.
		/// </summary>
		/// <value>The current game mode.</value>
		protected IGameMode GameMode { get; private set; }

		/// <summary>
		/// Gets the <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.
		/// </summary>
		/// <value>The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</value>
		protected IPluginOrderLog PluginOrderLog { get; private set; }

		/// <summary>
		/// Gets the LoadOrder plugin manager.
		/// </summary>
		/// <value>The LoadOrder plugin manager.</value>
		protected ILoadOrderManager LoadOrderManager { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_gmdGameMode">The current game mode.</param>
		/// <param name="p_polPluginOrderLog">The <see cref="IPluginOrderLog"/> tracking plugin order for the current game mode.</param>
		/// <param name="p_bstLoadOrder">The LoadOrder instance to use to set plugin order.</param>
		public GamebryoActivePluginLogSerializer(IGameMode p_gmdGameMode, IPluginOrderLog p_polPluginOrderLog, ILoadOrderManager p_bstLoadOrder)
		{
			GameMode = p_gmdGameMode;
			PluginOrderLog = p_polPluginOrderLog;
			LoadOrderManager = p_bstLoadOrder;
		}

		#endregion

		/// <summary>
		/// Deserializes the list of active plugins from the permanent store.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		public IEnumerable<string> LoadPluginLog()
		{
			return LoadOrderManager.GetActivePlugins();
		}

		/// <summary>
		/// Serializes the list of active plugins to the permanent store.
		/// </summary>
		/// <returns>The ordered list of active plugins.</returns>
		public void SavePluginLog(IList<Plugin> p_lstActivePlugins)
		{
			if (p_lstActivePlugins.IsNullOrEmpty())
			{
				LoadOrderManager.SetActivePlugins(GameMode.OrderedCriticalPluginNames);
				return;
			}

			List<string> lstPlugins = new List<string>();
			foreach (Plugin plgPlugin in p_lstActivePlugins)
				lstPlugins.Add(plgPlugin.Filename);
			lstPlugins.Sort();

			List<string> lstActivePlugins = new List<string>();
			foreach (string strPlugin in GameMode.OrderedCriticalPluginNames)
			{
				lstPlugins.RemoveAll(x => x.Equals(strPlugin, StringComparison.CurrentCultureIgnoreCase));
				if (!lstActivePlugins.Contains(strPlugin, StringComparer.CurrentCultureIgnoreCase))
					lstActivePlugins.Add(strPlugin);
			}
			foreach (Plugin plgPlugin in PluginOrderLog.OrderedPlugins)
			{
				if (lstPlugins.Contains(plgPlugin.Filename, StringComparer.CurrentCultureIgnoreCase))
					if (!lstActivePlugins.Contains(plgPlugin.Filename, StringComparer.CurrentCultureIgnoreCase))
						lstActivePlugins.Add(plgPlugin.Filename);
			}
			LoadOrderManager.SetActivePlugins(lstActivePlugins.ToArray());
		}
	}
}
