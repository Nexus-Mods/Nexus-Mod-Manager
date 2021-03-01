namespace Nexus.Client.Games.Gamebryo
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    using Nexus.Client.Util;

    /// <summary>
    /// Provides common information about Gamebryo based games.
    /// </summary>
    public abstract class GamebryoGameModeDescriptorBase : GameModeDescriptorBase
	{
		private static readonly List<string> PLUGIN_EXTENSIONS = new List<string> { ".esm", ".esl", ".esp", ".bsa" };
		private static readonly List<string> STOP_FOLDERS = new List<string> { "textures", "scripts",
																					"meshes", "music", "shaders", "video", "interface",
																					"facegen", "menus", "lodsettings", "lsdata",
																					"sound",
                                                                                    "CalienteTools", "SKSE" };
		private string[] _criticalPlugins;
		private string[] _officialPlugins;
        private string[] _officialUnmanagedPlugins;
		private string _pluginPath = string.Empty;

		#region Properties

		/// <summary>
		/// Gets the extensions that are used by the game mode for plugin files.
		/// </summary>
		/// <value>The extensions that are used by the game mode for plugin files.</value>
		public override IEnumerable<string> PluginExtensions => PLUGIN_EXTENSIONS;

	    /// <summary>
		/// Gets a list of possible folders that should be looked for in mod archives to determine
		/// file structure.
		/// </summary>
		/// <value>A list of possible folders that should be looked for in mod archives to determine
		/// file structure.</value>
		public override IEnumerable<string> StopFolders => STOP_FOLDERS;

	    /// <summary>
		/// Gets the directory where Fallout 3 plugins are installed.
		/// </summary>
		/// <value>The directory where Fallout 3 plugins are installed.</value>
		public override string PluginDirectory
		{
			get
			{
				if (!string.IsNullOrEmpty(_pluginPath))
                {
                    return _pluginPath;
                }

                var path = string.Empty;

			    if (!string.IsNullOrEmpty(InstallationPath))
				{
					path = Path.Combine(InstallationPath, "Data");

					var pathRoot = Path.GetPathRoot(path);

					if (DriveInfo.GetDrives().Where(x => x.Name.Equals(pathRoot, StringComparison.CurrentCultureIgnoreCase)).ToList().Count <= 0)
                    {
                        throw new DirectoryNotFoundException("The selected drive is no longer present on the system.");
                    }

                    if (!Directory.Exists(path))
                    {
                        Directory.CreateDirectory(path);
                    }
                }

				_pluginPath = path;

			    return path;
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
				if (_criticalPlugins == null)
				{
					_criticalPlugins = new string[OrderedCriticalPluginFilenames.Length];

				    for (var i = OrderedCriticalPluginFilenames.Length - 1; i >= 0; i--)
                    {
                        _criticalPlugins[i] = Path.Combine(PluginDirectory, OrderedCriticalPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    }
                }

				return _criticalPlugins;
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
        protected virtual string[] OrderedOfficialUnmanagedPluginFilenames => null;

	    /// <summary>
        /// Gets the list of official plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official plugin names, ordered by load order.</value>
        public override string[] OrderedOfficialPluginNames
		{
			get
			{
				if (_officialPlugins == null)
				{
					_officialPlugins = new string[OrderedOfficialPluginFilenames.Length];

				    for (var i = OrderedOfficialPluginFilenames.Length - 1; i >= 0; i--)
                    {
                        _officialPlugins[i] = Path.Combine(PluginDirectory, OrderedOfficialPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    }
                }

				return _officialPlugins;
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
                    if (_officialUnmanagedPlugins == null)
                    {
                        if (OrderedOfficialUnmanagedPluginFilenames != null)
                        {
                            _officialUnmanagedPlugins = new string[OrderedOfficialUnmanagedPluginFilenames.Length];

                            for (var i = OrderedOfficialUnmanagedPluginFilenames.Length - 1; i >= 0; i--)
                            {
                                _officialUnmanagedPlugins[i] = Path.Combine(PluginDirectory, OrderedOfficialUnmanagedPluginFilenames[i]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                            }

                            return _officialUnmanagedPlugins;
                        }
                        else
                        {
                            return new string[0];
                        }
                    }

                    return _officialUnmanagedPlugins;
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
        /// <param name="environmentInfo">The application's envrionment info.</param>
        public GamebryoGameModeDescriptorBase(IEnvironmentInfo environmentInfo)
			: base(environmentInfo)
		{
		}

		#endregion
	}
}
