using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;

namespace Nexus.Client.GameStorage
{
    public partial class GameStorageService
    {
        private readonly IEnvironmentInfo _environmentInfo;
        private readonly string _registryDirectory;
        private readonly Version _applicationVersion;
        private readonly JsonSerializerSettings _jsonSettings = new JsonSerializerSettings { Formatting = Formatting.Indented };

        public GameStorageService(IEnvironmentInfo environmentInfo)
        {
            _environmentInfo = environmentInfo;
            _registryDirectory = Path.Combine(environmentInfo.ApplicationPersonalDataFolderPath, "Game Storage");
            _applicationVersion = environmentInfo.ApplicationVersion;
        }

        public GameStorageService(string registryDirectory, Version applicationVersion)
        {
            _registryDirectory = registryDirectory;
            _applicationVersion = applicationVersion ?? new Version(0, 0);
        }

        public string RegistryDirectory => _registryDirectory;
        public string RegistryPath => Path.Combine(RegistryDirectory, GameStorageConstants.RegistryFileName);
        public string LastKnownGoodPath => Path.Combine(RegistryDirectory, GameStorageConstants.LastKnownGoodFileName);
        public string BackupDirectory => Path.Combine(RegistryDirectory, "Backups");
		public GameStoragePathSet FromGameMode(IGameMode gameMode)
		{
			string gameId = gameMode.ModeId;
			string linkFolder = GetSettingValue(_environmentInfo.Settings.HDLinkFolder, gameId);
			bool multiHd = GetBoolSettingValue(_environmentInfo.Settings.MultiHDInstall, gameId);

			// VirtualFolder historically stores the parent of the actual
			// VirtualInstall directory. VirtualModActivator appends
			// "VirtualInstall" to this setting.
			string virtualFolderSetting =
				GetSettingValue(_environmentInfo.Settings.VirtualFolder, gameId);

			string virtualInstallPath =
				NormalizeVirtualInstallDirectory(virtualFolderSetting);

			return new GameStoragePathSet
			{
				GameId = gameId,
				GameName = gameMode.Name,
				GameInstallPath = gameMode.GameModeEnvironmentInfo.InstallationPath,
				InstallInfoPath = gameMode.GameModeEnvironmentInfo.InstallInfoDirectory,
				ModsPath = gameMode.GameModeEnvironmentInfo.ModDirectory,
				VirtualInstallPath = virtualInstallPath,
				LinkFolderPath = linkFolder,
				LinkFolderRequired =
					multiHd ||
					IsLinkFolderRequired(
						virtualInstallPath,
						gameMode.GameModeEnvironmentInfo.InstallationPath),
				CompatibleSharedModsGameIds =
					GameModeStorageSharingRegistry.GetMutuallyCompatibleModsStorageModeIds(gameId)
			};
		}

		/// <summary>
		/// Converts either a legacy VirtualFolder root or an already resolved
		/// VirtualInstall directory into the actual VirtualInstall directory.
		///
		/// Examples:
		/// C:\NMM\Skyrim
		///     becomes C:\NMM\Skyrim\VirtualInstall
		///
		/// C:\NMM\Skyrim\VirtualInstall
		///     remains C:\NMM\Skyrim\VirtualInstall
		/// </summary>
		public string NormalizeVirtualInstallDirectory(string pathOrRoot)
		{
			if (string.IsNullOrWhiteSpace(pathOrRoot))
				return null;

			string normalizedPath = NormalizeDirectoryPath(pathOrRoot);

			if (string.Equals(
				Path.GetFileName(normalizedPath),
				GameStorageConstants.VirtualInstallDirectoryName,
				StringComparison.OrdinalIgnoreCase))
			{
				return normalizedPath;
			}

			return Path.Combine(
				normalizedPath,
				GameStorageConstants.VirtualInstallDirectoryName);
		}

		/// <summary>
		/// Converts the actual VirtualInstall directory back into the legacy
		/// VirtualFolder setting expected by VirtualModActivator.
		/// </summary>
		private string GetVirtualFolderSettingPath(string virtualInstallPath)
		{
			if (string.IsNullOrWhiteSpace(virtualInstallPath))
				return null;

			string normalizedPath = NormalizeDirectoryPath(virtualInstallPath);

			if (!string.Equals(
				Path.GetFileName(normalizedPath),
				GameStorageConstants.VirtualInstallDirectoryName,
				StringComparison.OrdinalIgnoreCase))
			{
				// Defensive fallback. The legacy runtime will append
				// VirtualInstall to this value.
				return normalizedPath;
			}

			string parentPath = Path.GetDirectoryName(normalizedPath);
			return string.IsNullOrWhiteSpace(parentPath)
				? normalizedPath
				: parentPath;
		}

