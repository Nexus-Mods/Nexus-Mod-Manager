namespace Nexus.Client.Mods.Formats.FOMod
{
	using Nexus.Client.Util;
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Data.SQLite;
	using System.Diagnostics;
	using System.IO;
	using System.Runtime.InteropServices;
	using System.Security;
	using System.Security.Permissions;

	/// <summary>
	/// Stores startup metadata that is expensive to rediscover from every archive.
	/// </summary>
	internal sealed class FOModArchiveMetadataCache
	{
		private const string DatabaseFileName = "fomodArchiveMetadata.sqlite";
		private const int SchemaVersion = 1;
		private const int CommitBatchSize = 50;
		private static readonly object NativeLoadLock = new object();
		private static readonly object DatabaseLock = new object();
		private static readonly Dictionary<string, SharedDatabase> Databases = new Dictionary<string, SharedDatabase>(StringComparer.OrdinalIgnoreCase);
		private static IntPtr _sqliteInteropHandle = IntPtr.Zero;
		private static bool _flushRegistered;
		private readonly string _databasePath;
		private readonly SharedDatabase _database;
		private readonly bool _available;

		public FOModArchiveMetadataCache(string cacheDirectory)
		{
			_databasePath = Path.Combine(cacheDirectory, DatabaseFileName);

			try
			{
				EnsureSQLiteInteropLoaded();
				_database = GetDatabase(_databasePath);
				_available = true;
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.FOModArchiveMetadataCache() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
			}
		}

		public bool TryGet(string archivePath, out FOModArchiveMetadata metadata)
		{
			metadata = null;

			try
			{
				if (!_available || !File.Exists(_databasePath))
				{
					return false;
				}

				var archiveInfo = new FileInfo(archivePath);

				if (!archiveInfo.Exists)
				{
					return false;
				}

				lock (_database.SyncRoot)
					using (var command = _database.Connection.CreateCommand())
					{
						if (_database.Transaction != null)
						{
							command.Transaction = _database.Transaction;
						}

						command.CommandText = @"
SELECT prefix_path, install_script_path, install_script_type, nested_archive, info_xml, screenshot_path, archive_write_time_utc
FROM archive_metadata
WHERE archive_path = @archive_path
  AND archive_length = @archive_length
  AND archive_write_time_utc = @archive_write_time_utc;";

						command.Parameters.AddWithValue("@archive_path", NormalizeArchivePath(archivePath));
						command.Parameters.AddWithValue("@archive_length", archiveInfo.Length);
						command.Parameters.AddWithValue("@archive_write_time_utc", archiveInfo.LastWriteTimeUtc.Ticks);

						using (var reader = command.ExecuteReader())
						{
							if (!reader.Read())
							{
								return false;
							}

							metadata = new FOModArchiveMetadata
							{
								PrefixPath = reader.IsDBNull(0) ? null : reader.GetString(0),
								InstallScriptPath = reader.IsDBNull(1) ? null : reader.GetString(1),
								InstallScriptType = reader.IsDBNull(2) ? null : reader.GetString(2),
								HasNestedArchive = !reader.IsDBNull(3) && reader.GetBoolean(3),
								InfoXml = reader.IsDBNull(4) ? null : (byte[])reader[4],
								ScreenshotPath = reader.IsDBNull(5) ? null : reader.GetString(5),
								ArchiveWriteTimeUtc = reader.IsDBNull(6)
									? (DateTime?)null
									: new DateTime(reader.GetInt64(6), DateTimeKind.Utc)
							};

							if (metadata.InfoXml == null || metadata.InfoXml.Length == 0)
							{
								metadata = null;
								return false;
							}

							return true;
						}
					}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.TryGet() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
				metadata = null;
				return false;
			}
		}

		public void Remove(string archivePath)
		{
			try
			{
				if (!_available)
				{
					return;
				}

				lock (_database.SyncRoot)
					using (var command = _database.Connection.CreateCommand())
					{
						_database.EnsureTransaction();
						command.Transaction = _database.Transaction;
						command.CommandText = "DELETE FROM archive_metadata WHERE archive_path = @archive_path;";
						command.Parameters.AddWithValue("@archive_path", NormalizeArchivePath(archivePath));
						command.ExecuteNonQuery();
						command.CommandText = "DELETE FROM archive_screenshot_cache WHERE archive_path = @archive_path;";
						command.ExecuteNonQuery();
						_database.RemoveArchiveFingerprint(archivePath);
						_database.PendingWrites++;
						CommitDatabase(_database);
					}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.Remove() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
			}
		}
		public bool IsUsable
		{
			get
			{
				return _available;
			}
		}

		/// <summary>
		/// Checks whether the database contains valid metadata for the current archive fingerprint.
		/// </summary>
		public bool ContainsValidArchive(string archivePath)
		{
			try
			{
				if (!IsUsable || string.IsNullOrEmpty(archivePath))
				{
					return false;
				}

				var archiveInfo = new FileInfo(archivePath);
				if (!archiveInfo.Exists)
				{
					return false;
				}

				lock (_database.SyncRoot)
				{
					_database.EnsureArchiveFingerprintsLoaded();

					if (!_database.ArchiveFingerprints.TryGetValue(NormalizeArchivePath(archivePath), out var fingerprint))
					{
						return false;
					}

					return fingerprint.Length == archiveInfo.Length &&
						fingerprint.WriteTimeUtcTicks == archiveInfo.LastWriteTimeUtc.Ticks;
				}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.ContainsValidArchive() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
				return false;
			}
		}

		/// <summary>
		/// Retrieves a screenshot override stored in SQLite for the current archive fingerprint.
		/// </summary>
		public bool TryGetScreenshot(string archivePath, string screenshotPath, out byte[] screenshotData)
		{
			screenshotData = null;

			try
			{
				if (!_available || string.IsNullOrEmpty(archivePath) || string.IsNullOrEmpty(screenshotPath))
				{
					return false;
				}

				var archiveInfo = new FileInfo(archivePath);
				if (!archiveInfo.Exists)
				{
					return false;
				}

				lock (_database.SyncRoot)
					using (var command = _database.Connection.CreateCommand())
					{
						if (_database.Transaction != null)
						{
							command.Transaction = _database.Transaction;
						}

						command.CommandText = @"
SELECT screenshot_data
FROM archive_screenshot_cache
WHERE archive_path = @archive_path
  AND archive_length = @archive_length
  AND archive_write_time_utc = @archive_write_time_utc
  AND screenshot_path = @screenshot_path;";
						command.Parameters.AddWithValue("@archive_path", NormalizeArchivePath(archivePath));
						command.Parameters.AddWithValue("@archive_length", archiveInfo.Length);
						command.Parameters.AddWithValue("@archive_write_time_utc", archiveInfo.LastWriteTimeUtc.Ticks);
						command.Parameters.AddWithValue("@screenshot_path", NormalizeVirtualPath(screenshotPath));

						var value = command.ExecuteScalar();
						if (value == null || value == DBNull.Value)
						{
							return false;
						}

						screenshotData = (byte[])value;
						return screenshotData.Length > 0;
					}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.TryGetScreenshot() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
				screenshotData = null;
				return false;
			}
		}

		/// <summary>
		/// Stores a generated screenshot override in SQLite without creating a loose cache file.
		/// </summary>
		public void SaveScreenshot(string archivePath, string screenshotPath, byte[] screenshotData)
		{
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();

				if (!_available || string.IsNullOrEmpty(screenshotPath) || screenshotData == null || screenshotData.Length == 0)
				{
					return;
				}

				var archiveInfo = new FileInfo(archivePath);
				if (!archiveInfo.Exists)
				{
					return;
				}

				lock (_database.SyncRoot)
					using (var command = _database.Connection.CreateCommand())
					{
						_database.EnsureTransaction();
						command.Transaction = _database.Transaction;
						command.CommandText = @"
INSERT OR REPLACE INTO archive_screenshot_cache
	(archive_path, archive_length, archive_write_time_utc, screenshot_path, screenshot_data, updated_utc)
VALUES
	(@archive_path, @archive_length, @archive_write_time_utc, @screenshot_path, @screenshot_data, @updated_utc);";
						command.Parameters.AddWithValue("@archive_path", NormalizeArchivePath(archivePath));
						command.Parameters.AddWithValue("@archive_length", archiveInfo.Length);
						command.Parameters.AddWithValue("@archive_write_time_utc", archiveInfo.LastWriteTimeUtc.Ticks);
						command.Parameters.AddWithValue("@screenshot_path", NormalizeVirtualPath(screenshotPath));
						command.Parameters.AddWithValue("@screenshot_data", screenshotData);
						command.Parameters.AddWithValue("@updated_utc", DateTime.UtcNow.Ticks);
						command.ExecuteNonQuery();
						_database.PendingWrites++;

						if (_database.PendingWrites >= CommitBatchSize || (DateTime.UtcNow - _database.LastCommitUtc).TotalSeconds >= 1)
						{
							CommitDatabase(_database);
						}
					}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.SaveScreenshot() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
		}

		/// <summary>
		/// Removes a generated screenshot override from SQLite.
		/// </summary>
		public void RemoveScreenshot(string archivePath)
		{
			try
			{
				new PermissionSet(PermissionState.Unrestricted).Assert();

				if (!_available)
				{
					return;
				}

				lock (_database.SyncRoot)
					using (var command = _database.Connection.CreateCommand())
					{
						_database.EnsureTransaction();
						command.Transaction = _database.Transaction;
						command.CommandText = "DELETE FROM archive_screenshot_cache WHERE archive_path = @archive_path;";
						command.Parameters.AddWithValue("@archive_path", NormalizeArchivePath(archivePath));
						command.ExecuteNonQuery();
						_database.PendingWrites++;

						if (_database.PendingWrites >= CommitBatchSize || (DateTime.UtcNow - _database.LastCommitUtc).TotalSeconds >= 1)
						{
							CommitDatabase(_database);
						}
					}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.RemoveScreenshot() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
		}

		public void Save(string archivePath, FOModArchiveMetadata metadata)
		{
			try
			{
				// This trusted cache write can be reached through a callback from the restricted C# script AppDomain.
				// Keep the assertion scoped to this internal operation so sandboxed scripts do not receive broader permissions.
				new PermissionSet(PermissionState.Unrestricted).Assert();

				if (!_available)
				{
					return;
				}

				var archiveInfo = new FileInfo(archivePath);
				if (!archiveInfo.Exists || metadata == null || metadata.InfoXml == null)
				{
					return;
				}

				lock (_database.SyncRoot)
					using (var command = _database.Connection.CreateCommand())
					{
						_database.EnsureTransaction();
						command.Transaction = _database.Transaction;
						command.CommandText = @"
INSERT OR REPLACE INTO archive_metadata
	(archive_path, archive_length, archive_write_time_utc, prefix_path, install_script_path, install_script_type, nested_archive, info_xml, screenshot_path, updated_utc)
VALUES
	(@archive_path, @archive_length, @archive_write_time_utc, @prefix_path, @install_script_path, @install_script_type, @nested_archive, @info_xml, @screenshot_path, @updated_utc);";
						command.Parameters.AddWithValue("@archive_path", NormalizeArchivePath(archivePath));
						command.Parameters.AddWithValue("@archive_length", archiveInfo.Length);
						command.Parameters.AddWithValue("@archive_write_time_utc", archiveInfo.LastWriteTimeUtc.Ticks);
						command.Parameters.AddWithValue("@prefix_path", (object)metadata.PrefixPath ?? DBNull.Value);
						command.Parameters.AddWithValue("@install_script_path", (object)metadata.InstallScriptPath ?? DBNull.Value);
						command.Parameters.AddWithValue("@install_script_type", (object)metadata.InstallScriptType ?? DBNull.Value);
						command.Parameters.AddWithValue("@nested_archive", metadata.HasNestedArchive);
						command.Parameters.AddWithValue("@info_xml", metadata.InfoXml);
						command.Parameters.AddWithValue("@screenshot_path", (object)metadata.ScreenshotPath ?? DBNull.Value);
						command.Parameters.AddWithValue("@updated_utc", DateTime.UtcNow.Ticks);
						command.ExecuteNonQuery();
						_database.AddArchiveFingerprint(archivePath, archiveInfo.Length, archiveInfo.LastWriteTimeUtc.Ticks);
						_database.PendingWrites++;

						if (_database.PendingWrites >= CommitBatchSize || (DateTime.UtcNow - _database.LastCommitUtc).TotalSeconds >= 1)
						{
							CommitDatabase(_database);
						}
					}
			}
			catch (Exception e)
			{
				Trace.TraceWarning("FOModArchiveMetadataCache.Save() - Encountered an ignored Exception.");
				TraceUtil.TraceException(e);
			}
			finally
			{
				PermissionSet.RevertAssert();
			}
		}

		private static SharedDatabase GetDatabase(string databasePath)
		{
			lock (DatabaseLock)
			{
				if (Databases.TryGetValue(databasePath, out var database))
				{
					return database;
				}

				database = new SharedDatabase(databasePath);
				Databases[databasePath] = database;

				if (!_flushRegistered)
				{
					AppDomain.CurrentDomain.ProcessExit += FlushDatabases;
					AppDomain.CurrentDomain.DomainUnload += FlushDatabases;
					_flushRegistered = true;
				}

				return database;
			}
		}

		private static void FlushDatabases(object sender, EventArgs e)
		{
			lock (DatabaseLock)
			{
				foreach (var database in Databases.Values)
				{
					lock (database.SyncRoot)
					{
						CommitDatabase(database);
						database.Connection.Dispose();
					}
				}

				Databases.Clear();
			}
		}

		private static void CommitDatabase(SharedDatabase database)
		{
			if (database.PendingWrites == 0)
			{
				return;
			}

			// Archive metadata is cache data, so batching commits avoids a disk flush per mod.
			database.Transaction.Commit();
			database.Transaction.Dispose();
			database.Transaction = database.Connection.BeginTransaction();
			database.PendingWrites = 0;
			database.LastCommitUtc = DateTime.UtcNow;
		}

		/// <summary>
		/// Represents the file-system identity used to validate cached archive metadata.
		/// </summary>
		private struct ArchiveFingerprint
		{
			public ArchiveFingerprint(long length, long writeTimeUtcTicks)
			{
				Length = length;
				WriteTimeUtcTicks = writeTimeUtcTicks;
			}

			public long Length { get; }
			public long WriteTimeUtcTicks { get; }
		}

		private sealed class SharedDatabase
		{
			public readonly object SyncRoot = new object();
			public readonly SQLiteConnection Connection;
			public readonly Dictionary<string, ArchiveFingerprint> ArchiveFingerprints = new Dictionary<string, ArchiveFingerprint>(StringComparer.OrdinalIgnoreCase);
			public SQLiteTransaction Transaction;
			public int PendingWrites;
			public DateTime LastCommitUtc;
			private bool _archiveFingerprintsLoaded;

			public SharedDatabase(string databasePath)
			{
				var cacheDirectory = Path.GetDirectoryName(databasePath);
				Directory.CreateDirectory(cacheDirectory);

				if (!Directory.Exists(cacheDirectory))
				{
					throw new DirectoryNotFoundException("Unable to create the FOMod archive metadata cache folder: " + cacheDirectory);
				}

				var builder = new SQLiteConnectionStringBuilder
				{
					DataSource = databasePath,
					ForeignKeys = true,
					JournalMode = SQLiteJournalModeEnum.Delete,
					Pooling = false,
					SyncMode = SynchronizationModes.Normal
				};

				Connection = new SQLiteConnection(builder.ConnectionString);
				Connection.Open();

				EnsureDeleteJournalMode(Connection);

				if (!HasCurrentSchema(Connection))
				{
					EnsureSchema(Connection);
				}

				EnsureSupplementalSchema(Connection);

				if (!File.Exists(databasePath))
				{
					throw new IOException("Unable to create the FOMod archive metadata cache database: " + databasePath);
				}

				LastCommitUtc = DateTime.UtcNow;
			}

			public void EnsureTransaction()
			{
				if (Transaction == null)
				{
					Transaction = Connection.BeginTransaction();
				}
			}

			/// <summary>
			/// Loads lightweight archive fingerprints into memory for O(1) warm-start format probes.
			/// </summary>
			public void EnsureArchiveFingerprintsLoaded()
			{
				if (_archiveFingerprintsLoaded)
				{
					return;
				}

				using (var command = Connection.CreateCommand())
				{
					if (Transaction != null)
					{
						command.Transaction = Transaction;
					}

					command.CommandText = @"
SELECT archive_path, archive_length, archive_write_time_utc
FROM archive_metadata
WHERE info_xml IS NOT NULL AND length(info_xml) > 0;";
					using (var reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							if (!reader.IsDBNull(0) && !reader.IsDBNull(1) && !reader.IsDBNull(2))
							{
								ArchiveFingerprints[NormalizeArchivePath(reader.GetString(0))] = new ArchiveFingerprint(reader.GetInt64(1), reader.GetInt64(2));
							}
						}
					}
				}

				_archiveFingerprintsLoaded = true;
			}

			/// <summary>
			/// Updates an already-loaded in-memory fingerprint index after a cache write.
			/// </summary>
			public void AddArchiveFingerprint(string archivePath, long archiveLength, long archiveWriteTimeUtcTicks)
			{
				if (_archiveFingerprintsLoaded)
				{
					ArchiveFingerprints[NormalizeArchivePath(archivePath)] = new ArchiveFingerprint(archiveLength, archiveWriteTimeUtcTicks);
				}
			}

			/// <summary>
			/// Removes an archive from the already-loaded in-memory fingerprint index.
			/// </summary>
			public void RemoveArchiveFingerprint(string archivePath)
			{
				if (_archiveFingerprintsLoaded)
				{
					ArchiveFingerprints.Remove(NormalizeArchivePath(archivePath));
				}
			}

			private static bool HasCurrentSchema(SQLiteConnection connection)
			{
				try
				{
					using (var command = connection.CreateCommand())
					{
						command.CommandText = @"
SELECT COUNT(1)
FROM sqlite_master
WHERE type = 'table'
  AND name IN ('schema_info', 'archive_metadata');";
						if (Convert.ToInt32(command.ExecuteScalar()) != 2)
						{
							return false;
						}

						command.CommandText = "SELECT version FROM schema_info LIMIT 1;";
						var version = command.ExecuteScalar();
						return version != null && version != DBNull.Value && Convert.ToInt32(version) == SchemaVersion;
					}
				}
				catch (SQLiteException)
				{
					return false;
				}
			}

			/// <summary>
			/// Ensures additive cache tables that do not require a destructive schema migration.
			/// </summary>
			private static void EnsureSupplementalSchema(SQLiteConnection connection)
			{
				using (var command = connection.CreateCommand())
				{
					command.CommandText = @"
CREATE TABLE IF NOT EXISTS archive_screenshot_cache (
	archive_path TEXT NOT NULL PRIMARY KEY,
	archive_length INTEGER NOT NULL,
	archive_write_time_utc INTEGER NOT NULL,
	screenshot_path TEXT NOT NULL,
	screenshot_data BLOB NOT NULL,
	updated_utc INTEGER NOT NULL
);";
					command.ExecuteNonQuery();
				}
			}

			private static void EnsureDeleteJournalMode(SQLiteConnection connection)
			{
				using (var command = connection.CreateCommand())
				{
					command.CommandText = "PRAGMA journal_mode;";
					var journalMode = Convert.ToString(command.ExecuteScalar());

					if (string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
					{
						command.CommandText = "PRAGMA journal_mode=DELETE;";
						command.ExecuteScalar();
					}
				}
			}

			private static void EnsureSchema(SQLiteConnection connection)
			{
				using (var command = connection.CreateCommand())
				{
					command.CommandText = @"
PRAGMA journal_mode=DELETE;
PRAGMA synchronous=NORMAL;
CREATE TABLE IF NOT EXISTS schema_info (
	version INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS archive_metadata (
	archive_path TEXT NOT NULL PRIMARY KEY,
	archive_length INTEGER NOT NULL,
	archive_write_time_utc INTEGER NOT NULL,
	prefix_path TEXT NULL,
	install_script_path TEXT NULL,
	install_script_type TEXT NULL,
	nested_archive INTEGER NOT NULL,
	info_xml BLOB NOT NULL,
	screenshot_path TEXT NULL,
	updated_utc INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_archive_metadata_stamp ON archive_metadata (archive_path, archive_length, archive_write_time_utc);
INSERT INTO schema_info (version)
SELECT @schema_version
WHERE NOT EXISTS (SELECT 1 FROM schema_info);";
					command.Parameters.AddWithValue("@schema_version", SchemaVersion);
					command.ExecuteNonQuery();
				}
			}
		}

		private void EnsureSQLiteInteropLoaded()
		{
			if (_sqliteInteropHandle != IntPtr.Zero)
			{
				return;
			}

			lock (NativeLoadLock)
			{
				if (_sqliteInteropHandle != IntPtr.Zero)
				{
					return;
				}

				var triedPaths = new List<string>();

				foreach (var path in GetSQLiteInteropCandidates())
				{
					triedPaths.Add(path);

					if (!File.Exists(path))
					{
						continue;
					}

					_sqliteInteropHandle = LoadLibrary(path);

					if (_sqliteInteropHandle != IntPtr.Zero)
					{
						return;
					}
				}

				var error = new Win32Exception(Marshal.GetLastWin32Error());
				throw new DllNotFoundException("Unable to load SQLite.Interop.dll. Tried: " + string.Join("; ", triedPaths) + Environment.NewLine + error.Message);
			}
		}

		private static IEnumerable<string> GetSQLiteInteropCandidates()
		{
			var architecture = Environment.Is64BitProcess ? "x64" : "x86";
			var assemblyDirectory = Path.GetDirectoryName(typeof(FOModArchiveMetadataCache).Assembly.Location);
			var appDirectory = AppDomain.CurrentDomain.BaseDirectory;

			if (!string.IsNullOrEmpty(assemblyDirectory))
			{
				yield return Path.Combine(assemblyDirectory, architecture, "SQLite.Interop.dll");
				yield return Path.Combine(assemblyDirectory, "SQLite.Interop.dll");
			}

			if (!string.IsNullOrEmpty(appDirectory))
			{
				yield return Path.Combine(appDirectory, architecture, "SQLite.Interop.dll");
				yield return Path.Combine(appDirectory, "ModFormats", architecture, "SQLite.Interop.dll");
				yield return Path.Combine(appDirectory, "SQLite.Interop.dll");
			}
		}

		private static string NormalizeArchivePath(string archivePath)
		{
			return Path.GetFullPath(archivePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
		}

		/// <summary>
		/// Normalizes a FOMod virtual path for SQLite lookup keys.
		/// </summary>
		private static string NormalizeVirtualPath(string path)
		{
			return (path ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar).ToUpperInvariant();
		}

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern IntPtr LoadLibrary(string lpFileName);
	}

	internal sealed class FOModArchiveMetadata
	{
		public string PrefixPath { get; set; }
		public string InstallScriptPath { get; set; }
		public string InstallScriptType { get; set; }
		public bool HasNestedArchive { get; set; }
		public byte[] InfoXml { get; set; }
		public string ScreenshotPath { get; set; }
		public DateTime? ArchiveWriteTimeUtc { get; set; }
	}
}
