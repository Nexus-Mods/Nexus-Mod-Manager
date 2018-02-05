using System;

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
            InstalledOnSuitableFileSystem = Util.FileUtil.DoesFileSystemSupportSymbolicLinks(p_strInstallationPath);            
        }        

		#endregion
	}
}
