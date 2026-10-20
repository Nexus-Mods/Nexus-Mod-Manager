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
		/// <summary>Recognizes only the two local Witcher 3 identities, without changing their storage or catalog IDs.</summary>
		internal static bool IsWitcher3Mode(string modeId) => String.Equals(modeId, "Witcher3", StringComparison.OrdinalIgnoreCase) ||
			String.Equals(modeId, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase);

		/// <summary>Restricts the edition preference to links from the shared Witcher 3 catalog.</summary>
		internal static bool IsWitcher3DownloadDomain(string domain) => !String.IsNullOrEmpty(domain) &&
			String.Equals(GameDomainTranslator.DetermineGameDomain(domain), "witcher3", StringComparison.OrdinalIgnoreCase);

		/// <summary>Uses a valid saved edition, then the active/remembered Witcher edition, preserving the legacy default otherwise.</summary>
		internal static string ResolveWitcher3DownloadMode(string preference, string fallbackMode)
		{
			string selected = IsWitcher3Mode(preference) ? preference : IsWitcher3Mode(fallbackMode) ? fallbackMode : "Witcher3";
			return String.Equals(selected, "Witcher3Remastered", StringComparison.OrdinalIgnoreCase) ? "Witcher3Remastered" : "Witcher3";
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
