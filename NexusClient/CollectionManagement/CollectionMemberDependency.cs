using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes the characterized dependency semantics between two Collection members.
	/// </summary>
	public enum CollectionMemberDependencyKind
	{
		Unknown = 0,
		InstallerPrerequisite = 1
	}

	/// <summary>
	/// Immutable typed prerequisite edge between two stable Collection member identities.
	/// </summary>
	/// <remarks>
	/// File-winner, plugin-load-order and acquisition ordering are separate concepts and must not be encoded as this edge.
	/// </remarks>
	public sealed class CollectionMemberDependency : IEquatable<CollectionMemberDependency>
	{
		/// <summary>
		/// Creates one prerequisite edge.
		/// </summary>
		public CollectionMemberDependency(CollectionMemberKey prerequisiteMemberKey, CollectionMemberKey dependentMemberKey, CollectionMemberDependencyKind kind)
		{
			if (prerequisiteMemberKey == null)
				throw new ArgumentNullException(nameof(prerequisiteMemberKey));
			if (dependentMemberKey == null)
				throw new ArgumentNullException(nameof(dependentMemberKey));
			if (prerequisiteMemberKey.Equals(dependentMemberKey))
				throw new ArgumentException("A Collection member cannot be its own prerequisite.", nameof(dependentMemberKey));
			if (!Enum.IsDefined(typeof(CollectionMemberDependencyKind), kind) || kind == CollectionMemberDependencyKind.Unknown)
				throw new ArgumentOutOfRangeException(nameof(kind));

			PrerequisiteMemberKey = prerequisiteMemberKey;
			DependentMemberKey = dependentMemberKey;
			Kind = kind;
		}

		public CollectionMemberKey PrerequisiteMemberKey { get; }
		public CollectionMemberKey DependentMemberKey { get; }
		public CollectionMemberDependencyKind Kind { get; }

		/// <inheritdoc />
		public bool Equals(CollectionMemberDependency other)
		{
			return !ReferenceEquals(other, null) &&
				Kind == other.Kind &&
				PrerequisiteMemberKey.Equals(other.PrerequisiteMemberKey) &&
				DependentMemberKey.Equals(other.DependentMemberKey);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionMemberDependency);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = (int)Kind;
				hash = (hash * 397) ^ PrerequisiteMemberKey.GetHashCode();
				hash = (hash * 397) ^ DependentMemberKey.GetHashCode();
				return hash;
			}
		}
	}
}
