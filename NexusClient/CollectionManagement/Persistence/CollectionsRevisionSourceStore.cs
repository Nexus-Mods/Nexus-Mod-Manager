using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Identifies how the exact retained collection manifest entered the Collections workflow.
	/// </summary>
	public enum CollectionRevisionSourceInputKind
	{
		/// <summary>No supported source kind has been established.</summary>
		Unknown = 0,

		/// <summary>The imported source was collection.json itself.</summary>
		RawManifest = 1,

		/// <summary>The imported source was an archive containing root collection.json.</summary>
		Archive = 2
	}

	/// <summary>
	/// Describes the immutable retained source provenance for one Collection revision.
	/// </summary>
	/// <remarks>
	/// This record contains content identities and retained-artifact identities only. It never persists a source filesystem
	/// path, provider authorization URL or signed download URL.
	/// </remarks>
	public sealed class CollectionRevisionSourceRecord : IEquatable<CollectionRevisionSourceRecord>
	{
		/// <summary>
		/// Creates one immutable revision-source record.
		/// </summary>
		public CollectionRevisionSourceRecord(CollectionRevisionIdentity revision, CollectionRevisionSourceInputKind inputKind,
			CollectionContentHash bundleContentHash, long bundleByteLength, string manifestEntryName,
			CollectionManifestSourceSnapshot manifestSource, string rawBundleArtifactId, string rawManifestArtifactId)
		{
			Revision = revision ?? throw new ArgumentNullException(nameof(revision));
			if (!Enum.IsDefined(typeof(CollectionRevisionSourceInputKind), inputKind) || inputKind == CollectionRevisionSourceInputKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(inputKind));
			if (bundleContentHash == null)
				throw new ArgumentNullException(nameof(bundleContentHash));
			if (bundleContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("Revision source bundle identity must use SHA-256.", nameof(bundleContentHash));
			if (bundleByteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(bundleByteLength), "Revision source bundle length cannot be negative.");
			if (manifestSource == null)
				throw new ArgumentNullException(nameof(manifestSource));
			if (manifestSource.ContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("Revision manifest identity must use SHA-256.", nameof(manifestSource));

			InputKind = inputKind;
			BundleContentHash = bundleContentHash;
			BundleByteLength = bundleByteLength;
			ManifestEntryName = RequireManifestEntryName(manifestEntryName, nameof(manifestEntryName));
			ManifestSource = manifestSource;
			RawBundleArtifactId = OptionalArtifactId(rawBundleArtifactId, nameof(rawBundleArtifactId));
			RawManifestArtifactId = OptionalArtifactId(rawManifestArtifactId, nameof(rawManifestArtifactId));

			if (InputKind == CollectionRevisionSourceInputKind.RawManifest &&
				(!BundleContentHash.Equals(ManifestSource.ContentHash) || BundleByteLength != ManifestSource.ByteLength))
				throw new ArgumentException("A raw-manifest source must have identical outer and manifest content identity.", nameof(bundleContentHash));
		}

		/// <summary>Gets the immutable Collection revision owning this source.</summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>Gets whether the imported source was collection.json or an outer archive.</summary>
		public CollectionRevisionSourceInputKind InputKind { get; }

		/// <summary>Gets the exact SHA-256 identity of the imported outer source.</summary>
		public CollectionContentHash BundleContentHash { get; }

		/// <summary>Gets the exact imported outer-source byte length.</summary>
		public long BundleByteLength { get; }

		/// <summary>Gets the archive entry/raw-file role which supplied collection.json.</summary>
		public string ManifestEntryName { get; }

		/// <summary>Gets the exact manifest hash/length/schema/normalizer provenance.</summary>
		public CollectionManifestSourceSnapshot ManifestSource { get; }

		/// <summary>Gets the retained outer-bundle artifact identity when a later phase has published one.</summary>
		public string RawBundleArtifactId { get; }

		/// <summary>Gets the retained exact collection.json artifact identity.</summary>
		public string RawManifestArtifactId { get; }

		/// <inheritdoc />
		public bool Equals(CollectionRevisionSourceRecord other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(Revision, other.Revision) &&
				InputKind == other.InputKind &&
				Equals(BundleContentHash, other.BundleContentHash) &&
				BundleByteLength == other.BundleByteLength &&
				StringComparer.Ordinal.Equals(ManifestEntryName, other.ManifestEntryName) &&
				Equals(ManifestSource, other.ManifestSource) &&
				StringComparer.Ordinal.Equals(RawBundleArtifactId, other.RawBundleArtifactId) &&
				StringComparer.Ordinal.Equals(RawManifestArtifactId, other.RawManifestArtifactId);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionRevisionSourceRecord);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = Revision.GetHashCode();
				hashCode = (hashCode * 397) ^ (int)InputKind;
				hashCode = (hashCode * 397) ^ BundleContentHash.GetHashCode();
				hashCode = (hashCode * 397) ^ BundleByteLength.GetHashCode();
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(ManifestEntryName ?? String.Empty);
				hashCode = (hashCode * 397) ^ ManifestSource.GetHashCode();
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(RawBundleArtifactId ?? String.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(RawManifestArtifactId ?? String.Empty);
				return hashCode;
			}
		}

		private static string RequireManifestEntryName(string value, string parameterName)
		{
			string entryName = CollectionIdentityValidation.RequireOpaqueToken(value, parameterName);
			Uri absoluteUri;
			if (entryName.IndexOf('/') >= 0 || entryName.IndexOf('\\') >= 0 || Path.IsPathRooted(entryName) ||
				Uri.TryCreate(entryName, UriKind.Absolute, out absoluteUri))
				throw new ArgumentException("A retained manifest entry name cannot be a filesystem path or absolute URL.", parameterName);
			return entryName;
		}

		private static string OptionalArtifactId(string value, string parameterName)
		{
			return value == null ? null : CollectionIdentityValidation.RequireOpaqueToken(value, parameterName);
		}
	}

	/// <summary>
	/// Persists and reloads the exact retained collection.json source bound to an immutable Collection revision.
	/// </summary>
	/// <remarks>
	/// Exact bytes use the existing C4 content-addressed retained store. Revision lifetime uses the existing retained-reference
	/// owner model; the existing revision_sources row keeps immutable source/provenance metadata. No native installation state is written.
	/// </remarks>
	public sealed class CollectionsRevisionSourceStore
	{
		/// <summary>Gets the exclusive retained-artifact role used for one revision's exact collection.json source.</summary>
		public const string ManifestRole = "manifest";

		/// <summary>Gets the exclusive retained-artifact role used for one revision's exact acquired outer bundle.</summary>
		public const string BundleRole = "bundle";
		private const string HashAlgorithmName = "sha256";
		private readonly CollectionsStore _store;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		/// <summary>
		/// Creates a revision-source store over the existing C4 Collections persistence and retained-content stores.
		/// </summary>
		public CollectionsRevisionSourceStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
			_artifactStore = new CollectionsRetainedArtifactStore(store);
			_referenceStore = new CollectionsRetainedArtifactReferenceStore(store);
		}

		/// <summary>
		/// Retains exact collection.json bytes and binds them immutably to one already-persisted Collection revision.
		/// </summary>
		/// <remarks>
		/// Repeating the exact same source is idempotent. Reusing the same immutable revision for different provenance or
		/// manifest bytes is rejected. The outer bundle identity is provenance only; C6.15.1 retains the manifest bytes themselves.
		/// </remarks>
		public CollectionRevisionSourceRecord RetainManifest(NormalizedCollectionManifest normalizedManifest,
			CollectionRevisionSourceInputKind inputKind, CollectionContentHash bundleContentHash, long bundleByteLength,
			string manifestEntryName, byte[] rawManifestBytes)
		{
			if (normalizedManifest == null)
				throw new ArgumentNullException(nameof(normalizedManifest));
			if (rawManifestBytes == null)
				throw new ArgumentNullException(nameof(rawManifestBytes));

			CollectionRevisionIdentity revision = normalizedManifest.Revision;
			CollectionManifestSourceSnapshot manifestSource = normalizedManifest.Source;
			var candidate = new CollectionRevisionSourceRecord(revision, inputKind, bundleContentHash, bundleByteLength,
				manifestEntryName, manifestSource, null, null);
			ValidateRawManifestBytes(rawManifestBytes, manifestSource);

			CollectionRevisionSourceRecord existing = _store.ExecuteRead((connection, transaction) =>
			{
				RequirePersistedRevision(connection, transaction, revision);
				return ReadSource(connection, transaction, revision);
			});
			if (existing != null && !HasSameProvenance(existing, candidate))
				throw new InvalidOperationException("A persisted Collection revision source is immutable and cannot be rebound to different provenance or manifest identity.");

			CollectionsRetainedArtifact retained;
			using (var stream = new MemoryStream(rawManifestBytes, false))
				retained = _artifactStore.Publish(stream);
			if (!retained.ContentHash.Equals(manifestSource.ContentHash) || retained.ByteLength != manifestSource.ByteLength)
				throw new InvalidDataException("The retained collection manifest does not match its normalized source snapshot.");

			_referenceStore.AcquireExclusiveRoleReference(retained.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
				GetRevisionOwnerId(revision), ManifestRole);

			_store.ExecuteWrite((connection, transaction) =>
			{
				RequirePersistedRevision(connection, transaction, revision);
				CollectionRevisionSourceRecord current = ReadSource(connection, transaction, revision);
				if (current == null)
				{
					InsertSource(connection, transaction, candidate, retained.ArtifactId);
					return;
				}
				if (!HasSameProvenance(current, candidate))
					throw new InvalidOperationException("A persisted Collection revision source is immutable and cannot be rebound to different provenance or manifest identity.");
				if (current.RawManifestArtifactId != null && !StringComparer.Ordinal.Equals(current.RawManifestArtifactId, retained.ArtifactId))
					throw new InvalidOperationException("The immutable Collection revision manifest is already bound to different retained bytes.");
				if (current.RawManifestArtifactId == null)
					SetManifestArtifact(connection, transaction, revision, retained.ArtifactId);
			});

			return GetSource(revision);
		}

		/// <summary>
		/// Retains the exact acquired outer bundle for an already-recorded immutable revision source.
		/// </summary>
		/// <remarks>
		/// The stream is published through the existing C4 content-addressed store and must match the outer hash/length
		/// already sealed in the revision source row. Repeating the exact same bytes is idempotent; changed bytes fail closed.
		/// </remarks>
		public CollectionRevisionSourceRecord RetainBundle(CollectionRevisionIdentity revision, Stream rawBundle,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (rawBundle == null)
				throw new ArgumentNullException(nameof(rawBundle));
			if (!rawBundle.CanRead)
				throw new ArgumentException("The retained Collection bundle stream must be readable.", nameof(rawBundle));

			CollectionRevisionSourceRecord expected = GetSource(revision);
			if (expected == null)
				throw new InvalidOperationException("The exact Collection revision manifest source must be retained before its outer bundle can be bound.");

			CollectionsRetainedArtifact retained = _artifactStore.Publish(rawBundle, cancellationToken);
			if (!retained.ContentHash.Equals(expected.BundleContentHash) || retained.ByteLength != expected.BundleByteLength)
				throw new InvalidDataException("The retained Collection bundle does not match the immutable outer source identity.");

			_referenceStore.AcquireExclusiveRoleReference(retained.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
				GetRevisionOwnerId(revision), BundleRole);

			_store.ExecuteWrite((connection, transaction) =>
			{
				RequirePersistedRevision(connection, transaction, revision);
				CollectionRevisionSourceRecord current = ReadSource(connection, transaction, revision);
				if (current == null)
					throw new CollectionsStoreSchemaException("The Collection revision source disappeared before its outer bundle could be bound.");
				if (!HasSameProvenance(current, expected))
					throw new InvalidOperationException("A persisted Collection revision source changed while its outer bundle was being retained.");
				if (current.RawBundleArtifactId != null && !StringComparer.Ordinal.Equals(current.RawBundleArtifactId, retained.ArtifactId))
					throw new InvalidOperationException("The immutable Collection revision bundle is already bound to different retained bytes.");
				if (current.RawBundleArtifactId == null)
					SetBundleArtifact(connection, transaction, revision, retained.ArtifactId);
			});

			return GetSource(revision);
		}

		/// <summary>
		/// Loads retained source metadata for one exact Collection revision, or <c>null</c> when no source row exists.
		/// </summary>
		public CollectionRevisionSourceRecord GetSource(CollectionRevisionIdentity revision)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));

			return _store.ExecuteRead((connection, transaction) =>
			{
				RequirePersistedRevision(connection, transaction, revision);
				return ReadSource(connection, transaction, revision);
			});
		}

		/// <summary>
		/// Loads exact retained collection.json bytes after verifying revision ownership, source provenance and full blob integrity.
		/// </summary>
		public byte[] LoadManifest(CollectionRevisionIdentity revision, CollectionManifestSourceSnapshot expectedSource)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (expectedSource == null)
				throw new ArgumentNullException(nameof(expectedSource));

			CollectionRevisionSourceRecord source = GetSource(revision);
			if (source == null)
				throw new FileNotFoundException("The Collection revision has no retained recipe source.");
			if (!source.ManifestSource.Equals(expectedSource))
				throw new InvalidDataException("The retained Collection revision source does not match the expected hash, length, schema or normalizer identity.");
			if (String.IsNullOrEmpty(source.RawManifestArtifactId))
				throw new CollectionsStoreSchemaException("The Collection revision source has no retained manifest artifact binding.");

			CollectionsRetainedArtifactReferenceRecord reference = _referenceStore.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Revision, GetRevisionOwnerId(revision), ManifestRole);
			if (reference == null || !StringComparer.Ordinal.Equals(reference.ArtifactId, source.RawManifestArtifactId))
				throw new CollectionsStoreSchemaException("The Collection revision source is missing its exact revision-owned manifest reference.");

			CollectionsRetainedArtifact artifact = _artifactStore.GetArtifact(source.RawManifestArtifactId);
			if (artifact == null || !artifact.ContentHash.Equals(expectedSource.ContentHash) || artifact.ByteLength != expectedSource.ByteLength)
				throw new CollectionsStoreSchemaException("The Collection revision source points to missing or mismatched retained manifest metadata.");
			if (!_artifactStore.VerifyArtifact(artifact.ArtifactId))
				throw new InvalidDataException("The retained Collection revision manifest failed its integrity check.");
			if (artifact.ByteLength > Int32.MaxValue)
				throw new InvalidDataException("The retained Collection revision manifest is too large to load as bounded recipe source bytes.");

			byte[] bytes = new byte[(int)artifact.ByteLength];
			using (Stream stream = _artifactStore.OpenRead(artifact.ArtifactId))
			{
				int offset = 0;
				while (offset < bytes.Length)
				{
					int read = stream.Read(bytes, offset, bytes.Length - offset);
					if (read <= 0)
						throw new EndOfStreamException("The retained Collection revision manifest ended before its sealed byte length.");
					offset += read;
				}
				if (stream.ReadByte() != -1)
					throw new InvalidDataException("The retained Collection revision manifest exceeds its sealed byte length.");
			}
			return bytes;
		}

		/// <summary>
		/// Loads exact retained collection.json bytes for restart/preparation while requiring the expected schema and normalizer contract.
		/// </summary>
		public byte[] LoadManifest(CollectionRevisionIdentity revision, string expectedSchemaIdentity, string expectedNormalizerVersion)
		{
			expectedSchemaIdentity = CollectionIdentityValidation.RequireOpaqueToken(expectedSchemaIdentity, nameof(expectedSchemaIdentity));
			expectedNormalizerVersion = CollectionIdentityValidation.RequireOpaqueToken(expectedNormalizerVersion, nameof(expectedNormalizerVersion));

			CollectionRevisionSourceRecord source = GetSource(revision);
			if (source == null)
				throw new FileNotFoundException("The Collection revision has no retained recipe source.");
			if (!StringComparer.Ordinal.Equals(source.ManifestSource.SchemaIdentity, expectedSchemaIdentity) ||
				!StringComparer.Ordinal.Equals(source.ManifestSource.NormalizerVersion, expectedNormalizerVersion))
				throw new InvalidDataException("The retained Collection revision source was produced by a different schema or normalizer contract.");

			return LoadManifest(revision, source.ManifestSource);
		}

		/// <summary>
		/// Returns the stable retained-reference owner identity for one immutable Collection revision.
		/// </summary>
		public static string GetRevisionOwnerId(CollectionRevisionIdentity revision)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			return revision.ToString();
		}

		private static void ValidateRawManifestBytes(byte[] rawManifestBytes, CollectionManifestSourceSnapshot manifestSource)
		{
			if (rawManifestBytes.LongLength != manifestSource.ByteLength)
				throw new InvalidDataException("The collection manifest byte length does not match its normalized source snapshot.");
			string actualHash;
			using (SHA256 sha = SHA256.Create())
				actualHash = BitConverter.ToString(sha.ComputeHash(rawManifestBytes)).Replace("-", String.Empty).ToLowerInvariant();
			if (!StringComparer.Ordinal.Equals(actualHash, manifestSource.ContentHash.Value))
				throw new InvalidDataException("The collection manifest digest does not match its normalized source snapshot.");
		}

		private static bool HasSameProvenance(CollectionRevisionSourceRecord existing, CollectionRevisionSourceRecord candidate)
		{
			return existing.Revision.Equals(candidate.Revision) &&
				existing.InputKind == candidate.InputKind &&
				existing.BundleContentHash.Equals(candidate.BundleContentHash) &&
				existing.BundleByteLength == candidate.BundleByteLength &&
				StringComparer.Ordinal.Equals(existing.ManifestEntryName, candidate.ManifestEntryName) &&
				existing.ManifestSource.Equals(candidate.ManifestSource);
		}

		private static void InsertSource(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRevisionSourceRecord source, string rawManifestArtifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
INSERT INTO revision_sources
    (origin, collection_id, revision_id, source_input_kind,
     bundle_hash_algorithm, bundle_hash_value, bundle_byte_length,
     manifest_entry_name, manifest_hash_algorithm, manifest_hash_value, manifest_byte_length,
     schema_identity, normalizer_version, raw_bundle_artifact_id, raw_manifest_artifact_id)
VALUES
    (@origin, @collection_id, @revision_id, @source_input_kind,
     @bundle_hash_algorithm, @bundle_hash_value, @bundle_byte_length,
     @manifest_entry_name, @manifest_hash_algorithm, @manifest_hash_value, @manifest_byte_length,
     @schema_identity, @normalizer_version, NULL, @raw_manifest_artifact_id);";
				AddRevisionIdentityParameters(command, source.Revision);
				command.Parameters.AddWithValue("@source_input_kind", (int)source.InputKind);
				command.Parameters.AddWithValue("@bundle_hash_algorithm", HashAlgorithmName);
				command.Parameters.AddWithValue("@bundle_hash_value", source.BundleContentHash.Value);
				command.Parameters.AddWithValue("@bundle_byte_length", source.BundleByteLength);
				command.Parameters.AddWithValue("@manifest_entry_name", source.ManifestEntryName);
				command.Parameters.AddWithValue("@manifest_hash_algorithm", HashAlgorithmName);
				command.Parameters.AddWithValue("@manifest_hash_value", source.ManifestSource.ContentHash.Value);
				command.Parameters.AddWithValue("@manifest_byte_length", source.ManifestSource.ByteLength);
				command.Parameters.AddWithValue("@schema_identity", source.ManifestSource.SchemaIdentity);
				command.Parameters.AddWithValue("@normalizer_version", source.ManifestSource.NormalizerVersion);
				command.Parameters.AddWithValue("@raw_manifest_artifact_id", rawManifestArtifactId);
				command.ExecuteNonQuery();
			}
		}

		private static void SetBundleArtifact(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRevisionIdentity revision, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE revision_sources
SET raw_bundle_artifact_id=@raw_bundle_artifact_id
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id AND raw_bundle_artifact_id IS NULL;";
				AddRevisionIdentityParameters(command, revision);
				command.Parameters.AddWithValue("@raw_bundle_artifact_id", artifactId);
				if (command.ExecuteNonQuery() != 1)
					throw new CollectionsStoreSchemaException("The Collection revision bundle artifact binding could not be published atomically.");
			}
		}

		private static void SetManifestArtifact(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRevisionIdentity revision, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
UPDATE revision_sources
SET raw_manifest_artifact_id=@raw_manifest_artifact_id
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id AND raw_manifest_artifact_id IS NULL;";
				AddRevisionIdentityParameters(command, revision);
				command.Parameters.AddWithValue("@raw_manifest_artifact_id", artifactId);
				if (command.ExecuteNonQuery() != 1)
					throw new CollectionsStoreSchemaException("The Collection revision manifest artifact binding could not be published atomically.");
			}
		}

		private static CollectionRevisionSourceRecord ReadSource(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRevisionIdentity revision)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT rs.source_input_kind,
       rs.bundle_hash_algorithm, rs.bundle_hash_value, rs.bundle_byte_length,
       rs.manifest_entry_name, rs.manifest_hash_algorithm, rs.manifest_hash_value, rs.manifest_byte_length,
       rs.schema_identity, rs.normalizer_version, rs.raw_bundle_artifact_id, rs.raw_manifest_artifact_id,
       cr.nexus_revision_number
