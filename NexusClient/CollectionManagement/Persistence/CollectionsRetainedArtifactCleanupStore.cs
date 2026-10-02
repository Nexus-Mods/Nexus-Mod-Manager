using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Provides the durable mark/recheck/delete primitive for Collections-owned retained content.
	/// </summary>
	/// <remarks>
	/// Cleanup is intentionally limited to sealed artifacts already tracked by <c>collections.sqlite</c>. It never scans or
	/// deletes the ordinary NMM archive library. A durable tombstone blocks new retained-artifact references before physical
	/// bytes are removed, and an interrupted deletion can be resumed from the persisted tombstone on the next run.
	/// </remarks>
	public sealed class CollectionsRetainedArtifactCleanupStore
	{
		private readonly CollectionsStore _store;
		private readonly CollectionsRetainedArtifactStore _artifactStore;

		/// <summary>
		/// Creates a retained-content cleanup store over an existing Collections feature store.
		/// </summary>
		public CollectionsRetainedArtifactCleanupStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_artifactStore = new CollectionsRetainedArtifactStore(store);
		}

		/// <summary>
		/// Releases obsolete retention after verified Collection removal and reports whether the store is idle enough to collect content.
		/// </summary>
		/// <remarks>
		/// Installed associations, saved captures and the currently open incoming preview keep their Collection content. Any
		/// unfinished operation or potentially active acquisition defers cleanup. Catalog, source provenance, plans and terminal
		/// journals are preserved; exact source bindings can be republished idempotently if the same revision is imported again.
		/// </remarks>
		public bool ReleaseRemovedCollectionContent(CollectionTargetIdentity target, CollectionIdentity retainedPreviewCollection = null)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));

			bool canCollect = false;
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT 1 FROM collection_operations WHERE phase<>@completed
UNION ALL
SELECT 1 FROM collection_acquisition_requests
WHERE state NOT IN (@pending, @verified, @cancelled, @failed)
   OR (state=@pending AND queue_operation_id IS NOT NULL)
LIMIT 1;";
					command.Parameters.AddWithValue("@completed", (int)CollectionOperationPhase.Completed);
					command.Parameters.AddWithValue("@pending", (int)CollectionAcquisitionPersistenceState.PendingUserAction);
					command.Parameters.AddWithValue("@verified", (int)CollectionAcquisitionPersistenceState.Verified);
					command.Parameters.AddWithValue("@cancelled", (int)CollectionAcquisitionPersistenceState.Cancelled);
					command.Parameters.AddWithValue("@failed", (int)CollectionAcquisitionPersistenceState.Failed);
					if (command.ExecuteScalar() != null)
						return;
				}

				var removedCollections = new List<CollectionIdentity>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT DISTINCT o.origin, o.collection_id
FROM collection_operations o
WHERE o.target_fingerprint=@target AND o.kind=@uninstall AND o.phase=@completed AND o.result_state=@committed
  AND NOT EXISTS (SELECT 1 FROM target_associations a WHERE a.origin=o.origin AND a.collection_id=o.collection_id)
  AND NOT EXISTS (SELECT 1 FROM local_captures c WHERE c.origin=o.origin AND c.collection_id=o.collection_id);";
					command.Parameters.AddWithValue("@target", target.Fingerprint);
					command.Parameters.AddWithValue("@uninstall", (int)CollectionOperationKind.UninstallCollectionEffects);
					command.Parameters.AddWithValue("@completed", (int)CollectionOperationPhase.Completed);
					command.Parameters.AddWithValue("@committed", (int)CollectionOperationResultState.Committed);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
						{
							CollectionOrigin origin = (CollectionOrigin)reader.GetInt32(0);
							CollectionIdentity collection;
							if (origin == CollectionOrigin.NexusMods)
								collection = CollectionIdentity.FromNexus(reader.GetString(1));
							else if (origin == CollectionOrigin.Local)
								collection = CollectionIdentity.FromLocal(Guid.ParseExact(reader.GetString(1), "D"));
							else
								throw new CollectionsStoreSchemaException("Retained-content cleanup encountered an unsupported Collection origin.");
							if (!collection.Equals(retainedPreviewCollection))
								removedCollections.Add(collection);
						}
					}
				}
				foreach (CollectionIdentity collection in removedCollections)
					ReleaseRemovedCollectionReferences(connection, transaction, collection);
				canCollect = true;
			});
			return canCollect;
		}

		/// <summary>
		/// Releases completed operation, acquisition and revision ownership for one Collection with no remaining installed baseline.
		/// </summary>
		private static void ReleaseRemovedCollectionReferences(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionIdentity collection)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.Parameters.AddWithValue("@origin", (int)collection.Origin);
				command.Parameters.AddWithValue("@collection_id", collection.StableId);
				command.Parameters.AddWithValue("@operation", (int)CollectionsRetainedArtifactOwnerKind.Operation);
				command.Parameters.AddWithValue("@completed", (int)CollectionOperationPhase.Completed);
				command.CommandText = @"
