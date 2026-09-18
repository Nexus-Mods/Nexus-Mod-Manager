using System;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.GameStorage;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C4.4 feature-store/schema foundation coverage.
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
						"ix_resolved_plans_revision",
						"ix_target_associations_target",
						"ix_member_bindings_native",
						"ix_user_overrides_association",
						"ix_drift_observations_association",
						"ix_local_captures_revision",
						"ix_retained_artifact_references_artifact",
						"ix_collection_operations_pending",
						"ix_native_operation_children_native"
					};

					foreach (string index in indexes)
						Assert.IsTrue(IndexExists(connection, index), "Missing expected Collections index: " + index);

					Assert.Greater(ForeignKeyCount(connection, "member_bindings"), 0);
					Assert.Greater(ForeignKeyCount(connection, "native_operation_children"), 0);
					Assert.Greater(ForeignKeyCount(connection, "revision_sources"), 0);
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

				Assert.Throws<CollectionsStoreSchemaException>(() => store.OpenExisting());

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
		public void OpenExisting_RejectsMetadataAndUserVersionDisagreement()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();

				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Execute(connection, null, "UPDATE store_metadata SET value='2' WHERE key='schema_version';");
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
