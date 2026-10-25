using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Nexus.Client.CollectionManagement;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;

namespace Nexus.Client.OnlineServices.NexusMods.Collections
{
	/// <summary>Selects manifest extension behavior from the configured catalog profile.</summary>
	internal class CollectionGameManifestAdapter
	{
		/// <summary>Resolves manifest behavior independently of local game IDs.</summary>
		internal static CollectionGameManifestAdapter ForManifest(JObject root)
		{
			GameModeRuntimeConfiguration.EnsureLoaded();
			string domain = (string)(root["info"] as JObject)?["domainName"];
			GameModeRuntimePolicy policy = GameModeRuntimePolicies.All.Values.FirstOrDefault(value => value.CollectionDomain != null &&
				String.Equals(value.CollectionDomain, domain, StringComparison.OrdinalIgnoreCase));
			switch (policy?.CollectionAdapter)
			{
				case "module-settings-v1": return new BaldursGate3ManifestAdapter();
				case "component-settings-v1": return new Witcher3ManifestAdapter();
				case "plugin-order-v1": return new StarfieldManifestAdapter();
				default: return new CollectionGameManifestAdapter();
			}
		}
		/// <summary>Uses configured extension types before applying ordinary root-type mappings.</summary>
		internal static bool SupportsMemberType(string domain, string type)
		{
			GameModeRuntimeConfiguration.EnsureLoaded();
			return GameModeRuntimePolicies.All.Values.Any(policy => String.Equals(policy.CollectionDomain, domain, StringComparison.OrdinalIgnoreCase) &&
				policy.CollectionMemberTypes?.Contains(type, StringComparer.Ordinal) == true);
		}
		/// <summary>Retains extension-owned settings in the member recipe identity.</summary>
		internal virtual JToken IncludeRecipeMetadata(JObject root, int ordinal, JToken recipe) => recipe;
		/// <summary>Validates characterized extension fields and returns their names.</summary>
		internal virtual ISet<string> Validate(JObject root, List<CollectionCapabilityIssue> issues) => new HashSet<string>(StringComparer.Ordinal);
		/// <summary>Translates an extension's plugin order into native intent.</summary>
		internal virtual bool NormalizeOrder(JObject root, ref List<CollectionDesiredPluginState> states,
			List<CollectionPluginRelativeOrderRule> rules, List<CollectionCapabilityIssue> issues) => false;
	}

