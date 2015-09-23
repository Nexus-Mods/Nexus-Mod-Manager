using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nexus.Client.Games.Gamebryo.PluginManagement.LoadOrder;
using Nexus.Client.Games.Gamebryo.PluginManagement.Sorter;
using Nexus.Client.PluginManagement;
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
		/// Gets the LoadOrder plugin manager.
		/// </summary>
		/// <value>The LoadOrder plugin manager.</value>
		protected ILoadOrderManager LoadOrderManager { get; private set; }


		/// <summary>
		/// Gets the Plugin sorter.
		/// </summary>
		/// <value>The Plugin sorter.</value>
		protected PluginSorter PluginSorter { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_bstPluginSorter">The PluginSorter instance to use to set plugin order.</param>
		public GamebryoPluginOrderLogSerializer(ILoadOrderManager p_bstLoadOrder, PluginSorter p_bstPluginSorter)
		{
			LoadOrderManager = p_bstLoadOrder;
			PluginSorter = p_bstPluginSorter;
		}

		#endregion

		#region IPluginOrderLogSerializer Members

		/// <summary>
		/// Deserializes the plugin order from the permanent store.
		/// </summary>
		/// <returns>The ordered list of plugins.</returns>
		public IEnumerable<string> LoadPluginOrder()
		{
			Trace.TraceInformation("Getting Plugin Load Order from the load order manager...");
			return new List<string>(LoadOrderManager.GetLoadOrder());
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
			LoadOrderManager.SetLoadOrder(strPlugins);
		}

		/// <summary>
		/// Sorts the plugins.
		/// </summary>
		/// <param name="p_lstOrderedPlugins">The list of ordered plugins.</param>
		public string[] SortPlugins(IList<Plugin> p_lstOrderedPlugins)
		{
			string[] lstSortedPlugins = null;

			if (PluginSorter != null)
			{
				string[] strPlugins = new string[p_lstOrderedPlugins.Count];
				for (Int32 i = 0; i < p_lstOrderedPlugins.Count; i++)
					strPlugins[i] = p_lstOrderedPlugins[i].Filename;
				lstSortedPlugins = PluginSorter.SortPlugins(strPlugins);
			}

			return lstSortedPlugins;
		}

		#endregion
	}
}
