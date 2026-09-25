using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods.Formats.FOMod;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies a non-fatal limitation observed while logically capturing user-authored Sort/FOMod metadata.
	/// </summary>
	public enum CollectionUserMetadataIssueKind
	{
		SortServiceUnavailable = 1,
		SortArchiveReferenceUnavailable = 2,
		SortAssignmentUnavailable = 3,
		SortAssignmentPendingIdentity = 4,
		SortAssignmentIdentityMismatch = 5,
		ScreenshotMetadataUnavailable = 6,
		ScreenshotArchiveReferenceUnavailable = 7,
		ScreenshotOverrideArchiveUnavailable = 8,
		AmbiguousArchiveMetadataBinding = 9,
		ScreenshotOverrideInvalid = 10
	}

	/// <summary>Records one immutable C7.6 user-metadata capture limitation.</summary>
	public sealed class CollectionUserMetadataIssue
	{
		/// <summary>Creates one user-metadata issue.</summary>
		public CollectionUserMetadataIssue(CollectionUserMetadataIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionUserMetadataIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionUserMetadataIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Captures one resolved native Sort assignment, including the persisted state that distinguishes explicit blank/numeric values.
	/// </summary>
	public sealed class CollectionCapturedSortAssignment
	{
		/// <summary>Creates one detached logical Sort assignment.</summary>
		public CollectionCapturedSortAssignment(string nativeSnapshotKey, int? sortNumber,
			ModSortOrderAssignmentState assignmentState, string recordedModId, string recordedDownloadId)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			if (!Enum.IsDefined(typeof(ModSortOrderAssignmentState), assignmentState))
				throw new ArgumentOutOfRangeException(nameof(assignmentState));
			NativeSnapshotKey = nativeSnapshotKey;
			SortNumber = sortNumber;
			AssignmentState = assignmentState;
			RecordedModId = recordedModId ?? String.Empty;
			RecordedDownloadId = recordedDownloadId ?? String.Empty;
		}

		public string NativeSnapshotKey { get; }
		public int? SortNumber { get; }
		public ModSortOrderAssignmentState AssignmentState { get; }
		public string RecordedModId { get; }
		public string RecordedDownloadId { get; }
	}

	/// <summary>
	/// Captures one current generated screenshot override as immutable retained content rather than a shared-database copy.
	/// </summary>
	public sealed class CollectionCapturedScreenshotOverride
	{
		/// <summary>Creates one retained screenshot-override descriptor.</summary>
		public CollectionCapturedScreenshotOverride(string nativeSnapshotKey, string screenshotPath,
			long sourceArchiveLength, long sourceArchiveWriteTimeUtcTicks, long updatedUtcTicks,
			RetainedArtifactReference retainedArtifact)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			if (String.IsNullOrWhiteSpace(screenshotPath))
				throw new ArgumentException("A screenshot override path is required.", nameof(screenshotPath));
			if (sourceArchiveLength < 0)
				throw new ArgumentOutOfRangeException(nameof(sourceArchiveLength));
			NativeSnapshotKey = nativeSnapshotKey;
			ScreenshotPath = screenshotPath;
			SourceArchiveLength = sourceArchiveLength;
			SourceArchiveWriteTimeUtcTicks = sourceArchiveWriteTimeUtcTicks;
			UpdatedUtcTicks = updatedUtcTicks;
			RetainedArtifact = retainedArtifact ?? throw new ArgumentNullException(nameof(retainedArtifact));
		}

		public string NativeSnapshotKey { get; }
		public string ScreenshotPath { get; }
		public long SourceArchiveLength { get; }
		public long SourceArchiveWriteTimeUtcTicks { get; }
		public long UpdatedUtcTicks { get; }
		public RetainedArtifactReference RetainedArtifact { get; }
	}

	/// <summary>
	/// Immutable target-scoped C7.6 logical export of selected user-authored metadata.
	/// </summary>
	/// <remarks>
	/// This snapshot deliberately excludes the shared FOMod SQLite database and rebuildable archive_metadata rows. Sort remains
	/// user metadata rather than deployment/plugin order, and generated screenshot overrides are retained as independent blobs.
	/// </remarks>
	public sealed class CollectionUserMetadataSnapshot
	{
		/// <summary>Creates one immutable user-metadata capture.</summary>
		public CollectionUserMetadataSnapshot(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			long deploymentCommitSequence, IEnumerable<CollectionCapturedSortAssignment> sortAssignments,
			NativeStateCaptureCoverage sortCoverage, IEnumerable<CollectionCapturedScreenshotOverride> screenshotOverrides,
			NativeStateCaptureCoverage screenshotCoverage, IEnumerable<CollectionUserMetadataIssue> issues)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			CaptureIdentity = captureIdentity ?? throw new ArgumentNullException(nameof(captureIdentity));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), sortCoverage))
				throw new ArgumentOutOfRangeException(nameof(sortCoverage));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), screenshotCoverage))
				throw new ArgumentOutOfRangeException(nameof(screenshotCoverage));
			DeploymentCommitSequence = deploymentCommitSequence;
			SortAssignments = Copy(sortAssignments, nameof(sortAssignments));
			ScreenshotOverrides = Copy(screenshotOverrides, nameof(screenshotOverrides));
			SortCoverage = sortCoverage;
			ScreenshotCoverage = screenshotCoverage;
			Coverage = AggregateCoverage(sortCoverage, screenshotCoverage);
			Issues = Copy(issues, nameof(issues));
		}

		public CollectionTargetIdentity Target { get; }
		public LocalCaptureIdentity CaptureIdentity { get; }
		/// <summary>Gets the observed native deployment checkpoint for diagnostics, not a universal state generation.</summary>
		public long DeploymentCommitSequence { get; }
		public ReadOnlyCollection<CollectionCapturedSortAssignment> SortAssignments { get; }
		public ReadOnlyCollection<CollectionCapturedScreenshotOverride> ScreenshotOverrides { get; }
		public NativeStateCaptureCoverage SortCoverage { get; }
		public NativeStateCaptureCoverage ScreenshotCoverage { get; }
		public NativeStateCaptureCoverage Coverage { get; }
		public ReadOnlyCollection<CollectionUserMetadataIssue> Issues { get; }

		private static NativeStateCaptureCoverage AggregateCoverage(params NativeStateCaptureCoverage[] coverages)
		{
			if (coverages.Any(x => x == NativeStateCaptureCoverage.Partial))
				return NativeStateCaptureCoverage.Partial;
			int applicableCount = coverages.Count(x => x != NativeStateCaptureCoverage.NotApplicable);
			if (applicableCount == 0)
				return NativeStateCaptureCoverage.NotApplicable;
			int unavailableCount = coverages.Count(x => x == NativeStateCaptureCoverage.Unavailable);
			if (unavailableCount == applicableCount)
				return NativeStateCaptureCoverage.Unavailable;
			if (unavailableCount > 0)
				return NativeStateCaptureCoverage.Partial;
			return NativeStateCaptureCoverage.Complete;
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A user-metadata snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
