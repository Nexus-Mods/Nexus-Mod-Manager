namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.Games
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using DataContracts.Games;
    using RestSharp;
    using Util;

    public class GamesEndPoint : EndPoint
    {
        public GamesEndPoint(ApiCallManager apiCallManager) : base(apiCallManager)
        {
        }

        /// <summary>
        /// Gets a list of information for all games. Optionally can also return unapproved games.
        /// </summary>
        /// <param name="includeUnapproved">Whether or not to include unapproved games in the list.</param>
        /// <returns>List of all games</returns>
        public List<Game> GetAllGamesInformation(bool includeUnapproved = false)
        {
            var request = new RestRequest("v1/games.json");
            request.AddParameter("include_unapproved", includeUnapproved.ToString().ToLower());
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetAllGamesInformation failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Game>>(result.Content);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gameId"></param>
        /// <returns></returns>
        public Game GetGameInformation(int gameId)
        {
            var request = new RestRequest($"v1/games/{gameId}.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetGameInformation failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<Game>(result.Content);
        }
    }
}
