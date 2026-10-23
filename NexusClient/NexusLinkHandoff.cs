using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Nexus.Client.Games;

namespace Nexus.Client
{
	/// <summary>Forwards startup mod sources before any window, game service or shared temporary-file cleanup is created.</summary>
	internal static class NexusLinkHandoff
	{
		/// <summary>Returns success only after a running game instance accepts all pending startup sources.</summary>
		internal static bool TryForward(GameDiscoveryStartupIntent intent, IEnumerable<string> installedModeIds, Func<string, IMessager> connect)
		{
			if (intent == null) throw new ArgumentNullException(nameof(intent));
			if (connect == null) throw new ArgumentNullException(nameof(connect));
			if (intent.LocalFiles.Count > 0) return TryForwardLocalFiles(intent, installedModeIds, connect);
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

		/// <summary>Routes local files to an explicit game or the sole running game, retaining unacknowledged files for startup.</summary>
		private static bool TryForwardLocalFiles(GameDiscoveryStartupIntent intent, IEnumerable<string> installedModeIds, Func<string, IMessager> connect)
		{
			string[] modes = (installedModeIds ?? Enumerable.Empty<string>()).Where(id => !String.IsNullOrWhiteSpace(id))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			string selected = intent.RequestedModeId;
			if (String.IsNullOrWhiteSpace(selected))
			{
				foreach (string mode in modes)
				{
					try
					{
						using (IMessager existing = connect(mode))
						{
							if (existing == null) continue;
							if (selected != null)
							{
								intent.RequiresGameSelection = true;
								return false;
							}
							selected = mode;
						}
					}
					catch (Exception exception) when (!(exception is OutOfMemoryException))
					{
						Trace.TraceWarning("Local file handoff probe failed for {0}: {1}", mode, exception.Message);
					}
				}
			}
			if (String.IsNullOrWhiteSpace(selected)) return false;
			selected = modes.FirstOrDefault(id => String.Equals(id, selected, StringComparison.OrdinalIgnoreCase)) ?? selected;
			intent.RequestedModeId = selected;
			bool accepted = false;
			try
			{
				using (IMessager existing = connect(selected))
				{
					if (existing == null) return false;
					while (intent.LocalFiles.Count > 0)
					{
						existing.AddMod(intent.LocalFiles.Peek());
						intent.LocalFiles.Dequeue();
					}
					accepted = true;
					return true;
				}
			}
			catch (Exception exception) when (!(exception is OutOfMemoryException))
			{
				Trace.TraceWarning("Startup local file handoff failed for {0}: {1}", selected, exception.Message);
				return accepted;
			}
		}
	}
}
