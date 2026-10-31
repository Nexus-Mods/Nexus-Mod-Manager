using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionsRetainedArtifactCleanupStoreTests
	{
		/// <summary>Opening Collections without retained data must not provision a feature database for maintenance.</summary>
		[Test]
		public void CleanupPreflight_MissingStoreDoesNotCreateIt()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Assert.IsFalse(new CollectionsRetainedArtifactCleanupStore(store).HasPendingCleanup(CollectionTargetIdentity.FromFingerprint("target")));
				Assert.IsFalse(store.Exists);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Downloaded and installed Collections with retained references are normal saved data, not stale cleanup work.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void CleanupPreflight_RetainedCollectionDoesNotNeedCleanup(bool installed)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root); store.CreateNew();
				CollectionRevisionIdentity revision = SaveRevision(store);
				var target = CollectionTargetIdentity.FromFingerprint("target");
				if (installed)
					new CollectionsAssociationStore(store).SaveAssociation(new CollectionTargetAssociation(Guid.NewGuid(), revision, target, CollectionAssociationState.Applied));
				CollectionsRetainedArtifact artifact = Publish(new CollectionsRetainedArtifactStore(store), "retained download");
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				references.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(revision), "manifest");
				var cleanup = new CollectionsRetainedArtifactCleanupStore(store);

				Assert.IsFalse(cleanup.HasPendingCleanup(target));
				Assert.IsFalse(new CollectionsRetainedArtifactCleanupStore(new CollectionsStore(root)).HasPendingCleanup(target));
				Assert.AreEqual(1, references.GetReferencesForArtifact(artifact.ArtifactId).Count);
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)));
				Assert.AreEqual(0, cleanup.GetPendingTombstones(1).Count);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Unreferenced blobs and interrupted deletions still trigger cleanup, and a completed sweep is not repeated.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void CleanupPreflight_IdentifiesRealArtifactsWithoutChangingThem(bool tombstoned)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root); store.CreateNew();
				CollectionsRetainedArtifact artifact = Publish(new CollectionsRetainedArtifactStore(store), "obsolete");
				var cleanup = new CollectionsRetainedArtifactCleanupStore(store);
				var target = CollectionTargetIdentity.FromFingerprint("target");
				if (tombstoned) Assert.IsTrue(cleanup.TryMarkForCleanup(artifact.ArtifactId));

				Assert.IsTrue(cleanup.HasPendingCleanup(target));
				Assert.AreEqual(tombstoned, cleanup.IsMarkedForCleanup(artifact.ArtifactId));
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)), "Preflight must not delete content.");
				Assert.IsTrue(cleanup.TryCollectUnreferencedArtifact(artifact.ArtifactId));
				Assert.IsFalse(cleanup.HasPendingCleanup(target));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Completed removals need maintenance only while disposable references remain, with preview and target exclusions intact.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void CleanupPreflight_RemovedReferencesAreDetectedOnce(bool acquisition)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root); store.CreateNew();
				CollectionRevisionIdentity revision = SaveRevision(store);
				var target = CollectionTargetIdentity.FromFingerprint("target");
				var operation = new CollectionOperation(CollectionOperationIdentity.CreateNew(), CollectionOperationKind.UninstallCollectionEffects,
					revision.Collection, target, revision, null, 1, CollectionOperationPhase.Completed, CollectionOperationResultState.Committed,
					new CollectionNativeChildOperation[0]);
				new CollectionsOperationStore(store).SaveOperation(operation);
				CollectionsRetainedArtifact artifact = Publish(new CollectionsRetainedArtifactStore(store), "keep revision source");
				var references = new CollectionsRetainedArtifactReferenceStore(store);
				references.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(revision), "manifest");
				if (acquisition)
				{
					string requestId = Guid.NewGuid().ToString("D");
					store.ExecuteWrite((connection, transaction) =>
					{
						using (SQLiteCommand command = connection.CreateCommand())
						{
							command.Transaction = transaction;
							command.CommandText = @"
INSERT INTO collection_acquisition_requests(request_id, plan_id, plan_version, revision_identity, target_fingerprint,
member_key_kind, member_key_value, requirement, artifact_scheme, artifact_stable_id, recipe_fingerprint, mode, state, updated_utc)
VALUES(@request, 'plan', 1, @revision, @target, 1, 'member', 1, 'nexus', 'archive', 'recipe', 1, @cancelled, @utc);";
							command.Parameters.AddWithValue("@request", requestId);
							command.Parameters.AddWithValue("@revision", CollectionsRevisionSourceStore.GetRevisionOwnerId(revision));
							command.Parameters.AddWithValue("@target", target.Fingerprint);
							command.Parameters.AddWithValue("@cancelled", (int)CollectionAcquisitionPersistenceState.Cancelled);
							command.Parameters.AddWithValue("@utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
							command.ExecuteNonQuery();
						}
					});
					references.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download, requestId, "archive");
				}
				else
					references.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation, operation.Identity.OperationId.ToString("D"), "operation");
				var cleanup = new CollectionsRetainedArtifactCleanupStore(store);

				Assert.IsTrue(cleanup.HasPendingCleanup(target));
				Assert.IsFalse(cleanup.HasPendingCleanup(target, revision.Collection));
				Assert.IsFalse(cleanup.HasPendingCleanup(CollectionTargetIdentity.FromFingerprint("other-game")));
				Assert.AreEqual(2, references.GetReferencesForArtifact(artifact.ArtifactId).Count, "Preflight must not release references.");
				Assert.IsTrue(cleanup.ReleaseRemovedCollectionContent(target));
				Assert.AreEqual(1, references.GetReferencesForArtifact(artifact.ArtifactId).Count);
				Assert.IsFalse(cleanup.HasPendingCleanup(target), "Old completed removal records alone must not cause another sweep.");
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Pending native work still defers cleanup even when an unreferenced artifact exists.</summary>
		[Test]
		public void CleanupPreflight_UnfinishedOperationDefersCollection()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root); store.CreateNew();
				CollectionRevisionIdentity revision = SaveRevision(store);
				var target = CollectionTargetIdentity.FromFingerprint("target");
				new CollectionsOperationStore(store).SaveOperation(new CollectionOperation(CollectionOperationIdentity.CreateNew(),
					CollectionOperationKind.UninstallCollectionEffects, revision.Collection, target, revision, null, 1,
					CollectionOperationPhase.Preparing, CollectionOperationResultState.Pending, new CollectionNativeChildOperation[0]));
				CollectionsRetainedArtifact artifact = Publish(new CollectionsRetainedArtifactStore(store), "wait for recovery");
				var cleanup = new CollectionsRetainedArtifactCleanupStore(store);
				Assert.IsFalse(cleanup.HasPendingCleanup(target));
				Assert.IsFalse(cleanup.ReleaseRemovedCollectionContent(target));
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Creates a remembered revision without introducing an installed association or operation.</summary>
		private static CollectionRevisionIdentity SaveRevision(CollectionsStore store)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("cleanup-preflight");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision", 1);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(new CollectionDefinition(collection, "Collection", null, null),
				new CollectionRevision(revision, "Revision 1", null, null));
			return revision;
		}

		[Test]
		public void CollectArchiveReference_DeletesOnlyCollectionMetadataAndLeavesLibraryArchiveIntact()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root); store.CreateNew();
				string archive = Path.Combine(root, "mod.zip"); File.WriteAllText(archive, "keep this archive");
				CollectionsRetainedArtifact reference = new CollectionsRetainedArtifactStore(store).ReferenceArchiveFile(archive);
				Assert.IsTrue(new CollectionsRetainedArtifactCleanupStore(store).TryCollectUnreferencedArtifact(reference.ArtifactId));
				Assert.AreEqual("keep this archive", File.ReadAllText(archive));
				Assert.IsNull(new CollectionsRetainedArtifactStore(store).GetArtifact(reference.ArtifactId));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void CollectUnreferencedArtifact_MarksDeletesAndFinalizesTrackedBlob()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "collect me");
				string blobPath = BlobPath(store, artifact.ContentHash.Value);

				Assert.IsTrue(cleanupStore.TryCollectUnreferencedArtifact(artifact.ArtifactId));
				Assert.IsFalse(File.Exists(blobPath));
				Assert.IsNull(retainedStore.GetArtifact(artifact.ArtifactId));
				Assert.IsFalse(cleanupStore.IsMarkedForCleanup(artifact.ArtifactId));
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_tombstones;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReferencedArtifact_IsNeverMarkedOrDeleted()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "still owned");
				referenceStore.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
					"revision-1", "manifest");

				Assert.IsFalse(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));
				Assert.IsFalse(cleanupStore.TryCollectUnreferencedArtifact(artifact.ArtifactId));
				Assert.IsFalse(cleanupStore.IsMarkedForCleanup(artifact.ArtifactId));
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)));
				Assert.IsNotNull(retainedStore.GetArtifact(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void MarkForCleanup_IsIdempotentAndBlocksNewReferences()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "pending cleanup");

				Assert.IsTrue(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));
				Assert.IsTrue(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));
				Assert.IsTrue(cleanupStore.IsMarkedForCleanup(artifact.ArtifactId));
				Assert.Throws<InvalidOperationException>(() => referenceStore.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, "late-operation", "recovery"));
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_tombstones;"));
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)),
					"Marking is a durable barrier; it does not itself delete bytes.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CollectMarkedArtifact_ResumesAfterBlobWasAlreadyDeleted()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "crash after file delete");
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				Assert.IsTrue(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));
				File.Delete(BlobPath(store, artifact.ContentHash.Value));

				var resumedCleanup = new CollectionsRetainedArtifactCleanupStore(new CollectionsStore(root));
				Assert.IsTrue(resumedCleanup.TryCollectMarkedArtifact(artifact.ArtifactId));
				Assert.IsNull(retainedStore.GetArtifact(artifact.ArtifactId));
				Assert.AreEqual(0, resumedCleanup.GetPendingTombstones(10).Count);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CollectMarkedArtifact_RechecksReferencesAndCancelsDeletionIfProtectionAppears()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "recheck before delete");
				Assert.IsTrue(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));

				// Simulate a stale/legacy writer bypassing the C4.11 API. Cleanup must still recheck the durable table.
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = @"
INSERT INTO retained_artifact_references(reference_id, artifact_id, owner_kind, owner_id, role)
VALUES(@reference_id, @artifact_id, @owner_kind, @owner_id, @role);";
					command.Parameters.AddWithValue("@reference_id", Guid.NewGuid().ToString("D"));
					command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
					command.Parameters.AddWithValue("@owner_kind", (int)CollectionsRetainedArtifactOwnerKind.Capture);
					command.Parameters.AddWithValue("@owner_id", "legacy-capture");
					command.Parameters.AddWithValue("@role", "payload");
					command.ExecuteNonQuery();
				}

				Assert.IsFalse(cleanupStore.TryCollectMarkedArtifact(artifact.ArtifactId));
				Assert.IsFalse(cleanupStore.IsMarkedForCleanup(artifact.ArtifactId),
					"A discovered owner cancels the cleanup tombstone before physical deletion.");
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)));
				Assert.IsNotNull(retainedStore.GetArtifact(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RevisionSourceDirectArtifactLink_ProtectsBlobEvenWithoutReferenceRow()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "revision source bytes");
				var catalog = new CollectionsCatalogStore(store);
				CollectionIdentity collectionIdentity = CollectionIdentity.FromNexus("gc-source-protection");
				CollectionRevisionIdentity revisionIdentity = CollectionRevisionIdentity.FromNexus(collectionIdentity, "revision-one", 1);
				catalog.SaveDefinitionAndRevision(
					new CollectionDefinition(collectionIdentity, "GC source protection", null, null),
					new CollectionRevision(revisionIdentity, "Revision one", null, 1));

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = @"
INSERT INTO revision_sources
    (origin, collection_id, revision_id, source_input_kind,
     bundle_hash_algorithm, bundle_hash_value, bundle_byte_length,
     manifest_entry_name, manifest_hash_algorithm, manifest_hash_value, manifest_byte_length,
     schema_identity, normalizer_version, raw_bundle_artifact_id, raw_manifest_artifact_id)
VALUES
    (@origin, @collection_id, @revision_id, 1,
     'sha256', @hash_value, @byte_length,
     'manifest.json', 'sha256', @hash_value, @byte_length,
     'test-schema', '1', @artifact_id, NULL);";
					command.Parameters.AddWithValue("@origin", (int)collectionIdentity.Origin);
					command.Parameters.AddWithValue("@collection_id", collectionIdentity.StableId);
					command.Parameters.AddWithValue("@revision_id", revisionIdentity.StableRevisionId);
					command.Parameters.AddWithValue("@hash_value", artifact.ContentHash.Value);
					command.Parameters.AddWithValue("@byte_length", artifact.ByteLength);
					command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
					command.ExecuteNonQuery();
				}

				Assert.IsFalse(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));
				Assert.IsFalse(cleanupStore.TryCollectUnreferencedArtifact(artifact.ArtifactId));
				Assert.IsTrue(File.Exists(BlobPath(store, artifact.ContentHash.Value)));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CleanupCandidates_AreBoundedAndExcludeReferencedOrTombstonedArtifacts()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact referenced = Publish(retainedStore, "referenced");
				CollectionsRetainedArtifact tombstoned = Publish(retainedStore, "tombstoned");
				CollectionsRetainedArtifact candidate = Publish(retainedStore, "candidate");
				referenceStore.AcquireReference(referenced.ArtifactId, CollectionsRetainedArtifactOwnerKind.Download,
					"download-1", "archive");
				Assert.IsTrue(cleanupStore.TryMarkForCleanup(tombstoned.ArtifactId));

				IReadOnlyList<string> candidates = cleanupStore.GetCleanupCandidates(1);
				Assert.AreEqual(1, candidates.Count);
				Assert.AreEqual(candidate.ArtifactId, candidates[0]);
				CollectionAssert.AreEqual(new[] { tombstoned.ArtifactId }, cleanupStore.GetPendingTombstones(10));
				Assert.Throws<ArgumentOutOfRangeException>(() => cleanupStore.GetCleanupCandidates(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Cleanup_InvalidPersistedPathFailsClosedWithoutTouchingOutsideFile()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				CollectionsRetainedArtifact artifact = Publish(retainedStore, "path safety");
				string outsidePath = Path.Combine(store.StoreDirectory, "outside.bin");
				File.WriteAllText(outsidePath, "must survive");
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = "UPDATE retained_artifacts SET relative_path='../outside.bin' WHERE artifact_id=@artifact_id;";
					command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
					command.ExecuteNonQuery();
				}

				Assert.IsTrue(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));
				Assert.Throws<InvalidDataException>(() => cleanupStore.TryCollectMarkedArtifact(artifact.ArtifactId));
				Assert.IsTrue(File.Exists(outsidePath));
				Assert.IsTrue(cleanupStore.IsMarkedForCleanup(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_SameContentWhileTombstonedIsRejectedInsteadOfResurrectingCleanupCandidate()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				byte[] bytes = Encoding.UTF8.GetBytes("do not resurrect during cleanup");
				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(bytes, false))
					artifact = retainedStore.Publish(source);
				Assert.IsTrue(cleanupStore.TryMarkForCleanup(artifact.ArtifactId));

				using (var source = new MemoryStream(bytes, false))
					Assert.Throws<InvalidOperationException>(() => retainedStore.Publish(source));
				Assert.IsTrue(cleanupStore.IsMarkedForCleanup(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Cleanup_DoesNotGuessThatUntrackedPhysicalBlobIsSafeToDelete()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var cleanupStore = new CollectionsRetainedArtifactCleanupStore(store);
				byte[] bytes = Encoding.UTF8.GetBytes("publication orphan");
				string hash = Sha256(bytes);
				string orphanPath = BlobPath(store, hash);
				Directory.CreateDirectory(Path.GetDirectoryName(orphanPath));
				File.WriteAllBytes(orphanPath, bytes);

				Assert.AreEqual(0, cleanupStore.GetCleanupCandidates(10).Count);
				Assert.IsFalse(cleanupStore.TryCollectUnreferencedArtifact("sha256:" + hash));
				Assert.IsTrue(File.Exists(orphanPath),
					"C4.12 only deletes tracked feature-owned artifacts; it must not race an uncommitted C4.10 publication.");
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionsRetainedArtifact Publish(CollectionsRetainedArtifactStore store, string text)
		{
			using (var source = new MemoryStream(Encoding.UTF8.GetBytes(text), false))
				return store.Publish(source);
		}

		private static SQLiteConnection OpenDatabase(string databasePath)
		{
			var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
			{
				DataSource = databasePath,
				ForeignKeys = true,
				Pooling = false,
				FailIfMissing = true
			}.ConnectionString);
			connection.Open();
			return connection;
		}

		private static int ScalarInt(SQLiteConnection connection, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = sql;
				return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
			}
		}

		private static string BlobPath(CollectionsStore store, string hash)
		{
			return Path.Combine(store.RetainedContentDirectory, "sha256", hash.Substring(0, 2), hash + ".blob");
		}

		private static string Sha256(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] digest = sha.ComputeHash(bytes);
				var builder = new StringBuilder(digest.Length * 2);
				foreach (byte value in digest)
					builder.Append(value.ToString("x2"));
				return builder.ToString();
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmCollectionsCleanup-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}
	}
}
