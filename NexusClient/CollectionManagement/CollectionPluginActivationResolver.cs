using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Resolves reviewed unlisted-plugin defaults against the final deployed master graph without changing explicit choices.</summary>
	internal static class CollectionPluginActivationResolver
	{
		/// <summary>Identifies activation defaults controlled by the install preference rather than a named Collection declaration.</summary>
		internal static bool IsUnspecifiedActivation(CollectionExecutionPolicy policy, NormalizedCollectionManifest manifest,
			CollectionPlannedPluginEffect effect)
		{
			return policy != null && policy.EnableUnspecifiedPlugins && manifest != null &&
				effect.Kind == CollectionPlannedPluginEffectKind.Activation && !manifest.PluginStates.Any(x =>
					StringComparer.OrdinalIgnoreCase.Equals(x.PluginName, PluginName(effect.PluginPaths[0])));
		}

		/// <summary>Enables only defaults whose transitive masters exist, parse correctly and will be active in the requested final state.</summary>
		internal static List<CollectionPlannedPluginEffect> Resolve(CollectionExecutionPolicy policy, NormalizedCollectionManifest manifest,
			CollectionNativeStateIndex state, IEnumerable<CollectionPlannedPluginEffect> effects)
		{
			if (policy == null) throw new ArgumentNullException(nameof(policy));
			if (manifest == null) throw new ArgumentNullException(nameof(manifest));
			if (state == null) throw new ArgumentNullException(nameof(state));
			List<CollectionPlannedPluginEffect> result = (effects ?? throw new ArgumentNullException(nameof(effects))).ToList();
			if (!result.Any(x => IsUnspecifiedActivation(policy, manifest, x))) return result;
			if (state.PluginCoverage != CollectionNativeStateCoverage.Complete)
				throw new InvalidOperationException("Unlisted Collection plugin activation requires a complete native plugin snapshot.");

			Dictionary<string, CollectionNativePluginState> plugins = state.Plugins.Values.GroupBy(x => PluginName(x.FileName), StringComparer.OrdinalIgnoreCase)
				.Where(x => x.Count() == 1).ToDictionary(x => x.Key, x => x.Single(), StringComparer.OrdinalIgnoreCase);
			var requested = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionPlannedPluginEffect effect in result.Where(x => x.Kind == CollectionPlannedPluginEffectKind.Activation))
			{
				string name = PluginName(effect.PluginPaths[0]);
				bool previous;
				if (requested.TryGetValue(name, out previous) && previous != effect.Active.Value)
					throw new InvalidOperationException("The Collection review contains contradictory plugin activation requests.");
				requested[name] = effect.Active.Value;
			}
			var available = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int index = 0; index < result.Count; index++)
			{
				CollectionPlannedPluginEffect effect = result[index];
				if (IsUnspecifiedActivation(policy, manifest, effect))
					result[index] = CollectionPlannedPluginEffect.Activation(effect.PluginPaths[0],
						CanActivate(PluginName(effect.PluginPaths[0]), plugins, requested, available, visiting));
			}
			return result;
		}

		/// <summary>Walks the intended activation graph, rejecting missing/inactive masters, unreadable headers and dependency cycles.</summary>
		private static bool CanActivate(string name, IDictionary<string, CollectionNativePluginState> plugins,
			IDictionary<string, bool> requested, IDictionary<string, bool> available, ISet<string> visiting)
		{
			bool result;
			if (available.TryGetValue(name, out result)) return result;
			CollectionNativePluginState plugin;
			bool active;
			if (!plugins.TryGetValue(name, out plugin) || plugin.ParseStatus != PluginParseStatus.Parsed ||
				!(requested.TryGetValue(name, out active) ? active : plugin.Active))
				return available[name] = false;
			if (!visiting.Add(name)) return false;
			result = plugin.Masters.All(master => CanActivate(PluginName(master), plugins, requested, available, visiting));
			visiting.Remove(name);
			return available[name] = result;
		}

		/// <summary>Uses native case-insensitive plugin basenames for both paths and master references.</summary>
		private static string PluginName(string path)
		{
			return Path.GetFileName((path ?? String.Empty).Replace('/', '\\'));
		}
	}
}
