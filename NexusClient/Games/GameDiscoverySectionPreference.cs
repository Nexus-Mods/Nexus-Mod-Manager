using System.Collections.Generic;
using Nexus.Client.Settings;

namespace Nexus.Client.Games
{
	/// <summary>Stores display-only section choices while preserving historical game configuration.</summary>
	internal static class GameDiscoverySectionPreference
	{
		/// <summary>Applies recognized preferences only while a previously configured game's installation is missing.</summary>
		internal static GameDiscoveryCollection GetCollection(ISettings settings, string modeId, bool missing, GameDiscoveryCollection automaticCollection)
		{
			string saved;
			if (missing && settings.GameDiscoverySections != null && settings.GameDiscoverySections.TryGetValue(modeId, out saved))
			{
				if (saved == nameof(GameDiscoveryCollection.ToConfigure)) return GameDiscoveryCollection.ToConfigure;
				if (saved == nameof(GameDiscoveryCollection.Supported)) return GameDiscoveryCollection.Supported;
			}
			return automaticCollection;
		}

		/// <summary>Updates only the section preference; the caller saves settings and can restore the previous dictionary on failure.</summary>
		internal static bool TryMove(ISettings settings, GameDiscoveryEntry entry, GameDiscoveryCollection destination)
		{
			if (settings == null || entry == null || !entry.IsInstallationMissing ||
				(destination != GameDiscoveryCollection.ToConfigure && destination != GameDiscoveryCollection.Supported) ||
				entry.Collection == destination)
				return false;

			PerGameModeSettings<string> sections = new PerGameModeSettings<string>();
			if (settings.GameDiscoverySections != null)
				foreach (KeyValuePair<string, string> section in settings.GameDiscoverySections) sections[section.Key] = section.Value;
			sections[entry.ModeId] = destination.ToString();
			settings.GameDiscoverySections = sections;
			return true;
		}
	}
}
