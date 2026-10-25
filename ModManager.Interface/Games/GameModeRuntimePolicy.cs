using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Client.Games
{
	/// <summary>Optional per-game decisions consumed by shared installation and application code.</summary>
	public sealed class GameModeRuntimePolicy
	{
		public int SchemaVersion { get; set; }
		public string CollectionAdapter { get; set; }
		public string CollectionDomain { get; set; }
		public string[] CollectionMemberTypes { get; set; }
		public GameModeCollectionPluginGroup[] CollectionPluginGroups { get; set; }
		public bool CollectionOrderProvidesMetadata { get; set; }
		public bool RequiresReviewedNativeInstall { get; set; }
		public string NativeRootOwnershipProfile { get; set; }
		public string NativeInstallReviewMessage { get; set; }
		public bool SupportsNestedArchives { get; set; }
		public string[] ConsoleBatchCommands { get; set; }
		public string ConsoleBatchNoArgumentCommand { get; set; }
		public string ConsoleBatchRuleId { get; set; }
		public GameModeStartupNotice StartupNotice { get; set; }
		public GameModeDownloadRouting DownloadRouting { get; set; }
	}

	/// <summary>Declares a catalog's default plugin-group order without game-specific tables in the normalizer.</summary>
	public sealed class GameModeCollectionPluginGroup
	{
		public string Id { get; set; }
		public string[] After { get; set; }
	}

	/// <summary>Declares a one-time version notice using an existing persisted preference.</summary>
	public sealed class GameModeStartupNotice
	{
		public string Preference { get; set; }
		public string MinimumVersion { get; set; }
		public bool IncludeMinimumVersion { get; set; }
		public bool RequiresObsoletePluginConfiguration { get; set; }
		public string Text { get; set; }
	}

	/// <summary>Declares a local download-edition choice without changing game or storage identities.</summary>
	public sealed class GameModeDownloadRouting
	{
		public string Preference { get; set; }
		public string DefaultMode { get; set; }
		public string AlternateMode { get; set; }
		public string FallbackMode { get; set; }
		public bool PreferRememberedMode { get; set; }
		public bool UseRememberedModeAsDefault { get; set; }
		public bool LegacySelectionToggle { get; set; }
		public bool FormatDescription { get; set; }
		public string[] IncomingDomains { get; set; }
		public string Label { get; set; }
		public string Description { get; set; }
		public string LabelKey { get; set; }
		public string IconResource { get; set; }
	}

	/// <summary>Publishes validated definition policies for both data-driven and legacy compiled game modes.</summary>
	public static class GameModeRuntimePolicies
	{
		private static readonly GameModeRuntimePolicy Empty = new GameModeRuntimePolicy();
		private static Dictionary<string, GameModeRuntimePolicy> _policies = new Dictionary<string, GameModeRuntimePolicy>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Atomically replaces the catalog after definition loading has isolated invalid files.</summary>
		public static void Replace(IEnumerable<KeyValuePair<string, GameModeRuntimePolicy>> policies)
		{
			_policies = policies.Where(item => item.Value != null).ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
		}

		/// <summary>Returns neutral defaults for older definitions without changing their saved settings.</summary>
		public static GameModeRuntimePolicy For(string modeId)
		{
			GameModeRuntimePolicy policy;
			return modeId != null && _policies.TryGetValue(modeId, out policy) ? policy : Empty;
		}

		/// <summary>Exposes configured protocol mappings without embedding game identifiers in consumers.</summary>
		public static IReadOnlyDictionary<string, GameModeRuntimePolicy> All => _policies;
	}
}
