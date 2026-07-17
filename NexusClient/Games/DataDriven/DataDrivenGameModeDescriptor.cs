using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGameModeDescriptor : GameModeDescriptorBase
    {
        protected readonly GameModeDefinition _definition;
        private Theme _theme;

        public DataDrivenGameModeDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(environmentInfo)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public GameModeDefinition Definition => _definition;
        public override string Name => _definition.Name;
        public override string ModeId => _definition.ModeId;
        public override string[] GameExecutables => _definition.GameExecutables ?? new string[0];
        public override IEnumerable<string> StopFolders => _definition.StopFolders ?? Enumerable.Empty<string>();
        public override IEnumerable<string> PluginExtensions => _definition.PluginExtensions ?? Enumerable.Empty<string>();
        public override string[] OrderedCriticalPluginNames => _definition.OrderedCriticalPluginNames ?? new string[0];
        public override string[] OrderedOfficialPluginNames => _definition.OrderedOfficialPluginNames ?? new string[0];
        public override string[] OrderedOfficialUnmanagedPluginNames => _definition.OrderedOfficialUnmanagedPluginNames ?? new string[0];
        public override string RequiredToolName => _definition.RequiredToolName;
        public override string[] OrderedRequiredToolFileNames => _definition.OrderedRequiredToolFileNames ?? new string[0];
        public override string RequiredToolErrorMessage => _definition.RequiredToolErrorMessage;
        public override string CriticalFilesErrorMessage => _definition.CriticalFilesErrorMessage;

        protected string BaseGameInstallationPath => base.InstallationPath;

        public override string InstallationPath
        {
            get
            {
                string gamePath = BaseGameInstallationPath;
                string configuredPath = _definition.ModInstall == null ? null : _definition.ModInstall.ManagedInstallationPath;
                if (string.IsNullOrWhiteSpace(configuredPath))
                    return gamePath;

                string path = DataDrivenPathResolver.ResolvePath(configuredPath, CreatePathContext(gamePath), gamePath);
                EnsureDirectory(path);
                return path;
            }
        }

        public override string PluginDirectory
        {
            get
            {
                string suffix = _definition.Plugin == null ? null : _definition.Plugin.PluginDirectorySuffix;
                string path = string.IsNullOrEmpty(suffix)
                    ? InstallationPath
                    : DataDrivenPathResolver.ResolvePath(suffix, CreatePathContext(BaseGameInstallationPath), InstallationPath);
                EnsureDirectory(path);
                return path;
            }
        }

        public override Theme ModeTheme
        {
            get
            {
                if (_theme == null)
                    _theme = new Theme(LoadIcon(), ParseColor(_definition.Theme == null ? null : _definition.Theme.PrimaryColor, Color.FromArgb(80, 80, 80)), null);
                return _theme;
            }
        }

        protected string ResolvePath(string path)
        {
            return ResolvePath(path, BaseGameInstallationPath);
        }

        protected string ResolvePath(string path, string relativeBasePath)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;
            return DataDrivenPathResolver.ResolvePath(path, CreatePathContext(BaseGameInstallationPath), relativeBasePath);
        }

		private DataDrivenPathContext CreatePathContext(string gamePath)
		{
            return new DataDrivenPathContext(EnvironmentInfo, gamePath, ExecutablePath ?? gamePath, ModeId, null);
        }

        private Icon LoadIcon()
        {
            try
            {
                string path = ResolveResource(_definition.Resources == null ? null : _definition.Resources.IconPath);
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

        private static void EnsureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string root = Path.GetPathRoot(path);
            if (!string.IsNullOrWhiteSpace(root) && !DriveInfo.GetDrives().Any(x => x.Name.Equals(root, StringComparison.CurrentCultureIgnoreCase)))
                throw new DirectoryNotFoundException("The selected drive is no longer present on the system.");

            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
    }
}
