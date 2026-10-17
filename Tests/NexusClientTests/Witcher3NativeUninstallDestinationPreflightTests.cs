using System;
using System.IO;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public sealed class Witcher3NativeUninstallDestinationPreflightTests
    {
        private string _root;
        private const string Target = @"Mods\modExample\content\some.ws";

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "w3r-uninstall-preflight", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Mods", "modExample", "content"));
            File.WriteAllText(Path.Combine(_root, "Mods", "modExample", "content", "some.ws"), "fixture");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null && Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void ExistingNonRedirectedFile_IsOnlyAnUninstallCandidate()
        {
            Assert.DoesNotThrow(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_root, new[] { Target }));
            Assert.That(File.Exists(Path.Combine(_root, "Mods", "modExample", "content", "some.ws")), Is.True);
        }

        [Test]
        public void MissingFileOrDirectory_IsBlocked()
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(
                _root, new[] { @"Mods\modExample\content\other.ws" }));
            Directory.Delete(Path.Combine(_root, "Mods", "modExample"), true);
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_root, new[] { Target }));
        }

        [Test]
        public void DuplicateNoncanonicalOrTraversal_IsRejected()
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_root, new[] { Target, Target }));
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_root, new[] { @"Mods\modExample\content\..\some.ws" }));
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_root, new string[0]));
        }

        [Test]
        public void FileCannotBeReinterpretedAsDirectory()
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallDestinationPreflight.RequireExistingTargets(_root,
                new[] { Target + @"\another.ws" }));
        }
    }
}