DELETE FROM retained_artifact_references
WHERE owner_kind=@operation AND owner_id IN (
    SELECT operation_id FROM collection_operations
    WHERE origin=@origin AND collection_id=@collection_id AND phase=@completed);";
				command.ExecuteNonQuery();
			}

			var revisionOwners = new List<string>();
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT revision_id, nexus_revision_number FROM collection_revisions
WHERE origin=@origin AND collection_id=@collection_id;";
				command.Parameters.AddWithValue("@origin", (int)collection.Origin);
				command.Parameters.AddWithValue("@collection_id", collection.StableId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					while (reader.Read())
					{
						CollectionRevisionIdentity revision = collection.Origin == CollectionOrigin.NexusMods
							? CollectionRevisionIdentity.FromNexus(collection, reader.GetString(0), reader.GetInt64(1))
							: CollectionRevisionIdentity.FromLocal(collection, Guid.ParseExact(reader.GetString(0), "D"));
						revisionOwners.Add(CollectionsRevisionSourceStore.GetRevisionOwnerId(revision));
					}
				}
			}
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.Parameters.AddWithValue("@revision", (int)CollectionsRetainedArtifactOwnerKind.Revision);
				command.Parameters.AddWithValue("@download", (int)CollectionsRetainedArtifactOwnerKind.Download);
				command.Parameters.AddWithValue("@owner_id", String.Empty);
				foreach (string ownerId in revisionOwners)
				{
					command.Parameters["@owner_id"].Value = ownerId;
					// Early acquisition plans may never have reached resolved_plans. The durable exact revision identity
					// also releases those requests, instead of leaving their Download references permanently pinned.
					command.CommandText = @"
DELETE FROM retained_artifact_references
WHERE owner_kind=@download AND owner_id IN (
    SELECT request_id FROM collection_acquisition_requests WHERE revision_identity=@owner_id);";
					command.ExecuteNonQuery();
					command.CommandText = "DELETE FROM collection_acquisition_requests WHERE revision_identity=@owner_id;";
					command.ExecuteNonQuery();
					command.CommandText = "DELETE FROM retained_artifact_references WHERE owner_kind=@revision AND owner_id=@owner_id;";
					command.ExecuteNonQuery();
				}
				command.CommandText = @"
