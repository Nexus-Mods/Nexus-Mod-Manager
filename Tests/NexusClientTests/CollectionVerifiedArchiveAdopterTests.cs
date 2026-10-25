using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModRepositories;
using Nexus.Client.OnlineServices.NexusMods.Collections;
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
		public void TryAdopt_ManagedArchive_ProviderVerifiesLibraryReferenceWithoutCopying()
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
				File.WriteAllBytes(archivePath, replacement);
				Assert.IsFalse(stores.Artifacts.VerifyArtifact(result.Artifact.ArtifactId));
				verifier.Handler = (artifact, stream) => Sha256(ReadAllBytes(stream)) == Sha256(original);
				Assert.IsNull(adopter.TryAdopt(request), "A changed library archive is reacquired rather than replaced by a hidden snapshot.");
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
		public void TryAdopt_DifferentRequestForSameProviderArtifact_ReusesPriorVerifiedBytesWithoutManagedRescan()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate-cross-plan.7z");
				File.WriteAllText(archivePath, "provider verified once across plan requests");
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);

				CollectionAcquisitionRequest firstRequest = CreateRequest(null);
				CollectionVerifiedArchive first = adopter.TryAdopt(firstRequest);
				source.ThrowOnFind = true;
				verifier.ThrowOnCall = true;
				CollectionAcquisitionRequest secondRequest = CreateRequest(null);
				CollectionVerifiedArchive second = adopter.TryAdopt(secondRequest);

				Assert.That(second, Is.Not.Null);
				Assert.That(second.Artifact, Is.EqualTo(first.Artifact));
				Assert.That(second.Reference.OwnerId, Is.EqualTo(secondRequest.RequestId.ToString("D")));
				Assert.That(second.Reference.ReferenceId, Is.Not.EqualTo(first.Reference.ReferenceId));
				Assert.That(second.SourceKind, Is.EqualTo(CollectionVerifiedArchiveSourceKind.RetainedContent));
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
		public void TryAdopt_DurableVerifiedAcquisitionRecord_ReusesBytesAfterOldRequestReferenceIsReleased()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate-durable-reuse.7z");
				File.WriteAllText(archivePath, "provider verified durable acquisition result");
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References, stores.Acquisitions);

				CollectionAcquisitionRequest firstRequest = CreateRequest(null);
				CollectionVerifiedArchive first = adopter.TryAdopt(firstRequest);
				Assert.That(stores.References.ReleaseReference(first.Reference.ReferenceId), Is.True);
				source.ThrowOnFind = true;
				verifier.ThrowOnCall = true;

				CollectionAcquisitionRequest secondRequest = CreateRequest(null);
				CollectionVerifiedArchive second = adopter.TryAdopt(secondRequest);

				Assert.That(second, Is.Not.Null);
				Assert.That(second.Artifact, Is.EqualTo(first.Artifact));
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

				CollectionArchiveIdentityConflictException conflict = Assert.Throws<CollectionArchiveIdentityConflictException>(() => adopter.TryAdopt(request));
				Assert.That(conflict.FirstPath, Is.EqualTo(firstPath));
				Assert.That(conflict.SecondPath, Is.EqualTo(secondPath));
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

		[TestCase(false)]
		[TestCase(true)]
		public void TryAdopt_AutomaticModeChoosesStableExactArchiveWithOrWithoutSavedHash(bool cached)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string selectedPath = Path.Combine(root, "b-selected.7z");
				string otherPath = Path.Combine(root, "c-other.7z");
				string fallbackPath = Path.Combine(root, "a-legacy.7z");
				File.WriteAllText(selectedPath, "selected exact identity");
				File.WriteAllText(otherPath, "different bytes with same metadata");
				File.WriteAllText(fallbackPath, "legacy mod ID only");
				if (cached)
				{
					stores.Artifacts.ReferenceArchiveFileWithoutMd5(selectedPath, CancellationToken.None);
					stores.Artifacts.ReferenceArchiveFileWithoutMd5(otherPath, CancellationToken.None);
				}
				var source = new RecordingArchiveSource(new[]
				{
					ExactCandidate(otherPath),
					new CollectionManagedArchiveCandidate("nexus-mod-file", StableArtifactId, fallbackPath, true),
					ExactCandidate(selectedPath)
				});
				var adopter = new CollectionVerifiedArchiveAdopter(source, new RecordingVerifier { ThrowOnCall = true },
					stores.Artifacts, stores.References, stores.Acquisitions);
				CollectionVerifiedArchive result = adopter.TryAdopt(CreateRequest(null), CancellationToken.None, true, true);
				Assert.That(result.Artifact.ContentHash.Value, Is.EqualTo(Sha256(File.ReadAllBytes(selectedPath))));
				Assert.That(result.VerificationBasis, Is.EqualTo(cached
					? CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash : CollectionArchiveVerificationBasis.ManagedArchiveMetadata));
				Assert.That(stores.Artifacts.GetArtifact(CollectionContentHash.FromSha256(Sha256(File.ReadAllBytes(fallbackPath)))), Is.Null);
				if (!cached)
					Assert.That(stores.Artifacts.GetArtifact(CollectionContentHash.FromSha256(Sha256(File.ReadAllBytes(otherPath)))), Is.Null);
				Assert.That(stores.Acquisitions.GetVerifiedArtifactIds(result.Request.SelectedArtifact), Is.Empty);
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void TryAdopt_AutomaticModeStillRequiresProviderVerificationWithoutHashSkip()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string rejectedPath = Path.Combine(root, "a-rejected.7z");
				string selectedPath = Path.Combine(root, "b-selected.7z");
				File.WriteAllText(rejectedPath, "incorrect provider file");
				File.WriteAllText(selectedPath, "verified provider file");
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => Encoding.UTF8.GetString(ReadAllBytes(stream)) == "verified provider file" };
				var adopter = new CollectionVerifiedArchiveAdopter(new RecordingArchiveSource(new[] { ExactCandidate(selectedPath), ExactCandidate(rejectedPath) }),
					verifier, stores.Artifacts, stores.References);
				CollectionVerifiedArchive result = adopter.TryAdopt(CreateRequest(null), CancellationToken.None, false, true);
				Assert.That(result.Artifact.ContentHash.Value, Is.EqualTo(Sha256(File.ReadAllBytes(selectedPath))));
				Assert.That(verifier.CallCount, Is.EqualTo(2));
				Assert.That(result.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ProviderContentIdentity));
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>A retry rebinds the same unchanged archive without rediscovering the entire managed library.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void TryAdopt_RetryReusesUnchangedArchiveWithoutLibraryDiscovery(bool metadataTrust)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string path = Path.Combine(root, "existing.7z");
				File.WriteAllBytes(path, Encoding.UTF8.GetBytes("existing library archive"));
				var source = new RecordingArchiveSource(new[] { new CollectionManagedArchiveCandidate("nexus-mod-file", StableArtifactId, path, false) });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References, stores.Acquisitions);
				CollectionVerifiedArchive first = adopter.TryAdopt(CreateRequest(null), CancellationToken.None, metadataTrust);
				Assert.That(first, Is.Not.Null);
				int discoveries = source.CallCount;
				int verifications = verifier.CallCount;
				source.ThrowOnFind = true;
				CollectionVerifiedArchive retry = adopter.TryAdopt(CreateRequest(null), CancellationToken.None, metadataTrust);
				Assert.That(retry.Artifact, Is.EqualTo(first.Artifact));
				Assert.That(source.CallCount, Is.EqualTo(discoveries));
				Assert.That(verifier.CallCount, Is.EqualTo(verifications));
				if (metadataTrust) Assert.That(retry.VerificationBasis, Is.EqualTo(first.VerificationBasis));
			}
			finally { Directory.Delete(root, true); }
		}

		[TestCase(true, false, 0)]
		[TestCase(false, false, 1)]
		[TestCase(true, true, 1)]
		public void TryAdopt_SkipMd5RequiresOptInAndExactLibraryMetadata(bool skipMd5, bool fallback, int expectedCalls)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string path = Path.Combine(root, "existing.7z");
				byte[] bytes = Encoding.UTF8.GetBytes("existing library archive");
				File.WriteAllBytes(path, bytes);
				var candidate = new CollectionManagedArchiveCandidate("nexus-mod-file", StableArtifactId, path, fallback);
				var source = new RecordingArchiveSource(new[] { candidate });
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References, stores.Acquisitions);
				CollectionAcquisitionRequest request = CreateRequest(null);

				CollectionVerifiedArchive result = adopter.TryAdopt(request, CancellationToken.None, skipMd5);

				Assert.That(result, Is.Not.Null);
				Assert.That(verifier.CallCount, Is.EqualTo(expectedCalls));
				Assert.That(result.Artifact.ContentHash.Value, Is.EqualTo(Sha256(bytes)));
				string md5;
				Assert.That(stores.Artifacts.TryGetPublishedMd5(result.Artifact.ArtifactId, out md5), Is.EqualTo(expectedCalls > 0));
				Assert.That(result.VerificationBasis, Is.EqualTo(expectedCalls == 0
					? CollectionArchiveVerificationBasis.ManagedArchiveMetadata : CollectionArchiveVerificationBasis.ProviderContentIdentity));
				if (expectedCalls == 0)
				{
					CollectionVerifiedArchive rebound = adopter.RebindVerified(result, CreateRequest(null));
					Assert.That(rebound.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ManagedArchiveMetadata));
					Assert.That(stores.Acquisitions.GetVerifiedArtifactIds(request.SelectedArtifact), Is.Empty);
					verifier.Handler = (artifact, stream) => false;
					Assert.That(adopter.TryAdopt(CreateRequest(null)), Is.Null, "Disabling the opt-in must restore provider verification.");
					Assert.That(verifier.CallCount, Is.EqualTo(1));
				}
			}
			finally { Directory.Delete(root, true); }
		}

		[TestCase(false)]
		[TestCase(true)]
		public void TryAdopt_ManifestSizeRejectsOlderReleaseEvenWhenMetadataClaimsExactFile(bool cached)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string olderPath = Path.Combine(root, "a-older-release.7z");
				string requestedPath = Path.Combine(root, "b-requested-release.7z");
				File.WriteAllText(olderPath, "old");
				byte[] requestedBytes = Encoding.UTF8.GetBytes("requested mod release");
				File.WriteAllBytes(requestedPath, requestedBytes);
				if (cached)
				{
					stores.Artifacts.ReferenceArchiveFileWithoutMd5(olderPath, CancellationToken.None);
					stores.Artifacts.ReferenceArchiveFileWithoutMd5(requestedPath, CancellationToken.None);
				}
				var source = new RecordingArchiveSource(new[] { ExactCandidate(olderPath), ExactCandidate(requestedPath) });
				var verifier = new RecordingVerifier { ThrowOnCall = true };
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References, stores.Acquisitions);
				CollectionAcquisitionRequest request = CreateRequest(null, StableArtifactId, Guid.NewGuid(), requestedBytes.Length);
				CollectionVerifiedArchive result = adopter.TryAdopt(request, CancellationToken.None, true, true);

				Assert.That(result, Is.Not.Null);
				Assert.That(result.Artifact.ContentHash.Value, Is.EqualTo(Sha256(requestedBytes)));
				Assert.That(result.Artifact.ByteLength, Is.EqualTo(requestedBytes.Length));
				Assert.That(result.VerificationBasis, Is.EqualTo(cached
					? CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash : CollectionArchiveVerificationBasis.ManagedArchiveMetadata));
				Assert.That(verifier.CallCount, Is.Zero);
			}
			finally { Directory.Delete(root, true); }
		}

		[TestCase(false)]
		[TestCase(true)]
		public void TryAdopt_ManifestSizeDisqualifiesOldVerifiedResultsAndRetainsCorrectedReuse(bool sameRequest)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string path = Path.Combine(root, "older-release.7z");
				File.WriteAllText(path, "old");
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => true };
				var originalAdopter = new CollectionVerifiedArchiveAdopter(new RecordingArchiveSource(new[] { ExactCandidate(path) }),
					verifier, stores.Artifacts, stores.References, stores.Acquisitions);
				CollectionAcquisitionRequest oldRequest = CreateRequest(null);
				CollectionVerifiedArchive older = originalAdopter.TryAdopt(oldRequest);

				string requestedPath = Path.Combine(root, "requested-release.7z");
				byte[] requestedBytes = Encoding.UTF8.GetBytes("requested mod release");
				File.WriteAllBytes(requestedPath, requestedBytes);
				var source = new RecordingArchiveSource(new[] { ExactCandidate(path), ExactCandidate(requestedPath) });
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References, stores.Acquisitions);
				CollectionAcquisitionRequest request = CreateRequest(null, StableArtifactId,
					sameRequest ? oldRequest.RequestId : Guid.NewGuid(), requestedBytes.Length);
				CollectionVerifiedArchive corrected = adopter.TryAdopt(request);
				Assert.That(corrected.Artifact.ContentHash.Value, Is.EqualTo(Sha256(requestedBytes)));
				Assert.That(corrected.Artifact, Is.Not.EqualTo(older.Artifact));
				if (sameRequest) Assert.That(stores.References.GetReference(older.Reference.ReferenceId), Is.Null);

				source.ThrowOnFind = true;
				verifier.ThrowOnCall = true;
				CollectionVerifiedArchive reused = adopter.TryAdopt(CreateRequest(null, StableArtifactId, Guid.NewGuid(), requestedBytes.Length));
				Assert.That(reused.Artifact, Is.EqualTo(corrected.Artifact));
				Assert.That(verifier.CallCount, Is.EqualTo(2));
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void TryAdopt_SavedHashReuseDoesNotBecomeOrdinaryIntegrityProof()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores original = CreateStores(root);
				string path = Path.Combine(root, "existing.7z");
				byte[] bytes = Encoding.UTF8.GetBytes("original archive");
				File.WriteAllBytes(path, bytes);
				CollectionsRetainedArtifact recorded = original.Artifacts.ReferenceArchiveFileWithoutMd5(path, CancellationToken.None);
				DateTime savedWriteTime = File.GetLastWriteTimeUtc(path);
				// Preserving the stamp while changing bytes proves the opt-in reused the saved hash rather than reading the archive.
				ForgetProcessObservedHash(path);
				File.WriteAllBytes(path, Encoding.UTF8.GetBytes("modified archive"));
				File.SetLastWriteTimeUtc(path, savedWriteTime);
				var store = new CollectionsStore(root);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				var verifier = new RecordingVerifier { ThrowOnCall = true };
				var adopter = new CollectionVerifiedArchiveAdopter(new RecordingArchiveSource(new[] { ExactCandidate(path) }), verifier,
					artifacts, new CollectionsRetainedArtifactReferenceStore(store), new CollectionsAcquisitionStore(store));
				CollectionVerifiedArchive trusted = adopter.TryAdopt(CreateRequest(recorded.ContentHash), CancellationToken.None, true);
				Assert.That(trusted.Artifact, Is.EqualTo(recorded));
				Assert.That(trusted.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ManagedArchiveMetadataWithCachedHash));
				Assert.That(new CollectionsRetainedArtifactStore(store).IsTrustedPreparationArchiveCurrent(path, recorded), Is.True);
				using (CollectionsRetainedArtifactStore.BeginSavedArchiveHashReuse(true))
				{
					Assert.That(artifacts.VerifyArtifact(recorded.ArtifactId), Is.True, "Approval honors the explicitly opted-in saved hash for an unchanged archive stamp.");
					Assert.That(CollectionArchiveContentMatcher.MatchesFile(path, recorded, CancellationToken.None), Is.True,
						"Native archive matching uses the same opted-in proof without another read.");
					using (CollectionsRetainedArtifactStore.BeginSavedArchiveHashReuse(false))
						Assert.That(CollectionsRetainedArtifactStore.CanReuseSavedArchiveHashes, Is.False);
					Assert.That(CollectionsRetainedArtifactStore.CanReuseSavedArchiveHashes, Is.True);
				}
				Assert.That(CollectionsRetainedArtifactStore.CanReuseSavedArchiveHashes, Is.False);
				Assert.That(artifacts.VerifyArtifact(recorded.ArtifactId), Is.False, "Normal integrity verification must still read and reject changed bytes.");
				Assert.That(new CollectionsRetainedArtifactStore(store).IsTrustedPreparationArchiveCurrent(path, recorded), Is.False);
				Assert.That(adopter.TryAdopt(CreateRequest(recorded.ContentHash)), Is.Null, "Disabling the preference must not trust the saved file stamp.");
			}
			finally { Directory.Delete(root, true); }
		}

		[Test]
		public void TryAdopt_ChangedOrInvalidSavedHashFallsBackToHashing()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores original = CreateStores(root);
				string path = Path.Combine(root, "existing.7z");
				File.WriteAllText(path, "original archive");
				original.Artifacts.ReferenceArchiveFileWithoutMd5(path, CancellationToken.None);
				DateTime savedWriteTime = File.GetLastWriteTimeUtc(path);
				byte[] changed = Encoding.UTF8.GetBytes("modified archive");
				File.WriteAllBytes(path, changed);
				File.SetLastWriteTimeUtc(path, savedWriteTime.AddSeconds(3));
				var store = new CollectionsStore(root);
				var artifacts = new CollectionsRetainedArtifactStore(store);
				Assert.That(artifacts.TryReferenceCachedArchiveFile(path, CancellationToken.None), Is.Null);
				var adopter = new CollectionVerifiedArchiveAdopter(new RecordingArchiveSource(new[] { ExactCandidate(path) }),
					new RecordingVerifier { ThrowOnCall = true }, artifacts, new CollectionsRetainedArtifactReferenceStore(store));
				CollectionVerifiedArchive refreshed = adopter.TryAdopt(CreateRequest(null), CancellationToken.None, true);
				Assert.That(refreshed.Artifact.ContentHash.Value, Is.EqualTo(Sha256(changed)));
				Assert.That(refreshed.VerificationBasis, Is.EqualTo(CollectionArchiveVerificationBasis.ManagedArchiveMetadata));
				string cachePath = Directory.GetFiles(Path.Combine(store.StoreDirectory, "ArchiveHashCache"), "*.json").Single();
				File.WriteAllText(cachePath, "invalid JSON");
				Assert.That(new CollectionsRetainedArtifactStore(store).TryReferenceCachedArchiveFile(path, CancellationToken.None), Is.Null);
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Models a fresh process without changing the durable saved archive hash or other tests' cache entries.</summary>
		private static void ForgetProcessObservedHash(string path)
		{
			System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
			Type matcher = typeof(CollectionArchiveContentMatcher);
			object cacheLock = matcher.GetField("HashCacheLock", flags).GetValue(null);
			var cache = (System.Collections.IDictionary)matcher.GetField("HashCache", flags).GetValue(null);
			lock (cacheLock) cache.Remove(Path.GetFullPath(path));
		}

		private static Stores CreateStores(string root)
		{
			var featureStore = new CollectionsStore(root);
			featureStore.CreateNew();
			return new Stores(new CollectionsRetainedArtifactStore(featureStore),
				new CollectionsRetainedArtifactReferenceStore(featureStore), new CollectionsAcquisitionStore(featureStore));
		}

		private static CollectionManagedArchiveCandidate ExactCandidate(string path)
		{
			return new CollectionManagedArchiveCandidate("nexus-mod-file", StableArtifactId, path);
		}

		private static CollectionAcquisitionRequest CreateRequest(CollectionContentHash expectedHash)
		{
			return CreateRequest(expectedHash, StableArtifactId, Guid.NewGuid());
		}

		/// <summary>Completed exact downloads remain usable when Nexus hash search cannot prove their file identity.</summary>
		[TestCase(false)]
		[TestCase(true)]
		public void TryAdopt_ManifestMd5AndSizeVerifyBytesWithoutNexusHashIndex(bool corrupt)
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "completed-download.7z");
				File.WriteAllText(archivePath, corrupt ? "abd" : "abc", new UTF8Encoding(false));
				var source = new RecordingArchiveSource(new[] { ExactCandidate(archivePath) });
				// This repository has no API client. Exact manifest evidence must succeed without a lookup.
				var verifier = new NexusCollectionArchiveIdentityVerifier(new NexusModsApiRepository("skyrimspecialedition", null));
				var adopter = new CollectionVerifiedArchiveAdopter(source, verifier, stores.Artifacts, stores.References);
				CollectionAcquisitionRequest request = CreateRequest(null, StableArtifactId, Guid.NewGuid(), 3,
					"900150983cd24fb0d6963f7d28e17f72");
				CollectionVerifiedArchive result = adopter.TryAdopt(request);
				if (corrupt)
					Assert.That(result, Is.Null, "Matching mod/file metadata and size must not accept different bytes.");
				else
				{
					Assert.That(result, Is.Not.Null);
					Assert.That(result.Artifact.ContentHash.Value, Is.EqualTo(Sha256(Encoding.ASCII.GetBytes("abc"))));
				}
			}
			finally { Directory.Delete(root, true); }
		}

		/// <summary>Direct stream verification requires both the manifest digest and the declared number of bytes.</summary>
		[TestCase(3, true)]
		[TestCase(4, false)]
		public void NexusVerifier_ManifestDigestAlsoRequiresDeclaredByteLength(long byteLength, bool expected)
		{
			var verifier = new NexusCollectionArchiveIdentityVerifier(new NexusModsApiRepository("skyrimspecialedition", null));
			var artifact = new CollectionArtifactReference("nexus-mod-file", StableArtifactId, null, byteLength,
				"900150983cd24fb0d6963f7d28e17f72");
			using (var stream = new MemoryStream(Encoding.ASCII.GetBytes("abc")))
				Assert.That(verifier.IsExactMatch(artifact, stream, CancellationToken.None), Is.EqualTo(expected));
		}

		/// <summary>Retains legacy provider verification when the manifest has no usable digest.</summary>
		[Test]
		public void TryAdopt_ManifestSizeWithoutMd5StillRequiresProviderVerification()
		{
			string root = CreateTemporaryDirectory();
			try
			{
				Stores stores = CreateStores(root);
				string archivePath = Path.Combine(root, "candidate.7z");
				File.WriteAllText(archivePath, "abc", new UTF8Encoding(false));
				var verifier = new RecordingVerifier { Handler = (artifact, stream) => false };
				var adopter = new CollectionVerifiedArchiveAdopter(new RecordingArchiveSource(new[] { ExactCandidate(archivePath) }),
					verifier, stores.Artifacts, stores.References);
				Assert.That(adopter.TryAdopt(CreateRequest(null, StableArtifactId, Guid.NewGuid(), 3)), Is.Null);
				Assert.That(verifier.CallCount, Is.EqualTo(1));
			}
			finally { Directory.Delete(root, true); }
		}

		private static CollectionAcquisitionRequest CreateRequest(CollectionContentHash expectedHash, string stableArtifactId, Guid requestId,
			long? expectedByteLength = null, string expectedMd5 = null)
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
				new CollectionArtifactReference("nexus-mod-file", stableArtifactId, expectedHash, expectedByteLength, expectedMd5),
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
			public Stores(CollectionsRetainedArtifactStore artifacts, CollectionsRetainedArtifactReferenceStore references,
				CollectionsAcquisitionStore acquisitions)
			{
				Artifacts = artifacts;
				References = references;
				Acquisitions = acquisitions;
			}

			public CollectionsRetainedArtifactStore Artifacts { get; }
			public CollectionsRetainedArtifactReferenceStore References { get; }
			public CollectionsAcquisitionStore Acquisitions { get; }
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
