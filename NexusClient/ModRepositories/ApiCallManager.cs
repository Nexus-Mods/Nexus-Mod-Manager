namespace Nexus.Client.ModRepositories
{
    using System;
    using Pathoschild.FluentNexus;
    using Pathoschild.FluentNexus.Endpoints;
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
        private NexusClient _nexusClient;

        private static ApiCallManager _instance;

        private ApiCallManager(IEnvironmentInfo environmentInfo)
        {
            _environmentInfo = environmentInfo;
            UpdateNexusClient();
        }

        /// <summary>
        /// Updates the NexusClient object for making API calls.
        /// </summary>
        public void UpdateNexusClient()
        {
            if (!string.IsNullOrEmpty(_environmentInfo.Settings.ApiKey))
            {
                _nexusClient = new NexusClient(_environmentInfo.Settings.ApiKey, "NMM", CommonData.VersionString);
                _nexusClient.SetUserAgent(UserAgent);
            }
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
        /// Gets the current rate limits.
        /// </summary>
        public IRateLimitManager RateLimit => _nexusClient.GetRateLimits().Result;

        #region EndPoints

        /// <summary>
        /// End point for getting ColourSchemes info from Nexus.
        /// </summary>
        public NexusColorSchemesClient ColourSchemes => _nexusClient.ColorSchemes;

        /// <summary>
        /// End point for getting Games info from Nexus.
        /// </summary>
        public NexusGamesClient Games => _nexusClient.Games;

        /// <summary>
        /// End point for getting User info from Nexus.
        /// </summary>
        public NexusUsersClient Users => _nexusClient.Users;

        /// <summary>
        /// End point for getting Mods info from Nexus.
        /// </summary>
        public NexusModsClient Mods => _nexusClient.Mods;

        /// <summary>
        /// End point for getting Mods info from Nexus.
        /// </summary>
        public NexusModFilesClient ModFiles => _nexusClient.ModFiles;

        #endregion

        #endregion
    }
}
