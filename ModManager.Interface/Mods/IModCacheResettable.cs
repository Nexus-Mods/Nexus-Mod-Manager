namespace Nexus.Client.Mods
{
	/// <summary>
	/// Supports regenerating generated mod cache data without replacing the mod instance.
	/// </summary>
	public interface IModCacheResettable
	{
		/// <summary>
		/// Invalidates and regenerates generated cache data for this mod.
		/// </summary>
		/// <param name="modCacheManager">The cache manager for the current game mode.</param>
		void ResetCache(IModCacheManager modCacheManager);
	}
}