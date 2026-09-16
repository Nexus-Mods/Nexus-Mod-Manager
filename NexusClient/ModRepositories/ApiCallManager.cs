namespace Nexus.Client.ModRepositories
{
    using System;
    using Nexus.Client.OnlineServices.NexusMods;
    using Nexus.Client.OnlineServices.NexusMods.V1;
    using Util;

    /// <summary>
    /// Central point for managing calls to the API.
    /// </summary>
    /// <remarks>
    /// We only want one central point of contact with the API (for rate limit reasons), so this is a singleton object.
    /// Note, however, that the singleton nature of the API call manager is not meant to provide global access to the object.
    /// As such any second call to get this object will throw an InvalidOperationException.
    /// </remarks>
    public class ApiCallManager
    {
        private readonly object _credentialSync = new object();
        private readonly IEnvironmentInfo _environmentInfo;
        private readonly NexusModsService _nexusService;

        private static ApiCallManager _instance;

        private ApiCallManager(IEnvironmentInfo environmentInfo)
        {
            _environmentInfo = environmentInfo;
            _nexusService = new NexusModsService(TimeSpan.FromSeconds(100), UserAgent, "NMM", CommonData.VersionString);
            UpdateNexusClient();
        }

        /// <summary>
        /// Updates the Nexus Mods service credentials used for API calls.
        /// </summary>
        public void UpdateNexusClient()
        {
            lock (_credentialSync)
                ReplaceCredentialsFromSettings();
        }

        /// <summary>
        /// Starts one authentication attempt and returns the exact credential generation it owns.
        /// </summary>
        internal NexusSessionContext BeginAuthenticationSession()
        {
            lock (_credentialSync)
                return ReplaceCredentialsFromSettings();
        }

        /// <summary>
        /// Updates credentials from settings and returns the resulting session generation.
        /// </summary>
        private NexusSessionContext ReplaceCredentialsFromSettings()
        {
            string apiKey = _environmentInfo.Settings.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
                return _nexusService.ClearCredentials();

            return _nexusService.ReplaceCredentials(NexusCredentials.FromApiKey(apiKey));
        }

        /// <summary>
        /// Clears the API key from the settings.
        /// </summary>
        public void ClearApiKey()
        {
            lock (_credentialSync)
                ClearApiKeyCore(null);
        }

        /// <summary>
        /// Clears credentials and applies related account-state changes under the same lifecycle lock.
        /// </summary>
        /// <remarks>
        /// The state update must not raise UI notifications. Callers should notify after this method returns,
        /// so synchronous UI marshaling can never run while the credential lifecycle lock is held.
        /// </remarks>
        internal void ClearApiKey(Action stateUpdate)
        {
            lock (_credentialSync)
                ClearApiKeyCore(stateUpdate);
        }

        /// <summary>
        /// Clears credentials only when the supplied authentication generation is still current.
        /// </summary>
        internal bool ClearApiKey(NexusSessionContext expectedSession)
        {
            lock (_credentialSync)
            {
                if (!IsCurrentSessionCore(expectedSession))
                    return false;

                ClearApiKeyCore(null);
                return true;
            }
        }

        /// <summary>
        /// Updates account state only while the originating authentication generation is still current.
        /// </summary>
        /// <remarks>
        /// The state update must not raise UI notifications. Callers should notify after this method returns,
        /// so synchronous UI marshaling can never run while the credential lifecycle lock is held.
        /// </remarks>
        internal bool TryUpdateAuthenticationState(NexusSessionContext expectedSession, Action stateUpdate)
        {
            if (stateUpdate == null)
                throw new ArgumentNullException(nameof(stateUpdate));

            lock (_credentialSync)
            {
                if (!IsCurrentSessionCore(expectedSession))
                    return false;

                stateUpdate();
                return true;
            }
        }

        /// <summary>
        /// Gets whether the supplied authentication generation is still current.
        /// </summary>
        internal bool IsCurrentSession(NexusSessionContext expectedSession)
        {
            lock (_credentialSync)
                return IsCurrentSessionCore(expectedSession);
        }

        /// <summary>
        /// Invalidates service credentials before persisting the cleared setting.
        /// </summary>
        private void ClearApiKeyCore(Action stateUpdate)
        {
            _nexusService.ClearCredentials();
            stateUpdate?.Invoke();
            _environmentInfo.Settings.ApiKey = string.Empty;
            _environmentInfo.Settings.Save();
        }

        /// <summary>
        /// Compares one captured session with the currently active credential generation.
        /// </summary>
        private bool IsCurrentSessionCore(NexusSessionContext expectedSession)
        {
            if (expectedSession == null)
                return false;

            NexusSessionContext current = _nexusService.CaptureSession();
            return ReferenceEquals(current, expectedSession) && current.Generation == expectedSession.Generation;
        }

        /// <summary>
        /// Initializes the <see cref="ApiCallManager"/>.
        /// </summary>
        /// <param name="environmentInfo">Environment info to be used by the <see cref="ApiCallManager"/>.</param>
        /// <returns>The ApiCallManager, unless it has already been created before.</returns>
        public static ApiCallManager Instance(IEnvironmentInfo environmentInfo)
        {
            return _instance ?? (_instance = new ApiCallManager(environmentInfo));
        }

        #region Properties

        /// <summary>
        /// Gets the user agent string to send to the Nexus Mods API.
        /// </summary>
        public static string UserAgent => $"NexusModManager/{CommonData.VersionString} ({Environment.OSVersion})";

        /// <summary>
        /// Gets the NMM-owned Nexus Mods service.
        /// </summary>
        internal NexusModsService NexusService => _nexusService;

        /// <summary>
        /// Gets the owned REST v1 client while an authenticated Nexus session is available.
        /// </summary>
        internal NexusV1Client V1 => _nexusService.CaptureSession().Credentials.HasCredentials ? _nexusService.V1 : null;

        #endregion
    }
}
