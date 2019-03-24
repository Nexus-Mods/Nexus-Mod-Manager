namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using ApiObjects;
    using RestSharp;
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
        }

        /// <summary>
        /// Initializes the <see cref="ApiCallManager"/>.
        /// </summary>
        /// <param name="environmentInfo">Environment info to be used by the <see cref="ApiCallManager"/>.</param>
        /// <returns>The ApiCallManager, unless it has already been created before.</returns>
        /// <remarks>Throws <see cref="InvalidOperationException"/> if an instance was already created.</remarks>
        public ApiCallManager Initialize(IEnvironmentInfo environmentInfo)
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

        #endregion

        private IRestResponse ExecuteRequest(IRestRequest request)
        {
            request.AddHeader("apikey", _environmentInfo.Settings.ApiKey);
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

        /// <summary>
        /// Looks up a file MD5 file hash.
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <param name="md5Checksum">Checksum of file to search for.</param>
        /// <returns>A <see cref="Md5SearchDataContract"/> if the mod is found, otherwise null.</returns>
        public List<Md5SearchDataContract> SearchForModByMd5(string game, string md5Checksum)
        {
            var request = new RestRequest($"v1/games/{game}/mods/md5_search/{md5Checksum}.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"SearchForModByMd5 failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize< List<Md5SearchDataContract>>(result.Content);
        }

        /// <summary>
        /// Retrieve specified mod, from a specified game.
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <returns>A <see cref="ModDataContract"/> if the mod is found, otherwise null.</returns>
        public ModDataContract SearchForModById(string game, string modId)
        {
            var request = new RestRequest($"v1/games/{game}/mods/{modId}.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"SearchForModById failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<ModDataContract>(result.Content);
        }

        /// <summary>
        /// Gets a list of information for all games. Optionally can also return unapproved games.
        /// </summary>
        /// <param name="includeUnapproved">Whether or not to include unapproved games in the list.</param>
        /// <returns>List of all games</returns>
        public List<GameDataContract> GetAllGamesInformation(bool includeUnapproved = false)
        {
            var request = new RestRequest("v1/games.json");
            request.AddParameter("include_unapproved", includeUnapproved.ToString().ToLower());
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetAllGamesInformation failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<GameDataContract>>(result.Content);
        }

        public GameDataContract GetGameInformation(string game)
        {
            var request = new RestRequest($"v1/games/{game}.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetGameInformation failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<GameDataContract>(result.Content);
        }

        /// <summary>
        /// Validate the current API key against Nexus Mods.
        /// </summary>
        /// <returns>A <see cref="UserDataContract"/> if the key is valid, otherwise null.</returns>
        public UserDataContract ValidateUser()
        {
            var request = new RestRequest("v1/users/validate.json", Method.GET);

            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"API key validation failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<UserDataContract>(result.Content);
        }
    }
}
