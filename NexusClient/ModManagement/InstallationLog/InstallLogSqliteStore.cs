using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Nexus.Client.Util;

namespace Nexus.Client.ModManagement.InstallationLog
{
	/// <summary>Persists native ownership as indexed records, retaining XML only at import/export boundaries.</summary>
	internal sealed class InstallLogSqliteStore
	{
		private const string SchemaVersion = "1";
		private readonly string _path;
		private long _generation;
		/// <summary>Creates a store for a database path without creating or modifying it.</summary>
		internal InstallLogSqliteStore(string path)
		{
			_path = Path.GetFullPath(path);
		}

		/// <summary>Resolves the SQLite sibling of a legacy InstallLog path.</summary>
		internal static string DatabasePath(string xmlPath)
		{
			return Path.ChangeExtension(xmlPath, ".sqlite");
		}

		/// <summary>Includes the independent authority marker when detecting an already migrated store.</summary>
		internal static bool IsAuthoritative(string xmlPath)
		{
			string path = DatabasePath(xmlPath);
			return File.Exists(path) || File.Exists(path + ".authority");
		}

		/// <summary>Uses the existing XML parser at startup/export, never during an ordinary commit.</summary>
		internal static XDocument ReadDocument(string xmlPath)
		{
			return IsAuthoritative(xmlPath) ? new InstallLogSqliteStore(DatabasePath(xmlPath)).Read() : XDocument.Load(xmlPath);
		}

		/// <summary>Reads only store metadata when validating native authority.</summary>
		internal Version ReadVersion()
		{
			using (SQLiteConnection connection = Open())
			{
				ValidateSchema(connection);
				return new Version(Convert.ToString(Scalar(connection, null, "SELECT Value FROM Metadata WHERE Key='file_version'"), CultureInfo.InvariantCulture));
			}
		}

		/// <summary>Imports an upgraded legacy log once, publishing the complete database before marking it authoritative.</summary>
		internal static void EnsureMigrated(string xmlPath)
		{
			string path = DatabasePath(xmlPath);
			NativeStoreAuthority.RequireAvailable(path);
			if (File.Exists(path))
			{
				new InstallLogSqliteStore(path).ReadVersion();
				NativeStoreAuthority.Mark(path);
				return;
			}

			if (File.Exists(xmlPath) && !File.Exists(xmlPath + ".pre-sqlite"))
				File.Copy(xmlPath, xmlPath + ".pre-sqlite");
			string temporary = path + "." + FileUtil.CreateTempToken() + ".tmp";
			try
			{
				XDocument source = File.Exists(xmlPath) ? XDocument.Load(xmlPath) : new XDocument(new XElement("installLog", new XAttribute("fileVersion", InstallLog.CurrentVersion), new XElement("modList")));
				CreateFromDocument(temporary, source);
				File.Move(temporary, path);
				NativeStoreAuthority.Mark(path);
			}
			finally
			{
				if (File.Exists(temporary))
					File.Delete(temporary);
			}
		}

