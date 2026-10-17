using System;
using System.IO;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public sealed class Witcher3NativeWritePolicyTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "w3r-write-policy-fixture");
        private const string Original = "original-values";

        private static ModDeploymentTarget Target(string path) => ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, path);
        private static InstallLogReadMod Owner(string key) => new InstallLogReadMod(key, key + ".zip", key + ".zip",
            "NMM " + key, "42", "11", "1", "1", false, ModInstallRoot.Data, ModInstallMethod.Direct, false);

        private static Witcher3NativeComponentIndex Index(string[] componentFolders, string[] files,
            string[] nativeOwners = null, string[] history = null, bool complete = true)
        {
            string[] owners = nativeOwners ?? new string[0];
            var nativeFiles = history == null ? new InstallLogReadFile[0] :
                new[] { new InstallLogReadFile(history[0], Target(history[0]), history.Skip(1)) };
            var log = new InstallLogReadSnapshot(Original, 7, owners.Select(Owner), nativeFiles,
                new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
                new InstallLogReadDeploymentTarget[0]);
            var state = new NativeStateCaptureSnapshot(log, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
                new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, Root),
                        new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, Root) },
                new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
                new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
                NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
            var disk = new Witcher3ComponentDiskObservation(Root, files, componentFolders, complete);
            return Witcher3NativeComponentIndexer.Build(state, Root, disk);
        }

        [Test]
        public void EmptyNativeAndDiskInventory_AllowsOnlyNewComponents()
        {
            var index = Index(new string[0], new string[0]);
            var decision = Witcher3NativeWritePolicy.Assess(index, new[]
            {
                @"Mods\modAlpha\content\a.ws", @"DLC\dlcBeta\content\b.bundle"
            });
            Assert.That(decision.Status, Is.EqualTo(Witcher3NativeWriteStatus.NewComponentsEligible));
            Assert.That(decision.NativeDeploymentCommitSequence, Is.EqualTo(7));
        }

        [Test]
        public void EmptyAndDuplicateTargetSets_AreDenied()
        {
            var index = Index(new string[0], new string[0]);
            Assert.That(Witcher3NativeWritePolicy.Assess(index, new string[0]).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.InvalidTargetSet));
            Assert.That(Witcher3NativeWritePolicy.Assess(index, new[]
            {
                @"Mods\modAlpha\content\a.ws", @"Mods\modAlpha\content\A.ws"
            }).Status, Is.EqualTo(Witcher3NativeWriteStatus.InvalidTargetSet));
        }

        [TestCase(@"Mods\modA\content\..\escape")]
        [TestCase(@"content\some.ws")]
        [TestCase(@"Mods\modA\content\valid.ws:stream")]
        public void UnreviewedPaths_AreDenied(string path)
        {
            Assert.That(Witcher3NativeWritePolicy.Assess(Index(new string[0], new string[0]), new[] { path }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.InvalidTargetSet));
        }

        [TestCase(@"Mods\mod0000_MergedFiles\content\merged.ws")]
        [TestCase(@"Mods\mod0000____CompilationTrigger\content\trigger.ws")]
        public void ReservedGeneratedMergeFolders_AreNotBasicInstallTargets(string path)
        {
            var index = Index(new string[0], new string[0]);
            Assert.That(Witcher3NativeWritePolicy.Assess(index, new[] { path }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.InvalidTargetSet));
        }

        [Test]
        public void UnmanagedFolder_DoesNotBecomeNativeOwned()
        {
            var index = Index(new[] { @"Mods\modManual" }, new[] { @"Mods\modManual\content\a.ws" });
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modManual\content\b.ws" }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.ExternalComponentBlocked));
        }

        [Test]
        public void UnmanagedDlcFolder_IsAlsoProtected()
        {
            var index = Index(new[] { @"DLC\dlcManual" }, new[] { @"DLC\dlcManual\content\a.bundle" });
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"DLC\dlcManual\content\b.bundle" }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.ExternalComponentBlocked));
        }

        [Test]
        public void UnrelatedManualFolder_DoesNotBlockNewManagedFolder()
        {
            var index = Index(new[] { @"Mods\modManual" }, new[] { @"Mods\modManual\content\a.ws" });
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modFresh\content\new.ws" }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.NewComponentsEligible));
        }

        [Test]
        public void SameNativeOwner_IsAnUpdateCandidateButStillBlocked()
        {
            const string previous = @"Mods\modExisting\content\old.ws";
            var index = Index(new[] { @"Mods\modExisting" }, new[] { previous },
                new[] { "owner" }, new[] { previous, "owner" });
            Assert.That(index.Issues, Is.Empty);
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modExisting\content\new.ws" }, "owner").Status,
                Is.EqualTo(Witcher3NativeWriteStatus.OwnedUpdateCandidateBlocked));
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modExisting\content\new.ws" }, "different").Status,
                Is.EqualTo(Witcher3NativeWriteStatus.ManagedComponentBlocked));
        }

        [Test]
        public void MixedUnmanagedAndManagedFiles_RequireReview()
        {
            const string previous = @"Mods\modExisting\content\old.ws";
            var index = Index(new[] { @"Mods\modExisting" },
                new[] { previous, @"Mods\modExisting\content\manual.ws" },
                new[] { "owner" }, new[] { previous, "owner" });
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modExisting\content\new.ws" }, "owner").Status,
                Is.EqualTo(Witcher3NativeWriteStatus.SharedComponentBlocked));
        }

        [Test]
        public void ShadowedNativeHistory_IsNotExclusiveOwnership()
        {
            const string previous = @"Mods\modExisting\content\old.ws";
            var index = Index(new[] { @"Mods\modExisting" }, new[] { previous },
                new[] { "first", "second" }, new[] { previous, "first", "second" });
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modExisting\content\new.ws" }, "second").Status,
                Is.EqualTo(Witcher3NativeWriteStatus.SharedComponentBlocked));
        }

        [Test]
        public void IncompleteDiskInventory_NeverProvesAbsence()
        {
            var index = Index(new string[0], new string[0], complete: false);
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modNew\content\one.ws" }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.NativeStateUncertain));
        }

        [Test]
        public void NativeDrift_NeverAuthorizesNewFolderEvenElsewhere()
        {
            const string previous = @"Mods\modMissing\content\old.ws";
            var index = Index(new string[0], new string[0],
                new[] { "owner" }, new[] { previous, "owner" });
            Assert.That(index.Issues.Any(), Is.True);
            Assert.That(Witcher3NativeWritePolicy.Assess(index,
                new[] { @"Mods\modNew\content\new.ws" }).Status,
                Is.EqualTo(Witcher3NativeWriteStatus.NativeStateUncertain));
        }
    }
}
