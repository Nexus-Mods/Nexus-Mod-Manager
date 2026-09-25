using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Classifies how C7.9 intends to satisfy one captured native mod registration.</summary>
	public enum CollectionLocalRestoreMemberAction
	{
		ReuseExistingNative = 1,
		RecreateFromRetainedArchive = 2,
		ManualReviewRequired = 3
	}

	/// <summary>Classifies the target of one captured owner-key remap without inventing future native keys.</summary>
	public enum CollectionLocalRestoreOwnerBindingKind
	{
		OriginalValue = 1,
		ExistingNative = 2,
		RecreatedSnapshotMember = 3,
		Unresolved = 4
	}

	/// <summary>Classifies a condition which blocks automatic execution of a C7.9 restore plan.</summary>
	public enum CollectionLocalRestorePlanIssueKind
	{
		CaptureNotLocallyRestorable = 1,
		UnsupportedCaptureVersion = 2,
		TargetMismatch = 3,
		CaptureSnapshotMismatch = 4,
		NativeMappingMissing = 5,
		NativeMappingAmbiguous = 6,
		CurrentNativeStateAmbiguous = 7,
		CurrentNativeMatchAmbiguous = 8,
		RetainedArchiveMissing = 9,
		RetainedArtifactMissing = 10,
		RetainedArtifactMetadataMismatch = 11,
		RetainedArtifactCorrupt = 12,
		CapturedOwnerUnresolved = 13,
		CapturedOwnerMethodMismatch = 14,
		CurrentOriginalOwnerUnavailable = 15
	}

	/// <summary>Records one deterministic C7.9 planning condition requiring review before native mutation.</summary>
	public sealed class CollectionLocalRestorePlanIssue
	{
		public CollectionLocalRestorePlanIssue(CollectionLocalRestorePlanIssueKind kind, string resourceKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionLocalRestorePlanIssueKind), kind))
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			ResourceKey = resourceKey ?? String.Empty;
			Message = message ?? String.Empty;
		}

		public CollectionLocalRestorePlanIssueKind Kind { get; }
		public string ResourceKey { get; }
		public string Message { get; }
	}

	/// <summary>Describes how one captured Local Collection member maps to native state before restoration begins.</summary>
	public sealed class CollectionLocalRestoreMemberPlan
	{
		public CollectionLocalRestoreMemberPlan(CollectionMemberKey snapshotMemberKey, string capturedNativeKey,
			CollectionLocalRestoreMemberAction action, string currentNativeKey, ModInstallContext installContext,
			CollectionCapturedArchiveArtifact retainedArchive)
		{
			SnapshotMemberKey = snapshotMemberKey ?? throw new ArgumentNullException(nameof(snapshotMemberKey));
			if (String.IsNullOrWhiteSpace(capturedNativeKey))
				throw new ArgumentException("A captured native key is required.", nameof(capturedNativeKey));
			if (!Enum.IsDefined(typeof(CollectionLocalRestoreMemberAction), action))
				throw new ArgumentOutOfRangeException(nameof(action));
			if (action == CollectionLocalRestoreMemberAction.ReuseExistingNative && String.IsNullOrWhiteSpace(currentNativeKey))
				throw new ArgumentException("Reusing a native registration requires its current native key.", nameof(currentNativeKey));
			if (installContext == null)
				throw new ArgumentNullException(nameof(installContext));
			CapturedNativeKey = capturedNativeKey;
			Action = action;
			CurrentNativeKey = currentNativeKey ?? String.Empty;
			InstallContext = new ModInstallContext(installContext.Method, installContext.InstallRoot);
			RetainedArchive = retainedArchive;
		}

		public CollectionMemberKey SnapshotMemberKey { get; }
		public string CapturedNativeKey { get; }
		public CollectionLocalRestoreMemberAction Action { get; }
		public string CurrentNativeKey { get; }
		public ModInstallContext InstallContext { get; }
		public CollectionCapturedArchiveArtifact RetainedArchive { get; }
	}

	/// <summary>Maps one captured owner reference to either current native state or a future recreated member.</summary>
	public sealed class CollectionLocalRestoreOwnerBinding
	{
		public CollectionLocalRestoreOwnerBinding(int stackIndex, string capturedOwnerKey,
			NativeStateCaptureDeploymentOwnerKind capturedOwnerKind, bool currentWinner,
			CollectionLocalRestoreOwnerBindingKind bindingKind, string currentNativeKey,
			CollectionMemberKey recreatedSnapshotMember)
		{
			if (stackIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(stackIndex));
			if (!Enum.IsDefined(typeof(NativeStateCaptureDeploymentOwnerKind), capturedOwnerKind))
				throw new ArgumentOutOfRangeException(nameof(capturedOwnerKind));
			if (!Enum.IsDefined(typeof(CollectionLocalRestoreOwnerBindingKind), bindingKind))
				throw new ArgumentOutOfRangeException(nameof(bindingKind));
			StackIndex = stackIndex;
			CapturedOwnerKey = capturedOwnerKey ?? String.Empty;
			CapturedOwnerKind = capturedOwnerKind;
			CurrentWinner = currentWinner;
			BindingKind = bindingKind;
			CurrentNativeKey = currentNativeKey ?? String.Empty;
			RecreatedSnapshotMember = recreatedSnapshotMember;
		}

		public int StackIndex { get; }
		public string CapturedOwnerKey { get; }
		public NativeStateCaptureDeploymentOwnerKind CapturedOwnerKind { get; }
		public bool CurrentWinner { get; }
		public CollectionLocalRestoreOwnerBindingKind BindingKind { get; }
		public string CurrentNativeKey { get; }
		public CollectionMemberKey RecreatedSnapshotMember { get; }
	}

	/// <summary>Describes the exact captured owner order that C7.10 will later reconstruct for one deployment target.</summary>
	public sealed class CollectionLocalRestoreDeploymentPlan
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreOwnerBinding> _owners;

		public CollectionLocalRestoreDeploymentPlan(ModDeploymentTarget target, bool promoted,
			IEnumerable<CollectionLocalRestoreOwnerBinding> owners)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			Promoted = promoted;
			List<CollectionLocalRestoreOwnerBinding> copied = (owners ?? throw new ArgumentNullException(nameof(owners))).ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A restore deployment plan cannot contain null owner bindings.", nameof(owners));
			_owners = new ReadOnlyCollection<CollectionLocalRestoreOwnerBinding>(copied);
		}

		public ModDeploymentTarget Target { get; }
		public bool Promoted { get; }
		public ReadOnlyCollection<CollectionLocalRestoreOwnerBinding> Owners { get { return _owners; } }
	}

	/// <summary>Immutable deterministic output of the read-only C7.9 native mapping/remap planner.</summary>
	public sealed class CollectionLocalRestorePlan
	{
		private readonly ReadOnlyCollection<CollectionLocalRestoreMemberPlan> _members;
		private readonly ReadOnlyCollection<CollectionLocalRestoreDeploymentPlan> _deploymentTargets;
		private readonly ReadOnlyCollection<string> _currentNativeKeysToRemove;
		private readonly ReadOnlyCollection<CollectionLocalRestorePlanIssue> _issues;

		internal CollectionLocalRestorePlan(LocalCaptureIdentity captureIdentity, CollectionTargetIdentity target,
			CollectionCurrentStateFingerprint currentStateFingerprint, long currentDeploymentCommitSequence,
			string currentOriginalValuesKey, string planFingerprint, IEnumerable<CollectionLocalRestoreMemberPlan> members,
			IEnumerable<CollectionLocalRestoreDeploymentPlan> deploymentTargets,
			IEnumerable<string> currentNativeKeysToRemove, IEnumerable<CollectionLocalRestorePlanIssue> issues)
		{
			CaptureIdentity = captureIdentity ?? throw new ArgumentNullException(nameof(captureIdentity));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			CurrentStateFingerprint = currentStateFingerprint ?? throw new ArgumentNullException(nameof(currentStateFingerprint));
			if (String.IsNullOrWhiteSpace(planFingerprint))
				throw new ArgumentException("A deterministic restore-plan fingerprint is required.", nameof(planFingerprint));
			CurrentDeploymentCommitSequence = currentDeploymentCommitSequence;
			CurrentOriginalValuesKey = currentOriginalValuesKey ?? String.Empty;
			PlanFingerprint = planFingerprint;
			_members = Copy(members, nameof(members));
			_deploymentTargets = Copy(deploymentTargets, nameof(deploymentTargets));
			_currentNativeKeysToRemove = new ReadOnlyCollection<string>((currentNativeKeysToRemove ?? throw new ArgumentNullException(nameof(currentNativeKeysToRemove))).ToList());
			_issues = Copy(issues, nameof(issues));
		}

		public LocalCaptureIdentity CaptureIdentity { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionCurrentStateFingerprint CurrentStateFingerprint { get; }
		public long CurrentDeploymentCommitSequence { get; }
		/// <summary>Gets the current native original-values owner key used when remapping captured original effects/owners.</summary>
		public string CurrentOriginalValuesKey { get; }
		public string PlanFingerprint { get; }
		public ReadOnlyCollection<CollectionLocalRestoreMemberPlan> Members { get { return _members; } }
		public ReadOnlyCollection<CollectionLocalRestoreDeploymentPlan> DeploymentTargets { get { return _deploymentTargets; } }
		public ReadOnlyCollection<string> CurrentNativeKeysToRemove { get { return _currentNativeKeysToRemove; } }
		public ReadOnlyCollection<CollectionLocalRestorePlanIssue> Issues { get { return _issues; } }
		public bool IsReadyForReview { get { return _issues.Count == 0; } }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName)
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => ReferenceEquals(x, null)))
				throw new ArgumentException("A restore plan cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
