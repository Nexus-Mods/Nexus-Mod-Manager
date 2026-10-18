using System;
using System.IO;
using Nexus.Client;
using Nexus.Client.GameStorage;
using Nexus.Client.Settings;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public sealed class Witcher3GameStorageWriteGuardTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "nmm-w3r-storage-write-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private GameStoragePathSet Paths(string modeId)
        {
            return new GameStoragePathSet
            {
                GameId = modeId,
                GameName = "The Witcher 3",
                GameInstallPath = Path.Combine(_root, "Game"),
                InstallInfoPath = Path.Combine(_root, "InstallInfo"),
                ModsPath = Path.Combine(_root, "Mods"),
                VirtualInstallPath = Path.Combine(_root, "Virtual", "VirtualInstall")
            };
        }

        private void CreateStorageDirectories(GameStoragePathSet paths)
        {
            Directory.CreateDirectory(paths.GameInstallPath);
            Directory.CreateDirectory(paths.InstallInfoPath);
            Directory.CreateDirectory(paths.ModsPath);
            Directory.CreateDirectory(paths.VirtualInstallPath);
        }

        [TestCase("Witcher3")]
        [TestCase("Witcher3Remastered")]
        public void DirectMetadataInitializationWithoutActiveSettingsIsRejectedBeforeWriting(string modeId)
        {
            var paths = Paths(modeId);
            CreateStorageDirectories(paths);
            var service = new GameStorageService(Path.Combine(_root, "Registry"), new Version(0, 93, 1));

            var error = Assert.Throws<InvalidOperationException>(() => service.InitializeMetadataForStorage(paths));
            Assert.That(error.Message, Does.Contain("Witcher 3 Game Storage write blocked"));
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(service.RegistryPath), Is.False);
        }

        [Test]
        public void DirectLegacyManifestRepairWithoutActiveSettingsIsRejectedWithoutChangingMetadata()
        {
            var paths = Paths("Witcher3");
            // Reconstruct the recognizable old collision: Mods holds VirtualInstall's manifest.
            paths.ModsPath = Path.Combine(_root, "Data");
            paths.VirtualInstallPath = Path.Combine(paths.ModsPath, "VirtualInstall");
            CreateStorageDirectories(paths);
            const string id = "old-witcher-storage";
            string wrong = Path.Combine(paths.ModsPath, ".nmm-folder.json");
            string info = Path.Combine(paths.InstallInfoPath, ".nmm-folder.json");
            string wrongText = JsonConvert.SerializeObject(new GameStorageFolderManifest
            {
                SchemaVersion = 2,
                FolderRole = GameStorageFolderRole.VirtualInstall,
                GameId = "Witcher3",
                StorageId = id
            });
            // The guard must run before even interpreting a legacy manifest.
            File.WriteAllText(wrong, wrongText);
            File.WriteAllText(info, JsonConvert.SerializeObject(new GameStorageFolderManifest
            {
                SchemaVersion = 2,
                FolderRole = GameStorageFolderRole.InstallInfo,
                GameId = "Witcher3",
                StorageId = id
            }));
            var service = new GameStorageService(Path.Combine(_root, "Registry"), new Version(0, 93, 1));
            byte[] before = File.ReadAllBytes(wrong);

            Assert.That(service.RepairKnownLegacyStorageMetadata(paths), Is.False);
            Assert.That(File.ReadAllBytes(wrong), Is.EqualTo(before));
            Assert.That(File.Exists(Path.Combine(paths.VirtualInstallPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(service.RegistryPath), Is.False);
        }

        [Test]
        public void RemovingHistoricalWitcherNativeBindingDoesNotEraseRegistryEvidence()
        {
            var service = new GameStorageService(Path.Combine(_root, "Registry"), new Version(0, 93, 1));
            Directory.CreateDirectory(service.RegistryDirectory);
            const string original = "{\"SchemaVersion\":1,\"KnownStorages\":[{\"GameId\":\"Witcher3\"," +
                "\"StorageId\":\"older-store\",\"LastKnownInstallLogPresent\":true}]}";
            File.WriteAllText(service.RegistryPath, original);

            Assert.That(service.RemoveStorageBinding("Witcher3", "older-store"), Is.False);
            Assert.That(File.ReadAllText(service.RegistryPath), Is.EqualTo(original));
        }

        [Test]
        public void RemovingUnrecordedWitcherVirtualPayloadBindingIsBlocked()
        {
            var paths = Paths("Witcher3Remastered");
            CreateStorageDirectories(paths);
            File.WriteAllText(Path.Combine(paths.VirtualInstallPath, "orphaned-mod.data"), "keep");
            var service = new GameStorageService(Path.Combine(_root, "Registry"), new Version(0, 93, 1));
            Directory.CreateDirectory(service.RegistryDirectory);
            string original = JsonConvert.SerializeObject(new GameStorageRegistry
            {
                KnownStorages = new System.Collections.Generic.List<GameStorageRegistryEntry>
                {
                    new GameStorageRegistryEntry
                    {
                        GameId = "Witcher3Remastered",
                        StorageId = "orphan",
                        InstallInfoPath = paths.InstallInfoPath,
                        VirtualInstallPath = paths.VirtualInstallPath
                    }
                }
            });
            File.WriteAllText(service.RegistryPath, original);

            Assert.That(service.RemoveStorageBinding("Witcher3Remastered", "orphan"), Is.False);
            Assert.That(File.ReadAllText(service.RegistryPath), Is.EqualTo(original));
            Assert.That(File.Exists(Path.Combine(paths.VirtualInstallPath, "orphaned-mod.data")), Is.True);
        }

        [Test]
        public void EmptyWitcherMetadataOnlyBindingMayStillBeRemoved()
        {
            var service = new GameStorageService(Path.Combine(_root, "Registry"), new Version(0, 93, 1));
            Directory.CreateDirectory(service.RegistryDirectory);
            var registry = new GameStorageRegistry();
            registry.KnownStorages.Add(new GameStorageRegistryEntry
            {
                GameId = "Witcher3",
                StorageId = "metadata-only"
            });
            File.WriteAllText(service.RegistryPath, JsonConvert.SerializeObject(registry));

            Assert.That(service.RemoveStorageBinding("Witcher3", "metadata-only"), Is.True);
            Assert.That(service.GetKnownNativeOwnershipLocations("Witcher3"), Is.Empty);
        }

        private IEnvironmentInfo EnvironmentForOtherMode(string otherMode, string otherGame,
            string otherInfo, string otherVirtual, PerGameModeSettings<string> gameRoots)
        {
            gameRoots[otherMode] = otherGame;
            var infos = new PerGameModeSettings<string>();
            infos[otherMode] = otherInfo;
            var virtualFolders = new PerGameModeSettings<string>();
            virtualFolders[otherMode] = otherVirtual;
            var setup = new PerGameModeSettings<bool>();
            setup[otherMode] = false;
            ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_InstallationPaths": return gameRoots;
                    case "get_InstallInfoFolder": return infos;
                    case "get_VirtualFolder": return virtualFolders;
                    case "get_CompletedSetup": return setup;
                    default: return null;
                }
            });
            return InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
            {
                if (method.Name == "get_Settings") return settings;
                if (method.Name == "get_ApplicationPersonalDataFolderPath") return _root;
                if (method.Name == "get_ApplicationVersion") return new Version(0, 93, 1);
                return null;
            });
        }

        [Test]
        public void DirectInitializationRejectsSharedVirtualStoreBeforeMetadataIsWrittenOnWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Physical Windows path identity required");
            var paths = Paths("Witcher3Remastered");
            CreateStorageDirectories(paths);
            string otherGame = Path.Combine(_root, "LegacyGame");
            Directory.CreateDirectory(otherGame);
            var roots = new PerGameModeSettings<string>();
            var environment = EnvironmentForOtherMode("Witcher3", otherGame,
                Path.Combine(_root, "LegacyInfo"), Path.GetDirectoryName(paths.VirtualInstallPath), roots);
            var service = new GameStorageService(environment);

            Assert.Throws<InvalidOperationException>(() => service.InitializeMetadataForStorage(paths));
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.False);
            Assert.That(File.Exists(service.RegistryPath), Is.False);
        }

        [Test]
        public void RechecksCounterpartAtDirectWriteBoundaryAfterInitialSuccessfulInitializationOnWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Physical Windows path identity required");
            var paths = Paths("Witcher3Remastered");
            CreateStorageDirectories(paths);
            string otherGame = Path.Combine(_root, "LegacyGame");
            Directory.CreateDirectory(otherGame);
            var roots = new PerGameModeSettings<string>();
            var environment = EnvironmentForOtherMode("Witcher3", otherGame,
                Path.Combine(_root, "LegacyInfo"), Path.Combine(_root, "LegacyVirtual"), roots);
            var service = new GameStorageService(environment);

            service.InitializeMetadataForStorage(paths);
            string manifestPath = Path.Combine(paths.InstallInfoPath, ".nmm-folder.json");
            Assert.That(File.Exists(manifestPath), Is.True);
            byte[] beforeManifest = File.ReadAllBytes(manifestPath);
            byte[] beforeRegistry = File.ReadAllBytes(service.RegistryPath);

            // The other mode is reconfigured to this same physical game after the
            // first successful metadata write. The next write must not trust startup.
            roots["Witcher3"] = paths.GameInstallPath;
            Assert.Throws<InvalidOperationException>(() => service.InitializeMetadataForStorage(paths));
            Assert.That(File.ReadAllBytes(manifestPath), Is.EqualTo(beforeManifest));
            Assert.That(File.ReadAllBytes(service.RegistryPath), Is.EqualTo(beforeRegistry));
        }

        [Test]
        public void DirectRepairRejectsConfiguredCounterpartCollisionWithoutChangingManifestOnWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Physical Windows path identity required");
            var paths = Paths("Witcher3Remastered");
            CreateStorageDirectories(paths);
            var roots = new PerGameModeSettings<string>();
            var environment = EnvironmentForOtherMode("Witcher3", paths.GameInstallPath,
                Path.Combine(_root, "LegacyInfo"), Path.Combine(_root, "LegacyVirtual"), roots);
            var service = new GameStorageService(environment);
            string manifestPath = Path.Combine(paths.InstallInfoPath, ".nmm-folder.json");
            const string original = "original metadata must remain unchanged";
            File.WriteAllText(manifestPath, original);

            Assert.That(service.RepairKnownLegacyStorageMetadata(paths), Is.False);
            Assert.That(File.ReadAllText(manifestPath), Is.EqualTo(original));
            Assert.That(File.Exists(service.RegistryPath), Is.False);
        }
    }
}
