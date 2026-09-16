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
            string apiKey = _environmentInfo.Settings.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _nexusService.ClearCredentials();
                return;
            }

            _nexusService.ReplaceCredentials(NexusCredentials.FromApiKey(apiKey));
        }

        /// <summary>
        /// Clears the API key from the settings.
        /// </summary>
        public void ClearApiKey()
        {
            _environmentInfo.Settings.ApiKey = string.Empty;
            _environmentInfo.Settings.Save();
            UpdateNexusClient();
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