		/// <summary>Creates an isolated restore/import database from a complete legacy-format snapshot.</summary>
		internal static void CreateFromDocument(string path, XDocument document)
		{
			if (File.Exists(path))
				throw new IOException("An import database already exists: " + path);
			XElement root = document.Root;
			if (root == null || root.Name != "installLog" || (string)root.Attribute("fileVersion") != InstallLog.CurrentVersion.ToString())
				throw new InvalidDataException("The InstallLog snapshot must be upgraded to the current supported version before importing.");
			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
			var store = new InstallLogSqliteStore(path);
			using (SQLiteConnection connection = store.Open(true))
			using (SQLiteTransaction transaction = connection.BeginTransaction())
			{
				Execute(connection, transaction, @"CREATE TABLE Metadata(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
CREATE TABLE Mods(ModKey TEXT PRIMARY KEY, OriginalKey TEXT NOT NULL, SortOrder INTEGER NOT NULL, Path TEXT NOT NULL, Name TEXT NOT NULL, MachineVersion TEXT NOT NULL, HumanVersion TEXT NOT NULL, InstallDate TEXT NOT NULL, InstallRoot TEXT NULL, InstallMethod TEXT NULL, Hidden INTEGER NOT NULL);
CREATE INDEX IX_Mods_Order ON Mods(SortOrder);
CREATE TABLE Items(ItemId INTEGER PRIMARY KEY, Kind INTEGER NOT NULL, Identity TEXT NOT NULL, Path TEXT NOT NULL, Section TEXT NULL, Name TEXT NULL, Root TEXT NULL, UNIQUE(Kind,Identity));
CREATE TABLE Owners(ItemId INTEGER NOT NULL REFERENCES Items(ItemId) ON DELETE CASCADE, Position INTEGER NOT NULL, ModKey TEXT NOT NULL, OwnerKey TEXT NOT NULL, TextValue TEXT NULL, BinaryValue BLOB NULL, PRIMARY KEY(ItemId,Position));
CREATE INDEX IX_Owners_Mod ON Owners(OwnerKey,ItemId);");
				SetMetadata(connection, transaction, "schema_version", SchemaVersion);
				SetMetadata(connection, transaction, "generation", "0");
				SetMetadata(connection, transaction, "file_version", (string)root.Attribute("fileVersion"));
				SetMetadata(connection, transaction, "deployment_commit_sequence", ((long?)root.Attribute("deploymentCommitSequence") ?? 0).ToString(CultureInfo.InvariantCulture));
				foreach (XElement mod in root.Element("modList")?.Elements("mod") ?? Enumerable.Empty<XElement>())
					WriteMod(connection, transaction, mod);
				foreach (Entry entry in EnumerateEntries(root))
					WriteEntry(connection, transaction, entry);
				transaction.Commit();
			}

			if (!XNode.DeepEquals(Normalize(document), Normalize(store.Read())))
				throw new InvalidDataException("InstallLog import did not preserve the legacy ownership state.");
		}

		/// <summary>Applies only affected records and their ordered owners in the same transaction as the recovery sequence.</summary>
		internal void Apply(IEnumerable<XElement> mods, IEnumerable<string> removedMods, IEnumerable<Entry> entries, long sequence)
		{
			NativeStoreAuthority.Mark(_path);
			using (SQLiteConnection connection = Open())
			using (SQLiteTransaction transaction = connection.BeginTransaction())
			{
				long generation = Convert.ToInt64(Scalar(connection, transaction, "SELECT Value FROM Metadata WHERE Key='generation'"), CultureInfo.InvariantCulture);
				if (generation != _generation)
					throw new InvalidOperationException("The InstallLog database changed externally. Reload native state before saving.");
				foreach (Entry entry in entries)
					WriteEntry(connection, transaction, entry);
				foreach (string key in removedMods)
					Execute(connection, transaction, "DELETE FROM Mods WHERE ModKey=@p0", Canonical(key));
				foreach (XElement mod in mods)
					WriteMod(connection, transaction, mod);
				SetMetadata(connection, transaction, "deployment_commit_sequence", sequence.ToString(CultureInfo.InvariantCulture));
				SetMetadata(connection, transaction, "generation", (generation + 1).ToString(CultureInfo.InvariantCulture));
				transaction.Commit();
				_generation = generation + 1;
			}
		}

		/// <summary>Finds only records owned by a removed mod through the reverse ownership index.</summary>
		internal List<Entry> FindOwnedEntries(IEnumerable<string> modKeys)
		{
			var result = new List<Entry>();
			string[] keys = modKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			if (keys.Length == 0)
				return result;
			using (SQLiteConnection connection = Open())
			{
				foreach (string key in keys)
					using (SQLiteCommand command = Command(connection, null, "SELECT DISTINCT i.Kind,i.Path,i.Section,i.Name,i.Root FROM Owners o JOIN Items i ON i.ItemId=o.ItemId WHERE o.OwnerKey=@p0", Canonical(key)))
					using (SQLiteDataReader reader = command.ExecuteReader())
						while (reader.Read())
							result.Add(new Entry(reader.GetInt32(0), Element(reader.GetInt32(0), reader.GetString(1), Text(reader, 2), Text(reader, 3), Text(reader, 4)), true));
			}

			return result;
		}

		/// <summary>Materializes a consistent committed snapshot for startup or an explicit export.</summary>
		internal XDocument Read(bool registrationsOnly = false)
		{
			using (SQLiteConnection connection = Open())
			using (SQLiteTransaction transaction = connection.BeginTransaction())
			{
				ValidateSchema(connection, transaction);
				if (!registrationsOnly)
					_generation = Convert.ToInt64(Scalar(connection, transaction, "SELECT Value FROM Metadata WHERE Key='generation'"), CultureInfo.InvariantCulture);
				var root = new XElement("installLog", new XAttribute("fileVersion", Convert.ToString(Scalar(connection, transaction, "SELECT Value FROM Metadata WHERE Key='file_version'"), CultureInfo.InvariantCulture)));
				long sequence = Convert.ToInt64(Scalar(connection, transaction, "SELECT Value FROM Metadata WHERE Key='deployment_commit_sequence'"), CultureInfo.InvariantCulture);
				if (sequence > 0)
					root.Add(new XAttribute("deploymentCommitSequence", sequence));
				var mods = new XElement("modList");
				root.Add(mods);
				using (SQLiteCommand command = Command(connection, transaction, "SELECT OriginalKey,Path,Name,MachineVersion,HumanVersion,InstallDate,InstallRoot,InstallMethod,Hidden FROM Mods ORDER BY SortOrder"))
				using (SQLiteDataReader reader = command.ExecuteReader())
					while (reader.Read())
					{
						var mod = new XElement("mod", new XAttribute("key", reader.GetString(0)), new XAttribute("path", reader.GetString(1)), new XElement("version", new XAttribute("machineVersion", reader.GetString(3)), reader.GetString(4)), new XElement("name", reader.GetString(2)), new XElement("installDate", reader.GetString(5)));
						if (!reader.IsDBNull(6))
							mod.Add(new XAttribute("installRoot", reader.GetString(6)));
						if (!reader.IsDBNull(7))
							mod.Add(new XAttribute("installMethod", reader.GetString(7)));
						if (reader.GetInt32(8) != 0)
							mod.Add(new XAttribute("hidden", true));
						mods.Add(mod);
					}

				if (registrationsOnly)
				{
					transaction.Commit();
					return new XDocument(root);
				}

				string[] sections =
				{
					"dataFiles",
					"iniEdits",
					"gameSpecificEdits",
					"deploymentFiles"
				};
				var containers = sections.Select(x => new XElement(x)).ToArray();
				var byId = new Dictionary<long, XElement>();
				using (SQLiteCommand command = Command(connection, transaction, "SELECT ItemId,Kind,Path,Section,Name,Root FROM Items ORDER BY ItemId"))
				using (SQLiteDataReader reader = command.ExecuteReader())
					while (reader.Read())
					{
						int kind = reader.GetInt32(1);
						XElement element = Element(kind, reader.GetString(2), Text(reader, 3), Text(reader, 4), Text(reader, 5));
						element.Add(new XElement("installingMods"));
						containers[kind].Add(element);
						byId.Add(reader.GetInt64(0), element);
					}

				using (SQLiteCommand command = Command(connection, transaction, "SELECT o.ItemId,i.Kind,o.ModKey,o.TextValue,o.BinaryValue FROM Owners o JOIN Items i ON i.ItemId=o.ItemId ORDER BY o.ItemId,o.Position"))
				using (SQLiteDataReader reader = command.ExecuteReader())
					while (reader.Read())
					{
						int kind = reader.GetInt32(1);
						XElement owner = kind == 2
							? InstallLog.SerializeGameSpecificValueOwner(reader.GetString(2), reader.IsDBNull(4) ? null : (byte[])reader.GetValue(4))
							: new XElement("mod", new XAttribute("key", reader.GetString(2)), Text(reader, 3));
						byId[reader.GetInt64(0)].Element("installingMods").Add(owner);
					}

				for (int i = 0; i < containers.Length; i++)
					if (i != 3 || containers[i].HasElements)
						root.Add(containers[i]);
				transaction.Commit();
				return new XDocument(root);
			}
		}

		/// <summary>Copies a committed database through SQLite's backup API without copying a live journal.</summary>
		internal void BackupTo(string destination)
		{
			using (SQLiteConnection source = Open())
			using (var target = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = destination, Enlist = false, Pooling = false }.ConnectionString))
			{
				target.Open();
				source.BackupDatabase(target, "main", "main", -1, null, 0);
			}
		}

