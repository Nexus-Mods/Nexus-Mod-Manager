using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Nexus.Client.Games;

namespace Nexus.Client.GameStorage
{
    public partial class GameStorageService
    {
        public List<GameStorageCandidate> DiscoverRecoveryCandidates(IGameMode gameMode)
        {
            return DiscoverRecoveryCandidates(FromGameMode(gameMode));
        }

        public List<GameStorageCandidate> DiscoverRecoveryCandidates(GameStoragePathSet currentPaths)
        {
            var registry = LoadRegistry();
            var candidates = new List<GameStorageCandidate>();

            int currentScore = GetPathSetEvidenceScore(currentPaths, 5);
            AddCandidate(candidates, CreateCandidateFromPathSet("Current configuration", currentPaths, null, currentScore, GetConfidenceLevel(currentScore), true,
                "Current per-game path settings."));

            foreach (var entry in registry.KnownStorages.Where(x => string.Equals(x.GameId, currentPaths.GameId, StringComparison.OrdinalIgnoreCase)))
                AddCandidate(candidates, CreateCandidateFromRegistry(entry, entry.LastKnownGood ? "Known good registry" : "Known registry", entry.LastKnownGood ? 95 : 75));

            foreach (var entry in LoadLastKnownGoodRegistry().KnownStorages.Where(x => string.Equals(x.GameId, currentPaths.GameId, StringComparison.OrdinalIgnoreCase)))
                AddCandidate(candidates, CreateCandidateFromRegistry(entry, "Last-known-good backup", 98));

            foreach (var root in GetLikelyRoots(currentPaths, registry, currentPaths.GameId))
            {
                foreach (var candidate in DiscoverRecoveryCandidatesFromRoot(currentPaths, root))
                    AddCandidate(candidates, candidate);
            }

            return candidates
                .Where(x => x != null)
                .OrderByDescending(x => x.ConfidenceScore)
                .ThenBy(x => x.CandidateKind)
                .ToList();
        }

        public List<GameStorageCandidate> DiscoverRecoveryCandidatesFromRoot(IGameMode gameMode, string rootPath)
        {
            return DiscoverRecoveryCandidatesFromRoot(FromGameMode(gameMode), rootPath);
        }

        public List<GameStorageCandidate> DiscoverRecoveryCandidatesFromRoot(GameStoragePathSet currentPaths, string rootPath)
        {
            var candidates = new List<GameStorageCandidate>();
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return candidates;

            AddCandidate(candidates, TryCreateCandidateFromRootManifest(currentPaths, rootPath, true));

            try
            {
                foreach (var child in Directory.EnumerateDirectories(rootPath).Take(200))
                    AddCandidate(candidates, TryCreateCandidateFromRootManifest(currentPaths, child, false));
            }
            catch
            {
            }

            AddFolderManifestCandidates(currentPaths, rootPath, candidates);
            AddLegacyLayoutCandidate(currentPaths, rootPath, candidates);
            AddInstallLogOnlyCandidates(rootPath, candidates);
            return candidates.OrderByDescending(x => x.ConfidenceScore).ToList();
        }

        public bool ApplyRecoveryCandidate(IGameMode gameMode, GameStorageCandidate candidate, out GameStorageHealthCheck healthCheck)
        {
            var currentPaths = FromGameMode(gameMode);
            if (!ValidateRecoveryCandidate(currentPaths, candidate, out healthCheck))
                return false;

            var paths = CreatePathSetFromCandidate(currentPaths, candidate);
            ApplyPathSet(paths);
            return true;
        }

        public bool ApplyInitialSetupCandidate(GameStoragePathSet currentPaths, GameStorageCandidate candidate, out GameStorageHealthCheck healthCheck)
        {
            healthCheck = null;
            if (candidate == null || !string.Equals(candidate.GameId, currentPaths.GameId, StringComparison.OrdinalIgnoreCase))
                return false;

            var paths = CreatePathSetFromCandidate(currentPaths, candidate);
            CreateSetupFolders(paths);
            string storageId = string.IsNullOrWhiteSpace(candidate.StorageId) ? ResolveStorageId(paths, LoadRegistry()) : candidate.StorageId;
            var registry = LoadRegistry();
            healthCheck = Validate(paths, storageId, registry);
            if (!healthCheck.IsHealthy)
                return false;

            InitializeMetadata(paths, storageId, registry);
            ApplyPathSet(paths);
            healthCheck = Validate(paths, storageId, registry);
            healthCheck.StorageId = storageId;
            return healthCheck.IsHealthy;
        }

