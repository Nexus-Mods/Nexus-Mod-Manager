using System;
using System.Linq;
using System.Windows.Forms;
using Nexus.Client.ModManagement.Witcher3;
using Nexus.Client.Util.Localization;

namespace Nexus.Client.GameStorage
{
    public partial class GameStorageService
    {
        /// <summary>Offers Continue or Cancel for storage warnings for any supported game without changing files during review.</summary>
        internal bool ReviewStorageWarnings(IWin32Window owner, GameStoragePathSet paths)
        {
            if (paths == null || _environmentInfo == null) return false;
            if (HasApprovedStoragePaths(paths)) return true;
            GameStorageHealthCheck health = InspectStorageWarnings(paths);
            if (!health.HasWarnings) return true;
            Action approve = GameStorageSessionConsent.CreateStorageApproval(_environmentInfo, paths);
            using (var warning = new UI.GameStorageWarningForm(UI.GameStorageWarningForm.CreateMessage(paths, health)))
                if (warning.ShowDialog(owner) != DialogResult.OK) return false;
            approve();
            return HasApprovedStoragePaths(paths);
        }

        /// <summary>Combines metadata health and saved cross-game path conflicts into advisory information.</summary>
        internal GameStorageHealthCheck InspectStorageWarnings(GameStoragePathSet paths)
        {
            GameStorageHealthCheck health = ValidateStorageForSetup(paths);
            if (_environmentInfo == null) return health;
            foreach (string file in new[] { RegistryPath, LastKnownGoodPath })
            {
                var read = ReadRegistryResult(file);
                if (read.Status == GameStorageMetadataReadStatus.Invalid || read.Status == GameStorageMetadataReadStatus.UnsupportedVersion)
                    Add(health, null, file, GameStorageHealthStatus.Warning, false, true,
                        LanguageManager.Get("GameStorage.Warning.Registry", "Storage registry metadata is unreadable or uses a newer format. Continue keeps that file unchanged and uses the selected folders."));
            }
            if (Witcher3RootOwnershipGuard.IsWitcherMode(paths.GameId))
            {
                Witcher3RootOwnershipDecision ownership = InspectWitcherOwnership(paths);
                if (!ownership.AllowsNativeMutation && !health.Items.Any(item => item.Message == ownership.Reason))
                    Add(health, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath, GameStorageHealthStatus.Warning, false, true, ownership.Reason);
            }
            foreach (GameStoragePathSet other in GameStorageSessionConsent.OtherConfigurations(_environmentInfo, paths.GameId))
            {
                bool sharedNative = Witcher3RootOwnershipGuard.CanShareNativeStorage(paths.GameId, other.GameId) &&
                    AreSamePaths(paths.GameInstallPath, other.GameInstallPath) && AreSamePaths(paths.InstallInfoPath, other.InstallInfoPath) &&
                    (AreSamePaths(paths.VirtualInstallPath, other.VirtualInstallPath) || String.IsNullOrWhiteSpace(paths.VirtualInstallPath) && String.IsNullOrWhiteSpace(other.VirtualInstallPath));
                if (sharedNative) continue;
                bool nativeOverlap = new[] { paths.InstallInfoPath, paths.VirtualInstallPath }.Any(path =>
                    new[] { other.InstallInfoPath, other.VirtualInstallPath }.Any(candidate => GameStorageSessionConsent.Overlaps(path, candidate)));
                bool archiveOverlap = GameStorageSessionConsent.Overlaps(paths.ModsPath, other.ModsPath) &&
                    !Games.DataDriven.GameModeStorageSharingRegistry.CanShareModsStorage(paths.GameId, other.GameId);
                bool rootOverlap = GameStorageSessionConsent.Overlaps(paths.GameInstallPath, other.GameInstallPath) &&
                    (!String.IsNullOrWhiteSpace(other.InstallInfoPath) || !String.IsNullOrWhiteSpace(other.VirtualInstallPath)) &&
                    !IsNativeSetupVerifiablyEmpty(other.GameId, other.InstallInfoPath, other.VirtualInstallPath);
                if (!nativeOverlap && !archiveOverlap && !rootOverlap) continue;
                Add(health, null, other.InstallInfoPath ?? other.ModsPath ?? other.GameInstallPath, GameStorageHealthStatus.Warning, false, true,
                    LanguageManager.Format("GameStorage.Warning.OtherSetup", "Saved setup {0} uses overlapping folders. Game: {1}; Install Info: {2}; Mods: {3}; Virtual Install: {4}. Saved paths do not prove that the game is still installed.",
                        other.GameId, other.GameInstallPath, other.InstallInfoPath, other.ModsPath, other.VirtualInstallPath));
            }
            return health;
        }

        /// <summary>Reuses the selected InstallInfo identity, or derives one from its physical directory when optional metadata is unavailable.</summary>
        private string GetAcceptedStorageId(GameStoragePathSet paths, GameStorageRegistry registry)
        {
            var manifest = ReadFolderManifestResult(paths.InstallInfoPath).Value;
            string candidate = manifest == null ? null : manifest.StorageId;
            if (String.IsNullOrWhiteSpace(candidate)) candidate = FindExactRegistryStorageId(paths, registry);
            if (!String.IsNullOrWhiteSpace(candidate))
                try { return CollectionManagement.CollectionIdentityValidation.RequireOpaqueToken(candidate, "storageId"); }
                catch (ArgumentException) { }
            string physicalKey = System.IO.Directory.Exists(paths.InstallInfoPath)
                ? CollectionManagement.CollectionPhysicalDirectoryIdentity.Resolve(paths.InstallInfoPath).StableKey
                : System.IO.Path.GetFullPath(paths.InstallInfoPath).TrimEnd('\\', '/').ToUpperInvariant();
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes("NMMCE selected InstallInfo\n" + physicalKey));
                var guid = new byte[16];
                Array.Copy(bytes, guid, guid.Length);
                return new Guid(guid).ToString("D");
            }
        }

        /// <summary>Leaves unreadable and future optional registries intact when the user continues with selected folders.</summary>
        private bool CanWriteApprovedRegistry()
        {
            return new[] { RegistryPath, LastKnownGoodPath }.All(file =>
            {
                GameStorageMetadataReadStatus status = ReadRegistryResult(file).Status;
                return status == GameStorageMetadataReadStatus.Valid || status == GameStorageMetadataReadStatus.Missing;
            });
        }

        /// <summary>Recognizes the user's Continue choice for the exact game and selected storage paths.</summary>
        internal bool HasApprovedStoragePaths(GameStoragePathSet paths)
        {
            return paths != null && GameStorageSessionConsent.HasStorageApproval(_environmentInfo, paths);
        }
    }
}
