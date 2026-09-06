namespace Nexus.Client.GameStorage
{
    internal static class GameStorageConstants
    {
        public const string ApplicationName = "Nexus Mod Manager CE";
        public const string FolderManifestFileName = ".nmm-folder.json";
        public const string RootManifestFileName = "NMMStorage.json";
        public const string RegistryFileName = "storages.json";
        public const string LastKnownGoodFileName = "storages.last-known-good.json";
        public const int FolderManifestSchemaVersion = 2;
        public const int RootManifestSchemaVersion = 1;
        public const int RegistrySchemaVersion = 1;
        public const int RegistryBackupRetentionCount = 20;
		public const string VirtualInstallDirectoryName = "VirtualInstall";
	}
}