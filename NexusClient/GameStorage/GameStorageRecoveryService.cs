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
            AddCandidate(candidates, CreateCandidateFromPathSet("Proposed setup", currentPaths, null, currentScore, GetConfidenceLevel(currentScore), true,
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

            return NormalizeAndMergeCandidates(currentPaths, candidates)
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
            return NormalizeAndMergeCandidates(currentPaths, candidates)
                .OrderByDescending(x => x.ConfidenceScore)
                .ThenBy(x => x.CandidateKind)
                .ToList();
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

		public bool ApplyInitialSetupCandidate(
			GameStoragePathSet currentPaths,
			GameStorageCandidate candidate,
			out GameStorageHealthCheck healthCheck)
		{
			healthCheck = null;

			if (candidate == null ||
				!string.Equals(
					candidate.GameId,
					currentPaths.GameId,
					StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			var paths = CreatePathSetFromCandidate(
				currentPaths,
				candidate);

			CreateSetupFolders(paths);

			var registry = LoadRegistry();

			TryRepairLegacyVirtualInstallManifestCollision(
				paths,
				registry);

			string storageId =
				string.IsNullOrWhiteSpace(candidate.StorageId)
					? ResolveStorageId(paths, registry)
					: candidate.StorageId;

			healthCheck = Validate(
				paths,
				storageId,
				registry);

			if (!healthCheck.IsHealthy)
				return false;

			InitializeMetadata(
				paths,
				storageId,
				registry);

			ApplyPathSet(paths);

			healthCheck = Validate(
				paths,
				storageId,
				registry);

			healthCheck.StorageId = storageId;
			return healthCheck.IsHealthy;
		}

		private void ApplyPathSet(GameStoragePathSet paths)
		{
			string gameId = paths.GameId;

			_environmentInfo.Settings.InstallInfoFolder[gameId] =
				paths.InstallInfoPath;

			_environmentInfo.Settings.ModFolder[gameId] =
				paths.ModsPath;

			// VirtualModActivator expects the parent directory and appends
			// "VirtualInstall" itself.
			_environmentInfo.Settings.VirtualFolder[gameId] =
				GetVirtualFolderSettingPath(paths.VirtualInstallPath);

			_environmentInfo.Settings.HDLinkFolder[gameId] =
				paths.LinkFolderRequired
					? paths.LinkFolderPath
					: null;

			_environmentInfo.Settings.MultiHDInstall[gameId] =
				paths.LinkFolderRequired;

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

		public bool ValidateRecoveryCandidate(
			GameStoragePathSet currentPaths,
			GameStorageCandidate candidate,
			out GameStorageHealthCheck healthCheck)
		{
			healthCheck = null;

			if (candidate == null ||
				!string.Equals(
					candidate.GameId,
					currentPaths.GameId,
					StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			var paths = CreatePathSetFromCandidate(
				currentPaths,
				candidate);

			var registry = LoadRegistry();

			TryRepairLegacyVirtualInstallManifestCollision(
				paths,
				registry);

			string storageId =
				string.IsNullOrWhiteSpace(candidate.StorageId)
					? ResolveStorageId(paths, registry)
					: candidate.StorageId;

			healthCheck = Validate(
				paths,
				storageId,
				registry);

			if (!CanRecoverExistingStorage(healthCheck))
				return false;

			if (paths.LinkFolderRequired &&
				(string.IsNullOrWhiteSpace(paths.LinkFolderPath) ||
				 !IsLinkFolderOnGameDrive(
					 paths.LinkFolderPath,
					 paths.GameInstallPath)))
			{
				return false;
			}

			bool virtualInstallWasMissing =
				!Directory.Exists(paths.VirtualInstallPath);

			try
			{
				CreateRecoveryFolders(paths);
			}
			catch (UnauthorizedAccessException ex)
			{
				AddWriteFailure(healthCheck, paths, ex);
				return false;
			}
			catch (IOException ex)
			{
				AddWriteFailure(healthCheck, paths, ex);
				return false;
			}

			TryRepairLegacyVirtualInstallManifestCollision(
				paths,
				registry);

			healthCheck = Validate(
				paths,
				storageId,
				registry);

			if (!CanFinalizeRecoveredStorage(
				healthCheck,
				virtualInstallWasMissing))
			{
				return false;
			}

			if (!TryInitializeMetadata(
				paths,
				storageId,
				registry,
				healthCheck))
			{
				return false;
			}

			healthCheck = Validate(
				paths,
				storageId,
				registry);

			healthCheck.StorageId = storageId;
			return healthCheck.IsHealthy;
		}

		private void CreateRecoveryFolders(GameStoragePathSet paths)
		{
			CreateFolder(paths.VirtualInstallPath);

			if (paths.LinkFolderRequired)
				CreateFolder(paths.LinkFolderPath);
		}

		private bool CanRecoverExistingStorage(GameStorageHealthCheck healthCheck)
		{
			if (healthCheck == null)
				return false;

			foreach (var item in healthCheck.Items)
			{
				if (item.Role == GameStorageFolderRole.InstallInfo ||
					item.Role == GameStorageFolderRole.Mods)
				{
					if (item.Status != GameStorageHealthStatus.Healthy &&
						item.Status != GameStorageHealthStatus.LegacyValidNeedsInitialization)
					{
						return false;
					}
				}

				if (item.Role == GameStorageFolderRole.VirtualInstall &&
					item.Status != GameStorageHealthStatus.Healthy &&
					item.Status != GameStorageHealthStatus.LegacyValidNeedsInitialization &&
					item.Status != GameStorageHealthStatus.MissingVirtualInstall)
				{
					return false;
				}

				if (item.Role == GameStorageFolderRole.LinkFolder &&
					item.Status != GameStorageHealthStatus.Healthy &&
					item.Status != GameStorageHealthStatus.LegacyValidNeedsInitialization &&
					item.Status != GameStorageHealthStatus.MissingLinkFolder &&
					item.Status != GameStorageHealthStatus.LinkFolderNotRequired)
				{
					return false;
				}
			}

			return true;
		}

		private bool CanFinalizeRecoveredStorage(
			GameStorageHealthCheck healthCheck,
			bool virtualInstallWasMissing)
		{
			if (healthCheck == null)
				return false;

			foreach (var item in healthCheck.Items)
			{
				if (item.Status == GameStorageHealthStatus.Healthy ||
					item.Status == GameStorageHealthStatus.LegacyValidNeedsInitialization ||
					item.Status == GameStorageHealthStatus.LinkFolderNotRequired)
				{
					continue;
				}

				if (virtualInstallWasMissing &&
					item.Role == GameStorageFolderRole.VirtualInstall &&
					item.Status == GameStorageHealthStatus.SuspiciousEmptyFolder)
				{
					continue;
				}

				return false;
			}

			return true;
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
				VirtualInstallPath = NormalizeVirtualInstallDirectory(entry.VirtualInstallPath),
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
					VirtualInstallPath = NormalizeVirtualInstallDirectory(ResolveManifestFolder(rootPath, manifest,	GameStorageFolderRole.VirtualInstall)),
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
						case GameStorageFolderRole.VirtualInstall: candidate.VirtualInstallPath = NormalizeVirtualInstallDirectory(item.Item1); break;
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
			string installInfoPath = SelectExistingPath(
				Path.Combine(rootPath, "Install Info"),
				Path.Combine(rootPath, "InstallInfo"),
				Path.Combine(rootPath, "instinfo"));
			if (string.IsNullOrWhiteSpace(installInfoPath) || !File.Exists(Path.Combine(installInfoPath, "InstallLog.xml")))
                return;

			string modsPath = SelectExistingPath(Path.Combine(rootPath, "Mods"));

			// Traditional NMM configurations frequently stored archives directly
			// in the selected root rather than inside a "Mods" child directory.
			if (string.IsNullOrWhiteSpace(modsPath) &&
				CountModArchives(rootPath) > 0)
			{
				modsPath = rootPath;
			}
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
                if (ReadFolderManifest(folder) != null || !File.Exists(Path.Combine(folder, "InstallLog.xml")) || HasCandidateInstallInfo(candidates, folder))
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

        private bool HasCandidateInstallInfo(IEnumerable<GameStorageCandidate> candidates, string installInfoPath)
        {
            return candidates.Any(x => string.Equals(x.InstallInfoPath, installInfoPath, StringComparison.OrdinalIgnoreCase));
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

		private GameStoragePathSet CreatePathSetFromCandidate(
			GameStoragePathSet currentPaths,
			GameStorageCandidate candidate)
		{
			string virtualInstallPath =
				NormalizeVirtualInstallDirectory(
					candidate.VirtualInstallPath);

			bool linkRequired =
				candidate.LinkFolderRequired ||
				IsLinkFolderRequired(
					virtualInstallPath,
					currentPaths.GameInstallPath);

			string linkFolderPath = ResolveLinkFolderPath(
				currentPaths,
				candidate.LinkFolderPath,
				linkRequired);

			return new GameStoragePathSet
			{
				GameId = currentPaths.GameId,
				GameName = currentPaths.GameName,
				GameInstallPath = currentPaths.GameInstallPath,
				InstallInfoPath = NormalizeDirectoryPath(candidate.InstallInfoPath),
				ModsPath = NormalizeDirectoryPath(candidate.ModsPath),
				VirtualInstallPath = virtualInstallPath,
				LinkFolderPath = linkFolderPath,
				LinkFolderRequired = linkRequired
			};
		}

		private string ResolveLinkFolderPath(
			GameStoragePathSet currentPaths,
			string candidateLinkFolderPath,
			bool linkRequired)
		{
			if (!linkRequired)
				return null;

			if (!string.IsNullOrWhiteSpace(candidateLinkFolderPath))
				return NormalizeDirectoryPath(candidateLinkFolderPath);

			if (!string.IsNullOrWhiteSpace(currentPaths.LinkFolderPath) &&
				IsLinkFolderOnGameDrive(
					currentPaths.LinkFolderPath,
					currentPaths.GameInstallPath))
			{
				return NormalizeDirectoryPath(currentPaths.LinkFolderPath);
			}

			string gameDriveRoot = GetPathRoot(currentPaths.GameInstallPath);
			if (string.IsNullOrWhiteSpace(gameDriveRoot))
				return null;

			return NormalizeDirectoryPath(
				Path.Combine(
					gameDriveRoot,
					"Games",
					"NMMCE",
					currentPaths.GameId,
					"LinkFolder"));
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

        private List<GameStorageCandidate> NormalizeAndMergeCandidates(
            GameStoragePathSet currentPaths,
            IEnumerable<GameStorageCandidate> candidates)
        {
            var merged = new List<GameStorageCandidate>();

            foreach (var candidate in candidates.Where(x => x != null))
            {
                CompleteCandidatePaths(currentPaths, candidate);

                var existing = merged.FirstOrDefault(x =>
                    CandidatesRepresentSameStorage(x, candidate));

                if (existing == null)
                {
                    merged.Add(candidate);
                    continue;
                }

                GameStorageCandidate preferred = SelectPreferredCandidate(
                    existing,
                    candidate);
                GameStorageCandidate other =
                    ReferenceEquals(preferred, existing)
                        ? candidate
                        : existing;

                MergeCandidateMetadata(preferred, other);

                if (!ReferenceEquals(preferred, existing))
                {
                    int index = merged.IndexOf(existing);
                    merged[index] = preferred;
                }
            }

            return merged;
        }

        private void CompleteCandidatePaths(
            GameStoragePathSet currentPaths,
            GameStorageCandidate candidate)
        {
            candidate.GameId = string.IsNullOrWhiteSpace(candidate.GameId)
                ? currentPaths.GameId
                : candidate.GameId;
            candidate.InstallInfoPath = NormalizeDirectoryPath(
                candidate.InstallInfoPath);
            candidate.ModsPath = NormalizeDirectoryPath(
                candidate.ModsPath);
            candidate.VirtualInstallPath = NormalizeVirtualInstallDirectory(
                candidate.VirtualInstallPath);

            candidate.LinkFolderRequired =
                candidate.LinkFolderRequired ||
                IsLinkFolderRequired(
                    candidate.VirtualInstallPath,
                    currentPaths.GameInstallPath);

            candidate.LinkFolderPath = ResolveLinkFolderPath(
                currentPaths,
                candidate.LinkFolderPath,
                candidate.LinkFolderRequired);

            if (string.IsNullOrWhiteSpace(candidate.CandidateRoot))
            {
                candidate.CandidateRoot = TryGetSharedStorageRoot(
                    CreatePathSetFromCandidate(currentPaths, candidate));
            }
            else
            {
                candidate.CandidateRoot = NormalizeDirectoryPath(
                    candidate.CandidateRoot);
            }

            RemoveGeneratedPathWarnings(candidate);
            AddPathWarnings(candidate);
        }

        private bool CandidatesRepresentSameStorage(
            GameStorageCandidate left,
            GameStorageCandidate right)
        {
            if (!string.Equals(
                left.GameId,
                right.GameId,
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(left.StorageId) &&
                !string.IsNullOrWhiteSpace(right.StorageId) &&
                !string.Equals(
                    left.StorageId,
                    right.StorageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return CandidatePathsEqual(
                    left.InstallInfoPath,
                    right.InstallInfoPath) &&
                CandidatePathsEqual(
                    left.ModsPath,
                    right.ModsPath) &&
                CandidatePathsEqual(
                    left.VirtualInstallPath,
                    right.VirtualInstallPath) &&
                CandidatePathsEqual(
                    left.LinkFolderPath,
                    right.LinkFolderPath) &&
                left.LinkFolderRequired == right.LinkFolderRequired;
        }

        private bool CandidatePathsEqual(string left, string right)
        {
            string normalizedLeft = NormalizeDirectoryPath(left) ?? string.Empty;
            string normalizedRight = NormalizeDirectoryPath(right) ?? string.Empty;

            return string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.OrdinalIgnoreCase);
        }

        private GameStorageCandidate SelectPreferredCandidate(
            GameStorageCandidate left,
            GameStorageCandidate right)
        {
            if (right.ConfidenceScore > left.ConfidenceScore)
                return right;
            if (left.ConfidenceScore > right.ConfidenceScore)
                return left;

            bool leftIsProposed = string.Equals(
                left.CandidateKind,
                "Proposed setup",
                StringComparison.OrdinalIgnoreCase);
            bool rightIsProposed = string.Equals(
                right.CandidateKind,
                "Proposed setup",
                StringComparison.OrdinalIgnoreCase);

            if (leftIsProposed != rightIsProposed)
                return leftIsProposed ? right : left;

            return left;
        }

        private void MergeCandidateMetadata(
            GameStorageCandidate target,
            GameStorageCandidate source)
        {
            if (string.IsNullOrWhiteSpace(target.StorageId))
                target.StorageId = source.StorageId;
            if (string.IsNullOrWhiteSpace(target.CandidateRoot))
                target.CandidateRoot = source.CandidateRoot;

            target.LinkFolderRequired =
                target.LinkFolderRequired || source.LinkFolderRequired;
            target.RequiresUserConfirmation =
                target.RequiresUserConfirmation || source.RequiresUserConfirmation;

            foreach (string evidence in source.Evidence)
            {
                if (!target.Evidence.Contains(
                    evidence,
                    StringComparer.OrdinalIgnoreCase))
                {
                    target.Evidence.Add(evidence);
                }
            }

            foreach (string warning in source.Warnings)
            {
                if (!target.Warnings.Contains(
                    warning,
                    StringComparer.OrdinalIgnoreCase))
                {
                    target.Warnings.Add(warning);
                }
            }
        }

        private void RemoveGeneratedPathWarnings(GameStorageCandidate candidate)
        {
            string[] generatedWarnings =
            {
                "InstallInfo folder is missing.",
                "Mods folder is missing.",
                "VirtualInstall folder is missing.",
                "Required Link Folder is missing."
            };

            candidate.Warnings.RemoveAll(x => generatedWarnings.Contains(
                x,
                StringComparer.OrdinalIgnoreCase));
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
