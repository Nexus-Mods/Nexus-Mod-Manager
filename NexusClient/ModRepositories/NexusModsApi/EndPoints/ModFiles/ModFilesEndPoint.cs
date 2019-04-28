namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.ModFiles
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using RestSharp;
    using SimpleJson;
    using Util;

    public class ModFilesEndPoint : EndPoint
    {
        public ModFilesEndPoint(ApiCallManager apiCallManager) : base(apiCallManager) {}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="gameDomainName">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <param name="category">
        /// Filter File Category. Case insensitive. Comma separated list of categories.
        /// Can be: "main", "update", "optional", "old_version" or "miscellaneous".</param>
        /// <returns><see cref="GetModFilesResponseDataContract"/> with all mod file information for the given mod.</returns>
        public List<GetModFilesResponse> ListAllModFiles(string gameDomainName, int modId, string category = "")
        {
            var request = new RestRequest($"v1/games/{gameDomainName}/mods/{modId}/files.json" + (string.IsNullOrEmpty(category) ? string.Empty : $"?category={category}"));
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"ListAllModFiles failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<GetModFilesResponse>>(result.Content);
        }

        /// <summary>
        /// View a specified mod file, using a specified game and mod
        /// </summary>
        /// <param name="gameDomainName">Game domain name.</param>
        /// <param name="modId">ID for mod to retrieve info for.</param>
        /// <param name="fileId">ID for the file to retrieve info for.</param>
        /// <returns>A single <see cref="ModFile"/>, or null if not found.</returns>
        public ModFile GetModFile(string gameDomainName, int modId, int fileId)
        {
            var request = new RestRequest($"v1/games/{gameDomainName}/mods/{modId}/files/{fileId}.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetModFile failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<ModFile>(result.Content);
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
        public List<ModFileDownloadInfo> GetModFileDownloadLink(int gameId, int modId, int fileId, string key = "", int expires = -1)
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

            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                // TODO: Test this before release
                dynamic content = SimpleJson.DeserializeObject(result.Content);
                Trace.TraceInformation($"GetModFileDownloadLink failed, status: {result.StatusCode} : {content.message}");
                return null;
            }

            return JSONSerializer.Deserialize<List<ModFileDownloadInfo>>(result.Content);
        }
    }
}
