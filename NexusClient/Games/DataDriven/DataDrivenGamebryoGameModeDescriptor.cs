using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGamebryoGameModeDescriptor : DataDrivenGameModeDescriptor
    {
        private string[] _criticalPlugins;
        private string[] _officialPlugins;
        private string[] _officialUnmanagedPlugins;

        public DataDrivenGamebryoGameModeDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(environmentInfo, definition)
        {
        }

        public override string[] OrderedCriticalPluginNames => _criticalPlugins ?? (_criticalPlugins = ExpandPluginPaths(_definition.OrderedCriticalPluginNames));
        public override string[] OrderedOfficialPluginNames => _officialPlugins ?? (_officialPlugins = ExpandPluginPaths(_definition.OrderedOfficialPluginNames));
        public override string[] OrderedOfficialUnmanagedPluginNames => _officialUnmanagedPlugins ?? (_officialUnmanagedPlugins = ExpandPluginPaths(GetOfficialUnmanagedPluginNames()));

        private IEnumerable<string> GetOfficialUnmanagedPluginNames()
        {
            var plugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string plugin in _definition.OrderedOfficialUnmanagedPluginNames ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(plugin))
                    plugins.Add(plugin.Trim());
            }

            string[] listFiles = _definition.Plugin == null ? null : _definition.Plugin.OfficialUnmanagedPluginListFiles;
            foreach (string filePath in listFiles ?? new string[0])
            {
                string resolvedPath = ResolvePath(filePath, BaseGameInstallationPath);
                if (!File.Exists(resolvedPath))
                    continue;

                foreach (string line in File.ReadLines(resolvedPath))
                {
                    string plugin = line == null ? null : line.Trim();
                    if (string.IsNullOrWhiteSpace(plugin) || plugin.StartsWith("#", StringComparison.Ordinal) || plugin.StartsWith(";", StringComparison.Ordinal))
                        continue;
                    if (!DataDrivenDefinitionRules.IsSafeFileName(plugin))
                    {
                        Trace.TraceWarning("Ignoring invalid plugin name '{0}' from official unmanaged plugin list: {1}", plugin, resolvedPath);
                        continue;
                    }
                    plugins.Add(plugin);
                }
            }

            return plugins;
        }

        private string[] ExpandPluginPaths(IEnumerable<string> filenames)
        {
            if (filenames == null)
                return new string[0];

            return filenames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => Path.Combine(PluginDirectory, x.Trim()).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
