using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one collection member within one exact immutable revision for operation correlation.
	/// </summary>
	public sealed class CollectionOperationMemberReference : IEquatable<CollectionOperationMemberReference>
	{
		public CollectionOperationMemberReference(CollectionRevisionIdentity revision, CollectionMemberKey memberKey)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (memberKey == null)
				throw new ArgumentNullException(nameof(memberKey));

			Revision = revision;
			MemberKey = memberKey;
		}

		/// <summary>
		/// Gets the exact revision containing the member.
		/// </summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>
		/// Gets the member's stable revision-local identity.
		/// </summary>
		public CollectionMemberKey MemberKey { get; }

		/// <inheritdoc />
		public bool Equals(CollectionOperationMemberReference other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(Revision, other.Revision) &&
				Equals(MemberKey, other.MemberKey);
		}

		/// <inheritdoc />
		public override bool Equals(object obj)
		{
			return Equals(obj as CollectionOperationMemberReference);
		}

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (Revision.GetHashCode() * 397) ^ MemberKey.GetHashCode();
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return Revision + "/" + MemberKey;
		}
	}
}
