using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.Games;
using Nexus.Client.Mods;
using Nexus.Client.Util.Collections;

namespace Nexus.Client.ModManagement
{
	/// <summary>
	/// A registry of all mods being managed by the mod manager.
	/// </summary>
	public class ModRegistry
	{
		/// <summary>
		/// Searches for mods in the specified path, and loads
		/// any mods that are found into a registry.
		/// </summary>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_strSearchPath">The path in which to search for mod format assemblies.</param>
		/// <param name="p_booRecurse">Whether to check sub folders of <paramref name="p_strSearchPath"/> for mods.</param>
		/// <param name="p_gmdGameMode">The game mode for which to discover mods.</param>
		/// <param name="p_strExcludedSubDirectories">The list of subdirectories not to examine for mods.</param>
		/// <returns>A registry containing all of the discovered mods.</returns>
		public static ModRegistry DiscoverManagedMods(IModFormatRegistry p_frgFormatRegistry, IModCacheManager p_frgCacheManager, string p_strSearchPath, bool p_booRecurse, IEnvironmentInfo p_eiEnvironmentInfo, IGameMode p_gmdGameMode, params string[] p_strExcludedSubDirectories)
		{
			Trace.TraceInformation("Discovering Managed Mods...");
			Trace.Indent();

			Trace.TraceInformation("Looking in: {0}", p_strSearchPath);

			List<string> lstExludedPaths = new List<string>();
			for (Int32 i = 0; i < p_strExcludedSubDirectories.Length; i++)
				lstExludedPaths.Add((p_strExcludedSubDirectories[i].Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToLower());

			ModRegistry mdrRegistry = new ModRegistry(p_frgFormatRegistry, p_gmdGameMode);
			string[] strMods = Directory.GetFiles(p_strSearchPath, "*", p_booRecurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
			IMod modMod = null;
			List<string> modList = new List<String>();
			bool booExcluded = false;
			foreach (string strMod in strMods)
			{
				string strModPath = strMod.ToLower();
				booExcluded = false;
				foreach (string strExclusion in lstExludedPaths)
					if (strModPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).StartsWith(strExclusion))
					{
						booExcluded = true;
						break;
					}
				if (booExcluded)
					continue;
				Trace.TraceInformation("Found: {0}", strMod);

				try
				{
					string strCachePath = p_frgCacheManager.GetCacheFilePath(strMod);
					modMod = mdrRegistry.CreateMod(strMod, strCachePath, p_gmdGameMode, p_eiEnvironmentInfo);
				}
				catch
				{
					modList.Add(strMod);
					modMod = null;
				}

				if (modMod == null)
					continue;
				mdrRegistry.m_oclRegisteredMods.Add(modMod);
				Trace.Indent();
				Trace.TraceInformation("Registered.");
				Trace.Unindent();
			}

			p_eiEnvironmentInfo.Settings.CacheOverhaulSetup = true;
			p_eiEnvironmentInfo.Settings.Save();

			if (modList.Count > 0)
			{
				string strErrorMessage = "Error loading the following mods: " + Environment.NewLine;
				foreach (string modstr in modList)
				{
					strErrorMessage += modstr + Environment.NewLine;
				}
				MessageBox.Show(strErrorMessage, "Mod loading error", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}

			Trace.Unindent();
			return mdrRegistry;
		}

		private ThreadSafeObservableList<IMod> m_oclRegisteredMods = new ThreadSafeObservableList<IMod>();
		
		#region Properties

		/// <summary>
		/// Gets the list of registered mods.
		/// </summary>
		/// <value>The list of installed mods.</value>
		public ReadOnlyObservableList<IMod> RegisteredMods { get; private set; }

		/// <summary>
		/// Gets or sets the <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.
		/// </summary>
		/// <value>The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</value>
		public IModFormatRegistry FormatRegistry { get; set; }

		/// <summary>
		/// The <see cref="IGameMode"/> for which mods are to be registered.
		/// </summary>
		protected IGameMode GameMode { get; private set; }

		#endregion

		#region Constructors

		/// <summary>
		/// A simple constructor that initializes the object with its dependencies.
		/// </summary>
		/// <param name="p_frgFormatRegistry">The <see cref="IModFormatRegistry"/> that contains the list
		/// of supported <see cref="IModFormat"/>s.</param>
		/// <param name="p_gmdGameMode">The <see cref="IGameMode"/> for which mods are to be registered.</param>
		public ModRegistry(IModFormatRegistry p_frgFormatRegistry, IGameMode p_gmdGameMode)
		{
			GameMode = p_gmdGameMode;
			FormatRegistry = p_frgFormatRegistry;
			RegisteredMods = new ReadOnlyObservableList<IMod>(m_oclRegisteredMods);
		}

		#endregion

		/// <summary>
		/// Creates a mod of the appropriate type from the specified file.
		/// </summary>
		/// <param name="p_strModPath">The path to the mod file.</param>
		/// <param name="p_strCachePath">The path to the cache file.</param>
		/// <param name="p_gmdGameMode">The game mode for which to create the plugin.</param>
		/// <returns>A mod of the appropriate type from the specified file, if the type of hte mod
		/// can be determined; <c>null</c> otherwise.</returns>
		protected IMod CreateMod(string p_strModPath, string p_strCachePath, IGameMode p_gmdGameMode, IEnvironmentInfo p_eiEnvironmentInfo)
		{
			if ((String.IsNullOrEmpty(p_strCachePath)) || (!Directory.Exists(p_strCachePath)))
				p_strCachePath = p_strModPath;

			List<KeyValuePair<FormatConfidence, IModFormat>> lstFormats = new List<KeyValuePair<FormatConfidence, IModFormat>>();
			foreach (IModFormat mftFormat in FormatRegistry.Formats)
				lstFormats.Add(new KeyValuePair<FormatConfidence, IModFormat>(mftFormat.CheckFormatCompliance(p_strCachePath), mftFormat));
			lstFormats.Sort((x, y) => -x.Key.CompareTo(y.Key));
			if (lstFormats[0].Key <= FormatConfidence.Convertible)
				return null;
			return lstFormats[0].Value.CreateMod(p_strModPath, p_gmdGameMode);
		}

		/// <summary>
		/// Registers the specified mod.
		/// </summary>
		/// <param name="p_strModPath">The path to the mod to register.</param>
		/// <returns>The mod that was registered, or <c>null</c> if the mod at the given path
		/// could not be registered.</returns>
		public IMod RegisterMod(string p_strModPath)
		{
			return RegisterMod(p_strModPath, null, null);
		}

		/// <summary>
		/// Registers the specified mod, tagging it with the given info.
		/// </summary>
		/// <param name="p_strModPath">The path to the mod to register.</param>
		/// <param name="p_mifTagInfo">The info with which to tag the mod.</param>
		/// <returns>The mod that was registered, or <c>null</c> if the mod at the given path
		/// could not be registered.</returns>
		public IMod RegisterMod(string p_strModPath, IModInfo p_mifTagInfo, IEnvironmentInfo p_eiEnvironmentInfo)
		{
			Int32 intExistingIndex = -1;
			IMod modMod = null;
			for (intExistingIndex = 0; intExistingIndex < m_oclRegisteredMods.Count; intExistingIndex++)
				if (p_strModPath.Equals(m_oclRegisteredMods[intExistingIndex].Filename, StringComparison.OrdinalIgnoreCase))
					break;
			modMod = CreateMod(p_strModPath, string.Empty, GameMode, p_eiEnvironmentInfo);
			if (p_mifTagInfo != null)
				modMod.UpdateInfo(p_mifTagInfo, false);
			if (modMod == null)
				return null;
			if (intExistingIndex < m_oclRegisteredMods.Count)
				m_oclRegisteredMods[intExistingIndex] = modMod;
			else
				m_oclRegisteredMods.Add(modMod);
			return modMod;
		}

		/// <summary>
		/// Removes the specified mod from the registry.
		/// </summary>
		/// <param name="p_modMod">The mod to unregister.</param>
		public void UnregisterMod(IMod p_modMod)
		{
			m_oclRegisteredMods.Remove(p_modMod);
		}

		/// <summary>
		/// Returns the mod registered with the given path.
		/// </summary>
		/// <param name="p_strModPath">The path of the mod to return</param>
		/// <returns>The mod registered with the given path, or
		/// <c>null</c> if there is no registered mod with the given path.</returns>
		public IMod GetMod(string p_strModPath)
		{
			return (from m in m_oclRegisteredMods
					where m.ModArchivePath.Equals(p_strModPath, StringComparison.OrdinalIgnoreCase)
					select m).FirstOrDefault();
		}

		/// <summary>
		/// Returns the mod registered with the given file name.
		/// </summary>
		/// <param name="p_strFilename">The path of the mod to return</param>
		/// <returns>The mod registered with the given file name, or
		/// <c>null</c> if there is no registered mod with the given file name.</returns>
		public IMod GetModByFilename(string p_strFilename)
		{
			string strFilename = string.Empty;

			if (!string.IsNullOrEmpty(p_strFilename))
			{
				strFilename = Path.GetFileName(p_strFilename);

				return (from m in m_oclRegisteredMods
						where Path.GetFileName(m.Filename).Equals(strFilename, StringComparison.OrdinalIgnoreCase)
						select m).FirstOrDefault();
			}
			else
				return null;
		}

		/// <summary>
		/// Returns the mod registered with the given downloadId.
		/// </summary>
		/// <param name="p_strDownloadID">The path of the mod to return</param>
		/// <returns>The mod registered with the given downloadId, or
		/// <c>null</c> if there is no registered mod with the given downloadId.</returns>
		public IMod GetModByDownloadID(string p_strDownloadID)
		{
			if (!string.IsNullOrEmpty(p_strDownloadID))
			{
				try
				{
					return (from m in m_oclRegisteredMods
							where m.DownloadId.Equals(p_strDownloadID, StringComparison.OrdinalIgnoreCase)
							select m).FirstOrDefault();
				}
				catch { }
			}

			return null;
		}
	}
}
