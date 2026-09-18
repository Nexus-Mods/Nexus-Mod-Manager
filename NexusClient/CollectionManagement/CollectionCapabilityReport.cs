using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable compatibility/capability report for one normalized collection manifest.
	/// </summary>
	/// <remarks>
	/// The overall status is selection-aware. Whole-manifest issues always apply. Selected-member issues apply. Issues on
	/// unselected optional members remain visible but do not block the current selection. Required omissions remain an
	/// action-required deviation even when the omitted member itself has unsupported capabilities.
	/// </remarks>
	public sealed class CollectionCapabilityReport
	{
		private readonly ReadOnlyCollection<CollectionCapabilityIssue> _manifestIssues;
		private readonly ReadOnlyCollection<CollectionMemberCapabilityReport> _memberReports;
		private readonly ReadOnlyCollection<CollectionCapabilityIssue> _allIssues;

		private CollectionCapabilityReport(
			NormalizedCollectionManifest manifest,
			CollectionCompatibilityStatus status,
			IList<CollectionCapabilityIssue> manifestIssues,
			IList<CollectionMemberCapabilityReport> memberReports,
			IList<CollectionCapabilityIssue> allIssues)
		{
			Manifest = manifest;
			Status = status;
			_manifestIssues = new ReadOnlyCollection<CollectionCapabilityIssue>(new List<CollectionCapabilityIssue>(manifestIssues));
			_memberReports = new ReadOnlyCollection<CollectionMemberCapabilityReport>(new List<CollectionMemberCapabilityReport>(memberReports));
			_allIssues = new ReadOnlyCollection<CollectionCapabilityIssue>(new List<CollectionCapabilityIssue>(allIssues));
		}

		/// <summary>
		/// Gets the normalized manifest being assessed.
		/// </summary>
		public NormalizedCollectionManifest Manifest { get; }

		/// <summary>
		/// Gets the compatibility status for the current selected set.
		/// </summary>
		public CollectionCompatibilityStatus Status { get; }

		/// <summary>
		/// Gets whole-manifest issues that always affect the selected set.
		/// </summary>
		public ReadOnlyCollection<CollectionCapabilityIssue> ManifestIssues
		{
			get { return _manifestIssues; }
		}

		/// <summary>
		/// Gets per-member capability reports in normalized manifest presentation order.
		/// </summary>
		public ReadOnlyCollection<CollectionMemberCapabilityReport> MemberReports
		{
			get { return _memberReports; }
		}

		/// <summary>
		/// Gets every issue, including issues on currently unselected optional members.
		/// </summary>
		public ReadOnlyCollection<CollectionCapabilityIssue> AllIssues
		{
			get { return _allIssues; }
		}

		/// <summary>
		/// Gets whether the current selected set needs no further action and contains no unsupported behavior.
		/// </summary>
		public bool IsSupported
		{
			get { return Status == CollectionCompatibilityStatus.Supported; }
		}

		/// <summary>
		/// Gets whether at least one unselected optional member has unsupported capabilities that are ignored only because it is not selected.
		/// </summary>
		public bool HasUnselectedUnsupportedOptionals
		{
			get
			{
				foreach (CollectionMemberCapabilityReport memberReport in _memberReports)
				{
					if (memberReport.IsUnselectedOptional && memberReport.Status == CollectionCompatibilityStatus.Unsupported)
						return true;
				}

				return false;
			}
		}

		/// <summary>
		/// Builds a deterministic capability report from normalized input and explicit adapter/normalizer findings.
		/// </summary>
		/// <param name="manifest">The normalized manifest.</param>
		/// <param name="declaredIssues">Explicit capability findings such as unsupported recipe behavior or unsafe fields.</param>
		public static CollectionCapabilityReport Create(
			NormalizedCollectionManifest manifest,
			IEnumerable<CollectionCapabilityIssue> declaredIssues)
		{
			if (manifest == null)
				throw new ArgumentNullException(nameof(manifest));

			List<CollectionCapabilityIssue> issues = BuildIntrinsicIssues(manifest);
			if (declaredIssues != null)
			{
				foreach (CollectionCapabilityIssue issue in declaredIssues)
				{
					if (issue == null)
						throw new ArgumentException("A capability report cannot contain a null issue.", nameof(declaredIssues));
					ValidateDeclaredIssueTarget(manifest, issue);
					issues.Add(issue);
				}
			}

			issues.Sort(CompareIssues);

			List<CollectionCapabilityIssue> manifestIssues = new List<CollectionCapabilityIssue>();
			Dictionary<int, List<CollectionCapabilityIssue>> memberIssuesByOrdinal = new Dictionary<int, List<CollectionCapabilityIssue>>();
			foreach (NormalizedCollectionMember member in manifest.Members)
			{
				if (memberIssuesByOrdinal.ContainsKey(member.SourceOrdinal))
					throw new ArgumentException("Capability reporting requires unique member source ordinals within one normalized manifest.", nameof(manifest));

				memberIssuesByOrdinal.Add(member.SourceOrdinal, new List<CollectionCapabilityIssue>());
			}

			foreach (CollectionCapabilityIssue issue in issues)
			{
				if (issue.Target == CollectionCapabilityIssueTarget.Manifest)
					manifestIssues.Add(issue);
				else
					memberIssuesByOrdinal[issue.SourceOrdinal.Value].Add(issue);
			}

			CollectionCompatibilityStatus overall = CollectionCompatibilityStatus.Supported;
			foreach (CollectionCapabilityIssue issue in manifestIssues)
				overall = CombineStatus(overall, issue.Status);

			List<CollectionMemberCapabilityReport> memberReports = new List<CollectionMemberCapabilityReport>();
			foreach (NormalizedCollectionMember member in manifest.Members)
			{
				CollectionMemberCapabilityReport memberReport = new CollectionMemberCapabilityReport(
					member, memberIssuesByOrdinal[member.SourceOrdinal]);
				memberReports.Add(memberReport);

				if (member.IsSelected)
					overall = CombineStatus(overall, memberReport.Status);
				else if (member.IsRequired)
					overall = CombineStatus(overall, CollectionCompatibilityStatus.ActionRequired);
			}

			return new CollectionCapabilityReport(manifest, overall, manifestIssues, memberReports, issues);
		}

		/// <summary>
		/// Builds a report when the normalizer/adapter produced no additional capability findings.
		/// </summary>
		public static CollectionCapabilityReport Create(NormalizedCollectionManifest manifest)
		{
			return Create(manifest, null);
		}

		internal static CollectionCompatibilityStatus CombineStatus(
			CollectionCompatibilityStatus current,
			CollectionCompatibilityStatus candidate)
		{
			if (current == CollectionCompatibilityStatus.Unknown || candidate == CollectionCompatibilityStatus.Unknown)
				throw new ArgumentOutOfRangeException(nameof(candidate), "Unknown is not a valid completed compatibility status.");

			return (CollectionCompatibilityStatus)Math.Max((int)current, (int)candidate);
		}

		private static List<CollectionCapabilityIssue> BuildIntrinsicIssues(NormalizedCollectionManifest manifest)
		{
			List<CollectionCapabilityIssue> issues = new List<CollectionCapabilityIssue>();

			if (!manifest.IsMemberSetComplete)
			{
				issues.Add(CollectionCapabilityIssue.ForManifest(
					CollectionCompatibilityStatus.ActionRequired,
					"manifest.member-set-incomplete",
					manifest.IncompletenessReason));
			}

			foreach (NormalizedCollectionMember member in manifest.Members)
			{
				if (!member.IdentityResolution.IsResolved)
				{
					issues.Add(CollectionCapabilityIssue.ForMember(
						CollectionCompatibilityStatus.ActionRequired,
						member.IdentityResolution.Status == CollectionMemberIdentityResolutionStatus.Ambiguous
							? "member.identity-ambiguous"
							: "member.identity-missing",
						member.IdentityResolution.Reason,
						member,
						"identity"));
				}

				if (member.Artifact == null)
				{
					issues.Add(CollectionCapabilityIssue.ForMember(
						CollectionCompatibilityStatus.ActionRequired,
						"member.artifact-missing",
						"The normalized member does not have a stable artifact identity.",
						member,
						"artifact"));
				}

				if (member.RecipeIdentity == null)
				{
					issues.Add(CollectionCapabilityIssue.ForMember(
						CollectionCompatibilityStatus.ActionRequired,
						"member.recipe-identity-missing",
						"The normalized member does not have an established recipe identity.",
						member,
						"recipe"));
				}

				if (member.IsRequiredOmission)
				{
					issues.Add(CollectionCapabilityIssue.ForMember(
						CollectionCompatibilityStatus.ActionRequired,
						"member.required-omitted",
						"A required collection member is explicitly unselected, so the current selection is a deviation.",
						member,
						"selection"));
				}
			}

			return issues;
		}

		private static void ValidateDeclaredIssueTarget(NormalizedCollectionManifest manifest, CollectionCapabilityIssue issue)
		{
			if (issue.Target == CollectionCapabilityIssueTarget.Manifest)
				return;

			NormalizedCollectionMember matchedMember = null;
			foreach (NormalizedCollectionMember member in manifest.Members)
			{
				if (member.SourceOrdinal == issue.SourceOrdinal.Value)
				{
					matchedMember = member;
					break;
				}
			}

			if (matchedMember == null)
				throw new ArgumentException("A declared member capability issue references a source ordinal that is not present in the manifest.", nameof(issue));

			if (issue.MemberKey != null)
			{
				if (!matchedMember.IdentityResolution.IsResolved || !issue.MemberKey.Equals(matchedMember.IdentityResolution.Key))
					throw new ArgumentException("A declared member capability issue references a stable key that does not match its source ordinal.", nameof(issue));
			}
		}

		private static int CompareIssues(CollectionCapabilityIssue left, CollectionCapabilityIssue right)
		{
			int targetComparison = left.Target.CompareTo(right.Target);
			if (targetComparison != 0)
				return targetComparison;

			int leftOrdinal = left.SourceOrdinal ?? -1;
			int rightOrdinal = right.SourceOrdinal ?? -1;
			int ordinalComparison = leftOrdinal.CompareTo(rightOrdinal);
			if (ordinalComparison != 0)
				return ordinalComparison;

			int fieldComparison = StringComparer.Ordinal.Compare(left.FieldPath ?? string.Empty, right.FieldPath ?? string.Empty);
			if (fieldComparison != 0)
				return fieldComparison;

			int statusComparison = left.Status.CompareTo(right.Status);
			if (statusComparison != 0)
				return statusComparison;

			int codeComparison = StringComparer.Ordinal.Compare(left.Code, right.Code);
			if (codeComparison != 0)
				return codeComparison;

			return StringComparer.Ordinal.Compare(left.Reason, right.Reason);
		}
	}
}
