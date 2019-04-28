namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints.ColourSchemes
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using RestSharp;
    using Util;

    public class ColourSchemesEndPoint : EndPoint
    {
        public ColourSchemesEndPoint(ApiCallManager apiCallManager) : base(apiCallManager)
        {
        }

        /// <summary>
        /// Returns list of all colour schemes, including the primary, secondary and 'darker' colours.
        /// </summary>
        /// <returns>A list of all available <see cref="ColourScheme"/>s.</returns>
        public List<ColourScheme> GetAllColourSchemes()
        {
            var request = new RestRequest("v1/colourschemes.json");
            var result = ApiCallManager.ExecuteRequest(request);

            if (!result.IsSuccessful)
            {
                Trace.TraceInformation($"GetAllColourSchemes failed, status: {result.StatusCode} : {result.StatusDescription}");
                return null;
            }

            return JSONSerializer.Deserialize<List<ColourScheme>>(result.Content);
        }
    }
}
