using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using Microsoft.Win32;
using Nexus.Client.Games.Steam;

namespace Nexus.Client.Games
{
    /// <summary>
    /// Find out the installation paths of a Steam game.
    /// </summary>
    public sealed class SteamInstallationPathDetector
    {

        private static readonly Lazy<SteamInstallationPathDetector> lazy = new Lazy<SteamInstallationPathDetector>(() => new SteamInstallationPathDetector());

        /// <summary>
        /// Returns the single instance of this class.
        /// </summary>
        public static SteamInstallationPathDetector Instance { get { return lazy.Value; } }

        private SteamInstallationPathDetector() { }

        /// <summary>
        /// Extracts Steam installation directory from the registry and tries to find the game with the provided ID.
        /// </summary>
        public string GetSteamInstallationPath(string steamId, string folderName, string binaryName)
        {
            var registryKey = @"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\" + steamId;
            Trace.TraceInformation(@"Checking for steam install: {0}\Installed", registryKey);
            Trace.Indent();

            string strValue = null;
            try
            {
                var steamKey = Registry.GetValue(registryKey, "Installed", 0);
                if (steamKey != null)
                {
                    var isSteamInstall = steamKey.ToString() == "1";
                    if (isSteamInstall)
                    {
                        Trace.TraceInformation("Getting Steam install folder.");

                        var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null).ToString();

                        // convert path to windows path. (steam uses C:/x/y we want C:\\x\\y
                        steamPath = Path.GetFullPath(steamPath);
                        var appPath = Path.Combine(steamPath, @"steamapps\common\" + folderName);

                        // check if game is installed in the default directory
                        if (!Directory.Exists(appPath))
                        {
                            Trace.TraceInformation(
                                folderName + " is not installed in standard directory. Checking steam config.vdf...");

                            // second try, check steam config.vdf
                            // if any of this fails, no problem... just drop through the catch
                            var steamConfig = Path.Combine(Path.Combine(steamPath, "config"), "config.vdf");
                            var kv = KeyValue.LoadAsText(steamConfig);
                            var node =
                                kv.Children[0].Children[0].Children[0].Children.Single(x => x.Name == "apps")
                                    .Children.Single(x => x.Name == steamId);
                            if (node != null)
                            {
                                appPath = node.Children.Single(x => x.Name == "installdir").Value;
                                if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, binaryName)))
                                    strValue = appPath;
                            }
                        }
                        else
                            strValue = appPath;
                    }
                }
            }
            catch
            {
                //if we can't read the registry or config.vdf, just return null
            }

            try
            {
                if (string.IsNullOrWhiteSpace(strValue))
                {
                    Trace.TraceInformation("Getting install folder from Uninstall.");

                    var uniPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + steamId, "InstallLocation", null)?.ToString();

                    if (uniPath != null && Directory.Exists(uniPath))
                        strValue = uniPath;
                }
            }
            catch
            {
            }

            Trace.TraceInformation("Found {0}", strValue);
            Trace.Unindent();

            return strValue;
        }

    }
}
