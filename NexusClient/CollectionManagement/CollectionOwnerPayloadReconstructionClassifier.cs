using System;
using System.IO;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies the characterized relationship between one managed owner payload and an immutable archive input.</summary>
	public enum CollectionOwnerPayloadReconstructionMappingKind
	{
		Unknown = 0,
		ExactArchiveEntry = 1,
		GeneratedOutput = 2,
		ScriptedSidecar = 3,
		TransformedOutput = 4
	}

	/// <summary>Explains why the pure Step 2 classifier selected byte retention or archive-backed reconstruction.</summary>
	public enum CollectionOwnerPayloadClassificationReason
	{
		EligibleArchiveBacked = 1,
		UnsupportedOwnerKind = 2,
		UnsupportedMappingKind = 3,
		ExactRetainedArchiveUnavailable = 4,
		AmbiguousArchiveMapping = 5,
		ReconstructionDescriptorIncomplete = 6,
		ExpectedContentIdentityUnavailable = 7,
		ObservedContentIdentityUnavailable = 8,
		ObservedContentDoesNotMatchExpected = 9
	}

	/// <summary>
	/// Immutable facts supplied to the pure archive-backed classifier. The caller remains responsible for proving these facts from
	/// characterized native recipe/replay state and retained immutable inputs; the classifier performs no I/O and never guesses.
	/// </summary>
	public sealed class CollectionOwnerPayloadReconstructionCandidate
	{
		/// <summary>Creates one candidate from already observed/proven reconstruction facts.</summary>
		public CollectionOwnerPayloadReconstructionCandidate(NativeStateCaptureDeploymentOwnerKind ownerKind,
			CollectionOwnerPayloadReconstructionMappingKind mappingKind, bool exactRetainedArchiveAvailable,
			bool mappingAmbiguous, string nativeSnapshotKey, string archiveEntryPath, string reconstructionIdentity,
			CollectionContentHash expectedContentHash, long? expectedByteLength,
			CollectionContentHash observedContentHash, long? observedByteLength)
		{
			if (!Enum.IsDefined(typeof(NativeStateCaptureDeploymentOwnerKind), ownerKind))
				throw new ArgumentOutOfRangeException(nameof(ownerKind));
			if (!Enum.IsDefined(typeof(CollectionOwnerPayloadReconstructionMappingKind), mappingKind))
				throw new ArgumentOutOfRangeException(nameof(mappingKind));
			OwnerKind = ownerKind;
			MappingKind = mappingKind;
			ExactRetainedArchiveAvailable = exactRetainedArchiveAvailable;
			MappingAmbiguous = mappingAmbiguous;
			NativeSnapshotKey = nativeSnapshotKey;
			ArchiveEntryPath = archiveEntryPath;
			ReconstructionIdentity = reconstructionIdentity;
			ExpectedContentHash = expectedContentHash;
			ExpectedByteLength = expectedByteLength;
			ObservedContentHash = observedContentHash;
			ObservedByteLength = observedByteLength;
		}

		public NativeStateCaptureDeploymentOwnerKind OwnerKind { get; }
		public CollectionOwnerPayloadReconstructionMappingKind MappingKind { get; }
		public bool ExactRetainedArchiveAvailable { get; }
		public bool MappingAmbiguous { get; }
		public string NativeSnapshotKey { get; }
		public string ArchiveEntryPath { get; }
		public string ReconstructionIdentity { get; }
		public CollectionContentHash ExpectedContentHash { get; }
		public long? ExpectedByteLength { get; }
		public CollectionContentHash ObservedContentHash { get; }
		public long? ObservedByteLength { get; }
	}

	/// <summary>Result of the pure fail-closed owner-payload source classification.</summary>
	public sealed class CollectionOwnerPayloadClassificationResult
	{
		internal CollectionOwnerPayloadClassificationResult(CollectionOwnerPayloadSourceKind sourceKind,
			CollectionOwnerPayloadClassificationReason reason, CollectionOwnerPayloadArchiveBackedDescriptor archiveBackedDescriptor)
		{
			SourceKind = sourceKind;
			Reason = reason;
			ArchiveBackedDescriptor = archiveBackedDescriptor;
		}

		public CollectionOwnerPayloadSourceKind SourceKind { get; }
		public CollectionOwnerPayloadClassificationReason Reason { get; }
		public CollectionOwnerPayloadArchiveBackedDescriptor ArchiveBackedDescriptor { get; }
	}

	/// <summary>
	/// Pure Step 2A classifier. Any missing, ambiguous, unsupported, or byte-mismatched fact falls back to CapturedArtifact.
	/// </summary>
	public static class CollectionOwnerPayloadReconstructionClassifier
	{
		/// <summary>Classifies one owner payload without reading archives, files, stores, or mutable native state.</summary>
		public static CollectionOwnerPayloadClassificationResult Classify(CollectionOwnerPayloadReconstructionCandidate candidate)
		{
			if (candidate == null)
				throw new ArgumentNullException(nameof(candidate));

			if (candidate.OwnerKind != NativeStateCaptureDeploymentOwnerKind.Direct &&
				candidate.OwnerKind != NativeStateCaptureDeploymentOwnerKind.Virtual)
			{
				return Captured(CollectionOwnerPayloadClassificationReason.UnsupportedOwnerKind);
			}
			if (candidate.MappingKind != CollectionOwnerPayloadReconstructionMappingKind.ExactArchiveEntry)
				return Captured(CollectionOwnerPayloadClassificationReason.UnsupportedMappingKind);
			if (!candidate.ExactRetainedArchiveAvailable)
				return Captured(CollectionOwnerPayloadClassificationReason.ExactRetainedArchiveUnavailable);
			if (candidate.MappingAmbiguous)
				return Captured(CollectionOwnerPayloadClassificationReason.AmbiguousArchiveMapping);
			if (candidate.ExpectedContentHash == null || candidate.ExpectedContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256 ||
				!candidate.ExpectedByteLength.HasValue || candidate.ExpectedByteLength.Value < 0)
			{
				return Captured(CollectionOwnerPayloadClassificationReason.ExpectedContentIdentityUnavailable);
			}
			if (candidate.ObservedContentHash == null || candidate.ObservedContentHash.Algorithm != CollectionContentHashAlgorithm.Sha256 ||
				!candidate.ObservedByteLength.HasValue || candidate.ObservedByteLength.Value < 0)
			{
				return Captured(CollectionOwnerPayloadClassificationReason.ObservedContentIdentityUnavailable);
			}
			if (!candidate.ExpectedContentHash.Equals(candidate.ObservedContentHash) ||
				candidate.ExpectedByteLength.Value != candidate.ObservedByteLength.Value)
			{
				return Captured(CollectionOwnerPayloadClassificationReason.ObservedContentDoesNotMatchExpected);
			}

			CollectionOwnerPayloadArchiveBackedDescriptor descriptor;
			try
			{
				descriptor = new CollectionOwnerPayloadArchiveBackedDescriptor(
					CollectionOwnerPayloadArchiveBackedDescriptor.CurrentFormatVersion,
					CollectionOwnerPayloadArchiveBackedKind.ExactArchiveEntry, candidate.NativeSnapshotKey,
					candidate.ArchiveEntryPath, candidate.ReconstructionIdentity);
			}
			catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
			{
				return Captured(CollectionOwnerPayloadClassificationReason.ReconstructionDescriptorIncomplete);
			}

			return new CollectionOwnerPayloadClassificationResult(CollectionOwnerPayloadSourceKind.ArchiveBacked,
				CollectionOwnerPayloadClassificationReason.EligibleArchiveBacked, descriptor);
		}

		private static CollectionOwnerPayloadClassificationResult Captured(CollectionOwnerPayloadClassificationReason reason)
		{
			return new CollectionOwnerPayloadClassificationResult(CollectionOwnerPayloadSourceKind.CapturedArtifact, reason, null);
		}
	}
}
