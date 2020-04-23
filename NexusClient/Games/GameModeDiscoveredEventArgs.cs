namespace Nexus.Client.Games
{
    using System;

	/// <summary>
	/// An event arguments class that indicates a possible installation path of
	/// a game mode has been found.
	/// </summary>
	public class GameModeDiscoveredEventArgs : EventArgs
	{
        /// <summary>
		/// Gets the game mode info for the game for which an installation path has been found.
		/// </summary>
		/// <value>The game mode info for the game for which an installation path has been found.</value>
		public IGameModeDescriptor GameMode { get; }

		/// <summary>
		/// Gets the installation path that was found.
		/// </summary>
		/// <value>The installation path that was found.</value>
		public string InstallationPath { get; }

        /// <summary>
        /// Gets a value determining whether or not the game is installed on a file system that supports symbolic linking.
        /// </summary>
        public bool InstalledOnSuitableFileSystem { get; }

		/// <summary>
		/// A simple constructor that initializes the object with the given values.
		/// </summary>
		/// <param name="gameModeDescriptor">The game mode info for the game for which an installation path has been found.</param>
		/// <param name="installationPath">The installation path that was found.</param>
		public GameModeDiscoveredEventArgs(IGameModeDescriptor gameModeDescriptor, string installationPath)
		{
			GameMode = gameModeDescriptor;
			InstallationPath = installationPath;
            InstalledOnSuitableFileSystem = Util.FileUtil.DoesFileSystemSupportSymbolicLinks(installationPath);            
        }
	}
}
