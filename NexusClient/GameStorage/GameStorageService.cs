using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Nexus.Client.Games;

namespace Nexus.Client.GameStorage
{
    public class GameStorageService
    {
        private readonly IEnvironmentInfo _environmentInfo;
        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings { Formatting = Formatting.Indented };

        public GameStorageService(IEnvironmentInfo environmentInfo)
        {
            _environmentInfo = environmentInfo;
        }

        public string RegistryDirectory => Path.Combine(_environmentInfo.ApplicationPersonalDataFolderPath, "Game Storage");
        public string RegistryPath => Path.Combine(RegistryDirectory, GameStorageConstants.RegistryFileName);
        public string LastKnownGoodPath => Path.Combine(RegistryDirectory, GameStorageConstants.LastKnownGoodFileName);
        public string BackupDirectory => Path.Combine(RegistryDirectory, "Backups");

        public GameStoragePathSet FromGameMode(IGameMode gameMode)
        {
            string gameId = gameMode.ModeId;
            string linkFolder = GetSettingValue(_environmentInfo.Settings.HDLinkFolder, gameId);
            bool multiHd = GetBoolSettingValue(_environmentInfo.Settings.MultiHDInstall, gameId);

            return new GameStoragePathSet
            {
                GameId = gameId,
                GameName = gameMode.Name,
                GameInstallPath = gameMode.GameModeEnvironmentInfo.InstallationPath,
                InstallInfoPath = gameMode.GameModeEnvironmentInfo.InstallInfoDirectory,
                ModsPath = gameMode.GameModeEnvironmentInfo.ModDirectory,
                VirtualInstallPath = GetSettingValue(_environmentInfo.Settings.VirtualFolder, gameId),
                LinkFolderPath = linkFolder,
                LinkFolderRequired = multiHd || IsLinkFolderRequired(GetSettingValue(_environmentInfo.Settings.VirtualFolder, gameId), gameMode.GameModeEnvironmentInfo.InstallationPath)
            };
        }

        public GameStorageHealthCheck ValidateCurrentStorage(IGameMode gameMode, bool initializeIfValid)
        {
            var paths = FromGameMode(gameMode);
            var registry = LoadRegistry();
            string storageId = ResolveStorageId(paths, registry);
            var result = Validate(paths, storageId, registry);

            if (result.IsHealthy && initializeIfValid)
            {
                InitializeMetadata(paths, storageId, registry);
                result.StorageId = storageId;
            }

            return result;
        }

        public void InitializeMetadataForCurrentStorage(IGameMode gameMode)
        {
            var paths = FromGameMode(gameMode);
            var registry = LoadRegistry();
            string storageId = ResolveStorageId(paths, registry);
            var result = Validate(paths, storageId, registry);
            if (result.IsHealthy)
                InitializeMetadata(paths, storageId, registry);
        }

