using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes how a stable member key was established.
	/// </summary>
	public enum CollectionMemberKeyKind
	{
		/// <summary>
		/// No trusted member identity has been established.
		/// </summary>
		Unknown = 0,

		/// <summary>
		/// The source manifest/provider supplied a stable member identifier.
		/// </summary>
		ProviderStable = 1,

		/// <summary>
		/// NMM assigned the member identity in a Local Collection.
		/// </summary>
		Local = 2,

		/// <summary>
		/// The normalizer established an explicit, validated stable match for a source that lacked a member identifier.
		/// </summary>
		ValidatedMatch = 3
	}

	/// <summary>
	/// Identifies one collection member independently from pagination order, UI sort order or archive display name.
	/// </summary>
	public sealed class CollectionMemberKey : IEquatable<CollectionMemberKey>
	{
		private CollectionMemberKey(CollectionMemberKeyKind kind, string value)
		{
			Kind = kind;
			Value = value;
		}

		/// <summary>
		/// Gets how this member identity was established.
		/// </summary>
		public CollectionMemberKeyKind Kind { get; }

		/// <summary>
		/// Gets the stable key value within <see cref="Kind"/>.
		/// </summary>
		public string Value { get; }

		/// <summary>
		/// Creates a key from a source/provider supplied stable member identifier.
		/// </summary>
		public static CollectionMemberKey FromProvider(string stableMemberId)
		{
			return new CollectionMemberKey(CollectionMemberKeyKind.ProviderStable,
				CollectionIdentityValidation.RequireOpaqueToken(stableMemberId, nameof(stableMemberId)));
		}

		/// <summary>
		/// Creates a key for a member owned by a Local Collection.
		/// </summary>
		public static CollectionMemberKey FromLocal(Guid localMemberId)
		{
			return new CollectionMemberKey(CollectionMemberKeyKind.Local,
				CollectionIdentityValidation.RequireGuid(localMemberId, nameof(localMemberId)));
		}

		/// <summary>
		/// Creates a key from an explicit validated matching result for a source without stable member IDs.
		/// </summary>
		/// <remarks>
		/// The manifest normalizer is responsible for proving that the match is unambiguous. This factory must not be
		/// fed a list index, display name or archive filename merely because they happen to be unique in one response.
		/// </remarks>
		public static CollectionMemberKey FromValidatedMatch(string stableMatchKey)
		{
			return new CollectionMemberKey(CollectionMemberKeyKind.ValidatedMatch,
				CollectionIdentityValidation.RequireOpaqueToken(stableMatchKey, nameof(stableMatchKey)));
		}

		/// <inheritdoc />
		public bool Equals(CollectionMemberKey other)
		{
			return !ReferenceEquals(other, null) &&
				Kind == other.Kind &&
				StringComparer.Ordinal.Equals(Value, other.Value);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionMemberKey);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Kind + ":" + Value;
		}
	}
}
