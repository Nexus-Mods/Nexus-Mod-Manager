using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes why C6.3 could not produce a dependency/phase execution ordering.
	/// </summary>
	public enum CollectionDependencyPhaseIssueKind
	{
		Unknown = 0,
		BlockedMemberMatch = 1,
		PhaseOrderingConflict = 2,
		DependencyCycle = 3
	}

	/// <summary>
	/// Describes the visibility boundary between two non-empty Collection installation phases.
	/// </summary>
	public enum CollectionPhaseBarrierKind
	{
		Unknown = 0,
		NativeStateVisibility = 1
	}

	/// <summary>
	/// Immutable C6.3 dependency/phase planning issue.
	/// </summary>
	public sealed class CollectionDependencyPhaseIssue
	{
		internal CollectionDependencyPhaseIssue(CollectionDependencyPhaseIssueKind kind, string reason,
			CollectionMemberKey memberKey, CollectionMemberKey relatedMemberKey, double? phase)
		{
			if (!Enum.IsDefined(typeof(CollectionDependencyPhaseIssueKind), kind) || kind == CollectionDependencyPhaseIssueKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			Kind = kind;
			Reason = CollectionDomainValidation.RequireDisplayValue(reason, nameof(reason));
			MemberKey = memberKey;
			RelatedMemberKey = relatedMemberKey;
			Phase = phase;
		}

		public CollectionDependencyPhaseIssueKind Kind { get; }
		public string Reason { get; }
		public CollectionMemberKey MemberKey { get; }
		public CollectionMemberKey RelatedMemberKey { get; }
		public double? Phase { get; }
	}

	/// <summary>
	/// One selected member placed into deterministic C6.3 dependency/phase order.
	/// </summary>
	public sealed class CollectionPlannedPhaseMember
	{
		internal CollectionPlannedPhaseMember(CollectionMemberMatchResult match)
		{
			Match = match ?? throw new ArgumentNullException(nameof(match));
		}

		public CollectionMemberMatchResult Match { get; }
		public ResolvedCollectionMemberPlan Member { get { return Match.Member; } }
		public CollectionMemberKey MemberKey { get { return Member.MemberKey; } }
		public double InstallationPhase { get { return Member.InstallationPhase; } }
		public CollectionMemberMatchDisposition MatchDisposition { get { return Match.Disposition; } }
	}

	/// <summary>
	/// One non-empty sparse installation phase in deterministic member order.
	/// </summary>
	public sealed class CollectionExecutionPhase
	{
		private readonly ReadOnlyCollection<CollectionPlannedPhaseMember> _members;

		internal CollectionExecutionPhase(double phaseNumber, IEnumerable<CollectionPlannedPhaseMember> members)
		{
			if (members == null)
				throw new ArgumentNullException(nameof(members));
			List<CollectionPlannedPhaseMember> copied = members.ToList();
			if (copied.Count == 0 || copied.Any(x => x == null || x.InstallationPhase != phaseNumber))
				throw new ArgumentException("An execution phase must contain only non-null members from that exact phase.", nameof(members));
			PhaseNumber = phaseNumber;
			_members = new ReadOnlyCollection<CollectionPlannedPhaseMember>(copied);
		}

		public double PhaseNumber { get; }
		public ReadOnlyCollection<CollectionPlannedPhaseMember> Members { get { return _members; } }
	}

	/// <summary>
	/// Immutable phase boundary requiring the earlier phase's relevant native state to be visible before the later phase begins.
	/// </summary>
	/// <remarks>
	/// This is not a command to deploy after every member. C6.5-C6.8 later decide whether the completed work requires any
	/// native publication/verification action to satisfy this visibility boundary.
	/// </remarks>
	public sealed class CollectionPhaseBarrier
	{
		internal CollectionPhaseBarrier(double completedPhase, double nextPhase, CollectionPhaseBarrierKind kind)
		{
			if (nextPhase <= completedPhase)
				throw new ArgumentOutOfRangeException(nameof(nextPhase));
			if (!Enum.IsDefined(typeof(CollectionPhaseBarrierKind), kind) || kind == CollectionPhaseBarrierKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			CompletedPhase = completedPhase;
			NextPhase = nextPhase;
			Kind = kind;
		}

		public double CompletedPhase { get; }
		public double NextPhase { get; }
		public CollectionPhaseBarrierKind Kind { get; }
	}

	/// <summary>
	/// Immutable C6.3 output for one exact C6.2 member-match set.
	/// </summary>
	/// <remarks>
	/// A ready plan establishes deterministic prerequisite/phase ordering only. It does not select conflict winners,
	/// submit native work, create checkpoints or authorize mutation.
	/// </remarks>
	public sealed class CollectionDependencyPhasePlan
	{
		private readonly ReadOnlyCollection<CollectionExecutionPhase> _phases;
		private readonly ReadOnlyCollection<CollectionPhaseBarrier> _barriers;
		private readonly ReadOnlyCollection<CollectionDependencyPhaseIssue> _issues;

		internal CollectionDependencyPhasePlan(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches,
			IEnumerable<CollectionExecutionPhase> phases, IEnumerable<CollectionPhaseBarrier> barriers,
			IEnumerable<CollectionDependencyPhaseIssue> issues)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (matches == null)
				throw new ArgumentNullException(nameof(matches));
			PlanIdentity = plan.Identity;
			Target = plan.Target;
			StateFingerprint = matches.StateFingerprint;
			_phases = Copy(phases, nameof(phases));
			_barriers = Copy(barriers, nameof(barriers));
			_issues = Copy(issues, nameof(issues));
		}

		public CollectionPlanIdentity PlanIdentity { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionCurrentStateFingerprint StateFingerprint { get; }
		public ReadOnlyCollection<CollectionExecutionPhase> Phases { get { return _phases; } }
		public ReadOnlyCollection<CollectionPhaseBarrier> Barriers { get { return _barriers; } }
		public ReadOnlyCollection<CollectionDependencyPhaseIssue> Issues { get { return _issues; } }
		public bool IsBlocked { get { return _issues.Count > 0; } }
		public bool IsReady { get { return !IsBlocked; } }

		private static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string parameterName) where T : class
		{
			if (values == null)
				throw new ArgumentNullException(parameterName);
			List<T> copied = values.ToList();
			if (copied.Any(x => x == null))
				throw new ArgumentException("A dependency/phase plan cannot contain null records.", parameterName);
			return new ReadOnlyCollection<T>(copied);
		}
	}
}
