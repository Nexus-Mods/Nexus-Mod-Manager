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
    public sealed class Witcher3NativeUninstallPolicyTests
    {
        private static readonly string Root = Path.Combine(Path.GetTempPath(), "w3r-uninstall-policy-fixture");
        private static readonly string[] Files = { @"Mods\modOne\content\a.ws", @"Mods\modOne\content\b.ws" };

        private static ModDeploymentTarget Target(string path) =>
            ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, path);

        private static InstallLogReadMod Owner(string key, ModInstallMethod method, bool scripted = false,
            ModInstallRoot root = ModInstallRoot.Data) =>
            new InstallLogReadMod(key, key + ".zip", key + ".zip", key,
                "42", "11", "1", "1", scripted, root, method, false);

        private static NativeStateCaptureSnapshot Native(string[] paths, ModInstallMethod method = ModInstallMethod.Direct,
            bool shared = false, bool scripted = false, bool ini = false, string outside = null,
            bool explicitFallbackAbsent = true, ModInstallRoot root = ModInstallRoot.Data)
        {
            string[] all = outside == null ? paths : paths.Concat(new[] { outside }).ToArray();
            string[] keys = shared ? new[] { "owner", "other" } : new[] { "owner" };
            var registrations = shared
                ? new[] { Owner("owner", method, scripted, root), Owner("other", method) }
                : new[] { Owner("owner", method, scripted, root) };
            var records = method == ModInstallMethod.Direct
                ? all.Select(path => new InstallLogReadFile(path, Target(path), keys))
                : Enumerable.Empty<InstallLogReadFile>();
            var links = method == ModInstallMethod.Virtual
                ? all.Select(path => new VirtualModReadLink(Target(path), "owner", "owner.zip", true, 1, "stage"))
                : Enumerable.Empty<VirtualModReadLink>();
            var fallbacks = method == ModInstallMethod.Virtual && explicitFallbackAbsent
                ? all.Select(path => new NativeStateCaptureVirtualFallback(Target(path),
                    NativeStateCaptureVirtualFallbackState.ExplicitlyAbsent, null))
                : Enumerable.Empty<NativeStateCaptureVirtualFallback>();
            var iniEdits = ini ? new[] { new InstallLogReadIniEdit("user.settings", "A", "B",
                new[] { new InstallLogReadStringValue("owner", "value") }) }
                : new InstallLogReadIniEdit[0];
            var snapshot = new InstallLogReadSnapshot("original-values", 27, registrations, records,
                iniEdits, new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
            return new NativeStateCaptureSnapshot(snapshot, new VirtualModReadSnapshot(links),
                new NativeStateCaptureVirtualPayloadSource[0], fallbacks,
                new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, Root),
                    new NativeStateCaptureRoot(ModDeploymentRoot.GameRoot, Root) },
                new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
                new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
                NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
        }

        private static Witcher3NativeUninstallDecision Assess(NativeStateCaptureSnapshot native, string[] diskFiles,
            bool complete = true, string ownerKey = "owner")
        {
            var folders = diskFiles.Select(file => file.Split('\\')[0] + "\\" + file.Split('\\')[1])
                .Distinct().ToArray();
            var disk = new Witcher3ComponentDiskObservation(Root, diskFiles, folders, complete);
            var index = Witcher3NativeComponentIndexer.Build(native, Root, disk);
            return Witcher3NativeUninstallPolicy.Assess(index, native, ownerKey);
        }

        [Test]
        public void ExclusiveDirectOwnership_ProducesCharacterizationNotAnUninstall()
        {
            var result = Assess(Native(Files), Files);
            Assert.That(result.IsExactRemovalCandidate, Is.True);
            Assert.That(result.NativeDeploymentCommitSequence, Is.EqualTo(27));
            Assert.That(result.ExactTargets.Count, Is.EqualTo(2));
        }

        [Test]
        public void OneArchiveCanOwnSeveralModsAndDlcComponents()
        {
            var paths = new[] { Files[0], @"Mods\modTwo\content\two.ws", @"DLC\dlcExtra\content\pack.bundle" };
            Assert.That(Assess(Native(paths), paths).ExactTargets.Count, Is.EqualTo(3));
        }

        [Test]
        public void VirtualRequiresExplicitlyAbsentFallbackForEveryTarget()
        {
            Assert.That(Assess(Native(Files, ModInstallMethod.Virtual), Files).IsExactRemovalCandidate, Is.True);
            Assert.That(Assess(Native(Files, ModInstallMethod.Virtual, explicitFallbackAbsent: false), Files).Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.VirtualFallbackUnverified));
        }

        [Test]
        public void MissingOrIncompleteDiskInventory_DeniesRemoval()
        {
            Assert.That(Assess(Native(Files), new[] { Files[0] }).IsExactRemovalCandidate, Is.False);
            Assert.That(Assess(Native(Files), Files, complete: false).Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.NativeStateUncertain));
        }

        [Test]
        public void SharedOwnerAndManualFiles_AreProtected()
        {
            Assert.That(Assess(Native(Files, shared: true), Files).IsExactRemovalCandidate, Is.False);
            Assert.That(Assess(Native(Files), Files.Concat(new[] { @"Mods\modOne\content\manual.ws" }).ToArray())
                .Status, Is.EqualTo(Witcher3NativeUninstallStatus.SharedOrUnmanagedComponent));
        }

        [Test]
        public void ScriptIniAndUnsupportedRoot_NeverBecomeSimpleUninstalls()
        {
            Assert.That(Assess(Native(Files, scripted: true), Files).Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.NonFileEffectsBlocked));
            Assert.That(Assess(Native(Files, ini: true), Files).Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.NonFileEffectsBlocked));
            Assert.That(Assess(Native(Files, root: ModInstallRoot.GameRoot), Files).IsExactRemovalCandidate, Is.False);
        }

        [Test]
        public void OutsideComponentOldNativeFile_IsNotSilentlyRemoved()
        {
            Assert.That(Assess(Native(Files, outside: @"bin\config\old.xml"), Files).Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.NativeFootprintMismatch));
        }

        [Test]
        public void MissingRegistrationOrUnidentifiedOwner_DeniesRemoval()
        {
            Assert.That(Assess(Native(Files), Files, ownerKey: "stranger").Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.OwnerNotFound));
            Assert.That(Assess(Native(Files), Files, ownerKey: " ").Status,
                Is.EqualTo(Witcher3NativeUninstallStatus.OwnerNotFound));
        }
    }
}
