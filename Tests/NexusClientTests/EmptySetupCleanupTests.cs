using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Nexus.Client;
using Nexus.Client.GameStorage;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Settings;
using NUnit.Framework;

namespace NexusClientTests
{
    /// <summary>Exercises explicit cleanup against real empty native stores without initializing a game session.</summary>
    [TestFixture]
    public class EmptySetupCleanupTests
    {
        private string _root;
        private Nexus.Client.Properties.Settings _settings;
        private GameStorageService _service;
        private GameStoragePathSet _paths;

        /// <summary>Builds independent game and storage folders, including both SQLite native authorities.</summary>
        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "nmm-empty-setup-" + Guid.NewGuid().ToString("N"));
            string storage = Path.Combine(_root, "Storage");
            _paths = new GameStoragePathSet { GameId = "Example", GameName = "Example", GameInstallPath = Path.Combine(_root, "Game"),
                InstallInfoPath = Path.Combine(storage, "Install Info"), ModsPath = Path.Combine(storage, "Mods"), VirtualInstallPath = Path.Combine(storage, "VirtualInstall") };
            foreach (string directory in new[] { _paths.GameInstallPath, _paths.InstallInfoPath, _paths.ModsPath, _paths.VirtualInstallPath }) Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(_paths.GameInstallPath, "game.exe"), "keep game");
            _settings = new Nexus.Client.Properties.Settings { InstallationPaths = new PerGameModeSettings<string>(), ExecutablePaths = new PerGameModeSettings<string>(),
                InstallInfoFolder = new PerGameModeSettings<string>(), ModFolder = new PerGameModeSettings<string>(), VirtualFolder = new PerGameModeSettings<string>(), HDLinkFolder = new PerGameModeSettings<string>() };
            _settings.InstallationPaths["Example"] = _paths.GameInstallPath;
            _settings.ExecutablePaths["Example"] = _paths.GameInstallPath;
            _settings.InstallInfoFolder["Example"] = _paths.InstallInfoPath;
            _settings.ModFolder["Example"] = _paths.ModsPath;
            _settings.VirtualFolder["Example"] = storage;
            IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
            {
                if (method.Name == "get_Settings") return _settings;
                if (method.Name == "get_ApplicationPersonalDataFolderPath") return Path.Combine(_root, "Personal");
                if (method.Name == "get_ApplicationVersion") return new Version(9, 0);
                return null;
            });
            _service = new GameStorageService(environment);
            string log = Path.Combine(_paths.InstallInfoPath, "InstallLog.xml");
            InstallLogSqliteStore.CreateFromDocument(Path.ChangeExtension(log, ".sqlite"), new XDocument(new XElement("installLog", new XAttribute("fileVersion", InstallLog.CurrentVersion), new XElement("modList"))));
            NativeStoreAuthority.Mark(Path.ChangeExtension(log, ".sqlite"));
            string virtualXml = Path.Combine(_paths.VirtualInstallPath, "VirtualModConfig.xml");
            var store = new SQLiteVirtualModStore(virtualXml, Path.ChangeExtension(virtualXml, ".sqlite"), VirtualModActivator.CurrentVersion, version => version == VirtualModActivator.CurrentVersion, new XmlVirtualModStore());
            store.Save(VirtualModActivator.CurrentVersion, virtualXml, new List<IVirtualModInfo>(), new IVirtualModLink[0]);
            _service.InitializeMetadataForStorage(_paths);
        }

        /// <summary>Removes only this fixture's temporary files after its stores have been closed.</summary>
        [TearDown]
        public void TearDown() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

        /// <summary>Empty native databases, manifests and registry claims are removed; unrelated games and unknown JSON fields survive.</summary>
        [Test]
        public void EmptySetup_RemovesNativeFilesAndBothRegistryClaims()
        {
            foreach (string registryPath in new[] { _service.RegistryPath, _service.LastKnownGoodPath })
            {
                JObject registry = JObject.Parse(File.ReadAllText(registryPath));
                registry["futureProperty"] = "keep";
                ((JArray)registry["KnownStorages"]).Add(JObject.FromObject(new GameStorageRegistryEntry { GameId = "Other", StorageId = "other-id", ModsPath = Path.Combine(_root, "Other") }));
                File.WriteAllText(registryPath, registry.ToString());
            }
            string[] before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
            EmptySetupCleanupPlan plan = _service.PlanEmptySetupCleanup("Example");
            Assert.That(plan.HasChanges, Is.True);
            Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(before), "Preview must not mutate storage.");
            bool saved = false;
            plan.Execute(() => saved = true);
            Assert.That(saved, Is.True);
            Assert.That(Directory.Exists(Path.GetDirectoryName(_paths.ModsPath)), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_paths.GameInstallPath, "game.exe")), Is.EqualTo("keep game"));
            foreach (string registryPath in new[] { _service.RegistryPath, _service.LastKnownGoodPath })
            {
                JObject registry = JObject.Parse(File.ReadAllText(registryPath));
                Assert.That((string)registry["futureProperty"], Is.EqualTo("keep"));
                var entries = registry["KnownStorages"].ToObject<GameStorageRegistryEntry[]>();
                Assert.That(entries.Select(entry => entry.GameId), Is.EqualTo(new[] { "Other" }));
                Assert.That(registry["ActiveStorageByGame"]["Example"], Is.Null);
            }
        }

        /// <summary>Direct Install topology beneath InstallInfo is verified and its null-virtual registry binding is retired.</summary>
        [Test]
        public void DirectInstall_CleansFallbackTopologyAndRegistry()
        {
            string direct = Path.Combine(_root, "Direct");
            string info = Path.Combine(direct, "Install Info");
            string mods = Path.Combine(direct, "Mods");
            string topology = Path.Combine(info, "VirtualInstall");
            Directory.CreateDirectory(topology);
            Directory.CreateDirectory(mods);
            File.WriteAllText(Path.Combine(info, "InstallLog.xml"), "<installLog fileVersion='0.6.0.0'><modList/></installLog>");
            File.WriteAllText(Path.Combine(topology, "VirtualModConfig.xml"), "<virtualModActivator fileVersion='0.3.0.0'><modList/></virtualModActivator>");
            _settings.InstallInfoFolder["Example"] = info;
            _settings.ModFolder["Example"] = mods;
            _settings.VirtualFolder.Remove("Example");
            _service.InitializeMetadataForStorage(new GameStoragePathSet { GameId = "Example", GameName = "Example", GameInstallPath = _paths.GameInstallPath,
                InstallInfoPath = info, ModsPath = mods });
            EmptySetupCleanupPlan plan = _service.PlanEmptySetupCleanup("Example");
            Assert.That(plan.HasChanges, Is.True);
            plan.Execute(() => { });
            Assert.That(Directory.Exists(info), Is.False);
            Assert.That(Directory.Exists(mods), Is.False);
            foreach (string registry in new[] { _service.RegistryPath, _service.LastKnownGoodPath })
                Assert.That(JsonConvert.DeserializeObject<GameStorageRegistry>(File.ReadAllText(registry)).KnownStorages.Any(entry => entry.GameId == "Example"), Is.False);
        }

        /// <summary>Unreadable stores, real mod state, recovery files and shared mode bindings prevent destructive cleanup.</summary>
        [TestCase("payload")]
        [TestCase("shared-settings")]
        [TestCase("shared-manifest")]
        [TestCase("authority-missing")]
        [TestCase("future-registry")]
        [TestCase("native-mod")]
        [TestCase("missing-native-directory")]
        public void UnsafeSetup_IsRetained(string problem)
        {
            string database = Path.Combine(_paths.InstallInfoPath, "InstallLog.sqlite");
            if (problem == "payload") File.WriteAllText(Path.Combine(_paths.VirtualInstallPath, "keep.esp"), "mod");
            if (problem == "shared-settings") _settings.InstallInfoFolder["Other"] = _paths.InstallInfoPath;
            if (problem == "shared-manifest")
            {
                string file = Path.Combine(_paths.InstallInfoPath, ".nmm-folder.json");
                var manifest = JsonConvert.DeserializeObject<GameStorageFolderManifest>(File.ReadAllText(file));
                manifest.Bindings.Add(new GameStorageFolderBinding { GameId = "Other", StorageId = manifest.StorageId });
                File.WriteAllText(file, JsonConvert.SerializeObject(manifest));
            }
            if (problem == "authority-missing") File.Delete(database);
            if (problem == "missing-native-directory") Directory.Delete(_paths.InstallInfoPath, true);
            if (problem == "future-registry") File.WriteAllText(_service.RegistryPath, "{\"SchemaVersion\":999}");
            if (problem == "native-mod")
                using (var connection = new System.Data.SQLite.SQLiteConnection("Data Source=" + database + ";Pooling=False"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "INSERT INTO Mods VALUES ('user','user',0,'archive.7z','mod','','','',NULL,NULL,0)";
                        command.ExecuteNonQuery();
                    }
                }
            var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            EmptySetupCleanupPlan plan = _service.PlanEmptySetupCleanup("Example");
            Assert.That(plan.HasChanges, Is.False);
            Assert.That(plan.Retained, Is.Not.Empty);
            Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(files.Keys));
            foreach (var file in files) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
        }

        /// <summary>A failed settings save restores every removed native file and both registries byte for byte.</summary>
        [Test]
        public void FailedSave_RestoresNativeFilesAndRegistryBytes()
        {
            var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            EmptySetupCleanupPlan plan = _service.PlanEmptySetupCleanup("Example");
            Assert.That(plan.HasChanges, Is.True);
            Assert.Throws<IOException>(() => plan.Execute(() => { throw new IOException("save failed"); }));
            Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.EquivalentTo(files.Keys));
            foreach (var file in files) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
        }

        /// <summary>Files added or changed after the preview abort cleanup before settings are forgotten.</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ChangedPreview_PreservesFilesAndSettings(bool newFile)
        {
            EmptySetupCleanupPlan plan = _service.PlanEmptySetupCleanup("Example");
            string file = Path.Combine(_paths.VirtualInstallPath, newFile ? "new.esp" : "VirtualModConfig.sqlite.authority");
            File.WriteAllText(file, "changed after preview");
            bool saved = false;
            Assert.Throws<IOException>(() => plan.Execute(() => saved = true));
            Assert.That(saved, Is.False);
            Assert.That(File.ReadAllText(file), Is.EqualTo("changed after preview"));
            Assert.That(File.Exists(Path.Combine(_paths.InstallInfoPath, "InstallLog.sqlite")), Is.True);
        }

        /// <summary>Downloaded archives and customized categories remain available even when empty native state is removed.</summary>
        [Test]
        public void LibraryData_IsRetainedWhileEmptyNativeStateIsCleaned()
        {
            string archive = Path.Combine(_paths.ModsPath, "keep.7z");
            File.WriteAllText(archive, "download");
            EmptySetupCleanupPlan plan = _service.PlanEmptySetupCleanup("Example");
            Assert.That(plan.HasChanges, Is.True);
            plan.Execute(() => { });
            Assert.That(File.ReadAllText(archive), Is.EqualTo("download"));
            Assert.That(Directory.Exists(_paths.InstallInfoPath), Is.False);
            Assert.That(plan.Retained.Any(path => path.Contains(_paths.ModsPath)), Is.True);
        }
    }
}
