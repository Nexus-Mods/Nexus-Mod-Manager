using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexus.Client.Plugins;

namespace Nexus.Client.PluginManagement
{
    public enum PluginValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

	public enum PluginRestrictionMode
	{
		Enforced = 0,
		Disabled = 1
	}

	public enum PluginValidationIssueKind
    {
        MissingMaster,
        InactiveRequiredMaster,
        MasterBelowDependent,
        DependencyCycle,
        UnsupportedPluginClass,
        AddressSpaceExhausted,
        InvalidFixedPluginPlacement
    }

    public sealed class PluginValidationDiagnostic
    {
        public PluginValidationDiagnostic(PluginValidationIssueKind kind, PluginValidationSeverity severity, Plugin plugin, string message)
        {
            Kind = kind;
            Severity = severity;
            Plugin = plugin;
            Message = message ?? String.Empty;
        }

        public PluginValidationIssueKind Kind { get; private set; }
        public PluginValidationSeverity Severity { get; private set; }
        public Plugin Plugin { get; private set; }
        public string Message { get; private set; }
    }

    public sealed class PluginSnapshotEntry
    {
        public PluginSnapshotEntry(Plugin plugin, bool active, int priority, int? allocatedIndex, string modIndex, IList<PluginValidationDiagnostic> diagnostics)
        {
            Plugin = plugin;
            Active = active;
            Priority = priority;
            AllocatedIndex = allocatedIndex;
            ModIndex = modIndex ?? String.Empty;
            Diagnostics = diagnostics == null ? new List<PluginValidationDiagnostic>() : new List<PluginValidationDiagnostic>(diagnostics);
        }

        public Plugin Plugin { get; private set; }
        public bool Active { get; private set; }
        public int Priority { get; private set; }
        public int? AllocatedIndex { get; private set; }
        public string ModIndex { get; private set; }
        public List<PluginValidationDiagnostic> Diagnostics { get; private set; }
        public bool HasErrors { get { return Diagnostics.Any(x => x.Severity == PluginValidationSeverity.Error); } }
        public string EffectiveType { get { return Plugin == null ? String.Empty : Plugin.EffectiveTypeDisplay; } }
    }

    public sealed class PluginSnapshot
    {
        private readonly Dictionary<Plugin, PluginSnapshotEntry> m_dicEntriesByPlugin;

        public PluginSnapshot(IList<PluginSnapshotEntry> entries, IList<PluginValidationDiagnostic> diagnostics)
        {
            Entries = entries == null ? new List<PluginSnapshotEntry>() : new List<PluginSnapshotEntry>(entries);
            Diagnostics = diagnostics == null ? new List<PluginValidationDiagnostic>() : new List<PluginValidationDiagnostic>(diagnostics);
            m_dicEntriesByPlugin = new Dictionary<Plugin, PluginSnapshotEntry>();
            foreach (PluginSnapshotEntry entry in Entries)
                if (entry.Plugin != null && !m_dicEntriesByPlugin.ContainsKey(entry.Plugin))
                    m_dicEntriesByPlugin.Add(entry.Plugin, entry);
        }

        public List<PluginSnapshotEntry> Entries { get; private set; }
        public List<PluginValidationDiagnostic> Diagnostics { get; private set; }
        public bool HasErrors { get { return Diagnostics.Any(x => x.Severity == PluginValidationSeverity.Error); } }

        public PluginSnapshotEntry GetEntry(Plugin plugin)
        {
            if (plugin == null)
                return null;
            PluginSnapshotEntry entry;
            return m_dicEntriesByPlugin.TryGetValue(plugin, out entry) ? entry : null;
        }
    }

    public sealed class PluginSnapshotBuilder
    {
		/// <summary>
		/// Describes the traversal state of a plugin while producing a dependency-corrected order.
		/// </summary>
		private enum PluginTraversalState
		{
			Visiting,
			Visited
		}

		/// <summary>
		/// Stores the current position of an iterative dependency traversal.
		/// </summary>
		private sealed class PluginTraversalFrame
		{
			/// <summary>
			/// Initializes a traversal frame for the specified plugin.
			/// </summary>
			/// <param name="p_plgPlugin">The plugin being traversed.</param>
			public PluginTraversalFrame(Plugin p_plgPlugin)
			{
				Plugin = p_plgPlugin;
			}

