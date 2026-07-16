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
        public PluginSnapshot Build(PluginManagementPolicy policy, IList<Plugin> orderedPlugins, ISet<Plugin> activePlugins)
        {
            if (policy == null)
                policy = new PluginManagementPolicy();
            orderedPlugins = orderedPlugins ?? new List<Plugin>();
            activePlugins = activePlugins ?? new HashSet<Plugin>();

            Dictionary<string, Plugin> pluginsByName = BuildPluginNameLookup(orderedPlugins);
            Dictionary<string, int> priorityByName = BuildPriorityLookup(orderedPlugins);
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

                ValidatePlugin(policy, plugin, active, i, pluginsByName, priorityByName, activePlugins, diagnostics, diagnosticsByPlugin);
                ValidateFixedPluginPlacement(policy, plugin, i, priorityByName, diagnostics, diagnosticsByPlugin);

                List<PluginValidationDiagnostic> entryDiagnostics = null;
                if (plugin != null)
                    diagnosticsByPlugin.TryGetValue(plugin, out entryDiagnostics);
                entries.Add(new PluginSnapshotEntry(plugin, active, i, allocatedIndex, modIndex, entryDiagnostics));
            }

            DetectDependencyCycles(orderedPlugins, pluginsByName, diagnostics, diagnosticsByPlugin);
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

        private static void StableMoveFixedPlugins(PluginManagementPolicy policy, List<Plugin> plugins)
        {
            int targetIndex = 0;
            foreach (string fixedPluginName in policy.FixedOrderPlugins)
            {
                int pluginIndex = plugins.FindIndex(x => x != null && String.Equals(NormalizePluginName(x.Filename), NormalizePluginName(fixedPluginName), StringComparison.OrdinalIgnoreCase));
                if (pluginIndex < 0)
                    continue;

                Plugin plugin = plugins[pluginIndex];
                plugins.RemoveAt(pluginIndex);
                if (targetIndex > plugins.Count)
                    plugins.Add(plugin);
                else
                    plugins.Insert(targetIndex, plugin);
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

        private static void StableMoveMastersAboveDependents(List<Plugin> plugins)
        {
            Dictionary<string, Plugin> lookup = BuildPluginNameLookup(plugins);
            bool changed;
            int guard = 0;
            do
            {
                changed = false;
                guard++;
                for (int i = 0; i < plugins.Count; i++)
                {
                    Plugin plugin = plugins[i];
                    if (plugin == null || plugin.Masters == null)
                        continue;
                    foreach (string masterName in plugin.Masters)
                    {
                        Plugin master;
                        if (!lookup.TryGetValue(NormalizePluginName(masterName), out master))
                            continue;
                        int masterIndex = plugins.IndexOf(master);
                        int pluginIndex = plugins.IndexOf(plugin);
                        if (masterIndex > pluginIndex)
                        {
                            plugins.RemoveAt(masterIndex);
                            plugins.Insert(pluginIndex, master);
                            changed = true;
                        }
                    }
                }
            }
            while (changed && guard < plugins.Count + 1);
        }

        private static void StableMoveBlueprintPluginsLate(List<Plugin> plugins)
        {
            List<Plugin> blueprintPlugins = plugins.Where(x => x != null && (x.Metadata.SpecialFlags & PluginSpecialFlags.Blueprint) == PluginSpecialFlags.Blueprint).ToList();
            if (blueprintPlugins.Count == 0)
                return;

            plugins.RemoveAll(x => x != null && (x.Metadata.SpecialFlags & PluginSpecialFlags.Blueprint) == PluginSpecialFlags.Blueprint);
            plugins.AddRange(blueprintPlugins);
        }

        private static void ValidateFixedPluginPlacement(PluginManagementPolicy policy, Plugin plugin, int priority, Dictionary<string, int> priorityByName, List<PluginValidationDiagnostic> diagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin)
        {
            if (plugin == null || !policy.IsFixedOrderPlugin(Path.GetFileName(plugin.Filename)))
                return;

            int expectedIndex = 0;
            foreach (string fixedPluginName in policy.FixedOrderPlugins)
            {
                int actualIndex;
                if (priorityByName.TryGetValue(NormalizePluginName(fixedPluginName), out actualIndex))
                {
                    if (String.Equals(NormalizePluginName(plugin.Filename), NormalizePluginName(fixedPluginName), StringComparison.OrdinalIgnoreCase) && actualIndex != expectedIndex)
                        AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.InvalidFixedPluginPlacement, PluginValidationSeverity.Error, "Fixed-order plugin is not in its configured position.");
                    expectedIndex++;
                }
            }
        }

        private static void ValidatePlugin(PluginManagementPolicy policy, Plugin plugin, bool active, int priority, Dictionary<string, Plugin> pluginsByName, Dictionary<string, int> priorityByName, ISet<Plugin> activePlugins, List<PluginValidationDiagnostic> diagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin)
        {
            if (plugin == null)
                return;

            if (!policy.ValidateDependencies || plugin.Masters == null)
                return;

            foreach (string masterName in plugin.Masters)
            {
                string normalizedMasterName = NormalizePluginName(masterName);
                Plugin master;
                if (!pluginsByName.TryGetValue(normalizedMasterName, out master))
                {
                    AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.MissingMaster, PluginValidationSeverity.Error, "Missing master: " + masterName);
                    continue;
                }

                if (active && !activePlugins.Contains(master))
                    AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.InactiveRequiredMaster, PluginValidationSeverity.Error, "Required master is inactive: " + masterName);

                int masterPriority;
                if (priorityByName.TryGetValue(normalizedMasterName, out masterPriority) && masterPriority > priority)
                    AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.MasterBelowDependent, PluginValidationSeverity.Error, "Required master loads below dependent: " + masterName);
            }
        }

        private static void DetectDependencyCycles(IList<Plugin> orderedPlugins, Dictionary<string, Plugin> pluginsByName, List<PluginValidationDiagnostic> diagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin)
        {
            HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Plugin plugin in orderedPlugins)
                DetectDependencyCycles(plugin, pluginsByName, visiting, visited, diagnostics, diagnosticsByPlugin);
        }

        private static void DetectDependencyCycles(Plugin plugin, Dictionary<string, Plugin> pluginsByName, HashSet<string> visiting, HashSet<string> visited, List<PluginValidationDiagnostic> diagnostics, Dictionary<Plugin, List<PluginValidationDiagnostic>> diagnosticsByPlugin)
        {
            if (plugin == null)
                return;

            string pluginName = NormalizePluginName(plugin.Filename);
            if (visited.Contains(pluginName))
                return;
            if (visiting.Contains(pluginName))
            {
                AddDiagnostic(diagnostics, diagnosticsByPlugin, plugin, PluginValidationIssueKind.DependencyCycle, PluginValidationSeverity.Error, "Dependency cycle detected.");
                return;
            }

            visiting.Add(pluginName);
            foreach (string masterName in plugin.Masters ?? new List<string>())
            {
                Plugin master;
                if (pluginsByName.TryGetValue(NormalizePluginName(masterName), out master))
                    DetectDependencyCycles(master, pluginsByName, visiting, visited, diagnostics, diagnosticsByPlugin);
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
