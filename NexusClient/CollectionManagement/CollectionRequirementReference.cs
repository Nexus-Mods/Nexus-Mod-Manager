using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies the kind of collection requirement whose desired/observed state is being tracked.
	/// </summary>
	public enum CollectionRequirementAspect
	{
		Unknown = 0,

		/// <summary>
		/// Whether a collection member participates in the applied recipe (including an explicit ignore/omission decision).
		/// </summary>
		MemberParticipation = 1,

		/// <summary>
		/// Whether a participating member is enabled/disabled where that distinction is meaningful.
		/// </summary>
		MemberEnabledState = 2,

		/// <summary>
		/// The exact artifact/file selected for a member.
		/// </summary>
		ArtifactSelection = 3,

		/// <summary>
		/// The exact installer choices/recipe satisfied by a member.
		/// </summary>
		InstallerRecipe = 4,

		/// <summary>
		/// The desired native winner for one owned path/effect.
		/// </summary>
		FileWinner = 5,

		/// <summary>
		/// A plugin/master enablement or ordering requirement.
		/// </summary>
		PluginState = 6,

		/// <summary>
		/// A supported INI/game-value/configuration requirement.
		/// </summary>
		ConfigurationState = 7,

		/// <summary>
		/// Additional managed content intentionally present outside the collection member closure.
		/// </summary>
		AdditionalManagedContent = 8
	}

	/// <summary>
	/// Stable reference to one requirement of one applied collection baseline.
	/// </summary>
	/// <remarks>
	/// The reference is pinned to the exact baseline revision and target so overrides/drift survive later revision
	/// comparisons without being accidentally reinterpreted against a newer curator recipe. Subject keys are opaque
	/// normalized identifiers (for example a target-relative owned path, plugin identity or configuration key), not UI text.
	/// </remarks>
	public sealed class CollectionRequirementReference : IEquatable<CollectionRequirementReference>
	{
		/// <summary>
		/// Creates a requirement reference bound to an existing collection/target association baseline.
		/// </summary>
		public CollectionRequirementReference(CollectionTargetAssociation association, CollectionMemberKey memberKey,
			CollectionRequirementAspect aspect, string subjectKey)
		{
			if (association == null)
				throw new ArgumentNullException(nameof(association));
			if (!Enum.IsDefined(typeof(CollectionRequirementAspect), aspect) || aspect == CollectionRequirementAspect.Unknown)
				throw new ArgumentOutOfRangeException(nameof(aspect));

			bool memberScoped = aspect == CollectionRequirementAspect.MemberParticipation ||
				aspect == CollectionRequirementAspect.MemberEnabledState ||
				aspect == CollectionRequirementAspect.ArtifactSelection ||
				aspect == CollectionRequirementAspect.InstallerRecipe;
			bool subjectScoped = aspect == CollectionRequirementAspect.FileWinner ||
				aspect == CollectionRequirementAspect.PluginState ||
				aspect == CollectionRequirementAspect.ConfigurationState ||
				aspect == CollectionRequirementAspect.AdditionalManagedContent;

			if (memberScoped && memberKey == null)
				throw new ArgumentNullException(nameof(memberKey), "This requirement aspect must identify a collection member.");
			if (memberScoped && subjectKey != null)
				throw new ArgumentException("Member-level requirement aspects do not use a separate subject key.", nameof(subjectKey));
			if (subjectScoped)
				subjectKey = CollectionIdentityValidation.RequireOpaqueToken(subjectKey, nameof(subjectKey));
			if (aspect == CollectionRequirementAspect.AdditionalManagedContent && memberKey != null)
				throw new ArgumentException("Additional managed content is outside the collection member closure and cannot use a collection member key.", nameof(memberKey));

			AssociationId = association.AssociationId;
			BaselineRevision = association.Revision;
			Target = association.Target;
			MemberKey = memberKey;
			Aspect = aspect;
			SubjectKey = subjectKey;
		}

		/// <summary>
		/// Gets the durable association this requirement belongs to.
		/// </summary>
		public Guid AssociationId { get; }

		/// <summary>
		/// Gets the exact old/current curator baseline against which the requirement is interpreted.
		/// </summary>
		public CollectionRevisionIdentity BaselineRevision { get; }

		/// <summary>
		/// Gets the real target on which the requirement was observed/applied.
		/// </summary>
		public CollectionTargetIdentity Target { get; }

		/// <summary>
		/// Gets the member key when this requirement is member-owned, otherwise <c>null</c>.
		/// </summary>
		public CollectionMemberKey MemberKey { get; }

		/// <summary>
		/// Gets the semantic requirement area.
		/// </summary>
		public CollectionRequirementAspect Aspect { get; }

		/// <summary>
		/// Gets an opaque normalized sub-resource identity for path/plugin/config/additional-content requirements.
		/// </summary>
		public string SubjectKey { get; }

		/// <inheritdoc />
		public bool Equals(CollectionRequirementReference other)
		{
			return !ReferenceEquals(other, null) &&
				AssociationId == other.AssociationId &&
				Equals(BaselineRevision, other.BaselineRevision) &&
				Equals(Target, other.Target) &&
				Equals(MemberKey, other.MemberKey) &&
				Aspect == other.Aspect &&
				StringComparer.Ordinal.Equals(SubjectKey, other.SubjectKey);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionRequirementReference);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = AssociationId.GetHashCode();
				hashCode = (hashCode * 397) ^ (BaselineRevision == null ? 0 : BaselineRevision.GetHashCode());
				hashCode = (hashCode * 397) ^ (Target == null ? 0 : Target.GetHashCode());
				hashCode = (hashCode * 397) ^ (MemberKey == null ? 0 : MemberKey.GetHashCode());
				hashCode = (hashCode * 397) ^ (int)Aspect;
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(SubjectKey ?? string.Empty);
				return hashCode;
			}
		}
	}
}
