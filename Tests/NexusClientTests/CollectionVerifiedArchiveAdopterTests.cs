using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using NUnit.Framework;

namespace NexusClientTests
{
	[TestFixture]
	public class CollectionVerifiedArchiveAdopterTests
	{
		private const string StableArtifactId = "skyrimspecialedition/100/200";
		private const string ManifestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

		[Test]
		public void TryAdopt_ExpectedHash_ReusesExistingRetainedArtifactWithoutManagedScan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				byte[] bytes = Encoding.UTF8.GetBytes("already retained exact archive");
				CollectionsRetainedArtifact retained;
				using (var stream = new MemoryStream(bytes, false))
					retained = stores.Artifacts.Publish(stream);

				CollectionAcquisitionRequest request = CreateRequest(retained.ContentHash);
				var source = new RecordingArchiveSource(new CollectionManagedArchiveCandidate[0]) { ThrowOnFind = true };
				var verifier = new RecordingVerifier { ThrowOnCall = true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionVerifiedArchive result = adopter.TryAdopt(request);

				Assert.That(result, Is.Not.Null);
				Assert.That(result.Artifact, Is.EqualTo(retained));
				Assert.That(result.SourceKind, Is.EqualTo(CollectionVerifiedArchiveSourceKind.RetainedContent));
				Assert.That(result.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ExpectedContentHash));
				Assert.That(result.Reference.OwnerKind, Is.EqualTo(CollectionsRetainedArtifactOwnerKind.Download));
				Assert.That(result.Reference.OwnerId, Is.EqualTo(request.RequestId.ToString("D")));
				Assert.That(source.CallCount, Is.EqualTo(0));
				Assert.That(verifier.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_ManagedArchive_ProviderVerifiesImmutableRetainedSnapshot()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate.7z");
				byte[] original = Encoding.ASCII.GetBytes("ORIGINAL-CONTENT");
				byte[] replacement = Encoding.ASCII.GetBytes("REPLACED-CONTENT");
				File.WriteAllBytes(archivePath, original);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier
				{
					Handler = (artifact, stream) =>
					{
						File.WriteAllBytes(archivePath, replacement);
						CollectionAssert.AreEqual(original, ReadAllBytes(stream));
						return true;
					}
				};
				CollectionAcquisitionRequest request = CreateRequest(null);
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionVerifiedArchive result = adopter.TryAdopt(request);

				Assert.That(result, Is.Not.Null);
				Assert.That(result.SourceKind, Is.EqualTo(CollectionVerifiedArchiveSourceKind.ManagedArchive));
				Assert.That(result.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ProviderContentIdentity));
				Assert.That(result.Artifact.ContentHash.Value, Is.EqualTo(Sha256(original)));
				Assert.That(verifier.CallCount, Is.EqualTo(1));
				using (Stream retained = stores.Artifacts.OpenRead(result.Artifact.ArtifactId))
					CollectionAssert.AreEqual(original, ReadAllBytes(retained));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_WrongRepositoryIdentity_IsIgnoredEvenWhenFilenameLooksUsable()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "same-name-as-request.7z");
				File.WriteAllText(archivePath, "wrong nexus file");
				var source = new RecordingArchiveSource(new[]
				{
					new CollectionManagedArchiveCandidate("nexus-mod-file", "skyrimspecialedition/100/201", archivePath)
				});
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionVerifiedArchive result = adopter.TryAdopt(CreateRequest(null));

				Assert.That(result, Is.Null);
				Assert.That(verifier.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_ProviderDoesNotRecognizeSealedBytes_ReturnsNullAndLeavesNoReference()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate.zip");
				File.WriteAllText(archivePath, "not the expected nexus bytes");
				CollectionAcquisitionRequest request = CreateRequest(null);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => false };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionVerifiedArchive result = adopter.TryAdopt(request);

