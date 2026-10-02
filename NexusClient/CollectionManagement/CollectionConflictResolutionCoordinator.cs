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
			if (plan == null) throw new ArgumentNullException(nameof(plan));
			if (impactPlan == null) throw new ArgumentNullException(nameof(impactPlan));
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (kind != CollectionConflictResolutionDecisionKind.IncomingCollectionWinsFile &&
				kind != CollectionConflictResolutionDecisionKind.KeepExistingManagedFileWinner)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (plan.Policy.Kind != CollectionExecutionPolicyKind.InstallIntoCurrentSetup)
				throw new InvalidOperationException("C9 file-conflict decisions are scoped to Install into current setup.");
			if (!impactPlan.PlanIdentity.Equals(plan.Identity) || !impactPlan.Target.Equals(plan.Target) || !impactPlan.StateFingerprint.Equals(plan.CurrentStateFingerprint))
				throw new InvalidOperationException("The conflict decision must be recorded against the exact reviewed plan and native-state observation.");

			CollectionFileImpact impact = impactPlan.FileImpacts.SingleOrDefault(x => x.Target.Equals(target));
			if (impact == null || impact.PlannedWinner == null || String.IsNullOrWhiteSpace(impact.CurrentOwnerKey))
				throw new InvalidOperationException("The selected file does not expose an existing-owner conflict that can be resolved by C9.");
			bool requiresDecision = impactPlan.Issues.Any(x => x.Kind == CollectionConflictImpactIssueKind.ExistingFileWinnerDecisionRequired &&
				Equals(x.MemberKey, impact.PlannedWinner) && StringComparer.Ordinal.Equals(x.SubjectKey, target.ToString()));
			if (!requiresDecision)
				throw new InvalidOperationException("The selected file no longer requires an explicit existing-owner winner decision.");
			CollectionConflictResolutionDecision existing = _store.GetForRevisionTarget(plan.Revision, plan.Target).FirstOrDefault(x =>
				x.Kind == kind && x.MemberKey.Equals(impact.PlannedWinner) && x.DeploymentRoot == target.Root &&
				StringComparer.OrdinalIgnoreCase.Equals(x.RelativePath, target.RelativePath) &&
				StringComparer.Ordinal.Equals(x.ExistingOwnerKey, impact.CurrentOwnerKey));
			if (existing != null) return existing;

			var decision = new CollectionConflictResolutionDecision(Guid.NewGuid(), plan.Revision, plan.Target, impact.PlannedWinner,
				target.Root, target.RelativePath, impact.CurrentOwnerKey, kind, note);
			_store.Save(decision);
			return decision;
		}

		public void ClearDecision(Guid decisionId)
		{
			_store.Delete(decisionId);
		}
	}
}
