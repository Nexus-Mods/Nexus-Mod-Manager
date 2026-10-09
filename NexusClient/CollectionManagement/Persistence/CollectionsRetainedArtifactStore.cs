using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Publishes and reads verified immutable blobs owned by Collections storage.
	/// </summary>
	/// <remarks>
	/// Publication deliberately performs file I/O before the short SQLite metadata transaction: source bytes are streamed
	/// to same-volume staging while SHA-256 is calculated, flushed, atomically renamed to their content-addressed path,
	/// and only then recorded as sealed. A crash or metadata failure may therefore leave a safely unreferenced blob, never
	/// a durable artifact row that points at a half-written file. Reference/lease ownership is handled by
	/// <see cref="CollectionsRetainedArtifactReferenceStore"/>; garbage collection remains a separate cleanup boundary.
	/// </remarks>
	public sealed class CollectionsRetainedArtifactStore
	{
		private const string HashAlgorithmName = "sha256";
		private const string BlobDirectoryName = "sha256";
		private const string StagingDirectoryName = ".staging";
		private const string BlobExtension = ".blob";
		private const int CopyBufferSize = 1024 * 1024;
		private const int ArtifactMetadataBatchSize = 128;

		private readonly CollectionsStore _store;
		private readonly object _verificationCacheLock = new object();
		private readonly Dictionary<string, ArtifactVerificationStamp> _verifiedArtifacts =
			new Dictionary<string, ArtifactVerificationStamp>(StringComparer.Ordinal);
		private readonly Dictionary<string, PublishedMd5Stamp> _publishedMd5 =
			new Dictionary<string, PublishedMd5Stamp>(StringComparer.Ordinal);
		private readonly Dictionary<string, PublishedSourceStamp> _publishedSources =
			new Dictionary<string, PublishedSourceStamp>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Creates a retained-artifact store over an existing Collections feature store.
		/// </summary>
		public CollectionsRetainedArtifactStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		/// <summary>Prepares one immutable file publication without opening a SQLite write transaction.</summary>
		internal PreparedFilePublication PrepareFilePublication(string sourcePath, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(sourcePath))
				throw new ArgumentException("A retained-artifact source path is required.", nameof(sourcePath));

			string fullPath = Path.GetFullPath(sourcePath);
			Directory.CreateDirectory(_store.RetainedContentDirectory);
			string stagingDirectory = Path.Combine(_store.RetainedContentDirectory, StagingDirectoryName);
			Directory.CreateDirectory(stagingDirectory);
			string stagingPath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");

			try
			{
				using (FileStream source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
					CopyBufferSize, FileOptions.SequentialScan))
				{
					long initialLength = source.Length;
					long initialLastWriteUtcTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
					CopyResult copied = CopyAndHash(source, stagingPath, cancellationToken);
					var contentHash = CollectionContentHash.FromSha256(copied.HashValue);
					var artifact = new CollectionsRetainedArtifact(CreateArtifactId(contentHash), contentHash, copied.ByteLength);
					string relativePath = GetCanonicalRelativePath(contentHash);
					string publishedPath = ResolveCanonicalPath(relativePath);
					PublishStagedBlob(stagingPath, publishedPath, contentHash, copied.ByteLength, cancellationToken);
					stagingPath = null;
					long finalLength = source.Length;
					long finalLastWriteUtcTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
					bool sourceStable = initialLength == finalLength && initialLength == copied.ByteLength &&
						initialLastWriteUtcTicks == finalLastWriteUtcTicks;
					return new PreparedFilePublication(artifact, relativePath, publishedPath, copied.Md5Value, fullPath,
						finalLength, finalLastWriteUtcTicks, sourceStable);
				}
			}
			finally
			{
				DeleteTemporaryFile(stagingPath);
			}
		}

		/// <summary>Commits metadata for already-published immutable blobs in one bounded SQLite transaction.</summary>
		internal void PublishPreparedMetadata(IList<PreparedFilePublication> prepared)
		{
			if (prepared == null) throw new ArgumentNullException(nameof(prepared));
			if (prepared.Count == 0) return;

			_store.ExecuteWrite((connection, transaction) =>
			{
				foreach (PreparedFilePublication publication in prepared)
				{
					if (publication == null)
						throw new ArgumentException("A prepared retained-artifact batch cannot contain null entries.", nameof(prepared));
					CollectionsRetainedArtifact persisted = ReadArtifactByContent(connection, transaction,
						publication.Artifact.ContentHash, publication.Artifact.ByteLength);
					if (persisted != null)
					{
						ValidatePersistedArtifact(connection, transaction, persisted, publication.RelativePath);
						continue;
					}
					var publishedFile = new FileInfo(publication.PublishedPath);
					if (!publishedFile.Exists || publishedFile.Length != publication.Artifact.ByteLength)
						throw new IOException("Retained content disappeared before its sealed metadata could be published.");
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
INSERT INTO retained_artifacts
    (artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed)
VALUES
    (@artifact_id, @hash_algorithm, @hash_value, @byte_length, @relative_path, 1);";
						command.Parameters.AddWithValue("@artifact_id", publication.Artifact.ArtifactId);
						command.Parameters.AddWithValue("@hash_algorithm", HashAlgorithmName);
						command.Parameters.AddWithValue("@hash_value", publication.Artifact.ContentHash.Value);
						command.Parameters.AddWithValue("@byte_length", publication.Artifact.ByteLength);
						command.Parameters.AddWithValue("@relative_path", publication.RelativePath);
						command.ExecuteNonQuery();
					}
				}
			});

			foreach (PreparedFilePublication publication in prepared)
			{
				RememberPublishedMd5(publication.Artifact, publication.PublishedPath, publication.Md5Value);
				if (publication.SourceStable)
					RememberPublishedSource(publication.SourcePath, publication.Artifact, publication.SourceLength,
						publication.SourceLastWriteUtcTicks);
			}
		}

		/// <summary>
		/// Streams a readable source into immutable Collections storage and returns its sealed content identity.
		/// </summary>
		public CollectionsRetainedArtifact Publish(Stream source)
		{
			return Publish(source, CancellationToken.None);
		}

		/// <summary>
		/// Streams a readable source into immutable Collections storage with cooperative cancellation.
		/// </summary>
		public CollectionsRetainedArtifact Publish(Stream source, CancellationToken cancellationToken)
		{
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (!source.CanRead)
				throw new ArgumentException("The retained-artifact source stream must be readable.", nameof(source));

			// Fail before copying large payloads when the authoritative feature store cannot accept a durable artifact row.
			_store.OpenExisting();

			Directory.CreateDirectory(_store.RetainedContentDirectory);
			string stagingDirectory = Path.Combine(_store.RetainedContentDirectory, StagingDirectoryName);
			Directory.CreateDirectory(stagingDirectory);
			string stagingPath = Path.Combine(stagingDirectory, Guid.NewGuid().ToString("N") + ".tmp");

			try
			{
				CopyResult copied = CopyAndHash(source, stagingPath, cancellationToken);
				var contentHash = CollectionContentHash.FromSha256(copied.HashValue);
				string artifactId = CreateArtifactId(contentHash);
				string relativePath = GetCanonicalRelativePath(contentHash);
				string publishedPath = ResolveCanonicalPath(relativePath);

				PublishStagedBlob(stagingPath, publishedPath, contentHash, copied.ByteLength, cancellationToken);
				stagingPath = null;

				CollectionsRetainedArtifact persisted = PublishMetadata(
					new CollectionsRetainedArtifact(artifactId, contentHash, copied.ByteLength), relativePath);
				RememberPublishedMd5(persisted, publishedPath, copied.Md5Value);
				return persisted;
			}
			finally
			{
				DeleteTemporaryFile(stagingPath);
			}
		}

		/// <summary>
		/// Retains an exact file snapshot while denying concurrent writers/deleters for the duration of the copy.
		/// </summary>
		public CollectionsRetainedArtifact PublishFile(string sourcePath)
		{
			return PublishFile(sourcePath, CancellationToken.None);
		}

		/// <summary>
		/// Retains an exact file snapshot with cooperative cancellation while the source handle is held stable.
		/// </summary>
		public CollectionsRetainedArtifact PublishFile(string sourcePath, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(sourcePath))
				throw new ArgumentException("A retained-artifact source path is required.", nameof(sourcePath));

			string fullPath = Path.GetFullPath(sourcePath);
			using (FileStream source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				long initialLength = source.Length;
				long initialLastWriteUtcTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
				CollectionsRetainedArtifact artifact = Publish(source, cancellationToken);
				long finalLength = source.Length;
				long finalLastWriteUtcTicks = File.GetLastWriteTimeUtc(fullPath).Ticks;
				if (initialLength == finalLength && initialLength == artifact.ByteLength &&
					initialLastWriteUtcTicks == finalLastWriteUtcTicks)
				{
					RememberPublishedSource(fullPath, artifact, finalLength, finalLastWriteUtcTicks);
				}
				return artifact;
			}
		}

		/// <summary>
		/// Returns whether a mutable source file still has the same size/write stamp as the exact bytes just published by this store.
		/// </summary>
		/// <remarks>
		/// This is a process-local preparation optimization only. It never replaces the native installer's final SHA-256 check
		/// immediately before mutation, and restarted processes intentionally have no source-file proof.
		/// </remarks>
		internal bool IsPublishedSourceCurrent(string sourcePath, CollectionsRetainedArtifact artifact)
		{
			if (artifact == null || string.IsNullOrWhiteSpace(sourcePath))
				return false;

			string fullPath = Path.GetFullPath(sourcePath);
			var info = new FileInfo(fullPath);
			if (!info.Exists || info.Length != artifact.ByteLength)
				return false;

			lock (_verificationCacheLock)
			{
				PublishedSourceStamp stamp;
				return _publishedSources.TryGetValue(fullPath, out stamp) &&
					stamp.ByteLength == info.Length && stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks &&
					StringComparer.Ordinal.Equals(stamp.HashValue, artifact.ContentHash.Value);
			}
		}

		/// <summary>
		/// Loads one sealed retained-artifact descriptor by stable identity, or <c>null</c> when no metadata row exists.
		/// </summary>
		public CollectionsRetainedArtifact GetArtifact(string artifactId)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			return _store.ExecuteRead((connection, transaction) => ReadArtifact(connection, transaction, artifactId));
		}

		/// <summary>
		/// Loads one sealed retained artifact by exact SHA-256 content identity, or <c>null</c> when it is absent.
		/// </summary>
		public CollectionsRetainedArtifact GetArtifact(CollectionContentHash contentHash)
		{
			if (contentHash == null)
				throw new ArgumentNullException(nameof(contentHash));
			if (contentHash.Algorithm != CollectionContentHashAlgorithm.Sha256)
				throw new ArgumentException("Only SHA-256 retained content identities are supported.", nameof(contentHash));

			return GetArtifact(CreateArtifactId(contentHash));
		}

		/// <summary>
		/// Opens a retained blob for read-only streaming after validating its durable metadata and exact length.
		/// </summary>
		/// <remarks>
		/// This method does not re-hash the complete blob. Call <see cref="VerifyArtifact"/> before a trust-sensitive use
		/// such as restoration when external tampering since publication must be detected.
		/// </remarks>
		public Stream OpenRead(string artifactId)
		{
			CollectionsRetainedArtifact artifact = GetArtifact(artifactId);
			if (artifact == null)
				throw new FileNotFoundException("The retained artifact is not recorded in the Collections feature store.", artifactId);

			string path = GetValidatedPhysicalPath(artifact);
			var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);
			if (stream.Length != artifact.ByteLength)
			{
				stream.Dispose();
				throw new InvalidDataException("The retained artifact length no longer matches its sealed metadata.");
			}

			CollectionPerformanceMetrics.RecordRetainedArtifactOpen(artifact.ArtifactId);
			return stream;
		}

		/// <summary>Returns the content-addressed physical path only after cryptographically verifying the sealed retained bytes.</summary>
		internal string GetVerifiedReadOnlyPath(string artifactId, CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifact artifact = GetArtifact(artifactId);
			if (artifact == null)
				throw new FileNotFoundException("The retained artifact is not recorded in the Collections feature store.", artifactId);
			if (!VerifyArtifact(artifactId, cancellationToken))
				throw new InvalidDataException("The retained artifact bytes no longer match their sealed SHA-256 identity.");
			return GetValidatedPhysicalPath(artifact);
		}

		/// <summary>
		/// Recomputes the cryptographic digest of one retained artifact and reports whether its immutable bytes still match.
		/// </summary>
		public bool VerifyArtifact(string artifactId)
		{
			return VerifyArtifact(artifactId, CancellationToken.None);
		}

		/// <summary>
		/// Recomputes the cryptographic digest of one retained artifact with cooperative cancellation.
		/// </summary>
		public bool VerifyArtifact(string artifactId, CancellationToken cancellationToken)
		{
			CollectionsRetainedArtifact artifact = GetArtifact(artifactId);
			if (artifact == null)
				return false;

			string path = GetValidatedPhysicalPath(artifact);
			return VerifyArtifactBytes(artifact, path, cancellationToken);
		}

		/// <summary>Loads an exact artifact set in one metadata transaction, then verifies its bytes outside the transaction.</summary>
		/// <remarks>Only identities in verifiedArtifactIds passed integrity verification. Missing metadata is omitted from the returned descriptors.</remarks>
		internal IReadOnlyDictionary<string, CollectionsRetainedArtifact> VerifyArtifacts(IEnumerable<string> artifactIds,
			CancellationToken cancellationToken, out ISet<string> verifiedArtifactIds)
		{
			if (artifactIds == null) throw new ArgumentNullException(nameof(artifactIds));
			cancellationToken.ThrowIfCancellationRequested();
			var ids = new HashSet<string>(StringComparer.Ordinal);
			foreach (string id in artifactIds)
				ids.Add(CollectionIdentityValidation.RequireOpaqueToken(id, nameof(artifactIds)));
			if (ids.Count == 0)
			{
				verifiedArtifactIds = new HashSet<string>(StringComparer.Ordinal);
				return new Dictionary<string, CollectionsRetainedArtifact>(StringComparer.Ordinal);
			}
			var requested = new List<string>(ids);
			requested.Sort(StringComparer.Ordinal);
			Dictionary<string, CollectionsRetainedArtifact> artifacts = _store.ExecuteRead((connection, transaction) =>
			{
				var result = new Dictionary<string, CollectionsRetainedArtifact>(StringComparer.Ordinal);
				// Keep each indexed lookup below legacy SQLite parameter limits without scanning unrelated retained content.
				for (int offset = 0; offset < requested.Count; offset += ArtifactMetadataBatchSize)
				{
					cancellationToken.ThrowIfCancellationRequested();
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						int count = Math.Min(ArtifactMetadataBatchSize, requested.Count - offset);
						var parameters = new string[count];
						for (int index = 0; index < count; index++)
						{
							parameters[index] = "@artifact_id" + index;
							command.Parameters.AddWithValue(parameters[index], requested[offset + index]);
						}
						command.CommandText = @"
SELECT artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed
FROM retained_artifacts WHERE artifact_id IN (" + String.Join(",", parameters) + ");";
						using (SQLiteDataReader reader = command.ExecuteReader())
						{
							while (reader.Read())
							{
								cancellationToken.ThrowIfCancellationRequested();
								CollectionsRetainedArtifact artifact = ReadArtifact(reader);
								if (!StringComparer.Ordinal.Equals(reader.GetString(4), GetCanonicalRelativePath(artifact.ContentHash)))
									throw new InvalidDataException("The retained artifact path does not match its content-addressed identity.");
								result.Add(artifact.ArtifactId, artifact);
							}
						}
					}
				}
				return result;
			});
			verifiedArtifactIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (CollectionsRetainedArtifact artifact in artifacts.Values)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (VerifyArtifactBytes(artifact, ResolveCanonicalPath(GetCanonicalRelativePath(artifact.ContentHash)), cancellationToken))
					verifiedArtifactIds.Add(artifact.ArtifactId);
			}
			return artifacts;
		}

		/// <summary>Verifies one already validated retained path using the existing size/write-stamp integrity cache.</summary>
		private bool VerifyArtifactBytes(CollectionsRetainedArtifact artifact, string path, CancellationToken cancellationToken)
		{
			if (!File.Exists(path))
				return false;

			var info = new FileInfo(path);
			if (info.Length != artifact.ByteLength)
				return false;

			if (IsVerifiedArtifactCached(artifact, info))
			{
				CollectionPerformanceMetrics.RecordRetainedArtifactVerificationCacheHit();
				return true;
			}

			long performanceStarted = CollectionPerformanceMetrics.StartTiming();
			string actualHash = ComputeFileHash(path, cancellationToken);
			CollectionPerformanceMetrics.RecordRetainedArtifactVerificationHash(performanceStarted, artifact.ByteLength);
			if (!StringComparer.Ordinal.Equals(actualHash, artifact.ContentHash.Value))
			{
				ForgetVerifiedArtifact(artifact.ArtifactId);
				return false;
			}

			RememberVerifiedArtifact(artifact, path);
			return true;
		}

		/// <summary>
		/// Returns an MD5 digest calculated during this process' publication pass when available.
		/// </summary>
		/// <remarks>
		/// The digest is an optimization for provider identity lookup only; SHA-256 remains the retained-artifact identity.
		/// The cache is intentionally process-local and is not persisted as trusted state.
		/// </remarks>
		internal bool TryGetPublishedMd5(string artifactId, out string md5)
		{
			artifactId = CollectionIdentityValidation.RequireOpaqueToken(artifactId, nameof(artifactId));
			CollectionsRetainedArtifact artifact = GetArtifact(artifactId);
			if (artifact == null)
			{
				md5 = null;
				return false;
			}

			string path = GetValidatedPhysicalPath(artifact);
			var info = new FileInfo(path);
			lock (_verificationCacheLock)
			{
				PublishedMd5Stamp stamp;
				if (_publishedMd5.TryGetValue(artifactId, out stamp) && info.Exists &&
					stamp.ByteLength == info.Length && stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
				{
					md5 = stamp.Md5;
					return true;
				}
			}
			md5 = null;
			return false;
		}

		/// <summary>
		/// Promotes bytes just published by this process into the process-local integrity cache without rereading the blob.
		/// </summary>
		/// <remarks>
		/// Publication already calculated the artifact SHA-256 while copying. Promotion is allowed only while the retained file's
		/// length and last-write stamp still match the publication stamp. Restarted processes have no such stamp and therefore
		/// perform a normal cryptographic <see cref="VerifyArtifact(string, CancellationToken)"/> pass.
		/// </remarks>
		internal bool PromotePublishedArtifactVerification(CollectionsRetainedArtifact artifact)
		{
			if (artifact == null)
				throw new ArgumentNullException(nameof(artifact));
			string path = GetValidatedPhysicalPath(artifact);
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != artifact.ByteLength)
				return false;

			lock (_verificationCacheLock)
			{
				PublishedMd5Stamp published;
				if (!_publishedMd5.TryGetValue(artifact.ArtifactId, out published) ||
					published.ByteLength != info.Length || published.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
					return false;
				_verifiedArtifacts[artifact.ArtifactId] = new ArtifactVerificationStamp(
					artifact.ContentHash.Value, info.Length, info.LastWriteTimeUtc.Ticks);
				return true;
			}
		}

		private CollectionsRetainedArtifact PublishMetadata(CollectionsRetainedArtifact candidate, string relativePath)
		{
			CollectionsRetainedArtifact persisted = null;
			_store.ExecuteWrite((connection, transaction) =>
			{
				persisted = ReadArtifactByContent(connection, transaction, candidate.ContentHash, candidate.ByteLength);
				if (persisted != null)
				{
					ValidatePersistedArtifact(connection, transaction, persisted, relativePath);
					return;
				}

				string physicalPath = ResolveCanonicalPath(relativePath);
				var publishedFile = new FileInfo(physicalPath);
				if (!publishedFile.Exists || publishedFile.Length != candidate.ByteLength)
					throw new IOException("Retained content disappeared before its sealed metadata could be published.");

				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
INSERT INTO retained_artifacts
    (artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed)
VALUES
    (@artifact_id, @hash_algorithm, @hash_value, @byte_length, @relative_path, 1);";
					command.Parameters.AddWithValue("@artifact_id", candidate.ArtifactId);
					command.Parameters.AddWithValue("@hash_algorithm", HashAlgorithmName);
					command.Parameters.AddWithValue("@hash_value", candidate.ContentHash.Value);
					command.Parameters.AddWithValue("@byte_length", candidate.ByteLength);
					command.Parameters.AddWithValue("@relative_path", relativePath);
					command.ExecuteNonQuery();
				}

				persisted = candidate;
			});

			return persisted;
		}

		private static CollectionsRetainedArtifact ReadArtifact(SQLiteConnection connection, SQLiteTransaction transaction, string artifactId)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed
