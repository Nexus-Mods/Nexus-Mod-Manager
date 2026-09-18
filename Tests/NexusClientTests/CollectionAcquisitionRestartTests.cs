using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionAcquisitionRestartTests
	{
		private const string ManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string ArtifactId = "skyrimspecialedition/100/200";

		[Test]
		public void AcquisitionStore_QueuedSharedConsumersRoundTripAndRejectRequestRebinding()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				var featureStore = new CollectionsStore(root);
				featureStore.CreateNew();
				var store = new CollectionsAcquisitionStore(featureStore);
				Guid producerId = Guid.NewGuid();
				CollectionAcquisitionRequest first = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				CollectionAcquisitionRequest second = CreateRequest(Guid.NewGuid(), "collection-b", "member-b", null, "recipe-b", "target-b");

				store.TrackQueued(first, producerId, CollectionAcquisitionPersistenceMode.PremiumNxm);
				store.TrackQueued(second, producerId, CollectionAcquisitionPersistenceMode.Manual);

				CollectionAcquisitionRecord reloadedFirst = new CollectionsAcquisitionStore(new CollectionsStore(root)).Get(first.RequestId);
				CollectionAcquisitionRecord reloadedSecond = new CollectionsAcquisitionStore(new CollectionsStore(root)).Get(second.RequestId);
				Assert.That(reloadedFirst.QueueOperationId, Is.EqualTo(producerId));
				Assert.That(reloadedSecond.QueueOperationId, Is.EqualTo(producerId));
				Assert.That(reloadedFirst.Matches(first), Is.True);
				Assert.That(reloadedSecond.Matches(second), Is.True);

				CollectionAcquisitionRequest rebound = CreateRequest(first.RequestId, "collection-a", "different-member", null, "recipe-a", "target-a");
				Assert.Throws<InvalidOperationException>(() => store.TrackQueued(
					rebound, producerId, CollectionAcquisitionPersistenceMode.PremiumNxm));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void AddModDescriptor_QueueOperationId_RoundTripsWithoutBreakingLegacyDescriptors()
		{
			Guid queueId = Guid.NewGuid();
			var descriptor = new AddModDescriptor(
				new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=temp&expires=2000000000&user_id=7"),
				"archive.7z", null, TaskStatus.Paused, new List<string> { "Default" }, "mod.7z", "mod.7z", queueId);
			descriptor.SourcePath = "archive.7z";
			var serializer = new XmlSerializer(typeof(AddModDescriptor));
			using (var stream = new MemoryStream())
			{
				serializer.Serialize(stream, descriptor);
				stream.Position = 0;
				var roundTripped = (AddModDescriptor)serializer.Deserialize(stream);
				Assert.That(roundTripped.QueueOperationId, Is.EqualTo(queueId));
				Assert.That(roundTripped.SourceUri, Is.EqualTo(descriptor.SourceUri));
			}

			var legacy = new AddModDescriptor(new Uri("file:///legacy.7z"), "legacy.7z", null,
				TaskStatus.Paused, new List<string> { "Default" }, "legacy.7z", "legacy.7z");
			legacy.SourcePath = "legacy.7z";
			using (var stream = new MemoryStream())
			{
				serializer.Serialize(stream, legacy);
				stream.Position = 0;
				Assert.That(((AddModDescriptor)serializer.Deserialize(stream)).QueueOperationId, Is.Null);
			}
		}

		[Test]
		public void AcquisitionStore_ManualRefreshPreservesSharedProducerAndCancelledConsumerStaysCancelled()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				Guid producerId = Guid.NewGuid();
				CollectionAcquisitionRequest first = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				CollectionAcquisitionRequest second = CreateRequest(Guid.NewGuid(), "collection-b", "member-b", null, "recipe-b", "target-b");
				stores.Acquisitions.TrackQueued(first, producerId, CollectionAcquisitionPersistenceMode.Manual);
				stores.Acquisitions.TrackQueued(second, producerId, CollectionAcquisitionPersistenceMode.Manual);

				stores.Acquisitions.TrackPending(first, CollectionAcquisitionPersistenceMode.Manual);
				stores.Acquisitions.MarkCancelled(second.RequestId);
				stores.Acquisitions.MarkProducerState(producerId, TaskStatus.Complete);

				Assert.That(stores.Acquisitions.Get(first.RequestId).QueueOperationId, Is.EqualTo(producerId));
				Assert.That(stores.Acquisitions.Get(first.RequestId).State, Is.EqualTo(CollectionAcquisitionPersistenceState.PendingUserAction));
				Assert.That(stores.Acquisitions.Get(second.RequestId).State, Is.EqualTo(CollectionAcquisitionPersistenceState.Cancelled));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void RequestCoordinator_AfterRestart_ReusesPersistedSharedProducerIdentity()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				Guid producerId = Guid.NewGuid();
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-b", "member-b", null, "recipe-b", "target-b");
				stores.Acquisitions.TrackQueued(request, producerId, CollectionAcquisitionPersistenceMode.PremiumNxm);
				var queue = new RecordingQueue();
				var coordinator = new CollectionAcquisitionRequestCoordinator(queue, stores.Acquisitions);

				CollectionAcquisitionQueueCorrelation correlation = coordinator.Queue(request,
					new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

				Assert.That(queue.LastQueueOperationId, Is.EqualTo(producerId));
				Assert.That(correlation.QueueOperationId, Is.EqualTo(producerId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifiedAcquisitionRecord_ProtectsRetainedArtifactFromCleanup()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("durable verified input"), false))
					artifact = stores.Artifacts.Publish(stream);
				stores.Acquisitions.MarkVerified(request, artifact.ArtifactId);
				var cleanup = new CollectionsRetainedArtifactCleanupStore(stores.Store);

				Assert.That(cleanup.GetCleanupCandidates(10), Does.Not.Contain(artifact.ArtifactId));
				Assert.That(cleanup.TryCollectUnreferencedArtifact(artifact.ArtifactId), Is.False);
				Assert.That(stores.Artifacts.GetArtifact(artifact.ArtifactId), Is.Not.Null);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_VerifiedRetainedInputWinsOverMissingNativeQueueState()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes("verified restart input");
				string digest;
				using (SHA256 sha256 = SHA256.Create())
					digest = ToHex(sha256.ComputeHash(bytes));
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a",
					CollectionContentHash.FromSha256(digest), "recipe-a", "target-a");
				TestStores stores = CreateStores(root);
				stores.Acquisitions.TrackPending(request, CollectionAcquisitionPersistenceMode.Manual);
				using (var stream = new MemoryStream(bytes, false))
					stores.Artifacts.Publish(stream);

				CollectionAcquisitionRestartCoordinator coordinator = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(null), new FakeAccountProvider(true, true));
				CollectionAcquisitionRestartResult result = coordinator.Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.VerifiedInputReady));
				Assert.That(result.VerifiedArchive, Is.Not.Null);
				Assert.That(stores.Acquisitions.Get(request.RequestId).State, Is.EqualTo(CollectionAcquisitionPersistenceState.Verified));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_PremiumPartialDownloadWithCurrentPremiumAccount_IsReadyForFreshAuthorizedResume()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				Guid queueId = Guid.NewGuid();
				stores.Acquisitions.TrackQueued(request, queueId, CollectionAcquisitionPersistenceMode.PremiumNxm);
				var native = new CollectionPersistedAddModState(queueId, TaskStatus.Incomplete, true,
					"skyrimspecialedition", 100, 200, false, null, true);

				CollectionAcquisitionRestartResult result = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(native), new FakeAccountProvider(true, true)).Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.PremiumResumeReady));
				Assert.That(result.HasPartialNativeData, Is.True);
				Assert.That(result.PremiumAvailability, Is.EqualTo(CollectionPremiumAcquisitionAvailability.Available));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_PremiumRequestAfterAccountChangeToFree_DoesNotResumeAutomatically()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				stores.Acquisitions.TrackQueued(request, Guid.NewGuid(), CollectionAcquisitionPersistenceMode.PremiumNxm);

				CollectionAcquisitionRestartResult result = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(null), new FakeAccountProvider(true, false)).Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.PremiumUnavailable));
				Assert.That(result.PremiumAvailability, Is.EqualTo(CollectionPremiumAcquisitionAvailability.PremiumRequired));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_PreviousSessionSignedNxm_RequiresFreshManualAuthorizationEvenWhenNotExpired()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				Guid queueId = Guid.NewGuid();
				stores.Acquisitions.TrackQueued(request, queueId, CollectionAcquisitionPersistenceMode.Manual);
				var native = new CollectionPersistedAddModState(queueId, TaskStatus.Paused, true,
					"skyrimspecialedition", 100, 200, true, DateTime.UtcNow.AddHours(1), true);

				CollectionAcquisitionRestartResult result = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(native), new FakeAccountProvider(true, true)).Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.ManualAuthorizationRefreshRequired));
				Assert.That(result.HasPartialNativeData, Is.True);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_ExpiredSignedNxm_IsReportedSeparatelyFromResumablePremiumWork()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				Guid queueId = Guid.NewGuid();
				stores.Acquisitions.TrackQueued(request, queueId, CollectionAcquisitionPersistenceMode.Manual);
				var native = new CollectionPersistedAddModState(queueId, TaskStatus.Incomplete, true,
					"skyrimspecialedition", 100, 200, true, DateTime.UtcNow.AddMinutes(-1), true);

				CollectionAcquisitionRestartResult result = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(native), new FakeAccountProvider(true, true)).Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.ManualAuthorizationExpired));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_PersistedNativeIdentityMismatch_FailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				Guid queueId = Guid.NewGuid();
				stores.Acquisitions.TrackQueued(request, queueId, CollectionAcquisitionPersistenceMode.PremiumNxm);
				var native = new CollectionPersistedAddModState(queueId, TaskStatus.Incomplete, true,
					"skyrimspecialedition", 100, 999, false, null, true);

				CollectionAcquisitionRestartResult result = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(native), new FakeAccountProvider(true, true)).Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.NativeStateMismatch));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void Reconcile_TerminalNativeDescriptorWithoutVerifiedBytes_RequiresReacquisition()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestStores stores = CreateStores(root);
				CollectionAcquisitionRequest request = CreateRequest(Guid.NewGuid(), "collection-a", "member-a", null, "recipe-a", "target-a");
				Guid queueId = Guid.NewGuid();
				stores.Acquisitions.TrackQueued(request, queueId, CollectionAcquisitionPersistenceMode.PremiumNxm);
				var native = new CollectionPersistedAddModState(queueId, TaskStatus.Error, true,
					"skyrimspecialedition", 100, 200, false, null, true);

				CollectionAcquisitionRestartResult result = CreateRestartCoordinator(stores,
					new FakeNativeStateSource(native), new FakeAccountProvider(true, true)).Reconcile(request);

				Assert.That(result.Disposition, Is.EqualTo(CollectionAcquisitionRestartDisposition.ReacquisitionRequired));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static CollectionAcquisitionRestartCoordinator CreateRestartCoordinator(TestStores stores,
			ICollectionPersistedAddModStateSource nativeState, ICollectionPremiumAcquisitionAccountProvider accounts)
		{
			var adopter = new CollectionVerifiedArchiveAdopter(new EmptyArchiveSource(), new RejectingVerifier(),
				stores.Artifacts, stores.References, stores.Acquisitions);
			var premium = new CollectionPremiumAcquisitionCoordinator(
				new CollectionAcquisitionRequestCoordinator(new NoopQueue(), stores.Acquisitions), accounts);
			return new CollectionAcquisitionRestartCoordinator(stores.Acquisitions, nativeState, adopter, premium);
		}

		private static TestStores CreateStores(string root)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			return new TestStores(store, new CollectionsAcquisitionStore(store),
				new CollectionsRetainedArtifactStore(store), new CollectionsRetainedArtifactReferenceStore(store));
		}

		private static CollectionAcquisitionRequest CreateRequest(Guid requestId, string collectionId, string memberId,
			CollectionContentHash expectedHash, string recipeId, string targetId)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus(collectionId);
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-" + collectionId, 1);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(ManifestHash), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider(memberId)),
				CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", ArtifactId, expectedHash),
				CollectionRecipeIdentity.FromFingerprint(recipeId), memberId);
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(
				member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.NewGuid(), 1), CollectionTargetIdentity.FromFingerprint(targetId),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-" + collectionId),
				CollectionCapabilityReport.Create(manifest), new[] { memberPlan });
			return CollectionAcquisitionRequest.Create(requestId, plan, memberPlan.MemberKey);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "nmm-c422-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static string ToHex(byte[] bytes)
		{
			var builder = new StringBuilder(bytes.Length * 2);
			for (int i = 0; i < bytes.Length; i++)
				builder.Append(bytes[i].ToString("x2"));
			return builder.ToString();
		}

		private sealed class TestStores
		{
			public TestStores(CollectionsStore store, CollectionsAcquisitionStore acquisitions,
				CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references)
			{
				Store = store;
				Acquisitions = acquisitions;
				Artifacts = artifacts;
				References = references;
			}
			public CollectionsStore Store { get; }
			public CollectionsAcquisitionStore Acquisitions { get; }
			public CollectionsRetainedArtifactStore Artifacts { get; }
			public CollectionsRetainedArtifactReferenceStore References { get; }
		}

		private sealed class FakeNativeStateSource : ICollectionPersistedAddModStateSource
		{
			private readonly CollectionPersistedAddModState _state;
			public FakeNativeStateSource(CollectionPersistedAddModState state) { _state = state; }
			public CollectionPersistedAddModState Find(Guid queueOperationId)
			{
				return _state != null && _state.QueueOperationId == queueOperationId ? _state : null;
			}
		}

		private sealed class FakeAccountProvider : ICollectionPremiumAcquisitionAccountProvider
		{
			private readonly bool _authenticated;
			private readonly bool _premium;
			public FakeAccountProvider(bool authenticated, bool premium) { _authenticated = authenticated; _premium = premium; }
			public CollectionPremiumAcquisitionAccountState Capture()
			{
				return new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", _authenticated, _premium);
			}
		}

		private sealed class EmptyArchiveSource : ICollectionManagedArchiveSource
		{
			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				return new CollectionManagedArchiveCandidate[0];
			}
		}

		private sealed class RejectingVerifier : ICollectionArchiveIdentityVerifier
		{
			public bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive,
				System.Threading.CancellationToken cancellationToken)
			{
				return false;
			}
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			public Guid LastQueueOperationId { get; private set; }

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				LastQueueOperationId = queueOperationId;
				return new RecordingBackgroundTask();
			}
		}

		private sealed class RecordingBackgroundTask : BackgroundTask
		{
		}

		private sealed class NoopQueue : ICollectionAddModQueue
		{
			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				throw new InvalidOperationException("Restart classification must not start native acquisition work.");
			}
		}
	}
}
