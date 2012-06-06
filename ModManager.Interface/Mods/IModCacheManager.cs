using Nexus.Client.Util;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// Describes the properties and methods of a mod cache manager.
	/// </summary>
	/// <remarks>
	/// A mod cache manager provides information od methods to use the mod cache.
	/// </remarks>
	public interface IModCacheManager
	{
		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		FileUtil FileUtility { get; }

		/// <summary>
		/// Gets the cache file for the specified mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to get the cache file.</param>
		/// <returns>The cache file for the specified mod, or <c>null</c>
		/// if there is no cache file.</returns>
		Archive GetCacheFile(IMod p_modMod);

		/// <summary>
		/// Creates a cache file for the given mod, containing the specified files.
		/// </summary>
		/// <param name="p_modMod">The mod for which to create the cache file.</param>
		/// <param name="p_strFilesToCacheFolder">The folder containing the files to put into the cache.</param>
		/// <returns>The cache file for the specified mod, or <c>null</c>
		/// if there were no files to cache.</returns>
		Archive CreateCacheFile(IMod p_modMod, string p_strFilesToCacheFolder);
	}
}