        private void ApplyPathSet(GameStoragePathSet paths)
        {
            string gameId = paths.GameId;
            _environmentInfo.Settings.InstallInfoFolder[gameId] = paths.InstallInfoPath;
            _environmentInfo.Settings.ModFolder[gameId] = paths.ModsPath;
            _environmentInfo.Settings.VirtualFolder[gameId] = paths.VirtualInstallPath;
            _environmentInfo.Settings.HDLinkFolder[gameId] = paths.LinkFolderRequired ? paths.LinkFolderPath : null;
            _environmentInfo.Settings.MultiHDInstall[gameId] = paths.LinkFolderRequired;
            _environmentInfo.Settings.Save();
        }

        private void CreateSetupFolders(GameStoragePathSet paths)
        {
            CreateFolder(paths.InstallInfoPath);
            CreateFolder(paths.ModsPath);
            CreateFolder(paths.VirtualInstallPath);
            if (paths.LinkFolderRequired)
                CreateFolder(paths.LinkFolderPath);
        }

        private void CreateFolder(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        public bool ValidateRecoveryCandidate(GameStoragePathSet currentPaths, GameStorageCandidate candidate, out GameStorageHealthCheck healthCheck)
        {
            healthCheck = null;
            if (candidate == null || !string.Equals(candidate.GameId, currentPaths.GameId, StringComparison.OrdinalIgnoreCase))
                return false;

            var paths = CreatePathSetFromCandidate(currentPaths, candidate);
            string storageId = string.IsNullOrWhiteSpace(candidate.StorageId) ? ResolveStorageId(paths, LoadRegistry()) : candidate.StorageId;
            var registry = LoadRegistry();
            healthCheck = Validate(paths, storageId, registry);
            if (!healthCheck.IsHealthy)
                return false;

            InitializeMetadata(paths, storageId, registry);
            healthCheck = Validate(paths, storageId, registry);
            healthCheck.StorageId = storageId;
            return healthCheck.IsHealthy;
        }

        private GameStorageCandidate CreateCandidateFromRegistry(GameStorageRegistryEntry entry, string kind, int score)
        {
            if (entry == null)
                return null;

            var candidate = new GameStorageCandidate
            {
                CandidateKind = kind,
                CandidateRoot = entry.StorageRootPath,
                GameId = entry.GameId,
                StorageId = entry.StorageId,
                InstallInfoPath = entry.InstallInfoPath,
                ModsPath = entry.ModsPath,
                VirtualInstallPath = entry.VirtualInstallPath,
                LinkFolderPath = entry.LinkFolderPath,
                LinkFolderRequired = entry.LinkFolderRequired,
                ConfidenceScore = score,
                ConfidenceLevel = score >= 85 ? GameStorageCandidateConfidence.High : GameStorageCandidateConfidence.Medium,
                RequiresUserConfirmation = score < 95
            };
            candidate.Evidence.Add(kind + " entry for this game.");
            AddPathWarnings(candidate);
            return candidate;
        }

        private GameStorageCandidate CreateCandidateFromPathSet(string kind, GameStoragePathSet paths, string storageId, int score, GameStorageCandidateConfidence confidence, bool requiresConfirmation, string evidence)
        {
            var candidate = new GameStorageCandidate
            {
                CandidateKind = kind,
                CandidateRoot = TryGetSharedStorageRoot(paths),
                GameId = paths.GameId,
                StorageId = storageId,
                InstallInfoPath = paths.InstallInfoPath,
                ModsPath = paths.ModsPath,
                VirtualInstallPath = paths.VirtualInstallPath,
                LinkFolderPath = paths.LinkFolderPath,
                LinkFolderRequired = paths.LinkFolderRequired,
                ConfidenceScore = score,
                ConfidenceLevel = confidence,
                RequiresUserConfirmation = requiresConfirmation
            };
            candidate.Evidence.Add(evidence);
            AddPathWarnings(candidate);
            return candidate;
        }

        private GameStorageCandidate TryCreateCandidateFromRootManifest(GameStoragePathSet currentPaths, string rootPath, bool explicitRoot)
        {
            string manifestPath = Path.Combine(rootPath, GameStorageConstants.RootManifestFileName);
            if (!File.Exists(manifestPath))
                return null;

            try
            {
                var manifest = JsonConvert.DeserializeObject<GameStorageRootManifest>(File.ReadAllText(manifestPath));
                if (manifest == null)
                    return null;

                bool gameMatches = string.Equals(manifest.GameId, currentPaths.GameId, StringComparison.OrdinalIgnoreCase);
                var candidate = new GameStorageCandidate
                {
                    CandidateKind = explicitRoot ? "Selected root manifest" : "Root manifest",
                    CandidateRoot = rootPath,
                    GameId = manifest.GameId,
                    StorageId = manifest.StorageId,
                    InstallInfoPath = ResolveManifestFolder(rootPath, manifest, GameStorageFolderRole.InstallInfo),
                    ModsPath = ResolveManifestFolder(rootPath, manifest, GameStorageFolderRole.Mods),
                    VirtualInstallPath = ResolveManifestFolder(rootPath, manifest, GameStorageFolderRole.VirtualInstall),
                    LinkFolderPath = ResolveManifestFolder(rootPath, manifest, GameStorageFolderRole.LinkFolder),
                    LinkFolderRequired = manifest.LinkFolderRequired,
                    ConfidenceScore = gameMatches ? 92 : 30,
                    ConfidenceLevel = gameMatches ? GameStorageCandidateConfidence.High : GameStorageCandidateConfidence.Low,
                    RequiresUserConfirmation = true
                };
                candidate.Evidence.Add(gameMatches ? "Found NMMStorage.json with matching game ID." : "Found NMMStorage.json for another or unknown game.");
                if (!gameMatches)
                    candidate.Warnings.Add("The root manifest does not match the selected game.");
                AddPathWarnings(candidate);
                return candidate;
            }
            catch
            {
                return null;
            }
        }

        private void AddFolderManifestCandidates(GameStoragePathSet currentPaths, string rootPath, List<GameStorageCandidate> candidates)
        {
            var manifests = new List<Tuple<string, GameStorageFolderManifest>>();
            foreach (var folder in EnumerateLikelyFolders(rootPath))
            {
                var manifest = ReadFolderManifest(folder);
                if (manifest != null)
                    manifests.Add(Tuple.Create(folder, manifest));
            }

            foreach (var group in manifests.Where(x => string.Equals(x.Item2.GameId, currentPaths.GameId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Item2.StorageId)).GroupBy(x => x.Item2.StorageId))
            {
                var candidate = new GameStorageCandidate
                {
                    CandidateKind = "Folder manifests",
                    CandidateRoot = rootPath,
                    GameId = currentPaths.GameId,
                    StorageId = group.Key,
                    ConfidenceScore = 80,
                    ConfidenceLevel = GameStorageCandidateConfidence.Medium,
                    RequiresUserConfirmation = true
                };

                foreach (var item in group)
                {
                    switch (item.Item2.FolderRole)
                    {
                        case GameStorageFolderRole.InstallInfo: candidate.InstallInfoPath = item.Item1; break;
                        case GameStorageFolderRole.Mods: candidate.ModsPath = item.Item1; break;
                        case GameStorageFolderRole.VirtualInstall: candidate.VirtualInstallPath = item.Item1; break;
                        case GameStorageFolderRole.LinkFolder: candidate.LinkFolderPath = item.Item1; candidate.LinkFolderRequired = true; break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(candidate.InstallInfoPath) && !string.IsNullOrWhiteSpace(candidate.ModsPath) && !string.IsNullOrWhiteSpace(candidate.VirtualInstallPath))
                {
                    candidate.ConfidenceScore = 88;
                    candidate.ConfidenceLevel = GameStorageCandidateConfidence.High;
                }
                candidate.Evidence.Add("Found folder manifests with matching game ID and Storage ID.");
                AddPathWarnings(candidate);
                AddCandidate(candidates, candidate);
            }
        }

        private void AddLegacyLayoutCandidate(GameStoragePathSet currentPaths, string rootPath, List<GameStorageCandidate> candidates)
        {
            string installInfoPath = SelectExistingPath(Path.Combine(rootPath, "Install Info"), Path.Combine(rootPath, "InstallInfo"));
            if (string.IsNullOrWhiteSpace(installInfoPath) || !File.Exists(Path.Combine(installInfoPath, "InstallLog.xml")))
                return;

            string modsPath = SelectExistingPath(Path.Combine(rootPath, "Mods"));
            string virtualInstallPath = SelectExistingPath(Path.Combine(rootPath, "VirtualInstall"));
            string linkFolderPath = SelectExistingPath(Path.Combine(rootPath, "LinkFolder"));

            var candidate = new GameStorageCandidate
            {
                CandidateKind = "Legacy NMM setup",
                CandidateRoot = rootPath,
                GameId = currentPaths.GameId,
                InstallInfoPath = installInfoPath,
                ModsPath = modsPath ?? Path.Combine(rootPath, "Mods"),
                VirtualInstallPath = virtualInstallPath ?? Path.Combine(rootPath, "VirtualInstall"),
                LinkFolderPath = linkFolderPath,
                LinkFolderRequired = false,
                RequiresUserConfirmation = true
            };

            candidate.ConfidenceScore = GetPathSetEvidenceScore(CreatePathSetFromCandidate(currentPaths, candidate), 35);
            candidate.ConfidenceLevel = GetConfidenceLevel(candidate.ConfidenceScore);
            candidate.Evidence.Add("Found legacy NMM folder layout with InstallLog.xml.");
            AddPathWarnings(candidate);
            AddCandidate(candidates, candidate);
        }

        private void AddInstallLogOnlyCandidates(string rootPath, List<GameStorageCandidate> candidates)
        {
            foreach (var folder in EnumerateLikelyFolders(rootPath))
            {
                if (ReadFolderManifest(folder) != null || !File.Exists(Path.Combine(folder, "InstallLog.xml")))
                    continue;

                var candidate = new GameStorageCandidate
                {
                    CandidateKind = "Possible InstallInfo folder",
                    CandidateRoot = rootPath,
                    InstallInfoPath = folder,
                    ConfidenceScore = 15,
                    ConfidenceLevel = GameStorageCandidateConfidence.Low,
                    RequiresUserConfirmation = true
                };
                candidate.Evidence.Add("Found InstallLog.xml, but no Game Storage manifest.");
                candidate.Warnings.Add("This candidate is ambiguous. NMM cannot identify the game or Storage ID from InstallLog.xml alone.");
                AddCandidate(candidates, candidate);
            }
        }

        private int GetPathSetEvidenceScore(GameStoragePathSet paths, int baseScore)
        {
            if (paths == null)
                return 0;

            int score = baseScore;
            if (Directory.Exists(paths.InstallInfoPath))
                score += 15;
            if (!string.IsNullOrWhiteSpace(paths.InstallInfoPath) && File.Exists(Path.Combine(paths.InstallInfoPath, "InstallLog.xml")))
                score += 20;
            if (Directory.Exists(paths.ModsPath))
                score += 10;
            if (ContainsAnyFile(paths.ModsPath))
                score += 10;
            if (Directory.Exists(paths.VirtualInstallPath))
                score += 10;
            if (ContainsAnyFile(paths.VirtualInstallPath))
                score += 10;
            if (paths.LinkFolderRequired && Directory.Exists(paths.LinkFolderPath))
                score += 5;

            return Math.Min(score, 90);
        }

        private GameStorageCandidateConfidence GetConfidenceLevel(int score)
        {
            if (score >= 70)
                return GameStorageCandidateConfidence.High;
            if (score >= 35)
                return GameStorageCandidateConfidence.Medium;
            return GameStorageCandidateConfidence.Low;
        }

        private bool ContainsAnyFile(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && Directory.EnumerateFiles(path).Any();
            }
            catch
            {
                return false;
            }
        }

        private string SelectExistingPath(params string[] paths)
        {
            return paths?.FirstOrDefault(Directory.Exists);
        }

        private IEnumerable<string> EnumerateLikelyFolders(string rootPath)
        {
            yield return rootPath;
            string[] expected = { "InstallInfo", "Mods", "VirtualInstall", "LinkFolder" };
            foreach (var name in expected)
            {
                string path = Path.Combine(rootPath, name);
                if (Directory.Exists(path))
                    yield return path;
            }

            IEnumerable<string> children = Enumerable.Empty<string>();
            try
            {
                children = Directory.EnumerateDirectories(rootPath).Take(200).ToList();
            }
            catch
            {
            }
            foreach (var child in children)
                yield return child;
        }

        private IEnumerable<string> GetLikelyRoots(GameStoragePathSet currentPaths, GameStorageRegistry registry, string gameId)
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddCandidateRoot(roots, currentPaths.InstallInfoPath);
            AddCandidateRoot(roots, currentPaths.ModsPath);
            AddCandidateRoot(roots, currentPaths.VirtualInstallPath);
            AddCandidateRoot(roots, currentPaths.LinkFolderPath);
            AddGameDriveRoots(roots, currentPaths);

            foreach (var entry in registry.KnownStorages.Where(x => string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase)))
            {
                AddRoot(roots, entry.StorageRootPath);
                AddCandidateRoot(roots, entry.InstallInfoPath);
                AddCandidateRoot(roots, entry.ModsPath);
                AddCandidateRoot(roots, entry.VirtualInstallPath);
                AddCandidateRoot(roots, entry.LinkFolderPath);
            }

