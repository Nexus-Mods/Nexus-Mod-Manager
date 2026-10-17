namespace NexusClientTests
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Net;
	using System.Linq;
	using System.Net.Http;
	using System.Reflection;
	using System.Runtime.Serialization;
	using Nexus.Client.ModManagement;
	using Nexus.Client.Util.Collections;
	using System.Threading;
	using System.Threading.Tasks;
	using Nexus.Client;
	using Nexus.Client.ModRepositories;
	using Nexus.Client.Mods;
	using Nexus.Client.OnlineServices.Infrastructure;
	using Nexus.Client.OnlineServices.NexusMods;
	using Nexus.Client.Settings;
	using NUnit.Framework;

	/// <summary>
	/// Verifies request coalescing, bulk parent metadata, freshness reuse, and bounded REST file resolution in the Nexus update-check path.
	/// </summary>
	[TestFixture]
	[Category("CollectionsC12Concurrency")]
	public class NexusRepositoryRequestCoalescingTests
	{
		/// <summary>
		/// Ensures several local archives from one Nexus mod share parent metadata and the file-list response.
		/// </summary>
		[Test]
		public void GetFileListInfo_SharedModFetchesParentAndFileListOnce()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|42|200|new.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual("200", results[0].DownloadId);
				Assert.AreEqual("2.0", results[0].HumanReadableVersion);
				Assert.AreEqual("200", results[1].DownloadId);
				Assert.AreEqual("2.0", results[1].HumanReadableVersion);
				Assert.AreNotSame(results[0], results[1]);

				string secondName = results[1].ModName;
				((ModInfo)results[0]).ModName = "Changed locally";
				Assert.AreEqual(secondName, results[1].ModName);

				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a successor file missing from the shared list is fetched once and reused by duplicate rows.
		/// </summary>
		[Test]
		public void GetFileListInfo_SharedMissingSuccessorFetchesSuccessorOnce()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", MissingSuccessorFileListFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files/300.json", Successor300Fixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|42|100|old-copy.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual("300", results[0].DownloadId);
				Assert.AreEqual("3.0", results[0].HumanReadableVersion);
				Assert.AreEqual("300", results[1].DownloadId);
				Assert.AreEqual("3.0", results[1].HumanReadableVersion);

				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files/300.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures successor coalescing remains keyed by the resolved file identity rather than only by mod ID.
		/// </summary>
		[Test]
		public void GetFileListInfo_DifferentSuccessorsFetchEachUniqueSuccessorOnce()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", TwoMissingSuccessorsFileListFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files/300.json", Successor300Fixture);
			router.AddOk("/v1/games/skyrim/mods/42/files/400.json", Successor400Fixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|42|200|other-old.zip",
					"Third|42|100|old-copy.zip"
				});

				Assert.AreEqual(3, results.Count);
				Assert.AreEqual("300", results[0].DownloadId);
				Assert.AreEqual("400", results[1].DownloadId);
				Assert.AreEqual("300", results[2].DownloadId);

				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files/300.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files/400.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures malformed packed metadata retains one empty result in its original position without defeating later reuse.
		/// </summary>
		[Test]
		public void GetFileListInfo_MalformedRowPreservesResultAlignment()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Malformed|42",
					"Third|42|200|new.zip"
				});

				Assert.AreEqual(3, results.Count);
				Assert.AreEqual("200", results[0].DownloadId);
				Assert.IsTrue(string.IsNullOrEmpty(results[1].Id));
				Assert.IsTrue(string.IsNullOrEmpty(results[1].DownloadId));
				Assert.AreEqual("200", results[2].DownloadId);

				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures filename-based file identity continues to be resolved independently from one shared file list.
		/// </summary>
		[Test]
		public void GetFileListInfo_FilenameFallbackUsesSharedFileListPerRow()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|0|old.zip",
					"Second|42|0|new.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual("200", results[0].DownloadId);
				Assert.AreEqual("200", results[1].DownloadId);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a non-rate-limit provider failure is retained as the shared operation result rather than retried for duplicates.
		/// </summary>
		[Test]
		public void GetFileListInfo_SharedParentFailureIsNotRetriedForDuplicates()
		{
			var router = new RequestRouter();
			router.Add("/v1/games/skyrim/mods/42.json", HttpStatusCode.InternalServerError, "{\"message\":\"fixture failure\"}");

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|42|200|new.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.IsTrue(string.IsNullOrEmpty(results[0].Id));
				Assert.IsTrue(string.IsNullOrEmpty(results[1].Id));
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a rate-limit failure still stops the operation at the same row boundary as the legacy path.
		/// </summary>
		[Test]
		public void GetFileListInfo_RateLimitStillStopsFurtherRows()
		{
			var router = new RequestRouter();
			router.Add("/v1/games/skyrim/mods/42.json", (HttpStatusCode)429, "{\"message\":\"rate limit\"}");
			router.AddOk("/v1/games/skyrim/mods/43.json", ParentMod43Fixture);
			router.AddOk("/v1/games/skyrim/mods/43/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|43|200|new.zip"
				});

				Assert.AreEqual(1, results.Count);
				Assert.IsTrue(string.IsNullOrEmpty(results[0].Id));
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/43.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/43/files.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures complete GraphQL parent metadata removes the REST v1 parent request while file resolution stays on v1.
		/// </summary>
		[Test]
		public void GetFileListInfo_GraphQlParentAvoidsV1ParentRequest()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParent42Fixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|42|200|new.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreNotSame(results[0], results[1]);
				Assert.AreEqual("GraphQL Author", results[0].Author);
				Assert.AreEqual(8, results[0].CategoryId);
				Assert.AreEqual(true, results[0].IsEndorsed);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures several unique parent identities are resolved by one bounded GraphQL request while each mod keeps its own v1 file list.
		/// </summary>
		[Test]
		public void GetFileListInfo_GraphQlBatchesSeveralUniqueParents()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParents42And43Fixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
			router.AddOk("/v1/games/skyrim/mods/43/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|43|100|old.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual("42", results[0].Id);
				Assert.AreEqual("43", results[1].Id);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/43.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/43/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a missing identity in an otherwise valid GraphQL batch falls back only that identity to REST v1.
		/// </summary>
		[Test]
		public void GetFileListInfo_MissingGraphQlIdentityFallsBackOnlyThatParent()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParent42Fixture);
			router.AddOk("/v1/games/skyrim/mods/43.json", ParentMod43Fixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
			router.AddOk("/v1/games/skyrim/mods/43/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|43|100|old.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual(8, results[0].CategoryId);
				Assert.AreEqual(7, results[1].CategoryId);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/43.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures GraphQL parent data missing a required category does not change repository semantics and uses v1 fallback.
		/// </summary>
		[Test]
		public void GetFileListInfo_IncompleteGraphQlParentFallsBackToV1()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlIncompleteParent42Fixture);
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string> { "First|42|100|old.zip" });

				Assert.AreEqual(1, results.Count);
				Assert.AreEqual("Fixture Author", results[0].Author);
				Assert.AreEqual(7, results[0].CategoryId);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a field-level GraphQL error invalidates only the affected node and preserves REST v1 parity.
		/// </summary>
		[Test]
		public void GetFileListInfo_GraphQlRelevantFieldErrorFallsBackOnlyAffectedNode()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParents42And43WithViewerErrorOn42Fixture);
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
			router.AddOk("/v1/games/skyrim/mods/43/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|43|100|old.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual(7, results[0].CategoryId);
				Assert.AreEqual(true, results[0].IsEndorsed);
				Assert.AreEqual(9, results[1].CategoryId);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/43.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a GraphQL schema/validation failure degrades to the existing REST v1 parent path.
		/// </summary>
		[Test]
		public void GetFileListInfo_GraphQlSchemaFailureFallsBackToV1()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlSchemaErrorFixture);
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string> { "First|42|100|old.zip" });

				Assert.AreEqual(1, results.Count);
				Assert.AreEqual(7, results[0].CategoryId);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures GraphQL false/abstained endorsement state retains the existing nullable NMM repository semantics.
		/// </summary>
		[Test]
		public void GetFileListInfo_GraphQlAbstainedEndorsementMapsToNull()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParent42AbstainedFixture);
			router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string> { "First|42|100|old.zip" });

				Assert.AreEqual(1, results.Count);
				Assert.IsNull(results[0].IsEndorsed);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures the client-side GraphQL batch size stays bounded without falling back to one REST parent request per mod.
		/// </summary>
		[Test]
		public void GetFileListInfo_MoreThanOneGraphQlBatchUsesBoundedRequests()
		{
			var router = new RequestRouter();
			router.AddGraphQlEcho();
			var requests = new List<string>();
			for (int modId = 1; modId <= 51; modId++)
			{
				requests.Add(string.Format("Mod {0}|{0}|100|old.zip", modId));
				router.AddOk(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), CompleteFileListFixture);
			}

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(requests);

				Assert.AreEqual(51, results.Count);
				router.AssertCount("/v2/graphql", 2);
				router.AssertCount("/v1/games/skyrim/mods/1.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/51.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures independent REST file-list requests overlap behind the fixed bound while result rows remain in caller order.
		/// </summary>
		[Test]
		public void GetFileListInfo_FileListsUseBoundedConcurrencyAndPreserveOrder()
		{
			var router = new RequestRouter();
			router.AddGraphQlEcho();
			var requests = new List<string>();
			int activeRequests = 0;
			int peakConcurrency = 0;

			for (int modId = 1; modId <= 8; modId++)
			{
				int capturedModId = modId;
				requests.Add(string.Format("Mod {0}|{0}|100|old.zip", modId));
				router.AddAsyncHandler(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), async (request, cancellationToken) =>
				{
					int active = Interlocked.Increment(ref activeRequests);
					UpdatePeak(ref peakConcurrency, active);
					try
					{
						await Task.Delay(TimeSpan.FromMilliseconds(15 + ((9 - capturedModId) * 4)), cancellationToken).ConfigureAwait(false);
						return new ResponseFixture(HttpStatusCode.OK, CompleteFileListFixture);
					}
					finally
					{
						Interlocked.Decrement(ref activeRequests);
					}
				});
			}

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(requests);

				Assert.AreEqual(8, results.Count);
				for (int index = 0; index < results.Count; index++)
					Assert.AreEqual((index + 1).ToString(), results[index].Id);
				Assert.GreaterOrEqual(peakConcurrency, 2, "File-list requests unexpectedly remained serial.");
				Assert.LessOrEqual(peakConcurrency, 4, "File-list concurrency exceeded the repository bound.");
				for (int modId = 1; modId <= 8; modId++)
					router.AssertCount(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a rate-limit response stops dispatching queued file-list work and retains the legacy row boundary.
		/// </summary>
		[Test]
		public void GetFileListInfo_BoundedFileListRateLimitStopsQueuedRows()
		{
			var router = new RequestRouter();
			router.AddGraphQlEcho();
			router.AddAsyncHandler("/v1/games/skyrim/mods/1/files.json", async (request, cancellationToken) =>
			{
				await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
				return new ResponseFixture(HttpStatusCode.OK, CompleteFileListFixture);
			});
			router.Add("/v1/games/skyrim/mods/2/files.json", (HttpStatusCode)429, "{\"message\":\"rate limit\"}");
			for (int modId = 3; modId <= 4; modId++)
			{
				router.AddAsyncHandler(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), async (request, cancellationToken) =>
				{
					await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
					return new ResponseFixture(HttpStatusCode.OK, CompleteFileListFixture);
				});
			}
			for (int modId = 5; modId <= 8; modId++)
				router.AddOk(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), CompleteFileListFixture);

			var requests = Enumerable.Range(1, 8)
				.Select(modId => string.Format("Mod {0}|{0}|100|old.zip", modId))
				.ToList();

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(requests);

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual("1", results[0].Id);
				Assert.IsTrue(string.IsNullOrEmpty(results[1].Id));
				for (int modId = 1; modId <= 4; modId++)
					router.AssertCount(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), 1);
				for (int modId = 5; modId <= 8; modId++)
					router.AssertCount(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a rate-limited successor lookup also stops dispatching later mod-resolution workers.
		/// </summary>
		[Test]
		public void GetFileListInfo_BoundedSuccessorRateLimitStopsQueuedMods()
		{
			var router = new RequestRouter();
			router.AddGraphQlEcho();
			router.AddOk("/v1/games/skyrim/mods/1/files.json", MissingSuccessorFileListFixture);
			router.Add("/v1/games/skyrim/mods/1/files/300.json", (HttpStatusCode)429, "{\"message\":\"rate limit\"}");
			for (int modId = 2; modId <= 4; modId++)
			{
				router.AddAsyncHandler(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), async (request, cancellationToken) =>
				{
					await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
					return new ResponseFixture(HttpStatusCode.OK, CompleteFileListFixture);
				});
			}
			for (int modId = 5; modId <= 6; modId++)
				router.AddOk(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), CompleteFileListFixture);

			var requests = Enumerable.Range(1, 6)
				.Select(modId => string.Format("Mod {0}|{0}|100|old.zip", modId))
				.ToList();

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(requests);

				Assert.AreEqual(1, results.Count);
				Assert.IsTrue(string.IsNullOrEmpty(results[0].Id));
				router.AssertCount("/v1/games/skyrim/mods/1/files/300.json", 1);
				for (int modId = 1; modId <= 4; modId++)
					router.AssertCount(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), 1);
				for (int modId = 5; modId <= 6; modId++)
					router.AssertCount(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures REST parent fallback remains a barrier so GraphQL failure does not reorder later provider work.
		/// </summary>
		[Test]
		public void GetFileListInfo_RestParentFallbackRemainsAFileResolutionBarrier()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);
			router.AddOk("/v1/games/skyrim/mods/43.json", ParentMod43Fixture);
			int activeRequests = 0;
			int peakConcurrency = 0;
			foreach (int modId in new[] { 42, 43 })
			{
				router.AddAsyncHandler(string.Format("/v1/games/skyrim/mods/{0}/files.json", modId), async (request, cancellationToken) =>
				{
					int active = Interlocked.Increment(ref activeRequests);
					UpdatePeak(ref peakConcurrency, active);
					try
					{
						await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
						return new ResponseFixture(HttpStatusCode.OK, CompleteFileListFixture);
					}
					finally
					{
						Interlocked.Decrement(ref activeRequests);
					}
				});
			}

			using (RepositoryContext context = CreateContext(router))
			{
				List<IModInfo> results = context.Repository.GetFileListInfo(new List<string>
				{
					"First|42|100|old.zip",
					"Second|43|100|old.zip"
				});

				Assert.AreEqual(2, results.Count);
				Assert.AreEqual(1, peakConcurrency);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/43.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/43/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures category refresh uses one bounded GraphQL parent lookup and never enters REST file-list resolution.
		/// </summary>
		[Test]
		public void GetModCategoryIds_GraphQlUsesCategoryMetadataWithoutFileRequests()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParents42And43Fixture);

			using (RepositoryContext context = CreateContext(router))
			{
				Dictionary<string, int> categories = context.Repository.GetModCategoryIds(new[] { "42", "43", "42" });

				Assert.AreEqual(2, categories.Count);
				Assert.AreEqual(8, categories["42"]);
				Assert.AreEqual(9, categories["43"]);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/43.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/43/files.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures a missing category in GraphQL falls back only to the REST parent endpoint and still never fetches file metadata.
		/// </summary>
		[Test]
		public void GetModCategoryIds_IncompleteGraphQlCategoryFallsBackToV1ParentOnly()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlIncompleteParent42Fixture);
			router.AddOk("/v1/games/skyrim/mods/42.json", ParentModFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				Dictionary<string, int> categories = context.Repository.GetModCategoryIds(new[] { "42" });

				Assert.AreEqual(1, categories.Count);
				Assert.AreEqual(7, categories["42"]);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 1);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/42/files/100.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures invalid/sentinel identities are ignored before the category bulk resolver is called.
		/// </summary>
		[Test]
		public void GetModCategoryIds_IgnoresInvalidAndDeduplicatesNumericIds()
		{
			var router = new RequestRouter();
			router.AddOk("/v2/graphql", GraphQlParent42Fixture);

			using (RepositoryContext context = CreateContext(router))
			{
				Dictionary<string, int> categories = context.Repository.GetModCategoryIds(new[] { null, string.Empty, "0", "-1", "bad", "042", "42" });

				Assert.AreEqual(1, categories.Count);
				Assert.AreEqual(8, categories["42"]);
				router.AssertCount("/v2/graphql", 1);
				router.AssertCount("/v1/games/skyrim/mods/42.json", 0);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures repository update metadata preserves both provider freshness timestamps instead of collapsing to IDs.
		/// </summary>
		[Test]
		public void GetUpdatedWithMetadata_PreservesProviderFreshnessTimestamps()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/updated.json?period=1m", UpdatedModsFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<RepositoryModUpdate> updates = context.Repository.GetUpdatedWithMetadata("1m");

				Assert.AreEqual(2, updates.Count);
				Assert.AreEqual("42", updates[0].ModId);
				Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000), updates[0].LatestFileUpdateUtc);
				Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000300), updates[0].LatestModActivityUtc);
				Assert.AreEqual("43", updates[1].ModId);
				Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1710000000), updates[1].LatestFileUpdateUtc);
				Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1710000600), updates[1].LatestModActivityUtc);
				router.AssertCount("/v1/games/skyrim/mods/updated.json?period=1m", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures the legacy updated-mod operation remains a compatibility projection of the richer repository records.
		/// </summary>
		[Test]
		public void GetUpdated_LegacyOperationStillProjectsModIds()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/updated.json?period=1w", UpdatedModsFixture);

			using (RepositoryContext context = CreateContext(router))
			{
				List<string> updates = context.Repository.GetUpdated("1w");

				CollectionAssert.AreEqual(new[] { "42", "43" }, updates);
				router.AssertCount("/v1/games/skyrim/mods/updated.json?period=1w", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Ensures fetching a candidate alone never advances freshness state before local metadata application commits it.
		/// </summary>
		[Test]
		public void GetFileListInfoWithFreshness_UncommittedCandidateDoesNotSkipNextFetch()
		{
			string installInfoDirectory = CreateTemporaryDirectory();
			try
			{
				var router = new RequestRouter();
				router.AddOk("/v2/graphql", GraphQlParent42Fixture);
				router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				var updates = new[] { CreateProviderUpdate("42", 1700000000) };

				using (RepositoryContext context = CreateContext(router, installInfoDirectory))
				{
					RepositoryFileListInfoResult first = context.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);
					RepositoryFileListInfoResult second = context.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);

					Assert.AreEqual(1, first.Checkpoints.Count);
					Assert.IsNotNull(first.Checkpoints[0]);
					Assert.AreEqual(1, second.Checkpoints.Count);
					Assert.IsNotNull(second.Checkpoints[0]);
					router.AssertCount("/v1/games/skyrim/mods/42/files.json", 2);
					router.AssertNoUnexpectedRequests();
				}
			}
			finally
			{
				DeleteDirectoryQuietly(installInfoDirectory);
			}
		}

		/// <summary>
		/// Ensures an explicitly committed unchanged file timestamp reuses file resolution across repository instances while parent metadata still refreshes.
		/// </summary>
		[Test]
		public void GetFileListInfoWithFreshness_CommittedCheckpointSkipsRepeatedFileListAcrossInstances()
		{
			string installInfoDirectory = CreateTemporaryDirectory();
			var updates = new[] { CreateProviderUpdate("42", 1700000000) };
			try
			{
				var firstRouter = new RequestRouter();
				firstRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				using (RepositoryContext firstContext = CreateContext(firstRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult first = firstContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);
					Assert.AreEqual("200", first.ModInfo[0].DownloadId);
					Assert.IsTrue(firstContext.Repository.CommitFileUpdateCheckpoints(first.Checkpoints));
					firstRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
					firstRouter.AssertNoUnexpectedRequests();
				}

				var secondRouter = new RequestRouter();
				secondRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				using (RepositoryContext secondContext = CreateContext(secondRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult second = secondContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);

					Assert.AreEqual("200", second.ModInfo[0].DownloadId);
					Assert.AreEqual("new.zip", second.ModInfo[0].FileName);
					Assert.AreEqual("2.0", second.ModInfo[0].HumanReadableVersion);
					Assert.AreEqual("GraphQL Mod 42 - New File", second.ModInfo[0].ModName);
					Assert.AreEqual("GraphQL Author", second.ModInfo[0].Author);
					secondRouter.AssertCount("/v2/graphql", 1);
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
					secondRouter.AssertNoUnexpectedRequests();
				}
			}
			finally
			{
				DeleteDirectoryQuietly(installInfoDirectory);
			}
		}

		/// <summary>
		/// Ensures a changed provider file timestamp invalidates the previous resolution and performs a new REST file-list request.
		/// </summary>
		[Test]
		public void GetFileListInfoWithFreshness_ChangedTimestampRefetchesFileList()
		{
			string installInfoDirectory = CreateTemporaryDirectory();
			try
			{
				var firstRouter = new RequestRouter();
				firstRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				using (RepositoryContext firstContext = CreateContext(firstRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult first = firstContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, new[] { CreateProviderUpdate("42", 1700000000) });
					Assert.IsTrue(firstContext.Repository.CommitFileUpdateCheckpoints(first.Checkpoints));
				}

				var secondRouter = new RequestRouter();
				secondRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				secondRouter.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				using (RepositoryContext secondContext = CreateContext(secondRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult second = secondContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, new[] { CreateProviderUpdate("42", 1700000001) });

					Assert.AreEqual("200", second.ModInfo[0].DownloadId);
					Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000001), second.Checkpoints[0].LatestFileUpdateUtc);
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
					secondRouter.AssertNoUnexpectedRequests();
				}
			}
			finally
			{
				DeleteDirectoryQuietly(installInfoDirectory);
			}
		}

		/// <summary>
		/// Ensures a local archive whose DownloadId advanced to the resolved successor still matches the prior unchanged checkpoint.
		/// </summary>
		[Test]
		public void GetFileListInfoWithFreshness_ResolvedDownloadIdMatchesCommittedCheckpoint()
		{
			string installInfoDirectory = CreateTemporaryDirectory();
			var updates = new[] { CreateProviderUpdate("42", 1700000000) };
			try
			{
				var firstRouter = new RequestRouter();
				firstRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				using (RepositoryContext firstContext = CreateContext(firstRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult first = firstContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);
					Assert.IsTrue(firstContext.Repository.CommitFileUpdateCheckpoints(first.Checkpoints));
				}

				var secondRouter = new RequestRouter();
				secondRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				using (RepositoryContext secondContext = CreateContext(secondRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult second = secondContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|200|old.zip" }, updates);

					Assert.AreEqual("200", second.ModInfo[0].DownloadId);
					Assert.AreEqual("2.0", second.ModInfo[0].HumanReadableVersion);
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
					secondRouter.AssertNoUnexpectedRequests();
				}
			}
			finally
			{
				DeleteDirectoryQuietly(installInfoDirectory);
			}
		}

		/// <summary>
		/// Ensures a committed individually fetched successor also avoids both file-list and successor REST calls on an unchanged timestamp.
		/// </summary>
		[Test]
		public void GetFileListInfoWithFreshness_CachedSuccessorAvoidsAllRepeatedFileResolutionRequests()
		{
			string installInfoDirectory = CreateTemporaryDirectory();
			var updates = new[] { CreateProviderUpdate("42", 1700000000) };
			try
			{
				var firstRouter = new RequestRouter();
				firstRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files.json", MissingSuccessorFileListFixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files/300.json", Successor300Fixture);
				using (RepositoryContext firstContext = CreateContext(firstRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult first = firstContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);
					Assert.AreEqual("300", first.ModInfo[0].DownloadId);
					Assert.IsTrue(firstContext.Repository.CommitFileUpdateCheckpoints(first.Checkpoints));
				}

				var secondRouter = new RequestRouter();
				secondRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				using (RepositoryContext secondContext = CreateContext(secondRouter, installInfoDirectory))
				{
					RepositoryFileListInfoResult second = secondContext.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, updates);

					Assert.AreEqual("300", second.ModInfo[0].DownloadId);
					Assert.AreEqual("successor.zip", second.ModInfo[0].FileName);
					Assert.AreEqual("3.0", second.ModInfo[0].HumanReadableVersion);
					Assert.AreEqual("GraphQL Mod 42 - Successor File", second.ModInfo[0].ModName);
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files/300.json", 0);
					secondRouter.AssertNoUnexpectedRequests();
				}
			}
			finally
			{
				DeleteDirectoryQuietly(installInfoDirectory);
			}
		}

		/// <summary>
		/// Ensures corrupt derived checkpoint data is ignored and rebuilt from Nexus rather than breaking update checks.
		/// </summary>
		[Test]
		public void GetFileListInfoWithFreshness_CorruptCheckpointFileFallsBackToProvider()
		{
			string installInfoDirectory = CreateTemporaryDirectory();
			try
			{
				File.WriteAllText(Path.Combine(installInfoDirectory, "NexusFileUpdateCheckpoints.json"), "not-json");
				var router = new RequestRouter();
				router.AddOk("/v2/graphql", GraphQlParent42Fixture);
				router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);

				using (RepositoryContext context = CreateContext(router, installInfoDirectory))
				{
					RepositoryFileListInfoResult result = context.Repository.GetFileListInfoWithFreshness(
						new List<string> { "First|42|100|old.zip" }, new[] { CreateProviderUpdate("42", 1700000000) });

					Assert.AreEqual("200", result.ModInfo[0].DownloadId);
					Assert.IsNotNull(result.Checkpoints[0]);
					router.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
					router.AssertNoUnexpectedRequests();
				}
			}
			finally
			{
				DeleteDirectoryQuietly(installInfoDirectory);
			}
		}

		/// <summary>
		/// Checks old files even when the provider's monthly update list contains no matching mod.
		/// Reopening the repository reuses only committed, recently verified results without advancing their age.
		/// </summary>
		[Test]
		public void FullCheck_OldUpdateIsFoundAndCommittedResultSurvivesRestart()
		{
			string directory = CreateTemporaryDirectory();
			try
			{
				var firstRouter = new RequestRouter();
				firstRouter.AddOk("/v1/games/skyrim/mods/updated.json?period=1m", "[]");
				firstRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				DateTimeOffset verified;
				using (RepositoryContext context = CreateContext(firstRouter, directory))
				{
					RepositoryFileListInfoResult first = context.Repository.GetFullFileListInfo(
						new List<string> { "First|42|100|old.zip" }, null);
					Assert.AreEqual("200", first.ModInfo[0].DownloadId);
					Assert.AreEqual("2.0", first.ModInfo[0].HumanReadableVersion);
					verified = first.Checkpoints[0].VerifiedUtc;
					Assert.Greater(verified, DateTimeOffset.UtcNow.AddMinutes(-10));
					Assert.IsFalse(File.Exists(Path.Combine(directory, "NexusFileUpdateCheckpoints.json")));
					// Fetching the candidate alone must not make a later Full check reuse it.
					context.Repository.GetFullFileListInfo(new List<string> { "First|42|100|old.zip" }, null);
					firstRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 2);
					Assert.IsTrue(context.Repository.CommitFileUpdateCheckpoints(first.Checkpoints));
					firstRouter.AssertNoUnexpectedRequests();
				}

				var secondRouter = new RequestRouter();
				secondRouter.AddOk("/v1/games/skyrim/mods/updated.json?period=1d", "[]");
				secondRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				using (RepositoryContext context = CreateContext(secondRouter, directory))
				{
					RepositoryFileListInfoResult second = context.Repository.GetFullFileListInfo(
						new List<string> { "First|42|200|old.zip" }, null);
					Assert.AreEqual("200", second.ModInfo[0].DownloadId);
					Assert.AreEqual("2.0", second.ModInfo[0].HumanReadableVersion);
					Assert.AreEqual("GraphQL Author", second.ModInfo[0].Author);
					Assert.AreEqual(verified, second.Checkpoints[0].VerifiedUtc);
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
					secondRouter.AssertNoUnexpectedRequests();
				}
			}
			finally { DeleteDirectoryQuietly(directory); }
		}

		/// <summary>
		/// Uses the smallest safe window, rechecking stale, future-dated and legacy entries rather than guessing freshness.
		/// </summary>
		[TestCase(-0.5, "1d", 0)]
		[TestCase(-2, "1w", 0)]
		[TestCase(-10, "1m", 0)]
		[TestCase(-28.1, "1m", 1)]
		[TestCase(-365, "1m", 1)]
		[TestCase(1, "1m", 1)]
		[TestCase(-100000, "1m", 1)]
		public void FullCheck_SelectsSafeCoverageAndRefetchesStaleEntries(double ageDays, string period, int fileRequests)
		{
			string directory = CreateTemporaryDirectory();
			try
			{
				var router = new RequestRouter();
				router.AddOk("/v1/games/skyrim/mods/updated.json?period=" + period, "[]");
				router.AddOk("/v2/graphql", GraphQlParent42Fixture);
				router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				using (RepositoryContext context = CreateContext(router, directory))
				{
					DateTimeOffset verified = ageDays == -100000 ? default(DateTimeOffset) : DateTimeOffset.UtcNow.AddDays(ageDays);
					context.Repository.CommitFileUpdateCheckpoints(new[] { CreateVerifiedCheckpoint(verified) });
					RepositoryFileListInfoResult result = context.Repository.GetFullFileListInfo(
						new List<string> { "First|42|100|old.zip" }, null);
					Assert.AreEqual("200", result.ModInfo[0].DownloadId);
					router.AssertCount("/v1/games/skyrim/mods/42/files.json", fileRequests);
					router.AssertNoUnexpectedRequests();
				}
			}
			finally { DeleteDirectoryQuietly(directory); }
		}

		/// <summary>
		/// File freshness controls reuse independently of parent activity; changed archive identities always refetch.
		/// </summary>
		[TestCase(1700000000L, "100", "old.zip", 0)]
		[TestCase(1700000001L, "100", "old.zip", 1)]
		[TestCase(1700000000L, "100", "renamed.zip", 1)]
		[TestCase(1700000000L, "999", "old.zip", 1)]
		public void FullCheck_RequiresMatchingFileTimestampAndArchiveIdentity(long timestamp, string fileId, string filename, int fileRequests)
		{
			string directory = CreateTemporaryDirectory();
			try
			{
				var router = new RequestRouter();
				router.AddOk("/v1/games/skyrim/mods/updated.json?period=1d",
					"[{\"mod_id\":42,\"latest_file_update\":" + timestamp + ",\"latest_mod_activity\":1800000000}]");
				router.AddOk("/v1/games/skyrim/mods/updated.json?period=1m",
					"[{\"mod_id\":42,\"latest_file_update\":" + timestamp + ",\"latest_mod_activity\":1800000000}]");
				router.AddOk("/v2/graphql", GraphQlParent42Fixture);
				router.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				// No successor exists for 999; an unresolved result must not become a verified entry.
				if (fileId == "999")
					router.AddOk("/v1/games/skyrim/mods/42/files/999.json", "null");
				using (RepositoryContext context = CreateContext(router, directory))
				{
					context.Repository.CommitFileUpdateCheckpoints(new[] { CreateVerifiedCheckpoint(DateTimeOffset.UtcNow.AddHours(-1)) });
					RepositoryFileListInfoResult result = context.Repository.GetFullFileListInfo(
						new List<string> { "First|42|" + fileId + "|" + filename }, null);
					Assert.AreEqual(1, result.ModInfo.Count);
					if (fileId == "999")
						Assert.IsFalse(result.Checkpoints[0]?.FileMetadataResolved == true);
					router.AssertCount("/v1/games/skyrim/mods/42/files.json", fileRequests);
					router.AssertNoUnexpectedRequests();
				}
			}
			finally { DeleteDirectoryQuietly(directory); }
		}

		/// <summary>
		/// Prevents failed, null and malformed recent-update responses from authorizing cache reuse or a request storm.
		/// </summary>
		[TestCase(401, "{}")]
		[TestCase(429, "{}")]
		[TestCase(503, "{}")]
		[TestCase(200, "null")]
		[TestCase(200, "[{}]")]
		public void FullCheck_InvalidCoverageStopsBeforeMetadataAndFiles(int status, string body)
		{
			var router = new RequestRouter();
			router.Add("/v1/games/skyrim/mods/updated.json?period=1m", (HttpStatusCode)status, body);
			using (RepositoryContext context = CreateContext(router))
			{
				Assert.Catch(() => context.Repository.GetFullFileListInfo(new List<string> { "First|42|100|old.zip" }, null));
				router.AssertCount("/v2/graphql", 0);
				router.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Shares file responses for different archives and preserves the bounded GraphQL batch size during Full checks.
		/// </summary>
		[Test]
		public void FullCheck_BatchesParentsAndSharesFileLists()
		{
			var router = new RequestRouter();
			router.AddOk("/v1/games/skyrim/mods/updated.json?period=1m", "[]");
			router.AddGraphQlEcho();
			var rows = new List<string>();
			for (int id = 1; id <= 55; id++)
			{
				rows.Add("First|" + id + "|100|old.zip");
				rows.Add("Second|" + id + "|200|new.zip");
				router.AddOk("/v1/games/skyrim/mods/" + id + "/files.json", CompleteFileListFixture);
			}
			using (RepositoryContext context = CreateContext(router))
			{
				RepositoryFileListInfoResult result = context.Repository.GetFullFileListInfo(rows, null);
				Assert.AreEqual(110, result.ModInfo.Count);
				Assert.IsTrue(result.ModInfo.All(info => info.DownloadId == "200"));
				router.AssertCount("/v2/graphql", 2);
				for (int id = 1; id <= 55; id++)
					router.AssertCount("/v1/games/skyrim/mods/" + id + "/files.json", 1);
				router.AssertNoUnexpectedRequests();
			}
		}

		/// <summary>
		/// Retains sibling archive results when one has a provider timestamp and another is verified outside the recent list.
		/// </summary>
		[Test]
		public void FullCheck_ArchiveWithoutProviderTimestampDoesNotEvictVerifiedSibling()
		{
			string directory = CreateTemporaryDirectory();
			try
			{
				var rows = new List<string> { "First|42|100|old.zip", "Second|42|200|copy.zip" };
				var firstRouter = new RequestRouter();
				firstRouter.AddOk("/v1/games/skyrim/mods/updated.json?period=1m", "[]");
				firstRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				firstRouter.AddOk("/v1/games/skyrim/mods/42/files.json", CompleteFileListFixture);
				using (RepositoryContext context = CreateContext(firstRouter, directory))
				{
					context.Repository.CommitFileUpdateCheckpoints(new[] { CreateVerifiedCheckpoint(DateTimeOffset.UtcNow.AddHours(-1)) });
					RepositoryFileListInfoResult first = context.Repository.GetFullFileListInfo(rows, null);
					Assert.AreNotEqual(default(DateTimeOffset), first.Checkpoints[0].LatestFileUpdateUtc);
					Assert.AreEqual(default(DateTimeOffset), first.Checkpoints[1].LatestFileUpdateUtc);
					Assert.IsTrue(context.Repository.CommitFileUpdateCheckpoints(first.Checkpoints));
					firstRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 1);
					firstRouter.AssertNoUnexpectedRequests();
				}
				var secondRouter = new RequestRouter();
				secondRouter.AddOk("/v1/games/skyrim/mods/updated.json?period=1d", "[]");
				secondRouter.AddOk("/v2/graphql", GraphQlParent42Fixture);
				using (RepositoryContext context = CreateContext(secondRouter, directory))
				{
					RepositoryFileListInfoResult second = context.Repository.GetFullFileListInfo(rows, null);
					Assert.IsTrue(second.ModInfo.All(info => info.DownloadId == "200"));
					secondRouter.AssertCount("/v1/games/skyrim/mods/42/files.json", 0);
					secondRouter.AssertNoUnexpectedRequests();
				}
			}
			finally { DeleteDirectoryQuietly(directory); }
		}

		/// <summary>
		/// Stops dispatching queued file work once cancellation is observed, without publishing any new verification.
		/// </summary>
		[Test]
		public void FullCheck_CancellationStopsSchedulingAndDoesNotCommit()
		{
			string directory = CreateTemporaryDirectory();
			try
			{
				bool cancelled = false;
				var router = new RequestRouter();
				router.AddOk("/v1/games/skyrim/mods/updated.json?period=1m", "[]");
				router.AddGraphQlEcho();
				router.AddHandler("/v1/games/skyrim/mods/1/files.json", request =>
				{
					cancelled = true;
					return new ResponseFixture(HttpStatusCode.OK, CompleteFileListFixture);
				});
				using (RepositoryContext context = CreateContext(router, directory))
				{
					context.Repository.GetFullFileListInfo(Enumerable.Range(1, 10).Select(id => "First|" + id + "|100|old.zip").ToList(), () => cancelled);
					router.AssertCount("/v1/games/skyrim/mods/1/files.json", 1);
					router.AssertCount("/v1/games/skyrim/mods/2/files.json", 0);
					Assert.IsFalse(File.Exists(Path.Combine(directory, "NexusFileUpdateCheckpoints.json")));
					router.AssertNoUnexpectedRequests();
				}
			}
			finally { DeleteDirectoryQuietly(directory); }
		}

		/// <summary>
		/// Full selects every eligible ID without hashing archives; period checks retain their existing filter.
		/// </summary>
		[TestCase(true, 2)]
		[TestCase(false, 1)]
		public void UpdateTask_FullScopeRespectsDisabledAndInvalidIds(bool full, int expected)
		{
			List<string> rows = null;
			IModRepository repository = InterfaceStub<IModRepository>.Create((method, args) =>
			{
				if (method.Name == "GetUpdatedWithMetadata" && !full)
					return new List<RepositoryModUpdate> { CreateProviderUpdate("42", 1700000000) };
				if (method.Name == (full ? "GetFullFileListInfo" : "GetFileListInfoWithFreshness"))
				{
					rows = (List<string>)args[0];
					return new RepositoryFileListInfoResult(rows.Select(row => (IModInfo)new ModInfo()).ToList(),
						rows.Select(row => (RepositoryFileUpdateCheckpoint)null).ToList());
				}
				throw new InvalidOperationException("Unexpected task repository call: " + method.Name);
			});
			new SynchronousUpdateTask(null, repository, new[]
			{
				CreateTaskMod("42"), CreateTaskMod("43"), CreateTaskMod("44", false), CreateTaskMod("0"), CreateTaskMod("local")
			}, full).Run();
			Assert.IsNotNull(rows);
			Assert.AreEqual(expected, rows.Count);
			Assert.IsTrue(rows.Any(row => row.Contains("|42|")));
			Assert.AreEqual(full, rows.Any(row => row.Contains("|43|")));
		}

		/// <summary>
		/// Publishes only successfully applied rows, preserves custom categories, and never commits a cancelled run.
		/// </summary>
		[TestCase(false)]
		[TestCase(true)]
		public void UpdateTask_FullCommitsOnlyAppliedResultsUnlessCancelled(bool cancel)
		{
			List<RepositoryFileUpdateCheckpoint> committed = null;
			IModInfo applied = null;
			var updater = (AutoUpdater)FormatterServices.GetUninitializedObject(typeof(AutoUpdater));
			typeof(AutoUpdater).GetField("_newInfo", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(updater, new ThreadSafeObservableList<AutoUpdater.UpdateInfo>());
			SynchronousUpdateTask task = null;
			IModRepository repository = InterfaceStub<IModRepository>.Create((method, args) =>
			{
				if (method.Name == "GetFullFileListInfo")
				{
					if (cancel) task.Cancel();
					return new RepositoryFileListInfoResult(new List<IModInfo>
					{
						new ModInfo { Id = "42", DownloadId = "200", HumanReadableVersion = "2.0" }, new ModInfo()
					}, new List<RepositoryFileUpdateCheckpoint> { CreateVerifiedCheckpoint(DateTimeOffset.UtcNow.AddMinutes(-5)), null });
				}
				if (method.Name == "CommitFileUpdateCheckpoints")
				{
					committed = ((IEnumerable<RepositoryFileUpdateCheckpoint>)args[0]).ToList();
					return true;
				}
				throw new InvalidOperationException("Unexpected task repository call: " + method.Name);
			});
			task = new SynchronousUpdateTask(updater, repository,
				new[] { CreateTaskMod("42", true, info => applied = info), CreateTaskMod("43") }, true);
			task.Run();
			if (cancel)
			{
				Assert.IsNull(committed);
				Assert.IsNull(applied);
			}
			else
			{
				Assert.AreEqual(1, committed.Count);
				Assert.AreEqual(42, committed[0].ModId);
				Assert.AreEqual(77, applied.CustomCategoryId);
				Assert.IsTrue(applied.UpdateChecksEnabled);
			}
		}

		/// <summary>
		/// Exercises the actual login continuation, ensuring both Full and period scopes survive its asynchronous wait.
		/// </summary>
		[TestCase(true, "1m")]
		[TestCase(false, "1w")]
		public async Task UpdateTask_LoginContinuationPreservesScope(bool full, string period)
		{
			var called = new TaskCompletionSource<bool>();
			string requestedPeriod = null;
			IModRepository repository = InterfaceStub<IModRepository>.Create((method, args) =>
			{
				if (method.Name == "GetUpdatedWithMetadata")
				{
					requestedPeriod = (string)args[0];
					return new List<RepositoryModUpdate> { CreateProviderUpdate("42", 1700000000) };
				}
				if (method.Name == "GetFullFileListInfo" || method.Name == "GetFileListInfoWithFreshness")
				{
					called.TrySetResult(method.Name == "GetFullFileListInfo");
					return new RepositoryFileListInfoResult(new List<IModInfo> { new ModInfo() },
						new List<RepositoryFileUpdateCheckpoint> { null });
				}
				return null;
			});
			var manager = (ModManager)FormatterServices.GetUninitializedObject(typeof(ModManager));
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) => null);
			IEnvironmentInfo environment = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
				method.Name == "get_Settings" ? settings : null);
			typeof(ModManager).GetProperty("EnvironmentInfo").SetValue(manager, environment, null);
			typeof(ModManager).GetProperty("ModRepository").SetValue(manager, repository, null);
			manager.LoginTask = new Nexus.Client.SSO.AuthenticationFormTask(manager);
			typeof(BackgroundTask).GetProperty("Status").SetValue(manager.LoginTask, Nexus.Client.BackgroundTasks.TaskStatus.Complete, null);
			manager.UpdateCheckStarted += (sender, args) => { };
			manager.AsyncUpdateMods(new List<IMod> { CreateTaskMod("42") }, null, null, false, false, period, full);
			Assert.AreSame(called.Task, await Task.WhenAny(called.Task, Task.Delay(10000)));
			Assert.AreEqual(full, await called.Task);
			Assert.AreEqual(full ? null : period, requestedPeriod);
		}

		/// <summary>
		/// Supplies a library archive without an installation or archive-hashing dependency.
		/// </summary>
		private static IMod CreateTaskMod(string id, bool enabled = true, Action<IModInfo> apply = null)
		{
			return InterfaceStub<IMod>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_Id": return id;
					case "get_DownloadId": return "100";
					case "get_ModName": return "Local name";
					case "get_Filename": return "old.zip";
					case "get_UpdateChecksEnabled": return enabled;
					case "get_CustomCategoryId": return 77;
					case "UpdateInfo": apply?.Invoke((IModInfo)args[0]); return null;
					default: return null;
				}
			});
		}

		/// <summary>
		/// Executes the real update task synchronously so selection and commit boundaries are observable.
		/// </summary>
		private sealed class SynchronousUpdateTask : ModUpdateCheckTask
		{
			/// <summary>
			/// Creates an update task using the same Full/period arguments as the view model.
			/// </summary>
			public SynchronousUpdateTask(AutoUpdater updater, IModRepository repository, IEnumerable<IMod> mods, bool full)
				: base(updater, null, repository, mods, "1m", false, false, false, full) { }

			/// <summary>
			/// Runs selection, retrieval, local application and checkpoint publication on the test thread.
			/// </summary>
			public void Run() { DoWork(new object[0]); }
		}

		/// <summary>
		/// Creates a resolved public-file checkpoint with an independently controlled direct-verification time.
		/// </summary>
		private static RepositoryFileUpdateCheckpoint CreateVerifiedCheckpoint(DateTimeOffset verified)
		{
			return new RepositoryFileUpdateCheckpoint("skyrim", 42, DateTimeOffset.FromUnixTimeSeconds(1700000000),
				"100", "old.zip", "200", "new.zip", "New File", "2.0", true, verified);
		}

		/// <summary>
		/// Atomically records the highest concurrent fixture request count.
		/// </summary>
		private static void UpdatePeak(ref int peakConcurrency, int activeRequests)
		{
			while (true)
			{
				int observed = peakConcurrency;
				if (activeRequests <= observed || Interlocked.CompareExchange(ref peakConcurrency, activeRequests, observed) == observed)
					return;
			}
		}

		/// <summary>
		/// Creates one deterministic provider freshness record.
		/// </summary>
		private static RepositoryModUpdate CreateProviderUpdate(string modId, long latestFileUpdate)
		{
			return new RepositoryModUpdate(
				modId,
				DateTimeOffset.FromUnixTimeSeconds(latestFileUpdate),
				DateTimeOffset.FromUnixTimeSeconds(latestFileUpdate + 300));
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NMM-P23b-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static void DeleteDirectoryQuietly(string path)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
					Directory.Delete(path, true);
			}
			catch
			{
				// Test cleanup must not hide the product assertion that already completed.
			}
		}

		/// <summary>
		/// Creates an isolated repository backed by a deterministic Nexus service and authenticated test session.
		/// </summary>
		private static RepositoryContext CreateContext(RequestRouter router)
		{
			return CreateContext(router, null);
		}

		/// <summary>
		/// Creates an isolated repository with optional persistent checkpoint storage.
		/// </summary>
		private static RepositoryContext CreateContext(RequestRouter router, string installInfoDirectory)
		{
			router.Ensure("/v2/graphql", HttpStatusCode.InternalServerError, "{\"message\":\"GraphQL unavailable fixture\"}");
			string apiKey = "test-key";
			ISettings settings = InterfaceStub<ISettings>.Create((method, args) =>
			{
				switch (method.Name)
				{
					case "get_ApiKey":
						return apiKey;
					case "set_ApiKey":
						apiKey = (string)args[0];
						return null;
					case "Save":
						return null;
					default:
						return null;
				}
			});
			IEnvironmentInfo environmentInfo = InterfaceStub<IEnvironmentInfo>.Create((method, args) =>
				method.Name == "get_Settings" ? settings : null);

			ConstructorInfo constructor = typeof(ApiCallManager).GetConstructor(
				BindingFlags.Instance | BindingFlags.NonPublic,
				null,
				new[] { typeof(IEnvironmentInfo) },
				null);
			Assert.IsNotNull(constructor);
			ApiCallManager manager = (ApiCallManager)constructor.Invoke(new object[] { environmentInfo });

			PropertyInfo serviceProperty = typeof(ApiCallManager).GetProperty("NexusService", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(serviceProperty);
			NexusModsService originalService = (NexusModsService)serviceProperty.GetValue(manager, null);

			var transport = new ApiTransport(new StubHttpMessageHandler(router.SendAsync), TimeSpan.FromSeconds(5));
			var service = new NexusModsService(transport, "NMM-Test/1.0", "NMM-Test", "1.0");
			FieldInfo serviceField = typeof(ApiCallManager).GetField("_nexusService", BindingFlags.Instance | BindingFlags.NonPublic);
			Assert.IsNotNull(serviceField);
			serviceField.SetValue(manager, service);
			originalService.Dispose();
			manager.UpdateNexusClient();

			return new RepositoryContext(new NexusModsApiRepository("skyrim", manager, installInfoDirectory), service);
		}

		/// <summary>
		/// Owns the deterministic provider service used by one repository test.
		/// </summary>
		private sealed class RepositoryContext : IDisposable
		{
			public RepositoryContext(NexusModsApiRepository repository, NexusModsService service)
			{
				Repository = repository;
				_service = service;
			}

			private readonly NexusModsService _service;

			public NexusModsApiRepository Repository { get; }

			public void Dispose()
			{
				_service.Dispose();
			}
		}

		/// <summary>
		/// Routes deterministic Nexus responses and records the exact number of requests per endpoint.
		/// </summary>
		private sealed class RequestRouter
		{
			private readonly object _sync = new object();
			private readonly Dictionary<string, Func<HttpRequestMessage, ResponseFixture>> _responses = new Dictionary<string, Func<HttpRequestMessage, ResponseFixture>>(StringComparer.Ordinal);
			private readonly Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<ResponseFixture>>> _asyncResponses = new Dictionary<string, Func<HttpRequestMessage, CancellationToken, Task<ResponseFixture>>>(StringComparer.Ordinal);
			private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
			private readonly List<string> _unexpected = new List<string>();

			/// <summary>
			/// Adds one fixed successful response.
			/// </summary>
			public void AddOk(string path, string content)
			{
				Add(path, HttpStatusCode.OK, content);
			}

			/// <summary>
			/// Adds one fixed response for the supplied path.
			/// </summary>
			public void Add(string path, HttpStatusCode statusCode, string content)
			{
				_responses[path] = request => new ResponseFixture(statusCode, content);
			}

			/// <summary>
			/// Adds a default response only when a test has not configured the path explicitly.
			/// </summary>
			public void Ensure(string path, HttpStatusCode statusCode, string content)
			{
				if (!_responses.ContainsKey(path))
					Add(path, statusCode, content);
			}

			/// <summary>
			/// Adds a request-aware deterministic response factory.
			/// </summary>
			public void AddHandler(string path, Func<HttpRequestMessage, ResponseFixture> responseFactory)
			{
				_responses[path] = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
			}

			/// <summary>
			/// Adds an asynchronous response factory used by bounded-concurrency request fixtures.
			/// </summary>
			public void AddAsyncHandler(string path, Func<HttpRequestMessage, CancellationToken, Task<ResponseFixture>> responseFactory)
			{
				_asyncResponses[path] = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
			}

			/// <summary>
			/// Adds a GraphQL fixture that echoes every requested legacy mod identity as complete parent metadata.
			/// </summary>
			public void AddGraphQlEcho()
			{
				AddAsyncHandler("/v2/graphql", async (request, cancellationToken) =>
				{
					string body = request.Content == null
						? string.Empty
						: await request.Content.ReadAsStringAsync().ConfigureAwait(false);
					GraphQlBulkRequestFixture envelope = ApiJson.Deserialize<GraphQlBulkRequestFixture>(body);
					GraphQlBulkIdentityFixture[] identities = envelope?.Variables?.Ids ?? new GraphQlBulkIdentityFixture[0];
					string nodes = string.Join(",", identities.Select(identity => CreateGraphQlNodeJson(identity.ModId)));
					string response = string.Format(
						"{{\"data\":{{\"legacyModsByDomain\":{{\"nodes\":[{0}],\"totalCount\":{1}}}}}}}",
						nodes,
						identities.Length);
					return new ResponseFixture(HttpStatusCode.OK, response);
				});
			}

			/// <summary>
			/// Creates one complete GraphQL mod node for a requested legacy ID.
			/// </summary>
			private static string CreateGraphQlNodeJson(int modId)
			{
				return string.Format(
					"{{\"modId\":{0},\"name\":\"GraphQL Mod {0}\",\"version\":\"9.1\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{{\"categoryId\":8}},\"viewerEndorsed\":true}}",
					modId);
			}

			public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				string path = request.RequestUri.AbsolutePath + Uri.UnescapeDataString(request.RequestUri.Query);
				Func<HttpRequestMessage, ResponseFixture> responseFactory;
				Func<HttpRequestMessage, CancellationToken, Task<ResponseFixture>> asyncResponseFactory;
				lock (_sync)
				{
					int count;
					_counts.TryGetValue(path, out count);
					_counts[path] = count + 1;
					_responses.TryGetValue(path, out responseFactory);
					_asyncResponses.TryGetValue(path, out asyncResponseFactory);
					if (responseFactory == null && asyncResponseFactory == null)
						_unexpected.Add(path);
				}

				ResponseFixture fixture;
				if (asyncResponseFactory != null)
					fixture = await asyncResponseFactory(request, cancellationToken).ConfigureAwait(false);
				else if (responseFactory != null)
					fixture = responseFactory(request);
				else
					fixture = new ResponseFixture(HttpStatusCode.InternalServerError, "{\"message\":\"unexpected fixture request\"}");

				return new HttpResponseMessage(fixture.StatusCode)
				{
					Content = new StringContent(fixture.Content)
				};
			}

			public void AssertCount(string path, int expected)
			{
				int actual;
				lock (_sync)
					_counts.TryGetValue(path, out actual);
				Assert.AreEqual(expected, actual, "Unexpected request count for {0}.", path);
			}

			public void AssertNoUnexpectedRequests()
			{
				string[] unexpected;
				lock (_sync)
					unexpected = _unexpected.ToArray();
				Assert.AreEqual(0, unexpected.Length, "Unexpected requests: {0}", string.Join(", ", unexpected));
			}
		}

		/// <summary>
		/// Represents a captured GraphQL bulk-parent request envelope.
		/// </summary>
		private sealed class GraphQlBulkRequestFixture
		{
			public GraphQlBulkVariablesFixture Variables { get; set; }
		}

		/// <summary>
		/// Represents the variables object in a captured GraphQL bulk-parent request.
		/// </summary>
		private sealed class GraphQlBulkVariablesFixture
		{
			public GraphQlBulkIdentityFixture[] Ids { get; set; }
		}

		/// <summary>
		/// Represents one legacy mod identity in a captured GraphQL bulk-parent request.
		/// </summary>
		private sealed class GraphQlBulkIdentityFixture
		{
			public int ModId { get; set; }
		}

		/// <summary>
		/// Stores one deterministic HTTP response used by the request router.
		/// </summary>
		private sealed class ResponseFixture
		{
			public ResponseFixture(HttpStatusCode statusCode, string content)
			{
				StatusCode = statusCode;
				Content = content;
			}

			public HttpStatusCode StatusCode { get; }

			public string Content { get; }
		}

		/// <summary>
		/// Provides deterministic HTTP responses without external Nexus Mods access.
		/// </summary>
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

		private const string GraphQlParent42Fixture = "{\"data\":{\"legacyModsByDomain\":{\"nodes\":[{\"modId\":42,\"name\":\"GraphQL Mod 42\",\"version\":\"9.1\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{\"categoryId\":8},\"viewerEndorsed\":true}],\"totalCount\":1}}}";
		private const string GraphQlParent42AbstainedFixture = "{\"data\":{\"legacyModsByDomain\":{\"nodes\":[{\"modId\":42,\"name\":\"GraphQL Mod 42\",\"version\":\"9.1\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{\"categoryId\":8},\"viewerEndorsed\":false}],\"totalCount\":1}}}";
		private const string GraphQlParents42And43Fixture = "{\"data\":{\"legacyModsByDomain\":{\"nodes\":[{\"modId\":42,\"name\":\"GraphQL Mod 42\",\"version\":\"9.1\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{\"categoryId\":8},\"viewerEndorsed\":true},{\"modId\":43,\"name\":\"GraphQL Mod 43\",\"version\":\"9.2\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{\"categoryId\":9},\"viewerEndorsed\":null}],\"totalCount\":2}}}";
		private const string GraphQlIncompleteParent42Fixture = "{\"data\":{\"legacyModsByDomain\":{\"nodes\":[{\"modId\":42,\"name\":\"Incomplete GraphQL Mod\",\"version\":\"9.1\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":null,\"viewerEndorsed\":true}],\"totalCount\":1}}}";
		private const string GraphQlParents42And43WithViewerErrorOn42Fixture = "{\"data\":{\"legacyModsByDomain\":{\"nodes\":[{\"modId\":42,\"name\":\"GraphQL Mod 42\",\"version\":\"9.1\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{\"categoryId\":8},\"viewerEndorsed\":null},{\"modId\":43,\"name\":\"GraphQL Mod 43\",\"version\":\"9.2\",\"author\":\"GraphQL Author\",\"description\":\"GraphQL description\",\"modCategory\":{\"categoryId\":9},\"viewerEndorsed\":null}],\"totalCount\":2}},\"errors\":[{\"message\":\"viewer endorsement unavailable\",\"path\":[\"legacyModsByDomain\",\"nodes\",0,\"viewerEndorsed\"]}]}";
		private const string GraphQlSchemaErrorFixture = "{\"data\":null,\"errors\":[{\"message\":\"Cannot query field viewerEndorsed on type Mod\"}]}";
		private const string UpdatedModsFixture = "[{\"mod_id\":42,\"latest_file_update\":1700000000,\"latest_mod_activity\":1700000300},{\"mod_id\":43,\"latest_file_update\":1710000000,\"latest_mod_activity\":1710000600}]";
		private const string ParentModFixture = "{\"mod_id\":42,\"name\":\"Fixture Mod\",\"description\":\"Fixture description\",\"domain_name\":\"skyrim\",\"category_id\":7,\"version\":\"9.0\",\"author\":\"Fixture Author\",\"endorsement\":{\"endorse_status\":\"Endorsed\",\"version\":\"9.0\"}}";
		private const string ParentMod43Fixture = "{\"mod_id\":43,\"name\":\"Fixture Mod 43\",\"description\":\"Fixture description\",\"domain_name\":\"skyrim\",\"category_id\":7,\"version\":\"9.0\",\"author\":\"Fixture Author\"}";
		private const string CompleteFileListFixture = "{\"files\":[{\"file_id\":100,\"name\":\"Old File\",\"file_name\":\"old.zip\",\"mod_version\":\"1.0\",\"category_id\":4,\"uploaded_timestamp\":1600000000},{\"file_id\":200,\"name\":\"New File\",\"file_name\":\"new.zip\",\"mod_version\":\"2.0\",\"category_id\":1,\"uploaded_timestamp\":1700000000}],\"file_updates\":[{\"old_file_id\":100,\"old_file_name\":\"old.zip\",\"new_file_id\":200,\"new_file_name\":\"new.zip\"}]}";
		private const string MissingSuccessorFileListFixture = "{\"files\":[{\"file_id\":100,\"name\":\"Old File\",\"file_name\":\"old.zip\",\"mod_version\":\"1.0\",\"category_id\":4,\"uploaded_timestamp\":1600000000}],\"file_updates\":[{\"old_file_id\":100,\"old_file_name\":\"old.zip\",\"new_file_id\":300,\"new_file_name\":\"successor.zip\"}]}";
		private const string TwoMissingSuccessorsFileListFixture = "{\"files\":[{\"file_id\":100,\"name\":\"Old File\",\"file_name\":\"old.zip\",\"mod_version\":\"1.0\",\"category_id\":4,\"uploaded_timestamp\":1600000000},{\"file_id\":200,\"name\":\"Other Old File\",\"file_name\":\"other-old.zip\",\"mod_version\":\"2.0\",\"category_id\":4,\"uploaded_timestamp\":1650000000}],\"file_updates\":[{\"old_file_id\":100,\"old_file_name\":\"old.zip\",\"new_file_id\":300,\"new_file_name\":\"successor.zip\"},{\"old_file_id\":200,\"old_file_name\":\"other-old.zip\",\"new_file_id\":400,\"new_file_name\":\"other-successor.zip\"}]}";
		private const string Successor300Fixture = "{\"file_id\":300,\"name\":\"Successor File\",\"file_name\":\"successor.zip\",\"mod_version\":\"3.0\",\"category_id\":1,\"uploaded_timestamp\":1750000000}";
		private const string Successor400Fixture = "{\"file_id\":400,\"name\":\"Other Successor File\",\"file_name\":\"other-successor.zip\",\"mod_version\":\"4.0\",\"category_id\":1,\"uploaded_timestamp\":1760000000}";
	}
}
