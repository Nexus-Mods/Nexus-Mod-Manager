using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Builds the candidate optional-member selection for a revision change. Existing installed participation is the
	/// default across revisions; explicit candidate-side UI decisions win. Exact member identity is preferred and the
	/// same conservative one-old/one-new Nexus game+mod correlation used by revision execution bridges file-id changes.
	/// </summary>
	internal sealed class CollectionRevisionUpdateCandidateSelectionBuilder
	{
		private readonly CollectionEffectiveSelectionBuilder _selectionBuilder = new CollectionEffectiveSelectionBuilder();

		internal CollectionEffectiveSelection Build(CollectionEffectiveSelection installedSelection,
			CollectionCapabilityReport candidateCapabilityReport,
			IEnumerable<CollectionOptionalMemberSelection> explicitCandidateSelections)
		{
			if (installedSelection == null) throw new ArgumentNullException(nameof(installedSelection));
			if (candidateCapabilityReport == null) throw new ArgumentNullException(nameof(candidateCapabilityReport));
			if (explicitCandidateSelections == null) throw new ArgumentNullException(nameof(explicitCandidateSelections));
			if (!installedSelection.Manifest.Revision.Collection.Equals(candidateCapabilityReport.Manifest.Revision.Collection))
				throw new ArgumentException("Revision candidate selection requires one Collection lineage.", nameof(candidateCapabilityReport));

			List<CollectionOptionalMemberSelection> explicitSelections = explicitCandidateSelections.ToList();
			Dictionary<CollectionMemberKey, NormalizedCollectionMember> candidateOptionalMembers = candidateCapabilityReport.Manifest.Members
				.Where(x => x != null && x.Requirement == CollectionMemberRequirement.Optional && x.IdentityResolution.IsResolved)
				.ToDictionary(x => x.IdentityResolution.Key);
			var explicitByKey = new Dictionary<CollectionMemberKey, CollectionOptionalMemberSelection>();
			foreach (CollectionOptionalMemberSelection selection in explicitSelections)
			{
				if (selection == null)
					throw new ArgumentException("A revision candidate selection cannot contain a null optional-member decision.", nameof(explicitCandidateSelections));
				if (!candidateOptionalMembers.ContainsKey(selection.MemberKey))
					throw new ArgumentException("An explicit revision candidate selection must reference an optional member in the candidate revision.", nameof(explicitCandidateSelections));
				if (explicitByKey.ContainsKey(selection.MemberKey))
					throw new ArgumentException("An optional candidate member cannot have more than one explicit revision decision.", nameof(explicitCandidateSelections));
				explicitByKey.Add(selection.MemberKey, selection);
			}

			Dictionary<CollectionMemberKey, NormalizedCollectionMember> installedByKey = installedSelection.Manifest.Members
				.Where(x => x != null && x.IdentityResolution.IsResolved).ToDictionary(x => x.IdentityResolution.Key);
			Dictionary<CollectionMemberKey, NormalizedCollectionMember> candidateByKey = candidateCapabilityReport.Manifest.Members
				.Where(x => x != null && x.IdentityResolution.IsResolved).ToDictionary(x => x.IdentityResolution.Key);
			Dictionary<CollectionMemberKey, CollectionMemberKey> correlatedOldByCandidate = BuildCorrelations(
				installedByKey.Values.Where(x => !candidateByKey.ContainsKey(x.IdentityResolution.Key)),
				candidateByKey.Values.Where(x => !installedByKey.ContainsKey(x.IdentityResolution.Key)));

			var decisions = new List<CollectionOptionalMemberSelection>();
			foreach (NormalizedCollectionMember candidate in candidateOptionalMembers.Values)
			{
				CollectionOptionalMemberSelection explicitSelection;
				if (explicitByKey.TryGetValue(candidate.IdentityResolution.Key, out explicitSelection))
				{
					decisions.Add(explicitSelection);
					continue;
				}

				NormalizedCollectionMember installed;
				if (!installedByKey.TryGetValue(candidate.IdentityResolution.Key, out installed))
				{
					CollectionMemberKey oldKey;
					if (!correlatedOldByCandidate.TryGetValue(candidate.IdentityResolution.Key, out oldKey) ||
						!installedByKey.TryGetValue(oldKey, out installed))
						continue;
				}

				CollectionMemberSelection inherited;
				if (TryGetInstalledParticipation(installed, out inherited))
					decisions.Add(new CollectionOptionalMemberSelection(candidate.IdentityResolution.Key, inherited));
			}

			return _selectionBuilder.Build(candidateCapabilityReport, decisions);
		}

		private static bool TryGetInstalledParticipation(NormalizedCollectionMember member, out CollectionMemberSelection selection)
		{
			if (member.Requirement == CollectionMemberRequirement.Required)
			{
				selection = CollectionMemberSelection.Selected;
				return true;
			}
			if (member.Selection == CollectionMemberSelection.Selected || member.Selection == CollectionMemberSelection.Unselected)
			{
				selection = member.Selection;
				return true;
			}
			selection = CollectionMemberSelection.Unknown;
			return false;
		}

		private static Dictionary<CollectionMemberKey, CollectionMemberKey> BuildCorrelations(
			IEnumerable<NormalizedCollectionMember> unmatchedInstalled, IEnumerable<NormalizedCollectionMember> unmatchedCandidate)
		{
			Dictionary<string, List<NormalizedCollectionMember>> installedGroups = GroupNexusMembers(unmatchedInstalled);
			Dictionary<string, List<NormalizedCollectionMember>> candidateGroups = GroupNexusMembers(unmatchedCandidate);
			var result = new Dictionary<CollectionMemberKey, CollectionMemberKey>();
			foreach (string identity in installedGroups.Keys.Intersect(candidateGroups.Keys, StringComparer.Ordinal))
			{
				List<NormalizedCollectionMember> installed = installedGroups[identity];
				List<NormalizedCollectionMember> candidate = candidateGroups[identity];
				if (installed.Count != 1 || candidate.Count != 1) continue;
				result.Add(candidate[0].IdentityResolution.Key, installed[0].IdentityResolution.Key);
			}
			return result;
		}

		private static Dictionary<string, List<NormalizedCollectionMember>> GroupNexusMembers(IEnumerable<NormalizedCollectionMember> members)
		{
			var result = new Dictionary<string, List<NormalizedCollectionMember>>(StringComparer.Ordinal);
			foreach (NormalizedCollectionMember member in members)
			{
				if (member == null || !member.IdentityResolution.IsResolved || member.IdentityResolution.Key.Kind != CollectionMemberKeyKind.ValidatedMatch)
					continue;
				string domain;
				long modId;
				long fileId;
				if (!NexusCollectionModFileArtifactIdentity.TryParse(member.Artifact, out domain, out modId, out fileId))
					continue;
				string identity = domain + "/" + modId.ToString(CultureInfo.InvariantCulture);
				List<NormalizedCollectionMember> group;
				if (!result.TryGetValue(identity, out group))
				{
					group = new List<NormalizedCollectionMember>();
					result.Add(identity, group);
				}
				group.Add(member);
			}
			return result;
		}
	}
}
