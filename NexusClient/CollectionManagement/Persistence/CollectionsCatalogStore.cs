using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists Collection definitions and immutable revision metadata in the Collections feature store.
	/// </summary>
	/// <remarks>
	/// This catalog owns feature metadata only. Native installation state remains authoritative in the existing NMM stores.
	/// Collection display metadata may be refreshed in place, while a persisted revision identity is immutable.
	/// </remarks>
	public sealed class CollectionsCatalogStore
	{
		private readonly CollectionsStore _store;

		/// <summary>
		/// Creates a catalog over an existing Collections feature store.
		/// </summary>
		public CollectionsCatalogStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>
		/// Inserts a new Collection definition or refreshes its display metadata without changing its identity.
		/// </summary>
		public void SaveDefinition(CollectionDefinition definition)
		{
			if (definition == null)
				throw new ArgumentNullException(nameof(definition));

			_store.ExecuteWrite((connection, transaction) => SaveDefinition(connection, transaction, definition));
		}

		/// <summary>
		/// Persists one immutable Collection revision. The owning definition must already exist.
		/// </summary>
		/// <remarks>
		/// Re-saving the exact same revision is idempotent. Reusing an existing revision identity for different metadata is rejected.
		/// </remarks>
		public void SaveRevision(CollectionRevision revision)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));

			_store.ExecuteWrite((connection, transaction) =>
			{
				RequireDefinition(connection, transaction, revision.Collection);
				SaveRevision(connection, transaction, revision);
			});
		}

		/// <summary>
		/// Refreshes a Collection definition and persists one of its immutable revisions in the same durable transaction.
		/// </summary>
		public void SaveDefinitionAndRevision(CollectionDefinition definition, CollectionRevision revision)
		{
			if (definition == null)
				throw new ArgumentNullException(nameof(definition));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (!definition.Identity.Equals(revision.Collection))
				throw new ArgumentException("The revision must belong to the supplied Collection definition.", nameof(revision));

			_store.ExecuteWrite((connection, transaction) =>
			{
				SaveDefinition(connection, transaction, definition);
				SaveRevision(connection, transaction, revision);
			});
		}

		/// <summary>
		/// Loads one Collection definition by exact identity, or <c>null</c> when it is not present.
		/// </summary>
		public CollectionDefinition GetDefinition(CollectionIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));

			return _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT origin, collection_id, display_name, author_display_name, summary
FROM collections
WHERE origin = @origin AND collection_id = @collection_id;";
					AddCollectionIdentityParameters(command, identity);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						return reader.Read() ? ReadDefinition(reader) : null;
					}
				}
			});
		}

		/// <summary>
		/// Loads every persisted Collection definition in stable identity order.
		/// </summary>
		public IReadOnlyList<CollectionDefinition> GetDefinitions()
		{
			return _store.ExecuteRead((connection, transaction) =>
			{
				var definitions = new List<CollectionDefinition>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT origin, collection_id, display_name, author_display_name, summary
FROM collections
ORDER BY origin, collection_id;";
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							definitions.Add(ReadDefinition(reader));
					}
				}
				return definitions;
			});
		}

		/// <summary>
		/// Loads one immutable Collection revision by exact identity, or <c>null</c> when it is not present.
		/// </summary>
		public CollectionRevision GetRevision(CollectionRevisionIdentity identity)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));

			return _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT origin, collection_id, revision_id, nexus_revision_number, revision_label, notes, declared_member_count
FROM collection_revisions
WHERE origin = @origin AND collection_id = @collection_id AND revision_id = @revision_id;";
					AddRevisionIdentityParameters(command, identity);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						return reader.Read() ? ReadRevision(reader) : null;
					}
				}
			});
		}

		/// <summary>
		/// Loads every immutable revision belonging to one Collection identity.
		/// </summary>
		public IReadOnlyList<CollectionRevision> GetRevisions(CollectionIdentity collection)
		{
			if (collection == null)
				throw new ArgumentNullException(nameof(collection));

			return _store.ExecuteRead((connection, transaction) =>
			{
				var revisions = new List<CollectionRevision>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT origin, collection_id, revision_id, nexus_revision_number, revision_label, notes, declared_member_count
FROM collection_revisions
WHERE origin = @origin AND collection_id = @collection_id
ORDER BY CASE WHEN nexus_revision_number IS NULL THEN 1 ELSE 0 END, nexus_revision_number, revision_id;";
					AddCollectionIdentityParameters(command, collection);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							revisions.Add(ReadRevision(reader));
					}
				}
				return revisions;
			});
		}

		private static void SaveDefinition(SQLiteConnection connection, SQLiteTransaction transaction, CollectionDefinition definition)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE collections
