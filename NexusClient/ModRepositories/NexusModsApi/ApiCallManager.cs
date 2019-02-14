namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.IO;
    using RestSharp;
    using Util;

    /// <summary>
    /// Central point for managing calls to the API.
    /// </summary>
    public class ApiCallManager
    {
        private const string BaseUrl = "https://api.nexusmods.com";

        private string _apiKey;
        private readonly RestClient _restClient;

        public ApiCallManager()
        {
            _apiKey = @"GetYourOwn";
            _restClient = new RestClient(BaseUrl);
            RateLimit = new RateLimit();
        }

        public RateLimit RateLimit { get; }

        /// <summary>
        /// Application reference to be used with SSO integration.
        /// </summary>
        public static string ApplicationReference => "HaveNotRequestedOneYet";

        /// <summary>
        /// Gets the user agent string to send to the Nexus Mods API.
        /// </summary>
        public static string UserAgent => $"NexusModManager/{CommonData.VersionString} ({Environment.OSVersion})";

        private IRestResponse ExecuteRequest(IRestRequest request)
        {
            request.AddHeader("apikey", _apiKey);
            request.AddHeader("User-Agent", UserAgent);

            var result = _restClient.Execute(request);

            RateLimit.Update(result.Headers);

            return result;
        }

        public IRestResponse TestApiCall()
        {
            var request = new RestRequest("v1/games.json", Method.GET);
            request.AddParameter("include_unapproved", "false");

            var result = ExecuteRequest(request);

            return result;
        }
    }
}
