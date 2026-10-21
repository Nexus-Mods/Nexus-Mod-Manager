namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.IO;
    using System.Diagnostics;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Threading;

    using Nexus.Client.Games;
    using Nexus.Client.ModManagement;
    using Nexus.Client.ModManagement.UI;
    using Nexus.Client.ModManagement.InstallationLog;
    using Nexus.Client.Mods;
    using Nexus.Client.Util.Collections;

    using NUnit.Framework;

    public class FileManagerManualSourceTests
    {
        [Test]
        public void WitcherOwnerSwitch_RequiresTheFreshOwnerAndCandidateSet()
        {
            FileManagerRow current = CreateReviewedWitcherRow("A", "B");
            Assert.DoesNotThrow(() => FileManagerVM.RequireFreshWitcherOwnerChoice(null, "A", "B", new[] { "A", "B" }, current));
            Assert.DoesNotThrow(() => FileManagerVM.RequireFreshWitcherOwnerChoice(null, "a", "b", new[] { "a", "b" }, current));
        }

        [Test]
        public void WitcherOwnerSwitch_RejectsStaleOrMissingOwners()
        {
            FileManagerRow current = CreateReviewedWitcherRow("C", "D");
            Assert.Throws<InvalidOperationException>(() =>
                FileManagerVM.RequireFreshWitcherOwnerChoice(null, "A", "D", new[] { "C", "D" }, current));
            Assert.Throws<InvalidOperationException>(() =>
                FileManagerVM.RequireFreshWitcherOwnerChoice(null, "C", "Missing", new[] { "C", "D" }, current));
            Assert.Throws<InvalidOperationException>(() =>
                FileManagerVM.RequireFreshWitcherOwnerChoice(null, "C", "D", new[] { "C", "D" }, new FileManagerRow()));
        }

        [Test]
        public void WitcherOwnerSwitch_RejectsChangedCandidateStacksEvenWhenWinnerIsUnchanged()
        {
            FileManagerRow current = CreateReviewedWitcherRow("A", "B");
            Assert.Throws<InvalidOperationException>(() => FileManagerVM.RequireFreshWitcherOwnerChoice(
                null, "A", "B", new[] { "B", "A" }, current));
            Assert.Throws<InvalidOperationException>(() => FileManagerVM.RequireFreshWitcherOwnerChoice(
                null, "A", "B", new[] { "A", "B", "C" }, current));
            Assert.Throws<InvalidOperationException>(() => FileManagerVM.RequireFreshWitcherOwnerChoice(
                null, "A", "B", new[] { "A", "C" }, current));
        }

        [Test]
        public void WitcherOwnerSwitch_RejectsPromotionTopologyChanges()
        {
            FileManagerRow current = CreateReviewedWitcherRow("A", "B");
            var promotedTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "Mods\\modA\\content\\x.ws");
            Assert.Throws<InvalidOperationException>(() =>
                FileManagerVM.RequireFreshWitcherOwnerChoice(promotedTarget, "A", "B", new[] { "A", "B" }, current));
            current.DeploymentTarget = promotedTarget;
            Assert.DoesNotThrow(() => FileManagerVM.RequireFreshWitcherOwnerChoice(promotedTarget, "A", "B", new[] { "A", "B" }, current));
            Assert.Throws<InvalidOperationException>(() =>
                FileManagerVM.RequireFreshWitcherOwnerChoice(null, "A", "B", new[] { "A", "B" }, current));
        }

        private static FileManagerRow CreateReviewedWitcherRow(string previousOwner, string nextOwner)
        {
            return new FileManagerRow
            {
                RelativePath = "Mods\\modA\\content\\x.ws",
                OwnerKey = previousOwner,
                OwnerCandidates = new List<FileManagerOwnerCandidate>
                {
                    new FileManagerOwnerCandidate(previousOwner, "Previous", 1),
                    new FileManagerOwnerCandidate(nextOwner, "Next", 2)
                }
            };
        }

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
                { row.RelativePath, FileManagerSource.Creations }
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
            Assert.AreEqual(1, row.OwnerCount);
            Assert.AreEqual(0, row.OwnerCandidates.Count);
            Assert.IsFalse(row.OwnerEditable);
        }

        [Test]
        public void AutomaticallyRecognizedBaseGameRowsAreReadOnly()
        {
            FileManagerRow row = CreateRow("falloutnv.esm");
            HashSet<string> baseFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.RelativePath };

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
            PrepareEditableUntrackedRow(row);

            firstService.ChangeManualSource("FalloutNV", row, FileManagerSource.ExternalModManager, FileManagerSource.Untracked);
            IDictionary<string, FileManagerSource> restored = secondService.LoadManualSources("FalloutNV");

            Assert.AreEqual(FileManagerSource.ExternalModManager, restored[row.RelativePath]);
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
        public void NormalizePathReusesAlreadyNormalizedPathWithoutChangingCase()
        {
            string path = "Textures\\Armor\\Combat.DDS";

            string normalizedPath = FileManagerQueryService.NormalizePath(path);

            Assert.AreSame(path, normalizedPath);
        }

        [Test]
        public void NormalizePathConvertsSeparatorsWithoutLowercasing()
        {
            string normalizedPath = FileManagerQueryService.NormalizePath("\\Textures/Armor/Combat.DDS");

            Assert.AreEqual("Textures\\Armor\\Combat.DDS", normalizedPath);
        }

        [Test]
        public void ManualClassificationSurvivesRefresh()
        {
            MemoryManualSourceStore store = new MemoryManualSourceStore();
            FileManagerQueryService service = new FileManagerQueryService(store);
            FileManagerRow firstRow = CreateRow("scripts\\manual.pex");
            PrepareEditableUntrackedRow(firstRow);
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
            PrepareEditableUntrackedRow(row);
            service.ChangeManualSource("FalloutNV", row, FileManagerSource.Creations, FileManagerSource.Untracked);

            service.ChangeManualSource("FalloutNV", row, FileManagerSource.Untracked, FileManagerSource.Creations);
            FileManagerRow refreshedRow = CreateRow("textures\\remove.dds");
            FileManagerQueryService.ApplySourceClassification(refreshedRow, null, EmptyBaseFiles(), service.LoadManualSources("FalloutNV"));

            Assert.IsFalse(service.LoadManualSources("FalloutNV").ContainsKey(row.RelativePath));
            Assert.AreEqual(FileManagerSource.Untracked, refreshedRow.Source);
        }

        [Test]
        public void NmmOwnershipTakesPrecedenceOverStoredOverride()
        {
            FileManagerRow row = CreateRow("textures\\owned.dds");
            Dictionary<string, FileManagerSource> manualSources = new Dictionary<string, FileManagerSource>(StringComparer.OrdinalIgnoreCase)
            {
                { row.RelativePath, FileManagerSource.Creations }
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
                { row.RelativePath, FileManagerSource.ExternalModManager }
            };
            HashSet<string> baseFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { row.RelativePath };

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


        [Test]
        public void NmmOwnerCandidatesArePreorderedAndDeduplicated()
        {
            FileManagerRow row = CreateRow("textures\\owned.dds");
            TestVirtualModInfo lowPriority = new TestVirtualModInfo("low.7z", "Low Priority");
            TestVirtualModInfo highPriority = new TestVirtualModInfo("high.7z", "High Priority");
            List<IVirtualModLink> links = new List<IVirtualModLink>
            {
                new TestVirtualModLink(row.RelativePath, true, 20, lowPriority),
                new TestVirtualModLink(row.RelativePath, false, 10, highPriority),
                new TestVirtualModLink(row.RelativePath, false, 30, lowPriority)
            };

            FileManagerQueryService.ApplySourceClassification(row, links, EmptyBaseFiles(), EmptyManualSources());

            Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
            Assert.AreEqual(FileManagerQueryService.CreateOwnerKey(lowPriority), row.OwnerKey);
            Assert.AreEqual(2, row.OwnerCount);
            Assert.AreEqual(2, row.OwnerCandidates.Count);
            Assert.IsTrue(row.OwnerEditable);
            Assert.AreEqual(FileManagerQueryService.CreateOwnerKey(highPriority), row.OwnerCandidates[0].OwnerKey);
            Assert.AreEqual(FileManagerQueryService.CreateOwnerKey(lowPriority), row.OwnerCandidates[1].OwnerKey);
        }

        [Test]
        public void SelectedOwnerUpdateUsesPreparedCandidateList()
        {
            FileManagerQueryService service = new FileManagerQueryService();
            FileManagerRow row = CreateRow("textures\\owned.dds");
            row.Source = FileManagerSource.InstalledByNmm;
            row.SourceEditable = false;
            row.OwnerCandidates = new List<FileManagerOwnerCandidate>
            {
                new FileManagerOwnerCandidate("first|", "First Mod", 0),
                new FileManagerOwnerCandidate("second|", "Second Mod", 1)
            };
            row.OwnerKey = "first|";
            row.OwnerName = "First Mod";

            service.ApplySelectedOwner(row, "second|");

            Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
            Assert.AreEqual("second|", row.OwnerKey);
            Assert.AreEqual("Second Mod", row.OwnerName);
        }

        [Test]
        public void SourceCountsUpdateWithoutRecountingRows()
        {
            FileManagerSourceCounts counts = new FileManagerSourceCounts();
            counts.Add(FileManagerSource.Untracked);
            counts.Add(FileManagerSource.InstalledByNmm);

            counts.Change(FileManagerSource.Untracked, FileManagerSource.Creations);

            Assert.AreEqual(2, counts.Total);
            Assert.AreEqual(1, counts.InstalledByNmm);
            Assert.AreEqual(1, counts.Creations);
            Assert.AreEqual(0, counts.Untracked);
        }

        /// <summary>Only untracked classifications may enter the bulk-removal action.</summary>
        [TestCase(FileManagerSource.BaseGame)]
        [TestCase(FileManagerSource.InstalledByNmm)]
        [TestCase(FileManagerSource.InstalledByCollection)]
        [TestCase(FileManagerSource.Creations)]
        [TestCase(FileManagerSource.ExternalModManager)]
        public void UntrackedRemoval_RejectsTrackedAndManuallyClassifiedSources(FileManagerSource source)
        {
            FileManagerRow row = CreateRow("file.esp");
            row.FullPath = @"C:\Game\Data\file.esp";
            row.Source = source;
            Assert.Throws<InvalidOperationException>(() => FileManagerQueryService.GetUntrackedRemovalPath(@"C:\Game\Data", row));
        }

        /// <summary>Both path representations must still identify a file inside the reviewed deployment root.</summary>
        [TestCase("file.esp", @"C:\Game\Data\file.esp", true)]
        [TestCase(@"..\outside.esp", @"C:\Game\outside.esp", false)]
        [TestCase(@"C:\Other\file.esp", @"C:\Other\file.esp", false)]
        [TestCase("file.esp", @"C:\Other\file.esp", false)]
        public void UntrackedRemoval_EnforcesDeploymentRoot(string relative, string full, bool valid)
        {
            FileManagerRow row = CreateRow(relative);
            row.FullPath = full;
            row.Source = FileManagerSource.Untracked;
            if (valid) Assert.AreEqual(Path.GetFullPath(full), FileManagerQueryService.GetUntrackedRemovalPath(@"C:\Game\Data", row));
            else Assert.Throws<InvalidOperationException>(() => FileManagerQueryService.GetUntrackedRemovalPath(@"C:\Game\Data", row));
        }

        /// <summary>Collection classification follows only the active winner and preserves NMM ownership controls.</summary>
        [TestCase("collection|", FileManagerSource.InstalledByCollection)]
        [TestCase("standalone|", FileManagerSource.InstalledByNmm)]
        [TestCase("native-key", FileManagerSource.InstalledByCollection)]
        public void CollectionSource_UsesWinningOwner(string winner, FileManagerSource expected)
        {
            var row = CreateRow("owned.dds");
            var candidates = new List<FileManagerOwnerCandidate>
            {
                new FileManagerOwnerCandidate("collection|", "Collection Mod", 0),
                new FileManagerOwnerCandidate("standalone|", "Standalone Mod", 1)
            };
            var ownership = new FileManagerPathOwnership(true, winner, "Winner", 2, candidates);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "collection|", "native-key" };

            FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, EmptyBaseFiles(), EmptyManualSources(), keys);

            Assert.AreEqual(expected, row.Source);
            Assert.IsFalse(row.SourceEditable);
            Assert.IsTrue(row.OwnerEditable);
            Assert.AreEqual(winner, row.OwnerKey);
        }

        /// <summary>Missing tracking is conservative and cannot override base-game or untracked classification.</summary>
        [Test]
        public void CollectionSource_RequiresActiveOwnershipAndTrustedProvenance()
        {
            var row = CreateRow("file.dds");
            var keys = new HashSet<string> { "owner" };
            var ownership = new FileManagerPathOwnership(true, "owner", "Mod", 1, FileManagerRow.EmptyOwnerCandidates);
            FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, EmptyBaseFiles(), EmptyManualSources());
            Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
            FileManagerQueryService.ApplySourceClassificationFromOwnership(row, null, EmptyBaseFiles(), EmptyManualSources(), keys);
            Assert.AreEqual(FileManagerSource.Untracked, row.Source);
            FileManagerQueryService.ApplySourceClassificationFromOwnership(row, null, new HashSet<string> { row.RelativePath }, EmptyManualSources(), keys);
            Assert.AreEqual(FileManagerSource.BaseGame, row.Source);
        }

        /// <summary>Only current registrations with unambiguous aliases may acquire the Collection source.</summary>
        [Test]
        public void CollectionLookup_MapsNativeAndVirtualKeysWithoutGuessing()
        {
            var registrations = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "native-a", "mod-a.7z|1" },
                { "native-b", "duplicate.7z|2" },
                { "native-c", "DUPLICATE.7Z|2" },
                { "native-d", "standalone.7z|3" }
            };
            ISet<string> keys = FileManagerCollectionSourceLookup.BuildOwnerKeys(
                new[] { "native-a", "native-b", "removed-native" }, registrations);
            CollectionAssert.AreEquivalent(new[] { "native-a", "mod-a.7z|1", "native-b" }, keys);
        }

        /// <summary>Existing Virtual records use archive basenames even when native registrations expose full archive paths.</summary>
        [TestCase(@"C:\Mods\Example.7z", "123")]
        [TestCase(@"C:\Mods\Nested\Example.7z", null)]
        [TestCase("Example.7z", "123")]
        public void CollectionLookup_CapturedRegistrationMatchesExistingVirtualOwner(string archivePath, string downloadId)
        {
            IMod mod = InterfaceStub<IMod>.Create((method, args) =>
            {
                if (method.Name == "get_Filename") return archivePath;
                if (method.Name == "get_DownloadId") return downloadId;
                return null;
            });
            var activeMods = new ReadOnlyObservableList<IMod>(new ThreadSafeObservableList<IMod>(new[] { mod }));
            IInstallLog log = InterfaceStub<IInstallLog>.Create((method, args) =>
            {
                if (method.Name == "get_ActiveMods") return activeMods;
                if (method.Name == "GetModKey") return "native-existing";
                return null;
            });
            var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
            typeof(ModManager).GetField("<InstallationLog>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, log);
            IDictionary<string, string> registrations = FileManagerCollectionSourceLookup.CaptureRegistrationKeys(manager);
            ISet<string> keys = FileManagerCollectionSourceLookup.BuildOwnerKeys(new[] { "native-existing" }, registrations);
            var info = new VirtualModInfo("1", downloadId, "Example", archivePath, "1.0");
            string ownerKey = FileManagerQueryService.CreateOwnerKey(info);
            var ownership = new FileManagerPathOwnership(true, ownerKey, "Example", 1, FileManagerRow.EmptyOwnerCandidates);
            FileManagerRow row = CreateRow("existing.dds");

            FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, EmptyBaseFiles(), EmptyManualSources(), keys);

            Assert.That(row.Source, Is.EqualTo(FileManagerSource.InstalledByCollection));
            Assert.That(keys, Does.Contain("native-existing"));
        }

        /// <summary>Collection sources are filterable but cannot be manually assigned, and legacy numeric IDs remain stable.</summary>
        [Test]
        public void CollectionSource_PreservesManualOptionsAndLegacyIds()
        {
            Assert.AreEqual(0, (int)FileManagerSource.InstalledByNmm);
            Assert.AreEqual(4, (int)FileManagerSource.Untracked);
            Assert.IsFalse(FileManagerSourceDisplay.IsManualSource(FileManagerSource.InstalledByCollection));
            Assert.IsTrue(FileManagerSourceDisplay.AllSourceOptions.Any(x => x.Source == FileManagerSource.InstalledByCollection));
            var row = CreateRow("file.dds");
            PrepareEditableUntrackedRow(row);
            Assert.Throws<InvalidOperationException>(() => new FileManagerQueryService().ApplyManualSource(row, FileManagerSource.InstalledByCollection));
        }

        /// <summary>Source changes and cloned summaries count every file exactly once.</summary>
        [Test]
        public void CollectionCounts_StaySeparateFromOtherNmmFiles()
        {
            var counts = new FileManagerSourceCounts();
            counts.Add(FileManagerSource.InstalledByNmm);
            counts.Add(FileManagerSource.InstalledByCollection);
            var copy = counts.Clone();
            copy.Change(FileManagerSource.InstalledByCollection, FileManagerSource.InstalledByNmm);
            Assert.AreEqual(2, copy.Total);
            Assert.AreEqual(2, copy.InstalledByNmm);
            Assert.AreEqual(0, copy.InstalledByCollection);
            Assert.AreEqual(1, counts.InstalledByCollection);
            Assert.AreEqual(0, copy.Untracked);
        }

        /// <summary>Discovery progress uses the existing scan, reports its final count and keeps the owner snapshot for switching.</summary>
        [Test]
        public void Scan_ReportsProgressAndKeepsCollectionSourceAcrossOwnerChanges()
        {
            string root = Path.Combine(Path.GetTempPath(), "NmmFileManager_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                for (int index = 0; index < 200; index++)
                    File.WriteAllText(Path.Combine(root, index + ".dds"), "test");
                var links = new ThreadSafeObservableList<IVirtualModLink>
                {
                    new TestVirtualModLink("0.dds", true, 0, new TestVirtualModInfo("collection.7z", "Collection Mod")),
                    new TestVirtualModLink("0.dds", false, 1, new TestVirtualModInfo("standalone.7z", "Standalone Mod"))
                };
                IGameMode game = CreateScanGame(root);
                IVirtualModActivator activator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
                    method.Name == "get_VirtualLinks" ? links : null);
                var service = new FileManagerQueryService();
                var progress = new RecordingScanProgress();
                Stopwatch watch = Stopwatch.StartNew();
                FileManagerScanResult result = service.Scan(game, activator, null, CancellationToken.None, progress,
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "collection.7z|" });
                Assert.AreEqual(200, result.TotalFiles);
                Assert.AreEqual(0, progress.Counts.First());
                Assert.AreEqual(200, progress.Counts.Last());
                Assert.IsTrue(progress.Counts.SequenceEqual(progress.Counts.OrderBy(x => x)));
                Assert.LessOrEqual(progress.Counts.Count, 2 + watch.ElapsedMilliseconds / 500);
                FileManagerRow row = result.RowsByNormalizedPath["0.dds"];
                Assert.AreEqual(FileManagerSource.InstalledByCollection, row.Source);
                Assert.AreEqual(1, result.Counts.InstalledByCollection);
                Assert.IsTrue(row.OwnerEditable);
                service.ApplySelectedOwner(row, "standalone.7z|");
                Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
                service.ApplySelectedOwner(row, "collection.7z|");
                Assert.AreEqual(FileManagerSource.InstalledByCollection, row.Source);
                service.RefreshRowOwnership(row, game, activator);
                Assert.AreEqual(FileManagerSource.InstalledByCollection, row.Source);
                Assert.AreEqual(1, service.ReclassifyRows(result.Rows, game, activator).InstalledByCollection);
                links[0].Active = false;
                Assert.AreEqual(0, service.SynchronizeRowsAfterActivation(result.Rows, result.RowsByNormalizedPath, game, activator).InstalledByCollection);
                Assert.AreEqual(FileManagerSource.Untracked, row.Source);
                FileManagerScanResult refreshed = service.Scan(game, activator, null, CancellationToken.None, null, null);
                Assert.AreEqual(0, refreshed.Counts.InstalledByCollection);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Long deployment paths remain discoverable, owned and correctly classified after scanning and activation refresh.</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Scan_LongPathsPreserveOwnershipAndResolveLinkTypes(bool longDeploymentRoot)
        {
            string temporaryRoot = Path.Combine(Path.GetTempPath(), "NmmFileManager_" + Guid.NewGuid().ToString("N"));
            string deploymentRoot = Path.Combine(temporaryRoot, "Data");
            string relativeDirectory = String.Empty;
            while (relativeDirectory.Length < 300)
                relativeDirectory = Path.Combine(relativeDirectory, new string('d', 24));
            if (longDeploymentRoot)
            {
                deploymentRoot = Path.Combine(deploymentRoot, relativeDirectory);
                relativeDirectory = "textures";
            }
            string directory = Path.Combine(deploymentRoot, relativeDirectory);
            Directory.CreateDirectory(Nexus.Client.Util.FileUtil.GetIoPath(directory));
            try
            {
                string source = Path.Combine(temporaryRoot, "source.bin");
                File.WriteAllText(source, "linked content");
                string linkedRelativePath = Path.Combine(relativeDirectory, "linked.dds");
                string linkedPath = Path.Combine(deploymentRoot, linkedRelativePath);
                string regularPath = Path.Combine(directory, "regular.dds");
                File.WriteAllText(Nexus.Client.Util.FileUtil.GetIoPath(regularPath), "regular content");
                Assert.IsTrue(new ChinhDo.Transactions.TxFileManager().CreateHardLink(linkedPath, source));
                var links = new ThreadSafeObservableList<IVirtualModLink>
                {
                    new TestVirtualModLink(linkedRelativePath, true, 0, new TestVirtualModInfo("nested.7z", "Nested Mod"))
                };
                IGameMode game = CreateScanGame(deploymentRoot);
                IVirtualModActivator activator = InterfaceStub<IVirtualModActivator>.Create((method, args) =>
                    method.Name == "get_VirtualLinks" ? links : null);
                var service = new FileManagerQueryService();
                FileManagerScanResult result = service.Scan(game, activator, CancellationToken.None);
                Assert.AreEqual(2, result.TotalFiles);
                Assert.AreEqual(0, result.Diagnostics.SkippedDirectoryCount);
                Assert.AreEqual(0, result.Diagnostics.SkippedFileCount);
                FileManagerRow row = result.RowsByNormalizedPath[linkedRelativePath];
                Assert.AreEqual(linkedPath, row.FullPath);
                Assert.AreEqual(FileManagerSource.InstalledByNmm, row.Source);
                Assert.IsFalse(row.FullPath.StartsWith(@"\\?\", StringComparison.Ordinal));
                FileManagerLinkTypeResolutionDiagnostics resolution = FileManagerLinkTypeResolver.Resolve(
                    result.Rows, result.Rows.Count, (batch, completed, total) => { }, CancellationToken.None);
                Assert.AreEqual(1, resolution.HardLinkCount);
                Assert.AreEqual(1, resolution.RealFileCount);
                Assert.AreEqual(0, resolution.UnavailableCount);
                Assert.AreEqual(0, resolution.NotFoundCount);
                Assert.AreEqual(1, service.SynchronizeRowsAfterActivation(
                    result.Rows, result.RowsByNormalizedPath, game, activator).InstalledByNmm);
                var refreshedRows = new List<FileManagerRow>();
                var refreshedIndex = new Dictionary<string, FileManagerRow>(StringComparer.OrdinalIgnoreCase);
                Assert.AreEqual(1, service.SynchronizeRowsAfterActivation(refreshedRows, refreshedIndex, game, activator).InstalledByNmm);
                Assert.AreEqual(linkedPath, refreshedRows.Single().FullPath);
            }
            finally
            {
                Directory.Delete(Nexus.Client.Util.FileUtil.GetIoPath(temporaryRoot), true);
            }
        }
        /// <summary>Cancellation after the initial progress event does not publish a completed scan.</summary>
        [Test]
        public void Scan_ProgressHonorsCancellation()
        {
            string root = Path.Combine(Path.GetTempPath(), "NmmFileManager_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, "file.dds"), "test");
                using (var cancellation = new CancellationTokenSource())
                {
                    var progress = new RecordingScanProgress { OnReport = count => cancellation.Cancel() };
                    var activator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
                    Assert.Throws<OperationCanceledException>(() => new FileManagerQueryService().Scan(
                        CreateScanGame(root), activator, null, cancellation.Token, progress, null));
                    CollectionAssert.AreEqual(new[] { 0 }, progress.Counts);
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Measures the additional classification cost for a large deployment without filesystem or database work per row.</summary>
        [Test]
        public void CollectionSource_LargeRowSetUsesOnePreparedLookup()
        {
            const int count = 200000;
            var rows = Enumerable.Range(0, count).Select(index => CreateRow(index + ".dds")).ToList();
            var ownership = new FileManagerPathOwnership(true, "collection|", "Mod", 1, FileManagerRow.EmptyOwnerCandidates);
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "collection|" };
            var baseFiles = EmptyBaseFiles();
            var manual = EmptyManualSources();
            foreach (FileManagerRow row in rows)
                FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, baseFiles, manual);
            Stopwatch watch = Stopwatch.StartNew();
            foreach (FileManagerRow row in rows)
                FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, baseFiles, manual);
            long baselineTicks = watch.ElapsedTicks;
            foreach (FileManagerRow row in rows)
                FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, baseFiles, manual, keys);
            watch.Restart();
            foreach (FileManagerRow row in rows)
                FileManagerQueryService.ApplySourceClassificationFromOwnership(row, ownership, baseFiles, manual, keys);
            TestContext.WriteLine("Classification for {0:N0} files: baseline {1:F2}ms, Collection lookup {2:F2}ms", count,
                baselineTicks * 1000.0 / Stopwatch.Frequency, watch.Elapsed.TotalMilliseconds);
            Assert.IsTrue(rows.All(row => row.Source == FileManagerSource.InstalledByCollection));
        }

        /// <summary>Supplies only metadata required by the File Manager directory scan.</summary>
        private static IGameMode CreateScanGame(string root)
        {
            return InterfaceStub<IGameMode>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_InstallationPath": return root;
                    case "get_UsesPlugins": return false;
                    case "get_BaseGameFiles": return String.Empty;
                    case "get_ModeId": return "TestGame";
                    case "GetModFormatAdjustedPath": return args[1];
                    default: return null;
                }
            });
        }

        /// <summary>Records synchronous worker-side reports without introducing UI dispatch into scan tests.</summary>
        private sealed class RecordingScanProgress : IProgress<int>
        {
            public readonly List<int> Counts = new List<int>();
            public Action<int> OnReport { get; set; }

            /// <summary>Records a discovered count and optionally cancels the scan under test.</summary>
            public void Report(int value)
            {
                Counts.Add(value);
                if (OnReport != null) OnReport(value);
            }
        }

        private static FileManagerRow CreateRow(string relativePath)
        {
            return new FileManagerRow
            {
                RelativePath = FileManagerQueryService.NormalizePath(relativePath)
            };
        }

        /// <summary>
        /// Applies the normal source-classification pass before exercising a manual source change.
        /// </summary>
        private static void PrepareEditableUntrackedRow(FileManagerRow row)
        {
            FileManagerQueryService.ApplySourceClassification(row, null, EmptyBaseFiles(), EmptyManualSources());
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
