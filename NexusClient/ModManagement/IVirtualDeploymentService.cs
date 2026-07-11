namespace Nexus.Client.ModManagement
{
    using Nexus.Client.Mods;

    /// <summary>
    /// Orchestrates virtual file deployment while keeping caller-owned policy outside the deployment backend.
    /// </summary>
    public interface IVirtualDeploymentService
    {
        VirtualDeploymentResult ActivateModLinks(IMod mod, VirtualDeploymentOptions options);
    }
}