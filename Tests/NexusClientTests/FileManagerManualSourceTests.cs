namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nexus.Client.ModManagement;

    using NUnit.Framework;

    public class FileManagerManualSourceTests
    {
        [Test]
        public void UntrackedRowsExposeSourceDropdown()
        {
            FileManagerRow row = CreateRow("textures\\loose.dds");

            FileManagerQueryService.ApplySourceClassification(row, null, EmptyBaseFiles(), EmptyManualSources());

            Assert.AreEqual(FileManagerSource.Untracked, row.Source);
            Assert.IsTrue(row.SourceEditable);
        }

        [Test]
        public void ManuallyClassifiedRowsRemainEditable()
        {
            FileManagerRow row = CreateRow("textures\\creation.dds");
            Dictionary<string, FileManagerSource> manualSources = new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase)
            {
                { row.NormalizedRelativePath, FileManagerSource.Creations }
            };

            FileManagerQueryService.ApplySourceClassification(row, null, EmptyBaseFiles(), manualSources);

            Assert.AreEqual(FileManagerSource.Creations, row.Source);
            Assert.IsTrue(row.SourceEditable);
        }

        [Test]
        public void NmmOwnedRowsAreReadOnly()
        {
            FileManagerRow row = CreateRow("meshes\\weapon.nif");
            List<IVirtualModLink> links = new List<IVirtualModLink>
            {
                new TestVirtualModLink(row.RelativePath, true, 0, new TestVirtualModInfo("weapon.7z", "Weapon Mod"))
            };

            FileManagerQueryService.ApplySourceClassification(row, links, EmptyBaseFiles(), EmptyManualSources());

            Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
            Assert.IsFalse(row.SourceEditable);
            Assert.IsTrue(row.OwnerEditable);
        }

        [Test]
        public void AutomaticallyRecognizedBaseGameRowsAreReadOnly()
        {
            FileManagerRow row = CreateRow("falloutnv.esm");
            HashSet<string> baseFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.NormalizedRelativePath };

            FileManagerQueryService.ApplySourceClassification(row, null, baseFiles, EmptyManualSources());

            Assert.AreEqual(FileManagerSource.BaseGame, row.Source);
            Assert.IsFalse(row.SourceEditable);
        }

        [Test]
        public void AllManualOptionsAreSelectable()
        {
            CollectionAssert.AreEquivalent(
                new[] { FileManagerSource.Untracked, FileManagerSource.BaseGame, FileManagerSource.Creations, FileManagerSource.ExternalModManager },
                FileManagerSourceDisplay.ManualSourceOptions.Select(x => x.Source));
        }

        [Test]
        public void PersistenceSurvivesServiceRecreation()
        {
            MemoryManualSourceStore store = new MemoryManualSourceStore();
            FileManagerRow row = CreateRow("textures\\persist.dds");
            FileManagerQueryService firstService = new FileManagerQueryService(store);
            FileManagerQueryService secondService = new FileManagerQueryService(store);

            firstService.ChangeManualSource("FalloutNV", row, FileManagerSource.ExternalModManager, FileManagerSource.Untracked);
            IDictionary<string, FileManagerSource> restored = secondService.LoadManualSources("FalloutNV");

            Assert.AreEqual(FileManagerSource.ExternalModManager, restored[row.NormalizedRelativePath]);
        }

        [Test]
        public void ManualSourcesUseCaseInsensitiveNormalizedPathMatching()
        {
            MemoryManualSourceStore store = new MemoryManualSourceStore();
            store.SetSource("FalloutNV", "Textures/Armor/Combat.DDS", FileManagerSource.Creations);
            FileManagerRow row = CreateRow("textures\\armor\\combat.dds");

            FileManagerQueryService.ApplySourceClassification(row, null, EmptyBaseFiles(), store.Load("FalloutNV"));

            Assert.AreEqual(FileManagerSource.Creations, row.Source);
        }

        [Test]
        public void ManualClassificationSurvivesRefresh()
        {
            MemoryManualSourceStore store = new MemoryManualSourceStore();
            FileManagerQueryService service = new FileManagerQueryService(store);
            FileManagerRow firstRow = CreateRow("scripts\\manual.pex");
            service.ChangeManualSource("FalloutNV", firstRow, FileManagerSource.ExternalModManager, FileManagerSource.Untracked);

            FileManagerRow refreshedRow = CreateRow("scripts\\manual.pex");
            FileManagerQueryService.ApplySourceClassification(refreshedRow, null, EmptyBaseFiles(), service.LoadManualSources("FalloutNV"));

            Assert.AreEqual(FileManagerSource.ExternalModManager, refreshedRow.Source);
            Assert.IsTrue(refreshedRow.SourceEditable);
        }

        [Test]
        public void SelectingUntrackedRemovesPersistedOverride()
        {
            MemoryManualSourceStore store = new MemoryManualSourceStore();
            FileManagerQueryService service = new FileManagerQueryService(store);
            FileManagerRow row = CreateRow("textures\\remove.dds");
            service.ChangeManualSource("FalloutNV", row, FileManagerSource.Creations, FileManagerSource.Untracked);

            service.ChangeManualSource("FalloutNV", row, FileManagerSource.Untracked, FileManagerSource.Creations);
            FileManagerRow refreshedRow = CreateRow("textures\\remove.dds");
            FileManagerQueryService.ApplySourceClassification(refreshedRow, null, EmptyBaseFiles(), service.LoadManualSources("FalloutNV"));

            Assert.IsFalse(service.LoadManualSources("FalloutNV").ContainsKey(row.NormalizedRelativePath));
            Assert.AreEqual(FileManagerSource.Untracked, refreshedRow.Source);
        }

        [Test]
        public void NmmOwnershipTakesPrecedenceOverStoredOverride()
        {
            FileManagerRow row = CreateRow("textures\\owned.dds");
            Dictionary<string, FileManagerSource> manualSources = new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase)
            {
                { row.NormalizedRelativePath, FileManagerSource.Creations }
            };
            List<IVirtualModLink> links = new List<IVirtualModLink>
            {
                new TestVirtualModLink(row.RelativePath, true, 0, new TestVirtualModInfo("owned.7z", "Owned Mod"))
            };

            FileManagerQueryService.ApplySourceClassification(row, links, EmptyBaseFiles(), manualSources);

            Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
            Assert.IsFalse(row.SourceEditable);
        }

        [Test]
        public void AutomaticBaseGameRecognitionTakesPrecedenceOverStoredOverride()
        {
            FileManagerRow row = CreateRow("base.esm");
            Dictionary<string, FileManagerSource> manualSources = new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase)
            {
                { row.NormalizedRelativePath, FileManagerSource.ExternalModManager }
            };
            HashSet<string> baseFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.NormalizedRelativePath };

            FileManagerQueryService.ApplySourceClassification(row, null, baseFiles, manualSources);

            Assert.AreEqual(FileManagerSource.BaseGame, row.Source);
            Assert.IsFalse(row.SourceEditable);
        }

        [Test]
        public void PersistenceFailureRestoresPreviousDisplayedValue()
        {
            ThrowingManualSourceStore store = new ThrowingManualSourceStore();
            FileManagerQueryService service = new FileManagerQueryService(store);
            FileManagerRow row = CreateRow("textures\\failure.dds");
            row.SourceEditable = true;
            row.Source = FileManagerSource.Creations;

            Assert.Throws<InvalidOperationException>(() => service.ChangeManualSource("FalloutNV", row, FileManagerSource.ExternalModManager, FileManagerSource.Untracked));
            Assert.AreEqual(FileManagerSource.Untracked, row.Source);
        }

        private static FileManagerRow CreateRow(string relativePath)
        {
            return new FileManagerRow
            {
                RelativePath = relativePath,
                NormalizedRelativePath = FileManagerQueryService.NormalizePath(relativePath)
            };
        }

        private static HashSet<string> EmptyBaseFiles()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, FileManagerSource> EmptyManualSources()
        {
            return new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class MemoryManualSourceStore : IFileManagerManualSourceStore
        {
            private readonly Dictionary<string, Dictionary<string, FileManagerSource>> _sources = new Dictionary<string, Dictionary<string, FileManagerSource>>(StringComparer.OrdinalIgnoreCase);

            public IDictionary<string, FileManagerSource> Load(string gameModeId)
            {
                Dictionary<string, FileManagerSource> gameSources;
                if (!_sources.TryGetValue(gameModeId, out gameSources))
                    return new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);

                return new Dictionary<string, FileManagerSource>(gameSources, StringComparer.OrdinalIgnoreCase);
            }

            public void SetSource(string gameModeId, string normalizedRelativePath, FileManagerSource source)
            {
                Dictionary<string, FileManagerSource> gameSources;
                if (!_sources.TryGetValue(gameModeId, out gameSources))
                {
                    gameSources = new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);
                    _sources[gameModeId] = gameSources;
                }

                string key = FileManagerQueryService.NormalizePath(normalizedRelativePath);
                if (source == FileManagerSource.Untracked)
                    gameSources.Remove(key);
                else
                    gameSources[key] = source;
            }
        }

        private sealed class ThrowingManualSourceStore : IFileManagerManualSourceStore
        {
            public IDictionary<string, FileManagerSource> Load(string gameModeId)
            {
                return new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase);
            }

            public void SetSource(string gameModeId, string normalizedRelativePath, FileManagerSource source)
            {
                throw new InvalidOperationException("Persistence failed.");
            }
        }

        private sealed class TestVirtualModInfo : IVirtualModInfo
        {
            public TestVirtualModInfo(string modFileName, string modName)
            {
                ModFileName = modFileName;
                ModName = modName;
            }

            public string ModId { get { return String.Empty; } }
            public string ModName { get; private set; }
            public string DownloadId { get { return String.Empty; } }
            public string UpdatedDownloadId { get { return String.Empty; } }
            public string ModFileName { get; private set; }
            public string NewFileName { get { return String.Empty; } }
            public string ModFilePath { get { return String.Empty; } }
            public string ModFileFullPath { get { return String.Empty; } }
            public string FileVersion { get { return String.Empty; } }

            public bool Equals(IVirtualModInfo other)
            {
                return ReferenceEquals(this, other);
            }
        }

        private sealed class TestVirtualModLink : IVirtualModLink
        {
            public TestVirtualModLink(string virtualModPath, bool active, int priority, IVirtualModInfo modInfo)
            {
                VirtualModPath = virtualModPath;
                Active = active;
                Priority = priority;
                ModInfo = modInfo;
                InstallRoot = ModInstallRoot.Default;
            }

            public string VirtualModPath { get; set; }
            public string RealModPath { get; set; }
            public int Priority { get; set; }
            public bool Active { get; set; }
            public IVirtualModInfo ModInfo { get; set; }
            public ModInstallRoot InstallRoot { get; set; }

            public bool Equals(IVirtualModLink other)
            {
                return ReferenceEquals(this, other);
            }
        }
    }
}