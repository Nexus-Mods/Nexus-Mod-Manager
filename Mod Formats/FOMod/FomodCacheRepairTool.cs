namespace Nexus.Client.Mods.Formats.FOMod
{
	using System;
	using System.Collections.Generic;
	using System.Data.SQLite;
	using System.Diagnostics;
	using System.IO;
	using System.Linq;
	using Nexus.Client.Util;

	/// <summary>
	/// A one-shot, UI-triggered repair tool that restores the cached <c>info.xml</c>
	/// for mods whose entry in <c>fomodArchiveMetadata.sqlite</c> is missing or empty,
	/// by pulling it back from the mod's legacy (pre-SQLite) loose cache folder,
	/// when one is still present on disk.
	/// </summary>
	/// <remarks>
	/// This does not attempt to recreate the loose cache, nor does it change how the
	/// live cache is normally read or written - it only ever writes the
	/// <c>info_xml</c> column of <c>archive_metadata</c>, and only for archives it is
	/// explicitly asked to check. It never deletes anything.
	/// </remarks>
	public static class FOModCacheRepairTool
	{
		private const string DatabaseFileName = "fomodArchiveMetadata.sqlite";

		/// <summary>
		/// The outcome of a repair run.
		/// </summary>
		public sealed class RepairResult
		{
			/// <summary>The number of archives that were checked.</summary>
			public int CheckedCount { get; internal set; }

			/// <summary>The number of archives whose info.xml was restored.</summary>
			public int FixedCount => FixedModArchives.Count;

			/// <summary>The archive paths that were successfully repaired.</summary>
			public IList<string> FixedModArchives { get; } = new List<string>();

			/// <summary>Human-readable problems encountered along the way (non-fatal - the run continues).</summary>
			public IList<string> Errors { get; } = new List<string>();
		}

		/// <summary>
		/// Attempts to restore the cached info.xml for each of the given mod archives,
		/// pulling it from that archive's legacy loose cache folder when one exists.
		/// </summary>
		/// <param name="p_strModCacheDirectory">The game mode's mod cache directory (contains fomodArchiveMetadata.sqlite and any legacy per-archive cache folders).</param>
		/// <param name="p_strArchivePaths">The full paths of the mod archives to check.</param>
		public static RepairResult RepairFromLegacyCache(string p_strModCacheDirectory, IEnumerable<string> p_strArchivePaths)
		{
			var result = new RepairResult();

			if (string.IsNullOrEmpty(p_strModCacheDirectory) || !Directory.Exists(p_strModCacheDirectory))
			{
				result.Errors.Add("Mod cache directory not found: " + p_strModCacheDirectory);
				return result;
			}

			var databasePath = Path.Combine(p_strModCacheDirectory, DatabaseFileName);

			if (!File.Exists(databasePath))
			{
				result.Errors.Add("FOMod archive metadata cache database not found: " + databasePath);
				return result;
			}

			var archivePaths = (p_strArchivePaths ?? Enumerable.Empty<string>())
				.Where(path => !string.IsNullOrEmpty(path))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (archivePaths.Count == 0)
			{
				return result;
			}

			try
			{
				using (var connection = OpenConnection(databasePath))
				{
					if (!HasArchiveMetadataTable(connection))
					{
						result.Errors.Add("The FOMod archive metadata cache has not been initialised yet - start NMM normally first, then retry.");
						return result;
					}

					using (var transaction = connection.BeginTransaction())
					{
						foreach (var archivePath in archivePaths)
						{
							result.CheckedCount++;

							try
							{
								if (TryRepairArchive(connection, transaction, p_strModCacheDirectory, archivePath))
								{
									result.FixedModArchives.Add(archivePath);
								}
							}
							catch (Exception e)
							{
								result.Errors.Add(Path.GetFileName(archivePath) + ": " + e.Message);
								TraceUtil.TraceException(e);
							}
						}

						transaction.Commit();
					}
				}
			}
			catch (Exception e)
			{
				result.Errors.Add("Unable to open the FOMod archive metadata cache: " + e.Message);
				TraceUtil.TraceException(e);
			}

			return result;
		}

		/// <summary>
		/// Repairs a single archive's info.xml, if a legacy cache copy with "Data" in its
		/// path can be found for it. Returns <c>true</c> if a repair was made.
		/// </summary>
		private static bool TryRepairArchive(SQLiteConnection p_conConnection, SQLiteTransaction p_trnTransaction, string p_strModCacheDirectory, string p_strArchivePath)
		{
			var legacyCacheFolder = Path.Combine(p_strModCacheDirectory, Path.GetFileNameWithoutExtension(p_strArchivePath));

			if (!Directory.Exists(legacyCacheFolder))
			{
				return false;
			}

			var legacyInfoXmlPath = FindLegacyInfoXmlWithDataInPath(legacyCacheFolder);

			if (legacyInfoXmlPath == null)
			{
				return false;
			}

			var infoXmlBytes = File.ReadAllBytes(legacyInfoXmlPath);

			if (infoXmlBytes.Length == 0)
			{
				return false;
			}

			var archiveKey = NormalizeArchivePath(p_strArchivePath);
			var nowTicks = DateTime.UtcNow.Ticks;

			using (var command = p_conConnection.CreateCommand())
			{
				command.Transaction = p_trnTransaction;
				command.CommandText = "SELECT COUNT(1) FROM archive_metadata WHERE archive_path = @archive_path;";
				command.Parameters.AddWithValue("@archive_path", archiveKey);
				var rowExists = Convert.ToInt32(command.ExecuteScalar()) > 0;

				if (rowExists)
				{
					// Only the cached info.xml is touched - prefix/script/nested data
					// already on record for this archive is left exactly as-is.
					command.Parameters.Clear();
					command.CommandText = @"
UPDATE archive_metadata
SET info_xml = @info_xml, updated_utc = @updated_utc
WHERE archive_path = @archive_path;";
					command.Parameters.AddWithValue("@info_xml", infoXmlBytes);
					command.Parameters.AddWithValue("@updated_utc", nowTicks);
					command.Parameters.AddWithValue("@archive_path", archiveKey);
					command.ExecuteNonQuery();
				}
				else
				{
					if (!File.Exists(p_strArchivePath))
					{
						return false;
					}

					var archiveInfo = new FileInfo(p_strArchivePath);
					command.Parameters.Clear();
					command.CommandText = @"
INSERT INTO archive_metadata
	(archive_path, archive_length, archive_write_time_utc, prefix_path, install_script_path, install_script_type, nested_archive, info_xml, screenshot_path, updated_utc)
VALUES
	(@archive_path, @archive_length, @archive_write_time_utc, NULL, NULL, NULL, 0, @info_xml, NULL, @updated_utc);";
					command.Parameters.AddWithValue("@archive_path", archiveKey);
					command.Parameters.AddWithValue("@archive_length", archiveInfo.Length);
					command.Parameters.AddWithValue("@archive_write_time_utc", archiveInfo.LastWriteTimeUtc.Ticks);
					command.Parameters.AddWithValue("@info_xml", infoXmlBytes);
					command.Parameters.AddWithValue("@updated_utc", nowTicks);
					command.ExecuteNonQuery();
				}
			}

			return true;
		}

		/// <summary>
		/// Looks for a <c>fomod/info.xml</c> under the given legacy per-archive cache
		/// folder, where the path down to it passes through a folder named "Data" -
		/// i.e. the layout the old (pre-SQLite) cache used for plugin-based games.
		/// </summary>
		private static string FindLegacyInfoXmlWithDataInPath(string p_strLegacyCacheFolder)
		{
			foreach (var infoXmlPath in Directory.EnumerateFiles(p_strLegacyCacheFolder, "info.xml", SearchOption.AllDirectories))
			{
				var fomodDirectory = Path.GetDirectoryName(infoXmlPath);

				if (fomodDirectory == null || !Path.GetFileName(fomodDirectory).Equals("fomod", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				var relativePath = infoXmlPath.Substring(p_strLegacyCacheFolder.Length)
					.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

				var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

				if (pathSegments.Any(segment => segment.Equals("Data", StringComparison.OrdinalIgnoreCase)))
				{
					return infoXmlPath;
				}
			}

			return null;
		}

		private static SQLiteConnection OpenConnection(string p_strDatabasePath)
		{
			var builder = new SQLiteConnectionStringBuilder
			{
				DataSource = p_strDatabasePath,
				ForeignKeys = true,
				JournalMode = SQLiteJournalModeEnum.Delete,
				Pooling = false,
				SyncMode = SynchronizationModes.Normal
			};

			var connection = new SQLiteConnection(builder.ConnectionString);
			connection.Open();

			using (var pragmaCommand = connection.CreateCommand())
			{
				// Avoid a hard failure if the app's own long-lived connection to this
				// same database happens to be mid-transaction when this runs.
				pragmaCommand.CommandText = "PRAGMA busy_timeout = 5000;";
				pragmaCommand.ExecuteNonQuery();
			}

			return connection;
		}

		private static bool HasArchiveMetadataTable(SQLiteConnection p_conConnection)
		{
			using (var command = p_conConnection.CreateCommand())
			{
				command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = 'archive_metadata';";
				return Convert.ToInt32(command.ExecuteScalar()) == 1;
			}
		}

		private static string NormalizeArchivePath(string p_strArchivePath)
		{
			// Mirrors FOModArchiveMetadataCache.NormalizeArchivePath exactly, so the
			// key matches what NMM itself would look up.
			return Path.GetFullPath(p_strArchivePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
		}
	}
}
