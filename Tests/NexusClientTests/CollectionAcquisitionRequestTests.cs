using System;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.ModAuthoring;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionAcquisitionRequestTests
	{
		private const string Sha256A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void Create_BindsExactSelectedMemberAndPlanIdentity()
		{
			ResolvedCollectionPlan plan = CreatePlan("skyrimspecialedition/100/200");
			Guid requestId = Guid.Parse("11111111-2222-3333-4444-555555555555");
			CollectionAcquisitionRequest request = CollectionAcquisitionRequest.Create(requestId, plan, plan.SelectedMembers[0].MemberKey);

			Assert.That(request.RequestId, Is.EqualTo(requestId));
			Assert.That(request.PlanIdentity, Is.SameAs(plan.Identity));
			Assert.That(request.Revision, Is.SameAs(plan.Revision));
			Assert.That(request.Target, Is.SameAs(plan.Target));
			Assert.That(request.MemberKey, Is.EqualTo(plan.SelectedMembers[0].MemberKey));
			Assert.That(request.SelectedArtifact, Is.EqualTo(plan.SelectedMembers[0].ArtifactChoice.SelectedArtifact));
			Assert.That(request.RecipeIdentity, Is.EqualTo(plan.SelectedMembers[0].RecipeIdentity));
		}

		[Test]
		public void Create_RejectsMemberOutsideResolvedSelectedClosure()
		{
			ResolvedCollectionPlan plan = CreatePlan("skyrimspecialedition/100/200");
			Assert.Throws<ArgumentException>(() => CollectionAcquisitionRequest.Create(
				Guid.NewGuid(), plan, CollectionMemberKey.FromProvider("another-member")));
		}

		[Test]
		public void Queue_ExactNexusSource_UsesExistingQueueWithStableCorrelation()
		{
			CollectionAcquisitionRequest request = CreateRequest("skyrimspecialedition/100/200");
			RecordingQueue queue = new RecordingQueue();
			CollectionAcquisitionRequestCoordinator coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			Uri source = new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=temporary&expires=123&user_id=7");

			CollectionAcquisitionQueueCorrelation correlation = coordinator.Queue(request, source, null);

			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(queue.SourceUri, Is.EqualTo(source));
			Assert.That(queue.QueueOperationId, Is.EqualTo(request.RequestId));
			Assert.That(correlation.Request, Is.SameAs(request));
			Assert.That(correlation.QueueOperationId, Is.EqualTo(queue.QueueOperationId));
			Assert.That(correlation.Task, Is.Not.SameAs(queue.Task));
			Assert.That(correlation.Task.Status, Is.EqualTo(queue.Task.Status));
			Assert.That(typeof(CollectionAcquisitionQueueCorrelation).GetProperty("SourceUri"), Is.Null,
				"Temporary NXM authorization must not become acquisition correlation state.");
		}

		[Test]
		public void Queue_WrongNexusFile_IsRejectedBeforeNativeQueue()
		{
			CollectionAcquisitionRequest request = CreateRequest("skyrimspecialedition/100/200");
			RecordingQueue queue = new RecordingQueue();
			CollectionAcquisitionRequestCoordinator coordinator = new CollectionAcquisitionRequestCoordinator(queue);

			Assert.Throws<ArgumentException>(() => coordinator.Queue(
				request, new Uri("nxm://skyrimspecialedition/mods/100/files/201"), null));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_DirectHttpSource_IsRejectedBeforeNativeQueue()
		{
			CollectionAcquisitionRequest request = CreateRequest("skyrimspecialedition/100/200");
			RecordingQueue queue = new RecordingQueue();
			CollectionAcquisitionRequestCoordinator coordinator = new CollectionAcquisitionRequestCoordinator(queue);

			Assert.Throws<ArgumentException>(() => coordinator.Queue(
				request, new Uri("https://cdn.example.invalid/signed/archive.7z?token=secret"), null));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_LocalFileCandidate_IsReservedForManualVerificationStage()
		{
			CollectionAcquisitionRequest request = CreateRequest("skyrimspecialedition/100/200");
			RecordingQueue queue = new RecordingQueue();
			CollectionAcquisitionRequestCoordinator coordinator = new CollectionAcquisitionRequestCoordinator(queue);

			Assert.Throws<ArgumentException>(() => coordinator.Queue(
				request, new Uri("file:///C:/NMM-Test/archive.7z"), null));
			Assert.That(queue.CallCount, Is.EqualTo(0));
		}

		[Test]
		public void Queue_RepeatedSameRequest_UsesSameQueueOperationIdentity()
		{
			CollectionAcquisitionRequest request = CreateRequest("skyrimspecialedition/100/200");
			RecordingQueue queue = new RecordingQueue();
			CollectionAcquisitionRequestCoordinator coordinator = new CollectionAcquisitionRequestCoordinator(queue);
			Uri source = new Uri("nxm://skyrimspecialedition/mods/100/files/200");

			CollectionAcquisitionQueueCorrelation first = coordinator.Queue(request, source, null);
			CollectionAcquisitionQueueCorrelation second = coordinator.Queue(request, source, null);

			Assert.That(first.QueueOperationId, Is.EqualTo(request.RequestId));
			Assert.That(second.QueueOperationId, Is.EqualTo(request.RequestId));
			Assert.That(queue.CallCount, Is.EqualTo(1));
			Assert.That(second, Is.SameAs(first));
		}

		[Test]
		public void AddModTask_ExplicitQueueOperationId_IsObservableAndImmutable()
		{
			Guid queueOperationId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
			AddModTask task = new AddModTask(null, null, null, null, null, null,
				new Uri("file:///C:/NMM-Test/archive.7z"), null, null, null, queueOperationId);
			try
			{
				Assert.That(task.QueueOperationId, Is.EqualTo(queueOperationId));
				Assert.That(typeof(AddModTask).GetProperty("QueueOperationId").GetSetMethod(), Is.Null);
			}
			finally
			{
				task.Dispose();
			}
		}

		private static CollectionAcquisitionRequest CreateRequest(string stableArtifactId)
		{
			ResolvedCollectionPlan plan = CreatePlan(stableArtifactId);
			return CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, plan.SelectedMembers[0].MemberKey);
		}

		private static ResolvedCollectionPlan CreatePlan(string stableArtifactId)
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
				new CollectionArtifactReference("nexus-mod-file", stableArtifactId, null),
				CollectionRecipeIdentity.FromFingerprint("recipe-1"),
				"Member 1");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			CollectionCapabilityReport report = CollectionCapabilityReport.Create(manifest);
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(
				member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			return new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"), 1),
				CollectionTargetIdentity.FromFingerprint("target-1"),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-1"),
				report,
				new[] { memberPlan });
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			public RecordingQueue()
			{
				Task = new BackgroundTask();
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
