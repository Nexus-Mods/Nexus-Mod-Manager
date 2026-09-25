using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;
using Nexus.Client.Mods.Formats.FOMod;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Captures selected user-authored Sort/FOMod metadata logically without copying or replacing the shared metadata database.
	/// </summary>
	public sealed class CollectionUserMetadataCaptureService
	{
		private readonly NativeStateCaptureReader _nativeStateReader;
		private readonly CollectionInstalledIdentityCaptureReader _installedIdentityReader;
		private readonly bool _sortSurfaceAvailable;
		private readonly Func<string, ModSortOrderRecord> _sortAssignmentReader;
		private readonly bool _screenshotSurfaceAvailable;
		private readonly Func<string, FOModScreenshotOverrideReadResult> _screenshotOverrideReader;
		private readonly CollectionsRetainedArtifactStore _artifactStore;
		private readonly CollectionsRetainedArtifactReferenceStore _referenceStore;

		/// <summary>Creates a C7.6 logical user-metadata capture service over the existing native metadata services.</summary>
		public CollectionUserMetadataCaptureService(NativeStateCaptureReader nativeStateReader,
			CollectionInstalledIdentityCaptureReader installedIdentityReader, ModSortOrderService sortOrderService,
			FOModUserMetadataReader userMetadataReader, CollectionsRetainedArtifactStore artifactStore,
			CollectionsRetainedArtifactReferenceStore referenceStore)
			: this(nativeStateReader, installedIdentityReader, sortOrderService != null,
				sortOrderService == null ? null : new Func<string, ModSortOrderRecord>(path =>
				{
					ModSortOrderRecord record;
					return sortOrderService.TryGetResolvedAssignment(path, out record) ? record : null;
				}), userMetadataReader != null && userMetadataReader.IsUsable,
				userMetadataReader == null ? null : new Func<string, FOModScreenshotOverrideReadResult>(userMetadataReader.ReadScreenshotOverride),
				artifactStore, referenceStore)
		{
		}

		internal CollectionUserMetadataCaptureService(NativeStateCaptureReader nativeStateReader,
			CollectionInstalledIdentityCaptureReader installedIdentityReader, bool sortSurfaceAvailable,
			Func<string, ModSortOrderRecord> sortAssignmentReader, bool screenshotSurfaceAvailable,
			Func<string, FOModScreenshotOverrideReadResult> screenshotOverrideReader,
			CollectionsRetainedArtifactStore artifactStore, CollectionsRetainedArtifactReferenceStore referenceStore)
		{
			_nativeStateReader = nativeStateReader ?? throw new ArgumentNullException(nameof(nativeStateReader));
			_installedIdentityReader = installedIdentityReader ?? throw new ArgumentNullException(nameof(installedIdentityReader));
			_sortSurfaceAvailable = sortSurfaceAvailable;
			_sortAssignmentReader = sortAssignmentReader;
			_screenshotSurfaceAvailable = screenshotSurfaceAvailable;
			_screenshotOverrideReader = screenshotOverrideReader;
			_artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
			_referenceStore = referenceStore ?? throw new ArgumentNullException(nameof(referenceStore));
		}

		/// <summary>Captures selected user metadata for one target and retains any current screenshot-override bytes.</summary>
		public CollectionUserMetadataSnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity)
		{
			return Capture(target, captureIdentity, CancellationToken.None);
		}

		/// <summary>Captures selected user metadata with cooperative cancellation during retained screenshot publication.</summary>
		public CollectionUserMetadataSnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			CancellationToken cancellationToken)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (captureIdentity == null)
				throw new ArgumentNullException(nameof(captureIdentity));
			NativeStateCaptureSnapshot nativeState = _nativeStateReader.Capture();
			CollectionInstalledIdentitySnapshot installedIdentities = _installedIdentityReader.Capture(target, nativeState);
			return Capture(target, captureIdentity, nativeState, installedIdentities, cancellationToken);
		}

		internal CollectionUserMetadataSnapshot Capture(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			NativeStateCaptureSnapshot nativeState, CollectionInstalledIdentitySnapshot installedIdentities,
			CancellationToken cancellationToken)
		{
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (captureIdentity == null)
				throw new ArgumentNullException(nameof(captureIdentity));
			if (nativeState == null)
				throw new ArgumentNullException(nameof(nativeState));
			if (installedIdentities == null)
				throw new ArgumentNullException(nameof(installedIdentities));
			if (!target.Equals(installedIdentities.Target))
				throw new InvalidOperationException("The installed-identity snapshot belongs to a different Collection target.");
			if (installedIdentities.DeploymentCommitSequence != nativeState.InstallLog.DeploymentCommitSequence)
				throw new InvalidOperationException("User-metadata capture requires installed identities from the same native-state observation.");

			List<CollectionInstalledModIdentity> mods = installedIdentities.Mods
				.OrderBy(x => x.NativeSnapshotKey, StringComparer.OrdinalIgnoreCase).ToList();
			var issues = new List<CollectionUserMetadataIssue>();
			NativeStateCaptureCoverage sortCoverage;
			List<CollectionCapturedSortAssignment> sortAssignments = CaptureSortAssignments(mods, issues, out sortCoverage);
			NativeStateCaptureCoverage screenshotCoverage;
			List<CollectionCapturedScreenshotOverride> screenshotOverrides = CaptureScreenshotOverrides(captureIdentity,
				mods, issues, cancellationToken, out screenshotCoverage);

			return new CollectionUserMetadataSnapshot(target, captureIdentity, nativeState.InstallLog.DeploymentCommitSequence,
				sortAssignments, sortCoverage, screenshotOverrides, screenshotCoverage, issues);
		}

		private List<CollectionCapturedSortAssignment> CaptureSortAssignments(IList<CollectionInstalledModIdentity> mods,
			List<CollectionUserMetadataIssue> issues, out NativeStateCaptureCoverage coverage)
		{
			var result = new List<CollectionCapturedSortAssignment>();
			if (mods.Count == 0)
			{
				coverage = NativeStateCaptureCoverage.NotApplicable;
				return result;
			}
			if (!_sortSurfaceAvailable || _sortAssignmentReader == null)
			{
				coverage = NativeStateCaptureCoverage.Unavailable;
				issues.Add(new CollectionUserMetadataIssue(CollectionUserMetadataIssueKind.SortServiceUnavailable,
					"sort", "The live mod Sort service is unavailable; current user Sort assignment state cannot be captured."));
				return result;
			}

			coverage = NativeStateCaptureCoverage.Complete;
			foreach (CollectionInstalledModIdentity mod in mods)
			{
				string archivePath = mod.Archive.LiveArchivePath;
				if (String.IsNullOrWhiteSpace(archivePath))
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.SortArchiveReferenceUnavailable,
						mod.NativeSnapshotKey, "The installed native member has no archive path for resolving its current Sort assignment.");
					continue;
				}

				ModSortOrderRecord record;
				try
				{
					record = _sortAssignmentReader(archivePath);
				}
				catch (Exception exception)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.SortAssignmentUnavailable,
						mod.NativeSnapshotKey, "The live Sort assignment could not be read: " + exception.Message);
					continue;
				}
				if (record == null)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.SortAssignmentUnavailable,
						mod.NativeSnapshotKey, "The live Sort service has no resolved assignment for the installed archive.");
					continue;
				}
				if (!Enum.IsDefined(typeof(ModSortOrderAssignmentState), record.AssignmentState))
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.SortAssignmentUnavailable,
						mod.NativeSnapshotKey, "The resolved Sort assignment contains an unknown persisted state.");
					continue;
				}

				result.Add(new CollectionCapturedSortAssignment(mod.NativeSnapshotKey, record.SortNumber,
					record.AssignmentState, record.ModId, record.DownloadId));
				if (record.AssignmentState == ModSortOrderAssignmentState.PendingAddIdentity)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.SortAssignmentPendingIdentity,
						mod.NativeSnapshotKey, "The current Sort assignment is still waiting for repository identity and is not a settled restorable binding.");
				}
				if (IdentityConflicts(record.ModId, mod.NexusModId) || IdentityConflicts(record.DownloadId, mod.NexusFileId))
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.SortAssignmentIdentityMismatch,
						mod.NativeSnapshotKey, "The resolved Sort assignment repository identity disagrees with the captured native registration.");
				}
			}
			return result;
		}

		private List<CollectionCapturedScreenshotOverride> CaptureScreenshotOverrides(LocalCaptureIdentity captureIdentity,
			IList<CollectionInstalledModIdentity> mods, List<CollectionUserMetadataIssue> issues,
			CancellationToken cancellationToken, out NativeStateCaptureCoverage coverage)
		{
			var result = new List<CollectionCapturedScreenshotOverride>();
			if (mods.Count == 0)
			{
				coverage = NativeStateCaptureCoverage.NotApplicable;
				return result;
			}
			if (!_screenshotSurfaceAvailable || _screenshotOverrideReader == null)
			{
				coverage = NativeStateCaptureCoverage.Unavailable;
				issues.Add(new CollectionUserMetadataIssue(CollectionUserMetadataIssueKind.ScreenshotMetadataUnavailable,
					"fomod-user-metadata", "The logical FOMod user-metadata reader is unavailable; screenshot overrides cannot be ruled in or out."));
				return result;
			}

			coverage = NativeStateCaptureCoverage.Complete;
			foreach (IGrouping<string, CollectionInstalledModIdentity> duplicate in mods
				.Where(x => !String.IsNullOrWhiteSpace(x.Archive.LiveArchivePath))
				.GroupBy(x => NormalizeArchiveKey(x.Archive.LiveArchivePath), StringComparer.OrdinalIgnoreCase)
				.Where(x => x.Count() > 1))
			{
				MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.AmbiguousArchiveMetadataBinding,
					duplicate.Key, "More than one captured native member refers to the same archive path, so path-scoped user metadata is ambiguous.");
			}

			foreach (CollectionInstalledModIdentity mod in mods)
			{
				cancellationToken.ThrowIfCancellationRequested();
				string archivePath = mod.Archive.LiveArchivePath;
				if (String.IsNullOrWhiteSpace(archivePath))
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.ScreenshotArchiveReferenceUnavailable,
						mod.NativeSnapshotKey, "The installed native member has no archive path for reading path-scoped FOMod user metadata.");
					continue;
				}

				FOModScreenshotOverrideReadResult readResult;
				try
				{
					readResult = _screenshotOverrideReader(archivePath);
				}
				catch (Exception exception)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.ScreenshotOverrideInvalid,
						mod.NativeSnapshotKey, "The logical FOMod screenshot override could not be read: " + exception.Message);
					continue;
				}
				if (readResult == null)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.ScreenshotOverrideInvalid,
						mod.NativeSnapshotKey, "The logical FOMod user-metadata reader returned no result.");
					continue;
				}
				if (readResult.State == FOModScreenshotOverrideReadState.None ||
					readResult.State == FOModScreenshotOverrideReadState.Stale)
					continue;
				if (readResult.State == FOModScreenshotOverrideReadState.ArchiveUnavailable)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.ScreenshotOverrideArchiveUnavailable,
						mod.NativeSnapshotKey, "A persisted screenshot override exists, but its archive is unavailable so current relevance cannot be verified.");
					continue;
				}
				if (readResult.State != FOModScreenshotOverrideReadState.Current || readResult.Record == null)
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.ScreenshotOverrideInvalid,
						mod.NativeSnapshotKey, "The logical FOMod screenshot override returned an unsupported state.");
					continue;
				}

				FOModScreenshotOverrideRecord screenshot = readResult.Record;
				byte[] bytes = screenshot.ScreenshotData;
				if (bytes == null || bytes.Length == 0 || String.IsNullOrWhiteSpace(screenshot.ScreenshotPath))
				{
					MarkPartial(issues, ref coverage, CollectionUserMetadataIssueKind.ScreenshotOverrideInvalid,
						mod.NativeSnapshotKey, "The current screenshot override does not contain a valid path and non-empty payload.");
					continue;
				}

				string role = CreateScreenshotRole(mod.NativeSnapshotKey);
				CollectionsRetainedArtifact artifact;
				using (var stream = new MemoryStream(bytes, false))
					artifact = _artifactStore.Publish(stream, cancellationToken);
				_referenceStore.AcquireExclusiveRoleReference(artifact.ArtifactId,
					CollectionsRetainedArtifactOwnerKind.Capture, captureIdentity.ToString(), role);
				var retained = new RetainedArtifactReference(artifact.ArtifactId, role, artifact.ContentHash, artifact.ByteLength);
				result.Add(new CollectionCapturedScreenshotOverride(mod.NativeSnapshotKey, screenshot.ScreenshotPath,
					screenshot.ArchiveLength, screenshot.ArchiveWriteTimeUtcTicks, screenshot.UpdatedUtcTicks, retained));
			}
			return result;
		}

		private static bool IdentityConflicts(string recorded, string captured)
		{
			return !String.IsNullOrWhiteSpace(recorded) && !String.IsNullOrWhiteSpace(captured) &&
				!StringComparer.OrdinalIgnoreCase.Equals(recorded.Trim(), captured.Trim());
		}

		private static string NormalizeArchiveKey(string path)
		{
			try
			{
				return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
			}
			catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
			{
				return (path ?? String.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).ToUpperInvariant();
			}
		}

		private static void MarkPartial(List<CollectionUserMetadataIssue> issues, ref NativeStateCaptureCoverage coverage,
			CollectionUserMetadataIssueKind kind, string resourceKey, string message)
		{
			coverage = NativeStateCaptureCoverage.Partial;
			issues.Add(new CollectionUserMetadataIssue(kind, resourceKey, message));
		}

		private static string CreateScreenshotRole(string nativeSnapshotKey)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			using (SHA256 sha256 = SHA256.Create())
			{
				byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(nativeSnapshotKey.ToUpperInvariant()));
				var builder = new StringBuilder(hash.Length * 2);
				foreach (byte value in hash)
					builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
				return "user-metadata-screenshot:" + builder.ToString();
			}
		}
	}
}
