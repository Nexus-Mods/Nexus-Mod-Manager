using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// Provides common information about Gamebryo based games.
	/// </summary>
	public abstract class GamebryoGameModeDescriptorBase : GameModeDescriptorBase
	{
		private static readonly List<string> PLUGIN_EXTENSIONS = new List<string>() { ".esm", ".esp", ".bsa" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "textures",
																					"meshes", "music", "shaders", "video",
																					"facegen", "menus", "lodsettings", "lsdata",
																					"sound" };
		private string[] m_strCriticalPlugins = null;

		#region Properties

		/// <summary>
		/// Gets the extensions that are used by the game mode for plugin files.
		/// </summary>
		/// <value>The extensions that are used by the game mode for plugin files.</value>
		public override IEnumerable<string> PluginExtensions
		{
			get
			{
				return PLUGIN_EXTENSIONS;
			}
		}

		/// <summary>
		/// Gets a list of possible folders that should be looked for in mod archives to determine
		/// file structure.
		/// </summary>
		/// <value>A list of possible folders that should be looked for in mod archives to determine
		/// file structure.</value>
		public override IEnumerable<string> StopFolders
		{
			get
			{
				return STOP_FOLDERS;
			}
		}

		/// <summary>
		/// Gets the directory where Fallout 3 plugins are installed.
		/// </summary>
		/// <value>The directory where Fallout 3 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				string strPath = String.Empty;
				if (!String.IsNullOrEmpty(InstallationPath))
				{
					Path.Combine(InstallationPath, "Data");
					if (!Directory.Exists(strPath))
						Directory.CreateDirectory(strPath);
				}

				return strPath;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected abstract string[] OrderedCriticalPluginFilenames { get; }

		/// <summary>
		/// Gets the list of critical plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin names, ordered by load order.</value>
		public override string[] OrderedCriticalPluginNames
		{
			get
			{
				if (m_strCriticalPlugins == null)
				{
					m_strCriticalPlugins = new string[OrderedCriticalPluginFilenames.Length];
					for (Int32 i = OrderedCriticalPluginFilenames.Length - 1; i >= 0; i--)
						m_strCriticalPlugins[i] = Path.Combine(PluginDirectory, OrderedCriticalPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				}
				return m_strCriticalPlugins;
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public GamebryoGameModeDescriptorBase(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
