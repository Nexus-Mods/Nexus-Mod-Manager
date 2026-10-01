using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Builds the bounded C8.2 condition environment from a C8.1 replacement difference and optional reviewed incoming effects.
	/// </summary>
	public sealed class CollectionReplacementEnvironmentProjector
	{
		public CollectionReplacementEnvironmentProjection Project(CollectionReplacementDiffPlan diff)
		{
			return Project(diff, Enumerable.Empty<CollectionMemberEffectPreview>());
		}

		/// <summary>
		/// Projects outgoing removal first, then overlays already-reviewed incoming effects in the supplied dependency order.
		/// Incomplete previews or unprovable baseline facts remain explicit blockers.
		/// </summary>
		public CollectionReplacementEnvironmentProjection Project(CollectionReplacementDiffPlan diff,
			IEnumerable<CollectionMemberEffectPreview> incomingEffectsInDependencyOrder)
		{
			if (diff == null) throw new ArgumentNullException(nameof(diff));
			if (incomingEffectsInDependencyOrder == null) throw new ArgumentNullException(nameof(incomingEffectsInDependencyOrder));

			var files = BuildFiles(diff);
			var ini = BuildIni(diff);
			var plugins = BuildPlugins(diff);
			var issues = new List<string>();

			if (!diff.CurrentStateMatchesPlan)
				issues.Add("The replacement difference no longer matches the native-state fingerprint bound to the incoming resolved plan.");
			if (diff.HasBlockers)
				issues.Add("The C8.1 replacement difference contains blocked or ambiguous decisions.");
			if (diff.CurrentSetup.NativeState.PluginCoverage == CollectionNativeStateCoverage.Unavailable)
				issues.Add("Native plugin-state coverage is unavailable, so plugin conditions cannot be evaluated against a replacement baseline.");

			foreach (CollectionMemberEffectPreview preview in incomingEffectsInDependencyOrder)
			{
				if (preview == null)
					throw new ArgumentException("The incoming replacement effect sequence cannot contain null previews.", nameof(incomingEffectsInDependencyOrder));
				if (!preview.IsComplete)
				{
					issues.Add("Incoming member " + preview.MemberKey + " has an incomplete effect preview and cannot be projected into the replacement environment.");
					continue;
				}
				ApplyIncoming(preview, files, ini, plugins, issues);
			}

			AppendKnowledgeIssues(files, plugins, issues);

			return new CollectionReplacementEnvironmentProjection(diff, files, ini, plugins, diff.CurrentSetup.NativeState.PluginCoverage, issues);
		}


		/// <summary>
		/// Overlays already-approved incoming effects on an existing replacement baseline without re-reading the pre-removal setup.
		/// This is used by C8.5 visibility barriers after a post-removal baseline was explicitly amended.
		/// </summary>
		public CollectionReplacementEnvironmentProjection Overlay(CollectionReplacementEnvironmentProjection baseline,
			IEnumerable<CollectionMemberEffectPreview> incomingEffectsInDependencyOrder)
		{
			if (baseline == null) throw new ArgumentNullException(nameof(baseline));
			if (incomingEffectsInDependencyOrder == null) throw new ArgumentNullException(nameof(incomingEffectsInDependencyOrder));
			var files = baseline.Files.ToDictionary(x => x.Key, x => x.Value);
			var ini = baseline.IniValues.ToDictionary(x => x.Key, x => x.Value);
			var plugins = baseline.Plugins.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
			var issues = new List<string>(baseline.Issues);
			foreach (CollectionMemberEffectPreview preview in incomingEffectsInDependencyOrder)
			{
				if (preview == null)
					throw new ArgumentException("The incoming replacement effect sequence cannot contain null previews.", nameof(incomingEffectsInDependencyOrder));
				if (!preview.IsComplete)
				{
					issues.Add("Incoming member " + preview.MemberKey + " has an incomplete effect preview and cannot be projected into the replacement environment.");
					continue;
				}
				ApplyIncoming(preview, files, ini, plugins, issues);
			}
			AppendKnowledgeIssues(files, plugins, issues);
			return new CollectionReplacementEnvironmentProjection(baseline.Diff, files, ini, plugins, baseline.PluginCoverage, issues);
		}

		/// <summary>
		/// Observes the current authoritative native snapshot through the same bounded condition-environment contract used by C8.2.
		/// Missing managed ownership is never treated as proof that an unmanaged/original file or INI value is absent.
		/// </summary>
		public CollectionReplacementEnvironmentProjection ObserveCurrent(CollectionReplacementEnvironmentProjection expected,
			CollectionReplacementCurrentSetupSnapshot current)
		{
			if (expected == null) throw new ArgumentNullException(nameof(expected));
			if (current == null) throw new ArgumentNullException(nameof(current));
			if (!expected.Diff.Target.Equals(current.NativeState.Target))
				throw new ArgumentException("The observed replacement environment belongs to a different target.", nameof(current));

			var files = BuildObservedFiles(expected, current.NativeState);
			var ini = BuildObservedIni(expected, current.NativeState);
			var plugins = BuildObservedPlugins(expected, current.NativeState);
			var issues = new List<string>();
			if (current.NativeState.PluginCoverage == CollectionNativeStateCoverage.Unavailable)
				issues.Add("Native plugin-state coverage is unavailable at the replacement phase barrier.");
			AppendKnowledgeIssues(files, plugins, issues);
			return new CollectionReplacementEnvironmentProjection(expected.Diff, files, ini, plugins, current.NativeState.PluginCoverage, issues);
		}

		private static Dictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> BuildObservedFiles(
			CollectionReplacementEnvironmentProjection expected, CollectionNativeStateIndex current)
		{
			var result = new Dictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState>();
			foreach (CollectionNativeFileState file in current.Files.Values)
			{
				CollectionNativeOwnerState effective = FindEffectiveOwner(file);
				if (String.IsNullOrWhiteSpace(file.EffectiveOwnerKey) || effective == null || effective.Kind == CollectionNativeOwnerKind.Unresolved)
				{
					result[file.Target] = UnprovableFile(file.Target,
						"The current native ownership snapshot cannot prove the effective visible owner at this replacement barrier.");
					continue;
				}
				result[file.Target] = new CollectionReplacementFileEnvironmentState(file.Target,
					CollectionReplacementEnvironmentKnowledge.Known, true, file.EffectiveOwnerKey,
					"Authoritative native ownership after the replacement phase barrier proves this path remains visible.");
			}
			foreach (ModDeploymentTarget target in expected.Files.Keys)
			{
				if (!result.ContainsKey(target))
					result[target] = UnprovableFile(target,
						"The reviewed path is no longer represented by managed ownership; unmanaged/original physical visibility cannot be inferred as absence.");
			}
			return result;
		}

		private static CollectionNativeOwnerState FindEffectiveOwner(CollectionNativeFileState file)
		{
			if (file == null || String.IsNullOrWhiteSpace(file.EffectiveOwnerKey)) return null;
			IEnumerable<CollectionNativeOwnerState> owners = file.Promoted ? file.DeploymentOwners :
				(file.VirtualOwners.Count != 0 ? file.VirtualOwners : file.InstallLogOwners);
			return owners.LastOrDefault(x => !String.IsNullOrWhiteSpace(x.OwnerKey) &&
				StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, file.EffectiveOwnerKey));
		}

		private static Dictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState> BuildObservedIni(
			CollectionReplacementEnvironmentProjection expected, CollectionNativeStateIndex current)
		{
			var result = new Dictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState>();
			foreach (CollectionNativeIniState ini in current.IniEdits.Values)
			{
				CollectionNativeTextOwnerValue value = ini.Values.LastOrDefault();
				if (value == null)
					result[ini.Key] = new CollectionReplacementIniEnvironmentState(ini.Key,
						CollectionReplacementEnvironmentKnowledge.Unprovable, null, null,
						"The current native INI history contains no effective owner/value at this replacement barrier.");
				else
					result[ini.Key] = new CollectionReplacementIniEnvironmentState(ini.Key,
						CollectionReplacementEnvironmentKnowledge.Known, value.Value, value.OwnerKey,
						"Authoritative native INI history after the replacement phase barrier supplies the effective value.");
			}
			foreach (CollectionNativeIniKey key in expected.IniValues.Keys)
			{
				if (!result.ContainsKey(key))
					result[key] = new CollectionReplacementIniEnvironmentState(key,
						CollectionReplacementEnvironmentKnowledge.Unprovable, null, null,
						"The reviewed INI setting disappeared from managed history; an original/unmanaged value cannot be inferred as absent.");
			}
			return result;
		}

		private static Dictionary<string, CollectionReplacementPluginEnvironmentState> BuildObservedPlugins(
			CollectionReplacementEnvironmentProjection expected, CollectionNativeStateIndex current)
		{
			var result = new Dictionary<string, CollectionReplacementPluginEnvironmentState>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionNativePluginState plugin in current.Plugins.Values)
			{
				string key = CollectionReplacementPluginEnvironmentState.NormalizePlugin(plugin.FileName);
				result[key] = new CollectionReplacementPluginEnvironmentState(key, true, plugin.Active,
					"The current native plugin snapshot supplies registration and activation state at the replacement barrier.");
			}
			foreach (string key in expected.Plugins.Keys)
			{
				if (result.ContainsKey(key)) continue;
				if (current.PluginCoverage == CollectionNativeStateCoverage.Complete || current.PluginCoverage == CollectionNativeStateCoverage.NotApplicable)
					result[key] = new CollectionReplacementPluginEnvironmentState(key, false, false,
						"Complete native plugin coverage proves this reviewed plugin is no longer registered.");
				else
					result[key] = new CollectionReplacementPluginEnvironmentState(key, null, null,
						"Native plugin coverage cannot prove the reviewed plugin's state at this replacement barrier.");
			}
			return result;
		}

		private static void AppendKnowledgeIssues(
			IDictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> files,
			IDictionary<string, CollectionReplacementPluginEnvironmentState> plugins, ICollection<string> issues)
		{
			foreach (CollectionReplacementFileEnvironmentState state in files.Values.Where(x => x.Knowledge == CollectionReplacementEnvironmentKnowledge.Unprovable))
				issues.Add("File visibility is unprovable in the effective replacement environment: " + state.Target + ". " + state.Reason);
			foreach (CollectionReplacementPluginEnvironmentState state in plugins.Values.Where(x => !x.Registered.HasValue || !x.Active.HasValue))
				issues.Add("Plugin state is unprovable in the effective replacement environment: " + state.FileName + ". " + state.Reason);
		}

		private static Dictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> BuildFiles(CollectionReplacementDiffPlan diff)
		{
			var effects = diff.Effects.Where(x => x.Kind == CollectionReplacementEffectKind.File)
				.ToDictionary(x => x.ResourceKey, StringComparer.OrdinalIgnoreCase);
			var result = new Dictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState>();
			foreach (CollectionNativeFileState file in diff.CurrentSetup.NativeState.Files.Values)
			{
				CollectionReplacementEffectImpact effect;
				if (!effects.TryGetValue(file.Target.ToString(), out effect))
				{
					result[file.Target] = UnprovableFile(file.Target, "No C8.1 effect classification exists for the observed native file target.");
					continue;
				}

				switch (effect.Disposition)
				{
					case CollectionReplacementEffectDisposition.Retained:
					case CollectionReplacementEffectDisposition.WinnerChange:
					case CollectionReplacementEffectDisposition.OutgoingOnly:
						result[file.Target] = new CollectionReplacementFileEnvironmentState(file.Target,
							CollectionReplacementEnvironmentKnowledge.Known, true, effect.ProjectedOwnerKey, effect.Reason);
						break;
					case CollectionReplacementEffectDisposition.PreservedUnknownOrUnmanaged:
						if (!String.IsNullOrWhiteSpace(effect.ProjectedOwnerKey) &&
							(String.IsNullOrWhiteSpace(effect.CurrentOwnerKey) || StringComparer.OrdinalIgnoreCase.Equals(effect.CurrentOwnerKey, effect.ProjectedOwnerKey)))
							result[file.Target] = new CollectionReplacementFileEnvironmentState(file.Target,
								CollectionReplacementEnvironmentKnowledge.Known, true, effect.ProjectedOwnerKey, effect.Reason);
						else
							result[file.Target] = UnprovableFile(file.Target, effect.Reason);
						break;
					default:
						result[file.Target] = UnprovableFile(file.Target, effect.Reason);
						break;
				}
			}
			return result;
		}

		private static Dictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState> BuildIni(CollectionReplacementDiffPlan diff)
		{
			var effects = diff.Effects.Where(x => x.Kind == CollectionReplacementEffectKind.Ini)
				.ToDictionary(x => x.ResourceKey, StringComparer.OrdinalIgnoreCase);
			var result = new Dictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState>();
			foreach (CollectionNativeIniState ini in diff.CurrentSetup.NativeState.IniEdits.Values)
			{
				CollectionReplacementEffectImpact effect;
				if (!effects.TryGetValue(ini.Key.ToString(), out effect))
				{
					result[ini.Key] = new CollectionReplacementIniEnvironmentState(ini.Key,
						CollectionReplacementEnvironmentKnowledge.Unprovable, null, null, "No C8.1 effect classification exists for the INI setting.");
					continue;
				}
				string ownerKey = effect.ProjectedOwnerKey;
				CollectionNativeTextOwnerValue value = String.IsNullOrWhiteSpace(ownerKey) ? null : ini.Values.LastOrDefault(x =>
					StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, ownerKey));
				bool stableCurrent = effect.Disposition == CollectionReplacementEffectDisposition.Retained &&
					!String.IsNullOrWhiteSpace(effect.CurrentOwnerKey);
				if (value == null && stableCurrent)
					value = ini.Values.LastOrDefault(x => StringComparer.OrdinalIgnoreCase.Equals(x.OwnerKey, effect.CurrentOwnerKey));
				if (value != null)
					result[ini.Key] = new CollectionReplacementIniEnvironmentState(ini.Key,
						CollectionReplacementEnvironmentKnowledge.Known, value.Value, value.OwnerKey, effect.Reason);
				else
					result[ini.Key] = new CollectionReplacementIniEnvironmentState(ini.Key,
						CollectionReplacementEnvironmentKnowledge.Unprovable, null, ownerKey, effect.Reason);
			}
			return result;
		}

		private static Dictionary<string, CollectionReplacementPluginEnvironmentState> BuildPlugins(CollectionReplacementDiffPlan diff)
		{
			var effects = diff.Effects.Where(x => x.Kind == CollectionReplacementEffectKind.Plugin)
				.ToDictionary(x => CollectionReplacementPluginEnvironmentState.NormalizePlugin(x.ResourceKey), StringComparer.OrdinalIgnoreCase);
			var result = new Dictionary<string, CollectionReplacementPluginEnvironmentState>(StringComparer.OrdinalIgnoreCase);
			foreach (CollectionNativePluginState plugin in diff.CurrentSetup.NativeState.Plugins.Values)
			{
				string key = CollectionReplacementPluginEnvironmentState.NormalizePlugin(plugin.FileName);
				CollectionReplacementEffectImpact effect;
				if (!effects.TryGetValue(key, out effect))
				{
					result[key] = new CollectionReplacementPluginEnvironmentState(key, null, null,
						"No C8.1 plugin effect classification exists for the observed plugin.");
					continue;
				}

				switch (effect.Disposition)
				{
					case CollectionReplacementEffectDisposition.Retained:
						result[key] = new CollectionReplacementPluginEnvironmentState(key, true, plugin.Active, effect.Reason);
						break;
					case CollectionReplacementEffectDisposition.PreservedUnknownOrUnmanaged:
						if (String.IsNullOrWhiteSpace(effect.CurrentOwnerKey) ||
							StringComparer.OrdinalIgnoreCase.Equals(effect.CurrentOwnerKey, effect.ProjectedOwnerKey))
							result[key] = new CollectionReplacementPluginEnvironmentState(key, true, plugin.Active, effect.Reason);
						else
							result[key] = new CollectionReplacementPluginEnvironmentState(key, null, null, effect.Reason);
						break;
					case CollectionReplacementEffectDisposition.WinnerChange:
					case CollectionReplacementEffectDisposition.OutgoingOnly:
						result[key] = new CollectionReplacementPluginEnvironmentState(key, true, null,
							"The plugin file remains visible, but native activation state after the owner transition is not proven by C8.1.");
						break;
					default:
						result[key] = new CollectionReplacementPluginEnvironmentState(key, null, null, effect.Reason);
						break;
				}
			}
			return result;
		}

		private static void ApplyIncoming(CollectionMemberEffectPreview preview,
			IDictionary<ModDeploymentTarget, CollectionReplacementFileEnvironmentState> files,
			IDictionary<CollectionNativeIniKey, CollectionReplacementIniEnvironmentState> ini,
			IDictionary<string, CollectionReplacementPluginEnvironmentState> plugins, ICollection<string> issues)
		{
			foreach (CollectionPlannedFileEffect file in preview.Files)
			{
				files[file.Target] = new CollectionReplacementFileEnvironmentState(file.Target,
					CollectionReplacementEnvironmentKnowledge.Known, true, null,
					"An already-reviewed incoming effect makes this path visible in dependency order.");
				string plugin = CollectionReplacementPluginEnvironmentState.NormalizePlugin(file.Target.RelativePath);
				if (IsPluginFile(plugin) && file.Target.Root == ModDeploymentRoot.Data)
				{
					CollectionReplacementPluginEnvironmentState previous;
					bool? active = plugins.TryGetValue(plugin, out previous) ? previous.Active : (bool?)false;
					plugins[plugin] = new CollectionReplacementPluginEnvironmentState(plugin, true, active,
						"An already-reviewed incoming file effect registers this plugin; activation follows explicit/previewed plugin effects.");
				}
			}
			foreach (CollectionPlannedIniEffect edit in preview.IniEdits)
				ini[edit.Key] = new CollectionReplacementIniEnvironmentState(edit.Key,
					CollectionReplacementEnvironmentKnowledge.Known, edit.Value, null,
					"An already-reviewed incoming INI effect supplies the effective value in dependency order.");
			foreach (CollectionPlannedPluginEffect effect in preview.PluginEffects)
			{
				if (effect.Kind != CollectionPlannedPluginEffectKind.Activation || !effect.Active.HasValue)
					continue;
				foreach (string path in effect.PluginPaths)
				{
					string plugin = CollectionReplacementPluginEnvironmentState.NormalizePlugin(path);
					CollectionReplacementPluginEnvironmentState previous;
					if (!plugins.TryGetValue(plugin, out previous) || previous.Registered != true)
					{
						issues.Add("Incoming plugin activation references a plugin whose registration is not proven in the projected environment: " + plugin + ".");
						plugins[plugin] = new CollectionReplacementPluginEnvironmentState(plugin, null, null,
							"Activation was requested before registration could be proven.");
						continue;
					}
					plugins[plugin] = new CollectionReplacementPluginEnvironmentState(plugin, true, effect.Active.Value,
						"An already-reviewed incoming plugin effect supplies the effective activation state.");
				}
			}
		}

		private static CollectionReplacementFileEnvironmentState UnprovableFile(ModDeploymentTarget target, string reason)
		{
			return new CollectionReplacementFileEnvironmentState(target, CollectionReplacementEnvironmentKnowledge.Unprovable,
				false, null, reason);
		}

		private static bool IsPluginFile(string path)
		{
			return path.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
				path.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);
		}
	}
}
