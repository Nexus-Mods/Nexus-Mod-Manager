using System;
using System.IO;
using Nexus.Client.GameStorage;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public sealed class Witcher3RecoverySelectionGuardTests
    {
        private string _root;
        private GameStorageService _storage;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "w3r-recovery-guard", Guid.NewGuid().ToString("N"));
            _storage = new GameStorageService(Path.Combine(_root, "Registry"), new Version(0, 93, 1));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private GameStoragePathSet Paths(string gameId) => new GameStoragePathSet
        {
            GameId = gameId,
            GameName = "The Witcher 3",
            GameInstallPath = Path.Combine(_root, "Game"),
            InstallInfoPath = Path.Combine(_root, "InstallInfo"),
            ModsPath = Path.Combine(_root, "Mods"),
            VirtualInstallPath = Path.Combine(_root, "VirtualInstall")
        };

        private GameStorageCandidate Candidate(string gameId) => new GameStorageCandidate
        {
            GameId = gameId,
            CandidateRoot = _root,
            InstallInfoPath = Path.Combine(_root, "InstallInfo"),
            ModsPath = Path.Combine(_root, "Mods"),
            VirtualInstallPath = Path.Combine(_root, "VirtualInstall")
        };

        [TestCase("Witcher3")]
        [TestCase("Witcher3Remastered")]
        public void WithoutActiveGameAuthority_EvenUnvalidatedRecoveryCannotAdoptPaths(string modeId)
        {
            // Registry-only service has no IEnvironmentInfo and must never claim that
            // another Witcher mode has no physical game-root ownership.
            var result = _storage.ValidateRecoverySelection(Paths(modeId), Candidate(modeId));
            Assert.That(result.IsUsable, Is.False);
            Assert.That(result.ToUserMessage(), Does.Contain("duplicate game-root ownership"));
            Assert.That(_storage.ApplySelectedCandidatePaths(Paths(modeId), Candidate(modeId)), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "InstallInfo")), Is.False);
        }

        [Test]
        public void InitialSetupCheckOccursBeforeDirectoryCreation()
        {
            GameStorageHealthCheck result;
            bool accepted = _storage.ApplyInitialSetupCandidate(Paths("Witcher3Remastered"),
                Candidate("Witcher3Remastered"), out result);
            Assert.That(accepted, Is.False);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.IsUsable, Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "Mods")), Is.False);
        }
    }
}
