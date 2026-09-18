using System;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModAuthoring;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionPremiumAcquisitionTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Queue_PremiumAccount_UsesUnsignedExactNxmAndExistingQueueIdentity()
		{
			CollectionAcquisitionRequest request = CreateRequest("nexus-mod-file", "skyrimspecialedition/100/200");
			RecordingQueue queue = new RecordingQueue();
			CollectionPremiumAcquisitionCoordinator coordinator = CreateCoordinator(
				queue, new CollectionPremiumAcquisitionAccountState("SkyrimSpecialEdition", true, true));

			CollectionAcquisitionQueueCorrelation result = coordinator.Queue(request, null);

			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(queue.SourceUri.AbsoluteUri, Is.EqualTo("nxm://skyrimspecialedition/mods/100/files/200"));
			Assert.That(queue.SourceUri.Query, Is.Empty);
			Assert.That(queue.QueueOperationId, Is.EqualTo(request.RequestId));
			Assert.That(result.QueueOperationId, Is.EqualTo(request.RequestId));
			Assert.That(result.Task, Is.Not.SameAs(queue.Task));
			Assert.That(result.Task.Status, Is.EqualTo(queue.Task.Status));
		}

		[Test]
		public void GetAvailability_UnauthenticatedAccount_RequiresManualOrLaterFlow()
		{
			CollectionPremiumAcquisitionCoordinator coordinator = CreateCoordinator(
				new RecordingQueue(), new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", false, false));

			Assert.That(coordinator.GetAvailability(CreateRequest("nexus-mod-file", "skyrimspecialedition/100/200")),
				Is.EqualTo(CollectionPremiumAcquisitionAvailability.NotAuthenticated));
		}

		[Test]
		public void Queue_FreeAccount_IsRejectedBeforeAddMod()
		{
			RecordingQueue queue = new RecordingQueue();
			CollectionPremiumAcquisitionCoordinator coordinator = CreateCoordinator(
				queue, new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, false));

			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
				coordinator.Queue(CreateRequest("nexus-mod-file", "skyrimspecialedition/100/200"), null));

			Assert.That(exception.Message, Does.Contain(CollectionPremiumAcquisitionAvailability.PremiumRequired.ToString()));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_DifferentRepositoryGame_IsRejectedBeforeAddMod()
		{
			RecordingQueue queue = new RecordingQueue();
			CollectionPremiumAcquisitionCoordinator coordinator = CreateCoordinator(
				queue, new CollectionPremiumAcquisitionAccountState("fallout4", true, true));

			Assert.That(coordinator.GetAvailability(CreateRequest("nexus-mod-file", "skyrimspecialedition/100/200")),
				Is.EqualTo(CollectionPremiumAcquisitionAvailability.GameDomainMismatch));
			Assert.Throws<InvalidOperationException>(() =>
				coordinator.Queue(CreateRequest("nexus-mod-file", "skyrimspecialedition/100/200"), null));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_NonNexusArtifact_IsRejectedBeforeAccountOrAddMod()
		{
			RecordingQueue queue = new RecordingQueue();
			RecordingAccountProvider accounts = new RecordingAccountProvider(
				new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, true));
			CollectionPremiumAcquisitionCoordinator coordinator = new CollectionPremiumAcquisitionCoordinator(
				new CollectionAcquisitionRequestCoordinator(queue), accounts);
			CollectionAcquisitionRequest request = CreateRequest("direct-url", "artifact-1");

			Assert.That(coordinator.GetAvailability(request), Is.EqualTo(CollectionPremiumAcquisitionAvailability.UnsupportedArtifact));
			Assert.That(accounts.CaptureCount, Is.EqualTo(0));
			Assert.Throws<InvalidOperationException>(() => coordinator.Queue(request, null));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_MalformedNexusArtifact_IsRejectedBeforeAddMod()
		{
			RecordingQueue queue = new RecordingQueue();
			CollectionPremiumAcquisitionCoordinator coordinator = CreateCoordinator(
				queue, new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, true));
			CollectionAcquisitionRequest request = CreateRequest("nexus-mod-file", "not/a/valid/file/id");

			Assert.That(coordinator.GetAvailability(request), Is.EqualTo(CollectionPremiumAcquisitionAvailability.UnsupportedArtifact));
			Assert.Throws<InvalidOperationException>(() => coordinator.Queue(request, null));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_CapturesAccountOnceAndNeverCarriesAuthorizationQuery()
		{
			RecordingQueue queue = new RecordingQueue();
			RecordingAccountProvider accounts = new RecordingAccountProvider(
				new CollectionPremiumAcquisitionAccountState("skyrimspecialedition", true, true));
			CollectionPremiumAcquisitionCoordinator coordinator = new CollectionPremiumAcquisitionCoordinator(
				new CollectionAcquisitionRequestCoordinator(queue), accounts);

			coordinator.Queue(CreateRequest("nexus-mod-file", "skyrimspecialedition/100/200"), null);

			Assert.That(accounts.CaptureCount, Is.EqualTo(1));
			Assert.That(queue.SourceUri.Query, Is.Empty);
			Assert.That(queue.SourceUri.ToString(), Does.Not.Contain("key="));
			Assert.That(queue.SourceUri.ToString(), Does.Not.Contain("expires="));
			Assert.That(queue.SourceUri.ToString(), Does.Not.Contain("user_id="));
		}

		private static CollectionPremiumAcquisitionCoordinator CreateCoordinator(
			RecordingQueue queue,
			CollectionPremiumAcquisitionAccountState state)
		{
			return new CollectionPremiumAcquisitionCoordinator(
				new CollectionAcquisitionRequestCoordinator(queue),
				new RecordingAccountProvider(state));
		}

		private static CollectionAcquisitionRequest CreateRequest(string scheme, string stableArtifactId)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-1");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-1", 1);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(Sha256A), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-1")),
				CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected,
				new CollectionArtifactReference(scheme, stableArtifactId, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-1"),
				"Member 1");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(
				member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"), 1),
				CollectionTargetIdentity.FromFingerprint("target-1"),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-1"),
				CollectionCapabilityReport.Create(manifest),
				new[] { memberPlan });
			return CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, memberPlan.MemberKey);
		}

		private sealed class RecordingAccountProvider : ICollectionPremiumAcquisitionAccountProvider
		{
			private readonly CollectionPremiumAcquisitionAccountState _state;

			public RecordingAccountProvider(CollectionPremiumAcquisitionAccountState state)
			{
				_state = state;
			}

			public int CaptureCount { get; private set; }

			public CollectionPremiumAcquisitionAccountState Capture()
			{
				CaptureCount++;
				return _state;
			}
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			public RecordingQueue()
			{
				Task = new Nexus.Client.BackgroundTask();
			}

			public int CallCount { get; private set; }
			public Uri SourceUri { get; private set; }
			public Guid QueueOperationId { get; private set; }
			public IBackgroundTask Task { get; }

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				SourceUri = sourceUri;
				QueueOperationId = queueOperationId;
				return Task;
			}
		}
	}
}
