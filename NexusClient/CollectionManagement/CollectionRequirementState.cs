using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether a tracked requirement/effect is absent or has a concrete normalized value.
	/// </summary>
	public enum CollectionRequirementStateKind
	{
		Unknown = 0,
		Absent = 1,
		Present = 2
	}

	/// <summary>
	/// Immutable normalized state used to compare curator baselines, deliberate user choices and observed reality.
	/// </summary>
	/// <remarks>
	/// Present-state values are opaque/versioned fingerprints produced by the owning adapter. This contract intentionally
	/// does not deserialize installer/plugin/configuration details or treat mutable display text as comparison identity.
	/// </remarks>
	public sealed class CollectionRequirementState : IEquatable<CollectionRequirementState>
	{
		private CollectionRequirementState(CollectionRequirementStateKind kind, string formatVersion, string fingerprint)
		{
			Kind = kind;
			FormatVersion = formatVersion;
			Fingerprint = fingerprint;
		}

		/// <summary>
		/// Gets whether the requirement/effect is absent or present with an exact normalized value.
		/// </summary>
		public CollectionRequirementStateKind Kind { get; }

		/// <summary>
		/// Gets the present-state fingerprint format/version, otherwise <c>null</c>.
		/// </summary>
		public string FormatVersion { get; }

		/// <summary>
		/// Gets the present-state opaque fingerprint, otherwise <c>null</c>.
		/// </summary>
		public string Fingerprint { get; }

		/// <summary>
		/// Creates an explicit absent state.
		/// </summary>
		public static CollectionRequirementState Absent()
		{
			return new CollectionRequirementState(CollectionRequirementStateKind.Absent, null, null);
		}

		/// <summary>
		/// Creates an exact normalized present state.
		/// </summary>
		public static CollectionRequirementState Present(string formatVersion, string fingerprint)
		{
			return new CollectionRequirementState(CollectionRequirementStateKind.Present,
				CollectionIdentityValidation.RequireOpaqueToken(formatVersion, nameof(formatVersion)),
				CollectionIdentityValidation.RequireOpaqueToken(fingerprint, nameof(fingerprint)));
		}

		/// <inheritdoc />
		public bool Equals(CollectionRequirementState other)
		{
			return !ReferenceEquals(other, null) &&
				Kind == other.Kind &&
				StringComparer.Ordinal.Equals(FormatVersion, other.FormatVersion) &&
				StringComparer.Ordinal.Equals(Fingerprint, other.Fingerprint);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionRequirementState);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = (int)Kind;
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(FormatVersion ?? string.Empty);
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Fingerprint ?? string.Empty);
				return hashCode;
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			if (Kind == CollectionRequirementStateKind.Absent)
				return "Absent";

			return FormatVersion + ":" + Fingerprint;
		}
	}
}
