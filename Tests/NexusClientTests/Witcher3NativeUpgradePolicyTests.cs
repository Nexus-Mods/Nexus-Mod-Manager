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
    public sealed class Witcher3NativeUpgradePolicyTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "w3r-upgrade-policy-fixture");
        private static readonly string[] ModFiles = { @"Mods\modA\content\one.ws", @"Mods\modA\content\two.ws" };
        private static ModDeploymentTarget Target(string path) => ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, path);
        private static InstallLogReadMod Owner(string key, ModInstallMethod method = ModInstallMethod.Direct,
            ModInstallRoot root = ModInstallRoot.Data, bool scripted = false) =>
            new InstallLogReadMod(key, key + ".zip", key + ".zip", key,
                "42", "11", "1", "1", scripted, root, method, false);

        private static NativeStateCaptureSnapshot Native(string[] files, ModInstallMethod method = ModInstallMethod.Direct,
            bool shared = false, bool scripted = false, bool iniEffect = false, string extraPath = null)
        {
            var keys = shared ? new[] { "owner", "stranger" } : new[] { "owner" };
            var paths = extraPath == null ? files : files.Concat(new[] { extraPath }).ToArray();
            var owners = shared ? new[] { Owner("owner", method, scripted: scripted), Owner("stranger", method) } :
                new[] { Owner("owner", method, scripted: scripted) };
            var nativeFiles = method == ModInstallMethod.Direct
                ? paths.Select(file => new InstallLogReadFile(file, Target(file), keys))
                : Enumerable.Empty<InstallLogReadFile>();
            var links = method == ModInstallMethod.Virtual
                ? paths.Select(file => new VirtualModReadLink(Target(file), "owner", "owner.zip", true, 1, "stage"))
                : Enumerable.Empty<VirtualModReadLink>();
            var ini = iniEffect ? new[] { new InstallLogReadIniEdit("user.settings", "A", "B",
                new[] { new InstallLogReadStringValue("owner", "value") }) } : new InstallLogReadIniEdit[0];
            var log = new InstallLogReadSnapshot("original-values", 12, owners, nativeFiles,
                ini, new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
            return new NativeStateCaptureSnapshot(log, new VirtualModReadSnapshot(links),
                new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, Root),
                        new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, Root) },
                new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
                new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
                NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
        }

        private static Witcher3NativeUpgradeDecision Assess(NativeStateCaptureSnapshot native, string[] onDisk,
            string[] targets, ModInstallMethod method = ModInstallMethod.Direct, bool complete = true,
            string owner = "owner", ModInstallRoot root = ModInstallRoot.Data)
        {
            var folders = onDisk.Select(file => file.Split('\\')[0] + "\\" + file.Split('\\')[1]).Distinct().ToArray();
            var disk = new Witcher3ComponentDiskObservation(Root, onDisk, folders, complete);
            var index = Witcher3NativeComponentIndexer.Build(native, Root, disk);
            return Witcher3NativeUpgradePolicy.Assess(index, native, owner, method, root, targets);
        }

        [Test]
        public void DirectExactFileSet_IsOnlyACandidateAndPreservesSequence()
        {
            var native = Native(ModFiles);
            var decision = Assess(native, ModFiles, ModFiles);
            Assert.That(decision.Status, Is.EqualTo(Witcher3NativeUpgradeStatus.ExactFootprintCandidate));
            Assert.That(decision.IsExactFootprintCandidate, Is.True);
            Assert.That(decision.NativeDeploymentCommitSequence, Is.EqualTo(12));
            Assert.That(decision.ExactTargets.Count, Is.EqualTo(2));
        }

        [Test]
        public void VirtualExactFileSet_CanBeCharacterizedWithoutCreatingALink()
        {
            var native = Native(ModFiles, ModInstallMethod.Virtual);
            Assert.That(Assess(native, ModFiles, ModFiles, ModInstallMethod.Virtual).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.ExactFootprintCandidate));
        }

        [Test]
        public void DlcPlusModUnderOneOwner_RemainsTwoComponents()
        {
            var files = new[] { ModFiles[0], @"DLC\dlcExpansion\content\one.bundle" };
            Assert.That(Assess(Native(files), files, files).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.ExactFootprintCandidate));
        }

        [TestCase("new")]
        [TestCase("deleted")]
        [TestCase("moved")]
        public void AdditionRemovalOrMove_RequiresReviewedFootprintChange(string change)
        {
            var proposed = change == "new" ? ModFiles.Concat(new[] { @"Mods\modA\content\three.ws" }).ToArray() :
                change == "deleted" ? new[] { ModFiles[0] } :
                new[] { ModFiles[0], @"Mods\modA\content\renamed.ws" };
            var decision = Assess(Native(ModFiles), ModFiles, proposed);
            Assert.That(decision.Status, Is.EqualTo(Witcher3NativeUpgradeStatus.ReviewedFootprintChangeCandidate));
            Assert.That(decision.IsExactFootprintCandidate, Is.False);
        }

        [Test]
        public void SharedNativeFileHistory_DeniesEvenWhenFilesMatch()
        {
            Assert.That(Assess(Native(ModFiles, shared: true), ModFiles, ModFiles).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.SharedOrUnmanagedComponent));
        }

        [Test]
        public void UnmanagedFileInOwnedFolder_BlocksUpgrade()
        {
            var disk = ModFiles.Concat(new[] { @"Mods\modA\content\manual.ws" }).ToArray();
            Assert.That(Assess(Native(ModFiles), disk, ModFiles).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.SharedOrUnmanagedComponent));
        }

        [Test]
        public void UndeployedNativeFileOrPartialDiskScan_Blocks()
        {
            Assert.That(Assess(Native(ModFiles), new[] { ModFiles[0] }, ModFiles).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.NativeStateUncertain));
            Assert.That(Assess(Native(ModFiles), ModFiles, ModFiles, complete: false).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.NativeStateUncertain));
        }

        [Test]
        public void MethodOrRootChange_Denies()
        {
            var native = Native(ModFiles);
            Assert.That(Assess(native, ModFiles, ModFiles, ModInstallMethod.Virtual).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.MethodOrRootMismatch));
            Assert.That(Assess(native, ModFiles, ModFiles, root: ModInstallRoot.GameRoot).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.MethodOrRootMismatch));
        }

        [Test]
        public void UnknownOwnerOrScriptedAndIniNativeEffects_Deny()
        {
            Assert.That(Assess(Native(ModFiles), ModFiles, ModFiles, owner: "other").Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.OwnerNotFound));
            Assert.That(Assess(Native(ModFiles, scripted: true), ModFiles, ModFiles).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.NonFileEffectsBlocked));
            Assert.That(Assess(Native(ModFiles, iniEffect: true), ModFiles, ModFiles).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.NonFileEffectsBlocked));
        }

        [Test]
        public void ExtraNativeOwnershipOutsideComponent_RejectsSilentCleanup()
        {
            var native = Native(ModFiles, extraPath: @"bin\config\old.xml");
            Assert.That(Assess(native, ModFiles, ModFiles).IsExactFootprintCandidate, Is.False);
        }

        [Test]
        public void DuplicateOrReservedMergeTargets_AreInvalid()
        {
            var native = Native(ModFiles);
            Assert.That(Assess(native, ModFiles, new[] { ModFiles[0], ModFiles[0] }).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.InvalidTargetSet));
            Assert.That(Assess(native, ModFiles, new[] { @"Mods\mod0000_MergedFiles\content\merged.ws" }).Status,
                Is.EqualTo(Witcher3NativeUpgradeStatus.InvalidTargetSet));
        }
    }
}
