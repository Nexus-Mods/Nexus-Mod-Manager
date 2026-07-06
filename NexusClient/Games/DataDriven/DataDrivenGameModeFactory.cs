using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using Nexus.Client.Games.Settings;
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
            _descriptor = CreateDescriptor(environmentInfo, definition);
        }

        public IGameModeDescriptor GameModeDescriptor => _descriptor;

        private static DataDrivenGameModeDescriptor CreateDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
        {
            if (string.Equals(definition.BehaviorProfile, "gamebryo", StringComparison.OrdinalIgnoreCase))
                return new DataDrivenGamebryoGameModeDescriptor(environmentInfo, definition);

            return new DataDrivenGameModeDescriptor(environmentInfo, definition);
        }

        public string GetInstallationPath()
        {
            return GameStoreInstallationPathDetector.Instance.GetInstallationPath(BuildStoreDiscovery(_definition.Discovery));
        }

        private static IEnumerable<GameStoreInstallInfo> BuildStoreDiscovery(GameModeDiscoveryDefinition discovery)
        {
            if (discovery == null)
                yield break;

            if (discovery.Stores != null)
            {
                foreach (GameModeStoreDiscoveryDefinition store in discovery.Stores)
                {
                    GameStoreInstallInfo info = CreateStoreInfo(store);
                    if (info != null)
                        yield return info;
                }
            }

            if (!string.IsNullOrWhiteSpace(discovery.SteamAppId))
            {
                yield return new GameStoreInstallInfo
                {
                    Store = GameStore.Steam,
                    Id = discovery.SteamAppId,
                    InstallFolderName = discovery.SteamInstallFolderName,
                    ExecutableName = discovery.SteamExecutableName
                };
            }

            if (!string.IsNullOrWhiteSpace(discovery.GogRegistryKey))
            {
                yield return new GameStoreInstallInfo
                {
                    Store = GameStore.Gog,
                    RegistryKey = discovery.GogRegistryKey,
                    RegistryValueName = discovery.GogPathValueName
                };
            }
        }

        private static GameStoreInstallInfo CreateStoreInfo(GameModeStoreDiscoveryDefinition store)
        {
            if (store == null || string.IsNullOrWhiteSpace(store.Store))
                return null;

            GameStore parsedStore;
            if (!Enum.TryParse(store.Store, true, out parsedStore))
                return null;

            return new GameStoreInstallInfo
            {
                Store = parsedStore,
                Id = store.Id,
                InstallFolderName = store.InstallFolderName,
                ExecutableName = store.ExecutableName,
                RegistryKey = store.RegistryKey,
                RegistryValueName = store.RegistryValueName,
                PathSuffix = store.PathSuffix
            };
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
            try
            {
                if (string.Equals(_definition.BehaviorProfile, "gamebryo", StringComparison.OrdinalIgnoreCase))
                    return new DataDrivenGamebryoGameMode(_environmentInfo, p_futFileUtility, _definition);
                return new DataDrivenGameMode(_environmentInfo, p_futFileUtility, _definition);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Could not build data-driven GameMode {0} ({1}).", _definition.Name, _definition.ModeId);
                TraceUtil.TraceException(ex);
                p_imsWarning = CreateBuildFailureMessage(ex);
                return null;
            }
        }

        private ViewMessage CreateBuildFailureMessage(Exception exception)
        {
            string message = "Could not initialize " + _definition.Name + " Game Mode." + Environment.NewLine + Environment.NewLine + exception.Message;
            string details = "Mode ID: " + _definition.ModeId + Environment.NewLine +
                             "Definition: " + (_definition.DefinitionPath ?? "unknown") + Environment.NewLine +
                             "Behavior profile: " + (_definition.BehaviorProfile ?? "generic") + Environment.NewLine +
                             Environment.NewLine + exception;
            return new ViewMessage(message, details, "Game Mode Initialization Error", MessageBoxIcon.Error);
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
