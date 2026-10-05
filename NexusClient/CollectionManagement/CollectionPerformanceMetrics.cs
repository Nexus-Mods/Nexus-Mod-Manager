using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Read-only process-session counters used by the C12 performance audit.</summary>
	public sealed class CollectionPerformanceSnapshot
	{
		internal CollectionPerformanceSnapshot(DateTimeOffset sessionStartedUtc, long nativeStateIndexBuildCount, double nativeStateIndexBuildMilliseconds,
			long nativeRecipePreparationCount, double nativeRecipePreparationMilliseconds, long effectPreviewCount, double effectPreviewMilliseconds,
			long reviewedSnapshotSerializeCount, long reviewedSnapshotSerializedBytes, double reviewedSnapshotSerializeMilliseconds,
			long reviewedSnapshotDeserializeCount, long reviewedSnapshotDeserializedBytes, double reviewedSnapshotDeserializeMilliseconds,
			long reviewedRuntimeReconstructionCount, double reviewedRuntimeReconstructionMilliseconds, long fileWinnerReconciliationCount,
			double fileWinnerReconciliationMilliseconds, long nxmMetadataResolutionCount, double nxmMetadataResolutionMilliseconds,
			long retainedManifestLoadCount, long retainedManifestLoadedBytes, double retainedManifestLoadMilliseconds,
			long retainedBundleOpenCount, long retainedBundleOpenedBytes, double retainedBundleOpenMilliseconds,
			long retainedArtifactOpenCount, long uniqueRetainedArtifactOpenCount, long retainedArtifactVerificationCacheHitCount,
			long retainedArtifactVerificationHashCount, long retainedArtifactVerificationHashedBytes, double retainedArtifactVerificationHashMilliseconds,
			long archiveSourceReadCount, long uniqueArchiveSourceMemberCount, long uniqueArchiveSourcePathReadCount, long archiveSourceReadBytes,
			long preparedNativeIdentityObservationCount, long uniquePreparedNativeIdentityCount, long preparedNativeIdentityRepeatCount,
			long startupRecoveryPassCount, long startupRecoveryObservedOperationCount, double startupRecoveryMilliseconds,
			long nxmUiDispatchCount, double nxmUiDispatchMilliseconds)
		{
			SessionStartedUtc = sessionStartedUtc;
			NativeStateIndexBuildCount = nativeStateIndexBuildCount;
			NativeStateIndexBuildMilliseconds = nativeStateIndexBuildMilliseconds;
			NativeRecipePreparationCount = nativeRecipePreparationCount;
			NativeRecipePreparationMilliseconds = nativeRecipePreparationMilliseconds;
			EffectPreviewCount = effectPreviewCount;
			EffectPreviewMilliseconds = effectPreviewMilliseconds;
			ReviewedSnapshotSerializeCount = reviewedSnapshotSerializeCount;
			ReviewedSnapshotSerializedBytes = reviewedSnapshotSerializedBytes;
			ReviewedSnapshotSerializeMilliseconds = reviewedSnapshotSerializeMilliseconds;
			ReviewedSnapshotDeserializeCount = reviewedSnapshotDeserializeCount;
			ReviewedSnapshotDeserializedBytes = reviewedSnapshotDeserializedBytes;
			ReviewedSnapshotDeserializeMilliseconds = reviewedSnapshotDeserializeMilliseconds;
			ReviewedRuntimeReconstructionCount = reviewedRuntimeReconstructionCount;
			ReviewedRuntimeReconstructionMilliseconds = reviewedRuntimeReconstructionMilliseconds;
			FileWinnerReconciliationCount = fileWinnerReconciliationCount;
			FileWinnerReconciliationMilliseconds = fileWinnerReconciliationMilliseconds;
			NxmMetadataResolutionCount = nxmMetadataResolutionCount;
			NxmMetadataResolutionMilliseconds = nxmMetadataResolutionMilliseconds;
			RetainedManifestLoadCount = retainedManifestLoadCount;
			RetainedManifestLoadedBytes = retainedManifestLoadedBytes;
			RetainedManifestLoadMilliseconds = retainedManifestLoadMilliseconds;
			RetainedBundleOpenCount = retainedBundleOpenCount;
			RetainedBundleOpenedBytes = retainedBundleOpenedBytes;
			RetainedBundleOpenMilliseconds = retainedBundleOpenMilliseconds;
			RetainedArtifactOpenCount = retainedArtifactOpenCount;
			UniqueRetainedArtifactOpenCount = uniqueRetainedArtifactOpenCount;
			RetainedArtifactVerificationCacheHitCount = retainedArtifactVerificationCacheHitCount;
			RetainedArtifactVerificationHashCount = retainedArtifactVerificationHashCount;
			RetainedArtifactVerificationHashedBytes = retainedArtifactVerificationHashedBytes;
			RetainedArtifactVerificationHashMilliseconds = retainedArtifactVerificationHashMilliseconds;
			ArchiveSourceReadCount = archiveSourceReadCount;
			UniqueArchiveSourceMemberCount = uniqueArchiveSourceMemberCount;
			UniqueArchiveSourcePathReadCount = uniqueArchiveSourcePathReadCount;
			ArchiveSourceReadBytes = archiveSourceReadBytes;
			PreparedNativeIdentityObservationCount = preparedNativeIdentityObservationCount;
			UniquePreparedNativeIdentityCount = uniquePreparedNativeIdentityCount;
			PreparedNativeIdentityRepeatCount = preparedNativeIdentityRepeatCount;
			StartupRecoveryPassCount = startupRecoveryPassCount;
			StartupRecoveryObservedOperationCount = startupRecoveryObservedOperationCount;
			StartupRecoveryMilliseconds = startupRecoveryMilliseconds;
			NxmUiDispatchCount = nxmUiDispatchCount;
			NxmUiDispatchMilliseconds = nxmUiDispatchMilliseconds;
		}

		public DateTimeOffset SessionStartedUtc { get; private set; }
		public long NativeStateIndexBuildCount { get; private set; }
		public double NativeStateIndexBuildMilliseconds { get; private set; }
		public long NativeRecipePreparationCount { get; private set; }
		public double NativeRecipePreparationMilliseconds { get; private set; }
		public long EffectPreviewCount { get; private set; }
		public double EffectPreviewMilliseconds { get; private set; }
		public long ReviewedSnapshotSerializeCount { get; private set; }
		public long ReviewedSnapshotSerializedBytes { get; private set; }
		public double ReviewedSnapshotSerializeMilliseconds { get; private set; }
		public long ReviewedSnapshotDeserializeCount { get; private set; }
		public long ReviewedSnapshotDeserializedBytes { get; private set; }
		public double ReviewedSnapshotDeserializeMilliseconds { get; private set; }
		public long ReviewedRuntimeReconstructionCount { get; private set; }
		public double ReviewedRuntimeReconstructionMilliseconds { get; private set; }
		public long FileWinnerReconciliationCount { get; private set; }
		public double FileWinnerReconciliationMilliseconds { get; private set; }
		public long NxmMetadataResolutionCount { get; private set; }
		public double NxmMetadataResolutionMilliseconds { get; private set; }
		public long RetainedManifestLoadCount { get; private set; }
		public long RetainedManifestLoadedBytes { get; private set; }
		public double RetainedManifestLoadMilliseconds { get; private set; }
		public long RetainedBundleOpenCount { get; private set; }
		public long RetainedBundleOpenedBytes { get; private set; }
		public double RetainedBundleOpenMilliseconds { get; private set; }
		public long RetainedArtifactOpenCount { get; private set; }
		public long UniqueRetainedArtifactOpenCount { get; private set; }
		public long RetainedArtifactVerificationCacheHitCount { get; private set; }
		public long RetainedArtifactVerificationHashCount { get; private set; }
		public long RetainedArtifactVerificationHashedBytes { get; private set; }
		public double RetainedArtifactVerificationHashMilliseconds { get; private set; }
		public long ArchiveSourceReadCount { get; private set; }
		public long UniqueArchiveSourceMemberCount { get; private set; }
		public long UniqueArchiveSourcePathReadCount { get; private set; }
		public long ArchiveSourceReadBytes { get; private set; }
		public long PreparedNativeIdentityObservationCount { get; private set; }
		public long UniquePreparedNativeIdentityCount { get; private set; }
		public long PreparedNativeIdentityRepeatCount { get; private set; }
		public long StartupRecoveryPassCount { get; private set; }
		public long StartupRecoveryObservedOperationCount { get; private set; }
		public double StartupRecoveryMilliseconds { get; private set; }
		public long NxmUiDispatchCount { get; private set; }
		public double NxmUiDispatchMilliseconds { get; private set; }
	}

	/// <summary>Low-overhead aggregate timing/counter recorder for C12 release measurements.</summary>
	internal static class CollectionPerformanceMetrics
	{
		private static readonly DateTimeOffset SessionStartedUtc = DateTimeOffset.UtcNow;
		private static readonly object DistinctIdentityLock = new object();
		private static readonly HashSet<string> OpenedRetainedArtifactIds = new HashSet<string>(StringComparer.Ordinal);
		private static readonly HashSet<string> ArchiveSourceMembers = new HashSet<string>(StringComparer.Ordinal);
		private static readonly HashSet<string> ArchiveSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> PreparedNativeIdentities = new HashSet<string>(StringComparer.Ordinal);
		private static long _nativeStateIndexBuildCount;
		private static long _nativeStateIndexBuildTicks;
		private static long _nativeRecipePreparationCount;
		private static long _nativeRecipePreparationTicks;
		private static long _effectPreviewCount;
		private static long _effectPreviewTicks;
		private static long _reviewedSnapshotSerializeCount;
		private static long _reviewedSnapshotSerializedBytes;
		private static long _reviewedSnapshotSerializeTicks;
		private static long _reviewedSnapshotDeserializeCount;
		private static long _reviewedSnapshotDeserializedBytes;
		private static long _reviewedSnapshotDeserializeTicks;
		private static long _reviewedRuntimeReconstructionCount;
		private static long _reviewedRuntimeReconstructionTicks;
		private static long _fileWinnerReconciliationCount;
		private static long _fileWinnerReconciliationTicks;
		private static long _nxmMetadataResolutionCount;
		private static long _nxmMetadataResolutionTicks;
		private static long _retainedManifestLoadCount;
		private static long _retainedManifestLoadedBytes;
		private static long _retainedManifestLoadTicks;
		private static long _retainedBundleOpenCount;
		private static long _retainedBundleOpenedBytes;
		private static long _retainedBundleOpenTicks;
		private static long _retainedArtifactOpenCount;
		private static long _retainedArtifactVerificationCacheHitCount;
		private static long _retainedArtifactVerificationHashCount;
		private static long _retainedArtifactVerificationHashedBytes;
		private static long _retainedArtifactVerificationHashTicks;
		private static long _archiveSourceReadCount;
		private static long _archiveSourceReadBytes;
		private static long _preparedNativeIdentityObservationCount;
		private static long _preparedNativeIdentityRepeatCount;
		private static long _startupRecoveryPassCount;
		private static long _startupRecoveryObservedOperationCount;
		private static long _startupRecoveryTicks;
		private static long _nxmUiDispatchCount;
		private static long _nxmUiDispatchTicks;

		internal static long StartTiming()
		{
			return Stopwatch.GetTimestamp();
		}

		internal static void RecordNativeStateIndexBuild(long started) { Record(started, ref _nativeStateIndexBuildCount, ref _nativeStateIndexBuildTicks); }
		internal static void RecordNativeRecipePreparation(long started) { Record(started, ref _nativeRecipePreparationCount, ref _nativeRecipePreparationTicks); }
		internal static void RecordEffectPreview(long started) { Record(started, ref _effectPreviewCount, ref _effectPreviewTicks); }
		internal static void RecordReviewedRuntimeReconstruction(long started) { Record(started, ref _reviewedRuntimeReconstructionCount, ref _reviewedRuntimeReconstructionTicks); }
		internal static void RecordFileWinnerReconciliation(long started) { Record(started, ref _fileWinnerReconciliationCount, ref _fileWinnerReconciliationTicks); }
		internal static void RecordNxmMetadataResolution(long started) { Record(started, ref _nxmMetadataResolutionCount, ref _nxmMetadataResolutionTicks); }

		internal static void RecordRetainedManifestLoad(long started, long payloadBytes)
		{
			Interlocked.Increment(ref _retainedManifestLoadCount);
			Interlocked.Add(ref _retainedManifestLoadedBytes, payloadBytes);
			Interlocked.Add(ref _retainedManifestLoadTicks, ElapsedTicks(started));
		}

		internal static void RecordRetainedBundleOpen(long started, long payloadBytes)
		{
			Interlocked.Increment(ref _retainedBundleOpenCount);
			Interlocked.Add(ref _retainedBundleOpenedBytes, payloadBytes);
			Interlocked.Add(ref _retainedBundleOpenTicks, ElapsedTicks(started));
		}

		internal static void RecordRetainedArtifactOpen(string artifactId)
		{
			if (String.IsNullOrEmpty(artifactId))
				return;
			Interlocked.Increment(ref _retainedArtifactOpenCount);
			lock (DistinctIdentityLock)
				OpenedRetainedArtifactIds.Add(artifactId);
		}

		internal static void RecordRetainedArtifactVerificationCacheHit()
		{
			Interlocked.Increment(ref _retainedArtifactVerificationCacheHitCount);
		}

		internal static void RecordRetainedArtifactVerificationHash(long started, long payloadBytes)
		{
			Interlocked.Increment(ref _retainedArtifactVerificationHashCount);
			Interlocked.Add(ref _retainedArtifactVerificationHashedBytes, payloadBytes < 0 ? 0 : payloadBytes);
			Interlocked.Add(ref _retainedArtifactVerificationHashTicks, ElapsedTicks(started));
		}

		internal static void RecordArchiveSourceRead(CollectionMemberKey memberKey, string sourcePath, long payloadBytes)
		{
			if (memberKey == null)
				return;
			Interlocked.Increment(ref _archiveSourceReadCount);
			Interlocked.Add(ref _archiveSourceReadBytes, payloadBytes < 0 ? 0 : payloadBytes);
			string memberIdentity = memberKey.ToString();
			lock (DistinctIdentityLock)
			{
				ArchiveSourceMembers.Add(memberIdentity);
				if (!String.IsNullOrWhiteSpace(sourcePath))
					ArchiveSourcePaths.Add(memberIdentity + "\n" + sourcePath.Replace('\\', '/'));
			}
		}

		internal static void RecordPreparedNativeIdentity(string fingerprint)
		{
			if (String.IsNullOrEmpty(fingerprint))
				return;
			Interlocked.Increment(ref _preparedNativeIdentityObservationCount);
			lock (DistinctIdentityLock)
			{
				if (!PreparedNativeIdentities.Add(fingerprint))
					Interlocked.Increment(ref _preparedNativeIdentityRepeatCount);
			}
		}

		internal static void RecordStartupRecovery(long started, long observedOperationCount)
		{
			Interlocked.Increment(ref _startupRecoveryPassCount);
			Interlocked.Add(ref _startupRecoveryObservedOperationCount, observedOperationCount < 0 ? 0 : observedOperationCount);
			Interlocked.Add(ref _startupRecoveryTicks, ElapsedTicks(started));
		}

		internal static void RecordNxmUiDispatch(long queuedAtTimestamp)
		{
			Record(queuedAtTimestamp, ref _nxmUiDispatchCount, ref _nxmUiDispatchTicks);
		}

		internal static void RecordReviewedSnapshotSerialize(long started, long payloadBytes)
		{
			Interlocked.Increment(ref _reviewedSnapshotSerializeCount);
			Interlocked.Add(ref _reviewedSnapshotSerializedBytes, payloadBytes);
			Interlocked.Add(ref _reviewedSnapshotSerializeTicks, ElapsedTicks(started));
		}

		internal static void RecordReviewedSnapshotDeserialize(long started, long payloadBytes)
		{
			Interlocked.Increment(ref _reviewedSnapshotDeserializeCount);
			Interlocked.Add(ref _reviewedSnapshotDeserializedBytes, payloadBytes);
			Interlocked.Add(ref _reviewedSnapshotDeserializeTicks, ElapsedTicks(started));
		}

		internal static CollectionPerformanceSnapshot Capture()
		{
			long uniqueRetainedArtifactOpenCount;
			long uniqueArchiveSourceMemberCount;
			long uniqueArchiveSourcePathReadCount;
			long uniquePreparedNativeIdentityCount;
			lock (DistinctIdentityLock)
			{
				uniqueRetainedArtifactOpenCount = OpenedRetainedArtifactIds.Count;
				uniqueArchiveSourceMemberCount = ArchiveSourceMembers.Count;
				uniqueArchiveSourcePathReadCount = ArchiveSourcePaths.Count;
				uniquePreparedNativeIdentityCount = PreparedNativeIdentities.Count;
			}

			return new CollectionPerformanceSnapshot(SessionStartedUtc,
				Interlocked.Read(ref _nativeStateIndexBuildCount), ToMilliseconds(Interlocked.Read(ref _nativeStateIndexBuildTicks)),
				Interlocked.Read(ref _nativeRecipePreparationCount), ToMilliseconds(Interlocked.Read(ref _nativeRecipePreparationTicks)),
				Interlocked.Read(ref _effectPreviewCount), ToMilliseconds(Interlocked.Read(ref _effectPreviewTicks)),
				Interlocked.Read(ref _reviewedSnapshotSerializeCount), Interlocked.Read(ref _reviewedSnapshotSerializedBytes), ToMilliseconds(Interlocked.Read(ref _reviewedSnapshotSerializeTicks)),
				Interlocked.Read(ref _reviewedSnapshotDeserializeCount), Interlocked.Read(ref _reviewedSnapshotDeserializedBytes), ToMilliseconds(Interlocked.Read(ref _reviewedSnapshotDeserializeTicks)),
				Interlocked.Read(ref _reviewedRuntimeReconstructionCount), ToMilliseconds(Interlocked.Read(ref _reviewedRuntimeReconstructionTicks)),
				Interlocked.Read(ref _fileWinnerReconciliationCount), ToMilliseconds(Interlocked.Read(ref _fileWinnerReconciliationTicks)),
				Interlocked.Read(ref _nxmMetadataResolutionCount), ToMilliseconds(Interlocked.Read(ref _nxmMetadataResolutionTicks)),
				Interlocked.Read(ref _retainedManifestLoadCount), Interlocked.Read(ref _retainedManifestLoadedBytes), ToMilliseconds(Interlocked.Read(ref _retainedManifestLoadTicks)),
				Interlocked.Read(ref _retainedBundleOpenCount), Interlocked.Read(ref _retainedBundleOpenedBytes), ToMilliseconds(Interlocked.Read(ref _retainedBundleOpenTicks)),
				Interlocked.Read(ref _retainedArtifactOpenCount), uniqueRetainedArtifactOpenCount, Interlocked.Read(ref _retainedArtifactVerificationCacheHitCount),
				Interlocked.Read(ref _retainedArtifactVerificationHashCount), Interlocked.Read(ref _retainedArtifactVerificationHashedBytes), ToMilliseconds(Interlocked.Read(ref _retainedArtifactVerificationHashTicks)),
				Interlocked.Read(ref _archiveSourceReadCount), uniqueArchiveSourceMemberCount, uniqueArchiveSourcePathReadCount, Interlocked.Read(ref _archiveSourceReadBytes),
				Interlocked.Read(ref _preparedNativeIdentityObservationCount), uniquePreparedNativeIdentityCount, Interlocked.Read(ref _preparedNativeIdentityRepeatCount),
				Interlocked.Read(ref _startupRecoveryPassCount), Interlocked.Read(ref _startupRecoveryObservedOperationCount), ToMilliseconds(Interlocked.Read(ref _startupRecoveryTicks)),
				Interlocked.Read(ref _nxmUiDispatchCount), ToMilliseconds(Interlocked.Read(ref _nxmUiDispatchTicks)));
		}

		private static void Record(long started, ref long count, ref long elapsedTicks)
		{
			Interlocked.Increment(ref count);
			Interlocked.Add(ref elapsedTicks, ElapsedTicks(started));
		}

		private static long ElapsedTicks(long started)
		{
			long elapsed = Stopwatch.GetTimestamp() - started;
			return elapsed < 0 ? 0 : elapsed;
		}

		private static double ToMilliseconds(long stopwatchTicks)
		{
			return stopwatchTicks <= 0 ? 0.0 : stopwatchTicks * 1000.0 / Stopwatch.Frequency;
		}
	}
}
