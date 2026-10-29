using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.ModManagement.LoadOrder
{
	/// <summary>Pure bounded diagnosis and stable dependency sorting; never downloads, disables alternatives or modifies a package.</summary>
	public static class LarianLoadOrderAnalyzer
	{
		private const int MaximumModules = 10000;
		private const int MaximumEdges = 200000;

		/// <summary>Proposes only uniquely identified, available changes, with curator constraints taking priority over prior positions.</summary>
		public static LoadOrderAnalysis Analyze(LarianLoadOrderDocument document, LoadOrderInventory inventory, LarianOrderPreference preference = null, IEnumerable<string> builtInModuleIdentities = null)
		{
			if (document == null || inventory == null) throw new ArgumentNullException(document == null ? nameof(document) : nameof(inventory));
			byte[] policy = preference?.Encode(); preference = LarianOrderPreference.Decode(policy);
			var issues = new List<LoadOrderIssue>();
			var steps = new List<LoadOrderRepairStep>();
			string[] original = document.Entries.Where(entry => !entry.IsProtected).Select(entry => entry.Identity).ToArray();
			bool complete = inventory.IsComplete && !inventory.Diagnostics.Any(issue => issue.Severity == LoadOrderDiagnosticSeverity.Error);
			foreach (LoadOrderDiagnostic diagnostic in inventory.Diagnostics) issues.Add(InventoryIssue(diagnostic));
			LoadOrderModuleInfo[] observed = inventory.Packages.SelectMany(package => package.Inspection.Modules).Take(MaximumModules + 1).ToArray();
			if (observed.Length > MaximumModules)
			{
				issues.Add(Issue(LoadOrderIssueKind.AnalysisLimit, "There are too many modules for a bounded repair plan.", "Reduce the package set and refresh before sorting."));
				return new LoadOrderAnalysis(document, inventory, policy, issues, steps, original, false);
			}
			Dictionary<string, LoadOrderModuleInfo> modules = observed.GroupBy(module => LarianLoadOrderDocument.Identity(module.Metadata.Identity))
				.Where(group => group.Count() == 1).ToDictionary(group => group.Key, group => group.Single());
			complete &= modules.Count == observed.Length && inventory.Packages.All(package => package.Inspection.Status == LoadOrderInspectionStatus.Complete);
			var protectedIds = new HashSet<string>(document.Entries.Where(entry => entry.IsProtected).Select(entry => entry.Identity));
			protectedIds.UnionWith(LoadOrderCapability.ValidateBuiltInIdentities(builtInModuleIdentities));
			var active = new HashSet<string>(original);
			var excluded = new HashSet<string>(preference?.Excluded ?? new string[0]);
			string[] curator = preference?.Source == "Curator" ? preference.Order.Where(id => preference.Managed.Contains(id)).ToArray() : new string[0];
			var chosen = new List<string>(original);
			foreach (string id in original.Where(id => !modules.ContainsKey(id) && !protectedIds.Contains(id)))
			{
				// Inventory matching cannot establish that a saved entry is unwanted, including intentional curator markers.
				issues.Add(complete ? Issue(LoadOrderIssueKind.MissingPackage, "NMM could not match this saved module to an inspected package.",
					"The saved entry is preserved. Verify the mod or curator's instructions; remove it manually only if you intend to exclude it.", new[] { id }, severity: LoadOrderDiagnosticSeverity.Warning)
					: Issue(LoadOrderIssueKind.UnverifiedModule, "NMM could not match this included module while package inspection is incomplete or ambiguous.",
						"This does not establish that the mod is missing or broken. Its saved entry is preserved. Review the package inspection details or export the diagnostic report before changing installed mods.", new[] { id }, severity: LoadOrderDiagnosticSeverity.Warning));
			}
			// Opaque saved entries may have dependencies we cannot inspect; do not sort around or discard them automatically.
			complete &= original.All(id => modules.ContainsKey(id) || protectedIds.Contains(id));
			foreach (string id in original.Where(excluded.Contains))
			{
				bool verifiedExclusion = complete && modules.TryGetValue(id, out LoadOrderModuleInfo excludedModule) && excludedModule.Kind == LoadOrderModuleKind.AddOn;
				issues.Add(Issue(LoadOrderIssueKind.SavedExclusionMismatch, "The current order includes a module excluded by the saved order policy.",
					verifiedExclusion ? "Automatic repair can restore the explicit exclusion without deleting its package." : "The saved entry is preserved while inspection is unresolved. Change its inclusion manually only if you intend to exclude it.", new[] { id }, verifiedExclusion, severity: LoadOrderDiagnosticSeverity.Warning));
				if (verifiedExclusion) { chosen.Remove(id); steps.Add(new LoadOrderRepairStep(LoadOrderRepairKind.RemoveExcludedRecord, id)); }
			}
			// A dependency closure is enabled only when the entire closure is available and compatible with explicit exclusions/conflicts.
			int closureBudget = 0;
			if (complete && !document.UsesLegacyModOrder)
			{
				foreach (string id in chosen.Concat(curator.Where(id => !chosen.Contains(id))).ToArray())
				{
					List<string> closure = DependencyClosure(id, modules, protectedIds, excluded, chosen, ref closureBudget);
					if (closure == null) continue;
					foreach (string dependency in closure.Where(dependency => !chosen.Contains(dependency))) chosen.Add(dependency);
					if (closureBudget > MaximumEdges) break;
				}
			}
			bool curatorReordered = false;
			if (curator.Length > 0)
			{
				string[] expected = curator.Where(chosen.Contains).ToArray();
				if (!chosen.Where(expected.Contains).SequenceEqual(expected))
				{
					curatorReordered = true;
					issues.Add(Issue(LoadOrderIssueKind.CuratorOrder, "The current order differs from the curator's supplied order.", "Automatic repair restores the curator's relative order.", expected, complete));
				}
				chosen = expected.Concat(chosen.Except(expected)).ToList();
			}
			var edges = chosen.ToDictionary(id => id, id => new HashSet<string>());
			string[] present = curator.Where(edges.ContainsKey).ToArray();
			for (int index = 1; index < present.Length; index++) edges[present[index - 1]].Add(present[index]);
			var chosenSet = new HashSet<string>(chosen);
			foreach (string id in curator.Where(id => !active.Contains(id)))
			{
				if (!modules.ContainsKey(id)) issues.Add(complete ? Issue(LoadOrderIssueKind.CuratorModuleUnavailable,
					"A module selected by the curator has no uniquely identified installed package.", "Restore the selected Collection package, then refresh. Automatic sorting cannot replace a missing download.", new[] { id })
					: Issue(LoadOrderIssueKind.UnverifiedModule, "NMM could not verify a curator-selected module because package inspection is incomplete or ambiguous.",
						"Review the package inspection details or export the report. NMM has not established that the Collection download is missing.", new[] { id }, severity: LoadOrderDiagnosticSeverity.Warning));
				else issues.Add(Issue(LoadOrderIssueKind.CuratorModuleInactive, "A module selected by the curator is not included in the current order.",
					chosenSet.Contains(id) ? "Automatic repair can restore this curator selection and its verified dependencies." : "Resolve this module's dependency chain, version requirements or declared incompatibilities before including it.", new[] { id }, complete && chosenSet.Contains(id)));
			}
			var originalPositions = original.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index);
			int edgeCount = 0;
			foreach (string id in chosen)
			{
				if (!modules.TryGetValue(id, out LoadOrderModuleInfo module)) continue;
				if (active.Contains(id) && module.Kind == LoadOrderModuleKind.AddOn && !document.UsesLegacyModOrder &&
					(LarianLoadOrderDocument.Field(document.Records[id], "Version64") != module.Metadata.Version || LarianLoadOrderDocument.Field(document.Records[id], "Folder") != module.Metadata.Folder || LarianLoadOrderDocument.Field(document.Records[id], "Name") != module.Metadata.Name))
				{
					issues.Add(Issue(LoadOrderIssueKind.NativeMetadata, "The saved module metadata differs from the inspected installed package.", "A metadata difference alone does not establish a broken mod. Review the saved and package values below; automatic repair can refresh these fields while retaining unrelated native fields.", new[] { id }, complete,
						severity: LoadOrderDiagnosticSeverity.Warning, technicalDetails: MetadataDifference(document, module)));
					if (complete) steps.Add(new LoadOrderRepairStep(LoadOrderRepairKind.RefreshMetadata, id));
				}
				foreach (LoadOrderModuleReference dependency in module.Dependencies)
				{
					if (++edgeCount > MaximumEdges) break;
					string parent = LarianLoadOrderDocument.Identity(dependency.Identity);
					if (protectedIds.Contains(parent)) continue;
					if (!modules.TryGetValue(parent, out LoadOrderModuleInfo required))
						issues.Add(complete ? Issue(LoadOrderIssueKind.MissingDependency, "An included mod declares a dependency not found in the inspected package folder.", "Check the author's requirements. Game-provided dependencies must be declared by the game configuration; sorting cannot supply a missing download.", new[] { id, parent })
							: Issue(LoadOrderIssueKind.UnverifiedDependency, "NMM could not verify a declared dependency while package inspection is incomplete or ambiguous.",
								"This is an unresolved check, not proof of a missing dependency. Review the package inspection details or export the report before changing installed mods.", new[] { id, parent }, severity: LoadOrderDiagnosticSeverity.Warning));
					else if (!chosenSet.Contains(parent))
						issues.Add(Issue(excluded.Contains(parent) ? LoadOrderIssueKind.ExcludedDependency : LoadOrderIssueKind.InactiveDependency,
							"A required installed dependency is excluded from the order.", "Include the dependency deliberately or remove the dependent mod. Automatic repair preserves explicit exclusions and unresolved dependency chains.", new[] { id, parent }));
					else
					{
						if (!active.Contains(parent)) issues.Add(Issue(LoadOrderIssueKind.InactiveDependency, "An installed dependency is not included in the current order.", "Automatic repair can include this uniquely identified dependency and its verified dependency chain.", new[] { id, parent }, complete));
						int parentCurator = Array.IndexOf(curator, parent), childCurator = Array.IndexOf(curator, id);
						if (parentCurator >= 0 && childCurator >= 0 && parentCurator > childCurator)
							issues.Add(Issue(LoadOrderIssueKind.CuratorDependencyConflict, "A declared dependency contradicts the curator's supplied order.", "The curator order takes precedence. Ask the curator to resolve the declared requirement before playing.", new[] { id, parent }));
						else
						{
							edges[parent].Add(id);
							if (active.Contains(parent) && active.Contains(id) && originalPositions[parent] > originalPositions[id])
								issues.Add(Issue(LoadOrderIssueKind.DependencyOrder, "A declared dependency loads after its dependent mod.", "Automatic sorting can place the dependency first while retaining curator constraints.", new[] { id, parent }, complete));
						}
					}
					if (required != null && VersionOlder(required.Metadata.Version, dependency.Version)) issues.Add(Issue(LoadOrderIssueKind.DependencyVersion,
						"The installed dependency version is older than the version declared by this mod.", "This compares declared metadata and does not prove runtime incompatibility. Check the author's requirements before updating. Automatic repair does not replace package files.", new[] { id, parent },
						severity: LoadOrderDiagnosticSeverity.Warning, technicalDetails: "Installed packed version: " + required.Metadata.Version + Environment.NewLine + "Declared packed version: " + dependency.Version));
				}
				foreach (LoadOrderModuleReference conflict in module.Conflicts.Where(conflict => chosenSet.Contains(LarianLoadOrderDocument.Identity(conflict.Identity)) || protectedIds.Contains(LarianLoadOrderDocument.Identity(conflict.Identity))))
					issues.Add(Issue(LoadOrderIssueKind.DeclaredConflict, "An included mod declares an incompatibility with another included or game-provided module.", "Choose a compatible version, author-provided patch, or remove the incompatible add-on. Sorting cannot resolve a declared incompatibility.", new[] { id, conflict.Identity }));
				if ((module.Features & LoadOrderContentFeatures.ScriptExtender) != 0 || module.RequiredScriptExtenderVersion.HasValue)
					issues.Add(new LoadOrderIssue(LoadOrderIssueKind.ScriptExtenderRequirement, LoadOrderDiagnosticSeverity.Information,
						module.RequiredScriptExtenderVersion.HasValue ? LanguageManager.Format("LoadOrder.Issues.ScriptExtenderRequirement.Version", "This mod declares Script Extender version {0} or newer.", module.RequiredScriptExtenderVersion.Value)
						: LanguageManager.Get("LoadOrder.Issues.ScriptExtenderRequirement.Message", "This mod declares Script Extender support."),
						LanguageManager.Get("LoadOrder.Issues.ScriptExtenderRequirement.Direction", "Verify that the required extender runtime is installed and compatible. Package inspection cannot verify the runtime loaded by the game."), new[] { id }));
				if (edgeCount > MaximumEdges) break;
			}
			bool bounded = edgeCount <= MaximumEdges && closureBudget <= MaximumEdges;
			if (!bounded) issues.Add(Issue(LoadOrderIssueKind.AnalysisLimit, "The dependency graph exceeds the supported repair bound.", "Reduce the package set before sorting."));
			List<string> sorted = bounded ? StableSort(chosen, edges) : null;
			if (sorted == null && bounded)
			{
				issues.Add(Issue(LoadOrderIssueKind.DependencyCycle, "The dependency constraints contain a cycle and cannot all be satisfied.", "Check the mods' dependency declarations or ask the curator to correct the Collection. Automatic repair will not choose an arbitrary cycle order.", chosen));
				// Do not introduce newly active modules when their order cannot be proven.
				chosen.RemoveAll(id => !active.Contains(id)); sorted = chosen;
				foreach (LoadOrderIssue issue in issues.Where(issue => issue.Kind == LoadOrderIssueKind.DependencyOrder || issue.Kind == LoadOrderIssueKind.InactiveDependency || issue.Kind == LoadOrderIssueKind.CuratorModuleInactive)) issue.AutomaticallyRepairable = false;
			}
			foreach (string id in chosen.Where(id => !active.Contains(id))) steps.Add(new LoadOrderRepairStep(curator.Contains(id) ? LoadOrderRepairKind.IncludeCuratorModule : LoadOrderRepairKind.IncludeDependency, id));
			if (sorted != null && !original.Where(sorted.Contains).SequenceEqual(sorted.Where(active.Contains)))
				steps.Add(new LoadOrderRepairStep(curatorReordered ? LoadOrderRepairKind.RestoreCuratorOrder : LoadOrderRepairKind.Sort));
			foreach (LoadOrderPackageObservation package in inventory.Packages)
			{
				if (package.Inspection.Kind == LoadOrderPackageKind.Override) issues.Add(Issue(LoadOrderIssueKind.OverrideAlwaysActive,
					"This package overrides game files and is effective independently of module inclusion.", "Disable or uninstall the package through NMM to stop its effects; removing an order entry is insufficient.", path: package.File.Path, severity: LoadOrderDiagnosticSeverity.Information));
				if ((package.Inspection.Features & LoadOrderContentFeatures.EmbeddedModFixer) != 0) issues.Add(Issue(LoadOrderIssueKind.EmbeddedModFixer,
					"This package contains embedded Mod Fixer content.", "Check the author's compatibility guidance for your game version. NMM will not rewrite the package to remove it.", path: package.File.Path, severity: LoadOrderDiagnosticSeverity.Information));
			}
			if (!complete || !bounded) foreach (LoadOrderIssue issue in issues) issue.AutomaticallyRepairable = false;
			return new LoadOrderAnalysis(document, inventory, policy, issues, complete && bounded ? steps : Enumerable.Empty<LoadOrderRepairStep>(), complete && bounded ? (IEnumerable<string>)sorted ?? original : original, complete && bounded);
		}

		/// <summary>Builds a bounded available dependency closure; the shared sort subsequently rejects cyclic inclusion.</summary>
		private static List<string> DependencyClosure(string root, IDictionary<string, LoadOrderModuleInfo> modules, ISet<string> protectedIds, ISet<string> excluded, IList<string> active, ref int count)
		{
			var pending = new Queue<string>(); pending.Enqueue(root);
			var closure = new List<string>(); var seen = new HashSet<string>();
			while (pending.Count > 0)
			{
				string id = pending.Dequeue();
				if (protectedIds.Contains(id) || !seen.Add(id)) continue;
				if (seen.Count > MaximumModules || excluded.Contains(id) || !modules.TryGetValue(id, out LoadOrderModuleInfo module) || module.Kind != LoadOrderModuleKind.AddOn) return null;
				closure.Add(id);
				foreach (LoadOrderModuleReference dependency in module.Dependencies)
				{
					if (++count > MaximumEdges) return null;
					string parent = LarianLoadOrderDocument.Identity(dependency.Identity);
					if (protectedIds.Contains(parent)) continue;
					if (!modules.TryGetValue(parent, out LoadOrderModuleInfo required) || VersionOlder(required.Metadata.Version, dependency.Version)) return null;
					pending.Enqueue(parent);
				}
			}
			var effective = new HashSet<string>(active.Concat(closure));
			if (closure.All(active.Contains)) return closure;
			effective.UnionWith(protectedIds);
			if (modules.Where(item => effective.Contains(item.Key)).Any(item => item.Value.Conflicts.Any(conflict => effective.Contains(LarianLoadOrderDocument.Identity(conflict.Identity))))) return null;
			return closure;
		}

		/// <summary>Kahn sorting uses existing positions as the deterministic tie-breaker; null denotes an unsatisfied cycle.</summary>
		private static List<string> StableSort(IList<string> original, IDictionary<string, HashSet<string>> edges)
		{
			var positions = original.Select((id, index) => new { id, index }).ToDictionary(item => item.id, item => item.index);
			var indegree = original.ToDictionary(id => id, id => 0);
			foreach (HashSet<string> targets in edges.Values) foreach (string target in targets) indegree[target]++;
			var ready = new SortedSet<int>(original.Where(id => indegree[id] == 0).Select(id => positions[id]));
			var result = new List<string>();
			while (ready.Count > 0)
			{
				int index = ready.Min; ready.Remove(index); string id = original[index]; result.Add(id);
				foreach (string target in edges[id]) if (--indegree[target] == 0) ready.Add(positions[target]);
			}
			return result.Count == original.Count ? result : null;
		}

		/// <summary>Compares lossless packed version observations, without guessing compatibility or downloading updates.</summary>
		private static bool VersionOlder(string installed, string declared) => UInt64.TryParse(installed, out ulong actual) && UInt64.TryParse(declared, out ulong required) && actual < required;

		/// <summary>Supplies actionable directory and inspection directions through existing localization.</summary>
		private static LoadOrderIssue InventoryIssue(LoadOrderDiagnostic diagnostic)
		{
			string message, direction;
			switch (diagnostic.Kind)
			{
				case LoadOrderDiagnosticKind.UnexpectedFile:
				case LoadOrderDiagnosticKind.UnexpectedDirectory:
					message = "The package folder contains a non-package file or subfolder. This can cause the game to reset the custom order.";
					direction = "Move unrelated content outside the package folder. For NMM-managed content, disable or reinstall its owning mod with the correct layout; do not flatten subfolders blindly."; break;
				case LoadOrderDiagnosticKind.DuplicateModuleIdentity:
					message = "Multiple packages declare the same module UUID."; direction = "Keep the intended version through NMM's installation controls. Automatic repair cannot choose a package for you."; break;
				case LoadOrderDiagnosticKind.InspectorUnavailable:
					message = "The package inspection helper is unavailable."; direction = "Install the complete NMM distribution including its load-order helper, then refresh."; break;
				case LoadOrderDiagnosticKind.PackageInspectionFailed:
					message = "NMM could not inspect this package."; direction = String.Empty; break;
				case LoadOrderDiagnosticKind.PackageInspectionIncomplete:
					message = "NMM could only partially inspect this package."; direction = String.Empty; break;
				default:
					message = "The package inventory could not be fully verified."; direction = "Check the indicated path and package, resolve the inspection error, then refresh. Editing remains blocked while evidence is incomplete."; break;
			}
			bool inspectionUnknown = diagnostic.Kind != LoadOrderDiagnosticKind.UnexpectedFile && diagnostic.Kind != LoadOrderDiagnosticKind.UnexpectedDirectory && diagnostic.Kind != LoadOrderDiagnosticKind.DuplicateModuleIdentity;
			if (inspectionUnknown) direction = "This is an NMM inspection limitation or read failure, not proof that the package is invalid for the game. Export the diagnostic report and review the technical details. Editing remains blocked while evidence is incomplete.";
			return new LoadOrderIssue(LoadOrderIssueKind.Inventory, inspectionUnknown ? LoadOrderDiagnosticSeverity.Warning : diagnostic.Severity,
				LanguageManager.Get(diagnostic.LocalizationKey + ".Message", message), LanguageManager.Get(diagnostic.LocalizationKey + ".Direction", direction),
				diagnostic.ModuleIdentity == null ? null : new[] { diagnostic.ModuleIdentity }, diagnostic.Path, technicalDetails: diagnostic.Kind + Environment.NewLine + diagnostic.TechnicalDetails);
		}

		/// <summary>Retains the exact compared fields so a metadata difference can be assessed without guessing its cause.</summary>
		private static string MetadataDifference(LarianLoadOrderDocument document, LoadOrderModuleInfo module)
		{
			return String.Join(Environment.NewLine, new[] { "Name", "Folder", "Version64" }.Select(field => field + ": saved = " + LarianLoadOrderDocument.Field(document.Records[module.Metadata.Identity], field)
				+ "; package = " + (field == "Name" ? module.Metadata.Name : field == "Folder" ? module.Metadata.Folder : module.Metadata.Version)));
		}

		/// <summary>Produces consistent localization keys for diagnosis and its next action.</summary>
		private static LoadOrderIssue Issue(LoadOrderIssueKind kind, string message, string direction, IEnumerable<string> identities = null,
			bool automatic = false, string path = null, LoadOrderDiagnosticSeverity severity = LoadOrderDiagnosticSeverity.Error, string technicalDetails = null) => new LoadOrderIssue(kind, severity,
				LanguageManager.Get("LoadOrder.Issues." + kind + ".Message", message), LanguageManager.Get("LoadOrder.Issues." + kind + ".Direction", direction), identities, path, automatic, technicalDetails);
	}
}
