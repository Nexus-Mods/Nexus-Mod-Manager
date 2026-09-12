namespace NexusClientTests
{
    using System;
    using System.IO;
    using System.Linq;

    using Nexus.Client.GameStorage;

    using NUnit.Framework;

    [TestFixture]
    public class GameStorageServiceTests
    {
        private string _tempRoot;
        private GameStorageService _service;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "NMM_GameStorage_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _service = new GameStorageService(Path.Combine(_tempRoot, "Registry"), new Version(9, 0, 1));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, true);
        }

        [Test]
        public void ValidateStorage_ValidLegacyFolders_IsReadOnly()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA", withArchive: true, withVirtualFile: true);

            var result = _service.ValidateStorage(paths);

            Assert.IsTrue(result.IsHealthy);
            Assert.IsTrue(result.NeedsInitialization);
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(paths.ModsPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "StorageA", "NMMStorage.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "Registry", "storages.json")), Is.False);
        }

        [Test]
        public void InitializeMetadataForStorage_ValidLegacyFolders_InitializesMetadata()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA", withArchive: true, withVirtualFile: true);

            _service.InitializeMetadataForStorage(paths);
            var result = _service.ValidateStorage(paths);

            Assert.IsTrue(result.IsHealthy);
            Assert.IsFalse(result.NeedsInitialization);
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(paths.ModsPath, ".nmm-folder.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "StorageA", "NMMStorage.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "Registry", "storages.json")), Is.True);
        }

        [Test]
        public void InitializeMetadataForStorage_EmptyVirtualInstallMetadataIsNotTreatedAsPayload()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");
            _service.InitializeMetadataForStorage(paths);
            DeleteFileIfExists(Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json"));

            GameStorageHealthCheck result = _service.ValidateStorage(paths);

            Assert.That(result.Items.Any(x => x.Role == GameStorageFolderRole.VirtualInstall && x.Status == GameStorageHealthStatus.SuspiciousEmptyFolder), Is.False);
        }

        [Test]
        public void ValidateStorage_LegacyVirtualInstallCollision_IsNotRepairedUntilExplicitInitialization()
        {
            string root = Path.Combine(_tempRoot, "LegacyCollision");
            string installInfo = Path.Combine(root, "InstallInfo");
            string virtualInstall = Path.Combine(root, "VirtualInstall");
            Directory.CreateDirectory(installInfo);
            Directory.CreateDirectory(virtualInstall);
            File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");

            var paths = new GameStoragePathSet
            {
                GameId = "Fallout3",
                GameName = "Fallout 3",
                GameInstallPath = Path.Combine(_tempRoot, "Game"),
                InstallInfoPath = installInfo,
                ModsPath = root,
                VirtualInstallPath = virtualInstall,
                LinkFolderRequired = false
            };

            const string storageId = "legacy-collision-storage";
            WriteFolderManifest(root, "Fallout3", storageId, "VirtualInstall");
            WriteFolderManifest(installInfo, "Fallout3", storageId, "InstallInfo");

            GameStorageHealthCheck validation = _service.ValidateStorage(paths);

            Assert.IsFalse(validation.IsHealthy);
            Assert.That(validation.Items.Any(x => x.Role == GameStorageFolderRole.Mods && x.Status == GameStorageHealthStatus.PartialMatch), Is.True);
            Assert.That(File.Exists(Path.Combine(virtualInstall, ".nmm-folder.json")), Is.False);

            Assert.IsTrue(_service.RepairKnownLegacyStorageMetadata(paths));

            Assert.IsTrue(_service.ValidateStorage(paths).IsHealthy);
            Assert.That(File.Exists(Path.Combine(virtualInstall, ".nmm-folder.json")), Is.True);
        }

        [Test]
        public void ValidateStorage_ExistingHealthyMetadata_IsNotRewritten()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA", withArchive: true, withVirtualFile: true);
            _service.InitializeMetadataForStorage(paths);

            string installManifestPath = Path.Combine(paths.InstallInfoPath, ".nmm-folder.json");
            string rootManifestPath = Path.Combine(_tempRoot, "StorageA", "NMMStorage.json");
            string registryPath = Path.Combine(_tempRoot, "Registry", "storages.json");
            string installManifestBefore = File.ReadAllText(installManifestPath);
            string rootManifestBefore = File.ReadAllText(rootManifestPath);
            string registryBefore = File.ReadAllText(registryPath);

            GameStorageHealthCheck result = _service.ValidateStorage(paths, true);

            Assert.IsTrue(result.IsHealthy);
            Assert.AreEqual(installManifestBefore, File.ReadAllText(installManifestPath));
            Assert.AreEqual(rootManifestBefore, File.ReadAllText(rootManifestPath));
            Assert.AreEqual(registryBefore, File.ReadAllText(registryPath));
        }

        [Test]
        public void ValidateStorage_MissingModsFolder_DoesNotCreateReplacementFolder()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");
            Directory.Delete(paths.ModsPath, true);

            var result = _service.ValidateStorage(paths, false);

            Assert.IsFalse(result.IsHealthy);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.MissingMods), Is.True);
            Assert.That(Directory.Exists(paths.ModsPath), Is.False);
        }

        [Test]
        public void ValidateStorage_MismatchedGameManifest_IsRejected()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");
            WriteFolderManifest(paths.ModsPath, "Fallout4", "StorageA", "Mods");

            var result = _service.ValidateStorage(paths, false);

            Assert.IsFalse(result.IsHealthy);
            Assert.IsTrue(result.IsUsable);
            Assert.IsTrue(result.HasWarnings);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedGame), Is.True);
        }

        [Test]
        public void ValidateStorage_CompatibilityInitializeFlag_RemainsReadOnly()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");

            var result = _service.ValidateStorage(paths, true);

            Assert.IsTrue(result.IsHealthy);
            Assert.IsTrue(result.NeedsInitialization);
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "Registry", "storages.json")), Is.False);
        }

        [Test]
        public void ValidateStorage_SelectedFolderManifestsOverrideStaleActiveStorage()
        {
            var current = CreateStorage("Fallout3", "CurrentStorage");
            _service.InitializeMetadataForStorage(current);
            string activeStorageId = _service.ValidateStorage(current).StorageId;

            var selected = CreateStorage("Fallout3", "SelectedStorage");
            const string selectedStorageId = "selected-folder-storage";
            WriteFolderManifest(selected.InstallInfoPath, "Fallout3", selectedStorageId, "InstallInfo");
            WriteFolderManifest(selected.ModsPath, "Fallout3", selectedStorageId, "Mods");
            WriteFolderManifest(selected.VirtualInstallPath, "Fallout3", selectedStorageId, "VirtualInstall");

            GameStorageHealthCheck result = _service.ValidateStorage(selected);

            Assert.AreEqual(selectedStorageId, result.StorageId);
            Assert.AreNotEqual(activeStorageId, result.StorageId);
            Assert.IsTrue(result.IsHealthy);
        }

        [Test]
        public void ValidateStorage_ExactRegistryPathsOverrideStaleActiveStorage()
        {
            var selected = CreateStorage("Fallout3", "SelectedStorage");
            _service.InitializeMetadataForStorage(selected);
            string selectedStorageId = _service.ValidateStorage(selected).StorageId;

            var current = CreateStorage("Fallout3", "CurrentStorage");
            // Metadata-free paths intentionally inherit the active storage identity; seed explicit metadata so this fixture represents a distinct storage.
            const string currentStorageId = "current-storage";
            WriteFolderManifest(current.InstallInfoPath, "Fallout3", currentStorageId, "InstallInfo");
            WriteFolderManifest(current.ModsPath, "Fallout3", currentStorageId, "Mods");
            WriteFolderManifest(current.VirtualInstallPath, "Fallout3", currentStorageId, "VirtualInstall");
            _service.InitializeMetadataForStorage(current);
            string activeStorageId = _service.ValidateStorage(current).StorageId;
            Assert.AreEqual(currentStorageId, activeStorageId);
            Assert.AreNotEqual(selectedStorageId, activeStorageId);

            DeleteFileIfExists(Path.Combine(selected.InstallInfoPath, ".nmm-folder.json"));
            DeleteFileIfExists(Path.Combine(selected.ModsPath, ".nmm-folder.json"));
            DeleteFileIfExists(Path.Combine(selected.VirtualInstallPath, ".nmm-folder.json"));
            DeleteFileIfExists(Path.Combine(_tempRoot, "SelectedStorage", "NMMStorage.json"));

            GameStorageHealthCheck result = _service.ValidateStorage(selected);

            Assert.AreEqual(selectedStorageId, result.StorageId);
            Assert.AreNotEqual(activeStorageId, result.StorageId);
            Assert.IsTrue(result.IsHealthy);
            Assert.IsTrue(result.NeedsInitialization);
        }

        [Test]
        public void ValidateStorage_MatchingRootManifestOverridesStaleActiveStorage()
        {
            var current = CreateStorage("Fallout3", "CurrentStorage");
            _service.InitializeMetadataForStorage(current);
            string activeStorageId = _service.ValidateStorage(current).StorageId;

            var selected = CreateStorage("Fallout3", "SelectedStorage");
            const string selectedStorageId = "selected-root-storage";
            WriteRootManifest(Path.Combine(_tempRoot, "SelectedStorage"), "Fallout3", selectedStorageId);

            GameStorageHealthCheck result = _service.ValidateStorage(selected);

            Assert.AreEqual(selectedStorageId, result.StorageId);
            Assert.AreNotEqual(activeStorageId, result.StorageId);
            Assert.IsTrue(result.IsHealthy);
        }

        [Test]
        public void LinkFolderRules_RequireGameDriveForHardlinks()
        {
            Assert.IsTrue(_service.IsLinkFolderRequired(@"D:\NMM\Virtual", @"C:\Games\Skyrim"));
            Assert.IsFalse(_service.IsLinkFolderOnGameDrive(@"D:\NMM\Links", @"C:\Games\Skyrim"));
            Assert.IsTrue(_service.IsLinkFolderOnGameDrive(@"C:\NMM\Links", @"C:\Games\Skyrim"));
        }


        [Test]
        public void NormalizeVirtualInstallDirectory_PreservesLegacyAndResolvedConfigurations()
        {
            string root = Path.Combine(_tempRoot, "ExistingStorage");
            string resolved = Path.Combine(root, "VirtualInstall");

            Assert.AreEqual(resolved, _service.NormalizeVirtualInstallDirectory(root));
            Assert.AreEqual(resolved, _service.NormalizeVirtualInstallDirectory(resolved));
        }

        [Test]
        public void ResolveExplicitlySelectedPaths_MismatchedMetadataUsesCurrentGameAndCompletesMissingPaths()
        {
            var current = CreateStorage("SkyrimSE", "CurrentStorage");
            string selectedInstallInfo = Path.Combine(_tempRoot, "OldStorage", "InstallInfo");
            var candidate = new GameStorageCandidate
            {
                GameId = "Fallout4",
                InstallInfoPath = selectedInstallInfo
            };

            GameStoragePathSet resolved = _service.ResolveExplicitlySelectedPaths(current, candidate);

            Assert.AreEqual(current.GameId, resolved.GameId);
            Assert.AreEqual(selectedInstallInfo, resolved.InstallInfoPath);
            Assert.AreEqual(current.ModsPath, resolved.ModsPath);
            Assert.AreEqual(current.VirtualInstallPath, resolved.VirtualInstallPath);
        }

        [Test]
        public void DiscoverRecoveryCandidatesFromRoot_SelectedRootManifestCreatesHighConfidenceCandidate()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");
            WriteRootManifest(Path.Combine(_tempRoot, "StorageA"), "SkyrimSE", "StorageA");

            var candidates = _service.DiscoverRecoveryCandidatesFromRoot(paths, Path.Combine(_tempRoot, "StorageA"));

            var candidate = candidates.FirstOrDefault(x => x.CandidateKind == "Selected root manifest");
            Assert.IsNotNull(candidate);
            Assert.AreEqual(GameStorageCandidateConfidence.High, candidate.ConfidenceLevel);
            Assert.AreEqual(paths.InstallInfoPath, candidate.InstallInfoPath);
            Assert.AreEqual(paths.ModsPath, candidate.ModsPath);
            Assert.AreEqual(paths.VirtualInstallPath, candidate.VirtualInstallPath);
        }

        [Test]
        public void DiscoverRecoveryCandidatesFromRoot_InstallLogOnlyCandidate_IsAmbiguous()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");
            string looseInstallInfo = Path.Combine(_tempRoot, "LooseInstallInfo");
            Directory.CreateDirectory(looseInstallInfo);
            File.WriteAllText(Path.Combine(looseInstallInfo, "InstallLog.xml"), "<installLog />");

            var candidates = _service.DiscoverRecoveryCandidatesFromRoot(paths, _tempRoot);

            var candidate = candidates.FirstOrDefault(x => x.CandidateKind == "Possible InstallInfo folder" && x.InstallInfoPath == looseInstallInfo);
            Assert.IsNotNull(candidate);
            Assert.IsNull(candidate.GameId);
            Assert.AreEqual(GameStorageCandidateConfidence.Low, candidate.ConfidenceLevel);
            Assert.IsTrue(candidate.RequiresUserConfirmation);
            Assert.That(candidate.Warnings.Any(x => x.Contains("ambiguous")), Is.True);
        }

        [Test]
        public void ValidateRecoveryCandidate_OtherGameCandidate_IsRejected()
        {
            var current = CreateStorage("SkyrimSE", "StorageA");
            var other = CreateStorage("Fallout4", "StorageB");
            var candidate = new GameStorageCandidate
            {
                GameId = "Fallout4",
                StorageId = "StorageB",
                InstallInfoPath = other.InstallInfoPath,
                ModsPath = other.ModsPath,
                VirtualInstallPath = other.VirtualInstallPath
            };

            GameStorageHealthCheck healthCheck;
            bool applied = _service.ValidateRecoveryCandidate(current, candidate, out healthCheck);

            Assert.IsFalse(applied);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedGame), Is.True);
        }

        [Test]
        public void ValidateStorage_EmptyCurrentStorageAfterKnownGood_IsSuspicious()
        {
            var knownGood = CreateStorage("SkyrimSE", "StorageA", withArchive: true, withVirtualFile: true);
            _service.InitializeMetadataForStorage(knownGood);

            var empty = CreateStorage("SkyrimSE", "StorageB");
            var result = _service.ValidateStorage(empty);

            Assert.IsFalse(result.IsHealthy);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.SuspiciousEmptyFolder), Is.True);
        }

        [Test]
        public void ValidateRecoveryCandidate_SuspiciousEmptyStorage_IsRejectedWithoutConfirmation()
        {
            var knownGood = CreateStorage("SkyrimSE", "StorageA", withArchive: true);
            _service.InitializeMetadataForStorage(knownGood);

            var empty = CreateStorage("SkyrimSE", "StorageB");
            var candidate = CreateCandidate(empty);

            GameStorageHealthCheck healthCheck;
            bool applied = _service.ValidateRecoveryCandidate(knownGood, candidate, out healthCheck);

            Assert.IsFalse(applied);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.SuspiciousEmptyFolder), Is.True);
            Assert.IsTrue(_service.CanAcceptSuspiciousEmptyFolders(healthCheck));
        }

        [Test]
        public void ValidateRecoveryCandidate_SuspiciousEmptyStorage_CanBeAcceptedWithoutMutation()
        {
            var knownGood = CreateStorage("SkyrimSE", "StorageA", withArchive: true);
            _service.InitializeMetadataForStorage(knownGood);

            var empty = CreateStorage("SkyrimSE", "StorageB");
            var candidate = CreateCandidate(empty);

            GameStorageHealthCheck healthCheck;
            bool accepted = _service.ValidateRecoveryCandidate(knownGood, candidate, true, out healthCheck);

            Assert.IsTrue(accepted);
            Assert.IsFalse(healthCheck.IsHealthy);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.SuspiciousEmptyFolder), Is.True);
            Assert.That(File.Exists(Path.Combine(empty.InstallInfoPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(empty.ModsPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(empty.VirtualInstallPath, ".nmm-folder.json")), Is.False);
        }

        [Test]
        public void ValidateRecoveryCandidate_EmptyConfirmation_DoesNotAllowMissingModsFolder()
        {
            var knownGood = CreateStorage("SkyrimSE", "StorageA", withArchive: true);
            _service.InitializeMetadataForStorage(knownGood);

            var missing = CreateStorage("SkyrimSE", "StorageB");
            Directory.Delete(missing.ModsPath, true);
            var candidate = CreateCandidate(missing);

            GameStorageHealthCheck healthCheck;
            bool applied = _service.ValidateRecoveryCandidate(knownGood, candidate, true, out healthCheck);

            Assert.IsFalse(applied);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.MissingMods), Is.True);
            Assert.IsFalse(_service.CanAcceptSuspiciousEmptyFolders(healthCheck));
            Assert.That(Directory.Exists(missing.ModsPath), Is.False);
        }

        [Test]
        public void DiscoverRecoveryCandidates_CurrentMissingPaths_IsLowConfidence()
        {
            var current = new GameStoragePathSet
            {
                GameId = "Fallout3",
                GameName = "Fallout 3",
                GameInstallPath = Path.Combine(_tempRoot, "Fallout3"),
                InstallInfoPath = Path.Combine(_tempRoot, "MissingNMMCE", "InstallInfo"),
                ModsPath = Path.Combine(_tempRoot, "MissingNMMCE", "Mods"),
                VirtualInstallPath = Path.Combine(_tempRoot, "MissingNMMCE", "VirtualInstall"),
                LinkFolderRequired = false
            };

            var candidates = _service.DiscoverRecoveryCandidates(current);

            var candidate = candidates.FirstOrDefault(x => x.CandidateKind == "Proposed setup");
            Assert.IsNotNull(candidate);
            Assert.AreEqual(5, candidate.ConfidenceScore);
            Assert.AreEqual(GameStorageCandidateConfidence.Low, candidate.ConfidenceLevel);
        }

        [Test]
        public void DiscoverRecoveryCandidatesFromRoot_LegacyLayoutCreatesHigherConfidenceCandidate()
        {
            var current = CreateStorage("Fallout3", "CurrentStorage");
            string legacyRoot = Path.Combine(_tempRoot, "Games", "Nexus Mod Manager", "Fallout3");
            string installInfo = Path.Combine(legacyRoot, "Install Info");
            string mods = Path.Combine(legacyRoot, "Mods");
            Directory.CreateDirectory(installInfo);
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");
            File.WriteAllText(Path.Combine(mods, "ExampleMod.7z"), "archive");

            var candidates = _service.DiscoverRecoveryCandidatesFromRoot(current, legacyRoot);

            var candidate = candidates.FirstOrDefault(x => x.CandidateKind == "Legacy NMM setup");
            Assert.IsNotNull(candidate);
            Assert.AreEqual("Fallout3", candidate.GameId);
            Assert.AreEqual(installInfo, candidate.InstallInfoPath);
            Assert.Greater(candidate.ConfidenceScore, 15);
            Assert.AreEqual(GameStorageCandidateConfidence.High, candidate.ConfidenceLevel);
            Assert.IsFalse(candidates.Any(x => x.CandidateKind == "Possible InstallInfo folder" && x.InstallInfoPath == installInfo));
        }

        [Test]
        public void ValidateRecoveryCandidate_UsesSelectedFolderStorageIdInsteadOfActiveStorage()
        {
            var current = CreateStorage("Fallout3", "NMMCECurrent");
            _service.InitializeMetadataForStorage(current);
            GameStorageHealthCheck currentHealth = _service.ValidateStorage(current);
            Assert.IsTrue(currentHealth.IsHealthy);

            var oldStorage = CreateStorage("Fallout3", "OldFallout3Storage");
            const string oldStorageId = "old-fallout3-storage";
            WriteFolderManifest(oldStorage.InstallInfoPath, "Fallout3", oldStorageId, "InstallInfo");
            WriteFolderManifest(oldStorage.ModsPath, "Fallout3", oldStorageId, "Mods");
            WriteFolderManifest(oldStorage.VirtualInstallPath, "Fallout3", oldStorageId, "VirtualInstall");

            var candidate = CreateCandidate(oldStorage);
            GameStorageHealthCheck healthCheck;
            bool applied = _service.ValidateRecoveryCandidate(current, candidate, out healthCheck);

            Assert.IsTrue(applied);
            Assert.AreEqual(oldStorageId, healthCheck.StorageId);
            Assert.AreNotEqual(currentHealth.StorageId, healthCheck.StorageId);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedStorageId), Is.False);
        }

        [Test]
        public void ValidateRecoveryCandidate_LegacyFoldersReceiveNewIdentityInsteadOfActiveStorageId()
        {
            var current = CreateStorage("Fallout3", "NMMCECurrent");
            _service.InitializeMetadataForStorage(current);
            GameStorageHealthCheck currentHealth = _service.ValidateStorage(current);
            Assert.IsTrue(currentHealth.IsHealthy);

            var legacy = CreateStorage("Fallout3", "LegacyFallout3Storage");
            var candidate = CreateCandidate(legacy);

            GameStorageHealthCheck healthCheck;
            bool applied = _service.ValidateRecoveryCandidate(current, candidate, out healthCheck);

            Assert.IsTrue(applied);
            Assert.IsFalse(string.IsNullOrWhiteSpace(healthCheck.StorageId));
            Assert.AreNotEqual(currentHealth.StorageId, healthCheck.StorageId);
            Assert.IsTrue(_service.ValidateStorage(legacy).IsHealthy);
        }

        [Test]
        public void ValidateRecoveryCandidate_ConflictingSameGameStorageIds_CanBeExplicitlyRebound()
        {
            var current = CreateStorage("Fallout3", "CurrentStorage");
            var mixed = CreateStorage("Fallout3", "MixedOldStorage");
            WriteFolderManifest(mixed.InstallInfoPath, "Fallout3", "install-info-storage", "InstallInfo");
            WriteFolderManifest(mixed.ModsPath, "Fallout3", "mods-storage", "Mods");
            WriteFolderManifest(mixed.VirtualInstallPath, "Fallout3", "install-info-storage", "VirtualInstall");

            var candidate = CreateCandidate(mixed);
            GameStorageHealthCheck healthCheck;

            Assert.IsFalse(_service.ValidateRecoveryCandidate(current, candidate, out healthCheck));
            Assert.IsTrue(_service.CanAcceptSameGameStorageRebinding(healthCheck));
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedStorageId), Is.True);

            string modsManifestBefore = File.ReadAllText(Path.Combine(mixed.ModsPath, ".nmm-folder.json"));
            Assert.IsTrue(_service.ValidateRecoveryCandidate(current, candidate, false, true, out healthCheck));
            Assert.AreEqual("install-info-storage", healthCheck.StorageId);
            Assert.IsFalse(healthCheck.IsHealthy);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedStorageId), Is.True);
            Assert.AreEqual(modsManifestBefore, File.ReadAllText(Path.Combine(mixed.ModsPath, ".nmm-folder.json")));
        }

        [Test]
        public void ValidateRecoveryCandidate_NonShareableFolderBoundToAnotherGame_RemainsBlocked()
        {
            var current = CreateStorage("Fallout3", "CurrentStorage");
            var mixed = CreateStorage("Fallout3", "CrossGameStorage");
            WriteFolderManifestWithBindings(
                mixed.InstallInfoPath,
                "InstallInfo",
                "Fallout3",
                "fallout3-storage",
                "FalloutNV",
                "falloutnv-storage");
            WriteFolderManifest(mixed.ModsPath, "Fallout3", "fallout3-storage", "Mods");
            WriteFolderManifest(mixed.VirtualInstallPath, "Fallout3", "fallout3-storage", "VirtualInstall");

            var candidate = CreateCandidate(mixed);
            GameStorageHealthCheck healthCheck;
            bool applied = _service.ValidateRecoveryCandidate(current, candidate, out healthCheck);

            Assert.IsFalse(applied);
            Assert.That(healthCheck.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedGame), Is.True);
            Assert.IsFalse(_service.CanAcceptSameGameStorageRebinding(healthCheck));
        }

        [Test]
        public void RecoveryCandidateUsability_PrefersUsableLegacySetupOverBrokenHighScoreBackup()
        {
            var current = CreateStorage("Fallout3", "CurrentStorage");
            var usable = CreateStorage("Fallout3", "UsableOldFallout3Storage");
            var usableCandidate = CreateCandidate(usable);
            usableCandidate.ConfidenceScore = 90;

            var brokenCandidate = new GameStorageCandidate
            {
                CandidateKind = "Last-known-good backup",
                GameId = "Fallout3",
                ConfidenceScore = 98,
                InstallInfoPath = Path.Combine(_tempRoot, "MissingNMMCE", "InstallInfo"),
                ModsPath = Path.Combine(_tempRoot, "MissingNMMCE", "Mods"),
                VirtualInstallPath = Path.Combine(_tempRoot, "MissingNMMCE", "VirtualInstall")
            };

            int usableRank = _service.GetRecoveryCandidateUsabilityRank(current, usableCandidate);
            int brokenRank = _service.GetRecoveryCandidateUsabilityRank(current, brokenCandidate);

            Assert.Greater(usableRank, brokenRank);
            Assert.AreEqual(4, usableRank);
            Assert.AreEqual(2, brokenRank);
        }

        [Test]
        public void ValidateStorage_CorruptFolderManifest_IsWarningNotLegacyAndRemainsUntouched()
        {
            var paths = CreateStorage("Fallout4", "CorruptManifest");
            string manifestPath = Path.Combine(paths.ModsPath, ".nmm-folder.json");
            File.WriteAllText(manifestPath, "{ definitely-not-json");

            GameStorageHealthCheck result = _service.ValidateStorage(paths);

            Assert.IsFalse(result.IsHealthy);
            Assert.IsTrue(result.IsUsable);
            Assert.That(result.Items.Any(x => x.Role == GameStorageFolderRole.Mods && x.Status == GameStorageHealthStatus.InvalidManifest), Is.True);
            Assert.That(result.Items.Any(x => x.Role == GameStorageFolderRole.Mods && x.Status == GameStorageHealthStatus.LegacyValidNeedsInitialization), Is.False);
            Assert.AreEqual("{ definitely-not-json", File.ReadAllText(manifestPath));
        }

        [Test]
        public void ValidateStorage_FutureFolderManifest_IsWarningAndExplicitInitializationDoesNotDowngradeIt()
        {
            var paths = CreateStorage("Fallout4", "FutureManifest");
            string manifestPath = Path.Combine(paths.ModsPath, ".nmm-folder.json");
            string futureManifest = "{\"SchemaVersion\":999,\"App\":\"Nexus Mod Manager CE\",\"FolderRole\":\"Mods\",\"StorageId\":\"future-storage\",\"GameId\":\"Fallout4\"}";
            File.WriteAllText(manifestPath, futureManifest);

            GameStorageHealthCheck result = _service.ValidateStorage(paths);
            _service.InitializeMetadataForStorage(paths);

            Assert.IsFalse(result.IsHealthy);
            Assert.IsTrue(result.IsUsable);
            Assert.That(result.Items.Any(x => x.Role == GameStorageFolderRole.Mods && x.Status == GameStorageHealthStatus.UnsupportedManifestVersion), Is.True);
            Assert.AreEqual(futureManifest, File.ReadAllText(manifestPath));
        }

        [Test]
        public void InitializeMetadataForStorage_ExactRoleCollision_DoesNotOverwriteSharedFolderManifest()
        {
            var paths = CreateStorage("Fallout3", "RoleCollision");
            paths.VirtualInstallPath = paths.ModsPath;
            string markerPath = Path.Combine(paths.ModsPath, "existing.txt");
            File.WriteAllText(markerPath, "keep");

            _service.InitializeMetadataForStorage(paths);
            GameStorageHealthCheck result = _service.ValidateStorage(paths);

            Assert.IsFalse(result.IsHealthy);
            Assert.IsTrue(result.IsUsable);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.FolderRoleCollision), Is.True);
            Assert.That(File.Exists(Path.Combine(paths.ModsPath, ".nmm-folder.json")), Is.False);
            Assert.AreEqual("keep", File.ReadAllText(markerPath));
        }

        [Test]
        public void InitializeMetadataForStorage_OnlyNewestStorageRemainsLastKnownGoodForGame()
        {
            var first = CreateStorage("SkyrimSE", "FirstStorage");
            var second = CreateStorage("SkyrimSE", "SecondStorage");

            _service.InitializeMetadataForStorage(first);
            _service.InitializeMetadataForStorage(second);

            string registry = File.ReadAllText(Path.Combine(_tempRoot, "Registry", "storages.json"));
            Assert.AreEqual(1, CountOccurrences(registry, "\"LastKnownGood\": true"));
        }

        [Test]
        public void ValidateStorage_CorruptPrimaryRegistry_FallsBackToLastKnownGoodRegistry()
        {
            var known = CreateStorage("SkyrimSE", "KnownStorage");
            _service.InitializeMetadataForStorage(known);
            string knownStorageId = _service.ValidateStorage(known).StorageId;
            DeleteFileIfExists(Path.Combine(known.InstallInfoPath, ".nmm-folder.json"));
            DeleteFileIfExists(Path.Combine(known.ModsPath, ".nmm-folder.json"));
            DeleteFileIfExists(Path.Combine(known.VirtualInstallPath, ".nmm-folder.json"));
            DeleteFileIfExists(Path.Combine(_tempRoot, "KnownStorage", "NMMStorage.json"));
            File.WriteAllText(Path.Combine(_tempRoot, "Registry", "storages.json"), "{ broken-registry");

            GameStorageHealthCheck result = _service.ValidateStorage(known);

            Assert.AreEqual(knownStorageId, result.StorageId);
            Assert.IsTrue(result.IsHealthy);
            Assert.IsTrue(result.NeedsInitialization);
        }

        [Test]
        public void InitializeMetadataForStorage_FutureRegistry_IsNotDowngraded()
        {
            var paths = CreateStorage("SkyrimSE", "FutureRegistryStorage");
            string registryDirectory = Path.Combine(_tempRoot, "Registry");
            Directory.CreateDirectory(registryDirectory);
            string registryPath = Path.Combine(registryDirectory, "storages.json");
            string futureRegistry = "{\"SchemaVersion\":999,\"ActiveStorageByGame\":{},\"KnownStorages\":[]}";
            File.WriteAllText(registryPath, futureRegistry);

            _service.InitializeMetadataForStorage(paths);

            Assert.AreEqual(futureRegistry, File.ReadAllText(registryPath));
        }

        [Test]
        public void ValidateStorage_MissingRequiredModsFolder_IsNotUsableButRemainsRecoverable()
        {
            var paths = CreateStorage("SkyrimSE", "MissingRequiredMods");
            Directory.Delete(paths.ModsPath, true);

            GameStorageHealthCheck result = _service.ValidateStorage(paths);

            Assert.IsFalse(result.IsUsable);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.MissingMods && x.IsRecoverable), Is.True);
        }

        [Test]
        public void ValidateStorage_NonEmptyInstallInfoWithoutInstallLog_IsUsableWarning()
        {
            var paths = CreateStorage("Fallout4", "MissingInstallLog");
            File.Delete(Path.Combine(paths.InstallInfoPath, "InstallLog.xml"));
            File.WriteAllText(Path.Combine(paths.InstallInfoPath, "existing-metadata.xml"), "data");

            GameStorageHealthCheck result = _service.ValidateStorage(paths);

            Assert.IsFalse(result.IsHealthy);
            Assert.IsTrue(result.IsUsable);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.MissingInstallLog), Is.True);
        }

        [Test]
        public void ValidateStorage_EmptyNewInstallInfoWithoutInstallLog_DoesNotWarn()
        {
            var paths = CreateStorage("Fallout4", "EmptyInstallInfo");
            File.Delete(Path.Combine(paths.InstallInfoPath, "InstallLog.xml"));

            GameStorageHealthCheck result = _service.ValidateStorage(paths);

            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.MissingInstallLog), Is.False);
        }

        [Test]
        public void ShouldAttemptKnownLegacyStorageMetadataRepair_OnlyMatchesKnownCollisionSignature()
        {
            var healthy = new GameStorageHealthCheck();
            healthy.Items.Add(new GameStorageHealthItem
            {
                Role = GameStorageFolderRole.Mods,
                Status = GameStorageHealthStatus.Healthy
            });
            Assert.IsFalse(_service.ShouldAttemptKnownLegacyStorageMetadataRepair(healthy));

            var collision = new GameStorageHealthCheck();
            collision.Items.Add(new GameStorageHealthItem
            {
                Role = GameStorageFolderRole.Mods,
                Status = GameStorageHealthStatus.PartialMatch
            });
            collision.Items.Add(new GameStorageHealthItem
            {
                Role = GameStorageFolderRole.VirtualInstall,
                Status = GameStorageHealthStatus.LegacyValidNeedsInitialization
            });

            Assert.IsTrue(_service.ShouldAttemptKnownLegacyStorageMetadataRepair(collision));
        }

        [Test]
        public void InitializeMetadataForStorage_RegistryBackupsArePrunedToRetentionLimit()
        {
            var first = CreateStorage("SkyrimSE", "BackupRetentionFirst");
            _service.InitializeMetadataForStorage(first);

            string backupDirectory = Path.Combine(_tempRoot, "Registry", "Backups");
            Directory.CreateDirectory(backupDirectory);
            for (int i = 0; i < 25; i++)
            {
                string backup = Path.Combine(backupDirectory, "storages-test-" + i.ToString("D2") + ".json");
                File.WriteAllText(backup, "{}");
                File.SetLastWriteTimeUtc(backup, DateTime.UtcNow.AddMinutes(-100 - i));
            }

            string unrelated = Path.Combine(backupDirectory, "keep-me.txt");
            File.WriteAllText(unrelated, "keep");

            var second = CreateStorage("SkyrimSE", "BackupRetentionSecond");
            _service.InitializeMetadataForStorage(second);

            Assert.LessOrEqual(Directory.EnumerateFiles(backupDirectory, "storages-*.json").Count(), 20);
            Assert.IsTrue(File.Exists(unrelated));
        }

        [Test]
        public void GetBestRecoveryCandidate_PrefersUsableStorageWithoutChangingRankingSemantics()
        {
            var current = CreateStorage("Fallout3", "BestCandidateCurrent");
            var usable = CreateStorage("Fallout3", "BestCandidateUsable");
            var usableCandidate = CreateCandidate(usable);
            usableCandidate.CandidateKind = "Legacy NMM setup";
            usableCandidate.ConfidenceScore = 90;

            var brokenCandidate = new GameStorageCandidate
            {
                CandidateKind = "Last-known-good backup",
                GameId = "Fallout3",
                ConfidenceScore = 98,
                InstallInfoPath = Path.Combine(_tempRoot, "MissingBestCandidate", "InstallInfo"),
                ModsPath = Path.Combine(_tempRoot, "MissingBestCandidate", "Mods"),
                VirtualInstallPath = Path.Combine(_tempRoot, "MissingBestCandidate", "VirtualInstall")
            };

            GameStorageCandidate best = _service.GetBestRecoveryCandidate(
                current,
                new[] { brokenCandidate, usableCandidate });

            Assert.AreSame(usableCandidate, best);
        }

        private static int CountOccurrences(string value, string search)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += search.Length;
            }
            return count;
        }

        private GameStoragePathSet CreateStorage(string gameId, string rootName, bool withArchive = false, bool withVirtualFile = false)
        {
            string root = Path.Combine(_tempRoot, rootName);
            string installInfo = Path.Combine(root, "InstallInfo");
            string mods = Path.Combine(root, "Mods");
            string virtualInstall = Path.Combine(root, "VirtualInstall");
            Directory.CreateDirectory(installInfo);
            Directory.CreateDirectory(mods);
            Directory.CreateDirectory(virtualInstall);
            File.WriteAllText(Path.Combine(installInfo, "InstallLog.xml"), "<installLog />");
            if (withArchive)
                File.WriteAllText(Path.Combine(mods, "ExampleMod.7z"), "archive");
            if (withVirtualFile)
            {
                string stagedFolder = Path.Combine(virtualInstall, "Data");
                Directory.CreateDirectory(stagedFolder);
                File.WriteAllText(Path.Combine(stagedFolder, "Example.esp"), "plugin");
            }

            return new GameStoragePathSet
            {
                GameId = gameId,
                GameName = gameId,
                GameInstallPath = Path.Combine(root, "Game"),
                InstallInfoPath = installInfo,
                ModsPath = mods,
                VirtualInstallPath = virtualInstall,
                LinkFolderRequired = false
            };
        }

        private static GameStorageCandidate CreateCandidate(GameStoragePathSet paths)
        {
            return new GameStorageCandidate
            {
                GameId = paths.GameId,
                InstallInfoPath = paths.InstallInfoPath,
                ModsPath = paths.ModsPath,
                VirtualInstallPath = paths.VirtualInstallPath,
                LinkFolderPath = paths.LinkFolderPath,
                LinkFolderRequired = paths.LinkFolderRequired
            };
        }

        private static void DeleteFileIfExists(string path)
        {
            if (!File.Exists(path))
                return;

            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }

        private static void WriteFolderManifest(string folder, string gameId, string storageId, string role)
        {
            File.WriteAllText(Path.Combine(folder, ".nmm-folder.json"),
                "{\"SchemaVersion\":1,\"App\":\"Nexus Mod Manager\",\"FolderRole\":\"" + role + "\",\"StorageId\":\"" + storageId + "\",\"GameId\":\"" + gameId + "\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\",\"LastSeenUtc\":\"2026-01-01T00:00:00Z\"}");
        }

        private static void WriteFolderManifestWithBindings(string folder, string role, string gameId, string storageId, string otherGameId, string otherStorageId)
        {
            File.WriteAllText(Path.Combine(folder, ".nmm-folder.json"),
                "{\"SchemaVersion\":2,\"App\":\"Nexus Mod Manager\",\"FolderRole\":\"" + role + "\",\"StorageId\":\"" + storageId + "\",\"GameId\":\"" + gameId + "\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\",\"LastSeenUtc\":\"2026-01-01T00:00:00Z\",\"Bindings\":[{\"GameId\":\"" + gameId + "\",\"StorageId\":\"" + storageId + "\"},{\"GameId\":\"" + otherGameId + "\",\"StorageId\":\"" + otherStorageId + "\"}]}" );
        }

        private static void WriteRootManifest(string root, string gameId, string storageId)
        {
            File.WriteAllText(Path.Combine(root, "NMMStorage.json"),
                "{\"SchemaVersion\":1,\"App\":\"Nexus Mod Manager\",\"StorageId\":\"" + storageId + "\",\"GameId\":\"" + gameId + "\",\"GameName\":\"" + gameId + "\",\"Folders\":{\"InstallInfo\":\"InstallInfo\",\"Mods\":\"Mods\",\"VirtualInstall\":\"VirtualInstall\"},\"LinkFolderRequired\":false,\"CreatedUtc\":\"2026-01-01T00:00:00Z\",\"LastSeenUtc\":\"2026-01-01T00:00:00Z\"}");
        }
    }
}
