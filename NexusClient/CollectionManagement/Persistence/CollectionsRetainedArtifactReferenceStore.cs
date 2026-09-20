using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Persists durable lifetime references that protect retained immutable artifacts from feature cleanup.
	/// </summary>
	/// <remarks>
	/// This layer owns reference lifetime only. It never deletes retained bytes or artifact metadata; tombstoning and
	/// garbage collection are a separate cleanup boundary. References are also independent from native deployment ownership.
	/// </remarks>
	public sealed class CollectionsRetainedArtifactReferenceStore
	{
		private readonly CollectionsStore _store;

		/// <summary>
		/// Creates a retained-artifact reference store over an existing Collections feature store.
		/// </summary>
		public CollectionsRetainedArtifactReferenceStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>
		/// Acquires an idempotent durable reference protecting one sealed retained artifact for one exact owner/role.
		/// </summary>
		public CollectionsRetainedArtifactReferenceRecord AcquireReference(string artifactId,
			CollectionsRetainedArtifactOwnerKind ownerKind, string ownerId, string role)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			ValidateOwnerKind(ownerKind);
			ownerId = CollectionsRetainedArtifactReferenceValidation.RequireOwnerId(ownerId, nameof(ownerId));
			role = CollectionIdentityValidation.RequireOpaqueToken(role, nameof(role));

			CollectionsRetainedArtifactReferenceRecord result = null;
			_store.ExecuteWrite((connection, transaction) =>
			{
				RequireSealedArtifact(connection, transaction, artifactId);
				result = ReadReferenceByOwner(connection, transaction, artifactId, ownerKind, ownerId, role);
				if (result != null)
					return;

				string proposedReferenceId = Guid.NewGuid().ToString("D");
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT OR IGNORE INTO retained_artifact_references
    (reference_id, artifact_id, owner_kind, owner_id, role)
VALUES
    (@reference_id, @artifact_id, @owner_kind, @owner_id, @role);";
					command.Parameters.AddWithValue("@reference_id", proposedReferenceId);
					command.Parameters.AddWithValue("@artifact_id", artifactId);
					command.Parameters.AddWithValue("@owner_kind", (int)ownerKind);
					command.Parameters.AddWithValue("@owner_id", ownerId);
					command.Parameters.AddWithValue("@role", role);
					command.ExecuteNonQuery();
				}

				result = ReadReferenceByOwner(connection, transaction, artifactId, ownerKind, ownerId, role);
				if (result == null)
					throw new CollectionsStoreSchemaException("A retained artifact reference could not be read after durable acquisition.");
			});

			return result;
		}

		/// <summary>
		/// Acquires an idempotent reference while requiring one owner/role to identify exactly one retained artifact.
		/// </summary>
		/// <remarks>Recovery-input roles use this to reject restart attempts that would silently bind the same role to changed bytes.</remarks>
		public CollectionsRetainedArtifactReferenceRecord AcquireExclusiveRoleReference(string artifactId,
			CollectionsRetainedArtifactOwnerKind ownerKind, string ownerId, string role)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			ValidateOwnerKind(ownerKind);
			ownerId = CollectionsRetainedArtifactReferenceValidation.RequireOwnerId(ownerId, nameof(ownerId));
			role = CollectionIdentityValidation.RequireOpaqueToken(role, nameof(role));
			CollectionsRetainedArtifactReferenceRecord result = null;
			_store.ExecuteWrite((connection, transaction) =>
			{
				RequireSealedArtifact(connection, transaction, artifactId);
				IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> existing = ReadReferencesForOwnerRole(connection, transaction, ownerKind, ownerId, role);
				if (existing.Count > 1)
					throw new CollectionsStoreSchemaException("A retained-artifact owner role is ambiguously bound to multiple artifacts.");
				if (existing.Count == 1)
				{
					if (!StringComparer.Ordinal.Equals(existing[0].ArtifactId, artifactId))
						throw new InvalidOperationException("A retained-artifact owner role is already bound to different immutable bytes.");
					result = existing[0];
					return;
				}
				string referenceId = Guid.NewGuid().ToString("D");
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT INTO retained_artifact_references
    (reference_id, artifact_id, owner_kind, owner_id, role)
VALUES
    (@reference_id, @artifact_id, @owner_kind, @owner_id, @role);";
					command.Parameters.AddWithValue("@reference_id", referenceId);
					command.Parameters.AddWithValue("@artifact_id", artifactId);
					command.Parameters.AddWithValue("@owner_kind", (int)ownerKind);
					command.Parameters.AddWithValue("@owner_id", ownerId);
					command.Parameters.AddWithValue("@role", role);
					command.ExecuteNonQuery();
				}
				IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> inserted = ReadReferencesForOwnerRole(connection, transaction, ownerKind, ownerId, role);
				if (inserted.Count != 1 || !StringComparer.Ordinal.Equals(inserted[0].ArtifactId, artifactId))
					throw new CollectionsStoreSchemaException("An exclusive retained-artifact owner role could not be read after durable acquisition.");
				result = inserted[0];
			});
			return result;
		}

		/// <summary>Loads the unique retained-artifact reference for one exact owner/role, or <c>null</c> when none exists.</summary>
		public CollectionsRetainedArtifactReferenceRecord GetReferenceForOwnerRole(
			CollectionsRetainedArtifactOwnerKind ownerKind, string ownerId, string role)
		{
			ValidateOwnerKind(ownerKind);
			ownerId = CollectionsRetainedArtifactReferenceValidation.RequireOwnerId(ownerId, nameof(ownerId));
			role = CollectionIdentityValidation.RequireOpaqueToken(role, nameof(role));
			return _store.ExecuteRead((connection, transaction) =>
			{
				IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> references = ReadReferencesForOwnerRole(connection, transaction, ownerKind, ownerId, role);
				if (references.Count > 1)
					throw new CollectionsStoreSchemaException("A retained-artifact owner role is ambiguously bound to multiple artifacts.");
				return references.Count == 0 ? null : references[0];
			});
		}

		/// <summary>
		/// Loads one durable retained-artifact reference by identity, or <c>null</c> when it is absent.
		/// </summary>
		public CollectionsRetainedArtifactReferenceRecord GetReference(string referenceId)
		{
			referenceId = CollectionIdentityValidation.RequireOpaqueToken(referenceId, nameof(referenceId));
			return _store.ExecuteRead((connection, transaction) => ReadReference(connection, transaction, referenceId));
		}

		/// <summary>
		/// Loads all durable references currently protecting one retained artifact.
		/// </summary>
		public IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> GetReferencesForArtifact(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			return _store.ExecuteRead((connection, transaction) =>
				ReadReferences(connection, transaction, "artifact_id=@artifact_id", command =>
					command.Parameters.AddWithValue("@artifact_id", artifactId)));
		}

		/// <summary>
		/// Loads all retained-artifact references owned by one exact feature record.
		/// </summary>
		public IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> GetReferencesForOwner(
			CollectionsRetainedArtifactOwnerKind ownerKind, string ownerId)
		{
			ValidateOwnerKind(ownerKind);
			ownerId = CollectionsRetainedArtifactReferenceValidation.RequireOwnerId(ownerId, nameof(ownerId));
			return _store.ExecuteRead((connection, transaction) =>
				ReadReferences(connection, transaction, "owner_kind=@owner_kind AND owner_id=@owner_id", command =>
				{
					command.Parameters.AddWithValue("@owner_kind", (int)ownerKind);
					command.Parameters.AddWithValue("@owner_id", ownerId);
				}));
		}

		/// <summary>
		/// Gets whether at least one durable owner currently protects an artifact from Collections cleanup.
		/// </summary>
		public bool IsReferenced(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			return _store.ExecuteRead((connection, transaction) => IsReferenced(connection, transaction, artifactId));
		}

		/// <summary>
		/// Releases one durable reference. This operation never deletes retained bytes or artifact metadata.
		/// </summary>
		/// <returns><c>true</c> when a reference row was released; otherwise <c>false</c>.</returns>
		public bool ReleaseReference(string referenceId)
		{
			referenceId = CollectionIdentityValidation.RequireOpaqueToken(referenceId, nameof(referenceId));
			bool released = false;
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM retained_artifact_references WHERE reference_id=@reference_id;";
					command.Parameters.AddWithValue("@reference_id", referenceId);
					released = command.ExecuteNonQuery() == 1;
				}
			});
			return released;
		}

		/// <summary>
		/// Releases every retained-artifact reference belonging to one exact owner. Shared references held by other owners remain.
		/// </summary>
		/// <returns>The number of reference rows released.</returns>
		public int ReleaseOwnerReferences(CollectionsRetainedArtifactOwnerKind ownerKind, string ownerId)
		{
			ValidateOwnerKind(ownerKind);
			ownerId = CollectionsRetainedArtifactReferenceValidation.RequireOwnerId(ownerId, nameof(ownerId));
			int released = 0;
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
DELETE FROM retained_artifact_references
WHERE owner_kind=@owner_kind AND owner_id=@owner_id;";
					command.Parameters.AddWithValue("@owner_kind", (int)ownerKind);
					command.Parameters.AddWithValue("@owner_id", ownerId);
					released = command.ExecuteNonQuery();
				}
			});
			return released;
		}

		/// <summary>
		/// Checks reference protection inside an existing Collections transaction for the later cleanup boundary.
		/// </summary>
		internal static bool IsReferenced(SQLiteConnection connection, SQLiteTransaction transaction, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT 1
FROM retained_artifact_references
WHERE artifact_id=@artifact_id
LIMIT 1;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				return command.ExecuteScalar() != null;
			}
		}

		private static void RequireSealedArtifact(SQLiteConnection connection, SQLiteTransaction transaction, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT a.relative_path, a.sealed,
       CASE WHEN t.artifact_id IS NULL THEN 0 ELSE 1 END AS tombstoned
FROM retained_artifacts a
LEFT JOIN retained_artifact_tombstones t ON t.artifact_id=a.artifact_id
WHERE a.artifact_id=@artifact_id;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						throw new InvalidOperationException("Cannot retain a reference to an artifact that is not present in the Collections feature store.");
					if (reader.IsDBNull(0) || reader.GetInt32(1) != 1)
						throw new InvalidDataException("Cannot retain a reference to an unsealed Collections artifact.");
					if (reader.GetInt32(2) != 0)
						throw new InvalidOperationException("Cannot acquire a reference to retained content that is already tombstoned for cleanup.");
				}
			}
		}

		private static CollectionsRetainedArtifactReferenceRecord ReadReference(SQLiteConnection connection,
			SQLiteTransaction transaction, string referenceId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = ReferenceSelect + " WHERE reference_id=@reference_id;";
				command.Parameters.AddWithValue("@reference_id", referenceId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadReference(reader) : null;
				}
			}
		}

		private static IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> ReadReferencesForOwnerRole(
			SQLiteConnection connection, SQLiteTransaction transaction, CollectionsRetainedArtifactOwnerKind ownerKind,
			string ownerId, string role)
		{
			return ReadReferences(connection, transaction,
				"owner_kind=@owner_kind AND owner_id=@owner_id AND role=@role", command =>
				{
					command.Parameters.AddWithValue("@owner_kind", (int)ownerKind);
					command.Parameters.AddWithValue("@owner_id", ownerId);
					command.Parameters.AddWithValue("@role", role);
				});
		}

		private static CollectionsRetainedArtifactReferenceRecord ReadReferenceByOwner(SQLiteConnection connection,
			SQLiteTransaction transaction, string artifactId, CollectionsRetainedArtifactOwnerKind ownerKind,
			string ownerId, string role)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = ReferenceSelect + @"
