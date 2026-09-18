using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes how a concrete artifact used by a resolved member plan relates to the artifact requested by the manifest.
	/// </summary>
	public enum CollectionResolvedArtifactChoiceKind
	{
		Unknown = 0,
		ExactRequestedArtifact = 1,
		SupportedSubstitution = 2
	}

	/// <summary>
	/// Immutable exact artifact decision for one selected collection member.
	/// </summary>
	/// <remarks>
	/// A supported substitution records a stable rule identifier rather than only human text. Authorization/download URLs are
	/// intentionally absent: this is durable plan identity, not an acquisition response.
	/// </remarks>
	public sealed class CollectionResolvedArtifactChoice : IEquatable<CollectionResolvedArtifactChoice>
	{
		private CollectionResolvedArtifactChoice(
			CollectionResolvedArtifactChoiceKind kind,
			CollectionArtifactReference requestedArtifact,
			CollectionArtifactReference selectedArtifact,
			string substitutionRuleId)
		{
			if (!Enum.IsDefined(typeof(CollectionResolvedArtifactChoiceKind), kind) || kind == CollectionResolvedArtifactChoiceKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));
			if (requestedArtifact == null)
				throw new ArgumentNullException(nameof(requestedArtifact));
			if (selectedArtifact == null)
				throw new ArgumentNullException(nameof(selectedArtifact));

			if (kind == CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact)
			{
				if (!requestedArtifact.Equals(selectedArtifact))
					throw new ArgumentException("An exact artifact choice must select the artifact requested by the normalized manifest.", nameof(selectedArtifact));
				if (substitutionRuleId != null)
					throw new ArgumentException("An exact artifact choice cannot carry a substitution rule.", nameof(substitutionRuleId));
			}
			else
			{
				if (requestedArtifact.Equals(selectedArtifact))
					throw new ArgumentException("A supported substitution must select a different concrete artifact.", nameof(selectedArtifact));
				substitutionRuleId = CollectionIdentityValidation.RequireOpaqueToken(substitutionRuleId, nameof(substitutionRuleId));
			}

			Kind = kind;
			RequestedArtifact = requestedArtifact;
			SelectedArtifact = selectedArtifact;
			SubstitutionRuleId = substitutionRuleId;
		}

		/// <summary>
		/// Creates a decision that uses the manifest-requested artifact exactly.
		/// </summary>
		public static CollectionResolvedArtifactChoice Exact(CollectionArtifactReference requestedArtifact)
		{
			return new CollectionResolvedArtifactChoice(
				CollectionResolvedArtifactChoiceKind.ExactRequestedArtifact,
				requestedArtifact,
				requestedArtifact,
				null);
		}

		/// <summary>
		/// Creates an explicitly supported substitution decision.
		/// </summary>
		public static CollectionResolvedArtifactChoice SupportedSubstitution(
			CollectionArtifactReference requestedArtifact,
			CollectionArtifactReference selectedArtifact,
			string substitutionRuleId)
		{
			return new CollectionResolvedArtifactChoice(
				CollectionResolvedArtifactChoiceKind.SupportedSubstitution,
				requestedArtifact,
				selectedArtifact,
				substitutionRuleId);
		}

		public CollectionResolvedArtifactChoiceKind Kind { get; }
		public CollectionArtifactReference RequestedArtifact { get; }
		public CollectionArtifactReference SelectedArtifact { get; }
		public string SubstitutionRuleId { get; }

		public bool IsSubstitution
		{
			get { return Kind == CollectionResolvedArtifactChoiceKind.SupportedSubstitution; }
		}

		/// <inheritdoc />
		public bool Equals(CollectionResolvedArtifactChoice other)
		{
			return !ReferenceEquals(other, null) &&
				Kind == other.Kind &&
				Equals(RequestedArtifact, other.RequestedArtifact) &&
				Equals(SelectedArtifact, other.SelectedArtifact) &&
				StringComparer.Ordinal.Equals(SubstitutionRuleId, other.SubstitutionRuleId);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionResolvedArtifactChoice);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = (int)Kind;
				hashCode = (hashCode * 397) ^ (RequestedArtifact == null ? 0 : RequestedArtifact.GetHashCode());
				hashCode = (hashCode * 397) ^ (SelectedArtifact == null ? 0 : SelectedArtifact.GetHashCode());
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(SubstitutionRuleId ?? string.Empty);
				return hashCode;
			}
		}
	}
}
