using System;
using System.Collections.Generic;
using System.Linq;
using Nexus.Client.CollectionManagement.Persistence;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Records and loads durable C9 conflict-resolution decisions.</summary>
	/// <remarks>
	/// File-winner decisions are bound to one exact reviewed conflict. C9.4 supports either allowing the incoming Collection
	/// winner or preserving the current unrelated managed owner; on multi-writer paths the reviewed Collection winner is kept
	/// as the immediate managed fallback. Native ownership remains authoritative in both cases.
	/// </remarks>
	public sealed class CollectionConflictResolutionCoordinator
	{
		private readonly CollectionsConflictResolutionStore _store;
		public CollectionConflictResolutionCoordinator(CollectionsConflictResolutionStore store)
		{
			_store = store ?? throw new ArgumentNullException(nameof(store));
		}

		public IReadOnlyList<CollectionConflictResolutionDecision> GetDecisions(ResolvedCollectionPlan plan)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			return _store.GetForRevisionTarget(plan.Revision, plan.Target);
		}

		public CollectionConflictResolutionDecision AuthorizeIncomingFileWinner(ResolvedCollectionPlan plan,
			CollectionConflictImpactPlan impactPlan, ModDeploymentTarget target, string note)
		{
			return SaveFileWinnerDecision(plan, impactPlan, target,
				CollectionConflictResolutionDecisionKind.IncomingCollectionWinsFile, note);
		}

		public CollectionConflictResolutionDecision KeepExistingManagedFileWinner(ResolvedCollectionPlan plan,
			CollectionConflictImpactPlan impactPlan, ModDeploymentTarget target, string note)
		{
			return SaveFileWinnerDecision(plan, impactPlan, target,
				CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner, note);
		}

		private CollectionConflictResolutionDecision SaveFileWinnerDecision(ResolvedCollectionPlan plan,
			CollectionConflictImpactPlan impactPlan, ModDeploymentTarget target, CollectionConflictResolutionDecisionKind kind, string note)
		{
			return SaveFileWinnerDecisions(plan, impactPlan,
				new[] { new CollectionFileWinnerDecisionRequest(target, kind) }, note).Single();
		}

		/// <summary>
		/// Validates every exact conflict against one immutable impact observation before saving any
		/// choices. The store writes the new per-file rows in a single atomic transaction.
		/// </summary>
		public IReadOnlyList<CollectionConflictResolutionDecision> SaveFileWinnerDecisions(ResolvedCollectionPlan plan,
			CollectionConflictImpactPlan impactPlan, IEnumerable<CollectionFileWinnerDecisionRequest> requests, string note)
		{
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (requests == null) throw new ArgumentNullException(nameof(requests));
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new InvalidOperationException("C9 file-conflict decisions are scoped to Install into current setup.");
			if (!impactPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.Target.Equals(plan.Target) ||
				!impactPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new InvalidOperationException("The conflict decisions must match the exact reviewed plan and native-state observation.");

			var batch = requests.ToList();
			if (batch.Count == 0) return new CollectionConflictResolutionDecision[0];
			var impacts = impactPlan.FileImpacts.ToDictionary(x => x.Target);
			var decisionIssues = impactPlan.Issues
				.Where(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired)
				.ToLookup(x => x.SubjectKey, StringComparer.Ordinal);
			var existingByPath = _store.GetForRevisionTarget(plan.Revision, plan.Target)
				.ToLookup(x => x.DeploymentRoot.ToString() + ":" + x.RelativePath, StringComparer.OrdinalIgnoreCase);
			var seen = new HashSet<ModDeploymentTarget>();
			var result = new List<CollectionConflictResolutionDecision>(batch.Count);
			var newDecisions = new List<CollectionConflictResolutionDecision>();

			foreach (CollectionFileWinnerDecisionRequest request in batch)
			{
				if (request == null) throw new ArgumentException("A bulk decision cannot contain a null request.", nameof(requests));
				if (!seen.Add(request.Target))
					throw new InvalidOperationException("The same file has more than one decision in this review.");
				CollectionFileImpact impact;
				if (!impacts.TryGetValue(request.Target, out impact) || impact.PlannedWinner == null ||
					String.IsNullOrWhiteSpace(impact.CurrentOwnerKey))
					throw new InvalidOperationException("The selected file has no exact existing-owner conflict to resolve: " + request.Target);
				if (!decisionIssues[request.Target.ToString()].Any(x => Equals(x.MemberKey, impact.PlannedWinner)))
					throw new InvalidOperationException("The selected file no longer requires an explicit conflict decision: " + request.Target);
				CollectionConflictResolutionDecision existing = existingByPath[request.Target.ToString()].FirstOrDefault(x =>
					x.Kind == request.Kind && x.MemberKey.Equals(impact.PlannedWinner) &&
					StringComparer.Ordinal.Equals(x.ExistingOwnerKey, impact.CurrentOwnerKey));
				if (existing != null)
				{
					result.Add(existing);
					continue;
				}
				var decision = new CollectionConflictResolutionDecision(Guid.NewGuid(), plan.Revision, plan.Target,
					impact.PlannedWinner, request.Target.Root, request.Target.RelativePath, impact.CurrentOwnerKey, request.Kind, note);
				newDecisions.Add(decision);
				result.Add(decision);
			}

			_store.SaveMany(newDecisions);
			return result;
		}

		public void ClearDecision(Guid decisionId)
		{
			_store.Delete(decisionId);
		}
	}
}
