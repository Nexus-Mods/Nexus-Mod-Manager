namespace Nexus.Client.Games
{
    using System;
    using System.IO;
    using System.Linq;
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
        /// Extracts Steam installation directory from the registry and tries to find the game with the provided ID.
        /// </summary>
        public string GetSteamInstallationPath(string steamId, string folderName, string binaryName)
        {
            var registryKey = $@"Software\Valve\Steam\Apps\{steamId}";
            Trace.TraceInformation($@"Checking for steam install: {registryKey}\Installed");
            Trace.Indent();

            string value = null;
            
            try
            {
                var steamKey = RegistryUtil.ReadValue(RegistryHive.CurrentUser, registryKey, "Installed");

                if (steamKey != null && steamKey.Equals("1"))
                {
                    Trace.TraceInformation("Getting Steam install folder.");

                    var steamPath = RegistryUtil.ReadValue(RegistryHive.CurrentUser, @"Software\Valve\Steam", "SteamPath");

                    // convert path to windows path. (steam uses C:/x/y we want C:\\x\\y
                    steamPath = Path.GetFullPath(steamPath);
                    var appPath = Path.Combine(steamPath, @"steamapps\common\" + folderName);

                    // check if game is installed in the default directory
                    if (!Directory.Exists(appPath))
                    {
                        Trace.TraceInformation($"{folderName} is not installed in standard directory. Checking steam libraryfolders.vdf...");

						// This chunk of code does a very simplistic cyclic check of the additional libraries within Steam.
						var steamLibraries = Path.Combine(Path.Combine(steamPath, "SteamApps"), "libraryfolders.vdf");
						var kv = KeyValue.LoadAsText(steamLibraries);

                        foreach (var node in kv.Children)
						{
							var nodePath = node.Value;
                            if (string.IsNullOrEmpty(nodePath))
                                continue;
							appPath = Path.Combine(nodePath, @"steamapps\common\" + folderName);
							
                            if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, binaryName)))
							{
								value = appPath;
								break;
							}

						}

						// third try, check steam config.vdf
						// if any of this fails, no problem... just drop through the catch
						if (string.IsNullOrWhiteSpace(value))
						{
							Trace.TraceInformation(folderName + " is not installed in standard directory. Checking steam config.vdf...");

							var steamConfig = Path.Combine(Path.Combine(steamPath, "steamapps"), "libraryfolders.vdf");
							//var steamConfig = Path.Combine(Path.Combine(steamPath, "config"), "config.vdf");
							kv = KeyValue.LoadAsText(steamConfig);


                            if (kv != null)
                            {
                                foreach (var children in kv.Children)
                                {
                                    try
                                    {
                                        var node = children?.Children?.Single(x => x.Name == "apps")?
                                                .Children?.Single(x => x.Name == steamId);

                                        if (node != null)
                                        {
                                            appPath = children.Children[0].Value;
                                            appPath = Path.Combine(Path.Combine(Path.Combine(appPath, @"steamapps\common"), folderName));

											if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, binaryName)))
                                            {
                                                value = appPath;
                                                break;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
						}
                    }
                    else
                    {
                        value = appPath;
                    }
                }
            }
            catch
            {
                //if we can't read the registry or config.vdf, just return null
            }

            try
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Trace.TraceInformation("Getting install folder from Uninstall.");

                    var uniPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + steamId, "InstallLocation", null)?.ToString();

                    if (uniPath != null && Directory.Exists(uniPath))
                    {
                        value = uniPath;
                    }
                }
            }
            catch
            {
                // Just ignore this.
            }

            Trace.TraceInformation("Found {0}", value);
            Trace.Unindent();

            return value;
        }

    }
}
