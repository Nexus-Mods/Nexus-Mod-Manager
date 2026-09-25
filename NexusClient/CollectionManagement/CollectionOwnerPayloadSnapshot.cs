using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Classifies a non-fatal limitation observed while retaining file-owner/fallback payloads for a Local Collection capture.
	/// </summary>
	public enum CollectionOwnerPayloadIssueKind
	{
		DeploymentTopologyUnavailable = 1,
		OwnerUnresolved = 2,
		PayloadSourceUnavailable = 3,
		PayloadSourceMissing = 4,
		LegacyPayloadSourceUnavailable = 5
	}

	/// <summary>
	/// Records one C7.3 ownership/payload capture limitation without claiming local restorability.
	/// </summary>
	public sealed class CollectionOwnerPayloadIssue
	{
		/// <summary>Creates one immutable owner-payload capture issue.</summary>
		public CollectionOwnerPayloadIssue(CollectionOwnerPayloadIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionOwnerPayloadIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionOwnerPayloadIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>
	/// Identifies one immutable Collections-retained payload together with the capture role that protects it from cleanup.
	/// </summary>
	public sealed class CollectionOwnerPayloadRetention
	{
		/// <summary>Creates one immutable retained owner-payload descriptor.</summary>
		public CollectionOwnerPayloadRetention(string stableArtifactId, string referenceRole,
			CollectionContentHash contentHash, long byteLength)
		{
			if (String.IsNullOrWhiteSpace(stableArtifactId))
				throw new ArgumentException("A retained artifact identity is required.", nameof(stableArtifactId));
			if (String.IsNullOrWhiteSpace(referenceRole))
				throw new ArgumentException("A retained artifact reference role is required.", nameof(referenceRole));
			if (contentHash == null)
				throw new ArgumentNullException(nameof(contentHash));
			if (byteLength < 0)
				throw new ArgumentOutOfRangeException(nameof(byteLength));
			StableArtifactId = stableArtifactId;
			ReferenceRole = referenceRole;
			ContentHash = contentHash;
			ByteLength = byteLength;
		}

		public string StableArtifactId { get; }
		public string ReferenceRole { get; }
		public CollectionContentHash ContentHash { get; }
		public long ByteLength { get; }
	}

	/// <summary>
	/// Captures one owner in the exact fallback-to-winner order for a deployment target.
	/// </summary>
	public sealed class CollectionOwnerPayloadOwner
	{
		/// <summary>Creates one immutable owner/topology record.</summary>
		public CollectionOwnerPayloadOwner(int stackIndex, string ownerKey, string ownerReference,
			NativeStateCaptureDeploymentOwnerKind kind, bool currentWinner, CollectionOwnerPayloadRetention retainedPayload)
		{
			if (stackIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(stackIndex));
			if (!Enum.IsDefined(typeof(NativeStateCaptureDeploymentOwnerKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (kind != NativeStateCaptureDeploymentOwnerKind.Unresolved && String.IsNullOrWhiteSpace(ownerKey))
				throw new ArgumentException("A resolved deployment owner requires its native owner key.", nameof(ownerKey));
			StackIndex = stackIndex;
			OwnerKey = ownerKey ?? String.Empty;
			OwnerReference = ownerReference ?? String.Empty;
			Kind = kind;
			CurrentWinner = currentWinner;
			RetainedPayload = retainedPayload;
		}

		public int StackIndex { get; }
		public string OwnerKey { get; }
		public string OwnerReference { get; }
		public NativeStateCaptureDeploymentOwnerKind Kind { get; }
		public bool CurrentWinner { get; }
		public CollectionOwnerPayloadRetention RetainedPayload { get; }
	}

	/// <summary>
	/// Captures one method-neutral file target, its exact current owner order and any retained payloads required by that order.
	/// </summary>
	public sealed class CollectionOwnerPayloadTarget
	{
		private readonly ReadOnlyCollection<CollectionOwnerPayloadOwner> _owners;

		/// <summary>Creates one immutable target ownership snapshot.</summary>
		public CollectionOwnerPayloadTarget(ModDeploymentTarget target, bool promoted,
			IEnumerable<CollectionOwnerPayloadOwner> owners)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			List<CollectionOwnerPayloadOwner> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Count == 0)
				throw new ArgumentException("A captured deployment target requires at least one current owner.", nameof(owners));
			if (copied.Any(x => x == null))
				throw new ArgumentException("A captured deployment target cannot contain null owners.", nameof(owners));
			for (int index = 0; index < copied.Count; index++)
			{
				if (copied[index].StackIndex != index)
					throw new ArgumentException("Captured owner stack indexes must be contiguous and ordered.", nameof(owners));
				if (copied[index].CurrentWinner != (index == copied.Count - 1))
					throw new ArgumentException("The final captured owner must be the unique current winner.", nameof(owners));
			}
			Promoted = promoted;
			_owners = new ReadOnlyCollection<CollectionOwnerPayloadOwner>(copied);
		}

		public ModDeploymentTarget Target { get; }
		public bool Promoted { get; }
		public ReadOnlyCollection<CollectionOwnerPayloadOwner> Owners { get { return _owners; } }
		public CollectionOwnerPayloadOwner CurrentWinner { get { return _owners[_owners.Count - 1]; } }
	}

	/// <summary>
	/// Immutable C7.3 capture of current file-owner topology plus independently retained owner/fallback payload bytes.
	/// </summary>
	/// <remarks>
	/// This snapshot is not a sealed Local Collection and does not itself promise restoration. Missing/unresolved payloads are
	/// represented explicitly so the later C7 completeness gate can distinguish recipe-only from locally-restorable scope.
	/// </remarks>
	public sealed class CollectionOwnerPayloadSnapshot
	{
		/// <summary>Creates one immutable owner/fallback-payload capture.</summary>
		public CollectionOwnerPayloadSnapshot(CollectionTargetIdentity target, LocalCaptureIdentity captureIdentity,
			long deploymentCommitSequence, IEnumerable<CollectionOwnerPayloadTarget> targets,
			NativeStateCaptureCoverage coverage, IEnumerable<CollectionOwnerPayloadIssue> issues)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			CaptureIdentity = captureIdentity ?? throw new ArgumentNullException(nameof(captureIdentity));
			if (!Enum.IsDefined(typeof(NativeStateCaptureCoverage), coverage))
				throw new ArgumentOutOfRangeException(nameof(coverage));
			DeploymentCommitSequence = deploymentCommitSequence;
			Targets = Copy(targets, nameof(targets));
			Coverage = coverage;
			Issues = Copy(issues, nameof(issues));
		}

		public CollectionTargetIdentity Target { get; }
		public LocalCaptureIdentity CaptureIdentity { get; }
		/// <summary>Gets the native deployment checkpoint observed before payload retention began.</summary>
		public long DeploymentCommitSequence { get; }
		public ReadOnlyCollection<CollectionOwnerPayloadTarget> Targets { get; }
		public NativeStateCaptureCoverage Coverage { get; }
		public ReadOnlyCollection<CollectionOwnerPayloadIssue> Issues { get; }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("An owner-payload snapshot cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
