using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Nexus.Client.PluginManagement;
using Nexus.Client.Plugins;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Bridges reviewed Collection relative plugin-order intent to NMM's authoritative plugin manager.</summary>
	internal static class CollectionPluginRelativeOrderApplicator
	{
		private static readonly ConditionalWeakTable<CollectionNativeStateIndex, NativePluginOrderConstraints> NativeConstraints =
			new ConditionalWeakTable<CollectionNativeStateIndex, NativePluginOrderConstraints>();

		/// <summary>Accepts the smallest required native correction rather than demanding that a plugin precede its master.</summary>
		internal static bool IsOrderSatisfied(CollectionNativeStateIndex state, IEnumerable<string> paths)
		{
			NativePluginOrderConstraints native = NativeConstraints.GetValue(state, snapshot => new NativePluginOrderConstraints(
				snapshot.Plugins.Values.Where(plugin => !snapshot.IsGameManagedPluginOrder(plugin)).Select(plugin => new NativePluginOrderNode(plugin.FileName, plugin.Masters,
					plugin.EffectiveMaster, (plugin.SpecialFlags & PluginSpecialFlags.Blueprint) != 0)), snapshot.PluginOrderRequiresMasterSection));
			CollectionNativePluginState previous = null;
			foreach (string path in paths)
			{
				string name = System.IO.Path.GetFileName(path);
				CollectionNativePluginState plugin;
				if (!state.Plugins.TryGetValue(name, out plugin))
					plugin = state.Plugins.Values.FirstOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(System.IO.Path.GetFileName(x.FileName), name));
				if (plugin == null) return false;
				if (state.IsGameManagedPluginOrder(plugin)) continue;
				if (previous != null)
				{
					bool nativePrecedence = native.RequiresBefore(plugin.FileName, previous.FileName);
					if (nativePrecedence ? plugin.Priority >= previous.Priority : plugin.Priority <= previous.Priority) return false;
				}
				previous = plugin;
			}
			return true;
		}

		internal static void Apply(IPluginManager pluginManager, IEnumerable<CollectionPlannedPluginEffect> effects)
		{
			if (pluginManager == null) throw new ArgumentNullException(nameof(pluginManager));
			List<IList<string>> constraints = (effects ?? Enumerable.Empty<CollectionPlannedPluginEffect>())
				.Where(x => x != null && x.Kind == CollectionPlannedPluginEffectKind.RelativeOrder)
				.Select(x => (IList<string>)x.PluginPaths.ToList()).ToList();
			ApplyConstraints(pluginManager, constraints);
		}

		internal static void ApplyRules(IPluginManager pluginManager, IEnumerable<CollectionPluginRelativeOrderRule> rules)
		{
			if (pluginManager == null) throw new ArgumentNullException(nameof(pluginManager));
			List<IList<string>> constraints = (rules ?? Enumerable.Empty<CollectionPluginRelativeOrderRule>())
				.Select(x => (IList<string>)new[] { x.AfterPluginName, x.PluginName }).ToList();
			ApplyConstraints(pluginManager, constraints);
		}

		internal static string RequirementSubject(CollectionPluginRelativeOrderRule rule)
		{
			if (rule == null) throw new ArgumentNullException(nameof(rule));
			return "after:" + rule.AfterPluginName.ToLowerInvariant() + "|" + rule.PluginName.ToLowerInvariant();
		}

		internal static bool IsRequirementSubject(string subject)
		{
			return !String.IsNullOrWhiteSpace(subject) && subject.StartsWith("after:", StringComparison.Ordinal);
		}

		private static void ApplyConstraints(IPluginManager pluginManager, IList<IList<string>> constraints)
		{
			if (constraints.Count == 0) return;
			IList<PluginValidationDiagnostic> diagnostics;
			ICollectionPluginStateManager collectionManager = pluginManager as ICollectionPluginStateManager;
			bool applied = collectionManager != null
				? collectionManager.TryReconcileCollectionPluginState(new string[0], new Dictionary<string, bool>(), constraints, out diagnostics)
				: pluginManager.TrySetRelativePluginOrder(constraints, out diagnostics);
			if (!applied)
			{
				string detail = diagnostics == null || diagnostics.Count == 0
					? "The native plugin policy could not satisfy the reviewed relative plugin order."
					: "The native plugin policy rejected the reviewed relative plugin order: " + String.Join(", ", diagnostics.Select(x => x.Kind.ToString()));
				throw new InvalidOperationException(detail);
			}
		}
	}
}
