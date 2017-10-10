using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Nexus.Client.PluginManagement;
using System;

namespace Nexus.Client.Games.Gamebryo.PluginManagement
{
	/// <summary>
	/// Finds the Gamebryo based game's <see cref="Nexus.Client.Plugins.Plugin"/>s.
	/// </summary>
	public class GamebryoPluginDiscoverer : IPluginDiscoverer
	{
		private static string[] PLUGIN_EXTENSIONS = new string[] { ".esp", ".esl", ".esm" };

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
		public GamebryoPluginDiscoverer(string p_strPluginsDirectory)
		{
			PluginDirectory = p_strPluginsDirectory;
		}

		#endregion

		#region IPluginDiscoverer Members

		/// <summary>
		/// Returns the list of plugin files for the current game mode.
		/// </summary>
		/// <returns>The list of plugin files for the current game mode.</returns>
		public IEnumerable<string> FindPlugins()
		{
			Trace.TraceInformation("Discovering Plugins...");
			Trace.Indent();

			Trace.TraceInformation("Looking in: {0}", PluginDirectory);

			List<string> lstPlugins = new List<string>();
			List<string> lstCandidates = new List<string>();
			foreach (String strExtension in PLUGIN_EXTENSIONS)
				lstCandidates.AddRange(Directory.GetFiles(PluginDirectory, "*" + strExtension));
			foreach (string strPlugin in lstCandidates)
			{
				Trace.TraceInformation("Found: {0}", strPlugin);
				lstPlugins.Add(strPlugin);
			}
			Trace.Unindent();
			return lstPlugins;
		}

		/// <summary>
		/// Determines if the given path points at a plugin.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the file to be idecntified.</param>
		/// <returns><c>true</c> if the given path represents a plugin file;
		/// <c>false</c> otherwise.</returns>
		public bool IsPlugin(string p_strPluginPath)
		{
			return Array.IndexOf(PLUGIN_EXTENSIONS, Path.GetExtension(p_strPluginPath).ToLowerInvariant()) > -1;
		}

		#endregion
	}
}
