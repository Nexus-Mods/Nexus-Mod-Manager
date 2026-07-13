using System;
using System.Collections.Generic;

namespace Nexus.Client.GameStorage
{
    public class GameStorageFolderManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string App { get; set; } = GameStorageConstants.ApplicationName;
        public GameStorageFolderRole FolderRole { get; set; }
        public string StorageId { get; set; }
        public string GameId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public string LastSeenByVersion { get; set; }
    }

    public class GameStorageRootManifest
    {
        public int SchemaVersion { get; set; } = 1;
        public string App { get; set; } = GameStorageConstants.ApplicationName;
        public string StorageId { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
        public Dictionary<string, string> Folders { get; set; } = new Dictionary<string, string>();
        public bool LinkFolderRequired { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    public class GameStorageRegistry
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> ActiveStorageByGame { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<GameStorageRegistryEntry> KnownStorages { get; set; } = new List<GameStorageRegistryEntry>();
    }

    public class GameStorageRegistryEntry
    {
        public string StorageId { get; set; }
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string StorageRootPath { get; set; }
        public string InstallInfoPath { get; set; }
        public string ModsPath { get; set; }
        public string VirtualInstallPath { get; set; }
        public string LinkFolderPath { get; set; }
        public bool LinkFolderRequired { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool LastKnownGood { get; set; }
        public int LastKnownArchiveCount { get; set; }
        public bool LastKnownInstallLogPresent { get; set; }
        public int LastKnownVirtualFileCount { get; set; }
    }

    public class GameStoragePathSet
    {
        public string GameId { get; set; }
        public string GameName { get; set; }
        public string GameInstallPath { get; set; }
        public string InstallInfoPath { get; set; }
        public string ModsPath { get; set; }
        public string VirtualInstallPath { get; set; }
        public string LinkFolderPath { get; set; }
        public bool LinkFolderRequired { get; set; }
    }
}