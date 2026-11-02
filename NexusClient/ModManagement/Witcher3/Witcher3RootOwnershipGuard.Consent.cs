using System;
using Nexus.Client.GameStorage;
using Nexus.Client.Settings;

namespace Nexus.Client.ModManagement.Witcher3
{
    public static partial class Witcher3RootOwnershipGuard
    {
        /// <summary>Bridges the retained per-game callers to the common session consent policy.</summary>
        internal static Action CreateSessionApproval(IEnvironmentInfo environment, string modeId, string root, string info, string virtualInstall)
        {
            return GameStorageSessionConsent.CreateNativeApproval(environment, modeId, root, info, virtualInstall);
        }

        /// <summary>Recognizes an explicit Continue choice at the later Witcher native ownership boundary.</summary>
        internal static bool HasSessionApproval(IEnvironmentInfo environment, string modeId, string root, string info, string virtualInstall)
        {
            return IsWitcherMode(modeId) && GameStorageSessionConsent.HasNativeApproval(environment, modeId, root, info, virtualInstall);
        }

        /// <summary>Retains the existing internal reset entry point while clearing the common policy's choice.</summary>
        internal static void RevokeSessionApproval(ISettings settings, string modeId) { GameStorageSessionConsent.Revoke(settings, modeId); }

        /// <summary>Resolves saved Virtual paths using the common storage policy.</summary>
        internal static string GetConfiguredVirtualPath(IEnvironmentInfo environment, string modeId) { return GameStorageSessionConsent.ConfiguredVirtualPath(environment, modeId); }
    }
}
