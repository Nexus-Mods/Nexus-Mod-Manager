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
        private string _cachedGamePath;
        private string _cachedExecutablePath;
        private string _cachedPluginDirectory;

        public DataDrivenGamebryoGameModeDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(environmentInfo, definition)
        {
        }

        public override string[] OrderedCriticalPluginNames
        {
            get
            {
                EnsurePluginPathsCurrent();
                return _criticalPlugins ?? (_criticalPlugins = ExpandPluginPaths(_definition.OrderedCriticalPluginNames));
            }
        }

        public override string[] OrderedOfficialPluginNames
        {
            get
            {
                EnsurePluginPathsCurrent();
                return _officialPlugins ?? (_officialPlugins = ExpandPluginPaths(_definition.OrderedOfficialPluginNames));
            }
        }

        public override string[] OrderedOfficialUnmanagedPluginNames
        {
            get
            {
                EnsurePluginPathsCurrent();
                return _officialUnmanagedPlugins ?? (_officialUnmanagedPlugins = ExpandPluginPaths(GetOfficialUnmanagedPluginNames()));
            }
        }

        /// <summary>
        /// Invalidates cached plugin paths and installation-specific lists after a game path changes.
        /// </summary>
        private void EnsurePluginPathsCurrent()
        {
            string gamePath = BaseGameInstallationPath;
            string executablePath = ExecutablePath;
            string pluginDirectory = PluginDirectory;
            if (string.Equals(_cachedGamePath, gamePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_cachedExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_cachedPluginDirectory, pluginDirectory, StringComparison.OrdinalIgnoreCase))
                return;

            // The factory survives a rescan, while its saved installation and executable paths can change.
            _criticalPlugins = null;
            _officialPlugins = null;
            _officialUnmanagedPlugins = null;
            _cachedGamePath = gamePath;
            _cachedExecutablePath = executablePath;
            _cachedPluginDirectory = pluginDirectory;
        }

        private IEnumerable<string> GetOfficialUnmanagedPluginNames()
        {
            var plugins = new List<string>();
            var seenPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string plugin in _definition.OrderedOfficialUnmanagedPluginNames ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(plugin) && seenPlugins.Add(plugin.Trim()))
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
                    if (seenPlugins.Add(plugin)) plugins.Add(plugin);
                }
                // Starfield reads the first existing CCC location; the per-user file supersedes the installation file.
                if (_definition.Plugin.Policy != null && _definition.Plugin.Policy.UseStarfieldActivationRules == true)
                    break;
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
