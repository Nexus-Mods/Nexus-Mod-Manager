using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Nexus.Client;
using Nexus.Client.CollectionManagement;
using Nexus.Client.GameStorage;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>Checks that the common warning policy permits selected paths for every game after explicit Continue.</summary>
    [TestFixture]
    public class GameStorageUserChoiceTests
    {
        private string _root;
        private Nexus.Client.Properties.Settings _values;
        private IEnvironmentInfo _environment;
        private GameStorageService _service;

        /// <summary>Creates isolated settings whose Save call never writes to a real user's configuration.</summary>
        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "nmm-storage-choice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _values = new Nexus.Client.Properties.Settings { InstallationPaths = new PerGameModeSettings<string>(),
                ExecutablePaths = new PerGameModeSettings<string>(), InstallInfoFolder = new PerGameModeSettings<string>(),
                ModFolder = new PerGameModeSettings<string>(), VirtualFolder = new PerGameModeSettings<string>(),
                HDLinkFolder = new PerGameModeSettings<string>(), MultiHDInstall = new PerGameModeSettings<bool>(),
                PreferredInstallMethod = new PerGameModeSettings<string>() };
            ISettings memory = InterfaceStub<ISettings>.Create((method, args) => method.Name == "Save" ? null : method.Invoke(_values, args));
            _environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
            {
                if (method.Name == "get_Settings") return memory;
                if (method.Name == "get_ApplicationPersonalDataFolderPath") return Path.Combine(_root, "Personal");
                if (method.Name == "get_ApplicationVersion") return new Version(0, 93, 11);
                return null;
            });
            _service = new GameStorageService(_environment);
        }

        /// <summary>Removes only this fixture's isolated data.</summary>
        [TearDown]
        public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        /// <summary>Builds the same selected directory layout for each game ID, independently of its runtime implementation.</summary>
        private GameStoragePathSet Paths(string modeId)
        {
            var paths = new GameStoragePathSet { GameId = modeId, GameName = modeId, GameInstallPath = Path.Combine(_root, "Game"),
                InstallInfoPath = Path.Combine(_root, "Storage", "Info"), ModsPath = Path.Combine(_root, "Storage", "Mods"),
                VirtualInstallPath = Path.Combine(_root, "Storage", "VirtualInstall") };
            foreach (string folder in new[] { paths.GameInstallPath, paths.InstallInfoPath, paths.ModsPath, paths.VirtualInstallPath }) Directory.CreateDirectory(folder);
            _values.InstallationPaths[modeId] = paths.GameInstallPath;
            return paths;
        }

        /// <summary>Cross-game manifests warn, then the selected paths remain usable by native operations without rewriting the original metadata.</summary>
        [TestCase("SkyrimSE")]
        [TestCase("SkyrimGOG")]
        [TestCase("Fallout4")]
        [TestCase("Starfield")]
        [TestCase("NoMansSky")]
        [TestCase("Witcher3Remastered")]
        public void ContinueAcceptsForeignMetadataForEveryGame(string modeId)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows physical identity API");
            GameStoragePathSet paths = Paths(modeId);
            foreach (var role in new[] { new { Path = paths.InstallInfoPath, Role = GameStorageFolderRole.InstallInfo },
                new { Path = paths.ModsPath, Role = GameStorageFolderRole.Mods }, new { Path = paths.VirtualInstallPath, Role = GameStorageFolderRole.VirtualInstall } })
                File.WriteAllText(Path.Combine(role.Path, ".nmm-folder.json"), JsonConvert.SerializeObject(new GameStorageFolderManifest {
                    SchemaVersion = 2, GameId = "OtherGame", StorageId = "original-ledger", FolderRole = role.Role }));
            _values.InstallInfoFolder["OtherGame"] = paths.InstallInfoPath;
            _values.ModFolder["OtherGame"] = paths.ModsPath;
            _values.VirtualFolder["OtherGame"] = Path.GetDirectoryName(paths.VirtualInstallPath);
            var original = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
            Assert.That(_service.InspectStorageWarnings(paths).HasWarnings, Is.True);
            Assert.Throws<CollectionTargetAuthorityException>(() => new CollectionTargetIdentityResolver(_service).Resolve(paths));
            Action proceed = GameStorageSessionConsent.CreateStorageApproval(_environment, paths);
            Assert.That(_service.HasApprovedStoragePaths(paths), Is.False, "Opening the warning or choosing Cancel is not consent.");
            proceed();
            GameStorageHealthCheck health;
            var candidate = new GameStorageCandidate { GameId = modeId, InstallInfoPath = paths.InstallInfoPath,
                ModsPath = paths.ModsPath, VirtualInstallPath = paths.VirtualInstallPath };
            Assert.That(_service.ApplySelectedCandidatePaths(paths, candidate, out health), Is.True);
            Assert.That(health.HasConfirmedPaths, Is.True);
            Assert.That(_values.InstallInfoFolder[modeId], Is.EqualTo(paths.InstallInfoPath));
            Assert.That(new CollectionTargetIdentityResolver(_service).Resolve(paths).StorageId, Is.EqualTo("original-ledger"));
            foreach (var file in original) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
        }

        /// <summary>Suspicious empty folders are advisory after Continue for unrelated game implementations too.</summary>
        [TestCase("SkyrimSE")]
        [TestCase("Fallout4")]
        [TestCase("Starfield")]
        public void ContinueAcceptsSuspiciousEmptySelection(string modeId)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows physical identity API");
            GameStoragePathSet paths = Paths(modeId);
            Directory.CreateDirectory(_service.RegistryDirectory);
            File.WriteAllText(_service.RegistryPath, JsonConvert.SerializeObject(new GameStorageRegistry {
                KnownStorages = new System.Collections.Generic.List<GameStorageRegistryEntry> { new GameStorageRegistryEntry {
                    GameId = modeId, StorageId = "prior-store", InstallInfoPath = paths.InstallInfoPath, ModsPath = paths.ModsPath,
                    VirtualInstallPath = paths.VirtualInstallPath, LastKnownGood = true, LastKnownArchiveCount = 5, LastKnownInstallLogPresent = true, LastKnownVirtualFileCount = 1 } } }));
            Assert.That(_service.ValidateStorage(paths).IsUsable, Is.False);
            GameStorageSessionConsent.CreateStorageApproval(_environment, paths)();
            GameStorageHealthCheck health;
            Assert.That(_service.ConfirmSelectedStoragePaths(paths, out health), Is.True);
            Assert.That(health.HasConfirmedPaths, Is.True);
            Assert.That(new CollectionTargetIdentityResolver(_service).Resolve(paths).StorageId, Is.EqualTo("prior-store"));
        }

        /// <summary>Bad optional registry files remain unchanged and cannot re-block a reviewed directory choice.</summary>
        [TestCase("{broken")]
        [TestCase("{\"SchemaVersion\":999,\"futureProperty\":\"keep\"}")]
        public void ContinuePreservesUnreadableRegistryAndStableNativeIdentity(string registryText)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows physical identity API");
            GameStoragePathSet paths = Paths("Fallout4");
            Directory.CreateDirectory(_service.RegistryDirectory);
            File.WriteAllText(_service.RegistryPath, registryText);
            Assert.That(_service.InspectStorageWarnings(paths).HasWarnings, Is.True);
            GameStorageSessionConsent.CreateStorageApproval(_environment, paths)();
            GameStorageHealthCheck health;
            Assert.That(_service.ConfirmSelectedStoragePaths(paths, out health), Is.True);
            Assert.That(health.HasConfirmedPaths, Is.True);
            string identity = new CollectionTargetIdentityResolver(_service).Resolve(paths).StorageId;
            Assert.That(new CollectionTargetIdentityResolver(new GameStorageService(_environment)).Resolve(paths).StorageId, Is.EqualTo(identity));
            Assert.That(File.ReadAllText(_service.RegistryPath), Is.EqualTo(registryText));
            Assert.That(File.Exists(Path.Combine(paths.InstallInfoPath, ".nmm-folder.json")), Is.False);
        }

        /// <summary>Overlapping game roots warn for unverified native data without modifying either setup or rejecting empty stores.</summary>
        [TestCase("empty", false)]
        [TestCase("install-info-payload", true)]
        [TestCase("virtual-payload", true)]
        [TestCase("invalid-log", true)]
        [TestCase("installed-mod", true)]
        public void OverlappingGameRoot_InspectsOtherSetupWithoutThrowing(string state, bool expectWarning)
        {
            GameStoragePathSet paths = Paths("SkyrimGOG");
            string otherInfo = Path.Combine(_root, "OtherStorage", "Info");
            string otherVirtualParent = Path.Combine(_root, "OtherStorage");
            string otherVirtual = Path.Combine(otherVirtualParent, "VirtualInstall");
            Directory.CreateDirectory(otherInfo);
            Directory.CreateDirectory(otherVirtual);
            _values.InstallationPaths["SkyrimSE"] = paths.GameInstallPath;
            _values.InstallInfoFolder["SkyrimSE"] = otherInfo;
            _values.VirtualFolder["SkyrimSE"] = otherVirtualParent;
            if (state == "install-info-payload") File.WriteAllText(Path.Combine(otherInfo, "keep.txt"), "user data");
            if (state == "virtual-payload") File.WriteAllText(Path.Combine(otherVirtual, "keep.esp"), "mod payload");
            if (state == "invalid-log") File.WriteAllText(Path.Combine(otherInfo, "InstallLog.xml"), "<broken");
            if (state == "installed-mod") File.WriteAllText(Path.Combine(otherInfo, "InstallLog.xml"),
                "<installLog fileVersion='0.6.0.0'><modList><mod key='user' path='archive.7z'/></modList></installLog>");
            var original = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
            string[] directories = Directory.GetDirectories(_root, "*", SearchOption.AllDirectories);

            Assert.That(_service.IsNativeSetupVerifiablyEmpty("SkyrimSE", otherInfo, otherVirtualParent), Is.EqualTo(!expectWarning));
            GameStorageHealthCheck health = _service.InspectStorageWarnings(paths);
            Assert.That(health.Items.Any(item => item.Status == GameStorageHealthStatus.Warning && item.Message.Contains("SkyrimSE")), Is.EqualTo(expectWarning));
            Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(original.Keys));
            foreach (var file in original) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
            Assert.That(Directory.GetDirectories(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(directories));
            Assert.That(_values.InstallationPaths["SkyrimSE"], Is.EqualTo(paths.GameInstallPath));
            Assert.That(_values.InstallInfoFolder["SkyrimSE"], Is.EqualTo(otherInfo));
            Assert.That(_values.VirtualFolder["SkyrimSE"], Is.EqualTo(otherVirtualParent));
        }

        /// <summary>A link-folder drive warning cannot reject a saved selection after Continue.</summary>
        [TestCase("SkyrimSE")]
        [TestCase("Fallout4")]
        public void ContinueAcceptsLinkFolderDriveWarning(string modeId)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Ignore("Windows physical identity API");
            GameStoragePathSet paths = Paths(modeId);
            string selectedDrive = Path.GetPathRoot(paths.InstallInfoPath);
            string gameDrive = selectedDrive.StartsWith("Z:", StringComparison.OrdinalIgnoreCase) ? "Y:" : "Z:";
            paths.GameInstallPath = gameDrive + "\\NMM-choice-" + Guid.NewGuid().ToString("N");
            paths.LinkFolderPath = Path.Combine(_root, "SelectedLinkFolder");
            paths.LinkFolderRequired = true;
            Directory.CreateDirectory(paths.LinkFolderPath);
            _values.InstallationPaths[modeId] = paths.GameInstallPath;
            Assert.That(_service.ValidateStorage(paths).Items.Any(item => item.Status == GameStorageHealthStatus.LinkFolderOnWrongDrive), Is.True);
            GameStorageHealthCheck health;
            Assert.That(_service.ConfirmSelectedStoragePaths(paths, out health), Is.False);
            GameStorageSessionConsent.CreateStorageApproval(_environment, paths)();
            Assert.That(_service.ConfirmSelectedStoragePaths(paths, out health), Is.True);
            Assert.That(health.HasConfirmedPaths, Is.True);
            Assert.That(Directory.Exists(paths.GameInstallPath), Is.False, "Accepting storage does not create a game installation.");
        }

        /// <summary>Accepted warnings are specific to selected folders, relevant other setups, and the current session.</summary>
        [Test]
        public void ChangedSelectionAndResetDoNotReuseConsent()
        {
            GameStoragePathSet paths = Paths("Starfield");
            _values.InstallInfoFolder["OtherGame"] = paths.InstallInfoPath;
            Action proceed = GameStorageSessionConsent.CreateStorageApproval(_environment, paths);
            Assert.That(_service.HasApprovedStoragePaths(paths), Is.False);
            proceed();
            Assert.That(_service.HasApprovedStoragePaths(paths), Is.True);
            string original = paths.ModsPath;
            paths.ModsPath += "Changed";
            Assert.That(_service.HasApprovedStoragePaths(paths), Is.False);
            paths.ModsPath = original;
            _values.InstallInfoFolder["OtherGame"] += "Changed";
            Assert.That(_service.HasApprovedStoragePaths(paths), Is.False);
            _values.InstallInfoFolder["OtherGame"] = paths.InstallInfoPath;
            GameStorageSessionConsent.Revoke(_environment.Settings, paths.GameId);
            Assert.That(_service.HasApprovedStoragePaths(paths), Is.False);
        }
    }
}
