using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.PluginManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Bridges reviewed Collection relative plugin-order intent to NMM's authoritative plugin manager.</summary>
	internal static class CollectionPluginRelativeOrderApplicator
	{
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
			if (!pluginManager.TrySetRelativePluginOrder(constraints, out diagnostics))
			{
				string detail = diagnostics == null || diagnostics.Count == 0
					? "The native plugin policy could not satisfy the reviewed relative plugin order."
					: "The native plugin policy rejected the reviewed relative plugin order: " + String.Join(", ", diagnostics.Select(x => x.Kind.ToString()));
				throw new InvalidOperationException(detail);
			}
		}
	}
}
