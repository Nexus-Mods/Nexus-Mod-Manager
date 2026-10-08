using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>Describes how one reviewed C9 override can survive a C10 revision transition.</summary>
	public enum CollectionRevisionUpdateOverridePreservationDisposition
	{
		PreserveExistingNativeState = 1,
		PreserveAdditionalManagedContent = 2,
		ReapplyAfterCandidateExecution = 3,
		SatisfiedByCandidateRemoval = 4,
		ActionRequired = 5
	}

	/// <summary>One exact reviewed override and its qualified carry-forward behavior.</summary>
	public sealed class CollectionRevisionUpdateOverridePreservationAction
	{
		internal CollectionRevisionUpdateOverridePreservationAction(UserOverride userOverride,
			CollectionRevisionUpdateOverridePreservationDisposition disposition, string reason)
		{
			UserOverride = userOverride ?? throw new ArgumentNullException(nameof(userOverride));
			if (!Enum.IsDefined(typeof(CollectionRevisionUpdateOverridePreservationDisposition), disposition))
				throw new ArgumentOutOfRangeException(nameof(disposition));
			Disposition = disposition;
			Reason = reason ?? String.Empty;
		}

		public UserOverride UserOverride { get; }
		public CollectionRevisionUpdateOverridePreservationDisposition Disposition { get; }
		public string Reason { get; }
		public bool IsQualified { get { return Disposition != CollectionRevisionUpdateOverridePreservationDisposition.ActionRequired; } }
		public bool SuppressesCandidateMemberMutation
		{
			get { return Disposition == CollectionRevisionUpdateOverridePreservationDisposition.PreserveExistingNativeState; }
		}
		public bool RequiresPostCandidateReapply
		{
			get { return Disposition == CollectionRevisionUpdateOverridePreservationDisposition.ReapplyAfterCandidateExecution; }
		}
	}

	/// <summary>Pure C10.5 qualification of the exact C9 overrides frozen by the approved update review.</summary>
	public sealed class CollectionRevisionUpdateOverridePreservationPlan
	{
		private readonly ReadOnlyCollection<CollectionRevisionUpdateOverridePreservationAction> _actions;

		internal CollectionRevisionUpdateOverridePreservationPlan(CollectionRevisionUpdateReviewedIntent reviewedIntent,
			CollectionRevisionUpdatePlan updatePlan, IEnumerable<CollectionRevisionUpdateOverridePreservationAction> actions)
		{
			ReviewedIntent = reviewedIntent ?? throw new ArgumentNullException(nameof(reviewedIntent));
			UpdatePlan = updatePlan ?? throw new ArgumentNullException(nameof(updatePlan));
			List<CollectionRevisionUpdateOverridePreservationAction> copied = (actions ?? throw new ArgumentNullException(nameof(actions))).ToList();
			if (copied.Any(x => x == null) || copied.Select(x => x.UserOverride.OverrideId).Distinct().Count() != copied.Count)
				throw new ArgumentException("A C10.5 override-preservation plan cannot contain null or duplicate override actions.", nameof(actions));
			_actions = new ReadOnlyCollection<CollectionRevisionUpdateOverridePreservationAction>(copied.OrderBy(x => x.UserOverride.OverrideId).ToList());
		}

		public CollectionRevisionUpdateReviewedIntent ReviewedIntent { get; }
		public CollectionRevisionUpdatePlan UpdatePlan { get; }
		public ReadOnlyCollection<CollectionRevisionUpdateOverridePreservationAction> Actions { get { return _actions; } }
		public bool IsQualified { get { return _actions.All(x => x.IsQualified); } }
		public bool RequiresPostCandidateReapply { get { return _actions.Any(x => x.RequiresPostCandidateReapply); } }
		public ReadOnlyCollection<CollectionMemberKey> MembersWhoseCandidateMutationIsSuppressed
		{
			get
			{
				return new ReadOnlyCollection<CollectionMemberKey>(_actions.Where(x => x.SuppressesCandidateMemberMutation && x.UserOverride.Requirement.MemberKey != null)
					.Select(x => UpdatePlan.MemberCorrelations.ResolveCandidateMemberKey(x.UserOverride.Requirement.MemberKey))
					.Distinct().OrderBy(x => x.Kind).ThenBy(x => x.Value, StringComparer.Ordinal).ToList());
			}
		}
	}

	/// <summary>
	/// C10.5 fail-closed override carry-forward planner. It never mutates native or Collections state. The planner proves only
	/// preservation semantics already represented by C9/C10; unsupported rebasing is kept ActionRequired before further mutation.
	/// </summary>
	public sealed class CollectionRevisionUpdateOverridePreservationPlanner
	{
		public CollectionRevisionUpdateOverridePreservationPlan Plan(CollectionRevisionUpdateReviewedIntent reviewedIntent,
			CollectionRevisionUpdatePlan updatePlan)
		{
			if (reviewedIntent == null) throw new ArgumentNullException(nameof(reviewedIntent));
			if (updatePlan == null) throw new ArgumentNullException(nameof(updatePlan));
			reviewedIntent.ValidateCurrentPlan(updatePlan);

			Dictionary<Guid, UserOverride> overrides = updatePlan.Members.SelectMany(x => x.Overrides)
				.Concat(updatePlan.UnscopedOverrides).ToDictionary(x => x.OverrideId);
			HashSet<Guid> reviewedIds = new HashSet<Guid>(reviewedIntent.PreservedOverrideIds);
			if (!reviewedIds.SetEquals(overrides.Keys))
				throw new InvalidOperationException("The active C9 override set no longer matches the exact overrides frozen by the approved revision-update review.");

			var actions = new List<CollectionRevisionUpdateOverridePreservationAction>();
			foreach (Guid overrideId in reviewedIntent.PreservedOverrideIds.OrderBy(x => x))
				actions.Add(Qualify(overrides[overrideId], updatePlan));
			return new CollectionRevisionUpdateOverridePreservationPlan(reviewedIntent, updatePlan, actions);
		}

		public CollectionRevisionUpdateOverridePreservationPlan RequireQualified(CollectionRevisionUpdateReviewedIntent reviewedIntent,
			CollectionRevisionUpdatePlan updatePlan)
		{
			CollectionRevisionUpdateOverridePreservationPlan plan = Plan(reviewedIntent, updatePlan);
			CollectionRevisionUpdateOverridePreservationAction blocker = plan.Actions.FirstOrDefault(x => !x.IsQualified);
			if (blocker != null)
				throw new InvalidOperationException("The approved C9 override cannot yet be preserved safely across this candidate revision: " + blocker.Reason);
			return plan;
		}

		private static CollectionRevisionUpdateOverridePreservationAction Qualify(UserOverride userOverride,
			CollectionRevisionUpdatePlan updatePlan)
		{
			CollectionRequirementReference requirement = userOverride.Requirement;
			CollectionMemberKey candidateMemberKey = requirement.MemberKey == null ? null :
				updatePlan.MemberCorrelations.ResolveCandidateMemberKey(requirement.MemberKey);
			CollectionRevisionUpdateMemberPlan member = candidateMemberKey == null ? null :
				updatePlan.Members.SingleOrDefault(x => x.MemberKey.Equals(candidateMemberKey));

			switch (requirement.Aspect)
			{
				case CollectionRequirementAspect.AdditionalManagedContent:
					return Action(userOverride, CollectionRevisionUpdateOverridePreservationDisposition.PreserveAdditionalManagedContent,
						"Additional managed content remains outside the candidate Collection member closure and must be left untouched.");

				case CollectionRequirementAspect.MemberParticipation:
					if (member == null)
						return Block(userOverride, "The member-scoped participation override no longer resolves to the reviewed old/new member set.");
					if (member.NewMember == null)
					{
						if (userOverride.UserChosenState.Kind == CollectionRequirementStateKind.Absent)
							return Action(userOverride, CollectionRevisionUpdateOverridePreservationDisposition.SatisfiedByCandidateRemoval,
								"The user's explicit omission is naturally satisfied because the candidate revision removes the member.");
						return Block(userOverride, "The candidate removes a member that the explicit participation override keeps present.");
					}
					if (userOverride.UserChosenState.Kind == CollectionRequirementStateKind.Absent)
						return Action(userOverride, CollectionRevisionUpdateOverridePreservationDisposition.PreserveExistingNativeState,
							"The candidate still contains a member the user deliberately omits; candidate native mutation for this member must remain suppressed.");
					return Block(userOverride, "The participation override uses a present-state value that cannot be safely rebased without a new explicit decision.");

				case CollectionRequirementAspect.MemberEnabledState:
					if (member == null || member.NewMember == null)
						return Block(userOverride, "The enabled-state override targets a member not present in the candidate revision.");
					bool enabled;
					if (!CollectionMemberRequirementStates.TryGetEnabled(userOverride.UserChosenState, out enabled))
						return Block(userOverride, "The enabled-state override is not encoded with the canonical bool-v1 adapter and cannot be replayed safely.");
					return Action(userOverride, CollectionRevisionUpdateOverridePreservationDisposition.ReapplyAfterCandidateExecution,
						"The exact " + (enabled ? "enabled" : "disabled") + " decision must be restored after candidate native execution and verified against the current target.");

				case CollectionRequirementAspect.ArtifactSelection:
				case CollectionRequirementAspect.InstallerRecipe:
					if (member == null || member.NewMember == null)
						return Block(userOverride, "The artifact/recipe override targets a member removed by the candidate revision.");
					return Block(userOverride, "The artifact/recipe override chosen state is opaque and C10 has no typed candidate-baseline rebase adapter; publishing it against the new revision would silently reinterpret the old decision.");

				case CollectionRequirementAspect.FileWinner:
				case CollectionRequirementAspect.PluginState:
				case CollectionRequirementAspect.ConfigurationState:
					if (!HasConcreteCandidateEffect(requirement, updatePlan))
						return Block(userOverride, "The subject-scoped override cannot be mapped to an exact candidate effect, so its preservation cannot be proven.");
					return Block(userOverride, "The override's chosen file/plugin/configuration state is intentionally opaque; C10 cannot replay that fingerprint as a native mutation without a typed state adapter.");

				default:
					return Block(userOverride, "The override aspect has no characterized C10 carry-forward semantics.");
			}
		}

		private static bool HasConcreteCandidateEffect(CollectionRequirementReference requirement,
			CollectionRevisionUpdatePlan updatePlan)
		{
			CollectionRevisionUpdateEffectKind? kind = null;
			switch (requirement.Aspect)
			{
				case CollectionRequirementAspect.FileWinner: kind = CollectionRevisionUpdateEffectKind.File; break;
				case CollectionRequirementAspect.PluginState: kind = CollectionRevisionUpdateEffectKind.Plugin; break;
				case CollectionRequirementAspect.ConfigurationState: break;
			}
			IEnumerable<CollectionRevisionUpdateEffectPlan> candidates = updatePlan.Effects.Where(x =>
				x.ChangeKind != CollectionRevisionUpdateEffectChangeKind.Removed &&
				StringComparer.Ordinal.Equals(x.ResourceKey, requirement.SubjectKey));
			if (kind.HasValue) candidates = candidates.Where(x => x.Kind == kind.Value);
			else candidates = candidates.Where(x => x.Kind == CollectionRevisionUpdateEffectKind.Ini || x.Kind == CollectionRevisionUpdateEffectKind.GameValue);
			if (requirement.MemberKey != null)
			{
				CollectionMemberKey candidateMemberKey = updatePlan.MemberCorrelations.ResolveCandidateMemberKey(requirement.MemberKey);
				candidates = candidates.Where(x => x.MemberKey.Equals(candidateMemberKey));
			}
			return candidates.Any();
		}

		private static CollectionRevisionUpdateOverridePreservationAction Action(UserOverride value,
			CollectionRevisionUpdateOverridePreservationDisposition disposition, string reason)
		{
			return new CollectionRevisionUpdateOverridePreservationAction(value, disposition, reason);
		}

		private static CollectionRevisionUpdateOverridePreservationAction Block(UserOverride value, string reason)
		{
			return Action(value, CollectionRevisionUpdateOverridePreservationDisposition.ActionRequired, reason);
		}
	}
}
