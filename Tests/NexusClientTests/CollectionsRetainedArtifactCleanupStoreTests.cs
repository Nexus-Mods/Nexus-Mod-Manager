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