FROM revision_sources rs
JOIN collection_revisions cr
  ON cr.origin=rs.origin AND cr.collection_id=rs.collection_id AND cr.revision_id=rs.revision_id
WHERE rs.origin=@origin AND rs.collection_id=@collection_id AND rs.revision_id=@revision_id;";
				AddRevisionIdentityParameters(command, revision);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						return null;
					try
					{
						long? persistedNumber = reader.IsDBNull(12) ? (long?)null : reader.GetInt64(12);
						if (persistedNumber != revision.NexusRevisionNumber)
							throw new CollectionsStoreSchemaException("A persisted Collection revision source resolves to a different concrete revision number.");
						CollectionRevisionSourceInputKind inputKind = (CollectionRevisionSourceInputKind)reader.GetInt32(0);
						if (!Enum.IsDefined(typeof(CollectionRevisionSourceInputKind), inputKind) || inputKind == CollectionRevisionSourceInputKind.Unknown)
							throw new CollectionsStoreSchemaException("A persisted Collection revision source has an invalid input kind.");
						CollectionContentHash bundleHash = ReadSha256(reader, 1, 2, "bundle");
						CollectionContentHash manifestHash = ReadSha256(reader, 5, 6, "manifest");
						long bundleByteLength = reader.GetInt64(3);
						long manifestByteLength = reader.GetInt64(7);
						if (bundleByteLength < 0 || manifestByteLength < 0)
							throw new CollectionsStoreSchemaException("A persisted Collection revision source has an invalid byte length.");
						var manifestSource = new CollectionManifestSourceSnapshot(manifestHash, manifestByteLength,
							reader.GetString(8), reader.GetString(9));
						return new CollectionRevisionSourceRecord(revision, inputKind, bundleHash, bundleByteLength,
							reader.GetString(4), manifestSource,
							reader.IsDBNull(10) ? null : reader.GetString(10),
							reader.IsDBNull(11) ? null : reader.GetString(11));
					}
					catch (CollectionsStoreSchemaException)
					{
						throw;
					}
					catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is InvalidCastException)
					{
						throw new CollectionsStoreSchemaException("A persisted Collection revision source contains invalid immutable metadata.", ex);
					}
				}
			}
		}

		private static CollectionContentHash ReadSha256(SQLiteDataReader reader, int algorithmOrdinal, int valueOrdinal, string description)
		{
			if (!StringComparer.OrdinalIgnoreCase.Equals(reader.GetString(algorithmOrdinal), HashAlgorithmName))
				throw new CollectionsStoreSchemaException("A persisted Collection revision " + description + " uses an unsupported content hash algorithm.");
			return CollectionContentHash.FromSha256(reader.GetString(valueOrdinal));
		}

		private static void RequirePersistedRevision(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionRevisionIdentity revision)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT nexus_revision_number
FROM collection_revisions
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id;";
				AddRevisionIdentityParameters(command, revision);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read())
						throw new InvalidOperationException("The exact Collection revision must be persisted before retaining its recipe source.");
					long? persistedNumber = reader.IsDBNull(0) ? (long?)null : reader.GetInt64(0);
					if (persistedNumber != revision.NexusRevisionNumber)
						throw new InvalidOperationException("The supplied Collection revision identity does not match the persisted concrete revision.");
				}
			}
		}

		private static void AddRevisionIdentityParameters(SQLiteCommand command, CollectionRevisionIdentity revision)
		{
			command.Parameters.AddWithValue("@origin", (int)revision.Collection.Origin);
			command.Parameters.AddWithValue("@collection_id", revision.Collection.StableId);
			command.Parameters.AddWithValue("@revision_id", revision.StableRevisionId);
		}
	}
}
