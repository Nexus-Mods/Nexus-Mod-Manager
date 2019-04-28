namespace Nexus.Client.ModRepositories.NexusModsApi.EndPoints
{
    public abstract class EndPoint
    {
        protected EndPoint(ApiCallManager apiCallManager)
        {
            ApiCallManager = apiCallManager;
        }

        protected ApiCallManager ApiCallManager { get; }
    }
}
