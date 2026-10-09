namespace Nexus.Client.ModManagement
{
    using Nexus.Client.Mods;

    /// <summary>Supports switching an exact deployment target without losing its Data/game-root identity.</summary>
    public interface IRootAwareVirtualDeploymentService : IVirtualDeploymentService
    {
        /// <summary>Switches the owner of one canonical deployment target.</summary>
        VirtualFileOwnerSwitchResult SwitchFileOwner(ModDeploymentTarget target, string selectedOwnerKey);
    }

    /// <summary>
    /// Orchestrates virtual file deployment while keeping caller-owned policy outside the deployment backend.
    /// </summary>
    public interface IVirtualDeploymentService
    {
        VirtualDeploymentResult ActivateModLinks(IMod mod, VirtualDeploymentOptions options);
        VirtualFileOwnerSwitchResult SwitchFileOwner(string relativePath, string selectedOwnerKey);
    }
}
