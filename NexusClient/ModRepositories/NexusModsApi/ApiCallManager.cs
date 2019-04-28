namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using EndPoints.ColourSchemes;
    using EndPoints.Games;
    using EndPoints.ModFiles;
    using EndPoints.Mods;
    using EndPoints.User;
    using RestSharp;
    using SimpleJson;
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
        private const string BaseUrl = "https://api.nexusmods.com";
        private readonly IEnvironmentInfo _environmentInfo;
        private readonly RestClient _restClient;

        private static ApiCallManager _instance;

        private ApiCallManager(IEnvironmentInfo environmentInfo)
        {
            _restClient = new RestClient(BaseUrl);
            _environmentInfo = environmentInfo;
            RateLimit = new RateLimit();

            ColourSchemes = new ColourSchemesEndPoint(this);
            Games = new GamesEndPoint(this);
            User = new UserEndPoint(this);
            Mods = new ModsEndPoint(this);
            ModFiles = new ModFilesEndPoint(this);
        }

        /// <summary>
        /// Initializes the <see cref="ApiCallManager"/>.
        /// </summary>
        /// <param name="environmentInfo">Environment info to be used by the <see cref="ApiCallManager"/>.</param>
        /// <returns>The ApiCallManager, unless it has already been created before.</returns>
        /// <remarks>Throws <see cref="InvalidOperationException"/> if an instance was already created.</remarks>
        public static ApiCallManager Initialize(IEnvironmentInfo environmentInfo)
        {
            if (_instance != null)
            {
                throw new InvalidOperationException("The ApiCallManager has already been initialized.");
            }

            _instance = new ApiCallManager(environmentInfo);
            return _instance;
        }

        #region Properties

        /// <summary>
        /// Current state of the API rate limitation.
        /// </summary>
        public RateLimit RateLimit { get; }

        /// <summary>
        /// Gets the user agent string to send to the Nexus Mods API.
        /// </summary>
        public static string UserAgent => $"NexusModManager/{CommonData.VersionString} ({Environment.OSVersion})";

        #region EndPoints

        /// <summary>
        /// End point for getting ColourSchemes info from Nexus.
        /// </summary>
        public ColourSchemesEndPoint ColourSchemes { get; }

        /// <summary>
        /// End point for getting Games info from Nexus.
        /// </summary>
        public GamesEndPoint Games { get; }

        /// <summary>
        /// End point for getting User info from Nexus.
        /// </summary>
        public UserEndPoint User { get; }

        /// <summary>
        /// End point for getting Mods info from Nexus.
        /// </summary>
        public ModsEndPoint Mods { get; }

        /// <summary>
        /// End point for getting Mods info from Nexus.
        /// </summary>
        public ModFilesEndPoint ModFiles { get; }

        #endregion

        #endregion

        internal IRestResponse ExecuteRequest(IRestRequest request, string apiKey = "")
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                apiKey = _environmentInfo.Settings.ApiKey;
            }

            request.AddHeader("apikey", apiKey);
            request.AddHeader("User-Agent", UserAgent);

            var result = _restClient.Execute(request);

            RateLimit.Update(result.Headers);

            return result;
        }

        /// <summary>
        /// Clears the stored API key.
        /// </summary>
        public void ClearApiKey()
        {
            _environmentInfo.Settings.ApiKey = string.Empty;
            _environmentInfo.Settings.Save();
        }
    }
}
