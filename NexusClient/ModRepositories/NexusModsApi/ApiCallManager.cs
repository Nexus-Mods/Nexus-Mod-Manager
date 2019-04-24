namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net;
    using DataContracts;
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

        #endregion

        private IRestResponse ExecuteRequest(IRestRequest request, string apiKey = "")
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

        #region Mods

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
        /// <param name="mod">ID for mod to retrieve info for.</param>
        /// <returns>A <see cref="ModDataContract"/> if the mod is found, otherwise null.</returns>
        public ModDataContract SearchForModById(string game, string mod)
        {
            var request = new RestRequest($"v1/games/{game}/mods/{mod}.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"SearchForModById failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<ModDataContract>(result.Content);
        }

        #endregion

        #region Mod Files

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gameDomainName">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <param name="category">
        /// Filter File Category. Case insensitive. Comma separated list of categories.
        /// Can be: "main", "update", "optional", "old_version" or "miscellaneous".</param>
        /// <returns><see cref="GetModFilesResponseDataContract"/> with all mod file information for the given mod.</returns>
        public List<GetModFilesResponseDataContract> ListAllModFiles(string gameDomainName, int modId, string category = "")
        {
            var request = new RestRequest($"v1/games/{gameDomainName}/mods/{modId}/files.json" + (string.IsNullOrEmpty(category) ? string.Empty : $"?category={category}"));
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"ListAllModFiles failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<GetModFilesResponseDataContract>>(result.Content);
        }

        /// <summary>
        /// View a specified mod file, using a specified game and mod
        /// </summary>
        /// <param name="gameDomainName">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <param name="fileId">ID for the file to retrieve info for.</param>
        /// <returns>A single <see cref="ModFileDataContract"/>, or null if not found.</returns>
        public ModFileDataContract GetModFile(string gameDomainName, int modId, int fileId)
        {
            var request = new RestRequest($"v1/games/{gameDomainName}/mods/{modId}/files/{fileId}.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetModFile failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<ModFileDataContract>(result.Content);
        }

        /// <summary>
        /// Generate download link for mod file. For premium users, will return array of
        /// download links with their preferred download location in the first element.
        /// </summary>
        /// <param name="gameId">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <param name="fileId">ID for the file to retrieve info for.</param>
        /// <param name="key">Key provided by Nexus Mods Website.</param>
        /// <param name="expires">Expiry of the key.</param>
        /// <returns></returns>
        public List<ModFileDownloadInfoDataContract> GetModFileDownloadLink(int gameId, int modId, int fileId, string key = "", int expires = -1)
        {
            var request = new RestRequest($"v1/games/{gameId}/mods/{modId}/download_link.json");

            if (!string.IsNullOrEmpty(key))
            {
                request.Resource += $"&key={key}";
            }

            if (expires != -1)
            {
                request.Resource += $"&expires={expires}";
            }

            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                // TODO: Test this before release
                dynamic content = SimpleJson.DeserializeObject(result.Content);
                Trace.TraceInformation($"GetModFileDownloadLink failed, status: {result.StatusCode} : {content.message}");
                return null;
            }

            return JSONSerializer.Deserialize<List<ModFileDownloadInfoDataContract>>(result.Content);
        }

        #endregion

        #region Games

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

        public GameDataContract GetGameInformation(int gameId)
        {
            var request = new RestRequest($"v1/games/{gameId}.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetGameInformation failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<GameDataContract>(result.Content);
        }

        #endregion

        #region User

        /// <summary>
        /// Validate the current API key against Nexus Mods.
        /// </summary>
        /// <returns>A <see cref="UserDataContract"/> if the key is valid, otherwise null.</returns>
        public UserDataContract ValidateUser(string apiKey)
        {
            var request = new RestRequest("v1/users/validate.json", Method.GET);

            var result = ExecuteRequest(request, apiKey);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"API key \"{apiKey}\" validation failed, status: {result.StatusCode} : {result.StatusDescription}");
                ClearApiKey();

                return null;
            }

            return JSONSerializer.Deserialize<UserDataContract>(result.Content);
        }

        /// <summary>
        /// Fetch all the mods being tracked by the current user.
        /// </summary>
        /// <returns>A list of <see cref="ModDataContract"/> with all tracked mods.</returns>
        public List<ModDataContract> GetAllTrackedMods()
        {
            var request = new RestRequest("v1/user/tracked_mods.json", Method.GET);
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetAllColourSchemes failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<ModDataContract>>(result.Content);
        }

        /// <summary>
        /// Start tracking a mod, for the current user.
        /// </summary>
        /// <param name="gameId">Game ID which mod belongs to.</param>
        /// <param name="modId">Mod ID for mod to track.</param>
        /// <returns>True if mod is tracked, otherwise false.</returns>
        public bool StartTrackingMod(int gameId, int modId)
        {
            var request = new RestRequest($"v1/user/tracked_mods.json?domain_name={gameId}", Method.POST);
            request.AddParameter("mod_id", modId);
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"StartTrackingMod failed, status: {result.StatusCode} : {result.StatusDescription}");
                return false;
            }

            // TODO: Do we want to differentiate between 201 (started tracking) and 200 (already tracked)?

            return true;
        }

        /// <summary>
        /// Stops tracking a mod, for the current user.
        /// </summary>
        /// <param name="gameId">Game ID which mod belongs to.</param>
        /// <param name="modId">Mod ID for mod to stop track.</param>
        /// <returns>True if mod is not tracked, otherwise false.</returns>
        public bool StopTrackingMod(int gameId, int modId)
        {
            var request = new RestRequest($"v1/user/tracked_mods.json?domain_name={gameId}", Method.DELETE);
            request.AddParameter("mod_id", modId);
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                if (result.StatusCode == HttpStatusCode.NotFound)
                {
                    // Mod was not tracked, technically this is a success.
                    return true;
                }

                Trace.TraceInformation($"StopTrackingMod failed, status: {result.StatusCode} : {result.StatusDescription}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns a list of all endorsements for the current user.
        /// </summary>
        /// <returns>A list of <see cref="EndorsementDataContract"/>.</returns>
        public List<EndorsementDataContract> GetEndorsements()
        {
            var request = new RestRequest("v1/user/endorsements.json", Method.GET);
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetEndorsements failed, status: {result.StatusCode} : {result.StatusDescription}");

                return null;
            }

            return JSONSerializer.Deserialize<List<EndorsementDataContract>>(result.Content);
        }

        #endregion

        #region Colour Schemes

        /// <summary>
        /// Returns list of all colour schemes, including the primary, secondary and 'darker' colours.
        /// </summary>
        /// <returns>A list of all available <see cref="ColourSchemeDataContract"/>s.</returns>
        public List<ColourSchemeDataContract> GetAllColourSchemes()
        {
            var request = new RestRequest("v1/colourschemes.json");
            var result = ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetAllColourSchemes failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<ColourSchemeDataContract>>(result.Content);
        }

        #endregion
    }
}
