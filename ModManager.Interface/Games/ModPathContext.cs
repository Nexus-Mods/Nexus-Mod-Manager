namespace Nexus.Client.Games
{
	/// <summary>
	/// Identifies the path space used when a game mode adjusts mod archive paths.
	/// </summary>
	public enum ModPathContext
	{
		/// <summary>
		/// A path that will be resolved against the game's installation path.
		/// </summary>
		GameInstall,

		/// <summary>
		/// A path that will be resolved against NMM's virtual install storage.
		/// </summary>
		VirtualStorage
	}
}
