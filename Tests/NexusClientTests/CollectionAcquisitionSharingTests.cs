using System;
using System.Collections.Generic;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModAuthoring;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionAcquisitionSharingTests
	{
		private const string ManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
		private const string HashA = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
		private const string HashB = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
		private const string ArtifactA = "skyrimspecialedition/100/200";
		private const string ArtifactB = "skyrimspecialedition/100/201";

		[Test]
		public void Queue_TwoCollectionConsumersForSameArtifact_ShareOneNativeProducer()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionRequest firstRequest = CreateRequest(
				Guid.Parse("11111111-1111-1111-1111-111111111111"), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a");
			CollectionAcquisitionRequest secondRequest = CreateRequest(
				Guid.Parse("22222222-2222-2222-2222-222222222222"), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b");

			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				firstRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				secondRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=temporary&expires=2147483647&user_id=42"), null);

			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(first.QueueOperationId, Is.EqualTo(firstRequest.RequestId));
			Assert.That(second.QueueOperationId, Is.EqualTo(first.QueueOperationId));
			Assert.That(first.Request, Is.SameAs(firstRequest));
			Assert.That(second.Request, Is.SameAs(secondRequest));
			Assert.That(first.Task, Is.Not.SameAs(second.Task));
			Assert.That(first.Task, Is.Not.SameAs(queue.Tasks[0]));
			Assert.That(second.Task, Is.Not.SameAs(queue.Tasks[0]));
			Assert.That(first.Task.Status, Is.EqualTo(TaskStatus.Running));
			Assert.That(second.Task.Status, Is.EqualTo(TaskStatus.Running));
		}

		[Test]
		public void Queue_SameProviderIdentityWithDifferentExpectedHashes_SharesDownloadButNotVerificationIdentity()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionRequest firstRequest = CreateRequest(
				Guid.NewGuid(), "collection-a", "member-a", ArtifactA, CollectionContentHash.FromSha256(HashA), "recipe-a", "target-a");
			CollectionAcquisitionRequest secondRequest = CreateRequest(
				Guid.NewGuid(), "collection-b", "member-b", ArtifactA, CollectionContentHash.FromSha256(HashB), "recipe-b", "target-b");

			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				firstRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				secondRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(second.QueueOperationId, Is.EqualTo(first.QueueOperationId));
			Assert.That(first.Request.SelectedArtifact.ExpectedContentHash, Is.Not.EqualTo(second.Request.SelectedArtifact.ExpectedContentHash));
		}

		[Test]
		public void Cancel_OneOfTwoConsumers_DetachesWithoutCancellingSharedProducer()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			first.Cancel();

			Assert.That(first.Task.Status, Is.EqualTo(TaskStatus.Cancelled));
			Assert.That(second.Task.Status, Is.EqualTo(TaskStatus.Running));
			Assert.That(queue.Tasks[0].CancelCount, Is.EqualTo(0));
		}

		[Test]
		public void Cancel_FinalConsumer_CancelsNativeProducerExactlyOnce()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			first.Cancel();
			second.Cancel();
			second.Cancel();

			Assert.That(queue.Tasks[0].CancelCount, Is.EqualTo(1));
			Assert.That(queue.Tasks[0].Status, Is.EqualTo(TaskStatus.Cancelling));
			Assert.That(first.Task.Status, Is.EqualTo(TaskStatus.Cancelled));
			Assert.That(second.Task.Status, Is.EqualTo(TaskStatus.Cancelled));
		}

		[Test]
		public void Cancel_CompletedProducer_DoesNotRewriteSuccessOrCancelNativeTask()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionQueueCorrelation correlation = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			queue.Tasks[0].Complete();

			correlation.Cancel();

			Assert.That(queue.Tasks[0].CancelCount, Is.EqualTo(0));
			Assert.That(correlation.Task.Status, Is.EqualTo(TaskStatus.Complete));
		}

		[Test]
		public void Queue_ResumableIncompleteProducer_RemainsSharedInsteadOfStartingDuplicateWork()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			queue.Tasks[0].Incomplete();

			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(first.QueueOperationId, Is.EqualTo(second.QueueOperationId));
			Assert.That(first.Task.Status, Is.EqualTo(TaskStatus.Incomplete));
			Assert.That(second.Task.Status, Is.EqualTo(TaskStatus.Incomplete));
		}

		[Test]
		public void Queue_ProducerAlreadyCancelling_RejectsNewConsumerWithoutStartingSecondProducer()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			queue.Tasks[0].Cancel();

			Assert.Throws<InvalidOperationException>(() => coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null));
			Assert.That(queue.CallCount, Is.EqualTo(1));
		}

		[Test]
		public void Queue_DifferentArtifacts_UseIndependentNativeProducers()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-b", ArtifactB, null, "recipe-b", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/201"), null);

			Assert.That(queue.CallCount, Is.EqualTo(2));
			Assert.That(first.QueueOperationId, Is.Not.EqualTo(second.QueueOperationId));
		}

		[Test]
		public void Queue_CancelledConsumerCanReattachWhileAnotherConsumerKeepsProducerAlive()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionRequest firstRequest = CreateRequest(
				Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a");
			CollectionAcquisitionRequest secondRequest = CreateRequest(
				Guid.NewGuid(), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b");
			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				firstRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(
				secondRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			first.Cancel();

			CollectionAcquisitionQueueCorrelation reattached = coordinator.Queue(
				firstRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=new&expires=2147483647&user_id=7"), null);

			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(reattached, Is.Not.SameAs(first));
			Assert.That(reattached.QueueOperationId, Is.EqualTo(second.QueueOperationId));
			Assert.That(reattached.Task.Status, Is.EqualTo(TaskStatus.Running));
		}

		[Test]
		public void Queue_ReusedRequestIdWithDifferentIntent_IsRejected()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			Guid requestId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
			CollectionAcquisitionRequest firstRequest = CreateRequest(
				requestId, "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a");
			CollectionAcquisitionRequest reboundRequest = CreateRequest(
				requestId, "collection-a", "member-b", ArtifactB, null, "recipe-b", "target-a");
			coordinator.Queue(firstRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			Assert.Throws<InvalidOperationException>(() => coordinator.Queue(
				reboundRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/201"), null));
			Assert.That(queue.CallCount, Is.EqualTo(1));
		}

		[Test]
		public void Queue_CancelledRequestIdCannotBeReboundToDifferentIntent()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			Guid requestId = Guid.Parse("dddddddd-bbbb-cccc-dddd-eeeeeeeeeeee");
			CollectionAcquisitionRequest firstRequest = CreateRequest(
				requestId, "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a");
			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(
				firstRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			first.Cancel();
			CollectionAcquisitionRequest reboundRequest = CreateRequest(
				requestId, "collection-a", "member-b", ArtifactB, null, "recipe-b", "target-a");

			Assert.Throws<InvalidOperationException>(() => coordinator.Queue(
				reboundRequest, new Uri("nxm://skyrimspecialedition/mods/100/files/201"), null));
			Assert.That(queue.CallCount, Is.EqualTo(1));
		}

		[Test]
		public void ConsumerTask_CannotPauseQueueOrResumeSharedProducer()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			CollectionAcquisitionQueueCorrelation correlation = coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			Assert.That(correlation.Task.SupportsPause, Is.False);
			Assert.That(correlation.Task.SupportsQueue, Is.False);
			Assert.Throws<InvalidOperationException>(() => correlation.Task.Pause());
			Assert.Throws<InvalidOperationException>(() => correlation.Task.Queue());
			Assert.Throws<InvalidOperationException>(() => correlation.Task.Resume());
			Assert.That(queue.Tasks[0].CancelCount, Is.EqualTo(0));
		}

		[Test]
		public void ProducerCompletion_RemovesSharingSlotForLaterExplicitAcquisitionAttempt()
		{
			var queue = new RecordingQueue();
			var coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-a", "member-a", ArtifactA, null, "recipe-a", "target-a"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);
			queue.Tasks[0].Complete();

			coordinator.Queue(
				CreateRequest(Guid.NewGuid(), "collection-b", "member-b", ArtifactA, null, "recipe-b", "target-b"),
				new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null);

			Assert.That(queue.CallCount, Is.EqualTo(2),
				"C4.21 shares active work only; C4.18 verified reuse should normally satisfy a later request before it is requeued.");
		}

		private static CollectionAcquisitionRequest CreateRequest(Guid requestId, string collectionId, string memberId,
			string stableArtifactId, CollectionContentHash expectedHash, string recipeId, string targetId)
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
				new CollectionArtifactReference("nexus-mod-file", stableArtifactId, expectedHash),
				CollectionRecipeIdentity.FromFingerprint(recipeId),
				memberId);
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(
				member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.NewGuid(), 1),
				CollectionTargetIdentity.FromFingerprint(targetId),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-" + collectionId),
				CollectionCapabilityReport.Create(manifest),
				new[] { memberPlan });
			return CollectionAcquisitionRequest.Create(requestId, plan, memberPlan.MemberKey);
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			public int CallCount { get; private set; }
			public List<RecordingBackgroundTask> Tasks { get; } = new List<RecordingBackgroundTask>();

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				var task = new RecordingBackgroundTask();
				Tasks.Add(task);
				return task;
			}
		}

		private sealed class RecordingBackgroundTask : BackgroundTask
		{
			public int CancelCount { get; private set; }

			public override void Cancel()
			{
				CancelCount++;
				base.Cancel();
			}

			public void Complete()
			{
				Status = TaskStatus.Complete;
				OnTaskEnded(new TaskEndedEventArgs(TaskStatus.Complete, null, null));
			}

			public void Incomplete()
			{
				Status = TaskStatus.Incomplete;
				OnTaskEnded(new TaskEndedEventArgs(TaskStatus.Incomplete, null, null));
			}
		}
	}
}
