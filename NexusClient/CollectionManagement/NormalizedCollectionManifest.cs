using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Describes whether the normalizer observed the complete collection-member set.
	/// </summary>
	public enum CollectionManifestMemberSetCompleteness
	{
		Unknown = 0,
		Complete = 1,
		Incomplete = 2
	}

	/// <summary>
	/// Immutable normalized view of one exact collection revision manifest.
	/// </summary>
	/// <remarks>
	/// This model is intentionally not a native installation plan. It preserves incomplete or ambiguous source data for
	/// preview/reporting while preventing list position, display name or archive filename from becoming implicit identity.
	/// </remarks>
	public sealed class NormalizedCollectionManifest
	{
		private readonly ReadOnlyCollection<NormalizedCollectionMember> _members;

		/// <summary>
		/// Creates an immutable normalized collection manifest snapshot.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members)
		{
			if (revision == null)
				throw new ArgumentNullException(nameof(revision));
			if (source == null)
				throw new ArgumentNullException(nameof(source));
			if (!Enum.IsDefined(typeof(CollectionManifestMemberSetCompleteness), memberSetCompleteness) ||
				memberSetCompleteness == CollectionManifestMemberSetCompleteness.Unknown)
				throw new ArgumentOutOfRangeException(nameof(memberSetCompleteness));
			if (members == null)
				throw new ArgumentNullException(nameof(members));

			if (memberSetCompleteness == CollectionManifestMemberSetCompleteness.Complete)
			{
				if (incompletenessReason != null)
					throw new ArgumentException("A complete manifest cannot carry an incompleteness reason.", nameof(incompletenessReason));
			}
			else
			{
				incompletenessReason = CollectionDomainValidation.RequireDisplayValue(incompletenessReason, nameof(incompletenessReason));
			}

			List<NormalizedCollectionMember> copiedMembers = new List<NormalizedCollectionMember>();
			HashSet<CollectionMemberKey> resolvedKeys = new HashSet<CollectionMemberKey>();
			foreach (NormalizedCollectionMember member in members)
			{
				if (member == null)
					throw new ArgumentException("A normalized manifest cannot contain a null member.", nameof(members));

				if (member.IdentityResolution.IsResolved && !resolvedKeys.Add(member.IdentityResolution.Key))
					throw new ArgumentException("A normalized manifest cannot contain duplicate resolved member keys.", nameof(members));

				copiedMembers.Add(member);
			}

			Revision = revision;
			Source = source;
			MemberSetCompleteness = memberSetCompleteness;
			IncompletenessReason = incompletenessReason;
			_members = new ReadOnlyCollection<NormalizedCollectionMember>(copiedMembers);
		}

		/// <summary>
		/// Gets the exact revision represented by this normalized source snapshot.
		/// </summary>
		public CollectionRevisionIdentity Revision { get; }

		/// <summary>
		/// Gets the exact raw-content/schema/normalizer provenance for this normalized model.
		/// </summary>
		public CollectionManifestSourceSnapshot Source { get; }

		/// <summary>
		/// Gets whether the normalizer received the complete revision member set.
		/// </summary>
		public CollectionManifestMemberSetCompleteness MemberSetCompleteness { get; }

		/// <summary>
		/// Gets the precise reason the member set is incomplete, or null for a complete set.
		/// </summary>
		public string IncompletenessReason { get; }

		/// <summary>
		/// Gets the immutable normalized members in source presentation order.
		/// </summary>
		/// <remarks>
		/// Presentation order is preserved only for diagnostics/UI. Member identity comes from <see cref="CollectionMemberKey"/>.
		/// </remarks>
		public ReadOnlyCollection<NormalizedCollectionMember> Members
		{
			get { return _members; }
		}

		/// <summary>
		/// Gets whether the source member set is known to be complete.
		/// </summary>
		public bool IsMemberSetComplete
		{
			get { return MemberSetCompleteness == CollectionManifestMemberSetCompleteness.Complete; }
		}
	}
}
