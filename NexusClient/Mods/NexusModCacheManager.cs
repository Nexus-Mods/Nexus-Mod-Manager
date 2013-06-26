using Nexus.Client.Util;
using System.IO;
using System.Collections.Generic;
using SevenZip;
using System;

namespace Nexus.Client.Mods
{
	/// <summary>
	/// The mod cache manager.
	/// </summary>
	/// <remarks>
	/// A mod cache manager provides information od methods to use the mod cache.
	/// </remarks>
	public class NexusModCacheManager : IModCacheManager
	{
		#region IModCacheManager Members

		/// <summary>
		/// Gets the path of the directory where the current Game Mode's mods' cache files are stored.
		/// </summary>
		/// <value>The path of the directory where the current Game Mode's mods' cache files are stored.</value>
		protected string ModCacheDirectory { get; private set; }

		/// <summary>
		/// Gets the path of the directory where the current Game Mode's mod files are stored.
		/// </summary>
		/// <value>The path of the directory where the current Game Mode's mod files are stored.</value>
		protected string ModDirectory { get; private set; }

		/// <summary>
		/// Gets or sets the file utility class.
		/// </summary>
		/// <value>The file utility class.</value>
		public FileUtil FileUtility { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_strModCacheDirectory">The path of the directory where the current Game Mode's mods' cache files are stored.</param>
		/// <param name="p_strModDirectory">The path of the directory where the current Game Mode's mod files are stored.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		public NexusModCacheManager(string p_strModCacheDirectory, string p_strModDirectory, FileUtil p_futFileUtility)
		{
			ModCacheDirectory = p_strModCacheDirectory;
			ModDirectory = p_strModDirectory;
			FileUtility = p_futFileUtility;
			CheckModCache();
		}

		#endregion

		/// <summary>
		/// This removes any old cache files.
		/// </summary>
		private void CheckModCache()
		{
			string[] strCaches = Directory.GetFiles(ModCacheDirectory);
			foreach (string strCache in strCaches)
			{
				string strModPath = Path.Combine(ModDirectory, Path.GetFileNameWithoutExtension(strCache));
				if (!File.Exists(strModPath) || (File.GetLastWriteTimeUtc(strCache) < File.GetLastWriteTimeUtc(strModPath)))
					FileUtil.ForceDelete(strCache);
			}
		}

		/// <summary>
		/// Gets the path to the specified mod's cache file.
		/// </summary>
		/// <param name="p_modMod">The mod for which to get the cache filename.</param>
		/// <returns>The path to the specified mod's cache file.</returns>
		private string GetCacheFilePath(IMod p_modMod)
		{
			return Path.Combine(ModCacheDirectory, Path.GetFileName(p_modMod.Filename) + ".zip");
		}

		/// <summary>
		/// Gets the path to the specified mod's cache file.
		/// </summary>
		/// <param name="p_strPath">The path for which to get the cache file.</param>
		/// <returns>The path to the specified mod's cache file.</returns>
		public string GetCacheFilePath(string p_strPath)
		{
			return Path.Combine(ModCacheDirectory, Path.GetFileName(p_strPath) + ".zip");
		}

		/// <summary>
		/// Gets the cache file for the specified path.
		/// </summary>
		/// <param name="p_strPath">The path for which to get the cache file.</param>
		/// <returns>The cache file for the specified path, or <c>null</c>
		/// if there is no cache file.</returns>
		public Archive GetCacheFile(string p_strPath)
		{
			string strCachePath = Path.Combine(ModCacheDirectory, Path.GetFileName(p_strPath) + ".zip");
			if (File.Exists(strCachePath))
			{
				try
				{
					return new Archive(strCachePath);
				}
				catch (SevenZipArchiveException)
				{
					//the cachef ile is corrupt - so delete it
					FileUtil.ForceDelete(strCachePath);
				}
				catch (UnauthorizedAccessException)
				{
					//we can't access the file - who know why
					//destroy it so we can try again
					FileUtil.ForceDelete(strCachePath);
				}
			}
			return null;
		}

		/// <summary>
		/// Gets the cache file for the specified mod.
		/// </summary>
		/// <param name="p_modMod">The mod for which to get the cache file.</param>
		/// <returns>The cache file for the specified mod, or <c>null</c>
		/// if there is no cache file.</returns>
		public Archive GetCacheFile(IMod p_modMod)
		{

			string strCachePath = GetCacheFilePath(p_modMod);
			if (File.Exists(strCachePath))
			{
				try
				{
					return new Archive(strCachePath);
				}
				catch (SevenZipArchiveException)
				{
					//the cachef ile is corrupt - so delete it
					FileUtil.ForceDelete(strCachePath);
				}
				catch (UnauthorizedAccessException)
				{
					//we can't access the file - who know why
					//destroy it so we can try again
					FileUtil.ForceDelete(strCachePath);
				}
			}
			return null;
		}

		/// <summary>
		/// Creates a cache file for the given mod, containing the specified files.
		/// </summary>
		/// <param name="p_modMod">The mod for which to create the cache file.</param>
		/// <param name="p_strFilesToCacheFolder">The folder containing the files to put into the cache.</param>
		/// <returns>The cache file for the specified mod, or <c>null</c>
		/// if there were no files to cache.</returns>
		public Archive CreateCacheFile(IMod p_modMod, string p_strFilesToCacheFolder)
		{
			if (!String.IsNullOrEmpty(p_strFilesToCacheFolder))
			{
				string[] strFilesToCompress = Directory.GetFiles(p_strFilesToCacheFolder, "*.*", SearchOption.AllDirectories);
				if (strFilesToCompress.Length > 0)
				{
					string strCachePath = GetCacheFilePath(p_modMod);
					SevenZipCompressor szcCompressor = new SevenZipCompressor();
					szcCompressor.ArchiveFormat = OutArchiveFormat.Zip;
					szcCompressor.CompressionLevel = CompressionLevel.Ultra;
					szcCompressor.CompressDirectory(p_strFilesToCacheFolder, strCachePath);
					return new Archive(strCachePath);
				}
			}
			return null;
		}
	}
}
