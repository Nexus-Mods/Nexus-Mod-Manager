using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Nexus.Client.Games
{
	/// <summary>A display-only snapshot of one game's last managed profile; never authoritative installation state.</summary>
	internal sealed class GameDiscoveryStatistics
	{
		[JsonProperty(Required = Required.Always)]
		public string InstallationPath { get; set; }
		[JsonProperty(Required = Required.Always)]
		public string InstallInfoPath { get; set; }
		public string ProfileName { get; set; }
		[JsonProperty(Required = Required.Always)]
		public DateTime UpdatedUtc { get; set; }
		[JsonProperty(Required = Required.Always)]
		public int ActiveMods { get; set; }
		[JsonProperty(Required = Required.Always)]
		public int ManagedMods { get; set; }
		public int? AppliedCollections { get; set; }
		public int? ManagedCollections { get; set; }
		public int? ActivePlugins { get; set; }
		public int? ManagedPlugins { get; set; }

		/// <summary>Rejects partial or invalid cache entries instead of displaying invented zero counts.</summary>
		internal bool IsValid => UpdatedUtc.Kind == DateTimeKind.Utc && UpdatedUtc > DateTime.MinValue &&
			!String.IsNullOrWhiteSpace(InstallationPath) && !String.IsNullOrWhiteSpace(InstallInfoPath) &&
			ActiveMods >= 0 && ManagedMods >= ActiveMods && ValidPair(AppliedCollections, ManagedCollections) &&
			ValidPair(ActivePlugins, ManagedPlugins);

		/// <summary>Checks an optional pair of known counts.</summary>
		private static bool ValidPair(int? active, int? managed)
		{
			return (!active.HasValue && !managed.HasValue) ||
				(active.HasValue && managed.HasValue && active.Value >= 0 && managed.Value >= active.Value);
		}
	}

	/// <summary>Loads one small optional cache at startup and saves snapshots without opening other game modes or storage.</summary>
	internal sealed class GameDiscoveryStatisticsCache
	{
		private const int SchemaVersion = 1;
		private readonly string _path;
		private readonly Dictionary<string, GameDiscoveryStatistics> _games = new Dictionary<string, GameDiscoveryStatistics>(StringComparer.OrdinalIgnoreCase);
		private bool _writable = true;
		private bool _dirty;

		/// <summary>Reads only the display cache; missing and corrupt entries remain unknown.</summary>
		internal GameDiscoveryStatisticsCache(string applicationDataDirectory)
		{
			_path = Path.Combine(applicationDataDirectory, "game-discovery-statistics.json");
			try
			{
				FileInfo file = new FileInfo(_path);
				if (!file.Exists) return;
				if (file.Length > 1024 * 1024) { _writable = false; return; }
				JObject document = JObject.Parse(File.ReadAllText(_path));
				if ((int?)document["schemaVersion"] != SchemaVersion) { _writable = false; return; }
				JObject games = document["games"] as JObject;
				if (games == null) return;
				foreach (JProperty item in games.Properties())
				{
					try
					{
						GameDiscoveryStatistics snapshot = item.Value.ToObject<GameDiscoveryStatistics>();
						if (!String.IsNullOrWhiteSpace(item.Name) && snapshot != null && snapshot.IsValid)
							_games[item.Name] = snapshot;
					}
					catch (JsonException error) { Trace.TraceWarning("Ignoring game summary {0}: {1}", item.Name, error.Message); }
				}
			}
			catch (Exception error) when (IsCacheError(error)) { Trace.TraceWarning("Could not read game summaries: {0}", error.Message); }
		}

		/// <summary>Uses counts only for the same configured installation and storage.</summary>
		internal GameDiscoveryStatistics Get(GameDiscoveryEntry entry)
		{
			GameDiscoveryStatistics snapshot;
			return entry != null && entry.Collection == GameDiscoveryCollection.Managed && _games.TryGetValue(entry.ModeId, out snapshot) &&
				SamePath(snapshot.InstallationPath, entry.SavedInstallationPath) ? snapshot : null;
		}

		/// <summary>Rejects a snapshot from a different InstallInfo folder after explicit storage reconfiguration.</summary>
		internal void ValidateStorage(string modeId, string installInfoPath)
		{
			GameDiscoveryStatistics snapshot;
			if (_games.TryGetValue(modeId, out snapshot) && !SamePath(snapshot.InstallInfoPath, installInfoPath))
			{
				_games.Remove(modeId);
				_dirty = true;
			}
		}

		/// <summary>Updates in-memory counts; disk writes are deferred to session boundaries.</summary>
		internal void Set(string modeId, GameDiscoveryStatistics snapshot)
		{
			if (String.IsNullOrWhiteSpace(modeId) || snapshot == null || !snapshot.IsValid) return;
			_games[modeId] = snapshot;
			_dirty = true;
		}

		/// <summary>Atomically writes optional display data; failure cannot interrupt game management.</summary>
		internal void Save()
		{
			if (!_dirty || !_writable) return;
			string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_path));
				File.WriteAllText(temporary, JsonConvert.SerializeObject(new CacheDocument { Games = _games }, Formatting.Indented));
				if (File.Exists(_path)) File.Replace(temporary, _path, null);
				else File.Move(temporary, _path);
				_dirty = false;
			}
			catch (Exception error) when (IsCacheError(error)) { Trace.TraceWarning("Could not save game summaries: {0}", error.Message); }
			finally
			{
				try { if (File.Exists(temporary)) File.Delete(temporary); }
				catch (Exception error) when (IsCacheError(error)) { Trace.TraceWarning("Could not remove temporary game summary: {0}", error.Message); }
			}
		}

		/// <summary>Compares saved identities without probing their filesystems.</summary>
		private static bool SamePath(string first, string second) => String.Equals(first?.TrimEnd('\\', '/'), second?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

		/// <summary>Identifies failures confined to this disposable cache.</summary>
		private static bool IsCacheError(Exception error) => error is IOException || error is UnauthorizedAccessException ||
			error is SecurityException || error is JsonException || error is ArgumentException || error is FormatException || error is OverflowException;

		/// <summary>Versions the display cache independently of authoritative settings and installation data.</summary>
		private sealed class CacheDocument
		{
			[JsonProperty("schemaVersion")]
			public int Version { get; set; } = SchemaVersion;
			[JsonProperty("games")]
			public Dictionary<string, GameDiscoveryStatistics> Games { get; set; }
		}
	}
}
