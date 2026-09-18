using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether the normalizer established a trusted stable member identity.
	/// </summary>
	public enum CollectionMemberIdentityResolutionStatus
	{
		/// <summary>
		/// No valid resolution status has been recorded.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// A trusted stable member key is available.
		/// </summary>
		Resolved = 1,

		/// <summary>
		/// The source did not provide enough information to establish a stable member key.
		/// </summary>
		Missing = 2,

		/// <summary>
		/// More than one candidate matched and the normalizer cannot choose safely without review.
		/// </summary>
		Ambiguous = 3
	}

	/// <summary>
	/// Explicit result of collection-member identity normalization.
	/// </summary>
	public sealed class CollectionMemberIdentityResolution : IEquatable<CollectionMemberIdentityResolution>
	{
		private CollectionMemberIdentityResolution(CollectionMemberIdentityResolutionStatus status, CollectionMemberKey key, string reason)
		{
			Status = status;
			Key = key;
			Reason = reason;
		}

		/// <summary>
		/// Creates a successful trusted identity resolution.
		/// </summary>
		public static CollectionMemberIdentityResolution Resolved(CollectionMemberKey key)
		{
			if (key == null)
				throw new ArgumentNullException(nameof(key));
			if (key.Kind == CollectionMemberKeyKind.Unknown)
				throw new ArgumentException("A resolved collection member requires a trusted member key.", nameof(key));

			return new CollectionMemberIdentityResolution(CollectionMemberIdentityResolutionStatus.Resolved, key, null);
		}

		/// <summary>
		/// Creates a missing-identity result suitable for preview and later capability reporting.
		/// </summary>
		public static CollectionMemberIdentityResolution Missing(string reason)
		{
			return new CollectionMemberIdentityResolution(CollectionMemberIdentityResolutionStatus.Missing, null,
				CollectionDomainValidation.RequireDisplayValue(reason, nameof(reason)));
		}

		/// <summary>
		/// Creates an ambiguous-identity result that requires explicit review instead of a guessed key.
		/// </summary>
		public static CollectionMemberIdentityResolution Ambiguous(string reason)
		{
			return new CollectionMemberIdentityResolution(CollectionMemberIdentityResolutionStatus.Ambiguous, null,
				CollectionDomainValidation.RequireDisplayValue(reason, nameof(reason)));
		}

		/// <summary>
		/// Gets the identity-resolution outcome.
		/// </summary>
		public CollectionMemberIdentityResolutionStatus Status { get; }

		/// <summary>
		/// Gets the trusted member key when <see cref="Status"/> is <see cref="CollectionMemberIdentityResolutionStatus.Resolved"/>.
		/// </summary>
		public CollectionMemberKey Key { get; }

		/// <summary>
		/// Gets the precise reason for a missing or ambiguous resolution.
		/// </summary>
		public string Reason { get; }

		/// <summary>
		/// Gets whether the result contains a trusted stable member key.
		/// </summary>
		public bool IsResolved
		{
			get { return Status == CollectionMemberIdentityResolutionStatus.Resolved; }
		}

		/// <inheritdoc />
		public bool Equals(CollectionMemberIdentityResolution other)
		{
			return !ReferenceEquals(other, null) &&
				Status == other.Status &&
				Equals(Key, other.Key) &&
				StringComparer.Ordinal.Equals(Reason, other.Reason);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionMemberIdentityResolution);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = (int)Status;
				hashCode = (hashCode * 397) ^ (Key == null ? 0 : Key.GetHashCode());
				hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Reason ?? string.Empty);
				return hashCode;
			}
		}
	}
}
