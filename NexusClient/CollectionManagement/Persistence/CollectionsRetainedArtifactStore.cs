using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Newtonsoft.Json;
using Nexus.Client.CollectionManagement;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>
	/// Reads verified archive-library references and publishes Collections-owned source/recovery content.
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
		private static readonly AsyncLocal<bool> SavedArchiveHashReuse = new AsyncLocal<bool>();

		/// <summary>Gets the explicit saved-hash preference for the current approval/install operation.</summary>
		internal static bool CanReuseSavedArchiveHashes { get { return SavedArchiveHashReuse.Value; } }

		/// <summary>Captures the user's preference across asynchronous approval and install calls without changing strict verification defaults.</summary>
		internal static IDisposable BeginSavedArchiveHashReuse(bool enabled)
		{
			return new SavedArchiveHashReuseScope(enabled);
		}

		/// <summary>Restores the caller's verification policy after an explicitly opted-in operation.</summary>
		private sealed class SavedArchiveHashReuseScope : IDisposable
		{
			private readonly bool _previous;
			/// <summary>Captures the existing policy before setting the current operation's preference.</summary>
			internal SavedArchiveHashReuseScope(bool enabled)
			{
				_previous = SavedArchiveHashReuse.Value;
				SavedArchiveHashReuse.Value = enabled;
			}
			/// <summary>Restores the parent operation's policy.</summary>
			public void Dispose() { SavedArchiveHashReuse.Value = _previous; }
		}

		private readonly CollectionsStore _store;
		private readonly object _verificationCacheLock = new object();
		private readonly Dictionary<string, ArtifactVerificationStamp> _verifiedArtifacts =
			new Dictionary<string, ArtifactVerificationStamp>(StringComparer.Ordinal);
		private readonly Dictionary<string, PublishedMd5Stamp> _publishedMd5 =
			new Dictionary<string, PublishedMd5Stamp>(StringComparer.Ordinal);
		private readonly Dictionary<string, PublishedSourceStamp> _publishedSources =
			new Dictionary<string, PublishedSourceStamp>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, ArtifactVerificationStamp> _trustedPreparationArtifacts =
			new Dictionary<string, ArtifactVerificationStamp>(StringComparer.Ordinal);

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
			string stagingPath = Path.Combine(stagingDirectory, Nexus.Client.Util.FileUtil.CreateTempToken() + ".tmp");

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
			string stagingPath = Path.Combine(stagingDirectory, Nexus.Client.Util.FileUtil.CreateTempToken() + ".tmp");

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

		/// <summary>Records verified archive-library bytes without copying them into Collections storage.</summary>
		public CollectionsRetainedArtifact ReferenceArchiveFile(string sourcePath, CancellationToken cancellationToken = default(CancellationToken))
		{
			return ReferenceArchiveFile(sourcePath, cancellationToken, false);
		}

		/// <summary>Records SHA-256-tracked library bytes without calculating MD5 for an explicitly trusted existing archive.</summary>
		internal CollectionsRetainedArtifact ReferenceArchiveFileWithoutMd5(string sourcePath, CancellationToken cancellationToken)
		{
			return ReferenceArchiveFile(sourcePath, cancellationToken, false, false);
		}

		/// <summary>Reuses an actually calculated SHA-256 only when an explicitly trusted library archive retains its saved file stamp.</summary>
		internal CollectionsRetainedArtifact TryReferenceCachedArchiveFile(string sourcePath, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string fullPath = Path.GetFullPath(sourcePath);
			var info = new FileInfo(fullPath);
			if (!info.Exists) return null;
			try
			{
				string cachePath = GetArchiveHashCachePath(fullPath);
				var cacheInfo = new FileInfo(cachePath);
				if (!cacheInfo.Exists || cacheInfo.Length > 8192) return null;
				ArchiveHashCacheRecord cached = JsonConvert.DeserializeObject<ArchiveHashCacheRecord>(File.ReadAllText(cachePath));
				if (cached == null || cached.SchemaVersion != 1 ||
					!StringComparer.OrdinalIgnoreCase.Equals(cached.ArchivePath, fullPath) ||
					cached.ByteLength != info.Length || cached.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
					return null;
				CollectionsRetainedArtifact artifact = GetArtifact(cached.ArtifactId);
				if (artifact == null || artifact.ByteLength != info.Length ||
					!StringComparer.Ordinal.Equals(artifact.ContentHash.Value, cached.Sha256) ||
					!IsArchiveReference(artifact.ArtifactId) ||
					!StringComparer.OrdinalIgnoreCase.Equals(GetValidatedPhysicalPath(artifact), fullPath))
					return null;
				lock (_verificationCacheLock)
					_trustedPreparationArtifacts[artifact.ArtifactId] = new ArtifactVerificationStamp(
						artifact.ContentHash.Value, info.Length, info.LastWriteTimeUtc.Ticks, fullPath);
				return IsTrustedPreparationArchiveCurrent(fullPath, artifact) ? artifact : null;
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is ArgumentException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection archive hash cache could not be reused: {0}", ex.Message);
				return null;
			}
		}

		/// <summary>Checks an opted-in saved archive proof without publishing it as a checksum observed in this process.</summary>
		internal bool IsTrustedPreparationArchiveCurrent(string sourcePath, CollectionsRetainedArtifact artifact)
		{
			if (artifact == null || String.IsNullOrWhiteSpace(sourcePath)) return false;
			var info = new FileInfo(Path.GetFullPath(sourcePath));
			if (!info.Exists || info.Length != artifact.ByteLength) return false;
			lock (_verificationCacheLock)
			{
				ArtifactVerificationStamp stamp;
				if (_trustedPreparationArtifacts.TryGetValue(artifact.ArtifactId, out stamp))
					return StringComparer.OrdinalIgnoreCase.Equals(stamp.FullPath, info.FullName) &&
						stamp.ByteLength == info.Length && stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks &&
						StringComparer.Ordinal.Equals(stamp.HashValue, artifact.ContentHash.Value);
			}
			CollectionsRetainedArtifact cached = TryReferenceCachedArchiveFile(info.FullName, CancellationToken.None);
			return cached != null && cached.Equals(artifact);
		}

		/// <summary>Checks the recorded physical location of a preparation-only cached proof.</summary>
		internal bool IsTrustedPreparationArchiveCurrent(CollectionsRetainedArtifact artifact)
		{
			return artifact != null && IsTrustedPreparationArchiveCurrent(GetValidatedPhysicalPath(artifact), artifact);
		}

		/// <summary>Converts legacy archive copies to library references without changing their durable content identities.</summary>
		public void MigrateArchiveCopies(IEnumerable<string> archivePaths, CancellationToken cancellationToken)
		{
			if (archivePaths == null) throw new ArgumentNullException(nameof(archivePaths));
			List<CollectionsRetainedArtifact> recorded = _store.ExecuteRead((connection, transaction) =>
			{
				var result = new List<CollectionsRetainedArtifact>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed FROM retained_artifacts WHERE sealed=1;";
					using (SQLiteDataReader reader = command.ExecuteReader())
						while (reader.Read()) result.Add(ReadArtifact(reader));
				}
				return result;
			});
			var lengths = new HashSet<long>();
			foreach (CollectionsRetainedArtifact artifact in recorded)
				if (File.Exists(ResolveCanonicalPath(GetCanonicalRelativePath(artifact.ContentHash)))) lengths.Add(artifact.ByteLength);
			if (lengths.Count == 0) return;
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string archivePath in archivePaths)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (String.IsNullOrWhiteSpace(archivePath) || !visited.Add(archivePath)) continue;
				try
				{
					var info = new FileInfo(archivePath);
					if (info.Exists && lengths.Contains(info.Length)) ReferenceArchiveFile(archivePath, cancellationToken, true);
				}
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					System.Diagnostics.Trace.TraceWarning("Collection archive-copy migration deferred for {0}: {1}", archivePath, ex.Message);
				}
			}
		}

		/// <summary>Hashes a stable archive and persists its location; migration only adopts already-recorded content.</summary>
		private CollectionsRetainedArtifact ReferenceArchiveFile(string sourcePath, CancellationToken cancellationToken, bool existingOnly, bool calculateMd5 = true)
		{
			if (String.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("An archive path is required.", nameof(sourcePath));
			string fullPath = Path.GetFullPath(sourcePath);
			string retainedRoot = Path.GetFullPath(_store.RetainedContentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (fullPath.StartsWith(retainedRoot, StringComparison.OrdinalIgnoreCase)) return PublishFile(fullPath, cancellationToken);
			using (FileStream source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan))
			{
				long initialWrite = File.GetLastWriteTimeUtc(fullPath).Ticks;
				cancellationToken.ThrowIfCancellationRequested();
				CopyResult hashed = null;
				// Reuse only hashes observed by this process while the same source remains unchanged.
				// The native installer still verifies the complete archive before applying it.
				lock (_verificationCacheLock)
				{
					PublishedSourceStamp stamp;
					PublishedMd5Stamp md5;
					if (_publishedSources.TryGetValue(fullPath, out stamp) && stamp.ByteLength == source.Length &&
						stamp.LastWriteUtcTicks == initialWrite)
					{
						if (!calculateMd5) hashed = new CopyResult(stamp.HashValue, null, stamp.ByteLength);
						else if (_publishedMd5.TryGetValue(CreateArtifactId(CollectionContentHash.FromSha256(stamp.HashValue)), out md5) &&
							StringComparer.OrdinalIgnoreCase.Equals(md5.FullPath, fullPath) && md5.ByteLength == source.Length &&
							md5.LastWriteUtcTicks == initialWrite)
							hashed = new CopyResult(stamp.HashValue, md5.Md5, stamp.ByteLength);
					}
				}
				if (hashed == null) hashed = HashArchive(source, cancellationToken, calculateMd5);
				if (source.Length != hashed.ByteLength || File.GetLastWriteTimeUtc(fullPath).Ticks != initialWrite)
					throw new IOException("The mod archive changed while its content was being verified.");
				CollectionContentHash hash = CollectionContentHash.FromSha256(hashed.HashValue);
				var artifact = new CollectionsRetainedArtifact(CreateArtifactId(hash), hash, hashed.ByteLength);
				if (existingOnly && GetArtifact(artifact.ArtifactId) == null) return null;
				string relativePath = GetCanonicalRelativePath(hash);
				_store.ExecuteWrite((connection, transaction) =>
				{
					CollectionsRetainedArtifact existing = ReadArtifactByContent(connection, transaction, hash, hashed.ByteLength);
					if (existing != null) ValidatePersistedArtifact(connection, transaction, existing, relativePath);
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.Transaction = transaction;
						command.CommandText = @"
INSERT OR IGNORE INTO retained_artifacts (artifact_id, hash_algorithm, hash_value, byte_length, relative_path, sealed)
VALUES (@id, 'sha256', @hash, @length, @relative, 1);
INSERT OR REPLACE INTO retained_archive_locations (artifact_id, archive_path) VALUES (@id, @path);";
						command.Parameters.AddWithValue("@id", artifact.ArtifactId);
						command.Parameters.AddWithValue("@hash", hash.Value);
						command.Parameters.AddWithValue("@length", hashed.ByteLength);
						command.Parameters.AddWithValue("@relative", relativePath);
						command.Parameters.AddWithValue("@path", fullPath);
						command.ExecuteNonQuery();
					}
				});
				RememberPublishedMd5(artifact, fullPath, hashed.Md5Value);
				RememberPublishedSource(fullPath, artifact, hashed.ByteLength, initialWrite);
				SaveArchiveHashCache(fullPath, artifact, initialWrite);
				RememberVerifiedArtifact(artifact, new FileInfo(fullPath));
				DeleteDuplicateArchiveBlob(artifact);
				return artifact;
			}
		}

		/// <summary>Hashes archive bytes without a staging file or a retained payload copy.</summary>
		private static CopyResult HashArchive(Stream source, CancellationToken cancellationToken, bool calculateMd5 = true)
		{
			CollectionPreparationProgress.ReportArchiveHashing(calculateMd5);
			byte[] buffer = new byte[CopyBufferSize];
			long length = 0;
			long total = source.CanSeek ? source.Length : 0;
			using (SHA256 sha256 = SHA256.Create())
			using (MD5 md5 = calculateMd5 ? MD5.Create() : null)
			{
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha256.TransformBlock(buffer, 0, read, buffer, 0);
					if (md5 != null) md5.TransformBlock(buffer, 0, read, buffer, 0);
					length += read;
					CollectionPreparationProgress.ReportBytes(length, total);
				}
				cancellationToken.ThrowIfCancellationRequested();
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				if (md5 != null) md5.TransformFinalBlock(new byte[0], 0, 0);
				return new CopyResult(BitConverter.ToString(sha256.Hash).Replace("-", "").ToLowerInvariant(),
					md5 == null ? null : BitConverter.ToString(md5.Hash).Replace("-", "").ToLowerInvariant(), length);
			}
		}

		/// <summary>Removes a redundant archive blob, preserving independently retained bundles, manifests and capture packages.</summary>
		private void DeleteDuplicateArchiveBlob(CollectionsRetainedArtifact artifact)
		{
			bool sourceContent = _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"
SELECT 1 FROM revision_sources WHERE raw_bundle_artifact_id=@id OR raw_manifest_artifact_id=@id
UNION ALL SELECT 1 FROM local_working_copies WHERE base_bundle_artifact_id=@id OR base_manifest_artifact_id=@id OR draft_manifest_artifact_id=@id
UNION ALL SELECT 1 FROM local_capture_packages WHERE package_artifact_id=@id LIMIT 1;";
					command.Parameters.AddWithValue("@id", artifact.ArtifactId);
					return command.ExecuteScalar() != null;
				}
			});
			if (sourceContent) return;
			string duplicate = ResolveCanonicalPath(GetCanonicalRelativePath(artifact.ContentHash));
			try { if (File.Exists(duplicate)) File.Delete(duplicate); }
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection archive copy cleanup deferred for {0}: {1}", artifact.ArtifactId, ex.Message);
			}
		}

		/// <summary>Checks an already prepared artifact's physical binding without hashing it at child submission.</summary>
		internal bool IsPreparedArtifactCurrent(CollectionsRetainedArtifact artifact)
		{
			if (artifact == null) return false;
			string path = GetValidatedPhysicalPath(artifact);
			if (CollectionArchiveContentMatcher.MatchesPreparedFile(path, artifact.ByteLength, artifact.ContentHash)) return true;
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != artifact.ByteLength || !IsArchiveReference(artifact.ArtifactId) ||
				!IsTrustedPreparationArchiveCurrent(path, artifact)) return false;
			// A resumed review can reuse its persisted path/size/write-stamp proof without reopening archive bytes.
			var after = new FileInfo(path);
			if (!after.Exists || after.Length != info.Length || after.LastWriteTimeUtc.Ticks != info.LastWriteTimeUtc.Ticks) return false;
			CollectionArchiveContentMatcher.RememberTrustedHash(info.FullName, info, artifact.ContentHash.Value);
			return CollectionArchiveContentMatcher.MatchesPreparedFile(path, artifact.ByteLength, artifact.ContentHash);
		}

		/// <summary>Reports whether an artifact depends on an ordinary archive-library file rather than retained bytes.</summary>
		internal bool IsArchiveReference(string artifactId)
		{
			return _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT 1 FROM retained_archive_locations WHERE artifact_id=@id;";
					command.Parameters.AddWithValue("@id", artifactId);
					return command.ExecuteScalar() != null;
				}
			});
		}

		/// <summary>Moves retained source content into a named Collection/revision folder without copying its bytes.</summary>
		internal void PlaceCollectionSource(string artifactId, string relativePath, CancellationToken cancellationToken = default(CancellationToken))
		{
			CollectionsRetainedArtifact artifact = GetArtifact(artifactId);
			if (artifact == null) throw new FileNotFoundException("The Collection source artifact is missing.", artifactId);
			ResolveCollectionSourcePath(relativePath);
			string selectedPath = null;
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "INSERT OR IGNORE INTO retained_collection_locations (artifact_id, relative_path) VALUES (@id, @path);";
					command.Parameters.AddWithValue("@id", artifactId);
					command.Parameters.AddWithValue("@path", relativePath);
					command.ExecuteNonQuery();
					command.CommandText = "SELECT relative_path FROM retained_collection_locations WHERE artifact_id=@id;";
					selectedPath = (string)command.ExecuteScalar();
				}
			});
			string destination = ResolveCollectionSourcePath(selectedPath);
			string original = ResolveCanonicalPath(GetCanonicalRelativePath(artifact.ContentHash));
			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(destination))
			{
				RequireExactFile(destination, artifact.ContentHash, artifact.ByteLength, cancellationToken);
				if (File.Exists(original)) File.Delete(original);
				return;
			}
			if (!File.Exists(original)) throw new FileNotFoundException("The retained Collection source bytes are missing.", original);
			RequireExactFile(original, artifact.ContentHash, artifact.ByteLength, cancellationToken);
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(destination));
				// Readers fall back to the verified old blob until the atomic move finishes.
				File.Move(original, destination);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection source move deferred for {0}: {1}", artifactId, ex.Message);
			}
		}

		/// <summary>Resolves only Collections-owned logical content paths, for reading and cleanup.</summary>
		internal string ResolveOwnedContentPath(string artifactId, string canonicalRelativePath)
		{
			string relative = _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT relative_path FROM retained_collection_locations WHERE artifact_id=@id;";
					command.Parameters.AddWithValue("@id", artifactId);
					return command.ExecuteScalar() as string;
				}
			});
			if (relative != null)
			{
				string named = ResolveCollectionSourcePath(relative);
				if (File.Exists(named)) return named;
			}
			return ResolveCanonicalPath(canonicalRelativePath);
		}

		/// <summary>Validates a named source path stays inside the Collections library.</summary>
		private string ResolveCollectionSourcePath(string relativePath)
		{
			if (String.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith("Library/", StringComparison.Ordinal) ||
				Path.IsPathRooted(relativePath) || relativePath.IndexOf(':') >= 0)
				throw new InvalidDataException("The named Collection source path is invalid.");
			string root = Path.GetFullPath(Path.Combine(_store.StoreDirectory, "Library")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			string fullPath = Path.GetFullPath(Path.Combine(_store.StoreDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
			if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("The named Collection source path escaped its library.");
			return fullPath;
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
				if (VerifyArtifactBytes(artifact, GetValidatedPhysicalPath(artifact), cancellationToken))
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

			if (CanReuseSavedArchiveHashes && IsArchiveReference(artifact.ArtifactId) && IsTrustedPreparationArchiveCurrent(path, artifact))
			{
				CollectionPerformanceMetrics.RecordRetainedArtifactVerificationCacheHit();
				// Keep opt-in saved proofs separate from hashes actually observed in this process.
				CollectionArchiveContentMatcher.RememberTrustedHash(info.FullName, info, artifact.ContentHash.Value);
				CollectionPreparationProgress.ReportArchiveVerification(path, true);
				return true;
			}

			cancellationToken.ThrowIfCancellationRequested();
			string actualHash;
			if (CollectionArchiveContentMatcher.TryGetObservedHash(info.FullName, info, out actualHash))
				CollectionPerformanceMetrics.RecordRetainedArtifactVerificationCacheHit();
			else
			{
				long performanceStarted = CollectionPerformanceMetrics.StartTiming();
				actualHash = ComputeFileHash(path, cancellationToken, IsArchiveReference(artifact.ArtifactId));
				CollectionPerformanceMetrics.RecordRetainedArtifactVerificationHash(performanceStarted, artifact.ByteLength);
			}
			if (!StringComparer.Ordinal.Equals(actualHash, artifact.ContentHash.Value))
			{
				CollectionArchiveContentMatcher.ForgetTrustedHash(path);
				if (IsArchiveReference(artifact.ArtifactId)) InvalidateArchiveHashCache(path, artifact);
				ForgetVerifiedArtifact(artifact.ArtifactId);
				return false;
			}

			RememberVerifiedArtifact(artifact, info);
			if (IsArchiveReference(artifact.ArtifactId)) SaveArchiveHashCache(path, artifact, info.LastWriteTimeUtc.Ticks);
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
					StringComparer.OrdinalIgnoreCase.Equals(stamp.FullPath, info.FullName) && stamp.ByteLength == info.Length && stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
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
					!StringComparer.OrdinalIgnoreCase.Equals(published.FullPath, info.FullName) || published.ByteLength != info.Length || published.LastWriteUtcTicks != info.LastWriteTimeUtc.Ticks)
					return false;
				_verifiedArtifacts[artifact.ArtifactId] = new ArtifactVerificationStamp(
					artifact.ContentHash.Value, info.Length, info.LastWriteTimeUtc.Ticks, info.FullName);
				CollectionArchiveContentMatcher.RememberObservedHash(info.FullName, info, artifact.ContentHash.Value);
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

			string ownedPath = ResolveOwnedContentPath(artifact.ArtifactId, expectedRelativePath);
			if (!StringComparer.OrdinalIgnoreCase.Equals(ownedPath, ResolveCanonicalPath(expectedRelativePath))) return ownedPath;
			string archivePath = _store.ExecuteRead((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = "SELECT archive_path FROM retained_archive_locations WHERE artifact_id=@id;";
					command.Parameters.AddWithValue("@id", artifact.ArtifactId);
					return command.ExecuteScalar() as string;
				}
			});
			if (!String.IsNullOrWhiteSpace(archivePath) && File.Exists(archivePath)) return Path.GetFullPath(archivePath);
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
				long stagedLastWriteUtcTicks = File.GetLastWriteTimeUtc(stagingPath).Ticks;
				File.Move(stagingPath, publishedPath);
				var publishedInfo = new FileInfo(publishedPath);
				// The move preserves the bytes already hashed during publication. Reuse that proof while unchanged.
				if (publishedInfo.Exists && publishedInfo.Length == expectedLength &&
					publishedInfo.LastWriteTimeUtc.Ticks == stagedLastWriteUtcTicks)
					CollectionArchiveContentMatcher.RememberObservedHash(publishedInfo.FullName, publishedInfo, expectedHash.Value);
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
			string actualHash = null;
			if (info.Exists && info.Length == expectedLength &&
				!CollectionArchiveContentMatcher.TryGetObservedHash(info.FullName, info, out actualHash))
				actualHash = ComputeFileHash(path, cancellationToken);
			if (!info.Exists || info.Length != expectedLength || !StringComparer.Ordinal.Equals(actualHash, expectedHash.Value))
				throw new InvalidDataException("Existing retained content does not match the content-addressed path selected for publication.");
			var after = new FileInfo(path);
			if (after.Exists && after.Length == info.Length && after.LastWriteTimeUtc.Ticks == info.LastWriteTimeUtc.Ticks)
				CollectionArchiveContentMatcher.RememberObservedHash(after.FullName, after, actualHash);
		}

		private static string ComputeFileHash(string path, CancellationToken cancellationToken, bool isArchive = false)
		{
			if (isArchive)
			{
				CollectionPreparationProgress.ReportArchiveVerification(path, false);
				CollectionPreparationProgress.ReportArchiveHashing(false);
			}
			else
				CollectionPreparationProgress.Report(Nexus.Client.Util.Localization.LanguageManager.Get(
					"Collections.Workflow.LoadingSavedData", "Loading saved Collection data..."), true);
			using (SHA256 sha256 = SHA256.Create())
			using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				byte[] buffer = new byte[CopyBufferSize];
				long completed = 0;
				int read;
				while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					sha256.TransformBlock(buffer, 0, read, buffer, 0);
					completed += read;
					CollectionPreparationProgress.ReportBytes(completed, stream.Length);
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
					StringComparer.OrdinalIgnoreCase.Equals(stamp.FullPath, info.FullName) && stamp.ByteLength == info.Length &&
					stamp.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks &&
					StringComparer.Ordinal.Equals(stamp.HashValue, artifact.ContentHash.Value);
			}
		}

		/// <summary>Publishes the file stamp observed during verification to both artifact lookup and managed-mod matching.</summary>
		private void RememberVerifiedArtifact(CollectionsRetainedArtifact artifact, FileInfo info)
		{
			if (!info.Exists || info.Length != artifact.ByteLength)
				return;

			lock (_verificationCacheLock)
			{
				_verifiedArtifacts[artifact.ArtifactId] = new ArtifactVerificationStamp(
					artifact.ContentHash.Value, info.Length, info.LastWriteTimeUtc.Ticks, info.FullName);
			}
			CollectionArchiveContentMatcher.RememberObservedHash(info.FullName, info, artifact.ContentHash.Value);
		}

		private void RememberPublishedSource(string fullPath, CollectionsRetainedArtifact artifact, long byteLength, long lastWriteUtcTicks)
		{
			lock (_verificationCacheLock)
				_publishedSources[fullPath] = new PublishedSourceStamp(artifact.ContentHash.Value, byteLength, lastWriteUtcTicks);
			var info = new FileInfo(fullPath);
			if (info.Exists && info.Length == byteLength && info.LastWriteTimeUtc.Ticks == lastWriteUtcTicks)
				CollectionArchiveContentMatcher.RememberObservedHash(info.FullName, info, artifact.ContentHash.Value);
		}

		private void RememberPublishedMd5(CollectionsRetainedArtifact artifact, string path, string md5)
		{
			if (String.IsNullOrWhiteSpace(md5))
				return;
			var info = new FileInfo(path);
			if (!info.Exists || info.Length != artifact.ByteLength)
				return;
			lock (_verificationCacheLock)
				_publishedMd5[artifact.ArtifactId] = new PublishedMd5Stamp(md5, info.Length, info.LastWriteTimeUtc.Ticks, info.FullName);
		}

		private void ForgetVerifiedArtifact(string artifactId)
		{
			lock (_verificationCacheLock)
			{
				_verifiedArtifacts.Remove(artifactId);
				_publishedMd5.Remove(artifactId);
				_trustedPreparationArtifacts.Remove(artifactId);
			}
		}

		/// <summary>Stores an optional file-stamp cache separately from authoritative Collection records.</summary>
		private void SaveArchiveHashCache(string fullPath, CollectionsRetainedArtifact artifact, long lastWriteUtcTicks)
		{
			string temporaryPath = null;
			try
			{
				var info = new FileInfo(fullPath);
				if (!info.Exists || info.Length != artifact.ByteLength || info.LastWriteTimeUtc.Ticks != lastWriteUtcTicks) return;
				string cachePath = GetArchiveHashCachePath(fullPath);
				Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
				temporaryPath = cachePath + "." + Nexus.Client.Util.FileUtil.CreateTempToken() + ".tmp";
				var record = new ArchiveHashCacheRecord
				{
					SchemaVersion = 1, ArchivePath = fullPath, ArtifactId = artifact.ArtifactId,
					Sha256 = artifact.ContentHash.Value, ByteLength = artifact.ByteLength, LastWriteUtcTicks = lastWriteUtcTicks
				};
				File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(record));
				if (File.Exists(cachePath)) File.Replace(temporaryPath, cachePath, null);
				else File.Move(temporaryPath, cachePath);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection archive hash cache could not be saved: {0}", ex.Message);
			}
			finally
			{
				try { if (temporaryPath != null && File.Exists(temporaryPath)) File.Delete(temporaryPath); }
				catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
				{
					System.Diagnostics.Trace.TraceWarning("Collection archive hash cache temporary file could not be removed: {0}", ex.Message);
				}
			}
		}

		/// <summary>Discards a saved preparation proof when ordinary verification finds different archive bytes.</summary>
		private void InvalidateArchiveHashCache(string fullPath, CollectionsRetainedArtifact artifact)
		{
			try
			{
				string cachePath = GetArchiveHashCachePath(fullPath);
				var info = new FileInfo(cachePath);
				if (!info.Exists || info.Length > 8192) return;
				ArchiveHashCacheRecord cached = JsonConvert.DeserializeObject<ArchiveHashCacheRecord>(File.ReadAllText(cachePath));
				if (cached != null && StringComparer.Ordinal.Equals(cached.ArtifactId, artifact.ArtifactId)) File.Delete(cachePath);
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
			{
				System.Diagnostics.Trace.TraceWarning("Collection archive hash cache could not be invalidated: {0}", ex.Message);
			}
		}

		/// <summary>Names a cache entry by the normalized archive path without reading archive bytes.</summary>
		private string GetArchiveHashCachePath(string fullPath)
		{
			using (SHA256 sha256 = SHA256.Create())
				return Path.Combine(_store.StoreDirectory, "ArchiveHashCache",
					ToHex(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()))) + ".json");
		}

		/// <summary>Versioned optimization metadata for a real hash observed during archive publication or verification.</summary>
		private sealed class ArchiveHashCacheRecord
		{
			public int SchemaVersion { get; set; }
			public string ArchivePath { get; set; }
			public string ArtifactId { get; set; }
			public string Sha256 { get; set; }
			public long ByteLength { get; set; }
			public long LastWriteUtcTicks { get; set; }
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
			public PublishedMd5Stamp(string md5, long byteLength, long lastWriteUtcTicks, string fullPath)
			{
				Md5 = md5;
				ByteLength = byteLength;
				LastWriteUtcTicks = lastWriteUtcTicks;
				FullPath = fullPath;
			}

			public string Md5 { get; }
			public long ByteLength { get; }
			public long LastWriteUtcTicks { get; }
			public string FullPath { get; }
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
			public ArtifactVerificationStamp(string hashValue, long byteLength, long lastWriteUtcTicks, string fullPath)
			{
				HashValue = hashValue;
				ByteLength = byteLength;
				LastWriteUtcTicks = lastWriteUtcTicks;
				FullPath = fullPath;
			}

			public string HashValue { get; }
			public long ByteLength { get; }
			public long LastWriteUtcTicks { get; }
			public string FullPath { get; }
		}
	}
}
