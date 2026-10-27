using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Nexus.Client.Settings;
using Nexus.Client.Util;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games.DataDriven
{
	/// <summary>Validates and consumes optional runtime policy without game-name branches in shared callers.</summary>
	internal static class GameModeRuntimeConfiguration
	{
		/// <summary>Validates only policy fields consumed by the shared runtime.</summary>
		internal static void Validate(GameModeDefinition definition)
		{
			GameModeRuntimePolicy policy = definition.Runtime;
			if (policy == null) return;
			if (policy.SchemaVersion != 1) throw new ArgumentException("runtime.schemaVersion must be 1.");
			if (policy.CollectionAdapter != null && !new[] { "module-settings-v1", "component-settings-v1", "plugin-order-v1" }.Contains(policy.CollectionAdapter))
				throw new ArgumentException("Unknown runtime.collectionAdapter.");
			if (policy.CollectionAdapter != null && !NexusGameDomain.IsValid(policy.CollectionDomain))
				throw new ArgumentException("A Collection adapter requires a canonical runtime.collectionDomain.");
			if (policy.CollectionMemberTypes != null && (policy.CollectionMemberTypes.Any(String.IsNullOrWhiteSpace) || policy.CollectionMemberTypes.Distinct(StringComparer.Ordinal).Count() != policy.CollectionMemberTypes.Length))
				throw new ArgumentException("runtime.collectionMemberTypes requires unique non-empty types.");
			if (policy.CollectionPluginGroups != null && (policy.CollectionPluginGroups.Any(group => group == null || String.IsNullOrWhiteSpace(group.Id) ||
				(group.After != null && group.After.Any(String.IsNullOrWhiteSpace))) ||
				policy.CollectionPluginGroups.Select(group => group.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != policy.CollectionPluginGroups.Length))
				throw new ArgumentException("Collection plugin groups require unique ids and non-empty ordering references.");
			if (policy.CollectionOrderProvidesMetadata && (policy.CollectionAdapter == null || definition.StructuredEffects == null))
				throw new ArgumentException("Collection-owned metadata requires an adapter and structuredEffects.");
			if (policy.NativeRootOwnershipProfile != null && policy.NativeRootOwnershipProfile != "edition-root-ownership-v1")
				throw new ArgumentException("Unsupported native root ownership profile.");
			if (policy.StartupNotice != null)
			{
				RequirePreference(policy.StartupNotice.Preference, typeof(bool));
				Version minimum;
				if (!Version.TryParse(policy.StartupNotice.MinimumVersion, out minimum) || String.IsNullOrWhiteSpace(policy.StartupNotice.Text))
					throw new ArgumentException("A startup notice requires a minimumVersion and text.");
			}
			if (policy.DownloadRouting != null)
			{
				GameModeDownloadRouting routing = policy.DownloadRouting;
				if (!DataDrivenDefinitionRules.IsIdentifier(routing.DefaultMode) || (routing.AlternateMode != null && !DataDrivenDefinitionRules.IsIdentifier(routing.AlternateMode)) ||
					(routing.FallbackMode != null && !DataDrivenDefinitionRules.IsIdentifier(routing.FallbackMode))) throw new ArgumentException("Download routing requires valid local mode identifiers.");
				if (routing.Preference != null) RequirePreference(routing.Preference, typeof(string));
				if (routing.IncomingDomains == null || routing.IncomingDomains.Length == 0 || routing.IncomingDomains.Any(domain => !NexusGameDomain.IsValid(domain)))
					throw new ArgumentException("Download routing requires canonical incomingDomains.");
			}
			if (policy.ConsoleBatchCommands != null && (policy.ConsoleBatchCommands.Length == 0 || policy.ConsoleBatchCommands.Any(String.IsNullOrWhiteSpace) || String.IsNullOrWhiteSpace(policy.ConsoleBatchRuleId)))
				throw new ArgumentException("Console batch recognition requires commands and a stable rule id.");
		}

		/// <summary>Limits configuration access to an existing, strongly typed, writable settings contract property.</summary>
		private static PropertyInfo RequirePreference(string name, Type type)
		{
			PropertyInfo property = String.IsNullOrEmpty(name) ? null : typeof(ISettings).GetProperty(name);
			if (property == null || property.PropertyType != type || !property.CanRead || !property.CanWrite)
				throw new ArgumentException("The runtime preference must name an existing writable " + type.Name + " setting.");
			return property;
		}

		/// <summary>Preserves historical settings names while letting policy choose which preference is used.</summary>
		internal static T GetPreference<T>(ISettings settings, string name) => String.IsNullOrEmpty(name) ? default(T) : (T)RequirePreference(name, typeof(T)).GetValue(settings, null);

		/// <summary>Updates the configured existing preference without creating or renaming persisted keys.</summary>
		internal static void SetPreference<T>(ISettings settings, string name, T value) => RequirePreference(name, typeof(T)).SetValue(settings, value, null);

		/// <summary>Returns the configured one-time notice, consuming its existing preference at the original version boundary.</summary>
		internal static string StartupNotice(IGameMode mode, ISettings settings)
		{
			GameModeStartupNotice notice = GameModeRuntimePolicies.For(mode.ModeId).StartupNotice;
			if (notice == null || !GetPreference<bool>(settings, notice.Preference) || mode.GameVersion == null) return String.Empty;
			int comparison = mode.GameVersion.CompareTo(new Version(notice.MinimumVersion));
			if (comparison < 0 || (comparison == 0 && !notice.IncludeMinimumVersion)) return String.Empty;
			SetPreference(settings, notice.Preference, false); settings.Save();
			if (notice.RequiresObsoletePluginConfiguration && !mode.LoadOrderManager.ObsoleteConfigFiles) return String.Empty;
			return notice.Text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
		}

		/// <summary>Publishes the active validated catalog without changing policy when a standalone definition is inspected.</summary>
		internal static void Publish(IEnumerable<GameModeDefinition> definitions) => GameModeRuntimePolicies.Replace(
			definitions.Select(definition => new KeyValuePair<string, GameModeRuntimePolicy>(definition.ModeId, definition.Runtime)));

		/// <summary>Loads definition policies before an early protocol link is routed during legacy startup.</summary>
		internal static void EnsureLoaded()
		{
			if (GameModeRuntimePolicies.All.Count != 0) return;
			string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameModes", "Definitions");
			if (Directory.Exists(directory)) Publish(new GameModeDefinitionLoader().LoadFromDirectory(directory).Definitions);
		}

		/// <summary>Resolves an edition using its configured preference, valid fallback and unchanged local identities.</summary>
		internal static string DownloadMode(string modeId, ISettings settings)
		{
			GameModeDownloadRouting route = GameModeRuntimePolicies.For(modeId).DownloadRouting;
			if (route == null) return modeId;
			string selected = GetPreference<string>(settings, route.Preference);
			if (!String.IsNullOrEmpty(selected) && (selected == route.DefaultMode || selected == route.AlternateMode)) return selected;
			if (route.UseRememberedModeAsDefault && (modeId == route.DefaultMode || modeId == route.AlternateMode)) return modeId;
			return route.DefaultMode;
		}

		/// <summary>Routes protocol aliases and shared catalogs from per-game definitions.</summary>
		internal static string RequestedMode(string domain, ISettings settings)
		{
			EnsureLoaded();
			string canonical = GameDomainTranslator.DetermineGameDomain(domain);
			var routes = GameModeRuntimePolicies.All.Where(item => item.Value.DownloadRouting?.IncomingDomains?.Contains(canonical, StringComparer.OrdinalIgnoreCase) == true).ToArray();
			var remembered = routes.FirstOrDefault(item => item.Value.DownloadRouting.PreferRememberedMode && String.Equals(item.Key, settings.RememberedGameMode, StringComparison.OrdinalIgnoreCase));
			if (remembered.Value != null) return remembered.Key;
			var choice = routes.FirstOrDefault(item => item.Value.DownloadRouting.Preference != null);
			if (choice.Value != null)
			{
				GameModeDownloadRouting route = choice.Value.DownloadRouting;
				string selected = GetPreference<string>(settings, route.Preference);
				if (!String.IsNullOrEmpty(selected) && (selected == route.DefaultMode || selected == route.AlternateMode)) return selected;
				if (route.UseRememberedModeAsDefault && (settings.RememberedGameMode == route.DefaultMode || settings.RememberedGameMode == route.AlternateMode)) return settings.RememberedGameMode;
				return route.DefaultMode;
			}
			if (routes.Length != 0) return routes[0].Value.DownloadRouting.DefaultMode;
			// Plain domains such as fallout4 still need the local mode ID's exact IPC spelling,
			// including when a newly opened game's settings have not yet reached another process.
			return GameModeRuntimePolicies.All.FirstOrDefault(item => String.Equals(item.Key, domain, StringComparison.OrdinalIgnoreCase)).Key ?? domain;
		}

		/// <summary>Uses configured localized edition text with its declared fallback.</summary>
		internal static string DownloadLabel(string modeId)
		{
			GameModeDownloadRouting routing = GameModeRuntimePolicies.For(modeId).DownloadRouting;
			if (routing == null) return String.Empty;
			return routing.LabelKey == null ? routing.Label : LanguageManager.Get(routing.LabelKey, routing.Label);
		}
	}
}
