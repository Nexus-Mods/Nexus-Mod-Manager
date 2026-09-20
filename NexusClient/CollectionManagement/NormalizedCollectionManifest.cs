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
		private readonly ReadOnlyCollection<CollectionMemberDependency> _dependencies;
		private readonly ReadOnlyCollection<CollectionFilePriorityRule> _filePriorityRules;

		/// <summary>
		/// Creates an immutable normalized collection manifest snapshot.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, null, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest snapshot with characterized member dependencies.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies)
			: this(revision, source, memberSetCompleteness, incompletenessReason, members, dependencies, null)
		{
		}

		/// <summary>
		/// Creates an immutable normalized collection manifest with characterized prerequisite and file-priority relationships.
		/// </summary>
		public NormalizedCollectionManifest(
			CollectionRevisionIdentity revision,
			CollectionManifestSourceSnapshot source,
			CollectionManifestMemberSetCompleteness memberSetCompleteness,
			string incompletenessReason,
			IEnumerable<NormalizedCollectionMember> members,
			IEnumerable<CollectionMemberDependency> dependencies,
			IEnumerable<CollectionFilePriorityRule> filePriorityRules)
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

			List<CollectionMemberDependency> copiedDependencies = new List<CollectionMemberDependency>();
			HashSet<CollectionMemberDependency> uniqueDependencies = new HashSet<CollectionMemberDependency>();
			if (dependencies != null)
			{
				foreach (CollectionMemberDependency dependency in dependencies)
				{
					if (dependency == null)
						throw new ArgumentException("A normalized manifest cannot contain a null dependency.", nameof(dependencies));
					if (!resolvedKeys.Contains(dependency.PrerequisiteMemberKey) || !resolvedKeys.Contains(dependency.DependentMemberKey))
						throw new ArgumentException("A normalized dependency must reference resolved members in the same manifest.", nameof(dependencies));
					if (!uniqueDependencies.Add(dependency))
						throw new ArgumentException("A normalized manifest cannot contain duplicate dependency edges.", nameof(dependencies));
					copiedDependencies.Add(dependency);
				}
			}

			List<CollectionFilePriorityRule> copiedFilePriorityRules = new List<CollectionFilePriorityRule>();
			HashSet<CollectionFilePriorityRule> uniqueFilePriorityRules = new HashSet<CollectionFilePriorityRule>();
			if (filePriorityRules != null)
			{
				foreach (CollectionFilePriorityRule rule in filePriorityRules)
				{
					if (rule == null)
						throw new ArgumentException("A normalized manifest cannot contain a null file-priority rule.", nameof(filePriorityRules));
					if (!resolvedKeys.Contains(rule.LowerPriorityMemberKey) || !resolvedKeys.Contains(rule.HigherPriorityMemberKey))
						throw new ArgumentException("A normalized file-priority rule must reference resolved members in the same manifest.", nameof(filePriorityRules));
					if (!uniqueFilePriorityRules.Add(rule))
						throw new ArgumentException("A normalized manifest cannot contain duplicate file-priority rules.", nameof(filePriorityRules));
					copiedFilePriorityRules.Add(rule);
				}
			}

			Revision = revision;
			Source = source;
			MemberSetCompleteness = memberSetCompleteness;
			IncompletenessReason = incompletenessReason;
			_members = new ReadOnlyCollection<NormalizedCollectionMember>(copiedMembers);
			_dependencies = new ReadOnlyCollection<CollectionMemberDependency>(copiedDependencies);
			_filePriorityRules = new ReadOnlyCollection<CollectionFilePriorityRule>(copiedFilePriorityRules);
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
		/// Gets characterized member-prerequisite edges. Uncharacterized raw rule systems are not silently translated here.
		/// </summary>
		public ReadOnlyCollection<CollectionMemberDependency> Dependencies
		{
			get { return _dependencies; }
		}

		/// <summary>
		/// Gets exact characterized lower-to-higher file-priority relationships between members.
		/// </summary>
		public ReadOnlyCollection<CollectionFilePriorityRule> FilePriorityRules
		{
			get { return _filePriorityRules; }
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
