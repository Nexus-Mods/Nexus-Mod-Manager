using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable concrete input decision for one member in the selected collection closure.
	/// </summary>
	/// <remarks>
	/// This is not a native installer operation. C5 translates the exact artifact/recipe decision into validated native input,
	/// while C6 later supplies dependency/phase ordering. Source ordinal remains diagnostic only and never determines execution order.
	/// </remarks>
	public sealed class ResolvedCollectionMemberPlan
	{
		/// <summary>
		/// Creates a resolved decision from one selected, fully identified normalized member.
		/// </summary>
		public ResolvedCollectionMemberPlan(
			NormalizedCollectionMember member,
			CollectionResolvedArtifactChoice artifactChoice)
		{
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (artifactChoice == null)
				throw new ArgumentNullException(nameof(artifactChoice));
			if (!member.IsSelected)
				throw new ArgumentException("Only selected collection members belong to a resolved member plan.", nameof(member));
			if (!member.IdentityResolution.IsResolved)
				throw new ArgumentException("A resolved member plan requires a stable member identity.", nameof(member));
			if (member.Artifact == null)
				throw new ArgumentException("A resolved member plan requires the manifest-requested artifact identity.", nameof(member));
			if (member.RecipeIdentity == null)
				throw new ArgumentException("A resolved member plan requires an exact recipe identity.", nameof(member));
			if (!member.Artifact.Equals(artifactChoice.RequestedArtifact))
				throw new ArgumentException("The artifact decision must reference the artifact requested by the normalized member.", nameof(artifactChoice));

			SourceOrdinal = member.SourceOrdinal;
			MemberKey = member.IdentityResolution.Key;
			Requirement = member.Requirement;
			ArtifactChoice = artifactChoice;
			RecipeIdentity = member.RecipeIdentity;
			DisplayName = member.DisplayName;
		}

		/// <summary>
		/// Gets the raw-source ordinal retained only for diagnostics.
		/// </summary>
		public int SourceOrdinal { get; }

		/// <summary>
		/// Gets the stable member identity.
		/// </summary>
		public CollectionMemberKey MemberKey { get; }

		/// <summary>
		/// Gets whether the source revision marks this selected member required or optional.
		/// </summary>
		public CollectionMemberRequirement Requirement { get; }

		/// <summary>
		/// Gets the exact requested/selected artifact decision.
		/// </summary>
		public CollectionResolvedArtifactChoice ArtifactChoice { get; }

		/// <summary>
		/// Gets the exact normalized recipe/options fingerprint for this selected member.
		/// </summary>
		public CollectionRecipeIdentity RecipeIdentity { get; }

		/// <summary>
		/// Gets optional decorative member text copied for plan diagnostics/preview.
		/// </summary>
		public string DisplayName { get; }
	}
}
