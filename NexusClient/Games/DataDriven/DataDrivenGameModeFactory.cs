using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using Nexus.Client.Games.Settings;
using Nexus.Client.Games.Steam;
using Nexus.Client.UI;
using Nexus.Client.Util;

namespace Nexus.Client.Games.DataDriven
{
    public class DataDrivenGameModeFactory : IGameModeFactory
    {
        private readonly IEnvironmentInfo _environmentInfo;
        private readonly GameModeDefinition _definition;
        private readonly DataDrivenGameModeDescriptor _descriptor;

        public DataDrivenGameModeFactory(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
        {
            _environmentInfo = environmentInfo;
            _definition = definition;
            _descriptor = new DataDrivenGameModeDescriptor(environmentInfo, definition);
        }

        public IGameModeDescriptor GameModeDescriptor => _descriptor;

        public string GetInstallationPath()
        {
            string path = null;
            var discovery = _definition.Discovery;
            if (discovery != null && !string.IsNullOrWhiteSpace(discovery.SteamAppId))
                path = SteamInstallationPathDetector.Instance.GetSteamInstallationPath(discovery.SteamAppId, discovery.SteamInstallFolderName, discovery.SteamExecutableName);

            if (string.IsNullOrWhiteSpace(path) && discovery != null && !string.IsNullOrWhiteSpace(discovery.GogRegistryKey))
            {
                try
                {
                    string valueName = string.IsNullOrWhiteSpace(discovery.GogPathValueName) ? "PATH" : discovery.GogPathValueName;
                    string gogPath = Registry.GetValue(discovery.GogRegistryKey, valueName, null)?.ToString();
                    if (!string.IsNullOrWhiteSpace(gogPath) && Directory.Exists(gogPath))
                        path = gogPath;
                }
                catch
                {
                }
            }

            return path;
        }

        public string GetInstallationPath(string p_strGameInstallPath)
        {
            return p_strGameInstallPath;
        }

        public string GetExecutablePath(string p_strGameInstallPath)
        {
            return p_strGameInstallPath;
        }

        public IGameMode BuildGameMode(FileUtil p_futFileUtility, out ViewMessage p_imsWarning)
        {
            Trace.TraceInformation("Building data-driven GameMode: {0} ({1})", _definition.Name, _definition.ModeId);
            p_imsWarning = null;
            return new DataDrivenGameMode(_environmentInfo, p_futFileUtility, _definition);
        }

        public bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            if (_definition.Setup == null || !_definition.Setup.UseGenericSetup)
                return false;

            var setupVm = new SetupBaseVM(_environmentInfo, GameModeDescriptor);
            var setupForm = new DataDrivenSetupForm(setupVm);
            return (DialogResult)p_dlgShowView(setupForm, true) != DialogResult.Cancel && setupVm.IsSetupComplete;
        }

        public bool PerformInitialization(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            return true;
        }
    }
}