            foreach (var entry in LoadLastKnownGoodRegistry().KnownStorages.Where(x => string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase)))
            {
                AddRoot(roots, entry.StorageRootPath);
                AddCandidateRoot(roots, entry.InstallInfoPath);
                AddCandidateRoot(roots, entry.ModsPath);
                AddCandidateRoot(roots, entry.VirtualInstallPath);
                AddCandidateRoot(roots, entry.LinkFolderPath);
            }

            return roots.Where(Directory.Exists).ToList();
        }

        private void AddGameDriveRoots(HashSet<string> roots, GameStoragePathSet currentPaths)
        {
            string gameDriveRoot = GetPathRoot(currentPaths.GameInstallPath);
            if (string.IsNullOrWhiteSpace(gameDriveRoot))
                return;

            AddRoot(roots, Path.Combine(gameDriveRoot, "Games", "NMMCE", currentPaths.GameId));
            AddRoot(roots, Path.Combine(gameDriveRoot, "Games", "Nexus Mod Manager", currentPaths.GameId));
        }

        private void AddCandidateRoot(HashSet<string> roots, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            AddRoot(roots, path);
            try
            {
                AddRoot(roots, Path.GetDirectoryName(path));
            }
            catch
            {
            }
        }

        private void AddRoot(HashSet<string> roots, string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                roots.Add(path);
        }

        private string ResolveManifestFolder(string rootPath, GameStorageRootManifest manifest, GameStorageFolderRole role)
        {
            if (manifest.Folders == null || !manifest.Folders.TryGetValue(role.ToString(), out string value) || string.IsNullOrWhiteSpace(value))
                return null;
            return Path.IsPathRooted(value) ? value : Path.Combine(rootPath, value);
        }

        private GameStoragePathSet CreatePathSetFromCandidate(GameStoragePathSet currentPaths, GameStorageCandidate candidate)
        {
            bool linkRequired = candidate.LinkFolderRequired || IsLinkFolderRequired(candidate.VirtualInstallPath, currentPaths.GameInstallPath);
            return new GameStoragePathSet
            {
                GameId = currentPaths.GameId,
                GameName = currentPaths.GameName,
                GameInstallPath = currentPaths.GameInstallPath,
                InstallInfoPath = candidate.InstallInfoPath,
                ModsPath = candidate.ModsPath,
                VirtualInstallPath = candidate.VirtualInstallPath,
                LinkFolderPath = candidate.LinkFolderPath,
                LinkFolderRequired = linkRequired
            };
        }

        private GameStorageRegistry LoadLastKnownGoodRegistry()
        {
            try
            {
                if (File.Exists(LastKnownGoodPath))
                    return JsonConvert.DeserializeObject<GameStorageRegistry>(File.ReadAllText(LastKnownGoodPath)) ?? new GameStorageRegistry();
            }
            catch
            {
            }
            return new GameStorageRegistry();
        }

        private void AddCandidate(List<GameStorageCandidate> candidates, GameStorageCandidate candidate)
        {
            if (candidate == null)
                return;

            string key = string.Join("|", candidate.GameId, candidate.StorageId, candidate.InstallInfoPath, candidate.ModsPath, candidate.VirtualInstallPath, candidate.LinkFolderPath);
            var existing = candidates.FirstOrDefault(x => string.Equals(string.Join("|", x.GameId, x.StorageId, x.InstallInfoPath, x.ModsPath, x.VirtualInstallPath, x.LinkFolderPath), key, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                candidates.Add(candidate);
            else if (candidate.ConfidenceScore > existing.ConfidenceScore)
            {
                candidates.Remove(existing);
                candidates.Add(candidate);
            }
        }

        private void AddPathWarnings(GameStorageCandidate candidate)
        {
            if (candidate == null)
                return;
            if (string.IsNullOrWhiteSpace(candidate.InstallInfoPath) || !Directory.Exists(candidate.InstallInfoPath))
                candidate.Warnings.Add("InstallInfo folder is missing.");
            if (string.IsNullOrWhiteSpace(candidate.ModsPath) || !Directory.Exists(candidate.ModsPath))
                candidate.Warnings.Add("Mods folder is missing.");
            if (string.IsNullOrWhiteSpace(candidate.VirtualInstallPath) || !Directory.Exists(candidate.VirtualInstallPath))
                candidate.Warnings.Add("VirtualInstall folder is missing.");
            if (candidate.LinkFolderRequired && (string.IsNullOrWhiteSpace(candidate.LinkFolderPath) || !Directory.Exists(candidate.LinkFolderPath)))
                candidate.Warnings.Add("Required Link Folder is missing.");
        }
    }
}