		/// <summary>One changed ownership record; a null owner stack denotes deletion.</summary>
		internal sealed class Entry
		{
			internal readonly int Kind;
			internal readonly XElement Value;
			internal readonly bool Deleted;
			/// <summary>Captures a changed record independently from live mutable installer stacks.</summary>
			internal Entry(int kind, XElement value, bool deleted = false)
			{
				Kind = kind;
				Value = value;
				Deleted = deleted;
			}
		}

		/// <summary>Enumerates the four legacy record namespaces without changing owner order.</summary>
		private static IEnumerable<Entry> EnumerateEntries(XElement root)
		{
			string[] sections =
			{
				"dataFiles",
				"iniEdits",
				"gameSpecificEdits",
				"deploymentFiles"
			};
			for (int kind = 0; kind < sections.Length; kind++)
				foreach (XElement element in root.Element(sections[kind])?.Elements() ?? Enumerable.Empty<XElement>())
					yield return new Entry(kind, element);
		}

		/// <summary>Creates the legacy shape used by the existing loader and explicit exports.</summary>
		private static XElement Element(int kind, string path, string section, string name, string root)
		{
			if (kind == 1)
				return new XElement("ini", new XAttribute("file", path), new XAttribute("section", section ?? ""), new XAttribute("key", name ?? ""));
			if (kind == 2)
				return new XElement("edit", new XAttribute("key", path));
			var result = new XElement("file", new XAttribute("path", path));
			if (kind == 3)
				result.Add(new XAttribute("root", root));
			return result;
		}

