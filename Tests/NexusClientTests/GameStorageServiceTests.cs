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
        public void ValidateStorage_ValidLegacyFolders_InitializesMetadata()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA", withArchive: true, withVirtualFile: true);

            var result = _service.ValidateStorage(paths, true);

            Assert.IsTrue(result.IsHealthy);
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(paths.ModsPath, ".nmm-folder.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "StorageA", "NMMStorage.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(_tempRoot, "Registry", "storages.json")), Is.True);
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
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.MismatchedGame), Is.True);
        }

        [Test]
        public void LinkFolderRules_RequireGameDriveForHardlinks()
        {
            Assert.IsTrue(_service.IsLinkFolderRequired(@"D:\NMM\Virtual", @"C:\Games\Skyrim"));
            Assert.IsFalse(_service.IsLinkFolderOnGameDrive(@"D:\NMM\Links", @"C:\Games\Skyrim"));
            Assert.IsTrue(_service.IsLinkFolderOnGameDrive(@"C:\NMM\Links", @"C:\Games\Skyrim"));
        }

        [Test]
        public void DiscoverRecoveryCandidatesFromRoot_RootManifestCreatesHighConfidenceCandidate()
        {
            var paths = CreateStorage("SkyrimSE", "StorageA");
            WriteRootManifest(Path.Combine(_tempRoot, "StorageA"), "SkyrimSE", "StorageA");

            var candidates = _service.DiscoverRecoveryCandidatesFromRoot(paths, Path.Combine(_tempRoot, "StorageA"));

            var candidate = candidates.FirstOrDefault(x => x.CandidateKind == "Root manifest");
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
            _service.ValidateStorage(knownGood, true);

            var empty = CreateStorage("SkyrimSE", "StorageB");
            var result = _service.ValidateStorage(empty, false);

            Assert.IsFalse(result.IsHealthy);
            Assert.That(result.Items.Any(x => x.Status == GameStorageHealthStatus.SuspiciousEmptyFolder), Is.True);
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

            var candidate = candidates.FirstOrDefault(x => x.CandidateKind == "Current configuration");
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

        private static void WriteFolderManifest(string folder, string gameId, string storageId, string role)
        {
            File.WriteAllText(Path.Combine(folder, ".nmm-folder.json"),
                "{\"SchemaVersion\":1,\"App\":\"Nexus Mod Manager\",\"FolderRole\":\"" + role + "\",\"StorageId\":\"" + storageId + "\",\"GameId\":\"" + gameId + "\",\"CreatedUtc\":\"2026-01-01T00:00:00Z\",\"LastSeenUtc\":\"2026-01-01T00:00:00Z\"}");
        }

        private static void WriteRootManifest(string root, string gameId, string storageId)
        {
            File.WriteAllText(Path.Combine(root, "NMMStorage.json"),
                "{\"SchemaVersion\":1,\"App\":\"Nexus Mod Manager\",\"StorageId\":\"" + storageId + "\",\"GameId\":\"" + gameId + "\",\"GameName\":\"" + gameId + "\",\"Folders\":{\"InstallInfo\":\"InstallInfo\",\"Mods\":\"Mods\",\"VirtualInstall\":\"VirtualInstall\"},\"LinkFolderRequired\":false,\"CreatedUtc\":\"2026-01-01T00:00:00Z\",\"LastSeenUtc\":\"2026-01-01T00:00:00Z\"}");
        }
    }
}