SET display_name = @display_name,
    author_display_name = @author_display_name,
    summary = @summary
WHERE origin = @origin AND collection_id = @collection_id;";
				AddDefinitionParameters(command, definition);
				if (command.ExecuteNonQuery() > 0)
					return;
			}

			if (DefinitionExists(connection, transaction, definition.Identity))
				return;

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO collections
    (origin, collection_id, display_name, author_display_name, summary)
VALUES
    (@origin, @collection_id, @display_name, @author_display_name, @summary);";
				AddDefinitionParameters(command, definition);
				command.ExecuteNonQuery();
			}
		}

		private static void SaveRevision(SQLiteConnection connection, SQLiteTransaction transaction, CollectionRevision revision)
		{
			CollectionRevision existing = ReadRevision(connection, transaction, revision.Identity);
			if (existing != null)
			{
				if (!RevisionMetadataEquals(existing, revision))
					throw new InvalidOperationException("A persisted Collection revision is immutable and cannot be replaced with different metadata.");
				return;
			}

			EnsureNoRevisionNumberConflict(connection, transaction, revision);
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO collection_revisions
    (origin, collection_id, revision_id, nexus_revision_number, revision_label, notes, declared_member_count)
VALUES
    (@origin, @collection_id, @revision_id, @nexus_revision_number, @revision_label, @notes, @declared_member_count);";
				AddRevisionParameters(command, revision);
				command.ExecuteNonQuery();
			}
		}

		private static CollectionRevision ReadRevision(SQLiteConnection connection, SQLiteTransaction transaction, CollectionRevisionIdentity identity)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT origin, collection_id, revision_id, nexus_revision_number, revision_label, notes, declared_member_count
FROM collection_revisions
WHERE origin = @origin AND collection_id = @collection_id AND revision_id = @revision_id;";
				AddRevisionIdentityParameters(command, identity);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadRevision(reader) : null;
				}
			}
		}

		private static void RequireDefinition(SQLiteConnection connection, SQLiteTransaction transaction, CollectionIdentity identity)
		{
			if (!DefinitionExists(connection, transaction, identity))
				throw new InvalidOperationException("The owning Collection definition must be persisted before its revision.");
		}

		private static bool DefinitionExists(SQLiteConnection connection, SQLiteTransaction transaction, CollectionIdentity identity)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT COUNT(*)