			/// <summary>
			/// Gets the plugin represented by the frame.
			/// </summary>
			public Plugin Plugin { get; private set; }

			/// <summary>
			/// Gets or sets the index of the next master to visit.
			/// </summary>
			public int NextMasterIndex { get; set; }
		}

		public PluginSnapshot Build(PluginManagementPolicy policy, IList<Plugin> orderedPlugins, ISet<Plugin> activePlugins)
		{
			return Build(policy, orderedPlugins, activePlugins,	PluginRestrictionMode.Enforced);
		}

		public PluginSnapshot Build(PluginManagementPolicy policy, IList<Plugin> orderedPlugins, ISet<Plugin> activePlugins, PluginRestrictionMode restrictionMode)
		{
			if (policy == null)
                policy = new PluginManagementPolicy();
            orderedPlugins = orderedPlugins ?? new List<Plugin>();
            activePlugins = activePlugins ?? new HashSet<Plugin>();

            Dictionary<string, Plugin> pluginsByName = BuildPluginNameLookup(orderedPlugins);
            Dictionary<string, int> priorityByName = BuildPriorityLookup(orderedPlugins);
			Dictionary<string, int> expectedFixedPriorityByName = restrictionMode == PluginRestrictionMode.Enforced ? BuildExpectedFixedPriorityLookup(policy, orderedPlugins, pluginsByName) : null;
			Dictionary<PluginAddressClass, int> allocatedCounts = new Dictionary<PluginAddressClass, int>();
            Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin = new Dictionary<Plugin, List<PluginValidationDiagnostic>>();
            List<PluginValidationDiagnostic> diagnostics = new List<PluginValidationDiagnostic>();
            List<PluginSnapshotEntry> entries = new List<PluginSnapshotEntry>();

            for (int i = 0; i < orderedPlugins.Count; i++)
            {
                Plugin plugin = orderedPlugins[i];
                bool active = plugin != null && activePlugins.Contains(plugin);
                int? allocatedIndex = null;
                string modIndex = String.Empty;

                if (plugin != null && active && plugin.Metadata.AddressClass != PluginAddressClass.None)
                {
                    PluginAddressSpacePolicy addressSpace = policy.GetAddressSpace(plugin.Metadata.AddressClass);
                    if (addressSpace == null)
                    {
                        AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.UnsupportedPluginClass, PluginValidationSeverity.Error, "Plugin class is not supported by this game policy.");
                    }
                    else
                    {
                        int usedCount;
                        allocatedCounts.TryGetValue(plugin.Metadata.AddressClass, out usedCount);
                        if (addressSpace.MaxCount > 0 && usedCount >= addressSpace.MaxCount)
                        {
                            AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.AddressSpaceExhausted, PluginValidationSeverity.Error, "Plugin address space is exhausted.");
                        }
                        else
                        {
                            allocatedIndex = addressSpace.FirstIndex + usedCount;
                            modIndex = addressSpace.Format(allocatedIndex.Value);
                            allocatedCounts[plugin.Metadata.AddressClass] = usedCount + 1;
                        }
                    }
                }

				ValidatePlugin(policy, plugin, active, i, pluginsByName, priorityByName, activePlugins,	diagnostics, diagnosticsByPlugin, restrictionMode);

				if (restrictionMode == PluginRestrictionMode.Enforced)
					ValidateFixedPluginPlacement(plugin, i, expectedFixedPriorityByName, diagnostics, diagnosticsByPlugin);

				List<PluginValidationDiagnostic> entryDiagnostics = null;
                if (plugin != null)
                    diagnosticsByPlugin.TryGetValue(plugin, out entryDiagnostics);
                entries.Add(new PluginSnapshotEntry(plugin, active, i, allocatedIndex, modIndex, entryDiagnostics));
            }

