using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGamebryoGameModeDescriptor : DataDrivenGameModeDescriptor
    {
        private string _pluginPath;
        private string[] _criticalPlugins;
        private string[] _officialPlugins;
        private string[] _officialUnmanagedPlugins;

        public DataDrivenGamebryoGameModeDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(environmentInfo, definition)
        {
        }

        public override string PluginDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_pluginPath))
                    return _pluginPath;

                string suffix = _definition.Plugin?.PluginDirectorySuffix;
                string path = string.IsNullOrWhiteSpace(suffix) ? InstallationPath : Path.Combine(InstallationPath ?? string.Empty, suffix);
                if (!string.IsNullOrEmpty(path))
                {
                    string root = Path.GetPathRoot(path);
                    if (!string.IsNullOrEmpty(root) && !DriveInfo.GetDrives().Any(x => x.Name.Equals(root, StringComparison.CurrentCultureIgnoreCase)))
                        throw new DirectoryNotFoundException("The selected drive is no longer present on the system.");

                    if (!Directory.Exists(path))
                        Directory.CreateDirectory(path);
                }

                _pluginPath = path;
                return _pluginPath;
            }
        }

        public override string[] OrderedCriticalPluginNames => _criticalPlugins ?? (_criticalPlugins = ExpandPluginPaths(_definition.OrderedCriticalPluginNames));
        public override string[] OrderedOfficialPluginNames => _officialPlugins ?? (_officialPlugins = ExpandPluginPaths(_definition.OrderedOfficialPluginNames));
        public override string[] OrderedOfficialUnmanagedPluginNames => _officialUnmanagedPlugins ?? (_officialUnmanagedPlugins = ExpandPluginPaths(GetOfficialUnmanagedPluginNames()));

        private IEnumerable<string> GetOfficialUnmanagedPluginNames()
        {
            IEnumerable<string> plugins = _definition.OrderedOfficialUnmanagedPluginNames ?? new string[0];
            if (_definition.Plugin?.OfficialUnmanagedPluginListFiles == null)
                return plugins;

            foreach (string filePath in _definition.Plugin.OfficialUnmanagedPluginListFiles)
            {
                string resolvedPath = ResolvePath(filePath);
                if (File.Exists(resolvedPath))
                    plugins = plugins.Union(File.ReadLines(resolvedPath).Where(line => !string.IsNullOrWhiteSpace(line)), StringComparer.OrdinalIgnoreCase);
            }

            return plugins;
        }

        private string[] ExpandPluginPaths(IEnumerable<string> filenames)
        {
            if (filenames == null)
                return new string[0];

            return filenames.Select(filename => Path.Combine(PluginDirectory, filename).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)).ToArray();
        }

    }
}