FROM collections
WHERE origin = @origin AND collection_id = @collection_id;";
				AddCollectionIdentityParameters(command, identity);
				return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
			}
		}

		private static void EnsureNoRevisionNumberConflict(SQLiteConnection connection, SQLiteTransaction transaction, CollectionRevision revision)
		{
			if (!revision.Identity.NexusRevisionNumber.HasValue)
				return;

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT revision_id
FROM collection_revisions
WHERE origin = @origin AND collection_id = @collection_id AND nexus_revision_number = @nexus_revision_number;";
				AddCollectionIdentityParameters(command, revision.Collection);
				command.Parameters.AddWithValue("@nexus_revision_number", revision.Identity.NexusRevisionNumber.Value);
				object value = command.ExecuteScalar();
				if (value != null && value != DBNull.Value &&
					!StringComparer.Ordinal.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), revision.Identity.StableRevisionId))
				{
					throw new InvalidOperationException("A Nexus revision number is already bound to a different stable revision identity.");
				}
			}
		}

		private static void AddDefinitionParameters(SQLiteCommand command, CollectionDefinition definition)
		{
			AddCollectionIdentityParameters(command, definition.Identity);
			command.Parameters.AddWithValue("@display_name", DbValue(definition.DisplayName));
			command.Parameters.AddWithValue("@author_display_name", DbValue(definition.AuthorDisplayName));
			command.Parameters.AddWithValue("@summary", DbValue(definition.Summary));
		}

		private static void AddRevisionParameters(SQLiteCommand command, CollectionRevision revision)
		{
			AddRevisionIdentityParameters(command, revision.Identity);
			command.Parameters.AddWithValue("@nexus_revision_number", DbValue(revision.Identity.NexusRevisionNumber));
			command.Parameters.AddWithValue("@revision_label", DbValue(revision.RevisionLabel));
			command.Parameters.AddWithValue("@notes", DbValue(revision.Notes));
			command.Parameters.AddWithValue("@declared_member_count", DbValue(revision.DeclaredMemberCount));
		}

		private static void AddCollectionIdentityParameters(SQLiteCommand command, CollectionIdentity identity)
		{
			command.Parameters.AddWithValue("@origin", (int)identity.Origin);
			command.Parameters.AddWithValue("@collection_id", identity.StableId);
		}

		private static void AddRevisionIdentityParameters(SQLiteCommand command, CollectionRevisionIdentity identity)
		{
			AddCollectionIdentityParameters(command, identity.Collection);
			command.Parameters.AddWithValue("@revision_id", identity.StableRevisionId);
		}

		private static object DbValue(object value)
		{
			return value ?? DBNull.Value;
		}

		private static CollectionDefinition ReadDefinition(SQLiteDataReader reader)
		{
			CollectionIdentity identity = ReadCollectionIdentity(reader.GetInt32(0), reader.GetString(1));
			return new CollectionDefinition(identity, ReadNullableString(reader, 2), ReadNullableString(reader, 3), ReadNullableString(reader, 4));
		}

		private static CollectionRevision ReadRevision(SQLiteDataReader reader)
		{
			CollectionIdentity collection = ReadCollectionIdentity(reader.GetInt32(0), reader.GetString(1));
			string revisionId = reader.GetString(2);
			long? nexusRevisionNumber = ReadNullableInt64(reader, 3);
			CollectionRevisionIdentity identity = ReadRevisionIdentity(collection, revisionId, nexusRevisionNumber);
			return new CollectionRevision(identity, ReadNullableString(reader, 4), ReadNullableString(reader, 5), ReadNullableInt32(reader, 6));
		}

		private static CollectionIdentity ReadCollectionIdentity(int rawOrigin, string stableId)
		{
			CollectionOrigin origin = (CollectionOrigin)rawOrigin;
			switch (origin)
			{
				case CollectionOrigin.NexusMods:
					return CollectionIdentity.FromNexus(stableId);
				case CollectionOrigin.Local:
					Guid localId;
					if (!TryParseCanonicalGuid(stableId, out localId))
						throw new CollectionsStoreSchemaException("A persisted Local Collection has an invalid identity.");
					return CollectionIdentity.FromLocal(localId);
				default:
					throw new CollectionsStoreSchemaException("A persisted Collection has an unsupported origin value.");
			}
		}

		private static CollectionRevisionIdentity ReadRevisionIdentity(CollectionIdentity collection, string revisionId, long? nexusRevisionNumber)
		{
			if (collection.Origin == CollectionOrigin.NexusMods)
			{
				if (!nexusRevisionNumber.HasValue || nexusRevisionNumber.Value <= 0)
					throw new CollectionsStoreSchemaException("A persisted Nexus Collection revision is missing its concrete revision number.");
				return CollectionRevisionIdentity.FromNexus(collection, revisionId, nexusRevisionNumber.Value);
			}

			if (nexusRevisionNumber.HasValue)
				throw new CollectionsStoreSchemaException("A persisted Local Collection revision cannot contain a Nexus revision number.");

			Guid localRevisionId;
			if (!TryParseCanonicalGuid(revisionId, out localRevisionId))
				throw new CollectionsStoreSchemaException("A persisted Local Collection revision has an invalid identity.");
			return CollectionRevisionIdentity.FromLocal(collection, localRevisionId);
		}

		private static bool TryParseCanonicalGuid(string value, out Guid parsed)
		{
			if (!Guid.TryParseExact(value, "D", out parsed) || parsed == Guid.Empty)
				return false;
			return StringComparer.Ordinal.Equals(value, parsed.ToString("D"));
		}

		private static bool RevisionMetadataEquals(CollectionRevision first, CollectionRevision second)
		{
			return first.Identity.Equals(second.Identity) &&
				StringComparer.Ordinal.Equals(first.RevisionLabel, second.RevisionLabel) &&
				StringComparer.Ordinal.Equals(first.Notes, second.Notes) &&
				first.DeclaredMemberCount == second.DeclaredMemberCount;
		}

		private static string ReadNullableString(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
		}

		private static long? ReadNullableInt64(SQLiteDataReader reader, int ordinal)
		{
			return reader.IsDBNull(ordinal) ? (long?)null : reader.GetInt64(ordinal);
		}

		private static int? ReadNullableInt32(SQLiteDataReader reader, int ordinal)
		{
			if (reader.IsDBNull(ordinal))
				return null;

			long value = reader.GetInt64(ordinal);
			if (value < 0 || value > Int32.MaxValue)
				throw new CollectionsStoreSchemaException("A persisted Collection revision has an invalid declared member count.");
			return (int)value;
		}
	}
}
