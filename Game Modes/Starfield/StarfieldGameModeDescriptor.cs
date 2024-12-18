﻿using System.Drawing;
using System.IO;
using System.Linq;
using Nexus.Client.Games.Gamebryo;

namespace Nexus.Client.Games.Starfield
{
	/// <summary>
	/// Provides the basic information about the Starfield game mode.
	/// </summary>
	public class StarfieldGameModeDescriptor : GamebryoGameModeDescriptorBase
	{
		private static string[] EXECUTABLES = { "Starfield.exe" };
		private static string[] CRITICAL_PLUGINS = { "Starfield.esm" };
		private static string[] OFFICIAL_PLUGINS = { "BlueprintShips-Starfield.esm", "Constellation.esm", "OldMars.esm", "ShatteredSpace.esm" };
        private static string[] OFFICIAL_UNMANAGED_PLUGINS = { "SFBGS003.esm", "SFBGS004.esm", "SFBGS006.esm", "SFBGS007.esm", "SFBGS008.esm" };

		private const string MODE_ID = "Starfield";

        private bool m_booCheckedCccFile;

		#region Properties

		/// <summary>
		/// Gets the display name of the game mode.
		/// </summary>
		/// <value>The display name of the game mode.</value>
		public override string Name
		{
			get
			{
				return "Starfield";
			}
		}

		/// <summary>
		/// Gets the unique id of the game mode.
		/// </summary>
		/// <value>The unique id of the game mode.</value>
		public override string ModeId
		{
			get
			{
				return MODE_ID;
			}
		}

		/// <summary>
		/// Gets the list of possible executable files for the game.
		/// </summary>
		/// <value>The list of possible executable files for the game.</value>
		public override string[] GameExecutables
		{
			get
			{
				return EXECUTABLES;
			}
		}

		/// <summary>
		/// Gets the list of critical plugin filenames, ordered by load order.
		/// </summary>
		/// <value>The list of critical plugin filenames, ordered by load order.</value>
		protected override string[] OrderedCriticalPluginFilenames
		{
			get
			{
				return CRITICAL_PLUGINS;
			}
		}

        /// <summary>
        /// Gets the list of official plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official plugin names, ordered by load order.</value>
        protected override string[] OrderedOfficialPluginFilenames
		{
			get
			{
				return OFFICIAL_PLUGINS;
			}
		}

        /// <summary>
        /// Gets the list of official unmanageable plugin names, ordered by load order.
        /// </summary>
        /// <value>The list of official unmanageable plugin names, ordered by load order.</value>
        protected override string[] OrderedOfficialUnmanagedPluginFilenames
        {
            get
            {
                if (!m_booCheckedCccFile)
                {
                    m_booCheckedCccFile = true;

                    if (File.Exists(Path.Combine(InstallationPath, "Starfield.ccc")))
                    {
                        var lines = File.ReadLines(Path.Combine(InstallationPath, "Starfield.ccc")).Where(line => !string.IsNullOrEmpty(line));
                        OFFICIAL_UNMANAGED_PLUGINS = OFFICIAL_UNMANAGED_PLUGINS.Union(lines).ToArray();
                        m_booCheckedCccFile = true;
                    }
                }

                return OFFICIAL_UNMANAGED_PLUGINS;
            }
        }

        /// <summary>
        /// Gets the theme to use for this game mode.
        /// </summary>
        /// <value>The theme to use for this game mode.</value>
        public override Theme ModeTheme
		{
			get
			{
				return new Theme(Properties.Resources.Starfield_logo,Color.FromArgb(50, 104, 250),null);
			}
		}

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_eifEnvironmentInfo">The application's envrionment info.</param>
		public StarfieldGameModeDescriptor(IEnvironmentInfo p_eifEnvironmentInfo)
			: base(p_eifEnvironmentInfo)
		{
		}

		#endregion
	}
}
