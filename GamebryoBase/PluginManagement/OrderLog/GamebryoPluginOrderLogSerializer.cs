using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.Games.Gamebryo.Tools.TESsnip;
using Nexus.Client.PluginManagement.OrderLog;
using Nexus.Client.Plugins;
using System.Diagnostics;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.OrderLog
{
	/// <summary>
	/// Serializes and deserializes the plugin order to a permanent store.
	/// </summary>
	public class GamebryoPluginOrderLogSerializer : IPluginOrderLogSerializer
	{
		#region Properties

		/// <summary>
		/// Gets the directory where the game's plugins are installed.
		/// </summary>
		/// <value>The directory where the game's plugins are installed.</value>
		protected string PluginDirectory { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_strPluginsDirectory">The directory where Fallout 3's plugins are installed.</param>
		public GamebryoPluginOrderLogSerializer(string p_strPluginsDirectory)
		{
			PluginDirectory = p_strPluginsDirectory;
		}

		#endregion

		#region IPluginOrderLogSerializer Members

		/// <summary>
		/// Deserializes the plugin order from the permanent store.
		/// </summary>
		/// <returns>The ordered list of plugins.</returns>
		public IEnumerable<string> LoadPluginOrder()
		{
			Trace.TraceInformation("Deserializing Plugin Load Order from: {0}", PluginDirectory);
			Trace.Indent();
			List<FileInfo> lstPlugins = new List<FileInfo>();
			DirectoryInfo difPluginsDirectory = new DirectoryInfo(PluginDirectory);
			lstPlugins.AddRange(difPluginsDirectory.GetFiles("*.esp"));
			lstPlugins.AddRange(difPluginsDirectory.GetFiles("*.esm"));
			lstPlugins.Sort(ComparePlugins);
			foreach (FileInfo fifPlugin in lstPlugins)
			{
				Trace.TraceInformation("Deserializing {0}", fifPlugin.FullName);
				yield return fifPlugin.FullName;
			}
			Trace.Unindent();
		}

		/// <summary>
		/// Serializes the plugin order to the permanent store.
		/// </summary>
		/// <param name="p_lstOrderedPlugins">The list of ordered plugins.</param>
		public void SavePluginOrder(IList<Plugin> p_lstOrderedPlugins)
		{
			DateTime dteTimestamp = new DateTime(2008, 1, 1);
			TimeSpan tspTwoMins = TimeSpan.FromMinutes(2);
			foreach (Plugin plgPlugin in p_lstOrderedPlugins)
			{
				File.SetLastWriteTime(plgPlugin.Filename, dteTimestamp);
				dteTimestamp += tspTwoMins;
			}
		}

		#endregion

		/// <summary>
		/// Compares the given plugins.
		/// </summary>
		/// <remarks>
		/// Master files are always less than non-master files. IF the two files are
		/// the same type, then the file that was modified first is the lesser of the two.
		/// </remarks>
		/// <param name="p_fifX">An object to compare to another object.</param>
		/// <param name="p_fifY">An object to compare to another object.</param>
		/// <returns>A value less than 0 if <paramref name="p_fifX"/> is less than <paramref name="p_fifY"/>.
		/// 0 if this node is equal to the other.
		/// A value greater than 0 if <paramref name="p_fifX"/> is greater than <paramref name="p_fifY"/>.</returns>
		protected Int32 ComparePlugins(FileInfo p_fifX, FileInfo p_fifY)
		{
			bool booXIsESM = TesPlugin.GetIsEsm(p_fifX.FullName);
			bool booYIsESM = TesPlugin.GetIsEsm(p_fifY.FullName);
			if (booXIsESM == booYIsESM)
				return p_fifX.LastWriteTime.CompareTo(p_fifY.LastWriteTime);
			return booXIsESM ? -1 : 1;
		}
	}
}
