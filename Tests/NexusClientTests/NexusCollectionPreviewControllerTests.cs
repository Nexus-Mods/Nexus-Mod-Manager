using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies the read-only C2.6 preview controller that bridges NXM metadata to a concrete domain revision and bundle preview.
	/// </summary>
	public class NexusCollectionPreviewControllerTests
	{
		[Test]
		public void CreateFromDispatch_MapsConcreteRevisionAndDecorativeSummary()
		{
			using (NexusModsService service = CreateService(request =>
			{
				string body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
					return Json(HttpStatusCode.OK, "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Preview Collection\",\"summary\":\"Summary\",\"user\":{\"name\":\"Curator\"}}}}");
				return Json(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("preview-key"));
				NexusCollectionNxmLink link = ParseLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/latest");
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher.ResolveAsync(link).GetAwaiter().GetResult();
				var controller = new NexusCollectionPreviewController(service.Collections);

				NexusCollectionPreviewSnapshot snapshot = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();

				Assert.IsTrue(snapshot.HasConcreteRevision);
				Assert.AreEqual("2210", snapshot.Definition.Identity.StableId);
				Assert.AreEqual("Preview Collection", snapshot.Definition.DisplayName);
				Assert.AreEqual("Curator", snapshot.Definition.AuthorDisplayName);
				Assert.AreEqual("Summary", snapshot.Definition.Summary);
				Assert.AreEqual("772530", snapshot.Revision.Identity.StableRevisionId);
				Assert.AreEqual(100L, snapshot.Revision.Identity.NexusRevisionNumber);
				Assert.AreEqual(1, snapshot.Revision.DeclaredMemberCount);
				Assert.IsFalse(snapshot.HasManifestPreview);
				Assert.IsNull(snapshot.RevisionError);
				Assert.IsNull(snapshot.SummaryError);
			}
		}

		[Test]
		public void CreateFromDispatch_SummaryFailureKeepsConcreteRevisionUsingRevisionIdentity()
		{
			using (NexusModsService service = CreateService(request =>
			{
				string body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
					return Json(HttpStatusCode.InternalServerError, "{\"message\":\"summary unavailable\"}");
				return Json(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("preview-key"));
				NexusCollectionNxmLink link = ParseLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/100");
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher.ResolveAsync(link).GetAwaiter().GetResult();
				var controller = new NexusCollectionPreviewController(service.Collections);

				NexusCollectionPreviewSnapshot snapshot = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();

				Assert.IsTrue(snapshot.HasConcreteRevision, "Decorative summary lookup must not erase exact identity already supplied by revision metadata.");
				Assert.AreEqual("2210", snapshot.Definition.Identity.StableId);
				Assert.IsNull(snapshot.Definition.DisplayName);
				Assert.AreEqual("772530", snapshot.Revision.Identity.StableRevisionId);
				Assert.IsNotNull(snapshot.SummaryError);
			}
		}

		[Test]
		public void CreateFromDispatch_SummaryIdentityMismatchDoesNotReplaceConcreteRevisionIdentity()
		{
			using (NexusModsService service = CreateService(request =>
			{
				string body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
					return Json(HttpStatusCode.OK, "{\"data\":{\"collection\":{\"id\":9999,\"slug\":\"xxsqm4\",\"name\":\"Wrong Collection\",\"summary\":\"Wrong summary\",\"user\":{\"name\":\"Wrong Curator\"}}}}");
				return Json(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("preview-key"));
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher.ResolveAsync(ParseLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/100")).GetAwaiter().GetResult();
				var controller = new NexusCollectionPreviewController(service.Collections);

				NexusCollectionPreviewSnapshot snapshot = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();

				Assert.IsTrue(snapshot.HasConcreteRevision);
				Assert.AreEqual("2210", snapshot.Definition.Identity.StableId);
				Assert.AreEqual("772530", snapshot.Revision.Identity.StableRevisionId);
				Assert.IsNull(snapshot.Definition.DisplayName, "Decorative metadata from a different collection identity must be ignored.");
				Assert.IsNotNull(snapshot.MetadataWarning);
			}
		}

		[Test]
		public void ImportFile_AttachesNormalizedManifestWithoutChangingConcreteRevisionIdentity()
		{
			using (NexusModsService service = CreateService(request =>
			{
				string body = request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
					return Json(HttpStatusCode.OK, "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Preview Collection\",\"summary\":null,\"user\":null}}}");
				return Json(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("preview-key"));
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher.ResolveAsync(ParseLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/100")).GetAwaiter().GetResult();
				var controller = new NexusCollectionPreviewController(service.Collections);
				NexusCollectionPreviewSnapshot snapshot = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();

				string directory = Path.Combine(Path.GetTempPath(), "nmm-c26-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(directory);
				string path = Path.Combine(directory, "collection.json");
				try
				{
					File.WriteAllText(path,
						"{\"info\":{\"author\":\"A\",\"authorUrl\":\"u\",\"name\":\"C\",\"description\":\"d\",\"domainName\":\"skyrimspecialedition\"},\"mods\":[{\"name\":\"M\",\"version\":\"1\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20}}],\"modRules\":[]}",
						new UTF8Encoding(false));

					NexusCollectionPreviewSnapshot imported = controller.ImportFile(snapshot, path);

					Assert.IsTrue(imported.HasManifestPreview);
					Assert.AreEqual(CollectionCompatibilityStatus.Supported, imported.CapabilityReport.Status);
					Assert.AreEqual(snapshot.Revision.Identity, imported.Revision.Identity);
					Assert.AreEqual(snapshot.Revision.Identity, imported.BundleImport.Manifest.Revision);
					Assert.AreEqual(1, imported.BundleImport.Manifest.Members.Count);
					Assert.IsFalse(snapshot.HasManifestPreview, "Preview snapshots must remain immutable.");
				}
				finally
				{
					Directory.Delete(directory, true);
				}
			}
		}

		[Test]
		public void DomainMapper_RevisionMetadataCanCreateMinimalDefinitionWithoutDecorativeSummary()
		{
			using (NexusModsService service = CreateService(request => Json(
				HttpStatusCode.OK,
				"{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}}}")))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("preview-key"));
				NexusCollectionRevisionMetadata metadata = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Concrete("xxsqm4", 100))
					.GetAwaiter().GetResult().Revision;

				CollectionDefinition definition = NexusCollectionDomainMapper.ToDefinition(metadata);
				Assert.IsNotNull(definition);
				Assert.AreEqual("2210", definition.Identity.StableId);
				Assert.IsNull(definition.DisplayName);
				Assert.IsNull(definition.AuthorDisplayName);
			}
		}

		private static NexusCollectionNxmLink ParseLink(string value)
		{
			NexusCollectionNxmLink link;
			Assert.AreEqual(NexusNxmLinkDisposition.Collection, NexusCollectionNxmLinkParser.Classify(value, out link));
			return link;
		}

		private static NexusModsService CreateService(Func<HttpRequestMessage, HttpResponseMessage> send)
		{
			return new NexusModsService(
				new ApiTransport(new StubHttpMessageHandler((request, token) => Task.FromResult(send(request))), TimeSpan.FromSeconds(5)),
				"NMM-Test/1.0",
				"NMM-Test",
				"1.0");
		}

		private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
		{
			return new HttpResponseMessage(statusCode) { Content = new StringContent(body ?? string.Empty) };
		}

		private sealed class StubHttpMessageHandler : HttpMessageHandler
		{
			private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

			public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
			{
				_send = send;
			}

			protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				return _send(request, cancellationToken);
			}
		}
	}
}
