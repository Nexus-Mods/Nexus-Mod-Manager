using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class NexusCollectionBundledArtifactMaterializerTests
	{
		[Test]
		public void Materialize_ExactBundleDirectoryProducesDeterministicRootedArchive()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				using (BundleFixture fixture = BundleFixture.Create(root, "Bundled - Selected (v1)", "selectedTag", 4096,
					new Dictionary<string, byte[]>
					{
						{ "bundled/Bundled - Selected (v1)/Tools/tool.exe", Encoding.UTF8.GetBytes("tool-bytes") },
						{ "bundled/Bundled - Selected (v1)/config.ini", Encoding.UTF8.GetBytes("setting=true") },
						{ "bundled/Bundled - Other (v1)/must-not-appear.txt", Encoding.UTF8.GetBytes("other") },
						{ "unrelated.txt", Encoding.UTF8.GetBytes("outer") }
					}))
				{
					var materializer = new NexusCollectionBundledArtifactMaterializer(fixture.Store, fixture.SourceStore);
					NexusCollectionBundledArtifactMaterialization first = materializer.Materialize(fixture.Request, CancellationToken.None);
					byte[] firstBytes = File.ReadAllBytes(first.StagingPath);

					Assert.AreEqual("Bundled - Selected (v1)", first.FileExpression);
					Assert.AreEqual("selectedTag", first.ReferenceTag);
					Assert.Greater(first.Artifact.ByteLength, 0);
					AssertArchive(first.StagingPath, new Dictionary<string, string>
					{
						{ "Tools/tool.exe", "tool-bytes" },
						{ "config.ini", "setting=true" }
					});

					NexusCollectionBundledArtifactMaterialization second = materializer.Materialize(fixture.Request, CancellationToken.None);
					byte[] secondBytes = File.ReadAllBytes(second.StagingPath);
					CollectionAssert.AreEqual(firstBytes, secondBytes, "Same retained revision/member must materialize identical archive bytes.");
					Assert.AreEqual(first.Artifact.ContentHash, second.Artifact.ContentHash);
					Assert.AreEqual(first.Artifact.ByteLength, second.Artifact.ByteLength);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Materialize_TraversalInsideSelectedBundleDirectoryFailsClosed()
		{
			AssertMaterializationFails(new Dictionary<string, byte[]>
			{
				{ "bundled/Bundled - Unsafe/../escape.txt", Encoding.UTF8.GetBytes("escape") }
			}, "Bundled - Unsafe", 1024);
		}

		[Test]
		public void Materialize_WindowsCaseCollisionFailsClosed()
		{
			AssertMaterializationFails(new Dictionary<string, byte[]>
			{
				{ "bundled/Bundled - Collision/File.txt", Encoding.UTF8.GetBytes("first") },
				{ "bundled/Bundled - Collision/file.txt", Encoding.UTF8.GetBytes("second") }
			}, "Bundled - Collision", 1024);
		}

		[Test]
		public void Materialize_PayloadExceedingManifestBoundFailsClosed()
		{
			AssertMaterializationFails(new Dictionary<string, byte[]>
			{
				{ "bundled/Bundled - Bounded/payload.bin", new byte[17] }
			}, "Bundled - Bounded", 16);
		}

		private static void AssertMaterializationFails(IDictionary<string, byte[]> entries, string expression, long declaredSize)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				using (BundleFixture fixture = BundleFixture.Create(root, expression, "bundleTag", declaredSize, entries))
				{
					var materializer = new NexusCollectionBundledArtifactMaterializer(fixture.Store, fixture.SourceStore);
					Assert.Throws<InvalidDataException>(() => materializer.Materialize(fixture.Request, CancellationToken.None));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static void AssertArchive(string path, IDictionary<string, string> expected)
		{
			using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
			{
				CollectionAssert.AreEquivalent(expected.Keys, archive.Entries.Select(x => x.FullName).ToArray());
				foreach (ZipArchiveEntry entry in archive.Entries)
				{
					using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8, true))
						Assert.AreEqual(expected[entry.FullName], reader.ReadToEnd());
				}
			}
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-stage2-bundle-tests-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}

		private sealed class BundleFixture : IDisposable
		{
			private BundleFixture(CollectionsStore store, CollectionsRevisionSourceStore sourceStore,
				CollectionAcquisitionRequest request, string archivePath)
			{
				Store = store;
				SourceStore = sourceStore;
				Request = request;
				ArchivePath = archivePath;
			}

			public CollectionsStore Store { get; private set; }
			public CollectionsRevisionSourceStore SourceStore { get; private set; }
			public CollectionAcquisitionRequest Request { get; private set; }
			public string ArchivePath { get; private set; }

			public static BundleFixture Create(string root, string fileExpression, string tag, long fileSize,
				IDictionary<string, byte[]> entries)
			{
				var store = new CollectionsStore(root);
				store.CreateNew();
				CollectionIdentity collection = CollectionIdentity.FromNexus("stage2-bundle-fixture");
				var revision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(collection, "revision-6", 6),
					"Revision 6", null, 1);
				new CollectionsCatalogStore(store).SaveDefinitionAndRevision(
					new CollectionDefinition(collection, "Bundle fixture", null, null), revision);

				string json = "{" +
					"\"info\":{\"author\":\"Curator\",\"name\":\"Bundle fixture\",\"domainName\":\"fallout4\"}," +
					"\"mods\":[{" +
					"\"name\":\"Bundled member\",\"version\":\"1\",\"optional\":false,\"domainName\":\"fallout4\"," +
					"\"source\":{\"type\":\"bundle\",\"fileSize\":" + fileSize.ToString(System.Globalization.CultureInfo.InvariantCulture) +
					",\"updatePolicy\":\"exact\",\"fileExpression\":" + Newtonsoft.Json.JsonConvert.SerializeObject(fileExpression) +
					",\"tag\":" + Newtonsoft.Json.JsonConvert.SerializeObject(tag) + "}}]," +
					"\"modRules\":[],\"tools\":[],\"pluginRules\":{\"plugins\":[],\"groups\":[]}," +
					"\"collectionConfig\":{\"recommendNewProfile\":true,\"referenceTagScheme\":\"v1\"}}";
				byte[] manifestBytes = new UTF8Encoding(false).GetBytes(json);
				string archivePath = Path.Combine(root, "collection.zip");
				using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
				{
					WriteEntry(archive, "collection.json", manifestBytes);
					foreach (KeyValuePair<string, byte[]> entry in entries)
						WriteEntry(archive, entry.Key, entry.Value);
				}

				NexusCollectionBundleImportResult imported = new NexusCollectionBundleImporter().ImportFile(archivePath, revision);
				Assert.AreEqual(CollectionCompatibilityStatus.Supported, imported.CapabilityReport.Status,
					"Synthetic fixture must isolate bundle acquisition semantics without unrelated blockers.");
				var sourceStore = new CollectionsRevisionSourceStore(store);
				sourceStore.RetainManifest(imported.Manifest, CollectionRevisionSourceInputKind.Archive,
					imported.BundleContentHash, imported.BundleByteLength, imported.ManifestEntryName,
					imported.Normalization.GetRawManifestBytes());
				using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
					sourceStore.RetainBundle(revision.Identity, stream, CancellationToken.None);

				NormalizedCollectionMember member = imported.Manifest.Members.Single();
				var memberPlan = new ResolvedCollectionMemberPlan(member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
				var plan = new ResolvedCollectionPlan(
					CollectionPlanIdentity.From(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), 1),
					CollectionTargetIdentity.FromFingerprint("stage2-target"),
					CollectionExecutionPolicy.InstallIntoCurrentSetup(),
					new CollectionCurrentStateFingerprint("managed-state-v1", "stage2-state"),
					imported.CapabilityReport,
					new[] { memberPlan });
				CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(
					Guid.Parse("11111111-2222-3333-4444-555555555555"), plan, member.IdentityResolution.Key);
				return new BundleFixture(store, sourceStore, request, archivePath);
			}

			private static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
			{
				ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
				entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
				using (Stream output = entry.Open())
					output.Write(bytes, 0, bytes.Length);
			}

			public void Dispose()
			{
				// CollectionsStore has no externally owned stream lifetime; fixture files are removed by the caller.
			}
		}
	}
}
