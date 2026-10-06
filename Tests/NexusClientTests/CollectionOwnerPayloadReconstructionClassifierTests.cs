using Nexus.Client.CollectionManagement;
using Nexus.Client.ModManagement;
using NUnit.Framework;

namespace NexusClientTests
{
	/// <summary>C12 Step 2A fail-closed archive-backed owner-payload classification coverage.</summary>
	[TestFixture]
	[Category("CollectionsGateL")]
	public class CollectionOwnerPayloadReconstructionClassifierTests
	{
		private static readonly CollectionContentHash ExpectedHash = CollectionContentHash.FromSha256(new string('a', 64));

		[Test]
		public void Classify_ExactArchiveEntryWithMatchingObservedIdentityIsArchiveBacked()
		{
			CollectionOwnerPayloadClassificationResult result = CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate());

			Assert.AreEqual(CollectionOwnerPayloadSourceKind.ArchiveBacked, result.SourceKind);
			Assert.AreEqual(CollectionOwnerPayloadClassificationReason.EligibleArchiveBacked, result.Reason);
			Assert.IsNotNull(result.ArchiveBackedDescriptor);
			Assert.AreEqual("native-a", result.ArchiveBackedDescriptor.NativeSnapshotKey);
			Assert.AreEqual("textures\\example.dds", result.ArchiveBackedDescriptor.ArchiveEntryPath);
			Assert.AreEqual("prepared-native-v1", result.ArchiveBackedDescriptor.ReconstructionIdentity);
		}

		[Test]
		public void Classify_GeneratedOutputFallsBackToCapturedArtifact()
		{
			CollectionOwnerPayloadClassificationResult result = CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(mappingKind: CollectionOwnerPayloadReconstructionMappingKind.GeneratedOutput));

			AssertCaptured(result, CollectionOwnerPayloadClassificationReason.UnsupportedMappingKind);
		}

		[Test]
		public void Classify_OriginalValueOwnerFallsBackToCapturedArtifact()
		{
			CollectionOwnerPayloadClassificationResult result = CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(ownerKind: NativeStateCaptureDeploymentOwnerKind.OriginalValue));

			AssertCaptured(result, CollectionOwnerPayloadClassificationReason.UnsupportedOwnerKind);
		}

		[Test]
		public void Classify_AmbiguousOrUnavailableArchiveMappingFallsBackToCapturedArtifact()
		{
			AssertCaptured(CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(mappingAmbiguous: true)), CollectionOwnerPayloadClassificationReason.AmbiguousArchiveMapping);
			AssertCaptured(CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(exactRetainedArchiveAvailable: false)), CollectionOwnerPayloadClassificationReason.ExactRetainedArchiveUnavailable);
		}

		[Test]
		public void Classify_MissingOrChangedContentIdentityFallsBackToCapturedArtifact()
		{
			AssertCaptured(CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(includeExpectedContentHash: false)), CollectionOwnerPayloadClassificationReason.ExpectedContentIdentityUnavailable);
			AssertCaptured(CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(includeObservedContentHash: false)), CollectionOwnerPayloadClassificationReason.ObservedContentIdentityUnavailable);
			AssertCaptured(CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(observedContentHash: CollectionContentHash.FromSha256(new string('b', 64)))),
				CollectionOwnerPayloadClassificationReason.ObservedContentDoesNotMatchExpected);
			AssertCaptured(CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(observedByteLength: 124)), CollectionOwnerPayloadClassificationReason.ObservedContentDoesNotMatchExpected);
		}

		[Test]
		public void Classify_InvalidReconstructionDescriptorFallsBackToCapturedArtifact()
		{
			CollectionOwnerPayloadClassificationResult result = CollectionOwnerPayloadReconstructionClassifier.Classify(
				CreateCandidate(archiveEntryPath: "..\\escape.dds"));

			AssertCaptured(result, CollectionOwnerPayloadClassificationReason.ReconstructionDescriptorIncomplete);
		}

		private static CollectionOwnerPayloadReconstructionCandidate CreateCandidate(
			NativeStateCaptureDeploymentOwnerKind ownerKind = NativeStateCaptureDeploymentOwnerKind.Direct,
			CollectionOwnerPayloadReconstructionMappingKind mappingKind = CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry,
			bool exactRetainedArchiveAvailable = true, bool mappingAmbiguous = false,
			string archiveEntryPath = "textures\\example.dds", bool includeExpectedContentHash = true,
			bool includeObservedContentHash = true, CollectionContentHash observedContentHash = null, long? observedByteLength = null)
		{
			return new CollectionOwnerPayloadReconstructionCandidate(ownerKind, mappingKind, exactRetainedArchiveAvailable,
				mappingAmbiguous, "native-a", archiveEntryPath, "prepared-native-v1", includeExpectedContentHash ? ExpectedHash : null, 123,
				includeObservedContentHash ? (observedContentHash ?? ExpectedHash) : null, observedByteLength ?? 123);
		}

		private static void AssertCaptured(CollectionOwnerPayloadClassificationResult result,
			CollectionOwnerPayloadClassificationReason reason)
		{
			Assert.AreEqual(CollectionOwnerPayloadSourceKind.CapturedArtifact, result.SourceKind);
			Assert.AreEqual(reason, result.Reason);
			Assert.IsNull(result.ArchiveBackedDescriptor);
		}
	}
}
