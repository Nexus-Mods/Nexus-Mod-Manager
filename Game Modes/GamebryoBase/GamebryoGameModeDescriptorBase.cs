using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Nexus.Client.Util;

namespace Nexus.Client.Games.Gamebryo
{
	/// <summary>
	/// Provides common information about Gamebryo based games.
	/// </summary>
	public abstract class GamebryoGameModeDescriptorBase : GameModeDescriptorBase
	{
		private static readonly List<string> PLUGIN_EXTENSIONS = new List<string>() { ".esm", ".esl", ".esp", ".bsa" };
		private static readonly List<string> STOP_FOLDERS = new List<string>() { "textures",
																					"meshes", "music", "shaders", "video", "interface",
																					"facegen", "menus", "lodsettings", "lsdata",
																					"sound" };
		private string[] m_strCriticalPlugins = null;
		private string[] m_strOfficialPlugins = null;
        private string[] officialUnmanagedPlugins = null;
		private string m_strPluginPath = string.Empty;

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
				if (!string.IsNullOrEmpty(m_strPluginPath))
					return m_strPluginPath;
 
				string strPath = string.Empty;
				if (!string.IsNullOrEmpty(InstallationPath))
				{
					strPath = Path.Combine(InstallationPath, "Data");

					string strPathRoot = Path.GetPathRoot(strPath);
					

					if (DriveInfo.GetDrives().Where(x => x.Name.Equals(strPathRoot, StringComparison.CurrentCultureIgnoreCase)).ToList().Count <= 0)
						throw new DirectoryNotFoundException("The selected drive is no longer present on the system.");

					if (!Directory.Exists(strPath))
						Directory.CreateDirectory(strPath);
				}

				m_strPluginPath = strPath;
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
					for (int i = OrderedCriticalPluginFilenames.Length - 1; i >= 0; i--)
						m_strCriticalPlugins[i] = Path.Combine(PluginDirectory, OrderedCriticalPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				}
				return m_strCriticalPlugins;
			}
		}

		/// <summary>
		/// Gets the list of official plugin names, ordered by load order.
		/// </summary>
		/// <value>The list of official plugin names, ordered by load order.</value>
		protected abstract string[] OrderedOfficialPluginFilenames { get; }

        /// <summary>
        /// Gets the list of official unmanageable plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official unmanageable plugin names, ordered by load order.</value>
        protected virtual string[] OrderedOfficialUnmanagedPluginFilenames {
            get
            {
                return null;
            }            
        }

        /// <summary>
        /// Gets the list of official plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official plugin names, ordered by load order.</value>
        public override string[] OrderedOfficialPluginNames
		{
			get
			{
				if (m_strOfficialPlugins == null)
				{
					m_strOfficialPlugins = new string[OrderedOfficialPluginFilenames.Length];
					for (int i = OrderedOfficialPluginFilenames.Length - 1; i >= 0; i--)
						m_strOfficialPlugins[i] = Path.Combine(PluginDirectory, OrderedOfficialPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
				}
				return m_strOfficialPlugins;
			}
		}

        /// <summary>
        /// Gets the list of official unamanageable plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official unamanageable plugin names, ordered by load order.</value>
        public override string[] OrderedOfficialUnmanagedPluginNames
        {
            get
            {
                try
                {
                    if (officialUnmanagedPlugins == null)
                    {
                        if (OrderedOfficialUnmanagedPluginFilenames != null)
                        {
                            officialUnmanagedPlugins = new string[OrderedOfficialUnmanagedPluginFilenames.Length];

                            for (var i = OrderedOfficialUnmanagedPluginFilenames.Length - 1; i >= 0; i--)
                            {
                                officialUnmanagedPlugins[i] = Path.Combine(PluginDirectory, OrderedOfficialUnmanagedPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                            }

                            return officialUnmanagedPlugins;
                        }
                        else
                        {
                            return new string[0];
                        }
                    }

                    return officialUnmanagedPlugins;
                }
                catch (ArgumentException e)
                {
                    Trace.TraceError("Problem encountered when parsing official plugin file names, see information below.");
                    TraceUtil.TraceException(e);

                    return new string[0];
                }
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
