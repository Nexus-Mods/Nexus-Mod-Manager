using System;
using System.Collections.Generic;
using System.Diagnostics;
using Nexus.Client.Games.Gamebryo.PluginManagement.Boss;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.OrderLog
{
	/// <summary>
	/// Serializes and deserializes the plugin order to a permanent store.
	/// </summary>
	public class GamebryoPluginOrderLogSerializer : IPluginOrderLogSerializer
	{
		#region Properties

		/// <summary>
		/// Gets the BOSS plugin sorter.
		/// </summary>
		/// <value>The BOSS plugin sorter.</value>
		protected BossSorter BossSorter { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_bstBoss">The BOSS instance to use to set plugin order.</param>
		public GamebryoPluginOrderLogSerializer(BossSorter p_bstBoss)
		{
			BossSorter = p_bstBoss;
		}

		#endregion

		#region IPluginOrderLogSerializer Members

		/// <summary>
		/// Deserializes the plugin order from the permanent store.
		/// </summary>
		/// <returns>The ordered list of plugins.</returns>
		public IEnumerable<string> LoadPluginOrder()
		{
			Trace.TraceInformation("Getting Plugin Load Order from BOSS...");
			return new List<string>(BossSorter.GetLoadOrder());
		}

		/// <summary>
		/// Serializes the plugin order to the permanent store.
		/// </summary>
		/// <param name="p_lstOrderedPlugins">The list of ordered plugins.</param>
		public void SavePluginOrder(IList<Plugin> p_lstOrderedPlugins)
		{
			string[] strPlugins = new string[p_lstOrderedPlugins.Count];
			for (Int32 i = 0; i < p_lstOrderedPlugins.Count; i++)
				strPlugins[i] = p_lstOrderedPlugins[i].Filename;
			BossSorter.SetLoadOrder(strPlugins);
		}

		#endregion
	}
}
