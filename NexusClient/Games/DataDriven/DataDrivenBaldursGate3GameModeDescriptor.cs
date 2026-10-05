using System;
using System.IO;

namespace Nexus.Client.Games.DataDriven
{
    /// <summary>
    /// Preserves Baldur's Gate 3's split managed-mod and game-root deployment layout while using a data-driven definition.
    /// </summary>
    public sealed class DataDrivenBaldursGate3GameModeDescriptor : DataDrivenGameModeDescriptor
    {
        public DataDrivenBaldursGate3GameModeDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
            : base(environmentInfo, definition)
        {
        }

        /// <summary>
        /// Gets the user-local managed mod directory used by the legacy BG3 game mode.
        /// Existing custom paths remain valid; old Documents-based defaults are migrated to LocalAppData.
        /// </summary>
        public override string InstallationPath => ResolveManagedInstallationPath(EnvironmentInfo, ModeId);

        /// <summary>
        /// Gets the discovered game root used for bin/Data secondary-root deployment.
        /// </summary>
        public override string SecondaryInstallationPath => ExecutablePath;

        internal static string ResolveManagedInstallationPath(IEnvironmentInfo environmentInfo, string modeId)
        {
            if (environmentInfo == null)
                throw new ArgumentNullException(nameof(environmentInfo));

            string configuredPath = null;
            if (environmentInfo.Settings.InstallationPaths.ContainsKey(modeId))
                configuredPath = environmentInfo.Settings.InstallationPaths[modeId];

            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(configuredPath) ||
                (!string.IsNullOrWhiteSpace(documentsPath) && configuredPath.IndexOf(documentsPath, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                configuredPath = GetDefaultManagedInstallationPath();
                environmentInfo.Settings.InstallationPaths[modeId] = configuredPath;
                environmentInfo.Settings.Save();
            }

            return configuredPath;
        }

        internal static string GetDefaultManagedInstallationPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Larian Studios\Baldur's Gate 3\Mods");
        }
    }
}
