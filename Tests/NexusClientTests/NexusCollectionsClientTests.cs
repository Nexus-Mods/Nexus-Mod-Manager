using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
	/// Verifies the first Nexus Collections provider slice over the owned GraphQL/session infrastructure.
	/// </summary>
	public class NexusCollectionsClientTests
	{
		/// <summary>
		/// Ensures collection summary lookup uses the owned GraphQL endpoint, keeps the slug as a locator, and preserves
		/// the stable provider ID separately from decorative metadata.
		/// </summary>
		[Test]
		public void GetSummary_MapsProviderMetadataAndUsesSlugOnlyLookup()
		{
			RequestCapture capture = null;
			const string fixture = "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Example Collection\",\"summary\":\"Example summary\",\"user\":{\"name\":\"Curator\"}}}}";
			using (NexusModsService service = CreateService(fixture, request => capture = RequestCapture.From(request)))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));

				NexusCollectionSummaryLookupResult result = service.Collections
					.GetSummaryAsync("xxsqm4")
					.GetAwaiter().GetResult();

				Assert.AreEqual("xxsqm4", result.RequestedSlug);
				Assert.AreEqual(service.CaptureSession().Generation, result.SessionGeneration);
				Assert.IsFalse(result.HasErrors);
				Assert.IsNotNull(result.Collection);
				Assert.IsTrue(result.Collection.HasStableIdentity);
				Assert.AreEqual(2210L, result.Collection.CollectionId);
				Assert.AreEqual("xxsqm4", result.Collection.CollectionSlug);
				Assert.AreEqual("Example Collection", result.Collection.Name);
				Assert.AreEqual("Curator", result.Collection.AuthorDisplayName);
				Assert.AreEqual("Example summary", result.Collection.Summary);

				Assert.AreEqual(HttpMethod.Post, capture.Method);
				Assert.AreEqual("https://api.nexusmods.com/v2/graphql", capture.Uri.AbsoluteUri);
				CollectionAssert.AreEqual(new[] { "collection-api-key" }, capture.Headers["apikey"]);
				GraphQlRequestFixture body = ApiJson.Deserialize<GraphQlRequestFixture>(capture.Body);
				Assert.AreEqual("NmmCollectionSummary", body.OperationName);
				Assert.AreEqual("xxsqm4", body.Variables.Slug);
				Assert.IsNull(body.Variables.Revision);
				StringAssert.Contains("collection(slug: $slug)", body.Query);
				StringAssert.Contains("user {", body.Query);
				Assert.IsFalse(body.Query.Contains("viewAdultContent"));
			}
		}

		/// <summary>
		/// Ensures missing decorative fields and their GraphQL errors do not erase stable collection identity or prevent
		/// construction of a minimal CollectionDefinition.
		/// </summary>
		[Test]
		public void GetSummary_PartialDecorativeDataStillMapsDefinitionAndPreservesErrors()
		{
			const string fixture = "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":null,\"summary\":null,\"user\":null}},\"errors\":[{\"message\":\"Name unavailable\",\"path\":[\"collection\",\"name\"],\"extensions\":{\"code\":\"FIELD_UNAVAILABLE\"}}]}";
			using (NexusModsService service = CreateService(fixture))
			{
				NexusCollectionSummaryLookupResult result = service.Collections
					.GetSummaryAsync("xxsqm4")
					.GetAwaiter().GetResult();
				CollectionDefinition definition = NexusCollectionDomainMapper.ToDefinition(result.Collection);

				Assert.IsNotNull(definition);
				Assert.AreEqual(CollectionOrigin.NexusMods, definition.Origin);
				Assert.AreEqual("2210", definition.Identity.StableId);
				Assert.IsNull(definition.DisplayName);
				Assert.IsNull(definition.AuthorDisplayName);
				Assert.IsNull(definition.Summary);
				Assert.IsTrue(result.HasErrors);
				Assert.AreEqual("FIELD_UNAVAILABLE", result.Errors[0].Code);
			}
		}

		/// <summary>
		/// Ensures the provider adapter maps stable numeric IDs, not the public slug or curator-facing revision number,
		/// into the opaque C1 collection/revision identities.
		/// </summary>
		[Test]
		public void DomainMapper_UsesStableProviderIdsAndDeclaredMemberCount()
		{
			const string summaryFixture = "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Example Collection\",\"summary\":\"Summary\",\"user\":{\"name\":\"Curator\"}}}}";
			const string revisionFixture = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}";

			CollectionDefinition definition;
			using (NexusModsService service = CreateService(summaryFixture))
			{
				NexusCollectionSummaryLookupResult summary = service.Collections.GetSummaryAsync("xxsqm4").GetAwaiter().GetResult();
				definition = NexusCollectionDomainMapper.ToDefinition(summary.Collection);
			}

			using (NexusModsService service = CreateService(revisionFixture))
			{
				NexusCollectionRevisionLookupResult lookup = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4"))
					.GetAwaiter().GetResult();
				CollectionRevision revision = NexusCollectionDomainMapper.ToRevision(definition, lookup.Revision);

				Assert.IsNotNull(revision);
				Assert.AreEqual("2210", revision.Collection.StableId);
				Assert.AreEqual("772530", revision.Identity.StableRevisionId);
				Assert.AreEqual(100L, revision.Identity.NexusRevisionNumber);
				Assert.AreEqual(567, revision.DeclaredMemberCount);
				Assert.IsNull(revision.RevisionLabel);
				Assert.IsNull(revision.Notes);
			}
		}

		/// <summary>
		/// Ensures a summary/revision mismatch is rejected instead of silently constructing cross-collection provenance.
		/// </summary>
		[Test]
		public void DomainMapper_RejectsRevisionFromDifferentCollectionIdentity()
		{
			const string summaryFixture = "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":\"Example Collection\",\"summary\":null,\"user\":null}}}";
			const string revisionFixture = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":9999,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":null}}}";
			CollectionDefinition definition;
			NexusCollectionRevisionMetadata revisionMetadata;

			using (NexusModsService service = CreateService(summaryFixture))
				definition = NexusCollectionDomainMapper.ToDefinition(service.Collections.GetSummaryAsync("xxsqm4").GetAwaiter().GetResult().Collection);
			using (NexusModsService service = CreateService(revisionFixture))
				revisionMetadata = service.Collections.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4")).GetAwaiter().GetResult().Revision;

			Assert.Throws<ArgumentException>(() => NexusCollectionDomainMapper.ToRevision(definition, revisionMetadata));
		}

		/// <summary>
		/// Ensures incomplete identity remains preview/provider data and is not promoted into fabricated C1 identity.
		/// </summary>
		[Test]
		public void DomainMapper_IncompleteProviderIdentityReturnsNull()
		{
			const string missingCollectionId = "{\"data\":{\"collection\":{\"slug\":\"xxsqm4\",\"name\":\"Example Collection\",\"summary\":null,\"user\":null}}}";
			using (NexusModsService service = CreateService(missingCollectionId))
			{
				NexusCollectionSummaryMetadata summary = service.Collections.GetSummaryAsync("xxsqm4").GetAwaiter().GetResult().Collection;
				Assert.IsFalse(summary.HasStableIdentity);
				Assert.IsNull(NexusCollectionDomainMapper.ToDefinition(summary));
			}

			const string completeSummary = "{\"data\":{\"collection\":{\"id\":2210,\"slug\":\"xxsqm4\",\"name\":null,\"summary\":null,\"user\":null}}}";
			const string incompleteRevision = "{\"data\":{\"collectionRevision\":{\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":null}}}";
			CollectionDefinition definition;
			using (NexusModsService service = CreateService(completeSummary))
				definition = NexusCollectionDomainMapper.ToDefinition(service.Collections.GetSummaryAsync("xxsqm4").GetAwaiter().GetResult().Collection);
			using (NexusModsService service = CreateService(incompleteRevision))
			{
				NexusCollectionRevisionMetadata revision = service.Collections.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4")).GetAwaiter().GetResult().Revision;
				Assert.IsFalse(revision.HasStableIdentity);
				Assert.IsNull(NexusCollectionDomainMapper.ToRevision(definition, revision));
			}
		}

		/// <summary>
		/// Ensures the relative revision downloadLink is resolved only against the trusted Nexus API origin and that the
		/// provider's final CDN locations remain ephemeral acquisition data rather than additional credentialed requests.
		/// </summary>
		[Test]
		public void ResolveBundle_UsesTrustedAuthorizationRouteAndReturnsEphemeralLocations()
		{
			var captures = new List<RequestCapture>();
			using (NexusModsService service = CreateService(request =>
			{
				captures.Add(RequestCapture.From(request));
				if (request.Method == HttpMethod.Post)
				{
					return CreateResponse(
						HttpStatusCode.OK,
						"{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}");
				}

				HttpResponseMessage response = CreateResponse(
					HttpStatusCode.OK,
					"{\"download_links\":[{\"URI\":\"https://collection-cdn.example.invalid/bundle.zip?token=short-lived\",\"name\":\"Primary CDN\",\"short_name\":\"primary\"}]}");
				response.Headers.TryAddWithoutValidation("x-rl-hourly-remaining", "77");
				return response;
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));
				NexusCollectionRevisionMetadata revision = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4"))
					.GetAwaiter().GetResult().Revision;

				NexusCollectionBundleResolutionResult result = service.Collections.ResolveBundleAsync(revision).GetAwaiter().GetResult();

				Assert.AreSame(revision, result.Revision);
				Assert.AreEqual(service.CaptureSession().Generation, result.SessionGeneration);
				Assert.IsTrue(result.HasDownloadLocations);
				Assert.AreEqual(1, result.DownloadLocations.Count);
				Assert.AreEqual("https://collection-cdn.example.invalid/bundle.zip?token=short-lived", result.DownloadLocations[0].Uri.AbsoluteUri);
				Assert.AreEqual("Primary CDN", result.DownloadLocations[0].Name);
				Assert.AreEqual("primary", result.DownloadLocations[0].ShortName);

				Assert.AreEqual(2, captures.Count, "Bundle resolution must not make a second credentialed request to the returned CDN URL.");
				Assert.AreEqual(HttpMethod.Get, captures[1].Method);
				Assert.AreEqual("https://api.nexusmods.com/v2/collections/2210/revisions/772530/download_link", captures[1].Uri.AbsoluteUri);
				CollectionAssert.AreEqual(new[] { "collection-api-key" }, captures[1].Headers["apikey"]);
				Assert.AreEqual(77, service.RateLimits.GetSnapshot(NexusApiSurface.Collections).HourlyRemaining);
				Assert.IsNull(service.RateLimits.GetSnapshot(NexusApiSurface.V1).HourlyRemaining);
			}
		}

		/// <summary>
		/// Ensures compatibility with the provider response shape which exposes one download_link instead of an array.
		/// </summary>
		[Test]
		public void ResolveBundle_AcceptsSingletonDownloadLinkShape()
		{
			using (NexusModsService service = CreateService(request => request.Method == HttpMethod.Post
				? CreateResponse(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}")
				: CreateResponse(HttpStatusCode.OK, "{\"download_link\":{\"URI\":\"https://cdn.example.invalid/collection.7z\",\"name\":\"Fallback\",\"short_name\":\"fallback\"}}")))
			{
				NexusCollectionRevisionMetadata revision = service.Collections.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4")).GetAwaiter().GetResult().Revision;
				NexusCollectionBundleResolutionResult result = service.Collections.ResolveBundleAsync(revision).GetAwaiter().GetResult();

				Assert.AreEqual(1, result.DownloadLocations.Count);
				Assert.AreEqual("https://cdn.example.invalid/collection.7z", result.DownloadLocations[0].Uri.AbsoluteUri);
			}
		}

		/// <summary>
		/// Ensures provider metadata cannot redirect API credentials to another host or another collection/revision route.
		/// </summary>
		[Test]
		public void ResolveBundle_RejectsUntrustedAndMismatchedAuthorizationPathsBeforeGet()
		{
			AssertUnsafeBundlePathRejected("https://evil.example.invalid/v2/collections/2210/revisions/772530/download_link");
			AssertUnsafeBundlePathRejected("/v2/collections/9999/revisions/772530/download_link");
			AssertUnsafeBundlePathRejected("/v2/collections/2210/revisions/999999/download_link");
			AssertUnsafeBundlePathRejected("/v2/collections/2210/revisions/772530/download_link?next=https://evil.example.invalid/");
		}

		/// <summary>
		/// Ensures a redirect response from the credentialed API authorization request is surfaced as an error rather than
		/// followed to a CDN/external host with the Nexus API key attached.
		/// </summary>
		[Test]
		public void ResolveBundle_DoesNotFollowAuthorizationRedirects()
		{
			var captures = new List<RequestCapture>();
			using (NexusModsService service = CreateService(request =>
			{
				captures.Add(RequestCapture.From(request));
				if (request.Method == HttpMethod.Post)
					return CreateResponse(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}");

				HttpResponseMessage response = CreateResponse(HttpStatusCode.Redirect, string.Empty);
				response.Headers.Location = new Uri("https://evil.example.invalid/bundle.zip");
				return response;
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));
				NexusCollectionRevisionMetadata revision = service.Collections.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4")).GetAwaiter().GetResult().Revision;

				ApiException error = Assert.Throws<ApiException>(() => service.Collections.ResolveBundleAsync(revision).GetAwaiter().GetResult());
				Assert.AreEqual(HttpStatusCode.Redirect, error.StatusCode);
				Assert.AreEqual(2, captures.Count);
				Assert.AreEqual(NexusRequestPolicy.ApiHost, captures[1].Uri.Host);
			}
		}

		/// <summary>
		/// Ensures final acquisition URLs are restricted to absolute HTTPS locations and missing links are not reported as
		/// a successful authorization result.
		/// </summary>
		[Test]
		public void ResolveBundle_RejectsUnsafeOrMissingFinalLocations()
		{
			AssertInvalidBundleResolutionResponse("{\"download_links\":[{\"URI\":\"http://cdn.example.invalid/bundle.zip\"}]}");
			AssertInvalidBundleResolutionResponse("{\"download_links\":[]}");
			AssertInvalidBundleResolutionResponse("{}");
		}

		/// <summary>
		/// Ensures summary locators receive the same strict validation as revision locators before network work begins.
		/// </summary>
		[Test]
		public void GetSummary_RejectsInvalidSlug()
		{
			using (NexusModsService service = CreateService("{\"data\":{}}"))
			{
				Assert.Throws<ArgumentException>(() => service.Collections.GetSummaryAsync(null).GetAwaiter().GetResult());
				Assert.Throws<ArgumentException>(() => service.Collections.GetSummaryAsync("   ").GetAwaiter().GetResult());
				Assert.Throws<ArgumentException>(() => service.Collections.GetSummaryAsync(" xxsqm4").GetAwaiter().GetResult());
			}
		}

		/// <summary>
		/// Ensures latest lookup uses the verified slug-only operation, resolves concrete provider identity, and keeps
		/// the relative bundle-resolution path separate from artifact identity.
		/// </summary>
		[Test]
		public void GetRevision_LatestMapsConcreteRevisionWithoutMagicSentinel()
		{
			RequestCapture capture = null;
			const string fixture = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}";
			using (NexusModsService service = CreateService(fixture, request => capture = RequestCapture.From(request)))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));
				NexusCollectionRevisionRequest request = NexusCollectionRevisionRequest.Latest("xxsqm4");

				NexusCollectionRevisionLookupResult result = service.Collections.GetRevisionAsync(request).GetAwaiter().GetResult();

				Assert.AreSame(request, result.Request);
				Assert.AreEqual(service.CaptureSession().Generation, result.SessionGeneration);
				Assert.IsFalse(result.HasErrors);
				Assert.IsNotNull(result.Revision);
				Assert.IsTrue(result.Revision.HasStableIdentity);
				Assert.AreEqual(2210L, result.Revision.CollectionId);
				Assert.AreEqual(772530L, result.Revision.RevisionId);
				Assert.AreEqual(100L, result.Revision.RevisionNumber);
				Assert.AreEqual(567L, result.Revision.ModCount);
				Assert.AreEqual("/v2/collections/2210/revisions/772530/download_link", result.Revision.BundleResolutionPath);

				Assert.AreEqual(HttpMethod.Post, capture.Method);
				Assert.AreEqual("https://api.nexusmods.com/v2/graphql", capture.Uri.AbsoluteUri);
				CollectionAssert.AreEqual(new[] { "collection-api-key" }, capture.Headers["apikey"]);
				GraphQlRequestFixture body = ApiJson.Deserialize<GraphQlRequestFixture>(capture.Body);
				Assert.AreEqual("NmmCollectionRevisionLatest", body.OperationName);
				Assert.AreEqual("xxsqm4", body.Variables.Slug);
				Assert.IsNull(body.Variables.Revision);
				StringAssert.Contains("collectionRevision(slug: $slug)", body.Query);
				Assert.IsFalse(body.Query.Contains("revision: $revision"));
				Assert.IsFalse(body.Query.Contains("viewAdultContent"));
			}
		}

		/// <summary>
		/// Ensures a concrete curator revision is sent through the provider's revision GraphQL argument rather than
		/// being confused with the stable provider revision ID returned by the response.
		/// </summary>
		[Test]
		public void GetRevision_ConcreteUsesRevisionArgumentAndPreservesStableRevisionId()
		{
			RequestCapture capture = null;
			const string fixture = "{\"data\":{\"collectionRevision\":{\"id\":900001,\"collectionId\":2210,\"revisionNumber\":42,\"modCount\":12,\"downloadLink\":\"/v2/collections/2210/revisions/900001/download_link\"}}}";
			using (NexusModsService service = CreateService(fixture, request => capture = RequestCapture.From(request)))
			{
				NexusCollectionRevisionLookupResult result = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Concrete("xxsqm4", 42))
					.GetAwaiter().GetResult();

				Assert.AreEqual(42L, result.Revision.RevisionNumber);
				Assert.AreEqual(900001L, result.Revision.RevisionId);
				GraphQlRequestFixture body = ApiJson.Deserialize<GraphQlRequestFixture>(capture.Body);
				Assert.AreEqual("NmmCollectionRevisionByNumber", body.OperationName);
				Assert.AreEqual(42, body.Variables.Revision);
				StringAssert.Contains("collectionRevision(slug: $slug, revision: $revision)", body.Query);
			}
		}

		/// <summary>
		/// Ensures useful revision metadata is retained when GraphQL also reports a field-level error.
		/// </summary>
		[Test]
		public void GetRevision_PreservesPartialDataAndGraphQlErrors()
		{
			const string fixture = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":null}},\"errors\":[{\"message\":\"Download path unavailable\",\"path\":[\"collectionRevision\",\"downloadLink\"],\"extensions\":{\"code\":\"FORBIDDEN\"}}]}";
			using (NexusModsService service = CreateService(fixture))
			{
				NexusCollectionRevisionLookupResult result = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4"))
					.GetAwaiter().GetResult();

				Assert.IsNotNull(result.Revision);
				Assert.IsTrue(result.Revision.HasStableIdentity);
				Assert.IsNull(result.Revision.BundleResolutionPath);
				Assert.IsTrue(result.HasErrors);
				Assert.AreEqual(1, result.Errors.Count);
				Assert.AreEqual("FORBIDDEN", result.Errors[0].Code);
				CollectionAssert.AreEqual(
					new[] { "collectionRevision", "downloadLink" },
					result.Errors[0].Path.Select(segment => segment.ToString()).ToArray());
			}
		}

		/// <summary>
		/// Ensures missing identity fields remain visible as partial metadata instead of being fabricated from slug,
		/// revision number, or the download-link route.
		/// </summary>
		[Test]
		public void GetRevision_MissingIdentityRemainsIncomplete()
		{
			const string fixture = "{\"data\":{\"collectionRevision\":{\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}";
			using (NexusModsService service = CreateService(fixture))
			{
				NexusCollectionRevisionLookupResult result = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4"))
					.GetAwaiter().GetResult();

				Assert.IsNotNull(result.Revision);
				Assert.IsFalse(result.Revision.HasStableIdentity);
				Assert.IsNull(result.Revision.CollectionId);
				Assert.IsNull(result.Revision.RevisionId);
				Assert.AreEqual(100L, result.Revision.RevisionNumber);
			}
		}

		/// <summary>
		/// Ensures an errors-only GraphQL response remains an ordinary provider result and does not invent revision data.
		/// </summary>
		[Test]
		public void GetRevision_ErrorsOnlyReturnsNullRevisionAndPreservedError()
		{
			const string fixture = "{\"errors\":[{\"message\":\"Collection revision not found\",\"extensions\":{\"code\":\"NOT_FOUND\"}}]}";
			using (NexusModsService service = CreateService(fixture))
			{
				NexusCollectionRevisionLookupResult result = service.Collections
					.GetRevisionAsync(NexusCollectionRevisionRequest.Concrete("xxsqm4", 999))
					.GetAwaiter().GetResult();

				Assert.IsNull(result.Revision);
				Assert.IsTrue(result.HasErrors);
				Assert.AreEqual("NOT_FOUND", result.Errors[0].Code);
			}
		}

		/// <summary>
		/// Ensures request construction rejects ambiguous/malformed selectors before any API request is started.
		/// </summary>
		[Test]
		public void RevisionRequest_RejectsInvalidSlugAndRevisionNumber()
		{
			Assert.Throws<ArgumentException>(() => NexusCollectionRevisionRequest.Latest(null));
			Assert.Throws<ArgumentException>(() => NexusCollectionRevisionRequest.Latest("   "));
			Assert.Throws<ArgumentException>(() => NexusCollectionRevisionRequest.Latest(" xxsqm4"));
			Assert.Throws<ArgumentOutOfRangeException>(() => NexusCollectionRevisionRequest.Concrete("xxsqm4", 0));
			Assert.Throws<ArgumentOutOfRangeException>(() => NexusCollectionRevisionRequest.Concrete("xxsqm4", -1));
		}

		private static void AssertUnsafeBundlePathRejected(string bundlePath)
		{
			int requestCount = 0;
			string graphQl = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"downloadLink\":" + ApiJson.Serialize(bundlePath) + "}}}";
			using (NexusModsService service = CreateService(request =>
			{
				requestCount++;
				return CreateResponse(HttpStatusCode.OK, graphQl);
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));
				NexusCollectionRevisionMetadata revision = service.Collections.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4")).GetAwaiter().GetResult().Revision;

				Assert.Throws<InvalidOperationException>(() => service.Collections.ResolveBundleAsync(revision).GetAwaiter().GetResult());
				Assert.AreEqual(1, requestCount, "Unsafe bundle paths must be rejected before a credentialed GET is sent.");
			}
		}

		private static void AssertInvalidBundleResolutionResponse(string bundleResponse)
		{
			using (NexusModsService service = CreateService(request => request.Method == HttpMethod.Post
				? CreateResponse(HttpStatusCode.OK, "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}")
				: CreateResponse(HttpStatusCode.OK, bundleResponse)))
			{
				NexusCollectionRevisionMetadata revision = service.Collections.GetRevisionAsync(NexusCollectionRevisionRequest.Latest("xxsqm4")).GetAwaiter().GetResult().Revision;
				ApiException error = Assert.Throws<ApiException>(() => service.Collections.ResolveBundleAsync(revision).GetAwaiter().GetResult());
				Assert.AreEqual(ApiErrorKind.InvalidResponse, error.ErrorKind);
			}
		}

		private static NexusModsService CreateService(Func<HttpRequestMessage, HttpResponseMessage> send)
		{
			if (send == null)
				throw new ArgumentNullException(nameof(send));

			return new NexusModsService(
				new ApiTransport(new StubHttpMessageHandler((request, token) => Task.FromResult(send(request))), TimeSpan.FromSeconds(5)),
				"NMM-Test/1.0",
				"NMM-Test",
				"1.0");
		}

		private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string content)
		{
			return new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(content ?? string.Empty)
			};
		}

		/// <summary>
		/// Creates a Nexus service backed by one deterministic GraphQL fixture response.
		/// </summary>
		private static NexusModsService CreateService(string content, Action<HttpRequestMessage> inspect = null)
		{
			var handler = new StubHttpMessageHandler((request, token) =>
			{
				inspect?.Invoke(request);
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = new StringContent(content)
				});
			});

			return new NexusModsService(
				new ApiTransport(handler, TimeSpan.FromSeconds(5)),
				"NMM-Test/1.0",
				"NMM-Test",
				"1.0");
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

		private sealed class RequestCapture
		{
			public HttpMethod Method { get; private set; }
			public Uri Uri { get; private set; }
			public string Body { get; private set; }
			public IDictionary<string, string[]> Headers { get; private set; }

			public static RequestCapture From(HttpRequestMessage request)
			{
				return new RequestCapture
				{
					Method = request.Method,
					Uri = request.RequestUri,
					Body = request.Content == null ? null : request.Content.ReadAsStringAsync().GetAwaiter().GetResult(),
					Headers = request.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase)
				};
			}
		}

		private sealed class GraphQlRequestFixture
		{
			public string Query { get; set; }
			public string OperationName { get; set; }
			public RevisionVariablesFixture Variables { get; set; }
		}

		private sealed class RevisionVariablesFixture
		{
			public string Slug { get; set; }
			public int? Revision { get; set; }
		}
	}
}
