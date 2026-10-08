using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Nexus.Client.CollectionManagement
{
	/// <summary>
	/// Deterministic revision-only correlation for members whose provider key changes because an exact Nexus file changes.
	/// Exact member keys remain authoritative everywhere else; this bridge exists only to relate the old and candidate
	/// sides of one reviewed revision transition without weakening CollectionMemberKey semantics.
	/// </summary>
	internal sealed class CollectionRevisionUpdateMemberCorrelationMap
	{
		private readonly ReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey> _oldByNew;
		private readonly ReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey> _newByOld;

		private CollectionRevisionUpdateMemberCorrelationMap(
			IDictionary<CollectionMemberKey, CollectionMemberKey> oldByNew,
			IDictionary<CollectionMemberKey, CollectionMemberKey> newByOld)
		{
			_oldByNew = new ReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey>(
				new Dictionary<CollectionMemberKey, CollectionMemberKey>(oldByNew));
			_newByOld = new ReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey>(
				new Dictionary<CollectionMemberKey, CollectionMemberKey>(newByOld));
		}

		public IReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey> OldByNew { get { return _oldByNew; } }
		public IReadOnlyDictionary<CollectionMemberKey, CollectionMemberKey> NewByOld { get { return _newByOld; } }

		public bool TryGetOldMemberKey(CollectionMemberKey newMemberKey, out CollectionMemberKey oldMemberKey)
		{
			if (newMemberKey == null) throw new ArgumentNullException(nameof(newMemberKey));
			return _oldByNew.TryGetValue(newMemberKey, out oldMemberKey);
		}

		public bool TryGetNewMemberKey(CollectionMemberKey oldMemberKey, out CollectionMemberKey newMemberKey)
		{
			if (oldMemberKey == null) throw new ArgumentNullException(nameof(oldMemberKey));
			return _newByOld.TryGetValue(oldMemberKey, out newMemberKey);
		}

		/// <summary>Maps a reviewed old-side member identity onto its unique candidate-side identity when a Nexus file changed.</summary>
		internal CollectionMemberKey ResolveCandidateMemberKey(CollectionMemberKey reviewedMemberKey)
		{
			if (reviewedMemberKey == null) throw new ArgumentNullException(nameof(reviewedMemberKey));
			CollectionMemberKey candidateMemberKey;
			return _newByOld.TryGetValue(reviewedMemberKey, out candidateMemberKey) ? candidateMemberKey : reviewedMemberKey;
		}

		public static CollectionRevisionUpdateMemberCorrelationMap Build(ResolvedCollectionPlan oldPlan, ResolvedCollectionPlan newPlan)
		{
			if (oldPlan == null) throw new ArgumentNullException(nameof(oldPlan));
			if (newPlan == null) throw new ArgumentNullException(nameof(newPlan));
			if (!oldPlan.Revision.Collection.Equals(newPlan.Revision.Collection))
				throw new ArgumentException("Revision member correlation requires one Collection lineage.");

			var oldByKey = oldPlan.SelectedMembers.ToDictionary(x => x.MemberKey);
			var newByKey = newPlan.SelectedMembers.ToDictionary(x => x.MemberKey);
			List<ResolvedCollectionMemberPlan> unmatchedOld = oldPlan.SelectedMembers.Where(x => !newByKey.ContainsKey(x.MemberKey)).ToList();
			List<ResolvedCollectionMemberPlan> unmatchedNew = newPlan.SelectedMembers.Where(x => !oldByKey.ContainsKey(x.MemberKey)).ToList();

			Dictionary<string, List<ResolvedCollectionMemberPlan>> oldGroups = GroupNexusMembers(unmatchedOld);
			Dictionary<string, List<ResolvedCollectionMemberPlan>> newGroups = GroupNexusMembers(unmatchedNew);
			var oldByNew = new Dictionary<CollectionMemberKey, CollectionMemberKey>();
			var newByOld = new Dictionary<CollectionMemberKey, CollectionMemberKey>();

			foreach (string identity in oldGroups.Keys.Intersect(newGroups.Keys, StringComparer.Ordinal))
			{
				List<ResolvedCollectionMemberPlan> oldCandidates = oldGroups[identity];
				List<ResolvedCollectionMemberPlan> newCandidates = newGroups[identity];
				// A single old and single new selected Nexus file for the same game/mod is the only correlation
				// we can prove without inventing provider identity. Multi-file cases remain deliberately unpaired.
				if (oldCandidates.Count != 1 || newCandidates.Count != 1) continue;
				CollectionMemberKey oldKey = oldCandidates[0].MemberKey;
				CollectionMemberKey newKey = newCandidates[0].MemberKey;
				oldByNew.Add(newKey, oldKey);
				newByOld.Add(oldKey, newKey);
			}

			return new CollectionRevisionUpdateMemberCorrelationMap(oldByNew, newByOld);
		}

		private static Dictionary<string, List<ResolvedCollectionMemberPlan>> GroupNexusMembers(IEnumerable<ResolvedCollectionMemberPlan> members)
		{
			var result = new Dictionary<string, List<ResolvedCollectionMemberPlan>>(StringComparer.Ordinal);
			foreach (ResolvedCollectionMemberPlan member in members)
			{
				// Provider-stable/local keys are stronger identities and must never be reinterpreted by artifact similarity.
				if (member.MemberKey.Kind != CollectionMemberKeyKind.ValidatedMatch) continue;
				string domain;
				long modId;
				long fileId;
				if (!NexusCollectionModFileArtifactIdentity.TryParse(member.ArtifactChoice.SelectedArtifact, out domain, out modId, out fileId))
					continue;
				string identity = domain + "/" + modId.ToString(CultureInfo.InvariantCulture);
				List<ResolvedCollectionMemberPlan> values;
				if (!result.TryGetValue(identity, out values))
				{
					values = new List<ResolvedCollectionMemberPlan>();
					result.Add(identity, values);
				}
				values.Add(member);
			}
			return result;
		}
	}
}