		/// <summary>Updates one registration without renumbering unrelated mods.</summary>
		private static void WriteMod(SQLiteConnection connection, SQLiteTransaction transaction, XElement mod)
		{
			string key = (string)mod.Attribute("key");
			object[] args =
			{
				Canonical(key),
				key,
				(string)mod.Attribute("path") ?? "",
				(string)mod.Element("name") ?? "",
				(string)mod.Element("version")?.Attribute("machineVersion") ?? "",
				(string)mod.Element("version") ?? "",
				(string)mod.Element("installDate") ?? "<No Data>",
				(string)mod.Attribute("installRoot"),
				(string)mod.Attribute("installMethod"),
				(bool?)mod.Attribute("hidden") == true ? 1 : 0
			};
			Execute(connection, transaction, "INSERT OR IGNORE INTO Mods(ModKey,OriginalKey,SortOrder,Path,Name,MachineVersion,HumanVersion,InstallDate,Hidden) VALUES(@p0,@p1,COALESCE((SELECT MAX(SortOrder)+1 FROM Mods),0),'','','','','',0)", args[0], args[1]);
			Execute(connection, transaction, "UPDATE Mods SET OriginalKey=@p1,Path=@p2,Name=@p3,MachineVersion=@p4,HumanVersion=@p5,InstallDate=@p6,InstallRoot=@p7,InstallMethod=@p8,Hidden=@p9 WHERE ModKey=@p0", args);
		}

		/// <summary>Replaces only an affected owner stack, preserving the containing record and its relative order.</summary>
		private static void WriteEntry(SQLiteConnection connection, SQLiteTransaction transaction, Entry entry)
		{
			XElement element = entry.Value;
			string path = (string)element.Attribute(entry.Kind == 1 ? "file" : entry.Kind == 2 ? "key" : "path") ?? "";
			string section = (string)element.Attribute("section"), name = (string)element.Attribute("key"), root = (string)element.Attribute("root");
			string identity = entry.Kind == 1 ? TupleKey(path, section, name) : entry.Kind == 3 ? TupleKey(root, path) : entry.Kind == 0 ? Canonical(path) : path;
			if (entry.Deleted)
			{
				Execute(connection, transaction, "DELETE FROM Items WHERE Kind=@p0 AND Identity=@p1", entry.Kind, identity);
				return;
			}

			Execute(connection, transaction, "INSERT OR IGNORE INTO Items(Kind,Identity,Path,Section,Name,Root) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", entry.Kind, identity, path, section, name, root);
			long id = Convert.ToInt64(Scalar(connection, transaction, "SELECT ItemId FROM Items WHERE Kind=@p0 AND Identity=@p1", entry.Kind, identity));
			Execute(connection, transaction, "DELETE FROM Owners WHERE ItemId=@p0", id);
			int position = 0;
			foreach (XElement owner in element.Element("installingMods")?.Elements("mod") ?? Enumerable.Empty<XElement>())
			{
				string key = (string)owner.Attribute("key") ?? "";
				Execute(connection, transaction, "INSERT INTO Owners(ItemId,Position,ModKey,OwnerKey,TextValue,BinaryValue) VALUES(@p0,@p1,@p2,@p3,@p4,@p5)", id, position++, key, Canonical(key), entry.Kind == 1 ? (object)owner.Value : null, entry.Kind == 2 ? (object)InstallLog.DeserializeGameSpecificValueOwner(owner) : null);
			}
		}

		/// <summary>Uses the same invariant case folding as Windows path identities, preserving original display spelling separately.</summary>
		private static string Canonical(string value)
		{
			return (value ?? "").ToUpperInvariant();
		}

		/// <summary>Encodes component boundaries so INI names cannot collide through separator characters.</summary>
		private static string TupleKey(params string[] parts)
		{
			return String.Concat(parts.Select(x => Canonical(x).Length.ToString(CultureInfo.InvariantCulture) + ":" + Canonical(x)));
		}

