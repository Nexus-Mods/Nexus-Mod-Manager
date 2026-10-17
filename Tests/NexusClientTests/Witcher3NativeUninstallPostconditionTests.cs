using System;
using System.IO;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Witcher3;
using NUnit.Framework;

namespace NexusClientTests
{
    [TestFixture]
    public class Witcher3NativeUninstallPostconditionTests
    {
        private const string FilePath = @"Mods\modA\content\a.ws";
        private static ModDeploymentTarget Target =>
            ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, FilePath);

        private static NativeStateCaptureSnapshot After(bool registration = false, bool file = false,
            bool virtualLink = false, bool promoted = false, bool iniEffect = false)
        {
            var owner = new InstallLogReadMod("owner", "old.zip", "old.zip", "Old", "1", "1", "1", "1",
                false, ModInstallRoot.Data, ModInstallMethod.Direct, false);
            var mods = registration ? new[] { owner } : new InstallLogReadMod[0];
            var files = file ? new[] { new InstallLogReadFile(FilePath, Target, new[] { "owner" }) } : new InstallLogReadFile[0];
            var links = virtualLink ? new[] { new VirtualModReadLink(Target, "owner", "old.zip", true, 1, "stage") }
                : new VirtualModReadLink[0];
            var deployments = promoted ? new[] { new InstallLogReadDeploymentTarget(Target, new[] { "owner" }) }
                : new InstallLogReadDeploymentTarget[0];
            var ini = iniEffect ? new[] { new InstallLogReadIniEdit("user.settings", "A", "B",
                new[] { new InstallLogReadStringValue("owner", "old") }) } : new InstallLogReadIniEdit[0];
            var log = new InstallLogReadSnapshot("original-values", 28, mods, files,
                ini, new InstallLogReadGameValue[0], deployments);
            return new NativeStateCaptureSnapshot(log, new VirtualModReadSnapshot(links),
                new[] { new NativeStateCaptureRoot(ModDeploymentRoot.Data, Path.GetTempPath()) },
                new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
                new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0],
                NativeStateCaptureCoverage.NotApplicable, new NativeStateCaptureIssue[0]);
        }

        [Test]
        public void OwnerAndFilesAbsent_IsReconciledWithoutWrites()
        {
            Assert.DoesNotThrow(() => Witcher3NativeUninstallPostcondition.RequireReconciled(
                After(), "owner", new[] { FilePath }, target => false));
        }

        [TestCase(true, false, false, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, true, false)]
        [TestCase(false, false, false, true)]
        public void AnyStaleRegistrationOrNativeOwnership_RequiresRecovery(
            bool registration, bool file, bool virtualLink, bool promoted)
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallPostcondition.RequireReconciled(
                After(registration, file, virtualLink, promoted), "owner", new[] { FilePath }, target => false));
        }

        [Test]
        public void ResidualIniOwner_IsAlsoAnIncompleteRemoval()
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallPostcondition.RequireReconciled(
                After(iniEffect: true), "owner", new[] { FilePath }, target => false));
        }

        [Test]
        public void LeftoverPhysicalTarget_RequiresRecovery()
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallPostcondition.RequireReconciled(
                After(), "owner", new[] { FilePath }, target => true));
        }

        [Test]
        public void NoTargetsOrDuplicateTargets_CannotBeReconciled()
        {
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallPostcondition.RequireReconciled(
                After(), "owner", new string[0], target => false));
            Assert.Throws<InvalidDataException>(() => Witcher3NativeUninstallPostcondition.RequireReconciled(
                After(), "owner", new[] { FilePath, FilePath }, target => false));
        }
    }
}
