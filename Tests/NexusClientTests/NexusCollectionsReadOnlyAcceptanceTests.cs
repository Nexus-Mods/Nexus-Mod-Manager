using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Closes the C2 read-only milestone with cross-layer acceptance/regression coverage.
	/// </summary>
	/// <remarks>
	/// These tests intentionally stop before C4 acquisition persistence and every native mutation boundary. They prove that
	/// Collection NXM metadata can become a concrete, normalized preview while the ordinary mod NXM parser remains intact.
	/// </remarks>
	public class NexusCollectionsReadOnlyAcceptanceTests
	{
		[Test]
		public void CollectionNxm_ResolvesAuthorizesAndNormalizesConcreteReadOnlyPreview()
		{
			var requests = new List<RequestCapture>();
			using (NexusModsService service = CreateService(request =>
			{
				requests.Add(RequestCapture.From(request));
				if (request.Method == HttpMethod.Get)
				{
					return Json(
						HttpStatusCode.OK,
						"{\"download_links\":[{\"URI\":\"https://collection-cdn.example.invalid/revision-772530.7z?token=short-lived\",\"name\":\"Primary\",\"short_name\":\"primary\"}]}");
				}

				string body = request.Content == null
					? string.Empty
					: request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
				{
					return Json(
						HttpStatusCode.OK,
						"{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Acceptance Collection\",\"summary\":\"Read-only C2 preview\",\"user\":{\"name\":\"Curator\"}}}}");
				}

				return Json(
					HttpStatusCode.OK,
					"{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":2,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("acceptance-api-key"));

				NexusCollectionNxmLink link;
				Assert.AreEqual(
					NexusNxmLinkDisposition.Collection,
					NexusCollectionNxmLinkParser.Classify(
						"nxm://skyrimspecialedition/collections/xxsqm4/revisions/latest?key=ephemeral&expires=123&user_id=7",
						out link));
				Assert.IsTrue(link.RevisionRequest.IsLatest);
				Assert.AreEqual(string.Empty, link.SourceUri.Query);

				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher.ResolveAsync(link).GetAwaiter().GetResult();
				Assert.IsTrue(dispatch.HasConcreteRevision);
				Assert.AreEqual(2210L, dispatch.RevisionLookup.Revision.CollectionId);
				Assert.AreEqual(772530L, dispatch.RevisionLookup.Revision.RevisionId);
				Assert.AreEqual(100L, dispatch.RevisionLookup.Revision.RevisionNumber);

				NexusCollectionBundleResolutionResult authorization = service.Collections
					.ResolveBundleAsync(dispatch.RevisionLookup.Revision)
					.GetAwaiter().GetResult();
				Assert.AreEqual(1, authorization.DownloadLocations.Count);
				Assert.AreEqual(
					"collection-cdn.example.invalid",
					authorization.DownloadLocations[0].Uri.Host,
					"The provider may return a CDN location, but C2 must not fetch it or treat it as durable identity.");

				var controller = new NexusCollectionPreviewController(service.Collections);
				NexusCollectionPreviewSnapshot preview = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();
				Assert.IsTrue(preview.HasConcreteRevision);
				Assert.AreEqual("2210", preview.Definition.Identity.StableId);
				Assert.AreEqual("772530", preview.Revision.Identity.StableRevisionId);
				Assert.AreEqual("Acceptance Collection", preview.Definition.DisplayName);
				Assert.AreEqual("Curator", preview.Definition.AuthorDisplayName);
				Assert.AreEqual(2, preview.Revision.DeclaredMemberCount);

				string directory = CreateTemporaryDirectory();
				try
				{
					string manifestPath = Path.Combine(directory, "collection.json");
					File.WriteAllText(manifestPath, BuildSupportedSelectionWithUnselectedUnsupportedOptional(), new UTF8Encoding(false));

					NexusCollectionPreviewSnapshot imported = controller.ImportFile(preview, manifestPath);

					Assert.IsTrue(imported.HasManifestPreview);
					Assert.AreEqual(preview.Revision.Identity, imported.Revision.Identity);
					Assert.AreEqual(preview.Revision.Identity, imported.BundleImport.Manifest.Revision);
					Assert.AreEqual(CollectionCompatibilityStatus.Supported, imported.CapabilityReport.Status);
					Assert.IsTrue(imported.CapabilityReport.HasUnselectedUnsupportedOptionals);
					Assert.AreEqual(2, imported.BundleImport.Manifest.Members.Count);
					Assert.IsTrue(imported.BundleImport.Manifest.Members[0].IsSelected);
					Assert.IsFalse(imported.BundleImport.Manifest.Members[1].IsSelected);
					Assert.AreEqual(
						CollectionCompatibilityStatus.Unsupported,
						imported.CapabilityReport.MemberReports[1].Status,
						"Unsupported optional behavior remains visible even though it does not block the current selected closure.");
				}
				finally
				{
					Directory.Delete(directory, true);
				}

				Assert.AreEqual(3, requests.Count, "C2 preview should issue one revision lookup, one bundle authorization, and one summary lookup in this flow.");
				Assert.IsTrue(requests.All(request => StringComparer.OrdinalIgnoreCase.Equals(request.Uri.Host, NexusRequestPolicy.ApiHost)));
				Assert.IsTrue(requests.All(request => request.HasApiKey));
				Assert.IsFalse(requests.Any(request => StringComparer.OrdinalIgnoreCase.Equals(request.Uri.Host, "collection-cdn.example.invalid")),
					"C2.3 returns the ephemeral CDN location but must not make a credentialed request to it.");
			}
		}

		[Test]
		public void PartialGraphQlErrors_DoNotEraseConcreteRevisionPreview()
		{
			using (NexusModsService service = CreateService(request =>
			{
				string body = request.Content == null
					? string.Empty
					: request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
				{
					return Json(
						HttpStatusCode.OK,
						"{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":null,\"summary\":null,\"user\":null}},\"errors\":[{\"message\":\"Decorative metadata unavailable\",\"extensions\":{\"code\":\"FIELD_UNAVAILABLE\"}}]}");
				}

				return Json(
					HttpStatusCode.OK,
					"{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}},\"errors\":[{\"message\":\"Download link unavailable\",\"extensions\":{\"code\":\"FORBIDDEN\"}}]}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("acceptance-api-key"));
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher
					.ResolveAsync(ParseCollectionLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/100"))
					.GetAwaiter().GetResult();
				var controller = new NexusCollectionPreviewController(service.Collections);

				NexusCollectionPreviewSnapshot preview = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();

				Assert.IsTrue(preview.HasConcreteRevision);
				Assert.AreEqual("2210", preview.Definition.Identity.StableId);
				Assert.AreEqual("772530", preview.Revision.Identity.StableRevisionId);
				Assert.IsTrue(preview.RevisionLookup.HasErrors);
				Assert.AreEqual("FORBIDDEN", preview.RevisionLookup.Errors[0].Code);
				Assert.IsTrue(preview.SummaryLookup.HasErrors);
				Assert.AreEqual("FIELD_UNAVAILABLE", preview.SummaryLookup.Errors[0].Code);
				Assert.IsNull(preview.Definition.DisplayName);
			}
		}

		[Test]
		public void AccountSwitchDuringRevisionLookup_DoesNotPublishStaleConcreteRevision()
		{
			var entered = new TaskCompletionSource<bool>();
			var release = new TaskCompletionSource<bool>();
			using (NexusModsService service = CreateService(async (request, token) =>
			{
				entered.TrySetResult(true);
				await release.Task.ConfigureAwait(false);
				return Json(
					HttpStatusCode.OK,
					"{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("first-account-key"));
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				Task<NexusCollectionNxmDispatchResult> pending = dispatcher.ResolveAsync(
					ParseCollectionLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/100"));

				Assert.IsTrue(entered.Task.Wait(TimeSpan.FromSeconds(2)), "The fixture request never started.");
				service.ReplaceCredentials(NexusCredentials.FromApiKey("second-account-key"));
				release.TrySetResult(true);

				NexusCollectionNxmDispatchResult result = pending.GetAwaiter().GetResult();

				Assert.IsFalse(result.HasConcreteRevision);
				Assert.IsFalse(result.Succeeded);
				Assert.IsNotNull(result.Error, "A result resolved under an invalidated account generation must not be published as current metadata.");
			}
		}

		[Test]
		public void MalformedManifest_RemainsUnsupportedPreviewWithoutChangingResolvedRevision()
		{
			using (NexusModsService service = CreateService(request =>
			{
				string body = request.Content == null
					? string.Empty
					: request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
				if (body.Contains("NmmCollectionSummary"))
					return Json(HttpStatusCode.OK, "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Malformed Fixture\",\"summary\":null,\"user\":null}}}");

				return Json(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":1,\"downloadLink\":null}}}");
			}))
			{
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmDispatchResult dispatch = dispatcher
					.ResolveAsync(ParseCollectionLink("nxm://skyrimspecialedition/collections/xxsqm4/revisions/100"))
					.GetAwaiter().GetResult();
				var controller = new NexusCollectionPreviewController(service.Collections);
				NexusCollectionPreviewSnapshot preview = controller.CreateFromDispatchAsync(dispatch).GetAwaiter().GetResult();
				string directory = CreateTemporaryDirectory();
				try
				{
					string manifestPath = Path.Combine(directory, "collection.json");
					File.WriteAllText(manifestPath, "{not-json", new UTF8Encoding(false));

					NexusCollectionPreviewSnapshot imported = controller.ImportFile(preview, manifestPath);

					Assert.IsTrue(imported.HasManifestPreview);
					Assert.AreEqual(preview.Revision.Identity, imported.Revision.Identity);
					Assert.AreEqual(CollectionCompatibilityStatus.Unsupported, imported.CapabilityReport.Status);
					Assert.IsFalse(imported.BundleImport.Manifest.IsMemberSetComplete);
					Assert.IsTrue(imported.CapabilityReport.ManifestIssues.Any(issue => issue.Code == "manifest.json-invalid"));
				}
				finally
				{
					Directory.Delete(directory, true);
				}
			}
		}

		[Test]
		public void LegacyModNxmAndV1DownloadFlow_RemainUnchangedAlongsideCollectionsProvider()
		{
			const string nxm = "nxm://fallout4/mods/100/files/200?key=legacy-key&expires=123&user_id=7";
			NexusCollectionNxmLink collectionLink;

			Assert.AreEqual(NexusNxmLinkDisposition.LegacyModOrFile, NexusCollectionNxmLinkParser.Classify(nxm, out collectionLink));
			Assert.IsNull(collectionLink);

			NexusModLink modLink;
			Assert.IsTrue(NexusModLinkParser.TryParse(nxm, out modLink));
			Assert.AreEqual("fallout4", modLink.GameDomain);
			Assert.AreEqual("100", modLink.ModId);
			Assert.AreEqual("200", modLink.FileId);
			Assert.AreEqual("?key=legacy-key&expires=123&user_id=7", modLink.SourceUri.Query,
				"The Collection classifier must not sanitize or consume ordinary mod NXM authorization parameters before the legacy downloader sees them.");

			var requests = new List<RequestCapture>();
			using (NexusModsService service = CreateService(request =>
			{
				requests.Add(RequestCapture.From(request));
				if (request.Method == HttpMethod.Get)
				{
					return Json(
						HttpStatusCode.OK,
						"[{\"name\":\"Nexus CDN\",\"short_name\":\"nexuscdn\",\"URI\":\"https://cdn.example.invalid/file.zip\"}]");
				}

				return Json(
					HttpStatusCode.OK,
					"{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Collection\",\"summary\":null,\"user\":null}}}");
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("account-api-key"));
				var legacyLinks = service.V1
					.GetDownloadLinksAsync("fallout4", 100, 200, "website-key", 1700000123)
					.GetAwaiter().GetResult();
				NexusCollectionSummaryLookupResult summary = service.Collections.GetSummaryAsync("xxsqm4").GetAwaiter().GetResult();

				Assert.AreEqual(1, legacyLinks.Length);
				Assert.AreEqual(2210L, summary.Collection.CollectionId);
				Assert.AreEqual(2, requests.Count);
				Assert.AreEqual(HttpMethod.Get, requests[0].Method);
				Assert.AreEqual(
					"/v1/games/fallout4/mods/100/files/200/download_link.json?key=website-key&expires=1700000123",
					requests[0].Uri.PathAndQuery);
				Assert.AreEqual(HttpMethod.Post, requests[1].Method);
				Assert.AreEqual("/v2/graphql", requests[1].Uri.AbsolutePath);
				Assert.IsTrue(requests.All(request => request.HasApiKey));
			}
		}

		[Test]
		public void PreviewController_PublicSurfaceContainsNoNativeMutationEntryPoint()
		{
			MethodInfo[] publicMethods = typeof(NexusCollectionPreviewController)
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
			string[] mutationVerbs = { "Install", "Activate", "Reinstall", "Uninstall", "Replace", "Detach", "Remove" };

			foreach (MethodInfo method in publicMethods)
			{
				Assert.IsFalse(
					mutationVerbs.Any(verb => method.Name.IndexOf(verb, StringComparison.OrdinalIgnoreCase) >= 0),
					"C2 preview unexpectedly exposes a mutation-shaped method: " + method.Name);
				Assert.IsFalse(IsNativeModManagementType(method.ReturnType),
					"C2 preview must not return a native mod-management execution handle: " + method.Name);
				foreach (ParameterInfo parameter in method.GetParameters())
				{
					Assert.IsFalse(IsNativeModManagementType(parameter.ParameterType),
						"C2 preview must not accept native mod-management mutation dependencies: " + method.Name);
				}
			}

			ConstructorInfo[] constructors = typeof(NexusCollectionPreviewController).GetConstructors();
			Assert.IsTrue(constructors.Length > 0);
			Assert.IsFalse(constructors
				.SelectMany(constructor => constructor.GetParameters())
				.Any(parameter => IsNativeModManagementType(parameter.ParameterType)));
		}

		private static bool IsNativeModManagementType(Type type)
		{
			if (type == null)
				return false;

			if (type.IsByRef || type.IsArray || type.IsPointer)
				return IsNativeModManagementType(type.GetElementType());

			string ns = type.Namespace;
			if (ns != null && ns.StartsWith("Nexus.Client.ModManagement", StringComparison.Ordinal))
				return true;

			return type.IsGenericType && type.GetGenericArguments().Any(IsNativeModManagementType);
		}

		private static NexusCollectionNxmLink ParseCollectionLink(string value)
		{
			NexusCollectionNxmLink link;
			Assert.AreEqual(NexusNxmLinkDisposition.Collection, NexusCollectionNxmLinkParser.Classify(value, out link));
			return link;
		}

		private static string CreateTemporaryDirectory()
		{
			string directory = Path.Combine(Path.GetTempPath(), "nmm-c27-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			return directory;
		}

		private static string BuildSupportedSelectionWithUnselectedUnsupportedOptional()
		{
			return "{" +
				"\"info\":{\"author\":\"Curator\",\"authorUrl\":\"https://example.invalid/author\",\"name\":\"Acceptance Collection\",\"description\":\"C2 acceptance fixture\",\"domainName\":\"skyrimspecialedition\"}," +
				"\"mods\":[" +
				"{\"name\":\"Required Exact Nexus Member\",\"version\":\"1.0\",\"optional\":false,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"nexus\",\"modId\":10,\"fileId\":20,\"updatePolicy\":\"exact\"}}," +
				"{\"name\":\"Optional Manual Member\",\"version\":\"1.0\",\"optional\":true,\"domainName\":\"skyrimspecialedition\",\"source\":{\"type\":\"manual\",\"instructions\":\"Acquire manually\"}}" +
				"],\"modRules\":[]}";
		}

		private static NexusModsService CreateService(Func<HttpRequestMessage, HttpResponseMessage> send)
		{
			if (send == null)
				throw new ArgumentNullException(nameof(send));

			return CreateService((request, token) => Task.FromResult(send(request)));
		}

		private static NexusModsService CreateService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
		{
			if (send == null)
				throw new ArgumentNullException(nameof(send));

			return new NexusModsService(
				new ApiTransport(new StubHttpMessageHandler(send), TimeSpan.FromSeconds(5)),
				"NMM-Test/1.0",
				"NMM-Test",
				"1.0");
		}

		private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
		{
			return new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(body ?? string.Empty, Encoding.UTF8, "application/json")
			};
		}

		private sealed class RequestCapture
		{
			private RequestCapture(HttpMethod method, Uri uri, bool hasApiKey)
			{
				Method = method;
				Uri = uri;
				HasApiKey = hasApiKey;
			}

			public HttpMethod Method { get; }
			public Uri Uri { get; }
			public bool HasApiKey { get; }

			public static RequestCapture From(HttpRequestMessage request)
			{
				IEnumerable<string> values;
				bool hasApiKey = request.Headers.TryGetValues("apikey", out values) && values.Any(value => !string.IsNullOrWhiteSpace(value));
				return new RequestCapture(request.Method, request.RequestUri, hasApiKey);
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
	}
}
