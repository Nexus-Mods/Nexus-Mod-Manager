using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Durable mutable-head record for one NMM-owned Local Collection cloned from an immutable source revision.
	/// </summary>
	/// <remarks>
	/// A working copy is not an installed association and is not itself an immutable Collection revision. It keeps the exact
	/// source recipe bytes alive so later C9 editing can create new Local revisions without mutating or reinterpreting the
	/// original Nexus revision.
	/// </remarks>
	public sealed class CollectionLocalWorkingCopyRecord
	{
		internal CollectionLocalWorkingCopyRecord(CollectionIdentity collection, CollectionRevisionIdentity sourceRevision,
			CollectionRevisionSourceRecord baseSource, string draftManifestArtifactId, DateTime createdUtc, DateTime updatedUtc)
		{
			Collection = collection ?? throw new ArgumentNullException(nameof(collection));
			if (Collection.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A Local Collection working copy requires an NMM-owned Local Collection identity.", nameof(collection));
			SourceRevision = sourceRevision ?? throw new ArgumentNullException(nameof(sourceRevision));
			BaseSource = baseSource ?? throw new ArgumentNullException(nameof(baseSource));
			if (!BaseSource.Revision.Equals(SourceRevision))
				throw new ArgumentException("The working-copy base source must belong to the exact cloned source revision.", nameof(baseSource));
			if (String.IsNullOrEmpty(BaseSource.RawManifestArtifactId))
				throw new ArgumentException("A Local Collection working copy requires retained exact base manifest bytes.", nameof(baseSource));
			DraftManifestArtifactId = String.IsNullOrWhiteSpace(draftManifestArtifactId)
				? BaseSource.RawManifestArtifactId
				: CollectionIdentityValidation.RequireOpaqueToken(draftManifestArtifactId, nameof(draftManifestArtifactId));
			if (createdUtc.Kind != DateTimeKind.Utc || updatedUtc.Kind != DateTimeKind.Utc)
				throw new ArgumentException("Local Collection working-copy timestamps must be UTC.");
			if (updatedUtc < createdUtc)
				throw new ArgumentException("A Local Collection working copy cannot be updated before it was created.");
			CreatedUtc = createdUtc;
			UpdatedUtc = updatedUtc;
		}

		public CollectionIdentity Collection { get; }
		public CollectionRevisionIdentity SourceRevision { get; }
		public CollectionRevisionSourceRecord BaseSource { get; }
		/// <summary>Gets the retained exact mutable-head manifest currently represented by this working copy.</summary>
		public string DraftManifestArtifactId { get; }
		public DateTime CreatedUtc { get; }
		public DateTime UpdatedUtc { get; }
	}

	/// <summary>
	/// Persists C9 Local Collection working copies while keeping their exact immutable base recipe bytes independently retained.
	/// </summary>
	public sealed class CollectionsLocalWorkingCopyStore
	{
		private readonly CollectionsStore _store;
		private readonly CollectionsRetainedArtifactStore _artifactStore;

		public CollectionsLocalWorkingCopyStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_artifactStore = new CollectionsRetainedArtifactStore(store);
		}

		/// <summary>
		/// Creates one new Local Collection mutable head from an exact retained source revision.
		/// </summary>
		/// <remarks>
		/// The clone does not alter native mods, Collection associations, or the immutable source revision. The Local definition
		/// and working-copy base record are published in one Collections transaction. The retained artifact foreign keys keep the
		/// exact recipe bytes alive even if source-revision retention is later released after uninstalling the original Collection.
		/// </remarks>
		public CollectionLocalWorkingCopyRecord CreateClone(CollectionDefinition localDefinition,
			CollectionRevision sourceRevision, CollectionRevisionSourceRecord source)
		{
			if (localDefinition == null)
				throw new ArgumentNullException(nameof(localDefinition));
			if (localDefinition.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A cloned working copy must use a Local Collection definition.", nameof(localDefinition));
			if (sourceRevision == null)
				throw new ArgumentNullException(nameof(sourceRevision));
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (!sourceRevision.Identity.Equals(source.Revision))
				throw new ArgumentException("The retained source does not belong to the supplied source revision.", nameof(source));
			CollectionRevisionSourceRecord persistedSource = new CollectionsRevisionSourceStore(_store).GetSource(sourceRevision.Identity);
			if (persistedSource == null || !persistedSource.Equals(source))
				throw new InvalidOperationException("Cloning requires the exact persisted source provenance for the source revision.");
			if (String.IsNullOrEmpty(source.RawManifestArtifactId))
				throw new InvalidOperationException("Cloning requires the exact retained collection.json bytes for the source revision.");

			RequireArtifact(source.RawManifestArtifactId, source.ManifestSource.ContentHash,
				source.ManifestSource.ByteLength, true, "manifest");
			if (!String.IsNullOrEmpty(source.RawBundleArtifactId))
				RequireArtifact(source.RawBundleArtifactId, source.BundleContentHash, source.BundleByteLength, false, "bundle");

			DateTime now = DateTime.UtcNow;
			_store.ExecuteWrite((connection, transaction) =>
			{
				if (DefinitionExists(connection, transaction, localDefinition.Identity))
					throw new InvalidOperationException("The generated Local Collection identity is already present in the catalog.");
				RequireRevision(connection, transaction, sourceRevision.Identity);
				CollectionsCatalogStore.SaveDefinition(connection, transaction, localDefinition);
				InsertWorkingCopy(connection, transaction, localDefinition.Identity, source, now);
			});

			CollectionLocalWorkingCopyRecord result = Get(localDefinition.Identity);
			if (result == null)
				throw new CollectionsStoreSchemaException("The Local Collection working copy could not be read after durable creation.");
			return result;
		}

		/// <summary>Loads one Local Collection working copy by exact local collection identity.</summary>
		public CollectionLocalWorkingCopyRecord Get(CollectionIdentity localCollection)
		{
			if (localCollection == null)
				throw new ArgumentNullException(nameof(localCollection));
			if (localCollection.Origin != CollectionOrigin.Local)
				throw new ArgumentException("A Local Collection identity is required.", nameof(localCollection));
			return _store.ExecuteRead((connection, transaction) => ReadOne(connection, transaction, localCollection.StableId));
		}

		/// <summary>Loads all persisted Local Collection working copies in stable identity order.</summary>
		public IReadOnlyList<CollectionLocalWorkingCopyRecord> GetAll()
		{
			return _store.ExecuteRead((connection, transaction) =>
			{
				var ids = new List<string>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT local_collection_id FROM local_working_copies ORDER BY local_collection_id;";
					using (SQLiteDataReader reader = command.ExecuteReader())
						while (reader.Read())
							ids.Add(reader.GetString(0));
				}
				var result = new List<CollectionLocalWorkingCopyRecord>(ids.Count);
				foreach (string id in ids)
					result.Add(ReadOne(connection, transaction, id));
				return result;
			});
		}

		/// <summary>
		/// Loads and re-verifies the exact source collection.json bytes owned by a working copy.
		/// </summary>
		public byte[] LoadBaseManifest(CollectionLocalWorkingCopyRecord workingCopy)
		{
			if (workingCopy == null)
				throw new ArgumentNullException(nameof(workingCopy));
			CollectionLocalWorkingCopyRecord persisted = Get(workingCopy.Collection);
			if (persisted == null)
				throw new InvalidOperationException("The Local Collection working copy is no longer persisted.");
			if (!persisted.SourceRevision.Equals(workingCopy.SourceRevision) ||
				!StringComparer.Ordinal.Equals(persisted.BaseSource.RawManifestArtifactId, workingCopy.BaseSource.RawManifestArtifactId))
				throw new InvalidOperationException("The supplied Local Collection working-copy snapshot is stale.");

			CollectionRevisionSourceRecord source = persisted.BaseSource;
			RequireArtifact(source.RawManifestArtifactId, source.ManifestSource.ContentHash,
				source.ManifestSource.ByteLength, true, "manifest");
			if (source.ManifestSource.ByteLength > Int32.MaxValue)
				throw new InvalidDataException("The Local Collection working-copy base manifest is too large to load.");
			byte[] bytes = new byte[(int)source.ManifestSource.ByteLength];
			using (Stream stream = _artifactStore.OpenRead(source.RawManifestArtifactId))
			{
				int offset = 0;
				while (offset < bytes.Length)
				{
					int read = stream.Read(bytes, offset, bytes.Length - offset);
					if (read <= 0)
						throw new EndOfStreamException("The Local Collection working-copy base manifest ended before its retained length.");
					offset += read;
				}
				if (stream.ReadByte() != -1)
					throw new InvalidDataException("The Local Collection working-copy base manifest exceeds its retained length.");
			}
			return bytes;
		}

		/// <summary>Loads and verifies the current editable collection.json bytes for a Local working copy.</summary>
		public byte[] LoadDraftManifest(CollectionLocalWorkingCopyRecord workingCopy)
		{
			if (workingCopy == null)
				throw new ArgumentNullException(nameof(workingCopy));
			CollectionLocalWorkingCopyRecord persisted = Get(workingCopy.Collection);
			if (persisted == null)
				throw new InvalidOperationException("The Local Collection working copy is no longer persisted.");
			if (!persisted.SourceRevision.Equals(workingCopy.SourceRevision) || persisted.UpdatedUtc != workingCopy.UpdatedUtc ||
				!StringComparer.Ordinal.Equals(persisted.DraftManifestArtifactId, workingCopy.DraftManifestArtifactId))
				throw new InvalidOperationException("The supplied Local Collection working-copy snapshot is stale.");

			return LoadArtifactBytes(persisted.DraftManifestArtifactId, "draft manifest");
		}

		/// <summary>
		/// Atomically publishes edited manifest bytes and refreshes mutable Local Collection display metadata.
		/// Native mods, installed associations and immutable saved Local revisions are never changed by this operation.
		/// </summary>
		public CollectionLocalWorkingCopyRecord SaveDraft(CollectionLocalWorkingCopyRecord workingCopy,
			CollectionDefinition definition, byte[] rawManifestBytes)
		{
			if (workingCopy == null) throw new ArgumentNullException(nameof(workingCopy));
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			if (rawManifestBytes == null) throw new ArgumentNullException(nameof(rawManifestBytes));
			if (!definition.Identity.Equals(workingCopy.Collection) || definition.Origin != CollectionOrigin.Local)
				throw new ArgumentException("The edited definition must belong to the exact Local working copy.", nameof(definition));

			CollectionsRetainedArtifact retained;
			using (var stream = new MemoryStream(rawManifestBytes, false))
				retained = _artifactStore.Publish(stream);
			if (!_artifactStore.VerifyArtifact(retained.ArtifactId))
				throw new InvalidDataException("The edited Local Collection manifest failed retained-content verification.");

			DateTime updated = DateTime.UtcNow;
			_store.ExecuteWrite((connection, transaction) =>
			{
				CollectionLocalWorkingCopyRecord current = ReadOne(connection, transaction, workingCopy.Collection.StableId);
				if (current == null)
					throw new InvalidOperationException("The Local Collection working copy is no longer persisted.");
				if (current.UpdatedUtc != workingCopy.UpdatedUtc ||
					!StringComparer.Ordinal.Equals(current.DraftManifestArtifactId, workingCopy.DraftManifestArtifactId))
					throw new InvalidOperationException("The Local Collection working copy changed after it was opened; reload it before saving edits.");

				CollectionsCatalogStore.SaveDefinition(connection, transaction, definition);
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
UPDATE local_working_copies
SET draft_manifest_artifact_id=@draft_manifest_artifact_id, updated_utc=@updated_utc
WHERE local_origin=@local_origin AND local_collection_id=@local_collection_id;";
					command.Parameters.AddWithValue("@draft_manifest_artifact_id", retained.ArtifactId);
					command.Parameters.AddWithValue("@updated_utc", updated.ToString("o", CultureInfo.InvariantCulture));
					command.Parameters.AddWithValue("@local_origin", (int)CollectionOrigin.Local);
					command.Parameters.AddWithValue("@local_collection_id", workingCopy.Collection.StableId);
					if (command.ExecuteNonQuery() != 1)
						throw new CollectionsStoreSchemaException("The Local Collection working copy disappeared while saving edits.");
				}
			});

			CollectionLocalWorkingCopyRecord result = Get(workingCopy.Collection);
			if (result == null)
				throw new CollectionsStoreSchemaException("The Local Collection working copy could not be reloaded after saving edits.");
			return result;
		}

		private byte[] LoadArtifactBytes(string artifactId, string role)
		{
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(artifactId);
			if (artifact == null)
				throw new CollectionsStoreSchemaException("The Local Collection working-copy " + role + " artifact is missing.");
			if (!_artifactStore.VerifyArtifact(artifact.ArtifactId))
				throw new InvalidDataException("The Local Collection working-copy " + role + " failed its integrity check.");
			if (artifact.ByteLength > Int32.MaxValue)
				throw new InvalidDataException("The Local Collection working-copy " + role + " is too large to load.");
			byte[] bytes = new byte[(int)artifact.ByteLength];
			using (Stream stream = _artifactStore.OpenRead(artifact.ArtifactId))
			{
				int offset = 0;
				while (offset < bytes.Length)
				{
					int read = stream.Read(bytes, offset, bytes.Length - offset);
					if (read <= 0) throw new EndOfStreamException("The Local Collection working-copy " + role + " ended before its retained length.");
					offset += read;
				}
				if (stream.ReadByte() != -1)
					throw new InvalidDataException("The Local Collection working-copy " + role + " exceeds its retained length.");
			}
			return bytes;
		}

		private void RequireArtifact(string artifactId, CollectionContentHash expectedHash, long expectedLength,
			bool verifyBytes, string role)
		{
			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(artifactId);
			if (artifact == null || !artifact.ContentHash.Equals(expectedHash) || artifact.ByteLength != expectedLength)
				throw new CollectionsStoreSchemaException("The Local Collection working-copy base " + role + " artifact is missing or mismatched.");
			if (verifyBytes && !_artifactStore.VerifyArtifact(artifactId))
				throw new InvalidDataException("The Local Collection working-copy base " + role + " artifact failed its integrity check.");
		}

		private static void InsertWorkingCopy(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionIdentity localCollection, CollectionRevisionSourceRecord source, DateTime now)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO local_working_copies
    (local_origin, local_collection_id,
     source_origin, source_collection_id, source_revision_id, source_input_kind,
     bundle_hash_value, bundle_byte_length, manifest_entry_name,
     manifest_hash_value, manifest_byte_length, schema_identity, normalizer_version,
     base_manifest_artifact_id, draft_manifest_artifact_id, base_bundle_artifact_id, created_utc, updated_utc)
