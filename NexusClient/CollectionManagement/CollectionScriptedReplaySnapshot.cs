using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies a non-fatal limitation observed while retaining scripted replay artifacts for a Local Collection capture.
	/// </summary>
	public enum CollectionScriptedReplayIssueKind
	{
		ReplayReferenceUnavailable = 1,
		ReplayReferenceAmbiguous = 2,
		ReplayPathCollision = 3,
		ReplayFileMissing = 4,
		ReplayPlanInvalid = 5,
		ReplayFormatIncomplete = 6,
		GeneratedPayloadMissing = 7,
		GeneratedPayloadChangedDuringCapture = 8
	}

	/// <summary>
	/// Records one C7.4 scripted-replay capture limitation without claiming local restorability.
	/// </summary>
	public sealed class CollectionScriptedReplayIssue
	{
		/// <summary>Creates one immutable scripted-replay capture issue.</summary>
		public CollectionScriptedReplayIssue(CollectionScriptedReplayIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionScriptedReplayIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionScriptedReplayIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Identifies one generated-file sidecar retained from an ordered scripted replay operation.
	/// </summary>
	public sealed class CollectionScriptedGeneratedPayload
	{
		/// <summary>Creates one immutable generated replay-payload descriptor.</summary>
		public CollectionScriptedGeneratedPayload(int replayOperationIndex, string payloadFileName, string destinationPath,
			RetainedArtifactReference retainedArtifact)
		{
			if (replayOperationIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(replayOperationIndex));
			if (String.IsNullOrWhiteSpace(payloadFileName) ||
				!StringComparer.Ordinal.Equals(PathLeaf(payloadFileName), payloadFileName))
				throw new ArgumentException("A generated replay payload requires one sidecar file name.", nameof(payloadFileName));
			ReplayOperationIndex = replayOperationIndex;
			PayloadFileName = payloadFileName;
			DestinationPath = destinationPath ?? String.Empty;
			RetainedArtifact = retainedArtifact ?? throw new ArgumentNullException(nameof(retainedArtifact));
		}

		public int ReplayOperationIndex { get; }
		public string PayloadFileName { get; }
		public string DestinationPath { get; }
		public RetainedArtifactReference RetainedArtifact { get; }

		private static string PathLeaf(string path)
		{
			int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
			return slash < 0 ? path : path.Substring(slash + 1);
		}
	}

	/// <summary>
	/// Captures one scripted native member's replay XML plus independently retained generated-file sidecars.
	/// </summary>
	/// <remarks>
	/// Live InstallInfo paths are deliberately excluded. The native snapshot key associates this artifact set with the exact
	/// captured native registration, while Collection provenance associates it with any verified member/recipe mappings known at
	/// capture time. Restore code must remap the native key rather than treating it as a permanent Local Collection identity.
	/// </remarks>
	public sealed class CollectionScriptedReplayArtifactSet
	{
		private readonly ReadOnlyCollection<CollectionScriptedGeneratedPayload> _generatedPayloads;
		private readonly ReadOnlyCollection<CollectionInstalledMemberProvenance> _provenance;

		/// <summary>Creates one immutable retained scripted-replay artifact set.</summary>
		public CollectionScriptedReplayArtifactSet(string nativeSnapshotKey, RetainedArtifactReference replayXml,
			bool completeReplayFormat, bool replayPlanValidated, int replayOperationCount,
			IEnumerable<CollectionScriptedGeneratedPayload> generatedPayloads,
			IEnumerable<CollectionInstalledMemberProvenance> provenance)
		{
			if (String.IsNullOrWhiteSpace(nativeSnapshotKey))
				throw new ArgumentException("A native snapshot key is required.", nameof(nativeSnapshotKey));
			if (replayOperationCount < 0)
				throw new ArgumentOutOfRangeException(nameof(replayOperationCount));
			NativeSnapshotKey = nativeSnapshotKey;
			ReplayXml = replayXml ?? throw new ArgumentNullException(nameof(replayXml));
			CompleteReplayFormat = completeReplayFormat;
			ReplayPlanValidated = replayPlanValidated;
			ReplayOperationCount = replayOperationCount;
			_generatedPayloads = Copy(generatedPayloads, nameof(generatedPayloads));
			_provenance = Copy(provenance, nameof(provenance));
		}

		public string NativeSnapshotKey { get; }
		public RetainedArtifactReference ReplayXml { get; }
		/// <summary>Gets whether the live XML declared the complete replay format before it was retained.</summary>
		public bool CompleteReplayFormat { get; }
		/// <summary>Gets whether the ordered replay plan and all referenced generated payload metadata validated.</summary>
		public bool ReplayPlanValidated { get; }
		/// <summary>Gets the ordered operation count when <see cref="ReplayPlanValidated"/> is true; otherwise zero.</summary>
		public int ReplayOperationCount { get; }
		public ReadOnlyCollection<CollectionScriptedGeneratedPayload> GeneratedPayloads { get { return _generatedPayloads; } }
		public ReadOnlyCollection<CollectionInstalledMemberProvenance> Provenance { get { return _provenance; } }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A scripted-replay artifact set cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}

	/// <summary>
	/// Immutable C7.4 capture of scripted replay XML and generated sidecars protected by Collections retained references.
	/// </summary>
	/// <remarks>
	/// This snapshot is not a sealed Local Collection. Missing, colliding, legacy or invalid replay state is represented explicitly
	/// so C7.7 can refuse a locally-restorable claim instead of silently relying on mutable InstallInfo files.
	/// </remarks>
	public sealed class CollectionScriptedReplaySnapshot
	{
		/// <summary>Creates one immutable scripted-replay capture snapshot.</summary>
		public CollectionScriptedReplaySnapshot(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			long deploymentCommitSequence, IEnumerable<CollectionScriptedReplayArtifactSet> artifactSets,
			NativeStateCaptureCoverage coverage, IEnumerable<CollectionScriptedReplayIssue> issues)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			CaptureIdentity = captureIdentity ?? throw new ArgumentNullException(nameof(captureIdentity));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), coverage))
				throw new ArgumentOutOfRangeException(nameof(coverage));
			DeploymentCommitSequence = deploymentCommitSequence;
			ArtifactSets = Copy(artifactSets, nameof(artifactSets));
			Coverage = coverage;
			Issues = Copy(issues, nameof(issues));
		}

		public CollectionTargetIdentity Target { get; }
		public LocalCaptureIdentity CaptureIdentity { get; }
		/// <summary>Gets the native deployment checkpoint shared with the C7.2 identity snapshot.</summary>
		public long DeploymentCommitSequence { get; }
		public ReadOnlyCollection<CollectionScriptedReplayArtifactSet> ArtifactSets { get; }
		public NativeStateCaptureCoverage Coverage { get; }
		public ReadOnlyCollection<CollectionScriptedReplayIssue> Issues { get; }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A scripted-replay snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
