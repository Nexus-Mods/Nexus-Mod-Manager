namespace Nexus.Client.Games
{
	using System;

	/// <summary>Game-independent launch intent. The shell is always shown first;
	/// the controller decides whether this targets an already configured game.</summary>
	internal sealed class GameDiscoveryStartupIntent
	{
		internal string RequestedModeId { get; private set; }
		internal Uri NxmLink { get; private set; }

		internal static GameDiscoveryStartupIntent Parse(string[] arguments, Func<string, string> mapNexusDomain)
		{
			var intent = new GameDiscoveryStartupIntent();
			if (arguments == null) return intent;
			if (arguments.Length > 0 && Uri.TryCreate(arguments[0], UriKind.Absolute, out Uri link) &&
				String.Equals(link.Scheme, "nxm", StringComparison.OrdinalIgnoreCase))
			{
				intent.NxmLink = link;
				intent.RequestedModeId = mapNexusDomain?.Invoke(link.Host);
				return intent;
			}
			for (int index = 0; index + 1 < arguments.Length; index++)
				if (String.Equals(arguments[index], "-game", StringComparison.OrdinalIgnoreCase) &&
					!arguments[index + 1].StartsWith("-", StringComparison.Ordinal) &&
					!String.IsNullOrWhiteSpace(arguments[index + 1]))
				{
					intent.RequestedModeId = arguments[index + 1];
					break;
				}
			return intent;
		}
	}
}
