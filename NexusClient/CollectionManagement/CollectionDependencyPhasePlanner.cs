using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Builds deterministic C6.3 prerequisite ordering and sparse phase barriers from one resolved plan and C6.2 match set.
	/// </summary>
	/// <remarks>
	/// This planner is read-only. It does not acquire/install content, choose file/plugin conflict winners, mutate native state,
	/// persist coordinator state or translate uncharacterized raw manager rule systems into dependency semantics.
	/// </remarks>
	public sealed class CollectionDependencyPhasePlanner
	{
		/// <summary>
		/// Plans the complete selected closure. Semantic dependency failures are returned as blocked issues; mismatched inputs throw.
		/// </summary>
		public CollectionDependencyPhasePlan Plan(ResolvedCollectionPlan plan, CollectionMemberMatchSet matches)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (matches == null)
				throw new ArgumentNullException(nameof(matches));
			if (!plan.Identity.Equals(matches.PlanIdentity))
				throw new ArgumentException("The member-match set belongs to a different resolved plan.", nameof(matches));
			if (!plan.Target.Equals(matches.Target))
				throw new ArgumentException("The member-match set belongs to a different Collection target.", nameof(matches));
			if (!plan.CurrentStateFingerprint.Equals(matches.StateFingerprint))
				throw new ArgumentException("The member-match set was produced from a different native-state fingerprint.", nameof(matches));

			var issues = new List<CollectionDependencyPhaseIssue>();
			foreach (CollectionMemberMatchResult match in matches.Members)
			{
				if (match.IsBlocked)
				{
					issues.Add(new CollectionDependencyPhaseIssue(CollectionDependencyPhaseIssueKind.BlockedMemberMatch,
						"C6.2 blocked this member, so dependency/phase planning cannot produce an actionable order.",
						match.Member.MemberKey, null, match.Member.InstallationPhase));
				}
			}

			Dictionary<CollectionMemberKey, ResolvedCollectionMemberPlan> selected = plan.SelectedMembers
				.ToDictionary(x => x.MemberKey);
			Dictionary<double, List<CollectionMemberKey>> membersByPhase = new Dictionary<double, List<CollectionMemberKey>>();
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				List<CollectionMemberKey> phaseMembers;
				if (!membersByPhase.TryGetValue(member.InstallationPhase, out phaseMembers))
				{
					phaseMembers = new List<CollectionMemberKey>();
					membersByPhase.Add(member.InstallationPhase, phaseMembers);
				}
				phaseMembers.Add(member.MemberKey);
			}

			Dictionary<CollectionMemberKey, List<CollectionMemberKey>> samePhaseDependents =
				new Dictionary<CollectionMemberKey, List<CollectionMemberKey>>();
			Dictionary<CollectionMemberKey, int> samePhaseInDegree = new Dictionary<CollectionMemberKey, int>();
			foreach (ResolvedCollectionMemberPlan member in plan.SelectedMembers)
			{
				samePhaseDependents.Add(member.MemberKey, new List<CollectionMemberKey>());
				samePhaseInDegree.Add(member.MemberKey, 0);
			}

			foreach (CollectionMemberDependency dependency in plan.CapabilityReport.Manifest.Dependencies)
			{
				ResolvedCollectionMemberPlan dependent;
				if (!selected.TryGetValue(dependency.DependentMemberKey, out dependent))
					continue;

				ResolvedCollectionMemberPlan prerequisite;
				if (!selected.TryGetValue(dependency.PrerequisiteMemberKey, out prerequisite))
				{
					// A supported ResolvedCollectionPlan should already have rejected this selection through capability reporting.
					throw new InvalidOperationException("A resolved selected member is missing a characterized selected prerequisite.");
				}

				if (prerequisite.InstallationPhase > dependent.InstallationPhase)
				{
					issues.Add(new CollectionDependencyPhaseIssue(CollectionDependencyPhaseIssueKind.PhaseOrderingConflict,
						"A member prerequisite is assigned to a later installation phase than its dependent.",
						dependent.MemberKey, prerequisite.MemberKey, dependent.InstallationPhase));
					continue;
				}

				if (prerequisite.InstallationPhase == dependent.InstallationPhase)
				{
					samePhaseDependents[prerequisite.MemberKey].Add(dependent.MemberKey);
					samePhaseInDegree[dependent.MemberKey] = samePhaseInDegree[dependent.MemberKey] + 1;
				}
			}

			if (issues.Count > 0)
				return Blocked(plan, matches, issues);

			var phases = new List<CollectionExecutionPhase>();
			var phaseNumbers = membersByPhase.Keys.OrderBy(x => x).ToList();
			foreach (double phaseNumber in phaseNumbers)
			{
				List<CollectionMemberKey> orderedKeys;
				if (!TryOrderPhase(membersByPhase[phaseNumber], samePhaseDependents, samePhaseInDegree, out orderedKeys))
				{
					issues.Add(new CollectionDependencyPhaseIssue(CollectionDependencyPhaseIssueKind.DependencyCycle,
						"The selected members contain a prerequisite cycle within one installation phase.",
						null, null, phaseNumber));
					return Blocked(plan, matches, issues);
				}

				phases.Add(new CollectionExecutionPhase(phaseNumber,
					orderedKeys.Select(key => new CollectionPlannedPhaseMember(matches.MembersByKey[key]))));
			}

			var barriers = new List<CollectionPhaseBarrier>();
			for (int i = 0; i + 1 < phaseNumbers.Count; i++)
			{
				barriers.Add(new CollectionPhaseBarrier(phaseNumbers[i], phaseNumbers[i + 1],
					CollectionPhaseBarrierKind.NativeStateVisibility));
			}

			return new CollectionDependencyPhasePlan(plan, matches, phases, barriers, issues);
		}

		private static CollectionDependencyPhasePlan Blocked(ResolvedCollectionPlan plan,
			CollectionMemberMatchSet matches, IEnumerable<CollectionDependencyPhaseIssue> issues)
		{
			return new CollectionDependencyPhasePlan(plan, matches,
				new CollectionExecutionPhase[0], new CollectionPhaseBarrier[0],
				issues.OrderBy(x => x.Phase.HasValue ? 1 : 0)
					.ThenBy(x => x.Phase.GetValueOrDefault())
					.ThenBy(x => x.MemberKey, CollectionMemberKeyComparer.Instance)
					.ThenBy(x => x.RelatedMemberKey, CollectionMemberKeyComparer.Instance)
					.ThenBy(x => x.Kind));
		}

		private static bool TryOrderPhase(IEnumerable<CollectionMemberKey> phaseMembers,
			IDictionary<CollectionMemberKey, List<CollectionMemberKey>> dependents,
			IDictionary<CollectionMemberKey, int> inDegree, out List<CollectionMemberKey> ordered)
		{
			var keys = phaseMembers.ToList();
			var phaseKeySet = new HashSet<CollectionMemberKey>(keys);
			var remainingDegree = new Dictionary<CollectionMemberKey, int>();
			var ready = new SortedSet<CollectionMemberKey>(CollectionMemberKeyComparer.Instance);
			foreach (CollectionMemberKey key in keys)
			{
				int degree = inDegree[key];
				remainingDegree.Add(key, degree);
				if (degree == 0)
					ready.Add(key);
			}

			ordered = new List<CollectionMemberKey>(keys.Count);
			while (ready.Count > 0)
			{
				CollectionMemberKey current = ready.Min;
				ready.Remove(current);
				ordered.Add(current);

				foreach (CollectionMemberKey dependent in dependents[current])
				{
					if (!phaseKeySet.Contains(dependent))
						continue;
					int degree = remainingDegree[dependent] - 1;
					remainingDegree[dependent] = degree;
					if (degree == 0)
						ready.Add(dependent);
				}
			}

			return ordered.Count == keys.Count;
		}

		private sealed class CollectionMemberKeyComparer : IComparer<CollectionMemberKey>
		{
			public static readonly CollectionMemberKeyComparer Instance = new CollectionMemberKeyComparer();

			public int Compare(CollectionMemberKey left, CollectionMemberKey right)
			{
				if (ReferenceEquals(left, right))
					return 0;
				if (ReferenceEquals(left, null))
					return -1;
				if (ReferenceEquals(right, null))
					return 1;
				int kind = left.Kind.CompareTo(right.Kind);
				return kind != 0 ? kind : StringComparer.Ordinal.Compare(left.Value, right.Value);
			}
		}
	}
}
