namespace Nexus.Client.Games
{
    using System;
    using System.IO;
    using System.Diagnostics;
    using Microsoft.Win32;

    using Nexus.Client.Games.Steam;
    using Nexus.Client.Util;

    /// <summary>
    /// Find out the installation paths of a Steam game.
    /// </summary>
    public sealed class SteamInstallationPathDetector
    {
        private static readonly Lazy<SteamInstallationPathDetector> Lazy = new Lazy<SteamInstallationPathDetector>(() => new SteamInstallationPathDetector());

        /// <summary>
        /// Returns the single instance of this class.
        /// </summary>
        public static SteamInstallationPathDetector Instance => Lazy.Value;

        private SteamInstallationPathDetector() { }

        /// <summary>
        /// Reads Steam's libraries and exact app manifest, retaining legacy folder and uninstall-registry fallbacks.
        /// </summary>
        public string GetSteamInstallationPath(string steamId, string folderName, string binaryName)
        {
            Trace.TraceInformation("Checking for Steam install: {0}", steamId);
            Trace.Indent();
            try
            {
                // The per-app Installed flag may be missing for a newly installed game or a different Steam account.
                string steamPath = RegistryUtil.ReadValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");
                string path = FindInSteamLibraries(steamPath, steamId, folderName, binaryName);
                if (path != null) return path;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Unable to inspect Steam libraries: {0}", ex.Message);
            }
            finally
            {
                Trace.Unindent();
            }

            foreach (string key in new[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App ",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App "
            })
            {
                try
                {
                    string path = Registry.GetValue(key + steamId, "InstallLocation", null)?.ToString();
                    if (IsValidInstallation(path, binaryName)) return path;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Unable to inspect Steam uninstall registration: {0}", ex.Message);
                }
            }
            return null;
        }

        /// <summary>Checks the primary library and both old and current libraryfolders.vdf layouts without trusting stale folders.</summary>
        private static string FindInSteamLibraries(string steamPath, string steamId, string folderName, string binaryName)
        {
            if (String.IsNullOrWhiteSpace(steamPath)) return null;
            steamPath = Path.GetFullPath(steamPath);
            string found = FindInSteamLibrary(steamPath, steamId, folderName, binaryName);
            if (found != null) return found;
            KeyValue libraries = KeyValue.LoadAsText(Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"));
            if (libraries == null) return null;
            foreach (KeyValue library in libraries.Children)
            {
                int index;
                if (!Int32.TryParse(library.Name, out index) || index < 0) continue;
                // Modern entries are objects: never depend on property order or a possibly stale apps cache.
                string libraryPath = library["path"].Value ?? library.Value;
                if (String.IsNullOrWhiteSpace(libraryPath)) continue;
                found = FindInSteamLibrary(libraryPath, steamId, folderName, binaryName);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Prefers Steam's recorded installdir to a game-mode hint, while isolating malformed or offline libraries.</summary>
        private static string FindInSteamLibrary(string libraryPath, string steamId, string folderName, string binaryName)
        {
            try
            {
                string common = Path.GetFullPath(Path.Combine(libraryPath, "steamapps", "common"));
                uint appId;
                if (UInt32.TryParse(steamId, out appId))
                {
                    KeyValue manifest = KeyValue.LoadAsText(Path.Combine(libraryPath, "steamapps", "appmanifest_" + steamId + ".acf"));
                    string installDirectory = manifest == null ? null : manifest["installdir"].Value;
                    if (manifest != null && String.Equals(manifest["appid"].Value, steamId, StringComparison.Ordinal) &&
                        !String.IsNullOrWhiteSpace(installDirectory) && !Path.IsPathRooted(installDirectory))
                    {
                        string path = Path.GetFullPath(Path.Combine(common, installDirectory));
                        if (path.StartsWith(common.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                            IsValidInstallation(path, binaryName)) return path;
                    }
                }
                if (!String.IsNullOrWhiteSpace(folderName))
                {
                    string path = Path.Combine(common, folderName);
                    if (IsValidInstallation(path, binaryName)) return path;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Unable to inspect Steam library '{0}': {1}", libraryPath, ex.Message);
            }
            return null;
        }

        /// <summary>Requires the expected executable so an empty directory left after a move cannot mask a valid install.</summary>
        private static bool IsValidInstallation(string path, string binaryName)
        {
            bool valid = !String.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
                (String.IsNullOrWhiteSpace(binaryName) || File.Exists(Path.Combine(path, binaryName)));
            if (valid) Trace.TraceInformation("Found Steam installation: {0}", path);
            return valid;
        }
    }
}
