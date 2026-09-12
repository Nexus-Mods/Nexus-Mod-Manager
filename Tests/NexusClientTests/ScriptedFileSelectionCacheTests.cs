namespace NexusClientTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Xml.Linq;

    using Nexus.Client.ModManagement.Scripting;

    using NUnit.Framework;

    /// <summary>
    /// Verifies scripted file-selection cache persistence and replay behavior.
    /// </summary>
    [TestFixture]
    public class ScriptedFileSelectionCacheTests
    {
        /// <summary>
        /// Verifies that the default cache path matches the legacy InstallInfo\Scripted location.
        /// </summary>
        [Test]
        public void Constructor_DefaultPathMatchesLegacyLocation()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(ctx.Mod, ctx.GameMode);

                Assert.AreEqual(Path.Combine(tmp.Path, "InstallInfo", "Scripted", "ExampleMod.xml"), cache.FilePath);
                Assert.IsFalse(cache.Exists);
            }
        }

        /// <summary>
        /// Verifies that recorded selections preserve legacy metadata, source text, destination text, and ordering.
        /// </summary>
        [Test]
        public void RecordSelection_WritesLegacyFormatAndPreservesOrder()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(ctx.Mod, ctx.GameMode);

                cache.RecordSelection("Textures/First.DDS", "Textures/Target.DDS");
                cache.RecordSelection("Meshes/Second.NIF", "Meshes/Target.NIF");

                Assert.IsTrue(cache.Exists);
                XDocument document = XDocument.Load(cache.FilePath);
                XElement root = document.Root;
                XElement[] files = new List<XElement>(root.Elements("File")).ToArray();

                Assert.AreEqual("FileList", root.Name.LocalName);
                Assert.AreEqual("Example Mod", (string)root.Attribute("ModName"));
                Assert.AreEqual("1.2.3", (string)root.Attribute("ModVersion"));
                Assert.AreEqual(2, files.Length);
                Assert.AreEqual("Textures/First.DDS", (string)files[0].Attribute("FileFrom"));
                Assert.AreEqual("Textures/Target.DDS", (string)files[0].Attribute("FileTo"));
                Assert.AreEqual("Meshes/Second.NIF", (string)files[1].Attribute("FileFrom"));
                Assert.AreEqual("Meshes/Target.NIF", (string)files[1].Attribute("FileTo"));
            }
        }

        /// <summary>
        /// Verifies that replay loading preserves mapping order and ignores entries with an empty archive source.
        /// </summary>
        [Test]
        public void LoadSelections_PreservesOrderAndSkipsEmptySources()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                string filePath = Path.Combine(tmp.Path, "profile-scripted.xml");
                new XDocument(
                    new XElement("FileList",
                        new XElement("File", new XAttribute("FileFrom", "A.txt"), new XAttribute("FileTo", "Data/A.txt")),
                        new XElement("File", new XAttribute("FileFrom", String.Empty), new XAttribute("FileTo", "ignored.txt")),
                        new XElement("File", new XAttribute("FileFrom", "B.txt"), new XAttribute("FileTo", "Data/B.txt"))))
                    .Save(filePath);
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(filePath);

                List<KeyValuePair<string, string>> selections = cache.LoadSelections();

                Assert.AreEqual(2, selections.Count);
                Assert.AreEqual("A.txt", selections[0].Key);
                Assert.AreEqual("Data/A.txt", selections[0].Value);
                Assert.AreEqual("B.txt", selections[1].Key);
                Assert.AreEqual("Data/B.txt", selections[1].Value);
            }
        }

        /// <summary>
        /// Verifies that a missing cache produces no replay mappings.
        /// </summary>
        [Test]
        public void LoadSelections_MissingCacheReturnsNull()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(Path.Combine(tmp.Path, "missing.xml"));

                Assert.IsNull(cache.LoadSelections());
            }
        }

        /// <summary>
        /// Verifies the legacy behavior where malformed mapping attributes invalidate the replay result.
        /// </summary>
        [Test]
        public void LoadSelections_MalformedMappingReturnsNull()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                string filePath = Path.Combine(tmp.Path, "malformed.xml");
                new XDocument(
                    new XElement("FileList",
                        new XElement("File", new XAttribute("FileFrom", "A.txt"), new XAttribute("FileTo", "Data/A.txt")),
                        new XElement("File", new XAttribute("FileFrom", "B.txt"))))
                    .Save(filePath);
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(filePath);

                Assert.IsNull(cache.LoadSelections());
            }
        }
        /// <summary>
        /// Verifies that generated payloads and basic-install operations are persisted in exact replay order.
        /// </summary>
        [Test]
        public void CompleteReplay_PreservesGeneratedPayloadAndOperationOrder()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                ScriptProxyContext ctx = new ScriptProxyContext(tmp.Path, null, false, false, "linked");
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(ctx.Mod, ctx.GameMode);
                byte[] generated = { 1, 3, 3, 7, 9 };

                cache.RecordSelection("A.txt", "Data/A.txt");
                cache.RecordGeneratedFile("config/generated.ini", generated);
                cache.RecordBasicInstall();

                Assert.IsTrue(cache.HasCompleteReplay);
                IReadOnlyList<ScriptedReplayOperation> operations = cache.LoadReplayOperations();
                Assert.AreEqual(3, operations.Count);
                Assert.AreEqual(ScriptedReplayOperationKind.ArchiveFile, operations[0].Kind);
                Assert.AreEqual(ScriptedReplayOperationKind.GeneratedFile, operations[1].Kind);
                Assert.AreEqual("config/generated.ini", operations[1].DestinationPath);
                CollectionAssert.AreEqual(generated, File.ReadAllBytes(operations[1].PayloadPath));
                Assert.AreEqual(ScriptedReplayOperationKind.BasicInstall, operations[2].Kind);
                CollectionAssert.AreEqual(new[] { "A.txt" }, cache.LoadSelections().ConvertAll(x => x.Key));
            }
        }

        /// <summary>
        /// Verifies that profile copies include generated sidecars and artifact deletion removes both representations.
        /// </summary>
        [Test]
        public void CopyArtifacts_CopiesAndDeletesGeneratedPayloadSidecars()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                string source = Path.Combine(tmp.Path, "source.xml");
                string destination = Path.Combine(tmp.Path, "profile", "target.xml");
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(source);
                cache.RecordGeneratedFile("generated.cfg", new byte[] { 4, 2, 1 });

                ScriptedFileSelectionCache.CopyArtifacts(source, destination);

                ScriptedFileSelectionCache copied = new ScriptedFileSelectionCache(destination);
                Assert.IsTrue(copied.HasCompleteReplay);
                IReadOnlyList<ScriptedReplayOperation> operations = copied.LoadReplayOperations();
                Assert.AreEqual(1, operations.Count);
                CollectionAssert.AreEqual(new byte[] { 4, 2, 1 }, File.ReadAllBytes(operations[0].PayloadPath));

                ScriptedFileSelectionCache.DeleteArtifacts(destination);
                Assert.IsFalse(File.Exists(destination));
                Assert.IsFalse(Directory.Exists(ScriptedFileSelectionCache.GetPayloadDirectoryPath(destination)));
            }
        }

        /// <summary>
        /// Verifies that legacy archive-only caches remain readable but are not advertised as exact Direct replay sources.
        /// </summary>
        [Test]
        public void LegacyCache_RemainsReadableButIsNotCompleteReplay()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                string filePath = Path.Combine(tmp.Path, "legacy.xml");
                new XDocument(
                    new XElement("FileList",
                        new XElement("File", new XAttribute("FileFrom", "A.txt"), new XAttribute("FileTo", "Data/A.txt"))))
                    .Save(filePath);
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(filePath);

                Assert.IsFalse(cache.HasCompleteReplay);
                Assert.AreEqual(1, cache.LoadReplayOperations().Count);
                Assert.AreEqual(1, cache.LoadSelections().Count);
            }
        }

        /// <summary>
        /// Verifies that replay cleanup keeps the legacy force-delete behavior for read-only artifacts.
        /// </summary>
        [Test]
        public void DeleteArtifacts_RemovesReadOnlyReplayArtifacts()
        {
            using (TemporaryDirectory tmp = new TemporaryDirectory())
            {
                string cachePath = Path.Combine(tmp.Path, "readonly.xml");
                ScriptedFileSelectionCache cache = new ScriptedFileSelectionCache(cachePath);
                cache.RecordGeneratedFile("generated.cfg", new byte[] { 8, 6, 7, 5, 3, 0, 9 });
                string payloadPath = cache.LoadReplayOperations()[0].PayloadPath;
                File.SetAttributes(cachePath, File.GetAttributes(cachePath) | FileAttributes.ReadOnly);
                File.SetAttributes(payloadPath, File.GetAttributes(payloadPath) | FileAttributes.ReadOnly);

                ScriptedFileSelectionCache.DeleteArtifacts(cachePath);

                Assert.IsFalse(File.Exists(cachePath));
                Assert.IsFalse(Directory.Exists(ScriptedFileSelectionCache.GetPayloadDirectoryPath(cachePath)));
            }
        }


    }
}
