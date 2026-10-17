using System;
using System.IO;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public class Witcher3NativeDestinationPreflightTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "nmm-w3r-native-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void NewModAndDlcFolders_AreEligibleForFurtherNativeReview()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Mods"));
            Directory.CreateDirectory(Path.Combine(_root, "DLC"));
            Assert.DoesNotThrow(() => Witcher3NativeDestinationPreflight.RequireNewComponents(_root,
                new[] { @"Mods\modNew\content\a.ws", @"Mods\modNew\content\b.ws",
                    @"DLC\dlcNew\content\a.bundle" }));
            Assert.That(Directory.Exists(Path.Combine(_root, "Mods", "modNew")), Is.False);
        }

        [Test]
        public void ExistingManualOrManagedComponent_IsBlockedEvenForNonCollidingNewFile()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Mods", "modExisting"));
            Assert.Throws<InvalidDataException>(() => Witcher3NativeDestinationPreflight.RequireNewComponents(
                _root, new[] { @"Mods\modExisting\content\new.ws" }));
        }

        [Test]
        public void ExistingTargetFile_IsBlocked()
        {
            string directory = Path.Combine(_root, "DLC", "dlcA", "content");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "a.bundle"), new byte[] { 1 });
            Assert.Throws<InvalidDataException>(() => Witcher3NativeDestinationPreflight.RequireNewComponents(
                _root, new[] { @"DLC\dlcA\content\a.bundle" }));
        }

        [Test]
        public void ExistingFileInAncestorPath_IsBlocked()
        {
            File.WriteAllBytes(Path.Combine(_root, "Mods"), new byte[] { 1 });
            Assert.Throws<InvalidDataException>(() => Witcher3NativeDestinationPreflight.RequireNewComponents(
                _root, new[] { @"Mods\modA\content\a.ws" }));
        }

        [TestCase(@"Mods\modA\content\..\escape")]
        [TestCase(@"bin\config\unsafe.xml")]
        [TestCase(@"content\a.ws")]
        public void UnreviewedOrUnsafeTarget_IsRejected(string path)
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeDestinationPreflight.RequireNewComponents(
                _root, new[] { path }));
        }

        [Test]
        public void UnrelatedExistingComponent_DoesNotBlockASeparateNewFolder()
        {
            Directory.CreateDirectory(Path.Combine(_root, "Mods", "modAnother"));
            Assert.DoesNotThrow(() => Witcher3NativeDestinationPreflight.RequireNewComponents(
                _root, new[] { @"Mods\modNew\content\a.ws" }));
        }
    }
}
