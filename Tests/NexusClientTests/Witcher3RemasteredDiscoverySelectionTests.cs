using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Nexus.Client.Games;
using Nexus.Client.Games.DataDriven;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public class Witcher3RemasteredDiscoverySelectionTests
    {
        [Test]
        public void MultipleStoreCandidates_SkipTheFirstLegacyDx12Installation()
        {
            var stores = Stores("steam-4x", "gog-5x", "gog-older");
            var inspected = new List<string>();
            string selected = DataDrivenGameModeFactory.SelectEditionValidStorePath(stores,
                store => store.Id,
                path => { inspected.Add(path); return path == "gog-5x"; });

            Assert.That(selected, Is.EqualTo("gog-5x"));
            Assert.That(inspected, Is.EqualTo(new[] { "steam-4x", "gog-5x" }));
        }

        [Test]
        public void DuplicateStoreRoots_AreValidatedOnceAndLaterDistinctRootsRemainAvailable()
        {
            var stores = Stores("steam", "gog", "epic");
            int probes = 0;
            string selected = DataDrivenGameModeFactory.SelectEditionValidStorePath(stores,
                store => store.Id == "epic" ? "OtherRoot" : "SameRoot",
                path => { probes++; return path == "OtherRoot"; });

            Assert.That(selected, Is.EqualTo("OtherRoot"));
            Assert.That(probes, Is.EqualTo(2));
        }

        [Test]
        public void UnreadableCandidate_DoesNotHideAnotherStore()
        {
            string selected = DataDrivenGameModeFactory.SelectEditionValidStorePath(
                Stores("unreadable", "working"),
                store => { if (store.Id == "unreadable") throw new IOException("Access denied"); return store.Id; },
                path => path == "working");
            Assert.That(selected, Is.EqualTo("working"));
        }

        [Test]
        public void NoConfirmedFiveXCandidate_ReturnsNoInstallation()
        {
            Assert.That(DataDrivenGameModeFactory.SelectEditionValidStorePath(
                Stores("steam-4x", "gog-4x"), store => store.Id, _ => false), Is.Null);
            Assert.That(DataDrivenGameModeFactory.SelectEditionValidStorePath(
                null, store => store.Id, _ => true), Is.Null);
        }

        [Test]
        public void StorePathSelection_RequiresExplicitEditionValidator()
        {
            Assert.Throws<ArgumentNullException>(() => DataDrivenGameModeFactory.SelectEditionValidStorePath(
                Stores("x"), store => store.Id, null));
        }

        [Test]
        public void EditionConstrainedManualOverride_CannotBypassValidation()
        {
            string root = CreateExeFixture();
            try
            {
                var discoverer = new GameDiscoverer();
                var descriptor = new TestDescriptor() { Allow = false };
                Register(discoverer, descriptor, null);

                // The executable exists, but the edition validator rejects the root.
                Assert.That(discoverer.TryOverride(descriptor.ModeId, root), Is.False);
                Assert.Throws<InvalidOperationException>(() =>
                    discoverer.Override(descriptor.ModeId, root));
                Assert.That(discoverer.GetFinalPath(descriptor.ModeId), Is.Null);

                descriptor.Allow = true;
                Assert.That(discoverer.TryOverride(descriptor.ModeId, root), Is.True);
                Assert.That(discoverer.GetFinalPath(descriptor.ModeId), Is.EqualTo(root));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void EditionConstrainedCandidate_IsRevalidatedOnAccept()
        {
            string root = CreateExeFixture();
            try
            {
                var discoverer = new GameDiscoverer();
                var descriptor = new TestDescriptor() { Allow = true };
                Register(discoverer, descriptor, new[] { root });
                descriptor.Allow = false; // Installation changed since the background scan.
                Assert.That(discoverer.TryAccept(descriptor.ModeId), Is.False);
                Assert.That(discoverer.GetFinalPath(descriptor.ModeId), Is.Null);
                Assert.That(discoverer.HasCandidates(descriptor.ModeId), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void EditionConstrainedCandidate_StillAcceptsWhenValidatedAtSelection()
        {
            string root = CreateExeFixture();
            try
            {
                var discoverer = new GameDiscoverer();
                var descriptor = new TestDescriptor() { Allow = true };
                Register(discoverer, descriptor, new[] { root });
                Assert.That(discoverer.TryAccept(descriptor.ModeId), Is.True);
                Assert.That(discoverer.GetFinalPath(descriptor.ModeId), Is.EqualTo(root));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void LegacyManualOverride_RemainsPermissiveForItsOwnMode()
        {
            var discoverer = new GameDiscoverer();
            var descriptor = new LegacyDescriptor();
            Register(discoverer, descriptor, null);
            Assert.That(discoverer.TryOverride(descriptor.ModeId, @"C:\LegacyPath"), Is.True);
            Assert.That(discoverer.GetFinalPath(descriptor.ModeId), Is.EqualTo(@"C:\LegacyPath"));
        }

        private static IEnumerable<GameStoreInstallInfo> Stores(params string[] ids)
        {
            foreach (string id in ids)
            {
                GameStore store = id.StartsWith("gog", StringComparison.OrdinalIgnoreCase) ? GameStore.Gog :
                    id.StartsWith("epic", StringComparison.OrdinalIgnoreCase) ? GameStore.Epic : GameStore.Steam;
                yield return new GameStoreInstallInfo { Store = store, Id = id };
            }
        }

        private static string CreateExeFixture()
        {
            string root = Path.Combine(Path.GetTempPath(), "nmm-w3r-discovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "witcher3.exe"), new byte[] { 1 });
            return root;
        }

        private static void Register(GameDiscoverer discoverer, IGameModeDescriptor descriptor,
            IEnumerable<string> candidates)
        {
            var binding = BindingFlags.Instance | BindingFlags.NonPublic;
            var modes = (Dictionary<string, IGameModeDescriptor>)typeof(GameDiscoverer)
                .GetField("_gameModesById", binding).GetValue(discoverer);
            var queue = (Dictionary<string, Queue<string>>)typeof(GameDiscoverer)
                .GetField("_candidatePathsByGame", binding).GetValue(discoverer);
            modes.Add(descriptor.ModeId, descriptor);
            queue.Add(descriptor.ModeId, new Queue<string>(candidates ?? new string[0]));
        }

        private sealed class TestDescriptor : GameModeDescriptorBase, IGameInstallationCandidateValidator
        {
            public TestDescriptor() : base(null) { }

            public bool Allow { get; set; }
            public override string Name => "Witcher fixture";
            public override string ModeId => "Witcher3Remastered";
            public override string[] GameExecutables => new[] { "witcher3.exe" };
            public override string PluginDirectory => null;
            public override Theme ModeTheme => null;

            public bool IsValidInstallationCandidate(string absoluteGameRoot) => Allow;
        }

        private sealed class LegacyDescriptor : GameModeDescriptorBase
        {
            public LegacyDescriptor() : base(null) { }
            public override string Name => "Legacy fixture";
            public override string ModeId => "Witcher3";
            public override string[] GameExecutables => new[] { "witcher3.exe" };
            public override string PluginDirectory => null;
            public override Theme ModeTheme => null;
        }
    }
}
