using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.Infrastructure;
using Nexus.Client.OnlineServices.NexusMods;
using Nexus.Client.OnlineServices.NexusMods.Collections;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>
	/// Verifies Collection NXM parsing/routing without changing the mature ordinary mod NXM path.
	/// </summary>
	public class NexusCollectionNxmDispatchTests
	{
		[Test]
		public void Parser_RecognizesConcreteCollectionRevisionWithoutUsingSlugAsIdentity()
		{
			NexusCollectionNxmLink link;
			NexusNxmLinkDisposition disposition = NexusCollectionNxmLinkParser.Classify(
				"nxm://skyrimspecialedition/collections/xxsqm4/revisions/100?key=temporary&expires=123&user_id=7",
				out link);

			Assert.AreEqual(NexusNxmLinkDisposition.Collection, disposition);
			Assert.IsNotNull(link);
			Assert.AreEqual("skyrimspecialedition", link.GameDomain);
			Assert.AreEqual("xxsqm4", link.CollectionSlug);
			Assert.IsFalse(link.RevisionRequest.IsLatest);
			Assert.AreEqual(100, link.RevisionRequest.RevisionNumber);
			Assert.AreEqual("xxsqm4", link.RevisionRequest.CollectionSlug);
			Assert.AreEqual(string.Empty, link.SourceUri.Query, "NXM key/expiry query material is not retained by the Collection route.");
		}

		[Test]
		public void Parser_RepresentsLatestAsTypedSelectorWithoutMagicSentinel()
		{
			NexusCollectionNxmLink link;
			Assert.AreEqual(
				NexusNxmLinkDisposition.Collection,
				NexusCollectionNxmLinkParser.Classify("nxm://fallout4/collections/abcdef/revisions/latest", out link));

			Assert.IsTrue(link.RevisionRequest.IsLatest);
			Assert.IsNull(link.RevisionRequest.RevisionNumber);
		}

		[Test]
		public void Parser_LeavesOrdinaryModNxmOnLegacyPipeline()
		{
			const string nxm = "nxm://fallout4/mods/100/files/200?key=legacy&expires=123&user_id=7";
			NexusCollectionNxmLink collectionLink;

			Assert.AreEqual(
				NexusNxmLinkDisposition.LegacyModOrFile,
				NexusCollectionNxmLinkParser.Classify(nxm, out collectionLink));
			Assert.IsNull(collectionLink);

			NexusModLink modLink;
			Assert.IsTrue(NexusModLinkParser.TryParse(nxm, out modLink));
			Assert.AreEqual("100", modLink.ModId);
			Assert.AreEqual("200", modLink.FileId);
		}

		[Test]
		public void Parser_MalformedCollectionPathCannotFallThroughAsModDownload()
		{
			NexusCollectionNxmLink link;
			Assert.AreEqual(
				NexusNxmLinkDisposition.InvalidCollection,
				NexusCollectionNxmLinkParser.Classify("nxm://fallout4/collections/abcdef/files/100", out link));
			Assert.IsNull(link);

			Assert.AreEqual(
				NexusNxmLinkDisposition.InvalidCollection,
				NexusCollectionNxmLinkParser.Classify("nxm://fallout4/collections/abcdef/revisions/0", out link));
			Assert.IsNull(link);
		}

		[Test]
		public void Parser_RejectsLegacyNumericCollectionIdShapeInsteadOfMisreadingItAsSlug()
		{
			NexusCollectionNxmLink link;
			Assert.AreEqual(
				NexusNxmLinkDisposition.InvalidCollection,
				NexusCollectionNxmLinkParser.Classify("nxm://skyrim/collections/2210/revisions/772530", out link));
			Assert.IsNull(link);
		}

		[Test]
		public void Dispatcher_ResolvesLatestOnceToConcreteProviderRevision()
		{
			const string fixture = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"modCount\":567,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}";
			using (NexusModsService service = CreateService((request, token) => Task.FromResult(CreateResponse(HttpStatusCode.OK, fixture))))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmLink link;
				Assert.AreEqual(
					NexusNxmLinkDisposition.Collection,
					NexusCollectionNxmLinkParser.Classify("nxm://skyrimspecialedition/collections/xxsqm4/revisions/latest", out link));

				NexusCollectionNxmDispatchResult result = dispatcher.ResolveAsync(link).GetAwaiter().GetResult();

				Assert.IsTrue(result.Succeeded);
				Assert.IsTrue(result.HasConcreteRevision);
				Assert.IsTrue(result.Link.RevisionRequest.IsLatest, "The source locator remains useful provenance.");
				Assert.AreEqual(2210L, result.RevisionLookup.Revision.CollectionId);
				Assert.AreEqual(772530L, result.RevisionLookup.Revision.RevisionId);
				Assert.AreEqual(100L, result.RevisionLookup.Revision.RevisionNumber);
			}
		}

		[Test]
		public void Dispatcher_CoalescesInFlightSelectorAndRetainsCompletedStartupResult()
		{
			int requestCount = 0;
			var release = new TaskCompletionSource<bool>();
			const string fixture = "{\"data\":{\"collectionRevision\":{\"id\":772530,\"collectionId\":2210,\"revisionNumber\":100,\"downloadLink\":\"/v2/collections/2210/revisions/772530/download_link\"}}}";
			using (NexusModsService service = CreateService(async (request, token) =>
			{
				Interlocked.Increment(ref requestCount);
				await release.Task.ConfigureAwait(false);
				return CreateResponse(HttpStatusCode.OK, fixture);
			}))
			{
				service.ReplaceCredentials(NexusCredentials.FromApiKey("collection-api-key"));
				var dispatcher = new NexusCollectionNxmDispatcher(service.Collections);
				NexusCollectionNxmLink link;
				NexusCollectionNxmLinkParser.Classify("nxm://skyrimspecialedition/collections/xxsqm4/revisions/latest", out link);

				using (var completed = new ManualResetEventSlim(false))
				{
					dispatcher.DispatchCompleted += (sender, args) => completed.Set();
					dispatcher.Enqueue(link);
					dispatcher.Enqueue(link);

					SpinWait.SpinUntil(() => Volatile.Read(ref requestCount) == 1, TimeSpan.FromSeconds(2));
					Assert.AreEqual(1, requestCount);
					release.SetResult(true);
					Assert.IsTrue(completed.Wait(TimeSpan.FromSeconds(2)));
				}

				NexusCollectionNxmDispatchResult queued;
				Assert.IsTrue(dispatcher.TryDequeueCompleted(out queued));
				Assert.IsTrue(queued.HasConcreteRevision);
				Assert.AreEqual(100L, queued.RevisionLookup.Revision.RevisionNumber);
				Assert.IsFalse(dispatcher.TryDequeueCompleted(out queued));
				Assert.AreEqual(1, requestCount);
			}
		}

		private static NexusModsService CreateService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
		{
			return new NexusModsService(
				new ApiTransport(new StubHttpMessageHandler(send), TimeSpan.FromSeconds(5)),
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
