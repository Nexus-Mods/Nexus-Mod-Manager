namespace Nexus.Client.ModRepositories.NexusModsApi
{
    using System;
    using System.Diagnostics;
    using ApiObjects;
    using RestSharp;
    using Util;

    /// <summary>
    /// Central point for managing calls to the API.
    /// </summary>
    public class ApiCallManager
    {
        private const string BaseUrl = "https://api.nexusmods.com";
        private readonly IEnvironmentInfo _environmentInfo;
        private readonly RestClient _restClient;
        private UserDataContract _currentUser;

        public ApiCallManager(IEnvironmentInfo environmentInfo)
        {
            _restClient = new RestClient(BaseUrl);
            _environmentInfo = environmentInfo;
            _currentUser = null;
            RateLimit = new RateLimit();
        }

        #region Properties

        public RateLimit RateLimit { get; }

        public UserDataContract CurrentUser
        {
            get => _currentUser;
            private set
            {
                if (_currentUser != value)
                {
                    CurrentUserChanged?.Invoke(this, null);
                    _currentUser = value;
                }
            }
        }

        /// <summary>
        /// Gets the user agent string to send to the Nexus Mods API.
        /// </summary>
        public static string UserAgent => $"NexusModManager/{CommonData.VersionString} ({Environment.OSVersion})";

        #endregion

        #region Events

        public event EventHandler CurrentUserChanged;

        #endregion

        private IRestResponse ExecuteRequest(IRestRequest request)
        {
            request.AddHeader("apikey", _environmentInfo.Settings.ApiKey);
            request.AddHeader("User-Agent", UserAgent);

            var result = _restClient.Execute(request);

            RateLimit.Update(result.Headers);

            return result;
        }

        public UserDataContract ValidateUser()
        {
            var request = new RestRequest("v1/users/validate.json", Method.GET);

            var result = ExecuteRequest(request);

            if (result.IsSuccessful)
            {
                var user = JSONSerializer.Deserialize<UserDataContract>(result.Content);
                CurrentUser = user;
            }
            else
            {
                Trace.TraceInformation($"API key validation failed, status: {result.StatusCode} : {result.StatusDescription}");
                CurrentUser = null;
            }

            return CurrentUser;
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
