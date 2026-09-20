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
}
