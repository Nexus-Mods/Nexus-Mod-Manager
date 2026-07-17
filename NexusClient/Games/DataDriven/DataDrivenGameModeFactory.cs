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
            _environmentInfo = environmentInfo ?? throw new ArgumentNullException(nameof(environmentInfo));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _descriptor = CreateDescriptor(environmentInfo, definition);
        }

        public IGameModeDescriptor GameModeDescriptor => _descriptor;
        public bool LegacyFallback => _definition.LegacyFallback;
        public string DefinitionPath => _definition.DefinitionPath;

        private static DataDrivenGameModeDescriptor CreateDescriptor(IEnvironmentInfo environmentInfo, GameModeDefinition definition)
        {
            if (string.Equals(definition.BehaviorProfile, "gamebryo", StringComparison.OrdinalIgnoreCase))
                return new DataDrivenGamebryoGameModeDescriptor(environmentInfo, definition);
            if (string.Equals(definition.BehaviorProfile, "generic", StringComparison.OrdinalIgnoreCase))
                return new DataDrivenGameModeDescriptor(environmentInfo, definition);
            throw new InvalidOperationException("Unsupported data-driven behavior profile: " + definition.BehaviorProfile);
        }

        public string GetInstallationPath()
        {
            return GameStoreInstallationPathDetector.Instance.GetInstallationPath(BuildStoreDiscovery(_definition.Discovery));
        }

        internal static IEnumerable<GameStoreInstallInfo> BuildStoreDiscovery(GameModeDiscoveryDefinition discovery)
        {
            if (discovery == null)
                yield break;

            if (discovery.Stores != null)
            {
                foreach (GameModeStoreDiscoveryDefinition store in discovery.Stores)
                    yield return CreateStoreInfo(store);
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
            if (store == null)
                throw new InvalidOperationException("Store discovery entry cannot be null.");
            if (string.IsNullOrWhiteSpace(store.Store))
                throw new InvalidOperationException("Store discovery entry has no store name.");

            GameStore parsedStore;
            if (!Enum.TryParse(store.Store, true, out parsedStore) || !Enum.IsDefined(typeof(GameStore), parsedStore))
                throw new InvalidOperationException("Unsupported game store: " + store.Store);

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
                if (string.Equals(_definition.BehaviorProfile, "generic", StringComparison.OrdinalIgnoreCase))
                    return new DataDrivenGameMode(_environmentInfo, p_futFileUtility, _definition);
                throw new InvalidOperationException("Unsupported data-driven behavior profile: " + _definition.BehaviorProfile);
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
                             "Behavior profile: " + (_definition.BehaviorProfile ?? "unknown") + Environment.NewLine +
                             Environment.NewLine + exception;
            return new ViewMessage(message, details, "Game Mode Initialization Error", MessageBoxIcon.Error);
        }

        public bool PerformInitialSetup(ShowViewDelegate p_dlgShowView, ShowMessageDelegate p_dlgShowMessage)
        {
            if (_definition.Setup == null || _definition.Setup.UseGenericSetup != true)
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

    /// <summary>
    /// Loads and registers data-driven Game Modes after legacy DLL factories have been discovered.
    /// </summary>
    public static class DataDrivenGameModeRegistration
    {
        public static GameModeDefinitionLoadResult RegisterDefinitions(GameModeRegistry registry, IEnvironmentInfo environmentInfo, string definitionsDirectory)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (environmentInfo == null)
                throw new ArgumentNullException(nameof(environmentInfo));

            GameModeDefinitionLoadResult loadResult = new GameModeDefinitionLoader().LoadFromDirectory(definitionsDirectory);
            foreach (GameModeDefinition definition in loadResult.Definitions)
            {
                bool hasExistingFactory = registry.IsRegistered(definition.ModeId);
                if (hasExistingFactory && definition.LegacyFallback)
                {
                    IGameModeFactory existingFactory = registry.GetGameMode(definition.ModeId);
                    string existingName = existingFactory == null || existingFactory.GameModeDescriptor == null
                        ? definition.ModeId
                        : existingFactory.GameModeDescriptor.Name;
                    Trace.TraceInformation(
                        "Keeping legacy Game Mode factory '{0}' ({1}); data-driven definition requested legacyFallback. Definition: {2}",
                        existingName,
                        definition.ModeId,
                        definition.DefinitionPath ?? "unknown");
                    continue;
                }

                if (hasExistingFactory)
                {
                    Trace.TraceInformation(
                        "Replacing the registered legacy Game Mode factory for {0} with data-driven definition: {1}",
                        definition.ModeId,
                        definition.DefinitionPath ?? "unknown");
                }
                else
                {
                    Trace.TraceInformation(
                        "Registering data-driven Game Mode {0} from: {1}",
                        definition.ModeId,
                        definition.DefinitionPath ?? "unknown");
                }

                registry.RegisterGameMode(new DataDrivenGameModeFactory(environmentInfo, definition));
            }

            return loadResult;
        }
    }

}
