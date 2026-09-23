using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes whether a C6.4 impact plan can proceed to later preparation/review gates.</summary>
	public enum CollectionConflictImpactStatus
	{
		Unknown = 0,
		Ready = 1,
		PreparationRequired = 2,
		ActionRequired = 3,
		Blocked = 4
	}

	/// <summary>Classifies one C6.4 conflict/impact finding.</summary>
	public enum CollectionConflictImpactIssueKind
	{
		Unknown = 0,
		UpstreamPlanBlocked = 1,
		AcquisitionOrTranslationRequired = 2,
		EffectPreviewIncomplete = 3,
		EffectPreviewMismatch = 4,
		FilePriorityCycle = 5,
		FileWinnerDecisionRequired = 6,
		ExistingFileWinnerDecisionRequired = 7,
		PluginStateUnavailable = 8,
		ConfigurationWinnerDecisionRequired = 9,
		ConflictingPluginIntent = 10,
		ExistingUserOverride = 11,
		AffectedAssociationRequiresRecovery = 12,
		AffectedAssociationModified = 13,
		ExistingFileOwnershipUnresolved = 14,
		PluginOrderImpactRequiresReview = 15,
		ExistingPluginStateDecisionRequired = 16,
		ExistingConfigurationDecisionRequired = 17
	}

	/// <summary>One deterministic C6.4 planning issue.</summary>
	public sealed class CollectionConflictImpactIssue
	{
		/// <summary>Creates one impact issue.</summary>
		public CollectionConflictImpactIssue(CollectionConflictImpactIssueKind kind, CollectionConflictImpactStatus status,
			CollectionMemberKey memberKey, string subjectKey, string message)
		{
			if (!Enum.IsDefined(typeof(CollectionConflictImpactIssueKind), kind) || kind == CollectionConflictImpactIssueKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (!Enum.IsDefined(typeof(CollectionConflictImpactStatus), status) || status == CollectionConflictImpactStatus.Unknown || status == CollectionConflictImpactStatus.Ready)
				throw new ArgumentOutOfRangeException(nameof(status));
			Kind = kind;
			Status = status;
			MemberKey = memberKey;
			SubjectKey = subjectKey ?? String.Empty;
			Message = message ?? String.Empty;
		}
		public CollectionConflictImpactIssueKind Kind { get; }
		public CollectionConflictImpactStatus Status { get; }
		public CollectionMemberKey MemberKey { get; }
		public string SubjectKey { get; }
		public string Message { get; }
	}

	/// <summary>One planned root-aware file winner and its current native owner.</summary>
	public sealed class CollectionFileImpact
	{
		private readonly ReadOnlyCollection<CollectionMemberKey> _writers;
		private readonly ReadOnlyCollection<Guid> _affectedAssociationIds;

		internal CollectionFileImpact(ModDeploymentTarget target, IEnumerable<CollectionMemberKey> writers,
			CollectionMemberKey plannedWinner, string currentOwnerKey, IEnumerable<Guid> affectedAssociationIds)
		{
			Target = target ?? throw new ArgumentNullException(nameof(target));
			_writers = new ReadOnlyCollection<CollectionMemberKey>((writers ?? throw new ArgumentNullException(nameof(writers))).ToList());
			PlannedWinner = plannedWinner;
			CurrentOwnerKey = currentOwnerKey;
			_affectedAssociationIds = new ReadOnlyCollection<Guid>((affectedAssociationIds ?? Enumerable.Empty<Guid>()).Distinct().OrderBy(x => x).ToList());
		}
		public ModDeploymentTarget Target { get; }
		public ReadOnlyCollection<CollectionMemberKey> Writers { get { return _writers; } }
		public CollectionMemberKey PlannedWinner { get; }
		public string CurrentOwnerKey { get; }
		public ReadOnlyCollection<Guid> AffectedAssociationIds { get { return _affectedAssociationIds; } }
	}

	/// <summary>One plugin effect and the observed plugin state it may change.</summary>
	public sealed class CollectionPluginImpact
	{
		private readonly ReadOnlyCollection<Guid> _affectedAssociationIds;
		internal CollectionPluginImpact(CollectionMemberKey memberKey, CollectionPlannedPluginEffect effect,
			CollectionNativePluginState currentState, IEnumerable<Guid> affectedAssociationIds)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			Effect = effect ?? throw new ArgumentNullException(nameof(effect));
			CurrentState = currentState;
			_affectedAssociationIds = new ReadOnlyCollection<Guid>((affectedAssociationIds ?? Enumerable.Empty<Guid>()).Distinct().OrderBy(x => x).ToList());
		}
		public CollectionMemberKey MemberKey { get; }
		public CollectionPlannedPluginEffect Effect { get; }
		public CollectionNativePluginState CurrentState { get; }
		public ReadOnlyCollection<Guid> AffectedAssociationIds { get { return _affectedAssociationIds; } }
	}

	/// <summary>Identifies the configuration domain touched by one planned effect.</summary>
	public enum CollectionConfigurationImpactKind
	{
		Unknown = 0,
		Ini = 1,
		GameSpecificValue = 2
	}

	/// <summary>One supported configuration effect and its current managed owner.</summary>
	public sealed class CollectionConfigurationImpact
	{
		private readonly ReadOnlyCollection<Guid> _affectedAssociationIds;
		internal CollectionConfigurationImpact(CollectionMemberKey memberKey, CollectionConfigurationImpactKind kind,
			string subjectKey, string currentOwnerKey, bool changesRecordedValue, IEnumerable<Guid> affectedAssociationIds)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionConfigurationImpactKind), kind) || kind == CollectionConfigurationImpactKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			SubjectKey = subjectKey ?? String.Empty;
			CurrentOwnerKey = currentOwnerKey;
			ChangesRecordedValue = changesRecordedValue;
			_affectedAssociationIds = new ReadOnlyCollection<Guid>((affectedAssociationIds ?? Enumerable.Empty<Guid>()).Distinct().OrderBy(x => x).ToList());
		}
		public CollectionMemberKey MemberKey { get; }
		public CollectionConfigurationImpactKind Kind { get; }
		public string SubjectKey { get; }
		public string CurrentOwnerKey { get; }
		public bool ChangesRecordedValue { get; }
		public ReadOnlyCollection<Guid> AffectedAssociationIds { get { return _affectedAssociationIds; } }
	}

	/// <summary>Why an existing Collection association is affected by the incoming additive plan.</summary>
	[Flags]
	public enum CollectionAssociationImpactKind
	{
		None = 0,
		SharedNativeInstance = 1,
		FileWinner = 2,
		PluginState = 4,
		ConfigurationState = 8,
		UserOverride = 16
	}

	/// <summary>One existing target association that later review/reconciliation must preserve or explicitly update.</summary>
	public sealed class CollectionAssociationImpact
	{
		internal CollectionAssociationImpact(CollectionTargetAssociation association, CollectionAssociationImpactKind kind)
		{
			Association = association ?? throw new ArgumentNullException(nameof(association));
			Kind = kind;
		}
		public CollectionTargetAssociation Association { get; }
		public CollectionAssociationImpactKind Kind { get; }
	}

	/// <summary>Immutable C6.4 conflict/impact result for one exact C6.3 plan and C6.1 state observation.</summary>
	public sealed class CollectionConflictImpactPlan
	{
		private readonly ReadOnlyCollection<CollectionFileImpact> _fileImpacts;
		private readonly ReadOnlyCollection<CollectionPluginImpact> _pluginImpacts;
		private readonly ReadOnlyCollection<CollectionConfigurationImpact> _configurationImpacts;
		private readonly ReadOnlyCollection<CollectionAssociationImpact> _associationImpacts;
		private readonly ReadOnlyCollection<CollectionConflictImpactIssue> _issues;

		internal CollectionConflictImpactPlan(ResolvedCollectionPlan plan, CollectionNativeStateIndex nativeState,
			IEnumerable<CollectionFileImpact> fileImpacts, IEnumerable<CollectionPluginImpact> pluginImpacts,
			IEnumerable<CollectionConfigurationImpact> configurationImpacts, IEnumerable<CollectionAssociationImpact> associationImpacts,
			IEnumerable<CollectionConflictImpactIssue> issues)
			: this(plan, nativeState == null ? null : nativeState.Fingerprint, fileImpacts, pluginImpacts, configurationImpacts, associationImpacts, issues)
		{
			if (nativeState == null) throw new ArgumentNullException(nameof(nativeState));
		}

		/// <summary>Reconstructs persisted reviewed impacts against the plan's original approved state fingerprint.</summary>
		internal CollectionConflictImpactPlan(ResolvedCollectionPlan plan, CollectionCurrentStateFingerprint stateFingerprint,
			IEnumerable<CollectionFileImpact> fileImpacts, IEnumerable<CollectionPluginImpact> pluginImpacts,
			IEnumerable<CollectionConfigurationImpact> configurationImpacts, IEnumerable<CollectionAssociationImpact> associationImpacts,
			IEnumerable<CollectionConflictImpactIssue> issues)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (stateFingerprint == null) throw new ArgumentNullException(nameof(stateFingerprint));
			if (!stateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new ArgumentException("A reviewed impact plan must remain bound to the plan's approved native-state fingerprint.", nameof(stateFingerprint));
			PlanIdentity = plan.Identity;
			Target = plan.Target;
			StateFingerprint = stateFingerprint;
			_fileImpacts = Copy(fileImpacts, nameof(fileImpacts));
			_pluginImpacts = Copy(pluginImpacts, nameof(pluginImpacts));
			_configurationImpacts = Copy(configurationImpacts, nameof(configurationImpacts));
			_associationImpacts = Copy(associationImpacts, nameof(associationImpacts));
			_issues = Copy(issues, nameof(issues));
			Status = DetermineStatus(_issues);
		}

		public CollectionPlanIdentity PlanIdentity { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public CollectionConflictImpactStatus Status { get; }
		public ReadOnlyCollection<CollectionFileImpact> FileImpacts { get { return _fileImpacts; } }
		public ReadOnlyCollection<CollectionPluginImpact> PluginImpacts { get { return _pluginImpacts; } }
		public ReadOnlyCollection<CollectionConfigurationImpact> ConfigurationImpacts { get { return _configurationImpacts; } }
		public ReadOnlyCollection<CollectionAssociationImpact> AssociationImpacts { get { return _associationImpacts; } }
		public ReadOnlyCollection<CollectionConflictImpactIssue> Issues { get { return _issues; } }
		public bool IsReady { get { return Status == CollectionConflictImpactStatus.Ready; } }
		public bool IsBlocked { get { return Status == CollectionConflictImpactStatus.Blocked; } }

		private static CollectionConflictImpactStatus DetermineStatus(IEnumerable<CollectionConflictImpactIssue> issues)
		{
			CollectionConflictImpactStatus status = CollectionConflictImpactStatus.Ready;
			foreach (CollectionConflictImpactIssue issue in issues)
				if ((int)issue.Status > (int)status) status = issue.Status;
			return status;
		}

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName) where T : class
		{
			if (values == null) throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => x == null)) throw new ArgumentException("A conflict/impact plan cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