				Assert.That(result, Is.Null);
				Assert.That(verifier.CallCount, Is.EqualTo(1));
				Assert.That(stores.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download,
					request.RequestId.ToString("D")).Count, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_ExpectedHashRejectsChangedBytesWithSameLengthAndTimestamp()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "mutable.zip");
				byte[] original = Encoding.ASCII.GetBytes("ABCDEF");
				byte[] changed = Encoding.ASCII.GetBytes("UVWXYZ");
				File.WriteAllBytes(archivePath, original);
				DateTime timestamp = File.GetLastWriteTimeUtc(archivePath);
				CollectionContentHash expectedHash = CollectionContentHash.FromSha256(Sha256(original));
				File.WriteAllBytes(archivePath, changed);
				File.SetLastWriteTimeUtc(archivePath, timestamp);

				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier { ThrowOnCall = true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionVerifiedArchive result = adopter.TryAdopt(CreateRequest(expectedHash));

				Assert.That(result, Is.Null, "Length/timestamp freshness must never substitute for the expected content digest.");
				Assert.That(verifier.CallCount, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_ProviderVerificationFailure_ReleasesTemporaryReference()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate-failure.7z");
				File.WriteAllText(archivePath, "provider verification throws");
				CollectionAcquisitionRequest request = CreateRequest(null);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier
				{
					Handler = (artifact, stream) => { throw new InvalidOperationException("verification unavailable"); }
				};
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				Assert.Throws<InvalidOperationException>(() => adopter.TryAdopt(request));
				Assert.That(stores.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download,
					request.RequestId.ToString("D")).Count, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_RepeatedSameRequest_ReusesVerifiedReferenceWithoutRehashingManagedSource()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate.7z");
				File.WriteAllText(archivePath, "verified once");
				CollectionAcquisitionRequest request = CreateRequest(null);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionVerifiedArchive first = adopter.TryAdopt(request);
				source.ThrowOnFind = true;
				verifier.ThrowOnCall = true;
				CollectionVerifiedArchive second = adopter.TryAdopt(request);

				Assert.That(second.Artifact, Is.EqualTo(first.Artifact));
				Assert.That(second.Reference.ReferenceId, Is.EqualTo(first.Reference.ReferenceId));
				Assert.That(second.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ExistingVerifiedReference));
				Assert.That(source.CallCount, Is.EqualTo(1));
				Assert.That(verifier.CallCount, Is.EqualTo(1));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_TwoProviderVerifiedCandidatesWithDifferentBytes_FailsClosed()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string firstPath = Path.Combine(root, "first.7z");
				string secondPath = Path.Combine(root, "second.7z");
				File.WriteAllText(firstPath, "first exact claim");
				File.WriteAllText(secondPath, "second contradictory claim");
				CollectionAcquisitionRequest request = CreateRequest(null);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(firstPath), ExactCandidate(secondPath) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				Assert.Throws<InvalidDataException>(() => adopter.TryAdopt(request));
				Assert.That(stores.References.GetReferencesForOwner(CollectionsRetainedArtifactOwnerKind.Download,
					request.RequestId.ToString("D")).Count, Is.EqualTo(0));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		[Test]
		public void TryAdopt_RequestIdReboundToDifferentArtifactIdentity_IsRejected()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate.7z");
				File.WriteAllText(archivePath, "verified identity binding");
				Guid requestId = Guid.Parse("bbbbbbbb-1111-2222-3333-cccccccccccc");
				CollectionAcquisitionRequest firstRequest = CreateRequest(null, StableArtifactId, requestId);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);
				Assert.That(adopter.TryAdopt(firstRequest), Is.Not.Null);

				CollectionAcquisitionRequest rebound = CreateRequest(null, "skyrimspecialedition/100/201", requestId);
				Assert.Throws<InvalidDataException>(() => adopter.TryAdopt(rebound));
			}
			finally
			{
				Directory.Delete(root, true);
			}
		}

		private static Stores CreateStores(string root)
		{
			var featureStore = new CollectionsStore(root);
			featureStore.CreateNew();
			return new Stores(new CollectionsRetainedArtifactStore(featureStore),
				new CollectionsRetainedArtifactReferenceStore(featureStore));
		}

		private static CollectionManagedArchiveCandidate ExactCandidate(string path)
		{
			return new CollectionManagedArchiveCandidate("nexus-mod-file", StableArtifactId, path);
		}

		private static CollectionAcquisitionRequest CreateRequest(CollectionContentHash expectedHash)
		{
			return CreateRequest(expectedHash, StableArtifactId, Guid.NewGuid());
		}

		private static CollectionAcquisitionRequest CreateRequest(CollectionContentHash expectedHash, string stableArtifactId, Guid requestId)
		{
			CollectionIdentity collection = CollectionIdentity.FromNexus("collection-c4-18");
			CollectionRevisionIdentity revision = CollectionRevisionIdentity.FromNexus(collection, "revision-c4-18", 18);
			CollectionManifestSourceSnapshot source = new CollectionManifestSourceSnapshot(
				CollectionContentHash.FromSha256(ManifestHash), 100, "schema-v1", "normalizer-v1");
			NormalizedCollectionMember member = new NormalizedCollectionMember(
				0,
				CollectionMemberIdentityResolution.Resolved(CollectionMemberKey.FromProvider("member-c4-18")),
				CollectionMemberRequirement.Required,
				CollectionMemberSelection.Selected,
				new CollectionArtifactReference("nexus-mod-file", stableArtifactId, expectedHash),
				CollectionRecipeIdentity.FromFingerprint("recipe-c4-18"),
				"C4.18 member");
			NormalizedCollectionManifest manifest = new NormalizedCollectionManifest(
				revision, source, CollectionManifestMemberSetCompleteness.Complete, null, new[] { member });
			ResolvedCollectionMemberPlan memberPlan = new ResolvedCollectionMemberPlan(
				member, CollectionResolvedArtifactChoice.Exact(member.Artifact));
			ResolvedCollectionPlan plan = new ResolvedCollectionPlan(
				CollectionPlanIdentity.From(Guid.Parse("aaaaaaaa-1111-2222-3333-bbbbbbbbbbbb"), 1),
				CollectionTargetIdentity.FromFingerprint("target-c4-18"),
				CollectionExecutionPolicy.InstallIntoCurrentSetup(),
				new CollectionCurrentStateFingerprint("managed-state-v1", "state-c4-18"),
				CollectionCapabilityReport.Create(manifest),
				new[] { memberPlan });
			return CollectionAcquisitionRequest.Create(requestId, plan, memberPlan.MemberKey);
		}

		private static string CreateTemporaryDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "NmmCollectionsC418-" + Guid.NewGuid().ToString("N"));
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

		private static byte[] ReadAllBytes(Stream stream)
		{
			using (var output = new MemoryStream())
			{
				stream.CopyTo(output);
				return output.ToArray();
			}
		}

		private sealed class Stores
		{
			public Stores(CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references)
			{
				Artifacts = artifacts;
				References = references;
			}

			public CollectionsRetainedArtifactStore Artifacts { get; }
			public CollectionsRetainedArtifactReferenceStore References { get; }
		}

		private sealed class RecordingArchiveSource : ICollectionManagedArchiveSource
		{
			private readonly IReadOnlyList<CollectionManagedArchiveCandidate> _candidates;

			public RecordingArchiveSource(IReadOnlyList<CollectionManagedArchiveCandidate> candidates)
			{
				_candidates = candidates;
			}

			public int CallCount { get; private set; }
			public bool ThrowOnFind { get; set; }

			public IReadOnlyList<CollectionManagedArchiveCandidate> FindCandidates(CollectionArtifactReference requestedArtifact)
			{
				CallCount++;
				if (ThrowOnFind)
					throw new AssertionException("Managed archive discovery should not have been called.");
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
					throw new AssertionException("Provider archive verification should not have been called.");
				return Handler != null && Handler(requestedArtifact, immutableArchive);
			}
		}
	}
}
