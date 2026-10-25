using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Util;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.Games
{
	/// <summary>Optional catalog identity independent of a local game-mode and native storage identity.</summary>
	public interface INexusGameDomainProvider
	{
		/// <summary>The explicitly configured canonical Nexus catalog slug, or null for legacy alias resolution.</summary>
		string NexusGameDomain { get; }
	}

	/// <summary>Resolves configured catalog identities while preserving legacy game-domain aliases.</summary>
	public static class NexusGameDomain
	{
		/// <summary>Whether a local mode explicitly selects an edition in a configured shared catalog.</summary>
		internal static bool IsExplicitDownloadEditionMode(string modeId)
		{
			DataDriven.GameModeRuntimeConfiguration.EnsureLoaded();
			GameModeDownloadRouting routing = GameModeRuntimePolicies.For(modeId).DownloadRouting;
			return routing != null && routing.AlternateMode != null && !routing.LegacySelectionToggle;
		}

		/// <summary>Whether a catalog declares a non-session-switching edition preference.</summary>
		internal static bool IsEditionChoiceDownloadDomain(string domain)
		{
			DataDriven.GameModeRuntimeConfiguration.EnsureLoaded();
			return GameModeRuntimePolicies.All.Any(item => IsExplicitDownloadEditionMode(item.Key) &&
				item.Value.DownloadRouting.IncomingDomains.Contains(GameDomainTranslator.DetermineGameDomain(domain), StringComparer.OrdinalIgnoreCase));
		}

		/// <summary>Resolves a declared edition using a valid preference, matching fallback, then its configured default.</summary>
		internal static string ResolveDownloadEdition(string preference, string fallbackMode)
		{
			DataDriven.GameModeRuntimeConfiguration.EnsureLoaded();
			var modes = GameModeRuntimePolicies.All.Where(item => IsExplicitDownloadEditionMode(item.Key)).ToArray();
			var preferred = modes.FirstOrDefault(item => String.Equals(item.Key, preference, StringComparison.OrdinalIgnoreCase));
			if (preferred.Value != null) return preferred.Key;
			var fallback = modes.FirstOrDefault(item => String.Equals(item.Key, fallbackMode, StringComparison.OrdinalIgnoreCase));
			return fallback.Value != null ? fallback.Key : modes.Length == 0 ? fallbackMode : modes[0].Value.DownloadRouting.DefaultMode;
		}

		/// <summary>Accepts a literal canonical slug, excluding URLs, paths and local-mode aliases.</summary>
		public static bool IsValid(string value) => !String.IsNullOrEmpty(value) && value.Length <= 64 &&
			(value[0] >= 'a' && value[0] <= 'z' || value[0] >= '0' && value[0] <= '9') && value.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-') &&
			String.Equals(value, GameDomainTranslator.DetermineGameDomain(value), StringComparison.Ordinal);

		/// <summary>Uses the same catalog for outbound API calls and inbound destination matching.</summary>
		public static string Resolve(IGameModeDescriptor mode)
		{
			if (mode == null) throw new ArgumentNullException(nameof(mode));
			string configured = (mode as INexusGameDomainProvider)?.NexusGameDomain;
			if (configured != null)
			{
				if (!IsValid(configured)) throw new ArgumentException("The configured Nexus catalog must be a canonical lowercase slug.");
				return configured;
			}
			return GameDomainTranslator.DetermineGameDomain(mode.ModeId);
		}

		/// <summary>Rejects an incoming link for a different catalog before configured consumers query or queue it.</summary>
		public static void RequireMatchingLink(IGameModeDescriptor mode, Uri link)
		{
			if (link == null || !link.IsAbsoluteUri || !String.Equals(link.Scheme, "nxm", StringComparison.OrdinalIgnoreCase) ||
				(mode as INexusGameDomainProvider)?.NexusGameDomain == null) return;
			string expected = Resolve(mode);
			if (!String.Equals(GameDomainTranslator.DetermineGameDomain(link.Host), expected, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException(LanguageManager.Format("GameModes.DownloadDestination.WrongCatalog",
					"This Nexus link belongs to {0}. The selected game mode uses {1}. Open the link for its matching local game mode.", link.Host, expected));
		}

		/// <summary>Returns installed catalog matches only when at least one explicitly configured consumer opts into routing.</summary>
		public static IReadOnlyList<IGameModeDescriptor> GetDestinations(string domain, IEnumerable<IGameModeDescriptor> installedModes)
		{
			if (String.IsNullOrWhiteSpace(domain)) throw new ArgumentException("A Nexus catalog is required.", nameof(domain));
			if (installedModes == null) throw new ArgumentNullException(nameof(installedModes));
			string canonical = GameDomainTranslator.DetermineGameDomain(domain).ToLowerInvariant();
			List<IGameModeDescriptor> matches = installedModes.Where(mode => mode != null &&
				String.Equals(Resolve(mode), canonical, StringComparison.OrdinalIgnoreCase)).ToList();
			if (!matches.Any(mode => (mode as INexusGameDomainProvider)?.NexusGameDomain != null))
				return Array.AsReadOnly(new IGameModeDescriptor[0]);
			return matches.OrderBy(mode => mode.Name, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(mode => mode.ModeId, StringComparer.Ordinal).ToList().AsReadOnly();
		}
	}
}