	/// <summary>Preserves BG3 module order and existing persisted recipe identities.</summary>
	internal sealed class BaldursGate3ManifestAdapter : CollectionGameManifestAdapter
	{
		/// <inheritdoc />
		internal override JToken IncludeRecipeMetadata(JObject root, int ordinal, JToken recipe) => ordinal == 0 && root["loadOrder"] is JArray && root["loadOrder"].HasValues
			? new JObject { ["member"] = recipe, ["bg3LoadOrder"] = root["loadOrder"].DeepClone() } : recipe;
		/// <inheritdoc />
		internal override ISet<string> Validate(JObject root, List<CollectionCapabilityIssue> issues)
		{
			try { CollectionBaldursGate3Data.ReadLoadOrder(root); }
			catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is InvalidCastException)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported, "manifest.bg3-load-order-invalid", ex.Message, "$.loadOrder"));
			}
			return new HashSet<string>(new[] { "loadOrder" }, StringComparer.Ordinal);
		}
	}

	/// <summary>Validates Witcher order and merged menu payloads.</summary>
	internal sealed class Witcher3ManifestAdapter : CollectionGameManifestAdapter
	{
		/// <inheritdoc />
		internal override ISet<string> Validate(JObject root, List<CollectionCapabilityIssue> issues)
		{
			try { CollectionWitcher3Data.Parse(root); }
			catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is OverflowException)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported, "manifest.witcher3-data-invalid", ex.Message, "$"));
			}
			return new HashSet<string>(new[] { "loadOrder", "mergedData" }, StringComparer.Ordinal);
		}
	}

	/// <summary>Interprets Starfield's file-based plugin order.</summary>
	internal sealed class StarfieldManifestAdapter : CollectionGameManifestAdapter
	{
		/// <inheritdoc />
		internal override ISet<string> Validate(JObject root, List<CollectionCapabilityIssue> issues)
		{
			if (root["extensionVersion"] != null && (root["extensionVersion"].Type != JTokenType.String || String.IsNullOrWhiteSpace((string)root["extensionVersion"])))
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.starfield-extension-version-invalid", "Starfield extensionVersion must be a non-empty string.", "$.extensionVersion"));
			return new HashSet<string>(new[] { "loadOrder", "extensionVersion" }, StringComparer.Ordinal);
		}
		/// <summary>Translates Starfield's file-based load order into native activation and relative-order intent.</summary>
		internal override bool NormalizeOrder(JObject root, ref List<CollectionDesiredPluginState> pluginStates,
			List<CollectionPluginRelativeOrderRule> rules, List<CollectionCapabilityIssue> issues)
		{
			if (root["loadOrder"] == null) return false;
			JArray entries = root["loadOrder"] as JArray;
			if (entries == null || entries.Count > 512)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.starfield-load-order-invalid", "Starfield loadOrder must be an array of at most 512 plugin entries.", "$.loadOrder"));
				return false;
			}
			if (entries.Count == 0) return false;

			// Starfield exports a sparse file-based list independently of the Gamebryo plugins section.
			// An empty plugins array must not turn every other contributed plugin off.
			bool partial = pluginStates == null || pluginStates.Count == 0;
			pluginStates = pluginStates ?? new List<CollectionDesiredPluginState>();
			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var orderedNames = new List<string>();
			var fields = new HashSet<string>(new[] { "id", "name", "enabled", "modId", "locked", "data" }, StringComparer.Ordinal);
			for (int index = 0; index < entries.Count; index++)
			{
				string path = "$.loadOrder[" + index.ToString(CultureInfo.InvariantCulture) + "]";
				JObject entry = entries[index] as JObject;
				string name = null;
				string id = null;
				bool valid = entry != null && NexusCollectionManifestNormalizer.TryReadPluginRuleFileName(entry["name"], out name) &&
					NexusCollectionManifestNormalizer.TryReadPluginRuleFileName(entry["id"], out id) && StringComparer.OrdinalIgnoreCase.Equals(name, id) &&
					name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
					new[] { ".esm", ".esp", ".esl" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) &&
					entry["enabled"] != null && entry["enabled"].Type == JTokenType.Boolean;
				if (valid)
				{
					valid = !entry.Properties().Any(x => !fields.Contains(x.Name)) &&
						(entry["modId"] == null || entry["modId"].Type == JTokenType.String) &&
						(entry["locked"] == null || entry["locked"].Type == JTokenType.Boolean);
					JToken data = entry["data"];
					if (data != null)
					{
						JObject metadata = data as JObject;
						valid = valid && metadata != null && !metadata.Properties().Any(x => x.Name != "isInvalid") &&
							(metadata["isInvalid"] == null || (metadata["isInvalid"].Type == JTokenType.Boolean && !(bool)metadata["isInvalid"]));
					}
				}
				bool baseMaster = StringComparer.OrdinalIgnoreCase.Equals(name, "Starfield.esm");
				if (valid)
					valid = names.Add(name) && (baseMaster
						? index == 0 && (bool)entry["enabled"]
						: entry["locked"] == null || !(bool)entry["locked"]);
				if (!valid)
				{
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.starfield-load-order-invalid",
						"A Starfield load-order entry has invalid, duplicate or uncharacterized plugin state/locking metadata.", path));
					continue;
				}
				// Starfield.esm is engine-owned and already first under the native game policy.
				// Vortex modId is machine-local display metadata, never evidence of NMM file ownership.
				if (baseMaster) continue;
				bool enabled = (bool)entry["enabled"];
				CollectionDesiredPluginState existing = pluginStates.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.PluginName, name));
				if (existing != null && existing.Enabled != enabled)
					issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
						"manifest.starfield-plugin-state-conflict", "plugins and loadOrder request conflicting enabled states.", path + ".enabled"));
				else if (existing == null)
					pluginStates.Add(new CollectionDesiredPluginState(name, enabled));
				orderedNames.Add(name);
			}
			var uniqueRules = new HashSet<CollectionPluginRelativeOrderRule>(rules);
			for (int later = 1; later < orderedNames.Count; later++)
				for (int earlier = 0; earlier < later; earlier++)
				{
					// Preserve the selected subsequence even when an optional/intermediate plugin is absent.
					var rule = new CollectionPluginRelativeOrderRule(orderedNames[later], orderedNames[earlier]);
					if (uniqueRules.Add(rule)) rules.Add(rule);
				}
			if (NexusCollectionManifestNormalizer.HasPluginRelativeOrderCycle(rules))
				issues.Add(CollectionCapabilityIssue.ForManifest(CollectionCompatibilityStatus.Unsupported,
					"manifest.plugin-rule-cycle", "Starfield loadOrder conflicts with the Collection's plugin rules.", "$.loadOrder"));
			return partial;
		}
	}
}
