namespace Nexus.Client.GameStorage
{
    public enum GameStorageFolderRole
    {
        InstallInfo,
        Mods,
        VirtualInstall,
        LinkFolder,
        Cache,
        Backups
    }

    public enum GameStorageHealthStatus
    {
        Healthy,
        MissingStorageRoot,
        MissingInstallInfo,
        MissingMods,
        MissingVirtualInstall,
        MissingInstallLog,
        MissingLinkFolder,
        LinkFolderRequired,
        LinkFolderNotRequired,
        LinkFolderOnWrongDrive,
        MismatchedGame,
        MismatchedStorageId,
        SuspiciousEmptyFolder,
        PartialMatch,
        LegacyValidNeedsInitialization,
        CompatibleSharedModsLibrary,
        NotWritable,
        Unknown,
        InvalidManifest,
        UnsupportedManifestVersion,
        FolderRoleCollision
    }

    public enum GameStorageCandidateConfidence
    {
        Low,
        Medium,
        High
    }
}