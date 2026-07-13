namespace Nexus.Client.ModManagement
{
	using System;
	using System.Collections.Generic;
	using System.Data;
	using System.Diagnostics;
	using System.Data.SQLite;
	using System.IO;
	using System.Linq;
	using System.Runtime.CompilerServices;

	internal sealed class SQLiteVirtualModStore : IVirtualModStore
	{
		private const int SCHEMA_VERSION = 1;
		private readonly string m_strXmlFilePath;
		private readonly string m_strDatabasePath;
		private readonly Version m_vrsCurrentVersion;
		private readonly Func<Version, bool> m_fncIsValidVersion;
		private readonly XmlVirtualModStore m_xmsXmlStore;

		public SQLiteVirtualModStore(string xmlFilePath, string databasePath, Version currentVersion, Func<Version, bool> isValidVersion, XmlVirtualModStore xmlStore)
		{
			m_strXmlFilePath = Path.GetFullPath(xmlFilePath);
			m_strDatabasePath = Path.GetFullPath(databasePath);
			m_vrsCurrentVersion = currentVersion;
			m_fncIsValidVersion = isValidVersion;
			m_xmsXmlStore = xmlStore;
		}

		public static string GetDefaultDatabasePath(string xmlFilePath)
		{
			return Path.ChangeExtension(xmlFilePath, ".sqlite");
		}

		public void Initialize()
		{
			EnsureDatabaseDirectory();
			using (SQLiteConnection connection = OpenConnection())
			{
				EnsureSchema(connection);
			}
		}

		public bool CanLoadExistingStore()
		{
			if (!File.Exists(m_strDatabasePath))
				return false;

			try
			{
				using (SQLiteConnection connection = OpenReadOnlyConnection())
				{
					return HasCompatibleSchema(connection, m_fncIsValidVersion);
				}
			}
			catch
			{
				return false;
			}
		}

		public bool IsReadyForWrite(string filePath)
		{
			if (IsMainStorePath(filePath))
				return m_xmsXmlStore.IsReadyForWrite(m_strDatabasePath);

			return m_xmsXmlStore.IsReadyForWrite(filePath);
		}

		public void Save(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			if (!IsMainStorePath(filePath))
			{
				m_xmsXmlStore.Save(fileVersion, filePath, virtualModInfo, virtualModLink);
				return;
			}

			SaveRecords(fileVersion, BuildReferenceRecords(virtualModInfo, virtualModLink));
			SaveXmlCompatibilityShadow(fileVersion, virtualModInfo, virtualModLink, false);
		}

		public void SaveWithModInfoMatching(Version fileVersion, string filePath, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			if (!IsMainStorePath(filePath))
			{
				m_xmsXmlStore.SaveWithModInfoMatching(fileVersion, filePath, virtualModInfo, virtualModLink);
				return;
			}

			SaveRecords(fileVersion, BuildMatchingRecords(virtualModInfo, virtualModLink));
			SaveXmlCompatibilityShadow(fileVersion, virtualModInfo, virtualModLink, true);
		}

		public VirtualModStoreData Load(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion)
		{
			if (!IsMainStorePath(filePath))
				return m_xmsXmlStore.Load(filePath, currentVersion, isValidVersion, getMissingFileVersion);

			return LoadStoreData(false).Data;
		}

		public bool TryLoad(string filePath, Version currentVersion, Func<Version, bool> isValidVersion, Func<string, string, string> getMissingFileVersion, out VirtualModStoreData data)
		{
			if (!IsMainStorePath(filePath))
				return m_xmsXmlStore.TryLoad(filePath, currentVersion, isValidVersion, getMissingFileVersion, out data);

			SQLiteLoadResult result = LoadStoreData(true);
			data = result.Data;
			return result.HasStoredMods;
		}

		public void Copy(string sourcePath, string destinationPath)
		{
			if (!IsMainStorePath(sourcePath))
			{
				m_xmsXmlStore.Copy(sourcePath, destinationPath);
				return;
			}

			VirtualModStoreData data = LoadStoreData(false).Data;
			m_xmsXmlStore.Save(m_vrsCurrentVersion, destinationPath, data.VirtualMods, data.VirtualLinks);
		}

		internal void SaveStoreData(Version fileVersion, VirtualModStoreData data)
		{
			SaveRecords(fileVersion, BuildReferenceRecords(data.VirtualMods, data.VirtualLinks));
		}

		private void SaveXmlCompatibilityShadow(Version fileVersion, IList<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink, bool useModInfoMatching)
		{
			try
			{
				if (useModInfoMatching)
					m_xmsXmlStore.SaveWithModInfoMatching(fileVersion, m_strXmlFilePath, virtualModInfo, virtualModLink);
				else
					m_xmsXmlStore.Save(fileVersion, m_strXmlFilePath, virtualModInfo, virtualModLink);
			}
			catch (Exception e)
			{
				Trace.TraceWarning("Could not update virtual mod XML compatibility shadow \"{0}\": {1}", m_strXmlFilePath, e.Message);
			}
		}

		private void SaveRecords(Version fileVersion, IList<ModRecord> records)
		{
			EnsureDatabaseDirectory();
			using (SQLiteConnection connection = OpenConnection())
			{
				EnsureSchema(connection);
				using (SQLiteTransaction transaction = connection.BeginTransaction())
				{
					ExecuteNonQuery(connection, transaction, "DELETE FROM VirtualLinks;");
					ExecuteNonQuery(connection, transaction, "DELETE FROM VirtualMods;");
					SetMetadata(connection, transaction, "schema_version", SCHEMA_VERSION.ToString());
					SetMetadata(connection, transaction, "file_version", fileVersion.ToString());

					for (int intModIndex = 0; intModIndex < records.Count; intModIndex++)
					{
						ModRecord record = records[intModIndex];
						long modKey = InsertMod(connection, transaction, intModIndex, record.ModInfo);

						for (int intLinkIndex = 0; intLinkIndex < record.Links.Count; intLinkIndex++)
						{
							InsertLink(connection, transaction, modKey, intLinkIndex, record.Links[intLinkIndex]);
						}
					}

					transaction.Commit();
				}
			}
		}

		private SQLiteLoadResult LoadStoreData(bool reportStoredMods)
		{
			List<IVirtualModInfo> lstVirtualMods = new List<IVirtualModInfo>();
			List<IVirtualModLink> lstVirtualLinks = new List<IVirtualModLink>();
			bool hasStoredMods = false;

			if (!File.Exists(m_strDatabasePath))
				return new SQLiteLoadResult(new VirtualModStoreData(lstVirtualMods, lstVirtualLinks), false);

			using (SQLiteConnection connection = OpenReadOnlyConnection())
			{
				if (!HasCompatibleSchema(connection, m_fncIsValidVersion))
					throw new InvalidOperationException("SQLite virtual mod store failed validation.");

				using (SQLiteCommand modCommand = connection.CreateCommand())
				{
					modCommand.CommandText = "SELECT VirtualModId, ModId, DownloadId, UpdatedDownloadId, ModName, ModFileName, ModNewFileName, ModFilePath, FileVersion FROM VirtualMods ORDER BY ModOrder;";
					using (SQLiteDataReader modReader = modCommand.ExecuteReader())
					{
						while (modReader.Read())
						{
							hasStoredMods = true;
							long modKey = modReader.GetInt64(0);
							VirtualModInfo modInfo = new VirtualModInfo(GetString(modReader, 1), GetString(modReader, 2), GetString(modReader, 3), GetString(modReader, 4), GetString(modReader, 5), GetString(modReader, 6), GetString(modReader, 7), GetString(modReader, 8));
							LoadLinks(connection, modKey, modInfo, lstVirtualMods, lstVirtualLinks);
						}
					}
				}
			}

			return new SQLiteLoadResult(new VirtualModStoreData(lstVirtualMods, lstVirtualLinks), reportStoredMods && hasStoredMods);
		}

		private static void LoadLinks(SQLiteConnection connection, long modKey, IVirtualModInfo modInfo, List<IVirtualModInfo> virtualMods, List<IVirtualModLink> virtualLinks)
		{
			bool booNoFileLink = true;
			using (SQLiteCommand linkCommand = connection.CreateCommand())
			{
				linkCommand.CommandText = "SELECT RealPath, VirtualPath, Priority, IsActive FROM VirtualLinks WHERE VirtualModId = @modKey ORDER BY LinkOrder;";
				linkCommand.Parameters.AddWithValue("@modKey", modKey);

				using (SQLiteDataReader linkReader = linkCommand.ExecuteReader())
				{
					while (linkReader.Read())
					{
						if (booNoFileLink)
						{
							booNoFileLink = false;
							virtualMods.Add(modInfo);
						}

						virtualLinks.Add(new VirtualModLink(GetString(linkReader, 0), GetString(linkReader, 1), linkReader.GetInt32(2), linkReader.GetInt32(3) != 0, modInfo));
					}
				}
			}
		}

		private static IList<ModRecord> BuildReferenceRecords(IEnumerable<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			Dictionary<IVirtualModInfo, List<IVirtualModLink>> linksByModInfo = BuildVirtualLinksByModInfo(virtualModLink);
			List<ModRecord> records = new List<ModRecord>();

			foreach (IVirtualModInfo modInfo in virtualModInfo)
			{
				records.Add(new ModRecord(modInfo, GetVirtualLinksForMod(linksByModInfo, modInfo).ToList()));
			}

			return records;
		}

		private static IList<ModRecord> BuildMatchingRecords(IEnumerable<IVirtualModInfo> virtualModInfo, IEnumerable<IVirtualModLink> virtualModLink)
		{
			List<IVirtualModLink> links = new List<IVirtualModLink>(virtualModLink);
			List<ModRecord> records = new List<ModRecord>();

			foreach (IVirtualModInfo modInfo in virtualModInfo)
			{
				records.Add(new ModRecord(modInfo, links.Where(x => CheckModInfo(x.ModInfo, modInfo) == true).ToList()));
			}

			return records;
		}

		private static Dictionary<IVirtualModInfo, List<IVirtualModLink>> BuildVirtualLinksByModInfo(IEnumerable<IVirtualModLink> virtualModLink)
		{
			Dictionary<IVirtualModInfo, List<IVirtualModLink>> dicVirtualLinksByModInfo = new Dictionary<IVirtualModInfo, List<IVirtualModLink>>(VirtualModInfoReferenceComparer.Instance);

			foreach (IVirtualModLink link in virtualModLink)
			{
				if (link.ModInfo == null)
					continue;

				List<IVirtualModLink> lstVirtualLinks;
				if (!dicVirtualLinksByModInfo.TryGetValue(link.ModInfo, out lstVirtualLinks))
				{
					lstVirtualLinks = new List<IVirtualModLink>();
					dicVirtualLinksByModInfo.Add(link.ModInfo, lstVirtualLinks);
				}

				lstVirtualLinks.Add(link);
			}

			return dicVirtualLinksByModInfo;
		}

		private static IEnumerable<IVirtualModLink> GetVirtualLinksForMod(Dictionary<IVirtualModInfo, List<IVirtualModLink>> p_dicVirtualLinksByModInfo, IVirtualModInfo p_vmiModInfo)
		{
			List<IVirtualModLink> lstVirtualLinks;
			if (p_dicVirtualLinksByModInfo.TryGetValue(p_vmiModInfo, out lstVirtualLinks))
				return lstVirtualLinks;

			return Enumerable.Empty<IVirtualModLink>();
		}

		private static bool CheckModInfo(IVirtualModInfo p_vmiA, IVirtualModInfo p_vmiB)
		{
			if (!string.IsNullOrEmpty(p_vmiA.DownloadId) && !string.IsNullOrEmpty(p_vmiB.DownloadId))
				if (p_vmiA.DownloadId.Equals(p_vmiB.DownloadId, StringComparison.OrdinalIgnoreCase))
					return true;

			if (!string.IsNullOrEmpty(p_vmiA.ModFileName) && !string.IsNullOrEmpty(p_vmiB.ModFileName))
				if (p_vmiA.ModFileName.Equals(p_vmiB.ModFileName, StringComparison.OrdinalIgnoreCase))
					return true;

			return false;
		}

		private long InsertMod(SQLiteConnection connection, SQLiteTransaction transaction, int modOrder, IVirtualModInfo modInfo)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "INSERT INTO VirtualMods (ModOrder, ModId, DownloadId, UpdatedDownloadId, ModName, ModFileName, ModNewFileName, ModFilePath, FileVersion) VALUES (@modOrder, @modId, @downloadId, @updatedDownloadId, @modName, @modFileName, @modNewFileName, @modFilePath, @fileVersion); SELECT last_insert_rowid();";
				command.Parameters.AddWithValue("@modOrder", modOrder);
				command.Parameters.AddWithValue("@modId", OptionalString(modInfo.ModId));
				command.Parameters.AddWithValue("@downloadId", OptionalString(modInfo.DownloadId));
				command.Parameters.AddWithValue("@updatedDownloadId", OptionalString(modInfo.UpdatedDownloadId));
				command.Parameters.AddWithValue("@modName", RequiredString(modInfo.ModName, "modName"));
				command.Parameters.AddWithValue("@modFileName", RequiredString(modInfo.ModFileName, "modFileName"));
				command.Parameters.AddWithValue("@modNewFileName", OptionalString(modInfo.NewFileName));
				command.Parameters.AddWithValue("@modFilePath", RequiredString(modInfo.ModFilePath, "modFilePath"));
				command.Parameters.AddWithValue("@fileVersion", OptionalString(modInfo.FileVersion));

				return Convert.ToInt64(command.ExecuteScalar());
			}
		}

		private static void InsertLink(SQLiteConnection connection, SQLiteTransaction transaction, long modKey, int linkOrder, IVirtualModLink link)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "INSERT INTO VirtualLinks (VirtualModId, LinkOrder, RealPath, VirtualPath, Priority, IsActive) VALUES (@virtualModId, @linkOrder, @realPath, @virtualPath, @priority, @isActive);";
				command.Parameters.AddWithValue("@virtualModId", modKey);
				command.Parameters.AddWithValue("@linkOrder", linkOrder);
				command.Parameters.AddWithValue("@realPath", RequiredString(link.RealModPath, "realPath"));
				command.Parameters.AddWithValue("@virtualPath", RequiredString(link.VirtualModPath, "virtualPath"));
				command.Parameters.AddWithValue("@priority", link.Priority);
				command.Parameters.AddWithValue("@isActive", link.Active ? 1 : 0);
				command.ExecuteNonQuery();
			}
		}

		private void EnsureDatabaseDirectory()
		{
			string directory = Path.GetDirectoryName(m_strDatabasePath);
			if (!Directory.Exists(directory))
				Directory.CreateDirectory(directory);
		}

		private SQLiteConnection OpenConnection()
		{
			SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder
			{
				DataSource = m_strDatabasePath,
				ForeignKeys = true,
				JournalMode = SQLiteJournalModeEnum.Delete
			};

			SQLiteConnection connection = new SQLiteConnection(builder.ConnectionString);
			connection.Open();
			return connection;
		}

		private SQLiteConnection OpenReadOnlyConnection()
		{
			SQLiteConnectionStringBuilder builder = new SQLiteConnectionStringBuilder
			{
				DataSource = m_strDatabasePath,
				ForeignKeys = true,
				JournalMode = SQLiteJournalModeEnum.Delete,
				ReadOnly = true,
				FailIfMissing = true
			};

			SQLiteConnection connection = new SQLiteConnection(builder.ConnectionString);
			connection.Open();
			return connection;
		}

		private static void EnsureSchema(SQLiteConnection connection)
		{
			ExecuteNonQuery(connection, null, "CREATE TABLE IF NOT EXISTS StoreMetadata (Key TEXT NOT NULL PRIMARY KEY, Value TEXT NOT NULL);");
			ExecuteNonQuery(connection, null, "CREATE TABLE IF NOT EXISTS VirtualMods (VirtualModId INTEGER PRIMARY KEY AUTOINCREMENT, ModOrder INTEGER NOT NULL, ModId TEXT NOT NULL, DownloadId TEXT NOT NULL, UpdatedDownloadId TEXT NOT NULL, ModName TEXT NOT NULL, ModFileName TEXT NOT NULL, ModNewFileName TEXT NOT NULL, ModFilePath TEXT NOT NULL, FileVersion TEXT NOT NULL);");
			ExecuteNonQuery(connection, null, "CREATE TABLE IF NOT EXISTS VirtualLinks (VirtualLinkId INTEGER PRIMARY KEY AUTOINCREMENT, VirtualModId INTEGER NOT NULL, LinkOrder INTEGER NOT NULL, RealPath TEXT NOT NULL, VirtualPath TEXT NOT NULL, Priority INTEGER NOT NULL, IsActive INTEGER NOT NULL, FOREIGN KEY(VirtualModId) REFERENCES VirtualMods(VirtualModId) ON DELETE CASCADE);");
			ExecuteNonQuery(connection, null, "CREATE INDEX IF NOT EXISTS IX_VirtualMods_ModOrder ON VirtualMods(ModOrder);");
			ExecuteNonQuery(connection, null, "CREATE INDEX IF NOT EXISTS IX_VirtualLinks_ModOrder ON VirtualLinks(VirtualModId, LinkOrder);");

			string schemaVersion = GetMetadata(connection, "schema_version");
			if (string.IsNullOrEmpty(schemaVersion))
				SetMetadata(connection, null, "schema_version", SCHEMA_VERSION.ToString());
			else if (!string.Equals(schemaVersion, SCHEMA_VERSION.ToString(), StringComparison.Ordinal))
				throw new InvalidOperationException(string.Format("Unsupported virtual mod SQLite schema version: {0}", schemaVersion));
		}

		private static bool HasCompatibleSchema(SQLiteConnection connection, Func<Version, bool> isValidVersion)
		{
			if (!HasTable(connection, "StoreMetadata") || !HasTable(connection, "VirtualMods") || !HasTable(connection, "VirtualLinks"))
				return false;

			if (!HasColumns(connection, "StoreMetadata", new[] { "Key", "Value" }))
				return false;

			if (!HasColumns(connection, "VirtualMods", new[] { "VirtualModId", "ModOrder", "ModId", "DownloadId", "UpdatedDownloadId", "ModName", "ModFileName", "ModNewFileName", "ModFilePath", "FileVersion" }))
				return false;

			if (!HasColumns(connection, "VirtualLinks", new[] { "VirtualLinkId", "VirtualModId", "LinkOrder", "RealPath", "VirtualPath", "Priority", "IsActive" }))
				return false;

			string schemaVersion = GetMetadata(connection, "schema_version");
			if (!string.Equals(schemaVersion, SCHEMA_VERSION.ToString(), StringComparison.Ordinal))
				return false;

			string fileVersion = GetMetadata(connection, "file_version");
			Version version;
			if (string.IsNullOrEmpty(fileVersion) || !Version.TryParse(fileVersion, out version))
				return false;

			return isValidVersion(version);
		}

		private static bool HasTable(SQLiteConnection connection, string tableName)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
				command.Parameters.AddWithValue("@name", tableName);
				return Convert.ToInt32(command.ExecuteScalar()) == 1;
			}
		}

		private static bool HasColumns(SQLiteConnection connection, string tableName, IEnumerable<string> requiredColumns)
		{
			HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = "PRAGMA table_info(" + tableName + ");";
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						columns.Add(reader.GetString(1));
					}
				}
			}

			foreach (string column in requiredColumns)
			{
				if (!columns.Contains(column))
					return false;
			}

			return true;
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
				command.CommandText = "SELECT Value FROM StoreMetadata WHERE Key = @key;";
				command.Parameters.AddWithValue("@key", key);
				object value = command.ExecuteScalar();
				return value == null ? null : value.ToString();
			}
		}

		private static void SetMetadata(SQLiteConnection connection, SQLiteTransaction transaction, string key, string value)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "INSERT OR REPLACE INTO StoreMetadata (Key, Value) VALUES (@key, @value);";
				command.Parameters.AddWithValue("@key", key);
				command.Parameters.AddWithValue("@value", value);
				command.ExecuteNonQuery();
			}
		}

		private bool IsMainStorePath(string filePath)
		{
			return string.Equals(Path.GetFullPath(filePath), m_strXmlFilePath, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(Path.GetFullPath(filePath), m_strDatabasePath, StringComparison.OrdinalIgnoreCase);
		}

		private static string GetString(IDataRecord reader, int index)
		{
			return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
		}

		private static string OptionalString(string value)
		{
			return value ?? string.Empty;
		}

		private static string RequiredString(string value, string name)
		{
			if (value == null)
				throw new ArgumentNullException(name);

			return value;
		}

		private sealed class ModRecord
		{
			public ModRecord(IVirtualModInfo modInfo, List<IVirtualModLink> links)
			{
				ModInfo = modInfo;
				Links = links;
			}

			public IVirtualModInfo ModInfo { get; private set; }

			public List<IVirtualModLink> Links { get; private set; }
		}

		private sealed class SQLiteLoadResult
		{
			public SQLiteLoadResult(VirtualModStoreData data, bool hasStoredMods)
			{
				Data = data;
				HasStoredMods = hasStoredMods;
			}

			public VirtualModStoreData Data { get; private set; }

			public bool HasStoredMods { get; private set; }
		}

		private sealed class VirtualModInfoReferenceComparer : IEqualityComparer<IVirtualModInfo>
		{
			public static readonly VirtualModInfoReferenceComparer Instance = new VirtualModInfoReferenceComparer();

			private VirtualModInfoReferenceComparer()
			{
			}

			public bool Equals(IVirtualModInfo p_vmiA, IVirtualModInfo p_vmiB)
			{
				return ReferenceEquals(p_vmiA, p_vmiB);
			}

			public int GetHashCode(IVirtualModInfo p_vmiModInfo)
			{
				return RuntimeHelpers.GetHashCode(p_vmiModInfo);
			}
		}
	}
}
