using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexus.Client.PluginManagement
{
	/// <summary>Detached native precedence metadata shared by Collection application and verification.</summary>
	internal sealed class NativePluginOrderNode
	{
		/// <summary>Copies the native plugin name, master references and section flags.</summary>
		internal NativePluginOrderNode(string name, IEnumerable<string> masters, bool effectiveMaster, bool blueprint)
		{
			Name = Path.GetFileName(name);
			Masters = (masters ?? Enumerable.Empty<string>()).Select(Path.GetFileName).ToArray();
			EffectiveMaster = effectiveMaster;
			Blueprint = blueprint;
		}

		internal string Name { get; }
		internal string[] Masters { get; }
		internal bool EffectiveMaster { get; }
		internal bool Blueprint { get; }
	}

	/// <summary>Indexes mandatory native precedence without reading plugin files or changing curator preferences.</summary>
	internal sealed class NativePluginOrderConstraints
	{
		private readonly Dictionary<string, HashSet<string>> _edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> _reachable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Uses the current game's master-section policy and already parsed plugin metadata.</summary>
		internal NativePluginOrderConstraints(IEnumerable<NativePluginOrderNode> plugins, bool mastersBeforeNonMasters)
		{
			Dictionary<string, NativePluginOrderNode> nodes = plugins.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
			foreach (NativePluginOrderNode plugin in nodes.Values)
			{
				foreach (string master in plugin.Masters.Where(nodes.ContainsKey)) AddEdge(master, plugin.Name);
				// Synthetic section nodes avoid a master-count times plugin-count edge set. '|' is not a valid filename.
				if (mastersBeforeNonMasters)
				{
					if (plugin.EffectiveMaster && !plugin.Blueprint) AddEdge(plugin.Name, "|master-section|");
					if (!plugin.EffectiveMaster) AddEdge("|master-section|", plugin.Name);
				}
				if (plugin.Blueprint) AddEdge("|blueprint-section|", plugin.Name);
				else AddEdge(plugin.Name, "|blueprint-section|");
			}
		}

		/// <summary>Checks mandatory direct or transitive precedence, caching reachability for this immutable snapshot.</summary>
		internal bool RequiresBefore(string earlier, string later)
		{
			earlier = Path.GetFileName(earlier);
			later = Path.GetFileName(later);
			lock (_reachable)
			{
				HashSet<string> reachable;
				if (!_reachable.TryGetValue(earlier, out reachable))
				{
					reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					var pending = new Stack<string>();
					pending.Push(earlier);
					while (pending.Count > 0)
					{
						HashSet<string> next;
						if (!_edges.TryGetValue(pending.Pop(), out next)) continue;
						foreach (string name in next) if (reachable.Add(name)) pending.Push(name);
					}
					_reachable.Add(earlier, reachable);
				}
				return reachable.Contains(later);
			}
		}

		/// <summary>Adds one mandatory native edge once.</summary>
		private void AddEdge(string earlier, string later)
		{
			HashSet<string> outgoing;
			if (!_edges.TryGetValue(earlier, out outgoing))
			{
				outgoing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_edges.Add(earlier, outgoing);
			}
			outgoing.Add(later);
		}
	}
}
