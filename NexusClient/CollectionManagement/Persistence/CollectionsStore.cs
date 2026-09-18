using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using Nexus.Client.GameStorage;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Owns the Collections feature SQLite store and its schema/durability boundary.
	/// </summary>
	/// <remarks>
	/// This store contains Collections feature state only. Native InstallLog, VirtualModConfig, replay artifacts and the
	/// shared FOMOD metadata database remain authoritative for live native state. Construction never creates a database;
	/// callers must explicitly create a new store or open an existing one so a missing store is not mistaken for "no owners".
	/// </remarks>
	public sealed class CollectionsStore
	{
		public const int CurrentSchemaVersion = 1;
		public const int BusyTimeoutMilliseconds = 5000;
		private const string SchemaName = "nmm-ce-collections";
		private const string SchemaVersionMetadataKey = "schema_version";
		private const string StoreIdMetadataKey = "store_id";
		private const string SchemaNameMetadataKey = "schema_name";
		private const string CreatedUtcMetadataKey = "created_utc";

		private static readonly object s_writerGatesLock = new object();
		private static readonly Dictionary<string, object> s_writerGates =
			new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

		private readonly object _writerGate;

		public CollectionsStore(GameStoragePathSet paths)
			: this(CollectionsStorePaths.GetStoreDirectory(paths), CollectionsStorePaths.GetDatabasePath(paths),
				CollectionsStorePaths.GetRetainedContentDirectory(paths))
		{
		}

		public CollectionsStore(string installInfoDirectory)
			: this(CollectionsStorePaths.GetStoreDirectory(installInfoDirectory),
				CollectionsStorePaths.GetDatabasePath(installInfoDirectory),
				CollectionsStorePaths.GetRetainedContentDirectory(installInfoDirectory))
		{
		}

		private CollectionsStore(string storeDirectory, string databasePath, string retainedContentDirectory)
		{
			StoreDirectory = storeDirectory;
			DatabasePath = databasePath;
			RetainedContentDirectory = retainedContentDirectory;
			_writerGate = GetWriterGate(databasePath);
		}

		public string StoreDirectory { get; }
		public string DatabasePath { get; }
		public string RetainedContentDirectory { get; }
		public bool Exists { get { return File.Exists(DatabasePath); } }

		/// <summary>
		/// Creates a brand-new store. This operation never overwrites or reinitializes an existing database.
		/// </summary>
		public Guid CreateNew()
		{
			lock (_writerGate)
			{
				Directory.CreateDirectory(StoreDirectory);
				if (File.Exists(DatabasePath))
					throw new IOException("A Collections feature store already exists at: " + DatabasePath);

				Guid storeId = Guid.NewGuid();
				bool claimedFile = false;
				try
				{
					using (FileStream stream = new FileStream(DatabasePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
					{
						claimedFile = true;
					}

					using (SQLiteConnection connection = OpenConnection(false, true))
					{
						ConfigureWritableConnection(connection);
						CreateSchema(connection, storeId);
						ValidateCurrentSchema(connection);
					}

					return storeId;
				}
				catch
				{
					if (claimedFile)
						CleanupFailedCreation();
					throw;
				}
			}
		}

		/// <summary>
		/// Opens and validates an existing store, applying only known forward migrations under the writer gate.
		/// </summary>
		/// <returns>The durable store identity and effective SQLite settings observed on the writable connection.</returns>
		public CollectionsStoreStatus OpenExisting()
		{
			lock (_writerGate)
			{
				int observedVersion = PreflightExistingStore();
				if (observedVersion > CurrentSchemaVersion)
					throw NewerSchemaException(observedVersion);

				using (SQLiteConnection connection = OpenConnection(false, true))
				{
					ConfigureWritableConnection(connection);
					int version = ReadAndValidateVersion(connection);
					if (version > CurrentSchemaVersion)
						throw NewerSchemaException(version);
					if (version < CurrentSchemaVersion)
						Migrate(connection, version);

					ValidateCurrentSchema(connection);
					return ReadEffectiveStatus(connection);
				}
			}
		}

		/// <summary>
		/// Reads the durable store identity without creating or migrating a missing/incompatible database.
		/// </summary>
		public Guid ReadStoreId()
		{
			if (!File.Exists(DatabasePath))
				throw new FileNotFoundException("The Collections feature store is missing.", DatabasePath);

			using (SQLiteConnection connection = OpenConnection(true, true))
			{
				ConfigureReadConnection(connection);
				ValidateCurrentSchema(connection);
				return ReadStoreId(connection);
			}
		}

		/// <summary>
		/// Executes one short Collections feature write transaction under the process-wide store writer gate.
		/// </summary>
		internal void ExecuteWrite(Action<SQLiteConnection, SQLiteTransaction> writeAction)
		{
			if (writeAction == null)
				throw new ArgumentNullException(nameof(writeAction));

			lock (_writerGate)
			{
				int observedVersion = PreflightExistingStore();
				if (observedVersion > CurrentSchemaVersion)
					throw NewerSchemaException(observedVersion);
				if (observedVersion < CurrentSchemaVersion)
					throw new CollectionsStoreSchemaException("Collections store must be migrated before feature writes are allowed.");

				using (SQLiteConnection connection = OpenConnection(false, true))
				{
					ConfigureWritableConnection(connection);
					ValidateCurrentSchema(connection);
					using (SQLiteTransaction transaction = connection.BeginTransaction())
					{
						writeAction(connection, transaction);
						transaction.Commit();
					}
				}
			}
		}

		/// <summary>
		/// Executes a bounded read against an existing store without acquiring the writer gate.
		/// </summary>
		internal T ExecuteRead<T>(Func<SQLiteConnection, SQLiteTransaction, T> readAction)
		{
			if (readAction == null)
				throw new ArgumentNullException(nameof(readAction));
			if (!File.Exists(DatabasePath))
				throw new FileNotFoundException("The Collections feature store is missing.", DatabasePath);

			using (SQLiteConnection connection = OpenConnection(true, true))
			{
				ConfigureReadConnection(connection);
				ValidateCurrentSchema(connection);
				using (SQLiteTransaction transaction = connection.BeginTransaction())
				{
					T result = readAction(connection, transaction);
					transaction.Commit();
					return result;
				}
			}
		}

		private int PreflightExistingStore()
		{
			if (!File.Exists(DatabasePath))
				throw new FileNotFoundException("The Collections feature store is missing.", DatabasePath);

			using (SQLiteConnection connection = OpenConnection(true, true))
			{
				ConfigureReadConnection(connection);
				int version = ReadAndValidateVersion(connection);
				if (version == CurrentSchemaVersion)
					ValidateCurrentSchema(connection);
				return version;
			}
		}

		private static CollectionsStoreSchemaException NewerSchemaException(int version)
		{
			return new CollectionsStoreSchemaException(string.Format(CultureInfo.InvariantCulture,
				"Collections store schema {0} is newer than supported schema {1}; refusing to downgrade.",
				version, CurrentSchemaVersion));
		}

		private static object GetWriterGate(string databasePath)
		{
			string key = Path.GetFullPath(databasePath);
			lock (s_writerGatesLock)
			{
				object gate;
				if (!s_writerGates.TryGetValue(key, out gate))
				{
					gate = new object();
					s_writerGates.Add(key, gate);
				}
				return gate;
			}
		}

		private SQLiteConnection OpenConnection(bool readOnly, bool failIfMissing)
		{
			SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder
			{
				DataSource = DatabasePath,
				ForeignKeys = true,
				Pooling = false,
				ReadOnly = readOnly,
				FailIfMissing = failIfMissing
			};

			SQLiteConnection connection = new SQLiteConnection(builder.ConnectionString);
			connection.Open();
			return connection;
		}

		private static void ConfigureWritableConnection(SQLiteConnection connection)
		{
			ExecuteNonQuery(connection, null, "PRAGMA foreign_keys=ON;");
			ExecuteNonQuery(connection, null, "PRAGMA busy_timeout=" + BusyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture) + ";");
			string journalMode = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode=DELETE;"), CultureInfo.InvariantCulture);
			ExecuteNonQuery(connection, null, "PRAGMA synchronous=FULL;");

			if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
				throw new CollectionsStoreSchemaException("Collections store could not establish DELETE rollback journaling.");
			ValidateConnectionSettings(connection, true);
		}

		private static void ConfigureReadConnection(SQLiteConnection connection)
		{
			ExecuteNonQuery(connection, null, "PRAGMA foreign_keys=ON;");
			ExecuteNonQuery(connection, null, "PRAGMA busy_timeout=" + BusyTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture) + ";");
			ValidateConnectionSettings(connection, false);
		}

		private static void ValidateConnectionSettings(SQLiteConnection connection, bool requireDurableWriteSettings)
		{
			if (Convert.ToInt32(ExecuteScalar(connection, "PRAGMA foreign_keys;"), CultureInfo.InvariantCulture) != 1)
				throw new CollectionsStoreSchemaException("Collections store requires SQLite foreign key enforcement.");

			int busyTimeout = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA busy_timeout;"), CultureInfo.InvariantCulture);
			if (busyTimeout != BusyTimeoutMilliseconds)
				throw new CollectionsStoreSchemaException("Collections store could not establish the configured bounded SQLite busy timeout.");

			if (!requireDurableWriteSettings)
				return;

			string journalMode = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode;"), CultureInfo.InvariantCulture);
			if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
				throw new CollectionsStoreSchemaException("Collections store requires DELETE rollback journaling.");
			if (Convert.ToInt32(ExecuteScalar(connection, "PRAGMA synchronous;"), CultureInfo.InvariantCulture) != 2)
				throw new CollectionsStoreSchemaException("Collections store requires SQLite synchronous=FULL for durable intent commits.");
		}

		private static void CreateSchema(SQLiteConnection connection, Guid storeId)
		{
			using (SQLiteTransaction transaction = connection.BeginTransaction())
			{
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE store_metadata (
	key TEXT NOT NULL PRIMARY KEY,
	value TEXT NOT NULL
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE collections (
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	display_name TEXT NULL,
	author_display_name TEXT NULL,
	summary TEXT NULL,
	PRIMARY KEY (origin, collection_id),
	CHECK (origin > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE collection_revisions (
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	revision_id TEXT NOT NULL,
	nexus_revision_number INTEGER NULL,
	revision_label TEXT NULL,
	notes TEXT NULL,
	declared_member_count INTEGER NULL,
	PRIMARY KEY (origin, collection_id, revision_id),
	FOREIGN KEY (origin, collection_id) REFERENCES collections(origin, collection_id) ON DELETE CASCADE,
	UNIQUE (origin, collection_id, nexus_revision_number),
	CHECK (nexus_revision_number IS NULL OR nexus_revision_number > 0),
	CHECK (declared_member_count IS NULL OR declared_member_count >= 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE retained_artifacts (
	artifact_id TEXT NOT NULL PRIMARY KEY,
	hash_algorithm TEXT NOT NULL,
	hash_value TEXT NOT NULL,
	byte_length INTEGER NOT NULL,
	relative_path TEXT NULL,
	sealed INTEGER NOT NULL DEFAULT 0,
	CHECK (byte_length >= 0),
	CHECK (sealed IN (0, 1)),
	UNIQUE (hash_algorithm, hash_value, byte_length)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE revision_sources (
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	revision_id TEXT NOT NULL,
	source_input_kind INTEGER NOT NULL,
	bundle_hash_algorithm TEXT NOT NULL,
	bundle_hash_value TEXT NOT NULL,
	bundle_byte_length INTEGER NOT NULL,
	manifest_entry_name TEXT NOT NULL,
	manifest_hash_algorithm TEXT NOT NULL,
	manifest_hash_value TEXT NOT NULL,
	manifest_byte_length INTEGER NOT NULL,
	schema_identity TEXT NOT NULL,
	normalizer_version TEXT NOT NULL,
	raw_bundle_artifact_id TEXT NULL,
	raw_manifest_artifact_id TEXT NULL,
	PRIMARY KEY (origin, collection_id, revision_id),
	FOREIGN KEY (origin, collection_id, revision_id) REFERENCES collection_revisions(origin, collection_id, revision_id) ON DELETE CASCADE,
	FOREIGN KEY (raw_bundle_artifact_id) REFERENCES retained_artifacts(artifact_id) ON DELETE RESTRICT,
	FOREIGN KEY (raw_manifest_artifact_id) REFERENCES retained_artifacts(artifact_id) ON DELETE RESTRICT,
	CHECK (source_input_kind > 0),
	CHECK (bundle_byte_length >= 0),
	CHECK (manifest_byte_length >= 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE resolved_plans (
	plan_id TEXT NOT NULL,
	plan_version INTEGER NOT NULL,
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	revision_id TEXT NOT NULL,
	target_fingerprint TEXT NOT NULL,
	policy_kind INTEGER NOT NULL,
	current_state_format_version TEXT NOT NULL,
	current_state_fingerprint TEXT NOT NULL,
	payload_format TEXT NOT NULL,
	payload BLOB NOT NULL,
	PRIMARY KEY (plan_id, plan_version),
	FOREIGN KEY (origin, collection_id, revision_id) REFERENCES collection_revisions(origin, collection_id, revision_id) ON DELETE RESTRICT,
	CHECK (plan_version > 0),
	CHECK (policy_kind > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE target_associations (
	association_id TEXT NOT NULL PRIMARY KEY,
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	revision_id TEXT NOT NULL,
	target_fingerprint TEXT NOT NULL,
	state INTEGER NOT NULL,
	FOREIGN KEY (origin, collection_id, revision_id) REFERENCES collection_revisions(origin, collection_id, revision_id) ON DELETE RESTRICT,
	CHECK (state > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE member_bindings (
	association_id TEXT NOT NULL,
	member_key_kind INTEGER NOT NULL,
	member_key_value TEXT NOT NULL,
	native_target_fingerprint TEXT NOT NULL,
	native_mod_key TEXT NOT NULL,
	verified_recipe_fingerprint TEXT NOT NULL,
	binding_kind INTEGER NOT NULL,
	PRIMARY KEY (association_id, member_key_kind, member_key_value),
	FOREIGN KEY (association_id) REFERENCES target_associations(association_id) ON DELETE CASCADE,
	CHECK (member_key_kind > 0),
	CHECK (binding_kind > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE user_overrides (
	override_id TEXT NOT NULL PRIMARY KEY,
	association_id TEXT NOT NULL,
	baseline_revision_id TEXT NOT NULL,
	target_fingerprint TEXT NOT NULL,
	member_key_kind INTEGER NULL,
	member_key_value TEXT NULL,
	aspect INTEGER NOT NULL,
	subject_key TEXT NULL,
	baseline_state_kind INTEGER NOT NULL,
	baseline_state_format_version TEXT NULL,
	baseline_state_fingerprint TEXT NULL,
	chosen_state_kind INTEGER NOT NULL,
	chosen_state_format_version TEXT NULL,
	chosen_state_fingerprint TEXT NULL,
	note TEXT NULL,
	FOREIGN KEY (association_id) REFERENCES target_associations(association_id) ON DELETE CASCADE,
	CHECK (aspect > 0),
	CHECK (baseline_state_kind > 0),
	CHECK (chosen_state_kind > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE drift_observations (
	observation_id TEXT NOT NULL PRIMARY KEY,
	association_id TEXT NOT NULL,
	baseline_revision_id TEXT NOT NULL,
	target_fingerprint TEXT NOT NULL,
	member_key_kind INTEGER NULL,
	member_key_value TEXT NULL,
	aspect INTEGER NOT NULL,
	subject_key TEXT NULL,
	expected_state_kind INTEGER NOT NULL,
	expected_state_format_version TEXT NULL,
	expected_state_fingerprint TEXT NULL,
	observed_state_kind INTEGER NOT NULL,
	observed_state_format_version TEXT NULL,
	observed_state_fingerprint TEXT NULL,
	detail TEXT NULL,
	FOREIGN KEY (association_id) REFERENCES target_associations(association_id) ON DELETE CASCADE,
	CHECK (aspect > 0),
	CHECK (expected_state_kind > 0),
	CHECK (observed_state_kind > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE local_captures (
	capture_id TEXT NOT NULL PRIMARY KEY,
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	revision_id TEXT NOT NULL,
	source_target_fingerprint TEXT NOT NULL,
	state_fingerprint_format_version TEXT NOT NULL,
	state_fingerprint TEXT NOT NULL,
	scope_version INTEGER NOT NULL,
	capability INTEGER NOT NULL,
	FOREIGN KEY (origin, collection_id, revision_id) REFERENCES collection_revisions(origin, collection_id, revision_id) ON DELETE RESTRICT,
	CHECK (scope_version > 0),
	CHECK (capability > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE local_capture_scope_areas (
	capture_id TEXT NOT NULL,
	area INTEGER NOT NULL,
	PRIMARY KEY (capture_id, area),
	FOREIGN KEY (capture_id) REFERENCES local_captures(capture_id) ON DELETE CASCADE,
	CHECK (area > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE local_capture_exclusions (
	capture_id TEXT NOT NULL,
	area INTEGER NOT NULL,
	code TEXT NOT NULL,
	reason TEXT NOT NULL,
	PRIMARY KEY (capture_id, area, code),
	FOREIGN KEY (capture_id) REFERENCES local_captures(capture_id) ON DELETE CASCADE,
	CHECK (area > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE local_capture_native_mappings (
	capture_id TEXT NOT NULL,
	member_key_kind INTEGER NOT NULL,
	member_key_value TEXT NOT NULL,
	source_target_fingerprint TEXT NOT NULL,
	native_mod_key TEXT NOT NULL,
	PRIMARY KEY (capture_id, member_key_kind, member_key_value),
	FOREIGN KEY (capture_id) REFERENCES local_captures(capture_id) ON DELETE CASCADE,
	CHECK (member_key_kind > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE retained_artifact_references (
	reference_id TEXT NOT NULL PRIMARY KEY,
	artifact_id TEXT NOT NULL,
	owner_kind INTEGER NOT NULL,
	owner_id TEXT NOT NULL,
	role TEXT NOT NULL,
	FOREIGN KEY (artifact_id) REFERENCES retained_artifacts(artifact_id) ON DELETE RESTRICT,
	UNIQUE (owner_kind, owner_id, artifact_id, role),
	CHECK (owner_kind > 0)
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE collection_operations (
	operation_id TEXT NOT NULL PRIMARY KEY,
	kind INTEGER NOT NULL,
	origin INTEGER NOT NULL,
	collection_id TEXT NOT NULL,
	target_fingerprint TEXT NOT NULL,
	revision_id TEXT NULL,
	plan_id TEXT NULL,
	plan_version INTEGER NULL,
	checkpoint_sequence INTEGER NOT NULL,
	phase INTEGER NOT NULL,
	result_state INTEGER NOT NULL,
	FOREIGN KEY (origin, collection_id) REFERENCES collections(origin, collection_id) ON DELETE RESTRICT,
	FOREIGN KEY (origin, collection_id, revision_id) REFERENCES collection_revisions(origin, collection_id, revision_id) ON DELETE RESTRICT,
	FOREIGN KEY (plan_id, plan_version) REFERENCES resolved_plans(plan_id, plan_version) ON DELETE RESTRICT,
	CHECK (kind > 0),
	CHECK (checkpoint_sequence >= 0),
	CHECK (phase > 0),
	CHECK (result_state >= 0),
	CHECK ((plan_id IS NULL AND plan_version IS NULL) OR (plan_id IS NOT NULL AND plan_version IS NOT NULL))
);");
				ExecuteSchemaStatement(connection, transaction, @"
CREATE TABLE native_operation_children (
	collection_operation_id TEXT NOT NULL,
	sequence INTEGER NOT NULL,
	member_origin INTEGER NOT NULL,
	member_collection_id TEXT NOT NULL,
	member_revision_id TEXT NOT NULL,
	member_key_kind INTEGER NOT NULL,
	member_key_value TEXT NOT NULL,
	action INTEGER NOT NULL,
	native_operation_id TEXT NOT NULL,
	native_attempt_id TEXT NOT NULL,
	native_origin INTEGER NOT NULL,
	native_target_fingerprint TEXT NOT NULL,
	native_context_fingerprint TEXT NOT NULL,
	native_recipe_fingerprint TEXT NOT NULL,
	checkpoint INTEGER NOT NULL,
	reported_status INTEGER NULL,
	durability INTEGER NULL,
	PRIMARY KEY (collection_operation_id, sequence),
	FOREIGN KEY (collection_operation_id) REFERENCES collection_operations(operation_id) ON DELETE CASCADE,
	UNIQUE (collection_operation_id, native_operation_id),
	CHECK (sequence > 0),
	CHECK (member_key_kind > 0),
	CHECK (action > 0),
	CHECK (native_origin > 0),
	CHECK (checkpoint > 0)
);");

				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_collection_revisions_collection ON collection_revisions(origin, collection_id);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_resolved_plans_revision ON resolved_plans(origin, collection_id, revision_id);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_target_associations_target ON target_associations(target_fingerprint, state);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_member_bindings_native ON member_bindings(native_target_fingerprint, native_mod_key);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_user_overrides_association ON user_overrides(association_id);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_drift_observations_association ON drift_observations(association_id);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_local_captures_revision ON local_captures(origin, collection_id, revision_id);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_retained_artifact_references_artifact ON retained_artifact_references(artifact_id);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_collection_operations_pending ON collection_operations(phase, result_state, target_fingerprint);");
				ExecuteSchemaStatement(connection, transaction, "CREATE INDEX ix_native_operation_children_native ON native_operation_children(native_operation_id, native_attempt_id);");

				SetMetadata(connection, transaction, SchemaNameMetadataKey, SchemaName);
				SetMetadata(connection, transaction, SchemaVersionMetadataKey, CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture));
				SetMetadata(connection, transaction, StoreIdMetadataKey, storeId.ToString("D"));
				SetMetadata(connection, transaction, CreatedUtcMetadataKey, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
				ExecuteNonQuery(connection, transaction, "PRAGMA user_version=" + CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture) + ";");
				transaction.Commit();
			}
		}

		private static void Migrate(SQLiteConnection connection, int version)
		{
			throw new CollectionsStoreSchemaException(string.Format(CultureInfo.InvariantCulture,
				"Collections store schema {0} has no supported migration path to schema {1}.", version, CurrentSchemaVersion));
		}

		private static int ReadAndValidateVersion(SQLiteConnection connection)
		{
			RequireTable(connection, "store_metadata", "key", "value");

			string schemaName = GetMetadata(connection, SchemaNameMetadataKey);
			if (!string.Equals(schemaName, SchemaName, StringComparison.Ordinal))
				throw new CollectionsStoreSchemaException("The database is not a recognized NMM Collections feature store.");

			string rawVersion = GetMetadata(connection, SchemaVersionMetadataKey);
			int metadataVersion;
			if (!Int32.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture, out metadataVersion) || metadataVersion <= 0)
				throw new CollectionsStoreSchemaException("Collections store has an invalid schema version.");

			int pragmaVersion = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
			if (pragmaVersion != metadataVersion)
				throw new CollectionsStoreSchemaException("Collections store schema metadata disagrees with SQLite user_version.");

			return metadataVersion;
		}

		private static void ValidateCurrentSchema(SQLiteConnection connection)
		{
			int version = ReadAndValidateVersion(connection);
			if (version != CurrentSchemaVersion)
				throw new CollectionsStoreSchemaException(string.Format(CultureInfo.InvariantCulture,
					"Collections store schema {0} is not the supported schema {1}.", version, CurrentSchemaVersion));

			RequireTable(connection, "collections", "origin", "collection_id", "display_name", "author_display_name", "summary");
			RequireTable(connection, "collection_revisions", "origin", "collection_id", "revision_id", "nexus_revision_number", "declared_member_count");
			RequireTable(connection, "revision_sources", "origin", "collection_id", "revision_id", "source_input_kind", "bundle_hash_algorithm", "bundle_hash_value", "bundle_byte_length", "manifest_entry_name", "manifest_hash_algorithm", "manifest_hash_value", "manifest_byte_length", "schema_identity", "normalizer_version", "raw_bundle_artifact_id", "raw_manifest_artifact_id");
			RequireTable(connection, "resolved_plans", "plan_id", "plan_version", "origin", "collection_id", "revision_id", "target_fingerprint", "payload_format", "payload");
			RequireTable(connection, "target_associations", "association_id", "origin", "collection_id", "revision_id", "target_fingerprint", "state");
			RequireTable(connection, "member_bindings", "association_id", "member_key_kind", "member_key_value", "native_target_fingerprint", "native_mod_key", "verified_recipe_fingerprint", "binding_kind");
			RequireTable(connection, "user_overrides", "override_id", "association_id", "aspect", "baseline_state_kind", "chosen_state_kind");
			RequireTable(connection, "drift_observations", "observation_id", "association_id", "aspect", "expected_state_kind", "observed_state_kind");
			RequireTable(connection, "local_captures", "capture_id", "origin", "collection_id", "revision_id", "source_target_fingerprint", "scope_version", "capability");
			RequireTable(connection, "local_capture_scope_areas", "capture_id", "area");
			RequireTable(connection, "local_capture_exclusions", "capture_id", "area", "code", "reason");
			RequireTable(connection, "local_capture_native_mappings", "capture_id", "member_key_kind", "member_key_value", "source_target_fingerprint", "native_mod_key");
			RequireTable(connection, "retained_artifacts", "artifact_id", "hash_algorithm", "hash_value", "byte_length", "relative_path", "sealed");
			RequireTable(connection, "retained_artifact_references", "reference_id", "artifact_id", "owner_kind", "owner_id", "role");
			RequireTable(connection, "collection_operations", "operation_id", "kind", "origin", "collection_id", "target_fingerprint", "checkpoint_sequence", "phase", "result_state");
			RequireTable(connection, "native_operation_children", "collection_operation_id", "sequence", "native_operation_id", "native_attempt_id", "checkpoint", "reported_status", "durability");

			RequireIndex(connection, "ix_collection_revisions_collection");
			RequireIndex(connection, "ix_resolved_plans_revision");
			RequireIndex(connection, "ix_target_associations_target");
			RequireIndex(connection, "ix_member_bindings_native");
			RequireIndex(connection, "ix_user_overrides_association");
			RequireIndex(connection, "ix_drift_observations_association");
			RequireIndex(connection, "ix_local_captures_revision");
			RequireIndex(connection, "ix_retained_artifact_references_artifact");
			RequireIndex(connection, "ix_collection_operations_pending");
			RequireIndex(connection, "ix_native_operation_children_native");
		}

		private static CollectionsStoreStatus ReadEffectiveStatus(SQLiteConnection connection)
		{
			return new CollectionsStoreStatus(
				ReadStoreId(connection),
				ReadAndValidateVersion(connection),
				Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode;"), CultureInfo.InvariantCulture),
				Convert.ToInt32(ExecuteScalar(connection, "PRAGMA synchronous;"), CultureInfo.InvariantCulture),
				Convert.ToInt32(ExecuteScalar(connection, "PRAGMA foreign_keys;"), CultureInfo.InvariantCulture) == 1,
				Convert.ToInt32(ExecuteScalar(connection, "PRAGMA busy_timeout;"), CultureInfo.InvariantCulture));
		}

		private static Guid ReadStoreId(SQLiteConnection connection)
		{
			Guid storeId;
			if (!Guid.TryParse(GetMetadata(connection, StoreIdMetadataKey), out storeId) || storeId == Guid.Empty)
				throw new CollectionsStoreSchemaException("Collections store has an invalid durable store identity.");
			return storeId;
		}

		private static void RequireTable(SQLiteConnection connection, string tableName, params string[] requiredColumns)
		{
			if (!HasTable(connection, tableName))
				throw new CollectionsStoreSchemaException("Collections store is missing required table: " + tableName);

			HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "PRAGMA table_info([" + tableName.Replace("]", "]]") + "]);";
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						columns.Add(reader.GetString(1));
				}
			}

			foreach (string requiredColumn in requiredColumns)
			{
				if (!columns.Contains(requiredColumn))
					throw new CollectionsStoreSchemaException("Collections store table " + tableName + " is missing required column: " + requiredColumn);
			}
		}

		private static bool HasTable(SQLiteConnection connection, string tableName)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name;";
				command.Parameters.AddWithValue("@name", tableName);
				return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
			}
		}

		private static void RequireIndex(SQLiteConnection connection, string indexName)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name;";
				command.Parameters.AddWithValue("@name", indexName);
				if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
					throw new CollectionsStoreSchemaException("Collections store is missing required index: " + indexName);
			}
		}

		private static object ExecuteScalar(SQLiteConnection connection, string commandText)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = commandText;
				return command.ExecuteScalar();
			}
		}

		private static void ExecuteSchemaStatement(SQLiteConnection connection, SQLiteTransaction transaction, string commandText)
		{
			ExecuteNonQuery(connection, transaction, commandText);
		}

		private static void ExecuteNonQuery(SQLiteConnection connection, SQLiteTransaction transaction, string commandText)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = commandText;
				command.ExecuteNonQuery();
			}
		}

		private static string GetMetadata(SQLiteConnection connection, string key)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT value FROM store_metadata WHERE key=@key;";
				command.Parameters.AddWithValue("@key", key);
				object value = command.ExecuteScalar();
				return value == null || value == DBNull.Value ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
			}
		}

		private static void SetMetadata(SQLiteConnection connection, SQLiteTransaction transaction, string key, string value)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "INSERT INTO store_metadata(key, value) VALUES(@key, @value);";
				command.Parameters.AddWithValue("@key", key);
				command.Parameters.AddWithValue("@value", value);
				command.ExecuteNonQuery();
			}
		}

		private void CleanupFailedCreation()
		{
			TryDelete(DatabasePath + "-journal");
			TryDelete(DatabasePath + "-wal");
			TryDelete(DatabasePath + "-shm");
			TryDelete(DatabasePath);
		}

		private static void TryDelete(string path)
		{
			try
			{
				if (File.Exists(path))
					File.Delete(path);
			}
			catch
			{
			}
		}
	}
}
