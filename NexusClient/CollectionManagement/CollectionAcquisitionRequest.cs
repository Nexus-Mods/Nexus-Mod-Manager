using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable request to acquire the exact archive selected for one member of a resolved Collection plan.
	/// </summary>
	/// <remarks>
	/// This request is acquisition intent only. It does not contain signed download URLs, does not assert that local bytes
	/// are verified, and does not authorize installation. Later C4 acquisition stages resolve or verify the requested artifact.
	/// </remarks>
	public sealed class CollectionAcquisitionRequest
	{
		private CollectionAcquisitionRequest(
			Guid requestId,
			CollectionPlanIdentity planIdentity,
			CollectionRevisionIdentity revision,
			CollectionTargetIdentity target,
			CollectionMemberKey memberKey,
			CollectionMemberRequirement requirement,
			CollectionArtifactReference selectedArtifact,
			CollectionRecipeIdentity recipeIdentity)
		{
			if (requestId == Guid.Empty)
				throw new ArgumentException("A non-empty acquisition request identifier is required.", nameof(requestId));
			if (planIdentity == null)
				throw new ArgumentNullException(nameof(planIdentity));
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));
			if (!Enum.IsDefined(typeof(CollectionMemberRequirement), requirement) || requirement == CollectionMemberRequirement.Unknown)
				throw new ArgumentOutOfRangeException(nameof(requirement));
			if (selectedArtifact == null)
				throw new ArgumentNullException(nameof(selectedArtifact));
			if (recipeIdentity == null)
				throw new ArgumentNullException(nameof(recipeIdentity));

			RequestId = requestId;
			PlanIdentity = planIdentity;
			Revision = revision;
			Target = target;
			MemberKey = memberKey;
			Requirement = requirement;
			SelectedArtifact = selectedArtifact;
			RecipeIdentity = recipeIdentity;
		}

		/// <summary>
		/// Creates an acquisition request for one exact selected member in the supplied immutable plan.
		/// </summary>
		public static CollectionAcquisitionRequest Create(Guid requestId, ResolvedCollectionPlan plan, CollectionMemberKey memberKey)
		{
			if (plan == null)
				throw new ArgumentNullException(nameof(plan));
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));

			ResolvedCollectionMemberPlan member = null;
			foreach (ResolvedCollectionMemberPlan candidate in plan.SelectedMembers)
			{
				if (candidate.MemberKey.Equals(memberKey))
				{
					member = candidate;
					break;
				}
			}

			if (member == null)
				throw new ArgumentException("The requested acquisition member does not belong to the resolved plan's selected closure.", nameof(memberKey));

			return new CollectionAcquisitionRequest(
				requestId,
				plan.Identity,
				plan.Revision,
				plan.Target,
				member.MemberKey,
				member.Requirement,
				member.ArtifactChoice.SelectedArtifact,
				member.RecipeIdentity);
		}

		/// <summary>Gets the stable acquisition request identifier.</summary>
		public Guid RequestId { get; }

		/// <summary>Gets the immutable resolved-plan snapshot which requested the artifact.</summary>
		public CollectionPlanIdentity PlanIdentity { get; }

		/// <summary>Gets the concrete Collection revision represented by the plan.</summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>Gets the target fingerprint against which the plan was resolved.</summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>Gets the exact selected Collection member requesting content.</summary>
		public CollectionMemberKey MemberKey { get; }

		/// <summary>Gets whether the selected member is required or optional in the revision.</summary>
		public CollectionMemberRequirement Requirement { get; }

		/// <summary>Gets the exact concrete artifact selected by the resolved plan.</summary>
		public CollectionArtifactReference SelectedArtifact { get; }

		/// <summary>Gets the exact installation-recipe identity associated with the requested artifact.</summary>
		public CollectionRecipeIdentity RecipeIdentity { get; }
	}
}
