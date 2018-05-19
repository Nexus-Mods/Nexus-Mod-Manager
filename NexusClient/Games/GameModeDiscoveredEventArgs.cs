using System;
using System.Collections.Generic;
using System.IO;

namespace Nexus.Client.Games
{
	/// <summary>
	/// An event arguments class that indicates a possible installation path of
	/// a game mode has been found.
	/// </summary>
	public class GameModeDiscoveredEventArgs : EventArgs
	{
		#region Properties

		/// <summary>
		/// Gets the game mode info for the game for which an installation path has been found.
		/// </summary>
		/// <value>The game mode info for the game for which an installation path has been found.</value>
		public IGameModeDescriptor GameMode { get; private set; }

		/// <summary>
		/// Gets the installation path that was found.
		/// </summary>
		/// <value>The installation path that was found.</value>
		public string InstallationPath { get; private set; }

        /// <summary>
        /// Gets a value determining whether or not the game is installed on a file system that supports symbolic linking.
        /// </summary>
        public bool InstalledOnSuitableFileSystem { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="p_gmdGameModeInfo">The game mode info for the game for which an installation path has been found.</param>
		/// <param name="p_strInstallationPath">The installation path that was found.</param>
		public GameModeDiscoveredEventArgs(IGameModeDescriptor p_gmdGameModeInfo, string p_strInstallationPath)
		{
			GameMode = p_gmdGameModeInfo;
			InstallationPath = p_strInstallationPath;
            InstalledOnSuitableFileSystem = DoesFileSystemSupportSymbolicLinks(p_strInstallationPath);            
        }

        /// <summary>
        /// Determines if the file system of the drive is suitable for NMM to use.
        /// </summary>
        /// <param name="p_strPath">Path to folder on drive we want to check.</param>
        /// <returns>True if we expect NMM to be able to use the drive in question, otherwise false.</returns>
        private static bool DoesFileSystemSupportSymbolicLinks(string p_strPath)
        {
            if (string.IsNullOrEmpty(p_strPath))
            {
                // Won't matter if there's no path.
                return true;
            }

            // This list can be extended as needed, and is not case sensitive.
            var knownBadFileSystems = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "FAT",
                "FAT32",
                "ReFS",
                "exFAT"
            };

            var file = new FileInfo(p_strPath);
            var drive = new DriveInfo(file.Directory.Root.FullName);

            return !knownBadFileSystems.Contains(drive.DriveFormat);
        }

		#endregion
	}
}
