using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionsRetainedArtifactReferenceStoreTests
	{
		[Test]
		public void AcquireReference_AllSupportedOwnerKindsPersistAndRoundTrip()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out featureStore);
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);
				var owners = new[]
				{
					CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRetainedArtifactOwnerKind.Download,
					CollectionsRetainedArtifactOwnerKind.Operation,
					CollectionsRetainedArtifactOwnerKind.Capture
				};

				for (int index = 0; index < owners.Length; index++)
				{
					CollectionsRetainedArtifactReferenceRecord reference = references.AcquireReference(
						artifact.ArtifactId, owners[index], "owner-" + index, "payload");
					Assert.AreEqual(reference, references.GetReference(reference.ReferenceId));
					Assert.IsTrue(reference.ProtectsRetainedContentFromCleanup);
					Assert.IsFalse(reference.AuthorizesNativeDeploymentRetention);
				}

				Assert.AreEqual(4, references.GetReferencesForArtifact(artifact.ArtifactId).Count);
				Assert.IsTrue(references.IsReferenced(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireReference_SameOwnerArtifactAndRoleIsIdempotent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out featureStore);
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);

				CollectionsRetainedArtifactReferenceRecord first = references.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, "operation-1", "rollback-preimage");
				CollectionsRetainedArtifactReferenceRecord second = references.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, "operation-1", "rollback-preimage");

				Assert.AreEqual(first.ReferenceId, second.ReferenceId);
				Assert.AreEqual(1, references.GetReferencesForArtifact(artifact.ArtifactId).Count);
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireReference_MissingArtifactIsRejectedWithoutCreatingAReference()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);

				Assert.Throws<InvalidOperationException>(() => references.AcquireReference("sha256:" + new string('a', 64),
					CollectionsRetainedArtifactOwnerKind.Download, "download-1", "verified-input"));

				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReleaseReference_SharedArtifactRemainsProtectedUntilLastOwnerReleasesIt()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out featureStore);
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);
				CollectionsRetainedArtifactReferenceRecord revision = references.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Revision, "revision-1", "manifest");
				CollectionsRetainedArtifactReferenceRecord operation = references.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Operation, "operation-1", "recovery-input");

				Assert.IsTrue(references.ReleaseReference(operation.ReferenceId));
				Assert.IsTrue(references.IsReferenced(artifact.ArtifactId));
				CollectionAssert.AreEqual(new[] { revision.ReferenceId },
					ReferenceIds(references.GetReferencesForArtifact(artifact.ArtifactId)));

				Assert.IsTrue(references.ReleaseReference(revision.ReferenceId));
				Assert.IsFalse(references.IsReferenced(artifact.ArtifactId));
				Assert.IsFalse(references.ReleaseReference(revision.ReferenceId));

				// C4.11 releases lifetime protection only; physical/metadata cleanup belongs to C4.12.
				Assert.IsNotNull(new CollectionsRetainedArtifactStore(featureStore).GetArtifact(artifact.ArtifactId));
				Assert.IsTrue(new CollectionsRetainedArtifactStore(featureStore).VerifyArtifact(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReleaseOwnerReferences_RemovesOnlyThatOwnersLeasesAcrossArtifacts()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var artifacts = new CollectionsRetainedArtifactStore(featureStore);
				CollectionsRetainedArtifact a;
				CollectionsRetainedArtifact b;
				using (var source = new MemoryStream(Encoding.UTF8.GetBytes("artifact A"), false))
					a = artifacts.Publish(source);
				using (var source = new MemoryStream(Encoding.UTF8.GetBytes("artifact B"), false))
					b = artifacts.Publish(source);
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);
				references.AcquireReference(a.ArtifactId, CollectionsRetainedArtifactOwnerKind.Capture, "capture-1", "archive");
				references.AcquireReference(b.ArtifactId, CollectionsRetainedArtifactOwnerKind.Capture, "capture-1", "replay");
				references.AcquireReference(a.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision, "revision-1", "archive");

				Assert.AreEqual(2, references.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Capture, "capture-1"));
				Assert.AreEqual(0, references.ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind.Capture, "capture-1"));
				Assert.AreEqual(0, references.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Capture, "capture-1").Count);
				Assert.IsTrue(references.IsReferenced(a.ArtifactId));
				Assert.IsFalse(references.IsReferenced(b.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReferenceRowsSurviveStoreReopenAndPreserveOwnerRoleIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore firstStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out firstStore);
				var firstReferences = new CollectionsRetainedArtifactReferenceStore(firstStore);
				CollectionsRetainedArtifactReferenceRecord saved = firstReferences.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Download, "download-42", "verified-archive");

				var reopenedStore = new CollectionsStore(root);
				reopenedStore.OpenExisting();
				var reopenedReferences = new CollectionsRetainedArtifactReferenceStore(reopenedStore);
				Assert.AreEqual(saved, reopenedReferences.GetReference(saved.ReferenceId));
				Assert.AreEqual(saved, reopenedReferences.GetReferencesForOwner(
					CollectionsRetainedArtifactOwnerKind.Download, "download-42")[0]);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReadReference_InvalidPersistedOwnerKindFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out featureStore);
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = @"
INSERT INTO retained_artifact_references(reference_id, artifact_id, owner_kind, owner_id, role)
VALUES('bad-ref', @artifact_id, 999, 'owner', 'role');";
					command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
					command.ExecuteNonQuery();
				}

				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);
				Assert.Throws<CollectionsStoreSchemaException>(() => references.GetReference("bad-ref"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireReference_RejectsTemporaryHttpOwnerIdentityBeforeWriting()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out featureStore);
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);

				Assert.Throws<ArgumentException>(() => references.AcquireReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Download, "https://example.invalid/file?key=secret", "download"));
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireReference_RejectsUnknownOwnerKindBeforeWriting()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				CollectionsStore featureStore;
				CollectionsRetainedArtifact artifact = CreateStoreAndArtifact(root, out featureStore);
				var references = new CollectionsRetainedArtifactReferenceStore(featureStore);

				Assert.Throws<ArgumentOutOfRangeException>(() => references.AcquireReference(artifact.ArtifactId,
					(CollectionsRetainedArtifactOwnerKind)999, "owner", "role"));
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionsRetainedArtifact CreateStoreAndArtifact(string root, out CollectionsStore featureStore)
		{
			featureStore = new CollectionsStore(root);
			featureStore.CreateNew();
			var artifacts = new CollectionsRetainedArtifactStore(featureStore);
			using (var source = new MemoryStream(Encoding.UTF8.GetBytes("C4.11 shared retained artifact"), false))
				return artifacts.Publish(source);
		}

		private static string[] ReferenceIds(IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> references)
		{
			var values = new string[references.Count];
			for (int index = 0; index < references.Count; index++)
				values[index] = references[index].ReferenceId;
			return values;
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmCollectionsRetainedRefs-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
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
				return Convert.ToInt32(command.ExecuteScalar());
			}
		}
	}
}
