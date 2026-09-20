using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Immutable collection-plan contract containing concrete selected inputs, decisions, policy and target/state fingerprints.
	/// </summary>
	/// <remarks>
	/// This object is deliberately not executable. It contains no background tasks, native installer handles, signed URLs or
	/// deployment mutations. C5/C6 consume it only after target/state revalidation and the remaining mutation safety gates.
	/// </remarks>
	public sealed class ResolvedCollectionPlan
	{
		private readonly ReadOnlyCollection<ResolvedCollectionMemberPlan> _selectedMembers;

		/// <summary>
		/// Creates an immutable resolved plan from a supported normalized selection.
		/// </summary>
		public ResolvedCollectionPlan(
			CollectionPlanIdentity identity,
			CollectionTargetIdentity target,
			CollectionExecutionPolicy policy,
			CollectionCurrentStateFingerprint currentStateFingerprint,
			CollectionCapabilityReport capabilityReport,
			IEnumerable<ResolvedCollectionMemberPlan> selectedMembers)
		{
			if (identity == null)
				throw new ArgumentNullException(nameof(identity));
			if (target == null)
				throw new ArgumentNullException(nameof(target));
			if (policy == null)
				throw new ArgumentNullException(nameof(policy));
			if (currentStateFingerprint == null)
				throw new ArgumentNullException(nameof(currentStateFingerprint));
			if (capabilityReport == null)
				throw new ArgumentNullException(nameof(capabilityReport));
			if (selectedMembers == null)
				throw new ArgumentNullException(nameof(selectedMembers));
			if (capabilityReport.Status != CollectionCompatibilityStatus.Supported)
				throw new ArgumentException("A resolved collection plan requires a supported current selection.", nameof(capabilityReport));
			if (!capabilityReport.Manifest.IsMemberSetComplete)
				throw new ArgumentException("A resolved collection plan requires a complete normalized member set.", nameof(capabilityReport));

			Dictionary<CollectionMemberKey, NormalizedCollectionMember> expectedSelected =
				new Dictionary<CollectionMemberKey, NormalizedCollectionMember>();
			foreach (NormalizedCollectionMember member in capabilityReport.Manifest.Members)
			{
				if (!member.IsSelected)
					continue;
				if (!member.IdentityResolution.IsResolved || member.Artifact == null || member.RecipeIdentity == null)
					throw new ArgumentException("Supported selected members must have stable identity, artifact and recipe data.", nameof(capabilityReport));

				expectedSelected.Add(member.IdentityResolution.Key, member);
			}

			List<ResolvedCollectionMemberPlan> copied = new List<ResolvedCollectionMemberPlan>();
			HashSet<CollectionMemberKey> actualKeys = new HashSet<CollectionMemberKey>();
			foreach (ResolvedCollectionMemberPlan memberPlan in selectedMembers)
			{
				if (memberPlan == null)
					throw new ArgumentException("A resolved collection plan cannot contain a null member plan.", nameof(selectedMembers));
				if (!actualKeys.Add(memberPlan.MemberKey))
					throw new ArgumentException("A resolved collection plan cannot contain duplicate member keys.", nameof(selectedMembers));

				NormalizedCollectionMember expected;
				if (!expectedSelected.TryGetValue(memberPlan.MemberKey, out expected))
					throw new ArgumentException("A resolved member plan must belong to the manifest's current selected closure.", nameof(selectedMembers));
				ValidateMemberDecision(expected, memberPlan, nameof(selectedMembers));
				copied.Add(memberPlan);
			}

			if (actualKeys.Count != expectedSelected.Count)
				throw new ArgumentException("The resolved member plans must cover the complete selected member closure.", nameof(selectedMembers));

			// Canonical storage order is identity-based only. Dependency/phase execution order is established later and must not
			// accidentally inherit API pagination, download completion or UI presentation order.
			copied.Sort(CompareMemberPlans);

			Identity = identity;
			Target = target;
			Policy = policy;
			CurrentStateFingerprint = currentStateFingerprint;
			CapabilityReport = capabilityReport;
			_selectedMembers = new ReadOnlyCollection<ResolvedCollectionMemberPlan>(copied);
		}

		public CollectionPlanIdentity Identity { get; }
		public CollectionTargetIdentity Target { get; }
		public CollectionExecutionPolicy Policy { get; }
		public CollectionCurrentStateFingerprint CurrentStateFingerprint { get; }
		public CollectionCapabilityReport CapabilityReport { get; }

		/// <summary>
		/// Gets the exact revision represented by this plan.
		/// </summary>
		public CollectionRevisionIdentity Revision
		{
			get { return CapabilityReport.Manifest.Revision; }
		}

		/// <summary>
		/// Gets the exact raw-manifest/schema/normalizer provenance that produced the plan.
		/// </summary>
		public CollectionManifestSourceSnapshot ManifestSource
		{
			get { return CapabilityReport.Manifest.Source; }
		}

		/// <summary>
		/// Gets the complete selected member closure in canonical identity order.
		/// </summary>
		/// <remarks>
		/// This order is not installation order. Later dependency/phase planning owns executable ordering.
		/// </remarks>
		public ReadOnlyCollection<ResolvedCollectionMemberPlan> SelectedMembers
		{
			get { return _selectedMembers; }
		}

		/// <summary>
		/// Gets whether all policy-level user decisions currently represented by C1 have been resolved.
		/// </summary>
		/// <remarks>
		/// True is not permission to mutate. Target authority, current-state revalidation, recovery inputs, detailed native
		/// preflight and consent still belong to later phases.
		/// </remarks>
		public bool HasResolvedPolicyDecisions
		{
			get { return Policy.HasResolvedReplacementBackupDecision; }
		}

		private static void ValidateMemberDecision(
			NormalizedCollectionMember expected,
			ResolvedCollectionMemberPlan actual,
			string parameterName)
		{
			if (actual.SourceOrdinal != expected.SourceOrdinal ||
				actual.Requirement != expected.Requirement ||
				actual.InstallationPhase != expected.InstallationPhase ||
				!Equals(actual.RecipeIdentity, expected.RecipeIdentity) ||
				!Equals(actual.ArtifactChoice.RequestedArtifact, expected.Artifact))
				throw new ArgumentException("A resolved member plan does not match the normalized selected member it identifies.", parameterName);
		}

		private static int CompareMemberPlans(ResolvedCollectionMemberPlan left, ResolvedCollectionMemberPlan right)
		{
			int kind = left.MemberKey.Kind.CompareTo(right.MemberKey.Kind);
			if (kind != 0)
				return kind;

			return StringComparer.Ordinal.Compare(left.MemberKey.Value, right.MemberKey.Value);
		}
	}
}