		private string NormalizeDirectoryPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				return null;

			try
			{
				string fullPath = Path.GetFullPath(path);
				string root = Path.GetPathRoot(fullPath);

				if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
					return fullPath;

				return fullPath.TrimEnd(
					Path.DirectorySeparatorChar,
					Path.AltDirectorySeparatorChar);
			}
			catch
			{
				return path.TrimEnd(
					Path.DirectorySeparatorChar,
					Path.AltDirectorySeparatorChar);
			}
		}

		private bool AreSamePaths(string left, string right)
		{
			if (string.IsNullOrWhiteSpace(left) ||
				string.IsNullOrWhiteSpace(right))
			{
				return false;
			}

			return string.Equals(
				NormalizeDirectoryPath(left),
				NormalizeDirectoryPath(right),
				StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Repairs the specific 0.91.0 migration issue where the legacy
		/// VirtualFolder root was treated as the actual VirtualInstall folder.
		///
		/// Broken layout:
		///
		/// ModsPath:
		///     C:\...\MODdatas
		///     .nmm-folder.json says VirtualInstall
		///
		/// Actual VirtualInstall:
		///     C:\...\MODdatas\VirtualInstall
		///     no .nmm-folder.json
		///
		/// The repair is intentionally conservative and only runs when the
		/// complete known corruption pattern is present.
		/// </summary>
		private bool TryRepairLegacyVirtualInstallManifestCollision(
			GameStoragePathSet paths,
			GameStorageRegistry registry)
		{
			if (paths == null ||
				registry == null ||
				string.IsNullOrWhiteSpace(paths.ModsPath) ||
				string.IsNullOrWhiteSpace(paths.InstallInfoPath) ||
				string.IsNullOrWhiteSpace(paths.VirtualInstallPath))
			{
				return false;
			}

			if (!Directory.Exists(paths.ModsPath) ||
				!Directory.Exists(paths.InstallInfoPath) ||
				!Directory.Exists(paths.VirtualInstallPath))
			{
				return false;
			}

			string expectedVirtualInstallPath = Path.Combine(
				NormalizeDirectoryPath(paths.ModsPath),
				GameStorageConstants.VirtualInstallDirectoryName);

			// Only repair the known layout where VirtualInstall is the direct
			// child of the Mods/archive root.
			if (!AreSamePaths(
				expectedVirtualInstallPath,
				paths.VirtualInstallPath))
			{
				return false;
			}

			GameStorageFolderManifest misplacedManifest =
				ReadFolderManifest(paths.ModsPath);

			if (misplacedManifest == null ||
				misplacedManifest.FolderRole !=
					GameStorageFolderRole.VirtualInstall ||
				!string.Equals(
					misplacedManifest.GameId,
					paths.GameId,
					StringComparison.OrdinalIgnoreCase) ||
				string.IsNullOrWhiteSpace(misplacedManifest.StorageId))
			{
				return false;
			}

			string actualVirtualManifestPath = Path.Combine(
				paths.VirtualInstallPath,
				GameStorageConstants.FolderManifestFileName);

			// Do not overwrite an existing manifest in the real folder. An
			// existing manifest could represent a different recovery problem.
			if (File.Exists(actualVirtualManifestPath))
				return false;

			GameStorageFolderManifest installInfoManifest =
				ReadFolderManifest(paths.InstallInfoPath);

			// Requiring the matching InstallInfo manifest makes this repair
			// specific to the metadata set generated by the broken migration.
			if (installInfoManifest == null ||
				installInfoManifest.FolderRole !=
					GameStorageFolderRole.InstallInfo ||
				!string.Equals(
					installInfoManifest.GameId,
					paths.GameId,
					StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(
					installInfoManifest.StorageId,
					misplacedManifest.StorageId,
					StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			try
			{
				// This overwrites the incorrect manifest in Mods with role Mods,
				// creates the missing manifest inside VirtualInstall, and updates
				// the registry and last-known-good metadata.
				InitializeMetadata(
					paths,
					misplacedManifest.StorageId,
					registry);

				return true;
			}
			catch (UnauthorizedAccessException)
			{
				return false;
			}
			catch (IOException)
			{
				return false;
			}
		}

        private List<GameStorageFolderBinding> GetManifestBindings(GameStorageFolderManifest manifest)
        {
            var bindings = new List<GameStorageFolderBinding>();
            if (manifest == null)
                return bindings;

            foreach (var binding in manifest.Bindings ?? new List<GameStorageFolderBinding>())
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.GameId) || string.IsNullOrWhiteSpace(binding.StorageId))
                    continue;

                if (!bindings.Any(x =>
                    string.Equals(x.GameId, binding.GameId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.StorageId, binding.StorageId, StringComparison.OrdinalIgnoreCase)))
                {
                    bindings.Add(binding);
                }
            }

            if (!string.IsNullOrWhiteSpace(manifest.GameId) &&
                !string.IsNullOrWhiteSpace(manifest.StorageId) &&
                !bindings.Any(x =>
                    string.Equals(x.GameId, manifest.GameId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.StorageId, manifest.StorageId, StringComparison.OrdinalIgnoreCase)))
            {
                bindings.Add(new GameStorageFolderBinding
                {
                    GameId = manifest.GameId,
                    StorageId = manifest.StorageId,
                    CreatedUtc = manifest.CreatedUtc,
                    LastSeenUtc = manifest.LastSeenUtc,
                    LastSeenByVersion = manifest.LastSeenByVersion
                });
            }

            return bindings;
        }

        private bool IsCompatibleSharedModsGame(GameStoragePathSet paths, string otherGameId)
        {
            return paths != null &&
                   !string.IsNullOrWhiteSpace(otherGameId) &&
                   paths.CompatibleSharedModsGameIds != null &&
                   paths.CompatibleSharedModsGameIds.Contains(otherGameId, StringComparer.OrdinalIgnoreCase);
        }

        private List<string> GetSharedModsGameIds(GameStoragePathSet paths, GameStorageFolderManifest manifest)
        {
            return GetManifestBindings(manifest)
                .Select(x => x.GameId)
                .Where(x => !string.Equals(x, paths.GameId, StringComparison.OrdinalIgnoreCase))
                .Where(x => IsCompatibleSharedModsGame(paths, x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string GetSharedModsDescription(IEnumerable<string> gameIds)
        {
            var labels = (gameIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(x => GameModeStorageSharingRegistry.GetGameModeName(x) + " (" + x + ")")
                .ToList();

            return labels.Count == 0
                ? null
                : "Shared Mods library currently used by: " + string.Join(", ", labels);
        }

		public GameStorageHealthCheck ValidateCurrentStorage(IGameMode gameMode, bool initializeIfValid)
        {
            return ValidateStorage(FromGameMode(gameMode), initializeIfValid);
        }

		public GameStorageHealthCheck ValidateStorage(
			GameStoragePathSet paths,
			bool initializeIfValid)
		{
			var registry = LoadRegistry();

			TryRepairLegacyVirtualInstallManifestCollision(
				paths,
				registry);

			string storageId = ResolveStorageId(paths, registry);
			var result = Validate(paths, storageId, registry);

			if (result.IsHealthy && initializeIfValid)
			{
				if (TryInitializeMetadata(paths, storageId, registry, result))
					result.StorageId = storageId;
			}

			return result;
		}

		public void InitializeMetadataForCurrentStorage(IGameMode gameMode)
        {
            InitializeMetadataForStorage(FromGameMode(gameMode));
        }

		public void InitializeMetadataForStorage(GameStoragePathSet paths)
		{
			var registry = LoadRegistry();

			TryRepairLegacyVirtualInstallManifestCollision(
				paths,
				registry);

			string storageId = ResolveStorageId(paths, registry);
			var result = Validate(paths, storageId, registry);

			if (result.IsHealthy)
				TryInitializeMetadata(paths, storageId, registry, result);
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
            foreach (var entry in registry.KnownStorages.Where(x =>
                GameModeStorageSharingRegistry.CanShareModsStorage(gameId, x.GameId) &&
                !string.IsNullOrWhiteSpace(x.ModsPath)))
            {
                var sharedGameIds = GetManifestBindings(ReadFolderManifest(entry.ModsPath))
                    .Select(x => x.GameId)
                    .Where(x => GameModeStorageSharingRegistry.CanShareModsStorage(gameId, x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (sharedGameIds.Count == 0)
                    sharedGameIds.Add(entry.GameId);

                candidates.Add(new GameStorageCandidate
                {
                    CandidateKind = "Shared Mods library",
                    CandidateRoot = entry.ModsPath,
                    GameId = gameId,
                    ModsPath = entry.ModsPath,
                    ConfidenceScore = entry.LastKnownGood ? 85 : 72,
                    ConfidenceLevel = entry.LastKnownGood ? GameStorageCandidateConfidence.High : GameStorageCandidateConfidence.Medium,
                    RequiresUserConfirmation = true,
                    IsSharedModsLibrary = true,
                    SharedModsGameIds = sharedGameIds,
                    SharedModsDescription = GetSharedModsDescription(sharedGameIds),
                    Evidence = { "Compatible shared Mods storage found in the Game Storage registry." }
                });
            }
            return candidates;
        }

        private GameStorageHealthCheck Validate(GameStoragePathSet paths, string storageId, GameStorageRegistry registry)
        {
            var result = new GameStorageHealthCheck { GameId = paths.GameId, StorageId = storageId };
            var lastKnownGood = registry.KnownStorages.FirstOrDefault(x =>
                string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.StorageId, storageId, StringComparison.OrdinalIgnoreCase) &&
                x.LastKnownGood)
                ?? registry.KnownStorages.FirstOrDefault(x =>
                    string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                    x.LastKnownGood);

            ValidateFolder(result, paths, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath, storageId, true);
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

            if (manifest.FolderRole != role)
            {
                Add(result, role, path, GameStorageHealthStatus.PartialMatch, required, true, $"The folder manifest role is {manifest.FolderRole}, but NMM expected {role}.", "Select the folder with the correct Game Storage role.");
                return;
            }

            var bindings = GetManifestBindings(manifest);
            var currentBinding = bindings.FirstOrDefault(x =>
                string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase));

            if (role == GameStorageFolderRole.Mods)
            {
                var unrelatedBindings = bindings.Where(x =>
                    !string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                    !IsCompatibleSharedModsGame(paths, x.GameId)).ToList();

                if (unrelatedBindings.Count > 0)
                {
                    Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true,
                        "The Mods folder is already bound to an unrelated Game Mode.",
                        "Select a different Mods folder or add reciprocal shareModsStorageWith declarations only for compatible Game Modes.");
                    return;
                }

                if (currentBinding != null)
                {
                    if (!string.Equals(currentBinding.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(result, role, path, GameStorageHealthStatus.MismatchedStorageId, required, true,
                            "The Mods folder binding for this Game Mode belongs to a different Game Storage.",
                            "Use folders from the same Game Storage or confirm a recovery candidate.");
                        return;
                    }

                    var sharedGameIds = GetSharedModsGameIds(paths, manifest);
                    string description = GetSharedModsDescription(sharedGameIds);
                    Add(result, role, path, GameStorageHealthStatus.Healthy, required, true,
                        string.IsNullOrWhiteSpace(description) ? "The Mods folder is valid." : description);
                    return;
                }

                var compatibleGameIds = GetSharedModsGameIds(paths, manifest);
                if (bindings.Count > 0 && compatibleGameIds.Count == bindings.Count)
                {
                    string description = GetSharedModsDescription(compatibleGameIds);
                    Add(result, role, path, GameStorageHealthStatus.CompatibleSharedModsLibrary, required, true,
                        string.IsNullOrWhiteSpace(description)
                            ? "This is a compatible shared Mods library."
                            : description,
                        "Confirm that this Game Mode should also use this Mods library.");
                    return;
                }

                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true,
                    "The Mods folder manifest belongs to another or unknown Game Mode.",
                    "Select the correct folder for this game.");
                return;
            }

            if (currentBinding == null)
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true, $"The {GetRoleName(role)} manifest belongs to another game.", "Select the correct folder for this game.");
                return;
            }

            if (!string.Equals(currentBinding.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedStorageId, required, true, $"The {GetRoleName(role)} manifest belongs to a different Game Storage.", "Use folders from the same Game Storage or confirm a recovery candidate.");
                return;
            }

            if (bindings.Any(x => !string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase)))
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true, $"The {GetRoleName(role)} folder cannot be shared between Game Modes.", "Select an exclusive folder for this Game Mode.");
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

        private bool TryInitializeMetadata(GameStoragePathSet paths, string storageId, GameStorageRegistry registry, GameStorageHealthCheck result)
        {
            try
            {
                InitializeMetadata(paths, storageId, registry);
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                AddWriteFailure(result, paths, ex);
                return false;
            }
            catch (IOException ex)
            {
                AddWriteFailure(result, paths, ex);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                AddWriteFailure(result, paths, ex);
                return false;
            }
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
            var bindings = role == GameStorageFolderRole.Mods
                ? GetManifestBindings(manifest)
                : new List<GameStorageFolderBinding>();

            if (role == GameStorageFolderRole.Mods && bindings.Any(x =>
                !string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                !IsCompatibleSharedModsGame(paths, x.GameId)))
            {
                throw new InvalidOperationException("The selected Mods folder is bound to an unrelated Game Mode.");
            }

            var binding = bindings.FirstOrDefault(x =>
                string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase));
            if (binding == null)
            {
                binding = new GameStorageFolderBinding
                {
                    GameId = paths.GameId,
                    StorageId = storageId,
                    CreatedUtc = now
                };
                bindings.Add(binding);
            }

            binding.StorageId = storageId;
            binding.LastSeenUtc = now;
            binding.LastSeenByVersion = _applicationVersion.ToString();

            manifest.SchemaVersion = 2;
            manifest.App = GameStorageConstants.ApplicationName;
            manifest.FolderRole = role;
            manifest.StorageId = storageId;
            manifest.GameId = paths.GameId;
            manifest.LastSeenUtc = now;
            manifest.LastSeenByVersion = _applicationVersion.ToString();
            manifest.Bindings = bindings;
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

            foreach (var path in new[] { paths.InstallInfoPath, paths.VirtualInstallPath, paths.LinkFolderPath, paths.ModsPath })
            {
                var manifest = ReadFolderManifest(path);
                var binding = GetManifestBindings(manifest).FirstOrDefault(x =>
                    string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(x.StorageId));
                if (binding != null)
                    return binding.StorageId;
            }

            return Guid.NewGuid().ToString("D");
        }

        public bool RemoveStorageBinding(string gameId, string storageId)
        {
            if (string.IsNullOrWhiteSpace(gameId) || string.IsNullOrWhiteSpace(storageId))
                return false;

            var registry = LoadRegistry();
            var entries = registry.KnownStorages.Where(x =>
                string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.StorageId, storageId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0)
                return false;

            foreach (var entry in entries)
            {
                RemoveFolderManifestBinding(entry.ModsPath, GameStorageFolderRole.Mods, gameId, storageId);
                RemoveFolderManifestBinding(entry.InstallInfoPath, GameStorageFolderRole.InstallInfo, gameId, storageId);
                RemoveFolderManifestBinding(entry.VirtualInstallPath, GameStorageFolderRole.VirtualInstall, gameId, storageId);
                RemoveFolderManifestBinding(entry.LinkFolderPath, GameStorageFolderRole.LinkFolder, gameId, storageId);

                if (!string.IsNullOrWhiteSpace(entry.StorageRootPath))
                {
                    string rootManifestPath = Path.Combine(entry.StorageRootPath, GameStorageConstants.RootManifestFileName);
                    try
                    {
                        if (File.Exists(rootManifestPath))
                        {
                            var rootManifest = JsonConvert.DeserializeObject<GameStorageRootManifest>(File.ReadAllText(rootManifestPath));
                            if (rootManifest != null &&
                                string.Equals(rootManifest.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(rootManifest.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(rootManifestPath);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            registry.KnownStorages.RemoveAll(x =>
                string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.StorageId, storageId, StringComparison.OrdinalIgnoreCase));

            string activeStorageId;
            if (registry.ActiveStorageByGame.TryGetValue(gameId, out activeStorageId) &&
                string.Equals(activeStorageId, storageId, StringComparison.OrdinalIgnoreCase))
            {
                registry.ActiveStorageByGame.Remove(gameId);
            }

            SaveRegistryWithBackup(registry);
            return true;
        }

        private void RemoveFolderManifestBinding(string folderPath, GameStorageFolderRole role, string gameId, string storageId)
        {
            var manifest = ReadFolderManifest(folderPath);
            if (manifest == null || manifest.FolderRole != role)
                return;

            var bindings = GetManifestBindings(manifest);
            int removed = bindings.RemoveAll(x =>
                string.Equals(x.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.StorageId, storageId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return;

            string manifestPath = Path.Combine(folderPath, GameStorageConstants.FolderManifestFileName);
            if (bindings.Count == 0)
            {
                try
                {
                    if (File.Exists(manifestPath))
                    {
                        File.SetAttributes(manifestPath, FileAttributes.Normal);
                        File.Delete(manifestPath);
                    }
                }
                catch
                {
                }
                return;
            }

            var primary = bindings.OrderByDescending(x => x.LastSeenUtc).First();
            manifest.SchemaVersion = 2;
            manifest.Bindings = bindings;
            manifest.GameId = primary.GameId;
            manifest.StorageId = primary.StorageId;
            manifest.CreatedUtc = primary.CreatedUtc;
            manifest.LastSeenUtc = primary.LastSeenUtc;
            manifest.LastSeenByVersion = primary.LastSeenByVersion;
            WriteJson(manifestPath, manifest);
            TryHideFile(manifestPath);
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
            FileAttributes? originalAttributes = null;
            if (File.Exists(path))
            {
                originalAttributes = File.GetAttributes(path);
                File.SetAttributes(path, originalAttributes.Value & ~FileAttributes.Hidden & ~FileAttributes.ReadOnly);
            }

            try
            {
                File.WriteAllText(path, JsonConvert.SerializeObject(value, _jsonSettings));
            }
            finally
            {
                if (originalAttributes.HasValue && File.Exists(path))
                    File.SetAttributes(path, originalAttributes.Value);
            }
        }

        private void AddWriteFailure(GameStorageHealthCheck result, GameStoragePathSet paths, Exception exception)
        {
            string failedPath = TryGetPathFromException(exception) ?? string.Empty;
            Add(result, ResolveFolderRole(paths, failedPath), failedPath, GameStorageHealthStatus.NotWritable, true, true,
                "NMM could not write Game Storage metadata to this folder.",
                exception.Message,
                "Check folder permissions or select a writable Game Storage folder.");
        }

        private GameStorageFolderRole? ResolveFolderRole(GameStoragePathSet paths, string failedPath)
        {
            if (string.IsNullOrWhiteSpace(failedPath))
                return null;

            if (IsSameOrChildPath(paths.InstallInfoPath, failedPath))
                return GameStorageFolderRole.InstallInfo;
            if (IsSameOrChildPath(paths.ModsPath, failedPath))
                return GameStorageFolderRole.Mods;
            if (IsSameOrChildPath(paths.VirtualInstallPath, failedPath))
                return GameStorageFolderRole.VirtualInstall;
            if (IsSameOrChildPath(paths.LinkFolderPath, failedPath))
                return GameStorageFolderRole.LinkFolder;
            return null;
        }

        private bool IsSameOrChildPath(string parentPath, string childPath)
        {
            if (string.IsNullOrWhiteSpace(parentPath) || string.IsNullOrWhiteSpace(childPath))
                return false;

            try
            {
                string parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string child = Path.GetFullPath(childPath);
                return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || string.Equals(parent.TrimEnd(Path.DirectorySeparatorChar), child.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string TryGetPathFromException(Exception exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
                return null;

            int start = exception.Message.IndexOf('\'');
            if (start < 0)
                return null;
            int end = exception.Message.IndexOf('\'', start + 1);
            return end > start ? exception.Message.Substring(start + 1, end - start - 1) : null;
        }

        private void Add(GameStorageHealthCheck result, GameStorageFolderRole? role, string path, GameStorageHealthStatus status, bool required, bool recoverable, string message, params string[] fixes)
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
			if (paths == null ||
				string.IsNullOrWhiteSpace(paths.InstallInfoPath) ||
				string.IsNullOrWhiteSpace(paths.ModsPath) ||
				string.IsNullOrWhiteSpace(paths.VirtualInstallPath))
			{
				return null;
			}

			var corePaths = new[]
			{
		NormalizeDirectoryPath(paths.InstallInfoPath),
		NormalizeDirectoryPath(paths.ModsPath),
		NormalizeDirectoryPath(paths.VirtualInstallPath)
	};

			string modsPath =
				NormalizeDirectoryPath(paths.ModsPath);

			// Traditional NMM layout:
			//
			// MODdatas\
			//     archives
			//     instinfo\
			//     VirtualInstall\
			//
			// In this layout, ModsPath is also the storage root.
			if (corePaths.All(
				path => IsSameOrChildPath(modsPath, path)))
			{
				return modsPath;
			}

			var parents = corePaths
				.Select(Path.GetDirectoryName)
				.Where(path => !string.IsNullOrWhiteSpace(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			return parents.Count == 1
				? parents[0]
				: null;
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