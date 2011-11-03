using System;
using System.Collections.Generic;
using System.IO;
using Nexus.Client.PluginManagement.InstallationLog;
using Nexus.Client.Plugins;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo.PluginManagement.InstallationLog
{
	/// <summary>
	/// Serializes and deserializes data from the active plugin log permanent store.
	/// </summary>
	public class GamebryoActivePluginLogSerializer : IActivePluginLogSerializer
	{
		private string m_strPluginsFilePath = null;

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
		/// <param name="p_strPluginLogFilePath">The path to the log file that stores the list of active plugins.</param>
		public GamebryoActivePluginLogSerializer(string p_strPluginsDirectory, string p_strPluginLogFilePath)
		{
			PluginDirectory = p_strPluginsDirectory;
			m_strPluginsFilePath = p_strPluginLogFilePath;
		}

		#endregion

		/// <summary>
		/// Deserializes the list of active plugins from the permanent store.
		/// </summary>
		/// <returns>The list of active plugins.</returns>
		public IEnumerable<string> LoadPluginLog()
		{
			Set<string> setActivePlugins = new Set<string>(StringComparer.OrdinalIgnoreCase);
			if (File.Exists(m_strPluginsFilePath))
			{
				string[] strPlugins = File.ReadAllLines(m_strPluginsFilePath);
				char[] strInvalidChars = Path.GetInvalidFileNameChars();
				for (int i = 0; i < strPlugins.Length; i++)
				{
					strPlugins[i] = strPlugins[i].Trim();
					if (strPlugins[i].Length == 0 || strPlugins[i][0] == '#' || strPlugins[i].IndexOfAny(strInvalidChars) != -1)
						continue;
					string strPluginPath = Path.Combine(PluginDirectory, strPlugins[i]);
					if (!File.Exists(strPluginPath))
						continue;
					setActivePlugins.Add(strPluginPath);
				}
			}
			return setActivePlugins;
		}

		/// <summary>
		/// Serializes the list of active plugins to the permanent store.
		/// </summary>
		/// <returns>The ordered list of active plugins.</returns>
		public void SavePluginLog(IList<Plugin> p_lstActivePlugins)
		{
			Set<string> setPluginFilenames = new Set<string>(StringComparer.OrdinalIgnoreCase);
			foreach (Plugin plgPlugin in p_lstActivePlugins)
				setPluginFilenames.Add(Path.GetFileName(plgPlugin.Filename));
			if (!Directory.Exists(Path.GetDirectoryName(m_strPluginsFilePath)))
				Directory.CreateDirectory(Path.GetDirectoryName(m_strPluginsFilePath));
			File.WriteAllLines(m_strPluginsFilePath, setPluginFilenames.ToArray(), System.Text.Encoding.Default);
		}
	}
}
