using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using Nexus.Client.Util.Localization;

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
                : LanguageManager.Format("GameStorage.SharedMods.CurrentlyUsedBy", "Shared Mods library currently used by: {0}", string.Join(", ", labels));
        }

		/// <summary>
		/// Returns whether a validation result matches the narrow legacy VirtualInstall
		/// manifest-collision signature. Healthy storage therefore avoids the extra
		/// manifest probe on every startup.
		/// </summary>
		public bool ShouldAttemptKnownLegacyStorageMetadataRepair(GameStorageHealthCheck healthCheck)
		{
			if (healthCheck == null)
				return false;

			return healthCheck.Items.Any(x =>
				x.Role == GameStorageFolderRole.Mods &&
				x.Status == GameStorageHealthStatus.PartialMatch) &&
			healthCheck.Items.Any(x =>
				x.Role == GameStorageFolderRole.VirtualInstall &&
				x.Status == GameStorageHealthStatus.LegacyValidNeedsInitialization);
		}

		/// <summary>
		/// Applies only the narrowly-scoped legacy VirtualInstall manifest repair to
		/// the current Game Storage. No general validation repair is performed.
		/// </summary>
		public bool RepairKnownLegacyStorageMetadata(IGameMode gameMode)
		{
			return gameMode != null && RepairKnownLegacyStorageMetadata(FromGameMode(gameMode));
		}

		/// <summary>
		/// Applies only the known legacy VirtualInstall manifest collision repair to
		/// the supplied path set. This is an explicit write operation.
		/// </summary>
		public bool RepairKnownLegacyStorageMetadata(GameStoragePathSet paths)
		{
			if (paths == null)
				return false;

			var registry = LoadRegistry();
			return TryRepairLegacyVirtualInstallManifestCollision(paths, registry);
		}

		/// <summary>
		/// Validates the current Game Storage without modifying folders, manifests,
		/// registry data, or settings.
		/// </summary>
		public GameStorageHealthCheck ValidateCurrentStorage(IGameMode gameMode)
		{
			return ValidateStorage(FromGameMode(gameMode));
		}

		/// <summary>
		/// Compatibility overload retained for callers compiled against the previous
		/// API. Validation is intentionally read-only regardless of initializeIfValid.
		/// Metadata initialization must be requested explicitly.
		/// </summary>
		public GameStorageHealthCheck ValidateCurrentStorage(IGameMode gameMode, bool initializeIfValid)
		{
			return ValidateCurrentStorage(gameMode);
		}

		/// <summary>
		/// Validates a Game Storage path set without performing repairs or writes.
		/// </summary>
		public GameStorageHealthCheck ValidateStorage(GameStoragePathSet paths)
		{
			var registry = LoadRegistry();
			string storageId = ResolveStorageId(paths, registry);
			return Validate(paths, storageId, registry);
		}

		/// <summary>
		/// Compatibility overload. The initializeIfValid argument no longer causes
		/// writes; use InitializeMetadataForStorage for an explicit metadata update.
		/// </summary>
		public GameStorageHealthCheck ValidateStorage(GameStoragePathSet paths, bool initializeIfValid)
		{
			return ValidateStorage(paths);
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
            var lastKnownGood = registry.KnownStorages
                .Where(x =>
                    string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.StorageId, storageId, StringComparison.OrdinalIgnoreCase) &&
                    x.LastKnownGood)
                .OrderByDescending(x => x.LastSeenUtc)
                .FirstOrDefault()
                ?? registry.KnownStorages
                    .Where(x =>
                        string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                        x.LastKnownGood)
                    .OrderByDescending(x => x.LastSeenUtc)
                    .FirstOrDefault();

            AddFolderRoleCollisionWarnings(result, paths);
            ValidateRootManifestMetadata(result, paths);
            ValidateFolder(result, paths, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath, storageId, true);
            ValidateFolder(result, paths, GameStorageFolderRole.Mods, paths.ModsPath, storageId, true);
            ValidateFolder(result, paths, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath, storageId, true);
            ValidateInstallLog(result, paths.InstallInfoPath);

            if (paths.LinkFolderRequired)
            {
                ValidateFolder(result, paths, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath, storageId, true);
                if (!string.IsNullOrWhiteSpace(paths.LinkFolderPath) && Directory.Exists(paths.LinkFolderPath) && !IsLinkFolderOnGameDrive(paths.LinkFolderPath, paths.GameInstallPath))
                {
                    Add(result, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath, GameStorageHealthStatus.LinkFolderOnWrongDrive, true, true,
                        LanguageManager.Get("GameStorage.Health.LinkFolderWrongDrive.Message", "The Link Folder must be on the same drive as the game because hardlinks cannot cross drives."),
                        LanguageManager.Get("GameStorage.Health.LinkFolderWrongDrive.Fix", "Select a Link Folder on the game drive or move the virtual install staging to the game drive."));
                }
            }
            else
            {
                Add(result, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath, GameStorageHealthStatus.LinkFolderNotRequired, false, true, LanguageManager.Get("GameStorage.Health.LinkFolderNotRequired.Message", "The Link Folder is not required for this game storage."));
            }

            AddSuspiciousEmptyWarnings(result, paths, lastKnownGood);
            return result;
        }

        /// <summary>
        /// Reports exact folder-role collisions without rejecting the selected paths.
        /// Nested legacy layouts remain valid; only two roles pointing to the exact
        /// same directory are considered a metadata collision.
        /// </summary>
        private void AddFolderRoleCollisionWarnings(GameStorageHealthCheck result, GameStoragePathSet paths)
        {
            foreach (var group in GetMetadataFolderAssignments(paths)
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .GroupBy(x => NormalizeDirectoryPath(x.Value), StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1))
            {
                string roles = string.Join(", ", group.Select(x => GetRoleName(x.Key)));
                foreach (var assignment in group)
                {
                    Add(result, assignment.Key, assignment.Value, GameStorageHealthStatus.FolderRoleCollision, true, true,
                        LanguageManager.Format("GameStorage.Health.FolderRoleCollision.Message", "This folder is assigned to multiple Game Storage roles ({0}). NMM will keep using the selected paths but will not write conflicting folder manifests.", roles),
                        LanguageManager.Get("GameStorage.Health.FolderRoleCollision.Fix", "Use separate folders for these roles when convenient."));
                }
            }
        }

        /// <summary>
        /// Reports unreadable or future-version root metadata while leaving the
        /// selected storage paths usable. Missing root metadata is valid for legacy
        /// configurations and is not reported as an error.
        /// </summary>
        private void ValidateRootManifestMetadata(GameStorageHealthCheck result, GameStoragePathSet paths)
        {
            string root = TryGetSharedStorageRoot(paths);
            if (string.IsNullOrWhiteSpace(root))
                return;

            GameStorageMetadataReadResult<GameStorageRootManifest> read = ReadRootManifestResult(root);
            if (read.Status == GameStorageMetadataReadStatus.Invalid)
            {
                Add(result, null, Path.Combine(root, GameStorageConstants.RootManifestFileName), GameStorageHealthStatus.InvalidManifest, false, true,
                    LanguageManager.Get("GameStorage.Health.InvalidRootManifest.Message", "The Game Storage root manifest is unreadable. Folder contents remain usable."),
                    LanguageManager.Get("GameStorage.Health.InvalidManifest.Fix", "Confirm the selected paths and repair the Game Storage metadata."));
            }
            else if (read.Status == GameStorageMetadataReadStatus.UnsupportedVersion)
            {
                Add(result, null, Path.Combine(root, GameStorageConstants.RootManifestFileName), GameStorageHealthStatus.UnsupportedManifestVersion, false, true,
                    LanguageManager.Get("GameStorage.Health.UnsupportedRootManifestVersion.Message", "The Game Storage root manifest uses a newer metadata format. NMM will leave it untouched."));
            }
        }

        private void ValidateFolder(GameStorageHealthCheck result, GameStoragePathSet paths, GameStorageFolderRole role, string path, string storageId, bool required)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Add(result, role, path, MissingStatus(role), required, true, LanguageManager.Format("GameStorage.Health.MissingFolder.Message", "The {0} folder is does not exist yet. It will be created when this setup is applied.", GetRoleName(role)), LanguageManager.Get("GameStorage.Health.MissingFolder.Fix", "Restore the previous folder or create a new one."));
                return;
            }

            GameStorageMetadataReadResult<GameStorageFolderManifest> manifestRead = ReadFolderManifestResult(path);
            if (manifestRead.Status == GameStorageMetadataReadStatus.Missing)
            {
                Add(result, role, path, GameStorageHealthStatus.LegacyValidNeedsInitialization, required, true, LanguageManager.Format("GameStorage.Health.LegacyNeedsManifest.Message", "The {0} folder is valid legacy storage and needs a Game Storage manifest.", GetRoleName(role)));
                return;
            }

            if (manifestRead.Status == GameStorageMetadataReadStatus.Invalid)
            {
                Add(result, role, path, GameStorageHealthStatus.InvalidManifest, required, true,
                    LanguageManager.Format("GameStorage.Health.InvalidManifest.Message", "The {0} folder contains an unreadable Game Storage manifest. The folder contents can still be used, but the metadata should be repaired.", GetRoleName(role)),
                    LanguageManager.Get("GameStorage.Health.InvalidManifest.Fix", "Confirm the selected paths and repair the Game Storage metadata."));
                return;
            }

            if (manifestRead.Status == GameStorageMetadataReadStatus.UnsupportedVersion)
            {
                Add(result, role, path, GameStorageHealthStatus.UnsupportedManifestVersion, required, true,
                    LanguageManager.Format("GameStorage.Health.UnsupportedManifestVersion.Message", "The {0} folder was written by a newer Game Storage metadata format. NMM will not overwrite that manifest automatically.", GetRoleName(role)),
                    LanguageManager.Get("GameStorage.Health.UnsupportedManifestVersion.Fix", "Continue using the selected folder, or update NMM before repairing its metadata."));
                return;
            }

            var manifest = manifestRead.Value;

            if (manifest.FolderRole != role)
            {
                Add(result, role, path, GameStorageHealthStatus.PartialMatch, required, true, LanguageManager.Format("GameStorage.Health.ManifestRoleMismatch.Message", "The folder manifest role is {0}, but NMM expected {1}.", GameStorageLocalization.GetFolderRoleName(manifest.FolderRole), GetRoleName(role)), LanguageManager.Get("GameStorage.Health.ManifestRoleMismatch.Fix", "Select the folder with the correct Game Storage role."));
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
                        LanguageManager.Get("GameStorage.Health.ModsBoundUnrelated.Message", "The Mods folder is already bound to an unrelated Game Mode."),
                        LanguageManager.Get("GameStorage.Health.ModsBoundUnrelated.Fix", "Select a different Mods folder or add reciprocal shareModsStorageWith declarations only for compatible Game Modes."));
                    return;
                }

                if (currentBinding != null)
                {
                    if (!string.Equals(currentBinding.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
                    {
                        Add(result, role, path, GameStorageHealthStatus.MismatchedStorageId, required, true,
                            LanguageManager.Get("GameStorage.Health.ModsStorageMismatch.Message", "The Mods folder binding for this Game Mode belongs to a different Game Storage."),
                            LanguageManager.Get("GameStorage.Health.StorageMismatch.Fix", "Use folders from the same Game Storage or confirm a recovery candidate."));
                        return;
                    }

                    var sharedGameIds = GetSharedModsGameIds(paths, manifest);
                    string description = GetSharedModsDescription(sharedGameIds);
                    Add(result, role, path, GameStorageHealthStatus.Healthy, required, true,
                        string.IsNullOrWhiteSpace(description) ? LanguageManager.Get("GameStorage.Health.ModsValid.Message", "The Mods folder is valid.") : description);
                    return;
                }

                var compatibleGameIds = GetSharedModsGameIds(paths, manifest);
                if (bindings.Count > 0 && compatibleGameIds.Count == bindings.Count)
                {
                    string description = GetSharedModsDescription(compatibleGameIds);
                    Add(result, role, path, GameStorageHealthStatus.CompatibleSharedModsLibrary, required, true,
                        string.IsNullOrWhiteSpace(description)
                            ? LanguageManager.Get("GameStorage.Health.SharedModsCompatible.Message", "This is a compatible shared Mods library.")
                            : description,
                        LanguageManager.Get("GameStorage.Health.SharedModsCompatible.Fix", "Confirm that this Game Mode should also use this Mods library."));
                    return;
                }

                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true,
                    LanguageManager.Get("GameStorage.Health.ModsManifestOtherGame.Message", "The Mods folder manifest belongs to another or unknown Game Mode."),
                    LanguageManager.Get("GameStorage.Health.SelectCorrectFolder.Fix", "Select the correct folder for this game."));
                return;
            }

            if (currentBinding == null)
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true, LanguageManager.Format("GameStorage.Health.ManifestOtherGame.Message", "The {0} manifest belongs to another game.", GetRoleName(role)), LanguageManager.Get("GameStorage.Health.SelectCorrectFolder.Fix", "Select the correct folder for this game."));
                return;
            }

            if (bindings.Any(x => !string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase)))
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedGame, required, true, LanguageManager.Format("GameStorage.Health.FolderCannotBeShared.Message", "The {0} folder cannot be shared between Game Modes.", GetRoleName(role)), LanguageManager.Get("GameStorage.Health.SelectExclusiveFolder.Fix", "Select an exclusive folder for this Game Mode."));
                return;
            }

            if (!string.Equals(currentBinding.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
            {
                Add(result, role, path, GameStorageHealthStatus.MismatchedStorageId, required, true, LanguageManager.Format("GameStorage.Health.ManifestStorageMismatch.Message", "The {0} manifest belongs to a different Game Storage.", GetRoleName(role)), LanguageManager.Get("GameStorage.Health.StorageMismatch.Fix", "Use folders from the same Game Storage or confirm a recovery candidate."));
                return;
            }

            Add(result, role, path, GameStorageHealthStatus.Healthy, required, true, LanguageManager.Format("GameStorage.Health.FolderValid.Message", "The {0} folder is valid.", GetRoleName(role)));
        }

        /// <summary>
        /// Warns about a missing InstallLog only when InstallInfo contains evidence
        /// of an existing setup. A completely empty/new InstallInfo directory stays
        /// valid so first-run configurations are not penalized.
        /// </summary>
        private void ValidateInstallLog(GameStorageHealthCheck result, string installInfoPath)
        {
            if (string.IsNullOrWhiteSpace(installInfoPath) || !Directory.Exists(installInfoPath))
                return;

            string installLog = Path.Combine(installInfoPath, "InstallLog.xml");
            if (File.Exists(installLog) || !HasInstallInfoEvidence(installInfoPath))
                return;

            Add(result, GameStorageFolderRole.InstallInfo, installInfoPath, GameStorageHealthStatus.MissingInstallLog, true, true,
                LanguageManager.Get("GameStorage.Health.MissingInstallLog.Message", "InstallInfo contains existing data but InstallLog.xml was not found."),
                LanguageManager.Get("GameStorage.Health.MissingInstallLog.Fix", "Restore the previous InstallInfo folder if this game already had installed mods."));
        }

        /// <summary>
        /// Returns whether InstallInfo contains data other than the Game Storage
        /// manifest itself.
        /// </summary>
        private bool HasInstallInfoEvidence(string installInfoPath)
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(installInfoPath)
                    .Any(x => !string.Equals(
                        Path.GetFileName(x),
                        GameStorageConstants.FolderManifestFileName,
                        StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        private void AddSuspiciousEmptyWarnings(GameStorageHealthCheck result, GameStoragePathSet paths, GameStorageRegistryEntry lastKnownGood)
        {
            if (lastKnownGood == null)
                return;

            if (lastKnownGood.LastKnownArchiveCount > 0 && Directory.Exists(paths.ModsPath) && CountModArchives(paths.ModsPath) == 0)
                Add(result, GameStorageFolderRole.Mods, paths.ModsPath, GameStorageHealthStatus.SuspiciousEmptyFolder, true, true, LanguageManager.Get("GameStorage.Health.EmptyMods.Message", "The Mods folder is empty, but the previous known-good folder contained mod archives."), LanguageManager.Format("GameStorage.Health.PreviousModsPath", "Previous Mods folder: {0}", lastKnownGood.ModsPath));

            if (lastKnownGood.LastKnownInstallLogPresent && Directory.Exists(paths.InstallInfoPath) && !File.Exists(Path.Combine(paths.InstallInfoPath, "InstallLog.xml")))
                Add(result, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath, GameStorageHealthStatus.SuspiciousEmptyFolder, true, true, LanguageManager.Get("GameStorage.Health.EmptyInstallInfo.Message", "The InstallInfo folder lacks InstallLog.xml, but the previous known-good storage had one."), LanguageManager.Format("GameStorage.Health.PreviousInstallInfoPath", "Previous InstallInfo folder: {0}", lastKnownGood.InstallInfoPath));

            if (lastKnownGood.LastKnownVirtualFileCount > 0 && Directory.Exists(paths.VirtualInstallPath) && CountVirtualInstallPayloadFiles(paths.VirtualInstallPath) == 0)
                Add(result, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath, GameStorageHealthStatus.SuspiciousEmptyFolder, true, true, LanguageManager.Get("GameStorage.Health.EmptyVirtualInstall.Message", "The VirtualInstall folder is empty, but the previous known-good folder contained staged files."), LanguageManager.Format("GameStorage.Health.PreviousVirtualInstallPath", "Previous VirtualInstall folder: {0}", lastKnownGood.VirtualInstallPath));
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
            WriteFolderManifests(paths, storageId, now);

            string root = TryGetSharedStorageRoot(paths);
            if (!string.IsNullOrWhiteSpace(root))
                WriteRootManifest(root, paths, storageId, now);

            UpsertRegistryEntry(registry, paths, storageId, root, now);
            SaveRegistryWithBackup(registry);
        }

        /// <summary>
        /// Returns the folder-role assignments that can carry Game Storage folder
        /// manifests for the supplied path set.
        /// </summary>
        private IEnumerable<KeyValuePair<GameStorageFolderRole, string>> GetMetadataFolderAssignments(GameStoragePathSet paths)
        {
            yield return new KeyValuePair<GameStorageFolderRole, string>(GameStorageFolderRole.InstallInfo, paths.InstallInfoPath);
            yield return new KeyValuePair<GameStorageFolderRole, string>(GameStorageFolderRole.Mods, paths.ModsPath);
            yield return new KeyValuePair<GameStorageFolderRole, string>(GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath);
            if (paths.LinkFolderRequired)
                yield return new KeyValuePair<GameStorageFolderRole, string>(GameStorageFolderRole.LinkFolder, paths.LinkFolderPath);
        }

        /// <summary>
        /// Writes folder manifests only where a directory has one unambiguous role.
        /// Existing configurations that intentionally reuse the same directory are
        /// left functional without repeatedly overwriting one manifest with another.
        /// </summary>
        private void WriteFolderManifests(GameStoragePathSet paths, string storageId, DateTime now)
        {
            var assignments = GetMetadataFolderAssignments(paths)
                .Where(x => !string.IsNullOrWhiteSpace(x.Value) && Directory.Exists(x.Value))
                .ToList();
            var collidingPaths = new HashSet<string>(
                assignments
                    .GroupBy(x => NormalizeDirectoryPath(x.Value), StringComparer.OrdinalIgnoreCase)
                    .Where(x => x.Count() > 1)
                    .Select(x => x.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (var assignment in assignments)
            {
                if (collidingPaths.Contains(NormalizeDirectoryPath(assignment.Value)))
                    continue;

                WriteFolderManifest(assignment.Value, assignment.Key, paths, storageId, now);
            }
        }

        private void WriteFolderManifest(string folderPath, GameStorageFolderRole role, GameStoragePathSet paths, string storageId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;

            string manifestPath = Path.Combine(folderPath, GameStorageConstants.FolderManifestFileName);
            GameStorageMetadataReadResult<GameStorageFolderManifest> existingRead = ReadFolderManifestResult(folderPath);
            if (existingRead.Status == GameStorageMetadataReadStatus.UnsupportedVersion)
                return;

            var existing = existingRead.Status == GameStorageMetadataReadStatus.Valid ? existingRead.Value : null;
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

            manifest.SchemaVersion = GameStorageConstants.FolderManifestSchemaVersion;
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
            GameStorageMetadataReadResult<GameStorageRootManifest> existingRead = ReadRootManifestResult(root);
            if (existingRead.Status == GameStorageMetadataReadStatus.UnsupportedVersion)
                return;

            GameStorageRootManifest existing = existingRead.Status == GameStorageMetadataReadStatus.Valid
                ? existingRead.Value
                : null;
            DateTime createdUtc = existing != null &&
                string.Equals(existing.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.StorageId, storageId, StringComparison.OrdinalIgnoreCase) &&
                existing.CreatedUtc != default(DateTime)
                    ? existing.CreatedUtc
                    : now;

            var manifest = new GameStorageRootManifest
            {
                SchemaVersion = GameStorageConstants.RootManifestSchemaVersion,
                StorageId = storageId,
                GameId = paths.GameId,
                GameName = paths.GameName,
                LinkFolderRequired = paths.LinkFolderRequired,
                CreatedUtc = createdUtc,
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

            foreach (var knownStorage in registry.KnownStorages.Where(x =>
                string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase)))
            {
                knownStorage.LastKnownGood = false;
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
            entry.LastKnownVirtualFileCount = CountVirtualInstallPayloadFiles(paths.VirtualInstallPath);
            registry.ActiveStorageByGame[paths.GameId] = storageId;
        }

        private string ResolveStorageId(GameStoragePathSet paths, GameStorageRegistry registry)
        {
            string manifestStorageId = ResolveConsistentFolderManifestStorageId(paths);
            if (!string.IsNullOrWhiteSpace(manifestStorageId))
                return manifestStorageId;

            string registryStorageId = FindExactRegistryStorageId(paths, registry);
            if (!string.IsNullOrWhiteSpace(registryStorageId))
                return registryStorageId;

            string rootManifestStorageId = ResolveMatchingRootManifestStorageId(paths);
            if (!string.IsNullOrWhiteSpace(rootManifestStorageId))
                return rootManifestStorageId;

            if (registry.ActiveStorageByGame.TryGetValue(paths.GameId, out string activeId) && !string.IsNullOrWhiteSpace(activeId))
                return activeId;

            return Guid.NewGuid().ToString("D");
        }

        /// <summary>
        /// Returns the Storage ID recorded by the selected folders only when all
        /// applicable same-game folder manifests agree on the same identity.
        /// </summary>
        private string ResolveConsistentFolderManifestStorageId(GameStoragePathSet paths)
        {
            var storageIds = new List<string>();
            AddFolderManifestStorageId(storageIds, paths, paths.InstallInfoPath, GameStorageFolderRole.InstallInfo);
            AddFolderManifestStorageId(storageIds, paths, paths.ModsPath, GameStorageFolderRole.Mods);
            AddFolderManifestStorageId(storageIds, paths, paths.VirtualInstallPath, GameStorageFolderRole.VirtualInstall);
            if (paths.LinkFolderRequired)
                AddFolderManifestStorageId(storageIds, paths, paths.LinkFolderPath, GameStorageFolderRole.LinkFolder);

            if (storageIds.Count == 0)
                return null;

            string storageId = storageIds[0];
            return storageIds.All(x => string.Equals(x, storageId, StringComparison.OrdinalIgnoreCase))
                ? storageId
                : null;
        }

        /// <summary>
        /// Adds the current Game Mode binding from a correctly-role-matched folder manifest.
        /// </summary>
        private void AddFolderManifestStorageId(
            ICollection<string> storageIds,
            GameStoragePathSet paths,
            string folderPath,
            GameStorageFolderRole expectedRole)
        {
            var manifest = ReadFolderManifest(folderPath);
            if (manifest == null || manifest.FolderRole != expectedRole)
                return;

            string storageId = GetManifestBindings(manifest)
                .Where(x => string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.StorageId)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            if (!string.IsNullOrWhiteSpace(storageId))
                storageIds.Add(storageId);
        }

        /// <summary>
        /// Returns the Storage ID of a registry entry whose recorded folders match
        /// the currently selected path set exactly.
        /// </summary>
        private string FindExactRegistryStorageId(GameStoragePathSet paths, GameStorageRegistry registry)
        {
            if (paths == null || registry == null)
                return null;

            var exactMatches = registry.KnownStorages.Where(x =>
                string.Equals(x.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase) &&
                AreSamePaths(x.InstallInfoPath, paths.InstallInfoPath) &&
                AreSamePaths(x.ModsPath, paths.ModsPath) &&
                AreSamePaths(NormalizeVirtualInstallDirectory(x.VirtualInstallPath), paths.VirtualInstallPath) &&
                (!paths.LinkFolderRequired || AreSamePaths(x.LinkFolderPath, paths.LinkFolderPath)))
                .ToList();

            if (registry.ActiveStorageByGame.TryGetValue(paths.GameId, out string activeId))
            {
                var activeExactMatch = exactMatches.FirstOrDefault(x =>
                    string.Equals(x.StorageId, activeId, StringComparison.OrdinalIgnoreCase));
                if (activeExactMatch != null)
                    return activeExactMatch.StorageId;
            }

            return exactMatches
                .Where(x => !string.IsNullOrWhiteSpace(x.StorageId))
                .OrderByDescending(x => x.LastKnownGood)
                .ThenByDescending(x => x.LastSeenUtc)
                .Select(x => x.StorageId)
                .FirstOrDefault();
        }

        /// <summary>
        /// Uses a root manifest only when it describes the current game and the
        /// exact folders currently selected by the user/settings.
        /// </summary>
        private string ResolveMatchingRootManifestStorageId(GameStoragePathSet paths)
        {
            string root = TryGetSharedStorageRoot(paths);
            if (string.IsNullOrWhiteSpace(root))
                return null;

            GameStorageRootManifest manifest = ReadRootManifest(root);
            if (manifest == null ||
                string.IsNullOrWhiteSpace(manifest.StorageId) ||
                !string.Equals(manifest.GameId, paths.GameId, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!RootManifestFolderMatches(root, manifest, GameStorageFolderRole.InstallInfo, paths.InstallInfoPath) ||
                !RootManifestFolderMatches(root, manifest, GameStorageFolderRole.Mods, paths.ModsPath) ||
                !RootManifestFolderMatches(root, manifest, GameStorageFolderRole.VirtualInstall, paths.VirtualInstallPath))
            {
                return null;
            }

            if (paths.LinkFolderRequired &&
                !RootManifestFolderMatches(root, manifest, GameStorageFolderRole.LinkFolder, paths.LinkFolderPath))
            {
                return null;
            }

            return manifest.StorageId;
        }

        /// <summary>
        /// Checks whether a root-manifest role resolves to the selected folder path.
        /// </summary>
        private bool RootManifestFolderMatches(
            string root,
            GameStorageRootManifest manifest,
            GameStorageFolderRole role,
            string expectedPath)
        {
            if (manifest.Folders == null ||
                !manifest.Folders.TryGetValue(role.ToString(), out string manifestPath) ||
                string.IsNullOrWhiteSpace(manifestPath))
            {
                return false;
            }

            string resolvedPath = Path.IsPathRooted(manifestPath)
                ? manifestPath
                : Path.Combine(root, manifestPath);
            return AreSamePaths(resolvedPath, expectedPath);
        }

        /// <summary>
        /// Reads and classifies a Game Storage root manifest without modifying it.
        /// </summary>
        private GameStorageMetadataReadResult<GameStorageRootManifest> ReadRootManifestResult(string root)
        {
            var result = new GameStorageMetadataReadResult<GameStorageRootManifest>();
            if (string.IsNullOrWhiteSpace(root))
            {
                result.Status = GameStorageMetadataReadStatus.Missing;
                return result;
            }

            string manifestPath = Path.Combine(root, GameStorageConstants.RootManifestFileName);
            if (!File.Exists(manifestPath))
            {
                result.Status = GameStorageMetadataReadStatus.Missing;
                return result;
            }

            try
            {
                result.Value = JsonConvert.DeserializeObject<GameStorageRootManifest>(File.ReadAllText(manifestPath));
                if (result.Value == null)
                {
                    result.Status = GameStorageMetadataReadStatus.Invalid;
                    return result;
                }

                result.Status = result.Value.SchemaVersion > GameStorageConstants.RootManifestSchemaVersion
                    ? GameStorageMetadataReadStatus.UnsupportedVersion
                    : GameStorageMetadataReadStatus.Valid;
                return result;
            }
            catch
            {
                result.Status = GameStorageMetadataReadStatus.Invalid;
                return result;
            }
        }

        /// <summary>
        /// Returns a root manifest only when its schema is supported by this NMM version.
        /// </summary>
        private GameStorageRootManifest ReadRootManifest(string root)
        {
            GameStorageMetadataReadResult<GameStorageRootManifest> result = ReadRootManifestResult(root);
            return result.Status == GameStorageMetadataReadStatus.Valid ? result.Value : null;
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
                        GameStorageMetadataReadResult<GameStorageRootManifest> rootRead = ReadRootManifestResult(entry.StorageRootPath);
                        GameStorageRootManifest rootManifest = rootRead.Status == GameStorageMetadataReadStatus.Valid
                            ? rootRead.Value
                            : null;
                        if (rootManifest != null &&
                            string.Equals(rootManifest.GameId, gameId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(rootManifest.StorageId, storageId, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(rootManifestPath);
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
            manifest.SchemaVersion = GameStorageConstants.FolderManifestSchemaVersion;
            manifest.Bindings = bindings;
            manifest.GameId = primary.GameId;
            manifest.StorageId = primary.StorageId;
            manifest.CreatedUtc = primary.CreatedUtc;
            manifest.LastSeenUtc = primary.LastSeenUtc;
            manifest.LastSeenByVersion = primary.LastSeenByVersion;
            WriteJson(manifestPath, manifest);
            TryHideFile(manifestPath);
        }

        /// <summary>
        /// Loads the primary registry, falling back to the last-known-good copy when
        /// the primary file is missing, corrupt, or written by an unsupported schema.
        /// </summary>
        private GameStorageRegistry LoadRegistry()
        {
            GameStorageMetadataReadResult<GameStorageRegistry> primary = ReadRegistryResult(RegistryPath);
            if (primary.Status == GameStorageMetadataReadStatus.Valid)
                return NormalizeRegistry(primary.Value);

            GameStorageMetadataReadResult<GameStorageRegistry> fallback = ReadRegistryResult(LastKnownGoodPath);
            if (fallback.Status == GameStorageMetadataReadStatus.Valid)
                return NormalizeRegistry(fallback.Value);

            return new GameStorageRegistry();
        }

        /// <summary>
        /// Reads and classifies one registry file without modifying it.
        /// </summary>
        private GameStorageMetadataReadResult<GameStorageRegistry> ReadRegistryResult(string path)
        {
            var result = new GameStorageMetadataReadResult<GameStorageRegistry>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                result.Status = GameStorageMetadataReadStatus.Missing;
                return result;
            }

            try
            {
                result.Value = JsonConvert.DeserializeObject<GameStorageRegistry>(File.ReadAllText(path));
                if (result.Value == null)
                {
                    result.Status = GameStorageMetadataReadStatus.Invalid;
                    return result;
                }

                result.Status = result.Value.SchemaVersion > GameStorageConstants.RegistrySchemaVersion
                    ? GameStorageMetadataReadStatus.UnsupportedVersion
                    : GameStorageMetadataReadStatus.Valid;
                return result;
            }
            catch
            {
                result.Status = GameStorageMetadataReadStatus.Invalid;
                return result;
            }
        }

        /// <summary>
        /// Restores non-null registry collections for legacy or partially-written
        /// registry payloads without changing their storage entries.
        /// </summary>
        private GameStorageRegistry NormalizeRegistry(GameStorageRegistry registry)
        {
            registry = registry ?? new GameStorageRegistry();
            registry.ActiveStorageByGame = registry.ActiveStorageByGame ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            registry.KnownStorages = registry.KnownStorages ?? new List<GameStorageRegistryEntry>();

            foreach (var group in registry.KnownStorages
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.GameId) && x.LastKnownGood)
                .GroupBy(x => x.GameId, StringComparer.OrdinalIgnoreCase)
                .Where(x => x.Count() > 1))
            {
                string activeStorageId;
                registry.ActiveStorageByGame.TryGetValue(group.Key, out activeStorageId);
                GameStorageRegistryEntry winner = group
                    .OrderByDescending(x => string.Equals(x.StorageId, activeStorageId, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.LastSeenUtc)
                    .First();

                foreach (var entry in group)
                    entry.LastKnownGood = ReferenceEquals(entry, winner);
            }

            return registry;
        }

        private void SaveRegistryWithBackup(GameStorageRegistry registry)
        {
            GameStorageMetadataReadResult<GameStorageRegistry> primaryRead = ReadRegistryResult(RegistryPath);
            GameStorageMetadataReadResult<GameStorageRegistry> lastKnownGoodRead = ReadRegistryResult(LastKnownGoodPath);

            // Never downgrade metadata written by a newer NMM. If the active
            // registry is newer, or the active registry is unavailable while the
            // only recoverable registry is newer, leave both files untouched.
            if (primaryRead.Status == GameStorageMetadataReadStatus.UnsupportedVersion ||
                ((primaryRead.Status == GameStorageMetadataReadStatus.Missing ||
                  primaryRead.Status == GameStorageMetadataReadStatus.Invalid) &&
                 lastKnownGoodRead.Status == GameStorageMetadataReadStatus.UnsupportedVersion))
            {
                return;
            }

            registry = NormalizeRegistry(registry);
            registry.SchemaVersion = GameStorageConstants.RegistrySchemaVersion;
            Directory.CreateDirectory(RegistryDirectory);
            Directory.CreateDirectory(BackupDirectory);
            if (File.Exists(RegistryPath))
            {
                string backupPath = Path.Combine(BackupDirectory, $"storages-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
                File.Copy(RegistryPath, backupPath, true);
            }
            WriteJson(RegistryPath, registry);
            if (lastKnownGoodRead.Status != GameStorageMetadataReadStatus.UnsupportedVersion)
                WriteJson(LastKnownGoodPath, registry);

            PruneRegistryBackups();
        }

        /// <summary>
        /// Keeps the registry backup folder bounded. Backup cleanup is best-effort
        /// and never affects the active or last-known-good registry files.
        /// </summary>
        private void PruneRegistryBackups()
        {
            try
            {
                var backups = Directory.EnumerateFiles(BackupDirectory, "storages-*.json")
                    .Select(path => new
                    {
                        Path = path,
                        LastWriteUtc = File.GetLastWriteTimeUtc(path)
                    })
                    .OrderByDescending(x => x.LastWriteUtc)
                    .ThenByDescending(x => x.Path, StringComparer.OrdinalIgnoreCase)
                    .Skip(GameStorageConstants.RegistryBackupRetentionCount)
                    .ToList();

                foreach (var backup in backups)
                {
                    try
                    {
                        File.Delete(backup.Path);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Reads and classifies one folder manifest. Missing, corrupt, and future
        /// schema files remain distinct so legacy storage is not confused with
        /// damaged or newer metadata.
        /// </summary>
        private GameStorageMetadataReadResult<GameStorageFolderManifest> ReadFolderManifestResult(string folderPath)
        {
            var result = new GameStorageMetadataReadResult<GameStorageFolderManifest>();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                result.Status = GameStorageMetadataReadStatus.Missing;
                return result;
            }

            string manifestPath = Path.Combine(folderPath, GameStorageConstants.FolderManifestFileName);
            if (!File.Exists(manifestPath))
            {
                result.Status = GameStorageMetadataReadStatus.Missing;
                return result;
            }

            try
            {
                result.Value = JsonConvert.DeserializeObject<GameStorageFolderManifest>(File.ReadAllText(manifestPath));
                if (result.Value == null)
                {
                    result.Status = GameStorageMetadataReadStatus.Invalid;
                    return result;
                }

                result.Status = result.Value.SchemaVersion > GameStorageConstants.FolderManifestSchemaVersion
                    ? GameStorageMetadataReadStatus.UnsupportedVersion
                    : GameStorageMetadataReadStatus.Valid;
                return result;
            }
            catch
            {
                result.Status = GameStorageMetadataReadStatus.Invalid;
                return result;
            }
        }

        /// <summary>
        /// Returns a folder manifest only when its schema is supported by this NMM version.
        /// </summary>
        private GameStorageFolderManifest ReadFolderManifest(string folderPath)
        {
            GameStorageMetadataReadResult<GameStorageFolderManifest> result = ReadFolderManifestResult(folderPath);
            return result.Status == GameStorageMetadataReadStatus.Valid ? result.Value : null;
        }

        /// <summary>
        /// Writes JSON through a same-directory temporary file. Atomic replacement is
        /// used when supported; a compatibility fallback keeps older/network file
        /// systems working if Replace is unavailable.
        /// </summary>
        private void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            FileAttributes? originalAttributes = null;
            if (File.Exists(path))
            {
                originalAttributes = File.GetAttributes(path);
                File.SetAttributes(path, originalAttributes.Value & ~FileAttributes.Hidden & ~FileAttributes.ReadOnly);
            }

            string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                WriteDurableTextFile(temporaryPath, JsonConvert.SerializeObject(value, _jsonSettings));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporaryPath, path, null);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                        File.Copy(temporaryPath, path, true);
                        File.Delete(temporaryPath);
                    }
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch
                    {
                    }
                }

                if (originalAttributes.HasValue && File.Exists(path))
                    File.SetAttributes(path, originalAttributes.Value);
            }
        }

        /// <summary>
        /// Writes UTF-8 text and flushes file contents before metadata replacement.
        /// </summary>
        private void WriteDurableTextFile(string path, string text)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                {
                    writer.Write(text);
                    writer.Flush();
                }

                try
                {
                    stream.Flush(true);
                }
                catch (IOException)
                {
                    // Some network/redirected file systems do not support a
                    // durable flush. Preserve compatibility and fall back to the
                    // normal flush semantics previously used by NMM.
                    stream.Flush();
                }
                catch (PlatformNotSupportedException)
                {
                    stream.Flush();
                }
            }
        }

        private void AddWriteFailure(GameStorageHealthCheck result, GameStoragePathSet paths, Exception exception)
        {
            string failedPath = TryGetPathFromException(exception) ?? string.Empty;
            Add(result, ResolveFolderRole(paths, failedPath), failedPath, GameStorageHealthStatus.NotWritable, true, true,
                LanguageManager.Get("GameStorage.Health.NotWritable.Message", "NMM could not write Game Storage metadata to this folder."),
                exception.Message,
                LanguageManager.Get("GameStorage.Health.NotWritable.Fix", "Check folder permissions or select a writable Game Storage folder."));
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

        /// <summary>
        /// Counts staged VirtualInstall payload files while excluding NMM Game Storage metadata.
        /// </summary>
        private int CountVirtualInstallPayloadFiles(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return 0;
            try
            {
                string manifestPath = Path.GetFullPath(Path.Combine(path, GameStorageConstants.FolderManifestFileName));
                return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(x => !string.Equals(Path.GetFullPath(x), manifestPath, StringComparison.OrdinalIgnoreCase))
                    .Take(101)
                    .Count();
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
            return GameStorageLocalization.GetFolderRoleName(role);
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