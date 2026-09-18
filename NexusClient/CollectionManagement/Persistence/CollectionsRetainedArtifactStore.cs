using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

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
		private const int CopyBufferSize = 81920;

		private readonly CollectionsStore _store;

		/// <summary>
		/// Creates a retained-artifact store over an existing Collections feature store.
		/// </summary>
		public CollectionsRetainedArtifactStore(CollectionsStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
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

				return PublishMetadata(new CollectionsRetainedArtifact(artifactId, contentHash, copied.ByteLength), relativePath);
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

			using (FileStream source = new FileStream(Path.GetFullPath(sourcePath), FileMode.Open, FileAccess.Read, FileShare.Read,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				return Publish(source, cancellationToken);
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

			return stream;
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
			if (!File.Exists(path))
				return false;

			var info = new FileInfo(path);
			if (info.Length != artifact.ByteLength)
				return false;

			string actualHash = ComputeFileHash(path, cancellationToken);
			return StringComparer.Ordinal.Equals(actualHash, artifact.ContentHash.Value);
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
			using (FileStream target = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
				CopyBufferSize, FileOptions.SequentialScan))
			{
				int read;
				while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
				{
					cancellationToken.ThrowIfCancellationRequested();
					target.Write(buffer, 0, read);
					sha256.TransformBlock(buffer, 0, read, buffer, 0);
					byteLength += read;
				}

				cancellationToken.ThrowIfCancellationRequested();
				sha256.TransformFinalBlock(new byte[0], 0, 0);
				target.Flush(true);
				return new CopyResult(ToHex(sha256.Hash), byteLength);
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

		private sealed class CopyResult
		{
			public CopyResult(string hashValue, long byteLength)
			{
				HashValue = hashValue;
				ByteLength = byteLength;
			}

			public string HashValue { get; }
			public long ByteLength { get; }
		}
	}
}
