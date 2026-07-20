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
			Stopwatch shadowWatch = Stopwatch.StartNew();
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
			finally
			{
				shadowWatch.Stop();
				Trace.TraceInformation("Virtual mod XML compatibility shadow completed in {0}ms.", shadowWatch.ElapsedMilliseconds);
			}
		}

		private void SaveRecords(Version fileVersion, IList<ModRecord> records)
		{
			EnsureDatabaseDirectory();
			Stopwatch syncWatch = Stopwatch.StartNew();
			StoreSyncStatistics statistics = new StoreSyncStatistics();

			using (SQLiteConnection connection = OpenConnection())
			{
				EnsureSchema(connection);
				using (SQLiteTransaction transaction = connection.BeginTransaction())
				{
					List<PersistedModRecord> storedRecords = LoadPersistedRecords(connection, transaction);
					SynchronizeRecords(connection, transaction, storedRecords, records, statistics);
					SetMetadata(connection, transaction, "schema_version", SCHEMA_VERSION.ToString());
					SetMetadata(connection, transaction, "file_version", fileVersion.ToString());
					transaction.Commit();
				}
			}

			syncWatch.Stop();
			Trace.TraceInformation(
				"Virtual mod SQLite synchronization completed in {0}ms. Mods: +{1}/~{2}/-{3}; Links: +{4}/~{5}/-{6}.",
				syncWatch.ElapsedMilliseconds,
				statistics.InsertedMods,
				statistics.UpdatedMods,
				statistics.DeletedMods,
				statistics.InsertedLinks,
				statistics.UpdatedLinks,
				statistics.DeletedLinks);
		}

		private static void SynchronizeRecords(SQLiteConnection connection, SQLiteTransaction transaction, IList<PersistedModRecord> storedRecords, IList<ModRecord> records, StoreSyncStatistics statistics)
		{
			PersistedModRecord[] matchedRecords = MatchStoredRecords(storedRecords, records);
			HashSet<long> matchedModKeys = new HashSet<long>();

			for (int incomingIndex = 0; incomingIndex < matchedRecords.Length; incomingIndex++)
			{
				if (matchedRecords[incomingIndex] != null)
					matchedModKeys.Add(matchedRecords[incomingIndex].ModKey);
			}

			for (int storedIndex = 0; storedIndex < storedRecords.Count; storedIndex++)
			{
				PersistedModRecord storedRecord = storedRecords[storedIndex];
				if (matchedModKeys.Contains(storedRecord.ModKey))
					continue;

				DeleteMod(connection, transaction, storedRecord.ModKey);
				statistics.DeletedMods++;
				statistics.DeletedLinks += storedRecord.Links.Count;
			}

			for (int incomingIndex = 0; incomingIndex < records.Count; incomingIndex++)
			{
				ModRecord incomingRecord = records[incomingIndex];
				PersistedModRecord storedRecord = matchedRecords[incomingIndex];

				if (storedRecord == null)
				{
					long modKey = InsertMod(connection, transaction, incomingIndex, incomingRecord.ModInfo);
					statistics.InsertedMods++;

					for (int linkIndex = 0; linkIndex < incomingRecord.Links.Count; linkIndex++)
					{
						InsertLink(connection, transaction, modKey, linkIndex, incomingRecord.Links[linkIndex]);
						statistics.InsertedLinks++;
					}

					continue;
				}

				if (UpdateModIfChanged(connection, transaction, storedRecord, incomingIndex, incomingRecord.ModInfo))
					statistics.UpdatedMods++;

				SynchronizeLinks(connection, transaction, storedRecord, incomingRecord.Links, statistics);
			}
		}

		private static PersistedModRecord[] MatchStoredRecords(IList<PersistedModRecord> storedRecords, IList<ModRecord> records)
		{
			PersistedModRecord[] matches = new PersistedModRecord[records.Count];
			HashSet<long> matchedModKeys = new HashSet<long>();
			Dictionary<string, Queue<PersistedModRecord>> storedByFileName = new Dictionary<string, Queue<PersistedModRecord>>(StringComparer.OrdinalIgnoreCase);

			for (int storedIndex = 0; storedIndex < storedRecords.Count; storedIndex++)
			{
				PersistedModRecord storedRecord = storedRecords[storedIndex];
				if (String.IsNullOrWhiteSpace(storedRecord.ModFileName))
					continue;

				Queue<PersistedModRecord> matchingRecords;
				if (!storedByFileName.TryGetValue(storedRecord.ModFileName.Trim(), out matchingRecords))
				{
					matchingRecords = new Queue<PersistedModRecord>();
					storedByFileName.Add(storedRecord.ModFileName.Trim(), matchingRecords);
				}

				matchingRecords.Enqueue(storedRecord);
			}

			for (int incomingIndex = 0; incomingIndex < records.Count; incomingIndex++)
			{
				string modFileName = records[incomingIndex].ModInfo.ModFileName;
				Queue<PersistedModRecord> matchingRecords;
				if (String.IsNullOrWhiteSpace(modFileName)
					|| !storedByFileName.TryGetValue(modFileName.Trim(), out matchingRecords)
					|| matchingRecords.Count == 0)
				{
					continue;
				}

				PersistedModRecord match = matchingRecords.Dequeue();
				matches[incomingIndex] = match;
				matchedModKeys.Add(match.ModKey);
			}

			Dictionary<string, List<PersistedModRecord>> storedByDownloadId = new Dictionary<string, List<PersistedModRecord>>(StringComparer.OrdinalIgnoreCase);
			for (int storedIndex = 0; storedIndex < storedRecords.Count; storedIndex++)
			{
				PersistedModRecord storedRecord = storedRecords[storedIndex];
				if (matchedModKeys.Contains(storedRecord.ModKey) || String.IsNullOrWhiteSpace(storedRecord.DownloadId))
					continue;

				List<PersistedModRecord> matchingRecords;
				if (!storedByDownloadId.TryGetValue(storedRecord.DownloadId.Trim(), out matchingRecords))
				{
					matchingRecords = new List<PersistedModRecord>();
					storedByDownloadId.Add(storedRecord.DownloadId.Trim(), matchingRecords);
				}

				matchingRecords.Add(storedRecord);
			}

			Dictionary<string, List<int>> incomingByDownloadId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
			for (int incomingIndex = 0; incomingIndex < records.Count; incomingIndex++)
			{
				if (matches[incomingIndex] != null)
					continue;

				string downloadId = records[incomingIndex].ModInfo.DownloadId;
				if (String.IsNullOrWhiteSpace(downloadId))
					continue;

				List<int> matchingIndexes;
				if (!incomingByDownloadId.TryGetValue(downloadId.Trim(), out matchingIndexes))
				{
					matchingIndexes = new List<int>();
					incomingByDownloadId.Add(downloadId.Trim(), matchingIndexes);
				}

				matchingIndexes.Add(incomingIndex);
			}

			foreach (KeyValuePair<string, List<int>> pair in incomingByDownloadId)
			{
				List<PersistedModRecord> matchingRecords;
				if (pair.Value.Count != 1
					|| !storedByDownloadId.TryGetValue(pair.Key, out matchingRecords)
					|| matchingRecords.Count != 1)
				{
					continue;
				}

				matches[pair.Value[0]] = matchingRecords[0];
			}

			return matches;
		}

		private static void SynchronizeLinks(SQLiteConnection connection, SQLiteTransaction transaction, PersistedModRecord storedRecord, IList<IVirtualModLink> incomingLinks, StoreSyncStatistics statistics)
		{
			Dictionary<string, Queue<PersistedLinkRecord>> storedByIdentity = new Dictionary<string, Queue<PersistedLinkRecord>>(StringComparer.OrdinalIgnoreCase);
			for (int storedIndex = 0; storedIndex < storedRecord.Links.Count; storedIndex++)
			{
				PersistedLinkRecord storedLink = storedRecord.Links[storedIndex];
				string identity = BuildLinkIdentity(storedLink.RealPath, storedLink.VirtualPath);
				Queue<PersistedLinkRecord> matchingLinks;
				if (!storedByIdentity.TryGetValue(identity, out matchingLinks))
				{
					matchingLinks = new Queue<PersistedLinkRecord>();
					storedByIdentity.Add(identity, matchingLinks);
				}

				matchingLinks.Enqueue(storedLink);
			}

			HashSet<long> matchedLinkKeys = new HashSet<long>();
			for (int incomingIndex = 0; incomingIndex < incomingLinks.Count; incomingIndex++)
			{
				IVirtualModLink incomingLink = incomingLinks[incomingIndex];
				Queue<PersistedLinkRecord> matchingLinks;
				if (!storedByIdentity.TryGetValue(BuildLinkIdentity(incomingLink.RealModPath, incomingLink.VirtualModPath), out matchingLinks)
					|| matchingLinks.Count == 0)
				{
					InsertLink(connection, transaction, storedRecord.ModKey, incomingIndex, incomingLink);
					statistics.InsertedLinks++;
					continue;
				}

				PersistedLinkRecord storedLink = matchingLinks.Dequeue();
				matchedLinkKeys.Add(storedLink.LinkKey);
				if (UpdateLinkIfChanged(connection, transaction, storedLink, incomingIndex, incomingLink))
					statistics.UpdatedLinks++;
			}

			for (int storedIndex = 0; storedIndex < storedRecord.Links.Count; storedIndex++)
			{
				PersistedLinkRecord storedLink = storedRecord.Links[storedIndex];
				if (matchedLinkKeys.Contains(storedLink.LinkKey))
					continue;

				DeleteLink(connection, transaction, storedLink.LinkKey);
				statistics.DeletedLinks++;
			}
		}

		private static List<PersistedModRecord> LoadPersistedRecords(SQLiteConnection connection, SQLiteTransaction transaction)
		{
			List<PersistedModRecord> records = new List<PersistedModRecord>();
			Dictionary<long, PersistedModRecord> recordsByKey = new Dictionary<long, PersistedModRecord>();

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT VirtualModId, ModOrder, ModId, DownloadId, UpdatedDownloadId, ModName, ModFileName, ModNewFileName, ModFilePath, FileVersion FROM VirtualMods ORDER BY ModOrder;";
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						PersistedModRecord record = new PersistedModRecord(
							reader.GetInt64(0),
							reader.GetInt32(1),
							GetString(reader, 2),
							GetString(reader, 3),
							GetString(reader, 4),
							GetString(reader, 5),
							GetString(reader, 6),
							GetString(reader, 7),
							GetString(reader, 8),
							GetString(reader, 9));

						records.Add(record);
						recordsByKey.Add(record.ModKey, record);
					}
				}
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT VirtualLinkId, VirtualModId, LinkOrder, RealPath, VirtualPath, Priority, IsActive FROM VirtualLinks ORDER BY VirtualModId, LinkOrder;";
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						PersistedModRecord record;
						if (!recordsByKey.TryGetValue(reader.GetInt64(1), out record))
							throw new InvalidOperationException("Virtual link references a missing virtual mod.");

						record.Links.Add(new PersistedLinkRecord(
							reader.GetInt64(0),
							reader.GetInt32(2),
							GetString(reader, 3),
							GetString(reader, 4),
							reader.GetInt32(5),
							reader.GetInt32(6) != 0));
					}
				}
			}

			return records;
		}

		private static bool UpdateModIfChanged(SQLiteConnection connection, SQLiteTransaction transaction, PersistedModRecord storedRecord, int modOrder, IVirtualModInfo modInfo)
		{
			string modId = OptionalString(modInfo.ModId);
			string downloadId = OptionalString(modInfo.DownloadId);
			string updatedDownloadId = OptionalString(modInfo.UpdatedDownloadId);
			string modName = RequiredString(modInfo.ModName, "modName");
			string modFileName = RequiredString(modInfo.ModFileName, "modFileName");
			string modNewFileName = OptionalString(modInfo.NewFileName);
			string modFilePath = RequiredString(modInfo.ModFilePath, "modFilePath");
			string fileVersion = OptionalString(modInfo.FileVersion);

			if (storedRecord.ModOrder == modOrder
				&& String.Equals(storedRecord.ModId, modId, StringComparison.Ordinal)
				&& String.Equals(storedRecord.DownloadId, downloadId, StringComparison.Ordinal)
				&& String.Equals(storedRecord.UpdatedDownloadId, updatedDownloadId, StringComparison.Ordinal)
				&& String.Equals(storedRecord.ModName, modName, StringComparison.Ordinal)
				&& String.Equals(storedRecord.ModFileName, modFileName, StringComparison.Ordinal)
				&& String.Equals(storedRecord.ModNewFileName, modNewFileName, StringComparison.Ordinal)
				&& String.Equals(storedRecord.ModFilePath, modFilePath, StringComparison.Ordinal)
				&& String.Equals(storedRecord.FileVersion, fileVersion, StringComparison.Ordinal))
			{
				return false;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "UPDATE VirtualMods SET ModOrder = @modOrder, ModId = @modId, DownloadId = @downloadId, UpdatedDownloadId = @updatedDownloadId, ModName = @modName, ModFileName = @modFileName, ModNewFileName = @modNewFileName, ModFilePath = @modFilePath, FileVersion = @fileVersion WHERE VirtualModId = @virtualModId;";
				command.Parameters.AddWithValue("@modOrder", modOrder);
				command.Parameters.AddWithValue("@modId", modId);
				command.Parameters.AddWithValue("@downloadId", downloadId);
				command.Parameters.AddWithValue("@updatedDownloadId", updatedDownloadId);
				command.Parameters.AddWithValue("@modName", modName);
				command.Parameters.AddWithValue("@modFileName", modFileName);
				command.Parameters.AddWithValue("@modNewFileName", modNewFileName);
				command.Parameters.AddWithValue("@modFilePath", modFilePath);
				command.Parameters.AddWithValue("@fileVersion", fileVersion);
				command.Parameters.AddWithValue("@virtualModId", storedRecord.ModKey);
				command.ExecuteNonQuery();
			}

			return true;
		}

		private static bool UpdateLinkIfChanged(SQLiteConnection connection, SQLiteTransaction transaction, PersistedLinkRecord storedLink, int linkOrder, IVirtualModLink link)
		{
			string realPath = RequiredString(link.RealModPath, "realPath");
			string virtualPath = RequiredString(link.VirtualModPath, "virtualPath");
			int isActive = link.Active ? 1 : 0;

			if (storedLink.LinkOrder == linkOrder
				&& String.Equals(storedLink.RealPath, realPath, StringComparison.Ordinal)
				&& String.Equals(storedLink.VirtualPath, virtualPath, StringComparison.Ordinal)
				&& storedLink.Priority == link.Priority
				&& storedLink.IsActive == (isActive != 0))
			{
				return false;
			}

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "UPDATE VirtualLinks SET LinkOrder = @linkOrder, RealPath = @realPath, VirtualPath = @virtualPath, Priority = @priority, IsActive = @isActive WHERE VirtualLinkId = @virtualLinkId;";
				command.Parameters.AddWithValue("@linkOrder", linkOrder);
				command.Parameters.AddWithValue("@realPath", realPath);
				command.Parameters.AddWithValue("@virtualPath", virtualPath);
				command.Parameters.AddWithValue("@priority", link.Priority);
				command.Parameters.AddWithValue("@isActive", isActive);
				command.Parameters.AddWithValue("@virtualLinkId", storedLink.LinkKey);
				command.ExecuteNonQuery();
			}

			return true;
		}

		private static void DeleteMod(SQLiteConnection connection, SQLiteTransaction transaction, long modKey)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "DELETE FROM VirtualMods WHERE VirtualModId = @virtualModId;";
				command.Parameters.AddWithValue("@virtualModId", modKey);
				command.ExecuteNonQuery();
			}
		}

		private static void DeleteLink(SQLiteConnection connection, SQLiteTransaction transaction, long linkKey)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "DELETE FROM VirtualLinks WHERE VirtualLinkId = @virtualLinkId;";
				command.Parameters.AddWithValue("@virtualLinkId", linkKey);
				command.ExecuteNonQuery();
			}
		}

		private static string BuildLinkIdentity(string realPath, string virtualPath)
		{
			return NormalizeIdentityPath(realPath) + "\u001f" + NormalizeIdentityPath(virtualPath);
		}

		private static string NormalizeIdentityPath(string path)
		{
			return (path ?? String.Empty)
				.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
				.Trim();
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

		private static long InsertMod(SQLiteConnection connection, SQLiteTransaction transaction, int modOrder, IVirtualModInfo modInfo)
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

		private sealed class PersistedModRecord
		{
			public PersistedModRecord(long modKey, int modOrder, string modId, string downloadId, string updatedDownloadId, string modName, string modFileName, string modNewFileName, string modFilePath, string fileVersion)
			{
				ModKey = modKey;
				ModOrder = modOrder;
				ModId = modId;
				DownloadId = downloadId;
				UpdatedDownloadId = updatedDownloadId;
				ModName = modName;
				ModFileName = modFileName;
				ModNewFileName = modNewFileName;
				ModFilePath = modFilePath;
				FileVersion = fileVersion;
				Links = new List<PersistedLinkRecord>();
			}

			public long ModKey { get; private set; }
			public int ModOrder { get; private set; }
			public string ModId { get; private set; }
			public string DownloadId { get; private set; }
			public string UpdatedDownloadId { get; private set; }
			public string ModName { get; private set; }
			public string ModFileName { get; private set; }
			public string ModNewFileName { get; private set; }
			public string ModFilePath { get; private set; }
			public string FileVersion { get; private set; }
			public List<PersistedLinkRecord> Links { get; private set; }
		}

		private sealed class PersistedLinkRecord
		{
			public PersistedLinkRecord(long linkKey, int linkOrder, string realPath, string virtualPath, int priority, bool isActive)
			{
				LinkKey = linkKey;
				LinkOrder = linkOrder;
				RealPath = realPath;
				VirtualPath = virtualPath;
				Priority = priority;
				IsActive = isActive;
			}

			public long LinkKey { get; private set; }
			public int LinkOrder { get; private set; }
			public string RealPath { get; private set; }
			public string VirtualPath { get; private set; }
			public int Priority { get; private set; }
			public bool IsActive { get; private set; }
		}

		private sealed class StoreSyncStatistics
		{
			public int InsertedMods { get; set; }
			public int UpdatedMods { get; set; }
			public int DeletedMods { get; set; }
			public int InsertedLinks { get; set; }
			public int UpdatedLinks { get; set; }
			public int DeletedLinks { get; set; }
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
