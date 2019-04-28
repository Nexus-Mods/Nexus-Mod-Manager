namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.Mods
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using RestSharp;
    using Util;

    public class ModsEndPoint : EndPoint
    {
        public ModsEndPoint(ApiCallManager apiCallManager) : base(apiCallManager) {}

        /// <summary>
        /// Returns a list of mods that have been updated in a given period, with timestamps of their last update. Cached for 5 minutes.
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <param name="period">The only accepted periods are 1d, 1w and 1m (1 day, 1 week and 1 month)</param>
        /// <returns></returns>
        public List<Mod> Updated(string game, string period)
        {
            var acceptedPeriod = new List<string> {"1d", "1w", "1m"};

            if (!acceptedPeriod.Contains(period))
            {
                throw new ArgumentException($"Only accepted values are {string.Join(", ", acceptedPeriod)}", nameof(period));
            }

            var request = new RestRequest($"/v1/games/{game}/mods/updated.json?period={period}");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"Updated failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Mod>>(result.Content);
        }

        /// <summary>
        /// Returns list of changelogs for mod
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <param name="modId"></param>
        public void ChangeLogs(string game, string modId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Retrieve 10 latest added mods for a specified game
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <returns>A list of mods, or null if something went wrong.</returns>
        public List<Mod> LatestAdded(string game)
        {
            var request = new RestRequest($"v1/games/{game}/mods/latest_added.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"LatestAdded failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Mod>>(result.Content);
        }

        /// <summary>
        /// Retrieve 10 latest updated mods for a specified game
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <returns>A list of mods, or null if something went wrong.</returns>
        public List<Mod> LatestUpdated(string game)
        {
            var request = new RestRequest($"v1/games/{game}/mods/latest_updated.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"LatestUpdated failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Mod>>(result.Content);
        }

        /// <summary>
        /// Retrieve 10 trending mods for a specified game
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <returns>A list of mods, or null if something went wrong.</returns>
        public List<Mod> Trending(string game)
        {
            var request = new RestRequest($"v1/games/{game}/mods/trending.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"Trending failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Mod>>(result.Content);
        }

        /// <summary>
        /// Retrieve specified mod, from a specified game.
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <returns>A <see cref="Mod"/> if the mod is found, otherwise null.</returns>
        public Mod GetById(string game, string modId)
        {
            var request = new RestRequest($"v1/games/{game}/mods/{modId}.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetById failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<Mod>(result.Content);
        }

        /// <summary>
        /// Looks up a file MD5 file hash.
        /// </summary>
        /// <param name="game">Game domain name.</param>
        /// <param name="md5Checksum">Checksum of file to search for.</param>
        /// <returns>A <see cref="Md5SearchResult"/> if the mod is found, otherwise null.</returns>
        public List<Md5SearchResult> SearchByMd5(string game, string md5Checksum)
        {
            var request = new RestRequest($"v1/games/{game}/mods/md5_search/{md5Checksum}.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"SearchByMd5 failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<Md5SearchResult>>(result.Content);
        }

        /// <summary>
        /// Endorse a mod.
        /// </summary>
        /// <param name="game"></param>
        /// <param name="modId"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public bool Endorse(string game, string modId, string version)
        {
            var request = new RestRequest($"v1/games/{game}/mods/{modId}/endorse.json", Method.POST);
            request.AddJsonBody(new {Version = version });
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"Endorse failed, status: {result.StatusCode} : {result.StatusDescription}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Abstain from endorsing a mod
        /// </summary>
        /// <param name="game"></param>
        /// <param name="modId"></param>
        /// <param name="version"></param>
        /// <returns></returns>
        public bool Abstain(string game, string modId, string version)
        {
            var request = new RestRequest($"v1/games/{game}/mods/{modId}/abstain.json", Method.POST);
            request.AddJsonBody(new { Version = version });
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"Abstain failed, status: {result.StatusCode} : {result.StatusDescription}");
                return false;
            }

            return true;
        }
    }
}
