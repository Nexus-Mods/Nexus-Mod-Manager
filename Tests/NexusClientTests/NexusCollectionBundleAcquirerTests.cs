using System;
using System.Data.SQLite;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies C6.15.2 concrete bundle acquisition remains credential-separated, bounded and durably revision-owned.
	/// </summary>
	public class NexusCollectionBundleAcquirerTests
	{
		private const string ArchiveManifest = "{\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Remote Archive\",\"description\":\"Example collection\",\"domainName\":\"skyrim\"},\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}],\"modRules\":[]}";
		private const string ArchiveBase64 = "UEsDBBQAAAAIAAAAIVBUMUX/xgAAACwBAAAPAAAAY29sbGVjdGlvbi5qc29ubY+xbsMwDER/JeAsxElHbUXRoUuHAJ2KDIJEw0Ql0aUkI4Hhfw9tB+jSjbzjPZIzUO4Z7Ayu1YEFLLw1cVUr85S+JKo61DoW23V4c2mMeKQ8uUihe8YMZJdQ5y6YuOLhVfxAE6oesHihsRJntd/3+MFzjOg3UUc4OcqfO6D83IUSLAYShwL2e/5D/zYSDJqYUMoOPGvHG93pmb2LBf8HGijcxOP6a72Pq5Px1gpsiz4C2PPJQE8R1/rltCzXzbm0iOsZ1+UBUEsBAhQDFAAAAAgAAAAhUFQxRf/GAAAALAEAAA8AAAAAAAAAAAAAAIABAAAAAGNvbGxlY3Rpb24uanNvblBLBQYAAAAAAQABAD0AAADzAAAAAAA=";

		[Test]
		public void AcquireAsync_RawManifestSeparatesApiCredentialsAndDurablyRetainsExactSource()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] manifest = CreateManifestBytes("Remote raw manifest");
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/collection.json?token=short-lived-secret");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				{
					int contentRequests = 0;
					var contentHandler = new StubHttpMessageHandler((request, token) =>
					{
						contentRequests++;
						Assert.AreEqual(contentUri, request.RequestUri);
						Assert.IsFalse(request.Headers.Contains("apikey"));
						Assert.IsNull(request.Headers.Authorization);
						return Task.FromResult(CreateBinaryResponse(HttpStatusCode.OK, manifest));
					});

					using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
						contentHandler, NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
					{
						NexusCollectionBundleAcquisitionResult result = acquirer
							.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult();

						Assert.AreEqual(1, contentRequests);
						Assert.AreEqual(1, fixture.BundleResolutionRequests);
						Assert.IsTrue(fixture.BundleResolutionRequestHadApiKey);
						Assert.AreEqual(NexusCollectionBundleInputKind.RawManifest, result.BundleImport.InputKind);
						Assert.AreEqual(CollectionRevisionSourceInputKind.RawManifest, result.RevisionSource.InputKind);
						Assert.IsNotNull(result.RevisionSource.RawManifestArtifactId);
						Assert.IsNotNull(result.RevisionSource.RawBundleArtifactId);
						Assert.AreEqual(result.RevisionSource.RawManifestArtifactId, result.RevisionSource.RawBundleArtifactId,
							"A remotely acquired raw collection.json is one immutable blob protected by both revision roles.");

						var sourceStore = new CollectionsRevisionSourceStore(fixture.Store);
						CollectionAssert.AreEqual(manifest, sourceStore.LoadManifest(fixture.Revision.Identity,
							NexusCollectionManifestNormalizer.SchemaIdentity, NexusCollectionManifestNormalizer.NormalizerVersion));
						AssertRevisionRoles(fixture.Store, fixture.Revision.Identity, result.RevisionSource);
					}

					AssertDatabaseDoesNotContain(fixture.Store.DatabasePath, "short-lived-secret");
					AssertStagingIsEmpty(fixture.Store);
					Assert.IsNull(typeof(NexusCollectionBundleAcquisitionResult).GetProperty("DownloadUrl"));
					Assert.IsNull(typeof(NexusCollectionBundleAcquisitionResult).GetProperty("SourcePath"));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_ArchiveRetainsDistinctOuterBundleAndExactManifest()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] archive = Convert.FromBase64String(ArchiveBase64);
				byte[] manifest = Encoding.UTF8.GetBytes(ArchiveManifest);
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/revision-100.zip?token=archive-secret");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
					new StubHttpMessageHandler((request, token) => Task.FromResult(CreateBinaryResponse(HttpStatusCode.OK, archive))),
					NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
				{
					NexusCollectionBundleAcquisitionResult result = acquirer
						.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult();

					Assert.AreEqual(NexusCollectionBundleInputKind.Archive, result.BundleImport.InputKind);
					Assert.AreEqual(CollectionRevisionSourceInputKind.Archive, result.RevisionSource.InputKind);
					Assert.AreNotEqual(result.RevisionSource.RawManifestArtifactId, result.RevisionSource.RawBundleArtifactId);
					Assert.AreEqual(archive.LongLength, result.RevisionSource.BundleByteLength);
					CollectionAssert.AreEqual(manifest, new CollectionsRevisionSourceStore(fixture.Store).LoadManifest(
						fixture.Revision.Identity, NexusCollectionManifestNormalizer.SchemaIdentity,
						NexusCollectionManifestNormalizer.NormalizerVersion));
					AssertRevisionRoles(fixture.Store, fixture.Revision.Identity, result.RevisionSource);
					AssertDatabaseDoesNotContain(fixture.Store.DatabasePath, "archive-secret");
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_ExpiredContentAuthorizationFailsWithoutPublishingRevisionSource()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/bundle.zip?token=expired-secret");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
					new StubHttpMessageHandler((request, token) => Task.FromResult(CreateBinaryResponse(HttpStatusCode.Forbidden, new byte[0]))),
					NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
				{
					Assert.Throws<IOException>(() => acquirer.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult());
					Assert.IsNull(new CollectionsRevisionSourceStore(fixture.Store).GetSource(fixture.Revision.Identity));
					Assert.AreEqual(0, CountRows(fixture.Store.DatabasePath, "retained_artifacts"));
					AssertStagingIsEmpty(fixture.Store);
					AssertDatabaseDoesNotContain(fixture.Store.DatabasePath, "expired-secret");
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_RejectsPrivateContentLocationBeforeContentRequest()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Uri contentUri = new Uri("https://127.0.0.1/bundle.zip?token=must-not-run");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				{
					int contentRequests = 0;
					using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
						new StubHttpMessageHandler((request, token) =>
						{
							contentRequests++;
							return Task.FromResult(CreateBinaryResponse(HttpStatusCode.OK, CreateManifestBytes("Unexpected")));
						}), NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
					{
						Assert.Throws<InvalidDataException>(() => acquirer.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult());
					}

					Assert.AreEqual(0, contentRequests);
					Assert.IsNull(new CollectionsRevisionSourceStore(fixture.Store).GetSource(fixture.Revision.Identity));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_ContentRedirectIsNotFollowed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/bundle.zip?token=redirect-source");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				{
					int contentRequests = 0;
					using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
						new StubHttpMessageHandler((request, token) =>
						{
							contentRequests++;
							var response = CreateBinaryResponse(HttpStatusCode.Redirect, new byte[0]);
							response.Headers.Location = new Uri("https://other-cdn.example.invalid/bundle.zip?token=redirect-target");
							return Task.FromResult(response);
						}), NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
					{
						Assert.Throws<IOException>(() => acquirer.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult());
					}

					Assert.AreEqual(1, contentRequests, "Content redirects must not be followed before the new destination is independently validated.");
					Assert.IsNull(new CollectionsRevisionSourceStore(fixture.Store).GetSource(fixture.Revision.Identity));
					AssertDatabaseDoesNotContain(fixture.Store.DatabasePath, "redirect-target");
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_RejectsRevisionMismatchBeforeBundleAuthorization()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root,
					new Uri("https://collection-cdn.example.invalid/collection.json?token=unused")))
				{
					var otherRevision = new CollectionRevision(CollectionRevisionIdentity.FromNexus(
						fixture.Revision.Collection, "772531", 101), null, null, fixture.Revision.DeclaredMemberCount);
					new CollectionsCatalogStore(fixture.Store).SaveRevision(otherRevision);

					using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
						new StubHttpMessageHandler((request, token) => Task.FromResult(CreateBinaryResponse(HttpStatusCode.OK,
							CreateManifestBytes("Unexpected")))), NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
					{
						Assert.Throws<InvalidOperationException>(() => acquirer.AcquireAsync(fixture.ProviderRevision, otherRevision).GetAwaiter().GetResult());
					}

					Assert.AreEqual(0, fixture.BundleResolutionRequests, "Mismatched immutable identity must fail before signed bundle authorization.");
					Assert.IsNull(new CollectionsRevisionSourceStore(fixture.Store).GetSource(otherRevision.Identity));
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_InterruptedDownloadDeletesStagingAndRetainsNothing()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] manifest = CreateManifestBytes("Interrupted");
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/collection.json?token=interrupt");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
					new StubHttpMessageHandler((request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StreamContent(new ThrowAfterReadStream(manifest, Math.Max(1, manifest.Length / 3)))
					})), NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
				{
					Assert.Throws<IOException>(() => acquirer.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult());
					Assert.IsNull(new CollectionsRevisionSourceStore(fixture.Store).GetSource(fixture.Revision.Identity));
					Assert.AreEqual(0, CountRows(fixture.Store.DatabasePath, "retained_artifacts"));
					AssertStagingIsEmpty(fixture.Store);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AcquireAsync_EnforcesConfiguredByteCeilingBeforeRetention()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] manifest = CreateManifestBytes("Too large for test ceiling");
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/collection.json?token=bounded");
				using (AcquisitionFixture fixture = AcquisitionFixture.Create(root, contentUri))
				using (var acquirer = new NexusCollectionBundleAcquirer(fixture.Service.Collections, fixture.Store,
					new StubHttpMessageHandler((request, token) => Task.FromResult(CreateBinaryResponse(HttpStatusCode.OK, manifest))), 32))
				{
					Assert.Throws<InvalidDataException>(() => acquirer.AcquireAsync(fixture.ProviderRevision, fixture.Revision).GetAwaiter().GetResult());
					Assert.IsNull(new CollectionsRevisionSourceStore(fixture.Store).GetSource(fixture.Revision.Identity));
					Assert.AreEqual(0, CountRows(fixture.Store.DatabasePath, "retained_artifacts"));
					AssertStagingIsEmpty(fixture.Store);
				}
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RemoteAcquisition_ConvergesWithLocalImportOnSameRetainedRevisionSource()
		{
			string localRoot = CreateTemporaryDirectory();
			string remoteRoot = CreateTemporaryDirectory();
			try
			{
				byte[] manifest = CreateManifestBytes("Local remote equivalence");
				Uri contentUri = new Uri("https://collection-cdn.example.invalid/collection.json?token=equivalent");
				using (AcquisitionFixture local = AcquisitionFixture.Create(localRoot, contentUri))
				using (AcquisitionFixture remote = AcquisitionFixture.Create(remoteRoot, contentUri))
				{
					string localPath = Path.Combine(localRoot, NexusCollectionBundleImporter.ManifestFileName);
					File.WriteAllBytes(localPath, manifest);
					NexusCollectionBundleImportResult localImport = new NexusCollectionBundleImporter().ImportFile(localPath, local.Revision);
					var localSources = new CollectionsRevisionSourceStore(local.Store);
					CollectionRevisionSourceRecord localSource = localSources.RetainManifest(localImport.Manifest,
						CollectionRevisionSourceInputKind.RawManifest, localImport.BundleContentHash, localImport.BundleByteLength,
						localImport.ManifestEntryName, localImport.Normalization.GetRawManifestBytes());
					using (var localStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
						localSource = localSources.RetainBundle(local.Revision.Identity, localStream);

					NexusCollectionBundleAcquisitionResult remoteResult;
					using (var acquirer = new NexusCollectionBundleAcquirer(remote.Service.Collections, remote.Store,
						new StubHttpMessageHandler((request, token) => Task.FromResult(CreateBinaryResponse(HttpStatusCode.OK, manifest))),
						NexusCollectionBundleAcquirer.DefaultMaximumBundleBytes))
					{
						remoteResult = acquirer.AcquireAsync(remote.ProviderRevision, remote.Revision).GetAwaiter().GetResult();
					}

					Assert.AreEqual(localSource, remoteResult.RevisionSource);
					Assert.AreEqual(localImport.BundleContentHash, remoteResult.BundleImport.BundleContentHash);
					Assert.AreEqual(localImport.Manifest.Source, remoteResult.BundleImport.Manifest.Source);
				}
			}
			finally
			{
				Directory.Delete(localRoot, true);
				Directory.Delete(remoteRoot, true);
			}
		}

		private static byte[] CreateManifestBytes(string name)
		{
			return Encoding.UTF8.GetBytes("{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"" + name + "\",\"description\":\"Example collection\",\"domainName\":\"skyrim\"}," +
				"\"mods\":[{\"name\":\"Required\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrim\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}]," +
				"\"modRules\":[]}");
		}

		private static HttpResponseMessage CreateBinaryResponse(HttpStatusCode statusCode, byte[] bytes)
		{
			return new HttpResponseMessage(statusCode)
			{
				Content = new ByteArrayContent(bytes ?? new byte[0])
			};
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c6152-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void AssertRevisionRoles(CollectionsStore store, CollectionRevisionIdentity revision,
			CollectionRevisionSourceRecord source)
		{
			var references = new CollectionsRetainedArtifactReferenceStore(store);
			string ownerId = CollectionsRevisionSourceStore.GetRevisionOwnerId(revision);
			Assert.AreEqual(source.RawManifestArtifactId, references.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Revision, ownerId, CollectionsRevisionSourceStore.ManifestRole).ArtifactId);
			Assert.AreEqual(source.RawBundleArtifactId, references.GetReferenceForOwnerRole(
				CollectionsRetainedArtifactOwnerKind.Revision, ownerId, CollectionsRevisionSourceStore.BundleRole).ArtifactId);
		}

		private static void AssertStagingIsEmpty(CollectionsStore store)
		{
			string stagingRoot = Path.Combine(store.StoreDirectory, ".bundle-staging");
			if (!Directory.Exists(stagingRoot))
				return;
			Assert.AreEqual(0, Directory.GetFileSystemEntries(stagingRoot).Length);
		}

		private static int CountRows(string databasePath, string table)
		{
			using (var connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;Read Only=True;"))
			{
				connection.Open();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
					return Convert.ToInt32(command.ExecuteScalar());
				}
			}
		}

		private static void AssertDatabaseDoesNotContain(string databasePath, string secret)
		{
			string ascii = Encoding.ASCII.GetString(File.ReadAllBytes(databasePath));
			StringAssert.DoesNotContain(secret, ascii);
		}

		private sealed class AcquisitionFixture : IDisposable
		{
			private AcquisitionFixture(CollectionsStore store, NexusModsService service,
				NexusCollectionRevisionMetadata providerRevision, CollectionRevision revision)
			{
				Store = store;
				Service = service;
				ProviderRevision = providerRevision;
				Revision = revision;
			}

			public CollectionsStore Store { get; }
			public NexusModsService Service { get; }
			public NexusCollectionRevisionMetadata ProviderRevision { get; }
			public CollectionRevision Revision { get; }
			public int BundleResolutionRequests { get; private set; }
			public bool BundleResolutionRequestHadApiKey { get; private set; }

			public static AcquisitionFixture Create(string root, Uri contentUri)
			{
				AcquisitionFixture fixture = null;
				var store = new CollectionsStore(root);
				store.CreateNew();

				var handler = new StubHttpMessageHandler((request, token) =>
				{
					if (request.Method == HttpMethod.Post)
					{
						return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
						{
							Content = new StringContent("{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}")
						});
					}

					if (fixture == null)
						throw new InvalidOperationException("The acquisition fixture was not initialized before bundle authorization.");
					fixture.BundleResolutionRequests++;
					fixture.BundleResolutionRequestHadApiKey = request.Headers.Contains("apikey");
					return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
					{
						Content = new StringContent("{\"download_links\":[{\"URI\":\"" + EscapeJson(contentUri.AbsoluteUri) + "\",\"name\":\"Primary\",\"short_name\":\"primary\"}]}")
					});
				});

				var service = new NexusModsService(new ApiTransport(handler, TimeSpan.FromSeconds(5)),
					"NMM-Test/1.0", "NMM-Test", "1.0");
				service.ReplaceCredentials(NexusCredentials.FromApiKey("bundle-api-key"));
				NexusCollectionRevisionMetadata providerRevision = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Concrete("xxsqm4", 100)).GetAwaiter().GetResult().Revision;
				CollectionDefinition definition = NexusCollectionDomainMapper.ToDefinition(providerRevision);
				CollectionRevision revision = NexusCollectionDomainMapper.ToRevision(definition, providerRevision);
				new CollectionsCatalogStore(store).SaveDefinitionAndRevision(definition, revision);

				fixture = new AcquisitionFixture(store, service, providerRevision, revision);
				return fixture;
			}

			public void Dispose()
			{
				Service.Dispose();
			}

			private static string EscapeJson(string value)
			{
				return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
			}
		}

		private sealed class StubHttpMessageHandler : HttpMessageHandler
		{
			private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

			public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
			{
				_send = send ?? throw new ArgumentNullException(nameof(send));
			}

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				return _send(request, cancellationToken);
			}
		}

		private sealed class ThrowAfterReadStream : Stream
		{
			private readonly MemoryStream _inner;
			private readonly int _throwAfter;
			private int _read;

			public ThrowAfterReadStream(byte[] bytes, int throwAfter)
			{
				_inner = new MemoryStream(bytes ?? new byte[0], false);
				_throwAfter = throwAfter;
			}

			public override bool CanRead { get { return true; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return false; } }
			public override long Length { get { throw new NotSupportedException(); } }
			public override long Position { get { return _read; } set { throw new NotSupportedException(); } }

			public override int Read(byte[] buffer, int offset, int count)
			{
				if (_read >= _throwAfter)
					throw new IOException("Simulated interrupted bundle stream.");
				int allowed = Math.Min(count, _throwAfter - _read);
				int read = _inner.Read(buffer, offset, allowed);
				_read += read;
				return read;
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
	}
}
