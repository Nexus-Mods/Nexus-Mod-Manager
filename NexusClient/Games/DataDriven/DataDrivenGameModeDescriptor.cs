using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGameModeDescriptor : GameModeDescriptorBase
    {
        private readonly GameModeDefinition _definition;
        private Theme _theme;

        public DataDrivenGameModeDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(environmentInfo)
        {
            _definition = definition;
        }

        public GameModeDefinition Definition => _definition;
        public override string Name => _definition.Name;
        public override string ModeId => _definition.ModeId;
        public override string[] GameExecutables => _definition.GameExecutables ?? new string[0];
        public override IEnumerable<string> StopFolders => _definition.StopFolders ?? Enumerable.Empty<string>();
        public override IEnumerable<string> PluginExtensions => _definition.PluginExtensions ?? Enumerable.Empty<string>();
        public override string[] OrderedCriticalPluginNames => _definition.OrderedCriticalPluginNames;
        public override string[] OrderedOfficialPluginNames => _definition.OrderedOfficialPluginNames;
        public override string[] OrderedOfficialUnmanagedPluginNames => _definition.OrderedOfficialUnmanagedPluginNames;
        public override string RequiredToolName => _definition.RequiredToolName;
        public override string[] OrderedRequiredToolFileNames => _definition.OrderedRequiredToolFileNames;
        public override string RequiredToolErrorMessage => _definition.RequiredToolErrorMessage;
        public override string CriticalFilesErrorMessage => _definition.CriticalFilesErrorMessage;

        public override string PluginDirectory
        {
            get
            {
                string suffix = _definition.Plugin?.PluginDirectorySuffix ?? string.Empty;
                string path = string.IsNullOrWhiteSpace(suffix) ? InstallationPath : Path.Combine(InstallationPath ?? string.Empty, suffix);
                if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        public override Theme ModeTheme
        {
            get
            {
                if (_theme == null)
                    _theme = new Theme(LoadIcon(), ParseColor(_definition.Theme?.PrimaryColor, Color.FromArgb(80, 80, 80)), null);
                return _theme;
            }
        }

        private Icon LoadIcon()
        {
            try
            {
                string path = ResolveResource(_definition.Resources?.IconPath);
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return new Icon(path);
            }
            catch
            {
            }
            return SystemIcons.Application;
        }

        private string ResolveResource(string relativePath)
        {
            return string.IsNullOrWhiteSpace(relativePath) ? null : Path.Combine(_definition.DefinitionDirectory ?? string.Empty, relativePath);
        }

        private static Color ParseColor(string value, Color fallback)
        {
            try
            {
                return string.IsNullOrWhiteSpace(value) ? fallback : ColorTranslator.FromHtml(value);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
