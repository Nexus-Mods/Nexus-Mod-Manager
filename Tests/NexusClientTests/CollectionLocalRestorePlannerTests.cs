using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.CollectionManagement.UI;
using Nexus.Client.ModManagement;
using Nexus.Client.ModManagement.InstallationLog;
using Nexus.Client.ModManagement.Operations;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.9 read-only native mapping/remap planner characterization.</summary>
	[TestFixture]
	[Category("CollectionsC12Workflow")]
	[Category("CollectionsGateL")]
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
		public void Plan_ReusableNativeWithExtraCurrentFileEffectForcesConservativeRecreation()
		{
			string root = CreateTemporaryDirectory("nmm-c79-extra-effect-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("archive-a"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				ModDeploymentTarget capturedTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "example.bin");
				ModDeploymentTarget extraTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "extra.bin");
				NativeStateCaptureSnapshot current = CreateNativeState("current-original", new[]
				{
					CreateCurrentMod("native-a", archivePath, ModInstallMethod.Direct)
				}, new[]
				{
					CreateCurrentDeployment(capturedTarget, "current-original", "native-a"),
					CreateCurrentDeployment(extraTarget, "current-original", "native-a")
				});
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "current-extra-effect"), current);

				Assert.IsTrue(plan.IsReadyForReview);
				Assert.AreEqual(CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, plan.Members.Single().Action);
				Assert.AreEqual(String.Empty, plan.Members.Single().CurrentNativeKey);
				CollectionAssert.AreEqual(new[] { "native-a" }, plan.CurrentNativeKeysToRemove);
				Assert.AreEqual(CollectionLocalRestoreOwnerBindingKind.RecreatedSnapshotMember,
					plan.DeploymentTargets.Single().Owners[1].BindingKind);
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

		/// <summary>Native reconciliation reads only archives; the later full preflight still rejects damaged file payloads.</summary>
		[Test]
		public void PlanForNativeReconciliation_DefersPayloadBytesWithoutAuthorizingRestore()
		{
			string root = CreateTemporaryDirectory("nmm-recovery-scope-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("recovery archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, true, distinctOwnerPayload: true);
				RetainedArtifactReference payload = capture.Capture.RetainedArtifacts.Single(x => x.Role.StartsWith("owner-payload:", StringComparison.Ordinal));
				string payloadPath = Path.Combine(store.RetainedContentDirectory, "sha256",
					payload.ContentHash.Value.Substring(0, 2), payload.ContentHash.Value + ".blob");
				byte[] damaged = File.ReadAllBytes(payloadPath);
				damaged[0] ^= 1;
				File.WriteAllBytes(payloadPath, damaged);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				NativeStateCaptureSnapshot native = CreateNativeState("original", new InstallLogReadMod[0]);
				var fingerprint = new CollectionCurrentStateFingerprint("state-v1", "recovery-scope");
				CollectionPerformanceSnapshot before = CollectionPerformanceMetrics.Capture();

				CollectionLocalRestorePlan recovery = planner.PlanForNativeReconciliation(capture, capture.Capture.SourceTarget,
					fingerprint, native, CancellationToken.None);
				CollectionPerformanceSnapshot after = CollectionPerformanceMetrics.Capture();

				Assert.IsEmpty(recovery.Issues);
				Assert.IsTrue(recovery.IsNativeRecoveryProjection);
				Assert.IsFalse(recovery.IsReadyForReview);
				Assert.AreEqual(1, after.RetainedArtifactVerificationHashCount - before.RetainedArtifactVerificationHashCount);
				Assert.AreEqual(capture.Archives.Single().RetainedArtifact.ByteLength,
					after.RetainedArtifactVerificationHashedBytes - before.RetainedArtifactVerificationHashedBytes);
				Assert.Throws<InvalidDataException>(() => CollectionLocalRestoreIntentCodec.Serialize(capture.Capture.Identity, recovery));

				CollectionLocalRestorePlan full = planner.Plan(capture, capture.Capture.SourceTarget, fingerprint, native);
				Assert.IsFalse(full.IsNativeRecoveryProjection);
				Assert.IsFalse(full.IsReadyForReview);
				Assert.IsTrue(full.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.RetainedArtifactCorrupt && x.ResourceKey == payload.Role));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Reducing recovery's payload closure must retain archive integrity checks.</summary>
		[Test]
		public void PlanForNativeReconciliation_RejectsDamagedRetainedArchive()
		{
			string root = CreateTemporaryDirectory("nmm-recovery-archive-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("recovery archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, true, distinctOwnerPayload: true);
				RetainedArtifactReference archive = capture.Archives.Single().RetainedArtifact;
				string blobPath = Path.Combine(store.RetainedContentDirectory, "sha256",
					archive.ContentHash.Value.Substring(0, 2), archive.ContentHash.Value + ".blob");
				byte[] damaged = File.ReadAllBytes(blobPath);
				damaged[0] ^= 1;
				File.WriteAllBytes(blobPath, damaged);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan recovery = planner.PlanForNativeReconciliation(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "damaged-archive"),
					CreateNativeState("original", new InstallLogReadMod[0]), CancellationToken.None);

				Assert.IsFalse(recovery.IsReadyForReview);
				Assert.IsTrue(recovery.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.RetainedArtifactCorrupt && x.ResourceKey == archive.Role));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Repeated resume observations reuse native archive hashes, while same-length changed bytes invalidate the proof.</summary>
		[Test]
		public void Plan_ReusesNativeArchiveProofAndInvalidatesItAfterAWrite()
		{
			string root = CreateTemporaryDirectory("nmm-recovery-archive-cache-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				byte[] archiveBytes = Encoding.ASCII.GetBytes("cached native archive");
				File.WriteAllBytes(archivePath, archiveBytes);
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, false);
				NativeStateCaptureSnapshot native = CreateNativeState("original", new[] { CreateCurrentMod("native-a", archivePath, ModInstallMethod.Direct) });
				var fingerprint = new CollectionCurrentStateFingerprint("state-v1", "archive-cache");
				var artifacts = new CollectionsRetainedArtifactStore(store);
				CollectionLocalRestorePlan cold = new CollectionLocalRestorePlanner(artifacts).Plan(capture,
					capture.Capture.SourceTarget, fingerprint, native);
				Assert.AreEqual(CollectionLocalRestoreMemberAction.ReuseExistingNative, cold.Members.Single().Action);

				// Metadata remains readable, but a second content read would fail under this exclusive handle.
				using (var locked = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.None))
				{
					CollectionLocalRestorePlan warm = new CollectionLocalRestorePlanner(artifacts).Plan(capture,
						capture.Capture.SourceTarget, fingerprint, native);
					Assert.IsTrue(warm.IsReadyForReview);
					Assert.AreEqual(CollectionLocalRestoreMemberAction.ReuseExistingNative, warm.Members.Single().Action);
					Assert.AreEqual(cold.PlanFingerprint, warm.PlanFingerprint);
				}

				archiveBytes[0] ^= 1;
				File.WriteAllBytes(archivePath, archiveBytes);
				File.SetLastWriteTimeUtc(archivePath, DateTime.UtcNow.AddMinutes(1));
				CollectionLocalRestorePlan changed = new CollectionLocalRestorePlanner(artifacts).Plan(capture,
					capture.Capture.SourceTarget, fingerprint, native);
				Assert.AreEqual(CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, changed.Members.Single().Action);
				CollectionAssert.AreEqual(new[] { "native-a" }, changed.CurrentNativeKeysToRemove);
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

		[Test]
		public void Plan_PreFallbackCapabilityVersionCannotEnterAutomaticRestorePlanning()
		{
			string root = CreateTemporaryDirectory("nmm-c79-old-capability-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("archive"));
				CollectionSealedCaptureSnapshot currentCapture = CreateCapture(store, archivePath, "native-a",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				LocalCapture source = currentCapture.Capture;
				var oldContract = new LocalCapture(source.Identity, source.Revision, source.SourceTarget,
					source.CapturedStateFingerprint, source.Scope, source.Capability,
					LocalCapture.CurrentSchemaVersion, 1, source.RetainedArtifacts, source.Exclusions, source.NativeRecordMappings);
				var oldCapture = new CollectionSealedCaptureSnapshot(oldContract, currentCapture.InstalledIdentities,
					currentCapture.Archives, currentCapture.OwnerPayloads, currentCapture.ScriptedReplay,
					currentCapture.NativeEffects, currentCapture.UserMetadata);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));

				CollectionLocalRestorePlan plan = planner.Plan(oldCapture, source.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "old-capability"),
					CreateNativeState("original", new InstallLogReadMod[0]));

				Assert.IsFalse(plan.IsReadyForReview);
				Assert.IsTrue(plan.Issues.Any(x => x.Kind == CollectionLocalRestorePlanIssueKind.UnsupportedCaptureVersion));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RestoreIntentCodec_RoundTripsExactReviewedPlan()
		{
			string root = CreateTemporaryDirectory("nmm-c710a-intent-roundtrip-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("intent archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-key",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				CollectionLocalRestorePlan reviewed = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "reviewed"),
					CreateNativeState("current-original", new InstallLogReadMod[0]));

				byte[] bytes = CollectionLocalRestoreIntentCodec.Serialize(capture.Capture.Identity, reviewed);
				CollectionLocalRestorePlan rehydrated = CollectionLocalRestoreIntentCodec.Deserialize(bytes, capture);

				Assert.AreEqual(reviewed.PlanFingerprint, rehydrated.PlanFingerprint);
				Assert.AreEqual(reviewed.Target, rehydrated.Target);
				Assert.AreEqual(reviewed.CurrentStateFingerprint, rehydrated.CurrentStateFingerprint);
				Assert.AreEqual(reviewed.CurrentDeploymentCommitSequence, rehydrated.CurrentDeploymentCommitSequence);
				Assert.AreEqual(reviewed.CurrentOriginalValuesKey, rehydrated.CurrentOriginalValuesKey);
				Assert.AreEqual(reviewed.Members.Single().SnapshotMemberKey, rehydrated.Members.Single().SnapshotMemberKey);
				Assert.AreEqual(reviewed.Members.Single().Action, rehydrated.Members.Single().Action);
				Assert.AreEqual(reviewed.Members.Single().RetainedArchive.RetainedArtifact.StableArtifactId,
					rehydrated.Members.Single().RetainedArchive.RetainedArtifact.StableArtifactId);
				Assert.AreEqual(reviewed.DeploymentTargets.Single().Owners.Count, rehydrated.DeploymentTargets.Single().Owners.Count);
				CollectionAssert.AreEqual(reviewed.CurrentNativeKeysToRemove, rehydrated.CurrentNativeKeysToRemove);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Characterizes third-entry selection through saved-package and operation-intent restart persistence.</summary>
		[Test]
		public void RestoreSelection_ThirdOfThreeSavedCapturesRemainsTheDurableSourceAfterSelectionChanges()
		{
			string root = CreateTemporaryDirectory("nmm-local-restore-third-source-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("exact source archive"));
				CollectionSealedCaptureSnapshot[] captures = Enumerable.Range(0, 3).Select(index =>
					CreateCapture(store, archivePath, "captured-key-" + index, LocalCaptureCapability.LocallyRestorableWithinScope, true)).ToArray();
				string[] names = { "C12 - Step 2B - Test", "C12 Test Collection", "C12 Test Collection BIS" };
				for (int index = 0; index < captures.Length; index++) SaveCapture(store, captures[index], names[index]);
				var captureStore = new CollectionsLocalCaptureStore(store);
				var catalog = new CollectionsCatalogStore(store);
				CollectionManagementLocalCapture[] saved = captureStore.GetCapturesForTarget(captures[2].Capture.SourceTarget)
					.Select(capture => new CollectionManagementLocalCapture(capture,
						catalog.GetDefinition(capture.Revision.Collection).DisplayName, "Captured setup"))
					.OrderBy(capture => capture.DisplayName, StringComparer.Ordinal).ToArray();
				Assert.AreEqual(2, CollectionsPreviewControl.FindLocalCaptureSelectionIndex(saved, captures[2].Capture.Identity));
				Assert.AreEqual(-1, CollectionsPreviewControl.FindLocalCaptureSelectionIndex(saved, null));
				Assert.AreEqual(-1, CollectionsPreviewControl.FindLocalCaptureSelectionIndex(saved, LocalCaptureIdentity.From(Guid.NewGuid())));
				Assert.Throws<InvalidDataException>(() => CollectionsPreviewControl.FindLocalCaptureSelectionIndex(
					new[] { saved[2], saved[2] }, captures[2].Capture.Identity));
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				CollectionLocalRestorePlan plan = planner.Plan(captures[2], captures[2].Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "reviewed-third"), CreateNativeState("original", new InstallLogReadMod[0]));
				var preview = new CollectionLocalRestorePreview(captures[2], plan);
				var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.RestoreLocalCapture,
					captures[2].Capture.Revision.Collection, plan.Target, captures[2].Capture.Revision, null, 1,
					CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				CollectionsRetainedArtifact intent;
				using (var input = new MemoryStream(CollectionLocalRestoreIntentCodec.Serialize(preview.Plan.CaptureIdentity, preview.Plan)))
					intent = artifacts.Publish(input);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				references.AcquireExclusiveRoleReference(intent.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D"), "local-restore-plan-v1");
				new CollectionsOperationStore(store).SaveOperation(operation);
				Assert.AreEqual(0, CollectionsPreviewControl.FindLocalCaptureSelectionIndex(saved, captures[0].Capture.Identity));
				CollectionsRetainedArtifactReferenceRecord persisted = new CollectionsRetainedArtifactReferenceStore(store).GetReferenceForOwnerRole(
					CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.OperationId.ToString("D"), "local-restore-plan-v1");
				byte[] bytes;
				using (Stream input = new CollectionsRetainedArtifactStore(store).OpenRead(persisted.ArtifactId))
				using (var buffer = new MemoryStream()) { input.CopyTo(buffer); bytes = buffer.ToArray(); }
				Assert.AreEqual(captures[2].Capture.Identity, CollectionLocalRestoreIntentCodec.ReadCaptureIdentity(bytes));
				CollectionSealedCaptureSnapshot restartedCapture;
				using (Stream input = artifacts.OpenRead(captureStore.GetPackageArtifactId(CollectionLocalRestoreIntentCodec.ReadCaptureIdentity(bytes))))
				using (var buffer = new MemoryStream())
				{
					input.CopyTo(buffer);
					restartedCapture = CollectionLocalCapturePackageCodec.Deserialize(buffer.ToArray());
				}
				Assert.AreEqual(captures[2].Capture.Identity, CollectionLocalRestoreIntentCodec.Deserialize(bytes, restartedCapture).CaptureIdentity);
				Assert.Throws<InvalidDataException>(() => new CollectionLocalRestorePreview(captures[0], plan));
				Assert.Throws<InvalidDataException>(() => CollectionLocalRestoreIntentCodec.Serialize(captures[0].Capture.Identity, plan));
				Assert.Throws<InvalidDataException>(() => CollectionLocalRestoreIntentCodec.Deserialize(bytes, captures[0]));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Rejects stopping an unresolved native child while allowing an independently reconciled partial outcome.</summary>
		[Test]
		public void LocalRestoreStop_RequiresKnownReconciledNativeDurability()
		{
			CollectionNativeChildOperation child;
			CollectionLocalRestoreMemberRehydrationResult recovery = CreateMemberRecoveryState(
				CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, out child);
			Assert.Throws<InvalidOperationException>(() => CollectionLocalRestoreApplicationService.RequireReconciledStopBoundary(recovery.Operation));
			var known = new ModOperationResult(child.NativeOperation, ModOperationReportedStatus.Failed,
				ModOperationDurability.VerifiedCommitted, "Durable partial work");
			var reconciled = new CollectionNativeChildOperation(child.Sequence, child.Member, child.Action, child.NativeOperation,
				CollectionNativeChildCheckpoint.Reconciled, known);
			CollectionOperation operation = recovery.Operation;
			var safe = new CollectionOperation(operation.Identity, operation.Kind, operation.Collection, operation.Target, operation.Revision,
				operation.PlanIdentity, operation.CheckpointSequence + 1, CollectionOperationPhase.PausedAtSafeBoundary,
				CollectionOperationResultState.Pending, new[] { reconciled });
			Assert.DoesNotThrow(() => CollectionLocalRestoreApplicationService.RequireReconciledStopBoundary(safe));
		}

		/// <summary>Publishes an exact sealed fixture using the normal capture references and immutable package binding.</summary>
		private static void SaveCapture(CollectionsStore store, CollectionSealedCaptureSnapshot snapshot, string displayName)
		{
			var artifacts = new CollectionsRetainedArtifactStore(store);
			var references = new CollectionsRetainedArtifactReferenceStore(store);
			string ownerId = snapshot.Capture.Identity.ToString();
			foreach (RetainedArtifactReference reference in snapshot.Capture.RetainedArtifacts)
				references.AcquireReference(reference.StableArtifactId, CollectionsRetainedArtifactOwnerKind.Capture, ownerId, reference.Role);
			var result = new CollectionCaptureSealResult(snapshot.Capture.Capability, snapshot, new CollectionCaptureSealIssue[0]);
			CollectionsRetainedArtifact package;
			using (var input = new MemoryStream(CollectionLocalCapturePackageCodec.Serialize(result))) package = artifacts.Publish(input);
			references.AcquireReference(package.ArtifactId, CollectionsRetainedArtifactOwnerKind.Capture, ownerId, CollectionsLocalCaptureStore.PackageReferenceRole);
			new CollectionsLocalCaptureStore(store).Save(new CollectionDefinition(snapshot.Capture.Revision.Collection, displayName, null, null),
				new CollectionRevision(snapshot.Capture.Revision, "Captured setup", null, null), snapshot.Capture,
				CollectionLocalCapturePackageCodec.CurrentFormatVersion, package.ArtifactId);
		}

		[Test]
		public void RestoreIntentCodec_RejectsNonCanonicalPersistedPayload()
		{
			string root = CreateTemporaryDirectory("nmm-c710a-intent-canonical-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("intent archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-key",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				CollectionLocalRestorePlan reviewed = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "reviewed"),
					CreateNativeState("current-original", new InstallLogReadMod[0]));
				byte[] canonical = CollectionLocalRestoreIntentCodec.Serialize(capture.Capture.Identity, reviewed);
				string json = Encoding.UTF8.GetString(canonical);
				byte[] nonCanonical = Encoding.UTF8.GetBytes("{ " + json.Substring(1));

				Assert.Throws<InvalidDataException>(() => CollectionLocalRestoreIntentCodec.Deserialize(nonCanonical, capture));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RestoreIntentCodec_RejectsCanonicalPayloadWithAlteredPlanFingerprint()
		{
			string root = CreateTemporaryDirectory("nmm-c710a-intent-fingerprint-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.ASCII.GetBytes("intent archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-key",
					LocalCaptureCapability.LocallyRestorableWithinScope, includeOwnerPayload: true);
				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				CollectionLocalRestorePlan reviewed = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "reviewed"),
					CreateNativeState("current-original", new InstallLogReadMod[0]));
				string canonical = Encoding.UTF8.GetString(CollectionLocalRestoreIntentCodec.Serialize(capture.Capture.Identity, reviewed));
				string replacement = "restore-plan-sha256:" + new string('0', 64);
				Assert.AreNotEqual(reviewed.PlanFingerprint, replacement);
				byte[] altered = Encoding.UTF8.GetBytes(canonical.Replace(reviewed.PlanFingerprint, replacement));

				Assert.Throws<InvalidDataException>(() => CollectionLocalRestoreIntentCodec.Deserialize(altered, capture));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Recovery_CommittedRecreatedMemberCanBeResolvedWhenFreshPlannerRejectsReuseForExtraEffects()
		{
			string root = CreateTemporaryDirectory("nmm-c710-recovery-committed-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.UTF8.GetBytes("exact retained archive bytes"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-member",
					LocalCaptureCapability.LocallyRestorableWithinScope, true);
				string restoredKey = "restored-native";
				ModDeploymentTarget capturedTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "example.bin");
				ModDeploymentTarget extraTarget = ModDeploymentTargetResolver.FromCanonical(ModDeploymentRoot.Data, "extra.bin");
				NativeStateCaptureSnapshot current = CreateNativeState("original",
					new[] { CreateCurrentMod(restoredKey, archivePath, ModInstallMethod.Direct) },
					new[] { CreateCurrentDeployment(capturedTarget, "original", restoredKey), CreateCurrentDeployment(extraTarget, "original", restoredKey) });

				var planner = new CollectionLocalRestorePlanner(new CollectionsRetainedArtifactStore(store));
				CollectionLocalRestorePlan plan = planner.Plan(capture, capture.Capture.SourceTarget,
					new CollectionCurrentStateFingerprint("state-v1", "recovery"), current);
				Assert.AreEqual(CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, plan.Members.Single().Action,
					"The ordinary planner must remain conservative when the current registration owns an extra effect.");
				CollectionAssert.Contains(plan.CurrentNativeKeysToRemove, restoredKey);

				string resolved = CollectionLocalRestoreMemberRehydrator.ResolveVerifiedCommittedRecreatedNativeKey(
					capture.InstalledIdentities.Mods.Single(), capture.Archives.Single(), current.InstallLog.Mods,
					new HashSet<string>(StringComparer.OrdinalIgnoreCase), System.Threading.CancellationToken.None);
				Assert.AreEqual(restoredKey, resolved,
					"Recovery must recognize the already-committed native registration independently of downstream owner effects.");
			}
			finally
			{
				if (Directory.Exists(root)) Directory.Delete(root, true);
			}
		}

		[Test]
		public void Recovery_CommittedRecreatedMemberDoesNotRequireNexusMetadataThatInstallLogDropsOnRestart()
		{
			string root = CreateTemporaryDirectory("nmm-c710-recovery-installlog-metadata-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.UTF8.GetBytes("restart exact retained archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-member",
					LocalCaptureCapability.LocallyRestorableWithinScope, true);
				string restoredKey = "restored-after-restart";
				var reloadedRegistration = new InstallLogReadMod(restoredKey, archivePath, Path.GetFileName(archivePath),
					String.Empty, String.Empty, "1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Direct, false);

				string resolved = CollectionLocalRestoreMemberRehydrator.ResolveVerifiedCommittedRecreatedNativeKey(
					capture.InstalledIdentities.Mods.Single(), capture.Archives.Single(), new[] { reloadedRegistration },
					new HashSet<string>(StringComparer.OrdinalIgnoreCase), System.Threading.CancellationToken.None);

				Assert.AreEqual(restoredKey, resolved,
					"Restart recovery must not require Nexus mod/file identifiers that the legacy InstallLog does not persist.");
			}
			finally
			{
				if (Directory.Exists(root)) Directory.Delete(root, true);
			}
		}

		[Test]
		public void Recovery_CommittedRecreatedMemberStillFailsClosedWhenExactArchiveRegistrationIsAmbiguous()
		{
			string root = CreateTemporaryDirectory("nmm-c710-recovery-installlog-ambiguous-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.UTF8.GetBytes("ambiguous retained archive"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-member",
					LocalCaptureCapability.LocallyRestorableWithinScope, true);
				var first = new InstallLogReadMod("restored-one", archivePath, Path.GetFileName(archivePath),
					String.Empty, String.Empty, "1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Direct, false);
				var second = new InstallLogReadMod("restored-two", archivePath, Path.GetFileName(archivePath),
					String.Empty, String.Empty, "1.0", "1.0", false, ModInstallRoot.Data, ModInstallMethod.Direct, false);

				string resolved = CollectionLocalRestoreMemberRehydrator.ResolveVerifiedCommittedRecreatedNativeKey(
					capture.InstalledIdentities.Mods.Single(), capture.Archives.Single(), new[] { first, second },
					new HashSet<string>(StringComparer.OrdinalIgnoreCase), System.Threading.CancellationToken.None);

				Assert.IsNull(resolved, "Recovery must remain fail-closed when more than one current registration matches the exact retained archive and context.");
			}
			finally
			{
				if (Directory.Exists(root)) Directory.Delete(root, true);
			}
		}

		[TestCase(false)]
		[TestCase(true)]
		public void Recovery_CompletedMemberBoundaryRequiresFullVerificationAndUsesRehydratedRemap(bool nativeRecoveryProjection)
		{
			string root = CreateTemporaryDirectory("nmm-c710-recovery-boundary-");
			try
			{
				CollectionsStore store = CreateStore(root);
				string archivePath = Path.Combine(root, "mod.zip");
				File.WriteAllBytes(archivePath, Encoding.UTF8.GetBytes("member-boundary"));
				CollectionSealedCaptureSnapshot capture = CreateCapture(store, archivePath, "captured-member",
					LocalCaptureCapability.LocallyRestorableWithinScope, true);
				CollectionMemberKey memberKey = capture.Capture.NativeRecordMappings.Single().SnapshotMemberKey;
				var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
				var reviewedMember = new CollectionLocalRestoreMemberPlan(memberKey, "captured-member",
					CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, String.Empty, context, capture.Archives.Single());
				var currentMember = new CollectionLocalRestoreMemberPlan(memberKey, "captured-member",
					CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, String.Empty, context, capture.Archives.Single());
				CollectionLocalRestorePlan reviewed = CreateClassificationPlan(capture.Capture.SourceTarget, new[] { reviewedMember },
					new string[0], new CollectionLocalRestorePlanIssue[0]);
				CollectionLocalRestorePlan current = CreateClassificationPlan(capture.Capture.SourceTarget, new[] { currentMember },
					new[] { "restored-native" }, new CollectionLocalRestorePlanIssue[0], nativeRecoveryProjection);
				var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.RestoreLocalCapture,
					capture.Capture.Revision.Collection, capture.Capture.SourceTarget, capture.Capture.Revision, null, 1,
					CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired, new CollectionNativeChildOperation[0]);
				var progress = new CollectionLocalRestoreMemberProgress(reviewedMember, CollectionLocalRestoreMemberProgressStatus.RecreatedVerified,
					null, "restored-native");
				var remap = new CollectionLocalRestoreMemberRemap(memberKey, "restored-native");
				var rehydrated = new CollectionLocalRestoreMemberRehydrationResult(CollectionLocalRestoreMemberRehydrationStatus.MemberPhaseComplete,
					operation, capture, reviewed, current, new[] { remap }, new[] { progress }, new CollectionLocalRestoreRemovalProgress[0], "complete");

				Assert.AreEqual(!nativeRecoveryProjection, CollectionLocalRestoreApplicationService.IsExactCompletedMemberBoundary(rehydrated));
			}
			finally
			{
				if (Directory.Exists(root)) Directory.Delete(root, true);
			}
		}

		[Test]
		[Category("CollectionsC12FailureInjection")]
		public void LocalRestoreResumeClassifier_RemovalPresenceDistinguishesRollbackFromCommit()
		{
			CollectionNativeChildOperation child;
			CollectionLocalRestoreMemberRehydrationResult rolledBack = CreateRemovalRecoveryState(true, out child);
			Assert.AreEqual(ModOperationDurability.VerifiedRolledBack,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(rolledBack, child));

			CollectionLocalRestoreMemberRehydrationResult committed = CreateRemovalRecoveryState(false, out child);
			Assert.AreEqual(ModOperationDurability.VerifiedCommitted,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(committed, child));
		}

		[Test]
		[Category("CollectionsC12FailureInjection")]
		public void LocalRestoreResumeClassifier_MemberProjectionDistinguishesRollbackFromCommit()
		{
			CollectionNativeChildOperation child;
			CollectionLocalRestoreMemberRehydrationResult rolledBack = CreateMemberRecoveryState(
				CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, out child);
			Assert.AreEqual(ModOperationDurability.VerifiedRolledBack,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(rolledBack, child));

			CollectionLocalRestoreMemberRehydrationResult committed = CreateMemberRecoveryState(
				CollectionLocalRestoreMemberAction.ReuseExistingNative, out child);
			Assert.AreEqual(ModOperationDurability.VerifiedCommitted,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(committed, child));

			CollectionLocalRestoreMemberRehydrationResult ambiguous = CreateMemberRecoveryState(
				CollectionLocalRestoreMemberAction.ManualReviewRequired, out child);
			Assert.AreEqual(ModOperationDurability.Unknown,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(ambiguous, child));

			CollectionLocalRestoreMemberRehydrationResult contradictory = CreateMemberRecoveryState(
				CollectionLocalRestoreMemberAction.ReuseExistingNative, out child, ModOperationDurability.VerifiedRolledBack);
			Assert.AreEqual(ModOperationDurability.Unknown,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(contradictory, child));

			CollectionLocalRestoreMemberRehydrationResult notStarted = CreateMemberRecoveryState(
				CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, out child, ModOperationDurability.NotStarted);
			Assert.AreEqual(ModOperationDurability.NotStarted,
				CollectionLocalRestoreMemberResumeCoordinator.ClassifyDurability(notStarted, child));
		}

		private static CollectionLocalRestoreMemberRehydrationResult CreateRemovalRecoveryState(bool stillRequiresRemoval,
			out CollectionNativeChildOperation child)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c710a-recovery-removal");
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
			CollectionOperationIdentity operationIdentity = CollectionOperationIdentity.CreateNew();
			CollectionMemberKey journalKey = CollectionMemberKey.FromLocal(Guid.NewGuid());
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.LocalRestore,
				new ModOperationFingerprint(target.Fingerprint, context, null));
			child = new CollectionNativeChildOperation(1, new CollectionOperationMemberReference(revision, journalKey),
				CollectionNativeChildAction.Deactivate, native, CollectionNativeChildCheckpoint.NativeSubmitted, null);
			var operation = new CollectionOperation(operationIdentity, CollectionOperationKind.RestoreLocalCapture, collection, target, revision,
				null, 2, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired, new[] { child });
			var plan = CreateClassificationPlan(target, new CollectionLocalRestoreMemberPlan[0],
				stillRequiresRemoval ? new[] { "native-remove" } : new string[0], new CollectionLocalRestorePlanIssue[0]);
			var removal = new CollectionLocalRestoreRemovalProgress("native-remove", child, false, true);
			return new CollectionLocalRestoreMemberRehydrationResult(CollectionLocalRestoreMemberRehydrationStatus.NativeRecoveryRequired,
				operation, null, plan, plan, null, null, new[] { removal }, "recovery test");
		}

		private static CollectionLocalRestoreMemberRehydrationResult CreateMemberRecoveryState(
			CollectionLocalRestoreMemberAction currentAction, out CollectionNativeChildOperation child,
			ModOperationDurability? priorDurability = null)
		{
			CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c710a-recovery-member");
			CollectionIdentity collection = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromLocal(collection, Guid.NewGuid());
			CollectionMemberKey memberKey = CollectionMemberKey.FromLocal(Guid.NewGuid());
			var context = new ModInstallContext(ModInstallMethod.Direct, ModInstallRoot.Data);
			var reviewedMember = new CollectionLocalRestoreMemberPlan(memberKey, "captured-member",
				CollectionLocalRestoreMemberAction.RecreateFromRetainedArchive, String.Empty, context, null);
			var currentMember = new CollectionLocalRestoreMemberPlan(memberKey, "captured-member", currentAction,
				currentAction == CollectionLocalRestoreMemberAction.ReuseExistingNative ? "restored-native" : String.Empty, context, null);
			ModOperationIdentity native = ModOperationIdentity.CreateNew(ModOperationOrigin.LocalRestore,
				new ModOperationFingerprint(target.Fingerprint, context, "restore-recipe"));
			ModOperationResult prior = priorDurability.HasValue
				? new ModOperationResult(native, ModOperationReportedStatus.Failed, priorDurability.Value, "persisted restart result")
				: null;
			child = new CollectionNativeChildOperation(1, new CollectionOperationMemberReference(revision, memberKey),
				CollectionNativeChildAction.ActivateOrReinstall, native,
				prior == null ? CollectionNativeChildCheckpoint.NativeSubmitted : CollectionNativeChildCheckpoint.NativeTerminalObserved, prior);
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.RestoreLocalCapture,
				collection, target, revision, null, 2, CollectionOperationPhase.RecoveryRequired,
				CollectionOperationResultState.RecoveryRequired, new[] { child });
			CollectionLocalRestorePlan reviewed = CreateClassificationPlan(target, new[] { reviewedMember }, new string[0],
				new CollectionLocalRestorePlanIssue[0]);
			CollectionLocalRestorePlan current = CreateClassificationPlan(target, new[] { currentMember }, new string[0],
				new CollectionLocalRestorePlanIssue[0]);
			var progress = new CollectionLocalRestoreMemberProgress(reviewedMember,
				CollectionLocalRestoreMemberProgressStatus.NativeRecoveryRequired, child, String.Empty);
			return new CollectionLocalRestoreMemberRehydrationResult(CollectionLocalRestoreMemberRehydrationStatus.NativeRecoveryRequired,
				operation, null, reviewed, current, null, new[] { progress }, null, "recovery test");
		}

		private static CollectionLocalRestorePlan CreateClassificationPlan(CollectionTargetIdentity target,
			IEnumerable<CollectionLocalRestoreMemberPlan> members, IEnumerable<string> removals, IEnumerable<CollectionLocalRestorePlanIssue> issues,
			bool nativeRecoveryProjection = false)
		{
			return new CollectionLocalRestorePlan(LocalCaptureIdentity.From(Guid.NewGuid()), target,
				new CollectionCurrentStateFingerprint("state-v1", Guid.NewGuid().ToString("N")), 1, "original",
				"classification-plan-" + Guid.NewGuid().ToString("N"), members, new CollectionLocalRestoreDeploymentPlan[0], removals, issues, nativeRecoveryProjection);
		}

		private static CollectionSealedCaptureSnapshot CreateCapture(CollectionsStore store, string archivePath,
			string capturedNativeKey, LocalCaptureCapability capability, bool includeOwnerPayload, bool distinctOwnerPayload = false)
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
				CollectionsRetainedArtifact ownerArtifact = archiveArtifact;
				if (distinctOwnerPayload)
				{
					using (var source = new MemoryStream(Encoding.ASCII.GetBytes("separate owner payload")))
						ownerArtifact = artifactStore.Publish(source);
				}
				var ownerReference = new RetainedArtifactReference(ownerArtifact.ArtifactId,
					"owner-payload:" + capturedNativeKey, ownerArtifact.ContentHash, ownerArtifact.ByteLength);
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
			return CreateNativeState(originalKey, mods, new NativeStateCaptureDeploymentTarget[0]);
		}

		private static NativeStateCaptureSnapshot CreateNativeState(string originalKey, IEnumerable<InstallLogReadMod> mods,
			IEnumerable<NativeStateCaptureDeploymentTarget> deploymentTargets)
		{
			NativeStateCaptureDeploymentTarget[] targets = deploymentTargets.ToArray();
			var install = new InstallLogReadSnapshot(originalKey, 101, mods,
				new InstallLogReadFile[0], new InstallLogReadIniEdit[0], new InstallLogReadGameValue[0],
				targets.Select(x => new InstallLogReadDeploymentTarget(x.Target, x.Owners.Select(owner => owner.OwnerKey))));
			return new NativeStateCaptureSnapshot(install, new VirtualModReadSnapshot(new VirtualModReadLink[0]),
				new NativeStateCaptureRoot[0], targets, NativeStateCaptureCoverage.Complete,
				new NativeStateCaptureReplayReference[0], new NativeStateCapturePlugin[0], NativeStateCaptureCoverage.Complete,
				new NativeStateCaptureIssue[0]);
		}

		private static NativeStateCaptureDeploymentTarget CreateCurrentDeployment(ModDeploymentTarget target,
			string originalKey, string modKey)
		{
			return new NativeStateCaptureDeploymentTarget(target, target.RelativePath, new[]
			{
				new NativeStateCaptureDeploymentOwner(originalKey, NativeStateCaptureDeploymentOwnerKind.OriginalValue, false, target.RelativePath + ".original"),
				new NativeStateCaptureDeploymentOwner(modKey, NativeStateCaptureDeploymentOwnerKind.Direct, true, target.RelativePath)
			});
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
