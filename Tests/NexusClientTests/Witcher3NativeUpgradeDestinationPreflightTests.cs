using System;
using System.IO;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public sealed class Witcher3NativeUpgradeDestinationPreflightTests
    {
        private string _root;
        private const string ModTarget = @"Mods\modOwned\content\scripts\one.ws";
        private const string DlcTarget = @"DLC\dlcOwned\content\one.bundle";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "nmm-w3r-upgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void CreateFile(string gameRelativePath)
        {
            string file = Path.Combine(_root, gameRelativePath.Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
        }

        [Test]
        public void ExistingModAndDlcFiles_AreReadOnlyCandidates()
        {
            CreateFile(ModTarget);
            CreateFile(DlcTarget);
            Assert.DoesNotThrow(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new[] { ModTarget, DlcTarget }));
            Assert.That(File.ReadAllBytes(Path.Combine(_root, ModTarget.Replace('\\', Path.DirectorySeparatorChar))),
                Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void MissingExistingFile_FailsClosed()
        {
            CreateFile(ModTarget);
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new[] { ModTarget, DlcTarget }));
        }

        [Test]
        public void DirectoryAtFileTarget_IsNotAnOwnedFile()
        {
            Directory.CreateDirectory(Path.Combine(_root, ModTarget.Replace('\\', Path.DirectorySeparatorChar)));
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new[] { ModTarget }));
        }

        [Test]
        public void FileAtAncestorPath_FailsClosed()
        {
            string folder = Path.Combine(_root, "Mods", "modOwned");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "content"), new byte[] { 1 });
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new[] { ModTarget }));
        }

        [Test]
        public void DuplicateOrEmptyTargetSet_FailsClosed()
        {
            CreateFile(ModTarget);
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new[] { ModTarget, ModTarget }));
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new string[0]));
        }

        [TestCase(@"Mods\modOwned\content\..\outside")]
        [TestCase(@"bin\config\unsafe.xml")]
        [TestCase(@"Mods\modOwned\content\one.ws:ads")]
        public void InvalidOrOutsidePayloadPaths_CannotBeReviewed(string path)
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUpgradeDestinationPreflight.RequireExistingOwnedFiles(
                _root, new[] { path }));
        }
    }
}
