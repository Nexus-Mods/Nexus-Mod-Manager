using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Nexus.Client.Settings;

namespace Nexus.Client.Games
{
	/// <summary>The three mutually exclusive sections of the main-window game catalog.</summary>
	public enum GameDiscoveryCollection
	{
		Managed,
		ToConfigure,
		Supported
	}

	/// <summary>Read-only evidence of an installation. Finding a game never configures its storage.</summary>
	public sealed class GameDiscoveryInstallationCandidate
	{
		public GameDiscoveryInstallationCandidate(string modeId, string gameRoot, string source)
		{
			if (String.IsNullOrWhiteSpace(modeId)) throw new ArgumentException("A game mode is required.", nameof(modeId));
			if (String.IsNullOrWhiteSpace(gameRoot)) throw new ArgumentException("A game root is required.", nameof(gameRoot));
			ModeId = modeId;
			GameRoot = gameRoot;
			Source = source ?? String.Empty;
		}

		public string ModeId { get; }
		public string GameRoot { get; }
		public string Source { get; }
	}

	/// <summary>Describes a supported game without constructing a game mode or touching storage.</summary>
	public sealed class GameDiscoveryEntry
	{
		internal GameDiscoveryEntry(string modeId, string name, GameDiscoveryCollection collection,
			string savedInstallationPath, string savedExecutablePath, bool completedSetup,
			bool hasLegacyStoragePaths, bool needsAttention, bool startupDefault,
			IReadOnlyList<GameDiscoveryInstallationCandidate> candidates, DateTime? lastPlayedUtc = null)
		{
			ModeId = modeId;
			Name = name;
			Collection = collection;
			SavedInstallationPath = savedInstallationPath;
			SavedExecutablePath = savedExecutablePath;
			CompletedSetup = completedSetup;
			HasLegacyStoragePaths = hasLegacyStoragePaths;
			NeedsAttention = needsAttention;
			IsStartupDefault = startupDefault;
			Candidates = candidates;
			LastPlayedUtc = lastPlayedUtc;
		}

		public string ModeId { get; }
		public string Name { get; }
		public GameDiscoveryCollection Collection { get; }
		public string SavedInstallationPath { get; }
		public string SavedExecutablePath { get; }
		public bool CompletedSetup { get; }
		public bool HasLegacyStoragePaths { get; }
		/// <summary>Shallow settings evidence only; not a filesystem/storage health verdict.</summary>
		public bool NeedsAttention { get; }
		public bool IsStartupDefault { get; }
		public IReadOnlyList<GameDiscoveryInstallationCandidate> Candidates { get; }
		/// <summary>Last successful game-mode entry or game launch through NMM, or null when no valid history is available.</summary>
		public DateTime? LastPlayedUtc { get; }
	}

	public sealed class GameDiscoveryCatalog
	{
		internal GameDiscoveryCatalog(IReadOnlyList<GameDiscoveryEntry> entries, IReadOnlyList<string> unavailableSavedModeIds)
		{
			Entries = entries;
			UnavailableSavedModeIds = unavailableSavedModeIds;
		}

		public IReadOnlyList<GameDiscoveryEntry> Entries { get; }
		/// <summary>Historical settings for unsupported modes stay untouched and are surfaced separately.</summary>
		public IReadOnlyList<string> UnavailableSavedModeIds { get; }
		public IEnumerable<GameDiscoveryEntry> Managed => Entries.Where(e => e.Collection == GameDiscoveryCollection.Managed);
		public IEnumerable<GameDiscoveryEntry> ToConfigure => Entries.Where(e => e.Collection == GameDiscoveryCollection.ToConfigure);
		public IEnumerable<GameDiscoveryEntry> Supported => Entries.Where(e => e.Collection == GameDiscoveryCollection.Supported);
	}

