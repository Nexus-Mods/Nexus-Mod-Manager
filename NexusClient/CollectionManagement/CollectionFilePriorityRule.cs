using System;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Identifies one exact Collection member file-priority relationship without conflating it with install order.
	/// </summary>
	/// <remarks>
	/// The lower-priority member must yield overlapping native deployment targets to the higher-priority member.
	/// This is planning intent only; native ownership stores remain authoritative and C6.4 performs no owner switch.
	/// </remarks>
	public sealed class CollectionFilePriorityRule : IEquatable<CollectionFilePriorityRule>
	{
		/// <summary>
		/// Creates one immutable lower-to-higher file-priority edge.
		/// </summary>
		public CollectionFilePriorityRule(CollectionMemberKey lowerPriorityMemberKey, CollectionMemberKey higherPriorityMemberKey)
		{
			LowerPriorityMemberKey = lowerPriorityMemberKey ?? throw new ArgumentNullException(nameof(lowerPriorityMemberKey));
			HigherPriorityMemberKey = higherPriorityMemberKey ?? throw new ArgumentNullException(nameof(higherPriorityMemberKey));
			if (LowerPriorityMemberKey.Equals(HigherPriorityMemberKey))
				throw new ArgumentException("A Collection member cannot have a file-priority rule against itself.", nameof(higherPriorityMemberKey));
		}

		/// <summary>Gets the member which must lose overlapping managed paths.</summary>
		public CollectionMemberKey LowerPriorityMemberKey { get; }

		/// <summary>Gets the member which must win overlapping managed paths.</summary>
		public CollectionMemberKey HigherPriorityMemberKey { get; }

		/// <inheritdoc />
		public bool Equals(CollectionFilePriorityRule other)
		{
			return !ReferenceEquals(other, null) &&
				Equals(LowerPriorityMemberKey, other.LowerPriorityMemberKey) &&
				Equals(HigherPriorityMemberKey, other.HigherPriorityMemberKey);
		}

		/// <inheritdoc />
		public override bool Equals(object obj) { return Equals(obj as CollectionFilePriorityRule); }

		/// <inheritdoc />
		public override int GetHashCode()
		{
			unchecked
			{
				return (LowerPriorityMemberKey.GetHashCode() * 397) ^ HigherPriorityMemberKey.GetHashCode();
			}
		}
	}
	/// <summary>
	/// Characterized before/after priority relationship between one Collection member and one portable external Vortex reference.
	/// </summary>
	/// <remarks>
	/// This record does not create a second mod-rule store. It exists so review can characterize the current effect of a
	/// Vortex rule whose other endpoint is outside the retained Collection member closure.
	/// </remarks>
	public sealed class CollectionExternalFilePriorityRule : IEquatable<CollectionExternalFilePriorityRule>
	{
		public CollectionExternalFilePriorityRule(CollectionMemberKey memberKey, CollectionConflictReference externalReference,
			bool memberIsLowerPriority)
		{
			MemberKey = memberKey ?? throw new ArgumentNullException(nameof(memberKey));
			ExternalReference = externalReference ?? throw new ArgumentNullException(nameof(externalReference));
			MemberIsLowerPriority = memberIsLowerPriority;
		}

		public CollectionMemberKey MemberKey { get; }
		public CollectionConflictReference ExternalReference { get; }
		public bool MemberIsLowerPriority { get; }

		public bool Equals(CollectionExternalFilePriorityRule other)
		{
			return other != null && MemberKey.Equals(other.MemberKey) &&
				ExternalReference.Equals(other.ExternalReference) && MemberIsLowerPriority == other.MemberIsLowerPriority;
		}
		public override bool Equals(object obj) { return Equals(obj as CollectionExternalFilePriorityRule); }
		public override int GetHashCode()
		{
			unchecked { return ((MemberKey.GetHashCode() * 397) ^ ExternalReference.GetHashCode()) * 397 ^ MemberIsLowerPriority.GetHashCode(); }
		}
	}

}