        public bool IsLinkFolderRequired(string virtualInstallPath, string gameInstallPath)
        {
            string virtualRoot = GetPathRoot(virtualInstallPath);
            string gameRoot = GetPathRoot(gameInstallPath);
            return !string.IsNullOrEmpty(virtualRoot) && !string.IsNullOrEmpty(gameRoot) && !string.Equals(virtualRoot, gameRoot, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsLinkFolderOnGameDrive(string linkFolderPath, string gameInstallPath)
        {
            string linkRoot = GetPathRoot(linkFolderPath);
            string gameRoot = GetPathRoot(gameInstallPath);
            return !string.IsNullOrEmpty(linkRoot) && !string.IsNullOrEmpty(gameRoot) && string.Equals(linkRoot, gameRoot, StringComparison.OrdinalIgnoreCase);
        }

        public List<GameStorageCandidate> DiscoverFromKnownLocations(string gameId)
        {
            var registry = LoadRegistry();
            var candidates = new List<GameStorageCandidate>();
            foreach (var entry in registry.KnownStorages.Where(x => string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(new GameStorageCandidate
                {
                    CandidateKind = "Registry",
                    CandidateRoot = entry.StorageRootPath,
                    GameId = entry.GameId,
                    StorageId = entry.StorageId,
                    InstallInfoPath = entry.InstallInfoPath,
                    ModsPath = entry.ModsPath,
                    VirtualInstallPath = entry.VirtualInstallPath,
                    LinkFolderPath = entry.LinkFolderPath,
                    ConfidenceScore = entry.LastKnownGood ? 90 : 70,
                    ConfidenceLevel = entry.LastKnownGood ? GameStorageCandidateConfidence.High : GameStorageCandidateConfidence.Medium,
                    RequiresUserConfirmation = !entry.LastKnownGood,
                    Evidence = { "Known Game Storage registry entry for this game." }
                });
            }
            return candidates;
        }

        private GameStorageHealthCheck Validate(GameStoragePathSet paths, string storageId, GameStorageRegistry registry)
        {
            var result = new GameStorageHealthCheck { GameId = paths.GameId, StorageId = storageId };
            var lastKnownGood = registry.KnownStorages.FirstOrDefault(x => string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) && x.LastKnownGood);

            ValidateFolder(result, paths, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath, storageId, true);
            ValidateInstallLog(result, paths.InstallInfoPath);
            ValidateFolder(result, paths, GameStorageFolderRole.Mods, paths.ModsPath, storageId, true);
            ValidateFolder(result, paths, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath, storageId, true);

            if (paths.LinkFolderRequired)
            {
                ValidateFolder(result, paths, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath, storageId, true);
                if (!string.IsNullOrWhiteSpace(paths.LinkFolderPath) && Directory.Exists(paths.LinkFolderPath) && !IsLinkFolderOnGameDrive(paths.LinkFolderPath, paths.GameInstallPath))
                {
                    Add(result, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath, GameStorageHealthStatus.LinkFolderOnWrongDrive, true, true,
                        "The Link Folder must be on the same drive as the game because hardlinks cannot cross drives.",
                        "Select a Link Folder on the game drive or move the virtual install staging to the game drive.");
                }
            }
            else
            {
                Add(result, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath, GameStorageHealthStatus.LinkFolderNotRequired, false, true, "The Link Folder is not required for this game storage.");
            }

            AddSuspiciousEmptyWarnings(result, paths, lastKnownGood);
            return result;
        }

        private void ValidateFolder(GameStorageHealthCheck result, GameStoragePathSet paths, GameStorageFolderRole role, string path, string storageId, bool required)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Add(result, role, path, MissingStatus(role), required, true, $"The {GetRoleName(role)} folder is missing or not configured.", "Restore the previous folder or select the correct folder for this game.");
                return;
            }

            var manifest = ReadFolderManifest(path);
            if (manifest == null)
            {
                Add(result, role, path, GameStorageHealthStatus.LegacyValidNeedsInitialization, required, true, $"The {GetRoleName(role)} folder is valid legacy storage and needs a Game Storage manifest.");
                return;
            }

            if (!string.Equals(manifest.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase))
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true, $"The {GetRoleName(role)} manifest belongs to another game.", "Select the correct folder for this game.");
                return;
            }

            if (!string.Equals(manifest.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedStorageId, required, true, $"The {GetRoleName(role)} manifest belongs to a different Game Storage.", "Use folders from the same Game Storage or confirm a recovery candidate.");
                return;
            }

            if (manifest.FolderRole != role)
            {
                Add(result, role, path, GameStorageHealthStatus.PartialMatch, required, true, $"The folder manifest role is {manifest.FolderRole}, but NMM expected {role}.", "Select the folder with the correct Game Storage role.");
                return;
            }

