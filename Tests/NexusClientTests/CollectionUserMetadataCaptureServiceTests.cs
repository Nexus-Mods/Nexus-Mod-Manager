using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.Games;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.Mods.Formats.FOMod;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C7.6 logical Sort/screenshot user-metadata capture characterization.
	/// </summary>
	public class CollectionUserMetadataCaptureServiceTests
	{
		[Test]
		public void Capture_PreservesExplicitSortStateAndRetainsCurrentScreenshotOverride()
		{
			string root = CreateTemporaryDirectory("nmm-c76-complete-");
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c76-complete");
				InstallLogReadSnapshot install = CreateInstall("native-a", Path.Combine(root, "Example.zip"), "100", "200");
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("native-a", Path.Combine(root, "Example.zip"), "100", "200")
				});
				CollectionsStore store;
				CollectionUserMetadataCaptureService service = CreateService(root, true, path =>
					new ModSortOrderRecord(7, path, "100", "200", 42, ModSortOrderAssignmentState.ExplicitNumeric, DateTime.UtcNow),
					true, path => new FOModScreenshotOverrideReadResult(FOModScreenshotOverrideReadState.Current,
						new FOModScreenshotOverrideRecord(123, 456, "fomod\\screenshot.png",
							Encoding.UTF8.GetBytes("custom-screenshot"), 789)), out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("30000000-0000-0000-0000-000000000001"));

				CollectionUserMetadataSnapshot snapshot = service.Capture(target, captureIdentity,
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.SortCoverage);
				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.ScreenshotCoverage);
				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				CollectionCapturedSortAssignment sort = snapshot.SortAssignments.Single();
				Assert.AreEqual("native-a", sort.NativeSnapshotKey);
				Assert.AreEqual(42, sort.SortNumber);
				Assert.AreEqual(ModSortOrderAssignmentState.ExplicitNumeric, sort.AssignmentState);
				CollectionCapturedScreenshotOverride screenshot = snapshot.ScreenshotOverrides.Single();
				Assert.AreEqual("fomod\\screenshot.png", screenshot.ScreenshotPath);
				Assert.AreEqual(123, screenshot.SourceArchiveLength);
				AssertRetainedText(store, screenshot.RetainedArtifact, "custom-screenshot");
				Assert.AreEqual(1, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_PreservesDistinctExplicitBlankAndInheritedNumericStates()
		{
			string root = CreateTemporaryDirectory("nmm-c76-sort-states-");
			try
			{
				string blankPath = Path.Combine(root, "Blank.zip");
				string inheritedPath = Path.Combine(root, "Inherited.zip");
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c76-sort-states");
				InstallLogReadSnapshot install = CreateInstall(new[]
				{
					CreateInstallMod("blank", blankPath, "101", "201"),
					CreateInstallMod("inherited", inheritedPath, "102", "202")
				});
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("blank", blankPath, "101", "201"),
					CreateInstalledMod("inherited", inheritedPath, "102", "202")
				});
				CollectionsStore store;
				CollectionUserMetadataCaptureService service = CreateService(root, true, path =>
				{
					if (path.EndsWith("Blank.zip", StringComparison.OrdinalIgnoreCase))
						return new ModSortOrderRecord(1, path, "101", "201", null, ModSortOrderAssignmentState.ExplicitBlank, DateTime.UtcNow);
					return new ModSortOrderRecord(2, path, "102", "202", 12, ModSortOrderAssignmentState.InheritedNumeric, DateTime.UtcNow);
				}, true, path => new FOModScreenshotOverrideReadResult(FOModScreenshotOverrideReadState.None, null), out store);

				CollectionUserMetadataSnapshot snapshot = service.Capture(target,
					LocalCaptureIdentity.From(Guid.Parse("30000000-0000-0000-0000-000000000002")),
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.Coverage);
				Assert.AreEqual(ModSortOrderAssignmentState.ExplicitBlank,
					snapshot.SortAssignments.Single(x => x.NativeSnapshotKey == "blank").AssignmentState);
				Assert.IsNull(snapshot.SortAssignments.Single(x => x.NativeSnapshotKey == "blank").SortNumber);
				Assert.AreEqual(ModSortOrderAssignmentState.InheritedNumeric,
					snapshot.SortAssignments.Single(x => x.NativeSnapshotKey == "inherited").AssignmentState);
				Assert.AreEqual(12, snapshot.SortAssignments.Single(x => x.NativeSnapshotKey == "inherited").SortNumber);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_UnavailableMetadataSurfacesFailClosedWithoutInventingValues()
		{
			string root = CreateTemporaryDirectory("nmm-c76-unavailable-");
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c76-unavailable");
				InstallLogReadSnapshot install = CreateInstall("native-a", "Missing.zip", "100", "200");
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("native-a", "Missing.zip", "100", "200")
				});
				CollectionsStore store;
				CollectionUserMetadataCaptureService service = CreateService(root, false, null, false, null, out store);

				CollectionUserMetadataSnapshot snapshot = service.Capture(target,
					LocalCaptureIdentity.From(Guid.Parse("30000000-0000-0000-0000-000000000003")),
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.SortCoverage);
				Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.ScreenshotCoverage);
				Assert.AreEqual(NativeStateCaptureCoverage.Unavailable, snapshot.Coverage);
				Assert.AreEqual(0, snapshot.SortAssignments.Count);
				Assert.AreEqual(0, snapshot.ScreenshotOverrides.Count);
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionUserMetadataIssueKind.SortServiceUnavailable));
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionUserMetadataIssueKind.ScreenshotMetadataUnavailable));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_MetadataReadFailuresBecomePartialCoverageInsteadOfInventedState()
		{
			string root = CreateTemporaryDirectory("nmm-c76-read-failure-");
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c76-read-failure");
				InstallLogReadSnapshot install = CreateInstall("native-a", "Failure.zip", "100", "200");
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("native-a", "Failure.zip", "100", "200")
				});
				CollectionsStore store;
				CollectionUserMetadataCaptureService service = CreateService(root, true, path =>
				{
					throw new InvalidOperationException("sort-read-failure");
				}, true, path =>
				{
					throw new InvalidOperationException("screenshot-read-failure");
				}, out store);

				CollectionUserMetadataSnapshot snapshot = service.Capture(target,
					LocalCaptureIdentity.From(Guid.Parse("30000000-0000-0000-0000-000000000006")),
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.SortCoverage);
				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.ScreenshotCoverage);
				Assert.AreEqual(NativeStateCaptureCoverage.Partial, snapshot.Coverage);
				Assert.AreEqual(0, snapshot.SortAssignments.Count);
				Assert.AreEqual(0, snapshot.ScreenshotOverrides.Count);
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionUserMetadataIssueKind.SortAssignmentUnavailable));
				Assert.IsTrue(snapshot.Issues.Any(x => x.Kind == CollectionUserMetadataIssueKind.ScreenshotOverrideInvalid));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_StaleScreenshotOverrideIsNotPartOfCurrentSetup()
		{
			string root = CreateTemporaryDirectory("nmm-c76-stale-");
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c76-stale");
				InstallLogReadSnapshot install = CreateInstall("native-a", "Stale.zip", "100", "200");
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("native-a", "Stale.zip", "100", "200")
				});
				CollectionsStore store;
				CollectionUserMetadataCaptureService service = CreateService(root, true, path =>
					new ModSortOrderRecord(1, path, "100", "200", null, ModSortOrderAssignmentState.BaselineBlank, DateTime.UtcNow),
					true, path => new FOModScreenshotOverrideReadResult(FOModScreenshotOverrideReadState.Stale,
						new FOModScreenshotOverrideRecord(1, 2, "fomod\\old.png", new byte[] { 1, 2, 3 }, 4)), out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("30000000-0000-0000-0000-000000000004"));

				CollectionUserMetadataSnapshot snapshot = service.Capture(target, captureIdentity,
					nativeState, identities, CancellationToken.None);

				Assert.AreEqual(NativeStateCaptureCoverage.Complete, snapshot.ScreenshotCoverage);
				Assert.AreEqual(0, snapshot.ScreenshotOverrides.Count);
				Assert.AreEqual(0, new CollectionsRetainedArtifactReferenceStore(store)
					.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString()).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Capture_SameScreenshotRoleRejectsChangedBytesInsteadOfSilentlyRebinding()
		{
			string root = CreateTemporaryDirectory("nmm-c76-rebind-");
			try
			{
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c76-rebind");
				InstallLogReadSnapshot install = CreateInstall("native-a", "Stable.zip", "100", "200");
				NativeStateCaptureSnapshot nativeState = CreateNativeState(install);
				CollectionInstalledIdentitySnapshot identities = CreateIdentities(target, install, new[]
				{
					CreateInstalledMod("native-a", "Stable.zip", "100", "200")
				});
				byte[] current = Encoding.UTF8.GetBytes("first");
				CollectionsStore store;
				CollectionUserMetadataCaptureService service = CreateService(root, true, path =>
					new ModSortOrderRecord(1, path, "100", "200", null, ModSortOrderAssignmentState.BaselineBlank, DateTime.UtcNow),
					true, path => new FOModScreenshotOverrideReadResult(FOModScreenshotOverrideReadState.Current,
						new FOModScreenshotOverrideRecord(1, 2, "fomod\\shot.png", current, 3)), out store);
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(
					Guid.Parse("30000000-0000-0000-0000-000000000005"));
				service.Capture(target, captureIdentity, nativeState, identities, CancellationToken.None);
				current = Encoding.UTF8.GetBytes("second");

				InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
					service.Capture(target, captureIdentity, nativeState, identities, CancellationToken.None));
				StringAssert.Contains("already bound to different immutable bytes", error.Message);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionUserMetadataCaptureService CreateService(string root, bool sortAvailable,
			Func<string, ModSortOrderRecord> sortReader, bool screenshotAvailable,
			Func<string, FOModScreenshotOverrideReadResult> screenshotReader, out CollectionsStore store)
		{
			store = new CollectionsStore(Path.Combine(root, "Collections"));
			store.CreateNew();
			IInstallLog installLog = InterfaceStub<IInstallLog>.Create((method, args) => null);
			IVirtualModActivator virtualModActivator = InterfaceStub<IVirtualModActivator>.Create((method, args) => null);
			IGameMode gameMode = InterfaceStub<IGameMode>.Create((method, args) => null);
			var nativeReader = new NativeStateCaptureReader(installLog, virtualModActivator, null, null, gameMode);
			var identityReader = new CollectionInstalledIdentityCaptureReader(nativeReader,
				(Func<CollectionTargetIdentity, CollectionsAssociationTargetSnapshot>)null, null);
			return new CollectionUserMetadataCaptureService(nativeReader, identityReader, sortAvailable, sortReader,
				screenshotAvailable, screenshotReader, new CollectionsRetainedArtifactStore(store),
				new CollectionsRetainedArtifactReferenceStore(store));
		}

		private static InstallLogReadSnapshot CreateInstall(string nativeKey, string archivePath, string modId, string fileId)
		{
			return CreateInstall(new[] { CreateInstallMod(nativeKey, archivePath, modId, fileId) });
		}

		private static InstallLogReadSnapshot CreateInstall(IEnumerable<InstallLogReadMod> mods)
		{
			return new InstallLogReadSnapshot("original-values", 51, mods, new InstallLogReadFile[0],
				new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0], new InstallLogReadDeploymentTarget[0]);
		}

		private static InstallLogReadMod CreateInstallMod(string nativeKey, string archivePath, string modId, string fileId)
		{
			return new InstallLogReadMod(nativeKey, archivePath, Path.GetFileName(archivePath), modId, fileId, "1", "1", false,
				ModInstallRoot.Data, ModInstallMethod.Virtual, false);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(InstallLogReadSnapshot install)
		{
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
				new NativeStateCaptureVirtualPayloadSource[0], new NativeStateCaptureRoot[0],
				new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.NotApplicable,
				new NativeStateCaptureIssue[0]);
		}

		private static CollectionInstalledIdentitySnapshot CreateIdentities(CollectionTargetIdentity target,
			InstallLogReadSnapshot install, IEnumerable<CollectionInstalledModIdentity> mods)
		{
			return new CollectionInstalledIdentitySnapshot(target, install.DeploymentCommitSequence, mods,
				NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);
		}

		private static CollectionInstalledModIdentity CreateInstalledMod(string nativeKey, string archivePath,
			string modId, string fileId)
		{
			return new CollectionInstalledModIdentity(nativeKey,
				new CollectionInstalledArchiveReference(archivePath, Path.GetFileName(archivePath), false, null), modId, fileId,
				"1", "1", false, new ModInstallContext(ModInstallMethod.Virtual, ModInstallRoot.Data),
				new CollectionInstalledMemberProvenance[0]);
		}

		private static string CreateTemporaryDirectory(string prefix)
		{
			string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void AssertRetainedText(CollectionsStore store, RetainedArtifactReference retained, string expected)
		{
			var artifacts = new CollectionsRetainedArtifactStore(store);
			Assert.IsTrue(artifacts.VerifyArtifact(retained.StableArtifactId));
			using (var reader = new StreamReader(artifacts.OpenRead(retained.StableArtifactId), Encoding.UTF8))
				Assert.AreEqual(expected, reader.ReadToEnd());
		}
	}
}
