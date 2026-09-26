using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.9 read-only native mapping/remap planner characterization.</summary>
	[TestFixture]
	public class CollectionLocalRestorePlannerTests
	{
		private const long Checkpoint = 17;

		[Test]
		public void Plan_ReusesExactVerifiedNativeAndRemapsOriginalAndDirectOwnersDeterministically()
		{
			string root = CreateTemporaryDirectory("nmm-c79-reuse-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("archive-a"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				NativeStateCaptureSnapshot current = CreateNativeState("current-original", new[]
				{
					CreateCurrentMod("native-a", archivePath, ModInstallMethod.Direct),
					CreateCurrentMod("extra-current", archivePath, ModInstallMethod.Virtual)
				});
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				var fingerprint = new CollectionCurrentStateFingerprint("state-v1", "current-a");

				CollectionLocalRestorePlan first = planner.Plan(capture, capture.Capture.SourceTarget, fingerprint, current);
				CollectionLocalRestorePlan second = planner.Plan(capture, capture.Capture.SourceTarget, fingerprint, current);

				Assert.IsTrue(first.IsReadyForReview);
				Assert.AreEqual(first.PlanFingerprint, second.PlanFingerprint);
				CollectionLocalRestoreMemberPlan member = first.Members.Single();
				Assert.AreEqual(CollectionLocalRestoreMemberAction.ReuseExistingNative, member.Action);
				Assert.AreEqual("native-a", member.CurrentNativeKey);
				CollectionAssert.AreEqual(new[] { "extra-current" }, first.CurrentNativeKeysToRemove);
				CollectionLocalRestoreDeploymentPlan deployment = first.DeploymentTargets.Single();
				Assert.AreEqual(CollectionLocalRestoreOwnerBindingKind.OriginalValue, deployment.Owners[0].BindingKind);
				Assert.AreEqual("current-original", first.CurrentOriginalValuesKey);
				Assert.AreEqual("current-original", deployment.Owners[0].CurrentNativeKey);
				Assert.AreEqual(CollectionLocalRestoreOwnerBindingKind.ExistingNative, deployment.Owners[1].BindingKind);
				Assert.AreEqual("native-a", deployment.Owners[1].CurrentNativeKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Plan_RemapsChangedNativeKeyWhenArchiveAndInstallContextMatchUniquely()
		{
			string root = CreateTemporaryDirectory("nmm-c79-remap-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("same exact archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-key",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				NativeStateCaptureSnapshot current = CreateNativeState("new-original", new[]
				{
					CreateCurrentMod("current-key", archivePath, ModInstallMethod.Direct)
				});
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "current-b"), current);

				Assert.IsTrue(plan.IsReadyForReview);
				Assert.AreEqual("current-key", plan.Members.Single().CurrentNativeKey);
				Assert.AreEqual(CollectionLocalRestoreOwnerBindingKind.ExistingNative,
					plan.DeploymentTargets.Single().Owners[1].BindingKind);
				Assert.AreEqual("current-key", plan.DeploymentTargets.Single().Owners[1].CurrentNativeKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Plan_MissingCurrentNativeCreatesDeferredMemberBindingInsteadOfInventingNativeKey()
		{
			string root = CreateTemporaryDirectory("nmm-c79-recreate-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("archive for recreate"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "old-native-key",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				NativeStateCaptureSnapshot current = CreateNativeState("current-original", new InstallLogReadMod[0]);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "empty-current"), current);

				Assert.IsTrue(plan.IsReadyForReview);
				CollectionLocalRestoreMemberPlan member = plan.Members.Single();
				Assert.AreEqual(CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, member.Action);
				Assert.AreEqual(String.Empty, member.CurrentNativeKey);
				CollectionLocalRestoreOwnerBinding owner = plan.DeploymentTargets.Single().Owners[1];
				Assert.AreEqual(CollectionLocalRestoreOwnerBindingKind.RecreatedSnapshotMember, owner.BindingKind);
				Assert.AreEqual(member.SnapshotMemberKey, owner.RecreatedSnapshotMember);
				Assert.AreEqual(String.Empty, owner.CurrentNativeKey);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Plan_ExactNativeKeyWithChangedArchiveIsRecreatedInsteadOfBlindlyReused()
		{
			string root = CreateTemporaryDirectory("nmm-c79-stale-archive-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("captured archive bytes"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: false);
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("different current archive bytes"));
				NativeStateCaptureSnapshot current = CreateNativeState("original", new[]
				{
					CreateCurrentMod("native-a", archivePath, ModInstallMethod.Direct)
				});
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "changed-archive"), current);

				Assert.IsTrue(plan.IsReadyForReview);
				Assert.AreEqual(CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, plan.Members.Single().Action);
				Assert.AreEqual(String.Empty, plan.Members.Single().CurrentNativeKey);
				CollectionAssert.AreEqual(new[] { "native-a" }, plan.CurrentNativeKeysToRemove);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Plan_MultipleVerifiedCurrentCandidatesStopForReview()
		{
			string root = CreateTemporaryDirectory("nmm-c79-ambiguous-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("duplicate archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-key",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: false);
				NativeStateCaptureSnapshot current = CreateNativeState("original", new[]
				{
					CreateCurrentMod("current-a", archivePath, ModInstallMethod.Direct),
					CreateCurrentMod("current-b", archivePath, ModInstallMethod.Direct)
				});
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "ambiguous"), current);

				Assert.IsFalse(plan.IsReadyForReview);
				Assert.IsTrue(plan.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.CurrentNativeMatchAmbiguous));
				Assert.AreEqual(CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, plan.Members.Single().Action);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Plan_TamperedRetainedArtifactStopsBeforeMutation()
		{
			string root = CreateTemporaryDirectory("nmm-c79-tamper-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("original retained bytes"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: false);
				RetainedArtifactReference retained = capture.Archives.Single().RetainedArtifact;
				string blobPath = Path.Combine(store.RetainedContentDirectory, "sha256",
					retained.ContentHash.Value.Substring(0, 2), retained.ContentHash.Value + ".blob");
				File.WriteAllBytes(blobPath, Encoding.ASCII.GetBytes("tampered retained bytes!"));
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "tampered"), CreateNativeState("original", new InstallLogReadMod[0]));

				Assert.IsFalse(plan.IsReadyForReview);
				Assert.IsTrue(plan.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.RetainedArtifactCorrupt));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Plan_RecipeOnlyOrDifferentTargetCannotBecomeAutomaticRestorePlan()
		{
			string root = CreateTemporaryDirectory("nmm-c79-contract-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.RecipeOnly, includeOwnerPayload: false);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, CollectionTargetIdentity.FromFingerprint("different-target"),
					new CollectionCurrentStateFingerprint("state-v1", "different"), CreateNativeState("original", new InstallLogReadMod[0]));

				Assert.IsFalse(plan.IsReadyForReview);
				Assert.IsTrue(plan.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.CaptureNotLocallyRestorable));
				Assert.IsTrue(plan.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.TargetMismatch));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionSealedCaptureSnapshot CreateCapture(CollectionsStore store, string archivePath,
			string capturedNativeKey, LocalCaptureCapability capability, bool includeOwnerPayload)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c79");
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
			LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.NewGuid());
			CollectionMemberKey memberKey = CollectionMemberKey.FromLocal(Guid.NewGuid());
			var artifactStore = new CollectionsRetainedArtifactStore(store);
			CollectionsRetainedArtifact archiveArtifact = artifactStore.PublishFile(archivePath);
			var archiveReference = new RetainedArtifactReference(archiveArtifact.ArtifactId,
				"mod-archive:" + capturedNativeKey, archiveArtifact.ContentHash, archiveArtifact.ByteLength);
			var capturedArchive = new CollectionCapturedArchiveArtifact(capturedNativeKey, Path.GetFileName(archivePath), null, archiveReference);

			var installedArchive = new CollectionInstalledArchiveReference(archivePath, Path.GetFileName(archivePath), true, null);
			var installed = new CollectionInstalledModIdentity(capturedNativeKey, installedArchive, "42", "77", "1.0", "1.0", false,
				new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data), new CollectionInstalledMemberProvenance[0]);
			var identities = new CollectionInstalledIdentitySnapshot(target, Checkpoint, new[] { installed },
				NativeStateCaptureCoverage.Complete, new CollectionInstalledIdentityIssue[0]);

			var retained = new List<RetainedArtifactReference> { archiveReference };
			CollectionOwnerPayloadSnapshot ownerPayloads;
			if (includeOwnerPayload)
			{
				var ownerReference = new RetainedArtifactReference(archiveArtifact.ArtifactId,
					"owner-payload:" + capturedNativeKey, archiveArtifact.ContentHash, archiveArtifact.ByteLength);
				retained.Add(ownerReference);
				var ownerRetention = new CollectionOwnerPayloadRetention(ownerReference.StableArtifactId, ownerReference.Role,
					ownerReference.ContentHash, ownerReference.ByteLength);
				ModDeploymentTarget deploymentTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "example.bin");
				var targetOwners = new[]
				{
					new CollectionOwnerPayloadOwner(0, "captured-original", String.Empty,
						NativeStateCaptureDeploymentOwnerKind.OriginalValue, false, ownerRetention),
					new CollectionOwnerPayloadOwner(1, capturedNativeKey, String.Empty,
						NativeStateCaptureDeploymentOwnerKind.Direct, true, ownerRetention)
				};
				ownerPayloads = new CollectionOwnerPayloadSnapshot(target, captureIdentity, Checkpoint,
					new[] { new CollectionOwnerPayloadTarget(deploymentTarget, true, targetOwners) },
					NativeStateCaptureCoverage.Complete, new CollectionOwnerPayloadIssue[0]);
			}
			else
				ownerPayloads = new CollectionOwnerPayloadSnapshot(target, captureIdentity, Checkpoint,
					new CollectionOwnerPayloadTarget[0], NativeStateCaptureCoverage.Complete, new CollectionOwnerPayloadIssue[0]);

			var replay = new CollectionScriptedReplaySnapshot(target, captureIdentity, Checkpoint,
				new CollectionScriptedReplayArtifactSet[0], NativeStateCaptureCoverage.Complete, new CollectionScriptedReplayIssue[0]);
			var effects = new CollectionNativeEffectSnapshot(target, Checkpoint,
				new CollectionCapturedIniEffect[0], NativeStateCaptureCoverage.Complete,
				new CollectionCapturedGameValueEffect[0], NativeStateCaptureCoverage.Complete,
				new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.Complete, new CollectionNativeEffectIssue[0]);
			var metadata = new CollectionUserMetadataSnapshot(target, captureIdentity, Checkpoint,
				new CollectionCapturedSortAssignment[0], NativeStateCaptureCoverage.Complete,
				new CollectionCapturedScreenshotOverride[0], NativeStateCaptureCoverage.Complete, new CollectionUserMetadataIssue[0]);
			LocalCaptureScope scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion, includeOwnerPayload
				? new[] { LocalCaptureScopeArea.ManagedModState, LocalCaptureScopeArea.ModArchives, LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads }
				: new[] { LocalCaptureScopeArea.ManagedModState, LocalCaptureScopeArea.ModArchives });
			var mapping = new LocalCaptureNativeRecordMapping(memberKey, new NativeModInstanceIdentity(target, capturedNativeKey));
			var capture = new LocalCapture(captureIdentity, revision, target,
				new CollectionCurrentStateFingerprint("state-v1", "captured"), scope, capability, retained,
				new LocalCaptureExclusion[0], new[] { mapping });
			return new CollectionSealedCaptureSnapshot(capture, identities, new[] { capturedArchive }, ownerPayloads,
				replay, effects, metadata);
		}

		private static InstallLogReadMod CreateCurrentMod(string nativeKey, string archivePath, ModInstallMethod method)
		{
			return new InstallLogReadMod(nativeKey, archivePath, Path.GetFileName(archivePath), "42", "77", "1.0", "1.0", false,
				ModInstallRoot.Data, method, false);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(string originalKey, IEnumerable<InstallLogReadMod> mods)
		{
			var install = new InstallLogReadSnapshot(originalKey, 101, mods,
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				new InstallLogReadDeploymentTarget[0]);
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
				new NativeStateCaptureRoot[0], new NativeStateCaptureDeploymentTarget[0], NativeStateCaptureCoverage.Complete,
				new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.Complete,
				new NativeStateCaptureIssue[0]);
		}

		private static CollectionsStore CreateStore(string root)
		{
			var store = new CollectionsStore(new Nexus.Client.GameStorage.GameStoragePathSet
			{
				GameId = "TEST",
				GameName = "TEST",
				GameInstallPath = root,
				InstallInfoPath = Path.Combine(root, "info"),
				ModsPath = Path.Combine(root, "mods"),
				VirtualInstallPath = Path.Combine(root, "cache"),
				LinkFolderPath = Path.Combine(root, "overwrite"),
				LinkFolderRequired = false
			});
			store.CreateNew();
			return store;
		}

		private static string CreateTemporaryDirectory(string prefix)
		{
			string path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
