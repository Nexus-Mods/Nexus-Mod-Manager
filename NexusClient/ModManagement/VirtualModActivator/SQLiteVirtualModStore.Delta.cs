using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace Nexus.Client.ModManagement
{
	internal sealed partial class SQLiteVirtualModStore
	{
		private readonly Dictionary<IVirtualModInfo, long> _modIds = new Dictionary<IVirtualModInfo, long>(NativeReferenceComparer<IVirtualModInfo>.Instance);
		private readonly Dictionary<IVirtualModLink, long> _linkIds = new Dictionary<IVirtualModLink, long>(NativeReferenceComparer<IVirtualModLink>.Instance);
		private long _generation;
		/// <summary>Validates the authoritative database version without depending on a stale XML shadow.</summary>
		internal static Version ReadDatabaseVersion(string xmlPath)
		{
			string path = GetDefaultDatabasePath(xmlPath);
			NativeStoreAuthority.RequireAvailable(path);
			var store = new SQLiteVirtualModStore(xmlPath, path, VirtualModActivator.CurrentVersion, x => true, new XmlVirtualModStore());
			using (SQLiteConnection connection = store.OpenReadOnlyConnection())
			{
				string schema = GetMetadata(connection, "schema_version");
				if (schema != SCHEMA_VERSION.ToString() && schema != LEGACY_SCHEMA_VERSION.ToString())
					throw new InvalidDataException("Unsupported virtual mod database schema.");
				return new Version(GetMetadata(connection, "file_version"));
			}
		}

		/// <summary>Recognizes a native reload by its loaded record identities; profiles require a bulk import.</summary>
		internal bool HasLoadedTopology(IList<IVirtualModInfo> mods, IList<IVirtualModLink> links)
		{
			return mods.Count == _modIds.Count && links.Count == _linkIds.Count && mods.All(_modIds.ContainsKey) && links.All(_linkIds.ContainsKey);
		}

		/// <summary>Writes changed mod/link records using stable keys without reloading the complete database.</summary>
		internal void SaveDelta(Version version, VirtualModStoreDelta delta)
		{
			if (delta.RequiresSnapshot)
				throw new InvalidOperationException("A full profile import requires snapshot persistence.");
			if (delta.Mods.Count == 0 && delta.RemovedMods.Count == 0 && delta.Links.Count == 0 && delta.RemovedLinks.Count == 0)
				return;
			NativeStoreAuthority.Mark(m_strDatabasePath);
			lock (m_objDatabaseAccessGate)
				using (SQLiteConnection connection = OpenConnection())
				using (SQLiteTransaction transaction = connection.BeginTransaction())
				{
					long generation = ReadGeneration(connection, transaction);
					if (generation != _generation)
						throw new InvalidOperationException("The virtual mod database changed externally. Reload native state before saving.");
					var newMods = new Dictionary<IVirtualModInfo, long>(NativeReferenceComparer<IVirtualModInfo>.Instance);
					var newLinks = new Dictionary<IVirtualModLink, long>(NativeReferenceComparer<IVirtualModLink>.Instance);
					foreach (IVirtualModLink link in delta.RemovedLinks)
					{
						long id;
						if (_linkIds.TryGetValue(link, out id))
							DeleteLink(connection, transaction, id);
					}

					foreach (IVirtualModInfo mod in delta.Mods.Concat(delta.Links.Where(x => x.ModInfo != null).Select(x => x.ModInfo)).Distinct(NativeReferenceComparer<IVirtualModInfo>.Instance).OrderBy(x => delta.AppendedMods.ContainsKey(x) ? delta.AppendedMods[x] : 0))
					{
						if (delta.RemovedMods.Contains(mod))
							continue;
						long id;
						if (!_modIds.TryGetValue(mod, out id))
						{
							int order = Convert.ToInt32(DeltaScalar(connection, transaction, "SELECT COALESCE(MAX(ModOrder)+1,0) FROM VirtualMods"));
							id = InsertMod(connection, transaction, order, mod);
							newMods.Add(mod, id);
						}
						else
						{
							DeltaExecute(connection, transaction, "UPDATE VirtualMods SET ModId=@p1,DownloadId=@p2,UpdatedDownloadId=@p3,ModName=@p4,ModFileName=@p5,ModNewFileName=@p6,ModFilePath=@p7,FileVersion=@p8 WHERE VirtualModId=@p0", id, OptionalString(mod.ModId), OptionalString(mod.DownloadId), OptionalString(mod.UpdatedDownloadId), RequiredString(mod.ModName, "modName"), RequiredString(mod.ModFileName, "modFileName"), OptionalString(mod.NewFileName), RequiredString(mod.ModFilePath, "modFilePath"), OptionalString(mod.FileVersion));
							if (delta.AppendedMods.ContainsKey(mod))
								DeltaExecute(connection, transaction, "UPDATE VirtualMods SET ModOrder=(SELECT COALESCE(MAX(ModOrder)+1,0) FROM VirtualMods) WHERE VirtualModId=@p0", id);
						}
					}

					foreach (IVirtualModLink link in delta.Links.OrderBy(x => delta.AppendedLinks.ContainsKey(x) ? delta.AppendedLinks[x] : 0))
					{
						long modId, id;
						if (link.ModInfo == null || delta.RemovedMods.Contains(link.ModInfo))
							continue;
						if (!newMods.TryGetValue(link.ModInfo, out modId) && !_modIds.TryGetValue(link.ModInfo, out modId))
							throw new InvalidDataException("A changed virtual link has no persisted mod identity.");
						bool existing = _linkIds.TryGetValue(link, out id);
						int order = !existing || delta.AppendedLinks.ContainsKey(link)
							? Convert.ToInt32(DeltaScalar(connection, transaction, "SELECT COALESCE(MAX(LinkOrder)+1,0) FROM VirtualLinks WHERE VirtualModId=@p0", modId)) : 0;
						if (!existing)
						{
							InsertLink(connection, transaction, modId, order, link);
							id = Convert.ToInt64(DeltaScalar(connection, transaction, "SELECT last_insert_rowid()"));
							newLinks.Add(link, id);
						}
						else
						{
							DeltaExecute(connection, transaction, "UPDATE VirtualLinks SET VirtualModId=@p1,RealPath=@p2,VirtualPath=@p3,Priority=@p4,IsActive=@p5,InstallRoot=@p6 WHERE VirtualLinkId=@p0", id, modId, RequiredString(link.RealModPath, "realPath"), RequiredString(link.VirtualModPath, "virtualPath"), link.Priority, link.Active ? 1 : 0, (int)NormalizeInstallRoot(link.InstallRoot));
							if (delta.AppendedLinks.ContainsKey(link))
								DeltaExecute(connection, transaction, "UPDATE VirtualLinks SET LinkOrder=@p1 WHERE VirtualLinkId=@p0", id, order);
						}
					}

					foreach (IVirtualModInfo mod in delta.RemovedMods)
					{
						long id;
						if (_modIds.TryGetValue(mod, out id))
							DeleteMod(connection, transaction, id);
					}

					SetMetadata(connection, transaction, "generation", (generation + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
					SetMetadata(connection, transaction, "file_version", version.ToString());
					transaction.Commit();
					foreach (IVirtualModLink link in delta.RemovedLinks)
						_linkIds.Remove(link);
					foreach (IVirtualModInfo mod in delta.RemovedMods)
						_modIds.Remove(mod);
					foreach (var pair in newMods)
						_modIds[pair.Key] = pair.Value;
					foreach (var pair in newLinks)
						_linkIds[pair.Key] = pair.Value;
					_generation = generation + 1;
				}
		}

		/// <summary>Rebinds identities after an explicit bulk import; ordinary delta saves never call this scan.</summary>
		private void RememberSnapshot(SQLiteConnection connection, IList<ModRecord> records)
		{
			List<PersistedModRecord> persisted = LoadPersistedRecords(connection, null);
			PersistedModRecord[] matches = MatchStoredRecords(persisted, records);
			_modIds.Clear();
			_linkIds.Clear();
			for (int i = 0; i < records.Count; i++)
			{
				PersistedModRecord match = matches[i];
				if (match == null)
					throw new InvalidDataException("Imported virtual mod identity was not persisted.");
				_modIds[records[i].ModInfo] = match.ModKey;
				for (int j = 0; j < records[i].Links.Count; j++)
					_linkIds[records[i].Links[j]] = match.Links[j].LinkKey;
			}

			_generation = ReadGeneration(connection, null);
		}

		/// <summary>Reads the committed generation with one metadata lookup.</summary>
		private static long ReadGeneration(SQLiteConnection connection, SQLiteTransaction transaction)
		{
			object value = DeltaScalar(connection, transaction, "SELECT Value FROM StoreMetadata WHERE Key='generation'");
			return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
		}

		/// <summary>Creates one parameterized delta command.</summary>
		private static SQLiteCommand DeltaCommand(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params object[] values)
		{
			SQLiteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = sql;
			for (int i = 0; i < values.Length; i++)
				command.Parameters.AddWithValue("@p" + i, values[i] ?? DBNull.Value);
			return command;
		}

		/// <summary>Executes an indexed mutation.</summary>
		private static void DeltaExecute(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params object[] values)
		{
			using (SQLiteCommand command = DeltaCommand(connection, transaction, sql, values))
				command.ExecuteNonQuery();
		}

		/// <summary>Reads an indexed scalar without loading unrelated records.</summary>
		private static object DeltaScalar(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params object[] values)
		{
			using (SQLiteCommand command = DeltaCommand(connection, transaction, sql, values))
				return command.ExecuteScalar();
		}
	}
}
