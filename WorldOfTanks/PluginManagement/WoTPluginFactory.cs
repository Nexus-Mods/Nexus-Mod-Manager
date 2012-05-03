using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.Games.WorldOfTanks.PluginManagement
{
	/// <summary>
	/// Creats <see cref="Plugin"/>s from WorldOfTanks based game plugin files.
	/// </summary>
	public class WoTPluginFactory : IPluginFactory
	{
		private string m_strPluginDirectory = null;

		#region Contructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_strPluginDirectory">The directory where the plugins are installed.</param>
		/// <param name="p_bstBoss">The BOSS instance to use to set plugin order.</param>
		public WoTPluginFactory(string p_strPluginDirectory)
		{
			m_strPluginDirectory = p_strPluginDirectory;
		}

		#endregion

		/// <summary>
		/// Creates a plugin of the appropriate type from the specified file.
		/// </summary>
		/// <param name="p_strPluginPath">The path to the plugin file.</param>
		/// <returns>A plugin of the appropriate type from the specified file, if the type of the plugin
		/// can be determined; <c>null</c> otherwise.</returns>
		public Plugin CreatePlugin(string p_strPluginPath)
		{
            return null;
		}

		/// <summary>
		/// Determines if the specified file is a plugin that can be activated for the game mode.
		/// </summary>
		/// <param name="p_strPath">The path to the file for which it is to be determined if it is a plugin file.</param>
		/// <returns><c>true</c> if the specified file is a plugin file that can be activated in the game mode;
		/// <c>false</c> otherwise.</returns>
		public bool IsActivatiblePluginFile(string p_strPath)
		{
            return true;
		}
	}
}
