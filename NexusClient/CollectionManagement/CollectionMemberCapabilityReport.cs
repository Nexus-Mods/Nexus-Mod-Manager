using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable capability result for one normalized collection member.
	/// </summary>
	public sealed class CollectionMemberCapabilityReport
	{
		private readonly ReadOnlyCollection<CollectionCapabilityIssue> _issues;

		internal CollectionMemberCapabilityReport(NormalizedCollectionMember member, IEnumerable<CollectionCapabilityIssue> issues)
		{
			if (member == null)
				throw new ArgumentNullException(nameof(member));
			if (issues == null)
				throw new ArgumentNullException(nameof(issues));

			List<CollectionCapabilityIssue> copiedIssues = new List<CollectionCapabilityIssue>();
			CollectionCompatibilityStatus status = CollectionCompatibilityStatus.Supported;
			foreach (CollectionCapabilityIssue issue in issues)
			{
				if (issue == null)
					throw new ArgumentException("A capability report cannot contain a null issue.", nameof(issues));
				if (issue.Target != CollectionCapabilityIssueTarget.Member)
					throw new ArgumentException("A member capability report can contain only member issues.", nameof(issues));

				copiedIssues.Add(issue);
				status = CollectionCapabilityReport.CombineStatus(status, issue.Status);
			}

			Member = member;
			Status = status;
			_issues = new ReadOnlyCollection<CollectionCapabilityIssue>(copiedIssues);
		}

		/// <summary>
		/// Gets the normalized member being assessed.
		/// </summary>
		public NormalizedCollectionMember Member { get; }

		/// <summary>
		/// Gets the strongest capability result for this member independent of whether the member is selected.
		/// </summary>
		public CollectionCompatibilityStatus Status { get; }

		/// <summary>
		/// Gets the precise issues recorded for this member.
		/// </summary>
		public ReadOnlyCollection<CollectionCapabilityIssue> Issues
		{
			get { return _issues; }
		}

		/// <summary>
		/// Gets whether this member currently contributes its capability result to the selected set.
		/// </summary>
		public bool IsSelected
		{
			get { return Member.IsSelected; }
		}

		/// <summary>
		/// Gets whether the member is an unselected optional whose issues are informational for the current selection.
		/// </summary>
		public bool IsUnselectedOptional
		{
			get { return !Member.IsSelected && !Member.IsRequired; }
		}
	}
}
