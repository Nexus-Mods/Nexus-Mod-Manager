using System;
using System.IO;
using System.Linq;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C7.8 durable Local Collection capture catalog/package binding coverage.</summary>
	[TestFixture]
	[Category("CollectionsGateL")]
	public class CollectionsLocalCaptureStoreTests
	{
		[Test]
		public void Save_RoundTripsSealedCaptureAndDurablePackageBinding()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);

				CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.Parse("11111111-1111-1111-1111-111111111111"));
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity,
					Guid.Parse("22222222-2222-2222-2222-222222222222"));
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.Parse("33333333-3333-3333-3333-333333333333"));
				CollectionTargetIdentity target = CollectionTargetIdentity.FromFingerprint("target-c78-store");
				var definition = new CollectionDefinition(collectionIdentity, "Saved setup", null, "Captured from NMM.");
				var revision = new CollectionRevision(revisionIdentity, "Captured setup", null, 1);

				CollectionsRetainedArtifact contentArtifact = Publish(artifacts, "captured owner bytes");
				const string contentRole = "owner-payload:test";
				references.AcquireExclusiveRoleReference(contentArtifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), contentRole);
				var retained = new RetainedArtifactReference(contentArtifact.ArtifactId, contentRole,
					contentArtifact.ContentHash, contentArtifact.ByteLength);

				CollectionsRetainedArtifact packageArtifact = Publish(artifacts, "versioned capture package");
				references.AcquireExclusiveRoleReference(packageArtifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(),
					CollectionsLocalCaptureStore.PackageReferenceRole);

				var scope = new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[]
				{
					LocalCaptureScopeArea.ManagedModState,
					LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads
				});
				var exclusion = new LocalCaptureExclusion(LocalCaptureScopeArea.FileOwnershipAndFallbackPayloads,
					"fixture-exclusion", "Fixture exclusion retained for round-trip coverage.");
				var mapping = new LocalCaptureNativeRecordMapping(
					CollectionMemberKey.FromLocal(Guid.Parse("44444444-4444-4444-4444-444444444444")),
					new NativeModInstanceIdentity(target, "native-a"));
				var capture = new LocalCapture(captureIdentity, revisionIdentity, target,
					new CollectionCurrentStateFingerprint("state-v1", "fingerprint-c78"), scope,
					LocalCaptureCapability.RecipeOnly, new[] { retained }, new[] { exclusion }, new[] { mapping });

				captures.Save(definition, revision, capture, CollectionLocalCapturePackageCodec.CurrentFormatVersion,
					packageArtifact.ArtifactId);

				LocalCapture loaded = captures.GetCapture(captureIdentity);
				Assert.IsNotNull(loaded);
				Assert.AreEqual(capture.Identity, loaded.Identity);
				Assert.AreEqual(capture.Revision, loaded.Revision);
				Assert.AreEqual(capture.SourceTarget, loaded.SourceTarget);
				Assert.AreEqual(capture.CapturedStateFingerprint, loaded.CapturedStateFingerprint);
				Assert.AreEqual(capture.Capability, loaded.Capability);
				Assert.AreEqual(capture.SchemaVersion, loaded.SchemaVersion);
				Assert.AreEqual(capture.CapabilityVersion, loaded.CapabilityVersion);
				Assert.AreEqual(1, loaded.RetainedArtifacts.Count);
				Assert.AreEqual(retained, loaded.RetainedArtifacts.Single());
				Assert.AreEqual(exclusion, loaded.Exclusions.Single());
				Assert.AreEqual(mapping, loaded.NativeRecordMappings.Single());
				Assert.AreEqual(packageArtifact.ArtifactId, captures.GetPackageArtifactId(captureIdentity));

				var catalog = new CollectionsCatalogStore(store);
				Assert.AreEqual("Saved setup", catalog.GetDefinition(collectionIdentity).DisplayName);
				Assert.AreEqual(1, catalog.GetRevision(revisionIdentity).DeclaredMemberCount);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Save_RequiresCaptureOwnedPackageReferenceBeforePublishingCatalogRows()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.NewGuid());
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity, Guid.NewGuid());
				LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(Guid.NewGuid());
				var definition = new CollectionDefinition(collectionIdentity, "Not published", null, null);
				var revision = new CollectionRevision(revisionIdentity, "Capture", null, 0);
				var target = CollectionTargetIdentity.FromFingerprint("target-c78-missing-package-reference");
				var capture = new LocalCapture(captureIdentity, revisionIdentity, target,
					new CollectionCurrentStateFingerprint("state-v1", "fingerprint"),
					new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[] { LocalCaptureScopeArea.ManagedModState }),
					LocalCaptureCapability.RecipeOnly, new RetainedArtifactReference[0],
					new LocalCaptureExclusion[0], new LocalCaptureNativeRecordMapping[0]);
				CollectionsRetainedArtifact packageArtifact = Publish(artifacts, "unreferenced package");

				Assert.Throws<InvalidOperationException>(() => captures.Save(definition, revision, capture,
					CollectionLocalCapturePackageCodec.CurrentFormatVersion, packageArtifact.ArtifactId));

				var catalog = new CollectionsCatalogStore(store);
				Assert.IsNull(catalog.GetDefinition(collectionIdentity));
				Assert.IsNull(captures.GetCapture(captureIdentity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void GetCapturesForTarget_IsTargetScopedAndStableRegardlessOfSaveOrder()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				CollectionTargetIdentity targetA = CollectionTargetIdentity.FromFingerprint("target-c78-list-a");
				CollectionTargetIdentity targetB = CollectionTargetIdentity.FromFingerprint("target-c78-list-b");

				LocalCapture second = SaveMinimalCapture(captures, artifacts, references, targetA,
					Guid.Parse("22222222-2222-2222-2222-222222222222"));
				SaveMinimalCapture(captures, artifacts, references, targetB,
					Guid.Parse("33333333-3333-3333-3333-333333333333"));
				LocalCapture first = SaveMinimalCapture(captures, artifacts, references, targetA,
					Guid.Parse("11111111-1111-1111-1111-111111111111"));

				LocalCapture[] targetCaptures = captures.GetCapturesForTarget(targetA).ToArray();

				Assert.AreEqual(2, targetCaptures.Length);
				Assert.AreEqual(first.Identity, targetCaptures[0].Identity);
				Assert.AreEqual(second.Identity, targetCaptures[1].Identity);
				Assert.IsTrue(targetCaptures.All(x => x.SourceTarget.Equals(targetA)));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		/// <summary>Deleting one capture releases only its ownership while shared backups and terminal journals keep their content.</summary>
		[Test]
		public void Delete_PreservesSharedContentOtherCapturesAndHistoricalCatalog()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("delete-shared-content");
				LocalCapture selected = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				LocalCapture other = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				string packageId = captures.GetPackageArtifactId(selected.Identity);
				references.AcquireExclusiveRoleReference(packageId, CollectionsRetainedArtifactOwnerKind.Capture,
					other.Identity.ToString(), "shared-payload");
				CollectionOperation operation = SaveCaptureOperation(store, selected, target,
					CollectionOperationKind.RestoreLocalCapture, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed);
				references.AcquireExclusiveRoleReference(packageId, CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D"), "history-package");

				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.Ready, captures.GetDeletionStatus(selected.Identity, target));
				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.Deleted, captures.Delete(selected.Identity, target));
				Assert.IsNull(captures.GetCapture(selected.Identity));
				Assert.IsNull(captures.GetPackageArtifactId(selected.Identity));
				Assert.AreEqual(0, references.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, selected.Identity.ToString()).Count);
				Assert.AreEqual(2, references.GetReferencesForArtifact(packageId).Count);
				Assert.IsTrue(artifacts.VerifyArtifact(packageId));
				Assert.IsNotNull(captures.GetCapture(other.Identity));
				Assert.IsNotNull(new CollectionsCatalogStore(store).GetRevision(selected.Revision));
				Assert.IsNotNull(new CollectionsOperationStore(store).GetOperation(operation.Identity));
				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.NotFound, captures.Delete(selected.Identity, target));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>A dependency created after the read-only review must block the write, including an association on another target.</summary>
		[Test]
		public void Delete_RechecksInstalledAssociationAfterReviewWithoutReleasingReferences()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("delete-installed-source");
				LocalCapture capture = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.Ready, captures.GetDeletionStatus(capture.Identity, target));
				var association = new CollectionTargetAssociation(Guid.NewGuid(), capture.Revision,
					CollectionTargetIdentity.FromFingerprint("delete-installed-other-target"), CollectionAssociationState.Incomplete);
				var associations = new CollectionsAssociationStore(store);
				associations.SaveAssociation(association);

				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.InstalledCollection, captures.Delete(capture.Identity, target));
				Assert.IsNotNull(captures.GetCapture(capture.Identity));
				Assert.IsNotNull(associations.GetAssociation(association.AssociationId));
				Assert.AreEqual(1, references.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, capture.Identity.ToString()).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Nonterminal restore and revision journals protect the selected revision even when another target uses it.</summary>
		[TestCase(CollectionOperationKind.RestoreLocalCapture, CollectionOperationPhase.ReadyToApply, CollectionOperationResultState.Pending)]
		[TestCase(CollectionOperationKind.RestoreLocalCapture, CollectionOperationPhase.RecoveryRequired, CollectionOperationResultState.RecoveryRequired)]
		[TestCase(CollectionOperationKind.UpdateRevision, CollectionOperationPhase.PausedAtSafeBoundary, CollectionOperationResultState.Pending)]
		public void Delete_BlocksUnfinishedExactRevision(CollectionOperationKind kind, CollectionOperationPhase phase, CollectionOperationResultState result)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("delete-recovery-source");
				LocalCapture capture = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				SaveCaptureOperation(store, capture, CollectionTargetIdentity.FromFingerprint("delete-recovery-other-target"), kind, phase, result);

				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.IncompleteOperation, captures.Delete(capture.Identity, target));
				Assert.IsNotNull(captures.GetCapture(capture.Identity));
				Assert.AreEqual(1, references.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, capture.Identity.ToString()).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>An unrelated operation protects a capture when its recovery references explicitly retain that package.</summary>
		[Test]
		public void Delete_BlocksOperationOwnedPackageWithoutMatchingRevision()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("delete-package-source");
				LocalCapture selected = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				LocalCapture other = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				CollectionOperation operation = SaveCaptureOperation(store, other, target, CollectionOperationKind.ApplyResolvedPlan,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending);
				references.AcquireExclusiveRoleReference(captures.GetPackageArtifactId(selected.Identity), CollectionsRetainedArtifactOwnerKind.Operation,
					operation.Identity.OperationId.ToString("D"), "recovery-package");

				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.IncompleteOperation, captures.Delete(selected.Identity, target));
				Assert.IsNotNull(captures.GetCapture(selected.Identity));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>The replacement barrier protects same-target backups without blocking deletion on an unrelated setup.</summary>
		[TestCase(true, CollectionsLocalCaptureDeletionStatus.ReplacementInProgress)]
		[TestCase(false, CollectionsLocalCaptureDeletionStatus.Deleted)]
		public void Delete_ScopesReplacementBarrierToSourceTarget(bool sameTarget, CollectionsLocalCaptureDeletionStatus expected)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("delete-replacement-source");
				var otherTarget = CollectionTargetIdentity.FromFingerprint("delete-replacement-other");
				LocalCapture selected = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				LocalCapture other = SaveMinimalCapture(captures, artifacts, references, otherTarget, Guid.NewGuid());
				SaveCaptureOperation(store, other, sameTarget ? target : otherTarget, CollectionOperationKind.ReplaceCurrentManagedSetup,
					CollectionOperationPhase.CapturingOptionalLocalBackup, CollectionOperationResultState.Pending);

				Assert.AreEqual(expected, captures.Delete(selected.Identity, target));
				Assert.IsNotNull(captures.GetCapture(other.Identity));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Wrong-target selection is rejected, and a failed metadata delete rolls back reference release.</summary>
		[Test]
		public void Delete_RejectsWrongTargetAndRollsBackReferenceReleaseOnFailure()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				var captures = new CollectionsLocalCaptureStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("delete-rollback-source");
				LocalCapture capture = SaveMinimalCapture(captures, artifacts, references, target, Guid.NewGuid());
				Assert.AreEqual(CollectionsLocalCaptureDeletionStatus.TargetMismatch,
					captures.Delete(capture.Identity, CollectionTargetIdentity.FromFingerprint("delete-wrong-target")));
				store.ExecuteWrite((connection, transaction) =>
				{
					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = "CREATE TRIGGER reject_capture_delete BEFORE DELETE ON local_captures BEGIN SELECT RAISE(ABORT, 'fixture delete failure'); END;";
						command.ExecuteNonQuery();
					}
				});

				Assert.Catch(() => captures.Delete(capture.Identity, target));
				Assert.IsNotNull(captures.GetCapture(capture.Identity));
				Assert.IsNotNull(captures.GetPackageArtifactId(capture.Identity));
				Assert.AreEqual(1, references.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, capture.Identity.ToString()).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Persists one capture-related journal with an explicit target and lifecycle for deletion dependency coverage.</summary>
		private static CollectionOperation SaveCaptureOperation(CollectionsStore store, LocalCapture capture, CollectionTargetIdentity target,
			CollectionOperationKind kind, CollectionOperationPhase phase, CollectionOperationResultState result)
		{
			var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), kind, capture.Revision.Collection, target,
				capture.Revision, null, 0, phase, result, new CollectionNativeChildOperation[0]);
			new CollectionsOperationStore(store).SaveOperation(operation);
			return operation;
		}

		private static LocalCapture SaveMinimalCapture(CollectionsLocalCaptureStore captures,
			CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references,
			CollectionTargetIdentity target, Guid captureGuid)
		{
			CollectionIdentity collectionIdentity = CollectionIdentity.FromLocal(Guid.NewGuid());
			CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromLocal(collectionIdentity, Guid.NewGuid());
			LocalCaptureIdentity captureIdentity = LocalCaptureIdentity.From(captureGuid);
			var definition = new CollectionDefinition(collectionIdentity, "Gate L capture", null, null);
			var revision = new CollectionRevision(revisionIdentity, "Gate L revision", null, 0);
			var capture = new LocalCapture(captureIdentity, revisionIdentity, target,
				new CollectionCurrentStateFingerprint("state-v1", captureGuid.ToString("N")),
				new LocalCaptureScope(LocalCaptureScope.CurrentVersion, new[] { LocalCaptureScopeArea.ManagedModState }),
				LocalCaptureCapability.RecipeOnly, new RetainedArtifactReference[0],
				new LocalCaptureExclusion[0], new LocalCaptureNativeRecordMapping[0]);
			CollectionsRetainedArtifact package = Publish(artifacts, "gate-l-package-" + captureGuid.ToString("N"));
			references.AcquireExclusiveRoleReference(package.ArtifactId, CollectionsRetainedArtifactOwnerKind.Capture,
				captureIdentity.ToString(), CollectionsLocalCaptureStore.PackageReferenceRole);
			captures.Save(definition, revision, capture, CollectionLocalCapturePackageCodec.CurrentFormatVersion, package.ArtifactId);
			return capture;
		}

		private static CollectionsRetainedArtifact Publish(CollectionsRetainedArtifactStore store, string text)
		{
			using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text), false))
				return store.Publish(stream);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c78-local-capture-store-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
