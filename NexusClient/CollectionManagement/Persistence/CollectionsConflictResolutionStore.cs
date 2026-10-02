using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Nexus.Client.ModManagement;

namespace Nexus.Client.CollectionManagement.Persistence
{
	/// <summary>Persists explicit C9 conflict decisions without changing native ownership.</summary>
	public sealed class CollectionsConflictResolutionStore
	{
		private readonly CollectionsStore _store;
		public CollectionsConflictResolutionStore(CollectionsStore store) { _store = store ?? throw new ArgumentNullException(nameof(store)); }

		public void Save(CollectionConflictResolutionDecision decision)
		{
			if (decision == null) throw new ArgumentNullException(nameof(decision));
			_store.ExecuteWrite((connection, transaction) =>
			{
				// One exact conflict may have only one durable resolution. This also lets a later C9 decision replace
				// the opposite choice without leaving two contradictory authorizations behind.
				using (SQLiteCommand delete = connection.CreateCommand())
				{
					delete.Transaction = transaction;
					delete.CommandText = @"DELETE FROM conflict_resolution_decisions
WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id AND target_fingerprint=@target
AND member_key_kind=@member_kind AND member_key_value=@member_value AND deployment_root=@root
AND relative_path=@path COLLATE NOCASE AND existing_owner_key=@owner;";
					BindScope(delete, decision);
					delete.ExecuteNonQuery();
				}
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"INSERT INTO conflict_resolution_decisions
(decision_id, origin, collection_id, revision_id, target_fingerprint, member_key_kind, member_key_value, deployment_root, relative_path, existing_owner_key, decision_kind, note)
VALUES (@decision_id,@origin,@collection_id,@revision_id,@target,@member_kind,@member_value,@root,@path,@owner,@kind,@note);";
					Bind(command, decision);
					command.ExecuteNonQuery();
				}
			});
		}

		public IReadOnlyList<CollectionConflictResolutionDecision> GetForRevisionTarget(CollectionRevisionIdentity revision, CollectionTargetIdentity target)
		{
			if (revision == null) throw new ArgumentNullException(nameof(revision));
			if (target == null) throw new ArgumentNullException(nameof(target));
			return _store.ExecuteRead((connection, transaction) =>
			{
				var result = new List<CollectionConflictResolutionDecision>();
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction;
					command.CommandText = @"SELECT decision_id, member_key_kind, member_key_value, deployment_root, relative_path, existing_owner_key, decision_kind, note
FROM conflict_resolution_decisions WHERE origin=@origin AND collection_id=@collection_id AND revision_id=@revision_id AND target_fingerprint=@target
ORDER BY deployment_root, relative_path, member_key_kind, member_key_value, existing_owner_key;";
					command.Parameters.AddWithValue("@origin", (int)revision.Collection.Origin);
					command.Parameters.AddWithValue("@collection_id", revision.Collection.StableId);
					command.Parameters.AddWithValue("@revision_id", revision.StableRevisionId);
					command.Parameters.AddWithValue("@target", target.Fingerprint);
					using (SQLiteDataReader reader = command.ExecuteReader())
						while (reader.Read())
							result.Add(Read(reader, revision, target));
				}
				return result;
			});
		}

		public void Delete(Guid decisionId)
		{
			if (decisionId == Guid.Empty) throw new ArgumentException("A non-empty conflict decision identifier is required.", nameof(decisionId));
			_store.ExecuteWrite((connection, transaction) =>
			{
				using (SQLiteCommand command = connection.CreateCommand())
				{
					command.Transaction = transaction; command.CommandText = "DELETE FROM conflict_resolution_decisions WHERE decision_id=@id;";
					command.Parameters.AddWithValue("@id", decisionId.ToString("D")); command.ExecuteNonQuery();
				}
			});
		}

		private static void Bind(SQLiteCommand command, CollectionConflictResolutionDecision decision)
		{
			command.Parameters.AddWithValue("@decision_id", decision.DecisionId.ToString("D"));
			BindScope(command, decision);
			command.Parameters.AddWithValue("@kind", (int)decision.Kind);
			command.Parameters.AddWithValue("@note", (object)decision.Note ?? DBNull.Value);
		}

		private static void BindScope(SQLiteCommand command, CollectionConflictResolutionDecision decision)
		{
			command.Parameters.AddWithValue("@origin", (int)decision.Revision.Collection.Origin);
			command.Parameters.AddWithValue("@collection_id", decision.Revision.Collection.StableId);
			command.Parameters.AddWithValue("@revision_id", decision.Revision.StableRevisionId);
			command.Parameters.AddWithValue("@target", decision.Target.Fingerprint);
			command.Parameters.AddWithValue("@member_kind", (int)decision.MemberKey.Kind);
			command.Parameters.AddWithValue("@member_value", decision.MemberKey.Value);
			command.Parameters.AddWithValue("@root", (int)decision.DeploymentRoot);
			command.Parameters.AddWithValue("@path", decision.RelativePath);
			command.Parameters.AddWithValue("@owner", decision.ExistingOwnerKey);
		}

		private static CollectionConflictResolutionDecision Read(SQLiteDataReader reader, CollectionRevisionIdentity revision, CollectionTargetIdentity target)
		{
			CollectionMemberKeyKind memberKind = (CollectionMemberKeyKind)Convert.ToInt32(reader["member_key_kind"]);
			string memberValue = Convert.ToString(reader["member_key_value"]);
			CollectionMemberKey member = memberKind == CollectionMemberKeyKind.ProviderStable ? CollectionMemberKey.FromProvider(memberValue) :
				memberKind == CollectionMemberKeyKind.Local ? CollectionMemberKey.FromLocal(Guid.Parse(memberValue)) : CollectionMemberKey.FromValidatedMatch(memberValue);
			return new CollectionConflictResolutionDecision(Guid.Parse(Convert.ToString(reader["decision_id"])), revision, target, member,
				(ModDeploymentRoot)Convert.ToInt32(reader["deployment_root"]), Convert.ToString(reader["relative_path"]),
				Convert.ToString(reader["existing_owner_key"]), (CollectionConflictResolutionDecisionKind)Convert.ToInt32(reader["decision_kind"]),
				reader["note"] == DBNull.Value ? null : Convert.ToString(reader["note"]));
		}
	}
}
