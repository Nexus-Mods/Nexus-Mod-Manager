using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nexus.Client.Games;

namespace Nexus.Client
{
	/// <summary>Forwards startup Nexus links before any window, game service or shared temporary-file cleanup is created.</summary>
	internal static class NexusLinkHandoff
	{
		/// <summary>Returns success only after the selected running game instance accepts the complete link.</summary>
		internal static bool TryForward(GameDiscoveryStartupIntent intent, IEnumerable<string> installedModeIds, Func<string, IMessager> connect)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (connect == null) throw new ArgumentNullException(nameof(connect));
			if (intent.NxmLink == null || String.IsNullOrWhiteSpace(intent.RequestedModeId)) return false;
			// URI hosts are lowercase; persisted mode IDs preserve the existing IPC endpoint's spelling.
			string modeId = installedModeIds?.FirstOrDefault(id => String.Equals(id, intent.RequestedModeId, StringComparison.OrdinalIgnoreCase))
				?? intent.RequestedModeId;
			bool accepted = false;
			try
			{
				using (IMessager existing = connect(modeId))
				{
					if (existing == null) return false;
					existing.AddMod(intent.NxmLink.OriginalString);
					accepted = true;
					return true;
				}
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Startup Nexus link handoff failed for {0}: {1}", modeId, exception.Message);
				return accepted;
			}
		}
	}
}
