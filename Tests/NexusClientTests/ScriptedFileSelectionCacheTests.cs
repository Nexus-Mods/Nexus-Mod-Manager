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
    }
}
