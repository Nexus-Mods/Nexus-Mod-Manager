using System;
using System.Collections.Generic;
using Nexus.Client.Games.Gamebryo.PluginManagement.Boss;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.InstallationLog
{
	/// <summary>
	/// Serializes and deserializes data from the active plugin log permanent store.
	/// </summary>
	public class GamebryoActivePluginLogSerializer : IActivePluginLogSerializer
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
		public GamebryoActivePluginLogSerializer(BossSorter p_bstBoss)
		{
			BossSorter = p_bstBoss;
		}

		#endregion

		/// <summary>
		/// Deserializes the list of active plugins from the permanent store.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		public IEnumerable<string> LoadPluginLog()
		{
			return BossSorter.GetActivePlugins();
		}

		/// <summary>
		/// Serializes the list of active plugins to the permanent store.
		/// </summary>
		/// <returns>The ordered list of active plugins.</returns>
		public void SavePluginLog(IList<Plugin> p_lstActivePlugins)
		{
			string[] strPlugins = new string[p_lstActivePlugins.Count];
			for (Int32 i = 0; i < p_lstActivePlugins.Count; i++)
				strPlugins[i] = p_lstActivePlugins[i].Filename;
			BossSorter.SetActivePlugins(strPlugins);
		}
	}
}
