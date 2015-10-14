using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Nexus.Client.Util;
using Nexus.Client.Util.Collections;
using SevenZip;

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
		public string ModCacheDirectory { get; private set; }

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

		/// <summary>
		/// Gets the application's envrionment info.
		/// </summary>
		/// <value>The application's envrionment info.</value>
		public IEnvironmentInfo EnvironmentInfo { get; private set; }

		protected bool m_booCacheOverhaulSetup = false;

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with the given dependencies.
		/// </summary>
		/// <param name="p_strModCacheDirectory">The path of the directory where the current Game Mode's mods' cache files are stored.</param>
		/// <param name="p_strModDirectory">The path of the directory where the current Game Mode's mod files are stored.</param>
		/// <param name="p_futFileUtility">The file utility class.</param>
		public NexusModCacheManager(string p_strModCacheDirectory, string p_strModDirectory, FileUtil p_futFileUtility, IEnvironmentInfo p_eifEnvironmentInfo)
		{
			ModCacheDirectory = p_strModCacheDirectory;
			ModDirectory = p_strModDirectory;
			FileUtility = p_futFileUtility;
			EnvironmentInfo = p_eifEnvironmentInfo;
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
			return Path.Combine(ModCacheDirectory, Path.GetFileName(p_modMod.ModArchivePath) + ".zip");
		}

		/// <summary>
		/// Gets the path to the specified mod's cache file.
		/// </summary>
		/// <param name="p_strPath">The path for which to get the cache file.</param>
		/// <returns>The path to the specified mod's cache file.</returns>
		public string GetCacheFilePath(string p_strPath)
		{
			return Path.Combine(ModCacheDirectory, Path.GetFileNameWithoutExtension(p_strPath));
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
		public void CreateCacheFile(IMod p_modMod, string p_strFilesToCacheFolder)
		{
			if (!String.IsNullOrEmpty(p_strFilesToCacheFolder))
			{
				var strFilesToCompress = Directory.EnumerateFiles(p_strFilesToCacheFolder, "*.*", SearchOption.AllDirectories);
				if (strFilesToCompress.Count() > 0)
				{
					string strCachePath = Path.Combine(ModCacheDirectory, Path.GetFileNameWithoutExtension(p_modMod.Filename));
					string strArcCacheFile = Path.Combine(ModCacheDirectory, Path.GetFileName(p_modMod.Filename) + ".zip");
					try
					{
						if (!Directory.Exists(strCachePath))
						{
							if (File.Exists(strArcCacheFile))
							{
								ExportCacheArchive(strArcCacheFile, strCachePath, EnvironmentInfo.Settings.CacheOverhaulSetup);
							}
							else
							{
								Directory.CreateDirectory(strCachePath);
								copyDirectory(p_strFilesToCacheFolder, strCachePath);
							}
						}
					}
					catch (FileNotFoundException ex)
					{
					}
				}
			}
		}

		/// <summary>
		/// Migrates the cache zip file for the given mod to the cache folder.
		/// </summary>
		/// <param name="p_modMod">The mod for which to create the cache file.</param>
		public void MigrateCacheFile(IMod p_modMod)
		{
			string strArcCacheFile = Path.Combine(ModCacheDirectory, Path.GetFileName(p_modMod.Filename) + ".zip");
			string strCachePath = Path.Combine(ModCacheDirectory, Path.GetFileNameWithoutExtension(p_modMod.Filename));

			try
			{
				if (!Directory.Exists(strCachePath))
				{
					if (File.Exists(strArcCacheFile))
					{
						if (!EnvironmentInfo.Settings.CacheOverhaulSetup)
						{
							string strMessage = "This new version changes the way NMM handles its cache. This update is designed to make NMM load faster. As a result the old caching system is no longer necessary. " + Environment.NewLine + Environment.NewLine +
							"This prompt is here to ask you what you would like to do with the files on your hard-drive used by NMM for the old caching system. We estimate this system used around 1MB - 100MB of space on your hard-drive, depending on how many mods you have installed in NMM. " + Environment.NewLine + Environment.NewLine +
							"If you select 'Yes' then NMM will delete the old cache files and do some spring cleaning. This will free up the small amount of space being used, but if you switch back to a previous version of NMM from before this update it will take a long time to recreate the cache again. " + Environment.NewLine + Environment.NewLine +
							"If you select 'No' then NMM will not delete the old cache files and they will remain on your hard-drive. The only negative to this, as mentioned, is the fact it'll take up a little extra space on your hard-drive (once again, estimated at 1MB to 100MB of space). " + Environment.NewLine + Environment.NewLine +
							"Would you like NMM to remove your old cache files? "+ Environment.NewLine + Environment.NewLine;

							if (MessageBox.Show(strMessage, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
							{
								m_booCacheOverhaulSetup = true;
							}

							EnvironmentInfo.Settings.CacheOverhaulSetup = true;
							EnvironmentInfo.Settings.Save();
						}

						ExportCacheArchive(strArcCacheFile, strCachePath, m_booCacheOverhaulSetup);
					}
				}
			}
			catch (FileNotFoundException)
			{
			}
		}

		private void ExportCacheArchive(string p_strCacheSource, string p_strDestinationFolder, bool p_booCacheOverhaulSetup)
		{
			try
			{
				ZipFile.ExtractToDirectory(p_strCacheSource, p_strDestinationFolder);
			}
			catch { }

			if (p_booCacheOverhaulSetup)
				FileUtil.ForceDelete(p_strCacheSource);
		}

		public static void copyDirectory(string strSource, string strDestination)
		{
			String[] Files;

			if (strDestination[strDestination.Length - 1] != Path.DirectorySeparatorChar)
				strDestination += Path.DirectorySeparatorChar;
			if (!Directory.Exists(strDestination)) Directory.CreateDirectory(strDestination);
			Files = Directory.GetFileSystemEntries(strSource);
			foreach (string Element in Files)
			{
				// Sub directories
				if (Directory.Exists(Element))
					copyDirectory(Element, strDestination + Path.GetFileName(Element));
				// Files in directory
				else
					File.Copy(Element, strDestination + Path.GetFileName(Element), true);
			}
		}
	}
}
