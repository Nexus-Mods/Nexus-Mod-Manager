using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nexus.Client.UI;
using Nexus.Client.Util;
using Nexus.Client.Settings;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Win32;
using System.IO;
using Nexus.Client.Games.Steam;

namespace Nexus.Client.Games.Witcher
{
    public class WitcherGameModeFactory : IGameModeFactory
    {
        private readonly IGameModeDescriptor m_gmdGameModeDescriptor = null;

        /// <summary>
		/// Gets the application's environement info.
		/// </summary>
		/// <value>The application's environement info.</value>
		protected IEnvironmentInfo EnvironmentInfo { get; private set; }

        public IGameModeDescriptor GameModeDescriptor
        {
            get
            {
                return m_gmdGameModeDescriptor;
            }
        }

        protected WitcherGameMode InstantiateGameMode(FileUtil p_futFileUtility)
        {
            return new WitcherGameMode(EnvironmentInfo, p_futFileUtility);
        }

        public IGameMode BuildGameMode(FileUtil p_futFileUtility, out ViewMessage p_imsWarning)
        {
            if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();
            if (!EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId].ContainsKey("AskAboutReadOnlySettingsFiles"))
            {
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["AskAboutReadOnlySettingsFiles"] = true;
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId]["UnReadOnlySettingsFiles"] = true;
                EnvironmentInfo.Settings.Save();
            }

            WitcherGameMode gmdGameMode = InstantiateGameMode(p_futFileUtility);
            p_imsWarning = null;

            return gmdGameMode;
        }

        public string GetExecutablePath(string p_strPath)
        {
            return p_strPath;
        }

        public string GetInstallationPath()
        {
            var registryKey = @"HKEY_CURRENT_USER\Software\Valve\Steam\Apps\20900";
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
                        var appPath = Path.Combine(steamPath, @"steamapps\common\the witcher\");

                        // check if game is installed in the default directory
                        if (!Directory.Exists(appPath))
                        {
                            Trace.TraceInformation(
                                "Witcher is not installed in standard directory. Checking steam config.vdf...");

                            // second try, check steam config.vdf
                            // if any of this fails, no problem... just drop through the catch
                            var steamConfig = Path.Combine(Path.Combine(steamPath, "config"), "config.vdf");
                            var kv = KeyValue.LoadAsText(steamConfig);
                            var node =
                                kv.Children[0].Children[0].Children[0].Children.Single(x => x.Name == "apps")
                                    .Children.Single(x => x.Name == "20900");
                            if (node != null)
                            {
                                appPath = node.Children.Single(x => x.Name == "installdir").Value;
                                if (Directory.Exists(appPath) && File.Exists(Path.Combine(appPath, "System", "witcher.exe")))
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

                    var uniPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 20900", "InstallLocation", null).ToString();

                    if (Directory.Exists(uniPath))
                        strValue = uniPath;
                }
            }
            catch
            {
            }

            Trace.TraceInformation("Found {0}", strValue);
            Trace.Unindent();

            if (strValue == null)
            {
                string strRegistryKey = null;
                if (EnvironmentInfo.Is64BitProcess)
                    strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\Wow6432Node\CD Projekt RED\The Witcher";
                else
                    strRegistryKey = @"HKEY_LOCAL_MACHINE\Software\CD Projekt RED\The Witcher";
                Trace.TraceInformation(@"Checking: {0}\InstallFolder", strRegistryKey);
                Trace.Indent();

                try
                {
                    strValue = Registry.GetValue(String.Format(strRegistryKey, GameModeDescriptor.ModeId), "InstallFolder", null) as string;
                }
                catch
                {
                    //if we can't read the registry, just return null
                }

                Trace.TraceInformation(String.Format("Found {0}", strValue));
                Trace.Unindent();
            }
            return strValue;
        }


        /// <summary>
		/// Gets the path where mod files should be installed.
		/// </summary>
		/// <remarks>
		/// This method uses the given path to the installed game
		/// to determine the installaiton path for mods.
		/// </remarks>
		/// <returns>The path where mod files should be installed, or
		/// <c>null</c> if the path could be be determined.</returns>
        public string GetInstallationPath(string p_strGameInstallPath)
        {
            string strPath = Path.Combine(Path.GetDirectoryName(p_strGameInstallPath), "Data", "Override");
            if (!Directory.Exists(strPath))
                Directory.CreateDirectory(strPath);
            return strPath;
        }

        public bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            return true;
        }

        public bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            if (EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] == null)
                EnvironmentInfo.Settings.CustomGameModeSettings[GameModeDescriptor.ModeId] = new PerGameModeSettings<object>();

            WitcherSetupVM vmlSetup = new WitcherSetupVM(EnvironmentInfo, GameModeDescriptor);
            SetupForm frmSetup = new SetupForm(vmlSetup);
            if (((DialogResult)p_dlgShowView(frmSetup, true)) == DialogResult.Cancel)
                return false;
            return vmlSetup.Save();
        }

        public WitcherGameModeFactory(IEnvironmentInfo p_eifEnvironmentInfo)
        {
            EnvironmentInfo = p_eifEnvironmentInfo;
            m_gmdGameModeDescriptor = new WitcherGameModeDescriptor(p_eifEnvironmentInfo);
        }
    }
}