	/// <summary>
	/// Projects legacy settings and verified candidate evidence into the three UI sections.
	/// In particular it does not consult descriptor.InstallationPath: some descriptors create
	/// directories or save settings when that property is read.
	/// </summary>
	public static class GameDiscoveryCatalogBuilder
	{
		public static GameDiscoveryCatalog Build(IEnumerable<IGameModeDescriptor> supportedModes, ISettings settings,
			IEnumerable<GameDiscoveryInstallationCandidate> verifiedCandidates = null)
		{
			if (supportedModes == null) throw new ArgumentNullException(nameof(supportedModes));
			if (settings == null) throw new ArgumentNullException(nameof(settings));

			var modes = supportedModes.Where(mode => mode != null && !String.IsNullOrWhiteSpace(mode.ModeId))
				.GroupBy(mode => mode.ModeId, StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First())
				.OrderBy(mode => mode.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(mode => mode.ModeId, StringComparer.OrdinalIgnoreCase).ToList();
			var knownIds = new HashSet<string>(modes.Select(mode => mode.ModeId), StringComparer.OrdinalIgnoreCase);
			var installedIds = new HashSet<string>(settings.InstalledGames == null
				? Enumerable.Empty<string>() : settings.InstalledGames.Cast<string>().Where(id => !String.IsNullOrWhiteSpace(id)),
				StringComparer.OrdinalIgnoreCase);
			var byMode = (verifiedCandidates ?? Enumerable.Empty<GameDiscoveryInstallationCandidate>())
				.Where(candidate => candidate != null && !String.IsNullOrWhiteSpace(candidate.GameRoot))
				.GroupBy(candidate => candidate.ModeId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

			var result = new List<GameDiscoveryEntry>();
			foreach (IGameModeDescriptor mode in modes)
			{
				string id = mode.ModeId;
				string installedPath = GetValue(settings.InstallationPaths, id);
				string executablePath = GetValue(settings.ExecutablePaths, id);
				bool completedSetup = GetValue(settings.CompletedSetup, id);
				string installInfo = GetValue(settings.InstallInfoFolder, id);
				string modFolder = GetValue(settings.ModFolder, id);
				string virtualFolder = GetValue(settings.VirtualFolder, id);
				string linkFolder = GetValue(settings.HDLinkFolder, id);
				bool hasAnyStoragePath = !String.IsNullOrWhiteSpace(installInfo) ||
					!String.IsNullOrWhiteSpace(modFolder) || !String.IsNullOrWhiteSpace(virtualFolder) ||
					!String.IsNullOrWhiteSpace(linkFolder);
				bool needsLink = GetValue(settings.MultiHDInstall, id) || AreOnDifferentDrives(virtualFolder, installedPath);
				bool legacyStorageComplete = !String.IsNullOrWhiteSpace(installInfo) &&
					!String.IsNullOrWhiteSpace(modFolder) && !String.IsNullOrWhiteSpace(virtualFolder) &&
					(!needsLink || !String.IsNullOrWhiteSpace(linkFolder));

				List<GameDiscoveryInstallationCandidate> discovered;
				byMode.TryGetValue(id, out discovered);
				var candidates = new List<GameDiscoveryInstallationCandidate>();
				if (!String.IsNullOrWhiteSpace(executablePath))
					candidates.Add(new GameDiscoveryInstallationCandidate(id, executablePath, "Saved"));
				if (discovered != null)
					foreach (var candidate in discovered)
						if (!candidates.Any(existing => SamePath(existing.GameRoot, candidate.GameRoot)))
							candidates.Add(candidate);

				bool managed = completedSetup || legacyStorageComplete;
				GameDiscoveryCollection collection = managed ? GameDiscoveryCollection.Managed :
					(installedIds.Contains(id) || candidates.Count != 0 || !String.IsNullOrWhiteSpace(installedPath) ||
					 hasAnyStoragePath ? GameDiscoveryCollection.ToConfigure : GameDiscoveryCollection.Supported);
				// Missing paths and incomplete old setups remain visible; do not manufacture replacements.
				bool needsAttention = (managed && (String.IsNullOrWhiteSpace(executablePath) ||
					String.IsNullOrWhiteSpace(installedPath))) ||
					(collection == GameDiscoveryCollection.ToConfigure && candidates.Count == 0) ||
					(hasAnyStoragePath && !legacyStorageComplete && !completedSetup);
				bool startupDefault = settings.RememberGameMode &&
					String.Equals(settings.RememberedGameMode, id, StringComparison.OrdinalIgnoreCase);
				result.Add(new GameDiscoveryEntry(id, mode.Name, collection, installedPath,
					executablePath, completedSetup, legacyStorageComplete, needsAttention, startupDefault,
					candidates.AsReadOnly(), GetLastPlayedUtc(settings.LastGameLaunchUtc, id)));
			}

			var savedIds = new HashSet<string>(installedIds, StringComparer.OrdinalIgnoreCase);
			AddKeys(savedIds, settings.InstallationPaths);
			AddKeys(savedIds, settings.ExecutablePaths);
			AddKeys(savedIds, settings.CompletedSetup);
			AddKeys(savedIds, settings.InstallInfoFolder);
			AddKeys(savedIds, settings.ModFolder);
			AddKeys(savedIds, settings.VirtualFolder);
			AddKeys(savedIds, settings.HDLinkFolder);
			AddKeys(savedIds, settings.MultiHDInstall);
			if (settings.RememberGameMode && !String.IsNullOrWhiteSpace(settings.RememberedGameMode))
				savedIds.Add(settings.RememberedGameMode);
			var unknown = savedIds.Where(id => !knownIds.Contains(id))
				.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
			// Only managed games use launch history; other sections retain their alphabetical order.
			var ordered = result.OrderByDescending(entry => entry.Collection == GameDiscoveryCollection.Managed ? entry.LastPlayedUtc : null)
				.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(entry => entry.ModeId, StringComparer.OrdinalIgnoreCase).ToList();
			return new GameDiscoveryCatalog(ordered.AsReadOnly(), unknown);
		}

		/// <summary>Treats absent, empty or invalid timestamps as unknown without changing saved settings.</summary>
		private static DateTime? GetLastPlayedUtc(IDictionary<string, string> values, string id)
		{
			DateTime timestamp;
			return DateTime.TryParseExact(GetValue(values, id), "O", CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind, out timestamp) && timestamp.Kind == DateTimeKind.Utc && timestamp > DateTime.MinValue
				? timestamp : (DateTime?)null;
		}

		private static void AddKeys<T>(ISet<string> ids, IDictionary<string, T> values)
		{
			if (values == null) return;
			foreach (string id in values.Keys)
				if (!String.IsNullOrWhiteSpace(id)) ids.Add(id);
		}

		private static string GetValue(IDictionary<string, string> values, string id)
		{
			string value;
			return values != null && values.TryGetValue(id, out value) ? value : null;
		}

		private static bool GetValue(IDictionary<string, bool> values, string id)
		{
			bool value;
			return values != null && values.TryGetValue(id, out value) && value;
		}

		private static bool SamePath(string first, string second)
		{
			return String.Equals(first.TrimEnd('\\', '/'), second.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
		}

		private static bool AreOnDifferentDrives(string virtualFolder, string gameRoot)
		{
			if (String.IsNullOrWhiteSpace(virtualFolder) || String.IsNullOrWhiteSpace(gameRoot)) return false;
			try
			{
				string first = Path.GetPathRoot(virtualFolder);
				string second = Path.GetPathRoot(gameRoot);
				return !String.IsNullOrWhiteSpace(first) && !String.IsNullOrWhiteSpace(second) &&
					!String.Equals(first, second, StringComparison.OrdinalIgnoreCase);
			}
			catch (ArgumentException) { return false; }
			catch (NotSupportedException) { return false; }
		}
	}
}
