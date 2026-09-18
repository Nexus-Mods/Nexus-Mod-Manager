using System;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C4.5/C4.9/C4.12 feature-store schema, migration, availability and failure-handling coverage.
	/// </summary>
	public class CollectionsStoreFoundationTests
	{
		[Test]
		public void StorePaths_FollowTheSelectedInstallInfoStorage()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var first = new GameStoragePathSet { InstallInfoPath = Path.Combine(root, "InstallInfo-A") };
				var second = new GameStoragePathSet { InstallInfoPath = Path.Combine(root, "InstallInfo-B") };

				Assert.AreEqual(
					Path.Combine(Path.GetFullPath(first.InstallInfoPath), "Collections", "collections.sqlite"),
					CollectionsStorePaths.GetDatabasePath(first));
				Assert.AreEqual(
					Path.Combine(Path.GetFullPath(second.InstallInfoPath), "Collections", "collections.sqlite"),
					CollectionsStorePaths.GetDatabasePath(second));
				Assert.AreNotEqual(CollectionsStorePaths.GetDatabasePath(first), CollectionsStorePaths.GetDatabasePath(second));
				Assert.AreEqual(
					Path.Combine(Path.GetFullPath(first.InstallInfoPath), "Collections", "Content"),
					CollectionsStorePaths.GetRetainedContentDirectory(first));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void MissingStore_IsNeverSilentlyRecreatedByOpenOrRead()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Assert.IsFalse(store.Exists);
				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.Missing, inspection.Availability);
				Assert.IsFalse(inspection.CanReadFeatureState);
				Assert.IsFalse(inspection.CanWriteFeatureState);
				Assert.Throws<FileNotFoundException>(() => store.OpenExisting());
				Assert.Throws<FileNotFoundException>(() => store.ReadStoreId());
				Assert.IsFalse(File.Exists(store.DatabasePath));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CreateNew_CreatesVersionedFeatureSchemaAndStableStoreIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Guid createdStoreId = store.CreateNew();

				Assert.AreNotEqual(Guid.Empty, createdStoreId);
				Assert.IsTrue(store.Exists);
				CollectionsStoreStatus status = store.OpenExisting();
				Assert.AreEqual(createdStoreId, status.StoreId);
				Assert.AreEqual(CollectionsStore.CurrentSchemaVersion, status.SchemaVersion);
				Assert.AreEqual("delete", status.JournalMode.ToLowerInvariant());
				Assert.AreEqual(2, status.SynchronousMode, "C4 durable intent uses SQLite synchronous=FULL.");
				Assert.IsTrue(status.ForeignKeysEnabled);
				Assert.AreEqual(CollectionsStore.BusyTimeoutMilliseconds, status.BusyTimeoutMilliseconds);
				Assert.IsTrue(status.HasRequiredDurabilitySettings);
				Assert.AreEqual(createdStoreId, store.ReadStoreId());
				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.Ready, inspection.Availability);
				Assert.AreEqual(createdStoreId, inspection.StoreId);
				Assert.AreEqual(CollectionsStore.CurrentSchemaVersion, inspection.SchemaVersion);
				Assert.IsTrue(inspection.CanReadFeatureState);
				Assert.IsTrue(inspection.CanWriteFeatureState);

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual(CollectionsStore.CurrentSchemaVersion, ScalarInt(connection, "PRAGMA user_version;"));
					Assert.AreEqual("delete", ScalarString(connection, "PRAGMA journal_mode;").ToLowerInvariant());
					Assert.AreEqual("ok", ScalarString(connection, "PRAGMA integrity_check;").ToLowerInvariant());

					string[] requiredTables =
					{
						"store_metadata",
						"collections",
						"collection_revisions",
						"revision_sources",
						"resolved_plans",
						"target_associations",
						"member_bindings",
						"user_overrides",
						"drift_observations",
						"local_captures",
						"local_capture_scope_areas",
						"local_capture_exclusions",
						"local_capture_native_mappings",
						"retained_artifacts",
						"retained_artifact_references",
						"retained_artifact_tombstones",
						"collection_acquisition_requests",
						"collection_operations",
						"native_operation_children"
					};

					foreach (string table in requiredTables)
						Assert.IsTrue(TableExists(connection, table), "Missing expected Collections table: " + table);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Schema_ContainsIndexesForNativeLookupPendingOperationsAndRetainedReferences()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					string[] indexes =
					{
						"ix_collection_revisions_collection",
						"ix_revision_sources_raw_bundle_artifact",
						"ix_revision_sources_raw_manifest_artifact",
						"ix_resolved_plans_revision",
						"ix_target_associations_target",
						"ix_member_bindings_native",
						"ix_user_overrides_association",
						"ix_drift_observations_association",
						"ix_local_captures_revision",
						"ix_retained_artifact_references_artifact",
						"ix_collection_operations_pending",
						"ix_native_operation_children_native",
						"ix_collection_acquisition_queue",
						"ix_collection_acquisition_verified_artifact"
					};

					foreach (string index in indexes)
						Assert.IsTrue(IndexExists(connection, index), "Missing expected Collections index: " + index);

					Assert.Greater(ForeignKeyCount(connection, "member_bindings"), 0);
					Assert.Greater(ForeignKeyCount(connection, "native_operation_children"), 0);
					Assert.Greater(ForeignKeyCount(connection, "revision_sources"), 0);
					Assert.Greater(ForeignKeyCount(connection, "retained_artifact_tombstones"), 0);
					Assert.Greater(ForeignKeyCount(connection, "collection_acquisition_requests"), 0);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}


		[Test]
		public void Schema_DoesNotPersistCredentialsOrEphemeralDownloadLocations()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					string schema = ScalarString(connection,
						"SELECT group_concat(sql, ' ') FROM sqlite_master WHERE sql IS NOT NULL;").ToLowerInvariant();
					StringAssert.DoesNotContain("api_key", schema);
					StringAssert.DoesNotContain("apikey", schema);
					StringAssert.DoesNotContain("access_token", schema);
					StringAssert.DoesNotContain("signed_url", schema);
					StringAssert.DoesNotContain("download_url", schema);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CreateNew_DoesNotOverwriteAnExistingFeatureStore()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Guid originalStoreId = store.CreateNew();
				Assert.Throws<IOException>(() => store.CreateNew());
				Assert.AreEqual(originalStoreId, store.OpenExisting().StoreId);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OpenExisting_MigratesVersion1ThroughCurrentSchemaWithoutLosingRetainedState()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Guid storeId = store.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(store);
				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("schema migration retained bytes"), false))
					artifact = retainedStore.Publish(source);
				var referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
				referenceStore.AcquireReference(artifact.ArtifactId, CollectionsRetainedArtifactOwnerKind.Operation,
					"migration-operation", "recovery");

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Execute(connection, null, "DROP INDEX ix_collection_acquisition_queue;");
					Execute(connection, null, "DROP TABLE collection_acquisition_requests;");
					Execute(connection, null, "DROP TABLE retained_artifact_tombstones;");
					Execute(connection, null, "DROP INDEX ix_revision_sources_raw_bundle_artifact;");
					Execute(connection, null, "DROP INDEX ix_revision_sources_raw_manifest_artifact;");
					Execute(connection, null, "UPDATE store_metadata SET value='1' WHERE key='schema_version';");
					Execute(connection, null, "PRAGMA user_version=1;");
				}

				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.MigrationRequired, inspection.Availability);
				Assert.AreEqual(1, inspection.SchemaVersion);
				Assert.IsFalse(inspection.CanReadFeatureState);
				Assert.IsFalse(inspection.CanWriteFeatureState);

				CollectionsStoreStatus migrated = store.OpenExisting();
				Assert.AreEqual(storeId, migrated.StoreId);
				Assert.AreEqual(CollectionsStore.CurrentSchemaVersion, migrated.SchemaVersion);
				Assert.IsTrue(retainedStore.VerifyArtifact(artifact.ArtifactId));
				Assert.AreEqual(1, referenceStore.GetReferencesForArtifact(artifact.ArtifactId).Count);
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual(CollectionsStore.CurrentSchemaVersion, ScalarInt(connection, "PRAGMA user_version;"));
					Assert.AreEqual(CollectionsStore.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture), ScalarString(connection, "SELECT value FROM store_metadata WHERE key='schema_version';"));
					Assert.IsTrue(TableExists(connection, "retained_artifact_tombstones"));
					Assert.IsTrue(TableExists(connection, "collection_acquisition_requests"));
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void InspectExisting_CorruptVersion1StoreIsNotRelabeledOrMigrated()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Execute(connection, null, "DROP TABLE retained_artifact_tombstones;");
					Execute(connection, null, "DROP INDEX ix_revision_sources_raw_bundle_artifact;");
					Execute(connection, null, "DROP INDEX ix_revision_sources_raw_manifest_artifact;");
					Execute(connection, null, "DROP INDEX ix_member_bindings_native;");
					Execute(connection, null, "UPDATE store_metadata SET value='1' WHERE key='schema_version';");
					Execute(connection, null, "PRAGMA user_version=1;");
				}

				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.InvalidOrCorrupt, inspection.Availability);
				Assert.Throws<CollectionsStoreSchemaException>(() => store.OpenExisting());
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual(1, ScalarInt(connection, "PRAGMA user_version;"));
					Assert.AreEqual("1", ScalarString(connection, "SELECT value FROM store_metadata WHERE key='schema_version';"));
					Assert.IsFalse(TableExists(connection, "retained_artifact_tombstones"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OpenExisting_RejectsUnknownNewerSchemaWithoutDowngradingIt()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual("wal", ScalarString(connection, "PRAGMA journal_mode=WAL;").ToLowerInvariant());
					using (SQLiteTransaction transaction = connection.BeginTransaction())
					{
						Execute(connection, transaction, "UPDATE store_metadata SET value='999' WHERE key='schema_version';");
						Execute(connection, transaction, "PRAGMA user_version=999;");
						transaction.Commit();
					}
				}

				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.UnsupportedNewerSchema, inspection.Availability);
				Assert.AreEqual(999, inspection.SchemaVersion);
				Assert.IsFalse(inspection.CanReadFeatureState);
				Assert.IsFalse(inspection.CanWriteFeatureState);

				CollectionsStoreSchemaException schemaException = Assert.Throws<CollectionsStoreSchemaException>(() => store.OpenExisting());
				Assert.AreEqual(CollectionsStoreSchemaFailureKind.NewerThanSupported, schemaException.FailureKind);
				Assert.AreEqual(999, schemaException.DetectedSchemaVersion);

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual(999, ScalarInt(connection, "PRAGMA user_version;"));
					Assert.AreEqual("999", ScalarString(connection, "SELECT value FROM store_metadata WHERE key='schema_version';"));
					Assert.AreEqual("wal", ScalarString(connection, "PRAGMA journal_mode;").ToLowerInvariant(),
						"An unknown newer store must be rejected before NMM mutates its journal mode or schema.");
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void InspectExisting_CorruptStoreIsNotReplacedOrReinitialized()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Directory.CreateDirectory(store.StoreDirectory);
				byte[] corruptBytes = { 0x4e, 0x4d, 0x4d, 0x2d, 0x43, 0x34, 0x2e, 0x39 };
				File.WriteAllBytes(store.DatabasePath, corruptBytes);

				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.InvalidOrCorrupt, inspection.Availability);
				Assert.IsFalse(inspection.CanReadFeatureState);
				Assert.IsFalse(inspection.CanWriteFeatureState);
				CollectionsStoreAccessException exception = Assert.Throws<CollectionsStoreAccessException>(() => store.OpenExisting());
				Assert.AreEqual(CollectionsStoreAccessFailureKind.Corrupt, exception.FailureKind);
				CollectionAssert.AreEqual(corruptBytes, File.ReadAllBytes(store.DatabasePath));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void ReadOnlyStore_RemainsReadableButFeatureWritesAreBlocked()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				Guid storeId = store.CreateNew();
				FileAttributes originalAttributes = File.GetAttributes(store.DatabasePath);
				try
				{
					File.SetAttributes(store.DatabasePath, originalAttributes | FileAttributes.ReadOnly);
					CollectionsStoreInspection inspection = store.InspectExisting();
					Assert.AreEqual(CollectionsStoreAvailability.ReadOnly, inspection.Availability);
					Assert.AreEqual(storeId, inspection.StoreId);
					Assert.IsTrue(inspection.CanReadFeatureState);
					Assert.IsFalse(inspection.CanWriteFeatureState);
					Assert.AreEqual(storeId, store.ReadStoreId());
					CollectionsStoreAccessException openException = Assert.Throws<CollectionsStoreAccessException>(() => store.OpenExisting());
					Assert.AreEqual(CollectionsStoreAccessFailureKind.ReadOnly, openException.FailureKind);

					var catalog = new CollectionsCatalogStore(store);
					var definition = new CollectionDefinition(CollectionIdentity.FromNexus("read-only-c4-9"), "Read only", null, null);
					CollectionsStoreAccessException exception = Assert.Throws<CollectionsStoreAccessException>(() => catalog.SaveDefinition(definition));
					Assert.AreEqual(CollectionsStoreAccessFailureKind.ReadOnly, exception.FailureKind);
					Assert.IsNull(catalog.GetDefinition(definition.Identity));
				}
				finally
				{
					File.SetAttributes(store.DatabasePath, originalAttributes);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void BusyWriter_FailsWithinConfiguredBoundAndDoesNotCommitFeatureState()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				var catalog = new CollectionsCatalogStore(store);
				var definition = new CollectionDefinition(CollectionIdentity.FromNexus("busy-c4-9"), "Busy", null, null);

				using (SQLiteConnection blocker = OpenDatabase(store.DatabasePath))
				{
					Execute(blocker, null, "BEGIN IMMEDIATE;");
					try
					{
						Stopwatch watch = Stopwatch.StartNew();
						CollectionsStoreAccessException exception = Assert.Throws<CollectionsStoreAccessException>(() => catalog.SaveDefinition(definition));
						watch.Stop();
						Assert.AreEqual(CollectionsStoreAccessFailureKind.Busy, exception.FailureKind);
						Assert.Less(watch.ElapsedMilliseconds, CollectionsStore.BusyTimeoutMilliseconds + 5000,
							"SQLite busy handling must remain bounded rather than spinning or waiting indefinitely.");
					}
					finally
					{
						Execute(blocker, null, "ROLLBACK;");
					}
				}

				Assert.IsNull(catalog.GetDefinition(definition.Identity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OpenExisting_RejectsMetadataAndUserVersionDisagreement()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Execute(connection, null, "UPDATE store_metadata SET value='1' WHERE key='schema_version';");
				}

				Assert.Throws<CollectionsStoreSchemaException>(() => store.OpenExisting());
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OpenExisting_RejectsAStoreMissingRequiredSchemaObjects()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Execute(connection, null, "DROP INDEX ix_member_bindings_native;");
				}

				CollectionsStoreInspection inspection = store.InspectExisting();
				Assert.AreEqual(CollectionsStoreAvailability.InvalidOrCorrupt, inspection.Availability);
				Assert.IsFalse(inspection.CanReadFeatureState);
				Assert.Throws<CollectionsStoreSchemaException>(() => store.OpenExisting());
			}
			finally
			{
				Directory.Delete(root, true);
			}
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

		private static bool TableExists(SQLiteConnection connection, string tableName)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
				command.Parameters.AddWithValue("@name", tableName);
				return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
			}
		}

		private static bool IndexExists(SQLiteConnection connection, string indexName)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name;";
				command.Parameters.AddWithValue("@name", indexName);
				return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
			}
		}

		private static int ForeignKeyCount(SQLiteConnection connection, string tableName)
		{
			int count = 0;
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "PRAGMA foreign_key_list([" + tableName + "]);";
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						count++;
				}
			}
			return count;
		}

		private static int ScalarInt(SQLiteConnection connection, string sql)
		{
			return Convert.ToInt32(Scalar(connection, sql), CultureInfo.InvariantCulture);
		}

		private static string ScalarString(SQLiteConnection connection, string sql)
		{
			return Convert.ToString(Scalar(connection, sql), CultureInfo.InvariantCulture);
		}

		private static object Scalar(SQLiteConnection connection, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = sql;
				return command.ExecuteScalar();
			}
		}

		private static void Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = sql;
				command.ExecuteNonQuery();
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-collections-store-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}
	}
}