            Add(result, role, path, GameStorageHealthStatus.Healthy, required, true, $"The {GetRoleName(role)} folder is valid.");
        }

        private void ValidateInstallLog(GameStorageHealthCheck result, string installInfoPath)
        {
            if (string.IsNullOrWhiteSpace(installInfoPath) || !Directory.Exists(installInfoPath))
                return;

            string installLog = Path.Combine(installInfoPath, "InstallLog.xml");
            if (!File.Exists(installLog))
            {
                Add(result, GameStorageFolderRole.InstallInfo, installInfoPath, GameStorageHealthStatus.MissingInstallLog, true, true,
                    "InstallInfo exists but InstallLog.xml was not found.",
                    "Restore the previous InstallInfo folder if this game already had installed mods.");
            }
        }

        private void AddSuspiciousEmptyWarnings(GameStorageHealthCheck result, GameStoragePathSet paths, GameStorageRegistryEntry lastKnownGood)
        {
            if (lastKnownGood == null)
                return;

            if (lastKnownGood.LastKnownArchiveCount > 0 && Directory.Exists(paths.ModsPath) && CountModArchives(paths.ModsPath) == 0)
                Add(result, GameStorageFolderRole.Mods, paths.ModsPath, GameStorageHealthStatus.SuspiciousEmptyFolder, true, true, "The Mods folder is empty, but the previous known-good folder contained mod archives.", $"Previous Mods folder: {lastKnownGood.ModsPath}");

            if (lastKnownGood.LastKnownInstallLogPresent && Directory.Exists(paths.InstallInfoPath) && !File.Exists(Path.Combine(paths.InstallInfoPath, "InstallLog.xml")))
                Add(result, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath, GameStorageHealthStatus.SuspiciousEmptyFolder, true, true, "The InstallInfo folder lacks InstallLog.xml, but the previous known-good storage had one.", $"Previous InstallInfo folder: {lastKnownGood.InstallInfoPath}");

            if (lastKnownGood.LastKnownVirtualFileCount > 0 && Directory.Exists(paths.VirtualInstallPath) && CountFiles(paths.VirtualInstallPath) == 0)
                Add(result, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath, GameStorageHealthStatus.SuspiciousEmptyFolder, true, true, "The VirtualInstall folder is empty, but the previous known-good folder contained staged files.", $"Previous VirtualInstall folder: {lastKnownGood.VirtualInstallPath}");
        }

        private void InitializeMetadata(GameStoragePathSet paths, string storageId, GameStorageRegistry registry)
        {
            DateTime now = DateTime.UtcNow;
            WriteFolderManifest(paths.InstallInfoPath, GameStorageFolderRole.InstallInfo, paths, storageId, now);
            WriteFolderManifest(paths.ModsPath, GameStorageFolderRole.Mods, paths, storageId, now);
            WriteFolderManifest(paths.VirtualInstallPath, GameStorageFolderRole.VirtualInstall, paths, storageId, now);
            if (paths.LinkFolderRequired && !string.IsNullOrWhiteSpace(paths.LinkFolderPath) && Directory.Exists(paths.LinkFolderPath))
                WriteFolderManifest(paths.LinkFolderPath, GameStorageFolderRole.LinkFolder, paths, storageId, now);

            string root = TryGetSharedStorageRoot(paths);
            if (!string.IsNullOrWhiteSpace(root))
                WriteRootManifest(root, paths, storageId, now);

            UpsertRegistryEntry(registry, paths, storageId, root, now);
            SaveRegistryWithBackup(registry);
        }

        private void WriteFolderManifest(string folderPath, GameStorageFolderRole role, GameStoragePathSet paths, string storageId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            string manifestPath = Path.Combine(folderPath, GameStorageConstants.FolderManifestFileName);
            var existing = ReadFolderManifest(folderPath);
            var manifest = existing ?? new GameStorageFolderManifest { CreatedUtc = now };
            manifest.SchemaVersion = 1;
            manifest.App = GameStorageConstants.ApplicationName;
            manifest.FolderRole = role;
            manifest.StorageId = storageId;
            manifest.GameId = paths.GameId;
            manifest.LastSeenUtc = now;
            manifest.LastSeenByVersion = _environmentInfo.ApplicationVersion.ToString();
            WriteJson(manifestPath, manifest);
            TryHideFile(manifestPath);
        }

        private void WriteRootManifest(string root, GameStoragePathSet paths, string storageId, DateTime now)
        {
            var manifest = new GameStorageRootManifest
            {
                StorageId = storageId,
                GameId = paths.GameId,
                GameName = paths.GameName,
                LinkFolderRequired = paths.LinkFolderRequired,
                CreatedUtc = now,
                LastSeenUtc = now
            };
            manifest.Folders[GameStorageFolderRole.InstallInfo.ToString()] = ToManifestPath(root, paths.InstallInfoPath);
            manifest.Folders[GameStorageFolderRole.Mods.ToString()] = ToManifestPath(root, paths.ModsPath);
            manifest.Folders[GameStorageFolderRole.VirtualInstall.ToString()] = ToManifestPath(root, paths.VirtualInstallPath);
            manifest.Folders[GameStorageFolderRole.LinkFolder.ToString()] = string.IsNullOrWhiteSpace(paths.LinkFolderPath) ? null : ToManifestPath(root, paths.LinkFolderPath);
            WriteJson(Path.Combine(root, GameStorageConstants.RootManifestFileName), manifest);
        }

        private void UpsertRegistryEntry(GameStorageRegistry registry, GameStoragePathSet paths, string storageId, string root, DateTime now)
        {
            var entry = registry.KnownStorages.FirstOrDefault(x => string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) && string.Equals(x.StorageId, storageId, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                entry = new GameStorageRegistryEntry { StorageId = storageId, GameId = paths.GameId };
                registry.KnownStorages.Add(entry);
            }

            entry.GameName = paths.GameName;
            entry.StorageRootPath = root;
            entry.InstallInfoPath = paths.InstallInfoPath;
            entry.ModsPath = paths.ModsPath;
            entry.VirtualInstallPath = paths.VirtualInstallPath;
            entry.LinkFolderPath = paths.LinkFolderRequired ? paths.LinkFolderPath : null;
            entry.LinkFolderRequired = paths.LinkFolderRequired;
            entry.LastSeenUtc = now;
            entry.LastKnownGood = true;
            entry.LastKnownArchiveCount = CountModArchives(paths.ModsPath);
            entry.LastKnownInstallLogPresent = File.Exists(Path.Combine(paths.InstallInfoPath ?? string.Empty, "InstallLog.xml"));
            entry.LastKnownVirtualFileCount = CountFiles(paths.VirtualInstallPath);
            registry.ActiveStorageByGame[paths.GameId] = storageId;
        }

        private string ResolveStorageId(GameStoragePathSet paths, GameStorageRegistry registry)
        {
            if (registry.ActiveStorageByGame.TryGetValue(paths.GameId, out string activeId) && !string.IsNullOrWhiteSpace(activeId))
                return activeId;

            foreach (var path in new[] { paths.InstallInfoPath, paths.ModsPath, paths.VirtualInstallPath, paths.LinkFolderPath })
            {
                var manifest = ReadFolderManifest(path);
                if (manifest != null && string.Equals(manifest.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(manifest.StorageId))
                    return manifest.StorageId;
            }

            return Guid.NewGuid().ToString("D");
        }

        private GameStorageRegistry LoadRegistry()
        {
            try
            {
                if (File.Exists(RegistryPath))
                    return JsonConvert.DeserializeObject<GameStorageRegistry>(File.ReadAllText(RegistryPath)) ?? new GameStorageRegistry();
            }
            catch
            {
            }
            return new GameStorageRegistry();
        }

        private void SaveRegistryWithBackup(GameStorageRegistry registry)
        {
            Directory.CreateDirectory(RegistryDirectory);
            Directory.CreateDirectory(BackupDirectory);
            if (File.Exists(RegistryPath))
            {
                string backupPath = Path.Combine(BackupDirectory, $"storages-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
                File.Copy(RegistryPath, backupPath, true);
            }
            WriteJson(RegistryPath, registry);
            WriteJson(LastKnownGoodPath, registry);
        }

        private GameStorageFolderManifest ReadFolderManifest(string folderPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                    return null;
                string manifestPath = Path.Combine(folderPath, GameStorageConstants.FolderManifestFileName);
                return File.Exists(manifestPath) ? JsonConvert.DeserializeObject<GameStorageFolderManifest>(File.ReadAllText(manifestPath)) : null;
            }
            catch
            {
                return null;
            }
        }

        private void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(value, _jsonSettings));
        }

        private void Add(GameStorageHealthCheck result, GameStorageFolderRole role, string path, GameStorageHealthStatus status, bool required, bool recoverable, string message, params string[] fixes)
        {
            result.Items.Add(new GameStorageHealthItem
            {
                Role = role,
                Path = path,
                Status = status,
                IsRequired = required,
                IsRecoverable = recoverable,
                Message = message,
                SuggestedFixes = fixes.Where(x => !string.IsNullOrWhiteSpace(x)).ToList()
            });
        }

        private GameStorageHealthStatus MissingStatus(GameStorageFolderRole role)
        {
            switch (role)
            {
                case GameStorageFolderRole.InstallInfo: return GameStorageHealthStatus.MissingInstallInfo;
                case GameStorageFolderRole.Mods: return GameStorageHealthStatus.MissingMods;
                case GameStorageFolderRole.VirtualInstall: return GameStorageHealthStatus.MissingVirtualInstall;
                case GameStorageFolderRole.LinkFolder: return GameStorageHealthStatus.MissingLinkFolder;
                default: return GameStorageHealthStatus.Unknown;
            }
        }

        private string TryGetSharedStorageRoot(GameStoragePathSet paths)
        {
            var required = new List<string> { paths.InstallInfoPath, paths.ModsPath, paths.VirtualInstallPath };
            if (paths.LinkFolderRequired && !string.IsNullOrWhiteSpace(paths.LinkFolderPath))
                required.Add(paths.LinkFolderPath);
            var parents = required.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetDirectoryName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return parents.Count == 1 ? parents[0] : null;
        }

        private string ToManifestPath(string root, string path)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
                return path;
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return path;
            return path.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
        }

        private int CountModArchives(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return 0;
            string[] extensions = { ".zip", ".7z", ".rar", ".fomod", ".omod" };
            try
            {
                return Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly).Count(x => extensions.Contains(Path.GetExtension(x), StringComparer.OrdinalIgnoreCase));
            }
            catch
            {
                return 0;
            }
        }

        private int CountFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return 0;
            try
            {
                return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Take(101).Count();
            }
            catch
            {
                return 0;
            }
        }

        private string GetPathRoot(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? null : Path.GetPathRoot(path);
            }
            catch
            {
                return null;
            }
        }

        private string GetRoleName(GameStorageFolderRole role)
        {
            return role == GameStorageFolderRole.LinkFolder ? "Link Folder" : role.ToString();
        }

        private string GetSettingValue(IDictionary<string, string> settings, string gameId)
        {
            return settings != null && settings.ContainsKey(gameId) ? settings[gameId] : null;
        }

        private bool GetBoolSettingValue(IDictionary<string, bool> settings, string gameId)
        {
            return settings != null && settings.ContainsKey(gameId) && settings[gameId];
        }

        private void TryHideFile(string path)
        {
            try
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }
            catch
            {
            }
        }
    }
}