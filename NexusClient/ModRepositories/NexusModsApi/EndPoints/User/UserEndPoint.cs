namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.User
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using RestSharp;
    using Util;
    using EndPoints;
    using Mods;

    public class UserEndPoint : EndPoint
    {
        public UserEndPoint(ApiCallManager apiCallManager) : base(apiCallManager) {}

        /// <summary>
        /// Validate the current API key against Nexus Mods.
        /// </summary>
        /// <returns>A <see cref="User"/> if the key is valid, otherwise null.</returns>
        public User ValidateUser(string apiKey)
        {
            var request = new RestRequest("v1/users/validate.json", Method.GET);

            var result = ApiCallManager.ExecuteRequest(request, apiKey);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"API key \"{apiKey}\" validation failed, status: {result.StatusCode} : {result.StatusDescription}");
                ApiCallManager.ClearApiKey();

                return null;
            }

            return JSONSerializer.Deserialize<User>(result.Content);
        }

        /// <summary>
        /// Fetch all the mods being tracked by the current user.
        /// </summary>
        /// <returns>A list of <see cref="Mod"/> with all tracked mods.</returns>
        public List<Mod> GetAllTrackedMods()
        {
            var request = new RestRequest("v1/user/tracked_mods.json", Method.GET);
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetAllColourSchemes failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Mod>>(result.Content);
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
            var result = ApiCallManager.ExecuteRequest(request);

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
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                if (result.StatusCode == System.Net.HttpStatusCode.NotFound)
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
        /// <returns>A list of <see cref="Endorsement"/>.</returns>
        public List<Endorsement> GetUserEndorsements()
        {
            var request = new RestRequest("v1/user/endorsements.json", Method.GET);
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetEndorsements failed, status: {result.StatusCode} : {result.StatusDescription}");

                return null;
            }

            return JSONSerializer.Deserialize<List<Endorsement>>(result.Content);
        }
    }
}