WHERE artifact_id=@artifact_id AND owner_kind=@owner_kind AND owner_id=@owner_id AND role=@role;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				command.Parameters.AddWithValue("@owner_kind", (int)ownerKind);
				command.Parameters.AddWithValue("@owner_id", ownerId);
				command.Parameters.AddWithValue("@role", role);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadReference(reader) : null;
				}
			}
		}

		private static IReadOnlyList<CollectionsRetainedArtifactReferenceRecord> ReadReferences(SQLiteConnection connection,
			SQLiteTransaction transaction, string whereClause, Action<SQLiteCommand> bindParameters)
		{
			var references = new List<CollectionsRetainedArtifactReferenceRecord>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = ReferenceSelect + " WHERE " + whereClause + " ORDER BY reference_id;";
				bindParameters(command);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
						references.Add(ReadReference(reader));
				}
			}
			return references;
		}

		private static CollectionsRetainedArtifactReferenceRecord ReadReference(SQLiteDataReader reader)
		{
			try
			{
				CollectionsRetainedArtifactOwnerKind ownerKind = (CollectionsRetainedArtifactOwnerKind)reader.GetInt32(2);
				ValidateOwnerKind(ownerKind);
				return new CollectionsRetainedArtifactReferenceRecord(reader.GetString(0), reader.GetString(1), ownerKind,
					reader.GetString(3), reader.GetString(4));
			}
			catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is InvalidCastException)
			{
				throw new CollectionsStoreSchemaException("A persisted retained-artifact reference is inconsistent with the Collections reference model.", ex);
			}
		}

		private static void ValidateOwnerKind(CollectionsRetainedArtifactOwnerKind ownerKind)
		{
			if (!Enum.IsDefined(typeof(CollectionsRetainedArtifactOwnerKind), ownerKind) ||
				ownerKind == CollectionsRetainedArtifactOwnerKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(ownerKind));
		}

		private const string ReferenceSelect = @"
SELECT reference_id, artifact_id, owner_kind, owner_id, role
FROM retained_artifact_references";
	}
}
