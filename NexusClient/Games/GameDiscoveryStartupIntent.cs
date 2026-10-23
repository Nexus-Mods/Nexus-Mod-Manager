namespace Nexus.Client.Games
{
	using System;
	using System.Collections.Generic;
	using System.IO;

	/// <summary>Game-independent launch intent, including imports retained until a running or newly selected game accepts them.</summary>
	internal sealed class GameDiscoveryStartupIntent
	{
		internal string RequestedModeId { get; set; }
		internal Uri NxmLink { get; private set; }
		internal Queue<string> LocalFiles { get; } = new Queue<string>();
		internal bool RequiresGameSelection { get; set; }

		/// <summary>Retains local file arguments alongside explicit game selection without changing saved preferences.</summary>
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
			for (int index = 0; index < arguments.Length; index++)
			{
				if (String.Equals(arguments[index], "-game", StringComparison.OrdinalIgnoreCase) &&
					index + 1 < arguments.Length &&
					!String.IsNullOrWhiteSpace(arguments[index + 1]) &&
					!arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
				{
					if (intent.RequestedModeId == null) intent.RequestedModeId = arguments[index + 1];
					index++;
					continue;
				}
				string argument = arguments[index];
				if (String.IsNullOrWhiteSpace(argument) || argument.StartsWith("-", StringComparison.Ordinal)) continue;
				if (File.Exists(argument))
					intent.LocalFiles.Enqueue(Path.GetFullPath(argument));
				else if (Uri.TryCreate(argument, UriKind.Absolute, out Uri file) && file.IsFile && File.Exists(file.LocalPath))
					intent.LocalFiles.Enqueue(file.LocalPath);
			}
			return intent;
		}
	}
}
