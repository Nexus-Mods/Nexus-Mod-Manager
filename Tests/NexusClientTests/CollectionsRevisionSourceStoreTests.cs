using System;
using System.Data.SQLite;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// C6.15.1 durable exact Collection revision recipe-source retention coverage.
	/// </summary>
	public class CollectionsRevisionSourceStoreTests
	{
		[Test]
		public void RetainManifest_RawManifestRoundTripsAcrossStoreReopen()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "raw-roundtrip");
				byte[] bytes = CreateManifestBytes("Raw roundtrip");
				string importPath = Path.Combine(root, NexusCollectionBundleImporter.ManifestFileName);
				File.WriteAllBytes(importPath, bytes);
				NexusCollectionBundleImportResult import = new NexusCollectionBundleImporter().ImportFile(importPath, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);

				CollectionRevisionSourceRecord saved = sourceStore.RetainManifest(import.Manifest,
					CollectionRevisionSourceInputKind.RawManifest, import.BundleContentHash, import.BundleByteLength,
					import.ManifestEntryName, import.Normalization.GetRawManifestBytes());

				Assert.AreEqual(fixture.Revision.Identity, saved.Revision);
				Assert.AreEqual(CollectionRevisionSourceInputKind.RawManifest, saved.InputKind);
				Assert.AreEqual(import.Manifest.Source, saved.ManifestSource);
				Assert.IsNull(saved.RawBundleArtifactId, "C6.15.1 retains collection.json itself; outer bundle retention is later work.");
				Assert.IsNotNull(saved.RawManifestArtifactId);

				var references = new CollectionsRetainedArtifactReferenceStore(fixture.Store);
				CollectionsRetainedArtifactReferenceRecord reference = references.GetReferenceForOwnerRole(
					CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(fixture.Revision.Identity),
					CollectionsRevisionSourceStore.ManifestRole);
				Assert.IsNotNull(reference);
				Assert.AreEqual(saved.RawManifestArtifactId, reference.ArtifactId);

				File.Delete(importPath);
				var reopenedStore = new CollectionsStore(root);
				reopenedStore.OpenExisting();
				var reopenedSources = new CollectionsRevisionSourceStore(reopenedStore);
				CollectionAssert.AreEqual(bytes, reopenedSources.LoadManifest(fixture.Revision.Identity,
					NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion));
				Assert.AreEqual(saved, reopenedSources.GetSource(fixture.Revision.Identity));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_ArchiveSourcePersistsOuterProvenanceWithoutRetainingOuterBundle()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "archive-provenance");
				byte[] manifestBytes = CreateManifestBytes("Archive source");
				byte[] outerBundleBytes = Encoding.UTF8.GetBytes("bounded archive bytes are retained by the later bundle-acquisition slice");
				NexusCollectionManifestNormalizationResult normalization = Normalize(manifestBytes, fixture.Revision);
				CollectionContentHash outerHash = ComputeHash(outerBundleBytes);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);

				CollectionRevisionSourceRecord saved = sourceStore.RetainManifest(normalization.Manifest,
					CollectionRevisionSourceInputKind.Archive, outerHash, outerBundleBytes.LongLength,
					NexusCollectionBundleImporter.ManifestFileName, normalization.GetRawManifestBytes());

				Assert.AreEqual(CollectionRevisionSourceInputKind.Archive, saved.InputKind);
				Assert.AreEqual(outerHash, saved.BundleContentHash);
				Assert.AreEqual(outerBundleBytes.LongLength, saved.BundleByteLength);
				Assert.AreNotEqual(saved.BundleContentHash, saved.ManifestSource.ContentHash);
				Assert.IsNull(saved.RawBundleArtifactId);
				Assert.IsNotNull(saved.RawManifestArtifactId);
				CollectionAssert.AreEqual(manifestBytes, sourceStore.LoadManifest(fixture.Revision.Identity, normalization.Manifest.Source));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_BytesMustMatchNormalizedSourceBeforePublication()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "prepublish-integrity");
				byte[] bytes = CreateManifestBytes("Expected bytes");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, fixture.Revision);
				byte[] changed = (byte[])bytes.Clone();
				changed[changed.Length - 2] ^= 1;
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);

				Assert.Throws<InvalidDataException>(() => sourceStore.RetainManifest(normalization.Manifest,
					CollectionRevisionSourceInputKind.RawManifest, normalization.Manifest.Source.ContentHash, bytes.LongLength,
					NexusCollectionBundleImporter.ManifestFileName, changed));
				using (SQLiteConnection connection = OpenDatabase(fixture.Store.DatabasePath))
				{
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM revision_sources;"));
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_ExactDuplicateIsIdempotent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "idempotent");
				byte[] bytes = CreateManifestBytes("Idempotent");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);

				CollectionRevisionSourceRecord first = RetainRaw(sourceStore, normalization, bytes);
				CollectionRevisionSourceRecord second = RetainRaw(sourceStore, normalization, bytes);

				Assert.AreEqual(first, second);
				using (SQLiteConnection connection = OpenDatabase(fixture.Store.DatabasePath))
				{
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM revision_sources;"));
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_SameImmutableRevisionRejectsChangedSourceBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "immutable-source");
				byte[] firstBytes = CreateManifestBytes("First source");
				byte[] changedBytes = CreateManifestBytes("Changed source");
				NexusCollectionManifestNormalizationResult firstNormalization = Normalize(firstBytes, fixture.Revision);
				NexusCollectionManifestNormalizationResult changedNormalization = Normalize(changedBytes, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				RetainRaw(sourceStore, firstNormalization, firstBytes);

				Assert.Throws<InvalidOperationException>(() => RetainRaw(sourceStore, changedNormalization, changedBytes));
				CollectionAssert.AreEqual(firstBytes, sourceStore.LoadManifest(fixture.Revision.Identity, firstNormalization.Manifest.Source));
				using (SQLiteConnection connection = OpenDatabase(fixture.Store.DatabasePath))
				{
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM revision_sources;"));
					Assert.AreEqual(1, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_ConflictingExclusiveRevisionRoleFailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "exclusive-role-conflict");
				byte[] manifestBytes = CreateManifestBytes("Expected source");
				NexusCollectionManifestNormalizationResult normalization = Normalize(manifestBytes, fixture.Revision);
				var retainedStore = new CollectionsRetainedArtifactStore(fixture.Store);
				CollectionsRetainedArtifact conflicting;
				using (var source = new MemoryStream(Encoding.UTF8.GetBytes("different immutable manifest bytes"), false))
					conflicting = retainedStore.Publish(source);
				var references = new CollectionsRetainedArtifactReferenceStore(fixture.Store);
				references.AcquireExclusiveRoleReference(conflicting.ArtifactId, CollectionsRetainedArtifactOwnerKind.Revision,
					CollectionsRevisionSourceStore.GetRevisionOwnerId(fixture.Revision.Identity), CollectionsRevisionSourceStore.ManifestRole);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);

				Assert.Throws<InvalidOperationException>(() => RetainRaw(sourceStore, normalization, manifestBytes));
				Assert.IsNull(sourceStore.GetSource(fixture.Revision.Identity));
				Assert.AreEqual(conflicting.ArtifactId, references.GetReferenceForOwnerRole(
					CollectionsRetainedArtifactOwnerKind.Revision, CollectionsRevisionSourceStore.GetRevisionOwnerId(fixture.Revision.Identity),
					CollectionsRevisionSourceStore.ManifestRole).ArtifactId);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_RequiresPersistedRevisionBeforePublishingRetainedContent()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				CollectionIdentity collection = CollectionIdentity.FromNexus("missing-revision");
				var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1), null, null, 0);
				byte[] bytes = CreateManifestBytes("Missing revision");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, revision);
				var sourceStore = new CollectionsRevisionSourceStore(store);

				Assert.Throws<InvalidOperationException>(() => RetainRaw(sourceStore, normalization, bytes));
				using (SQLiteConnection connection = OpenDatabase(store.DatabasePath))
				{
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM revision_sources;"));
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifacts;"));
					Assert.AreEqual(0, ScalarInt(connection, "SELECT COUNT(*) FROM retained_artifact_references;"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void LoadManifest_RejectsDifferentSchemaOrNormalizerContract()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "producer-contract");
				byte[] bytes = CreateManifestBytes("Producer contract");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				RetainRaw(sourceStore, normalization, bytes);

				Assert.Throws<InvalidDataException>(() => sourceStore.LoadManifest(fixture.Revision.Identity,
					"different-schema", NexusCollectionManifestNormalizer.NormalizerVersion));
				Assert.Throws<InvalidDataException>(() => sourceStore.LoadManifest(fixture.Revision.Identity,
					NexusCollectionManifestNormalizer.SchemaIdentity, "different-normalizer"));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void LoadManifest_TamperedRetainedBytesFailClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "tampered-source");
				byte[] bytes = CreateManifestBytes("Tampered source");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				CollectionRevisionSourceRecord saved = RetainRaw(sourceStore, normalization, bytes);
				byte[] tampered = (byte[])bytes.Clone();
				tampered[tampered.Length - 2] ^= 1;
				File.WriteAllBytes(BlobPath(fixture.Store, saved.ManifestSource.ContentHash.Value), tampered);

				Assert.Throws<InvalidDataException>(() => sourceStore.LoadManifest(fixture.Revision.Identity, normalization.Manifest.Source));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void LoadManifest_MissingRetainedBytesFailClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "missing-source");
				byte[] bytes = CreateManifestBytes("Missing source");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				CollectionRevisionSourceRecord saved = RetainRaw(sourceStore, normalization, bytes);
				File.Delete(BlobPath(fixture.Store, saved.ManifestSource.ContentHash.Value));

				Assert.Throws<InvalidDataException>(() => sourceStore.LoadManifest(fixture.Revision.Identity, normalization.Manifest.Source));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RetainManifest_RevisionOwnershipProtectsManifestFromCleanup()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				RevisionFixture fixture = CreateFixture(root, "cleanup-protection");
				byte[] bytes = CreateManifestBytes("Cleanup protection");
				NexusCollectionManifestNormalizationResult normalization = Normalize(bytes, fixture.Revision);
				var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
				CollectionRevisionSourceRecord saved = RetainRaw(sourceStore, normalization, bytes);
				var cleanup = new CollectionsRetainedArtifactCleanupStore(fixture.Store);

				Assert.IsFalse(cleanup.TryMarkForCleanup(saved.RawManifestArtifactId));
				Assert.IsFalse(cleanup.TryCollectUnreferencedArtifact(saved.RawManifestArtifactId));
				Assert.IsTrue(File.Exists(BlobPath(fixture.Store, saved.ManifestSource.ContentHash.Value)));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RevisionSourceRecord_DoesNotExposeSourcePathOrUrl()
		{
			Assert.IsNull(typeof(CollectionRevisionSourceRecord).GetProperty("SourcePath"));
			Assert.IsNull(typeof(CollectionRevisionSourceRecord).GetProperty("Path"));
			Assert.IsNull(typeof(CollectionRevisionSourceRecord).GetProperty("Url"));
			Assert.IsNull(typeof(CollectionRevisionSourceRecord).GetProperty("DownloadUrl"));
			Assert.IsNull(typeof(CollectionRevisionSourceRecord).GetProperty("SignedUrl"));
		}

		private static CollectionRevisionSourceRecord RetainRaw(CollectionsRevisionSourceStore sourceStore,
			NexusCollectionManifestNormalizationResult normalization, byte[] bytes)
		{
			return sourceStore.RetainManifest(normalization.Manifest, CollectionRevisionSourceInputKind.RawManifest,
				normalization.Manifest.Source.ContentHash, bytes.LongLength,
				NexusCollectionBundleImporter.ManifestFileName, normalization.GetRawManifestBytes());
		}

		private static NexusCollectionManifestNormalizationResult Normalize(byte[] bytes, CollectionRevision revision)
		{
			return new NexusCollectionManifestNormalizer().Normalize(bytes, revision);
		}

		private static RevisionFixture CreateFixture(string root, string identity)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			CollectionIdentity collection = CollectionIdentity.FromNexus(identity);
			var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-one", 1), "Revision one", null, 0);
			new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
				new CollectionDefinition(collection, "Collection", null, null), revision);
			return new RevisionFixture(store, revision);
		}

		private static byte[] CreateManifestBytes(string name)
		{
			string json = "{" +
				"\"info\":{\"author\":\"Curator\",\"name\":\"" + name + "\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[],\"modRules\":[]}";
			return new UTF8Encoding(false).GetBytes(json);
		}

		private static CollectionContentHash ComputeHash(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
				return CollectionContentHash.FromSha256(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", String.Empty).ToLowerInvariant());
		}

		private static string BlobPath(CollectionsStore store, string sha256)
		{
			return Path.Combine(store.RetainedContentDirectory, "sha256", sha256.Substring(0, 2), sha256 + ".blob");
		}

		private static SQLiteConnection OpenDatabase(string databasePath)
		{
			var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;Foreign Keys=True;");
			connection.Open();
			return connection;
		}

		private static int ScalarInt(SQLiteConnection connection, string sql)
		{
			using (SQLiteCommand command = connection.CreateCommand())
			{
				command.CommandText = sql;
				return Convert.ToInt32(command.ExecuteScalar());
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-c6151-revision-source-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}

		private sealed class RevisionFixture
		{
			public RevisionFixture(CollectionsStore store, CollectionRevision revision)
			{
				Store = store;
				Revision = revision;
			}

			public CollectionsStore Store { get; }
			public CollectionRevision Revision { get; }
		}
	}
}
