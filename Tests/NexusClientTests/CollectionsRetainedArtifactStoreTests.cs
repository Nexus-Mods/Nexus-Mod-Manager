using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionsRetainedArtifactStoreTests
	{
		[Test]
		public void Publish_StreamsHashesAndSealsAContentAddressedBlob()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				byte[] bytes = Encoding.UTF8.GetBytes("C4.10 immutable retained content");
				string expectedHash = Sha256(bytes);

				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(bytes, false))
					artifact = retainedStore.Publish(source);

				Assert.AreEqual("sha256:" + expectedHash, artifact.ArtifactId);
				Assert.AreEqual(CollectionContentHashAlgorithm.Sha256, artifact.ContentHash.Algorithm);
				Assert.AreEqual(expectedHash, artifact.ContentHash.Value);
				Assert.AreEqual(bytes.Length, artifact.ByteLength);
				Assert.AreEqual(artifact, retainedStore.GetArtifact(artifact.ArtifactId));
				Assert.IsTrue(retainedStore.VerifyArtifact(artifact.ArtifactId));

				using (Stream stored = retainedStore.OpenRead(artifact.ArtifactId))
					CollectionAssert.AreEqual(bytes, ReadAllBytes(stored));

				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
				{
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
					Assert.AreEqual(1, ScalarInt(connection, "SELECT sealed FROM retained_artifacts;"));
					Assert.AreEqual("sha256/" + expectedHash.Substring(0, 2) + "/" + expectedHash + ".blob",
						ScalarString(connection, "SELECT relative_path FROM retained_artifacts;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_SameBytesReuseOneBlobAndOneMetadataRow()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				byte[] bytes = Encoding.UTF8.GetBytes("deduplicate me");

				CollectionsRetainedArtifact first;
				CollectionsRetainedArtifact second;
				using (var source = new MemoryStream(bytes, false))
					first = retainedStore.Publish(source);
				using (var source = new MemoryStream(bytes, false))
					second = retainedStore.Publish(source);

				Assert.AreEqual(first, second);
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
				Assert.AreEqual(1, Directory.GetFiles(featureStore.RetainedContentDirectory, "*.blob", SearchOption.AllDirectories).Length);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_AdoptsAnExistingVerifiedOrphanBlob()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				byte[] bytes = Encoding.UTF8.GetBytes("orphan from an interrupted metadata publication");
				string hash = Sha256(bytes);
				string blobPath = BlobPath(featureStore, hash);
				Directory.CreateDirectory(Path.GetDirectoryName(blobPath));
				File.WriteAllBytes(blobPath, bytes);

				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(bytes, false))
					artifact = retainedStore.Publish(source);

				Assert.AreEqual("sha256:" + hash, artifact.ArtifactId);
				Assert.IsTrue(retainedStore.VerifyArtifact(artifact.ArtifactId));
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_RejectsDifferentBytesAtTheSelectedContentAddress()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				byte[] expected = Encoding.UTF8.GetBytes("expected bytes");
				string hash = Sha256(expected);
				string blobPath = BlobPath(featureStore, hash);
				Directory.CreateDirectory(Path.GetDirectoryName(blobPath));
				File.WriteAllBytes(blobPath, Encoding.UTF8.GetBytes("different bytes"));

				using (var source = new MemoryStream(expected, false))
					Assert.Throws<InvalidDataException>(() => retainedStore.Publish(source));

				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_SourceFailureLeavesNoPublishedBlobOrMetadata()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);

				using (var source = new ThrowingReadStream(Encoding.UTF8.GetBytes("partial then fail")))
					Assert.Throws<IOException>(() => retainedStore.Publish(source));

				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
				Assert.AreEqual(0, Directory.GetFiles(featureStore.RetainedContentDirectory, "*.blob", SearchOption.AllDirectories).Length);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_DoesNotHoldCollectionsWriterGateWhileCopyingAndHashing()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				var catalog = new CollectionsCatalogStore(featureStore);
				using (var source = new BlockingReadStream(Encoding.UTF8.GetBytes("slow blob")))
				{
					Task<CollectionsRetainedArtifact> publishTask = Task.Run(() => retainedStore.Publish(source));
					Assert.IsTrue(source.WaitUntilReadStarted(3000), "Blob publication never reached source streaming.");

					var definition = new CollectionDefinition(CollectionIdentity.FromNexus("c4-10-concurrent-catalog"),
						"Concurrent catalog write", null, null);
					Task catalogTask = Task.Run(() => catalog.SaveDefinition(definition));
					bool catalogCompleted = catalogTask.Wait(3000);
					source.Release();

					Assert.IsTrue(catalogCompleted, "Large retained-content I/O must not hold the Collections SQLite writer gate.");
					publishTask.Wait(5000);
					Assert.IsTrue(publishTask.IsCompleted);
					Assert.IsNotNull(catalog.GetDefinition(definition.Identity));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Publish_MetadataFailureAfterAtomicBlobPublicationLeavesOnlyAnUnreferencedBlob()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				byte[] bytes = Encoding.UTF8.GetBytes("blob survives metadata publication failure");
				string hash = Sha256(bytes);
				using (var source = new BlockingReadStream(bytes))
				{
					Task<CollectionsRetainedArtifact> publishTask = Task.Run(() => retainedStore.Publish(source));
					Assert.IsTrue(source.WaitUntilReadStarted(3000));

					using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
						Execute(connection, "DROP INDEX ix_retained_artifact_references_artifact;");

					source.Release();
					AggregateException aggregate = Assert.Throws<AggregateException>(() => publishTask.Wait(5000));
					Assert.IsInstanceOf<CollectionsStoreSchemaException>(aggregate.GetBaseException());
				}

				Assert.IsTrue(File.Exists(BlobPath(featureStore, hash)),
					"Blob bytes must be atomically published before metadata is attempted.");
				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyArtifact_DetectsPostPublicationByteTampering()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				byte[] bytes = Encoding.ASCII.GetBytes("abcdef");
				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(bytes, false))
					artifact = retainedStore.Publish(source);

				File.WriteAllBytes(BlobPath(featureStore, artifact.ContentHash.Value), Encoding.ASCII.GetBytes("ABCDEF"));
				Assert.IsFalse(retainedStore.VerifyArtifact(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void OpenRead_RejectsPersistedPathThatDoesNotMatchContentAddressedIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var retainedStore = new CollectionsRetainedArtifactStore(featureStore);
				CollectionsRetainedArtifact artifact;
				using (var source = new MemoryStream(Encoding.UTF8.GetBytes("safe path"), false))
					artifact = retainedStore.Publish(source);

				using (SQLiteConnection connection = OpenDatabase(featureStore.DatabasePath))
				{
					using (SQLiteCommand command = connection.CreateCommand())
					{
						command.CommandText = "UPDATE retained_artifacts SET relative_path='../outside.bin' WHERE artifact_id=@artifact_id;";
						command.Parameters.AddWithValue("@artifact_id", artifact.ArtifactId);
						command.ExecuteNonQuery();
					}
				}

				Assert.Throws<InvalidDataException>(() => retainedStore.OpenRead(artifact.ArtifactId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmCollectionsRetained-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static string BlobPath(CollectionsStore store, string hash)
		{
			return Path.Combine(store.RetainedContentDirectory, "sha256", hash.Substring(0, 2), hash + ".blob");
		}

		private static string Sha256(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] digest = sha.ComputeHash(bytes);
				var builder = new StringBuilder(digest.Length * 2);
				foreach (byte value in digest)
					builder.Append(value.ToString("x2"));
				return builder.ToString();
			}
		}

		private static byte[] ReadAllBytes(Stream stream)
		{
			using (var buffer = new MemoryStream())
			{
				stream.CopyTo(buffer);
				return buffer.ToArray();
			}
		}

		private static SQLiteConnection OpenDatabase(string databasePath)
		{
			var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder
			{
				DataSource = databasePath,
				ForeignKeys = true,
				Pooling = false,
				FailIfMissing = true
			}.ConnectionString);
			connection.Open();
			return connection;
		}

		private static void Execute(SQLiteConnection connection, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = sql;
				command.ExecuteNonQuery();
			}
		}

		private static int ScalarInt(SQLiteConnection connection, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = sql;
				return Convert.ToInt32(command.ExecuteScalar());
			}
		}

		private static string ScalarString(SQLiteConnection connection, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = sql;
				return Convert.ToString(command.ExecuteScalar());
			}
		}

		private sealed class ThrowingReadStream : Stream
		{
			private readonly MemoryStream _inner;
			private bool _hasRead;

			public ThrowingReadStream(byte[] bytes)
			{
				_inner = new MemoryStream(bytes, false);
			}

			public override bool CanRead { get { return true; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return false; } }
			public override long Length { get { throw new NotSupportedException(); } }
			public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

			public override int Read(byte[] buffer, int offset, int count)
			{
				if (_hasRead)
					throw new IOException("Injected source read failure.");
				_hasRead = true;
				return _inner.Read(buffer, offset, Math.Min(count, 4));
			}

			public override void Flush() { }
			public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
			public override void SetLength(long value) { throw new NotSupportedException(); }
			public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

			protected override void Dispose(bool disposing)
			{
				if (disposing)
					_inner.Dispose();
				base.Dispose(disposing);
			}
		}

		private sealed class BlockingReadStream : Stream
		{
			private readonly MemoryStream _inner;
			private readonly ManualResetEventSlim _readStarted = new ManualResetEventSlim(false);
			private readonly ManualResetEventSlim _release = new ManualResetEventSlim(false);
			private bool _blocked;

			public BlockingReadStream(byte[] bytes)
			{
				_inner = new MemoryStream(bytes, false);
			}

			public bool WaitUntilReadStarted(int milliseconds)
			{
				return _readStarted.Wait(milliseconds);
			}

			public void Release()
			{
				_release.Set();
			}

			public override bool CanRead { get { return true; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return false; } }
			public override long Length { get { throw new NotSupportedException(); } }
			public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }

			public override int Read(byte[] buffer, int offset, int count)
			{
				if (!_blocked)
				{
					_blocked = true;
					_readStarted.Set();
					_release.Wait();
				}
				return _inner.Read(buffer, offset, count);
			}

			public override void Flush() { }
			public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
			public override void SetLength(long value) { throw new NotSupportedException(); }
			public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

			protected override void Dispose(bool disposing)
			{
				if (disposing)
				{
					_release.Set();
					_inner.Dispose();
					_readStarted.Dispose();
					_release.Dispose();
				}
				base.Dispose(disposing);
			}
		}
	}
}
