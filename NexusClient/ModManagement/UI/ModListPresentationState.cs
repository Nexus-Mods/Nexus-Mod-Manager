namespace Nexus.Client.ModManagement.UI
{
	using System;
	using System.Collections.Generic;
	using System.Globalization;
	using System.IO;

	using Nexus.Client.Mods;

	/// <summary>
	/// Caches semantic mod-list state shared by all Mod Manager presentation surfaces.
	/// This class deliberately contains no GridView/TreeList or drawing concerns.
	/// </summary>
	internal sealed class ModListPresentationState
	{
		private readonly HashSet<string> _activeModFileNames =
			new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<IMod> _installedMods =
			new HashSet<IMod>();
		private readonly Dictionary<IMod, ModVisualStatus> _modVisualStatusCache =
			new Dictionary<IMod, ModVisualStatus>();
		private readonly Dictionary<IMod, bool> _outdatedModCache =
			new Dictionary<IMod, bool>();
		private readonly Dictionary<IMod, string> _categoryNameCache =
			new Dictionary<IMod, string>();
		private readonly Dictionary<string, bool> _missingArchiveByFileName =
			new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		private readonly object _missingArchiveLock = new object();
		private ModManagerVM _viewModel;

		/// <summary>
		/// Attaches the state cache to a Mod Manager view model and clears state derived from any previous view model.
		/// </summary>
		public void Attach(ModManagerVM viewModel)
		{
			_viewModel = viewModel;
			Clear();
		}

		/// <summary>
		/// Detaches the current view model and clears all cached presentation state.
		/// </summary>
		public void Detach()
		{
			_viewModel = null;
			Clear();
		}

		/// <summary>
		/// Clears all cached activation, version, category and archive state.
		/// </summary>
		public void Clear()
		{
			_activeModFileNames.Clear();
			_installedMods.Clear();
			_modVisualStatusCache.Clear();
			_outdatedModCache.Clear();
			_categoryNameCache.Clear();
			lock (_missingArchiveLock)
				_missingArchiveByFileName.Clear();
		}

		/// <summary>
		/// Clears cached values that can be recomputed directly from the current mod metadata.
		/// </summary>
		public void ClearDerivedCaches()
		{
			_outdatedModCache.Clear();
			_categoryNameCache.Clear();
		}

		/// <summary>
		/// Clears cached category names after category definitions or assignments change.
		/// </summary>
		public void ClearCategoryCache()
		{
			_categoryNameCache.Clear();
		}

		/// <summary>
		/// Invalidates cached values derived from metadata for a single mod.
		/// </summary>
		public void InvalidateMod(IMod mod)
		{
			if (mod == null)
				return;

			_outdatedModCache.Remove(mod);
			_categoryNameCache.Remove(mod);
		}

		/// <summary>
		/// Rebuilds installed and active-mod lookup sets from the current view model.
		/// </summary>
		public void RebuildActivationState()
		{
			_activeModFileNames.Clear();
			_installedMods.Clear();
			_modVisualStatusCache.Clear();

			if (_viewModel == null)
				return;

			// The activator list represents files currently linked into the game, while
			// ActiveMods identifies mods installed in the current profile. Keeping both
			// sets allows the UI to distinguish InstalledUnlinked from InstalledActive.
			foreach (string fileName in _viewModel.VirtualModActivator.ActiveModList)
			{
				if (!String.IsNullOrWhiteSpace(fileName))
					_activeModFileNames.Add(fileName);
			}

			foreach (IMod mod in _viewModel.ActiveMods)
			{
				if (mod != null)
					_installedMods.Add(mod);
			}
		}

		/// <summary>
		/// Determines whether a mod is installed and currently linked into the active deployment.
		/// </summary>
		public bool IsModActive(IMod mod)
		{
			return GetModVisualStatus(mod) == ModVisualStatus.InstalledActive;
		}

		/// <summary>
		/// Determines whether a mod belongs to the current installed-mod collection.
		/// </summary>
		public bool IsModInstalled(IMod mod)
		{
			return mod != null && _installedMods.Contains(mod);
		}

		/// <summary>
		/// Gets the cached visual installation state used consistently by all Mods surfaces.
		/// </summary>
		public ModVisualStatus GetModVisualStatus(IMod mod)
		{
			if (mod == null)
				return ModVisualStatus.Uninstalled;

			ModVisualStatus status;
			if (_modVisualStatusCache.TryGetValue(mod, out status))
				return status;

			bool installed = IsModInstalled(mod);
			bool linked = installed &&
				!String.IsNullOrEmpty(mod.Filename) &&
				_activeModFileNames.Contains(Path.GetFileName(mod.Filename));

			status = linked
				? ModVisualStatus.InstalledActive
				: installed
					? ModVisualStatus.InstalledUnlinked
					: ModVisualStatus.Uninstalled;
			_modVisualStatusCache[mod] = status;
			return status;
		}

		/// <summary>
		/// Determines whether the last known repository version differs from or supersedes the local version.
		/// </summary>
		public bool IsModOutdated(IMod mod)
		{
			if (mod == null)
				return false;

			bool outdated;
			if (!_outdatedModCache.TryGetValue(mod, out outdated))
			{
				outdated = IsVersionOutdated(
					mod.HumanReadableVersion,
					mod.LastKnownVersion);
				_outdatedModCache[mod] = outdated;
			}

			return outdated;
		}

		/// <summary>
		/// Resolves and caches the effective category name for a mod.
		/// </summary>
		public string GetCategoryName(IMod mod)
		{
			if (mod == null)
				return String.Empty;

			string categoryName;
			if (_categoryNameCache.TryGetValue(mod, out categoryName))
				return categoryName;

			if (_viewModel?.CategoryManager != null)
			{
				IModCategory category = _viewModel.CategoryManager.FindCategory(
					mod.CustomCategoryId >= 0
						? mod.CustomCategoryId
						: mod.CategoryId);
				categoryName = category?.CategoryName ?? String.Empty;
			}
			else
			{
				categoryName = Convert.ToString(
					mod.CategoryId,
					CultureInfo.InvariantCulture);
			}

			_categoryNameCache[mod] = categoryName;
			return categoryName;
		}

		/// <summary>
		/// Merges background archive-existence scan results into the thread-safe lookup cache.
		/// </summary>
		public void SetMissingArchiveResults(IDictionary<string, bool> results)
		{
			if (results == null)
				return;

			lock (_missingArchiveLock)
			{
				foreach (KeyValuePair<string, bool> item in results)
					_missingArchiveByFileName[item.Key] = item.Value;
			}
		}

		/// <summary>
		/// Determines whether the source archive for a mod was reported missing by the latest scan.
		/// </summary>
		public bool IsModArchiveMissing(IMod mod)
		{
			if (mod == null || String.IsNullOrEmpty(mod.Filename))
				return false;

			lock (_missingArchiveLock)
			{
				bool missing;
				return _missingArchiveByFileName.TryGetValue(mod.Filename, out missing) && missing;
			}
		}

		/// <summary>
		/// Compares local and repository version strings, using System.Version when possible and a normalized string fallback otherwise.
		/// </summary>
		private static bool IsVersionOutdated(string local, string latest)
		{
			if (String.IsNullOrEmpty(local) || String.IsNullOrEmpty(latest))
				return false;

			string localNorm = local.TrimStart('v', 'V').Trim();
			string latestNorm = latest.TrimStart('v', 'V').Trim();
			Version localVersion;
			Version latestVersion;
			// Prefer semantic version ordering when both values are parseable. Repository
			// versions are not guaranteed to be System.Version-compatible, so preserve the
			// historical normalized-string comparison as a deterministic fallback.
			if (Version.TryParse(localNorm, out localVersion) &&
				Version.TryParse(latestNorm, out latestVersion))
			{
				return localVersion < latestVersion;
			}

			return !String.Equals(localNorm, latestNorm, StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Describes the installation/deployment state rendered by the Mods list surfaces.
	/// </summary>
	internal enum ModVisualStatus
	{
		Uninstalled,
		InstalledUnlinked,
		InstalledActive
	}
}
