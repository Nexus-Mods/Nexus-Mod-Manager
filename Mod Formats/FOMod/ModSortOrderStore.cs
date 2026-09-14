namespace Nexus.Client.Mods.Formats.FOMod
{
	using System;
	using System.Collections.Generic;
	using System.Data.SQLite;
	using System.IO;

	/// <summary>
	/// Describes how a persisted mod Sort assignment was established.
	/// </summary>
	public enum ModSortOrderAssignmentState
	{
		BaselineBlank = 0,
		PendingAddIdentity = 1,
		ExplicitBlank = 2,
		InheritedNumeric = 3,
		ExplicitNumeric = 4
	}

	/// <summary>
	/// Represents one durable Sort assignment row.
	/// </summary>
	public sealed class ModSortOrderRecord
	{
		/// <summary>
		/// Initializes a Sort assignment record.
		/// </summary>
		public ModSortOrderRecord(long assignmentId, string archivePath, string modId, string downloadId, int? sortNumber, ModSortOrderAssignmentState assignmentState, DateTime updatedUtc)
		{
			AssignmentId = assignmentId;
			ArchivePath = archivePath;
			ModId = modId;
			DownloadId = downloadId;
			SortNumber = sortNumber;
			AssignmentState = assignmentState;
			UpdatedUtc = updatedUtc;
		}

		public long AssignmentId { get; }
		public string ArchivePath { get; }
		public string ModId { get; }
		public string DownloadId { get; }
		public int? SortNumber { get; }
		public ModSortOrderAssignmentState AssignmentState { get; }
		public DateTime UpdatedUtc { get; }
	}

	/// <summary>
	/// Persists durable mod Sort assignments in the shared FOMod metadata SQLite database.
	/// </summary>
	public sealed class ModSortOrderStore
	{
		private const string TableName = "mod_sort_assignments";
		private readonly string _modDirectory;
		private readonly FOModArchiveMetadataCache.SharedDatabase _database;
		private readonly Exception _initializationException;

		/// <summary>
		/// Initializes the Sort store for one configured game storage.
		/// </summary>
		public ModSortOrderStore(string cacheDirectory, string modDirectory)
		{
			if (string.IsNullOrWhiteSpace(modDirectory))
			{
				throw new ArgumentException("A mod directory is required.", nameof(modDirectory));
			}

			_modDirectory = NormalizeDirectoryPath(modDirectory);

			try
			{
				_database = FOModArchiveMetadataCache.GetSharedDatabase(cacheDirectory);
				EnsureSchema();
			}
			catch (Exception e)
			{
				_initializationException = e;
			}
		}

		/// <summary>
		/// Gets whether the supplemental Sort schema is available and compatible.
		/// </summary>
		public bool IsUsable => _database != null && _initializationException == null;

		/// <summary>
		/// Converts an archive path into the stable, case-insensitive locator stored by this game storage.
		/// </summary>
		public string GetArchiveLocator(string archivePath)
		{
			if (string.IsNullOrWhiteSpace(archivePath))
			{
				throw new ArgumentException("An archive path is required.", nameof(archivePath));
			}

			var fullPath = Path.IsPathRooted(archivePath)
				? NormalizeFilePath(archivePath)
				: NormalizeFilePath(Path.Combine(_modDirectory, archivePath));
			var modRoot = _modDirectory + Path.DirectorySeparatorChar;

			if (fullPath.StartsWith(modRoot, StringComparison.OrdinalIgnoreCase))
			{
				return fullPath.Substring(modRoot.Length).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToUpperInvariant();
			}

			return fullPath.ToUpperInvariant();
		}

		/// <summary>
		/// Loads every historical and current Sort assignment from the active storage.
		/// </summary>
		public IReadOnlyList<ModSortOrderRecord> LoadAll()
		{
			EnsureAvailable();

			lock (_database.SyncRoot)
			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = @"
SELECT assignment_id, archive_path, mod_id, download_id, sort_number, assignment_state, updated_utc
FROM mod_sort_assignments
ORDER BY assignment_id;";
				return ReadRecords(command);
			}
		}

		/// <summary>
		/// Loads assignments previously bound to the specified archive locator.
		/// </summary>
		public IReadOnlyList<ModSortOrderRecord> FindByArchivePath(string archivePath)
		{
			EnsureAvailable();
			var locator = GetArchiveLocator(archivePath);

			lock (_database.SyncRoot)
			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = @"
SELECT assignment_id, archive_path, mod_id, download_id, sort_number, assignment_state, updated_utc
FROM mod_sort_assignments
WHERE archive_path = @archive_path
ORDER BY assignment_id;";
				command.Parameters.AddWithValue("@archive_path", locator);
				return ReadRecords(command);
			}
		}

		/// <summary>
		/// Loads assignments matching an exact repository file identity.
		/// </summary>
		public IReadOnlyList<ModSortOrderRecord> FindByRepositoryFile(string modId, string downloadId)
		{
			EnsureAvailable();
			var normalizedModId = NormalizeRepositoryId(modId);
			var normalizedDownloadId = NormalizeRepositoryId(downloadId);

			if (normalizedModId == null || normalizedDownloadId == null)
			{
				return new ModSortOrderRecord[0];
			}

			lock (_database.SyncRoot)
			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = @"
SELECT assignment_id, archive_path, mod_id, download_id, sort_number, assignment_state, updated_utc
FROM mod_sort_assignments
WHERE mod_id = @mod_id AND download_id = @download_id
ORDER BY assignment_id;";
				command.Parameters.AddWithValue("@mod_id", normalizedModId);
				command.Parameters.AddWithValue("@download_id", normalizedDownloadId);
				return ReadRecords(command);
			}
		}

		/// <summary>
		/// Inserts or updates an assignment and durably commits it before returning.
		/// </summary>
		public ModSortOrderRecord Save(ModSortOrderRecord record)
		{
			return Save(record, null);
		}

		/// <summary>
		/// Inserts or updates an assignment and optionally removes a transient obsolete binding in the same durable commit.
		/// </summary>
		public ModSortOrderRecord Save(ModSortOrderRecord record, long? obsoleteAssignmentId)
		{
			if (record == null)
			{
				throw new ArgumentNullException(nameof(record));
			}

			EnsureAvailable();
			ValidateState(record.AssignmentState);
			var locator = GetArchiveLocator(record.ArchivePath);
			var modId = NormalizeRepositoryId(record.ModId);
			var downloadId = NormalizeRepositoryId(record.DownloadId);
			var updatedUtc = DateTime.UtcNow;
			long assignmentId = record.AssignmentId;

			_database.ExecuteDurableWrite((connection, transaction) =>
			{
				if (assignmentId > 0)
				{
					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
UPDATE mod_sort_assignments
SET archive_path = @archive_path,
    mod_id = @mod_id,
    download_id = @download_id,
    sort_number = @sort_number,
    assignment_state = @assignment_state,
    updated_utc = @updated_utc
WHERE assignment_id = @assignment_id;";
						AddAssignmentParameters(command, locator, modId, downloadId, record.SortNumber, record.AssignmentState, updatedUtc);
						command.Parameters.AddWithValue("@assignment_id", assignmentId);
						if (command.ExecuteNonQuery() != 1)
						{
							throw new InvalidOperationException("The mod Sort assignment no longer exists.");
						}
					}
				}
				else
				{
					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
INSERT INTO mod_sort_assignments
    (archive_path, mod_id, download_id, sort_number, assignment_state, updated_utc)
VALUES
    (@archive_path, @mod_id, @download_id, @sort_number, @assignment_state, @updated_utc);";
						AddAssignmentParameters(command, locator, modId, downloadId, record.SortNumber, record.AssignmentState, updatedUtc);
						command.ExecuteNonQuery();
					}

					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = "SELECT last_insert_rowid();";
						assignmentId = Convert.ToInt64(command.ExecuteScalar());
					}
				}

				if (obsoleteAssignmentId.HasValue && obsoleteAssignmentId.Value > 0 && obsoleteAssignmentId.Value != assignmentId)
				{
					using (var command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = "DELETE FROM mod_sort_assignments WHERE assignment_id = @assignment_id;";
						command.Parameters.AddWithValue("@assignment_id", obsoleteAssignmentId.Value);
						command.ExecuteNonQuery();
					}
				}
			});

			return new ModSortOrderRecord(assignmentId, locator, modId, downloadId, record.SortNumber, record.AssignmentState, updatedUtc);
		}

		/// <summary>
		/// Ensures the supplemental Sort table exists and is compatible without rebuilding the archive metadata database.
		/// </summary>
		private void EnsureSchema()
		{
			_database.ExecuteDurableWrite((connection, transaction) =>
			{
				if (!TableExists())
				{
					using (var command = _database.Connection.CreateCommand())
					{
						AttachCurrentTransaction(command);
						command.CommandText = @"
CREATE TABLE mod_sort_assignments (
    assignment_id INTEGER PRIMARY KEY AUTOINCREMENT,
    archive_path TEXT NOT NULL,
    mod_id TEXT NULL,
    download_id TEXT NULL,
    sort_number INTEGER NULL,
    assignment_state INTEGER NOT NULL,
    updated_utc INTEGER NOT NULL
);";
						command.ExecuteNonQuery();
					}
				}
				else
				{
					ValidateSchema();
				}

				EnsureIndex("ix_mod_sort_assignments_archive_path", "archive_path");
				EnsureIndex("ix_mod_sort_assignments_mod_id", "mod_id");
				EnsureIndex("ix_mod_sort_assignments_repository_file", "mod_id", "download_id");
			});
		}

		/// <summary>
		/// Determines whether the supplemental Sort table already exists.
		/// </summary>
		private bool TableExists()
		{
			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @name;";
				command.Parameters.AddWithValue("@name", TableName);
				return Convert.ToInt32(command.ExecuteScalar()) == 1;
			}
		}

		/// <summary>
		/// Validates the required columns of an existing supplemental Sort table.
		/// </summary>
		private void ValidateSchema()
		{
			var columns = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);

			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = "PRAGMA table_info(mod_sort_assignments);";
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						columns[reader.GetString(1)] = new ColumnDefinition(reader.GetString(2), reader.GetInt32(3) != 0, reader.GetInt32(5) != 0);
					}
				}
			}

			RequireColumn(columns, "assignment_id", "INTEGER", false, true);
			RequireColumn(columns, "archive_path", "TEXT", true, false);
			RequireColumn(columns, "mod_id", "TEXT", false, false);
			RequireColumn(columns, "download_id", "TEXT", false, false);
			RequireColumn(columns, "sort_number", "INTEGER", false, false);
			RequireColumn(columns, "assignment_state", "INTEGER", true, false);
			RequireColumn(columns, "updated_utc", "INTEGER", true, false);
		}

		/// <summary>
		/// Validates one required supplemental-table column.
		/// </summary>
		private static void RequireColumn(IDictionary<string, ColumnDefinition> columns, string name, string type, bool notNull, bool primaryKey)
		{
			if (!columns.TryGetValue(name, out var column) ||
				!string.Equals(column.Type, type, StringComparison.OrdinalIgnoreCase) ||
				column.NotNull != notNull || column.PrimaryKey != primaryKey)
			{
				throw new InvalidDataException("The existing mod_sort_assignments table is incompatible with this NMM version.");
			}
		}

		/// <summary>
		/// Creates one required index or rejects an incompatible index with the same name.
		/// </summary>
		private void EnsureIndex(string name, params string[] columns)
		{
			var existingColumns = new List<string>();
			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = "PRAGMA index_info('" + name.Replace("'", "''") + "');";
				using (var reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						existingColumns.Add(reader.GetString(2));
					}
				}
			}

			if (existingColumns.Count > 0)
			{
				if (existingColumns.Count != columns.Length)
				{
					throw new InvalidDataException("The existing " + name + " index is incompatible with this NMM version.");
				}

				for (var i = 0; i < columns.Length; i++)
				{
					if (!string.Equals(existingColumns[i], columns[i], StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("The existing " + name + " index is incompatible with this NMM version.");
					}
				}
				return;
			}

			using (var command = _database.Connection.CreateCommand())
			{
				AttachCurrentTransaction(command);
				command.CommandText = "CREATE INDEX " + name + " ON mod_sort_assignments (" + string.Join(", ", columns) + ");";
				command.ExecuteNonQuery();
			}
		}

		/// <summary>
		/// Reads assignment rows from the supplied command.
		/// </summary>
		private static IReadOnlyList<ModSortOrderRecord> ReadRecords(SQLiteCommand command)
		{
			var records = new List<ModSortOrderRecord>();
			using (var reader = command.ExecuteReader())
			{
				while (reader.Read())
				{
					var sortNumber = ReadNullableInt32(reader, 4);
					var state = (ModSortOrderAssignmentState)reader.GetInt32(5);
					ValidateState(state);
					records.Add(new ModSortOrderRecord(
						reader.GetInt64(0),
						reader.GetString(1),
						reader.IsDBNull(2) ? null : reader.GetString(2),
						reader.IsDBNull(3) ? null : reader.GetString(3),
						sortNumber,
						state,
						new DateTime(reader.GetInt64(6), DateTimeKind.Utc)));
				}
			}
			return records;
		}

		/// <summary>
		/// Reads a nullable signed 32-bit Sort value without accepting out-of-range SQLite integers.
		/// </summary>
		private static int? ReadNullableInt32(SQLiteDataReader reader, int ordinal)
		{
			if (reader.IsDBNull(ordinal))
			{
				return null;
			}

			var value = reader.GetInt64(ordinal);
			if (value < int.MinValue || value > int.MaxValue)
			{
				throw new InvalidDataException("A persisted mod Sort value is outside the Int32 range.");
			}
			return (int)value;
		}

		/// <summary>
		/// Adds common assignment parameters to a write command.
		/// </summary>
		private static void AddAssignmentParameters(SQLiteCommand command, string archivePath, string modId, string downloadId, int? sortNumber, ModSortOrderAssignmentState state, DateTime updatedUtc)
		{
			command.Parameters.AddWithValue("@archive_path", archivePath);
			command.Parameters.AddWithValue("@mod_id", (object)modId ?? DBNull.Value);
			command.Parameters.AddWithValue("@download_id", (object)downloadId ?? DBNull.Value);
			command.Parameters.AddWithValue("@sort_number", sortNumber.HasValue ? (object)sortNumber.Value : DBNull.Value);
			command.Parameters.AddWithValue("@assignment_state", (int)state);
			command.Parameters.AddWithValue("@updated_utc", updatedUtc.Ticks);
		}

		/// <summary>
		/// Attaches the shared batched transaction to a read or schema command when one is active.
		/// </summary>
		private void AttachCurrentTransaction(SQLiteCommand command)
		{
			if (_database.Transaction != null)
			{
				command.Transaction = _database.Transaction;
			}
		}

		/// <summary>
		/// Throws the initialization failure before a store operation can appear successful.
		/// </summary>
		private void EnsureAvailable()
		{
			if (IsUsable)
			{
				return;
			}

			throw new InvalidOperationException("The mod Sort assignment store is unavailable.", _initializationException);
		}

		/// <summary>
		/// Validates a persisted assignment state.
		/// </summary>
		private static void ValidateState(ModSortOrderAssignmentState state)
		{
			if (!Enum.IsDefined(typeof(ModSortOrderAssignmentState), state))
			{
				throw new InvalidDataException("A persisted mod Sort assignment has an unknown state.");
			}
		}

		/// <summary>
		/// Normalizes a repository identifier for indexed case-insensitive lookup.
		/// </summary>
		private static string NormalizeRepositoryId(string identifier)
		{
			return string.IsNullOrWhiteSpace(identifier) ? null : identifier.Trim().ToUpperInvariant();
		}

		/// <summary>
		/// Normalizes the configured Mods root without a trailing separator.
		/// </summary>
		private static string NormalizeDirectoryPath(string path)
		{
			return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		/// <summary>
		/// Normalizes an archive path without a trailing separator.
		/// </summary>
		private static string NormalizeFilePath(string path)
		{
			return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		}

		/// <summary>
		/// Describes one required SQLite column during compatibility validation.
		/// </summary>
		private sealed class ColumnDefinition
		{
			/// <summary>
			/// Initializes a required-column descriptor.
			/// </summary>
			public ColumnDefinition(string type, bool notNull, bool primaryKey)
			{
				Type = type;
				NotNull = notNull;
				PrimaryKey = primaryKey;
			}

			public string Type { get; }
			public bool NotNull { get; }
			public bool PrimaryKey { get; }
		}
	}
}
