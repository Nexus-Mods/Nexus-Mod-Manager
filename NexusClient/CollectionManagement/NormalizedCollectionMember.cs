using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether a normalized collection member is required by the revision or optional.
	/// </summary>
	public enum CollectionMemberRequirement
	{
		Unknown = 0,
		Required = 1,
		Optional = 2
	}

	/// <summary>
	/// Records the concrete selection state captured by the normalized manifest.
	/// </summary>
	public enum CollectionMemberSelection
	{
		Unknown = 0,
		Selected = 1,
		Unselected = 2
	}

	/// <summary>
	/// Immutable normalized collection-member record independent from API pagination or UI ordering.
	/// </summary>
	public sealed class NormalizedCollectionMember
	{
		/// <summary>
		/// Creates one normalized member snapshot.
		/// </summary>
		/// <remarks>
		/// <paramref name="sourceOrdinal"/> is retained only for diagnostics against the raw source. It is never an identity.
		/// Missing artifact or recipe data is representable so a read-only preview can explain incomplete source data; later
		/// capability/preflight gates decide whether that member can participate in a trusted installation plan.
		/// </remarks>
		public NormalizedCollectionMember(
			int sourceOrdinal,
			CollectionMemberIdentityResolution identityResolution,
			CollectionMemberRequirement requirement,
			CollectionMemberSelection selection,
			CollectionArtifactReference artifact,
			CollectionRecipeIdentity recipeIdentity,
			string displayName)
		{
			if (sourceOrdinal < 0)
				throw new ArgumentOutOfRangeException(nameof(sourceOrdinal), "Source ordinal cannot be negative.");
			if (identityResolution == null)
				throw new ArgumentNullException(nameof(identityResolution));
			if (!Enum.IsDefined(typeof(CollectionMemberRequirement), requirement) || requirement == CollectionMemberRequirement.Unknown)
				throw new ArgumentOutOfRangeException(nameof(requirement));
			if (!Enum.IsDefined(typeof(CollectionMemberSelection), selection) || selection == CollectionMemberSelection.Unknown)
				throw new ArgumentOutOfRangeException(nameof(selection));

			SourceOrdinal = sourceOrdinal;
			IdentityResolution = identityResolution;
			Requirement = requirement;
			Selection = selection;
			Artifact = artifact;
			RecipeIdentity = recipeIdentity;
			DisplayName = CollectionDomainValidation.OptionalDisplayValue(displayName, nameof(displayName));
		}

		/// <summary>
		/// Gets the source position used only to correlate diagnostics with retained raw content.
		/// </summary>
		public int SourceOrdinal { get; }

		/// <summary>
		/// Gets the explicit stable-identity normalization result.
		/// </summary>
		public CollectionMemberIdentityResolution IdentityResolution { get; }

		/// <summary>
		/// Gets whether the revision requires or optionally offers this member.
		/// </summary>
		public CollectionMemberRequirement Requirement { get; }

		/// <summary>
		/// Gets the concrete selected/unselected state captured for this normalized revision.
		/// </summary>
		public CollectionMemberSelection Selection { get; }

		/// <summary>
		/// Gets the exact normalized source artifact reference, or null when the source data is incomplete.
		/// </summary>
		public CollectionArtifactReference Artifact { get; }

		/// <summary>
		/// Gets the exact normalized recipe fingerprint, or null when recipe identity has not been established.
		/// </summary>
		public CollectionRecipeIdentity RecipeIdentity { get; }

		/// <summary>
		/// Gets optional decorative member text. It is never used as member identity.
		/// </summary>
		public string DisplayName { get; }

		/// <summary>
		/// Gets whether the revision requires this member.
		/// </summary>
		public bool IsRequired
		{
			get { return Requirement == CollectionMemberRequirement.Required; }
		}

		/// <summary>
		/// Gets whether the member is concretely selected by this normalized revision snapshot.
		/// </summary>
		public bool IsSelected
		{
			get { return Selection == CollectionMemberSelection.Selected; }
		}

		/// <summary>
		/// Gets whether a required member has been explicitly omitted.
		/// </summary>
		/// <remarks>
		/// This is representable because omission is a deviation to report, not something the domain layer silently repairs.
		/// </remarks>
		public bool IsRequiredOmission
		{
			get { return IsRequired && !IsSelected; }
		}
	}
}