UPDATE revision_sources SET raw_bundle_artifact_id=NULL, raw_manifest_artifact_id=NULL
WHERE origin=@origin AND collection_id=@collection_id;";
				command.Parameters.AddWithValue("@origin", (int)collection.Origin);
				command.Parameters.AddWithValue("@collection_id", collection.StableId);
				command.ExecuteNonQuery();
			}
		}

		/// <summary>
		/// Gets a bounded set of sealed tracked artifacts that currently have no durable references and are not already tombstoned.
		/// </summary>
		public IReadOnlyList<string> GetCleanupCandidates(int maximumCount)
		{
			RequirePositiveMaximum(maximumCount);
			return _store.ExecuteRead((connection, transaction) =>
			{
				var result = new List<string>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT a.artifact_id
FROM retained_artifacts a
LEFT JOIN retained_artifact_references r ON r.artifact_id=a.artifact_id
LEFT JOIN retained_artifact_tombstones t ON t.artifact_id=a.artifact_id
WHERE a.sealed=1 AND r.reference_id IS NULL AND t.artifact_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM revision_sources rs WHERE rs.raw_bundle_artifact_id=a.artifact_id)
  AND NOT EXISTS (SELECT 1 FROM revision_sources rs WHERE rs.raw_manifest_artifact_id=a.artifact_id)
  AND NOT EXISTS (SELECT 1 FROM collection_acquisition_requests car WHERE car.verified_artifact_id=a.artifact_id)
  AND NOT EXISTS (SELECT 1 FROM local_capture_packages cp WHERE cp.package_artifact_id=a.artifact_id)
  AND NOT EXISTS (SELECT 1 FROM local_working_copies wc WHERE wc.base_manifest_artifact_id=a.artifact_id OR wc.draft_manifest_artifact_id=a.artifact_id OR wc.base_bundle_artifact_id=a.artifact_id)
ORDER BY a.artifact_id
LIMIT @maximum_count;";
					command.Parameters.AddWithValue("@maximum_count", maximumCount);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							result.Add(reader.GetString(0));
					}
				}
				return result;
			});
		}

		/// <summary>
		/// Gets a bounded set of durable tombstones left by cleanup work that has not yet been finalized.
		/// </summary>
		public IReadOnlyList<string> GetPendingTombstones(int maximumCount)
		{
			RequirePositiveMaximum(maximumCount);
			return _store.ExecuteRead((connection, transaction) =>
			{
				var result = new List<string>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT artifact_id
FROM retained_artifact_tombstones
ORDER BY marked_utc, artifact_id
LIMIT @maximum_count;";
					command.Parameters.AddWithValue("@maximum_count", maximumCount);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						while (reader.Read())
							result.Add(reader.GetString(0));
					}
				}
				return result;
			});
		}

		/// <summary>
		/// Gets whether an artifact is currently protected from new references by a durable cleanup tombstone.
		/// </summary>
		public bool IsMarkedForCleanup(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			return _store.ExecuteRead((connection, transaction) => IsTombstoned(connection, transaction, artifactId));
		}

		/// <summary>
		/// Atomically marks an unreferenced tracked artifact for cleanup.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the tombstone exists after the call; <c>false</c> when the artifact is absent or still referenced.
		/// </returns>
		public bool TryMarkForCleanup(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			bool marked = false;
			_store.ExecuteWrite((connection, transaction) =>
			{
				if (IsTombstoned(connection, transaction, artifactId))
				{
					marked = true;
					return;
				}

				CleanupArtifactRecord artifact = ReadCleanupArtifact(connection, transaction, artifactId);
				if (artifact == null || IsProtectedFromCleanup(connection, transaction, artifactId))
					return;

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT INTO retained_artifact_tombstones (artifact_id, marked_utc)
VALUES (@artifact_id, @marked_utc);";
					command.Parameters.AddWithValue("@artifact_id", artifactId);
					command.Parameters.AddWithValue("@marked_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
					command.ExecuteNonQuery();
				}
				marked = true;
			});
			return marked;
		}

		/// <summary>
		/// Marks and collects one currently unreferenced tracked artifact.
		/// </summary>
		/// <remarks>
		/// This convenience method never treats an absent artifact or a referenced artifact as collectible.
		/// </remarks>
		public bool TryCollectUnreferencedArtifact(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			return TryMarkForCleanup(artifactId) && TryCollectMarkedArtifact(artifactId);
		}

		/// <summary>
		/// Rechecks and physically removes one tombstoned artifact, then finalizes its metadata deletion.
		/// </summary>
		/// <remarks>
		/// If physical deletion throws, the tombstone and artifact metadata are intentionally retained so cleanup can be retried.
		/// A missing physical blob is treated as an interrupted prior delete and the durable metadata is finalized safely.
		/// </remarks>
		public bool TryCollectMarkedArtifact(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			CleanupArtifactRecord candidate = null;

			_store.ExecuteWrite((connection, transaction) =>
			{
				if (!IsTombstoned(connection, transaction, artifactId))
					return;

				if (IsProtectedFromCleanup(connection, transaction, artifactId))
				{
					DeleteTombstone(connection, transaction, artifactId);
					return;
				}

				candidate = ReadCleanupArtifact(connection, transaction, artifactId);
				if (candidate == null)
					DeleteTombstone(connection, transaction, artifactId);
			});

			if (candidate == null)
				return false;

			string expectedRelativePath = CollectionsRetainedArtifactStore.GetCanonicalRelativePath(candidate.Artifact.ContentHash);
			if (!StringComparer.Ordinal.Equals(candidate.RelativePath, expectedRelativePath))
				throw new InvalidDataException("The tombstoned artifact path does not match its content-addressed identity.");

			string physicalPath = _artifactStore.ResolveCanonicalPath(candidate.RelativePath);
			if (File.Exists(physicalPath))
				File.Delete(physicalPath);

			bool finalized = false;
			_store.ExecuteWrite((connection, transaction) =>
			{
				if (!IsTombstoned(connection, transaction, artifactId))
					return;
				if (IsProtectedFromCleanup(connection, transaction, artifactId))
					throw new InvalidDataException("A tombstoned retained artifact acquired durable feature protection during cleanup.");

				CleanupArtifactRecord current = ReadCleanupArtifact(connection, transaction, artifactId);
				if (current == null)
				{
					DeleteTombstone(connection, transaction, artifactId);
					return;
				}
				if (!candidate.Equals(current))
					throw new InvalidDataException("Retained artifact metadata changed while cleanup was in progress.");

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "DELETE FROM retained_artifacts WHERE artifact_id=@artifact_id;";
					command.Parameters.AddWithValue("@artifact_id", artifactId);
					finalized = command.ExecuteNonQuery() == 1;
				}
			});

			return finalized;
		}

		private static CleanupArtifactRecord ReadCleanupArtifact(SQLiteConnection connection, SQLiteTransaction transaction,
			string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed
FROM retained_artifacts
WHERE artifact_id=@artifact_id;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;
					if (reader.GetInt32(5) != 1 || reader.IsDBNull(4))
						throw new InvalidDataException("Cleanup can only operate on sealed retained artifacts with a local content path.");
					if (!StringComparer.OrdinalIgnoreCase.Equals(reader.GetString(1), "sha256"))
						throw new InvalidDataException("Cleanup encountered a retained artifact with an unsupported hash algorithm.");

					CollectionContentHash contentHash;
					try
					{
						contentHash = CollectionContentHash.FromSha256(reader.GetString(2));
					}
					catch (ArgumentException ex)
					{
						throw new InvalidDataException("Cleanup encountered an invalid retained-artifact SHA-256 digest.", ex);
					}

					long byteLength = reader.GetInt64(3);
					if (byteLength < 0)
						throw new InvalidDataException("Cleanup encountered an invalid retained-artifact byte length.");
					string persistedArtifactId = reader.GetString(0);
					if (!StringComparer.Ordinal.Equals(persistedArtifactId, "sha256:" + contentHash.Value))
						throw new InvalidDataException("Cleanup encountered a retained-artifact identity that does not match its SHA-256 digest.");
					var artifact = new CollectionsRetainedArtifact(persistedArtifactId, contentHash, byteLength);
					return new CleanupArtifactRecord(artifact, reader.GetString(4));
				}
			}
		}

		private static bool IsProtectedFromCleanup(SQLiteConnection connection, SQLiteTransaction transaction, string artifactId)
		{
			if (CollectionsRetainedArtifactReferenceStore.IsReferenced(connection, transaction, artifactId))
				return true;

			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT 1 FROM revision_sources WHERE raw_bundle_artifact_id=@artifact_id
UNION ALL
SELECT 1 FROM revision_sources WHERE raw_manifest_artifact_id=@artifact_id
UNION ALL
SELECT 1 FROM collection_acquisition_requests WHERE verified_artifact_id=@artifact_id
UNION ALL
SELECT 1 FROM local_capture_packages WHERE package_artifact_id=@artifact_id
UNION ALL
SELECT 1 FROM local_working_copies WHERE base_manifest_artifact_id=@artifact_id OR draft_manifest_artifact_id=@artifact_id OR base_bundle_artifact_id=@artifact_id
LIMIT 1;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				return command.ExecuteScalar() != null;
			}
		}

		private static bool IsTombstoned(SQLiteConnection connection, SQLiteTransaction transaction, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "SELECT 1 FROM retained_artifact_tombstones WHERE artifact_id=@artifact_id LIMIT 1;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				return command.ExecuteScalar() != null;
			}
		}

		private static void DeleteTombstone(SQLiteConnection connection, SQLiteTransaction transaction, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = "DELETE FROM retained_artifact_tombstones WHERE artifact_id=@artifact_id;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				command.ExecuteNonQuery();
			}
		}

		private static void RequirePositiveMaximum(int maximumCount)
		{
			if (maximumCount <= 0)
				throw new ArgumentOutOfRangeException(nameof(maximumCount), "Cleanup enumeration must request at least one artifact.");
		}

		private sealed class CleanupArtifactRecord : IEquatable<CleanupArtifactRecord>
		{
			public CleanupArtifactRecord(CollectionsRetainedArtifact artifact, string relativePath)
			{
				Artifact = artifact ?? throw new ArgumentNullException(nameof(artifact));
				RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
			}

			public CollectionsRetainedArtifact Artifact { get; }
			public string RelativePath { get; }

			public bool Equals(CleanupArtifactRecord other)
			{
				return !ReferenceEquals(other, null) && Artifact.Equals(other.Artifact) &&
					StringComparer.Ordinal.Equals(RelativePath, other.RelativePath);
			}

			public override bool Equals(object obj)
			{
				return Equals(obj as CleanupArtifactRecord);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					return (Artifact.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(RelativePath);
				}
			}
		}
	}
}