            DetectDependencyCycles(orderedPlugins, pluginsByName, diagnostics, diagnosticsByPlugin, restrictionMode);
            foreach (PluginSnapshotEntry entry in entries)
            {
                List<PluginValidationDiagnostic> entryDiagnostics;
                if (entry.Plugin != null && diagnosticsByPlugin.TryGetValue(entry.Plugin, out entryDiagnostics))
                {
                    entry.Diagnostics.Clear();
                    entry.Diagnostics.AddRange(entryDiagnostics);
                }
            }
            return new PluginSnapshot(entries, diagnostics);
        }

        public List<Plugin> CorrectStable(PluginManagementPolicy policy, IList<Plugin> orderedPlugins)
        {
            List<Plugin> corrected = orderedPlugins == null ? new List<Plugin>() : new List<Plugin>(orderedPlugins);
            if (policy == null || corrected.Count < 2)
                return corrected;

            StableMoveFixedPlugins(policy, corrected);
            StableMoveMastersBeforeNonMasters(policy, corrected);
            StableMoveMastersAboveDependents(corrected);
            StableMoveBlueprintPluginsLate(corrected);
            return corrected;
        }

		private static void StableMoveFixedPlugins(
			PluginManagementPolicy policy,
			List<Plugin> plugins)
		{
			if (policy == null || plugins == null || plugins.Count < 2)
				return;

			if (!policy.MasterPluginsMustLoadBeforeNonMasters)
			{
				StableMoveFixedPluginsWithinSection(policy, plugins);
				return;
			}

			List<Plugin> masters = plugins
				.Where(
					x =>
						x != null &&
						x.Metadata != null &&
						x.Metadata.EffectiveMaster)
				.ToList();

			List<Plugin> nonMasters = plugins
				.Where(
					x =>
						x == null ||
						x.Metadata == null ||
						!x.Metadata.EffectiveMaster)
				.ToList();

			/*
			 * Fixed masters are pinned to the beginning of the master section.
			 * Fixed non-masters are pinned to the beginning of the non-master
			 * section.
			 */
			StableMoveFixedPluginsWithinSection(policy, masters);
			StableMoveFixedPluginsWithinSection(policy, nonMasters);

			plugins.Clear();
			plugins.AddRange(masters);
			plugins.AddRange(nonMasters);
		}

		private static void StableMoveFixedPluginsWithinSection(
			PluginManagementPolicy policy,
			List<Plugin> plugins)
		{
			int targetIndex = 0;

			foreach (string fixedPluginName in policy.FixedOrderPlugins)
			{
				string normalizedFixedName =
					NormalizePluginName(fixedPluginName);

				int pluginIndex = plugins.FindIndex(
					x =>
						x != null &&
						String.Equals(
							NormalizePluginName(x.Filename),
							normalizedFixedName,
							StringComparison.OrdinalIgnoreCase));

				/*
				 * A fixed plugin belonging to the other section simply will not
				 * be present in this list.
				 */
				if (pluginIndex < 0)
					continue;

				Plugin plugin = plugins[pluginIndex];

				if (pluginIndex != targetIndex)
				{
					plugins.RemoveAt(pluginIndex);
					plugins.Insert(targetIndex, plugin);
				}

				targetIndex++;
			}
		}

		private static void StableMoveMastersBeforeNonMasters(PluginManagementPolicy policy, List<Plugin> plugins)
        {
            if (!policy.MasterPluginsMustLoadBeforeNonMasters)
                return;

            List<Plugin> masters = plugins.Where(x => x != null && x.Metadata.EffectiveMaster).ToList();
            List<Plugin> nonMasters = plugins.Where(x => x == null || !x.Metadata.EffectiveMaster).ToList();
            plugins.Clear();
            plugins.AddRange(masters);
            plugins.AddRange(nonMasters);
        }

		/// <summary>
		/// Moves every installed master above its dependents while preserving the existing stable order whenever dependency constraints allow it.
		/// </summary>
		/// <param name="p_lstPlugins">The plugin order to correct.</param>
		private static void StableMoveMastersAboveDependents(List<Plugin> p_lstPlugins)
		{
			if (p_lstPlugins == null || p_lstPlugins.Count < 2)
				return;

			Dictionary<string, Plugin> dicPluginsByName = BuildPluginNameLookup(p_lstPlugins);
			Dictionary<Plugin, PluginTraversalState> dicTraversalStates = new Dictionary<Plugin, PluginTraversalState>();
			List<Plugin> lstCorrected = new List<Plugin>(p_lstPlugins.Count);

			foreach (Plugin plgPlugin in p_lstPlugins)
				AppendPluginWithMasters(plgPlugin, dicPluginsByName, dicTraversalStates, lstCorrected);

			p_lstPlugins.Clear();
			p_lstPlugins.AddRange(lstCorrected);
		}

		/// <summary>
		/// Appends a plugin after all of its installed masters using an iterative depth-first traversal.
		/// </summary>
		/// <param name="p_plgRootPlugin">The root plugin to append.</param>
		/// <param name="p_dicPluginsByName">The installed plugins indexed by normalized file name.</param>
		/// <param name="p_dicTraversalStates">The current traversal states.</param>
		/// <param name="p_lstCorrected">The dependency-corrected output order.</param>
		private static void AppendPluginWithMasters(Plugin p_plgRootPlugin, IDictionary<string, Plugin> p_dicPluginsByName, IDictionary<Plugin, PluginTraversalState> p_dicTraversalStates, IList<Plugin> p_lstCorrected)
		{
			if (p_plgRootPlugin == null)
			{
				p_lstCorrected.Add(null);
				return;
			}

			PluginTraversalState ptsRootState;

			if (p_dicTraversalStates.TryGetValue(p_plgRootPlugin, out ptsRootState))
				return;

			Stack<PluginTraversalFrame> stkTraversal = new Stack<PluginTraversalFrame>();
			p_dicTraversalStates[p_plgRootPlugin] = PluginTraversalState.Visiting;
			stkTraversal.Push(new PluginTraversalFrame(p_plgRootPlugin));

			while (stkTraversal.Count > 0)
			{
				PluginTraversalFrame ptfFrame = stkTraversal.Peek();
				IList<string> lstMasters = ptfFrame.Plugin.Masters;
				bool booMasterPushed = false;

				while (lstMasters != null && ptfFrame.NextMasterIndex < lstMasters.Count)
				{
					string strMasterName = lstMasters[ptfFrame.NextMasterIndex];
					ptfFrame.NextMasterIndex++;

					Plugin plgMaster;

					if (!p_dicPluginsByName.TryGetValue(NormalizePluginName(strMasterName), out plgMaster) || plgMaster == null)
						continue;

					PluginTraversalState ptsMasterState;

					if (p_dicTraversalStates.TryGetValue(plgMaster, out ptsMasterState))
					{
						/*
						 * Visited masters are already correctly placed.
						 * Visiting masters identify a dependency cycle; the snapshot
						 * validator will report it after ordering completes.
						 */
						continue;
					}

					p_dicTraversalStates[plgMaster] = PluginTraversalState.Visiting;
					stkTraversal.Push(new PluginTraversalFrame(plgMaster));
					booMasterPushed = true;
					break;
				}

				if (booMasterPushed)
					continue;

				stkTraversal.Pop();
				p_dicTraversalStates[ptfFrame.Plugin] = PluginTraversalState.Visited;
				p_lstCorrected.Add(ptfFrame.Plugin);
			}
		}

		private static void StableMoveBlueprintPluginsLate(List<Plugin> plugins)
        {
            List<Plugin> blueprintPlugins = plugins.Where(x => x != null && (x.Metadata.SpecialFlags & PluginSpecialFlags.Blueprint) == PluginSpecialFlags.Blueprint).ToList();
            if (blueprintPlugins.Count == 0)
                return;

            plugins.RemoveAll(x => x != null && (x.Metadata.SpecialFlags & PluginSpecialFlags.Blueprint) == PluginSpecialFlags.Blueprint);
            plugins.AddRange(blueprintPlugins);
        }

		/// <summary>
		/// Builds the expected absolute priorities of all installed fixed-order plugins.
		/// </summary>
		/// <param name="p_pmpPolicy">The active plugin-management policy.</param>
		/// <param name="p_lstOrderedPlugins">The current ordered plugins.</param>
		/// <param name="p_dicPluginsByName">The installed plugins indexed by normalized file name.</param>
		/// <returns>The expected priorities indexed by normalized plugin file name.</returns>
		private static Dictionary<string, int> BuildExpectedFixedPriorityLookup(PluginManagementPolicy p_pmpPolicy, IList<Plugin> p_lstOrderedPlugins, IDictionary<string, Plugin> p_dicPluginsByName)
		{
			Dictionary<string, int> dicExpectedPriorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

			if (p_pmpPolicy == null || p_lstOrderedPlugins == null || p_dicPluginsByName == null)
				return dicExpectedPriorities;

			bool booUseMasterSections = p_pmpPolicy.MasterPluginsMustLoadBeforeNonMasters;
			int intMasterCount = booUseMasterSections
				? p_lstOrderedPlugins.Count(x => x != null && x.Metadata != null && x.Metadata.EffectiveMaster)
				: 0;

			int intGlobalOffset = 0;
			int intMasterOffset = 0;
			int intNonMasterOffset = 0;

			foreach (string strFixedPluginName in p_pmpPolicy.FixedOrderPlugins)
			{
				string strNormalizedName = NormalizePluginName(strFixedPluginName);
				Plugin plgFixedPlugin;

				if (!p_dicPluginsByName.TryGetValue(strNormalizedName, out plgFixedPlugin) || plgFixedPlugin == null || plgFixedPlugin.Metadata == null)
					continue;

				int intExpectedPriority;

				if (!booUseMasterSections)
				{
					intExpectedPriority = intGlobalOffset;
					intGlobalOffset++;
				}
				else if (plgFixedPlugin.Metadata.EffectiveMaster)
				{
					intExpectedPriority = intMasterOffset;
					intMasterOffset++;
				}
				else
				{
					intExpectedPriority = intMasterCount + intNonMasterOffset;
					intNonMasterOffset++;
				}

				if (!dicExpectedPriorities.ContainsKey(strNormalizedName))
					dicExpectedPriorities.Add(strNormalizedName, intExpectedPriority);
			}

			return dicExpectedPriorities;
		}

		/// <summary>
		/// Validates the position of a fixed-order plugin against the precomputed expected priority.
		/// </summary>
		/// <param name="p_plgPlugin">The plugin to validate.</param>
		/// <param name="p_intPriority">The current plugin priority.</param>
		/// <param name="p_dicExpectedPriorities">The expected fixed-plugin priorities indexed by normalized file name.</param>
		/// <param name="p_lstDiagnostics">The complete diagnostic collection.</param>
		/// <param name="p_dicDiagnosticsByPlugin">The diagnostics grouped by plugin.</param>
		private static void ValidateFixedPluginPlacement(Plugin p_plgPlugin, int p_intPriority, IDictionary<string, int> p_dicExpectedPriorities, List<PluginValidationDiagnostic> p_lstDiagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> p_dicDiagnosticsByPlugin)
		{
			if (p_plgPlugin == null || p_plgPlugin.Metadata == null || p_dicExpectedPriorities == null)
				return;

			int intExpectedPriority;

			if (!p_dicExpectedPriorities.TryGetValue(NormalizePluginName(p_plgPlugin.Filename), out intExpectedPriority) || p_intPriority == intExpectedPriority)
				return;

			string strSectionName = p_plgPlugin.Metadata.EffectiveMaster ? "master" : "non-master";

			AddDiagnostic(
				p_lstDiagnostics,
				p_dicDiagnosticsByPlugin,
				p_plgPlugin,
				PluginValidationIssueKind.InvalidFixedPluginPlacement,
				PluginValidationSeverity.Error,
				"Fixed-order plugin is not in its configured position within the " + strSectionName + " section.");
		}

		private static void ValidatePlugin(PluginManagementPolicy policy, Plugin plugin, bool active, int priority, Dictionary<string, Plugin> pluginsByName, Dictionary<string, int> priorityByName, ISet<Plugin> activePlugins,
			List<PluginValidationDiagnostic> diagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin, PluginRestrictionMode restrictionMode)
		{
			if (plugin == null)
				return;

			if (!policy.ValidateDependencies || plugin.Masters == null)
				return;

			PluginValidationSeverity restrictionSeverity =	restrictionMode == PluginRestrictionMode.Disabled ? PluginValidationSeverity.Warning : PluginValidationSeverity.Error;

			foreach (string masterName in plugin.Masters)
			{
				string normalizedMasterName = NormalizePluginName(masterName);

				Plugin master;
				if (!pluginsByName.TryGetValue(normalizedMasterName, out master))
				{
					PluginValidationSeverity missingMasterSeverity =
						restrictionMode == PluginRestrictionMode.Disabled || !active
							? PluginValidationSeverity.Warning
							: PluginValidationSeverity.Error;

					AddDiagnostic(
						diagnostics,
						diagnosticsByPlugin,
						plugin,
						PluginValidationIssueKind.MissingMaster,
						missingMasterSeverity,
						"Missing master: " + masterName);

					continue;
				}

				if (active && !activePlugins.Contains(master))
				{
					AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.InactiveRequiredMaster, restrictionSeverity, "Required master is inactive: " + masterName);
				}

				int masterPriority;
				if (priorityByName.TryGetValue(normalizedMasterName, out masterPriority) &&	masterPriority > priority)
				{
					AddDiagnostic(
						diagnostics,
						diagnosticsByPlugin,
						plugin,
						PluginValidationIssueKind.MasterBelowDependent,
						restrictionSeverity,
						"Required master loads below dependent: " + masterName);
				}
			}
		}

		private static void DetectDependencyCycles(
			IList<Plugin> orderedPlugins,
			Dictionary<string, Plugin> pluginsByName,
			List<PluginValidationDiagnostic> diagnostics,
			Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin,
			PluginRestrictionMode restrictionMode)
		{
			HashSet<string> visiting =
				new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			HashSet<string> visited =
				new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (Plugin plugin in orderedPlugins)
			{
				DetectDependencyCycles(
					plugin,
					pluginsByName,
					visiting,
					visited,
					diagnostics,
					diagnosticsByPlugin,
					restrictionMode);
			}
		}

		private static void DetectDependencyCycles(
			Plugin plugin,
			Dictionary<string, Plugin> pluginsByName,
			HashSet<string> visiting,
			HashSet<string> visited,
			List<PluginValidationDiagnostic> diagnostics,
			Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin,
			PluginRestrictionMode restrictionMode)
		{
			if (plugin == null)
				return;

			string pluginName = NormalizePluginName(plugin.Filename);

			if (visited.Contains(pluginName))
				return;

			if (visiting.Contains(pluginName))
			{
				AddDiagnostic(
					diagnostics,
					diagnosticsByPlugin,
					plugin,
					PluginValidationIssueKind.DependencyCycle,
					restrictionMode == PluginRestrictionMode.Disabled
						? PluginValidationSeverity.Warning
						: PluginValidationSeverity.Error,
					"Dependency cycle detected.");

				return;
			}

			visiting.Add(pluginName);

			foreach (string masterName in plugin.Masters ?? new List<string>())
			{
				Plugin master;
				if (pluginsByName.TryGetValue(
						NormalizePluginName(masterName),
						out master))
				{
					DetectDependencyCycles(
						master,
						pluginsByName,
						visiting,
						visited,
						diagnostics,
						diagnosticsByPlugin,
						restrictionMode);
				}
			}

			visiting.Remove(pluginName);
			visited.Add(pluginName);
		}

		private static void AddDiagnostic(List<PluginValidationDiagnostic> diagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin, Plugin plugin, PluginValidationIssueKind kind, PluginValidationSeverity severity, string message)
        {
            PluginValidationDiagnostic diagnostic = new PluginValidationDiagnostic(kind, severity, plugin, message);
            diagnostics.Add(diagnostic);
            if (plugin == null)
                return;

            List<PluginValidationDiagnostic> pluginDiagnostics;
            if (!diagnosticsByPlugin.TryGetValue(plugin, out pluginDiagnostics))
            {
                pluginDiagnostics = new List<PluginValidationDiagnostic>();
                diagnosticsByPlugin.Add(plugin, pluginDiagnostics);
            }
            pluginDiagnostics.Add(diagnostic);
        }

        private static Dictionary<string, Plugin> BuildPluginNameLookup(IEnumerable<Plugin> plugins)
        {
            Dictionary<string, Plugin> lookup = new Dictionary<string, Plugin>(StringComparer.OrdinalIgnoreCase);
            foreach (Plugin plugin in plugins ?? new List<Plugin>())
            {
                string name = NormalizePluginName(plugin == null ? null : plugin.Filename);
                if (!String.IsNullOrEmpty(name) && !lookup.ContainsKey(name))
                    lookup.Add(name, plugin);
            }
            return lookup;
        }

        private static Dictionary<string, int> BuildPriorityLookup(IList<Plugin> plugins)
        {
            Dictionary<string, int> lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (plugins == null)
                return lookup;
            for (int i = 0; i < plugins.Count; i++)
            {
                string name = NormalizePluginName(plugins[i] == null ? null : plugins[i].Filename);
                if (!String.IsNullOrEmpty(name) && !lookup.ContainsKey(name))
                    lookup.Add(name, i);
            }
            return lookup;
        }

        private static string NormalizePluginName(string pluginName)
        {
            return String.IsNullOrWhiteSpace(pluginName) ? String.Empty : Path.GetFileName(pluginName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
        }
    }
}