		/// <summary>Reads a nullable text column without changing empty values.</summary>
		private static string Text(SQLiteDataReader reader, int index)
		{
			return reader.IsDBNull(index) ? null : reader.GetString(index);
		}

		/// <summary>Opens only the requested database, without ambient transaction auto-enlistment.</summary>
		private SQLiteConnection Open(bool create = false)
		{
			if (!create)
				NativeStoreAuthority.RequireAvailable(_path);
			var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Enlist = false, Pooling = false, FailIfMissing = !create, DefaultTimeout = 5, JournalMode = SQLiteJournalModeEnum.Delete }.ConnectionString);
			try
			{
				connection.Open();
				Execute(connection, null, "PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000;");
				return connection;
			}
			catch
			{
				connection.Dispose();
				throw;
			}
		}

		/// <summary>Rejects missing or newer schemas rather than loading stale XML.</summary>
		private static void ValidateSchema(SQLiteConnection connection, SQLiteTransaction transaction = null)
		{
			if (Convert.ToString(Scalar(connection, transaction, "SELECT Value FROM Metadata WHERE Key='schema_version'"), CultureInfo.InvariantCulture) != SchemaVersion)
				throw new InvalidDataException("Unsupported InstallLog SQLite schema.");
		}

		/// <summary>Creates a parameterized command scoped to the caller's transaction.</summary>
		private static SQLiteCommand Command(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params object[] values)
		{
			SQLiteCommand command = connection.CreateCommand();
			command.Transaction = transaction;
			command.CommandText = sql;
			for (int i = 0; i < values.Length; i++)
				command.Parameters.AddWithValue("@p" + i.ToString(CultureInfo.InvariantCulture), values[i] ?? DBNull.Value);
			return command;
		}

		/// <summary>Executes one parameterized mutation.</summary>
		private static void Execute(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params object[] values)
		{
			using (SQLiteCommand command = Command(connection, transaction, sql, values))
				command.ExecuteNonQuery();
		}

		/// <summary>Reads a scalar through the current transaction.</summary>
		private static object Scalar(SQLiteConnection connection, SQLiteTransaction transaction, string sql, params object[] values)
		{
			using (SQLiteCommand command = Command(connection, transaction, sql, values))
				return command.ExecuteScalar();
		}

		/// <summary>Updates a bounded metadata value.</summary>
		private static void SetMetadata(SQLiteConnection connection, SQLiteTransaction transaction, string key, string value)
		{
			Execute(connection, transaction, "INSERT OR REPLACE INTO Metadata(Key,Value) VALUES(@p0,@p1)", key, value);
		}

		/// <summary>Normalizes optional containers and attribute order for one-time migration comparison.</summary>
		private static XElement Normalize(XDocument document)
		{
			XElement root = document.Root;
			var result = new XElement("installLog", root.Attributes().Where(x => x.Name != "deploymentCommitSequence" || (long)x != 0).OrderBy(x => x.Name.ToString()).Select(x => new XAttribute(x)));
			foreach (string section in new[]
			{
				"modList",
				"dataFiles",
				"iniEdits",
				"gameSpecificEdits",
				"deploymentFiles"
			}

			)
			{
				var container = new XElement(section);
				result.Add(container);
				foreach (XElement item in root.Element(section)?.Elements() ?? Enumerable.Empty<XElement>())
				{
					var copy = new XElement(item.Name, item.Attributes().Where(x => x.Name != "hidden" || (bool)x).OrderBy(x => x.Name.ToString()).Select(x => new XAttribute(x)));
					if (section == "modList")
					{
						foreach (string name in new[]
						{
							"version",
							"name",
							"installDate"
						}

						)
						{
							XElement child = item.Element(name);
							copy.Add(child != null ? new XElement(child) : name == "version" ? new XElement(name, new XAttribute("machineVersion", "")) : new XElement(name, name == "installDate" ? "<No Data>" : ""));
						}
					}
					else
					{
						var owners = new XElement(item.Element("installingMods") ?? new XElement("installingMods"));
						if (section == "gameSpecificEdits")
							foreach (XElement owner in owners.Elements("mod"))
							{
								byte[] value = InstallLog.DeserializeGameSpecificValueOwner(owner);
								owner.Value = value == null ? String.Empty : Convert.ToBase64String(value);
								owner.SetAttributeValue("absent", value == null ? (object)true : null);
							}
						copy.Add(owners);
					}

					container.Add(copy);
				}
			}

			return result;
		}
	}
}