VALUES
    (@local_origin, @local_collection_id,
     @source_origin, @source_collection_id, @source_revision_id, @source_input_kind,
     @bundle_hash_value, @bundle_byte_length, @manifest_entry_name,
     @manifest_hash_value, @manifest_byte_length, @schema_identity, @normalizer_version,
     @base_manifest_artifact_id, @draft_manifest_artifact_id, @base_bundle_artifact_id, @created_utc, @updated_utc);";
				command.Parameters.AddWithValue("@local_origin", (int)CollectionOrigin.Local);
				command.Parameters.AddWithValue("@local_collection_id", localCollection.StableId);
				command.Parameters.AddWithValue("@source_origin", (int)source.Revision.Collection.Origin);
				command.Parameters.AddWithValue("@source_collection_id", source.Revision.Collection.StableId);
				command.Parameters.AddWithValue("@source_revision_id", source.Revision.StableRevisionId);
				command.Parameters.AddWithValue("@source_input_kind", (int)source.InputKind);
				command.Parameters.AddWithValue("@bundle_hash_value", source.BundleContentHash.Value);
				command.Parameters.AddWithValue("@bundle_byte_length", source.BundleByteLength);
				command.Parameters.AddWithValue("@manifest_entry_name", source.ManifestEntryName);
				command.Parameters.AddWithValue("@manifest_hash_value", source.ManifestSource.ContentHash.Value);
				command.Parameters.AddWithValue("@manifest_byte_length", source.ManifestSource.ByteLength);
				command.Parameters.AddWithValue("@schema_identity", source.ManifestSource.SchemaIdentity);
				command.Parameters.AddWithValue("@normalizer_version", source.ManifestSource.NormalizerVersion);
				command.Parameters.AddWithValue("@base_manifest_artifact_id", source.RawManifestArtifactId);
				command.Parameters.AddWithValue("@draft_manifest_artifact_id", source.RawManifestArtifactId);
				command.Parameters.AddWithValue("@base_bundle_artifact_id", (object)source.RawBundleArtifactId ?? DBNull.Value);
				string timestamp = now.ToString("o", CultureInfo.InvariantCulture);
				command.Parameters.AddWithValue("@created_utc", timestamp);
				command.Parameters.AddWithValue("@updated_utc", timestamp);
				command.ExecuteNonQuery();
			}
		}

		private static CollectionLocalWorkingCopyRecord ReadOne(SQLiteConnection connection, SQLiteTransaction transaction,
			string localCollectionId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT w.source_origin, w.source_collection_id, w.source_revision_id, r.nexus_revision_number,
       w.source_input_kind, w.bundle_hash_value, w.bundle_byte_length, w.manifest_entry_name,
       w.manifest_hash_value, w.manifest_byte_length, w.schema_identity, w.normalizer_version,
       w.base_manifest_artifact_id, w.draft_manifest_artifact_id, w.base_bundle_artifact_id, w.created_utc, w.updated_utc
FROM local_working_copies w
JOIN collection_revisions r
  ON r.origin=w.source_origin AND r.collection_id=w.source_collection_id AND r.revision_id=w.source_revision_id
WHERE w.local_origin=@local_origin AND w.local_collection_id=@local_collection_id;";
				command.Parameters.AddWithValue("@local_origin", (int)CollectionOrigin.Local);
				command.Parameters.AddWithValue("@local_collection_id", localCollectionId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;
					CollectionRevisionIdentity sourceRevision = ReadRevisionIdentity(reader.GetInt32(0), reader.GetString(1),
						reader.GetString(2), reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3));
					CollectionRevisionSourceInputKind inputKind = (CollectionRevisionSourceInputKind)reader.GetInt32(4);
					if (!Enum.IsDefined(typeof(CollectionRevisionSourceInputKind), inputKind) || inputKind == CollectionRevisionSourceInputKind.Unknown)
						throw new CollectionsStoreSchemaException("A Local Collection working copy has an invalid source input kind.");
					var manifestSource = new CollectionManifestSourceSnapshot(CollectionContentHash.FromSha256(reader.GetString(8)),
						reader.GetInt64(9), reader.GetString(10), reader.GetString(11));
					var source = new CollectionRevisionSourceRecord(sourceRevision, inputKind,
						CollectionContentHash.FromSha256(reader.GetString(5)), reader.GetInt64(6), reader.GetString(7),
						manifestSource, reader.IsDBNull(14) ? null : reader.GetString(14), reader.GetString(12));
					string draftArtifactId = reader.IsDBNull(13) ? reader.GetString(12) : reader.GetString(13);
					DateTime created = ParseUtc(reader.GetString(15), "created");
					DateTime updated = ParseUtc(reader.GetString(16), "updated");
					return new CollectionLocalWorkingCopyRecord(CollectionIdentity.FromLocal(Guid.ParseExact(localCollectionId, "D")),
						sourceRevision, source, draftArtifactId, created, updated);
				}
			}
		}

		private static CollectionRevisionIdentity ReadRevisionIdentity(int rawOrigin, string collectionId,
			string revisionId, long? nexusRevisionNumber)
		{
			CollectionOrigin origin = (CollectionOrigin)rawOrigin;
			if (origin == CollectionOrigin.NexusMods)
			{
				if (!nexusRevisionNumber.HasValue)
					throw new CollectionsStoreSchemaException("A Local Collection working copy refers to a Nexus revision without its revision number.");
				return CollectionRevisionIdentity.FromNexus(CollectionIdentity.FromNexus(collectionId), revisionId, nexusRevisionNumber.Value);
			}
			if (origin == CollectionOrigin.Local)
			{
				if (nexusRevisionNumber.HasValue)
					throw new CollectionsStoreSchemaException("A Local Collection working copy refers to a Local revision with a Nexus revision number.");
				return CollectionRevisionIdentity.FromLocal(CollectionIdentity.FromLocal(Guid.ParseExact(collectionId, "D")),
					Guid.ParseExact(revisionId, "D"));
			}
			throw new CollectionsStoreSchemaException("A Local Collection working copy refers to an unsupported source origin.");
		}

		private static DateTime ParseUtc(string value, string field)
		{
			DateTime result;
			if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
				throw new CollectionsStoreSchemaException("A Local Collection working copy has an invalid " + field + " timestamp.");
			return result.Kind == DateTimeKind.Utc ? result : result.ToUniversalTime();
		}

		private static bool DefinitionExists(SQLiteConnection connection, SQLiteTransaction transaction, CollectionIdentity identity)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT 1 FROM collections WHERE origin=@origin AND collection_id=@collection_id LIMIT 1;";
				command.Parameters.AddWithValue("@origin", (int)identity.Origin);
				command.Parameters.AddWithValue("@collection_id", identity.StableId);
				return command.ExecuteScalar() != null;
			}
		}

		private static void RequireRevision(SQLiteConnection connection, SQLiteTransaction transaction, CollectionRevisionIdentity revision)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT 1 FROM collection_revisions
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id LIMIT 1;";
				command.Parameters.AddWithValue("@origin", (int)revision.Collection.Origin);
				command.Parameters.AddWithValue("@collection_id", revision.Collection.StableId);
				command.Parameters.AddWithValue("@revision_id", revision.StableRevisionId);
				if (command.ExecuteScalar() == null)
					throw new InvalidOperationException("The source Collection revision must already be persisted before it can be cloned.");
			}
		}
	}
}
