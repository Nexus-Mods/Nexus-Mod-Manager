using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client;
using Nexus.Client.BackgroundTasks;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModAuthoring;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionManualAcquisitionTests
	{
		private const string NexusArtifactId = "skyrimspecialedition/100/200";
		private const string ManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void CreatePendingAction_NexusArtifact_OffersBrowserNxmAndLocalFileWithoutTemporaryAuthorization()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionAcquisitionRequest request = CreateRequest("nexus-mod-file", NexusArtifactId, null);

				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(request);

				Assert.That(pending.ActionId, Is.EqualTo(request.RequestId));
				Assert.That(pending.Supports(CollectionManualAcquisitionActionKind.Browser), Is.True);
				Assert.That(pending.Supports(CollectionManualAcquisitionActionKind.Nxm), Is.True);
				Assert.That(pending.Supports(CollectionManualAcquisitionActionKind.LocalFile), Is.True);
				Assert.That(pending.BrowserUri.AbsoluteUri,
					Is.EqualTo("https://www.nexusmods.com/skyrimspecialedition/mods/100?tab=files&file_id=200"));
				Assert.That(pending.BrowserUri.Query, Does.Not.Contain("key="));
				Assert.That(pending.BrowserUri.Query, Does.Not.Contain("expires="));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CreatePendingAction_NonNexusExpectedHash_OffersLocalFileOnly()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionAcquisitionRequest request = CreateRequest(
					"manual-provider", "artifact-1", CollectionContentHash.FromSha256(Sha256(Encoding.UTF8.GetBytes("expected"))));

				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(request);

				Assert.That(pending.AllowedActions, Is.EqualTo(CollectionManualAcquisitionActionKind.LocalFile));
				Assert.That(pending.BrowserUri, Is.Null);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void CreatePendingAction_UnverifiableNonNexusArtifact_IsRejected()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionAcquisitionRequest request = CreateRequest("manual-provider", "artifact-1", null);

				Assert.Throws<InvalidOperationException>(() => context.Coordinator.CreatePendingAction(request));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void QueueReturnedNxm_SignedExactCallback_UsesExistingAddModCorrelation()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionAcquisitionRequest request = CreateRequest("nexus-mod-file", NexusArtifactId, null);
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(request);
				Uri returned = new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=temporary&expires=2147483647&user_id=42");

				CollectionAcquisitionQueueCorrelation correlation = context.Coordinator.QueueReturnedNxm(pending, returned, null);

				Assert.That(context.Queue.CallCount, Is.EqualTo(1));
				Assert.That(context.Queue.SourceUri, Is.EqualTo(returned));
				Assert.That(context.Queue.QueueOperationId, Is.EqualTo(request.RequestId));
				Assert.That(correlation.QueueOperationId, Is.EqualTo(request.RequestId));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void QueueReturnedNxm_UnsignedCallback_IsRejectedBeforeAddMod()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));

				Assert.Throws<ArgumentException>(() => context.Coordinator.QueueReturnedNxm(
					pending, new Uri("nxm://skyrimspecialedition/mods/100/files/200"), null));
				Assert.That(context.Queue.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void QueueReturnedNxm_DifferentFile_IsRejectedByExactC417IdentityCheck()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));
				Uri wrong = new Uri("nxm://skyrimspecialedition/mods/100/files/201?key=temporary&expires=2147483647&user_id=42");

				Assert.Throws<ArgumentException>(() => context.Coordinator.QueueReturnedNxm(pending, wrong, null));
				Assert.That(context.Queue.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyLocalFile_ExpectedHash_SealsAndProtectsManualBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				byte[] bytes = Encoding.UTF8.GetBytes("user supplied exact archive");
				string path = Path.Combine(root, "manual.zip");
				File.WriteAllBytes(path, bytes);
				var verifier = new RecordingVerifier { ThrowOnCall = true };
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0], verifier);
				CollectionAcquisitionRequest request = CreateRequest(
					"manual-provider", "artifact-1", CollectionContentHash.FromSha256(Sha256(bytes)));
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(request);

				CollectionVerifiedArchive verified = context.Coordinator.VerifyLocalFile(pending, path);

				Assert.That(verified, Is.Not.Null);
				Assert.That(verified.SourceKind, Is.EqualTo(CollectionVerifiedArchiveSourceKind.ManualFile));
				Assert.That(verified.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ExpectedContentHash));
				Assert.That(verified.Artifact.ContentHash.Value, Is.EqualTo(Sha256(bytes)));
				Assert.That(context.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download,
					request.RequestId.ToString("D")).Count, Is.EqualTo(1));
				Assert.That(verifier.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyLocalFile_NexusArtifactWithoutHash_UsesProviderIdentityOnImmutableBytes()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				string path = Path.Combine(root, "manual-nexus.7z");
				File.WriteAllText(path, "manual nexus exact bytes");
				var verifier = new RecordingVerifier
				{
					Handler = (artifact, stream) =>
					{
						Assert.That(artifact.StableId, Is.EqualTo(NexusArtifactId));
						return true;
					}
				};
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0], verifier);
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));

				CollectionVerifiedArchive verified = context.Coordinator.VerifyLocalFile(pending, path);

				Assert.That(verified, Is.Not.Null);
				Assert.That(verified.SourceKind, Is.EqualTo(CollectionVerifiedArchiveSourceKind.ManualFile));
				Assert.That(verified.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ProviderContentIdentity));
				Assert.That(verifier.CallCount, Is.EqualTo(1));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyLocalFile_WrongHash_ReturnsNullAndLeavesNoVerifiedReference()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				string path = Path.Combine(root, "wrong.zip");
				File.WriteAllText(path, "wrong bytes");
				byte[] expected = Encoding.UTF8.GetBytes("expected bytes");
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionAcquisitionRequest request = CreateRequest(
					"manual-provider", "artifact-1", CollectionContentHash.FromSha256(Sha256(expected)));
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(request);

				CollectionVerifiedArchive verified = context.Coordinator.VerifyLocalFile(pending, path);

				Assert.That(verified, Is.Null);
				Assert.That(context.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download,
					request.RequestId.ToString("D")).Count, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyReturnedNxm_BeforeAddModCompletes_IsRejected()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				TestContext context = CreateContext(root, new CollectionManagedArchiveCandidate[0]);
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));
				CollectionAcquisitionQueueCorrelation correlation = context.Coordinator.QueueReturnedNxm(
					pending, new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=temporary&expires=2147483647&user_id=42"), null);

				Assert.Throws<InvalidOperationException>(() => context.Coordinator.VerifyReturnedNxm(
					pending, correlation, CancellationToken.None));
				Assert.That(context.ArchiveSource.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyReturnedNxm_CompletedAddMod_ReverifiesManagedArchiveBeforeSatisfaction()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				string archivePath = Path.Combine(root, "downloaded.7z");
				File.WriteAllText(archivePath, "downloaded exact nexus bytes");
				var candidate = new CollectionManagedArchiveCandidate("nexus-mod-file", NexusArtifactId, archivePath);
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				TestContext context = CreateContext(root, new[] { candidate }, verifier);
				CollectionManualAcquisitionPendingAction pending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));
				CollectionAcquisitionQueueCorrelation correlation = context.Coordinator.QueueReturnedNxm(
					pending, new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=temporary&expires=2147483647&user_id=42"), null);
				context.Queue.Task.Complete();

				CollectionVerifiedArchive verified = context.Coordinator.VerifyReturnedNxm(
					pending, correlation, CancellationToken.None);

				Assert.That(verified, Is.Not.Null);
				Assert.That(verified.SourceKind, Is.EqualTo(CollectionVerifiedArchiveSourceKind.ManagedArchive));
				Assert.That(verified.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ProviderContentIdentity));
				Assert.That(verifier.CallCount, Is.EqualTo(1));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void VerifyReturnedNxm_SharedSecondConsumer_AcceptsProducerOperationOwnedByFirstRequest()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				string archivePath = Path.Combine(root, "shared-downloaded.7z");
				File.WriteAllText(archivePath, "shared downloaded exact nexus bytes");
				var candidate = new CollectionManagedArchiveCandidate("nexus-mod-file", NexusArtifactId, archivePath);
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				TestContext context = CreateContext(root, new[] { candidate }, verifier);
				CollectionManualAcquisitionPendingAction firstPending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));
				CollectionManualAcquisitionPendingAction secondPending = context.Coordinator.CreatePendingAction(
					CreateRequest("nexus-mod-file", NexusArtifactId, null));
				CollectionAcquisitionQueueCorrelation first = context.Coordinator.QueueReturnedNxm(
					firstPending, new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=first&expires=2147483647&user_id=42"), null);
				CollectionAcquisitionQueueCorrelation second = context.Coordinator.QueueReturnedNxm(
					secondPending, new Uri("nxm://skyrimspecialedition/mods/100/files/200?key=second&expires=2147483647&user_id=42"), null);
				context.Queue.Task.Complete();

				CollectionVerifiedArchive verified = context.Coordinator.VerifyReturnedNxm(
					secondPending, second, CancellationToken.None);

				Assert.That(context.Queue.CallCount, Is.EqualTo(1));
				Assert.That(second.QueueOperationId, Is.EqualTo(first.QueueOperationId));
				Assert.That(second.QueueOperationId, Is.Not.EqualTo(secondPending.Request.RequestId));
				Assert.That(verified, Is.Not.Null);
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static TestContext CreateContext(string root, IReadOnlyList<CollectionManagedArchiveCandidate> candidates)
		{
			return CreateContext(root, candidates, new RecordingVerifier { Handler = (artifact, stream) => false });
		}

		private static TestContext CreateContext(string root, IReadOnlyList<CollectionManagedArchiveCandidate> candidates,
			RecordingVerifier verifier)
		{
			var store = new CollectionsStore(root);
			store.CreateNew();
			var artifacts = new CollectionsRetainedArtifactStore(store);
			var references = new CollectionsRetainedArtifactReferenceStore(store);
			var source = new RecordingArchiveSource(candidates);
			var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, artifacts, references);
			var queue = new RecordingQueue();
			var coordinator = new CollectionManualAcquisitionCoordinator(
				new CollectionAcquisitionRequestCoordinator(queue), adopter);
			return new TestContext(coordinator, queue, source, references);
		}

		private static CollectionAcquisitionRequest CreateRequest(string scheme, string stableArtifactId,
			CollectionContentHash expectedHash)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c4-20");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c4-20", 20);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(ManifestHash), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-c4-20")),
				CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected,
				new CollectionArtifactReference(scheme, stableArtifactId, expectedHash),
				CollectionRecipeIdentity.FromFingerprint("recipe-c4-20"),
				"C4.20 member");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(
				member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"), 1),
				CollectionTargetIdentity.FromFingerprint("target-c4-20"),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-c4-20"),
				CollectionCapabilityReport.Create(manifest),
				new[] { memberPlan });
			return CollectionAcquisitionRequest.Create(Guid.NewGuid(), plan, memberPlan.MemberKey);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmCollectionsC420-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		private static string Sha256(byte[] bytes)
		{
			using (SHA256 sha = SHA256.Create())
			{
				byte[] digest = sha.ComputeHash(bytes);
				var builder = new StringBuilder(digest.Length * 2);
				for (int index = 0; index < digest.Length; index++)
					builder.Append(digest[index].ToString("x2"));
				return builder.ToString();
			}
		}

		private sealed class TestContext
		{
			public TestContext(CollectionManualAcquisitionCoordinator coordinator, RecordingQueue queue,
				RecordingArchiveSource archiveSource, CollectionsRetainedArtifactReferenceStore references)
			{
				Coordinator = coordinator;
				Queue = queue;
				ArchiveSource = archiveSource;
				References = references;
			}

			public CollectionManualAcquisitionCoordinator Coordinator { get; }
			public RecordingQueue Queue { get; }
			public RecordingArchiveSource ArchiveSource { get; }
			public CollectionsRetainedArtifactReferenceStore References { get; }
		}

		private sealed class RecordingQueue : ICollectionAddModQueue
		{
			public RecordingQueue()
			{
				Task = new TestBackgroundTask();
			}

			public int CallCount { get; private set; }
			public Uri SourceUri { get; private set; }
			public Guid QueueOperationId { get; private set; }
			public TestBackgroundTask Task { get; }

			public IBackgroundTask Queue(Uri sourceUri, ConfirmOverwriteCallback confirmOverwriteCallback, Guid queueOperationId)
			{
				CallCount++;
				SourceUri = sourceUri;
				QueueOperationId = queueOperationId;
				return Task;
			}
		}

		private sealed class TestBackgroundTask : BackgroundTask
		{
			public void Complete()
			{
				Status = TaskStatus.Complete;
				OnTaskEnded(new TaskEndedEventArgs(TaskStatus.Complete, null, null));
			}
		}

		private sealed class RecordingArchiveSource : ICollectionManagedArchiveSource
		{
			private readonly IReadOnlyList<CollectionManagedArchiveCandidate> _candidates;

			public RecordingArchiveSource(IReadOnlyList<CollectionManagedArchiveCandidate> candidates)
			{
				_candidates = candidates;
			}

			public int CallCount { get; private set; }

			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				CallCount++;
				return _candidates;
			}
		}

		private sealed class RecordingVerifier : ICollectionArchiveIdentityVerifier
		{
			public int CallCount { get; private set; }
			public bool ThrowOnCall { get; set; }
			public Func<CollectionArtifactReference, Stream, bool> Handler { get; set; }

			public bool IsExactMatch(CollectionArtifactReference requestedArtifact, Stream immutableArchive,
				CancellationToken cancellationToken)
			{
				CallCount++;
				if (ThrowOnCall)
					throw new AssertionException("Provider verification should not have been required.");
				return Handler != null && Handler(requestedArtifact, immutableArchive);
			}
		}
	}
}