FROM retained_artifacts
WHERE artifact_id = @artifact_id;";
				command.Parameters.AddWithValue("@artifact_id", artifactId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadArtifact(reader) : null;
				}
			}
		}

		private static CollectionsRetainedArtifact ReadArtifactByContent(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionContentHash contentHash, long byteLength)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.Transaction = transaction;
				command.CommandText = @"
SELECT artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed
FROM retained_artifacts
WHERE hash_algorithm = @hash_algorithm AND hash_value = @hash_value AND byte_length = @byte_length;";
				command.Parameters.AddWithValue("@hash_algorithm", HashAlgorithmName);
				command.Parameters.AddWithValue("@hash_value", contentHash.Value);
				command.Parameters.AddWithValue("@byte_length", byteLength);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					return reader.Read() ? ReadArtifact(reader) : null;
				}
			}
		}

		private static CollectionsRetainedArtifact ReadArtifact(SQLiteDataReader reader)
		{
			string algorithm = reader.GetString(1);
			if (!StringComparer.OrdinalIgnoreCase.Equals(algorithm, HashAlgorithmName))
				throw new InvalidDataException("The retained artifact uses an unsupported hash algorithm: " + algorithm);
			if (reader.IsDBNull(4))
				throw new InvalidDataException("A sealed retained artifact is missing its local content path.");
			if (reader.GetInt32(5) != 1)
				throw new InvalidDataException("Collections cannot expose an unsealed retained artifact as immutable content.");

			long byteLength = reader.GetInt64(3);
			if (byteLength < 0)
				throw new InvalidDataException("The retained artifact contains an invalid negative byte length.");

			CollectionContentHash contentHash;
			try
			{
				contentHash = CollectionContentHash.FromSha256(reader.GetString(2));
			}
			catch (ArgumentException ex)
			{
				throw new InvalidDataException("The retained artifact contains an invalid SHA-256 digest.", ex);
			}

			string artifactId = reader.GetString(0);
			if (!StringComparer.Ordinal.Equals(artifactId, CreateArtifactId(contentHash)))
				throw new InvalidDataException("The retained artifact identity does not match its content-addressed SHA-256 digest.");

			return new CollectionsRetainedArtifact(artifactId, contentHash, byteLength);
		}

		private static void ValidatePersistedArtifact(SQLiteConnection connection, SQLiteTransaction transaction,
			CollectionsRetainedArtifact artifact, string expectedRelativePath)
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
				command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
				using (SQLiteDataReader reader = command.ExecuteReader())
				{
					if (!reader.Read() || reader.IsDBNull(0) || reader.GetInt32(1) != 1 ||
						!StringComparer.Ordinal.Equals(reader.GetString(0), expectedRelativePath))
						throw new InvalidDataException("A retained artifact identity cannot be rebound to a different path or unsealed record.");
					if (reader.GetInt32(2) != 0)
						throw new InvalidOperationException("The retained artifact is already tombstoned for cleanup.");
				}
			}
		}

		private string GetValidatedPhysicalPath(CollectionsRetainedArtifact artifact)
		{
			string persistedRelativePath = _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT relative_path, sealed FROM retained_artifacts WHERE artifact_id=@artifact_id;";
					command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
					using (SQLiteDataReader reader = command.ExecuteReader())
					{
						if (!reader.Read() || reader.IsDBNull(0) || reader.GetInt32(1) != 1)
							throw new InvalidDataException("The retained artifact is not sealed with a local content path.");
						return reader.GetString(0);
					}
				}
			});

			string expectedRelativePath = GetCanonicalRelativePath(artifact.ContentHash);
			if (!StringComparer.Ordinal.Equals(persistedRelativePath, expectedRelativePath))
				throw new InvalidDataException("The retained artifact path does not match its content-addressed identity.");

			return ResolveCanonicalPath(expectedRelativePath);
		}

		private static CopyResult CopyAndHash(Stream source, string stagingPath, CancellationToken cancellationToken)
		{
			byte[] buffer = new byte[CopyBufferSize];
			long byteLength = 0;
			using (SHA256 sha256 = SHA256.Create())
			using (MD5 md5 = MD5.Create())
			using (FileStream target = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					target.Write(buffer, 0, read);
					sha256.TransformBlock(buffer, 0, read, buffer, 0);
					md5.TransformBlock(buffer, 0, read, buffer, 0);
					byteLength += read;
				}

				cancellationToken.ThrowIfCancellationRequested();
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				md5.TransformFinalBlock(new byte[0], 0, 0);
				target.Flush(true);
				return new CopyResult(ToHex(sha256.Hash), ToHex(md5.Hash), byteLength);
			}
		}

		private static void PublishStagedBlob(string stagingPath, string publishedPath, CollectionContentHash expectedHash,
			long expectedLength, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Directory.CreateDirectory(Path.GetDirectoryName(publishedPath));
			if (File.Exists(publishedPath))
			{
				RequireExactFile(publishedPath, expectedHash, expectedLength, cancellationToken);
				File.Delete(stagingPath);
				return;
			}

			try
			{
				File.Move(stagingPath, publishedPath);
			}
			catch (IOException)
			{
				// Another process may have atomically published the same content-addressed blob after our existence check.
				if (!File.Exists(publishedPath))
					throw;

				RequireExactFile(publishedPath, expectedHash, expectedLength, cancellationToken);
				File.Delete(stagingPath);
			}
		}

		private static void RequireExactFile(string path, CollectionContentHash expectedHash, long expectedLength,
			CancellationToken cancellationToken)
		{
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != expectedLength ||
				!StringComparer.Ordinal.Equals(ComputeFileHash(path, cancellationToken), expectedHash.Value))
				throw new InvalidDataException("Existing retained content does not match the content-addressed path selected for publication.");
		}

		private static string ComputeFileHash(string path, CancellationToken cancellationToken)
		{
			using (SHA256 sha256 = SHA256.Create())
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				byte[] buffer = new byte[CopyBufferSize];
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha256.TransformBlock(buffer, 0, read, buffer, 0);
				}
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				return ToHex(sha256.Hash);
			}
		}

		private bool IsVerifiedArtifactCached(CollectionsRetainedArtifact artifact, FileInfo info)
		{
			lock (_verificationCacheLock)
			{
				ArtifactVerificationStamp stamp;
				return _verifiedArtifacts.TryGetValue(artifact.ArtifactId, out stamp) &&
					stamp.ByteLength == info.Length &&
					stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks &&
					StringComparer.Ordinal.Equals(stamp.HashValue, artifact.ContentHash.Value);
			}
		}

		private void RememberVerifiedArtifact(CollectionsRetainedArtifact artifact, string path)
		{
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != artifact.ByteLength)
				return;

			lock (_verificationCacheLock)
			{
				_verifiedArtifacts[artifact.ArtifactId] = new ArtifactVerificationStamp(
					artifact.ContentHash.Value, info.Length, info.LastWriteTimeUtc.Ticks);
			}
		}

		private void RememberPublishedSource(string fullPath, CollectionsRetainedArtifact artifact, long byteLength, long lastWriteUtcTicks)
		{
			lock (_verificationCacheLock)
				_publishedSources[fullPath] = new PublishedSourceStamp(artifact.ContentHash.Value, byteLength, lastWriteUtcTicks);
		}

		private void RememberPublishedMd5(CollectionsRetainedArtifact artifact, string path, string md5)
		{
			if (String.IsNullOrWhiteSpace(md5))
				return;
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != artifact.ByteLength)
				return;
			lock (_verificationCacheLock)
				_publishedMd5[artifact.ArtifactId] = new PublishedMd5Stamp(md5, info.Length, info.LastWriteTimeUtc.Ticks);
		}

		private void ForgetVerifiedArtifact(string artifactId)
		{
			lock (_verificationCacheLock)
			{
				_verifiedArtifacts.Remove(artifactId);
				_publishedMd5.Remove(artifactId);
			}
		}

		private static string CreateArtifactId(CollectionContentHash contentHash)
		{
			return HashAlgorithmName + ":" + contentHash.Value;
		}

		internal static string GetCanonicalRelativePath(CollectionContentHash contentHash)
		{
			return BlobDirectoryName + "/" + contentHash.Value.Substring(0, 2) + "/" + contentHash.Value + BlobExtension;
		}

		internal string ResolveCanonicalPath(string relativePath)
		{
			string root = Path.GetFullPath(_store.RetainedContentDirectory);
			string path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
			string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("A retained artifact path escaped Collections-owned content storage.");
			return path;
		}

		private static string ToHex(byte[] bytes)
		{
			char[] chars = new char[bytes.Length * 2];
			const string hex = "0123456789abcdef";
			for (int index = 0; index < bytes.Length; index++)
			{
				chars[index * 2] = hex[bytes[index] >> 4];
				chars[(index * 2) + 1] = hex[bytes[index] & 0x0f];
			}
			return new string(chars);
		}

		private static void DeleteTemporaryFile(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return;
			try
			{
				File.Delete(path);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}

		internal sealed class PreparedFilePublication
		{
			internal PreparedFilePublication(CollectionsRetainedArtifact artifact, string relativePath, string publishedPath,
				string md5Value, string sourcePath, long sourceLength, long sourceLastWriteUtcTicks, bool sourceStable)
			{
				Artifact = artifact; RelativePath = relativePath; PublishedPath = publishedPath; Md5Value = md5Value;
				SourcePath = sourcePath; SourceLength = sourceLength; SourceLastWriteUtcTicks = sourceLastWriteUtcTicks; SourceStable = sourceStable;
			}
			internal CollectionsRetainedArtifact Artifact { get; }
			internal string RelativePath { get; }
			internal string PublishedPath { get; }
			internal string Md5Value { get; }
			internal string SourcePath { get; }
			internal long SourceLength { get; }
			internal long SourceLastWriteUtcTicks { get; }
			internal bool SourceStable { get; }
		}

		private sealed class CopyResult
		{
			public CopyResult(string hashValue, string md5Value, long byteLength)
			{
				HashValue = hashValue;
				Md5Value = md5Value;
				ByteLength = byteLength;
			}

			public string HashValue { get; }
			public string Md5Value { get; }
			public long ByteLength { get; }
		}

		private sealed class PublishedMd5Stamp
		{
			public PublishedMd5Stamp(string md5, long byteLength, long lastWriteUtcTicks)
			{
				Md5 = md5;
				ByteLength = byteLength;
				LastWriteUtcTicks = lastWriteUtcTicks;
			}

			public string Md5 { get; }
			public long ByteLength { get; }
			public long LastWriteUtcTicks { get; }
		}

		private sealed class PublishedSourceStamp
		{
			public PublishedSourceStamp(string hashValue, long byteLength, long lastWriteUtcTicks)
			{
				HashValue = hashValue;
				ByteLength = byteLength;
				LastWriteUtcTicks = lastWriteUtcTicks;
			}

			public string HashValue { get; }
			public long ByteLength { get; }
			public long LastWriteUtcTicks { get; }
		}

		private sealed class ArtifactVerificationStamp
		{
			public ArtifactVerificationStamp(string hashValue, long byteLength, long lastWriteUtcTicks)
			{
				HashValue = hashValue;
				ByteLength = byteLength;
				LastWriteUtcTicks = lastWriteUtcTicks;
			}

			public string HashValue { get; }
			public long ByteLength { get; }
			public long LastWriteUtcTicks { get; }
		}
	}
}
